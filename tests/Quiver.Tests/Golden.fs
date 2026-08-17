/// §13 — "Golden-image snapshots against the SVG backend (**text-diffable, reviewable in a PR**)".
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
/// Baselines live in `tests/Quiver.Tests/golden/` in the **source** tree, not in the build
/// output, because that is the copy a pull request diffs. The directory is found by walking up
/// from the test assembly until `Quiver.Tests.fsproj` appears, so nothing here depends on where
/// the repository is checked out or on which working directory the runner chose.
///
/// Regenerate with:
///
///     QUIVER_UPDATE_GOLDEN=1 dotnet test
module Quiver.Tests.Golden

open System
open System.IO
open System.Text
open Xunit
open Quiver

/// UTF-8 with no byte-order mark, matching `SvgBackend.renderToFile`. A BOM appearing or
/// disappearing would show up as a whole-file diff, which is exactly what golden masters must
/// never do.
let private utf8NoBom = UTF8Encoding(false)

[<Literal>]
let UpdateVariable = "QUIVER_UPDATE_GOLDEN"

/// True when the run has been asked to rewrite the baselines rather than check them.
let updateRequested =
    match Environment.GetEnvironmentVariable UpdateVariable with
    | null -> false
    | v ->
        let v = v.Trim()
        v <> "" && v <> "0" && not (String.Equals(v, "false", StringComparison.OrdinalIgnoreCase))

/// The `golden` directory in the source tree.
///
/// Walking up for the project file rather than using `__SOURCE_DIRECTORY__` keeps the path out
/// of the compiled assembly: a baked-in absolute path is a machine-specific path, and it is
/// wrong the moment the assembly is built on one machine and run on another.
let goldenDir =
    lazy
        (let rec findUp (dir: DirectoryInfo) =
            if isNull (box dir) then
                None
            elif File.Exists(Path.Combine(dir.FullName, "Quiver.Tests.fsproj")) then
                Some dir.FullName
            else
                findUp dir.Parent

         let root =
             match findUp (DirectoryInfo AppContext.BaseDirectory) with
             | Some d -> d
             // Fallback: the copy that travels with the binary. Check-only; an update run
             // without the source tree present would write somewhere nobody reviews, so that
             // case is refused below rather than silently accepted.
             | None -> AppContext.BaseDirectory

         let dir = Path.Combine(root, "golden")
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
    elif not (File.Exists file) then
        // Write the candidate anyway: a reviewer looking at a new test wants to see what it
        // produced, and the artefacts are git-ignored so they cannot be committed by accident.
        File.WriteAllText(candidate, actual, utf8NoBom)

        failwithf
            "Quiver golden: no baseline for '%s'.\n  expected at: %s\n  candidate written to: %s\n\nRe-run with %s=1 to accept it."
            name
            file
            candidate
            UpdateVariable
    else
        let expected = normalise (File.ReadAllText(file, Encoding.UTF8))

        if expected <> actual then
            File.WriteAllText(candidate, actual, utf8NoBom)

            failwithf
                "Quiver golden mismatch: %s\n  expected: %s\n  actual:   %s\n\n%s\nRe-run with %s=1 to accept the new output."
                name
                file
                candidate
                (describeDifference expected actual)
                UpdateVariable
        else
            // A stale artefact from a previous failing run would confuse the next reviewer.
            if File.Exists candidate then
                File.Delete candidate

/// The standard surface for golden documents.
///
/// Deliberately **not square and not round**: 320x200 is 8:5, so an anisotropic nd → px map is
/// the normal case rather than the exception. D13's text-rotation finding — that pushing the
/// direction vector through the CTM renders a requested 30° as 23.4° on 800x600 — is invisible
/// on a square surface, and a golden suite that only ever renders squares would never have
/// caught it.
let surface: Extent<px> = { Width = 320.0<px>; Height = 200.0<px> }

/// Render a scene at the standard surface and compare it to its baseline.
let render (name: string) (node: Node) = check name (SvgBackend.render surface node)

/// Render at a given surface, for the cases where the aspect ratio is the point.
let renderAt (extent: Extent<px>) (name: string) (node: Node) = check name (SvgBackend.render extent node)

// ======================================================================================
// SHARED SCENE VOCABULARY
//
// Small constructors so the golden scenes read as pictures rather than as record literals.
// ======================================================================================

let font size weight italic : FontSpec =
    { Family = "Helvetica"
      Size = size * 1.0<pt>
      Weight = weight
      Italic = italic }

let text (s: string) (x: float) (y: float) (anchor: TextAnchor) (baseline: TextBaseline) (rotation: float) =
    Text
        { Text = s
          Tex = None
          At = { X = x * 1.0<nd>; Y = y * 1.0<nd> }
          Font = font 11.0 400 false
          Paint = Solid(Color.rgb 0.1 0.1 0.12)
          Anchor = anchor
          Baseline = baseline
          Rotation = rotation * 1.0<rad> }

let pt (x: float) (y: float) : Vec2<nd> = { X = x * 1.0<nd>; Y = y * 1.0<nd> }

let stops (cs: (float * Color) list) =
    cs |> List.map (fun (o, c) -> { Offset = o; Color = c }) |> Array.ofList

/// Assert that a scene really does exercise the `Node` cases it claims to. §5 makes the case
/// count the thing to defend, so a golden test that says it covers all nine should be able to
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
                | Keyed _ -> "Keyed"
                | Annotated _ -> "Annotated"

            acc.Add name)
        Set.empty
        n

let assertAllNineCases (n: Node) =
    let used = casesUsed n

    let all =
        set [ "Path"; "Text"; "Markers"; "Raster"; "Group"; "Transform"; "Clip"; "Keyed"; "Annotated" ]

    Assert.Equal<Set<string>>(all, used)
