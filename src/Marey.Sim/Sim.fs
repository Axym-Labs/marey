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
    }

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
          Checkpoint = id }

    /// A simulation from an adaptive integrator (`Ode.dopri5`), the step its controller starts
    /// from, and a start.
    let ofAdaptive (h0: float<s>) (step: AdaptiveStep<'s, s>) (initial: 's) : Sim<'s> =
        positive "h0" h0

        { Initial = initial
          Stepping = Stepping.Adaptive(h0, step)
          Checkpoint = id }

    /// The same simulation with a state that a step updates in place, and `copy` to keep one.
    let withCheckpoint (copy: 's -> 's) (sim: Sim<'s>) : Sim<'s> = { sim with Checkpoint = copy }

    /// Every state from the start through `n` steps (accepted steps, for an adaptive
    /// integrator), with its time, passed to `visit`. The start is copied through `Checkpoint`
    /// first, so a simulation that steps in place can be run again.
    let private walk (n: int) (sim: Sim<'s>) (visit: int -> float<s> -> 's -> unit) : 's =
        if n < 0 then
            invalidArg "n" (sprintf "Marey: cannot run %d steps." n)

        let mutable s = sim.Checkpoint sim.Initial
        visit 0 0.0<s> s

        match sim.Stepping with
        | Stepping.Fixed(h, step) ->
            for k in 0 .. n - 1 do
                s <- step (float k * h) h s
                visit (k + 1) (float (k + 1) * h) s
        | Stepping.Adaptive(h0, step) ->
            let mutable t = 0.0<s>
            let mutable control = Controller.start h0

            for k in 0 .. n - 1 do
                let a = step t control s
                s <- a.State
                t <- a.T1
                control <- a.Next
                visit (k + 1) t s

        s

    /// The state after `n` steps: at `t = n · h` for a fixed step, wherever the `n`-th accepted
    /// step ends for an adaptive one.
    let run (n: int) (sim: Sim<'s>) : 's = walk n sim (fun _ _ _ -> ())

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

    /// A checkpoint of an adaptive run: a step boundary, the state there, and the controller that
    /// proposes the next step (D143).
    [<Struct>]
    type private Mark<'s> =
        { T: float<s>
          S: 's
          C: Controller<s> }

    let private adaptiveTrajectory maxBytes (every: float<s>) (horizon: float<s>) (sim: Sim<'s>) (h0: float<s>) (step: AdaptiveStep<'s, s>) =
        if horizon / every >= 2.0e9 then
            invalidArg "every" (sprintf "Marey: %g s checkpointed every %g s is more checkpoints than a trajectory counts." (float horizon) (float every))

        let count = lastStepAt every horizon + 1
        let first = sim.Checkpoint sim.Initial

        capped maxBytes count first (fun fit -> sprintf "%g s" (float horizon / float fit))

        // Checkpoint `i` is the first step boundary at or past `i · every`. A step that crosses
        // several of those marks fills them all with the same boundary.
        let table = Array.zeroCreate count
        table.[0] <- { T = 0.0<s>; S = sim.Checkpoint first; C = Controller.start h0 }
        let mutable filled = 1
        let mutable mark = { T = 0.0<s>; S = first; C = Controller.start h0 }

        while filled < count do
            let a = step mark.T mark.C mark.S
            mark <- { T = a.T1; S = a.State; C = a.Next }

            while filled < count && float filled * every <= mark.T do
                // Written out, not `{ mark with … }`: Fable 5.20 compiles a copy-and-update of a
                // mutable struct local through a reference cell and reads the fields off the cell.
                table.[filled] <- { T = mark.T; S = sim.Checkpoint mark.S; C = mark.C }
                filled <- filled + 1

        let seek (t: float<s>) : 's =
            let t = clampTo horizon t
            // The last checkpoint at or before `t`. Its mark is at or past `i · every`, so it may
            // lie past `t`; the start never does.
            let mutable i = min (count - 1) (lastStepAt every t)

            while table.[i].T > t do
                i <- i - 1

            let m = table.[i]
            let mutable a = step m.T m.C (sim.Checkpoint m.S)

            // The step whose interval `[T0, T1)` holds `t`: half-open, so a seek to a boundary
            // reads the step that starts there, as the straight run does.
            while a.T1 <= t do
                a <- step a.T1 a.Next a.State

            a.Dense t

        Anim<'s>(horizon, seek)

    /// `trajectory`, with the checkpoint table capped at `maxBytes` rather than the default.
    let trajectoryWithin (maxBytes: int64) (every: float<s>) (horizon: float<s>) (sim: Sim<'s>) : Anim<'s> =
        if not (every > 0.0<s>) || Double.IsInfinity(float every) then
            invalidArg "every" (sprintf "Marey: checkpoints must be a positive, finite time apart, not %g s." (float every))

        if not (horizon >= 0.0<s>) || Double.IsInfinity(float horizon) then
            invalidArg "horizon" (sprintf "Marey: a trajectory's horizon must be non-negative and finite, not %g s." (float horizon))

        match sim.Stepping with
        | Stepping.Fixed(h, step) -> fixedTrajectory maxBytes every horizon sim h step
        | Stepping.Adaptive(h0, step) -> adaptiveTrajectory maxBytes every horizon sim h0 step

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
