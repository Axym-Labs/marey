/// Uncertainty propagation under JavaScript: the package compiles with Fable, and Node gets the
/// numbers .NET gets for a pendulum, a shared input, correlated inputs and a coverage factor.
module Marey.Fable.UncertaintyGate

open System
open Marey

/// The failures, by description; empty when the gate holds.
let check () : string list =
    let near (name: string) (tolerance: float) (expected: float) (actual: float) =
        if abs (expected - actual) <= tolerance then [] else [ sprintf "%s: expected %g, got %g" name expected actual ]

    let l = Uncertain.standard "Length" U.metre 1.000<m> 0.002<m> |> Uncertain.withDof 4.0
    let t = Uncertain.standard "Period" U.second 2.006<s> 0.004<s> |> Uncertain.withDof 9.0
    let g = 4.0 * Math.PI ** 2.0 * l / (t * t)
    let value = 4.0 * Math.PI ** 2.0 / (2.006 * 2.006)
    let relative = sqrt (0.002 ** 2.0 + (2.0 * 0.004 / 2.006) ** 2.0)

    let dof =
        match Uncertain.dof g with
        | EffectiveDof.Known nu -> nu
        | EffectiveDof.Undefined _ -> nan

    let a = 1.0<m> +- 0.3<m>
    let b = 1.0<m> +- 0.4<m>
    let independent = float (a + b).Uncertainty
    Uncertain.correlate 1.0 a b

    [ yield! near "g" 1e-12 value (float g.Value)
      yield! near "u(g)" 1e-12 (value * relative) (float g.Uncertainty)
      yield! near "ν_eff(g)" 1e-9 (relative ** 4.0 / (0.002 ** 4.0 / 4.0 + (2.0 * 0.004 / 2.006) ** 4.0 / 9.0)) dof
      yield! near "u(l − l)" 0.0 0.0 (float (l - l).Uncertainty)
      yield! near "u(sqrt (l · l))" 1e-15 0.002 (float (sqrt (l * l)).Uncertainty)
      yield! near "u(sin (t / t))" 0.0 0.0 (float (sin (t / t)).Uncertainty)
      yield! near "independent sum" 1e-15 0.5 independent
      yield! near "correlated sum" 1e-15 0.7 (float (a + b).Uncertainty)
      yield! near "t₉₉(16)" 5e-5 2.9208 (Coverage.studentT 0.99 16.0)
      yield! near "k(0.95)" 5e-7 1.959964 (Coverage.normal 0.95)
      if (sqrt (l * l)).Unit.Symbol <> "m" then
          yield "the witness of sqrt (l · l) is not the metre" ]
