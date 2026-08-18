# Running the notebooks

The host these were written for is gone. Polyglot Notebooks was deprecated on 2026-02-11 and
retired 2026-03-27; **.NET Interactive**, the engine under it, followed on 2026-04-24. The
maintained successor is [**Verso**](https://github.com/DataficationSDK/Verso) — MIT, open source,
imports `.dib` and `.ipynb`, and runs in VS Code, the browser, the terminal and CI.

Quiver targets Verso. The .NET Interactive support that existed briefly has been deleted rather
than carried; what survived is `Quiver.Interactive`'s `Html` module, which never referenced a
notebook host and so did not need to change.

## Two ways to see a figure

**Without installing anything.** Verso ships an `SvgFormatter` that claims any string beginning
with `<svg`. The SVG backend produces exactly that, so this renders in a stock Verso today:

```fsharp
Chart.line U.second U.metre t x
|> Chart.withTitle "Free fall"
|> Chart.render SvgBackend.render (Chart.size 720.0 450.0)
```

**With the extension**, the value itself displays — no pipe, no size, no ceremony:

```fsharp
Chart.line U.second U.metre t x
|> Chart.withTitle "Free fall"
```

That is what `Quiver.Verso` is for. Install it from Verso's **Extensions** panel and approve it
once; extensions are executable code that runs inside your notebook host, so Verso asks, per
version. Nine types then display: `Figure`, `Node` (with a node census), `Color`, `Ramp`,
`Color[]`, `PaletteReport`, `Dim`, `U<'u>` and `IScaleErased`.

Note that installing the extension and referencing the library are **separate steps that cannot
substitute for each other**. `#r "nuget: Quiver.Interactive"` in a cell gives you the types; the
extension gives you the rendering. Verso has no `#r`-time extension hook — unlike .NET Interactive,
which ran an `extension.dib` out of any referenced package.

## Why the extension is needed at all

Verso resolves output through `IDataFormatter`s ordered by priority. `Verso.FSharp`'s own
`FSharpDataFormatter` sits at **priority 20** and claims *every* F# record, union and tuple —
`Figure` is a record, `Node` a union, `Color` a record — so without something ranked above it you
get the field table it renders. `Quiver.Verso` is that something, at priority 30.

The tempting cheap alternative does not work. Verso's built-in `HtmlFormatter` is duck-typed on a
public parameterless `ToHtml() : string`, which would need no extension at all — but it is *also*
priority 20, and `FormatterResolver` sorts stably, so an equal-priority pair resolves by
registration order. A figure would render as a picture or as a table depending on enumeration
order. On top of that, `Figure.ToHtml()` would put HTML rendering inside `Quiver.Plot`, which would
then need the SVG backend, which D21 forbids — and `Color[]` cannot be given a member at all.

## Setting up

| | |
|---|---|
| .NET SDK | 10.0.301 |
| `Microsoft.NETCore.App` runtime | 10.0.9 |
| `Microsoft.AspNetCore.App` runtime | 10.0.9 |
| Verso | `code --install-extension` the Verso extension for VS Code, or use the browser host |

Verso's VS Code extension carries its own engine. The Python **Jupyter** extension is unrelated and
does nothing for .NET.

## Running the tour before the packages are published

`quiver-tour.dib` is written as the published-package experience: one `#r "nuget: …"` line, with
Core, Units, the SVG backend and Plot arriving transitively. Until the packages are on nuget.org:

```
cd C:\path\to\quiver
dotnet pack -o local-feed
```

That produces all seven packages at `1.0.0`. Add one line above the `#r` in the first F# cell:

```fsharp
#i "nuget:C:\path\to\quiver\local-feed"
```

`#i` adds a package source for the session and must precede the `#r` lines that use it. The same
feed is where Verso can install `Quiver.Verso` from — see Verso's *Local Feed Testing* notes in
`docs/extensions/packaging-and-publishing.md` upstream, or load the built DLL directly with
`#!extension ./src/Quiver.Verso/bin/Debug/net10.0/Quiver.Verso.dll` while developing.

Add `local-feed/` to `.gitignore` if you put it in the repo.

## What has and has not been verified

Verified in the test suite (`tests/Quiver.Tests/NotebookTests.fs`):

- `QuiverFormatter` driven as a real `IDataFormatter` through Verso's own `StubFormatterContext`:
  it claims all nine types and nothing else, claims only the mime type it emits, outranks the
  built-ins at 20, is not a fallback, and carries `[VersoExtension]` with a parameterless
  constructor so the host can discover and activate it.
- **The load-context assumption, reproduced rather than trusted.** The extension is loaded into a
  genuinely separate collectible `AssemblyLoadContext` — `Verso.Abstractions` shared from the
  default context, everything else isolated, exactly as Verso does it — and asked to render a
  `Figure` built in the test's own context. This is the assumption the whole package rests on and
  the one that would fail *silently*: a `CanFormat` quietly answering `false` is indistinguishable
  from "nothing claimed it", which is a field table and no error anywhere.
- The two bridge function signatures in `Quiver.Html`, resolved by the exact reflection the
  extension uses, so renaming either fails a test instead of every notebook.
- Every HTML fragment parsed as XML; escaping; culture invariance; the no-`<style>` and
  no-pinned-colour rules; figures sized from the host's `MaxWidth`/`MaxHeight`.

**Not** verified: that a real Verso host discovers, approves and loads the package end to end.
There is no Verso installation on the machine this was built on.

## If something does not display

1. **A field table of the object's members.** The extension is not installed, not approved, or not
   enabled. Everything is otherwise fine — pipe through `Chart.render SvgBackend.render (Chart.size w h)`
   to confirm, since that path needs no extension.
2. **`Quiver.Core` loads but `Chart` and the measures are undefined.** You referenced
   `Quiver.Core` alone. `open Quiver` succeeds because the namespace exists, but `s`, `m` live in
   `Quiver.Units` and `Chart` in `Quiver.Plot`. Reference `Quiver.Interactive` and let the graph
   resolve.
3. **A figure wider than the cell.** It scrolls; it does not shrink, deliberately, because scaling
   the SVG would invalidate the text measurements it was laid out with. With the extension
   installed this should not happen — the size comes from the cell.
