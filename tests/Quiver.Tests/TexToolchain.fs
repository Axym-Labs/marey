/// Driving a real TeX installation from the test suite.
///
/// The SVG backend has an oracle that costs nothing: `XDocument.Parse` says whether the output is
/// well-formed, and the golden suite uses it. A TeX backend has no such thing in .NET — the only
/// authority on whether a `.tex` file is valid is a TeX engine — so this module runs one.
///
/// **This is the most valuable gate in the TikZ work.** A backend that emits plausible-looking
/// broken TeX is worse than no backend, because the failure surfaces in the user's document
/// rather than in our test run, with an error message about a control sequence they never wrote.
///
/// ### When TeX is absent
///
/// xUnit 2.x has no dynamic skip, so a test that needs an engine and cannot find one returns
/// without asserting. That is a real hole, and it is closed from the other side: CI installs a
/// TeX distribution, so the gate is enforced where enforcement counts, and `report` prints what
/// was found so a local run says plainly whether the gate ran or was silently vacuous. Read the
/// test output rather than the green tick.
module Quiver.Tests.TexToolchain

open System
open System.Diagnostics
open System.IO
open System.Text

/// The engine to compile with. `pdflatex` rather than `lualatex` or `xelatex` because it is the
/// strictest of the three about Unicode input, so a document that satisfies it satisfies all
/// three — which is the direction a backend wants its test to run.
[<Literal>]
let Engine = "pdflatex"

/// Full path to the engine, or `None` when it is not installed.
let enginePath: Lazy<string option> =
    lazy
        (let exe =
            if Environment.OSVersion.Platform = PlatformID.Win32NT then
                Engine + ".exe"
            else
                Engine

         let dirs =
             (Environment.GetEnvironmentVariable "PATH" |> Option.ofObj |> Option.defaultValue "")
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
                         None

                 match candidate with
                 | Some p when File.Exists p -> Some p
                 | _ -> None))

let available = lazy (enginePath.Force() |> Option.isSome)

/// The result of asking TeX to read a document.
type CompileResult =
    /// The engine is not installed; nothing was checked.
    | Unavailable
    /// The engine read the document and reported no errors.
    | Compiled
    /// The engine reported errors. Carries the error lines from the log.
    | Failed of string

/// The preamble a Quiver TikZ picture needs.
///
/// `textcomp` has been part of the LaTeX kernel since 2020 and loading it is a no-op on a modern
/// distribution, but it is cheap insurance for an older one and it documents which macros the
/// emitter relies on. `siunitx` is what makes `UnitStyle.Siunitx` output typeset.
[<Literal>]
let Preamble =
    "\\usepackage{tikz}\n\\usepackage{textcomp}\n\\usepackage{siunitx}\n"

/// Wrap a fragment in a minimal standalone document.
///
/// `standalone` crops to the content, which means an overfull picture is visible as a huge page
/// rather than silently clipped to A4.
let standalone (fragment: string) : string =
    String.Concat
        [| "\\documentclass[border=2bp]{standalone}\n"
           Preamble
           "\\begin{document}\n"
           fragment
           "\n\\end{document}\n" |]

/// Wrap body text in a `tikzpicture` node — for checking that escaped *text* typesets, without
/// needing a whole scene.
let textFragment (body: string) : string =
    "\\begin{tikzpicture}\n\\node[anchor=base west] at (0bp,0bp) {" + body + "};\n\\end{tikzpicture}"

/// TeX reports an error as a line beginning `! `.
///
/// **Do not pass `-file-line-error` to the engine.** It rewrites errors as
/// `./doc.tex:5: Undefined control sequence.`, which does not start with `! ` — so this filter
/// finds nothing, every document is declared clean, and the whole gate becomes theatre. That
/// exact mistake was made here and was caught only by the deliberately-broken document in
/// `TikzWriterTests`, which is why that test exists.
let private errorLines (log: string) =
    log.Split '\n'
    |> Array.map (fun l -> l.TrimEnd '\r')
    |> Array.filter (fun l -> l.StartsWith("! ", StringComparison.Ordinal))

/// Compile a complete LaTeX document and report whether the engine accepted it.
///
/// Runs in a fresh temporary directory so concurrent tests cannot collide over `.aux` files, and
/// deletes it afterwards. UTF-8 without a BOM, because that is what the backend writes.
let compile (name: string) (document: string) : CompileResult =
    match enginePath.Force() with
    | None -> Unavailable
    | Some exe ->
        let dir =
            Path.Combine(Path.GetTempPath(), "quiver-tex-" + Guid.NewGuid().ToString("N").Substring(0, 12))

        Directory.CreateDirectory dir |> ignore

        try
            let stem =
                let safe = String(name |> Seq.filter Char.IsLetterOrDigit |> Seq.toArray)
                if safe = "" then "doc" else safe

            let texFile = Path.Combine(dir, stem + ".tex")
            File.WriteAllText(texFile, document, UTF8Encoding false)

            let psi =
                ProcessStartInfo(
                    FileName = exe,
                    // Two deliberate omissions. `-halt-on-error` is not passed because we want
                    // every error the engine finds, so one failure report is actionable in one
                    // pass. `-file-line-error` is not passed because it rewrites errors into a
                    // form `errorLines` cannot see — see the note there.
                    Arguments = "-interaction=nonstopmode " + stem + ".tex",
                    WorkingDirectory = dir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                )

            use p = Process.Start psi
            let stdout = p.StandardOutput.ReadToEnd()
            p.StandardError.ReadToEnd() |> ignore

            if not (p.WaitForExit 120_000) then
                try
                    p.Kill()
                with _ ->
                    ()

                Failed "the engine did not terminate within 120 s"
            else
                // The log file is more complete than stdout when the engine is chatty.
                let logFile = Path.Combine(dir, stem + ".log")

                let log =
                    if File.Exists logFile then
                        try
                            File.ReadAllText logFile
                        with _ ->
                            stdout
                    else
                        stdout

                // Two independent signals, because relying on one is how this gate went vacuous
                // the first time. The log gives a useful message; the exit code cannot be missed
                // by a pattern that stops matching after a flag change or an engine update.
                match errorLines log, p.ExitCode with
                | [||], 0 -> Compiled
                | [||], code -> Failed(sprintf "%s exited with code %d but reported no '!' error line" Engine code)
                | errs, _ -> Failed(String.Join("\n", errs))
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

/// Assert that TeX accepts a document, reporting the engine's own errors on failure.
///
/// Returns `false` when the engine is unavailable, so a caller can say so in its output rather
/// than pretending it checked something.
let accepts (name: string) (document: string) : bool =
    match compile name document with
    | Unavailable -> false
    | Compiled -> true
    | Failed errs ->
        failwithf
            "%s rejected the document '%s':\n%s\n\n--- document ---\n%s"
            Engine
            name
            errs
            document

/// A human-readable description of what was found, for test output.
let report () =
    match enginePath.Force() with
    | None -> Engine + " not found on PATH — TeX gates did not run"
    | Some p -> Engine + " found at " + p
