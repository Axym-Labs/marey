(*  Heat in a steel plate: a solver on a grid, a film, and a live figure with a time control.

    Run it:  dotnet fsi examples/heat.fsx
    Writes:  heat.svg, heat-frames/ (one SVG per frame) and heat-live.html in the current directory.

    A 20 cm steel plate at 300 K with a hot spot of 400 K in it. Its left edge is held at 350 K and
    the other three are insulated. Heat spreads as ∂T/∂t = α ∇²T, with T in kelvin and the
    diffusivity α in m²/s, so a step is in kelvin and the stability limit of the explicit scheme,
    h² / (4α), is a time the compiler has checked is one.

    The plate is stepped by Crank–Nicolson (ADI), which is stable at any step: here 1 s, eight
    times the explicit limit on this grid. Asked for the same step, forward Euler refuses and says
    by how much. The film's colour scale is the union of every frame's range, so one colour
    is one temperature throughout. The live figure is the same trajectory under a time control
    that a notebook plays or scrubs; written out here as a static page. *)

#r "nuget: Marey.Sim"
#r "nuget: Marey.Html"
#r "nuget: Marey.Backend.Svg"

open System
open Marey
open Marey.UnitsOfMeasure

let alpha = 1.2e-5<m^2/s> // steel
let side = 0.2<m>
let xs = Grid.linear 0.0<m> side 81
let ys = Grid.linear 0.0<m> side 81

let plate =
    ScalarGrid.sample xs ys (fun x y ->
        let r2 = (x - 0.12<m>) * (x - 0.12<m>) + (y - 0.1<m>) * (y - 0.1<m>)
        300.0<K> + 100.0<K> * exp (-r2 / (2.0 * 0.025<m> * 0.025<m>)))

let edges =
    { Boundaries.all (Boundary.Neumann 0.0<K/m>) with
        Left = Boundary.Dirichlet 350.0<K> }

let dt = 1.0<s>
let limit = Fdm.explicitLimit alpha plate
printfn "the explicit limit on this grid is %.3f s; the implicit step is %g s, %.1f times it" (float limit) (float dt) (dt / limit)

// Forward Euler at the same step refuses, with both numbers.
try
    Fdm.heat alpha edges 0.0<s> dt plate |> ignore
with :? ArgumentException as e ->
    printfn "forward Euler: %s" (e.Message.Split('\n').[0])

let sim = Sim.ofStep dt (Fdm.heatImplicit alpha edges) plate
let horizon = 600.0<s>
let trajectory = Sim.trajectory 30.0<s> horizon sim

let view (t: float<s>) =
    Field.heatmap U.metre U.metre U.kelvin (trajectory.At t)
    |> Chart.overlay (Field.contours U.metre U.metre (trajectory.At t))
    |> Chart.withXLabel "x"
    |> Chart.withYLabel "y"
    |> Chart.withTitle (sprintf "A steel plate after %.0f s" (float t))

let out (file: string) = IO.Path.Combine(IO.Directory.GetCurrentDirectory(), file)
view 60.0<s> |> Chart.toSvg SvgBackend.render (out "heat.svg") (Chart.size 640.0 520.0)
printfn "wrote %s" (out "heat.svg")

// The film: ten minutes, one frame every 20 s of the plate's time.
let film = Film.ofFigures 16 (Chart.size 480.0 400.0) (Anim.ofSeconds horizon view)
let frames = Frames.writeText FramePass.Concurrent SvgBackend.render (FrameTarget.into "heat-frames" ".svg") 0.05 film
printfn "wrote %d frames into %s/" frames.Files.Length frames.Directory

// The live figure: the same trajectory under a time control.
let live =
    interact {
        let! t = Control.time "t" 0.0<s> horizon
        return view t
    }

IO.File.WriteAllText(out "heat-live.html", Html.widget live (Live.init live) false)
printfn "wrote %s" (out "heat-live.html")

// ---- The claims, checked --------------------------------------------------------------------------

let all = [ 0.0<s> .. 30.0<s> .. horizon ] |> List.map trajectory.At

printfn "every temperature stays between the coldest and the hottest start: %b"
    (all |> List.forall (fun g -> g.Values |> Array.forall (fun v -> v >= 300.0<K> - 1e-9<K> && v <= 400.0<K> + 1e-9<K>)))

// From the first step on: at t = 0 the plate is as it was given, 300 K at the edge too.
printfn "the held edge holds at 350 K: %b"
    (all |> List.tail |> List.forall (fun g -> [ 0..80 ] |> List.forall (fun j -> g.Values.[j * 81] = 350.0<K>)))

let peak (g: ScalarGrid<m, m, K>) = Array.max g.Values
printfn "the hot spot cools, monotonically: %b" (all |> List.pairwise |> List.forall (fun (a, b) -> peak b <= peak a))
