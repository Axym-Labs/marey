/// Coordinate-space measures.
///
/// "Give every coordinate space its own measure. Then mixing them is a compile error."
///
/// These live in Marey.Core rather than Marey.Units because they are *graphics* spaces,
/// not physical quantities: they have no SI dimension and no conversion to SI. A `float<px>`
/// is not a length in the dimensional-analysis sense — it is a device coordinate. Keeping
/// them out of `Dim` is deliberate: it stops anyone writing a `Scale<px>` with a dimension
/// vector and pretending pixels are metres.
namespace Marey

/// Device pixels. The unit of the output surface.
[<Measure>]
type px

/// Normalized panel space, nominally [0,1]x[0,1]. The IR is expressed *entirely* in `nd`
///: the Scene never knows the output size, which is what makes one Scene render to a
/// 320x200 SVG and a 4K PNG without recompilation.
[<Measure>]
type nd

/// Typographic points (1/72 inch). Font sizes.
[<Measure>]
type pt

/// Millimetres of physical output. Print sizing, and the bridge to `pt` (1 pt = 25.4/72 mm).
[<Measure>]
type mm

/// Radians. Rotations and polar angles.
///
/// Angle is dimensionless in SI (m/m), so this is a *tag*, not a dimension — which is why it
/// lives here and not in Marey.Units. Giving it a measure anyway is worth it: it stops the
/// classic degrees/radians bug at compile time.
[<Measure>]
type rad

/// Degrees. `U.degree` is its witness, π/180 rad, for an axis in degrees.
[<Measure>]
type deg

/// Physical-space conversions between the graphics measures. All exact by definition.
module Space =

    /// 1 inch = 72 pt = 25.4 mm.
    [<Literal>]
    let private MmPerInch = 25.4

    [<Literal>]
    let private PtPerInch = 72.0

    let mmPerPt: float<mm / pt> = MmPerInch / PtPerInch * 1.0<mm / pt>

    let ptPerMm: float<pt / mm> = PtPerInch / MmPerInch * 1.0<pt / mm>

    let inline mmOfPt (x: float<pt>) : float<mm> = x * mmPerPt

    let inline ptOfMm (x: float<mm>) : float<pt> = x * ptPerMm

    /// Points to device pixels at a given output resolution. `dpi` carries its own measure so
    /// you cannot accidentally pass a scale factor where a resolution was wanted.
    let inline pxOfPt (dpi: float<px / mm>) (x: float<pt>) : float<px> = mmOfPt x * dpi

    /// Device pixels to points — the inverse of `pxOfPt`.
    ///
    /// A backend whose output is measured in physical units rather than device ones needs this
    /// direction: a TeX picture is laid out in `bp` (1/72 inch, which is exactly this `pt`), so
    /// an `Extent<px>` has to become an extent in points before any geometry is emitted. At the
    /// 96 px/inch screen convention the factor is 72/96 = 0.75.
    let inline ptOfPx (dpi: float<px / mm>) (x: float<px>) : float<pt> = ptOfMm (x / dpi)

    /// The screen convention: 96 px per inch.
    let screenDpi: float<px / mm> = 96.0 / MmPerInch * 1.0<px / mm>

    let radPerDeg: float<rad / deg> = System.Math.PI / 180.0 * 1.0<rad / deg>

    let inline radOfDeg (a: float<deg>) : float<rad> = a * radPerDeg

    let inline degOfRad (a: float<rad>) : float<deg> = a / radPerDeg

    /// Strip the `nd` tag. Used only at the erasure boundary and inside backends.
    let inline ndValue (x: float<nd>) : float = x / 1.0<nd>

    /// Attach the `nd` tag to a raw fraction.
    let inline nd (x: float) : float<nd> = x * 1.0<nd>
