namespace Marey

open System
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// D148 — STAM'S STABLE FLUIDS, ON A STAGGERED GRID.
//
// Incompressible flow in a box of square cells: semi-Lagrangian advection, implicit viscosity, and
// a pressure projection, each unconditionally stable, so a step is as long as the picture wants.
//
// **The grid is staggered (MAC).** The x velocity lives on the cells' vertical faces and the y
// velocity on their horizontal ones; the dye and the pressure potential at the centres. Then the divergence of a
// pressure gradient is exactly the five-point Laplacian, and a projection leaves only its
// solver's residual. The collocated grid of GPU Gems takes the divergence over two cells and the
// Laplacian over one, and its projection stops at 8.8% of the divergence however long the solver
// runs (measured in the M5.6 inventory).
//
// **The pressure by multigrid, a fixed number of V-cycles**, warm-started from the last step's
// pressure, which is why it is part of the state; viscosity and diffusion by red-black
// Gauss–Seidel at a fixed number of sweeps. Fixed rather than to a tolerance, so the CPU and the GPU
// (`GpuSim.stam`) do the same work; a half-sweep reads only the other colour, so it is order-free
// and the GPU's pass computes the same numbers.
//
// **Edges per axis**: a wall (no flow through it, free slip along it) or periodic. On a periodic
// axis the last face column (or row) repeats the first.
//
// **Each stage is a fresh array**, so a step never writes its input, and a trajectory's
// checkpoints are the states themselves.
// ======================================================================================

/// An axis's two edges: a wall, or joined to each other.
[<RequireQualifiedAccess>]
type FluidEdge =
    /// No flow through it; the flow slips along it freely.
    | Wall
    /// The far edge joins the near one.
    | Periodic

/// An incompressible flow in a box of `Nx × Ny` square cells of side `Cell`, its lower-left
/// corner at `(X0, Y0)`, carrying a dye in `'q`.
///
/// Every array is row-major with x fastest. `UFaces` is the x velocity on the vertical faces,
/// `(Nx + 1) × Ny`; `VFaces` the y velocity on the horizontal faces, `Nx × (Ny + 1)`; `Dye` and
/// `Pressure` at the centres, `Nx × Ny`. `Pressure` is the kinematic pressure (over the density)
/// the last projection found, which starts the next one.
type Fluid<[<Measure>] 'x, [<Measure>] 'q> =
    { Nx: int
      Ny: int
      Cell: float<'x>
      X0: float<'x>
      Y0: float<'x>
      XEdge: FluidEdge
      YEdge: FluidEdge
      UFaces: float<'x / s>[]
      VFaces: float<'x / s>[]
      Dye: float<'q>[]
      Pressure: float<'x ^ 2 / s ^ 2>[] }

/// What drives a fluid and how hard its pressure is solved.
type StamOptions<[<Measure>] 'x, [<Measure>] 'q> =
    { /// The kinematic viscosity; zero skips the viscous solve.
      Viscosity: float<'x ^ 2 / s>
      /// The dye's diffusivity; zero skips its solve.
      Diffusion: float<'x ^ 2 / s>
      /// Upward acceleration per unit of dye: smoke where the dye is warm.
      Buoyancy: float<'x / s ^ 2 / 'q>
      /// A steady acceleration, by position, evaluated once per geometry.
      Force: float<'x> -> float<'x> -> struct (float<'x / s ^ 2> * float<'x / s ^ 2>)
      /// A steady dye source, by position, evaluated once per geometry.
      Source: float<'x> -> float<'x> -> float<'q / s>
      /// Multigrid V-cycles for the pressure, each step.
      Cycles: int
      /// Red-black Gauss–Seidel sweeps for the viscous and the diffusive solve.
      Sweeps: int }

[<RequireQualifiedAccess>]
module Fluid =

    let inline internal plain (xs: float<'u>[]) : float[] = unbox<float[]> (box xs)
    let inline internal tagged<[<Measure>] 'u> (xs: float[]) : float<'u>[] = unbox<float<'u>[]> (box xs)

    /// A box at rest and without dye, its corner at the origin.
    let box<[<Measure>] 'x, [<Measure>] 'q> (x: FluidEdge) (y: FluidEdge) (h: float<'x>) (nx: int) (ny: int) : Fluid<'x, 'q> =
        if nx < 2 || ny < 2 then
            invalidArg "nx" (sprintf "Marey: a fluid needs at least 2 × 2 cells; got %d × %d." nx ny)

        if not (h > LanguagePrimitives.FloatWithMeasure 0.0) || Double.IsInfinity(float h) then
            invalidArg "h" (sprintf "Marey: a cell's side must be positive and finite; got %g." (float h))

        { Nx = nx
          Ny = ny
          Cell = h
          X0 = LanguagePrimitives.FloatWithMeasure 0.0
          Y0 = LanguagePrimitives.FloatWithMeasure 0.0
          XEdge = x
          YEdge = y
          UFaces = Array.zeroCreate ((nx + 1) * ny)
          VFaces = Array.zeroCreate (nx * (ny + 1))
          Dye = Array.zeroCreate (nx * ny)
          Pressure = Array.zeroCreate (nx * ny) }

    /// The box moved so its lower-left corner is at `(x0, y0)`.
    let withOrigin<[<Measure>] 'x, [<Measure>] 'q> (x0: float<'x>) (y0: float<'x>) (f: Fluid<'x, 'q>) : Fluid<'x, 'q> =
        { f with X0 = x0; Y0 = y0 }

    /// The x coordinate of face column `i` and the y of face row `j`.
    let private faceX<[<Measure>] 'x, [<Measure>] 'q> (f: Fluid<'x, 'q>) (i: int) = f.X0 + float i * f.Cell
    let private faceY<[<Measure>] 'x, [<Measure>] 'q> (f: Fluid<'x, 'q>) (j: int) = f.Y0 + float j * f.Cell
    let private centreX<[<Measure>] 'x, [<Measure>] 'q> (f: Fluid<'x, 'q>) (i: int) = f.X0 + (float i + 0.5) * f.Cell
    let private centreY<[<Measure>] 'x, [<Measure>] 'q> (f: Fluid<'x, 'q>) (j: int) = f.Y0 + (float j + 0.5) * f.Cell

    /// The velocity sampled from a function: `u` on the vertical faces, `v` on the horizontal
    /// ones. Wall faces stay at rest and a periodic edge's last faces repeat its first. Not
    /// projected: a field that is not divergence-free becomes so in the first step.
    let withVelocity<[<Measure>] 'x, [<Measure>] 'q>
        (velocity: float<'x> -> float<'x> -> struct (float<'x / s> * float<'x / s>))
        (f: Fluid<'x, 'q>)
        : Fluid<'x, 'q> =
        let u =
            Array.init ((f.Nx + 1) * f.Ny) (fun k ->
                let i, j = k % (f.Nx + 1), k / (f.Nx + 1)
                let i = if f.XEdge = FluidEdge.Periodic && i = f.Nx then 0 else i

                if f.XEdge = FluidEdge.Wall && (i = 0 || i = f.Nx) then LanguagePrimitives.FloatWithMeasure 0.0
                else (let struct (a, _) = velocity (faceX f i) (centreY f j) in a))

        let v =
            Array.init (f.Nx * (f.Ny + 1)) (fun k ->
                let i, j = k % f.Nx, k / f.Nx
                let j = if f.YEdge = FluidEdge.Periodic && j = f.Ny then 0 else j

                if f.YEdge = FluidEdge.Wall && (j = 0 || j = f.Ny) then LanguagePrimitives.FloatWithMeasure 0.0
                else (let struct (_, b) = velocity (centreX f i) (faceY f j) in b))

        { f with UFaces = u; VFaces = v }

    /// The dye sampled from a function at the cells' centres.
    let withDye<[<Measure>] 'x, [<Measure>] 'q> (dye: float<'x> -> float<'x> -> float<'q>) (f: Fluid<'x, 'q>) : Fluid<'x, 'q> =
        { f with Dye = Array.init (f.Nx * f.Ny) (fun k -> dye (centreX f (k % f.Nx)) (centreY f (k / f.Nx))) }

    let private centres<[<Measure>] 'x, [<Measure>] 'q> (f: Fluid<'x, 'q>) =
        Array.init f.Nx (centreX f), Array.init f.Ny (centreY f)

    /// The velocity at the cells' centres, each component the mean of its two faces: a grid every
    /// field mark draws.
    let velocity<[<Measure>] 'x, [<Measure>] 'q> (f: Fluid<'x, 'q>) : VectorGrid<'x, 'x, 'x / s, 'x / s> =
        let xs, ys = centres f
        let su = f.Nx + 1

        { Xs = xs
          Ys = ys
          U = Array.init (f.Nx * f.Ny) (fun k -> let i, j = k % f.Nx, k / f.Nx in 0.5 * (f.UFaces.[j * su + i] + f.UFaces.[j * su + i + 1]))
          W = Array.init (f.Nx * f.Ny) (fun k -> let i, j = k % f.Nx, k / f.Nx in 0.5 * (f.VFaces.[j * f.Nx + i] + f.VFaces.[(j + 1) * f.Nx + i])) }

    /// The dye at the cells' centres.
    let dye<[<Measure>] 'x, [<Measure>] 'q> (f: Fluid<'x, 'q>) : ScalarGrid<'x, 'x, 'q> =
        let xs, ys = centres f
        { Xs = xs; Ys = ys; Values = f.Dye }

    /// Each cell's net outflow per unit area, from its four faces: what a projection drives to
    /// zero. Exactly the discrete divergence the projection solves against.
    let divergence<[<Measure>] 'x, [<Measure>] 'q> (f: Fluid<'x, 'q>) : ScalarGrid<'x, 'x, 1 / s> =
        let xs, ys = centres f
        let su = f.Nx + 1

        { Xs = xs
          Ys = ys
          Values =
            Array.init (f.Nx * f.Ny) (fun k ->
                let i, j = k % f.Nx, k / f.Nx
                (f.UFaces.[j * su + i + 1] - f.UFaces.[j * su + i] + f.VFaces.[(j + 1) * f.Nx + i] - f.VFaces.[j * f.Nx + i]) / f.Cell) }

    /// The kinematic pressure, the pressure over the density, at the cells' centres, up to a
    /// constant: what the last projection found.
    let pressure<[<Measure>] 'x, [<Measure>] 'q> (f: Fluid<'x, 'q>) : ScalarGrid<'x, 'x, 'x ^ 2 / s ^ 2> =
        let xs, ys = centres f
        { Xs = xs; Ys = ys; Values = f.Pressure }

    /// The vorticity at the cells' centres, `∂v/∂x − ∂u/∂y` of `velocity`.
    let vorticity<[<Measure>] 'x, [<Measure>] 'q> (f: Fluid<'x, 'q>) : ScalarGrid<'x, 'x, 1 / s> = Field.curl (velocity f)

/// D148 — a fluid's step, on the CPU. `GpuSim.stam` is the same step on the GPU.
[<RequireQualifiedAccess>]
module Stam =

    /// No viscosity, diffusion, buoyancy, force or source; two V-cycles for the pressure and twenty
    /// sweeps for a viscous or diffusive solve.
    let defaults<[<Measure>] 'x, [<Measure>] 'q> : StamOptions<'x, 'q> =
        { Viscosity = LanguagePrimitives.FloatWithMeasure 0.0
          Diffusion = LanguagePrimitives.FloatWithMeasure 0.0
          Buoyancy = LanguagePrimitives.FloatWithMeasure 0.0
          Force = fun _ _ -> struct (LanguagePrimitives.FloatWithMeasure 0.0, LanguagePrimitives.FloatWithMeasure 0.0)
          Source = fun _ _ -> LanguagePrimitives.FloatWithMeasure 0.0
          Cycles = 2
          Sweeps = 20 }

    // ---- The three lattices, erased ------------------------------------------------------------

    /// Nodes a solve moves, `Mx × My` of them in rows `Stride` apart, at `(i + Ox, j + Oy)` in
    /// cells. On a wrapped axis the node past the last is the first, and the stored copy of it (a
    /// periodic face's last column or row) is refreshed after every stage. On a fixed axis the end
    /// nodes are wall faces, held at zero. Otherwise the axis has no node beyond its ends, and a
    /// solve leaves the missing neighbour out, which is a zero flux through the edge.
    type internal Lat =
        { Mx: int
          My: int
          Stride: int
          Rows: int
          WrapX: bool
          WrapY: bool
          FixX: bool
          FixY: bool
          Ox: float
          Oy: float }

    let internal lattices (nx: int) (ny: int) (px: bool) (py: bool) =
        let u =
            { Mx = (if px then nx else nx + 1)
              My = ny
              Stride = nx + 1
              Rows = ny
              WrapX = px
              WrapY = py
              FixX = not px
              FixY = false
              Ox = 0.0
              Oy = 0.5 }

        let v =
            { Mx = nx
              My = (if py then ny else ny + 1)
              Stride = nx
              Rows = ny + 1
              WrapX = px
              WrapY = py
              FixX = false
              FixY = not py
              Ox = 0.5
              Oy = 0.0 }

        let c =
            { Mx = nx
              My = ny
              Stride = nx
              Rows = ny
              WrapX = px
              WrapY = py
              FixX = false
              FixY = false
              Ox = 0.5
              Oy = 0.5 }

        u, v, c

    let inline internal fixedAt (l: Lat) (i: int) (j: int) =
        (l.FixX && (i = 0 || i = l.Mx - 1)) || (l.FixY && (j = 0 || j = l.My - 1))

    /// A periodic face column's or row's stored copy of the first, refreshed.
    let internal refresh (l: Lat) (a: float[]) =
        if l.Stride > l.Mx then
            for j in 0 .. l.Rows - 1 do
                a.[j * l.Stride + l.Mx] <- a.[j * l.Stride]

        if l.Rows > l.My then
            for i in 0 .. l.Stride - 1 do
                a.[l.My * l.Stride + i] <- a.[i]

    /// One axis's two neighbouring indices and fraction for a position in node units: wrapped, or
    /// clamped into the nodes.
    let inline private axis (wrap: bool) (m: int) (a: float) =
        if wrap then
            let p = float m
            let mutable w = a - p * floor (a / p)
            let mutable i0 = int (floor w)

            if i0 >= m then
                i0 <- i0 - m
                w <- w - p

            struct (i0, (if i0 + 1 = m then 0 else i0 + 1), w - float i0)
        else
            let c = max 0.0 (min (float (m - 1)) a)
            let i0 = min (m - 2) (int (floor c))
            struct (i0, i0 + 1, c - float i0)

    /// The bilinear interpolant of a lattice's values at `(x, y)` in cells.
    let internal sample (l: Lat) (a: float[]) (x: float) (y: float) =
        let struct (i0, i1, fx) = axis l.WrapX l.Mx (x - l.Ox)
        let struct (j0, j1, fy) = axis l.WrapY l.My (y - l.Oy)
        let a00 = a.[j0 * l.Stride + i0]
        let a10 = a.[j0 * l.Stride + i1]
        let a01 = a.[j1 * l.Stride + i0]
        let a11 = a.[j1 * l.Stride + i1]
        let lo = a00 + fx * (a10 - a00)
        let hi = a01 + fx * (a11 - a01)
        lo + fy * (hi - lo)

    /// Every free node's value carried back along the velocity for `h` seconds: the value at the
    /// point the flow brings there, `x − h·u(x)`, clamped into the box across a wall.
    let internal advect (lu: Lat) (lv: Lat) (nx: int) (ny: int) (hc: float) (u: float[]) (v: float[]) (l: Lat) (a: float[]) =
        let out = Array.zeroCreate a.Length

        for j in 0 .. l.My - 1 do
            for i in 0 .. l.Mx - 1 do
                if not (fixedAt l i j) then
                    let x = float i + l.Ox
                    let y = float j + l.Oy
                    let mutable px = x - hc * sample lu u x y
                    let mutable py = y - hc * sample lv v x y

                    if not l.WrapX then px <- max 0.0 (min (float nx) px)
                    if not l.WrapY then py <- max 0.0 (min (float ny) py)

                    out.[j * l.Stride + i] <- sample l a px py

        refresh l out
        out

    /// The over-relaxation factor of each half-sweep, by Chebyshev acceleration for a Jacobi
    /// iteration contracting by `rho`: 1, then `1 / (1 − ρ²/2)`, then `1 / (1 − ρ²ω/4)`, rising to the
    /// optimal factor. The optimal factor from the first sweep amplifies the error before it
    /// damps it, and a fluid stopped after 40 sweeps would be left more divergent than it
    /// started (measured: 7.9 times at 10 sweeps on 64²).
    let internal omegas (rho: float) (halves: int) =
        let w = Array.zeroCreate halves
        let r2 = rho * rho

        for k in 0 .. halves - 1 do
            w.[k] <- if k = 0 then 1.0 elif k = 1 then 1.0 / (1.0 - r2 / 2.0) else 1.0 / (1.0 - r2 * w.[k - 1] / 4.0)

        w

    /// The sum of a node's present neighbours and their count: across a wrapped edge to the other
    /// side, and none past an edge that is not.
    let inline private around (l: Lat) (src: float[]) (i: int) (j: int) =
        let mutable s = 0.0
        let mutable n = 0

        if i > 0 then
            s <- s + src.[j * l.Stride + i - 1]
            n <- n + 1
        elif l.WrapX then
            s <- s + src.[j * l.Stride + l.Mx - 1]
            n <- n + 1

        if i < l.Mx - 1 then
            s <- s + src.[j * l.Stride + i + 1]
            n <- n + 1
        elif l.WrapX then
            s <- s + src.[j * l.Stride]
            n <- n + 1

        if j > 0 then
            s <- s + src.[(j - 1) * l.Stride + i]
            n <- n + 1
        elif l.WrapY then
            s <- s + src.[(l.My - 1) * l.Stride + i]
            n <- n + 1

        if j < l.My - 1 then
            s <- s + src.[(j + 1) * l.Stride + i]
            n <- n + 1
        elif l.WrapY then
            s <- s + src.[i]
            n <- n + 1

        struct (s, n)

    /// Red-black SOR for `(c + a·n) x − a·Σ neighbours = b` on a lattice's free nodes, `n` being
    /// the neighbours present, from `x0`, for `sweeps` sweeps under Chebyshev acceleration for a
    /// Jacobi contraction `rho`; Gauss–Seidel when `rho` is zero. A half-sweep reads only the
    /// other colour, so the result does not depend on the order cells are visited in.
    let internal sor (l: Lat) (a: float) (c: float) (rho: float) (sweeps: int) (b: float[]) (x0: float[]) =
        let schedule = omegas rho (2 * sweeps)
        let mutable cur = Array.copy x0
        // A wrapped axis with an odd count puts same-coloured nodes side by side across the seam,
        // and then a half-sweep has to read a copy.
        let copying = (l.WrapX && l.Mx % 2 = 1) || (l.WrapY && l.My % 2 = 1)

        for sweep in 0 .. sweeps - 1 do
            for colour in 0 .. 1 do
                let omega = schedule.[2 * sweep + colour]
                let src = cur
                let dst = if copying then Array.copy cur else cur

                for j in 0 .. l.My - 1 do
                    for i in (colour + j) % 2 .. 2 .. l.Mx - 1 do
                        if not (fixedAt l i j) then
                            let struct (s, n) = around l src i j
                            let k = j * l.Stride + i
                            let gs = (b.[k] + a * s) / (c + a * float n)
                            dst.[k] <- src.[k] + omega * (gs - src.[k])

                cur <- dst

        refresh l cur
        cur

    // ---- The pressure's multigrid --------------------------------------------------------------
    //
    // Over-relaxation amplifies a residual before it damps it (measured: 5.9 times after five
    // Chebyshev sweeps on 64²), and Gauss–Seidel alone damps a box's slowest mode in O(N²) sweeps,
    // so neither is a pressure solver at a fixed small count. A V-cycle is: two red-black
    // Gauss–Seidel sweeps, the residual averaged onto a grid of half the cells each way, the same
    // cycle there, the correction interpolated back bilinearly, two sweeps more. The coarse
    // operator is the fine one at a quarter of the coupling, because the cells are twice the size.

    /// The centres' lattice at one coarseness, and its coupling.
    type internal Level = { L: Lat; A: float }

    /// The levels, finest first: halved while both counts are even and at least four.
    let internal levels (l: Lat) (a: float) : Level[] =
        let rec go (lv: Level) (acc: Level list) =
            let acc = lv :: acc

            if lv.L.Mx % 2 = 0 && lv.L.My % 2 = 0 && lv.L.Mx >= 4 && lv.L.My >= 4 then
                let mx, my = lv.L.Mx / 2, lv.L.My / 2

                go
                    { L = { lv.L with Mx = mx; My = my; Stride = mx; Rows = my }
                      A = lv.A / 4.0 }
                    acc
            else
                List.rev acc

        go { L = l; A = a } [] |> Array.ofList

    /// `b − ((c + a·n) x − a·Σ neighbours)` at every node of a centres' lattice.
    let internal residual (l: Lat) (a: float) (c: float) (x: float[]) (b: float[]) =
        let r = Array.zeroCreate x.Length

        for j in 0 .. l.My - 1 do
            for i in 0 .. l.Mx - 1 do
                let struct (s, n) = around l x i j
                let k = j * l.Stride + i
                r.[k] <- b.[k] - ((c + a * float n) * x.[k] - a * s)

        r

    /// The mean of each 2 × 2 block, onto the coarser lattice.
    let internal restrict (fine: Lat) (coarse: Lat) (r: float[]) =
        Array.init (coarse.Mx * coarse.My) (fun k ->
            let i, j = 2 * (k % coarse.Mx), 2 * (k / coarse.Mx)
            let w = fine.Stride
            0.25 * (r.[j * w + i] + r.[j * w + i + 1] + r.[(j + 1) * w + i] + r.[(j + 1) * w + i + 1]))

    /// A coarse correction on the finer lattice, bilinearly: 9/16 of the parent cell, 3/16 of each
    /// side neighbour nearest the fine cell, 1/16 of the diagonal one. Past an edge that does not
    /// wrap, the parent stands in for the missing neighbour.
    let internal prolong (coarse: Lat) (fine: Lat) (e: float[]) =
        let side (wrap: bool) (m: int) (p: int) (q: int) =
            if q >= 0 && q < m then q
            elif wrap then (q + m) % m
            else p

        Array.init (fine.Mx * fine.My) (fun k ->
            let i, j = k % fine.Mx, k / fine.Mx
            let pi, pj = i / 2, j / 2
            let si = side coarse.WrapX coarse.Mx pi (if i % 2 = 0 then pi - 1 else pi + 1)
            let sj = side coarse.WrapY coarse.My pj (if j % 2 = 0 then pj - 1 else pj + 1)
            let at a b = e.[b * coarse.Mx + a]
            0.5625 * at pi pj + 0.1875 * at si pj + 0.1875 * at pi sj + 0.0625 * at si sj)

    /// One V-cycle from level `k` down.
    let rec internal vcycle (lv: Level[]) (k: int) (c: float) (b: float[]) (x: float[]) : float[] =
        let l = lv.[k].L
        let a = lv.[k].A

        if k = lv.Length - 1 then
            // The coarsest is solved outright: over-relaxed, long enough that its transient is over.
            let m = max l.Mx l.My
            let rho = a * (2.0 + 2.0 * cos (Math.PI / float m)) / (c + 4.0 * a)
            sor l a c rho (4 * m) b x
        else
            let x1 = sor l a c 0.0 2 b x
            let coarse = lv.[k + 1].L
            let rc = restrict l coarse (residual l a c x1 b)
            let ec = vcycle lv (k + 1) c rc (Array.zeroCreate rc.Length)
            let x2 = Array.map2 (+) x1 (prolong coarse l ec)
            sor l a c 0.0 2 b x2

    /// `cycles` V-cycles for `(c + a·n) x − a·Σ neighbours = b` on a centres' lattice, from `x0`.
    let internal multigrid (l: Lat) (a: float) (c: float) (cycles: int) (b: float[]) (x0: float[]) =
        let lv = levels l a
        let mutable x = x0

        for _ in 1 .. cycles do
            x <- vcycle lv 0 c b x

        x

    /// The forcing, sampled once per geometry: the force on each face, the source at each centre.
    type internal Forcing =
        { Key: struct (int * int * float * float * float)
          Fu: float[]
          Fv: float[]
          Src: float[] }

    let internal forcing<[<Measure>] 'x, [<Measure>] 'q> (o: StamOptions<'x, 'q>) (f: Fluid<'x, 'q>) : Forcing =
        let h = float f.Cell
        let x0, y0 = float f.X0, float f.Y0
        let w = LanguagePrimitives.FloatWithMeasure<'x>

        { Key = struct (f.Nx, f.Ny, h, x0, y0)
          Fu =
            Array.init ((f.Nx + 1) * f.Ny) (fun k ->
                let i, j = k % (f.Nx + 1), k / (f.Nx + 1)
                let struct (a, _) = o.Force (w (x0 + float i * h)) (w (y0 + (float j + 0.5) * h))
                float a)
          Fv =
            Array.init (f.Nx * (f.Ny + 1)) (fun k ->
                let i, j = k % f.Nx, k / f.Nx
                let struct (_, b) = o.Force (w (x0 + (float i + 0.5) * h)) (w (y0 + float j * h))
                float b)
          Src =
            Array.init (f.Nx * f.Ny) (fun k ->
                float (o.Source (w (x0 + (float (k % f.Nx) + 0.5) * h)) (w (y0 + (float (k / f.Nx) + 0.5) * h)))) }

    /// The step: advection of the velocity and the dye by the velocity; buoyancy, the force and the
    /// source; viscosity and diffusion, implicit; and the projection. The forcing is sampled once
    /// for each geometry the step meets and kept, as one immutable value replaced whole, so two
    /// threads stepping at once can only sample it twice.
    let step<[<Measure>] 'x, [<Measure>] 'q> (options: StamOptions<'x, 'q>) : Stepper<Fluid<'x, 'q>, s> =
        if options.Sweeps < 1 || options.Cycles < 1 then
            invalidArg "options" (sprintf "Marey: a fluid's solves need at least one sweep and one cycle; got %d and %d." options.Sweeps options.Cycles)

        if options.Viscosity < LanguagePrimitives.FloatWithMeasure 0.0 || options.Diffusion < LanguagePrimitives.FloatWithMeasure 0.0 then
            invalidArg "options" "Marey: a viscosity or a diffusivity cannot be negative."

        let kept = ref None

        fun _ h f ->
            let key = struct (f.Nx, f.Ny, float f.Cell, float f.X0, float f.Y0)

            let forced =
                match kept.Value with
                | Some(k: Forcing) when k.Key = key -> k
                | _ ->
                    let k = forcing options f
                    kept.Value <- Some k
                    k

            let px = f.XEdge = FluidEdge.Periodic
            let py = f.YEdge = FluidEdge.Periodic
            let lu, lv, lc = lattices f.Nx f.Ny px py
            let hs = float h
            let hx = float f.Cell
            let u0 = Fluid.plain f.UFaces
            let v0 = Fluid.plain f.VFaces

            // Advection, in cells: a velocity times the step over the side.
            let hc = hs / hx
            let mutable u = advect lu lv f.Nx f.Ny hc u0 v0 lu u0
            let mutable v = advect lu lv f.Nx f.Ny hc u0 v0 lv v0
            let mutable d = advect lu lv f.Nx f.Ny hc u0 v0 lc (Fluid.plain f.Dye)

            // Forcing.
            let buoy = float options.Buoyancy

            for j in 0 .. lu.My - 1 do
                for i in 0 .. lu.Mx - 1 do
                    if not (fixedAt lu i j) then
                        let k = j * lu.Stride + i
                        u.[k] <- u.[k] + hs * forced.Fu.[k]

            for j in 0 .. lv.My - 1 do
                for i in 0 .. lv.Mx - 1 do
                    if not (fixedAt lv i j) then
                        let k = j * lv.Stride + i
                        let below = if j > 0 then j - 1 else f.Ny - 1
                        let warm = 0.5 * (d.[below * f.Nx + i] + d.[(if j < f.Ny then j else 0) * f.Nx + i])
                        v.[k] <- v.[k] + hs * (forced.Fv.[k] + buoy * warm)

            for k in 0 .. d.Length - 1 do
                d.[k] <- d.[k] + hs * forced.Src.[k]

            refresh lu u
            refresh lv v

            // Viscosity and diffusion: (1 − νh∇²) x = x*, with ∇² over the free nodes.
            let sweeps = options.Sweeps
            let nu = float options.Viscosity * hs / (hx * hx)

            if nu > 0.0 then
                u <- sor lu nu 1.0 0.0 sweeps u u
                v <- sor lv nu 1.0 0.0 sweeps v v

            let kd = float options.Diffusion * hs / (hx * hx)

            if kd > 0.0 then
                d <- sor lc kd 1.0 0.0 sweeps d d

            // Projection: ∇²φ = ∇·u, then u −= ∇φ on the free faces.
            let div = Array.zeroCreate (f.Nx * f.Ny)
            let su = f.Nx + 1

            for j in 0 .. f.Ny - 1 do
                for i in 0 .. f.Nx - 1 do
                    div.[j * f.Nx + i] <- -(u.[j * su + i + 1] - u.[j * su + i] + v.[(j + 1) * f.Nx + i] - v.[j * f.Nx + i]) * hx

            // The potential is the step times the pressure, so the last pressure starts it at any step.
            let p0 = Fluid.plain f.Pressure
            let phi = multigrid lc 1.0 0.0 options.Cycles div (Array.map (fun p -> hs * p) p0)

            for j in 0 .. lu.My - 1 do
                for i in 0 .. lu.Mx - 1 do
                    if not (fixedAt lu i j) then
                        let left = if i > 0 then i - 1 else f.Nx - 1
                        let k = j * lu.Stride + i
                        u.[k] <- u.[k] - (phi.[j * f.Nx + (if i < f.Nx then i else 0)] - phi.[j * f.Nx + left]) / hx

            for j in 0 .. lv.My - 1 do
                for i in 0 .. lv.Mx - 1 do
                    if not (fixedAt lv i j) then
                        let below = if j > 0 then j - 1 else f.Ny - 1
                        let k = j * lv.Stride + i
                        v.[k] <- v.[k] - (phi.[(if j < f.Ny then j else 0) * f.Nx + i] - phi.[below * f.Nx + i]) / hx

            refresh lu u
            refresh lv v

            { f with
                UFaces = Fluid.tagged u
                VFaces = Fluid.tagged v
                Dye = Fluid.tagged d
                // A step of no length says nothing about the pressure; the last one stands.
                Pressure = (if hs > 0.0 then Fluid.tagged (Array.map (fun q -> q / hs) phi) else f.Pressure) }
