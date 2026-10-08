# Running the notebooks

Marey targets [**Verso**](https://github.com/DataficationSDK/Verso) — MIT, open source, imports
`.dib` and `.ipynb`, and runs in VS Code, the browser, the terminal and CI. It is the maintained
successor to Polyglot Notebooks, which was retired in March 2026 along with .NET Interactive.

[`marey-tour.dib`](marey-tour.dib) is the tour.

## Two ways to see a figure

**Without installing anything.** Verso ships an `SvgFormatter` that claims any string beginning
with `<svg`. The SVG backend produces exactly that, so this renders in a stock Verso:

```fsharp
#r "nuget: Marey.Html"

open Marey
open Marey.UnitsOfMeasure

Chart.line U.second U.metre t x
|> Chart.withTitle "Free fall"
|> Chart.render SvgBackend.render (Chart.size 720.0 450.0)
```

**With the `Marey.Verso` extension**, the value itself displays — no pipe, no size, no ceremony:

```fsharp
Chart.line U.second U.metre t x
|> Chart.withTitle "Free fall"
```

Twelve types then display: `Figure`, `Film`, `Node`, `Color`, `Ramp`, `Color[]`, `PaletteReport`,
`Dim`, `U<'u>`, `IScaleErased`, `Live` and `Recorder`. A `Film` **plays** — an animation built with
the `scene { … }` block loops in the cell as a flipbook, with no encoder installed and no video file
on disk. A `Live`, built with `interact { … }`, is a figure with controls that the kernel redraws as
the reader moves them, in a Verso newer than 1.2.2; [`examples/interact.dib`](../../examples/interact.dib)
is one.

Installing the extension and referencing the library are **separate steps that cannot substitute
for each other**. `#r "nuget: Marey.Html"` in a cell provides the types; the extension provides the
rendering. Verso has no `#r`-time extension hook.

## Installing the extension

Install `Marey.Verso` from Verso's **Extensions** panel, then approve it once — extensions are
executable code that runs inside the notebook host, so approval is requested per version.

## Setting up

| | |
|---|---|
| .NET SDK | 10.0.301 |
| `Microsoft.NETCore.App` runtime | 10.0.9 |
| `Microsoft.AspNetCore.App` runtime | 10.0.9 |
| Verso | `code --install-extension` the Verso extension for VS Code, or use the browser host |

Verso's VS Code extension carries its own engine. The Python **Jupyter** extension is unrelated and
does nothing for .NET.

## If something does not display

1. **A field table of the object's members.** The extension is not installed, not approved, or not
   enabled for this notebook. Everything else is unaffected —
   `Chart.render SvgBackend.render (Chart.size w h)` still draws, since that path needs no
   extension.
2. **`Marey.Core` loads but `Chart` and the measures are undefined.** Only `Marey.Core` was
   referenced. `open Marey` succeeds because the namespace exists, but `s` and `m` live in
   `Marey.Units` and `Chart` in `Marey.Plot`. Reference `Marey.Html` and let the graph resolve.
3. **A figure wider than the cell.** It scrolls; it does not shrink, deliberately, because scaling
   the SVG would invalidate the text measurements it was laid out with. With the extension
   installed this should not happen — the size comes from the cell.
