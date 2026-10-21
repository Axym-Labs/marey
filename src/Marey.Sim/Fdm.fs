namespace Marey

open System
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// FINITE DIFFERENCES ON A GRID: HEAT, WAVE, POISSON.
//
// The solvers step `Marey.Fields`' `ScalarGrid`, and their units come out of the types: `∂T/∂t = α
// ∇²T` with `T` in kelvin and `α` in m²/s steps in kelvin, and the explicit limit `dt ≤ h² / (4α)`
// is an expression in seconds.
//
// **Their own stencil.** `Field.laplacian` is `div (grad g)`, two central first differences: the
// wide stencil `(f₍ᵢ₋₂₎ − 2fᵢ + f₍ᵢ₊₂₎) / 4h²`, exactly zero on a checkerboard. A heat solver
// stepped with it never damps the odd–even mode, so round-off stripes persist forever. The solvers
// use the compact five-point stencil, `Fdm.laplacian5`, and `Field.laplacian` stays the display
// operator it was built as.
//
// **Uniform grids only**, refused otherwise with the reason: a five-point stencil on uneven nodes
// is first order there. The two axes may have different spacings.
//
// **Boundaries are typed, per side**: a Dirichlet value in the field's unit, a Neumann flux (the
// outward normal derivative) in the field's unit per metre, or periodic, which both sides of an
// axis must be. A Neumann side is a ghost node mirrored through the boundary, second order. On a
// periodic axis the last node's neighbour is the first, so the grid does not repeat the end of the
// period. At a corner of two Dirichlet sides, the x side's value holds.
//
// **Each step returns a new grid** and leaves its input as it was. The plan was a buffer the step
// owns, copied at checkpoints; a buffer in the step is shared by two threads seeking one
// trajectory, and one carried in the state is overwritten under any state a caller kept. A fresh
// array a step is the price of neither, measured in the notes.
// ======================================================================================

/// One side's condition, for a field in `'v` over a plane in `'x`.
[<RequireQualifiedAccess>]
type Boundary<[<Measure>] 'x, [<Measure>] 'v> =
    /// The field holds this value on the side.
    | Dirichlet of float<'v>
    /// The field's outward normal derivative on the side: zero is insulated.
    | Neumann of float<'v / 'x>
    /// The side joins the opposite one; both must say so.
    | Periodic

/// The four sides' conditions.
type Boundaries<[<Measure>] 'x, [<Measure>] 'v> =
    { Left: Boundary<'x, 'v>
      Right: Boundary<'x, 'v>
      Bottom: Boundary<'x, 'v>
      Top: Boundary<'x, 'v> }

/// Boundary conditions for the finite-difference solvers.
[<RequireQualifiedAccess>]
module Boundaries =

    /// One condition on every side.
    let all<[<Measure>] 'x, [<Measure>] 'v> (b: Boundary<'x, 'v>) : Boundaries<'x, 'v> =
        { Left = b; Right = b; Bottom = b; Top = b }

/// A Poisson solve's answer and how it got there.
type PoissonSolution<[<Measure>] 'x, [<Measure>] 'u> =
    { Solution: ScalarGrid<'x, 'x, 'u>
      /// Sweeps taken.
      Iterations: int
      /// The largest `|∇²u − f|` left, over the nodes the solve moves.
      Residual: float<'u / 'x ^ 2>
      /// Whether the residual reached the tolerance before the sweeps ran out.
      Converged: bool }

/// Finite-difference solvers on scalar grids: heat (explicit and implicit), wave and Poisson, with
/// their stability limits.
[<RequireQualifiedAccess>]
module Fdm =

    // ---- The erased geometry -------------------------------------------------------------------

    [<RequireQualifiedAccess>]
    type private Side =
        | D of float
        | N of float
        | P

    type private Geo =
        { Nx: int
          Ny: int
          Hx: float
          Hy: float
          L: Side
          R: Side
          B: Side
          T: Side }

    let private side (b: Boundary<'x, 'v>) =
        match b with
        | Boundary.Dirichlet v -> Side.D(float v)
        | Boundary.Neumann q -> Side.N(float q)
        | Boundary.Periodic -> Side.P

    /// The spacing of a uniform axis, or the refusal naming the worst node.
    let private spacing (name: string) (xs: float[]) =
        if xs.Length < 3 then
            invalidArg "grid" (sprintf "Marey: a grid solver needs at least three nodes on each axis; %s has %d." name xs.Length)

        let h = (xs.[xs.Length - 1] - xs.[0]) / float (xs.Length - 1)
        let mutable worst = 0
        let mutable dev = 0.0

        for k in 1 .. xs.Length - 1 do
            let d = abs ((xs.[k] - xs.[k - 1]) - h) / h

            if d > dev then
                dev <- d
                worst <- k

        if dev > 1e-9 then
            invalidArg
                "grid"
                (sprintf
                    "Marey: the grid solvers need a uniform grid, and %s is not: the gap before node %d is %g against an average of %g. A five-point stencil on uneven nodes is first order there."
                    name worst (xs.[worst] - xs.[worst - 1]) h)

        h

    let private geometry (b: Boundaries<'x, 'v>) (g: ScalarGrid<'x, 'x, 'v>) : Geo =
        let L, R, B, T = side b.Left, side b.Right, side b.Bottom, side b.Top

        let periodic s = (match s with Side.P -> true | _ -> false)

        if periodic L <> periodic R || periodic B <> periodic T then
            invalidArg "boundaries" "Marey: a periodic side joins the opposite one, so both sides of an axis are periodic or neither is."

        { Nx = g.Xs.Length
          Ny = g.Ys.Length
          Hx = spacing "Xs" (g.Xs |> Array.map float)
          Hy = spacing "Ys" (g.Ys |> Array.map float)
          L = L
          R = R
          B = B
          T = T }

    /// The value a node is held at, if a Dirichlet side holds it.
    let private fixedAt (g: Geo) (i: int) (j: int) : float voption =
        match (if i = 0 then g.L elif i = g.Nx - 1 then g.R else Side.P) with
        | Side.D v -> ValueSome v
        | _ ->
            match (if j = 0 then g.B elif j = g.Ny - 1 then g.T else Side.P) with
            | Side.D v -> ValueSome v
            | _ -> ValueNone

    /// The second difference along x at `(i, j)`, times hx², with ghosts for Neumann sides and
    /// wrap-around for periodic ones. A Dirichlet side's node is held, so its value never matters.
    let private dxx (g: Geo) (v: float[]) (i: int) (j: int) =
        let row = j * g.Nx
        let c = v.[row + i]

        let left =
            if i > 0 then v.[row + i - 1]
            else
                match g.L with
                | Side.P -> v.[row + g.Nx - 1]
                | Side.N q -> v.[row + 1] + 2.0 * g.Hx * q
                | Side.D _ -> c

        let right =
            if i < g.Nx - 1 then v.[row + i + 1]
            else
                match g.R with
                | Side.P -> v.[row]
                | Side.N q -> v.[row + g.Nx - 2] + 2.0 * g.Hx * q
                | Side.D _ -> c

        left - 2.0 * c + right

    let private dyy (g: Geo) (v: float[]) (i: int) (j: int) =
        let c = v.[j * g.Nx + i]

        let below =
            if j > 0 then v.[(j - 1) * g.Nx + i]
            else
                match g.B with
                | Side.P -> v.[(g.Ny - 1) * g.Nx + i]
                | Side.N q -> v.[g.Nx + i] + 2.0 * g.Hy * q
                | Side.D _ -> c

        let above =
            if j < g.Ny - 1 then v.[(j + 1) * g.Nx + i]
            else
                match g.T with
                | Side.P -> v.[i]
                | Side.N q -> v.[(g.Ny - 2) * g.Nx + i] + 2.0 * g.Hy * q
                | Side.D _ -> c

        below - 2.0 * c + above

    /// `out = base + scale · ∇²src` on every node a Dirichlet side does not hold, and `held v` on
    /// one it holds at `v`. The interior, where no side reaches, is one loop with no branch: on a
    /// 512² grid the boundary logic in that loop was nineteen times the arithmetic.
    let private fused (g: Geo) (src: float[]) (bas: float[]) (scale: float) (held: float -> float) (out: float[]) =
        let nx = g.Nx
        let ax = scale / (g.Hx * g.Hx)
        let ay = scale / (g.Hy * g.Hy)

        for j in 1 .. g.Ny - 2 do
            let row = j * nx

            for i in 1 .. nx - 2 do
                let n = row + i
                let c = src.[n]
                out.[n] <- bas.[n] + ax * (src.[n - 1] - 2.0 * c + src.[n + 1]) + ay * (src.[n - nx] - 2.0 * c + src.[n + nx])

        let edge (i: int) (j: int) =
            let n = j * nx + i

            out.[n] <-
                match fixedAt g i j with
                | ValueSome v -> held v
                | ValueNone -> bas.[n] + ax * dxx g src i j + ay * dyy g src i j

        for j in 0 .. g.Ny - 1 do
            edge 0 j
            edge (nx - 1) j

        for i in 1 .. nx - 2 do
            edge i 0
            edge i (g.Ny - 1)

    /// The five-point Laplacian of every node; NaN on a held node, where the scheme asks for none.
    let private lap (g: Geo) (v: float[]) : float[] =
        let out = Array.zeroCreate v.Length
        fused g v (Array.zeroCreate v.Length) 1.0 (fun _ -> nan) out
        out

    let inline private plain (xs: float<'u>[]) : float[] = unbox<float[]> (box xs)
    let inline private tagged<[<Measure>] 'u> (xs: float[]) : float<'u>[] = unbox<float<'u>[]> (box xs)

    let private withValues<[<Measure>] 'x, [<Measure>] 'v> (g: ScalarGrid<'x, 'x, 'v>) (values: float[]) : ScalarGrid<'x, 'x, 'v> =
        { Xs = g.Xs
          Ys = g.Ys
          Values = tagged<'v> values }

    /// Every held node at its value.
    let private hold (g: Geo) (v: float[]) =
        for j in 0 .. g.Ny - 1 do
            for i in 0 .. g.Nx - 1 do
                match fixedAt g i j with
                | ValueSome x -> v.[j * g.Nx + i] <- x
                | ValueNone -> ()

    // ---- The operator ----------------------------------------------------------------------------

    /// The compact five-point Laplacian, `(f₍ᵢ₋₁₎ − 2fᵢ + f₍ᵢ₊₁₎)/hx² + (…)/hy²`, on a uniform grid
    /// under `boundaries`, in `'v/'x²`. NaN on a node a Dirichlet side holds. Unlike
    /// `Field.laplacian` it sees the Nyquist mode: a checkerboard of ±1 at unit spacing gives ∓8.
    let laplacian5<[<Measure>] 'x, [<Measure>] 'v> (boundaries: Boundaries<'x, 'v>) (grid: ScalarGrid<'x, 'x, 'v>) : ScalarGrid<'x, 'x, 'v / 'x ^ 2> =
        let g = geometry boundaries grid

        { Xs = grid.Xs
          Ys = grid.Ys
          Values = tagged (lap g (plain grid.Values)) }

    // ---- Heat --------------------------------------------------------------------------------------

    /// The largest step forward Euler is stable at on this grid, `1 / (2α (1/hx² + 1/hy²))`:
    /// `h² / (4α)` on a square grid.
    let explicitLimit<[<Measure>] 'x, [<Measure>] 'v> (alpha: float<'x ^ 2 / s>) (grid: ScalarGrid<'x, 'x, 'v>) : float<s> =
        let hx = spacing "Xs" (grid.Xs |> Array.map float)
        let hy = spacing "Ys" (grid.Ys |> Array.map float)
        1.0 / (2.0 * float alpha * (1.0 / (hx * hx) + 1.0 / (hy * hy))) * 1.0<s>

    let private checkAlpha (alpha: float) =
        if not (alpha > 0.0) || Double.IsInfinity alpha then
            invalidArg "alpha" (sprintf "Marey: a diffusivity must be positive and finite, not %g." alpha)

    /// `∂v/∂t = α ∇²v` by forward Euler on the five-point stencil. A step past `explicitLimit` is
    /// refused with both numbers: forward Euler there is unstable, and the picture it makes is
    /// noise growing from the highest frequency.
    let heat<[<Measure>] 'x, [<Measure>] 'v> (alpha: float<'x ^ 2 / s>) (boundaries: Boundaries<'x, 'v>) : Stepper<ScalarGrid<'x, 'x, 'v>, s> =
        checkAlpha (float alpha)

        fun _ dt grid ->
            let limit = explicitLimit alpha grid

            if dt > limit * (1.0 + 1e-12) then
                invalidArg
                    "dt"
                    (sprintf "Marey: a step of %g s is past forward Euler's limit of %g s on this grid (h²/4α); take a smaller step or Fdm.heatImplicit." (float dt) (float limit))

            let g = geometry boundaries grid
            let v = plain grid.Values
            let out = Array.zeroCreate v.Length
            fused g v v (float alpha * float dt) id out
            withValues grid out

    /// Buffers for the tridiagonal solves along lines of `n` nodes, allocated once a step and reused
    /// for every line: allocating them per line was 36 MB a step on a 512² grid.
    type private Line =
        { A: float[]
          B: float[]
          C: float[]
          R: float[]
          Cp: float[]
          X: float[]
          /// Sherman–Morrison's second right-hand side and solution, for a periodic line.
          U: float[]
          Z: float[]
          Bb: float[] }

    let private line (n: int) =
        { A = Array.zeroCreate n
          B = Array.zeroCreate n
          C = Array.zeroCreate n
          R = Array.zeroCreate n
          Cp = Array.zeroCreate n
          X = Array.zeroCreate n
          U = Array.zeroCreate n
          Z = Array.zeroCreate n
          Bb = Array.zeroCreate n }

    /// Thomas: `a` below the diagonal `b`, `c` above, right-hand side `r`, into `x`.
    let private thomas (n: int) (a: float[]) (b: float[]) (c: float[]) (r: float[]) (cp: float[]) (x: float[]) =
        cp.[0] <- c.[0] / b.[0]
        x.[0] <- r.[0] / b.[0]

        for k in 1 .. n - 1 do
            let m = b.[k] - a.[k] * cp.[k - 1]
            cp.[k] <- c.[k] / m
            x.[k] <- (r.[k] - a.[k] * x.[k - 1]) / m

        for k in n - 2 .. -1 .. 0 do
            x.[k] <- x.[k] - cp.[k] * x.[k + 1]

    /// One implicit half step along a line of `n` nodes, `(I − s·δ²) x = R` with `R` already in
    /// `w.R`: the line's two end conditions, and its held nodes at its ends. The answer is `w.X`.
    let private solveLine (n: int) (s: float) (h: float) (lo: Side) (hi: Side) (w: Line) =
        let a, b, c, r = w.A, w.B, w.C, w.R

        for k in 0 .. n - 1 do
            a.[k] <- -s
            b.[k] <- 1.0 + 2.0 * s
            c.[k] <- -s

        let finish (k: int) (side: Side) (inward: int) =
            match side with
            | Side.N q ->
                // The ghost mirrored through the side: the inward neighbour counts twice.
                if inward > 0 then (a.[k] <- 0.0; c.[k] <- -2.0 * s) else (c.[k] <- 0.0; a.[k] <- -2.0 * s)
                r.[k] <- r.[k] + s * 2.0 * h * q
            | Side.D v ->
                a.[k] <- 0.0
                b.[k] <- 1.0
                c.[k] <- 0.0
                r.[k] <- v
            | Side.P -> ()

        finish 0 lo 1
        finish (n - 1) hi -1

        match lo, hi with
        | Side.P, Side.P ->
            // Cyclic: Sherman–Morrison over Thomas. `a.[0]` is row 0's coefficient on the last
            // unknown, `c.[n−1]` the last row's on the first.
            let alpha = c.[n - 1]
            let beta = a.[0]
            let gamma = -b.[0]
            Array.blit b 0 w.Bb 0 n
            w.Bb.[0] <- b.[0] - gamma
            w.Bb.[n - 1] <- b.[n - 1] - alpha * beta / gamma
            a.[0] <- 0.0
            c.[n - 1] <- 0.0
            thomas n a w.Bb c r w.Cp w.X
            Array.fill w.U 0 n 0.0
            w.U.[0] <- gamma
            w.U.[n - 1] <- alpha
            thomas n a w.Bb c w.U w.Cp w.Z
            let fact = (w.X.[0] + beta * w.X.[n - 1] / gamma) / (1.0 + w.Z.[0] + beta * w.Z.[n - 1] / gamma)

            for k in 0 .. n - 1 do
                w.X.[k] <- w.X.[k] - fact * w.Z.[k]
        | _ ->
            a.[0] <- 0.0
            c.[n - 1] <- 0.0
            thomas n a b c r w.Cp w.X

    /// `∂v/∂t = α ∇²v` by Crank–Nicolson, split by Peaceman–Rachford ADI into a tridiagonal solve
    /// along every row and then every column. Unconditionally stable and second order in time.
    /// It damps the highest frequencies weakly, so data with a jump rings for a while; a few
    /// steps of a smaller size first, or of `heat`, smooth that out.
    let heatImplicit<[<Measure>] 'x, [<Measure>] 'v> (alpha: float<'x ^ 2 / s>) (boundaries: Boundaries<'x, 'v>) : Stepper<ScalarGrid<'x, 'x, 'v>, s> =
        checkAlpha (float alpha)

        fun _ dt grid ->
            if not (dt > 0.0<s>) then
                invalidArg "dt" (sprintf "Marey: a step must be positive, not %g s." (float dt))

            let g = geometry boundaries grid
            let nx, ny = g.Nx, g.Ny
            let v = Array.copy (plain grid.Values)
            hold g v
            let r = float alpha * float dt
            let sx = r / (2.0 * g.Hx * g.Hx)
            let sy = r / (2.0 * g.Hy * g.Hy)
            let half = Array.zeroCreate v.Length
            let rowLine = line nx
            let colLine = line ny

            // A row or column every node of which a Dirichlet side holds is copied, not solved.
            let heldRow (j: int) = (j = 0 && (match g.B with Side.D _ -> true | _ -> false)) || (j = ny - 1 && (match g.T with Side.D _ -> true | _ -> false))
            let heldCol (i: int) = (i = 0 && (match g.L with Side.D _ -> true | _ -> false)) || (i = nx - 1 && (match g.R with Side.D _ -> true | _ -> false))

            // Implicit in x, explicit in y.
            for j in 0 .. ny - 1 do
                let row = j * nx

                if heldRow j then
                    Array.blit v row half row nx
                else
                    for i in 0 .. nx - 1 do
                        let n = row + i

                        rowLine.R.[i] <-
                            if j > 0 && j < ny - 1 then v.[n] + sy * (v.[n - nx] - 2.0 * v.[n] + v.[n + nx])
                            else v.[n] + sy * dyy g v i j

                    solveLine nx sx g.Hx g.L g.R rowLine
                    Array.blit rowLine.X 0 half row nx

            // Implicit in y, explicit in x.
            let out = Array.zeroCreate v.Length

            for i in 0 .. nx - 1 do
                if heldCol i then
                    for j in 0 .. ny - 1 do
                        out.[j * nx + i] <- half.[j * nx + i]
                else
                    for j in 0 .. ny - 1 do
                        let n = j * nx + i

                        colLine.R.[j] <-
                            if i > 0 && i < nx - 1 then half.[n] + sx * (half.[n - 1] - 2.0 * half.[n] + half.[n + 1])
                            else half.[n] + sx * dxx g half i j

                    solveLine ny sy g.Hy g.B g.T colLine

                    for j in 0 .. ny - 1 do
                        out.[j * nx + i] <- colLine.X.[j]

            withValues grid out

    // ---- Wave ------------------------------------------------------------------------------------

    /// The largest step leapfrog is stable at for speed `c`, `1 / (c √(1/hx² + 1/hy²))`: `h / (c √2)`
    /// on a square grid.
    let waveLimit<[<Measure>] 'x, [<Measure>] 'v> (c: float<'x / s>) (grid: ScalarGrid<'x, 'x, 'v>) : float<s> =
        let hx = spacing "Xs" (grid.Xs |> Array.map float)
        let hy = spacing "Ys" (grid.Ys |> Array.map float)
        1.0 / (float c * sqrt (1.0 / (hx * hx) + 1.0 / (hy * hy))) * 1.0<s>

    /// `∂²v/∂t² = c² ∇²v`, the displacement and its rate, by leapfrog (kick–drift–kick, as
    /// `Ode.verlet`): second order, and its energy stays in a band under the CFL limit, past which
    /// a step is refused with both numbers. A held node holds still.
    let wave<[<Measure>] 'x, [<Measure>] 'v>
        (c: float<'x / s>)
        (boundaries: Boundaries<'x, 'v>)
        : Stepper<struct (ScalarGrid<'x, 'x, 'v> * ScalarGrid<'x, 'x, 'v / s>), s> =
        if not (float c > 0.0) || Double.IsInfinity(float c) then
            invalidArg "c" (sprintf "Marey: a wave speed must be positive and finite, not %g." (float c))

        fun _ dt (struct (u, v)) ->
            let limit = waveLimit c u

            if dt > limit * (1.0 + 1e-12) then
                invalidArg "dt" (sprintf "Marey: a step of %g s is past the CFL limit of %g s on this grid (h/(c√2)); take a smaller step." (float dt) (float limit))

            let g = geometry boundaries u
            let c2 = float c * float c
            let half = 0.5 * float dt
            let u0 = plain u.Values
            let vh = Array.zeroCreate u0.Length
            fused g u0 (plain v.Values) (half * c2) (fun _ -> 0.0) vh
            let u1 = Array.init u0.Length (fun n -> u0.[n] + float dt * vh.[n])
            hold g u1
            let v1 = Array.zeroCreate u0.Length
            fused g u1 vh (half * c2) (fun _ -> 0.0) v1
            struct (withValues u u1, { Xs = v.Xs; Ys = v.Ys; Values = tagged v1 })

    // ---- Poisson ---------------------------------------------------------------------------------

    /// `∇²u = f` by successive over-relaxation at the optimal factor for a rectangle, sweeping
    /// until the largest `|∇²u − f|` is at most `tolerance · max |f|` or `maxSweeps` have run, from
    /// zero. A static solve, not a step. At least one side must be Dirichlet: with none, `u` is
    /// fixed only up to a constant.
    let poisson<[<Measure>] 'x, [<Measure>] 'u>
        (boundaries: Boundaries<'x, 'u>)
        (tolerance: float)
        (maxSweeps: int)
        (f: ScalarGrid<'x, 'x, 'u / 'x ^ 2>)
        : PoissonSolution<'x, 'u> =
        let shape: ScalarGrid<'x, 'x, 'u> = { Xs = f.Xs; Ys = f.Ys; Values = Array.zeroCreate f.Values.Length }
        let g = geometry boundaries shape

        let dirichlet s = (match s with Side.D _ -> true | _ -> false)

        if not (dirichlet g.L || dirichlet g.R || dirichlet g.B || dirichlet g.T) then
            invalidArg "boundaries" "Marey: a Poisson problem with no Dirichlet side fixes its solution only up to a constant; hold one side."

        if not (tolerance > 0.0) then
            invalidArg "tolerance" "Marey: the tolerance must be positive."

        let rhs = plain f.Values
        let u = Array.zeroCreate rhs.Length
        hold g u
        let ax = 1.0 / (g.Hx * g.Hx)
        let ay = 1.0 / (g.Hy * g.Hy)
        let rho = (ax * cos (Math.PI / float (g.Nx - 1)) + ay * cos (Math.PI / float (g.Ny - 1))) / (ax + ay)
        let omega = 2.0 / (1.0 + sqrt (1.0 - rho * rho))
        let scale = max 1e-300 (rhs |> Array.fold (fun m x -> max m (abs x)) 0.0)

        let residual () =
            let mutable worst = 0.0

            for j in 0 .. g.Ny - 1 do
                for i in 0 .. g.Nx - 1 do
                    match fixedAt g i j with
                    | ValueSome _ -> ()
                    | ValueNone -> worst <- max worst (abs (ax * dxx g u i j + ay * dyy g u i j - rhs.[j * g.Nx + i]))

            worst

        let mutable sweeps = 0
        let mutable res = residual ()

        while res > tolerance * scale && sweeps < maxSweeps do
            for j in 0 .. g.Ny - 1 do
                for i in 0 .. g.Nx - 1 do
                    match fixedAt g i j with
                    | ValueSome _ -> ()
                    | ValueNone ->
                        let n = j * g.Nx + i
                        let c = u.[n]
                        // dxx and dyy include −2c; add it back for the neighbours' sum.
                        let neighbours = ax * (dxx g u i j + 2.0 * c) + ay * (dyy g u i j + 2.0 * c)
                        let gs = (neighbours - rhs.[n]) / (2.0 * (ax + ay))
                        u.[n] <- (1.0 - omega) * c + omega * gs

            sweeps <- sweeps + 1
            res <- residual ()

        { Solution = withValues shape u
          Iterations = sweeps
          Residual = LanguagePrimitives.FloatWithMeasure res
          Converged = res <= tolerance * scale }
