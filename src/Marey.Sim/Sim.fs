namespace Marey

open System
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols
#if !FABLE_COMPILER
open Microsoft.FSharp.Reflection
#endif

/// How a simulation advances: by a fixed step, or by the steps an adaptive integrator chooses.
[<RequireQualifiedAccess>]
type Stepping<'s> =
    /// Steps of `h`, the `k`-th at time `k · h`.
    | Fixed of h: float<s> * step: Step<'s, s>
    /// Accepted steps of an adaptive integrator, its controller started at `h0`.
    | Adaptive of h0: float<s> * step: AdaptiveStep<'s, s>

/// §8.2 — a quantity a simulation should conserve: energy, momentum, mass. Erased to `float`, with
/// the unit it was stated in kept beside it for the drift figure's axis (D136).
type Invariant<'s> =
    { Name: string
      Unit: IUnit
      Of: 's -> float }

/// §8.2 — a simulation is a start and a way to step.
///
/// Simulations are sequential and `Anim` asks for random access to time; `Sim.trajectory` is the
/// bridge, and this record is what it needs. The stepping is any integrator's (`Ode`). A fixed
/// step size is stated once, because time is `k · h` for the `k`-th step and never a sum of
/// `h`s; an adaptive integrator's controller is restarted from what a checkpoint stored.
type Sim<'s> =
    {
        /// The state at `t = 0`.
        Initial: 's
        /// How the state advances.
        Stepping: Stepping<'s>
        /// A copy of a state that is safe to keep while stepping goes on: the identity for an
        /// immutable state, a deep copy for one a step updates in place (a grid solver's buffers).
        Checkpoint: 's -> 's
        /// What the simulation should conserve, for `Sim.drift`. Plotting them against time is
        /// the standard way to catch a broken integrator.
        Invariants: Invariant<'s> list
        /// Crossings to locate and respond to, in the order a tie is broken (D144).
        Events: Event<'s> list
    }

/// One invariant's drift over a trajectory: `Values.[i]` at `Times.[i]`, relative to its initial
/// value, or absolute in its own unit where the initial value is zero and a ratio would not exist.
type Drift =
    { Name: string
      /// The invariant's unit. The drift is in it when `Relative` is false.
      Unit: IUnit
      Relative: bool
      /// The invariant's value at the start, in its unit.
      Initial: float
      Times: float<s>[]
      Values: float[] }

[<RequireQualifiedAccess>]
module Sim =

    let private positive (name: string) (h: float<s>) =
        if not (h > 0.0<s>) || Double.IsInfinity(float h) then
            invalidArg name (sprintf "Marey: a step size must be positive and finite, not %g s." (float h))

    /// A simulation from a step size, a step and a start. The state is taken to be immutable;
    /// `withCheckpoint` says otherwise.
    let ofStep (h: float<s>) (step: Step<'s, s>) (initial: 's) : Sim<'s> =
        positive "h" h

        { Initial = initial
          Stepping = Stepping.Fixed(h, step)
          Checkpoint = id
          Invariants = []
          Events = [] }

    /// A simulation from an adaptive integrator (`Ode.dopri5`), the step its controller starts
    /// from, and a start.
    let ofAdaptive (h0: float<s>) (step: AdaptiveStep<'s, s>) (initial: 's) : Sim<'s> =
        positive "h0" h0

        { Initial = initial
          Stepping = Stepping.Adaptive(h0, step)
          Checkpoint = id
          Invariants = []
          Events = [] }

    /// The same simulation with a state that a step updates in place, and `copy` to keep one.
    let withCheckpoint (copy: 's -> 's) (sim: Sim<'s>) : Sim<'s> = { sim with Checkpoint = copy }

    /// D136 — the simulation with an invariant attached, in no stated unit. In the unit-required
    /// mode (`open Marey.UnitsOfMeasure`) this is shadowed by a form that takes a witness.
    let withInvariant (name: string) (f: 's -> float) (sim: Sim<'s>) : Sim<'s> =
        { sim with Invariants = sim.Invariants @ [ { Name = name; Unit = U.unspecified; Of = f } ] }

    /// `withInvariant` with the invariant's unit as a witness, erased into the record. What the
    /// unit-required mode's `Sim.withInvariant` reaches.
    let internal withInvariantIn<'s, [<Measure>] 'e> (name: string) (u: U<'e>) (f: 's -> float<'e>) (sim: Sim<'s>) : Sim<'s> =
        { sim with Invariants = sim.Invariants @ [ { Name = name; Unit = (u :> IUnit); Of = fun s -> float (f s) } ] }

    /// D144 — the simulation with events attached, after any it has. Their order breaks a tie
    /// between two roots at the same time.
    let withEvents (events: Event<'s> list) (sim: Sim<'s>) : Sim<'s> = { sim with Events = sim.Events @ events }

    // ---- The engine: adaptive steps and events (D143, D144) ------------------------------------------
    //
    // A run is a sequence of units, each one step or one step cut short at an event, between
    // cursors. A fixed step's time is `Origin + J · h`: `k · h` until the first event, and counted
    // from the last event after it, never summed. Without events a fixed step does not come through
    // here: `walk` and `trajectory` have a path of their own that allocates nothing per step.

    /// Where a run is: a boundary between two units, with everything the next unit depends on.
    type private Cursor<'s> =
        {
            /// The time a fixed step counts from: zero, or the last event.
            Origin: float<s>
            /// Fixed steps since `Origin`.
            J: int
            T: float<s>
            S: 's
            /// An adaptive integrator's controller (D143).
            C: Controller<s>
            /// When each event last fired; NaN before it has.
            Last: float<s>[]
            /// Units in a row that ended at an event.
            Streak: int
            Stopped: bool
        }

    /// One unit: the cursor it ends at, the state anywhere in `[start, end)`, and the event that
    /// ended it, if one did.
    type private Unit<'s> =
        { Next: Cursor<'s>
          At: float<s> -> 's
          Hit: Hit option }

    /// Events in a row, each ending a unit before a whole step was taken, that stop a run as jammed.
    [<Literal>]
    let private MaxStreak = 10_000

    let private start (sim: Sim<'s>) : Cursor<'s> =
        { Origin = 0.0<s>
          J = 0
          T = 0.0<s>
          S = sim.Checkpoint sim.Initial
          C =
            match sim.Stepping with
            | Stepping.Fixed(h, _) -> Controller.start h
            | Stepping.Adaptive(h0, _) -> Controller.start h0
          Last = Array.create sim.Events.Length (LanguagePrimitives.FloatWithMeasure<s> nan)
          Streak = 0
          Stopped = false }

    let private copyOf (sim: Sim<'s>) (c: Cursor<'s>) = { c with S = sim.Checkpoint c.S }

    /// The unit that starts at `c`. `c.S` may be stepped in place; `At` works from a copy.
    let private advance (sim: Sim<'s>) (events: Event<'s>[]) (c: Cursor<'s>) : Unit<'s> =
        if c.Stopped then
            let held = c.S

            { Next = { c with T = LanguagePrimitives.FloatWithMeasure<s> infinity }
              At = fun _ -> sim.Checkpoint held
              Hit = None }
        else
            let t0 = c.T
            let g0 = events |> Array.map (fun e -> e.Of c.S)

            // The step: where it ends, the state there, the next controller, and the state at any
            // time inside it.
            let t1, s1, next, stateAt =
                match sim.Stepping with
                | Stepping.Fixed(h, step) ->
                    let start = sim.Checkpoint c.S
                    let at (t: float<s>) = if t > t0 then step t0 (t - t0) (sim.Checkpoint start) else sim.Checkpoint start
                    c.Origin + float (c.J + 1) * h, step t0 h c.S, c.C, at
                | Stepping.Adaptive(_, step) ->
                    let a = step t0 c.C c.S
                    a.T1, a.State, a.Next, a.Dense

            // The earliest root of an event crossed in this step, located on the crossed side.
            let mutable first = -1
            let mutable tau = infinity
            let span = float (t1 - t0)

            for i in 0 .. events.Length - 1 do
                let e = events.[i]
                let g1 = e.Of s1

                if Event.crosses e.Direction g0.[i] g1 then
                    let phi (x: float) = e.Of(stateAt (t0 + LanguagePrimitives.FloatWithMeasure<s> x))
                    let root = Event.locate phi 0.0 span g0.[i] g1 (e.Tolerance * span)

                    if root < tau then
                        first <- i
                        tau <- root

            if first < 0 then
                { Next =
                    { c with
                        J = c.J + 1
                        T = t1
                        S = s1
                        C = next
                        Streak = 0 }
                  At = stateAt
                  Hit = None }
            else
                let e = events.[first]
                let te = t0 + LanguagePrimitives.FloatWithMeasure<s> tau
                let se = stateAt te
                let last = c.Last.[first]

                let tooSoon =
                    match e.MinSeparation with
                    | Some m -> not (Double.IsNaN(float last)) && te - last < m
                    | None -> false

                let state, kind =
                    if tooSoon then
                        match e.Rest with
                        | Some rest -> rest se, HitKind.Rested
                        | None ->
                            se,
                            HitKind.Stopped(
                                sprintf
                                    "%s fired %g s after its previous crossing, under its minimum separation of %g s, and has no rest response"
                                    e.Name
                                    (float (te - last))
                                    (float e.MinSeparation.Value)
                            )
                    else
                        match e.Response with
                        | Response.Continue f -> f se, HitKind.Fired
                        | Response.Stop -> se, HitKind.Stopped(sprintf "%s stops the simulation" e.Name)

                let kind =
                    if c.Streak + 1 >= MaxStreak then
                        HitKind.Stopped(sprintf "%d events in a row without a whole step, the last %s at %g s" MaxStreak e.Name (float te))
                    else
                        kind

                let fired = Array.copy c.Last
                fired.[first] <- te

                { Next =
                    { Origin = te
                      J = 0
                      T = te
                      S = state
                      C = next
                      Last = fired
                      Streak = c.Streak + 1
                      Stopped =
                        match kind with
                        | HitKind.Stopped _ -> true
                        | _ -> false }
                  At = stateAt
                  Hit = Some { Name = e.Name; Time = te; Kind = kind } }

    /// Every state from the start through `n` units (steps; accepted steps for an adaptive
    /// integrator; a step cut short at an event counts as one), with its time, passed to `visit`.
    /// The start is copied through `Checkpoint` first, so a simulation that steps in place can be
    /// run again. After a stop the state and time hold.
    let private walk (n: int) (sim: Sim<'s>) (visit: int -> float<s> -> 's -> unit) : 's =
        if n < 0 then
            invalidArg "n" (sprintf "Marey: cannot run %d steps." n)

        match sim.Stepping, sim.Events with
        | Stepping.Fixed(h, step), [] ->
            let mutable s = sim.Checkpoint sim.Initial
            visit 0 0.0<s> s

            for k in 0 .. n - 1 do
                s <- step (float k * h) h s
                visit (k + 1) (float (k + 1) * h) s

            s
        | _ ->
            let events = Array.ofList sim.Events
            let mutable c = start sim
            visit 0 c.T c.S

            for k in 0 .. n - 1 do
                if not c.Stopped then
                    c <- (advance sim events c).Next

                visit (k + 1) c.T c.S

            c.S

    /// The state after `n` steps: at `t = n · h` for a fixed step without events, wherever the
    /// `n`-th unit ends otherwise.
    let run (n: int) (sim: Sim<'s>) : 's = walk n sim (fun _ _ _ -> ())

    /// The state at time `t` by one straight run from the start, without a checkpoint table: what
    /// a trajectory's seek must equal.
    let at (t: float<s>) (sim: Sim<'s>) : 's =
        let t = if not (t > 0.0<s>) then 0.0<s> else t
        let events = Array.ofList sim.Events
        let mutable c = start sim
        let mutable u = advance sim events c

        while u.Next.T <= t do
            c <- u.Next
            u <- advance sim events c

        u.At t

    /// The events a straight run passes up to `horizon`, each with the state the run went on from
    /// (after the response), kept through `Checkpoint`.
    let internal hitsWithStates (horizon: float<s>) (sim: Sim<'s>) : (Hit * 's) list =
        let events = Array.ofList sim.Events
        let hits = Collections.Generic.List<Hit * 's>()

        if events.Length > 0 then
            let mutable c = start sim

            while not c.Stopped && c.T <= horizon do
                let u = advance sim events c

                match u.Hit with
                | Some hit when hit.Time <= horizon -> hits.Add((hit, sim.Checkpoint u.Next.S))
                | _ -> ()

                c <- u.Next

        List.ofSeq hits

    /// The events a straight run passes up to `horizon`, in order, each with its time and what it
    /// did. A run that stopped ends the list with the reason.
    let events (horizon: float<s>) (sim: Sim<'s>) : Hit list =
        let events = Array.ofList sim.Events
        let hits = Collections.Generic.List<Hit>()

        if events.Length > 0 then
            let mutable c = start sim

            while not c.Stopped && c.T <= horizon do
                let u = advance sim events c

                match u.Hit with
                | Some hit when hit.Time <= horizon -> hits.Add hit
                | _ -> ()

                c <- u.Next

        List.ofSeq hits

    /// The `n + 1` states of `run n`, the start first, each kept through `Checkpoint`.
    let states (n: int) (sim: Sim<'s>) : 's[] =
        let out = Array.zeroCreate (max 0 n + 1)
        walk n sim (fun k _ s -> out.[k] <- sim.Checkpoint s) |> ignore
        out

    /// The times of `states n`.
    let times (n: int) (sim: Sim<'s>) : float<s>[] =
        let out = Array.zeroCreate (max 0 n + 1)
        walk n sim (fun k t _ -> out.[k] <- t) |> ignore
        out

    // ---- Trajectory (D135) ------------------------------------------------------------------------

    /// The checkpoint table `trajectory` refuses to exceed, unless told otherwise: 512 MiB.
    [<Literal>]
    let DefaultCheckpointBytes = 536_870_912L

#if !FABLE_COMPILER
    /// A state's size in bytes, as far as reflection sees it: arrays of numbers by their length,
    /// tuples and records by their fields, an array of anything else as its first element times
    /// its length. What it cannot see inside counts as nothing.
    let rec private bytesOf (depth: int) (o: obj) : int64 =
        if isNull o || depth > 6 then
            0L
        else
            match o with
            | :? Array as a ->
                if a.GetType().GetElementType().IsPrimitive then int64 (Buffer.ByteLength a)
                elif a.Length = 0 then 0L
                else int64 a.Length * bytesOf (depth + 1) (a.GetValue 0)
            | _ ->
                let t = o.GetType()

                if t.IsPrimitive then 8L
                elif FSharpType.IsTuple t then FSharpValue.GetTupleFields o |> Array.sumBy (bytesOf (depth + 1))
                elif FSharpType.IsRecord(t, true) then FSharpValue.GetRecordFields(o, true) |> Array.sumBy (bytesOf (depth + 1))
                else 0L
#endif

    /// An estimate of one checkpoint's size. Under Fable there is no reflection to estimate with,
    /// so it is zero and the table is not capped.
    let private estimate (s: 's) : int64 =
#if FABLE_COMPILER
        0L
#else
        bytesOf 0 (box s)
#endif

    /// The largest `k` with `k · h ≤ t`, for `t ≥ 0`: the division's guess, corrected against the
    /// product the steps themselves are timed by.
    let private lastStepAt (h: float<s>) (t: float<s>) : int =
        let mutable k = int (floor (t / h))

        while float (k + 1) * h <= t do
            k <- k + 1

        while k > 0 && float k * h > t do
            k <- k - 1

        k

    /// Refuses a table of `count` checkpoints the size of `first` over `maxBytes`, naming the
    /// interval that would fit: `wider count` is that interval, given how many checkpoints fit.
    let private capped (maxBytes: int64) (count: int) (first: 's) (wider: int64 -> string) =
        let size = estimate first

        if size > 0L && int64 count * size > maxBytes then
            let fit = max 1L (maxBytes / size - 1L)

            invalidArg
                "every"
                (sprintf
                    "Marey: %d checkpoints of about %d bytes each is %d MB, over the %d MB allowed. Checkpoint every %s or more, or raise the cap with Sim.trajectoryWithin."
                    count
                    size
                    (int64 count * size / 1_048_576L)
                    (maxBytes / 1_048_576L)
                    (wider fit))

    let private clampTo (horizon: float<s>) (t: float<s>) =
        if not (t > 0.0<s>) then 0.0<s>
        elif t > horizon then horizon
        else t

    let private fixedTrajectory maxBytes (every: float<s>) (horizon: float<s>) (sim: Sim<'s>) (h: float<s>) (step: Step<'s, s>) =
        if horizon / h >= 2.0e9 then
            invalidArg "horizon" (sprintf "Marey: %g s at steps of %g s is more steps than a trajectory counts." (float horizon) (float h))

        let last = lastStepAt h horizon
        // `every` as a whole number of steps, at least one.
        let stride = max 1 (int (Math.Round(every / h)))
        let count = last / stride + 1
        let first = sim.Checkpoint sim.Initial

        capped maxBytes count first (fun fit ->
            let wider = int (ceil (float last / float fit))
            sprintf "%g s (%d steps)" (float wider * float h) wider)

        // One straight run to the last step, keeping every `stride`-th state. Filled here, before
        // the animation exists, and never written again: concurrent seeks read it without a lock.
        let table = Array.zeroCreate count
        let mutable state = first
        table.[0] <- sim.Checkpoint state

        for k in 0 .. last - 1 do
            state <- step (float k * h) h state

            if (k + 1) % stride = 0 then
                table.[(k + 1) / stride] <- sim.Checkpoint state

        let seek (t: float<s>) : 's =
            let t = clampTo horizon t
            let k = min last (lastStepAt h t)
            let c = k / stride
            // A copy, so that a step that works in place cannot write into the table.
            let mutable s = sim.Checkpoint table.[c]

            for j in c * stride .. k - 1 do
                s <- step (float j * h) h s

            let tk = float k * h
            // The remainder past the last whole step, never stored, so it cannot change the
            // sequence a later seek replays.
            if t > tk then step tk (t - tk) s else s

        Anim<'s>(horizon, seek)

    /// A trajectory through the engine, for an adaptive integrator or a simulation with events.
    /// Checkpoint `i` is the first unit boundary at or past `i · every`, with everything the next
    /// unit depends on: the controller (D143), when each event last fired, the step origin.
    let private unitTrajectory maxBytes (every: float<s>) (horizon: float<s>) (sim: Sim<'s>) =
        if horizon / every >= 2.0e9 then
            invalidArg "every" (sprintf "Marey: %g s checkpointed every %g s is more checkpoints than a trajectory counts." (float horizon) (float every))

        let events = Array.ofList sim.Events
        let count = lastStepAt every horizon + 1
        let first = start sim

        capped maxBytes count first.S (fun fit -> sprintf "%g s" (float horizon / float fit))

        // A unit that crosses several marks fills them all with the same boundary. A run that
        // stopped fills what is left with where it stopped.
        let table = Array.zeroCreate count
        table.[0] <- copyOf sim first
        let mutable filled = 1
        let mutable c = first

        while filled < count do
            if c.Stopped then
                table.[filled] <- copyOf sim c
                filled <- filled + 1
            else
                c <- (advance sim events c).Next

                while filled < count && float filled * every <= c.T do
                    table.[filled] <- copyOf sim c
                    filled <- filled + 1

        let seek (t: float<s>) : 's =
            let t = clampTo horizon t
            // The last checkpoint at or before `t`: its boundary is at or past `i · every`, so it
            // may lie past `t`, and the start never does.
            let mutable i = min (count - 1) (lastStepAt every t)

            while table.[i].T > t do
                i <- i - 1

            let mutable c = copyOf sim table.[i]
            let mutable u = advance sim events c

            // The unit whose interval `[start, end)` holds `t`: half-open, so a seek to a boundary
            // reads the unit that starts there, as the straight run does.
            while u.Next.T <= t do
                c <- u.Next
                u <- advance sim events c

            u.At t

        Anim<'s>(horizon, seek)

    /// `trajectory`, with the checkpoint table capped at `maxBytes` rather than the default.
    let trajectoryWithin (maxBytes: int64) (every: float<s>) (horizon: float<s>) (sim: Sim<'s>) : Anim<'s> =
        if not (every > 0.0<s>) || Double.IsInfinity(float every) then
            invalidArg "every" (sprintf "Marey: checkpoints must be a positive, finite time apart, not %g s." (float every))

        if not (horizon >= 0.0<s>) || Double.IsInfinity(float horizon) then
            invalidArg "horizon" (sprintf "Marey: a trajectory's horizon must be non-negative and finite, not %g s." (float horizon))

        match sim.Stepping, sim.Events with
        | Stepping.Fixed(h, step), [] -> fixedTrajectory maxBytes every horizon sim h step
        | _ -> unitTrajectory maxBytes every horizon sim

    /// §8.2's bridge: the simulation as an `Anim` over `[0, horizon]`, seekable at any time.
    ///
    /// One straight run fills a table with a checkpoint every `every`, and a seek to `t` copies the
    /// checkpoint at or before it and steps from there. A fixed step rounds `every` to a whole
    /// number of steps, steps to the last whole step `k · h ≤ t`, then takes one shorter step to
    /// `t`. An adaptive integrator checkpoints at the first step boundary past each `k · every`,
    /// with its controller, replays accepted steps to the one holding `t`, and reads its dense
    /// output. Either way a seek to `t` gives exactly the state a straight run reaches, bit for
    /// bit, whatever was seeked before and on whichever thread: the table is immutable once built
    /// and each seek owns its copy.
    ///
    /// Time is clamped to `[0, horizon]` at every door, `AtSeconds` included: past its horizon a
    /// trajectory has no state. `every` is the memory dial: a seek costs the steps in one `every`,
    /// and the table holds `horizon / every` states. A table estimated over 512 MiB is refused,
    /// naming the `every` that would fit (`trajectoryWithin` sets another cap).
    let trajectory (every: float<s>) (horizon: float<s>) (sim: Sim<'s>) : Anim<'s> =
        trajectoryWithin DefaultCheckpointBytes every horizon sim

    // ---- Drift (D136) -------------------------------------------------------------------------------

    /// Each invariant's drift over a trajectory, at `samples` evenly spaced times from 0 to its
    /// duration: `(I(t) − I(0)) / |I(0)|`, or `I(t) − I(0)` in the invariant's unit where `I(0)` is
    /// zero, and `Relative` says which. A simulation with no invariants has no drift to give, and
    /// is refused rather than answered with nothing.
    let drift (samples: int) (trajectory: Anim<'s>) (sim: Sim<'s>) : Drift list =
        if samples < 2 then
            invalidArg "samples" (sprintf "Marey: a drift needs at least two samples, not %d." samples)

        if List.isEmpty sim.Invariants then
            invalidArg "sim" "Marey: the simulation has no invariants; attach one with Sim.withInvariant."

        let d = trajectory.Duration
        let times = Array.init samples (fun i -> d * float i / float (samples - 1))
        let states = times |> Array.map trajectory.At

        sim.Invariants
        |> List.map (fun inv ->
            let values = states |> Array.map inv.Of
            let i0 = values.[0]
            let relative = i0 <> 0.0

            { Name = inv.Name
              Unit = inv.Unit
              Relative = relative
              Initial = i0
              Times = times
              Values = values |> Array.map (fun v -> if relative then (v - i0) / abs i0 else v - i0) })

/// Drawing a drift.
[<RequireQualifiedAccess>]
module Drift =

    /// The same drift under another name: the integrator's, say, when three are compared.
    let named (name: string) (d: Drift) : Drift = { d with Name = name }

    /// The drift in the invariant's own unit, `I(t) − I(0)`, whichever way it was computed: an
    /// energy's drift in joules rather than as a fraction of where it started.
    let absolute (d: Drift) : Drift =
        if d.Relative then { d with Relative = false; Values = d.Values |> Array.map (fun v -> v * abs d.Initial) } else d

    /// The drift's size, `|Values|`, for a logarithmic axis, where an energy that RK4 loses and one
    /// Euler gains are both a distance from where they started.
    let magnitude (d: Drift) : Drift = { d with Values = Array.map abs d.Values }

    /// One line per drift against time in seconds, named in the legend, on one panel. Relative
    /// drifts are dimensionless; absolute ones are in their invariant's unit, and must share it.
    /// A mixture of the two has no common axis and is refused.
    let chart (drifts: Drift list) : Figure =
        match drifts with
        | [] -> invalidArg "drifts" "Marey: no drift to draw."
        | first :: _ ->
            if drifts |> List.exists (fun d -> d.Relative <> first.Relative) then
                invalidArg "drifts" "Marey: relative and absolute drifts have no axis in common; draw them apart."

            let yUnit: IUnit = if first.Relative then U.one else first.Unit
            let line (d: Drift) = Chart.Line(Array.map float d.Times, d.Values, xUnit = U.second, yUnit = yUnit)

            drifts
            |> List.tail
            |> List.fold (fun fig d -> fig |> Chart.add (line d) |> Chart.withName d.Name) (line first |> Chart.withName first.Name)
            |> Chart.withYLabel (if first.Relative then "relative drift" else "drift")
            |> Chart.withXLabel "time"
