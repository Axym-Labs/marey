/// Weighted least squares on uncertain points, checked against the GUM's worked fit (JCGM
/// 100:2008, H.3), against the covariance matrix worked apart from the library, and against
/// synthetic data whose scatter is known.
module Marey.Tests.UncertaintyFitTests

open System
open Xunit
open Marey

let private close (digits: int) (expected: float) (actual: float) =
    Assert.Equal(expected, Math.Round(actual, digits), digits + 2)

let private near (tolerance: float) (expected: float) (actual: float) =
    Assert.True(abs (expected - actual) <= tolerance, sprintf "expected %g within %g, got %.12g" expected tolerance actual)

let private known (dof: EffectiveDof) =
    match dof with
    | EffectiveDof.Known nu -> nu
    | EffectiveDof.Undefined reason -> failwithf "no degrees of freedom: %s" reason

// ======================================================================================
// GUM H.3: calibration of a thermometer
// ======================================================================================

/// Table H.6: eleven thermometer readings tₖ and the corrections bₖ = t_R − tₖ a reference
/// thermometer gives, with no uncertainty stated for either. The line b(t) = y₁ + y₂ (t − t₀),
/// t₀ = 20 °C, takes its uncertainty from the scatter (H.3.3). Temperatures are kelvin here, so
/// t₀ is 293.15 K and the corrections, as differences, are the same numbers.
let private thermometer () =
    let t = [| 21.521; 22.012; 22.512; 23.003; 23.507; 23.999; 24.513; 25.002; 25.503; 26.010; 26.511 |]
    let b = [| -0.171; -0.169; -0.166; -0.159; -0.164; -0.165; -0.156; -0.157; -0.159; -0.161; -0.160 |]
    let readings = U.kelvin.Of(t |> Array.map (fun c -> (c + 273.15) * 1.0<K>))
    let corrections = U.kelvin.Of(b |> Array.map (fun c -> c * 1.0<K>))
    Fit.lineAbout 293.15<K> (Uncertain.exactly readings) (Uncertain.exactly corrections)

[<Fact>]
let ``GUM H.3, the thermometer`` () =
    let fit = thermometer ()
    let y1: Uncertain<K> = fit.Intercept
    let y2: Uncertain<1> = fit.Slope

    Assert.True(fit.FromScatter)
    Assert.Equal(11, fit.Points)
    Assert.Equal(9, fit.Dof)
    Assert.True(Double.IsNaN fit.ChiSquared)

    // H.3.3: y₁ = −0.1712 °C, s(y₁) = 0.0029 °C; y₂ = 0.00218, s(y₂) = 0.00067; r = −0.930;
    // s = 0.0035 °C.
    close 4 -0.1712 (float y1.Value)
    close 4 0.0029 (float y1.Uncertainty)
    close 5 0.00218 (float y2.Value)
    close 5 0.00067 (float y2.Uncertainty)
    close 3 -0.930 (Uncertain.correlation y1 y2)
    close 4 0.0035 (float fit.Scatter)

    // H.3.4: the correction at 30 °C, b(30 °C) = −0.1494 °C with u = 0.0041 °C.
    let at30 = fit.At 303.15<K>
    close 4 -0.1494 (float at30.Value)
    close 4 0.0041 (float at30.Uncertainty)

    // One estimate of the scatter stands behind every point, so the slope, the intercept and
    // what is made of them have its n − 2 = 9 degrees of freedom, not more.
    near 1e-9 9.0 (known (Uncertain.dof y1))
    near 1e-9 9.0 (known (Uncertain.dof y2))
    near 1e-9 9.0 (known (Uncertain.dof at30))

[<Fact>]
let ``GUM H.3, the line about the mean temperature has independent coefficients`` () =
    // H.3.6: about t̄ = 24.0085 °C, y₁′ = −0.1625 °C with s(y₁′) = 0.0011 °C, and r(y₁′, y₂) = 0.
    // The text prints t̄ rounded; the correlation vanishes at the mean itself.
    let fit = thermometer ()
    let mean = (21.521 + 22.012 + 22.512 + 23.003 + 23.507 + 23.999 + 24.513 + 25.002 + 25.503 + 26.010 + 26.511) / 11.0
    close 4 24.0085 mean
    let about = fit.At((mean + 273.15) * 1.0<K>)
    close 4 -0.1625 (float about.Value)
    close 4 0.0011 (float about.Uncertainty)
    near 1e-9 0.0 (Uncertain.correlation about fit.Slope)

[<Fact>]
let ``a value derived from slope and intercept agrees with the covariance matrix`` () =
    let fit = thermometer ()
    let u1 = float fit.Intercept.Uncertainty
    let u2 = float fit.Slope.Uncertainty
    let r = Uncertain.correlation fit.Intercept fit.Slope

    // u²(b) = u₁² + Δt² u₂² + 2 Δt r u₁ u₂ at Δt = 10 K, the formula H.3.4 uses.
    for dt in [ -3.0; 0.0; 4.0; 10.0; 25.0 ] do
        let byMatrix = sqrt (u1 * u1 + dt * dt * u2 * u2 + 2.0 * dt * r * u1 * u2)
        near 1e-15 byMatrix (float (fit.At(293.15<K> + dt * 1.0<K>)).Uncertainty)

    // The temperature at which the correction vanishes, −y₁/y₂: a ratio of the two.
    let zero = -fit.Intercept / fit.Slope
    let v = float zero.Value
    let byMatrix = sqrt ((u1 / float fit.Slope.Value) ** 2.0 + (v * u2 / float fit.Slope.Value) ** 2.0 + 2.0 * v * r * u1 * u2 / float fit.Slope.Value ** 2.0)
    near 1e-12 byMatrix (float zero.Uncertainty)

// ======================================================================================
// Weighted fits
// ======================================================================================

/// The weighted least-squares covariance (Aᵀ W A)⁻¹ for a line, worked by hand.
let private lineCovariance (x: float[]) (u: float[]) =
    let w = u |> Array.map (fun u -> 1.0 / (u * u))
    let s = Array.sum w
    let sx = Array.fold2 (fun acc w x -> acc + w * x) 0.0 w x
    let sxx = Array.fold2 (fun acc w x -> acc + w * x * x) 0.0 w x
    let d = s * sxx - sx * sx
    sxx / d, -sx / d, s / d

[<Fact>]
let ``a weighted line has the covariance of the normal equations`` () =
    let x = [| 0.5; 1.0; 1.5; 2.0; 2.5; 3.0 |]
    let y = [| 1.21; 1.98; 2.83; 3.51; 4.40; 5.02 |]
    let u = [| 0.05; 0.04; 0.08; 0.05; 0.12; 0.06 |]
    let ts = Uncertain.exactly (U.second.Of(x |> Array.map (fun v -> v * 1.0<s>)))
    let ds = Array.init x.Length (fun i -> Uncertain.standard (sprintf "d%d" i) U.metre (y.[i] * 1.0<m>) (u.[i] * 1.0<m>))
    let fit = Fit.line ts ds
    let v: Uncertain<m / s> = fit.Slope

    let caa, cab, cbb = lineCovariance x u
    near 1e-12 (sqrt caa) (float fit.Intercept.Uncertainty)
    near 1e-12 (sqrt cbb) (float v.Uncertainty)
    near 1e-12 cab (float (Uncertain.covariance fit.Intercept fit.Slope))
    Assert.Equal("m·s⁻¹", Uncertain.report v |> fun s -> s.Substring(s.IndexOf ' ' + 1))

    let chi2 =
        Array.init x.Length (fun i -> ((y.[i] - float fit.Intercept.Value - float v.Value * x.[i]) / u.[i]) ** 2.0)
        |> Array.sum

    Assert.False(fit.FromScatter)
    near 1e-12 chi2 fit.ChiSquared
    near 1e-12 (chi2 / 4.0) fit.ReducedChiSquared

    // Each point's own input is behind the slope, with the weight its uncertainty gives it.
    Assert.Equal(6, (Uncertain.contributions v).Length)
    let shares = (Uncertain.budget v).Rows |> Array.sumBy (fun row -> row.Share)
    near 1e-12 1.0 shares

[<Fact>]
let ``a calibration shared by every point moves the intercept and not the slope`` () =
    let calibration = Uncertain.standard "Scale zero" U.metre 0.0<m> 0.03<m>
    let x = [| 1.0; 2.0; 3.0; 4.0; 5.0 |]
    let ts = Uncertain.exactly (U.second.Of(x |> Array.map (fun v -> v * 1.0<s>)))
    let ds = x |> Array.mapi (fun i v -> Uncertain.standard (sprintf "d%d" i) U.metre ((0.2 + 0.9 * v + 0.01 * float (i % 2)) * 1.0<m>) 0.02<m> + calibration)
    let fit = Fit.line ts ds

    let part (y: Uncertain<'u>) =
        Uncertain.contributions y |> Array.find (fun (input, _) -> input.Label = "Scale zero") |> snd |> float

    near 1e-15 0.03 (part fit.Intercept)
    near 1e-15 0.0 (part fit.Slope)

    // The difference of two predictions is a length the zero cancels from.
    let rise = fit.At 5.0<s> - fit.At 1.0<s>
    near 1e-15 0.0 (part rise)

[<Fact>]
let ``an uncertain x weighs as its shift along the line`` () =
    // Points on y = 3 x exactly, x uncertain by 0.1, y exact: the effective variance is
    // (3 · 0.1)², so the slope's uncertainty is that of a y uncertain by 0.3.
    let slope = 3.0
    let x = [| 1.0; 2.0; 3.5; 4.0; 6.0 |]
    let xs = x |> Array.mapi (fun i v -> Uncertain.standard (sprintf "x%d" i) U.second (v * 1.0<s>) 0.1<s>)
    let ys = Uncertain.exactly (U.metre.Of(x |> Array.map (fun v -> slope * v * 1.0<m>)))
    let fit = Fit.line xs ys

    let _, _, cbb = lineCovariance x (Array.create x.Length (slope * 0.1))
    near 1e-12 slope (float fit.Slope.Value)
    near 1e-12 (sqrt cbb) (float fit.Slope.Uncertainty)
    Assert.Equal(5, (Uncertain.contributions fit.Slope).Length)
    near 1e-9 0.0 fit.ChiSquared

[<Fact>]
let ``uncertainties on both axes are weighed by the slope the fit settles on`` () =
    let x = [| 0.0; 1.0; 2.0; 3.0; 4.0; 5.0; 6.0 |]
    let y = [| 0.1; 2.2; 3.8; 6.3; 7.9; 10.2; 11.8 |]
    let ux = [| 0.1; 0.2; 0.1; 0.15; 0.1; 0.2; 0.1 |]
    let uy = [| 0.2; 0.1; 0.3; 0.2; 0.2; 0.1; 0.25 |]
    let xs = Array.init x.Length (fun i -> Uncertain.standard "" U.second (x.[i] * 1.0<s>) (ux.[i] * 1.0<s>))
    let ys = Array.init x.Length (fun i -> Uncertain.standard "" U.metre (y.[i] * 1.0<m>) (uy.[i] * 1.0<m>))
    let fit = Fit.line xs ys
    let b = float fit.Slope.Value
    let a = float fit.Intercept.Value

    // At the solution the weights are those of the effective variance at its own slope, and the
    // line is the weighted fit with them.
    let u = Array.init x.Length (fun i -> sqrt (uy.[i] ** 2.0 + (b * ux.[i]) ** 2.0))
    let w = u |> Array.map (fun u -> 1.0 / (u * u))
    let s, sx, sy = Array.sum w, Array.fold2 (fun acc w x -> acc + w * x) 0.0 w x, Array.fold2 (fun acc w y -> acc + w * y) 0.0 w y
    let sxx = Array.fold2 (fun acc w x -> acc + w * x * x) 0.0 w x
    let sxy = Array.init x.Length (fun i -> w.[i] * x.[i] * y.[i]) |> Array.sum
    let d = s * sxx - sx * sx
    near 1e-10 ((s * sxy - sx * sy) / d) b
    near 1e-10 ((sxx * sy - sx * sxy) / d) a

    // Propagated from both axes' inputs, the slope's uncertainty is the matrix's, but for the
    // residual terms, which are small beside it here.
    let _, _, cbb = lineCovariance x u
    near (0.02 * sqrt cbb) (sqrt cbb) (float fit.Slope.Uncertainty)
    Assert.Equal(14, (Uncertain.contributions fit.Slope).Length)

[<Fact>]
let ``chi-squared per degree of freedom is near one for data with the scatter it states`` () =
    // A thousand data sets of twenty points about y = 2 + 0.5 x, each y drawn with the σ it
    // states: χ²/ν averages 1, and the slope's spread over the sets is the uncertainty each fit
    // states for it.
    let random = Random 20261111
    let gauss () = sqrt (-2.0 * log (1.0 - random.NextDouble())) * cos (2.0 * Math.PI * random.NextDouble())
    let x = Array.init 20 (fun i -> float i * 0.5)
    let sigma = x |> Array.map (fun x -> 0.1 + 0.05 * x)
    let ts = Uncertain.exactly (U.second.Of(x |> Array.map (fun v -> v * 1.0<s>)))

    let fits =
        Array.init 1000 (fun _ ->
            let ys =
                Array.init x.Length (fun i ->
                    Uncertain.standard "" U.metre ((2.0 + 0.5 * x.[i] + sigma.[i] * gauss ()) * 1.0<m>) (sigma.[i] * 1.0<m>))

            Fit.line ts ys)

    let reduced = fits |> Array.averageBy (fun f -> f.ReducedChiSquared)
    // The mean of a thousand χ²/18 has a standard deviation of √(2/18)/√1000 = 0.011.
    near 0.04 1.0 reduced

    let slopes = fits |> Array.map (fun f -> float f.Slope.Value)
    let mean = Array.average slopes
    let spread = sqrt (Array.sumBy (fun s -> (s - mean) ** 2.0) slopes / float (slopes.Length - 1))
    let stated = float fits.[0].Slope.Uncertainty
    near 0.01 0.5 mean
    near (0.08 * stated) stated spread

// ======================================================================================
// Curves linear in their coefficients
// ======================================================================================

[<Fact>]
let ``a linear fit with the basis of a line is the line`` () =
    let x = [| 0.5; 1.0; 1.5; 2.0; 2.5; 3.0 |]
    let y = [| 1.21; 1.98; 2.83; 3.51; 4.40; 5.02 |]
    let ts = Uncertain.exactly (U.second.Of(x |> Array.map (fun v -> v * 1.0<s>)))
    let ds = Array.init x.Length (fun i -> Uncertain.standard "" U.metre (y.[i] * 1.0<m>) ((0.05 + 0.01 * float i) * 1.0<m>))
    let line = Fit.line ts ds
    let curve = Fit.linear [ (fun _ -> 1.0); (fun (t: float<s>) -> float t) ] ts ds

    near 1e-12 (float line.Intercept.Value) (float curve.Coefficients.[0].Value)
    near 1e-12 (float line.Slope.Value) (float curve.Coefficients.[1].Value)
    near 1e-12 (float line.Intercept.Uncertainty) (float curve.Coefficients.[0].Uncertainty)
    near 1e-12 (float line.Slope.Uncertainty) (float curve.Coefficients.[1].Uncertainty)
    near 1e-12 line.ChiSquared curve.ChiSquared
    near 1e-12 (float (line.At 1.7<s>).Uncertainty) (float (curve.At 1.7<s>).Uncertainty)

[<Fact>]
let ``a parabola through exact points is recovered, with the scatter's uncertainty`` () =
    // A ball's height against time: h = 1.2 + 4.0 t − 4.9 t², read to a few millimetres.
    let t = Array.init 9 (fun i -> 0.1 * float i)
    let noise = [| 0.003; -0.002; 0.001; -0.004; 0.002; 0.0; -0.001; 0.003; -0.002 |]
    let h = Array.init t.Length (fun i -> 1.2 + 4.0 * t.[i] - 4.9 * t.[i] * t.[i] + noise.[i])
    let ts = Uncertain.exactly (U.second.Of(t |> Array.map (fun v -> v * 1.0<s>)))
    let hs = Uncertain.exactly (U.metre.Of(h |> Array.map (fun v -> v * 1.0<m>)))
    let fit = Fit.linear [ (fun _ -> 1.0); (fun (t: float<s>) -> float t); (fun t -> float t ** 2.0) ] ts hs

    Assert.True(fit.FromScatter)
    Assert.Equal(6, fit.Dof)
    near 0.01 1.2 (float fit.Coefficients.[0].Value)
    near 0.05 4.0 (float fit.Coefficients.[1].Value)
    near 0.05 -4.9 (float fit.Coefficients.[2].Value)
    near 1e-9 6.0 (known (Uncertain.dof fit.Coefficients.[2]))

    // The scatter is the residuals' standard deviation, and with it as every point's
    // uncertainty χ² would be ν: the uncertainties are the matrix's scaled by it.
    let residual = Array.init t.Length (fun i -> h.[i] - float (fit.At(t.[i] * 1.0<s>)).Value)
    near 1e-12 (sqrt (Array.sumBy (fun r -> r * r) residual / 6.0)) (float fit.Scatter)

// ======================================================================================
// What a fit refuses
// ======================================================================================

[<Fact>]
let ``a fit refuses what it cannot weigh or solve`` () =
    let ts = Uncertain.exactly (U.second.Of [ 1.0<s>; 2.0<s>; 3.0<s> ])

    // One point with an uncertainty and the others without would weigh infinitely.
    let mixed = [| Uncertain.exact U.metre 1.0<m>; Uncertain.standard "" U.metre 2.0<m> 0.1<m>; Uncertain.exact U.metre 3.0<m> |]
    let e = Assert.Throws<ArgumentException>(fun () -> Fit.line ts mixed |> ignore)
    Assert.Contains("point 1 has no uncertainty", e.Message)

    // Two exact points leave no scatter to take an uncertainty from.
    let two = Uncertain.exactly (U.second.Of [ 1.0<s>; 2.0<s> ])
    let e = Assert.Throws<ArgumentException>(fun () -> Fit.line two (Uncertain.exactly (U.metre.Of [ 1.0<m>; 2.0<m> ])) |> ignore)
    Assert.Contains("scatter", e.Message)

    // A line through points at one x has no slope.
    let same = Uncertain.exactly (U.second.Of [ 2.0<s>; 2.0<s>; 2.0<s> ])
    let e = Assert.Throws<ArgumentException>(fun () -> Fit.line same (Uncertain.exactly (U.metre.Of [ 1.0<m>; 2.0<m>; 3.0<m> ])) |> ignore)
    Assert.Contains("x does not vary", e.Message)

    // A missing value is not dropped.
    let gap = Uncertain.exactly (U.metre.Of [ 1.0<m>; nan * 1.0<m>; 3.0<m> ])
    let e = Assert.Throws<ArgumentException>(fun () -> Fit.line ts gap |> ignore)
    Assert.Contains("point 2", e.Message)

[<Fact>]
let ``two points with uncertainties make a line with no degrees of freedom left`` () =
    let ts = Uncertain.exactly (U.second.Of [ 1.0<s>; 3.0<s> ])
    let ds = [| Uncertain.standard "a" U.metre 1.0<m> 0.1<m>; Uncertain.standard "b" U.metre 2.0<m> 0.1<m> |]
    let fit = Fit.line ts ds
    near 1e-12 0.5 (float fit.Slope.Value)
    // u(slope) = √(0.1² + 0.1²) / 2.
    near 1e-12 (sqrt 0.02 / 2.0) (float fit.Slope.Uncertainty)
    Assert.Equal(0, fit.Dof)
    Assert.True(Double.IsNaN fit.ReducedChiSquared)

// ======================================================================================
// The band
// ======================================================================================

[<Fact>]
let ``the band is the curve's uncertainty at each x, and the legend says so`` () =
    let fit = thermometer ()
    let fig = Chart.fitBand fit
    let layers = fig.Panels.[0].Layers
    Assert.Equal(2, layers.Length)
    Assert.Equal("standard uncertainty of the fit", layers.[0].Name)
    Assert.True(layers.[0].ShowInLegend)
    Assert.False(layers.[1].ShowInLegend)

    let halves (l: Layer) =
        match l.Geom with
        | GeomRibbon(low, high) -> Array.map2 (fun lo hi -> float (hi - lo) / 2.0) low high
        | g -> failwithf "expected a ribbon, got %A" g

    let lo, hi = fit.XRange
    let first = halves layers.[0]
    Assert.Equal(101, first.Length)
    near 1e-15 (float (fit.At lo).Uncertainty) first.[0]
    near 1e-15 (float (fit.At hi).Uncertainty) first.[100]

    // At 95 % the band widens by Student's t at the scatter's nine degrees of freedom.
    let expanded = (Chart.fitBandWith (ErrorBars.Coverage 0.95) fit).Panels.[0].Layers
    Assert.Equal("expanded uncertainty of the fit, k = 2.26 (95 %)", expanded.[0].Name)
    let k = Coverage.studentT 0.95 9.0
    Array.iter2 (fun s e -> near 1e-15 (k * s) e) first (halves expanded.[0])

[<Fact>]
let ``a band added to error bars states its own uncertainty beside theirs`` () =
    let fig = UncertaintyScenes.fitted ()
    let named = fig.Panels.[0].Layers |> Array.filter (fun l -> l.ShowInLegend) |> Array.map (fun l -> l.Name)
    Assert.Equal<string[]>([| "standard uncertainty"; "standard uncertainty of the fit" |], named)
