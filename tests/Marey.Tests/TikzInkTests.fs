/// The ink gate: the M1 plan §4 gate 7.
///
/// Every other test in this project reads the *text* the backend produced. These compile it and
/// count pixels, because Phase 2 proved that a text emitter can be verified into emitting a
/// valid, correct, entirely blank document — see the header of `Raster.fs` for the incident.
///
/// The suite is built out of **differentials** wherever it can be. "This figure has 1.5 % ink" is
/// a number nobody can defend; "removing the markers from this figure removes *all* the ink from
/// its panel" is a claim about the backend that cannot be satisfied by accident and needs no
/// magic constant. Where an absolute assertion is unavoidable the threshold is zero — either
/// there is ink or there is not — and the calibration behind that choice is in
/// `Raster.InkThreshold`.
///
/// Each fixture costs one `pdflatex` run and one `pdftoppm` run, so they are `lazy` and shared,
/// and a test that needs two pictures is written as one test rather than two.
module Marey.Tests.TikzInkTests

open Xunit
open Marey
open Marey.UnitsOfMeasure
open Marey.Tests.Raster

// ======================================================================================
// Fixtures
// ======================================================================================

/// Integral, so the page rasterises to a whole number of pixels at `Raster.Dpi` (see the note
/// there), and the same 8:5 shape the golden suite uses.
let private size: Extent<px> = { Width = 400.0<px>; Height = 250.0<px> }

let private ink = Color.rgb 0.1 0.1 0.12

let private at (x: float) (y: float) : Vec2<nd> = { X = x * 1.0<nd>; Y = y * 1.0<nd> }

let private ts = Array.init 40 (fun i -> float i * 0.25<s>)
let private xs = ts |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)

/// Free fall under `Theme.tikz` — the figure the milestone is announced on.
///
/// **The legend is switched off deliberately**, and not for tidiness. Naming a layer turns the
/// legend on, and it is drawn *inside* the panel, so a legend box would put a `Path` and a `Text`
/// into the region the differentials below attribute to series geometry alone. Leaving it on made
/// two of those tests fail against a perfectly correct backend, which is the right outcome for a
/// figure that no longer matched the claim being made about it. Each fixture here contains exactly
/// what its test talks about; the legend gets its own test, where it is the subject.
let private freeFall =
    Chart.line U.second U.metre ts xs
    |> Chart.withName "model"
    |> Chart.withTheme Theme.tikz
    |> Chart.withLegend LegendNone

/// The same data as points only. With the legend off its panel interior is inked by **nothing but
/// its markers** — no series line, no grid — which makes it the sharpest available probe for the
/// D33 paint bug.
let private points =
    Chart.scatter U.second U.metre ts.[..8] xs.[..8]
    |> Chart.withName "measured"
    |> Chart.withTheme Theme.tikz
    |> Chart.withLegend LegendNone

/// Delete every node of one case from a scene, leaving the layout untouched.
///
/// This is what makes the differentials honest: the two documents differ in exactly one kind of
/// primitive, so any change in ink is attributable to that kind and to nothing else — not to a
/// re-laid-out axis, not to a shifted panel.
let private without (isTarget: Node -> bool) =
    Scene.transform (fun n -> if isTarget n then Scene.empty else n)

let private markers n =
    match n with
    | Markers _ -> true
    | _ -> false

let private text n =
    match n with
    | Text _ -> true
    | _ -> false

/// Compile and rasterise a scene, or `None` when the toolchain is absent.
let private rasterOf (name: string) (node: Node) : Gray option =
    match Raster.ofDocument name (TikzBackend.renderStandalone size node) with
    | TexToolchain.PdfProduced g -> Some g
    | TexToolchain.PdfUnavailable _ -> None
    | TexToolchain.PdfFailed why -> failwithf "ink gate: could not produce a raster for '%s':\n%s" name why

let private rasterOfFigure (name: string) (fig: Figure) : Gray option =
    rasterOf name (Chart.toScene size fig)

/// Every test that needs a toolchain funnels through here, so "skipped" is said once, in one
/// form, and is impossible to confuse with "passed".
///
/// Three arities rather than one list-taking helper because a lambda that destructures a list
/// compiles to an incomplete match, and this project builds with warnings as errors.
let private skipped () =
    System.Console.WriteLine("SKIPPED: " + Raster.report ())

let private withRaster (a: Gray option) (body: Gray -> unit) =
    match a with
    | Some x -> body x
    | None -> skipped ()

let private withRasters2 (a: Gray option) (b: Gray option) (body: Gray -> Gray -> unit) =
    match a, b with
    | Some x, Some y -> body x y
    | _ -> skipped ()

let private withRasterList (rs: Gray option list) (body: Gray list -> unit) =
    if rs |> List.forall Option.isSome then
        body (rs |> List.map Option.get)
    else
        skipped ()

// ======================================================================================
// Regions
//
// Fractions of the page rather than pixels, so a layout tweak that moves the panel by a few
// pixels does not invalidate them, and deliberately loose: each one is a claim about which part
// of the figure a thing belongs to, not a claim about where exactly it is. Every boundary here
// was measured against a compiled figure rather than guessed.
// ======================================================================================

/// Where the y tick labels and the rotated y axis title live.
let private leftMargin =
    { Left = 0.0
      Top = 0.0
      Right = 0.15
      Bottom = 0.85 }

/// Where the x tick labels and the x axis title live. Starts at 0.10 so the y title, which
/// descends to the axis line, cannot be mistaken for an x label.
let private bottomMargin =
    { Left = 0.10
      Top = 0.85
      Right = 1.0
      Bottom = 1.0 }

/// Inside the axes: series geometry only, for these figures.
let private panel =
    { Left = 0.20
      Top = 0.05
      Right = 0.98
      Bottom = 0.78 }

/// The panel's upper-left quarter. `x = ½gt²` rises left to right, so on a correctly oriented
/// page this is the one quadrant the curve never enters — and it is where a top-left-anchored
/// legend goes.
let private panelTopLeft =
    { panel with
        Right = 0.59
        Bottom = 0.41 }

/// The panel's lower-left quarter, where that same curve starts.
let private panelBottomLeft = { panel with Right = 0.59; Top = 0.41 }

// ======================================================================================
// Assertions
// ======================================================================================

let private assertInk (what: string) (region: Region) (g: Gray) =
    let c = coverageIn region g

    Assert.True(c > 0.0, sprintf "expected ink in %s and found none.\n\n%s" what (describe g))

let private assertBlank (what: string) (region: Region) (g: Gray) =
    let c = coverageIn region g

    // The parentheses are load-bearing: `c = 0.0` in an argument position is a named argument.
    Assert.True((c = 0.0), sprintf "expected %s to be blank, found %.3f%% ink.\n\n%s" what (100.0 * c) (describe g))

// ======================================================================================
// THE NEGATIVE CONTROL
//
// The compile gate was theatre for ten minutes because nothing asserted that it could fail
// (§4 gate 1). That mistake is not repeated: before any test here claims to have found ink, one
// test establishes that this apparatus reports its absence.
// ======================================================================================

module ``the gate can fail`` =

    [<Fact>]
    let ``an empty picture rasterises to a page with no ink at all`` () =
        withRaster (rasterOf "ink-blank" Scene.empty) (fun blank ->
            assertBlank "an empty picture" whole blank
            // ...and the page is still the requested size, so "no ink" is a statement about a
            // real page rather than about a degenerate one. It also pins `Raster.Dpi` against
            // `TikzOptions.Dpi`: at any other resolution these two numbers are different.
            Assert.Equal(400, blank.Width)
            Assert.Equal(250, blank.Height))

    /// D33, in the smallest form that shows it.
    ///
    /// A marker batch asking for no paint must draw nothing; the same batch asking for a fill must
    /// draw something. The bug was that **both** drew nothing, and the pair is what pins it: the
    /// first half alone passes on a broken emitter, and the second half alone can be satisfied by
    /// a batch that ignores its paint entirely.
    [<Fact>]
    let ``a marker batch draws exactly when it is painted`` () =
        let batch fill =
            Markers
                { Points = [| at 0.2 0.3; at 0.5 0.5; at 0.8 0.7 |]
                  Shape = MarkCircle
                  Size = 6.0<px>
                  Fill = fill
                  Stroke = None }

        withRasters2 (rasterOf "ink-markers-unpainted" (batch None)) (rasterOf "ink-markers-filled" (batch (Some(Solid ink)))) (fun unpainted filled ->
            assertBlank "a marker batch with no fill and no stroke" whole unpainted
            assertInk "a filled marker batch" whole filled)

// ======================================================================================
// EVERY DRAWABLE CASE PUTS INK ON THE PAGE
// ======================================================================================

module ``the drawable cases draw`` =

    /// `Path`, `Text` and `Markers` are the three cases that put ink on a page by themselves;
    /// `Group`, `Transform`, `Clip`, `Opacity`, `Keyed` and `Annotated` can only wrap them, and
    /// `Raster` is degraded away by this backend (`Capabilities.Rasters = false`), which is
    /// checked below.
    ///
    /// The `Path` here is a **0.4 px hairline**, which is 0.3 bp — comfortably under one device
    /// pixel at 96 dpi. It is the thinnest thing the library can be asked to draw, so if the
    /// threshold in `Raster.InkThreshold` were ever set too aggressively this is the case that
    /// would notice.
    [<Fact>]
    let ``a path, a text run and a marker batch each ink an otherwise empty page`` () =
        let cases =
            [ "path", Scene.polyline (Stroke.solid ink 0.4<px>) [| at 0.1 0.5; at 0.9 0.5 |]
              "text",
              Text
                  { Text = "Wg"
                    Tex = None
                    At = at 0.5 0.5
                    Font =
                      { Family = "Helvetica"
                        Size = 11.0<pt>
                        Weight = 400
                        Italic = false }
                    Paint = Solid ink
                    Anchor = AnchorMiddle
                    Baseline = BaselineMiddle
                    Rotation = 0.0<rad> }
              "markers",
              Markers
                  { Points = [| at 0.2 0.3; at 0.5 0.5; at 0.8 0.7 |]
                    Shape = MarkCircle
                    Size = 6.0<px>
                    Fill = Some(Solid ink)
                    Stroke = None } ]

        let rasters = cases |> List.map (fun (name, node) -> rasterOf ("ink-case-" + name) node)

        withRasterList rasters (fun gs ->
            for (name, _), g in List.zip cases gs do
                assertInk ("a scene containing only a " + name) whole g)

// ======================================================================================
// THE FIGURE
// ======================================================================================

module ``a figure inks the parts it is made of`` =

    /// **The gate.** A scatter figure's panel interior is inked by its markers and by nothing
    /// else — no series line, no grid — so deleting the batches must empty it completely.
    ///
    /// This is the assertion that would have caught the Phase 2 bug on its own, at the level a
    /// user would meet it, and it is exact rather than approximate: the stripped figure's panel
    /// measures **zero** ink, not "less".
    [<Fact>]
    let ``the panel of a scatter figure is inked only by its markers`` () =
        let scene = Chart.toScene size points

        withRasters2 (rasterOf "ink-scatter" scene) (rasterOf "ink-scatter-no-markers" (without markers scene)) (fun full stripped ->
            assertInk "the panel of a scatter figure" panel full
            assertBlank "the panel of a scatter figure with its markers deleted" panel stripped
            // The axes survived the surgery, so the two pages differ only inside the panel and
            // the differential above is attributable.
            assertInk "the axis margin of the stripped figure" leftMargin stripped)

    /// The axis labels are the other half of what a reader looks at, and they reach the page by a
    /// completely different route: `ITextMeasurer` reserves the margin, `Axis` composes the
    /// string, `Tikz.runContent` escapes it, and TeX typesets it. The bottom margin of these
    /// figures contains nothing but text, so the same differential works.
    [<Fact>]
    let ``the margins of a figure are inked only by its text`` () =
        let scene = Chart.toScene size freeFall

        withRasters2 (rasterOf "ink-figure" scene) (rasterOf "ink-figure-no-text" (without text scene)) (fun full stripped ->
            assertInk "the bottom margin of a figure" bottomMargin full
            assertInk "the left margin of a figure" leftMargin full
            assertBlank "the bottom margin of a figure with its text deleted" bottomMargin stripped
            // The curve is unaffected, which is what makes the two claims above about *text*.
            Assert.Equal(coverageIn panel full, coverageIn panel stripped, 6))

    /// The legend was the second thing the D33 bug made invisible, and `LegendTopLeft` puts it in
    /// the one part of the panel this curve never reaches — so the placement is the assertion.
    [<Fact>]
    let ``a legend inks the corner it is placed in`` () =
        let withLegend =
            freeFall
            |> Chart.addScatter U.second U.metre ts.[..8] xs.[..8]
            |> Chart.withName "measured"

        withRasters2
            (rasterOfFigure "ink-legend" (withLegend |> Chart.withLegend LegendTopLeft))
            (rasterOfFigure "ink-no-legend" (withLegend |> Chart.withLegend LegendNone))
            (fun shown hidden ->
                assertInk "the top-left of the panel, where the legend was placed" panelTopLeft shown
                assertBlank "the top-left of the panel of the same figure with no legend" panelTopLeft hidden)

    /// **Which way up the page is** — the one property no other gate in this project can reach.
    ///
    /// Cross-backend agreement states that a TikZ y equals the SVG y measured from the opposite
    /// edge (D29), which is satisfied just as well by two backends that are *both* upside down.
    /// A raster is not: `x = ½gt²` increases, so on a correct page the curve leaves the panel's
    /// lower-left corner and never enters its upper-left one. Invert the flip and the two
    /// assertions below swap.
    [<Fact>]
    let ``the figure is drawn the right way up`` () =
        withRaster (rasterOfFigure "ink-orientation" freeFall) (fun g ->
            assertInk "the lower-left of the panel, where an increasing curve starts" panelBottomLeft g
            assertBlank "the upper-left of the panel, which an increasing curve never enters" panelTopLeft g)

    /// A coverage *band*, and the only assertion here with a number in it. It is the cheap
    /// catch-all for the two ways a page can be obviously wrong in a way no region test frames:
    /// nothing was drawn, or something was drawn over everything — a runaway fill, a clip that
    /// inverted, a panel background painted in the foreground colour.
    ///
    /// The bounds are an order of magnitude either side of the ~2 % a line chart measures, so
    /// they say "a figure, not a blank page and not a black rectangle" and nothing narrower.
    [<Fact>]
    let ``a figure is neither a blank page nor a solid block`` () =
        withRaster (rasterOfFigure "ink-coverage" freeFall) (fun g ->
            let c = coverage g

            Assert.True(c > 0.002, sprintf "a figure covering %.3f%% of its page is blank.\n\n%s" (100.0 * c) (describe g))

            Assert.True(c < 0.40, sprintf "a figure covering %.3f%% of its page is a smear.\n\n%s" (100.0 * c) (describe g)))

    /// D10 and D31: this backend advertises `Rasters = false`, so `Degrade` removes the node
    /// before it reaches the emitter. The document must still compile, and — the part only a
    /// rasteriser can confirm — must be *blank*, rather than carrying a stray box or a literal
    /// `\pgfimage` that Poppler renders as an error glyph.
    [<Fact>]
    let ``a raster node degrades to no ink rather than to a broken box`` () =
        let node =
            Raster
                { Image =
                    { Width = 4
                      Height = 4
                      Rgba = Array.init (4 * 4 * 4) (fun i -> if i % 4 = 3 then 255uy else 40uy) }
                  Bounds =
                    { X0 = 0.2<nd>
                      Y0 = 0.2<nd>
                      X1 = 0.8<nd>
                      Y1 = 0.8<nd> }
                  Smooth = true }

        withRaster (rasterOf "ink-raster-degraded" node) (fun g -> assertBlank "a degraded raster node" whole g)

// ======================================================================================
// What ran
// ======================================================================================

module ``toolchain report`` =

    [<Fact>]
    let ``the ink gate says whether it ran`` () =
        System.Console.WriteLine("Marey ink gate: " + Raster.report ())
        Assert.True(true)

// ======================================================================================
// D49 — the colorbar, in ink
// ======================================================================================

/// **The gate the decision was written for.**
///
/// D49 rejected a gradient-filled colorbar because `Gradients = false` makes `Degrade.forCaps`
/// collapse the ramp to its Oklab midpoint, and the result is a single flat block: valid TeX, a
/// clean golden diff, and no colorbar. Every text-level assertion in this repository would pass
/// on that document — which is the same shape of failure as D33's invisible markers, arrived at
/// from the other direction.
///
/// So the bar is checked the only way that can tell the two apart: compile it, rasterise it, and
/// look at the greys down its length. A viridis ramp is lightness-monotone by construction, so a
/// bar of strips is a gradient of greys and a degraded bar is one value repeated.
module ColorbarInk =

    let private times = Array.init 24 (fun i -> float i * 0.4<s>)
    let private falls = times |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)

    let private temperatures =
        times |> Array.map (fun t -> (280.0 + 2.5 * float t) * 1.0<K>)

    let private withBar =
        Chart.scatter U.second U.metre times falls
        |> Chart.colorBy U.kelvin temperatures
        |> Chart.withColorLabel "T"
        |> Chart.withTheme Theme.tikz
        |> Chart.withLegend LegendNone

    let private withoutBar =
        Chart.scatter U.second U.metre times falls
        |> Chart.withTheme Theme.tikz
        |> Chart.withLegend LegendNone

    /// Where the bar actually is, read off the compiled scene rather than guessed at.
    ///
    /// The strips are the filled paths inside the colorbar's own `Annotated` node, so their
    /// union is the bar. Deriving the region this way means the test keeps working when the
    /// theme changes the bar's width — the alternative, a hand-measured fraction of the page,
    /// would be measuring the layout instead of the ink.
    let private barRegion (node: Node) : Region option =
        let bounds =
            Scene.fold
                (fun acc n ->
                    match n with
                    | Annotated(sem, body) when sem.Role = RoleOther "colorbar" ->
                        Scene.fold
                            (fun (x0, y0, x1, y1) inner ->
                                match inner with
                                | Path(segs, _, Some _, _) ->
                                    segs
                                    |> Array.fold
                                        (fun (a, b, c, d) seg ->
                                            match seg with
                                            | MoveTo pt
                                            | LineTo pt ->
                                                min a (float pt.X), min b (float pt.Y), max c (float pt.X), max d (float pt.Y)
                                            | _ -> a, b, c, d)
                                        (x0, y0, x1, y1)
                                | _ -> x0, y0, x1, y1)
                            acc
                            body
                    | _ -> acc)
                (infinity, infinity, -infinity, -infinity)
                node

        match bounds with
        | (x0, y0, x1, y1) when x0 <= x1 ->
            // nd is bottom-up and the raster is top-down. Inset by a strip's own width so the
            // region is the bar's interior rather than its frame.
            Some
                { Left = x0 + 0.004
                  Right = x1 - 0.004
                  Top = 1.0 - y1 + 0.01
                  Bottom = 1.0 - y0 - 0.01 }
        | _ -> None

    /// The distinct grey levels inside a region, ignoring the near-white page.
    let private greysIn (r: Region) (g: Gray) =
        let x0 = max 0 (int (r.Left * float g.Width))
        let x1 = min g.Width (int (r.Right * float g.Width))
        let y0 = max 0 (int (r.Top * float g.Height))
        let y1 = min g.Height (int (r.Bottom * float g.Height))

        let mutable found = Set.empty

        for y in y0 .. y1 - 1 do
            for x in x0 .. x1 - 1 do
                let v = g.Pixels.[y * g.Width + x]

                if int v < 250 then
                    found <- Set.add (int v) found

        found

    /// D49's claim, in pixels: the bar is inked, its greys form a ramp rather than a block, and
    /// the ink is *there because of the bar* — the same figure without a colour channel leaves
    /// that column of the page empty.
    [<Fact>]
    let ``the colorbar is a ramp of greys, not one flat block`` () =
        let scene = Chart.toScene size withBar

        match barRegion scene with
        | None -> failwith "no colorbar was compiled"
        | Some region ->
            withRasters2 (rasterOf "colorbar-with" scene) (rasterOfFigure "colorbar-without" withoutBar) (fun a b ->
                let inked = coverageIn region a
                Assert.True(inked > 0.9, sprintf "the bar covers only %.1f%% of its own region" (inked * 100.0))

                let greys = greysIn region a

                Assert.True(
                    greys.Count > 20,
                    sprintf "the bar rasterises to %d grey levels — a degraded gradient is one\n%s" greys.Count (inkMap 100 a)
                )

                // The differential: without the channel there is no bar, and no ink where it was.
                let empty = coverageIn region b
                Assert.True(empty < 0.02, sprintf "%.1f%% ink where the bar is not" (empty * 100.0)))
