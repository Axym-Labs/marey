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

/// Paths the coords share.
module internal CoordPath =

    /// Sample a parametric curve into a polyline path. Shared by every non-identity coord.
    let sampleCurve (n: int) (f: float -> Vec2<nd>) =
        let n = max 2 n

        [| yield MoveTo(f 0.0)
           for i in 1 .. n - 1 do
               yield LineTo(f (float i / float (n - 1))) |]

/// **A coord that zooms by changing its own view**, as a polar one does: a box dragged over it
/// is shown as the region of its unit square that bounds the box, drawn in that coord's own
/// geometry and magnified, rather than as that region's domains stretched over the whole coord.
///
/// Internal, and asked of through `Coord.zoomTo` and `Coord.zoomedTo`, so a coord a user writes
/// keeps the interface's three members and zooms by its domains alone, as before.
[<AbstractClass>]
type internal ZoomingCoord() =
    /// The part of the unit square, before the warp, that bounds `area`, a rectangle of the
    /// panel after it. `None` where the coord cannot say.
    abstract ZoomTo: area: Rect<nd> -> Rect<nd> option

    /// The coord that draws the part `part` of this one's unit square, before the warp, across
    /// its own: what a panel whose domains were narrowed to `part` is drawn with.
    abstract Zoomed: part: Rect<nd> -> Coord

    static member ZoomTo(c: Coord, area: Rect<nd>) =
        match box c with
        | :? ZoomingCoord as z -> z.ZoomTo area
        | _ -> None

    static member Zoomed(c: Coord, part: Rect<nd>) =
        match box c with
        | :? ZoomingCoord as z -> z.Zoomed part
        | _ -> c

/// **Polar**, drawing the part `gx` by `gy` of its unit square: x becomes the angle from `a0` to
/// `a1` and y the radius out from `r0`, and that part of it, an annular sector, is drawn as large
/// as the panel holds. The whole square is `Coord.polarBetween`.
type internal PolarCoord(a0: float, a1: float, r0: float, gx: float * float, gy: float * float) =
    inherit ZoomingCoord()

    let turn = 2.0 * System.Math.PI
    let gx0, gx1 = gx
    let gy0, gy1 = gy
    let span = a1 - a0
    let whole = gx = (0.0, 1.0) && gy = (0.0, 1.0)

    // Angle and radius, the radius as a fraction of the full one.
    let angleAt (u: float) = a0 + span * (gx0 + (gx1 - gx0) * u)
    let radiusAt (v: float) = r0 + (1.0 - r0) * (gy0 + (gy1 - gy0) * v)

    // The drawn sector's angles and radii.
    let lo, hi = angleAt 0.0, angleAt 1.0
    let mid = (lo + hi) / 2.0
    let inner, outer = radiusAt 0.0, radiusAt 1.0
    let round = abs (hi - lo) >= turn - 1e-12

    // **The view**: the pole at `(cx, cy)` of the panel and the full radius `k` halves of it. The
    // whole square keeps the pole in the middle and the full radius at the edge; a part of it is
    // its sector's bounding box, scaled the same way on both axes to fill the panel, so that
    // angles are still true angles.
    let cx, cy, k =
        if whole then
            0.5, 0.5, 1.0
        else
            // Offsets from the pole, at a full radius of one half.
            let at (a: float) (r: float) = 0.5 * r * cos a, 0.5 * r * sin a
            let rIn = max 0.0 inner

            let pts =
                if round then
                    [ at 0.0 outer; at (System.Math.PI / 2.0) outer; at System.Math.PI outer; at (-System.Math.PI / 2.0) outer ]
                else
                    let a, b = min lo hi, max lo hi

                    [ yield at a rIn
                      yield at b rIn
                      yield at a outer
                      yield at b outer
                      // The outer arc's extremes, where it crosses an axis.
                      for q in ceil (a / (System.Math.PI / 2.0)) .. floor (b / (System.Math.PI / 2.0)) do
                          yield at (q * System.Math.PI / 2.0) outer ]

            let xs, ys = List.map fst pts, List.map snd pts
            let x0, x1, y0, y1 = List.min xs, List.max xs, List.min ys, List.max ys
            let k = 1.0 / max (max (x1 - x0) (y1 - y0)) 1e-12
            0.5 - k * (x0 + x1) / 2.0, 0.5 - k * (y0 + y1) / 2.0, k

    let toPanel (p: Vec2<nd>) : Vec2<nd> =
        let theta = angleAt (p.X / 1.0<nd>)
        let r = radiusAt (p.Y / 1.0<nd>) * 0.5

        { X = (cx + k * (r * cos theta)) * 1.0<nd>
          Y = (cy + k * (r * sin theta)) * 1.0<nd> }

    // An angle into the turn centred on the drawn sector, so a point reads the side of the
    // sector it is nearer: the lower half of a full circle is the end of its turn, not angles
    // before its start. One already in that turn is kept as it is.
    let unwrap (theta: float) =
        if theta >= mid - System.Math.PI && theta < mid + System.Math.PI then
            theta
        else
            theta - turn * floor ((theta - (mid - System.Math.PI)) / turn)

    // The angle and radius, before the view, as a point of this coord's unit square.
    let ofPolar (theta: float) (r: float) : float * float =
        ((theta - a0) / span - gx0) / (gx1 - gx0), ((r - r0) / (1.0 - r0) - gy0) / (gy1 - gy0)

    // A path along the curve `f`, from `a` to `b`, without its start.
    let along (n: int) (a: float) (b: float) (f: float -> Vec2<nd>) =
        [| for i in 1..n -> LineTo(f (a + (b - a) * float i / float n)) |]

    override _.ZoomTo(area: Rect<nd>) =
        if span = 0.0 || r0 >= 1.0 then
            None
        else
            // The box as offsets from the pole, before the view, at a full radius of one half.
            let ox (x: float<nd>) = (float x - cx) / k
            let oy (y: float<nd>) = (float y - cy) / k
            let x0, x1 = min (ox area.X0) (ox area.X1), max (ox area.X0) (ox area.X1)
            let y0, y1 = min (oy area.Y0) (oy area.Y1), max (oy area.Y0) (oy area.Y1)
            let radius (x: float) (y: float) = 2.0 * sqrt (x * x + y * y)
            let corners = [ x0, y0; x1, y0; x1, y1; x0, y1 ]
            let holdsPole = x0 <= 0.0 && x1 >= 0.0 && y0 <= 0.0 && y1 >= 0.0

            // The radii: out to the farthest corner, and in to the nearest point of the box,
            // which is on an edge rather than at a corner when the box straddles an axis.
            let rHi = corners |> List.map (fun (x, y) -> radius x y) |> List.max
            let rLo = if holdsPole then 0.0 else radius (max x0 (min x1 0.0)) (max y0 (min y1 0.0))

            // The angles: every drawn one for a box over the pole; otherwise the box subtends
            // less than half a turn, and its corners' angles about the box's own direction bound
            // it, whichever side of the start of the turn they fall.
            let u0, u1 =
                if holdsPole then
                    0.0, 1.0
                else
                    let c = atan2 ((y0 + y1) / 2.0) ((x0 + x1) / 2.0)

                    let around =
                        corners
                        |> List.map (fun (x, y) ->
                            let d = atan2 y x - c
                            d - turn * System.Math.Round(d / turn))

                    let from = unwrap (c + List.min around)
                    let u0 = fst (ofPolar from 1.0)
                    u0, u0 + (List.max around - List.min around) / (span * (gx1 - gx0))

            let v0, v1 = snd (ofPolar 0.0 rLo), snd (ofPolar 0.0 rHi)

            Some
                { X0 = min u0 u1 * 1.0<nd>
                  Y0 = min v0 v1 * 1.0<nd>
                  X1 = max u0 u1 * 1.0<nd>
                  Y1 = max v0 v1 * 1.0<nd> }

    override _.Zoomed(part: Rect<nd>) =
        let within (g0: float, g1: float) (p0: float<nd>) (p1: float<nd>) =
            g0 + (g1 - g0) * float p0, g0 + (g1 - g0) * float p1

        PolarCoord(a0, a1, r0, within gx part.X0 part.X1, within gy part.Y0 part.Y1)

    interface Coord with
        member _.ToPanel p = toPanel p

        member _.FromPanel p =
            let dx = (p.X / 1.0<nd> - cx) / k
            let dy = (p.Y / 1.0<nd> - cy) / k
            let r = sqrt (dx * dx + dy * dy) * 2.0

            // The pole carries no angular information: inverting there would invent one.
            if r < 1e-12 then
                None
            elif span = 0.0 || r0 >= 1.0 then
                None
            else
                let u, v = ofPolar (unwrap (atan2 dy dx)) r
                Some { X = u * 1.0<nd>; Y = v * 1.0<nd> }

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
                   yield CoordPath.sampleCurve spec.Samples (fun t -> toPanel { X = t * 1.0<nd>; Y = y }) |]

        member _.Clip =
            let arc (y: float<nd>) (t: float) = toPanel { X = t * 1.0<nd>; Y = y }

            if whole then
                let outer = CoordPath.sampleCurve 64 (arc 1.0<nd>)
                Array.append outer [| ClosePath |]
            else
                // The annular sector: out along the outer arc, and back along the inner one, or
                // through the pole when the sector reaches it.
                let pole: Vec2<nd> = { X = cx * 1.0<nd>; Y = cy * 1.0<nd> }

                [| yield MoveTo(arc 1.0<nd> 0.0)
                   yield! along 64 0.0 1.0 (arc 1.0<nd>)
                   if inner > 0.0 then
                       yield LineTo(arc 0.0<nd> 1.0)
                       yield! along 64 1.0 0.0 (arc 0.0<nd>)
                   elif not round then
                       yield LineTo pole
                   yield ClosePath |]

/// A coord with its axes swapped, zooming as the coord inside it does.
type internal FlippedCoord(inner: Coord) =
    inherit ZoomingCoord()

    static let swap (p: Vec2<nd>) : Vec2<nd> = { X = p.Y; Y = p.X }

    static let swapRect (r: Rect<nd>) : Rect<nd> =
        { X0 = r.Y0
          Y0 = r.X0
          X1 = r.Y1
          Y1 = r.X1 }

    override _.ZoomTo area =
        ZoomingCoord.ZoomTo(inner, area) |> Option.map swapRect

    override _.Zoomed part =
        FlippedCoord(ZoomingCoord.Zoomed(inner, swapRect part))

    interface Coord with
        member _.ToPanel p = inner.ToPanel(swap p)
        member _.FromPanel p = inner.FromPanel p |> Option.map swap

        member _.Grid spec =
            inner.Grid { spec with X = spec.Y; Y = spec.X }

        member _.Clip = inner.Clip

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
        PolarCoord(float startAngle, float endAngle, max 0.0 (min 0.999 innerRadius), (0.0, 1.0), (0.0, 1.0))

    /// Full-circle polar.
    let polar = polarBetween 0.0<rad> (2.0 * System.Math.PI * 1.0<rad>) 0.0

    /// Swap the axes. `flipped cartesian` is how a horizontal bar chart is a vertical one —
    /// no second geom, no `Orientation` flag threaded through the grammar.
    let flipped (inner: Coord) : Coord = FlippedCoord inner

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

    /// **The part of a coord's unit square a box zoom shows**: for a coord that zooms by its own
    /// view, as polar does, the region bounding `area`, a rectangle of the panel after the warp,
    /// in the unit square before it. For a polar coord that is the annular sector around the box:
    /// every drawn angle and out from the pole when the box holds the pole, and otherwise the
    /// box's angles, across the start of the turn if it straddles it, and its nearest and
    /// farthest distances from the pole.
    ///
    /// `None` for a coord that zooms by its domains alone: cartesian, flipped cartesian, and any
    /// coord written outside Marey.
    let internal zoomTo (c: Coord) (area: Rect<nd>) : Rect<nd> option = ZoomingCoord.ZoomTo(c, area)

    /// The coord that draws the part `part` of `c`'s unit square, before the warp, across the
    /// panel: the view of a box zoom, for a panel whose domains were narrowed to `part`. `c`
    /// itself for a coord that zooms by its domains alone.
    let internal zoomedTo (c: Coord) (part: Rect<nd>) : Coord = ZoomingCoord.Zoomed(c, part)
