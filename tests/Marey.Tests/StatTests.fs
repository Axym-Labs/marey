/// M2 phase 4 — the stats, and D47's answer to where one sits.
///
/// D47 puts a stat *below* the erasure boundary: a pure function over typed arrays, called
/// while both measures are still alive, returning a typed result. Everything in this file is
/// either a closed-form check on the arithmetic — the plan's rule for which stats ship at all —
/// or a claim about the measure that came out of it.
///
/// The measure claims are the reason this file exists rather than the arithmetic ones. A
/// histogram of metres is a *dimensionless count* against a metre axis; a density over metres is
/// in **reciprocal** metres; a fit of metres against seconds has a slope in metres per second.
/// Two of those are asserted by *compiling* a binding whose type annotation is the claim, in the
/// style `RegressionTests.fs` and `ChannelTests.fs` use, because a measure does not survive to
/// runtime where an assertion could look at it. The rest are asserted through the witness, which
/// does.
module Marey.Tests.StatTests

open Xunit
open Marey
open Marey.UnitsOfMeasure

// ======================================================================================
// Fixtures
// ======================================================================================

/// A linear congruential generator, spelled out rather than taken from `System.Random`.
///
/// The tolerance tests below are only meaningful against a *fixed* sample, and `System.Random`'s
/// seeded sequence is a framework implementation detail that has changed before. Eleven lines
/// here buys a sample that is the same on every runtime, which is the same argument `Num.fs`
/// makes for not trusting the platform's number formatting.
type private Lcg(seed: uint64) =
    let mutable s = seed

    member _.NextFloat() =
        s <- s * 6364136223846793005UL + 1442695040888963407UL
        float (s >>> 11) / float (1UL <<< 53)

    /// Box–Muller, so the sample really is normal and Silverman's rule is being asked the
    /// question it answers.
    member this.NextNormal() =
        let u1 = max 1e-12 (this.NextFloat())
        let u2 = this.NextFloat()
        sqrt (-2.0 * log u1) * cos (2.0 * System.Math.PI * u2)

/// Two thousand draws from a standard normal, in metres.
let private heights =
    let g = Lcg 20260820UL
    Array.init 2000 (fun _ -> g.NextNormal() * 1.0<m>)

/// An exact straight line: 3.5 m/s from 2 m.
let private times = Array.init 50 (fun i -> float i * 0.1<s>)
let private positions = times |> Array.map (fun t -> 3.5<m / s> * t + 2.0<m>)

let private close (tol: float) (expected: float) (actual: float) =
    Assert.True(
        abs (expected - actual) <= tol,
        sprintf "expected %g +/- %g, got %g" expected tol actual
    )

// ======================================================================================
// regression — the typed slope, which is M2's headline claim
// ======================================================================================

module Regression =

    /// **The gate, first half: the arithmetic.** Data generated from a known line gives that
    /// line back, exactly enough that the tolerance is float noise rather than method error.
    [<Fact>]
    let ``a fit of a straight line recovers its slope, intercept and R2`` () =
        let f = Stat.regression U.second U.metre times positions

        close 1e-9 3.5 (float f.Slope)
        close 1e-9 2.0 (float f.Intercept)
        close 1e-12 1.0 f.R2
        Assert.Equal(50, f.N)
        Assert.Equal(0, f.Dropped)

    /// **The gate, second half, and the one M2 exists to pass: the slope is a velocity.**
    ///
    /// The annotation on `v` is the whole test. `Fit.Slope` is `float<'uy/'ux>`, so binding it to
    /// an annotated `float<m/s>` is a claim the *compiler* checks: if the quotient were ever
    /// weakened to `float` or to `float<'uy>`, this file would stop building. Nothing at runtime
    /// can stand in for it, because the measure is gone by then.
    ///
    /// The runtime half is what the slope can still *say* about itself once it is a number on a
    /// page. The witness is `U.div uy ux`, so D18's recorded factorisation survives and the axis
    /// compiler prints `m·s⁻¹` rather than falling back to a base-unit expansion.
    [<Fact>]
    let ``the slope of metres against seconds is a velocity, and says so`` () =
        let f = Stat.regression U.second U.metre times positions

        // The claim, checked at compile time.
        let v: float<m / s> = f.Slope
        close 1e-9 3.5 (float v)

        // The claim, checked at runtime, through the same path an axis title takes.
        Assert.Equal(Dim.sub Dim.length Dim.time, f.SlopeUnit.Dim)
        Assert.Equal<(string * int) list>([ "m", 1; "s", -1 ], f.SlopeUnit.Factors)

        let axis =
            Axis.auto UnitStyle.Unicode "v" (Scale.linear f.SlopeUnit (0.0<m / s>, 4.0<m / s>))

        Assert.Equal("m·s⁻¹", axis.UnitText)

    /// The intercept keeps y's measure, which is the other half of the quotient being right: a
    /// slope of `m/s` and an intercept of `m` is the only pair that makes `predict` type-check.
    [<Fact>]
    let ``the intercept is in y's own unit`` () =
        let f = Stat.regression U.second U.metre times positions
        let x0: float<m> = f.Intercept

        close 1e-9 2.0 (float x0)
        Assert.Equal(Dim.length, f.InterceptUnit.Dim)

    /// `predict` is the one-line proof that the quotient cancels. If `Slope` were not
    /// `float<'uy/'ux>`, `Intercept + Slope * x` would not be a `float<m>` and this would not
    /// compile.
    [<Fact>]
    let ``predict is the fitted line, in y's unit`` () =
        let f = Stat.regression U.second U.metre times positions
        let atTwoSeconds: float<m> = Stat.predict f 2.0<s>

        close 1e-9 9.0 (float atTwoSeconds)

    /// Noise averages out. A tolerance test rather than an exact one, and the sample is fixed
    /// (see `Lcg`) so it is a tolerance on the *method* and not on the runtime.
    [<Fact>]
    let ``a fit through noisy data recovers the slope it was generated from`` () =
        let g = Lcg 7UL

        let ys =
            times |> Array.map (fun t -> 3.5<m / s> * t + 2.0<m> + g.NextNormal() * 0.05<m>)

        let f = Stat.regression U.second U.metre times ys

        close 0.05 3.5 (float f.Slope)
        Assert.True(f.R2 > 0.99, sprintf "R2 was %g" f.R2)

    /// D52's rule, one layer earlier: a pair the fit cannot use is not folded in as if it were a
    /// measurement, and the count is reported rather than swallowed.
    [<Fact>]
    let ``a fit drops non-finite pairs and reports how many`` () =
        let xs = Array.append times [| 5.0<s>; nan * 1.0<s> |]

        let ys =
            Array.append positions [| nan * 1.0<m>; 1.0<m> |]

        let f = Stat.regression U.second U.metre xs ys

        Assert.Equal(50, f.N)
        Assert.Equal(2, f.Dropped)
        close 1e-9 3.5 (float f.Slope)

    /// Both ways of having no line at all. Raising is the decision (D47): the alternative is a
    /// NaN slope that renders as an empty layer with no explanation.
    [<Fact>]
    let ``a fit with fewer than two finite pairs raises`` () =
        let e =
            Assert.Throws<System.Exception>(fun () -> Stat.regression U.second U.metre [| 1.0<s> |] [| 1.0<m> |] |> ignore)

        Assert.Contains("at least two finite", e.Message)

    [<Fact>]
    let ``a fit whose x does not vary raises, and the message says which value`` () =
        let xs = Array.create 5 2.0<s>
        let ys = Array.init 5 (fun i -> float i * 1.0<m>)

        let e =
            Assert.Throws<System.Exception>(fun () -> Stat.regression U.second U.metre xs ys |> ignore)

        Assert.Contains("needs x to vary", e.Message)
        Assert.Contains("2", e.Message)
        Assert.Contains("[s]", e.Message)

    /// D47's escape hatch, exercised. Outside the scope that knows the measure the slope is an
    /// existential — but the witness travelled with it, so it can still be *printed* correctly
    /// by code that cannot name its type.
    [<Fact>]
    let ``the packed slope still knows it is a velocity`` () =
        let f = Stat.regression U.second U.metre times positions

        let shown =
            (Stat.slopeQuantity f)
                .Accept
                { new IQuantityConsumer<string * Dim> with
                    member _.Apply<[<Measure>] 'u>(u: U<'u>, x: float<'u>) = Num.upTo 3 (float x), u.Dim }

        Assert.Equal(("3.5", Dim.sub Dim.length Dim.time), shown)

    /// A perfect fit through data with no y variation is not a division by zero. The residuals
    /// are all zero, so the fit is perfect, and 1.0 is the only answer consistent with that.
    [<Fact>]
    let ``a fit through constant y has zero slope and R2 of one`` () =
        let f =
            Stat.regression U.second U.metre times (Array.create times.Length 4.0<m>)

        close 1e-12 0.0 (float f.Slope)
        close 1e-12 4.0 (float f.Intercept)
        Assert.Equal(1.0, f.R2)

// ======================================================================================
// bin — a histogram
// ======================================================================================

module Binning =

    [<Fact>]
    let ``every value lands in exactly one bin`` () =
        let h = Stat.bin U.metre heights

        Assert.Equal(heights.Length, h.Total)
        Assert.Equal(0, h.Dropped)

    [<Fact>]
    let ``there is one more edge than there are bins, and they are evenly spaced`` () =
        let h = Stat.binWith U.metre 17 heights

        Assert.Equal(17, h.Counts.Length)
        Assert.Equal(18, h.Edges.Length)

        let w = float h.Width

        for i in 0 .. h.Edges.Length - 2 do
            close 1e-9 w (float h.Edges.[i + 1] - float h.Edges.[i])

    /// The top edge is closed, so the sample maximum is inside the histogram rather than in a
    /// phantom bin past the end. This is the off-by-one every histogram implementation gets
    /// wrong once.
    [<Fact>]
    let ``the largest value is in the last bin, not past it`` () =
        let h = Stat.binWith U.metre 4 [| 0.0<m>; 1.0<m>; 2.0<m>; 3.0<m>; 4.0<m> |]

        Assert.Equal(0, h.Dropped)
        Assert.Equal(5, h.Total)
        Assert.True(h.Counts.[3] >= 1, "the maximum should be counted in the last bin")

    /// A closed form, and a reminder of what "the data's own range" means: ten values `0 … 9`
    /// binned into ten bins span `0 … 9`, not `0 … 10`, so each bin is **0.9** wide. One value
    /// still lands in each — including the last, which the closed top edge catches.
    [<Fact>]
    let ``evenly spaced data over its own range fills one value per bin`` () =
        let xs = Array.init 10 (fun i -> float i * 1.0<m>)
        let h = Stat.binWith U.metre 10 xs

        Assert.Equal<int[]>([| 1; 1; 1; 1; 1; 1; 1; 1; 1; 1 |], h.Counts)
        close 1e-9 0.9 (float h.Width)

        // The same ten values over a *stated* domain one wider give the unit-width bins the
        // intuition reaches for first.
        let stated = Stat.binIn U.metre 10 (0.0<m>, 10.0<m>) xs
        Assert.Equal<int[]>([| 1; 1; 1; 1; 1; 1; 1; 1; 1; 1 |], stated.Counts)
        close 1e-9 1.0 (float stated.Width)

    /// A stated domain narrower than the data drops what falls outside it and says how much —
    /// rather than folding it into the end bins, where it draws a spike that is an artefact of
    /// the domain rather than a feature of the data.
    [<Fact>]
    let ``a stated domain drops what falls outside it and reports the count`` () =
        let xs = Array.init 11 (fun i -> float i * 1.0<m>)
        let h = Stat.binIn U.metre 5 (2.0<m>, 7.0<m>) xs

        Assert.Equal(6, h.Total)
        Assert.Equal(5, h.Dropped)

    /// Freedman–Diaconis divides by the interquartile range, so all-ties data asks it for
    /// infinitely many bins. Sturges depends on `n` alone and therefore always answers.
    [<Fact>]
    let ``the bin count falls back to Sturges when the interquartile range is zero`` () =
        let ties = Array.create 64 3.0<m>

        // ceil(log2 64) + 1 = 7.
        Assert.Equal(7, Stat.binCountFor ties)

    /// A histogram of one distinct value is still a histogram: the domain is widened rather than
    /// collapsed, exactly as `Scale.paddedDomain` widens a single-valued axis.
    [<Fact>]
    let ``a sample with one distinct value still bins`` () =
        let h = Stat.binWith U.metre 3 (Array.create 8 5.0<m>)

        Assert.Equal(8, h.Total)
        Assert.True(float h.Width > 0.0, "a degenerate domain must be widened, not collapsed")

// ======================================================================================
// count — the categorical counterpart
// ======================================================================================

module Counting =

    /// First-appearance order, which is `Levels.ofSeq`'s (D45). The two have to agree or a
    /// counted axis and a plotted one put the same category in different bands.
    [<Fact>]
    let ``count is in first-appearance order and agrees with Levels`` () =
        let keys = [| "washout"; "control"; "treated"; "control"; "washout"; "control" |]
        let pairs = Stat.count keys

        Assert.Equal<string[]>([| "washout"; "control"; "treated" |], pairs |> Array.map fst)
        Assert.Equal<int[]>([| 2; 3; 1 |], pairs |> Array.map snd)
        Assert.Equal<string[]>((Levels.ofSeq keys).Values, pairs |> Array.map fst)

    [<Fact>]
    let ``the counts sum to the number of rows`` () =
        let keys = Array.init 97 (fun i -> i % 5)
        Assert.Equal(97, Stat.count keys |> Array.sumBy snd)

// ======================================================================================
// ecdf
// ======================================================================================

module EmpiricalCdf =

    [<Fact>]
    let ``F is non-decreasing and ends at exactly one`` () =
        let e = Stat.ecdf U.metre heights

        Assert.Equal(1.0, Array.last e.F)
        Assert.Equal(heights.Length, e.N)

        for i in 0 .. e.F.Length - 2 do
            Assert.True(e.F.[i] < e.F.[i + 1], "F must be strictly increasing over distinct values")
            Assert.True(float e.X.[i] < float e.X.[i + 1], "X must be strictly increasing")

    /// The closed form: four distinct values, F = 1/4, 2/4, 3/4, 4/4.
    [<Fact>]
    let ``F of a four-point sample is the quarters`` () =
        let e = Stat.ecdf U.metre [| 3.0<m>; 1.0<m>; 4.0<m>; 2.0<m> |]

        Assert.Equal<float[]>([| 0.25; 0.5; 0.75; 1.0 |], e.F)
        Assert.Equal<float[]>([| 1.0; 2.0; 3.0; 4.0 |], e.X |> Array.map float)

    /// Ties are one point at the *top* of their step, which is what makes this the
    /// right-continuous function it is defined to be rather than a staircase with a landing in
    /// the middle of each riser.
    [<Fact>]
    let ``ties collapse to one point carrying the higher F`` () =
        let e = Stat.ecdf U.metre [| 1.0<m>; 2.0<m>; 2.0<m>; 2.0<m>; 3.0<m> |]

        Assert.Equal<float[]>([| 1.0; 2.0; 3.0 |], e.X |> Array.map float)
        Assert.Equal<float[]>([| 0.2; 0.8; 1.0 |], e.F)

    [<Fact>]
    let ``non-finite values are dropped and counted`` () =
        let e = Stat.ecdf U.metre [| 1.0<m>; nan * 1.0<m>; 2.0<m>; infinity * 1.0<m> |]

        Assert.Equal(2, e.N)
        Assert.Equal(2, e.Dropped)
        Assert.Equal(1.0, Array.last e.F)

// ======================================================================================
// density — and the reciprocal measure, which is the interesting claim
// ======================================================================================

module Kde =

    /// **The closed form.** A KDE of a single observation, evaluated at that observation, is the
    /// standard normal kernel at zero scaled by the bandwidth: `1 / (h·sqrt(2π))`. Nothing about
    /// the sample can hide an error in the normalisation.
    [<Fact>]
    let ``a one-point estimate peaks at one over h root two pi`` () =
        let h = 0.25<m>
        let d = Stat.densityWith U.metre h 3 [| 0.0<m> |]

        // Three points: -3h, 0, +3h.
        close 1e-12 0.0 (float d.X.[1])
        close 1e-12 (1.0 / (0.25 * sqrt (2.0 * System.Math.PI))) (float d.Y.[1])

    /// The estimate is a probability density, so it integrates to one — the property that makes
    /// the reciprocal measure the *right* one rather than merely a consistent one. Trapezoid,
    /// over the grid the estimate itself chose.
    [<Fact>]
    let ``a density integrates to one over its own grid`` () =
        let d = Stat.density U.metre heights

        let area =
            Array.init (d.X.Length - 1) (fun i ->
                (float d.Y.[i] + float d.Y.[i + 1]) * 0.5 * (float d.X.[i + 1] - float d.X.[i]))
            |> Array.sum

        close 5e-3 1.0 area

    /// **The measure claim, checked by compiling it.** A density over metres is per metre. If
    /// `Density.Y` were ever weakened to `float<'u>` or to a bare `float`, this binding would
    /// stop compiling — and the runtime half shows the witness agrees, so an axis over it labels
    /// itself without anyone writing the reciprocal down.
    [<Fact>]
    let ``the density of a metre sample is in reciprocal metres`` () =
        let d = Stat.density U.metre heights
        let atGrid: float<1 / m> = d.Y.[64]

        Assert.True(float atGrid >= 0.0)
        Assert.Equal(Dim.neg Dim.length, d.DensityUnit.Dim)

        let axis =
            Axis.auto UnitStyle.Unicode "f" (Scale.linear d.DensityUnit (0.0</m>, 1.0</m>))

        Assert.Equal("m⁻¹", axis.UnitText)

    /// Silverman's rule is `0.9 · min(sigma, IQR/1.34) · n^(-1/5)`, and every term in it is
    /// homogeneous of degree one in the data — so doubling the sample doubles the bandwidth,
    /// exactly. A closed form that does not depend on the distribution.
    [<Fact>]
    let ``the bandwidth scales with the data`` () =
        let a = Stat.bandwidth heights
        let b = Stat.bandwidth (heights |> Array.map (fun v -> v * 2.0))

        close 1e-9 (2.0 * float a) (float b)

    /// A distribution concentrated on one point has no density to estimate, and `bandwidth`
    /// answers zero rather than pretending otherwise. The refusal is at the point of use.
    [<Fact>]
    let ``a sample with no spread has no bandwidth and no density`` () =
        let ties = Array.create 20 3.0<m>

        Assert.Equal(0.0, float (Stat.bandwidth ties))
        Assert.Throws<System.ArgumentException>(fun () -> Stat.density U.metre ties |> ignore)
        |> ignore

    [<Fact>]
    let ``the grid runs three bandwidths past the data at each end`` () =
        let d = Stat.densityWith U.metre 0.5<m> 64 [| -1.0<m>; 1.0<m> |]

        close 1e-9 -2.5 (float d.X.[0])
        close 1e-9 2.5 (float (Array.last d.X))

// ======================================================================================
// The charts the stats draw
// ======================================================================================

module Charts =

    let private yScaleOf (fig: Figure) =
        snd (Erase.seriesScales fig.Panels.[0].Layers.[0].Data)

    let private xScaleOf (fig: Figure) =
        fst (Erase.seriesScales fig.Panels.[0].Layers.[0].Data)

    /// A histogram partitions an interval, so the bars are full width and the x domain is the
    /// outer edges exactly. Padding it would draw a piece of the axis that no bin covers.
    [<Fact>]
    let ``a histogram is full-width bars from zero over its exact edges`` () =
        let h = Stat.binWith U.metre 12 heights
        let fig = Chart.ofHistogram h

        match fig.Panels.[0].Layers.[0].Geom with
        | GeomBar(baseline, width) ->
            Assert.Equal(1.0, width)
            // The data value zero (D97): where it lands on the panel is `Geom.project`'s business,
            // against whatever domain the panel ends up with.
            Assert.Equal(0.0, baseline)
        | g -> failwithf "expected bars, got %A" g

        let xs = xScaleOf fig
        close 1e-9 (float h.Edges.[0]) (fst xs.Domain)
        close 1e-9 (float (Array.last h.Edges)) (snd xs.Domain)

    /// D50 in a second mark: a count is read as a length from zero, so zero is the bottom of the
    /// axis exactly — not zero minus five percent, which would draw a strip of negative count.
    [<Fact>]
    let ``a histogram's count axis starts at exactly zero and carries no unit`` () =
        let fig = Chart.histogram U.metre heights
        let ys = yScaleOf fig

        Assert.Equal(0.0, fst ys.Domain)
        Assert.Equal(Dim.zero, ys.Dim)
        Assert.Equal("", ys.Symbol)

    /// The claim the type made, now visible on a figure: the y axis of a density figure is in
    /// reciprocal x units, and it labels itself off a witness nobody wrote down.
    [<Fact>]
    let ``a density figure's y axis is in reciprocal x units`` () =
        let fig = Chart.density U.metre heights
        let ys = yScaleOf fig

        Assert.Equal(Dim.neg Dim.length, ys.Dim)
        Assert.Equal(0.0, fst ys.Domain)

    /// **The consequence a user actually meets**, and the reason the reciprocal measure earns
    /// its keep: a density curve and the sample it summarises are different physical dimensions,
    /// so they cannot share a y axis, and the library says so instead of drawing it.
    [<Fact>]
    let ``a density layer cannot share a panel with the sample it summarises`` () =
        let fig =
            Chart.density U.metre heights
            |> Chart.addScatter U.metre U.metre heights heights

        let e =
            Assert.Throws<System.Exception>(fun () ->
                Chart.render SvgBackend.render Chart.defaultSize fig |> ignore)

        Assert.Contains("cannot share an axis", e.Message)

    /// An ECDF's range is known before the data is.
    [<Fact>]
    let ``an ecdf figure's y axis is exactly zero to one`` () =
        let fig = Chart.ecdf U.metre heights
        let ys = yScaleOf fig

        Assert.Equal((0.0, 1.0), ys.Domain)
        Assert.Equal(Dim.zero, ys.Dim)

    /// Two points per distinct value — the foot and the head of each riser — because a line
    /// through one point per value interpolates data between observations that was not observed.
    [<Fact>]
    let ``an ecdf is drawn as a staircase, two points per distinct value`` () =
        let e = Stat.ecdf U.metre [| 1.0<m>; 2.0<m>; 3.0<m> |]
        let s, geom = Chart.ecdfSeries "" e

        Assert.Equal(GeomLine, geom)
        Assert.Equal<float[]>([| 1.0; 1.0; 2.0; 2.0; 3.0; 3.0 |], s.X |> Array.map float)

        Assert.Equal<float[]>(
            [| 0.0
               1.0 / 3.0
               1.0 / 3.0
               2.0 / 3.0
               2.0 / 3.0
               1.0 |],
            s.Y
        )

    [<Fact>]
    let ``Chart.addFit adds one line layer over the range of the data`` () =
        let fig =
            Chart.scatter U.second U.metre times positions
            |> Chart.addFit U.second U.metre times positions

        Assert.Equal(2, fig.Panels.[0].Layers.Length)
        Assert.Equal(GeomLine, fig.Panels.[0].Layers.[1].Geom)
        Assert.Equal(2, Erase.seriesLength fig.Panels.[0].Layers.[1].Data)

    [<Fact>]
    let ``Chart.Category.count draws one bar per level, in first-appearance order`` () =
        let keys = [| "b"; "a"; "b"; "c"; "b" |]
        let fig = Chart.Category.count keys
        let xs = xScaleOf fig

        match xs.Kind with
        | ScaleKind.Ordinal levels -> Assert.Equal<string[]>([| "b"; "a"; "c" |], levels)
        | k -> failwithf "expected an ordinal axis, got %A" k

        Assert.Equal(0.0, fst (yScaleOf fig).Domain)

// ======================================================================================
// The block — D47's answer to "where does a stat sit"
// ======================================================================================

module Block =

    type private Row =
        { Time: float<s>
          Displacement: float<m>
          Sample: string }

    let private rows =
        Array.init 40 (fun i ->
            { Time = float i * 0.1<s>
              Displacement = 3.5<m / s> * (float i * 0.1<s>) + 2.0<m>
              Sample = (if i % 2 = 0 then "A" else "B") })

    /// `fit` appends. Layer 0 is still the data.
    [<Fact>]
    let ``fit adds a second layer and keeps the points in front of it`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.Time)
                y U.metre (fun r -> r.Displacement)
                fit
            }

        let layers = fig.Panels.[0].Layers
        Assert.Equal(2, layers.Length)
        Assert.Equal(GeomPoint, layers.[0].Geom)
        Assert.Equal(GeomLine, layers.[1].Geom)
        Assert.Equal(2, Erase.seriesLength layers.[1].Data)

    /// **D53's near-miss, from the other side.** The legend entry — the layer's name *and* its
    /// channels' levels — sits on the block's first layer. A fitted line prepended rather than
    /// appended would take `ShowInLegend` with it, and since a fit layer carries no channels the
    /// figure would render with both levels silently missing from the legend.
    [<Fact>]
    let ``fit does not take the legend away from a categorical channel`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.Time)
                y U.metre (fun r -> r.Displacement)
                color (fun r -> r.Sample)
                fit
            }

        // The levels first, because they are the thing that goes missing. Under the prepended
        // spelling this figure still renders — with a legend box and nothing in it — so the
        // assertion that catches the regression has to come before the ones about indices, which
        // would otherwise fail first and for the wrong reason.
        let scene = Chart.render SvgBackend.render Chart.defaultSize fig
        Assert.Contains(">A<", scene)
        Assert.Contains(">B<", scene)

        let layers = fig.Panels.[0].Layers
        Assert.Equal<bool[]>([| true; false |], layers |> Array.map (fun l -> l.ShowInLegend))
        Assert.Empty(layers.[1].Channels)

    /// The fitted layer shares the block's base style, because D54 makes the layers of a block
    /// one series and a fit is a statement *about* that series rather than a second one.
    [<Fact>]
    let ``the fitted line takes the block's own colour, and fitWith restyles it`` () =
        let plain =
            plot rows {
                x U.second (fun r -> r.Time)
                y U.metre (fun r -> r.Displacement)
                fit
            }

        let layers = plain.Panels.[0].Layers
        Assert.Equal(layers.[0].Style.Color, layers.[1].Style.Color)

        let dashed =
            plot rows {
                x U.second (fun r -> r.Time)
                y U.metre (fun r -> r.Displacement)
                fitWith (Geom.withDash [| 4.0<px>; 3.0<px> |])
            }

        Assert.Equal(2, (dashed.Panels.[0].Layers.[1].Style.Dash).Length)
        Assert.Empty(dashed.Panels.[0].Layers.[0].Style.Dash)

    [<Fact>]
    let ``a histogram block needs no y channel and draws bars`` () =
        let fig =
            plot rows {
                x U.metre (fun r -> r.Displacement)
                histogram
            }

        Assert.Equal(1, fig.Panels.[0].Layers.Length)

        match fig.Panels.[0].Layers.[0].Geom with
        | GeomBar(_, width) -> Assert.Equal(1.0, width)
        | g -> failwithf "expected bars, got %A" g

    [<Fact>]
    let ``histogramWith takes the bin count and densityWith the grid size`` () =
        let binned =
            plot rows {
                x U.metre (fun r -> r.Displacement)
                histogramWith 6
            }

        Assert.Equal(6, Erase.seriesLength binned.Panels.[0].Layers.[0].Data)

        let smooth =
            plot rows {
                x U.metre (fun r -> r.Displacement)
                densityWith 32
            }

        Assert.Equal(32, Erase.seriesLength smooth.Panels.[0].Layers.[0].Data)

    /// The block's theme reaches a stat's layer, which it would not if `runDistribution` simply
    /// returned what `Chart` built.
    [<Fact>]
    let ``a stat block still takes the block's theme and name`` () =
        let fig =
            plot rows {
                x U.metre (fun r -> r.Displacement)
                density
                name "heights"
                theme Theme.publication
            }

        Assert.Equal(Theme.publication, fig.Theme)
        Assert.Equal("heights", fig.Panels.[0].Layers.[0].Name)
        Assert.Equal(Geom.styleFor Theme.publication 0, fig.Panels.[0].Layers.[0].Style)

    /// **The three refusals, which are D47's finding rather than defensive coding.** A
    /// distribution stat manufactures y and changes the row count, so a y channel, an encoding
    /// channel and a `layer` are each a request the block cannot honour — and silently dropping
    /// any of them lets a caller believe it was drawn.
    [<Fact>]
    let ``a distribution block refuses a y channel`` () =
        let e =
            Assert.Throws<System.Exception>(fun () ->
                plot rows {
                    x U.metre (fun r -> r.Displacement)
                    y U.second (fun r -> r.Time)
                    histogram
                }
                |> ignore)

        Assert.Contains("has no y channel", e.Message)

    [<Fact>]
    let ``a distribution block refuses an encoding channel`` () =
        let e =
            Assert.Throws<System.Exception>(fun () ->
                plot rows {
                    x U.metre (fun r -> r.Displacement)
                    color (fun r -> r.Sample)
                    ecdf
                }
                |> ignore)

        Assert.Contains("changes the row count", e.Message)

    [<Fact>]
    let ``a distribution block refuses a layer`` () =
        let e =
            Assert.Throws<System.Exception>(fun () ->
                plot rows {
                    x U.metre (fun r -> r.Displacement)
                    layer GeomLine
                    density
                }
                |> ignore)

        Assert.Contains("chooses its own mark", e.Message)

    /// Gate 6, extended to the stats: a stat must not let iteration order or accumulated float
    /// noise reach the output.
    [<Fact>]
    let ``a stat block renders identically twice`` () =
        let build () =
            plot rows {
                x U.metre (fun r -> r.Displacement)
                density
            }

        let a = Chart.render SvgBackend.render Chart.defaultSize (build ())
        let b = Chart.render SvgBackend.render Chart.defaultSize (build ())
        Assert.Equal(a, b)

// ======================================================================================
// M4 phase 8 — hexbin (D93)
// ======================================================================================

module ``hexbin`` =

    let private sample = GoldenScenes.Hexbin.sample
    let private xs, ys = GoldenScenes.Hexbin.xs, GoldenScenes.Hexbin.ys
    let private h = GoldenScenes.Hexbin.h

    /// Ray casting, independent of how the stat assigned anything.
    let private inside (px: float) (py: float) (poly: (float * float)[]) =
        let mutable odd = false
        let n = poly.Length

        for k in 0 .. n - 1 do
            let x1, y1 = poly.[k]
            let x2, y2 = poly.[(k + 1) % n]

            if (y1 > py) <> (y2 > py) && px < x1 + (py - y1) * (x2 - x1) / (y2 - y1) then
                odd <- not odd

        odd

    /// **Each point lies in the hexagon the figure draws for its cell.** The drawn polygons come
    /// from `Chart.ofHexbin`'s layer, so the assignment and the geometry are checked against
    /// each other, not against a second copy of the same arithmetic.
    [<Fact>]
    let ``every point is inside the hexagon drawn for its cell`` () =
        let polygons =
            match (Chart.ofHexbin h).Panels.[0].Layers.[0].Geom with
            | GeomPolygon(px, py) -> Array.map2 Array.zip px py
            | g -> failwithf "a hexbin is drawn as %A" g

        Assert.Equal(h.X.Length, polygons.Length)

        let cellOf (x: float) (y: float) =
            polygons |> Array.tryFindIndex (inside x y)

        for i in 0 .. xs.Length - 1 do
            match cellOf (float xs.[i]) (float ys.[i]) with
            | None -> failwithf "(%g, %g) is in no drawn hexagon" (float xs.[i]) (float ys.[i])
            | Some _ -> ()

        // And the counts are the drawn hexagons' point counts.
        let counted = Array.zeroCreate polygons.Length

        for i in 0 .. xs.Length - 1 do
            match cellOf (float xs.[i]) (float ys.[i]) with
            | Some c -> counted.[c] <- counted.[c] + 1
            | None -> ()

        Assert.Equal<int[]>(h.Counts, counted)

    /// The density is a probability density in 1/(m·s): its integral over the cells is 1.
    [<Fact>]
    let ``the density integrates to one, in 1/(x·y)`` () =
        let area = h.Width * h.RowSpacing
        let total: float = h.Density |> Array.sumBy (fun d -> d * area)
        Assert.True(abs (total - 1.0) < 1e-12, sprintf "∫ = %g" total)
        Assert.Equal(2000, h.Counts |> Array.sum)

        let d: float<1 / (m * s)> = h.Density.[0]
        Assert.True(d > 0.0<_>)
        Assert.Equal("m⁻¹·s⁻¹", UnitDisplay.unitText Unicode UnitRegistry.si Prefix.none h.DensityUnit)

    /// A uniform sample has a flat density, equal to one over the region's area.
    [<Fact>]
    let ``a uniform sample has the density of its region`` () =
        let n = 120
        let gx = [| for _ in 0 .. n - 1 do for i in 0 .. n - 1 -> (float i + 0.5) / float n * 4.0<m> |]
        let gy = [| for j in 0 .. n - 1 do for _ in 0 .. n - 1 -> (float j + 0.5) / float n * 2.0<m> |]
        let u = Stat.hexbinWith U.metre U.metre 8 gx gy

        // Interior cells only: a cell on the boundary is partly outside the region.
        let interior =
            Array.init u.X.Length id
            |> Array.filter (fun k ->
                u.X.[k] > u.Width && u.X.[k] < 4.0<m> - u.Width && u.Y.[k] > u.RowSpacing && u.Y.[k] < 2.0<m> - u.RowSpacing)

        Assert.True(interior.Length > 10)

        for k in interior do
            Assert.True(abs (float u.Density.[k] - 1.0 / 8.0) < 0.1 / 8.0, sprintf "%g at (%g, %g)" (float u.Density.[k]) (float u.X.[k]) (float u.Y.[k]))

    [<Fact>]
    let ``non-finite points are dropped and counted`` () =
        let g = Stat.hexbin U.metre U.second (Array.append xs [| nan * 1.0<m> |]) (Array.append ys [| 0.0<s> |])
        Assert.Equal(1, g.Dropped)
        Assert.Equal(2000, g.N)

    [<Fact>]
    let ``golden: a hexbin with a colorbar in m⁻¹·s⁻¹ (svg)`` () =
        Golden.check "figure-hexbin.svg" (Chart.render SvgBackend.render (Chart.size 480.0 300.0) GoldenScenes.Hexbin.figure)

// ======================================================================================
// M4 phase 8 — loess
// ======================================================================================

module ``loess`` =

    /// Unevenly spaced x in seconds, from a seed.
    let private xs =
        let gaps, _ = Rng.units 60 (Rng.seeded 11UL)
        gaps |> Array.scan (fun acc g -> acc + 0.05 + g) 0.0 |> Array.map (fun v -> v * 1.0<s>)

    let private close (tol: float) (a: float) (b: float) = abs (a - b) <= tol * (1.0 + abs b)

    /// **A local fit of degree d reproduces a polynomial of degree d exactly**, at every span and
    /// on uneven spacing — the property that pins weighted least squares down, whatever the
    /// weights. Degree 1 on a line, degree 2 on a quadratic.
    [<Theory>]
    [<InlineData(1, 0.3)>]
    [<InlineData(1, 0.75)>]
    [<InlineData(2, 0.2)>]
    [<InlineData(2, 0.75)>]
    [<InlineData(2, 1.5)>]
    let ``a polynomial of the fit's degree is reproduced exactly`` (degree: int) (span: float) =
        let f (t: float<s>) =
            let t = float t
            (if degree = 1 then 3.0 - 0.7 * t else 3.0 - 0.7 * t + 0.11 * t * t) * 1.0<m>

        let at = [| 0.0<s>; 1.3<s>; 7.77<s>; xs.[xs.Length - 1] |]
        let l = Stat.loessIn U.second U.metre span degree at xs (xs |> Array.map f)

        for k in 0 .. at.Length - 1 do
            let fitted: float<m> = l.Y.[k]
            Assert.True(close 1e-9 (float fitted) (float (f at.[k])), sprintf "at %g: %g, not %g" (float at.[k]) (float fitted) (float (f at.[k])))

    /// And it is local: a quadratic fit does not reproduce a cubic, and a smaller span follows it
    /// more closely than a larger one.
    [<Fact>]
    let ``a cubic is followed more closely by a smaller span`` () =
        let f (t: float<s>) = (pown (float t - 15.0) 3 / 100.0) * 1.0<m>
        let ys = xs |> Array.map f

        let error span =
            let l = Stat.loessWith U.second U.metre span 40 xs ys
            Array.map2 (fun x y -> abs (float y - float (f x))) l.X l.Y |> Array.max

        Assert.True(error 0.75 > 1e-3, "a local quadratic reproduced a cubic")
        Assert.True(error 0.2 < error 0.75, sprintf "span 0.2: %g, span 0.75: %g" (error 0.2) (error 0.75))

    /// It smooths: over noisy samples of a sine, the curve is closer to the sine than the samples
    /// are.
    [<Fact>]
    let ``noise is smoothed toward the signal`` () =
        let noise, _ = Rng.units xs.Length (Rng.seeded 3UL)
        let truth (t: float<s>) = sin (float t / 5.0) * 1.0<m>
        let ys = Array.map2 (fun x e -> truth x + (e - 0.5) * 0.8<m>) xs noise
        let l = Stat.loessIn U.second U.metre 0.3 2 xs xs ys
        let rms (a: float<m>[]) = sqrt (Array.map2 (fun x v -> (float v - float (truth x)) ** 2.0) xs a |> Array.average)
        Assert.True(rms l.Y < 0.5 * rms ys, sprintf "curve %g against samples %g" (rms l.Y) (rms ys))

    [<Fact>]
    let ``non-finite pairs are dropped and counted, and nonsense refused`` () =
        let ys = xs |> Array.map (fun t -> float t * 1.0<m>)
        let l = Stat.loess U.second U.metre (Array.append xs [| nan * 1.0<s> |]) (Array.append ys [| 1.0<m> |])
        Assert.Equal(1, l.Dropped)
        Assert.Equal(xs.Length, l.N)
        Assert.Equal(Stat.DefaultLoessPoints, l.X.Length)
        Assert.Throws<System.ArgumentException>(fun () -> Stat.loessIn U.second U.metre 0.0 2 xs xs ys |> ignore) |> ignore
        Assert.Throws<System.ArgumentException>(fun () -> Stat.loessIn U.second U.metre 0.5 3 xs xs ys |> ignore) |> ignore

    [<Fact>]
    let ``addLoess draws the curve as a second layer`` () =
        let ys = xs |> Array.map (fun t -> sin (float t / 5.0) * 1.0<m>)
        let fig = Chart.scatter U.second U.metre xs ys |> Chart.addLoess U.second U.metre xs ys
        Assert.Equal(2, fig.Panels.[0].Layers.Length)
        Assert.Equal(Stat.DefaultLoessPoints, Erase.seriesLength fig.Panels.[0].Layers.[1].Data)

    /// The weights and the neighbourhood, pinned against the closed form of a weighted straight-
    /// line fit, computed here independently of the library's solver: q = ⌊span·n⌋ nearest
    /// points, h the q-th nearest distance, tricube weights (1 − (d/h)³)³.
    [<Theory>]
    [<InlineData(0.6)>]
    [<InlineData(1.0)>]
    [<InlineData(1.4)>]
    let ``a local line is the tricube-weighted least-squares line`` (span: float) =
        let px = [| 0.0; 0.4; 1.1; 1.5; 2.6; 3.0; 3.3; 4.9; 5.2; 6.0 |]
        let py = [| 1.0; 0.2; 2.5; 1.9; 0.4; 3.1; 2.2; 4.0; 1.0; 2.8 |]

        let expected (x0: float) =
            let n = px.Length
            let q = int (floor (span * float n + 1e-5)) |> min n
            let d = px |> Array.map (fun x -> abs (x - x0))
            let h = (Array.sort d).[q - 1] * max 1.0 span
            let w = d |> Array.map (fun di -> if di < h then (1.0 - (di / h) ** 3.0) ** 3.0 else 0.0)
            let sw = Array.sum w
            let mx = Array.fold2 (fun acc wi xi -> acc + wi * xi) 0.0 w px / sw
            let my = Array.fold2 (fun acc wi yi -> acc + wi * yi) 0.0 w py / sw
            let sxy = Array.init n (fun i -> w.[i] * (px.[i] - mx) * (py.[i] - my)) |> Array.sum
            let sxx = Array.init n (fun i -> w.[i] * (px.[i] - mx) ** 2.0) |> Array.sum
            my + sxy / sxx * (x0 - mx)

        let at = [| 0.0; 0.7; 2.0; 3.3; 5.5; 6.0 |]
        let l = Stat.loessIn U.one U.one span 1 at px py

        for k in 0 .. at.Length - 1 do
            Assert.True(close 1e-9 (float l.Y.[k]) (expected at.[k]), sprintf "at %g: %g, not %g" at.[k] (float l.Y.[k]) (expected at.[k]))

// ======================================================================================
// M4 phase 8 — bootstrap intervals (D95's second consumer)
// ======================================================================================

module ``bootstrap`` =

    /// n normal draws in metres, mean 5 m and spread 2 m, from a seed, by Box–Muller.
    let private normal (seed: uint64) (n: int) =
        let us, _ = Rng.units (2 * n) (Rng.seeded seed)
        Array.init n (fun i -> (5.0 + 2.0 * sqrt (-2.0 * log (1.0 - us.[2 * i])) * cos (2.0 * System.Math.PI * us.[2 * i + 1])) * 1.0<m>)

    let private mean (xs: float<m>[]) = Array.average xs
    let private sample = normal 1UL 40

    /// **Gate 11, the bootstrap's half.** A seed names one interval: two calls agree, so do four
    /// at once on the thread pool, and another seed gives another interval.
    [<Fact>]
    let ``the same seed gives the same interval, and another seed does not`` () =
        let a = Stat.bootstrap 42UL mean sample
        let b = Stat.bootstrap 42UL mean sample
        Assert.Equal(a, b)

        for c in Array.Parallel.init 4 (fun _ -> Stat.bootstrap 42UL mean sample) do
            Assert.Equal(a, c)

        let d = Stat.bootstrap 43UL mean sample
        Assert.True(d.Lo <> a.Lo || d.Hi <> a.Hi, "seed 43 gave seed 42's interval")
        Assert.Equal(42UL, a.Seed)

    /// It covers what it claims: over 300 independent samples, a 90% interval for the mean
    /// contains the true mean about 90% of the time. The percentile interval runs slightly narrow
    /// at n = 40, so the band is 84–95%; three binomial standard deviations either side is 85–95%.
    [<Fact>]
    let ``a 90% interval covers the mean about 90% of the time`` () =
        let trials = 300

        let hits =
            Array.init trials (fun t ->
                let i = Stat.bootstrapWith (uint64 (1000 + t)) 400 0.90 mean (normal (uint64 (5000 + t)) 40)
                i.Lo <= 5.0<m> && 5.0<m> <= i.Hi)
            |> Array.filter id
            |> Array.length

        let coverage = float hits / float trials
        Assert.True(coverage >= 0.84 && coverage <= 0.95, sprintf "coverage %g" coverage)

    /// And it is as wide as theory says: for a mean, about 2 · 1.96 · s/√n at 95%.
    [<Fact>]
    let ``a 95% interval for a mean is as wide as the normal theory says`` () =
        let i = Stat.bootstrap 7UL mean sample
        let s = sqrt ((sample |> Array.sumBy (fun x -> float ((x - mean sample) * (x - mean sample)))) / 39.0)
        let theory = 2.0 * 1.96 * s / sqrt 40.0
        let width = float (i.Hi - i.Lo)
        Assert.True(abs (width / theory - 1.0) < 0.15, sprintf "width %g against %g" width theory)
        Assert.Equal(mean sample, i.Estimate)
        Assert.True(i.Lo < i.Estimate && i.Estimate < i.Hi)

    /// The unit survives: a pairs bootstrap of a regression slope of metres against seconds is
    /// an interval in m/s, by type.
    [<Fact>]
    let ``a slope's interval is in metres per second`` () =
        let ts = Array.init 30 (fun i -> float i * 0.5<s>)
        let noise, _ = Rng.units 30 (Rng.seeded 9UL)
        let xs = Array.map2 (fun t e -> 1.5<m / s> * t + (e - 0.5) * 1.0<m>) ts noise
        let slope (t: float<s>[]) (x: float<m>[]) = (Stat.regression U.second U.metre t x).Slope
        let i = Stat.bootstrapPairs 3UL slope ts xs
        let lo: float<m / s> = i.Lo
        Assert.True(lo < 1.5<m / s> && 1.5<m / s> < i.Hi, sprintf "[%g, %g]" (float i.Lo) (float i.Hi))

    [<Fact>]
    let ``non-finite values are dropped before resampling, and nonsense refused`` () =
        let i = Stat.bootstrap 1UL mean (Array.append sample [| nan * 1.0<m> |])
        Assert.Equal(mean sample, i.Estimate)
        Assert.Throws<System.ArgumentException>(fun () -> Stat.bootstrapWith 1UL 0 0.95 mean sample |> ignore) |> ignore
        Assert.Throws<System.ArgumentException>(fun () -> Stat.bootstrapWith 1UL 100 1.0 mean sample |> ignore) |> ignore
