/// **path morphing**, and the three policies that stop it being an `Interp`
/// instance.
///
/// The laws split into two kinds and it is worth saying which is which before reading them.
///
/// The **endpoint laws** are stated against `Morph.prepare`, not against the input paths, and
/// that is a correction to the plan rather than a weakening. `m3-plan.md` §3 asks for
/// `morph a b 0.0 ≡ a` structurally; that cannot hold and should not. A morph interpolates two
/// paths in a *common* form — same subpath count, same sample count, same closures — and
/// reaching that form flattens curves and can add a subpath that one side did not have. At
/// `t = 0` the honest answer is the prepared `a`, and `prepare` is public precisely so a caller
/// can draw it for the still frame and have the two agree exactly.
///
/// Stating it that way is also what makes the law bite. `paths a b 0.0 = fst (prepare a b)` is
/// false the moment the pairing depends on `t`, the moment the sampler is fed the wrong side, or
/// the moment padding is applied to one path and not the other — all of which a law quantified
/// over `a` alone would let through.
///
/// The **zero end is exact and the one end is not**, and the asymmetry is arithmetic rather than
/// sloppiness: `x + (y - x) * 0` is `x` plus a true zero, while `x + (y - x) * 1` recovers `y`
/// only to a few ulps of the larger endpoint. `InterpProperties` states the same thing about the
/// witness itself. `Tol.panel` here, because these are panel-space coordinates and a billionth
/// of the panel is below any rendering resolution.
module Marey.Tests.MorphProperties

open FsCheck.Xunit
open Xunit
open Marey
open Marey.Tests.Generators

// ======================================================================================
// SHARED HELPERS
// ======================================================================================

/// The segment kinds, in order, with the coordinates dropped. Two paths with the same shape can
/// be compared point for point; two without it are not the same morph at all.
let private shape (p: PathSeg[]) =
    p
    |> Array.map (function
        | MoveTo _ -> 'M'
        | LineTo _ -> 'L'
        | CubicTo _ -> 'C'
        | ClosePath -> 'Z')
    |> System.String

let private points (p: PathSeg[]) =
    p
    |> Array.choose (function
        | MoveTo v
        | LineTo v -> Some v
        | CubicTo(_, _, v) -> Some v
        | ClosePath -> None)

let private nearPath (tol: float) (a: PathSeg[]) (b: PathSeg[]) =
    shape a = shape b
    && Array.forall2 (Approx.vec2 tol) (points a) (points b)

let private finitePath (p: PathSeg[]) =
    points p
    |> Array.forall (fun v ->
        let ok (x: float) =
            not (System.Double.IsNaN x || System.Double.IsInfinity x)

        ok (float v.X) && ok (float v.Y))

let private subpathCount (p: PathSeg[]) =
    p
    |> Array.sumBy (function
        | MoveTo _ -> 1
        | _ -> 0)

/// A closed unit square with a corner at `(x, y)`, drawn anticlockwise from that corner.
let private square (x: float) (y: float) =
    let v a b : Vec2<nd> = { X = a * 1.0<nd>; Y = b * 1.0<nd> }

    [| MoveTo(v x y)
       LineTo(v (x + 1.0) y)
       LineTo(v (x + 1.0) (y + 1.0))
       LineTo(v x (y + 1.0))
       ClosePath |]

let private line (x0: float) (x1: float) =
    let v a : Vec2<nd> = { X = a * 1.0<nd>; Y = 0.0<nd> }
    [| MoveTo(v x0); LineTo(v x1) |]

// ======================================================================================

/// The common form: same subpath count on both sides, same sample count in every subpath, same
/// closures, and no curves left anywhere.
[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 300, Replay = Seed, QuietOnSuccess = true)>]
module Prepared =

    [<Property>]
    let ``both sides come out the same shape`` (MorphPath a) (MorphPath b) =
        let x, y = Morph.prepare Morph.defaults a b
        shape x = shape y

    [<Property>]
    let ``every frame has that shape too`` (MorphPath a) (MorphPath b) (UnitT t) =
        let x, _ = Morph.prepare Morph.defaults a b
        shape (Morph.paths Morph.defaults a b t) = shape x

    /// A morph between two curves is not a curve. An arc-length correspondence between two
    /// arbitrary cubics has no cubic answer, and fitting one would mean calling a residual zero.
    [<Property>]
    let ``a morph is a polyline`` (MorphPath a) (MorphPath b) (UnitT t) =
        Morph.paths Morph.defaults a b t
        |> Array.forall (function
            | CubicTo _ -> false
            | _ -> true)

    /// `Samples` is a promise about the output, not a hint. Every subpath carries exactly that
    /// many points, whichever side it came from and whether or not it was padded into existence.
    [<Property>]
    let ``every subpath carries exactly Samples points`` (MorphPath a) (MorphPath b) =
        let opts = { Morph.defaults with Samples = 12 }
        let got = Morph.paths opts a b 0.5
        let drawn = points got

        drawn.Length = 12 * subpathCount got

    [<Property>]
    let ``the subpath count is the larger of the two`` (MorphPath a) (MorphPath b) =
        let expected =
            max (subpathCount (Morph.paths Morph.defaults a a 0.0)) (subpathCount (Morph.paths Morph.defaults b b 0.0))

        subpathCount (Morph.paths Morph.defaults a b 0.5) = expected

    [<Fact>]
    let ``options that cannot be sampled are refused`` () =
        let a = square 0.0 0.0

        Assert.Throws<System.ArgumentException>(fun () ->
            Morph.paths { Morph.defaults with Samples = 1 } a a 0.5 |> ignore)
        |> ignore

        Assert.Throws<System.ArgumentException>(fun () ->
            Morph.paths { Morph.defaults with Flatten = 0 } a a 0.5 |> ignore)
        |> ignore

// ======================================================================================

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 300, Replay = Seed, QuietOnSuccess = true)>]
module Endpoints =

    /// **Exact.** Not to a tolerance: `interp x y 0.0` is `x + (y - x) * 0.0`, and the second
    /// term is a true zero for every finite pair.
    [<Property>]
    let ``at zero a morph is the prepared source, exactly`` (MorphPath a) (MorphPath b) =
        Morph.paths Morph.defaults a b 0.0 = fst (Morph.prepare Morph.defaults a b)

    /// Not exact, and it cannot be — see the module header. Shape still exact, though: only the
    /// coordinates move, and only by ulps.
    [<Property>]
    let ``at one a morph is the prepared target, to within an ulp`` (MorphPath a) (MorphPath b) =
        nearPath Tol.panel (Morph.paths Morph.defaults a b 1.0) (snd (Morph.prepare Morph.defaults a b))

    /// An open subpath keeps its own two ends. The resampler takes them from the source rather
    /// than interpolating towards them, so this is exact — which matters, because the first
    /// thing anyone checks after a morph is whether the shape still starts where it started.
    [<Fact>]
    let ``an open subpath keeps its own ends`` () =
        let a = line 0.0 4.0
        let got, _ = Morph.prepare Morph.defaults a a
        let ps = points got

        Assert.Equal(0.0, float ps.[0].X)
        Assert.Equal(4.0, float ps.[ps.Length - 1].X)

// ======================================================================================

/// The arc-length correspondence itself. These are the tests that fail if the sampler stops
/// measuring distance and starts counting vertices.
module Geometry =

    /// Even spacing, stated where it is exactly checkable: five samples along a unit segment are
    /// the quarters, to the last bit.
    [<Fact>]
    let ``samples are evenly spaced by arc length`` () =
        let got, _ = Morph.prepare { Morph.defaults with Samples = 5 } (line 0.0 1.0) (line 0.0 1.0)
        let xs = points got |> Array.map (fun v -> float v.X)

        Assert.Equal<float[]>([| 0.0; 0.25; 0.5; 0.75; 1.0 |], xs)

    /// **The one that separates arc length from vertex index.** Two segments of length 1 and 3;
    /// five samples land on the whole numbers. A sampler that walked the vertex list instead
    /// would put three of the five in the first quarter of the shape — and would still pass the
    /// two tests above, because a unit segment and an equal-sided square cannot tell the
    /// difference.
    [<Fact>]
    let ``samples follow the distance, not the vertex list`` () =
        let v a : Vec2<nd> = { X = a * 1.0<nd>; Y = 0.0<nd> }
        let uneven = [| MoveTo(v 0.0); LineTo(v 1.0); LineTo(v 4.0) |]

        let got, _ = Morph.prepare { Morph.defaults with Samples = 5 } uneven uneven
        let xs = points got |> Array.map (fun p -> float p.X)

        Assert.Equal<float[]>([| 0.0; 1.0; 2.0; 3.0; 4.0 |], xs)

    /// **A vertex count would pass this by accident and an arc length would not.** The square's
    /// four edges are equal, so uniform-by-vertex and uniform-by-length agree on the corners;
    /// what they disagree about is everything between, and every sample being *on* the boundary
    /// is what says the parameterisation followed the path rather than the vertex list.
    [<Fact>]
    let ``every sample of a square lies on the square`` () =
        let got, _ = Morph.prepare Morph.defaults (square 0.0 0.0) (square 0.0 0.0)

        for v in points got do
            let x = float v.X
            let y = float v.Y
            Assert.InRange(x, -1e-12, 1.0 + 1e-12)
            Assert.InRange(y, -1e-12, 1.0 + 1e-12)
            // Distance to the nearest edge, which is zero exactly on the boundary.
            Assert.True(min (min x (1.0 - x)) (min y (1.0 - y)) <= 1e-12, sprintf "(%g, %g) is not on the square" x y)

    /// The documented cost: a closed subpath's last sample is its first one again, because the
    /// sampler traces the closing edge and lands back where it started.
    [<Fact>]
    let ``a closed subpath ends where it began`` () =
        let got, _ = Morph.prepare Morph.defaults (square 0.0 0.0) (square 0.0 0.0)
        let ps = points got

        Assert.Equal(float ps.[0].X, float ps.[ps.Length - 1].X, 12)
        Assert.Equal(float ps.[0].Y, float ps.[ps.Length - 1].Y, 12)

    /// A cubic whose control points are collinear and evenly spaced *is* the straight line, so
    /// flattening plus arc-length resampling has an answer that can be written down. This gates
    /// the Bernstein evaluation and the sampler at once.
    [<Fact>]
    let ``a straight cubic flattens to a straight line`` () =
        let v a : Vec2<nd> = { X = a * 1.0<nd>; Y = 0.0<nd> }

        let curve =
            [| MoveTo(v 0.0)
               CubicTo(v (1.0 / 3.0), v (2.0 / 3.0), v 1.0) |]

        let got, _ = Morph.prepare { Morph.defaults with Samples = 5 } curve curve

        for i, p in points got |> Array.indexed do
            Assert.Equal(float i / 4.0, float p.X, 12)
            Assert.Equal(0.0, float p.Y, 12)

    /// `Flatten` is the resolution of the curve, and this is what it buys. The cubic below bulges
    /// three-quarters of a unit off its own chord; at one segment per cubic the flattening *is*
    /// the chord and the bulge is gone, at thirty-two it is there.
    [<Fact>]
    let ``Flatten decides how closely a curve is followed`` () =
        let v a b : Vec2<nd> = { X = a * 1.0<nd>; Y = b * 1.0<nd> }

        let arch =
            [| MoveTo(v 0.0 0.0)
               CubicTo(v 0.0 1.0, v 1.0 1.0, v 1.0 0.0) |]

        let highest (steps: int) =
            let got, _ = Morph.prepare { Morph.defaults with Flatten = steps } arch arch
            points got |> Array.map (fun p -> float p.Y) |> Array.max

        Assert.Equal(0.0, highest 1, 12)
        Assert.InRange(highest 32, 0.7, 0.8)

    /// A cubic's own endpoint survives the flattening exactly — the last of `Flatten` steps is
    /// `u = 1`, whose Bernstein weights are `(0, 0, 0, 1)`.
    [<Fact>]
    let ``a cubic ends exactly where it was told to`` () =
        let v a b : Vec2<nd> = { X = a * 1.0<nd>; Y = b * 1.0<nd> }

        let curve =
            [| MoveTo(v 0.0 0.0)
               CubicTo(v 0.0 1.0, v 1.0 1.0, v 1.0 0.25) |]

        let got, _ = Morph.prepare Morph.defaults curve curve
        let ps = points got

        Assert.Equal(1.0, float ps.[ps.Length - 1].X, 12)
        Assert.Equal(0.25, float ps.[ps.Length - 1].Y, 12)

// ======================================================================================

/// The first of D74's three policies: what happens to a subpath with no partner.
module Subpaths =

    let private centroidOf (ps: Vec2<nd>[]) =
        let n = float ps.Length
        (ps |> Array.sumBy (fun p -> float p.X)) / n, (ps |> Array.sumBy (fun p -> float p.Y)) / n

    let private subpaths (p: PathSeg[]) =
        let out = ResizeArray<ResizeArray<Vec2<nd>>>()

        for seg in p do
            match seg with
            | MoveTo v ->
                out.Add(ResizeArray())
                out.[out.Count - 1].Add v
            | LineTo v -> out.[out.Count - 1].Add v
            | CubicTo(_, _, v) -> out.[out.Count - 1].Add v
            | ClosePath -> ()

        out |> Seq.map (fun r -> r.ToArray()) |> Seq.toArray

    /// An unmatched subpath grows out of a point **of its own**, not out of the other path. A
    /// second marker appearing in a chart should bloom where it belongs, not fly in from
    /// wherever the first one happens to sit.
    [<Fact>]
    let ``an extra subpath grows from a point of its own`` () =
        let a = square 0.0 0.0
        let b = Array.append (square 0.0 0.0) (square 5.0 5.0)

        let start = Morph.paths Morph.defaults a b 0.0
        let extra = (subpaths start).[1]

        Assert.All(extra, fun p -> Assert.Equal(float extra.[0].X, float p.X, 12))
        Assert.All(extra, fun p -> Assert.Equal(float extra.[0].Y, float p.Y, 12))

        // Its own, not the other path's — and its *middle*, not its corner. The tighter range is
        // the difference between `GrowFromCentroid` and `GrowFromStart`, which would both land
        // somewhere inside the square and only one of which is what was asked for. (Not exactly
        // 5.5: the ring's duplicated corner biases the mean by about 0.004 towards it.)
        Assert.InRange(float extra.[0].X, 5.45, 5.55)
        Assert.InRange(float extra.[0].Y, 5.45, 5.55)

        // And by the end it is the square it was always going to be.
        let finish = Morph.paths Morph.defaults a b 1.0
        let grown = (subpaths finish).[1]
        let cx, cy = centroidOf grown
        Assert.Equal(5.5, cx, 1)
        Assert.Equal(5.5, cy, 1)

    /// `GrowFromStart` puts the collapsed point exactly at the subpath's pen-down corner, which
    /// is the choice for a stroke that should look drawn rather than revealed.
    [<Fact>]
    let ``GrowFromStart collapses to the pen, not the middle`` () =
        let a: PathSeg[] = [||]
        let b = square 5.0 5.0

        let opts = { Morph.defaults with Padding = GrowFromStart }
        let start = Morph.paths opts a b 0.0
        let ps = points start

        Assert.All(ps, fun p -> Assert.Equal(5.0, float p.X, 12))
        Assert.All(ps, fun p -> Assert.Equal(5.0, float p.Y, 12))

    /// **Subpaths are paired by index, and this is the pin for it.** Two squares listed in the
    /// opposite order morph by crossing over, and both output subpaths meet in the middle. It is
    /// documented rather than fixed: pairing by proximity is unstable under exactly the small
    /// motions an animation is made of.
    [<Fact>]
    let ``subpaths are paired by index, so a reordered path crosses over`` () =
        let a = Array.append (square 0.0 0.0) (square 8.0 0.0)
        let b = Array.append (square 8.0 0.0) (square 0.0 0.0)

        let mid = Morph.paths Morph.defaults a b 0.5

        for sub in subpaths mid do
            let cx, _ = centroidOf sub
            Assert.Equal(4.5, cx, 1)

// ======================================================================================

/// The second policy: closed against open.
module Closure =

    let private closes (p: PathSeg[]) =
        p
        |> Array.sumBy (function
            | ClosePath -> 1
            | _ -> 0)

    [<Fact>]
    let ``closed with closed stays closed`` () =
        Assert.Equal(1, closes (Morph.paths Morph.defaults (square 0.0 0.0) (square 3.0 0.0) 0.5))

    /// The safe direction, and the default. Nothing is added that was not there — and nothing is
    /// lost either, because the sampler still traces the square's closing edge, so the opened
    /// square is drawn over its whole outline and ends back at its first corner.
    [<Fact>]
    let ``closed with open opens, and still traces the closing edge`` () =
        let got = Morph.paths Morph.defaults (square 0.0 0.0) (line 0.0 4.0) 0.0

        Assert.Equal(0, closes got)

        let ps = points got
        Assert.Equal(float ps.[0].X, float ps.[ps.Length - 1].X, 12)
        Assert.Equal(float ps.[0].Y, float ps.[ps.Length - 1].Y, 12)

    /// The other direction, which adds an edge the open path did not have. Available because a
    /// caller occasionally wants it; never reached by accident.
    [<Fact>]
    let ``CloseWhenEither closes the pair`` () =
        let opts = { Morph.defaults with Closure = CloseWhenEither }
        Assert.Equal(1, closes (Morph.paths opts (square 0.0 0.0) (line 0.0 4.0) 0.5))

    /// A subpath with no partner keeps its own closure: a policy applied to a path and its own
    /// shadow is that path.
    [<Fact>]
    let ``an unmatched closed subpath stays closed`` () =
        let a = square 0.0 0.0
        let b = Array.append (square 0.0 0.0) (square 5.0 5.0)
        Assert.Equal(2, closes (Morph.paths Morph.defaults a b 0.5))

// ======================================================================================

/// D71 reaches here. `Morph` interpolates through the witness, and the witness does not clamp.
module Overshoot =

    [<Fact>]
    let ``a morph overshoots its target`` () =
        let got = Morph.paths Morph.defaults (line 0.0 0.0) (line 0.0 10.0) 1.25
        let ps = points got

        Assert.Equal(0.0, float ps.[0].X, 12)
        Assert.Equal(12.5, float ps.[ps.Length - 1].X, 12)

    [<Fact>]
    let ``a morph undershoots below zero`` () =
        let got = Morph.paths Morph.defaults (line 0.0 0.0) (line 0.0 10.0) -0.25
        let ps = points got

        Assert.Equal(-2.5, float ps.[ps.Length - 1].X, 12)

// ======================================================================================

/// `anim` hoists the preparation out of the frame loop. That is the whole reason it exists and
/// the whole risk of having it, so the two are held to exact agreement.
[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 300, Replay = Seed, QuietOnSuccess = true)>]
module Frames =

    [<Property>]
    let ``the prepared morph and the naive one agree, frame for frame`` (MorphPath a) (MorphPath b) (UnitT t) =
        let anim = Morph.anim Morph.defaults 2.0<s> a b
        Anim.atFraction t anim = Morph.paths Morph.defaults a b t

    /// Including past the ends, where the preparation could plausibly have been rescaled.
    [<Theory>]
    [<InlineData(1.25)>]
    [<InlineData(-0.25)>]
    let ``they agree past the ends too`` (t: float) =
        let a = square 0.0 0.0
        let b = Array.append (square 2.0 2.0) (line 0.0 4.0)

        Assert.Equal<PathSeg[]>(Morph.paths Morph.defaults a b t, Anim.atFraction t (Morph.anim Morph.defaults 2.0<s> a b))

    [<Fact>]
    let ``a morph animation has the duration it was given`` () =
        let anim = Morph.anim Morph.defaults 3.0<s> (square 0.0 0.0) (square 1.0 1.0)
        Assert.Equal(3.0, float (Anim.duration anim), 12)

// ======================================================================================

/// The paths a caller should not be able to break this with. Two of these are decisions rather
/// than defences, and they are named as such.
[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Degenerate =

    [<Fact>]
    let ``two empty paths morph to nothing`` () =
        Assert.Empty(Morph.paths Morph.defaults [||] [||] 0.5)

    [<Fact>]
    let ``an empty path is a shape that has not appeared yet`` () =
        let b = square 5.0 5.0
        let start = Morph.paths Morph.defaults [||] b 0.0
        let ps = points start

        Assert.Equal(Morph.defaults.Samples, ps.Length)
        Assert.All(ps, fun p -> Assert.Equal(float ps.[0].X, float p.X, 12))

        Assert.True(nearPath Tol.panel (Morph.paths Morph.defaults [||] b 1.0) (snd (Morph.prepare Morph.defaults [||] b)))

    /// A lone `MoveTo` is a subpath of zero length. The sampler answers with copies of its only
    /// point rather than dividing by that length.
    [<Fact>]
    let ``a lone MoveTo is a point`` () =
        let dot: PathSeg[] = [| MoveTo { X = 2.0<nd>; Y = 3.0<nd> } |]
        let ps = points (Morph.paths Morph.defaults dot dot 0.5)

        Assert.Equal(Morph.defaults.Samples, ps.Length)
        Assert.All(ps, fun p -> Assert.Equal(2.0, float p.X, 12))
        Assert.All(ps, fun p -> Assert.Equal(3.0, float p.Y, 12))

    /// **A decision, not a defence.** A path whose first segment is not a `MoveTo` is malformed;
    /// SVG says rendering stops, Skia says the current point is the origin. Skia's answer is one
    /// line and never throws, so it is the one taken — and it is pinned here so that "total on a
    /// malformed path" cannot quietly become "empty on a malformed path".
    [<Fact>]
    let ``a segment before the first MoveTo starts at the origin`` () =
        let stray: PathSeg[] = [| LineTo { X = 1.0<nd>; Y = 0.0<nd> } |]
        let ps = points (Morph.paths Morph.defaults stray stray 0.0)

        Assert.Equal(0.0, float ps.[0].X, 12)
        Assert.Equal(1.0, float ps.[ps.Length - 1].X, 12)

    /// The other half of the same decision: a `ClosePath` with nothing open closes nothing.
    [<Fact>]
    let ``a ClosePath with nothing open is ignored`` () =
        let stray: PathSeg[] = [| ClosePath; ClosePath |]
        Assert.Empty(Morph.paths Morph.defaults stray stray 0.5)

    /// The free generator, including every malformed sequence it can produce. One law, and it is
    /// the one that matters: a renderer walking an animation must not meet an exception or a NaN
    /// halfway through a frame.
    [<Property>]
    let ``a morph is total on any path`` (RawPath a) (RawPath b) (UnitT t) =
        let got = Morph.paths Morph.defaults a b t
        finitePath got && shape got = shape (fst (Morph.prepare Morph.defaults a b))
