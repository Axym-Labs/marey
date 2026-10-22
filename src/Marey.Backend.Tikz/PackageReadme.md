# Marey.Backend.Tikz

The TikZ backend for [Marey](https://axym-labs.github.io/marey/). Emits `tikzpicture` source for
pasting into a LaTeX document. Pure F#, **zero dependencies**, `netstandard2.1`.

```fsharp
#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Tikz"

open Marey
open Marey.UnitsOfMeasure

let t = Array.init 24 (fun i -> float i * 0.025<s>)
let x = t |> Array.map (fun t -> 0.5 * 9.81<m/s^2> * t * t)

Chart.lineIn U.second U.metre t x
|> Chart.withTheme Theme.tikz
|> Chart.toFile TikzBackend.render "fall.tex" (Chart.size 320.0 200.0)
```

```tex
\node[anchor=north,font=\fontsize{9}{10.8}\selectfont] at (133.59bp,15.825bp) {t [\qty{}{\second}]};
\node[anchor=base,rotate=90,font=\fontsize{9}{10.8}\selectfont] at (15.825bp,69.075bp) {x [\qty{}{\metre}]};
```

## Why TikZ rather than an image

An exported image is a foreign object in a paper: different fonts, different weights, and units set
in whatever the plotting library took a minus sign to be. A `tikzpicture` is typeset by the same
engine as the rest of the document, so **the figure inherits the document's fonts**, and the axis
titles are typeset by `siunitx` from the unit witnesses — `\qty{}{\metre}`, not the string `"m"`.

`TikzBackend.standalone` wraps a fragment in a document that compiles on its own.

## What it needs, and what it will not do

Only `\usepackage{tikz}`, plus `siunitx` when the siunitx unit style is used. Coordinates are emitted
in `bp` at 96 px/inch, so a 400×250 figure produces a page of exactly 300×187.5 pt.

Unsupported constructs degrade visibly rather than silently: a gradient becomes its Oklab
midpoint, and a raster becomes a comment naming what was skipped and at what size. Layout is
measured against the theme's font metrics while TeX sets the text in the document's own face;
`Theme.withFontFamily` is what reconciles the two.

Depends on [`Marey.Core`](https://www.nuget.org/packages/Marey.Core) and nothing else.

- [The guide](https://axym-labs.github.io/marey/guide/06-paper.html)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
