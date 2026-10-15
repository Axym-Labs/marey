# Marey.Plot

The chart API of [Marey](https://github.com/Axym-Labs/marey), a dimensionally-typed
visualization system for F#. This is the package most people install.

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
derived from the two unit witnesses. `x` is never annotated as a length — the compiler works that
out — and plotting metres against metres on these axes is a compile error.

## What is in it

- **`Chart`** — twelve geoms: `line`, `scatter`, `area`, `bar`, `errorbar`, `step`, `ribbon`,
  `tile`, `rect`, `image`, `segment` and `text`, with box plots and violins by category under
  `Chart.Category`. Two modes: under `open Marey.UnitsOfMeasure` each takes a witness per axis,
  checked at compile time, and most have an `add*` that layers onto the last panel; under `open
  Marey` alone, `Chart.Line(xs, ys, yUnit = U.metre)` takes plain arrays and units that may be
  left out, and `Chart.add` layers any figure onto another.
- **The `plot` encoding block** — for data that is a table rather than two arrays. Declares x, y,
  colour, shape and size in one place, with each dimensioned channel checked against its witness;
  without `Marey.UnitsOfMeasure`, a channel may also leave its witness out.
- **C#.** Extension methods on `Figure` and the `Category`, `Figures` and renderer-delegate doors,
  in the `Marey.CSharp` namespace.
- **Channels.** One colour or marker shape per category, with a legend that names the levels; or a
  colour or size driven by a *quantity*, which brings a colorbar labelled from its unit witness.
- **Statistics** — histogram, kernel density, ECDF, least-squares fit, and a five-number summary.
  The measures carry through: a fit of metres against seconds has a slope of type `float<m/s>`.
- **Facets.** `facetWrap` gives one panel per level of a key, over shared axes.
- **What a paper figure adds last.** Rules and spans across a panel, annotations with arrows,
  panel letters, ticks stated by step, value or name in the axis's own unit, minor ticks, tick
  formats, reversed axes, and legends outside the plot, in the emptiest corner, in columns, or
  shared by every panel. None of them moves the axes the data chose.
- **Themes.** Six, all measured against the colour validator below. `Theme.publication` is the box
  physics journals print, framed with ticks inside; `Theme.tikz` inherits the host LaTeX
  document's fonts.
- **Palettes** — Okabe–Ito, viridis, cividis, magma and an Oklab diverging ramp, with a
  colour-vision-deficiency and contrast validator that every shipped theme is checked against.
- **A measure–arrange constraint layout**, and the axis and legend compilers.
- **The `scene` storyboard block**, for a figure that moves:

  ```fsharp
  open Marey.Storyboard

  let intro =
      scene {
          let! curve = draw measured |> over 1.2<s>
          do! wait 0.5<s>
          do! parallel' [ curve |> morphTo fitted |> over 2.0<s>
                          curve |> zoomToX (0.0, 8.0) |> over 2.0<s> ]
          do! highlight curve.[42]
      }

  let anim = animate 32 (Chart.size 640.0 400.0) intro
  ```

  `let!` binds a name for something on stage; `animate` lays the whole shot out **once**, against
  the union of every state it sampled, so the panel holds still while the data moves. Seeking is
  evaluation, so `anim.At 2.7<s>` is a direct call and frames render in any order, in parallel.
- **The frame writer.** `Film.ofStory` pairs the scene animation with the layout it is drawn
  inside, and `Frames.write` renders every frame over `Array.Parallel.map` and numbers them onto
  disk as `frame-00000.png`. The concurrent and serial passes produce the same bytes in the same
  order, and one memoised `ITextMeasurer` serves the whole film — because font resolution walks a
  family stack against whatever is installed, and a measurer per thread is a chance for one frame
  of a video to be set in a different typeface from the frame before it.
- Per-layer styling that survives a theme change where it was set explicitly and is recoloured
  where it was not, which is what makes `withTheme` safe to apply last.

## No backend reference

`Marey.Plot` does not depend on a renderer. A backend is a function passed in at render time,
which is why the same figure goes to SVG or to LaTeX without the figure knowing either exists.
Install one:

- [`Marey.Backend.Svg`](https://www.nuget.org/packages/Marey.Backend.Svg) — pure F#, zero
  dependencies.
- [`Marey.Backend.Tikz`](https://www.nuget.org/packages/Marey.Backend.Tikz) — `tikzpicture`
  source for LaTeX, with `siunitx` units.

Depends on [`Marey.Core`](https://www.nuget.org/packages/Marey.Core),
[`Marey.Units`](https://www.nuget.org/packages/Marey.Units) and
[`Marey.Anim`](https://www.nuget.org/packages/Marey.Anim) — the last because the storyboard names
an `Anim` and a `Figure` in one signature. It is 36 KB and has no dependencies of its own.

- [The guide](https://github.com/Axym-Labs/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
