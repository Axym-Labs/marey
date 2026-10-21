// From a storyboard to an .mp4, by shelling out to ffmpeg.
//
//   dotnet fsi examples/video.fsx
//
// The frame sequence is the product and the mux is a convenience over it. That is not a slogan
// about layering — it is what happens when ffmpeg is not installed: this script still writes
// every frame, still tells you what it wrote, and reports the encoder's absence as an outcome
// rather than throwing. A numbered directory is something every editor imports.
//
// What it checks rather than asserts:
//
//   * the frame rate is an *input* option, so every rendered frame is in the video exactly once
//     — the container is read back and counted;
//   * the container names no ffmpeg build, which is what the bit-exact flags are for;
//   * two encodes of one sequence are byte-identical.
//
// It writes `frames/`, `video.mp4` and a poster frame into the working directory.
#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Skia"
#r "nuget: Marey.Toolchain"
#r "nuget: Marey.Tools.Video"

open Marey
open Marey.UnitsOfMeasure
open Marey.Storyboard

// ---------------------------------------------------------------------------------------------
// What was found. Printed whether or not anything was: a script that silently did less is worse
// than one that says which half it did.
// ---------------------------------------------------------------------------------------------

printfn "%s" (Video.report ())

// ---------------------------------------------------------------------------------------------
// A damped oscillation, drawn and then zoomed out of.
// ---------------------------------------------------------------------------------------------

let t = Array.init 200 (fun i -> float i * 0.05<s>)

let ring =
    t
    |> Array.map (fun v -> 30.0e-3 * exp (-float v / 3.0) * cos (6.0 * float v) * 1.0<m>)

let trace =
    Chart.scatter U.second U.metre t ring
    |> Chart.withTitle "A damped oscillation"
    |> Chart.withYLabel "x"

let story =
    scene {
        let! curve = draw trace |> over 0.8<s>
        do! curve |> zoomToY (-0.05, 0.05) |> over 1.2<s> |> ease Ease.cubicInOut
    }

// 480 x 300: both even, which `yuv420p` needs and which `Video.check` refuses without. One
// measurer for the whole film, memoised, because every frame is a HarfBuzz shaping run otherwise.
let measurer = MemoTextMeasurer TextMeasure.approx
let film = Film.ofStoryWith 32 measurer (Chart.size 480.0 300.0) story

let fps = 25.0
let target = FrameTarget.into "frames" ".png"
let sequence = film |> Frames.write FramePass.Concurrent SkiaBackend.renderPng target fps

printfn "wrote %d frames into %s/ as %s" sequence.Files.Length sequence.Directory sequence.Pattern
printfn "the frames are %A px" (Video.frameSize sequence.Files.[0])

// A poster frame beside the video, which is the still a reader of a paper actually gets.
System.IO.File.Copy(sequence.Files.[sequence.Files.Length - 1], "video-poster.png", true)

// ---------------------------------------------------------------------------------------------
// The command. It is a pure function of the options and the sequence, so it can be printed,
// diffed and reproduced, which a wrapper that builds the arguments itself would not allow.
// ---------------------------------------------------------------------------------------------

printfn "ffmpeg %s" (String.concat " " (Video.args MuxOptions.Default sequence "video.mp4"))

// Refused before ffmpeg is started, when there is something to refuse. `None` is a clean bill.
printfn "nothing objects to this encode: %b" ((Video.check MuxOptions.Default sequence).IsNone)

// ---------------------------------------------------------------------------------------------
// The mux, and the degrade. Both branches print; neither throws.
// ---------------------------------------------------------------------------------------------

match Video.mux MuxOptions.Default sequence "video.mp4" with
| MuxUnavailable why ->
    printfn "%s" why
    printfn "the frames are still on disk: %b" (sequence.Files |> Array.forall System.IO.File.Exists)

| MuxRefused why -> printfn "refused: %s" why
| MuxTimedOut why -> printfn "timed out: %s" why

| MuxRan run when not run.Succeeded ->
    printfn "ffmpeg exited %d" run.ExitCode
    printfn "%s" run.Log

| MuxRan run ->
    printfn "encoded %s, %d bytes" (System.IO.Path.GetFileName run.Output) (System.IO.FileInfo run.Output).Length

    // Read the container back rather than trusting the exit code. An encoder that put thirteen
    // of twenty-four frames in the file exits zero too — which is what one misplaced `-r` does.
    match Video.inspect run.Output with
    | InspectUnavailable why -> printfn "%s" why
    | InspectFailed why -> printfn "could not read it back: %s" why
    | Inspected info ->
        printfn "%s, %d x %d, %d frames at %g fps, %g s" info.Codec info.Width info.Height info.FrameCount info.Fps (float info.Duration)

        printfn "every rendered frame is in the video, exactly once: %b" (info.FrameCount = sequence.Files.Length)

        // The video plays for frames/fps, which is one frame period longer than the last frame's
        // own time — a frame occupies the interval that starts at its time.
        printfn "it plays for as long as the frames last: %b" (abs (float info.Duration - float info.FrameCount / info.Fps) < 1e-3)

        // The container carries nothing naming the build that wrote it, so two machines write the
        // same file. Without
        // `-fflags +bitexact` this list is `Lavf…` and `Lavc… libx264`.
        printfn "the container names no ffmpeg build: %b" (List.isEmpty info.BuildTags)

    // Same frames in, same bytes out.
    match Video.mux MuxOptions.Default sequence "video-again.mp4" with
    | MuxRan again when again.Succeeded ->
        let a = System.IO.File.ReadAllBytes run.Output
        let b = System.IO.File.ReadAllBytes again.Output
        printfn "two encodes of one sequence are byte-identical: %b" (a = b)
        System.IO.File.Delete again.Output
    | _ -> ()
