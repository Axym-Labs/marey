/// M5.6 phase 1 — Stam's stable fluids on the CPU. Gates 1 and 2 of `m5.6-plan.md`; gate 3,
/// the units, is with the other compile gates in `SimTests`.
///
/// **The projection is held to its solver.** On a staggered grid the divergence of a pressure
/// gradient is the five-point Laplacian, so what a projection leaves is only the multigrid's
/// residual, and that falls with every V-cycle. The collocated grid's floor (8.8% on 64², in the
/// M5.6 inventory) is the mutation this gate exists for.
module Marey.Tests.FluidTests

open System
open Xunit
open Marey
open Marey.UnitsOfMeasure

let private rms (g: ScalarGrid<m, m, 1 / s>) =
    sqrt (g.Values |> Array.averageBy (fun v -> float v * float v))

/// A smooth field with flow through the walls and divergence everywhere.
let private divergent (edge: FluidEdge) (n: int) : Fluid<m, 1> =
    Fluid.box edge edge (1.0<m> / float n) n n
    |> Fluid.withVelocity (fun x y ->
        let k = 2.0 * Math.PI
        struct ((sin (k * float x) * cos (2.0 * k * float y) + 0.3) * 1.0<m/s>, (cos (k * float x) * sin (k * float y) + 0.2 * float x) * 1.0<m/s>))

/// Two counter-rotating vortices and a dye blob below them, in a closed box.
let internal vortexPair (n: int) : Fluid<m, 1> =
    Fluid.box FluidEdge.Wall FluidEdge.Wall (1.0<m> / float n) n n
    |> Fluid.withVelocity (fun x y ->
        let g cx cy = exp (-((float x - cx) ** 2.0 + (float y - cy) ** 2.0) / 0.0064)
        let a, b = g 0.4 0.5, g 0.6 0.5
        struct (20.0 * (-(float y - 0.5) * a + (float y - 0.5) * b) * 1.0<m/s>, 20.0 * ((float x - 0.4) * a - (float x - 0.6) * b) * 1.0<m/s>))
    |> Fluid.withDye (fun x y -> exp (-((float x - 0.5) ** 2.0 + (float y - 0.3) ** 2.0) / 0.01))

// ======================================================================================
// Gate 1 — the projection
// ======================================================================================

module ``a projection leaves only its solver's residual`` =

    /// A step of zero length is the projection alone: advection by nothing, forcing for no time.
    let private projected (cycles: int) (f: Fluid<m, 1>) =
        Stam.step { Stam.defaults<m, 1> with Cycles = cycles } 0.0<s> 0.0<s> f

    [<Theory>]
    [<InlineData(true)>]
    [<InlineData(false)>]
    let ``every V-cycle cuts the divergence, to 1e-6 by six`` (walls: bool) =
        let f = divergent (if walls then FluidEdge.Wall else FluidEdge.Periodic) 64
        let start = rms (Fluid.divergence f)
        let left = [ 1..6 ] |> List.map (fun c -> rms (Fluid.divergence (projected c f)) / start)

        // Measured: 0.048, 0.0029, 2.0e-4, 1.5e-5, …, 8.7e-8 with walls; 0.089 … 4.1e-7 periodic.
        // A collocated projection stops near 0.09 however many cycles run.
        for a, b in List.pairwise left do
            Assert.True(b < 0.2 * a, sprintf "a cycle cut the divergence only from %g to %g" a b)

        Assert.True(List.last left < 1e-6, sprintf "six cycles left %g of the divergence" (List.last left))

    [<Fact>]
    let ``a divergence-free field is a fixed point`` () =
        let f = projected 12 (divergent FluidEdge.Wall 64)
        let again = projected 2 f
        let scale = f.UFaces |> Array.map (fun v -> abs (float v)) |> Array.max

        let moved =
            Array.append (Array.map2 (fun a b -> abs (float a - float b)) f.UFaces again.UFaces) (Array.map2 (fun a b -> abs (float a - float b)) f.VFaces again.VFaces)
            |> Array.max

        Assert.True(moved < 1e-9 * scale, sprintf "a projected field moved by %g of its maximum" (moved / scale))

    [<Fact>]
    let ``warm-started, two cycles hold a running flow divergence-free`` () =
        let step = Stam.step { Stam.defaults<m, 1> with Buoyancy = 1.0<m/s^2> }
        let mutable f = vortexPair 128
        let scale = (f.UFaces |> Array.map (fun v -> abs (float v)) |> Array.max) / float f.Cell
        let mutable worst = 0.0

        for k in 1..100 do
            f <- step (float k * 0.005<s>) 0.005<s> f
            // The first step projects a field that was sampled, not solved for.
            if k > 1 then
                worst <- max worst (rms (Fluid.divergence f) / scale)

        // Measured on 256², 200 steps: 1.2e-7.
        Assert.True(worst < 1e-6, sprintf "the divergence reached %g of |u|/H" worst)

// ======================================================================================
// Gate 2 — the physics
// ======================================================================================

module ``a fluid moves as the equations say`` =

    /// A Taylor–Green vortex on a periodic box decays as exp(−2νk²t). Small, so that advection's
    /// own smoothing is negligible beside the viscosity.
    [<Fact>]
    let ``a Taylor–Green vortex decays at its viscous rate`` () =
        let k = 2.0 * Math.PI / 1.0<m>
        let nu = 1e-3<m^2/s>
        let amp = 1e-4<m/s>

        let f0 =
            Fluid.box FluidEdge.Periodic FluidEdge.Periodic (1.0<m> / 64.0) 64 64
            |> Fluid.withVelocity (fun x y -> struct (amp * sin (k * x) * cos (k * y), -amp * cos (k * x) * sin (k * y)))

        let step = Stam.step { Stam.defaults<m, 1> with Viscosity = nu; Sweeps = 20 }
        let f = Sim.ofStep 0.1<s> step f0 |> Sim.run 100
        let peak (g: Fluid<m, 1>) = g.UFaces |> Array.map abs |> Array.max
        let expected = exp (-2.0 * nu * k * k * 10.0<s>)
        let ratio = (peak f / peak f0) / expected

        // Measured 1.0035: implicit Euler damps a little less than the exponential.
        Assert.True(abs (ratio - 1.0) < 0.01, sprintf "decayed to %g of the exponential's rate" ratio)

    [<Fact>]
    let ``dye moves at the flow's speed`` () =
        let f0 =
            Fluid.box FluidEdge.Periodic FluidEdge.Periodic (1.0<m> / 64.0) 64 64
            |> Fluid.withVelocity (fun _ _ -> struct (0.3<m/s>, 0.1<m/s>))
            |> Fluid.withDye (fun x y -> exp (-((float x - 0.3) ** 2.0 + (float y - 0.3) ** 2.0) / 0.01))

        let centroid (f: Fluid<m, 1>) =
            let g = Fluid.dye f
            let total = g.Values |> Array.sumBy float
            let mx = Array.init g.Values.Length (fun k -> float g.Values.[k] * float g.Xs.[k % 64]) |> Array.sum
            let my = Array.init g.Values.Length (fun k -> float g.Values.[k] * float g.Ys.[k / 64]) |> Array.sum
            mx / total, my / total

        let f = Sim.ofStep 0.05<s> (Stam.step Stam.defaults<m, 1>) f0 |> Sim.run 20
        let x0, y0 = centroid f0
        let x1, y1 = centroid f
        Assert.Equal(0.3, x1 - x0, 6)
        Assert.Equal(0.1, y1 - y0, 6)

    [<Fact>]
    let ``warm dye rises and the walls hold`` () =
        let f0 =
            Fluid.box FluidEdge.Wall FluidEdge.Wall (1.0<m> / 48.0) 48 48
            |> Fluid.withDye (fun x y -> if abs (float x - 0.5) < 0.1 && float y < 0.2 then 1.0 else 0.0)

        let step = Stam.step { Stam.defaults<m, 1> with Buoyancy = 2.0<m/s^2> }
        let f = Sim.ofStep 0.02<s> step f0 |> Sim.run 40
        let height (g: Fluid<m, 1>) =
            let d = Fluid.dye g
            (Array.init d.Values.Length (fun k -> float d.Values.[k] * float d.Ys.[k / 48]) |> Array.sum) / (d.Values |> Array.sumBy float)

        Assert.True(height f > height f0 + 0.05, sprintf "the dye's height went from %g to %g" (height f0) (height f))

        for j in 0..47 do
            Assert.Equal(0.0<m/s>, f.UFaces.[j * 49])
            Assert.Equal(0.0<m/s>, f.UFaces.[j * 49 + 48])

        for i in 0..47 do
            Assert.Equal(0.0<m/s>, f.VFaces.[i])
            Assert.Equal(0.0<m/s>, f.VFaces.[48 * 48 + i])

    [<Fact>]
    let ``a periodic edge's last faces repeat its first`` () =
        let f = Sim.ofStep 0.01<s> (Stam.step Stam.defaults<m, 1>) (divergent FluidEdge.Periodic 32) |> Sim.run 5

        for j in 0..31 do
            Assert.Equal(f.UFaces.[j * 33], f.UFaces.[j * 33 + 32])

        for i in 0..31 do
            Assert.Equal(f.VFaces.[i], f.VFaces.[32 * 32 + i])

    [<Fact>]
    let ``a step leaves its input as it was`` () =
        let f = vortexPair 32
        let copies = Array.copy f.UFaces, Array.copy f.VFaces, Array.copy f.Dye, Array.copy f.Pressure
        Stam.step { Stam.defaults<m, 1> with Viscosity = 1e-3<m^2/s>; Diffusion = 1e-3<m^2/s> } 0.0<s> 0.01<s> f |> ignore
        Assert.Equal((f.UFaces, f.VFaces, f.Dye, f.Pressure), copies)

    [<Fact>]
    let ``a seek through a fluid's trajectory is the run`` () =
        let sim = Sim.ofStep 0.01<s> (Stam.step { Stam.defaults<m, 1> with Buoyancy = 1.0<m/s^2> }) (vortexPair 32)
        let run = Sim.states 30 sim
        let traj = Sim.trajectory 0.1<s> 0.3<s> sim

        let seeks = [| 7; 13; 21; 30 |] |> Array.Parallel.map (fun k -> k, traj.At(float k * 0.01<s>))

        for k, s in seeks do
            Assert.True((s.UFaces = run.[k].UFaces && s.VFaces = run.[k].VFaces && s.Dye = run.[k].Dye), sprintf "the seek to step %d differs from the run" k)

    [<Fact>]
    let ``a box of fewer than two cells or a cell of no size is refused`` () =
        Assert.Throws<ArgumentException>(fun () -> Fluid.box<m, 1> FluidEdge.Wall FluidEdge.Wall 0.1<m> 1 4 |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> Fluid.box<m, 1> FluidEdge.Wall FluidEdge.Wall 0.0<m> 4 4 |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> Stam.step { Stam.defaults<m, 1> with Cycles = 0 } |> ignore) |> ignore

// ======================================================================================
// Gates 4 and 5 — lattice Boltzmann (phase 2)
// ======================================================================================

module ``a lattice flows as the equations say`` =

    let private dx = 1e-3<m>
    let private dt = 1e-3<s>

    /// Plane Poiseuille flow: a channel between two walls, periodic along it, driven by a body
    /// force, settles to `u = a y (H − y) / 2ν` with the walls half a cell outside the edge cells.
    [<Theory>]
    [<InlineData(0.6)>]
    [<InlineData(0.8)>]
    [<InlineData(1.2)>]
    let ``Poiseuille flow reaches its parabola within 1%`` (tau: float) =
        let ny = 32
        let nu = (tau - 0.5) / 3.0 * dx * dx / dt
        let a = 1e-5<m/s^2>
        let options = { Lbm.defaults nu with Force = (a, 0.0<m/s^2>); Bottom = LatticeEdge.Wall; Top = LatticeEdge.Wall }
        let l = Sim.ofStep dt (Lbm.step options) (Lattice.create dx dt 4 ny) |> Sim.run 40_000
        let v = Lattice.velocityUnder options.Force l
        let height = float ny * dx
        let exact (j: int) = let y = (float j + 0.5) * dx in a / (2.0 * nu) * y * (height - y)
        let peak = exact (ny / 2)
        let worst = [ 0 .. ny - 1 ] |> List.map (fun j -> abs (v.U.[j * 4] - exact j)) |> List.max

        // Measured: 0.067%, 0.027%, 0.34%.
        Assert.True(worst < 0.01 * peak, sprintf "the profile is %g of its peak from the parabola" (worst / peak))

    [<Fact>]
    let ``a closed channel keeps its mass to 1e-10`` () =
        let l0 =
            Lattice.create dx dt 100 30
            |> Lattice.withObstacle (fun x y -> (x - 0.03<m>) * (x - 0.03<m>) + (y - 0.016<m>) * (y - 0.016<m>) <= 0.005<m> * 0.005<m>)

        let options = { Lbm.defaults (0.02 * dx * dx / dt) with Force = (2e-3<m/s^2>, 1e-3<m/s^2>); Bottom = LatticeEdge.Wall; Top = LatticeEdge.Wall }
        let l = Sim.ofStep dt (Lbm.step options) l0 |> Sim.run 10_000
        let drift = abs ((Lattice.mass l - Lattice.mass l0) / Lattice.mass l0)
        let moving = (Lattice.velocityUnder options.Force l).U |> Array.exists (fun u -> u > 1e-3<m/s>)

        // Measured on 200 × 50: 8.6e-15.
        Assert.True(drift < 1e-10, sprintf "the mass drifted by %g" drift)
        Assert.True(moving, "the force moved nothing, so the mass was never at risk")

    [<Fact>]
    let ``an obstacle's cells hold no velocity and an inflow fills the channel`` () =
        let u = 0.05<m/s>

        let l0 =
            Lattice.create dx dt 60 20
            |> Lattice.withObstacle (fun x y -> abs (x - 0.02<m>) < 0.003<m> && abs (y - 0.01<m>) < 0.003<m>)

        let options = { Lbm.defaults (0.05 * dx * dx / dt) with Left = LatticeEdge.Inflow(u, 0.0<m/s>); Right = LatticeEdge.Outflow; Bottom = LatticeEdge.Wall; Top = LatticeEdge.Wall }
        let l = Sim.ofStep dt (Lbm.step options) l0 |> Sim.run 3000
        let v = Lattice.velocity l

        for k in 0 .. l.Solid.Length - 1 do
            if l.Solid.[k] then Assert.True(Double.IsNaN(float v.U.[k]))

        // The column after the inflow carries its flux, the walls' boundary layers aside.
        let mid = [ 5..14 ] |> List.averageBy (fun j -> float v.U.[j * 60 + 1])
        Assert.True(abs (mid - float u) < 0.05 * float u, sprintf "the inflow column moves at %g against %g" mid (float u))

    [<Fact>]
    let ``a seek through a lattice's trajectory is the run`` () =
        let l0 = Lattice.create dx dt 24 12 |> Lattice.withVelocity (fun x _ -> struct (0.02<m/s> * sin (float x * 300.0), 0.01<m/s>))
        let sim = Sim.ofStep dt (Lbm.step (Lbm.defaults (0.1 * dx * dx / dt))) l0
        let run = Sim.states 40 sim
        let traj = Sim.trajectory 0.01<s> 0.04<s> sim

        for k, s in [| 7; 19; 33; 40 |] |> Array.Parallel.map (fun k -> k, traj.At(float k * dt)) do
            Assert.True((s.Populations = run.[k].Populations), sprintf "the seek to step %d differs from the run" k)

module ``a lattice refuses what it cannot do`` =

    let private dx = 1e-3<m>
    let private dt = 1e-3<s>
    let private l = Lattice.create dx dt 8 8

    let private refused (options: LbmOptions<m>) (h: float<s>) (fragments: string list) =
        let e = Assert.Throws<ArgumentException>(fun () -> Lbm.step options 0.0<s> h l |> ignore)

        for f in fragments do
            Assert.Contains(f, e.Message)

    [<Fact>]
    let ``no viscosity, with tau`` () =
        refused (Lbm.defaults 0.0<m^2/s>) dt [ "τ = 0.5"; "τ > ½" ]

    [<Fact>]
    let ``an inflow past the lattice's Mach bound, with both speeds`` () =
        refused { Lbm.defaults 1e-4<m^2/s> with Left = LatticeEdge.Inflow(0.4<m/s>, 0.0<m/s>); Right = LatticeEdge.Outflow } dt [ "0.4"; "0.3" ]

    [<Fact>]
    let ``a step that is not the tick, with both`` () =
        refused (Lbm.defaults 1e-4<m^2/s>) 2e-3<s> [ "0.001 s"; "0.002 s" ]

    [<Fact>]
    let ``periodic on one side of an axis only`` () =
        refused { Lbm.defaults 1e-4<m^2/s> with Left = LatticeEdge.Wall } dt [ "periodic" ]
