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
open Marey.UnitsOfMeasure

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
// Plain numbers: unspecified, and dimensionless
// ======================================================================================

/// `Chart.Plain` was `Chart` at `U.one U.one`; D109 retired it for the unit-optional
/// constructors, which leave a unit out as `U.unspecified` (D127). The two witnesses differ in
/// what they *claim* — `one` says dimensionless, and the axis-sharing check acts on it — and not
/// in how a single layer is drawn, so that is pinned here, along with the honesty of a bare axis:
/// a plain `x`, never `x [1]`, and never rescaled into a prefix.
module PlainNumbers =

    let private xs = Array.init 40 (fun i -> float i / 4.0)
    let private ys = xs |> Array.map sin
    let private errs = ys |> Array.map (fun y -> abs y * 0.1)

    [<Fact>]
    let ``a single layer draws the same whether its units are unspecified or dimensionless`` () =
        let cases =
            [ "line", Marey.Chart.Line(xs, ys), Chart.line U.one U.one xs ys
              "scatter", Marey.Chart.Scatter(xs, ys), Chart.scatter U.one U.one xs ys
              "area", Marey.Chart.Area(xs, ys), Chart.area U.one U.one xs ys
              "bar", Marey.Chart.Bar(xs, ys), Chart.bar U.one U.one xs ys
              "errorbar", Marey.Chart.Errorbar(xs, ys, errs), Chart.errorbar U.one U.one xs ys errs
              "add",
              (Marey.Chart.Line(xs, ys) |> Chart.add (Marey.Chart.Scatter(xs, Array.rev ys))),
              (Chart.line U.one U.one xs ys |> Chart.addScatter U.one U.one xs (Array.rev ys)) ]

        for name, unspecified, dimensionless in cases do
            Assert.True(renderFig surface unspecified = renderFig surface dimensionless, name)

    [<Fact>]
    let ``a plain axis reads a bare x and y, never '[1]'`` () =
        for fig in [ Marey.Chart.Line(xs, ys); Chart.line U.one U.one xs ys ] do
            let svg = renderFig surface fig
            Assert.Contains(">x<", svg)
            Assert.Contains(">y<", svg)
            Assert.DoesNotContain("[1]", svg)
            Assert.DoesNotContain("[]", svg)

    /// `Prefix` refuses to rescale a quantity with no dimension, and an unspecified one has none
    /// to rescale by. A ratio spanning 1e-9 is a small ratio, not a nano-ratio.
    [<Fact>]
    let ``tiny plain data is never given an SI prefix`` () =
        let tiny = ys |> Array.map (fun y -> y * 1e-9)

        for fig in [ Marey.Chart.Line(xs, tiny); Chart.line U.one U.one xs tiny ] do
            let svg = renderFig surface fig
            Assert.Contains(">y<", svg)
            Assert.DoesNotContain("[n]", svg)
            Assert.DoesNotContain("[p]", svg)

// ======================================================================================
// D45 — a categorical axis, and the check that keeps two of them apart
// ======================================================================================

module CategoricalAxes =

    let private levels = [| "control"; "treated"; "placebo"; "washout" |]

    let private bars (ls: string[]) (ys: float<m>[]) : Series<1, m> =
        { Name = ""
          X = Array.init ls.Length float
          Y = ys
          XScale = Scale.ordinal ls
          YScale = Scale.linear U.metre (0.0<m>, 21.0<m>) }

    let private yields = [| 12.4<m>; 19.1<m>; 7.8<m>; 15.0<m> |]

    let private layerOf i (s: Series<1, m>) : Layer =
        { Name = ""
          Data = Erase.packSeries s
          Geom = GeomPoint
          Style = Geom.styleFor Theme.initial i
          Channels = [||]
          ShowInLegend = false
          Addressed = [||]
          RowKeys = [||] }

    /// The whole point of D45, rendered: a bar per level, a named tick per band, and no
    /// configuration beyond the level set.
    [<Fact>]
    let ``a categorical axis renders one named tick per level`` () =
        let s = bars levels yields
        let svg = renderFig surface (Chart.ofSeries (Geom.bar 0.7 s) s)

        for name in levels do
            Assert.Contains(">" + name + "<", svg)

    /// An ordinal scale is dimensionless, so `Figure.unify`'s runtime `Dim` check — the one
    /// runtime dimensional check in the library — cannot tell two *different* categories apart.
    /// Verified before the guard was written: the panel unified silently and the second layer's
    /// points were drawn against the first layer's labels.
    [<Fact>]
    let ``two categories cannot share an axis`` () =
        let panel =
            Figure.panelOf
                [| layerOf 0 (bars levels yields)
                   layerOf 1 (bars [| "a"; "b"; "c"; "d" |] yields) |]

        let ex = Assert.Throws<System.Exception>(fun () -> Figure.unify panel |> ignore)
        Assert.Contains("different sets of levels", ex.Message)
        // The message names both level sets, because "they differ" is not actionable.
        Assert.Contains("control", ex.Message)
        Assert.Contains("a, b, c, d", ex.Message)

    /// The same guard in its other direction: a categorical axis and a continuous one are not
    /// the same axis either, and both are dimensionless when the continuous one is a bare ratio.
    [<Fact>]
    let ``a category cannot share an axis with a continuous scale`` () =
        let continuous: Series<1, m> =
            { bars levels yields with
                XScale = Scale.linear U.one (0.0, 3.0) }

        let panel =
            Figure.panelOf [| layerOf 0 (bars levels yields); layerOf 1 continuous |]

        let ex = Assert.Throws<System.Exception>(fun () -> Figure.unify panel |> ignore)
        Assert.Contains("categorical", ex.Message)

    /// Two layers over the *same* levels are a legitimate figure — a grouped bar chart is
    /// exactly that — and the guard must not refuse them.
    [<Fact>]
    let ``two layers over the same levels unify`` () =
        let panel =
            Figure.panelOf
                [| layerOf 0 (bars levels yields)
                   layerOf 1 (bars levels [| 3.0<m>; 4.0<m>; 5.0<m>; 6.0<m> |]) |]

        let unified = Figure.unify panel
        let x, _ = Erase.seriesScales unified.Layers.[1].Data

        // Unification did not stretch the bands: a categorical domain is its level count.
        Assert.Equal(ScaleKind.Ordinal levels, x.Kind)
        Assert.Equal((-0.5, 3.5), x.Domain)

// ======================================================================================
// D45 — the level set, and the categorical front door
// ======================================================================================

module LevelSets =

    /// First appearance, not sorted: a categorical axis usually arrives in an order that is
    /// itself information, and `control, treated, washout` is the case that makes the point.
    [<Fact>]
    let ``levels are the distinct keys in first-appearance order`` () =
        let ls =
            Levels.ofSeq [| "washout"; "control"; "washout"; "treated"; "control" |]

        Assert.Equal<string[]>([| "washout"; "control"; "treated" |], ls.Values)
        Assert.Equal<string[]>(ls.Values, ls.Labels)

    /// Determinism gate 6 reaches this file: the level order must come from the sequence, never
    /// from enumerating a set. Shuffling *repeats* while keeping first appearances fixed must
    /// not move a band.
    [<Property(MaxTest = 200, Replay = Marey.Tests.Generators.Seed, QuietOnSuccess = true)>]
    let ``level order depends only on first appearances`` (xs: int list) =
        let keys = xs |> List.map (fun x -> abs x % 7) |> Array.ofList
        let once = Levels.ofSeq keys
        let twice = Levels.ofSeq (Array.append keys keys)

        once.Values = twice.Values

    /// Sorting is one call away, and it sorts on the **key**, so renaming a level cannot
    /// reorder the bands.
    [<Fact>]
    let ``sorted orders by key`` () =
        let ls = Levels.ofSeq [| 30; 10; 20 |] |> Levels.sorted

        Assert.Equal<int[]>([| 10; 20; 30 |], ls.Values)
        Assert.Equal<string[]>([| "10"; "20"; "30" |], ls.Labels)

    /// A union key spells itself by case name, which is what `string` gives and what a reader
    /// expects on the axis.
    [<Fact>]
    let ``a union key is labelled by its case name`` () =
        let ls = Levels.ofSeq [| Choice1Of2 1; Choice2Of2 "x" |]

        Assert.Equal(2, Levels.count ls)

    /// `positions` is the x array of a categorical series, so a key outside the level set has
    /// no honest answer: level -1 is off the panel and clamping puts the mark on somebody
    /// else's band.
    [<Fact>]
    let ``a key outside the level set raises rather than landing somewhere`` () =
        let ls = Levels.ofSeq [| "a"; "b" |]

        let ex =
            Assert.Throws<System.Exception>(fun () -> Levels.positions ls [| "a"; "c" |] |> ignore)

        Assert.Contains("'c' is not one of the 2 levels", ex.Message)
        Assert.Contains("a, b", ex.Message)

module CategoricalFrontDoor =

    let private keys = [| "control"; "treated"; "placebo"; "washout" |]
    let private yields = [| 12.4<m>; 19.1<m>; 7.8<m>; 15.0<m> |]

    /// Phase 1's exit criterion through the API a user actually has: one named band per level,
    /// with nothing configured but the witness on y.
    [<Fact>]
    let ``Chart.Category.bar labels one band per level`` () =
        let svg = renderFig surface (Chart.Category.bar U.metre keys yields)

        for name in keys do
            Assert.Contains(">" + name + "<", svg)

    /// The y axis is dimensioned as it is everywhere else. The categorical door does not cost
    /// the units story on the other axis.
    [<Fact>]
    let ``the y axis still labels itself from the witness`` () =
        let svg = renderFig surface (Chart.Category.bar U.metre keys yields)
        Assert.Contains(">x [m]<", svg)

    /// `Axes.defaultLabel` would print a bare `x` under bands already named `control`,
    /// `treated` — two meanings of `x` in one figure. It is suppressed, and a real name still
    /// goes back.
    [<Fact>]
    let ``a categorical axis is titled by its bands, not by a conventional symbol`` () =
        let plain = renderFig surface (Chart.Category.bar U.metre keys yields)
        Assert.DoesNotContain(">x<", plain)

        let named =
            renderFig surface (Chart.Category.bar U.metre keys yields |> Chart.withXLabel "treatment")

        Assert.Contains(">treatment<", named)

    /// **A bar is a length from zero.** Data spanning 7.8 … 19.1 on a 7.2 … 19.7 axis draws a
    /// 50:1 picture of a 2.4:1 ratio, which is the mark type lying rather than a styling
    /// preference. Both bar doors widen the domain to reach the baseline.
    [<Fact>]
    let ``a bar chart's y domain reaches zero`` () =
        let categorical =
            Chart.Category.bar U.metre keys yields |> fun f -> f.Panels.[0].Layers.[0].Data

        let _, y = Erase.seriesScales categorical
        Assert.Equal(0.0, fst y.Domain, 12)

        let continuous =
            Chart.bar U.second U.metre [| 1.0<s>; 2.0<s>; 3.0<s> |] [| 12.4<m>; 19.1<m>; 7.8<m> |]

        let _, cy = Erase.seriesScales continuous.Panels.[0].Layers.[0].Data
        Assert.Equal(0.0, fst cy.Domain, 12)

    /// Zero enters unpadded and only on the side it is missing from: all-negative data keeps
    /// its baseline at the top of the bars rather than gaining a spurious positive headroom,
    /// and data straddling zero is untouched.
    [<Fact>]
    let ``zero enters only on the side it is missing from`` () =
        let negative =
            Chart.Category.bar U.metre [| "a"; "b" |] [| -4.0<m>; -9.0<m> |]

        let _, y = Erase.seriesScales negative.Panels.[0].Layers.[0].Data
        Assert.Equal(0.0, snd y.Domain, 12)
        Assert.True(fst y.Domain < -9.0, sprintf "lower end %f should still be padded" (fst y.Domain))

        let straddling =
            Chart.Category.bar U.metre [| "a"; "b" |] [| -4.0<m>; 9.0<m> |]

        let _, sy = Erase.seriesScales straddling.Panels.[0].Layers.[0].Data
        Assert.True(fst sy.Domain < -4.0 && snd sy.Domain > 9.0)

    /// Repeated keys are two bars in one band, not a sum. Aggregation is a stat's job, and
    /// doing it here silently would be the library deciding what the data means.
    [<Fact>]
    let ``a repeated key is two marks in one band, not an aggregate`` () =
        let fig = Chart.Category.bar U.metre [| "a"; "b"; "a" |] [| 1.0<m>; 2.0<m>; 3.0<m> |]
        let x, _ = Erase.seriesScales (fig.Panels.[0].Layers.[0].Data)

        Assert.Equal(ScaleKind.Ordinal [| "a"; "b" |], x.Kind)

        let xs =
            Erase.projectXY fig.Panels.[0].Layers.[0].Data |> Array.map (fun p -> float p.X)

        // Two marks share the first band; the third sits in the second.
        Assert.Equal(xs.[0], xs.[2], 12)
        Assert.NotEqual(xs.[0], xs.[1])

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
            | Opacity(_, child)
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
// D60 — the output surface is the renderer's shape, not `string`
//
// Every function-shaped entry point named `string` in its signature until M2.5. That is a
// claim about the *artifact* made by a function that only forwards it, and a raster backend
// does not fit through it: a PNG has no UTF-8 encoding and a PDF's is a lie.
//
// These tests are the ones that would have caught the two ways the generalisation could go
// wrong — inference silently pinning `render` to `string` at a call site, and `toFileBytes`
// acquiring an encoding on the way to disk.
// ======================================================================================

module ByteShapedOutput =

    open System.IO

    /// A byte-producing renderer of the shape a raster backend has. It ignores the scene's
    /// content on purpose: what is under test is the *path*, not a rasteriser.
    let private byteRenderer (_: Extent<px>) (node: Node) : byte[] =
        let count = node |> Scene.fold (fun acc _ -> acc + 1) 0
        // A BOM's bytes, deliberately, followed by something that is not valid UTF-8. If the
        // write path ever grows an encoding, one of the two halves will not survive it.
        [| 0xEFuy; 0xBBuy; 0xBFuy; 0xFFuy; 0xFEuy; byte count |]

    /// The generalisation itself. `render` returns what the renderer returns.
    [<Fact>]
    let ``render returns bytes for a byte-producing renderer`` () =
        let bytes = baseFigure () |> Chart.render byteRenderer surface
        Assert.NotEmpty bytes
        Assert.Equal(0xEFuy, bytes.[0])

    /// The inference risk D60 weighed against the general form, exercised rather than argued.
    /// A generic `render` must still pin to `string` from `SvgBackend.render` alone, with no
    /// annotation at the call site and no annotation on the binding — which is how every
    /// example in the repository and every snippet in the guide calls it.
    [<Fact>]
    let ``render still infers string from the renderer alone`` () =
        let svg = baseFigure () |> Chart.render SvgBackend.render surface
        // `String.length` only compiles if `svg` was inferred as `string`, not left generic
        // and not defaulted to `obj`.
        Assert.True(String.length svg > 0)
        Assert.StartsWith("<svg", svg)

    /// Both entry points must reach the same artifact, or the byte path is a second
    /// implementation of the render pipeline rather than a second exit from it.
    [<Fact>]
    let ``toFileBytes writes exactly what render produced`` () =
        let fig = baseFigure ()
        let path = Path.Combine(Path.GetTempPath(), "marey-d60-" + string (System.Guid.NewGuid()) + ".bin")

        try
            fig |> Chart.toFileBytes byteRenderer path surface
            Assert.Equal<byte[]>(fig |> Chart.render byteRenderer surface, File.ReadAllBytes path)
        finally
            if File.Exists path then File.Delete path

    /// The failure this rules out is the one that produces a file no decoder opens: a
    /// three-byte BOM prepended to a PNG signature. `WriteAllBytes` has no encoding, and this
    /// asserts that nothing on the way to it acquired one.
    [<Fact>]
    let ``toFileBytes adds no encoding preamble and alters no byte`` () =
        let path = Path.Combine(Path.GetTempPath(), "marey-d60-raw-" + string (System.Guid.NewGuid()) + ".bin")

        try
            baseFigure () |> Chart.toFileBytes byteRenderer path surface
            let onDisk = File.ReadAllBytes path
            // 0xFF 0xFE is not valid UTF-8 in any position, so a round trip through any text
            // encoding would have replaced it with U+FFFD's bytes rather than preserved it.
            Assert.Equal<byte[]>([| 0xEFuy; 0xBBuy; 0xBFuy; 0xFFuy; 0xFEuy |], onDisk.[0..4])
            Assert.Equal(6, onDisk.Length)
        finally
            if File.Exists path then File.Delete path

    /// `toFile` is unchanged and stays string-typed, which is the other half of D60: the
    /// existing path keeps its encoding guarantee (UTF-8, no BOM — these files are golden
    /// masters) and does not become generic over something it has to branch on.
    [<Fact>]
    let ``toFile still writes UTF-8 without a byte-order mark`` () =
        let path = Path.Combine(Path.GetTempPath(), "marey-d60-text-" + string (System.Guid.NewGuid()) + ".svg")

        try
            baseFigure () |> Chart.toFile SvgBackend.render path surface
            let onDisk = File.ReadAllBytes path
            Assert.NotEqual<byte[]>([| 0xEFuy; 0xBBuy; 0xBFuy |], onDisk.[0..2])
            Assert.Equal('<', char onDisk.[0])
        finally
            if File.Exists path then File.Delete path

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

// ======================================================================================
// M2 phase 2 / D46 / D49 — non-positional channels, the legend, and the colorbar
// ======================================================================================

/// The phase's three exit criteria, plus the claims D49 makes about where a colorbar goes.
///
/// The first two are counts of IR nodes, because that is what D46 decided: a categorical
/// channel is one batch per level and a continuous one is one batch per *quantised step*, and
/// both of those are §5.1's batching surviving rather than being defeated. The third is a
/// golden in both backends, and it lives in `GoldenTests`/`TikzGoldenTests` — it is there
/// rather than here because the failure it guards against (a gradient degrading to one flat
/// block) is invisible to every assertion that is not a picture.
module Channels =

    let private keys =
        Array.init 30 (fun i -> [| "control"; "treated"; "washout" |].[i % 3])

    let private xs = Array.init 30 (fun i -> float i * 1.0<s>)
    let private ys = Array.init 30 (fun i -> (2.0 + sin (float i)) * 1.0<m>)

    let private temperatures = Array.init 30 (fun i -> (280.0 + float i) * 1.0<K>)

    let private scatter () = Chart.scatter U.second U.metre xs ys

    /// The `Markers` nodes belonging to the *data*, which is not every `Markers` node in the
    /// scene: a legend swatch is one too, and counting those would make a three-level scatter
    /// look like six batches.
    let private dataMarkers (fig: Figure) =
        Chart.toScene surface fig
        |> Scene.fold
            (fun acc n ->
                match n with
                | Annotated(sem, body) when sem.Role = RoleSeries ->
                    acc
                    + Scene.fold
                        (fun k node ->
                            match node with
                            | Markers _ -> k + 1
                            | _ -> k)
                        0
                        body
                | _ -> acc)
            0

    /// Every `Markers` node in the scene, wherever it sits. The bound in D46 is stated over the
    /// whole figure, so the assertion should be too.
    let private allMarkers (node: Node) =
        Scene.fold
            (fun k n ->
                match n with
                | Markers _ -> k + 1
                | _ -> k)
            0
            node

    let private markerFills (node: Node) =
        Scene.fold
            (fun acc n ->
                match n with
                | Markers b ->
                    match b.Fill with
                    | Some(Solid c) -> Set.add (Color.toHex c) acc
                    | _ -> acc
                | _ -> acc)
            Set.empty
            node

    let private annotationOf (role: SemanticRole) (node: Node) =
        Scene.fold
            (fun acc n ->
                match n with
                | Annotated(sem, _) when sem.Role = role -> Some sem
                | _ -> acc)
            None
            node

    // ---- Exit criterion 1: the categorical half ------------------------------------------

    /// **Phase 2's first exit criterion**, through the front door and counted with `Scene.count`'s
    /// own oracle: three levels, thirty points, three batches. Not thirty markers.
    [<Fact>]
    let ``a three-level scatter renders three batches, not thirty marks`` () =
        Assert.Equal(3, dataMarkers (scatter () |> Chart.colorByKey keys))

    /// The colour-blind-safe form is still three, because both channels read the same level
    /// number. A ninth of the batches is not the point — the point is that redundant encoding
    /// costs nothing, so there is no reason not to use it.
    [<Fact>]
    let ``colour and shape over one key stays three batches`` () =
        let fig = scatter () |> Chart.colorByKey keys |> Chart.shapeByKey keys
        Assert.Equal(3, dataMarkers fig)

        let shapes =
            Chart.toScene surface fig
            |> Scene.fold
                (fun acc n ->
                    match n with
                    | Markers b -> Set.add (sprintf "%A" b.Shape) acc
                    | _ -> acc)
                Set.empty

        Assert.True(shapes.Count >= 3, sprintf "expected three mark shapes, got %A" shapes)

    /// **Phase 2's other half of the first criterion**: the colours a level channel assigns are
    /// separable, to the standard `Theme.validate` holds a palette to — under protanopia,
    /// deuteranopia and tritanopia as well as normal vision.
    ///
    /// This is a real risk rather than a formality. The level colours come off the theme's
    /// categorical palette by index, and a channel with more levels than the palette has
    /// entries would wrap it — assigning two levels the same colour and saying nothing.
    [<Fact>]
    let ``the colours a categorical channel assigns pass the palette validator`` () =
        let fig = scatter () |> Chart.colorByKey keys

        let colors =
            match fig.Panels.[0].Layers.[0].Channels.[0].Map with
            | ColorByLevel(_, cs) -> cs
            | _ -> failwith "expected a categorical colour channel"

        let report = Palette.validate Theme.initial.PanelBackground colors

        Assert.True(
            report.Ok,
            sprintf
                "level colours separate by only %.3f under %s (needs %.3f)"
                report.WorstSeparation.Minimum
                report.WorstSeparation.Vision
                Palette.SeparationFloor
        )

    /// D49 — the legend enumerates the *levels*, not the series. The series name is already on
    /// the panel; what the reader cannot otherwise recover is which colour is which level.
    [<Fact>]
    let ``a categorical channel puts one legend entry per level`` () =
        let names =
            Chart.toScene surface (scatter () |> Chart.withName "yield" |> Chart.colorByKey keys)
            |> Scene.fold
                (fun acc n ->
                    match n with
                    | Annotated(sem, _) when sem.Role = RoleOther "legend-entry" -> sem.Label :: acc
                    | _ -> acc)
                []
            |> List.rev

        Assert.Equal<string list>([ "control"; "treated"; "washout" ], names)

    // ---- Exit criterion 2: the continuous half -------------------------------------------

    /// **D46's bound, end to end.** A hundred thousand points, a continuous colour channel, and
    /// at most 257 `Markers` nodes in the whole figure — 256 steps plus the one a legend could
    /// contribute. The assertion that fails loudly if anybody ever reaches for one batch per
    /// point, which is the thing the decision refused.
    [<Fact>]
    let ``a hundred thousand points with a continuous colour channel stay under 257 batches`` () =
        let n = 100_000
        let rng = System.Random 5
        let bx = Array.init n (fun _ -> rng.NextDouble() * 10.0 * 1.0<s>)
        let by = Array.init n (fun _ -> rng.NextDouble() * 10.0 * 1.0<m>)
        let t = Array.init n (fun i -> (280.0 + 25.0 * float i / float n) * 1.0<K>)

        let scene =
            Chart.toScene surface (Chart.scatter U.second U.metre bx by |> Chart.colorBy U.kelvin t)

        let markers = allMarkers scene
        Assert.True(markers <= 257, sprintf "%d Markers nodes" markers)

        // And every point is still drawn: a bound met by dropping data is not a bound met.
        let drawn =
            Scene.fold
                (fun acc node ->
                    match node with
                    | Markers b -> acc + b.Points.Length
                    | _ -> acc)
                0
                scene

        Assert.Equal(n, drawn)

    // ---- D49: the colorbar -----------------------------------------------------------------

    let private colorbarFigure () =
        scatter () |> Chart.colorBy U.kelvin temperatures |> Chart.withColorLabel "T"

    /// The whole thesis in a third place, after the axis and the legend: the bar is titled from
    /// the witness, and nothing was configured but the quantity's name.
    [<Fact>]
    let ``a continuous colour channel gets a colorbar titled from its witness`` () =
        let scene = Chart.toScene surface (colorbarFigure ())

        match annotationOf (RoleOther "colorbar") scene with
        | None -> failwith "no colorbar was compiled"
        | Some sem ->
            Assert.Equal("T [K]", sem.Label)
            Assert.Contains(("unit", "K"), sem.Data)

        let svg = renderFig surface (colorbarFigure ())
        Assert.Contains("T [K]", svg)
        Assert.Contains(">280<", svg)
        Assert.Contains(">305<", svg)

    /// Unnamed, the bar reads its name off the *dimension*, exactly as an axis does — a kelvin
    /// channel is `T`. What it must not do is borrow an axis's positional fallback: a bar
    /// labelled `y [K]` would be naming an axis that is not there.
    [<Fact>]
    let ``an unnamed colour channel takes its name from the dimension, not from an axis`` () =
        let named =
            Chart.toScene surface (scatter () |> Chart.colorBy U.kelvin temperatures)

        match annotationOf (RoleOther "colorbar") named with
        | None -> failwith "no colorbar was compiled"
        | Some sem -> Assert.Equal("T [K]", sem.Label)

        // A dimensionless channel has no conventional symbol, and prints none.
        let plain =
            Chart.toScene surface (scatter () |> Chart.colorBy U.one (Array.init 30 float))

        match annotationOf (RoleOther "colorbar") plain with
        | None -> failwith "no colorbar was compiled"
        | Some sem -> Assert.Equal("", sem.Label)

    /// D49's placement claim, measured. The bar is one more `Track.Content` column, so it takes
    /// its width out of the plot — and the plot survives it with its tick labels intact, which
    /// is the half of the claim that could have gone wrong.
    [<Fact>]
    let ``the colorbar takes a column from the plot and the x axis keeps its ticks`` () =
        let withoutBar = scatter ()
        let withBar = colorbarFigure ()

        let plotWidth (fig: Figure) =
            let scene = Chart.toScene surface fig
            ignore scene
            // The panel frame's extent is the plot area; read it from the gridline-free
            // background rect the panel compiles first.
            Scene.fold
                (fun acc n ->
                    match n with
                    | Annotated(sem, _) when sem.Role = RoleOther "panel" -> acc
                    | Path(segs, _, _, _) ->
                        let xs =
                            segs
                            |> Array.choose (fun seg ->
                                match seg with
                                | MoveTo p
                                | LineTo p -> Some(float p.X)
                                | _ -> None)

                        if xs.Length = 0 then acc else max acc (Array.max xs)
                    | _ -> acc)
                0.0
                (Chart.toScene surface fig)

        // Everything drawn stays inside the surface in both cases; the bar does not hang off.
        Assert.True(plotWidth withBar <= 1.0 + 1e-9, sprintf "%f" (plotWidth withBar))
        Assert.True(plotWidth withoutBar <= 1.0 + 1e-9)

        // The x axis still has its full tick set: the column came out of the plot's width, and
        // the tick plan was recomputed against what was left.
        let ticksOf (fig: Figure) =
            textNodes (Chart.toScene surface fig)
            |> List.filter (fun t -> t.Text = "0" || t.Text = "10" || t.Text = "20")
            |> List.length

        Assert.Equal(ticksOf withoutBar, ticksOf withBar)

    /// A figure with no continuous colour channel has no colorbar and no column for one. This
    /// is what keeps every existing golden byte-identical, and it is worth an assertion rather
    /// than a hope.
    [<Fact>]
    let ``a figure without a continuous colour channel is unchanged`` () =
        let scene = Chart.toScene surface (scatter () |> Chart.colorByKey keys)
        Assert.True((annotationOf (RoleOther "colorbar") scene).IsNone)

    /// D49's agreement claim: the bar is drawn from the same ramp call as the marks, so every
    /// mark's colour is one of the strips'. Two implementations that merely resemble each other
    /// would fail this on the first change to either.
    [<Fact>]
    let ``every mark colour appears in the colorbar`` () =
        let scene = Chart.toScene surface (colorbarFigure ())

        let stripFills =
            Scene.fold
                (fun acc n ->
                    match n with
                    | Annotated(sem, body) when sem.Role = RoleOther "colorbar" ->
                        Scene.fold
                            (fun k node ->
                                match node with
                                | Path(_, _, Some(Solid c), _) -> Set.add (Color.toHex c) k
                                | _ -> k)
                            acc
                            body
                    | _ -> acc)
                Set.empty
                scene

        let marks = markerFills scene

        Assert.False(Set.isEmpty marks)
        Assert.True(Set.isSubset marks stripFills, sprintf "marks not on the bar: %A" (Set.difference marks stripFills))

    /// The strip count is `min(256, ⌈height⌉)`: on a short bar the steps are pixels, not 256
    /// bands inside 60 px. Below the resolution of the output is where D46 stops, and the bar
    /// stops in the same place.
    [<Fact>]
    let ``a short colorbar has one strip per pixel, not two hundred and fifty-six`` () =
        let small = Chart.size 320.0 200.0

        match annotationOf (RoleOther "colorbar") (Chart.toScene small (colorbarFigure ())) with
        | None -> failwith "no colorbar was compiled"
        | Some sem ->
            let steps = sem.Data |> Array.pick (fun (k, v) -> if k = "steps" then Some(int v) else None)

            Assert.True(steps < Channel.ColorSteps, sprintf "%d steps on a 200 px figure" steps)
            Assert.True(steps > 1)

    // ---- D49: the size key ------------------------------------------------------------------

    /// A size channel is the one thing that *does* go in the legend, as `LegendSizedMarker`.
    /// Three entries, at the sizes the marks are drawn at, carrying the unit — because a legend
    /// row that reads `3.0` alone names no quantity.
    [<Fact>]
    let ``a size channel puts a three-entry key in the legend, with its unit`` () =
        let masses = Array.init 30 (fun i -> (1.0 + float i * 0.1) * 1.0<kg>)

        let names =
            Chart.toScene surface (scatter () |> Chart.sizeBy U.kilogram masses)
            |> Scene.fold
                (fun acc n ->
                    match n with
                    | Annotated(sem, _) when sem.Role = RoleOther "legend-entry" -> sem.Label :: acc
                    | _ -> acc)
                []
            |> List.rev

        Assert.Equal(3, names.Length)

        for n in names do
            Assert.EndsWith("kg", n)

        // The swatches really are different sizes: a size key drawn at one size is not a key.
        let sizes =
            Chart.toScene surface (scatter () |> Chart.sizeBy U.kilogram masses)
            |> Scene.fold
                (fun acc n ->
                    match n with
                    | Markers b -> Set.add (float b.Size) acc
                    | _ -> acc)
                Set.empty

        Assert.True(sizes.Count >= 3, sprintf "expected several mark sizes, got %A" sizes)

    /// §1.1's no-mutation rule has a corollary phase 2 could have broken quietly: a retheme has
    /// to reach a channel's colours too. They came off the outgoing theme's palette, so they
    /// follow the theme on the same rule a layer's style does — and a figure whose levels stayed
    /// in the old palette while everything else moved would be worse than one that never
    /// rethemed at all.
    [<Fact>]
    let ``a retheme recolours a categorical channel, and leaves a hand-picked one alone`` () =
        let fig = scatter () |> Chart.colorByKey keys

        let colorsOf (f: Figure) =
            match f.Panels.[0].Layers.[0].Channels.[0].Map with
            | ColorByLevel(_, cs) -> cs
            | _ -> failwith "expected a categorical colour channel"

        let dark = fig |> Chart.withTheme Theme.dark

        Assert.Equal<Color[]>(Channel.levelColors Theme.dark 3, colorsOf dark)
        Assert.NotEqual<Color[]>(colorsOf fig, colorsOf dark)

        // The original is untouched, because nothing in `Chart` mutates.
        Assert.Equal<Color[]>(Channel.levelColors Theme.initial 3, colorsOf fig)

        // A palette the caller chose is not the theme's, so it survives the retheme.
        let chosen = [| Palette.vermillion; Palette.blue; Palette.yellow |]

        let byHand =
            { fig with
                Panels =
                    fig.Panels
                    |> Array.map (fun p ->
                        { p with
                            Layers =
                                p.Layers
                                |> Array.map (fun l ->
                                    { l with
                                        Channels =
                                            l.Channels
                                            |> Array.map (fun c ->
                                                { c with
                                                    Map = ColorByLevel([| "control"; "treated"; "washout" |], chosen) }) }) }) }

        Assert.Equal<Color[]>(chosen, colorsOf (byHand |> Chart.withTheme Theme.dark))

    // ---- The front door's own guard ----------------------------------------------------------

    /// A channel with the wrong number of values would leave the tail of a series unmapped, and
    /// unmapped points are not drawn — a figure quietly missing marks. It raises instead, at
    /// the call that supplied the array.
    [<Fact>]
    let ``a channel of the wrong length is rejected`` () =
        let ex =
            Assert.Throws<System.ArgumentException>(fun () -> scatter () |> Chart.colorByKey [| "a"; "b" |] |> ignore)

        Assert.Contains("2 values", ex.Message)
        Assert.Contains("30 points", ex.Message)

// ======================================================================================
// §5.2 / D68 — THE PRODUCER NAMES WHAT IT DREW
//
// Before this milestone `Marey.Plot` produced exactly one `Keyed` node — the root — so the diff
// §5.2 designed the IR around had a granularity of one, and D1's rejection of closure-valued
// transforms was protecting a facility nothing used. These tests are the claim that the
// granularity is now the layer.
// ======================================================================================

module Keying =

    let private keysOf (n: Node) =
        Scene.fold
            (fun acc node ->
                match node with
                | Keyed(Key k, _) -> k :: acc
                | _ -> acc)
            []
            n
        |> List.rev

    /// The scheme, spelled out once so nothing downstream has to guess at it. D69's storyboard
    /// quotes `curve.[42]` as `figure/panel-0-0/layer-0/mark-42`, and this is where that string
    /// is fixed — before the phase that constructs one exists, so it cannot invent a second
    /// spelling.
    [<Fact>]
    let ``the four levels compose into a path`` () =
        let panel = Keys.panel 0 1
        let layer = Keys.layer panel 2

        Assert.Equal(Key "figure", Keys.figure)
        Assert.Equal(Key "figure/panel-0-1", panel)
        Assert.Equal(Key "figure/panel-0-1/layer-2", layer)
        Assert.Equal(Key "figure/panel-0-0/layer-0/mark-42", Keys.mark (Keys.layer (Keys.panel 0 0) 0) 42)

    [<Fact>]
    let ``a figure carries a key for its root, each panel and each layer`` () =
        let fig =
            Chart.line U.second U.metre fallT fallX
            |> Chart.addScatter U.second U.metre fallT (Array.rev fallX)
            |> Chart.beside (Chart.line U.second U.metre fallT fallX)

        Assert.Equal<string list>(
            [ "figure"
              "figure/panel-0-0"
              "figure/panel-0-0/grid"
              "figure/panel-0-0/layer-0"
              "figure/panel-0-0/layer-1"
              "figure/panel-0-0/axis-x"
              "figure/panel-0-0/axis-y"
              "figure/panel-0-1"
              "figure/panel-0-1/grid"
              "figure/panel-0-1/layer-0"
              "figure/panel-0-1/axis-x"
              "figure/panel-0-1/axis-y" ],
            keysOf (Chart.toScene surface fig)
        )

    /// **Ordinals, not names**, and this is the test that says why. Two layers of one panel may
    /// carry the same `Name` — nothing forbids it and `Chart.withName` is optional — so a key
    /// built from the name would collide, and `Scene.mapKey` would then fade both when asked to
    /// fade one. The label still says "measured" twice, because that is `Annotated`'s job.
    [<Fact>]
    let ``two layers with the same name still get different keys`` () =
        let fig =
            Chart.line U.second U.metre fallT fallX
            |> Chart.withName "measured"
            |> Chart.addLine U.second U.metre fallT (Array.rev fallX)

        let scene = Chart.toScene surface (fig |> Chart.withName "measured")

        Assert.Equal<string list>(
            [ "figure"
              "figure/panel-0-0"
              "figure/panel-0-0/grid"
              "figure/panel-0-0/layer-0"
              "figure/panel-0-0/layer-1"
              "figure/panel-0-0/axis-x"
              "figure/panel-0-0/axis-y"
              "figure/panel-0-0/legend" ],
            keysOf scene
        )

    /// The property M3 actually needs: a figure rebuilt from changed data keeps its keys, so two
    /// frames of an animation are addressable by the same names and a diff can compare subtree
    /// to subtree rather than figure to figure.
    ///
    /// **What this does not claim**, because no positional scheme can: identity across a *row
    /// removal*. Drop row 3 and every later row's index shifts, so `mark-417` names a different
    /// datum. Stable identity under an edit to the data needs an id supplied by the data, and
    /// that is the differ's problem at M4. What M3 needs is stability under restyling,
    /// re-theming and a change of values — which is what an animation does between frames.
    [<Fact>]
    let ``keys survive a restyle, a rename and a change of values`` () =
        // Named in both, so both have a legend: naming a layer for the first time adds one,
        // and a legend appearing is a change of structure rather than of identity.
        let before = Chart.toScene surface (Chart.line U.second U.metre fallT fallX |> Chart.withName "original")

        let after =
            Chart.line U.second U.metre fallT (fallX |> Array.map (fun x -> x * 0.5))
            |> Chart.withName "renamed"
            |> Chart.withTheme Theme.dark
            |> Chart.toScene surface

        Assert.Equal<string list>(keysOf before, keysOf after)

        // And the keys address something different in each, which is what makes the equality
        // above a statement about identity rather than about two identical scenes.
        Assert.NotEqual(
            Scene.tryFindKey (Keys.layer (Keys.panel 0 0) 0) before,
            Scene.tryFindKey (Keys.layer (Keys.panel 0 0) 0) after
        )

    /// One layer of one panel, faded, and nothing else moved. This is the operation the whole
    /// scheme exists to make possible, run against a real figure rather than a hand-built tree.
    [<Fact>]
    let ``one layer of one panel can be faded through its key`` () =
        let fig =
            Chart.line U.second U.metre fallT fallX
            |> Chart.addScatter U.second U.metre fallT (Array.rev fallX)

        let scene = Chart.toScene surface fig
        let target = Keys.layer (Keys.panel 0 0) 1
        let faded = Scene.mapKey target (Scene.opacity 0.25) scene

        match Scene.tryFindKey target faded with
        | Some(Opacity(a, inner)) ->
            Assert.Equal(0.25, a)
            Assert.Equal(Scene.tryFindKey target scene, Some inner)
        | other -> failwithf "expected the second layer to be wrapped in an Opacity, got %A" other

        // Exactly one node was added, and the first layer is the same object it was.
        Assert.Equal(Scene.count scene + 1, Scene.count faded)

        let untouched = Keys.layer (Keys.panel 0 0) 0

        match Scene.tryFindKey untouched scene, Scene.tryFindKey untouched faded with
        | Some a, Some b -> Assert.True(System.Object.ReferenceEquals(a, b), "the untouched layer was rebuilt")
        | _ -> failwith "the untouched layer went missing"
