namespace Marey

open System
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// HARD DISCS.
//
// Particle–particle collisions are pairwise events, resolved inside a step in time order: the
// earliest contact first, then the rest re-tested. Between contacts every disc moves in a
// straight line, so a contact time is the root of a quadratic and is exact to rounding, not
// located to a tolerance; an elastic gas conserves its momentum and its kinetic energy to
// rounding, which is what the gate holds it to.
//
// Above a stated number of discs the pairs are found through a uniform grid whose cells are as
// wide as two of the largest radius plus twice the farthest any disc can travel in what is left
// of the step. The grid is rebuilt after every contact, because a contact changes the speeds the
// margin was computed from; a pair the grid cannot see could not have touched.
//
// A step resolves at most a stated number of contacts. Past that the discs fly on without
// colliding for the rest of the step, and the step is counted as capped: what is not done is
// counted. Hard discs only: no friction, no rotation, no soft contact. Two dimensions, as
// everything else here.
// ======================================================================================

/// The box discs move in.
[<RequireQualifiedAccess>]
type Box<[<Measure>] 'q> =
    /// No walls.
    | Open
    /// Walls at the four sides, which reflect a disc with the collisions' restitution.
    | Walls of xMin: float<'q> * xMax: float<'q> * yMin: float<'q> * yMax: float<'q>
    /// A torus from `(0, 0)` to `(width, height)`: a disc leaving one side enters the other, and
    /// distances are taken to the nearest image.
    | Periodic of width: float<'q> * height: float<'q>

/// N discs in a plane: positions and velocities in one unit per axis, as arrays per component,
/// and what the steps that produced this state did.
type Discs<[<Measure>] 'q> =
    { X: float<'q>[]
      Y: float<'q>[]
      VX: float<'q / s>[]
      VY: float<'q / s>[]
      /// Disc–disc contacts resolved since the start.
      Collisions: int64
      /// Disc–wall contacts resolved since the start.
      WallHits: int64
      /// Steps that reached the contact cap and finished without colliding.
      Capped: int64 }

/// The discs' fixed properties and the rules of their contacts.
type Collider<[<Measure>] 'q> =
    { Radii: float<'q>[]
      /// Masses, which need no unit: a contact depends only on their ratio.
      Masses: float[]
      /// 1 is elastic, 0 perfectly inelastic, for disc–disc and disc–wall contacts alike.
      Restitution: float
      Box: Box<'q>
      /// Contacts resolved in one step before the rest are skipped and the step counted as capped.
      CapPerStep: int
      /// Above this many discs, pairs are found through a grid rather than all against all.
      BroadPhaseAbove: int }

[<RequireQualifiedAccess>]
module Discs =

    /// Discs at rest positions and velocities, with no history.
    let create<[<Measure>] 'q> (x: float<'q>[]) (y: float<'q>[]) (vx: float<'q / s>[]) (vy: float<'q / s>[]) : Discs<'q> =
        let n = x.Length

        if y.Length <> n || vx.Length <> n || vy.Length <> n then
            invalidArg "x" "Marey: positions and velocities must have one entry per disc."

        { X = Array.copy x
          Y = Array.copy y
          VX = Array.copy vx
          VY = Array.copy vy
          Collisions = 0L
          WallHits = 0L
          Capped = 0L }

    /// A copy of the arrays, for a checkpoint.
    let copy (d: Discs<'q>) : Discs<'q> =
        { d with
            X = Array.copy d.X
            Y = Array.copy d.Y
            VX = Array.copy d.VX
            VY = Array.copy d.VY }

    /// `Σ ½ m v²`, in mass units times `'q²/s²`.
    let kinetic<[<Measure>] 'q> (masses: float[]) (d: Discs<'q>) : float<'q ^ 2 / s ^ 2> =
        let mutable e = LanguagePrimitives.FloatWithMeasure<'q ^ 2 / s ^ 2> 0.0

        for i in 0 .. d.X.Length - 1 do
            e <- e + 0.5 * masses.[i] * (d.VX.[i] * d.VX.[i] + d.VY.[i] * d.VY.[i])

        e

    /// `Σ m v`, per axis.
    let momentum<[<Measure>] 'q> (masses: float[]) (d: Discs<'q>) : struct (float<'q / s> * float<'q / s>) =
        let mutable px = LanguagePrimitives.FloatWithMeasure<'q / s> 0.0
        let mutable py = LanguagePrimitives.FloatWithMeasure<'q / s> 0.0

        for i in 0 .. d.X.Length - 1 do
            px <- px + masses.[i] * d.VX.[i]
            py <- py + masses.[i] * d.VY.[i]

        struct (px, py)

[<RequireQualifiedAccess>]
module Collide =

    /// Discs of the given radii and masses colliding with restitution `e` in `box`, with a cap of
    /// 10 000 contacts a step and a grid above 256 discs.
    let create<[<Measure>] 'q> (radii: float<'q>[]) (masses: float[]) (e: float) (box: Box<'q>) : Collider<'q> =
        if radii.Length <> masses.Length then
            invalidArg "masses" "Marey: one mass per disc."

        if radii |> Array.exists (fun r -> not (float r > 0.0)) || masses |> Array.exists (fun m -> not (m > 0.0)) then
            invalidArg "radii" "Marey: radii and masses must be positive."

        if not (e >= 0.0 && e <= 1.0) then
            invalidArg "e" (sprintf "Marey: a restitution is between 0 and 1, not %g." e)

        { Radii = radii
          Masses = masses
          Restitution = e
          Box = box
          CapPerStep = 10_000
          BroadPhaseAbove = 256 }

    /// The same collider with another cap on contacts per step.
    let withCap (cap: int) (c: Collider<'q>) : Collider<'q> =
        if cap < 1 then invalidArg "cap" "Marey: the cap must be at least one contact."
        { c with CapPerStep = cap }

    /// The same collider with the grid used above `n` discs.
    let withBroadPhaseAbove (n: int) (c: Collider<'q>) : Collider<'q> = { c with BroadPhaseAbove = max 0 n }

    /// The candidate pairs `(i, j)`, `i < j`, in the same or neighbouring grid cells, that could
    /// touch within `r` from the current positions. Empty at or below `BroadPhaseAbove`, where
    /// every pair is a candidate and none is listed.
    let private candidates
        (col: Collider<'q>)
        (x: float[])
        (y: float[])
        (vx: float[])
        (vy: float[])
        (r: float)
        : struct (int * int)[] =
        let n = x.Length

        if n <= col.BroadPhaseAbove then
            [||]
        else
            let mutable rmax = 0.0
            let mutable vmax = 0.0

            for i in 0 .. n - 1 do
                rmax <- max rmax (float col.Radii.[i])
                vmax <- max vmax (sqrt (vx.[i] * vx.[i] + vy.[i] * vy.[i]))

            // Two discs in non-neighbouring cells are a cell apart, and closing at most `2 vmax`.
            let cell = max (2.0 * rmax + 2.0 * vmax * r) 1e-300

            let x0, y0, cols, rows =
                match col.Box with
                | Box.Periodic(w, hgt) ->
                    let cols = max 1 (int (floor (float w / cell)))
                    let rows = max 1 (int (floor (float hgt / cell)))
                    0.0, 0.0, cols, rows
                | _ ->
                    let x0 = Array.min x
                    let y0 = Array.min y
                    let cols = int (floor ((Array.max x - x0) / cell)) + 1
                    let rows = int (floor ((Array.max y - y0) / cell)) + 1
                    x0, y0, cols, rows

            let periodicCell =
                match col.Box with
                | Box.Periodic(w, hgt) -> Some(float w / float cols, float hgt / float rows)
                | _ -> None

            let cellOf i =
                match periodicCell with
                | Some(cw, ch) ->
                    let cx = (int (floor (x.[i] / cw)) % cols + cols) % cols
                    let cy = (int (floor (y.[i] / ch)) % rows + rows) % rows
                    struct (cx, cy)
                | None -> struct (int (floor ((x.[i] - x0) / cell)), int (floor ((y.[i] - y0) / cell)))

            let buckets = Collections.Generic.Dictionary<struct (int * int), Collections.Generic.List<int>>()

            for i in 0 .. n - 1 do
                let key = cellOf i

                match buckets.TryGetValue key with
                | true, l -> l.Add i
                | _ ->
                    let l = Collections.Generic.List<int>()
                    l.Add i
                    buckets.[key] <- l

            let pairs = Collections.Generic.HashSet<struct (int * int)>()

            for i in 0 .. n - 1 do
                let struct (cx, cy) = cellOf i

                for dx in -1 .. 1 do
                    for dy in -1 .. 1 do
                        let key =
                            match periodicCell with
                            | Some _ -> struct ((cx + dx + cols) % cols, (cy + dy + rows) % rows)
                            | None -> struct (cx + dx, cy + dy)

                        match buckets.TryGetValue key with
                        | true, l ->
                            for j in l do
                                if j > i then
                                    pairs.Add(struct (i, j)) |> ignore
                        | _ -> ()

            Array.ofSeq pairs

    /// The displacement from `i` to `j`, to the nearest image on a torus.
    let private delta (col: Collider<'q>) (x: float[]) (y: float[]) (i: int) (j: int) =
        let mutable dx = x.[j] - x.[i]
        let mutable dy = y.[j] - y.[i]

        match col.Box with
        | Box.Periodic(w, hgt) ->
            let w = float w
            let hgt = float hgt
            // `floor (x + 0.5)` rather than `Math.Round`, which Fable rounds differently near a
            // half, and ties to even on .NET.
            dx <- dx - w * floor (dx / w + 0.5)
            dy <- dy - hgt * floor (dy / hgt + 0.5)
        | _ -> ()

        struct (dx, dy)

    /// The time two discs first touch, if they are closing and do within `r`.
    let private contact (col: Collider<'q>) (x: float[]) (y: float[]) (vx: float[]) (vy: float[]) (i: int) (j: int) (r: float) =
        let struct (dx, dy) = delta col x y i j
        let dvx = vx.[j] - vx.[i]
        let dvy = vy.[j] - vy.[i]
        let b = dx * dvx + dy * dvy

        if b >= 0.0 then
            infinity
        else
            let sum = float col.Radii.[i] + float col.Radii.[j]
            let a = dvx * dvx + dvy * dvy
            let c = dx * dx + dy * dy - sum * sum

            if c <= 0.0 then
                // Touching or overlapping, and closing: now.
                0.0
            else
                let disc = b * b - a * c

                if disc < 0.0 then
                    infinity
                else
                    // `c / (−b + √disc)`: the smaller root, without cancellation.
                    let tau = c / (-b + sqrt disc)
                    if tau <= r then tau else infinity

    /// The time disc `i` reaches a wall, and which: 0 for an x wall, 1 for a y wall.
    let private wall (col: Collider<'q>) (x: float[]) (y: float[]) (vx: float[]) (vy: float[]) (i: int) =
        match col.Box with
        | Box.Walls(xMin, xMax, yMin, yMax) ->
            let rad = float col.Radii.[i]

            let along (p: float) (v: float) (lo: float) (hi: float) =
                if v > 0.0 then max 0.0 ((hi - rad - p) / v)
                elif v < 0.0 then max 0.0 ((lo + rad - p) / v)
                else infinity

            let tx = along x.[i] vx.[i] (float xMin) (float xMax)
            let ty = along y.[i] vy.[i] (float yMin) (float yMax)
            if tx <= ty then struct (tx, 0) else struct (ty, 1)
        | _ -> struct (infinity, -1)

    /// A step of `h`: free flight, with every contact inside it resolved in time order.
    let step<[<Measure>] 'q> (col: Collider<'q>) : Stepper<Discs<'q>, s> =
        let e = col.Restitution
        let m = col.Masses

        fun _ h d ->
            let n = d.X.Length

            if col.Radii.Length <> n then
                invalidArg "d" (sprintf "Marey: %d discs for a collider of %d." n col.Radii.Length)

            let x = d.X |> Array.map float
            let y = d.Y |> Array.map float
            let vx = d.VX |> Array.map float
            let vy = d.VY |> Array.map float
            let mutable remaining = float h
            let mutable collisions = d.Collisions
            let mutable wallHits = d.WallHits
            let mutable capped = d.Capped
            let mutable resolved = 0
            let mutable fly = true

            let move (tau: float) =
                for k in 0 .. n - 1 do
                    x.[k] <- x.[k] + vx.[k] * tau
                    y.[k] <- y.[k] + vy.[k] * tau

            while fly do
                if resolved >= col.CapPerStep then
                    move remaining
                    capped <- capped + 1L
                    fly <- false
                else
                    // The earliest contact, ties broken by the lower pair, then by walls.
                    let mutable best = infinity
                    let mutable bi = -1
                    let mutable bj = -1

                    let consider i j =
                        let tau = contact col x y vx vy i j remaining

                        if tau < best || (tau = best && tau < infinity && (i < bi || (i = bi && j < bj))) then
                            best <- tau
                            bi <- i
                            bj <- j

                    if n <= col.BroadPhaseAbove then
                        for i in 0 .. n - 2 do
                            for j in i + 1 .. n - 1 do
                                consider i j
                    else
                        for struct (i, j) in candidates col x y vx vy remaining do
                            consider i j

                    let mutable wi = -1
                    let mutable axis = -1

                    for i in 0 .. n - 1 do
                        let struct (tau, a) = wall col x y vx vy i

                        if tau <= remaining && tau < best then
                            best <- tau
                            wi <- i
                            axis <- a

                    if best > remaining then
                        move remaining
                        fly <- false
                    else
                        move best
                        remaining <- remaining - best
                        resolved <- resolved + 1

                        if wi >= 0 then
                            if axis = 0 then vx.[wi] <- -e * vx.[wi] else vy.[wi] <- -e * vy.[wi]
                            wallHits <- wallHits + 1L
                        else
                            // The impulse along the line of centres at contact.
                            let struct (dx, dy) = delta col x y bi bj
                            let dist = sqrt (dx * dx + dy * dy)
                            let nx = dx / dist
                            let ny = dy / dist
                            let u = (vx.[bj] - vx.[bi]) * nx + (vy.[bj] - vy.[bi]) * ny
                            let j = -(1.0 + e) * u / (1.0 / m.[bi] + 1.0 / m.[bj])
                            vx.[bi] <- vx.[bi] - j / m.[bi] * nx
                            vy.[bi] <- vy.[bi] - j / m.[bi] * ny
                            vx.[bj] <- vx.[bj] + j / m.[bj] * nx
                            vy.[bj] <- vy.[bj] + j / m.[bj] * ny
                            collisions <- collisions + 1L

            match col.Box with
            | Box.Periodic(w, hgt) ->
                let w = float w
                let hgt = float hgt

                for k in 0 .. n - 1 do
                    x.[k] <- x.[k] - w * floor (x.[k] / w)
                    y.[k] <- y.[k] - hgt * floor (y.[k] / hgt)
            | _ -> ()

            { X = x |> Array.map LanguagePrimitives.FloatWithMeasure<'q>
              Y = y |> Array.map LanguagePrimitives.FloatWithMeasure<'q>
              VX = vx |> Array.map LanguagePrimitives.FloatWithMeasure<'q / s>
              VY = vy |> Array.map LanguagePrimitives.FloatWithMeasure<'q / s>
              Collisions = collisions
              WallHits = wallHits
              Capped = capped }
