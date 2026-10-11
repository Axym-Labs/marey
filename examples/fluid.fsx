(*  Smoke in a box: Stam's stable fluids, on the GPU when there is one.

    Run it:  dotnet fsi examples/fluid.fsx
    Writes:  fluid.svg and a film's frames in fluid/ in the current directory.

    A closed 1 m box of air, heated at a patch in its floor. The heat is a dye in kelvin above the
    room, carried by the flow, diffusing, and lifting the air where it is: a buoyancy in metres per
    second squared per kelvin, so a dye in the wrong unit, or a viscosity in m/s, does not compile.

    The warm plume spreads under the ceiling, and the warm layer's lower edge comes down as the
    box fills: the straight front across the picture is that edge, not a seam.

    Each step advects the velocity and the heat semi-Lagrangian, adds the buoyancy and the source,
    diffuses implicitly, and projects the velocity onto a divergence-free field by multigrid on a
    staggered grid (D148): the divergence it leaves is the solver's alone, a few parts in ten
    million of |u|/H, checked below. With a GPU the same step runs there (D147), five steps a call,
    and its result agrees with the CPU's to a few parts in a million; the step times are printed. *)

#r "nuget: Marey.Sim"
#r "nuget: Marey.Backend.Gpu"
#r "nuget: Marey.Backend.Svg"

open System
open Marey
open Marey.UnitsOfMeasure

let n = 96

let room =
    Fluid.box FluidEdge.Wall FluidEdge.Wall (1.0<m> / float n) n n

let options =
    { Stam.defaults<m, K> with
        Viscosity = 2e-4<m^2/s>
        Diffusion = 2e-4<m^2/s>
        Buoyancy = 0.08<m/s^2/K>
        Source = fun x y -> if abs (x - 0.45<m>) < 0.06<m> && y < 0.06<m> then 40.0<K/s> else 0.0<K/s> }

let gpu =
    try
        Some(new GpuRenderer())
    with e ->
        printfn "no GPU here (%s); stepping on the CPU" (e.Message.Split([| ". " |], StringSplitOptions.None).[0])
        None

let dt = 0.02<s>

let timed (name: string) (step: Step<Fluid<m, K>, s>) (h: float<s>) (count: int) =
    step 0.0<s> h room |> ignore
    let sw = Diagnostics.Stopwatch.StartNew()
    let mutable f = room

    for k in 1..count do
        f <- step (float k * h) h f

    printfn "%s: %.2f ms a step" name (sw.Elapsed.TotalMilliseconds / float count)

timed "CPU" (Stam.step options) dt 20

let step =
    match gpu with
    | Some g ->
        printfn "stepping on %s" g.Description
        timed "GPU, one step a call" (GpuSim.stam g options) dt 50
        GpuSim.stamWith 5 g options
    | None -> fun t h f -> Seq.fold (fun f k -> Stam.step options (t + float k * dt) dt f) f [ 0..4 ]

let horizon = 8.0<s>
let trajectory = Sim.ofStep (5.0 * dt) step room |> Sim.trajectory 1.0<s> horizon

let view (t: float<s>) =
    let f = trajectory.At t
    Field.heatmapWith Palette.magma true U.metre U.metre U.kelvin (Fluid.dye f)
    |> Chart.overlay (Field.streamlines U.metre U.metre (Fluid.velocity f))
    |> Chart.withXLabel "x"
    |> Chart.withYLabel "y"
    |> Chart.withTitle (sprintf "Warm air rising, t = %.1f s" (float t))

let out (file: string) = IO.Path.Combine(IO.Directory.GetCurrentDirectory(), file)
view horizon |> Chart.toSvg SvgBackend.render (out "fluid.svg") (Chart.size 560.0 520.0)
printfn "wrote %s" (out "fluid.svg")

let film = Film.ofFigures 8 (Chart.size 420.0 390.0) (Anim.ofSeconds horizon view)
let frames = Frames.writeText FramePass.Concurrent SvgBackend.render (FrameTarget.into "fluid" ".svg") 2.0 film
printfn "wrote %d frames into %s/" frames.Files.Length frames.Directory

// ---- The claims, checked --------------------------------------------------------------------------

let states = [ 0.0<s> .. 0.5<s> .. horizon ] |> List.map trajectory.At

let worstDivergence =
    states
    |> List.tail
    |> List.map (fun f ->
        let scale = (f.UFaces |> Array.map abs |> Array.max) / f.Cell
        let d = (Fluid.divergence f).Values
        sqrt (Array.averageBy (fun v -> v * v) d) / scale)
    |> List.max

printfn "the flow stays divergence-free, under 1e-5 of |u|/H (%.1e): %b" worstDivergence (worstDivergence < 1e-5)

let height (f: Fluid<m, K>) =
    let d = Fluid.dye f
    (Array.init d.Values.Length (fun k -> d.Values.[k] * d.Ys.[k / n]) |> Array.sum) / (Array.sum d.Values)

let early, late = trajectory.At 1.0<s>, trajectory.At horizon
printfn "the warm air rises, from %.2f m to %.2f m: %b" (float (height early)) (float (height late)) (height late > height early + 0.1<m>)

printfn "no air crosses the walls: %b" ([ 0 .. n - 1 ] |> List.forall (fun j -> late.UFaces.[j * (n + 1)] = 0.0<m/s> && late.UFaces.[j * (n + 1) + n] = 0.0<m/s>))

match gpu with
| Some g ->
    let cpu = Sim.ofStep dt (Stam.step options) room |> Sim.run 100
    let onGpu = Sim.ofStep dt (GpuSim.stam g options) room |> Sim.run 100
    let worst = Array.map2 (fun (a: float<K>) b -> abs (a - b)) cpu.Dye onGpu.Dye |> Array.max
    let largest = cpu.Dye |> Array.max
    printfn "after 100 steps the GPU's heat is within 1e-3 of the CPU's hottest (%.1e): %b" (worst / largest) (worst < 1e-3 * largest)
    (g :> IDisposable).Dispose()
| None -> ()
