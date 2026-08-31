/// §7 / D70 / D71 — the animation algebra, the two clocks, and the easing catalogue.
///
/// **Gate 5** ("overshoot survives, per `Interp` instance") and **gate 6** ("the algebra's laws,
/// by FsCheck: monoid, applicative, `stretch` composition, `At` total") both live here, because
/// both are statements about `tween` and `tween` is in this package.
///
/// Gate 6's word "monoid" is one word too strong and the difference is a single instant; what
/// actually holds is stated — and tested — in `Algebra` below.
///
/// ### These laws are asserted with `=`, and that is a claim about the representation
///
/// Every law below except `ease composes` holds **exactly**, bit for bit, and is written with
/// `=` rather than a tolerance. That is not luck. `Anim` stores a function of *seconds*, so
/// `andThen` and `map2` reach their components without normalising: nesting them costs one
/// subtraction and no round trip through a duration.
///
/// The first version of this file stored the *fraction* instead, and these same laws had to be
/// written with `Approx.rel` — every level of nesting divided by a duration and multiplied by
/// one again, and `(t / d) * d` is not `t` for every double. It was not the ulp that mattered.
/// It was that `andThen` compares `t` against the seam `d1`, so an ulp decides **which
/// animation a frame shows**, and a frame landing exactly on a seam is the common case rather
/// than the rare one: seams are at durations, which are round numbers, and frames are at
/// multiples of 1/60. The counterexample that found it had `t = d1` exactly.
///
/// So a tolerance here would not have been a weaker law. It would have been the wrong law,
/// passing over a representation that could show the wrong frame.
///
/// `ease composes` is the one that keeps `Tol.arith`, and it is where the cost went: composing
/// two easings normalises once. That is the right way round — `andThen` is structural and
/// `ease` is cosmetic.
module Marey.Tests.AnimProperties

open System
open System.Reflection
open FsCheck.Xunit
open Xunit
open Marey
open Marey.Tests.Generators


// ======================================================================================
// THE EASING CATALOGUE
//
// Found by reflection rather than by a list somebody maintains. A catalogue law that is fed by
// hand is a law over the functions whoever wrote it remembered.
// ======================================================================================

module Catalogue =

    /// Every public `float -> float` in `Marey.Ease`.
    ///
    /// Module-level functions compile to static methods, which is why `Ease.fs` writes every
    /// entry as `let quadIn (u: float) = powIn 2.0 u` rather than `let quadIn = powIn 2.0`: the
    /// second spelling compiles to a *property*, and half a catalogue invisible to its own law
    /// is worse than no law. The shared implementations are `private` and therefore not here.
    let private catalogue: (string * (float -> float))[] =
        typeof<Anim<int>>.Assembly.GetType("Marey.Ease").GetMethods(BindingFlags.Public ||| BindingFlags.Static)
        |> Array.filter (fun m ->
            m.ReturnType = typeof<float>
            && (let ps = m.GetParameters() in
                ps.Length = 1 && ps.[0].ParameterType = typeof<float>))
        |> Array.map (fun m -> m.Name, (fun (u: float) -> m.Invoke(null, [| box u |]) :?> float))
        |> Array.sortBy fst

    let private sample (f: float -> float) =
        [| for i in 0..1000 -> f (float i / 1000.0) |]

    /// How far outside `[0, 1]` counts as leaving it. Not zero: `bounceOut` touches exactly 1.0
    /// at two interior seams by construction — that is the ball reaching the target and coming
    /// off it again — and a threshold of zero would call float rounding an overshoot.
    [<Literal>]
    let private Escape = 1e-6

    /// The same tripwire `Node has exactly ten cases` is: adding an easing is fine, and it has
    /// to be a decision rather than a diff. The names are the ones the laws below are quantified
    /// over, so this assertion is also what stops the reflection above silently finding nothing.
    [<Fact>]
    let ``the catalogue is exactly these twenty-eight`` () =
        Assert.Equal<string[]>(
            [| "backIn"
               "backInOut"
               "backOut"
               "bounceIn"
               "bounceInOut"
               "bounceOut"
               "circIn"
               "circInOut"
               "circOut"
               "cubicIn"
               "cubicInOut"
               "cubicOut"
               "elasticIn"
               "elasticInOut"
               "elasticOut"
               "expoIn"
               "expoInOut"
               "expoOut"
               "linear"
               "quadIn"
               "quadInOut"
               "quadOut"
               "quartIn"
               "quartInOut"
               "quartOut"
               "sineIn"
               "sineInOut"
               "sineOut" |],
            catalogue |> Array.map fst
        )

    /// The one thing every easing must do, whatever it does in between. `expoIn` needs its
    /// endpoint branch to pass this — `2.0 ** (10.0 * 0.0 - 10.0)` is 1/1024, not 0 — so this
    /// law is load-bearing rather than decorative.
    [<Fact>]
    let ``every easing starts at zero and ends at one`` () =
        for name, f in catalogue do
            Assert.True(Approx.abs' 1e-12 0.0 (f 0.0), sprintf "%s 0.0 = %.17g" name (f 0.0))
            Assert.True(Approx.abs' 1e-12 1.0 (f 1.0), sprintf "%s 1.0 = %.17g" name (f 1.0))

    /// **Two-sided, which is what makes it a gate.** It fails if a tame easing overflows, and it
    /// fails if `back` or `elastic` stops overshooting — which is the failure D71 exists to
    /// prevent and the one that would otherwise look like everything working.
    [<Fact>]
    let ``exactly back and elastic leave the unit interval`` () =
        for name, f in catalogue do
            let ys = sample f
            let leaves = Array.min ys < -Escape || Array.max ys > 1.0 + Escape

            let allowed =
                name.StartsWith("back", StringComparison.Ordinal)
                || name.StartsWith("elastic", StringComparison.Ordinal)

            // Parenthesised: inside a method call, `leaves = allowed` parses as a named argument.
            Assert.True(
                (leaves = allowed),
                sprintf
                    "%s ranges over [%.6f, %.6f] and %s allowed to leave [0,1]"
                    name
                    (Array.min ys)
                    (Array.max ys)
                    (if allowed then "is" else "is not")
            )

    /// And it leaves by an amount a viewer can see. Penner's constant is chosen so `backOut`
    /// overshoots by about 10% of the travel; a gate that accepted 1e-9 would pass against an
    /// easing that had been flattened to within a rounding error of linear.
    [<Fact>]
    let ``the overshoot is visible, not residual`` () =
        let below (f: float -> float) = Array.min (sample f)
        let above (f: float -> float) = Array.max (sample f)

        Assert.True(below Ease.backIn < -0.05, "backIn should anticipate below zero")
        Assert.True(above Ease.backOut > 1.05, "backOut should overshoot past one")
        Assert.True(below Ease.backInOut < -0.05 && above Ease.backInOut > 1.05)
        Assert.True(below Ease.elasticIn < -0.05, "elasticIn should anticipate below zero")
        Assert.True(above Ease.elasticOut > 1.05, "elasticOut should overshoot past one")
        Assert.True(below Ease.elasticInOut < -0.05 && above Ease.elasticInOut > 1.05)


// ======================================================================================
// THE TWO CLOCKS
//
// D70 outside, D71 inside, and the one sentence that separates them: clamp the seconds, never
// the fraction.
// ======================================================================================

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Clock =

    let private ramp (d: float) = Anim.tween (d * 1.0<s>) 0.0 1.0

    /// D70. Seeking before the start holds the first frame and seeking past the end holds the
    /// last, for every duration including zero.
    [<Property>]
    let ``At clamps the seconds`` (AnimSeconds d) (Seek t) =
        let a = ramp d
        let t' = t * 1.0<s>

        if t <= 0.0 then a.At t' = a.At 0.0<s>
        elif t' >= a.Duration then a.At t' = a.At a.Duration
        else true

    /// GATE 6, the "`At` is total" half. Not a property, because the interesting inputs are the
    /// three FsCheck's generators are built to exclude, and they are exactly the three a seek
    /// bar can produce: a division by a zero-length timeline, and either infinity from a
    /// runaway scrub.
    [<Fact>]
    let ``At is total on every double`` () =
        let a = ramp 2.0
        let secs (x: float) = LanguagePrimitives.FloatWithMeasure<s> x

        Assert.Equal(0.0, a.At(secs Double.NaN), 15)
        Assert.Equal(0.0, a.At(secs Double.NegativeInfinity), 15)
        Assert.Equal(1.0, a.At(secs Double.PositiveInfinity), 15)
        Assert.Equal(0.0, a.At(secs -1e300), 15)
        Assert.Equal(1.0, a.At(secs 1e300), 15)

    /// A zero-duration animation has no fraction to compute — `0.0 / 0.0` is `nan` — so it
    /// answers at the first frame for every input rather than poisoning the picture.
    [<Fact>]
    let ``a zero-duration animation answers everywhere`` () =
        let a = Anim.pure' 7.0
        Assert.Equal(7.0, a.At 0.0<s>, 15)
        Assert.Equal(7.0, a.At 5.0<s>, 15)
        Assert.Equal(7.0, a.At -5.0<s>, 15)
        Assert.Equal(0.0, float (Anim.tween 0.0<s> 3.0 9.0).Duration, 15)
        Assert.Equal(3.0, (Anim.tween 0.0<s> 3.0 9.0).At 1.0<s>, 15)

    /// D71. The fraction is not clamped, and this is the shortest possible statement of it.
    [<Property>]
    let ``the fraction is not clamped`` (Seek u) =
        let a = Anim.tween 2.0<s> 0.0 1.0
        Approx.rel Tol.arith (a.AtFraction u) u

    /// **The D70/D71 gate.** `Ease.backOut` peaks about 10% above 1. If `At` clamped the
    /// fraction, or if `ease` clamped its result, this animation would top out at exactly its
    /// endpoint — and the interesting half of the easing catalogue would compile, run, and do
    /// nothing.
    [<Fact>]
    let ``an eased tween overshoots its endpoint through the seconds clock`` () =
        let a = Anim.tween 2.0<s> 0.0<m> 10.0<m> |> Anim.ease Ease.backOut

        let peak =
            [| 0..400 |] |> Array.map (fun i -> a.At(float i / 200.0 * 1.0<s>)) |> Array.max

        Assert.True(peak > 10.5<m>, sprintf "peaked at %g m" (float peak))

    /// The instrument check for the gate above. The same ten metres over the same two seconds,
    /// built through the **seconds** door instead of the fraction one, cannot overshoot at all:
    /// `ofSeconds` clamps what it hands over. Two animations, one constructor apart, and the
    /// difference between them is exactly D71.
    [<Fact>]
    let ``the seconds door cannot overshoot`` () =
        let a = Anim.ofSeconds 2.0<s> (fun t -> 5.0<m/s> * t) |> Anim.ease Ease.backOut

        let peak =
            [| 0..400 |] |> Array.map (fun i -> a.At(float i / 200.0 * 1.0<s>)) |> Array.max

        Assert.Equal(10.0, float peak, 12)

    /// A duration is validated rather than clamped: `Scene.opacity` has a nearest legal value
    /// and a schedule of negative length does not.
    [<Fact>]
    let ``a duration that is not a duration is refused`` () =
        // A `[<Theory>]` cannot carry these: `Double.NaN` is a field, not a literal, so an
        // attribute cannot name it — which is also why the two values that matter most here
        // would be the two a theory quietly left out.
        for d in [ -1.0; Double.NaN; Double.PositiveInfinity; Double.NegativeInfinity ] do
            Assert.Throws<ArgumentException>(fun () ->
                Anim.ofFraction (LanguagePrimitives.FloatWithMeasure<s> d) id |> ignore)
            |> ignore


// ======================================================================================
// THE ALGEBRA
//
// GATE 6. What §7 calls a monoid is an associative operation with an exact left identity and a
// right identity that differs at one instant, and the tests say so rather than the slogan.
// ======================================================================================

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Algebra =

    /// Non-linear and strictly increasing on `[0, 1]`, so two different fractions give two
    /// different values and a law that compares values is really comparing clocks. A linear
    /// animation would hide a reparameterisation error inside its own linearity.
    let private animOf (AnimSpec(seconds, k)) : Anim<float> =
        Anim.ofFraction (seconds * 1.0<s>) (fun u -> k + u + 0.5 * u * u)

    let private near (a: float) (b: float) = Approx.rel Tol.arith a b

    let private ap (fa: Anim<'a -> 'b>) (xa: Anim<'a>) : Anim<'b> =
        Anim.map2 (fun f x -> f x) fa xa

    // ---- andThen ------------------------------------------------------------------------

    /// The law the storyboard CE will actually rest on: a fold over a list of steps must not
    /// depend on how the fold grouped them.
    [<Property>]
    let ``andThen is associative`` (a: AnimSpec) (b: AnimSpec) (c: AnimSpec) (Seek t) =
        let x, y, z = animOf a, animOf b, animOf c
        let l = Anim.andThen (Anim.andThen x y) z
        let r = Anim.andThen x (Anim.andThen y z)
        let t' = t * 1.0<s>

        l.Duration = r.Duration && l.At t' = r.At t'

    /// `pure'` is an exact left identity because `andThen` gives the earlier animation the
    /// half-open interval `[0, d1)`: an animation of zero duration owns nothing, so it cannot
    /// cost `x` its first frame.
    [<Property>]
    let ``pure' is a left identity`` (a: AnimSpec) (FiniteFloat v) (Seek t) =
        let x = animOf a
        let l = Anim.andThen (Anim.pure' v) x
        let t' = t * 1.0<s>

        l.Duration = x.Duration && l.At t' = x.At t'

    /// And a right identity **everywhere except the final instant**, where the appended
    /// constant is what the sequence ends on. That is the direct consequence of the same
    /// half-open rule, and it is why the module header does not say "monoid": patching it would
    /// mean special-casing zero durations, and the special case is undecidable when *both*
    /// components have zero duration.
    [<Property>]
    let ``pure' is a right identity everywhere but the last instant`` (a: AnimSpec) (FiniteFloat v) (Seek t) =
        let x = animOf a
        let r = Anim.andThen x (Anim.pure' v)
        let t' = max 0.0 t * 1.0<s>

        r.Duration = x.Duration
        && (if t' >= x.Duration then
                r.At t' = v
            else
                r.At t' = x.At t')

    [<Property>]
    let ``andThen adds the durations`` (a: AnimSpec) (b: AnimSpec) =
        let x, y = animOf a, animOf b
        (Anim.andThen x y).Duration = x.Duration + y.Duration

    /// The sequence really is sequential: before the seam it is the first animation's picture,
    /// after it the second's, and the second starts from its own beginning rather than from
    /// wherever the clock happened to be.
    [<Fact>]
    let ``andThen plays one and then the other`` () =
        let first = Anim.tween 1.0<s> 0.0 1.0
        let second = Anim.tween 3.0<s> 10.0 20.0
        let both = Anim.andThen first second

        Assert.Equal(4.0, float both.Duration, 15)
        Assert.Equal(0.5, both.At 0.5<s>, 12)
        Assert.Equal(10.0, both.At 1.0<s>, 12)
        Assert.Equal(15.0, both.At 2.5<s>, 12)
        Assert.Equal(20.0, both.At 4.0<s>, 12)

    // ---- map, and the applicative --------------------------------------------------------

    /// **Exact.** `map` rewrites the value and never touches the clock.
    [<Property>]
    let ``map id is the identity`` (a: AnimSpec) (Seek t) =
        let x = animOf a
        let t' = t * 1.0<s>
        (Anim.map id x).At t' = x.At t' && (Anim.map id x).Duration = x.Duration

    /// **Exact**, for the same reason.
    [<Property>]
    let ``map composes`` (a: AnimSpec) (FiniteFloat k) (Seek t) =
        let x = animOf a
        let f (v: float) = v * 2.0 + k
        let g (v: float) = v * v
        let t' = t * 1.0<s>

        (Anim.map (f >> g) x).At t' = (Anim.map g (Anim.map f x)).At t'

    [<Property>]
    let ``map2 takes the longer duration`` (a: AnimSpec) (b: AnimSpec) =
        let x, y = animOf a, animOf b
        (Anim.map2 (+) x y).Duration = max x.Duration y.Duration

    [<Property>]
    let ``applicative identity`` (a: AnimSpec) (Seek t) =
        let x = animOf a
        let t' = t * 1.0<s>
        let l = ap (Anim.pure' id) x

        l.Duration = x.Duration && l.At t' = x.At t'

    [<Property>]
    let ``applicative homomorphism`` (FiniteFloat k) (FiniteFloat v) (Seek t) =
        let f (x: float) = x + k
        let t' = t * 1.0<s>
        let l = ap (Anim.pure' f) (Anim.pure' v)

        l.Duration = 0.0<s> && l.At t' = f v

    [<Property>]
    let ``applicative interchange`` (a: AnimSpec) (FiniteFloat y) (Seek t) =
        let u = Anim.map (fun k -> (fun (x: float) -> k + x)) (animOf a)
        let t' = t * 1.0<s>
        let l = ap u (Anim.pure' y)
        let r = ap (Anim.pure' (fun (g: float -> float) -> g y)) u

        l.Duration = r.Duration && l.At t' = r.At t'

    [<Property>]
    let ``applicative composition`` (a: AnimSpec) (b: AnimSpec) (c: AnimSpec) (Seek t) =
        let u = Anim.map (fun k -> (fun (x: float) -> k + x)) (animOf a)
        let v = Anim.map (fun k -> (fun (x: float) -> k * x)) (animOf b)
        let w = animOf c
        let t' = t * 1.0<s>

        let compose =
            Anim.pure' (fun (g: float -> float) (h: float -> float) (x: float) -> g (h x))

        let l = ap (ap (ap compose u) v) w
        let r = ap u (ap v w)

        l.Duration = r.Duration && l.At t' = r.At t'

    /// D70's substance, and the reason "duration = max" is a decision rather than a default:
    /// the shorter component **holds its final value** through the tail instead of running off
    /// the end of its own schedule.
    [<Fact>]
    let ``the shorter component holds through the tail`` () =
        let short = Anim.tween 1.0<s> 0.0 1.0
        let long = Anim.tween 4.0<s> 0.0 1.0
        let both = Anim.map2 (fun a b -> a, b) short long

        Assert.Equal(4.0, float both.Duration, 15)
        let a0, b0 = both.At 0.5<s>
        Assert.Equal(0.5, a0, 12)
        Assert.Equal(0.125, b0, 12)
        let a1, b1 = both.At 3.0<s>
        Assert.Equal(1.0, a1, 12)
        Assert.Equal(0.75, b1, 12)

    // ---- stretch and ease ----------------------------------------------------------------

    /// **Exact**, and this is what the two-clock design buys: `stretch` changes the duration and
    /// leaves the curve entirely alone, so the composition law is an identity on the stored
    /// function and a product on one float.
    [<Property>]
    let ``stretch composes`` (a: AnimSpec) (StretchFactor j) (StretchFactor k) (Seek t) =
        let x = animOf a
        let l = Anim.stretch j (Anim.stretch k x)
        let r = Anim.stretch (j * k) x
        let t' = t * 1.0<s>

        l.Duration = r.Duration && l.At t' = r.At t'

    [<Property>]
    let ``stretch scales the duration and nothing else`` (a: AnimSpec) (StretchFactor k) (UnitT u) =
        let x = animOf a
        let y = Anim.stretch k x

        y.Duration = k * x.Duration && y.AtFraction u = x.AtFraction u

    [<Property>]
    let ``stretch 1 is the identity`` (a: AnimSpec) (Seek t) =
        let x = animOf a
        let t' = t * 1.0<s>
        (Anim.stretch 1.0 x).At t' = x.At t'

    [<Fact>]
    let ``stretch refuses a factor that is not a speed`` () =
        for k in [ 0.0; -2.0; Double.PositiveInfinity; Double.NaN ] do
            Assert.Throws<ArgumentException>(fun () -> Anim.stretch k (Anim.pure' 1.0) |> ignore)
            |> ignore

    /// Easing twice is easing once with the composed curve — note the order: the outer easing
    /// is applied to the clock *first*.
    ///
    /// **The one law here that is not exact.** Nesting two easings normalises the clock once in
    /// between, which is the cost the seconds representation pays and the module header
    /// explains. It is an ulp on a cosmetic curve, not a frame on a seam.
    [<Property>]
    let ``ease composes, outermost first`` (a: AnimSpec) (Seek t) =
        let x = animOf a
        let t' = t * 1.0<s>
        let l = Anim.ease Ease.quadIn (Anim.ease Ease.cubicOut x)
        let r = Anim.ease (Ease.quadIn >> Ease.cubicOut) x

        l.Duration = r.Duration && near (l.At t') (r.At t')

    [<Property>]
    let ``ease leaves the duration alone`` (a: AnimSpec) =
        let x = animOf a
        (Anim.ease Ease.elasticInOut x).Duration = x.Duration


// ======================================================================================
// GATE 5 — OVERSHOOT SURVIVES, PER `Interp` INSTANCE
//
// Stated against the fraction clock directly, because that is where D71 put it: `tween`
// evaluates `interp a b u` at whatever `u` it is handed, and §1 measured that none of Core's
// six interpolators clamp it. One assertion per instance, because SRTP resolution is per-type
// and an overload that clamps is invisible from a test of a different one.
// ======================================================================================

module Overshoot =

    /// 25% past the end: far enough to be unambiguous, close enough to stay in gamut for the
    /// colour case below.
    [<Literal>]
    let private Beyond = 1.25

    [<Fact>]
    let ``float overshoots`` () =
        let got = Anim.tween 1.0<s> 0.0<m> 10.0<m> |> Anim.atFraction Beyond
        Assert.Equal(12.5, float got, 12)

    [<Fact>]
    let ``float32 overshoots`` () =
        let got = Anim.tween 1.0<s> 0.0f 10.0f |> Anim.atFraction Beyond
        Assert.Equal(12.5, float got, 4)

    [<Fact>]
    let ``Vec2 overshoots`` () =
        let a: Vec2<nd> = { X = 0.0<nd>; Y = 4.0<nd> }
        let b: Vec2<nd> = { X = 10.0<nd>; Y = 8.0<nd> }
        let got = Anim.tween 1.0<s> a b |> Anim.atFraction Beyond

        Assert.Equal(12.5, float got.X, 12)
        Assert.Equal(9.0, float got.Y, 12)

    [<Fact>]
    let ``Vec3 overshoots`` () =
        let a: Vec3<nd> = { X = 0.0<nd>; Y = 0.0<nd>; Z = 4.0<nd> }
        let b: Vec3<nd> = { X = 10.0<nd>; Y = 20.0<nd>; Z = 8.0<nd> }
        let got = Anim.tween 1.0<s> a b |> Anim.atFraction Beyond

        Assert.Equal(12.5, float got.X, 12)
        Assert.Equal(25.0, float got.Y, 12)
        Assert.Equal(9.0, float got.Z, 12)

    [<Fact>]
    let ``Rect overshoots`` () =
        let a: Rect<nd> =
            { X0 = 0.0<nd>
              Y0 = 0.0<nd>
              X1 = 1.0<nd>
              Y1 = 1.0<nd> }

        let b: Rect<nd> =
            { X0 = 1.0<nd>
              Y0 = 2.0<nd>
              X1 = 3.0<nd>
              Y1 = 5.0<nd> }

        let got = Anim.tween 1.0<s> a b |> Anim.atFraction Beyond

        Assert.Equal(1.25, float got.X0, 12)
        Assert.Equal(2.5, float got.Y0, 12)
        Assert.Equal(3.5, float got.X1, 12)
        Assert.Equal(6.0, float got.Y1, 12)

    /// **The colour case is asserted in Oklab, and it has to be.**
    ///
    /// `Interp(Color, ...)` goes sRGB -> linear -> Oklab, interpolates there, and returns
    /// through `ofOklab`, which ends in three `clamp01`s. A tween between two saturated colours
    /// therefore overshoots perceptually and then lands back inside the sRGB cube, and a gate
    /// written in sRGB would be measuring the clamp.
    ///
    /// Two mid greys, so the overshoot stays in gamut and the extrapolation is the only thing
    /// under test. `Tol.colour` because the sRGB round trip has an irreducible floor of ~1e-6 —
    /// decisions.md F1.
    [<Fact>]
    let ``Color overshoots in Oklab`` () =
        let a = Color.rgb 0.35 0.35 0.35
        let b = Color.rgb 0.45 0.45 0.45
        let got = Anim.tween 1.0<s> a b |> Anim.atFraction Beyond

        let la = (Color.toOklab a).L
        let lb = (Color.toOklab b).L
        let got' = (Color.toOklab got).L

        Assert.True(got' > lb, sprintf "L went from %g to %g and stopped at %g" la lb got')
        Assert.True(Approx.abs' Tol.colour got' (la + Beyond * (lb - la)))

    /// **Alpha overshoot does not exist**, and it is not a defect. `ofOklab` ends with
    /// `A = clamp01 c.Alpha`, unconditionally: an alpha above 1 is not a colour. So a `fadeIn`
    /// eased with `Ease.backOut` cannot overshoot its opacity, only its hue and lightness. The
    /// plan lists this among the imprecisions to pin rather than file, and this is the pin.
    [<Fact>]
    let ``alpha cannot overshoot`` () =
        let a = Color.rgba 0.4 0.4 0.4 0.0
        let b = Color.rgba 0.4 0.4 0.4 1.0
        let got = Anim.tween 1.0<s> a b |> Anim.atFraction Beyond

        Assert.Equal(1.0, got.A, 12)
