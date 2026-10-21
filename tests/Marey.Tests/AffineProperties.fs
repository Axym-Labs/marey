/// The affine laws.
///
/// `compose f g` is "**f then g**". That is the reading Affine.fs chose deliberately —
/// "the one thing every graphics codebase gets wrong at 2am is which side the new transform
/// goes on" — and it is the opposite of matrix-multiplication order, so it is asserted directly
/// rather than trusted: `apply (compose f g) p = apply g (apply f p)`.
///
/// The measures are not decoration here. A map from `'a` to `'b` has a linear part in `float<'b/'a>`
/// and a translation in `float<'b>`, which makes `Affine<nd,px>` and `Affine<px,nd>` different
/// types. Every law below is therefore stated across *three* coordinate spaces (`nd`, `px`, `mm`)
/// rather than one, so that the composition chain §3 claims is "checked end to end" is actually
/// exercised end to end.
module Marey.Tests.AffineProperties

open FsCheck.Xunit
open Xunit
open Marey
open Marey.Tests.Generators

// Three typed readings of the same six generated floats. The generator does not know about
// coordinate spaces; the test does, which is the right split.

let private ndToPx (Mat(a, b, c, d, dx, dy)) : Affine<nd, px> =
    { M11 = a * 1.0<px / nd>
      M12 = b * 1.0<px / nd>
      M21 = c * 1.0<px / nd>
      M22 = d * 1.0<px / nd>
      Dx = dx * 1.0<px>
      Dy = dy * 1.0<px> }

let private pxToMm (Mat(a, b, c, d, dx, dy)) : Affine<px, mm> =
    { M11 = a * 1.0<mm / px>
      M12 = b * 1.0<mm / px>
      M21 = c * 1.0<mm / px>
      M22 = d * 1.0<mm / px>
      Dx = dx * 1.0<mm>
      Dy = dy * 1.0<mm> }

let private mmToMm (Mat(a, b, c, d, dx, dy)) : Affine<mm, mm> =
    { M11 = a
      M12 = b
      M21 = c
      M22 = d
      Dx = dx * 1.0<mm>
      Dy = dy * 1.0<mm> }

let private point (Vec2Nd v) = v

let private closeRaw (tol: float) (x: float[]) (y: float[]) = Array.forall2 (Approx.rel tol) x y

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Composition =

    /// **The composition order, asserted.** If `compose` ever silently becomes "g then f", this
    /// is the test that says so, and it says so in the language of the pipeline rather than of
    /// matrix indices.
    [<Property>]
    let ``apply (compose f g) p = apply g (apply f p)`` (f: Mat) (g: Mat) (p: Vec2Nd) =
        let a = ndToPx f
        let b = pxToMm g
        let v = point p
        let l = Affine.apply (Affine.compose a b) v
        let r = Affine.apply b (Affine.apply a v)

        let scale =
            max 1.0 (max (abs (float l.X)) (abs (float l.Y)))

        Approx.scaled 1e-10 scale (float l.X) (float r.X)
        && Approx.scaled 1e-10 scale (float l.Y) (float r.Y)

    /// Associativity across three distinct coordinate spaces, so the measure arithmetic has to
    /// line up as well as the floats.
    [<Property>]
    let ``compose is associative`` (f: Mat) (g: Mat) (h: Mat) =
        let a = ndToPx f
        let b = pxToMm g
        let c = mmToMm h

        closeRaw 1e-9 (Affine.toRaw (Affine.compose (Affine.compose a b) c)) (Affine.toRaw (Affine.compose a (Affine.compose b c)))

    [<Property>]
    let ``identity is a left unit`` (f: Mat) =
        let a = ndToPx f
        Affine.compose Affine.identity<nd> a = a

    [<Property>]
    let ``identity is a right unit`` (f: Mat) =
        let a = ndToPx f
        Affine.compose a Affine.identity<px> = a

    /// Both units are *exact*, not approximate: composing with the identity multiplies by one
    /// and adds zero, and IEEE 754 is exact for both. Asserting equality rather than closeness
    /// is what would catch an implementation that rebuilt the record through a rounding step.
    [<Property>]
    let ``identity maps every point to itself`` (p: Vec2Nd) =
        Affine.apply Affine.identity<nd> (point p) = point p

    /// `det(f ∘ g) = det f · det g`.
    ///
    /// Stated over well-conditioned matrices. `determinant` is `M11*M22 - M12*M21`, a
    /// subtraction of two products, so its relative accuracy is governed by `norm² / |det|` —
    /// the condition number of the determinant itself. Over the unrestricted generator, whose
    /// entries span twelve decades, that ratio reaches 1e15 and the computed determinant has no
    /// correct digits: `-0.047 * 1.884 - 6901.3 * -7.68e-6` cancels to three significant figures
    /// out of sixteen. That is arithmetic, not a defect in `Affine`.
    ///
    /// `InvertibleMat` bounds the ratio at 1e3, which bounds the composed ratio at ~4e6.
    /// Measured worst relative error under that guard: 2.6e-11 over 200 000 pairs.
    [<Property>]
    let ``determinant is multiplicative under composition`` (InvertibleMat f) (InvertibleMat g) =
        let a = ndToPx f
        let b = pxToMm g

        let l = float (Affine.determinant (Affine.compose a b))
        let r = float (Affine.determinant a) * float (Affine.determinant b)
        Approx.rel 1e-8 l r

    /// The linear-part-only application. Arrow directions, tick offsets and gradient vectors go
    /// through it precisely because a delta must not pick up the translation.
    [<Property>]
    let ``applyVector ignores the translation`` (f: Mat) (p: Vec2Nd) =
        let (Mat(a, b, c, d, _, _)) = f
        let withT = ndToPx f
        let noT = ndToPx (Mat(a, b, c, d, 0.0, 0.0))
        Affine.applyVector withT (point p) = Affine.apply noT (point p)

    /// `apply t (p + v) - apply t p = applyVector t v` — the defining relationship between the
    /// two, and the one that makes "transform a direction" well defined.
    [<Property>]
    let ``apply and applyVector differ exactly by the translation`` (f: Mat) (p: Vec2Nd) (q: Vec2Nd) =
        let t = ndToPx f
        let a = point p
        let b = point q
        let l = Affine.apply t (a + b) - Affine.apply t a
        let r = Affine.applyVector t b

        let scale =
            max 1.0 (max (abs (float r.X)) (abs (float r.Y)))

        Approx.scaled 1e-9 scale (float l.X) (float r.X)
        && Approx.scaled 1e-9 scale (float l.Y) (float r.Y)

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Inversion =

    /// Two conditioning facts govern every law in this module, and both are properties of the
    /// arithmetic rather than of `Affine`.
    ///
    /// **The linear part.** `Affine.tryInvert` only refuses below `|det| < 1e-300`, which is
    /// right for its purpose — a panel legitimately collapses to zero width during layout — but
    /// it means everything above that threshold is invertible and arbitrarily ill-conditioned.
    /// A matrix with a condition number of 1e14 has no digits left after a round trip, so the
    /// *generator* keeps `|det| >= 1e-3 * norm^2`. Measured worst deviation from the identity
    /// under that guard: 1.1e-13 over 200 000 matrices.
    ///
    /// **The translation.** `compose (invert t) t` computes its translation as
    /// `M11·Dx' + M12·Dy' + Dx` — three terms each the size of `t`'s own translation, required
    /// to sum to exactly zero. The absolute error is therefore `eps · |Dx| · cond(t)`, and no
    /// absolute bound is both true and tight: a map that translates by 10^6 px cannot cancel to
    /// 10^-9 px. So the translation is compared against the magnitude it is cancelling, which is
    /// the only statement that is scale-free.
    ///
    /// Both were found by running the whole suite under four other FsCheck seeds; the property
    /// that only holds for the seed it was written against is not a property.
    let private translationScale (Mat(_, _, _, _, dx, dy)) = max 1.0 (max (abs dx) (abs dy))

    let private isIdentity (scale: float) (c: Affine<'a, 'a>) =
        Approx.rel 1e-9 (float c.M11) 1.0
        && Approx.rel 1e-9 (float c.M12) 0.0
        && Approx.rel 1e-9 (float c.M21) 0.0
        && Approx.rel 1e-9 (float c.M22) 1.0
        && Approx.scaled 1e-9 scale (float c.Dx) 0.0
        && Approx.scaled 1e-9 scale (float c.Dy) 0.0

    [<Property>]
    let ``compose t (invert t) is the identity`` (InvertibleMat m) =
        let t = ndToPx m

        match Affine.tryInvert t with
        | None -> false
        | Some inv -> isIdentity (translationScale m) (Affine.compose t inv)

    [<Property>]
    let ``compose (invert t) t is the identity, in the other space`` (InvertibleMat m) =
        let t = ndToPx m

        match Affine.tryInvert t with
        | None -> false
        | Some inv -> isIdentity (translationScale m) (Affine.compose inv t)

    [<Property>]
    let ``invert recovers the original point`` (InvertibleMat m) (p: Vec2Nd) =
        let t = ndToPx m

        match Affine.tryInvert t with
        | None -> false
        | Some inv ->
            let v = point p
            let back = Affine.apply inv (Affine.apply t v)

            // The forward map adds the translation and the inverse subtracts it, so the point's
            // own magnitude is not the scale the error is measured against — the translation is.
            let scale =
                max (translationScale m) (max (abs (float v.X)) (abs (float v.Y)))

            Approx.scaled 1e-8 scale (float back.X) (float v.X)
            && Approx.scaled 1e-8 scale (float back.Y) (float v.Y)

    [<Property>]
    let ``inverting twice returns the original map`` (InvertibleMat m) =
        let t = ndToPx m

        match Affine.tryInvert t |> Option.bind Affine.tryInvert with
        | None -> false
        | Some back ->
            let scale = translationScale m

            Approx.rel 1e-8 (float back.M11) (float t.M11)
            && Approx.rel 1e-8 (float back.M12) (float t.M12)
            && Approx.rel 1e-8 (float back.M21) (float t.M21)
            && Approx.rel 1e-8 (float back.M22) (float t.M22)
            && Approx.scaled 1e-8 scale (float back.Dx) (float t.Dx)
            && Approx.scaled 1e-8 scale (float back.Dy) (float t.Dy)

    [<Fact>]
    let ``a singular map has no inverse`` () =
        let collapsed: Affine<nd, px> =
            { M11 = 1.0<px / nd>
              M12 = 2.0<px / nd>
              M21 = 2.0<px / nd>
              M22 = 4.0<px / nd>
              Dx = 0.0<px>
              Dy = 0.0<px> }

        Assert.True((Affine.tryInvert collapsed).IsNone)

        // A panel collapsed to zero width during layout, which Affine.fs names as the real case.
        let zeroWidth = Affine.scale 0.0<px / nd> 1.0<px / nd>
        Assert.True((Affine.tryInvert zeroWidth).IsNone)

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 400, Replay = Seed, QuietOnSuccess = true)>]
module Constructors =

    [<Property>]
    let ``translate adds the offset and nothing else`` (Vec2Nd d) (Vec2Nd p) =
        Affine.apply (Affine.translate d) p = p + d

    [<Property>]
    let ``rotation preserves length`` (Rotation theta) (Vec2Nd p) =
        let r = Affine.apply (Affine.rotate<nd> theta) p
        Approx.rel 1e-12 (float (Vec2.norm r)) (float (Vec2.norm p))

    [<Property>]
    let ``rotation composes additively`` (Rotation a) (Rotation b) (Vec2Nd p) =
        let l = Affine.apply (Affine.compose (Affine.rotate<nd> a) (Affine.rotate<nd> b)) p
        let r = Affine.apply (Affine.rotate<nd> (a + b)) p
        Approx.vec2 1e-10 l r

    /// `rotateAbout` exists because "rotated axis labels are the reason ... doing it by hand at
    /// every call site is how labels end up 3 px off". Its defining property is that the centre
    /// does not move.
    [<Property>]
    let ``rotateAbout fixes its centre`` (Rotation theta) (Vec2Nd c) =
        let r = Affine.apply (Affine.rotateAbout theta c) c
        Approx.vec2 1e-10 r c

    [<Property>]
    let ``scale multiplies each axis independently`` (FiniteFloat kx) (FiniteFloat ky) (Vec2Nd p) =
        let t = Affine.scale (kx * 1.0<px / nd>) (ky * 1.0<px / nd>)
        let r = Affine.apply t p
        float r.X = kx * float p.X && float r.Y = ky * float p.Y

    /// `fromRects` is "the workhorse" — panel placement, facet offsets, viewport mapping and
    /// zoom are all this one function — so its corner-to-corner property is the single most
    /// load-bearing geometric fact in the layout layer.
    [<Property>]
    let ``fromRects maps the source corners onto the destination corners`` (RectNd src) (RectNd dst') =
        let dst: Rect<px> =
            { X0 = float dst'.X0 * 1.0<px>
              Y0 = float dst'.Y0 * 1.0<px>
              X1 = float dst'.X1 * 1.0<px>
              Y1 = float dst'.Y1 * 1.0<px> }

        // A degenerate source rectangle divides by zero; that is a layout precondition, not an
        // affine law.
        float src.X1 = float src.X0
        || float src.Y1 = float src.Y0
        || (let t = Affine.fromRects src dst
            let lo = Affine.apply t { X = src.X0; Y = src.Y0 }
            let hi = Affine.apply t { X = src.X1; Y = src.Y1 }

            Approx.rel 1e-9 (float lo.X) (float dst.X0)
            && Approx.rel 1e-9 (float lo.Y) (float dst.Y0)
            && Approx.rel 1e-9 (float hi.X) (float dst.X1)
            && Approx.rel 1e-9 (float hi.Y) (float dst.Y1))

    /// The y flip the SVG backend establishes in `Begin` is one `fromRects` call, so its
    /// sign convention is worth pinning: nd y = 0 must land at the *bottom* of the surface.
    [<Fact>]
    let ``the backend's nd-to-px map flips y`` () =
        let t =
            Affine.fromRects
                Rect.unit
                { X0 = 0.0<px>
                  Y0 = 200.0<px>
                  X1 = 320.0<px>
                  Y1 = 0.0<px> }

        let bottom = Affine.apply t { X = 0.0<nd>; Y = 0.0<nd> }
        let top = Affine.apply t { X = 1.0<nd>; Y = 1.0<nd> }

        Assert.Equal(0.0, float bottom.X, 9)
        Assert.Equal(200.0, float bottom.Y, 9)
        Assert.Equal(320.0, float top.X, 9)
        Assert.Equal(0.0, float top.Y, 9)
        Assert.True(float (Affine.determinant t) < 0.0, "the flip must invert orientation")

    /// Affine.fs documents `toRaw` as row-major `[M11; M12; M21; M22; Dx; Dy]` and warns that
    /// SVG's `matrix(a b c d e f)` is a *different* order. That contract is what SvgWriter
    /// depends on, so it is asserted here rather than assumed.
    [<Property>]
    let ``toRaw is row-major`` (f: Mat) =
        let (Mat(a, b, c, d, dx, dy)) = f
        Affine.toRaw (ndToPx f) = [| a; b; c; d; dx; dy |]
