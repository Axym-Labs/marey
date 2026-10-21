/// "Golden-image snapshots against the SVG backend (**text-diffable, reviewable in a PR**)".
///
/// The two words in bold are the whole design of this file. A golden test that fails with
/// "Assert.Equal() Failure: Strings differ" and 4 kB of SVG on each side is not reviewable, so
/// this one reports the *first differing line* with surrounding context, and writes the actual
/// document next to the expected one as `*.actual.<ext>` so the reviewer can open both.
///
/// `check` is deliberately **format-agnostic**: it compares normalised text and derives its
/// failure-artefact name from the baseline's extension, so the TikZ backend's `.tex` output is
/// reviewed on exactly the same terms as the SVG backend's. Only the `render` helpers below are
/// SVG-specific, because they choose a backend.
///
/// Baselines live in `tests/Marey.Tests/golden/` in the **source** tree, not in the build
/// output, because that is the copy a pull request diffs. The directory is found by walking up
/// from the test assembly until `Marey.Tests.fsproj` appears, so nothing here depends on where
/// the repository is checked out or on which working directory the runner chose.
///
/// Regenerate with:
///
///     MAREY_UPDATE_GOLDEN=1 dotnet test
module Marey.Tests.Golden

open System
open System.IO
open System.Text
open Xunit
open Marey
open Marey.UnitsOfMeasure

/// UTF-8 with no byte-order mark, matching `SvgBackend.renderToFile`. A BOM appearing or
/// disappearing would show up as a whole-file diff, which is exactly what golden masters must
/// never do.
let private utf8NoBom = UTF8Encoding(false)

[<Literal>]
let UpdateVariable = "MAREY_UPDATE_GOLDEN"

/// True when the run has been asked to rewrite the baselines rather than check them.
let updateRequested =
    match Environment.GetEnvironmentVariable UpdateVariable with
    | null -> false
    | v ->
        let v = v.Trim()
        v <> "" && v <> "0" && not (String.Equals(v, "false", StringComparison.OrdinalIgnoreCase))

/// The `Marey.Tests` project directory in the source tree.
///
/// Walking up for the project file rather than using `__SOURCE_DIRECTORY__` keeps the path out
/// of the compiled assembly: a baked-in absolute path is a machine-specific path, and it is
/// wrong the moment the assembly is built on one machine and run on another.
///
/// A named value rather than the body of `goldenDir` because `InteractiveTests` is the second
/// consumer — it checks two files that ship in the source tree and are not golden masters — and
/// F2's rule is to promote on the second consumer rather than to copy.
///
/// Falls back to the directory the binary sits in, where the baselines are copied by the build.
/// That keeps a *checking* run working when the test assembly has been moved away from its source
/// tree. It does not make an *update* run in that situation safe — `MAREY_UPDATE_GOLDEN=1` would
/// write into the build output, where nobody reviews it and the next build overwrites it. The
/// earlier version of this comment claimed the case was "refused below"; nothing refuses it, and
/// the honest statement is that an update run belongs in a checkout.
let projectDir =
    lazy
        (let rec findUp (dir: DirectoryInfo) =
            if isNull (box dir) then
                None
            elif File.Exists(Path.Combine(dir.FullName, "Marey.Tests.fsproj")) then
                Some dir.FullName
            else
                findUp dir.Parent

         match findUp (DirectoryInfo AppContext.BaseDirectory) with
         | Some d -> d
         | None -> AppContext.BaseDirectory)

/// The repository root — `projectDir` is `tests/Marey.Tests`, so it is two levels up.
let repoRoot =
    lazy (Path.GetFullPath(Path.Combine(projectDir.Force(), "..", "..")))

/// The `golden` directory in the source tree.
let goldenDir =
    lazy
        (let dir = Path.Combine(projectDir.Force(), "golden")
         Directory.CreateDirectory dir |> ignore
         dir)

let private path (name: string) = Path.Combine(goldenDir.Force(), name)

/// Where a failing run writes what it actually produced.
///
/// The suffix mirrors the baseline's own extension — `paths.svg` fails to `paths.svg.actual.svg`
/// and `paths.tex` to `paths.tex.actual.tex` — so `.gitignore` can exclude the artefacts by
/// pattern per format and a reviewer can open the failure in the right editor. `check` itself is
/// format-agnostic: it compares text, and the TikZ backend's output is text on exactly the same
/// terms as the SVG backend's.
let private actualPath (file: string) =
    let ext = Path.GetExtension file
    file + ".actual" + (if ext = "" then ".txt" else ext)

/// Line endings are normalised on both sides.
///
/// The backend always emits `\n` (SvgWriter.fs: "line endings are always `\n`, never
/// `Environment.NewLine`"), but git on Windows may check the baseline *out* as CRLF depending on
/// the user's `core.autocrlf`. Comparing raw bytes would then fail on a clean checkout for a
/// reason that has nothing to do with rendering.
let private normalise (s: string) = s.Replace("\r\n", "\n").Replace("\r", "\n")

/// A readable report of where two documents first diverge.
///
/// Shows three lines of context on each side of the first differing line, marks the differing
/// pair with `-` (expected) and `+` (actual), and points at the differing column. That is enough
/// to see "the y coordinate moved by 0.5 px" without opening either file.
let private describeDifference (expected: string) (actual: string) =
    let e = expected.Split '\n'
    let a = actual.Split '\n'

    let firstDiff =
        let n = min e.Length a.Length

        let rec go i =
            if i >= n then (if e.Length = a.Length then -1 else n)
            elif e.[i] <> a.[i] then i
            else go (i + 1)

        go 0

    let sb = StringBuilder()

    if firstDiff < 0 then
        sb.AppendLine "Documents differ only in trailing content." |> ignore
    else
        let column =
            if firstDiff < e.Length && firstDiff < a.Length then
                let el = e.[firstDiff]
                let al = a.[firstDiff]
                let n = min el.Length al.Length

                let rec go i =
                    if i >= n then n + 1
                    elif el.[i] <> al.[i] then i + 1
                    else go (i + 1)

                go 0
            else
                1

        sb
            .AppendLine(sprintf "First difference at line %d, column %d." (firstDiff + 1) column)
            .AppendLine()
        |> ignore

        let lo = max 0 (firstDiff - 3)
        let hi = min (max e.Length a.Length) (firstDiff + 4)

        for i in lo .. hi - 1 do
            let el = if i < e.Length then Some e.[i] else None
            let al = if i < a.Length then Some a.[i] else None

            if i = firstDiff || el <> al then
                match el with
                | Some l -> sb.AppendLine(sprintf "  - %4d | %s" (i + 1) l) |> ignore
                | None -> sb.AppendLine(sprintf "  - %4d | <no such line>" (i + 1)) |> ignore

                match al with
                | Some l -> sb.AppendLine(sprintf "  + %4d | %s" (i + 1) l) |> ignore
                | None -> sb.AppendLine(sprintf "  + %4d | <no such line>" (i + 1)) |> ignore
            else
                match el with
                | Some l -> sb.AppendLine(sprintf "    %4d | %s" (i + 1) l) |> ignore
                | None -> ()

    sb
        .AppendLine()
        .AppendLine(sprintf "(%d lines expected, %d lines actual)" e.Length a.Length)
    |> ignore

    sb.ToString()

/// Compare a rendered document against its baseline.
///
/// * baseline missing → fail with instructions, unless an update was requested;
/// * baseline differs → write `<name>.actual.<ext>` beside it and fail with the diff;
/// * update requested → write the baseline and pass.
let check (name: string) (actual: string) : unit =
    let file = path name
    let candidate = actualPath file
    let actual = normalise actual

    if updateRequested then
        File.WriteAllText(file, actual, utf8NoBom)

        // An accepted baseline makes the candidate from the run that failed a stale copy of the
        // file next to it. Leaving it behind is how a reviewer ends up diffing two identical
        // files and wondering which one matters.
        if File.Exists candidate then
            File.Delete candidate
    elif not (File.Exists file) then
        // Write the candidate anyway: a reviewer looking at a new test wants to see what it
        // produced, and the artefacts are git-ignored so they cannot be committed by accident.
        File.WriteAllText(candidate, actual, utf8NoBom)

        failwithf
            "Marey golden: no baseline for '%s'.\n  expected at: %s\n  candidate written to: %s\n\nRe-run with %s=1 to accept it."
            name
            file
            candidate
            UpdateVariable
    else
        let expected = normalise (File.ReadAllText(file, Encoding.UTF8))

        if expected <> actual then
            File.WriteAllText(candidate, actual, utf8NoBom)

            failwithf
                "Marey golden mismatch: %s\n  expected: %s\n  actual:   %s\n\n%s\nRe-run with %s=1 to accept the new output."
                name
                file
                candidate
                (describeDifference expected actual)
                UpdateVariable
        else
            // A stale artefact from a previous failing run would confuse the next reviewer.
            if File.Exists candidate then
                File.Delete candidate

/// Compare rendered **bytes** against their baseline.
///
/// The same three-way behaviour as `check`, and deliberately not the same failure message: a
/// binary has no lines to diff, so what a reader gets instead is the first differing offset and
/// the two bytes there, which is what actually locates a change in an encoder's output.
///
/// **§5 of the M2.5 plan says a PNG baseline is not a golden, and this does not contradict it.**
/// That argument is about a *rasteriser's* output — a bitmap that moves on a Skia version bump
/// for reasons unrelated to Marey. These bytes come from Marey's own zero-dependency encoder
/// over a hand-written pixel array, so nothing outside this repository can move them, and they
/// are the only check anywhere that the encoder's actual output is stable. The file is a few
/// hundred bytes and a reviewer can open it.
let checkBytes (name: string) (actual: byte[]) : unit =
    let file = path name
    let candidate = actualPath file

    let describe (expected: byte[]) (actual: byte[]) =
        let n = min expected.Length actual.Length

        let firstDiff =
            let rec go i =
                if i >= n then n
                elif expected.[i] <> actual.[i] then i
                else go (i + 1)

            go 0

        if firstDiff < n then
            sprintf "First difference at byte %d: expected 0x%02X, actual 0x%02X. (%d bytes expected, %d actual)" firstDiff expected.[firstDiff] actual.[firstDiff] expected.Length actual.Length
        else
            sprintf "Identical for %d bytes, then the lengths differ: %d expected, %d actual." n expected.Length actual.Length

    if updateRequested then
        File.WriteAllBytes(file, actual)

        if File.Exists candidate then
            File.Delete candidate
    elif not (File.Exists file) then
        File.WriteAllBytes(candidate, actual)

        failwithf
            "Marey golden: no baseline for '%s'.
  expected at: %s
  candidate written to: %s

Re-run with %s=1 to accept it."
            name
            file
            candidate
            UpdateVariable
    else
        let expected = File.ReadAllBytes file

        if expected <> actual then
            File.WriteAllBytes(candidate, actual)

            failwithf
                "Marey golden mismatch: %s
  expected: %s
  actual:   %s

%s
Re-run with %s=1 to accept the new output."
                name
                file
                candidate
                (describe expected actual)
                UpdateVariable
        elif File.Exists candidate then
            File.Delete candidate

// The scene vocabulary and the figures moved to `tests/Shared/GoldenScenes.fs`, where
// the Fable project compiles them too. These are the names the suite already uses.
let surface = GoldenScenes.surface
let font = GoldenScenes.font
let text = GoldenScenes.text
let pt = GoldenScenes.pt
let stops = GoldenScenes.stops
let figure = GoldenScenes.figure
let categoryFigure = GoldenScenes.categoryFigure
let channelFigure = GoldenScenes.channelFigure
let colorbarFigure = GoldenScenes.colorbarFigure
let boxplotFigure = GoldenScenes.boxplotFigure
let heatmapFigure = GoldenScenes.heatmapFigure
let imageFigure = GoldenScenes.imageFigure
let logFigure = GoldenScenes.logFigure
let figureSurface = GoldenScenes.figureSurface


/// Render a scene at the standard surface and compare it to its baseline.
let render (name: string) (node: Node) = check name (SvgBackend.render surface node)

/// Render at a given surface, for the cases where the aspect ratio is the point.
let renderAt (extent: Extent<px>) (name: string) (node: Node) = check name (SvgBackend.render extent node)

/// The TikZ counterpart. The baseline is the **picture**, not a whole document: a `tikzpicture` is
/// what a user `\input`s, and a constant `\documentclass`/preamble prologue repeated across every
/// baseline is thirteen copies of boilerplate for a reviewer to scroll past. `TexToolchain` adds
/// the wrapper when a baseline has to be handed to an engine.
let renderTikzAt (extent: Extent<px>) (name: string) (node: Node) =
    check name (TikzBackend.render extent node)


/// Assert that a scene really does exercise the `Node` cases it claims to. §5 makes the case
/// count the thing to defend, so a golden test that says it covers all ten should be able to
/// prove it rather than be believed.
let casesUsed (n: Node) =
    Scene.fold
        (fun (acc: Set<string>) node ->
            let name =
                match node with
                | Path _ -> "Path"
                | Text _ -> "Text"
                | Markers _ -> "Markers"
                | Raster _ -> "Raster"
                | Group _ -> "Group"
                | Transform _ -> "Transform"
                | Clip _ -> "Clip"
                | Opacity _ -> "Opacity"
                | Keyed _ -> "Keyed"
                | Annotated _ -> "Annotated"

            acc.Add name)
        Set.empty
        n

let assertAllTenCases (n: Node) =
    let used = casesUsed n

    let all =
        set [ "Path"; "Text"; "Markers"; "Raster"; "Group"; "Transform"; "Clip"; "Opacity"; "Keyed"; "Annotated" ]

    Assert.Equal<Set<string>>(all, used)
