namespace Marey

open System

// ======================================================================================
// UNSTEADY FIELDS, PATHLINES, PARTICLES.
//
// A pathline is an ODE trajectory through a velocity field that depends on time, so its input is
// the integrators' second-tier function itself: `t -> x -> y -> (dx/dt, dy/dt)`. An analytic field
// (the double gyre, a rotating flow) is one already, exactly and without allocation. A gridded
// field reaches it through one of two adapters, and both ship:
//
//   * `ofSnapshots`: grids at stated times, bilinear in space and linear in time. Data arrives in
//     this form, and a stage costs two bilinear reads. Linear in time is second order in the
//     snapshots' spacing whatever the integrator, so RK4's fourth order is spent unless the
//     snapshots are dense, and a feature moving more than a cell between two of them is smeared.
//   * `ofFunction`: a grid built at whatever time a stage asks for. Exact in time; every new time
//     builds a whole grid. Affordable only because particles are stepped together, stage by
//     stage, so a step builds one grid per stage time for all of them, never one per particle.
//
// Outside its grid, or outside its snapshots' times, a field has no velocity: NaN, which a
// particle then carries and a scatter does not draw: what is not there is not invented.
//
// Streamlines integrate in pixels on the unit direction and stay as they are: a streamline is
// a picture of a field, a pathline is a trajectory in the field's own units.
// ======================================================================================

/// A velocity field on a plane that depends on time: at `(t, x, y)`, the velocity `(dx/dt,
/// dy/dt)`. `Flow2T` itself, named for what it holds here.
type Unsteady<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't> = Flow2T<'x, 'y, 't>

/// Velocity fields that change in time, from a function or from snapshots on a grid.
[<RequireQualifiedAccess>]
module Unsteady =

    /// A grid's arrays as plain numbers, read once. `float<'u>[]` is `float[]` at run time, so
    /// this copies nothing.
    let inline private plain (xs: float<'u>[]) : float[] = unbox<float[]> (box xs)

    /// The cell holding `x` on increasing `xs`, and the fraction across it; -1 outside.
    let private locate (xs: float[]) (x: float) : struct (int * float) =
        let n = xs.Length

        if Double.IsNaN x || x < xs.[0] || x > xs.[n - 1] then
            struct (-1, 0.0)
        else
            let mutable lo = 0
            let mutable hi = n - 1

            while hi - lo > 1 do
                let mid = (lo + hi) / 2
                if xs.[mid] <= x then lo <- mid else hi <- mid

            struct (lo, (x - xs.[lo]) / (xs.[lo + 1] - xs.[lo]))

    let private bilinear (nx: int) (values: float[]) (i: int) (fx: float) (j: int) (fy: float) =
        let v00 = values.[j * nx + i]
        let v10 = values.[j * nx + i + 1]
        let v01 = values.[(j + 1) * nx + i]
        let v11 = values.[(j + 1) * nx + i + 1]
        let a = v00 + (v10 - v00) * fx
        let b = v01 + (v11 - v01) * fx
        a + (b - a) * fy

    /// A grid's nodes and components, read once.
    type private Plain =
        { Xs: float[]
          Ys: float[]
          U: float[]
          W: float[] }

    let private plainOf (g: VectorGrid<'x, 'y, 'u, 'w>) =
        { Xs = plain g.Xs
          Ys = plain g.Ys
          U = plain g.U
          W = plain g.W }

    let private read (g: Plain) (x: float) (y: float) : struct (float * float) =
        let struct (i, fx) = locate g.Xs x
        let struct (j, fy) = locate g.Ys y

        if i < 0 || j < 0 then
            struct (nan, nan)
        else
            struct (bilinear g.Xs.Length g.U i fx j fy, bilinear g.Xs.Length g.W i fx j fy)

    /// A field that does not change in time.
    let ofSteady<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't> (f: Flow<'x, 'y, 't>) : Unsteady<'x, 'y, 't> = Ode.ofFlow f

    /// Grids at stated times, bilinear between nodes and linear between snapshots. Every
    /// snapshot must be on the same grid, and the times strictly increasing; outside them, or
    /// outside the grid, the velocity is NaN. Second order in the snapshots' spacing.
    let ofSnapshots<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (times: float<'t>[])
        (grids: VectorGrid<'x, 'y, 'x / 't, 'y / 't>[])
        : Unsteady<'x, 'y, 't> =
        if times.Length < 2 || times.Length <> grids.Length then
            invalidArg "grids" (sprintf "Marey: snapshots need at least two times and one grid for each; got %d times and %d grids." times.Length grids.Length)

        for k in 1 .. times.Length - 1 do
            if not (times.[k] > times.[k - 1]) then
                invalidArg "times" (sprintf "Marey: snapshot times must be strictly increasing; times.[%d] is not." k)

        let snaps = grids |> Array.map plainOf
        let first = snaps.[0]

        for k in 1 .. snaps.Length - 1 do
            if snaps.[k].Xs <> first.Xs || snaps.[k].Ys <> first.Ys then
                invalidArg "grids" (sprintf "Marey: snapshot %d is on a different grid from snapshot 0; interpolating between them in time would mix two fields." k)

        let ts = plain times

        fun t x y ->
            let struct (k, ft) = locate ts (float t)

            if k < 0 then
                struct (LanguagePrimitives.FloatWithMeasure nan, LanguagePrimitives.FloatWithMeasure nan)
            else
                let struct (u0, w0) = read snaps.[k] (float x) (float y)
                let struct (u1, w1) = read snaps.[k + 1] (float x) (float y)

                struct (LanguagePrimitives.FloatWithMeasure(u0 + (u1 - u0) * ft),
                        LanguagePrimitives.FloatWithMeasure(w0 + (w1 - w0) * ft))

    /// The last grid `ofFunction` built, and its time.
    type private Built = { T: float; Grid: Plain }

    /// A grid built for whatever time is asked, bilinear between its nodes, NaN outside
    /// it. The last grid is kept, so every particle a batch step evaluates at one stage time
    /// shares one build: step particles with `Particles.step`, never one at a time.
    ///
    /// The kept grid is one immutable value, replaced whole, so two threads seeking one trajectory
    /// at once can only make a grid be built twice, never read the wrong one.
    let ofFunction<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (f: float<'t> -> VectorGrid<'x, 'y, 'x / 't, 'y / 't>)
        : Unsteady<'x, 'y, 't> =
        let last = ref None

        fun t x y ->
            let grid =
                match last.Value with
                | Some b when b.T = float t -> b.Grid
                | _ ->
                    let g = plainOf (f t)
                    last.Value <- Some { T = float t; Grid = g }
                    g

            let struct (u, w) = read grid (float x) (float y)
            struct (LanguagePrimitives.FloatWithMeasure u, LanguagePrimitives.FloatWithMeasure w)

/// Many particles in a plane, carried by a field: x and y per particle.
type Particles<[<Measure>] 'x, [<Measure>] 'y> = (struct (float<'x>[] * float<'y>[]))

/// Particles carried by an unsteady field, stepped together by RK4: pathlines.
[<RequireQualifiedAccess>]
module Particles =

    /// RK4 for every particle at once, **stage by stage**: every particle's first stage, then
    /// every particle's second, so a field built per stage time (`Unsteady.ofFunction`) is built
    /// three times a step whatever the count, rather than three times a particle. A particle the
    /// field has no velocity for becomes NaN and stays so.
    let step<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't> (field: Unsteady<'x, 'y, 't>) : Stepper<Particles<'x, 'y>, 't> =
        fun t h (struct (x, y)) ->
            let n = x.Length

            if y.Length <> n then
                invalidArg "y" (sprintf "Marey: %d x positions and %d y positions." n y.Length)

            let half = 0.5 * h

            let stage (tt: float<'t>) (px: int -> float<'x>) (py: int -> float<'y>) =
                let u = Array.zeroCreate n
                let w = Array.zeroCreate n

                for i in 0 .. n - 1 do
                    let struct (a, b) = field tt (px i) (py i)
                    u.[i] <- a
                    w.[i] <- b

                struct (u, w)

            let struct (k1x, k1y) = stage t (fun i -> x.[i]) (fun i -> y.[i])
            let struct (k2x, k2y) = stage (t + half) (fun i -> x.[i] + half * k1x.[i]) (fun i -> y.[i] + half * k1y.[i])
            let struct (k3x, k3y) = stage (t + half) (fun i -> x.[i] + half * k2x.[i]) (fun i -> y.[i] + half * k2y.[i])
            let struct (k4x, k4y) = stage (t + h) (fun i -> x.[i] + h * k3x.[i]) (fun i -> y.[i] + h * k3y.[i])
            let sixth = h / 6.0

            struct (Array.init n (fun i -> x.[i] + sixth * ((k1x.[i] + 2.0 * k2x.[i]) + (2.0 * k3x.[i] + k4x.[i]))),
                    Array.init n (fun i -> y.[i] + sixth * ((k1y.[i] + 2.0 * k2y.[i]) + (2.0 * k3y.[i] + k4y.[i]))))

    /// A streakline's step: the particles carried as `step` carries them, then one more released
    /// at each seed. The particles a streak holds grow by the seeds' count every step; a
    /// trajectory over it checkpoints growing arrays, which `trajectory`'s size cap sees.
    let streakStep<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (seedsX: float<'x>[])
        (seedsY: float<'y>[])
        (field: Unsteady<'x, 'y, 't>)
        : Stepper<Particles<'x, 'y>, 't> =
        if seedsX.Length <> seedsY.Length then
            invalidArg "seedsY" "Marey: one y for every seed's x."

        let carry = step field

        fun t h p ->
            let struct (x, y) = carry t h p
            struct (Array.append x seedsX, Array.append y seedsY)

    /// Each particle's path: its positions at `times`, read from a trajectory of the particles,
    /// as one `(xs, ys)` per particle, ready for `Chart.line`. A particle that did not exist yet at
    /// an early time (a streak's later ones) is NaN there.
    let pathlines<[<Measure>] 'x, [<Measure>] 'y>
        (times: float<Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols.s>[])
        (trajectory: Anim<Particles<'x, 'y>>)
        : struct (float<'x>[] * float<'y>[])[] =
        let states = times |> Array.map trajectory.At
        let n = states |> Array.fold (fun m (struct (x, _)) -> max m x.Length) 0

        Array.init n (fun i ->
            struct (states |> Array.map (fun (struct (x, _)) -> if i < x.Length then x.[i] else LanguagePrimitives.FloatWithMeasure nan),
                    states |> Array.map (fun (struct (_, y)) -> if i < y.Length then y.[i] else LanguagePrimitives.FloatWithMeasure nan)))
