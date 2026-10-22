# Marey.Sim

Simulation for [Marey](https://axym-labs.github.io/marey/). **A simulation is a start and a step,
and its trajectory is an animation**: it seeks, it films, and a seek to any time is exactly the
state a straight run reaches.

```fsharp
#r "nuget: Marey.Sim"
#r "nuget: Marey.Backend.Svg"

open Marey
open Marey.UnitsOfMeasure

// A driven, damped pendulum: an angle in rad and a rate in rad/s, stepped by RK4 at 1 ms.
let pendulum: Flow2T<rad, rad/s, s> =
    fun t th om -> struct (om, -9.81<rad/s^2> * sin (float th) - 0.2</s> * om + 0.5<rad/s^2> * cos (1.3 * float t))

let sim = Sim.ofStep 1e-3<s> (Ode.rk4 pendulum) (struct (0.5<rad>, 0.0<rad/s>))
let trajectory = Sim.trajectory 1.0<s> 60.0<s> sim

let path = Array.init 600 (fun i -> trajectory.At(float i * 0.1<s>))

Chart.lineIn U.radian (U.div U.radian U.second) (path |> Array.map (fun (struct (th, _)) -> th)) (path |> Array.map (fun (struct (_, om)) -> om))
|> Chart.toSvg SvgBackend.render "pendulum.svg" (Chart.size 640.0 480.0)
```

- **Integrators typed by the shape of the state.** `Ode.verlet` and `Ode.leapfrog` over positions
  and velocities in one unit, with the acceleration checked to be in that unit per second squared;
  `Ode.euler`, `Ode.rk4` and `Ode.dopri5` (Dormand–Prince 4(5), dense output) over a plane flow
  with time; and `eulerWith`, `rk4With`, `dopri5With` over any record given a `Tangent`. A rate in
  the wrong unit does not compile.
- **Trajectories.** `Sim.trajectory every horizon` checkpoints one straight run and seeks by
  replay, bit for bit equal to the run, on any thread. Invariants (`Sim.withInvariant`) and their
  drift (`Sim.drift`, `Drift.chart`).
- **Events.** A function of the state crossing zero, located in time, with a response; a minimum
  separation ends a Zeno sequence at rest instead of looping. `Collide.step` resolves hard-disc
  contacts exactly within a step, with a grid broad phase.
- **Sections and particles.** `Poincare.stroboscopic` and `Poincare.section`. `Unsteady` fields,
  analytic or from grids (`ofSnapshots`, `ofFunction`), carried by `Particles.step` stage by
  stage; streaklines and pathlines.
- **Grid solvers.** `Fdm.heat` (explicit, with its stability limit refused in seconds),
  `Fdm.heatImplicit` (Crank–Nicolson by ADI), `Fdm.wave`, `Fdm.poisson`, on the compact five-point
  stencil with Dirichlet, Neumann and periodic sides.
- **Fluids.** `Stam.step`, stable fluids on a staggered grid with the pressure by multigrid, walls
  or periodic edges, viscosity, a dye in its own unit and a buoyancy per unit of it; `Lbm.step`,
  lattice Boltzmann D2Q9 with obstacles, walls, an inflow and an outflow, refusing a relaxation
  time at or below ½ with the numbers. `Marey.Backend.Gpu` steps both on a GPU (`GpuSim`).

Compiles under Fable. A chaotic system run in a browser and the same one under .NET are different
trajectories after tens of seconds, because JavaScript's `sin` and `exp` differ in the last bit;
both are correct to the integrator's order.

- [The guide on simulation](https://axym-labs.github.io/marey/guide/08-simulation.html)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples): `integrators.fsx`,
  `double-pendulum.fsx`, `double-gyre.fsx`, `heat.fsx`, `fluid.fsx`, `cylinder.fsx`
