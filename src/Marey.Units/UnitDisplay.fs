namespace Marey

/// §2.2 — the four typographies the library can emit for a unit.
///
/// "The TikZ backend emits `\qty{}{\kilogram\metre\per\second\squared}` for siunitx; the SVG
/// backend emits `kg·m·s⁻²`. Same `Dim`, different renderer. Get this right once and every
/// backend inherits it."
type UnitStyle =
    /// `kg·m·s⁻²` — real U+00B7 middle dot, real superscript digits and U+207B minus. What the
    /// SVG, Skia and Canvas backends want.
    | Unicode
    /// `kg.m.s^-2` — 7-bit clean, for CSV headers, log lines, file names and terminals that
    /// are lying about their encoding.
    | Ascii
    /// `\mathrm{kg}\,\mathrm{m}\,\mathrm{s}^{-2}` — plain LaTeX math, no packages required.
    | Latex
    /// `\qty{}{\kilogram\metre\per\second\squared}` — siunitx macros, so the figure inherits
    /// the document's unit formatting. §6 calls this "a killer feature almost nobody offers",
    /// and it is why the registry stores a macro rather than a glyph.
    | Siunitx

/// §2.2 / §12.6 — unit rendering, driven **from `Dim`**.
///
/// The rule this module exists to enforce, quoting §12.6 in full: *"`mul force metre` produced
/// the symbol `kg·m/s/s·m` for what is `J`. The `Dim` was correct (`L=2, M=1, T=-2`); only the
/// string was wrong. Never compose display strings, always canonicalise from `Dim`."*
///
/// Every public entry point here therefore takes a `Dim` (or a witness, from which it takes the
/// `Dim`) and never a symbol — with exactly one documented exception, `renderSymbol`, for units
/// whose `Dim` cannot identify them. See `unitText`.
[<RequireQualifiedAccess>]
module UnitDisplay =

    let private invariant = System.Globalization.CultureInfo.InvariantCulture

    let private intText (n: int) = n.ToString(invariant)

    /// Unicode superscript for an integer exponent: `-2` becomes `⁻²`. See `Num.superscript`.
    let internal superscript (n: int) = Num.superscript n

    // ---- Spelling tables --------------------------------------------------------------

    /// The seven SI base symbols, keyed by the strings `Dim.basis` uses. Core owns the
    /// decomposition and its order; this module owns only the typography of the pieces.
    let private baseSymbol (sym: string) =
        match sym with
        | "kg" -> UnitSymbol.plain "kg" "\\kilogram"
        | "m" -> UnitSymbol.plain "m" "\\metre"
        | "s" -> UnitSymbol.plain "s" "\\second"
        | "A" -> UnitSymbol.plain "A" "\\ampere"
        | "K" -> UnitSymbol.plain "K" "\\kelvin"
        | "mol" -> UnitSymbol.plain "mol" "\\mole"
        | "cd" -> UnitSymbol.plain "cd" "\\candela"
        | other -> UnitSymbol.plain other ("\\text{" + other + "}")

    /// The gram. Not a base unit and not in `Dim.basis` — it exists only so a prefixed mass can
    /// be printed against it (`mg`, not `µkg`).
    let private gram = UnitSymbol.plain "g" "\\gram"

    /// Spellings for units that must be named by their *declared symbol* because their `Dim`
    /// cannot identify them — every non-SI unit, since eV and J share a dimension.
    ///
    /// Unknown symbols fall back to the literal text wrapped in `\text{…}` for siunitx, which
    /// typesets correctly even though it is not a unit macro.
    let private declaredSpellings =
        dict
            [ "eV", UnitSymbol.plain "eV" "\\electronvolt"
              "cal", UnitSymbol.plain "cal" "\\text{cal}"
              "Å", UnitSymbol.create "Å" "Ang" "\\AA" "\\angstrom"
              "au", UnitSymbol.plain "au" "\\astronomicalunit"
              "ly", UnitSymbol.plain "ly" "\\text{ly}"
              "pc", UnitSymbol.plain "pc" "\\text{pc}"
              "b", UnitSymbol.plain "b" "\\text{b}"
              "L", UnitSymbol.plain "L" "\\litre"
              "bar", UnitSymbol.plain "bar" "\\text{bar}"
              "atm", UnitSymbol.plain "atm" "\\text{atm}"
              "min", UnitSymbol.plain "min" "\\minute"
              "h", UnitSymbol.plain "h" "\\hour"
              "d", UnitSymbol.plain "d" "\\day"
              "yr", UnitSymbol.plain "yr" "\\text{yr}"
              "Da", UnitSymbol.plain "Da" "\\dalton"
              "G", UnitSymbol.plain "G" "\\text{G}"
              "g", gram
              "rad", UnitSymbol.plain "rad" "\\radian"
              "sr", UnitSymbol.plain "sr" "\\steradian"
              "°C", UnitSymbol.create "°C" "degC" "^\\circ C" "\\degreeCelsius"
              "°F", UnitSymbol.create "°F" "degF" "^\\circ F" "\\text{\\degree F}" ]

    let private spellingOf (sym: string) =
        match declaredSpellings.TryGetValue sym with
        | true, v -> v
        | _ -> UnitSymbol.create sym sym sym ("\\text{" + sym + "}")

    /// The spelling for one factor of a composed unit (`U.Factors`).
    ///
    /// Wider than `baseSymbol`, which knows only the seven base units and would typeset the `V`
    /// of `V·m⁻¹` as `\text{V}` — valid TeX, but not siunitx, and the entire point of the
    /// siunitx path is that a physicist's unit macros are used. The registry already holds a
    /// `UnitSymbol` for every named derived unit; this reaches them by their own symbol rather
    /// than by `Dim`, which is the one lookup direction `UnitRegistry` does not offer.
    ///
    /// Order matters: registry first so `V` resolves to `\volt`, then the declared non-SI table,
    /// then the base units, then the `\text{…}` fallback that never fails.
    let private factorSpelling (registry: UnitRegistry) (sym: string) =
        let fromRegistry =
            registry.Entries
            |> Array.tryPick (fun (_, us) -> if us.Unicode = sym then Some us else None)

        match fromRegistry with
        | Some us -> us
        | None ->
            match declaredSpellings.TryGetValue sym with
            | true, v -> v
            | _ -> baseSymbol sym

    // ---- Pieces -----------------------------------------------------------------------

    let private symText style (us: UnitSymbol) =
        match style with
        | Unicode -> us.Unicode
        | Ascii -> us.Ascii
        | Latex -> "\\mathrm{" + us.LatexCore + "}"
        | Siunitx -> us.Siunitx

    let private prefixText style (p: Prefix) =
        match style with
        | Unicode -> p.Symbol
        | Ascii -> p.Ascii
        | Latex -> if p.Latex = "" then "" else "\\mathrm{" + p.Latex + "}"
        | Siunitx -> p.Siunitx

    /// siunitx spells exponents as words, and only positive ones: the sign is carried by
    /// `\per`. `\per\second\squared` is s⁻², which is what §2.2's worked example relies on.
    let private siunitxPower (e: int) =
        match e with
        | 1 -> ""
        | 2 -> "\\squared"
        | 3 -> "\\cubed"
        | n -> "\\tothe{" + intText n + "}"

    let private factorText style (us: UnitSymbol) (e: int) =
        let body = symText style us

        match style with
        | Unicode -> body + (if e = 1 then "" else superscript e)
        | Ascii -> body + (if e = 1 then "" else "^" + intText e)
        | Latex -> body + (if e = 1 then "" else "^{" + intText e + "}")
        | Siunitx -> body + siunitxPower e

    /// The base-unit decomposition, unwrapped. Iteration order is `Dim.basis` — kg, m, s, A, K,
    /// mol, cd — which is both SI's canonical order and, not coincidentally, the order §2.2's
    /// siunitx example is written in.
    /// `spelling` is how a factor's symbol becomes typography: `baseSymbol` for the seven-base
    /// decomposition of a `Dim`, `factorSpelling registry` for a caller-supplied composition.
    /// The layout is identical either way, which is the reason this takes a function rather
    /// than being written twice.
    let private expandedBodyWith spelling style (exps: (string * int) list) =
        match style with
        | Siunitx ->
            let positive =
                exps
                |> List.filter (fun (_, e) -> e > 0)
                |> List.map (fun (sym, e) -> factorText Siunitx (spelling sym) e)
                |> String.concat ""

            let negative =
                exps
                |> List.filter (fun (_, e) -> e < 0)
                |> List.map (fun (sym, e) -> "\\per" + factorText Siunitx (spelling sym) (-e))
                |> String.concat ""

            positive + negative
        | _ ->
            let sep =
                match style with
                | Ascii -> "."
                | Latex -> "\\,"
                | _ -> "·"

            exps
            |> List.map (fun (sym, e) -> factorText style (spelling sym) e)
            |> String.concat sep

    let private expandedBody style (exps: (string * int) list) =
        expandedBodyWith baseSymbol style exps

    /// siunitx is the only style whose unit lives inside a command. Everything internal deals
    /// in unwrapped bodies so that the `10⁻⁹` fallback can splice a number into the same call.
    let private wrapQty style (body: string) =
        match style with
        | Siunitx -> "\\qty{}{" + body + "}"
        | _ -> body

    /// The honest fallback when a prefix cannot be attached to a symbol: state the power of ten
    /// explicitly. `10⁻⁹·m²` is unambiguous where `nm²` is a factor of 10⁹ wrong.
    let private multiplier style (exponent: int) (body: string) =
        match style with
        | Unicode -> "10" + superscript exponent + "·" + body
        | Ascii -> "10^" + intText exponent + "." + body
        | Latex -> "10^{" + intText exponent + "}\\," + body
        | Siunitx -> "\\qty{1e" + intText exponent + "}{" + body + "}"

    /// The unit body, in preference order: a registered name for the whole dimension, then the
    /// caller's own factorisation, then the seven-base decomposition.
    ///
    /// The middle rung is the one D18 does not cover and does not conflict with. D18 refuses to
    /// *infer* `J·K⁻¹` from a bare `Dim` because inference is ambiguous and non-confluent —
    /// the same vector is `J/K` and `N·m/K`, and picking one is a guess. `factors` is not a
    /// guess: it is what the caller wrote. The registry still wins, so `mul newton metre`
    /// remains `J` rather than becoming `N·m`.
    /// The registry's name for a dimension — unless the factorisation carries an angle, which
    /// the dimension cannot see (D98). `rad·s⁻¹` and `Hz` share `s⁻¹`; naming the first by the
    /// second is a factor of 2π said confidently.
    let private canonicalFor (registry: UnitRegistry) (factors: (string * int) list) (dim: Dim) =
        if U.angleExponentOf "" factors <> 0 then None else UnitRegistry.tryCanonical registry dim

    let private bodyOfWith style (registry: UnitRegistry) (factors: (string * int) list) (dim: Dim) =
        match canonicalFor registry factors dim with
        | Some us -> symText style us
        | None when not (List.isEmpty factors) -> expandedBodyWith (factorSpelling registry) style factors
        | None -> expandedBody style (Dim.exponents dim)

    let private bodyOf style (registry: UnitRegistry) (dim: Dim) =
        bodyOfWith style registry [] dim

    // ---- Public rendering -------------------------------------------------------------

    /// §2.2's `render`. Canonicalises through the registry, falls back to the normalised
    /// exponent form.
    ///
    /// A dimensionless `Dim` renders as the **empty string**, not `"1"`. Core's `Dim.show`
    /// prints `1` and is right to, but the only consumer of this function is a label, and
    /// `x [1]` is noise on an axis of a pure ratio. `Axis` drops the brackets entirely when
    /// this returns empty.
    let render (style: UnitStyle) (registry: UnitRegistry) (dim: Dim) =
        if Dim.isDimensionless dim then
            ""
        else
            wrapQty style (bodyOf style registry dim)

    /// The base-unit form, ignoring the registry. Force renders as `kg·m·s⁻²` rather than `N`.
    ///
    /// Worth having as a first-class function rather than "call `render` with an empty
    /// registry": it is what §2.2's own siunitx example shows, it is what a dimensional-analysis
    /// readout wants, and it is the form a reader falls back on when a canonical symbol is
    /// unfamiliar.
    let renderExpanded (style: UnitStyle) (dim: Dim) =
        if Dim.isDimensionless dim then
            ""
        else
            wrapQty style (expandedBody style (Dim.exponents dim))

    /// `render` against `UnitRegistry.si`.
    let renderSi (style: UnitStyle) (dim: Dim) = render style UnitRegistry.si dim

    /// Where a `Prefix` and a `Dim` become one string.
    ///
    /// The kilogram is the special case the whole design has to bend around: SI's base mass
    /// unit already carries a prefix, so a chosen prefix of 10⁻⁶ on `float<kg>` prints `mg` —
    /// the prefix shifted up three decades and attached to the *gram*, which is not a base unit
    /// at all. Values are still divided by 10⁻⁶, because 10⁻⁶ kg **is** 1 mg. Get this wrong
    /// and every mass axis in the library is off by a factor of 1000.
    ///
    /// When the prefix cannot be attached (`m²`, `m·s⁻¹` — see `Prefix.isPrefixableDim`) this
    /// falls back to an explicit `10ⁿ` multiplier instead of silently dropping the prefix. A
    /// dropped prefix would leave rescaled tick values under an unrescaled unit, which is the
    /// worst possible failure mode: a plot that is quietly wrong by nine orders of magnitude.
    let internal renderPrefixedWith
        (style: UnitStyle)
        (registry: UnitRegistry)
        (factors: (string * int) list)
        (p: Prefix)
        (dim: Dim)
        =
        if Dim.isDimensionless dim then
            ""
        elif p.Exponent = 0 then
            // Not `render`: that one takes only a `Dim` and would drop the factorisation, so an
            // unprefixed field axis read `kg·m·s⁻³·A⁻¹` while a prefixed one read `MV·m⁻¹`.
            wrapQty style (bodyOfWith style registry factors dim)
        else
            let target =
                match canonicalFor registry factors dim with
                | Some us -> Some(p, us)
                | None ->
                    match Dim.exponents dim with
                    | [ ("kg", 1) ] -> Prefix.shift 3 p |> Option.map (fun pg -> pg, gram)
                    | [ (sym, 1) ] -> Some(p, baseSymbol sym)
                    | _ -> None

            match target with
            | Some(pfx, us) -> wrapQty style (prefixText style pfx + symText style us)
            | None when not (List.isEmpty factors) ->
                // A composed unit takes its prefix on the leading factor: `MV·m⁻¹` is mega on
                // the volt, and that is how it is read aloud and how siunitx spells it.
                //
                // Only when that factor has exponent exactly 1, and only when it is not the
                // kilogram. `M` on an `m²` factor would mean 10⁶ m² where the rescaling applied
                // 10⁶ to the value — wrong by a squared decade — and the kilogram needs the
                // gram shift the branch above performs. Both fall through to the honest
                // `10ⁿ·` multiplier rather than guessing.
                match factors with
                | (sym, 1) :: rest when sym <> "kg" ->
                    let head = prefixText style p + symText style (factorSpelling registry sym)

                    let body =
                        if List.isEmpty rest then
                            head
                        else
                            let tail = expandedBodyWith (factorSpelling registry) style rest

                            match style with
                            | Siunitx -> head + tail
                            | Ascii -> head + "." + tail
                            | Latex -> head + "\\," + tail
                            | _ -> head + "·" + tail

                    wrapQty style body
                | _ -> multiplier style p.Exponent (bodyOfWith style registry factors dim)
            | None -> multiplier style p.Exponent (bodyOf style registry dim)

    let renderPrefixed (style: UnitStyle) (registry: UnitRegistry) (p: Prefix) (dim: Dim) =
        renderPrefixedWith style registry [] p dim

    /// **The documented exception to §12.6.** Renders a unit by its *declared* symbol rather
    /// than from its dimension.
    ///
    /// §12.6 forbids composing display strings, and this does not compose anything: it takes a
    /// symbol that a witness author wrote down deliberately and typesets it. It exists because
    /// `Dim` is genuinely insufficient for one class of unit — eV, cal, bar, min, Å and °C all
    /// share a dimension with an SI unit of a different size, so canonicalising from `Dim`
    /// would name the wrong unit and, worse, name it confidently. `unitText` decides which of
    /// the two paths applies, on evidence from the witness rather than by inspecting strings.
    let internal renderSymbol (style: UnitStyle) (p: Prefix) (symbol: string) =
        if System.String.IsNullOrEmpty symbol then
            ""
        else
            wrapQty style (prefixText style p + symText style (spellingOf symbol))

    /// Dimensionless units whose declared symbol is still worth printing. `rad` and `sr` have
    /// `Dim.zero` — angle is dimensionless in SI — so `render` would return empty for them, and
    /// an unlabelled angle axis is a regression from a labelled one. Same policy-table pattern
    /// as `Prefix.nonMetric`, and same justification: this is a fact about units, not a display
    /// string being assembled.
    let private angleSymbols = set [ Derived.radian.Symbol; Derived.steradian.Symbol ]

    /// The unit text for an *axis*, from a witness plus a chosen prefix. This is the function
    /// that decides between the two rendering paths, and it decides on witness metadata alone:
    ///
    /// * `IsAffine` or `ToSI <> 1` — the unit is not SI-coherent, so its `Dim` names a
    ///   *different* unit of the same dimension. Use the declared symbol. This is what makes an
    ///   electronvolt axis say `keV` rather than `kJ`.
    /// * dimensionless — nothing to render, unless it is an angle (see `angleSymbols`).
    /// * otherwise — SI-coherent, so the `Dim` **is** the unit, and §12.6's rule applies with
    ///   full force. Anything built by `U.mul`/`U.div` out of coherent parts lands here, which
    ///   is precisely the case where the composed `Symbol` is junk.
    /// The same rendering decision, stated over the *erased* witness metadata rather than a
    /// `U<'u>`. See `Prefix.selectForErased` for why this pair exists: §15's reloaded figure has
    /// only `IScaleErased`, and the `ToSI <> 1.0` test is what makes an electronvolt axis read
    /// `keV` rather than `kJ` (D16). Sharing one implementation is what keeps the erased path
    /// and the measured path from disagreeing.
    let internal unitTextErasedWith
        (style: UnitStyle)
        (registry: UnitRegistry)
        (p: Prefix)
        (symbol: string)
        (factors: (string * int) list)
        (dim: Dim)
        (toSI: float)
        (offsetSI: float)
        =
        if offsetSI <> 0.0 || toSI <> 1.0 then
            renderSymbol style p symbol
        elif Dim.isDimensionless dim then
            if angleSymbols.Contains symbol then
                renderSymbol style Prefix.none symbol
            else
                ""
        else
            renderPrefixedWith style registry factors p dim

    /// The factorless form, kept because most callers have no witness to hand — a bare
    /// `Symbol`/`Dim`/`ToSI` triple still renders exactly as it did before `U.Factors` existed.
    let internal unitTextErased
        (style: UnitStyle)
        (registry: UnitRegistry)
        (p: Prefix)
        (symbol: string)
        (dim: Dim)
        (toSI: float)
        (offsetSI: float)
        =
        unitTextErasedWith style registry p symbol [] dim toSI offsetSI

    let unitText<[<Measure>] 'u> (style: UnitStyle) (registry: UnitRegistry) (p: Prefix) (u: U<'u>) =
        unitTextErasedWith style registry p u.Symbol u.Factors u.Dim u.ToSI u.OffsetSI

    /// A number already written, and the unit text it was written in: `2 kV·m⁻¹`, or the number
    /// alone when the unit has no text. `quantityIn` ends here, and so does a live figure's
    /// readout (D105), which has its unit text from the axis rather than from a witness.
    let withUnit (unitText: string) (shown: string) : string =
        if unitText = "" then shown else shown + " " + unitText

    /// `quantity` under a prefix the caller has already chosen. A control chooses it once from
    /// its range, so its label does not jump from `950 V` to `1.05 kV` in the middle of a drag.
    let quantityIn<[<Measure>] 'u>
        (style: UnitStyle)
        (registry: UnitRegistry)
        (prefix: Prefix)
        (number: float -> string)
        (u: U<'u>)
        (v: float<'u>)
        : string =
        withUnit (unitText style registry prefix u) (number (float v / Prefix.factor prefix))

    /// A value and its unit as one string: `2 kV·m⁻¹`, `300 K`, `0 rad`.
    ///
    /// The one place a single quantity is written for a reader. The quiver key (D87) and
    /// `FixedPoint.describe` (D92) each had a private copy, and the two already disagreed on the
    /// prefix; a control's label would have made a third. What the callers legitimately differ in
    /// is an argument here rather than a reason to copy it: `prefixed` chooses the prefix the value
    /// would be ticked in (`Prefix.selectForUnit`) or none, and `number` writes the scaled value,
    /// because a key wants the shortest exact form and a description wants four places.
    ///
    /// A dimensionless value with no symbol is the number alone, with no trailing space.
    let quantity<[<Measure>] 'u>
        (style: UnitStyle)
        (registry: UnitRegistry)
        (prefixed: bool)
        (number: float -> string)
        (u: U<'u>)
        (v: float<'u>)
        : string =
        let x = float v
        let prefix = if prefixed then Prefix.selectForUnit u (x, x) else Prefix.none
        quantityIn style registry prefix number u v
