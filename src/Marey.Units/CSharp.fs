namespace Marey.CSharp

open Marey

/// D124 — **every unit witness, for C#, under one name**: `Units.Metre`, `Units.DegreeCelsius`,
/// `Units.Div(Units.Volt, Units.Metre)`.
///
/// F# reaches the witnesses as `U.metre`, `Derived.volt` and `NonSi.degreeCelsius`, three modules
/// whose split follows how a unit is defined rather than how it is looked for; a C# caller should
/// not have to know which is which, and should not read F#'s lowercase as a C# property. Each
/// member here is the F# witness itself, not a copy, so `Units.Metre` and `U.metre` are one
/// object. Its type is `U<'u>`, which C# sees as the class `U`.
///
/// The algebra takes `IUnit` and returns a witness at measure 1: the measure is F#'s, and C#
/// gives it up by definition (D109). `Dim`, the symbol, the factors and the SI conversion are all
/// carried, so a quotient labels its axis as F#'s does.
[<AbstractClass; Sealed>]
type Units =

    /// A quantity with no dimension — a ratio, a count, a probability. It claims dimensionless,
    /// which an axis acts on; for numbers nobody named, leave the unit out, or pass `Unspecified`.
    static member One = U.one

    /// The unit nobody stated (D127): no bracket, no prefix, no dimensional claim.
    static member Unspecified = U.unspecified

    static member Mul(a: IUnit, b: IUnit) : U<1> = U.mul (U.erase a) (U.erase b)

    static member Div(a: IUnit, b: IUnit) : U<1> = U.div (U.erase a) (U.erase b)

    static member Recip(a: IUnit) : U<1> = U.recip (U.erase a)

    static member Square(a: IUnit) : U<1> = U.square (U.erase a)

    static member Cube(a: IUnit) : U<1> = U.cube (U.erase a)

    /// A unit rescaled by a factor, keeping its dimension: `Units.Scaled("km", 1000.0, Units.Metre)`.
    static member Scaled(symbol: string, factor: float, a: IUnit) : U<1> = U.scaled symbol factor (U.erase a)

    // ---- SI base units, and the gram (`U`) ------------------------------------------

    static member Metre = U.metre
    static member Kilogram = U.kilogram
    static member Second = U.second
    static member Ampere = U.ampere
    static member Kelvin = U.kelvin
    static member Mole = U.mole
    static member Candela = U.candela
    static member SquareMetre = U.squareMetre
    static member CubicMetre = U.cubicMetre
    static member MetrePerSecond = U.metrePerSecond
    static member MetrePerSecondSquared = U.metrePerSecondSquared
    static member KilogramPerCubicMetre = U.kilogramPerCubicMetre
    static member PerSecond = U.perSecond
    static member Gram = U.gram

    // ---- SI derived units (`Derived`) -----------------------------------------------

    static member Hertz = Derived.hertz
    static member Newton = Derived.newton
    static member Pascal = Derived.pascal
    static member Joule = Derived.joule
    static member Watt = Derived.watt
    static member Coulomb = Derived.coulomb
    static member Volt = Derived.volt
    static member Farad = Derived.farad
    static member Ohm = Derived.ohm
    static member Siemens = Derived.siemens
    static member Weber = Derived.weber
    static member Tesla = Derived.tesla
    static member Henry = Derived.henry
    static member Lumen = Derived.lumen
    static member Lux = Derived.lux
    static member Becquerel = Derived.becquerel
    static member Gray = Derived.gray
    static member Sievert = Derived.sievert
    static member Katal = Derived.katal
    static member Radian = Derived.radian
    static member Steradian = Derived.steradian
    static member NewtonByComposition = Derived.newtonByComposition
    static member JouleByComposition = Derived.jouleByComposition
    static member WattByComposition = Derived.wattByComposition
    static member HertzByComposition = Derived.hertzByComposition
    static member LumenByComposition = Derived.lumenByComposition

    // ---- units outside SI (`NonSi`) -------------------------------------------------

    static member Electronvolt = NonSi.electronvolt
    static member Calorie = NonSi.calorie
    static member Angstrom = NonSi.angstrom
    static member AstronomicalUnit = NonSi.astronomicalUnit
    static member LightYear = NonSi.lightYear
    static member Parsec = NonSi.parsec
    static member Barn = NonSi.barn
    static member Litre = NonSi.litre
    static member Bar = NonSi.bar
    static member Atmosphere = NonSi.atmosphere
    static member Minute = NonSi.minute
    static member Hour = NonSi.hour
    static member Day = NonSi.day
    static member Year = NonSi.year
    static member Dalton = NonSi.dalton
    static member Gauss = NonSi.gauss
    static member DegreeCelsius = NonSi.degreeCelsius
    static member DegreeFahrenheit = NonSi.degreeFahrenheit

    // ---- Read from text (D110) -----------------------------------------------------------------

    /// A unit in UDUNITS-2's syntax: `Units.Parse("V/m")`, `Units.Parse("kg m s-2")`. Throws
    /// `FormatException` with the reason when the text is not one. See `U.parse`.
    static member Parse(text: string) : U<1> = U.parse text

    /// `Parse`, returning false rather than throwing.
    static member TryParse(text: string, [<System.Runtime.InteropServices.Out>] result: byref<U<1>>) : bool =
        match U.tryParse text with
        | Ok u ->
            result <- u
            true
        | Error _ ->
            result <- Unchecked.defaultof<_>
            false
