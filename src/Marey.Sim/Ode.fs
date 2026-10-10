namespace Marey

open System

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

// ---- D143: adaptive steps -----------------------------------------------------------------------

/// What an adaptive integrator carries from one step to the next: the step it proposes, and the
/// previous step's error, which the PI controller weighs against the current one. Both belong in a
/// checkpoint (D143): a replay that restarted the controller from anything else would choose
/// different steps than the straight run did.
[<Struct>]
type Controller<[<Measure>] 't> =
    { H: float<'t>
      ErrOld: float }

[<RequireQualifiedAccess>]
module Controller =

    /// The controller before its first step: a proposed `h` and no error history.
    let start<[<Measure>] 't> (h: float<'t>) : Controller<'t> = { H = h; ErrOld = 1e-4 }

/// One accepted step of an adaptive integrator, from `T0` to `T1`.
type Accepted<'s, [<Measure>] 't> =
    {
        T0: float<'t>
        T1: float<'t>
        /// The state at `T1`.
        State: 's
        /// The controller for the next step.
        Next: Controller<'t>
        /// The state at any time in `[T0, T1]`, from the step's own stages.
        Dense: float<'t> -> 's
        /// Attempts refused before this one was accepted.
        Rejected: int
    }

/// An adaptive integrator: from a state at `t` and the controller, the next accepted step.
type AdaptiveStep<'s, [<Measure>] 't> = float<'t> -> Controller<'t> -> 's -> Accepted<'s, 't>

/// The size of a step's local error against its tolerances, dimensionless, so that a step is
/// accepted at 1 or less: `norm h e s0 s1`, where `h · e` is the error estimate and `s0`, `s1`
/// the states at the step's ends. A single tolerance across kelvin and mol/m³ means nothing, so a
/// state of mixed units weighs each component by its own.
type ErrorNorm<'s, 'ds, [<Measure>] 't> = float<'t> -> 'ds -> 's -> 's -> float

[<RequireQualifiedAccess>]
module ErrorNorm =

    let private term (e: float) (scale: float) = let r = e / scale in r * r

    let private check (rtol: float) (atols: float list) =
        if not (rtol >= 0.0) || List.exists (fun a -> not (a >= 0.0)) atols then
            invalidArg "rtol" "Marey: tolerances must be non-negative."

        if rtol = 0.0 && List.exists (fun a -> a = 0.0) atols then
            invalidArg "rtol" "Marey: a component with no absolute tolerance needs a relative one."

    /// The root mean square of each component's error over `atol + rtol · max(|s0|, |s1|)`, for a
    /// 2-D state with an absolute tolerance per axis.
    let flow2<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (atolX: float<'x>)
        (atolY: float<'y>)
        (rtol: float)
        : ErrorNorm<struct (float<'x> * float<'y>), struct (float<'x / 't> * float<'y / 't>), 't> =
        check rtol [ float atolX; float atolY ]

        fun h (struct (ex, ey)) (struct (x0, y0)) (struct (x1, y1)) ->
            let sx = atolX + rtol * max (abs x0) (abs x1)
            let sy = atolY + rtol * max (abs y0) (abs y1)
            sqrt ((term (float (h * ex)) (float sx) + term (float (h * ey)) (float sy)) / 2.0)

    /// The same for a second-order state: one absolute tolerance for the coordinates, one for the
    /// velocities.
    let secondOrder<[<Measure>] 'q, [<Measure>] 't>
        (atolQ: float<'q>)
        (atolV: float<'q / 't>)
        (rtol: float)
        : ErrorNorm<struct (float<'q>[] * float<'q / 't>[]), struct (float<'q / 't>[] * float<'q / 't ^ 2>[]), 't> =
        check rtol [ float atolQ; float atolV ]

        fun h (struct (eq, ev)) (struct (q0, v0)) (struct (q1, v1)) ->
            let mutable sum = 0.0

            for i in 0 .. q0.Length - 1 do
                sum <- sum + term (float (h * eq.[i])) (float (atolQ + rtol * max (abs q0.[i]) (abs q1.[i])))
                sum <- sum + term (float (h * ev.[i])) (float (atolV + rtol * max (abs v0.[i]) (abs v1.[i])))

            if q0.Length = 0 then 0.0 else sqrt (sum / float (2 * q0.Length))

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

    // ---- Dormand–Prince 4(5) (D143) ---------------------------------------------------------------

    // The tableau, and the error weights `b − b̂`. The fifth-order solution is propagated (local
    // extrapolation), and the seventh stage is the derivative at the step's end.
    let private a21 = 1.0 / 5.0
    let private a31, a32 = 3.0 / 40.0, 9.0 / 40.0
    let private a41, a42, a43 = 44.0 / 45.0, -56.0 / 15.0, 32.0 / 9.0
    let private a51, a52, a53, a54 = 19372.0 / 6561.0, -25360.0 / 2187.0, 64448.0 / 6561.0, -212.0 / 729.0
    let private a61, a62, a63, a64, a65 = 9017.0 / 3168.0, -355.0 / 33.0, 46732.0 / 5247.0, 49.0 / 176.0, -5103.0 / 18656.0
    let private b1, b3, b4, b5, b6 = 35.0 / 384.0, 500.0 / 1113.0, 125.0 / 192.0, -2187.0 / 6784.0, 11.0 / 84.0
    let private e1, e3, e4, e5, e6, e7 = 71.0 / 57600.0, -71.0 / 16695.0, 71.0 / 1920.0, -17253.0 / 339200.0, 22.0 / 525.0, -1.0 / 40.0

    // Shampine's fourth-order continuous extension, as Hairer's DOPRI5 writes it.
    let private d1, d3, d4, d5, d6, d7 =
        -12715105075.0 / 11282082432.0,
        87487479700.0 / 32700410799.0,
        -10690763975.0 / 1880347072.0,
        701980252875.0 / 199316789632.0,
        -1453857185.0 / 822651844.0,
        69997945.0 / 29380423.0

    /// The weights of `k₁…k₇` in the dense output at `θ ∈ [0, 1]`: `y(θ) = y₀ + h Σ wᵢ kᵢ`. Hairer's
    /// nested form, `θ(r₂ + (1−θ)(r₃ + θ(r₄ + (1−θ)r₅)))`, with each `r` expanded into the stages.
    let private denseWeights (th: float) : float[] =
        let b = [| b1; 0.0; b3; b4; b5; b6; 0.0 |]
        let d = [| d1; 0.0; d3; d4; d5; d6; d7 |]
        let u = 1.0 - th

        Array.init 7 (fun i ->
            let r2 = b.[i]
            let r3 = (if i = 0 then 1.0 else 0.0) - b.[i]
            let r4 = 2.0 * b.[i] - (if i = 0 then 1.0 else 0.0) - (if i = 6 then 1.0 else 0.0)
            th * (r2 + u * (r3 + th * (r4 + u * d.[i]))))

    // The PI controller, with Hairer's constants: a safety factor of 0.9, a step that grows at most
    // tenfold and shrinks at most fivefold, β = 0.04 on the previous error.
    let private safety = 0.9
    let private beta = 0.04
    let private expo = 0.2 - 0.75 * beta
    let private maxRejections = 100

    /// Dormand–Prince 4(5) over any state given a tangent and an error norm: steps chosen by a PI
    /// controller to keep each step's error at 1 in `norm`, and the state between a step's ends
    /// from its fourth-order dense output. A step that cannot meet its tolerance at a step size
    /// rounding can still see is refused, naming the time.
    let dopri5With<'s, 'ds, [<Measure>] 't>
        (tangent: Tangent<'s, 'ds, 't>)
        (norm: ErrorNorm<'s, 'ds, 't>)
        (f: float<'t> -> 's -> 'ds)
        : AdaptiveStep<'s, 't> =
        // `a₁k₁ + a₂k₂ + …`, folded left from the first pair. Written as calls rather than over
        // arrays: the arrays were most of a step's allocation.
        let inline lin2 w1 k1 w2 k2 = tangent.Lin w1 k1 w2 k2
        let inline lin3 w1 k1 w2 k2 w3 k3 = tangent.Lin 1.0 (lin2 w1 k1 w2 k2) w3 k3
        let inline lin4 w1 k1 w2 k2 w3 k3 w4 k4 = tangent.Lin 1.0 (lin3 w1 k1 w2 k2 w3 k3) w4 k4
        let inline lin5 w1 k1 w2 k2 w3 k3 w4 k4 w5 k5 = tangent.Lin 1.0 (lin4 w1 k1 w2 k2 w3 k3 w4 k4) w5 k5
        let inline lin6 w1 k1 w2 k2 w3 k3 w4 k4 w5 k5 w6 k6 = tangent.Lin 1.0 (lin5 w1 k1 w2 k2 w3 k3 w4 k4 w5 k5) w6 k6

        let dense (ks: 'ds[]) (th: float) =
            let w = denseWeights th
            let mutable acc = tangent.Lin w.[0] ks.[0] w.[1] ks.[1]

            for i in 2..6 do
                acc <- tangent.Lin 1.0 acc w.[i] ks.[i]

            acc

        fun t0 control s0 ->
            if not (control.H > 0.0<_>) || Double.IsInfinity(float control.H) then
                invalidArg "control" (sprintf "Marey: an adaptive step must start from a positive, finite h, not %g." (float control.H))

            let k1 = f t0 s0
            let mutable h = control.H
            let mutable result = None
            let mutable rejected = 0

            while result.IsNone do
                if float h <= 1e-14 * max 1.0 (abs (float t0)) then
                    failwithf "Marey: Dormand–Prince cannot meet its tolerance at t = %g; the step fell to %g." (float t0) (float h)

                let k2 = f (t0 + 0.2 * h) (tangent.Axpy h (lin2 a21 k1 0.0 k1) s0)
                let k3 = f (t0 + 0.3 * h) (tangent.Axpy h (lin2 a31 k1 a32 k2) s0)
                let k4 = f (t0 + 0.8 * h) (tangent.Axpy h (lin3 a41 k1 a42 k2 a43 k3) s0)
                let k5 = f (t0 + 8.0 / 9.0 * h) (tangent.Axpy h (lin4 a51 k1 a52 k2 a53 k3 a54 k4) s0)
                let k6 = f (t0 + h) (tangent.Axpy h (lin5 a61 k1 a62 k2 a63 k3 a64 k4 a65 k5) s0)
                let s1 = tangent.Axpy h (lin5 b1 k1 b3 k3 b4 k4 b5 k5 b6 k6) s0
                let t1 = t0 + h
                let k7 = f t1 s1
                let e = lin6 e1 k1 e3 k3 e4 k4 e5 k5 e6 k6 e7 k7
                let err = norm h e s0 s1
                let err = if Double.IsNaN err then infinity else err
                let fac11 = err ** expo

                if err <= 1.0 then
                    // Grow by at most 10, shrink by at most 5, and not at all right after a refusal.
                    let fac = max 0.1 (min 5.0 (fac11 / control.ErrOld ** beta / safety))
                    let next = h / fac
                    let next = if rejected > 0 then min next h else next
                    let ks = [| k1; k2; k3; k4; k5; k6; k7 |]
                    let hh = h

                    result <-
                        Some
                            { T0 = t0
                              T1 = t1
                              State = s1
                              Next = { H = next; ErrOld = max err 1e-4 }
                              Dense =
                                fun t ->
                                    if t = t0 then s0
                                    else tangent.Axpy hh (dense ks (float ((t - t0) / hh))) s0
                              Rejected = rejected }
                else
                    rejected <- rejected + 1

                    if rejected > maxRejections then
                        failwithf "Marey: Dormand–Prince refused %d steps in a row at t = %g." rejected (float t0)

                    h <- h / min 5.0 (fac11 / safety)

            result.Value

    /// Dormand–Prince 4(5) over a 2-D flow, with an absolute tolerance per axis and one relative
    /// tolerance.
    let dopri5<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (atolX: float<'x>)
        (atolY: float<'y>)
        (rtol: float)
        (f: Flow2T<'x, 'y, 't>)
        : AdaptiveStep<struct (float<'x> * float<'y>), 't> =
        dopri5With Tangent.flow2 (ErrorNorm.flow2 atolX atolY rtol) (fun t (struct (x, y)) -> f t x y)
