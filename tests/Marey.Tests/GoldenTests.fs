/// The golden-image suite.
///
/// Every scene below is chosen to make one part of the SVG backend's behaviour visible in a
/// text diff. Between them they cover all ten `Node` cases, all seven `MarkerShape`s, both
/// gradient kinds, clipping, dashes, rotated text, semantics, and a realistic axis composition.
///
/// **Determinism is the property that makes any of this work.** SvgWriter.fs opens with it:
/// "the same Scene must produce byte-identical output on every machine, every run", and the
/// first hazard it names is a comma-decimal locale emitting `0,5`, "which is not a number SVG
/// can parse — this is a correctness bug, not a cosmetic one". So each golden scene is also
/// rendered twice and rendered again under `de-DE` and `tr-TR`, and all four results must be
/// byte-identical. Without that, a golden baseline is a record of one machine's locale.
module Marey.Tests.GoldenTests

open Xunit
open Marey
open Marey.UnitsOfMeasure
open Marey.Tests.Generators
open Marey.Tests.Golden
open Marey.Tests.GoldenScenes


// ======================================================================================
// THE SNAPSHOTS
// ======================================================================================

module Snapshots =

    [<Fact>]
    let ``paths`` () = render "paths.svg" Scenes.paths

    [<Fact>]
    let ``text`` () = render "text.svg" Scenes.textRuns

    [<Fact>]
    let ``every marker shape`` () = render "markers-shapes.svg" Scenes.markerShapes

    [<Fact>]
    let ``a batched scatter`` () = render "markers-batch.svg" Scenes.markerBatch

    [<Fact>]
    let ``gradients`` () = render "gradients.svg" Scenes.gradients

    [<Fact>]
    let ``dashes, caps and joins`` () = render "dashes.svg" Scenes.dashes

    [<Fact>]
    let ``clipping`` () = render "clipping.svg" Scenes.clipping

    [<Fact>]
    let ``keyed and annotated`` () =
        render "keyed-annotated.svg" Scenes.keyedAndAnnotated

    [<Fact>]
    let ``raster`` () = render "raster.svg" Scenes.raster

    [<Fact>]
    let ``groups and transforms`` () =
        render "groups-transforms.svg" Scenes.groupsAndTransforms

    /// Rendered on a deliberately anisotropic 2:1 surface, which is where D13's rejected
    /// implementation would have differed.
    [<Fact>]
    let ``rotated text on an anisotropic surface`` () =
        renderAt Scenes.anisotropic "rotated-text.svg" Scenes.rotatedText

    [<Fact>]
    let ``a realistic axis composition`` () =
        renderAt Scenes.wide "axis-composition.svg" Scenes.axisComposition

    [<Fact>]
    let ``all ten Node cases`` () =
        assertAllTenCases Scenes.allTenCases
        render "all-ten-nodes.svg" Scenes.allTenCases

    /// M2 phase 6 — `ribbon`, `step`, `tile`, `segment` and `text`, through `Geom.compile`.
    [<Fact>]
    let ``the band geoms`` () = render "geoms-bands.svg" Scenes.phase6Bands

    /// M2 phase 6 — `boxplot` and `violin`, from real `Stat` output.
    [<Fact>]
    let ``the distribution geoms`` () =
        render "geoms-distributions.svg" Scenes.phase6Distributions

// ======================================================================================
// THE FIGURE — gate 6
//
// The gap this closes: every scene above is a hand-built `Node`, and until now `Chart` output was
// checked only by substring assertions. A figure is what users produce and the only artefact that
// exercises layout, axes, ticks, legend and theme at once, and it had no reviewable baseline in
// either backend. The TikZ half is in `TikzGoldenTests`, from the same `Golden.figure`.
// ======================================================================================

module Figures =

    [<Fact>]
    let ``a complete figure`` () =
        check "figure.svg" (Chart.render SvgBackend.render figureSurface figure)

    /// The categorical axis, as a picture. The claim under review in the diff is that the
    /// bands are evenly spaced and the bars sit one band wide inside them.
    [<Fact>]
    let ``a categorical figure`` () =
        check "figure-category.svg" (Chart.render SvgBackend.render figureSurface categoryFigure)

    /// A categorical colour channel. Three levels, three batches, and a legend that
    /// enumerates the levels rather than the series.
    [<Fact>]
    let ``a channelled figure`` () =
        check "figure-channels.svg" (Chart.render SvgBackend.render figureSurface channelFigure)

    /// The colorbar. Its pair in `TikzGoldenTests` is the one that matters: a bar that
    /// degraded to a single flat block would still produce a clean diff *here*.
    [<Fact>]
    let ``a figure with a colorbar`` () =
        check "figure-colorbar.svg" (Chart.render SvgBackend.render figureSurface colorbarFigure)

    /// The box plot as a whole figure: a categorical axis, a summary geom and an outlier.
    [<Fact>]
    let ``a box plot figure`` () =
        check "figure-boxplot.svg" (Chart.render SvgBackend.render figureSurface boxplotFigure)

    /// The tile with the colour channel that makes it mean something, and a colorbar over it.
    [<Fact>]
    let ``a heatmap figure`` () =
        check "figure-heatmap.svg" (Chart.render SvgBackend.render figureSurface heatmapFigure)

    /// The first figure in the suite with a `Raster` in it.
    ///
    /// SVG inlines the pixels as a `data:` URI, so this baseline is self-contained and there is
    /// no sidecar on this path — deliberately, and stated in `RenderedDocument`: a single file
    /// is the property that makes SVG the notebook and golden format. The TikZ half of the pair
    /// is the one that grows a `.png` beside it.
    [<Fact>]
    let ``an image figure`` () =
        check "figure-image.svg" (Chart.render SvgBackend.render figureSurface imageFigure)

    /// A log axis over eleven decades: powers of ten, every one distinct, and `drift [J]` with no
    /// prefix.
    [<Fact>]
    let ``a wide log axis`` () =
        check "figure-log.svg" (Chart.render SvgBackend.render figureSurface logFigure)

    /// A figure is a scene like any other, so it has to be one: if `Figure.toScene` ever emitted
    /// something outside the ten cases this would not compile, and if it stopped exercising them
    /// the golden above would be measuring less than it claims.
    [<Fact>]
    let ``the figure is built from the IR and nothing else`` () =
        let used = casesUsed (Chart.toScene figureSurface figure)

        // Not all ten: a figure has no `Raster` and no `Opacity` — a static figure fades
        // nothing — and no `Clip` is needed for a series that fits.
        // Asserting the *set* rather than a count keeps this honest as layout changes.
        Assert.Contains("Path", used)
        Assert.Contains("Text", used)
        Assert.Contains("Markers", used)
        Assert.Contains("Keyed", used)
        Assert.Contains("Annotated", used)

// ======================================================================================
// DETERMINISM
//
// The property that makes a golden baseline mean anything at all.
// ======================================================================================

module Determinism =

    [<Fact>]
    let ``two renders of the same scene are byte-identical`` () =
        for name, scene, extent in Scenes.all do
            let a = SvgBackend.render extent scene
            let b = SvgBackend.render extent scene
            Assert.True((a = b), name)

    /// SvgWriter.fs's opening requirement, and the bug class the backend was explicitly built to
    /// survive: under `de-DE` a naive `ToString()` writes `0,5`, which SVG cannot parse.
    [<Fact>]
    let ``rendering under a comma-decimal culture is byte-identical`` () =
        for name, scene, extent in Scenes.all do
            let baseline = SvgBackend.render extent scene
            let german = Culture.run Culture.CommaDecimal (fun () -> SvgBackend.render extent scene)
            Assert.True((baseline = german), sprintf "%s differed under %s" name Culture.CommaDecimal)

    /// Turkish, because its dotless-i changes what `ToLowerInvariant`-free code does — and
    /// `ApproxTextMeasurer` lowercases a font family name on the layout path.
    [<Fact>]
    let ``rendering under a dotless-i culture is byte-identical`` () =
        for name, scene, extent in Scenes.all do
            let baseline = SvgBackend.render extent scene
            let turkish = Culture.run Culture.DotlessI (fun () -> SvgBackend.render extent scene)
            Assert.True((baseline = turkish), sprintf "%s differed under %s" name Culture.DotlessI)

    /// One backend instance, reused. "`Begin` resets all state, so the same backend can render a
    /// sequence of scenes, and two renders of the same scene produce byte-identical strings."
    [<Fact>]
    let ``a reused backend instance produces identical output`` () =
        let backend = SvgBackend()
        let sink = backend :> IBackend

        let once scene =
            sink.Begin surface
            sink.Draw scene
            sink.Finish()
            backend.Result

        let first = once Scenes.axisComposition
        let _other = once Scenes.gradients
        let again = once Scenes.axisComposition
        Assert.Equal(first, again)

    /// A hex colour is not a number, and `#009e73` is Okabe–Ito's bluish green.
    ///
    /// The exponent guard below is a search for `digit e digit`, and every six-digit colour in
    /// the palette is a candidate false positive — which is exactly what happened the first time
    /// a golden scene was built out of `Geom.styleFor` rather than out of hand-picked colours.
    /// Stripping the colour literals first is what keeps the guard a claim about *numbers*; the
    /// alternative, picking scene colours that do not contain an `e`, makes the test pass by
    /// avoiding the input it was written to check.
    let private withoutColorLiterals (doc: string) =
        System.Text.RegularExpressions.Regex.Replace(doc, @"#[0-9a-fA-F]{3,8}\b", "#")

    /// No number in the output may carry an exponent: "the SVG number grammar has no exponent in
    /// path data", which is why every number goes through a custom .NET numeric format.
    [<Fact>]
    let ``no rendered document contains scientific notation`` () =
        for name, scene, extent in Scenes.all do
            let doc = withoutColorLiterals (SvgBackend.render extent scene)
            // Guard against digit-E-digit and digit-e-digit, which is what "R"/"%g" would emit.
            let bad =
                System.Text.RegularExpressions.Regex.IsMatch(doc, @"\d[eE][-+]?\d")

            Assert.False(bad, sprintf "%s contains scientific notation" name)

    /// The guard's own guard: a document that really does carry an exponent must still be
    /// caught after the colours are stripped, and a palette colour must not be.
    [<Fact>]
    let ``the exponent guard still sees an exponent and no longer sees a colour`` () =
        let exponential = System.Text.RegularExpressions.Regex.IsMatch(withoutColorLiterals "d=\"M1e-05 2\"", @"\d[eE][-+]?\d")

        let colored =
            System.Text.RegularExpressions.Regex.IsMatch(withoutColorLiterals "fill=\"#009e73\"", @"\d[eE][-+]?\d")

        Assert.True exponential
        Assert.False colored

    /// Tiny coordinates are the case that would trip a `%g`-style format: 1e-5 formats as
    /// "1e-05" under "R" and as "0" under the backend's three-decimal coordinate format.
    [<Fact>]
    let ``sub-resolution coordinates round rather than going exponential`` () =
        let scene =
            Scene.polyline (Stroke.solid Color.black 1.0<px>) [| pt 1e-9 1e-9; pt 2e-9 0.5 |]

        let doc = SvgBackend.render surface scene
        Assert.Contains("M0 200L0 100", doc)

        // Not a bare "e-" search: `stroke-width` contains one. The hazard is a *number* with an
        // exponent, which is what "R" and "%g" produce below 1e-4.
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(doc, @"\d[eE][-+]?\d"))

// ======================================================================================
// BACKEND CONTRACT
//
// Behaviour the golden files show but do not *state*.
// ======================================================================================

module Contract =

    [<Fact>]
    let ``the SVG backend advertises what it can do`` () =
        let caps = (SvgBackend() :> IBackend).Caps

        Assert.True(caps.Gradients && caps.RadialGradients && caps.Clipping && caps.Dashes && caps.Rasters)
        Assert.True(caps.RotatedText && caps.Semantics)
        // Shaping is delegated to whatever opens the file, and the flag says so honestly.
        Assert.False(caps.TextShaping)
        // "a text file has no hardware limit, and one batch stays one batch".
        Assert.True(caps.MaxBatch.IsNone)

    [<Fact>]
    let ``Draw before Begin is refused`` () =
        let sink = SvgBackend() :> IBackend
        Assert.Throws<System.InvalidOperationException>(fun () -> sink.Draw Scene.empty) |> ignore

    [<Fact>]
    let ``Draw after Finish is refused until Begin is called again`` () =
        let backend = SvgBackend()
        let sink = backend :> IBackend
        sink.Begin surface
        sink.Draw Scene.empty
        sink.Finish()

        Assert.Throws<System.InvalidOperationException>(fun () -> sink.Draw Scene.empty)
        |> ignore

        sink.Begin surface
        sink.Draw Scene.empty
        sink.Finish()
        Assert.Contains("<svg", backend.Result)

    [<Fact>]
    let ``Result before Begin is refused`` () =
        let backend = SvgBackend()
        Assert.Throws<System.InvalidOperationException>(fun () -> backend.Result |> ignore) |> ignore

    /// Geometry is pre-transformed and no `transform` attribute is emitted for it. The two
    /// exceptions are text rotation and `gradientTransform`, so a document with neither must
    /// contain no `transform` at all.
    [<Fact>]
    let ``geometry carries no transform attribute`` () =
        let doc = SvgBackend.render surface Scenes.groupsAndTransforms
        Assert.DoesNotContain("transform=", doc)
        Assert.Contains("<path", doc)

    /// D15's consequence: "every `<g>` in the output carries meaning (an id, a clip, or
    /// semantics), because `Group` emits no element of its own". That is what keeps the diffs
    /// readable, so it is worth asserting rather than hoping.
    [<Fact>]
    let ``a plain Group emits no element`` () =
        let plain =
            SvgBackend.render surface (Group [| Group [| Scene.polyline (Stroke.solid Color.black 1.0<px>) [| pt 0.0 0.0; pt 1.0 1.0 |] |] |])

        Assert.DoesNotContain("<g", plain)

    [<Fact>]
    let ``the root transform matches the surface`` () =
        let backend = SvgBackend()
        let sink = backend :> IBackend
        sink.Begin surface
        let t = backend.RootTransform

        let origin = Affine.apply t (pt 0.0 0.0)
        Assert.Equal(0.0, float origin.X, 9)
        Assert.Equal(200.0, float origin.Y, 9)
        Assert.True(float (Affine.determinant t) < 0.0)

    /// The one place `xlink` is used rather than SVG 2's plain `href`, on purpose: "every
    /// renderer in existence understands the xlink form".
    [<Fact>]
    let ``markers reference their symbol through xlink`` () =
        let doc = SvgBackend.render surface Scenes.markerBatch
        Assert.Contains("xlink:href=\"#qm\"", doc)
        // One definition, twenty-four uses.
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(doc, "id=\"qm\"").Count)
        Assert.Equal(24, System.Text.RegularExpressions.Regex.Matches(doc, "<use ").Count)

    [<Fact>]
    let ``identical gradients and clips are defined once`` () =
        let doc = SvgBackend.render surface Scenes.gradients
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(doc, "<linearGradient ").Count)
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(doc, "<radialGradient ").Count)

        let clipDoc = SvgBackend.render surface Scenes.clipping
        // Two identical disc clips collapse to one definition; the empty clip is a second.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(clipDoc, "<clipPath ").Count)

    [<Fact>]
    let ``duplicate keys are disambiguated deterministically`` () =
        let doc = SvgBackend.render surface Scenes.keyedAndAnnotated
        Assert.Contains("id=\"panel\"", doc)
        Assert.Contains("id=\"panel-2\"", doc)
        // A key starting with a digit is not a valid NCName; a leading underscore is prepended
        // rather than the digit being replaced, so the key stays readable.
        Assert.Contains("id=\"_9_starts_with_a_digit\"", doc)

    [<Fact>]
    let ``decoration is aria-hidden and carries no label`` () =
        let doc = SvgBackend.render surface Scenes.keyedAndAnnotated
        Assert.Contains("aria-hidden=\"true\"", doc)
        Assert.DoesNotContain("should not be emitted as a label", doc)
        // But the data survives, because hit-testing still wants it.
        Assert.Contains("data-value=\"42\"", doc)
        Assert.Contains("data-value-2=\"43\"", doc)

    [<Fact>]
    let ``text is escaped and padded runs preserve their whitespace`` () =
        let doc = SvgBackend.render surface Scenes.textRuns
        Assert.Contains("a &amp; b &lt; c &gt;", doc)
        Assert.Contains("xml:space=\"preserve\"", doc)

    /// D13, as a number rather than as a picture: a 30° run on a 2:1 surface must emit
    /// `rotate(-30 ...)`, not the 16.1° the direction-vector implementation would produce.
    [<Fact>]
    let ``a 30 degree run renders as 30 degrees on an anisotropic surface`` () =
        let scene = text "thirty" 0.5 0.5 AnchorMiddle BaselineAlphabetic (System.Math.PI / 6.0)

        let doc =
            SvgBackend.render { Width = 400.0<px>; Height = 200.0<px> } scene

        Assert.Contains("rotate(-30 ", doc)
        Assert.DoesNotContain("rotate(-16", doc)

    [<Fact>]
    let ``the document is a standalone SVG with no XML prolog`` () =
        let doc = SvgBackend.render surface Scenes.paths
        Assert.StartsWith("<svg xmlns=", doc)
        Assert.EndsWith("</svg>\n", doc)
        Assert.DoesNotContain("<?xml", doc)
        Assert.Contains("viewBox=\"0 0 320 200\"", doc)

    /// Every golden document must parse as XML. A golden file that is byte-stable but malformed
    /// is a golden file that records a bug.
    [<Fact>]
    let ``every golden document is well-formed XML`` () =
        for name, scene, extent in Scenes.all do
            let doc = SvgBackend.render extent scene

            let parsed =
                try
                    System.Xml.Linq.XDocument.Parse doc |> ignore
                    None
                with e ->
                    Some e.Message

            Assert.True(parsed.IsNone, sprintf "%s is not well-formed XML: %A" name parsed)

// ======================================================================================
// THE SHARED TABLE — D130
//
// `GoldenScenes.svg` is what the Fable project renders under Node. It is only evidence for the
// claim if it covers every SVG baseline and renders each one as the suite above does, so both
// halves are checked here, where a mismatch names the file.
// ======================================================================================

module SharedTable =

    [<Fact>]
    let ``the shared table names every SVG baseline, once`` () =
        let onDisk =
            System.IO.Directory.GetFiles(goldenDir.Force(), "*.svg")
            |> Array.map System.IO.Path.GetFileNameWithoutExtension
            |> Array.filter (fun n -> not (n.EndsWith ".actual"))
            |> Set.ofArray

        let names = GoldenScenes.svg |> List.map fst
        Assert.Equal(names.Length, (Set.ofList names).Count)
        Assert.Equal<Set<string>>(onDisk, Set.ofList names)

    [<Fact>]
    let ``every entry in the shared table renders its baseline`` () =
        for name, render in GoldenScenes.svg do
            check (name + ".svg") (render ())
