# Running the notebooks

Marey targets [**Verso**](https://github.com/DataficationSDK/Verso) — MIT, open source, imports
`.dib` and `.ipynb`, and runs in VS Code, the browser, the terminal and CI. It is the maintained
successor to Polyglot Notebooks, which was retired in March 2026 along with .NET Interactive.

[`marey-tour.dib`](marey-tour.dib) is the tour.

## Two ways to see a figure

**Without installing anything.** Verso ships an `SvgFormatter` that claims any string beginning
with `<svg`. The SVG backend produces exactly that, so this renders in a stock Verso today:

```fsharp
#r "nuget: Marey.Html"

open Marey

Chart.line U.second U.metre t x
|> Chart.withTitle "Free fall"
|> Chart.render SvgBackend.render (Chart.size 720.0 450.0)
```

**With the `Marey.Verso` extension**, the value itself displays — no pipe, no size, no ceremony:

```fsharp
Chart.line U.second U.metre t x
|> Chart.withTitle "Free fall"
```

Nine types then display: `Figure`, `Node`, `Color`, `Ramp`, `Color[]`, `PaletteReport`, `Dim`,
`U<'u>` and `IScaleErased`.

Installing the extension and referencing the library are **separate steps that cannot substitute
for each other**. `#r "nuget: Marey.Html"` in a cell gives you the types; the extension gives you
the rendering. Verso has no `#r`-time extension hook.

## Installing the extension

From Verso's **Extensions** panel, then approve it once — extensions are executable code that runs
inside your notebook host, so Verso asks per version.

**If the panel cannot find `Marey.Verso`, that is expected right now.** The packages are published
and restorable, but are not yet in nuget.org's *search* index, and the panel searches. Until that
resolves, download the `.nupkg` from
[nuget.org/packages/Marey.Verso](https://www.nuget.org/packages/Marey.Verso) and install it from
file.

While developing, load the built assembly straight into the session instead:

```
#!extension C:\path\to\marey\src\Marey.Verso\bin\Debug\net10.0\Marey.Verso.dll
```

That is also the right way to bisect a display problem: if `#!extension` fixes it, the problem is
installation or a stale package, not the formatter.

## Setting up

| | |
|---|---|
| .NET SDK | 10.0.301 |
| `Microsoft.NETCore.App` runtime | 10.0.9 |
| `Microsoft.AspNetCore.App` runtime | 10.0.9 |
| Verso | `code --install-extension` the Verso extension for VS Code, or use the browser host |

Verso's VS Code extension carries its own engine. The Python **Jupyter** extension is unrelated and
does nothing for .NET.

## Running against a local build

`marey-tour.dib` is written as the published-package experience — one `#r "nuget: …"` line, with
the rest arriving transitively. To run it against your own build instead:

```
dotnet pack -o local-feed -p:Version=1.0.$(date +%m%d%H%M)
```

**Stamp the version. Do not pack repeatedly as `1.0.0`.** NuGet's global packages folder is keyed
on id *and version*: once `marey.html/1.0.0` has been extracted to `~/.nuget/packages`, a
`#r "nuget: Marey.Html"` finds it there and **never looks at your feed again**, no matter how many
times you re-pack. The symptom is that a code change appears to have no effect, with nothing in the
cell output to suggest a stale assembly is involved. A monotonic stamp sidesteps it permanently.

If the cache is already poisoned:

```
rm -rf ~/.nuget/packages/marey.*
```

Then add one line above the `#r` in the first F# cell:

```fsharp
#i "nuget:C:\path\to\marey\local-feed"
```

`#i` adds a package source **for the F# session only**. It does nothing for Verso's Extensions
panel, which searches the feeds in your NuGet *configuration* — so installing `Marey.Verso` from a
local folder needs that folder registered:

```
dotnet nuget add source C:\path\to\marey\local-feed --name MareyLocal
```

Add `local-feed/` to `.gitignore` if you put it in the repo.

## If something does not display

1. **A field table of the object's members.** Three causes, in the order worth checking: a **stale
   `Marey.Html` in the NuGet cache** — likeliest, because it survives rebuilding, re-packing and
   reinstalling; the extension not installed, approved or enabled; or the local feed not being a
   registered NuGet source, so the Extensions panel never found the package. Bisect with
   `#!extension` on the built DLL. Everything else is fine regardless —
   `Chart.render SvgBackend.render (Chart.size w h)` still draws, since that path needs no
   extension.
2. **`Marey.Core` loads but `Chart` and the measures are undefined.** You referenced `Marey.Core`
   alone. `open Marey` succeeds because the namespace exists, but `s` and `m` live in
   `Marey.Units` and `Chart` in `Marey.Plot`. Reference `Marey.Html` and let the graph resolve.
3. **A figure wider than the cell.** It scrolls; it does not shrink, deliberately, because scaling
   the SVG would invalidate the text measurements it was laid out with. With the extension
   installed this should not happen — the size comes from the cell.
