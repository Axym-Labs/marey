/// M5.6 phases 3 and 4 — the GPU solvers against their CPU twins (D140). Gates 6, 7 and 8 of
/// `m5.6-plan.md`.
///
/// **A GPU result is never a golden.** Two drivers disagree in the last bits (fused multiply-adds),
/// so each gate holds the GPU to the CPU solver, which computes the same expressions in float64,
/// within a bound stated from measurement, on whatever driver the host has: radeonsi here,
/// llvmpipe in CI. Skipped, and said, where no context can be made.
module Marey.Tests.GpuSimTests

open System
open Xunit
open Marey
open Marey.UnitsOfMeasure

/// The largest difference between two fields, over the larger field's largest magnitude.
let private relative (cpu: float<'u>[]) (gpu: float<'u>[]) =
    let scale = cpu |> Array.map (fun v -> abs (float v)) |> Array.max
    let worst = Array.map2 (fun (a: float<'u>) (b: float<'u>) -> abs (float a - float b)) cpu gpu |> Array.max
    worst / scale

let private bits (xs: float<'u>[]) = xs |> Array.map (fun v -> BitConverter.DoubleToInt64Bits(float v))

// ======================================================================================
// Gate 6 — Stam on the GPU agrees with Stam on the CPU
// ======================================================================================

module ``Stam on the GPU is Stam on the CPU`` =

    let private options =
        { Stam.defaults<m, 1> with
            Viscosity = 1e-4<m^2/s>
            Diffusion = 1e-4<m^2/s>
            Buoyancy = 1.0<m/s^2>
            Source = fun x y -> if abs (float x - 0.5) < 0.05 && float y < 0.1 then 1.0</s> else 0.0</s> }

    let private pair (edge: FluidEdge) =
        let f = FluidTests.vortexPair 128
        { f with XEdge = edge; YEdge = edge } |> Fluid.withVelocity (fun x y ->
            let g cx cy = exp (-((float x - cx) ** 2.0 + (float y - cy) ** 2.0) / 0.0064)
            let a, b = g 0.4 0.5, g 0.6 0.5
            struct (20.0 * (-(float y - 0.5) * a + (float y - 0.5) * b) * 1.0<m/s>, 20.0 * ((float x - 0.4) * a - (float x - 0.6) * b) * 1.0<m/s>))

    [<Theory>]
    [<InlineData(true)>]
    [<InlineData(false)>]
    let ``after 200 steps every cell is within 1e-3 of the field's largest`` (walls: bool) =
        GpuTests.withGpu (fun gpu ->
            let f0 = pair (if walls then FluidEdge.Wall else FluidEdge.Periodic)
            let cpu = Sim.ofStep 0.005<s> (Stam.step options) f0 |> Sim.run 200
            let gpuRun = Sim.ofStep 0.005<s> (GpuSim.stam gpu options) f0 |> Sim.run 200

            // Measured in the inventory's spike, collocated and without viscosity: 1.1e-4.
            for name, e in [ "u", relative cpu.UFaces gpuRun.UFaces; "v", relative cpu.VFaces gpuRun.VFaces; "dye", relative cpu.Dye gpuRun.Dye ] do
                Console.WriteLine(sprintf "Stam GPU against CPU, %s, %s: %.2e (%s)" (if walls then "walls" else "periodic") name e gpu.Description)
                Assert.True(e < 1e-3, sprintf "%s differs by %g of its largest after 200 steps" name e))

    [<Fact>]
    let ``k steps a call are k calls of a step`` () =
        GpuTests.withGpu (fun gpu ->
            let f0 = pair FluidEdge.Wall
            let one = Sim.ofStep 0.0025<s> (GpuSim.stam gpu options) f0 |> Sim.run 4
            let four = GpuSim.stamWith 4 gpu options 0.0<s> 0.01<s> f0
            Assert.Equal<int64[]>(bits one.UFaces, bits four.UFaces)
            Assert.Equal<int64[]>(bits one.Dye, bits four.Dye)
            Assert.Equal<int64[]>(bits one.Pressure, bits four.Pressure))

// ======================================================================================
// Gate 7 — a GPU trajectory seeks to the bit
// ======================================================================================

module ``a GPU trajectory seeks to what a run reaches`` =

    [<Fact>]
    let ``from four threads at once`` () =
        GpuTests.withGpu (fun gpu ->
            let sim = Sim.ofStep 0.01<s> (GpuSim.stam gpu { Stam.defaults<m, 1> with Buoyancy = 1.0<m/s^2> }) (FluidTests.vortexPair 48)
            let run = Sim.states 40 sim
            let traj = Sim.trajectory 0.1<s> 0.4<s> sim
            let seeks = [| 3; 17; 26; 40; 11; 33; 8; 29 |] |> Array.Parallel.map (fun k -> k, traj.At(float k * 0.01<s>))

            for k, s in seeks do
                Assert.True(
                    (bits s.UFaces = bits run.[k].UFaces && bits s.VFaces = bits run.[k].VFaces && bits s.Dye = bits run.[k].Dye),
                    sprintf "the seek to step %d differs from the run" k
                ))
