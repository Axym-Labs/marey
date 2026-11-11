namespace Marey

// ---------------------------------------------------------------------------------------
// The physical constants physics computes with, from CODATA 2022.
//
// Each is a `float<'u>`, so a formula over them is checked as any other: `h * c / wavelength`
// is a `float<J>`. `Constants.Quantities` holds the same values with their witnesses, for
// arithmetic with a `Q`.
//
// The constants that define the SI (c, h, e, k, N_A) are exact by definition, and so is any
// product or quotient of them; those are computed here from the defining five, so that a
// relation between them (R = N_A·k) holds to the last bit a float has. The rest are measured,
// and each doc comment gives CODATA 2022's value with its standard uncertainty, which the float
// does not carry.
//
// The constants physics writes most are named by their symbols, in ASCII: `c`, `h`, `hbar`, `e`,
// `kB`, `NA`, `G`, `eps0`, `mu0`, `me`, `mp`, `mn`. The rest are spelled out, where the symbol
// means more than one thing (`R` is the gas constant and the Rydberg constant, `σ` and `α` are
// used for much else, `g` is any acceleration) or is a unit's name (`F`, `eV`). Under `open
// Marey.Constants` a caller's own `c` or `e` shadows the constant, as any local does; one used by
// mistake in its place is almost always a compile error, since the constant carries its unit. A
// caller who prefers other names binds them once: `let speedOfLight = Constants.c`.
// ---------------------------------------------------------------------------------------

/// **Physical constants**, CODATA 2022: the speed of light, the Planck and Boltzmann constants,
/// the elementary charge, the particle masses and the rest, each a `float` in its SI unit.
///
/// ```fsharp
/// let photonEnergy (wavelength: float<m>) : float<J> = Constants.h * Constants.c / wavelength
/// ```
///
/// The constants written most are named by their symbols: `c`, `h`, `hbar`, `e`, `kB`, `NA`,
/// `G`, `eps0`, `mu0`, `me`, `mp`, `mn`. The rest are spelled out (`molarGas`, `rydberg`,
/// `fineStructure`), where a symbol would be ambiguous.
///
/// The SI's defining constants are exact, and the constants derived from them are computed from
/// them. A measured constant's doc comment gives its standard uncertainty, which the value does
/// not carry. `Constants.Quantities` holds each constant as a `Quantity`, with its witness.
module Constants =

    // ---- The SI's defining constants: exact ------------------------------------------

    /// **Speed of light in vacuum**, c = 299792458 m·s⁻¹, exact: it defines the metre.
    /// CODATA 2022.
    let c: float<m / s> = 299792458.0<m / s>

    /// **Planck constant**, h = 6.62607015 × 10⁻³⁴ J·s, exact: it defines the kilogram.
    /// CODATA 2022.
    let h: float<J s> = 6.62607015e-34<J s>

    /// **Elementary charge**, e = 1.602176634 × 10⁻¹⁹ C, exact: it defines the ampere.
    /// CODATA 2022.
    let e: float<C> = 1.602176634e-19<C>

    /// **Boltzmann constant**, k = 1.380649 × 10⁻²³ J·K⁻¹, exact: it defines the kelvin.
    /// CODATA 2022.
    let kB: float<J / K> = 1.380649e-23<J / K>

    /// **Avogadro constant**, N_A = 6.02214076 × 10²³ mol⁻¹, exact: it defines the mole.
    /// CODATA 2022.
    let NA: float<1 / mol> = 6.02214076e23</mol>

    // ---- Exact, derived from the defining constants ----------------------------------

    /// **Reduced Planck constant**, ħ = h/2π = 1.054571817… × 10⁻³⁴ J·s, exact. The value is
    /// `h / (2π)`. CODATA 2022.
    let hbar: float<J s> = h / (2.0 * System.Math.PI)

    /// **Molar gas constant**, R = N_A·k = 8.314462618… J·mol⁻¹·K⁻¹, exact. The value is
    /// `NA * kB`. CODATA 2022.
    let molarGas: float<J / (mol K)> = NA * kB

    /// **Faraday constant**, F = N_A·e = 96485.33212… C·mol⁻¹, exact. The value is
    /// `NA * e`. CODATA 2022.
    let faraday: float<C / mol> = NA * e

    /// **Stefan–Boltzmann constant**, σ = 2π⁵k⁴/(15h³c²) = 5.670374419… × 10⁻⁸ W·m⁻²·K⁻⁴,
    /// exact. The value is computed from `kB`, `h` and `c`. CODATA 2022.
    let stefanBoltzmann: float<W / (m^2 K^4)> =
        let k2 = kB * kB
        2.0 * System.Math.Pow(System.Math.PI, 5.0) * k2 * k2
        / (15.0 * h * h * h * c * c)

    /// **Wien wavelength displacement law constant**, b = hc/(k·x) = 2.897771955… × 10⁻³ m·K,
    /// exact, where x = 4.965114231744276… is the root of x = 5(1 − e⁻ˣ). The peak of a black
    /// body's spectral radiance per unit wavelength is at λ = b/T. CODATA 2022.
    let wienDisplacement: float<m K> = h * c / (kB * 4.965114231744276)

    /// **Electronvolt**, eV = 1.602176634 × 10⁻¹⁹ J, exact: the energy one elementary charge
    /// gains across one volt. A value in joules; `U.convert U.electronvolt U.joule` reads a
    /// `float<eV>` in joules by the same factor. CODATA 2022.
    let electronvolt: float<J> = 1.602176634e-19<J>

    // ---- Conventional, exact by definition --------------------------------------------

    /// **Standard acceleration of gravity**, g_n = 9.80665 m·s⁻², exact: a conventional value
    /// (3rd CGPM, 1901), not a measured one. CODATA 2022.
    let standardGravity: float<m / s^2> = 9.80665<m / s^2>

    // ---- Measured -----------------------------------------------------------------------

    /// **Newtonian constant of gravitation**, G = 6.67430(15) × 10⁻¹¹ m³·kg⁻¹·s⁻²: standard
    /// uncertainty 0.00015 × 10⁻¹¹, relative 2.2 × 10⁻⁵. CODATA 2022.
    let G: float<m^3 / (kg s^2)> = 6.67430e-11<m^3 / (kg s^2)>

    /// **Fine-structure constant**, α = 7.2973525643(11) × 10⁻³, dimensionless: standard
    /// uncertainty 0.0000000011 × 10⁻³, relative 1.6 × 10⁻¹⁰. 1/α = 137.035999177(21).
    /// CODATA 2022.
    let fineStructure: float = 7.2973525643e-3

    /// **Vacuum magnetic permeability**, μ₀ = 1.25663706127(20) × 10⁻⁶ N·A⁻²: standard uncertainty
    /// 0.00000000020 × 10⁻⁶, relative 1.6 × 10⁻¹⁰. Measured since the 2019 SI, through α; no
    /// longer 4π × 10⁻⁷. N·A⁻² and H·m⁻¹ are one measure. CODATA 2022.
    let mu0: float<N / A^2> = 1.25663706127e-6<N / A^2>

    /// **Vacuum electric permittivity**, ε₀ = 8.8541878188(14) × 10⁻¹² F·m⁻¹: standard
    /// uncertainty 0.0000000014 × 10⁻¹², relative 1.6 × 10⁻¹⁰. ε₀ = 1/(μ₀c²). CODATA 2022.
    let eps0: float<F / m> = 8.8541878188e-12<F / m>

    /// **Electron mass**, m_e = 9.1093837139(28) × 10⁻³¹ kg: standard uncertainty
    /// 0.0000000028 × 10⁻³¹, relative 3.1 × 10⁻¹⁰. CODATA 2022.
    let me: float<kg> = 9.1093837139e-31<kg>

    /// **Proton mass**, m_p = 1.67262192595(52) × 10⁻²⁷ kg: standard uncertainty
    /// 0.00000000052 × 10⁻²⁷, relative 3.1 × 10⁻¹⁰. CODATA 2022.
    let mp: float<kg> = 1.67262192595e-27<kg>

    /// **Neutron mass**, m_n = 1.67492750056(85) × 10⁻²⁷ kg: standard uncertainty
    /// 0.00000000085 × 10⁻²⁷, relative 5.1 × 10⁻¹⁰. CODATA 2022.
    let mn: float<kg> = 1.67492750056e-27<kg>

    /// **Atomic mass constant**, m_u = 1.66053906892(52) × 10⁻²⁷ kg: one twelfth of the mass of
    /// a carbon-12 atom, the unified atomic mass unit u, which is the dalton (`U.dalton`).
    /// Standard uncertainty 0.00000000052 × 10⁻²⁷, relative 3.1 × 10⁻¹⁰. CODATA 2022.
    let atomicMass: float<kg> = 1.66053906892e-27<kg>

    /// **Rydberg constant**, R∞ = 10973731.568157(12) m⁻¹: standard uncertainty 0.000012 m⁻¹,
    /// relative 1.1 × 10⁻¹². CODATA 2022.
    let rydberg: float<1 / m> = 10973731.568157</m>

    /// **Bohr radius**, a₀ = 5.29177210544(82) × 10⁻¹¹ m: standard uncertainty
    /// 0.00000000082 × 10⁻¹¹, relative 1.6 × 10⁻¹⁰. CODATA 2022.
    let bohrRadius: float<m> = 5.29177210544e-11<m>

    /// **Bohr magneton**, μ_B = eħ/2m_e = 9.2740100657(29) × 10⁻²⁴ J·T⁻¹: standard uncertainty
    /// 0.0000000029 × 10⁻²⁴, relative 3.1 × 10⁻¹⁰. CODATA 2022.
    let bohrMagneton: float<J / T> = 9.2740100657e-24<J / T>

    /// The constants as `Quantity` values, each with its witness, for arithmetic with a `Q`:
    /// `Constants.Quantities.c * t`, with `t` a `Q<s>`, is a `Q<m>` that titles its
    /// axis in metres. Each is the `float` of the same name in `Constants` with the witness of
    /// its unit, and its doc comment is there.
    module Quantities =

        /// Speed of light in vacuum, c, in m·s⁻¹. Exact. See `Constants.c`.
        let c: Quantity<m / s> = U.metrePerSecond.Of c

        /// Planck constant, h, in J·s. Exact. See `Constants.h`.
        let h: Quantity<J s> = (U.mul U.joule U.second).Of h

        /// Elementary charge, e, in C. Exact. See `Constants.e`.
        let e: Quantity<C> = U.coulomb.Of e

        /// Boltzmann constant, k, in J·K⁻¹. Exact. See `Constants.kB`.
        let kB: Quantity<J / K> = (U.div U.joule U.kelvin).Of kB

        /// Avogadro constant, N_A, in mol⁻¹. Exact. See `Constants.NA`.
        let NA: Quantity<1 / mol> = (U.recip U.mole).Of NA

        /// Reduced Planck constant, ħ, in J·s. Exact. See `Constants.hbar`.
        let hbar: Quantity<J s> = (U.mul U.joule U.second).Of hbar

        /// Molar gas constant, R, in J·mol⁻¹·K⁻¹. Exact. See `Constants.molarGas`.
        let molarGas: Quantity<J / (mol K)> = (U.div U.joule (U.mul U.mole U.kelvin)).Of molarGas

        /// Faraday constant, F, in C·mol⁻¹. Exact. See `Constants.faraday`.
        let faraday: Quantity<C / mol> = (U.div U.coulomb U.mole).Of faraday

        /// Stefan–Boltzmann constant, σ, in W·m⁻²·K⁻⁴. Exact. See `Constants.stefanBoltzmann`.
        let stefanBoltzmann: Quantity<W / (m^2 K^4)> =
            (U.div U.watt (U.mul U.squareMetre (U.square (U.square U.kelvin)))).Of stefanBoltzmann

        /// Wien wavelength displacement law constant, b, in m·K. Exact. See
        /// `Constants.wienDisplacement`.
        let wienDisplacement: Quantity<m K> = (U.mul U.metre U.kelvin).Of wienDisplacement

        /// Electronvolt, in J. Exact. See `Constants.electronvolt`.
        let electronvolt: Quantity<J> = U.joule.Of electronvolt

        /// Standard acceleration of gravity, g_n, in m·s⁻². Exact. See `Constants.standardGravity`.
        let standardGravity: Quantity<m / s^2> = U.metrePerSecondSquared.Of standardGravity

        /// Newtonian constant of gravitation, G, in m³·kg⁻¹·s⁻². Measured, relative standard
        /// uncertainty 2.2 × 10⁻⁵. See `Constants.G`.
        let G: Quantity<m^3 / (kg s^2)> =
            (U.div U.cubicMetre (U.mul U.kilogram (U.square U.second))).Of G

        /// Fine-structure constant, α, dimensionless. Measured. See `Constants.fineStructure`.
        let fineStructure: Quantity<1> = U.one.Of fineStructure

        /// Vacuum magnetic permeability, μ₀, in H·m⁻¹ (N·A⁻²). Measured. See
        /// `Constants.mu0`.
        let mu0: Quantity<N / A^2> = (U.div U.henry U.metre).Of mu0

        /// Vacuum electric permittivity, ε₀, in F·m⁻¹. Measured. See `Constants.eps0`.
        let eps0: Quantity<F / m> = (U.div U.farad U.metre).Of eps0

        /// Electron mass, m_e, in kg. Measured. See `Constants.me`.
        let me: Quantity<kg> = U.kilogram.Of me

        /// Proton mass, m_p, in kg. Measured. See `Constants.mp`.
        let mp: Quantity<kg> = U.kilogram.Of mp

        /// Neutron mass, m_n, in kg. Measured. See `Constants.mn`.
        let mn: Quantity<kg> = U.kilogram.Of mn

        /// Atomic mass constant, m_u, in kg. Measured. See `Constants.atomicMass`.
        let atomicMass: Quantity<kg> = U.kilogram.Of atomicMass

        /// Rydberg constant, R∞, in m⁻¹. Measured. See `Constants.rydberg`.
        let rydberg: Quantity<1 / m> = (U.recip U.metre).Of rydberg

        /// Bohr radius, a₀, in m. Measured. See `Constants.bohrRadius`.
        let bohrRadius: Quantity<m> = U.metre.Of bohrRadius

        /// Bohr magneton, μ_B, in J·T⁻¹. Measured. See `Constants.bohrMagneton`.
        let bohrMagneton: Quantity<J / T> = (U.div U.joule U.tesla).Of bohrMagneton
