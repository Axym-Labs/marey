# Running the notebooks

`quiver-tour.dib` is written as the *published-package* experience: five `#r "nuget: …"` lines and
nothing else. Until the packages are on nuget.org, running it locally needs one extra line. This
file is the setup, and it says plainly which parts have been verified and which have not.

## What a kernel needs

.NET Interactive runs as a separate process — a **kernel** — that your notebook front end talks to.
Five things have to line up:

| | |
|---|---|
| .NET SDK | 10.0.301 |
| `Microsoft.NETCore.App` runtime | 10.0.9 |
| **`Microsoft.AspNetCore.App` runtime** | 10.0.9 — the kernel needs this one, not just the base runtime. `dotnet --list-runtimes` will tell you. |
| The kernel | `Microsoft.dotnet-interactive` 1.0.712001, acquired by the extension into its own global storage as a *local* tool |
| A front end | Polyglot Notebooks, or one of the alternatives below |

The kernel tool's current build targets `net10.0` and declares **both** frameworks above, so on a
box with only the base runtime it installs and then fails to start. Worth knowing because that
failure reads as a broken extension rather than a missing runtime — and it is not the only failure
that does; see the acquisition bug further down, which is the one you are most likely to hit.

## A front end

**VS Code + Polyglot Notebooks** is the shortest path and opens `.dib` files natively:

```
code --install-extension ms-dotnettools.dotnet-interactive-vscode
```

The extension acquires the kernel itself, so the `dotnet tool install` above is only needed for the
other two paths. Note that the Python **Jupyter** extension is a different thing and does not
provide a .NET kernel — having it installed does nothing for this.

The alternatives, in descending convenience: `dotnet repl` (`dotnet tool install -g dotnet-repl`),
which runs a `.dib` in the terminal; or a real Jupyter install, where
`dotnet interactive jupyter install` registers the .NET kernels.

## Running the tour before the packages are published

`#r "nuget: Quiver.Core"` needs a feed with `Quiver.Core` in it. Build one:

```
dotnet pack -o ./local-feed
```

That produces all six packages at `1.0.0` — `Quiver.Core`, `Quiver.Units`, `Quiver.Backend.Svg`,
`Quiver.Backend.Tikz`, `Quiver.Plot`, `Quiver.Interactive`. Then add **one line above the `#r`
block** in the notebook's first F# cell, pointing at that folder as an absolute path:

```fsharp
#i "nuget:C:\Users\you\source\quiver\local-feed"
```

`#i` adds a package source for the session and must come before the `#r` lines that use it.

This is the path worth taking rather than referencing the built `.dll`s directly, because it is the
only way to exercise the one thing the test suite cannot: whether the kernel actually runs
`interactive-extensions/dotnet/extension.dib` out of the package and registers the formatters. If
figures display **without** anyone calling `Quiver.Formatters.register ()`, that worked.

Referencing the DLLs instead — `#r "…/src/Quiver.Interactive/bin/Debug/netstandard2.1/Quiver.Interactive.dll"` —
also works and is quicker, but there is no package for the kernel to find an extension in, so you
must call `Quiver.Formatters.register ()` yourself. That tests the formatters and not the wiring.

## What has and has not been verified

Verified on this machine:

- All six packages pack cleanly, with no NuGet warnings, and `Quiver.Interactive.1.0.0.nupkg`
  contains `interactive-extensions/dotnet/extension.dib` at exactly the path the kernel looks for.
- All seventeen F# cells of `quiver-tour.dib` evaluate against the built assemblies under
  `dotnet fsi`. The only failure is the final `Formatters.registerWith` call, which is the
  documented no-kernel behaviour: the package compiles against
  `Microsoft.DotNet.Interactive.Formatting` and deliberately does not ship it (D39), so
  registration needs a kernel and `Html.*` does not.
- Every HTML fragment parses as XML, and all of them were rendered in a headless browser against
  both a light and a dark background and inspected.

**Not** verified: that a live kernel loads the package and runs `extension.dib`. There is no kernel
here. That is the gap this file exists to help close, and it is why the one line inside
`extension.dib` is pinned by a reflection test rather than trusted.

## If the kernel never starts at all

On **.NET SDK 10**, the Polyglot Notebooks extension works exactly once and then breaks, with this
in its output log:

```
create-tool-manifest: Finished with code 73.
  Overwrite   ./dotnet-tools.json
Error acquiring dotnet-interactive tool: Error: Unable to create local tool manifest.
Extension activation failed: Cannot read properties of undefined (reading 'workingDirectory')
```

It is not a corrupted install and reinstalling the extension does not fix it. It is a version skew
between the extension and the SDK, and the whole of it is these four lines of
`out/src/vscode-common/acquisition.js`:

```js
const toolManifestFile = path.join(globalStoragePath, '.config', 'dotnet-tools.json');
if (!fs.existsSync(toolManifestFile)) {
    await createToolManifest(args.dotnetPath, globalStoragePath);   // throws on non-zero exit
}
```

The extension looks for the manifest at `.config/dotnet-tools.json`. **SDK 10's
`dotnet new tool-manifest` writes it to `./dotnet-tools.json` instead** — verified by running the
template into an empty directory on 10.0.301. So the check never succeeds, and the command runs on
every activation:

- **first run** — the folder is empty, the template writes the root-level file, exit 0, the tool
  installs, everything works. This is the day it worked.
- **every run after** — the root-level file now exists, the template refuses to overwrite it
  without `--force` and exits **73**, `createToolManifest` throws, the acquire promise rejects, and
  `launchOptions` comes back `undefined`. Hence the second error: nothing has a `workingDirectory`.

The fix is to put the manifest where the extension looks, which also makes the failing call
unreachable forever, since `fs.existsSync` then short-circuits it:

```bash
cd "$APPDATA/Code/User/globalStorage/ms-dotnettools.dotnet-interactive-vscode"
mkdir -p .config
mv dotnet-tools.json .config/dotnet-tools.json
dotnet tool restore
```

Then **reload the VS Code window**. `dotnet tool run dotnet-interactive -- --version` in that
folder is the exact probe the extension uses next; if it prints a version, activation will get past
the point it was failing at.

A stray empty `dotnet-tools.json` reappearing at the folder root is harmless — when both exist,
`.config/dotnet-tools.json` wins, confirmed with `dotnet tool list`.

## If something does not display

In order of likelihood:

1. **A reflection dump instead of a figure.** The formatters were not registered — the extension
   did not run. Call `Quiver.Formatters.register ()` in a cell; if that fixes it, the packaged
   `extension.dib` is the problem, not the formatters.
2. **`FileNotFoundException: Microsoft.DotNet.Interactive.Formatting`.** You are not in a kernel —
   `dotnet fsi`, or a plain console app. Expected, by design.
3. **A figure wider than the cell.** It scrolls; it does not shrink, deliberately, because scaling
   the SVG would invalidate the text measurements it was laid out with. Use
   `Quiver.Formatters.registerWith (Quiver.Chart.size 560.0 350.0)`.
