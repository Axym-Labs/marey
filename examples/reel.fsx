// The demo reel: the whole library in one shot, ending in an .mp4.
//
//   dotnet fsi examples/reel.fsx
//
// Every other example here shows one thing. This one is the milestone's argument end to end, and
// it is the only file in the repository written for somebody who has read none of the rest:
//
//   * the axes are derived from two **unit witnesses**, so "Hz" and "m" are the types of the
//     data and not strings anybody typed;
//   * the storyboard is a **value** — it measures nothing and renders nothing until an instant is
//     asked for, which is why frame 900 costs the same as frame 1;
//   * the layout is computed **once for the whole shot**, so the panel holds still while the data
//     moves under it — the thing that makes a plot animation watchable rather than seasick;
//   * a mark is addressed by its **row**, so the point picked out at the end is a fact about the
//     measurement rather than about what happened to land where;
//   * the frames are the product and ffmpeg is a convenience over them, so a machine without an
//     encoder still gets a numbered directory any editor imports.
//
// It writes `reel-frames/`, `reel.mp4`, `reel-poster.svg` and `reel-contact-sheet.svg` into the
// working directory. The contact sheet is there so that the reel can be *looked at* without an
// encoder, which is the whole degrade story in one artefact.
#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Svg"
#r "nuget: Marey.Backend.Skia"
#r "nuget: Marey.Toolchain"
#r "nuget: Marey.Tools.Video"
#r "nuget: Marey.Tools.Tex"
#r "nuget: Marey.Html"

open Marey
open Marey.UnitsOfMeasure
open Marey.Storyboard

// ---------------------------------------------------------------------------------------------
// What this machine can do. Printed first and unconditionally: the two interesting failures here
// are silent ones — a reel that never encoded, and a poster whose formula never typeset — and
// both of them still produce a file and exit zero.
// ---------------------------------------------------------------------------------------------

printfn "%s" (Video.report ())
printfn "%s" (Typeset.report ())

// ---------------------------------------------------------------------------------------------
// The measurement: a driven oscillator's amplitude against drive frequency.
//
// A resonance, because it is the shape every physicist recognises from across a room, and because
// it has a feature worth zooming into and a single point worth naming.
// ---------------------------------------------------------------------------------------------

/// Natural frequency, damping, and the low-frequency amplitude. Written as measured quantities,
/// so the model below is checked by the compiler in the same currency as the data.
let f0 = 1.4<Hz>
let gamma = 0.18<Hz>
let a0 = 4.0e-3<m>

/// The steady-state amplitude of a driven damped oscillator:
///
///     A(f) = A₀ f₀² / sqrt((f₀² − f²)² + (γf)²)
///
/// which peaks near `f0` at roughly `a0 * f0 / gamma` — about 31 mm here. `float` on the way in
/// because the shape is a ratio of frequencies; the witness belongs on the axis, and it is there.
let response (f: float<Hz>) : float<m> =
    let d = float f0 ** 2.0 - float f ** 2.0
    let g = float gamma * float f
    a0 * float f0 ** 2.0 / sqrt (d * d + g * g)

let drive = Array.init 90 (fun i -> 0.25<Hz> + float i * 0.025<Hz>)

/// Seeded, so the reel is the same reel on every machine and in every run. A demo that shuffles
/// between takes cannot be diffed, and the golden baselines in this repository exist because that
/// matters more than it sounds like it does.
///
/// Scaled against the *peak* rather than fixed: noise that swamps the resonance makes a reel of a
/// scatter plot, which is what the first take of this file was.
let noise =
    let rng = System.Random 20260908
    Array.init drive.Length (fun _ -> (rng.NextDouble() - 0.5) * 1.6e-3<m>)

let measured = Array.map2 (fun f n -> response f + n) drive noise
let model = drive |> Array.map response

// The two figures the reel morphs between. Same geom and same length, because a morph pairs marks
// by row — a scatter of 90 points becoming a curve of 90 points, not a scatter becoming a line.
let figureOf (title: string) (ys: float<m>[]) =
    Chart.scatter U.hertz U.metre drive ys
    |> Chart.withTitle title
    |> Chart.withYLabel "amplitude"

let observed = figureOf "Driven oscillator: a resonance" measured
let fitted = figureOf "Driven oscillator: a resonance" model

/// The row the reel names at the end: the loudest measurement. Found in the data, not counted off
/// the picture — which is the point of addressing a mark by its row rather than by its position
/// among whatever marks turned out to be finite and on-screen.
let loudest = measured |> Array.mapi (fun i v -> i, float v) |> Array.maxBy snd |> fst

printfn "the loudest measurement is row %d, at %.3g Hz" loudest (float drive.[loudest])

// ---------------------------------------------------------------------------------------------
// The reel itself. Four shots, and the whole thing is a value — nothing below has happened yet.
// ---------------------------------------------------------------------------------------------

let reel =
    scene {
        // 1. The measurements arrive.
        let! points = draw observed |> over 1.0<s>
        do! wait 0.3<s>

        // 2. They settle onto the model. `cubicInOut` because a physical thing that starts and
        //    stops should do both gently; `Ease` carries twenty-eight of these.
        do! points |> morphTo fitted |> over 1.4<s> |> ease Ease.cubicInOut
        do! wait 0.2<s>

        // 3. Two zooms at once, onto the peak. This is the shot the milestone is really about:
        //    the y domain crosses a decade and the x domain gains a digit, so a layout computed
        //    per frame would have the panel breathing and the axis title stepping between
        //    millimetres and metres while the data moves. It does neither, and the check below
        //    is what says so.
        do!
            parallel'
                [ points |> zoomToX (0.9, 2.0) |> over 1.6<s> |> ease Ease.cubicInOut
                  points |> zoomToY (0.0, 0.034) |> over 1.6<s> |> ease Ease.cubicInOut ]

        // 4. One mark, named. No `over`: the step runs for as long as the handle took to arrive,
        //    which is why a handle carries a duration at all.
        do! highlight points.[loudest]
    }

let duration = Storyboard.duration reel
printfn "the reel runs for %g s" (float duration)

// ---------------------------------------------------------------------------------------------
// Rendering. One arrangement for the shot, one measurer for the film.
// ---------------------------------------------------------------------------------------------

// 640 x 400: both even, which `yuv420p` requires and which `Video.check` refuses without rather
// than letting ffmpeg say "width not divisible by 2" four steps later.
let extent = Chart.size 640.0 400.0

// 48 sampled states for the union arrangement. There is deliberately no default in
// `Chart.arrangeUnion` — a default belongs where a caller can see it — and the number is a real
// trade: the union is only as good as its samples, so a spike between two of them can still
// overflow. 48 for a reel with a fast zoom in it; the guide uses 32.
let samples = 48

// One measurer for the whole film, memoised. An `IBackend` is single-threaded and an
// `ITextMeasurer` must not be, and exactly one of the latter serves a whole film — so a video
// cannot change typeface on a frame boundary, which is a thing that can otherwise happen once
// per thread.
let measurer = MemoTextMeasurer TextMeasure.approx

// One film, and everything below reads it. `Film.ofStoryWith` is `Storyboard.shoot` plus the
// record around it, so building the film and then shooting the storyboard again would arrange
// the union twice — which is the expensive half, and the half that happens before any frame
// exists.
let film = Film.ofStoryWith samples measurer extent reel
let anim = film.Frames

printfn "the shot was arranged against %d sampled states" film.Shot.Samples

// Seeking is evaluation, not replay: an instant is computed directly, so asking for the same one
// twice gives the same scene and asking out of order costs nothing. This is the property that
// makes the parallel render below legal at all.
printfn "seeking is evaluation: %b" (anim.At(0.7 * duration) = anim.At(0.7 * duration))

// The panel rectangle is the panel's `Transform`. Collected across frames spanning the whole reel
// and de-duplicated it should be exactly *one* rectangle.
let plotRect (n: Node) =
    n
    |> Scene.fold
        (fun acc x ->
            match x with
            | Transform(a, _) -> a :: acc
            | _ -> acc)
        []

let probes = [| 0.05; 0.2; 0.35; 0.5; 0.65; 0.8; 0.95; 1.0 |]
let probed = probes |> Array.map (fun u -> anim.At(u * duration))

printfn
    "the panel holds still across the whole reel: %b"
    ((probed |> Array.map plotRect |> Array.distinct).Length = 1)

// ---------------------------------------------------------------------------------------------
// The frames. This is the product; everything after it is a convenience over it.
// ---------------------------------------------------------------------------------------------

let fps = 25.0
let target = FrameTarget.into "reel-frames" ".png"
let sequence = film |> Frames.write FramePass.Concurrent SkiaBackend.renderPng target fps

printfn "wrote %d frames into %s/ as %s" sequence.Files.Length sequence.Directory sequence.Pattern

// What the shared measurer was worth. `Measurements` is what reached the measurer underneath and
// `Entries` is how many distinct strings it holds — so the measuring is bounded by the strings in
// the reel rather than by its frames, which is the whole reason one memoised measurer serves the
// film instead of one per thread.
printfn
    "%d frames measured %d distinct strings in %d calls"
    sequence.Files.Length
    measurer.Entries
    measurer.Measurements

printfn "the measuring is bounded by the text, not by the frames: %b" (measurer.Measurements < sequence.Files.Length)

// A directory that already held a longer reel would leave that one's tail in place, and
// `ffmpeg -i reel-%05d.png` reads until the first gap — so it is reported rather than deleted,
// because emptying a caller's directory is not a rendering function's decision.
printfn "no stale frames from an earlier take: %b" (Array.isEmpty sequence.Stale)

// ---------------------------------------------------------------------------------------------
// The poster: one still, with its axis titles set by TeX.
//
// **One still, and not every frame, on purpose.** The typesetting cache lives for one call, so
// typesetting a film frame by frame pays a figure's worth of `latex` per frame — about a fifth of
// a second each. The still is the frame that ends up in a paper; the reel is the frame sequence.
// ---------------------------------------------------------------------------------------------

let posterFigure =
    figureOf "Driven oscillator: a resonance" model |> Chart.withUnitStyle Siunitx

let posterScene = Chart.toSceneWith measurer extent posterFigure
let poster, note = Typeset.sceneOrPlain TexOptions.Default extent posterScene

printfn "%s" note

System.IO.File.WriteAllText("reel-poster.svg", SvgBackend.render extent poster)
printfn "wrote reel-poster.svg"

// ---------------------------------------------------------------------------------------------
// The contact sheet: eight frames of the reel, so it can be seen without an encoder.
// ---------------------------------------------------------------------------------------------

let cell (row: int) (col: int) (node: Node) =
    let squeeze: Affine<nd, nd> = Affine.scale 0.25 0.5

    let slide: Affine<nd, nd> =
        Affine.translate
            { X = float col / 4.0 * 1.0<nd>
              Y = (1.0 - float (row + 1) / 2.0) * 1.0<nd> }

    Transform(Affine.compose squeeze slide, node)

let sheet =
    Group
        [| Scene.rect None (Some(Solid Color.white)) Rect.unit
           yield! probed |> Array.mapi (fun i f -> cell (i / 4) (i % 4) f) |]

System.IO.File.WriteAllText("reel-contact-sheet.svg", SvgBackend.render (Chart.size 1280.0 800.0) sheet)
printfn "wrote reel-contact-sheet.svg"

// ---------------------------------------------------------------------------------------------
// The flipbook: the reel, playing, with no encoder involved at all.
//
// This is the same film the mux below encodes, rendered as one SVG whose frames cycle under SMIL
// `<animate>`. It is what a notebook cell shows when the value is a `Film`, and the reason to
// write it to a file here is that a file can be opened in a browser without installing Verso.
//
// 24 frames rather than `Html.FlipbookFrames`: this reel is 5.5 s, so 48 would be 8.7 fps and
// half a megabyte. The frame count is the cost, and an example should show the knob rather than
// take the default silently.
// ---------------------------------------------------------------------------------------------

let flipbook = Html.filmAt 24 film

System.IO.File.WriteAllText(
    "reel-flipbook.html",
    "<!doctype html><meta charset=\"utf-8\"><title>Marey reel</title>" + flipbook
)

printfn "wrote reel-flipbook.html, %d KB" (System.Text.Encoding.UTF8.GetByteCount flipbook / 1024)

// The two properties the whole approach rests on: it animates, and it does it without a
// stylesheet — a `<style>` element in a notebook cell is not scoped to that cell.
printfn "the flipbook animates without a stylesheet: %b" (flipbook.Contains "<animate" && not (flipbook.Contains "<style"))

// And no id is repeated across frames. SVG ids are scoped to the document rather than to a nested
// `<svg>`, so without per-frame prefixes the highlighted mark in the last frames would draw with
// the first frame's marker.
let flipbookIds =
    System.Text.RegularExpressions.Regex.Matches(flipbook, "id=\"([^\"]+)\"")
    |> Seq.map (fun m -> m.Groups.[1].Value)
    |> Seq.toList

printfn
    "%d ids across 24 frames, all distinct: %b"
    flipbookIds.Length
    ((List.distinct flipbookIds).Length = flipbookIds.Length)

// ---------------------------------------------------------------------------------------------
// The encode, and the degrade. Both branches print; neither throws.
// ---------------------------------------------------------------------------------------------

match Video.mux MuxOptions.Default sequence "reel.mp4" with
| MuxUnavailable why ->
    printfn "%s" why
    printfn "the frames and the contact sheet are still on disk: %b" (sequence.Files |> Array.forall System.IO.File.Exists)

| MuxRefused why -> printfn "refused: %s" why
| MuxTimedOut why -> printfn "timed out: %s" why

| MuxRan run when not run.Succeeded ->
    printfn "ffmpeg exited %d" run.ExitCode
    printfn "%s" run.Log

| MuxRan run ->
    printfn "encoded %s, %d bytes" (System.IO.Path.GetFileName run.Output) (System.IO.FileInfo run.Output).Length

    // Read the container back rather than trusting the exit code. An encoder that put a third of
    // the frames in the file exits zero too.
    match Video.inspect run.Output with
    | InspectUnavailable why -> printfn "%s" why
    | InspectFailed why -> printfn "could not read it back: %s" why
    | Inspected info ->
        printfn
            "%s, %d x %d, %d frames at %g fps, %g s"
            info.Codec
            info.Width
            info.Height
            info.FrameCount
            info.Fps
            (float info.Duration)

        printfn "every rendered frame is in the reel, exactly once: %b" (info.FrameCount = sequence.Files.Length)

        // The container carries nothing naming the build that wrote it, which is what makes two
        // encodes on two machines comparable.
        printfn "the reel names no ffmpeg build: %b" (List.isEmpty info.BuildTags)
