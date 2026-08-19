# Marey.Html

HTML renderings of [Marey](https://github.com/Jonathan-Schaefer-git/marey) values, for a notebook
cell. Host-neutral, **zero dependencies**, `netstandard2.1`.

This is also the convenient package to reference, because it sits at the top of the graph:
installing it brings `Marey.Core`, `Marey.Units`, `Marey.Plot` and the SVG backend with it.

```fsharp
#r "nuget: Marey.Html"

open Marey

let figure = Chart.line U.second U.metre t x

// something -> string. No kernel, no display call, no global registry.
Html.figure figure
```

Nine types render: `Figure` and `Node` as inline SVG, `Color` as a swatch that does not lie about
alpha, `Ramp` resampled through Oklab, `Color[]`, `PaletteReport` as a colour-vision-deficiency and
contrast report, and `Dim`, `U<'u>` and `IScaleErased` as themselves.

## Host-neutral

Every function here is `something -> string`: no formatter registration, no kernel reference and no
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

- [The guide](https://github.com/Jonathan-Schaefer-git/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Jonathan-Schaefer-git/marey/tree/main/examples)
