# 11. Notebooks

In a [Verso](https://github.com/DataficationSDK/Verso) notebook, a cell whose value is a figure
displays as the figure: no display call, no file. The first cell loads the extension and the
library, and Verso asks once before loading the extension's code:

```
#!extension Marey.Verso
#r "nuget: Marey"
```

What displays: a `Figure`, a `Film`, a scene `Node`, a `Color`, a `Ramp` and a palette, a
`PaletteReport`, a `Dim`, a unit witness, a scale, and a live figure (§12). Without the extension a
figure rendered to an SVG string still displays, through Verso's own SVG formatter.

The renderer is `Marey.Html`, and it knows nothing about notebooks: each rendering is a function
to a string, so the same HTML can go into a page, a report or another host.

```fsharp
let dipoleHtml = Html.figure dipole
let swingFlipbook = Html.film swingFilm

printfn "the dipole is %d characters of HTML; the swing a flipbook of %d" dipoleHtml.Length swingFlipbook.Length
printfn "the flipbook animates with no stylesheet: %b" (swingFlipbook.Contains "<animate" && not (swingFlipbook.Contains "<style"))
```

**A film plays in the cell** as a flipbook: its frames sampled into one SVG and shown in turn, as
this guide's films are. A `<style>` in an output cell would restyle the reader's whole notebook, so
the flipbook animates with SVG's own `<animate>`, which stays inside the fragment. `Html.film`
samples 48 frames, about half a megabyte; `Html.filmAt n` takes another budget. It needs no
encoder, so it plays wherever the notebook renders.

**A large figure is drawn on a canvas.** From 5000 drawn elements on, `Html.CanvasThreshold`, the
cell holds a canvas and the scene goes to it as bytes rather than SVG: a scatter of 10⁵ points is
2.1 MB rather than 6.4 MB and draws in tens of milliseconds. The bytes carry the kernel's text
measurements, so the labels sit where they would in the SVG, and under the canvas is the figure's
SVG without its large batches, so a viewer that runs no script still shows the axes, titles and
legend. `Html.renderMode` chooses `RenderMode.Svg`, `Canvas` or `Auto`, the default.

**A saved notebook** keeps each figure as it was drawn, a live one at the state the reader left it
in, with its controls disabled and a line saying it is static until the kernel runs again.
