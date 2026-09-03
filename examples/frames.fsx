// Rendering an animation: one `map` over the frame times, on every core there is.
//
//   dotnet fsi examples/frames.fsx
//
// An animation is a pure function of time, so a frame is an *evaluation* rather than a step in a
// replay. Three things follow from that, and this script checks all three rather than asserting
// them:
//
//   * a frame rendered on its own is byte-identical to the same frame inside a sequence;
//   * a concurrent render is the serial one, frame for frame;
//   * the measuring a render does is bounded by the distinct strings in the shot rather than by
//     the number of frames, so four times the frames is not four times the work.
//
// It writes the frames into `frames/` and one seek check into the working directory.
#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Svg"

open Marey
open Marey.Storyboard

// ---------------------------------------------------------------------------------------------
// A first-order rise, and a zoom out of it. The zoom is what makes this a real test of the
// measuring claim: a y domain that sweeps produces tick labels the layout never saw.
// ---------------------------------------------------------------------------------------------

let t = Array.init 20 (fun i -> float i * 0.5<s>)

let settling =
    t |> Array.map (fun v -> 42.0e-3 * (1.0 - exp (-float v / 2.5)) * 1.0<m>)

let rise =
    Chart.scatter U.second U.metre t settling
    |> Chart.withTitle "A first-order rise"
    |> Chart.withYLabel "x"

let story =
    scene {
        let! curve = draw rise |> over 0.8<s>
        do! curve |> zoomToY (0.0, 0.12) |> over 1.2<s> |> ease Ease.cubicInOut
    }

let surface = Chart.size 480.0 300.0

// ---------------------------------------------------------------------------------------------
// A film is the pair a renderer needs: the scene at every instant, and the layout it is drawn
// inside. The surface is not passed again below — it is already in the shot, and a second one
// beside it would be a chance to render at a size the layout was never sized for.
//
// The measurer is memoised because that is what makes sharing one across every frame affordable.
// ---------------------------------------------------------------------------------------------

let measurer = MemoTextMeasurer TextMeasure.approx
let film = Film.ofStoryWith 32 measurer surface story

printfn "the film runs %g s on a %g x %g surface" (float film.Duration) (float film.Extent.Width) (float film.Extent.Height)

let target = FrameTarget.into "frames" ".svg"
let sequence = Frames.writeText FramePass.Concurrent SvgBackend.render target 10.0 film

printfn "wrote %d frames into %s/ as %s" sequence.Files.Length sequence.Directory sequence.Pattern

printfn "%d files in %s/ this render did not write" sequence.Stale.Length sequence.Directory

// ---------------------------------------------------------------------------------------------
// Seeking is evaluation. Frame four, rendered with nothing before it, is frame four.
// ---------------------------------------------------------------------------------------------

let instants = Frames.times 10.0 film.Duration
let alone = SvgBackend.render film.Extent (film.Frames.At instants.[4])
System.IO.File.WriteAllText("frames-seek.svg", alone)

printfn "a frame rendered alone is the frame rendered in sequence: %b" (alone = System.IO.File.ReadAllText sequence.Files.[4])

// The final instant is deliberately not a frame: a frame occupies the interval that starts at its
// own time, so a film of duration d at f frames per second is round(d * f) of them and the last
// one lands short of d.
printfn "the last frame lands short of the end: %b" (float (Array.last instants) < float film.Duration)

// ---------------------------------------------------------------------------------------------
// The two passes are the same frames. `Array.Parallel.map` places its results by index, and the
// backends are constructed inside the renderer rather than shared across it.
// ---------------------------------------------------------------------------------------------

let serial = Frames.render FramePass.Serial SvgBackend.render 10.0 film
let concurrent = Frames.render FramePass.Concurrent SvgBackend.render 10.0 film

printfn "a concurrent render is the serial one, frame for frame: %b" (serial = concurrent)

// ---------------------------------------------------------------------------------------------
// And what the cache is worth. `Measurements` is what reached the measurer underneath; `Entries`
// is what was asked. In a serial render they are equal — no string is ever measured twice.
// ---------------------------------------------------------------------------------------------

let profile fps =
    let memo = MemoTextMeasurer TextMeasure.approx
    let f = Film.ofStoryWith 32 memo surface story
    (Frames.render FramePass.Serial SvgBackend.render fps f).Length, memo

let n10, memo10 = profile 10.0
let n40, memo40 = profile 40.0

printfn "%d frames asked about %d distinct strings" n10 memo10.Entries
printfn "no string is measured twice: %b" (memo10.Measurements = memo10.Entries)

printfn "%d frames measured %d times; %d frames measured %d times" n10 memo10.Measurements n40 memo40.Measurements
printfn "four times the frames is not four times the measuring: %b" (memo40.Measurements < 2 * memo10.Measurements)
printfn "the shared measurer answered the whole film: %b" (measurer.Entries > 0)

// ---------------------------------------------------------------------------------------------
// The trap the writer reports rather than solves. A second, shorter render into the same
// directory leaves the first one's tail behind, and `ffmpeg -i frame-%05d.svg` reads until the
// first gap — so the encode would quietly gain frames from a shot that no longer exists.
//
// They are named rather than deleted: emptying a directory somebody else chose is not a
// rendering function's decision, and the report is what lets the caller make it.
// ---------------------------------------------------------------------------------------------

let shorter = Frames.writeText FramePass.Serial SvgBackend.render target 4.0 film
let leftBehind = Set.ofArray (shorter.Stale |> Array.map System.IO.Path.GetFullPath)

printfn "the shorter render wrote %d frames and reported %d left behind" shorter.Files.Length shorter.Stale.Length

printfn
    "every frame the longer render left behind is reported: %b"
    (sequence.Files.[shorter.Files.Length ..]
     |> Array.forall (fun f -> leftBehind.Contains(System.IO.Path.GetFullPath f)))

printfn "and they are still on disk rather than deleted: %b" (shorter.Stale |> Array.forall System.IO.File.Exists)
