namespace Marey

/// The unit witness: a phantom-typed record tying a compile-time `[<Measure>]` to
/// runtime metadata.
///
/// Microsoft's own docs on units of measure: *"implementing a ToString function to print out
/// the units is not possible."* Every axis label, legend entry and serialized figure in this
/// library needs a runtime handle on the unit. This is it.
///
/// `Symbol` is the unit as *written by whoever declared it* — it is a presentation hint, and
/// composing it is wrong. `Dim` is the truth.
/// **The witness as a value that may be absent**: everything a `U<'u>` carries at run time,
/// and nothing typed.
///
/// The unit-optional mode takes its witness as an optional method parameter, and an optional
/// parameter cannot be measure-generic, because an omitted `'u` has nothing to resolve to. This is
/// the non-generic type it is declared at instead. F# inserts the coercion from `U<m>` at a method
/// argument, so `xUnit = U.metre` needs no annotation; C# already sees `U<'u>` as one non-generic
/// class, and this is the same thing spelled for F#.
///
/// `[<AllowNullLiteral>]` because an omitted parameter has to be *something* to C#: F#'s own
/// `?xUnit` compiles to an `FSharpOption` that a C# caller must pass by hand, and
/// `DefaultParameterValue(null)` is the spelling both languages read as optional. `null` means
/// `U.unspecified`, and `U.erase` is where that is decided, once.
[<AllowNullLiteral>]
type IUnit =
    abstract Symbol: string
    abstract Dim: Dim
    abstract Factors: (string * int) list
    abstract ToSI: float
    abstract OffsetSI: float
    /// False only for `U.unspecified` and what is composed from it — see `U.Stated`.
    abstract Stated: bool

/// A unit witness: the value that carries a measure's symbol, dimension and conversion to SI,
/// so an axis can label and convert what the type system checks.
[<Sealed>]
type U<[<Measure>] 'u>(symbol: string, dim: Dim, toSI: float, offsetSI: float, factors: (string * int) list, stated: bool) =

    /// A unit built by composition, which records its factors — what `U.mul` and friends pass.
    new(symbol, dim, toSI, offsetSI, factors) = U<'u>(symbol, dim, toSI, offsetSI, factors, true)

    /// A unit as *declared*: it records no composition, and `Factors` is empty. This is the
    /// constructor every witness in `Marey.Units` uses; only `U.mul` and friends pass factors.
    new(symbol, dim, toSI, offsetSI) = U<'u>(symbol, dim, toSI, offsetSI, [])

    /// Multiplicative unit: `siValue = raw * ToSI`.
    new(symbol, dim, toSI) = U<'u>(symbol, dim, toSI, 0.0, [])

    /// The symbol this unit was *declared* with. Never build one by concatenation — see
    /// `Marey.Units.UnitDisplay`.
    member _.Symbol = symbol

    /// The composition this unit was *built* from, as `(symbol, exponent)` pairs — empty for a
    /// declared unit, non-empty only for one produced by `U.mul`, `div`, `recip`, `square` or
    /// `cube`.
    ///
    /// **This is not string composition, and the difference is the whole point.** The
    /// string bug was `mul force metre` concatenating its way to `kg·m/s/s·m`: a
    /// string, appended to, with no way to cancel or combine like factors. This is structure.
    /// `U.mul` merges exponents per symbol, so the same call yields `[kg, 1; m, 2; s, -2]` —
    /// which is what the quantity *is*, in the terms the caller wrote it in, and cannot
    /// accumulate the duplicated `/s/s` that made the string wrong.
    ///
    /// It exists because `Dim` alone cannot express a factorisation the caller supplied. `U.div
    /// volt metre` has the dimension of `kg·m·s⁻³·A⁻¹` and no registered name, so display falls
    /// back to base units and an electric-field axis reads `10⁶·kg·m·s⁻³·A⁻¹` rather than `MV·m⁻¹`.
    /// The library declines to *infer* a factorisation from a bare `Dim` because inference is
    /// ambiguous — the same `Dim` is `J/K` and `N·m/K`. Nothing is inferred here: the caller wrote
    /// `div volt metre` and this records which of those readings they meant.
    member _.Factors = factors

    /// SI dimension exponents. The source of truth for identity and for display.
    member _.Dim = dim

    /// Multiplicative factor to SI base units. `U.kilometre.ToSI = 1000.0`.
    member _.ToSI = toSI

    /// Additive offset to SI base units, applied *after* scaling. Non-zero only for affine units —
    /// degrees Celsius (273.15) and Fahrenheit. This is the "°C offset problem"; carrying the field
    /// from day one is what stops it becoming a breaking change when the UDUNITS-2 type provider
    /// lands.
    member _.OffsetSI = offsetSI

    /// True for °C / °F and anything else where zero is not zero. An affine unit has no
    /// meaningful product or quotient — see `U.mul`.
    member _.IsAffine = offsetSI <> 0.0

    /// Whether anyone *said* what this unit is. True for every witness but
    /// `U.unspecified` and what is composed from it.
    ///
    /// **Unspecified is not dimensionless.** `U.one` claims the data has no dimension, and
    /// `Prefix` and the axis-sharing check act on that claim: a `U.one` layer over metres is an
    /// error. Revenue in dollars, read from a spreadsheet by a caller who never stated a unit,
    /// is neither dimensionless nor metres, and the honest thing for an axis to do with it is
    /// to make no claim at all. An unstated axis prints no bracket, takes no prefix, and shares
    /// with anything.
    member _.Stated = stated

    override _.ToString() = symbol

    interface IUnit with
        member _.Symbol = symbol
        member _.Dim = dim
        member _.Factors = factors
        member _.ToSI = toSI
        member _.OffsetSI = offsetSI
        member _.Stated = stated

/// **The algebra lives in module-level functions with explicit `[<Measure>]` type
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

    /// The factorisation a witness contributes to a composition: what it was built from, or
    /// itself if it was declared rather than built. An empty symbol contributes nothing, which
    /// is what keeps `one` from appearing as a factor.
    let private factorsOf (symbol: string) (factors: (string * int) list) =
        if not (List.isEmpty factors) then factors
        elif System.String.IsNullOrEmpty symbol then []
        else [ symbol, 1 ]

    /// Merge two factorisations, summing exponents per symbol and dropping anything that
    /// cancels to zero. **This is the step string concatenation could not perform**, and
    /// the reason `mul force metre` yields `[kg,1; m,2; s,-2]` here where the string yielded
    /// `kg·m/s/s·m`.
    ///
    /// Order is first-appearance, left operand first, so `div volt metre` reads `V·m⁻¹` rather
    /// than `m⁻¹·V`: a caller who wrote a numerator first expects to read it first, and nothing
    /// downstream re-sorts.
    /// How many powers of angle a witness carries: a radian or a degree counts once, a steradian twice
    /// (solid angle is angle squared). Read from the factorisation, or from the declared symbol
    /// when the witness was declared rather than built — `U.radian` is `rad` with no
    /// factors.
    ///
    /// **Angle is dimensionless in SI, so `Dim` cannot see it**, and this is the only place it
    /// survives the erasure. It is what keeps `rad/s` from rendering as `Hz` and from sharing an
    /// axis with a frequency, two quantities a factor of 2π apart.
    ///
    /// A witness declared by hand with a compound symbol — `U<rad/s>("rad/s", …)` — has no
    /// factors and a symbol that is not `rad`, so it reads as carrying no angle. Build angular
    /// units by composition, `U.div U.radian U.second`, and the angle is seen.
    let angleExponentOf (symbol: string) (factors: (string * int) list) : int =
        let parts =
            if not (List.isEmpty factors) then factors
            elif System.String.IsNullOrEmpty symbol then []
            else [ symbol, 1 ]

        parts |> List.sumBy (fun (s, e) -> if s = "rad" || s = "°" then e elif s = "sr" then 2 * e else 0)

    let private mergeFactors (a: (string * int) list) (b: (string * int) list) =
        let merged =
            b
            |> List.fold
                (fun acc (sym, e) ->
                    if List.exists (fst >> (=) sym) acc then
                        acc |> List.map (fun (s, x) -> if s = sym then s, x + e else s, x)
                    else
                        acc @ [ sym, e ])
                a

        merged |> List.filter (fun (_, e) -> e <> 0)

    let private scaleFactors (n: int) (factors: (string * int) list) =
        factors |> List.map (fun (s, e) -> s, e * n)

    /// Product of two units. Verified generic across five distinct instantiations
    /// (`m/s`, `m/s^2`, `kg*m/s^2`, `kg*m/s`, `kg*m^2/s^2`).
    ///
    /// The composed `Symbol` is deliberately naive and deliberately not trusted: it is a
    /// debugging aid only. Display goes through `Dim`, or through `Factors` when `Dim` has no
    /// registered name — see `U.Factors`.
    let mul<[<Measure>] 'a, [<Measure>] 'b> (a: U<'a>) (b: U<'b>) : U<'a * 'b> =
        U<'a * 'b>(
            a.Symbol + "·" + b.Symbol,
            Dim.add a.Dim b.Dim,
            a.ToSI * b.ToSI,
            0.0,
            mergeFactors (factorsOf a.Symbol a.Factors) (factorsOf b.Symbol b.Factors),
            a.Stated && b.Stated
        )

    /// Quotient of two units. Offset is dropped: the quotient of an affine unit is not
    /// affine, and there is no sensible offset to carry. `isAffine` lets a caller check
    /// first if it cares.
    let div<[<Measure>] 'a, [<Measure>] 'b> (a: U<'a>) (b: U<'b>) : U<'a / 'b> =
        U<'a / 'b>(
            a.Symbol + "/" + b.Symbol,
            Dim.sub a.Dim b.Dim,
            a.ToSI / b.ToSI,
            0.0,
            mergeFactors (factorsOf a.Symbol a.Factors) (scaleFactors -1 (factorsOf b.Symbol b.Factors)),
            a.Stated && b.Stated
        )

    /// Reciprocal. `recip second : U<1/s>` — frequency, and the type of a frame rate.
    let recip<[<Measure>] 'a> (a: U<'a>) : U<1 / 'a> =
        U<1 / 'a>(
            "1/" + a.Symbol,
            Dim.neg a.Dim,
            1.0 / a.ToSI,
            0.0,
            scaleFactors -1 (factorsOf a.Symbol a.Factors),
            a.Stated
        )

    /// Integer powers, spelled out. There is no way to write `U<'a^n>` for a runtime `n`, so
    /// the exponents anyone actually uses get their own function. Everything past cube goes
    /// through `mul`.
    let square<[<Measure>] 'a> (a: U<'a>) : U<'a^2> =
        U<'a^2>(
            a.Symbol + "²",
            Dim.pow 2 a.Dim,
            a.ToSI * a.ToSI,
            0.0,
            scaleFactors 2 (factorsOf a.Symbol a.Factors),
            a.Stated
        )

    let cube<[<Measure>] 'a> (a: U<'a>) : U<'a^3> =
        U<'a^3>(
            a.Symbol + "³",
            Dim.pow 3 a.Dim,
            a.ToSI * a.ToSI * a.ToSI,
            0.0,
            scaleFactors 3 (factorsOf a.Symbol a.Factors),
            a.Stated
        )

    /// The dimensionless unit. `float = float<1>`, which is why this is also what a ratio,
    /// a count and a probability all get.
    let one: U<1> = U<1>("", Dim.zero, 1.0, 0.0)

    /// The unit nobody stated: what the unit-optional mode draws an axis in when no
    /// witness was given. No bracket, no prefix, and no claim for the axis-sharing check to act
    /// on. **Not `one`**, which says the quantity has no dimension — see `U.Stated`.
    let unspecified: U<1> = U<1>("", Dim.zero, 1.0, 0.0, [], false)

    /// **A runtime unit, carried at measure 1.** The witness of the unit-optional mode,
    /// where data is `float[]` and the unit arrives as a value: everything `u` carries at run
    /// time, under a measure that says nothing. `null` is `unspecified`.
    ///
    /// After the erasure boundary the result is indistinguishable from `u`, which is why
    /// one measure-generic implementation serves both modes: a stat that derives a unit derives
    /// it from these runtime fields, so `density` over `erase U.metre` still reports `m⁻¹`. What
    /// is given up is the compile-time half, and that is the mode's definition.
    let erase (u: IUnit) : U<1> =
        match u with
        | null -> unspecified
        // Measures are erased, so at run time every `U<'u>` *is* a `U<1>`, and the witness the
        // caller passed is returned as it is. Only an `IUnit` implemented elsewhere is copied.
        // Not `:? U<1>`, which FS1240 refuses for saying the same thing.
        | u when u.GetType() = typeof<U<1>> -> unbox<U<1>> (box u)
        | u -> U<1>(u.Symbol, u.Dim, u.ToSI, u.OffsetSI, u.Factors, u.Stated)

    let symbol (u: U<'u>) = u.Symbol
    let dim (u: U<'u>) = u.Dim
    let isAffine (u: U<'u>) = u.IsAffine

    /// Rescale a unit by a numeric factor, keeping the dimension. This is how prefixes are
    /// built: `scaled "km" 1000.0 metre`. The measure is *unchanged*, which is correct —
    /// kilometres and metres are the same measure, and the difference is a runtime factor.
    let scaled<[<Measure>] 'a> (newSymbol: string) (factor: float) (a: U<'a>) : U<'a> =
        U<'a>(newSymbol, a.Dim, a.ToSI * factor, a.OffsetSI, [], a.Stated)

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
