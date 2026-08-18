/// The TikZ golden suite — `docs/m1-plan.md` §4 gates 1, 5 and 6.
///
/// Every scene the SVG suite renders is rendered again here, from the **same** `Scenes.all` table
/// at the **same** surface, so `golden/paths.svg` and `golden/paths.tex` describe one picture in
/// two languages and can be read against each other. That is what makes the pair worth committing
/// rather than two unrelated snapshots that happen to share a name.
///
/// Three things this suite checks that the SVG one cannot:
///
/// * **Degradation is visible as text.** `Capabilities.Gradients = false` and `Rasters = false`, so
///   `gradients.tex` shows the Oklab-midpoint solid D10 promises — both ramps collapsing to the
///   same colour, which is the cost stated plainly — and `raster.tex` shows an empty picture where
///   two images were. D10's first real exercise, recorded in files a reviewer reads.
/// * **Every baseline compiles.** The SVG suite's oracle is `XDocument.Parse`; the only authority
///   on a `.tex` file is a TeX engine, so one runs over all thirteen.
/// * **A figure has a baseline at all** — in either backend. See `Golden.figure`.
module Marey.Tests.TikzGoldenTests

open Xunit
open Marey
open Marey.Tests.Golden
open Marey.Tests.GoldenTests

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

    /// `Rasters = false`, so the baseline is a **bare bounding box** — and that is worth being
    /// precise about, because the emitter contains a branch that writes
    /// `% marey: raster 2x2 skipped ...` and it never runs on this path.
    ///
    /// `Degrade.forCaps` replaces an unsupported `Raster` with `Scene.empty` before the backend
    /// sees it, which is exactly the contract: a backend advertising `Rasters = false` never gets
    /// handed one. The emitter's branch is still correct and still reachable — `IBackend.Draw` is
    /// public and can be driven with an un-degraded scene — so it is defensive, not dead. Both
    /// facts are pinned by the test below.
    ///
    /// The consequence for a user is real and accepted: an image in a TikZ figure disappears
    /// without a trace in the document. Routing around `Degrade` to preserve a comment would make
    /// Core's contract conditional on the backend, which is a worse trade for a `%` comment that
    /// nobody reading the compiled PDF can see.
    [<Fact>]
    let ``rasters degraded away`` () = tikz "raster"

    /// The two halves of the paragraph above, so neither can drift into a comment that is no
    /// longer true. This is the second time a claim about this backend's output was written down
    /// before being checked; the golden review caught it both times.
    [<Fact>]
    let ``the raster skip comment is unreachable through render and reachable through Draw`` () =
        let _, scene, extent = Scenes.all |> List.find (fun (n, _, _) -> n = "raster")

        // Through the public entry point: degraded first, so no trace survives.
        Assert.DoesNotContain("raster", TikzBackend.render extent scene)

        // Driven directly with the scene as authored: the branch fires.
        let backend = TikzBackend()
        let sink = backend :> IBackend
        sink.Begin extent
        sink.Draw scene
        sink.Finish()
        Assert.Contains("% marey: raster 2x2 skipped", backend.Result)

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
    let ``all nine Node cases`` () = tikz "all-nine-nodes"

// ======================================================================================
// THE FIGURE — gate 6
// ======================================================================================

module Figures =

    /// The same figure as `GoldenTests.Figures`, through the other backend. Reviewing the two
    /// diffs side by side is the cheapest way to see a layout change described twice.
    [<Fact>]
    let ``a complete figure`` () =
        check "figure.tex" (Chart.render TikzBackend.render figureSurface figure)

    /// The milestone's headline as a committed artefact rather than a substring assertion:
    /// `Theme.tikz` alone turns the unit witnesses into siunitx markup. TeX-only, because
    /// `\qty{}{\metre}` in an SVG would be nonsense.
    [<Fact>]
    let ``a complete figure under Theme tikz`` () =
        check "figure-siunitx.tex" (figure |> Chart.withTheme Theme.tikz |> Chart.render TikzBackend.render figureSurface)

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

    /// The two figure baselines, which go through `Chart` rather than through a hand-built scene
    /// and are the documents a user would actually produce.
    [<Fact>]
    let ``both figure baselines are valid TeX`` () =
        let plain = Chart.render TikzBackend.render figureSurface figure

        let siunitx =
            figure |> Chart.withTheme Theme.tikz |> Chart.render TikzBackend.render figureSurface

        if
            not (
                TexToolchain.accepts "figure-golden" (TikzBackend.standalone plain)
                && TexToolchain.accepts "figure-siunitx-golden" (TikzBackend.standalone siunitx)
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
