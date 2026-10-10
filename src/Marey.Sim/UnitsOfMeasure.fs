namespace Marey.UnitsOfMeasure

open Marey

/// D109 — `Marey.Sim` in the unit-required mode: an invariant takes a witness, checked against
/// the unit its function returns. Everything else in `Sim` is the core's in both modes, because
/// nothing else takes a unit.
[<RequireQualifiedAccess>]
module Sim =

    /// D136 — the simulation with an invariant attached: `f` gives it in `'e`, and the drift
    /// figure labels its axis in `u`.
    let withInvariant<'s, [<Measure>] 'e> (name: string) (u: U<'e>) (f: 's -> float<'e>) (sim: Sim<'s>) : Sim<'s> =
        Marey.Sim.withInvariantIn name u f sim
