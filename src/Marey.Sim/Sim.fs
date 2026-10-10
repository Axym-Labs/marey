namespace Marey

open System
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols
#if !FABLE_COMPILER
open Microsoft.FSharp.Reflection
#endif

/// §8.2 — a simulation is a step function and a start.
///
/// Simulations are sequential and `Anim` asks for random access to time; `Sim.trajectory` is the
/// bridge, and this record is what it needs. The step is any integrator's (`Ode`), and the step
/// size is stated once, here, because time is `k · H` for the `k`-th step and never a sum of
/// `H`s.
type Sim<'s> =
    {
        /// The state at `t = 0`.
        Initial: 's
        /// The step size.
        H: float<s>
        /// From the state at `t`, the state at `t + h`.
        Step: Step<'s, s>
        /// A copy of a state that is safe to keep while stepping goes on: the identity for an
        /// immutable state, a deep copy for one a step updates in place (a grid solver's buffers).
        Checkpoint: 's -> 's
    }

[<RequireQualifiedAccess>]
module Sim =

    /// A simulation from a step size, a step and a start. The state is taken to be immutable;
    /// `withCheckpoint` says otherwise.
    let ofStep (h: float<s>) (step: Step<'s, s>) (initial: 's) : Sim<'s> =
        if not (h > 0.0<s>) || Double.IsInfinity(float h) then
            invalidArg "h" (sprintf "Marey: a step size must be positive and finite, not %g s." (float h))

        { Initial = initial
          H = h
          Step = step
          Checkpoint = id }

    /// The same simulation with a state that a step updates in place, and `copy` to keep one.
    let withCheckpoint (copy: 's -> 's) (sim: Sim<'s>) : Sim<'s> = { sim with Checkpoint = copy }

    /// The time of step `k`: `k · H`, computed rather than accumulated.
    let timeOf (k: int) (sim: Sim<'s>) : float<s> = float k * sim.H

    /// The state after `n` steps, at `t = n · H`. The start is copied through `Checkpoint` first,
    /// so a simulation that steps in place can be run again.
    let run (n: int) (sim: Sim<'s>) : 's =
        if n < 0 then
            invalidArg "n" (sprintf "Marey: cannot run %d steps." n)

        let mutable s = sim.Checkpoint sim.Initial

        for k in 0 .. n - 1 do
            s <- sim.Step (float k * sim.H) sim.H s

        s

    /// The `n + 1` states at `0, H, …, n · H`, each kept through `Checkpoint`.
    let states (n: int) (sim: Sim<'s>) : 's[] =
        if n < 0 then
            invalidArg "n" (sprintf "Marey: cannot run %d steps." n)

        let out = Array.zeroCreate (n + 1)
        let mutable s = sim.Checkpoint sim.Initial
        out.[0] <- sim.Checkpoint s

        for k in 0 .. n - 1 do
            s <- sim.Step (float k * sim.H) sim.H s
            out.[k + 1] <- sim.Checkpoint s

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

    /// `trajectory`, with the checkpoint table capped at `maxBytes` rather than the default.
    let trajectoryWithin (maxBytes: int64) (every: float<s>) (horizon: float<s>) (sim: Sim<'s>) : Anim<'s> =
        if not (every > 0.0<s>) || Double.IsInfinity(float every) then
            invalidArg "every" (sprintf "Marey: checkpoints must be a positive, finite time apart, not %g s." (float every))

        if not (horizon >= 0.0<s>) || Double.IsInfinity(float horizon) then
            invalidArg "horizon" (sprintf "Marey: a trajectory's horizon must be non-negative and finite, not %g s." (float horizon))

        let h = sim.H

        if horizon / h >= 2.0e9 then
            invalidArg "horizon" (sprintf "Marey: %g s at steps of %g s is more steps than a trajectory counts." (float horizon) (float h))

        let last = lastStepAt h horizon
        // `every` as a whole number of steps, at least one.
        let stride = max 1 (int (Math.Round(every / h)))
        let count = last / stride + 1
        let first = sim.Checkpoint sim.Initial
        let size = estimate first

        if size > 0L && int64 count * size > maxBytes then
            // The fewest checkpoints that fit, and the stride that gives them.
            let fit = max 1L (maxBytes / size - 1L)
            let wider = int (ceil (float last / float fit))

            invalidArg
                "every"
                (sprintf
                    "Marey: %d checkpoints of about %d bytes each is %d MB, over the %d MB allowed. Checkpoint every %g s (%d steps) or more, or raise the cap with Sim.trajectoryWithin."
                    count
                    size
                    (int64 count * size / 1_048_576L)
                    (maxBytes / 1_048_576L)
                    (float wider * float h)
                    wider)

        // One straight run to the last step, keeping every `stride`-th state. Filled here, before
        // the animation exists, and never written again: concurrent seeks read it without a lock.
        let table = Array.zeroCreate count
        let mutable state = first
        table.[0] <- sim.Checkpoint state

        for k in 0 .. last - 1 do
            state <- sim.Step (float k * h) h state

            if (k + 1) % stride = 0 then
                table.[(k + 1) / stride] <- sim.Checkpoint state

        let seek (t: float<s>) : 's =
            let t =
                if not (t > 0.0<s>) then 0.0<s>
                elif t > horizon then horizon
                else t

            let k = min last (lastStepAt h t)
            let c = k / stride
            // A copy, so that a step that works in place cannot write into the table.
            let mutable s = sim.Checkpoint table.[c]

            for j in c * stride .. k - 1 do
                s <- sim.Step (float j * h) h s

            let tk = float k * h
            // The remainder past the last whole step, never stored, so it cannot change the
            // sequence a later seek replays.
            if t > tk then sim.Step tk (t - tk) s else s

        Anim<'s>(horizon, seek)

    /// §8.2's bridge: the simulation as an `Anim` over `[0, horizon]`, seekable at any time.
    ///
    /// One straight run fills a table with the state at every `every` (rounded to a whole number
    /// of steps), and a seek to `t` copies the checkpoint at or below it and steps from there to
    /// the last whole step `k · H ≤ t`, then takes one shorter step to `t`. So seeking to `t` gives
    /// exactly the state a straight run reaches, bit for bit, whatever was seeked before and on
    /// whichever thread: the table is immutable once built and each seek owns its copy.
    ///
    /// Time is clamped to `[0, horizon]` at every door, `AtSeconds` included: past its horizon a
    /// trajectory has no state. `every` is the memory dial: a seek costs at most `every / H` steps,
    /// and the table holds `horizon / every` states. A table estimated over 512 MiB is refused,
    /// naming the `every` that would fit (`trajectoryWithin` sets another cap).
    let trajectory (every: float<s>) (horizon: float<s>) (sim: Sim<'s>) : Anim<'s> =
        trajectoryWithin DefaultCheckpointBytes every horizon sim
