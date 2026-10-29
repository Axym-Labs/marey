/// The analyzers as FSharp.Analyzers.SDK runs them.
module Marey.Analyzers.Tests.AnalyzerTests

open Xunit
open FSharp.Analyzers.SDK
open Marey.Analyzers.Tests.Scripts

/// The CLI's context for a checked script. Built here rather than by the SDK's test helper, which
/// refuses a source that does not compile, and every finding is on one.
let private contextOf (c: Checked) : CliContext =
    let project = checker.ParseAndCheckProject c.Options |> Async.RunSynchronously

    { FileName = c.Parse.FileName
      SourceText = c.Source
      ParseFileResults = c.Parse
      CheckFileResults = c.Check
      TypedTree = None
      CheckProjectResults = project
      ProjectOptions = AnalyzerProjectOptions.BackgroundCompilerOptions c.Options
      AnalyzerIgnoreRanges = Map.empty }

[<Fact>]
let ``the CLI analyzer reports the finding with each fix as one replacement`` () =
    let c = check "let t = [| 0.0<s>; 1.0<s> |]\nlet fig = Chart.lineIn t t\n"
    let messages = Marey.Analyzers.Witnesses.cli (contextOf c) |> Async.RunSynchronously

    match messages with
    | [ m ] ->
        Assert.Equal("MAREY001", m.Code)

        match m.Fixes with
        | [ fix ] ->
            // An insertion: the empty range in front of `t`.
            Assert.Equal("", fix.FromText)
            Assert.Equal("U.second U.second ", fix.ToText)
            Assert.Equal((2, 23), (fix.FromRange.StartLine - 6, fix.FromRange.StartColumn))
        | fixes -> failwithf "expected one fix, got %A" fixes
    | ms -> failwithf "expected one message, got %A" ms
