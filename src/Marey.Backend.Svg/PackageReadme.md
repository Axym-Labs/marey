# Marey.Backend.Svg

The SVG backend for [Marey](https://github.com/Jonathan-Schaefer-git/marey), a dimensionally-typed
visualization system for F#. Pure F#, **zero dependencies**, `netstandard2.1`.

```fsharp
#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Svg"

open Marey

let figure = Chart.line U.second U.metre t x

// To a string, or straight to a file.
let svg = figure |> Chart.render SvgBackend.render (Chart.size 800.0 500.0)
figure |> Chart.toSvg SvgBackend.render "out.svg" (Chart.size 800.0 500.0)
```

## Deterministic

Two renders of one figure are **byte-identical**. Numbers are formatted culture-invariantly with a
fixed precision, and nothing depends on hash ordering or on the clock. That is what makes a golden
-image test suite meaningful — the repository pins 29 baselines and diffs them as text in review,
rather than eyeballing bitmaps.

## Complete

All nine cases of the scene IR: paths, polylines, batched markers, text, rectangles, gradients,
clipping groups, images and transforms. Accessibility semantics are emitted alongside the geometry.

Because the output is a string starting with `<svg`, it renders in any notebook or viewer that
knows what to do with one, without an extension installed.

Depends on [`Marey.Core`](https://www.nuget.org/packages/Marey.Core) and nothing else — it is a
string producer, and a renderer that needed a package to write text would be admitting it is
something else. It also doubles as the reference implementation and the test oracle the other
backends are checked against.

- [The guide](https://github.com/Jonathan-Schaefer-git/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Jonathan-Schaefer-git/marey/tree/main/examples)
