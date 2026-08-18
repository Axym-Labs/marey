/// §7.1 / §13 — "`Interp` endpoint laws".
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
        interp x y 0.0 = x && recovers Tol.arith a b (float (interp x y 1.0))

    /// float32 has an eps of 1.19e-7, so the same law needs a tolerance three orders coarser.
    [<Property>]
    let ``float32 endpoints`` (FiniteFloat a) (FiniteFloat b) =
        let x = float32 a
        let y = float32 b

        interp x y 0.0 = x
        && recovers 1e-6 (float x) (float y) (float (interp x y 1.0))

    [<Property>]
    let ``Vec2 endpoints`` (Vec2Nd a) (Vec2Nd b) =
        let r = interp a b 1.0

        interp a b 0.0 = a
        && recovers Tol.arith (float a.X) (float b.X) (float r.X)
        && recovers Tol.arith (float a.Y) (float b.Y) (float r.Y)

    [<Property>]
    let ``Vec3 endpoints`` (Vec3Nd a) (Vec3Nd b) =
        let r = interp a b 1.0

        interp a b 0.0 = a
        && recovers Tol.arith (float a.X) (float b.X) (float r.X)
        && recovers Tol.arith (float a.Y) (float b.Y) (float r.Y)
        && recovers Tol.arith (float a.Z) (float b.Z) (float r.Z)

    [<Property>]
    let ``Rect endpoints`` (RectNd a) (RectNd b) =
        let r = interp a b 1.0

        interp a b 0.0 = a
        && recovers Tol.arith (float a.X0) (float b.X0) (float r.X0)
        && recovers Tol.arith (float a.Y0) (float b.Y0) (float r.Y0)
        && recovers Tol.arith (float a.X1) (float b.X1) (float r.X1)
        && recovers Tol.arith (float a.Y1) (float b.Y1) (float r.Y1)

    /// F1 again: the tolerance here is a property of Oklab, not of `interp`. Measured worst
    /// endpoint deviation over 100 000 random colour pairs: 1.66e-6.
    [<Property>]
    let ``Color endpoints, within the Oklab round-trip floor`` (Colour a) (Colour b) =
        Approx.colour (interp a b 0.0) a && Approx.colour (interp a b 1.0) b

    /// The witness is reachable through all three published spellings, and they agree. §12.2 is
    /// the reason there are three: every wrapper level has to restate the constraint, and a
    /// wrapper that forgot would monomorphise the chain at its first call site.
    [<Property>]
    let ``interp, interpWith and Interp.between agree`` (FiniteFloat a) (FiniteFloat b) (UnitT t) =
        let x = a * 1.0<s>
        let y = b * 1.0<s>
        let r = interp x y t
        r = interpWith<Interpolate, float<s>> x y t && r = Interp.between x y t

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
        within (min a b) (max a b) (interp a b t)

    [<Property>]
    let ``Vec2 interpolation stays inside the bounding box`` (Vec2Nd a) (Vec2Nd b) (UnitT t) =
        let v = interp a b t

        within (min (float a.X) (float b.X)) (max (float a.X) (float b.X)) (float v.X)
        && within (min (float a.Y) (float b.Y)) (max (float a.Y) (float b.Y)) (float v.Y)

    [<Property>]
    let ``Rect interpolation stays inside the componentwise interval`` (RectNd a) (RectNd b) (UnitT t) =
        let r = interp a b t

        let ok (x: float<nd>) (y: float<nd>) (v: float<nd>) =
            within (min (float x) (float y)) (max (float x) (float y)) (float v)

        ok a.X0 b.X0 r.X0 && ok a.Y0 b.Y0 r.Y0 && ok a.X1 b.X1 r.X1 && ok a.Y1 b.Y1 r.Y1

    /// Colour interpolation goes through Oklab, so the *sRGB* components need not stay between
    /// the endpoints — that is the entire point of doing it perceptually. What must hold is that
    /// the result is a valid colour: `Color.ofOklab` clamps to gamut.
    [<Property>]
    let ``Color interpolation always lands in gamut`` (Colour a) (Colour b) (UnitT t) =
        let c = interp a b t
        let ok (x: float) = x >= 0.0 && x <= 1.0
        ok c.R && ok c.G && ok c.B && ok c.A

    /// Midpoint symmetry: interpolating backwards from the far end gives the same point. Catches
    /// an asymmetric implementation, which is the classic `a*(1-t) + b*t` versus `a + (b-a)*t`
    /// discrepancy.
    [<Property>]
    let ``interpolation is symmetric about the midpoint`` (FiniteFloat a) (FiniteFloat b) (UnitT t) =
        Approx.scaled 1e-12 (max (abs a) (abs b)) (interp a b t) (interp b a (1.0 - t))

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

    /// §7.1 — "decompose to translate/rotate/scale/skew and interpolate the components, never
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
