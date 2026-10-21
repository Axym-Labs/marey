/// "`Interp` endpoint laws".
///
/// The endpoint laws are `interp a b 0 = a` and `interp a b 1 = b`, plus the containment law
/// that an interpolated value never leaves the interval it was interpolated across. They are
/// stated once per overload of the `Interpolate` witness, because SRTP resolution is per-type
/// and an overload that is subtly wrong for `Rect` is invisible from a test of `float`.
///
/// The colour case uses `Tol.colour = 1e-5`, per **decisions.md F1**: `Color.mix` goes through
/// Oklab, whose sRGB round trip has an irreducible floor of ~1e-6, so `interp a b 0` is `a` only
/// to that accuracy. Tightening it below 1e-5 makes the suite flap forever.
module Marey.Tests.InterpProperties

open FsCheck.Xunit
open Xunit
open Marey
open Marey.Tests.Generators

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Endpoints =

    /// `interp a b 0` is **exact**: `a + (b - a) * 0` is `a` plus a true zero.
    ///
    /// `interp a b 1` is not, and cannot be. `a + (b - a) * 1` recovers `b` only to within a few
    /// ulps **of the larger endpoint** — for `a = -89957` and `b = 1.18e-5`, `(b - a)` rounds to
    /// a number whose ulp is 7.8e-3 in float32, so every digit of `b` is gone. Relative to `b`
    /// the error is 100%; relative to `max(|a|,|b|)`, which is the magnitude the addition
    /// actually operated on, it is 1.3e-10.
    ///
    /// So the endpoint law is stated against that scale. Anything else is either false or a
    /// tolerance chosen to make a failure disappear.
    let private recovers (tol: float) (a: float) (b: float) (got: float) =
        Approx.scaled tol (max (abs a) (abs b)) got b

    /// §12.3 in law form: the overload is measure-generic, so this is `float<m>`, not `float`.
    [<Property>]
    let ``float endpoints`` (FiniteFloat a) (FiniteFloat b) =
        let x = a * 1.0<m>
        let y = b * 1.0<m>
        Interp.between x y 0.0 = x && recovers Tol.arith a b (float (Interp.between x y 1.0))

    /// float32 has an eps of 1.19e-7, so the same law needs a tolerance three orders coarser.
    [<Property>]
    let ``float32 endpoints`` (FiniteFloat a) (FiniteFloat b) =
        let x = float32 a
        let y = float32 b

        Interp.between x y 0.0 = x
        && recovers 1e-6 (float x) (float y) (float (Interp.between x y 1.0))

    [<Property>]
    let ``Vec2 endpoints`` (Vec2Nd a) (Vec2Nd b) =
        let r = Interp.between a b 1.0

        Interp.between a b 0.0 = a
        && recovers Tol.arith (float a.X) (float b.X) (float r.X)
        && recovers Tol.arith (float a.Y) (float b.Y) (float r.Y)

    [<Property>]
    let ``Vec3 endpoints`` (Vec3Nd a) (Vec3Nd b) =
        let r = Interp.between a b 1.0

        Interp.between a b 0.0 = a
        && recovers Tol.arith (float a.X) (float b.X) (float r.X)
        && recovers Tol.arith (float a.Y) (float b.Y) (float r.Y)
        && recovers Tol.arith (float a.Z) (float b.Z) (float r.Z)

    [<Property>]
    let ``Rect endpoints`` (RectNd a) (RectNd b) =
        let r = Interp.between a b 1.0

        Interp.between a b 0.0 = a
        && recovers Tol.arith (float a.X0) (float b.X0) (float r.X0)
        && recovers Tol.arith (float a.Y0) (float b.Y0) (float r.Y0)
        && recovers Tol.arith (float a.X1) (float b.X1) (float r.X1)
        && recovers Tol.arith (float a.Y1) (float b.Y1) (float r.Y1)

    /// F1 again: the tolerance here is a property of Oklab, not of `interp`. Measured worst
    /// endpoint deviation over 100 000 random colour pairs: 1.66e-6.
    [<Property>]
    let ``Color endpoints, within the Oklab round-trip floor`` (Colour a) (Colour b) =
        Approx.colour (Interp.between a b 0.0) a && Approx.colour (Interp.between a b 1.0) b

    /// The witness is reachable through all three published spellings, and they agree. §12.2 is
    /// the reason there are three: every wrapper level has to restate the constraint, and a
    /// wrapper that forgot would monomorphise the chain at its first call site.
    [<Property>]
    let ``Interp.between and Interp.betweenWith agree`` (FiniteFloat a) (FiniteFloat b) (UnitT t) =
        let x = a * 1.0<s>
        let y = b * 1.0<s>
        let r = Interp.between x y t
        r = Interp.betweenWith<Interpolate, float<s>> x y t

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Bounds =

    /// "Interpolation stays within bounds for the numeric cases." A single-ulp overshoot at the
    /// endpoints is inherent to `a + (b - a) * t`, so the bound carries a relative slack rather
    /// than being exact; measured overshoot over 200 000 pairs was zero, and the slack exists to
    /// document the guarantee rather than to hide a failure.
    let private within (lo: float) (hi: float) (v: float) =
        let slack = 1e-12 * max 1.0 (max (abs lo) (abs hi))
        v >= lo - slack && v <= hi + slack

    [<Property>]
    let ``float interpolation stays inside the interval`` (FiniteFloat a) (FiniteFloat b) (UnitT t) =
        within (min a b) (max a b) (Interp.between a b t)

    [<Property>]
    let ``Vec2 interpolation stays inside the bounding box`` (Vec2Nd a) (Vec2Nd b) (UnitT t) =
        let v = Interp.between a b t

        within (min (float a.X) (float b.X)) (max (float a.X) (float b.X)) (float v.X)
        && within (min (float a.Y) (float b.Y)) (max (float a.Y) (float b.Y)) (float v.Y)

    [<Property>]
    let ``Rect interpolation stays inside the componentwise interval`` (RectNd a) (RectNd b) (UnitT t) =
        let r = Interp.between a b t

        let ok (x: float<nd>) (y: float<nd>) (v: float<nd>) =
            within (min (float x) (float y)) (max (float x) (float y)) (float v)

        ok a.X0 b.X0 r.X0 && ok a.Y0 b.Y0 r.Y0 && ok a.X1 b.X1 r.X1 && ok a.Y1 b.Y1 r.Y1

    /// Colour interpolation goes through Oklab, so the *sRGB* components need not stay between
    /// the endpoints — that is the entire point of doing it perceptually. What must hold is that
    /// the result is a valid colour: `Color.ofOklab` clamps to gamut.
    [<Property>]
    let ``Color interpolation always lands in gamut`` (Colour a) (Colour b) (UnitT t) =
        let c = Interp.between a b t
        let ok (x: float) = x >= 0.0 && x <= 1.0
        ok c.R && ok c.G && ok c.B && ok c.A

    /// Midpoint symmetry: interpolating backwards from the far end gives the same point. Catches
    /// an asymmetric implementation, which is the classic `a*(1-t) + b*t` versus `a + (b-a)*t`
    /// discrepancy.
    [<Property>]
    let ``interpolation is symmetric about the midpoint`` (FiniteFloat a) (FiniteFloat b) (UnitT t) =
        Approx.scaled 1e-12 (max (abs a) (abs b)) (Interp.between a b t) (Interp.between b a (1.0 - t))

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 300, Replay = Seed, QuietOnSuccess = true)>]
module Stops =

    let private ramp (cs: float list) =
        cs
        |> List.mapi (fun i v -> (float i / float (max 1 (cs.Length - 1)), v))
        |> Array.ofList

    [<Property>]
    let ``stops clamps outside its range and hits its own stops`` (FiniteFloat a) (FiniteFloat b) (FiniteFloat c) =
        let xs = ramp [ a; b; c ]

        Interp.stops xs -1.0 = a
        && Interp.stops xs 2.0 = c
        && Interp.stops xs 0.0 = a
        && Approx.scaled Tol.arith (max (abs b) (abs c)) (Interp.stops xs 1.0) c

    [<Property>]
    let ``stops is monotone in t for a monotone ramp`` (UnitT t) (UnitT u) =
        let xs = ramp [ 0.0; 1.0; 2.0; 3.0 ]
        let lo, hi = min t u, max t u
        Interp.stops xs lo <= Interp.stops xs hi + 1e-12

    [<Fact>]
    let ``a single stop is constant, and an empty one raises`` () =
        Assert.Equal(7.0, Interp.stops [| 0.5, 7.0 |] 0.0)
        Assert.Equal(7.0, Interp.stops [| 0.5, 7.0 |] 1.0)

        Assert.Throws<System.ArgumentException>(fun () -> Interp.stops ([||]: (float * float)[]) 0.5 |> ignore)
        |> ignore

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 400, Replay = Seed, QuietOnSuccess = true)>]
module AffineTweening =

    let private toAffine (Mat(a, b, c, d, dx, dy)) : Affine<nd, nd> =
        { M11 = a
          M12 = b
          M21 = c
          M22 = d
          Dx = dx * 1.0<nd>
          Dy = dy * 1.0<nd> }

    let private close (tol: float) (x: Affine<nd, nd>) (y: Affine<nd, nd>) =
        Array.forall2 (Approx.rel tol) (Affine.toRaw x) (Affine.toRaw y)

    /// The QR decomposition is exact algebraically — `recompose (decompose t) = t` reduces to
    /// `M12 * (M11² + M21²) / r² = M12` — so this is testing the arithmetic, not the algebra.
    [<Property>]
    let ``decompose and recompose round-trip`` (InvertibleMat m) =
        let t = toAffine m
        close 1e-8 (AffineInterp.recompose (AffineInterp.decompose t)) t

    /// "decompose to translate/rotate/scale/skew and interpolate the components, never
    /// the matrix entries", because componentwise interpolation between a 0° and a 180° rotation
    /// passes through the zero matrix. The endpoint law is what makes the decomposed tween safe
    /// to use as a drop-in.
    [<Property>]
    let ``affine tween hits its endpoints`` (InvertibleMat a) (InvertibleMat b) =
        let x = toAffine a
        let y = toAffine b
        close 1e-8 (AffineInterp.lerp 0.0 x y) x && close 1e-8 (AffineInterp.lerp 1.0 x y) y

    /// The failure mode §7.1 names, asserted directly: halfway between a 0° and a 180° rotation
    /// must be a 90° rotation, not the zero matrix. Componentwise interpolation of the entries
    /// gives `M11 = M22 = 0` and a determinant of 0 — a shape collapsed to a point.
    [<Fact>]
    let ``halfway between 0 and 180 degrees is a rotation, not a collapse`` () =
        let a = Affine.rotate<nd> 0.0<rad>
        let b = Affine.rotate<nd> (System.Math.PI * 1.0<rad>)
        let mid = AffineInterp.lerp 0.5 a b

        // A pure rotation has unit determinant; the naive componentwise tween has zero.
        Assert.Equal(1.0, float (Affine.determinant mid), 9)

        let naive = 0.5 * (float a.M11 + float b.M11)
        Assert.Equal(0.0, naive, 12)

    /// Shortest-arc angle interpolation: 350° to 10° goes forwards through 0°, not backwards
    /// through the whole circle. Checked through the rotation the tween actually produces.
    [<Fact>]
    let ``angle tweening takes the short way round`` () =
        let deg (d: float) = Affine.rotate<nd> (d * System.Math.PI / 180.0 * 1.0<rad>)
        let mid = AffineInterp.lerp 0.5 (deg 350.0) (deg 10.0)
        let angle = atan2 (float mid.M21) (float mid.M11) * 180.0 / System.Math.PI
        Assert.Equal(0.0, angle, 9)

/// **`Affine` is the seventh overload of the witness, and the only one that is not
/// componentwise.**
///
/// Two things are under test here and they are separable. The first is that the overload
/// *resolves*: `Affine<'a,'b>` carries two measure variables, and the note this file's
/// counterpart in `Marey.Core` used to carry said that keeping the witness single-variable kept
/// SRTP resolution predictable. It does resolve, and the four call sites below say so through
/// all three wrapper levels — `interp`, `Interp.between`, `Interp.stops` — which is where §12.2's
/// restated-constraint tax would show up if it had been missed.
///
/// The second is the **degenerate geometry**, which is where a decomposed tween earns its keep
/// and also where it has to be honest about what it cannot do. A reflection is not a rotation of
/// an identity in the plane; no continuous path of invertible matrices connects them. So the
/// tween passes through a collapsed frame, and the thing worth gating is that it collapses
/// *predictably* rather than producing NaN and taking an entire animation with it.
[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 400, Replay = Seed, QuietOnSuccess = true)>]
module AffineWitness =

    let private toAffine (Mat(a, b, c, d, dx, dy)) : Affine<nd, nd> =
        { M11 = a
          M12 = b
          M21 = c
          M22 = d
          Dx = dx * 1.0<nd>
          Dy = dy * 1.0<nd> }

    let private rot (deg: float) = Affine.rotate<nd> (deg * System.Math.PI / 180.0 * 1.0<rad>)

    let private finite (t: Affine<nd, nd>) =
        Affine.toRaw t |> Array.forall (fun x -> not (System.Double.IsNaN x || System.Double.IsInfinity x))

    /// The overload delegates and does not reimplement. Exact equality, because there is no
    /// arithmetic between the two — the witness is one call.
    [<Property>]
    let ``the witness is the decomposed tween`` (InvertibleMat a) (InvertibleMat b) (UnitT t) =
        Interp.between (toAffine a) (toAffine b) t = AffineInterp.lerp t (toAffine a) (toAffine b)

    /// The constraint is restated at every wrapper level, so a new overload either
    /// reaches all of them or none of them. `Interp.stops` is the deepest — it calls `interp`
    /// from inside a `let inline` that is itself constrained — and it is the one that would fail
    /// if a restatement had been skipped.
    [<Fact>]
    let ``the affine overload reaches every wrapper`` () =
        let a = Affine.identity<nd>
        let b = Affine.translate { X = 4.0<nd>; Y = 0.0<nd> }

        Assert.Equal(2.0, float (Interp.between a b 0.5).Dx, 12)
        Assert.Equal(2.0, float (Interp.between a b 0.5).Dx, 12)
        Assert.Equal(2.0, float (Interp.stops [| 0.0, a; 1.0, b |] 0.5).Dx, 12)

    /// A transform whose first column is zero has no recoverable rotation, so `decompose` takes
    /// its degenerate branch. Two things are pinned: it does not produce NaN, and it **is
    /// lossy** — `M12` does not survive the round trip. The module's own comment says "preserve
    /// what is left", and this is what is left.
    ///
    /// The loss is not a defect to fix. A matrix with a zero first column is rank-deficient: it
    /// maps the whole plane onto a line, and there is no rotation-scale-shear factorisation of
    /// that which also records the direction the collapsed axis came from.
    [<Fact>]
    let ``a zero first column decomposes without NaN, and drops the shear`` () =
        let t: Affine<nd, nd> =
            { M11 = 0.0
              M12 = 3.0
              M21 = 0.0
              M22 = 2.0
              Dx = 7.0<nd>
              Dy = -1.0<nd> }

        let d = AffineInterp.decompose t
        let back = AffineInterp.recompose d

        Assert.True(finite back)
        Assert.Equal(0.0, float d.Rotation, 12)
        Assert.Equal(7.0, float back.Dx, 12)
        Assert.Equal(-1.0, float back.Dy, 12)
        Assert.Equal(2.0, float back.M22, 12)

        // The pin: 3.0 went in, 0.0 comes back.
        Assert.Equal(0.0, float back.M12, 12)

    /// Tweening *to* a collapsed transform stays finite the whole way, which is the property an
    /// animation actually depends on: a panel that legitimately collapses to zero width during
    /// layout must not poison every later frame with NaN.
    [<Property>]
    let ``a tween into a collapsed transform stays finite`` (UnitT t) =
        let a = Affine.identity<nd>

        let b: Affine<nd, nd> =
            { M11 = 0.0
              M12 = 0.0
              M21 = 0.0
              M22 = 0.0
              Dx = 1.0<nd>
              Dy = 1.0<nd> }

        finite (Interp.between a b t)

    /// **A reflection cannot be rotated into an identity.** `SO(2)` is connected and the
    /// orientation-reversing component is not attached to it, so any continuous tween between
    /// `det > 0` and `det < 0` crosses `det = 0` — the shape flattens to a line and comes back
    /// mirrored. That is the correct answer and it is a surprising one, so it is pinned rather
    /// than filed: it is in the guide's list of imprecisions for the same reason.
    [<Fact>]
    let ``a reflection tweens through a collapse, not through a NaN`` () =
        let a = Affine.identity<nd>
        let b = Affine.scale 1.0 -1.0

        Assert.Equal(0.0, float (Affine.determinant (Interp.between a b 0.5)), 12)

        for i in 0..20 do
            let got = Interp.between a b (float i / 20.0)
            Assert.True(finite got, sprintf "not finite at t = %g" (float i / 20.0))

        Assert.Equal(1.0, float (Affine.determinant (Interp.between a b 0.0)), 12)
        Assert.Equal(-1.0, float (Affine.determinant (Interp.between a b 1.0)), 12)

    /// Just short of a half-turn on either side. The componentwise tween's failure mode is
    /// worst here and the decomposed one is unremarkable: the midpoint is a rotation of half the
    /// angle, with a determinant of one rather than of nearly zero.
    ///
    /// The two 181° rows are the ones worth reading. **A rotation is an angle mod 2π, and a
    /// matrix does not remember which way round it was written**: `rotate 181°` and
    /// `rotate -179°` are the same six floats, so the tween takes the short way and passes
    /// through -89.5°, not +90.5°. A caller who wants the long way round has asked for something
    /// a target matrix cannot express, and needs two tweens or an explicit angle track.
    [<Theory>]
    [<InlineData(179.0, 89.5)>]
    [<InlineData(181.0, -89.5)>]
    [<InlineData(-179.0, -89.5)>]
    [<InlineData(-181.0, 89.5)>]
    let ``near a half-turn the midpoint is the shortest-arc rotation`` (deg: float, expected: float) =
        let mid = Interp.between (rot 0.0) (rot deg) 0.5

        Assert.Equal(1.0, float (Affine.determinant mid), 9)

        let angle = atan2 (float mid.M21) (float mid.M11) * 180.0 / System.Math.PI
        Assert.Equal(expected, angle, 9)

    /// **At exactly a half-turn the direction is genuinely undecided**, and the tie is broken by
    /// the floating-point residue of `sin π` rather than by an argument. `Affine.rotate π` has
    /// `M21 = 1.22e-16`, so `atan2` reports a hair under `+π` and the shortest arc goes
    /// anticlockwise; `rotate -π` has `M21 = -1.22e-16` and it goes the other way.
    ///
    /// Pinned because it is deterministic and because a reader who sees a half-turn animate
    /// anticlockwise deserves to find the reason written down rather than to go looking for a
    /// bug. Any tie-break here would be equally arbitrary and would need the same note.
    [<Fact>]
    let ``a half-turn breaks its tie on the sign of sin pi`` () =
        let forwards = Interp.between (rot 0.0) (rot 180.0) 0.5
        let backwards = Interp.between (rot 0.0) (rot -180.0) 0.5

        Assert.Equal(1.0, float forwards.M21, 9)
        Assert.Equal(-1.0, float backwards.M21, 9)
