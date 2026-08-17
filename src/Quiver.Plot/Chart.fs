namespace Quiver

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

    /// A typed series over linear scales with 5% breathing room. The last point at which the
    /// measures are alive on this path.
    let series (name: string) (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Series<'ux, 'uy> =
        if x.Length <> y.Length then
            invalidArg "y" (sprintf "Quiver: series '%s' has %d x values and %d y values." name x.Length y.Length)

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
          ShowInLegend = not (System.String.IsNullOrEmpty name) }

    // ---- Constructors ------------------------------------------------------------------------

    /// A figure built from an already-scaled typed series. The full-control door: pick the
    /// scales yourself and everything downstream still works.
    let ofSeries (g: Geom) (s: Series<'ux, 'uy>) : Figure =
        figureOf [| layerOf s.Name Theme.publication 0 g (Erase.packSeries s) |]

    let line (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        ofSeries GeomLine (series "" ux uy x y)

    /// Scatter. **One `Markers` node for the whole series**, however many points — §5.1, "the
    /// single biggest performance decision in the IR". Verifiable with `Scene.count`.
    let scatter (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        ofSeries GeomPoint (series "" ux uy x y)

    let area (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        let s = series "" ux uy x y
        ofSeries (Geom.area s) s

    let bar (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        let s = series "" ux uy x y
        ofSeries (Geom.bar 0.7 s) s

    /// Error bars with symmetric uncertainties.
    ///
    /// `err` is `float('uy)[]` — the same measure as `y`. An uncertainty in the wrong unit does
    /// not compile, which is the whole thesis applied to the one place experimental data most
    /// often gets it wrong. The y domain is taken over `y ± err`, so the caps are inside the
    /// panel rather than clipped by it.
    let errorbar (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (err: float<'uy>[]) : Figure =
        if err.Length <> y.Length then
            invalidArg "err" (sprintf "Quiver: %d y values but %d error values." y.Length err.Length)

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
            [| layerOf "" Theme.publication 0 bars packed
               { layerOf "" Theme.publication 0 GeomPoint packed with
                   ShowInLegend = false } |]

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

        { fig with
            Theme = t
            Panels =
                fig.Panels
                |> Array.map (fun p ->
                    { p with
                        Layers =
                            p.Layers
                            |> Array.mapi (fun i l -> if l.Style = Geom.styleFor old i then { l with Style = Geom.styleFor t i } else l) }) }

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
                    XAxis = { p.XAxis with Style = s }
                    YAxis = { p.YAxis with Style = s } })
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
    /// `Quiver.Plot` takes no backend dependency, on purpose: §0's diagram has the DSLs above the
    /// IR and the backends below it, and a grammar package that references a renderer inverts
    /// that. Passing the renderer in costs one identifier and keeps the layering honest.
    let render (renderer: Extent<px> -> Node -> string) (extent: Extent<px>) (fig: Figure) : string =
        renderer extent (toScene extent fig)

    /// One line to a file: `fig |> Chart.toSvg SvgBackend.render "plot.svg" (Chart.size 800.0 500.0)`.
    ///
    /// UTF-8 without a byte-order mark, matching `SvgBackend.renderToFile`, because these files
    /// are golden masters (§13) and a BOM appearing would read as a whole-file diff.
    let toSvg (renderer: Extent<px> -> Node -> string) (path: string) (extent: Extent<px>) (fig: Figure) : unit =
        let text = render renderer extent fig
        System.IO.File.WriteAllText(path, text, System.Text.UTF8Encoding(false))
