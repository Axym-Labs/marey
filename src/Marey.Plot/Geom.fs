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
      Join: LineJoin
      /// The face a `GeomText` layer's labels are set in.
      ///
      /// A resolved value like every other field here, taken from the theme's *tick* font
      /// rather than a font of its own: a data label sits inside the plot area at the same
      /// visual weight as the numbers on the axis, and giving it a seventh theme font would be
      /// one more thing to keep in step for no difference anyone can see. `Geom.withFont`
      /// overrides it per layer.
      Font: FontSpec }

/// Where the riser of a step function goes, relative to the two points it joins.
///
/// Three cases, because the choice is a claim about the data rather than a style: a
/// sampled-and-held signal changes *at* the sample it was read at (`StepAfter`), a value
/// reported for the interval that ends at its timestamp changes at the previous one
/// (`StepBefore`), and a quantity known only to have changed somewhere between two samples is
/// conventionally drawn as changing halfway (`StepMid`). There is no default that is right for
/// all three, so the geom carries it.
type StepAlign =
    /// The value holds from this point to the next: across, then up.
    | StepAfter
    /// The value holds from the previous point: up, then across.
    | StepBefore
    /// The riser is halfway between the two points.
    | StepMid

/// One box of a box plot, in panel-local nd — the five-number summary after `Geom.boxplot`
/// has spent the measures on it.
[<NoComparison>]
type BoxSpan =
    { Lower: float<nd>
      Q1: float<nd>
      Median: float<nd>
      Q3: float<nd>
      Upper: float<nd>
      /// Everything outside the whiskers, drawn as points. D52's rule, one geom down: a datum
      /// the box cannot describe is still drawn, because a box plot that quietly discards its
      /// tails is a summary of a sample nobody measured.
      Outliers: float<nd>[] }

/// One violin of a violin plot: sample positions on the panel's y axis, and the half-width of
/// the profile at each of them.
///
/// `Width` is a **fraction of the band, normalised across every profile in the layer**
/// (`Geom.violin`), not per violin. A violin scaled to its own peak draws a group of four and
/// a group of four hundred at exactly the same width, which erases the comparison the plot
/// exists to make.
[<NoComparison>]
type ViolinProfile =
    { At: float<nd>[]
      Width: float[] }

/// M0's geoms (§16: "line/scatter/errorbar" by name), and M2 phase 6's.
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
    /// A staircase rather than a polyline: the same points, with the claim that nothing
    /// happened between them.
    | GeomStep of align: StepAlign
    /// A filled band between two y values per point, in panel-local nd.
    ///
    /// The band and nothing else — no line through the middle of it, because a confidence
    /// band wants the fit drawn over it and a min/max band usually wants nothing. Two layers
    /// say that; a geom that decided for you cannot.
    | GeomRibbon of low: float<nd>[] * high: float<nd>[]
    /// Axis-aligned rectangles given by opposite corners, in panel-local nd.
    ///
    /// `Geom.rect` builds them from data corners and `Geom.tile` from centres and one size,
    /// and both land here: a tile *is* a rectangle once the measures have been spent, and a
    /// second case would be a second branch in every consumer for no difference in what is
    /// drawn.
    | GeomRect of x0: float<nd>[] * y0: float<nd>[] * x1: float<nd>[] * y1: float<nd>[]
    /// A line from each point to a second point, in panel-local nd.
    | GeomSegment of x1: float<nd>[] * y1: float<nd>[]
    /// A label per point, offset from it by a device-space vector.
    | GeomText of labels: string[] * anchor: TextAnchor * baseline: TextBaseline * offset: Vec2<px>
    /// Box-and-whisker summaries, one per point; width as a fraction of the median x spacing.
    | GeomBox of spans: BoxSpan[] * widthFraction: float
    /// Mirrored density profiles, one per point; width as a fraction of the median x spacing.
    | GeomViolin of profiles: ViolinProfile[] * widthFraction: float
    /// A user-supplied RGBA image filling a box, in panel-local nd (D64).
    ///
    /// The one geom in this file that does **not** bottom out in `Path`, `Markers` or `Text`:
    /// it is the only producer of §5's `Raster` node anywhere above the IR, and until M2.5
    /// there was none, which is why `renderWithAssets` had no customer and D31's "M2 door" had
    /// nothing to walk through it.
    ///
    /// **D55 does not apply here, and the reason is worth stating.** Every other geom carrying
    /// its own payload carries a per-*point* array and must be told which rows a channel batch
    /// renumbered it to. This one carries a whole image and one box — scalars, not arrays — so
    /// a batched subset draws the same image in the same place, which is correct rather than
    /// lucky. A channel over an image layer has nothing to say.
    | GeomImage of image: Image * bounds: Rect<nd> * smooth: bool

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

    let private finiteNum (x: float) =
        not (System.Double.IsNaN x) && not (System.Double.IsInfinity x)

    let private finite (p: Vec2<nd>) = finiteNum (float p.X) && finiteNum (float p.Y)

    /// A point of the layer, paired with a y taken from one of the geom's own arrays.
    let private at (x: float<nd>) (y: float<nd>) : Vec2<nd> = { X = x; Y = y }

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
          Join = RoundJoin
          Font = theme.TickFont }

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
    let withFillOpacity (o: float) (s: GeomStyle) = { s with FillOpacity = o }
    let withFont (f: FontSpec) (s: GeomStyle) = { s with Font = f }

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

    // ---- Phase 6's typed constructors ----------------------------------------------------
    //
    // Same rule as the four above: the measures are spent here, where the caller wrote the
    // code, and what reaches the DU is panel-local `nd`. Every one of these takes the series it
    // will be drawn against, so the arrays it is handed are checked against that series' units
    // by the compiler and projected by that series' own scales -- which is what makes a ribbon
    // on a log axis asymmetric, correctly, and a tile on one the right size.

    let private lengthCheck (s: Series<'ux, 'uy>) (field: string) (n: int) =
        if n <> s.X.Length then
            invalidArg
                field
                (sprintf "Marey: series '%s' has %d points, but '%s' has %d values." s.Name s.X.Length field n)

    /// A filled band between two y series, given as absolute values in the y unit.
    ///
    /// The band alone. `Chart.ribbon` draws the middle line as a second layer, which is the
    /// composition rather than a decision this geom makes.
    let ribbon (s: Series<'ux, 'uy>) (low: float<'uy>[]) (high: float<'uy>[]) : Geom =
        lengthCheck s "low" low.Length
        lengthCheck s "high" high.Length
        GeomRibbon(Array.map s.YScale.Forward low, Array.map s.YScale.Forward high)

    /// A band of `y +/- err`. The signature `errorY` has, and the same guarantee: an
    /// uncertainty in seconds around a displacement does not compile.
    let ribbonAround (s: Series<'ux, 'uy>) (err: float<'uy>[]) : Geom =
        lengthCheck s "err" err.Length

        GeomRibbon(
            Array.init s.Y.Length (fun i -> s.YScale.Forward(s.Y.[i] - err.[i])),
            Array.init s.Y.Length (fun i -> s.YScale.Forward(s.Y.[i] + err.[i]))
        )

    /// Rectangles from opposite corners, in the series' own units. Both measures are checked,
    /// so a rectangle cannot be given its width in the y unit.
    let rect (s: Series<'ux, 'uy>) (x0: float<'ux>[]) (y0: float<'uy>[]) (x1: float<'ux>[]) (y1: float<'uy>[]) : Geom =
        lengthCheck s "x0" x0.Length
        lengthCheck s "y0" y0.Length
        lengthCheck s "x1" x1.Length
        lengthCheck s "y1" y1.Length

        GeomRect(
            Array.map s.XScale.Forward x0,
            Array.map s.YScale.Forward y0,
            Array.map s.XScale.Forward x1,
            Array.map s.YScale.Forward y1
        )

    /// Tiles of one size in the data's own units, centred on the series' points -- the heatmap
    /// geom, and the one a 2-D bin will hand its cells to.
    ///
    /// **Each corner is projected separately** rather than a half-width being converted once,
    /// and that is the whole content of the function. On a log axis a constant width in data
    /// units is not a constant width on the panel; converting once would draw every tile the
    /// size of the one at the centre of the domain, and the tiles at the ends would overlap or
    /// leave gaps that look like missing data.
    let tile (w: float<'ux>) (h: float<'uy>) (s: Series<'ux, 'uy>) : Geom =
        let hw = w * 0.5
        let hh = h * 0.5

        GeomRect(
            s.X |> Array.map (fun x -> s.XScale.Forward(x - hw)),
            s.Y |> Array.map (fun y -> s.YScale.Forward(y - hh)),
            s.X |> Array.map (fun x -> s.XScale.Forward(x + hw)),
            s.Y |> Array.map (fun y -> s.YScale.Forward(y + hh))
        )

    /// An RGBA image filling a box given in the series' own units (D64).
    ///
    /// Both measures are checked, so the box cannot be given its width in the y unit — the same
    /// guarantee `rect` makes, for the same reason and by the same route.
    ///
    /// The box is projected corner by corner rather than as an origin plus a size, which is
    /// D57's correction applied before it can become a bug: on a log axis a size in data units
    /// is not a size on the panel, so an image placed by one corner and a width would be the
    /// right size only in the middle of the domain.
    let image (s: Series<'ux, 'uy>) (img: Image) (x0: float<'ux>) (y0: float<'uy>) (x1: float<'ux>) (y1: float<'uy>) (smooth: bool) : Geom =
        GeomImage(
            img,
            { X0 = s.XScale.Forward x0
              Y0 = s.YScale.Forward y0
              X1 = s.XScale.Forward x1
              Y1 = s.YScale.Forward y1 },
            smooth
        )

    /// A line from each of the series' points to a second point, in the series' own units.
    let segment (s: Series<'ux, 'uy>) (x2: float<'ux>[]) (y2: float<'uy>[]) : Geom =
        lengthCheck s "x2" x2.Length
        lengthCheck s "y2" y2.Length
        GeomSegment(Array.map s.XScale.Forward x2, Array.map s.YScale.Forward y2)

    /// Labels at the series' points, set just above them.
    ///
    /// **The one geom constructor that takes no series**, because a string is not a quantity and
    /// there is no measure to spend. Worth being explicit that this is not a hole in the
    /// pattern: everything else here exists to turn a measured value into `nd` with a scale, and
    /// a label has no value to turn.
    let text (labels: string[]) : Geom =
        GeomText(labels, AnchorMiddle, BaselineAlphabetic, { X = 0.0<px>; Y = 7.0<px> })

    /// Labels with their placement stated. The offset is a **device** length, so a label sits
    /// the same distance off its mark on a tall panel and a wide one.
    let textAt (anchor: TextAnchor) (baseline: TextBaseline) (offset: Vec2<px>) (labels: string[]) : Geom =
        GeomText(labels, anchor, baseline, offset)

    /// Box plots, one per point of the series, from summaries in the **y** unit.
    ///
    /// `Stat.Summary<'uy>` is what carries the guarantee: a summary is entirely in the unit of
    /// the sample it was taken from, so a box plot of metres cannot be drawn against a joule
    /// axis, and the whiskers and the outliers are projected by the same scale as the box.
    let boxplot (widthFraction: float) (s: Series<'ux, 'uy>) (summaries: Stat.Summary<'uy>[]) : Geom =
        lengthCheck s "summaries" summaries.Length

        GeomBox(
            summaries
            |> Array.map (fun q ->
                { Lower = s.YScale.Forward q.LowerWhisker
                  Q1 = s.YScale.Forward q.Q1
                  Median = s.YScale.Forward q.Median
                  Q3 = s.YScale.Forward q.Q3
                  Upper = s.YScale.Forward q.UpperWhisker
                  Outliers = q.Outliers |> Array.map s.YScale.Forward }),
            max 0.05 (min 1.0 widthFraction)
        )

    /// Violins, one per point of the series, from kernel density estimates over the y unit.
    ///
    /// **One normaliser for the whole layer.** The widest point of any profile becomes a full
    /// band and every other width is measured against it, so a sparse group draws a thin violin
    /// and a populous one a fat violin. Normalising each violin to its own peak -- which is what
    /// falls out of doing it inside the map -- makes every group exactly the same width and
    /// throws away the only thing a row of violins is read for.
    ///
    /// The densities' `X` is the *sample* axis and therefore the panel's **y**: a violin is a
    /// density turned on its side, which is why `d.X` goes through `s.YScale`.
    let violin (widthFraction: float) (s: Series<'ux, 'uy>) (densities: Stat.Density<'uy>[]) : Geom =
        lengthCheck s "densities" densities.Length

        let peak =
            densities
            |> Array.fold
                (fun acc d ->
                    d.Y
                    |> Array.fold (fun a v -> if finiteNum (float v) then max a (float v) else a) acc)
                0.0

        let inv = if peak > 0.0 then 1.0 / peak else 0.0

        GeomViolin(
            densities
            |> Array.map (fun d ->
                { At = d.X |> Array.map s.YScale.Forward
                  Width = d.Y |> Array.map (fun v -> float v * inv) }),
            max 0.05 (min 1.0 widthFraction)
        )

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
    let private runsWhere (n: int) (ok: int -> bool) =
        [| let mutable start = -1

           for i in 0 .. n - 1 do
               if ok i then
                   if start < 0 then start <- i
               else if start >= 0 then
                   yield struct (start, i - 1)
                   start <- -1

           if start >= 0 then
               yield struct (start, n - 1) |]

    let private runsOf (pts: Vec2<nd>[]) =
        runsWhere pts.Length (fun i -> finite pts.[i])
        |> Array.map (fun (struct (a, b)) -> pts.[a..b])

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

    /// The body of `compile`, with two parameters `compile` does not expose.
    ///
    /// `spacing` overrides the rhythm a bar's width is taken from. `None` means "the points
    /// you were given", which is right for a whole layer and wrong for one channel batch of
    /// one — three coloured batches of a bar chart are not three bar charts, and each would
    /// otherwise measure its own width from the gaps between its own bars. See `compileSubset`.
    ///
    /// `sel` is the **row each drawn point came from**, or `None` when `pts` is the whole
    /// layer — and it is the correctness half of the same argument. Every geom that carries a
    /// per-point array of its own (an error bar's span, a ribbon's edges, a rectangle's
    /// corners, a label, a box) indexes that array *by row*, while a channel batch hands this
    /// function a subset of the rows renumbered from zero. Without the indirection the second
    /// colour batch of an error bar reads the first batch's spans: every bar is still drawn,
    /// the figure looks entirely reasonable, and every uncertainty belongs to a different
    /// point. Phase 2 introduced that hazard for `GeomErrorY` and `GeomErrorX`; phase 6 would
    /// have introduced it six more times.
    let rec private compileWith
        (address: (Key * int[]) option)
        (spacing: float option)
        (sel: int[] option)
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

            let inline row i =
                match sel with
                | Some s -> s.[i]
                | None -> i

            let inline mapPt (v: Vec2<nd>) =
                if Coord.isIdentity coord then v else coord.ToPanel v

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

            // §5.1 — "a 100 000-point scatter must be *one* IR node". One `Markers`, always —
            // unless a row was **addressed** (D69), and then it is three, which is the whole
            // cost of being able to name a single mark.
            | GeomPoint ->
                // The row travels beside the point because the filter below destroys the
                // correspondence otherwise: a non-finite y in the middle of a series shifts
                // every later mark's position within the batch by one, and a key built from
                // that position names a different datum. Which is D55's defect, and D68 wrote
                // the rule this obeys — a mark's key is its original row.
                let kept =
                    [| for i in 0 .. pts.Length - 1 do
                           if finite pts.[i] then
                               yield row i, (if Coord.isIdentity coord then pts.[i] else coord.ToPanel pts.[i]) |]

                let batch (points: Vec2<nd>[]) =
                    Markers
                        { Points = points
                          Shape = st.Shape
                          Size = st.MarkerSize
                          Fill = Some(Solid(Color.withAlpha (st.Color.A * st.Opacity) st.Color))
                          Stroke = None }

                if kept.Length = 0 then
                    Scene.empty
                else
                    match address with
                    | None -> batch (kept |> Array.map snd)
                    | Some(_, rows) when rows.Length = 0 -> batch (kept |> Array.map snd)
                    | Some(layerKey, rows) ->
                        let wanted = Set.ofArray rows
                        let out = ResizeArray<Node>()
                        let run = ResizeArray<Vec2<nd>>()

                        let flush () =
                            if run.Count > 0 then
                                out.Add(batch (run.ToArray()))
                                run.Clear()

                        for r, p in kept do
                            if wanted.Contains r then
                                flush ()
                                out.Add(Keyed(Keys.mark layerKey r, batch [| p |]))
                            else
                                run.Add p

                        flush ()

                        // One addressed mark in the middle of a scatter is three nodes; one at
                        // either end is two. **One node is one node**: a layer whose addressed
                        // rows were all filtered out, or were never in the data, is the batch it
                        // always was rather than a `Group` around it. Asking for a row that is
                        // not drawn must not change the picture, and a wrapper is a change.
                        if out.Count = 1 then out.[0] else Group(out.ToArray())

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
                           compileWith address spacing sel coord plotSize st GeomLine pts |]

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

                let segs =
                    [| for i in 0 .. pts.Length - 1 do
                           let p = pts.[i]
                           let k = row i

                           if
                               k < low.Length
                               && k < high.Length
                               && finite p
                               && finite (at p.X low.[k])
                               && finite (at p.X high.[k])
                           then
                               let a = at p.X low.[k]
                               let b = at p.X high.[k]

                               yield MoveTo(mapPt a)
                               yield LineTo(mapPt b)
                               yield MoveTo(mapPt (at (p.X - nd capHalf) a.Y))
                               yield LineTo(mapPt (at (p.X + nd capHalf) a.Y))
                               yield MoveTo(mapPt (at (p.X - nd capHalf) b.Y))
                               yield LineTo(mapPt (at (p.X + nd capHalf) b.Y)) |]

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

                let segs =
                    [| for i in 0 .. pts.Length - 1 do
                           let p = pts.[i]
                           let k = row i

                           if
                               k < low.Length
                               && k < high.Length
                               && finite p
                               && finite (at low.[k] p.Y)
                               && finite (at high.[k] p.Y)
                           then
                               let a = at low.[k] p.Y
                               let b = at high.[k] p.Y

                               yield MoveTo(mapPt a)
                               yield LineTo(mapPt b)
                               yield MoveTo(mapPt (at a.X (p.Y - nd capHalf)))
                               yield LineTo(mapPt (at a.X (p.Y + nd capHalf)))
                               yield MoveTo(mapPt (at b.X (p.Y - nd capHalf)))
                               yield LineTo(mapPt (at b.X (p.Y + nd capHalf))) |]

                if segs.Length = 0 then
                    Scene.empty
                else
                    Path(segs, Some { stroke with Cap = Butt }, None, NonZero)

            // ---- Phase 6 -----------------------------------------------------------------
            //
            // Seven geoms, and every one of them still bottoms out in `Path`, `Markers` or
            // `Text`. No `Node` case was added and none was wanted, which is the standing claim
            // of §5.5 tested rather than repeated.

            | GeomStep align ->
                // The staircase is built in data space and then warped, so a step under a polar
                // coordinate is an arc and a radius rather than two chords.
                let staircase (run: Vec2<nd>[]) =
                    [| yield run.[0]

                       for i in 1 .. run.Length - 1 do
                           let a = run.[i - 1]
                           let b = run.[i]

                           match align with
                           | StepAfter -> yield at b.X a.Y
                           | StepBefore -> yield at a.X b.Y
                           | StepMid ->
                               let m = (a.X + b.X) * 0.5
                               yield at m a.Y
                               yield at m b.Y

                           yield b |]

                let segs =
                    [| for run in runsOf pts do
                           let w = warp coord (staircase run)

                           if w.Length > 0 then
                               yield MoveTo w.[0]

                               for i in 1 .. w.Length - 1 do
                                   yield LineTo w.[i] |]

                if segs.Length = 0 then
                    Scene.empty
                else
                    Path(segs, Some stroke, None, NonZero)

            | GeomRibbon(low, high) ->
                // A run is broken by a gap in *either* bound as well as by a gap in x. A band
                // drawn across a missing bound asserts a spread that was never measured, which
                // is the argument `runsOf` already makes for a line with two more arrays in it.
                let ok i =
                    let k = row i

                    k < low.Length
                    && k < high.Length
                    && finite pts.[i]
                    && finiteNum (float low.[k])
                    && finiteNum (float high.[k])

                let segs =
                    [| for struct (a, b) in runsWhere pts.Length ok do
                           if b > a then
                               let top =
                                   warp coord (Array.init (b - a + 1) (fun k -> at pts.[a + k].X high.[row (a + k)]))

                               let bottom =
                                   warp coord (Array.init (b - a + 1) (fun k -> at pts.[b - k].X low.[row (b - k)]))

                               yield MoveTo top.[0]

                               for i in 1 .. top.Length - 1 do
                                   yield LineTo top.[i]

                               for q in bottom do
                                   yield LineTo q

                               yield ClosePath |]

                if segs.Length = 0 then
                    Scene.empty
                else
                    Path(segs, None, Some(fillOf st), NonZero)

            | GeomRect(x0, y0, x1, y1) ->
                let segs =
                    [| for i in 0 .. pts.Length - 1 do
                           let k = row i

                           if k < x0.Length && k < y0.Length && k < x1.Length && k < y1.Length then
                               let a = at x0.[k] y0.[k]
                               let b = at x1.[k] y1.[k]

                               if finite a && finite b then
                                   yield MoveTo(mapPt a)
                                   yield LineTo(mapPt (at b.X a.Y))
                                   yield LineTo(mapPt b)
                                   yield LineTo(mapPt (at a.X b.Y))
                                   yield ClosePath |]

                if segs.Length = 0 then
                    Scene.empty
                else
                    // **The outline is the fill's own paint**, unlike a bar's, and the
                    // difference is the mark rather than the taste. Bars are separated marks
                    // and a darker edge reads as a border; tiles tessellate, so an edge in the
                    // stroke colour draws a grid over a heatmap that nobody asked for. Stroking
                    // in the fill is still needed: without it, adjacent tiles leave a hairline
                    // of background wherever the renderer antialiases a shared edge, and a
                    // reader takes those seams for structure in the data.
                    Path(
                        segs,
                        Some
                            { stroke with
                                Paint = fillOf st
                                Width = stroke.Width * 0.5 },
                        Some(fillOf st),
                        NonZero
                    )

            | GeomSegment(x1s, y1s) ->
                let segs =
                    [| for i in 0 .. pts.Length - 1 do
                           let k = row i

                           if k < x1s.Length && k < y1s.Length then
                               let a = pts.[i]
                               let b = at x1s.[k] y1s.[k]

                               if finite a && finite b then
                                   let w = warp coord [| a; b |]

                                   yield MoveTo w.[0]

                                   for j in 1 .. w.Length - 1 do
                                       yield LineTo w.[j] |]

                if segs.Length = 0 then
                    Scene.empty
                else
                    Path(segs, Some stroke, None, NonZero)

            | GeomText(labels, anchor, baseline, offset) ->
                // The offset is a device length and this function's output is about to be
                // scaled by the panel transform, so it is divided by the panel's own pixel
                // size -- the correction an error bar's cap makes, for the same reason (D12).
                let dx =
                    if plotSize.Width = 0.0<px> then
                        0.0
                    else
                        float (offset.X / plotSize.Width)

                let dy =
                    if plotSize.Height = 0.0<px> then
                        0.0
                    else
                        float (offset.Y / plotSize.Height)

                let paint = Solid(Color.withAlpha (st.Color.A * st.Opacity) st.Color)

                let runs =
                    [| for i in 0 .. pts.Length - 1 do
                           let k = row i

                           if k < labels.Length && not (System.String.IsNullOrEmpty labels.[k]) && finite pts.[i] then
                               let q = mapPt pts.[i]

                               yield
                                   Text
                                       { Text = labels.[k]
                                         Tex = None
                                         At = at (q.X + nd dx) (q.Y + nd dy)
                                         Font = st.Font
                                         Paint = paint
                                         Anchor = anchor
                                         Baseline = baseline
                                         Rotation = 0.0<rad> } |]

                if runs.Length = 0 then Scene.empty else Group runs

            | GeomBox(spans, widthFraction) ->
                let half = (defaultArg spacing (medianSpacing pts)) * widthFraction * 0.5
                let capHalf = half * 0.5

                // Three nodes rather than one, and the split is not cosmetic: a `Path` carrying
                // both a fill and the whisker segments fills the whiskers too. They are
                // degenerate and so fill nothing today, but that is an accident of their being
                // straight, and a reviewer reading a golden cannot see which subpath a fill
                // belongs to.
                let boxes = ResizeArray<PathSeg>()
                let rules = ResizeArray<PathSeg>()
                let outliers = ResizeArray<Vec2<nd>>()

                for i in 0 .. pts.Length - 1 do
                    let k = row i

                    if k < spans.Length && finite pts.[i] then
                        let q = spans.[k]
                        let x = pts.[i].X
                        let x0 = x - nd half
                        let x1 = x + nd half

                        if finiteNum (float q.Q1) && finiteNum (float q.Q3) then
                            boxes.Add(MoveTo(mapPt (at x0 q.Q1)))
                            boxes.Add(LineTo(mapPt (at x1 q.Q1)))
                            boxes.Add(LineTo(mapPt (at x1 q.Q3)))
                            boxes.Add(LineTo(mapPt (at x0 q.Q3)))
                            boxes.Add ClosePath

                        if finiteNum (float q.Median) then
                            rules.Add(MoveTo(mapPt (at x0 q.Median)))
                            rules.Add(LineTo(mapPt (at x1 q.Median)))

                        for (near, far) in [ q.Q3, q.Upper; q.Q1, q.Lower ] do
                            if finiteNum (float near) && finiteNum (float far) then
                                rules.Add(MoveTo(mapPt (at x near)))
                                rules.Add(LineTo(mapPt (at x far)))
                                rules.Add(MoveTo(mapPt (at (x - nd capHalf) far)))
                                rules.Add(LineTo(mapPt (at (x + nd capHalf) far)))

                        for o in q.Outliers do
                            if finiteNum (float o) then
                                outliers.Add(mapPt (at x o))

                Scene.group
                    [ if boxes.Count > 0 then
                          Path(boxes.ToArray(), Some stroke, Some(fillOf st), NonZero)
                      if rules.Count > 0 then
                          Path(rules.ToArray(), Some { stroke with Cap = Butt }, None, NonZero)
                      // Hollow, so an outlier sitting on a whisker cap is still countable -- and
                      // one `Markers` node for every outlier in the layer, because §5.1's rule
                      // does not stop applying just because the points are meant to be rare.
                      if outliers.Count > 0 then
                          Markers
                              { Points = outliers.ToArray()
                                Shape = st.Shape
                                Size = st.MarkerSize
                                Fill = None
                                Stroke = Some { stroke with Width = stroke.Width * 0.75 } } ]

            | GeomImage(image, bounds, smooth) ->
                // The only `Raster` this library produces above the IR (D64).
                //
                // **The pixels are not warped, and that is a limit rather than an oversight.**
                // `RasterNode.Bounds` is an axis-aligned `Rect<nd>` by construction, so under a
                // non-identity coordinate — a polar panel — the box's corners are mapped and
                // their bounding box is used, and the image inside it is *not* resampled along
                // the warp. Doing that honestly needs a resampler, which is a rasteriser inside
                // a zero-dependency package, and D64 rules that out of this milestone by name.
                // Under the identity coord, which is every Cartesian figure and so every figure
                // `Chart.image` is for, this is exact.
                let corners =
                    [| { X = bounds.X0; Y = bounds.Y0 }
                       { X = bounds.X1; Y = bounds.Y0 }
                       { X = bounds.X1; Y = bounds.Y1 }
                       { X = bounds.X0; Y = bounds.Y1 } |]
                    |> Array.map mapPt

                let box: Rect<nd> =
                    { X0 = corners |> Array.map (fun p -> p.X) |> Array.min
                      Y0 = corners |> Array.map (fun p -> p.Y) |> Array.min
                      X1 = corners |> Array.map (fun p -> p.X) |> Array.max
                      Y1 = corners |> Array.map (fun p -> p.Y) |> Array.max }

                if image.Width <= 0 || image.Height <= 0 then
                    // Both backends already say so in their output when handed one, so nothing
                    // is gained by emitting a node whose only effect is a diagnostic comment.
                    Scene.empty
                else
                    Raster
                        { Image = image
                          Bounds = box
                          Smooth = smooth }

            | GeomViolin(profiles, widthFraction) ->
                let half = (defaultArg spacing (medianSpacing pts)) * widthFraction * 0.5

                let segs =
                    [| for i in 0 .. pts.Length - 1 do
                           let k = row i

                           if k < profiles.Length && finite pts.[i] then
                               let prof = profiles.[k]
                               let x = pts.[i].X
                               let n = min prof.At.Length prof.Width.Length

                               let keep =
                                   [| for j in 0 .. n - 1 do
                                          if finiteNum (float prof.At.[j]) && finiteNum prof.Width.[j] then
                                              yield j |]

                               if keep.Length >= 2 then
                                   let side (sign: float) (j: int) =
                                       mapPt (at (x + nd (sign * half * prof.Width.[j])) prof.At.[j])

                                   yield MoveTo(side 1.0 keep.[0])

                                   for j in 1 .. keep.Length - 1 do
                                       yield LineTo(side 1.0 keep.[j])

                                   for j in keep.Length - 1 .. -1 .. 0 do
                                       yield LineTo(side -1.0 keep.[j])

                                   yield ClosePath |]

                if segs.Length = 0 then
                    Scene.empty
                else
                    Path(segs, Some { stroke with Width = stroke.Width * 0.75 }, Some(fillOf st), NonZero)

    /// **The one compile**, with the two things a whole unaddressed layer does not have.
    ///
    /// `plotSize` is the plot area's device size, and it is here for exactly one reason: an error
    /// bar's cap is `px` wide (D12) while this function's output is about to be scaled by the
    /// panel transform. Dividing by the panel's own pixel size is what makes a 3 px cap 3 px
    /// wide after that scaling, on both a wide panel and a tall one.
    ///
    /// `indices` is D46's **channel batch**: the rows this call draws, out of `all`. `None`
    /// draws the whole layer, which is not the same thing as `Some [| 0 .. n - 1 |]` — see the
    /// spacing note below.
    ///
    /// `address` is D69's **named rows**, with the layer key their names hang from. Only
    /// `GeomPoint` can honour it, because `Markers` is the only node in this file that batches
    /// N data points into one; a `Path` is one shape, not a batch of marks, and splitting it in
    /// three would change the joins between the pieces rather than name one of them.
    ///
    /// `compile` and `compileSubset` are this function with one or both of them absent. They
    /// are two names for one body rather than two bodies, which is the shape D72 arrived at the
    /// hard way in `Figure.fs`.
    let compileFor
        (address: (Key * int[]) option)
        (indices: int[] option)
        (coord: Coord)
        (plotSize: Extent<px>)
        (st: GeomStyle)
        (geom: Geom)
        (all: Vec2<nd>[])
        : Node =
        match indices with
        | None -> compileWith address None None coord plotSize st geom all
        | Some ix ->
            // Only a bar reads the layer's rhythm, and reading it is a sort of every gap in the
            // series. Computing it for a geom that will not look at it would put that sort inside
            // the per-batch loop, which is 256 sorts of 100 000 gaps for one continuous colour
            // channel — measured at 1.5 s against 4 ms.
            let spacing =
                match geom with
                | GeomBar _
                | GeomBox _
                | GeomViolin _ -> Some(medianSpacing all)
                | GeomLine
                | GeomPoint
                | GeomArea _
                | GeomErrorY _
                | GeomErrorX _
                | GeomStep _
                | GeomRibbon _
                | GeomRect _
                | GeomSegment _
                | GeomText _
                // An image occupies a stated box and has no rhythm to read.
                | GeomImage _ -> None

            compileWith address spacing (Some ix) coord plotSize st geom (ix |> Array.map (fun i -> all.[i]))

    /// A whole layer, nothing addressed. The path every static figure takes.
    let compile (coord: Coord) (plotSize: Extent<px>) (st: GeomStyle) (geom: Geom) (pts: Vec2<nd>[]) : Node =
        compileFor None None coord plotSize st geom pts

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
        compileFor None (Some indices) coord plotSize st geom all

    /// The legend swatch that matches a geom. Derived, never chosen separately.
    let legendMark (geom: Geom) (st: GeomStyle) : LegendMark =
        match geom with
        | GeomLine -> LegendLine st.Dash
        | GeomPoint -> LegendMarker st.Shape
        | GeomStep _
        | GeomSegment _ -> LegendLine st.Dash
        | GeomArea _
        | GeomBar _
        | GeomRibbon _
        | GeomRect _
        | GeomBox _
        | GeomViolin _ -> LegendSwatch
        // A text layer's mark is its colour and nothing else: the labels *are* the data, so a
        // line or a marker in the legend would be a swatch for a shape the layer never draws.
        | GeomText _ -> LegendSwatch
        | GeomErrorY _
        | GeomErrorX _ -> LegendLineMarker(st.Dash, st.Shape)
        // An image carries no series colour, so a swatch of the layer's paint would name a
        // colour that appears nowhere in what was drawn. `Chart.image` gives its layer no name,
        // and an unnamed layer gets no legend entry, so this is reached only by a caller who
        // named one deliberately — and a swatch is then the least wrong of the four marks.
        | GeomImage _ -> LegendSwatch
