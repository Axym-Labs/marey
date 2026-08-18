namespace Marey

/// One row or column of a `Grid`.
///
/// Three kinds, because those are the three answers to "how big is this?": *you tell me*,
/// *whatever is left*, and *ask the content*. Facets, colourbars, shared axes and inset axes all
/// decompose into those three, and ad-hoc layout does not (§9).
[<RequireQualifiedAccess>]
type Track =
    /// Exactly this many device pixels, regardless of content.
    | Fixed of size: float<px>
    /// A share of whatever is left after the fixed and content tracks have taken theirs.
    /// Weights are relative: two `Frac 1.0` columns split the remainder evenly.
    | Frac of weight: float
    /// Sized to the largest measurement any cell in this track reported, floored at `minimum`.
    /// **This is the track that makes layout two passes.**
    | Content of minimum: float<px>

/// A cell in the grid.
///
/// `Measure` is the measure half of measure–arrange. It is a function, not data, and that is
/// deliberate: a cell's demand is "how wide is this tick label in *this* backend's font", which
/// is exactly the question `ITextMeasurer` answers and exactly the question the layout layer
/// cannot answer for itself (§9, D8). It is called **once per arrange**, before any track size
/// is known.
///
/// Layout values never enter the IR and are never serialized, so the function field costs
/// nothing here that D1 was protecting against in `Node`.
[<NoComparison>]
type GridCell =
    { Key: string
      Row: int
      Col: int
      RowSpan: int
      ColSpan: int
      Measure: ITextMeasurer -> Extent<px> }

/// A constraint grid, à la Makie's `GridLayout` (§9).
[<NoComparison>]
type Grid =
    { Rows: Track[]
      Cols: Track[]
      RowGap: float<px>
      ColGap: float<px>
      PadLeft: float<px>
      PadTop: float<px>
      PadRight: float<px>
      PadBottom: float<px>
      Cells: GridCell[] }

/// The result of arranging a grid.
///
/// Two coordinate systems, on purpose:
///
/// * `Placements` is in **device px**, origin top-left, **y down**. That is the space text
///   metrics arrive in and the space rows are naturally ordered in — row 0 is at the top,
///   because that is what everybody means by row 0.
/// * `Regions` is the same rectangles in **figure `nd`**, origin bottom-left, **y up**, which is
///   what the IR speaks (Affine.fs: "the y axis is *not* flipped: `nd` is y-up like the data").
///
/// The flip between them happens in exactly one function, `Layout.ndRect`, for the same reason
/// the SVG backend keeps its own flip in exactly one place.
[<NoComparison>]
type Arrangement =
    { /// The device-space box this grid was arranged into.
      Region: Rect<px>
      /// The figure surface. Needed for every px to nd conversion — D12.
      Extent: Extent<px>
      ColSizes: float<px>[]
      RowSizes: float<px>[]
      Placements: (string * Rect<px>)[]
      Regions: (string * Rect<nd>)[] }

/// §9 — **"Layout is a measure–arrange pass against `IBackend.MeasureText`."**
///
/// ### The device-unit convention, stated once (D12)
///
/// Positions in the IR are `nd`. Stroke widths, font sizes, tick lengths and paddings are device
/// units, because a hairline must stay a hairline at 4K. Layout is the only layer that holds
/// both, so it is the only layer that may convert between them, and converting requires the
/// figure's `Extent(px)`:
///
/// ```text
///     nd.x = px.x / extent.Width
///     nd.y = 1 - px.y / extent.Height        // and the y axis flips
/// ```
///
/// The conversion is **anisotropic**: a 10 px gap is a different `nd` distance horizontally and
/// vertically on any non-square figure. `ndX` and `ndY` are therefore separate functions and
/// there is deliberately no `nd` scalar conversion that does not say which axis it means. Every
/// `px` quantity that becomes an offset in the IR goes through one of them.
///
/// ### Why two passes and not one
///
/// A y-axis gutter is as wide as its widest tick label. How wide that is depends on the font,
/// which depends on the backend. So the grid cannot be solved until the content has been
/// measured, and the content cannot be measured by the grid. Hence `Track.Content`, hence
/// `GridCell.Measure`, hence two passes.
///
/// The axis compiler runs the whole thing *twice* on top of that, which is a different loop and
/// is documented at `Figure.toScene`: the tick *plan* (how many labels survive, at what
/// rotation) depends on the panel width, which depends on the arrangement.
[<RequireQualifiedAccess>]
module Layout =

    // ---- px <-> nd, the D12 boundary ---------------------------------------------------

    /// A horizontal device distance as a fraction of the figure width.
    let ndX (e: Extent<px>) (x: float<px>) : float<nd> =
        if e.Width = 0.0<px> then 0.0<nd> else x / e.Width * 1.0<nd>

    /// A vertical device distance as a fraction of the figure height.
    let ndY (e: Extent<px>) (y: float<px>) : float<nd> =
        if e.Height = 0.0<px> then 0.0<nd> else y / e.Height * 1.0<nd>

    let pxX (e: Extent<px>) (x: float<nd>) : float<px> = x / 1.0<nd> * e.Width
    let pxY (e: Extent<px>) (y: float<nd>) : float<px> = y / 1.0<nd> * e.Height

    /// **The one place the y axis flips.** Device px (top-left origin, y down) to figure nd
    /// (bottom-left origin, y up).
    let ndRect (e: Extent<px>) (r: Rect<px>) : Rect<nd> =
        { X0 = ndX e r.X0
          Y0 = 1.0<nd> - ndY e r.Y1
          X1 = ndX e r.X1
          Y1 = 1.0<nd> - ndY e r.Y0 }

    /// A device point (top-left origin, y down) as a panel-space point (bottom-left, y up).
    let ndPoint (e: Extent<px>) (p: Vec2<px>) : Vec2<nd> =
        { X = ndX e p.X
          Y = 1.0<nd> - ndY e p.Y }

    let pxRect (e: Extent<px>) (r: Rect<nd>) : Rect<px> =
        { X0 = pxX e r.X0
          Y0 = pxY e (1.0<nd> - r.Y1)
          X1 = pxX e r.X1
          Y1 = pxY e (1.0<nd> - r.Y0) }

    // ---- Construction ------------------------------------------------------------------

    let cell (key: string) (row: int) (col: int) (measure: ITextMeasurer -> Extent<px>) =
        { Key = key
          Row = row
          Col = col
          RowSpan = 1
          ColSpan = 1
          Measure = measure }

    /// A cell that demands nothing. The common case: a plot area takes whatever the `Frac` track
    /// gives it.
    let fill (key: string) (row: int) (col: int) =
        cell key row col (fun _ -> { Width = 0.0<px>; Height = 0.0<px> })

    let spanning (rowSpan: int) (colSpan: int) (c: GridCell) =
        { c with
            RowSpan = max 1 rowSpan
            ColSpan = max 1 colSpan }

    let grid (rows: Track[]) (cols: Track[]) (cells: GridCell[]) =
        { Rows = rows
          Cols = cols
          RowGap = 0.0<px>
          ColGap = 0.0<px>
          PadLeft = 0.0<px>
          PadTop = 0.0<px>
          PadRight = 0.0<px>
          PadBottom = 0.0<px>
          Cells = cells }

    let withGaps (rowGap: float<px>) (colGap: float<px>) (g: Grid) = { g with RowGap = rowGap; ColGap = colGap }

    let withPadding (p: float<px>) (g: Grid) =
        { g with
            PadLeft = p
            PadTop = p
            PadRight = p
            PadBottom = p }

    // ---- The solver --------------------------------------------------------------------

    /// Resolve one axis of tracks against the space available.
    ///
    /// Overflow is handled rather than ignored: if the fixed and content tracks together exceed
    /// the space, content tracks shrink proportionally, and only if that is not enough do fixed
    /// tracks shrink too. A negative track size would put a label outside the figure, which is
    /// one of the two layout invariants §13 asks to be testable.
    let private resolveTracks (tracks: Track[]) (gap: float<px>) (available: float<px>) (demand: float<px>[]) : float<px>[] =
        let n = tracks.Length

        if n = 0 then
            [||]
        else
            let space = available - gap * float (n - 1)

            let basis =
                Array.init n (fun i ->
                    match tracks.[i] with
                    | Track.Fixed s -> max 0.0<px> s
                    | Track.Content m -> max (max 0.0<px> m) demand.[i]
                    | Track.Frac _ -> 0.0<px>)

            let sumOf (pick: int -> bool) =
                let mutable t = 0.0<px>

                for i in 0 .. n - 1 do
                    if pick i then t <- t + basis.[i]

                t

            let isContent i =
                match tracks.[i] with
                | Track.Content _ -> true
                | _ -> false

            let isFixed i =
                match tracks.[i] with
                | Track.Fixed _ -> true
                | _ -> false

            let rigid = sumOf (fun i -> isContent i || isFixed i)

            if rigid > space then
                // Shrink content first, fixed only if that is still not enough.
                let contentTotal = sumOf isContent
                let fixedTotal = sumOf isFixed

                if fixedTotal >= space then
                    let k = if fixedTotal = 0.0<px> then 0.0 else float (space / fixedTotal)

                    Array.init n (fun i -> if isFixed i then basis.[i] * k else 0.0<px>)
                else
                    let room = space - fixedTotal
                    let k = if contentTotal = 0.0<px> then 0.0 else float (room / contentTotal)

                    Array.init n (fun i ->
                        if isContent i then basis.[i] * k
                        elif isFixed i then basis.[i]
                        else 0.0<px>)
            else
                let leftover = space - rigid

                let weights =
                    Array.init n (fun i ->
                        match tracks.[i] with
                        | Track.Frac w -> max 0.0 w
                        | _ -> 0.0)

                let totalWeight = Array.sum weights

                if totalWeight <= 0.0 then
                    basis
                else
                    Array.init n (fun i ->
                        if weights.[i] > 0.0 then
                            leftover * (weights.[i] / totalWeight)
                        else
                            basis.[i])

    /// Content demand per track, from the measured cells.
    ///
    /// Single-span cells set their own track's demand directly. A spanning cell that wants more
    /// than its tracks already provide has the deficit spread over the `Content` tracks it
    /// covers — and if it covers none, nothing happens, because a `Frac` track will absorb it.
    let private demandOf (count: int) (tracks: Track[]) (gap: float<px>) (spans: (int * int * float<px>)[]) =
        let demand = Array.create count 0.0<px>

        for (start, span, want) in spans do
            if span = 1 && start >= 0 && start < count then
                if want > demand.[start] then
                    demand.[start] <- want

        for (start, span, want) in spans do
            if span > 1 && start >= 0 && start + span <= count then
                let mutable have = gap * float (span - 1)

                for i in start .. start + span - 1 do
                    have <-
                        have
                        + (match tracks.[i] with
                           | Track.Fixed s -> s
                           | Track.Content m -> max m demand.[i]
                           | Track.Frac _ -> 0.0<px>)

                if want > have then
                    let contentIdx =
                        [| for i in start .. start + span - 1 do
                               match tracks.[i] with
                               | Track.Content _ -> yield i
                               | _ -> () |]

                    if contentIdx.Length > 0 then
                        let share = (want - have) / float contentIdx.Length

                        for i in contentIdx do
                            demand.[i] <- demand.[i] + share

        demand

    /// Arrange a grid into a device-space region of a figure.
    ///
    /// `extent` is the whole figure and is what the `nd` conversion divides by; `region` is the
    /// box this particular grid gets. They differ whenever a grid is nested inside another —
    /// which is the normal case, since a figure is a grid of panels and each panel is a grid of
    /// gutters.
    let arrangeIn (m: ITextMeasurer) (extent: Extent<px>) (region: Rect<px>) (g: Grid) : Arrangement =
        // ---- pass 1: measure. Exactly one call per cell.
        let measured = g.Cells |> Array.map (fun c -> c.Measure m)

        let colSpans =
            Array.init g.Cells.Length (fun i -> g.Cells.[i].Col, g.Cells.[i].ColSpan, measured.[i].Width)

        let rowSpans =
            Array.init g.Cells.Length (fun i -> g.Cells.[i].Row, g.Cells.[i].RowSpan, measured.[i].Height)

        let colDemand = demandOf g.Cols.Length g.Cols g.ColGap colSpans
        let rowDemand = demandOf g.Rows.Length g.Rows g.RowGap rowSpans

        // ---- pass 2: arrange.
        let innerW = max 0.0<px> (Rect.width region - g.PadLeft - g.PadRight)
        let innerH = max 0.0<px> (Rect.height region - g.PadTop - g.PadBottom)

        let colSizes = resolveTracks g.Cols g.ColGap innerW colDemand
        let rowSizes = resolveTracks g.Rows g.RowGap innerH rowDemand

        let colStart = Array.zeroCreate (g.Cols.Length + 1)
        let mutable x = region.X0 + g.PadLeft

        for i in 0 .. g.Cols.Length - 1 do
            colStart.[i] <- x
            x <- x + colSizes.[i] + g.ColGap

        colStart.[g.Cols.Length] <- x

        let rowStart = Array.zeroCreate (g.Rows.Length + 1)
        let mutable y = region.Y0 + g.PadTop

        for i in 0 .. g.Rows.Length - 1 do
            rowStart.[i] <- y
            y <- y + rowSizes.[i] + g.RowGap

        rowStart.[g.Rows.Length] <- y

        let place (c: GridCell) : Rect<px> =
            let c0 = max 0 (min (g.Cols.Length - 1) c.Col)
            let c1 = max c0 (min (g.Cols.Length - 1) (c.Col + c.ColSpan - 1))
            let r0 = max 0 (min (g.Rows.Length - 1) c.Row)
            let r1 = max r0 (min (g.Rows.Length - 1) (c.Row + c.RowSpan - 1))

            { X0 = colStart.[c0]
              Y0 = rowStart.[r0]
              X1 = colStart.[c1] + colSizes.[c1]
              Y1 = rowStart.[r1] + rowSizes.[r1] }

        let placements = g.Cells |> Array.map (fun c -> c.Key, place c)

        { Region = region
          Extent = extent
          ColSizes = colSizes
          RowSizes = rowSizes
          Placements = placements
          Regions = placements |> Array.map (fun (k, r) -> k, ndRect extent r) }

    /// Arrange a grid over the whole figure.
    let arrange (m: ITextMeasurer) (extent: Extent<px>) (g: Grid) : Arrangement =
        arrangeIn
            m
            extent
            { X0 = 0.0<px>
              Y0 = 0.0<px>
              X1 = extent.Width
              Y1 = extent.Height }
            g

    // ---- Reading the result ---------------------------------------------------------------

    let tryFindPx (key: string) (a: Arrangement) =
        a.Placements
        |> Array.tryPick (fun (k, r) -> if k = key then Some r else None)

    let findPx (key: string) (a: Arrangement) =
        match tryFindPx key a with
        | Some r -> r
        | None -> failwithf "Marey.Layout: no cell named '%s' in this arrangement." key

    let tryFind (key: string) (a: Arrangement) =
        a.Regions |> Array.tryPick (fun (k, r) -> if k = key then Some r else None)

    let find (key: string) (a: Arrangement) =
        match tryFind key a with
        | Some r -> r
        | None -> failwithf "Marey.Layout: no cell named '%s' in this arrangement." key

    // ---- Invariants (§13: "layout invariants — no overlapping panels, labels inside bounds")

    /// Pairs of cells whose device rectangles overlap. Cells that share a track legitimately
    /// touch, so a shared edge is not an overlap; the test is strict interior intersection,
    /// which is what `Rect.intersects` already computes.
    let overlaps (a: Arrangement) : (string * string)[] =
        [| for i in 0 .. a.Placements.Length - 1 do
               for j in i + 1 .. a.Placements.Length - 1 do
                   let (ka, ra) = a.Placements.[i]
                   let (kb, rb) = a.Placements.[j]

                   if Rect.intersects ra rb then
                       yield ka, kb |]

    /// True when every cell sits inside the region it was arranged into, to within `tolerance`
    /// device pixels.
    let withinRegion (tolerance: float<px>) (a: Arrangement) =
        a.Placements
        |> Array.forall (fun (_, r) ->
            r.X0 >= a.Region.X0 - tolerance
            && r.Y0 >= a.Region.Y0 - tolerance
            && r.X1 <= a.Region.X1 + tolerance
            && r.Y1 <= a.Region.Y1 + tolerance)
