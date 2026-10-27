namespace Marey

/// What gridlines to draw, in **normalized panel space after scaling but before the coord
/// warp**. The coord decides how those straight lines curve.
[<NoComparison>]
type GridSpec =
    { /// Positions of constant-x gridlines, in [0,1].
      X: float<nd>[]
      /// Positions of constant-y gridlines, in [0,1].
      Y: float<nd>[]
      /// Points sampled along each line when the coord curves it. Cartesian ignores this and
      /// emits two-point lines; polar needs ~64 to look round.
      Samples: int }

/// Coordinate systems, which sit *above* scales.
///
/// "Cartesian is the identity `Coord`. Polar warps and gives circular gridlines and a circular
/// clip. **Nothing else in the system needs to know polar exists** — which is exactly the
/// property that makes the design worth having."
///
/// That last sentence is the acceptance test for this interface. If a geom, a backend, or the
/// layout pass ever has to ask "am I polar?", this abstraction has failed and the leak should
/// be fixed here rather than special-cased there.
///
/// An interface rather than a DU, deliberately: unlike `Node`, this type is *meant* to be
/// extended from outside. A user's Smith chart or Mollweide projection is three functions, and
/// adding it must not require touching Marey.Core.
type Coord =
    /// Post-scale warp. Input and output are both normalized panel space.
    abstract ToPanel: Vec2<nd> -> Vec2<nd>

    /// Inverse warp, for hit-testing and tooltips. `None` where the map is not invertible —
    /// the pole of a polar plot, the antipode of an azimuthal projection.
    abstract FromPanel: Vec2<nd> -> Vec2<nd> option

    /// How gridlines curve under this coord.
    abstract Grid: GridSpec -> PathSeg[][]

    /// The panel boundary shape. Rectangular for cartesian, circular for polar — and it is
    /// what the compiler feeds to the IR's `Clip` node.
    abstract Clip: PathSeg[]

/// Coordinate systems for a panel: Cartesian, flipped, polar and polar sectors, and warping a
/// polyline through one.
[<RequireQualifiedAccess>]
module Coord =

    let private corners =
        [| { X = 0.0<nd>; Y = 0.0<nd> }
           { X = 1.0<nd>; Y = 0.0<nd> }
           { X = 1.0<nd>; Y = 1.0<nd> }
           { X = 0.0<nd>; Y = 1.0<nd> } |]

    let private closedPath (pts: Vec2<nd>[]) =
        [| yield MoveTo pts.[0]
           for i in 1 .. pts.Length - 1 do
               yield LineTo pts.[i]
           yield ClosePath |]

    /// Sample a parametric curve into a polyline path. Shared by every non-identity coord.
    let private sampleCurve (n: int) (f: float -> Vec2<nd>) =
        let n = max 2 n

        [| yield MoveTo(f 0.0)
           for i in 1 .. n - 1 do
               yield LineTo(f (float i / float (n - 1))) |]

    /// The identity coordinate system. Straight gridlines, rectangular clip.
    let cartesian: Coord =
        { new Coord with
            member _.ToPanel p = p
            member _.FromPanel p = Some p

            member _.Grid spec =
                [| for x in spec.X do
                       yield [| MoveTo { X = x; Y = 0.0<nd> }; LineTo { X = x; Y = 1.0<nd> } |]
                   for y in spec.Y do
                       yield [| MoveTo { X = 0.0<nd>; Y = y }; LineTo { X = 1.0<nd>; Y = y } |] |]

            member _.Clip = closedPath corners }

    /// Polar: x becomes the angle, y becomes the radius.
    ///
    /// `startAngle`/`endAngle` let this cover the full circle, a half-disc, or a wedge without
    /// a second implementation. The radial extent is fixed to the inscribed circle of the
    /// panel, so the panel's own aspect ratio — a layout concern — never leaks in here.
    let polarBetween (startAngle: float<rad>) (endAngle: float<rad>) (innerRadius: float) : Coord =
        let a0 = float startAngle
        let a1 = float endAngle
        let r0 = max 0.0 (min 0.999 innerRadius)

        let toPanel (p: Vec2<nd>) : Vec2<nd> =
            let theta = a0 + (a1 - a0) * (p.X / 1.0<nd>)
            let r = (r0 + (1.0 - r0) * (p.Y / 1.0<nd>)) * 0.5

            { X = (0.5 + r * cos theta) * 1.0<nd>
              Y = (0.5 + r * sin theta) * 1.0<nd> }

        { new Coord with
            member _.ToPanel p = toPanel p

            member _.FromPanel p =
                let dx = p.X / 1.0<nd> - 0.5
                let dy = p.Y / 1.0<nd> - 0.5
                let r = sqrt (dx * dx + dy * dy) * 2.0

                // The pole carries no angular information: inverting there would invent one.
                if r < 1e-12 then
                    None
                else
                    let theta = atan2 dy dx
                    let span = a1 - a0

                    if span = 0.0 || r0 >= 1.0 then
                        None
                    else
                        Some
                            { X = (theta - a0) / span * 1.0<nd>
                              Y = (r - r0) / (1.0 - r0) * 1.0<nd> }

            member _.Grid spec =
                [|
                   // Constant x: rays from centre outward. Straight after the warp, so two
                   // points suffice regardless of `Samples`.
                   for x in spec.X do
                       yield
                           [| MoveTo(toPanel { X = x; Y = 0.0<nd> })
                              LineTo(toPanel { X = x; Y = 1.0<nd> }) |]
                   // Constant y: arcs. These are the ones that need sampling.
                   for y in spec.Y do
                       yield sampleCurve spec.Samples (fun t -> toPanel { X = t * 1.0<nd>; Y = y }) |]

            member _.Clip =
                let outer = sampleCurve 64 (fun t -> toPanel { X = t * 1.0<nd>; Y = 1.0<nd> })
                Array.append outer [| ClosePath |] }

    /// Full-circle polar.
    let polar = polarBetween 0.0<rad> (2.0 * System.Math.PI * 1.0<rad>) 0.0

    /// Swap the axes. `flipped cartesian` is how a horizontal bar chart is a vertical one —
    /// no second geom, no `Orientation` flag threaded through the grammar.
    let flipped (inner: Coord) : Coord =
        let swap (p: Vec2<nd>) : Vec2<nd> = { X = p.Y; Y = p.X }

        { new Coord with
            member _.ToPanel p = inner.ToPanel(swap p)
            member _.FromPanel p = inner.FromPanel p |> Option.map swap

            member _.Grid spec =
                inner.Grid { spec with X = spec.Y; Y = spec.X }

            member _.Clip = inner.Clip }

    /// True when the coord lays x up the panel and y across it, as `flipped cartesian` does,
    /// so the axis reading x stands on the left and the one reading y along the bottom.
    ///
    /// Asked of the map rather than of which coord it is, at the unit square's corners and an
    /// off-diagonal point, so a user's own transposing coord gets its axes moved too.
    let internal transposes (c: Coord) =
        [| 0.0, 0.0; 1.0, 0.0; 0.0, 1.0; 1.0, 1.0; 0.25, 0.75 |]
        |> Array.forall (fun (x, y) ->
            let q = c.ToPanel { X = x * 1.0<nd>; Y = y * 1.0<nd> }
            abs (float q.X - y) < 1e-9 && abs (float q.Y - x) < 1e-9)

    /// Apply a coord to a polyline, resampling straight segments so they curve correctly.
    ///
    /// This is the function that keeps the leak sealed: a geom emits straight segments in
    /// scaled space and calls this, and a line under `polar` comes out as a spiral without
    /// the geom knowing anything happened.
    let warpPolyline (c: Coord) (samplesPerSegment: int) (pts: Vec2<nd>[]) : Vec2<nd>[] =
        if pts.Length < 2 then
            Array.map c.ToPanel pts
        else
            let k = max 1 samplesPerSegment
            let out = ResizeArray<Vec2<nd>>((pts.Length - 1) * k + 1)
            out.Add(c.ToPanel pts.[0])

            for i in 1 .. pts.Length - 1 do
                let a = pts.[i - 1]
                let b = pts.[i]

                for j in 1..k do
                    let t = float j / float k
                    out.Add(c.ToPanel(Vec2.lerp t a b))

            out.ToArray()

    /// True when the coord is the identity, so callers can skip resampling entirely.
    /// A hint, not a question about *which* coord it is — the distinction that keeps
    /// "nothing else needs to know polar exists" honest.
    let isIdentity (c: Coord) = System.Object.ReferenceEquals(c, cartesian)
