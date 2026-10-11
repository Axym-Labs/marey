namespace Marey

open System
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// D149 — LATTICE BOLTZMANN, D2Q9.
//
// A fluid as nine populations per cell moving along the lattice's links: each step they collide
// towards a local equilibrium (BGK) and stream to the neighbour they point at. Weakly
// compressible, explicit, and every cell independent within a step, which is what a GPU wants.
//
// **Physical units through two numbers.** The lattice has a spacing Δx and a tick Δt, and a
// population encodes a lattice velocity u·Δt/Δx, so the tick is part of the state and a step of
// any other length is refused. The relaxation time follows from the viscosity, τ = ½ + 3νΔt/Δx²;
// τ ≤ ½ (no viscosity) is refused, and so is an inflow faster than 0.3 of the lattice's speed,
// where the scheme's compressibility error stops being small. Both with the numbers.
//
// **Deviation form.** A population is stored and computed as fᵢ − wᵢ, its departure from rest,
// and the density as 1 + Σ(fᵢ − wᵢ). Every sum is then over small numbers. In float32 (the GPU)
// the raw form drifts mass by 6 × 10⁻⁴ over 50 000 steps, systematically, and this form holds it
// at a rounding walk below 1.4 × 10⁻⁷ (measured in the M5.6 inventory). The CPU computes the same
// expressions in float64, so it is the GPU's oracle.
//
// **Pull streaming, fused.** A cell gathers each population from the neighbour it comes from,
// collides, and stores; the stored state is after collision. A population whose source is solid
// bounces back halfway (no slip). An edge is periodic, a wall (bounce-back), an inflow, or an
// outflow. An inflow sends in the equilibrium at its velocity and the edge cell's density, so it
// fixes the velocity and lets the pressure float; an outflow the equilibrium at the reference
// density and the edge cell's velocity, so it fixes the pressure and lets the velocity float. The
// pair drives a channel from rest to its inflow; an inflow at the reference density with a copying
// outflow reached 0.014 of 0.05 m/s in three seconds, measured. A population crossing two edges at
// a corner takes the first of wall, inflow, outflow, periodic that either edge is.
//
// **A body force** by shifting the equilibrium's velocity by τF/ρ; the fluid's velocity is read
// as (Σcᵢfᵢ + F/2)/ρ.
// ======================================================================================

/// One side of a lattice.
[<RequireQualifiedAccess>]
type LatticeEdge<[<Measure>] 'x> =
    /// Joined to the opposite side, which must say so too.
    | Periodic
    /// A wall half a cell outside the edge cells: no slip.
    | Wall
    /// Fluid arriving at this velocity, at whatever density the edge cell has.
    | Inflow of float<'x / s> * float<'x / s>
    /// Fluid leaving at the reference density, at whatever velocity the edge cell has.
    | Outflow

/// A lattice of `Nx × Ny` cells of side `Spacing`, its lower-left corner at `(X0, Y0)`, stepped
/// every `Tick`. `Populations` is nine planes of `Nx × Ny`, population-major, rows x fastest, each
/// population's departure from rest, `fᵢ − wᵢ`, in lattice units. `Solid` marks obstacle cells.
///
/// The populations run east, north, west, south, then north-east, north-west, south-west,
/// south-east, after the rest population.
type Lattice<[<Measure>] 'x> =
    { Nx: int
      Ny: int
      Spacing: float<'x>
      Tick: float<s>
      X0: float<'x>
      Y0: float<'x>
      Populations: float[]
      Solid: bool[] }

/// What drives a lattice: its viscosity, a body force, and its four sides.
type LbmOptions<[<Measure>] 'x> =
    { Viscosity: float<'x ^ 2 / s>
      /// An acceleration on every fluid cell.
      Force: float<'x / s ^ 2> * float<'x / s ^ 2>
      Left: LatticeEdge<'x>
      Right: LatticeEdge<'x>
      Bottom: LatticeEdge<'x>
      Top: LatticeEdge<'x> }

[<RequireQualifiedAccess>]
module Lattice =

    /// The links: x and y of each population, its weight, and the population opposite it.
    let internal cx = [| 0; 1; 0; -1; 0; 1; -1; -1; 1 |]
    let internal cy = [| 0; 0; 1; 0; -1; 1; 1; -1; -1 |]
    let internal w = [| 4.0 / 9.0; 1.0 / 9.0; 1.0 / 9.0; 1.0 / 9.0; 1.0 / 9.0; 1.0 / 36.0; 1.0 / 36.0; 1.0 / 36.0; 1.0 / 36.0 |]
    let internal opp = [| 0; 3; 4; 1; 2; 7; 8; 5; 6 |]

    /// The equilibrium's departure from rest for population `i`, at a density departure `dr`
    /// (the density is `1 + dr`) and a lattice velocity.
    let inline internal equilibrium (i: int) (dr: float) (ux: float) (uy: float) =
        let cu = 3.0 * (float cx.[i] * ux + float cy.[i] * uy)
        w.[i] * (dr + (1.0 + dr) * (cu + 0.5 * cu * cu - 1.5 * (ux * ux + uy * uy)))

    /// A lattice at rest at the reference density, with no obstacle, its corner at the origin.
    let create<[<Measure>] 'x> (spacing: float<'x>) (tick: float<s>) (nx: int) (ny: int) : Lattice<'x> =
        if nx < 2 || ny < 2 then
            invalidArg "nx" (sprintf "Marey: a lattice needs at least 2 × 2 cells; got %d × %d." nx ny)

        if not (spacing > LanguagePrimitives.FloatWithMeasure 0.0) || Double.IsInfinity(float spacing) then
            invalidArg "spacing" (sprintf "Marey: a lattice's spacing must be positive and finite; got %g." (float spacing))

        if not (tick > 0.0<s>) || Double.IsInfinity(float tick) then
            invalidArg "tick" (sprintf "Marey: a lattice's tick must be positive and finite; got %g s." (float tick))

        { Nx = nx
          Ny = ny
          Spacing = spacing
          Tick = tick
          X0 = LanguagePrimitives.FloatWithMeasure 0.0
          Y0 = LanguagePrimitives.FloatWithMeasure 0.0
          Populations = Array.zeroCreate (9 * nx * ny)
          Solid = Array.zeroCreate (nx * ny) }

    /// The lattice moved so its lower-left corner is at `(x0, y0)`.
    let withOrigin<[<Measure>] 'x> (x0: float<'x>) (y0: float<'x>) (l: Lattice<'x>) : Lattice<'x> = { l with X0 = x0; Y0 = y0 }

    let private centreX<[<Measure>] 'x> (l: Lattice<'x>) (i: int) = l.X0 + (float i + 0.5) * l.Spacing
    let private centreY<[<Measure>] 'x> (l: Lattice<'x>) (j: int) = l.Y0 + (float j + 0.5) * l.Spacing

    /// Every cell whose centre is inside the obstacle made solid, in addition to those that are.
    let withObstacle<[<Measure>] 'x> (inside: float<'x> -> float<'x> -> bool) (l: Lattice<'x>) : Lattice<'x> =
        { l with Solid = Array.init (l.Nx * l.Ny) (fun k -> l.Solid.[k] || inside (centreX l (k % l.Nx)) (centreY l (k / l.Nx))) }

    /// Every cell at the equilibrium of the reference density and this velocity.
    let withVelocity<[<Measure>] 'x> (velocity: float<'x> -> float<'x> -> struct (float<'x / s> * float<'x / s>)) (l: Lattice<'x>) : Lattice<'x> =
        let n = l.Nx * l.Ny
        let scale = float l.Tick / float l.Spacing
        let f = Array.zeroCreate (9 * n)

        for k in 0 .. n - 1 do
            let struct (u, v) = velocity (centreX l (k % l.Nx)) (centreY l (k / l.Nx))

            for i in 0..8 do
                f.[i * n + k] <- equilibrium i 0.0 (float u * scale) (float v * scale)

        { l with Populations = f }

    let private centres<[<Measure>] 'x> (l: Lattice<'x>) = Array.init l.Nx (centreX l), Array.init l.Ny (centreY l)

    /// The density over the reference density at each cell's centre; NaN in an obstacle.
    let density<[<Measure>] 'x> (l: Lattice<'x>) : ScalarGrid<'x, 'x, 1> =
        let n = l.Nx * l.Ny
        let xs, ys = centres l

        { Xs = xs
          Ys = ys
          Values =
            Array.init n (fun k ->
                if l.Solid.[k] then nan
                else
                    let mutable dr = 0.0

                    for i in 0..8 do
                        dr <- dr + l.Populations.[i * n + k]

                    1.0 + dr) }

    /// The velocity of the fluid at each cell's centre under a body force `force` (zero when there
    /// is none, or `options.Force`); NaN in an obstacle.
    let velocityUnder<[<Measure>] 'x> (force: float<'x / s ^ 2> * float<'x / s ^ 2>) (l: Lattice<'x>) : VectorGrid<'x, 'x, 'x / s, 'x / s> =
        let n = l.Nx * l.Ny
        let xs, ys = centres l
        let toLattice = float l.Tick * float l.Tick / float l.Spacing
        let fx, fy = float (fst force) * toLattice, float (snd force) * toLattice
        let back = float l.Spacing / float l.Tick
        let u = Array.zeroCreate n
        let v = Array.zeroCreate n

        for k in 0 .. n - 1 do
            if l.Solid.[k] then
                u.[k] <- nan
                v.[k] <- nan
            else
                let mutable dr = 0.0
                let mutable mx = 0.0
                let mutable my = 0.0

                for i in 0..8 do
                    let h = l.Populations.[i * n + k]
                    dr <- dr + h
                    mx <- mx + float cx.[i] * h
                    my <- my + float cy.[i] * h

                let rho = 1.0 + dr
                u.[k] <- (mx + 0.5 * fx) / rho * back
                v.[k] <- (my + 0.5 * fy) / rho * back

        { Xs = xs
          Ys = ys
          U = Fluid.tagged u
          W = Fluid.tagged v }

    /// The velocity of the fluid at each cell's centre, with no body force; NaN in an obstacle.
    let velocity<[<Measure>] 'x> (l: Lattice<'x>) : VectorGrid<'x, 'x, 'x / s, 'x / s> =
        velocityUnder (LanguagePrimitives.FloatWithMeasure 0.0, LanguagePrimitives.FloatWithMeasure 0.0) l

    /// The vorticity at each cell's centre, `∂v/∂x − ∂u/∂y` of `velocity`.
    let vorticity<[<Measure>] 'x> (l: Lattice<'x>) : ScalarGrid<'x, 'x, 1 / s> = Field.curl (velocity l)

    /// The fluid's mass over its reference density, per unit depth: the area it would fill at the
    /// reference density. Conserved by every edge but an inflow or an outflow.
    let mass<[<Measure>] 'x> (l: Lattice<'x>) : float<'x ^ 2> =
        let n = l.Nx * l.Ny
        let mutable total = 0.0

        for k in 0 .. n - 1 do
            if not l.Solid.[k] then
                total <- total + 1.0

                for i in 0..8 do
                    total <- total + l.Populations.[i * n + k]

        total * l.Spacing * l.Spacing

/// D149 — a lattice's step, on the CPU. `GpuSim.lbm` is the same step on the GPU.
[<RequireQualifiedAccess>]
module Lbm =

    /// The relaxation time a viscosity gives on a lattice: `½ + 3νΔt/Δx²`.
    let tau<[<Measure>] 'x> (viscosity: float<'x ^ 2 / s>) (l: Lattice<'x>) : float =
        0.5 + 3.0 * float (viscosity * l.Tick / (l.Spacing * l.Spacing))

    /// The highest lattice velocity an inflow may have: past it the scheme's compressibility error
    /// is no longer small.
    [<Literal>]
    let MaxMach = 0.3

    /// Periodic on every side, no force, and the viscosity given.
    let defaults<[<Measure>] 'x> (viscosity: float<'x ^ 2 / s>) : LbmOptions<'x> =
        { Viscosity = viscosity
          Force = (LanguagePrimitives.FloatWithMeasure 0.0, LanguagePrimitives.FloatWithMeasure 0.0)
          Left = LatticeEdge.Periodic
          Right = LatticeEdge.Periodic
          Bottom = LatticeEdge.Periodic
          Top = LatticeEdge.Periodic }

    /// An edge as the step uses it: 0 periodic, 1 wall, 2 inflow, 3 outflow; and an inflow's
    /// lattice velocity.
    let internal code<[<Measure>] 'x> (scale: float) (e: LatticeEdge<'x>) =
        match e with
        | LatticeEdge.Periodic -> struct (0, 0.0, 0.0)
        | LatticeEdge.Wall -> struct (1, 0.0, 0.0)
        | LatticeEdge.Inflow(u, v) -> struct (2, float u * scale, float v * scale)
        | LatticeEdge.Outflow -> struct (3, 0.0, 0.0)

    /// The checks a step makes before it runs, refusing with the numbers, and the relaxation time
    /// it will use. Public for a step of one's own on these states, as `GpuSim.lbm` is.
    let validate<[<Measure>] 'x> (options: LbmOptions<'x>) (l: Lattice<'x>) (h: float<s>) : float =
        if abs (float h - float l.Tick) > 1e-12 * float l.Tick then
            invalidArg
                "h"
                (sprintf
                    "Marey: this lattice was built for a tick of %g s and was asked to step %g s. A population encodes a lattice velocity u·Δt/Δx, so the tick is fixed once the lattice is."
                    (float l.Tick)
                    (float h))

        let t = tau options.Viscosity l

        if not (t > 0.5) then
            invalidArg
                "options"
                (sprintf
                    "Marey: a viscosity of %g gives a relaxation time τ = %g on this lattice, and BGK needs τ > ½. Raise the viscosity or the tick, or refine the spacing."
                    (float options.Viscosity)
                    t)

        let periodic e = (match e with LatticeEdge.Periodic -> true | _ -> false)

        if periodic options.Left <> periodic options.Right || periodic options.Bottom <> periodic options.Top then
            invalidArg "options" "Marey: a periodic side joins the opposite one, so both sides of an axis are periodic or neither is."

        let scale = float l.Tick / float l.Spacing

        for e in [ options.Left; options.Right; options.Bottom; options.Top ] do
            match e with
            | LatticeEdge.Inflow(u, v) ->
                let mach = sqrt (float u * float u + float v * float v) * scale

                if mach > MaxMach then
                    invalidArg
                        "options"
                        (sprintf
                            "Marey: an inflow of %g moves %g cells a tick on this lattice; past %g the scheme's compressibility error is not small. Shorten the tick or coarsen the spacing."
                            (sqrt (float u * float u + float v * float v))
                            mach
                            MaxMach)
            | _ -> ()

        t

    /// The step: every fluid cell gathers its populations, bounces back those whose source is
    /// solid, takes an edge's populations by its rule, and relaxes towards the equilibrium.
    let step<[<Measure>] 'x> (options: LbmOptions<'x>) : Step<Lattice<'x>, s> =
        fun _ h l ->
            let t = validate options l h
            let nx, ny = l.Nx, l.Ny
            let n = nx * ny
            let scale = float l.Tick / float l.Spacing
            let toLattice = float l.Tick * float l.Tick / float l.Spacing
            let fx = float (fst options.Force) * toLattice
            let fy = float (snd options.Force) * toLattice
            let struct (eL, uL, vL) = code scale options.Left
            let struct (eR, uR, vR) = code scale options.Right
            let struct (eB, uB, vB) = code scale options.Bottom
            let struct (eT, uT, vT) = code scale options.Top
            let src = l.Populations
            let solid = l.Solid
            let out = Array.copy src
            let f = Array.zeroCreate 9

            for j in 0 .. ny - 1 do
                for i in 0 .. nx - 1 do
                    let k = j * nx + i

                    if not solid.[k] then
                        // The edge cell's own density and velocity, which an inflow and an outflow
                        // take half of their populations' equilibrium from.
                        let mutable er = 0.0
                        let mutable eu = 0.0
                        let mutable ev = 0.0

                        if i = 0 || j = 0 || i = nx - 1 || j = ny - 1 then
                            let mutable mx = 0.0
                            let mutable my = 0.0

                            for p in 0..8 do
                                let q = src.[p * n + k]
                                er <- er + q
                                mx <- mx + float Lattice.cx.[p] * q
                                my <- my + float Lattice.cy.[p] * q

                            eu <- mx / (1.0 + er)
                            ev <- my / (1.0 + er)

                        for p in 0..8 do
                            let si = i - Lattice.cx.[p]
                            let sj = j - Lattice.cy.[p]
                            // The edge each crossing leaves through, if any, as (code, u, v).
                            let struct (xe, xu, xv) = if si < 0 then struct (eL, uL, vL) elif si >= nx then struct (eR, uR, vR) else struct (-1, 0.0, 0.0)
                            let struct (ye, yu, yv) = if sj < 0 then struct (eB, uB, vB) elif sj >= ny then struct (eT, uT, vT) else struct (-1, 0.0, 0.0)

                            f.[p] <-
                                if xe = 1 || ye = 1 then
                                    src.[Lattice.opp.[p] * n + k]
                                elif xe = 2 then
                                    Lattice.equilibrium p er xu xv
                                elif ye = 2 then
                                    Lattice.equilibrium p er yu yv
                                elif xe = 3 || ye = 3 then
                                    Lattice.equilibrium p 0.0 eu ev
                                else
                                    let ci = if xe = 0 then (si + nx) % nx else si
                                    let cj = if ye = 0 then (sj + ny) % ny else sj
                                    let s = cj * nx + ci

                                    if solid.[s] then src.[Lattice.opp.[p] * n + k] else src.[p * n + s]

                        let mutable dr = 0.0
                        let mutable mx = 0.0
                        let mutable my = 0.0

                        for p in 0..8 do
                            dr <- dr + f.[p]
                            mx <- mx + float Lattice.cx.[p] * f.[p]
                            my <- my + float Lattice.cy.[p] * f.[p]

                        let rho = 1.0 + dr
                        let ux = (mx + t * fx) / rho
                        let uy = (my + t * fy) / rho

                        for p in 0..8 do
                            out.[p * n + k] <- f.[p] - (f.[p] - Lattice.equilibrium p dr ux uy) / t

            { l with Populations = out }
