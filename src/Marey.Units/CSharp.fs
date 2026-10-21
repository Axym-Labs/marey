namespace Marey.CSharp

open Marey

/// **Every unit witness, for C#, under one name**: `Units.Metre`, `Units.DegreeCelsius`,
/// `Units.Div(Units.Volt, Units.Metre)`.
///
/// F# reaches the witnesses as `U.metre`, `U.volt` and `U.degreeCelsius`, three modules
/// whose split follows how a unit is defined rather than how it is looked for; a C# caller should
/// not have to know which is which, and should not read F#'s lowercase as a C# property. Each
/// member here is the F# witness itself, not a copy, so `Units.Metre` and `U.metre` are one
/// object. Its type is `U<'u>`, which C# sees as the class `U`.
///
/// The algebra takes `IUnit` and returns a witness at measure 1: the measure is F#'s, and C#
/// gives it up by definition. `Dim`, the symbol, the factors and the SI conversion are all
/// carried, so a quotient labels its axis as F#'s does.
[<AbstractClass; Sealed>]
type Units =

    /// A quantity with no dimension — a ratio, a count, a probability. It claims dimensionless,
    /// which an axis acts on; for numbers nobody named, leave the unit out, or pass `Unspecified`.
    static member One = U.one

    /// The unit nobody stated: no bracket, no prefix, no dimensional claim.
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

    // ---- SI derived units (`Derived.fs`) -----------------------------------------------

    static member Hertz = U.hertz
    static member Newton = U.newton
    static member Pascal = U.pascal
    static member Joule = U.joule
    static member Watt = U.watt
    static member Coulomb = U.coulomb
    static member Volt = U.volt
    static member Farad = U.farad
    static member Ohm = U.ohm
    static member Siemens = U.siemens
    static member Weber = U.weber
    static member Tesla = U.tesla
    static member Henry = U.henry
    static member Lumen = U.lumen
    static member Lux = U.lux
    static member Becquerel = U.becquerel
    static member Gray = U.gray
    static member Sievert = U.sievert
    static member Katal = U.katal
    static member Radian = U.radian
    static member Steradian = U.steradian

    // ---- units outside SI (`NonSi.fs`) -------------------------------------------------

    static member Electronvolt = U.electronvolt
    static member Calorie = U.calorie
    static member Angstrom = U.angstrom
    static member AstronomicalUnit = U.astronomicalUnit
    static member LightYear = U.lightYear
    static member Parsec = U.parsec
    static member Barn = U.barn
    static member Litre = U.litre
    static member Bar = U.bar
    static member Atmosphere = U.atmosphere
    static member Minute = U.minute
    static member Hour = U.hour
    static member Day = U.day
    static member Year = U.year
    static member Dalton = U.dalton
    static member Gauss = U.gauss
    static member DegreeCelsius = U.degreeCelsius
    static member DegreeFahrenheit = U.degreeFahrenheit

    // ---- Read from text -----------------------------------------------------------------

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
