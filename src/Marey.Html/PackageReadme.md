# Marey.Html

HTML renderings of [Marey](https://github.com/Axym-Labs/marey) values, for a notebook
cell. Host-neutral, **zero dependencies**, `netstandard2.1`.

This is also the convenient package to reference, because it sits at the top of the graph:
installing it brings `Marey.Core`, `Marey.Units`, `Marey.Plot`, `Marey.Interact` and the SVG
backend with it.

```fsharp
#r "nuget: Marey.Html"

open Marey
open Marey.UnitsOfMeasure

let figure = Chart.line U.second U.metre t x

// something -> string. No kernel, no display call, no global registry.
Html.figure figure
```

Twelve types render: `Figure` and `Node` as inline SVG, **`Film` as a flipbook that plays**,
`Color` as a swatch that does not lie about alpha, `Ramp` resampled through Oklab, `Color[]`,
`PaletteReport` as a colour-vision-deficiency and contrast report, `Dim`, `U<'u>` and
`IScaleErased` as themselves, and a live figure (`Live`, `Recorder`) as its figure.

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

In [Verso](https://github.com/DataficationSDK/Verso), install
[`Marey.Verso`](https://www.nuget.org/packages/Marey.Verso) into the notebook host and a cell whose
value is a `Figure` shows the figure. Referencing this package and installing that extension are
separate steps that cannot substitute for each other: this one provides the types, the extension
provides the rendering.

Without any extension, render to SVG and let the host's built-in SVG formatter draw it — that path
needs nothing installed:

```fsharp
figure |> Chart.render SvgBackend.render (Chart.size 720.0 450.0)
```

- [The guide](https://github.com/Axym-Labs/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
