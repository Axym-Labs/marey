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
        Assert.Equal(30.0 * 0.1, float (Sim.timeOf 30 sim))
        Assert.NotEqual(float summed, float (Sim.timeOf 30 sim))

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
