# Marey.Plot

The chart API of [Marey](https://github.com/Jonathan-Schaefer-git/marey), a dimensionally-typed
visualization system for F#. This is the package most people install.

```fsharp
#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Svg"

open Marey

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

- **`Chart`** — `line`, `scatter`, `area`, `bar` and `errorbar`, each with an `add*` that layers
  onto the last panel, plus `Chart.Plain` for data that genuinely has no units.
- **Themes.** Six, all measured against the colour validator below. `Theme.tikz` inherits the host
  LaTeX document's fonts.
- **Palettes** — Okabe–Ito, viridis, cividis, magma and an Oklab diverging ramp, with a
  colour-vision-deficiency and contrast validator that every shipped theme is checked against.
- **A measure–arrange constraint layout**, and the axis and legend compilers.
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

Depends on [`Marey.Core`](https://www.nuget.org/packages/Marey.Core) and
[`Marey.Units`](https://www.nuget.org/packages/Marey.Units).

- [The guide](https://github.com/Jonathan-Schaefer-git/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Jonathan-Schaefer-git/marey/tree/main/examples)
