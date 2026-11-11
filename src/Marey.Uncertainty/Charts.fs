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

    /// What the legend says the bars are.
    let describe (bars: ErrorBars) (factors: float[]) : string =
        let percent (p: float) = Num.upTo 2 (100.0 * p) + " %"

        let k () =
            if factors.Length = 0 then
                ""
            else
                let lo = Num.fixedPoint 2 (Array.min factors)
                let hi = Num.fixedPoint 2 (Array.max factors)
                if lo = hi then ", k = " + lo else ", k = " + lo + " to " + hi

        match bars with
        | ErrorBars.Standard -> "standard uncertainty"
        | ErrorBars.Coverage p -> "expanded uncertainty" + k () + " (" + percent p + ")"
        | ErrorBars.CoverageAssumingNormal p -> "expanded uncertainty" + k () + " (" + percent p + ", normal)"

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
            let top = errorbarsWith bars x y

            // A statement the figure's legend already makes is not made a second time.
            let stated =
                match Array.tryLast fig.Panels with
                | Some p -> p.Layers |> Array.filter (fun l -> l.ShowInLegend) |> Array.map (fun l -> l.Name) |> Set.ofArray
                | None -> Set.empty

            let quiet (l: Layer) = if l.ShowInLegend && stated.Contains l.Name then { l with ShowInLegend = false } else l
            Chart.add { top with Panels = top.Panels |> Array.map (fun p -> { p with Layers = Array.map quiet p.Layers }) } fig

        /// `errorbars` as the next series of a figure, in the next colour.
        let addErrorbars (x: seq<Uncertain<'ux>>) (y: seq<Uncertain<'uy>>) (fig: Figure) : Figure =
            addErrorbarsWith ErrorBars.Standard x y fig

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
