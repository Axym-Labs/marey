/// The physical constants: their values against CODATA 2022, the relations between them, and
/// their units.
///
/// The units are checked by the compiler, not by a test that runs: every binding in
/// `UnitsCompile` names the measure a constant must have, so a constant declared in the wrong
/// unit stops this file compiling. `WrongUnits` checks the other half, that a formula in the
/// wrong unit does not compile, through `dotnet fsi`.
module Marey.Tests.ConstantsTests

open System
open Xunit
open Marey
open Marey.UnitsOfMeasure

/// Within `tol` of each other, relative to the larger magnitude, with no absolute floor: the
/// constants are far from 1 either way.
let private close (tol: float) (a: float) (b: float) = abs (a - b) <= tol * max (abs a) (abs b)

let private assertClose tol (expected: float) (actual: float) =
    Assert.True(close tol expected actual, sprintf "expected %.17g, got %.17g (relative tolerance %g)" expected actual tol)

/// Each constant at the measure it must have. A binding, so a wrong unit is a compile error.
module UnitsCompile =

    let speedOfLight: float<m / s> = Constants.speedOfLight
    let planck: float<J / Hz> = Constants.planck
    let reducedPlanck: float<J s> = Constants.reducedPlanck
    let elementaryCharge: float<A s> = Constants.elementaryCharge
    let boltzmann: float<J / K> = Constants.boltzmann
    let avogadro: float<1 / mol> = Constants.avogadro
    let molarGas: float<J / mol / K> = Constants.molarGas
    let faraday: float<C / mol> = Constants.faraday
    let stefanBoltzmann: float<W / m^2 / K^4> = Constants.stefanBoltzmann
    let wienDisplacement: float<m K> = Constants.wienDisplacement
    let electronvolt: float<J> = Constants.electronvolt
    let standardGravity: float<m / s^2> = Constants.standardGravity
    let gravitational: float<N m^2 / kg^2> = Constants.gravitational
    let fineStructure: float = Constants.fineStructure
    let vacuumPermeability: float<H / m> = Constants.vacuumPermeability
    let vacuumPermittivity: float<C^2 / (N m^2)> = Constants.vacuumPermittivity
    let electronMass: float<kg> = Constants.electronMass
    let protonMass: float<kg> = Constants.protonMass
    let neutronMass: float<kg> = Constants.neutronMass
    let atomicMass: float<kg> = Constants.atomicMass
    let rydberg: float<1 / m> = Constants.rydberg
    let bohrRadius: float<m> = Constants.bohrRadius
    let bohrMagneton: float<A m^2> = Constants.bohrMagneton

    /// Formulas over the constants type as physics says: E = hc/λ is an energy, and α built from
    /// e, ε₀, ħ and c has no unit.
    let photonEnergy (wavelength: float<m>) : float<J> = Constants.planck * Constants.speedOfLight / wavelength

    let fineStructureFromCharge: float =
        Constants.elementaryCharge * Constants.elementaryCharge
        / (4.0 * Math.PI * Constants.vacuumPermittivity * Constants.reducedPlanck * Constants.speedOfLight)

    /// A quantity times a `Q` is a `Q` in the product's unit.
    let distance (t: Q<s>) : Q<m> = Constants.Quantities.speedOfLight * t

[<Fact>]
let ``the defining constants are exact`` () =
    Assert.Equal(299792458.0, float Constants.speedOfLight)
    Assert.Equal(6.62607015e-34, float Constants.planck)
    Assert.Equal(1.602176634e-19, float Constants.elementaryCharge)
    Assert.Equal(1.380649e-23, float Constants.boltzmann)
    Assert.Equal(6.02214076e23, float Constants.avogadro)
    Assert.Equal(1.602176634e-19, float Constants.electronvolt)
    Assert.Equal(9.80665, float Constants.standardGravity)

/// CODATA 2022's printed values, to every digit it prints. The exact derived constants are printed
/// with an ellipsis, so they agree to that many digits.
[<Fact>]
let ``the derived exact constants match CODATA 2022 to its printed digits`` () =
    assertClose 1e-9 1.054571817e-34 (float Constants.reducedPlanck)
    assertClose 1e-10 8.314462618 (float Constants.molarGas)
    assertClose 1e-10 96485.33212 (float Constants.faraday)
    assertClose 1e-9 5.670374419e-8 (float Constants.stefanBoltzmann)
    assertClose 1e-9 2.897771955e-3 (float Constants.wienDisplacement)

[<Fact>]
let ``the measured constants are CODATA 2022's values`` () =
    Assert.Equal(6.67430e-11, float Constants.gravitational)
    Assert.Equal(7.2973525643e-3, Constants.fineStructure)
    Assert.Equal(1.25663706127e-6, float Constants.vacuumPermeability)
    Assert.Equal(8.8541878188e-12, float Constants.vacuumPermittivity)
    Assert.Equal(9.1093837139e-31, float Constants.electronMass)
    Assert.Equal(1.67262192595e-27, float Constants.protonMass)
    Assert.Equal(1.67492750056e-27, float Constants.neutronMass)
    Assert.Equal(1.66053906892e-27, float Constants.atomicMass)
    Assert.Equal(10973731.568157, float Constants.rydberg)
    Assert.Equal(5.29177210544e-11, float Constants.bohrRadius)
    Assert.Equal(9.2740100657e-24, float Constants.bohrMagneton)

/// The exact relations hold to a float's rounding, since the derived constants are computed.
[<Fact>]
let ``the exact derived constants follow from the defining ones`` () =
    assertClose 1e-15 (float Constants.planck) (float (2.0 * Math.PI * Constants.reducedPlanck))
    assertClose 1e-15 (float (Constants.avogadro * Constants.boltzmann)) (float Constants.molarGas)
    assertClose 1e-15 (float (Constants.avogadro * Constants.elementaryCharge)) (float Constants.faraday)
    assertClose 1e-15 (float Constants.elementaryCharge) (float Constants.electronvolt)

    let k = float Constants.boltzmann
    let h = float Constants.planck
    let c = float Constants.speedOfLight
    assertClose 1e-14 (2.0 * Math.PI ** 5.0 * k ** 4.0 / (15.0 * h ** 3.0 * c ** 2.0)) (float Constants.stefanBoltzmann)

/// Wien's x is the root of x = 5(1 − e⁻ˣ), and b = hc/(kx).
[<Fact>]
let ``the Wien constant is hc over k times the root of its equation`` () =
    let x = 4.965114231744276
    Assert.True(abs (x - 5.0 * (1.0 - exp (-x))) < 1e-14)
    let b = float (Constants.planck * Constants.speedOfLight / Constants.boltzmann) / x
    assertClose 1e-15 b (float Constants.wienDisplacement)

/// The measured constants are not independent: CODATA adjusts them together, so the relations
/// between them hold to well within their uncertainties (all about 10⁻¹⁰ relative, or less).
[<Fact>]
let ``the measured constants agree with each other within their uncertainties`` () =
    let c = Constants.speedOfLight
    let hbar = Constants.reducedPlanck
    let e = Constants.elementaryCharge
    let me = Constants.electronMass
    let alpha = Constants.fineStructure

    // ε₀μ₀c² = 1, to the eleven digits CODATA prints of each
    assertClose 1e-11 1.0 (float (Constants.vacuumPermittivity * Constants.vacuumPermeability * c * c))
    // α = e²/(4πε₀ħc)
    assertClose 1e-9 alpha UnitsCompile.fineStructureFromCharge
    // R∞ = α²m_e c/2h
    assertClose 1e-9 (float Constants.rydberg) (float (alpha * alpha * me * c / (2.0 * Constants.planck)))
    // a₀ = ħ/(αm_e c)
    assertClose 1e-9 (float Constants.bohrRadius) (float (hbar / (alpha * me * c)))
    // μ_B = eħ/2m_e
    assertClose 1e-9 (float Constants.bohrMagneton) (float (e * hbar / (2.0 * me)))

/// `U.dalton` is the atomic mass constant, so a mass in daltons converts with it.
[<Fact>]
let ``the dalton is the atomic mass constant`` () =
    Assert.Equal(float Constants.atomicMass, U.dalton.ToSI)
    Assert.Equal(float Constants.electronvolt, U.electronvolt.ToSI)

/// Each `Quantity` is the float of the same name with a witness of the constant's dimension.
[<Fact>]
let ``each quantity is its float with the witness of its dimension`` () =
    let d l m t i th n =
        { L = l; M = m; T = t; I = i; Th = th; N = n; J = 0 }

    let check (value: float<'u>) (q: Quantity<'u>) (dim: Dim) =
        Assert.Equal(float value, float q.Value)
        Assert.Equal(1.0, q.Unit.ToSI)
        Assert.Equal(dim, q.Unit.Dim)

    check Constants.speedOfLight Constants.Quantities.speedOfLight (d 1 0 -1 0 0 0)
    check Constants.planck Constants.Quantities.planck (d 2 1 -1 0 0 0)
    check Constants.reducedPlanck Constants.Quantities.reducedPlanck (d 2 1 -1 0 0 0)
    check Constants.elementaryCharge Constants.Quantities.elementaryCharge (d 0 0 1 1 0 0)
    check Constants.boltzmann Constants.Quantities.boltzmann (d 2 1 -2 0 -1 0)
    check Constants.avogadro Constants.Quantities.avogadro (d 0 0 0 0 0 -1)
    check Constants.molarGas Constants.Quantities.molarGas (d 2 1 -2 0 -1 -1)
    check Constants.faraday Constants.Quantities.faraday (d 0 0 1 1 0 -1)
    check Constants.stefanBoltzmann Constants.Quantities.stefanBoltzmann (d 0 1 -3 0 -4 0)
    check Constants.wienDisplacement Constants.Quantities.wienDisplacement (d 1 0 0 0 1 0)
    check Constants.electronvolt Constants.Quantities.electronvolt (d 2 1 -2 0 0 0)
    check Constants.standardGravity Constants.Quantities.standardGravity (d 1 0 -2 0 0 0)
    check Constants.gravitational Constants.Quantities.gravitational (d 3 -1 -2 0 0 0)
    check Constants.fineStructure Constants.Quantities.fineStructure Dim.zero
    check Constants.vacuumPermeability Constants.Quantities.vacuumPermeability (d 1 1 -2 -2 0 0)
    check Constants.vacuumPermittivity Constants.Quantities.vacuumPermittivity (d -3 -1 4 2 0 0)
    check Constants.electronMass Constants.Quantities.electronMass (d 0 1 0 0 0 0)
    check Constants.protonMass Constants.Quantities.protonMass (d 0 1 0 0 0 0)
    check Constants.neutronMass Constants.Quantities.neutronMass (d 0 1 0 0 0 0)
    check Constants.atomicMass Constants.Quantities.atomicMass (d 0 1 0 0 0 0)
    check Constants.rydberg Constants.Quantities.rydberg (d -1 0 0 0 0 0)
    check Constants.bohrRadius Constants.Quantities.bohrRadius (d 1 0 0 0 0 0)
    check Constants.bohrMagneton Constants.Quantities.bohrMagneton (d 2 0 0 1 0 0)

/// A `Q` times a constant's quantity carries the product's witness to the axis: c·t over a few
/// nanoseconds reads millimetres.
[<Fact>]
let ``a quantity's witness reaches a chart's axis`` () =
    let t = U.second.Of [ 0.0<s>; 1e-9<s>; 2e-9<s>; 3e-9<s> ]
    let x = UnitsCompile.distance t
    Assert.Equal(Dim.length, x.Unit.Dim)
    Assert.Equal(float Constants.speedOfLight * 2e-9, float x.[2])

    let svg = Chart.line t x |> Chart.render SvgBackend.render (Chart.size 400.0 300.0)
    Assert.Contains("[ns]", svg)
    Assert.Contains("[mm]", svg)

/// A formula over the constants in the wrong unit is a compile error, and `open Marey.Constants`
/// shadows nothing a script uses: the hour and the farad stay measures, and `min` a function.
module WrongUnits =

    open System.IO
    open System.Text

    let private compile (name: string) (body: string) =
        let tool = Tools.dotnet.Force()

        if not tool.IsAvailable then
            None
        else
            let binDir = Path.GetDirectoryName(Uri(typeof<Marey.Dim>.Assembly.Location).LocalPath)
            let sb = StringBuilder()

            for dll in [ "Marey.Core"; "Marey.Units" ] do
                sb.AppendLine(sprintf "#r @\"%s\"" (Path.Combine(binDir, dll + ".dll"))) |> ignore

            sb.AppendLine("open Marey").AppendLine("open Marey.Constants") |> ignore
            let path = Path.Combine(Path.GetTempPath(), sprintf "marey-constants-%s.fsx" name)
            File.WriteAllText(path, sb.ToString() + body + "\n", UTF8Encoding false)

            match Toolchain.run tool (sprintf "fsi --nologo \"%s\"" path) (Path.GetTempPath()) 600_000 with
            | ToolUnavailable _ -> None
            | ToolTimedOut why -> failwith why
            | ToolRan run -> Some(run.ExitCode, path, run.Out + "\n" + run.Error)

    let private skipped () = Console.WriteLine("SKIPPED: dotnet not found on PATH")

    /// The positive control: without it, a bad `#r` makes the negative green.
    [<Fact>]
    let ``a formula in the right unit compiles under open Marey.Constants`` () =
        let body =
            "let energy: float<J> = planck * speedOfLight / 500e-9<m>\n"
            + "let capacitance: float<F> = vacuumPermittivity * 1.0<m>\n"
            + "let shift: float<h> = 2.0<h>\n"
            + "printfn \"%g %g %g %d\" (float energy) (float capacitance) (float shift) (min 1 2)"

        match compile "right" body with
        | None -> skipped ()
        | Some(code, path, output) -> Assert.True((code = 0), sprintf "did not compile: %s\n%s" path output)

    [<Fact>]
    let ``a formula in the wrong unit does not compile`` () =
        match compile "wrong" "let energy: float<J> = planck * speedOfLight" with
        | None -> skipped ()
        | Some(code, path, output) ->
            Assert.True(code <> 0, sprintf "h·c compiled as an energy: %s" path)
            Assert.Contains("error FS0001", output)
