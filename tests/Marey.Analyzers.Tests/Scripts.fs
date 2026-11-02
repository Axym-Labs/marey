/// A script checked as an editor checks it, against the Marey assemblies built beside the tests,
/// and the text a fix makes of it.
module Marey.Analyzers.Tests.Scripts

open System
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Text
open Marey.Witnesses

let checker = FSharpChecker.Create()

let private here = AppContext.BaseDirectory

/// The lines every script starts with: the Marey assemblies, and the opens a user writes.
let header =
    [ for dll in [ "Marey.Core"; "Marey.Units"; "Marey.Anim"; "Marey.Plot" ] do
          yield sprintf "#r @\"%s\"" (Path.Combine(here, dll + ".dll"))
      yield "open Marey"
      yield "open Marey.UnitsOfMeasure" ]
    |> String.concat "\n"

type Checked =
    { Text: string
      Source: ISourceText
      Options: FSharpProjectOptions
      Parse: FSharpParseFileResults
      Check: FSharpCheckFileResults }

    member c.Errors =
        c.Check.Diagnostics |> Array.filter (fun d -> d.Severity = FSharpDiagnosticSeverity.Error)

let private path = Path.Combine(here, "script.fsx")

/// A script's text, header and body, checked.
let checkText (text: string) : Checked =
    let source = SourceText.ofString text

    let options, _ =
        checker.GetProjectOptionsFromScript(path, source, assumeDotNetFramework = false)
        |> Async.RunSynchronously

    let parse, answer = checker.ParseAndCheckFileInProject(path, 0, source, options) |> Async.RunSynchronously

    match answer with
    | FSharpCheckFileAnswer.Succeeded check ->
        { Text = text
          Source = source
          Options = options
          Parse = parse
          Check = check }
    | FSharpCheckFileAnswer.Aborted -> failwith "the check was aborted"

/// A body under the header, checked.
let check (body: string) : Checked = checkText (header + "\n" + body)

/// The text with a fix's edits applied.
let apply (text: string) (fix: Fix) : string =
    let lines = text.Split '\n'
    let starts = lines |> Array.scan (fun acc (l: string) -> acc + l.Length + 1) 0
    let offset (p: pos) = starts.[p.Line - 1] + p.Column

    fix.Edits
    |> List.sortByDescending (fun e -> offset e.Range.Start)
    |> List.fold (fun (t: string) e -> t.Substring(0, offset e.Range.Start) + e.Text + t.Substring(offset e.Range.End)) text

/// The findings for a body.
let findings (body: string) : Checked * Finding list =
    let c = check body
    c, Findings.ofFile c.Parse c.Check

/// The body of a text, the header taken off.
let body (text: string) = text.Substring(header.Length + 1)

let errors (c: Checked) =
    c.Errors |> Array.map (fun d -> sprintf "%A: %s" d.Range d.Message) |> String.concat "\n"
