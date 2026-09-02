/// Custom FsCheck generators, and the tolerance policy for the whole suite.
///
/// **This file is the load-bearing part of the property suite.** FsCheck's default `float`
/// generator emits NaN, infinity and subnormals, all three of which are outside the domain of
/// every law in this library: a scale cannot round-trip NaN, `prune` cannot be idempotent on a
/// tree containing NaN (structural equality says `nan <> nan`), and an infinite domain gives a
/// zero-span projection. Handing that generator to `Scale.log10` produces a wall of failures
/// that says nothing about the code.
///
/// Every generator below therefore produces values that are *in the domain of the property
/// being tested*, and every restriction is annotated with the reason and — where it is a
/// numerical-conditioning restriction — the measurement that set it.
///
/// ### Tolerances
///
/// Tolerances in this suite come from measurement, not from hope. Each constant records the
/// worst error actually observed over a large calibration run against the frozen `Marey.Core`
/// build, and the headroom it leaves. Two of them are set by documented findings rather than by
/// measurement:
///
/// * `Tol.colour = 1e-5` — **decisions.md F1**: the sRGB → Oklab → sRGB round trip has an
///   irreducible floor of ~1e-6 (measured here at 1.65e-6 over random colours including alpha),
///   because Ottosson's published matrices are only inverse to ~2.4e-10 and `ofLinear`'s curve
///   has a slope of ~1500 near black. "A colour test demanding better than 1e-5 will flap
///   forever."
/// * `Tol.panel = 1e-9` — a billionth of the normalized panel, which is a billionth of a pixel
///   on a gigapixel surface. Below any conceivable rendering resolution.
module Marey.Tests.Generators

open FsCheck
open FsCheck.FSharp
open Marey

// ======================================================================================
// TOLERANCES
// ======================================================================================

[<RequireQualifiedAccess>]
module Tol =

    /// Absolute tolerance in normalized panel space (`nd`).
    /// Calibration: worst observed `|forward (inverse t) - t|` was 2.7e-13 across all five
    /// scale kinds over 100k+ random domains. Headroom ~3700x.
    [<Literal>]
    let panel = 1e-9

    /// Relative tolerance for `inverse (forward x) = x` on the well-conditioned scale kinds.
    /// Calibration: worst observed relative error 9.0e-14 (reciprocal over three decades);
    /// linear 4.4e-16, log10/log2/ln 1.2e-14. Headroom ~11000x.
    [<Literal>]
    let scaleRelative = 1e-9

    /// Tolerance for `inverse (forward x) = x` expressed as a fraction of the domain span,
    /// used for the two kinds whose inverse has an unbounded derivative at the origin.
    ///
    /// `Scale.power p` inverts through `y ** (1/p)`, whose derivative diverges at `y = 0` for
    /// `p > 1`; `Scale.symlog` reconstructs a value near zero from a projection anchored at
    /// `fwd lo`, which for a domain far from the origin costs most of the mantissa. Neither is
    /// a defect — both are the conditioning of the map itself — and neither is visible, because
    /// the error is a fraction of the *axis*, which is what a reader sees.
    ///
    /// Calibration: worst observed 1.08e-12 (symlog, x = 2.7e-9 on a domain spanning 904),
    /// 4.79e-13 (signed power, p in [0.5,2]). Headroom ~900x.
    [<Literal>]
    let scaleSpan = 1e-9

    /// Relative tolerance for ordinary float arithmetic laws (interpolation endpoints, affine
    /// composition). Calibration: worst observed 2.2e-16 for `interp`, 4.8e-14 for
    /// `apply (compose f g)`, 1.1e-13 for the affine inverse round trip. Headroom ~1000x.
    [<Literal>]
    let arith = 1e-10

    /// Colour tolerance. **decisions.md F1** — never tighten this below 1e-5.
    [<Literal>]
    let colour = 1e-5

/// Approximate comparison helpers. Kept out of the tests themselves so that a tolerance is
/// never chosen at a call site by whoever was making a test go green.
[<RequireQualifiedAccess>]
module Approx =

    /// Equal to within `tol` *relative to the larger magnitude*, with an absolute floor so that
    /// comparisons against exact zero behave.
    let rel (tol: float) (a: float) (b: float) =
        let d = abs (a - b)
        d <= tol * max 1.0 (max (abs a) (abs b))

    /// Equal to within an absolute tolerance.
    let abs' (tol: float) (a: float) (b: float) = abs (a - b) <= tol

    /// Equal to within `tol * scale`, where `scale` is the natural size of the problem (an axis
    /// span, a figure width). The honest statement when a relative comparison is not achievable.
    let scaled (tol: float) (scale: float) (a: float) (b: float) = abs (a - b) <= tol * abs scale

    let colour (a: Color) (b: Color) =
        abs' Tol.colour a.R b.R
        && abs' Tol.colour a.G b.G
        && abs' Tol.colour a.B b.B
        && abs' Tol.colour a.A b.A

    let vec2 (tol: float) (a: Vec2<'u>) (b: Vec2<'u>) =
        rel tol (float a.X) (float b.X) && rel tol (float a.Y) (float b.Y)

    let vec3 (tol: float) (a: Vec3<'u>) (b: Vec3<'u>) =
        rel tol (float a.X) (float b.X)
        && rel tol (float a.Y) (float b.Y)
        && rel tol (float a.Z) (float b.Z)

    let rect (tol: float) (a: Rect<'u>) (b: Rect<'u>) =
        rel tol (float a.X0) (float b.X0)
        && rel tol (float a.Y0) (float b.Y0)
        && rel tol (float a.X1) (float b.X1)
        && rel tol (float a.Y1) (float b.Y1)

// ======================================================================================
// THE GENERATED TYPES
//
// Every generated value is wrapped in its own single-case type. That is deliberate: it lets a
// property say *in its signature* which class of input it is quantified over, and it stops one
// suite's `float` generator silently becoming another suite's.
// ======================================================================================

/// A finite, non-subnormal double of moderate magnitude. Never NaN, never infinity.
type FiniteFloat = FiniteFloat of float

/// A finite double strictly greater than zero.
type PositiveFloat = PositiveFloat of float

/// A value in `[0, 1]`, including both endpoints — interpolation and projection parameters.
type UnitT = UnitT of float

/// A `Dim` with small exponents. Exponents beyond about +/-4 do not occur in physics and would
/// only test `int` overflow.
type SmallDim = SmallDim of Dim

/// An ascending linear domain of bounded *dynamic range*: `|lo| / span <= 1000`.
///
/// The bound is a conditioning requirement, not a convenience. `Scale`'s projection is
/// `(x - a) / span` and its inverse is `a + span * t`; the reconstruction loses
/// `log10(|a|/span)` significant digits, so a domain like `1e9 .. 1e9 + 1e-6` cannot round-trip
/// through `[0,1]` in double precision at *any* tolerance. Three decades of offset costs three
/// digits and leaves thirteen.
type LinearDomain = LinearDomain of lo: float * hi: float

/// A strictly positive ascending domain spanning between 0.1 and 12 decades — what `log10`,
/// `log2` and `ln` require (`Scale.logBase` has no defence of its own; `Ticks.log10` returns an
/// empty array for a non-positive domain).
type LogDomain = LogDomain of lo: float * hi: float

/// A domain that does not contain or touch zero, spanning at most three decades — the
/// precondition `Scale.reciprocal` enforces with `invalidArg`, plus a conditioning bound.
///
/// Both signs are generated. `1/x` over `[-100, -1]` is a legitimate descending-in-`x`,
/// ascending-in-projection axis and it is the case a naive implementation gets wrong.
///
/// Three decades: `1/x` maps `[1e-12, 1e12]` onto `[1e-12, 1e12]` reversed, so the projection
/// of the far endpoint is `(1e-12 - 1e12) / (1e-12 - 1e12)`, which is exact cancellation of
/// twenty-four decades. Measured relative round-trip error at twelve decades: 8.8e-5.
type ReciprocalDomain = ReciprocalDomain of lo: float * hi: float

/// A scale, described as data rather than as a closure, so that a failing property prints
/// something a human can paste into a REPL. `ScaleSpec.build` is the only place these become
/// `Scale<1>` values.
///
/// Bases are drawn from {10, 2, e} — the three `Scale` exposes by name. `Scale.logBase` rejects
/// a base of 1 or below; `Scale.symlog` does **not** (see `ScaleDefects` in ScaleProperties.fs),
/// so the generator must not produce one.
type ScaleSpec =
    | SLinear of lo: float * hi: float
    | SLog of logBase: float * lo: float * hi: float
    | SSymlog of logBase: float * linthresh: float * lo: float * hi: float
    /// Positive domain only — see `SignedPower` for the sign-preserving case.
    | SPower of exponent: float * lo: float * hi: float
    | SReciprocal of lo: float * hi: float

/// The subset of `ScaleSpec` whose `inverse (forward x) = x` law holds to a *relative*
/// tolerance everywhere on the domain. `SSymlog` and signed `SPower` are excluded and get the
/// span-relative statement instead; see `Tol.scaleSpan`.
type WellConditionedScale = WellConditionedScale of ScaleSpec

/// A `Scale.power` over a domain that may straddle zero, exercising `signPow`'s
/// sign-preservation. Exponent restricted to `[0.5, 2]`.
type SignedPower = SignedPower of exponent: float * lo: float * hi: float

/// A tick target in `[4, 24]`.
///
/// **Four, not one.** §4 states "`TickHint n` returns between `n/2` and `2n` ticks". That is
/// false for `n = 1` and for `n = 3` against the shipped 1-2-5 ladder — see
/// `TickProperties.MeasuredExceptions`, which locks both counterexamples in as regression
/// tests. It holds for `n = 2` and for every `n >= 4`.
type TickTarget = TickTarget of int

/// Six raw matrix entries. Measures are attached by the test, not by the generator, so one
/// generator serves `Affine<nd,nd>`, `Affine<nd,px>` and `Affine<px,mm>` and the *test* is what
/// states which coordinate spaces are involved.
type Mat = Mat of m11: float * m12: float * m21: float * m22: float * dx: float * dy: float

/// A `Mat` whose linear part is comfortably far from singular: `|det| >= 1e-3 * norm^2`.
/// `Affine.tryInvert` only refuses below 1e-300, so everything between is invertible but
/// arbitrarily ill-conditioned, and an inverse round trip through a matrix with a condition
/// number of 1e14 has no digits left.
type InvertibleMat = InvertibleMat of Mat

/// An sRGB colour with every component in `[0,1]`, alpha included. Out-of-gamut input is
/// excluded because `Color.ofOklab` clamps, which would make the round-trip law false for a
/// reason that has nothing to do with Oklab.
type Colour = Colour of Color

type Vec2Nd = Vec2Nd of Vec2<nd>
type Vec3Nd = Vec3Nd of Vec3<nd>

/// A rectangle in panel space, always with `X0 <= X1` and `Y0 <= Y1`.
type RectNd = RectNd of Rect<nd>

/// A rotation angle in `(-2pi, 2pi)`.
type Rotation = Rotation of float<rad>

/// A scene tree, bounded in depth and breadth, drawing from all ten `Node` cases.
type SceneTree = SceneTree of Node

/// A duration in seconds: an exact multiple of 1/8 s in `[0, 8]`.
///
/// **The eighths are not decoration.** `andThen` adds durations and `stretch` multiplies them,
/// and the laws in `AnimProperties` compare `(d1 + d2) + d3` against `d1 + (d2 + d3)`, which is
/// not an identity over the doubles. Restricting the generator to binary fractions takes
/// IEEE754's non-associativity out of a law that is about the algebra — the same move every
/// other generator in this file makes for its own domain. Zero is included, and often: `pure'`
/// has duration zero and it is the whole identity case.
type AnimSeconds = AnimSeconds of float

/// A stretch factor: a power of two between 1/16 and 16. Powers of two for the same reason the
/// durations are eighths, and because `stretch` divides by the factor as well as multiplying.
type StretchFactor = StretchFactor of float

/// A seek time in seconds, reaching well past both ends of any duration this file generates, so
/// that the clamp at each boundary is exercised rather than assumed. Also an exact multiple of
/// 1/8, so a seek can land precisely on a seam.
type Seek = Seek of float

/// A generated animation: a duration, and a seed that separates one animation from another so a
/// law comparing two compositions is comparing something.
type AnimSpec = AnimSpec of seconds: float * seed: float

/// A **well-formed** path for the morph laws: zero to three subpaths, each opened by a `MoveTo`,
/// each carrying up to four line or cubic segments, each independently closed.
///
/// Deliberately not `pathSeg`'s free sequence. `Morph` is total on a malformed path — a leading
/// `LineTo`, a stray `ClosePath` — and `MorphProperties` says so in named facts, because that
/// totality is a decision (Skia's implicit `moveTo(0,0)`) rather than an accident. But a law
/// about an arc-length correspondence between two shapes is not a law about a path that starts
/// by closing nothing, and quantifying it over one would be testing the recovery rather than
/// the correspondence.
///
/// Zero subpaths is generated, and often: the empty path is the padding rule's whole edge case.
type MorphPath = MorphPath of PathSeg[]

/// The *free* sequence: `pathSeg` drawn without structure, so a leading `LineTo`, a `ClosePath`
/// with nothing open and a run of bare `MoveTo`s all occur. Only one law is quantified over it —
/// that `Morph` is total — and that is the point of having it.
type RawPath = RawPath of PathSeg[]

// ======================================================================================
// THE GENERATORS
// ======================================================================================

[<RequireQualifiedAccess>]
module Gens =

    /// Mantissa/decade construction rather than `Gen.Choose` over a bit pattern: it gives an
    /// even spread over *magnitudes*, which is what numerical code is sensitive to, instead of
    /// an even spread over the reals, which puts 99.9% of the samples in one decade.
    let private scaledFloat (minDecade: int) (maxDecade: int) : Gen<float> =
        gen {
            let! mantissa = Gen.choose (1, 999999)
            let! decade = Gen.choose (minDecade, maxDecade)
            return float mantissa / 1e6 * (10.0 ** float decade)
        }

    let private signG = Gen.elements [ -1.0; 1.0 ]

    /// Finite doubles spanning twelve decades, salted with the exact values that break naive
    /// arithmetic (zero, one, a half) because a purely random generator never produces them.
    let finiteFloat: Gen<float> =
        Gen.frequency
            [ 8, gen {
                     let! s = signG
                     let! m = scaledFloat -6 6
                     return s * m
                 }
              1, Gen.elements [ 0.0; 1.0; -1.0; 0.5; -0.5; 2.0; -2.0; 10.0; -10.0 ] ]

    let positiveFloat: Gen<float> = scaledFloat -6 6

    /// `[0,1]` with both endpoints represented exactly and often — `interp a b 0 = a` and
    /// `interp a b 1 = b` are endpoint laws, so the endpoints must actually be generated.
    let unitT: Gen<float> =
        Gen.frequency
            [ 3, Gen.elements [ 0.0; 1.0; 0.5 ]
              7, Gen.choose (0, 1000000) |> Gen.map (fun i -> float i / 1e6) ]

    let smallDim: Gen<Dim> =
        let e = Gen.choose (-4, 4)

        gen {
            let! l = e
            let! ma = e
            let! t = e
            let! i = e
            let! th = e
            let! n = e
            let! j = e

            return
                { L = l
                  M = ma
                  T = t
                  I = i
                  Th = th
                  N = n
                  J = j }
        }

    /// See `LinearDomain`: magnitude `M`, offset within `+/-M`, span within `[M/1000, 2M]`.
    let linearDomain: Gen<float * float> =
        gen {
            let! m = scaledFloat -6 6
            let! offsetFrac = Gen.choose (-1000, 1000) |> Gen.map (fun i -> float i / 1000.0)
            let! spanFrac = Gen.choose (1, 2000) |> Gen.map (fun i -> float i / 1000.0)
            let lo = offsetFrac * m
            let span = m * max 1e-3 spanFrac
            return lo, lo + span
        }

    let logDomain: Gen<float * float> =
        gen {
            let! lo = scaledFloat -12 12
            let! decades = Gen.choose (1, 120) |> Gen.map (fun i -> float i / 10.0)
            return lo, lo * (10.0 ** decades)
        }

    let reciprocalDomain: Gen<float * float> =
        gen {
            let! lo = scaledFloat -12 12
            let! decades = Gen.choose (1, 30) |> Gen.map (fun i -> float i / 10.0)
            let! s = signG
            let hi = lo * (10.0 ** decades)
            // Negating swaps which endpoint is smaller, and the domain must stay ascending.
            return if s > 0.0 then lo, hi else -hi, -lo
        }

    /// A positive domain spanning at most three decades.
    ///
    /// Used for `Scale.power`, which generates its ticks with `Ticks.linear`. `Ticks.linear`
    /// computes its first tick as `ceil(lo/step - 1e-9) * step`, and that `1e-9` is absolute in
    /// units of `step`, so once `lo` is more than nine orders of magnitude below the step the
    /// subtraction pushes `ceil` down past the domain and the first tick lands *outside* it —
    /// `Ticks.linear 10 (1.0, 1e10)` yields a tick at 0. That is a real defect, pinned in
    /// `TickProperties.MeasuredExceptions`; three decades keeps the generator on the correct
    /// side of it so the containment property can still be asserted exactly everywhere else.
    let narrowPositiveDomain: Gen<float * float> =
        gen {
            let! lo = scaledFloat -12 12
            let! decades = Gen.choose (1, 30) |> Gen.map (fun i -> float i / 10.0)
            return lo, lo * (10.0 ** decades)
        }

    let logBase: Gen<float> = Gen.elements [ 10.0; 2.0; System.Math.E ]

    /// `[0.5, 2]`. `Scale.power` rejects 0 and is defined for any other real, but an exponent
    /// far from 1 is a scale nobody plots and it only measures how fast `**` loses digits.
    let powerExponent: Gen<float> =
        Gen.choose (500, 2000) |> Gen.map (fun i -> float i / 1000.0)

    let wellConditionedScale: Gen<ScaleSpec> =
        Gen.frequency
            [ 3, linearDomain |> Gen.map SLinear
              3,
              gen {
                  let! b = logBase
                  let! (lo, hi) = logDomain
                  return SLog(b, lo, hi)
              }
              2, reciprocalDomain |> Gen.map SReciprocal
              2,
              gen {
                  // Positive domain: the relative statement of the round-trip law is only
                  // achievable away from the origin. See `SignedPower` for the other half.
                  let! p = powerExponent
                  let! (lo, hi) = narrowPositiveDomain
                  return SPower(p, lo, hi)
              } ]

    let symlogScale: Gen<ScaleSpec> =
        gen {
            let! b = logBase
            let! (lo, hi) = linearDomain
            // The linear threshold has to sit inside the domain to exercise both branches.
            let! frac = Gen.choose (1, 500) |> Gen.map (fun i -> float i / 1000.0)
            return SSymlog(b, (hi - lo) * frac, lo, hi)
        }

    /// Every kind, including the two that only satisfy the span-relative form of the round-trip
    /// law. Used by the properties that hold for all five: monotonicity, endpoint mapping,
    /// `forward (inverse t) = t`, and tick containment.
    let anyScale: Gen<ScaleSpec> =
        Gen.frequency [ 4, wellConditionedScale; 1, symlogScale ]

    let signedPower: Gen<float * float * float> =
        gen {
            let! p = powerExponent
            let! (lo, hi) = linearDomain
            return p, lo, hi
        }

    let mat: Gen<Mat> =
        gen {
            let! a = finiteFloat
            let! b = finiteFloat
            let! c = finiteFloat
            let! d = finiteFloat
            let! e = finiteFloat
            let! f = finiteFloat
            return Mat(a, b, c, d, e, f)
        }

    /// Entries in `[-100, 100]` with a determinant guard. A generator that filtered the general
    /// `mat` would reject almost everything, so the linear part is built directly.
    let invertibleMat: Gen<Mat> =
        let entry =
            Gen.choose (-100000, 100000) |> Gen.map (fun i -> float i / 1000.0)

        gen {
            let! a = entry
            let! b = entry
            let! c = entry
            let! d = entry
            let! dx = finiteFloat
            let! dy = finiteFloat
            return Mat(a, b, c, d, dx, dy)
        }
        |> Gen.where (fun (Mat(a, b, c, d, _, _)) ->
            let det = a * d - b * c
            let norm = max (max (abs a) (abs b)) (max (abs c) (abs d))
            norm > 0.0 && abs det >= 1e-3 * norm * norm)

    let component01: Gen<float> =
        Gen.choose (0, 100000) |> Gen.map (fun i -> float i / 100000.0)

    let colour: Gen<Color> =
        gen {
            let! r = component01
            let! g = component01
            let! b = component01
            let! a = component01
            return Color.rgba r g b a
        }

    /// Panel-space coordinates. Kept near `[0,1]` because that is what `nd` means, with enough
    /// overshoot to cover the out-of-domain points `Scale.Forward` deliberately does not clamp.
    let ndCoord: Gen<float> =
        Gen.choose (-2000, 3000) |> Gen.map (fun i -> float i / 1000.0)

    let vec2Nd: Gen<Vec2<nd>> =
        gen {
            let! x = ndCoord
            let! y = ndCoord
            return { X = x * 1.0<nd>; Y = y * 1.0<nd> }
        }

    let vec3Nd: Gen<Vec3<nd>> =
        gen {
            let! x = ndCoord
            let! y = ndCoord
            let! z = ndCoord

            return
                { X = x * 1.0<nd>
                  Y = y * 1.0<nd>
                  Z = z * 1.0<nd> }
        }

    let rectNd: Gen<Rect<nd>> =
        gen {
            let! a = ndCoord
            let! b = ndCoord
            let! c = ndCoord
            let! d = ndCoord

            return
                { X0 = min a b * 1.0<nd>
                  Y0 = min c d * 1.0<nd>
                  X1 = max a b * 1.0<nd>
                  Y1 = max c d * 1.0<nd> }
        }

    let rotation: Gen<float<rad>> =
        Gen.choose (-6283, 6283)
        |> Gen.map (fun i -> float i / 1000.0 * 1.0<rad>)

    // ---- scene ---------------------------------------------------------------------

    let private pathSeg: Gen<PathSeg> =
        Gen.frequency
            [ 2, vec2Nd |> Gen.map MoveTo
              5, vec2Nd |> Gen.map LineTo
              2,
              gen {
                  let! a = vec2Nd
                  let! b = vec2Nd
                  let! c = vec2Nd
                  return CubicTo(a, b, c)
              }
              1, Gen.constant ClosePath ]

    let private gradientStops: Gen<GradientStop[]> =
        gen {
            let! n = Gen.choose (2, 4)
            let! cs = Gen.arrayOfLength n colour

            return cs |> Array.mapi (fun i c -> { Offset = float i / float (n - 1); Color = c })
        }

    let private paint: Gen<Paint> =
        Gen.frequency
            [ 6, colour |> Gen.map Solid
              2,
              gen {
                  let! a = vec2Nd
                  let! b = vec2Nd
                  let! s = gradientStops
                  return LinearGradient(a, b, s)
              }
              2,
              gen {
                  let! c = vec2Nd
                  let! r = Gen.choose (1, 2000) |> Gen.map (fun i -> float i / 1000.0 * 1.0<nd>)
                  let! s = gradientStops
                  return RadialGradient(c, r, s)
              } ]

    let private stroke: Gen<Stroke> =
        gen {
            let! p = paint
            let! w = Gen.choose (1, 10000) |> Gen.map (fun i -> float i / 1000.0 * 1.0<px>)
            let! nDash = Gen.choose (0, 4)
            let! dashes = Gen.arrayOfLength nDash (Gen.choose (1, 20000) |> Gen.map (fun i -> float i / 1000.0 * 1.0<px>))
            let! offset = Gen.choose (0, 10000) |> Gen.map (fun i -> float i / 1000.0 * 1.0<px>)
            let! cap = Gen.elements [ Butt; Round; Square ]
            let! join = Gen.elements [ Miter; RoundJoin; Bevel ]
            let! miter = Gen.choose (1000, 20000) |> Gen.map (fun i -> float i / 1000.0)

            return
                { Paint = p
                  Width = w
                  Dash = dashes
                  DashOffset = offset
                  Cap = cap
                  Join = join
                  MiterLimit = miter }
        }

    let private fontSpec: Gen<FontSpec> =
        gen {
            let! fam = Gen.elements [ "Helvetica"; "Times"; "Consolas"; "" ]
            let! size = Gen.choose (1000, 72000) |> Gen.map (fun i -> float i / 1000.0 * 1.0<pt>)
            let! weight = Gen.elements [ 100; 400; 700; 900 ]
            let! italic = Gen.elements [ true; false ]

            return
                { Family = fam
                  Size = size
                  Weight = weight
                  Italic = italic }
        }

    /// Strings that exercise XML escaping, whitespace preservation and NCName sanitisation.
    /// Never null: `Text` with a null string is not a value the IR is meant to hold, and
    /// `String.IsNullOrEmpty` already treats it as empty everywhere it matters.
    let text: Gen<string> =
        Gen.elements
            [ ""
              "x"
              "1.5"
              "E [keV]"
              "a & b"
              "<tag>"
              "\"quoted\""
              "  padded  "
              "kg·m·s⁻²"
              "µm"
              "line\nbreak" ]

    /// Optional TeX source for a run. Mostly `None`, because most runs have none.
    ///
    /// `Some ""` is in the distribution deliberately: it is `Scene.prune`'s edge case. A run with
    /// no plain text and an *empty* TeX form must be pruned, while a run with no plain text and a
    /// *non-empty* TeX form must survive — testing `Text` alone would delete the second kind, and
    /// it would only disappear on a typesetting backend, which is the worst possible shape for
    /// the bug.
    let private texSource: Gen<string option> =
        let real =
            Gen.elements
                [ "\\qty{}{\\metre}"
                  "$x$ [\\unit{\\metre\\per\\second\\squared}]"
                  "\\mathrm{\\Omega}"
                  "50\\% yield\\_2" ]
            |> Gen.map Some

        Gen.frequency
            [ 4, Gen.constant None
              1, Gen.constant (Some "")
              2, real ]

    let private textRun: Gen<TextRun> =
        gen {
            let! s = text
            let! tex = texSource
            let! at = vec2Nd
            let! font = fontSpec
            let! p = paint
            let! anchor = Gen.elements [ AnchorStart; AnchorMiddle; AnchorEnd ]
            let! baseline = Gen.elements [ BaselineAlphabetic; BaselineMiddle; BaselineHanging ]
            let! rot = rotation

            return
                { Text = s
                  Tex = tex
                  At = at
                  Font = font
                  Paint = p
                  Anchor = anchor
                  Baseline = baseline
                  Rotation = rot }
        }

    let markerShape: Gen<MarkerShape> =
        Gen.elements
            [ MarkCircle
              MarkSquare
              MarkDiamond
              MarkTriangleUp
              MarkTriangleDown
              MarkCross
              MarkPlus ]

    let private markerBatch: Gen<MarkerBatch> =
        gen {
            let! n = Gen.choose (0, 12)
            let! pts = Gen.arrayOfLength n vec2Nd
            let! shape = markerShape
            let! size = Gen.choose (1, 40000) |> Gen.map (fun i -> float i / 1000.0 * 1.0<px>)
            let! fill = Gen.frequency [ 3, paint |> Gen.map Some; 1, Gen.constant None ]
            let! st = Gen.frequency [ 2, stroke |> Gen.map Some; 2, Gen.constant None ]

            return
                { Points = pts
                  Shape = shape
                  Size = size
                  Fill = fill
                  Stroke = st }
        }

    let private rasterNode: Gen<RasterNode> =
        gen {
            let! w = Gen.choose (1, 4)
            let! h = Gen.choose (1, 4)
            let! px' = Gen.arrayOfLength (w * h * 4) (Gen.choose (0, 255) |> Gen.map byte)
            let! bounds = rectNd
            let! smooth = Gen.elements [ true; false ]

            return
                { Image = { Width = w; Height = h; Rgba = px' }
                  Bounds = bounds
                  Smooth = smooth }
        }

    let private semantics: Gen<Semantics> =
        gen {
            let! role =
                Gen.frequency
                    [ 6,
                      Gen.elements
                          [ RoleSeries
                            RoleDatum
                            RoleAxis
                            RoleLegend
                            RoleAnnotation
                            RoleDecoration ]
                      1, text |> Gen.map RoleOther ]

            let! label = text
            let! n = Gen.choose (0, 3)
            let! keys = Gen.arrayOfLength n text
            let! vals = Gen.arrayOfLength n text
            return { Role = role; Label = label; Data = Array.zip keys vals }
        }

    let affineNdNd: Gen<Affine<nd, nd>> =
        gen {
            let! (Mat(a, b, c, d, dx, dy)) = mat

            return
                { M11 = a
                  M12 = b
                  M21 = c
                  M22 = d
                  Dx = dx * 1.0<nd>
                  Dy = dy * 1.0<nd> }
        }

    /// Depth-limited so the tree stays comparable in reasonable time: `Node` equality is
    /// structural and O(size), and `SceneProperties` compares whole trees repeatedly.
    let rec private nodeOfDepth (depth: int) : Gen<Node> =
        let leaf =
            Gen.frequency
                [ 4,
                  gen {
                      let! n = Gen.choose (0, 8)
                      let! segs = Gen.arrayOfLength n pathSeg
                      let! st = Gen.frequency [ 3, stroke |> Gen.map Some; 1, Gen.constant None ]
                      let! fill = Gen.frequency [ 2, paint |> Gen.map Some; 2, Gen.constant None ]
                      let! rule = Gen.elements [ NonZero; EvenOdd ]
                      return Path(segs, st, fill, rule)
                  }
                  3, textRun |> Gen.map Text
                  3, markerBatch |> Gen.map Markers
                  1, rasterNode |> Gen.map Raster ]

        if depth <= 0 then
            leaf
        else
            let child = nodeOfDepth (depth - 1)

            Gen.frequency
                [ 6, leaf
                  3,
                  gen {
                      let! n = Gen.choose (0, 3)
                      return! Gen.arrayOfLength n child |> Gen.map Group
                  }
                  2,
                  gen {
                      let! a = affineNdNd
                      let! c = child
                      return Transform(a, c)
                  }
                  2,
                  gen {
                      let! n = Gen.choose (0, 5)
                      let! segs = Gen.arrayOfLength n pathSeg
                      let! c = child
                      return Clip(segs, c)
                  }
                  2,
                  gen {
                      // Generated in [0,1] rather than over the whole `float` line. An
                      // out-of-range opacity is not a scene anything produces — `Scene.opacity`
                      // clamps — so generating one would make every property here a statement
                      // about a value the IR is not meant to hold. `0.0` and `1.0` are included,
                      // because those two are the boundaries a backend is most likely to
                      // special-case.
                      let! a = Gen.elements [ 0.0; 0.25; 0.5; 1.0 ]
                      let! c = child
                      return Opacity(a, c)
                  }
                  2,
                  gen {
                      let! k = text
                      let! c = child
                      return Keyed(Key k, c)
                  }
                  2,
                  gen {
                      let! s = semantics
                      let! c = child
                      return Annotated(s, c)
                  } ]

    let scene: Gen<Node> = Gen.choose (0, 3) |> Gen.bind nodeOfDepth

    let animSeconds: Gen<float> = Gen.choose (0, 64) |> Gen.map (fun i -> float i / 8.0)

    let stretchFactor: Gen<float> =
        Gen.choose (-4, 4) |> Gen.map (fun i -> 2.0 ** float i)

    /// Deliberately unbounded by the durations: a third of the range is outside `[0, 8]` on one
    /// side or the other, which is where `At`'s clamp lives.
    let seek: Gen<float> =
        Gen.frequency
            [ 2, Gen.elements [ 0.0; 1.0; 8.0; -1.0 ]
              8, Gen.choose (-32, 96) |> Gen.map (fun i -> float i / 8.0) ]

    let animSpec: Gen<AnimSpec> =
        gen {
            let! d = animSeconds
            let! k = Gen.choose (-40, 40) |> Gen.map (fun i -> float i / 10.0)
            return AnimSpec(d, k)
        }

    let morphPath: Gen<PathSeg[]> =
        let subpath =
            gen {
                let! start = vec2Nd
                let! n = Gen.choose (0, 4)

                let! segs =
                    Gen.arrayOfLength
                        n
                        (Gen.frequency
                            [ 3, vec2Nd |> Gen.map LineTo
                              1,
                              gen {
                                  let! c1 = vec2Nd
                                  let! c2 = vec2Nd
                                  let! until = vec2Nd
                                  return CubicTo(c1, c2, until)
                              } ])

                let! closed = Gen.elements [ true; false ]

                return
                    [| yield MoveTo start
                       yield! segs
                       if closed then
                           yield ClosePath |]
            }

        gen {
            let! n = Gen.choose (0, 3)
            let! subs = Gen.arrayOfLength n subpath
            return Array.concat subs
        }

    let rawPath: Gen<PathSeg[]> =
        Gen.choose (0, 8) |> Gen.bind (fun n -> Gen.arrayOfLength n pathSeg)

// ======================================================================================
// ARBITRARY REGISTRATION
//
// No shrinkers. Every generator here maintains an invariant that the default shrinkers would
// break — a shrunk log domain can contain zero, a shrunk invertible matrix is singular, a
// shrunk `linthresh` is negative — and a shrinker that produces an input outside the property's
// precondition reports a "minimal counterexample" that is not a counterexample at all. The
// generated values print as data (`ScaleSpec` is a DU, `Mat` is six floats), which is what makes
// a failure reproducible without one.
// ======================================================================================

type MareyArbs =

    static member FiniteFloat() = Gens.finiteFloat |> Gen.map FiniteFloat |> Arb.fromGen

    static member PositiveFloat() =
        Gens.positiveFloat |> Gen.map PositiveFloat |> Arb.fromGen

    static member UnitT() = Gens.unitT |> Gen.map UnitT |> Arb.fromGen

    static member SmallDim() = Gens.smallDim |> Gen.map SmallDim |> Arb.fromGen

    static member LinearDomain() =
        Gens.linearDomain |> Gen.map LinearDomain |> Arb.fromGen

    static member LogDomain() = Gens.logDomain |> Gen.map LogDomain |> Arb.fromGen

    static member ReciprocalDomain() =
        Gens.reciprocalDomain |> Gen.map ReciprocalDomain |> Arb.fromGen

    static member ScaleSpec() = Gens.anyScale |> Arb.fromGen

    static member WellConditionedScale() =
        Gens.wellConditionedScale |> Gen.map WellConditionedScale |> Arb.fromGen

    static member SignedPower() =
        Gens.signedPower |> Gen.map SignedPower |> Arb.fromGen

    static member TickTarget() =
        Gen.choose (4, 24) |> Gen.map TickTarget |> Arb.fromGen

    static member Mat() = Gens.mat |> Arb.fromGen

    static member InvertibleMat() =
        Gens.invertibleMat |> Gen.map InvertibleMat |> Arb.fromGen

    static member Colour() = Gens.colour |> Gen.map Colour |> Arb.fromGen

    static member Vec2Nd() = Gens.vec2Nd |> Gen.map Vec2Nd |> Arb.fromGen

    static member Vec3Nd() = Gens.vec3Nd |> Gen.map Vec3Nd |> Arb.fromGen

    static member RectNd() = Gens.rectNd |> Gen.map RectNd |> Arb.fromGen

    static member Rotation() = Gens.rotation |> Gen.map Rotation |> Arb.fromGen

    static member SceneTree() = Gens.scene |> Gen.map SceneTree |> Arb.fromGen

    static member MarkerShape() = Gens.markerShape |> Arb.fromGen

    static member AnimSeconds() = Gens.animSeconds |> Gen.map AnimSeconds |> Arb.fromGen

    static member StretchFactor() =
        Gens.stretchFactor |> Gen.map StretchFactor |> Arb.fromGen

    static member Seek() = Gens.seek |> Gen.map Seek |> Arb.fromGen

    static member AnimSpec() = Gens.animSpec |> Arb.fromGen

    static member MorphPath() = Gens.morphPath |> Gen.map MorphPath |> Arb.fromGen

    static member RawPath() = Gens.rawPath |> Gen.map RawPath |> Arb.fromGen

// ======================================================================================
// SHARED HELPERS
// ======================================================================================

/// Turn a `ScaleSpec` into the scale it describes. `Scale<1>` throughout: `float<1> = float`, so
/// the numeric laws are stated once and the measure-carrying behaviour is tested separately in
/// `WitnessProperties` and `TickProperties`.
[<RequireQualifiedAccess>]
module ScaleSpec =

    let domain (s: ScaleSpec) =
        match s with
        | SLinear(a, b)
        | SLog(_, a, b)
        | SSymlog(_, _, a, b)
        | SPower(_, a, b)
        | SReciprocal(a, b) -> a, b

    let span (s: ScaleSpec) =
        let a, b = domain s
        abs (b - a)

    let build (s: ScaleSpec) : Scale<1> =
        match s with
        | SLinear(a, b) -> Scale.linear U.one (a, b)
        | SLog(bse, a, b) -> Scale.logBase bse U.one (a, b)
        | SSymlog(bse, lt, a, b) -> Scale.symlog bse lt U.one (a, b)
        | SPower(p, a, b) -> Scale.power p U.one (a, b)
        | SReciprocal(a, b) -> Scale.reciprocal U.one (a, b)

    /// Sample points across the domain, endpoints included. Deterministic — a fixed lattice, not
    /// a second random draw — so a failing case is fully described by the `ScaleSpec` alone.
    let samples (n: int) (s: ScaleSpec) =
        let lo, hi = domain s
        Array.init n (fun i -> lo + (hi - lo) * float i / float (n - 1))

/// Running a thunk under a chosen `CultureInfo`.
///
/// SvgWriter.fs opens with the requirement this module exists to check: "**the same Scene must
/// produce byte-identical output on every machine, every run**", and the first bullet under it
/// is that a German locale would otherwise emit `0,5`, "which is not a number SVG can parse —
/// this is a correctness bug, not a cosmetic one".
///
/// The culture is set on a *dedicated thread* rather than on the ambient one. xUnit runs test
/// collections in parallel over a shared thread pool, so mutating `CurrentCulture` in place
/// would leak into whatever else happened to be running and make the suite's result depend on
/// scheduling — the precise opposite of what this test is for.
[<RequireQualifiedAccess>]
module Culture =

    let run (name: string) (f: unit -> 'a) : 'a =
        let mutable result = Unchecked.defaultof<'a>
        let mutable error: exn = null

        let body () =
            try
                let c = System.Globalization.CultureInfo(name)
                System.Threading.Thread.CurrentThread.CurrentCulture <- c
                System.Threading.Thread.CurrentThread.CurrentUICulture <- c
                result <- f ()
            with e ->
                error <- e

        let t = System.Threading.Thread(body)
        t.Start()
        t.Join()

        if not (isNull error) then
            raise (System.Exception("Marey.Tests: culture '" + name + "' run failed.", error))

        result

    /// The canonical hostile locale: comma decimal separator, dot thousands separator.
    [<Literal>]
    let CommaDecimal = "de-DE"

    /// Turkish. Included because its dotless-i lowercasing breaks `ToLowerInvariant`-free code
    /// paths — `ApproxTextMeasurer.familyFactor` lowercases a font family name.
    [<Literal>]
    let DotlessI = "tr-TR"

/// The replay seed for every property in the suite.
///
/// §"Hard rules": the suite must be deterministic. FsCheck seeds itself from the clock by
/// default, which makes a property that fails one run in fifty a property that fails on someone
/// else's machine, at random, months later. A fixed seed trades a little coverage per run for a
/// suite whose green is reproducible — and the coverage is bought back with a high `MaxTest`
/// instead.
[<Literal>]
let Seed = "17081337,29082027"
