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
      "Marey.Fields"
      // M5.5 phase 12, with the simulation chapter. Before the GPU backend since M5.6, whose
      // solvers step its states.
      "Marey.Sim"
      // M5 phase 10, with the GPU and window chapter.
      "Marey.Backend.Gpu" ]

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

/// Blocks fenced ```` ```fsharp optional ````, in the unit-optional mode. They are a second
/// session, run in order like the first, because the first opens `Marey.UnitsOfMeasure` in §1 and
/// under that `open` a unit-optional constructor is a compile error by design. GitHub highlights
/// the info string by its first word, so a reader sees F#.
let private optionalPattern =
    Regex(@"^```fsharp optional\r?$\r?\n(.*?)^```\r?$", RegexOptions.Multiline ||| RegexOptions.Singleline)

let private optionalBlocks (markdown: string) =
    optionalPattern.Matches markdown
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

/// Run one session under `dotnet fsi`: the assemblies, a prelude, then the blocks in order. `source`
/// names the session in the failure message and the script's file name.
let private runBlocks (source: string) (name: string) (prelude: string) (workDir: string) (blocks: string[]) =
    let tool = dotnetTool.Force()

    match tool.Path with
    | None ->
        // No SDK on PATH. Say so in the assertion message rather than passing silently.
        Assert.True(true, Toolchain.missing "the guide was not executed" tool)
    | Some _ ->
        let binDir = Path.GetDirectoryName(Uri(typeof<Marey.Figure>.Assembly.Location).LocalPath)

        let script = StringBuilder()

        for name in assemblies do
            script.AppendLine(sprintf "#r @\"%s\"" (Path.Combine(binDir, name + ".dll"))) |> ignore

        script.AppendLine prelude |> ignore

        blocks
        |> Array.iteri (fun i block ->
            script.AppendLine(sprintf "// ---- %s block %d ----" source (i + 1)) |> ignore

            for line in block.Replace("\r\n", "\n").Split('\n') do
                if not (line.StartsWith("#r \"nuget:", StringComparison.Ordinal)) then
                    script.AppendLine line |> ignore)

        let scriptPath = Path.Combine(Path.GetTempPath(), sprintf "marey-guide-check%s.fsx" name)
        File.WriteAllText(scriptPath, script.ToString(), UTF8Encoding false)

        let run =
            match Toolchain.run tool (sprintf "fsi --nologo \"%s\"" scriptPath) workDir 600_000 with
            | ToolUnavailable why
            | ToolTimedOut why -> failwith why
            | ToolRan r -> r

        let stdout = run.Out
        let stderr = run.Error

        Assert.True(
            run.ExitCode = 0,
            sprintf
                "%s does not run.\n\nThe concatenated script is at %s; its block \
                 boundaries are marked `// ---- %s block N ----`, and fsi reports line \
                 numbers against that file.\n\n--- stdout ---\n%s\n--- stderr ---\n%s"
                source
                scriptPath
                source
                stdout
                stderr
        )

        // The examples print; an empty run would mean the script compiled to nothing useful.
        Assert.False(String.IsNullOrWhiteSpace stdout, "the guide ran but produced no output")

/// Run one session of `docs/guide.md`.
let private runSession (name: string) (pick: string -> string[]) =
    let markdown = File.ReadAllText(guidePath.Force(), Encoding.UTF8)
    runBlocks "guide.md" name "" (Path.GetTempPath()) (pick markdown)

[<Fact>]
let ``every example in the guide compiles and runs`` () = runSession "" blocks

[<Fact>]
let ``the unit-optional examples are there, and checked apart`` () =
    let markdown = File.ReadAllText(guidePath.Force(), Encoding.UTF8)
    let found = optionalBlocks markdown
    Assert.True(found.Length >= 3, sprintf "expected at least 3 `fsharp optional` blocks, found %d" found.Length)
    // Not swept into the first session, where they would fail by design.
    Assert.DoesNotContain(found.[0], String.concat "\n" (blocks markdown))

[<Fact>]
let ``every unit-optional example in the guide compiles and runs`` () = runSession "-optional" optionalBlocks

// ======================================================================================
// The tour: `docs/guide/`, one file per section
// ======================================================================================

/// The guide as a tour, one page per section, read in file-name order and run as the one session
/// `guide.md` is. A block whose fence says ```` ```fsharp output=dipole ```` ends on the value it
/// shows, a figure or a scene; the run draws it to `docs/figures/dipole.svg`, and the page shows
/// that file under the block. The figures are committed, so the guide reads on GitHub with its
/// pictures, and a figure that no longer matches what its block draws fails here, as a golden
/// does. `MAREY_UPDATE_GOLDEN=1` accepts the new drawing. `size=640x400` on the fence sets its
/// size; 720 × 450 otherwise.
module ``the tour`` =

    let private guideDir = lazy (Path.Combine(Golden.repoRoot.Force(), "docs", "guide"))

    let figuresDir = lazy (Path.Combine(Golden.repoRoot.Force(), "docs", "figures"))

    /// Each page, by file name, in reading order.
    let pages () =
        Directory.GetFiles(guideDir.Force(), "*.md")
        |> Array.sort
        |> Array.map (fun p -> Path.GetFileName p, File.ReadAllText(p, Encoding.UTF8).Replace("\r\n", "\n"))

    /// A fenced F# block and what its fence says about it.
    type Block =
        { Code: string
          Optional: bool
          Output: string option
          Size: float * float }

    let private fence =
        Regex(@"^```fsharp((?: [^\n]*)?)\n(.*?)^```$", RegexOptions.Multiline ||| RegexOptions.Singleline)

    let private outputAttr = Regex(@"\boutput=([A-Za-z0-9-]+)")
    let private sizeAttr = Regex(@"\bsize=(\d+)x(\d+)")

    /// The blocks of a page, in order. `fsharp-error` does not match the fence, so a block meant
    /// not to compile is left out as in `guide.md`.
    let blocksOf (markdown: string) =
        [ for m in fence.Matches markdown do
              let attrs = m.Groups.[1].Value
              let output = outputAttr.Match attrs
              let size = sizeAttr.Match attrs

              { Code = m.Groups.[2].Value
                Optional = Regex.IsMatch(attrs, @"\boptional\b")
                Output = if output.Success then Some output.Groups.[1].Value else None
                Size =
                  if size.Success then
                      float size.Groups.[1].Value, float size.Groups.[2].Value
                  else
                      720.0, 450.0 } ]

    /// Where the value a block ends on begins: its last line at the left margin that does not
    /// continue an expression above it.
    let private continues (line: string) =
        line = ""
        || Char.IsWhiteSpace line.[0]
        || [ "|>"; ">>"; "//"; ")"; "]"; "}" ] |> List.exists (fun p -> line.StartsWith p)

    /// The block with the value it ends on bound and drawn into `dir`.
    let captured (dir: string) (index: int) (b: Block) =
        match b.Output with
        | None -> b.Code
        | Some name ->
            let lines = b.Code.TrimEnd().Split('\n')

            let start =
                lines
                |> Array.tryFindIndexBack (fun l -> not (continues l))
                |> Option.defaultWith (fun () -> failwithf "output=%s: the block ends on no value" name)

            let head = lines.[.. start - 1]
            let value = lines.[start..] |> Array.map (fun l -> "    " + l)
            let w, h = b.Size

            String.concat
                "\n"
                [ yield! head
                  sprintf "let guideOutput%d =" index
                  yield! value
                  sprintf "GuideOutput.write @\"%s\" \"%s\" %g %g (box guideOutput%d)" dir name w h index ]

    /// What draws a block's value. Defined in the script, ahead of the first block.
    let private prelude =
        """
module GuideOutput =
    let write (dir: string) (name: string) (w: float) (h: float) (value: obj) =
        let path = System.IO.Path.Combine(dir, name + ".svg")

        match value with
        | :? Marey.Figure as f -> Marey.Chart.toSvg Marey.SvgBackend.render path (Marey.Chart.size w h) f
        | :? Marey.Node as n -> System.IO.File.WriteAllText(path, Marey.SvgBackend.render (Marey.Chart.size w h) n)
        | v -> failwithf "output=%s is a %s, which the guide does not draw" name (v.GetType().Name)
"""

    let private session (optional: bool) (dir: string) =
        pages ()
        |> Array.collect (fun (_, md) -> blocksOf md |> List.filter (fun b -> b.Optional = optional) |> Array.ofList)
        |> Array.mapi (fun i b -> captured dir i b)

    [<Fact>]
    let ``the tour has pages, and each output is shown on its page`` () =
        let ps = pages ()
        Assert.True(ps.Length >= 1, "docs/guide/ has no pages")

        for file, md in ps do
            for b in blocksOf md do
                b.Output
                |> Option.iter (fun name ->
                    Assert.True(
                        md.Contains(sprintf "figures/%s.svg" name),
                        sprintf "%s draws %s.svg and does not show it" file name
                    ))

    /// A working directory for a session, holding a copy of `examples/data`, which the guide
    /// reads by that path as a reader in the repository would.
    let private workDir (name: string) =
        let dir = Path.Combine(Path.GetTempPath(), sprintf "marey-%s-%d" name (Diagnostics.Process.GetCurrentProcess().Id))
        let data = Path.Combine(dir, "examples", "data")
        Directory.CreateDirectory data |> ignore

        for file in Directory.GetFiles(Path.Combine(Golden.repoRoot.Force(), "examples", "data")) do
            File.Copy(file, Path.Combine(data, Path.GetFileName file), true)

        dir

    [<Fact>]
    let ``every example in the tour runs, and draws the figures it shows`` () =
        let work = workDir "tour"
        let dir = Path.Combine(work, "figures")
        Directory.CreateDirectory dir |> ignore

        try
            runBlocks "docs/guide" "-tour" prelude work (session false dir)

            let drawn = Directory.GetFiles(dir, "*.svg") |> Array.sort
            let committed = figuresDir.Force()
            Directory.CreateDirectory committed |> ignore
            let normalise (s: string) = s.Replace("\r\n", "\n")

            let stale =
                [ for file in drawn do
                      let target = Path.Combine(committed, Path.GetFileName file)
                      let actual = normalise (File.ReadAllText file)

                      if Golden.updateRequested then
                          File.WriteAllText(target, actual, UTF8Encoding false)
                      elif not (File.Exists target) || normalise (File.ReadAllText target) <> actual then
                          File.Copy(file, Path.ChangeExtension(target, ".actual.svg"), true)
                          yield Path.GetFileName file ]

            Assert.True(
                List.isEmpty stale,
                sprintf
                    "docs/figures/ does not show what the guide draws: %s. Each new drawing is beside it as \
                     .actual.svg; re-run with %s=1 to accept them."
                    (String.concat ", " stale)
                    Golden.UpdateVariable
            )
        finally
            try Directory.Delete(work, true) with _ -> ()

    [<Fact>]
    let ``every unit-optional example in the tour runs`` () =
        let work = workDir "tour-optional"

        try
            let blocks = session true work

            if blocks.Length > 0 then
                runBlocks "docs/guide" "-tour-optional" prelude work blocks
        finally
            try Directory.Delete(work, true) with _ -> ()
