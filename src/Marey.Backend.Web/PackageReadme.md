# Marey.Backend.Web

The browser backend for [Marey](https://github.com/Axym-Labs/marey), a dimensionally-typed
visualization system for F#. A [Fable](https://fable.io) app builds a figure, lays it out with the
same compiler a notebook kernel runs, and draws it on a canvas. Consumed from source by Fable;
`Fable.Core` is its one dependency.

```fsharp
open Marey
open Marey.UnitsOfMeasure

let ctx = Canvas.context (Canvas.byId "plot")
let extent = Chart.size 720.0 450.0

WebText.measurer ()
|> fun ready ->
    ready.``then`` (fun measurer ->
        let scene = Chart.line U.second U.metre t x |> Chart.toSceneWith measurer extent
        CanvasBackend.draw measurer ctx extent scene)
```

## Text

Layout measures text, and in a page the font it should measure may still be downloading.
`WebText.measurer ()` resolves to the canvas's own measurer once `document.fonts` has finished
loading. `WebText.measurerNow ()` answers at once: the canvas's measurer if the fonts are in, and
otherwise Marey's approximate widths with `Approximated` saying so. The canvas measurer itself
refuses to measure while fonts are loading, so a figure cannot be laid out against a fallback face
by accident.

Pass the backend the measurer the figure was laid out with: text is anchored with its advances.

## What it draws

All ten node cases, by the rules the Skia backend draws them by: marker batches with one paint
state and each mark filled then stroked, group opacity on a scratch canvas composited once,
rasters nearest or smoothed as the node asks, at the display's pixel ratio. `Semantics` is false,
because a canvas has no element per shape; give the canvas element an `aria-label`.

A `CanvasRenderingContext2D` from `Fable.Browser.Dom` is the same object as this package's
`CanvasContext`; pass it with `unbox`.
