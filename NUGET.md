# Marey

Figures, fields, simulations and animations for F#, where the compiler checks the units and the
axes label themselves. `Marey` is the library in one reference: units, the plotting grammar,
fields, simulations, animation, live figures, and SVG, TikZ and HTML output.

```fsharp
#r "nuget: Marey"

open Marey
open Marey.UnitsOfMeasure

let t = Array.init 400 (fun i -> float i * 0.01<s>)
let x = t |> Array.map (fun t -> 0.012<m> * exp (-t / 1.5<s>) * cos (2.0 * System.Math.PI * 1.2<Hz> * t))
let stiffness = 40.0<N/m>

let spring =
    Chart.line U.second U.metre t x
    |> Chart.above (Chart.line U.second U.joule t (x |> Array.map (fun x -> 0.5 * stiffness * x * x)))
    |> Chart.shareX
    |> Chart.withTitle "A damped spring"

spring |> Chart.toSvg SvgBackend.render "spring.svg" (Chart.size 720.0 480.0)
```

Nothing set an axis. The displacement was computed in metres and reads `x [mm]`; the energy,
`½ k x²` with `k` in N/m, is a `float<J>` because the compiler worked it out, and reads in mJ.
Plotting seconds against a metres axis does not compile. Under `Theme.tikz` the same figure is a
`tikzpicture`, typeset by the LaTeX document with `siunitx` units.

In a [Verso](https://github.com/DataficationSDK/Verso) notebook, a first cell of
`#!extension Marey.Verso` makes a figure display as a figure and a figure with controls live.

## The packages

`Marey` depends on every package below that needs no native library or external program. The
others are separate references.

| Package | |
|---|---|
| `Marey.Core`, `Marey.Units` | Dimensions, unit witnesses, scales, colour, and the scene every backend draws. |
| `Marey.Plot` | `Chart`, the `plot` block, layout, themes, the storyboard. |
| `Marey.Anim`, `Marey.Fields`, `Marey.Sim` | Animation, fields, simulation. |
| `Marey.Interact`, `Marey.Html` | Live figures, and HTML for a notebook cell. |
| `Marey.Backend.Svg`, `Marey.Backend.Tikz` | SVG, and `tikzpicture` source for LaTeX. |
| `Marey.Backend.Skia` | PNG and vector PDF with text shaped by HarfBuzz. Not in `Marey`: it brings SkiaSharp's natives. |
| `Marey.Backend.Gpu` | The GPU, offscreen or in a window. Not in `Marey`. |
| `Marey.Backend.Web` | A canvas in a browser, for a Fable app. Not in `Marey`. |
| `Marey.Tools.Tex`, `Marey.Tools.Video` | Typeset text through `latex` and `dvisvgm`, video through `ffmpeg`. Not in `Marey`. |
| `Marey.Verso` | The Verso notebook extension, loaded by `#!extension Marey.Verso`. |

Pre-1.0: the next release is 1.0.0, and the compatibility page says what it will promise. MIT
licensed.

- [The guide](https://github.com/Axym-Labs/marey/blob/main/docs/guide/README.md)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
- [Compatibility](https://github.com/Axym-Labs/marey/blob/main/docs/compatibility.md)
- [Repository](https://github.com/Axym-Labs/marey)
