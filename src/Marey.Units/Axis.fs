namespace Marey

/// §9 — a fully resolved axis: everything a backend needs to draw one, and nothing it has to
/// decide for itself.
///
/// "`x (fun r -> r.Time)` where `Time : float<s>` gives you a linear scale over seconds with
/// the axis labelled `t [s]`, with no configuration. That is the user-visible payoff of §2, and
/// it should be the first thing in the README."
type AxisSpec =
    { /// The finished title: `t [s]`, `x [nm]`, or bare `x` when the quantity is dimensionless.
      ///
      /// **Always plain text**, never markup. When the requested `UnitStyle` is `Latex` or
      /// `Siunitx` this holds the `Unicode` rendering and the requested one goes to `TitleTex`.
      /// See the note on `TitleTex` for why.
      Title: string
      /// The quantity name alone, as supplied by the caller.
      Label: string
      /// The unit alone, prefix included, in plain text. Empty when dimensionless.
      UnitText: string
      /// The finished title as TeX source, when the requested style was a TeX one.
      ///
      /// **Why the two TeX styles behave differently from the two plain ones.** `Unicode` and
      /// `Ascii` are *text*: `m·s⁻²` and `m.s^-2` are strings a viewer can display, so they
      /// belong in `Title` and there is nothing to put here. `Latex` and `Siunitx` are *markup*:
      /// `\qty{}{\metre\per\second\squared}` is meaningless to an SVG renderer, which would draw
      /// the backslashes literally. So for those two styles the plain `Unicode` rendering goes to
      /// `Title` — every backend can draw it — and the markup comes here, where a typesetting
      /// backend will find it via `TextRun.Tex`.
      ///
      /// The caller's `Label` is TeX-escaped when composing this, since it is arbitrary text
      /// sitting next to already-escaped unit markup. It is deliberately *not* wrapped in `$…$`:
      /// a single-symbol name like `t` would read better as math italic, but a multi-word name
      /// like `Time since launch` would lose its spaces in math mode, and guessing which is
      /// which from the string is not a judgement this layer can make correctly.
      TitleTex: string option
      /// The unit alone as TeX source, on the same rule as `TitleTex`. `None` for the plain
      /// styles. Exposed separately from `TitleTex` because a colorbar or a legend may need the
      /// unit without the quantity name.
      UnitTextTex: string option
      /// The prefix the data range selected. `Prefix.none` when no rescaling happened.
      Prefix: Prefix
      /// Tick values, positions and labels. **`Value` is the rescaled number that `Label`
      /// prints** — 1.0 for a tick at 1 nm, not 1e-9 — so that `Value` and `Label` can never
      /// disagree. `Position` is untouched normalized panel space: rescaling is a relabelling,
      /// it never moves a tick.
      Ticks: TickLabel[]
      /// D167 — the minor ticks' positions, in normalized panel space, none on a major tick.
      Minor: float<nd>[] }

/// D166 — where an axis's ticks go: chosen, or stated.
///
/// Values are in the axis's own numbers, the ones its data is written in (a nanometre axis over
/// metres takes `50e-9`, not `50`). They are stated in the data's unit and never in the prefix the
/// axis happens to pick, so a step cannot be right on one figure and a thousand times wrong on
/// the next. The prefix and the unit in the title are still chosen as for any axis.
type TickSpec =
    /// The 1-2-5 ladder at the tick target: what every axis did before M8.
    | TicksAuto
    /// Every multiple of `step` inside the domain — matplotlib's `MultipleLocator`.
    | TicksEvery of step: float
    /// These values, labelled as chosen ticks would be.
    | TicksAt of values: float[]
    /// These values, each with the label given — `X`, `Γ`, `X` under a wave-vector axis. The
    /// title keeps its unit; the collision ladder may tilt them and never drops one.
    | TicksNamed of ticks: (float * string)[]

/// D167 — where an axis's minor ticks go. They carry no labels and never take part in the
/// collision ladder.
type MinorSpec =
    /// Each major interval in 5 when the major step's mantissa is 1 or 5, in 4 otherwise
    /// (matplotlib's `AutoMinorLocator`), and 2 … 9 in each decade of a log axis. None between
    /// stated or named ticks, which have no interval to divide. Drawn where the theme says so.
    | MinorAuto
    | MinorOff
    /// Every multiple of `step`, in the axis's own numbers.
    | MinorEvery of step: float
    /// Each major interval in `count`.
    | MinorCount of count: int

/// D169 — a tick label written by the caller's own function, kept out of structural equality so
/// that a figure carrying one is still a value. It compares by reference, and it does not survive
/// a figure written to bytes, which is why the formats beside it are data.
[<Sealed>]
type TickFormatter(format: float -> string) =
    member _.Format(value: float) = format value
    override _.ToString() = "custom"

/// D169 — how tick numbers are written, after the prefix has rescaled them.
[<NoComparison>]
type TickFormat =
    /// The decimals the step needs: what every axis did before M8.
    | FormatAuto
    /// This many decimals.
    | FormatFixed of decimals: int
    /// Times a hundred, with a percent sign: a fraction written as a percentage.
    | FormatPercent
    /// A common power of ten taken into the title beside the unit, `x [10⁻³ m]`, and the ticks
    /// written as small numbers — matplotlib's offset text. The power replaces the prefix, so it
    /// is refused on an axis whose prefix is pinned to one.
    | FormatScientific
    /// The caller's function, of the number the label would show (after the prefix).
    | FormatCustom of TickFormatter

[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TickFormat =

    /// A format from a function of the number the label would show.
    let custom (format: float -> string) = FormatCustom(TickFormatter format)

/// §9 — the axis compiler. Turns a `Scale<'u>` into an `AxisSpec` with no configuration.
///
/// ### Why the input is `Scale<'u>` and not `IScaleErased`
///
/// `IScaleErased` is the right input for everything *below* the erasure boundary, and it is
/// deliberately the wrong input here, for two independent reasons.
///
/// 1. **It has already formatted the labels.** `Erase.scale` computes tick text with a decimal
///    count derived from the *unrescaled* step, so ticks at 1e-9 m come out as
///    `"0.000000001"`. §2.2 asks for `1, 2, 3, 4, 5`. Prefix selection has to happen before
///    formatting, so the axis compiler has to own the formatting, so it needs the raw
///    `TickHint`.
/// 2. **It has already formatted the tick labels for the wrong scale.** Prefix selection has to
///    happen before formatting, and `IScaleErased` hands over finished label strings, so the
///    erased path has to recompute them from `TickLabel.Value` regardless.
///
/// A second objection used to live here and no longer applies: `IScaleErased` once exposed
/// `Symbol` and `Dim` but not `ToSI`/`OffsetSI`, so it could not tell electronvolts from joules
/// (same `Dim`, 1.6e-19 apart) or °C from K (same `Dim`, affine), and an erased eV axis was
/// labelled `kJ`. D16 added both fields to `IScaleErased` — §15's reloaded-figure story means
/// the information would otherwise be *destroyed* at save time rather than merely hidden — and
/// `ofErasedWith` now routes its prefix and symbol decisions through the same
/// `Prefix.selectForErased`/`UnitDisplay.unitTextErased` the measured path uses.
///
/// `ofPacked` is the third door and the correct one whenever the packed value still exists:
/// §2.3's rank-2 encoding hands the measure back, so the full `Scale<'u>` path applies.
[<RequireQualifiedAccess>]
module Axis =

    /// The default tick target.
    ///
    /// Five, not six or ten. It is a *target*, not a count — `Ticks.linear` walks the 1-2-5
    /// ladder and returns whatever falls out — but the choice is load-bearing for §2.2's worked
    /// example: over 1e-9 … 5e-9 a target of 5 picks a step of 1 nm and yields exactly
    /// `1, 2, 3, 4, 5`, while a target of 6 picks 0.5 nm and yields `1.0, 1.5, … 5.0`. Both are
    /// legitimate axes; only one is the one the design asks for, and a sparser axis is the
    /// better publication default anyway.
    [<Literal>]
    let DefaultTickCount = 5

    /// `x` + `nm` becomes `x [nm]`; `x` + nothing stays `x`. The bracket convention is the one
    /// §9 uses, and dropping it for dimensionless quantities is what keeps a probability axis
    /// from being labelled `p [1]`.
    let title (label: string) (unitText: string) =
        if System.String.IsNullOrEmpty unitText then label
        elif System.String.IsNullOrEmpty label then "[" + unitText + "]"
        else label + " [" + unitText + "]"

    /// The plain-text style to pair with a requested style.
    ///
    /// `Latex` and `Siunitx` are markup, so their plain counterpart is `Unicode` — the richest
    /// rendering every backend can actually draw. `Unicode` and `Ascii` are already text and are
    /// their own counterpart. See `AxisSpec.TitleTex`.
    let private plainCounterpart (style: UnitStyle) =
        match style with
        | Latex
        | Siunitx -> Unicode
        | plain -> plain

    /// True when the style produces TeX markup rather than displayable text.
    let private isTexStyle (style: UnitStyle) =
        match style with
        | Latex
        | Siunitx -> true
        | Unicode
        | Ascii -> false

    /// The prefix an axis takes when nothing pins one: D19's choice from the domain, except on
    /// a wide log axis, which takes none.
    ///
    /// A prefix names one decade in three, and a wide log axis has many more. Chosen from the top
    /// of the domain it turned a joule axis over 10⁻¹⁸ … 10⁸ into megajoules, and moved every
    /// label twenty-four decades away from the number in the data. Powers of ten need no prefix:
    /// `10⁻¹⁸` reads in joules as well as anything would. A narrow log axis takes its prefix as
    /// a linear one does, which leaves its ticks in [10⁻³, 10³) (`Ticks.WideLogDecades`).
    let private prefixFor (kind: ScaleKind) (select: float * float -> Prefix) (domain: float * float) =
        match kind with
        | ScaleKind.Log _ when Ticks.isWideLog domain -> Prefix.none
        | _ -> select domain

    let private intText (n: int) =
        n.ToString(System.Globalization.CultureInfo.InvariantCulture)

    /// A log tick as a power of ten in `style`. `Unicode` is `Ticks.formatPower`'s `2×10⁻⁸`;
    /// `Ascii` spells it `2x10^-8`, the caret `UnitDisplay` uses for `m.s^-2`; both TeX styles
    /// set it in math, `$2\times10^{-8}$`, which needs no package.
    let private powerText (style: UnitStyle) (x: float) =
        match style, Ticks.powerOfTen x with
        | Unicode, _ -> Ticks.formatPower x
        | _, (0, _) -> "0"
        | Ascii, (m, e) -> (if m = 1 then "" else intText m + "x") + "10^" + intText e
        | (Latex | Siunitx), (m, e) -> "$" + (if m = 1 then "" else intText m + "\\times") + "10^{" + intText e + "}$"

    /// The ticks' labels, plain and TeX, *after* prefix rescaling, so that they are injective
    /// (§4) in the numbers the axis actually prints.
    ///
    /// A log axis's ticks are not on a uniform grid, so each takes its own decimals, or the axis
    /// is written in powers of ten — the rule is `Ticks.logInPowers`, applied here to the
    /// rescaled values. A linear axis takes the decimals its step needs. A categorical axis
    /// prints level names, read out of the kind through `Scale.levelLabel` — the same function
    /// `Erase.scale` uses, so the measured, packed and erased paths cannot label a category
    /// three different ways. It is not rescaled, so the value handed here is the level index:
    /// `Prefix.selectFor` refuses to rescale a dimensionless axis and an ordinal scale is
    /// dimensionless by construction (D45).
    ///
    /// Only a power of ten has a TeX form. Digits and a decimal point typeset identically in TeX
    /// and in plain text.
    let private tickLabels
        (style: UnitStyle)
        (kind: ScaleKind)
        (domain: float * float)
        (step: float)
        (values: float[])
        : (string * string option)[] =
        match kind with
        | ScaleKind.Ordinal levels -> values |> Array.map (fun v -> Scale.levelLabel levels v, None)
        | ScaleKind.Log _ when Ticks.logInPowers domain values ->
            values
            |> Array.map (fun v ->
                powerText (plainCounterpart style) v, (if isTexStyle style then Some(powerText style v) else None))
        | ScaleKind.Log _ -> values |> Array.map (fun v -> Ticks.formatOwn v, None)
        | _ ->
            let decimals = Ticks.decimalsFor step
            values |> Array.map (fun v -> Ticks.format decimals v, None)

    /// The most ticks a stated step may produce. A step given in the wrong prefix — 50 for 50 nm
    /// on a metre axis — would otherwise draw a solid bar of a hundred million of them.
    [<Literal>]
    let MaxStatedTicks = 50

    /// D166 — the stated values inside the domain, in order, and the step their labels need.
    let private statedTicks (ticks: TickSpec) (domain: float * float) (axisUnit: string) : float[] * float option =
        let lo, hi = min (fst domain) (snd domain), max (fst domain) (snd domain)
        let slack = 1e-9 * (hi - lo)
        let within (v: float) = v >= lo - slack && v <= hi + slack

        // The smallest gap between stated values sets the decimals their labels need.
        let minGap (sorted: float[]) =
            let gaps = sorted |> Array.pairwise |> Array.map (fun (a, b) -> b - a) |> Array.filter (fun g -> g > 0.0)
            if gaps.Length = 0 then None else Some(Array.min gaps)

        match ticks with
        | TicksAuto -> [||], None
        | TicksEvery step ->
            if not (step > 0.0) || System.Double.IsInfinity step then
                invalidArg "step" (sprintf "Marey: a tick step must be a positive finite number, and %g is not." step)

            let first = ceil (lo / step - 1e-9)
            let last = floor (hi / step + 1e-9)
            let count = int (last - first) + 1

            if count > MaxStatedTicks then
                failwithf
                    "Marey: a tick every %g on an axis over %g … %g%s would be %d ticks, more than %d. The step is in the axis's own numbers, the unit its data is in, not the prefix it is labelled in."
                    step
                    lo
                    hi
                    (if System.String.IsNullOrEmpty axisUnit then "" else " " + axisUnit)
                    count
                    MaxStatedTicks

            Array.init (max 0 count) (fun k -> (first + float k) * step), Some step
        | TicksAt values ->
            let kept = values |> Array.filter within |> Array.sort
            kept, minGap kept
        | TicksNamed named ->
            let kept = named |> Array.map fst |> Array.filter within |> Array.sort
            kept, minGap kept

    /// D166 — `ofScalePinnedWith` with the ticks stated. `TicksAuto` is that function exactly.
    /// The most minor ticks an axis draws. Past it a minor step is so fine against the domain that
    /// the ticks are a grey bar, and none are drawn.
    [<Literal>]
    let private MaxMinorTicks = 500

    /// D167 — the minor values inside the domain, none on a major one. `majors` are the major
    /// values and `step` the major step, when the majors are a regular step apart.
    let private minorValues (minor: MinorSpec) (kind: ScaleKind) (domain: float * float) (majors: float[]) (step: float option) : float[] =
        let lo, hi = min (fst domain) (snd domain), max (fst domain) (snd domain)
        let span = hi - lo
        let slack = 1e-9 * span

        let notMajor (v: float) (sub: float) =
            majors |> Array.forall (fun m -> abs (v - m) > 1e-6 * sub)

        let multiples (sub: float) =
            if not (sub > 0.0) || System.Double.IsInfinity sub then
                [||]
            else
                let first = ceil (lo / sub - 1e-9)
                let last = floor (hi / sub + 1e-9)
                let count = int (last - first) + 1

                if count <= 0 || count > MaxMinorTicks then
                    [||]
                else
                    Array.init count (fun k -> (first + float k) * sub) |> Array.filter (fun v -> notMajor v sub)

        // Matplotlib's rule: an interval whose step reads 1 or 5 is cut in five, any other in four.
        let divisions (major: float) =
            let mantissa = major / 10.0 ** floor (log10 major)
            let near (x: float) = abs (mantissa - x) < 1e-6
            if near 1.0 || near 5.0 || near 10.0 then 5 else 4

        match minor, kind, step with
        | MinorOff, _, _
        | _, ScaleKind.Ordinal _, _ -> [||]
        | MinorEvery sub, _, _ -> multiples sub
        | (MinorAuto | MinorCount _), ScaleKind.Log b, _ when b = 10.0 && lo > 0.0 ->
            // 2 … 9 in each decade, up to twelve decades.
            let d0 = floor (log10 lo)
            let d1 = ceil (log10 hi)

            if d1 - d0 > 12.0 then
                [||]
            else
                [| for d in d0..d1 do
                       for m in 2.0..9.0 do
                           let v = m * 10.0 ** d
                           if v >= lo - slack && v <= hi + slack && notMajor v v then
                               yield v |]
        | _, ScaleKind.Log _, _ -> [||]
        | MinorAuto, _, Some major -> multiples (major / float (divisions major))
        | MinorCount n, _, Some major when n > 1 -> multiples (major / float n)
        | _ -> [||]

    let ofScaleTicked<[<Measure>] 'u>
        (ticks: TickSpec)
        (minor: MinorSpec)
        (format: TickFormat)
        (pin: Prefix option)
        (style: UnitStyle)
        (registry: UnitRegistry)
        (tickCount: int)
        (label: string)
        (scale: Scale<'u>)
        : AxisSpec =
        let lo, hi = scale.Domain
        let domain = float lo, float hi

        let prefix =
            match pin, format with
            | Some p, FormatScientific when p <> Prefix.none ->
                failwithf
                    "Marey: the axis '%s' is pinned to the prefix %s and written in powers of ten. The two are one scaling of the same number; pin Prefix.none or use another format."
                    label
                    p.Name
            | Some p, _ -> p
            | None, FormatScientific -> Prefix.none
            | None, _ -> prefixFor scale.Kind (Prefix.selectForUnit scale.Unit) domain

        let factor = Prefix.factor prefix

        let raw, step =
            match ticks with
            | TicksAuto -> scale.TickHint tickCount, None
            | stated ->
                let values, step = statedTicks stated domain (UnitDisplay.unitText Unicode registry Prefix.none scale.Unit)
                values |> Array.map (fun v -> LanguagePrimitives.FloatWithMeasure<'u> v), step

        let rescaled = raw |> Array.map (fun v -> float v / factor)

        let labelStep = (defaultArg step (float (Ticks.linearStep tickCount scale.Domain))) / factor

        // D169 — the common power of ten a scientific axis takes into its title.
        let exponent =
            match format with
            | FormatScientific ->
                let largest = rescaled |> Array.fold (fun m v -> max m (abs v)) 0.0
                if largest > 0.0 then int (floor (log10 largest)) else 0
            | _ -> 0

        let labels =
            let step = labelStep

            let formatted =
                match format, scale.Kind with
                | FormatAuto, _
                | _, ScaleKind.Ordinal _ -> tickLabels style scale.Kind domain step rescaled
                | FormatFixed decimals, _ -> rescaled |> Array.map (fun v -> Ticks.format decimals v, None)
                | FormatPercent, _ ->
                    let decimals = Ticks.decimalsFor (step * 100.0)
                    rescaled |> Array.map (fun v -> Ticks.format decimals (v * 100.0) + "%", None)
                | FormatScientific, _ ->
                    let power = 10.0 ** float exponent
                    let decimals = Ticks.decimalsFor (step / power)
                    rescaled |> Array.map (fun v -> Ticks.format decimals (v / power), None)
                | FormatCustom f, _ -> rescaled |> Array.map (fun v -> f.Format v, None)

            match ticks with
            | TicksNamed named ->
                // A stated name replaces the number and has no TeX form of its own: it is the
                // caller's prose, escaped by a TeX backend as a title is.
                raw
                |> Array.mapi (fun i v ->
                    match named |> Array.tryFind (fun (value, _) -> value = float v) with
                    | Some(_, name) -> name, None
                    | None -> formatted.[i])
            | _ -> formatted

        let majors = raw |> Array.map float

        // The step the majors are apart: stated, or read off the chosen ladder. Stated or named
        // values have none, so `MinorAuto` divides nothing between them.
        let majorStep =
            match ticks, step with
            | TicksEvery _, Some s -> Some s
            | TicksAuto, _ when majors.Length >= 2 -> Some(abs (majors.[1] - majors.[0]))
            | TicksAuto, _ -> Some(float (Ticks.linearStep tickCount scale.Domain))
            | _ -> None

        let minorPositions =
            minorValues minor scale.Kind domain majors majorStep
            |> Array.map (fun v -> scale.Forward(LanguagePrimitives.FloatWithMeasure<'u> v))

        let ticks =
            Array.init raw.Length (fun i ->
                { Value = rescaled.[i]
                  Position = scale.Forward raw.[i]
                  Label = fst labels.[i]
                  LabelTex = snd labels.[i] })

        // A scientific axis's power stands in front of the unit, as a prefix would: `10⁻³·m`.
        let withPower (style: UnitStyle) (unit: string) =
            if exponent = 0 then
                unit
            else
                let power = powerText style (10.0 ** float exponent)

                if System.String.IsNullOrEmpty unit then power
                else
                    match style with
                    | Unicode -> power + "·" + unit
                    | Ascii -> power + "." + unit
                    | Latex
                    | Siunitx -> power + "\\," + unit

        let unitText =
            UnitDisplay.unitText (plainCounterpart style) registry prefix scale.Unit
            |> withPower (plainCounterpart style)

        let unitTextTex =
            if isTexStyle style then
                Some(UnitDisplay.unitText style registry prefix scale.Unit |> withPower style)
            else
                None

        { Title = title label unitText
          Label = label
          UnitText = unitText
          TitleTex = unitTextTex |> Option.map (title (Tex.escape label))
          UnitTextTex = unitTextTex
          Prefix = prefix
          Ticks = ticks
          Minor = minorPositions }

    /// The full form, with the prefix decided somewhere else — D73.
    ///
    /// `pin = None` is `ofScaleWith` exactly: the prefix comes from this scale's own domain.
    /// `Some p` uses `p` instead, and everything downstream of the choice is unchanged — the
    /// tick values are still rescaled to the prefix and the labels still formatted from the
    /// rescaled values, because rescaling is not the part being pinned.
    ///
    /// **Only an animation needs this.** Which decade a prefix is chosen from is a property of
    /// "the numbers" (D19), and across a shot the numbers are the union of every frame's domain
    /// rather than the instantaneous one. Without the pin a zoom crossing a decade steps
    /// `mm → µm` mid-shot: the axis *title* changes while the data is the thing supposed to be
    /// moving, and `10⁻¹⁸·m²` can appear and disappear between two frames.
    ///
    /// It is deliberately not a `Prefix` with a "none means auto" convention, because
    /// `Prefix.none` is a real prefix — an axis pinned to unprefixed metres is a thing a caller
    /// may want, and it must not read as "choose for me".
    let ofScalePinnedWith<[<Measure>] 'u>
        (pin: Prefix option)
        (style: UnitStyle)
        (registry: UnitRegistry)
        (tickCount: int)
        (label: string)
        (scale: Scale<'u>)
        : AxisSpec =
        ofScaleTicked TicksAuto MinorAuto FormatAuto pin style registry tickCount label scale

    /// The full form: choose the registry, the style, the tick count and the label.
    let ofScaleWith<[<Measure>] 'u>
        (style: UnitStyle)
        (registry: UnitRegistry)
        (tickCount: int)
        (label: string)
        (scale: Scale<'u>)
        : AxisSpec =
        ofScalePinnedWith None style registry tickCount label scale

    /// §9's payoff, in one call: `Axis.auto Unicode "x" xScale`.
    ///
    /// Registry is `UnitRegistry.si`, tick target is `DefaultTickCount`, prefix selection is
    /// automatic, and the tick *values* are rescaled to match the prefix — which is the whole
    /// point, and the half that a naive implementation forgets.
    let auto<[<Measure>] 'u> (style: UnitStyle) (label: string) (scale: Scale<'u>) : AxisSpec =
        ofScaleWith style UnitRegistry.si DefaultTickCount label scale

    /// Recover the measure from §2.3's rank-2 package and take the `Scale<'u>` path. This is
    /// the right way to build an axis from a heterogeneous `PackedSeries list`.
    let ofPackedWith (style: UnitStyle) (registry: UnitRegistry) (tickCount: int) (label: string) (packed: PackedScale) =
        packed.Accept
            { new IScaleConsumer<AxisSpec> with
                member _.Apply<[<Measure>] 'u>(s: Scale<'u>) =
                    ofScaleWith style registry tickCount label s }

    let ofPacked (style: UnitStyle) (label: string) (packed: PackedScale) =
        ofPackedWith style UnitRegistry.si DefaultTickCount label packed

    /// `ofPackedWith` with the prefix decided elsewhere — D73. See `ofScalePinnedWith`.
    let ofPackedPinnedWith
        (pin: Prefix option)
        (style: UnitStyle)
        (registry: UnitRegistry)
        (tickCount: int)
        (label: string)
        (packed: PackedScale)
        =
        packed.Accept
            { new IScaleConsumer<AxisSpec> with
                member _.Apply<[<Measure>] 'u>(s: Scale<'u>) =
                    ofScalePinnedWith pin style registry tickCount label s }

    /// D166 — `ofPackedPinnedWith` with the ticks stated.
    let ofPackedTicked
        (ticks: TickSpec)
        (minor: MinorSpec)
        (format: TickFormat)
        (pin: Prefix option)
        (style: UnitStyle)
        (registry: UnitRegistry)
        (tickCount: int)
        (label: string)
        (packed: PackedScale)
        =
        packed.Accept
            { new IScaleConsumer<AxisSpec> with
                member _.Apply<[<Measure>] 'u>(s: Scale<'u>) =
                    ofScaleTicked ticks minor format pin style registry tickCount label s }

    /// The domain a packed scale covers, as plain floats — the half of D73 that has to be
    /// unioned across an animation's frames before a prefix can be chosen for all of them.
    ///
    /// Plain floats rather than `float<'u>` because the caller is comparing *several* frames'
    /// domains, and the measure is the same on every one of them by construction: it is the
    /// same axis of the same figure at a different time. The measure is recovered where it
    /// matters, in `prefixForPacked`, which is the only thing that consumes this.
    let domainOfPacked (packed: PackedScale) : float * float =
        packed.Accept
            { new IScaleConsumer<float * float> with
                member _.Apply<[<Measure>] 'u>(s: Scale<'u>) =
                    let lo, hi = s.Domain
                    float lo, float hi }

    /// The prefix this axis *would* choose if its numbers were `domain` rather than its own —
    /// D73's other half. The unit comes from the scale, the range from the caller, and a log
    /// scale over a wide range takes none, as `ofScaleWith` would decide.
    let prefixForPacked (domain: float * float) (packed: PackedScale) : Prefix =
        packed.Accept
            { new IScaleConsumer<Prefix> with
                member _.Apply<[<Measure>] 'u>(s: Scale<'u>) = prefixFor s.Kind (Prefix.selectForUnit s.Unit) domain }

    /// The reloaded-figure path (§15).
    ///
    /// **This used to be correct for SI-coherent axes only**, because it selected a prefix from
    /// the `Dim` alone and rendered from the `Dim` alone. That made a reloaded electronvolt axis
    /// read `E [kJ]` — same `Dim` as joules, off by 1.6×10⁻¹⁹ — and a reloaded Celsius axis read
    /// `T [K]`, since °C and K also share a `Dim` and differ only by an offset.
    ///
    /// D16 put `ToSI` and `OffsetSI` on `IScaleErased` precisely so this path could be fixed:
    /// "without `ToSI`/`OffsetSI` the information is *destroyed* at save time, not merely hidden,
    /// and a reloaded non-SI axis can never be relabelled correctly." Both decisions now go
    /// through `Prefix.selectForErased` and `UnitDisplay.unitTextErased`, which are the same
    /// functions the measured path uses, so the two cannot drift apart again.
    ///
    /// `ofScaleWith` and `ofPackedWith` remain preferable whenever the measure still exists, for
    /// the other reason given in the module note: they own tick formatting, so they can rescale
    /// before formatting rather than after.
    ///
    /// Labels are recomputed from `TickLabel.Value` rather than reused, because the erased
    /// labels were formatted before any prefix was chosen.
    let ofErasedWith
        (style: UnitStyle)
        (registry: UnitRegistry)
        (tickCount: int)
        (label: string)
        (scale: IScaleErased)
        : AxisSpec =
        let prefix =
            prefixFor scale.Kind (Prefix.selectForErased scale.Symbol scale.Dim scale.ToSI scale.OffsetSI) scale.Domain

        let factor = Prefix.factor prefix
        let raw = scale.Ticks tickCount
        let rescaled = raw |> Array.map (fun t -> t.Value / factor)

        // `float = float<1>`, so the raw erased domain feeds the measure-generic tick helpers
        // unchanged.
        let labels =
            tickLabels style scale.Kind scale.Domain (Ticks.linearStep tickCount scale.Domain / factor) rescaled

        let ticks =
            Array.init raw.Length (fun i ->
                { Value = rescaled.[i]
                  Position = raw.[i].Position
                  Label = fst labels.[i]
                  LabelTex = snd labels.[i] })

        let unitTextIn s =
            UnitDisplay.unitTextErasedWith
                s
                registry
                prefix
                scale.Symbol
                scale.Factors
                scale.Dim
                scale.ToSI
                scale.OffsetSI

        let unitText = unitTextIn (plainCounterpart style)

        let unitTextTex =
            if isTexStyle style then Some(unitTextIn style) else None

        { Title = title label unitText
          Label = label
          UnitText = unitText
          TitleTex = unitTextTex |> Option.map (title (Tex.escape label))
          UnitTextTex = unitTextTex
          Prefix = prefix
          Ticks = ticks
          Minor = [||] }

    let ofErased (style: UnitStyle) (label: string) (scale: IScaleErased) =
        ofErasedWith style UnitRegistry.si DefaultTickCount label scale
