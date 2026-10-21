namespace Marey

/// A 2-D affine map between two *coordinate spaces*, checked by the compiler.
///
/// ```text
///     | M11 M12 Dx |     x' = M11·x + M12·y + Dx
///     | M21 M22 Dy |     y' = M21·x + M22·y + Dy
///     |  0   0   1 |
/// ```
///
/// The dimensions fall out of the arithmetic and they are not decoration: a map from `'a` to
/// `'b` must have a linear part in `'b/'a` and a translation in `'b`, or the multiply does
/// not type-check. That makes `Affine<nd,px>` and `Affine<px,nd>` different types, and it
/// makes the §3 claim — "the composition `data -> nd -> px -> device` is checked end to end"
/// — literally true rather than aspirational.
///
/// ---
///
/// **Why this is a record and not a function.** The spike models the IR's transform node as
/// `Transform of (Vec2<nd> -> Vec2<nd>) * Node`. That is a latent design failure with two
/// heads:
///
/// * §5.2 wants scene diffing from F#'s structural equality on immutable values. A closure
///   has reference equality, so a single function field makes every enclosing `Node`
///   compare unequal to itself and collapses `Keyed` diffing to "redraw everything".
/// * §15 wants a serializable figure format. A closure cannot be written to a file.
///
/// Six floats have neither problem, and every backend can hand them straight to its native
/// transform (SVG `matrix(...)`, TikZ `cm=`, Skia `SKMatrix`).
[<Struct>]
type Affine<[<Measure>] 'a, [<Measure>] 'b> =
    { M11: float<'b / 'a>
      M12: float<'b / 'a>
      M21: float<'b / 'a>
      M22: float<'b / 'a>
      Dx: float<'b>
      Dy: float<'b> }

[<RequireQualifiedAccess>]
module Affine =

    /// Below which a determinant counts as singular. Panels can legitimately collapse to
    /// zero width during layout, so this is a real case, not a defensive one.
    [<Literal>]
    let private SingularEpsilon = 1e-300

    let identity<[<Measure>] 'a> : Affine<'a, 'a> =
        { M11 = 1.0
          M12 = 0.0
          M21 = 0.0
          M22 = 1.0
          Dx = LanguagePrimitives.FloatWithMeasure<'a> 0.0
          Dy = LanguagePrimitives.FloatWithMeasure<'a> 0.0 }

    /// Apply the map. `Vec2<'a> -> Vec2<'b>` — the whole point.
    let apply (t: Affine<'a, 'b>) (p: Vec2<'a>) : Vec2<'b> =
        { X = t.M11 * p.X + t.M12 * p.Y + t.Dx
          Y = t.M21 * p.X + t.M22 * p.Y + t.Dy }

    /// Apply only the linear part — the correct transform for a *direction* or a *delta*,
    /// which must not pick up the translation. Arrow directions, tick offsets and gradient
    /// vectors all go through here rather than `apply`.
    let applyVector (t: Affine<'a, 'b>) (p: Vec2<'a>) : Vec2<'b> =
        { X = t.M11 * p.X + t.M12 * p.Y
          Y = t.M21 * p.X + t.M22 * p.Y }

    /// `compose f g` is "f then g": `Vec2<'a> -> Vec2<'b> -> Vec2<'c>`.
    ///
    /// Reading order matches data flow rather than matrix-multiplication order, deliberately.
    /// The one thing every graphics codebase gets wrong at 2am is which side the new
    /// transform goes on; naming the arguments after the pipeline makes it unambiguous.
    let compose (f: Affine<'a, 'b>) (g: Affine<'b, 'c>) : Affine<'a, 'c> =
        { M11 = g.M11 * f.M11 + g.M12 * f.M21
          M12 = g.M11 * f.M12 + g.M12 * f.M22
          M21 = g.M21 * f.M11 + g.M22 * f.M21
          M22 = g.M21 * f.M12 + g.M22 * f.M22
          Dx = g.M11 * f.Dx + g.M12 * f.Dy + g.Dx
          Dy = g.M21 * f.Dx + g.M22 * f.Dy + g.Dy }

    let determinant (t: Affine<'a, 'b>) : float<'b^2 / 'a^2> = t.M11 * t.M22 - t.M12 * t.M21

    /// `None` when the map is singular — a collapsed panel, a zero-span domain. Hit-testing
    /// and inverse-projection (screen point back to data value) are the callers that care.
    let tryInvert (t: Affine<'a, 'b>) : Affine<'b, 'a> option =
        let det = determinant t

        if abs (float det) < SingularEpsilon then
            None
        else
            let m11 = t.M22 / det
            let m12 = -t.M12 / det
            let m21 = -t.M21 / det
            let m22 = t.M11 / det

            Some
                { M11 = m11
                  M12 = m12
                  M21 = m21
                  M22 = m22
                  Dx = -(m11 * t.Dx + m12 * t.Dy)
                  Dy = -(m21 * t.Dx + m22 * t.Dy) }

    /// Translation within a space.
    let translate (d: Vec2<'a>) : Affine<'a, 'a> =
        { identity<'a> with Dx = d.X; Dy = d.Y }

    /// Uniform scale that *changes* space: `float<'b/'a>` in, `Affine<'a,'b>` out. This is
    /// the nd -> px step in one line.
    let scaleUniform (k: float<'b / 'a>) : Affine<'a, 'b> =
        { M11 = k
          M12 = LanguagePrimitives.FloatWithMeasure<'b / 'a> 0.0
          M21 = LanguagePrimitives.FloatWithMeasure<'b / 'a> 0.0
          M22 = k
          Dx = LanguagePrimitives.FloatWithMeasure<'b> 0.0
          Dy = LanguagePrimitives.FloatWithMeasure<'b> 0.0 }

    /// Independent x/y scale. Anisotropic by design: aspect ratio is a *policy* decision made
    /// by the layout layer, not something geometry should quietly enforce.
    let scale (kx: float<'b / 'a>) (ky: float<'b / 'a>) : Affine<'a, 'b> =
        { M11 = kx
          M12 = LanguagePrimitives.FloatWithMeasure<'b / 'a> 0.0
          M21 = LanguagePrimitives.FloatWithMeasure<'b / 'a> 0.0
          M22 = ky
          Dx = LanguagePrimitives.FloatWithMeasure<'b> 0.0
          Dy = LanguagePrimitives.FloatWithMeasure<'b> 0.0 }

    /// The measure parameter is declared explicitly rather than inferred. Without it F#
    /// rejects `rotate<px> theta` outright (FS0686), and the same rule that §12.1 imposes on
    /// `U.mul`/`U.div` applies here: a measure-generic function that anyone needs to
    /// instantiate by hand must say so in its signature.
    let rotate<[<Measure>] 'a> (theta: float<rad>) : Affine<'a, 'a> =
        let t = theta / 1.0<rad>
        let c = cos t
        let s = sin t

        { M11 = c
          M12 = -s
          M21 = s
          M22 = c
          Dx = LanguagePrimitives.FloatWithMeasure<'a> 0.0
          Dy = LanguagePrimitives.FloatWithMeasure<'a> 0.0 }

    /// Rotate about a point rather than the origin. Rotated axis labels are the reason this
    /// exists; doing it by hand at every call site is how labels end up 3 px off.
    let rotateAbout (theta: float<rad>) (centre: Vec2<'a>) : Affine<'a, 'a> =
        compose (translate -centre) (compose (rotate<'a> theta) (translate centre))

    /// Map one box onto another. **The workhorse.** Panel placement, facet offsets, viewport
    /// mapping and zoom are all this function, and expressing them as one operation is what
    /// keeps §5's `Transform` node sufficient for all of them.
    ///
    /// Note the y axis is *not* flipped: `nd` is y-up like the data, and the flip to y-down
    /// device space is the backend's business, applied once in `Begin`.
    let fromRects (src: Rect<'a>) (dst: Rect<'b>) : Affine<'a, 'b> =
        let sw = src.X1 - src.X0
        let sh = src.Y1 - src.Y0
        let kx = (dst.X1 - dst.X0) / sw
        let ky = (dst.Y1 - dst.Y0) / sh

        { M11 = kx
          M12 = LanguagePrimitives.FloatWithMeasure<'b / 'a> 0.0
          M21 = LanguagePrimitives.FloatWithMeasure<'b / 'a> 0.0
          M22 = ky
          Dx = dst.X0 - kx * src.X0
          Dy = dst.Y0 - ky * src.Y0 }

    /// Transform a rectangle and take the axis-aligned bounds of the result. Correct under
    /// rotation, where the image of a box is not a box — which is exactly when hand-rolled
    /// bounding-box code goes wrong.
    let mapRectBounds (t: Affine<'a, 'b>) (r: Rect<'a>) : Rect<'b> =
        let a = apply t { X = r.X0; Y = r.Y0 }
        let b = apply t { X = r.X1; Y = r.Y0 }
        let c = apply t { X = r.X1; Y = r.Y1 }
        let d = apply t { X = r.X0; Y = r.Y1 }

        { X0 = min (min a.X b.X) (min c.X d.X)
          Y0 = min (min a.Y b.Y) (min c.Y d.Y)
          X1 = max (max a.X b.X) (max c.X d.X)
          Y1 = max (max a.Y b.Y) (max c.Y d.Y) }

    /// Row-major `[ M11; M12; M21; M22; Dx; Dy ]`, measures erased.
    ///
    /// Backends want this in their own order — SVG's `matrix(a b c d e f)` is column-major
    /// `[M11; M21; M12; M22; Dx; Dy]`, which is *not* the same list. Reordering is the
    /// backend's job; Core hands over one documented convention and no more.
    let toRaw (t: Affine<'a, 'b>) =
        [| float t.M11; float t.M12; float t.M21; float t.M22; float t.Dx; float t.Dy |]
