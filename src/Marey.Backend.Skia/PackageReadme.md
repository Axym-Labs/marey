# Marey.Backend.Skia

The PNG and PDF backend for [Marey](https://axym-labs.github.io/marey/). `netstandard2.1`.

```fsharp
#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Skia"

open Marey
open Marey.UnitsOfMeasure

let g = U.metrePerSecondSquared.Of 9.81<m/s^2>
let t = Q.init U.second 24 (fun i -> float i * 0.025<s>)

let figure = Chart.line t (0.5 * g * t * t)

// Bytes, or straight to a file. `toFileBytes` rather than `toFile`: writing text has an
// encoding and writing bytes does not, and a PNG that picked up a byte-order mark on the way
// to disk would not open.
let png = figure |> Chart.render SkiaBackend.renderPng (Chart.size 800.0 500.0)
figure |> Chart.toFileBytes SkiaBackend.renderPdf "fall.pdf" (Chart.size 800.0 500.0)
```

## The first backend that measures text

`Marey.Core` ships an *approximate* text measurer (a Helvetica advance-width table, with no
kerning and no ligatures), because a core with no dependencies cannot do better. This backend
shapes text with HarfBuzz and is the only one for which `Capabilities.TextShaping` is `true`.

As a result, **two backends of this library place an axis title at different coordinates for the
same figure**, which can look like a bug. Layout is a measure–arrange pass
against `ITextMeasurer`, so a real measurer moves the gutters. At 12 px, `AVATAR` is 48.01 px
estimated and 44.51 px shaped in Arial, and a real face reserves about a quarter more vertical
room than the estimate. Both reach the panel rectangle.

## Which font, and reproducibility

SVG names a family and lets the viewer resolve it. Skia has to open a real face, so this package
carries the two the built-in themes name: **Inter** (`Theme.Sans`, at weights 400, 500, 600 and
700) and **Liberation Serif** (`Theme.Serif`, upright, italic and bold). Both are under the SIL
Open Font License 1.1, whose texts are in the package's `fonts/`. A stack that names a carried
family resolves to it before anything installed, so a figure in a built-in theme is the same PNG on
every machine. CI renders one script on Linux and on Windows and compares the pixels. A PNG draws
its text as the glyphs' outlines, filled by Skia itself, because the platform's font engine
(FreeType, DirectWrite) would rasterise a glyph and smooth it differently on each platform. A PDF
keeps its text as text. Inter is subset to Latin, Greek, punctuation, super- and subscripts, letterlike symbols, arrows and the
mathematical operators, and a codepoint outside that falls back to the machine like any other.

A stack that names no carried family, or a slant the package does not carry, resolves against
what is installed, and the result is reported. `SkiaBackend.ResolvedFaces` names, for every run
in the scene, the entry in the stack that matched, the family that was opened and whether it was a
carried face. `Matched = None` means the whole stack missed and Skia's default was used. `SkiaOptions.FontFile` states a font file and skips resolution.

## Font fallback

A face holds the scripts it holds, and one face is not enough for a CJK axis title against a Latin
theme. The measurer splits a run by coverage, giving each codepoint the first face that has it as CSS
font matching does. It shapes each piece in its own face and sums the advances. `ITextMeasurer` is
unchanged. It still answers a width, and a run one face covers is one piece.

The fallback is reported too. `ResolvedFace.Fallbacks` names the faces that covered what the primary could not, with
the codepoints each covered; `ResolvedFace.Missing` lists the codepoints nothing covered, which are
drawn as `.notdef`. Both are empty for a Latin figure.

`SkiaOptions.FontFiles` states the whole chain and is the reproducible answer for a mixed-script
figure. `FontFile` states one face and **does not fall back**, so a codepoint that face lacks is drawn as
a box and reported. Setting both is an error.

## PDF

The PDF is vector, although this is the raster package. Paths stay paths and glyphs stay glyphs.
The page is the requested extent converted exactly (a 400 × 250 px figure is a 300 × 187.5 pt
page), and the document carries **no timestamp**, so two renders of one figure are byte-identical.
`SkiaOptions.Pdf` sets the title, author, subject and keywords.

## Native assets

This package depends on `SkiaSharp` and `SkiaSharp.HarfBuzz`, which bring native libraries, with
the Win32, macOS and Linux asset packages. On a slim
Linux container `libfontconfig1` and `libfreetype6` have to be installed as well. If a native
library is missing, the entry points raise a message naming the package to add rather than a
`DllNotFoundException` naming a file.

`Marey.Core`, `Marey.Units`, `Marey.Plot`, `Marey.Backend.Svg` and `Marey.Backend.Tikz` have no
dependency on this package or on SkiaSharp, and a test asserts it against the built assembly
graph.

- [The guide](https://axym-labs.github.io/marey/guide/01-start.html)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
