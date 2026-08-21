namespace Marey

/// §9 — **the quick, matplotlib-shaped front door.**
///
/// ```fsharp
/// Chart.line U.second U.metre ts positions
/// |> Chart.withTitle "Free fall"
/// |> Chart.toSvg SvgBackend.render "fall.svg" (Chart.size 800.0 500.0)
/// ```
///
/// ### Why the witnesses are arguments and cannot be anything else
///
/// §9 sketches `Chart.line (ts, positions) |> Chart.withXAxis (Axis.auto U.second)`, and the
/// witness appears in that sketch too — because §1.3 leaves no alternative. `float(s)` **is**
/// `float` at runtime; a `float(s)[]` carries no evidence that it is seconds. Something has to
/// hand the library the runtime witness, exactly once, and `U.second` is that something.
///
/// What is *not* configured is everything downstream of it: the scale, the domain, the prefix,
/// the tick values, the tick labels and both axis titles. `Chart.line U.second U.metre ts xs`
/// prints `t [s]` and `x [m]`, and the same call on data spanning 1e-9…5e-9 m prints `x [nm]`
/// with ticks reading 1, 2, 3, 4, 5 (§2.2). That is the payoff, and it survives being handed the
/// witness because the witness is the *only* thing supplied.
///
/// ### Nothing here mutates
///
/// §1.1 indicts Plotly.NET for exactly one thing this module could get wrong: "styling functions
/// *mutate* the chart (documented caveat)." Every `with*` and `add*` below returns a new
/// `Figure`; there is no `mutable` in this file and no member on any type in this package that
/// changes one. `Chart.withTitle` on a figure you have already rendered gives you a second
/// figure and leaves the first alone.
[<RequireQualifiedAccess>]
module Chart =

    let private tag<[<Measure>] 'u> (x: float) : float<'u> =
        LanguagePrimitives.FloatWithMeasure<'u> x

    /// A surface size in device pixels.
    let size (w: float) (h: float) : Extent<px> =
        { Width = w * 1.0<px>
          Height = h * 1.0<px> }

    /// A default figure size that is a sensible aspect ratio for one panel.
    let defaultSize = size 800.0 500.0

    // ---- Building series -------------------------------------------------------------------

    let private paddedOr (fraction: float) (xs: float<'u>[]) : float<'u> * float<'u> =
        match Scale.paddedDomain fraction xs with
        | Some d -> d
        | None -> tag<'u> 0.0, tag<'u> 1.0

    /// **A bar is read as a length from zero, so zero is in the domain.**
    ///
    /// A bar chart over 7.8 … 19.1 on a 7.2 … 19.7 axis draws the smallest value as a sliver
    /// and the largest as a full-height bar — a ratio of about 50:1 for data whose real ratio
    /// is 2.4:1. That is not a styling preference, it is the mark type's encoding: length
    /// *means* magnitude, and a baseline that is not zero misstates every comparison the chart
    /// exists to support. `Geom.baselineOf` already says as much for areas, and its clamp is
    /// the fallback for a domain that could not be widened, not a licence to leave one narrow.
    ///
    /// Zero enters **unpadded**, and only on the side it is missing from: all-positive data
    /// gets `0 … hi+pad`, all-negative gets `lo-pad … 0`, and data straddling zero is
    /// unaffected because zero is already inside. Padding below zero would lift the baseline
    /// off the axis, which is the thing being fixed.
    let private withZero (lo: float<'u>, hi: float<'u>) : float<'u> * float<'u> =
        min lo (tag<'u> 0.0), max hi (tag<'u> 0.0)

    /// A typed series over linear scales with 5% breathing room. The last point at which the
    /// measures are alive on this path.
    let series (name: string) (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Series<'ux, 'uy> =
        if x.Length <> y.Length then
            invalidArg "y" (sprintf "Marey: series '%s' has %d x values and %d y values." name x.Length y.Length)

        { Name = name
          X = x
          Y = y
          XScale = Scale.linear ux (paddedOr 0.05 x)
          YScale = Scale.linear uy (paddedOr 0.05 y) }

    let private figureOf (layers: Layer[]) =
        Figure.create [| Figure.panelOf layers |]

    let private layerOf (name: string) (theme: Theme) (index: int) (g: Geom) (data: PackedSeries) : Layer =
        { Name = name
          Data = data
          Geom = g
          Style = Geom.styleFor theme index
          Channels = [||]
          ShowInLegend = not (System.String.IsNullOrEmpty name) }

    // ---- Constructors ------------------------------------------------------------------------

    /// A figure built from an already-scaled typed series. The full-control door: pick the
    /// scales yourself and everything downstream still works.
    let ofSeries (g: Geom) (s: Series<'ux, 'uy>) : Figure =
        figureOf [| layerOf s.Name Theme.initial 0 g (Erase.packSeries s) |]

    /// A figure from an already-reunified `PackedSeries` — the door the encoding CE comes
    /// through. The reunifier lives in `Marey.Core` (D44 finding 3) and terminates there; this
    /// is what lets it, so nothing in `Marey.Plot` has to write a rank-2 consumer of its own.
    let ofPacked (g: Geom) (p: PackedSeries) : Figure =
        figureOf [| layerOf (Erase.seriesName p) Theme.initial 0 g p |]

    let line (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        ofSeries GeomLine (series "" ux uy x y)

    /// Scatter. **One `Markers` node for the whole series**, however many points — §5.1, "the
    /// single biggest performance decision in the IR". Verifiable with `Scene.count`.
    let scatter (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        ofSeries GeomPoint (series "" ux uy x y)

    let area (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        let s = series "" ux uy x y
        ofSeries (Geom.area s) s

    /// Bars from zero — see `withZero`. The y domain is the data's, widened to reach the
    /// baseline the marks are measured from.
    let bar (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        let s = series "" ux uy x y

        let s =
            { s with
                YScale = Scale.withDomain (withZero (paddedOr 0.05 y)) s.YScale }

        ofSeries (Geom.bar 0.7 s) s

    /// Error bars with symmetric uncertainties.
    ///
    /// `err` is `float('uy)[]` — the same measure as `y`. An uncertainty in the wrong unit does
    /// not compile, which is the whole thesis applied to the one place experimental data most
    /// often gets it wrong. The y domain is taken over `y ± err`, so the caps are inside the
    /// panel rather than clipped by it.
    let errorbar (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (err: float<'uy>[]) : Figure =
        if err.Length <> y.Length then
            invalidArg "err" (sprintf "Marey: %d y values but %d error values." y.Length err.Length)

        let span =
            Array.append (Array.init y.Length (fun i -> y.[i] - err.[i])) (Array.init y.Length (fun i -> y.[i] + err.[i]))

        let s: Series<'ux, 'uy> =
            { Name = ""
              X = x
              Y = y
              XScale = Scale.linear ux (paddedOr 0.05 x)
              YScale = Scale.linear uy (paddedOr 0.05 span) }

        let bars = Geom.errorY s err
        let packed = Erase.packSeries s

        figureOf
            [| layerOf "" Theme.initial 0 bars packed
               { layerOf "" Theme.initial 0 GeomPoint packed with
                   ShowInLegend = false } |]

    // ---- Phase 6's constructors ---------------------------------------------------------------

    /// A staircase: the same points as `line`, with the claim that nothing happened between
    /// them.
    ///
    /// The default is `StepAfter` — the value holds from each sample until the next — which is
    /// what a sampled-and-held signal, a counter and a piecewise-constant control input all do.
    /// `stepWith` states the other two.
    let step (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        ofSeries (GeomStep StepAfter) (series "" ux uy x y)

    let stepWith (align: StepAlign) (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        ofSeries (GeomStep align) (series "" ux uy x y)

    /// The series a band is drawn against: x, the midline, and a y domain over **both** bounds.
    ///
    /// The midline is the series' y because a `PackedSeries` has to have one and the geom reads
    /// only x from it — but it is the honest choice rather than a placeholder: it is what the
    /// axis-sharing union in `Figure.unify` will compare against, and a band whose domain was
    /// taken from one bound would clip the other.
    let private ribbonSeries
        (name: string)
        (ux: U<'ux>)
        (uy: U<'uy>)
        (x: float<'ux>[])
        (low: float<'uy>[])
        (high: float<'uy>[])
        : Series<'ux, 'uy> * Geom =
        if low.Length <> x.Length || high.Length <> x.Length then
            invalidArg
                "low"
                (sprintf "Marey: %d x values but %d low and %d high values." x.Length low.Length high.Length)

        let s: Series<'ux, 'uy> =
            { Name = name
              X = x
              Y = Array.init x.Length (fun i -> (low.[i] + high.[i]) * 0.5)
              XScale = Scale.linear ux (paddedOr 0.05 x)
              YScale = Scale.linear uy (paddedOr 0.05 (Array.append low high)) }

        s, Geom.ribbon s low high

    /// A filled band between two y series — a confidence interval, an envelope, a min/max
    /// spread.
    ///
    /// The band and nothing else. `Chart.line … |> Chart.addRibbon …` is the fit-with-interval
    /// figure, and the order matters: the band is added second and drawn over the line, so it
    /// wants the theme's fill opacity, which it has.
    let ribbon (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (low: float<'uy>[]) (high: float<'uy>[]) : Figure =
        let s, g = ribbonSeries "" ux uy x low high
        ofSeries g s

    /// The exact extent a tessellation occupies: the data's range, out to the outer edge of the
    /// outermost tile, and **unpadded**.
    ///
    /// Same rule as a histogram's x domain, for the same reason. A grid of tiles partitions a
    /// region; a margin past the last tile draws a piece of that region as if no cell covered
    /// it, and a reader takes the gap for missing data.
    let private tileDomain (v: float<'u>[]) (half: float<'u>) : float<'u> * float<'u> =
        match Scale.domainOf v with
        | Some(lo, hi) -> lo - half, hi + half
        | None -> tag<'u> 0.0, tag<'u> 1.0

    /// **The grid pitch of a coordinate array**: the smallest positive gap between two of its
    /// values.
    ///
    /// On a regular grid — which is what a tiled figure is — that gap *is* the cell size, and
    /// taking the smallest rather than the mean is what keeps a grid with a missing row from
    /// widening every tile to cover the hole.
    let private pitch (v: float<'u>[]) : float<'u> =
        let gaps =
            v
            |> Array.filter (fun x -> not (System.Double.IsNaN(float x)) && not (System.Double.IsInfinity(float x)))
            |> Array.sort
            |> Array.pairwise
            |> Array.map (fun (a, b) -> b - a)
            |> Array.filter (fun g -> float g > 0.0)

        if gaps.Length = 0 then tag<'u> 1.0 else Array.min gaps

    /// Tiles of a stated size, centred on the given points — the heatmap geom.
    ///
    /// Both sizes are in the data's own units, so a cell 0.5 s wide and 2 K tall is written as
    /// `0.5&lt;s&gt;` and `2.0&lt;K&gt;` and cannot be transposed: the compiler will not accept
    /// a width in kelvin. What makes the tiles *mean* anything is a colour channel —
    /// `Chart.tile … |> Chart.colorBy U.kelvin temps`, which is what `Chart.heatmap` is.
    let tile
        (ux: U<'ux>)
        (uy: U<'uy>)
        (w: float<'ux>)
        (h: float<'uy>)
        (x: float<'ux>[])
        (y: float<'uy>[])
        : Figure =
        if x.Length <> y.Length then
            invalidArg "y" (sprintf "Marey: %d x values and %d y values." x.Length y.Length)

        let s: Series<'ux, 'uy> =
            { Name = ""
              X = x
              Y = y
              XScale = Scale.linear ux (tileDomain x (w * 0.5))
              YScale = Scale.linear uy (tileDomain y (h * 0.5)) }

        ofSeries (Geom.tile w h s) s

    /// A line from each `(x0, y0)` to the matching `(x1, y1)` — a displacement, an edge of a
    /// graph, a before-and-after pair.
    ///
    /// Both ends are inside the domain, which is the one thing a caller cannot fix afterwards:
    /// a segment whose far end is off the panel is clipped to a line pointing at nothing.
    let segment
        (ux: U<'ux>)
        (uy: U<'uy>)
        (x0: float<'ux>[])
        (y0: float<'uy>[])
        (x1: float<'ux>[])
        (y1: float<'uy>[])
        : Figure =
        if x0.Length <> y0.Length || x1.Length <> x0.Length || y1.Length <> x0.Length then
            invalidArg "x1" (sprintf "Marey: a segment needs four arrays of one length; got %d, %d, %d, %d." x0.Length y0.Length x1.Length y1.Length)

        let s: Series<'ux, 'uy> =
            { Name = ""
              X = x0
              Y = y0
              XScale = Scale.linear ux (paddedOr 0.05 (Array.append x0 x1))
              YScale = Scale.linear uy (paddedOr 0.05 (Array.append y0 y1)) }

        ofSeries (Geom.segment s x1 y1) s

    /// Labels at stated positions — an annotated point, a named landmark, a value printed on
    /// its own mark.
    ///
    /// `Chart.addLabels` is the form that labels a series already on the figure, and is what
    /// most callers want; this one is for text that is the whole layer.
    let text (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (labels: string[]) : Figure =
        if labels.Length <> x.Length then
            invalidArg "labels" (sprintf "Marey: %d positions and %d labels." x.Length labels.Length)

        ofSeries (Geom.text labels) (series "" ux uy x y)

    // ---- Adding layers -----------------------------------------------------------------------

    let private lastPanel (fig: Figure) =
        if fig.Panels.Length = 0 then 0 else fig.Panels.Length - 1

    let private mapPanel (i: int) (f: Panel -> Panel) (fig: Figure) =
        { fig with
            Panels = fig.Panels |> Array.mapi (fun j p -> if j = i then f p else p) }

    let private addLayers (mk: int -> Layer[]) (fig: Figure) : Figure =
        let i = lastPanel fig

        if fig.Panels.Length = 0 then
            fig
        else
            mapPanel
                i
                (fun p ->
                    { p with
                        Layers = Array.append p.Layers (mk p.Layers.Length) })
                fig

    /// Add a second (third, …) series to the last panel.
    ///
    /// The panel's axes are then shared: `Figure.unify` unions the domains at compile time and
    /// rebuilds every layer's scale over the union. It also checks that the added series has the
    /// *same physical dimension* as the existing ones and raises if not — the one runtime
    /// dimensional check in the library, and it is runtime only because the two series have
    /// already crossed §2.3's existential and F# can no longer compare their measures.
    let addLine (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        fig
        |> addLayers (fun i -> [| layerOf "" fig.Theme i GeomLine (Erase.packSeries (series "" ux uy x y)) |])

    let addScatter (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        fig
        |> addLayers (fun i -> [| layerOf "" fig.Theme i GeomPoint (Erase.packSeries (series "" ux uy x y)) |])

    let addArea (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        let s = series "" ux uy x y
        fig |> addLayers (fun i -> [| layerOf "" fig.Theme i (Geom.area s) (Erase.packSeries s) |])

    let addErrorbar (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (err: float<'uy>[]) (fig: Figure) : Figure =
        let span =
            Array.append (Array.init y.Length (fun i -> y.[i] - err.[i])) (Array.init y.Length (fun i -> y.[i] + err.[i]))

        let s: Series<'ux, 'uy> =
            { Name = ""
              X = x
              Y = y
              XScale = Scale.linear ux (paddedOr 0.05 x)
              YScale = Scale.linear uy (paddedOr 0.05 span) }

        fig
        |> addLayers (fun i -> [| layerOf "" fig.Theme i (Geom.errorY s err) (Erase.packSeries s) |])

    let addStep (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        fig
        |> addLayers (fun i -> [| layerOf "" fig.Theme i (GeomStep StepAfter) (Erase.packSeries (series "" ux uy x y)) |])

    /// Add a band between two y series to the last panel — the interval around a fit that was
    /// already drawn.
    ///
    /// It goes in the palette like any other added series, so a band added to a line comes out
    /// in the *next* colour rather than the line's. That is deliberate and matches
    /// `addErrorbar`: the two arrays are a second thing that was measured, and a caller who
    /// means it as a shading of the first says so with `Chart.withStyle`.
    let addRibbon
        (ux: U<'ux>)
        (uy: U<'uy>)
        (x: float<'ux>[])
        (low: float<'uy>[])
        (high: float<'uy>[])
        (fig: Figure)
        : Figure =
        let s, g = ribbonSeries "" ux uy x low high
        fig |> addLayers (fun i -> [| layerOf "" fig.Theme i g (Erase.packSeries s) |])

    let addSegment
        (ux: U<'ux>)
        (uy: U<'uy>)
        (x0: float<'ux>[])
        (y0: float<'uy>[])
        (x1: float<'ux>[])
        (y1: float<'uy>[])
        (fig: Figure)
        : Figure =
        let s: Series<'ux, 'uy> =
            { Name = ""
              X = x0
              Y = y0
              XScale = Scale.linear ux (paddedOr 0.05 (Array.append x0 x1))
              YScale = Scale.linear uy (paddedOr 0.05 (Array.append y0 y1)) }

        fig
        |> addLayers (fun i -> [| layerOf "" fig.Theme i (Geom.segment s x1 y1) (Erase.packSeries s) |])

    // ---- Stats (§9, D47) ----------------------------------------------------------------------

    /// The y domain of a quantity that cannot be negative and is read from zero: zero exactly,
    /// to the maximum plus breathing room *above only*.
    ///
    /// D50 widens a bar's domain to reach zero; this is the same rule for a quantity that never
    /// leaves the positive half-line, and it differs from `withZero (paddedOr …)` in the case
    /// that matters here. An empty histogram bin, or a density that reaches the axis, has the
    /// value zero — and padding below it draws a strip of negative count under the baseline,
    /// which is a region the quantity cannot occupy.
    let private fromZero (ys: float<'u>[]) : float<'u> * float<'u> =
        let hi =
            if Array.isEmpty ys then
                0.0
            else
                ys |> Array.fold (fun acc v -> if System.Double.IsNaN(float v) then acc else max acc (float v)) 0.0

        tag<'u> 0.0, tag<'u> (if hi <= 0.0 then 1.0 else hi * 1.05)

    /// §9 / D47 — **the bar series a histogram is drawn as**: bin centres against counts, bars
    /// touching, y from zero.
    ///
    /// **The x measure is the sample's and the y measure is `float<1>`**, and nothing had to be
    /// told so — which is the first place a stat pays out on the grammar's promise. A histogram
    /// of metres is a dimensionless count against a metre axis, and the count cannot end up on
    /// the metre axis by a transposition, because the two do not have the same type.
    ///
    /// The x domain is the outer bin edges *exactly*, unpadded, and the bars are full width.
    /// A histogram partitions an interval; a gap between the bars, or a margin past the last
    /// edge, both draw a piece of that interval as if no bin covered it.
    let histogramSeries (name: string) (h: Stat.Histogram<'u>) : Series<'u, 1> * Geom =
        let counts = h.Frequencies

        let s: Series<'u, 1> =
            { Name = name
              X = h.Centers
              Y = counts
              XScale = Scale.linear h.Unit (h.Edges.[0], h.Edges.[h.Edges.Length - 1])
              YScale = Scale.linear U.one (fromZero counts) }

        s, Geom.bar 1.0 s

    /// The line series a kernel density estimate is drawn as.
    ///
    /// **`Series<'u, 1/'u>`.** The y axis of a density over metres is in reciprocal metres, and
    /// this is where the library says it out loud: the axis labels itself `m⁻¹` off the witness
    /// `Stat.density` built, and `Figure.unify` refuses to put this layer on the same panel as
    /// the sample it summarises — which is correct, and is a mistake most plotting libraries
    /// will draw for you.
    let densitySeries (name: string) (d: Stat.Density<'u>) : Series<'u, 1 / 'u> * Geom =
        let s: Series<'u, 1 / 'u> =
            { Name = name
              X = d.X
              Y = d.Y
              XScale = Scale.linear d.Unit (d.X.[0], d.X.[d.X.Length - 1])
              YScale = Scale.linear d.DensityUnit (fromZero d.Y) }

        s, GeomLine

    /// The staircase an empirical CDF is drawn as, and the y domain it is known to occupy.
    ///
    /// Two points per distinct value — the foot and the head of its riser — because `GeomLine`
    /// through one point per value draws a diagonal where the function has a jump, and an ECDF
    /// interpolated between observations asserts data between them that was not observed.
    ///
    /// **`GeomStep` exists now and this still does not use it**, which is worth stating because
    /// the obvious reading of phase 6 is that it should. `GeomStep StepAfter` draws the riser
    /// *between* two points, so it has no riser to draw at the first observation — and the jump
    /// from zero to `1/n` at the smallest datum is the one an ECDF is read for. The doubled
    /// array draws it. `Chart.step` is the geom's own front door for the data that has no such
    /// edge.
    ///
    /// The y domain is `0 … 1` stated rather than measured: an ECDF's range is known before the
    /// data is, and taking it from the sample instead would start the axis at `1/n`.
    let ecdfSeries (name: string) (e: Stat.Ecdf<'u>) : Series<'u, 1> * Geom =
        let n = e.X.Length
        let xs = Array.zeroCreate<float<'u>> (2 * n)
        let ys = Array.zeroCreate<float> (2 * n)

        for i in 0 .. n - 1 do
            let below = if i = 0 then 0.0 else e.F.[i - 1]
            xs.[2 * i] <- e.X.[i]
            ys.[2 * i] <- below
            xs.[2 * i + 1] <- e.X.[i]
            ys.[2 * i + 1] <- e.F.[i]

        let s: Series<'u, 1> =
            { Name = name
              X = xs
              Y = ys
              XScale = Scale.linear e.Unit (paddedOr 0.05 e.X)
              YScale = Scale.linear U.one (0.0, 1.0) }

        s, GeomLine

    /// A figure from an already-computed histogram — the door for a caller who chose the bins.
    let ofHistogram (h: Stat.Histogram<'u>) : Figure =
        let s, g = histogramSeries "" h
        ofSeries g s

    /// A histogram of a sample, binned by Freedman–Diaconis.
    let histogram (u: U<'u>) (xs: float<'u>[]) : Figure = ofHistogram (Stat.bin u xs)

    /// A histogram of a sample with a stated bin count.
    let histogramWith (u: U<'u>) (bins: int) (xs: float<'u>[]) : Figure = ofHistogram (Stat.binWith u bins xs)

    /// A figure from an already-computed density — the door for a caller who chose the bandwidth.
    let ofDensity (d: Stat.Density<'u>) : Figure =
        let s, g = densitySeries "" d
        ofSeries g s

    /// A Gaussian kernel density estimate of a sample, at Silverman's bandwidth.
    let density (u: U<'u>) (xs: float<'u>[]) : Figure = ofDensity (Stat.density u xs)

    /// A figure from an already-computed ECDF.
    let ofEcdf (e: Stat.Ecdf<'u>) : Figure =
        let s, g = ecdfSeries "" e
        ofSeries g s

    /// The empirical CDF of a sample, drawn as the step function it is.
    let ecdf (u: U<'u>) (xs: float<'u>[]) : Figure = ofEcdf (Stat.ecdf u xs)

    /// Add a least-squares fit of y against x to the last panel, as a two-point line over the
    /// range of the data.
    ///
    /// **The slope is not returned, and that is D47's split rather than an omission.** The fit
    /// is a number — `float<'uy/'ux>`, and for a displacement against a time that is a velocity
    /// the caller can bind, print, and do arithmetic with. Handing it back through a `Figure`
    /// would mean packing it into an existential to get it out of this function, so a caller
    /// who wants the number calls `Stat.regression` and keeps it typed; this draws it.
    ///
    /// ```fsharp
    /// let fit = Stat.regression U.second U.metre ts xs
    /// let v: float<m/s> = fit.Slope
    /// Chart.scatter U.second U.metre ts xs |> Chart.addFit U.second U.metre ts xs
    /// ```
    let addFit (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        let f = Stat.regression ux uy x y
        let fx, fy = Stat.fitLine f x

        fig
        |> addLayers (fun i -> [| layerOf "" fig.Theme i GeomLine (Erase.packSeries (series "" ux uy fx fy)) |])

    // ---- Dimensionless ------------------------------------------------------------------------

    /// Pure maths, where the quantity genuinely has no dimension: `sin x`, a ratio, a
    /// probability, a count.
    ///
    /// ```fsharp
    /// Chart.Plain.line xs (xs |> Array.map sin)
    /// |> Chart.withTitle "sin x"
    /// ```
    ///
    /// Every binding here is the same-named constructor at `U.one U.one` and nothing else. It
    /// costs nothing because `float<1> = float`: the arrays a caller already has *are* the
    /// arrays these take, with no `LanguagePrimitives.FloatWithMeasure` and no witness to invent
    /// for a quantity that has none. The `Figure` that comes out is an ordinary one, so every
    /// combinator, axis and output function below applies unchanged.
    ///
    /// `U.one` is not a hole in §1.3's thesis; it is the thesis answering "dimensionless"
    /// correctly. Its `Dim` is `Dim.zero` and its symbol is empty, so `Axis` drops the bracket
    /// and prints a bare `x` rather than `x [1]`, and `Prefix` refuses to rescale it, so no
    /// probability is ever labelled in kilo-anything. The witness still arrives exactly once —
    /// it just says "none".
    ///
    /// Deliberately **not** the default spelling of `Chart.line`, and not an overload of it:
    /// `Chart` is a module, so F# has no overloading here anyway, and data that does have a
    /// dimension should have to say so. A shim that let it stay silent would cost the library
    /// the one thing it is for.
    [<RequireQualifiedAccess>]
    module Plain =

        let line (x: float[]) (y: float[]) : Figure = line U.one U.one x y

        let scatter (x: float[]) (y: float[]) : Figure = scatter U.one U.one x y

        let area (x: float[]) (y: float[]) : Figure = area U.one U.one x y

        let bar (x: float[]) (y: float[]) : Figure = bar U.one U.one x y

        let errorbar (x: float[]) (y: float[]) (err: float[]) : Figure = errorbar U.one U.one x y err

        let addLine (x: float[]) (y: float[]) (fig: Figure) : Figure = addLine U.one U.one x y fig

        let addScatter (x: float[]) (y: float[]) (fig: Figure) : Figure = addScatter U.one U.one x y fig

        let addArea (x: float[]) (y: float[]) (fig: Figure) : Figure = addArea U.one U.one x y fig

        let addErrorbar (x: float[]) (y: float[]) (err: float[]) (fig: Figure) : Figure =
            addErrorbar U.one U.one x y err fig

        // The distribution stats, where the sample genuinely has no dimension — a residual in
        // units of sigma, a correlation coefficient, a p-value. `density` is the one whose type
        // changes here rather than merely simplifying: a density over `float<1>` is
        // `float<1/1>`, which *is* `float`, so the reciprocal measure is still correct and
        // simply has nothing to say.

        let histogram (x: float[]) : Figure = histogram U.one x

        let histogramWith (bins: int) (x: float[]) : Figure = histogramWith U.one bins x

        let density (x: float[]) : Figure = density U.one x

        let ecdf (x: float[]) : Figure = ecdf U.one x

        let addFit (x: float[]) (y: float[]) (fig: Figure) : Figure = addFit U.one U.one x y fig

        // Phase 6's geoms, where the quantity genuinely has no dimension.

        let step (x: float[]) (y: float[]) : Figure = step U.one U.one x y

        let stepWith (align: StepAlign) (x: float[]) (y: float[]) : Figure = stepWith align U.one U.one x y

        let ribbon (x: float[]) (low: float[]) (high: float[]) : Figure = ribbon U.one U.one x low high

        let tile (w: float) (h: float) (x: float[]) (y: float[]) : Figure = tile U.one U.one w h x y

        let segment (x0: float[]) (y0: float[]) (x1: float[]) (y1: float[]) : Figure =
            segment U.one U.one x0 y0 x1 y1

        let text (x: float[]) (y: float[]) (labels: string[]) : Figure = text U.one U.one x y labels

        let addStep (x: float[]) (y: float[]) (fig: Figure) : Figure = addStep U.one U.one x y fig

        let addRibbon (x: float[]) (low: float[]) (high: float[]) (fig: Figure) : Figure =
            addRibbon U.one U.one x low high fig

        let addSegment (x0: float[]) (y0: float[]) (x1: float[]) (y1: float[]) (fig: Figure) : Figure =
            addSegment U.one U.one x0 y0 x1 y1 fig

    /// §9 / D45 — **the categorical front door**: a chart whose x is a category rather than a
    /// quantity.
    ///
    /// A sibling module rather than a `Chart.bars` beside `Chart.bar`, for two reasons. The
    /// names would differ by one letter while taking entirely different arguments, which is how
    /// a caller gets an error message about the wrong thing; and this module grows — a
    /// categorical scatter and a boxplot are the same axis with a different geom.
    ///
    /// The category is *stated as data*, exactly as a unit witness is: `keys` is one value per
    /// row, and the level set is derived from it in first-appearance order (`Levels.ofSeq`). The
    /// y axis is dimensioned as everywhere else, so `Chart.Category.bar U.metre keys yields`
    /// prints named bands under `x [m]` with nothing configured.
    ///
    /// Repeated keys are **not** aggregated: two rows with the same key are two bars in one
    /// band, which is honest about the data. Counting them is a stat's job, and `Category.count`
    /// is that stat with its chart — an explicit call, because doing it silently here would be
    /// the library deciding what the data means.
    [<RequireQualifiedAccess>]
    module Category =

        /// The typed categorical series, over the levels the keys themselves define.
        let series (name: string) (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Levels<'k> * Series<1, 'uy> =
            if keys.Length <> values.Length then
                invalidArg
                    "values"
                    (sprintf "Marey: series '%s' has %d keys and %d values." name keys.Length values.Length)

            let levels = Levels.ofSeq keys

            let s: Series<1, 'uy> =
                { Name = name
                  X = Levels.positions levels keys
                  Y = values
                  XScale = Levels.scale levels
                  YScale = Scale.linear uy (paddedOr 0.05 values) }

            levels, s

        /// The same series with the bar rule on y: zero is in the domain, because a bar is a
        /// length from it.
        let private barSeries (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) =
            let levels, s = series "" uy keys values

            levels,
            { s with
                YScale = Scale.withDomain (withZero (paddedOr 0.05 values)) s.YScale }

        /// A categorical axis is titled by its bands, so the conventional symbol is suppressed.
        ///
        /// `Axes.defaultLabel` reads the quantity name out of the dimension — `t` for a time
        /// axis, `x` for a length one — and a category's dimension is `1`, so it falls back to
        /// the axis's own name and prints a bare `x` underneath bands already labelled
        /// `control`, `treated`. Two different meanings of `x` in one figure, one of which is
        /// noise. An explicit empty label wins over the fallback, and `Chart.withXLabel` still
        /// puts a real name back.
        let private unlabelledX (fig: Figure) =
            { fig with
                Panels =
                    fig.Panels
                    |> Array.map (fun p ->
                        { p with
                            XAxis = { p.XAxis with Label = Some "" } }) }

        /// A bar per row, in the band its key names, measured from zero.
        let bar (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Figure =
            let _, s = barSeries uy keys values
            ofSeries (Geom.bar 0.7 s) s |> unlabelledX

        /// The same axis with points instead of bars — a dot plot, and the shape a categorical
        /// colour channel will hang off in phase 2.
        let scatter (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Figure =
            let _, s = series "" uy keys values
            ofSeries GeomPoint s |> unlabelledX

        /// §9 / D47 — **`count`**: one bar per level, as tall as the number of rows that carry
        /// it.
        ///
        /// The categorical counterpart of `Chart.histogram`, and it has the same two measures:
        /// no x measure at all, because a category has none (D45), and `float<1>` on y, because
        /// a count is a number of rows.
        ///
        /// The level order is `Stat.count`'s, which is `Levels.ofSeq`'s, which is
        /// first-appearance — so a counted axis and a plotted one put the same category in the
        /// same band.
        let count (keys: 'k[]) : Figure =
            let pairs = Stat.count keys
            bar U.one (pairs |> Array.map fst) (pairs |> Array.map (snd >> float))

        /// One point per **level** rather than per row, which is what a summarising geom needs:
        /// a box plot has one box per band however many rows went into it.
        ///
        /// The x array is the level numbers `0 … n-1` in level order, so `medianSpacing` reads
        /// a pitch of exactly one band and a width fraction means what it says.
        let private bandSeries
            (uy: U<'uy>)
            (levels: Levels<'k>)
            (domain: float<'uy> * float<'uy>)
            (mid: float<'uy>[])
            : Series<1, 'uy> =
            { Name = ""
              X = Array.init levels.Values.Length float
              Y = mid
              XScale = Levels.scale levels
              YScale = Scale.linear uy domain }

        /// §9 / D55 — **a box plot per level**, from the rows that carry it.
        ///
        /// The measures again do the work nobody had to ask for: `Stat.summary` of a
        /// `float&lt;'uy&gt;[]` is entirely in `float&lt;'uy&gt;`, so the box, the whiskers and
        /// the outliers all project through the same y scale and the axis labels itself off the
        /// same witness the values were declared with.
        ///
        /// The y domain covers the whiskers **and the outliers**, padded. A domain taken from
        /// the boxes alone is the tidier picture and it is a lie by omission: the points that
        /// fall outside it are exactly the ones a box plot is read to find, and clipping them
        /// hides them behind the panel edge rather than dropping them somewhere visible.
        let boxplot (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Figure =
            let groups = Stat.groupBy keys values
            let levels = Levels.ofSeq (groups |> Array.map fst)
            let summaries = groups |> Array.map (fun (_, vs) -> Stat.summary uy vs)

            let reach =
                summaries
                |> Array.collect (fun q ->
                    let lo, hi = q.Extent
                    [| lo; hi |])

            let s =
                bandSeries uy levels (paddedOr 0.05 reach) (summaries |> Array.map (fun q -> q.Median))

            ofSeries (Geom.boxplot 0.6 s summaries) s |> unlabelledX

        /// §9 / D55 — **a violin per level**: the same split, drawn as a density instead of a
        /// summary.
        ///
        /// **The bandwidth comes from the undivided sample** (`Stat.frameOf`), not from each
        /// group, and that is D48's facet argument arriving in a second place. A bandwidth
        /// chosen per group smooths the sparse ones harder and invents a difference in *shape*
        /// that is not in the data — the quiet kind of wrong, because the figure looks fine. It
        /// also removes a failure: a group of one value has no bandwidth of its own and a
        /// density over it raises, where under a shared one it draws the whole sample's kernel
        /// over its single point, which is what it is.
        ///
        /// A sample with no spread at all still has no density to estimate, and says so.
        let violin (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Figure =
            let groups = Stat.groupBy keys values
            let levels = Levels.ofSeq (groups |> Array.map fst)
            let h = tag<'uy> (Stat.frameOf values).Bandwidth

            if float h <= 0.0 then
                invalidArg
                    "values"
                    "Marey: a violin needs a sample with spread. Every value here is the same, so there is no density to estimate."

            let densities =
                groups
                |> Array.map (fun (_, vs) -> Stat.densityWith uy h Stat.DefaultDensityPoints vs)

            let reach =
                densities |> Array.collect (fun d -> [| d.X.[0]; d.X.[d.X.Length - 1] |])

            let s =
                bandSeries uy levels (paddedOr 0.05 reach) (groups |> Array.map (fun (_, vs) -> Stat.quantile 0.5 vs))

            ofSeries (Geom.violin 0.8 s densities) s |> unlabelledX

    // ---- Combinators. All pure; every one returns a new figure. -------------------------------

    let withTitle (t: string) (fig: Figure) = { fig with Title = t }
    let withSubtitle (t: string) (fig: Figure) = { fig with Subtitle = t }

    /// Re-theme, recolouring only the layers that were never coloured by hand.
    ///
    /// A layer's style is resolved values (same rule as `Theme`), so a layer coloured under the
    /// publication palette keeps that colour when the theme changes — unless it still has
    /// exactly the colour the old theme would have given it, which is the machine-checkable
    /// definition of "the user did not choose this". Explicit choices survive; defaults follow
    /// the theme.
    let withTheme (t: Theme) (fig: Figure) =
        let old = fig.Theme

        // A categorical channel's colours came off the outgoing theme's palette, so they follow
        // a retheme on exactly the rule a layer's style does: replaced when they are still what
        // the old theme would have chosen, kept when they are not. Without this, a rethemed
        // figure would draw its levels in the old palette while its unchannelled layers moved —
        // the worst of both, and silent.
        let retheme (c: Channel) =
            match c.Map with
            | ColorByLevel(levels, colors) when colors = Channel.levelColors old levels.Length ->
                { c with
                    Map = ColorByLevel(levels, Channel.levelColors t levels.Length) }
            | ShapeByLevel(levels, shapes) when shapes = Channel.levelShapes old levels.Length ->
                { c with
                    Map = ShapeByLevel(levels, Channel.levelShapes t levels.Length) }
            | _ -> c

        { fig with
            Theme = t
            Panels =
                fig.Panels
                |> Array.map (fun p ->
                    { p with
                        Layers =
                            p.Layers
                            |> Array.mapi (fun i l ->
                                let l =
                                    { l with
                                        Channels = l.Channels |> Array.map retheme }

                                if l.Style = Geom.styleFor old i then
                                    { l with Style = Geom.styleFor t i }
                                else
                                    l) }) }

    let private mapLast (f: Panel -> Panel) (fig: Figure) = mapPanel (lastPanel fig) f fig

    let private mapLastLayer (f: Layer -> Layer) (fig: Figure) =
        mapLast
            (fun p ->
                if p.Layers.Length = 0 then
                    p
                else
                    { p with
                        Layers = p.Layers |> Array.mapi (fun i l -> if i = p.Layers.Length - 1 then f l else l) })
            fig

    /// Name the most recently added series. The name is what the legend shows and what the
    /// `Annotated` node carries for hit-testing.
    let withName (n: string) (fig: Figure) =
        mapLastLayer (fun l -> { l with Name = n; ShowInLegend = true }) fig

    let withColor (c: Color) (fig: Figure) =
        mapLastLayer (fun l -> { l with Style = Geom.withColor c l.Style }) fig

    let withStyle (f: GeomStyle -> GeomStyle) (fig: Figure) =
        mapLastLayer (fun l -> { l with Style = f l.Style }) fig

    let withDash (d: float<px>[]) (fig: Figure) =
        mapLastLayer (fun l -> { l with Style = Geom.withDash d l.Style }) fig

    let withMarker (s: MarkerShape) (fig: Figure) =
        mapLastLayer (fun l -> { l with Style = Geom.withShape s l.Style }) fig

    let withOpacity (o: float) (fig: Figure) =
        mapLastLayer (fun l -> { l with Style = Geom.withOpacity o l.Style }) fig

    /// Label the points of the most recently added series, one string per point.
    ///
    /// **It reuses that layer's own data and style**, which is the whole reason it is a
    /// combinator and not a constructor: the labels then sit on the marks they name, in the
    /// colour of the series they belong to, and there is no second copy of the positions to
    /// fall out of step with the first. It is not in the legend — the labels *are* the reading,
    /// so a swatch saying so adds nothing.
    ///
    /// `Chart.withStyle` on the result moves the labels, because the offset and the face live in
    /// the geom and the style: `Chart.addLabels names |> Chart.withStyle (Geom.withFont small)`.
    let addLabels (labels: string[]) (fig: Figure) : Figure =
        mapLast
            (fun p ->
                if p.Layers.Length = 0 then
                    p
                else
                    let last = p.Layers.[p.Layers.Length - 1]
                    let n = Erase.seriesLength last.Data

                    if labels.Length <> n then
                        invalidArg "labels" (sprintf "Marey: the series has %d points and %d labels." n labels.Length)

                    { p with
                        Layers =
                            Array.append
                                p.Layers
                                [| { last with
                                       Geom = Geom.text labels
                                       Channels = [||]
                                       ShowInLegend = false } |] })
            fig

    // ---- Non-positional channels (§9, D46, D49) -----------------------------------------------

    /// Attach a channel to the most recently added layer.
    ///
    /// The length check is here rather than inside `Channel` because here is where the caller's
    /// array is: a channel with the wrong number of values would otherwise silently leave the
    /// tail of a series unmapped, and unmapped points are not drawn.
    ///
    /// Attaching a channel also puts the layer **in** the legend, which is what `ShowInLegend`
    /// now means: a categorical channel gives an unnamed layer something to enumerate, and
    /// `Figure.layerEntries` reads the flag before it enumerates anything. Without this line the
    /// primary case — `Chart.scatter |> Chart.colorByKey`, where the layer's name is empty and
    /// the flag is therefore `false` — would lose its legend.
    let private withChannel (what: string) (values: int) (ch: Channel) (fig: Figure) : Figure =
        mapLastLayer
            (fun l ->
                let n = Erase.seriesLength l.Data

                if values <> n then
                    invalidArg
                        "values"
                        (sprintf "Marey: the %s channel has %d values but the series has %d points." what values n)

                { l with
                    Channels = Array.append l.Channels [| ch |]
                    ShowInLegend = true })
            fig

    /// **Colour by a category.** One colour per level, one batch per level, one legend entry
    /// per level — and the levels come from the keys themselves, in first-appearance order,
    /// exactly as `Chart.Category` derives them (D45).
    ///
    /// ```fsharp
    /// Chart.scatter U.second U.metre ts xs
    /// |> Chart.colorByKey species
    /// |> Chart.shapeByKey species
    /// ```
    ///
    /// Applying both to the same keys is §9's colour-blind-safe scatter, and it is still *one
    /// batch per level*: the two channels read the same level number, so the grouping does not
    /// take their product.
    let colorByKey (keys: 'k[]) (fig: Figure) : Figure =
        let levels = Levels.ofSeq keys

        withChannel "colour" keys.Length (Channel.colorByLevel fig.Theme levels.Labels (Levels.positions levels keys)) fig

    /// **Shape by a category** — the redundant encoding that keeps a scatter readable in
    /// greyscale and under colour-vision deficiency, which is why `MarkerShape` is on the
    /// batched node at all (D5).
    let shapeByKey (keys: 'k[]) (fig: Figure) : Figure =
        let levels = Levels.ofSeq keys

        withChannel "shape" keys.Length (Channel.shapeByLevel fig.Theme levels.Labels (Levels.positions levels keys)) fig

    /// **Colour by a quantity**, over a named ramp.
    ///
    /// The witness is an argument for the same reason it is one on `Chart.line`: `float<K>` is
    /// `float` at runtime, and something has to hand the library the evidence exactly once.
    /// What it buys is the same thing it buys on an axis — the colorbar labels itself `T [K]`,
    /// picks its own prefix, and its ticks are the axis compiler's (D49).
    ///
    /// The domain is the data's own range, **unpadded**: an axis pads so that the extreme
    /// points are not on the frame, and a colour ramp has no frame to sit on — padding it would
    /// simply throw away both ends of the ramp.
    let colorByWith (ramp: Ramp) (u: U<'u>) (values: float<'u>[]) (fig: Figure) : Figure =
        let domain =
            match Scale.domainOf values with
            | Some d -> d
            | None -> tag<'u> 0.0, tag<'u> 1.0

        withChannel "colour" values.Length (Channel.colorByValue "" ramp (Scale.linear u domain) values) fig

    /// `colorByWith` over the theme's sequential ramp — viridis-family, perceptually ordered
    /// and lightness-monotone, so it survives greyscale printing.
    let colorBy (u: U<'u>) (values: float<'u>[]) (fig: Figure) : Figure =
        colorByWith fig.Theme.Sequential u values fig

    /// Name the quantity a colour channel encodes: `T` in `T [K]`. The unit half is never
    /// configured, exactly as on an axis.
    ///
    /// Without it the name is read from the channel's *dimension* — a kelvin channel is `T`, a
    /// time channel is `t` — and a dimension with no conventional symbol leaves the bar
    /// labelled with its unit alone rather than borrowing an axis's `y`.
    let withColorLabel (label: string) (fig: Figure) : Figure =
        mapLastLayer
            (fun l ->
                { l with
                    Channels =
                        l.Channels
                        |> Array.map (fun c ->
                            match c.Map with
                            | ColorByValue _ -> { c with Label = label }
                            | _ -> c) })
            fig

    /// **Size by a quantity**, between two device diameters.
    ///
    /// Interpolated in area rather than in diameter, because area is what the eye reads off a
    /// disc. Size is the weakest of the channels for accurate reading, so it is the one to
    /// reach for last: the key it produces is three marks in the legend, not a bar.
    /// §9 — **a heatmap**: one tile per row, sized to the grid's own pitch, coloured by a third
    /// quantity.
    ///
    /// Three witnesses and three measures, and the middle one is what a heatmap usually gets
    /// wrong: `z` has its own unit, so the colorbar labels itself `T [K]` off the same witness
    /// the values were declared with (D49), and a `z` handed the x array does not compile.
    ///
    /// The cell size is derived rather than asked for, because it is a property of the grid and
    /// not a choice: the smallest positive gap between two x values is the column pitch, and a
    /// tile that size tessellates. `Chart.tile` takes the size for the case where the grid is
    /// irregular and the caller knows what the cells are.
    let heatmap
        (ux: U<'ux>)
        (uy: U<'uy>)
        (uz: U<'uz>)
        (x: float<'ux>[])
        (y: float<'uy>[])
        (z: float<'uz>[])
        : Figure =
        tile ux uy (pitch x) (pitch y) x y |> colorBy uz z

    let sizeByWith (smallest: float<px>) (largest: float<px>) (u: U<'u>) (values: float<'u>[]) (fig: Figure) : Figure =
        let domain =
            match Scale.domainOf values with
            | Some d -> d
            | None -> tag<'u> 0.0, tag<'u> 1.0

        withChannel "size" values.Length (Channel.sizeByValue "" smallest largest (Scale.linear u domain) values) fig

    /// `sizeByWith` over a range centred on the theme's marker size: half of it to three times
    /// it, which is a visible span without producing marks that swallow their neighbours.
    let sizeBy (u: U<'u>) (values: float<'u>[]) (fig: Figure) : Figure =
        let m = fig.Theme.MarkerSize
        sizeByWith (m * 0.5) (m * 3.0) u values fig

    // ---- Axes ---------------------------------------------------------------------------------

    /// An axis configuration named after the quantity. §9's `Chart.withXAxis (Axis.auto …)` in
    /// spirit: the unit half of the title is never configured, only the quantity name.
    let axis (label: string) : AxisConfig = { Figure.axisConfig with Label = Some label }

    let withXAxis (cfg: AxisConfig) (fig: Figure) = mapLast (fun p -> { p with XAxis = cfg }) fig
    let withYAxis (cfg: AxisConfig) (fig: Figure) = mapLast (fun p -> { p with YAxis = cfg }) fig

    let withXLabel (l: string) (fig: Figure) =
        mapLast (fun p -> { p with XAxis = { p.XAxis with Label = Some l } }) fig

    let withYLabel (l: string) (fig: Figure) =
        mapLast (fun p -> { p with YAxis = { p.YAxis with Label = Some l } }) fig

    /// Render unit symbols in another typography — `Siunitx` for a figure destined for a LaTeX
    /// document, `Ascii` for a terminal.
    let withUnitStyle (s: UnitStyle) (fig: Figure) =
        mapLast
            (fun p ->
                { p with
                    XAxis = { p.XAxis with Style = Some s }
                    YAxis = { p.YAxis with Style = Some s } })
            fig

    let private mapLayers (f: PackedSeries -> PackedSeries) (fig: Figure) =
        mapLast (fun p -> { p with Layers = p.Layers |> Array.map (fun l -> { l with Data = f l.Data }) }) fig

    /// Put the x axis on a log scale. The domain is re-derived from the positive data, because a
    /// padded linear domain usually straddles zero and a log scale cannot.
    let withXLog (fig: Figure) =
        mapLayers (Figure.withKinds (Some(ScaleKind.Log 10.0)) None) fig

    let withYLog (fig: Figure) =
        mapLayers (Figure.withKinds None (Some(ScaleKind.Log 10.0))) fig

    let withXScaleKind (k: ScaleKind) (fig: Figure) = mapLayers (Figure.withKinds (Some k) None) fig
    let withYScaleKind (k: ScaleKind) (fig: Figure) = mapLayers (Figure.withKinds None (Some k)) fig

    /// Set the x domain in raw numeric values of the axis's own unit. Raw because the measure
    /// has already been erased into the figure; use `Chart.ofSeries` with a hand-built scale
    /// when you want the domain checked.
    let withXDomain (lo: float) (hi: float) (fig: Figure) =
        mapLayers (Figure.withDomains (Some(lo, hi)) None) fig

    let withYDomain (lo: float) (hi: float) (fig: Figure) =
        mapLayers (Figure.withDomains None (Some(lo, hi))) fig

    let withGrid (x: bool) (y: bool) (fig: Figure) =
        mapLast
            (fun p ->
                { p with
                    XAxis = { p.XAxis with Grid = Some x }
                    YAxis = { p.YAxis with Grid = Some y } })
            fig

    let withLegend (placement: LegendPlacement) (fig: Figure) = mapLast (fun p -> { p with Legend = placement }) fig

    /// §4 — swap the coordinate system. `Coord.polar` turns every geom on the panel polar and no
    /// geom finds out.
    let withCoord (c: Coord) (fig: Figure) = mapLast (fun p -> { p with Coord = c }) fig

    let withPanelTitle (t: string) (fig: Figure) = mapLast (fun p -> { p with Title = t }) fig

    // ---- Multi-panel ---------------------------------------------------------------------------

    /// Put `b`'s panels to the right of `a`'s. `b`'s figure-level title is dropped; use
    /// `withPanelTitle` for per-panel headings.
    let beside (b: Figure) (a: Figure) : Figure =
        { a with
            Cols = a.Cols + b.Cols
            Rows = max a.Rows b.Rows
            Panels = Array.append a.Panels (b.Panels |> Array.map (fun p -> { p with Col = p.Col + a.Cols })) }

    /// Put `b`'s panels below `a`'s.
    let above (b: Figure) (a: Figure) : Figure =
        { a with
            Rows = a.Rows + b.Rows
            Cols = max a.Cols b.Cols
            Panels = Array.append a.Panels (b.Panels |> Array.map (fun p -> { p with Row = p.Row + a.Rows })) }

    // ---- Facets (§9, D48) ----------------------------------------------------------------------

    /// The wrap width a facet takes when nobody states one: as square as the panel count allows,
    /// which is what keeps each panel's aspect ratio closest to the single-panel figure the
    /// caller was looking at a moment ago.
    let wrapCols (n: int) : int =
        if n <= 1 then 1 else int (ceil (sqrt (float n)))

    /// **The key is drawn once.** Every layer outside the first drawn panel leaves the legend and
    /// the colorbar (D53, as D48 widens it).
    ///
    /// A facet's channels are built over the whole block and then sliced, so every panel's key is
    /// the same key. Repeating it says nothing the first one did not, and a colorbar repeated
    /// takes a column out of every panel to do it.
    let private keyOnFirstPanelOnly (fig: Figure) : Figure =
        let first =
            fig.Panels |> Array.tryFindIndex (fun p -> p.Layers.Length > 0)

        match first with
        | None -> fig
        | Some k ->
            { fig with
                Panels =
                    fig.Panels
                    |> Array.mapi (fun i p ->
                        if i = k then
                            p
                        else
                            { p with
                                Layers = p.Layers |> Array.map (fun l -> { l with ShowInLegend = false }) }) }

    /// §9 / D48 — **lay independently built figures out as a facet**: a wrapped grid of panels
    /// over shared axes.
    ///
    /// This is the *hand-composed* facet, and it is the only one whose panels can disagree
    /// dimensionally — which makes it the one place the cross-panel `Dim` check can actually fire.
    /// The block's `facetWrap` splits one dataset by a key, so every panel there reads the same
    /// column under the same witness and a mismatch is unconstructible.
    ///
    /// Each part is a **single-panel** figure and a title for it. A multi-panel figure is refused
    /// rather than flattened: a facet cell holds one panel, and silently spreading two of them
    /// across the grid would put a caller's `beside` composition somewhere they did not ask for.
    let facetWith (mode: FacetScales) (cols: int) (parts: (string * Figure)[]) : Figure =
        if parts.Length = 0 then
            failwith "Marey: a facet needs at least one panel."

        parts
        |> Array.iteri (fun i (title, f) ->
            if f.Panels.Length <> 1 then
                failwithf
                    "Marey: facet cell %d ('%s') is a %d-panel figure, and a facet cell holds one panel. Compose it with `beside` or `above` instead, or facet the parts separately."
                    i
                    title
                    f.Panels.Length)

        let n = parts.Length
        let cols = max 1 (min cols n)

        let panels =
            parts
            |> Array.mapi (fun i (title, f) ->
                { f.Panels.[0] with
                    Title = title
                    Row = i / cols
                    Col = i % cols })

        { Figure.create panels with
            Theme = (snd parts.[0]).Theme }
        |> Figure.shareScales mode
        |> keyOnFirstPanelOnly

    /// `facetWith` sharing both axes — what a facet means — over an automatic wrap width.
    let facet (parts: (string * Figure)[]) : Figure =
        facetWith ShareBoth (wrapCols parts.Length) parts

    /// `facet` at a stated wrap width.
    let facetIn (cols: int) (parts: (string * Figure)[]) : Figure =
        facetWith ShareBoth cols parts

    // ---- Output ---------------------------------------------------------------------------------

    /// The scene, measured with the zero-dependency default measurer.
    let toScene (extent: Extent<px>) (fig: Figure) : Node =
        Figure.toScene TextMeasure.approx extent fig

    let toSceneWith (m: ITextMeasurer) (extent: Extent<px>) (fig: Figure) : Node = Figure.toScene m extent fig

    /// Drive any backend.
    ///
    /// This is the path that honours the backend contract in full: layout measures against the
    /// backend's **own** `ITextMeasurer` (§9's "measure–arrange pass against
    /// `IBackend.MeasureText`"), and the scene is then run through `Degrade.forCaps` and
    /// `Degrade.splitBatches` so a backend that cannot do gradients, dashes, clipping or
    /// unbounded batches gets a defensible approximation rather than wrong output (§5, D10).
    let renderTo (backend: IBackend) (extent: Extent<px>) (fig: Figure) : unit =
        let scene =
            Figure.toScene backend.Measurer extent fig
            |> Degrade.forCaps backend.Caps
            |> Degrade.splitBatches backend.Caps

        backend.Begin extent
        backend.Draw scene
        backend.Finish()

    /// Render with a plain scene renderer — `SvgBackend.render` has exactly this shape.
    ///
    /// `Marey.Plot` takes no backend dependency, on purpose: §0's diagram has the DSLs above the
    /// IR and the backends below it, and a grammar package that references a renderer inverts
    /// that. Passing the renderer in costs one identifier and keeps the layering honest.
    let render (renderer: Extent<px> -> Node -> string) (extent: Extent<px>) (fig: Figure) : string =
        renderer extent (toScene extent fig)

    /// One line to a file: `fig |> Chart.toFile SvgBackend.render "plot.svg" (Chart.size 800.0 500.0)`.
    ///
    /// UTF-8 without a byte-order mark, matching `SvgBackend.renderToFile`, because these files
    /// are golden masters (§13) and a BOM appearing would read as a whole-file diff.
    ///
    /// Backend-neutral by name as well as by shape: any `Extent<px> -> Node -> string` renderer
    /// fits, so this is equally the way to write a `.tex` file.
    let toFile (renderer: Extent<px> -> Node -> string) (path: string) (extent: Extent<px>) (fig: Figure) : unit =
        let text = render renderer extent fig
        System.IO.File.WriteAllText(path, text, System.Text.UTF8Encoding(false))

    /// `toFile` under its original name.
    ///
    /// Kept because it appears in the README and in every M0-era example, and because renaming a
    /// public entry point is not worth a breaking change. It was always renderer-shaped rather
    /// than SVG-specific — the name was the only thing that said otherwise.
    let toSvg (renderer: Extent<px> -> Node -> string) (path: string) (extent: Extent<px>) (fig: Figure) : unit =
        toFile renderer path extent fig
