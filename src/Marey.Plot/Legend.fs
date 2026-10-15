namespace Marey

/// What a legend entry's swatch looks like. Derived from the geom, not chosen separately: a
/// legend that does not look like the thing it labels is worse than no legend.
[<NoComparison>]
type LegendMark =
    | LegendLine of dash: float<px>[]
    | LegendMarker of MarkerShape
    | LegendLineMarker of dash: float<px>[] * shape: MarkerShape
    /// A filled rectangle: areas and bars.
    | LegendSwatch
    /// D49 — a marker drawn at a stated diameter rather than the theme's: the size key of a
    /// continuous size channel, three of them at the domain's ends and middle.
    ///
    /// **A new case here is cheap in a way an IR case is not**, which is the whole of D11's
    /// "one variant against six backends" read the other way. `LegendMark` is a `Marey.Plot`
    /// type with exactly one consumer — the `match` inside `Legend.compile` — and it is
    /// compiled into `Path`/`Markers` before any backend sees it. So the size key is a case
    /// and the colorbar is a sibling compiler: the question is never whether a new case is
    /// allowed, it is what the case costs the thing it lives in.
    | LegendSizedMarker of shape: MarkerShape * size: float<px>

[<NoComparison>]
type LegendEntry =
    { Name: string
      Color: Color
      Mark: LegendMark }

/// Where the legend sits: a corner of the plot area, outside it, the emptiest corner, or off.
///
/// Outside placement waited from M0 to M8 (D171) because it changes the panel's width and the tick
/// plan is measured against that width. D49's colorbar showed the way round: a column of the
/// panel's own grid, sized before the ticks are planned, so the loop is still two passes.
type LegendPlacement =
    | LegendNone
    | LegendTopRight
    | LegendTopLeft
    | LegendBottomRight
    | LegendBottomLeft
    /// D171 — a column of its own to the right of the plot (and of a colorbar).
    | LegendOutsideRight
    /// D171 — a row of its own below the x axis's title.
    | LegendOutsideBottom
    /// D171 — the inside corner whose box covers the fewest drawn marks, or outside right when
    /// every corner is covered. Decided once for an animation, over all its frames, so the legend
    /// does not jump.
    | LegendBest

/// D171 — the legend shared by every panel of a figure (`Chart.withFigureLegend`).
type FigureLegend =
    | FigureLegendRight
    | FigureLegendBottom

/// §5.5 — a legend is not an IR node either. It is a `Path` for the box, a `Path` or `Markers`
/// per swatch, and a `Text` per entry, wrapped in `Annotated` with `RoleLegend` so that a later
/// milestone can make it clickable without the scene having to be rebuilt to carry the
/// information (§5.3: "retrofitting this is painful; it costs almost nothing now").
///
/// Sizing goes through `ITextMeasurer`, like everything else that has to know how wide a string
/// is. §15's warning that "legend layout … is where plotting libraries actually die" is the
/// reason the box is measured rather than estimated from a character count.
[<RequireQualifiedAccess>]
module Legend =

    /// The drawn size of a swatch: the theme's, unless the entry states one.
    let private swatchSize (theme: Theme) (e: LegendEntry) =
        match e.Mark with
        | LegendSizedMarker(_, size) -> size
        | _ -> theme.MarkerSize

    /// Height of one row: the taller of the type and the swatch.
    let private rowHeight (m: ITextMeasurer) (theme: Theme) (e: LegendEntry) =
        let b =
            TextMeasure.bounds m theme.LegendFont AnchorStart BaselineMiddle 0.0<rad> e.Name

        max (Rect.height b) (swatchSize theme e)

    /// The swatch column's width. A sized marker can be wider than the theme's swatch, and a
    /// key whose largest mark is clipped by its own box is worse than no key.
    let private swatchWidth (theme: Theme) (entries: LegendEntry[]) =
        entries
        |> Array.fold (fun acc e -> max acc (swatchSize theme e)) theme.LegendSwatchWidth

    /// D171 — the entries split into `columns` columns, filled down then across.
    let private columnsOf (columns: int) (entries: LegendEntry[]) : LegendEntry[][] =
        let columns = max 1 (min columns entries.Length)
        let perColumn = (entries.Length + columns - 1) / columns
        entries |> Array.chunkBySize (max 1 perColumn)

    let private textWidth (m: ITextMeasurer) (theme: Theme) (e: LegendEntry) =
        Rect.width (TextMeasure.bounds m theme.LegendFont AnchorStart BaselineMiddle 0.0<rad> e.Name)

    /// One column's width: its swatches, the gap, and its widest name.
    let private columnWidth (m: ITextMeasurer) (theme: Theme) (all: LegendEntry[]) (column: LegendEntry[]) =
        swatchWidth theme all + theme.LabelGap * 2.0 + (column |> Array.map (textWidth m theme) |> Array.max)

    /// The box the legend needs, in device px, in `columns` columns.
    let measureIn (columns: int) (m: ITextMeasurer) (theme: Theme) (entries: LegendEntry[]) : Extent<px> =
        if entries.Length = 0 then
            { Width = 0.0<px>; Height = 0.0<px> }
        else
            let cols = columnsOf columns entries
            let swatchW = swatchWidth theme entries

            // Summed in the order one column always was, so a one-column legend measures to the bit
            // what it did before columns existed, and no baseline moves for it.
            let mutable width = 2.0 * theme.LegendPadding

            for i in 0 .. cols.Length - 1 do
                if i > 0 then
                    width <- width + theme.LegendPadding

                width <- width + swatchW + theme.LabelGap * 2.0 + (cols.[i] |> Array.map (textWidth m theme) |> Array.max)

            let height (column: LegendEntry[]) =
                2.0 * theme.LegendPadding + Array.sum (column |> Array.map (rowHeight m theme)) + theme.LegendItemGap * float (column.Length - 1)

            { Width = width
              Height = cols |> Array.map height |> Array.max }

    /// The box the legend needs, in device px.
    let measure (m: ITextMeasurer) (theme: Theme) (entries: LegendEntry[]) : Extent<px> = measureIn 1 m theme entries

    /// Position the box inside a plot area, in device px.
    let cornerBox (theme: Theme) (plot: Rect<px>) (size: Extent<px>) (placement: LegendPlacement) : Rect<px> =
        let inset = theme.LegendPadding

        let x0 =
            match placement with
            | LegendTopLeft
            | LegendBottomLeft -> plot.X0 + inset
            | _ -> plot.X1 - inset - size.Width

        let y0 =
            match placement with
            | LegendBottomLeft
            | LegendBottomRight -> plot.Y1 - inset - size.Height
            | _ -> plot.Y0 + inset

        { X0 = x0
          Y0 = y0
          X1 = x0 + size.Width
          Y1 = y0 + size.Height }

    /// D171 — compile the legend into the box `b`, in device px, in `columns` columns, with its
    /// frame drawn or not. The corner placements and the outside ones are both this, at a box
    /// chosen for them.
    let compileIn
        (m: ITextMeasurer)
        (theme: Theme)
        (extent: Extent<px>)
        (b: Rect<px>)
        (columns: int)
        (framed: bool)
        (entries: LegendEntry[])
        : Node =
        if entries.Length = 0 then
            Scene.empty
        else
            let pt (x: float<px>) (y: float<px>) : Vec2<nd> = Layout.ndPoint extent { X = x; Y = y }

            let frame =
                let r: Rect<nd> =
                    Layout.ndRect extent b

                let stroke =
                    if framed then
                        Some(Stroke.solid theme.LegendBorder (theme.AxisWidth * 0.75))
                    else
                        None

                Scene.rect stroke (Some(Solid theme.LegendBackground)) r

            let cols = columnsOf columns entries
            let mutable x0 = b.X0 + theme.LegendPadding

            let items =
                [| for column in cols do
                       let swatchX0 = x0
                       let swatchX1 = swatchX0 + swatchWidth theme entries
                       let textX = swatchX1 + theme.LabelGap * 2.0
                       let mutable y = b.Y0 + theme.LegendPadding

                       for e in column do
                           let h = rowHeight m theme e
                           let cy = y + h / 2.0
                           let mid = (swatchX0 + swatchX1) / 2.0

                           let stroke =
                               { Stroke.solid e.Color theme.SeriesWidth with
                                   Cap = Round }

                           let lineOf (dash: float<px>[]) =
                               Path([| MoveTo(pt swatchX0 cy); LineTo(pt swatchX1 cy) |], Some { stroke with Dash = dash }, None, NonZero)

                           let markerAt (shape: MarkerShape) (size: float<px>) =
                               Markers
                                   { Points = [| pt mid cy |]
                                     Shape = shape
                                     Size = size
                                     Fill = Some(Solid e.Color)
                                     Stroke = None }

                           let markerOf (shape: MarkerShape) = markerAt shape theme.MarkerSize

                           let swatch =
                               match e.Mark with
                               | LegendLine dash -> lineOf dash
                               | LegendMarker shape -> markerOf shape
                               | LegendLineMarker(dash, shape) -> Group [| lineOf dash; markerOf shape |]
                               | LegendSizedMarker(shape, size) -> markerAt shape size
                               | LegendSwatch ->
                                   let r: Rect<nd> =
                                       Layout.ndRect
                                           extent
                                           { X0 = swatchX0
                                             Y0 = cy - h * 0.3
                                             X1 = swatchX1
                                             Y1 = cy + h * 0.3 }

                                   Scene.rect None (Some(Solid e.Color)) r

                           let label =
                               Text
                                   { Text = e.Name
                                     // Caller-supplied series name; a TeX backend escapes it. See
                                     // the note in Figure.fs on why only axis titles carry a `Tex`.
                                     Tex = None
                                     At = pt textX cy
                                     Font = theme.LegendFont
                                     Paint = Solid theme.Foreground
                                     Anchor = AnchorStart
                                     Baseline = BaselineMiddle
                                     Rotation = 0.0<rad> }

                           yield
                               Annotated(
                                   { Role = RoleOther "legend-entry"
                                     Label = e.Name
                                     Data = [| "series", e.Name; "color", Color.toHex e.Color |] },
                                   Group [| swatch; label |]
                               )

                           y <- y + h + theme.LegendItemGap

                       x0 <- x0 + columnWidth m theme entries column + theme.LegendPadding |]

            Annotated(
                { Role = RoleLegend
                  Label = "legend"
                  Data = [| "entries", string entries.Length |] },
                Group(Array.append [| frame |] items)
            )

    /// Compile the legend for a plot area given in **figure nd**, in one of its corners.
    let compile
        (m: ITextMeasurer)
        (theme: Theme)
        (extent: Extent<px>)
        (plot: Rect<nd>)
        (placement: LegendPlacement)
        (entries: LegendEntry[])
        : Node =
        if placement = LegendNone || entries.Length = 0 then
            Scene.empty
        else
            let size = measure m theme entries
            let plotPx = Layout.pxRect extent plot
            compileIn m theme extent (cornerBox theme plotPx size placement) 1 theme.ShowLegendFrame entries
