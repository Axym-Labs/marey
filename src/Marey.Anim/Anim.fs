namespace Marey

open System
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

/// §7 — an animation is a value, not a mutation.
///
/// Manim's objects are mutable and its scene replays a timeline from the start. That single
/// decision is why a Manim render cannot be scrubbed, cannot produce frames out of order, and
/// cannot be parallelised. Model the animation as a function of time instead and all three fall
/// out: `anim.At 12.7<s>` is a direct evaluation, and
/// `[| 0 .. n |] |> Array.Parallel.map (fun i -> render (anim.At (dt * float i)))` is the whole
/// renderer.
///
/// ### One clock, three doors, and only one of them clamps
///
/// D70 and D71 had to be taken together, and they resolve in opposite directions.
///
/// * `At` is the **public seek**, and it clamps to `[0, Duration]`. It is therefore total on
///   every `float`, `nan` and both infinities included: before the start holds the first frame,
///   past the end holds the last. That is what makes `map2`'s "duration = max" mean something —
///   the shorter component holds through the tail rather than running off its own schedule —
///   and what makes `andThen`'s `t - d1` well-defined everywhere. D9 refused to clamp
///   `Scale.Forward` because clamping piles out-of-range data onto the panel edge and lies about
///   a measurement; an animation is a schedule, not a measurement, and holding a final frame is
///   not a claim about anything.
/// * `AtUnclamped` and `AtFraction` are the **combinator doors**, and neither clamps.
///   `AtFraction 1.3` extrapolates, which is what lets `Ease.backOut` overshoot past 1 and
///   settle back. Clamping there would leave a library in which the interesting half of the
///   easing catalogue compiles, runs, and does nothing.
///
/// One sentence: **the seek clamps, the interpolation does not.**
///
/// ### Why the stored function takes seconds
///
/// It could equally take the normalised fraction, and the first version of this file did. The
/// difference is what happens at a **seam**.
///
/// `andThen` and `map2` evaluate their components in seconds, because seconds are the only
/// clock two animations of different durations share. With a fraction stored, every level of
/// nesting divides by a duration and multiplies by one again, and `(t / d) * d` is not `t` for
/// every double — so `(a `andThen` b) `andThen` c` and `a `andThen` (b `andThen` c)` could
/// disagree about which side of `t = d1` a frame fell on. Not by a rounding error in the
/// picture: by a whole frame of the wrong animation. And a frame landing *exactly* on a seam is
/// the common case, not the rare one — seams are at durations, which are round numbers, and
/// frames are at multiples of `1/60`.
///
/// Storing seconds removes the round trip from the two combinators a storyboard is made of, and
/// pays for it in `ease`, where composing two easings now costs one normalisation. That is the
/// right way round: `andThen` is structural and `ease` is cosmetic.
///
/// ### Where an overshoot survives, and where it is absorbed
///
/// `ease` composes into the fraction, so an overshoot reaches whatever is underneath it. It
/// survives `tween`, `map` and `stretch`, all of which pass their argument through the
/// unclamped doors. It is **absorbed** by `andThen` and `map2`, which reach their components
/// through `At` — because that is what "the shorter one holds" and "the first one has finished"
/// mean.
///
/// So `ease Ease.backOut (tween 1.0<s> 0.0 1.0)` overshoots and `ease Ease.backOut (map2 f a b)`
/// does not. **Ease the components, not the composition** — which is what you want anyway,
/// since each component wants its own curve.
[<Sealed>]
type Anim<'a>(duration: float<s>, atSeconds: float<s> -> 'a) =

    // Validated rather than clamped, unlike `Scene.opacity`. A translucent thing has a nearest
    // legal opacity; a schedule of negative length does not, because zero and "reversed" are
    // both defensible and neither is what the caller meant. Nothing in the algebra can produce
    // one — `andThen` sums non-negatives, `map2` takes a max, `stretch` refuses a non-positive
    // factor — so this fires only on a value a caller supplied.
    do
        if
            Double.IsNaN(float duration)
            || Double.IsInfinity(float duration)
            || duration < 0.0<s>
        then
            invalidArg "duration" "Marey: an animation's duration must be finite and non-negative."

    /// How long the animation runs. Always finite and non-negative.
    member _.Duration: float<s> = duration

    /// The **seek**, clamped to `[0, Duration]`. Total on every `float<s>`: `nan` and anything
    /// at or below zero give the first frame, anything at or beyond the end gives the last.
    /// Seeking is evaluation, so this is the whole of "scrub the timeline".
    member _.At(t: float<s>) : 'a =
        // Written as `not (t > 0)` rather than `t < 0` so that `nan` lands on the first frame
        // instead of falling through both branches and propagating into the picture.
        atSeconds (
            if not (t > 0.0<s>) then 0.0<s>
            elif t > duration then duration
            else t
        )

    /// The raw clock, in seconds and **not** clamped. This is what a combinator reaches for
    /// when it needs to shift or scale time without the endpoints getting in the way;
    /// `Anim.stretch` is three characters of arithmetic on top of it.
    member _.AtUnclamped(t: float<s>) : 'a = atSeconds t

    /// The **normalised** clock, `0.0` the first frame and `1.0` the last, and not clamped
    /// either. This is the door `ease` composes into and the reason `Ease.backOut` does
    /// something. A zero-duration animation has no fraction, so every input lands on its one
    /// frame rather than on `nan`.
    member _.AtFraction(u: float) : 'a = atSeconds (u * duration)


/// §7 — the algebra. Six combinators and two constructors; the laws are stated and tested in
/// `AnimProperties` rather than asserted here.
///
/// **What actually holds**, because §7's one-line summary ("`(Anim<'a>, andThen, constant)` is a
/// monoid") is one word too strong and the difference is a single instant:
///
/// * `andThen` is **associative**, exactly, at every time.
/// * `constant v` is an exact **left identity**: an animation of zero duration occupies no time, so
///   putting one in front of `x` does not cost `x` its first frame.
/// * `constant v` is a right identity **everywhere except the final instant**, where it supplies
///   `v`. That is not a defect to be patched. `andThen` gives the earlier animation the
///   half-open interval `[0, d1)` and the later one the seam and the tail, so a zero-duration
///   animation is invisible unless it is last — where it is the final frame, which is exactly
///   what appending a constant should mean. Patching it would need a special case for zero
///   durations, and that special case is undecidable when *both* components have one.
/// * `map2` is a genuine **applicative**: identity, homomorphism, interchange and composition
///   all hold exactly.
[<RequireQualifiedAccess>]
module Anim =

    /// A duration and a function of the **unclamped** normalised clock. This is the door that
    /// admits overshoot, and `tween` comes through it.
    let ofFraction (d: float<s>) (f: float -> 'a) : Anim<'a> =
        Anim<'a>(d, (fun t -> f (if d > 0.0<s> then t / d else 0.0)))

    /// A duration and a function of **seconds**, which are clamped to `[0, d]` before it sees
    /// them. An animation built this way cannot overshoot, and that is the point of having two
    /// doors: a caller thinking in seconds is describing a schedule, and a schedule has no time
    /// before it starts.
    let ofSeconds (d: float<s>) (f: float<s> -> 'a) : Anim<'a> =
        Anim<'a>(
            d,
            fun t ->
                f (
                    if not (t > 0.0<s>) then 0.0<s>
                    elif t > d then d
                    else t
                )
        )

    let duration (a: Anim<'a>) : float<s> = a.Duration

    /// `anim |> Anim.at 12.7<s>`. Clamped — see `Anim.At`.
    let at (t: float<s>) (a: Anim<'a>) : 'a = a.At t

    /// `anim |> Anim.atFraction 1.3`. Not clamped — see `Anim.AtFraction`.
    let atFraction (u: float) (a: Anim<'a>) : 'a = a.AtFraction u

    /// A constant, of zero duration. The identity of `andThen` on the left, and on the right
    /// everywhere but the last instant.
    let constant (v: 'a) : Anim<'a> = Anim<'a>(0.0<s>, (fun _ -> v))

    let map (f: 'a -> 'b) (a: Anim<'a>) : Anim<'b> =
        Anim<'b>(a.Duration, (fun t -> f (a.AtUnclamped t)))

    /// Parallel composition; the duration is the **maximum**, and the shorter component holds
    /// its final value through the tail. That hold is D70's clamp doing its job, which is why
    /// the components are reached through `At` rather than through the raw door.
    let map2 (f: 'a -> 'b -> 'c) (a: Anim<'a>) (b: Anim<'b>) : Anim<'c> =
        Anim<'c>(max a.Duration b.Duration, (fun t -> f (a.At t) (b.At t)))

    /// Sequential composition; the duration is the **sum**. The earlier animation owns the
    /// half-open interval `[0, d1)` and the later one owns the seam and everything after, which
    /// is what makes `constant` an exact left identity.
    let andThen (a: Anim<'a>) (b: Anim<'a>) : Anim<'a> =
        let d1 = a.Duration

        Anim<'a>(d1 + b.Duration, (fun t -> if t < d1 then a.At t else b.At(t - d1)))

    /// Scale the duration by `k`, leaving the curve alone.
    ///
    /// `k` must be positive and finite. A factor of zero collapses the animation to a single
    /// instant with no way to say which one, and a negative factor is a request for reversal
    /// wearing the wrong name — `reverse` is a different combinator and this is not it.
    let stretch (k: float) (a: Anim<'a>) : Anim<'a> =
        if not (k > 0.0) || Double.IsInfinity k then
            invalidArg "k" "Marey: stretch takes a positive, finite factor."

        Anim<'a>(k * a.Duration, (fun t -> a.AtUnclamped(t / k)))

    /// Reparameterise the normalised clock. The result of `f` is **not** clamped — D71 — so an
    /// easing that leaves `[0, 1]` reaches the interpolation underneath and extrapolates.
    let ease (f: float -> float) (a: Anim<'a>) : Anim<'a> =
        let d = a.Duration

        Anim<'a>(d, (fun t -> a.AtFraction(f (if d > 0.0<s> then t / d else 0.0))))

    /// Interpolate from `a` to `b` over `d`, through Core's `Interpolate` witness — so this one
    /// combinator tweens `float<m>`, `float32`, `Vec2`, `Vec3`, `Color` and `Rect`, with no
    /// boxing and no per-type overload here.
    ///
    /// The constraint is restated in full, per §12.2: omitting it compiles, and then the second
    /// call site in the program fails with a type error naming the first one.
    let inline tween< ^T when (Interpolate or ^T): (static member Interp: ^T * ^T * float -> ^T)>
        (d: float<s>)
        (a: ^T)
        (b: ^T)
        : Anim< ^T> =
        ofFraction d (fun u -> Interp.between< ^T> a b u)
