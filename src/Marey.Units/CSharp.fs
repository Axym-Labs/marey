namespace Marey.CSharp

open Marey

/// **Every unit witness, for C#, under one name**: `Units.Metre`, `Units.DegreeCelsius`,
/// `Units.Div(Units.Volt, Units.Metre)`.
///
/// F# reaches the witnesses as `U.metre`, `U.volt` and `U.degreeCelsius`; a C# caller should
/// not read F#'s lowercase as a C# property, nor have to know that F# opens three modules named
/// `U` to put them under one name. Each
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
    static member Degree = U.degree

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

/// The SI prefixes, for C#: `fig.WithYPrefix(Prefixes.Nano)`. `None` writes the unit bare.
[<AbstractClass; Sealed>]
type Prefixes =

    static member None = Prefix.none
    static member Quecto = Prefix.quecto
    static member Ronto = Prefix.ronto
    static member Yocto = Prefix.yocto
    static member Zepto = Prefix.zepto
    static member Atto = Prefix.atto
    static member Femto = Prefix.femto
    static member Pico = Prefix.pico
    static member Nano = Prefix.nano
    static member Micro = Prefix.micro
    static member Milli = Prefix.milli
    static member Centi = Prefix.centi
    static member Deci = Prefix.deci
    static member Deca = Prefix.deca
    static member Hecto = Prefix.hecto
    static member Kilo = Prefix.kilo
    static member Mega = Prefix.mega
    static member Giga = Prefix.giga
    static member Tera = Prefix.tera
    static member Peta = Prefix.peta
    static member Exa = Prefix.exa
    static member Zetta = Prefix.zetta
    static member Yotta = Prefix.yotta
    static member Ronna = Prefix.ronna
    static member Quetta = Prefix.quetta

    /// Every prefix, smallest first.
    static member All = Prefix.all

    /// The prefix a range of values in `unit` would be written with.
    static member For(unit: IUnit, lo: float, hi: float) =
        Prefix.selectForErased unit.Symbol unit.Dim unit.ToSI unit.OffsetSI (lo, hi)

/// **The physical constants of CODATA 2022, for C#**, each a `Quantity` in its SI unit:
/// `PhysicalConstants.Planck * PhysicalConstants.SpeedOfLight / new Quantity(500e-9, Units.Metre)`
/// is a photon's energy, and its `Unit` has the dimension of `Units.Joule`. `.Value` is the bare
/// `double`, in SI.
///
/// F# reaches them as `Constants.c`, `Constants.h` and `Constants.Quantities.c`, named by their
/// symbols. Here each has its name, `SpeedOfLight` and `Planck`, as a C# reader expects, and is
/// F#'s `Constants.Quantities` value itself, not a copy. The doc comment of the F# constant gives
/// the value and, for a measured one, its standard uncertainty.
///
/// Not `Constants`: a C# file with `using Marey;` and `using Marey.CSharp;` would then see two
/// types of that name, F#'s module and this, and every use of either would be ambiguous.
[<AbstractClass; Sealed>]
type PhysicalConstants =

    // ---- The SI's defining constants: exact ------------------------------------------

    /// c, the speed of light in vacuum, in m·s⁻¹. Exact.
    static member SpeedOfLight = Constants.Quantities.c

    /// h, the Planck constant, in J·s. Exact.
    static member Planck = Constants.Quantities.h

    /// e, the elementary charge, in C. Exact.
    static member ElementaryCharge = Constants.Quantities.e

    /// k, the Boltzmann constant, in J·K⁻¹. Exact.
    static member Boltzmann = Constants.Quantities.kB

    /// N_A, the Avogadro constant, in mol⁻¹. Exact.
    static member Avogadro = Constants.Quantities.NA

    // ---- Exact, derived from the defining constants ----------------------------------

    /// ħ = h/2π, the reduced Planck constant, in J·s. Exact.
    static member ReducedPlanck = Constants.Quantities.hbar

    /// R = N_A·k, the molar gas constant, in J·mol⁻¹·K⁻¹. Exact.
    static member MolarGas = Constants.Quantities.molarGas

    /// F = N_A·e, the Faraday constant, in C·mol⁻¹. Exact.
    static member Faraday = Constants.Quantities.faraday

    /// σ, the Stefan–Boltzmann constant, in W·m⁻²·K⁻⁴. Exact.
    static member StefanBoltzmann = Constants.Quantities.stefanBoltzmann

    /// b, the Wien wavelength displacement law constant, in m·K. Exact.
    static member WienDisplacement = Constants.Quantities.wienDisplacement

    /// The electronvolt, in J. Exact. As a unit, for an axis, it is `Units.Electronvolt`.
    static member Electronvolt = Constants.Quantities.electronvolt

    // ---- Conventional, exact by definition --------------------------------------------

    /// g_n, the standard acceleration of gravity, in m·s⁻². Exact, by convention.
    static member StandardGravity = Constants.Quantities.standardGravity

    // ---- Measured -----------------------------------------------------------------------

    /// G, the Newtonian constant of gravitation, in m³·kg⁻¹·s⁻². Measured.
    static member Gravitation = Constants.Quantities.G

    /// α, the fine-structure constant, dimensionless. Measured.
    static member FineStructure = Constants.Quantities.fineStructure

    /// μ₀, the vacuum magnetic permeability, in N·A⁻². Measured.
    static member VacuumPermeability = Constants.Quantities.mu0

    /// ε₀, the vacuum electric permittivity, in F·m⁻¹. Measured.
    static member VacuumPermittivity = Constants.Quantities.eps0

    /// m_e, the electron mass, in kg. Measured.
    static member ElectronMass = Constants.Quantities.me

    /// m_p, the proton mass, in kg. Measured.
    static member ProtonMass = Constants.Quantities.mp

    /// m_n, the neutron mass, in kg. Measured.
    static member NeutronMass = Constants.Quantities.mn

    /// m_u, the atomic mass constant, in kg: the dalton, `Units.Dalton`. Measured.
    static member AtomicMass = Constants.Quantities.atomicMass

    /// R∞, the Rydberg constant, in m⁻¹. Measured.
    static member Rydberg = Constants.Quantities.rydberg

    /// a₀, the Bohr radius, in m. Measured.
    static member BohrRadius = Constants.Quantities.bohrRadius

    /// μ_B, the Bohr magneton, in J·T⁻¹. Measured.
    static member BohrMagneton = Constants.Quantities.bohrMagneton
