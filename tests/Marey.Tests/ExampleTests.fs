/// `examples/` is executable, and this is what executes it.
///
/// Each `.fsx` under `examples/` is run **on its own** under `dotnet fsi`, unlike `docs/guide.md`,
/// whose blocks are concatenated into one continuous session by `GuideTests`. That difference is
/// the point: a guide example is a fragment of a narrative and may lean on a binding from six
/// sections earlier, while an example is something a reader downloads and runs. It has to stand
/// up alone, so it is tested alone.
///
/// ### Why this is a test
///
/// The same reason the guide is one. An example that is not executed rots at the first rename, and
/// the person who discovers it is a new user on their first afternoon — the worst possible
/// reviewer to leave the job to. Every example is therefore compiled and run on every
/// `dotnet test`, against the working tree rather than against whatever is on nuget.org.
///
/// ### The rule an example has to follow
///
/// It must open with `#r "nuget: …"` lines, because an example's first job is to show the reader
/// what to reference. Those lines are stripped here and replaced with direct `#r` paths into the
/// test's own output directory, so the example *documents* the published packages while the
/// checker exercises the build in hand. `the examples reference their packages` pins that the
/// convention is actually followed.
module Marey.Tests.ExampleTests

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text
open Xunit

/// The assemblies an example may reference, in dependency order. They sit beside the test binary
/// because the test project references all of them.
let private assemblies =
    [ "Marey.Core"
      "Marey.Units"
      "Marey.Plot"
      "Marey.Backend.Svg"
      "Marey.Backend.Tikz"
      "Marey.Backend.Skia"
      // See the note in `GuideTests`: F# needs SkiaSharp on hand to type-check a construction
      // of `SkiaTextMeasurer`, and only this harness references the dlls directly rather than
      // through the package that would bring it transitively.
      "SkiaSharp"
      "SkiaSharp.HarfBuzz"
      "Marey.Html" ]

let private exampleDir =
    lazy (Path.Combine(Golden.repoRoot.Force(), "examples"))

let private exampleFiles () =
    let dir = exampleDir.Force()

    if Directory.Exists dir then
        Directory.GetFiles(dir, "*.fsx") |> Array.sortBy Path.GetFileName
    else
        [||]

/// The xunit `MemberData` source: one case per example, named by its file so a failure in the
/// runner output names the example rather than an index.
let exampleNames: IEnumerable<obj[]> =
    exampleFiles ()
    |> Array.map (fun p -> [| box (Path.GetFileName p) |])
    :> IEnumerable<obj[]>

[<Fact>]
let ``there are examples, and they are the ones being checked`` () =
    let found = exampleFiles ()

    // A discovery step that silently found nothing would make the theory below vacuously green —
    // xunit reports zero cases as a pass. Pin a floor, exactly as the guide checker does.
    Assert.True(
        found.Length >= 2,
        sprintf "expected at least 2 examples under %s, found %d" (exampleDir.Force()) found.Length
    )

[<Fact>]
let ``the examples reference their packages`` () =
    for file in exampleFiles () do
        let text = File.ReadAllText(file, Encoding.UTF8)

        Assert.True(
            text.Contains "#r \"nuget:",
            sprintf
                "%s has no `#r \"nuget: …\"` line. An example's first job is to show the reader \
                 what to reference; the checker strips these and substitutes the local build, so \
                 carrying them costs nothing here and is the whole documentation value there."
                (Path.GetFileName file)
        )

[<Theory>]
[<MemberData(nameof exampleNames)>]
let ``the example compiles and runs`` (name: string) =
    match TexToolchain.onPath "dotnet" with
    | None ->
        // No SDK on PATH. Say so in the assertion message rather than passing silently.
        Assert.True(true, "dotnet not found on PATH; the examples were not executed")
    | Some dotnet ->
        let source = Path.Combine(exampleDir.Force(), name)
        let binDir = Path.GetDirectoryName(Uri(typeof<Marey.Figure>.Assembly.Location).LocalPath)

        let script = StringBuilder()

        for assembly in assemblies do
            script.AppendLine(sprintf "#r @\"%s\"" (Path.Combine(binDir, assembly + ".dll")))
            |> ignore

        // `#r "nuget: …"` is the reader's line, not the checker's: resolving it would test
        // whatever is published rather than this working tree, and would need a network.
        for line in File.ReadAllText(source, Encoding.UTF8).Replace("\r\n", "\n").Split('\n') do
            if not (line.StartsWith("#r \"nuget:", StringComparison.Ordinal)) then
                script.AppendLine line |> ignore

        // Its own directory per example, because examples write files and two of them writing
        // `wave-packet.svg` into a shared temp would couple tests that have nothing to do with
        // each other. It also means the assertion below can look at what the example produced.
        let workDir =
            Path.Combine(Path.GetTempPath(), "marey-examples", Path.GetFileNameWithoutExtension name)

        Directory.CreateDirectory workDir |> ignore

        // The checked script runs from `workDir`, so its `__SOURCE_DIRECTORY__` is `workDir` and
        // not `examples/`. An example that reads a committed dataset resolves it relative to its
        // own source directory — which is right when a reader runs the file and wrong here — so
        // the data travels with the copy rather than the examples reaching back into the tree.
        let dataSource = Path.Combine(exampleDir.Force(), "data")

        if Directory.Exists dataSource then
            let dataTarget = Path.Combine(workDir, "data")
            Directory.CreateDirectory dataTarget |> ignore

            for file in Directory.GetFiles dataSource do
                File.Copy(file, Path.Combine(dataTarget, Path.GetFileName file), true)

        let scriptPath = Path.Combine(workDir, Path.GetFileNameWithoutExtension name + ".check.fsx")
        File.WriteAllText(scriptPath, script.ToString(), UTF8Encoding false)

        let psi = ProcessStartInfo(dotnet, sprintf "fsi --nologo \"%s\"" scriptPath)
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        // The examples write to the current directory, which is what makes them behave the way a
        // reader expects when run by hand. Point that at the temp directory rather than the repo.
        psi.WorkingDirectory <- workDir

        use proc = Process.Start psi
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        proc.WaitForExit()

        Assert.True(
            proc.ExitCode = 0,
            sprintf
                "examples/%s does not run.\n\nThe checked script is at %s — it is the example with \
                 its `#r \"nuget: …\"` lines replaced, so fsi's line numbers are close to but not \
                 identical with the example's.\n\n--- stdout ---\n%s\n--- stderr ---\n%s"
                name
                scriptPath
                stdout
                stderr
        )

        // An example that runs and says nothing has not shown the reader anything, and would let
        // a figure silently stop being produced without failing here.
        Assert.False(
            String.IsNullOrWhiteSpace stdout,
            sprintf "examples/%s ran but produced no output" name
        )

        // **An example prints a boolean only to demonstrate a claim, so a printed `false` is a
        // failed example.** Exit code alone does not reach this: the first draft of
        // `wave-packet.fsx` asserted that a `V/m` axis would label itself `MV/m`, printed
        // `false`, and exited 0 — a wrong claim about the library, shipped in the file whose job
        // is to teach the library, passing a green suite. The claim was wrong and D18 says so;
        // nothing in the run said so.
        let liars =
            stdout.Replace("\r\n", "\n").Split('\n')
            |> Array.filter (fun l -> l.EndsWith("false", StringComparison.OrdinalIgnoreCase))

        Assert.True(
            liars.Length = 0,
            sprintf
                "examples/%s printed a false claim:\n\n%s\n\nAn example's printed booleans are \
                 assertions about the library aimed at a reader. If one is false, either the \
                 example is wrong or the library changed under it."
                name
                (String.concat "\n" liars)
        )

        // Every example writes at least one figure. This is the assertion that would catch a
        // rendering call quietly removed while the script still exits 0.
        //
        // `.png` and `.pdf` joined the list at M2.5: `shaped-text.fsx` writes nothing else, and
        // a filter that still knew only about the two text formats would have failed it for
        // being a raster example rather than for being wrong.
        let produced =
            Directory.GetFiles(workDir)
            |> Array.filter (fun f ->
                let ext = Path.GetExtension f
                ext = ".svg" || ext = ".tex" || ext = ".png" || ext = ".pdf")

        Assert.True(
            produced.Length > 0,
            sprintf
                "examples/%s wrote no figure into %s. Its stdout was:\n%s"
                name
                workDir
                stdout
        )
