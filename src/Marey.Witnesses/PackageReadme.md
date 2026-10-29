# Marey.Witnesses

Code fixes for [Marey](https://axym-labs.github.io/marey/)'s unit witnesses, over the F# compiler
service, for a tool that checks F# itself: an editor, a notebook, a live-coding app.
`Marey.Analyzers` puts the same fixes behind FSharp.Analyzers.SDK for Ionide and the CLI.

Given a file's parse and check results, `Findings.ofFile` returns the Marey calls that do not
compile for want of the right witness, each with the fixes that make it compile:

| Code | The call | The fix |
|---|---|---|
| MAREY001 | `Chart.lineIn t x` | `Chart.lineIn U.second U.metre t x` |
| MAREY002 | `Chart.lineIn U.metre U.metre t x`, `t` in seconds | `U.second` for the first |
| MAREY003 | `Chart.line t x`, raw arrays | `Chart.lineIn U.second U.metre t x`, or `Chart.line (U.second.Of t) (U.metre.Of x)` |

```fsharp norun
open Marey.Witnesses

let parse, answer = checker.ParseAndCheckFileInProject(path, 0, source, options) |> Async.RunSynchronously

match answer with
| FSharpCheckFileAnswer.Succeeded check ->
    for f in Findings.ofFile parse check do
        printfn "%s %A: %s" f.Code f.Range f.Message

        for fix in f.Fixes do
            printfn "  %s: %A" fix.Title fix.Edits
| FSharpCheckFileAnswer.Aborted -> ()
```

A fix is a list of edits applied together; `Findings.collapse` makes one replacement of them for
a host that takes one per fix. `Witness.ofMeasure` is the witness expression for a measure as the
compiler prints it.

It reads Marey's witnesses from the symbols of the Marey assemblies the checked file references,
and references no Marey package itself, so it works with whichever Marey the file was checked
against. FSharp.Compiler.Service 43.12.202 or later.
