# Marey.Verso

The [Verso](https://github.com/DataficationSDK/Verso) notebook extension for
[Marey](https://github.com/Axym-Labs/marey): with it, a cell whose value is a figure displays the
figure, a film plays, and a figure with controls is live.

## Setting up a notebook

The extension is loaded into the notebook host, and Marey is referenced from the kernel. A first
cell does both:

```
#!extension Marey.Verso
#r "nuget: Marey"
```

`#!extension` fetches the package from nuget.org and asks once before loading its code, since an
extension runs inside the host. Verso's Extensions panel installs the same package. After that, a
cell's value is drawn:

```fsharp
#r "nuget: Marey"

open Marey
open Marey.UnitsOfMeasure

let t = Array.init 24 (fun i -> float i * 0.025<s>)
let x = t |> Array.map (fun t -> 0.5 * 9.81<m/s^2> * t * t)

Chart.line U.second U.metre t x
|> Chart.withTitle "Free fall"
```

A `Figure`, a `Film`, a scene `Node`, a `Color`, a `Ramp` and a palette, a `PaletteReport`, a
`Dim`, a unit witness, a scale, and a live figure display. A film plays as a flipbook in the cell,
with no encoder and no file. A figure above 5000 drawn elements is drawn on a canvas from bytes.

## A live figure

A cell whose value is built with `interact { … }` is a figure with controls. The extension opens an
output channel for the cell; each change the reader makes is stepped by the kernel and answered with
the figure, or with the parts of it that changed. A drag zooms, a shift-drag pans, a double click
resets, and the pointer's position is read in the axes' units. A saved notebook keeps the figure at
the state the reader left it in, with its controls disabled. This needs Verso 1.2.3 or newer; on an
older host the same page is drawn with its controls disabled.

## Why an extension

Verso's own F# formatter claims every record at its priority, and a `Figure` is a record, so
without something ranked above it a table of fields is shown. A live figure also needs the cell's
output channel and its size, which only a formatter is given. The extension and the kernel load into
different assembly load contexts, so the extension reaches Marey by reflection, depends only on
`Verso.Abstractions`, and keeps working across Marey versions.

Without the extension, a figure rendered to an SVG string still displays, through Verso's SVG
formatter, with nothing installed.

- [The guide on notebooks](https://github.com/Axym-Labs/marey/blob/main/docs/guide/11-notebooks.md) and [on interaction](https://github.com/Axym-Labs/marey/blob/main/docs/guide/12-interaction.md)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
