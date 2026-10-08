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
      Addressed: int[]

      /// D104 — **the data's own names for its rows**, one per row and unique, or empty for none.
      ///
      /// A mark is then keyed `Keys.datum` rather than `Keys.mark`, so it keeps its name when an
      /// earlier row is removed, and a morph pairs it with the datum of the same name rather than
      /// with whatever sits at the same row. Empty is the common case and keeps the ordinals.
      RowKeys: string[] }

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
      Samples: int
      /// D94 — every continuous colour channel's domain, unioned over the sampled states and
      /// keyed `panel-0-0/layer/channel`. A frame draws its colours and its colorbar against
      /// this rather than against its own range, so a growing field reads as growing rather than
      /// being renormalised every frame. Empty for a still. A value outside it, between two
      /// samples, takes the ramp's end colour.
      ColourDomains: (string * (float * float))[]
      /// D94 — every quiver layer's key, the one with the largest magnitude among the sampled
      /// states, keyed `panel-0-0/layer`. The arrows are scaled against it in every frame, for
      /// the colour domain's reason. Empty for a still.
      QuiverKeys: (string * QuiverKey)[] }

/// D105 — **one panel as a pointer meets it**: where its plot area is on the surface, and the
/// coord and the two scales that turn a point in it back into data.
///
/// Read off a `Shot` rather than off a scene, because the scene has spent its scales: a `Node`
/// knows where a mark is and not what it measures. The scales are the panel's after `unify`, which
/// are the ones its marks and axes were drawn through.
[<NoEquality; NoComparison>]
type PanelView =
    { Row: int
      Col: int
      /// The plot rectangle, in the surface's px, top-left origin.
      Plot: Rect<px>
      Coord: Coord
      X: IScaleErased
      Y: IScaleErased
      /// Each axis as it is drawn: its name, and the prefix and unit text its ticks are written
      /// in. A reading of the pointer is written in them too (D105), so it can be checked
      /// against the ticks without converting anything.
      XAxis: AxisSpec
      YAxis: AxisSpec }

[<RequireQualifiedAccess>]
module PanelView =

    /// Is the point inside the plot rectangle, edges included?
    let contains (p: Vec2<px>) (v: PanelView) =
        p.X >= v.Plot.X0 && p.X <= v.Plot.X1 && p.Y >= v.Plot.Y0 && p.Y <= v.Plot.Y1

    /// A surface point in the panel's own unit square, through the coord: where the scales would
    /// have put it. `None` where the coord cannot invert (the pole of a polar plot), or for a
    /// plot rectangle with no area.
    let toUnit (p: Vec2<px>) (v: PanelView) : Vec2<nd> option =
        let w = v.Plot.X1 - v.Plot.X0
        let h = v.Plot.Y1 - v.Plot.Y0

        if not (w > 0.0<px> && h > 0.0<px>) then
            None
        else
            // The one flip: px grows downward, the unit square upward (`Layout.ndRect`'s).
            v.Coord.FromPanel
                { X = (p.X - v.Plot.X0) / w * 1.0<nd>
                  Y = (v.Plot.Y1 - p.Y) / h * 1.0<nd> }

    /// The data under a surface point, as raw values in each axis's own unit.
    let toData (p: Vec2<px>) (v: PanelView) : (float * float) option =
        toUnit p v |> Option.map (fun u -> v.X.Invert u.X, v.Y.Invert u.Y)

    /// The data domains a px rectangle covers, each ascending: the box around its four corners'
    /// inverses, which under a cartesian coord is the rectangle itself. `None` when a corner has
    /// no inverse.
    let domainsOf (r: Rect<px>) (v: PanelView) : ((float * float) * (float * float)) option =
        let corners =
            [| { X = r.X0; Y = r.Y0 }; { X = r.X1; Y = r.Y0 }; { X = r.X1; Y = r.Y1 }; { X = r.X0; Y = r.Y1 } |]
            |> Array.map (fun c -> toData c v)

        if corners |> Array.exists Option.isNone then
            None
        else
            let xs = corners |> Array.map (fun c -> fst c.Value)
            let ys = corners |> Array.map (fun c -> snd c.Value)
            Some((Array.min xs, Array.max xs), (Array.min ys, Array.max ys))

    /// **D105 — what a pointer reads**: one line per axis, x first, each the axis's name and the
    /// value under the point, written in the prefix and unit text the axis's ticks are written
    /// in. A categorical axis reads the level whose band the point is in.
    ///
    /// **The places are the ones a pixel resolves.** A reading is good to how far the value moves
    /// when the pointer moves by one px, read back through the scales and the coord, so a log
    /// axis gets more places near its bottom than near its top and a polar plot gets them from
    /// both directions at once. Places past that would be the pointer's noise written down as
    /// data; a step coarser than 1 is rounded to its decade, so a count axis reads `4120000` and
    /// not a seven-digit number whose last four digits are the pixel grid.
    ///
    /// `None` when the point has no inverse, or is outside the plot.
    let read (p: Vec2<px>) (v: PanelView) : string[] option =
        match (if contains p v then toData p v else None) with
        | None -> None
        | Some(x, y) ->
            // How far each value moves over one px, in either direction across the surface,
            // the larger of the two: what the coord turns sideways, a pixel still resolves.
            let moved (dx: float<px>) (dy: float<px>) =
                match toData { X = p.X + dx; Y = p.Y + dy } v with
                | Some(x', y') -> Some(abs (x' - x), abs (y' - y))
                | None ->
                    toData { X = p.X - dx; Y = p.Y - dy } v
                    |> Option.map (fun (x', y') -> abs (x' - x), abs (y' - y))

            let span f =
                [ moved 1.0<px> 0.0<px>; moved 0.0<px> 1.0<px> ]
                |> List.choose id
                |> List.map f
                |> List.fold max 0.0

            let line (spec: AxisSpec) (scale: IScaleErased) (value: float) (resolution: float) =
                let shown =
                    match scale.Kind with
                    | ScaleKind.Ordinal levels -> Scale.levelLabel levels value
                    | _ ->
                        let factor = Prefix.factor spec.Prefix
                        let r = resolution / factor
                        let scaled = value / factor

                        let number =
                            if r >= 1.0 && not (System.Double.IsInfinity r) then
                                let decade = 10.0 ** floor (log10 r)
                                Ticks.format 0 (System.Math.Round(scaled / decade) * decade)
                            else
                                Ticks.format (Ticks.decimalsFor r) scaled

                        UnitDisplay.withUnit spec.UnitText number

                if spec.Label = "" then shown else spec.Label + " = " + shown

            Some [| line v.XAxis v.X x (span fst); line v.YAxis v.Y y (span snd) |]

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

    /// The key a layer's mark carries: by the data's own key when the layer has them (D104),
    /// by its original row otherwise (D68). `row` is always the original row.
    let markKey (l: Layer) (layerKey: Key) (row: int) : Key =
        if l.RowKeys.Length = 0 then Keys.mark layerKey row else Keys.datum layerKey l.RowKeys.[row]

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

            // D98 — the one difference `Dim` cannot see. An angular velocity and a frequency are
            // both s⁻¹ and differ by 2π, so a rad/s layer and a Hz layer on one axis plot one of
            // them 6.28 times too high without a word.
            let a0 = U.angleExponentOf s0.Symbol s0.Factors
            let ai = U.angleExponentOf si.Symbol si.Factors

            if si.Dim = s0.Dim && ai <> a0 then
                failwithf
                    "Marey: %s has %s %s axis in %s but %s has one in %s. Their dimensions agree, but one carries an angle and the other does not — an angular velocity and a frequency differ by 2π. They cannot share an axis."
                    (describe 0)
                    (if axis = "x" then "an" else "a")
                    axis
                    (UnitDisplay.unitTextErasedWith Unicode UnitRegistry.si Prefix.none s0.Symbol s0.Factors s0.Dim s0.ToSI s0.OffsetSI)
                    (describe i)
                    (UnitDisplay.unitTextErasedWith Unicode UnitRegistry.si Prefix.none si.Symbol si.Factors si.Dim si.ToSI si.OffsetSI)

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

    /// A panel's inner grid, from gutters that are already decided. Pure arithmetic on the record
    /// plus the solver — it measures nothing, which is exactly what lets a frozen layout be handed
    /// in from outside and produce the same rectangle every time. The drawing pass and
    /// `panelsIn` both take the plot rectangle from here, so there is one of it.
    let private innerGrid (m: ITextMeasurer) (extent: Extent<px>) (region: Rect<px>) (g: PanelLayout) =
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

    /// A panel's two axes as they are drawn, under the prefixes given (`None` chooses from the
    /// panel's own domain). The drawing pass and `panelsIn` both take them from here, so a
    /// pointer's reading cannot be written in a unit the axis is not.
    let private axisSpecs (theme: Theme) (pinX: Prefix option, pinY: Prefix option) (p: Panel) : AxisSpec * AxisSpec =
        let (xPacked, yPacked) = packedScales p.Layers.[0].Data

        // The one place the theme's unit spelling and a per-axis override meet. Resolved here
        // rather than at construction time so that changing the theme on a finished figure
        // changes its axis titles — which is what `Chart.withTheme Theme.tikz` has to do.
        let unitStyle (cfg: AxisConfig) = defaultArg cfg.Style theme.UnitStyle

        Axes.specOfPinned pinX (unitStyle p.XAxis) p.XAxis.TickCount p.XAxis.Label AxisBottom xPacked,
        Axes.specOfPinned pinY (unitStyle p.YAxis) p.YAxis.TickCount p.YAxis.Label AxisLeft yPacked

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
        (pins: Prefix option * Prefix option)
        (p: Panel)
        : PanelLayout * (unit -> PanelLayout) * (unit -> Node) =
        if p.Layers.Length = 0 then
            PanelLayout.empty, (fun () -> PanelLayout.empty), (fun () -> Scene.empty)
        else
            // D68 — this panel's name, from the grid position it already writes into the
            // `Annotated` node at the end of this function. `Keyed` says *which* panel; the
            // `Annotated` beside it says that it is a panel. Both, because §5.2 and §5.3 are
            // different questions.
            let panelKey = Keys.panel p.Row p.Col

            // D73 — the prefix comes from the frozen layout when there is one. A still figure
            // pins nothing and chooses per figure, which is what it has always done; an
            // animation pins both axes against the union of every frame's domain, so the axis
            // *title* cannot step from `mm` to `µm` halfway through a zoom.
            //
            // `pins` is the same choice for a panel being *measured* rather than drawn: the union
            // measures every state under the prefix it will be drawn in, or a state whose own
            // prefix differs is measured with labels it never shows (D108).
            let pinX = match frozen with Some f -> f.XPrefix | None -> fst pins
            let pinY = match frozen with Some f -> f.YPrefix | None -> snd pins

            let xSpec, ySpec = axisSpecs theme (pinX, pinY) p

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

            let build (g: PanelLayout) = innerGrid m extent region g

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
                                    Some(markKey l layerKey, l.Addressed)

                            // D46 — **one batch per bucket, not one per point.** With no channels
                            // this is `Geom.compile`'s own path; with a continuous colour channel
                            // it is at most 256 groups for any number of points, which is what
                            // keeps §5.1's batching intact while the colour varies.
                            let body, counts =
                                match geom with
                                | GeomField(field, mark) ->
                                    // D86 / D91 — a field is drawn once, not once per colour
                                    // batch: its pixels and its contour levels are coloured
                                    // through the channel's own quantiser, the call the
                                    // colorbar's strips are drawn with, so the three agree.
                                    let colourOf =
                                        match
                                            l.Channels
                                            |> Array.tryFind (fun c ->
                                                match c.Map with
                                                | ColorByValue _ -> true
                                                | _ -> false)
                                        with
                                        | Some c ->
                                            let bucket = Channel.bucketer c.Map

                                            fun (v: float) ->
                                                match bucket v with
                                                | k when k < 0 -> None
                                                | k ->
                                                    let col = (Channel.restyle c.Map k l.Style).Color
                                                    Some(Color.withAlpha (col.A * l.Style.Opacity) col)
                                        | None -> fun v -> if System.Double.IsNaN v || System.Double.IsInfinity v then None else Some l.Style.Color

                                    Geom.compileField p.Coord plotSize m theme.PanelBackground l.Style colourOf field mark, [||]
                                | _ ->
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
                        // D103 — everything but the background, the frame and the title is
                        // keyed below the panel, so a change to one part replaces that part.
                        // An empty part is pruned with its key, so a panel with no legend
                        // writes no empty element for one.
                        Scene.prune (
                            Group
                                [| Axes.panelBackground theme p.Coord plotNd
                                   Keyed(Keys.grid panelKey, Axes.gridlines theme p.Coord plotNd gridX gridY)
                                   data
                                   Keyed(Keys.axisX panelKey, axisNode AxisBottom p.XAxis xSpec xPlan)
                                   Keyed(Keys.axisY panelKey, axisNode AxisLeft p.YAxis ySpec yPlan)
                                   Axes.panelFrame theme p.Coord plotNd
                                   panelTitle
                                   Keyed(Keys.legend panelKey, legend)
                                   Keyed(Keys.colorbar panelKey, colorbarNode) |]
                        )
                    )
                )

            // D106 — what this panel's own labels need inside the rectangle it was given. With
            // no frozen layout that is the layout. With one, it is the gutters the tick plan made
            // at the frozen probe asks for, under the frozen prefixes: the question a widget asks
            // of a state nobody sampled. Lazy, because drawing never needs it.
            let needed () =
                match frozen with
                | None -> layout
                | Some f ->
                    let (xPlan, yPlan, barPlan) = plan f.Probe.Width f.Probe.Height
                    gutters xPlan yPlan barPlan f.Probe

            layout, needed, draw

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

    // ---- D94: what a shot pins besides the gutters -------------------------------------------
    //
    // A frame normalises two things to its own data: a continuous colour channel's domain is the
    // range of its values, and a quiver's length scale is set against its largest magnitude.
    // Across a film both renormalise every frame, so a field that grows tenfold looks the same
    // throughout. D72 already builds every sampled state, so the union of their domains, and the
    // largest of their quiver magnitudes, cost nothing more to take, and they are pinned here in
    // the way D73 pins a prefix.

    let private channelKey (panel: string) (layer: int) (channel: int) = sprintf "%s/%d/%d" panel layer channel
    let private layerKey (panel: string) (layer: int) = sprintf "%s/%d" panel layer
    let private panelKey (p: Panel) = sprintf "panel-%d-%d" p.Row p.Col

    let private colourDomainsOf (states: Figure[]) : (string * (float * float))[] =
        let finite (v: float) = not (System.Double.IsNaN v || System.Double.IsInfinity v)

        states
        |> Array.collect (fun fig ->
            fig.Panels
            |> Array.collect (fun p ->
                p.Layers
                |> Array.mapi (fun i l ->
                    l.Channels
                    |> Array.mapi (fun j c ->
                        match c.Map with
                        | ColorByValue(scale, _) ->
                            let lo, hi = Axis.domainOfPacked scale
                            if finite lo && finite hi then Some(channelKey (panelKey p) i j, (lo, hi)) else None
                        | _ -> None)
                    |> Array.choose id)
                |> Array.concat))
        |> Array.groupBy fst
        |> Array.map (fun (key, entries) ->
            let domains = entries |> Array.map snd
            let lo = domains |> Array.map (fun (a, b) -> min a b) |> Array.min
            let hi = domains |> Array.map (fun (a, b) -> max a b) |> Array.max
            // A reversed colour scale stays reversed.
            let a0, b0 = domains.[0]
            key, (if a0 <= b0 then (lo, hi) else (hi, lo)))

    let private quiverKeysOf (states: Figure[]) : (string * QuiverKey)[] =
        states
        |> Array.collect (fun fig ->
            fig.Panels
            |> Array.collect (fun p ->
                p.Layers
                |> Array.mapi (fun i l ->
                    match l.Geom with
                    | GeomField(_, FieldQuiver(Some k)) -> Some(layerKey (panelKey p) i, k)
                    | _ -> None)
                |> Array.choose id))
        |> Array.groupBy fst
        |> Array.map (fun (key, entries) -> key, entries |> Array.map snd |> Array.maxBy (fun k -> k.Largest))

    /// The same scale kind and unit over another raw domain.
    let private rescale (scale: PackedScale) (lo: float, hi: float) : PackedScale =
        scale.Accept
            { new IScaleConsumer<PackedScale> with
                member _.Apply<[<Measure>] 'u>(s: Scale<'u>) =
                    Erase.packScale (
                        Scale.ofKind s.Kind s.Unit (LanguagePrimitives.FloatWithMeasure<'u> lo, LanguagePrimitives.FloatWithMeasure<'u> hi)
                    ) }

    /// A panel with the shot's colour domains and quiver keys put into its layers.
    let private pinned (domains: (string * (float * float))[]) (keys: (string * QuiverKey)[]) (p: Panel) : Panel =
        if domains.Length = 0 && keys.Length = 0 then
            p
        else
            let pk = panelKey p
            let find (k: string) (table: (string * 'a)[]) = table |> Array.tryPick (fun (k', v) -> if k' = k then Some v else None)

            { p with
                Layers =
                    p.Layers
                    |> Array.mapi (fun i l ->
                        { l with
                            Channels =
                                l.Channels
                                |> Array.mapi (fun j c ->
                                    match c.Map, find (channelKey pk i j) domains with
                                    | ColorByValue(scale, ramp), Some d -> { c with Map = ColorByValue(rescale scale d, ramp) }
                                    | _ -> c)
                            Geom =
                                match l.Geom, find (layerKey pk i) keys with
                                | GeomField(f, FieldQuiver _), Some k -> GeomField(f, FieldQuiver(Some k))
                                | g, _ -> g }) }

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
                    let layout, _, _ = compilePanel m fig.Theme extent region None (None, None) (unify p)
                    Some(key, layout))

        { Outer = outer
          Panels = panels
          Samples = 1
          ColourDomains = [||]
          QuiverKeys = [||] }

    /// The head row's height: the title and the subtitle, each with its gap, or nothing.
    let private headHeight (m: ITextMeasurer) (fig: Figure) =
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

    /// **D106 — one arrangement over states a caller lists.** `arrangeUnion` is this over samples
    /// of time; a widget calls it over the states its controls can reach (their extremes), which
    /// it knows without running anything, because the controls are known before the view runs.
    ///
    /// Everything `arrangeUnion` says about the union holds: componentwise, the prefixes pinned
    /// from the union domain, the colour domains and quiver keys (D94) held across the states.
    /// `Samples` reports how many states there were.
    let arrangeOver (m: ITextMeasurer) (extent: Extent<px>) (raw: Figure[]) : Shot =
        if raw.Length = 0 then
            invalidArg "raw" "Marey: a shot needs at least one state."

        let colourDomains = colourDomainsOf raw
        let quiverKeys = quiverKeysOf raw

        let states =
            raw |> Array.map (fun f -> { f with Panels = f.Panels |> Array.map (pinned colourDomains quiverKeys) })

        // The outer grid first, because a panel cannot be measured until it knows its region and
        // the region depends on how tall the head row is.
        let headOf = headHeight m

        let head = states |> Array.map headOf |> Array.max

        // The grid is the union of every state's: as many rows and columns as any state has,
        // and a region for every panel position any state uses. It used to be taken from the
        // one state with the most panels, which is the same thing until two states have the
        // same count in different shapes — a 1×2 and a 2×1 — and then one of them lost a panel
        // to a grid that had no region for it. Gaps, padding and the theme do not vary with the
        // data, so the first state supplies them.
        let template =
            { states.[0] with
                Rows = states |> Array.map (fun f -> max 1 f.Rows) |> Array.max
                Cols = states |> Array.map (fun f -> max 1 f.Cols) |> Array.max
                Panels =
                    states
                    |> Array.collect (fun f -> f.Panels)
                    |> Array.distinctBy (fun p -> p.Row, p.Col) }

        let outer = Layout.arrange m extent (outerGrid (Some head) template)

        // Then every panel of every state, unioned by key. `Array.choose` rather than an
        // exception when a key is missing: a state that lacks a panel another state has
        // contributes nothing to that panel's union, which is the right answer — an absent panel
        // needs no gutters.
        let entries =
            states
            |> Array.collect (fun fig ->
                fig.Panels
                |> Array.choose (fun p ->
                    let key = sprintf "panel-%d-%d" p.Row p.Col

                    Layout.tryFindPx key outer
                    |> Option.map (fun region -> key, (fig, region, unify p))))

        // D73 — the prefix, chosen once from the union of every frame's domain rather than per
        // frame from its own. This is the same mechanism as the gutters and not a second one:
        // which decade a prefix comes from is a property of "the numbers" (D19), and across a
        // shot the numbers are all of them.
        //
        // **Chosen before anything is measured** — D108, M4.5's correction to it. Each state
        // used to be measured under its own prefix and drawn under the pinned one, so a state
        // from the low end of a decade-crossing zoom was measured as `0 2 4 6 8` and drawn as
        // `0.002 … 0.008`, in a gutter sized for the shorter labels. The rectangle held still,
        // which is what gate 8 checked, and the labels did not fit in it, which nothing checked.
        // `fitsIn` asked that question for a widget and found it.
        //
        // A layerless panel has no numbers and therefore no prefix to pin. `toScene` draws one
        // as `Scene.empty`, so it is a case that exists and has to be answered rather than
        // indexed into.
        let pins =
            entries
            |> Array.groupBy fst
            |> Array.map (fun (key, es) ->
                let drawn = es |> Array.map (fun (_, (_, _, u)) -> u) |> Array.filter (fun p -> p.Layers.Length > 0)

                let pinFor (packedOf: Panel -> PackedScale) =
                    if drawn.Length = 0 then
                        None
                    else
                        let domains = drawn |> Array.map (packedOf >> Axis.domainOfPacked)
                        let lo = domains |> Array.map fst |> Array.min
                        let hi = domains |> Array.map snd |> Array.max
                        Some(Axis.prefixForPacked (lo, hi) (packedOf drawn.[0]))

                key,
                (pinFor (fun p -> fst (packedScales p.Layers.[0].Data)),
                 pinFor (fun p -> snd (packedScales p.Layers.[0].Data))))
            |> dict

        let panels =
            entries
            |> Array.map (fun (key, (fig, region, unified)) ->
                let layout, _, _ = compilePanel m fig.Theme extent region None pins.[key] unified
                key, layout)
            |> Array.groupBy fst
            |> Array.map (fun (key, measured) ->
                let merged = measured |> Array.map snd |> Array.reduce PanelLayout.union
                let pinX, pinY = pins.[key]
                key, { merged with XPrefix = pinX; YPrefix = pinY })

        { Outer = outer
          Panels = panels
          Samples = raw.Length
          ColourDomains = colourDomains
          QuiverKeys = quiverKeys }

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

        // D94 — the colour domains and quiver keys first, and every state measured with them in
        // place: the union domain's colorbar labels can be wider than any one state's.
        arrangeOver m extent (times |> Array.map sample)

    /// **D106 — does a state fit a layout that is already decided?** A widget lays out once over
    /// the states its controls can reach at their extremes, and the reader can still choose one
    /// between them whose labels are wider, or zoom to a domain no extreme had. This is the
    /// question it asks of each new state, before drawing it in the old rectangle.
    ///
    /// Exact rather than estimated: each panel is planned the way `toSceneIn` will plan it, at the
    /// frozen probe, under the frozen prefixes and colour domains, and the gutters that plan needs
    /// are compared with the frozen ones. A panel at a grid position the shot has no region for
    /// does not fit; a panel with no layers needs nothing.
    let fitsIn (m: ITextMeasurer) (shot: Shot) (fig: Figure) : bool =
        let extent = shot.Outer.Extent
        let within (a: float<px>) (b: float<px>) = a <= b + 1e-9<px>

        let headRoom =
            match Layout.tryFindPx "head" shot.Outer with
            | Some r -> Rect.height r
            | None -> 0.0<px>

        let panelFits (p: Panel) =
            if p.Layers.Length = 0 then
                true
            else
                let key = sprintf "panel-%d-%d" p.Row p.Col
                let frozen = shot.Panels |> Array.tryPick (fun (k, l) -> if k = key then Some l else None)

                match Layout.tryFindPx key shot.Outer, frozen with
                | Some region, Some f ->
                    let _, needed, _ =
                        compilePanel m fig.Theme extent region (Some f) (None, None) (unify (pinned shot.ColourDomains shot.QuiverKeys p))

                    let n = needed ()

                    within n.TitleH f.TitleH
                    && within n.TopPad f.TopPad
                    && within n.XTicksH f.XTicksH
                    && within n.XTitleH f.XTitleH
                    && within n.YTitleW f.YTitleW
                    && within n.YTicksW f.YTicksW
                    && within n.RightPad f.RightPad
                    && within n.BarGap f.BarGap
                    && within n.BarW f.BarW
                | _ -> false

        within (headHeight m fig) headRoom && Array.forall panelFits fig.Panels

    /// **D105 — every drawn panel of a figure, as a pointer meets it in a shot.** The plot
    /// rectangle comes from the shot's frozen inner grid through `innerGrid`, the function the
    /// drawing pass takes it from, and the scales from the panel after `unify`. A panel with no
    /// layers, or one the shot has no layout for, has nothing to point at and is left out.
    let panelsIn (m: ITextMeasurer) (shot: Shot) (fig: Figure) : PanelView[] =
        fig.Panels
        |> Array.choose (fun p ->
            let key = sprintf "panel-%d-%d" p.Row p.Col
            let frozen = shot.Panels |> Array.tryPick (fun (k, l) -> if k = key then Some l else None)

            match p.Layers.Length, Layout.tryFindPx key shot.Outer, frozen with
            | 0, _, _ -> None
            | _, Some region, Some f ->
                let plot = Layout.findPx "plot" (innerGrid m shot.Outer.Extent region f)
                let unified = unify p
                let sx, sy = Erase.seriesScales unified.Layers.[0].Data
                let ax, ay = axisSpecs fig.Theme (f.XPrefix, f.YPrefix) unified

                Some
                    { Row = p.Row
                      Col = p.Col
                      Plot = plot
                      Coord = p.Coord
                      X = sx
                      Y = sy
                      XAxis = ax
                      YAxis = ay }
            | _ -> None)

    /// One panel's domains, stated: `Chart.withXDomain` for a panel other than the last. Raw
    /// values in each axis's own unit; `None` leaves an axis as it is.
    let withPanelDomains
        (row: int, col: int)
        (dx: (float * float) option)
        (dy: (float * float) option)
        (fig: Figure)
        : Figure =
        { fig with
            Panels =
                fig.Panels
                |> Array.map (fun p ->
                    if p.Row = row && p.Col = col then
                        { p with Layers = p.Layers |> Array.map (fun l -> { l with Data = withDomains dx dy l.Data }) }
                    else
                        p) }

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
                    let _, _, draw = compilePanel m theme extent region frozen (None, None) (unify (pinned shot.ColourDomains shot.QuiverKeys p))
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
