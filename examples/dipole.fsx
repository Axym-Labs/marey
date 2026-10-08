(*  A rotating dipole: a field in motion, filmed and encoded.

    Run it:  dotnet fsi examples/dipole.fsx
    Writes:  dipole-frames/ (one PNG per frame), dipole-poster.png, and dipole.mp4 when ffmpeg
             is on PATH.

    Two line charges, +q and −q, turn about their midpoint once in three seconds. Each frame
    draws the potential as a heatmap, its equipotentials, and the electric field's lines over
    them, recomputed from that instant's field.

    What stays still is the point. The panel does not move (D72), and **neither does the colour
    scale**. By default a frame's colours are drawn against the union of every sampled frame's
    range rather than its own, so one colour means one number of volts throughout the film.
    Without that, each frame would stretch its own range over the whole ramp, and a field that
    grew tenfold would look the same in every frame. This film states its range instead, for the
    reason given where the heatmap is built. *)

#r "nuget: Marey.Fields"
#r "nuget: Marey.Backend.Skia"
#r "nuget: Marey.Toolchain"
#r "nuget: Marey.Tools.Video"

open Marey
open Marey.UnitsOfMeasure

let period = 3.0<s>
let separation = 0.5<m>

// The potential of +q and −q at ±(d/2)(cos θ, sin θ), in volts: ln(r₋ / r₊) for unit line charges.
let potential (t: float<s>) : Scalar2<m, V> =
    let a = 2.0 * System.Math.PI * float (t / period)
    let cx, cy = separation / 2.0 * cos a, separation / 2.0 * sin a

    fun p ->
        let plus = Vec2.norm { X = p.X - cx; Y = p.Y - cy }
        let minus = Vec2.norm { X = p.X + cx; Y = p.Y + cy }
        (log (float minus) - log (float plus)) * 1.0<V>

// An even count, so no node lands on a charge.
let xs = Grid.linear -1.6<m> 1.6<m> 96
let ys = Grid.linear -1.0<m> 1.0<m> 60

let theme = Theme.initial
let fieldUnit = U.div Derived.volt U.metre

let figureAt (t: float<s>) =
    let phi = Field.sampleScalar xs ys (potential t)
    let e = Field.grad phi |> fun g -> { g with U = Array.map (~-) g.U; W = Array.map (~-) g.W }
    let levels = [| -1.5 .. 0.5 .. 1.5 |] |> Array.map (fun v -> v * 1.0<V>)

    // The potential is singular at each charge, so the union of the frames' ranges is set by
    // whichever grid nodes land nearest one — ±5 V here — and the field around them is drawn in
    // the ramp's pale middle. A stated domain says what the colours should span instead.
    Field.heatmapWith theme.Diverging true U.metre U.metre Derived.volt phi
    |> Chart.withColorDomain Derived.volt -2.0<V> 2.0<V>
    |> Chart.withColorLabel "φ"
    |> Chart.overlay (Field.contoursAt levels U.metre U.metre phi)
    |> Chart.overlay (Field.streamlinesWith 20.0<px> U.metre U.metre e)
    |> Chart.withYLabel "y"
    |> Chart.withTitle "A rotating dipole"

let anim = Anim.ofSeconds period figureAt
let extent = Chart.size 560.0 380.0
let measurer = MemoTextMeasurer TextMeasure.approx

// D72's union, taken over 32 sampled instants: the panel's gutters and the colour domain. Every
// frame states the same domain, so the union is that domain.
let clock = System.Diagnostics.Stopwatch.StartNew()
let film = Film.ofFiguresWith 32 measurer extent anim
let arranging = clock.Elapsed.TotalMilliseconds

let lo, hi = film.Shot.ColourDomains |> Array.exactlyOne |> snd
printfn "the colour scale is pinned to %.3f V .. %.3f V for the whole film" lo hi

clock.Restart()
let fps = 20.0
let target = FrameTarget.into "dipole-frames" ".png"
let sequence = film |> Frames.write FramePass.Concurrent SkiaBackend.renderPng target fps
let rendering = clock.Elapsed.TotalMilliseconds

printfn "wrote %d frames into %s/ as %s" sequence.Files.Length sequence.Directory sequence.Pattern
printfn "arranging the shot took %.0f ms; rendering the frames, %.0f ms" arranging rendering
System.IO.File.Copy(sequence.Files.[sequence.Files.Length / 3], "dipole-poster.png", true)

// The claims. Every frame has the same words in it, the colorbar's labels among them, so the
// scale did not move; and the first and a later frame differ, so the field did.
let rec texts (n: Node) : string list =
    match n with
    | Text t -> [ t.Text ]
    | Group ns -> ns |> Array.toList |> List.collect texts
    | Transform(_, c)
    | Clip(_, c)
    | Keyed(_, c)
    | Annotated(_, c)
    | Opacity(_, c) -> texts c
    | _ -> []

let times = Frames.times fps film.Duration
let first = texts (film.Frames.At times.[0])
printfn "every frame has the same labels, colorbar included: %b" (times |> Array.forall (fun t -> texts (film.Frames.At t) = first))
printfn "the field turns between frames: %b" (System.IO.File.ReadAllBytes sequence.Files.[0] <> System.IO.File.ReadAllBytes sequence.Files.[10])

match Video.mux MuxOptions.Default sequence "dipole.mp4" with
| MuxUnavailable why ->
    printfn "%s" why
    printfn "the frames are still on disk: %b" (sequence.Files |> Array.forall System.IO.File.Exists)
| MuxRefused why -> printfn "refused: %s" why
| MuxTimedOut why -> printfn "timed out: %s" why
| MuxRan run when not run.Succeeded -> printfn "ffmpeg exited %d\n%s" run.ExitCode run.Log
| MuxRan run -> printfn "encoded %s, %d bytes" (System.IO.Path.GetFileName run.Output) (System.IO.FileInfo run.Output).Length
