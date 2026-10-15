/// M9 — **composition**: where panels sit, how big their plots are, and which axes they share.
///
/// Every gate here reads the plot rectangles back through `Chart.panelsIn`, which takes them from
/// the same inner grid the drawing pass uses, so a rectangle that is right here is right in every
/// backend.
module Marey.Tests.CompositionTests

open System
open Xunit
open Marey
open Marey.UnitsOfMeasure

let private measurer = TextMeasure.approx

let private wobble (i: int) =
    let x = sin (float i * 12.9898) * 43758.5453
    2.0 * (x - floor x) - 1.0

let private t = Array.init 31 (fun i -> float i * 0.2<s>)

let private model (ti: float<s>) =
    12e-3<m> * exp (-ti / 1.8<s>) * cos (2.0 * Math.PI * 1.4<1/s> * ti)

let private sigma = Array.create t.Length 0.6e-3<m>
let private x = t |> Array.mapi (fun i ti -> model ti + sigma.[i] * wobble i)

/// P1's pair: the fit's y labels are `10` wide, the residuals' `−0.5`, so their y gutters differ.
let private fit = Chart.errorbar U.second U.metre t x sigma |> Chart.withName "measured"

let private residuals (scale: float) =
    Chart.errorbar U.second U.metre t (Array.map2 (fun xi ti -> scale * (xi - model ti)) x t) sigma
    |> Chart.addHLine U.metre 0.0<m>
    |> Chart.withYLabel "residual"

let rec private texts (n: Node) : string list =
    match n with
    | Text t -> [ t.Text ]
    | Group ns -> ns |> List.ofArray |> List.collect texts
    | Transform(_, c)
    | Clip(_, c)
    | Opacity(_, c)
    | Keyed(_, c)
    | Annotated(_, c) -> texts c
    | _ -> []

let private viewsOf (extent: Extent<px>) (fig: Figure) =
    Chart.panelsIn measurer (Chart.arrangeFor measurer extent fig) fig

let private close (a: float<px>) (b: float<px>) = abs (float (a - b)) < 0.01

/// Every two panels that start in one column share a plot x0, and that end in one share an x1;
/// likewise rows and y. A panel's span is read off the figure.
let private assertAlignedIn (fig: Figure) (views: PanelView[]) =
    let spanOf (v: PanelView) =
        fig.Panels |> Array.find (fun p -> p.Row = v.Row && p.Col = v.Col) |> fun p -> p.RowSpan, p.ColSpan

    for a in views do
        let ar, ac = spanOf a

        for b in views do
            let br, bc = spanOf b
            let pair = sprintf "panels %d,%d and %d,%d" a.Row a.Col b.Row b.Col

            if a.Col = b.Col then Assert.True(close a.Plot.X0 b.Plot.X0, pair + ": x0")
            if a.Col + ac = b.Col + bc then Assert.True(close a.Plot.X1 b.Plot.X1, pair + ": x1")
            if a.Row = b.Row then Assert.True(close a.Plot.Y0 b.Plot.Y0, pair + ": y0")
            if a.Row + ar = b.Row + br then Assert.True(close a.Plot.Y1 b.Plot.Y1, pair + ": y1")

let private assertAligned (views: PanelView[]) =
    for a in views do
        for b in views do
            if a.Col = b.Col then
                Assert.True(close a.Plot.X0 b.Plot.X0 && close a.Plot.X1 b.Plot.X1, sprintf "column %d: x %g..%g against %g..%g" a.Col (float a.Plot.X0) (float a.Plot.X1) (float b.Plot.X0) (float b.Plot.X1))

            if a.Row = b.Row then
                Assert.True(close a.Plot.Y0 b.Plot.Y0 && close a.Plot.Y1 b.Plot.Y1, sprintf "row %d: y %g..%g against %g..%g" a.Row (float a.Plot.Y0) (float a.Plot.Y1) (float b.Plot.Y0) (float b.Plot.Y1))

module ``D173 panels line up`` =

    [<Fact>]
    let ``a stack whose y tick labels differ in width lines up its plots (gate 1, C1)`` () =
        let views = fit |> Chart.above (residuals 1.0) |> viewsOf (Chart.size 500.0 420.0)
        Assert.Equal(2, views.Length)
        assertAligned views

    [<Fact>]
    let ``a row where one panel has a title lines up its plots (gate 1, C4)`` () =
        let titled = Chart.line U.second U.metre t x |> Chart.withPanelTitle "a titled panel"
        let views = fit |> Chart.beside titled |> viewsOf (Chart.size 700.0 300.0)
        assertAligned views

    [<Fact>]
    let ``a two-by-two grid with a colorbar and an outside legend lines up its plots (gate 1)`` () =
        let barred =
            Chart.scatter U.second U.metre t x
            |> Chart.colorBy U.metre x

        let legended = fit |> Chart.withLegend LegendOutsideRight
        let fig = (barred |> Chart.beside fit) |> Chart.above (fit |> Chart.beside legended)
        assertAligned (viewsOf (Chart.size 760.0 560.0) fig)

    [<Fact>]
    let ``every multi-panel golden lines up its plots (gate 1)`` () =
        for name, extent, fig in
            [ "figure-rules", GoldenScenes.rulesSurface, GoldenScenes.rulesFigure
              "figure-annotate", GoldenScenes.annotateSurface, GoldenScenes.annotateFigure
              "figure-ticks", GoldenScenes.ticksSurface, GoldenScenes.ticksFigure
              "figure-publication", GoldenScenes.publicationSurface, GoldenScenes.publicationFigure
              "figure-legends", GoldenScenes.legendsSurface, GoldenScenes.legendsFigure
              "figure-legend-shared", GoldenScenes.sharedLegendSurface, GoldenScenes.sharedLegendFigure ] do
            let views = viewsOf extent fig
            Assert.True(views.Length > 1, name)
            assertAligned views

    [<Fact>]
    let ``equal weights give equal plots, not equal cells`` () =
        let views = fit |> Chart.above (residuals 1.0) |> viewsOf (Chart.size 500.0 420.0)
        let h (v: PanelView) = float (v.Plot.Y1 - v.Plot.Y0)
        Assert.Equal(h views.[0], h views.[1], 6)

    [<Fact>]
    let ``a panel alone is placed exactly as before alignment existed`` () =
        // A figure of one cell skips the second pass, so its arrangement is pass 1's.
        let fig = fit
        let shot = Chart.arrangeFor measurer (Chart.size 480.0 300.0) fig
        let _, layout = shot.Panels.[0]
        Assert.Equal(0.0<px>, layout.SlackLeft + layout.SlackRight + layout.SlackTop + layout.SlackBottom)

    [<Fact>]
    let ``an animated stack keeps its plots lined up on every frame (gate 9)`` () =
        // The residuals grow a hundredfold, so their y labels gain digits frame by frame.
        let frame (u: float) = fit |> Chart.above (residuals (1.0 + 99.0 * u))
        let extent = Chart.size 500.0 420.0
        let shot = Figure.arrangeUnion 16 measurer extent frame

        let first = Chart.panelsIn measurer shot (frame 0.0)

        for k in 0..15 do
            let views = Chart.panelsIn measurer shot (frame (float k / 15.0))
            assertAligned views

            for v, f in Array.zip views first do
                Assert.Equal(float f.Plot.X0, float v.Plot.X0, 9)
                Assert.Equal(float f.Plot.Y1, float v.Plot.Y1, 9)

module ``D174 spans`` =

    let private single = Chart.scatter U.metre U.metre x (Array.map2 (fun xi ti -> xi - model ti) x t)
    let private extent = Chart.size 700.0 420.0

    let private find (fig: Figure) (row, col) =
        fig.Panels |> Array.find (fun p -> p.Row = row && p.Col = col)

    [<Fact>]
    let ``a panel beside a stack of two spans both rows (gate 3, C5)`` () =
        let fig = (fit |> Chart.above (residuals 1.0)) |> Chart.beside single
        Assert.Equal((2, 2), (fig.Rows, fig.Cols))
        Assert.Equal((2, 1), (let p = find fig (0, 1) in p.RowSpan, p.ColSpan))

        let views = viewsOf extent fig
        let at r c = views |> Array.find (fun v -> v.Row = r && v.Col = c)
        Assert.True(close (at 0 1).Plot.Y0 (at 0 0).Plot.Y0)
        Assert.True(close (at 0 1).Plot.Y1 (at 1 0).Plot.Y1)
        assertAlignedIn fig views

    [<Fact>]
    let ``a spanning panel ends on its last row's line, not its first's (gate 3)`` () =
        // A legend below the top-left panel makes row 0's bottom gutter taller than row 1's, so a
        // span measured against the wrong row would stop short of (or past) the lower panel.
        let fig = (fit |> Chart.withLegend LegendOutsideBottom |> Chart.above (residuals 1.0)) |> Chart.beside single
        let views = viewsOf extent fig
        let at r c = views |> Array.find (fun v -> v.Row = r && v.Col = c)
        Assert.True(close (at 0 1).Plot.Y1 (at 1 0).Plot.Y1)
        assertAlignedIn fig views

    [<Fact>]
    let ``equal counts compose exactly as before`` () =
        let fig = (fit |> Chart.beside fit) |> Chart.above (fit |> Chart.beside fit)
        Assert.Equal((2, 2), (fig.Rows, fig.Cols))
        Assert.True(fig.Panels |> Array.forall (fun p -> p.RowSpan = 1 && p.ColSpan = 1))
        Assert.Equal<(int * int)[]>([| 0, 0; 0, 1; 1, 0; 1, 1 |], fig.Panels |> Array.map (fun p -> p.Row, p.Col))

    [<Fact>]
    let ``three beside two takes six rows`` () =
        let three = fit |> Chart.above fit |> Chart.above fit
        let two = fit |> Chart.above fit
        let fig = three |> Chart.beside two
        Assert.Equal(6, fig.Rows)
        Assert.Equal<(int * int * int)[]>([| 0, 0, 2; 2, 0, 2; 4, 0, 2; 0, 1, 3; 3, 1, 3 |], fig.Panels |> Array.map (fun p -> p.Row, p.Col, p.RowSpan))
        assertAlignedIn fig (viewsOf extent fig)

    [<Fact>]
    let ``a mosaic places each part on its rectangle (gate 3)`` () =
        // A pinwheel, which no nesting of `beside` and `above` reaches.
        let fig = Chart.mosaic "AAB;DEB;DCC" [ 'A', fit; 'B', single; 'C', residuals 1.0; 'D', single; 'E', fit ]
        Assert.Equal((3, 3), (fig.Rows, fig.Cols))

        Assert.Equal<(int * int * int * int)[]>(
            [| 0, 0, 1, 2; 0, 2, 2, 1; 1, 0, 2, 1; 1, 1, 1, 1; 2, 1, 1, 2 |],
            fig.Panels |> Array.map (fun p -> p.Row, p.Col, p.RowSpan, p.ColSpan)
        )

        assertAlignedIn fig (viewsOf extent fig)

    [<Fact>]
    let ``a mosaic takes newlines and empty cells`` () =
        let fig = Chart.mosaic "A.\nAB" [ 'A', fit; 'B', single ]
        Assert.Equal(2, fig.Panels.Length)
        Assert.Equal((2, 1), (let p = find fig (0, 0) in p.RowSpan, p.ColSpan))

    [<Fact>]
    let ``a mosaic refuses what it cannot place, naming it (gate 3)`` () =
        let refused (f: unit -> Figure) (needle: string) =
            let e = Assert.ThrowsAny<exn>(fun () -> f () |> ignore)
            Assert.Contains(needle, e.Message)

        refused (fun () -> Chart.mosaic "AB;BA" [ 'A', fit; 'B', single ]) "'A' does not cover a rectangle"
        refused (fun () -> Chart.mosaic "AB;A" [ 'A', fit; 'B', single ]) "row 1"
        refused (fun () -> Chart.mosaic "AB" [ 'A', fit ]) "'B' has no part"
        refused (fun () -> Chart.mosaic "A" [ 'A', fit; 'Z', single ]) "'Z' has no cell"
        refused (fun () -> Chart.mosaic "AB" [ 'A', fit |> Chart.beside fit; 'B', single ]) "'A' is a 2-panel figure"

    [<Fact>]
    let ``letters follow the reading order of top-left cells`` () =
        let fig = Chart.mosaic "AB;AC" [ 'A', fit; 'B', single; 'C', residuals 1.0 ] |> Chart.withPanelLetters LetterParen
        let scene = Chart.toSceneWith measurer extent fig
        let letters = texts scene |> List.filter (fun s -> s.StartsWith "(")
        Assert.Equal<string list>([ "(a)"; "(b)"; "(c)" ], letters)
