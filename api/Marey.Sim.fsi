namespace Marey

    type Step<'s,[<Measure>] 't> = float<'t> -> float<'t> -> 's -> 's

    type Flow2T<[<Measure>] 'x,[<Measure>] 'y,[<Measure>] 't> =
        float<'t> ->
          float<'x> -> float<'y> -> struct (float<'x/'t> * float<'y/'t>)

    type Tangent<'s,'ds,[<Measure>] 't> =
        {

          Axpy: (float<'t> -> 'ds -> 's -> 's)

          Lin: (float -> 'ds -> float -> 'ds -> 'ds)
        }

    [<RequireQualifiedAccess>]
    module Tangent =

        val flow2<[<Measure>] 'x,[<Measure>] 'y,[<Measure>] 't> :
          Tangent<struct (float<'x> * float<'y>),
                  struct (float<'x/'t> * float<'y/'t>),'t>

        val secondOrder<[<Measure>] 'q,[<Measure>] 't> :
          Tangent<struct (float<'q> array * float<'q/'t> array),
                  struct (float<'q/'t> array * float<'q/'t ^ 2> array),'t>

    [<Struct>]
    type Controller<[<Measure>] 't> =
        {
          H: float<'t>
          ErrOld: float
        }

    [<RequireQualifiedAccess>]
    module Controller =

        val start: h: float<'t> -> Controller<'t>

    type Accepted<'s,[<Measure>] 't> =
        {
          T0: float<'t>
          T1: float<'t>

          State: 's

          Next: Controller<'t>

          Dense: (float<'t> -> 's)

          Rejected: int
        }

    type AdaptiveStep<'s,[<Measure>] 't> =
        float<'t> -> Controller<'t> -> 's -> Accepted<'s,'t>

    type ErrorNorm<'s,'ds,[<Measure>] 't> =
        float<'t> -> 'ds -> 's -> 's -> float

    [<RequireQualifiedAccess>]
    module ErrorNorm =

        val flow2:
          atolX: float<'x> ->
            atolY: float<'y> ->
            rtol: float ->
            ErrorNorm<struct (float<'x> * float<'y>),
                      struct (float<'x/'t> * float<'y/'t>),'t>

        val secondOrder:
          atolQ: float<'q> ->
            atolV: float<'q/'t> ->
            rtol: float ->
            ErrorNorm<struct (float<'q> array * float<'q/'t> array),
                      struct (float<'q/'t> array * float<'q/'t ^ 2> array),'t>

    [<RequireQualifiedAccess>]
    module Ode =

        val ofFlow:
          f: Flow<'x,'y,'t> ->
            float<'t> ->
            x: float<'x> -> y: float<'y> -> struct (float<'x/'t> * float<'y/'t>)

        val eulerWith:
          tangent: Tangent<'s,'ds,'t> ->
            f: (float<'t> -> 's -> 'ds) ->
            t: float<'t> -> h: float<'t> -> s: 's -> 's

        val rk4With:
          tangent: Tangent<'s,'ds,'t> ->
            f: (float<'t> -> 's -> 'ds) ->
            t: float<'t> -> h: float<'t> -> s: 's -> 's

        val euler:
          f: Flow2T<'x,'y,'t> ->
            t: float<'t> ->
            h: float<'t> ->
            struct (float<'x> * float<'y>) -> struct (float<'x> * float<'y>)

        val rk4:
          f: Flow2T<'x,'y,'t> ->
            t: float<'t> ->
            h: float<'t> ->
            struct (float<'x> * float<'y>) -> struct (float<'x> * float<'y>)

        val verlet:
          a: (float<'t> -> float<'q> array -> float<'q/'t ^ 2> array) ->
            t: float<'t> ->
            h: float<'t> ->
            struct (float<'q> array * float<'q/'t> array) ->
            struct (float<'q> array * float<'q/'t> array)

        val leapfrog:
          a: (float<'t> -> float<'q> array -> float<'q/'t ^ 2> array) ->
            t: float<'t> ->
            h: float<'t> ->
            struct (float<'q> array * float<'q/'t> array) ->
            struct (float<'q> array * float<'q/'t> array)

        val verletInPlace:
          kick: (float<'t> ->
                   float<'q> array -> float<'t> -> float<'q/'t> array -> unit) ->
            t: float<'t> ->
            h: float<'t> ->
            struct (float<'q> array * float<'q/'t> array) ->
            struct (float<'q> array * float<'q/'t> array)

        val dopri5With:
          tangent: Tangent<'s,'ds,'t> ->
            norm: ErrorNorm<'s,'ds,'t> ->
            f: (float<'t> -> 's -> 'ds) -> AdaptiveStep<'s,'t>

        val dopri5:
          atolX: float<'x> ->
            atolY: float<'y> ->
            rtol: float ->
            f: Flow2T<'x,'y,'t> ->
            AdaptiveStep<struct (float<'x> * float<'y>),'t>

        val withMaxStep:
          hmax: float<'t> -> step: AdaptiveStep<'s,'t> -> AdaptiveStep<'s,'t>

namespace Marey

    [<RequireQualifiedAccess>]
    type Direction =

        | Rising

        | Falling

        | Either

    [<RequireQualifiedAccess>]
    type Response<'s> =

        | Continue of ('s -> 's)

        | Stop

    type Event<'s> =
        {
          Name: string

          Of: ('s -> float)
          Direction: Direction
          Response: Response<'s>

          Rest: ('s -> 's) option

          MinSeparation: float<Data.UnitSystems.SI.UnitSymbols.s> option

          Tolerance: float
        }

    [<RequireQualifiedAccess>]
    type HitKind =

        | Fired

        | Rested

        | Stopped of reason: string

    type Hit =
        {
          Name: string
          Time: float<Data.UnitSystems.SI.UnitSymbols.s>
          Kind: HitKind
        }

    [<RequireQualifiedAccess>]
    module Event =

        val create:
          name: string ->
            direction: Direction ->
            response: Response<'s> -> g: ('s -> float<'g>) -> Event<'s>

        val withRest:
          separation: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            rest: ('s -> 's) -> e: Event<'s> -> Event<'s>

        val withMinSeparation:
          separation: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            e: Event<'s> -> Event<'s>

        val withTolerance: fraction: float -> e: Event<'s> -> Event<'s>

        val crosses: direction: Direction -> g0: float -> g1: float -> bool

        val locate:
          phi: (float -> float) ->
            a: float ->
            b: float -> ga: float -> gb: float -> tol: float -> float

    [<RequireQualifiedAccess>]
    module Response =

        val reflect:
          axis: int ->
            e: float -> Response<struct (float<'q> array * float<'q/'t> array)>

namespace Marey

    [<RequireQualifiedAccess>]
    type Stepping<'s> =

        | Fixed of
          h: float<Data.UnitSystems.SI.UnitSymbols.s> *
          step: Step<'s,Data.UnitSystems.SI.UnitSymbols.s>

        | Adaptive of
          h0: float<Data.UnitSystems.SI.UnitSymbols.s> *
          step: AdaptiveStep<'s,Data.UnitSystems.SI.UnitSymbols.s>

    type Invariant<'s> =
        {
          Name: string
          Unit: IUnit
          Of: ('s -> float)
        }

    type Sim<'s> =
        {

          Initial: 's

          Stepping: Stepping<'s>

          Checkpoint: ('s -> 's)

          Invariants: Invariant<'s> list

          Events: Event<'s> list
        }

    type Drift =
        {
          Name: string

          Unit: IUnit
          Relative: bool

          Initial: float
          Times: float<Data.UnitSystems.SI.UnitSymbols.s> array
          Values: float array
        }

    [<RequireQualifiedAccess>]
    module Sim =

        val ofStep:
          h: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            step: Step<'s,Data.UnitSystems.SI.UnitSymbols.s> ->
            initial: 's -> Sim<'s>

        val ofAdaptive:
          h0: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            step: AdaptiveStep<'s,Data.UnitSystems.SI.UnitSymbols.s> ->
            initial: 's -> Sim<'s>

        val withCheckpoint: copy: ('s -> 's) -> sim: Sim<'s> -> Sim<'s>

        val withInvariant:
          name: string -> f: ('s -> float) -> sim: Sim<'s> -> Sim<'s>

        val withEvents: events: Event<'s> list -> sim: Sim<'s> -> Sim<'s>

        val run: n: int -> sim: Sim<'s> -> 's

        val at:
          t: float<Data.UnitSystems.SI.UnitSymbols.s> -> sim: Sim<'s> -> 's

        val events:
          horizon: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            sim: Sim<'s> -> Hit list

        val states: n: int -> sim: Sim<'s> -> 's array

        val times:
          n: int ->
            sim: Sim<'s> -> float<Data.UnitSystems.SI.UnitSymbols.s> array

        [<Literal>]
        val DefaultCheckpointBytes: int64 = 536870912L

        val trajectoryWithin:
          maxBytes: int64 ->
            every: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            horizon: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            sim: Sim<'s> -> Anim<'s>

        val trajectory:
          every: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            horizon: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            sim: Sim<'s> -> Anim<'s>

        val drift:
          samples: int -> trajectory: Anim<'s> -> sim: Sim<'s> -> Drift list

    [<RequireQualifiedAccess>]
    module Drift =

        val named: name: string -> d: Drift -> Drift

        val absolute: d: Drift -> Drift

        val magnitude: d: Drift -> Drift

        val chart: drifts: Drift list -> Figure

namespace Marey

    [<RequireQualifiedAccess>]
    type Box<[<Measure>] 'q> =

        | Open

        | Walls of
          xMin: float<'q> * xMax: float<'q> * yMin: float<'q> * yMax: float<'q>

        | Periodic of width: float<'q> * height: float<'q>

    type Discs<[<Measure>] 'q> =
        {
          X: float<'q> array
          Y: float<'q> array
          VX: float<'q/Data.UnitSystems.SI.UnitSymbols.s> array
          VY: float<'q/Data.UnitSystems.SI.UnitSymbols.s> array

          Collisions: int64

          WallHits: int64

          Capped: int64
        }

    type Collider<[<Measure>] 'q> =
        {
          Radii: float<'q> array

          Masses: float array

          Restitution: float
          Box: Box<'q>

          CapPerStep: int

          BroadPhaseAbove: int
        }

    [<RequireQualifiedAccess>]
    module Discs =

        val create:
          x: float<'q> array ->
            y: float<'q> array ->
            vx: float<'q/Data.UnitSystems.SI.UnitSymbols.s> array ->
            vy: float<'q/Data.UnitSystems.SI.UnitSymbols.s> array -> Discs<'q>

        val copy: d: Discs<'q> -> Discs<'q>

        val kinetic:
          masses: float array ->
            d: Discs<'q> -> float<'q ^ 2/Data.UnitSystems.SI.UnitSymbols.s ^ 2>

        val momentum:
          masses: float array ->
            d: Discs<'q> ->
            struct
              (float<'q/Data.UnitSystems.SI.UnitSymbols.s> *
               float<'q/Data.UnitSystems.SI.UnitSymbols.s>)

    [<RequireQualifiedAccess>]
    module Collide =

        val create:
          radii: float<'q> array ->
            masses: float array -> e: float -> box: Box<'q> -> Collider<'q>

        val withCap: cap: int -> c: Collider<'q> -> Collider<'q>

        val withBroadPhaseAbove: n: int -> c: Collider<'q> -> Collider<'q>

        val step:
          col: Collider<'q> -> Step<Discs<'q>,Data.UnitSystems.SI.UnitSymbols.s>

namespace Marey

    type SectionPoint<'s> =
        {
          Time: float<Data.UnitSystems.SI.UnitSymbols.s>
          State: 's
        }

    [<RequireQualifiedAccess>]
    module Poincare =

        val stroboscopic:
          period: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            trajectory: Anim<'s> -> SectionPoint<'s> array

        val section:
          direction: Direction ->
            surface: ('s -> float<'g>) ->
            horizon: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            sim: Sim<'s> -> SectionPoint<'s> array

namespace Marey

    type Unsteady<[<Measure>] 'x,[<Measure>] 'y,[<Measure>] 't> =
        Flow2T<'x,'y,'t>

    [<RequireQualifiedAccess>]
    module Unsteady =

        val ofSteady: f: Flow<'x,'y,'t> -> Unsteady<'x,'y,'t>

        val ofSnapshots:
          times: float<'t> array ->
            grids: VectorGrid<'x,'y,'x/'t,'y/'t> array -> Unsteady<'x,'y,'t>

        val ofFunction:
          f: (float<'t> -> VectorGrid<'x,'y,'x/'t,'y/'t>) -> Unsteady<'x,'y,'t>

    type Particles<[<Measure>] 'x,[<Measure>] 'y> =
        struct (float<'x> array * float<'y> array)

    [<RequireQualifiedAccess>]
    module Particles =

        val step:
          field: Unsteady<'x,'y,'t> ->
            t: float<'t> -> h: float<'t> -> Particles<'x,'y> -> Particles<'x,'y>

        val streakStep:
          seedsX: float<'x> array ->
            seedsY: float<'y> array ->
            field: Unsteady<'x,'y,'t> -> Step<Particles<'x,'y>,'t>

        val pathlines:
          times: float<Data.UnitSystems.SI.UnitSymbols.s> array ->
            trajectory: Anim<Particles<'x,'y>> ->
            struct (float<'x> array * float<'y> array) array

namespace Marey

    [<RequireQualifiedAccess>]
    type Boundary<[<Measure>] 'x,[<Measure>] 'v> =

        | Dirichlet of float<'v>

        | Neumann of float<'v/'x>

        | Periodic

    type Boundaries<[<Measure>] 'x,[<Measure>] 'v> =
        {
          Left: Boundary<'x,'v>
          Right: Boundary<'x,'v>
          Bottom: Boundary<'x,'v>
          Top: Boundary<'x,'v>
        }

    [<RequireQualifiedAccess>]
    module Boundaries =

        val all: b: Boundary<'x,'v> -> Boundaries<'x,'v>

    type PoissonSolution<[<Measure>] 'x,[<Measure>] 'u> =
        {
          Solution: ScalarGrid<'x,'x,'u>

          Iterations: int

          Residual: float<'u/'x ^ 2>

          Converged: bool
        }

    [<RequireQualifiedAccess>]
    module Fdm =

        val laplacian5:
          boundaries: Boundaries<'x,'v> ->
            grid: ScalarGrid<'x,'x,'v> -> ScalarGrid<'x,'x,'v/'x ^ 2>

        val explicitLimit:
          alpha: float<'x ^ 2/Data.UnitSystems.SI.UnitSymbols.s> ->
            grid: ScalarGrid<'x,'x,'v> ->
            float<Data.UnitSystems.SI.UnitSymbols.s>

        val heat:
          alpha: float<'x ^ 2/Data.UnitSystems.SI.UnitSymbols.s> ->
            boundaries: Boundaries<'x,'v> ->
            Step<ScalarGrid<'x,'x,'v>,Data.UnitSystems.SI.UnitSymbols.s>

        val heatImplicit:
          alpha: float<'x ^ 2/Data.UnitSystems.SI.UnitSymbols.s> ->
            boundaries: Boundaries<'x,'v> ->
            Step<ScalarGrid<'x,'x,'v>,Data.UnitSystems.SI.UnitSymbols.s>

        val waveLimit:
          c: float<'x/Data.UnitSystems.SI.UnitSymbols.s> ->
            grid: ScalarGrid<'x,'x,'v> ->
            float<Data.UnitSystems.SI.UnitSymbols.s>

        val wave:
          c: float<'x/Data.UnitSystems.SI.UnitSymbols.s> ->
            boundaries: Boundaries<'x,'v> ->
            Step<struct
                   (ScalarGrid<'x,'x,'v> *
                    ScalarGrid<'x,'x,'v/Data.UnitSystems.SI.UnitSymbols.s>),
                 Data.UnitSystems.SI.UnitSymbols.s>

        val poisson:
          boundaries: Boundaries<'x,'u> ->
            tolerance: float ->
            maxSweeps: int ->
            f: ScalarGrid<'x,'x,'u/'x ^ 2> -> PoissonSolution<'x,'u>

namespace Marey

    [<RequireQualifiedAccess>]
    type FluidEdge =

        | Wall

        | Periodic

    type Fluid<[<Measure>] 'x,[<Measure>] 'q> =
        {
          Nx: int
          Ny: int
          Cell: float<'x>
          X0: float<'x>
          Y0: float<'x>
          XEdge: FluidEdge
          YEdge: FluidEdge
          UFaces: float<'x/Data.UnitSystems.SI.UnitSymbols.s> array
          VFaces: float<'x/Data.UnitSystems.SI.UnitSymbols.s> array
          Dye: float<'q> array
          Pressure: float<'x ^ 2/Data.UnitSystems.SI.UnitSymbols.s ^ 2> array
        }

    type StamOptions<[<Measure>] 'x,[<Measure>] 'q> =
        {

          Viscosity: float<'x ^ 2/Data.UnitSystems.SI.UnitSymbols.s>

          Diffusion: float<'x ^ 2/Data.UnitSystems.SI.UnitSymbols.s>

          Buoyancy: float<'x/('q Data.UnitSystems.SI.UnitSymbols.s ^ 2)>

          Force:
            (float<'x> ->
               float<'x> ->
               struct
                 (float<'x/Data.UnitSystems.SI.UnitSymbols.s ^ 2> *
                  float<'x/Data.UnitSystems.SI.UnitSymbols.s ^ 2>))

          Source:
            (float<'x> ->
               float<'x> -> float<'q/Data.UnitSystems.SI.UnitSymbols.s>)

          Cycles: int

          Sweeps: int
        }

    [<RequireQualifiedAccess>]
    module Fluid =

        val box:
          x: FluidEdge ->
            y: FluidEdge -> h: float<'x> -> nx: int -> ny: int -> Fluid<'x,'q>

        val withOrigin:
          x0: float<'x> -> y0: float<'x> -> f: Fluid<'x,'q> -> Fluid<'x,'q>

        val withVelocity:
          velocity: (float<'x> ->
                       float<'x> ->
                       struct
                         (float<'x/Data.UnitSystems.SI.UnitSymbols.s> *
                          float<'x/Data.UnitSystems.SI.UnitSymbols.s>)) ->
            f: Fluid<'x,'q> -> Fluid<'x,'q>

        val withDye:
          dye: (float<'x> -> float<'x> -> float<'q>) ->
            f: Fluid<'x,'q> -> Fluid<'x,'q>

        val velocity:
          f: Fluid<'x,'q> ->
            VectorGrid<'x,'x,'x/Data.UnitSystems.SI.UnitSymbols.s,
                       'x/Data.UnitSystems.SI.UnitSymbols.s>

        val dye: f: Fluid<'x,'q> -> ScalarGrid<'x,'x,'q>

        val divergence:
          f: Fluid<'x,'q> ->
            ScalarGrid<'x,'x,/Data.UnitSystems.SI.UnitSymbols.s>

        val pressure:
          f: Fluid<'x,'q> ->
            ScalarGrid<'x,'x,'x ^ 2/Data.UnitSystems.SI.UnitSymbols.s ^ 2>

        val vorticity:
          f: Fluid<'x,'q> ->
            ScalarGrid<'x,'x,/Data.UnitSystems.SI.UnitSymbols.s>

    [<RequireQualifiedAccess>]
    module Stam =

        val defaults<[<Measure>] 'x,[<Measure>] 'q> : StamOptions<'x,'q>

        val step:
          options: StamOptions<'x,'q> ->
            Step<Fluid<'x,'q>,Data.UnitSystems.SI.UnitSymbols.s>

namespace Marey

    [<RequireQualifiedAccess>]
    type LatticeEdge<[<Measure>] 'x> =

        | Periodic

        | Wall

        | Inflow of
          float<'x/Data.UnitSystems.SI.UnitSymbols.s> *
          float<'x/Data.UnitSystems.SI.UnitSymbols.s>

        | Outflow

    type Lattice<[<Measure>] 'x> =
        {
          Nx: int
          Ny: int
          Spacing: float<'x>
          Tick: float<Data.UnitSystems.SI.UnitSymbols.s>
          X0: float<'x>
          Y0: float<'x>
          Populations: float array
          Solid: bool array
        }

    type LbmOptions<[<Measure>] 'x> =
        {
          Viscosity: float<'x ^ 2/Data.UnitSystems.SI.UnitSymbols.s>

          Force:
            float<'x/Data.UnitSystems.SI.UnitSymbols.s ^ 2> *
            float<'x/Data.UnitSystems.SI.UnitSymbols.s ^ 2>
          Left: LatticeEdge<'x>
          Right: LatticeEdge<'x>
          Bottom: LatticeEdge<'x>
          Top: LatticeEdge<'x>
        }

    [<RequireQualifiedAccess>]
    module Lattice =

        val create:
          spacing: float<'x> ->
            tick: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            nx: int -> ny: int -> Lattice<'x>

        val withOrigin:
          x0: float<'x> -> y0: float<'x> -> l: Lattice<'x> -> Lattice<'x>

        val withObstacle:
          inside: (float<'x> -> float<'x> -> bool) ->
            l: Lattice<'x> -> Lattice<'x>

        val withVelocity:
          velocity: (float<'x> ->
                       float<'x> ->
                       struct
                         (float<'x/Data.UnitSystems.SI.UnitSymbols.s> *
                          float<'x/Data.UnitSystems.SI.UnitSymbols.s>)) ->
            l: Lattice<'x> -> Lattice<'x>

        val density: l: Lattice<'x> -> ScalarGrid<'x,'x,1>

        val velocityUnder:
          float<'x/Data.UnitSystems.SI.UnitSymbols.s ^ 2> *
          float<'x/Data.UnitSystems.SI.UnitSymbols.s ^ 2> ->
            l: Lattice<'x> ->
            VectorGrid<'x,'x,'x/Data.UnitSystems.SI.UnitSymbols.s,
                       'x/Data.UnitSystems.SI.UnitSymbols.s>

        val velocity:
          l: Lattice<'x> ->
            VectorGrid<'x,'x,'x/Data.UnitSystems.SI.UnitSymbols.s,
                       'x/Data.UnitSystems.SI.UnitSymbols.s>

        val vorticity:
          l: Lattice<'x> -> ScalarGrid<'x,'x,/Data.UnitSystems.SI.UnitSymbols.s>

        val mass: l: Lattice<'x> -> float<'x ^ 2>

    [<RequireQualifiedAccess>]
    module Lbm =

        val tau:
          viscosity: float<'x ^ 2/Data.UnitSystems.SI.UnitSymbols.s> ->
            l: Lattice<'x> -> float

        [<Literal>]
        val MaxMach: float = 0.3

        val defaults:
          viscosity: float<'x ^ 2/Data.UnitSystems.SI.UnitSymbols.s> ->
            LbmOptions<'x>

        val validate:
          options: LbmOptions<'x> ->
            l: Lattice<'x> ->
            h: float<Data.UnitSystems.SI.UnitSymbols.s> -> float

        val step:
          options: LbmOptions<'x> ->
            float<Data.UnitSystems.SI.UnitSymbols.s> ->
            h: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            l: Lattice<'x> -> Lattice<'x>

namespace Marey.UnitsOfMeasure

    [<RequireQualifiedAccess>]
    module Sim =

        val withInvariant:
          name: string ->
            u: Marey.U<'e> ->
            f: ('s -> float<'e>) -> sim: Marey.Sim<'s> -> Marey.Sim<'s>
