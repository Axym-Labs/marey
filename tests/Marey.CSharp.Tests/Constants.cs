// The physical constants from C#. This file has both `using`s a C# caller writes, and names both
// F#'s module, `Constants`, and the facade, `PhysicalConstants`: were the facade called
// `Constants`, every use of either here would be CS0104, ambiguous, and this file would not build.

using System.Reflection;
using Marey;
using Marey.CSharp;
using Xunit;

namespace Marey.CSharp.Tests;

public class PhysicalConstantsFromCSharp
{
    /// A photon's energy, E = h·c/λ, as C# computes it: the arithmetic carries the unit, so the
    /// result has the dimension of the joule, and the value is F#'s `Constants.h * Constants.c / λ`.
    [Fact]
    public void APhotonsEnergy()
    {
        var wavelength = new Quantity(500e-9, Units.Metre);
        var energy = PhysicalConstants.Planck * PhysicalConstants.SpeedOfLight / wavelength;

        Assert.Equal(Constants.h * Constants.c / 500e-9, energy.Value);
        Assert.Equal(Units.Joule.Dim, energy.Unit.Dim);
        Assert.Equal(2.4797, energy.Value / PhysicalConstants.Electronvolt.Value, 4);
    }

    /// Relations between them hold as F#'s do: R = N_A·k to the last bit, and 1/α within CODATA's
    /// standard uncertainty of 1/α, 0.000000021, since CODATA rounds α and 1/α each on its own.
    [Fact]
    public void RelationsBetweenThem()
    {
        var r = PhysicalConstants.Avogadro * PhysicalConstants.Boltzmann;
        Assert.Equal(PhysicalConstants.MolarGas.Value, r.Value);
        Assert.Equal(PhysicalConstants.MolarGas.Unit.Dim, r.Unit.Dim);

        var inverse = 1.0 / PhysicalConstants.FineStructure;
        Assert.Equal(137.035999177, inverse.Value, 0.000000021);
    }

    /// Each is F#'s `Constants.Quantities` value itself, so its value is F#'s `float` of the same
    /// constant.
    [Fact]
    public void EachIsTheFSharpConstant()
    {
        var pairs = new (Quantity CSharp, Quantity FSharp, double Float)[]
        {
            (PhysicalConstants.SpeedOfLight, Constants.Quantities.c, Constants.c),
            (PhysicalConstants.Planck, Constants.Quantities.h, Constants.h),
            (PhysicalConstants.ElementaryCharge, Constants.Quantities.e, Constants.e),
            (PhysicalConstants.Boltzmann, Constants.Quantities.kB, Constants.kB),
            (PhysicalConstants.Avogadro, Constants.Quantities.NA, Constants.NA),
            (PhysicalConstants.ReducedPlanck, Constants.Quantities.hbar, Constants.hbar),
            (PhysicalConstants.MolarGas, Constants.Quantities.molarGas, Constants.molarGas),
            (PhysicalConstants.Faraday, Constants.Quantities.faraday, Constants.faraday),
            (PhysicalConstants.StefanBoltzmann, Constants.Quantities.stefanBoltzmann, Constants.stefanBoltzmann),
            (PhysicalConstants.WienDisplacement, Constants.Quantities.wienDisplacement, Constants.wienDisplacement),
            (PhysicalConstants.Electronvolt, Constants.Quantities.electronvolt, Constants.electronvolt),
            (PhysicalConstants.StandardGravity, Constants.Quantities.standardGravity, Constants.standardGravity),
            (PhysicalConstants.Gravitation, Constants.Quantities.G, Constants.G),
            (PhysicalConstants.FineStructure, Constants.Quantities.fineStructure, Constants.fineStructure),
            (PhysicalConstants.VacuumPermeability, Constants.Quantities.mu0, Constants.mu0),
            (PhysicalConstants.VacuumPermittivity, Constants.Quantities.eps0, Constants.eps0),
            (PhysicalConstants.ElectronMass, Constants.Quantities.me, Constants.me),
            (PhysicalConstants.ProtonMass, Constants.Quantities.mp, Constants.mp),
            (PhysicalConstants.NeutronMass, Constants.Quantities.mn, Constants.mn),
            (PhysicalConstants.AtomicMass, Constants.Quantities.atomicMass, Constants.atomicMass),
            (PhysicalConstants.Rydberg, Constants.Quantities.rydberg, Constants.rydberg),
            (PhysicalConstants.BohrRadius, Constants.Quantities.bohrRadius, Constants.bohrRadius),
            (PhysicalConstants.BohrMagneton, Constants.Quantities.bohrMagneton, Constants.bohrMagneton),
        };

        foreach (var (cs, fs, f) in pairs)
        {
            Assert.Same(fs, cs);
            Assert.Equal(f, cs.Value);
        }

        // A constant F# gains and C# does not is a count that differs.
        static int Count(Type t) => t.GetProperties(BindingFlags.Public | BindingFlags.Static).Count(p => p.PropertyType == typeof(Quantity));
        Assert.Equal(Count(typeof(Constants.Quantities)), pairs.Length);
        Assert.Equal(Count(typeof(PhysicalConstants)), pairs.Length);
    }

    /// A constant's unit goes to an axis as any witness does.
    [Fact]
    public void OnAnAxis()
    {
        double[] t = { 3000.0, 4000.0, 5000.0, 6000.0 };
        var peak = t.Select(k => PhysicalConstants.WienDisplacement.Value / k).ToArray();
        var svg = Chart.Line(t, peak, xUnit: Units.Kelvin, yUnit: Units.Metre).Render(SvgRenderer.Render, 600, 400);

        Assert.Contains("[nm]", svg);
    }
}
