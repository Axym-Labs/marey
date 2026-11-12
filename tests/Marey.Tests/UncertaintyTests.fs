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
// Inputs as a lab names them
// ======================================================================================

[<Fact>]
let ``repeated readings are a type A input`` () =
    let t = Uncertain.ofReadings "Period" U.second [ 2.01<s>; 2.00<s>; 2.01<s> ]
    near 1e-12 (6.02 / 3.0) (float t.Value)
    // s = 0.01 s / √3 over three readings, so s/√n is a third of 0.01 s.
    near 1e-12 (0.01 / 3.0) (float t.Uncertainty)
    let input, _ = (Uncertain.contributions t).[0]
    Assert.Equal(2.0, input.Dof)
    Assert.Equal(InputKind.TypeA, input.Kind)
    Assert.Equal("Period", input.Label)
    Assert.Throws<ArgumentException>(fun () -> Uncertain.ofReadings "One" U.second [ 2.0<s> ] |> ignore) |> ignore

[<Fact>]
let ``type B inputs divide by what their distribution says`` () =
    let u (x: Uncertain<m>) = float x.Uncertainty
    near 1e-15 (1e-3 / sqrt 12.0) (u (Uncertain.resolution "Ruler" U.metre 1.000<m> 1e-3<m>))
    near 1e-15 (2e-3 / sqrt 3.0) (u (Uncertain.rectangular "Tolerance" U.metre 1.000<m> 2e-3<m>))
    near 1e-15 (2e-3 / sqrt 6.0) (u (Uncertain.triangular "Tolerance" U.metre 1.000<m> 2e-3<m>))
    near 1e-15 0.025e-6 (u (Uncertain.certificate "Gauge" U.metre 0.05<m> 0.075e-6<m> 3.0))
    // GUM 4.3.4: a 95 % interval is 1.960 standard deviations wide.
    near 1e-7 (0.5 / 1.959964) (u (Uncertain.certificateAt "Gauge" U.metre 1.0<m> 0.5<m> 0.95))
    let input, _ = (Uncertain.contributions (Uncertain.resolution "Ruler" U.metre 1.000<m> 1e-3<m>)).[0]
    Assert.Equal(InputKind.TypeB, input.Kind)
    Assert.True(Double.IsPositiveInfinity input.Dof)
    Assert.Throws<ArgumentException>(fun () -> Uncertain.certificate "Gauge" U.metre 1.0<m> 0.1<m> 0.0 |> ignore) |> ignore

[<Fact>]
let ``apply takes a function arithmetic does not reach`` () =
    let x = Uncertain.standard "x" U.one 0.37 1e-3
    let viaApply = Uncertain.apply U.one (fun v -> sin v) x
    near 1e-15 (sin 0.37) viaApply.Value
    // The slope is a secant over ± u, a part in 10⁷ from the tangent here.
    near 1e-9 (sin x).Uncertainty viaApply.Uncertainty
    // The result still depends on x, and knows it.
    near 1e-9 0.0 (viaApply - sin x).Uncertainty
    Assert.Equal(0.0, (viaApply - viaApply).Uncertainty)

    let l = Uncertain.standard "Length" U.metre 2.0<m> 0.1<m>
    let w = Uncertain.standard "Width" U.metre 3.0<m> 0.2<m>
    let area: Uncertain<m^2> = Uncertain.apply2 (U.square U.metre) (fun a b -> a * b) l w
    near 1e-12 (float (l * w).Uncertainty) (float area.Uncertainty)
    near 1e-12 0.0 (float (area - l * w).Uncertainty)
    near 1e-15 (0.1 / 2.0) (Uncertain.relative l)

// ======================================================================================
// Reporting
// ======================================================================================

/// The gram under the kilogram's measure: the unit the GUM's example is in.
let private gram: U<kg> = U.scaled "g" 1e-3 U.kilogram

/// GUM 7.2.2: m_S = 100.021 47 g with u_c = 0.35 mg, in the forms the text numbers 2 to 4.
[<Fact>]
let ``GUM 7.2.2, the forms of a result`` () =
    let m = Uncertain.standard "m_S" gram 100.02147<kg> 0.00035<kg>
    let inForm form = Uncertain.reportWith { Uncertain.reportFormat with Form = form } m
    Assert.Equal("100.02147(35) g", Uncertain.report m)
    Assert.Equal("100.02147(35) g", string m)
    Assert.Equal("100.02147(0.00035) g", inForm ReportForm.Explicit)
    Assert.Equal("(100.02147 ± 0.00035) g", inForm ReportForm.PlusMinus)

/// GUM 7.2.4: the same mass with U = 0.79 mg from k = 2.26, Student's t at 9 degrees of
/// freedom and 95 %.
[<Fact>]
let ``GUM 7.2.4, an expanded uncertainty states what it rests on`` () =
    let m = Uncertain.standard "m_S" gram 100.02147<kg> 0.00035<kg> |> Uncertain.withDof 9.0
    Assert.Equal("(100.02147 ± 0.00079) g (k = 2.26, 95 %, ν = 9)", Uncertain.reportExpanded (Uncertain.expanded 0.95 m))

    Assert.Equal(
        "(100.02147 ± 0.00069) g (k = 1.96, 95 %, ν = ∞)",
        Uncertain.reportExpanded (Uncertain.expandedAssumingNormal 0.95 m)
    )

[<Theory>]
// GUM 7.2.6: at most two significant digits, and the estimate rounded to match: 10.057 62 Ω with
// 27 mΩ is 10.058 Ω, and 28.05 kHz is 28 kHz.
[<InlineData(10.05762, 0.027, "10.058(27)", "10.058 ± 0.027")>]
[<InlineData(28050.0, 28.05, "28050(28)", "28050 ± 28")>]
// A second digit that rounds up to a third moves the place.
[<InlineData(5.0, 0.0996, "5.00(10)", "5.00 ± 0.10")>]
// Across the decimal point the parentheses keep it, and past it the zeros stay.
[<InlineData(12.34, 1.23, "12.3(1.2)", "12.3 ± 1.2")>]
[<InlineData(1234.0, 120.0, "1230(120)", "1230 ± 120")>]
[<InlineData(-0.5, 0.12, "-0.50(12)", "-0.50 ± 0.12")>]
[<InlineData(-0.0001, 0.04, "0.000(40)", "0.000 ± 0.040")>]
[<InlineData(0.001234, 0.000056, "0.001234(56)", "0.001234 ± 0.000056")>]
// Outside 10⁻⁴ to 10⁶ a power of ten is taken out.
[<InlineData(6.62607e-34, 1.2e-38, "6.62607(12) × 10⁻³⁴", "(6.62607 ± 0.00012) × 10⁻³⁴")>]
[<InlineData(123456789.0, 4321.0, "1.234568(43) × 10⁸", "(1.234568 ± 0.000043) × 10⁸")>]
let ``GUM 7.2.6, rounding`` (value: float, u: float, concise: string, plusMinus: string) =
    let x = Uncertain.standard "" U.one value u
    Assert.Equal(concise, Uncertain.report x)
    Assert.Equal(plusMinus, Uncertain.reportWith { Uncertain.reportFormat with Form = ReportForm.PlusMinus } x)

[<Fact>]
let ``a report in each typography`` () =
    let l = Uncertain.standard "Length" U.metre 1.000<m> 0.002<m>
    let t = Uncertain.standard "Period" U.second 2.006<s> 0.004<s>
    let g = 4.0 * Math.PI ** 2.0 * l / (t * t)
    let inStyle style form = Uncertain.reportWith { Form = form; Style = style; Digits = 2 } g
    Assert.Equal("9.811(44) m·s⁻²", inStyle Unicode ReportForm.Concise)
    Assert.Equal("9.811(44) m.s^-2", inStyle Ascii ReportForm.Concise)
    Assert.Equal("(9.811 +- 0.044) m.s^-2", inStyle Ascii ReportForm.PlusMinus)
    Assert.Equal(@"9.811(44)\,\mathrm{m}\,\mathrm{s}^{-2}", inStyle Latex ReportForm.Concise)
    Assert.Equal(@"(9.811 \pm 0.044)\,\mathrm{m}\,\mathrm{s}^{-2}", inStyle Latex ReportForm.PlusMinus)
    Assert.Equal(@"\qty{9.811(44)}{\metre\per\second\squared}", inStyle Siunitx ReportForm.Concise)
    Assert.Equal(@"\qty[separate-uncertainty=true]{9.811(44)}{\metre\per\second\squared}", inStyle Siunitx ReportForm.PlusMinus)
    Assert.Equal("9.81(4) m·s⁻²", Uncertain.reportWith { Uncertain.reportFormat with Digits = 1 } g)

    // No uncertainty, no parentheses; no stated unit, no unit.
    Assert.Equal("2.5 m", Uncertain.report (Uncertain.exact U.metre 2.5<m>))
    Assert.Equal("6.02214076 × 10²³", Uncertain.report (Uncertain.exact U.one 6.02214076e23))
    Assert.Equal("1.00(30)", Uncertain.report (1.0<m> +- 0.3<m>))
    Assert.Equal(@"\num{6.62607(12)e-34}", Uncertain.reportWith { Uncertain.reportFormat with Style = Siunitx } (Uncertain.standard "h" U.one 6.62607e-34 1.2e-38))

// ======================================================================================
// The budget
// ======================================================================================

let private pendulum () =
    let l = Uncertain.resolution "Pendulum length" U.metre 1.000<m> 1e-3<m>
    let t = Uncertain.ofReadings "Period" U.second [ 2.01<s>; 2.00<s>; 2.01<s> ]
    let calibration = Uncertain.certificate "Stopwatch calibration" U.second 0.0<s> 0.01<s> 2.0
    4.0 * Math.PI ** 2.0 * l / ((t + calibration) * (t + calibration))

[<Fact>]
let ``a budget's shares sum to one for independent inputs, largest first`` () =
    let budget = Uncertain.budget (pendulum ())
    near 1e-12 1.0 (budget.Rows |> Array.sumBy (fun row -> row.Share))
    near 1e-12 0.0 budget.Correlations
    Assert.Equal<string[]>([| "Stopwatch calibration"; "Period"; "Pendulum length" |], budget.Rows |> Array.map (fun row -> row.Input.Label))
    // |∂g/∂l| = g / l.
    near 1e-9 (float budget.Estimate / 1.0) (budget.Rows.[2].Sensitivity)

    near
        1e-15
        (float budget.Uncertainty)
        (sqrt (budget.Rows |> Array.sumBy (fun row -> float row.Contribution ** 2.0)))

[<Fact>]
let ``a budget as text`` () =
    let expected =
        [ "Input                  Value            ν     Type  Contribution  Share"
          "Stopwatch calibration  0.0000(50) s     ∞     B     0.049 m·s⁻²   69.1 %"
          "Period                 2.0067(33) s     2     A     0.033 m·s⁻²   30.7 %"
          "Pendulum length        1.00000(29) m    ∞     B     0.0028 m·s⁻²  0.2 %"
          "Combined               9.804(59) m·s⁻²  21.2        0.059 m·s⁻²" ]
        |> String.concat "\n"

    Assert.Equal(expected, Budget.text (Uncertain.budget (pendulum ())))

[<Fact>]
let ``declared correlations take their own line in a budget`` () =
    let a = Uncertain.standard "a" U.metre 1.0<m> 0.3<m>
    let b = Uncertain.standard "b" U.metre 1.0<m> 0.4<m>
    Uncertain.correlate 1.0 a b
    let budget = Uncertain.budget (a + b)
    // u² = 0.49, of which the rows carry 0.09 and 0.16.
    near 1e-12 (0.24 / 0.49) budget.Correlations
    near 1e-12 1.0 (budget.Correlations + (budget.Rows |> Array.sumBy (fun row -> row.Share)))
    Assert.Contains("Correlations", Budget.text budget)

    match budget.Dof with
    | EffectiveDof.Undefined _ -> ()
    | EffectiveDof.Known nu -> failwithf "expected none, got %g" nu

/// The LaTeX and siunitx forms and the budget's table, through the engine.
[<Fact>]
let ``the TeX forms compile`` () =
    let g = pendulum ()
    let named = Uncertain.standard "δθ_1 & 50 %" U.kelvin 0.0<K> 0.029<K> |> Uncertain.withDof 2.0
    let small = Uncertain.standard "h" U.one 6.62607e-34 1.2e-38

    let forms =
        [ for form in [ ReportForm.Concise; ReportForm.Explicit; ReportForm.PlusMinus ] do
              yield "$" + Uncertain.reportWith { Form = form; Style = Latex; Digits = 2 } g + "$"
              yield Uncertain.reportWith { Form = form; Style = Siunitx; Digits = 2 } g
              yield "$" + Uncertain.reportWith { Form = form; Style = Latex; Digits = 2 } small + "$"
              yield Uncertain.reportWith { Form = form; Style = Siunitx; Digits = 2 } small ]

    let document =
        String.concat
            "\n"
            [ @"\documentclass{article}"
              @"\usepackage{siunitx}"
              @"\begin{document}"
              String.concat "\n\n" forms
              ""
              Budget.latex (Uncertain.budget g)
              ""
              Budget.latex (Uncertain.budget (named + named * 2.0))
              @"\end{document}"
              "" ]

    if not (TexToolchain.accepts "uncertainty-forms" document) then
        Console.WriteLine("SKIPPED: " + TexToolchain.Engine + " not found")

// ======================================================================================
// Charts
// ======================================================================================

let private layersOf (fig: Figure) = fig.Panels.[0].Layers

let private textsOf (fig: Figure) =
    Chart.toScene UncertaintyScenes.surface fig
    |> Scene.fold
        (fun acc n ->
            match n with
            | Text t -> t.Text :: acc
            | _ -> acc)
        []

[<Fact>]
let ``the uncertainty figures are their baselines`` () =
    for name, render in UncertaintyScenes.svg do
        Golden.check ("uncertainty/" + name + ".svg") (render ())

[<Fact>]
let ``the legend says what the bars are`` () =
    Assert.Contains("standard uncertainty", textsOf (UncertaintyScenes.errorbars ()))
    // The extensions' uncertainty is taken as known (k = 1.96), the forces have two degrees of
    // freedom each (k = 4.30).
    Assert.Contains("expanded uncertainty, k = 1.96 to 4.30 (95 %)", textsOf (UncertaintyScenes.coverage ()))
    Assert.Contains("x [cm]", textsOf (UncertaintyScenes.errorbars ()) |> List.map (fun t -> t.Replace("extension", "x")))
    Assert.Contains("force [N]", textsOf (UncertaintyScenes.errorbars ()))

    let x = [| 1.0<m> +- 0.1<m>; 2.0<m> +- 0.1<m> |]
    Assert.Equal("expanded uncertainty, k = 1.96 (95 %)", (layersOf (Chart.errorbarsWith (ErrorBars.Coverage 0.95) x x)).[0].Name)

    Assert.Equal(
        "expanded uncertainty, k = 2.58 (99 %, normal)",
        (layersOf (Chart.errorbarsWith (ErrorBars.CoverageAssumingNormal 0.99) x x)).[0].Name
    )

/// With every uncertainty taken as known, 95 % bars are the standard ones times 1.960, and
/// nothing else about the layers moves.
[<Fact>]
let ``standard and expanded bars differ by the factor alone`` () =
    let x = [| 1.0; 2.0; 3.5 |] |> Array.map (fun v -> Uncertain.standard "" U.second (v * 1.0<s>) 0.05<s>)
    let y = [| 0.9; 2.2; 3.4 |] |> Array.map (fun v -> Uncertain.standard "" U.metre (v * 1.0<m>) (0.1<m> * v))
    let standard = layersOf (Chart.errorbars x y)
    let expanded = layersOf (Chart.errorbarsWith (ErrorBars.Coverage 0.95) x y)
    let k = Coverage.normal 0.95
    Assert.Equal(3, standard.Length)
    Assert.Equal(3, expanded.Length)

    let halves (centres: float[]) (l: Layer) =
        match l.Geom with
        | GeomErrorY(low, high)
        | GeomErrorX(low, high) ->
            Array.init centres.Length (fun i ->
                near 1e-12 (float high.[i] - centres.[i]) (centres.[i] - float low.[i])
                float high.[i] - centres.[i])
        | _ -> failwith "not an error-bar layer"

    for i, centres in [ 0, y |> Array.map (fun v -> float v.Value); 1, x |> Array.map (fun v -> float v.Value) ] do
        Array.iter2 (fun s e -> near 1e-12 (k * s) e) (halves centres standard.[i]) (halves centres expanded.[i])
        Assert.Equal(standard.[i].Style, expanded.[i].Style)

    near 1e-12 0.05 (halves (x |> Array.map (fun v -> float v.Value)) standard.[1]).[0]
    Assert.Equal(GeomPoint, standard.[2].Geom)
    Assert.Equal(GeomPoint, expanded.[2].Geom)
    Assert.Equal("standard uncertainty", standard.[0].Name)
    Assert.Equal<bool[]>([| true; false; false |], standard |> Array.map (fun l -> l.ShowInLegend))

[<Fact>]
let ``a value with no uncertainty gets no bar`` () =
    let t = U.second.Of [ 1.0<s>; 2.0<s> ]
    let x = [| 1.0<m> +- 0.1<m>; 2.0<m> +- 0.1<m> |]
    let onY = layersOf (Chart.errorbars (Uncertain.exactly t) x)
    Assert.Equal(2, onY.Length)
    Assert.True(match onY.[0].Geom with GeomErrorY _ -> true | _ -> false)
    let onX = layersOf (Chart.errorbars x (Uncertain.exactly t))
    Assert.True(match onX.[0].Geom with GeomErrorX _ -> true | _ -> false)
    Assert.Equal("standard uncertainty", onX.[0].Name)
    let none = layersOf (Chart.errorbarsWith (ErrorBars.Coverage 0.95) (Uncertain.exactly t) (Uncertain.exactly t))
    Assert.Equal(1, none.Length)
    Assert.False(none.[0].ShowInLegend)
    Assert.Throws<ArgumentException>(fun () -> Chart.errorbars x [| x.[0] |] |> ignore) |> ignore

[<Fact>]
let ``expanded bars are refused where no factor can be stated`` () =
    let a = Uncertain.standard "a" U.metre 1.0<m> 0.3<m>
    let b = Uncertain.standard "b" U.metre 1.0<m> 0.4<m>
    Uncertain.correlate 0.5 a b
    let t = Uncertain.exactly (U.second.Of [ 1.0<s> ])
    Assert.Throws<InvalidOperationException>(fun () -> Chart.errorbarsWith (ErrorBars.Coverage 0.95) t [| a + b |] |> ignore) |> ignore
    Assert.Equal(2, (layersOf (Chart.errorbarsWith (ErrorBars.CoverageAssumingNormal 0.95) t [| a + b |])).Length)
    Assert.Equal(2, (layersOf (Chart.errorbars t [| a + b |])).Length)

[<Fact>]
let ``added series keep their bars and take the next colour`` () =
    let layers = layersOf (UncertaintyScenes.added ())
    Assert.Equal(4, layers.Length)
    Assert.Equal(layers.[0].Style, layers.[1].Style |> fun s -> { s with Shape = layers.[0].Style.Shape })
    Assert.NotEqual(layers.[0].Style, layers.[2].Style)
    Assert.Equal<string[]>([| "standard uncertainty"; "run 1"; "standard uncertainty"; "run 2" |], layers |> Array.map (fun l -> l.Name))
    // The second series' bars are what the first's already said they are.
    Assert.Equal<bool[]>([| true; true; false; true |], layers |> Array.map (fun l -> l.ShowInLegend))

[<Fact>]
let ``the block takes uncertain values and draws their bars`` () =
    let fig = UncertaintyScenes.block ()
    let layers = layersOf fig
    Assert.Equal(4, layers.Length)
    Assert.True(match layers.[0].Geom with GeomErrorY _ -> true | _ -> false)
    Assert.True(match layers.[1].Geom with GeomErrorX _ -> true | _ -> false)
    Assert.Equal(GeomPoint, layers.[2].Geom)
    Assert.Equal(GeomLine, layers.[3].Geom)
    Assert.Equal("standard uncertainty", layers.[0].Name)
    let texts = textsOf fig
    Assert.Contains("standard uncertainty", texts)
    Assert.Contains("x [cm]", texts)
    Assert.Contains("F [N]", texts)

    // The first row: 1.1 cm with 0.15 cm either side.
    match layers.[1].Geom with
    | GeomErrorX(low, high) ->
        near 1e-12 0.95 (float low.[0])
        near 1e-12 1.25 (float high.[0])
    | _ -> ()

    // Values with no uncertainty are a plain channel.
    let exact =
        plot [ 1.0; 2.0; 3.0 ] {
            xUncertain (fun v -> Uncertain.exact U.second (v * 1.0<s>))
            yUncertain (fun v -> Uncertain.exact U.metre (v * 2.0<m>))
        }

    Assert.Equal(1, (layersOf exact).Length)

// ======================================================================================
// The measured constants, CODATA 2022
// ======================================================================================

/// Each measured constant reports CODATA 2022's value and standard uncertainty, as NIST's table
/// prints them, and its value is the `float` of the same name in `Constants`.
[<Fact>]
let ``the uncertain constants report CODATA's values and standard uncertainties`` () =
    let check (expected: string) (exact: float<'u>) (x: Uncertain<'u>) =
        Assert.Equal(expected, Uncertain.report x)
        Assert.Equal(float exact, float x.Value)

    check "0.0072973525643(11)" Constants.fineStructure Uncertain.Constants.fineStructure
    check "1.0973731568157(12) × 10⁷ m⁻¹" Constants.rydberg Uncertain.Constants.rydberg
    check "6.67430(15) × 10⁻¹¹ m³·kg⁻¹·s⁻²" Constants.G Uncertain.Constants.G
    check "1.25663706127(20) × 10⁻⁶ H·m⁻¹" Constants.mu0 Uncertain.Constants.mu0
    check "8.8541878188(14) × 10⁻¹² F·m⁻¹" Constants.eps0 Uncertain.Constants.eps0
    check "9.1093837139(28) × 10⁻³¹ kg" Constants.me Uncertain.Constants.me
    check "1.66053906892(52) × 10⁻²⁷ kg" Constants.atomicMass Uncertain.Constants.atomicMass
    check "1.67262192595(52) × 10⁻²⁷ kg" Constants.mp Uncertain.Constants.mp
    check "1.67492750056(85) × 10⁻²⁷ kg" Constants.mn Uncertain.Constants.mn
    check "5.29177210544(82) × 10⁻¹¹ m" Constants.bohrRadius Uncertain.Constants.bohrRadius
    check "9.2740100657(29) × 10⁻²⁴ J·T⁻¹" Constants.bohrMagneton Uncertain.Constants.bohrMagneton

/// The constants are correlated through what they rest on: μ₀ ∝ α and ε₀ ∝ 1/α, m_e ∝ R∞/α²
/// and μ_B ∝ 1/m_e, the masses through m_u. G rests on nothing the others do.
[<Fact>]
let ``the uncertain constants are correlated through the relations that tie them`` () =
    let r a b = Uncertain.correlation a b
    near 1e-9 -1.0 (r Uncertain.Constants.mu0 Uncertain.Constants.eps0)
    near 1e-9 -1.0 (r Uncertain.Constants.me Uncertain.Constants.bohrMagneton)
    near 1e-4 -1.0 (r Uncertain.Constants.me Uncertain.Constants.bohrRadius)
    near 1e-2 1.0 (r Uncertain.Constants.me Uncertain.Constants.mp)
    near 1e-2 1.0 (r Uncertain.Constants.mp Uncertain.Constants.atomicMass)
    Assert.Equal(0.0, r Uncertain.Constants.me Uncertain.Constants.G)

/// α cancels from the proton–electron mass ratio: its uncertainty is the relative atomic masses',
/// 2 × 10⁻¹¹, not the masses' 3 × 10⁻¹⁰. CODATA states 1836.152673426(32); the inputs here are
/// independent and CODATA's uncertainties rounded, so the last digits differ.
[<Fact>]
let ``alpha cancels from the proton-electron mass ratio`` () =
    let ratio = Uncertain.Constants.mp / Uncertain.Constants.me
    near 1e-8 1836.152673426 (float ratio.Value)
    let relative = Uncertain.relative ratio
    Assert.InRange(relative, 1.7e-11, 2.2e-11)
    let inputs = Uncertain.budget ratio |> fun b -> b.Rows |> Array.map (fun row -> row.Input.Label)
    Assert.Equal("electron relative atomic mass (CODATA 2022)", Array.head inputs)

/// The product μ₀ε₀ is 1/c², exact by the SI: what is left of α is CODATA's rounding.
[<Fact>]
let ``mu0 times eps0 is 1 over c squared`` () =
    let product = Uncertain.Constants.mu0 * Uncertain.Constants.eps0
    near 1e-26 (1.0 / (float Constants.c * float Constants.c)) (float product.Value)
    Assert.True(Uncertain.relative product < 2e-12, sprintf "%g" (Uncertain.relative product))

/// The constants are one input each wherever they are used, so a constant over itself is exact.
[<Fact>]
let ``a constant over itself is exact`` () =
    let one = Uncertain.Constants.G / Uncertain.Constants.G
    Assert.Equal(0.0, float one.Uncertainty)

/// e/m_e, the figure an e/m experiment is compared with.
[<Fact>]
let ``the electron's specific charge carries m_e's uncertainty`` () =
    let eOverM = Constants.Quantities.e / Uncertain.Constants.me
    Assert.Equal("1.75882000838(54) × 10¹¹ C·kg⁻¹", Uncertain.report eOverM)

// ======================================================================================
// The guide's pendulum
// ======================================================================================

/// The guide's uncertainty chapter shows what its first block prints, in a `text` block that the
/// guide's own run does not compare. This does: the report and the budget, line for line.
[<Fact>]
let ``the guide shows what its pendulum prints`` () =
    let page = IO.File.ReadAllText(IO.Path.Combine(Golden.repoRoot.Force(), "docs", "guide", "16-uncertainty.md")).Replace("\r\n", "\n")
    let shown = Text.RegularExpressions.Regex.Match(page, "^```text\n(.*?)^```$", Text.RegularExpressions.RegexOptions.Multiline ||| Text.RegularExpressions.RegexOptions.Singleline)
    Assert.True(shown.Success, "the chapter has no text block")

    let pendulumLength = Uncertain.resolution "Length" U.metre 0.800<m> 1e-3<m>
    let tenSwings = Uncertain.ofReadings "Ten periods" U.second [ 17.951<s>; 17.901<s>; 17.971<s>; 17.921<s>; 17.911<s> ]
    let stopwatch = Uncertain.certificate "Stopwatch calibration" U.second 0.0<s> 0.02<s> 2.0
    let swingPeriod = (tenSwings + stopwatch) / 10.0
    let gPendulum = 4.0 * Math.PI ** 2.0 * pendulumLength / (swingPeriod * swingPeriod)

    let printed = Uncertain.report gPendulum + "\n" + Budget.text (Uncertain.budget gPendulum) + "\n"
    Assert.Equal(printed, shown.Groups.[1].Value)
    Assert.Contains("Uncertain.resolution \"Length\" U.metre 0.800<m> 1e-3<m>", page)
    Assert.Contains("[ 17.951<s>; 17.901<s>; 17.971<s>; 17.921<s>; 17.911<s> ]", page)

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
