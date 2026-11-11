/// Uncertainty propagation, checked against the GUM's own worked examples (JCGM 100:2008,
/// annex H) to the digits the text prints, and against what the model promises: correlation
/// through a shared input is exact, and the measure and the witness follow the arithmetic.
module Marey.Tests.UncertaintyTests

open System
open Xunit
open Marey

let private close (digits: int) (expected: float) (actual: float) =
    Assert.Equal(expected, Math.Round(actual, digits), digits + 2)

let private near (tolerance: float) (expected: float) (actual: float) =
    Assert.True(abs (expected - actual) <= tolerance, sprintf "expected %g within %g, got %.12g" expected tolerance actual)

// ======================================================================================
// Coverage factors
// ======================================================================================

[<Theory>]
// GUM table G.2.
[<InlineData(0.95, 1.0, 12.71)>]
[<InlineData(0.95, 2.0, 4.30)>]
[<InlineData(0.95, 10.0, 2.23)>]
[<InlineData(0.9545, 10.0, 2.28)>]
[<InlineData(0.99, 16.0, 2.92)>]
[<InlineData(0.99, 5.0, 4.03)>]
[<InlineData(0.9973, 3.0, 9.22)>]
[<InlineData(0.6827, 4.0, 1.14)>]
[<InlineData(0.95, 100.0, 1.984)>]
let ``Student's t factors are those of GUM table G.2`` (p: float, dof: float, expected: float) =
    let digits = if expected < 2.0 && dof > 50.0 then 3 else 2
    close digits expected (Coverage.studentT p dof)

[<Fact>]
let ``the normal factors are the familiar ones`` () =
    close 4 1.9600 (Coverage.normal 0.95)
    close 4 2.5758 (Coverage.normal 0.99)
    close 3 1.000 (Coverage.normal 0.682689492)
    close 3 3.000 (Coverage.normal 0.997300204)
    close 4 1.9600 (Coverage.studentT 0.95 Double.PositiveInfinity)

[<Fact>]
let ``Student's t approaches the normal factor`` () =
    near 1e-4 (Coverage.normal 0.95) (Coverage.studentT 0.95 1e6)

// ======================================================================================
// The model
// ======================================================================================

[<Fact>]
let ``a pendulum's g carries its unit and both inputs`` () =
    let l = Uncertain.standard "Length" U.metre 1.000<m> 0.002<m>
    let t = Uncertain.standard "Period" U.second 2.006<s> 0.004<s>
    let g: Uncertain<m / s^2> = 4.0 * Math.PI ** 2.0 * l / (t * t)

    close 3 9.811 (float g.Value)
    // By hand: u_rel = √(0.002² + (2 · 0.004 / 2.006)²) = 0.00446.
    close 3 0.044 (float g.Uncertainty)
    Assert.Equal<(string * int) list>([ "m", 1; "s", -2 ], g.Unit.Factors)
    Assert.Equal<string[]>([| "Length"; "Period" |], Uncertain.contributions g |> Array.map (fun (i, _) -> i.Label))

    let shares = Uncertain.contributions g |> Array.map (fun (_, c) -> float (c * c) / float (g.Uncertainty * g.Uncertainty))
    near 1e-12 1.0 (Array.sum shares)

[<Fact>]
let ``a value minus itself is zero with no uncertainty`` () =
    let l = Uncertain.standard "Length" U.metre 1.000<m> 0.002<m>
    let d = l - l
    Assert.Equal(0.0<m>, d.Value)
    Assert.Equal(0.0<m>, d.Uncertainty)

    let ratio = l / l
    Assert.Equal(1.0, float ratio.Value)
    Assert.Equal(0.0, float ratio.Uncertainty)
    Assert.Equal(0.0, float (sin ratio).Uncertainty)

[<Fact>]
let ``a shared calibration cancels in a difference and not in a sum`` () =
    let calibration = Uncertain.standard "Calibration" U.metre 0.0<m> 0.010<m>
    let a = Uncertain.standard "Reading a" U.metre 1.200<m> 0.003<m> + calibration
    let b = Uncertain.standard "Reading b" U.metre 0.700<m> 0.004<m> + calibration

    near 1e-15 0.005 (float (a - b).Uncertainty)
    near 1e-15 (sqrt (0.003 ** 2.0 + 0.004 ** 2.0 + 0.020 ** 2.0)) (float (a + b).Uncertainty)
    near 1e-12 (0.010 ** 2.0) (float (Uncertain.covariance a b))

[<Fact>]
let ``independent inputs that look alike do not cancel`` () =
    let a = 1.0<m> +- 0.3<m>
    let b = 1.0<m> +- 0.4<m>
    near 1e-15 0.5 (float (a - b).Uncertainty)
    Assert.Equal(0.0, Uncertain.correlation a b)
    Assert.False(a.Unit.Stated)

[<Fact>]
let ``the square root halves the measure and the witness`` () =
    let l = Uncertain.standard "Length" U.metre 1.000<m> 0.002<m>
    let r: Uncertain<m> = sqrt (l * l)
    near 1e-15 1.0 (float r.Value)
    near 1e-15 0.002 (float r.Uncertainty)
    Assert.Equal("m", r.Unit.Symbol)
    Assert.Equal(Dim.length, r.Unit.Dim)

    // A period from a length and an acceleration: √(m / (m/s²)) is seconds.
    let g = Uncertain.standard "g" (U.div U.metre (U.square U.second)) 9.81<m / s^2> 0.01<m / s^2>
    let t: Uncertain<s> = 2.0 * Math.PI * sqrt (l / g)
    Assert.Equal<(string * int) list>([ "s", 1 ], t.Unit.Factors)
    Assert.Equal(Dim.time, t.Unit.Dim)

    // An odd power has no root to name, and the result says nothing in place of something wrong.
    let odd: Uncertain<m> = sqrt (Uncertain.standard "Area" (U<m^2>("ha", Dim.pow 2 Dim.length, 1e4)) 4.0<m^2> 0.1<m^2>)
    Assert.False(odd.Unit.Stated)

[<Fact>]
let ``a sum reads the right operand in the left one's unit`` () =
    let kilometre: U<m> = U.scaled "km" 1000.0 U.metre
    let a = Uncertain.standard "a" U.metre 500.0<m> 3.0<m>
    let b = Uncertain.standard "b" kilometre 2.0<m> 0.004<m>
    let sum = a + b
    near 1e-9 2500.0 (float sum.Value)
    near 1e-12 5.0 (float sum.Uncertainty)
    Assert.Equal("m", sum.Unit.Symbol)
    near 1e-9 -1500.0 (float (a - b).Value)

[<Fact>]
let ``arithmetic with numbers and quantities`` () =
    let l = Uncertain.standard "Length" U.metre 2.0<m> 0.1<m>
    let width = U.metre.Of 3.0<m>
    let area: Uncertain<m^2> = l * width
    near 1e-12 6.0 (float area.Value)
    near 1e-12 0.3 (float area.Uncertainty)
    near 1e-12 0.3 (float (width * l).Uncertainty)
    near 1e-12 (0.1 / 3.0) (float (l / width).Uncertainty)
    near 1e-12 (3.0 * 0.1 / 4.0) (float (width / l).Uncertainty)
    near 1e-12 0.1 (float (l + 1.0<m>).Uncertainty)
    near 1e-12 -1.0 (float (1.0<m> - l).Value)
    near 1e-12 0.1 (float (l + width).Uncertainty)
    near 1e-12 0.025 (float (1.0 / l).Uncertainty)
    near 1e-12 0.05 (float (l / 2.0).Uncertainty)
    near 1e-12 0.1 (float (abs (-l)).Uncertainty)
    Assert.Equal(0.0<m>, (Uncertain.exact U.metre 2.0<m>).Uncertainty)

/// Each function's contribution is its derivative, checked against a central difference.
[<Fact>]
let ``the functions propagate their derivative`` () =
    let x0 = 0.37
    let u = 1e-3
    let x = Uncertain.standard "x" U.one x0 u

    let check (name: string) (f: float -> float) (y: Uncertain<1>) =
        let h = 1e-6
        let slope = (f (x0 + h) - f (x0 - h)) / (2.0 * h)
        Assert.True(abs (f x0 - y.Value) < 1e-14, name)
        Assert.True(abs (abs slope * u - y.Uncertainty) < 1e-11, sprintf "%s: %g against %g" name (abs slope * u) y.Uncertainty)

    check "exp" exp (exp x)
    check "log" log (log x)
    check "log10" log10 (log10 x)
    check "sin" sin (sin x)
    check "cos" cos (cos x)
    check "tan" tan (tan x)
    check "asin" asin (asin x)
    check "acos" acos (acos x)
    check "atan" atan (atan x)
    check "sinh" sinh (sinh x)
    check "cosh" cosh (cosh x)
    check "tanh" tanh (tanh x)
    check "sqrt" sqrt (sqrt x)
    check "pow" (fun v -> v ** 2.5) (x ** 2.5)
    check "pow of itself" (fun v -> v ** v) (x ** x)

    let angle = Uncertain.standard "φ" U.radian (x0 * 1.0<rad>) (u * 1.0<rad>)
    check "sin of radians" sin (Uncertain.sin angle)
    check "cos of radians" cos (Uncertain.cos angle)
    check "tan of radians" tan (Uncertain.tan angle)

[<Fact>]
let ``what is not an uncertainty or an input is refused`` () =
    Assert.Throws<ArgumentException>(fun () -> Uncertain.standard "x" U.metre 1.0<m> -0.1<m> |> ignore) |> ignore
    let a = Uncertain.standard "a" U.metre 1.0<m> 0.1<m>
    let b = Uncertain.standard "b" U.metre 1.0<m> 0.1<m>
    Assert.Throws<ArgumentException>(fun () -> Uncertain.correlate 1.5 a b) |> ignore
    Assert.Throws<ArgumentException>(fun () -> Uncertain.correlate 0.5 a a) |> ignore
    Assert.Throws<ArgumentException>(fun () -> Uncertain.correlate 0.5 a (a + b)) |> ignore
    Assert.Throws<ArgumentException>(fun () -> Uncertain.withDof 3.0 (a + b) |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> Uncertain.withDof 0.0 a |> ignore) |> ignore

[<Fact>]
let ``declared correlation reaches the uncertainty`` () =
    let a = Uncertain.standard "a" U.metre 1.0<m> 0.3<m>
    let b = Uncertain.standard "b" U.metre 1.0<m> 0.4<m>
    Uncertain.correlate 1.0 a b
    near 1e-12 0.7 (float (a + b).Uncertainty)
    near 1e-12 0.1 (float (a - b).Uncertainty)
    near 1e-12 1.0 (Uncertain.correlation a b)
    // Declared again, the coefficient is replaced.
    Uncertain.correlate -1.0 a b
    near 1e-12 0.1 (float (a + b).Uncertainty)

// ======================================================================================
// GUM H.1: calibration of an end gauge
// ======================================================================================

/// l = l_s + d − l_s (δα · θ + α_s · δθ), with the inputs of table H.1. Temperatures are
/// differences, so kelvin stands for the text's °C.
[<Fact>]
let ``GUM H.1, the end gauge`` () =
    let perKelvin = U.recip U.kelvin
    let rectangular (halfWidth: float<'u>) = halfWidth / sqrt 3.0

    // The standard: 50.000 623 mm, U = 0.075 µm at k = 3, 18 degrees of freedom (H.1.3.1).
    let ls = Uncertain.standard "Standard, l_s" U.metre 50.000623e-3<m> (0.075e-6<m> / 3.0) |> Uncertain.withDof 18.0
    // The difference: five readings against a pooled standard deviation, and the comparator's
    // random and systematic effects (H.1.3.2).
    let dMean = Uncertain.standard "Readings" U.metre 215e-9<m> 5.8e-9<m> |> Uncertain.withDof 24.0
    let dRandom = Uncertain.standard "Comparator, random" U.metre 0.0<m> 3.9e-9<m> |> Uncertain.withDof 5.0
    let dSystematic = Uncertain.standard "Comparator, systematic" U.metre 0.0<m> 6.7e-9<m> |> Uncertain.withDof 8.0
    let d = dMean + dRandom + dSystematic
    // Expansion coefficient of the standard, ± 2 × 10⁻⁶ °C⁻¹ rectangular (H.1.3.3).
    let alphaS = Uncertain.standard "α_s" perKelvin 11.5e-6</K> (rectangular 2e-6</K>)
    // Deviation of the bench from 20 °C: the mean and a cyclic variation (H.1.3.4).
    let theta = Uncertain.standard "θ" U.kelvin -0.1<K> 0.41<K>
    // Difference of the expansion coefficients, ± 1 × 10⁻⁶ °C⁻¹, 50 degrees of freedom (H.1.3.5).
    let deltaAlpha = Uncertain.standard "δα" perKelvin 0.0</K> (rectangular 1e-6</K>) |> Uncertain.withDof 50.0
    // Difference of the temperatures, ± 0.05 °C, 2 degrees of freedom (H.1.3.6).
    let deltaTheta = Uncertain.standard "δθ" U.kelvin 0.0<K> (rectangular 0.05<K>) |> Uncertain.withDof 2.0

    let l: Uncertain<m> = ls + d - ls * (deltaAlpha * theta + alphaS * deltaTheta)

    let nm (x: float<m>) = float x * 1e9

    // H.1.3.2: u(d) = 9.7 nm with 25.6 effective degrees of freedom. The text forms the 25.6 from
    // its rounded 9.7 nm, 9.7⁴ / 345.3; from the unrounded 9.68 nm it is 25.4.
    close 1 9.7 (nm d.Uncertainty)

    match Uncertain.dof d with
    | EffectiveDof.Known nu ->
        close 1 25.4 nu
        close 1 25.6 (nu * (9.7 / nm d.Uncertainty) ** 4.0)
    | EffectiveDof.Undefined reason -> failwith reason

    // Table H.1, the column u_i(l) in nanometres.
    let part (label: string) =
        Uncertain.contributions l |> Array.find (fun (i, _) -> i.Label = label) |> snd |> nm |> abs

    close 1 25.0 (part "Standard, l_s")
    close 1 0.0 (part "α_s")
    close 1 0.0 (part "θ")
    close 1 2.9 (part "δα")
    close 1 16.6 (part "δθ")

    // H.1.4 and H.1.5: l = 50.000 838 mm, u_c(l) = 32 nm (from u_c² = 1002 nm²).
    close 6 50.000838 (float l.Value * 1e3)
    close 0 1002.0 (nm l.Uncertainty ** 2.0 - 0.5)
    close 0 32.0 (nm l.Uncertainty)

    // H.1.6: 16.7 effective degrees of freedom, truncated to 16, t₉₉(16) = 2.92, U₉₉ = 93 nm.
    match Uncertain.dof l with
    | EffectiveDof.Known nu -> near 0.1 16.7 nu
    | EffectiveDof.Undefined reason -> failwith reason

    let expanded = Uncertain.expanded 0.99 l
    Assert.Equal(16.0, expanded.Dof)
    close 2 2.92 expanded.CoverageFactor
    // The text multiplies its rounded figures, 2.92 × 32 nm = 93 nm; unrounded it is 92.5 nm.
    close 0 93.0 (Math.Round(expanded.CoverageFactor, 2) * Math.Round(nm l.Uncertainty))
    near 1.0 93.0 (nm expanded.Expanded)
    Assert.Equal(l.Value, expanded.Estimate)

// ======================================================================================
// GUM H.2: simultaneous resistance and reactance measurement
// ======================================================================================

/// Five simultaneous observations of V, I and φ (table H.2), which are correlated because they
/// are read together; R = V/I · cos φ, X = V/I · sin φ, Z = V/I (table H.4).
[<Fact>]
let ``GUM H.2, correlated inputs`` () =
    let vs = [| 5.007; 4.994; 5.005; 4.990; 4.999 |]
    let is = [| 19.663e-3; 19.639e-3; 19.640e-3; 19.685e-3; 19.678e-3 |]
    let phis = [| 1.0456; 1.0438; 1.0468; 1.0428; 1.0433 |]

    let mean (xs: float[]) = Array.average xs

    /// The experimental standard deviation of the mean, s/√n.
    let ofMean (xs: float[]) =
        let m = mean xs
        sqrt (Array.sumBy (fun x -> (x - m) ** 2.0) xs / float (xs.Length - 1) / float xs.Length)

    let r (xs: float[]) (ys: float[]) =
        let mx, my = mean xs, mean ys
        Array.fold2 (fun acc x y -> acc + (x - mx) * (y - my)) 0.0 xs ys
        / sqrt (Array.sumBy (fun x -> (x - mx) ** 2.0) xs * Array.sumBy (fun y -> (y - my) ** 2.0) ys)

    // Table H.2's last rows.
    close 4 4.9990 (mean vs)
    close 4 0.0032 (ofMean vs)
    close 4 19.6610 (mean is * 1e3)
    close 4 0.0095 (ofMean is * 1e3)
    close 5 1.04446 (mean phis)
    close 5 0.00075 (ofMean phis)
    close 2 -0.36 (r vs is)
    close 2 0.86 (r vs phis)
    close 2 -0.65 (r is phis)

    let v = Uncertain.standard "V" U.volt (mean vs * 1.0<V>) (ofMean vs * 1.0<V>)
    let i = Uncertain.standard "I" U.ampere (mean is * 1.0<A>) (ofMean is * 1.0<A>)
    let phi = Uncertain.standard "φ" U.radian (mean phis * 1.0<rad>) (ofMean phis * 1.0<rad>)
    Uncertain.correlate (r vs is) v i
    Uncertain.correlate (r vs phis) v phi
    Uncertain.correlate (r is phis) i phi

    let resistance: Uncertain<ohm> = v / i * Uncertain.cos phi
    let reactance: Uncertain<ohm> = v / i * Uncertain.sin phi
    let impedance: Uncertain<ohm> = v / i

    // Table H.4.
    close 3 127.732 (float resistance.Value)
    close 3 0.071 (float resistance.Uncertainty)
    close 3 219.847 (float reactance.Value)
    // The table prints 0.295. From its own data the law of propagation gives 0.2956, by this
    // library and by the covariance matrix worked separately, so the last digit is the text's.
    close 4 0.2956 (float reactance.Uncertainty)
    close 3 254.260 (float impedance.Value)
    close 3 0.236 (float impedance.Uncertainty)
    close 3 -0.588 (Uncertain.correlation resistance reactance)
    close 3 -0.485 (Uncertain.correlation resistance impedance)
    close 3 0.993 (Uncertain.correlation reactance impedance)

    // Z² = R² + X², so the three results are bound: the uncertainty of the identity is nothing.
    let identity = impedance * impedance - resistance * resistance - reactance * reactance
    near 1e-9 0.0 (float identity.Value)
    near 1e-9 0.0 (float identity.Uncertainty)

    // Correlated inputs leave no effective degrees of freedom to state, and no factor is made up.
    match Uncertain.dof resistance with
    | EffectiveDof.Undefined _ -> ()
    | EffectiveDof.Known nu -> failwithf "expected none, got %g" nu

    let refused = Assert.Throws<InvalidOperationException>(fun () -> Uncertain.expanded 0.95 resistance |> ignore)
    Assert.Contains("correlated", refused.Message)
    near 1e-9 (Coverage.normal 0.95 * float resistance.Uncertainty) (float (Uncertain.expandedAssumingNormal 0.95 resistance).Expanded)

// ======================================================================================
// Cost
// ======================================================================================

/// A timing is only a timing with the machine to itself, so this runs while no other test does.
[<CollectionDefinition("Uncertainty cost", DisableParallelization = true)>]
type CostCollection() = class end

[<Collection("Uncertainty cost")>]
module Cost =

    /// Ten thousand rows of a five-input model, each row with its own inputs, in under 50 ms.
    [<Fact>]
    let ``ten thousand rows of a five-input model take under 50 ms`` () =
        let rows = 10_000
        let calibration = Uncertain.standard "Stopwatch calibration" U.second 0.0<s> 0.005<s>

        let run () =
            let mutable sum = 0.0

            for row in 0 .. rows - 1 do
                let l = Uncertain.standard "Length" U.metre (1.0<m> + 1e-5<m> * float row) 0.002<m>
                let t = Uncertain.standard "Period" U.second 2.006<s> 0.004<s>
                let zero = Uncertain.standard "Zero" U.metre 0.0<m> 0.001<m>
                let angle = Uncertain.standard "Amplitude" U.radian 0.1<rad> 0.01<rad>
                let period = t + calibration
                let correction = 1.0 + Uncertain.sin angle * Uncertain.sin angle / 16.0
                let g = 4.0 * Math.PI ** 2.0 * (l + zero) / (period * period) * correction * correction
                sum <- sum + float g.Uncertainty

            sum

        run () |> ignore
        let best =
            Seq.init 5 (fun _ ->
                let clock = Diagnostics.Stopwatch.StartNew()
                run () |> ignore
                clock.Elapsed.TotalMilliseconds)
            |> Seq.min

        Assert.True(best < 50.0, sprintf "%.1f ms" best)
