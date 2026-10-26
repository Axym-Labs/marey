namespace Marey

open System
open System.Collections.Generic

// ======================================================================================
// WHAT A FIELD LAYER DRAWS, COMPUTED ON THE PANEL.
//
// A field layer carries a grid in data space and is drawn at compile time, after `Geom.project`
// has put its node coordinates on the panel through the panel's final scales. Everything
// in this file therefore works in `nd`, on a rectilinear grid whose nodes are where the panel
// says they are — which is what makes a heatmap, its contours and its bands line up with each
// other and with every other layer, on a linear axis and a log one alike.
//
// **Interpolation is in panel space.** Between nodes, a value is read bilinearly in `nd`, not in
// data units. On a linear axis the two are the same. On a log axis the panel-space reading is
// the one a reader sees — a straight contour segment across a cell is straight on the page —
// and it is the space a grid sampled with `Grid.log` is uniform in.
//
// **Missing values**. A node that is not finite is not drawn: it is a transparent pixel,
// no contour line crosses a cell that has one, and every band treats it as below every level, so
// no band covers it. It was counted when the grid was built.
// ======================================================================================

/// What a field layer draws.
type FieldMark =
    /// The scalar as an image, one pixel per node, coloured by the layer's colour channel.
    /// `smooth` interpolates between nodes; otherwise each node is a flat cell.
    | FieldHeatmap of smooth: bool
    /// Iso-lines at the given values, raw in the field's unit.
    | FieldContours of levels: float[]
    /// Filled bands between consecutive levels, each coloured at its midpoint value.
    | FieldBands of levels: float[]
    /// An arrow at every node, as long as the vector's magnitude through the quiver's own length
    /// scale, with a key saying what one length means. The payload's `Values` are the
    /// magnitudes.
    | FieldQuiver of key: QuiverKey option
    /// An arrow at every node, all one length: direction only. What a phase space gets, where
    /// the two components are in different units and a magnitude does not exist.
    | FieldDirections
    /// Evenly spaced streamlines, `separation` apart on the panel, with arrowheads along
    /// them. The payload's `Values` colour each line by their mean along it.
    | FieldStreamlines of separation: float<px>
    /// Line-integral convolution: a noise texture smeared along the field's streamlines,
    /// rasterised at the plot area's resolution. Its brightness is the texture; its colour is the
    /// payload's `Values` through the layer's colour.
    | FieldLic of LicOptions

/// What a line-integral convolution is computed with.
and LicOptions =
    { /// The convolution kernel's full length along a streamline, on the page. Longer reads as
      /// smoother, more coherent strokes; shorter as grainier ones.
      Length: float<px>
      /// Raster pixels per page pixel along each axis: 1 for a screen, 2 or 3 for print.
      Oversample: int
      /// The noise texture's seed. The same seed, field and plot size draw the same image.
      Seed: uint64 }

/// What one arrow length means: a round value of the magnitude and its label, prefix and
/// unit included, both decided from the data when the layer is built, and the magnitude the
/// length scale is set against. The arrow it is drawn with
/// is sized on the panel, so the label is the same at every figure size and the arrow is not.
and QuiverKey =
    { Value: float
      Label: string
      /// The magnitude the length scale is set against: the longest arrow is 0.9 of the grid's
      /// pitch at this magnitude. The layer's own largest when it is built; across a film, the
      /// largest of every sampled frame's, so the arrows grow and shrink with the field rather
      /// than being renormalised each frame.
      Largest: float }

/// A grid with its measures spent: node coordinates in `'p` — data space on a layer, `nd` once
/// projected — and values raw in the field's unit.
///
/// `U` and `W` are a vector field's components, empty for a scalar one. On a layer they are raw
/// in the field's units; `Geom.project` multiplies each by its scale's derivative at the node, so
/// on the panel they are the vector's direction in `nd` — the direction a streamline through the
/// node is drawn in, on a linear axis or a log one.
type FieldPayload<[<Measure>] 'p> =
    { Xs: float<'p>[]
      Ys: float<'p>[]
      Values: float[]
      U: float[]
      W: float[] }

[<RequireQualifiedAccess>]
module internal FieldMarks =

    let private finite (v: float) = not (Double.IsNaN v || Double.IsInfinity v)

    /// How long a filled arrowhead is, in px, on a stroke `width` wide.
    let arrowheadLength (width: float<px>) = 3.0 + 2.0 * float width

    /// A filled arrowhead's three corners, in px: the tip at (`tx`, `ty`), pointing along the unit
    /// direction (`dx`, `dy`), `head` long and 0.8 × `head` across its base. The streamlines' heads
    /// and an annotation's arrow are this one shape.
    let arrowhead (head: float) (tx: float, ty: float) (dx: float, dy: float) : struct (float * float)[] =
        let baseX, baseY = tx - dx * head, ty - dy * head
        let nx, ny = -dy * head * 0.4, dx * head * 0.4
        [| struct (tx, ty); struct (baseX + nx, baseY + ny); struct (baseX - nx, baseY - ny) |]

    /// The grid as drawn: finite node coordinates, strictly increasing, values permuted to match.
    /// A reversed scale projects coordinates decreasing, and a log scale projects a non-positive
    /// coordinate to NaN; both are normalised here, the second by dropping that row or column.
    type private Plane =
        { X: float[]
          Y: float[]
          V: float[]
          U: float[]
          W: float[] }

        member p.Nx = p.X.Length
        member p.Ny = p.Y.Length
        member p.At (i: int) (j: int) = p.V.[j * p.X.Length + i]

    let private plane (f: FieldPayload<nd>) : Plane option =
        let xs = f.Xs |> Array.map float
        let ys = f.Ys |> Array.map float
        let nx = xs.Length

        let order (cs: float[]) =
            [| 0 .. cs.Length - 1 |] |> Array.filter (fun i -> finite cs.[i]) |> Array.sortBy (fun i -> cs.[i])

        let ix = order xs
        let iy = order ys

        let strictly (cs: float[]) (idx: int[]) =
            idx |> Array.pairwise |> Array.forall (fun (a, b) -> cs.[b] > cs.[a])

        if ix.Length < 2 || iy.Length < 2 || not (strictly xs ix) || not (strictly ys iy) then
            None
        else
            let permute (a: float[]) =
                if a.Length = 0 then [||] else [| for j in iy do for i in ix -> a.[j * nx + i] |]

            Some
                { X = ix |> Array.map (fun i -> xs.[i])
                  Y = iy |> Array.map (fun j -> ys.[j])
                  V = permute f.Values
                  U = permute f.U
                  W = permute f.W }

    /// The bilinear interpolant of `values`, laid out as the plane's nodes are, at (x, y) in
    /// `nd`; NaN outside the grid.
    let private interpolate (p: Plane) (values: float[]) (x: float) (y: float) =
        let locate (cs: float[]) (c: float) =
            if c < cs.[0] || c > cs.[cs.Length - 1] then
                ValueNone
            else
                let mutable lo = 0
                let mutable hi = cs.Length - 1

                while hi - lo > 1 do
                    let mid = (lo + hi) / 2
                    if cs.[mid] <= c then lo <- mid else hi <- mid

                ValueSome(struct (lo, (c - cs.[lo]) / (cs.[lo + 1] - cs.[lo])))

        match locate p.X x, locate p.Y y with
        | ValueSome(struct (i, fx)), ValueSome(struct (j, fy)) ->
            let at i j = values.[j * p.Nx + i]
            let a = at i j + (at (i + 1) j - at i j) * fx
            let b = at i (j + 1) + (at (i + 1) (j + 1) - at i (j + 1)) * fx
            a + (b - a) * fy
        | _ -> nan

    let private bilinearAt (p: Plane) (x: float) (y: float) = interpolate p p.V x y

    /// The part of the plane inside the plot area, 0 to 1 each way in `nd`: the nodes inside it,
    /// and a row or column of nodes on each edge the grid reaches past. A new node takes the
    /// value the plane's interpolant has there, along its row or column when it lies on one, so
    /// the part's interpolant is the plane's own wherever the plane's is defined. The plane
    /// itself when it lies inside; `None` when no area of it does. What is traced or rastered
    /// over this part is bounded by the plot area, however far a zoom takes the grid past it.
    let private visible (p: Plane) : Plane option =
        let inside (cs: float[]) = cs.[0] >= 0.0 && cs.[cs.Length - 1] <= 1.0

        // The coordinates kept along one axis, each with its node's index, or -1 for a new one.
        let cut (cs: float[]) =
            let lo, hi = max cs.[0] 0.0, min cs.[cs.Length - 1] 1.0

            if not (lo < hi) then
                None
            else
                let kept = [| for i in 0 .. cs.Length - 1 do if cs.[i] >= lo && cs.[i] <= hi then struct (cs.[i], i) |]
                let edge (c: float) = if kept |> Array.exists (fun (struct (k, _)) -> k = c) then [||] else [| struct (c, -1) |]
                Some(Array.concat [ edge lo; kept; edge hi ])

        if inside p.X && inside p.Y then
            Some p
        else
            match cut p.X, cut p.Y with
            | Some xs, Some ys ->
                // The node below a new coordinate, which lies strictly inside a cell.
                let below (cs: float[]) (c: float) = Array.findIndexBack (fun v -> v < c) cs
                let lerp (a: float) (b: float) (t: float) = a + (b - a) * t

                let sample (values: float[]) (struct (x, i)) (struct (y, j)) =
                    let at i j = values.[j * p.Nx + i]

                    if i >= 0 && j >= 0 then
                        at i j
                    elif i >= 0 then
                        let j0 = below p.Y y
                        lerp (at i j0) (at i (j0 + 1)) ((y - p.Y.[j0]) / (p.Y.[j0 + 1] - p.Y.[j0]))
                    elif j >= 0 then
                        let i0 = below p.X x
                        lerp (at i0 j) (at (i0 + 1) j) ((x - p.X.[i0]) / (p.X.[i0 + 1] - p.X.[i0]))
                    else
                        interpolate p values x y

                let resample (values: float[]) =
                    if values.Length = 0 then [||] else [| for y in ys do for x in xs -> sample values x y |]

                Some
                    { X = xs |> Array.map (fun (struct (x, _)) -> x)
                      Y = ys |> Array.map (fun (struct (y, _)) -> y)
                      V = resample p.V
                      U = resample p.U
                      W = resample p.W }
            | _ -> None

    // ---- The heatmap ----------------------------------------------------------------

    /// One pixel per node, the pixel centres on the nodes. A grid uneven in panel space — a log
    /// grid on a linear axis — is first resampled to the same counts, evenly, so that pixel
    /// centres can be on a lattice at all; one even in panel space is used node for node.
    let heatmap (colourOf: float -> Color option) (smooth: bool) (f: FieldPayload<nd>) : Node =
        match plane f with
        | None -> Scene.empty
        | Some p ->
            let even (cs: float[]) =
                let step = (cs.[cs.Length - 1] - cs.[0]) / float (cs.Length - 1)

                cs
                |> Array.mapi (fun i c -> abs (c - (cs.[0] + step * float i)))
                |> Array.forall (fun d -> d <= 1e-9 * (abs step + 1e-300))

            let lattice (cs: float[]) =
                if even cs then cs
                else Array.init cs.Length (fun i -> cs.[0] + (cs.[cs.Length - 1] - cs.[0]) * float i / float (cs.Length - 1))

            let lx = lattice p.X
            let ly = lattice p.Y
            let resampled = not (obj.ReferenceEquals(lx, p.X) && obj.ReferenceEquals(ly, p.Y))
            let nx, ny = lx.Length, ly.Length
            let rgba = Array.zeroCreate<byte> (nx * ny * 4)

            let byte01 (c: float) = byte (Num.roundEven ((max 0.0 (min 1.0 c)) * 255.0))

            for j in 0 .. ny - 1 do
                for i in 0 .. nx - 1 do
                    let v = if resampled then bilinearAt p lx.[i] ly.[j] else p.At i j

                    match (if finite v then colourOf v else None) with
                    | Some c ->
                        // Rows run top first and `nd` is y-up, so node row `ny - 1` is image row 0.
                        let k = (((ny - 1 - j) * nx) + i) * 4
                        rgba.[k] <- byte01 c.R
                        rgba.[k + 1] <- byte01 c.G
                        rgba.[k + 2] <- byte01 c.B
                        rgba.[k + 3] <- byte01 c.A
                    | None -> ()

            let halfX = (lx.[nx - 1] - lx.[0]) / float (nx - 1) / 2.0
            let halfY = (ly.[ny - 1] - ly.[0]) / float (ny - 1) / 2.0

            Raster
                { Image = { Width = nx; Height = ny; Rgba = rgba }
                  Bounds =
                    { X0 = (lx.[0] - halfX) * 1.0<nd>
                      Y0 = (ly.[0] - halfY) * 1.0<nd>
                      X1 = (lx.[nx - 1] + halfX) * 1.0<nd>
                      Y1 = (ly.[ny - 1] + halfY) * 1.0<nd> }
                  Smooth = smooth }

    // ---- Marching squares -------------------------------------------------------------
    //
    // Each cell's boundary is walked counter-clockwise — bottom, right, top, left — and every
    // edge whose two ends straddle the level holds one crossing. A crossing where the walk goes
    // from above the level to below is an *exit*, the other kind an *entry*, and a segment runs
    // from an exit to an entry. That orientation is the whole trick: every segment then has the
    // region above the level on its left, so segments chain head to tail into polylines that are
    // consistently oriented, and a band can be assembled from them (below).
    //
    // A cell with two exits and two entries is a saddle, and which pairs join is the asymptotic
    // decider's question: the bilinear interpolant's saddle value is
    // (v₀₀v₁₁ − v₁₀v₀₁)/(v₀₀ + v₁₁ − v₁₀ − v₀₁), and if it is above the level the two above
    // corners are connected through the centre. The cell-mean decider answers the same question
    // with a different number and is wrong whenever the two disagree; this one is right by
    // construction for the interpolant everything else in this file reads.

    /// A crossing's identity: which grid edge it lies on. Horizontal edges are even, vertical odd.
    let inline private hEdge (p: Plane) i j = 2 * (j * p.Nx + i)
    let inline private vEdge (p: Plane) i j = 2 * (j * p.Nx + i) + 1

    /// Where `level` crosses the edge from node value `a` at `pa` to `b` at `pb`. A `-inf` end
    /// (a missing value, for a band) puts the crossing at the finite end.
    let private crossing (level: float) (pa: float) (a: float) (pb: float) (b: float) =
        let t =
            if Double.IsNegativeInfinity a then 1.0
            elif Double.IsNegativeInfinity b then 0.0
            else (level - a) / (b - a)

        pa + (pb - pa) * t

    /// Oriented segments for one level, as (from-edge, to-edge) pairs, and every crossing's point.
    /// `missingBelow` makes a non-finite node `-inf` — below every level — which is what a band
    /// wants; without it, a cell with a missing corner holds no line at all.
    let private segments (p: Plane) (level: float) (missingBelow: bool) =
        let value i j =
            let v = p.At i j
            if finite v then v elif missingBelow then -infinity else nan

        let points = Dictionary<int, Vec2<nd>>()
        let segs = ResizeArray<struct (int * int)>()

        let pointOn (key: int) (i: int) (j: int) =
            match points.TryGetValue key with
            | true, pt -> pt
            | _ ->
                let pt =
                    if key % 2 = 0 then
                        { X = crossing level p.X.[i] (value i j) p.X.[i + 1] (value (i + 1) j) * 1.0<nd>
                          Y = p.Y.[j] * 1.0<nd> }
                    else
                        { X = p.X.[i] * 1.0<nd>
                          Y = crossing level p.Y.[j] (value i j) p.Y.[j + 1] (value i (j + 1)) * 1.0<nd> }

                points.[key] <- pt
                pt

        for j in 0 .. p.Ny - 2 do
            for i in 0 .. p.Nx - 2 do
                let v00 = value i j
                let v10 = value (i + 1) j
                let v11 = value (i + 1) (j + 1)
                let v01 = value i (j + 1)

                if not (Double.IsNaN v00 || Double.IsNaN v10 || Double.IsNaN v11 || Double.IsNaN v01) then
                    // The four corners counter-clockwise, and the edge leaving each one.
                    let corners = [| v00; v10; v11; v01 |]

                    let edges =
                        [| hEdge p i j, i, j
                           vEdge p (i + 1) j, i + 1, j
                           hEdge p i (j + 1), i, j + 1
                           vEdge p i j, i, j |]

                    let above = corners |> Array.map (fun v -> v >= level)

                    // Crossings in CCW order: (edge key, is an exit).
                    let crossings =
                        [| for k in 0..3 do
                               if above.[k] <> above.[(k + 1) % 4] then
                                   let key, ei, ej = edges.[k]
                                   pointOn key ei ej |> ignore
                                   yield struct (key, above.[k]) |]

                    match crossings.Length with
                    | 2 ->
                        let struct (a, aExit) = crossings.[0]
                        let struct (b, _) = crossings.[1]
                        segs.Add(if aExit then struct (a, b) else struct (b, a))
                    | 4 ->
                        let centreAbove =
                            if Array.exists Double.IsInfinity corners then
                                false
                            else
                                let denom = v00 + v11 - v10 - v01

                                if denom = 0.0 then
                                    (v00 + v10 + v11 + v01) / 4.0 >= level
                                else
                                    (v00 * v11 - v10 * v01) / denom >= level

                        // Exits pair with the next entry counter-clockwise when the centre is
                        // above (the above corners join through it), with the previous one
                        // when it is below (they are cut off from each other).
                        for k in 0..3 do
                            let struct (key, isExit) = crossings.[k]

                            if isExit then
                                let partner = if centreAbove then (k + 1) % 4 else (k + 3) % 4
                                let struct (other, _) = crossings.[partner]
                                segs.Add(struct (key, other))
                    | _ -> ()

        points, segs

    /// Chain oriented segments into polylines: `(points, closed)`. Open ones start and end on
    /// the grid's boundary, or at a cell a missing value removed.
    let private chains (points: Dictionary<int, Vec2<nd>>) (segs: ResizeArray<struct (int * int)>) =
        let next = Dictionary<int, int>()
        let isTarget = HashSet<int>()

        for struct (a, b) in segs do
            next.[a] <- b
            isTarget.Add b |> ignore

        let used = HashSet<int>()
        let out = ResizeArray<struct (int[] * bool)>()

        let walk (start: int) =
            let keys = ResizeArray<int>([ start ])
            used.Add start |> ignore
            let mutable cur = start
            let mutable closed = false
            let mutable go = true

            while go do
                match next.TryGetValue cur with
                | true, n when n = start ->
                    closed <- true
                    go <- false
                | true, n when not (used.Contains n) ->
                    keys.Add n
                    used.Add n |> ignore
                    cur <- n
                | true, n ->
                    // Joined a chain already walked — only possible through a degenerate
                    // crossing shared by two cells' segments. End here rather than loop.
                    keys.Add n
                    go <- false
                | _ -> go <- false

            out.Add(struct (keys.ToArray(), closed))

        // Open chains first, from the starts nothing leads into, then the rings that remain.
        for struct (a, _) in segs do
            if not (isTarget.Contains a) && not (used.Contains a) then walk a

        for struct (a, _) in segs do
            if not (used.Contains a) then walk a

        out.ToArray() |> Array.map (fun (struct (keys, closed)) -> keys |> Array.map (fun k -> points.[k]), closed)

    let private pathOf (polylines: (Vec2<nd>[] * bool)[]) (mapPt: Vec2<nd> -> Vec2<nd>) =
        [| for pts, closed in polylines do
               if pts.Length >= 2 then
                   yield MoveTo(mapPt pts.[0])

                   for k in 1 .. pts.Length - 1 do
                       yield LineTo(mapPt pts.[k])

                   if closed then yield ClosePath |]

    /// The polylines `level` traces, each oriented with the region above it on its left. For
    /// tests and for anyone who wants the geometry rather than the picture.
    let isolines (level: float) (f: FieldPayload<nd>) : (Vec2<nd>[] * bool)[] =
        match plane f with
        | None -> [||]
        | Some p ->
            let points, segs = segments p level false
            chains points segs

    /// Contour lines: one `Path` per level, stroked in the level's colour.
    let contours
        (mapPt: Vec2<nd> -> Vec2<nd>)
        (stroke: Stroke)
        (colourOf: float -> Color option)
        (levels: float[])
        (f: FieldPayload<nd>)
        : Node =
        match plane f with
        | None -> Scene.empty
        | Some p ->
            Group
                [| for level in levels do
                       let points, segs = segments p level false
                       let path = pathOf (chains points segs) mapPt

                       if path.Length > 0 then
                           let paint =
                               match colourOf level with
                               | Some c -> Solid c
                               | None -> stroke.Paint

                           yield Path(path, Some { stroke with Paint = paint }, None, NonZero) |]

    // ---- Bands ------------------------------------------------------------------------
    //
    // The region at or above a level is bounded by that level's contour and by the stretches of
    // the grid's edge where the field is above it. Because every contour is oriented with the
    // region on its left, the stretches are found by walking the boundary counter-clockwise from
    // each open chain's end to the next open chain's start — the region is on the left of a CCW
    // boundary walk too. Closed contours are rings as they stand. A band between two levels is
    // then the rings of both, filled even-odd: the region above the lower minus the region
    // above the upper, holes and islands included, with no polygon clipping at all.

    let private regionRings (p: Plane) (level: float) : Vec2<nd>[][] =
        let points, segs = segments p level true
        let polylines = chains points segs
        let x0, x1, y0, y1 = p.X.[0], p.X.[p.Nx - 1], p.Y.[0], p.Y.[p.Ny - 1]
        let w, h = x1 - x0, y1 - y0
        let perimeter = 2.0 * (w + h)

        // Arc length along the boundary, counter-clockwise from the bottom-left corner.
        let along (pt: Vec2<nd>) =
            let x, y = float pt.X, float pt.Y
            let tol = 1e-12 * (w + h)

            if abs (y - y0) <= tol && x < x1 then x - x0
            elif abs (x - x1) <= tol && y < y1 then w + (y - y0)
            elif abs (y - y1) <= tol && x > x0 then w + h + (x1 - x)
            else 2.0 * w + h + (y1 - y)

        let corners =
            [| w, { X = x1 * 1.0<nd>; Y = y0 * 1.0<nd> }
               w + h, { X = x1 * 1.0<nd>; Y = y1 * 1.0<nd> }
               2.0 * w + h, { X = x0 * 1.0<nd>; Y = y1 * 1.0<nd> }
               perimeter, { X = x0 * 1.0<nd>; Y = y0 * 1.0<nd> } |]

        let closedRings = polylines |> Array.filter snd |> Array.map fst
        let opens = polylines |> Array.filter (fun (pts, closed) -> not closed && pts.Length >= 2) |> Array.map fst

        let fromBoundary =
            if opens.Length = 0 then
                // No contour reaches the edge, so the edge is wholly above or wholly below.
                let corner = p.At 0 0

                if finite corner && corner >= level then
                    [| corners |> Array.map snd |]
                else
                    [||]
            else
                let starts = opens |> Array.map (fun c -> along c.[0])
                let visited = Array.zeroCreate opens.Length

                [| for first in 0 .. opens.Length - 1 do
                       if not visited.[first] then
                           let ring = ResizeArray<Vec2<nd>>()
                           let mutable cur = first
                           let mutable go = true

                           while go do
                               visited.[cur] <- true
                               ring.AddRange opens.[cur]
                               let e = along (Array.last opens.[cur])

                               // The next start counter-clockwise from `e`, wrapping round.
                               let dist (s: float) =
                                   let d = s - e
                                   if d >= 0.0 then d else d + perimeter

                               let nxt = [| 0 .. opens.Length - 1 |] |> Array.minBy (fun k -> dist starts.[k])
                               let span = dist starts.[nxt]

                               // Corners passed on the way, in order.
                               for (_, c) in
                                   corners
                                   |> Array.collect (fun (s, c) -> [| s, c; s + perimeter, c |])
                                   |> Array.filter (fun (s, _) -> s - e > 0.0 && s - e < span)
                                   |> Array.sortBy fst do
                                   ring.Add c

                               if visited.[nxt] then go <- false else cur <- nxt

                           yield ring.ToArray() |]

        Array.append closedRings fromBoundary

    /// Filled bands between consecutive levels, each one `Path` filled even-odd in the colour of
    /// its midpoint value.
    let bands
        (mapPt: Vec2<nd> -> Vec2<nd>)
        (opacity: float)
        (colourOf: float -> Color option)
        (levels: float[])
        (f: FieldPayload<nd>)
        : Node =
        match plane f with
        | None -> Scene.empty
        | Some p ->
            let sorted = levels |> Array.filter finite |> Array.distinct |> Array.sort
            let rings = sorted |> Array.map (regionRings p)

            Group
                [| for k in 0 .. sorted.Length - 2 do
                       match colourOf ((sorted.[k] + sorted.[k + 1]) / 2.0) with
                       | None -> ()
                       | Some c ->
                           let path = pathOf (Array.append rings.[k] rings.[k + 1] |> Array.map (fun r -> r, true)) mapPt

                           if path.Length > 0 then
                               yield Path(path, None, Some(Solid(Color.withAlpha (c.A * opacity) c)), EvenOdd) |]

    // ---- Arrows -----------------------------------------------------------------------
    //
    // An arrow is centred on its node and points along the vector's direction *on the panel*:
    // the components arrive already multiplied by the scales' derivatives (`Geom.project`), and
    // here by the plot area's size in pixels, so an arrow is tangent to the streamline through
    // its node on any axis and any aspect ratio. Its length is the magnitude times the quiver's
    // own scale — **not** the axes', which are in a different unit — and that scale is chosen on
    // the panel so the longest arrow is 0.9 of the grid's pitch in pixels: long enough to read,
    // and two neighbours pointing at each other do not touch.

    /// The smallest spacing between neighbouring nodes along either axis, in pixels.
    let private pitch (p: Plane) (plot: Extent<px>) =
        let along (cs: float[]) (size: float) =
            cs |> Array.pairwise |> Array.map (fun (a, b) -> (b - a) * size) |> Array.min

        min (along p.X (float plot.Width)) (along p.Y (float plot.Height))

    /// One arrow in pixels: shaft and head, `d` the unit direction.
    let private arrow (cx: float) (cy: float) (dx: float) (dy: float) (length: float) (strokeWidth: float) =
        let head = min (0.4 * length) (5.0 + 1.5 * strokeWidth)
        let half = 0.45 * head
        let tx, ty = cx - dx * length / 2.0, cy - dy * length / 2.0
        let px, py = cx + dx * length / 2.0, cy + dy * length / 2.0
        let bx, by = px - dx * head, py - dy * head
        struct (tx, ty, bx, by, px, py, -dy * half, dx * half)

    let private arrowSegs (toNd: float -> float -> Vec2<nd>) (struct (tx, ty, bx, by, px, py, nx, ny)) =
        [| MoveTo(toNd tx ty)
           LineTo(toNd bx by)
           MoveTo(toNd px py)
           LineTo(toNd (bx + nx) (by + ny))
           LineTo(toNd (bx - nx) (by - ny))
           ClosePath |]

    /// The arrows, one `Path` per colour, and — for a quiver with a key — the key in the plot
    /// area's top-right corner on a panel-coloured box, so it reads over the arrows beneath it.
    let arrows
        (mapPt: Vec2<nd> -> Vec2<nd>)
        (measurer: ITextMeasurer)
        (plot: Extent<px>)
        (background: Color)
        (stroke: Stroke)
        (font: FontSpec)
        (colourOf: float -> Color option)
        (directionOnly: bool)
        (key: QuiverKey option)
        (f: FieldPayload<nd>)
        : Node =
        match plane f with
        | Some p when p.U.Length > 0 ->
            let w, h = float plot.Width, float plot.Height
            let toNd (x: float) (y: float) : Vec2<nd> = mapPt { X = x / w * 1.0<nd>; Y = y / h * 1.0<nd> }
            let span = pitch p plot

            let largest =
                match key with
                | Some k when k.Largest > 0.0 -> k.Largest
                | _ -> p.V |> Array.fold (fun acc v -> if finite v && v > acc then v else acc) 0.0

            // Pixels per unit of magnitude, or `None` when nothing has a length.
            let perUnit = if directionOnly || largest <= 0.0 then None else Some(0.9 * span / largest)

            let groups = Dictionary<Color, ResizeArray<PathSeg>>()

            for j in 0 .. p.Ny - 1 do
                for i in 0 .. p.Nx - 1 do
                    let k = j * p.Nx + i
                    let ux, uy = p.U.[k] * w, p.W.[k] * h
                    let norm = sqrt (ux * ux + uy * uy)
                    let v = p.V.[k]

                    let length =
                        match perUnit with
                        | _ when directionOnly -> 0.7 * span
                        | Some s when finite v -> v * s
                        | _ -> 0.0

                    if finite norm && norm > 0.0 && length >= 0.5 then
                        match colourOf v with
                        | Some c ->
                            let segs =
                                match groups.TryGetValue c with
                                | true, g -> g
                                | _ ->
                                    let g = ResizeArray<PathSeg>()
                                    groups.[c] <- g
                                    g

                            segs.AddRange(arrowSegs toNd (arrow (p.X.[i] * w) (p.Y.[j] * h) (ux / norm) (uy / norm) length (float stroke.Width)))
                        | None -> ()

            let drawn =
                [| for KeyValue(c, segs) in groups do
                       yield Path(segs.ToArray(), Some { stroke with Paint = Solid c }, Some(Solid c), NonZero) |]

            let keyNode =
                match key, perUnit with
                | Some k, Some s when k.Value > 0.0 ->
                    let length = k.Value * s
                    let metrics = measurer.Measure font k.Label
                    let pad, gap, margin = 5.0, 6.0, 6.0
                    let textH = float (metrics.Ascent + metrics.Descent)
                    let boxW = pad + float metrics.Advance + gap + length + pad
                    let boxH = pad + max textH 8.0 + pad
                    let x1, y1 = w - margin, h - margin
                    let x0, y0 = x1 - boxW, y1 - boxH
                    let cy = (y0 + y1) / 2.0
                    let tailX = x1 - pad - length

                    Annotated(
                        { Role = RoleLegend
                          Label = "quiver key"
                          Data = [||] },
                        Group
                            [| Path(
                                   [| MoveTo(toNd x0 y0); LineTo(toNd x1 y0); LineTo(toNd x1 y1); LineTo(toNd x0 y1); ClosePath |],
                                   None,
                                   Some(Solid(Color.withAlpha 0.85 background)),
                                   NonZero
                               )
                               Path(arrowSegs toNd (arrow (tailX + length / 2.0) cy 1.0 0.0 length (float stroke.Width)), Some stroke, Some stroke.Paint, NonZero)
                               Text
                                   { Text = k.Label
                                     Tex = None
                                     At = toNd (tailX - gap) cy
                                     Font = font
                                     Paint = stroke.Paint
                                     Anchor = AnchorEnd
                                     Baseline = BaselineMiddle
                                     Rotation = 0.0<rad> } |]
                    )
                | _ -> Scene.empty

            Group(Array.append drawn [| keyNode |])
        | _ -> Scene.empty

    // ---- Streamlines --------------------------------------------------------------------
    //
    // **Integrated in pixels, along the unit direction.** Arc length on a phase space adds rad²
    // to rad²/s², which is not a quantity, and on a log axis it is not a visual one either: the
    // only length a set of streamlines can be evenly spaced in is a length on the page. So the
    // integrator follows dX/ds = v̂(X), where v is the field's direction on the panel — the
    // components `Geom.project` has already multiplied by the scales' derivatives, times the
    // plot area's size — and s is arc length in pixels. Streamlines do not depend on how they are
    // parameterised, so these are the field's own curves, on any axis.
    //
    // **Evenly spaced by Jobard and Lefer's method** ("Creating evenly-spaced streamlines of
    // arbitrary density", 1997). A line is integrated both ways from a seed until it comes within
    // `dTest = separation / 2` of a point of another line, leaves the grid or the plot area,
    // meets a missing value, reaches a fixed point, or turns back on itself. New seeds are
    // proposed `separation` to either side of every finished line, and one is accepted only if
    // nothing is closer than `separation`. Seeds are tried in a fixed order — never at random —
    // because a film of a changing field whose lines are reseeded at random flickers.
    //
    // **Only the grid's part in the plot area is traced.** Zoomed in k times, the whole grid
    // would hold k² times the lines, all but the plot area's share drawn only to be clipped.
    //
    // **A line that comes back to where it started is closed**, which is what a centre's orbits
    // are. A line's own points near its start are exempt from the separation test for that
    // reason; its own points near its head are exempt because they are where it just was.

    /// The field's direction on the panel at a point in pixels, and its speed there; `ValueNone`
    /// outside the grid or at a missing value.
    let private velocityAt (p: Plane) (w: float) (h: float) (x: float) (y: float) =
        let u = interpolate p p.U (x / w) (y / h) * w
        let v = interpolate p p.W (x / w) (y / h) * h

        if finite u && finite v then ValueSome(struct (u, v)) else ValueNone

    /// The lines as polylines in pixels, each with whether it closed, in the order they were
    /// found. Public for the tests; `streamlines` draws them.
    let streamlinePolylines (plot: Extent<px>) (separation: float<px>) (f: FieldPayload<nd>) : (struct (float * float)[] * bool)[] =
        match plane f |> Option.bind visible with
        | Some p when p.U.Length > 0 ->
            let w, h = float plot.Width, float plot.Height
            let dSep = max 2.0 (float separation)
            let dTest = dSep / 2.0
            let step = min 2.0 (dSep / 8.0)
            let x0, x1 = p.X.[0] * w, p.X.[p.Nx - 1] * w
            let y0, y1 = p.Y.[0] * h, p.Y.[p.Ny - 1] * h
            let maxSteps = int (8.0 * (x1 - x0 + y1 - y0) / step)

            let fastest =
                Array.init p.U.Length (fun k -> sqrt ((p.U.[k] * w) ** 2.0 + (p.W.[k] * h) ** 2.0))
                |> Array.filter finite
                |> Array.fold max 0.0

            let slowest = 1e-6 * fastest

            let direction (x: float) (y: float) =
                if x < x0 || x > x1 || y < y0 || y > y1 then
                    ValueNone
                else
                    match velocityAt p w h x y with
                    | ValueSome(struct (u, v)) ->
                        let speed = sqrt (u * u + v * v)
                        if speed > slowest then ValueSome(struct (u / speed, v / speed)) else ValueNone
                    | ValueNone -> ValueNone

            // Every accepted point, hashed on a dSep lattice, tagged with its line and its index
            // along that line.
            let cells = Dictionary<struct (int * int), ResizeArray<struct (float * float * int * int)>>()
            let cellOf (x: float) (y: float) = struct (int (floor (x / dSep)), int (floor (y / dSep)))

            let add (line: int) (index: int) (x: float) (y: float) =
                let c = cellOf x y

                let bucket =
                    match cells.TryGetValue c with
                    | true, b -> b
                    | _ ->
                        let b = ResizeArray()
                        cells.[c] <- b
                        b

                bucket.Add(struct (x, y, line, index))

            /// Whether anything other than `exempt` lies within `radius` of (x, y).
            let crowded (x: float) (y: float) (radius: float) (exempt: int -> int -> bool) =
                let struct (cx, cy) = cellOf x y
                let r2 = radius * radius
                let mutable hit = false

                for i in cx - 1 .. cx + 1 do
                    for j in cy - 1 .. cy + 1 do
                        if not hit then
                            match cells.TryGetValue(struct (i, j)) with
                            | true, b ->
                                for struct (px, py, line, index) in b do
                                    if not hit && not (exempt line index) then
                                        let dx, dy = px - x, py - y
                                        if dx * dx + dy * dy < r2 then hit <- true
                            | _ -> ()

                hit

            let rk4 (x: float) (y: float) (sign: float) =
                match direction x y with
                | ValueNone -> ValueNone
                | ValueSome(struct (ax, ay)) ->
                    let s = sign * step

                    match direction (x + s / 2.0 * ax) (y + s / 2.0 * ay) with
                    | ValueNone -> ValueNone
                    | ValueSome(struct (bx, by)) ->
                        match direction (x + s / 2.0 * bx) (y + s / 2.0 * by) with
                        | ValueNone -> ValueNone
                        | ValueSome(struct (cx, cy)) ->
                            match direction (x + s * cx) (y + s * cy) with
                            | ValueNone -> ValueNone
                            | ValueSome(struct (dx, dy)) ->
                                // A direction that reverses within one step is a singularity the
                                // step straddles, not a curve to follow.
                                if ax * bx + ay * by < 0.0 || bx * cx + by * cy < 0.0 || cx * dx + cy * dy < 0.0 then
                                    ValueNone
                                else
                                    ValueSome(
                                        struct (x + s / 6.0 * (ax + 2.0 * bx + 2.0 * cx + dx),
                                                y + s / 6.0 * (ay + 2.0 * by + 2.0 * cy + dy))
                                    )

            let near = int (ceil (2.0 * dSep / step))

            /// Trace from a seed in one direction. Indices run outward from the seed: forward
            /// positive, backward negative. Returns the points (seed excluded) and whether the
            /// line came back to the seed.
            let trace (line: int) (sx: float) (sy: float) (sign: float) =
                let pts = ResizeArray<struct (float * float)>()
                let mutable x, y = sx, sy
                let mutable go = true
                let mutable closed = false
                let mutable n = 0

                while go && n < maxSteps do
                    match rk4 x y sign with
                    | ValueNone -> go <- false
                    | ValueSome(struct (nx, ny)) ->
                        n <- n + 1
                        let index = if sign > 0.0 then n else -n

                        let exempt (l: int) (i: int) =
                            l = line && (abs (i - index) < near || abs i < near)

                        if n > near && (nx - sx) ** 2.0 + (ny - sy) ** 2.0 < (1.5 * step) ** 2.0 then
                            // Kept, so the ring's closing segment is under a step and a half
                            // rather than spanning the gap from the point before.
                            pts.Add(struct (nx, ny))
                            closed <- true
                            go <- false
                        elif crowded nx ny dTest exempt then
                            go <- false
                        else
                            pts.Add(struct (nx, ny))
                            add line index nx ny
                            x <- nx
                            y <- ny

                pts, closed

            let lines = ResizeArray<struct (struct (float * float)[] * bool)>()
            let queue = Queue<struct (float * float)[]>()

            let tryLine (sx: float) (sy: float) =
                if (direction sx sy).IsSome && not (crowded sx sy dSep (fun _ _ -> false)) then
                    let id = lines.Count
                    add id 0 sx sy
                    let ahead, closed = trace id sx sy 1.0

                    let pts =
                        if closed then
                            Array.append [| struct (sx, sy) |] (ahead.ToArray())
                        else
                            let behind, _ = trace id sx sy -1.0
                            let back = behind.ToArray() |> Array.rev
                            Array.concat [ back; [| struct (sx, sy) |]; ahead.ToArray() ]

                    if pts.Length >= 2 then
                        lines.Add(struct (pts, closed))
                        queue.Enqueue pts

            /// Seeds `dSep` to either side of a line, every half `dSep` along it.
            let propose (pts: struct (float * float)[]) =
                let every = max 1 (int (dSep / 2.0 / step))

                for k in 0 .. every .. pts.Length - 2 do
                    let struct (ax, ay) = pts.[k]
                    let struct (bx, by) = pts.[k + 1]
                    let dx, dy = bx - ax, by - ay
                    let len = sqrt (dx * dx + dy * dy)

                    if len > 0.0 then
                        let nx, ny = -dy / len * dSep, dx / len * dSep
                        tryLine (ax + nx) (ay + ny)
                        tryLine (ax - nx) (ay - ny)

            // The first seed is the centre of the grid's part in the plot area; then every line's
            // neighbours; then, for regions no line reached, a lattice of candidates in row order.
            let drain () =
                while queue.Count > 0 do
                    propose (queue.Dequeue())

            tryLine ((x0 + x1) / 2.0) ((y0 + y1) / 2.0)
            drain ()

            let mutable gy = y0 + dSep / 2.0

            while gy < y1 do
                let mutable gx = x0 + dSep / 2.0

                while gx < x1 do
                    tryLine gx gy
                    drain ()
                    gx <- gx + dSep

                gy <- gy + dSep

            lines.ToArray() |> Array.map (fun (struct (pts, closed)) -> pts, closed)
        | _ -> [||]

    /// The streamlines, one `Path` per colour, with filled arrowheads along each line pointing
    /// downstream: one at the middle of a short line, one every three separations along a long
    /// one.
    let streamlines
        (mapPt: Vec2<nd> -> Vec2<nd>)
        (plot: Extent<px>)
        (stroke: Stroke)
        (colourOf: float -> Color option)
        (separation: float<px>)
        (f: FieldPayload<nd>)
        : Node =
        match plane f with
        | Some p when p.U.Length > 0 ->
            let w, h = float plot.Width, float plot.Height
            let toNd (x: float) (y: float) : Vec2<nd> = mapPt { X = x / w * 1.0<nd>; Y = y / h * 1.0<nd> }
            let dSep = max 2.0 (float separation)
            let head = arrowheadLength stroke.Width
            let groups = Dictionary<Color, ResizeArray<PathSeg>>()
            let heads = Dictionary<Color, ResizeArray<PathSeg>>()

            let into (d: Dictionary<Color, ResizeArray<PathSeg>>) (c: Color) =
                match d.TryGetValue c with
                | true, g -> g
                | _ ->
                    let g = ResizeArray<PathSeg>()
                    d.[c] <- g
                    g

            for pts, closed in streamlinePolylines plot separation f do
                let mean =
                    pts
                    |> Array.map (fun (struct (x, y)) -> bilinearAt p (x / w) (y / h))
                    |> Array.filter finite
                    |> fun vs -> if vs.Length = 0 then nan else Array.average vs

                match colourOf mean with
                | None -> ()
                | Some c ->
                    let segs = into groups c
                    let struct (sx, sy) = pts.[0]
                    segs.Add(MoveTo(toNd sx sy))

                    for k in 1 .. pts.Length - 1 do
                        let struct (x, y) = pts.[k]
                        segs.Add(LineTo(toNd x y))

                    if closed then segs.Add ClosePath

                    // Arc length along the line, for placing the heads.
                    let arc = Array.zeroCreate pts.Length

                    for k in 1 .. pts.Length - 1 do
                        let struct (ax, ay) = pts.[k - 1]
                        let struct (bx, by) = pts.[k]
                        arc.[k] <- arc.[k - 1] + sqrt ((bx - ax) ** 2.0 + (by - ay) ** 2.0)

                    let total = arc.[arc.Length - 1]

                    if total > 2.0 * head then
                        let spacing = 3.0 * dSep

                        let marks =
                            if total < 2.0 * spacing then [ total / 2.0 ]
                            else [ spacing / 2.0 .. spacing .. total - spacing / 2.0 ]

                        let hs = into heads c

                        for m in marks do
                            let k = arc |> Array.findIndex (fun a -> a >= m) |> max 1
                            let struct (ax, ay) = pts.[k - 1]
                            let struct (bx, by) = pts.[k]
                            let len = sqrt ((bx - ax) ** 2.0 + (by - ay) ** 2.0)

                            if len > 0.0 then
                                let dx, dy = (bx - ax) / len, (by - ay) / len
                                let tx, ty = ax + dx * head / 2.0, ay + dy * head / 2.0
                                let corners = arrowhead head (tx, ty) (dx, dy)
                                let struct (x0, y0) = corners.[0]
                                let struct (x1, y1) = corners.[1]
                                let struct (x2, y2) = corners.[2]

                                hs.AddRange [| MoveTo(toNd x0 y0); LineTo(toNd x1 y1); LineTo(toNd x2 y2); ClosePath |]

            Group
                [| for KeyValue(c, segs) in groups do
                       yield Path(segs.ToArray(), Some { stroke with Paint = Solid c }, None, NonZero)
                   for KeyValue(c, segs) in heads do
                       yield Path(segs.ToArray(), None, Some(Solid c), NonZero) |]
        | _ -> Scene.empty

    // ---- Line-integral convolution ------------------------------------------------------
    //
    // **What it is.** Every pixel takes the mean of a white-noise texture along the streamline
    // through it, `Length` pixels long on the page and centred on the pixel (Cabral and Leedom,
    // 1993). Pixels on one streamline average nearly the same noise and come out nearly the same
    // grey; pixels on neighbouring streamlines average independent noise and do not. The texture
    // is the field's streamlines, at every pixel rather than at a chosen few.
    //
    // **Fast LIC** (Stalling and Hege, 1995). Computed pixel by pixel, a kernel `n` samples long
    // costs `n` integration steps per pixel. Here a streamline is traced once, much longer than
    // the kernel, and the kernel slides along it as a running sum, so every sample on it is a
    // finished pixel for the cost of one step. Pixels are visited in row order and a pixel some
    // earlier line already crossed is not a seed; the hits a pixel collects are averaged. The
    // trace runs in raster pixels, half a pixel per step, by the same RK4 on the unit direction
    // as the streamlines, with the same stopping rules: the grid's edge, a missing value, a fixed
    // point, a direction that reverses within a step.
    //
    // **A truncated kernel is used only where the line really ends.** Near a trace's far end the
    // window runs out because the trace was cut short, not because the streamline stops, and those
    // samples are not deposited — another seed covers them with a whole window. Where the line
    // stops at the edge or a fixed point, the window is what there is.
    //
    // **The noise is the library's one generator**, one uniform draw per raster pixel in row order
    // from the stated seed. Nothing else is random, and the tracing runs in row order on one
    // thread, so a seed, a field and a plot size name one image on every runtime: the arithmetic is
    // +, ×, ÷ and √, which IEEE 754 rounds the same everywhere.
    //
    // **Only the per-pixel passes after the tracing run in parallel**. Fast LIC
    // seeds a line only at a pixel no earlier line crossed, so the lines traced depend on the
    // order they are traced in, and tracing in parallel would change the image a seed names. The
    // intensity and the colouring each write one pixel from values nothing else writes, so they
    // are parallel without the thread count reaching a byte.
    //
    // **A backend may convolve instead**. When the compile measurer is an `ILicConvolver`
    // that answers, its intensities replace the traced ones: the GPU renderer's computes each
    // pixel's own window, the exact convolution at its centre, which correlates 0.90 with fast
    // LIC's averaged windows on the same noise (measured). The stretch and
    // the colouring below are the same either way. Any other measurer leaves the image as it was,
    // to the byte.
    //
    // **Contrast.** A mean of many draws has a small spread around one half, so the texture is
    // stretched: two standard deviations either side of its mean become black and white. The
    // brightness then scales the colour from 0.2 to 1 of itself, so the darkest strokes still
    // carry the colour a colorbar reads.

    /// The image and the panel rectangle it covers, which is the grid's extent inside the plot
    /// area; `None` for a payload with no vector components, or a grid wholly outside the plot
    /// area. Public for the tests; `lic` draws it.
    let licImage (measurer: ITextMeasurer) (plot: Extent<px>) (colourOf: float -> Color option) (options: LicOptions) (f: FieldPayload<nd>) : (Image * Rect<nd>) option =
        match plane f |> Option.bind visible with
        | Some p when p.U.Length > 0 ->
            let gx0, gx1 = p.X.[0], p.X.[p.Nx - 1]
            let gy0, gy1 = p.Y.[0], p.Y.[p.Ny - 1]
            let os = float (max 1 options.Oversample)
            let rw = max 1 (int (Num.roundEven ((gx1 - gx0) * float plot.Width * os)))
            let rh = max 1 (int (Num.roundEven ((gy1 - gy0) * float plot.Height * os)))

            // Raster coordinates to `nd`, and `nd` components to raster pixels per unit time.
            let ndX (rx: float) = gx0 + rx / float rw * (gx1 - gx0)
            let ndY (ry: float) = gy0 + ry / float rh * (gy1 - gy0)
            let kx = float rw / (gx1 - gx0)
            let ky = float rh / (gy1 - gy0)

            let fastest =
                Array.init p.U.Length (fun k -> sqrt ((p.U.[k] * kx) ** 2.0 + (p.W.[k] * ky) ** 2.0))
                |> Array.filter finite
                |> Array.fold max 0.0

            let slowest = 1e-6 * fastest

            let direction (rx: float) (ry: float) =
                if rx < 0.0 || rx > float rw || ry < 0.0 || ry > float rh then
                    ValueNone
                else
                    let x, y = ndX rx, ndY ry
                    let u = interpolate p p.U x y * kx
                    let v = interpolate p p.W x y * ky
                    let speed = sqrt (u * u + v * v)

                    if finite speed && speed > slowest then ValueSome(struct (u / speed, v / speed)) else ValueNone

            let step = 0.5

            let rk4 (x: float) (y: float) (sign: float) =
                match direction x y with
                | ValueNone -> ValueNone
                | ValueSome(struct (ax, ay)) ->
                    let s = sign * step

                    match direction (x + s / 2.0 * ax) (y + s / 2.0 * ay) with
                    | ValueNone -> ValueNone
                    | ValueSome(struct (bx, by)) ->
                        match direction (x + s / 2.0 * bx) (y + s / 2.0 * by) with
                        | ValueNone -> ValueNone
                        | ValueSome(struct (cx, cy)) ->
                            match direction (x + s * cx) (y + s * cy) with
                            | ValueNone -> ValueNone
                            | ValueSome(struct (dx, dy)) ->
                                if ax * bx + ay * by < 0.0 || bx * cx + by * cy < 0.0 || cx * dx + cy * dy < 0.0 then
                                    ValueNone
                                else
                                    ValueSome(
                                        struct (x + s / 6.0 * (ax + 2.0 * bx + 2.0 * cx + dx),
                                                y + s / 6.0 * (ay + 2.0 * by + 2.0 * cy + dy))
                                    )

            let noise, _ = Rng.units (rw * rh) (Rng.seeded options.Seed)

            let pixelOf (x: float) (y: float) =
                let i = min (rw - 1) (max 0 (int (floor x)))
                let j = min (rh - 1) (max 0 (int (floor y)))
                j * rw + i

            // Half the kernel, in samples; a trace runs this many times further each way, so
            // all but the last `n` samples at a cut end are deposited.
            let n = max 1 (int (Num.roundEven (float options.Length * os / 2.0 / step)))
            let reach = 6 * n
            let ownNoise (k: int) =
                let x, y = ndX (float (k % rw) + 0.5), ndY (float (k / rw) + 0.5)
                if finite (interpolate p p.U x y) && finite (interpolate p p.W x y) then noise.[k] else nan

            let convolved: float[] voption =
#if FABLE_COMPILER
                // Fable cannot test for an interface, and nothing in a page convolves on a GPU.
                ignore measurer
                ValueNone
#else
                match measurer with
                | :? ILicConvolver as c ->
                    c.Convolve
                        { Xs = p.X
                          Ys = p.Y
                          U = p.U |> Array.map (fun u -> u * kx)
                          W = p.W |> Array.map (fun w -> w * ky)
                          Width = rw
                          Height = rh
                          Noise = noise
                          HalfLength = n
                          Step = step
                          Slowest = slowest }
                | _ -> ValueNone
#endif

            let intensity =
                match convolved with
                | ValueSome c -> Array.Parallel.init (rw * rh) (fun k -> if finite c.[k] then c.[k] else ownNoise k)
                | ValueNone ->
                    let sum = Array.zeroCreate<float> (rw * rh)
                    let hits = Array.zeroCreate<int> (rw * rh)

                    // One side of a trace: the points after the seed, and whether the line ended there
                    // rather than being cut at `reach`.
                    let side (sx: float) (sy: float) (sign: float) =
                        let pts = ResizeArray<struct (float * float)>()
                        let mutable x, y = sx, sy
                        let mutable ended = false

                        while not ended && pts.Count < reach do
                            match rk4 x y sign with
                            | ValueSome(struct (nx, ny)) ->
                                pts.Add(struct (nx, ny))
                                x <- nx
                                y <- ny
                            | ValueNone -> ended <- true

                        pts, ended

                    for j in 0 .. rh - 1 do
                        for i in 0 .. rw - 1 do
                            let k = j * rw + i
                            let sx, sy = float i + 0.5, float j + 0.5

                            if hits.[k] = 0 && (direction sx sy).IsSome then
                                let ahead, aheadEnded = side sx sy 1.0
                                let behind, behindEnded = side sx sy -1.0

                                let line =
                                    Array.concat [ behind.ToArray() |> Array.rev; [| struct (sx, sy) |]; ahead.ToArray() ]

                                let cells = line |> Array.map (fun (struct (x, y)) -> pixelOf x y)
                                let prefix = Array.zeroCreate (cells.Length + 1)

                                for m in 0 .. cells.Length - 1 do
                                    prefix.[m + 1] <- prefix.[m] + noise.[cells.[m]]

                                let last = cells.Length - 1

                                for m in 0 .. last do
                                    let lo = m - n
                                    let hi = m + n

                                    if (lo >= 0 || behindEnded) && (hi <= last || aheadEnded) then
                                        let a = max 0 lo
                                        let b = min last hi
                                        sum.[cells.[m]] <- sum.[cells.[m]] + (prefix.[b + 1] - prefix.[a]) / float (b - a + 1)
                                        hits.[cells.[m]] <- hits.[cells.[m]] + 1

                    // A pixel whose field is finite but which no line crossed — a fixed point, or a
                    // direction that reverses at its centre — keeps its own noise.
                    Array.Parallel.init (rw * rh) (fun k -> if hits.[k] > 0 then sum.[k] / float hits.[k] else ownNoise k)

            let present = intensity |> Array.filter finite
            let mean = if present.Length = 0 then 0.5 else Array.average present

            let spread =
                if present.Length = 0 then 0.0
                else sqrt (present |> Array.averageBy (fun v -> (v - mean) * (v - mean)))

            let rgba = Array.zeroCreate<byte> (rw * rh * 4)
            let byte01 (c: float) = byte (Num.roundEven ((max 0.0 (min 1.0 c)) * 255.0))

            let row (j: int) =
                for i in 0 .. rw - 1 do
                    let k = j * rw + i
                    let t = intensity.[k]

                    if finite t then
                        let v = bilinearAt p (ndX (float i + 0.5)) (ndY (float j + 0.5))

                        match (if finite v then colourOf v else None) with
                        | Some c ->
                            let stretched = if spread > 0.0 then 0.5 + (t - mean) / (4.0 * spread) else 0.5
                            let b = 0.2 + 0.8 * max 0.0 (min 1.0 stretched)
                            // Rows run top first and raster `y` is up.
                            let o = (((rh - 1 - j) * rw) + i) * 4
                            rgba.[o] <- byte01 (c.R * b)
                            rgba.[o + 1] <- byte01 (c.G * b)
                            rgba.[o + 2] <- byte01 (c.B * b)
                            rgba.[o + 3] <- byte01 c.A
                        | None -> ()

            // Each row writes its own pixels and reads nothing another row writes, so this runs in
            // parallel without the thread count reaching the image.
            Array.Parallel.iter row [| 0 .. rh - 1 |]

            Some(
                { Width = rw; Height = rh; Rgba = rgba },
                { X0 = gx0 * 1.0<nd>
                  Y0 = gy0 * 1.0<nd>
                  X1 = gx1 * 1.0<nd>
                  Y1 = gy1 * 1.0<nd> }
            )
        | _ -> None

    /// The convolution as a `Raster` over the grid's extent, interpolated by the backend where it
    /// does not land on whole device pixels.
    let lic (measurer: ITextMeasurer) (plot: Extent<px>) (colourOf: float -> Color option) (options: LicOptions) (f: FieldPayload<nd>) : Node =
        match licImage measurer plot colourOf options f with
        | Some(image, bounds) ->
            Raster
                { Image = image
                  Bounds = bounds
                  Smooth = true }
        | None -> Scene.empty
