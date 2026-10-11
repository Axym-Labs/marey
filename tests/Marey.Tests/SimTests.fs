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

    /// An invariant whose witness says joules and whose function returns a momentum.
    [<Fact>]
    let ``an invariant in the wrong unit does not compile`` () =
        let sim = "let sim = Sim.ofStep 0.1<s> (Ode.rk4 (fun (_: float<s>) (x: float<m>) (v: float<m/s>) -> struct (v, -4.0</s^2> * x))) (struct (1.0<m>, 0.0<m/s>))"

        match compile "invariant-control" (sim + "\nlet e = sim |> Sim.withInvariant \"p\" (U.mul U.kilogram (U.div U.metre U.second)) (fun (struct (_, v)) -> 2.0<kg> * v)") with
        | None -> skipped ()
        | Some(code, path, output) -> Assert.True((code = 0), sprintf "the control did not compile: %s\n%s" path output)

        refuses "invariant" (sim + "\nlet e = sim |> Sim.withInvariant \"energy\" Derived.joule (fun (struct (_, v)) -> 2.0<kg> * v)") "'J' does not match the unit of measure 'kg m/s'"

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

// ======================================================================================
// Phase 5 — invariants and drift (D136)
// ======================================================================================

module ``invariants and drift`` =

    let private omega = 2.0 * Math.PI * 1.0</s>
    let private energy (x: float<m>) (v: float<m/s>) : float<J> = 0.5 * 1.0<kg> * (v * v + omega * omega * x * x)
    let private flow: Flow2T<m, m/s, s> = fun _ x v -> struct (v, -omega * omega * x)

    let private sim (step: Step<struct (float<m> * float<m/s>), s>) =
        Sim.ofStep 0.01<s> step (struct (0.1<m>, 0.0<m/s>))
        |> Sim.withInvariant "energy" Derived.joule (fun (struct (x, v)) -> energy x v)
        |> Sim.withInvariant "momentum" (U.mul U.kilogram (U.div U.metre U.second)) (fun (struct (_, v)) -> 1.0<kg> * v)

    [<Fact>]
    let ``a drift is relative, and absolute where the start is zero`` () =
        let s = sim (Ode.rk4 flow)
        let traj = Sim.trajectory 1.0<s> 10.0<s> s
        let drifts = Sim.drift 11 traj s
        Assert.Equal<string list>([ "energy"; "momentum" ], drifts |> List.map (fun d -> d.Name))
        let e = drifts.[0]
        let p = drifts.[1]
        Assert.True(e.Relative)
        Assert.False(p.Relative, "the momentum starts at rest, so its drift cannot be relative")
        Assert.Equal("J", e.Unit.Symbol)
        Assert.Equal<float<s>[]>(Array.init 11 (fun i -> 10.0<s> * float i / 10.0), e.Times)
        Assert.Equal(0.0, e.Values.[0])
        // The relative drift at the end is the energy's own, computed by hand.
        let struct (x, v) = traj.At 10.0<s>
        let e0 = energy 0.1<m> 0.0<m/s>
        Assert.Equal(float ((energy x v - e0) / e0), e.Values.[10], 15)
        Assert.Equal(float (1.0<kg> * v), p.Values.[10], 15)

    /// The integrators' signatures: Euler's energy grows, RK4's falls slowly, Verlet's stays in
    /// its band — the figure `integrators.fsx` draws.
    [<Fact>]
    let ``Euler gains, RK4 loses, Verlet holds`` () =
        let first (s: Sim<'s>) = Sim.drift 401 (Sim.trajectory 1.0<s> 20.0<s> s) s |> List.head
        let e = first (sim (Ode.euler flow))
        let r = first (sim (Ode.rk4 flow))

        let v =
            Sim.ofStep 0.01<s> (Ode.verlet (fun _ (q: float<m>[]) -> q |> Array.map (fun x -> -omega * omega * x))) (struct ([| 0.1<m> |], [| 0.0<m/s> |]))
            |> Sim.withInvariant "energy" Derived.joule (fun (struct (q, v)) -> energy q.[0] v.[0])
            |> first

        let last (d: Drift) = d.Values.[d.Values.Length - 1]
        let band (d: Drift) lo hi = d.Values.[lo..hi] |> Array.map abs |> Array.max
        Assert.True(last e > 1.0, sprintf "Euler's energy should have more than doubled: %g" (last e))
        Assert.True(last r < 0.0 && last r > -1e-5, sprintf "RK4's energy should fall slightly: %g" (last r))
        Assert.True(band v 380 400 <= 1.01 * band v 0 20, "Verlet's band grew")

    [<Fact>]
    let ``absolute and magnitude`` () =
        let s = sim (Ode.euler flow)
        let d = Sim.drift 5 (Sim.trajectory 1.0<s> 4.0<s> s) s |> List.head
        let a = Drift.absolute d
        Assert.False a.Relative
        Assert.Equal(float (energy 0.1<m> 0.0<m/s>), d.Initial, 15)

        for i in 0..4 do
            Assert.Equal(d.Values.[i] * d.Initial, a.Values.[i], 15)

        Assert.True(Drift.magnitude { a with Values = [| -2.0; 3.0 |] } |> fun m -> m.Values = [| 2.0; 3.0 |])
        Assert.Equal("RK4", (Drift.named "RK4" d).Name)

    [<Fact>]
    let ``a drift needs an invariant and two samples`` () =
        let bare = Sim.ofStep 0.01<s> (Ode.rk4 flow) (struct (0.1<m>, 0.0<m/s>))
        let traj = Sim.trajectory 1.0<s> 1.0<s> bare
        let e = Assert.Throws<ArgumentException>(fun () -> Sim.drift 10 traj bare |> ignore)
        Assert.Contains("Sim.withInvariant", e.Message)
        Assert.Throws<ArgumentException>(fun () -> Sim.drift 1 traj (sim (Ode.rk4 flow)) |> ignore) |> ignore

    /// The chart is one line per drift, labelled in the invariant's unit when absolute and
    /// dimensionless when relative; a mixture is refused.
    [<Fact>]
    let ``the drift chart`` () =
        let s = sim (Ode.rk4 flow)
        let drifts = Sim.drift 50 (Sim.trajectory 1.0<s> 5.0<s> s) s
        let energyAbs = drifts.[0] |> Drift.absolute
        let extent = Chart.size 640.0 400.0
        let svg (fig: Figure) = SvgBackend.render extent (Chart.toScene extent fig)
        let relative = Drift.chart [ drifts.[0]; Drift.named "again" drifts.[0] ] |> svg
        Assert.Contains("relative drift", relative)
        Assert.Contains("again", relative)
        let absolute = Drift.chart [ energyAbs ] |> svg
        Assert.Contains("J]", absolute)
        Assert.Throws<ArgumentException>(fun () -> Drift.chart [ drifts.[0]; drifts.[1] ] |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> Drift.chart [] |> ignore) |> ignore

    /// Under `open Marey` alone an invariant takes no witness, and its unit is unspecified.
    [<Fact>]
    let ``the unit-optional mode's invariant`` () =
        let s =
            Sim.ofStep 0.01<s> (Ode.rk4 flow) (struct (0.1<m>, 0.0<m/s>))
            |> Marey.Sim.withInvariant "x" (fun (struct (x, _)) -> float x)

        Assert.False(s.Invariants.Head.Unit.Stated)

// ======================================================================================
// Phase 6 — events (D144): gate 7's bouncing ball, and gate 4 with events
// ======================================================================================

/// A ball over a floor at `y = 0`. `Resting` is the modelling decision D144 leaves to the user: at
/// rest, the floor holds it and nothing moves.
type private Ball =
    { Y: float<m>
      V: float<m/s>
      Resting: bool }

type private BallRate = { DY: float<m/s>; DV: float<m/s^2> }

module ``events`` =

    open FsCheck.Xunit

    let private g = 9.81<m/s^2>
    let private e = 0.8
    let private h0 = 1.0<m>

    let private tangent: Tangent<Ball, BallRate, s> =
        { Axpy = fun h d b -> { b with Y = b.Y + h * d.DY; V = b.V + h * d.DV }
          Lin = fun a d1 b d2 -> { DY = a * d1.DY + b * d2.DY; DV = a * d1.DV + b * d2.DV } }

    let private fall (_: float<s>) (b: Ball) =
        if b.Resting then { DY = 0.0<m/s>; DV = 0.0<m/s^2> } else { DY = b.V; DV = -g }

    /// Longer than the fixed step: a flight shorter than one step lands with both of the step's
    /// ends below the floor, and is not seen.
    let private separation = 1e-2<s>

    let private floor =
        Event.create "floor" Direction.Falling (Response.Continue(fun b -> { b with V = -e * b.V })) (fun (b: Ball) -> b.Y)
        |> Event.withRest separation (fun _ -> { Y = 0.0<m>; V = 0.0<m/s>; Resting = true })

    let private drop = { Y = h0; V = 0.0<m/s>; Resting = false }

    let private fixedBall = Sim.ofStep 1e-3<s> (Ode.rk4With tangent fall) drop |> Sim.withEvents [ floor ]

    let private norm: ErrorNorm<Ball, BallRate, s> =
        fun h d b0 b1 ->
            let sy = 1e-9<m> + 1e-9 * max (abs b0.Y) (abs b1.Y)
            let sv = 1e-9<m/s> + 1e-9 * max (abs b0.V) (abs b1.V)
            sqrt (((float (h * d.DY / sy)) ** 2.0 + (float (h * d.DV / sv)) ** 2.0) / 2.0)

    let private adaptiveBall =
        Sim.ofAdaptive 1e-3<s> (Ode.dopri5With tangent norm fall |> Ode.withMaxStep 1e-3<s>) drop |> Sim.withEvents [ floor ]

    /// The first impact, the impact speed, and the Zeno time `t₁ + 2 e v₁ / (g (1 − e))`.
    let private t1 = sqrt (2.0 * h0 / g)
    let private v1 = g * t1
    let private zeno = t1 + 2.0 * e * v1 / (g * (1.0 - e))

    /// The n-th impact (from 1): `t₁ + Σ_{k<n} 2 eᵏ v₁ / g`.
    let private impact (n: int) = t1 + (List.sum [ for k in 1 .. n - 1 -> 2.0 * (e ** float k) * v1 / g ])

    let private check (sim: Sim<Ball>) =
        let hits = Sim.events 10.0<s> sim
        let fired = hits |> List.filter (fun h -> h.Kind = HitKind.Fired)
        Assert.True(fired.Length > 20, sprintf "only %d bounces" fired.Length)

        // The bounce times, against the closed form.
        fired |> List.iteri (fun i hit -> Assert.True(abs (hit.Time - impact (i + 1)) < 1e-9<s>, sprintf "bounce %d at %.12f s, expected %.12f" (i + 1) (float hit.Time) (float (impact (i + 1)))))

        // Each apex, read from the state just after its bounce, is e² of the one before.
        let apex (t: float<s>) = let b = Sim.at t sim in b.Y + b.V * b.V / (2.0 * g)
        let apexes = h0 :: (fired |> List.map (fun hit -> apex hit.Time))

        for a, b in List.pairwise apexes do
            Assert.True(abs (float (b / a) - e * e) < 1e-6, sprintf "apex ratio %.9f, expected %.9f" (float (b / a)) (e * e))

        // The run ends at rest near the Zeno time, once, and does not loop: within the time the
        // remaining bounces would have taken, which is under `separation · e / (1 − e)`.
        let rested = hits |> List.filter (fun h -> h.Kind = HitKind.Rested)
        Assert.Equal(1, rested.Length)
        Assert.True(abs (rested.Head.Time - zeno) <= separation * e / (1.0 - e) + 1e-9<s>, sprintf "rested at %.9f s, Zeno time %.9f s" (float rested.Head.Time) (float zeno))
        Assert.Equal(rested.Head, List.last hits)
        let still = Sim.at 9.0<s> sim
        Assert.True(still.Resting && still.Y = 0.0<m> && still.V = 0.0<m/s>)

    [<Fact>]
    let ``gate 7: the bouncing ball, at a fixed step`` () = check fixedBall

    [<Fact>]
    let ``gate 7: the bouncing ball, adaptively`` () = check adaptiveBall

    /// Without a rest response the guard stops the run, naming the event and the separation.
    [<Fact>]
    let ``without a rest the Zeno guard stops the run, saying why`` () =
        let floor =
            Event.create "floor" Direction.Falling (Response.Continue(fun b -> { b with V = -e * b.V })) (fun (b: Ball) -> b.Y)
            |> Event.withMinSeparation separation

        let sim = Sim.ofStep 1e-3<s> (Ode.rk4With tangent fall) drop |> Sim.withEvents [ floor ]
        let hits = Sim.events 10.0<s> sim

        match (List.last hits).Kind with
        | HitKind.Stopped why ->
            Assert.Contains("floor fired", why)
            Assert.Contains("minimum separation of 0.01 s", why)
        | other -> Assert.Fail(sprintf "the run ended with %A" other)

    [<Fact>]
    let ``a stop holds the state from the event on`` () =
        let ground = Event.create "ground" Direction.Falling Response.Stop (fun (b: Ball) -> b.Y)
        let sim = Sim.ofStep 1e-3<s> (Ode.rk4With tangent fall) drop |> Sim.withEvents [ ground ]
        let hits = Sim.events 5.0<s> sim
        Assert.Equal(1, hits.Length)
        Assert.True(abs (hits.Head.Time - t1) < 1e-9<s>)
        let traj = Sim.trajectory 0.1<s> 5.0<s> sim
        Assert.Equal(traj.At 2.0<s>, traj.At 5.0<s>)
        Assert.True((traj.At 5.0<s>).Y <= 0.0<m> && (traj.At 5.0<s>).Y > -1e-9<m>)

    /// Gate 4 with events: seeking into the middle of the bounce sequence equals the straight run,
    /// bit for bit, at a fixed step and adaptively, in any order.
    [<Property(MaxTest = 40)>]
    let ``gate 4: seeking through bounces equals the straight run`` (times: FsCheck.NormalFloat list) =
        let ts = times |> List.map (fun x -> abs (float x.Get) % 6.0 * 1.0<s>) |> List.append [ 0.0<s>; t1; impact 3; 4.0<s>; zeno; 6.0<s> ]
        let bits (x: float<'u>) = BitConverter.DoubleToInt64Bits(float x)
        let same (a: Ball) (b: Ball) = bits a.Y = bits b.Y && bits a.V = bits b.V && a.Resting = b.Resting

        [ fixedBall; adaptiveBall ]
        |> List.forall (fun sim ->
            let traj = Sim.trajectory 0.3<s> 6.0<s> sim
            ts |> List.forall (fun t -> same (traj.At t) (Sim.at t sim)))

    /// At and around every bounce, where a checkpoint inside the event's step would show.
    [<Fact>]
    let ``gate 4: at and around every bounce, concurrently`` () =
        for sim in [ fixedBall; adaptiveBall ] do
            let traj = Sim.trajectory 0.05<s> 6.0<s> sim
            let ts = Sim.events 6.0<s> sim |> List.collect (fun hit -> [ hit.Time * (1.0 - 1e-12); hit.Time; hit.Time * (1.0 + 1e-12) ]) |> Array.ofList
            let serial = ts |> Array.map traj.At
            let concurrent = ts |> Array.Parallel.map traj.At

            for i in 0 .. ts.Length - 1 do
                Assert.Equal(Sim.at ts.[i] sim, serial.[i])
                Assert.Equal(serial.[i], concurrent.[i])

    /// Checkpoints a millisecond apart put some between the last bounces, closer together than
    /// the separation: a seek from one of those must remember when the floor last fired, or it
    /// bounces where the straight run rests.
    [<Fact>]
    let ``gate 4: seeking from a checkpoint between the last bounces`` () =
        for sim in [ fixedBall; adaptiveBall ] do
            let traj = Sim.trajectory 1e-3<s> 4.2<s> sim
            let rest = (Sim.events 4.2<s> sim |> List.last).Time

            for i in 0..400 do
                let t = rest - 0.2<s> + float i * 1e-3<s>
                Assert.Equal(Sim.at t sim, traj.At t)

    /// Without events, `Sim.at` and the fixed-step trajectory agree too: the engine's clock is
    /// `k · h` until the first event.
    [<Fact>]
    let ``the engine's clock is the fixed path's`` () =
        let sim = Sim.ofStep 0.01<s> (Ode.rk4 Driven.flow) (struct (1.0<m>, 0.0<m/s>))
        let traj = Sim.trajectory 0.37<s> 5.0<s> sim

        for i in 0..500 do
            let t = float i * 0.0097<s>
            Assert.Equal(traj.At t, Sim.at t sim)

    [<Fact>]
    let ``directions`` () =
        // A sine crossed upward at 0 (not counted: it starts there), 2π; downward at π, 3π.
        let osc = Sim.ofStep 0.01<s> (Ode.rk4 (fun _ (x: float<m>) (v: float<m/s>) -> struct (v, -1.0</s^2> * x))) (struct (0.0<m>, 1.0<m/s>))
        let ev d = Event.create "x" d (Response.Continue id) (fun (struct (x: float<m>, _)) -> x)
        let times d = Sim.events 10.0<s> (osc |> Sim.withEvents [ ev d ]) |> List.map (fun h -> float h.Time)
        let close (expected: float list) (got: float list) =
            Assert.Equal(expected.Length, got.Length)
            List.iter2 (fun a b -> Assert.True(abs (a - b) < 1e-8, sprintf "%g against %g" b a)) expected got

        close [ 2.0 * Math.PI ] (times Direction.Rising)
        close [ Math.PI; 3.0 * Math.PI ] (times Direction.Falling)
        close [ Math.PI; 2.0 * Math.PI; 3.0 * Math.PI ] (times Direction.Either)

    /// Two events in one step: the earlier fires first, and the later is found again after it.
    [<Fact>]
    let ``two roots in one step are taken in order`` () =
        let line = Sim.ofStep 1.0<s> (Ode.euler (fun _ (_: float<m>) (_: float<m/s>) -> struct (1.0<m/s>, 0.0<m/s^2>))) (struct (0.0<m>, 0.0<m/s>))
        let at (name: string) (x0: float<m>) = Event.create name Direction.Rising (Response.Continue id) (fun (struct (x: float<m>, _)) -> x - x0)
        let hits = Sim.events 3.0<s> (line |> Sim.withEvents [ at "late" 0.7<m>; at "early" 0.3<m> ])
        Assert.Equal<string list>([ "early"; "late" ], hits |> List.map (fun h -> h.Name))
        Assert.True(abs (hits.[0].Time - 0.3<s>) < 1e-9<s> && abs (hits.[1].Time - 0.7<s>) < 1e-9<s>)

    /// An event whose response does not move the state off its root fires once per restart until
    /// the jam guard stops it.
    [<Fact>]
    let ``a jam stops instead of looping`` () =
        let line = Sim.ofStep 0.1<s> (Ode.euler (fun _ (_: float<m>) (_: float<m/s>) -> struct (1.0<m/s>, 0.0<m/s^2>))) (struct (0.0<m>, 0.0<m/s>))
        // Every crossing of 0.5 m sends the point back to 0.4999999 m.
        let sticky = Event.create "sticky" Direction.Rising (Response.Continue(fun (struct (_, v)) -> struct (0.5<m> - 1e-7<m>, v))) (fun (struct (x: float<m>, _)) -> x - 0.5<m>)
        let hits = Sim.events 10.0<s> (line |> Sim.withEvents [ sticky ])

        match (List.last hits).Kind with
        | HitKind.Stopped why -> Assert.Contains("events in a row", why)
        | other -> Assert.Fail(sprintf "ended with %A" other)

    [<Fact>]
    let ``Illinois locates a root to its tolerance from either side`` () =
        for root in [ 0.1; 0.5; 0.999 ] do
            let phi x = Math.Exp(x) - Math.Exp(root)
            let b = Event.locate phi 0.0 1.0 (phi 0.0) (phi 1.0) 1e-12
            Assert.True(b >= root && b - root < 1e-11, sprintf "%.15f for %g" b root)
            let falling x = -(phi x)
            let b = Event.locate falling 0.0 1.0 (falling 0.0) (falling 1.0) 1e-12
            Assert.True(b >= root && b - root < 1e-11, sprintf "%.15f for %g, falling" b root)

    [<Fact>]
    let ``Response.reflect`` () =
        match Response.reflect<m, s> 1 0.5 with
        | Response.Continue f ->
            let q = [| 1.0<m>; 0.0<m> |]
            let v = [| 2.0<m/s>; -4.0<m/s> |]
            let struct (q', v') = f (struct (q, v))
            Assert.Equal<float<m/s>[]>([| 2.0<m/s>; 2.0<m/s> |], v')
            Assert.Equal<float<m/s>[]>([| 2.0<m/s>; -4.0<m/s> |], v)
            Assert.Equal<float<m>[]>(q, q')
        | Response.Stop -> Assert.Fail "reflect stopped"

        Assert.Throws<ArgumentException>(fun () -> Response.reflect<m, s> 0 1.5 |> ignore) |> ignore

module ``hard discs`` =

    /// 400 discs of radius 1 cm on a 20 × 20 lattice in a 1 m box, jittered, with masses from 1
    /// to 3 and velocities up to about 1 m/s, all from a seed.
    let private gas (seed: uint64) =
        let n = 400
        let u, _ = Rng.units (5 * n) (Rng.seeded seed)
        let x = Array.init n (fun i -> (float (i % 20) + 0.5 + 0.3 * (u.[i] - 0.5)) * 0.05<m>)
        let y = Array.init n (fun i -> (float (i / 20) + 0.5 + 0.3 * (u.[n + i] - 0.5)) * 0.05<m>)
        let vx = Array.init n (fun i -> (u.[2 * n + i] - 0.5) * 2.0<m/s>)
        let vy = Array.init n (fun i -> (u.[3 * n + i] - 0.5) * 2.0<m/s>)
        let masses = Array.init n (fun i -> 1.0 + 2.0 * u.[4 * n + i])
        Discs.create x y vx vy, Array.create n 0.01<m>, masses

    /// The smallest gap between two discs, as a fraction of their radii's sum, to the nearest image.
    let private worstOverlap (box: Box<m>) (radii: float<m>[]) (d: Discs<m>) =
        let n = d.X.Length
        let mutable worst = infinity

        for i in 0 .. n - 2 do
            for j in i + 1 .. n - 1 do
                let mutable dx = float (d.X.[j] - d.X.[i])
                let mutable dy = float (d.Y.[j] - d.Y.[i])

                match box with
                | Box.Periodic(w, hgt) ->
                    dx <- dx - float w * Math.Round(dx / float w)
                    dy <- dy - float hgt * Math.Round(dy / float hgt)
                | _ -> ()

                worst <- min worst (sqrt (dx * dx + dy * dy) / float (radii.[i] + radii.[j]))

        worst

    /// Gate 7, the gas: ten thousand elastic collisions on a torus conserve momentum and kinetic
    /// energy to rounding, and no two discs ever overlap at a step's end, which a pair missed by
    /// the grid would.
    [<Fact>]
    let ``an elastic gas conserves momentum and energy over 10^4 collisions`` () =
        let d0, radii, masses = gas 7UL
        let box = Box.Periodic(1.0<m>, 1.0<m>)
        let col = Collide.create radii masses 1.0 box
        Assert.True(400 > col.BroadPhaseAbove, "the gate should exercise the grid")
        let step = Collide.step col
        let e0 = Discs.kinetic masses d0
        let struct (px0, py0) = Discs.momentum masses d0
        let scale = Array.sum (Array.map2 (fun mi (vx: float<m/s>) -> mi * abs vx) masses d0.VX)
        let mutable d = d0
        let mutable k = 0

        while d.Collisions < 10_000L do
            d <- step (float k * 0.01<s>) 0.01<s> d
            k <- k + 1

            // Every step: a missed pair overlaps for a step or two before the next contact
            // between them pushes them apart.
            Assert.True(worstOverlap box radii d >= 1.0 - 1e-9, sprintf "two discs overlap after step %d" k)

        let struct (px, py) = Discs.momentum masses d
        Assert.True(abs (px - px0) < 1e-12 * scale && abs (py - py0) < 1e-12 * scale, sprintf "momentum moved by %g, %g" (float (px - px0)) (float (py - py0)))
        let drift = abs (float ((Discs.kinetic masses d - e0) / e0))
        Assert.True(drift < 1e-12, sprintf "kinetic energy drifted by %g relative" drift)
        Assert.Equal(0L, d.Capped)
        Assert.True(worstOverlap box radii d >= 1.0 - 1e-9)

    /// The grid is a speed-up, not a different answer: with every pair tested instead, the same
    /// steps give the same bits.
    [<Fact>]
    let ``the grid and all pairs agree, bit for bit`` () =
        let d0, radii, masses = gas 11UL
        let box = Box.Periodic(1.0<m>, 1.0<m>)
        let grid = Collide.step (Collide.create radii masses 1.0 box)
        let all = Collide.step (Collide.create radii masses 1.0 box |> Collide.withBroadPhaseAbove 1000)
        let mutable a = d0
        let mutable b = d0

        for k in 0..39 do
            a <- grid (float k * 0.01<s>) 0.01<s> a
            b <- all (float k * 0.01<s>) 0.01<s> b

        Assert.True(a.Collisions > 100L, sprintf "only %d collisions" a.Collisions)
        Assert.Equal(a, b)

    [<Fact>]
    let ``walls hold the gas, and an inelastic one cools while keeping its momentum off the walls`` () =
        let d0, radii, masses = gas 3UL
        let box = Box.Walls(0.0<m>, 1.0<m>, 0.0<m>, 1.0<m>)
        let mutable d = d0
        let step = Collide.step (Collide.create radii masses 1.0 box)

        for k in 0..299 do
            d <- step (float k * 0.01<s>) 0.01<s> d

        Assert.True(d.WallHits > 100L && d.Collisions > 1000L)
        Assert.True(abs (float ((Discs.kinetic masses d - Discs.kinetic masses d0) / Discs.kinetic masses d0)) < 1e-12)

        for i in 0 .. d.X.Length - 1 do
            Assert.True(d.X.[i] >= radii.[i] * (1.0 - 1e-9) && d.X.[i] <= 1.0<m> - radii.[i] * (1.0 - 1e-9), sprintf "disc %d left the box" i)

        // Inelastic, on a torus so that only the discs exchange momentum.
        let torus = Box.Periodic(1.0<m>, 1.0<m>)
        let sticky = Collide.step (Collide.create radii masses 0.5 torus)
        let mutable c = d0

        for k in 0..99 do
            c <- sticky (float k * 0.01<s>) 0.01<s> c

        let struct (px0, _) = Discs.momentum masses d0
        let struct (px, _) = Discs.momentum masses c
        Assert.True(Discs.kinetic masses c < 0.5 * Discs.kinetic masses d0)
        Assert.True(abs (px - px0) < 1e-12<m/s> * 400.0)

    /// A step that reaches its cap finishes without colliding and is counted, rather than running
    /// on.
    [<Fact>]
    let ``the cap is counted`` () =
        let d0, radii, masses = gas 5UL
        let step = Collide.step (Collide.create radii masses 1.0 (Box.Periodic(1.0<m>, 1.0<m>)) |> Collide.withCap 2)
        let mutable d = d0

        for k in 0..49 do
            d <- step (float k * 0.01<s>) 0.01<s> d

        Assert.True(d.Capped > 0L, "no step reached a cap of two contacts")
        Assert.True(d.Collisions <= 2L * 50L)

    /// Gate 4 for a collision step: a seek is the straight run.
    [<Fact>]
    let ``seeking a gas equals the straight run`` () =
        let d0, radii, masses = gas 13UL
        let sim = Sim.ofStep 0.01<s> (Collide.step (Collide.create radii masses 1.0 (Box.Periodic(1.0<m>, 1.0<m>)))) d0
        let traj = Sim.trajectory 0.1<s> 1.0<s> sim

        for t in [ 0.0<s>; 0.123<s>; 0.5<s>; 0.777<s>; 1.0<s>; 0.05<s> ] do
            Assert.Equal(Sim.at t sim, traj.At t)

    [<Fact>]
    let ``a collider refuses what it cannot simulate`` () =
        Assert.Throws<ArgumentException>(fun () -> Collide.create [| 1.0<m> |] [| 1.0; 2.0 |] 1.0 Box.Open |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> Collide.create [| -1.0<m> |] [| 1.0 |] 1.0 Box.Open |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> Collide.create [| 1.0<m> |] [| 1.0 |] 1.2 Box.Open |> ignore) |> ignore

// ======================================================================================
// Phase 8 — Poincaré sections (D142): gate 8
// ======================================================================================

module ``Poincaré sections`` =

    /// Gate 8, stroboscopic: a damped oscillator driven at Ω settles onto a period-1 orbit, whose
    /// stroboscopic section is one point, `(A cos φ, A Ω sin φ)` in closed form.
    [<Fact>]
    let ``a period-1 orbit is one stroboscopic point`` () =
        let gamma, w0, bigW, f = 0.5</s>, 2.0</s>, 1.3</s>, 1.0<m/s^2>
        let flow: Flow2T<m, m/s, s> = fun t x v -> struct (v, -gamma * v - w0 * w0 * x + f * cos (bigW * t))
        let period = 2.0 * Math.PI / bigW
        let sim = Sim.ofStep 1e-3<s> (Ode.rk4 flow) (struct (1.0<m>, 0.0<m/s>))
        let points = Poincare.stroboscopic period (Sim.trajectory period (60.0 * period) sim)
        Assert.Equal(61, points.Length)
        Assert.Equal(60.0 * period, points.[60].Time)

        let amplitude = f / sqrt ((w0 * w0 - bigW * bigW) * (w0 * w0 - bigW * bigW) + gamma * gamma * bigW * bigW)
        let phase = atan2 (float (gamma * bigW)) (float (w0 * w0 - bigW * bigW))
        let xe, ve = amplitude * cos phase, amplitude * bigW * sin phase

        // The transient decays as e^(−γt/2); after 40 periods it is below 10⁻¹⁰ m.
        for p in points.[40..] do
            let struct (x, v) = p.State
            Assert.True(abs (x - xe) < 1e-9<m> && abs (v - ve) < 1e-9<m/s>, sprintf "(%g, %g) at %g s, expected (%g, %g)" (float x) (float v) (float p.Time) (float xe) (float ve))

        // And early on it is not there yet: the section shows the approach.
        let struct (x0, _) = points.[1].State
        Assert.True(abs (x0 - xe) > 1e-3<m>)

    let private omega = 2.0</s>
    let private circle: Flow2T<m, m/s, s> = fun _ x v -> struct (v, -omega * omega * x)

    /// The rising crossings of `x = 0` by `x = cos ωt`: at `ωt = 3π/2 + 2πk`, with `v = ω`.
    let private crossings (horizon: float<s>) =
        [ for k in 0..100 do
              let t = (1.5 * Math.PI + 2.0 * Math.PI * float k) / omega
              if t <= horizon then yield t ]

    let private section (direction: Direction) (step: Step<struct (float<m> * float<m/s>), s>) (h: float<s>) =
        Sim.ofStep h step (struct (1.0<m>, 0.0<m/s>))
        |> Poincare.section direction (fun (struct (x: float<m>, _)) -> x) 20.0<s>

    /// Gate 8, spatial: each orbit crosses once, on the rising side, on the surface, and the point
    /// is right to the integrator's order, not to an interpolation's.
    [<Fact>]
    let ``a spatial section is crossed once an orbit, on its side, at the integrator's order`` () =
        let expected = crossings 20.0<s>
        let points = section Direction.Rising (Ode.rk4 circle) 0.01<s>
        Assert.Equal(expected.Length, points.Length)

        for p, t in Array.zip points (Array.ofList expected) do
            let struct (x, v) = p.State
            // RK4's own phase error at this step is a few 10⁻⁹ s by 20 s; the order is measured below.
            Assert.True(abs (p.Time - t) < 1e-7<s>, sprintf "crossed at %.12f s, expected %.12f" (float p.Time) (float t))
            Assert.True(abs x < 1e-10<m>, sprintf "%g m off the surface" (float x))
            Assert.True(v > 0.0<m/s>)

        // The error in the crossing times against the step: RK4's fourth order, and Verlet's
        // second. The times carry the phase error; v at the crossing is the orbit's amplitude
        // times ω, whose error on a linear oscillator is one order higher.
        let timeErrors (points: SectionPoint<'s>[]) =
            Array.map2 (fun (p: SectionPoint<'s>) t -> abs (float (p.Time - t))) points (Array.ofList (crossings 20.0<s>)) |> Array.max

        let errorAt (step: Step<_, s>) (h: float<s>) = timeErrors (section Direction.Rising step h)

        let order (step: Step<_, s>) (hs: float<s> list) =
            let xs = hs |> List.map (float >> log) |> Array.ofList
            let ys = hs |> List.map (errorAt step >> log) |> Array.ofList
            slope xs ys

        let rk4 = order (Ode.rk4 circle) [ 0.08<s>; 0.04<s>; 0.02<s>; 0.01<s> ]
        Assert.True(abs (rk4 - 4.0) <= 0.3, sprintf "RK4's section points converge at order %.3f" rk4)

        let verletSection (h: float<s>) =
            Sim.ofStep h (Ode.verlet (fun _ (q: float<m>[]) -> q |> Array.map (fun x -> -omega * omega * x))) (struct ([| 1.0<m> |], [| 0.0<m/s> |]))
            |> Poincare.section Direction.Rising (fun (struct (q: float<m>[], _)) -> q.[0]) 20.0<s>
            |> timeErrors

        let hs = [ 0.04<s>; 0.02<s>; 0.01<s>; 0.005<s> ]
        let verlet = slope (hs |> List.map (float >> log) |> Array.ofList) (hs |> List.map (verletSection >> log) |> Array.ofList)
        Assert.True(abs (verlet - 2.0) <= 0.3, sprintf "Verlet's section points converge at order %.3f" verlet)

    [<Fact>]
    let ``falling and either`` () =
        let falling = section Direction.Falling (Ode.rk4 circle) 0.01<s>
        let either = section Direction.Either (Ode.rk4 circle) 0.01<s>
        Assert.True(falling |> Array.forall (fun p -> let struct (_, v) = p.State in v < 0.0<m/s>))
        Assert.Equal(falling.Length + (crossings 20.0<s>).Length, either.Length)
        // Interleaved, never two in a row on one side.
        either |> Array.pairwise |> Array.iter (fun (a, b) ->
            let struct (_, va) = a.State
            let struct (_, vb) = b.State
            Assert.True(sign (float va) <> sign (float vb)))

    /// Adaptively, through the dense output, and alongside the simulation's own events.
    [<Fact>]
    let ``adaptively, and beside other events`` () =
        let sim = Sim.ofAdaptive 0.01<s> (Ode.dopri5 1e-10<m> 1e-10<m/s> 1e-10 circle |> Ode.withMaxStep 0.1<s>) (struct (1.0<m>, 0.0<m/s>))

        let points = Poincare.section Direction.Rising (fun (struct (x: float<m>, _)) -> x) 20.0<s> sim
        Assert.Equal((crossings 20.0<s>).Length, points.Length)

        for p in points do
            let struct (_, v) = p.State
            Assert.True(abs (v - omega * 1.0<m>) < 1e-8<m/s>)

        // A simulation with an event of its own that stops it at its first maximum of v, at
        // ωt = 3π/2, keeps the crossings before it: one, at the same time.
        let stopper = Event.create "top" Direction.Falling Response.Stop (fun (struct (_: float<m>, v: float<m/s>)) -> v - 1.999<m/s>)
        let stopped = Poincare.section Direction.Rising (fun (struct (x: float<m>, _)) -> x) 20.0<s> (sim |> Sim.withEvents [ stopper ])
        Assert.Equal(1, stopped.Length)
        Assert.True(abs (stopped.[0].Time - points.[0].Time) < 1e-6<s>)

    [<Fact>]
    let ``a stroboscopic period must be positive`` () =
        let traj = Sim.trajectory 1.0<s> 2.0<s> (Sim.ofStep 0.1<s> (Ode.rk4 circle) (struct (1.0<m>, 0.0<m/s>)))
        Assert.Throws<ArgumentException>(fun () -> Poincare.stroboscopic 0.0<s> traj |> ignore) |> ignore
        Assert.Equal(1, (Poincare.stroboscopic 5.0<s> traj).Length)

// ======================================================================================
// Phase 9 — unsteady fields, pathlines, particles (D142, D145): gate 11
// ======================================================================================

module ``pathlines and particles`` =

    /// A rotation about the origin at `ω(t) = ω₀ (1 + ½ sin t)`: linear in space, so bilinear
    /// interpolation is exact on it and a gridded copy differs from it only in time.
    let private w0 = 1.0</s>
    let private rate (t: float<s>) = w0 * (1.0 + 0.5 * sin (float t))
    let private turned (t: float<s>) = float w0 * (float t - 0.5 * cos (float t) + 0.5)

    let private rotation: Unsteady<m, m, s> = fun t x y -> struct (-rate t * y, rate t * x)

    let private exact (x0: float<m>) (y0: float<m>) (t: float<s>) =
        let a = turned t
        struct (x0 * cos a - y0 * sin a, x0 * sin a + y0 * cos a)

    let private seeds = struct ([| 1.0<m>; 0.0<m>; -0.5<m>; 1.2<m> |], [| 0.0<m>; 0.8<m>; 0.5<m>; -1.0<m> |])

    /// The largest distance from the closed form after `horizon`, stepping at `h`.
    let private error (field: Unsteady<m, m, s>) (h: float<s>) (horizon: float<s>) =
        let n = int (Math.Round(horizon / h))
        let struct (x, y) = Sim.ofStep h (Particles.step field) seeds |> Sim.run n
        let struct (x0, y0) = seeds

        Array.init x.Length (fun i ->
            let struct (xe, ye) = exact x0.[i] y0.[i] (float n * h)
            sqrt (float ((x.[i] - xe) * (x.[i] - xe) + (y.[i] - ye) * (y.[i] - ye))))
        |> Array.max

    let private orderOf (f: float -> float) (hs: float list) =
        slope (hs |> List.map log |> Array.ofList) (hs |> List.map (f >> log) |> Array.ofList)

    let private grid (t: float<s>) : VectorGrid<m, m, m/s, m/s> =
        VectorGrid.sample (Grid.linear -2.0<m> 2.0<m> 9) (Grid.linear -2.0<m> 2.0<m> 9) (rotation t)

    [<Fact>]
    let ``a rotation's pathlines match its closed form at RK4's order`` () =
        let errs = [ 0.2; 0.1; 0.05; 0.025 ] |> List.map (fun h -> error rotation (h * 1.0<s>) 5.0<s>)
        let got = orderOf (fun h -> error rotation (h * 1.0<s>) 5.0<s>) [ 0.2; 0.1; 0.05; 0.025 ]
        Assert.True(abs (got - 4.0) <= 0.2, sprintf "order %.3f, errors %A" got errs)

    /// A steady double gyre: a pathline is a streamline in metres, a level set of the
    /// streamfunction `ψ = sin πx sin πy`, which it keeps to RK4's error.
    [<Fact>]
    let ``in a steady field a pathline is a streamline`` () =
        let psi (x: float<m>) (y: float<m>) = sin (Math.PI * float x) * sin (Math.PI * float y)
        let gyre: Flow<m, m, s> = fun x y -> struct (-Math.PI * sin (Math.PI * float x) * cos (Math.PI * float y) * 1.0<m/s>, Math.PI * cos (Math.PI * float x) * sin (Math.PI * float y) * 1.0<m/s>)
        let start = struct ([| 0.3<m>; 1.6<m>; 0.5<m> |], [| 0.5<m>; 0.3<m>; 0.1<m> |])

        let drift (h: float<s>) =
            let n = int (Math.Round(10.0<s> / h))
            let struct (x, y) = Sim.ofStep h (Particles.step (Unsteady.ofSteady gyre)) start |> Sim.run n
            let struct (x0, y0) = start
            Array.init 3 (fun i -> abs (psi x.[i] y.[i] - psi x0.[i] y0.[i])) |> Array.max

        Assert.True(drift 0.005<s> < 1e-7, sprintf "ψ drifted by %g" (drift 0.005<s>))
        let got = orderOf (fun h -> drift (h * 1.0<s>)) [ 0.08; 0.04; 0.02; 0.01 ]
        Assert.True(got > 3.7, sprintf "ψ's drift falls at order %.3f" got)

    /// Snapshots are second order in their spacing whatever the integrator: at a fine RK4 step,
    /// halving the spacing quarters the error.
    [<Fact>]
    let ``snapshots converge at second order in their spacing`` () =
        let withSpacing (dt: float) =
            let times = Array.init (int (Math.Round(6.0 / dt)) + 1) (fun k -> float k * dt * 1.0<s>)
            error (Unsteady.ofSnapshots times (times |> Array.map grid)) 0.001<s> 5.0<s>

        let got = orderOf withSpacing [ 0.4; 0.2; 0.1; 0.05 ]
        Assert.True(abs (got - 2.0) <= 0.2, sprintf "order %.3f in the snapshot spacing" got)

    /// A grid built at every stage time is exact in time, so the integrator's order is back.
    [<Fact>]
    let ``a grid per stage time converges at the integrator's order`` () =
        let got = orderOf (fun h -> error (Unsteady.ofFunction grid) (h * 1.0<s>) 5.0<s>) [ 0.2; 0.1; 0.05; 0.025 ]
        Assert.True(abs (got - 4.0) <= 0.2, sprintf "order %.3f" got)

    /// The batch step builds a grid per stage time, never per particle: the same count for one
    /// particle as for a thousand.
    [<Fact>]
    let ``a batch step builds one grid per stage time, whatever the count`` () =
        let builds (n: int) =
            let mutable count = 0
            let field = Unsteady.ofFunction (fun t -> count <- count + 1; grid t)
            let start = struct (Array.init n (fun i -> float i / float n * 1.0<m>), Array.zeroCreate n)
            Sim.ofStep 0.1<s> (Particles.step field) start |> Sim.run 10 |> ignore
            count

        // Stages at t, t + h/2 (twice) and t + h: three times a step at most, and fewer when the
        // next step starts at the time this one ended (`(k + 1) · h` and `k · h + h` can differ by
        // an ulp, so not always).
        Assert.InRange(builds 1, 1 + 2 * 10, 3 * 10)
        Assert.Equal(builds 1, builds 1000)

    /// Concurrent seeks of particles carried by a grid built per stage time see the serial ones:
    /// the kept grid is replaced whole, never read half-written.
    [<Fact>]
    let ``concurrent seeks through a built field equal serial ones`` () =
        let sim = Sim.ofStep 0.05<s> (Particles.step (Unsteady.ofFunction grid)) seeds
        let traj = Sim.trajectory 0.5<s> 5.0<s> sim
        let ts = Array.init 400 (fun i -> float ((i * 7919) % 400) / 80.0 * 1.0<s>)
        let serial = ts |> Array.map traj.At
        let concurrent = ts |> Array.Parallel.map traj.At

        for i in 0 .. ts.Length - 1 do
            let struct (sx, sy) = serial.[i]
            let struct (cx, cy) = concurrent.[i]
            Assert.Equal<float<m>[]>(sx, cx)
            Assert.Equal<float<m>[]>(sy, cy)

    [<Fact>]
    let ``streaks, pathlines and what is outside`` () =
        let field = Unsteady.ofSteady (fun (_: float<m>) (_: float<m>) -> struct (1.0<m/s>, 0.0<m/s>))
        let streak = Sim.ofStep 0.1<s> (Particles.streakStep [| 0.0<m> |] [| 0.0<m> |] field) (struct ([| 0.0<m> |], [| 0.0<m> |]))
        let struct (x, _) = Sim.run 10 streak
        Assert.Equal(11, x.Length)
        // The oldest particle has gone 1 m; each later one 0.1 m less.
        for i in 0..10 do
            Assert.Equal(1.0 - 0.1 * float i, float x.[i], 12)

        let lines = Particles.pathlines [| 0.0<s>; 0.5<s>; 1.0<s> |] (Sim.trajectory 0.5<s> 1.0<s> streak)
        Assert.Equal(11, lines.Length)
        let struct (lastX, _) = lines.[10]
        Assert.True(Double.IsNaN(float lastX.[0]) && Double.IsNaN(float lastX.[1]) && lastX.[2] = 0.0<m>)

        // A snapshot field has no velocity outside its grid or its times.
        let snaps = Unsteady.ofSnapshots [| 0.0<s>; 1.0<s> |] [| grid 0.0<s>; grid 1.0<s> |]
        let struct (u, _) = snaps 0.5<s> 3.0<m> 0.0<m>
        let struct (u2, _) = snaps 2.0<s> 0.0<m> 0.0<m>
        Assert.True(Double.IsNaN(float u) && Double.IsNaN(float u2))

    [<Fact>]
    let ``snapshots on different grids are refused`` () =
        let other = VectorGrid.sample (Grid.linear -2.0<m> 2.0<m> 5) (Grid.linear -2.0<m> 2.0<m> 9) (rotation 1.0<s>)
        let e = Assert.Throws<ArgumentException>(fun () -> Unsteady.ofSnapshots [| 0.0<s>; 1.0<s> |] [| grid 0.0<s>; other |] |> ignore)
        Assert.Contains("snapshot 1 is on a different grid", e.Message)
        Assert.Throws<ArgumentException>(fun () -> Unsteady.ofSnapshots [| 1.0<s>; 0.0<s> |] [| grid 0.0<s>; grid 1.0<s> |] |> ignore) |> ignore

// ======================================================================================
// Phase 10 — grid solvers (D138): gates 12 and 13
// ======================================================================================

module ``grid solvers`` =

    let private square (n: int) (lo: float<m>) (hi: float<m>) (f: float<m> -> float<m> -> float<K>) : ScalarGrid<m, m, K> =
        ScalarGrid.sample (Grid.linear lo hi n) (Grid.linear lo hi n) f

    let private cold = Boundaries.all (Boundary.Dirichlet 0.0<K>)
    let private insulated = Boundaries.all (Boundary.Neumann 0.0<K/m>)
    let private torus: Boundaries<m, K> = Boundaries.all Boundary.Periodic

    let private maxAbs (xs: float<'u>[]) = xs |> Array.fold (fun m x -> max m (abs (float x))) 0.0

    /// A Gaussian of σ = 0.1 m spreading at α = 10⁻³ m²/s on a 2 m plate: its peak falls as
    /// `σ² / (σ² + 2αt)` and it stays Gaussian.
    let private alpha = 1e-3<m^2/s>
    let private sigma = 0.1<m>
    let private gaussian = square 101 -1.0<m> 1.0<m> (fun x y -> exp (-(x * x + y * y) / (2.0 * sigma * sigma)) * 1.0<K>)

    let private spread (t: float<s>) (x: float<m>) (y: float<m>) =
        let s2 = sigma * sigma + 2.0 * alpha * t
        float (sigma * sigma / s2) * exp (-(x * x + y * y) / (2.0 * s2))

    let private errorAgainst (t: float<s>) (g: ScalarGrid<m, m, K>) =
        let n = g.Xs.Length
        Array.init g.Values.Length (fun k -> abs (float g.Values.[k] - spread t g.Xs.[k % n] g.Ys.[k / n])) |> Array.max

    [<Fact>]
    let ``gate 12: a Gaussian spreads as the analytic one, explicitly and implicitly`` () =
        let limit = Fdm.explicitLimit alpha gaussian
        Assert.Equal(0.1, float limit, 12)

        let explicitRun = Sim.ofStep 0.05<s> (Fdm.heat alpha cold) gaussian |> Sim.run 40
        let implicitRun = Sim.ofStep 0.2<s> (Fdm.heatImplicit alpha cold) gaussian |> Sim.run 10
        let ee = errorAgainst 2.0<s> explicitRun
        let ie = errorAgainst 2.0<s> implicitRun
        Assert.True(ee < 2e-3, sprintf "explicit: %g K from the analytic spread" ee)
        Assert.True(ie < 2e-3, sprintf "implicit: %g K from the analytic spread" ie)

    [<Fact>]
    let ``gate 12: the explicit limit is refused with both numbers; the implicit takes ten times it`` () =
        let step = Fdm.heat alpha cold
        let e = Assert.Throws<ArgumentException>(fun () -> step 0.0<s> 0.11<s> gaussian |> ignore)
        Assert.Contains("0.11 s is past forward Euler's limit of 0.1 s", e.Message)

        let big = Sim.ofStep 1.0<s> (Fdm.heatImplicit alpha cold) gaussian |> Sim.states 20

        for g in big do
            Assert.True(maxAbs g.Values <= 1.0 + 1e-12, "the implicit solution grew")

        Assert.True(errorAgainst 20.0<s> big.[20] < 2e-2)

    /// The checkerboard is the five-point stencil's highest mode, eigenvalue −8/h². Forward Euler
    /// multiplies it by `1 − 8αΔt/h²` a step and ADI by `((1 − 4s)/(1 + 4s))²`, `s = αΔt/2h²`. And
    /// `Field.laplacian` gives it exactly zero, which is why the solvers do not use it.
    [<Fact>]
    let ``gate 12: a checkerboard damps at the five-point rate, and Field.laplacian does not see it`` () =
        let board = ScalarGrid.sample (Grid.linear 0.0<m> 15.0<m> 16) (Grid.linear 0.0<m> 15.0<m> 16) (fun x y -> (if (int x + int y) % 2 = 0 then 1.0 else -1.0) * 1.0<K>)
        let a = 0.1<m^2/s>

        let lap = Fdm.laplacian5 torus board
        Assert.True(Array.forall2 (fun (l: float<K/m^2>) (v: float<K>) -> float l = -8.0 * float v) lap.Values board.Values)

        let ratio (g: ScalarGrid<m, m, K>) = float (g.Values.[17] / board.Values.[17])
        let explicitOne = Fdm.heat a torus 0.0<s> 1.0<s> board
        Assert.Equal(1.0 - 8.0 * 0.1, ratio explicitOne, 12)
        let s = 0.1 * 1.0 / 2.0
        let implicitOne = Fdm.heatImplicit a torus 0.0<s> 1.0<s> board
        Assert.Equal(((1.0 - 4.0 * s) / (1.0 + 4.0 * s)) ** 2.0, ratio implicitOne, 12)

        // The wide stencil: zero on every interior node.
        let wide = Field.laplacian board
        for j in 2..13 do
            for i in 2..13 do
                Assert.Equal(0.0, float wide.Values.[j * 16 + i], 12)

    /// Crank–Nicolson is second order in Δt, forward Euler first: against a run at a tiny step on
    /// the same grid, so the spatial error cancels.
    [<Fact>]
    let ``gate 12: Crank–Nicolson is second order in time`` () =
        let mode = square 21 0.0<m> 1.0<m> (fun x y -> sin (Math.PI * float x) * sin (Math.PI * float y) * 1.0<K>)
        let a = 0.05<m^2/s>
        let at (step: Step<ScalarGrid<m, m, K>, s>) (dt: float<s>) = Sim.ofStep dt step mode |> Sim.run (int (Math.Round(0.2<s> / dt)))
        let reference = at (Fdm.heatImplicit a cold) 1e-4<s>
        let err (step: Step<ScalarGrid<m, m, K>, s>) (dt: float) = maxAbs (Array.map2 (-) (at step (dt * 1.0<s>)).Values reference.Values)
        let dts = [ 0.02; 0.01; 0.005; 0.0025 ]
        let order step = slope (dts |> List.map log |> Array.ofList) (dts |> List.map (err step >> log) |> Array.ofList)
        let cn = order (Fdm.heatImplicit a cold)
        Assert.True(abs (cn - 2.0) <= 0.2, sprintf "Crank–Nicolson at order %.3f" cn)
        let fe = slope ([ 0.002; 0.001; 0.0005 ] |> List.map log |> Array.ofList) ([ 0.002; 0.001; 0.0005 ] |> List.map (err (Fdm.heat a cold) >> log) |> Array.ofList)
        Assert.True(abs (fe - 1.0) <= 0.2, sprintf "forward Euler at order %.3f" fe)

    /// An insulated plate keeps its heat: the trapezoid sum, half weights on the edges, is what the
    /// mirrored ghost nodes conserve.
    [<Fact>]
    let ``an insulated plate keeps its heat; a periodic one too`` () =
        let blob = square 31 0.0<m> 1.0<m> (fun x y -> exp (-((x - 0.3<m>) * (x - 0.3<m>) + (y - 0.6<m>) * (y - 0.6<m>)) / 0.01<m^2>) * 1.0<K>)
        let weighted (g: ScalarGrid<m, m, K>) =
            let n = g.Xs.Length
            let w k = (if k = 0 || k = n - 1 then 0.5 else 1.0)
            Array.init g.Values.Length (fun k -> w (k % n) * w (k / n) * float g.Values.[k]) |> Array.sum

        for step in [ Fdm.heat 1e-3<m^2/s> insulated; Fdm.heatImplicit 1e-3<m^2/s> insulated ] do
            let after = Sim.ofStep 0.2<s> step blob |> Sim.run 200
            Assert.Equal(weighted blob, weighted after, 9)

        let ring = ScalarGrid.sample (Grid.linear 0.0<m> 0.95<m> 20) (Grid.linear 0.0<m> 0.95<m> 20) (fun x _ -> sin (2.0 * Math.PI * float x) * 1.0<K>)
        for step in [ Fdm.heat 1e-3<m^2/s> torus; Fdm.heatImplicit 1e-3<m^2/s> torus ] do
            let after = Sim.ofStep 0.2<s> step ring |> Sim.run 100
            Assert.Equal(Array.sum (ring.Values |> Array.map float), Array.sum (after.Values |> Array.map float), 9)

    [<Fact>]
    let ``Dirichlet sides hold, and a one-sided periodic axis or an uneven grid is refused`` () =
        let warm = { cold with Left = Boundary.Dirichlet 300.0<K> }
        let after = Sim.ofStep 0.1<s> (Fdm.heatImplicit 1e-3<m^2/s> warm) gaussian |> Sim.run 5

        for j in 1..99 do
            Assert.Equal(300.0, float after.Values.[j * 101])
            Assert.Equal(0.0, float after.Values.[j * 101 + 100])

        let e = Assert.Throws<ArgumentException>(fun () -> Fdm.laplacian5 { torus with Left = Boundary.Neumann 0.0<K/m> } gaussian |> ignore)
        Assert.Contains("both sides of an axis", e.Message)
        let uneven = ScalarGrid.sample [| 0.0<m>; 0.1<m>; 0.3<m>; 0.4<m> |] (Grid.linear 0.0<m> 1.0<m> 5) (fun _ _ -> 1.0<K>)
        let e2 = Assert.Throws<ArgumentException>(fun () -> Fdm.laplacian5 cold uneven |> ignore)
        Assert.Contains("uniform grid", e2.Message)

    /// A standing wave on a fixed square: at a quarter period the mode is at zero, and the error
    /// there is the phase error, second order in h. (After a whole period the mode is at the top
    /// of its cosine, so the error is the phase error squared and falls at fourth order, which
    /// says nothing about the scheme.)
    [<Fact>]
    let ``the wave equation: a standing mode, the CFL limit, second order`` () =
        let c = 1.0<m/s>

        let run (n: int) =
            let u = ScalarGrid.sample (Grid.linear 0.0<m> 1.0<m> n) (Grid.linear 0.0<m> 1.0<m> n) (fun x y -> sin (Math.PI * float x) * sin (Math.PI * float y) * 1.0<m>)
            let v = { Xs = u.Xs; Ys = u.Ys; Values = Array.zeroCreate<float<m/s>> u.Values.Length }
            let limit = Fdm.waveLimit c u
            let quarter = 2.0 / sqrt 2.0 / 4.0 * 1.0<s>
            let steps = int (ceil (quarter / (0.5 * limit)))
            let dt = quarter / float steps
            let struct (uT, _) = Sim.ofStep dt (Fdm.wave c (Boundaries.all (Boundary.Dirichlet 0.0<m>))) (struct (u, v)) |> Sim.run steps
            maxAbs uT.Values

        let e1, e2 = run 17, run 33
        Assert.True(e1 < 0.01, sprintf "at a quarter period, %g m from zero" e1)
        let order = log (e1 / e2) / log 2.0
        Assert.True(abs (order - 2.0) <= 0.2, sprintf "order %.3f in h" order)

        let u = ScalarGrid.sample (Grid.linear 0.0<m> 1.0<m> 11) (Grid.linear 0.0<m> 1.0<m> 11) (fun _ _ -> 0.0<m>)
        let v = { Xs = u.Xs; Ys = u.Ys; Values = Array.zeroCreate<float<m/s>> u.Values.Length }
        let e = Assert.Throws<ArgumentException>(fun () -> Fdm.wave c (Boundaries.all (Boundary.Dirichlet 0.0<m>)) 0.0<s> 0.1<s> (struct (u, v)) |> ignore)
        Assert.Contains("CFL limit", e.Message)

    /// Gate 13: `∇²u = −2π² sin πx sin πy` on the unit square, held at zero, recovers
    /// `sin πx sin πy` at second order in h.
    [<Fact>]
    let ``gate 13: Poisson recovers a manufactured solution at second order`` () =
        let solveAt (n: int) =
            let f = ScalarGrid.sample (Grid.linear 0.0<m> 1.0<m> n) (Grid.linear 0.0<m> 1.0<m> n) (fun x y -> -2.0 * Math.PI * Math.PI * sin (Math.PI * float x) * sin (Math.PI * float y) * 1.0<V/m^2>)
            let sol = Fdm.poisson (Boundaries.all (Boundary.Dirichlet 0.0<V>)) 1e-12 100_000 f
            Assert.True(sol.Converged, sprintf "%d sweeps, residual %g" sol.Iterations (float sol.Residual))
            let xs = sol.Solution.Xs
            Array.init sol.Solution.Values.Length (fun k -> abs (float sol.Solution.Values.[k] - sin (Math.PI * float xs.[k % n]) * sin (Math.PI * float xs.[k / n]))) |> Array.max, sol.Iterations

        let runs = [ 9; 17; 33; 65 ] |> List.map solveAt
        let hs = [ 8.0; 16.0; 32.0; 64.0 ] |> List.map (fun n -> log (1.0 / n)) |> Array.ofList
        let order = slope hs (runs |> List.map (fst >> log) |> Array.ofList)
        Assert.True(abs (order - 2.0) <= 0.1, sprintf "order %.3f in h" order)
        // The optimal factor: sweeps grow like n, not n².
        let sweeps = runs |> List.map snd
        // Gauss–Seidel's would grow sixteenfold from 17 to 65 nodes; this grows about fourfold.
        Assert.True(sweeps.[3] < 6 * sweeps.[1], sprintf "sweeps %A" sweeps)

        let e = Assert.Throws<ArgumentException>(fun () -> Fdm.poisson (Boundaries.all (Boundary.Neumann 0.0<V/m>)) 1e-9 10 (ScalarGrid.sample (Grid.linear 0.0<m> 1.0<m> 5) (Grid.linear 0.0<m> 1.0<m> 5) (fun _ _ -> 0.0<V/m^2>)) |> ignore)
        Assert.Contains("no Dirichlet side", e.Message)

/// M5.5 phase 11, gate 10's .NET half: the runs Node prints are these, to twelve decimals.
module ``the simulations Node runs`` =

    [<Fact>]
    let ``print as golden/sim-runs.txt says`` () = Golden.check "sim-runs.txt" (SimRuns.text ())
