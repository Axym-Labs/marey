namespace Marey

/// Everything a geom needs to know about how it looks, resolved. Same rule as `Theme`: concrete
/// values, no lookups.
[<NoComparison>]
type GeomStyle =
    { Color: Color
      Width: float<px>
      Dash: float<px>[]
      /// Multiplies `Color.A` for strokes and marks.
      Opacity: float
      /// Multiplies `Color.A` for area and bar fills.
      FillOpacity: float
      Shape: MarkerShape
      MarkerSize: float<px>
      Cap: LineCap
      Join: LineJoin }

/// M0's geoms (§16: "line/scatter/errorbar" by name).
///
/// The numeric payloads are already in **panel-local `nd`**, because they were produced by the
/// series' own scales at construction time by a *typed* constructor. That is what forces an
/// error bar's magnitude to share the y axis's measure: `Geom.errorY` takes `float('uy)[]`
/// against a `Series('ux,'uy)`, so handing it seconds for a metre axis is a compile error, and
/// by the time the value reaches this DU the measure has done its job and gone.
[<NoComparison>]
type Geom =
    | GeomLine
    | GeomPoint
    /// Filled between the data and a baseline, given in panel-local nd.
    | GeomArea of baseline: float<nd>
    /// Bars from a baseline, width as a fraction of the median x spacing.
    | GeomBar of baseline: float<nd> * widthFraction: float
    /// Vertical error bars: low and high y, per point, in panel-local nd.
    | GeomErrorY of low: float<nd>[] * high: float<nd>[]
    /// Horizontal error bars: low and high x, per point, in panel-local nd.
    | GeomErrorX of low: float<nd>[] * high: float<nd>[]

/// §5.5 — **"error bars … are compiled into `Path`/`Text`/`Markers` by the layer above."**
///
/// Every geom in this module bottoms out in `Path` or `Markers`. There is no error-bar node, no
/// bar node and no area node, and the whole file is the argument that none is needed.
///
/// ### Why geoms consume `PackedSeries` and not `Series('ux,'uy)`
///
/// A geom needs positions, and positions are `Vec2(nd)` — the measures are gone by then, by
/// construction (§2.3: "erase *once*, at the point where a series becomes a scale"). Making
/// geoms measure-generic would push two type parameters through `Layer`, `Panel` and `Figure`,
/// and a figure with a metres panel beside a joules panel could then never be one value. That
/// is the exact problem §2.3's rank-2 encoding exists to solve, so geoms take the erased side of
/// it and `Erase.projectXY` is the whole of the conversion.
///
/// The measures are not thrown away, though — they are *spent* first. Everything that needs
/// them (the domain, the baseline of an area, the span of an error bar) is computed by a typed
/// constructor below while the `Series('ux,'uy)` is still in hand, so the dimensional check
/// happens where the user writes the code rather than where the library draws it.
[<RequireQualifiedAccess>]
module Geom =

    let private nd (x: float) : float<nd> = x * 1.0<nd>

    let private finite (p: Vec2<nd>) =
        let x = float p.X
        let y = float p.Y

        not (System.Double.IsNaN x)
        && not (System.Double.IsNaN y)
        && not (System.Double.IsInfinity x)
        && not (System.Double.IsInfinity y)

    // ---- Styles -------------------------------------------------------------------------

    let style (theme: Theme) (color: Color) : GeomStyle =
        { Color = color
          Width = theme.SeriesWidth
          Dash = [||]
          Opacity = 1.0
          FillOpacity = theme.FillOpacity
          Shape = MarkCircle
          MarkerSize = theme.MarkerSize
          Cap = Round
          Join = RoundJoin }

    /// Style for series `i` of a theme: colour and marker shape cycle together, so that shape is
    /// a redundant encoding of colour rather than a decoration (D5).
    let styleFor (theme: Theme) (i: int) : GeomStyle =
        { style theme (Palette.pick theme.Categorical i) with
            Shape =
                if theme.MarkerShapes.Length = 0 then
                    MarkCircle
                else
                    theme.MarkerShapes.[((i % theme.MarkerShapes.Length) + theme.MarkerShapes.Length) % theme.MarkerShapes.Length] }

    let withColor (c: Color) (s: GeomStyle) = { s with Color = c }
    let withWidth (w: float<px>) (s: GeomStyle) = { s with Width = w }
    let withDash (d: float<px>[]) (s: GeomStyle) = { s with Dash = d }
    let withOpacity (o: float) (s: GeomStyle) = { s with Opacity = o }
    let withShape (sh: MarkerShape) (s: GeomStyle) = { s with Shape = sh }
    let withMarkerSize (sz: float<px>) (s: GeomStyle) = { s with MarkerSize = sz }

    // ---- Typed constructors: the last place the measures are alive -----------------------

    /// Where zero on this scale falls in panel space, clamped into the panel.
    ///
    /// An area filled to "the bottom of the panel" is a different chart from one filled to zero,
    /// and only the latter is honest about magnitudes. When zero is outside the domain the
    /// clamp puts the baseline on the nearer edge, which is the conventional compromise.
    let baselineOf (scale: Scale<'u>) : float<nd> =
        let z = scale.Forward(LanguagePrimitives.FloatWithMeasure<'u> 0.0)
        let v = float z

        if System.Double.IsNaN v then 0.0<nd>
        elif v < 0.0 then 0.0<nd>
        elif v > 1.0 then 1.0<nd>
        else z

    /// Symmetric error bars on y.
    ///
    /// **The signature is the point.** `err` is `float('uy)[]` — the same measure as the y data
    /// — so an uncertainty in seconds on a displacement axis does not compile. Projection uses
    /// the series' own y scale, so a log axis gets asymmetric-looking bars, correctly.
    let errorY (s: Series<'ux, 'uy>) (err: float<'uy>[]) : Geom =
        if err.Length <> s.Y.Length then
            invalidArg "err" (sprintf "Marey: series '%s' has %d y values and %d error values." s.Name s.Y.Length err.Length)

        GeomErrorY(
            Array.init s.Y.Length (fun i -> s.YScale.Forward(s.Y.[i] - err.[i])),
            Array.init s.Y.Length (fun i -> s.YScale.Forward(s.Y.[i] + err.[i]))
        )

    /// Asymmetric error bars on y, given as absolute low and high values in the y unit.
    let errorYBetween (s: Series<'ux, 'uy>) (low: float<'uy>[]) (high: float<'uy>[]) : Geom =
        if low.Length <> s.Y.Length || high.Length <> s.Y.Length then
            invalidArg "low" (sprintf "Marey: series '%s' has %d y values; low/high must match." s.Name s.Y.Length)

        GeomErrorY(Array.map s.YScale.Forward low, Array.map s.YScale.Forward high)

    /// Symmetric error bars on x. Same dimensional guarantee, other axis.
    let errorX (s: Series<'ux, 'uy>) (err: float<'ux>[]) : Geom =
        if err.Length <> s.X.Length then
            invalidArg "err" (sprintf "Marey: series '%s' has %d x values and %d error values." s.Name s.X.Length err.Length)

        GeomErrorX(
            Array.init s.X.Length (fun i -> s.XScale.Forward(s.X.[i] - err.[i])),
            Array.init s.X.Length (fun i -> s.XScale.Forward(s.X.[i] + err.[i]))
        )

    let area (s: Series<'ux, 'uy>) : Geom = GeomArea(baselineOf s.YScale)

    let bar (widthFraction: float) (s: Series<'ux, 'uy>) : Geom =
        GeomBar(baselineOf s.YScale, max 0.05 (min 1.0 widthFraction))

    // ---- Compilation ---------------------------------------------------------------------

    let private strokeOf (st: GeomStyle) =
        { Paint = Solid(Color.withAlpha (st.Color.A * st.Opacity) st.Color)
          Width = st.Width
          Dash = st.Dash
          DashOffset = 0.0<px>
          Cap = st.Cap
          Join = st.Join
          MiterLimit = 4.0 }

    let private fillOf (st: GeomStyle) =
        Solid(Color.withAlpha (st.Color.A * st.FillOpacity) st.Color)

    /// Contiguous runs of finite points. A NaN is a gap in the data and must break the line
    /// rather than being drawn through — one of the small correctness details that separates a
    /// plotting library from a polyline renderer.
    let private runsOf (pts: Vec2<nd>[]) =
        [| let mutable start = -1

           for i in 0 .. pts.Length - 1 do
               if finite pts.[i] then
                   if start < 0 then start <- i
               else if start >= 0 then
                   yield pts.[start .. i - 1]
                   start <- -1

           if start >= 0 then
               yield pts.[start..] |]

    let private samplesFor (coord: Coord) = if Coord.isIdentity coord then 1 else 16

    let private warp (coord: Coord) (pts: Vec2<nd>[]) =
        if Coord.isIdentity coord then
            pts
        else
            Coord.warpPolyline coord (samplesFor coord) pts

    /// Median spacing between consecutive x positions. Bar width comes from the data's own
    /// rhythm rather than from a magic constant.
    let private medianSpacing (pts: Vec2<nd>[]) =
        if pts.Length < 2 then
            0.5
        else
            let gaps =
                Array.init (pts.Length - 1) (fun i -> abs (float pts.[i + 1].X - float pts.[i].X))
                |> Array.filter (fun g -> g > 0.0)

            if gaps.Length = 0 then
                0.5
            else
                let sorted = Array.sort gaps
                sorted.[sorted.Length / 2]

    /// The body of `compile`, with one parameter `compile` does not expose.
    ///
    /// `spacing` overrides the rhythm a bar's width is taken from. `None` means "the points
    /// you were given", which is right for a whole layer and wrong for one channel batch of
    /// one — three coloured batches of a bar chart are not three bar charts, and each would
    /// otherwise measure its own width from the gaps between its own bars. See `compileSubset`.
    let rec private compileWith
        (spacing: float option)
        (coord: Coord)
        (plotSize: Extent<px>)
        (st: GeomStyle)
        (geom: Geom)
        (pts: Vec2<nd>[])
        : Node =
        if pts.Length = 0 then
            Scene.empty
        else
            let stroke = strokeOf st

            match geom with

            | GeomLine ->
                let runs = runsOf pts

                if runs.Length = 0 then
                    Scene.empty
                else
                    let segs =
                        [| for run in runs do
                               let w = warp coord run

                               if w.Length > 0 then
                                   yield MoveTo w.[0]

                                   for i in 1 .. w.Length - 1 do
                                       yield LineTo w.[i] |]

                    Path(segs, Some stroke, None, NonZero)

            // §5.1 — "a 100 000-point scatter must be *one* IR node". One `Markers`, always.
            | GeomPoint ->
                let ps = pts |> Array.filter finite

                let ps = if Coord.isIdentity coord then ps else Array.map coord.ToPanel ps

                if ps.Length = 0 then
                    Scene.empty
                else
                    Markers
                        { Points = ps
                          Shape = st.Shape
                          Size = st.MarkerSize
                          Fill = Some(Solid(Color.withAlpha (st.Color.A * st.Opacity) st.Color))
                          Stroke = None }

            | GeomArea baseline ->
                let runs = runsOf pts

                let segs =
                    [| for run in runs do
                           if run.Length >= 2 then
                               let top = warp coord run

                               let bottom =
                                   run
                                   |> Array.map (fun p -> { X = p.X; Y = baseline })
                                   |> Array.rev
                                   |> warp coord

                               yield MoveTo top.[0]

                               for i in 1 .. top.Length - 1 do
                                   yield LineTo top.[i]

                               for p in bottom do
                                   yield LineTo p

                               yield ClosePath |]

                if segs.Length = 0 then
                    Scene.empty
                else
                    Group
                        [| Path(segs, None, Some(fillOf st), NonZero)
                           compileWith spacing coord plotSize st GeomLine pts |]

            | GeomBar(baseline, widthFraction) ->
                let half = (defaultArg spacing (medianSpacing pts)) * widthFraction * 0.5

                let segs =
                    [| for p in pts do
                           if finite p then
                               let x0 = p.X - nd half
                               let x1 = p.X + nd half

                               let corners =
                                   [| { X = x0; Y = baseline }
                                      { X = x1; Y = baseline }
                                      { X = x1; Y = p.Y }
                                      { X = x0; Y = p.Y } |]

                               let w = if Coord.isIdentity coord then corners else Array.map coord.ToPanel corners

                               yield MoveTo w.[0]
                               yield LineTo w.[1]
                               yield LineTo w.[2]
                               yield LineTo w.[3]
                               yield ClosePath |]

                if segs.Length = 0 then
                    Scene.empty
                else
                    Path(segs, Some { stroke with Width = stroke.Width * 0.6 }, Some(fillOf st), NonZero)

            // §5.5's named example, and it is three line segments per point in one `Path`.
            | GeomErrorY(low, high) ->
                let capHalf =
                    if plotSize.Width = 0.0<px> then
                        0.0
                    else
                        float (st.MarkerSize / 2.0 / plotSize.Width)

                let n = min pts.Length (min low.Length high.Length)

                let segs =
                    [| for i in 0 .. n - 1 do
                           let p = pts.[i]

                           if finite p && finite { X = p.X; Y = low.[i] } && finite { X = p.X; Y = high.[i] } then
                               let a: Vec2<nd> = { X = p.X; Y = low.[i] }
                               let b: Vec2<nd> = { X = p.X; Y = high.[i] }
                               let l: Vec2<nd> = { X = p.X - nd capHalf; Y = a.Y }
                               let r: Vec2<nd> = { X = p.X + nd capHalf; Y = a.Y }
                               let l2: Vec2<nd> = { X = p.X - nd capHalf; Y = b.Y }
                               let r2: Vec2<nd> = { X = p.X + nd capHalf; Y = b.Y }

                               let m (v: Vec2<nd>) = if Coord.isIdentity coord then v else coord.ToPanel v

                               yield MoveTo(m a)
                               yield LineTo(m b)
                               yield MoveTo(m l)
                               yield LineTo(m r)
                               yield MoveTo(m l2)
                               yield LineTo(m r2) |]

                if segs.Length = 0 then
                    Scene.empty
                else
                    Path(segs, Some { stroke with Cap = Butt }, None, NonZero)

            | GeomErrorX(low, high) ->
                let capHalf =
                    if plotSize.Height = 0.0<px> then
                        0.0
                    else
                        float (st.MarkerSize / 2.0 / plotSize.Height)

                let n = min pts.Length (min low.Length high.Length)

                let segs =
                    [| for i in 0 .. n - 1 do
                           let p = pts.[i]

                           if finite p && finite { X = low.[i]; Y = p.Y } && finite { X = high.[i]; Y = p.Y } then
                               let a: Vec2<nd> = { X = low.[i]; Y = p.Y }
                               let b: Vec2<nd> = { X = high.[i]; Y = p.Y }
                               let l: Vec2<nd> = { X = a.X; Y = p.Y - nd capHalf }
                               let r: Vec2<nd> = { X = a.X; Y = p.Y + nd capHalf }
                               let l2: Vec2<nd> = { X = b.X; Y = p.Y - nd capHalf }
                               let r2: Vec2<nd> = { X = b.X; Y = p.Y + nd capHalf }

                               let m (v: Vec2<nd>) = if Coord.isIdentity coord then v else coord.ToPanel v

                               yield MoveTo(m a)
                               yield LineTo(m b)
                               yield MoveTo(m l)
                               yield LineTo(m r)
                               yield MoveTo(m l2)
                               yield LineTo(m r2) |]

                if segs.Length = 0 then
                    Scene.empty
                else
                    Path(segs, Some { stroke with Cap = Butt }, None, NonZero)

    /// Compile a geom into the IR, in **panel-local nd**.
    ///
    /// `plotSize` is the plot area's device size, and it is here for exactly one reason: an error
    /// bar's cap is `px` wide (D12) while this function's output is about to be scaled by the
    /// panel transform. Dividing by the panel's own pixel size is what makes a 3 px cap 3 px
    /// wide after that scaling, on both a wide panel and a tall one.
    let compile (coord: Coord) (plotSize: Extent<px>) (st: GeomStyle) (geom: Geom) (pts: Vec2<nd>[]) : Node =
        compileWith None coord plotSize st geom pts

    /// D46 — **one channel batch of a layer**: the points named by `indices`, drawn with
    /// everything that belongs to the *layer* still taken from the layer.
    ///
    /// Only bar width is such a thing today, and it is enough to justify the second entry
    /// point: split a four-band bar chart into two coloured batches and each batch would read
    /// the gaps between its own two bars as the data's rhythm, so the bars would come out
    /// twice as wide as an uncoloured chart's. The geometry a channel changes is the paint,
    /// not the layout.
    let compileSubset
        (coord: Coord)
        (plotSize: Extent<px>)
        (st: GeomStyle)
        (geom: Geom)
        (all: Vec2<nd>[])
        (indices: int[])
        : Node =
        // Only a bar reads the layer's rhythm, and reading it is a sort of every gap in the
        // series. Computing it for a geom that will not look at it would put that sort inside
        // the per-batch loop, which is 256 sorts of 100 000 gaps for one continuous colour
        // channel — measured at 1.5 s against 4 ms.
        let spacing =
            match geom with
            | GeomBar _ -> Some(medianSpacing all)
            | GeomLine
            | GeomPoint
            | GeomArea _
            | GeomErrorY _
            | GeomErrorX _ -> None

        compileWith spacing coord plotSize st geom (indices |> Array.map (fun i -> all.[i]))

    /// The legend swatch that matches a geom. Derived, never chosen separately.
    let legendMark (geom: Geom) (st: GeomStyle) : LegendMark =
        match geom with
        | GeomLine -> LegendLine st.Dash
        | GeomPoint -> LegendMarker st.Shape
        | GeomArea _
        | GeomBar _ -> LegendSwatch
        | GeomErrorY _
        | GeomErrorX _ -> LegendLineMarker(st.Dash, st.Shape)
