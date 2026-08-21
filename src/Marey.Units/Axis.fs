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
      Ticks: TickLabel[] }

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

    /// Decimals that make the labels injective (§4) *after* prefix rescaling.
    ///
    /// Log ticks are not on a uniform grid, so a single step-derived count is wrong for them;
    /// take the finest any individual tick needs. This mirrors `Erase.scale` exactly — the
    /// difference is only that it runs on the rescaled values.
    let private decimalsFor (kind: ScaleKind) (step: float) (values: float[]) =
        match kind with
        | ScaleKind.Log _ ->
            values
            |> Array.fold
                (fun acc v ->
                    let a = abs v
                    if a = 0.0 then acc else max acc (Ticks.decimalsFor a))
                0
        | _ -> Ticks.decimalsFor step

    /// How a tick value becomes text. Numeric for every continuous kind; a level name for
    /// `Ordinal`, read out of the kind through `Scale.levelLabel` — the same function
    /// `Erase.scale` uses, so the measured, packed and erased paths cannot label a category
    /// three different ways.
    ///
    /// A categorical axis is not rescaled, so the value handed here is the level index whatever
    /// path it arrived by: `Prefix.selectFor` refuses to rescale a dimensionless axis and an
    /// ordinal scale is dimensionless by construction (D45).
    let private labelFor (kind: ScaleKind) (decimals: int) =
        match kind with
        | ScaleKind.Ordinal levels -> Scale.levelLabel levels
        | _ -> Ticks.format decimals

    /// The full form: choose the registry, the style, the tick count and the label.
    let ofScaleWith<[<Measure>] 'u>
        (style: UnitStyle)
        (registry: UnitRegistry)
        (tickCount: int)
        (label: string)
        (scale: Scale<'u>)
        : AxisSpec =
        let lo, hi = scale.Domain
        let prefix = Prefix.selectForUnit scale.Unit (float lo, float hi)
        let factor = Prefix.factor prefix

        let raw = scale.TickHint tickCount
        let rescaled = raw |> Array.map (fun v -> float v / factor)

        let decimals =
            decimalsFor scale.Kind (Ticks.linearStep tickCount scale.Domain / factor) rescaled

        let tickText = labelFor scale.Kind decimals

        let ticks =
            Array.init raw.Length (fun i ->
                { Value = rescaled.[i]
                  Position = scale.Forward raw.[i]
                  Label = tickText rescaled.[i] })

        let unitText =
            UnitDisplay.unitText (plainCounterpart style) registry prefix scale.Unit

        let unitTextTex =
            if isTexStyle style then
                Some(UnitDisplay.unitText style registry prefix scale.Unit)
            else
                None

        { Title = title label unitText
          Label = label
          UnitText = unitText
          TitleTex = unitTextTex |> Option.map (title (Tex.escape label))
          UnitTextTex = unitTextTex
          Prefix = prefix
          Ticks = ticks }

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
            Prefix.selectForErased scale.Symbol scale.Dim scale.ToSI scale.OffsetSI scale.Domain

        let factor = Prefix.factor prefix
        let raw = scale.Ticks tickCount
        let rescaled = raw |> Array.map (fun t -> t.Value / factor)

        // `float = float<1>`, so the raw erased domain feeds the measure-generic tick helpers
        // unchanged.
        let decimals =
            decimalsFor scale.Kind (Ticks.linearStep tickCount scale.Domain / factor) rescaled

        let tickText = labelFor scale.Kind decimals

        let ticks =
            Array.init raw.Length (fun i ->
                { Value = rescaled.[i]
                  Position = raw.[i].Position
                  Label = tickText rescaled.[i] })

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
          Ticks = ticks }

    let ofErased (style: UnitStyle) (label: string) (scale: IScaleErased) =
        ofErasedWith style UnitRegistry.si DefaultTickCount label scale
