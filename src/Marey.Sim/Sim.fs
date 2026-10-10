namespace Marey

open System
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

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
