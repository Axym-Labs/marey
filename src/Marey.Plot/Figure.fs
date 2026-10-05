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
      /// §9 / D46 — the non-positional channels driving this layer: colour, size, shape.
      ///
      /// They live on the layer rather than being resolved into styles at construction time
      /// because two things downstream need them *unresolved*. The legend needs the level
      /// names, and the colorbar needs the scale with its unit still attached — and both are
      /// compiled at figure-compile time, not when the caller says `colorByKey`.
      ///
      /// Empty is the overwhelmingly common case and costs one array comparison.
      Channels: Channel[]
      ShowInLegend: bool

      /// D69 — the rows that get a **node of their own**, by original row index.
      ///
      /// §5.1 batches N marks into one `Markers` node, which is "the single biggest performance
      /// decision in the IR" and the reason mark 42 has nothing to address. A row named here
      /// splits that batch three ways — before it, itself under `Keys.mark`, after it — so a
      /// storyboard's `highlight curve.[42]` has a subtree to rewrite. Two extra nodes per
      /// addressed mark, not N.
      ///
      /// Empty is the overwhelmingly common case, takes the unsplit path exactly, and is what
      /// every figure in the library that does not name a mark carries.
      Addressed: int[] }

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

/// §9 / D48 — **which axes the panels of a composed figure share.**
///
/// Three cases, and the two that are missing are the decision.
///
/// **Fully free is not here**, because a grid of panels that shares nothing is not a facet — it
/// is a page of charts, and `Chart.beside` / `Chart.above` already spell that. A fourth case
/// would give two ways to say one thing, and the one named `Facet…` would be the misleading one.
///
/// **Free per row or per column is not here either**, and that is stronger than "not yet": under
/// `facetWrap` it is incoherent. A wrapped facet's rows come from the *wrap width*, not from the
/// data, so an axis shared along a row would change when the grid was reshaped — a scale that
/// depends on the layout rather than on what is being plotted. It becomes meaningful only under a
/// `facetGrid` whose rows and columns are each keyed by a variable, which is not built.
type FacetScales =
    /// Every panel gets the same x and the same y. What "facet" means, and the default.
    | ShareBoth
    /// One x across the panels; each panel keeps its own y.
    | ShareX
    /// One y across the panels; each panel keeps its own x.
    | ShareY

/// D72 — one panel's inner grid, frozen.
///
/// The nine sizes are the `Track.Content` extents of the panel's inner grid: its *gutters*. They
/// are what an axis title, a column of tick labels, a panel title or a colorbar takes out of the
/// region the figure gave the panel, and the plot rectangle is what is left over. That order is
/// the reason a panel breathes when it is re-arranged per frame — since D61 the rectangle is a
/// function of shaped text, so a domain crossing `9.5 → 10.5` gains a digit on every label at
/// once, every gutter widens together, and the axis crawls while the data is the thing that was
/// supposed to be moving.
///
/// `Probe` is the rectangle the *tick plan* is measured against — how many labels survive and
/// whether they tilt. It is one of the two passes' inputs rather than one of its outputs, and it
/// is frozen for the same reason and stated separately for that one.
///
/// **What is deliberately not here: the tick plan, and the labels.** D72 freezes the rectangle,
/// not its contents. An animation whose numbers change and whose labels do not is not showing the
/// numbers changing.
type PanelLayout =
    { TitleH: float<px>
      TopPad: float<px>
      XTicksH: float<px>
      XTitleH: float<px>
      YTitleW: float<px>
      YTicksW: float<px>
      RightPad: float<px>
      BarGap: float<px>
      BarW: float<px>
      /// The plot rectangle the tick plan is measured against.
      Probe: Extent<px>
      /// D73 — the prefix the x axis is pinned to. `None` chooses one from this figure's own
      /// domain, which is what a still figure does and has always done.
      XPrefix: Prefix option
      YPrefix: Prefix option }

[<RequireQualifiedAccess>]
module PanelLayout =

    let empty: PanelLayout =
        { TitleH = 0.0<px>
          TopPad = 0.0<px>
          XTicksH = 0.0<px>
          XTitleH = 0.0<px>
          YTitleW = 0.0<px>
          YTicksW = 0.0<px>
          RightPad = 0.0<px>
          BarGap = 0.0<px>
          BarW = 0.0<px>
          Probe = { Width = 0.0<px>; Height = 0.0<px> }
          XPrefix = None
          YPrefix = None }

    /// D72's union, and the whole rule is one sentence: **every size is the largest any sampled
    /// frame needed.**
    ///
    /// Componentwise, not "the sample with the biggest gutters". There is no such sample — one
    /// frame can want a wider y gutter and a shorter x one than another, and picking either
    /// whole arrangement then overflows in the direction it did not win. The componentwise
    /// maximum is the arrangement no frame can overflow, which is the property D72 is actually
    /// asking for, and it is the same rule for `Probe`: a larger probe keeps *more* tick labels,
    /// and the gutters that hold them have already been maximised.
    ///
    /// The prefixes are not unioned — a prefix has no maximum, and D73 decides it from the union
    /// *domain* rather than from the sampled choices. `union` carries the left operand's through
    /// and `arrangeUnion` writes the pinned pair afterwards.
    let union (a: PanelLayout) (b: PanelLayout) : PanelLayout =
        { TitleH = max a.TitleH b.TitleH
          TopPad = max a.TopPad b.TopPad
          XTicksH = max a.XTicksH b.XTicksH
          XTitleH = max a.XTitleH b.XTitleH
          YTitleW = max a.YTitleW b.YTitleW
          YTicksW = max a.YTicksW b.YTicksW
          RightPad = max a.RightPad b.RightPad
          BarGap = max a.BarGap b.BarGap
          BarW = max a.BarW b.BarW
          Probe =
            { Width = max a.Probe.Width b.Probe.Width
              Height = max a.Probe.Height b.Probe.Height }
          XPrefix = a.XPrefix
          YPrefix = a.YPrefix }

/// D72 — **the layout of one shot**: everything every frame of an animation shares, and nothing
/// a frame is allowed to recompute.
///
/// A still figure has one of these too, over a single sample. That is not a courtesy: it is what
/// makes `toScene` and this type the same code path, and the reason `Figure.arrangement` — the
/// hand-written second copy that this replaces — could disagree with the renderer for two
/// milestones without a test noticing.
[<NoComparison>]
type Shot =
    { /// The figure's outer grid: the head row, and one region per panel.
      Outer: Arrangement
      /// Per panel key — `panel-0-1`, `Keys.panel`'s spelling — that panel's frozen inner grid.
      /// A panel whose key is absent is measured from its own data, so a shot taken over a
      /// figure that later grows a panel degrades to today's behaviour rather than failing.
      Panels: (string * PanelLayout)[]
      /// How many states the union was taken over. **Reported rather than assumed, per D72:**
      /// the union is only as good as its samples, and an animation with a spike between two of
      /// them can still overflow. A single figure's own shot reports 1.
      Samples: int }

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

                            Scale.ofKind k s.XScale.Unit d

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

                            Scale.ofKind k s.YScale.Unit d

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

    // ---- Sharing an axis: the one runtime dimensional check --------------------------------

    /// **The check, once.** Whether a set of things can share one axis, and what domain they
    /// share if they can.
    ///
    /// Inside a single `Chart.line (xs, ys)` call the compiler already rejects metres against
    /// seconds, because the two arrays have different measures and `Series<'ux,'uy>` names both.
    /// Across *two* of anything — two layers on a panel, two panels of a facet — the measures
    /// have been erased into the existential and F# cannot compare them, so the `Dim` values are
    /// compared at runtime instead and a mismatch raises rather than silently plotting joules on
    /// a metre axis.
    ///
    /// `describe` names the thing at index `i` in the message, because "layer 3" and "panel 3"
    /// are different mistakes and this is the one dimensional error the compiler cannot catch —
    /// so it is the one whose message has to work hardest.
    let private agreeOn (describe: int -> string) (axis: string) (scales: IScaleErased[]) : float * float =
        let s0 = scales.[0]

        // The categorical counterpart of the dimensional check, and it is needed for exactly the
        // reason that check is: an ordinal scale is dimensionless (D45), so two scales with
        // *different* level sets have identical `Dim` and the check cannot see the difference.
        // Verified before it was written: two four-level layers over unrelated levels unified
        // silently, and the second layer's points were drawn against the first layer's labels —
        // the same class of lie as joules on a metre axis, arrived at by a different route.
        let levelsOf (s: IScaleErased) =
            match s.Kind with
            | ScaleKind.Ordinal levels -> Some levels
            | _ -> None

        for i in 1 .. scales.Length - 1 do
            let si = scales.[i]

            match levelsOf s0, levelsOf si with
            | Some la, Some lb when la <> lb ->
                failwithf
                    "Marey: %s and %s have %s axes over different sets of levels (%s against %s). Two categories cannot share an axis."
                    (describe 0)
                    (describe i)
                    axis
                    (String.concat ", " la)
                    (String.concat ", " lb)
            | Some _, None
            | None, Some _ ->
                failwithf
                    "Marey: %s has a categorical %s axis and %s has a continuous one. They cannot share an axis."
                    (describe 0)
                    axis
                    (describe i)
            | _ -> ()

            if si.Dim <> s0.Dim then
                failwithf
                    "Marey: %s has %s %s axis in %s but %s has one in %s. Two physical dimensions cannot share an axis."
                    (describe 0)
                    (if axis = "x" then "an" else "a")
                    axis
                    (Dim.show s0.Dim)
                    (describe i)
                    (Dim.show si.Dim)

        let ds = scales |> Array.map (fun s -> s.Domain)
        (ds |> Array.map fst |> Array.min), (ds |> Array.map snd |> Array.max)

    /// Give every layer on a panel the same x and y domains.
    ///
    /// Applied at **compile** time rather than at construction, and that is deliberate: a panel
    /// accumulates layers after it is built — `Chart.add` is one call — so there is no moment
    /// before rendering at which its layer set is known to be complete. `shareScales` below makes
    /// the opposite choice, for the opposite reason.
    let unify (p: Panel) : Panel =
        if p.Layers.Length < 2 then
            p
        else
            let scales = p.Layers |> Array.map (fun l -> Erase.seriesScales l.Data)

            // Layers are usually unnamed until `Chart.withName` is applied, and "layer ''"
            // identifies nothing.
            let describe i =
                let name = p.Layers.[i].Name

                if System.String.IsNullOrWhiteSpace name then
                    sprintf "layer %d" i
                else
                    sprintf "layer %d ('%s')" i name

            let dx = agreeOn describe "x" (scales |> Array.map fst)
            let dy = agreeOn describe "y" (scales |> Array.map snd)

            { p with
                Layers =
                    p.Layers
                    |> Array.map (fun l ->
                        { l with
                            Data = withDomains (Some dx) (Some dy) l.Data }) }

    /// §9 / D48 — **give every panel of a figure the same axes.** What makes a grid of panels a
    /// facet rather than a page of unrelated charts.
    ///
    /// Applied **eagerly**, by whoever composes the panels, so that the shared domains are on the
    /// figure and can be read back off it without rendering — and so that a dimensional mismatch
    /// is reported by the call that asked for the sharing rather than by the render, a long way
    /// downstream. That is the opposite of `unify`'s choice above and for the opposite reason: a
    /// facet's panel set is complete at the moment it is made.
    ///
    /// **The check follows the sharing.** An axis that is shared is checked; an axis left free
    /// makes no cross-panel claim and is not checked. That is the whole answer to where the
    /// runtime `Dim` check lives once panels share scales: it is not a property of the figure, it
    /// is a property of the axis being shared.
    let shareScales (mode: FacetScales) (fig: Figure) : Figure =
        let drawn =
            fig.Panels
            |> Array.mapi (fun i p -> i, p)
            |> Array.filter (fun (_, p) -> p.Layers.Length > 0)

        if drawn.Length < 2 then
            fig
        else
            // One entry per layer, tagged with the panel it came from: a panel with two layers
            // agrees with the other panels layer by layer, exactly as it agrees with itself.
            let entries =
                [| for (pi, p) in drawn do
                       for li in 0 .. p.Layers.Length - 1 do
                           yield pi, p, li, Erase.seriesScales p.Layers.[li].Data |]

            let describe i =
                let (pi, p: Panel, li, _) = entries.[i]

                let panel =
                    if System.String.IsNullOrWhiteSpace p.Title then
                        sprintf "panel %d" pi
                    else
                        sprintf "panel %d ('%s')" pi p.Title

                if p.Layers.Length > 1 then
                    sprintf "%s layer %d" panel li
                else
                    panel

            let scalesOf pick =
                entries |> Array.map (fun (_, _, _, pair) -> pick pair)

            let dx =
                match mode with
                | ShareBoth
                | ShareX -> Some(agreeOn describe "x" (scalesOf fst))
                | ShareY -> None

            let dy =
                match mode with
                | ShareBoth
                | ShareY -> Some(agreeOn describe "y" (scalesOf snd))
                | ShareX -> None

            { fig with
                Panels =
                    fig.Panels
                    |> Array.map (fun p ->
                        { p with
                            Layers =
                                p.Layers
                                |> Array.map (fun l ->
                                    { l with
                                        Data = withDomains dx dy l.Data }) }) }

    // ---- Compilation -----------------------------------------------------------------------

    let private textBox (m: ITextMeasurer) (f: FontSpec) (s: string) =
        TextMeasure.bounds m f AnchorMiddle BaselineMiddle 0.0<rad> s

    /// D49 — **the legend enumerates.** What one layer contributes to it.
    ///
    /// A layer with a **categorical** channel contributes one entry per level rather than one
    /// entry for itself: what the reader has to tell apart is the level, and the series name is
    /// already on the panel. Colour and shape keyed to the same levels merge into a single
    /// entry per level, because both read the same level number — which is why `Channel.split`
    /// mixes buckets into one key rather than taking a product.
    ///
    /// A **size** channel contributes a size key: entries at the ends and the middle of its
    /// domain, at the sizes the marks are actually drawn at, labelled through the axis compiler
    /// so they carry the prefix and the unit. `LegendSizedMarker` exists for exactly this.
    ///
    /// A continuous **colour** channel contributes nothing here. It gets a colorbar.
    ///
    /// `ShowInLegend` gates **all** of it, not only the layer's own entry. The field says
    /// whether this layer is in the legend, and a layer that is not cannot be in it by way of
    /// its channels either. The encoding block (§9) is what makes the distinction matter: its
    /// channels belong to the block rather than to one geom, so every layer carries them, and
    /// three levels drawn as points *and* as a line would enumerate themselves twice under two
    /// different swatches. `Chart.withChannel` sets the flag when it attaches a channel, so
    /// nothing built through the quick front door notices this.
    let private layerEntries (theme: Theme) (l: Layer) : LegendEntry[] =
        let categorical =
            if l.ShowInLegend then
                l.Channels |> Array.filter Channel.isCategorical
            else
                [||]

        let levelEntries =
            if categorical.Length = 0 then
                [||]
            else
                match Channel.levelsOf categorical.[0] with
                | None -> [||]
                | Some levels ->
                    levels
                    |> Array.mapi (fun i name ->
                        let st =
                            categorical
                            |> Array.fold (fun acc (c: Channel) -> Channel.restyle c.Map i acc) l.Style

                        { Name = name
                          Color = st.Color
                          Mark = Geom.legendMark l.Geom st })

        let sizeEntries =
            (if l.ShowInLegend then l.Channels else [||])
            |> Array.collect (fun c ->
                match c.Map with
                | SizeByValue(scale, smallest, largest) ->
                    let spec =
                        Axis.ofPackedWith theme.UnitStyle UnitRegistry.si 3 (Colorbar.labelOf c.Label scale) scale

                    // The ends and the middle of what the axis compiler chose, so the key's
                    // numbers are the round ones an axis would have printed.
                    let ticks = spec.Ticks

                    let picks =
                        if ticks.Length <= 3 then
                            ticks
                        else
                            [| ticks.[0]; ticks.[ticks.Length / 2]; ticks.[ticks.Length - 1] |]

                    picks
                    |> Array.map (fun t ->
                        let size =
                            Channel.quantiseSize (smallest + (largest - smallest) * float t.Position)

                        { Name =
                            if isEmpty spec.UnitText then
                                t.Label
                            else
                                t.Label + " " + spec.UnitText
                          Color = l.Style.Color
                          Mark = LegendSizedMarker(l.Style.Shape, size) })
                | ColorByLevel _
                | ShapeByLevel _
                | ColorByValue _ -> [||])

        let own =
            if l.ShowInLegend && not (isEmpty l.Name) && levelEntries.Length = 0 then
                [| { Name = l.Name
                     Color = l.Style.Color
                     Mark = Geom.legendMark l.Geom l.Style } |]
            else
                [||]

        Array.concat [ own; levelEntries; sizeEntries ]

    /// Every layer's contribution, with **identical entries collapsed**.
    ///
    /// A legend enumerates what the reader has to tell apart, so the same swatch under the same
    /// name twice is not a second thing — it is the same thing counted twice. The encoding block
    /// (§9) is what makes this reachable: its channels belong to the block rather than to one
    /// geom, so a two-layer block over three levels would otherwise print six entries naming
    /// three levels. `Array.distinct` keeps first-appearance order, which is level order.
    ///
    /// Entries that differ in *anything* the reader can see — name, colour, or mark — survive,
    /// which is why this cannot quietly swallow a real second series.
    let private legendEntries (theme: Theme) (p: Panel) =
        p.Layers |> Array.collect (layerEntries theme) |> Array.distinct

    /// Compile one panel into a device-space region.
    /// D72 — one function, two answers: the panel's frozen inner grid, and a thunk that draws
    /// the panel inside it.
    ///
    /// **Why one function and not two.** Measuring and drawing share every intermediate here —
    /// the two axis specs, the colorbar, the inner grid, the tick planner — and this file already
    /// carries the proof of what splitting them costs: `Figure.arrangement` was a second copy of
    /// `toScene`'s grid, and it had been silently wrong since M1, omitting the head row so that
    /// every titled figure reported a layout the renderer did not use. So the two answers come
    /// out of one body, and a caller that only wants the layout simply never calls the thunk —
    /// which matters, because sampling an animation 32 times must not compile 32 scenes' worth
    /// of geometry to find out how wide a gutter is.
    ///
    /// `frozen = None` measures the panel from its own data, which is every still figure.
    /// `Some layout` skips the measuring pass and uses the layout given — the animation path,
    /// where the layout is a union over the whole shot and no single frame may move it.
    let private compilePanel
        (m: ITextMeasurer)
        (theme: Theme)
        (extent: Extent<px>)
        (region: Rect<px>)
        (frozen: PanelLayout option)
        (p: Panel)
        : PanelLayout * (unit -> Node) =
        if p.Layers.Length = 0 then
            PanelLayout.empty, (fun () -> Scene.empty)
        else
            // D68 — this panel's name, from the grid position it already writes into the
            // `Annotated` node at the end of this function. `Keyed` says *which* panel; the
            // `Annotated` beside it says that it is a panel. Both, because §5.2 and §5.3 are
            // different questions.
            let panelKey = Keys.panel p.Row p.Col

            let (xPacked, yPacked) = packedScales p.Layers.[0].Data

            // The one place the theme's unit spelling and a per-axis override meet. Resolved here
            // rather than at construction time so that changing the theme on a finished figure
            // changes its axis titles — which is what `Chart.withTheme Theme.tikz` has to do.
            let unitStyle (cfg: AxisConfig) = defaultArg cfg.Style theme.UnitStyle

            // D73 — the prefix comes from the frozen layout when there is one. A still figure
            // pins nothing and chooses per figure, which is what it has always done; an
            // animation pins both axes against the union of every frame's domain, so the axis
            // *title* cannot step from `mm` to `µm` halfway through a zoom.
            let pinX = frozen |> Option.bind (fun f -> f.XPrefix)
            let pinY = frozen |> Option.bind (fun f -> f.YPrefix)

            let xSpec =
                Axes.specOfPinned pinX (unitStyle p.XAxis) p.XAxis.TickCount p.XAxis.Label AxisBottom xPacked

            let ySpec =
                Axes.specOfPinned pinY (unitStyle p.YAxis) p.YAxis.TickCount p.YAxis.Label AxisLeft yPacked

            let titleH =
                if isEmpty p.Title then
                    0.0<px>
                else
                    Rect.height (textBox m theme.PanelTitleFont p.Title) + theme.TitleGap

            // D49 — a continuous colour channel gets a colorbar, and the bar is a column of the
            // panel's own grid. The first one on the panel wins: two different colour scales on
            // one panel are two keys the reader has to arbitrate between, and that is a different
            // question from D48's, which is about one scale across several panels.
            //
            // Gated on `ShowInLegend`, which D53 defines as a layer's whole legend entry and D48
            // widens to a layer's whole *key* contribution — the bar and the entry are the same
            // claim about the same channel, differing only in whether the values are levels or a
            // range. A facet builds its channels over the whole block before it splits, so every
            // panel's bar would be identical; drawing it n times says nothing the first one did
            // not, and costs a column of every panel.
            let colorbar =
                p.Layers
                |> Array.filter (fun l -> l.ShowInLegend)
                |> Array.tryPick (fun l ->
                    l.Channels
                    |> Array.tryPick (fun c -> Colorbar.specOf theme.UnitStyle theme.TickCount c))

            // The gap between the plot's right edge and the bar. A `PanelGap` rather than a
            // `LabelGap`, because what it separates is two things the eye reads separately.
            let barGap =
                match colorbar with
                | Some _ -> theme.PanelGap
                | None -> 0.0<px>

            // D72 — the gutters, measured. This is the half of the panel that depends on the
            // data: how tall the surviving x labels are, how wide the widest y label is, how
            // much the outermost label hangs past the end of its axis, how wide the colorbar
            // needs to be. Every one of them is a `Track.Content` size below, and every one of
            // them moves when the numbers move.
            let gutters (xPlan: TickPlan) (yPlan: TickPlan) (barPlan: TickPlan option) (probe: Extent<px>) : PanelLayout =
                { TitleH = titleH
                  BarGap = barGap

                  BarW =
                    match colorbar, barPlan with
                    | Some(spec, _), Some plan -> Colorbar.measure m theme spec plan
                    | _ -> 0.0<px>

                  XTicksH =
                    if p.XAxis.Visible then
                        Axes.tickExtent m theme AxisBottom xSpec xPlan
                    else
                        0.0<px>

                  YTicksW =
                    if p.YAxis.Visible then
                        Axes.tickExtent m theme AxisLeft ySpec yPlan
                    else
                        0.0<px>

                  XTitleH =
                    if p.XAxis.Visible && p.XAxis.ShowTitle then
                        Axes.titleExtent m theme xSpec.Title
                    else
                        0.0<px>

                  YTitleW =
                    if p.YAxis.Visible && p.YAxis.ShowTitle then
                        Axes.titleExtent m theme ySpec.Title
                    else
                        0.0<px>

                  // Half of the outermost tick label hangs past the end of the axis. Reserving
                  // it is half of §13's "labels inside bounds"; without it the right-hand x
                  // label and the top y label are clipped by the figure edge.
                  RightPad =
                    if p.XAxis.Visible then
                        Axes.labelOverhang m theme AxisBottom xSpec xPlan
                    else
                        0.0<px>

                  TopPad =
                    if p.YAxis.Visible then
                        Axes.labelOverhang m theme AxisLeft ySpec yPlan
                    else
                        0.0<px>

                  Probe = probe
                  XPrefix = pinX
                  YPrefix = pinY }

            // The inner grid, from gutters that are already decided. Pure arithmetic on the
            // record above plus the solver — it measures nothing, which is exactly what lets a
            // frozen layout be handed in from outside and produce the same rectangle every time.
            let build (g: PanelLayout) =
                let cols =
                    [| Track.Content g.YTitleW
                       Track.Content g.YTicksW
                       Track.Frac 1.0
                       Track.Content g.RightPad
                       Track.Content g.BarGap
                       Track.Content g.BarW |]

                let rows =
                    [| Track.Content g.TitleH
                       Track.Content g.TopPad
                       Track.Frac 1.0
                       Track.Content g.XTicksH
                       Track.Content g.XTitleH |]

                let fixedCell key row col (w: float<px>) (h: float<px>) =
                    Layout.cell key row col (fun _ -> { Width = w; Height = h })

                let cells =
                    [| fixedCell "ytitle" 2 0 g.YTitleW 0.0<px>
                       fixedCell "yticks" 2 1 g.YTicksW 0.0<px>
                       Layout.fill "plot" 2 2
                       fixedCell "rightpad" 2 3 g.RightPad 0.0<px>
                       fixedCell "bargap" 2 4 g.BarGap 0.0<px>
                       fixedCell "colorbar" 2 5 g.BarW 0.0<px>
                       fixedCell "ptitle" 0 0 0.0<px> g.TitleH |> Layout.spanning 1 6
                       fixedCell "toppad" 1 0 0.0<px> g.TopPad |> Layout.spanning 1 6
                       fixedCell "xticks" 3 2 0.0<px> g.XTicksH
                       fixedCell "xtitle" 4 2 0.0<px> g.XTitleH |]

                Layout.arrangeIn m extent region (Layout.grid rows cols cells)

            // The bar's plan is made against its own height, which is the plot's — the bar
            // sits in the plot's row. That is why `Legend.fs`'s objection to outside placement
            // does not transfer: the width this adds is settled by a vertical measurement, so
            // nothing here is circular and the loop is still the same two passes.
            let plan (w: float<px>) (h: float<px>) =
                Axes.planTicks m theme.TickFont theme.LabelGap w AxisBottom xSpec.Ticks,
                Axes.planTicks m theme.TickFont theme.LabelGap h AxisLeft ySpec.Ticks,
                colorbar |> Option.map (fun (spec, _) -> Colorbar.planTicks m theme h spec)

            let layout =
                match frozen with
                | Some f -> f
                | None ->
                    // Iteration 1: plan against an estimate of the plot area. 78% x 72% of the
                    // region is what a labelled panel typically ends up with; the estimate only
                    // has to be close enough that the second pass converges.
                    let estW = Rect.width region * 0.78
                    let estH = Rect.height region * 0.72
                    let (xPlan0, yPlan0, barPlan0) = plan estW estH
                    let a0 = build (gutters xPlan0 yPlan0 barPlan0 { Width = estW; Height = estH })
                    let plot0 = Layout.findPx "plot" a0

                    let probe: Extent<px> =
                        { Width = Rect.width plot0
                          Height = Rect.height plot0 }

                    let (xPlan1, yPlan1, barPlan1) = plan probe.Width probe.Height
                    gutters xPlan1 yPlan1 barPlan1 probe

            let draw () =
                // D72 — the plan is *not* frozen, only the rectangle it is measured against. A
                // frame's own tick values decide its own labels, inside a panel that does not
                // move. Freezing the plan too would freeze the labels, and an animation whose
                // numbers change and whose labels do not is not showing the numbers changing.
                let (xPlan, yPlan, barPlan) = plan layout.Probe.Width layout.Probe.Height
                let arrangement = build layout
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
                        |> Array.mapi (fun ordinal l ->
                            let pts = Erase.projectXY l.Data

                            // D97 — the geom's own arrays through the same scales as `pts`, at
                            // the same moment. Projected at construction they went stale as soon
                            // as `unify` or a stated domain moved the panel out from under them.
                            let geom =
                                let sx, sy = Erase.seriesScales l.Data
                                Geom.project sx.Project sy.Project l.Geom

                            let layerKey = Keys.layer panelKey ordinal

                            // D69 — the rows this layer was asked to name. `None` is the
                            // unsplit path and is what every figure that highlights nothing
                            // takes, which is why the golden baselines did not move.
                            let address =
                                if l.Addressed.Length = 0 then
                                    None
                                else
                                    Some(layerKey, l.Addressed)

                            // D46 — **one batch per bucket, not one per point.** With no channels
                            // this is `Geom.compile`'s own path; with a continuous colour channel
                            // it is at most 256 groups for any number of points, which is what
                            // keeps §5.1's batching intact while the colour varies.
                            let body, counts =
                                if l.Channels.Length = 0 then
                                    // No channels, no counts: an unchannelled layer's annotation is
                                    // exactly what it was before phase 2, so every baseline that
                                    // predates channels stays byte-identical.
                                    Geom.compileFor address None p.Coord plotSize l.Style geom pts, [||]
                                else
                                    let (batches, unmapped) = Channel.split l.Channels pts.Length

                                    let nodes =
                                        batches
                                        |> Array.map (fun b ->
                                            Geom.compileFor
                                                address
                                                (Some b.Indices)
                                                p.Coord
                                                plotSize
                                                (b.Restyle l.Style)
                                                geom
                                                pts)

                                    // `unmapped` is reported rather than swallowed: a point whose
                                    // channel value was missing is not drawn (see
                                    // `Channel.bucketer`), and a figure that contains fewer marks
                                    // than it has rows should be able to say so.
                                    Group nodes,
                                    [| "batches", string nodes.Length
                                       "unmapped", string unmapped |]

                            // D68 — the layer's **ordinal**, not its name. `l.Name` is written
                            // into the `Annotated` node just below because that is where a
                            // human-readable label belongs; it is kept out of the key because it
                            // is caller-supplied, is empty until `Chart.withName` is called, and
                            // may be the same on two layers of one panel. The ordinal is unique
                            // and survives a restyle, a rename and a change of data.
                            Keyed(
                                layerKey,
                                Annotated(
                                    { Role = RoleSeries
                                      Label = (if isEmpty l.Name then "series" else l.Name)
                                      Data = Array.append [| "series", l.Name; "points", string pts.Length |] counts },
                                    body
                                )
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
                    Legend.compile m theme extent plotNd p.Legend (legendEntries theme p)

                let colorbarNode =
                    match colorbar, barPlan with
                    | Some(spec, ramp), Some plan ->
                        Colorbar.compile m theme extent (Layout.findPx "colorbar" arrangement) spec plan ramp
                    | _ -> Scene.empty

                Keyed(
                    panelKey,
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
                                   legend
                                   colorbarNode |]
                        )
                    )
                )

            layout, draw

    /// The figure's own outer grid: one content row for the head, then one row of tracks per
    /// panel row. Split out of `toScene` by D72 so that `arrangeFor` and the renderer cannot be
    /// looking at two different grids — which they were, for two milestones. See `Shot`.
    ///
    /// `head` overrides the measured head height when it is `Some`, which is how the union takes
    /// the tallest head of the shot rather than each frame's own.
    let private outerGrid (head: float<px> option) (fig: Figure) =
        let theme = fig.Theme
        let rows = max 1 fig.Rows
        let cols = max 1 fig.Cols

        let headHeight (measurer: ITextMeasurer) =
            match head with
            | Some h -> h
            | None ->
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

    /// **`Figure -> Shot`.** The layout this figure would be drawn in, measured and nothing more.
    ///
    /// This is D72's cheap half and it is public API on the type every example in the repository
    /// touches, because `Chart`'s surface had no way to say "lay out once and draw many times":
    /// `toScene` computed its arrangement internally and `arrangement` computed a *second* one
    /// for inspection. Both are now this function.
    let arrangeFor (m: ITextMeasurer) (extent: Extent<px>) (fig: Figure) : Shot =
        let outer = Layout.arrange m extent (outerGrid None fig)

        let panels =
            fig.Panels
            |> Array.choose (fun p ->
                let key = sprintf "panel-%d-%d" p.Row p.Col

                match Layout.tryFindPx key outer with
                | None -> None
                | Some region ->
                    let layout, _ = compilePanel m fig.Theme extent region None (unify p)
                    Some(key, layout))

        { Outer = outer
          Panels = panels
          Samples = 1 }

    /// **D72 — one arrangement for the whole shot.** Sample the animation, arrange each state,
    /// and take the union: the arrangement no frame can overflow.
    ///
    /// `sample` is `float -> Figure` over `[0, 1]`, which is deliberately not `Anim<Figure>`.
    /// `Marey.Anim` depends on `Marey.Core` and mentions no `Figure`, and `Marey.Plot` returns
    /// the compliment by depending on no animation package: an `Anim<'a>` is generic in what it
    /// animates, and a user tweening a `float<m>` should not be made to carry the grammar. The
    /// animation supplies the sampler at the call site — `fun u -> Anim.atFraction u anim` — and
    /// nothing in the dependency graph moves. It is also strictly more general, because a union
    /// over hand-listed keyframes needs no `Anim` at all.
    ///
    /// The two alternatives D72 rejected, since a reader will reach for both. **Re-arranging per
    /// frame makes the panel breathe** — see `PanelLayout`. **Freezing frame zero's arrangement
    /// overflows**, because a later frame's labels are wider than the gutters that were sized for
    /// frame zero. The union does neither, and tick labels still recompute per frame inside a
    /// rectangle that does not move.
    ///
    /// `samples` is stated rather than hidden, and reported back on the `Shot`, because the
    /// union is only as good as it: an animation with a spike between two samples can still
    /// overflow. D72's default is 32 and this function does not supply it — a default belongs
    /// where a caller can see it.
    let arrangeUnion
        (samples: int)
        (m: ITextMeasurer)
        (extent: Extent<px>)
        (sample: float -> Figure)
        : Shot =
        if samples < 1 then
            invalidArg "samples" "Marey: a shot needs at least one sample."

        let times =
            if samples = 1 then
                [| 0.0 |]
            else
                Array.init samples (fun i -> float i / float (samples - 1))

        let states = times |> Array.map sample

        // The outer grid first, because a panel cannot be measured until it knows its region and
        // the region depends on how tall the head row is.
        let headOf (fig: Figure) =
            let a =
                if isEmpty fig.Title then
                    0.0<px>
                else
                    Rect.height (textBox m fig.Theme.TitleFont fig.Title) + fig.Theme.TitleGap

            let b =
                if isEmpty fig.Subtitle then
                    0.0<px>
                else
                    Rect.height (textBox m fig.Theme.SubtitleFont fig.Subtitle) + fig.Theme.TitleGap

            a + b

        let head = states |> Array.map headOf |> Array.max

        // The frame the grid is taken from is the one with the most panels, so an animation that
        // adds a panel mid-shot still has a region for it. Everything else about the grid — rows,
        // columns, gaps, padding — is the theme's and does not vary with the data.
        let widest =
            states |> Array.maxBy (fun f -> (f.Panels.Length, max 1 f.Rows, max 1 f.Cols))

        let outer = Layout.arrange m extent (outerGrid (Some head) widest)

        // Then every panel of every state, unioned by key. `Array.choose` rather than an
        // exception when a key is missing: a state that lacks a panel another state has
        // contributes nothing to that panel's union, which is the right answer — an absent panel
        // needs no gutters.
        let measured =
            states
            |> Array.collect (fun fig ->
                fig.Panels
                |> Array.choose (fun p ->
                    let key = sprintf "panel-%d-%d" p.Row p.Col

                    match Layout.tryFindPx key outer with
                    | None -> None
                    | Some region ->
                        let unified = unify p
                        let layout, _ = compilePanel m fig.Theme extent region None unified
                        Some(key, (layout, unified))))

        let panels =
            measured
            |> Array.groupBy fst
            |> Array.map (fun (key, entries) ->
                let layouts = entries |> Array.map (fun (_, (l, _)) -> l)
                let unified = entries |> Array.map (fun (_, (_, u)) -> u)
                let merged = layouts |> Array.reduce PanelLayout.union

                // D73 — the prefix, chosen once from the union of every frame's domain rather
                // than per frame from its own. This is the same mechanism as the gutters and not
                // a second one: which decade a prefix comes from is a property of "the numbers"
                // (D19), and across a shot the numbers are all of them.
                //
                // A layerless panel has no numbers and therefore no prefix to pin. `toScene`
                // draws one as `Scene.empty`, so it is a case that exists and has to be answered
                // rather than indexed into.
                let drawn = unified |> Array.filter (fun p -> p.Layers.Length > 0)

                let pinFor (packedOf: Panel -> PackedScale) =
                    if drawn.Length = 0 then
                        None
                    else
                        let domains = drawn |> Array.map (packedOf >> Axis.domainOfPacked)
                        let lo = domains |> Array.map fst |> Array.min
                        let hi = domains |> Array.map snd |> Array.max
                        Some(Axis.prefixForPacked (lo, hi) (packedOf drawn.[0]))

                let layout =
                    { merged with
                        XPrefix = pinFor (fun p -> fst (packedScales p.Layers.[0].Data))
                        YPrefix = pinFor (fun p -> snd (packedScales p.Layers.[0].Data)) }

                key, layout)

        { Outer = outer
          Panels = panels
          Samples = samples }

    /// **`Shot -> Figure -> Node`.** Draw this figure inside a layout that has already been
    /// decided — the frame renderer's entry point.
    ///
    /// There is no `Extent<px>` parameter, and the plan that asked for one was wrong: the shot
    /// already carries the surface it was arranged against, and a second extent beside it is a
    /// second chance to be inconsistent with no way to detect it. An arrangement is in device
    /// pixels for one surface; it does not travel to another.
    let toSceneIn (m: ITextMeasurer) (shot: Shot) (fig: Figure) : Node =
        let theme = fig.Theme
        let extent = shot.Outer.Extent

        let background =
            Scene.rect None (Some(Solid theme.Background)) Rect.unit

        let head =
            let box = Layout.findPx "head" shot.Outer

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

                match Layout.tryFindPx key shot.Outer with
                | None -> Scene.empty
                | Some region ->
                    let frozen = shot.Panels |> Array.tryPick (fun (k, l) -> if k = key then Some l else None)
                    let _, draw = compilePanel m theme extent region frozen (unify p)
                    draw ())

        Scene.prune (
            Keyed(
                Keys.figure,
                Group(Array.append [| background; head |] panels)
            )
        )

    /// **`Figure -> Node`.** The one entry point a backend's caller needs.
    ///
    /// D72 split this into `arrangeFor` and `toSceneIn`; this is their composition and it is what
    /// every still figure in the library goes through. It is byte-for-byte what it was before the
    /// split — the two-pass tick solve, the estimate it plans against, and the grid it settles on
    /// are all unchanged, which is what the 46 golden baselines are for.
    let toScene (m: ITextMeasurer) (extent: Extent<px>) (fig: Figure) : Node =
        toSceneIn m (arrangeFor m extent fig) fig

    /// The layout `toScene` chose. For tests, and for anyone debugging why a label landed where
    /// it did.
    ///
    /// **This used to be a second, hand-written copy of `toScene`'s grid, and it had been wrong
    /// since M1.** It omitted the head row entirely, so every figure with a title or a subtitle
    /// reported panel regions taller than the ones it was actually drawn in — by exactly the
    /// height of the title. §1 of the M3 plan predicted the class of the bug from the shape of
    /// the code ("two functions that must agree and have no test saying they do") without knowing
    /// it had already happened. There is now one grid and a test that says so.
    let arrangement (m: ITextMeasurer) (extent: Extent<px>) (fig: Figure) : Arrangement =
        (arrangeFor m extent fig).Outer
