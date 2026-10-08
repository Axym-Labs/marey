# Marey

A plotting library for F# where the compiler checks your units, and the axes label themselves.

```fsharp
#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Svg"

open Marey
open Marey.UnitsOfMeasure

let t = Array.init 24 (fun i -> float i * 0.4<s>)
let x = t |> Array.map (fun t -> 0.5 * 9.81<m/s^2> * t * t)

Chart.line U.second U.metre t x
|> Chart.withTitle "Free fall"
|> Chart.toSvg SvgBackend.render "fall.svg" (Chart.size 800.0 500.0)
```

That prints an x axis reading `t [s]` and a y axis reading `x [m]` with **nothing configured**.
The scale, the domain, the SI prefix, the tick values, the tick labels and both axis titles are
derived from the two unit witnesses — and plotting seconds against seconds on these axes is a
compile error.

Swapping the theme turns the same figure into `tikzpicture` source, typeset by the LaTeX
document's own engine and fonts, with axis titles set by `siunitx`. Swapping the *renderer* turns
it into a PNG or a vector PDF, through `Marey.Backend.Skia`, with text shaped by HarfBuzz rather
than estimated.

When the data is a table rather than two arrays, `plot` declares what each column means in one
place — positional channels, colour, shape and size, a statistic, and small multiples:

```fsharp
plot trials {
    x U.second (fun r -> r.Time)
    y U.metre  (fun r -> r.Displacement)
    color      (fun r -> r.Sample)
    layer GeomPoint
    fit
}
```

Units are opt-in. `open Marey.UnitsOfMeasure` is the mode above, with a witness per axis checked at
compile time; without it a chart takes plain arrays and a unit per axis that may be stated or left
out, `Chart.Line(ts, xs, yUnit = U.metre)`, which is also the mode C# has, under `using
Marey.CSharp`. A unit known only as text is read with `U.parse "V/m"`.

## The packages

| Package | |
|---|---|
| `Marey.Core` | Dimension vectors, unit witnesses, unit-indexed geometry, scales, and the scene IR. Zero dependencies. |
| `Marey.Units` | SI, derived and non-SI unit witnesses; canonicalisation, prefix selection, and four rendering styles. |
| `Marey.Plot` | `Chart` and the `plot` encoding block, thirteen geoms, channels, statistics (loess and bootstrap intervals among them), facets, themes, colour-blind-safe palettes, layout, the `scene` storyboard block, and the parallel frame writer. No backend reference. |
| `Marey.Backend.Svg` | SVG. Pure F#, zero dependencies, deterministic. |
| `Marey.Backend.Tikz` | `tikzpicture` source for LaTeX. |
| `Marey.Backend.Skia` | PNG and vector PDF, with real text shaping. The only package with third-party dependencies. |
| `Marey.Anim` | Animation as a pure function of time: a seekable `Anim<'a>`, twenty-eight easings, and arc-length path morphing. Depends on `Marey.Core` alone. |
| `Marey.Fields` | Fields on dimensioned planes, phase spaces included: derivatives in the units the types work out; heatmaps, contours, bands, quivers, streamlines and line-integral convolution; fixed points found and classified. |
| `Marey.Tools.Video` | Encoding a rendered frame sequence to video with `ffmpeg`, resolved on `PATH` and reported. |
| `Marey.Tools.Tex` | Mathematical typesetting: TeX source becomes vector outlines through `latex` and `dvisvgm`, so every backend draws it. Opt-in, and degrades to plain text. |
| `Marey.Toolchain` | Resolution and invocation of the external programs Marey drives but does not ship. Zero dependencies. |
| `Marey.Interact` | A figure with typed controls (a slider over kelvin hands the view kelvin), stepped by a pure function a notebook drives: zoom, pan, a readout in the axes' units, and recordings that replay as films. |
| `Marey.Html` | HTML renderings for a notebook cell, including a film as a playing flipbook and a live figure's page. Host-neutral. |
| `Marey.Verso` | The Verso notebook extension. A figure displays as a figure, a film plays, and a figure with controls is live. |

Pre-1.0, and versioned accordingly. The API is still free to move. MIT licensed.

- [Repository](https://github.com/Axym-Labs/marey)
- [The guide](https://github.com/Axym-Labs/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
