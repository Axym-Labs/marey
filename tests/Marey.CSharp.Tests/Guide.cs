// The C# page of the guide, docs/guide/14-csharp.md, run. Each `csharp` block of the page is a
// method here, line for line, and `ThePageShowsThisCode` fails when the page and this file
// disagree, so the C# a reader copies is the C# that compiles and draws.

using System.Text.RegularExpressions;
using Marey;
using Marey.CSharp;
using Xunit;

namespace Marey.CSharp.Tests;

public class Guide
{
    static readonly object CurrentDirectory = new();

    /// Run a block in a directory of its own, since it writes its files where it stands.
    static void InTemp(Action block)
    {
        lock (CurrentDirectory)
        {
            var before = Directory.GetCurrentDirectory();
            var dir = Directory.CreateTempSubdirectory("marey-guide-cs-");

            try
            {
                Directory.SetCurrentDirectory(dir.FullName);
                block();
            }
            finally
            {
                Directory.SetCurrentDirectory(before);
                dir.Delete(true);
            }
        }
    }

    [Fact]
    public void TheDroppedBall() =>
        InTemp(() =>
        {
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

            var svg = File.ReadAllText("fall.svg");
            Assert.Contains("t [ms]", svg);
            Assert.Contains("x [m]", svg);
            Assert.Contains("light gate", svg);
            Assert.True(new FileInfo("fall.pdf").Length > 0);
        });

    [Fact]
    public void TheBodePlot() =>
        InTemp(() =>
        {
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

            var svg = File.ReadAllText("bode.svg");
            Assert.Contains("f [Hz]", svg);
            Assert.Contains("−π/2", svg);
            Assert.Contains("10⁴", svg);
        });

    /// Every line of every `csharp` block on the page, other than its `using`s, is a line of this
    /// file, in order.
    [Fact]
    public void ThePageShowsThisCode()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Marey.slnx")))
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("no Marey.slnx above the test binary");

        var page = File.ReadAllText(Path.Combine(root, "docs", "guide", "14-csharp.md")).Replace("\r\n", "\n");
        var here = File.ReadAllLines(Path.Combine(root, "tests", "Marey.CSharp.Tests", "Guide.cs")).Select(l => l.Trim()).ToArray();
        var blocks = Regex.Matches(page, @"^```csharp\n(.*?)^```$", RegexOptions.Multiline | RegexOptions.Singleline);

        Assert.Equal(2, blocks.Count);

        var at = 0;

        foreach (var line in blocks.SelectMany(b => b.Groups[1].Value.Split('\n')).Select(l => l.Trim()))
        {
            if (line == "" || line.StartsWith("using ")) continue;

            var found = Array.IndexOf(here, line, at);
            Assert.True(found >= 0, $"the guide's C# has a line this file does not run, or not in this order:\n  {line}");
            at = found + 1;
        }
    }
}
