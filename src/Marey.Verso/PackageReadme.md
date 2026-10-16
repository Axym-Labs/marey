# Marey.Verso

The [Verso](https://github.com/DataficationSDK/Verso) notebook extension for
[Marey](https://github.com/Axym-Labs/marey), a dimensionally-typed visualization system
for F#.

**This package is installed into the notebook host, not referenced from user code.** With it, a
cell whose value is a `Figure` displays the figure — no `.Show()`, no save-then-open, no display
call:

```fsharp
#r "nuget: Marey.Html"

open Marey
open Marey.UnitsOfMeasure

Chart.line U.second U.metre t x
|> Chart.withTitle "Free fall"
```

Twelve types render: `Figure`, `Film`, `Node`, `Color`, `Ramp`, `Color[]`, `PaletteReport`, `Dim`,
`U<'u>`, `IScaleErased`, and a live figure, `Live` or `Recorder`.

A `Film` **plays in the cell** — an animation built with `Marey.Anim` and the `scene { … }` block
displays as a looping flipbook rather than as a record field table, with no encoder installed and
no video file on disk.

## A live figure

A cell whose value is a `Live`, built with `interact { … }` from `Marey.Interact`, is a figure with
controls. The extension opens an output channel for the cell, and each change the reader makes is
stepped by the kernel and answered with the figure, or with the parts of it that changed. A drag
zooms, a shift-drag pans, a double click resets, and the pointer's position is read in the axes'
units. A save writes the figure at the state the reader left it in, with its controls disabled.

This needs a Verso whose formatter context carries output channels and the cell's id: a release
1.2.3 or newer. On an older host, or one without channels, the extension writes the same page
with the controls disabled and opens nothing.

## Installing

Verso's **Extensions** panel searches nuget.org, then unpacks the package into its own store and
asks for approval — extensions are code that runs inside the notebook host, so approval is
requested per version.

## Installing it is not the same as referencing Marey

The two are separate steps and neither substitutes for the other. `#r "nuget: Marey.Html"` in a
cell provides the types; the extension provides the rendering. Verso has no `#r`-time extension
hook.

Without the extension nothing is lost except the convenience — render to SVG and Verso's built-in
formatter draws it, with nothing installed:

```fsharp
figure |> Chart.render SvgBackend.render (Chart.size 720.0 450.0)
```

## Why an extension is needed

Verso resolves output through formatters ordered by priority, and its F# formatter claims every
record, union and tuple at priority 20. `Figure` is a record, so without something ranked above it
a table of fields is displayed instead. This formatter sits at priority 30.

## No reference to Marey

A Verso extension and the notebook's `#r` load into different `AssemblyLoadContext`s, so a `Figure`
on one side and a `Figure` on the other would be two unequal types. The bridge is therefore
reflective, which leaves this package with exactly one dependency — `Verso.Abstractions` — and
keeps it working across Marey versions. A live figure's state crosses the same boundary as an
opaque object that only `Marey.Html` looks inside.

- [The guide](https://github.com/Axym-Labs/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
