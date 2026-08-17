namespace Quiver

/// §3 — Unit-indexed 2-D vector.
///
/// `Vec2<m>` and `Vec2<px>` are distinct types, so "a layout bug where you passed a normalized
/// coordinate to a routine expecting pixels stops being a runtime mystery and becomes a red
/// squiggle."
///
/// `[<Struct>]` is not a micro-optimisation here. §1.3: "Per-frame allocation of records will
/// destroy a 60 fps loop. The hot path (scene -> backend, particle stepping) must be
/// struct/array-based." Reference-type vectors in a particle loop are the difference between
/// 60 fps and 6.
///
/// **Operators are restricted to measure-*preserving* operations on purpose.** §3 sketches
///
/// ```fsharp
/// static member ( * ) (k: float<'k>, v: Vec2<'u>) : Vec2<'k * 'u>
/// ```
///
/// but that is the same shape §12.1 proves over-constrains: measure algebra in an operator
/// overload emits FS0064 and silently pins the second measure variable after its first use.
/// Unit-*changing* operations therefore live in the `Vec2` module as functions with explicit
/// `[<Measure>]` type parameters, exactly as `U.mul`/`U.div` do.
[<Struct>]
type Vec2<[<Measure>] 'u> =
    { X: float<'u>
      Y: float<'u> }

    static member (+)(a: Vec2<'u>, b: Vec2<'u>) = { X = a.X + b.X; Y = a.Y + b.Y }
    static member (-)(a: Vec2<'u>, b: Vec2<'u>) = { X = a.X - b.X; Y = a.Y - b.Y }
    static member (~-)(a: Vec2<'u>) = { X = -a.X; Y = -a.Y }
    static member (*)(k: float, a: Vec2<'u>) = { X = k * a.X; Y = k * a.Y }
    static member (*)(a: Vec2<'u>, k: float) = { X = a.X * k; Y = a.Y * k }
    static member (/)(a: Vec2<'u>, k: float) = { X = a.X / k; Y = a.Y / k }

/// §3 — Unit-indexed 3-D vector. Present in Core from the start because §5.4 requires the IR
/// to leave a door open for `Project3D of Camera * Node3`; absent from the Scene IR entirely,
/// because that door stays shut until M4.
[<Struct>]
type Vec3<[<Measure>] 'u> =
    { X: float<'u>
      Y: float<'u>
      Z: float<'u> }

    static member (+)(a: Vec3<'u>, b: Vec3<'u>) =
        { X = a.X + b.X
          Y = a.Y + b.Y
          Z = a.Z + b.Z }

    static member (-)(a: Vec3<'u>, b: Vec3<'u>) =
        { X = a.X - b.X
          Y = a.Y - b.Y
          Z = a.Z - b.Z }

    static member (~-)(a: Vec3<'u>) = { X = -a.X; Y = -a.Y; Z = -a.Z }

    static member (*)(k: float, a: Vec3<'u>) =
        { X = k * a.X
          Y = k * a.Y
          Z = k * a.Z }

    static member (*)(a: Vec3<'u>, k: float) =
        { X = a.X * k
          Y = a.Y * k
          Z = a.Z * k }

    static member (/)(a: Vec3<'u>, k: float) =
        { X = a.X / k
          Y = a.Y / k
          Z = a.Z / k }

/// An axis-aligned box. Layout (§9) is a measure–arrange pass over these, and the panel /
/// facet / colourbar grid is nothing but rectangle subdivision.
[<Struct>]
type Rect<[<Measure>] 'u> =
    { X0: float<'u>
      Y0: float<'u>
      X1: float<'u>
      Y1: float<'u> }

/// Output surface size. `IBackend.Begin` takes an `Extent<px>` (§5).
[<Struct>]
type Extent<[<Measure>] 'u> = { Width: float<'u>; Height: float<'u> }

[<RequireQualifiedAccess>]
module Vec2 =

    let inline create (x: float<'u>) (y: float<'u>) = { X = x; Y = y }

    let zero<[<Measure>] 'u> : Vec2<'u> =
        { X = LanguagePrimitives.FloatWithMeasure<'u> 0.0
          Y = LanguagePrimitives.FloatWithMeasure<'u> 0.0 }

    let inline map (f: float<'u> -> float<'v>) (a: Vec2<'u>) : Vec2<'v> = { X = f a.X; Y = f a.Y }

    /// Scale by a *dimensioned* quantity, changing the unit. This is the payoff, and the
    /// reason it is a function and not an operator (§12.1):
    ///
    /// ```fsharp
    /// scaleBy dt (field p)   // float<s> * Vec2<m/s> -> Vec2<m>
    /// ```
    let scaleBy<[<Measure>] 'k, [<Measure>] 'u> (k: float<'k>) (a: Vec2<'u>) : Vec2<'k * 'u> =
        { X = k * a.X; Y = k * a.Y }

    /// Divide by a dimensioned quantity. `divBy dt displacement : Vec2<m/s>`.
    let divBy<[<Measure>] 'k, [<Measure>] 'u> (k: float<'k>) (a: Vec2<'u>) : Vec2<'u / 'k> =
        { X = a.X / k; Y = a.Y / k }

    let dot<[<Measure>] 'a, [<Measure>] 'b> (u: Vec2<'a>) (v: Vec2<'b>) : float<'a * 'b> = u.X * v.X + u.Y * v.Y

    /// 2-D cross product — the signed area of the parallelogram. Winding tests, convex hulls,
    /// and the sign of a curl all come from this.
    let cross<[<Measure>] 'a, [<Measure>] 'b> (u: Vec2<'a>) (v: Vec2<'b>) : float<'a * 'b> = u.X * v.Y - u.Y * v.X

    /// Squared length, in `'u^2`. Prefer this in comparisons: no square root, and the type
    /// still tells you what you have.
    let normSq (a: Vec2<'u>) : float<'u^2> = a.X * a.X + a.Y * a.Y

    /// Length. F#'s `sqrt` is measure-aware — `float<'u^2> -> float<'u>` — which is exactly
    /// the arithmetic you want and one of the small pleasures of the host language.
    let norm (a: Vec2<'u>) : float<'u> = sqrt (normSq a)

    /// Unit vector. Dimensionless by construction: a direction is not a length.
    let normalize (a: Vec2<'u>) : Vec2<1> =
        let n = norm a

        if n = LanguagePrimitives.FloatWithMeasure<'u> 0.0 then
            { X = 0.0; Y = 0.0 }
        else
            { X = a.X / n; Y = a.Y / n }

    /// Rotate by an angle. Measure-preserving, so it is safe as an operator-free helper.
    let rotate (theta: float<rad>) (a: Vec2<'u>) : Vec2<'u> =
        let t = theta / 1.0<rad>
        let c = cos t
        let s = sin t
        { X = c * a.X - s * a.Y; Y = s * a.X + c * a.Y }

    /// Left-hand perpendicular. Line joins, arrow heads, and tick direction all want it.
    let perp (a: Vec2<'u>) : Vec2<'u> = { X = -a.Y; Y = a.X }

    let angle (a: Vec2<'u>) : float<rad> = atan2 (float a.Y) (float a.X) * 1.0<rad>

    /// Componentwise linear interpolation. `Interp` for `Vec2` delegates here (§7.1).
    let lerp (t: float) (a: Vec2<'u>) (b: Vec2<'u>) : Vec2<'u> =
        { X = a.X + (b.X - a.X) * t
          Y = a.Y + (b.Y - a.Y) * t }

    /// Reattach a measure to a raw pair. The escape hatch backends need, and the only
    /// sanctioned way back across the erasure boundary (§2.3).
    let ofRaw<[<Measure>] 'u> (x: float, y: float) : Vec2<'u> =
        { X = LanguagePrimitives.FloatWithMeasure<'u> x
          Y = LanguagePrimitives.FloatWithMeasure<'u> y }

    let toRaw (a: Vec2<'u>) = float a.X, float a.Y

[<RequireQualifiedAccess>]
module Vec3 =

    let inline create (x: float<'u>) (y: float<'u>) (z: float<'u>) = { X = x; Y = y; Z = z }

    let dot<[<Measure>] 'a, [<Measure>] 'b> (u: Vec3<'a>) (v: Vec3<'b>) : float<'a * 'b> =
        u.X * v.X + u.Y * v.Y + u.Z * v.Z

    let cross<[<Measure>] 'a, [<Measure>] 'b> (u: Vec3<'a>) (v: Vec3<'b>) : Vec3<'a * 'b> =
        { X = u.Y * v.Z - u.Z * v.Y
          Y = u.Z * v.X - u.X * v.Z
          Z = u.X * v.Y - u.Y * v.X }

    let normSq (a: Vec3<'u>) : float<'u^2> = a.X * a.X + a.Y * a.Y + a.Z * a.Z

    let norm (a: Vec3<'u>) : float<'u> = sqrt (normSq a)

    let normalize (a: Vec3<'u>) : Vec3<1> =
        let n = norm a

        if n = LanguagePrimitives.FloatWithMeasure<'u> 0.0 then
            { X = 0.0; Y = 0.0; Z = 0.0 }
        else
            { X = a.X / n; Y = a.Y / n; Z = a.Z / n }

    let lerp (t: float) (a: Vec3<'u>) (b: Vec3<'u>) : Vec3<'u> =
        { X = a.X + (b.X - a.X) * t
          Y = a.Y + (b.Y - a.Y) * t
          Z = a.Z + (b.Z - a.Z) * t }

[<RequireQualifiedAccess>]
module Rect =

    let inline create (x0: float<'u>) (y0: float<'u>) (x1: float<'u>) (y1: float<'u>) =
        { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1 }

    let width (r: Rect<'u>) = r.X1 - r.X0
    let height (r: Rect<'u>) = r.Y1 - r.Y0
    let area (r: Rect<'u>) : float<'u^2> = width r * height r

    let centre (r: Rect<'u>) : Vec2<'u> =
        { X = (r.X0 + r.X1) * 0.5
          Y = (r.Y0 + r.Y1) * 0.5 }

    let contains (p: Vec2<'u>) (r: Rect<'u>) =
        p.X >= r.X0 && p.X <= r.X1 && p.Y >= r.Y0 && p.Y <= r.Y1

    let intersects (a: Rect<'u>) (b: Rect<'u>) =
        a.X0 < b.X1 && b.X0 < a.X1 && a.Y0 < b.Y1 && b.Y0 < a.Y1

    /// Shrink on all sides. Negative insets grow. Padding, panel margins, and the gap between
    /// facets are all this function.
    let inset (d: float<'u>) (r: Rect<'u>) =
        { X0 = r.X0 + d
          Y0 = r.Y0 + d
          X1 = r.X1 - d
          Y1 = r.Y1 - d }

    let insetBy (left: float<'u>) (top: float<'u>) (right: float<'u>) (bottom: float<'u>) (r: Rect<'u>) =
        { X0 = r.X0 + left
          Y0 = r.Y0 + top
          X1 = r.X1 - right
          Y1 = r.Y1 - bottom }

    /// The full normalized panel, [0,1]^2. The root of every layout.
    let unit: Rect<nd> =
        { X0 = 0.0<nd>
          Y0 = 0.0<nd>
          X1 = 1.0<nd>
          Y1 = 1.0<nd> }
