# Marey

A plotting library for F# where the compiler checks your units, and the axes label themselves.

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
derived from the two unit witnesses — and plotting seconds against seconds on these axes is a
compile error.

Swapping the theme turns the same figure into `tikzpicture` source, typeset by the LaTeX
document's own engine and fonts, with axis titles set by `siunitx`.

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

## The packages

| Package | |
|---|---|
| `Marey.Core` | Dimension vectors, unit witnesses, unit-indexed geometry, scales, and the scene IR. Zero dependencies. |
| `Marey.Units` | SI, derived and non-SI unit witnesses; canonicalisation, prefix selection, and four rendering styles. |
| `Marey.Plot` | `Chart` and the `plot` encoding block, twelve geoms, channels, statistics, facets, themes, colour-blind-safe palettes, layout. No backend reference. |
| `Marey.Backend.Svg` | SVG. Pure F#, zero dependencies, deterministic. |
| `Marey.Backend.Tikz` | `tikzpicture` source for LaTeX. |
| `Marey.Html` | HTML renderings for a notebook cell. Host-neutral. |
| `Marey.Verso` | The Verso notebook extension. |

Pre-1.0, and versioned accordingly. The API is still free to move. MIT licensed.

- [Repository](https://github.com/Jonathan-Schaefer-git/marey)
- [The guide](https://github.com/Jonathan-Schaefer-git/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Jonathan-Schaefer-git/marey/tree/main/examples)
