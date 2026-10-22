// One script's PNGs, for comparing across machines: `dotnet fsi tools/png/render.fsx OUT`.
// Built against the repository's Release output (`dotnet build src/Marey.Backend.Skia -c Release`
// and `src/Marey.Plot`), with the default and the publication theme, whose fonts are the faces
// Marey.Backend.Skia carries. CI renders this on Linux and on Windows and compares the pixels.

#r "nuget: SkiaSharp, 4.151.1"
#r "nuget: SkiaSharp.HarfBuzz, 4.151.1"
#r "nuget: SkiaSharp.NativeAssets.Linux, 4.151.1"
#r "nuget: HarfBuzzSharp.NativeAssets.Linux, 14.2.1.1"
#I "../../src/Marey.Plot/bin/Release/netstandard2.1"
#r "Marey.Core.dll"
#r "Marey.Units.dll"
#r "Marey.Anim.dll"
#r "Marey.Plot.dll"
#I "../../src/Marey.Backend.Skia/bin/Release/netstandard2.1"
#r "Marey.Backend.Skia.dll"

open System.IO
open Marey
open Marey.UnitsOfMeasure

let out = fsi.CommandLineArgs |> Array.tryItem 1 |> Option.defaultValue "png-out"
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
