/// The Monte Carlo check (JCGM 101:2008, Supplement 1 to the GUM), against the Supplement's
/// additive model (9.2) within its stated tolerance, against what each input distribution must
/// be, and against models the law of propagation is known to get wrong.
module Marey.Tests.UncertaintyMonteCarloTests

open System
open Xunit
open Marey

let private near (tolerance: float) (expected: float) (actual: float) =
    Assert.True(abs (expected - actual) <= tolerance, sprintf "expected %g within %g, got %.12g" expected tolerance actual)

let private sum4 (a: Uncertain<1>) b c d = a + b + c + d

let private additive (xs: Uncertain<1>[]) (options: MonteCarloOptions) =
    MonteCarlo.model4 sum4 xs.[0] xs.[1] xs.[2] xs.[3] |> MonteCarlo.checkWith options

/// The distribution of the one input a value was made as.
let private shapeOf (x: Uncertain<'u>) = (fst (Uncertain.contributions x).[0]).Distribution

/// Exactly this many trials.
let private fixedAt (trials: int) = { MonteCarlo.defaults with Trials = trials; Adaptive = false }

let private gaussian i = Uncertain.standard (sprintf "X%d" i) U.one 0.0 1.0

/// Rectangular with expectation zero and standard deviation `u`.
let private rectangular i (u: float) = Uncertain.rectangular (sprintf "X%d" i) U.one 0.0 (u * sqrt 3.0)

// ======================================================================================
// JCGM 101, 9.2: the additive model Y = X₁ + X₂ + X₃ + X₄
// ======================================================================================

[<Fact>]
let ``JCGM 101 table 2, normal inputs: the law of propagation is validated`` () =
    let c = additive [| for i in 1..4 -> gaussian i |] MonteCarlo.defaults

    // The table's analytical row: y = 0.00, u(y) = 2.00, [−3.92, 3.92]; δ = 0.05 (9.2.2.7).
    near 1e-15 0.05 (float c.Tolerance)
    near 0.05 0.0 (float c.Estimate)
    near 0.05 2.0 (float c.Uncertainty)
    near 0.05 -3.92 (float c.Low)
    near 0.05 3.92 (float c.High)
    near 1e-12 (1.959964 * 2.0) (float c.LinearInterval.Expanded |> fun e -> Math.Round(e, 6))
    Assert.True(c.Validated)
    Assert.True(c.Settled)
    Assert.Equal(c.Trials, c.Values.Length)

[<Fact>]
let ``JCGM 101 table 3, rectangular inputs: near the analytical interval and the tolerance`` () =
    // Analytically ±2√3 (2 − (3/5)^¼) ≈ ±3.88 (9.2.3.2). The ends are close enough to δ that the
    // check runs until they are known to δ/5, as 8.2 asks for a validation, some 10⁶ trials as in
    // the table; they then lie within δ/5 of the analytical ones, and the linear interval's ends
    // 0.04 away, just inside δ = 0.05, as the table's first adaptive run found.
    let exact = 2.0 * sqrt 3.0 * (2.0 - (3.0 / 5.0) ** 0.25)
    let c = additive [| for i in 1..4 -> rectangular i 1.0 |] MonteCarlo.defaults
    Assert.True(c.Settled)
    Assert.True(c.Trials >= 300_000, sprintf "%d trials" c.Trials)
    near 0.01 -exact (float c.Low)
    near 0.01 exact (float c.High)
    near 0.01 2.0 (float c.Uncertainty)
    near 0.015 0.04 (float c.LowDifference)
    near 0.015 0.04 (float c.HighDifference)

[<Fact>]
let ``JCGM 101 table 4, one wide rectangular input: not validated`` () =
    let c = additive [| rectangular 1 1.0; rectangular 2 1.0; rectangular 3 1.0; rectangular 4 10.0 |] MonteCarlo.defaults

    // The table: GUF u = 10.1, [−19.9, 19.9]; MCM u = 10.2, [−17.0, 17.0]; d = 2.8 to 2.9 against
    // δ = 0.5, so the law of propagation fails here.
    near 1e-15 0.5 (float c.Tolerance)
    near 0.05 10.1 (float c.Linear.Uncertainty)
    near 0.05 19.9 (float c.LinearInterval.Expanded)
    near 0.1 10.2 (float c.Uncertainty)
    near 0.5 -17.0 (float c.Low)
    near 0.5 17.0 (float c.High)
    near 0.2 2.85 (float c.LowDifference)
    near 0.2 2.85 (float c.HighDifference)
    Assert.False(c.Validated)
    // Ends this far from the linear ones settle the comparison in a few batches.
    Assert.True(c.Settled)
    Assert.True(c.Trials <= 50_000, sprintf "%d trials" c.Trials)

[<Fact>]
let ``the report says what was compared and how it came out`` () =
    let c = additive [| for i in 1..4 -> gaussian i |] MonteCarlo.defaults
    let lines = (MonteCarlo.report c).Split '\n'
    Assert.Equal(3, lines.Length)
    Assert.StartsWith(sprintf "Monte Carlo, %d trials: y = 0.00, u = 2.00, 95 %% interval [−3.9" c.Trials, lines.[0])
    Assert.Equal("Law of propagation: y = 0.00, u = 2.00, 95 % interval [−3.92, 3.92] (k = 1.96)", lines.[1])
    Assert.EndsWith("within δ = 0.05: the law of propagation holds here.", lines.[2])

// ======================================================================================
// Models the law of propagation gets wrong
// ======================================================================================

[<Fact>]
let ``a ratio with a denominator near zero is flagged`` () =
    // b is 2.5 standard uncertainties from zero: a/b has a long right tail, and its first-order
    // interval misses it.
    let a = Uncertain.standard "a" U.metre 1.0<m> 0.1<m>
    let b = Uncertain.standard "b" U.second 0.5<s> 0.2<s>
    let c = MonteCarlo.model2 (fun a b -> a / b) a b |> MonteCarlo.check
    let speed: MonteCarloCheck<m / s> = c
    Assert.False(speed.Validated)
    Assert.True(float c.High - float c.Linear.Value > 2.0 * (float c.Linear.Value - float c.Low))
    Assert.Contains("does not hold here", MonteCarlo.report c)

[<Fact>]
let ``a model nearly linear over its inputs' spread is validated`` () =
    let l = Uncertain.standard "Length" U.metre 1.000<m> 0.002<m>
    let t = Uncertain.standard "Period" U.second 2.006<s> 0.004<s>
    let m = MonteCarlo.model2 (fun l t -> 4.0 * Math.PI ** 2.0 * l / (t * t)) l t
    // To u's first digit the pendulum's g is linear enough.
    let c = m |> MonteCarlo.checkWith { MonteCarlo.defaults with Digits = 1 }
    let g: MonteCarloCheck<m / s^2> = c
    Assert.True(g.Validated)
    Assert.True(g.Settled)
    Assert.Contains("m·s⁻²", MonteCarlo.report c)

[<Fact>]
let ``a check that runs out of trials says it cannot tell`` () =
    // At two digits the pendulum's ends lie about δ from the linear ones (the curvature of 1/T²),
    // which takes millions of trials to settle; 2 × 10⁴ cannot.
    let l = Uncertain.standard "Length" U.metre 1.000<m> 0.002<m>
    let t = Uncertain.standard "Period" U.second 2.006<s> 0.004<s>
    let m = MonteCarlo.model2 (fun l t -> 4.0 * Math.PI ** 2.0 * l / (t * t)) l t
    let c = m |> MonteCarlo.checkWith { MonteCarlo.defaults with Trials = 20_000 }
    Assert.Equal(20_000, c.Trials)
    Assert.False(c.Settled)
    Assert.Contains("more trials would tell", MonteCarlo.report c)

[<Fact>]
let ``a square near its minimum is flagged`` () =
    // y = x² at x = 0.1 ± 1: the derivative 2x nearly vanishes, so the linear u is a fifth of the
    // spread the square really has.
    let x = Uncertain.standard "x" U.one 0.1 1.0
    let c = MonteCarlo.model (fun x -> x * x) x |> MonteCarlo.check
    Assert.False(c.Validated)
    near 0.02 0.2 (float c.Linear.Uncertainty)
    near 0.05 (sqrt (2.0 + 4.0 * 0.01)) (float c.Uncertainty)
    Assert.True(float c.Low >= 0.0)

// ======================================================================================
// Inputs as the Supplement draws them
// ======================================================================================

let private standardised (x: Uncertain<1>) (trials: int) =
    let c = MonteCarlo.model id x |> MonteCarlo.checkWith (fixedAt trials)
    c.Values |> Array.map float

[<Fact>]
let ``each distribution has the stated standard deviation and its own shape`` () =
    let moments (v: float[]) =
        let m = Array.average v
        let var = Array.averageBy (fun x -> (x - m) ** 2.0) v
        m, sqrt var, Array.averageBy (fun x -> (x - m) ** 4.0) v / (var * var)

    // Kurtosis: normal 3, rectangular 1.8, triangular 2.4, arcsine 1.5.
    for x, kurtosis, bound in
        [ Uncertain.standard "n" U.one 0.0 1.0, 3.0, infinity
          Uncertain.rectangular "r" U.one 0.0 (sqrt 3.0), 1.8, sqrt 3.0
          Uncertain.triangular "t" U.one 0.0 (sqrt 6.0), 2.4, sqrt 6.0
          Uncertain.arcsine "a" U.one 0.0 (sqrt 2.0), 1.5, sqrt 2.0 ] do
        let v = standardised x 200_000
        let mean, sd, k = moments v
        near 0.01 0.0 mean
        near 0.01 1.0 sd
        near 0.05 kurtosis k
        Assert.True(Array.forall (fun y -> abs y <= bound) v)

    Assert.Equal(Distribution.Rectangular, shapeOf (Uncertain.resolution "step" U.one 0.0 1.0))
    Assert.Equal(Distribution.Normal, shapeOf (Uncertain.certificate "c" U.one 0.0 1.0 2.0))
    Assert.Equal(Distribution.Arcsine, shapeOf (Uncertain.arcsine "a" U.one 0.0 1.0))

[<Fact>]
let ``repeated readings are drawn from Student's t, wider than their s over root n`` () =
    // Ten readings: ν = 9, and the scaled t has variance u² ν/(ν − 2) (JCGM 101, 6.4.9.4).
    let x = Uncertain.ofReadings "x" U.one [ 1.0; 1.2; 0.9; 1.1; 1.0; 0.8; 1.05; 1.15; 0.95; 1.0 ]
    Assert.Equal(Distribution.StudentT, shapeOf x)
    let c = MonteCarlo.model id x |> MonteCarlo.checkWith (fixedAt 400_000)
    near (0.01 * float x.Uncertainty) (float x.Uncertainty * sqrt (9.0 / 7.0)) (float c.Uncertainty)
    // Its 95 % interval is t₉'s, which is what the law of propagation also uses here.
    near (0.01 * float x.Uncertainty) (Coverage.studentT 0.95 9.0 * float x.Uncertainty) (float c.High - float x.Value)
    Assert.True(c.Validated)

[<Fact>]
let ``the points of a fit share one draw of their scatter`` () =
    // H.3's thermometer: every point's uncertainty is the one s with ν = 9, so the correction at
    // 30 °C is a scaled t₉, and its 95 % interval t₉'s, 2.26 u. Points drawn independently would
    // give a nearly normal sum, near 1.96 √(9/7) u = 2.22 u.
    let t = [| 21.521; 22.012; 22.512; 23.003; 23.507; 23.999; 24.513; 25.002; 25.503; 26.010; 26.511 |]
    let b = [| -0.171; -0.169; -0.166; -0.159; -0.164; -0.165; -0.156; -0.157; -0.159; -0.161; -0.160 |]
    let readings = U.kelvin.Of(t |> Array.map (fun c -> (c + 273.15) * 1.0<K>))
    let corrections = U.kelvin.Of(b |> Array.map (fun c -> c * 1.0<K>))
    let fit = Fit.lineAbout 293.15<K> (Uncertain.exactly readings) (Uncertain.exactly corrections)
    let at30 = fit.At 303.15<K>
    let c = MonteCarlo.model id at30 |> MonteCarlo.checkWith (fixedAt 400_000)
    let u = float at30.Uncertainty
    near (0.01 * u) (Coverage.studentT 0.95 9.0 * u) (0.5 * (float c.High - float c.Low))
    Assert.True(c.Validated)

[<Fact>]
let ``inputs declared correlated are drawn together`` () =
    let a = Uncertain.standard "a" U.metre 1.0<m> 0.3<m>
    let b = Uncertain.standard "b" U.metre 1.0<m> 0.4<m>
    Uncertain.correlate 0.8 a b
    let c = MonteCarlo.model2 (fun a b -> a - b) a b |> MonteCarlo.checkWith (fixedAt 200_000)
    // u² = 0.09 + 0.16 − 2 · 0.8 · 0.12.
    let u = sqrt (0.09 + 0.16 - 2.0 * 0.8 * 0.12)
    near 1e-12 u (float c.Linear.Uncertainty)
    near (0.01 * u) u (float c.Uncertainty)
    // No effective degrees of freedom with correlated inputs, so the linear interval takes the
    // normal factor and says so.
    Assert.True(Double.IsPositiveInfinity c.LinearInterval.Dof)
    Assert.True(c.Validated)

[<Fact>]
let ``a shared input cancels in every trial`` () =
    let calibration = Uncertain.rectangular "Calibration" U.metre 0.0<m> 0.01<m>
    let a = Uncertain.standard "a" U.metre 1.2<m> 0.003<m> + calibration
    let b = Uncertain.standard "b" U.metre 0.7<m> 0.004<m> + calibration
    let c = MonteCarlo.model2 (fun a b -> a - b) a b |> MonteCarlo.check
    near 1e-4 0.005 (float c.Uncertainty)

    let same = MonteCarlo.model2 (fun a b -> a - b) a a |> MonteCarlo.check
    Assert.True(same.Values |> Array.forall (fun v -> v = 0.0<m>))

// ======================================================================================
// Reproducible, and what it refuses
// ======================================================================================

[<Fact>]
let ``a seed gives the same values every time, and another seed others`` () =
    let x = Uncertain.triangular "x" U.one 2.0 0.5
    let run seed = (MonteCarlo.model (fun x -> exp x) x |> MonteCarlo.checkWith { MonteCarlo.defaults with Trials = 1000; Seed = seed }).Values
    Assert.Equal<float[]>(run 7, run 7)
    Assert.NotEqual<float[]>(run 7, run 8)

[<Fact>]
let ``an uncertain value the model reaches without being given it is refused`` () =
    let l = Uncertain.standard "Length" U.metre 1.0<m> 0.002<m>
    let calibration = Uncertain.standard "Calibration" U.metre 0.0<m> 0.001<m>
    let e = Assert.Throws<ArgumentException>(fun () -> MonteCarlo.model (fun l -> l + calibration) l |> MonteCarlo.check |> ignore)
    Assert.Contains("Calibration", e.Message)

[<Fact>]
let ``correlated inputs of another shape, and impossible options, are refused`` () =
    let a = Uncertain.rectangular "a" U.one 0.0 1.0
    let b = Uncertain.standard "b" U.one 0.0 1.0
    Uncertain.correlate 0.5 a b
    let e = Assert.Throws<ArgumentException>(fun () -> MonteCarlo.model2 (fun a b -> a + b) a b |> MonteCarlo.check |> ignore)
    Assert.Contains("jointly normal", e.Message)

    let x = Uncertain.standard "x" U.one 0.0 1.0
    let m = MonteCarlo.model id x
    Assert.Throws<ArgumentException>(fun () -> MonteCarlo.checkWith { MonteCarlo.defaults with Trials = 1 } m |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> MonteCarlo.checkWith { MonteCarlo.defaults with Coverage = 1.0 } m |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> MonteCarlo.checkWith { MonteCarlo.defaults with Digits = 0 } m |> ignore) |> ignore

[<Theory>]
// JCGM 101, 7.9.2 and its examples: u = 2.0 → 0.05, u = 10.1 → 0.5, 0.000 35 g → 0.000 005 g;
// and 0.0996, which two digits write 0.10.
[<InlineData(2.0, 2, 0.05)>]
[<InlineData(10.1, 2, 0.5)>]
[<InlineData(0.00035, 2, 0.000005)>]
[<InlineData(0.0996, 2, 0.005)>]
[<InlineData(0.08, 1, 0.005)>]
let ``the tolerance is half a unit in the last stated digit`` (u: float, digits: int, expected: float) =
    let x = Uncertain.standard "x" U.one 1.0 u
    let c = MonteCarlo.model id x |> MonteCarlo.checkWith { MonteCarlo.defaults with Trials = 100; Digits = digits }
    near (expected * 1e-9) expected (float c.Tolerance)

// ======================================================================================
// The figure
// ======================================================================================

[<Fact>]
let ``the figure draws the values, the linear distribution and both intervals`` () =
    let fig = UncertaintyScenes.monteCarlo ()
    let layers = fig.Panels.[0].Layers
    Assert.Equal(4, layers.Length)
    Assert.Equal<string[]>([| "Monte Carlo, 20000 trials"; "law of propagation" |], layers |> Array.filter (fun l -> l.ShowInLegend) |> Array.map (fun l -> l.Name))
    // The linear interval's ends are dashed, the Monte Carlo ones not.
    Assert.Empty(layers.[1].Style.Dash)
    Assert.NotEmpty(layers.[3].Style.Dash)
