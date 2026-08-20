namespace Marey

/// §9 / D43 / D44 — **the accumulated encoding block**, before it becomes a `Figure`.
///
/// Public only because `PlotBuilder`'s members are, and F# requires a type to be at least as
/// accessible as the signatures it appears in. Nothing constructs one by hand: every field is
/// written by a custom operation and read once, by `Run`.
///
/// The channels are stored as **constructors** rather than as finished `Channel` values. A
/// categorical channel's colours come off the theme's palette, and `theme` may appear anywhere
/// in the block — including after the channel it recolours. Deferring construction to `Run` is
/// what makes the block order-free in the one place a reader would never suspect order
/// mattered.
/// §9 / D47 — **the stat a block runs between its data and its geom**, if it runs one.
///
/// D47's finding is that stats do not have one shape, and this type is that finding written
/// down. `fit` takes the block's two positional channels and adds a *third* thing to draw;
/// `histogram`, `density` and `ecdf` take the x channel alone and **manufacture y**. A single
/// pipeline node over a channel pair would have fitted the first and forced a y channel on the
/// other three that is ignored — so there is no such node, and the cases are enumerated
/// instead. The compiler then has to be told what each one means, which is the whole argument
/// D45 makes for putting the levels in `ScaleKind`.
///
/// Nothing here computes: every case is one call into `Marey.Core`'s `Stat`, which is where the
/// arithmetic lives and where the measures are still typed.
[<NoComparison; NoEquality>]
type StatSpec =
    | StatNone
    /// A least-squares line over the block's own x and y, as an extra layer. The transform
    /// styles that layer, against the block's base style.
    | StatFit of restyle: (GeomStyle -> GeomStyle)
    /// Counts against bin centres. `None` bins by Freedman–Diaconis.
    | StatHistogram of bins: int option
    /// A Gaussian KDE at Silverman's bandwidth, on a grid of this many points.
    | StatDensity of points: int option
    /// The empirical CDF, as a staircase.
    | StatEcdf

[<NoComparison; NoEquality>]
type PlotSpec =
    { /// The series name. One legend entry, on the first layer only — see `PlotBuilder.Run`.
      Name: string
      /// The positional channels, each erased where it was declared (D44 finding 2).
      X: PackedChannel option
      Y: PackedChannel option
      /// Non-positional channels, in declaration order, as functions of the final theme.
      Channels: (Theme -> Channel) list
      /// The quantity name a continuous colour channel puts in front of its unit: `T` in
      /// `T [K]`. Empty means "read it from the dimension", exactly as an axis does.
      ColorLabel: string
      /// One entry per `layer`, in declaration order, with its style transform.
      Layers: (Geom * (GeomStyle -> GeomStyle)) list
      /// The stat, if any. **One per block, not one per layer** — see `StatSpec`.
      Stat: StatSpec
      Theme: Theme
      Coord: Coord }

/// The parts of `Run` that are generic over a measure, and the empty spec.
///
/// A private module rather than members on the builder: a helper generic over `[<Measure>] 'u`
/// has to be a `let` binding to be usable from two custom operations at two different measures.
module private PlotCompile =

    let empty: PlotSpec =
        { Name = ""
          X = None
          Y = None
          Channels = []
          ColorLabel = ""
          Layers = []
          Stat = StatNone
          Theme = Theme.initial
          Coord = Coord.cartesian }

    /// The domain a non-positional channel takes when nobody states one: the data's own range,
    /// **unpadded**. Identical to `Chart.colorByWith`'s choice and for its reason — an axis pads
    /// so that the extreme points are not on the frame, and a ramp has no frame to sit on.
    let domainFor (values: float<'u>[]) : float<'u> * float<'u> =
        match Scale.domainOf values with
        | Some d -> d
        | None -> LanguagePrimitives.FloatWithMeasure<'u> 0.0, LanguagePrimitives.FloatWithMeasure<'u> 1.0

    /// A categorical channel's two arrays: the level set the keys themselves define, in
    /// first-appearance order, and one level number per row (D45).
    let leveled (keys: 'k[]) : string[] * float[] =
        let levels = Levels.ofSeq keys
        levels.Labels, Levels.positions levels keys

    let append (spec: PlotSpec) (mk: Theme -> Channel) =
        { spec with
            Channels = spec.Channels @ [ mk ] }

    /// The one error the block cannot turn into a compile error.
    ///
    /// Everything else a block can get wrong is caught by the compiler: a mismatched row type,
    /// an accessor that disagrees with its witness, a channel of the wrong measure. A *missing*
    /// channel is not, because the builder's state has the same type whether or not it has been
    /// written to, so this is the message that has to do the work instead.
    let required (what: string) (c: PackedChannel option) : PackedChannel =
        match c with
        | Some c -> c
        | None ->
            failwithf
                "Marey: this plot block has no %s channel. Add `%s <witness> (fun r -> …)` — for example `%s U.metre (fun r -> r.Displacement)`."
                what
                what
                what

    /// **The fitted line, built where both measures exist and nowhere else** (D47).
    ///
    /// `Stat.regression` is typed — its slope is `float<'uy/'ux>` — and D44 finding 3 says the
    /// only scope in which that type can be written is the body of the inner `Apply`. So the
    /// call is here, and what comes back out is an ordinary `PackedSeries` of two points: the
    /// line, with the slope spent on it. A caller who wants the *number* calls
    /// `Stat.regression` themselves, where it is still `float<m/s>` — see `Chart.addFit`.
    let private fitSeries (x: PackedChannel) (y: PackedChannel) : PackedSeries =
        Erase.reunify
            x
            y
            { new IChannelPairConsumer<PackedSeries> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(ux: U<'ux>, xs: float<'ux>[], uy: U<'uy>, ys: float<'uy>[]) =
                    let f = Stat.regression ux uy xs ys
                    let fx, fy = Stat.fitLine f xs
                    Erase.series "" (Erase.autoScale ux fx) (Erase.autoScale uy fy) fx fy }

    /// The name of a stat, for the messages below. A block reports the operation the caller
    /// wrote, not the case name of the union it became.
    let private statName (stat: StatSpec) =
        match stat with
        | StatNone -> ""
        | StatFit _ -> "fit"
        | StatHistogram _ -> "histogram"
        | StatDensity _ -> "density"
        | StatEcdf -> "ecdf"

    let private withCoord (c: Coord) (fig: Figure) =
        { fig with
            Panels = fig.Panels |> Array.map (fun p -> { p with Coord = c }) }

    /// §9 / D47 — **a block whose y comes from a stat rather than from the data.**
    ///
    /// `histogram`, `density` and `ecdf` are one-channel stats: they take a sample and produce a
    /// distribution over it, so the block states `x` and the stat supplies y. Everything a
    /// two-channel block can also carry is refused here rather than ignored, and the three
    /// refusals are the finding:
    ///
    /// * **`y` is refused** because the stat is what y *is*. Accepting one and dropping it would
    ///   let a caller write a y channel and never learn it was not drawn.
    /// * **A channel is refused** because a stat changes the row count. D53 makes a channel the
    ///   block's rather than a layer's, and every channel is one `Array.map` over the block's
    ///   rows — which is exactly the array a histogram replaces with one entry per bin. There is
    ///   no correspondence left to attach.
    /// * **`layer` is refused** because these stats choose their own mark, and it is not a free
    ///   choice: a histogram's bars are full width and measured from zero, and an ECDF is a
    ///   staircase. Drawing a histogram as a line is a different chart, not a restyling.
    let private runDistribution (stat: StatSpec) (spec: PlotSpec) : Figure =
        let what = statName stat

        if spec.Y.IsSome then
            failwithf
                "Marey: a `%s` block has no y channel — the stat is what supplies y (it is a count, a density or a probability, not something in the data). Remove the `y` operation."
                what

        if not (List.isEmpty spec.Channels) then
            failwithf
                "Marey: a `%s` block cannot carry a colour, size or shape channel. The stat changes the row count, so a per-row channel has nothing left to line up with (D47). Facet the block instead, or bin each group yourself and add the series."
                what

        if not (List.isEmpty spec.Layers) then
            failwithf
                "Marey: a `%s` block chooses its own mark — a histogram is full-width bars from zero, a density is a curve, an ECDF is a staircase — so `layer` has nothing to add here."
                what

        let x = required "x" spec.X

        let fig =
            x.Accept
                { new IChannelConsumer<Figure> with
                    member _.Apply<[<Measure>] 'u>(u: U<'u>, xs: float<'u>[]) =
                        match stat with
                        | StatHistogram bins ->
                            Chart.ofHistogram (
                                match bins with
                                | Some b -> Stat.binWith u b xs
                                | None -> Stat.bin u xs
                            )
                        | StatDensity points ->
                            Chart.ofDensity (
                                match points with
                                | Some n -> Stat.densityWith u (Stat.bandwidth xs) n xs
                                | None -> Stat.density u xs
                            )
                        | StatEcdf -> Chart.ofEcdf (Stat.ecdf u xs)
                        // Unreachable: `run` dispatches here on exactly the three cases above.
                        // Spelled out rather than wildcarded so that a seventh stat is a build
                        // error here and not a silent fall-through.
                        | StatNone
                        | StatFit _ -> failwith "Marey: internal — runDistribution called without a distribution stat." }

        fig
        |> Chart.withTheme spec.Theme
        |> (if System.String.IsNullOrEmpty spec.Name then id else Chart.withName spec.Name)
        |> withCoord spec.Coord

    let private runPositional (spec: PlotSpec) : Figure =
        let packed =
            Erase.channelSeries spec.Name (required "x" spec.X) (required "y" spec.Y)

        let theme = spec.Theme

        // The label is applied here rather than inside the constructor because `colorLabel` may
        // appear before or after the channel it names, and the block is order-free.
        let labelled (c: Channel) =
            match c.Map with
            | ColorByValue _ when not (System.String.IsNullOrEmpty spec.ColorLabel) -> { c with Label = spec.ColorLabel }
            | ColorByValue _
            | ColorByLevel _
            | ShapeByLevel _
            | SizeByValue _ -> c

        let channels =
            spec.Channels |> List.map (fun mk -> labelled (mk theme)) |> List.toArray

        // A block with no `layer` is a scatter: two positional channels and nothing else is a
        // statement about where the points are, and points are how that is drawn. Choosing
        // `GeomLine` instead would additionally assert that consecutive rows are adjacent, which
        // the block never said.
        let specs =
            if spec.Layers.IsEmpty then
                [ GeomPoint, id ]
            else
                spec.Layers

        // **Every layer of one block shares one base style**, index 0 — unlike `Chart.addLine`,
        // which advances the palette because each of its calls adds a *different series*. The
        // layers here are alternative renderings of a single encoding: `layer GeomLine` beside
        // `layer GeomPoint` is a line with markers, and drawing them in two palette colours
        // would say there were two things to tell apart.
        let baseStyle = Geom.styleFor theme 0

        let layers =
            specs
            |> List.mapi (fun i (g, restyle) ->
                { Name = (if i = 0 then spec.Name else "")
                  Data = packed
                  Geom = g
                  Style = restyle baseStyle
                  // Channels reach *every* layer, because an encoding belongs to the block
                  // rather than to one geom. The length check `Chart.withChannel` performs is
                  // not repeated here and does not need to be: every array in the block is one
                  // `Array.map` over the same rows, so a channel cannot be a different length
                  // from its series.
                  Channels = channels
                  // Only the first layer is in the legend. All the layers share one series and
                  // one set of channels, so a second contribution would enumerate the same
                  // thing twice under a different swatch. An *unnamed* first layer still
                  // contributes its levels and its size key — `Figure.layerEntries` drops the
                  // layer's own entry when the name is empty, which is the same rule the quick
                  // front door plays by.
                  ShowInLegend = i = 0 })

        // **The fitted line is a layer, and it is appended — the order is load-bearing** (D47).
        //
        // It carries no channels, because it has two points and the channels have one entry per
        // row; and it is not in the legend, because D54 makes the layers of a block one series
        // and a fit is a statement *about* that series rather than a second one. Appending is
        // what keeps both true without touching the legend rule: D53 puts the whole legend entry
        // — the layer's name *and* its channels' levels — on the first layer, so a fit line
        // prepended instead would take the legend with it and silently drop every level of a
        // categorical channel. That is D53's near-miss arrived at from the other side.
        let layers =
            match spec.Stat with
            | StatFit restyle ->
                layers
                @ [ { Name = ""
                      Data = fitSeries (required "x" spec.X) (required "y" spec.Y)
                      Geom = GeomLine
                      Style = restyle baseStyle
                      Channels = [||]
                      ShowInLegend = false } ]
            | _ -> layers

        let panel =
            { Figure.panelOf (List.toArray layers) with
                Coord = spec.Coord }

        { Figure.create [| panel |] with Theme = theme }

    /// Which of the two shapes of block this is (D47).
    let run (spec: PlotSpec) : Figure =
        match spec.Stat with
        | StatNone
        | StatFit _ -> runPositional spec
        | distribution -> runDistribution distribution spec

/// §9 — **the grammar-of-graphics front door**: one dataset, several channels, several layers.
///
/// ```fsharp
/// plot measurements {
///     x U.second (fun r -> r.Time)
///     y U.metre  (fun r -> r.Displacement)
///     color (fun r -> r.Sample)
///     size U.kilogram (fun r -> r.Mass)
///     layerWith GeomPoint (Geom.withOpacity 0.6)
///     theme Theme.publication
/// }
/// ```
///
/// ### The witness is stated, and that is permanent (D43)
///
/// §9 originally had the block *infer* the unit from the accessor's return type. It cannot be
/// built: a measure parameter cannot be reflected on, cannot carry a member constraint, and
/// cannot be dispatched on by overload, because erasure makes `'r -> float&lt;s&gt;` and
/// `'r -> float&lt;m&gt;` the same signature. So a positional channel takes its witness exactly
/// as `Chart.line` does — and then **checks the accessor against it**: naming `r.Displacement`
/// under `U.second` is a compile error, in the measures, at the call site.
///
/// What the block adds is therefore composition and not inference: channels beyond x and y,
/// layers over one dataset, a stat between the data and the geom, and — from phase 5 — facets.
/// Everything downstream of the witness is still derived from it and never configured: the
/// scale, the domain, the prefix, the ticks and both axis titles.
///
/// ### A block has one stat, and the stat decides the block's shape (D47)
///
/// `fit` adds a least-squares line over the block's own two channels. `histogram`, `density` and
/// `ecdf` take the x channel *alone* and manufacture y — a count, a density, a probability — so
/// those blocks have no `y` operation, and refuse a `layer` or an encoding channel rather than
/// dropping it. The two shapes are why `StatSpec` enumerates rather than abstracting: a single
/// pipeline node over a channel pair fits the first and forces a dead y channel on the rest.
///
/// ### The block is flat, and that is a type check rather than a taste (D44)
///
/// There is no nested `encode { … }`. It does not parse — a custom operation and a builder
/// value cannot share a name — and where it does parse it severs the row type: the inner
/// builder's `'r` is inferred from the accessors inside the block and never unified with the
/// data's. A block whose accessors take an unrelated record then compiles and fails later, or
/// worse, does not.
///
/// Flat closes that hole, because `'r` arrives from the data through this type's own parameter.
/// The mismatch is `error FS0001: This expression was expected to have type 'Row' but here has
/// type 'Other'`, and `ChannelTests` keeps it a compile *failure* rather than a paragraph.
///
/// ### It returns an ordinary `Figure`
///
/// Not a private type, not a builder to be finished elsewhere: the block ends in the same
/// `Figure` `Chart.line` produces, so the whole `Chart.with*` surface — titles, axis labels,
/// log scales, domains, legend placement, output — composes onto it with `|>` and none of it
/// has to be restated as a custom operation.
type PlotBuilder<'r>(rows: 'r[]) =

    /// The dataset, materialized once. `plot` takes a `seq` and every accessor is one
    /// `Array.map` over *this* array, so a lazy or single-shot sequence is enumerated exactly
    /// once however many channels read it — and two channels can never disagree about the row
    /// count.
    member _.Rows = rows

    member _.Yield(_: unit) : PlotSpec = PlotCompile.empty
    member _.Zero() : PlotSpec = PlotCompile.empty
    member _.Delay(f: unit -> PlotSpec) : PlotSpec = f ()
    member _.Run(spec: PlotSpec) : Figure = PlotCompile.run spec

    // ---- Positional channels ------------------------------------------------------------------

    /// The x channel: a witness, and an accessor checked against it.
    [<CustomOperation("x")>]
    member _.X(spec: PlotSpec, u: U<'u>, f: 'r -> float<'u>) : PlotSpec =
        { spec with
            X = Some(Erase.packChannel u (Array.map f rows)) }

    /// The y channel: a witness, and an accessor checked against it.
    [<CustomOperation("y")>]
    member _.Y(spec: PlotSpec, u: U<'u>, f: 'r -> float<'u>) : PlotSpec =
        { spec with
            Y = Some(Erase.packChannel u (Array.map f rows)) }

    // ---- Non-positional channels (D46, D49, D51) ----------------------------------------------
    //
    // Colour is the only channel spelled two ways, and the reason is `ChannelMap`'s four arms: a
    // category can drive colour or shape, a quantity can drive colour or size, and nothing else
    // is possible. There is no continuous shape — a shape does not interpolate — and no
    // categorical size, because a size that is not ordered says nothing. So `shape` needs no
    // continuous spelling and `size` needs no categorical one, and only colour has two halves
    // to name. They are two operations rather than one overloaded one deliberately: overload
    // resolution on a custom operation would have to run before the accessor's lambda is
    // typed, which is where the error message a caller sees stops naming their mistake.

    /// **Colour by a category.** One colour per level, one batch per level, one legend entry per
    /// level — and the levels come from the keys themselves, in first-appearance order.
    ///
    /// Applied together with `shape` over the same keys this is §9's colour-blind-safe scatter,
    /// and it is still one batch per level: both channels read the same level number, so the
    /// grouping does not take their product.
    [<CustomOperation("color")>]
    member _.Color(spec: PlotSpec, key: 'r -> 'k) : PlotSpec =
        let labels, positions = PlotCompile.leveled (Array.map key rows)
        PlotCompile.append spec (fun theme -> Channel.colorByLevel theme labels positions)

    /// **Shape by a category** — the redundant encoding that keeps a scatter readable in
    /// greyscale and under colour-vision deficiency (D5).
    [<CustomOperation("shape")>]
    member _.Shape(spec: PlotSpec, key: 'r -> 'k) : PlotSpec =
        let labels, positions = PlotCompile.leveled (Array.map key rows)
        PlotCompile.append spec (fun theme -> Channel.shapeByLevel theme labels positions)

    /// **Colour by a quantity**, over the theme's sequential ramp.
    ///
    /// The witness buys here what it buys on an axis: the colorbar labels itself `T [K]`, picks
    /// its own prefix, and takes its ticks from the axis compiler (D49).
    [<CustomOperation("colorBy")>]
    member _.ColorBy(spec: PlotSpec, u: U<'u>, f: 'r -> float<'u>) : PlotSpec =
        let values = Array.map f rows
        let scale = Scale.linear u (PlotCompile.domainFor values)
        PlotCompile.append spec (fun theme -> Channel.colorByValue "" theme.Sequential scale values)

    /// `colorBy` over a stated ramp rather than the theme's.
    [<CustomOperation("colorByWith")>]
    member _.ColorByWith(spec: PlotSpec, ramp: Ramp, u: U<'u>, f: 'r -> float<'u>) : PlotSpec =
        let values = Array.map f rows
        let scale = Scale.linear u (PlotCompile.domainFor values)
        PlotCompile.append spec (fun _ -> Channel.colorByValue "" ramp scale values)

    /// Name the quantity a continuous colour channel encodes: `T` in `T [K]`. The unit half is
    /// never configured, exactly as on an axis, and without this the name is read from the
    /// channel's dimension.
    [<CustomOperation("colorLabel")>]
    member _.ColorLabel(spec: PlotSpec, label: string) : PlotSpec = { spec with ColorLabel = label }

    /// **Size by a quantity**, over a range centred on the theme's marker size.
    ///
    /// Interpolated in area rather than in diameter, because area is what the eye takes off a
    /// disc (D51). Size is the weakest channel for accurate reading, so it is the one to reach
    /// for last: what it produces is three marks in the legend, not a bar.
    [<CustomOperation("size")>]
    member _.Size(spec: PlotSpec, u: U<'u>, f: 'r -> float<'u>) : PlotSpec =
        let values = Array.map f rows
        let scale = Scale.linear u (PlotCompile.domainFor values)

        PlotCompile.append spec (fun theme ->
            let m = theme.MarkerSize
            Channel.sizeByValue "" (m * 0.5) (m * 3.0) scale values)

    /// `size` between two stated device diameters.
    [<CustomOperation("sizeWith")>]
    member _.SizeWith(spec: PlotSpec, smallest: float<px>, largest: float<px>, u: U<'u>, f: 'r -> float<'u>) : PlotSpec =
        let values = Array.map f rows
        let scale = Scale.linear u (PlotCompile.domainFor values)
        PlotCompile.append spec (fun _ -> Channel.sizeByValue "" smallest largest scale values)

    // ---- Layers -------------------------------------------------------------------------------

    /// Draw the encoding with a geom. Repeatable: two `layer` operations are two renderings of
    /// the one series, in declaration order, back to front.
    ///
    /// `GeomArea` and `GeomBar` take a **baseline in panel space**, and the block cannot supply
    /// a good one: `Geom.area` and `Geom.bar` derive it from the y *scale*, which does not exist
    /// until `Run` reunifies the channels. Passing one by hand compiles and draws, but it is a
    /// number the caller has no way to compute — so the honest spelling of a filled block is
    /// still `Chart.area` / `Chart.bar`, and the block's own version arrives with the geoms.
    [<CustomOperation("layer")>]
    member _.Layer(spec: PlotSpec, g: Geom) : PlotSpec =
        { spec with
            Layers = spec.Layers @ [ g, id ] }

    /// A layer with its style adjusted: `layerWith GeomPoint (Geom.withOpacity 0.6)`.
    ///
    /// The transform runs against the theme's style for the block, so it states a difference
    /// from the theme rather than replacing it — `Geom.withOpacity 0.6` leaves the colour, the
    /// width and the marker alone.
    [<CustomOperation("layerWith")>]
    member _.LayerWith(spec: PlotSpec, g: Geom, restyle: GeomStyle -> GeomStyle) : PlotSpec =
        { spec with
            Layers = spec.Layers @ [ g, restyle ] }

    // ---- Stats (§9, D47) --------------------------------------------------------------------
    //
    // **One stat per block, and the last one written wins** — the same rule a repeated `x`
    // plays by. A block with two stats would have to say what a density of a histogram means,
    // and it does not mean anything.
    //
    // The two shapes are not interchangeable and the block will say so: `fit` needs both
    // positional channels and adds a layer; `histogram`, `density` and `ecdf` take `x` alone and
    // *are* the layer, so a `y` channel, an encoding channel or a `layer` in one of those blocks
    // is refused rather than ignored. `StatSpec` carries the argument.

    /// **A least-squares fit of y against x**, drawn over the range of the data as an extra
    /// layer in the block's own colour.
    ///
    /// ```fsharp
    /// plot runs {
    ///     x U.second (fun r -> r.Time)
    ///     y U.metre  (fun r -> r.Displacement)
    ///     fit
    /// }
    /// ```
    ///
    /// The block draws the line and does not report the slope, which is D47's split rather than
    /// an omission: the slope of metres against seconds is a `float&lt;m/s&gt;`, and that type
    /// exists only inside the scope where the two channels are reunified (D44 finding 3). A
    /// caller who wants the number calls `Stat.regression` on the same two arrays, where it is
    /// still typed, still bindable to an annotated `float&lt;m/s&gt;`, and comes with `R2`, `N`
    /// and the count of non-finite pairs the fit ignored.
    [<CustomOperation("fit")>]
    member _.Fit(spec: PlotSpec) : PlotSpec = { spec with Stat = StatFit id }

    /// `fit` with the fitted line styled: `fitWith (Geom.withDash [| 4.0&lt;px&gt;; 3.0&lt;px&gt; |])`.
    ///
    /// The transform runs against the block's base style, so it states a difference — dashing a
    /// fit is the conventional way to say "this is a model and not a measurement" without
    /// claiming it belongs to a different series.
    [<CustomOperation("fitWith")>]
    member _.FitWith(spec: PlotSpec, restyle: GeomStyle -> GeomStyle) : PlotSpec =
        { spec with Stat = StatFit restyle }

    /// **A histogram of the x channel**: counts against bin centres, bars full width, measured
    /// from zero. Bins by Freedman–Diaconis.
    ///
    /// ```fsharp
    /// plot samples { x U.metre (fun r -> r.Height); histogram }
    /// ```
    ///
    /// There is no `y` in that block and there cannot be: a count is not a column of the data.
    [<CustomOperation("histogram")>]
    member _.Histogram(spec: PlotSpec) : PlotSpec =
        { spec with Stat = StatHistogram None }

    /// `histogram` with a stated bin count.
    ///
    /// A stated bin *width* or domain goes through `Stat.binIn` and `Chart.ofHistogram`, because
    /// both are measured in the sample's own unit and a custom operation cannot name that
    /// measure — it belongs to the `x` channel's witness, which the operation cannot see.
    [<CustomOperation("histogramWith")>]
    member _.HistogramWith(spec: PlotSpec, bins: int) : PlotSpec =
        { spec with Stat = StatHistogram(Some bins) }

    /// **A Gaussian kernel density estimate of the x channel**, at Silverman's bandwidth.
    ///
    /// The y axis is in **reciprocal** x units — a density over metres is per metre — so it
    /// labels itself `m⁻¹` off a witness nobody wrote, and a density layer cannot share a panel
    /// with the sample it summarises. Both are consequences of `Stat.density`'s return type.
    [<CustomOperation("density")>]
    member _.Density(spec: PlotSpec) : PlotSpec = { spec with Stat = StatDensity None }

    /// `density` on a grid of a stated number of points. The bandwidth is still Silverman's,
    /// for the reason `histogramWith` gives: a stated one is measured in x's unit.
    [<CustomOperation("densityWith")>]
    member _.DensityWith(spec: PlotSpec, points: int) : PlotSpec =
        { spec with Stat = StatDensity(Some points) }

    /// **The empirical CDF of the x channel**, drawn as the staircase it is rather than
    /// interpolated between observations.
    ///
    /// y is a probability, so it is `float&lt;1&gt;` whatever x is measured in, and the axis runs
    /// `0 … 1` because that is the range of the function and not of this sample.
    [<CustomOperation("ecdf")>]
    member _.Ecdf(spec: PlotSpec) : PlotSpec = { spec with Stat = StatEcdf }

    // ---- The rest of the block ------------------------------------------------------------

    /// Name the series. This is what the legend shows, and it appears once however many layers
    /// the block has.
    [<CustomOperation("name")>]
    member _.Name(spec: PlotSpec, n: string) : PlotSpec = { spec with Name = n }

    /// The theme. It may appear anywhere in the block: every channel is built against it at
    /// `Run`, so a `color` declared three lines above still takes this theme's palette.
    [<CustomOperation("theme")>]
    member _.Theme(spec: PlotSpec, t: Theme) : PlotSpec = { spec with Theme = t }

    /// The coordinate system the panel is drawn in.
    [<CustomOperation("coord")>]
    member _.Coord(spec: PlotSpec, c: Coord) : PlotSpec = { spec with Coord = c }

/// §9 — the encoding block's one entry point.
[<AutoOpen>]
module Grammar =

    /// Open an encoding block over a dataset.
    ///
    /// The row type `'r` arrives here and is carried by the builder, which is what makes an
    /// accessor over an unrelated record a compile error rather than a runtime one (D44
    /// finding 4). The sequence is materialized once, so a lazy one is safe.
    let plot (data: 'r seq) : PlotBuilder<'r> = PlotBuilder<'r>(Seq.toArray data)
