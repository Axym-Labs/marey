namespace Marey

// ======================================================================================
// D134 — THE SHAPE OF A STATE, AND THE INTEGRATOR'S TYPE.
//
// §8.3 asks for integrators "unit-generic over state", and F# cannot write `'s / s` for a record
// `'s`. Three tiers cover every state without an erased one:
//
//   * **Second order, one unit for the coordinates.** `q̈ = a(t, q)`, with `q: float<'q>[]`,
//     `v: float<'q/'t>[]` and `a: float<'q/'t²>[]`. Every mechanical system is this shape — its
//     positions share a unit, or its angles do — so velocity Verlet, the default §8.3 asks for,
//     is typed by measures alone. An acceleration in `m/s` is a compile error.
//   * **First order in two dimensions, with time.** `Flow2T`, M4's `Flow` with a clock. Typed by
//     measures alone and allocation-free: the state is a struct pair.
//   * **Anything else, given a `Tangent`.** A mixed first-order state (a reactor's kelvin and
//     mol/m³) is a record, and the integrator reaches it through two functions: advance a state
//     along a derivative, and combine two derivatives. A `Tangent` whose advance forgets `h` is
//     a compile error, because a rate times nothing is not a state.
//
// What is *not* here is an integrator over `float[]` with the units as runtime labels. It was
// measured: it was the slowest of the four shapes (754 ns a step against 184 for the typed 2-D
// flow), and a stray `× 1000` in its derivative compiled and ran.
//
// **Every step is told the time.** A step is `t -> h -> 's -> 's`, not `h -> 's -> 's`, because
// time is `k · h` and only the caller that counts steps knows `k`. A state that carried its own
// clock would advance it by `t + h`, which drifts by an ulp a step, and a seek replayed from a
// checkpoint would then see a different force than the straight run did.
//
// **Every public function declares its measure parameters**, as `Marey.Fields` does: inferred,
// they narrow silently (§12.1).
// ======================================================================================

/// One step of an integrator: from the state at time `t`, the state at `t + h`.
type Step<'s, [<Measure>] 't> = float<'t> -> float<'t> -> 's -> 's

/// A 2-D first-order system with time: at `(t, x, y)`, the rates `dx/dt` and `dy/dt`. M4's
/// `Flow` with a clock, for a driven pendulum or an unsteady field. `Ode.ofFlow` lifts a `Flow`
/// that does not depend on time.
type Flow2T<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't> =
    float<'t> -> float<'x> -> float<'y> -> struct (float<'x / 't> * float<'y / 't>)

/// The arithmetic an integrator needs on a state it cannot see inside: `'s` is the state, `'ds`
/// its rate of change, and `'t` the unit of time between them.
///
/// For a reactor with a temperature in kelvin and a concentration in mol/m³, `'ds` holds a K/s
/// and a mol/(m³·s), and `Axpy` is where the units meet: `s.T + h * d.DT` is in kelvin, and
/// `s.T + d.DT` — the `h` forgotten — does not compile.
type Tangent<'s, 'ds, [<Measure>] 't> =
    {
        /// `s + h·ds`: the state advanced along a rate for a time `h`.
        Axpy: float<'t> -> 'ds -> 's -> 's
        /// `a·d₁ + b·d₂`: two rates combined, with dimensionless weights.
        Lin: float -> 'ds -> float -> 'ds -> 'ds
    }

/// The tangents of the two built-in shapes, so that `Ode.eulerWith` and `Ode.rk4With` reach them
/// too.
[<RequireQualifiedAccess>]
module Tangent =

    /// A 2-D state, `(x, y)`, whose rate is `(dx/dt, dy/dt)`: the tier `Flow2T` integrates.
    let flow2<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        : Tangent<struct (float<'x> * float<'y>), struct (float<'x / 't> * float<'y / 't>), 't> =
        { Axpy = fun h (struct (dx, dy)) (struct (x, y)) -> struct (x + h * dx, y + h * dy)
          Lin = fun a (struct (u1, w1)) b (struct (u2, w2)) -> struct (a * u1 + b * u2, a * w1 + b * w2) }

    /// A second-order state, positions and velocities, whose rate is (velocities,
    /// accelerations). With it, Euler and RK4 integrate what Verlet does, which is how the drift
    /// figure compares them on one system.
    let secondOrder<[<Measure>] 'q, [<Measure>] 't>
        : Tangent<struct (float<'q>[] * float<'q / 't>[]), struct (float<'q / 't>[] * float<'q / 't ^ 2>[]), 't> =
        { Axpy =
            fun h (struct (dq, dv)) (struct (q, v)) ->
                struct (Array.map2 (fun q dq -> q + h * dq) q dq, Array.map2 (fun v dv -> v + h * dv) v dv)
          Lin =
            fun a (struct (dq1, dv1)) b (struct (dq2, dv2)) ->
                struct (Array.map2 (fun x y -> a * x + b * y) dq1 dq2, Array.map2 (fun x y -> a * x + b * y) dv1 dv2) }

/// §8.3's integrators. Each returns a `Step`, so `Sim.ofStep h (Ode.rk4 f) s0` is a whole
/// simulation, and a step is usable on its own: `step t h s`.
[<RequireQualifiedAccess>]
module Ode =

    /// A flow that does not depend on time, as one that does.
    let ofFlow<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't> (f: Flow<'x, 'y, 't>) : Flow2T<'x, 'y, 't> =
        fun _ x y -> f x y

    // ---- Tier 3: any state, given a tangent -----------------------------------------------------

    /// Forward Euler, first order: `s + h·f(t, s)`. A teaching foil: it gains energy on every
    /// oscillator, which is what the drift figure shows.
    let eulerWith<'s, 'ds, [<Measure>] 't> (tangent: Tangent<'s, 'ds, 't>) (f: float<'t> -> 's -> 'ds) : Step<'s, 't> =
        fun t h s -> tangent.Axpy h (f t s) s

    /// The classical fourth-order Runge–Kutta step.
    let rk4With<'s, 'ds, [<Measure>] 't> (tangent: Tangent<'s, 'ds, 't>) (f: float<'t> -> 's -> 'ds) : Step<'s, 't> =
        fun t h s ->
            let half = 0.5 * h
            let k1 = f t s
            let k2 = f (t + half) (tangent.Axpy half k1 s)
            let k3 = f (t + half) (tangent.Axpy half k2 s)
            let k4 = f (t + h) (tangent.Axpy h k3 s)
            tangent.Axpy (h / 6.0) (tangent.Lin 1.0 (tangent.Lin 1.0 k1 2.0 k2) 1.0 (tangent.Lin 2.0 k3 1.0 k4)) s

    // ---- Tier 2: a 2-D flow with time ------------------------------------------------------------
    //
    // Written out rather than as `rk4With Tangent.flow2`: through the tangent's closures the same
    // step took 109 ns against 61 (measured, 10⁶ steps of a driven oscillator). The arithmetic is
    // the tangent's, term for term, so the two agree bit for bit, and a test holds them to it.

    /// Forward Euler over a 2-D flow. `eulerWith Tangent.flow2`, bit for bit.
    let euler<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't> (f: Flow2T<'x, 'y, 't>) : Step<struct (float<'x> * float<'y>), 't> =
        fun t h (struct (x, y)) ->
            let struct (dx, dy) = f t x y
            struct (x + h * dx, y + h * dy)

    /// RK4 over a 2-D flow, allocating nothing. `rk4With Tangent.flow2`, bit for bit.
    let rk4<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't> (f: Flow2T<'x, 'y, 't>) : Step<struct (float<'x> * float<'y>), 't> =
        fun t h (struct (x, y)) ->
            let half = 0.5 * h
            let struct (k1x, k1y) = f t x y
            let struct (k2x, k2y) = f (t + half) (x + half * k1x) (y + half * k1y)
            let struct (k3x, k3y) = f (t + half) (x + half * k2x) (y + half * k2y)
            let struct (k4x, k4y) = f (t + h) (x + h * k3x) (y + h * k3y)
            let sixth = h / 6.0
            struct (x + sixth * ((k1x + 2.0 * k2x) + (2.0 * k3x + k4x)), y + sixth * ((k1y + 2.0 * k2y) + (2.0 * k3y + k4y)))

    // ---- Tier 1: second order, one unit for the coordinates ---------------------------------------

    let private checkLength (n: int) (a: float<'a>[]) =
        if a.Length <> n then
            invalidArg "a" (sprintf "Marey: the acceleration has %d components for %d coordinates." a.Length n)

    /// Velocity Verlet, kick–drift–kick: half a kick at `t`, a drift over `h`, half a kick at
    /// `t + h`. Second order and symplectic, so an oscillator's energy error stays in a band
    /// instead of growing. Two evaluations of `a` a step, because the state does not carry the
    /// last one.
    let verlet<[<Measure>] 'q, [<Measure>] 't>
        (a: float<'t> -> float<'q>[] -> float<'q / 't ^ 2>[])
        : Step<struct (float<'q>[] * float<'q / 't>[]), 't> =
        fun t h (struct (q, v)) ->
            let n = q.Length
            let half = 0.5 * h
            let a0 = a t q
            checkLength n a0
            let v1 = Array.zeroCreate n
            let q1 = Array.zeroCreate n

            for i in 0 .. n - 1 do
                v1.[i] <- v.[i] + half * a0.[i]
                q1.[i] <- q.[i] + h * v1.[i]

            let a1 = a (t + h) q1
            checkLength n a1

            for i in 0 .. n - 1 do
                v1.[i] <- v1.[i] + half * a1.[i]

            struct (q1, v1)

    /// Leapfrog, drift–kick–drift: half a drift, a whole kick at `t + h/2`, half a drift. Second
    /// order and symplectic like `verlet`, with one evaluation of `a` a step.
    let leapfrog<[<Measure>] 'q, [<Measure>] 't>
        (a: float<'t> -> float<'q>[] -> float<'q / 't ^ 2>[])
        : Step<struct (float<'q>[] * float<'q / 't>[]), 't> =
        fun t h (struct (q, v)) ->
            let n = q.Length
            let half = 0.5 * h
            let q1 = Array.zeroCreate n

            for i in 0 .. n - 1 do
                q1.[i] <- q.[i] + half * v.[i]

            let am = a (t + half) q1
            checkLength n am
            let v1 = Array.zeroCreate n

            for i in 0 .. n - 1 do
                v1.[i] <- v.[i] + h * am.[i]
                q1.[i] <- q1.[i] + half * v1.[i]

            struct (q1, v1)

    /// Velocity Verlet over the caller's own arrays, allocating nothing: the step updates `q` and
    /// `v` in place and returns them. `kick t q dt v` adds `dt · a(t, q)` to `v`, which is where
    /// the units are checked (`dt * a` is a velocity).
    ///
    /// The same sequence as `verlet` when `kick` computes the same accelerations. A simulation
    /// over it needs `Sim.withCheckpoint` with a copy of both arrays, because the state it keeps
    /// is the one the next step overwrites. *Measured*: 26 ns a step and nothing allocated, against
    /// 40–110 ns and 128 B for `verlet`, on one oscillator; 5.2 µs against 6.1 µs on a chain of 1000 springs.
    /// No buffer is held between calls, so one step function is safe on several threads, each
    /// with its own state.
    let verletInPlace<[<Measure>] 'q, [<Measure>] 't>
        (kick: float<'t> -> float<'q>[] -> float<'t> -> float<'q / 't>[] -> unit)
        : Step<struct (float<'q>[] * float<'q / 't>[]), 't> =
        fun t h (struct (q, v)) ->
            let half = 0.5 * h
            kick t q half v

            for i in 0 .. q.Length - 1 do
                q.[i] <- q.[i] + h * v.[i]

            kick (t + h) q half v
            struct (q, v)
