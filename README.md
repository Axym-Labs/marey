# Marey

A plotting library for F# where the compiler checks your units, and the axes label themselves.

> Named for **Étienne-Jules Marey** (1830–1904), who invented the graphical method in experimental
> science: record a physical quantity as a curve against time, and read the physics off the curve.

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

<img src="docs/images/free-fall.svg" alt="Free fall: displacement against elapsed time" width="640">

Nothing above configures an axis. The scale, the domain, the SI prefix, the tick values, the tick
labels and both axis titles are derived from the two unit witnesses. `x` is never annotated as a
length — `0.5 * 9.81<m/s^2> * t * t` has type `float<m>` and the compiler works that out — and
plotting seconds against seconds on these axes is a compile error.

## Install

```
dotnet add package Marey.Plot
dotnet add package Marey.Backend.Svg
```

`Marey.Html` sits at the top of the graph, so referencing it alone brings the rest.

## Units, end to end

Give it data spanning 1e-9 to 5e-9 metres and the axis reads `nm` with ticks `1, 2, 3, 4, 5`.
Give it kilopascals and it says so:

<img src="docs/images/wave-packet.svg" alt="A Gaussian wave packet in millimetres and kilopascals" width="640">

The unit registry is keyed on the seven-element SI dimension vector rather than on strings, so
anything that composes to a newton prints `N`. Units with no SI name work too, as long as you say
what they are made of: `U.div Derived.volt U.metre` renders `V·m⁻¹`, and `MV·m⁻¹` when prefixed.
Nothing is inferred — a dimension handed over bare is never factorised for you, because the same
vector is `J/K` and `N·m/K`, and a confident guess is worse than a verbose truth.

Affine units are handled honestly: degrees Celsius carries an offset, an electronvolt axis reads
`keV` rather than `kJ`, and a mass rescaled by 10⁻⁶ prints `mg` against the gram rather than
`µkg`.

## Figures that belong in a LaTeX document

Swap the theme and the same figure becomes `tikzpicture` source. It is typeset by the same engine
as the rest of your document, so it **inherits your fonts**, and the axis titles are set by
`siunitx` from the unit witnesses:

```fsharp
figure
|> Chart.withTheme Theme.tikz
|> Chart.toFile TikzBackend.render "fall.tex" (Chart.size 320.0 200.0)
```

```tex
\node[anchor=north,font=\fontsize{9}{10.8}\selectfont] at (133.59bp,15.825bp) {t [\qty{}{\second}]};
\node[anchor=base,rotate=90,font=\fontsize{9}{10.8}\selectfont] at (15.825bp,69.075bp) {x [\qty{}{\metre}]};
```

## The usual scientific figure

A model curve, measured points, error bars, and a legend that names only the layers you named:

<img src="docs/images/oscillator.svg" alt="A damped oscillator: model against measurement with error bars" width="640">

## On real data, and where the units story stops

Units are transformative for physical measurements and irrelevant for a mole fraction in ppm,
which is dimensionless. Marey renders a dimensionless axis with no unit brackets rather than
printing `[1]`, refuses to rescale values it has no unit to hang a prefix on, and lets you write
the label yourself:

<img src="docs/images/co2-record.svg" alt="Atmospheric CO2 at Mauna Loa, annual means with uncertainties" width="640">

The [`examples/`](examples/) directory has five worked figures, two of them on real committed
datasets. They are executed by the test suite on every run, so they cannot drift from the library.

## What is in the box

| Package | |
|---|---|
| `Marey.Core` | Dimension vectors, unit witnesses, unit-indexed geometry, scales, and the nine-case scene IR. Zero dependencies. |
| `Marey.Units` | SI, 22 derived and 18 non-SI unit witnesses; canonicalisation, prefix selection, and Unicode/ASCII/LaTeX/siunitx rendering. |
| `Marey.Plot` | `Chart`, five geoms, six themes, colour-blind-safe palettes with a validator, constraint layout, and the axis and legend compilers. No backend reference. |
| `Marey.Backend.Svg` | SVG. Pure F#, zero dependencies, byte-identical across renders. |
| `Marey.Backend.Tikz` | `tikzpicture` source for LaTeX, with `siunitx` units and inherited fonts. |
| `Marey.Html` | HTML renderings of nine Marey types for a notebook cell. Host-neutral. |
| `Marey.Verso` | The [Verso](https://github.com/DataficationSDK/Verso) notebook extension, so a bare `Figure` displays as a figure. |

## Notebooks

In [Verso](https://github.com/DataficationSDK/Verso) with the `Marey.Verso` extension installed, a
cell whose value is a `Figure` shows the figure — no `.Show()`, no save-then-open, no display call.
Without the extension, render to SVG and the built-in formatter draws it, with nothing installed.

## Building

```
dotnet build          # warning-free; warnings are errors
dotnet test           # 506 tests
```

The suite is FsCheck property suites for the dimension group laws, all seven scale kinds, ticks,
interpolation, colour and the scene IR; 29 golden baselines in SVG *and* `tikzpicture` source; a
real `pdflatex` compile gate; a rasterised ink gate that checks the picture is not blank; and
executable checks over every example in [the guide](docs/guide.md) and in [`examples/`](examples/).

## Documentation

- **[The guide](docs/guide.md)** — everything the library does today, with a runnable example for
  each part. Every example in it is extracted and executed by the test suite.
- **[Examples](examples/)** — five standalone scripts, also executed by the test suite.
- **[Why Marey is like this](docs/why.md)** — the case for a unit witness over a string label,
  what the other bets buy, what they cost, and when not to use the library.

## Status

Pre-1.0, and versioned accordingly. The API is still free to move.

MIT licensed.
