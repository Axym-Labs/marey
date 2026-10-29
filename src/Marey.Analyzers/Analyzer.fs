/// `Marey.Witnesses`' findings as FSharp.Analyzers.SDK messages, each fix one replacement.
module Marey.Analyzers.Witnesses

open FSharp.Analyzers.SDK
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open Marey.Witnesses

[<Literal>]
let private help = "https://axym-labs.github.io/marey/guide/02-units.html"

let private messages (source: ISourceText) (parse: FSharpParseFileResults) (check: FSharpCheckFileResults) : Message list =
    Findings.ofFile parse check
    |> List.map (fun f ->
        { Type = "Marey witnesses"
          Message = f.Message
          Code = f.Code
          // Each is a call the compiler already refuses; the message is there for its fixes.
          Severity = Severity.Info
          Range = f.Range
          Fixes =
            f.Fixes
            |> List.map (fun fix ->
                let e = Findings.collapse source fix

                { FromRange = e.Range
                  FromText = Findings.textOf source e.Range
                  ToText = e.Text }) })

[<Literal>]
let private name = "MareyWitnesses"

[<Literal>]
let private description =
    "Fixes a Marey call that does not compile for want of the right unit witness: inserts the witnesses a call is missing, replaces one that names another unit than its data, and makes measured arrays carry theirs where a Q is taken."

/// The CLI's analyzer.
[<CliAnalyzer(name, description, help)>]
let cli: Analyzer<CliContext> =
    fun ctx -> async { return messages ctx.SourceText ctx.ParseFileResults ctx.CheckFileResults }

/// The editor's analyzer.
[<EditorAnalyzer(name, description, help)>]
let editor: Analyzer<EditorContext> =
    fun ctx ->
        async {
            return
                match ctx.CheckFileResults with
                | Some check -> messages ctx.SourceText ctx.ParseFileResults check
                | None -> []
        }
