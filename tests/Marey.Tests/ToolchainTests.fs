/// `Marey.Toolchain` — the one implementation of "is the tool there, and what happened when it
/// ran".
///
/// Five gates in this suite shell out, and before this module each answered that question for
/// itself. Consolidating them means one implementation carries every one of those gates, so it is
/// worth more tests than any single call site would have justified — and two of the tests below
/// are for bugs that were live in all five copies.
module Marey.Tests.ToolchainTests

open System
open System.IO
open System.Text
open Xunit
open Marey

// ======================================================================================
// RESOLUTION
// ======================================================================================

[<Fact>]
let ``a tool that exists is found, with a usable path`` () =
    let tool = Tools.dotnet.Force()

    // The suite is running under `dotnet test`, so the SDK is on PATH by construction. If this
    // ever fails, the PATH walk is broken rather than the environment.
    Assert.True(tool.IsAvailable, "dotnet is not on PATH, but this suite is running under it")

    match tool.Path with
    | None -> failwith "unreachable: IsAvailable was true"
    | Some p ->
        Assert.True(File.Exists p, sprintf "resolved %s to '%s', which does not exist" tool.Name p)
        Assert.True(Path.IsPathRooted p, sprintf "resolved to '%s', which is not an absolute path" p)

[<Fact>]
let ``a tool that does not exist is absent rather than throwing`` () =
    let name = "marey-no-such-tool-" + Guid.NewGuid().ToString("N")
    let tool = Toolchain.resolve "nothing; this tool is fictional" name

    Assert.False(tool.IsAvailable)
    Assert.Equal(None, tool.Path)

[<Fact>]
let ``empty and nonexistent PATH entries are skipped rather than ending the search`` () =
    // A trailing separator is the common real case — `PATH=/usr/bin:` produces an empty entry —
    // and a directory listed but never created is the next most common. Either one ending the
    // search would make every tool after it look uninstalled, which is the failure that reads as
    // "pdflatex is not installed" on a machine where it is.
    let original = Environment.GetEnvironmentVariable "PATH"
    let real = Path.GetDirectoryName (Tools.dotnet.Force()).Path.Value
    let sep = string Path.PathSeparator

    try
        Environment.SetEnvironmentVariable(
            "PATH",
            String.concat sep [ ""; "   "; Path.Combine(Path.GetTempPath(), "marey-no-such-dir"); real ]
        )

        match Toolchain.onPath "dotnet" with
        | Some p -> Assert.True(File.Exists p, sprintf "resolved to '%s', which does not exist" p)
        | None -> failwith "empty, blank or nonexistent PATH entries hid a tool that is installed"
    finally
        Environment.SetEnvironmentVariable("PATH", original)

// ======================================================================================
// WORDING
//
// The point of one implementation is one wording. These pin that the message a reader of a
// failing run sees actually carries the two things they need: what is missing, and what to
// install.
// ======================================================================================

[<Fact>]
let ``the absence message names the tool, the consequence and the remedy`` () =
    let tool = Toolchain.resolve "apt install marey-fictional" "marey-fictional"
    let message = Toolchain.missing "the gate did not run" tool

    Assert.Contains("marey-fictional", message)
    Assert.Contains("the gate did not run", message)
    Assert.Contains("apt install marey-fictional", message)

[<Fact>]
let ``report says where a present tool was found, and falls back to the absence message`` () =
    let present = Tools.dotnet.Force()
    let reported = Toolchain.report "the gate did not run" present
    Assert.Contains(present.Path.Value, reported)

    let absent = Toolchain.resolve "apt install marey-fictional" "marey-fictional"
    Assert.Equal(Toolchain.missing "the gate did not run" absent, Toolchain.report "the gate did not run" absent)

// ======================================================================================
// RUNNING
// ======================================================================================

[<Fact>]
let ``running a missing tool reports it unavailable rather than throwing`` () =
    let tool = Toolchain.resolve "nothing; fictional" ("marey-missing-" + Guid.NewGuid().ToString("N"))

    match Toolchain.run tool "--version" (Path.GetTempPath()) 10_000 with
    | ToolUnavailable why -> Assert.Contains(tool.Name, why)
    | other -> failwithf "expected ToolUnavailable, got %A" other

[<Fact>]
let ``a file on PATH that cannot be executed is unavailable, not an unhandled exception`` () =
    // `onPath` proves a file exists, not that it can be run. A directory satisfies `File.Exists`
    // nowhere, but a plain non-executable file does — and `Process.Start` then throws. That was
    // an unhandled exception in every call site this module replaced.
    let dir = Path.Combine(Path.GetTempPath(), "marey-toolchain-" + Guid.NewGuid().ToString("N").Substring(0, 8))
    Directory.CreateDirectory dir |> ignore

    try
        let name = "marey-not-executable"
        let file = Path.Combine(dir, name)
        File.WriteAllText(file, "this is not a program\n", UTF8Encoding false)

        let tool =
            { Name = name
              Path = Some file
              Install = "nothing; this file is deliberately not a program" }

        // Either the OS refuses to start it (the usual case) or it starts and fails. Both are
        // acceptable; an escaping exception is not, and that is what this asserts.
        match Toolchain.run tool "" dir 10_000 with
        | ToolUnavailable why -> Assert.Contains(name, why)
        | ToolRan run -> Assert.NotEqual(0, run.ExitCode)
        | ToolTimedOut why -> failwithf "a non-program should not time out: %s" why
    finally
        try
            Directory.Delete(dir, true)
        with _ ->
            ()

[<Fact>]
let ``a run captures the exit code and both streams`` () =
    let tool = Tools.dotnet.Force()

    match Toolchain.run tool "--version" (Path.GetTempPath()) 60_000 with
    | ToolRan run ->
        Assert.Equal(0, run.ExitCode)
        Assert.False(String.IsNullOrWhiteSpace run.Out, "dotnet --version printed nothing")
    | other -> failwithf "expected ToolRan, got %A" other

[<Fact>]
let ``a failing run keeps the stream that explains it`` () =
    let tool = Tools.dotnet.Force()

    // A subcommand that does not exist: non-zero exit, and the explanation is on one of the two
    // streams. Which one is the SDK's business and has moved between versions — which is exactly
    // why `ToolRun` captures both rather than the one the caller expected.
    match Toolchain.run tool "marey-no-such-command" (Path.GetTempPath()) 60_000 with
    | ToolRan run ->
        Assert.NotEqual(0, run.ExitCode)
        Assert.False(
            String.IsNullOrWhiteSpace run.Out && String.IsNullOrWhiteSpace run.Error,
            "a failing tool explained itself on neither stream, so the explanation was dropped"
        )
    | other -> failwithf "expected ToolRan, got %A" other

// ======================================================================================
// THE DEADLOCK
//
// This is the test worth having. Reading one stream to the end and *then* the other deadlocks
// whenever the child fills the second pipe's buffer: the child blocks writing stderr, so it
// never closes stdout, so the parent blocks on a read that can no longer complete. All five call
// sites this module replaced were written that way, and every one of them survived only because
// no tool had yet produced enough output to fill a pipe — roughly 64 KiB on Linux.
//
// The child below writes 128 KiB to stderr *first*, then 128 KiB to stdout, which makes the
// failure deterministic rather than a race. Under the sequential implementation this returns
// `ToolTimedOut`; under the concurrent one it returns both payloads intact.
// ======================================================================================

[<Fact>]
let ``a tool that fills both pipes does not deadlock`` () =
    let tool = Tools.dotnet.Force()

    if not tool.IsAvailable then
        Assert.True(true, Toolchain.missing "the deadlock gate did not run" tool)
    else
        let dir = Path.Combine(Path.GetTempPath(), "marey-pipes-" + Guid.NewGuid().ToString("N").Substring(0, 8))
        Directory.CreateDirectory dir |> ignore

        try
            let script = Path.Combine(dir, "pipes.fsx")

            File.WriteAllText(
                script,
                String.concat
                    "\n"
                    [ "let block = System.String('x', 1024)"
                      // stderr first, so the child is already blocked on a full stderr buffer
                      // before it writes a byte of the stream a sequential reader is waiting on.
                      "for _ in 1 .. 128 do eprintfn \"%s\" block"
                      "for _ in 1 .. 128 do printfn \"%s\" block"
                      "" ],
                UTF8Encoding false
            )

            // The timeout is what turns a regression into a failing test rather than a hung run.
            match Toolchain.run tool (sprintf "fsi --nologo \"%s\"" script) dir 120_000 with
            | ToolTimedOut why ->
                failwithf "the pipes deadlocked: %s -- both streams must be drained concurrently" why
            | ToolUnavailable why -> Assert.True(true, why)
            | ToolRan run ->
                Assert.Equal(0, run.ExitCode)
                // Both payloads arrive whole. A truncated capture would mean the reads were
                // abandoned when the process exited rather than awaited.
                Assert.True(run.Out.Length >= 128 * 1024, sprintf "stdout truncated to %d bytes" run.Out.Length)
                Assert.True(run.Error.Length >= 128 * 1024, sprintf "stderr truncated to %d bytes" run.Error.Length)
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()
