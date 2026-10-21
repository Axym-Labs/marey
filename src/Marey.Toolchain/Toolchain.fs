namespace Marey

open System
open System.Diagnostics
open System.IO

// ======================================================================================
// EXTERNAL TOOLS: FOUND AND *REPORTED*, OR ABSENT AND SAID SO.
//
// Marey drives three programs it does not ship — `pdflatex` compiles the TikZ goldens,
// `pdftoppm` rasterises the compiled page for the ink gate, and `dotnet fsi` executes every
// example and guide snippet — and two more: `ffmpeg` for video export and a TeX engine for the
// mathematical-typesetting package.
//
// Before this file, "is the tool there?" was answered five times in four files. `onPath` was
// written twice — once in `TexToolchain` and once, in a different spelling, in `GuideTests`,
// whose own comment admitted the copy: "Mirrors `TexToolchain.onPath`". Three call sites then
// phrased the absence differently, so a local run reported the same condition in three
// registers and a reader had to know which file printed which.
//
// Two more consumers were about to be written against no shared thing at all, which is how a
// fifth spelling gets in. This module is the one implementation, and the wording of "it is not
// installed" is part of what it implements.
//
// **The convention.** A tool is *resolved and reported*, or it is absent and the absence names what
// to install. Fonts follow it — "`Matched = None` with a non-empty `Family` means every entry
// missed … the figure looks like nothing the theme asked for, and now says so" — and so does a
// missing native library: a message naming the package, not a `DllNotFoundException` from three
// frames down. An external process is the third instance of one pattern, not a third pattern.
//
// **What is deliberately not here.** The native-library half — the exception-chain walk in
// `SkiaNative` in the Skia backend — is *not* promoted into this module, even though it is
// visibly the same idea. It has exactly one implementation and one consumer, and a facility with
// no second caller is one nobody reads. It moves when a second package needs it, and that is a
// one-file change because the shape is already agreed.
//
// **Zero package dependencies, and this is not `Marey.Core`.** Core is Fable-compatible and
// defends it actively — `Color.fs` hand-rolls a cube root rather than call
// `System.Math.Cbrt` for that reason alone — and a PATH walk and a `Process` are not
// meaningfully Fable-compatible. So this is its own package rather than a file in Core, and it
// takes no `PackageReference`, so nothing it is referenced from acquires a transitive one.
// ======================================================================================

/// An external program Marey drives but does not ship, and where it was found.
///
/// `Install` is carried rather than derived: a caller reading a failure wants something they can
/// act on, and the right sentence differs per tool and per platform. The same reasoning as
/// `SkiaNative.packagesFor`, which names its asset packages rather than inferring them from a RID
/// because "`linux-musl-x64` is not that".
[<NoComparison>]
type ResolvedTool =
    { /// The executable as it was asked for, without any platform suffix.
      Name: string
      /// Full path to the executable, or `None` when nothing on `PATH` matched.
      Path: string option
      /// What to install to make this tool available. Shown whenever the tool is missing.
      Install: string }

    /// Whether the tool was found. Prefer matching on `Path` when the path itself is wanted.
    member t.IsAvailable = Option.isSome t.Path

/// A tool that ran to completion, and what it said.
///
/// Both streams are captured even when a caller wants only one: a tool that fails usually
/// explains itself on the stream the caller was not reading, and discarding it at the point of
/// capture means the explanation cannot be recovered later.
[<NoComparison>]
type ToolRun =
    { ExitCode: int
      Out: string
      Error: string }

/// What happened when a tool was asked to run.
///
/// Three cases rather than a `Result`, because "not installed" and "ran and failed" call for
/// different behaviour at every call site in this repository: the first skips a gate and says so,
/// the second fails it. Collapsing them is how a gate goes quietly vacuous, which is the failure
/// `TexToolchain`'s header was written about.
[<NoComparison>]
type ToolOutcome =
    /// The tool is not installed. Carries the message naming what to install.
    | ToolUnavailable of string
    /// The tool was still running when the timeout elapsed, and was killed.
    | ToolTimedOut of string
    /// The tool exited. Its exit code may still be non-zero.
    | ToolRan of ToolRun

/// Finding external programs on `PATH`, running them, and saying what is missing.
[<RequireQualifiedAccess>]
module Toolchain =

    /// Full path to an executable found on `PATH`, or `None`.
    ///
    /// **Hand-rolled rather than shelled out to `where`/`which`.** Those are themselves external
    /// tools, so their absence would be reported as the absence of the thing being looked for —
    /// a failure that reads as "pdflatex is not installed" on a machine where it is. This is
    /// `TexToolchain.onPath` verbatim in behaviour, promoted here and deduplicated against the
    /// second copy that had grown in `GuideTests`.
    let onPath (name: string) : string option =
        let exe =
            if Environment.OSVersion.Platform = PlatformID.Win32NT then
                name + ".exe"
            else
                name

        let dirs =
            (Environment.GetEnvironmentVariable "PATH"
             |> Option.ofObj
             |> Option.defaultValue "")
                .Split(Path.PathSeparator)

        dirs
        |> Array.tryPick (fun d ->
            if String.IsNullOrWhiteSpace d then
                None
            else
                let candidate =
                    try
                        Some(Path.Combine(d.Trim(), exe))
                    with _ ->
                        // A PATH entry can contain characters `Path.Combine` rejects. That is a
                        // bad entry, not a missing tool, so skip it rather than fail the search.
                        None

                match candidate with
                | Some p when File.Exists p -> Some p
                | _ -> None)

    /// Look for a tool on `PATH`, recording what to install if it is not there.
    ///
    /// Cheap enough to call directly, but every consumer in the repository wraps it in a `Lazy`
    /// so a suite of tests walks `PATH` once rather than once per test.
    let resolve (install: string) (name: string) : ResolvedTool =
        { Name = name
          Path = onPath name
          Install = install }

    /// The message for a tool that is not installed.
    ///
    /// One wording, in one place, so the same condition reads the same way whichever gate hit it.
    /// `consequence` is the caller's half — what did not happen because the tool is missing —
    /// because that is the part the reader of a test log actually needs and it is the part that
    /// legitimately differs.
    let missing (consequence: string) (tool: ResolvedTool) : string =
        sprintf "%s not found on PATH — %s. To enable it: %s" tool.Name consequence tool.Install

    /// A human-readable line saying what was found, for a test run's output or a diagnostic.
    ///
    /// Printed whether or not the tool is present, because "the gate ran" and "the gate was
    /// skipped" are equally worth knowing and only one of them is visible from a green tick.
    /// `TexToolchain`'s header states the rule this serves: read the test output rather than the
    /// green tick.
    let report (consequence: string) (tool: ResolvedTool) : string =
        match tool.Path with
        | Some p -> sprintf "%s found at %s" tool.Name p
        | None -> missing consequence tool

    /// The body `run` and `runArgs` share.
    ///
    /// `setArguments` writes exactly one of `ProcessStartInfo.Arguments` and
    /// `ProcessStartInfo.ArgumentList`: setting both throws, and which one a caller wants is the
    /// only difference between the two entry points below.
    let private runCore
        (tool: ResolvedTool)
        (setArguments: ProcessStartInfo -> unit)
        (workingDirectory: string)
        (timeoutMs: int)
        : ToolOutcome =
        match tool.Path with
        | None -> ToolUnavailable(missing "it was not run" tool)
        | Some exe ->
            let psi =
                ProcessStartInfo(
                    FileName = exe,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                )

            setArguments psi

            // **`onPath` proves a file exists, not that it can be executed.** A directory named
            // `ffmpeg`, or a file without the execute bit, satisfies `File.Exists` and then
            // throws here — `Win32Exception` on Unix, and it surfaces as a stack trace rather
            // than as the sentence naming what to install. A broken installation is reported as
            // unavailable, with the path, because that is what the reader has to go and fix.
            let started =
                try
                    Ok(Process.Start psi)
                with e ->
                    Error(sprintf "%s at %s could not be started: %s. To reinstall it: %s" tool.Name exe e.Message tool.Install)

            match started with
            | Error why -> ToolUnavailable why
            | Ok p when isNull p -> ToolUnavailable(sprintf "%s at %s started no process." tool.Name exe)
            | Ok p ->
                use p = p

                // **Both reads are started before either is awaited, and that ordering is the
                // whole point.** Reading one stream to the end and *then* the other deadlocks
                // whenever the child fills the second pipe's buffer: the child blocks writing
                // stderr, so it never closes stdout, so the parent blocks forever on a read that
                // can no longer finish. It survives every small output and fails on the large
                // one — which is to say, on the run that finally had enough errors to be worth
                // reading. Four call sites in this repository were written the sequential way.
                let out = p.StandardOutput.ReadToEndAsync()
                let err = p.StandardError.ReadToEndAsync()

                if not (p.WaitForExit timeoutMs) then
                    // The process is killed rather than left running. A `pdflatex` stopped at an
                    // interactive prompt never returns, and an orphan holding a temporary
                    // directory open makes the *next* test fail for a reason of its own.
                    try
                        p.Kill()
                    with _ ->
                        ()

                    ToolTimedOut(sprintf "%s did not terminate within %d ms" tool.Name timeoutMs)
                else
                    // The process has exited, so both pipes are closed and both reads are either
                    // complete or about to be. Awaiting them here cannot block for long, and it
                    // is what guarantees no trailing output is lost.
                    ToolRan
                        { ExitCode = p.ExitCode
                          Out = out.Result
                          Error = err.Result }

    /// Run a tool to completion, capturing both streams.
    ///
    /// Returns `ToolUnavailable` rather than throwing when the tool is missing, so a caller can
    /// decide between skipping and failing — the two behaviours this repository actually wants,
    /// and they differ per gate.
    ///
    /// Both streams are drained concurrently and the process is killed on timeout. See the
    /// comments in `runCore`: each of those is a bug that exists in the call sites this replaces.
    ///
    /// **`arguments` is one string, and .NET splits it back apart.** Prefer `runArgs` whenever an
    /// argument is a path.
    let run (tool: ResolvedTool) (arguments: string) (workingDirectory: string) (timeoutMs: int) : ToolOutcome =
        runCore tool (fun psi -> psi.Arguments <- arguments) workingDirectory timeoutMs

    /// `run`, with the argument vector handed over as a list rather than as one joined string.
    ///
    /// **Prefer this whenever an argument is a path or comes from a caller.**
    /// `ProcessStartInfo.Arguments` is a single string that .NET re-splits using *Windows'*
    /// quoting rules on every platform, so the caller owns the quoting — and the rule that
    /// catches people is that a backslash immediately before the closing quote escapes the
    /// quote rather than itself, which makes `"C:\out\"` a parse error rather than a directory.
    /// A vector is handed over unsplit and there is nothing to quote.
    ///
    /// `run` stays for the caller assembling a fixed string of flags, which is every call site
    /// that existed before this one and has no path in it.
    let runArgs (tool: ResolvedTool) (arguments: string seq) (workingDirectory: string) (timeoutMs: int) : ToolOutcome =
        runCore
            tool
            (fun psi ->
                for a in arguments do
                    psi.ArgumentList.Add a)
            workingDirectory
            timeoutMs

    /// The command as a person would type it, for a log, a failure message or a bug report.
    ///
    /// **Display only, and deliberately not round-trippable.** The quoting here is good enough to
    /// paste into a shell and not good enough to hand back to `run` — feeding it back is exactly
    /// the mistake `runArgs` exists to make unnecessary, so nothing in this module consumes what
    /// this returns.
    let commandLine (tool: ResolvedTool) (arguments: string seq) : string =
        let show (a: string) =
            if a.Length = 0 || a |> Seq.exists (fun c -> Char.IsWhiteSpace c || c = '"' || c = '\'') then
                "\"" + a.Replace("\"", "\\\"") + "\""
            else
                a

        Seq.append (Seq.singleton (show (defaultArg tool.Path tool.Name))) (Seq.map show arguments)
        |> String.concat " "
