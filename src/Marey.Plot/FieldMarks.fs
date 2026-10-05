namespace Marey

open System
open System.Collections.Generic

// ======================================================================================
// D86 / D88 / D91 — WHAT A FIELD LAYER DRAWS, COMPUTED ON THE PANEL.
//
// A field layer carries a grid in data space and is drawn at compile time, after `Geom.project`
// has put its node coordinates on the panel through the panel's final scales (D97). Everything
// in this file therefore works in `nd`, on a rectilinear grid whose nodes are where the panel
// says they are — which is what makes a heatmap, its contours and its bands line up with each
// other and with every other layer, on a linear axis and a log one alike.
//
// **Interpolation is in panel space.** Between nodes, a value is read bilinearly in `nd`, not in
// data units. On a linear axis the two are the same. On a log axis the panel-space reading is
// the one a reader sees — a straight contour segment across a cell is straight on the page —
// and it is the space a grid sampled with `Grid.log` is uniform in.
//
// **Missing values** (D52). A node that is not finite is not drawn: it is a transparent pixel,
// no contour line crosses a cell that has one, and every band treats it as below every level, so
// no band covers it. It was counted when the grid was built.
// ======================================================================================

/// What a field layer draws (D86).
type FieldMark =
    /// The scalar as an image, one pixel per node, coloured by the layer's colour channel.
    /// `smooth` interpolates between nodes; otherwise each node is a flat cell (D91).
    | FieldHeatmap of smooth: bool
    /// Iso-lines at the given values, raw in the field's unit (D88).
    | FieldContours of levels: float[]
    /// Filled bands between consecutive levels, each coloured at its midpoint value (D88).
    | FieldBands of levels: float[]

/// A scalar grid with its measures spent: node coordinates in `'p` — data space on a layer,
/// `nd` once projected — and values raw in the field's unit.
type FieldPayload<[<Measure>] 'p> =
    { Xs: float<'p>[]
      Ys: float<'p>[]
      Values: float[] }

[<RequireQualifiedAccess>]
module FieldMarks =

    let private finite (v: float) = not (Double.IsNaN v || Double.IsInfinity v)

    /// The grid as drawn: finite node coordinates, strictly increasing, values permuted to match.
    /// A reversed scale projects coordinates decreasing, and a log scale projects a non-positive
    /// coordinate to NaN; both are normalised here, the second by dropping that row or column.
    type private Plane =
        { X: float[]
          Y: float[]
          V: float[] }

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
            Some
                { X = ix |> Array.map (fun i -> xs.[i])
                  Y = iy |> Array.map (fun j -> ys.[j])
                  V = [| for j in iy do for i in ix -> f.Values.[j * nx + i] |] }

    let private bilinearAt (p: Plane) (x: float) (y: float) =
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
            let a = p.At i j + (p.At (i + 1) j - p.At i j) * fx
            let b = p.At i (j + 1) + (p.At (i + 1) (j + 1) - p.At i (j + 1)) * fx
            a + (b - a) * fy
        | _ -> nan

    // ---- The heatmap (D91) ----------------------------------------------------------------

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

            let byte01 (c: float) = byte (Math.Round((max 0.0 (min 1.0 c)) * 255.0))

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

    // ---- Marching squares (D88) -------------------------------------------------------------
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

    // ---- Bands (D88) ------------------------------------------------------------------------
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
