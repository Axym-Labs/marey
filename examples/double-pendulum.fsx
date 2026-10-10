(*  A double pendulum: its path in phase space, a Poincaré section, and a film.

    Run it:  dotnet fsi examples/double-pendulum.fsx
    Writes:  double-pendulum-portrait.svg, double-pendulum-section.svg, and a film's frames in
             double-pendulum/ in the current directory.

    Two equal pendulums, the second hung from the first, released from rest at θ₁ = 2 rad and
    θ₂ = 2.5 rad. The state has four numbers, two angles and two angular velocities, so the motion
    is not a flow on a plane, and M4's phase portrait of a single pendulum shows what it would do
    if the second bob were not there: drawn under the first bob's actual path, the path cuts
    across the single pendulum's orbits. A Poincaré section cuts the four dimensions down to two:
    every time the first arm swings through the vertical going forward, the second arm's angle and
    speed are a point. It is integrated with RK4 at 1 ms and its energy kept to parts in 10⁸ over
    ten minutes.

    **This motion is chaotic, and the browser gives a different one.** Two starts an ulp apart
    differ by 0.1 rad within half a minute. Compiled by Fable, this code runs under JavaScript,
    whose `sin` and `exp` differ from .NET's in the last bit for a few percent of arguments, so
    the double pendulum in a browser and this one are different trajectories after about 25 s.
    Both are correct to the integrator's order; neither is the other. *)

#r "nuget: Marey.Sim"
#r "nuget: Marey.Backend.Svg"

open System
open Marey
open Marey.UnitsOfMeasure

let g = 9.81<m/s^2>
let length = 1.0<m>

/// The two angles and their rates. A record with four quantities of two units is the case D134's
/// third tier is for: a `Tangent` says how to advance it.
type Arms =
    { T1: float<rad>
      T2: float<rad>
      W1: float<rad/s>
      W2: float<rad/s> }

type ArmRates =
    { DT1: float<rad/s>
      DT2: float<rad/s>
      DW1: float<rad/s^2>
      DW2: float<rad/s^2> }

let tangent: Tangent<Arms, ArmRates, s> =
    { Axpy =
        fun h d a ->
            { T1 = a.T1 + h * d.DT1
              T2 = a.T2 + h * d.DT2
              W1 = a.W1 + h * d.DW1
              W2 = a.W2 + h * d.DW2 }
      Lin =
        fun p d q e ->
            { DT1 = p * d.DT1 + q * e.DT1
              DT2 = p * d.DT2 + q * e.DT2
              DW1 = p * d.DW1 + q * e.DW1
              DW2 = p * d.DW2 + q * e.DW2 } }

/// Equal masses and lengths: the standard equations, with the angles in radians and the rates in
/// rad/s². `float` strips the radian before a trigonometric function, which takes a plain number.
let motion (_: float<s>) (a: Arms) : ArmRates =
    let d = float (a.T1 - a.T2)
    let w1, w2 = float a.W1, float a.W2
    let k = float (g / length)
    let den = 3.0 - cos (2.0 * d)

    let a1 =
        (-3.0 * k * sin (float a.T1) - k * sin (float a.T1 - 2.0 * float a.T2)
         - 2.0 * sin d * (w2 * w2 + w1 * w1 * cos d))
        / den

    let a2 = 2.0 * sin d * (2.0 * w1 * w1 + 2.0 * k * cos (float a.T1) + w2 * w2 * cos d) / den

    { DT1 = a.W1
      DT2 = a.W2
      DW1 = a1 * 1.0<rad/s^2>
      DW2 = a2 * 1.0<rad/s^2> }

/// The energy per unit mass, in J/kg: kinetic in the two bobs, potential below the pivot.
let energy (a: Arms) : float<J/kg> =
    let w1, w2 = float a.W1, float a.W2
    let l2 = length * length
    let kinetic = 0.5 * l2 * (2.0 * w1 * w1 + w2 * w2 + 2.0 * w1 * w2 * cos (float (a.T1 - a.T2))) * 1.0</s^2>
    let potential = -g * length * (2.0 * cos (float a.T1) + cos (float a.T2))
    kinetic + potential

let start = { T1 = 2.0<rad>; T2 = 2.5<rad>; W1 = 0.0<rad/s>; W2 = 0.0<rad/s> }

let sim =
    Sim.ofStep 1e-3<s> (Ode.rk4With tangent motion) start
    |> Sim.withInvariant "energy" (U.div Derived.joule U.kilogram) energy

let out (file: string) = IO.Path.Combine(IO.Directory.GetCurrentDirectory(), file)

// ---- The first arm's path, over a single pendulum's phase portrait ----------------------------

let single: Flow<rad, rad/s, s> = fun th om -> struct (om, -float (g / length) * sin (float th) * 1.0<rad/s^2>)
// Wide enough for the path, which goes over the top: the single pendulum's portrait repeats every
// turn.
let grid = Field.sampleFlow (Grid.linear -3.5<rad> 9.5<rad> 131) (Grid.linear -10.0<rad/s> 10.0<rad/s> 81) single

let trajectory = Sim.trajectory 1.0<s> 600.0<s> sim
let path = Array.init 2001 (fun i -> trajectory.At(float i * 0.01<s>))

let portrait =
    Phase.portrait Derived.radian (U.div Derived.radian U.second) grid
    |> Chart.addLine Derived.radian (U.div Derived.radian U.second) (path |> Array.map (fun a -> a.T1)) (path |> Array.map (fun a -> a.W1))
    |> Chart.withName "the first arm, 20 s"
    |> Chart.withTitle "The first arm against a single pendulum's flow"

portrait |> Chart.toSvg SvgBackend.render (out "double-pendulum-portrait.svg") (Chart.size 900.0 520.0)
printfn "wrote %s" (out "double-pendulum-portrait.svg")

// ---- The section: the first arm through the vertical going forward, over ten minutes -----------

// The arm can go over the top, so its angle is not confined to one turn: `sin θ₁` rising with the
// arm below the pivot is "through the bottom, going forward" on every turn.
let section =
    Poincare.section Direction.Rising (fun (a: Arms) -> sin (float a.T1)) 600.0<s> sim
    |> Array.filter (fun p -> cos (float p.State.T1) > 0.0)

/// An angle in (−π, π].
let wrap (a: float<rad>) = (float a - 2.0 * Math.PI * Math.Round(float a / (2.0 * Math.PI))) * 1.0<rad>

let sectionFigure =
    Chart.scatter Derived.radian (U.div Derived.radian U.second) (section |> Array.map (fun p -> wrap p.State.T2)) (section |> Array.map (fun p -> p.State.W2))
    |> Chart.withTitle "Poincaré section"
    |> Chart.withSubtitle "the second arm, each time the first swings through the vertical"

sectionFigure |> Chart.toSvg SvgBackend.render (out "double-pendulum-section.svg") (Chart.size 640.0 480.0)
printfn "wrote %s, %d crossings" (out "double-pendulum-section.svg") section.Length

// ---- A film: the pendulum itself, five seconds at 20 frames a second ---------------------------

/// The arms as a line from the pivot through both bobs, on axes stated once, so the panel holds
/// still while the pendulum moves (a view of the current state on automatic axes re-scales as a
/// coordinate passes zero).
let draw (a: Arms) =
    let x1 = length * sin (float a.T1)
    let y1 = -length * cos (float a.T1)
    let x2 = x1 + length * sin (float a.T2)
    let y2 = y1 - length * cos (float a.T2)

    Chart.line U.metre U.metre [| 0.0<m>; x1; x2 |] [| 0.0<m>; y1; y2 |]
    |> Chart.addScatter U.metre U.metre [| x1; x2 |] [| y1; y2 |]
    |> Chart.withXDomain -2.2 2.2
    |> Chart.withYDomain -2.2 2.2
    |> Chart.withXLabel "x"
    |> Chart.withYLabel "y"
    |> Chart.withTitle "A double pendulum"

let film = Film.ofFigures 32 (Chart.size 480.0 480.0) (Anim.map draw (Sim.trajectory 0.5<s> 5.0<s> sim))
let frames = Frames.writeText FramePass.Concurrent SvgBackend.render (FrameTarget.into "double-pendulum" ".svg") 20.0 film
printfn "wrote %d frames into %s/" frames.Files.Length frames.Directory

// ---- The claims, checked --------------------------------------------------------------------------

let drift = Sim.drift 601 trajectory sim |> List.head
printfn "energy kept to %.1e relative over 600 s: %b" (drift.Values |> Array.map abs |> Array.max) (drift.Values |> Array.forall (fun v -> abs v < 1e-5))

printfn "every section point is on the vertical, going forward: %b"
    (section |> Array.forall (fun p -> abs (wrap p.State.T1) < 1e-9<rad> && p.State.W1 > 0.0<rad/s>))

// Chaos: a start one nanoradian away is a different pendulum within half a minute.
let nudged = Sim.ofStep 1e-3<s> (Ode.rk4With tangent motion) { start with T1 = start.T1 + 1e-9<rad> }
let apart (t: float<s>) = abs (float ((Sim.at t sim).T1 - (Sim.at t nudged).T1))
printfn "a nanoradian apart at the start, more than 0.1 rad apart by 30 s: %b" (apart 1.0<s> < 1e-6 && apart 30.0<s> > 0.1)
