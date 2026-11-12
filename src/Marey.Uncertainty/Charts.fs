namespace Marey

open System

/// What the error bars of uncertain values span.
[<RequireQualifiedAccess>]
type ErrorBars =
    /// One standard uncertainty either side (k = 1).
    | Standard
    /// The expanded uncertainty for a coverage probability, `ErrorBars.Coverage 0.95`, with each
    /// value's factor read from Student's t at its own effective degrees of freedom.
    | Coverage of float
    /// The expanded uncertainty for a coverage probability with the factor of a normal
    /// distribution, whatever the degrees of freedom: the caller's assumption.
    | CoverageAssumingNormal of float

/// One axis of a chart of uncertain values: the witness, the values, the half-length of each
/// bar and the coverage factor behind it.
module internal ChartAxis =

    type Axis<[<Measure>] 'u> =
        { Unit: U<'u>
          Values: float<'u>[]
          Bars: float<'u>[]
          Factors: float[] }

    let ofValues (bars: ErrorBars) (xs: Uncertain<'u>[]) : Axis<'u> =
        // The axis is in the first unit anyone stated, and a value in another unit of the
        // measure is read in it.
        let unit =
            xs
            |> Array.tryFind (fun x -> x.Unit.Stated)
            |> Option.map (fun x -> x.Unit)
            |> Option.defaultWith Parts.unstated

        let expanded =
            xs
            |> Array.map (fun x ->
                let struct (_, scale) = Parts.aligned unit x.Unit x.Value

                let e =
                    match bars with
                    | ErrorBars.Standard ->
                        { Estimate = x.Value
                          Expanded = x.Uncertainty
                          Unit = x.Unit
                          CoverageFactor = 1.0
                          Coverage = nan
                          Dof = nan }
                    // A value with no uncertainty has no bar, and no factor to ask for.
                    | _ when float x.Uncertainty = 0.0 ->
                        { Estimate = x.Value
                          Expanded = x.Uncertainty
                          Unit = x.Unit
                          CoverageFactor = nan
                          Coverage = nan
                          Dof = nan }
                    | ErrorBars.Coverage p -> Uncertain.expanded p x
                    | ErrorBars.CoverageAssumingNormal p -> Uncertain.expandedAssumingNormal p x

                struct (e, scale))

        { Unit = unit
          Values = Array.init xs.Length (fun i -> let struct (v, _) = Parts.aligned unit xs.[i].Unit xs.[i].Value in v)
          Bars = expanded |> Array.map (fun struct (e, scale) -> e.Expanded * scale)
          Factors =
            expanded
            |> Array.filter (fun struct (e, _) -> float e.Expanded > 0.0)
            |> Array.map (fun struct (e, _) -> e.CoverageFactor) }

    let hasBars (axis: Axis<'u>) : bool = axis.Bars |> Array.exists (fun b -> float b > 0.0)

    /// The ends of the bars, for a domain that holds them.
    let span (axis: Axis<'u>) : float<'u>[] =
        Array.append
            (Array.init axis.Values.Length (fun i -> axis.Values.[i] - axis.Bars.[i]))
            (Array.init axis.Values.Length (fun i -> axis.Values.[i] + axis.Bars.[i]))

    /// What the legend says the bars are, or a band: `of` names whose uncertainty it is.
    let describeOf (subject: string) (bars: ErrorBars) (factors: float[]) : string =
        let percent (p: float) = Num.upTo 2 (100.0 * p) + " %"

        let k () =
            if factors.Length = 0 then
                ""
            else
                let lo = Num.fixedPoint 2 (Array.min factors)
                let hi = Num.fixedPoint 2 (Array.max factors)
                if lo = hi then ", k = " + lo else ", k = " + lo + " to " + hi

        let subject = if subject = "" then "" else " of " + subject

        match bars with
        | ErrorBars.Standard -> "standard uncertainty" + subject
        | ErrorBars.Coverage p -> "expanded uncertainty" + subject + k () + " (" + percent p + ")"
        | ErrorBars.CoverageAssumingNormal p -> "expanded uncertainty" + subject + k () + " (" + percent p + ", normal)"

    /// What the legend says the bars are.
    let describe (bars: ErrorBars) (factors: float[]) : string = describeOf "" bars factors

    /// `top` as the next series of `fig`, without a statement the figure's legend already makes.
    let addQuietly (top: Figure) (fig: Figure) : Figure =
        let stated =
            match Array.tryLast fig.Panels with
            | Some p -> p.Layers |> Array.filter (fun l -> l.ShowInLegend) |> Array.map (fun l -> l.Name) |> Set.ofArray
            | None -> Set.empty

        let quiet (l: Layer) = if l.ShowInLegend && stated.Contains l.Name then { l with ShowInLegend = false } else l
        Chart.add { top with Panels = top.Panels |> Array.map (fun p -> { p with Layers = Array.map quiet p.Layers }) } fig

/// The figure of a Monte Carlo check.
module internal MonteCarloChart =

    /// The law of propagation's density for the output: normal, or Student's t scaled by u at the
    /// degrees of freedom its interval was read at.
    let density (c: MonteCarloCheck<'u>) (x: float) : float =
        let y = float c.Linear.Value
        let u = float c.Linear.Uncertainty
        let t = (x - y) / u
        let nu = c.LinearInterval.Dof

        if Double.IsPositiveInfinity nu then
            exp (-0.5 * t * t) / (sqrt (2.0 * Math.PI) * u)
        else
            exp (Coverage.logGamma ((nu + 1.0) / 2.0) - Coverage.logGamma (nu / 2.0)) / (sqrt (nu * Math.PI) * u)
            * Math.Pow(1.0 + t * t / nu, -(nu + 1.0) / 2.0)

/// Charts of uncertain values. `open Marey` adds these to `Chart`.
[<AutoOpen>]
module UncertaintyCharts =

    /// Charts that draw the error bars their values carry.
    [<RequireQualifiedAccess>]
    module Chart =

        /// `errorbars` with a choice of what the bars span: `Chart.errorbarsWith (ErrorBars.Coverage 0.95) t x`.
        /// The legend states the choice and the coverage factor.
        let errorbarsWith (bars: ErrorBars) (x: seq<Uncertain<'ux>>) (y: seq<Uncertain<'uy>>) : Figure =
            let xs = Array.ofSeq x
            let ys = Array.ofSeq y

            if xs.Length <> ys.Length then
                invalidArg "y" (sprintf "Marey: %d x values and %d y values." xs.Length ys.Length)

            let ax = ChartAxis.ofValues bars xs
            let ay = ChartAxis.ofValues bars ys
            let domain (span: float<'u>[]) = Scale.paddedDomain 0.05 span |> Option.defaultValue (Parts.tag 0.0, Parts.tag 1.0)

            let s: Series<'ux, 'uy> =
                { Name = ""
                  X = ax.Values
                  Y = ay.Values
                  XScale = Scale.linear ax.Unit (domain (ChartAxis.span ax))
                  YScale = Scale.linear ay.Unit (domain (ChartAxis.span ay)) }

            let points = Chart.ofSeries GeomPoint s
            let layerOf (g: Geom) = (Chart.ofSeries g s).Panels.[0].Layers.[0]
            let statement = ChartAxis.describe bars (Array.append ax.Factors ay.Factors)

            // The first bars drawn carry the statement into the legend, and the others share
            // their colour and stay out of it. The points come last, so a name given to the
            // series afterwards is theirs.
            let barLayers =
                [| if ChartAxis.hasBars ay then
                       yield layerOf (Geom.errorY s ay.Bars)
                   if ChartAxis.hasBars ax then
                       yield layerOf (Geom.errorX s ax.Bars) |]
                |> Array.mapi (fun i l ->
                    if i = 0 then
                        { l with Name = statement; ShowInLegend = true }
                    else
                        { l with Name = ""; ShowInLegend = false })

            let pointLayer = { points.Panels.[0].Layers.[0] with ShowInLegend = false }

            // The statement puts a legend on every such figure, so it goes to the corner that
            // hides the least of the data.
            { points with
                Panels =
                    [| { points.Panels.[0] with
                           Layers = Array.append barLayers [| pointLayer |]
                           Legend = LegendBest } |] }

        /// Points with the error bars their values carry, on x and on y: one standard uncertainty
        /// either side, which the legend says. A value with no uncertainty gets no bar, so an
        /// exact x (`Uncertain.exactly t`) leaves bars on y alone.
        let errorbars (x: seq<Uncertain<'ux>>) (y: seq<Uncertain<'uy>>) : Figure = errorbarsWith ErrorBars.Standard x y

        /// `errorbarsWith` as the next series of a figure, in the next colour.
        let addErrorbarsWith (bars: ErrorBars) (x: seq<Uncertain<'ux>>) (y: seq<Uncertain<'uy>>) (fig: Figure) : Figure =
            ChartAxis.addQuietly (errorbarsWith bars x y) fig

        /// `errorbars` as the next series of a figure, in the next colour.
        let addErrorbars (x: seq<Uncertain<'ux>>) (y: seq<Uncertain<'uy>>) (fig: Figure) : Figure =
            addErrorbarsWith ErrorBars.Standard x y fig

        /// `fitBand` with a choice of what the band spans: `Chart.fitBandWith (ErrorBars.Coverage 0.95) fit`,
        /// its factor read at the curve's effective degrees of freedom.
        let fitBandWith (band: ErrorBars) (fit: IFittedCurve<'ux, 'uy>) : Figure =
            let lo, hi = fit.Span
            let count = 101
            let xs = Array.init count (fun i -> lo + (hi - lo) * (float i / float (count - 1)))
            let axis = ChartAxis.ofValues band (Array.map fit.ValueAt xs)
            let low = Array.init count (fun i -> axis.Values.[i] - axis.Bars.[i])
            let high = Array.init count (fun i -> axis.Values.[i] + axis.Bars.[i])
            let domain (span: float<'u>[]) = Scale.paddedDomain 0.05 span |> Option.defaultValue (Parts.tag 0.0, Parts.tag 1.0)

            let s: Series<'ux, 'uy> =
                { Name = ""
                  X = xs
                  Y = axis.Values
                  XScale = Scale.linear fit.XWitness (domain xs)
                  YScale = Scale.linear axis.Unit (domain (Array.append low high)) }

            let line = Chart.ofSeries GeomLine s
            let lineLayer = { line.Panels.[0].Layers.[0] with ShowInLegend = false }

            // The band under the curve, carrying the statement; the curve last, so a name given
            // to the series afterwards is its.
            let layers =
                if ChartAxis.hasBars axis then
                    let bandLayer = (Chart.ofSeries (Geom.ribbon s low high) s).Panels.[0].Layers.[0]

                    [| { bandLayer with Name = ChartAxis.describeOf "the fit" band axis.Factors; ShowInLegend = true }
                       lineLayer |]
                else
                    [| lineLayer |]

            { line with
                Panels =
                    [| { line.Panels.[0] with
                           Layers = layers
                           Legend = LegendBest } |] }

        /// **A fitted curve with the band of its standard uncertainty** over the x its points
        /// spanned, `Chart.fitBand (Fit.line t x)`. The band is the uncertainty of the curve at each
        /// x, from all its coefficients together, and the legend says so.
        let fitBand (fit: IFittedCurve<'ux, 'uy>) : Figure = fitBandWith ErrorBars.Standard fit

        /// **What a Monte Carlo check found, drawn**: a histogram of the model's values, the law
        /// of propagation's distribution over it as a curve (normal, or t at its degrees of
        /// freedom) in the next colour, and each method's coverage interval as a pair of vertical
        /// lines in its colour, the linear one dashed. Where the curve and the bars part, the
        /// model is too curved for the law of propagation. The histogram spans both intervals and
        /// half as much again either side; the legend gives the share of values beyond it.
        let monteCarlo (c: MonteCarloCheck<'u>) : Figure =
            let y = float c.Linear.Value
            let big = float c.LinearInterval.Expanded
            let lo = min (float c.Low) (y - big)
            let hi = max (float c.High) (y + big)
            let pad = 0.5 * (hi - lo)
            let first = float c.Values.[0]
            let last = float c.Values.[c.Values.Length - 1]
            // Half again either side where trials fell there, and both intervals whole, with a
            // margin, where one reaches past them: a linear interval below zero for a value that
            // cannot be negative is what the figure is for.
            let margin = 0.05 * (hi - lo)
            let from, upto = min (lo - margin) (max first (lo - pad)), max (hi + margin) (min last (hi + pad))
            let from, upto = if upto > from then from, upto else lo - 1.0, hi + 1.0
            let h = Stat.binIn c.Unit 60 (Parts.tag from, Parts.tag upto) c.Values
            let width = float h.Width
            let trials = float c.Values.Length
            let outside = trials - float h.Total

            let name =
                let e = int (Math.Round(log10 trials))
                let count = if Math.Pow(10.0, float e) = trials then "10" + Num.superscript e else string c.Values.Length
                let beyond = if outside > 0.0 then ", " + Num.upTo 2 (100.0 * outside / trials) + " % beyond" else ""
                "Monte Carlo, " + count + " trials" + beyond

            let xs = Array.init 201 (fun i -> from + (upto - from) * float i / 200.0)
            let curve = xs |> Array.map (fun x -> trials * width * MonteCarloChart.density c x)
            let top = 1.05 * max (Array.max h.Frequencies) (Array.max curve)

            let verticals (a: float) (b: float) : Series<'u, 1> * Geom =
                let s: Series<'u, 1> =
                    { Name = ""
                      X = [| Parts.tag a; Parts.tag b |]
                      Y = [| 0.0; 0.0 |]
                      XScale = Scale.linear c.Unit (Parts.tag from, Parts.tag upto)
                      YScale = Scale.linear U.one (0.0, top) }

                s, Geom.segment s s.X [| top; top |]

            let layerOf (g: Geom) (s: Series<'u, 1>) = (Chart.ofSeries g s).Panels.[0].Layers.[0]

            let bars = Chart.ofHistogram h
            let barLayer = { bars.Panels.[0].Layers.[0] with Name = name; ShowInLegend = true }
            let mcSeries, mcGeom = verticals (float c.Low) (float c.High)
            let mcLines = { layerOf mcGeom mcSeries with ShowInLegend = false }

            let curveSeries: Series<'u, 1> =
                { Name = ""
                  X = Array.map Parts.tag xs
                  Y = curve
                  XScale = Scale.linear c.Unit (Parts.tag from, Parts.tag upto)
                  YScale = Scale.linear U.one (0.0, top) }

            let curveFigure = Chart.ofSeries GeomLine curveSeries
            let curveLayer = { curveFigure.Panels.[0].Layers.[0] with Name = "law of propagation"; ShowInLegend = true }
            let linSeries, linGeom = verticals (y - big) (y + big)
            let linLines = layerOf linGeom linSeries

            let linLines = { linLines with ShowInLegend = false }

            let lower =
                { bars with Panels = [| { bars.Panels.[0] with Layers = [| barLayer; mcLines |] } |] }

            let upper =
                { curveFigure with Panels = [| { curveFigure.Panels.[0] with Layers = [| curveLayer; linLines |] } |] }

            // `Chart.add` gives the added layers the next colour's style, so the dash goes on
            // after. A histogram leaves no corner free, so the legend goes below.
            let fig = Chart.add upper lower |> Chart.withDash [ 5.0<px>; 3.0<px> ] |> Chart.withYLabel "trials"
            { fig with Panels = fig.Panels |> Array.map (fun p -> { p with Legend = LegendOutsideBottom }) }

        /// `fitBandWith` as the next series of a figure, in the next colour.
        let addFitBandWith (band: ErrorBars) (fit: IFittedCurve<'ux, 'uy>) (fig: Figure) : Figure =
            ChartAxis.addQuietly (fitBandWith band fit) fig

        /// `fitBand` as the next series of a figure: `Chart.errorbars t x |> Chart.addFitBand fit`.
        let addFitBand (fit: IFittedCurve<'ux, 'uy>) (fig: Figure) : Figure = addFitBandWith ErrorBars.Standard fit fig

/// The `plot` block's channels for uncertain values. `open Marey` adds them to the block.
[<AutoOpen>]
module UncertaintyPlot =

    /// A value's estimate and its standard uncertainty, read in the axis's unit.
    let private valueIn (unit: U<'u>) (x: Uncertain<'u>) : float<'u> =
        let struct (v, _) = Parts.aligned unit x.Unit x.Value
        v

    let private barIn (unit: U<'u>) (x: Uncertain<'u>) : float<'u> =
        let struct (_, scale) = Parts.aligned unit x.Unit x.Value
        x.Uncertainty * scale

    type PlotBuilderBase<'r> with

        /// **The x channel from uncertain values**, `xUncertain (fun r -> r.Length)`: the estimates
        /// on the axis, in the unit the values carry, and an error bar of one standard uncertainty
        /// either side of each. The legend says so.
        [<CustomOperation("xUncertain")>]
        member b.XUncertain(spec: PlotSpec, f: 'r -> Uncertain<'u>) : PlotSpec =
            let axis = ChartAxis.ofValues ErrorBars.Standard (Array.map f b.Rows)
            let spec = b.X(spec, axis.Unit, (fun r -> valueIn axis.Unit (f r)))

            if ChartAxis.hasBars axis then
                let spec = b.XError(spec, axis.Unit, (fun r -> barIn axis.Unit (f r)))
                b.ErrorLabel(spec, ChartAxis.describe ErrorBars.Standard axis.Factors)
            else
                spec

        /// **The y channel from uncertain values**, with an error bar of one standard uncertainty
        /// either side of each. See `xUncertain`.
        [<CustomOperation("yUncertain")>]
        member b.YUncertain(spec: PlotSpec, f: 'r -> Uncertain<'u>) : PlotSpec =
            let axis = ChartAxis.ofValues ErrorBars.Standard (Array.map f b.Rows)
            let spec = b.Y(spec, axis.Unit, (fun r -> valueIn axis.Unit (f r)))

            if ChartAxis.hasBars axis then
                let spec = b.YError(spec, axis.Unit, (fun r -> barIn axis.Unit (f r)))
                b.ErrorLabel(spec, ChartAxis.describe ErrorBars.Standard axis.Factors)
            else
                spec
