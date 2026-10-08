// The storyboard block: an animation written as a sequence of things happening.
//
//   dotnet fsi examples/storyboard.fsx
//
// A `scene { … }` block is a value. It measures nothing, renders nothing, and no time passes
// until it is asked for a frame — which is the whole difference between this and a Manim scene,
// and the reason a frame can be produced at any instant, in any order, in parallel.
//
// This script writes a six-frame filmstrip of one storyboard and prints four claims about it.
#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Svg"

open Marey
open Marey.UnitsOfMeasure
open Marey.Storyboard

// ---------------------------------------------------------------------------------------------
// The data: a decaying oscillation, and the envelope it is settling onto.
//
// In metres, like every other example here: a prefix is runtime metadata rather than a measure,
// so the axis chooses millimetres for itself from the numbers.
// ---------------------------------------------------------------------------------------------

let t = Array.init 28 (fun i -> float i * 0.25<s>)

let displacement =
    t |> Array.map (fun v -> 40.0e-3 * exp (-0.25 * float v) * cos (2.2 * float v) * 1.0<m>)

let envelopeOf = t |> Array.map (fun v -> 40.0e-3 * exp (-0.25 * float v) * 1.0<m>)

let measured =
    Chart.scatter U.second U.metre t displacement
    |> Chart.withTitle "A ringdown, settling"
    |> Chart.withYLabel "x"

let envelope =
    Chart.scatter U.second U.metre t envelopeOf
    |> Chart.withTitle "A ringdown, settling"
    |> Chart.withYLabel "x"

// The point the storyboard picks out at the end: the crest of the second lobe, a full period in.
// Found in the data rather than counted off the picture, which is the point of addressing a mark
// by its **row** — the row is a fact about the series, and the position among the drawn marks is
// a fact about what happened to be finite.
let crest =
    displacement
    |> Array.mapi (fun i v -> i, float v)
    |> Array.filter (fun (i, _) -> float t.[i] > 2.0)
    |> Array.maxBy snd
    |> fst

// ---------------------------------------------------------------------------------------------
// The storyboard.
// ---------------------------------------------------------------------------------------------

let ringdown =
    scene {
        let! trace = draw measured |> over 1.0<s>
        do! wait 0.4<s>

        // Two things at once. The duration is the longer branch, and a shorter one would hold
        // its final value through the tail rather than running off its own schedule.
        do!
            parallel'
                [ trace |> morphTo envelope |> over 1.6<s> |> ease Ease.cubicInOut
                  trace |> zoomToY (0.0, 45.0e-3) |> over 1.6<s> |> ease Ease.cubicInOut ]

        // No `over`: the step runs for as long as `trace` took to arrive, which is why a handle
        // carries a duration at all.
        do! highlight trace.[crest]
    }

printfn "the storyboard runs for %g s" (float (Storyboard.duration ringdown))

// ---------------------------------------------------------------------------------------------
// Rendering: one arrangement for the whole shot.
// ---------------------------------------------------------------------------------------------

let extent = Chart.size 320.0 220.0
let anim = animate 32 extent ringdown

// Sampled from just after the reveal starts rather than from `0.0`: the first instant of a fade
// is a blank surface, which is correct and is a wasted cell in a filmstrip.
let fractions = [| 0.1; 0.28; 0.46; 0.64; 0.82; 1.0 |]
let frames = fractions |> Array.map (fun u -> anim.At(u * anim.Duration))

// The plot rectangle is the panel's `Transform`. Collected across every frame and de-duplicated
// it should be *one* rectangle: the layout is sized once against the union of the whole shot, so
// the panel holds still while the data moves. Laid out per frame it would crawl, because the y
// labels gain and lose a digit as the domain changes.
let plotRect (n: Node) =
    n
    |> Scene.fold
        (fun acc x ->
            match x with
            | Transform(a, _) -> a :: acc
            | _ -> acc)
        []

printfn
    "the panel holds still across all %d frames: %b"
    frames.Length
    ((frames |> Array.map plotRect |> Array.distinct).Length = 1)

// The mark the storyboard named has a node of its own by the end, and did not before the
// highlight began. Nothing splits a batch until something is addressed.
let markKey = Keys.mark (Keys.layer (Keys.panel 0 0) 0) crest

printfn
    "mark %d is unaddressed at the start and addressed at the end: %b"
    crest
    ((Scene.tryFindKey markKey frames.[0]).IsNone
     && (Scene.tryFindKey markKey frames.[frames.Length - 1]).IsSome)

// Seeking is evaluation: an instant is a direct evaluation, not a replay, so asking twice gives
// the same scene and asking out of order costs nothing.
printfn "seeking is evaluation: %b" (anim.At 1.7<s> = anim.At 1.7<s>)

// ---------------------------------------------------------------------------------------------
// The filmstrip: three columns, two rows, each cell one frame of the storyboard.
// ---------------------------------------------------------------------------------------------

let cell (row: int) (col: int) (node: Node) =
    let squeeze: Affine<nd, nd> = Affine.scale (1.0 / 3.0) 0.5

    let slide: Affine<nd, nd> =
        Affine.translate
            { X = float col / 3.0 * 1.0<nd>
              Y = (1.0 - float (row + 1) / 2.0) * 1.0<nd> }

    Transform(Affine.compose squeeze slide, node)

let strip =
    Group
        [| Scene.rect None (Some(Solid Color.white)) Rect.unit
           yield! frames |> Array.mapi (fun i f -> cell (i / 3) (i % 3) f) |]

let out =
    System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "storyboard.svg")

System.IO.File.WriteAllText(out, SvgBackend.render (Chart.size 960.0 440.0) strip)
printfn "wrote %s" out
