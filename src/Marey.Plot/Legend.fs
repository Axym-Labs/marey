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

[<NoComparison>]
type LegendEntry =
    { Name: string
      Color: Color
      Mark: LegendMark }

/// Where the legend sits. All four corners of the plot area, plus off.
///
/// Outside placement (a column of its own in the figure grid) is a `Track` away and deliberately
/// not in M0: it changes the panel's width, which means the tick plan has to be recomputed
/// against it, and that loop is already running twice for the axes.
type LegendPlacement =
    | LegendNone
    | LegendTopRight
    | LegendTopLeft
    | LegendBottomRight
    | LegendBottomLeft

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

    /// Height of one row: the taller of the type and the swatch.
    let private rowHeight (m: ITextMeasurer) (theme: Theme) (e: LegendEntry) =
        let b =
            TextMeasure.bounds m theme.LegendFont AnchorStart BaselineMiddle 0.0<rad> e.Name

        max (Rect.height b) theme.MarkerSize

    /// The box the legend needs, in device px.
    let measure (m: ITextMeasurer) (theme: Theme) (entries: LegendEntry[]) : Extent<px> =
        if entries.Length = 0 then
            { Width = 0.0<px>; Height = 0.0<px> }
        else
            let textW =
                entries
                |> Array.map (fun e ->
                    let b =
                        TextMeasure.bounds m theme.LegendFont AnchorStart BaselineMiddle 0.0<rad> e.Name

                    Rect.width b)
                |> Array.max

            let rows = entries |> Array.map (rowHeight m theme)

            { Width = 2.0 * theme.LegendPadding + theme.LegendSwatchWidth + theme.LabelGap * 2.0 + textW
              Height = 2.0 * theme.LegendPadding + Array.sum rows + theme.LegendItemGap * float (entries.Length - 1) }

    /// Position the box inside a plot area, in device px.
    let private box (theme: Theme) (plot: Rect<px>) (size: Extent<px>) (placement: LegendPlacement) : Rect<px> =
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

    /// Compile the legend for a plot area given in **figure nd**.
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
            let b = box theme plotPx size placement

            let pt (x: float<px>) (y: float<px>) : Vec2<nd> = Layout.ndPoint extent { X = x; Y = y }

            let frame =
                let r: Rect<nd> =
                    Layout.ndRect extent b

                let stroke =
                    if theme.ShowLegendFrame then
                        Some(Stroke.solid theme.LegendBorder (theme.AxisWidth * 0.75))
                    else
                        None

                Scene.rect stroke (Some(Solid theme.LegendBackground)) r

            let rows = entries |> Array.map (rowHeight m theme)

            let swatchX0 = b.X0 + theme.LegendPadding
            let swatchX1 = swatchX0 + theme.LegendSwatchWidth
            let textX = swatchX1 + theme.LabelGap * 2.0

            let mutable y = b.Y0 + theme.LegendPadding

            let items =
                [| for i in 0 .. entries.Length - 1 do
                       let e = entries.[i]
                       let h = rows.[i]
                       let cy = y + h / 2.0
                       let mid = (swatchX0 + swatchX1) / 2.0

                       let stroke =
                           { Stroke.solid e.Color theme.SeriesWidth with
                               Cap = Round }

                       let lineOf (dash: float<px>[]) =
                           Path([| MoveTo(pt swatchX0 cy); LineTo(pt swatchX1 cy) |], Some { stroke with Dash = dash }, None, NonZero)

                       let markerOf (shape: MarkerShape) =
                           Markers
                               { Points = [| pt mid cy |]
                                 Shape = shape
                                 Size = theme.MarkerSize
                                 Fill = Some(Solid e.Color)
                                 Stroke = None }

                       let swatch =
                           match e.Mark with
                           | LegendLine dash -> lineOf dash
                           | LegendMarker shape -> markerOf shape
                           | LegendLineMarker(dash, shape) -> Group [| lineOf dash; markerOf shape |]
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

                       y <- y + h + theme.LegendItemGap |]

            Annotated(
                { Role = RoleLegend
                  Label = "legend"
                  Data = [| "entries", string entries.Length |] },
                Group(Array.append [| frame |] items)
            )
