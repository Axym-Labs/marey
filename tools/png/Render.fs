/// The default and the publication theme's PNGs of one figure, whose fonts are the faces
/// Marey.Backend.Skia carries. CI renders this on Linux and on Windows and compares the pixels
/// (tools/png/compare.fsx).
module Marey.Tools.Png.Render

open System.IO
open Marey
open Marey.UnitsOfMeasure

[<EntryPoint>]
let main argv =
    let out = argv |> Array.tryHead |> Option.defaultValue "png-out"
    Directory.CreateDirectory out |> ignore

    let g = U.metrePerSecondSquared.Of 9.81<m/s^2>
    let t = Q.init U.second 61 (fun i -> float i * 0.01<s>)
    let drop = 0.5 * g * t * t

    let figure =
        Chart.line t drop
        |> Chart.withName "½ g t²"
        |> Chart.addScatter (U.second.Of [| 0.1<s>; 0.3<s>; 0.5<s> |]) (U.metre.Of [| 0.051<m>; 0.447<m>; 1.232<m> |])
        |> Chart.withName "light gate, µ ± σ"
        |> Chart.withTitle "A dropped ball: Δx in metres, θ ≈ 0°"
        |> Chart.withLegend LegendBest

    for name, theme in [ "modern", Theme.modern; "publication", Theme.publication ] do
        let png = figure |> Chart.withTheme theme |> Chart.render SkiaBackend.renderPng (Chart.size 800.0 500.0)
        File.WriteAllBytes(Path.Combine(out, name + ".png"), png)
        printfn "%s.png: %d bytes" name png.Length

    0
