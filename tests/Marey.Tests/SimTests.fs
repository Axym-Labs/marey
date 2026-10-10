/// M5.5 phase 2 — `Marey.Sim`: the integrators in three tiers, and `Sim`. Gates 2, 3 and 6 of
/// `m5.5-plan.md`.
///
/// **An integrator's order is measured, not asserted.** Gate 2 runs each one at a ladder of step
/// sizes and fits the slope of log error against log h, on an oscillator driven by a force that
/// depends on time, so a stage evaluated at the wrong time shows as a lost order. Gate 3 is the
/// property that makes Verlet the default: over ten thousand periods its energy error stays in
/// the band it starts in, and RK4's grows. Gate 6 compiles what must not compile.
module Marey.Tests.SimTests

open System
open System.IO
open System.Text
open Xunit
open Marey
open Marey.UnitsOfMeasure

// ======================================================================================
// Fixtures: a driven oscillator with a closed form
// ======================================================================================

/// `ẍ = −ω² x + F cos(Ω t)`, from `x = 1 m` at rest. ω = 2/s, Ω = 1.3/s, F = 0.5 m/s².
module private Driven =

    let omega = 2.0</s>
    let bigOmega = 1.3</s>
    let force = 0.5<m/s^2>

    let accel (t: float<s>) (x: float<m>) : float<m/s^2> =
        -omega * omega * x + force * cos (bigOmega * t)

    /// The particular amplitude, `F / (ω² − Ω²)`.
    let c = force / (omega * omega - bigOmega * bigOmega)

    let exact (t: float<s>) : struct (float<m> * float<m/s>) =
        let a = 1.0<m> - c
        struct (a * cos (omega * t) + c * cos (bigOmega * t), -a * omega * sin (omega * t) - c * bigOmega * sin (bigOmega * t))

    let flow: Flow2T<m, m/s, s> = fun t x v -> struct (v, accel t x)

    let second (t: float<s>) (q: float<m>[]) : float<m/s^2>[] = Array.map (accel t) q

    let derivative (t: float<s>) (struct (q: float<m>[], v: float<m/s>[])) = struct (v, second t q)

/// The four fixed-step integrators and the tiers they are reached through, each reduced to "run
/// `n` steps to `t` and give back `(x, v)`".
type private Scheme =
    { Name: string
      Order: float
      /// Step counts over the horizon: past the pre-asymptotic range, short of rounding.
      Ns: int list
      Run: int -> float<s> -> struct (float<m> * float<m/s>) }

let private flowScheme name order ns (step: Step<struct (float<m> * float<m/s>), s>) =
    { Name = name
      Order = order
      Ns = ns
      Run =
        fun n horizon ->
            let h = horizon / float n
            Sim.ofStep h step (struct (1.0<m>, 0.0<m/s>)) |> Sim.run n }

let private secondScheme name order ns (step: Step<struct (float<m>[] * float<m/s>[]), s>) =
    { Name = name
      Order = order
      Ns = ns
      Run =
        fun n horizon ->
            let h = horizon / float n
            let struct (q, v) = Sim.ofStep h step (struct ([| 1.0<m> |], [| 0.0<m/s> |])) |> Sim.run n
            struct (q.[0], v.[0]) }

let private schemes =
    let ladder lo = [ lo .. lo + 4 ] |> List.map (fun k -> 1 <<< k)

    [ flowScheme "Euler, tier 2" 1.0 (ladder 10) (Ode.euler Driven.flow)
      flowScheme "RK4, tier 2" 4.0 (ladder 4) (Ode.rk4 Driven.flow)
      secondScheme "Euler, tier 3" 1.0 (ladder 10) (Ode.eulerWith Tangent.secondOrder Driven.derivative)
      secondScheme "RK4, tier 3" 4.0 (ladder 4) (Ode.rk4With Tangent.secondOrder Driven.derivative)
      secondScheme "Verlet" 2.0 (ladder 6) (Ode.verlet Driven.second)
      secondScheme "leapfrog" 2.0 (ladder 6) (Ode.leapfrog Driven.second) ]

/// The least-squares slope of `ys` against `xs`.
let private slope (xs: float[]) (ys: float[]) =
    let mx = Array.average xs
    let my = Array.average ys
    let num = Array.fold2 (fun acc x y -> acc + (x - mx) * (y - my)) 0.0 xs ys
    let den = xs |> Array.sumBy (fun x -> (x - mx) * (x - mx))
    num / den

// ======================================================================================
// Gate 2 — convergence order
// ======================================================================================

module ``each integrator converges at its order`` =

    let private horizon = 2.0<s>

    /// The error at the horizon, position and velocity together, in metres.
    let private error (scheme: Scheme) (n: int) =
        let struct (x, v) = scheme.Run n horizon
        let struct (xe, ve) = Driven.exact horizon
        sqrt (float ((x - xe) * (x - xe)) + float ((v - ve) / Driven.omega * ((v - ve) / Driven.omega)))

    let private measured (scheme: Scheme) =
        let hs = scheme.Ns |> List.map (fun n -> log (float horizon / float n)) |> Array.ofList
        let es = scheme.Ns |> List.map (fun n -> log (error scheme n)) |> Array.ofList
        slope hs es

    [<Fact>]
    let ``Euler 1, Verlet and leapfrog 2, RK4 4, within 0.2`` () =
        let report =
            schemes |> List.map (fun s -> s.Name, s.Order, measured s)

        for (name, order, got) in report do
            Assert.True(
                abs (got - order) <= 0.2,
                sprintf "%s converges at order %.3f, expected %.0f.\n%s" name got order
                    (report |> List.map (fun (n, o, g) -> sprintf "  %-14s %.0f  %.3f" n o g) |> String.concat "\n")
            )

// ======================================================================================
// Gate 3 — symplectic drift
// ======================================================================================

module ``Verlet's energy stays in a band and RK4's does not`` =

    /// An undriven oscillator with a period of 1 s, twenty steps a period, ten thousand periods.
    let private omega = 2.0 * Math.PI * 1.0</s>
    let private perPeriod = 20
    let private periods = 10_000
    let private h = 1.0<s> / float perPeriod

    let private energy (x: float<m>) (v: float<m/s>) = 0.5 * v * v + 0.5 * omega * omega * x * x

    /// The relative energy error at the end of every period.
    let private errors (step: Step<struct (float<m>[] * float<m/s>[]), s>) =
        let e0 = energy 1.0<m> 0.0<m/s>
        let mutable s = struct ([| 1.0<m> |], [| 0.0<m/s> |])
        let out = Array.zeroCreate periods

        for p in 0 .. periods - 1 do
            for j in 0 .. perPeriod - 1 do
                let k = p * perPeriod + j
                s <- step (float k * h) h s

            let struct (q, v) = s
            out.[p] <- abs (float ((energy q.[0] v.[0] - e0) / e0))

        out

    let private accel (_: float<s>) (q: float<m>[]) = q |> Array.map (fun x -> -omega * omega * x)

    let private derivative (t: float<s>) (struct (q: float<m>[], v: float<m/s>[])) = struct (v, accel t q)

    /// The band the first hundred periods occupy holds the last hundred.
    [<Fact>]
    let ``Verlet's energy error is bounded over ten thousand periods`` () =
        let e = errors (Ode.verlet accel)
        let first = Array.max e.[0..99]
        let last = Array.max e.[periods - 100 ..]
        Assert.True(first > 0.0, "the energy was exact, so the test measures nothing")
        Assert.True(last <= 1.01 * first, sprintf "Verlet's energy error grew from %g to %g" first last)

    [<Fact>]
    let ``leapfrog's energy error is bounded over ten thousand periods`` () =
        let e = errors (Ode.leapfrog accel)
        let first = Array.max e.[0..99]
        let last = Array.max e.[periods - 100 ..]
        Assert.True(last <= 1.01 * first, sprintf "leapfrog's energy error grew from %g to %g" first last)

    /// RK4 is more accurate per period and loses energy steadily: after ten thousand periods its
    /// error is thousands of times what it was after one.
    [<Fact>]
    let ``RK4's energy error grows over ten thousand periods`` () =
        let e = errors (Ode.rk4With Tangent.secondOrder derivative)
        Assert.True(e.[periods - 1] > 1000.0 * e.[0], sprintf "RK4's energy error went from %g to %g" e.[0] e.[periods - 1])

// ======================================================================================
// The tiers agree where they overlap, bit for bit
// ======================================================================================

module ``the written-out tiers are the tangent's arithmetic`` =

    let private seeds = [ 1UL .. 20UL ]

    let private start (seed: uint64) =
        let xs, _ = Rng.units 2 (Rng.seeded seed)
        struct (xs.[0] * 4.0<m> - 2.0<m>, xs.[1] * 4.0<m/s> - 2.0<m/s>)

    let private agree (written: Step<_, s>) (generic: Step<_, s>) =
        for seed in seeds do
            let h = 0.01<s>
            let mutable a = start seed
            let mutable b = a

            for k in 0 .. 999 do
                a <- written (float k * h) h a
                b <- generic (float k * h) h b

            let struct (xa, va) = a
            let struct (xb, vb) = b
            Assert.True(BitConverter.DoubleToInt64Bits(float xa) = BitConverter.DoubleToInt64Bits(float xb), sprintf "x differs, seed %d" seed)
            Assert.True(BitConverter.DoubleToInt64Bits(float va) = BitConverter.DoubleToInt64Bits(float vb), sprintf "v differs, seed %d" seed)

    let private generic (t: float<s>) (struct (x: float<m>, v: float<m/s>)) = Driven.flow t x v

    [<Fact>]
    let ``Ode.rk4 is rk4With Tangent.flow2`` () =
        agree (Ode.rk4 Driven.flow) (Ode.rk4With Tangent.flow2 generic)

    [<Fact>]
    let ``Ode.euler is eulerWith Tangent.flow2`` () =
        agree (Ode.euler Driven.flow) (Ode.eulerWith Tangent.flow2 generic)

    [<Fact>]
    let ``Ode.ofFlow ignores the clock`` () =
        let f: Flow<m, m/s, s> = fun x v -> struct (v, -4.0</s^2> * x)
        let lifted = Ode.ofFlow f
        let struct (a, b) = lifted 123.0<s> 1.0<m> 2.0<m/s>
        Assert.Equal(2.0, float a)
        Assert.Equal(-4.0, float b)

    /// The in-place step and the allocating one compute the same sequence when the kick adds
    /// `dt · a` exactly as `verlet` does.
    [<Fact>]
    let ``verletInPlace is verlet, bit for bit`` () =
        let kick (t: float<s>) (q: float<m>[]) (dt: float<s>) (v: float<m/s>[]) =
            let a = Driven.second t q

            for i in 0 .. v.Length - 1 do
                v.[i] <- v.[i] + dt * a.[i]

        let h = 0.01<s>
        let q0 = [| 1.0<m>; -0.5<m>; 0.25<m> |]
        let v0 = [| 0.0<m/s>; 0.3<m/s>; -0.7<m/s> |]
        let copy (struct (q: float<m>[], v: float<m/s>[])) = struct (Array.copy q, Array.copy v)
        let allocating = Sim.ofStep h (Ode.verlet Driven.second) (struct (q0, v0)) |> Sim.states 500
        let inPlace = Sim.ofStep h (Ode.verletInPlace kick) (struct (q0, v0)) |> Sim.withCheckpoint copy |> Sim.states 500

        for k in 0..500 do
            let struct (qa, va) = allocating.[k]
            let struct (qb, vb) = inPlace.[k]
            Assert.Equal<float<m>[]>(qa, qb)
            Assert.Equal<float<m/s>[]>(va, vb)

        // And the caller's start was not overwritten, because `run` and `states` copy it first.
        Assert.Equal<float<m>[]>([| 1.0<m>; -0.5<m>; 0.25<m> |], q0)

    [<Fact>]
    let ``an acceleration of the wrong length is refused, naming both lengths`` () =
        let step = Ode.verlet (fun _ (q: float<m>[]) -> Array.zeroCreate<float<m/s^2>> (q.Length + 1))
        let e = Assert.Throws<ArgumentException>(fun () -> step 0.0<s> 0.1<s> (struct ([| 1.0<m> |], [| 0.0<m/s> |])) |> ignore)
        Assert.Contains("2 components for 1 coordinates", e.Message)

// ======================================================================================
// `Sim`
// ======================================================================================

module ``a simulation`` =

    [<Fact>]
    let ``time is k times H, not a sum`` () =
        let h = 0.1<s>
        let sim = Sim.ofStep h (fun _ _ s -> s) 0
        // 0.1 added 30 times is 3.0000000000000013; 30 · 0.1 is 3.0000000000000004.
        let summed = Seq.fold (fun acc _ -> acc + h) 0.0<s> [ 1..30 ]
        let times = Sim.times 30 sim
        Assert.Equal(30.0 * 0.1, float times.[30])
        Assert.NotEqual(float summed, float times.[30])

    /// A step that records the times it is given: `run` hands it `k · H`.
    [<Fact>]
    let ``run passes each step its time`` () =
        let h = 0.1<s>
        let seen = Collections.Generic.List<float<s>>()
        let sim = Sim.ofStep h (fun t _ s -> seen.Add t; s + 1) 0
        Assert.Equal(30, Sim.run 30 sim)
        Assert.Equal<float<s>[]>(Array.init 30 (fun k -> float k * h), seen.ToArray())

    [<Fact>]
    let ``states are the run's, one per step and the start`` () =
        let sim = Sim.ofStep 0.01<s> (Ode.rk4 Driven.flow) (struct (1.0<m>, 0.0<m/s>))
        let all = Sim.states 100 sim
        Assert.Equal(101, all.Length)
        Assert.Equal(sim.Initial, all.[0])

        for n in [ 0; 1; 37; 100 ] do
            Assert.Equal(Sim.run n sim, all.[n])

    [<Fact>]
    let ``a step size must be positive and finite`` () =
        for h in [ 0.0<s>; -0.1<s>; nan * 1.0<s>; infinity * 1.0<s> ] do
            Assert.Throws<ArgumentException>(fun () -> Sim.ofStep h (fun _ _ s -> s) 0 |> ignore) |> ignore

        Assert.Throws<ArgumentException>(fun () -> Sim.ofStep 0.1<s> (fun _ _ s -> s) 0 |> Sim.run -1 |> ignore) |> ignore

// ======================================================================================
// Gate 6 — a wrong unit does not compile, per tier
// ======================================================================================

module ``a wrong unit does not compile`` =

    let private preamble () =
        let binDir = Path.GetDirectoryName(Uri(typeof<Marey.Figure>.Assembly.Location).LocalPath)
        let sb = StringBuilder()

        for name in [ "Marey.Core"; "Marey.Units"; "Marey.Anim"; "Marey.Plot"; "Marey.Fields"; "Marey.Sim" ] do
            sb.AppendLine(sprintf "#r @\"%s\"" (Path.Combine(binDir, name + ".dll"))) |> ignore

        sb.AppendLine("open Marey").AppendLine("open Marey.UnitsOfMeasure").ToString()

    let private compile (name: string) (body: string) =
        let tool = Tools.dotnet.Force()

        if not tool.IsAvailable then
            None
        else
            let path = Path.Combine(Path.GetTempPath(), sprintf "marey-sim-%s.fsx" name)
            File.WriteAllText(path, preamble () + "\n" + body + "\n", UTF8Encoding false)

            match Toolchain.run tool (sprintf "fsi --nologo \"%s\"" path) (Path.GetTempPath()) 600_000 with
            | ToolUnavailable _ -> None
            | ToolTimedOut why -> failwith why
            | ToolRan run -> Some(run.ExitCode, path, run.Out + "\n" + run.Error)

    let private skipped () = Console.WriteLine("SKIPPED: dotnet not found on PATH")

    /// A reactor: a temperature and a concentration, a record no measure can divide by seconds.
    let private reactor =
        String.concat
            "\n"
            [ "[<Measure>] type mol"
              "type R = { T: float<K>; C: float<mol/m^3> }"
              "type DR = { DT: float<K/s>; DC: float<mol/(m^3 s)> }"
              "let f (_: float<s>) (r: R) = { DT = -0.1</s> * (r.T - 300.0<K>); DC = -0.2</s> * r.C }" ]

    let private reactorLin =
        "Lin = fun a d1 b d2 -> { DT = a * d1.DT + b * d2.DT; DC = a * d1.DC + b * d2.DC }"

    /// The positive control for every negative below: each tier, written correctly, compiles and
    /// runs. Without it a bad `#r` would make every negative green.
    [<Fact>]
    let ``each tier compiles with the right units`` () =
        let body =
            String.concat
                "\n"
                [ reactor
                  "let tangent: Tangent<R, DR, s> = { Axpy = (fun h d r -> { T = r.T + h * d.DT; C = r.C + h * d.DC }); " + reactorLin + " }"
                  "let s3 = Ode.rk4With tangent f 0.0<s> 0.1<s> { T = 350.0<K>; C = 1.0<mol/m^3> }"
                  "let s2 = Ode.rk4 (fun (_: float<s>) (x: float<m>) (v: float<m/s>) -> struct (v, -4.0</s^2> * x)) 0.0<s> 0.1<s> (struct (1.0<m>, 0.0<m/s>))"
                  "let s1 = Ode.verlet (fun (_: float<s>) (q: float<m>[]) -> q |> Array.map (fun x -> -4.0</s^2> * x)) 0.0<s> 0.1<s> (struct ([| 1.0<m> |], [| 0.0<m/s> |]))"
                  "printfn \"%A\" s3.T" ]

        match compile "control" body with
        | None -> skipped ()
        | Some(code, path, output) -> Assert.True((code = 0), sprintf "the control did not compile: %s\n%s" path output)

    let private refuses (name: string) (body: string) (expected: string) =
        match compile name body with
        | None -> skipped ()
        | Some(code, path, output) ->
            Assert.True(code <> 0, sprintf "%s compiled: %s" name path)
            Assert.Contains("error FS0001", output)
            Assert.Contains(expected, output)

    /// Tier 1: an acceleration in m/s, not m/s².
    [<Fact>]
    let ``Verlet refuses an acceleration in the wrong unit`` () =
        refuses
            "verlet"
            "let s = Ode.verlet (fun (_: float<s>) (q: float<m>[]) -> q |> Array.map (fun x -> -4.0</s> * x)) 0.0<s> 0.1<s> (struct ([| 1.0<m> |], [| 0.0<m/s> |]))"
            "'m/s ^ 2' does not match the unit of measure 'm/s'"

    /// Tier 2: the same mistake in a flow's second component.
    [<Fact>]
    let ``RK4 refuses a flow whose rate is in the wrong unit`` () =
        refuses
            "flow"
            "let s = Ode.rk4 (fun (_: float<s>) (x: float<m>) (v: float<m/s>) -> struct (v, -4.0</s> * x)) 0.0<s> 0.1<s> (struct (1.0<m>, 0.0<m/s>))"
            "'m/s ^ 2' does not match the unit of measure 'm/s'"

    /// Tier 3: a tangent whose advance forgets `h` adds kelvin per second to kelvin.
    [<Fact>]
    let ``a tangent that forgets h does not compile`` () =
        refuses
            "tangent"
            (reactor
             + "\nlet tangent: Tangent<R, DR, s> = { Axpy = (fun h d r -> { T = r.T + d.DT; C = r.C + h * d.DC }); "
             + reactorLin
             + " }")
            "'K/s' does not match the unit of measure 'K'"

// ======================================================================================
// Gates 4 and 5 — a seek is a run (phase 3)
// ======================================================================================

module ``a trajectory seeks to what a run reaches`` =

    open FsCheck
    open FsCheck.Xunit

    let private bits (x: float<'u>) = BitConverter.DoubleToInt64Bits(float x)

    let private sameState (struct (qa: float<m>[], va: float<m/s>[])) (struct (qb: float<m>[], vb: float<m/s>[])) =
        Array.forall2 (fun a b -> bits a = bits b) qa qb && Array.forall2 (fun a b -> bits a = bits b) va vb

    /// Three bodies on springs, each driven by a force that depends on time, so that a step
    /// replayed at a drifted time sees a different force.
    let private second (t: float<s>) (q: float<m>[]) : float<m/s^2>[] =
        q |> Array.mapi (fun i x -> -4.0</s^2> * x + 0.5<m/s^2> * cos (1.3</s> * t + float i))

    let private kick (t: float<s>) (q: float<m>[]) (dt: float<s>) (v: float<m/s>[]) =
        let a = second t q

        for i in 0 .. v.Length - 1 do
            v.[i] <- v.[i] + dt * a.[i]

    let private copy (struct (q: float<m>[], v: float<m/s>[])) = struct (Array.copy q, Array.copy v)

    let private start () = struct ([| 1.0<m>; -0.5<m>; 0.25<m> |], [| 0.0<m/s>; 0.3<m/s>; -0.7<m/s> |])

    let private h = 0.01<s>

    /// The four fixed-step integrators, and Verlet in place, over the same state.
    let private sims =
        [ "Euler", Sim.ofStep h (Ode.eulerWith Tangent.secondOrder (fun t (struct (q, v)) -> struct (v, second t q))) (start ())
          "RK4", Sim.ofStep h (Ode.rk4With Tangent.secondOrder (fun t (struct (q, v)) -> struct (v, second t q))) (start ())
          "Verlet", Sim.ofStep h (Ode.verlet second) (start ())
          "leapfrog", Sim.ofStep h (Ode.leapfrog second) (start ())
          "Verlet in place", Sim.ofStep h (Ode.verletInPlace kick) (start ()) |> Sim.withCheckpoint copy ]

    /// The straight run to `t`: whole steps from zero, then the same remainder step.
    let private straight (sim: Sim<_>) (t: float<s>) =
        match sim.Stepping with
        | Stepping.Adaptive _ -> failwith "a fixed-step simulation was expected"
        | Stepping.Fixed(h, step) ->
            let mutable k = int (floor (t / h))

            while float (k + 1) * h <= t do
                k <- k + 1

            while k > 0 && float k * h > t do
                k <- k - 1

            let s = Sim.run k sim
            let tk = float k * h
            if t > tk then step tk (t - tk) s else s

    /// Gate 4: for random times, in a random order, a seek equals the straight run, bit for bit,
    /// with checkpoints every 0.37 s (a stride of 37 steps) over 10 s.
    [<Property(MaxTest = 60)>]
    let ``seeking equals stepping from zero, bit for bit`` (times: NormalFloat list) =
        let ts =
            times |> List.map (fun x -> abs (float x.Get) % 10.0 * 1.0<s>) |> List.append [ 0.0<s>; 0.37<s>; 5.0<s>; 9.99<s>; 10.0<s> ]

        sims
        |> List.forall (fun (name, sim) ->
            let traj = Sim.trajectory 0.37<s> 10.0<s> sim

            ts
            |> List.forall (fun t ->
                let ok = sameState (traj.At t) (straight sim t)

                if not ok then
                    printfn "%s differs at t = %.17g" name (float t)

                ok))

    /// The same, at every whole step and the steps either side of every checkpoint.
    [<Fact>]
    let ``every whole step and both sides of every checkpoint`` () =
        for (name, sim) in sims do
            let traj = Sim.trajectory 0.37<s> 3.0<s> sim
            let all = Sim.states 300 sim

            for k in 0..300 do
                Assert.True(sameState all.[k] (traj.At(float k * h)), sprintf "%s differs at step %d" name k)

    /// Gate 5: frames rendered concurrently see the frames rendered one by one. With Verlet in
    /// place, a seek that stepped the table's own arrays would corrupt the next one.
    [<Fact>]
    let ``concurrent seeks equal serial seeks`` () =
        for (name, sim) in sims do
            let traj = Sim.trajectory 0.25<s> 10.0<s> sim
            let ts = Array.init 600 (fun i -> float ((i * 7919) % 600) / 60.0 * 1.0<s>)
            // Copied as they are read: a state stepped in place is a reference to arrays that a
            // later seek could still be writing, and comparing references compares nothing.
            let seek t = copy (traj.At t)
            let serial = ts |> Array.map seek
            let concurrent = ts |> Array.Parallel.map seek
            let again = ts |> Array.map seek

            for i in 0 .. ts.Length - 1 do
                Assert.True(sameState serial.[i] concurrent.[i], sprintf "%s: the concurrent seek differs at %g s" name (float ts.[i]))
                Assert.True(sameState serial.[i] again.[i], sprintf "%s: a second seek differs at %g s" name (float ts.[i]))

    [<Fact>]
    let ``time is clamped at every door, AtSeconds included`` () =
        let sim = Sim.ofStep h (Ode.verlet second) (start ())
        let traj = Sim.trajectory 1.0<s> 2.0<s> sim
        Assert.Equal(2.0, float traj.Duration)
        Assert.True(sameState (traj.At 0.0<s>) (traj.AtSeconds -5.0<s>))
        Assert.True(sameState (traj.At 0.0<s>) (traj.AtSeconds(nan * 1.0<s>)))
        Assert.True(sameState (traj.At 2.0<s>) (traj.AtSeconds 50.0<s>))
        Assert.True(sameState (traj.At 2.0<s>) (traj.AtFraction 1.5))
        Assert.True(sameState (Sim.run 200 sim) (traj.At 2.0<s>))

    /// A horizon between steps ends on a remainder step; `every` past the horizon keeps only the
    /// start; a zero horizon is the start.
    [<Fact>]
    let ``odd horizons and strides`` () =
        let sim = Sim.ofStep h (Ode.leapfrog second) (start ())
        Assert.True(sameState (straight sim 1.234<s>) ((Sim.trajectory 0.1<s> 1.234<s> sim).At 1.234<s>))
        Assert.True(sameState (straight sim 0.7<s>) ((Sim.trajectory 50.0<s> 1.0<s> sim).At 0.7<s>))
        Assert.True(sameState (start ()) ((Sim.trajectory 0.1<s> 0.0<s> sim).At 3.0<s>))
        // `every` below the step size is one step.
        Assert.True(sameState (straight sim 0.55<s>) ((Sim.trajectory 0.001<s> 1.0<s> sim).At 0.55<s>))

    [<Fact>]
    let ``a bad every or horizon is refused`` () =
        let sim = Sim.ofStep h (Ode.leapfrog second) (start ())

        for every in [ 0.0<s>; -1.0<s>; nan * 1.0<s>; infinity * 1.0<s> ] do
            Assert.Throws<ArgumentException>(fun () -> Sim.trajectory every 1.0<s> sim |> ignore) |> ignore

        for horizon in [ -1.0<s>; nan * 1.0<s>; infinity * 1.0<s>; 1e8<s> ] do
            Assert.Throws<ArgumentException>(fun () -> Sim.trajectory 1.0<s> horizon sim |> ignore) |> ignore

    /// A 512² grid of doubles is 2 MiB; checkpointed every step over 1000 steps it is 2 GiB. The
    /// refusal names the size and an `every` that fits, and that `every` is accepted.
    [<Fact>]
    let ``a table over the cap is refused, naming an every that fits`` () =
        let grid = Array.zeroCreate<float> (512 * 512)
        let sim = Sim.ofStep 0.001<s> (fun _ _ (g: float[]) -> g) grid
        let e = Assert.Throws<ArgumentException>(fun () -> Sim.trajectory 0.001<s> 1.0<s> sim |> ignore)
        Assert.Contains("1001 checkpoints of about 2097152 bytes each", e.Message)
        Assert.Contains("over the 512 MB allowed", e.Message)
        let m = Text.RegularExpressions.Regex.Match(e.Message, @"Checkpoint every ([0-9.eE+-]+) s")
        Assert.True(m.Success, e.Message)
        let every = float m.Groups.[1].Value * 1.0<s>
        Sim.trajectory every 1.0<s> sim |> ignore
        // And a lower cap through trajectoryWithin.
        Assert.Throws<ArgumentException>(fun () -> Sim.trajectoryWithin 1_000_000L 0.5<s> 1.0<s> sim |> ignore) |> ignore

// ======================================================================================
// Phase 4 — Dormand–Prince 4(5): gates 2 and 4 for the adaptive integrator
// ======================================================================================

module ``Dormand–Prince`` =

    open FsCheck.Xunit

    let private start = struct (1.0<m>, 0.0<m/s>)

    let private error (struct (x: float<m>, v: float<m/s>)) (struct (xe: float<m>, ve: float<m/s>)) =
        sqrt (float ((x - xe) * (x - xe)) + float ((v - ve) / Driven.omega * ((v - ve) / Driven.omega)))

    /// A tolerance so loose that every step is accepted at the `h` it was given: Dormand–Prince
    /// at a fixed step, reached through the public step by ignoring the controller's proposal.
    let private loose = Ode.dopri5 1e300<m> 1e300<m/s> 0.0 Driven.flow

    /// Gate 2: the propagated solution is fifth order.
    [<Fact>]
    let ``at a fixed step it converges at order 5`` () =
        let horizon = 2.0<s>
        let ns = [ 8; 16; 32; 64; 128 ]

        let run n =
            let h = horizon / float n
            let mutable s = start

            for k in 0 .. n - 1 do
                let a = loose (float k * h) (Controller.start h) s
                Assert.Equal(float k * h + h, a.T1)
                s <- a.State

            error s (Driven.exact horizon)

        let got = slope (ns |> List.map (fun n -> log (float horizon / float n)) |> Array.ofList) (ns |> List.map (run >> log) |> Array.ofList)
        Assert.True(abs (got - 5.0) <= 0.2, sprintf "order %.3f" got)

    /// The dense output's own error, one step from the exact state, at the step's middle: fourth
    /// order as a continuous extension, so a local error of order 5. Linear interpolation between
    /// the ends would be order 2.
    [<Fact>]
    let ``its dense output is a fourth-order continuous extension`` () =
        let t0 = 0.7<s>
        let hs = [ 0.2<s>; 0.1<s>; 0.05<s>; 0.025<s>; 0.0125<s> ]

        let local (h: float<s>) =
            let a = loose t0 (Controller.start h) (Driven.exact t0)
            [ 0.25; 0.5; 0.75 ] |> List.map (fun th -> error (a.Dense(t0 + th * h)) (Driven.exact (t0 + th * h))) |> List.max

        let got = slope (hs |> List.map (float >> log) |> Array.ofList) (hs |> List.map (local >> log) |> Array.ofList)
        Assert.True(got >= 4.8, sprintf "the dense output's local error has order %.3f" got)

        // And it meets the step's ends: the start exactly, the end to rounding.
        let a = loose t0 (Controller.start 0.1<s>) (Driven.exact t0)
        Assert.Equal(Driven.exact t0, a.Dense t0)
        let struct (x1, v1) = a.State
        let struct (xd, vd) = a.Dense a.T1
        Assert.Equal(float x1, float xd, 12)
        Assert.Equal(float v1, float vd, 12)

    /// Gate 2, adaptively: across a sweep of tolerances the error over 20 s follows the tolerance,
    /// and against the mean step it falls at order 5.
    [<Fact>]
    let ``across a tolerance sweep the error follows the tolerance at order 5`` () =
        let horizon = 20.0<s>

        let measured (tol: float) =
            let sim = Sim.ofAdaptive 0.01<s> (Ode.dopri5 (tol * 1.0<m>) (tol * 1.0<m/s>) tol Driven.flow) start
            let traj = Sim.trajectory 0.5<s> horizon sim
            let ts = Sim.times 100_000 sim
            let steps = ts |> Array.findIndex (fun t -> t > horizon)
            let e = [ 0..400 ] |> List.map (fun i -> let t = float i * 0.05<s> in error (traj.At t) (Driven.exact t)) |> List.max
            float horizon / float steps, e

        let tols = [ 1e-5; 1e-6; 1e-7; 1e-8; 1e-9 ]
        let runs = tols |> List.map measured

        for tol, (_, e) in List.zip tols runs do
            Assert.True(e < 20.0 * tol && e > 0.5 * tol, sprintf "at a tolerance of %g the error was %g" tol e)

        let got = slope (runs |> List.map (fst >> log) |> Array.ofList) (runs |> List.map (snd >> log) |> Array.ofList)
        Assert.True(abs (got - 5.0) <= 0.2, sprintf "order %.3f against the mean step" got)

    [<Fact>]
    let ``a step too large is refused and retried smaller`` () =
        let step = Ode.dopri5 1e-9<m> 1e-9<m/s> 1e-9 Driven.flow
        let a = step 0.0<s> (Controller.start 1.0<s>) start
        Assert.True(a.Rejected > 0)
        Assert.True(a.T1 < 1.0<s>)
        Assert.True(error a.State (Driven.exact a.T1) < 1e-8)

    [<Fact>]
    let ``a tolerance that cannot be met is refused, naming the time`` () =
        let blowUp: Flow2T<m, m/s, s> = fun t x v -> struct (v, 1.0</s^2> * x * x * x * 1.0</m^2> + 0.0<m/s^2> * float t)
        let sim = Sim.ofAdaptive 0.1<s> (Ode.dopri5 1e-6<m> 1e-6<m/s> 1e-6 blowUp) (struct (10.0<m>, 0.0<m/s>))
        let e = Assert.ThrowsAny<exn>(fun () -> Sim.run 10_000 sim |> ignore)
        Assert.Contains("Dormand–Prince", e.Message)
        Assert.Contains("at t = ", e.Message)

    [<Fact>]
    let ``tolerances must be non-negative, and not both zero`` () =
        Assert.Throws<ArgumentException>(fun () -> ErrorNorm.flow2 -1.0<m> 1.0<m/s> 0.1 |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> ErrorNorm.flow2 0.0<m> 1.0<m/s> 0.0 |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> ErrorNorm.flow2 1.0<m> 1.0<m/s> nan |> ignore) |> ignore
        ErrorNorm.flow2 0.0<m> 0.0<m/s> 1e-6 |> ignore

    // ---- gate 4, adaptively -------------------------------------------------------------------

    let private bits (x: float<'u>) = BitConverter.DoubleToInt64Bits(float x)

    /// The straight run to `t`, written out against the step rather than through `Sim`: accepted
    /// steps from zero until one ends past `t`, then its dense output.
    let private straight (h0: float<s>) (step: AdaptiveStep<'s, s>) (s0: 's) (t: float<s>) : 's =
        let mutable a = step 0.0<s> (Controller.start h0) s0

        while a.T1 <= t do
            a <- step a.T1 a.Next a.State

        a.Dense t

    let private tier2 = Ode.dopri5 1e-8<m> 1e-8<m/s> 1e-8 Driven.flow

    let private tier3 =
        Ode.dopri5With Tangent.secondOrder (ErrorNorm.secondOrder 1e-8<m> 1e-8<m/s> 1e-8) Driven.derivative

    let private sameFlow (struct (a: float<m>, b: float<m/s>)) (struct (c: float<m>, d: float<m/s>)) = bits a = bits c && bits b = bits d

    let private sameSecond (struct (qa: float<m>[], va: float<m/s>[])) (struct (qb: float<m>[], vb: float<m/s>[])) =
        Array.forall2 (fun a b -> bits a = bits b) qa qb && Array.forall2 (fun a b -> bits a = bits b) va vb

    [<Property(MaxTest = 40)>]
    let ``seeking equals the straight run, bit for bit`` (times: FsCheck.NormalFloat list) =
        let ts = times |> List.map (fun x -> abs (float x.Get) % 10.0 * 1.0<s>) |> List.append [ 0.0<s>; 0.5<s>; 5.0<s>; 10.0<s> ]
        let t2 = Sim.ofAdaptive 0.01<s> tier2 start |> Sim.trajectory 0.5<s> 10.0<s>
        let s3 = struct ([| 1.0<m>; 0.5<m> |], [| 0.0<m/s>; -0.2<m/s> |])
        let t3 = Sim.ofAdaptive 0.01<s> tier3 s3 |> Sim.trajectory 0.5<s> 10.0<s>

        ts
        |> List.forall (fun t ->
            sameFlow (t2.At t) (straight 0.01<s> tier2 start t)
            && sameSecond (t3.At t) (straight 0.01<s> tier3 s3 t))

    /// At every step boundary of the straight run, and just either side of it: a boundary is where
    /// a half-open interval and a closed one would disagree.
    [<Fact>]
    let ``at and around every step boundary`` () =
        let sim = Sim.ofAdaptive 0.01<s> tier2 start
        let traj = Sim.trajectory 0.5<s> 10.0<s> sim
        let ts = Sim.times 2000 sim |> Array.filter (fun t -> t <= 10.0<s>)
        Assert.True(ts.Length > 50, sprintf "only %d steps" ts.Length)

        for t in ts do
            for u in [ t; t * (1.0 - 1e-15); t * (1.0 + 1e-15) ] do
                Assert.True(sameFlow (traj.At u) (straight 0.01<s> tier2 start u), sprintf "differs at %.17g" (float u))

    [<Fact>]
    let ``concurrent seeks equal serial seeks`` () =
        let traj = Sim.ofAdaptive 0.01<s> tier2 start |> Sim.trajectory 0.25<s> 10.0<s>
        let ts = Array.init 600 (fun i -> float ((i * 7919) % 600) / 60.0 * 1.0<s>)
        let serial = ts |> Array.map traj.At
        let concurrent = ts |> Array.Parallel.map traj.At

        for i in 0 .. ts.Length - 1 do
            Assert.True(sameFlow serial.[i] concurrent.[i], sprintf "differs at %g s" (float ts.[i]))

    /// A step that crosses several checkpoint marks fills each; a seek before a mark's boundary
    /// steps from an earlier one.
    [<Fact>]
    let ``checkpoints closer than the steps`` () =
        let sim = Sim.ofAdaptive 0.01<s> (Ode.dopri5 1e-3<m> 1e-3<m/s> 1e-3 Driven.flow) start
        let traj = Sim.trajectory 0.01<s> 3.0<s> sim

        for i in 0..300 do
            let t = float i * 0.01<s>
            Assert.True(sameFlow (traj.At t) (straight 0.01<s> (Ode.dopri5 1e-3<m> 1e-3<m/s> 1e-3 Driven.flow) start t), sprintf "differs at %g" (float t))
