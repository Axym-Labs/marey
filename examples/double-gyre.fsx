(*  The double gyre: a field that changes in time, and what it carries.

    Run it:  dotnet fsi examples/double-gyre.fsx
    Writes:  double-gyre.svg and a film's frames in double-gyre/ in the current directory.

    Two counter-rotating gyres in a 2 m × 1 m box, the line between them swinging left and right
    every 10 s: the standard test field for transport in an unsteady flow. Four kinds of thing move
    through it, and they differ only in how particles are seeded and what is kept:

      * the instantaneous streamlines, M4's picture of the field at one moment, drawn in pixels;
      * pathlines, where three particles went, a trajectory in metres;
      * a streakline, every particle released from one point so far, which is what dye shows;
      * the particles themselves, the streak's current positions.

    In a steady field the three would coincide. Here the streamlines say nothing about where
    anything goes, and the streak folds into the gyres.

    The field is the analytic one, passed to the particles as it is. A field that arrives as grids,
    from data or a solver, goes through `Unsteady.ofSnapshots` (linear between the snapshots'
    times) or `Unsteady.ofFunction` (a grid built for each time asked), into the same step. *)

#r "nuget: Marey.Sim"
#r "nuget: Marey.Backend.Svg"

open System
open Marey
open Marey.UnitsOfMeasure

let amplitude = 0.1<m/s>
let epsilon = 0.25
let omega = 2.0 * Math.PI / 10.0<s>

/// `ψ = A sin(π f(x, t)) sin(π y)` with `f = ε sin(ωt) x² + (1 − 2ε sin(ωt)) x`, in metres; the
/// velocity is `(−∂ψ/∂y, ∂ψ/∂x)`.
let gyre: Unsteady<m, m, s> =
    fun t x y ->
        let a = epsilon * sin (omega * t)
        let x, y = float x, float y
        let f = a * x * x + (1.0 - 2.0 * a) * x
        let dfdx = 2.0 * a * x + 1.0 - 2.0 * a
        struct (-Math.PI * amplitude * sin (Math.PI * f) * cos (Math.PI * y), Math.PI * amplitude * cos (Math.PI * f) * sin (Math.PI * y) * dfdx)

let xs = Grid.linear 0.0<m> 2.0<m> 81
let ys = Grid.linear 0.0<m> 1.0<m> 41
let fieldAt (t: float<s>) = VectorGrid.sample xs ys (gyre t)

// The streak: one particle released from (1.1 m, 0.5 m) every 0.1 s.
let h = 0.1<s>
let streak = Sim.ofStep h (Particles.streakStep [| 1.1<m> |] [| 0.5<m> |] gyre) (struct ([| 1.1<m> |], [| 0.5<m> |]))
let streakAt = Sim.trajectory 1.0<s> 30.0<s> streak

// Three particles carried from where they start, and kept: their pathlines.
let carried = Sim.ofStep h (Particles.step gyre) (struct ([| 0.4<m>; 1.5<m>; 1.0<m> |], [| 0.3<m>; 0.7<m>; 0.9<m> |]))
let carriedAt = Sim.trajectory 1.0<s> 30.0<s> carried

let frameAt (t: float<s>) =
    let samples = Array.init (int (Math.Round(t / h)) + 1) (fun k -> float k * h)
    let struct (sx, sy) = streakAt.At t

    let fig =
        Field.streamlines U.metre U.metre (fieldAt t)
        // A translucent colour rather than `withOpacity`: a field mark with no colour channel paints
        // every segment in the layer's colour, which carries no opacity.
        |> Chart.withColor (Color.withAlpha 0.3 Color.black)
        |> Chart.addScatter U.metre U.metre sx sy
        |> Chart.withName "streak"

    Particles.pathlines samples carriedAt
    |> Array.mapi (fun i line -> i, line)
    |> Array.fold (fun fig (i, struct (px, py)) -> fig |> Chart.addLine U.metre U.metre px py |> Chart.withName (sprintf "pathline %d" (i + 1))) fig
    |> Chart.withXDomain 0.0 2.0
    |> Chart.withYDomain 0.0 1.0
    |> Chart.withXLabel "x"
    |> Chart.withYLabel "y"
    |> Chart.withTitle (sprintf "The double gyre at %.1f s" (float t))

let out = IO.Path.Combine(IO.Directory.GetCurrentDirectory(), "double-gyre.svg")
frameAt 30.0<s> |> Chart.toSvg SvgBackend.render out (Chart.size 900.0 520.0)
printfn "wrote %s" out

// A film of the first 20 s at 5 frames a second.
let film = Film.ofFigures 16 (Chart.size 640.0 380.0) (Anim.ofSeconds 20.0<s> frameAt)
let frames = Frames.writeText FramePass.Concurrent SvgBackend.render (FrameTarget.into "double-gyre" ".svg") 5.0 film
printfn "wrote %d frames into %s/" frames.Files.Length frames.Directory

// ---- The claims, checked --------------------------------------------------------------------------

let struct (fx, fy) = streakAt.At 30.0<s>
printfn "the streak holds one particle a step: %b" (fx.Length = 301)

// The walls are streamlines of every instantaneous field, so nothing crosses them.
let inside (x: float<m>) (y: float<m>) = x >= -1e-9<m> && x <= 2.0<m> + 1e-9<m> && y >= -1e-9<m> && y <= 1.0<m> + 1e-9<m>
printfn "every particle stayed in the box: %b" (Array.forall2 inside fx fy)

// The field is divergence-free, so it keeps areas: three particles 10⁻⁷ m apart span a triangle
// whose area holds to a part in 10⁴ over 10 s. A millimetre triangle does not, and that is not
// the integrator: the flow stretches it far past the scale where three points measure an area.
let blob = Sim.ofStep 0.01<s> (Particles.step gyre) (struct ([| 0.5<m>; 0.5000001<m>; 0.5<m> |], [| 0.5<m>; 0.5<m>; 0.5000001<m> |]))
let area (struct (x: float<m>[], y: float<m>[])) = 0.5 * abs (float ((x.[1] - x.[0]) * (y.[2] - y.[0]) - (x.[2] - x.[0]) * (y.[1] - y.[0])))
let a0 = area (Sim.run 0 blob)
printfn "a small triangle keeps its area: %b" (abs (area (Sim.run 1000 blob) - a0) / a0 < 1e-4)
