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

/// The TikZ counterpart. The baseline is the **picture**, not a whole document: a `tikzpicture` is
/// what a user `\input`s, and a constant `\documentclass`/preamble prologue repeated across every
/// baseline is thirteen copies of boilerplate for a reviewer to scroll past. `TexToolchain` adds
/// the wrapper when a baseline has to be handed to an engine.
let renderTikzAt (extent: Extent<px>) (name: string) (node: Node) =
    check name (TikzBackend.render extent node)

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

// ======================================================================================
// THE SHARED FIGURE
//
// Gate 6 in the M1 plan: until now there was **no** golden baseline for a `Figure` in
// either backend. All thirteen scenes are hand-built `Node`s, and `Chart` output was checked only
// by substring assertions — so the one artefact users actually produce, and the only one that
// exercises layout, axes, ticks, legend and theme together, had no reviewable record at all.
//
// One figure, rendered by both backends, so a layout change shows up as a diff in two files that
// can be read against each other.
// ======================================================================================

/// A two-series figure with everything a real one has: a title, a subtitle, a legend, grids on
/// both axes, unit-derived axis titles, a line and a scatter.
///
/// `UnitStyle` is left at the theme default (`Unicode`) deliberately. The siunitx spelling is a
/// TeX-only artefact and would put raw macros into the SVG baseline; it gets its own TikZ-only
/// golden instead, so this pair stays a comparison of *the same picture* through two backends.
let figure: Figure =
    let ts = Array.init 24 (fun i -> float i * 0.4<s>)
    let xs = ts |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)

    Chart.line U.second U.metre ts xs
    |> Chart.withName "model"
    |> Chart.addScatter U.second U.metre ts.[..5] (ts.[..5] |> Array.map (fun t -> 0.5 * 9.6<m / s^2> * t * t))
    |> Chart.withName "measured"
    |> Chart.withTitle "Free fall"
    |> Chart.withSubtitle "displacement against elapsed time"
    |> Chart.withLegend LegendTopLeft
    |> Chart.withGrid true true

/// D45 — the categorical figure, and the second thing in this file that a user actually
/// produces.
///
/// Four string levels, one bar each, through `Chart.Category.bar`. What the baseline records
/// that no substring assertion can: that the bands are evenly spaced and centred, that the bars
/// are one band wide, that the level names land under them in order, and that the x axis carries
/// no conventional symbol while the y axis still carries its unit. A shift in any of those is a
/// diff in two files.
let categoryFigure: Figure =
    Chart.Category.bar
        U.metre
        [| "control"; "treated"; "placebo"; "washout" |]
        [| 12.4<m>; 19.1<m>; 7.8<m>; 15.0<m> |]
    |> Chart.withXLabel "treatment"
    |> Chart.withYLabel "yield"
    |> Chart.withTitle "Yield by treatment"
    |> Chart.withGrid false true

/// D46 / D49 — a categorical colour channel with shape as its redundant encoding.
///
/// One series, three levels, and the baseline records what the counts cannot: that the legend
/// enumerates the *levels* rather than the series, that each level's marks carry both its colour
/// and its shape, and that a channelled layer is still one batch per level in the emitted
/// document rather than one element per point.
let channelFigure: Figure =
    let keys = Array.init 24 (fun i -> [| "control"; "treated"; "washout" |].[i % 3])
    let ts = Array.init 24 (fun i -> float i * 0.4<s>)
    let xs = ts |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)

    Chart.scatter U.second U.metre ts xs
    |> Chart.colorByKey keys
    |> Chart.shapeByKey keys
    |> Chart.withTitle "Fall by treatment"
    |> Chart.withLegend LegendTopLeft

/// D49 — **the colorbar, in both backends, and this is the baseline the decision asked for.**
///
/// The failure it exists to catch is one no assertion can see. A gradient-filled bar is valid in
/// SVG and comes out of the TikZ backend as a single flat `#23908c` block, because
/// `Gradients = false` makes `Degrade.forCaps` collapse the ramp to its Oklab midpoint — valid
/// TeX, clean text diff, and no colorbar. So the bar is strips, and here are the strips, in a
/// file a reviewer can count.
///
/// It also records the placement claim: the bar is one more content column, the plot gives up
/// its width to it, and the x axis keeps its ticks.
let colorbarFigure: Figure =
    let ts = Array.init 24 (fun i -> float i * 0.4<s>)
    let xs = ts |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)
    let temps = ts |> Array.map (fun t -> (280.0 + 2.5 * float t) * 1.0<K>)

    Chart.scatter U.second U.metre ts xs
    |> Chart.colorBy U.kelvin temps
    |> Chart.withColorLabel "T"
    |> Chart.withTitle "Fall by temperature"

/// M2 phase 6 / D55 — **the box plot, as a picture**, and the first figure in this file whose
/// marks are a *summary* rather than the data.
///
/// What the baseline records that no assertion reaches: that the whiskers stop at an
/// observation rather than at the extreme, that the outlier is drawn outside them and is
/// countable, that the median rule spans the box exactly, and that the three bands are the same
/// width and evenly spaced. Every one of those is a thing that still renders when it is wrong.
///
/// The sample is written out rather than generated, because a golden master over a generated
/// sample is a record of a generator.
let boxplotFigure: Figure =
    let control = [| 12.1; 12.8; 13.4; 13.9; 14.2; 14.6; 15.1; 15.9; 22.5 |]
    let treated = [| 16.4; 17.0; 17.3; 17.9; 18.4; 18.8; 19.2; 19.9; 20.6 |]
    let washout = [| 11.8; 12.4; 13.1; 13.6; 14.0; 14.9; 15.4; 16.2; 16.8 |]

    let keys =
        Array.concat
            [ Array.create control.Length "control"
              Array.create treated.Length "treated"
              Array.create washout.Length "washout" ]

    let values =
        Array.concat [ control; treated; washout ] |> Array.map (fun v -> v * 1.0<m>)

    Chart.Category.boxplot U.metre keys values
    |> Chart.withYLabel "yield"
    |> Chart.withTitle "Yield by treatment"
    |> Chart.withGrid false true

/// M2 phase 6 — **the tile, and the colorbar over it.**
///
/// A heatmap is the geom that is worthless without a colour channel, so this is also the second
/// figure in the suite carrying a continuous ramp — and the one where the TikZ half matters for
/// a reason `figure-colorbar` does not cover: the tiles themselves are one `Path` per quantised
/// colour step (D46), so a batching regression shows up here as a file that is many times longer.
let heatmapFigure: Figure =
    let xs = [| 0.0 .. 5.0 |]
    let ys = [| 0.0 .. 3.0 |]

    let grid =
        [| for gy in ys do
               for gx in xs do
                   yield gx, gy |]

    let temps =
        grid |> Array.map (fun (gx, gy) -> (280.0 + 4.0 * gx + 7.0 * gy) * 1.0<K>)

    Chart.heatmap U.second U.metre U.kelvin (grid |> Array.map (fst >> (*) 1.0<s>)) (grid |> Array.map (snd >> (*) 1.0<m>)) temps
    |> Chart.withColorLabel "T"
    |> Chart.withTitle "Temperature field"

/// The figure surface. Larger than `surface` because a figure has margins, ticks and a legend to
/// fit, and a cramped one tests the collision ladder rather than the layout.
let figureSurface: Extent<px> = { Width = 480.0<px>; Height = 300.0<px> }

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
