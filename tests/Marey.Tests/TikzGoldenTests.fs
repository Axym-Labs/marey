/// The TikZ golden suite — the M1 plan §4 gates 1, 5 and 6.
///
/// Every scene the SVG suite renders is rendered again here, from the **same** `Scenes.all` table
/// at the **same** surface, so `golden/paths.svg` and `golden/paths.tex` describe one picture in
/// two languages and can be read against each other. That is what makes the pair worth committing
/// rather than two unrelated snapshots that happen to share a name.
///
/// Three things this suite checks that the SVG one cannot:
///
/// * **Degradation is visible as text.** `Capabilities.Gradients = false`, so `gradients.tex`
///   shows the Oklab-midpoint solid D10 promises — both ramps collapsing to the same colour,
///   which is the cost stated plainly. `raster.tex` shows an empty picture where two images were
///   for a different reason after D64: the capability is now `true`, but `TikzBackend.render`
///   returns a `string` and degrades rasters away because a string cannot carry a sidecar. D10's
///   first real exercise, recorded in files a reviewer reads.
/// * **Every baseline compiles.** The SVG suite's oracle is `XDocument.Parse`; the only authority
///   on a `.tex` file is a TeX engine, so one runs over all thirteen.
/// * **A figure has a baseline at all** — in either backend. See `Golden.figure`.
module Marey.Tests.TikzGoldenTests

open Xunit
open Marey
open Marey.UnitsOfMeasure
open Marey.Tests.Golden
open Marey.Tests.GoldenTests
open Marey.Tests.GoldenScenes

// ======================================================================================
// THE SNAPSHOTS
//
// One `Fact` per scene rather than a loop, so a failure names the picture that changed and the
// thirteen baselines can be regenerated and reviewed independently.
// ======================================================================================

module Snapshots =

    let private tikz (name: string) =
        let _, scene, extent =
            Scenes.all |> List.find (fun (n, _, _) -> n = name)

        renderTikzAt extent (name + ".tex") scene

    [<Fact>]
    let ``paths`` () = tikz "paths"

    [<Fact>]
    let ``text`` () = tikz "text"

    [<Fact>]
    let ``every marker shape`` () = tikz "markers-shapes"

    [<Fact>]
    let ``a batched scatter`` () = tikz "markers-batch"

    /// D31 and D10: this backend cannot do gradients, so what lands in the baseline is the
    /// degraded form — and the diff is where a reviewer sees which colour was chosen.
    [<Fact>]
    let ``gradients degraded to solids`` () = tikz "gradients"

    [<Fact>]
    let ``dashes, caps and joins`` () = tikz "dashes"

    [<Fact>]
    let ``clipping`` () = tikz "clipping"

    /// §5.3's structure has no `aria-label` to land in, so it survives as `%` comments (D31).
    [<Fact>]
    let ``keyed and annotated`` () = tikz "keyed-annotated"

    /// The baseline is still a **bare bounding box**, and after D64 that is a statement about the
    /// *entry point* rather than about TeX.
    ///
    /// `TikzBackend.render` returns a `string`, and a string has nowhere to put a sidecar `.png`,
    /// so it degrades with `Rasters = false` of its own and the node goes away — which is why
    /// this baseline did not move when the capability flipped. The picture a *reader* of this
    /// file sees is unchanged; what changed is that there is now a second door out of the
    /// backend, and it draws the image.
    ///
    /// The consequence for a user of `render` is real and accepted, and it is the same one as
    /// before: an image in a `.tex` produced this way disappears without a trace. The fix is to
    /// call `renderWithAssets` or `renderToFileWithAssets`, which is what the test below pins.
    [<Fact>]
    let ``rasters degraded away on the string-shaped path`` () = tikz "raster"

    /// Both doors out of the backend, on one scene, so neither claim can drift into a comment
    /// that is no longer true. This test read
    /// `the raster skip comment is unreachable through render and reachable through Draw` until
    /// M2.5 — the second half inverted, and the first half is why the baseline above is
    /// byte-identical across the change.
    [<Fact>]
    let ``a raster is absent through render and present through renderWithAssets`` () =
        let _, scene, extent = Scenes.all |> List.find (fun (n, _, _) -> n = "raster")

        // Through the string-shaped entry point: degraded first, so no trace survives.
        Assert.DoesNotContain("raster", TikzBackend.render extent scene)

        // Through the asset-shaped one: two images in the fixture, two sidecars, and the
        // document refers to both by the names the backend chose.
        let rendered = TikzBackend.renderWithAssets extent scene
        Assert.Equal(2, rendered.Assets.Length)

        for asset in rendered.Assets do
            Assert.Contains("\\includegraphics[width=", rendered.Document)
            Assert.Contains(asset.Name, rendered.Document)

        // Deterministic in name *and* order (D64): the names are positions in traversal order,
        // so a reordering would be visible here rather than in a golden diff months later.
        Assert.Equal<string[]>([| "marey-raster-0.png"; "marey-raster-1.png" |], rendered.Assets |> Array.map (fun a -> a.Name))

    /// The same figure twice. A sidecar whose bytes or whose name moved between renders would
    /// make `renderToFileWithAssets` non-reproducible in a way no `.tex` diff would show.
    [<Fact>]
    let ``a rendered document and its assets are byte-identical across renders`` () =
        let _, scene, extent = Scenes.all |> List.find (fun (n, _, _) -> n = "raster")
        let a = TikzBackend.renderWithAssets extent scene
        let b = TikzBackend.renderWithAssets extent scene

        Assert.Equal(a.Document, b.Document)
        Assert.Equal<string[]>(a.Assets |> Array.map (fun x -> x.Name), b.Assets |> Array.map (fun x -> x.Name))

        for (x, y) in Array.zip a.Assets b.Assets do
            Assert.Equal<byte[]>(x.Bytes, y.Bytes)

    [<Fact>]
    let ``groups and transforms`` () = tikz "groups-transforms"

    /// D29's payoff. The SVG backend negates a run's rotation because its y flip mirrors the
    /// plane; this one does not, because TeX's canvas is y-up like `nd`. The two baselines
    /// therefore carry *differently signed* angles for the same picture, which is the clearest
    /// possible record that the two derivations were independent.
    [<Fact>]
    let ``rotated text on an anisotropic surface`` () = tikz "rotated-text"

    [<Fact>]
    let ``a realistic axis composition`` () = tikz "axis-composition"

    [<Fact>]
    let ``all ten Node cases`` () = tikz "all-ten-nodes"

    /// M2 phase 6, gate 5. The pair of this and `geoms-bands.svg` is the reviewable half of
    /// "cross-backend geometry agreement, extended to every new geom" — `GeomTests` asserts the
    /// coordinate relationship numerically, and these two files are where it can be *seen*.
    [<Fact>]
    let ``the band geoms`` () = tikz "geoms-bands"

    [<Fact>]
    let ``the distribution geoms`` () = tikz "geoms-distributions"

// ======================================================================================
// THE FIGURE — gate 6
// ======================================================================================

module Figures =

    /// The same figure as `GoldenTests.Figures`, through the other backend. Reviewing the two
    /// diffs side by side is the cheapest way to see a layout change described twice.
    [<Fact>]
    let ``a complete figure`` () =
        check "figure.tex" (Chart.render TikzBackend.render figureSurface figure)

    /// The categorical figure through the other backend. Level names are ordinary text in both,
    /// so the pair is also the check that a category survives TeX escaping — which is the half
    /// of a categorical axis that has no counterpart on a numeric one.
    [<Fact>]
    let ``a categorical figure`` () =
        check "figure-category.tex" (Chart.render TikzBackend.render figureSurface categoryFigure)

    /// The channelled figure through the other backend. Shape is the encoding that survives
    /// greyscale, so a pair of baselines is also the check that both backends draw the same
    /// three shapes for the same three levels.
    [<Fact>]
    let ``a channelled figure`` () =
        check "figure-channels.tex" (Chart.render TikzBackend.render figureSurface channelFigure)

    /// **D49's third exit criterion, and the only gate that would have caught the gradient.**
    ///
    /// A `LinearGradient`-filled colorbar comes through this backend as one flat `#23908c`
    /// rectangle: `Gradients = false`, so `Degrade.forCaps` collapses the ramp to its Oklab
    /// midpoint. The document stays valid, the diff stays clean, and the bar is gone. What this
    /// baseline records is a column of individually coloured strips — countable in the file.
    [<Fact>]
    let ``a figure with a colorbar`` () =
        check "figure-colorbar.tex" (Chart.render TikzBackend.render figureSurface colorbarFigure)

    /// The claim above, as an assertion rather than as a reviewer's attention: the bar in the
    /// TeX document really does carry many distinct colours. One would mean it had degraded.
    [<Fact>]
    let ``the colorbar reaches TeX as strips, not as one flat block`` () =
        let tex = Chart.render TikzBackend.render figureSurface colorbarFigure

        let colors =
            System.Text.RegularExpressions.Regex.Matches(tex, @"[0-9a-fA-F]{6}")
            |> Seq.map (fun m -> m.Value.ToLowerInvariant())
            |> Set.ofSeq

        Assert.True(colors.Count > 100, sprintf "only %d distinct colours in the document" colors.Count)

    /// D55 — the box plot through the other backend. The outlier is a `\marey@mark` and the
    /// whiskers are a stroked path, so the pair also records that a summary geom degrades to
    /// nothing on a backend with no gradients and no rasters: there is nothing in it to degrade.
    [<Fact>]
    let ``a box plot figure`` () =
        check "figure-boxplot.tex" (Chart.render TikzBackend.render figureSurface boxplotFigure)

    /// The heatmap through the other backend, and the one place a tile's batching is visible as
    /// file length: one path per quantised colour step (D46), not one per cell.
    [<Fact>]
    let ``a heatmap figure`` () =
        check "figure-heatmap.tex" (Chart.render TikzBackend.render figureSurface heatmapFigure)

    /// **The golden pair D64 is really about: a `.tex` baseline and its sidecar `.png`.**
    ///
    /// Two files, checked together, because either alone is a document that cannot be drawn. The
    /// `.tex` names a file; the `.png` is that file; and `renderToFileWithAssets` is what puts
    /// them in one directory. A reviewer can read the first and open the second.
    ///
    /// The `.png` is a committed *binary* baseline, which §13 otherwise argues against, and the
    /// reason it is admissible here is narrow: these bytes come from Marey's own zero-dependency
    /// encoder over a hand-written pixel array, so nothing outside this repository can move
    /// them. It is also the only place the encoder's actual output is checked — it has been in
    /// the tree since M1 and every test of it until now went through a base64 `data:` URI.
    [<Fact>]
    let ``an image figure, and the sidecar it refers to`` () =
        let rendered = Chart.render TikzBackend.renderWithAssets figureSurface imageFigure

        check "figure-image.tex" rendered.Document

        let asset = Assert.Single rendered.Assets
        Assert.Contains(asset.Name, rendered.Document)
        checkBytes "figure-image.png" asset.Bytes

    /// Gate 6 meets gate 7 for the sidecar path: the pair is compiled by a real engine with the
    /// `.png` written beside the `.tex`, and the page is looked at.
    ///
    /// Two ways this fails that no string test can see. `\\includegraphics` pointing at a file
    /// nobody wrote is a TeX error, which the engine reports here. An image placed correctly but
    /// drawn upside down is a valid document with ink in the right box — so the fixture's top
    /// row is bright and its bottom row dark, and the test compares the two halves.
    [<Fact>]
    let ``the image pair compiles, and the image is the right way up`` () =
        let rendered = Chart.render TikzBackend.renderWithAssets figureSurface imageFigure

        let standalone =
            { rendered with Document = TikzBackend.standalone rendered.Document }

        match Raster.ofRendered "golden-figure-image" standalone with
        | TexToolchain.PdfUnavailable _ -> System.Console.WriteLine("SKIPPED: " + Raster.report ())
        | TexToolchain.PdfFailed why -> failwithf "the image pair did not compile:\n%s" why
        | TexToolchain.PdfProduced page ->
            // The panel, loosely — the image fills the data area, which is inside the axes.
            let upper: Raster.Region =
                { Left = 0.25
                  Top = 0.15
                  Right = 0.95
                  Bottom = 0.40 }

            let lower =
                { upper with
                    Top = 0.55
                    Bottom = 0.80 }

            let above = Raster.meanLevel page upper
            let below = Raster.meanLevel page lower

            // The image is bright on top and dark underneath, and a raster's y grows *downward*
            // — so on a correctly oriented page the upper region is the lighter one. A y flip
            // applied wrongly anywhere between `Chart.image` and `\\includegraphics` inverts
            // this, and nothing else in the suite would notice.
            Assert.True(
                above > below + 20.0,
                sprintf "the image is upside down or missing: upper mean %.1f, lower mean %.1f" above below
            )

            // And it is an image rather than an empty box: a blank page is uniformly 255.
            Assert.True(below < 200.0, sprintf "no dark pixels where the image's dark half belongs (mean %.1f)" below)

    /// Axis titles carrying the non-ASCII characters a quantity name uses: Greek, `×`, `°`, `µ`,
    /// `·`, `−`, `≤`, `∂`, a superscript and a subscript. Under `Theme.tikz` a title is TeX source
    /// (`TitleTex`) rather than text, so it does not pass through the backend's escaping; until
    /// `Tex.escape` mapped these, a raw `Δ` reached `pdflatex` and stopped it.
    let symbolFigure: Figure =
        let ts = Array.init 24 (fun i -> float i * 0.4<s>)
        let es = ts |> Array.map (fun t -> 1.5e-3<J / s^2> * t * t)

        Chart.line U.second Derived.joule ts es
        |> Chart.withXLabel "ωt − φ₀ at 20 °C, µ·τ² ≤ ∂θ"
        |> Chart.withYLabel "|ΔE| × 10³"
        |> Chart.withLegend LegendNone
        |> Chart.withTheme Theme.tikz

    [<Fact>]
    let ``axis titles with Greek and symbols under Theme tikz`` () =
        check "figure-symbols.tex" (Chart.render TikzBackend.render figureSurface symbolFigure)

    /// The milestone's headline as a committed artefact rather than a substring assertion:
    /// `Theme.tikz` alone turns the unit witnesses into siunitx markup. TeX-only, because
    /// `\qty{}{\metre}` in an SVG would be nonsense.
    [<Fact>]
    let ``a complete figure under Theme tikz`` () =
        check "figure-siunitx.tex" (figure |> Chart.withTheme Theme.tikz |> Chart.render TikzBackend.render figureSurface)

    /// The wide log axis under `Theme.tikz`, so its ticks are the TeX `$10^{-8}$` a Siunitx axis
    /// carries in `TickLabel.LabelTex` rather than the Unicode `10⁻⁸` escaped as text.
    [<Fact>]
    let ``a wide log axis under Theme tikz`` () =
        check "figure-log-siunitx.tex" (logFigure |> Chart.withTheme Theme.tikz |> Chart.render TikzBackend.render figureSurface)

    /// D162 — rules, spans and a polar circle in TeX: dashed strokes, a filled band at a tenth,
    /// and the sampled arc.
    [<Fact>]
    let ``rules and spans`` () =
        check "figure-rules.tex" (Chart.render TikzBackend.render rulesSurface rulesFigure)

    /// D164 / D165 — arrows straight and arced, text past the plot area, and panel letters.
    [<Fact>]
    let ``annotations and panel letters`` () =
        check "figure-annotate.tex" (Chart.render TikzBackend.render annotateSurface annotateFigure)

    /// D166 / D169 / D170 — a stated step, named ticks, a reversed axis and a pinned prefix.
    [<Fact>]
    let ``stated ticks`` () =
        check "figure-ticks.tex" (Chart.render TikzBackend.render ticksSurface ticksFigure)

    /// D167 / D168 — the publication box: frame, inward and mirrored ticks, minor ticks.
    [<Fact>]
    let ``the publication theme`` () =
        check "figure-publication.tex" (Chart.render TikzBackend.render publicationSurface publicationFigure)

    /// D171 — legends outside, in columns, in the emptiest corner, and shared by a figure.
    [<Fact>]
    let ``legends`` () =
        check "figure-legends.tex" (Chart.render TikzBackend.render legendsSurface legendsFigure)
        check "figure-legend-shared.tex" (Chart.render TikzBackend.render sharedLegendSurface sharedLegendFigure)

    /// D174 — a panel spanning two rows of a mosaic.
    [<Fact>]
    let ``a mosaic`` () =
        check "figure-mosaic.tex" (Chart.render TikzBackend.render mosaicSurface mosaicFigure)

    /// D173, D175, D176 — P1 with one x, three to one.
    [<Fact>]
    let ``a shared axis over ratios`` () =
        check "figure-shared.tex" (Chart.render TikzBackend.render sharedSurface sharedFigure)

    /// D177 — two y axes, each in its series' colour.
    [<Fact>]
    let ``a twin y`` () =
        check "figure-twin.tex" (Chart.render TikzBackend.render twinSurface twinFigure)

    /// Gate 6 meets gate 7. A baseline can be byte-stable, valid TeX and *blank* — that is
    /// precisely the failure this milestone already shipped once. So the committed figure is also
    /// compiled and looked at, which is what makes it a record of a picture rather than of a
    /// string.
    ///
    /// Both baselines, because the siunitx one goes through a different typesetting path for its
    /// axis titles and `\qty{}{...}` producing an empty box is a completely plausible way for the
    /// headline feature to fail silently.
    [<Fact>]
    let ``both figure baselines have ink in them`` () =
        let ofDoc name doc =
            match Raster.ofDocument name (TikzBackend.standalone doc) with
            | TexToolchain.PdfProduced g -> Some g
            | TexToolchain.PdfUnavailable _ -> None
            | TexToolchain.PdfFailed why -> failwithf "%s: %s" name why

        let plain = ofDoc "golden-figure" (Chart.render TikzBackend.render figureSurface figure)

        let siunitx =
            ofDoc "golden-figure-siunitx" (figure |> Chart.withTheme Theme.tikz |> Chart.render TikzBackend.render figureSurface)

        match plain, siunitx with
        | Some plain, Some siunitx ->
            // The panel is painted white by the theme, so "not blank" here is a claim about the
            // strokes and glyphs on top of it rather than about the background.
            for name, g in [ "figure.tex", plain; "figure-siunitx.tex", siunitx ] do
                let c = Raster.coverage g

                Assert.True(
                    c > 0.002 && c < 0.40,
                    sprintf "%s rasterises to %.3f%% ink, which is a blank page or a smear.\n\n%s" name c (Raster.describe g)
                )

            // The axis titles are the only thing that differs between the two documents, and they
            // are set in different languages — so the two pages must both be inked and must not be
            // the *same* page.
            Assert.True(
                (Raster.coverage plain <> Raster.coverage siunitx),
                "the siunitx figure rasterises identically to the plain one, so `Theme.tikz` changed nothing that typesets"
            )
        | _ -> System.Console.WriteLine("SKIPPED: " + Raster.report ())

// ======================================================================================
// DETERMINISM
//
// The same four properties the SVG suite asserts, for the same reason: a baseline that is not
// reproducible is a record of one machine's locale rather than of the backend's behaviour. TeX is
// *stricter* than SVG about this — it cannot read `0,5` as a dimension at all, and it raises a
// hard error rather than reading a wrong number.
// ======================================================================================

module Determinism =

    [<Fact>]
    let ``two renders of the same scene are byte-identical`` () =
        for name, scene, extent in Scenes.all do
            let a = TikzBackend.render extent scene
            let b = TikzBackend.render extent scene
            Assert.True((a = b), name)

    [<Fact>]
    let ``rendering under a comma-decimal culture is byte-identical`` () =
        for name, scene, extent in Scenes.all do
            let baseline = TikzBackend.render extent scene

            let german =
                Generators.Culture.run Generators.Culture.CommaDecimal (fun () -> TikzBackend.render extent scene)

            Assert.True((baseline = german), sprintf "%s differed under %s" name Generators.Culture.CommaDecimal)

    [<Fact>]
    let ``rendering under a dotless-i culture is byte-identical`` () =
        for name, scene, extent in Scenes.all do
            let baseline = TikzBackend.render extent scene

            let turkish =
                Generators.Culture.run Generators.Culture.DotlessI (fun () -> TikzBackend.render extent scene)

            Assert.True((baseline = turkish), sprintf "%s differed under %s" name Generators.Culture.DotlessI)

    /// TeX cannot interpret `1e-05` as a dimension — it is a hard error, not a wrong number — so
    /// this is a stronger requirement here than in SVG, where the same string is merely unparseable
    /// inside path data.
    [<Fact>]
    let ``no rendered document contains scientific notation`` () =
        for name, scene, extent in Scenes.all do
            let doc = TikzBackend.render extent scene

            let bad =
                System.Text.RegularExpressions.Regex.IsMatch(doc, @"\d[eE][-+]?\d")

            Assert.False(bad, sprintf "%s contains scientific notation" name)

// ======================================================================================
// THE COMPILE GATE — gate 1, over the baselines
//
// All thirteen pictures go into **one** document. Thirteen separate `pdflatex` runs would cost
// thirteen seconds to answer a question one run answers, and the one thing a per-picture run buys
// — knowing which picture broke — is bought back below by re-compiling individually *only on
// failure*. The fast path is fast and the slow path is precise.
// ======================================================================================

module ``the baselines compile`` =

    /// Every baseline picture, separated by `\par` so the engine does not run them together into
    /// one unbreakable horizontal box wider than a page.
    let private everything =
        Scenes.all
        |> List.map (fun (name, scene, extent) ->
            String.concat "" [ "\\par\\noindent %% "; name; "\n"; TikzBackend.render extent scene ])
        |> String.concat "\n"

    /// The `standalone` class crops to its content, which for thirteen stacked pictures means a
    /// very tall page — harmless, and preferable to `article` silently clipping an overfull one.
    let private document (body: string) =
        String.concat
            ""
            [ "\\documentclass{standalone}\n"
              TikzBackend.Preamble
              "\\begin{document}\n"
              "\\begin{minipage}{2000bp}\n"
              body
              "\n\\end{minipage}\n\\end{document}\n" ]

    [<Fact>]
    let ``every golden picture is valid TeX`` () =
        match TexToolchain.compile "goldens" (document everything) with
        | TexToolchain.Unavailable -> System.Console.WriteLine("SKIPPED: " + TexToolchain.report ())
        | TexToolchain.Compiled -> ()
        | TexToolchain.Failed combined ->
            // Attribute the failure. One extra run per picture, on the failing path only.
            let culprits =
                Scenes.all
                |> List.choose (fun (name, scene, extent) ->
                    match TexToolchain.compile name (document (TikzBackend.render extent scene)) with
                    | TexToolchain.Failed why -> Some(sprintf "  %s:\n%s" name why)
                    | _ -> None)

            let detail =
                if culprits.IsEmpty then
                    "no individual picture failed on its own, so the fault is in how they combine"
                else
                    String.concat "\n" culprits

            failwithf "the golden pictures do not compile.\n\n%s\n\n--- individually ---\n%s" combined detail

    /// The figure baselines that go through `Chart` rather than through a hand-built scene and
    /// are the documents a user would actually produce.
    [<Fact>]
    let ``the figure baselines are valid TeX`` () =
        let plain = Chart.render TikzBackend.render figureSurface figure

        let siunitx =
            figure |> Chart.withTheme Theme.tikz |> Chart.render TikzBackend.render figureSurface

        if
            not (
                TexToolchain.accepts "figure-golden" (TikzBackend.standalone plain)
                && TexToolchain.accepts "figure-siunitx-golden" (TikzBackend.standalone siunitx)
                && TexToolchain.accepts
                    "figure-symbols-golden"
                    (TikzBackend.standalone (Chart.render TikzBackend.render figureSurface Figures.symbolFigure))
            )
        then
            System.Console.WriteLine("SKIPPED: " + TexToolchain.report ())

    /// The wide log axis both ways: its ticks as math under `Theme.tikz`, and as the Unicode
    /// `10⁻⁸` the writer escapes to `10\textsuperscript{-8}` under the default theme.
    [<Fact>]
    let ``the wide log axis is valid TeX`` () =
        let plain = Chart.render TikzBackend.render figureSurface logFigure

        let siunitx =
            logFigure |> Chart.withTheme Theme.tikz |> Chart.render TikzBackend.render figureSurface

        Assert.Contains("$10^{-8}$", siunitx)

        if
            not (
                TexToolchain.accepts "figure-log" (TikzBackend.standalone plain)
                && TexToolchain.accepts "figure-log-siunitx" (TikzBackend.standalone siunitx)
            )
        then
            System.Console.WriteLine("SKIPPED: " + TexToolchain.report ())

// ======================================================================================
// CROSS-BACKEND AGREEMENT, OVER THE WHOLE SUITE — gate 2
//
// `TikzBackendTests` states the coordinate relationship exactly for a handful of scenes. This
// applies the cheap half of it — the picture is exactly the requested extent — to all thirteen,
// which is what catches a scene shape that happens to defeat `\useasboundingbox`.
// ======================================================================================

module ``the two backends agree on the page`` =

    [<Fact>]
    let ``every picture declares a bounding box of the requested extent`` () =
        for name, scene, extent in Scenes.all do
            let tex = TikzBackend.render extent scene

            // px -> bp is 72/96, and `Tikz.dim` writes at most three decimals.
            let expected =
                sprintf
                    "\\useasboundingbox (0bp,0bp) rectangle (%sbp,%sbp);"
                    (Num.at 3 (float extent.Width * 0.75))
                    (Num.at 3 (float extent.Height * 0.75))

            Assert.True(tex.Contains expected, sprintf "%s: expected %s" name expected)

            // And the SVG half of the same claim, so the two are asserted in one place.
            let svg = SvgBackend.render extent scene

            Assert.Contains(
                sprintf "viewBox=\"0 0 %s %s\"" (Num.at 3 (float extent.Width)) (Num.at 3 (float extent.Height)),
                svg
            )
