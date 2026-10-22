# 14. From C#

C# has no units of measure, so it has the unit-optional mode: a chart takes any
`IEnumerable<double>` (an array, a `List<double>`, a LINQ query), and each axis a unit as an
optional named argument. A stated unit gives the axis what a witness gives it in F#, its title,
prefix and the check when two layers meet, at the moment the figure is drawn. The
witnesses and their algebra are under `Units`, the combinators are extension methods on `Figure`,
and the themes, coordinate systems and prefixes are `Themes`, `Coords` and `Prefixes`, all in the
`Marey.CSharp` namespace. The renderers are `SvgRenderer`, `TikzRenderer`, `PngRenderer` and
`PdfRenderer`.

The blocks on this page are not run by the F# session. `tests/Marey.CSharp.Tests/Guide.cs` holds
each of them, runs them, and fails if this page and that file disagree.

§1's dropped ball:

```csharp
using Marey;
using Marey.CSharp;

double[] tGate = { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6 };
double[] xGate = { 0.051, 0.193, 0.447, 0.779, 1.232, 1.759 };
double[] xErr = tGate.Select(_ => 0.02).ToArray();
double[] tModel = Enumerable.Range(0, 61).Select(i => i * 0.01).ToArray();
double[] xModel = tModel.Select(t => 0.5 * 9.81 * t * t).ToArray();

var fall = Chart.Line(tModel, xModel, xUnit: Units.Second, yUnit: Units.Metre)
    .WithName("½ g t²")
    .Add(Chart.Errorbar(tGate, xGate, xErr, xUnit: Units.Second, yUnit: Units.Metre))
    .WithName("light gate")
    .WithTitle("A dropped ball")
    .WithLegend(LegendPlacement.LegendBest);

fall.Save(SvgRenderer.Render, "fall.svg", 720, 450);
fall.Save(PdfRenderer.Render, "fall.pdf", 720, 450);
```

§4's Bode plot, with its phase on the right:

```csharp
double cutoff = 1000.0, zeta = 0.15;
double[] f = Enumerable.Range(0, 241).Select(i => 10.0 * Math.Pow(10.0, i / 60.0)).ToArray();
double[] gain = f.Select(v => 1.0 / Math.Sqrt(Math.Pow(1 - v * v / (cutoff * cutoff), 2) + Math.Pow(2 * zeta * v / cutoff, 2))).ToArray();
double[] phase = f.Select(v => -Math.Atan2(2 * zeta * v / cutoff, 1 - v * v / (cutoff * cutoff))).ToArray();

var phaseSide = Chart.Line(f, phase, xUnit: Units.Hertz, yUnit: Units.Radian)
    .WithName("phase")
    .WithXLog()
    .WithYTicksNamed(new[] { 0.0, -Math.PI / 2, -Math.PI }, new[] { "0", "−π/2", "−π" }, Units.Radian);

var bode = Chart.Line(f, gain, xUnit: Units.Hertz, yUnit: Units.One)
    .WithName("gain")
    .Add(Chart.VLine(cutoff, Units.Hertz))
    .WithXLog()
    .WithYLog()
    .WithRightAxis(phaseSide)
    .WithXLabel("f")
    .WithYLabel("|H|")
    .WithRightYLabel("φ")
    .WithLegend(LegendPlacement.LegendBottomLeft)
    .WithTitle("A second-order low-pass filter, ζ = 0.15");

bode.Save(SvgRenderer.Render, "bode.svg", 720, 450);
```

A unit known only as text is `Units.Parse("V/m")`, or `Units.Div(Units.Volt, Units.Metre)`.
`Category` puts categories on the x axis, `Figures.Facet` draws small multiples, and `Grids` samples
a field from a delegate for §7's marks.
