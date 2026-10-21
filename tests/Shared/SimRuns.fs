/// M5.5 phase 11, gate 10 — simulations the .NET suite and Node both run, printed to twelve
/// decimals and compared with one file, `golden/sim-runs.txt`.
///
/// **No chaotic system, and nothing adaptive with a transcendental in its force**: Node's
/// `sin` and `exp` differ from .NET's in the last bit for a few percent of arguments, a chaotic
/// system turns that into a different trajectory, and an adaptive controller into a different
/// step sequence. What is here keeps an ulp an ulp, so twelve decimals agree, and a platform call
/// that crept into a step (a formatting, a rounding, a `Math` function Fable maps differently)
/// shows as a line that does not.
///
/// Printed with `Num.fixedPoint`, Marey's own, because `sprintf "%g"` writes small numbers
/// differently under Node.
module Marey.Tests.SimRuns

open Marey
open Marey.UnitsOfMeasure

let private line (name: string) (values: float list) =
    name + " " + (values |> List.map (Num.fixedPoint 12) |> String.concat " ")

/// A Verlet oscillator of period 1 s, sampled at the end of each of ten periods.
let private oscillator () =
    let w2 = 4.0 * System.Math.PI * System.Math.PI * 1.0</s^2>
    let sim = Sim.ofStep 1e-3<s> (Ode.verlet (fun _ (q: float<m>[]) -> q |> Array.map (fun x -> -w2 * x))) (struct ([| 1.0<m> |], [| 0.0<m/s> |]))
    let states = Sim.states 10_000 sim

    [ for k in 1..10 do
          let struct (q, v) = states.[k * 1000]
          yield line (sprintf "verlet %2d" k) [ float q.[0]; float v.[0] ] ]

/// A damped pendulum driven at 1.3/s, by RK4, within its first period of about 2 s.
let private pendulum () =
    let flow: Flow2T<rad, rad/s, s> =
        fun t th om -> struct (om, -9.81<rad/s^2> * sin (float th) - 0.2</s> * om + 0.5<rad/s^2> * cos (1.3 * float t))

    let traj = Sim.ofStep 1e-3<s> (Ode.rk4 flow) (struct (0.5<rad>, 0.0<rad/s>)) |> Sim.trajectory 0.5<s> 2.0<s>

    [ for k in 1..8 do
          let struct (th, om) = traj.At(float k * 0.25<s>)
          yield line (sprintf "pendulum %.2f" (float k * 0.25)) [ float th; float om ] ]

/// A ball bouncing with restitution 0.8, Verlet under gravity alone, located by the event
/// machinery: the first five bounce times.
let private ball () =
    let floor = Event.create "floor" Direction.Falling (Response.reflect 0 0.8) (fun (struct (q: float<m>[], _)) -> q.[0])

    let sim =
        Sim.ofStep 1e-3<s> (Ode.verlet (fun _ (q: float<m>[]) -> q |> Array.map (fun _ -> -9.81<m/s^2>))) (struct ([| 1.0<m> |], [| 0.0<m/s> |]))
        |> Sim.withEvents [ floor ]

    Sim.events 3.0<s> sim |> List.truncate 5 |> List.mapi (fun i h -> line (sprintf "bounce %d" (i + 1)) [ float h.Time ])

/// Sixty hard discs on a torus from an arithmetic start, fifty steps: collisions, and two discs.
let private gas () =
    let n = 60
    let x = Array.init n (fun i -> (float (i % 8) + 0.5) * 0.125<m>)
    let y = Array.init n (fun i -> (float (i / 8) + 0.5) * 0.125<m>)
    let vx = Array.init n (fun i -> (float ((i * 7919) % 1000) / 500.0 - 1.0) * 1.0<m/s>)
    let vy = Array.init n (fun i -> (float ((i * 104729) % 997) / 498.5 - 1.0) * 1.0<m/s>)
    let col = Collide.create (Array.create n 0.03<m>) (Array.init n (fun i -> 1.0 + float (i % 3))) 1.0 (Box.Periodic(1.0<m>, 1.0<m>))
    let d = Sim.ofStep 0.01<s> (Collide.step col) (Discs.create x y vx vy) |> Sim.run 50
    [ line "gas collisions" [ float d.Collisions ]; line "gas disc 7" [ float d.X.[7]; float d.Y.[7] ]; line "gas disc 31" [ float d.VX.[31]; float d.VY.[31] ] ]

/// A plate by Crank–Nicolson with Dirichlet, Neumann and periodic sides, and a Poisson solve.
let private grids () =
    let plate = ScalarGrid.sample (Grid.linear 0.0<m> 1.0<m> 21) (Grid.linear 0.0<m> 0.95<m> 20) (fun x y -> float (x * x) * 1.0<K> + float y * 0.5<K>)

    let sides =
        { Boundaries.all Boundary.Periodic with
            Left = Boundary.Dirichlet 1.0<K>
            Right = Boundary.Neumann 0.5<K/m> }

    let hot = Sim.ofStep 0.1<s> (Fdm.heatImplicit 0.01<m^2/s> sides) plate |> Sim.run 20
    let f = ScalarGrid.sample (Grid.linear 0.0<m> 1.0<m> 17) (Grid.linear 0.0<m> 1.0<m> 17) (fun x y -> float (x * y) * 1.0<V/m^2>)
    let poisson = Fdm.poisson (Boundaries.all (Boundary.Dirichlet 0.0<V>)) 1e-10 10_000 f
    [ line "heat" [ float hot.Values.[0]; float hot.Values.[205]; float hot.Values.[419] ]
      line "poisson" [ float poisson.Iterations; float poisson.Solution.Values.[144] ] ]

/// Stam in a closed box and on a torus, and lattice Boltzmann past an obstacle, from
/// polynomial starts. The steps are arithmetic but for the multigrid's coarsest factor,
/// `cos (π/m)`, which reaches a printed digit through a factor that only speeds convergence.
let private fluids () =
    let stam (edge: FluidEdge) =
        Fluid.box edge edge 0.05<m> 24 16
        |> Fluid.withVelocity (fun x y -> struct ((x * (1.2<m> - x) * (y - 0.4<m>)) * 4.0</(m^2 s)>, (y * (0.8<m> - y) * (x - 0.6<m>)) * -4.0</(m^2 s)>))
        |> Fluid.withDye (fun x y -> if x > 0.4<m> && x < 0.8<m> && y < 0.3<m> then 1.0<K> else 0.0<K>)

    let options =
        { Stam.defaults<m, K> with
            Viscosity = 1e-3<m^2/s>
            Diffusion = 1e-3<m^2/s>
            Buoyancy = 0.5<m/s^2/K>
            Source = fun x y -> if x > 0.55<m> && x < 0.65<m> && y < 0.1<m> then 2.0<K/s> else 0.0<K/s> }

    let walls = Sim.ofStep 0.02<s> (Stam.step options) (stam FluidEdge.Wall) |> Sim.run 20
    let torus = Sim.ofStep 0.02<s> (Stam.step options) (stam FluidEdge.Periodic) |> Sim.run 20

    let dx, dt = 1e-3<m>, 1e-3<s>

    let lattice =
        Lattice.create dx dt 30 12
        |> Lattice.withObstacle (fun x y -> x > 0.008<m> && x < 0.012<m> && y > 0.004<m> && y < 0.007<m>)
        |> Lattice.withVelocity (fun _ y -> struct (0.03<m/s>, (y - 0.006<m>) * 2.0</s>))

    let lbmOptions =
        { Lbm.defaults (0.05 * dx * dx / dt) with
            Left = LatticeEdge.Inflow(0.03<m/s>, 0.0<m/s>)
            Right = LatticeEdge.Outflow
            Bottom = LatticeEdge.Wall
            Top = LatticeEdge.Wall }

    let flowed = Sim.ofStep dt (Lbm.step lbmOptions) lattice |> Sim.run 100
    let v = Lattice.velocity flowed

    [ line "stam walls" [ float walls.UFaces.[160]; float walls.VFaces.[200]; float walls.Dye.[205]; float walls.Pressure.[100] ]
      line "stam torus" [ float torus.UFaces.[160]; float torus.VFaces.[200]; float torus.Dye.[205]; float torus.Pressure.[100] ]
      line "lbm" [ float v.U.[6 * 30 + 20]; float v.W.[6 * 30 + 20]; float (Lattice.density flowed).Values.[3 * 30 + 25] ] ]

/// Every line, in order: what `golden/sim-runs.txt` holds.
let text () : string =
    [ oscillator (); pendulum (); ball (); gas (); grids (); fluids () ] |> List.concat |> String.concat "\n" |> fun s -> s + "\n"
