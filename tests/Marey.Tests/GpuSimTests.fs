/// M5.6 phases 3 and 4 — the GPU solvers against their CPU twins. Gates 6, 7 and 8 of
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

// ======================================================================================
// Gates 6, 7 and 8 for lattice Boltzmann (phase 4)
// ======================================================================================

module ``LBM on the GPU is LBM on the CPU`` =

    let private dx = 1e-3<m>
    let private dt = 1e-3<s>

    let private finite (xs: float<'u>[]) = xs |> Array.filter (fun v -> not (Double.IsNaN(float v)))

    /// Flow past a cylinder: an inflow, an outflow, walls, and a kick to break the symmetry.
    let private cylinder () =
        Lattice.create dx dt 120 40
        |> Lattice.withObstacle (fun x y -> (x - 0.03<m>) * (x - 0.03<m>) + (y - 0.021<m>) * (y - 0.021<m>) <= 0.005<m> * 0.005<m>)
        |> Lattice.withVelocity (fun x _ -> struct (0.05<m/s>, (if x > 0.035<m> && x < 0.05<m> then 0.005<m/s> else 0.0<m/s>)))

    let private cylinderOptions =
        { Lbm.defaults (0.02 * dx * dx / dt) with
            Left = LatticeEdge.Inflow(0.05<m/s>, 0.0<m/s>)
            Right = LatticeEdge.Outflow
            Bottom = LatticeEdge.Wall
            Top = LatticeEdge.Wall }

    /// A periodic channel with walls, driven by a body force across an obstacle.
    let private channel () =
        Lattice.create dx dt 100 30
        |> Lattice.withObstacle (fun x y -> (x - 0.03<m>) * (x - 0.03<m>) + (y - 0.016<m>) * (y - 0.016<m>) <= 0.005<m> * 0.005<m>)

    let private channelOptions =
        { Lbm.defaults (0.02 * dx * dx / dt) with Force = (2e-3<m/s^2>, 1e-3<m/s^2>); Bottom = LatticeEdge.Wall; Top = LatticeEdge.Wall }

    [<Theory>]
    [<InlineData(true)>]
    [<InlineData(false)>]
    let ``after 200 steps every cell is within 1e-3 of the largest velocity`` (cylindrical: bool) =
        GpuTests.withGpu (fun gpu ->
            let l0, o = if cylindrical then cylinder (), cylinderOptions else channel (), channelOptions
            let cpu = Sim.ofStep dt (Lbm.step o) l0 |> Sim.run 200 |> Lattice.velocityUnder o.Force
            let gpuRun = Sim.ofStep dt (GpuSim.lbm gpu o) l0 |> Sim.run 200 |> Lattice.velocityUnder o.Force

            for name, e in [ "u", relative (finite cpu.U) (finite gpuRun.U); "v", relative (finite cpu.W) (finite gpuRun.W) ] do
                Console.WriteLine(sprintf "LBM GPU against CPU, %s, %s: %.2e (%s)" (if cylindrical then "cylinder" else "channel") name e gpu.Description)
                Assert.True(e < 1e-3, sprintf "%s differs by %g of its largest after 200 steps" name e))

    /// Gate 8. Measured: 1.0e-10 on radeonsi and 4.0e-10 on llvmpipe in the deviation form, and
    /// 1.5e-6 with the collision computed raw, which the plan's bound of 1e-6 caught by a margin of
    /// a half; 1e-8 is a hundred times inside the raw form and twenty-five outside llvmpipe.
    [<Fact>]
    let ``a closed channel keeps its mass to 1e-8 over ten thousand steps`` () =
        GpuTests.withGpu (fun gpu ->
            let l0 = channel ()
            let l = Sim.ofStep (100.0 * dt) (GpuSim.lbmWith 100 gpu channelOptions) l0 |> Sim.run 100
            let drift = abs ((Lattice.mass l - Lattice.mass l0) / Lattice.mass l0)
            let fastest = (Lattice.velocityUnder channelOptions.Force l).U |> finite |> Array.max
            Console.WriteLine(sprintf "LBM GPU mass drift over 1e4 steps: %.2e, fastest %.4f m/s (%s)" drift (float fastest) gpu.Description)
            Assert.True(fastest > 1e-3<m/s>, "the force moved nothing, so the mass was never at risk")
            Assert.True(drift < 1e-8, sprintf "the mass drifted by %g" drift))

    [<Fact>]
    let ``k ticks a call are k calls of a tick, and seeks are runs`` () =
        GpuTests.withGpu (fun gpu ->
            let l0 = cylinder ()
            let one = Sim.ofStep dt (GpuSim.lbm gpu cylinderOptions) l0 |> Sim.run 5
            let five = GpuSim.lbmWith 5 gpu cylinderOptions 0.0<s> (5.0 * dt) l0
            Assert.Equal<float[]>(one.Populations, five.Populations)

            let sim = Sim.ofStep dt (GpuSim.lbm gpu cylinderOptions) l0
            let run = Sim.states 30 sim
            let traj = Sim.trajectory 0.01<s> 0.03<s> sim

            for k, s in [| 4; 13; 22; 30; 9 |] |> Array.Parallel.map (fun k -> k, traj.At(float k * dt)) do
                Assert.True((s.Populations = run.[k].Populations), sprintf "the seek to step %d differs from the run" k))

    [<Fact>]
    let ``a step that is not k ticks is refused as the CPU refuses it`` () =
        GpuTests.withGpu (fun gpu ->
            let e = Assert.Throws<ArgumentException>(fun () -> GpuSim.lbmWith 2 gpu cylinderOptions 0.0<s> dt (cylinder ()) |> ignore)
            Assert.Contains("0.0005 s", e.Message))

// ======================================================================================
// Gate 9 — GPU LIC (phase 5)
// ======================================================================================

/// The per-pixel LIC the shader computes, on the CPU in float64: the oracle, and an
/// `ILicConvolver` like the GPU's, so `FieldMarks` runs it through the same stretch and colour.
type private PerPixelLic() =
    interface ITextMeasurer with
        member _.Measure spec text = TextMeasure.approx.Measure spec text

    interface ILicConvolver with
        member _.Convolve(job: LicJob) =
            let nx, ny = job.Xs.Length, job.Ys.Length
            let rw, rh = job.Width, job.Height

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

            let field (t: float[]) i j fx fy =
                let a = t.[j * nx + i] + (t.[j * nx + i + 1] - t.[j * nx + i]) * fx
                let b = t.[(j + 1) * nx + i] + (t.[(j + 1) * nx + i + 1] - t.[(j + 1) * nx + i]) * fx
                a + (b - a) * fy

            let dir (rx: float) (ry: float) =
                if rx < 0.0 || rx > float rw || ry < 0.0 || ry > float rh then
                    ValueNone
                else
                    let x = job.Xs.[0] + rx / float rw * (job.Xs.[nx - 1] - job.Xs.[0])
                    let y = job.Ys.[0] + ry / float rh * (job.Ys.[ny - 1] - job.Ys.[0])

                    match locate job.Xs x, locate job.Ys y with
                    | ValueSome(struct (i, fx)), ValueSome(struct (j, fy)) ->
                        let u = field job.U i j fx fy
                        let v = field job.W i j fx fy
                        let s = sqrt (u * u + v * v)
                        if Double.IsFinite s && s > job.Slowest then ValueSome(struct (u / s, v / s)) else ValueNone
                    | _ -> ValueNone

            let rk4 (x: float) (y: float) (s: float) =
                match dir x y with
                | ValueNone -> ValueNone
                | ValueSome(struct (ax, ay)) ->
                    match dir (x + s / 2.0 * ax) (y + s / 2.0 * ay) with
                    | ValueNone -> ValueNone
                    | ValueSome(struct (bx, by)) ->
                        match dir (x + s / 2.0 * bx) (y + s / 2.0 * by) with
                        | ValueNone -> ValueNone
                        | ValueSome(struct (cx, cy)) ->
                            match dir (x + s * cx) (y + s * cy) with
                            | ValueNone -> ValueNone
                            | ValueSome(struct (dx, dy)) ->
                                if ax * bx + ay * by < 0.0 || bx * cx + by * cy < 0.0 || cx * dx + cy * dy < 0.0 then ValueNone
                                else ValueSome(struct (x + s / 6.0 * (ax + 2.0 * bx + 2.0 * cx + dx), y + s / 6.0 * (ay + 2.0 * by + 2.0 * cy + dy)))

            let noise (x: float) (y: float) =
                job.Noise.[(min (rh - 1) (max 0 (int (floor y)))) * rw + min (rw - 1) (max 0 (int (floor x)))]

            ValueSome(
                Array.Parallel.init (rw * rh) (fun k ->
                    let sx, sy = float (k % rw) + 0.5, float (k / rw) + 0.5

                    if (dir sx sy).IsNone then
                        nan
                    else
                        let mutable sum = noise sx sy
                        let mutable count = 1.0

                        for sign in [ 1.0; -1.0 ] do
                            let mutable x, y, go, m = sx, sy, true, 0

                            while go && m < job.HalfLength do
                                match rk4 x y (sign * job.Step) with
                                | ValueSome(struct (a, b)) ->
                                    x <- a
                                    y <- b
                                    sum <- sum + noise x y
                                    count <- count + 1.0
                                    m <- m + 1
                                | ValueNone -> go <- false

                        sum / count)
            )

module ``GPU LIC is the per-pixel convolution`` =

    /// A spiral on a grid uneven in x, as a log axis makes it, with a hole of missing values.
    let private payload: FieldPayload<nd> =
        let g = 41
        let xs = Array.init g (fun i -> (float i / float (g - 1)) ** 1.3 * 1.0<nd>)
        let ys = Array.init g (fun j -> float j / float (g - 1) * 1.0<nd>)

        let at (k: int) =
            let x, y = float xs.[k % g] - 0.5, float ys.[k / g] - 0.5
            if x * x + y * y < 0.003 then nan, nan else -y + 0.2 * x, x + 0.2 * y

        { Xs = xs
          Ys = ys
          Values = Array.create (g * g) 1.0
          U = Array.init (g * g) (at >> fst)
          W = Array.init (g * g) (at >> snd) }

    let private plot: Extent<px> = { Width = 600.0<px>; Height = 400.0<px> }
    let private white (_: float) = Some Color.white
    let private image (m: ITextMeasurer) = (FieldMarks.licImage m plot white Field.licDefaults payload).Value |> fst

    let private brightness (i: Image) = Array.init (i.Width * i.Height) (fun k -> float i.Rgba.[4 * k])

    let private correlation (a: float[]) (b: float[]) =
        let ma, mb = Array.average a, Array.average b
        let c = Array.map2 (fun x y -> (x - ma) * (y - mb)) a b |> Array.sum
        c / sqrt ((a |> Array.sumBy (fun x -> (x - ma) ** 2.0)) * (b |> Array.sumBy (fun y -> (y - mb) ** 2.0)))

    [<Fact>]
    let ``the GPU's image is the CPU per-pixel image, 99% of pixels within 2 of 255`` () =
        GpuTests.withGpu (fun gpu ->
            let g = image gpu.Measurer
            let c = image (PerPixelLic())
            let off = Array.map2 (fun (a: byte) (b: byte) -> abs (int a - int b)) g.Rgba c.Rgba |> Array.chunkBySize 4 |> Array.filter (Array.exists (fun d -> d > 2)) |> Array.length
            let share = float off / float (g.Width * g.Height)
            Console.WriteLine(sprintf "GPU LIC against the CPU per-pixel LIC: %.3f%% of pixels more than 2 apart (%s)" (100.0 * share) gpu.Description)
            Assert.True(share < 0.01, sprintf "%.2f%% of pixels differ by more than 2" (100.0 * share)))

    [<Fact>]
    let ``the GPU's image correlates with fast LIC's at 0.85 or more`` () =
        GpuTests.withGpu (fun gpu ->
            let r = correlation (brightness (image gpu.Measurer)) (brightness (image TextMeasure.approx))
            Console.WriteLine(sprintf "GPU LIC against fast LIC: correlation %.3f" r)
            Assert.True(r >= 0.85, sprintf "correlation %g" r))

    [<Fact>]
    let ``a memoising measurer forwards the convolution, and the approximate one does not convolve`` () =
        GpuTests.withGpu (fun gpu ->
            Assert.Equal<byte[]>((image gpu.Measurer).Rgba, (image (MemoTextMeasurer gpu.Measurer)).Rgba)
            Assert.NotEqual<byte[]>((image gpu.Measurer).Rgba, (image TextMeasure.approx).Rgba))
