# Marey.Plot

The plotting grammar of [Marey](https://github.com/Axym-Labs/marey): figures whose axes take their
titles, ticks and prefixes from the units of the data. Most projects reference
[`Marey`](https://www.nuget.org/packages/Marey), which brings this package with the rest of the
library; this one alone is the charts, without fields, simulations or live figures.

```fsharp
#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Svg"

open Marey
open Marey.UnitsOfMeasure

let g = U.metrePerSecondSquared.Of 9.81<m/s^2>
let t = Q.init U.second 61 (fun i -> float i * 0.01<s>)
let drop = 0.5 * g * t * t

Chart.line t drop
|> Chart.withName "½ g t²"
|> Chart.addScatterIn U.second U.metre [| 0.1<s>; 0.3<s>; 0.5<s> |] [| 0.051<m>; 0.447<m>; 1.232<m> |]
|> Chart.withName "light gate"
|> Chart.withTitle "A dropped ball"
|> Chart.withLegend LegendBest
|> Chart.toSvg SvgBackend.render "fall.svg" (Chart.size 720.0 450.0)
```

The axes read `t [ms]` and `x [m]`, and nothing set them. `t` carries its unit, `drop` was worked
out from `g` and `t`, so its axis knows it is a length, and the measured points state theirs with
`addScatterIn`. A pressure added to the metre axis would not compile.

## What it draws

- **Marks.** Lines, scatters, areas, bars, error bars, steps, ribbons, tiles, rectangles, images,
  segments and text, each layered onto the last panel with an `add` form; box plots, violins and
  counts over categories under `Chart.Category`.
- **Statistics with their units.** Histograms, kernel densities, ECDFs, fits and loess curves. A
  fit of metres against seconds has a slope of type `float<m/s>`.
- **Channels.** A colour or marker shape per category, with a legend naming the levels; a colour or
  size driven by a quantity, with a colorbar titled from its unit.
- **The `plot` block**, for a table rather than two series: x, y, colour, shape, size, facets and
  layers declared in one place, each channel checked against its witness.
- **Scales.** Log, symlog, power and reciprocal axes, set on the axis, so a layer added later is
  drawn on them; stated domains, ticks by step, value or name, minor ticks, tick formats, reversed
  axes.
- **Units on one axis.** A layer in minutes on a seconds axis is drawn in seconds; a frequency and an
  angular velocity are refused, naming both.

## A figure for a paper

`beside`, `above` and `mosaic` place panels on one grid whose plots line up, with stated row and
column proportions; `shareX` and `shareY` share an axis across any composition and label it once.
A second y axis, a secondary axis in another unit or through a map (photon energy over
wavelength), equal aspect, insets with their zoom marked on the host, rules and spans, annotations
with arrows, panel letters, and legends in the emptiest corner, outside the plot, in columns or
shared by every panel. None of them moves the axes the data chose.

Six themes, among them `Theme.publication`, the framed box with ticks inside that physics journals
print, and `Theme.tikz`, which takes the LaTeX document's fonts. The palettes are Okabe–Ito,
viridis, cividis, magma and an Oklab diverging ramp, and every theme is checked for contrast and
under three colour-vision deficiencies.

## A figure that moves

The `scene` block (`open Marey.Storyboard`) draws, morphs, zooms and highlights the layers of a
figure over time, and lays the whole shot out once, against every state it reaches, so the panel
holds still while the data moves. `Film.ofStory` pairs it with that layout and `Frames.write`
renders the frames in parallel to numbered PNGs, all set in one typeface.

## Output

A renderer is a function passed in when a figure is drawn, so this package references none and the
same figure goes to any of them:
[`Marey.Backend.Svg`](https://www.nuget.org/packages/Marey.Backend.Svg) (pure F#, no
dependencies), [`Marey.Backend.Tikz`](https://www.nuget.org/packages/Marey.Backend.Tikz) (a
`tikzpicture` for LaTeX, with `siunitx` units) and
[`Marey.Backend.Skia`](https://www.nuget.org/packages/Marey.Backend.Skia) (PNG and PDF). From C#,
`using Marey.CSharp` gives extension methods on `Figure`, and every chart takes its units as
optional named arguments.

Depends on [`Marey.Core`](https://www.nuget.org/packages/Marey.Core),
[`Marey.Units`](https://www.nuget.org/packages/Marey.Units) and
[`Marey.Anim`](https://www.nuget.org/packages/Marey.Anim).

- [The guide](https://github.com/Axym-Labs/marey/blob/main/docs/guide/README.md)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
