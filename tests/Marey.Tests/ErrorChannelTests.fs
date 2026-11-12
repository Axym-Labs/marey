/// The block's error channels: `xError`, `yError` and `errorLabel`.
///
/// An error bar is a per-row length along an axis, so it is a channel of the block and not a geom
/// handed to `layer`: it is split with the rows by a facet, it widens the domain it lies along,
/// and it is checked against the axis it belongs to when the block runs.
module Marey.Tests.ErrorChannelTests

open System
open Xunit
open Marey

type private Row =
    { T: float<s>
      X: float<m>
      DX: float<m>
      DT: float<s>
      Group: string }

let private rows =
    [ for i in 0..5 ->
          { T = float i * 1.0<s>
            X = (1.0 + 0.5 * float i) * 1.0<m>
            DX = (0.1 + 0.02 * float i) * 1.0<m>
            DT = 0.2<s>
            Group = (if i % 2 = 0 then "even" else "odd") } ]

let private layersOf (fig: Figure) = fig.Panels.[0].Layers

let private ends (l: Layer) =
    match l.Geom with
    | GeomErrorY(low, high)
    | GeomErrorX(low, high) -> Array.map float low, Array.map float high
    | g -> failwithf "not an error-bar layer: %A" g

let private close (expected: float[]) (actual: float[]) =
    Assert.Equal(expected.Length, actual.Length)
    Array.iter2 (fun (e: float) a -> Assert.True(abs (e - a) < 1e-12, sprintf "%g against %g" e a)) expected actual

[<Fact>]
let ``error bars are layers under the block's own, a half-length either side`` () =
    let fig =
        plot rows {
            x U.second (fun r -> r.T)
            y U.metre (fun r -> r.X)
            xError U.second (fun r -> r.DT)
            yError U.metre (fun r -> r.DX)
        }

    let layers = layersOf fig
    Assert.Equal(3, layers.Length)
    Assert.True(match layers.[0].Geom with GeomErrorY _ -> true | _ -> false)
    Assert.True(match layers.[1].Geom with GeomErrorX _ -> true | _ -> false)
    Assert.Equal(GeomPoint, layers.[2].Geom)
    let low, high = ends layers.[0]
    close (rows |> List.map (fun r -> float (r.X - r.DX)) |> Array.ofList) low
    close (rows |> List.map (fun r -> float (r.X + r.DX)) |> Array.ofList) high
    let left, right = ends layers.[1]
    close (rows |> List.map (fun r -> float r.T - 0.2) |> Array.ofList) left
    close (rows |> List.map (fun r -> float r.T + 0.2) |> Array.ofList) right
    // Unlabelled bars stay out of the legend, and a name given afterwards is the series'.
    Assert.False layers.[0].ShowInLegend
    Assert.False layers.[1].ShowInLegend
    let named = layersOf (fig |> Chart.withName "run")
    Assert.Equal<string[]>([| ""; ""; "run" |], named |> Array.map (fun l -> l.Name))

[<Fact>]
let ``the domains hold the bars`` () =
    let fig =
        plot rows {
            x U.second (fun r -> r.T)
            y U.metre (fun r -> r.X)
            xError U.second (fun r -> r.DT)
            yError U.metre (fun r -> r.DX)
        }

    let xScale, yScale = Erase.seriesScales (layersOf fig).[0].Data
    let xLo, xHi = xScale.Domain
    let yLo, yHi = yScale.Domain
    Assert.True(xLo < -0.2 && xHi > 5.2, sprintf "x: %g to %g" xLo xHi)
    Assert.True(yLo < 0.9 && yHi > 3.7, sprintf "y: %g to %g" yLo yHi)

    // Without the bars the domain stops just past the points.
    let bare =
        plot rows {
            x U.second (fun r -> r.T)
            y U.metre (fun r -> r.X)
        }

    let _, bareY = Erase.seriesScales (layersOf bare).[0].Data
    Assert.True(snd bareY.Domain < yHi)

[<Fact>]
let ``an error in another unit of the dimension is read in the axis's`` () =
    let millimetre: U<m> = U.scaled "mm" 1e-3 U.metre

    let fig =
        plot rows {
            x U.second (fun r -> r.T)
            y U.metre (fun r -> r.X)
            yError millimetre (fun r -> r.DX * 1000.0)
        }

    let low, _ = ends (layersOf fig).[0]
    close (rows |> List.map (fun r -> float (r.X - r.DX)) |> Array.ofList) low

[<Fact>]
let ``an error of another dimension is refused, naming both`` () =
    let ex =
        Assert.ThrowsAny<exn>(fun () ->
            plot rows {
                x U.second (fun r -> r.T)
                y U.metre (fun r -> r.X)
                yError U.second (fun r -> r.DT)
            }
            |> ignore)

    Assert.Contains("yError", ex.Message)
    Assert.Contains("one dimension", ex.Message)

[<Fact>]
let ``a label puts the bars in the legend`` () =
    let fig =
        plot rows {
            x U.second (fun r -> r.T)
            y U.metre (fun r -> r.X)
            yError U.metre (fun r -> r.DX)
            errorLabel "standard deviation"
            color (fun r -> r.Group)
        }

    let layers = layersOf fig
    Assert.Equal("standard deviation", layers.[0].Name)
    Assert.True layers.[0].ShowInLegend
    Assert.Empty layers.[0].Channels
    Assert.NotEmpty layers.[1].Channels

    let texts =
        Chart.toScene (Chart.size 480.0 300.0) fig
        |> Scene.fold
            (fun acc n ->
                match n with
                | Text t -> t.Text :: acc
                | _ -> acc)
            []

    Assert.Contains("standard deviation", texts)
    Assert.Contains("even", texts)

[<Fact>]
let ``a facet splits the bars with the rows`` () =
    let fig =
        plot rows {
            x U.second (fun r -> r.T)
            y U.metre (fun r -> r.X)
            yError U.metre (fun r -> r.DX)
            facetWrap (fun r -> r.Group)
        }

    Assert.Equal(2, fig.Panels.Length)

    for p, group in Array.zip fig.Panels [| "even"; "odd" |] do
        let mine = rows |> List.filter (fun r -> r.Group = group) |> Array.ofList
        let low, _ = ends p.Layers.[0]
        close (mine |> Array.map (fun r -> float (r.X - r.DX))) low

[<Fact>]
let ``plain numbers take error bars without a witness`` () =
    let fig =
        plot rows {
            x (fun r -> float r.T)
            y (fun r -> float r.X)
            yError (fun r -> float r.DX)
            xError (fun r -> float r.DT)
        }

    Assert.Equal(3, (layersOf fig).Length)

[<Fact>]
let ``a distribution block refuses error bars`` () =
    let ex =
        Assert.ThrowsAny<exn>(fun () ->
            plot rows {
                x U.metre (fun r -> r.X)
                xError U.metre (fun r -> r.DX)
                histogram
            }
            |> ignore)

    Assert.Contains("xError", ex.Message)
