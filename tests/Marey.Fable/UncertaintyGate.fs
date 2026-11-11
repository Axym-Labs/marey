/// Uncertainty propagation under JavaScript: the package compiles with Fable, and Node gets the
/// numbers .NET gets for a pendulum, a shared input, correlated inputs and a coverage factor.
module Marey.Fable.UncertaintyGate

open System
open Marey

/// The reports and a budget, character for character as .NET writes them.
let private reports () : string list =
    let same (name: string) (expected: string) (actual: string) =
        if expected = actual then [] else [ sprintf "%s: expected «%s», got «%s»" name expected actual ]

    let plain (value: float) (u: float) = Uncertain.standard "" U.one value u
    let plusMinus = { Uncertain.reportFormat with Form = ReportForm.PlusMinus }
    let gram: U<kg> = U.scaled "g" 1e-3 U.kilogram
    let mass = Uncertain.standard "m_S" gram 100.02147<kg> 0.00035<kg> |> Uncertain.withDof 9.0

    let g =
        let l = Uncertain.resolution "Pendulum length" U.metre 1.000<m> 1e-3<m>
        let t = Uncertain.ofReadings "Period" U.second [ 2.01<s>; 2.00<s>; 2.01<s> ]
        let calibration = Uncertain.certificate "Stopwatch calibration" U.second 0.0<s> 0.01<s> 2.0
        4.0 * Math.PI ** 2.0 * l / ((t + calibration) * (t + calibration))

    let budget =
        [ "Input                  Value            ν     Type  Contribution  Share"
          "Stopwatch calibration  0.0000(50) s     ∞     B     0.049 m·s⁻²   69.1 %"
          "Period                 2.0067(33) s     2     A     0.033 m·s⁻²   30.7 %"
          "Pendulum length        1.00000(29) m    ∞     B     0.0028 m·s⁻²  0.2 %"
          "Combined               9.804(59) m·s⁻²  21.2        0.059 m·s⁻²" ]
        |> String.concat "\n"

    [ yield! same "concise" "100.02147(35) g" (Uncertain.report mass)
      yield! same "ToString" "100.02147(35) g" (string mass)
      yield! same "plus-minus" "(100.02147 ± 0.00035) g" (Uncertain.reportWith plusMinus mass)
      yield! same "expanded" "(100.02147 ± 0.00079) g (k = 2.26, 95 %, ν = 9)" (Uncertain.reportExpanded (Uncertain.expanded 0.95 mass))
      yield! same "a place moved" "5.00(10)" (Uncertain.report (plain 5.0 0.0996))
      yield! same "across the point" "12.3(1.2)" (Uncertain.report (plain 12.34 1.23))
      yield! same "past the point" "1230(120)" (Uncertain.report (plain 1234.0 120.0))
      yield! same "negative zero" "0.000(40)" (Uncertain.report (plain -0.0001 0.04))
      yield! same "small" "(6.62607 ± 0.00012) × 10⁻³⁴" (Uncertain.reportWith plusMinus (plain 6.62607e-34 1.2e-38))
      yield! same "large" "1.234568(43) × 10⁸" (Uncertain.report (plain 123456789.0 4321.0))
      yield! same "exact" "6.02214076 × 10²³" (Uncertain.report (Uncertain.exact U.one 6.02214076e23))
      yield! same "siunitx" "\\qty{9.804(59)}{\\metre\\per\\second\\squared}" (Uncertain.reportWith { Uncertain.reportFormat with Style = Siunitx } g)
      yield! same "budget" budget (Budget.text (Uncertain.budget g)) ]

/// GUM H.3's thermometer line, from the scatter of eleven exact points, as .NET fits it.
let private fit () : string list =
    let near (name: string) (tolerance: float) (expected: float) (actual: float) =
        if abs (expected - actual) <= tolerance then [] else [ sprintf "%s: expected %.12g, got %.12g" name expected actual ]

    let t = [| 21.521; 22.012; 22.512; 23.003; 23.507; 23.999; 24.513; 25.002; 25.503; 26.010; 26.511 |]
    let b = [| -0.171; -0.169; -0.166; -0.159; -0.164; -0.165; -0.156; -0.157; -0.159; -0.161; -0.160 |]
    let readings = U.kelvin.Of(t |> Array.map (fun c -> (c + 273.15) * 1.0<K>))
    let corrections = U.kelvin.Of(b |> Array.map (fun c -> c * 1.0<K>))
    let line = Fit.lineAbout 293.15<K> (Uncertain.exactly readings) (Uncertain.exactly corrections)
    let at30 = line.At 303.15<K>

    let dof =
        match Uncertain.dof at30 with
        | EffectiveDof.Known nu -> nu
        | EffectiveDof.Undefined _ -> nan

    [ yield! near "y₁" 5e-5 -0.1712 (float line.Intercept.Value)
      yield! near "u(y₁)" 5e-5 0.0029 (float line.Intercept.Uncertainty)
      yield! near "y₂" 5e-6 0.00218 (float line.Slope.Value)
      yield! near "u(y₂)" 5e-6 0.00067 (float line.Slope.Uncertainty)
      yield! near "r(y₁, y₂)" 5e-4 -0.930 (Uncertain.correlation line.Intercept line.Slope)
      yield! near "b(30 °C)" 5e-5 -0.1494 (float at30.Value)
      yield! near "u(b(30 °C))" 5e-5 0.0041 (float at30.Uncertainty)
      yield! near "ν(b(30 °C))" 1e-9 9.0 dof ]

/// Monte Carlo checks drawn under JavaScript as .NET draws them: the generator, every shape of
/// input, the t's gamma draw and the correlated normals give the same values to the last bits a
/// sum and a sort can move.
let private monteCarlo () : string list =
    let near (name: string) (expected: float) (actual: float) =
        if abs (expected - actual) <= 1e-12 * max 1.0 (abs expected) then []
        else [ sprintf "%s: expected %.17g, got %.17g" name expected actual ]

    let fixedAt n = { MonteCarlo.defaults with Trials = n; Adaptive = false }

    let same (name: string) (e: float, u: float, lo: float, hi: float) (c: MonteCarloCheck<'u>) =
        near (name + " y") e (float c.Estimate)
        @ near (name + " u") u (float c.Uncertainty)
        @ near (name + " low") lo (float c.Low)
        @ near (name + " high") hi (float c.High)

    let x i (u: float) = Uncertain.rectangular (sprintf "X%d" i) U.one 0.0 (u * sqrt 3.0)

    let rectangular =
        MonteCarlo.model4 (fun a b c d -> a + b + c + d) (x 1 1.0) (x 2 1.0) (x 3 1.0) (x 4 10.0)
        |> MonteCarlo.checkWith (fixedAt 20_000)

    let t = Uncertain.ofReadings "t" U.second [ 2.01<s>; 2.00<s>; 2.01<s>; 2.02<s>; 2.00<s> ]
    let w = Uncertain.triangular "w" U.metre 1.0<m> 0.01<m>
    let studentT = MonteCarlo.model2 (fun w t -> w / (t * t)) w t |> MonteCarlo.checkWith (fixedAt 20_000)

    let a = Uncertain.standard "a" U.metre 1.0<m> 0.3<m>
    let b = Uncertain.arcsine "b" U.metre 1.0<m> 0.4<m>
    let c = Uncertain.standard "c" U.metre 2.0<m> 0.2<m>
    Uncertain.correlate 0.6 a c
    let correlated = MonteCarlo.model3 (fun a b c -> a * b - c * c) a b c |> MonteCarlo.checkWith (fixedAt 20_000)

    same "rectangular" (-0.12578658620837571, 10.179455013745223, -17.025326882022306, 17.019787020572608) rectangular
    @ same "t" (0.24799689553979687, 0.0016835995572727185, 0.24477024720775697, 0.25120515024241197) studentT
    @ same "correlated" (-3.0477660979609884, 0.72985212303251135, -4.5768076758004863, -1.7108083024589278) correlated

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
          yield "the witness of sqrt (l · l) is not the metre"
      yield! reports ()
      yield! fit ()
      yield! monteCarlo () ]
