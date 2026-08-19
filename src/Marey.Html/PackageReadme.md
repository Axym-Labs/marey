# Marey.Html

HTML renderings of [Marey](https://github.com/Jonathan-Schaefer-git/marey) values, for a notebook
cell. Host-neutral, **zero dependencies**, `netstandard2.1`.

This is also the convenient package to reference, because it sits at the top of the graph: install
it and `Marey.Core`, `Marey.Units`, `Marey.Plot` and the SVG backend all arrive with it.

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

## Host-neutral on purpose

Every function here is `something -> string`. There is no formatter registration, no kernel
reference and no dependency on a notebook host — which is why this package outlived the host it
was originally written for. Polyglot Notebooks was retired in March 2026 and .NET Interactive in
April; the registration half of this package went with them, and `Html` did not have to change.

The output carries no `<style>` block and no pinned colours, so it inherits the notebook's theme
rather than fighting it.

## To make a bare value display

In [Verso](https://github.com/DataficationSDK/Verso), install
[`Marey.Verso`](https://www.nuget.org/packages/Marey.Verso) into the notebook host and a cell whose
value is a `Figure` shows the figure. Referencing this package and installing that extension are
separate steps that cannot substitute for each other: this one gives you the types, the extension
gives you the rendering.

Without any extension, render to SVG and let the host's built-in SVG formatter draw it — that path
needs nothing installed:

```fsharp
figure |> Chart.render SvgBackend.render (Chart.size 720.0 450.0)
```

## The name is wrong and is being left alone

It named a Microsoft product that no longer exists. Renaming it is a breaking change waiting for a
reason, and it is on the list.

- [The guide](https://github.com/Jonathan-Schaefer-git/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Jonathan-Schaefer-git/marey/tree/main/examples)
