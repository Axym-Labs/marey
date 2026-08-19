# Marey.Backend.Tikz

The TikZ backend for [Marey](https://github.com/Jonathan-Schaefer-git/marey), a dimensionally-typed
visualization system for F#. Emits `tikzpicture` source you paste into a LaTeX document. Pure F#,
**zero dependencies**, `netstandard2.1`.

```fsharp
#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Tikz"

open Marey

Chart.line U.second U.metre t x
|> Chart.withTheme Theme.tikz
|> Chart.toFile TikzBackend.render "fall.tex" (Chart.size 320.0 200.0)
```

```tex
\node[anchor=north,font=\fontsize{9}{10.8}\selectfont] at (133.59bp,15.825bp) {t [\qty{}{\second}]};
\node[anchor=base,rotate=90,font=\fontsize{9}{10.8}\selectfont] at (15.825bp,69.075bp) {x [\qty{}{\metre}]};
```

## Why this exists

A figure exported as an image is a foreign object in a paper: different fonts, different weights,
units set in whatever the plotting library thought a minus sign was. A `tikzpicture` is typeset by
the same engine as the rest of the document, so **it inherits your fonts**, and the axis titles are
typeset by `siunitx` from the unit witnesses — `\qty{}{\metre}`, not the string `"m"`.

`TikzBackend.standalone` wraps a fragment in a document that compiles on its own, which is what the
test suite gates against a real `pdflatex` run.

## What it needs, and what it will not do

Only `\usepackage{tikz}`, plus `siunitx` if you use the siunitx unit style. Coordinates are emitted
in `bp` at 96 px/inch, so a 400×250 figure produces a page of exactly 300×187.5 pt.

It degrades visibly rather than lying: a gradient becomes its Oklab midpoint and a raster becomes a
comment naming what was skipped and at what size. Layout is measured against the theme's font
metrics, and TeX will use the document's own face — `Theme.withFontFamily` is how you tell it which.

Depends on [`Marey.Core`](https://www.nuget.org/packages/Marey.Core) and nothing else.

- [The guide](https://github.com/Jonathan-Schaefer-git/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Jonathan-Schaefer-git/marey/tree/main/examples)
