namespace Marey

/// §2.1 — The unit witness: a phantom-typed record tying a compile-time `[<Measure>]` to
/// runtime metadata.
///
/// Microsoft's own docs on units of measure: *"implementing a ToString function to print out
/// the units is not possible."* Every axis label, legend entry and serialized figure in this
/// library needs a runtime handle on the unit. This is it.
///
/// `Symbol` is the unit as *written by whoever declared it* — it is a presentation hint, and
/// composing it is wrong (§2.2, §12.6). `Dim` is the truth.
[<Sealed>]
type U<[<Measure>] 'u>(symbol: string, dim: Dim, toSI: float, offsetSI: float) =

    /// Multiplicative unit: `siValue = raw * ToSI`.
    new(symbol, dim, toSI) = U<'u>(symbol, dim, toSI, 0.0)

    /// The symbol this unit was *declared* with. Never build one by concatenation — see
    /// `Marey.Units.UnitDisplay` and §12.6.
    member _.Symbol = symbol

    /// SI dimension exponents. The source of truth for identity and for display.
    member _.Dim = dim

    /// Multiplicative factor to SI base units. `U.kilometre.ToSI = 1000.0`.
    member _.ToSI = toSI

    /// Additive offset to SI base units, applied *after* scaling. Non-zero only for affine
    /// units — degrees Celsius (273.15) and Fahrenheit. §2.4 flags this as the "°C offset
    /// problem"; carrying the field from day one is what stops it becoming a breaking change
    /// when the UDUNITS-2 type provider lands.
    member _.OffsetSI = offsetSI

    /// True for °C / °F and anything else where zero is not zero. An affine unit has no
    /// meaningful product or quotient — see `U.mul`.
    member _.IsAffine = offsetSI <> 0.0

    override _.ToString() = symbol

/// §12.1 — **The algebra lives in module-level functions with explicit `[<Measure>]` type
/// parameters, and it must never move into operator overloads on `U<'u>`.**
///
/// ```fsharp
/// static member ( * ) (a: U<'a>, b: U<'b>) : U<'a * 'b> = ...   // warning FS0064
/// ```
/// > *This construct causes code to be less generic than indicated by the type annotations.
/// > The unit-of-measure variable 'b has been constrained to be measure ''a 'b/'a0'.*
///
/// It compiles, and it is subtly wrong: the second measure parameter is silently
/// over-constrained after the first use. `TreatWarningsAsErrors` in Directory.Build.props is
/// what stops anyone re-introducing it by accident.
[<RequireQualifiedAccess>]
module U =

    /// Product of two units. Verified generic across five distinct instantiations
    /// (`m/s`, `m/s^2`, `kg*m/s^2`, `kg*m/s`, `kg*m^2/s^2`) — §12.1.
    ///
    /// The composed `Symbol` is deliberately naive and deliberately not trusted: it is a
    /// debugging aid only. Display goes through `Dim`.
    let mul<[<Measure>] 'a, [<Measure>] 'b> (a: U<'a>) (b: U<'b>) : U<'a * 'b> =
        U<'a * 'b>(a.Symbol + "·" + b.Symbol, Dim.add a.Dim b.Dim, a.ToSI * b.ToSI, 0.0)

    /// Quotient of two units. Offset is dropped: the quotient of an affine unit is not
    /// affine, and there is no sensible offset to carry. `isAffine` lets a caller check
    /// first if it cares.
    let div<[<Measure>] 'a, [<Measure>] 'b> (a: U<'a>) (b: U<'b>) : U<'a / 'b> =
        U<'a / 'b>(a.Symbol + "/" + b.Symbol, Dim.sub a.Dim b.Dim, a.ToSI / b.ToSI, 0.0)

    /// Reciprocal. `recip second : U<1/s>` — frequency, and the type of a frame rate.
    let recip<[<Measure>] 'a> (a: U<'a>) : U<1 / 'a> =
        U<1 / 'a>("1/" + a.Symbol, Dim.neg a.Dim, 1.0 / a.ToSI, 0.0)

    /// Integer powers, spelled out. There is no way to write `U<'a^n>` for a runtime `n`, so
    /// the exponents anyone actually uses get their own function. Everything past cube goes
    /// through `mul`.
    let square<[<Measure>] 'a> (a: U<'a>) : U<'a^2> =
        U<'a^2>(a.Symbol + "²", Dim.pow 2 a.Dim, a.ToSI * a.ToSI, 0.0)

    let cube<[<Measure>] 'a> (a: U<'a>) : U<'a^3> =
        U<'a^3>(a.Symbol + "³", Dim.pow 3 a.Dim, a.ToSI * a.ToSI * a.ToSI, 0.0)

    /// The dimensionless unit. `float = float<1>`, which is why this is also what a ratio,
    /// a count and a probability all get.
    let one: U<1> = U<1>("", Dim.zero, 1.0, 0.0)

    let symbol (u: U<'u>) = u.Symbol
    let dim (u: U<'u>) = u.Dim
    let isAffine (u: U<'u>) = u.IsAffine

    /// Rescale a unit by a numeric factor, keeping the dimension. This is how prefixes are
    /// built: `scaled "km" 1000.0 metre`. The measure is *unchanged*, which is correct —
    /// kilometres and metres are the same measure, and the difference is a runtime factor.
    let scaled<[<Measure>] 'a> (newSymbol: string) (factor: float) (a: U<'a>) : U<'a> =
        U<'a>(newSymbol, a.Dim, a.ToSI * factor, a.OffsetSI)

    /// Value in SI base units, measure erased. Honours the affine offset.
    let toSI (u: U<'u>) (x: float<'u>) : float = float x * u.ToSI + u.OffsetSI

    /// Value from SI base units, measure reattached. Honours the affine offset.
    let ofSI<[<Measure>] 'u> (u: U<'u>) (siValue: float) : float<'u> =
        LanguagePrimitives.FloatWithMeasure<'u>((siValue - u.OffsetSI) / u.ToSI)

    /// Convert between two units *of the same dimension* whose measures differ — e.g. a
    /// `float<eV>` read as a `float<J>`.
    ///
    /// This is the one place in the library where a unit error is a runtime error rather
    /// than a compile error, and that is unavoidable: the caller has asserted a measure
    /// change that the type system cannot check. It raises rather than returning an option
    /// because a dimension mismatch here is always a bug, never data.
    let convert<[<Measure>] 'a, [<Measure>] 'b> (from: U<'a>) (into: U<'b>) (x: float<'a>) : float<'b> =
        if from.Dim <> into.Dim then
            failwithf
                "Marey: cannot convert %s [%s] to %s [%s] — different physical dimensions."
                from.Symbol
                (Dim.show from.Dim)
                into.Symbol
                (Dim.show into.Dim)

        ofSI into (toSI from x)
