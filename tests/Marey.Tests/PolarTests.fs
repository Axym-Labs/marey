/// **A plain polar figure draws its data where it belongs.** No stated domain, no box aspect:
/// the angle is drawn at its true angle, the radius starts at the pole, the circle is round, and
/// the axes read as an angle round the rim and a radius along a ray.
///
/// Every rule here is the coord's, asked of it rather than of which coord it is, so a cartesian
/// panel over the same data keeps the padding and the free shape it always had.
module Marey.Tests.PolarTests

open Xunit
open Marey
open Marey.UnitsOfMeasure

let private measurer = TextMeasure.approx
let private surface = Chart.size 640.0 400.0
let private turn = 2.0 * System.Math.PI

let private angles = Array.init 201 (fun i -> turn * float i / 200.0)

/// The repro: a figure-eight along the horizontal axis, unitless.
let private eight =
    Chart.lineIn U.unspecified U.unspecified angles (angles |> Array.map (fun a -> cos a * cos a))
    |> Chart.withCoord Coord.polar

let private view (fig: Figure) =
    (Chart.panelsIn measurer (Chart.arrangeFor measurer surface fig) fig).[0]

let private near (tolerance: float) (expected: float) (actual: float) =
    Assert.True(abs (expected - actual) <= tolerance, sprintf "expected %g, got %g" expected actual)

/// A data point through the panel's scales and coord, in its unit square.
let private drawnAt (v: PanelView) (x: float) (y: float) =
    let p = v.Coord.ToPanel { X = v.X.Project x; Y = v.Y.Project y }
    float p.X, float p.Y

let private texts (node: Node) =
    node
    |> Scene.fold
        (fun acc n ->
            match n with
            | Text t -> t :: acc
            | _ -> acc)
        []

let private scene (fig: Figure) = Chart.toScene surface fig

/// The plot rectangle in figure nd, y up, as the scene's text is placed.
let private plotNd (v: PanelView) =
    Layout.ndRect surface v.Plot

let private textAt (label: string) (node: Node) =
    match texts node |> List.filter (fun t -> t.Text = label) with
    | [ t ] -> t
    | ts -> failwithf "expected one '%s', found %d" label ts.Length

module ``the domains a polar panel takes`` =

    [<Fact>]
    let ``the angle of unitless data spans the data exactly`` () =
        let v = view eight
        Assert.Equal((0.0, turn), v.X.Domain)

    [<Fact>]
    let ``the radius starts at the pole and keeps room outside`` () =
        let v = view eight
        let lo, hi = v.Y.Domain
        Assert.Equal(0.0, lo)
        near 1e-12 1.05 hi

    [<Fact>]
    let ``the figure-eight is symmetric about both axes and meets at the pole`` () =
        let v = view eight
        let x0, y0 = drawnAt v 0.0 1.0
        let xPi, yPi = drawnAt v System.Math.PI 1.0
        let xp, yp = drawnAt v (System.Math.PI / 2.0) 0.0
        near 1e-9 0.5 yp
        near 1e-9 0.5 xp
        near 1e-9 0.5 y0
        near 1e-9 0.5 yPi
        near 1e-9 (x0 - 0.5) (0.5 - xPi)
        Assert.True(x0 > 0.97, sprintf "θ=0 drawn at %g" x0)

    [<Fact>]
    let ``an angle in radians spans the whole turn`` () =
        let fig =
            Chart.lineIn U.radian U.metre [| 0.0<rad>; 6.0<rad> |] [| 1.0<m>; 3.0<m> |]
            |> Chart.withCoord Coord.polar

        let v = view fig
        Assert.Equal((0.0, turn), v.X.Domain)
        Assert.Equal(0.0, fst v.Y.Domain)

    [<Fact>]
    let ``an angle in degrees spans the whole turn in degrees`` () =
        let fig =
            Chart.lineIn U.degree U.metre [| 0.0<deg>; 90.0<deg>; 180.0<deg> |] [| 1.0<m>; 2.0<m>; 3.0<m> |]
            |> Chart.withCoord Coord.polar

        let lo, hi = (view fig).X.Domain
        near 1e-9 0.0 lo
        near 1e-9 360.0 hi

    [<Fact>]
    let ``a half-disc in radians spans half a turn`` () =
        let fig =
            Chart.lineIn U.radian U.metre [| 0.0<rad>; 1.0<rad> |] [| 1.0<m>; 3.0<m> |]
            |> Chart.withCoord (Coord.polarBetween 0.0<rad> (System.Math.PI * 1.0<rad>) 0.0)

        let lo, hi = (view fig).X.Domain
        near 1e-12 0.0 lo
        near 1e-12 System.Math.PI hi

    [<Fact>]
    let ``a radius below zero starts at its least value`` () =
        let fig =
            Chart.lineIn U.unspecified U.unspecified [| 0.0; 1.0; 2.0 |] [| -1.0; 0.5; 2.0 |] |> Chart.withCoord Coord.polar

        Assert.Equal(-1.0, fst (view fig).Y.Domain)

    [<Fact>]
    let ``stated domains are kept`` () =
        let fig = eight |> Chart.withXDomain 1.0 2.0 |> Chart.withYDomain 0.5 3.0
        let v = view fig
        Assert.Equal((1.0, 2.0), v.X.Domain)
        Assert.Equal((0.5, 3.0), v.Y.Domain)

    [<Fact>]
    let ``a cartesian panel over the same data keeps its padding`` () =
        let fig = Chart.lineIn U.unspecified U.unspecified angles (angles |> Array.map (fun a -> cos a * cos a))
        let lo, hi = (view fig).X.Domain
        near 1e-12 (-0.05 * turn) lo
        near 1e-12 (1.05 * turn) hi

    [<Fact>]
    let ``a flipped polar panel gives the turn to y`` () =
        let fig =
            Chart.lineIn U.metre U.radian [| 1.0<m>; 3.0<m> |] [| 0.0<rad>; 6.0<rad> |]
            |> Chart.withCoord (Coord.flipped Coord.polar)

        let v = view fig
        Assert.Equal((0.0, turn), v.Y.Domain)
        Assert.Equal(0.0, fst v.X.Domain)

module ``the shape of a polar panel`` =

    let private ratio (v: PanelView) = Rect.height v.Plot / Rect.width v.Plot

    [<Fact>]
    let ``a full circle is drawn in a square`` () = near 1e-6 1.0 (ratio (view eight))

    [<Fact>]
    let ``a half-disc is drawn in a box twice as wide as tall`` () =
        let fig = eight |> Chart.withCoord (Coord.polarBetween 0.0<rad> (System.Math.PI * 1.0<rad>) 0.0)
        near 1e-6 0.5 (ratio (view fig))

    [<Fact>]
    let ``a stated box aspect wins`` () =
        near 1e-6 0.5 (ratio (view (eight |> Chart.withBoxAspect 0.5)))

    [<Fact>]
    let ``a cartesian panel keeps the shape its region gives it`` () =
        let v = view (Chart.lineIn U.unspecified U.unspecified angles angles)
        Assert.True(ratio v < 0.9)

    [<Fact>]
    let ``a half-disc fills its box`` () =
        let c = Coord.polarBetween 0.0<rad> (System.Math.PI * 1.0<rad>) 0.0

        let at (x: float) (y: float) =
            let p = c.ToPanel { X = x * 1.0<nd>; Y = y * 1.0<nd> }
            float p.X, float p.Y

        let check (ex: float, ey: float) (ax: float, ay: float) =
            near 1e-12 ex ax
            near 1e-12 ey ay

        check (1.0, 0.0) (at 0.0 1.0)
        check (0.5, 1.0) (at 0.5 1.0)
        check (0.0, 0.0) (at 1.0 1.0)
        check (0.5, 0.0) (at 0.3 0.0)

    [<Fact>]
    let ``a half-disc's inverse undoes it`` () =
        let c = Coord.polarBetween 0.0<rad> (System.Math.PI * 1.0<rad>) 0.0

        for x, y in [ 0.1, 0.3; 0.5, 0.9; 0.8, 0.5 ] do
            let p = c.ToPanel { X = x * 1.0<nd>; Y = y * 1.0<nd> }

            match c.FromPanel p with
            | Some q ->
                near 1e-9 x (float q.X)
                near 1e-9 y (float q.Y)
            | None -> failwith "no inverse"

    [<Fact>]
    let ``the full circle is drawn where it always was`` () =
        let p = Coord.polar.ToPanel { X = 0.125<nd>; Y = 1.0<nd> }
        near 1e-12 (0.5 + 0.5 * cos (System.Math.PI / 4.0)) (float p.X)
        near 1e-12 (0.5 + 0.5 * sin (System.Math.PI / 4.0)) (float p.Y)

module ``the axes of a polar panel`` =

    let private spiral =
        Chart.lineIn U.radian U.metre [| 0.0<rad>; 6.0<rad> |] [| 1.0<m>; 3.0<m> |]
        |> Chart.withCoord Coord.polar

    [<Fact>]
    let ``the angle in radians is ticked in eighths of a turn, read in π`` () =
        let v = view spiral
        let labels = v.XAxis.Ticks |> Array.map (fun t -> t.Label)
        Assert.Equal<string[]>([| "0"; "π/4"; "π/2"; "3π/4"; "π"; "5π/4"; "3π/2"; "7π/4"; "2π" |], labels)

    [<Fact>]
    let ``the angle in degrees is ticked every 45°`` () =
        let fig =
            Chart.lineIn U.degree U.metre [| 0.0<deg>; 180.0<deg> |] [| 1.0<m>; 3.0<m> |]
            |> Chart.withCoord Coord.polar

        let labels = (view fig).XAxis.Ticks |> Array.map (fun t -> t.Label)
        Assert.Equal<string[]>([| "0"; "45"; "90"; "135"; "180"; "225"; "270"; "315"; "360" |], labels)

    [<Fact>]
    let ``the angle labels stand round the rim and the turn's end is not labelled twice`` () =
        let s = scene spiral
        let plot = plotNd (view spiral)
        let cx, cy = (plot.X0 + plot.X1) / 2.0, (plot.Y0 + plot.Y1) / 2.0
        let all = texts s |> List.map (fun t -> t.Text)
        Assert.DoesNotContain("2π", all)
        Assert.True((textAt "0" s).At.X > plot.X1, "0 right of the circle")
        Assert.True((textAt "π/2" s).At.Y > plot.Y1, "π/2 above it")
        Assert.True((textAt "π" s).At.X < plot.X0, "π left of it")
        Assert.True((textAt "3π/2" s).At.Y < plot.Y0, "3π/2 below it")
        near 1e-9 (float cy) (float (textAt "π" s).At.Y)
        near 1e-9 (float cx) (float (textAt "π/2" s).At.X)

    [<Fact>]
    let ``the radius is labelled along the ray at angle zero, inside the circle`` () =
        let s = scene spiral
        let v = view spiral
        let plot = plotNd v
        let cx, cy = (plot.X0 + plot.X1) / 2.0, (plot.Y0 + plot.Y1) / 2.0

        let radial =
            v.YAxis.Ticks
            |> Array.filter (fun t -> float t.Position > 0.0 && float t.Position < 1.0)
            |> Array.choose (fun t -> texts s |> List.tryFind (fun r -> r.Text = t.Label))

        Assert.NotEmpty radial

        for t in radial do
            Assert.True(t.At.X > cx && t.At.X < plot.X1, sprintf "'%s' along the ray" t.Text)
            Assert.True(t.At.Y < cy && t.At.Y > plot.Y0, sprintf "'%s' just below it" t.Text)

    [<Fact>]
    let ``the axes are titled θ and r`` () =
        let v = view spiral
        Assert.Equal("θ [rad]", v.XAxis.Title)
        Assert.Equal("r [m]", v.YAxis.Title)
        let unitless = view eight
        Assert.Equal("θ", unitless.XAxis.Title)
        Assert.Equal("r", unitless.YAxis.Title)

    [<Fact>]
    let ``every label stands inside the figure`` () =
        for fig in [ spiral; eight ] do
            for t in texts (scene fig) do
                let b = TextMeasure.bounds measurer t.Font t.Anchor t.Baseline t.Rotation t.Text
                let x = float t.At.X * float surface.Width
                let y = (1.0 - float t.At.Y) * float surface.Height
                Assert.True(x + float b.X0 >= -0.5 && x + float b.X1 <= float surface.Width + 0.5, sprintf "'%s' across" t.Text)
                Assert.True(y + float b.Y0 >= -0.5 && y + float b.Y1 <= float surface.Height + 0.5, sprintf "'%s' up" t.Text)

    [<Fact>]
    let ``no angle label overlaps the plot`` () =
        let s = scene spiral
        let v = view spiral

        for t in v.XAxis.Ticks |> Array.filter (fun t -> t.Label <> "2π") do
            let r = textAt t.Label s
            let b = TextMeasure.bounds measurer r.Font r.Anchor r.Baseline r.Rotation r.Text
            let x = float r.At.X * float surface.Width
            let y = (1.0 - float r.At.Y) * float surface.Height
            // The box's nearest point to the centre is outside the inscribed circle.
            let cx, cy = (float v.Plot.X0 + float v.Plot.X1) / 2.0, (float v.Plot.Y0 + float v.Plot.Y1) / 2.0
            let nx = max (x + float b.X0) (min cx (x + float b.X1))
            let ny = max (y + float b.Y0) (min cy (y + float b.Y1))
            let radius = float (Rect.width v.Plot) / 2.0
            Assert.True(sqrt ((nx - cx) ** 2.0 + (ny - cy) ** 2.0) >= radius, sprintf "'%s' clear of the rim" t.Label)

    [<Fact>]
    let ``a cartesian panel's axes are where they were`` () =
        let fig = Chart.lineIn U.radian U.metre [| 0.0<rad>; 6.0<rad> |] [| 1.0<m>; 3.0<m> |]
        let v = view fig
        let s = scene fig
        let plot = plotNd v
        let zero = texts s |> List.filter (fun t -> t.Text = "0") |> List.head
        Assert.True(zero.At.Y < plot.Y0, "the x labels below the plot")
        Assert.Equal("x [m]", v.YAxis.Title)
