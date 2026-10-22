namespace Marey

// ---------------------------------------------------------------------------------------
// Common non-SI units.
//
// **These are opaque measures, not abbreviations, and that is the opposite choice from
// Derived.fs.** The reason is the conversion factor. `[<Measure>] type eV = J` would make
// `float<eV>` and `float<J>` the *same type*, so a joule could be silently added to an
// electronvolt and the 1.602×10⁻¹⁹ would never be applied. Keeping them distinct forces the
// conversion through `U.convert`, which Core's own doc comment anticipates: "Convert between
// two units of the same dimension whose measures differ — e.g. a `float<eV>` read as a
// `float<J>`."
//
// So: same `Dim`, different measure, factor in the witness. A dimension mismatch is still a
// compile error; a *unit* mismatch within a dimension is a call to `U.convert`.
// ---------------------------------------------------------------------------------------

/// Electronvolt. Dimension of energy, 1 eV = 1.602176634×10⁻¹⁹ J (exact since the 2019 SI).
[<Measure>]
type eV

/// Ångström, 10⁻¹⁰ m.
[<Measure>]
type angstrom

/// Bar, 10⁵ Pa.
[<Measure>]
type bar

/// Standard atmosphere, 101325 Pa exactly.
[<Measure>]
type atm

/// Litre, 10⁻³ m³.
[<Measure>]
type L

/// Minute, 60 s.
///
/// **Named `minute`, not `min`, and that is not a style choice.** A `[<Measure>] type min` in
/// namespace `Marey` shadows `Microsoft.FSharp.Core.Operators.min` in *application* position:
/// the namespace is opened more recently than FSharp.Core, so `min 30 e` resolves the type and
/// fails with `FS1133: No constructors are available for the type 'min'`. Every file that opens
/// `Marey` would lose the `min` function. Verified by building it that way first.
[<Measure>]
type minute

/// Hour, 3600 s.
[<Measure>]
type h

/// Day, 86400 s.
[<Measure>]
type d

/// Julian year, 365.25 d = 31 557 600 s. The astronomer's year and the one `ly` is built from.
[<Measure>]
type yr

/// **Degree Celsius — affine.** See `U.degreeCelsius`.
[<Measure>]
type degC

/// **Degree Fahrenheit — affine.** See `U.degreeFahrenheit`.
[<Measure>]
type degF

/// Astronomical unit, 1.495978707×10¹¹ m exactly.
[<Measure>]
type au

/// Parsec.
[<Measure>]
type pc

/// Light-year (Julian).
[<Measure>]
type ly

/// Unified atomic mass unit / dalton.
[<Measure>]
type Da

/// Barn, 10⁻²⁸ m². The nuclear cross-section unit.
[<Measure>]
type barn

/// Gauss, 10⁻⁴ T.
[<Measure>]
type Gs

/// Thermochemical calorie, 4.184 J exactly.
[<Measure>]
type cal

/// Witnesses for the non-SI units a physics audience actually uses.
[<AutoOpen>]
module NonSiWitnesses =

    /// Units outside SI that physics uses: the electronvolt, ångström, astronomical unit, bar, litre,
    /// hour, degree Celsius and the rest.
    [<RequireQualifiedAccess>]
    module U =

        // The helper is spelled out rather than aliased to a one-letter name: `d` is a *measure*
        // in this file (the day), and a value shadowing a measure name in the same file is exactly
        // the kind of thing that costs an hour to read back.
        //                                        L  M  T  I Th  N  J
        let private length = U.dimOf 1 0 0 0 0 0 0
        let private area = U.dimOf 2 0 0 0 0 0 0
        let private volume = U.dimOf 3 0 0 0 0 0 0
        let private energy = U.dimOf 2 1 -2 0 0 0 0
        let private pressure = U.dimOf -1 1 -2 0 0 0 0
        let private magneticFluxDensity = U.dimOf 0 1 -2 -1 0 0 0

        // ---- Energy ----------------------------------------------------------------------

        let electronvolt: U<eV> = U<eV>("eV", energy, 1.602176634e-19)
        let calorie: U<cal> = U<cal>("cal", energy, 4.184)

        // ---- Length ----------------------------------------------------------------------

        let angstrom: U<angstrom> = U<angstrom>("Å", length, 1e-10)
        let astronomicalUnit: U<au> = U<au>("au", length, 1.495978707e11)
        let lightYear: U<ly> = U<ly>("ly", length, 9.4607304725808e15)
        let parsec: U<pc> = U<pc>("pc", length, 3.0856775814913673e16)

        // ---- Area, volume ----------------------------------------------------------------

        let barn: U<barn> = U<barn>("b", area, 1e-28)
        let litre: U<L> = U<L>("L", volume, 1e-3)

        // ---- Pressure --------------------------------------------------------------------

        let bar: U<bar> = U<bar>("bar", pressure, 1e5)
        let atmosphere: U<atm> = U<atm>("atm", pressure, 101325.0)

        // ---- Time ------------------------------------------------------------------------

        let minute: U<minute> = U<minute>("min", Dim.time, 60.0)
        let hour: U<h> = U<h>("h", Dim.time, 3600.0)
        let day: U<d> = U<d>("d", Dim.time, 86400.0)
        let year: U<yr> = U<yr>("yr", Dim.time, 31557600.0)

        // ---- Mass ------------------------------------------------------------------------

        let dalton: U<Da> = U<Da>("Da", Dim.mass, 1.66053906660e-27)

        // ---- Magnetism -------------------------------------------------------------------

        let gauss: U<Gs> = U<Gs>("G", magneticFluxDensity, 1e-4)

        // ---- Angle -----------------------------------------------------------------------

        /// Degree, π/180 rad: a phase, an angle of incidence, a latitude. An angle as the radian
        /// is, so a degree axis shares with a radian one (converted) and `°/s` does not share
        /// with `Hz`.
        let degree: U<deg> = U<deg>("°", Dim.zero, System.Math.PI / 180.0)

        // ---- Affine units ----------------------------------------------------------------
        //
        // Everything below this line has a non-zero `OffsetSI`, and everything below this line
        // must not be composed.

        /// **Affine — do not compose.** `T/K = T/°C + 273.15`.
        ///
        /// `U.mul` and `U.div` both hard-code `0.0` for the offset of their result, and that is
        /// correct rather than lazy: "°C per second" is a *rate*, and a rate of change of an
        /// affine quantity has no offset — the constant differentiates away. But it means
        /// `U.mul degreeCelsius x` silently produces a unit whose zero is absolute zero, which is
        /// almost never what a caller who wrote it meant. Check `U.isAffine` before composing, or
        /// convert to `U.kelvin` first with `U.convert`.
        ///
        /// A prefix on this unit is meaningless for the same reason (a "milli-°C" scale would have
        /// to rescale the offset too), so `Prefix.selectForUnit` refuses it outright.
        let degreeCelsius: U<degC> = U<degC>("°C", Dim.temperature, 1.0, 273.15)

        /// **Affine — do not compose.** `T/K = (T/°F + 459.67) × 5/9`.
        ///
        /// Same rules as `degreeCelsius`: no products, no quotients, no prefixes. The factor and
        /// the offset are both non-trivial here, which makes it the better regression test of the
        /// two — a witness that only carried `ToSI` would round-trip Celsius by accident and
        /// Fahrenheit never.
        let degreeFahrenheit: U<degF> =
            U<degF>("°F", Dim.temperature, 5.0 / 9.0, 459.67 * 5.0 / 9.0)
