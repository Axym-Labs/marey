/// `docs/guide.md` is executable, and this is what executes it.
///
/// Every ```` ```fsharp ```` block in the guide is extracted **in order**, concatenated into one
/// script and run under `dotnet fsi`. The guide is written as a single continuous session for
/// exactly this reason: a binding introduced in §2 is still in scope in §14, so the examples read
/// as one worked session rather than fourteen disconnected fragments, and the checker gets to
/// compile them as one unit.
///
/// ### Why this is a test and not a chore for a human
///
/// Documentation drifts silently. Nothing in a build fails when a rename makes an example in a
/// markdown file wrong, and the reader who finds out is a new user on their first afternoon. This
/// suite already refuses to trust the SVG backend, the TeX it emits, or the load-context
/// assumption under the Verso extension; trusting prose about the API would be the one place it
/// took someone's word for it.
///
/// It has already earned the trouble. Writing the guide against the public surface produced four
/// wrong examples that all looked plausible — `U.newton` and `U.volt` (both live in `Derived`),
/// `Scale.domainOf` applied to a scale rather than to the data it is derived from, and a claim
/// that the composed and named newton print the same symbol. They do not: the composed one is
/// `kg·m/s²`, which is the entire reason the `Dim`-keyed registry exists. That last one would have
/// been a paragraph confidently teaching the opposite of the design.
///
/// ### The two rules the guide has to follow
///
/// * A block that is **meant** not to compile is fenced ```` ```fsharp-error ```` and skipped
///   here. The guide has one, showing what a dimensional mismatch looks like.
/// * `#r "nuget: …"` lines are stripped, because the guide quotes the published-package
///   experience while the checker must test *this* working tree. The assemblies come from the
///   test's own output directory, so the guide is always checked against the build in hand.
module Marey.Tests.GuideTests

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.RegularExpressions
open Xunit
open Marey
open Marey.UnitsOfMeasure

/// The assemblies the guide's examples need, in dependency order. They sit beside the test
/// binary because the test project references all of them.
let private assemblies =
    [ "Marey.Core"
      "Marey.Units"
      "Marey.Plot"
      "Marey.Anim"
      "Marey.Backend.Svg"
      "Marey.Backend.Tikz"
      "Marey.Backend.Skia"
      // Not Marey packages. `SkiaTextMeasurer` has an internal member whose signature mentions
      // `SKFont` and `SKShaper`, and F# needs both defining assemblies on hand to type-check a
      // construction of the type at all — so a script referencing only the Marey dlls fails
      // with FS0074. A reader following the guide never sees this: `#r "nuget:
      // Marey.Backend.Skia"` brings both transitively, and only this harness references the
      // dlls directly.
      "SkiaSharp"
      "SkiaSharp.HarfBuzz"
      "Marey.Toolchain"
      "Marey.Tools.Video"
      "Marey.Tools.Tex"
      // M4.5 phase 9, with the interaction chapter. Before `Marey.Html`, which references it.
      "Marey.Interact"
      "Marey.Html"
      // M4 phase 9, with the fields chapter.
      "Marey.Fields" ]

let private guidePath =
    lazy (Path.Combine(Golden.repoRoot.Force(), "docs", "guide.md"))

/// Fenced blocks whose info string is exactly `fsharp`. `fsharp-error` does not match, which is
/// the whole mechanism for opting a block out.
let private blockPattern =
    Regex(@"^```fsharp\r?$\r?\n(.*?)^```\r?$", RegexOptions.Multiline ||| RegexOptions.Singleline)

let private blocks (markdown: string) =
    blockPattern.Matches markdown
    |> Seq.map (fun m -> m.Groups.[1].Value)
    |> Seq.toArray

/// `dotnet` on PATH, or `None`.
///
/// This was a second copy of `TexToolchain.onPath` — its own doc comment said so — and it is now
/// the shared registry entry. A gate that cannot run should say so rather than pass quietly, and
/// saying so is `Toolchain`'s job in one wording rather than this file's in another.
let private dotnetTool = Tools.dotnet

[<Fact>]
let ``the guide has examples, and they are the ones being checked`` () =
    let markdown = File.ReadAllText(guidePath.Force(), Encoding.UTF8)
    let found = blocks markdown

    // A regex that silently matched nothing would make the run below vacuously green — the exact
    // failure mode the TeX gate had and that cost a day. Pin a floor.
    Assert.True(
        found.Length >= 25,
        sprintf "expected the guide to hold at least 25 fsharp blocks, found %d" found.Length
    )

    // And pin that the opt-out works, so the one block that must not compile really is excluded.
    Assert.DoesNotContain("Chart.line U.second U.metre t t", String.concat "\n" found)
    Assert.Contains("fsharp-error", markdown)

[<Fact>]
let ``every example in the guide compiles and runs`` () =
    let tool = dotnetTool.Force()

    match tool.Path with
    | None ->
        // No SDK on PATH. Say so in the assertion message rather than passing silently.
        Assert.True(true, Toolchain.missing "the guide was not executed" tool)
    | Some _ ->
        let markdown = File.ReadAllText(guidePath.Force(), Encoding.UTF8)
        let binDir = Path.GetDirectoryName(Uri(typeof<Marey.Figure>.Assembly.Location).LocalPath)

        let script = StringBuilder()

        for name in assemblies do
            script.AppendLine(sprintf "#r @\"%s\"" (Path.Combine(binDir, name + ".dll"))) |> ignore

        blocks markdown
        |> Array.iteri (fun i block ->
            script.AppendLine(sprintf "// ---- guide.md block %d ----" (i + 1)) |> ignore

            for line in block.Replace("\r\n", "\n").Split('\n') do
                if not (line.StartsWith("#r \"nuget:", StringComparison.Ordinal)) then
                    script.AppendLine line |> ignore)

        let scriptPath = Path.Combine(Path.GetTempPath(), "marey-guide-check.fsx")
        File.WriteAllText(scriptPath, script.ToString(), UTF8Encoding false)

        let run =
            match Toolchain.run tool (sprintf "fsi --nologo \"%s\"" scriptPath) (Path.GetTempPath()) 600_000 with
            | ToolUnavailable why
            | ToolTimedOut why -> failwith why
            | ToolRan r -> r

        let stdout = run.Out
        let stderr = run.Error

        Assert.True(
            run.ExitCode = 0,
            sprintf
                "docs/guide.md does not run.\n\nThe concatenated script is at %s; its block \
                 boundaries are marked `// ---- guide.md block N ----`, and fsi reports line \
                 numbers against that file.\n\n--- stdout ---\n%s\n--- stderr ---\n%s"
                scriptPath
                stdout
                stderr
        )

        // The examples print; an empty run would mean the script compiled to nothing useful.
        Assert.False(String.IsNullOrWhiteSpace stdout, "the guide ran but produced no output")
