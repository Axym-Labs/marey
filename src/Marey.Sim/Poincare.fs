namespace Marey

open System
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// POINCARÉ SECTIONS.
//
// Both kinds are a few lines over what `Sim` already has.
//
// A *stroboscopic* section is the state at every multiple of a period, which a trajectory gives
// exactly, because a seek to `k · T` is the state the straight run reaches there.
//
// A *spatial* section is the state wherever a surface `σ(s) = 0` is crossed in a stated direction.
// Hénon's step does it: integrate one step with `σ` as the independent variable, so the
// point lies on the surface to the integrator's order. That needs `dσ/dt` along the flow, which a
// surface given as a function of the state does not provide. The event engine gets the same
// thing another way: it locates the crossing in time by re-stepping with the integrator itself, so
// the point is the integrator's own state, on the surface to the event's tolerance, with the
// integrator's order of error rather than an interpolation's. One implementation serves every tier.
// ======================================================================================

/// One crossing of a section: when, and the state there.
type SectionPoint<'s> = { Time: float<s>; State: 's }

/// Poincaré sections of a trajectory: stroboscopic, at every multiple of a period, and spatial,
/// where a surface is crossed.
[<RequireQualifiedAccess>]
module Poincare =

    /// The state at every `k · period` the trajectory reaches, from `t = 0`: each one exact, by a
    /// seek.
    let stroboscopic (period: float<s>) (trajectory: Anim<'s>) : SectionPoint<'s>[] =
        if not (period > 0.0<s>) || Double.IsInfinity(float period) then
            invalidArg "period" (sprintf "Marey: a period must be positive and finite, not %g s." (float period))

        let d = trajectory.Duration
        let mutable n = int (floor (d / period))

        while float (n + 1) * period <= d do
            n <- n + 1

        while n > 0 && float n * period > d do
            n <- n - 1

        Array.init (n + 1) (fun k ->
            let t = float k * period
            { Time = t; State = trajectory.At t })

    /// Every crossing of `surface` in `direction` by a straight run to `horizon`: the state on the
    /// surface, to its event's tolerance, with the integrator's order of error. The simulation's
    /// own events go on firing around it.
    let section<'s, [<Measure>] 'g>
        (direction: Direction)
        (surface: 's -> float<'g>)
        (horizon: float<s>)
        (sim: Sim<'s>)
        : SectionPoint<'s>[] =
        let name = "Poincaré section"
        let crossing = Event.create name direction (Response.Continue id) surface

        Sim.hitsWithStates horizon (sim |> Sim.withEvents [ crossing ])
        |> List.filter (fun (hit, _) -> hit.Name = name && hit.Kind = HitKind.Fired)
        |> List.map (fun (hit, s) -> { Time = hit.Time; State = s })
        |> Array.ofList
