# Marey.Html

HTML renderings of [Marey](https://axym-labs.github.io/marey/) values, for a notebook
cell. Host-neutral, **zero dependencies**, `netstandard2.1`.

[`Marey`](https://www.nuget.org/packages/Marey) brings it with the rest of the library, and
[`Marey.Verso`](https://www.nuget.org/packages/Marey.Verso) is what shows it in a Verso notebook.

```fsharp
#r "nuget: Marey.Html"

open Marey
open Marey.UnitsOfMeasure

let g = U.metrePerSecondSquared.Of 9.81<m/s^2>
let t = Q.init U.second 24 (fun i -> float i * 0.025<s>)

let figure = Chart.line t (0.5 * g * t * t)

// something -> string. No kernel, no display call, no global registry.
Html.figure figure
```

Twelve types render: `Figure` and `Node` as inline SVG, **`Film` as a flipbook that plays**,
`Color` as a swatch that does not lie about alpha, `Ramp` resampled through Oklab, `Color[]`,
`PaletteReport` as a colour-vision-deficiency and contrast report, `Dim`, `U<'u>` and
`IScaleErased` as themselves, and a live figure (`Live`, `Recorder`) as its figure.

**A large figure is drawn on a canvas.** From `Html.CanvasThreshold` drawn elements on (5000), a
figure or a live figure is a page holding a `<canvas>`, the scene's bytes (`Wire`, in
`Marey.Core`) and a script compiled from Marey's own canvas backend, which this package embeds.
A 10⁵-point scatter is 2.1 MB rather than 6.4 MB of SVG and draws in tens of milliseconds; a live
step that moves one mark sends a few kilobytes. Under the canvas is the figure's SVG without its
large batches, for a viewer that runs no script. `Html.renderMode` sets `Svg`, `Canvas` or `Auto`.

A film is sampled into stills stacked in one SVG and cycled with SMIL `<animate>` — no `<style>`
block, which a notebook cell cannot scope, and no external tool, so it plays wherever the notebook
renders and looks the same for every reader.

## A live figure's page

`Html.widget live state isLive` writes a whole page for a host that draws output in a frame of its
own: the controls at the state's values, the figure, and a script that posts the reader's changes,
drags and pointer position, and swaps in what comes back. **The script formats no number**: every
label and every reading is written in F#, by the kernel. With `isLive = false` the same page has
its controls disabled, for a host with nothing to talk back to and for a saved notebook.

## Host-neutral

Every renderer here is `something -> string`: no formatter registration, no kernel reference and no
dependency on a notebook host. The output carries no `<style>` block and no pinned colours, so it
inherits the notebook's theme rather than fighting it.

## To make a bare value display

In a [Verso](https://github.com/DataficationSDK/Verso) notebook, a first cell of
`#!extension Marey.Verso` loads [`Marey.Verso`](https://www.nuget.org/packages/Marey.Verso), and
from then on a cell whose value is a `Figure` shows the figure. This package provides the
renderings; the extension hands them to the host.

Without any extension, render to SVG and let the host's built-in SVG formatter draw it, with
nothing installed:

```fsharp
figure |> Chart.render SvgBackend.render (Chart.size 720.0 450.0)
```

- [The guide](https://axym-labs.github.io/marey/guide/11-notebooks.html)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
