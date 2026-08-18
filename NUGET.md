# Marey

A dimensionally-typed visualization system for F# — plotting where the compiler checks your units.

```fsharp
open Marey

let t = Array.init 24 (fun i -> float i * 0.4<s>)
let x = t |> Array.map (fun t -> 0.5 * 9.81<m/s^2> * t * t)

Chart.line U.second U.metre t x
|> Chart.withTitle "Free fall"
|> Chart.toSvg SvgBackend.render "fall.svg" (Chart.size 800.0 500.0)
```

That prints an x axis reading `t [s]` and a y axis reading `x [m]` with **nothing configured**.
The scale, the domain, the SI prefix, the tick values, the tick labels and both axis titles are
all derived from the two unit witnesses. The same call on data spanning 1e-9…5e-9 m prints
`x [nm]` with ticks `1, 2, 3, 4, 5`, and plotting metres against seconds is a compile error.

Swap one value and the same figure becomes TeX whose axis titles are typeset by `siunitx`, in the
host document's own fonts:

```fsharp
figure
|> Chart.withTheme Theme.tikz
|> Chart.toFile TikzBackend.render "fall.tex" (Chart.size 320.0 200.0)
```

## The packages

| Package | What it is |
|---|---|
| `Marey.Core` | Dimensions, unit witnesses, scales, geometry, and the closed nine-case `Scene` IR. Zero dependencies. |
| `Marey.Units` | SI + 22 derived + 18 non-SI witnesses, `Dim`-keyed canonicalisation, prefix selection, Unicode/ASCII/LaTeX/siunitx rendering. |
| `Marey.Plot` | `Chart`, five geoms, six themes, CVD-validated palettes, constraint layout. No backend reference. |
| `Marey.Backend.Svg` | SVG. Pure F#, deterministic, byte-identical output. |
| `Marey.Backend.Tikz` | `tikzpicture` source for LaTeX, inheriting the document's fonts. |
| `Marey.Html` | HTML renderings of nine Marey types for a notebook cell. Host-neutral. |
| `Marey.Verso` | The [Verso](https://github.com/DataficationSDK/Verso) notebook extension, so a `Figure` displays as a figure. |

Referencing `Marey.Html` brings Plot, Units and Core transitively, which is the usual starting
point.

## Documentation

- [**The guide**](https://github.com/Jonathan-Schaefer-git/marey/blob/main/docs/guide.md) —
  everything the library does today, with a runnable example for each part. Every example is
  extracted and executed by the test suite.
- [Design document](https://github.com/Jonathan-Schaefer-git/marey/blob/main/docs/design.md)
- [Decision log](https://github.com/Jonathan-Schaefer-git/marey/blob/main/docs/decisions.md)
- [Notebooks](https://github.com/Jonathan-Schaefer-git/marey/blob/main/docs/notebooks/README.md)

Requires the .NET 10 SDK to build; the libraries target `netstandard2.1`.

MIT licensed. Named for Étienne-Jules Marey, who invented the graphical method in experimental
science, built the instruments that inscribe a measurement as a trace, and then invented
chronophotography.
