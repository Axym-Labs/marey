// M4.6 gate 8 — every unit-optional entry point, called from C# once without a unit and once
// with one. What fails here is a compile error, not an assertion: CS0433 on a witness, an
// optional parameter C# cannot omit (an `FSharpOption` where an `IUnit` should be), or a member
// C# cannot see. The assertions only say each call drew something, with the bracket a stated
// unit earns.

using Marey;
using Marey.CSharp;
using Xunit;

namespace Marey.CSharp.Tests;

public class Surface
{
    static readonly double[] X = { 0.0, 1.0, 2.0, 3.5, 4.0 };
    static readonly double[] Y = { 0.3, 1.1, 0.7, 2.4, 1.9 };
    static readonly double[] Err = { 0.1, 0.2, 0.1, 0.3, 0.2 };
    static readonly double[] Sample = Enumerable.Range(0, 200).Select(i => (i * 37 % 101) * 0.01).ToArray();

    static string Svg(Figure fig) => fig.Render(SvgRenderer.Render, 600, 400);

    static void Draws(Figure plain, Figure stated, string bracket)
    {
        Assert.DoesNotContain(bracket, Svg(plain));
        Assert.Contains(bracket, Svg(stated));
    }

    [Fact]
    public void EveryChartConstructor()
    {
        var s = Units.Second;
        var m = Units.Metre;
        Draws(Chart.Line(X, Y), Chart.Line(X, Y, xUnit: s, yUnit: m), "[m]");
        Draws(Chart.Scatter(X, Y), Chart.Scatter(X, Y, s, m), "[m]");
        Draws(Chart.Area(X, Y), Chart.Area(X, Y, yUnit: m), "[m]");
        Draws(Chart.Bar(X, Y), Chart.Bar(X, Y, yUnit: m), "[m]");
        Draws(Chart.Errorbar(X, Y, Err), Chart.Errorbar(X, Y, Err, yUnit: m), "[m]");
        Draws(Chart.Step(X, Y), Chart.Step(X, Y, yUnit: m), "[m]");
        Draws(Chart.StepWith(StepAlign.StepBefore, X, Y), Chart.StepWith(StepAlign.StepBefore, X, Y, yUnit: m), "[m]");
        Draws(Chart.Ribbon(X, Y, Err), Chart.Ribbon(X, Y, Err, yUnit: m), "[m]");
        Draws(Chart.Tile(0.5, 0.2, X, Y), Chart.Tile(0.5, 0.2, X, Y, yUnit: m), "[m]");
        Draws(Chart.Segment(X, Y, X, Err), Chart.Segment(X, Y, X, Err, yUnit: m), "[m]");
        Draws(Chart.Text(X, Y, new[] { "a", "b", "c", "d", "e" }), Chart.Text(X, Y, new[] { "a", "b", "c", "d", "e" }, yUnit: m), "[m]");
        Draws(Chart.Fit(X, Y), Chart.Fit(X, Y, yUnit: m), "[m]");
        Draws(Chart.Loess(X, Y), Chart.Loess(X, Y, yUnit: m), "[m]");
        Draws(Chart.LoessWith(0.9, X, Y), Chart.LoessWith(0.9, X, Y, yUnit: m), "[m]");
        Draws(Chart.Hexbin(X, Y), Chart.Hexbin(X, Y, yUnit: m), "[m]");
        Draws(Chart.HexbinWith(4, X, Y), Chart.HexbinWith(4, X, Y, yUnit: m), "[m]");
        Draws(Chart.Histogram(Sample), Chart.Histogram(Sample, unit: m), "[m]");
        Draws(Chart.HistogramWith(7, Sample), Chart.HistogramWith(7, Sample, unit: m), "[m]");
        Draws(Chart.Density(Sample), Chart.Density(Sample, unit: m), "[m⁻¹]");
        Draws(Chart.Ecdf(Sample), Chart.Ecdf(Sample, unit: m), "[m]");
        Draws(Chart.Heatmap(X, Y, Err), Chart.Heatmap(X, Y, Err, valueUnit: Units.Kelvin), "[mK]"); // 0.1 … 0.3 K

        var img = new Image(2, 1, new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 });
        Draws(Chart.Image(img, 0, 0, 2, 1, false), Chart.Image(img, 0, 0, 2, 1, false, yUnit: m), "[m]");

        var series = Chart.Series("s", X, Y, yUnit: m);
        Assert.Equal("s", series.Name);
    }

    [Fact]
    public void TheChannelsAndComposition()
    {
        var fig = Chart.Scatter(X, Y, yUnit: Units.Metre)
            .ColorBy(Err, unit: Units.Kelvin)
            .WithColorDomain(0, 1, unit: Units.Kelvin)
            .Add(Chart.Line(X, Y, yUnit: Units.Metre))
            .SizeBy(Err)
            .WithXLog();

        Assert.Contains("[mK]", Svg(Chart.Scatter(X, Y).ColorByWith(Palette.viridis, Err, Units.Kelvin)));
        Assert.Contains("[m]", Svg(fig));
        Assert.Contains("[m]", Svg(Chart.Line(X, Y).SizeByWith(2.0, 9.0, Err, Units.Metre).Beside(Chart.Line(X, Y, yUnit: Units.Metre))));

        var facet = Figures.Facet(new[] { "one", "two" }, new[] { Chart.Line(X, Y), Chart.Line(X, Err) });
        Assert.Equal(2, facet.Panels.Length);
    }

    /// Rules and spans from C#: built as figures and added, checked against the axis.
    [Fact]
    public void RulesAndSpans()
    {
        var fig = Chart.Line(X, Y, yUnit: Units.Metre)
            .Add(Chart.HLine(0.0, Units.Metre))
            .Add(Chart.VLine(1.0))
            .Add(Chart.HSpan(0.1, 0.2, Units.Metre))
            .WithName("band")
            .Add(Chart.VSpan(0.5, 0.7))
            .Add(Chart.LineThrough(0.0, 0.0, 1.0, yUnit: Units.Metre))
            .Add(Chart.HLine(100.0, Units.Metre))
            .WithDomainIncluded();

        Assert.Contains("[m]", Svg(fig));
        Assert.Contains("band", Svg(fig));
        Assert.ThrowsAny<Exception>(() => Svg(Chart.Line(X, Y, yUnit: Units.Metre).Add(Chart.HLine(1.0, Units.Second))));
    }

    /// Annotations, panel text and letters from C#.
    [Fact]
    public void AnnotationsAndLetters()
    {
        var fig = Chart.Line(X, Y, yUnit: Units.Metre)
            .Add(Chart.Annotation(X[1], Y[1], "peak", yUnit: Units.Metre))
            .WithTextOffset(12.0, 16.0)
            .WithArrow(ArrowStyle.ArrowStraight)
            .Add(Chart.Annotation(X[2], Y[2], "edge"))
            .WithTextAt(X[0], Y[0])
            .WithArrow(ArrowStyle.NewArrowArc(0.3))
            .AddPanelText(0.98, 0.98, "note")
            .Beside(Chart.Line(X, Y))
            .WithPanelLetters(LetterStyle.LetterParen);

        var svg = Svg(fig);
        Assert.Contains("peak", svg);
        Assert.Contains("note", svg);
        Assert.Contains("(b)", svg);
    }

    /// Stated ticks, a prefix and reversal from C#.
    [Fact]
    public void StatedTicks()
    {
        var fig = Chart.Line(new[] { 400e-9, 700e-9 }, new[] { 0.1, 0.9 }, xUnit: Units.Metre)
            .WithXDomain(400e-9, 700e-9)
            .WithXTicksEvery(50e-9, Units.Metre)
            .WithYTicksNamed(new[] { 0.1, 0.9 }, new[] { "low", "high" })
            .WithYReversed();

        var svg = Svg(fig);
        Assert.Contains("[nm]", svg);
        Assert.Contains(">450<", svg);
        Assert.Contains(">high<", svg);
        Assert.Contains("[m]", Svg(Chart.Line(X, Y, yUnit: Units.Metre).WithYPrefix(PrefixModule.none)));
        Assert.ThrowsAny<Exception>(() => Svg(fig.WithXTicksEvery(1.0, Units.Second)));
    }

    /// Minor ticks and the tick conventions of a theme, from C#.
    [Fact]
    public void MinorTicksAndTickDirection()
    {
        var screen = ThemeModule.withMinorTicks(true, ThemeModule.withTickDirection(TickDirection.TickIn, ThemeModule.modern));
        var fig = Chart.Line(X, Y, yUnit: Units.Metre)
            .WithXMinorTicks(MinorSpec.NewMinorCount(4))
            .WithYMinorTicks(MinorSpec.MinorOff)
            .WithTheme(screen);

        Assert.Contains("[m]", Svg(fig));
        Assert.Contains("[m]", Svg(Chart.Line(X, Y, yUnit: Units.Metre).WithTheme(ThemeModule.publication)));
    }

    /// Tick formats from C#, a custom one from a lambda.
    [Fact]
    public void TickFormats()
    {
        var fig = Chart.Line(X, new[] { 0.0, 0.001, 0.004, 0.002, 0.003 }, yUnit: Units.Metre)
            .WithYTickFormat(TickFormat.FormatScientific)
            .WithXTickFormat(v => $"<{v}>");

        var svg = Svg(fig);
        Assert.Contains("10⁻³·m", svg);
        Assert.Contains("&lt;", svg);
    }

    /// Legends outside, in columns, without a frame, and shared by a figure, from C#.
    [Fact]
    public void Legends()
    {
        var one = Chart.Line(X, Y, yUnit: Units.Metre).WithName("model")
            .Add(Chart.Scatter(X, Y, yUnit: Units.Metre)).WithName("measured");

        Assert.Contains("measured", Svg(one.WithLegend(LegendPlacement.LegendOutsideBottom).WithLegendColumns(2).WithLegendFrame(false)));
        Assert.Contains("measured", Svg(one.WithLegend(LegendPlacement.LegendBest)));
        Assert.Contains("measured", Svg(one.Beside(one).WithFigureLegend(FigureLegend.FigureLegendRight)));
    }

    [Fact]
    public void TheCategoricalConstructors()
    {
        var keys = new[] { "a", "b", "a", "c", "b", "c", "a" };
        var values = new[] { 1.2, 2.5, 1.9, 3.1, 2.2, 2.8, 1.4 };
        Draws(Category.Bar(keys, values), Category.Bar(keys, values, yUnit: Units.Metre), "[m]");
        Draws(Category.Scatter(keys, values), Category.Scatter(keys, values, Units.Metre), "[m]");
        Draws(Category.Boxplot(keys, values), Category.Boxplot(keys, values, Units.Metre), "[m]");
        Draws(Category.Violin(keys, values), Category.Violin(keys, values, Units.Metre), "[m]");
        Assert.Single(Category.Count(keys).Panels);
    }

    /// The facade is the F# witness, not a copy: `Units.Metre` and F#'s `U.metre` are one object.
    [Fact]
    public void TheFacadeIsTheWitness()
    {
        Assert.Same(SiWitnesses.U.metre, Units.Metre);
        Assert.Same(DerivedWitnesses.U.volt, Units.Volt);
        Assert.Same(NonSiWitnesses.U.degreeCelsius, Units.DegreeCelsius);
        Assert.False(Units.Unspecified.Stated);
        Assert.True(Units.One.Stated);

        var field = Units.Div(Units.Volt, Units.Metre);
        Assert.Contains("V·m⁻¹", Svg(Chart.Line(X, Y, yUnit: field)));
        Assert.Equal(1000.0, Units.Scaled("km", 1000.0, Units.Metre).ToSI);
    }

    [Fact]
    public void FieldsAndPhasePortraits()
    {
        var xs = Grids.Linear(-1, 1, 9);
        var scalar = Grids.Scalar(xs, xs, (x, y) => 280 + 10 * x * y);
        var flow = Grids.Vector(xs, xs, (x, y) => (y, -x - 0.3 * y));
        var m = Units.Metre;

        Draws(Field.Heatmap(scalar), Field.Heatmap(scalar, m, m, Units.Kelvin), "[K]");
        Draws(Field.Contours(scalar), Field.Contours(scalar, m, m), "[m]");
        Draws(Field.Bands(scalar), Field.Bands(scalar, m, m, Units.Kelvin), "[m]");
        Draws(Field.Quiver(flow), Field.Quiver(flow, m, m), "[m]");
        Draws(Field.Streamlines(flow), Field.Streamlines(flow, m, m), "[m]");
        Draws(Field.Lic(flow), Field.Lic(flow, m, m), "[m]");
        Draws(Phase.Portrait(flow), Phase.Portrait(flow, m, m), "[m]");
        Assert.Contains("[m]", Svg(Field.Heatmap(Grids.Laplacian(scalar), m, m)));

        var points = Grids.FixedPoints(flow);
        Assert.NotEmpty(points);
        Assert.NotEmpty(Phase.Describe(points[0], m, m, Units.Second));
    }

    [Fact]
    public void AControlOverPlainNumbers()
    {
        var slider = Control.Slider("T", 200, 400, 300, unit: Units.Kelvin);
        var spec = Assert.Single(slider.Specs);
        Assert.Equal("T", spec.Label);
        Assert.Contains("K", spec.Show.Invoke(spec.Init));
    }

    /// Every renderer is a method group C# can hand to `Save`, and each writes its own format.
    [Fact]
    public void EveryRendererSaves()
    {
        var fig = Chart.Line(X, Y, yUnit: Units.Metre);
        var dir = Directory.CreateTempSubdirectory("marey-csharp-");

        fig.Save(SvgRenderer.Render, Path.Combine(dir.FullName, "f.svg"), 400, 300);
        fig.Save(TikzRenderer.Render, Path.Combine(dir.FullName, "f.tex"), 400, 300);
        fig.Save(PngRenderer.Render, Path.Combine(dir.FullName, "f.png"), 400, 300);
        fig.Save(PdfRenderer.Render, Path.Combine(dir.FullName, "f.pdf"), 400, 300);

        Assert.StartsWith("<svg", File.ReadAllText(Path.Combine(dir.FullName, "f.svg")).TrimStart());
        Assert.Contains("tikzpicture", File.ReadAllText(Path.Combine(dir.FullName, "f.tex")));
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, File.ReadAllBytes(Path.Combine(dir.FullName, "f.png")).Take(4));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(dir.FullName, "f.pdf")), 0, 4));
        dir.Delete(true);
    }

    /// D110 from C#: the spelling a unit read out of a file header arrives in.
    [Fact]
    public void AUnitFromText()
    {
        var parsed = Units.Parse("V/m");
        Assert.Equal(Svg(Chart.Line(X, Y, yUnit: Units.Div(Units.Volt, Units.Metre))), Svg(Chart.Line(X, Y, yUnit: parsed)));
        Assert.True(Units.TryParse("kg m s-2", out var newton));
        Assert.Equal(Units.Newton.Dim, newton.Dim);
        Assert.False(Units.TryParse("days since 1970-01-01", out _));
        Assert.Throws<FormatException>(() => Units.Parse("furlong"));
    }

    /// An axis's look from C#, which cannot spell an `AxisConfig` record update. The drawn title goes;
    /// the axis's `aria-label` keeps it for a screen reader.
    /// Composition from C#: one call from each group — spans and mosaics, ratios, sharing, a
    /// twin y axis, secondary axes, aspect and insets — so a missing spelling fails the build.
    [Fact]
    public void Composition()
    {
        var a = Chart.Line(X, Y, xUnit: Units.Second, yUnit: Units.Metre);
        var b = Chart.Scatter(X, Err, xUnit: Units.Second, yUnit: Units.Metre);

        var grid = a.Above(b).WithRowHeights(3.0, 1.0).ShareX().Beside(a).WithColWidths(2.0, 1.0);
        Assert.Equal(3, grid.Panels.Length);
        Assert.Equal(3, Figures.Mosaic("AB;AC", "ABC", a, b, a).Panels.Length);

        Assert.Contains("[K]", Svg(a.WithRightAxis(Chart.Line(X, Y, xUnit: Units.Second, yUnit: Units.Kelvin))));
        Assert.Contains("[min]", Svg(a.WithTopAxisIn(Units.Minute)));
        Assert.Contains(">period [", Svg(a.WithTopAxisMapped(Units.Second, t => 1.0 / (t + 1.0), p => 1.0 / p - 1.0).WithTopAxisLabel("period")));
        Assert.Contains("[mK]", Svg(a.Add(Chart.Line(X, Err, xUnit: Units.Second, yUnit: Units.Kelvin)).ToRightAxis()));

        Assert.NotEmpty(Svg(Chart.Line(X, Y, xUnit: Units.Metre, yUnit: Units.Metre).WithEqualAspect()));
        Assert.NotEmpty(Svg(a.WithBoxAspect(1.0)));
        Assert.Single(a.WithInset(0.6, 0.6, 0.95, 0.95, b).WithInsetZoom().Panels[0].Insets);
    }

    /// The facade's names for what F# reaches through modules C# sees with a `Module` suffix:
    /// themes, coordinate systems, prefixes, a whole axis configuration and a scale kind.
    [Fact]
    public void TheFacadesNames()
    {
        var a = Chart.Line(X, Y, xUnit: Units.Second, yUnit: Units.Metre);
        Assert.Same(Themes.Publication, a.WithTheme(Themes.Publication).Theme);
        Assert.Equal(6, Themes.All.Length);
        Assert.NotEmpty(Svg(Chart.Line(X, Y).WithCoord(Coords.Polar)));
        Assert.NotEmpty(Svg(Chart.Line(X, Y).WithCoord(Coords.PolarBetween(0.0, Math.PI, 0.3))));
        Assert.Contains("[km]", Svg(a.WithYPrefix(Prefixes.Kilo)));
        Assert.Equal(Prefixes.Nano, Prefixes.For(Units.Metre, 1e-9, 5e-9));
        Assert.Contains(">time [s]<", Svg(a.WithXAxis(Figures.Axis("time"))));
        Assert.NotEmpty(Svg(a.WithYScaleKind(ScaleKind.NewLog(10.0))));
        Assert.NotEmpty(Svg(a.Beside(a).WithPanelXDomain(0, 1, 0.0, 10.0).WithPanelYDomain(0, 0, 0.0, 5.0)));
    }

    [Fact]
    public void AnyEnumerable()
    {
        var fromArrays = Svg(Chart.Line(X, Y, xUnit: Units.Second));
        Assert.Equal(fromArrays, Svg(Chart.Line(new List<double>(X), Y.Select(v => v), xUnit: Units.Second)));
        Assert.Equal(Svg(Chart.Histogram(Sample)), Svg(Chart.Histogram(Sample.ToList())));
    }

    [Fact]
    public void AnAxisLook()
    {
        var fig = Chart.Line(X, Y, xUnit: Units.Second, yUnit: Units.Metre);
        Assert.Contains(">t [s]<", Svg(fig));
        Assert.DoesNotContain(">t [s]<", Svg(fig.WithAxesLook(AxisLook.AxisUntitled)));
        Assert.DoesNotContain(">t [s]<", Svg(fig.WithXLook(AxisLook.AxisHidden)));
        Assert.Contains(">x [m]<", Svg(fig.WithXLook(AxisLook.AxisHidden)));
    }
}
