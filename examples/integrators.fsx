(*  Three integrators on one oscillator, and what each does to its energy.

    Run it:  dotnet fsi examples/integrators.fsx
    Writes:  integrators.svg in the current directory.

    A mass on a spring, stepped 100 periods by forward Euler, by RK4 and by velocity Verlet at the
    same step, and the energy each run has gained or lost, in joules, against time. Euler gains
    energy every step, so its error grows exponentially. RK4 is far more accurate per step and
    still loses energy steadily. Verlet's error is larger than RK4's for a long while, and it does
    not grow: it is symplectic, so the energy it sees oscillates in a band for as long as it runs.
    That boundedness is why Verlet is the default for anything Hamiltonian.

    The three integrators take the state in two shapes. Euler and RK4 here step a 2-D flow,
    `(x, v)`; Verlet steps positions and velocities as arrays, and its acceleration is typed in
    m/s². An acceleration in the wrong unit does not compile in either. *)

#r "nuget: Marey.Sim"
#r "nuget: Marey.Backend.Svg"

open Marey
open Marey.UnitsOfMeasure

let mass = 1.0<kg>
let stiffness = 4.0 * System.Math.PI * System.Math.PI * 1.0<N/m> // a period of one second
let omega2 = stiffness / mass

let h = 0.005<s>
let periods = 100.0<s>

let energyOf (x: float<m>) (v: float<m/s>) : float<J> = 0.5 * mass * v * v + 0.5 * stiffness * x * x

// Euler and RK4 over the flow (x, v) -> (v, −k/m · x).
let flow: Flow2T<m, m/s, s> = fun _ x v -> struct (v, -omega2 * x)
let start = struct (0.1<m>, 0.0<m/s>)

let euler =
    Sim.ofStep h (Ode.euler flow) start
    |> Sim.withInvariant "energy" Derived.joule (fun (struct (x, v)) -> energyOf x v)

let rk4 =
    Sim.ofStep h (Ode.rk4 flow) start
    |> Sim.withInvariant "energy" Derived.joule (fun (struct (x, v)) -> energyOf x v)

// Verlet over positions and velocities, with the acceleration in m/s².
let verlet =
    Sim.ofStep h (Ode.verlet (fun _ (q: float<m>[]) -> q |> Array.map (fun x -> -omega2 * x))) (struct ([| 0.1<m> |], [| 0.0<m/s> |]))
    |> Sim.withInvariant "energy" Derived.joule (fun (struct (q, v)) -> energyOf q.[0] v.[0])

// Each simulation as a trajectory, checkpointed once a period and sampled twenty times a period,
// so that Verlet's band is seen whole rather than at one phase of it. The drift is in joules
// rather than as a fraction of the start, and signed: Euler's is a gain and RK4's a loss.
let samples = 2001

let driftOf (name: string) (sim: Sim<'s>) =
    let trajectory = Sim.trajectory 1.0<s> periods sim
    Sim.drift samples trajectory sim |> List.head |> Drift.absolute |> Drift.named name

let drifts = [ driftOf "Euler" euler; driftOf "RK4" rk4; driftOf "Verlet" verlet ]

// One panel each. The three differ by fifteen orders of magnitude, so a shared axis would show
// Euler and two flat lines; on its own axis each picks its own prefix: MJ for Euler, nJ for RK4
// and µJ for Verlet, whose band is wider than RK4's loss for all hundred periods and never grows.
let panel (d: Drift) = Drift.chart [ d ] |> Chart.withPanelTitle d.Name

let figure =
    panel drifts.[0]
    |> Chart.above (panel drifts.[1])
    |> Chart.above (panel drifts.[2])
    |> Chart.withTitle "Energy drift over 100 periods"
    |> Chart.withSubtitle "a mass on a spring, the same step for all three, h = T/200"

let out = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "integrators.svg")
figure |> Chart.toSvg SvgBackend.render out (Chart.size 800.0 720.0)
printfn "wrote %s" out

let final (d: Drift) = d.Values.[d.Values.Length - 1]
// The largest |ΔE| between two whole periods.
let peakIn (d: Drift) (lo: int) (hi: int) = d.Values.[lo * 20 .. hi * 20] |> Array.map abs |> Array.max

for d in drifts do
    printfn "%-7s ΔE after 100 periods: %.3g J" d.Name (final d)

// The three claims the figure makes, as checks.
let e, r, v = drifts.[0], drifts.[1], drifts.[2]
printfn "Euler gains, and grows:  %b" (final e > 0.0 && peakIn e 90 100 > 1e6 * peakIn e 0 10)
printfn "RK4 loses, and grows:    %b" (final r < 0.0 && peakIn r 90 100 > 5.0 * peakIn r 0 10)
printfn "Verlet stays in a band:  %b" (peakIn v 90 100 <= 1.01 * peakIn v 0 10)
