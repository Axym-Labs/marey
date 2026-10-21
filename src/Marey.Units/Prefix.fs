namespace Marey

/// An SI decimal prefix.
///
/// "Data in `float<m>` spanning 1e-9 … 5e-9 should produce ticks labelled 1, 2, 3, 4, 5 with
/// an axis title of `x [nm]`. That is one of the most visible quality-of-life wins the unit
/// system pays for."
///
/// The prefix is carried as a *value*, not folded into the unit, for the same reason the
/// measure is not: a prefix is a presentation decision made at the axis, from the data range,
/// and the underlying quantity is still metres. Four spellings for the same reason
/// `UnitSymbol` has four.
type Prefix =
    { /// `nano`
      Name: string
      /// `n`, `µ`
      Symbol: string
      /// `n`, `u` — 7-bit safe.
      Ascii: string
      /// Content for `\mathrm{…}`: `n`, `\mu`.
      Latex: string
      /// Bare siunitx macro: `\nano`, `\micro`.
      Siunitx: string
      /// Decimal exponent. `nano = -9`.
      Exponent: int }

    /// The multiplier this prefix denotes. Data is *divided* by it to produce the mantissas.
    member this.Factor = 10.0 ** float this.Exponent

[<RequireQualifiedAccess>]
module Prefix =

    let private mk name symbol ascii latex siunitx exponent =
        { Name = name
          Symbol = symbol
          Ascii = ascii
          Latex = latex
          Siunitx = siunitx
          Exponent = exponent }

    let private simple name symbol exponent =
        mk name symbol symbol symbol ("\\" + name) exponent

    /// No prefix. `Exponent = 0`, `Factor = 1.0`, every spelling empty — so gluing it onto a
    /// symbol is the identity and no renderer needs a special case for "unprefixed".
    let none = mk "" "" "" "" "" 0

    let quecto = simple "quecto" "q" -30
    let ronto = simple "ronto" "r" -27
    let yocto = simple "yocto" "y" -24
    let zepto = simple "zepto" "z" -21
    let atto = simple "atto" "a" -18
    let femto = simple "femto" "f" -15
    let pico = simple "pico" "p" -12
    let nano = simple "nano" "n" -9

    /// The only prefix whose spellings genuinely differ: `µ` / `u` / `\mu` / `\micro`.
    let micro = mk "micro" "µ" "u" "\\mu" "\\micro" -6

    let milli = simple "milli" "m" -3
    let centi = simple "centi" "c" -2
    let deci = simple "deci" "d" -1
    let deca = mk "deca" "da" "da" "da" "\\deca" 1
    let hecto = simple "hecto" "h" 2
    let kilo = simple "kilo" "k" 3
    let mega = simple "mega" "M" 6
    let giga = simple "giga" "G" 9
    let tera = simple "tera" "T" 12
    let peta = simple "peta" "P" 15
    let exa = simple "exa" "E" 18
    let zetta = simple "zetta" "Z" 21
    let yotta = simple "yotta" "Y" 24
    let ronna = simple "ronna" "R" 27
    let quetta = simple "quetta" "Q" 30

    /// Every SI prefix, quecto (10⁻³⁰) to quetta (10³⁰), ascending.
    let all =
        [| quecto
           ronto
           yocto
           zepto
           atto
           femto
           pico
           nano
           micro
           milli
           centi
           deci
           none
           deca
           hecto
           kilo
           mega
           giga
           tera
           peta
           exa
           zetta
           yotta
           ronna
           quetta |]

    /// The prefixes whose exponent is a multiple of three — the only ones automatic selection
    /// is allowed to choose. `centi`, `deci`, `deca` and `hecto` exist and are legal SI, but an
    /// axis auto-labelled in decametres is worse than one labelled in metres, and engineering
    /// notation is what readers expect.
    let engineering = all |> Array.filter (fun p -> p.Exponent % 3 = 0)

    let factor (p: Prefix) = p.Factor

    /// The prefix with exactly this decimal exponent, if SI has one.
    let ofExponent (e: int) =
        all |> Array.tryFind (fun p -> p.Exponent = e)

    /// Move a prefix by `k` decades. `None` when that runs off the end of the SI table.
    ///
    /// This exists for exactly one caller: the kilogram. A prefix chosen for data in `float<kg>`
    /// has to be re-expressed against the *gram* before it can be printed, which is a shift of
    /// +3. See `UnitDisplay.renderPrefixed`.
    let shift (k: int) (p: Prefix) = ofExponent (p.Exponent + k)

    // ---- Selection -------------------------------------------------------------------

    let private finite (x: float) =
        not (System.Double.IsNaN x) && not (System.Double.IsInfinity x)

    /// The pure prefix choice, minus the `Dim`.
    ///
    /// **The design gives the signature as `Dim * (float*float) -> Prefix`. The `Dim` is not
    /// needed to choose the decade** — which prefix puts the mantissas in [1, 1000) is a
    /// property of the numbers alone, and mixing the dimension into that computation would
    /// make the most testable function in the unit system harder to test for no gain. What the
    /// `Dim` decides is something else entirely: whether the chosen prefix may be *attached* to
    /// the unit at all. That is `selectFor`, and separating the two is the correction.
    ///
    /// The magnitude used is `max |lo| |hi|`, not the span. A range of 1000.000 … 1000.005 m is
    /// kilometres with five identical labels, not micrometres with a huge offset; matplotlib
    /// solves that with a separate additive offset annotation, which Marey does not implement
    /// and which is a real gap rather than a disagreement.
    ///
    /// Zero, empty and non-finite ranges select `none` — an axis with no magnitude has no
    /// decade to move to.
    let select (lo: float, hi: float) : Prefix =
        let a = if finite lo then abs lo else 0.0
        let b = if finite hi then abs hi else 0.0
        let magnitude = max a b

        if magnitude = 0.0 then
            none
        else
            let decade = int (floor (log10 magnitude))
            // Round *down* to a multiple of three, for negatives too: floor, not truncation.
            let e = 3 * int (floor (float decade / 3.0))
            let clamped = max -30 (min 30 e)
            ofExponent clamped |> Option.defaultValue none

    /// Whether the chosen prefix can be **glued onto a symbol**, as opposed to stated as an
    /// explicit power of ten. This is the part of the choice that genuinely needs the
    /// `Dim`, and it is a question about typography, not about whether to rescale.
    ///
    /// Two dimensions refuse the glue:
    ///
    /// * **Compound with no registered name.** `m·s⁻¹` prefixed is `nm·s⁻¹` or `m·ns⁻¹` — the
    ///   placement is ambiguous and the two differ by 10¹⁸. Compounds that *do* have a
    ///   registered name (`kg·m·s⁻²` = `N`) are a single symbol again and prefix fine: `kN`.
    /// * **Non-unit exponent.** `m²` prefixed is `nm²`, which by SI convention means `(nm)²` =
    ///   10⁻¹⁸ m², not 10⁻⁹ m². The factor-of-two-in-the-exponent trap.
    ///
    /// In both cases `UnitDisplay.renderPrefixed` falls back to `10⁻⁹·m²`, which is unambiguous.
    /// The predicate is exposed because a caller may want to know *which* form it will get, but
    /// the renderer re-derives it structurally rather than calling this — the two must agree,
    /// and a shared match is easier to keep honest than a shared boolean.
    let internal isPrefixableDim (registry: UnitRegistry) (dim: Dim) =
        if Dim.isDimensionless dim then
            false
        elif (UnitRegistry.tryCanonical registry dim).IsSome then
            true
        else
            match Dim.exponents dim with
            | [ (_, 1) ] -> true
            | _ -> false

    /// `select`, plus the two `Dim`-driven adjustments — **the whole choice**.
    ///
    /// The design gives `Dim * (float*float) -> Prefix`. An earlier version of this function
    /// also took the registry, because deciding whether a compound unit could carry a prefix
    /// needs to know whether the compound has a registered name. Moving the unattachable case
    /// from "refuse to rescale" to "rescale and print `10⁻⁹·m²`" moved that question entirely
    /// into the renderer, and left selection needing nothing but the dimension. The design's
    /// signature is right; the reason is narrower than it looks.
    ///
    /// **Dimensionless refuses to rescale at all.** A ratio, a probability, a count: there is no
    /// unit to hang a prefix or a multiplier on, so rescaling the numbers would be a silent lie
    /// about them. This is the one case where the right answer is to leave the axis alone.
    ///
    /// **Everything else rescales**, including the compounds and powers that cannot take a glued
    /// prefix — they get `10⁻⁹·m²` from the renderer instead. Refusing to rescale those was the
    /// first implementation and it is worse than wrong: an `m²` axis over 0 … 5e-18 renders six
    /// ticks that all read `0.000000000000000`, which violates tick injectivity
    /// outright, because `Ticks.decimalsFor` clamps at 15 places.
    ///
    /// **The mass clamp.** A prefix chosen for `float<kg>` is displayed against the *gram*,
    /// three decades up. Selecting `quetta` (10³⁰) would need `10³³ g`, which SI has no name
    /// for, so mass selection stops at `ronna` (10²⁷ kg = 10³⁰ g = `Qg`).
    let selectFor (dim: Dim) (range: float * float) : Prefix =
        if Dim.isDimensionless dim then
            none
        else
            let p = select range

            if dim = Dim.mass && p.Exponent > 27 then
                ronna
            else
                p

    /// Units that are metric in dimension but never take an SI prefix by convention.
    ///
    /// Keyed on the witness's declared `Symbol`, which is the only stable identity a `U<'u>`
    /// carries — and legitimately so: this is a *policy* table about units, not a display string
    /// being composed, so the canonicalise-from-`Dim` rule does not apply. Everything absent from
    /// this set and dimensionally sensible does take prefixes, which is why `keV`, `mbar`, `kcal`,
    /// `kDa`, `Mpc`, `mG` and `mL` all come out right while `kmin` and `kau` never appear.
    let internal nonMetric =
        set
            [ U.minute.Symbol
              U.hour.Symbol
              U.day.Symbol
              U.astronomicalUnit.Symbol
              U.angstrom.Symbol
              U.atmosphere.Symbol
              U.degreeCelsius.Symbol
              U.degreeFahrenheit.Symbol ]

    /// Whether this *witness* may be prefixed. The witness knows two things the `Dim` does not,
    /// and both of them are disqualifying:
    ///
    /// * **Affine.** `OffsetSI <> 0`. A milli-degree-Celsius would have to rescale 273.15 as
    ///   well as the mantissa; the prefix does not distribute over an affine map. Refused
    ///   outright, which is the machine-checked half of the "must not be composed" warning on
    ///   `U.degreeCelsius`.
    /// * **Not SI-coherent.** `ToSI <> 1`. `float<eV>` has the dimension of joules, so the
    ///   `Dim` path would happily produce `kJ` for data that is in `keV` — off by 10¹⁹. Such a
    ///   unit is prefixed against its own *declared* symbol instead (see
    ///   `UnitDisplay.unitText`), gated by `nonMetric`.
    /// The same policy, stated over the *erased* witness metadata rather than a `U<'u>`.
    ///
    /// This exists because a reloaded figure has no measure left — reloading gives you
    /// `IScaleErased`, not `Scale<'u>` — but it does carry `Symbol`, `Dim`, `ToSI` and
    /// `OffsetSI`, which are there for precisely this reason. Having the erased path call
    /// this rather than re-deriving the branches is what stops the two from drifting: an earlier
    /// version of `Axis.ofErasedWith` re-implemented the decision, got it wrong, and labelled a
    /// reloaded electronvolt axis `kJ`.
    let internal selectForErased (symbol: string) (dim: Dim) (toSI: float) (offsetSI: float) (range: float * float) : Prefix =
        if offsetSI <> 0.0 then none
        elif toSI <> 1.0 then
            if nonMetric.Contains symbol then none else select range
        else
            selectFor dim range

    let selectForUnit<[<Measure>] 'u> (u: U<'u>) (range: float * float) : Prefix =
        selectForErased u.Symbol u.Dim u.ToSI u.OffsetSI range
