namespace Marey

open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

/// What happens when one path has more subpaths than the other.
///
/// There is no correct answer, only a choice that has to be made somewhere, and D74's argument
/// is that a `static member Interp(a, b, t)` has nowhere to put it. Both cases here collapse the
/// unmatched subpath to a *point of its own*, so an extra shape grows out of itself and shrinks
/// back into itself rather than flying in from wherever the other path happens to be.
type SubpathPadding =
    /// The mean of the subpath's samples. The shape blooms from its middle — right for a marker,
    /// a blob, anything convex.
    | GrowFromCentroid
    /// The subpath's own first point. The shape unrolls from where its pen starts — right for a
    /// stroke that is drawn rather than revealed.
    | GrowFromStart

/// What a morph between a closed subpath and an open one produces.
///
/// The result cannot be both, and it cannot change partway without popping, so it is decided
/// once for the pair.
type ClosurePolicy =
    /// Closed only when *both* sides are. The safe direction: a closed source still traces its
    /// closing edge, because the sampler includes it in the arc length, so the drawn shape is
    /// the same modulo the join at the seam. Nothing is added that was not there.
    | CloseWhenBoth
    /// Closed when *either* side is. Adds a closing edge to whichever side lacked one, which is
    /// occasionally what a caller wants and is never what they get by accident.
    | CloseWhenEither

/// The three policies D74 says an `Interp` instance has nowhere to hold.
[<NoComparison>]
type MorphOptions =
    { /// Points per subpath, per side. The output is a polyline, so this is the resolution of
      /// the morph — 64 holds a circle to about 0.06% of its radius.
      Samples: int
      /// Line segments per cubic, when flattening to the common parameterisation.
      Flatten: int
      Padding: SubpathPadding
      Closure: ClosurePolicy }

/// §7.1 / D74 — **path morphing is a function, not an `Interp` instance.**
///
/// `interp a b t` on a `PathSeg[]` is not well defined without three policies: how subpaths are
/// paired when the counts differ, how many samples the common parameterisation gets, and what a
/// closed path morphing into an open one produces. A static `Interp(a, b, t)` has nowhere to put
/// them, so making paths a witness instance would mean hard-coding three answers into a
/// signature that cannot express that it made a choice. Hence `MorphOptions`, and hence an
/// ordinary function here rather than a seventh overload in `Marey.Core`.
///
/// ### What a morph actually is
///
/// Both paths are flattened to polylines (D11 keeps this cheap: one curve kind, `CubicTo`, plus
/// lines and closes — no arcs and no quadratics to special-case), split into subpaths, and each
/// subpath is resampled to `Samples` points spaced evenly **by arc length**. Two paths in that
/// form have the same shape, so they can be interpolated point by point.
///
/// **The output is therefore a polyline, always** — a morph between two curves is not a curve.
/// That is not a limitation being apologised for: an arc-length correspondence between two
/// arbitrary cubics does not have a cubic answer, and pretending otherwise would mean fitting
/// curves to sampled points and calling the residual zero.
///
/// ### Three costs, stated
///
/// * **A closed subpath's last sample repeats its first.** The sampler traces the closing edge
///   (or a closed square would be morphed over three of its four sides) and lands the last
///   sample back at the start, so the emitted path ends with a zero-length segment before
///   `ClosePath`. One point in `Samples`, invisible in every renderer, and the alternative is
///   two sampling rules that have to agree.
/// * **Subpaths are paired by index, not by position.** A path whose subpaths are listed in a
///   different order morphs by crossing them over. Pairing by proximity is a better default and
///   a worse *policy* — it is unstable under small motions, which is exactly what an animation
///   is made of — so the ordering stays the caller's, where it is at least predictable.
/// * **The start point of a closed subpath is not rotated to match.** A circle morphing into a
///   square whose first vertex is a quarter-turn away twists as it goes. Correcting that is a
///   ring alignment, it is O(Samples²) done honestly, and it is a fourth policy; it is not here
///   and the guide says so.
///
/// ### `paths` versus `anim`
///
/// `paths` prepares both sides on **every call**, which is right for a one-off and wrong for a
/// render: at 60fps it re-flattens, re-splits and re-resamples both paths sixty times a second
/// to produce the same two point arrays. `anim` prepares once and closes over the result. They
/// agree exactly, and a test says so — that equality is the whole risk of keeping the fast one.
[<RequireQualifiedAccess>]
module Morph =

    /// 64 samples and 16 segments per cubic. Both are resolutions, not tolerances: doubling them
    /// costs linear time and buys linear accuracy, and 64 is where a circle stops looking like a
    /// polygon at plausible figure sizes.
    let defaults =
        { Samples = 64
          Flatten = 16
          Padding = GrowFromCentroid
          Closure = CloseWhenBoth }

    /// A flattened subpath: the polyline the pen actually traces, and whether it comes back.
    [<NoComparison>]
    type private Sub = { Points: Vec2<nd>[]; Closed: bool }

    let private origin: Vec2<nd> = { X = 0.0<nd>; Y = 0.0<nd> }

    /// Split into subpaths and flatten every cubic to `steps` line segments.
    ///
    /// Total on any `PathSeg[]`, including the malformed ones a generator produces. A `LineTo`
    /// or `CubicTo` before any `MoveTo` opens a subpath at the origin — which is what Skia's
    /// `SkPath` does, and one line rather than a special case for a path the caller has already
    /// got wrong. A `ClosePath` with nothing open is ignored.
    let private flatten (steps: int) (segs: PathSeg[]) : Sub[] =
        let subs = ResizeArray<Sub>()
        let pts = ResizeArray<Vec2<nd>>()

        let finish (closed: bool) =
            if pts.Count > 0 then
                subs.Add { Points = pts.ToArray(); Closed = closed }
                pts.Clear()

        let ensureStarted () = if pts.Count = 0 then pts.Add origin

        for seg in segs do
            match seg with
            | MoveTo p ->
                finish false
                pts.Add p
            | LineTo p ->
                ensureStarted ()
                pts.Add p
            | CubicTo(c1, c2, until) ->
                ensureStarted ()
                let p0 = pts.[pts.Count - 1]

                for i in 1..steps do
                    let u = float i / float steps
                    let v = 1.0 - u
                    // Bernstein form, not de Casteljau: the coefficients are the same three
                    // multiplies either way and this reads as the formula it is.
                    let b0 = v * v * v
                    let b1 = 3.0 * v * v * u
                    let b2 = 3.0 * v * u * u
                    let b3 = u * u * u

                    pts.Add
                        { X = b0 * p0.X + b1 * c1.X + b2 * c2.X + b3 * until.X
                          Y = b0 * p0.Y + b1 * c1.Y + b2 * c2.Y + b3 * until.Y }
            | ClosePath -> finish true

        finish false
        subs.ToArray()

    /// Resample a subpath to `n` points evenly spaced by arc length, tracing the closing edge
    /// when there is one. A zero-length subpath — a lone `MoveTo`, or a shape collapsed to a
    /// point — answers with `n` copies of its only point rather than dividing by its length.
    let private resample (n: int) (sub: Sub) : Vec2<nd>[] =
        let src = sub.Points
        let m = src.Length
        let count = if sub.Closed then m + 1 else m
        let at i = if i = m then src.[0] else src.[i]

        let cum: float<nd>[] = Array.zeroCreate count

        for i in 1 .. count - 1 do
            cum.[i] <- cum.[i - 1] + Vec2.norm (at i - at (i - 1))

        let total = cum.[count - 1]
        let out: Vec2<nd>[] = Array.zeroCreate n

        if not (total > 0.0<nd>) then
            for i in 0 .. n - 1 do
                out.[i] <- src.[0]
        else
            // The targets ascend, so one forward scan covers all of them.
            let mutable j = 0

            for i in 0 .. n - 1 do
                let target = total * (float i / float (n - 1))

                while j < count - 2 && cum.[j + 1] < target do
                    j <- j + 1

                let span = cum.[j + 1] - cum.[j]
                let u = if not (span > 0.0<nd>) then 0.0 else float ((target - cum.[j]) / span)

                // The two ends are taken from the source rather than interpolated to it: an
                // endpoint that is a few ulps off its own input is the first thing a reader
                // checks and the last thing worth explaining.
                out.[i] <-
                    if u <= 0.0 then at j
                    elif u >= 1.0 then at (j + 1)
                    else Vec2.lerp u (at j) (at (j + 1))

        out

    let private centroid (ps: Vec2<nd>[]) : Vec2<nd> =
        let mutable acc = origin

        for p in ps do
            acc <- acc + p

        acc / float ps.Length

    let private collapse (padding: SubpathPadding) (ps: Vec2<nd>[]) : Vec2<nd> =
        match padding with
        | GrowFromCentroid -> centroid ps
        | GrowFromStart -> ps.[0]

    let private resolveClosure (policy: ClosurePolicy) (a: bool) (b: bool) =
        match policy with
        | CloseWhenBoth -> a && b
        | CloseWhenEither -> a || b

    /// The two paths reduced to a common form: one entry per output subpath, each holding both
    /// sides' sample rings and the closure they agreed on. Everything public below is this plus
    /// an interpolation.
    let private align (opts: MorphOptions) (a: PathSeg[]) (b: PathSeg[]) =
        if opts.Samples < 2 then
            invalidArg "opts" "Marey: a morph needs at least two samples per subpath."

        if opts.Flatten < 1 then
            invalidArg "opts" "Marey: a morph needs at least one line segment per cubic."

        let sa = flatten opts.Flatten a
        let sb = flatten opts.Flatten b

        Array.init (max sa.Length sb.Length) (fun i ->
            if i < sa.Length && i < sb.Length then
                resample opts.Samples sa.[i],
                resample opts.Samples sb.[i],
                resolveClosure opts.Closure sa.[i].Closed sb.[i].Closed
            elif i < sa.Length then
                // Unmatched on the far side: it shrinks into a point of its own. Its closure is
                // its own, because a policy applied to one path and its own shadow is that path.
                let ps = resample opts.Samples sa.[i]
                ps, Array.create opts.Samples (collapse opts.Padding ps), sa.[i].Closed
            else
                let ps = resample opts.Samples sb.[i]
                Array.create opts.Samples (collapse opts.Padding ps), ps, sb.[i].Closed)

    let private emit (rings: (Vec2<nd>[] * bool)[]) : PathSeg[] =
        let acc = ResizeArray<PathSeg>()

        for ps, closed in rings do
            acc.Add(MoveTo ps.[0])

            for i in 1 .. ps.Length - 1 do
                acc.Add(LineTo ps.[i])

            if closed then
                acc.Add ClosePath

        acc.ToArray()

    let private blend (t: float) (aligned: (Vec2<nd>[] * Vec2<nd>[] * bool)[]) : PathSeg[] =
        let acc = ResizeArray<PathSeg>()

        for pa, pb, closed in aligned do
            acc.Add(MoveTo(Interp.between pa.[0] pb.[0] t))

            for i in 1 .. pa.Length - 1 do
                acc.Add(LineTo(Interp.between pa.[i] pb.[i] t))

            if closed then
                acc.Add ClosePath

        acc.ToArray()

    /// Both paths in the common form the morph interpolates between — the same subpath count,
    /// the same sample count, the same closures.
    ///
    /// Public because it is what a caller draws to avoid a pop. `paths opts a b 0.0` is the
    /// *prepared* `a`, not `a`: it has been flattened and resampled, and for a path made of
    /// curves that is a visibly different picture at low `Samples`. Draw `fst (prepare opts a b)`
    /// for the still frame before the morph starts and the two agree exactly.
    let prepare (opts: MorphOptions) (a: PathSeg[]) (b: PathSeg[]) : PathSeg[] * PathSeg[] =
        let aligned = align opts a b

        emit (aligned |> Array.map (fun (pa, _, c) -> pa, c)),
        emit (aligned |> Array.map (fun (_, pb, c) -> pb, c))

    /// Morph `a` into `b`. `t = 0` is the prepared `a`, `t = 1` the prepared `b`, and `t`
    /// outside `[0, 1]` extrapolates — D71's overshoot reaches here like it reaches `tween`.
    ///
    /// Prepares both sides on every call. Use `anim` in a render loop.
    let paths (opts: MorphOptions) (a: PathSeg[]) (b: PathSeg[]) (t: float) : PathSeg[] = blend t (align opts a b)

    /// The same morph as an animation, with the preparation hoisted out of the frame loop.
    let anim (opts: MorphOptions) (d: float<s>) (a: PathSeg[]) (b: PathSeg[]) : Anim<PathSeg[]> =
        let aligned = align opts a b
        Anim.ofFraction d (fun u -> blend u aligned)
