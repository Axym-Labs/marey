/// Rasterising a compiled figure, so that a test can look at the picture.
///
/// ### Why this exists
///
/// Phase 2 of the TikZ work shipped a backend whose markers and legend swatch were **entirely
/// invisible**, and every automated check passed. TikZ's `fill=<colour>` on a scope sets the
/// colour but does not request the fill *action*, so a bare `\path` inside `[fill=orange]`
/// compiles happily and draws nothing. Structure tests confirmed one macro, one scope and N
/// calls — all true. The compile gate confirmed valid TeX — also true. Determinism,
/// culture-invariance and cross-backend coordinate agreement all passed, because every
/// coordinate was right; only the ink was missing.
///
/// **A text emitter can be verified into a state where it emits a valid, correct, entirely blank
/// document.** Nothing in a string-comparison suite can see that. So this module compiles the
/// document for real and counts pixels.
///
/// ### What it can and cannot do
///
/// It answers *"is there ink, and is it where it belongs"*. It is deliberately **not** a
/// golden-image suite: no baseline bitmap is committed, because a bitmap is not reviewable in a
/// pull request (§13 wants text-diffable artefacts) and would break on every Poppler or TeX
/// font-metric update for reasons unrelated to Marey. Exact geometry is checked numerically by
/// the cross-backend agreement test instead, which is a sharper instrument.
///
/// The one thing here that no other gate can reach: **orientation.** Cross-backend agreement
/// states that TikZ's y is the SVG's y measured from the opposite edge — so a flip applied
/// wrongly to *both* backends still satisfies it. A raster knows which way up the page is.
///
/// the M1 plan §4 gate 7 is the entry this module implements.
///
/// ### When the rasteriser is absent
///
/// Same convention as `TexToolchain`: a test that cannot find its tools returns without
/// asserting and prints why, and CI installs the toolchain so enforcement is real there. Read the
/// test output rather than the green tick.
module Marey.Tests.Raster

open System
open System.Diagnostics
open System.IO
open System.Text
open Marey

/// Poppler's PDF rasteriser. Ships alongside the TeX distribution — it is already present in the
/// MiKTeX and TeX Live binary directories — so requiring it adds nothing to what the compile gate
/// already needs.
///
/// `pdftoppm` rather than `pdftocairo` (also present) because its PPM/PGM output is a header and
/// raw bytes, which this file parses in twenty lines. Asking for PNG would mean taking a PNG
/// decoder dependency into the test project to answer the question "is this pixel white".
[<Literal>]
let Tool = "pdftoppm"

let toolPath: Lazy<string option> = lazy TexToolchain.onPath Tool

let available = lazy (toolPath.Force() |> Option.isSome)

/// The resolution to rasterise at, and it is not an arbitrary choice.
///
/// `TikzBackend` maps the requested `Extent<px>` to TeX lengths at 96 px/inch (`TikzOptions.Dpi`),
/// and `TikzBackend.standalone` crops the page to the picture's bounding box — so the page is
/// exactly `extent * 72/96` PostScript points. Rasterising *that* at 96 dpi gives back
/// `extent * 72/96 * 96/72` = **exactly the requested extent in pixels**.
///
/// Two consequences worth having:
///
/// * a region can be written in the same numbers the layout used, and
/// * the pixel grid is the SVG backend's coordinate space, y downward — so if an SVG rasteriser
///   ever becomes available, the two images are directly comparable with no resampling.
///
/// It must therefore stay equal to `TikzOptions.Default.Dpi`. A fractional extent would round to
/// whole pixels here, so the tests use integral ones.
[<Literal>]
let Dpi = 96.0

/// A greyscale bitmap: one byte per pixel, row-major from the top-left, 0 black, 255 white.
type Gray =
    { Width: int
      Height: int
      Pixels: byte[] }

    member g.Item
        with get (x: int, y: int) = g.Pixels.[y * g.Width + x]

/// A rectangular region of the page, in fractions of its width and height.
///
/// `Left`/`Top`/`Right`/`Bottom` rather than `Rect<'u>`'s `X0`/`Y0`/`X1`/`Y1` **deliberately**:
/// every `Rect` in the library is in `nd`, where y grows *upward*, and a raster's y grows
/// *downward*. Reusing `Rect` here would put a silent flip one field-access away. Named edges
/// cannot be misread.
type Region =
    { Left: float
      Top: float
      Right: float
      Bottom: float }

/// Anything not essentially white counts as ink.
///
/// Calibrated rather than guessed. On this toolchain a blank page is **uniformly 255** — not
/// 254, not dithered — and the faintest thing Marey draws (an 0.4 px hairline, which is 0.3 bp,
/// well under one device pixel) rasterises to a minimum of 26. So every threshold between 255 and
/// 128 gives identical verdicts on every case in the suite, and this one is chosen to also ignore
/// near-white dithering that some other rasteriser might introduce.
[<Literal>]
let InkThreshold = 250

let isInk (v: byte) = int v < InkThreshold

// ======================================================================================
// PARSING
// ======================================================================================

/// Parse a binary greyscale PGM (`P5`), which is what `pdftoppm -gray` writes.
///
/// The header is `P5`, width, height, maximum value, each separated by whitespace and with `#`
/// comments permitted between them, then a single whitespace byte, then `width * height` bytes.
let parsePgm (bytes: byte[]) : Result<Gray, string> =
    let isSpace (b: byte) = b = 32uy || b = 10uy || b = 13uy || b = 9uy

    let mutable i = 0

    let token () =
        // Skip whitespace and comments, which may alternate.
        let mutable go = true

        while go do
            while i < bytes.Length && isSpace bytes.[i] do
                i <- i + 1

            if i < bytes.Length && bytes.[i] = 35uy then
                while i < bytes.Length && bytes.[i] <> 10uy do
                    i <- i + 1
            else
                go <- false

        let start = i

        while i < bytes.Length && not (isSpace bytes.[i]) do
            i <- i + 1

        Encoding.ASCII.GetString(bytes, start, i - start)

    let magic = token ()
    let w = token ()
    let h = token ()
    let maxValue = token ()

    let int' (s: string) =
        match Int32.TryParse(s, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture) with
        | true, v -> Some v
        | _ -> None

    match magic, int' w, int' h, int' maxValue with
    | "P5", Some w, Some h, Some maxValue when w > 0 && h > 0 ->
        if maxValue <> 255 then
            // 16-bit PGM would be two bytes per sample. `pdftoppm` never writes one, and a
            // silently mis-strided buffer is a worse outcome than a refusal.
            Error(sprintf "%s: expected an 8-bit PGM (maxval 255), got maxval %d" Tool maxValue)
        else
            // Exactly one whitespace byte separates the header from the data.
            let dataStart = i + 1
            let needed = w * h

            if bytes.Length - dataStart < needed then
                Error(
                    sprintf
                        "%s: PGM claims %dx%d (%d bytes) but only %d bytes follow the header"
                        Tool
                        w
                        h
                        needed
                        (bytes.Length - dataStart)
                )
            else
                Ok
                    { Width = w
                      Height = h
                      Pixels = Array.sub bytes dataStart needed }
    | _ -> Error(sprintf "%s: not a binary PGM (header read as %s %s %s %s)" Tool magic w h maxValue)

// ======================================================================================
// MEASURING
// ======================================================================================

let whole =
    { Left = 0.0
      Top = 0.0
      Right = 1.0
      Bottom = 1.0 }

/// Clamp a fractional region to whole pixels inside the bitmap.
let private pixelBounds (r: Region) (g: Gray) =
    let clamp lo hi v = max lo (min hi v)
    let x0 = clamp 0 g.Width (int (r.Left * float g.Width))
    let x1 = clamp 0 g.Width (int (r.Right * float g.Width))
    let y0 = clamp 0 g.Height (int (r.Top * float g.Height))
    let y1 = clamp 0 g.Height (int (r.Bottom * float g.Height))
    x0, y0, x1, y1

/// The fraction of pixels in a region that carry ink, in 0..1.
let coverageIn (r: Region) (g: Gray) : float =
    let x0, y0, x1, y1 = pixelBounds r g
    let mutable inked = 0
    let mutable total = 0

    for y in y0 .. y1 - 1 do
        let row = y * g.Width

        for x in x0 .. x1 - 1 do
            total <- total + 1

            if isInk g.Pixels.[row + x] then
                inked <- inked + 1

    if total = 0 then 0.0 else float inked / float total

let coverage (g: Gray) : float = coverageIn whole g

/// The mean grey level of a region, 0 black to 255 white (D64).
///
/// `coverageIn` answers "is there ink"; this answers "how dark", which is the question
/// **orientation** needs. A flipped image has ink in exactly the same box and the same amount
/// of it — only the light and dark halves have swapped — so a coverage differential cannot see
/// the bug at all, and a mean level can.
let meanLevelIn (r: Region) (g: Gray) : float =
    let x0, y0, x1, y1 = pixelBounds r g
    let mutable total = 0.0
    let mutable n = 0

    for y in y0 .. y1 - 1 do
        let row = y * g.Width

        for x in x0 .. x1 - 1 do
            total <- total + float g.Pixels.[row + x]
            n <- n + 1

    if n = 0 then 255.0 else total / float n

let meanLevel (g: Gray) (r: Region) : float = meanLevelIn r g

// ======================================================================================
// RASTERISING
// ======================================================================================

/// Rasterise a PDF that is already on disk.
///
/// Anti-aliasing is left **on** (Poppler's default). Turning it off would make the bitmap more
/// reproducible across Poppler versions, which matters for a golden image and this is not one; it
/// would also risk dropping a sub-pixel hairline entirely, which is exactly the false "no ink"
/// this gate must never report.
let rasterise (dpi: float) (pdf: string) : Result<Gray, string> =
    match toolPath.Force() with
    | None -> Error(Tool + " not found on PATH")
    | Some exe ->
        let dir = Path.GetDirectoryName pdf
        let prefix = Path.Combine(dir, Path.GetFileNameWithoutExtension pdf + "-raster")
        let output = prefix + ".pgm"

        let psi =
            ProcessStartInfo(
                FileName = exe,
                // `-gray` for one byte per pixel, `-singlefile` so the output name is
                // predictable rather than page-numbered. The resolution goes through the
                // library's own invariant formatter — a comma decimal separator here would be
                // read by the tool as a different number, or as none.
                Arguments =
                    String.Concat
                        [| "-gray -singlefile -r "
                           Num.at 4 dpi
                           " \""
                           pdf
                           "\" \""
                           prefix
                           "\"" |],
                WorkingDirectory = dir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            )

        use p = Process.Start psi
        p.StandardOutput.ReadToEnd() |> ignore
        let stderr = p.StandardError.ReadToEnd()

        if not (p.WaitForExit 120_000) then
            try
                p.Kill()
            with _ ->
                ()

            Error(Tool + " did not terminate within 120 s")
        elif p.ExitCode <> 0 then
            Error(sprintf "%s exited with code %d: %s" Tool p.ExitCode (stderr.Trim()))
        elif not (File.Exists output) then
            Error(sprintf "%s reported success but wrote no %s" Tool (Path.GetFileName output))
        else
            parsePgm (File.ReadAllBytes output)

/// Compile a LaTeX document and rasterise the page it produces.
///
/// Reuses `TexToolchain.compileWith`, so the engine is invoked with exactly the flags the compile
/// gate uses and a broken document fails here with the engine's own error message. The
/// rasterisation happens inside the compiler's temporary directory, before it is cleaned up.
let rec ofDocument (name: string) (document: string) : TexToolchain.PdfResult<Gray> =
    ofRendered name { Document = document; Assets = [||] }

/// `ofDocument` for a picture that refers to sidecar files (D64).
///
/// The assets are written beside the `.tex` before the engine runs, so a `\includegraphics`
/// pointing at a file nobody wrote fails here as a TeX error rather than as a blank region
/// nobody notices. That is the failure this gate is for: a valid document with no ink in it is
/// exactly what `Raster.fs` was written about.
and ofRendered (name: string) (rendered: RenderedDocument) : TexToolchain.PdfResult<Gray> =
    match toolPath.Force() with
    | None -> TexToolchain.PdfUnavailable(Tool + " not found on PATH")
    | Some _ -> TexToolchain.compileAssetsWith name rendered.Document rendered.Assets (rasterise Dpi)

// ======================================================================================
// REPORTING
//
// A coverage figure alone is a bad failure message: "expected ink, found none" leaves the reader
// no way to tell a blank page from a figure drawn off the edge. So a failure prints the picture,
// as text, in the test output. This is the same instinct as `Golden.describeDifference`.
// ======================================================================================

/// Render a bitmap as ASCII, one character per cell, densest last.
///
/// Cells are made twice as tall as they are wide because a terminal character is, so the shape of
/// the output is the shape of the figure. Density is mapped through a square root, which is what
/// makes a 2 %-covered line chart legible rather than uniformly blank.
let inkMap (columns: int) (g: Gray) : string =
    let columns = max 8 (min columns g.Width)
    let cellW = float g.Width / float columns
    let rows = max 1 (int (round (float g.Height / cellW / 2.0)))
    let cellH = float g.Height / float rows
    let ramp = " .:-=+*#%@"
    let sb = StringBuilder()

    for r in 0 .. rows - 1 do
        for c in 0 .. columns - 1 do
            let region =
                { Left = float c * cellW / float g.Width
                  Top = float r * cellH / float g.Height
                  Right = min 1.0 (float (c + 1) * cellW / float g.Width)
                  Bottom = min 1.0 (float (r + 1) * cellH / float g.Height) }

            let f = coverageIn region g
            let idx = min (ramp.Length - 1) (int (sqrt f * float ramp.Length))
            sb.Append ramp.[idx] |> ignore

        sb.Append '\n' |> ignore

    sb.ToString()

/// What a failing ink assertion prints: the numbers, then the picture.
let describe (g: Gray) : string =
    String.Concat
        [| sprintf "%dx%d px, ink coverage %.3f%%\n" g.Width g.Height (100.0 * coverage g)
           inkMap 78 g |]

/// A human-readable description of what was found, for test output.
let report () =
    match TexToolchain.enginePath.Force(), toolPath.Force() with
    | None, _ -> TexToolchain.Engine + " not found on PATH — ink gates did not run"
    | _, None -> Tool + " not found on PATH — ink gates did not run"
    | Some tex, Some raster -> sprintf "ink gate ready: %s, %s" tex raster
