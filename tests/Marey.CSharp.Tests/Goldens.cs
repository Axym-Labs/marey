// M4.6 gate 8 — the F# goldens, drawn from C#. Each figure here is the one `Golden.fs` builds,
// written as a C# caller writes it: the unit-optional constructors, the `Units` facade, the
// extension methods, and a renderer passed as a method group. The SVG must be byte-identical to
// the baseline F# checks, so the C# surface cannot drift into a second implementation.

using Marey;
using Marey.CSharp;
using Xunit;

namespace Marey.CSharp.Tests;

public class Goldens
{
    const double Width = 480, Height = 300;

    static string Baseline(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Marey.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "tests", "Marey.Tests", "golden", name);
        return File.ReadAllText(path).Replace("\r\n", "\n");
    }

    static void Same(string baseline, Figure fig) =>
        Assert.Equal(Baseline(baseline), fig.Render(SvgRenderer.Render, Width, Height).Replace("\r\n", "\n"));

    static double[] Times(int n) => Enumerable.Range(0, n).Select(i => i * 0.4).ToArray();

    [Fact]
    public void TheCompleteFigure()
    {
        var ts = Times(24);
        var xs = ts.Select(t => 0.5 * 9.81 * t * t).ToArray();
        var early = ts.Take(6).ToArray();

        var fig = Chart.Line(ts, xs, xUnit: Units.Second, yUnit: Units.Metre)
            .WithName("model")
            .Add(Chart.Scatter(early, early.Select(t => 0.5 * 9.6 * t * t).ToArray(), xUnit: Units.Second, yUnit: Units.Metre))
            .WithName("measured")
            .WithTitle("Free fall")
            .WithSubtitle("displacement against elapsed time")
            .WithLegend(LegendPlacement.LegendTopLeft)
            .WithGrid(true, true);

        Same("figure.svg", fig);
    }

    [Fact]
    public void TheCategoricalFigure()
    {
        var fig = Category.Bar(new[] { "control", "treated", "placebo", "washout" }, new[] { 12.4, 19.1, 7.8, 15.0 }, yUnit: Units.Metre)
            .WithXLabel("treatment")
            .WithYLabel("yield")
            .WithTitle("Yield by treatment")
            .WithGrid(false, true);

        Same("figure-category.svg", fig);
    }

    [Fact]
    public void TheChannelledFigure()
    {
        string[] levels = { "control", "treated", "washout" };
        var keys = Enumerable.Range(0, 24).Select(i => levels[i % 3]).ToArray();
        var ts = Times(24);
        var xs = ts.Select(t => 0.5 * 9.81 * t * t).ToArray();

        var fig = Chart.Scatter(ts, xs, xUnit: Units.Second, yUnit: Units.Metre)
            .ColorByKey(keys)
            .ShapeByKey(keys)
            .WithTitle("Fall by treatment")
            .WithLegend(LegendPlacement.LegendTopLeft);

        Same("figure-channels.svg", fig);
    }

    [Fact]
    public void TheColorbarFigure()
    {
        var ts = Times(24);
        var xs = ts.Select(t => 0.5 * 9.81 * t * t).ToArray();
        var temps = ts.Select(t => (280.0 + 2.5 * t) * 1.0).ToArray();

        var fig = Chart.Scatter(ts, xs, xUnit: Units.Second, yUnit: Units.Metre)
            .ColorBy(temps, unit: Units.Kelvin)
            .WithColorLabel("T")
            .WithTitle("Fall by temperature");

        Same("figure-colorbar.svg", fig);
    }
}
