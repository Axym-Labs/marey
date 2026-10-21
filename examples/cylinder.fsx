(*  Flow past a cylinder: lattice Boltzmann, a Kármán street, and LIC on the GPU.

    Run it:  dotnet fsi examples/cylinder.fsx
    Writes:  cylinder.png (or cylinder.svg without a GPU) and a film's frames in cylinder/ in the
             current directory.

    Water-like flow at 0.1 m/s through a 40 cm channel past a 2 cm cylinder: a Reynolds number of
    100, where the wake no longer holds still and sheds vortices from either side in turn. The
    lattice is 200 × 50 cells of 2 mm stepped every 2 ms, and the viscosity sets the relaxation
    time, τ = ½ + 3νΔt/Δx² = 0.53; a step the lattice was not built for, or a viscosity that would
    put τ at or below ½, is refused with the numbers.

    With a GPU the lattice is stepped there, twenty ticks a call, and the figure's LIC is convolved
    there too, because the figure is compiled with the GPU renderer's measurer (D150). Without one
    the same code runs on the CPU, and says so. The two agree to a few parts in a million, checked
    below when there is a GPU to check.

    A perfectly symmetric start would shed only when rounding broke the symmetry, which takes
    long and differs between machines; a small sideways push behind the cylinder starts it. *)

#r "nuget: Marey.Sim"
#r "nuget: Marey.Backend.Gpu"
#r "nuget: Marey.Backend.Svg"

open System
open Marey
open Marey.UnitsOfMeasure

let dx, dt = 2e-3<m>, 2e-3<s>
let speed = 0.1<m/s>
let diameter = 0.02<m>
let nu = speed * diameter / 100.0 // Re = 100

let start =
    Lattice.create dx dt 200 50
    |> Lattice.withObstacle (fun x y -> (x - 0.08<m>) * (x - 0.08<m>) + (y - 0.051<m>) * (y - 0.051<m>) <= diameter * diameter / 4.0)
    |> Lattice.withVelocity (fun x _ -> struct (speed, (if x > 0.09<m> && x < 0.13<m> then 0.1 * speed else 0.0<m/s>)))

let options =
    { Lbm.defaults nu with
        Left = LatticeEdge.Inflow(speed, 0.0<m/s>)
        Right = LatticeEdge.Outflow }

printfn "Reynolds number 100, relaxation time τ = %.3f" (Lbm.tau nu start)

let gpu =
    try
        Some(new GpuRenderer())
    with e ->
        printfn "no GPU here (%s); stepping on the CPU" (e.Message.Split([| ". " |], StringSplitOptions.None).[0])
        None

// Twenty ticks a step of the simulation: one transfer each on the GPU.
let ticks = 20
let cpuStep: Stepper<Lattice<m>, s> = fun t _ l -> Seq.fold (fun l k -> Lbm.step options (t + float k * dt) dt l) l [ 0 .. ticks - 1 ]
let step = match gpu with Some g -> GpuSim.lbmWith ticks g options | None -> cpuStep
gpu |> Option.iter (fun g -> printfn "stepping on %s" g.Description)

let horizon = 14.0<s>
let stepLength = float ticks * dt
// A checkpoint every 0.2 s, so each frame of the film below is a checkpoint and costs no steps:
// 70 states of 90 000 populations, about 50 MB.
let trajectory = Sim.ofStep stepLength step start |> Sim.trajectory 0.2<s> horizon

// A lattice steps whole ticks, so a seek lands on a whole step: between two, a trajectory would
// take a shorter last step, which the lattice refuses.
let at (t: float<s>) = trajectory.At(float (int (round (t / stepLength))) * stepLength)

// The vorticity, with the flow's texture laid over it. The shear layers at the cylinder's wall
// reach 40 s⁻¹ and the vortices in the wake about 10, so the colours span ±10 s⁻¹ and the wall
// saturates rather than leaving the wake in the ramp's pale middle. `Lattice.vorticity` is in s⁻¹,
// and `U.perSecond` writes it so, where `U.hertz` would call it a frequency.
let vorticity l = Lattice.vorticity l

let view (t: float<s>) =
    let l = at t
    Field.heatmapWith Theme.initial.Diverging true U.metre U.metre U.perSecond (vorticity l)
    |> Chart.withColorDomain U.perSecond -10.0</s> 10.0</s>
    |> Chart.withColorLabel "ω"
    |> Chart.overlay (Field.lic U.metre U.metre (Lattice.velocity l) |> Chart.withOpacity 0.35)
    |> Chart.withXLabel "x"
    |> Chart.withYLabel "y"
    |> Chart.withTitle (sprintf "The vorticity behind a cylinder, Re = 100, t = %.1f s" (float t))

let out (file: string) = IO.Path.Combine(IO.Directory.GetCurrentDirectory(), file)
let size = Chart.size 900.0 300.0

match gpu with
| Some g ->
    let png = view horizon |> Chart.toSceneWith g.Measurer size |> g.RenderPng size
    IO.File.WriteAllBytes(out "cylinder.png", png)
    printfn "wrote %s" (out "cylinder.png")
| None ->
    view horizon |> Chart.toSvg SvgBackend.render (out "cylinder.svg") size
    printfn "wrote %s" (out "cylinder.svg")

// The film: the last four seconds, one frame every 0.2 s.
let last = Anim.ofSeconds 4.0<s> (fun t -> view (horizon - 4.0<s> + t))

let frames =
    match gpu with
    | Some g -> Frames.write FramePass.Concurrent g.RenderPng (FrameTarget.into "cylinder" ".png") 5.0 (Film.ofFiguresWith 8 g.Measurer size last)
    | None -> Frames.writeText FramePass.Concurrent SvgBackend.render (FrameTarget.into "cylinder" ".svg") 5.0 (Film.ofFigures 8 size last)

printfn "wrote %d frames into %s/" frames.Files.Length frames.Directory

// ---- The claims, checked --------------------------------------------------------------------------

// The transverse velocity a cylinder's width behind it changes sign as each vortex passes.
// Read from one straight run over the last six seconds, not from 150 seeks.
let probe =
    Sim.ofStep stepLength step (at (horizon - 6.0<s>))
    |> Sim.states 150
    |> Array.map (fun l -> (Lattice.velocity l).W.[25 * 200 + 80])
let crossings = [ for k in 1 .. probe.Length - 1 do if probe.[k - 1] < 0.0<m/s> && probe.[k] >= 0.0<m/s> then yield k ]
let period = float (List.last crossings - List.head crossings) / float (crossings.Length - 1) * 0.04<s>
let strouhal = diameter / (period * speed)
printfn "the wake sheds, every %.2f s: Strouhal number %.3f, between 0.15 and 0.25: %b" (float period) strouhal (strouhal > 0.15 && strouhal < 0.25)

match gpu with
| Some g ->
    let cpu = Sim.ofStep dt (Lbm.step options) start |> Sim.run 200 |> Lattice.velocity
    let onGpu = Sim.ofStep dt (GpuSim.lbm g options) start |> Sim.run 200 |> Lattice.velocity
    let present (xs: float<m/s>[]) = xs |> Array.filter (fun v -> not (Double.IsNaN(float v)))
    let worst = Array.map2 (fun (a: float<m/s>) b -> abs (a - b)) (present cpu.U) (present onGpu.U) |> Array.max
    let largest = present cpu.U |> Array.map abs |> Array.max
    printfn "after 200 ticks the GPU is within 1e-3 of the CPU's fastest flow (%.1e): %b" (worst / largest) (worst < 1e-3 * largest)
    (g :> IDisposable).Dispose()
| None -> ()
