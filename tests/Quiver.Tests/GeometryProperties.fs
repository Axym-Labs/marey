/// §3 / §9 / §13 — geometry and layout invariants.
///
/// §13 asks for "layout invariants (no overlapping panels, labels inside bounds)". The layout
/// pass itself lives in `Quiver.Plot`, which is not in scope for this file — but every invariant
/// it will have to maintain is a property of the `Rect` and `Affine` primitives it is built out
/// of, and those are in Core and frozen. So the invariants are stated here against the
/// primitives: rectangle subdivision produces disjoint cells, an inset stays inside, and a
/// measured text box contains the text it measured.
///
/// `Affine.mapRectBounds` gets particular attention because it is the function Affine.fs flags
/// as "exactly when hand-rolled bounding-box code goes wrong": under rotation the image of a box
/// is not a box, and taking the axis-aligned bounds of the transformed *corners* is only correct
/// because an affine map sends the convex hull of the corners to the convex hull of the images.
module Quiver.Tests.GeometryProperties

open FsCheck.Xunit
open Xunit
open Quiver
open Quiver.Tests.Generators

let private nonDegenerate (r: Rect<nd>) =
    float r.X1 > float r.X0 && float r.Y1 > float r.Y0

[<Properties(Arbitrary = [| typeof<QuiverArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Rectangles =

    [<Property>]
    let ``width, height and area agree`` (RectNd r) =
        Approx.rel 1e-12 (float (Rect.area r)) (float (Rect.width r) * float (Rect.height r))

    [<Property>]
    let ``a rectangle contains its own centre and corners`` (RectNd r) =
        Rect.contains (Rect.centre r) r
        && Rect.contains { X = r.X0; Y = r.Y0 } r
        && Rect.contains { X = r.X1; Y = r.Y1 } r

    [<Property>]
    let ``the centre is the midpoint of both extents`` (RectNd r) =
        let c = Rect.centre r

        Approx.rel 1e-12 (float c.X) ((float r.X0 + float r.X1) / 2.0)
        && Approx.rel 1e-12 (float c.Y) ((float r.Y0 + float r.Y1) / 2.0)

    [<Property>]
    let ``intersects is symmetric`` (RectNd a) (RectNd b) =
        Rect.intersects a b = Rect.intersects b a

    [<Property>]
    let ``a non-degenerate rectangle intersects itself`` (RectNd r) =
        not (nonDegenerate r) || Rect.intersects r r

    [<Property>]
    let ``intersecting rectangles share a point`` (RectNd a) (RectNd b) =
        not (Rect.intersects a b)
        || (let p: Vec2<nd> =
                { X = (max (float a.X0) (float b.X0) + min (float a.X1) (float b.X1)) / 2.0 * 1.0<nd>
                  Y = (max (float a.Y0) (float b.Y0) + min (float a.Y1) (float b.Y1)) / 2.0 * 1.0<nd> }

            Rect.contains p a && Rect.contains p b)

    [<Property>]
    let ``inset and its negative are inverses`` (RectNd r) (FiniteFloat d) =
        Approx.rect 1e-9 (Rect.inset (-d * 1.0<nd>) (Rect.inset (d * 1.0<nd>) r)) r

    /// The layout invariant, in its simplest form: padding never escapes the box it pads.
    [<Property>]
    let ``a positive inset stays inside the original`` (RectNd r) (UnitT t) =
        let w = min (float (Rect.width r)) (float (Rect.height r))
        let d = t * w / 2.0 * 1.0<nd>
        let inner = Rect.inset d r

        float inner.X0 >= float r.X0
        && float inner.Y0 >= float r.Y0
        && float inner.X1 <= float r.X1
        && float inner.Y1 <= float r.Y1

    [<Property>]
    let ``insetBy applies each side independently`` (RectNd r) (UnitT a) (UnitT b) =
        let l = a * float (Rect.width r) / 4.0 * 1.0<nd>
        let t = b * float (Rect.height r) / 4.0 * 1.0<nd>
        let inner = Rect.insetBy l t l t r

        Approx.rel 1e-9 (float (Rect.width inner)) (float (Rect.width r) - 2.0 * float l)
        && Approx.rel 1e-9 (float (Rect.height inner)) (float (Rect.height r) - 2.0 * float t)

    [<Fact>]
    let ``the unit panel is [0,1] squared`` () =
        Assert.Equal(1.0, float (Rect.area Rect.unit), 12)
        Assert.Equal(0.5, float (Rect.centre Rect.unit).X, 12)

[<Properties(Arbitrary = [| typeof<QuiverArbs> |], MaxTest = 300, Replay = Seed, QuietOnSuccess = true)>]
module PanelLayout =

    /// The facet grid, as `Rect` subdivision. §9's layout pass is "nothing but rectangle
    /// subdivision", so the invariant §13 names — **no overlapping panels** — is a property of
    /// the subdivision arithmetic and can be stated without the layout pass existing yet.
    let private grid (cols: int) (rows: int) (gutter: float) (r: Rect<nd>) =
        let w = float (Rect.width r)
        let h = float (Rect.height r)
        let cw = w / float cols
        let ch = h / float rows

        [| for j in 0 .. rows - 1 do
               for i in 0 .. cols - 1 do
                   yield
                       { X0 = (float r.X0 + float i * cw + gutter / 2.0) * 1.0<nd>
                         Y0 = (float r.Y0 + float j * ch + gutter / 2.0) * 1.0<nd>
                         X1 = (float r.X0 + float (i + 1) * cw - gutter / 2.0) * 1.0<nd>
                         Y1 = (float r.Y0 + float (j + 1) * ch - gutter / 2.0) * 1.0<nd> } |]

    [<Property>]
    let ``a gutter-separated facet grid has no overlapping panels`` (RectNd r) (UnitT g) =
        let cols, rows = 3, 4
        let cw = float (Rect.width r) / float cols
        let ch = float (Rect.height r) / float rows
        // A gutter wider than the cell inverts the panel, which is a layout bug, not a Rect one.
        let gutter = g * min cw ch * 0.5
        let cells = grid cols rows gutter r

        cells.Length = cols * rows
        && Seq.forall
            id
            (seq {
                for i in 0 .. cells.Length - 1 do
                    for j in i + 1 .. cells.Length - 1 do
                        yield not (Rect.intersects cells.[i] cells.[j])
            })

    [<Property>]
    let ``every panel stays inside the figure`` (RectNd r) (UnitT g) =
        let cols, rows = 3, 4
        let cw = float (Rect.width r) / float cols
        let ch = float (Rect.height r) / float rows
        let gutter = g * min cw ch * 0.5

        grid cols rows gutter r
        |> Array.forall (fun c ->
            float c.X0 >= float r.X0 - 1e-9
            && float c.Y0 >= float r.Y0 - 1e-9
            && float c.X1 <= float r.X1 + 1e-9
            && float c.Y1 <= float r.Y1 + 1e-9)

[<Properties(Arbitrary = [| typeof<QuiverArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module MapRectBounds =

    let private toPx (Mat(a, b, c, d, dx, dy)) : Affine<nd, px> =
        { M11 = a * 1.0<px / nd>
          M12 = b * 1.0<px / nd>
          M21 = c * 1.0<px / nd>
          M22 = d * 1.0<px / nd>
          Dx = dx * 1.0<px>
          Dy = dy * 1.0<px> }

    /// The correctness statement: the bounds contain the image of **every** point of the source
    /// rectangle, not just the corners. Sampled on a lattice, because an affine map sends the
    /// convex hull to the convex hull and the lattice covers it densely enough to catch a wrong
    /// min/max.
    [<Property>]
    let ``bounds contain the image of every interior point`` (RectNd r) (m: Mat) =
        let t = toPx m
        let b = Affine.mapRectBounds t r

        let slack =
            1e-9 * max 1.0 (max (abs (float b.X1 - float b.X0)) (abs (float b.Y1 - float b.Y0)))

        Seq.forall
            id
            (seq {
                for i in 0..4 do
                    for j in 0..4 do
                        let p: Vec2<nd> =
                            { X = float r.X0 + (float r.X1 - float r.X0) * float i / 4.0 |> (*) 1.0<nd>
                              Y = float r.Y0 + (float r.Y1 - float r.Y0) * float j / 4.0 |> (*) 1.0<nd> }

                        let q = Affine.apply t p

                        yield
                            float q.X >= float b.X0 - slack
                            && float q.X <= float b.X1 + slack
                            && float q.Y >= float b.Y0 - slack
                            && float q.Y <= float b.Y1 + slack
            })

    [<Property>]
    let ``bounds are tight, in the sense that each edge is attained by a corner`` (RectNd r) (m: Mat) =
        let t = toPx m
        let b = Affine.mapRectBounds t r

        let corners =
            [| { X = r.X0; Y = r.Y0 }
               { X = r.X1; Y = r.Y0 }
               { X = r.X1; Y = r.Y1 }
               { X = r.X0; Y = r.Y1 } |]
            |> Array.map (Affine.apply t)

        let xs = corners |> Array.map (fun p -> float p.X)
        let ys = corners |> Array.map (fun p -> float p.Y)

        float b.X0 = Array.min xs
        && float b.X1 = Array.max xs
        && float b.Y0 = Array.min ys
        && float b.Y1 = Array.max ys

    [<Property>]
    let ``bounds under the identity are the rectangle itself`` (RectNd r) =
        Affine.mapRectBounds Affine.identity<nd> r = r

    /// The case that motivates the function. A 45° rotation of the unit square has bounds of
    /// side sqrt(2), *not* side 1 — which is what naive per-component transformation gives.
    [<Fact>]
    let ``a 45 degree rotation grows the bounding box by sqrt 2`` () =
        let r: Rect<nd> =
            { X0 = -0.5<nd>
              Y0 = -0.5<nd>
              X1 = 0.5<nd>
              Y1 = 0.5<nd> }

        let b =
            Affine.mapRectBounds (Affine.rotate<nd> (System.Math.PI / 4.0 * 1.0<rad>)) r

        Assert.Equal(sqrt 2.0, float (Rect.width b), 12)
        Assert.Equal(sqrt 2.0, float (Rect.height b), 12)

    [<Property>]
    let ``a quarter turn swaps width and height`` (RectNd r) =
        let b =
            Affine.mapRectBounds (Affine.rotate<nd> (System.Math.PI / 2.0 * 1.0<rad>)) r

        Approx.rel 1e-9 (float (Rect.width b)) (float (Rect.height r))
        && Approx.rel 1e-9 (float (Rect.height b)) (float (Rect.width r))

[<Properties(Arbitrary = [| typeof<QuiverArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Vectors =

    [<Property>]
    let ``norm is the square root of normSq`` (Vec2Nd a) =
        Approx.rel 1e-12 (float (Vec2.norm a)) (sqrt (float (Vec2.normSq a)))

    [<Property>]
    let ``normalize produces a unit vector, or zero`` (Vec2Nd a) =
        let n = Vec2.normalize a

        if float (Vec2.norm a) = 0.0 then
            n = { X = 0.0; Y = 0.0 }
        else
            Approx.rel 1e-12 (float (Vec2.norm n)) 1.0

    [<Property>]
    let ``perp is orthogonal and length-preserving`` (Vec2Nd a) =
        let p = Vec2.perp a
        let scale = max 1.0 (float (Vec2.normSq a))

        Approx.scaled 1e-12 scale (float (Vec2.dot a p)) 0.0
        && Approx.rel 1e-12 (float (Vec2.norm p)) (float (Vec2.norm a))

    [<Property>]
    let ``cross is antisymmetric and dot is symmetric`` (Vec2Nd a) (Vec2Nd b) =
        let scale = max 1.0 (float (Vec2.norm a) * float (Vec2.norm b))

        Approx.scaled 1e-12 scale (float (Vec2.cross a b)) (-(float (Vec2.cross b a)))
        && float (Vec2.dot a b) = float (Vec2.dot b a)

    [<Property>]
    let ``rotate preserves length and composes additively`` (Vec2Nd a) (Rotation t) (Rotation u) =
        Approx.rel 1e-12 (float (Vec2.norm (Vec2.rotate t a))) (float (Vec2.norm a))
        && Approx.vec2 1e-10 (Vec2.rotate u (Vec2.rotate t a)) (Vec2.rotate (t + u) a)

    [<Property>]
    let ``angle and norm reconstruct the vector`` (Vec2Nd a) =
        let n = float (Vec2.norm a)
        let th = float (Vec2.angle a)

        n = 0.0
        || (Approx.rel 1e-9 (n * cos th) (float a.X)
            && Approx.rel 1e-9 (n * sin th) (float a.Y))

    [<Property>]
    let ``Vec3 cross is orthogonal to both factors`` (Vec3Nd a) (Vec3Nd b) =
        let c = Vec3.cross a b
        let scale = max 1.0 (float (Vec3.norm a) * float (Vec3.norm b))

        Approx.scaled 1e-11 scale (float (Vec3.dot a c)) 0.0
        && Approx.scaled 1e-11 scale (float (Vec3.dot b c)) 0.0

    /// D2 — measure-changing operations are module functions, never operators, because §12.1
    /// proves an operator over-constrains. The payoff is that this line type-checks and its
    /// result really is `Vec2<m>`.
    [<Fact>]
    let ``scaleBy and divBy change the measure`` () =
        let velocity: Vec2<m / s> = { X = 3.0<m / s>; Y = 4.0<m / s> }
        let dt = 2.0<s>
        let displacement: Vec2<m> = Vec2.scaleBy dt velocity
        Assert.Equal(6.0, float displacement.X, 12)

        let back: Vec2<m / s> = Vec2.divBy dt displacement
        Assert.Equal(3.0, float back.X, 12)

        // `sqrt` is measure-aware: float<m^2> -> float<m>.
        let speed: float<m / s> = Vec2.norm velocity
        Assert.Equal(5.0, float speed, 12)

[<Properties(Arbitrary = [| typeof<QuiverArbs> |], MaxTest = 300, Replay = Seed, QuietOnSuccess = true)>]
module TextBounds =

    let private measurer = TextMeasure.approx

    let private font: FontSpec =
        { Family = "Helvetica"
          Size = 12.0<pt>
          Weight = 400
          Italic = false }

    /// §13's "labels inside bounds", at the only level Core can state it: the box a label
    /// reports is the box its advance width actually occupies, for every anchor.
    [<Property>]
    let ``the measured box is exactly one advance wide`` (anchor: TextAnchor) =
        let s = "Wm 1.5 km"
        let b = TextMeasure.bounds measurer font anchor BaselineAlphabetic 0.0<rad> s
        let m = measurer.Measure font s
        Approx.rel 1e-12 (float (Rect.width b)) (float m.Advance)

    [<Fact>]
    let ``anchoring places the box relative to the origin`` () =
        let s = "label"
        let m = measurer.Measure font s

        let box a =
            TextMeasure.bounds measurer font a BaselineAlphabetic 0.0<rad> s

        Assert.Equal(0.0, float (box AnchorStart).X0, 9)
        Assert.Equal(-float m.Advance / 2.0, float (box AnchorMiddle).X0, 9)
        Assert.Equal(-float m.Advance, float (box AnchorEnd).X0, 9)

    [<Fact>]
    let ``baselines place the box vertically`` () =
        let s = "label"
        let m = measurer.Measure font s

        let box b =
            TextMeasure.bounds measurer font AnchorStart b 0.0<rad> s

        Assert.Equal(-float m.Ascent, float (box BaselineAlphabetic).Y0, 9)
        Assert.Equal(0.0, float (box BaselineHanging).Y0, 9)
        Assert.Equal(float m.Ascent + float m.Descent, float (box BaselineHanging).Y1, 9)

    /// A rotated label's box is the axis-aligned bounds of the rotated box — which is why
    /// `mapRectBounds` had to be correct under rotation before layout could be.
    [<Property>]
    let ``a rotated label box is the rotated box's bounds`` (Rotation theta) =
        let s = "rotated tick label"
        let upright = TextMeasure.bounds measurer font AnchorMiddle BaselineMiddle 0.0<rad> s
        let rotated = TextMeasure.bounds measurer font AnchorMiddle BaselineMiddle theta s

        theta = 0.0<rad>
        || Approx.rect 1e-9 rotated (Affine.mapRectBounds (Affine.rotate<px> theta) upright)

    [<Property>]
    let ``measured width grows with the string`` (Rotation _ignored) =
        let advance (s: string) = float (measurer.Measure font s).Advance
        advance "" = 0.0 && advance "ab" > advance "a" && advance "a" > 0.0

    /// The fallback table exists because "unit symbols are full of these — ·, ², ⁻, Ω, µ, Å —
    /// so guessing badly here shows up directly in axis-label layout". A zero-width answer would
    /// collapse every unit-bearing tick label.
    [<Fact>]
    let ``non-ASCII unit symbols have a non-zero advance`` () =
        for s in [ "kg·m·s⁻²"; "µm"; "Ω"; "Å"; "°C" ] do
            Assert.True(float (measurer.Measure font s).Advance > 0.0, s)
