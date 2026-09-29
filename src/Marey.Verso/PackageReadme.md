# Marey.Verso

The [Verso](https://github.com/DataficationSDK/Verso) notebook extension for
[Marey](https://github.com/Jonathan-Schaefer-git/marey), a dimensionally-typed visualization system
for F#.

**This package is installed into the notebook host, not referenced from user code.** With it, a
cell whose value is a `Figure` displays the figure — no `.Show()`, no save-then-open, no display
call:

```fsharp
#r "nuget: Marey.Html"

open Marey

Chart.line U.second U.metre t x
|> Chart.withTitle "Free fall"
```

Ten types render: `Figure`, `Film`, `Node`, `Color`, `Ramp`, `Color[]`, `PaletteReport`, `Dim`,
`U<'u>` and `IScaleErased`.

A `Film` **plays in the cell** — an animation built with `Marey.Anim` and the `scene { … }` block
displays as a looping flipbook rather than as a record field table, with no encoder installed and
no video file on disk.

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
keeps it working across Marey versions.

- [The guide](https://github.com/Jonathan-Schaefer-git/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Jonathan-Schaefer-git/marey/tree/main/examples)
