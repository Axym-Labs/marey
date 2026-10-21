(*  A pendulum's phase portrait: where it can rest, and what happens near each place.

    Run it:  dotnet fsi examples/pendulum.fsx
    Writes:  pendulum.svg and pendulum-damped.svg in the current directory.

    A pendulum's state is its angle θ and its angular velocity ω, so its phase space has a
    different unit on each axis: radians and radians per second. The flow says how the state
    changes, θ̇ = ω and ω̇ = −(g/L)·sin θ, in rad/s and rad/s².

    The library finds the fixed points where both rates vanish, classifies each one by its
    linearisation, and prints the eigenvalues in 1/s. It draws them over the streamlines and the
    two nullclines. The pendulum hanging down (θ = 0) is a centre: nearby orbits circle it at
    √(g/L) rad/s. Standing up (θ = ±π) is a saddle. Add friction and each centre becomes a stable
    focus, and phase-space area starts to shrink at the friction's rate. *)

#r "nuget: Marey.Fields"
#r "nuget: Marey.Backend.Svg"

open Marey
open Marey.UnitsOfMeasure

let gOverL = 9.81<1/s^2>
let theta = U.radian
let omega = U.div U.radian U.second

let pendulum (friction: float<1/s>) : Flow<rad, rad/s, s> =
    fun th om -> struct (om, -gOverL * sin (float th) * 1.0<rad> - friction * om)

let sample flow =
    Field.sampleFlow (Grid.linear -6.5<rad> 6.5<rad> 105) (Grid.linear -9.0<rad/s> 9.0<rad/s> 73) flow

let free = sample (pendulum 0.0<1/s>)
let damped = sample (pendulum 0.6<1/s>)

let report (name: string) grid =
    printfn "%s:" name

    for p in Phase.fixedPoints grid do
        printfn "  %s" (FixedPoint.describe theta omega U.second p)

report "free" free
report "damped" damped

let write (file: string) (title: string) grid =
    let out = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), file)

    Phase.portrait theta omega grid
    |> Chart.withTitle title
    |> Chart.toSvg SvgBackend.render out (Chart.size 900.0 520.0)

    printfn "wrote %s" out

write "pendulum.svg" "A pendulum's phase portrait" free
write "pendulum-damped.svg" "The same pendulum, with friction" damped

// The claims, checked.
let kinds grid = Phase.fixedPoints grid |> Array.map (fun p -> p.Kind)

printfn "hanging down is a centre, standing up is a saddle: %b"
    (kinds free = [| FixedPointKind.Centre; FixedPointKind.Saddle; FixedPointKind.Centre; FixedPointKind.Saddle; FixedPointKind.Centre |])

printfn "with friction, every centre is a stable focus: %b"
    (kinds damped |> Array.forall (fun k -> k = FixedPointKind.Saddle || k = FixedPointKind.Focus true))

// Near the bottom the small-angle frequency is √(g/L). Differences on the grid are O(h²), so
// the eigenvalue is close to it rather than equal.
let centre = Phase.fixedPoints free |> Array.find (fun p -> abs (float p.X) < 0.1)
let lambda: float<1/s> = (fst centre.Eigenvalues).Im
printfn "the centre's eigenvalue is √(g/L) to 0.2%%: %b" (abs (lambda - sqrt gOverL) < 0.002 * sqrt gOverL)

// Liouville: the divergence of the flow is the rate phase-space area grows, in 1/s. Without
// friction it is zero everywhere, and with it, minus the friction everywhere.
let divFree: ScalarGrid<rad, rad/s, 1/s> = Field.div free
let divDamped = Field.div damped
printfn "without friction, phase-space area is conserved: %b" (divFree.Values |> Array.forall (fun d -> abs d < 1e-9<1/s>))
printfn "with friction, it shrinks at 0.6 per second: %b" (divDamped.Values |> Array.forall (fun d -> abs (d + 0.6<1/s>) < 1e-9<1/s>))
