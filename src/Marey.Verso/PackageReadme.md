# Marey.Verso

The [Verso](https://github.com/DataficationSDK/Verso) notebook extension for
[Marey](https://github.com/Jonathan-Schaefer-git/marey), a dimensionally-typed visualization system
for F#.

**This package is installed into the notebook host, not referenced from your code.** With it, a
cell whose value is a `Figure` displays the figure — no `.Show()`, no save-then-open, no display
call:

```fsharp
#r "nuget: Marey.Html"

open Marey

Chart.line U.second U.metre t x
|> Chart.withTitle "Free fall"
```

Nine types render: `Figure`, `Node`, `Color`, `Ramp`, `Color[]`, `PaletteReport`, `Dim`, `U<'u>`
and `IScaleErased`.

## Installing

Verso's **Extensions** panel searches nuget.org, then unpacks the package into its own store and
asks you to approve it — extensions are code that runs inside your notebook host, so it asks per
version.

**If the panel cannot find `Marey.Verso`, that is expected right now and is not a fault in the
package.** These packages are published and restorable — `#r "nuget: …"` and `dotnet add package`
both work — but they are not yet in nuget.org's *search* index, and the Extensions panel searches.
Until that resolves, install it by hand: download the `.nupkg` from
[nuget.org/packages/Marey.Verso](https://www.nuget.org/packages/Marey.Verso) and add it through
Verso's install-from-file route.

While developing, the shortest path skips the package manager entirely — load the built assembly
straight into the session, which is also the right way to bisect a display problem:

```
#!extension path\to\Marey.Verso.dll
```

## Installing it is not the same as referencing Marey

They are separate steps and neither substitutes for the other. `#r "nuget: Marey.Html"` in a cell
gives you the types; the extension gives you the rendering. Verso has no `#r`-time extension hook.

Without the extension nothing is lost except the convenience — render to SVG and Verso's built-in
formatter draws it, with nothing installed:

```fsharp
figure |> Chart.render SvgBackend.render (Chart.size 720.0 450.0)
```

## Why an extension is needed at all

Verso resolves output through formatters ordered by priority, and its F# formatter claims every
record, union and tuple at priority 20. `Figure` is a record, so without something ranked above it
you get a table of fields. This sits at priority 30.

## It does not reference Marey

Deliberately. A Verso extension and the notebook's `#r` load into different
`AssemblyLoadContext`s, so a `Figure` here and a `Figure` there would be two unequal types. The
bridge is reflective by necessity, and the payoff is that this package has exactly one dependency —
`Verso.Abstractions` — and keeps working across Marey versions.

- [The guide](https://github.com/Jonathan-Schaefer-git/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Jonathan-Schaefer-git/marey/tree/main/examples)
