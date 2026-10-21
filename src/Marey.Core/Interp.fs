namespace Marey

/// "affine transforms (decompose to translate/rotate/scale/skew and interpolate the
/// components, never the matrix entries)."
///
/// Componentwise interpolation of the matrix entries between a 0° and a 180° rotation passes
/// through the *zero matrix*: the shape collapses to a point and pops back out. Decomposing
/// first is the difference between a zoom that looks intentional and one that looks broken.
///
/// **This sits above the witness, and `Interpolate` has an `Affine` overload that delegates here.**
/// An earlier version kept the module outside the witness because `Affine<'a,'b>` carries two
/// measure variables and single-variable overloads keep SRTP resolution predictable. That was a
/// precaution, not a measurement: the two-variable overload resolves, and it resolves for the same
/// reason the one-variable ones do — `^T` is concrete at every call site, so the measures are
/// decided before the constraint is solved.
///
/// The precaution was worth taking and worth dropping. It cost nothing to hold, and holding it past
/// the point where an animation wants to tween a transform would have cost the restated-constraint
/// tax at every wrapper that carries one: the alternative to an overload is a second spelling of
/// `Interp.between` that a caller has to know to reach for.
///
/// The functions stay public. A caller who wants the *components* — a renderer that needs the
/// rotation on its own — should not have to interpolate a matrix to get at them.
[<RequireQualifiedAccess>]
module AffineInterp =

    /// QR-style decomposition: `M = R(theta) · [[sx, sy·k], [0, sy]]`, plus a translation.
    [<Struct>]
    type Decomposed<[<Measure>] 'a, [<Measure>] 'b> =
        { Translate: Vec2<'b>
          Rotation: float<rad>
          ScaleX: float<'b / 'a>
          ScaleY: float<'b / 'a>
          /// Dimensionless shear factor.
          Shear: float }

    let decompose (t: Affine<'a, 'b>) : Decomposed<'a, 'b> =
        let det = Affine.determinant t
        let r = sqrt (t.M11 * t.M11 + t.M21 * t.M21)

        if float r = 0.0 then
            // Degenerate: no recoverable rotation. Preserve what is left rather than
            // producing NaNs that then propagate through an entire animation.
            { Translate = { X = t.Dx; Y = t.Dy }
              Rotation = 0.0<rad>
              ScaleX = LanguagePrimitives.FloatWithMeasure<'b / 'a> 0.0
              ScaleY = t.M22
              Shear = 0.0 }
        else
            let sx = r
            let sy = det / r

            { Translate = { X = t.Dx; Y = t.Dy }
              Rotation = atan2 (float t.M21) (float t.M11) * 1.0<rad>
              ScaleX = sx
              ScaleY = sy
              Shear = if float det = 0.0 then 0.0 else float ((t.M11 * t.M12 + t.M21 * t.M22) / det) }

    let recompose (d: Decomposed<'a, 'b>) : Affine<'a, 'b> =
        let th = d.Rotation / 1.0<rad>
        let c = cos th
        let s = sin th
        let shx = d.ScaleY * d.Shear

        { M11 = c * d.ScaleX
          M12 = c * shx - s * d.ScaleY
          M21 = s * d.ScaleX
          M22 = s * shx + c * d.ScaleY
          Dx = d.Translate.X
          Dy = d.Translate.Y }

    /// Shortest-arc angle interpolation, so a 350° -> 10° tween goes forwards through 0°
    /// rather than backwards through the whole circle.
    let private lerpAngle (a: float<rad>) (b: float<rad>) (t: float) : float<rad> =
        let twoPi = 2.0 * System.Math.PI * 1.0<rad>
        let mutable d = (b - a) % twoPi

        if d > twoPi / 2.0 then d <- d - twoPi
        elif d < -twoPi / 2.0 then d <- d + twoPi

        a + d * t

    let lerp (t: float) (a: Affine<'a, 'b>) (b: Affine<'a, 'b>) : Affine<'a, 'b> =
        let da = decompose a
        let db = decompose b

        recompose
            { Translate = Vec2.lerp t da.Translate db.Translate
              Rotation = lerpAngle da.Rotation db.Rotation t
              ScaleX = da.ScaleX + (db.ScaleX - da.ScaleX) * t
              ScaleY = da.ScaleY + (db.ScaleY - da.ScaleY) * t
              Shear = da.Shear + (db.Shear - da.Shear) * t }


/// Interpolation as a type class.
///
/// F# has no higher-kinded types, so this is SRTP against a witness type. Three findings are baked
/// into the shape of this file, and every one of them cost real time to
/// diagnose:
///
/// * **Every overload is measure-generic from its first line.** An `Interp` declared
///   as `(a: float, ...)` *rejects* `float<m>`: measures are erased at runtime but they are
///   absolutely present during overload resolution. `float<'u>` subsumes the unitless case,
///   because `float = float<1>`.
/// * **The member is called `Interp`, never `Lerp`.** .NET 9 added `System.Double.Lerp`
///   to the numeric interfaces, and it produces an ambiguous-overload error against a witness
///   of the same name. Check `Clamp`, `Sign`, `Abs`, `Min`, `Max`, `CreateChecked` before
///   naming any future SRTP member.
/// * **The constraint is restated in full at every wrapper level.** Omit it and the
///   first call site monomorphises the whole chain; the errors then surface at the *other*
///   call sites, pointing at innocent code.
[<Sealed; AbstractClass>]
type Interpolate private () =

    /// Unit-generic numeric interpolation. `Interp(0.0<m>, 10.0<m>, 0.5) = 5.0<m>`.
    static member inline Interp(a: float<'u>, b: float<'u>, t: float) : float<'u> = a + (b - a) * t

    static member inline Interp(a: float32, b: float32, t: float) : float32 = a + (b - a) * float32 t

    static member inline Interp(a: Vec2<'u>, b: Vec2<'u>, t: float) : Vec2<'u> = Vec2.lerp t a b

    static member inline Interp(a: Vec3<'u>, b: Vec3<'u>, t: float) : Vec3<'u> = Vec3.lerp t a b

    /// Colour interpolation goes through **Oklab**, not sRGB. An sRGB lerp between
    /// two saturated colours passes through a desaturated grey; a perceptual one does not.
    /// Making this the default — rather than an option on a palette somewhere — is what stops
    /// the wrong thing being easy.
    static member inline Interp(a: Color, b: Color, t: float) : Color = Color.mix t a b

    static member inline Interp(a: Rect<'u>, b: Rect<'u>, t: float) : Rect<'u> =
        { X0 = a.X0 + (b.X0 - a.X0) * t
          Y0 = a.Y0 + (b.Y0 - a.Y0) * t
          X1 = a.X1 + (b.X1 - a.X1) * t
          Y1 = a.Y1 + (b.Y1 - a.Y1) * t }

    /// `Affine` joins the witness, and it is the one overload that is not componentwise.
    /// It decomposes to translate/rotate/scale/skew first, because the componentwise answer
    /// collapses a half-turn through the zero matrix. See `AffineInterp` above for the argument
    /// and for the degenerate cases.
    static member inline Interp(a: Affine<'a, 'b>, b: Affine<'a, 'b>, t: float) : Affine<'a, 'b> =
        AffineInterp.lerp t a b

/// The SRTP call sites. `betweenWith` names the witness explicitly; `between` fixes it to the
/// built-in `Interpolate` and is what everything else in the library calls.
[<RequireQualifiedAccess>]
module Interp =

    /// Generic over the witness type, so a downstream package can supply its own overloads
    /// for its own types without reopening `Interpolate`.
    let inline betweenWith< ^W, ^T when (^W or ^T): (static member Interp: ^T * ^T * float -> ^T)>
        (a: ^T)
        (b: ^T)
        (t: float)
        : ^T =
        ((^W or ^T): (static member Interp: ^T * ^T * float -> ^T) (a, b, t))

    /// The constraint below is *not* redundant with the one on `betweenWith`. Deleting it
    /// compiles, and then breaks the second call site in the program with a type error naming
    /// the first call site's type. Every public wrapper restates it.
    let inline between< ^T when (Interpolate or ^T): (static member Interp: ^T * ^T * float -> ^T)>
        (a: ^T)
        (b: ^T)
        (t: float)
        : ^T =
        betweenWith<Interpolate, ^T> a b t

    /// Interpolate along a sequence of stops given as `(position, value)` in ascending
    /// position order. Colour ramps, multi-stop gradients and keyframed properties are all
    /// this function.
    let inline stops< ^T when (Interpolate or ^T): (static member Interp: ^T * ^T * float -> ^T)>
        (xs: (float * ^T)[])
        (t: float)
        : ^T =
        if xs.Length = 0 then
            invalidArg "xs" "Marey: interpolation needs at least one stop."
        elif xs.Length = 1 || t <= fst xs.[0] then
            snd xs.[0]
        elif t >= fst xs.[xs.Length - 1] then
            snd xs.[xs.Length - 1]
        else
            // Linear scan: stop counts are small (a colour ramp has 3-11 stops) and a scan
            // beats a binary search plus branch misprediction at that size.
            let mutable i = 0

            while i < xs.Length - 2 && fst xs.[i + 1] < t do
                i <- i + 1

            let x0, v0 = xs.[i]
            let x1, v1 = xs.[i + 1]
            let span = x1 - x0
            let local = if span = 0.0 then 0.0 else (t - x0) / span
            between< ^T> v0 v1 local
