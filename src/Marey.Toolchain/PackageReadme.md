# Marey.Toolchain

Resolves and runs the external programs that [Marey](https://axym-labs.github.io/marey/) drives.
**A tool is resolved and *reported*, or it is absent and the report names what to install.**

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

Marey drives programs it does not ship, such as `pdflatex` for the TikZ goldens, `pdftoppm` for
the ink gate and `ffmpeg` for video. This package is the one place that asks whether a program is
there, and it owns the wording of "it is not installed". Before it, that question was answered five
times in four files, in three different registers, and a reader had to know which one printed
which.

**It has no dependencies, on purpose.** The packages that use it already drag in a toolchain each,
and the module that reports a missing one must not be a third thing to install.

## What is in it

- **`Toolchain.onPath`**, a hand-rolled `PATH` walk rather than a shell-out to `where`/`which`.
  Those are external tools themselves, and their absence would be reported as the absence of the
  thing being looked for.
- **`ResolvedTool`**, which carries the install sentence rather than deriving it, since the right
  thing to say differs per tool and per platform.
- **`Toolchain.run` / `runArgs`**, which capture both streams and drain them *concurrently*.
  Reading one to the end and then the other deadlocks as soon as the child fills the second pipe's
  buffer, which happens on the very run that finally had enough errors to be worth reading. Prefer
  `runArgs` whenever an argument is a path. `ProcessStartInfo.Arguments` is re-split using Windows'
  quoting rules on every platform, and a vector of arguments needs no quoting.
- **`ToolOutcome`**, with three cases instead of a `Result`. "Not installed", "timed out" and "ran
  and exited" call for different behaviour at every call site, and collapsing them is how a gate
  goes quietly vacuous.

- [The repository](https://github.com/Axym-Labs/marey)
