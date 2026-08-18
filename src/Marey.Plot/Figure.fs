namespace Marey

/// One drawn thing: data, a geom, and a style.
///
/// `Data` is a `PackedSeries` — §2.3's rank-2 existential — which is what lets a `Figure` hold a
/// metres-against-seconds panel beside a joules-against-kelvin one and still be a single value.
[<NoComparison>]
type Layer =
    { Name: string
      Data: PackedSeries
      Geom: Geom
      Style: GeomStyle
      ShowInLegend: bool }

/// Per-axis configuration. Every field has a defensible default, which is what "no
/// configuration" in §9 has to mean in practice.
[<NoComparison>]
type AxisConfig =
    { /// The quantity name. `None` takes the conventional symbol for the dimension —
      /// `Axes.defaultLabel` — which is how `t [s]` appears without anyone typing `t`.
      Label: string option
      /// Per-axis override of the theme's unit spelling. `None` defers to `Theme.UnitStyle`,
      /// which is `Unicode` for screen and SVG themes and `Siunitx` for `Theme.tikz`.
      ///
      /// An override rather than a value, so that selecting a theme is enough: with a plain
      /// `UnitStyle` here there would be no way to tell an explicit `Unicode` from an unset
      /// field, and a theme could never supply a different default.
      Style: UnitStyle option
      TickCount: int
      /// `None` defers to the theme.
      Grid: bool option
      Visible: bool
      ShowTitle: bool }

[<NoComparison>]
type Panel =
    { Title: string
      Row: int
      Col: int
      Layers: Layer[]
      XAxis: AxisConfig
      YAxis: AxisConfig
      /// §4 — cartesian is the identity. Nothing else in this file knows polar exists.
      Coord: Coord
      Legend: LegendPlacement }

[<NoComparison>]
type Figure =
    { Title: string
      Subtitle: string
      Rows: int
      Cols: int
      Panels: Panel[]
      Theme: Theme }

/// **The compiler.** A `Figure` in, a `Node` out, nothing in between that a backend can see.
///
/// This is the layer §5.5 describes: "axes, legends, titles, colorbars, facets, error bars, box
/// plots … are all *compiled into* `Path`/`Text`/`Markers` by the layer above." Everything below
/// emits one of the nine existing cases. No `Node` case was added, and none was needed.
///
/// ### The three loops, and why there are three
///
/// 1. **Measure–arrange** inside `Layout.arrangeIn`: content tracks need the text metrics.
/// 2. **Arrange twice per panel**: the tick *plan* — how many labels survive and whether they
///    tilt — depends on how wide the plot area is, and how wide the plot area is depends on how
///    much room the tick labels need. One iteration settles it: arrange with an estimated plot
///    size, re-plan against the real one, arrange again. A third pass changes nothing measurable
///    because the plan only ever coarsens the labels, and a coarser plan needs no more room.
/// 3. **Outer then inner grid**: the figure is a grid of panels; each panel is a grid of
///    gutters. Same solver, twice, which is the entire argument for having a solver.
[<RequireQualifiedAccess>]
module Figure =

    let private isEmpty (s: string) = System.String.IsNullOrEmpty s

    // ---- Crossing the existential --------------------------------------------------------

    /// The two scales of a packed series, still packed. §2.3's encoding hands the measures back
    /// inside `Apply`; this repacks them one at a time so the axis compiler can take either.
    let packedScales (p: PackedSeries) : PackedScale * PackedScale =
        p.Accept
            { new ISeriesConsumer<PackedScale * PackedScale> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) =
                    Erase.packScale s.XScale, Erase.packScale s.YScale }

    let private ofKind (k: ScaleKind) (u: U<'u>) (d: float<'u> * float<'u>) : Scale<'u> =
        match k with
        | ScaleKind.Linear -> Scale.linear u d
        | ScaleKind.Log b -> Scale.logBase b u d
        | ScaleKind.Symlog(b, l) -> Scale.symlog b l u d
        | ScaleKind.Power p -> Scale.power p u d
        | ScaleKind.Reciprocal -> Scale.reciprocal u d

    /// Rebuild a packed series over new raw domains.
    ///
    /// The floats are raw and the measures are reattached inside `Apply`, where they are known —
    /// so this is measure-safe even though the caller could not name the measure. It is how two
    /// layers on one panel come to share an axis: their domains are unioned as numbers and each
    /// series is rebuilt over the union in its own unit.
    let withDomains (dx: (float * float) option) (dy: (float * float) option) (p: PackedSeries) : PackedSeries =
        p.Accept
            { new ISeriesConsumer<PackedSeries> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) =
                    let x =
                        match dx with
                        | None -> s.XScale
                        | Some(lo, hi) ->
                            Scale.withDomain
                                (LanguagePrimitives.FloatWithMeasure<'ux> lo, LanguagePrimitives.FloatWithMeasure<'ux> hi)
                                s.XScale

                    let y =
                        match dy with
                        | None -> s.YScale
                        | Some(lo, hi) ->
                            Scale.withDomain
                                (LanguagePrimitives.FloatWithMeasure<'uy> lo, LanguagePrimitives.FloatWithMeasure<'uy> hi)
                                s.YScale

                    Erase.packSeries { s with XScale = x; YScale = y } }

    /// Change a packed series' scale kind. A log scale needs a positive domain, so switching to
    /// one re-derives the domain from the positive data rather than raising on the zero that a
    /// padded linear domain will usually contain.
    let withKinds (kx: ScaleKind option) (ky: ScaleKind option) (p: PackedSeries) : PackedSeries =
        p.Accept
            { new ISeriesConsumer<PackedSeries> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) =
                    let needsPositive k =
                        match k with
                        | ScaleKind.Log _
                        | ScaleKind.Reciprocal -> true
                        | _ -> false

                    let x =
                        match kx with
                        | None -> s.XScale
                        | Some k ->
                            let d =
                                if needsPositive k then
                                    match Scale.domainOf (s.X |> Array.filter (fun v -> float v > 0.0)) with
                                    | Some d -> d
                                    | None -> s.XScale.Domain
                                else
                                    s.XScale.Domain

                            ofKind k s.XScale.Unit d

                    let y =
                        match ky with
                        | None -> s.YScale
                        | Some k ->
                            let d =
                                if needsPositive k then
                                    match Scale.domainOf (s.Y |> Array.filter (fun v -> float v > 0.0)) with
                                    | Some d -> d
                                    | None -> s.YScale.Domain
                                else
                                    s.YScale.Domain

                            ofKind k s.YScale.Unit d

                    Erase.packSeries { s with XScale = x; YScale = y } }

    // ---- Defaults ------------------------------------------------------------------------

    let axisConfig: AxisConfig =
        { Label = None
          // `None` means "take the theme's", which is what makes `Theme.tikz` sufficient on its
          // own. An `AxisConfig` cannot default to `Unicode` and still let a theme change it: an
          // explicit `Unicode` and an unset field would be indistinguishable, so a theme could
          // never tell whether it was allowed to supply a different one.
          Style = None
          TickCount = Axis.DefaultTickCount
          Grid = None
          Visible = true
          ShowTitle = true }

    let panelOf (layers: Layer[]) : Panel =
        { Title = ""
          Row = 0
          Col = 0
          Layers = layers
          XAxis = axisConfig
          YAxis = axisConfig
          Coord = Coord.cartesian
          Legend = LegendTopRight }

    let create (panels: Panel[]) : Figure =
        { Title = ""
          Subtitle = ""
          Rows = (if panels.Length = 0 then 1 else 1 + (panels |> Array.map (fun p -> p.Row) |> Array.max))
          Cols = (if panels.Length = 0 then 1 else 1 + (panels |> Array.map (fun p -> p.Col) |> Array.max))
          Panels = panels
          Theme = Theme.initial }

    // ---- Shared domains across the layers of one panel -------------------------------------

    /// Give every layer on a panel the same x and y domains.
    ///
    /// **This is where the one runtime dimensional check in the library lives.** Inside a single
    /// `Chart.line (xs, ys)` call the compiler already rejects metres against seconds, because
    /// the two arrays have different measures and `Series('ux,'uy)` names both. Across *two*
    /// layers the measures have been erased into the existential and F# cannot compare them, so
    /// the `Dim` values are compared at runtime instead, and a mismatch raises rather than
    /// silently plotting joules on a metre axis.
    let unify (p: Panel) : Panel =
        if p.Layers.Length < 2 then
            p
        else
            let scales = p.Layers |> Array.map (fun l -> Erase.seriesScales l.Data)
            let (x0, y0) = scales.[0]

            // Layers are usually unnamed until `Chart.withName` is applied, and "layer ''"
            // identifies nothing. This is the one dimensional error the *compiler* cannot
            // catch, so it is the one the message has to work hardest for.
            let describe i =
                let name = p.Layers.[i].Name
                if System.String.IsNullOrWhiteSpace name then
                    sprintf "layer %d" i
                else
                    sprintf "layer %d ('%s')" i name

            for i in 1 .. scales.Length - 1 do
                let (xi, yi) = scales.[i]

                if xi.Dim <> x0.Dim then
                    failwithf
                        "Marey: %s has an x axis in %s but %s has one in %s. Two physical dimensions cannot share an axis."
                        (describe 0)
                        (Dim.show x0.Dim)
                        (describe i)
                        (Dim.show xi.Dim)

                if yi.Dim <> y0.Dim then
                    failwithf
                        "Marey: %s has a y axis in %s but %s has one in %s. Two physical dimensions cannot share an axis."
                        (describe 0)
                        (Dim.show y0.Dim)
                        (describe i)
                        (Dim.show yi.Dim)

            let union (get: IScaleErased -> float * float) (pick: IScaleErased * IScaleErased -> IScaleErased) =
                let ds = scales |> Array.map (pick >> get)
                (ds |> Array.map fst |> Array.min), (ds |> Array.map snd |> Array.max)

            let dx = union (fun s -> s.Domain) fst
            let dy = union (fun s -> s.Domain) snd

            { p with
                Layers =
                    p.Layers
                    |> Array.map (fun l ->
                        { l with
                            Data = withDomains (Some dx) (Some dy) l.Data }) }

    // ---- Compilation -----------------------------------------------------------------------

    let private textBox (m: ITextMeasurer) (f: FontSpec) (s: string) =
        TextMeasure.bounds m f AnchorMiddle BaselineMiddle 0.0<rad> s

    let private legendEntries (p: Panel) =
        p.Layers
        |> Array.filter (fun l -> l.ShowInLegend && not (isEmpty l.Name))
        |> Array.map (fun l ->
            { Name = l.Name
              Color = l.Style.Color
              Mark = Geom.legendMark l.Geom l.Style })

    /// Compile one panel into a device-space region.
    let private compilePanel (m: ITextMeasurer) (theme: Theme) (extent: Extent<px>) (region: Rect<px>) (p: Panel) : Node =
        if p.Layers.Length = 0 then
            Scene.empty
        else
            let (xPacked, yPacked) = packedScales p.Layers.[0].Data

            // The one place the theme's unit spelling and a per-axis override meet. Resolved here
            // rather than at construction time so that changing the theme on a finished figure
            // changes its axis titles — which is what `Chart.withTheme Theme.tikz` has to do.
            let unitStyle (cfg: AxisConfig) = defaultArg cfg.Style theme.UnitStyle

            let xSpec =
                Axes.specOf (unitStyle p.XAxis) p.XAxis.TickCount p.XAxis.Label AxisBottom xPacked

            let ySpec =
                Axes.specOf (unitStyle p.YAxis) p.YAxis.TickCount p.YAxis.Label AxisLeft yPacked

            let titleH =
                if isEmpty p.Title then
                    0.0<px>
                else
                    Rect.height (textBox m theme.PanelTitleFont p.Title) + theme.TitleGap

            // Build the inner grid for a given tick plan. Called twice: once against an
            // estimated plot size, once against the real one.
            let build (xPlan: TickPlan) (yPlan: TickPlan) =
                let xTicksH =
                    if p.XAxis.Visible then
                        Axes.tickExtent m theme AxisBottom xSpec xPlan
                    else
                        0.0<px>

                let yTicksW =
                    if p.YAxis.Visible then
                        Axes.tickExtent m theme AxisLeft ySpec yPlan
                    else
                        0.0<px>

                let xTitleH =
                    if p.XAxis.Visible && p.XAxis.ShowTitle then
                        Axes.titleExtent m theme xSpec.Title
                    else
                        0.0<px>

                let yTitleW =
                    if p.YAxis.Visible && p.YAxis.ShowTitle then
                        Axes.titleExtent m theme ySpec.Title
                    else
                        0.0<px>

                // Half of the outermost tick label hangs past the end of the axis. Reserving it
                // is half of §13's "labels inside bounds"; without it the right-hand x label and
                // the top y label are clipped by the figure edge.
                let rightPad =
                    if p.XAxis.Visible then
                        Axes.labelOverhang m theme AxisBottom xSpec xPlan
                    else
                        0.0<px>

                let topPad =
                    if p.YAxis.Visible then
                        Axes.labelOverhang m theme AxisLeft ySpec yPlan
                    else
                        0.0<px>

                let cols =
                    [| Track.Content yTitleW; Track.Content yTicksW; Track.Frac 1.0; Track.Content rightPad |]

                let rows =
                    [| Track.Content titleH
                       Track.Content topPad
                       Track.Frac 1.0
                       Track.Content xTicksH
                       Track.Content xTitleH |]

                let fixedCell key row col (w: float<px>) (h: float<px>) =
                    Layout.cell key row col (fun _ -> { Width = w; Height = h })

                let cells =
                    [| fixedCell "ytitle" 2 0 yTitleW 0.0<px>
                       fixedCell "yticks" 2 1 yTicksW 0.0<px>
                       Layout.fill "plot" 2 2
                       fixedCell "rightpad" 2 3 rightPad 0.0<px>
                       fixedCell "ptitle" 0 0 0.0<px> titleH |> Layout.spanning 1 4
                       fixedCell "toppad" 1 0 0.0<px> topPad |> Layout.spanning 1 4
                       fixedCell "xticks" 3 2 0.0<px> xTicksH
                       fixedCell "xtitle" 4 2 0.0<px> xTitleH |]

                Layout.arrangeIn m extent region (Layout.grid rows cols cells)

            // Iteration 1: plan against an estimate of the plot area. 78% x 72% of the region is
            // what a labelled panel typically ends up with; the estimate only has to be close
            // enough that the second pass converges.
            let estW = Rect.width region * 0.78
            let estH = Rect.height region * 0.72

            let plan (w: float<px>) (h: float<px>) =
                Axes.planTicks m theme.TickFont theme.LabelGap w AxisBottom xSpec.Ticks,
                Axes.planTicks m theme.TickFont theme.LabelGap h AxisLeft ySpec.Ticks

            let (xPlan0, yPlan0) = plan estW estH
            let a0 = build xPlan0 yPlan0
            let plot0 = Layout.findPx "plot" a0

            let (xPlan, yPlan) = plan (Rect.width plot0) (Rect.height plot0)
            let arrangement = build xPlan yPlan
            let plotPx = Layout.findPx "plot" arrangement
            let plotNd = Layout.ndRect extent plotPx

            let plotSize: Extent<px> =
                { Width = Rect.width plotPx
                  Height = Rect.height plotPx }

            // ---- gridlines ---------------------------------------------------------------
            let gridX =
                if (defaultArg p.XAxis.Grid theme.ShowGridX) && p.XAxis.Visible then
                    Axes.keptTicks xSpec xPlan |> Array.map (fun t -> t.Position)
                else
                    [||]

            let gridY =
                if (defaultArg p.YAxis.Grid theme.ShowGridY) && p.YAxis.Visible then
                    Axes.keptTicks ySpec yPlan |> Array.map (fun t -> t.Position)
                else
                    [||]

            // ---- the data ------------------------------------------------------------------
            //
            // One Transform for the panel, one Clip inside it. D9: `Scale.Forward` does not
            // clamp, so out-of-domain data projects outside [0,1] and this Clip is what removes
            // it. That is also why a backend without clipping still gets the common case right.
            let data =
                let layers =
                    p.Layers
                    |> Array.map (fun l ->
                        let pts = Erase.projectXY l.Data

                        Annotated(
                            { Role = RoleSeries
                              Label = (if isEmpty l.Name then "series" else l.Name)
                              Data = [| "series", l.Name; "points", string pts.Length |] },
                            Geom.compile p.Coord plotSize l.Style l.Geom pts
                        ))

                Transform(Axes.panelTransform plotNd, Clip(Axes.panelShape p.Coord, Group layers))

            // ---- axes -----------------------------------------------------------------------
            let axisNode (side: AxisSide) (cfg: AxisConfig) (spec: AxisSpec) (tickPlan: TickPlan) =
                if not cfg.Visible then
                    Scene.empty
                else
                    Axes.compile
                        { Spec = spec
                          Side = side
                          Plan = tickPlan
                          Panel = plotNd
                          Extent = extent
                          Theme = theme
                          ShowSpine = not theme.PanelFrame
                          ShowTicks = true
                          ShowLabels = true
                          ShowTitle = cfg.ShowTitle
                          TitleOffset = Axes.tickExtent m theme side spec tickPlan + theme.TitleGap }

            let panelTitle =
                if isEmpty p.Title then
                    Scene.empty
                else
                    let box = Layout.findPx "ptitle" arrangement

                    Text
                        { Text = p.Title
                          // Titles, subtitles and legend names are caller-supplied prose, not
                          // unit markup, so they have no TeX form of their own — a TeX backend
                          // escapes the plain text instead. Only the axis compiler produces a
                          // `Tex` form, because only it has a unit rendering to produce one from.
                          Tex = None
                          At = Layout.ndPoint extent { X = (plotPx.X0 + plotPx.X1) / 2.0; Y = box.Y0 }
                          Font = theme.PanelTitleFont
                          Paint = Solid theme.Foreground
                          Anchor = AnchorMiddle
                          Baseline = BaselineHanging
                          Rotation = 0.0<rad> }

            let legend =
                Legend.compile m theme extent plotNd p.Legend (legendEntries p)

            Annotated(
                { Role = RoleOther "panel"
                  Label = (if isEmpty p.Title then "panel" else p.Title)
                  Data =
                    [| "row", string p.Row
                       "col", string p.Col
                       "layers", string p.Layers.Length
                       "x-tick-rule", xPlan.Rule
                       "y-tick-rule", yPlan.Rule |] },
                Scene.prune (
                    Group
                        [| Axes.panelBackground theme p.Coord plotNd
                           Axes.gridlines theme p.Coord plotNd gridX gridY
                           data
                           axisNode AxisBottom p.XAxis xSpec xPlan
                           axisNode AxisLeft p.YAxis ySpec yPlan
                           Axes.panelFrame theme p.Coord plotNd
                           panelTitle
                           legend |]
                )
            )

    /// **`Figure -> Node`.** The one entry point a backend's caller needs.
    let toScene (m: ITextMeasurer) (extent: Extent<px>) (fig: Figure) : Node =
        let theme = fig.Theme
        let rows = max 1 fig.Rows
        let cols = max 1 fig.Cols

        let headHeight (measurer: ITextMeasurer) =
            let a =
                if isEmpty fig.Title then
                    0.0<px>
                else
                    Rect.height (textBox measurer theme.TitleFont fig.Title) + theme.TitleGap

            let b =
                if isEmpty fig.Subtitle then
                    0.0<px>
                else
                    Rect.height (textBox measurer theme.SubtitleFont fig.Subtitle) + theme.TitleGap

            a + b

        let outer =
            let trackRows =
                Array.append [| Track.Content 0.0<px> |] (Array.create rows (Track.Frac 1.0))

            let trackCols = Array.create cols (Track.Frac 1.0)

            let cells =
                [| yield Layout.cell "head" 0 0 (fun mm -> { Width = 0.0<px>; Height = headHeight mm })
                       |> Layout.spanning 1 cols

                   for p in fig.Panels do
                       yield Layout.fill (sprintf "panel-%d-%d" p.Row p.Col) (1 + max 0 p.Row) (max 0 p.Col) |]

            Layout.grid trackRows trackCols cells
            |> Layout.withGaps theme.PanelGap theme.PanelGap
            |> Layout.withPadding theme.FigurePadding

        let arrangement = Layout.arrange m extent outer

        let background =
            Scene.rect None (Some(Solid theme.Background)) Rect.unit

        let head =
            let box = Layout.findPx "head" arrangement

            if isEmpty fig.Title && isEmpty fig.Subtitle then
                Scene.empty
            else
                let cx = (box.X0 + box.X1) / 2.0
                let mutable y = box.Y0

                let nodes =
                    [| if not (isEmpty fig.Title) then
                           yield
                               Text
                                   { Text = fig.Title
                                     Tex = None
                                     At = Layout.ndPoint extent { X = cx; Y = y }
                                     Font = theme.TitleFont
                                     Paint = Solid theme.Foreground
                                     Anchor = AnchorMiddle
                                     Baseline = BaselineHanging
                                     Rotation = 0.0<rad> }

                           y <- y + Rect.height (textBox m theme.TitleFont fig.Title) + theme.TitleGap

                       if not (isEmpty fig.Subtitle) then
                           yield
                               Text
                                   { Text = fig.Subtitle
                                     Tex = None
                                     At = Layout.ndPoint extent { X = cx; Y = y }
                                     Font = theme.SubtitleFont
                                     Paint = Solid theme.MutedForeground
                                     Anchor = AnchorMiddle
                                     Baseline = BaselineHanging
                                     Rotation = 0.0<rad> } |]

                Annotated(
                    { Role = RoleAnnotation
                      Label = fig.Title
                      Data = [||] },
                    Group nodes
                )

        let panels =
            fig.Panels
            |> Array.map (fun p ->
                let key = sprintf "panel-%d-%d" p.Row p.Col

                match Layout.tryFindPx key arrangement with
                | None -> Scene.empty
                | Some region -> compilePanel m theme extent region (unify p))

        Scene.prune (
            Keyed(
                Key "figure",
                Group(Array.append [| background; head |] panels)
            )
        )

    /// The same compilation, but reporting the layout it chose. For tests and for anyone
    /// debugging why a label landed where it did.
    let arrangement (m: ITextMeasurer) (extent: Extent<px>) (fig: Figure) : Arrangement =
        let theme = fig.Theme
        let rows = max 1 fig.Rows
        let cols = max 1 fig.Cols

        let trackRows =
            Array.append [| Track.Content 0.0<px> |] (Array.create rows (Track.Frac 1.0))

        let cells =
            [| for p in fig.Panels do
                   yield Layout.fill (sprintf "panel-%d-%d" p.Row p.Col) (1 + max 0 p.Row) (max 0 p.Col) |]

        Layout.grid trackRows (Array.create cols (Track.Frac 1.0)) cells
        |> Layout.withGaps theme.PanelGap theme.PanelGap
        |> Layout.withPadding theme.FigurePadding
        |> Layout.arrange m extent
