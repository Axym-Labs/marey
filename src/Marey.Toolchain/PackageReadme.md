# Marey.Toolchain

External-program resolution and invocation for
[Marey](https://github.com/Axym-Labs/marey), a dimensionally-typed visualization
system for F#. **A tool is resolved and *reported*, or it is absent and the absence names what to
install.**

```fsharp
#r "nuget: Marey.Toolchain"

open Marey

let ffmpeg = Toolchain.resolve "ffmpeg, from your package manager" "ffmpeg"

printfn "%s" (Toolchain.report "nothing was encoded" ffmpeg)

match Toolchain.runArgs ffmpeg [ "-version" ] "." 20_000 with
| ToolRan run -> printfn "%d: %s" run.ExitCode run.Out
| ToolUnavailable why -> printfn "%s" why
| ToolTimedOut why -> printfn "%s" why
```

Marey drives programs it does not ship — `pdflatex` for the TikZ goldens, `pdftoppm` for the ink
gate, `ffmpeg` for video. This is the one place that answers "is it there?", and the wording of
"it is not installed" is part of what it implements. It exists because that question had been
answered five times in four files, in three different registers, and a reader had to know which
one printed which.

**Zero dependencies, deliberately.** The packages that consume this already drag a toolchain
each; the module that reports their absence must not be a third thing to install.

## What is in it

- **`Toolchain.onPath`** — a hand-rolled `PATH` walk rather than a shell-out to `where`/`which`,
  because those are themselves external tools and their absence would be reported as the absence
  of the thing being looked for.
- **`ResolvedTool`**, carrying the install sentence rather than deriving it: the right thing to
  say differs per tool and per platform.
- **`Toolchain.run` / `runArgs`**, which capture both streams and drain them *concurrently* —
  reading one to the end and then the other deadlocks as soon as the child fills the second
  pipe's buffer, which is to say on the run that finally had enough errors to be worth reading.
  Prefer `runArgs` whenever an argument is a path: `ProcessStartInfo.Arguments` is re-split using
  Windows' quoting rules on every platform, and a vector needs no quoting.
- **`ToolOutcome`**, three cases and not a `Result`: "not installed", "timed out" and "ran and
  exited" call for different behaviour at every call site, and collapsing them is how a gate goes
  quietly vacuous.

- [The repository](https://github.com/Axym-Labs/marey)
