/// §9 / §13 — `Marey.Plot`: themes, layout, the axis compiler, and the `Chart` front door.
///
/// §13 names five property suites. This file supplies the one that needed `Marey.Plot` to
/// exist first:
///
/// > "**layout invariants (no overlapping panels, labels inside bounds)**"
///
/// It also locks in the three claims the project is actually *sold* on, because they are
/// claims about behaviour and behaviour regresses:
///
/// * **§9 / §2.2 — an axis labels itself from the unit witness with no configuration.** If
///   `t [s]` ever stops appearing, the README is lying and the whole units thesis is dead.
/// * **§5.1 — batching survives the compile pipeline**, not just the backend. A 50 000-point
///   scatter must still be *one* `Markers` node after layout, theming and clipping.
/// * **§1.1 — combinators do not mutate.** The design indicts Plotly.NET by name for exactly
///   this ("styling functions *mutate* the chart (documented caveat)"). A library that repeats
///   the flaw it was written to avoid is worse than one that never mentioned it.
///
/// What §13 still owes, and why it is not here: the `Anim` monoid laws need `Marey.Anim`
/// (§14 puts it at M3), and the `Sim` invariant-drift checks need `Marey.Sim` (M5). Those are
/// "not yet writable", not "not needed".
module Marey.Tests.PlotTests

open FsCheck.Xunit
open Xunit
open Marey

// ======================================================================================
// Fixtures
// ======================================================================================

/// Free fall: `float<s>` in, `float<m>` out. The measures are the entire configuration.
let private fallT = Array.init 40 (fun i -> float i * 0.25<s>)
let private fallX = fallT |> Array.map (fun t -> 0.5 * 9.81<m/s^2> * t * t)

let private surface = Chart.size 800.0 500.0

let private baseFigure () = Chart.line U.second U.metre fallT fallX

let private renderFig (extent: Extent<px>) (fig: Figure) =
    SvgBackend.render extent (Chart.toScene extent fig)

/// Every `Text` node in a compiled scene, with its panel-space anchor.
let private textNodes (node: Node) =
    node
    |> Scene.fold
        (fun acc n ->
            match n with
            | Text t -> t :: acc
            | _ -> acc)
        []

// ======================================================================================
// §9 / §2.2 — the headline: axes label themselves from the witness
// ======================================================================================

module SelfLabellingAxes =

    [<Fact>]
    let ``seconds against metres labels itself 't [s]' and 'x [m]' with no configuration`` () =
        let svg = renderFig surface (baseFigure ())
        Assert.Contains(">t [s]<", svg)
        Assert.Contains(">x [m]<", svg)

    /// §2.2's worked example, verbatim: "data in `float<m>` spanning `1e-9 … 5e-9` should
    /// produce ticks labelled `1, 2, 3, 4, 5` with an axis title of `x [nm]`."
    [<Fact>]
    let ``§2.2 nanometre data produces 'x [nm]' with a 1..5 tick ladder`` () =
        let x = Array.init 5 (fun i -> float i / 4.0 * 1.0<s>)
        let y = Array.init 5 (fun i -> (1.0 + float i) * 1e-9<m>)
        let svg = renderFig surface (Chart.line U.second U.metre x y)

        Assert.Contains(">x [nm]<", svg)

        for i in 1..5 do
            Assert.Contains(sprintf ">%d<" i, svg)

    /// The kilogram is the one SI base unit that already carries a prefix, so a milligram
    /// axis is the case a naive implementation gets wrong by a factor of 1000 (D19).
    [<Fact>]
    let ``a microgram-scale mass axis reads mg, never µkg`` () =
        let x = Array.init 5 (fun i -> float i * 1.0<s>)
        let y = Array.init 5 (fun i -> (1.0 + float i) * 1e-6<kg>)
        let svg = renderFig surface (Chart.line U.second U.kilogram x y)

        Assert.Contains("[mg]", svg)
        Assert.DoesNotContain("µkg", svg)

// ======================================================================================
// §13 — layout invariants
// ======================================================================================

module LayoutInvariants =

    let private sizes =
        [ "800x500", Chart.size 800.0 500.0
          "300x260", Chart.size 300.0 260.0
          "1600x900", Chart.size 1600.0 900.0
          // Deliberately cramped: the case where tick labels cannot all fit and the
          // collision ladder has to do real work.
          "220x180", Chart.size 220.0 180.0 ]

    let private themes =
        [ "publication", Theme.publication
          "dark", Theme.dark
          "notebook", Theme.notebook
          "presentation", Theme.presentation ]

    let private busyFigure () =
        baseFigure ()
        |> Chart.withName "model"
        |> Chart.addScatter U.second U.metre fallT fallX
        |> Chart.withName "measured"
        |> Chart.withTitle "Free fall"
        |> Chart.withLegend LegendTopRight

    /// §13: "labels inside bounds". Panel space is nominally [0,1]²; a small tolerance
    /// allows for the axis gutter, where tick labels legitimately sit just outside the plot
    /// area but still inside the figure.
    [<Fact>]
    let ``every text label sits inside the figure at every size and theme`` () =
        for (sizeName, extent) in sizes do
            for (themeName, theme) in themes do
                let scene = Chart.toScene extent (busyFigure () |> Chart.withTheme theme)

                let strays =
                    textNodes scene
                    |> List.filter (fun t ->
                        let x = float t.At.X
                        let y = float t.At.Y
                        x < -0.05 || x > 1.05 || y < -0.05 || y > 1.05)

                Assert.True(
                    strays.IsEmpty,
                    sprintf
                        "%s / %s: %d label(s) outside the figure, e.g. %A"
                        sizeName
                        themeName
                        strays.Length
                        (strays |> List.truncate 3 |> List.map (fun t -> t.Text, float t.At.X, float t.At.Y))
                )

    /// §13: "no overlapping panels". Two panels stacked vertically must occupy disjoint
    /// regions — checked through the `Clip` nodes the compiler emits per panel, which are
    /// the authoritative statement of where a panel actually is.
    [<Fact>]
    let ``panels in a multi-panel figure do not overlap`` () =
        let twoPanel =
            baseFigure ()
            |> Chart.withPanelTitle "left"
            |> Chart.beside (Chart.line U.second U.metre fallT (Array.rev fallX) |> Chart.withPanelTitle "right")

        let scene = Chart.toScene surface twoPanel

        // A panel's `Clip` path is expressed in *panel-local* space; the enclosing `Transform`
        // is what places it in the figure. Comparing the raw clip coordinates therefore reports
        // every panel as (0,0,1,1) and every pair as overlapping — so the accumulated transform
        // has to be carried down the tree.
        //
        // Being able to do this at all is D1 paying off: `Transform` holds an `Affine` value, so
        // the test can compose it. With the spike's closure-valued transform there would be no
        // way to compute a panel's figure-space bounds from outside the renderer.
        let rec collect (ctm: Affine<nd, nd>) (node: Node) =
            match node with
            | Clip(segs, child) ->
                let pts =
                    segs
                    |> Array.choose (function
                        | MoveTo p
                        | LineTo p -> Some p
                        | CubicTo(_, _, p) -> Some p
                        | ClosePath -> None)
                    |> Array.map (Affine.apply ctm)

                let here =
                    if pts.Length = 0 then
                        []
                    else
                        let xs = pts |> Array.map (fun p -> float p.X)
                        let ys = pts |> Array.map (fun p -> float p.Y)
                        [ Array.min xs, Array.min ys, Array.max xs, Array.max ys ]

                here @ collect ctm child

            | Transform(a, child) -> collect (Affine.compose a ctm) child
            | Group ns -> ns |> Array.toList |> List.collect (collect ctm)
            | Keyed(_, child)
            | Annotated(_, child) -> collect ctm child
            | Path _
            | Text _
            | Markers _
            | Raster _ -> []

        let clipBounds = collect Affine.identity scene

        Assert.True(clipBounds.Length >= 2, sprintf "expected >= 2 panel clips, got %d" clipBounds.Length)

        let overlaps (ax0, ay0, ax1, ay1) (bx0, by0, bx1, by1) =
            ax0 < bx1 - 1e-9 && bx0 < ax1 - 1e-9 && ay0 < by1 - 1e-9 && by0 < ay1 - 1e-9

        let pairs =
            [ for i in 0 .. clipBounds.Length - 1 do
                  for j in i + 1 .. clipBounds.Length - 1 do
                      if overlaps clipBounds.[i] clipBounds.[j] then
                          yield (clipBounds.[i], clipBounds.[j]) ]

        Assert.True(pairs.IsEmpty, sprintf "overlapping panel regions: %A" pairs)

    /// A figure must compile at any plausible aspect ratio without throwing. Extreme ratios
    /// are where a measure-arrange pass divides by a track that shrank to zero.
    [<Property(MaxTest = 60)>]
    let ``compiles at any plausible surface size`` (w: int) (h: int) =
        // Map arbitrary ints into a plausible pixel range rather than filtering them out, so
        // no generated case is discarded and the shrinker still has room to work.
        // `v % 1880` is safe for Int32.MinValue where `abs v` would not be.
        let toPx v = 120 + abs (v % 1880)
        let extent = Chart.size (float (toPx w)) (float (toPx h))
        Scene.count (Chart.toScene extent (busyFigure ())) > 0

// ======================================================================================
// §5.1 — batching survives the whole compile pipeline
// ======================================================================================

module Batching =

    [<Fact>]
    let ``a 50k-point scatter compiles to exactly one Markers node`` () =
        let n = 50000
        let x = Array.init n (fun i -> float i / float n * 10.0<s>)
        let y = x |> Array.map (fun t -> sin (float t) * 1.0<m>)

        let scene = Chart.toScene surface (Chart.scatter U.second U.metre x y)

        let markerNodes =
            scene
            |> Scene.fold
                (fun acc node ->
                    match node with
                    | Markers _ -> acc + 1
                    | _ -> acc)
                0

        Assert.Equal(1, markerNodes)

        // The whole figure stays small while still accounting for every point. The gap
        // between these two numbers is precisely what §5.1 buys.
        Assert.True(Scene.count scene < 200, sprintf "figure exploded to %d nodes" (Scene.count scene))
        Assert.True(Scene.primitiveCount scene >= n)

// ======================================================================================
// §1.1 — combinators are pure. The Plotly.NET flaw, by name.
// ======================================================================================

module Immutability =

    [<Fact>]
    let ``styling a figure does not mutate the original`` () =
        let original = baseFigure ()
        let before = renderFig surface original

        let derived =
            original
            |> Chart.withTitle "MUTATED"
            |> Chart.withColor (Color.ofHex "#ff0000")
            |> Chart.withMarker MarkDiamond

        let after = renderFig surface original

        Assert.Equal(before, after)
        Assert.DoesNotContain("MUTATED", after)
        Assert.NotEqual<string>(before, renderFig surface derived)

// ======================================================================================
// Determinism through the full pipeline — the property golden images depend on
// ======================================================================================

module Determinism =

    [<Fact>]
    let ``the whole compile-and-render pipeline is deterministic`` () =
        let fig = baseFigure () |> Chart.withTitle "determinism"
        Assert.Equal(renderFig surface fig, renderFig surface fig)

    /// D15 / the SVG writer's contract, but exercised through Plot: a comma-decimal locale
    /// must not change one byte. Tick labels are generated by Plot, so this covers a code
    /// path the backend-only culture test cannot reach.
    [<Fact>]
    let ``a comma-decimal culture produces byte-identical output`` () =
        let fig = baseFigure () |> Chart.withTitle "culture"
        let previous = System.Threading.Thread.CurrentThread.CurrentCulture

        try
            System.Threading.Thread.CurrentThread.CurrentCulture <- System.Globalization.CultureInfo "en-US"
            let invariantish = renderFig surface fig

            System.Threading.Thread.CurrentThread.CurrentCulture <- System.Globalization.CultureInfo "de-DE"
            let german = renderFig surface fig

            Assert.Equal(invariantish, german)
            Assert.DoesNotContain("1,5", german)
        finally
            System.Threading.Thread.CurrentThread.CurrentCulture <- previous

// ======================================================================================
// §9 — the colour system, which the design says is embarrassing to get wrong
// ======================================================================================

module Palettes =

    /// §9 asks for "a validator that checks contrast and CVD-safety, because a physics
    /// library that renders fields in jet is embarrassing". Lightness monotonicity is the
    /// machine-checkable core of that: jet fails it, viridis and friends pass.
    [<Theory>]
    [<InlineData "viridis">]
    [<InlineData "cividis">]
    [<InlineData "magma">]
    let ``sequential ramps are lightness-monotone`` (name: string) =
        let ramp =
            match name with
            | "viridis" -> Palette.viridis
            | "cividis" -> Palette.cividis
            | _ -> Palette.magma

        // A small tolerance: the ramps are published as discrete stops, so interpolation
        // between them can wobble by a hair without the ramp being non-monotone in any way a
        // reader could see.
        Assert.True(Palette.isLightnessMonotone 1e-3 ramp, sprintf "%s is not lightness-monotone" name)

    /// The counter-example the design names: "a physics library that renders fields in jet is
    /// embarrassing". Jet must *fail* the same check the good ramps pass, or the validator is
    /// not actually testing anything.
    [<Fact>]
    let ``jet fails the lightness-monotonicity check that viridis passes`` () =
        let jet =
            { Name = "jet"
              Stops =
                [| 0.00, Color.ofHex "#00007F"
                   0.125, Color.ofHex "#0000FF"
                   0.375, Color.ofHex "#00FFFF"
                   0.625, Color.ofHex "#FFFF00"
                   0.875, Color.ofHex "#FF0000"
                   1.00, Color.ofHex "#7F0000" |] }

        Assert.False(Palette.isLightnessMonotone 1e-3 jet, "jet should not be lightness-monotone")
        Assert.True(Palette.isLightnessMonotone 1e-3 Palette.viridis)

    [<Fact>]
    let ``the Okabe-Ito set stays distinguishable under all three dichromacies`` () =
        let report = Palette.validate Color.white Palette.okabeIto

        // Separation only. `report.Ok` also folds in the contrast floor, which Okabe-Ito
        // genuinely fails on white — see the test below.
        Assert.True(
            report.WorstSeparation.Minimum >= Palette.SeparationFloor,
            sprintf
                "Okabe-Ito separation fell to %.4f under %s (colours %d and %d), below the %.3f floor"
                report.WorstSeparation.Minimum
                report.WorstSeparation.Vision
                report.WorstSeparation.IndexA
                report.WorstSeparation.IndexB
                Palette.SeparationFloor
        )

    /// **A known, deliberate limitation, pinned so it cannot drift silently.**
    ///
    /// Okabe–Ito is the canonical colour-blind-safe qualitative palette and it is what §9 asks
    /// for — but two of its eight colours are very light, and against a white background its
    /// yellow lands around 1.3:1, far below WCAG's 3:1 for graphical objects.
    ///
    /// The palette is not "fixed", because a corrected Okabe–Ito is not Okabe–Ito; the light
    /// series order demotes the offenders instead. This test states the trade-off out loud so
    /// that a future change to either the palette or the contrast floor has to confront it.
    [<Fact>]
    let ``Okabe-Ito fails the contrast floor on white, and that is a known trade-off`` () =
        let report = Palette.validate Color.white Palette.okabeIto

        Assert.True(
            report.MinContrast < Palette.ContrastFloor,
            "Okabe-Ito now clears the contrast floor on white. If the palette or the floor "
            + "changed deliberately, delete this test; if not, something regressed."
        )

        // Whatever else is true, the offender must stay the pale one — yellow or sky blue.
        Assert.Contains(report.MinContrastIndex, [| 2; 4 |])

    /// A diverging palette for signed fields must be neutral in the middle, or zero stops
    /// looking like zero.
    [<Fact>]
    let ``the diverging ramp is neutral at its midpoint`` () =
        let mid = Ramp.at Palette.diverging 0.5
        let lch = Color.toOklch mid
        Assert.True(lch.C < 0.03, sprintf "diverging midpoint has chroma %.4f, expected near zero" lch.C)

/// The two things the `modern` default rests on, both of which fail silently.
module Themes =

    /// §9 asks for a validator. A validator only earns its place if the themes the library
    /// itself ships are run through it — a shipped theme that fails its own contrast floor is
    /// the exact failure this test exists to make impossible.
    ///
    /// It has already done work: `Theme.modern` was first drafted with a pale `#C8CDD4` spine,
    /// which is the conventional way to make chart furniture recede. That measures about 1.5:1
    /// on white and fails here. The theme now recedes by getting thinner rather than paler.
    [<Fact>]
    let ``every shipped theme passes its own validator`` () =
        for theme in Theme.all do
            let r = Theme.validate theme

            Assert.True(
                r.Ok,
                sprintf
                    "Theme.%s fails validation: text %.2f:1 (needs 7), axis %.2f:1 (needs %.1f)"
                    r.Theme
                    r.TextContrast
                    r.AxisContrast
                    Palette.ContrastFloor
            )

    /// `Chart.withTheme` recolours a layer **only if that layer's style still equals the outgoing
    /// theme's default for its index** — which is what lets it retheme a figure without discarding
    /// a colour set by hand. The test is meaningful only while the theme a `Figure` is born with
    /// and the theme `Chart`'s constructors style their layers against are the same value.
    ///
    /// Those lived in two files and agreed by coincidence until `Theme.initial` was introduced.
    /// Had they drifted, nothing would have crashed: `withTheme` would simply have stopped
    /// working, silently, leaving every figure in the default palette.
    [<Fact>]
    let ``a fresh figure's layer styles match its own theme, so withTheme can recolour it`` () =
        let fig = Chart.line U.second U.metre fallT fallX
        Assert.Equal(Theme.initial.Name, fig.Theme.Name)

        let layer = fig.Panels.[0].Layers.[0]
        Assert.Equal(Geom.styleFor fig.Theme 0, layer.Style)

        // The consequence, end to end: a retheme must actually reach the paint.
        let dark = fig |> Chart.withTheme Theme.dark
        Assert.Equal(Geom.styleFor Theme.dark 0, dark.Panels.[0].Layers.[0].Style)
        Assert.NotEqual(layer.Style, dark.Panels.[0].Layers.[0].Style)
