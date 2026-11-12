// Marey.Uncertainty from C#: the inputs, the arithmetic through `Uncertain`'s own operators, the
// reports, a fit, a Monte Carlo check and the charts, each giving what F# gives for the same values.
// The pendulum is the one the Fable gate checks, so the strings here are .NET's and Node's too.

using Marey;
using Marey.CSharp;
using Xunit;

namespace Marey.CSharp.Tests;

public class UncertaintyFromCSharp
{
    static string Svg(Figure fig) => fig.Render(SvgRenderer.Render, 600, 400);

    static Uncertain Pendulum()
    {
        var l = Uncertainty.Resolution("Pendulum length", 1.000, 1e-3, Units.Metre);
        var t = Uncertainty.OfReadings("Period", new[] { 2.01, 2.00, 2.01 }, Units.Second);
        var calibration = Uncertainty.Certificate("Stopwatch calibration", 0.0, 0.01, 2.0, Units.Second);
        return 4.0 * Math.PI * Math.PI * l / ((t + calibration) * (t + calibration));
    }

    [Fact]
    public void APendulumReportsAsFSharpDoes()
    {
        var g = Pendulum();
        Assert.Equal("9.804(59) m·s⁻²", g.Report());
        Assert.Equal("9.804(59) m·s⁻²", g.ToString());
        Assert.Equal("(9.804 ± 0.059) m·s⁻²", g.Report(new ReportFormat(ReportForm.PlusMinus, UnitStyle.Unicode, 2)));

        var budget = string.Join("\n", new[]
        {
            "Input                  Value            ν     Type  Contribution  Share",
            "Stopwatch calibration  0.0000(50) s     ∞     B     0.049 m·s⁻²   69.1 %",
            "Period                 2.0067(33) s     2     A     0.033 m·s⁻²   30.7 %",
            "Pendulum length        1.00000(29) m    ∞     B     0.0028 m·s⁻²  0.2 %",
            "Combined               9.804(59) m·s⁻²  21.2        0.059 m·s⁻²",
        });
        Assert.Equal(budget, g.Budget().Text());
        Assert.Contains("\\begin{tabular}", g.Budget().Latex());
    }

    [Fact]
    public void ExpandedAndDegreesOfFreedom()
    {
        var mass = Uncertainty.Standard("m_S", 100.02147, 0.00035, Units.Scaled("g", 1e-3, Units.Kilogram)).WithDof(9.0);
        Assert.Equal("(100.02147 ± 0.00079) g (k = 2.26, 95 %, ν = 9)", mass.Expanded(0.95).Report());
        Assert.Equal(2.26, mass.Expanded(0.95).CoverageFactor, 2);
        Assert.Equal(1.96, Uncertainty.NormalFactor(0.95), 2);
        Assert.Equal(2.26, Uncertainty.StudentFactor(0.95, 9.0), 2);
        Assert.True(mass.Dof().IsKnown);
    }

    [Fact]
    public void ASharedInputCancels()
    {
        var calibration = Uncertainty.Standard("Calibration", 0.0, 0.01, Units.Metre);
        var a = Uncertainty.Standard("Reading a", 1.20, 0.002, Units.Metre) + calibration;
        var b = Uncertainty.Standard("Reading b", 1.00, 0.002, Units.Metre) + calibration;
        var d = a - b;
        Assert.Equal(0.20, d.Value, 12);
        Assert.Equal(Math.Sqrt(2) * 0.002, d.Uncertainty, 12);
        Assert.Equal(0.0, (a - a).Uncertainty);
        Assert.True(Uncertainty.Correlation(a, b) > 0.9);
        Assert.Equal(2, a.Contributions().Length);
    }

    /// F# refuses a sum of metres and seconds at compile time; C#, which has no measures, at run time.
    [Fact]
    public void ASumOfDifferentDimensionsThrows()
    {
        var x = Uncertainty.Standard("x", 1.0, 0.1, Units.Metre);
        var t = Uncertainty.Standard("t", 1.0, 0.1, Units.Second);
        Assert.ThrowsAny<Exception>(() => x + t);
        var y = Uncertainty.Standard("y", 99.0, 1.0, Units.Scaled("cm", 0.01, Units.Metre));
        Assert.Equal("1.99(10) m", (x + y).Report());
    }

    [Fact]
    public void TheMathFunctionsAreUncertainsOwn()
    {
        var x = Uncertainty.Standard("x", 4.0, 0.2);
        Assert.Equal(2.0, Uncertain.Sqrt(x).Value, 12);
        Assert.Equal(0.05, Uncertain.Sqrt(x).Uncertainty, 12);
        Assert.Equal(Math.Exp(4.0) * 0.2, Uncertain.Exp(x).Uncertainty, 9);
        var y = Uncertainty.Apply(v => v * v, x);
        Assert.Equal(1.6, y.Uncertainty, 9);
    }

    [Fact]
    public void AWeightedLineAndItsBand()
    {
        double[] t = { 0.0, 1.0, 2.0, 3.0, 4.0 };
        double[] x = { 0.10, 1.12, 1.98, 3.05, 3.96 };
        var tx = Uncertainty.Exactly(t, Units.Second);
        var xs = x.Select((v, i) => Uncertainty.Standard($"x{i}", v, 0.05, Units.Metre)).ToArray();
        var fit = Uncertainty.FitLine(tx, xs);
        Assert.Equal(0.965, fit.Slope.Value, 9);
        Assert.Equal(3, fit.Dof);
        Assert.Equal("m/s", fit.Slope.Unit.Symbol);

        var fig = Uncertainty.Uncertain(tx, xs).AddFitBand(fit);
        var svg = Svg(fig);
        Assert.Contains("[m]", svg);
        Assert.Contains("standard uncertainty", svg);
        Assert.Contains("[s]", Svg(Uncertainty.FitBand(fit, ErrorBars.NewCoverage(0.95))));
        Assert.Contains("[m]", Svg(Chart.Line(t, x, yUnit: Units.Metre).AddUncertain(tx, xs, ErrorBars.NewCoverage(0.95))));
    }

    [Fact]
    public void AMonteCarloCheck()
    {
        var a = Uncertainty.Standard("a", 1.0, 0.1);
        var b = Uncertainty.Standard("b", 2.0, 0.1);
        var model = Uncertainty.Model((x, y) => x + y, a, b);
        var check = Uncertainty.Check(model, new MonteCarloOptions(20_000, false, 1, 0.95, 1));
        Assert.Equal(20_000, check.Trials);
        Assert.True(check.Validated);
        Assert.StartsWith("Monte Carlo, 20000 trials", check.Report());
        Assert.Contains("<svg", Svg(Uncertainty.MonteCarloChart(check)));
    }

    [Fact]
    public void TheMeasuredConstants()
    {
        Assert.Equal("9.1093837139(28) × 10⁻³¹ kg", UncertainConstants.ElectronMass.Report());
        Assert.Equal("6.67430(15) × 10⁻¹¹ m³·kg⁻¹·s⁻²", UncertainConstants.Gravitation.Report());
        var ratio = UncertainConstants.ProtonMass / UncertainConstants.ElectronMass;
        Assert.InRange(ratio.Relative(), 1e-11, 3e-11);
        Assert.Equal(-1.0, Uncertainty.Correlation(UncertainConstants.VacuumPermeability, UncertainConstants.VacuumPermittivity), 9);
        Assert.Equal(0.0, (UncertainConstants.BohrMagneton / UncertainConstants.BohrMagneton).Uncertainty);
    }
}
