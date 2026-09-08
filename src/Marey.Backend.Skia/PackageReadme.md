# Marey.Backend.Skia

The PNG and PDF backend for [Marey](https://github.com/Jonathan-Schaefer-git/marey), a
dimensionally-typed visualization system for F#. `netstandard2.1`.

```fsharp
#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Skia"

open Marey

let figure = Chart.line U.second U.metre t x

// Bytes, or straight to a file. `toFileBytes` rather than `toFile`: writing text has an
// encoding and writing bytes does not, and a PNG that picked up a byte-order mark on the way
// to disk would not open.
let png = figure |> Chart.render SkiaBackend.renderPng (Chart.size 800.0 500.0)
figure |> Chart.toFileBytes SkiaBackend.renderPdf "fall.pdf" (Chart.size 800.0 500.0)
```

## The first backend that measures text

`Marey.Core` ships an *approximate* text measurer — a Helvetica advance-width table, no kerning,
no ligatures — because a zero-dependency core cannot do better. This backend shapes with HarfBuzz
and is the only one for which `Capabilities.TextShaping` is `true`.

The consequence is worth knowing before it looks like a bug: **two backends of this library place
an axis title at different coordinates for the same figure.** Layout is a measure–arrange pass
against `ITextMeasurer`, so a real measurer moves the gutters. At 12 px, `AVATAR` is 48.01 px
estimated and 44.51 px shaped in Arial, and a real face reserves about a quarter more vertical
room than the estimate. Both reach the panel rectangle.

## Which font, and reproducibility

SVG names a family and lets the viewer resolve it. Skia has to pick a real face from what is
installed, so the same figure is a slightly different PNG on two machines. That is reported
rather than hidden — `SkiaBackend.ResolvedFaces` names, for every run in the scene, the entry in
the theme's stack that matched and the family that was opened, with `Matched = None` meaning the
whole stack missed and Skia's default was used.

For output that must be identical everywhere, `SkiaOptions.FontFile` states a font file and
resolution is skipped.

## Font fallback

A face holds the scripts it holds, and one face is not enough for a CJK axis title against a Latin
theme. The measurer splits a run by coverage — per codepoint, the first face that has it, CSS font
matching's rule — shapes each piece in its own face and sums the advances. `ITextMeasurer` is
unchanged: it still answers a width, and a run one face covers is one piece.

It reports. `ResolvedFace.Fallbacks` names the faces that covered what the primary could not, with
the codepoints each covered; `ResolvedFace.Missing` lists the codepoints nothing covered, which are
drawn as `.notdef`. Both are empty for a Latin figure.

`SkiaOptions.FontFiles` states the whole chain and is the reproducible answer for a mixed-script
figure. `FontFile` states one face and **does not fall back** — one face is the whole promise, and
a codepoint it lacks is a reported box. Setting both is an error.

## PDF

Vector, despite the package being the raster one: paths stay paths and glyphs stay glyphs. The
page is the requested extent converted exactly — a 400 × 250 px figure is a 300 × 187.5 pt page —
and the document carries **no timestamp**, so two renders of one figure are byte-identical.
`SkiaOptions.Pdf` sets the title, author, subject and keywords.

## Native assets

This is the one Marey package with third-party dependencies, and they include native libraries:
`SkiaSharp` and `SkiaSharp.HarfBuzz`, with the Win32, macOS and Linux asset packages. On a slim
Linux container `libfontconfig1` and `libfreetype6` have to be installed as well. If a native
library is missing, the entry points raise a message naming the package to add rather than a
`DllNotFoundException` naming a file.

`Marey.Core`, `Marey.Units`, `Marey.Plot`, `Marey.Backend.Svg` and `Marey.Backend.Tikz` have no
dependency on this package or on SkiaSharp, and a test asserts it against the built assembly
graph.

- [The guide](https://github.com/Jonathan-Schaefer-git/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Jonathan-Schaefer-git/marey/tree/main/examples)
