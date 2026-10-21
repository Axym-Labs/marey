// Path morphing, and the two things about it that look like bugs the first time you see them.
//
//   dotnet fsi examples/morph.fsx
//
// A morph interpolates two shapes through a *common* form: both are flattened to polylines,
// resampled by arc length so that a point halfway along one shape meets the point halfway along
// the other, and then interpolated point by point. Arc length rather than vertex index is what
// makes a square become a triangle at a steady speed instead of racing across one edge and
// crawling along the next.
//
// Two consequences are worth seeing rather than reading about, because both look like defects
// and neither is one. They are pinned by tests and stated in the guide; this script draws them.
#r "nuget: Marey.Core"
#r "nuget: Marey.Anim"
#r "nuget: Marey.Backend.Svg"

open Marey

// ---------------------------------------------------------------------------------------------
// Shapes, in nd — the unit square, y up.
// ---------------------------------------------------------------------------------------------

let at (x: float) (y: float) : Vec2<nd> =
    { X = x * 1.0<nd>; Y = y * 1.0<nd> }

// The picture is a 3 x 2 filmstrip in a 900 x 340 surface, so each cell is 300 x 170 device px.
// `nd` is normalised per axis, so a shape that is round on screen is *wider* than it is tall in
// nd by exactly this ratio, which anyone drawing a circle by hand meets first.
let aspect = 170.0 / 300.0

/// A regular n-gon, from the angle `phase`, as a closed subpath.
let poly (n: int) (phase: float) (cx: float) (cy: float) (r: float) =
    [| for i in 0 .. n - 1 do
           let a = phase + 2.0 * System.Math.PI * float i / float n
           let p = at (cx + r * aspect * cos a) (cy + r * sin a)
           if i = 0 then yield MoveTo p else yield LineTo p
       yield ClosePath |]

/// A circle, as four cubics: the scene has no arc, only lines and Béziers.
let circle (cx: float) (cy: float) (r: float) =
    let rx = r * aspect
    let kx = 0.5522847498307936 * rx
    let ky = 0.5522847498307936 * r

    [| MoveTo(at (cx + rx) cy)
       CubicTo(at (cx + rx) (cy + ky), at (cx + kx) (cy + r), at cx (cy + r))
       CubicTo(at (cx - kx) (cy + r), at (cx - rx) (cy + ky), at (cx - rx) cy)
       CubicTo(at (cx - rx) (cy - ky), at (cx - kx) (cy - r), at cx (cy - r))
       CubicTo(at (cx + kx) (cy - r), at (cx + rx) (cy - ky), at (cx + rx) cy)
       ClosePath |]

/// Split a path into its subpaths, so each can be drawn in its own colour. Every `MoveTo`
/// starts one; a morph's output is always a polyline, so nothing else here has to be handled.
let subpaths (segs: PathSeg[]) =
    (([], []), segs)
    ||> Array.fold (fun (finished, current) seg ->
        match seg with
        | MoveTo _ when not (List.isEmpty current) -> (List.rev current :: finished, [ seg ])
        | _ -> (finished, seg :: current))
    |> fun (finished, current) -> List.rev (List.rev current :: finished) |> List.map Array.ofList

// ---------------------------------------------------------------------------------------------
// The two morphs
// ---------------------------------------------------------------------------------------------

// **A closed subpath's start point is not rotated to match its partner.** These are the same
// square twice, written from different corners. The morph pairs sample zero with sample zero, so
// the shape twists a quarter-turn on its way across rather than sitting still — watch the dot,
// which is where each ring starts. Rotating one ring to best-match the other is a real algorithm
// and is not built.
let squareA = poly 4 (System.Math.PI / 4.0) 0.5 0.5 0.34
let squareB = poly 4 (3.0 * System.Math.PI / 4.0) 0.5 0.5 0.34

// **Subpaths pair by index.** These two paths are also the *same picture* — a large disc up on
// the left, a small one down on the right — written in the opposite subpath order. Nothing should
// move, and the two ends of the morph agree that nothing did. In between, index pairing sends
// each disc to the other's place, so they cross over and trade size on the way. Matching subpaths
// by proximity or by area is a real algorithm and is not built either.
let big = circle 0.32 0.66 0.20
let small = circle 0.70 0.34 0.11
let pairA = Array.append big small
let pairB = Array.append small big

let opts = Morph.defaults

// ---------------------------------------------------------------------------------------------
// Draw each morph as a three-frame filmstrip: start, middle, end.
// ---------------------------------------------------------------------------------------------

// One colour per *subpath*, which is the whole point of the second row: you have to be able to
// see which shape ended up where.
let inks = [| Color.ofHex "#1b3a5c"; Color.ofHex "#c1580b" |]
let fractions = [| 0.0; 0.5; 1.0 |]

/// A dot at a subpath's first sample — the point the morph pairs with its partner's first
/// sample, and the reason a closed ring can twist.
let dot (paint: Color) (segs: PathSeg[]) =
    match Array.tryHead segs with
    | Some(MoveTo p) -> Path(circle (float p.X) (float p.Y) 0.030, None, Some(Solid paint), NonZero)
    | _ -> Scene.empty

let frame (a: PathSeg[]) (b: PathSeg[]) (t: float) =
    Morph.paths opts a b t
    |> subpaths
    |> List.mapi (fun k sub ->
        let paint = inks.[k % inks.Length]

        Group
            [| Path(sub, Some(Stroke.solid paint 1.8<px>), None, NonZero)
               dot paint sub |])
    |> Array.ofList
    |> Group

/// Cell `(row, col)` of a 3 x 2 grid, in nd. Squeeze into the cell, then slide to it:
/// `Affine.compose f g` is "f then g", and the IR's transform node carries the composed affine
/// rather than a closure, which is why two identically built scenes compare equal.
let cell (row: int) (col: int) (node: Node) =
    let squeeze: Affine<nd, nd> = Affine.scale (1.0 / 3.0) 0.5

    let slide: Affine<nd, nd> =
        Affine.translate
            { X = float col / 3.0 * 1.0<nd>
              Y = (1.0 - float (row + 1) / 2.0) * 1.0<nd> }

    Transform(Affine.compose squeeze slide, node)

let strip (row: int) (a: PathSeg[]) (b: PathSeg[]) =
    fractions
    |> Array.mapi (fun col t -> cell row col (frame a b t))
    |> Group

let scene =
    Group
        [| Scene.rect None (Some(Solid Color.white)) Rect.unit
           strip 0 squareA squareB
           strip 1 pairA pairB |]

let extent = Chart.size 900.0 340.0
let out = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "morph.svg")
System.IO.File.WriteAllText(out, SvgBackend.render extent scene)
printfn "wrote %s" out

// ---------------------------------------------------------------------------------------------
// The endpoints, checked rather than asserted in prose.
// ---------------------------------------------------------------------------------------------
//
// `Morph.paths a b 0.0` is not `a` — it is `a` *in the common form*, which is what `prepare`
// hands back, and it is exactly that. The still frame before a morph starts should be drawn from
// `prepare` rather than from the input, or the shape pops on the first frame.
let prepared, _ = Morph.prepare opts squareA squareB
printfn "at t = 0 the morph is the prepared source: %b" (Morph.paths opts squareA squareB 0.0 = prepared)

// And an animation is just a function of time over the same thing.
let flight = Morph.anim opts 2.0<s> pairA pairB |> Anim.ease Ease.cubicInOut
printfn "the morph as an animation lasts %.1f s" (float (Anim.duration flight))
printfn "seeking is evaluation, in any order: %b" (Anim.atFraction 0.5 flight = Anim.atFraction 0.5 flight)
