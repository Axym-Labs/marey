# 8. Simulation

`Marey.Sim` integrates a system over physical time. A simulation is a start and a way to step, and
its trajectory is an animation (§9): it can be sought to any time, drawn frame by frame, and
filmed.

```fsharp
let springRate = 4.0 * System.Math.PI * System.Math.PI * 1.0</s^2>
let springAccel (_: float<s>) (q: float<m>[]) : float<m/s^2>[] = q |> Array.map (fun x -> -springRate * x)

let springSim = Sim.ofStep 1e-3<s> (Ode.verlet springAccel) (struct ([| 0.1<m> |], [| 0.0<m/s> |]))
let springOrbit = Sim.trajectory 0.5<s> 20.0<s> springSim

let struct (springQ, _) = springOrbit.At 7.1<s>
printfn "a 1 s spring at 7.1 s: %.6f m" (float springQ.[0])
```

`Sim.trajectory every horizon` runs once to the horizon and keeps a checkpoint every `every`; a
seek replays from the checkpoint before it, and is exactly the state a straight run reaches, bit
for bit, on any thread.

## The shape of a state

An integrator is chosen by the shape of the state. Positions and velocities in one unit take
`Ode.verlet` or `Ode.leapfrog`, symplectic, with the acceleration checked in `'q/s²`. A plane with
a clock, a `Flow2T`, takes `Ode.euler` or `Ode.rk4`. Anything else is a record, and takes
`Ode.rk4With` given a `Tangent`: the two operations that advance a state along a rate and combine
two rates, which is where a state's units meet. `Ode.dopri5` is Dormand–Prince 4(5) with a
tolerance per axis, run by `Sim.ofAdaptive`. An acceleration in the wrong unit does not compile:

```fsharp-error
let wrong = Ode.verlet (fun _ (q: float<m>[]) -> q |> Array.map (fun x -> -4.0</s> * x))
```

A double pendulum's state is two angles and two angular velocities, and its rates are in rad/s and
rad/s²:

```fsharp
type Arms = { T1: float<rad>; T2: float<rad>; W1: float<rad/s>; W2: float<rad/s> }
type ArmRates = { DT1: float<rad/s>; DT2: float<rad/s>; DW1: float<rad/s^2>; DW2: float<rad/s^2> }

let armTangent: Tangent<Arms, ArmRates, s> =
    { Axpy = fun h d a -> { T1 = a.T1 + h * d.DT1; T2 = a.T2 + h * d.DT2; W1 = a.W1 + h * d.DW1; W2 = a.W2 + h * d.DW2 }
      Lin = fun p d q e -> { DT1 = p * d.DT1 + q * e.DT1; DT2 = p * d.DT2 + q * e.DT2; DW1 = p * d.DW1 + q * e.DW1; DW2 = p * d.DW2 + q * e.DW2 } }

let gravity = 9.81<m/s^2>
let armLength = 1.0<m>

let armMotion (_: float<s>) (a: Arms) : ArmRates =
    let d = float (a.T1 - a.T2)
    let w1, w2 = float a.W1, float a.W2
    let k = float (gravity / armLength)
    let den = 3.0 - cos (2.0 * d)
    let a1 = (-3.0 * k * sin (float a.T1) - k * sin (float a.T1 - 2.0 * float a.T2) - 2.0 * sin d * (w2 * w2 + w1 * w1 * cos d)) / den
    let a2 = 2.0 * sin d * (2.0 * w1 * w1 + 2.0 * k * cos (float a.T1) + w2 * w2 * cos d) / den
    { DT1 = a.W1; DT2 = a.W2; DW1 = a1 * 1.0<rad/s^2>; DW2 = a2 * 1.0<rad/s^2> }
```

## Invariants, events and sections

A conserved quantity watched over a trajectory is how a broken integrator is caught.
`Sim.withInvariant` names one with its unit, and `Sim.drift` samples it relative to its start:

```fsharp
let armEnergy (a: Arms) : float<J/kg> =
    let w1, w2 = float a.W1, float a.W2
    let kinetic = 0.5 * armLength * armLength * (2.0 * w1 * w1 + w2 * w2 + 2.0 * w1 * w2 * cos (float (a.T1 - a.T2))) * 1.0</s^2>
    kinetic - gravity * armLength * (2.0 * cos (float a.T1) + cos (float a.T2))

let doublePendulum =
    Sim.ofStep 1e-3<s> (Ode.rk4With armTangent armMotion) { T1 = 2.0<rad>; T2 = 2.5<rad>; W1 = 0.0<rad/s>; W2 = 0.0<rad/s> }
    |> Sim.withInvariant "energy" (U.div U.joule U.kilogram) armEnergy

let armTrajectory = Sim.trajectory 1.0<s> 300.0<s> doublePendulum
let energyDrift = Sim.drift 301 armTrajectory doublePendulum

printfn "energy kept to %.1f parts in 10⁸ over 300 s" (1e8 * (energyDrift.Head.Values |> Array.map abs |> Array.max))
```

An event is a function of the state crossing zero in a direction, located in time to a fraction of
a step; its response replaces the state or stops the run. `Event.withMinSeparation` ends a ball's
infinitely many bounces before a finite time:

```fsharp
let floorHit =
    Event.create "floor" Direction.Falling (Response.reflect 0 0.8) (fun (struct (q: float<m>[], _)) -> q.[0])
    |> Event.withMinSeparation 0.01<s>

let bouncing =
    Sim.ofStep 1e-3<s> (Ode.verlet (fun _ (q: float<m>[]) -> q |> Array.map (fun _ -> -9.81<m/s^2>))) (struct ([| 1.0<m> |], [| 0.0<m/s> |]))
    |> Sim.withEvents [ floorHit ]

for hit in Sim.events 5.0<s> bouncing |> List.truncate 3 do
    printfn "%s at %.6f s" hit.Name (float hit.Time)
```

`Poincare.section` is every crossing of a surface in a direction, each point the integrator's own
state on it, and `Poincare.stroboscopic` the state at every multiple of a period. `Collide.step`
steps hard discs with every contact resolved exactly in time order.

## A double pendulum

Released from rest at θ₁ = 2 rad and θ₂ = 2.5 rad, the second bob's path over the first 20 s; the
energy's relative drift over 300 s; and a Poincaré section, the second arm's angle and speed each
time the first swings forward through the vertical:

```fsharp output=double-pendulum size=980x420
let wrapAngle (a: float<rad>) = (float a - 2.0 * System.Math.PI * round (float a / (2.0 * System.Math.PI))) * 1.0<rad>

let bobPath =
    Array.init 2001 (fun i ->
        let a = armTrajectory.At(float i * 0.01<s>)
        let x1, y1 = armLength * sin (float a.T1), -armLength * cos (float a.T1)
        x1 + armLength * sin (float a.T2), y1 - armLength * cos (float a.T2))

let section =
    Poincare.section Direction.Rising (fun (a: Arms) -> sin (float a.T1)) 300.0<s> doublePendulum
    |> Array.filter (fun p -> cos (float p.State.T1) > 0.0)

let pathPanel =
    Chart.line U.metre U.metre (bobPath |> Array.map fst) (bobPath |> Array.map snd)
    |> Chart.withOpacity 0.7
    |> Chart.withXLabel "x"
    |> Chart.withYLabel "y"
    |> Chart.withEqualAspect
    |> Chart.withPanelTitle "the second bob, 20 s"

let sectionPanel =
    Chart.scatter U.radian angularVelocity (section |> Array.map (fun p -> wrapAngle p.State.T2)) (section |> Array.map (fun p -> p.State.W2))
    |> Chart.withPanelTitle "Poincaré section"

let doublePendulumFigure =
    pathPanel
    |> Chart.beside (Drift.chart energyDrift |> Chart.withYTickFormat FormatScientific |> Chart.withPanelTitle "energy drift")
    |> Chart.beside sectionPanel
    |> Chart.withPanelLetters LetterParen

doublePendulumFigure
```

![Three panels: the second bob's tangled path over 20 s, the relative energy drift staying below one part in a million over 300 s, and a scattered Poincaré section](../figures/double-pendulum.svg)

The motion is chaotic: two starts a nanoradian apart differ by 0.1 rad within half a minute. The
energy stays within the integrator's error over the whole run, so the scatter in (c) is the
system's and not the method's.

## Grids and fluids

`Fdm` steps a field on a grid by finite differences, with a boundary per side in the field's unit:
`Fdm.heat` (forward Euler, refused past its stability limit, which is a time), `Fdm.heatImplicit`
(Crank–Nicolson, stable at any step), `Fdm.wave` and `Fdm.poisson`. `Stam.step` is Stam's stable
fluids, incompressible in a box, with a dye in a unit of its own and a buoyancy. `Lbm.step` is
lattice Boltzmann, good at flow round obstacles; the viscosity sets its relaxation time, which must
exceed ½, and a lattice is read at whole ticks. Every state draws with §7's marks. `GpuSim.stam` and
`GpuSim.lbm` run the same steps on a GPU (§13).

Water past a 2 cm cylinder at 0.1 m/s, a Reynolds number of 100, where the wake sheds vortices from
either side in turn. A small sideways push behind the cylinder starts the shedding:

```fsharp output=cylinder-wake size=900x320
let cell, tick = 2e-3<m>, 2e-3<s>
let inflow = 0.1<m/s>
let diameter = 0.02<m>

let channel =
    Lattice.create cell tick 200 50
    |> Lattice.withObstacle (fun x y -> (x - 0.08<m>) * (x - 0.08<m>) + (y - 0.051<m>) * (y - 0.051<m>) <= diameter * diameter / 4.0)
    |> Lattice.withVelocity (fun x _ -> struct (inflow, (if x > 0.09<m> && x < 0.13<m> then 0.1 * inflow else 0.0<m/s>)))

let water =
    { Lbm.defaults (inflow * diameter / 100.0) with
        Left = LatticeEdge.Inflow(inflow, 0.0<m/s>)
        Right = LatticeEdge.Outflow }

printfn "relaxation time τ = %.3f" (Lbm.tau water.Viscosity channel)

let wake = Sim.ofStep tick (Lbm.step water) channel |> Sim.run 4000

Field.heatmapWith Theme.modern.Diverging true U.metre U.metre U.perSecond (Lattice.vorticity wake)
|> Chart.withColorDomain U.perSecond -10.0</s> 10.0</s>
|> Chart.withColorLabel "ω"
|> Chart.overlay (Field.streamlinesWith 14.0<px> U.metre U.metre (Lattice.velocity wake))
|> Chart.withXLabel "x"
|> Chart.withYLabel "y"
|> Chart.withEqualAspect
|> Chart.withTitle "Vorticity behind a cylinder, Re = 100, after 8 s"
```

![The vorticity behind a cylinder in a channel, alternating vortices of opposite sign shed downstream, with streamlines](../figures/cylinder-wake.svg)
