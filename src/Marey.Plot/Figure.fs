namespace Marey

/// One drawn thing: data, a geom, and a style.
///
/// `Data` is a `PackedSeries` — a rank-2 existential — which is what lets a `Figure` hold a
/// metres-against-seconds panel beside a joules-against-kelvin one and still be a single value.
[<NoComparison>]
type Layer =
    { Name: string
      Data: PackedSeries
      Geom: Geom
      Style: GeomStyle
      /// The non-positional channels driving this layer: colour, size, shape.
      ///
      /// They live on the layer rather than being resolved into styles at construction time
      /// because two things downstream need them *unresolved*. The legend needs the level
      /// names, and the colorbar needs the scale with its unit still attached — and both are
      /// compiled at figure-compile time, not when the caller says `colorByKey`.
      ///
      /// Empty is the overwhelmingly common case and costs one array comparison.
      Channels: Channel[]
      ShowInLegend: bool

      /// The rows that get a **node of their own**, by original row index.
      ///
      /// The IR batches N marks into one `Markers` node, which is the single biggest performance
      /// decision in the IR and the reason mark 42 has nothing to address. A row named here
      /// splits that batch three ways — before it, itself under `Keys.mark`, after it — so a
      /// storyboard's `highlight curve.[42]` has a subtree to rewrite. Two extra nodes per
      /// addressed mark, not N.
      ///
      /// Empty is the overwhelmingly common case, takes the unsplit path exactly, and is what
      /// every figure in the library that does not name a mark carries.
      Addressed: int[]

      /// **The data's own names for its rows**, one per row and unique, or empty for none.
      ///
      /// A mark is then keyed `Keys.datum` rather than `Keys.mark`, so it keeps its name when an
      /// earlier row is removed, and a morph pairs it with the datum of the same name rather than
      /// with whatever sits at the same row. Empty is the common case and keeps the ordinals.
      RowKeys: string[]

      /// Whether this layer's extent joins the panel's domain, on x and on y.
      ///
      /// `true, true` for data. A rule, a span or an annotation is `false, false`: it marks a place
      /// on the axes the data chose, and an `axhline` at zero that dragged a millimetre panel out
      /// to its own position would be deciding the figure rather than marking it. The unit is still
      /// checked against the axis either way, because a rule in seconds on a metre axis is wrong
      /// whether or not it moves the domain. A panel whose every layer is out takes their extents
      /// anyway, so a figure of rules alone still has axes.
      InDomain: bool * bool

      /// The layer is drawn against the panel's second y axis, on its right
      /// (`Panel.Y2Axis`). Its y unit is checked against the other layers on that side only.
      SecondaryY: bool }

/// A secondary axis's map from the primary axis's numbers to its own and back, kept out
/// of structural equality as `TickFormatter` is: it compares by reference.
[<Sealed>]
type SecondaryMap(forward: float -> float, inverse: float -> float) =
    member _.Forward(value: float) = forward value
    member _.Inverse(value: float) = inverse value
    override _.ToString() = "function"

/// **An axis that reads the primary one in other numbers**: photon energy over a
/// wavelength, Celsius over kelvin, period over frequency. It draws on the opposite side and owns
/// no data: it joins no domain and no layer is drawn against it.
[<NoComparison>]
type SecondaryAxis =
    { /// The unit its numbers are in.
      Unit: U<1>
      /// `None` for another unit of the primary's dimension, converted by the witnesses, offset
      /// included; otherwise the map, checked on the domain when the figure is drawn.
      Map: SecondaryMap option
      /// The quantity's name; `None` takes the conventional symbol for the unit's dimension.
      Label: string option }

/// Per-axis configuration. Every field has a defensible default, which is what "no
/// configuration" has to mean in practice.
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
      ShowTitle: bool
      /// The ticks, chosen (`TicksAuto`) or stated, in the axis's own numbers.
      Ticks: TickSpec
      /// The unit `Ticks` was stated in: checked against the axis, and converted into its numbers
      /// when it is another unit of the same dimension. `U.unspecified` claims nothing.
      TickUnit: U<1>
      /// The prefix the axis is written in, instead of the one its domain selects.
      /// `Some Prefix.none` writes an unprefixed axis.
      Prefix: Prefix option
      /// The axis runs from its high end to its low end, over the domain it would have
      /// had: a depth below a surface, a magnitude, a Hertzsprung–Russell diagram.
      Reversed: bool
      /// The minor ticks. `MinorAuto` draws them where the theme does (`MinorTicks`); a
      /// stated step or count draws them whatever the theme.
      Minor: MinorSpec
      /// How the tick numbers are written.
      Format: TickFormat
      /// Whether the tick labels are written. The ticks stay either way. Off on a panel
      /// whose shared axis is labelled by the panel beyond it (matplotlib's `label_outer`).
      ShowTickLabels: bool
      /// An axis on the opposite side reading this one in other numbers: the top for x,
      /// the right for y.
      Secondary: SecondaryAxis option
      /// Whether the tick marks are drawn, mirrored ones included. An axis without them reserves
      /// no room for their length.
      ShowTicks: bool }

/// How much of an axis is drawn, whatever its ticks, prefix and domain say: the five looks a
/// caller asks for by name rather than by fields. `Chart.withXLook` sets only `Visible`,
/// `ShowTicks`, `ShowTickLabels` and `ShowTitle`, so it composes with every other axis verb in
/// either order.
type AxisLook =
    /// Ticks, numbers and title: the default.
    | AxisFull
    /// No title: an inset, or a panel whose quantity its neighbour names.
    | AxisUntitled
    /// Ticks only, without numbers or title (what a shared axis's inner panels draw).
    | AxisUnlabelled
    /// The line alone, without ticks (matplotlib's `set_xticks([])`).
    | AxisBare
    /// Nothing (matplotlib's `axis('off')`). A framed theme still draws its frame.
    | AxisHidden

/// The shape a panel's plot is held to.
[<RequireQualifiedAccess>]
type Aspect =
    /// One unit of the data is as long on y as on x (matplotlib's `set_aspect('equal')`). Both axes
    /// must have one dimension, compared in SI, and be linear.
    | Equal
    /// The plot's height over its width (matplotlib's `set_box_aspect`).
    | Box of heightOverWidth: float

[<NoComparison>]
type Panel =
    { Title: string
      /// The panel's top-left cell, and its name: `panel-<row>-<col>`.
      Row: int
      Col: int
      /// How many rows and columns of the grid the panel covers, from its top-left cell.
      /// 1 and 1 for a panel in one cell, which is every panel `beside` and `above` make when
      /// the two sides have the same number of rows or columns.
      RowSpan: int
      ColSpan: int
      /// The group of panels this one shares its x axis with, or 0 for none. A group is
      /// made by `Chart.shareX` (or a facet) over the panels the figure has at that moment, once
      /// their units have been checked, and it is renumbered when figures are composed, so a
      /// panel composed in later never joins it.
      XShare: int
      /// The same for y.
      YShare: int
      /// The second y axis, on the right, for the layers marked `SecondaryY`; `None` for a
      /// panel with one y axis.
      Y2Axis: AxisConfig option
      /// The shape the plot is held to, or `None` for whatever its region gives it.
      Aspect: Aspect option
      /// Panels drawn over this one's plot, in order.
      Insets: Inset[]
      Layers: Layer[]
      XAxis: AxisConfig
      YAxis: AxisConfig
      /// Cartesian is the identity. Nothing else in this file knows polar exists.
      Coord: Coord
      Legend: LegendPlacement
      /// The legend's columns, filled down then across.
      LegendColumns: int
      /// The legend's frame, overriding the theme's `ShowLegendFrame`.
      LegendFrame: bool option }

/// **A panel drawn over another's plot** (matplotlib's `inset_axes`), at a rectangle in
/// fractions of the host's plot, y up. The rectangle is the inset's own plot; its tick labels and
/// titles stand round it, over the host, where the caller put it. It shares nothing with its
/// host: its axes are its own, and a zoom inside it zooms it.
and [<NoComparison>] Inset =
    { Panel: Panel
      At: Rect<nd>
      /// The inset's domain drawn as a rectangle on the host, joined to the inset by two lines
      /// (matplotlib's `indicate_inset_zoom`).
      Indicate: bool }

/// How `Chart.withPanelLetters` writes a panel's letter.
type LetterStyle =
    /// (a), (b), (c) — the usual one in physics.
    | LetterParen
    /// a, b, c
    | LetterPlain
    /// a), b), c)
    | LetterRightParen
    /// A, B, C
    | LetterUpper

[<NoComparison>]
type PanelLetters = { Letter: LetterStyle; Bold: bool }

[<NoComparison>]
type Figure =
    { Title: string
      Subtitle: string
      Rows: int
      Cols: int
      Panels: Panel[]
      Theme: Theme
      /// Letters for the panels, assigned in reading order when the figure is drawn, so a
      /// panel added by `beside` after the call is lettered too. `None` writes none.
      Letters: PanelLetters option
      /// One legend for every panel, beside or below the grid of them, in place of each
      /// panel's own.
      Legend: FigureLegend option
      /// The rows' plot heights and the columns' plot widths, relative, one weight per row
      /// or column and averaging 1. Empty for equal ones, which is every figure that does not
      /// state them. They share out the plots, not the cells, so `[3; 1]` draws a plot
      /// three times as tall as the one below it whatever their gutters.
      RowWeights: float[]
      ColWeights: float[] }

/// **Which axes the panels of a composed figure share.**
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

/// One panel's inner grid, frozen.
///
/// The nine sizes are the `Track.Content` extents of the panel's inner grid: its *gutters*. They
/// are what an axis title, a column of tick labels, a panel title or a colorbar takes out of the
/// region the figure gave the panel, and the plot rectangle is what is left over. That order is
/// the reason a panel breathes when it is re-arranged per frame — the rectangle is a
/// function of shaped text, so a domain crossing `9.5 → 10.5` gains a digit on every label at
/// once, every gutter widens together, and the axis crawls while the data is the thing that was
/// supposed to be moving.
///
/// `Probe` is the rectangle the *tick plan* is measured against — how many labels survive and
/// whether they tilt. It is one of the two passes' inputs rather than one of its outputs, and it
/// is frozen for the same reason and stated separately for that one.
///
/// **What is deliberately not here: the tick plan, and the labels.** A shot freezes the rectangle,
/// not its contents. An animation whose numbers change and whose labels do not is not showing the
/// numbers changing.
type PanelLayout =
    internal
        { TitleH: float<px>
          TopPad: float<px>
          XTicksH: float<px>
          XTitleH: float<px>
          YTitleW: float<px>
          YTicksW: float<px>
          RightPad: float<px>
          BarGap: float<px>
          BarW: float<px>
          /// An outside legend's column (right) or row (bottom), and the gap before it.
          LegendGap: float<px>
          LegendW: float<px>
          LegendH: float<px>
          /// For `LegendBest`, the share of the panel's drawn samples under the legend's box
          /// in each inside corner (top right, top left, bottom left, bottom right). A shot keeps the
          /// largest each corner had in any frame, and the legend goes where that is least.
          LegendScores: float[]
          /// The plot rectangle the tick plan is measured against.
          Probe: Extent<px>
          /// The prefix the x axis is pinned to. `None` chooses one from this figure's own
          /// domain, which is what a still figure does and has always done.
          XPrefix: Prefix option
          YPrefix: Prefix option
          /// Room left empty at each outer edge of the panel's region, so that its gutters
          /// add up to those of the widest panel on the same grid line and the plot rectangles line
          /// up. Zero until the figure's panels are aligned, and always zero for a panel alone.
          SlackLeft: float<px>
          SlackRight: float<px>
          SlackTop: float<px>
          SlackBottom: float<px>
          /// The second y axis's tick labels and title, on the right of the plot; zero for a
          /// panel with one y axis.
          Y2TicksW: float<px>
          Y2TitleW: float<px>
          /// The prefix the axis on the right is pinned to, as `YPrefix` is for the first: a
          /// second y axis's, or a secondary y axis's. A panel has one right side.
          Y2Prefix: Prefix option
          /// A secondary x axis's tick labels and title, above the plot.
          X2TicksH: float<px>
          X2TitleH: float<px>
          /// The prefix a secondary x axis is pinned to.
          X2Prefix: Prefix option
          /// Room for an annotation's text past the plot's left and bottom edges, beyond what
          /// the axis already takes there. `RightPad` and `TopPad` take it on the other two sides.
          LeftPad: float<px>
          BottomPad: float<px> }

[<RequireQualifiedAccess>]
module PanelLayout =

    let internal empty: PanelLayout =
        { TitleH = 0.0<px>
          TopPad = 0.0<px>
          XTicksH = 0.0<px>
          XTitleH = 0.0<px>
          YTitleW = 0.0<px>
          YTicksW = 0.0<px>
          RightPad = 0.0<px>
          BarGap = 0.0<px>
          BarW = 0.0<px>
          LegendGap = 0.0<px>
          LegendW = 0.0<px>
          LegendH = 0.0<px>
          LegendScores = [||]
          Probe = { Width = 0.0<px>; Height = 0.0<px> }
          XPrefix = None
          YPrefix = None
          SlackLeft = 0.0<px>
          SlackRight = 0.0<px>
          SlackTop = 0.0<px>
          SlackBottom = 0.0<px>
          Y2TicksW = 0.0<px>
          Y2TitleW = 0.0<px>
          Y2Prefix = None
          X2TicksH = 0.0<px>
          X2TitleH = 0.0<px>
          X2Prefix = None
          LeftPad = 0.0<px>
          BottomPad = 0.0<px> }

    /// The four inside corners, in the order `LegendScores` keeps them.
    let internal legendCorners = [| LegendTopRight; LegendTopLeft; LegendBottomLeft; LegendBottomRight |]

    /// Whether every inside corner is more than 2% covered: a corner the legend would hide
    /// data in.
    let internal crowded (scores: float[]) = scores.Length = 4 && Array.min scores > 0.02

    /// Where a legend stated as `stated` stands in a panel laid out as `g`: `LegendBest`
    /// resolved to the least covered inside corner, or outside on the right when every corner is
    /// crowded and the layout made room there. The drawing pass places it here, and a pointer's
    /// readout keeps out of its corner.
    let legendPlacement (stated: LegendPlacement) (g: PanelLayout) =
        match stated with
        | LegendBest when g.LegendScores.Length = 4 ->
            if crowded g.LegendScores && g.LegendW > 0.0<px> then
                LegendOutsideRight
            else
                let least = Array.min g.LegendScores
                legendCorners.[Array.findIndex (fun v -> v = least) g.LegendScores]
        | LegendBest -> LegendTopRight
        | other -> other

    /// The shot's union, and the whole rule is one sentence: **every size is the largest any
    /// sampled frame needed.**
    ///
    /// Componentwise, not "the sample with the biggest gutters". There is no such sample — one
    /// frame can want a wider y gutter and a shorter x one than another, and picking either
    /// whole arrangement then overflows in the direction it did not win. The componentwise
    /// maximum is the arrangement no frame can overflow, which is the property a shot is actually
    /// asking for, and it is the same rule for `Probe`: a larger probe keeps *more* tick labels,
    /// and the gutters that hold them have already been maximised.
    ///
    /// The prefixes are not unioned — a prefix has no maximum, and it is decided from the union
    /// *domain* rather than from the sampled choices. `union` carries the left operand's through
    /// and `arrangeUnion` writes the pinned pair afterwards.
    let internal union (a: PanelLayout) (b: PanelLayout) : PanelLayout =
        { TitleH = max a.TitleH b.TitleH
          TopPad = max a.TopPad b.TopPad
          XTicksH = max a.XTicksH b.XTicksH
          XTitleH = max a.XTitleH b.XTitleH
          YTitleW = max a.YTitleW b.YTitleW
          YTicksW = max a.YTicksW b.YTicksW
          RightPad = max a.RightPad b.RightPad
          BarGap = max a.BarGap b.BarGap
          BarW = max a.BarW b.BarW
          LegendGap = max a.LegendGap b.LegendGap
          LegendW = max a.LegendW b.LegendW
          LegendH = max a.LegendH b.LegendH
          LegendScores =
            if a.LegendScores.Length = 0 then b.LegendScores
            elif b.LegendScores.Length = 0 then a.LegendScores
            else Array.map2 max a.LegendScores b.LegendScores
          Probe =
            { Width = max a.Probe.Width b.Probe.Width
              Height = max a.Probe.Height b.Probe.Height }
          XPrefix = a.XPrefix
          YPrefix = a.YPrefix
          SlackLeft = max a.SlackLeft b.SlackLeft
          SlackRight = max a.SlackRight b.SlackRight
          SlackTop = max a.SlackTop b.SlackTop
          SlackBottom = max a.SlackBottom b.SlackBottom
          Y2TicksW = max a.Y2TicksW b.Y2TicksW
          Y2TitleW = max a.Y2TitleW b.Y2TitleW
          Y2Prefix = a.Y2Prefix
          X2TicksH = max a.X2TicksH b.X2TicksH
          X2TitleH = max a.X2TitleH b.X2TitleH
          X2Prefix = a.X2Prefix
          LeftPad = max a.LeftPad b.LeftPad
          BottomPad = max a.BottomPad b.BottomPad }

    /// The room a panel's gutters take on each side of its plot, slack included: what the
    /// outer grid's gutter tracks are sized from.
    let internal left (g: PanelLayout) = g.SlackLeft + g.LeftPad + g.YTitleW + g.YTicksW

    let internal right (g: PanelLayout) =
        g.Y2TicksW + g.Y2TitleW + g.RightPad + g.BarGap + g.BarW + (if g.LegendW > 0.0<px> then g.LegendGap + g.LegendW else 0.0<px>) + g.SlackRight

    let internal top (g: PanelLayout) = g.SlackTop + g.TitleH + g.TopPad + g.X2TitleH + g.X2TicksH

    let internal bottom (g: PanelLayout) =
        g.XTicksH + g.XTitleH + (if g.LegendH > 0.0<px> then g.LegendGap + g.LegendH else 0.0<px>) + g.BottomPad + g.SlackBottom

/// **The layout of one shot**: everything every frame of an animation shares, and nothing
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
      /// How many states the union was taken over. **Reported rather than assumed:**
      /// the union is only as good as its samples, and an animation with a spike between two of
      /// them can still overflow. A single figure's own shot reports 1.
      Samples: int
      /// Every continuous colour channel's domain, unioned over the sampled states and
      /// keyed `panel-0-0/layer/channel`. A frame draws its colours and its colorbar against
      /// this rather than against its own range, so a growing field reads as growing rather than
      /// being renormalised every frame. Empty for a still. A value outside it, between two
      /// samples, takes the ramp's end colour.
      ColourDomains: (string * (float * float))[]
      /// Every quiver layer's key, the one with the largest magnitude among the sampled
      /// states, keyed `panel-0-0/layer`. The arrows are scaled against it in every frame, for
      /// the colour domain's reason. Empty for a still.
      QuiverKeys: (string * QuiverKey)[] }

/// **One panel as a pointer meets it**: where its plot area is on the surface, and the
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
      /// in. A reading of the pointer is written in them too, so it can be checked
      /// against the ticks without converting anything.
      XAxis: AxisSpec
      YAxis: AxisSpec
      /// The second y axis's scale and spec, for a panel that has one.
      Y2: IScaleErased option
      Y2Axis: AxisSpec option
      /// The panel holds one unit as long on y as on x.
      EqualAspect: bool
      /// Which of the panel at `Row`, `Col`'s insets this is, or `None` for the panel.
      Inset: int option }

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

    /// Two domains widened about their centres until one unit is as long on y as on x
    /// over this panel's plot, or as they were for a panel without equal aspect. What a zoom of
    /// such a panel states, so that re-arranging it keeps the plot's shape.
    let fitEqual (v: PanelView) (dx: float * float, dy: float * float) : (float * float) * (float * float) =
        let w = v.Plot.X1 - v.Plot.X0
        let h = v.Plot.Y1 - v.Plot.Y0
        let xs = abs (snd dx - fst dx) * v.X.ToSI
        let ys = abs (snd dy - fst dy) * v.Y.ToSI

        if not v.EqualAspect || w <= 0.0<px> || h <= 0.0<px> || xs = 0.0 || ys = 0.0 then
            dx, dy
        else
            let want = float (h / w)
            let have = ys / xs
            let grow (lo: float, hi: float) (k: float) =
                let c = (lo + hi) / 2.0
                let half = (hi - lo) / 2.0 * k
                c - half, c + half

            if have < want then dx, grow dy (want / have) else grow dx (have / want), dy

    /// The second y axis's value under a surface point, raw, in its own unit.
    let toY2 (p: Vec2<px>) (v: PanelView) : float option =
        match v.Y2 with
        | Some s -> toUnit p v |> Option.map (fun u -> s.Invert u.Y)
        | None -> None

    /// The second y axis's domain over a dragged rectangle, as `domainsOf` gives the first's.
    let y2DomainOf (r: Rect<px>) (v: PanelView) : (float * float) option =
        match toY2 { X = r.X0; Y = r.Y0 } v, toY2 { X = r.X0; Y = r.Y1 } v with
        | Some a, Some b -> Some(min a b, max a b)
        | _ -> None

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

    /// **What a pointer reads**: one line per axis, x first, each the axis's name and the
    /// value under the point, written in the prefix and unit text the axis's ticks are written
    /// in. A categorical axis reads the level whose band the point is in.
    ///
    /// **The places are the ones a pixel resolves.** A reading is good to how far the value moves
    /// when the pointer moves by one px, read back through the scales and the coord, so a log
    /// axis gets more places near its bottom than near its top and a polar plot gets them from
    /// both directions at once. Places past that would be the pointer's noise written down as
    /// data; a step coarser than 1 is rounded to its decade, so a count axis reads `4120000` and
    /// not a seven-digit number whose last four digits are the pixel grid. A log axis whose ticks
    /// are powers of ten (`Ticks.logInPowers`) is read in them, its mantissa to the same places:
    /// `1.1×10⁻¹⁸ J` under ticks of `10⁻¹⁸ … 10⁸`, where fixed point wrote fifteen zeros.
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

                        // A log axis whose ticks are powers of ten is read in them, by the rule
                        // that wrote the ticks: fixed point at its fine end is a row of zeros.
                        let inPowers =
                            match scale.Kind with
                            | ScaleKind.Log _ -> Ticks.logInPowers scale.Domain (spec.Ticks |> Array.map (fun t -> t.Value))
                            | _ -> false

                        let number =
                            if inPowers then
                                Ticks.formatPowerTo r scaled
                            elif r >= 1.0 && not (System.Double.IsInfinity r) then
                                let decade = 10.0 ** floor (log10 r)
                                Ticks.format 0 (Num.roundEven (scaled / decade) * decade)
                            else
                                Ticks.format (Ticks.decimalsFor r) scaled

                        UnitDisplay.withUnit spec.UnitText number

                if spec.Label = "" then shown else spec.Label + " = " + shown

            // A third line for a second y axis, resolved over one px as the others are.
            let second =
                match v.Y2, v.Y2Axis, toY2 p v with
                | Some s, Some spec, Some y2 ->
                    let r =
                        [ toY2 { X = p.X; Y = p.Y + 1.0<px> } v; toY2 { X = p.X; Y = p.Y - 1.0<px> } v ]
                        |> List.choose id
                        |> List.map (fun y' -> abs (y' - y2))
                        |> List.fold max 0.0

                    [| line spec s y2 r |]
                | _ -> [||]

            Some(Array.append [| line v.XAxis v.X x (span fst); line v.YAxis v.Y y (span snd) |] second)

/// **The compiler.** A `Figure` in, a `Node` out, nothing in between that a backend can see.
///
/// This is the layer where axes, legends, titles, colorbars, facets, error bars and box plots
/// are all *compiled into* `Path`/`Text`/`Markers`. Everything below
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

    /// The key a layer's mark carries: by the data's own key when the layer has them,
    /// by its original row otherwise. `row` is always the original row.
    let internal markKey (l: Layer) (layerKey: Key) (row: int) : Key =
        if l.RowKeys.Length = 0 then Keys.mark layerKey row else Keys.datum layerKey l.RowKeys.[row]

    let private isEmpty (s: string) = System.String.IsNullOrEmpty s

    // ---- Crossing the existential --------------------------------------------------------

    /// The two scales of a packed series, still packed. The rank-2 encoding hands the measures back
    /// inside `Apply`; this repacks them one at a time so the axis compiler can take either.
    let internal packedScales (p: PackedSeries) : PackedScale * PackedScale =
        p.Accept
            { new ISeriesConsumer<PackedScale * PackedScale> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) =
                    Erase.packScale s.XScale, Erase.packScale s.YScale }

    /// **Which layer an axis takes its unit from**: the first whose unit on that axis was
    /// stated, or the first layer when none was. An unspecified layer drawn first under a metre
    /// layer makes no claim about the axis, so the axis reads `[m]` whichever order they were
    /// added in. Every reader of a panel's axes goes through here — the drawing pass, the prefix a
    /// shot pins and the pointer's readout — so they cannot disagree about it.
    let private axisLayer (pick: PackedSeries -> IScaleErased) (layers: Layer[]) : PackedSeries =
        match layers |> Array.tryFind (fun l -> (pick l.Data).Stated) with
        | Some l -> l.Data
        | None -> layers.[0].Data

    /// The layers a panel's left y axis is drawn from: all of them but those on its second
    /// axis, or all of them when every layer is there.
    let private onSecond (p: Panel) (l: Layer) = l.SecondaryY && p.Y2Axis.IsSome

    let private primaryLayers (p: Panel) =
        match p.Layers |> Array.filter (fun l -> not (onSecond p l)) with
        | [||] -> p.Layers
        | ls -> ls

    /// **A panel's second y axis, as a panel of its own**: its secondary layers under
    /// `Y2Axis`. Everything that reads one y axis — the scales, the spec, the prefix — reads the
    /// second through this, so there is one code path for both. `None` for a panel with one y.
    let internal secondaryOf (p: Panel) : Panel option =
        match p.Y2Axis with
        | Some cfg when p.Layers |> Array.exists (fun l -> l.SecondaryY) ->
            Some
                { p with
                    Layers = p.Layers |> Array.filter (fun l -> l.SecondaryY)
                    YAxis = cfg
                    Y2Axis = None }
        | _ -> None

    /// A panel's x and y scales as its axes draw them. See `axisLayer`.
    let internal axisScales (p: Panel) : PackedScale * PackedScale =
        fst (packedScales (axisLayer (Erase.seriesScales >> fst) p.Layers)),
        snd (packedScales (axisLayer (Erase.seriesScales >> snd) (primaryLayers p)))

    /// `axisScales`, across the erasure boundary.
    let internal axisScalesErased (p: Panel) : IScaleErased * IScaleErased =
        fst (Erase.seriesScales (axisLayer (Erase.seriesScales >> fst) p.Layers)),
        snd (Erase.seriesScales (axisLayer (Erase.seriesScales >> snd) (primaryLayers p)))


    /// Rebuild a packed series over new raw domains.
    ///
    /// The floats are raw and the measures are reattached inside `Apply`, where they are known —
    /// so this is measure-safe even though the caller could not name the measure. It is how two
    /// layers on one panel come to share an axis: their domains are unioned as numbers and each
    /// series is rebuilt over the union in its own unit.
    let internal withDomains (dx: (float * float) option) (dy: (float * float) option) (p: PackedSeries) : PackedSeries =
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
    let internal withKinds (kx: ScaleKind option) (ky: ScaleKind option) (p: PackedSeries) : PackedSeries =
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

    let internal axisConfig: AxisConfig =
        { Label = None
          // `None` means "take the theme's", which is what makes `Theme.tikz` sufficient on its
          // own. An `AxisConfig` cannot default to `Unicode` and still let a theme change it: an
          // explicit `Unicode` and an unset field would be indistinguishable, so a theme could
          // never tell whether it was allowed to supply a different one.
          Style = None
          TickCount = Axis.DefaultTickCount
          Grid = None
          Visible = true
          ShowTitle = true
          Ticks = TicksAuto
          TickUnit = U.unspecified
          Prefix = None
          Reversed = false
          Minor = MinorAuto
          Format = FormatAuto
          ShowTickLabels = true
          Secondary = None
          ShowTicks = true }

    /// `cfg` drawn with `look`; every other field kept.
    let internal withLook (look: AxisLook) (cfg: AxisConfig) : AxisConfig =
        let shown visible ticks labels title =
            { cfg with
                Visible = visible
                ShowTicks = ticks
                ShowTickLabels = labels
                ShowTitle = title }

        match look with
        | AxisFull -> shown true true true true
        | AxisUntitled -> shown true true true false
        | AxisUnlabelled -> shown true true false false
        | AxisBare -> shown true false false false
        | AxisHidden -> shown false false false false

    let internal panelOf (layers: Layer[]) : Panel =
        { Title = ""
          Row = 0
          Col = 0
          RowSpan = 1
          ColSpan = 1
          XShare = 0
          YShare = 0
          Y2Axis = None
          Aspect = None
          Insets = [||]
          Layers = layers
          XAxis = axisConfig
          YAxis = axisConfig
          Coord = Coord.cartesian
          Legend = LegendTopRight
          LegendColumns = 1
          LegendFrame = None }

    let create (panels: Panel[]) : Figure =
        { Title = ""
          Subtitle = ""
          Rows = (if panels.Length = 0 then 1 else panels |> Array.map (fun p -> p.Row + max 1 p.RowSpan) |> Array.max)
          Cols = (if panels.Length = 0 then 1 else panels |> Array.map (fun p -> p.Col + max 1 p.ColSpan) |> Array.max)
          Panels = panels
          Theme = Theme.initial
          Letters = None
          Legend = None
          RowWeights = [||]
          ColWeights = [||] }

    // ---- Sharing an axis: the one runtime dimensional check --------------------------------

    /// The padding `Chart.series` and `Erase.autoScale` give a series, and therefore the one
    /// `agreeOn` can recognise as padding rather than as a domain.
    [<Literal>]
    let private autoPadding = 0.05

    /// A layer's `Scale.pointOf` on each axis: the constant its domain is only the padding of.
    /// Read inside `Apply`, from the data in place, because `unify` runs on every render and a
    /// copy of each layer's data per frame is not what an axis is worth. Only a linear padding
    /// is recognised: a point given room on a log axis by a linear margin could be given a
    /// negative one.
    let private pointsOf (p: PackedSeries) : float option * float option =
        p.Accept
            { new ISeriesConsumer<float option * float option> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) =
                    let point (sc: Scale<'u>) (xs: float<'u>[]) =
                        match sc.Kind with
                        | ScaleKind.Linear -> Scale.pointOf autoPadding sc.Domain xs |> Option.map float
                        | _ -> None

                    point s.XScale s.X, point s.YScale s.Y }

    /// The entries whose layer joins the domain, or all of them when none does, so that a
    /// panel of rules alone still has an axis. `included` is one flag per entry.
    let private joining (included: bool[]) (entries: 'a[]) : 'a[] =
        if Array.exists id included then
            Array.zip entries included |> Array.filter snd |> Array.map fst
        else
            entries

    /// A unified panel's `pointsOf`: the constant on each axis when every layer is that same
    /// constant and the axis is only its padding. `unify` has given every layer the panel's
    /// domain, so one layer with a real span is enough to make the axis a span.
    let private panelPoints (p: Panel) : float option * float option =
        let shared (layers: Layer[]) pick (inDomain: Layer -> bool) =
            let points =
                layers
                |> Array.map (fun l -> pick (pointsOf l.Data))
                |> joining (layers |> Array.map inDomain)

            match points.[0] with
            | Some v when points |> Array.forall (fun q -> q = Some v) -> Some v
            | _ -> None

        // Y over the left axis's layers; a second axis is a panel of its own (`secondaryOf`).
        shared p.Layers fst (fun l -> fst l.InDomain), shared (primaryLayers p) snd (fun l -> snd l.InDomain)

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
    ///
    /// `points` is each thing's `pointsOf` on this axis, so that a constant's padding can be told
    /// from a domain. `included` is whether each thing joins the domain: every one is
    /// checked, and only those that join are shared.
    let private agreeOn
        (describe: int -> string)
        (axis: string)
        (scales: IScaleErased[])
        (points: float option[])
        (included: bool[])
        : float * float =
        let s0 = scales.[0]

        let reference =
            scales |> Array.indexed |> Array.tryFind (fun (_, s) -> s.Stated)

        // The categorical counterpart of the dimensional check, and it is needed for exactly the
        // reason that check is: an ordinal scale is dimensionless, so two scales with
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

            // The dimensional checks compare stated units only, each against the first
            // stated one: an unspecified layer makes no claim, so it neither fails the check nor
            // becomes the reference that hides a disagreement between two that did.
            match reference with
            | Some(ri, r) when ri <> i && si.Stated ->
                // The one difference `Dim` cannot see. An angular velocity and a frequency are
                // both s⁻¹ and differ by 2π, so a rad/s layer and a Hz layer on one axis plot one of
                // them 6.28 times too high without a word.
                let ar = U.angleExponentOf r.Symbol r.Factors
                let ai = U.angleExponentOf si.Symbol si.Factors

                if si.Dim = r.Dim && ai <> ar then
                    failwithf
                        "Marey: %s has %s %s axis in %s but %s has one in %s. Their dimensions agree, but one carries an angle and the other does not — an angular velocity and a frequency differ by 2π. They cannot share an axis."
                        (describe ri)
                        (if axis = "x" then "an" else "a")
                        axis
                        (UnitDisplay.unitTextErasedWith Unicode UnitRegistry.si Prefix.none r.Symbol r.Factors r.Dim r.ToSI r.OffsetSI)
                        (describe i)
                        (UnitDisplay.unitTextErasedWith Unicode UnitRegistry.si Prefix.none si.Symbol si.Factors si.Dim si.ToSI si.OffsetSI)

                if si.Dim <> r.Dim then
                    failwithf
                        "Marey: %s has %s %s axis in %s but %s has one in %s. Two physical dimensions cannot share an axis."
                        (describe ri)
                        (if axis = "x" then "an" else "a")
                        axis
                        (Dim.show r.Dim)
                        (describe i)
                        (Dim.show si.Dim)
            | _ -> ()

        // The union is of domains, except that a constant's padding is not a domain: a flat line
        // at zero is padded to ±1 in its own unit, and taking that as data turned a ±1 mm axis
        // into a ±1 m one.
        Array.zip (scales |> Array.map (fun s -> s.Domain)) points
        |> joining included
        |> Scale.shareDomain autoPadding

    /// Give every layer on a panel the same x and y domains.
    ///
    /// Applied at **compile** time rather than at construction, and that is deliberate: a panel
    /// accumulates layers after it is built — `Chart.add` is one call — so there is no moment
    /// before rendering at which its layer set is known to be complete. `shareScales` below makes
    /// the opposite choice, for the opposite reason.
    let private share (p: Panel) : Panel =
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

            let points = p.Layers |> Array.map (fun l -> pointsOf l.Data)
            let inX = p.Layers |> Array.map (fun l -> fst l.InDomain)
            let inY = p.Layers |> Array.map (fun l -> snd l.InDomain)
            let dx = agreeOn describe "x" (scales |> Array.map fst) (points |> Array.map fst) inX

            // Y is agreed per side: the layers on the left among themselves, and those on
            // a second axis among themselves, each naming its layers by their place on the panel.
            let ySide (second: bool) (axis: string) =
                let idx = p.Layers |> Array.indexed |> Array.filter (fun (_, l) -> onSecond p l = second) |> Array.map fst

                if idx.Length = 0 then
                    None
                else
                    Some(
                        agreeOn
                            (fun i -> describe idx.[i])
                            axis
                            (idx |> Array.map (fun i -> snd scales.[i]))
                            (idx |> Array.map (fun i -> snd points.[i]))
                            (idx |> Array.map (fun i -> inY.[i]))
                    )

            let dy = ySide false "y"
            let dy2 = ySide true "right y"

            { p with
                Layers =
                    p.Layers
                    |> Array.map (fun l ->
                        { l with
                            Data = withDomains (Some dx) (if onSecond p l then dy2 else dy) l.Data }) }

    /// Run a reversed axis from its high end to its low end, over the domain it would
    /// have had. After the sharing, so the padding and the union are the ones a forward axis gets.
    let private orient (p: Panel) : Panel =
        let y2Reversed = p.Y2Axis |> Option.exists (fun c -> c.Reversed)

        if not (p.XAxis.Reversed || p.YAxis.Reversed || y2Reversed) then
            p
        else
            let flip (reversed: bool) (lo: float, hi: float) =
                if reversed then Some(max lo hi, min lo hi) else None

            { p with
                Layers =
                    p.Layers
                    |> Array.map (fun l ->
                        let sx, sy = Erase.seriesScales l.Data
                        let yReversed = if onSecond p l then y2Reversed else p.YAxis.Reversed
                        { l with Data = withDomains (flip p.XAxis.Reversed sx.Domain) (flip yReversed sy.Domain) l.Data }) }

    /// Give every layer on a panel the same x and y domains, and run a reversed axis backwards.
    /// See `share`; every reader of a panel's axes goes through here.
    let internal unify (p: Panel) : Panel = p |> share |> orient

    /// **The plot's height over its width that a panel asks for**, over the states of it a
    /// shot holds (one for a still figure), unified. Equal aspect takes the union of their domains,
    /// so the box is the one every state fits in; it needs one dimension on both axes, compared in
    /// SI, and linear scales.
    let private aspectOf (states: Panel[]) : float option =
        let drawn = states |> Array.filter (fun p -> p.Layers.Length > 0)

        if drawn.Length = 0 then
            None
        else
            match drawn.[0].Aspect with
            | None -> None
            | Some(Aspect.Box r) ->
                if not (r > 0.0) || System.Double.IsInfinity r then
                    failwithf "Marey: a box aspect is a positive height over width; got %g." r

                Some r
            | Some Aspect.Equal ->
                let scales = drawn |> Array.map axisScalesErased
                let sx, sy = scales.[0]

                let text (u: IScaleErased) =
                    UnitDisplay.unitTextErasedWith Unicode UnitRegistry.si Prefix.none u.Symbol u.Factors u.Dim u.ToSI u.OffsetSI

                if sx.Dim <> sy.Dim then
                    failwithf
                        "Marey: equal aspect needs one dimension on both axes; this panel's x is in %s and its y in %s."
                        (text sx)
                        (text sy)

                match sx.Kind, sy.Kind with
                | ScaleKind.Linear, ScaleKind.Linear -> ()
                | _ -> failwith "Marey: equal aspect needs two linear axes; this panel has a log, power or categorical one."

                let span (pick: IScaleErased * IScaleErased -> IScaleErased) =
                    let ds = scales |> Array.map (fun pair -> (pick pair).Domain)
                    let lo = ds |> Array.map (fun (a, b) -> min a b) |> Array.min
                    let hi = ds |> Array.map (fun (a, b) -> max a b) |> Array.max
                    hi - lo

                let xs = span fst * sx.ToSI
                let ys = span snd * sy.ToSI
                if xs > 0.0 && ys > 0.0 then Some(ys / xs) else None

    /// A frame whose domains do not have the frozen plot's shape, widened about their
    /// centres until they do, so equal aspect holds in a box that does not move. A still figure's
    /// domains already have it and pass through.
    let private fitAspect (f: PanelLayout) (p: Panel) : Panel =
        match p.Aspect with
        | Some Aspect.Equal when p.Layers.Length > 0 && f.Probe.Width > 0.0<px> && f.Probe.Height > 0.0<px> ->
            let sx, sy = axisScalesErased p
            let (x0, x1), (y0, y1) = sx.Domain, sy.Domain
            let xs = abs (x1 - x0) * sx.ToSI
            let ys = abs (y1 - y0) * sy.ToSI
            let want = float (f.Probe.Height / f.Probe.Width)

            if xs = 0.0 || ys = 0.0 || abs (ys / xs / want - 1.0) < 1e-9 then
                p
            else
                let grow (lo: float, hi: float) (k: float) =
                    let c = (lo + hi) / 2.0
                    let half = (hi - lo) / 2.0 * k
                    c - half, c + half

                let dx, dy =
                    if ys / xs < want then None, Some(grow (y0, y1) (want / (ys / xs)))
                    else Some(grow (x0, x1) ((ys / xs) / want)), None

                { p with Layers = p.Layers |> Array.map (fun l -> { l with Data = withDomains dx (if onSecond p l then None else dy) l.Data }) }
        | _ -> p

    /// **Give every panel of a figure the same axes.** What makes a grid of panels a
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
    let internal shareScales (mode: FacetScales) (fig: Figure) : Figure =
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

            let pointsOn pick =
                entries |> Array.map (fun (_, p: Panel, li, _) -> pick (pointsOf p.Layers.[li].Data))

            let includedOn pick =
                entries |> Array.map (fun (_, p: Panel, li, _) -> pick p.Layers.[li].InDomain)

            let dx =
                match mode with
                | ShareBoth
                | ShareX -> Some(agreeOn describe "x" (scalesOf fst) (pointsOn fst) (includedOn fst))
                | ShareY -> None

            let dy =
                match mode with
                | ShareBoth
                | ShareY ->
                    // A facet shares the left y; a panel's second axis stays its own.
                    let yIdx =
                        entries
                        |> Array.indexed
                        |> Array.filter (fun (_, (_, p: Panel, li, _)) -> not (onSecond p p.Layers.[li]))
                        |> Array.map fst

                    let pickY (xs: 'a[]) = yIdx |> Array.map (fun i -> xs.[i])
                    Some(agreeOn (fun i -> describe yIdx.[i]) "y" (pickY (scalesOf snd)) (pickY (pointsOn snd)) (pickY (includedOn snd)))
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
                                        Data = withDomains dx (if onSecond p l then None else dy) l.Data }) }) }

    // ---- Share groups ------------------------------------------------------------------

    let private sharesX (mode: FacetScales) = mode <> ShareY
    let private sharesY (mode: FacetScales) = mode <> ShareX

    /// **Share the axes of every panel the figure has now, and remember it.** The check is
    /// `shareScales`', made here, at the call, so a dimensional mismatch is reported by the call
    /// that asked for the sharing. Only when it passes is the sharing recorded, as a group
    /// on each panel, and the group is what keeps the axis shared afterwards: a domain stated on
    /// one of its panels is stated on all of them, and the compiled figure shares them again.
    let internal shareRecorded (mode: FacetScales) (fig: Figure) : Figure =
        let shared = shareScales mode fig
        let next (pick: Panel -> int) = 1 + (fig.Panels |> Array.fold (fun acc p -> max acc (pick p)) 0)
        let gx = next (fun p -> p.XShare)
        let gy = next (fun p -> p.YShare)

        { shared with
            Panels =
                shared.Panels
                |> Array.map (fun p ->
                    { p with
                        XShare = (if sharesX mode then gx else p.XShare)
                        YShare = (if sharesY mode then gy else p.YShare) }) }

    /// The panels in `p`'s x group (or `p` alone when it has none), as a predicate.
    let private inXGroup (p: Panel) (q: Panel) = (p.XShare <> 0 && q.XShare = p.XShare) || (q.Row = p.Row && q.Col = p.Col)
    let private inYGroup (p: Panel) (q: Panel) = (p.YShare <> 0 && q.YShare = p.YShare) || (q.Row = p.Row && q.Col = p.Col)

    /// A domain stated on one panel, stated on every panel its axis is shared with.
    let internal withDomainsShared (target: Panel -> bool) (dx: (float * float) option) (dy: (float * float) option) (fig: Figure) : Figure =
        let targets = fig.Panels |> Array.filter target
        let onX (q: Panel) = dx.IsSome && targets |> Array.exists (fun p -> inXGroup p q)
        let onY (q: Panel) = dy.IsSome && targets |> Array.exists (fun p -> inYGroup p q)

        { fig with
            Panels =
                fig.Panels
                |> Array.map (fun q ->
                    let x = if onX q then dx else None
                    let y = if onY q then dy else None

                    if x.IsNone && y.IsNone then
                        q
                    else
                        // A y domain is the left axis's; `withPanelDomainsY2` states the right's.
                        { q with Layers = q.Layers |> Array.map (fun l -> { l with Data = withDomains x (if onSecond q l then None else y) l.Data }) }) }

    /// Renumber `b`'s share groups past `a`'s, so composing two figures never merges a
    /// group of one with a group of the other.
    let internal offsetShares (a: Figure) (b: Figure) : Figure =
        let top (pick: Panel -> int) = a.Panels |> Array.fold (fun acc p -> max acc (pick p)) 0
        let dx, dy = top (fun p -> p.XShare), top (fun p -> p.YShare)

        { b with
            Panels =
                b.Panels
                |> Array.map (fun p ->
                    { p with
                        XShare = (if p.XShare = 0 then 0 else p.XShare + dx)
                        YShare = (if p.YShare = 0 then 0 else p.YShare + dy) }) }

    /// The figure as it is compiled: every share group shared again, which is idempotent
    /// for a group whose domains nothing has touched since and puts back one that something has;
    /// then the inner tick labels and titles off. A shared x is labelled only on a panel with no
    /// panel of its group directly below it over the same columns, a shared y only on one with
    /// none directly to its left over the same rows.
    let private prepare (fig: Figure) : Figure =
        if fig.Panels |> Array.forall (fun p -> p.XShare = 0 && p.YShare = 0) then
            fig
        else
            let regroup (pick: Panel -> int) (mode: FacetScales) (f: Figure) =
                let groups = f.Panels |> Array.map pick |> Array.filter (fun g -> g <> 0) |> Array.distinct

                groups
                |> Array.fold
                    (fun (f: Figure) g ->
                        let members = f.Panels |> Array.filter (fun p -> pick p = g)
                        let shared = shareScales mode { f with Panels = members }
                        let mutable k = 0

                        { f with
                            Panels =
                                f.Panels
                                |> Array.map (fun p ->
                                    if pick p = g then
                                        let q = shared.Panels.[k]
                                        k <- k + 1
                                        q
                                    else
                                        p) })
                    f

            let shared = fig |> regroup (fun p -> p.XShare) ShareX |> regroup (fun p -> p.YShare) ShareY
            let drawn = shared.Panels |> Array.filter (fun p -> p.Layers.Length > 0)

            let below (p: Panel) =
                p.XShare <> 0
                && drawn
                   |> Array.exists (fun q -> q.XShare = p.XShare && q.Row = p.Row + max 1 p.RowSpan && q.Col = p.Col && q.ColSpan = p.ColSpan)

            let left (p: Panel) =
                p.YShare <> 0
                && drawn
                   |> Array.exists (fun q -> q.YShare = p.YShare && q.Col + max 1 q.ColSpan = p.Col && q.Row = p.Row && q.RowSpan = p.RowSpan)

            { shared with
                Panels =
                    shared.Panels
                    |> Array.map (fun p ->
                        let p = if below p then { p with XAxis = { p.XAxis with ShowTickLabels = false; ShowTitle = false } } else p
                        if left p then { p with YAxis = { p.YAxis with ShowTickLabels = false; ShowTitle = false } } else p) }

    // ---- Compilation -----------------------------------------------------------------------

    let private textBox (m: ITextMeasurer) (f: FontSpec) (s: string) =
        TextMeasure.bounds m f AnchorMiddle BaselineMiddle 0.0<rad> s

    /// **The legend enumerates.** What one layer contributes to it.
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
    /// its channels either. The encoding block is what makes the distinction matter: its
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
                     Color =
                        // A span is a tint over the panel, and a swatch at full strength
                        // would name a colour the figure never shows.
                        (match l.Geom with
                         | GeomRule(SpanH _)
                         | GeomRule(SpanV _) -> Color.withAlpha (l.Style.Color.A * l.Style.FillOpacity) l.Style.Color
                         | _ -> l.Style.Color)
                     Mark = Geom.legendMark l.Geom l.Style } |]
            else
                [||]

        Array.concat [ own; levelEntries; sizeEntries ]

    /// Every layer's contribution, with **identical entries collapsed**.
    ///
    /// A legend enumerates what the reader has to tell apart, so the same swatch under the same
    /// name twice is not a second thing — it is the same thing counted twice. The encoding block
    /// is what makes this reachable: its channels belong to the block rather than to one
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
        // An outside legend's gap and column to the right of the colorbar, or its gap and
        // row below the x title. A panel without one has them at zero, which changes nothing.
        let gapRight = if g.LegendW > 0.0<px> then g.LegendGap else 0.0<px>
        let gapBelow = if g.LegendH > 0.0<px> then g.LegendGap else 0.0<px>

        // The slack sits outside everything else, so a title stays beside its ticks and
        // only the plot rectangle moves into line. Zero tracks add exactly nothing, so a panel
        // alone is placed as it always was.
        let cols =
            [| Track.Content g.SlackLeft
               Track.Content g.LeftPad
               Track.Content g.YTitleW
               Track.Content g.YTicksW
               Track.Frac 1.0
               Track.Content g.Y2TicksW
               Track.Content g.Y2TitleW
               Track.Content g.RightPad
               Track.Content g.BarGap
               Track.Content g.BarW
               Track.Content gapRight
               Track.Content g.LegendW
               Track.Content g.SlackRight |]

        let rows =
            [| Track.Content g.SlackTop
               Track.Content g.TitleH
               Track.Content g.TopPad
               Track.Content g.X2TitleH
               Track.Content g.X2TicksH
               Track.Frac 1.0
               Track.Content g.XTicksH
               Track.Content g.XTitleH
               Track.Content gapBelow
               Track.Content g.LegendH
               Track.Content g.BottomPad
               Track.Content g.SlackBottom |]

        let fixedCell key row col (w: float<px>) (h: float<px>) =
            Layout.cell key row col (fun _ -> { Width = w; Height = h })

        let cells =
            [| fixedCell "ytitle" 5 2 g.YTitleW 0.0<px>
               fixedCell "yticks" 5 3 g.YTicksW 0.0<px>
               Layout.fill "plot" 5 4
               fixedCell "y2ticks" 5 5 g.Y2TicksW 0.0<px>
               fixedCell "y2title" 5 6 g.Y2TitleW 0.0<px>
               fixedCell "rightpad" 5 7 g.RightPad 0.0<px>
               fixedCell "bargap" 5 8 g.BarGap 0.0<px>
               fixedCell "colorbar" 5 9 g.BarW 0.0<px>
               fixedCell "legendgap" 5 10 gapRight 0.0<px>
               fixedCell "legend" 5 11 g.LegendW 0.0<px>
               fixedCell "ptitle" 1 2 0.0<px> g.TitleH |> Layout.spanning 1 10
               fixedCell "toppad" 2 2 0.0<px> g.TopPad |> Layout.spanning 1 10
               fixedCell "x2title" 3 4 0.0<px> g.X2TitleH
               fixedCell "x2ticks" 4 4 0.0<px> g.X2TicksH
               fixedCell "xticks" 6 4 0.0<px> g.XTicksH
               fixedCell "xtitle" 7 4 0.0<px> g.XTitleH
               fixedCell "legendgapb" 8 4 0.0<px> gapBelow
               fixedCell "legendb" 9 4 0.0<px> g.LegendH |]

        Layout.arrangeIn m extent region (Layout.grid rows cols cells)

    /// A panel's two axes as they are drawn, under the prefixes given (`None` chooses from the
    /// panel's own domain). The drawing pass and `panelsIn` both take them from here, so a
    /// pointer's reading cannot be written in a unit the axis is not.
    let private axisSpecs (theme: Theme) (pinX: Prefix option, pinY: Prefix option) (p: Panel) : AxisSpec * AxisSpec =
        let (xPacked, yPacked) = axisScales p

        // The one place the theme's unit spelling and a per-axis override meet. Resolved here
        // rather than at construction time so that changing the theme on a finished figure
        // changes its axis titles — which is what `Chart.withTheme Theme.tikz` has to do.
        let unitStyle (cfg: AxisConfig) = defaultArg cfg.Style theme.UnitStyle

        // Stated ticks, checked against the axis and brought into its numbers when they were
        // stated in another unit of its dimension: kilometres on a metre axis, °C on a kelvin one.
        let ticksOf (axis: string) (cfg: AxisConfig) (scale: IScaleErased) =
            let t = cfg.TickUnit

            if not t.Stated then
                cfg.Ticks
            else
                let name (u: IUnit) =
                    UnitDisplay.unitTextErasedWith Unicode UnitRegistry.si Prefix.none u.Symbol u.Factors u.Dim u.ToSI u.OffsetSI

                if scale.Stated && scale.Dim <> t.Dim then
                    failwithf
                        "Marey: the %s ticks are stated in %s but the %s axis is in %s. Two physical dimensions cannot share an axis."
                        axis
                        (name t)
                        axis
                        (UnitDisplay.unitTextErasedWith Unicode UnitRegistry.si Prefix.none scale.Symbol scale.Factors scale.Dim scale.ToSI scale.OffsetSI)

                let at (v: float) = (v * t.ToSI + t.OffsetSI - scale.OffsetSI) / scale.ToSI

                match cfg.Ticks with
                | TicksAuto -> TicksAuto
                | TicksEvery step -> TicksEvery(step * t.ToSI / scale.ToSI)
                | TicksAt values -> TicksAt(Array.map at values)
                | TicksNamed named -> TicksNamed(named |> Array.map (fun (v, n) -> at v, n))

        // A stated prefix is the axis's, in a still figure and across a shot alike.
        let pin (cfg: AxisConfig) (chosen: Prefix option) =
            match cfg.Prefix with
            | Some _ -> cfg.Prefix
            | None -> chosen

        // A map's two axes are both lengths, and naming both `x` would say they were one
        // quantity. Each is named by its side then; a length against anything else stays `x`.
        let lengths =
            let isLength (packed: PackedScale) = Axes.dimOf packed = Dim.length && Axes.angleOf packed = 0
            isLength xPacked && isLength yPacked

        let label (cfg: AxisConfig) (positional: string) =
            match cfg.Label with
            | None when lengths -> Some positional
            | l -> l

        let xErased, yErased = axisScalesErased p

        Axes.specTicked (ticksOf "x" p.XAxis xErased) p.XAxis.Minor p.XAxis.Format (pin p.XAxis pinX) (unitStyle p.XAxis) p.XAxis.TickCount (label p.XAxis "x") AxisBottom xPacked,
        Axes.specTicked (ticksOf "y" p.YAxis yErased) p.YAxis.Minor p.YAxis.Format (pin p.YAxis pinY) (unitStyle p.YAxis) p.YAxis.TickCount (label p.YAxis "y") AxisLeft yPacked

    /// The panel letters' face, and the height of the row they need: none without letters.
    let private letterFont (theme: Theme) (letters: PanelLetters) =
        { theme.PanelTitleFont with
            Weight = (if letters.Bold then 700 else theme.PanelTitleFont.Weight) }

    let private letterHeight (m: ITextMeasurer) (fig: Figure) : float<px> =
        match fig.Letters with
        | None -> 0.0<px>
        | Some letters -> Rect.height (textBox m (letterFont fig.Theme letters) "(a)")

    /// A secondary axis's map from the primary axis's numbers to its own, and back: the one
    /// stated, or else the conversion between the two units, refused when their dimensions differ.
    let private secondaryMap (axis: string) (sa: SecondaryAxis) (primary: IScaleErased) =
        let u = sa.Unit

        let unitText (dim: Dim) symbol factors toSI offset =
            UnitDisplay.unitTextErasedWith Unicode UnitRegistry.si Prefix.none symbol factors dim toSI offset

        match primary.Kind with
        | ScaleKind.Ordinal _ -> failwithf "Marey: a categorical %s axis has no numbers for a secondary axis to read." axis
        | _ -> ()

        match sa.Map with
        | Some map -> map.Forward, map.Inverse
        | None ->
            if primary.Stated && u.Dim <> primary.Dim then
                failwithf
                    "Marey: the secondary %s axis is in %s but the %s axis is in %s. Two dimensions need a map between them (`withTopAxisMapped`, `withRightAxisMapped`)."
                    axis
                    (unitText u.Dim u.Symbol u.Factors u.ToSI u.OffsetSI)
                    axis
                    (unitText primary.Dim primary.Symbol primary.Factors primary.ToSI primary.OffsetSI)

            (fun v -> (v * primary.ToSI + primary.OffsetSI - u.OffsetSI) / u.ToSI),
            (fun w -> (w * u.ToSI + u.OffsetSI - primary.OffsetSI) / primary.ToSI)

    /// The prefix a secondary axis takes over the primary's numbers `lo` to `hi`: the one
    /// its own numbers there would choose, which is what a shot pins it to.
    let private secondaryPrefix (axis: string) (sa: SecondaryAxis) (primary: IScaleErased) (lo: float, hi: float) =
        let forward, _ = secondaryMap axis sa primary
        let a, b = forward lo, forward hi
        let lo2, hi2 = min a b, max a b
        Axis.prefixForPacked (lo2, hi2) (Erase.packScale (Scale.linear sa.Unit (LanguagePrimitives.FloatWithMeasure lo2, LanguagePrimitives.FloatWithMeasure hi2)))

    /// Compile one panel into a device-space region.
    /// One function, two answers: the panel's frozen inner grid, and a thunk that draws
    /// the panel inside it.
    ///
    /// **Why one function and not two.** Measuring and drawing share every intermediate here —
    /// the two axis specs, the colorbar, the inner grid, the tick planner — and this file already
    /// carries the proof of what splitting them costs: `Figure.arrangement` was a second copy of
    /// `toScene`'s grid, and it was silently wrong, omitting the head row so that
    /// every titled figure reported a layout the renderer did not use. So the two answers come
    /// out of one body, and a caller that only wants the layout simply never calls the thunk —
    /// which matters, because sampling an animation 32 times must not compile 32 scenes' worth
    /// of geometry to find out how wide a gutter is.
    ///
    /// `frozen = None` measures the panel from its own data, which is every still figure.
    /// `Some layout` skips the measuring pass and uses the layout given — the animation path,
    /// where the layout is a union over the whole shot and no single frame may move it.
    let private compilePanelKeyed
        (panelKey: Key)
        (m: ITextMeasurer)
        (theme: Theme)
        (extent: Extent<px>)
        (region: Rect<px>)
        (frozen: PanelLayout option)
        (pins: Prefix option * Prefix option * Prefix option * Prefix option)
        (letterH: float<px>)
        (p: Panel)
        : PanelLayout * (unit -> PanelLayout) * (unit -> Node) =
        if p.Layers.Length = 0 then
            PanelLayout.empty, (fun () -> PanelLayout.empty), (fun () -> Scene.empty)
        else
            // `panelKey` is this panel's name: its grid position for a panel, its host's and its
            // index for an inset. `Keyed` says *which* panel; the `Annotated` at the end of this
            // function says that it is a panel. Both, because identity and meaning are different
            // questions.

            // The prefix comes from the frozen layout when there is one. A still figure
            // pins nothing and chooses per figure, which is what it has always done; an
            // animation pins both axes against the union of every frame's domain, so the axis
            // *title* cannot step from `mm` to `µm` halfway through a zoom.
            //
            // `pins` is the same choice for a panel being *measured* rather than drawn: the union
            // measures every state under the prefix it will be drawn in, or a state whose own
            // prefix differs is measured with labels it never shows.
            let pinsX, pinsY, pinsY2, pinsX2 = pins
            let pinX = match frozen with Some f -> f.XPrefix | None -> pinsX
            let pinY = match frozen with Some f -> f.YPrefix | None -> pinsY
            let pinY2 = match frozen with Some f -> f.Y2Prefix | None -> pinsY2
            let pinX2 = match frozen with Some f -> f.X2Prefix | None -> pinsX2

            let xSpec, ySpec = axisSpecs theme (pinX, pinY) p

            // The second y axis, read as a panel of its own: its config and its spec.
            let second = secondaryOf p

            if second.IsSome && not (Coord.isIdentity p.Coord) then
                failwith "Marey: a second y axis needs a cartesian panel; this one has another coord."

            let y2 = second |> Option.map (fun sp -> sp.YAxis, snd (axisSpecs theme (pinX, pinY2) sp))

            // An axis reading the primary one in other numbers. Its ticks are chosen in its
            // own numbers over the image of the primary's domain, then placed through the inverse
            // and the primary's scale, so a reciprocal map puts evenly spaced energies unevenly
            // along a wavelength. The map is checked on the domain before anything is drawn.
            //
            // In a shot its prefix is pinned too, as the primary's is, so a zoom that
            // crosses a decade does not step its title.
            let secondaryOn (axis: string) (cfg: AxisConfig) (side: AxisSide) (pin: Prefix option) (primary: IScaleErased) : AxisSpec option =
                match cfg.Secondary with
                | None -> None
                | Some sa ->
                    let u = sa.Unit
                    let forward, inverse = secondaryMap axis sa primary

                    let finite (v: float) = not (System.Double.IsNaN v || System.Double.IsInfinity v)
                    let lo, hi = primary.Domain
                    let n = 256
                    let xs = Array.init (n + 1) (fun i -> lo + (hi - lo) * float i / float n)
                    let ws = xs |> Array.map forward
                    let tolerance = 1e-9 * abs (hi - lo)

                    for i in 0..n do
                        let x, w = xs.[i], ws.[i]

                        if not (finite w) then
                            failwithf "Marey: the secondary %s axis's map is not finite at %g." axis x

                        let back = inverse w

                        if not (finite back) || abs (back - x) > tolerance + 1e-12 * abs x then
                            failwithf "Marey: the secondary %s axis's map and its inverse disagree at %g: it maps to %g, which comes back as %g." axis x w back

                        if i > 0 && (w = ws.[i - 1] || sign (w - ws.[i - 1]) <> sign (ws.[1] - ws.[0])) then
                            failwithf "Marey: the secondary %s axis's map turns back at %g. A secondary axis must read each point of the primary once." axis x

                    let lo2, hi2 = min ws.[0] ws.[n], max ws.[0] ws.[n]
                    let packed = Erase.packScale (Scale.linear u (LanguagePrimitives.FloatWithMeasure lo2, LanguagePrimitives.FloatWithMeasure hi2))
                    let spec = Axes.specTicked TicksAuto cfg.Minor FormatAuto pin (defaultArg cfg.Style theme.UnitStyle) cfg.TickCount sa.Label side packed
                    let factor = Prefix.factor spec.Prefix
                    let at (w: float) = primary.Project(inverse w)

                    Some
                        { spec with
                            Ticks = spec.Ticks |> Array.map (fun t -> { t with Position = at (t.Value * factor) })
                            Minor = spec.Minor |> Array.map (fun q -> at (lo2 + float q * (hi2 - lo2))) }

            // What a secondary axis is drawn with: shown, labelled, with the primary's minor ticks.
            let shown (cfg: AxisConfig) = { axisConfig with Minor = cfg.Minor }
            let primaryX, primaryY = axisScalesErased p

            let x2 =
                secondaryOn "x" p.XAxis AxisTop pinX2 primaryX |> Option.map (fun spec -> shown p.XAxis, spec)

            let ySecondary = secondaryOn "y" p.YAxis AxisRight pinY2 primaryY

            if ySecondary.IsSome && y2.IsSome then
                failwith "Marey: a panel has one right side, and this one has both a second y axis and a secondary one."

            // The right side: a second y axis or a secondary one, laid out alike.
            let right =
                match y2 with
                | Some _ -> y2
                | None -> ySecondary |> Option.map (fun spec -> shown p.YAxis, spec)

            // A lettered figure's panels keep a title row for the letter, which stands at
            // the row's left, above the axis title, clear of the data and of the legend.
            let titleH =
                let title =
                    if isEmpty p.Title then
                        0.0<px>
                    else
                        Rect.height (textBox m theme.PanelTitleFont p.Title) + theme.TitleGap

                max title (if letterH > 0.0<px> then letterH + theme.TitleGap else 0.0<px>)

            // A continuous colour channel gets a colorbar, and the bar is a column of the
            // panel's own grid. The first one on the panel wins: two different colour scales on
            // one panel are two keys the reader has to arbitrate between, and that is a different
            // question from one scale shared across several panels.
            //
            // Gated on `ShowInLegend`, which is a layer's whole legend entry and, by extension, its
            // whole *key* contribution — the bar and the entry are the same
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

            // The legend: its entries and box, whether it sits outside, and for
            // `LegendBest` the share of the panel's drawn samples under each inside corner.
            let entries = legendEntries theme p
            let hasLegend = p.Legend <> LegendNone && entries.Length > 0
            let legendSize = Legend.measureIn p.LegendColumns m theme entries

            // The panel's marks as sampled points in panel nd: a line's vertices and 200 points
            // across the panel along each of its segments, every other geom's points.
            let samples =
                lazy
                    (p.Layers
                     |> Array.collect (fun l ->
                         let pts =
                             Erase.projectXY l.Data
                             |> Array.filter (fun v -> not (System.Double.IsNaN(float v.X) || System.Double.IsNaN(float v.Y)))
                             |> Array.map (fun v -> if Coord.isIdentity p.Coord then v else p.Coord.ToPanel v)

                         match l.Geom with
                         | GeomLine
                         | GeomStep _
                         | GeomArea _ ->
                             [| for i in 0 .. pts.Length - 2 do
                                    let a, b = pts.[i], pts.[i + 1]
                                    let n = max 1 (int (200.0 * sqrt (float ((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)))))

                                    for k in 0 .. n - 1 do
                                        let t = float k / float n
                                        yield struct (float a.X + t * float (b.X - a.X), float a.Y + t * float (b.Y - a.Y))
                                if pts.Length > 0 then
                                    yield struct (float pts.[pts.Length - 1].X, float pts.[pts.Length - 1].Y) |]
                         | _ -> pts |> Array.map (fun v -> struct (float v.X, float v.Y))))

            let corners = PanelLayout.legendCorners

            let scores (probe: Extent<px>) =
                if p.Legend <> LegendBest || not hasLegend || probe.Width <= 0.0<px> || probe.Height <= 0.0<px> then
                    [||]
                else
                    let plot: Rect<px> = { X0 = 0.0<px>; Y0 = 0.0<px>; X1 = probe.Width; Y1 = probe.Height }
                    let all = samples.Force()

                    corners
                    |> Array.map (fun corner ->
                        // `cornerBox` is in px with y down; the samples are in nd with y up.
                        let b = Legend.cornerBox theme plot legendSize corner
                        let x0, x1 = float (b.X0 / probe.Width), float (b.X1 / probe.Width)
                        let y0, y1 = 1.0 - float (b.Y1 / probe.Height), 1.0 - float (b.Y0 / probe.Height)

                        if all.Length = 0 then
                            0.0
                        else
                            let under =
                                all |> Array.sumBy (fun (struct (x, y)) -> if x >= x0 && x <= x1 && y >= y0 && y <= y1 then 1 else 0)

                            float under / float all.Length)

            let crowded = PanelLayout.crowded

            // The gutters, measured. This is the half of the panel that depends on the
            // data: how tall the surviving x labels are, how wide the widest y label is, how
            // much the outermost label hangs past the end of its axis, how wide the colorbar
            // needs to be. Every one of them is a `Track.Content` size below, and every one of
            // them moves when the numbers move.
            // An axis whose labels are off keeps its marks, and an outward mark still takes
            // its length outside the plot.
            // What an axis without numbers still reserves: an outward tick's length.
            let marksOnly (cfg: AxisConfig) =
                match theme.TickDirection with
                | _ when not cfg.ShowTicks -> 0.0<px>
                | TickIn -> 0.0<px>
                | TickOut
                | TickInOut -> theme.TickLength

            // A reader who asked for Γ sees Γ: named ticks may be tilted by the ladder,
            // and are never thinned.
            let keepNamed (cfg: AxisConfig) (spec: AxisSpec) (plan: TickPlan) =
                match cfg.Ticks with
                | TicksNamed _ when plan.Kept.Length < spec.Ticks.Length ->
                    { plan with
                        Kept = Array.init spec.Ticks.Length id
                        Rule = plan.Rule + ", named ticks kept" }
                | _ -> plan

            // The second axis's plan, against the plot's height as the first's is.
            let planY2 (h: float<px>) =
                right |> Option.map (fun (cfg, spec) -> keepNamed cfg spec (Axes.planTicks m theme.TickFont theme.LabelGap h AxisRight spec.Ticks))

            // How far the annotations' text stands past each edge of a plot of this size,
            // in px, as left, right, top and bottom (zero where it does not).
            let annotationOverhang (probe: Extent<px>) : Rect<px> =
                let none: Rect<px> = { X0 = 0.0<px>; X1 = 0.0<px>; Y0 = 0.0<px>; Y1 = 0.0<px> }

                p.Layers
                |> Array.choose (fun l ->
                    let sx, sy = Erase.seriesScales l.Data

                    match Geom.project sx.Project sy.Project l.Geom with
                    | GeomAnnotation(text, placement, arrow) ->
                        let pts = Erase.projectXY l.Data

                        if pts.Length = 0 then
                            None
                        else
                            Geom.compileAnnotation p.Coord probe l.Style text placement arrow pts.[0]
                    | _ -> None)
                |> Array.collect (fun node ->
                    node
                    |> Scene.fold
                        (fun acc n ->
                            match n with
                            | Text t -> t :: acc
                            | _ -> acc)
                        []
                    |> Array.ofList)
                |> Array.fold
                    (fun (acc: Rect<px>) t ->
                        let b = TextMeasure.bounds m t.Font t.Anchor t.Baseline t.Rotation t.Text
                        let x = float t.At.X * probe.Width
                        let y = (1.0 - float t.At.Y) * probe.Height

                        { X0 = max acc.X0 (-(x + b.X0))
                          X1 = max acc.X1 (x + b.X1 - probe.Width)
                          Y0 = max acc.Y0 (-(y + b.Y0))
                          Y1 = max acc.Y1 (y + b.Y1 - probe.Height) })
                    none

            let gutters (xPlan: TickPlan) (yPlan: TickPlan) (barPlan: TickPlan option) (probe: Extent<px>) : PanelLayout =
                let legendScores = scores probe

                let x2Ticks, x2Title, x2Overhang =
                    match x2 with
                    | Some(_, spec) ->
                        let plan = Axes.planTicks m theme.TickFont theme.LabelGap probe.Width AxisTop spec.Ticks
                        Axes.tickExtent m theme AxisTop spec plan, Axes.titleExtent m theme spec.Title, Axes.labelOverhang m theme AxisTop spec plan
                    | None -> 0.0<px>, 0.0<px>, 0.0<px>

                let y2Ticks, y2Title, y2Overhang =
                    match right, planY2 probe.Height with
                    | Some(cfg, spec), Some plan when cfg.Visible ->
                        (if cfg.ShowTickLabels then Axes.tickExtent m theme AxisRight spec plan else marksOnly cfg),
                        (if cfg.ShowTitle then Axes.titleExtent m theme spec.Title else 0.0<px>),
                        (if cfg.ShowTickLabels then Axes.labelOverhang m theme AxisRight spec plan else 0.0<px>)
                    | _ -> 0.0<px>, 0.0<px>, 0.0<px>

                let outsideRight =
                    hasLegend && (p.Legend = LegendOutsideRight || (p.Legend = LegendBest && crowded legendScores))

                let outsideBelow = hasLegend && p.Legend = LegendOutsideBottom

                let g =
                  { TitleH = titleH
                    BarGap = barGap
                    LegendGap = (if outsideRight || outsideBelow then theme.PanelGap else 0.0<px>)
                    LegendW = (if outsideRight then legendSize.Width else 0.0<px>)
                    LegendH = (if outsideBelow then legendSize.Height else 0.0<px>)
                    LegendScores = legendScores

                    BarW =
                      match colorbar, barPlan with
                      | Some(spec, _), Some plan -> Colorbar.measure m theme spec plan
                      | _ -> 0.0<px>

                    XTicksH =
                      if not p.XAxis.Visible then 0.0<px>
                      elif p.XAxis.ShowTickLabels then Axes.tickExtent m theme AxisBottom xSpec xPlan
                      else marksOnly p.XAxis

                    YTicksW =
                      if not p.YAxis.Visible then 0.0<px>
                      elif p.YAxis.ShowTickLabels then Axes.tickExtent m theme AxisLeft ySpec yPlan
                      else marksOnly p.YAxis

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
                    // it is half of keeping labels inside bounds; without it the right-hand x
                    // label and the top y label are clipped by the figure edge.
                    // A second y axis's labels already stand where the last x label hangs, so
                    // only what still sticks out past them is reserved.
                    RightPad =
                      if p.XAxis.Visible && p.XAxis.ShowTickLabels then
                          max 0.0<px> (max (Axes.labelOverhang m theme AxisBottom xSpec xPlan) x2Overhang - y2Ticks - y2Title)
                      else
                          max 0.0<px> (x2Overhang - y2Ticks - y2Title)

                    // Likewise a secondary x axis's labels stand where the top y label hangs.
                    TopPad =
                      max
                          0.0<px>
                          (max
                              y2Overhang
                              (if p.YAxis.Visible && p.YAxis.ShowTickLabels then
                                   Axes.labelOverhang m theme AxisLeft ySpec yPlan
                               else
                                   0.0<px>)
                           - x2Ticks
                           - x2Title)

                    X2TicksH = x2Ticks
                    X2TitleH = x2Title

                    Y2TicksW = y2Ticks
                    Y2TitleW = y2Title
                    Y2Prefix = pinY2
                    X2Prefix = pinX2

                    Probe = probe
                    XPrefix = pinX
                    YPrefix = pinY
                    SlackLeft = 0.0<px>
                    SlackRight = 0.0<px>
                    SlackTop = 0.0<px>
                    SlackBottom = 0.0<px>
                    LeftPad = 0.0<px>
                    BottomPad = 0.0<px> }

                // An annotation's text may stand past the plot, and is not clipped, so the
                // gutter on that side grows until it holds it, over whatever the axis already
                // takes there.
                let past = annotationOverhang probe
                let rightOfPlot = g.Y2TicksW + g.Y2TitleW + g.BarGap + g.BarW + (if g.LegendW > 0.0<px> then g.LegendGap + g.LegendW else 0.0<px>)
                let belowPlot = g.XTicksH + g.XTitleH + (if g.LegendH > 0.0<px> then g.LegendGap + g.LegendH else 0.0<px>)

                { g with
                    LeftPad = max 0.0<px> (past.X0 - g.YTitleW - g.YTicksW)
                    RightPad = max g.RightPad (past.X1 - rightOfPlot)
                    TopPad = max g.TopPad (past.Y0 - g.TitleH - g.X2TitleH - g.X2TicksH)
                    BottomPad = max 0.0<px> (past.Y1 - belowPlot) }

            let build (g: PanelLayout) = innerGrid m extent region g

            // The bar's plan is made against its own height, which is the plot's — the bar
            // sits in the plot's row. That is why `Legend.fs`'s objection to outside placement
            // does not transfer: the width this adds is settled by a vertical measurement, so
            // nothing here is circular and the loop is still the same two passes.

            let plan (w: float<px>) (h: float<px>) =
                keepNamed p.XAxis xSpec (Axes.planTicks m theme.TickFont theme.LabelGap w AxisBottom xSpec.Ticks),
                keepNamed p.YAxis ySpec (Axes.planTicks m theme.TickFont theme.LabelGap h AxisLeft ySpec.Ticks),
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
                // The plan is *not* frozen, only the rectangle it is measured against. A
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
                // One Transform for the panel, one Clip inside it. `Scale.Forward` does not
                // clamp, so out-of-domain data projects outside [0,1] and this Clip is what removes
                // it. That is also why a backend without clipping still gets the common case right.
                // Marks that fell outside the panel, counted on the panel. A dropped
                // rule's layer is empty and is pruned with its key, so the count cannot live on it.
                let dropped = ref 0

                let data =
                    let layers =
                        p.Layers
                        |> Array.mapi (fun ordinal l ->
                            let pts = Erase.projectXY l.Data

                            // The geom's own arrays through the same scales as `pts`, at
                            // the same moment. Projected at construction they went stale as soon
                            // as `unify` or a stated domain moved the panel out from under them.
                            let geom =
                                let sx, sy = Erase.seriesScales l.Data
                                Geom.project sx.Project sy.Project l.Geom

                            let layerKey = Keys.layer panelKey ordinal

                            // The rows this layer was asked to name. `None` is the
                            // unsplit path and is what every figure that highlights nothing
                            // takes, which is why the golden baselines did not move.
                            let address =
                                if l.Addressed.Length = 0 then
                                    None
                                else
                                    Some(markKey l layerKey, l.Addressed)

                            // **One batch per bucket, not one per point.** With no channels
                            // this is `Geom.compile`'s own path; with a continuous colour channel
                            // it is at most 256 groups for any number of points, which is what
                            // keeps batching intact while the colour varies.
                            let body, counts =
                                match geom with
                                | GeomField(field, mark) ->
                                    // A field is drawn once, not once per colour
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
                                                    // `compileField` applies the opacity.
                                                    Some (Channel.restyle c.Map k l.Style).Color
                                        | None -> fun v -> if System.Double.IsNaN v || System.Double.IsInfinity v then None else Some l.Style.Color

                                    Geom.compileField p.Coord plotSize m theme.PanelBackground l.Style colourOf field mark, [||]
                                | GeomRule rule ->
                                    // Laid across the panel's final domain. One that misses the
                                    // panel is not drawn and says so: what is dropped is counted.
                                    match Geom.compileRule p.Coord l.Style rule with
                                    | Some node -> node, [||]
                                    | None ->
                                        dropped.Value <- dropped.Value + 1
                                        Scene.empty, [||]
                                | GeomAnnotation(text, placement, arrow) ->
                                    // And an annotation whose point is off the panel.
                                    match Geom.compileAnnotation p.Coord plotSize l.Style text placement arrow pts.[0] with
                                    | Some node -> node, [||]
                                    | None ->
                                        dropped.Value <- dropped.Value + 1
                                        Scene.empty, [||]
                                | _ ->
                                    if l.Channels.Length = 0 then
                                        // No channels, no counts: an unchannelled layer's
                                        // annotation is exactly what it was before channels, so
                                        // every baseline that predates channels stays
                                        // byte-identical.
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

                            // The layer's **ordinal**, not its name. `l.Name` is written
                            // into the `Annotated` node just below because that is where a
                            // human-readable label belongs; it is kept out of the key because it
                            // is caller-supplied, is empty until `Chart.withName` is called, and
                            // may be the same on two layers of one panel. The ordinal is unique
                            // and survives a restyle, a rename and a change of data.
                            Keyed(
                                layerKey,
                                Annotated(
                                    { Role =
                                        // A rule marks the axes rather than carrying data.
                                        (match l.Geom with
                                         | GeomRule _
                                         | GeomAnnotation _ -> RoleAnnotation
                                         | _ -> RoleSeries)
                                      Label = (if isEmpty l.Name then "series" else l.Name)
                                      Data = Array.append [| "series", l.Name; "points", string pts.Length |] counts },
                                    body
                                )
                            ))

                    // An annotation's point is in the panel (one outside is dropped above),
                    // but its text and arrow may reach past the plot area, as matplotlib's do, so
                    // annotations are drawn after the data and outside its clip. A panel without
                    // one keeps the single clipped group it has always had.
                    let isAnnotation (l: Layer) =
                        match l.Geom with
                        | GeomAnnotation _ -> true
                        | _ -> false

                    let free = Array.zip p.Layers layers |> Array.filter (fst >> isAnnotation) |> Array.map snd

                    if free.Length = 0 then
                        Transform(Axes.panelTransform plotNd, Clip(Axes.panelShape p.Coord, Group layers))
                    else
                        let clipped = Array.zip p.Layers layers |> Array.filter (fst >> isAnnotation >> not) |> Array.map snd

                        Group
                            [| Transform(Axes.panelTransform plotNd, Clip(Axes.panelShape p.Coord, Group clipped))
                               Transform(Axes.panelTransform plotNd, Group free) |]

                // ---- axes -----------------------------------------------------------------------
                let axisNodeIn (theme: Theme) (mirrored: bool) (side: AxisSide) (cfg: AxisConfig) (spec: AxisSpec) (tickPlan: TickPlan) =
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
                              ShowTicks = cfg.ShowTicks
                              ShowLabels = cfg.ShowTickLabels
                              ShowTitle = cfg.ShowTitle
                              TitleOffset = Axes.tickExtent m theme side spec tickPlan + theme.TitleGap
                              Direction = theme.TickDirection
                              // Mirrored onto the frame, so only where a frame is drawn, and only
                              // around a rectangle.
                              Mirror = mirrored && theme.MirrorTicks && theme.PanelFrame && Coord.isIdentity p.Coord
                              ShowMinor =
                                match cfg.Minor with
                                | MinorOff -> false
                                | MinorAuto -> theme.MinorTicks
                                | MinorEvery _
                                | MinorCount _ -> true }

                // On a panel with two y axes, each takes the colour of its series when it
                // has exactly one, so the reader can tell which curve is read against which side
                // without a legend. With several on a side, that side keeps the axis colour.
                let tint (layers: Layer[]) =
                    match y2, layers |> Array.filter (fun l -> fst l.InDomain || snd l.InDomain) with
                    | Some _, [| l |] -> { theme with AxisColor = l.Style.Color; Foreground = l.Style.Color }
                    | _ -> theme

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

                // The legend where it was put: a corner, outside, or the corner the frozen
                // scores make least covered.
                let legend =
                    let framed = defaultArg p.LegendFrame theme.ShowLegendFrame
                    let draw (box: Rect<px>) = Legend.compileIn m theme extent box p.LegendColumns framed entries

                    // Through nd and back, as the legend has always been placed: the round trip is
                    // a last-bit change against `plotPx`, and the baselines record it.
                    let plotPx = Layout.pxRect extent plotNd

                    let at (x0: float<px>) (y0: float<px>) : Rect<px> =
                        { X0 = x0; Y0 = y0; X1 = x0 + legendSize.Width; Y1 = y0 + legendSize.Height }

                    let placement = PanelLayout.legendPlacement p.Legend layout

                    if not hasLegend then
                        Scene.empty
                    else
                        match placement with
                        | LegendNone
                        | LegendBest -> Scene.empty
                        | LegendOutsideRight ->
                            match Layout.tryFindPx "legend" arrangement with
                            | Some cell when layout.LegendW > 0.0<px> -> draw (at cell.X0 plotPx.Y0)
                            | _ -> draw (Legend.cornerBox theme plotPx legendSize LegendTopRight)
                        | LegendOutsideBottom ->
                            match Layout.tryFindPx "legendb" arrangement with
                            | Some cell when layout.LegendH > 0.0<px> ->
                                draw (at ((plotPx.X0 + plotPx.X1 - legendSize.Width) / 2.0) cell.Y0)
                            | _ -> draw (Legend.cornerBox theme plotPx legendSize LegendBottomRight)
                        | corner -> draw (Legend.cornerBox theme plotPx legendSize corner)

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
                               "y-tick-rule", yPlan.Rule
                               // Written only when there is something to say, so that every
                               // figure without a dropped mark keeps its baseline.
                               if dropped.Value > 0 then
                                   "dropped-marks", string dropped.Value |] },
                        // Everything but the background, the frame and the title is
                        // keyed below the panel, so a change to one part replaces that part.
                        // An empty part is pruned with its key, so a panel with no legend
                        // writes no empty element for one.
                        Scene.prune (
                            Group
                                [| Axes.panelBackground theme p.Coord plotNd
                                   Keyed(Keys.grid panelKey, Axes.gridlines theme p.Coord plotNd gridX gridY)
                                   data
                                   Keyed(Keys.axisX panelKey, axisNodeIn theme x2.IsNone AxisBottom p.XAxis xSpec xPlan)
                                   Keyed(Keys.axisY panelKey, axisNodeIn (tint (primaryLayers p)) right.IsNone AxisLeft p.YAxis ySpec yPlan)
                                   match right, planY2 layout.Probe.Height with
                                   | Some(cfg, spec), Some plan ->
                                       Keyed(Keys.axisY2 panelKey, axisNodeIn (tint (p.Layers |> Array.filter (onSecond p))) false AxisRight cfg spec plan)
                                   | _ -> ()
                                   match x2 with
                                   | Some(cfg, spec) ->
                                       let plan = Axes.planTicks m theme.TickFont theme.LabelGap layout.Probe.Width AxisTop spec.Ticks
                                       Keyed(Keys.axisX2 panelKey, axisNodeIn theme false AxisTop cfg spec plan)
                                   | None -> ()
                                   Axes.panelFrame theme p.Coord plotNd
                                   panelTitle
                                   Keyed(Keys.legend panelKey, legend)
                                   Keyed(Keys.colorbar panelKey, colorbarNode) |]
                        )
                    )
                )

            // What this panel's own labels need inside the rectangle it was given. With
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

    /// A panel at its own cell, keyed by it.
    let private compilePanel m theme extent region frozen pins letterH (p: Panel) =
        compilePanelKeyed (Keys.panel p.Row p.Col) m theme extent region frozen pins letterH p

    /// Every panel's legend entries, once each, for a figure's shared legend.
    let private figureEntries (fig: Figure) =
        fig.Panels |> Array.collect (legendEntries fig.Theme) |> Array.distinct

    /// The shared legend, when there is one and it has something to say.
    let private sharedLegend (fig: Figure) =
        match fig.Legend with
        | Some placement when (figureEntries fig).Length > 0 -> Some placement
        | _ -> None

    /// A panel as it is drawn: without its own legend when the figure has a shared one.
    let private ownLegend (fig: Figure) (p: Panel) =
        if fig.Legend.IsSome then { p with Legend = LegendNone } else p

    /// A row's or column's weight, 1 where none is stated.
    let private weightOf (weights: float[]) (i: int) = if i < weights.Length then weights.[i] else 1.0

    /// The figure's own outer grid: one content row for the head, then one row of tracks per
    /// panel row. Split out of `toScene` so that `arrangeFor` and the renderer cannot be looking at
    /// two different grids, as they once were. See `Shot`.
    ///
    /// `head` overrides the measured head height when it is `Some`, which is how the union takes
    /// the tallest head of the shot rather than each frame's own.
    let private outerGrid (head: float<px> option) (fig: Figure) =
        let theme = fig.Theme
        let rows = max 1 fig.Rows
        let cols = max 1 fig.Cols
        let shared = sharedLegend fig

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

        // A shared legend is one more column, or one more row, of the figure's own grid.
        // A figure without one keeps the grid it had: a track of zero would still take a gap.
        let trackRows =
            Array.concat
                [ [| Track.Content 0.0<px> |]
                  Array.init rows (fun i -> Track.Frac(weightOf fig.RowWeights i))
                  (if shared = Some FigureLegendBottom then [| Track.Content 0.0<px> |] else [||]) ]

        let trackCols =
            Array.append (Array.init cols (fun i -> Track.Frac(weightOf fig.ColWeights i))) (if shared = Some FigureLegendRight then [| Track.Content 0.0<px> |] else [||])

        let legendSize (mm: ITextMeasurer) = Legend.measure mm theme (figureEntries fig)

        let cells =
            [| yield Layout.cell "head" 0 0 (fun mm -> { Width = 0.0<px>; Height = headHeight mm })
                   |> Layout.spanning 1 cols

               for p in fig.Panels do
                   yield
                       Layout.fill (sprintf "panel-%d-%d" p.Row p.Col) (1 + max 0 p.Row) (max 0 p.Col)
                       |> Layout.spanning p.RowSpan p.ColSpan

               match shared with
               | Some FigureLegendRight -> yield Layout.cell "flegend" 1 cols legendSize |> Layout.spanning rows 1
               | Some FigureLegendBottom -> yield Layout.cell "flegend" (1 + rows) 0 legendSize |> Layout.spanning 1 cols
               | None -> () |]

        Layout.grid trackRows trackCols cells
        |> Layout.withGaps theme.PanelGap theme.PanelGap
        |> Layout.withPadding theme.FigurePadding

    /// Whether a figure's panels have anything to line up with: more than one cell.
    let private isGrid (fig: Figure) = max 1 fig.Rows * max 1 fig.Cols > 1

    /// **The panels lined up.** Every panel on one grid line is given the gutters of the
    /// widest one there, as slack at its outer edge: the left gutters of the panels in a column
    /// all add up to the column's largest, and so on for right, top and bottom. It is the shot's
    /// union again, componentwise and taken along a grid line instead of over frames, and it
    /// is what puts the plot rectangles of a column at one x and of a row at one y.
    ///
    /// Returns the gutter sizes per grid line with the slackened layouts: the tracks of
    /// `alignedGrid` are those sizes.
    let private align (fig: Figure) (layouts: (string * PanelLayout)[]) =
        let rows = max 1 fig.Rows
        let cols = max 1 fig.Cols
        let fresh (l: PanelLayout) = { l with SlackLeft = 0.0<px>; SlackRight = 0.0<px>; SlackTop = 0.0<px>; SlackBottom = 0.0<px> }

        let placed =
            fig.Panels
            |> Array.choose (fun p ->
                let key = sprintf "panel-%d-%d" p.Row p.Col

                layouts
                |> Array.tryPick (fun (k, l) -> if k = key then Some(key, p, fresh l) else None))
            |> Array.distinctBy (fun (k, _, _) -> k)

        let line (count: int) (at: Panel -> int) (size: PanelLayout -> float<px>) =
            Array.init count (fun i ->
                placed
                |> Array.filter (fun (_, p, _) -> at p = i)
                |> Array.fold (fun acc (_, _, l) -> max acc (size l)) 0.0<px>)

        // A spanning panel's gutters sit on the outer lines of its span: its top on its
        // first row's, its bottom on its last row's.
        let lastRow (p: Panel) = min (rows - 1) (max 0 p.Row + max 1 p.RowSpan - 1)
        let lastCol (p: Panel) = min (cols - 1) (max 0 p.Col + max 1 p.ColSpan - 1)
        let lefts = line cols (fun p -> max 0 p.Col) PanelLayout.left
        let rights = line cols lastCol PanelLayout.right
        let tops = line rows (fun p -> max 0 p.Row) PanelLayout.top
        let bottoms = line rows lastRow PanelLayout.bottom

        let slackened =
            placed
            |> Array.map (fun (key, p, l) ->
                key,
                { l with
                    SlackLeft = lefts.[max 0 p.Col] - PanelLayout.left l
                    SlackRight = rights.[lastCol p] - PanelLayout.right l
                    SlackTop = tops.[max 0 p.Row] - PanelLayout.top l
                    SlackBottom = bottoms.[lastRow p] - PanelLayout.bottom l })

        (lefts, rights, tops, bottoms), slackened

    /// The outer grid once the gutters are known: each panel row is three tracks, its top
    /// gutters, its plot and its bottom gutters, and each panel column three likewise, so a
    /// panel's region is the span of its three and its plot lands on the middle one. The gaps
    /// between panels become tracks of their own, since `Layout`'s gap would also fall between a
    /// gutter and its plot.
    let private alignedGrid (head: float<px> option) (fig: Figure) (lefts: float<px>[], rights: float<px>[], tops: float<px>[], bottoms: float<px>[]) =
        let theme = fig.Theme
        let rows = max 1 fig.Rows
        let cols = max 1 fig.Cols
        let shared = sharedLegend fig
        let gap = Track.Fixed theme.PanelGap

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

        let lanes (count: int) (weights: float[]) (before: float<px>[]) (after: float<px>[]) =
            [| for i in 0 .. count - 1 do
                   if i > 0 then yield gap
                   yield Track.Fixed before.[i]
                   yield Track.Frac(weightOf weights i)
                   yield Track.Fixed after.[i] |]

        // Row tracks: the head and its gap, then 4 per panel row less the last gap; column tracks
        // 4 per panel column less the last gap. A panel at (r, c) starts at row 2 + 4r, column 4c.
        let trackRows =
            Array.concat
                [ [| Track.Content 0.0<px>; gap |]
                  lanes rows fig.RowWeights tops bottoms
                  (if shared = Some FigureLegendBottom then [| gap; Track.Content 0.0<px> |] else [||]) ]

        let trackCols =
            Array.append (lanes cols fig.ColWeights lefts rights) (if shared = Some FigureLegendRight then [| gap; Track.Content 0.0<px> |] else [||])

        let legendSize (mm: ITextMeasurer) = Legend.measure mm theme (figureEntries fig)
        let panelRows = 4 * rows - 1
        let panelCols = 4 * cols - 1

        let cells =
            [| yield Layout.cell "head" 0 0 (fun mm -> { Width = 0.0<px>; Height = headHeight mm })
                   |> Layout.spanning 1 panelCols

               for p in fig.Panels do
                   yield
                       Layout.fill (sprintf "panel-%d-%d" p.Row p.Col) (2 + 4 * max 0 p.Row) (4 * max 0 p.Col)
                       |> Layout.spanning (4 * max 1 p.RowSpan - 1) (4 * max 1 p.ColSpan - 1)

               match shared with
               | Some FigureLegendRight -> yield Layout.cell "flegend" 2 (panelCols + 1) legendSize |> Layout.spanning panelRows 1
               | Some FigureLegendBottom -> yield Layout.cell "flegend" (2 + panelRows + 1) 0 legendSize |> Layout.spanning 1 panelCols
               | None -> () |]

        Layout.grid trackRows trackCols cells |> Layout.withPadding theme.FigurePadding

    /// **The plot held to its shape**: a panel that asks for one has its plot shrunk to it
    /// inside its region and centred, as slack on the two sides that give way. Applied last, after
    /// the panels are lined up, so a neighbour keeps its rectangle and only this panel's plot moves
    /// out of line. The probe becomes the plot's exact size, which `fitAspect` holds each frame to.
    let private withAspects
        (outer: Arrangement)
        (ratioOf: string -> float option)
        (rescore: string -> Rect<px> -> PanelLayout -> float[])
        (layouts: (string * PanelLayout)[])
        : (string * PanelLayout)[] =
        layouts
        |> Array.map (fun (key, l) ->
            match ratioOf key, Layout.tryFindPx key outer with
            | Some r, Some region ->
                let w = Rect.width region - PanelLayout.left l - PanelLayout.right l
                let h = Rect.height region - PanelLayout.top l - PanelLayout.bottom l

                if w <= 0.0<px> || h <= 0.0<px> then
                    key, l
                else
                    let shaped =
                        if h > r * w then
                            let d = (h - r * w) / 2.0
                            { l with SlackTop = l.SlackTop + d; SlackBottom = l.SlackBottom + d; Probe = { Width = w; Height = r * w } }
                        else
                            let d = (w - h / r) / 2.0
                            { l with SlackLeft = l.SlackLeft + d; SlackRight = l.SlackRight + d; Probe = { Width = h / r; Height = h } }

                    key, (if shaped.LegendScores.Length = 4 then { shaped with LegendScores = rescore key region shaped } else shaped)
            | _ -> key, l)

    // ---- Insets ------------------------------------------------------------------------

    /// An inset's name in a shot: its host's, and its index.
    let private insetKey (p: Panel) (i: int) = sprintf "panel-%d-%d/inset-%d" p.Row p.Col i

    /// An inset's plot on the surface, from the host's plot and the fractions it was placed at.
    let private insetRect (hostPlot: Rect<px>) (at: Rect<nd>) : Rect<px> =
        let w = Rect.width hostPlot
        let h = Rect.height hostPlot

        { X0 = hostPlot.X0 + float at.X0 * w
          X1 = hostPlot.X0 + float at.X1 * w
          Y0 = hostPlot.Y1 - float at.Y1 * h
          Y1 = hostPlot.Y1 - float at.Y0 * h }

    /// The region an inset is compiled in: its plot with its gutters round it, so that its inner
    /// grid puts the plot exactly where it was placed.
    let private insetRegion (rect: Rect<px>) (l: PanelLayout) : Rect<px> =
        { X0 = rect.X0 - PanelLayout.left l
          X1 = rect.X1 + PanelLayout.right l
          Y0 = rect.Y0 - PanelLayout.top l
          Y1 = rect.Y1 + PanelLayout.bottom l }

    /// An inset's gutters, measured at the plot it will have.
    let private measureInset (m: ITextMeasurer) (theme: Theme) (extent: Extent<px>) (key: Key) (rect: Rect<px>) (ip: Panel) : PanelLayout =
        let first, _, _ = compilePanelKeyed key m theme extent rect None (None, None, None, None) 0.0<px> ip
        let probe: Extent<px> = { Width = Rect.width rect; Height = Rect.height rect }
        let _, needed, _ = compilePanelKeyed key m theme extent (insetRegion rect first) (Some { first with Probe = probe }) (None, None, None, None) 0.0<px> ip
        let l = needed ()

        if l.BarW > 0.0<px> || l.LegendW > 0.0<px> || l.LegendH > 0.0<px> then
            failwith "Marey: an inset draws no colorbar and no legend outside itself; give it an inside corner, or no colour scale."

        { l with Probe = probe }

    /// Every inset's layout, measured over its host's frozen plot.
    let private insetLayouts (m: ITextMeasurer) (extent: Extent<px>) (fig: Figure) (outer: Arrangement) (hosts: (string * PanelLayout)[]) =
        [| for p in fig.Panels do
               if p.Insets.Length > 0 && p.Layers.Length > 0 then
                   let key = sprintf "panel-%d-%d" p.Row p.Col

                   match Layout.tryFindPx key outer, hosts |> Array.tryPick (fun (k, l) -> if k = key then Some l else None) with
                   | Some region, Some host ->
                       let hostPlot = Layout.findPx "plot" (innerGrid m extent region host)

                       for i in 0 .. p.Insets.Length - 1 do
                           let inset = p.Insets.[i]

                           if inset.Panel.Layers.Length > 0 then
                               yield
                                   insetKey p i,
                                   measureInset m fig.Theme extent (Keys.inset (Keys.panel p.Row p.Col) i) (insetRect hostPlot inset.At) (unify inset.Panel)
                   | _ -> () |]

    /// The inset's domain on its host, as a rectangle, and two lines from its corners to
    /// the inset's nearest ones. Both must be cartesian, with the inset's axes in the host's
    /// dimensions.
    let private indicator (theme: Theme) (extent: Extent<px>) (host: Panel) (hostPlot: Rect<px>) (inset: Panel) (rect: Rect<px>) : Node =
        if not (Coord.isIdentity host.Coord && Coord.isIdentity inset.Coord) then
            failwith "Marey: an inset's zoom is drawn on cartesian panels only."

        let hx, hy = axisScalesErased host
        let ix, iy = axisScalesErased inset

        if (hx.Stated && ix.Stated && hx.Dim <> ix.Dim) || (hy.Stated && iy.Stated && hy.Dim <> iy.Dim) then
            failwith "Marey: an inset's zoom needs the inset's axes in the host's dimensions."

        // The inset's domain, through each scale's SI, into the host's numbers.
        let toHost (h: IScaleErased) (i: IScaleErased) (v: float) = (v * i.ToSI + i.OffsetSI - h.OffsetSI) / h.ToSI
        let (x0, x1), (y0, y1) = ix.Domain, iy.Domain
        let px (v: float) = hostPlot.X0 + float (hx.Project(toHost hx ix v)) * Rect.width hostPlot
        let py (v: float) = hostPlot.Y1 - float (hy.Project(toHost hy iy v)) * Rect.height hostPlot

        let source: Rect<px> =
            { X0 = min (px x0) (px x1)
              X1 = max (px x0) (px x1)
              Y0 = min (py y0) (py y1)
              Y1 = max (py y0) (py y1) }

        let corners (r: Rect<px>) : Vec2<px>[] =
            [| { X = r.X0; Y = r.Y0 }; { X = r.X1; Y = r.Y0 }; { X = r.X1; Y = r.Y1 }; { X = r.X0; Y = r.Y1 } |]

        let length (a: Vec2<px>) (b: Vec2<px>) = sqrt (float ((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)))

        // The joins are like corner to like corner, and only those that cross neither rectangle,
        // so the lines frame the zoom rather than strike through it.
        let inside (r: Rect<px>) (v: Vec2<px>) =
            let e = 1e-6<px>
            v.X > r.X0 + e && v.X < r.X1 - e && v.Y > r.Y0 + e && v.Y < r.Y1 - e

        let crosses (a: Vec2<px>, b: Vec2<px>) =
            [ 1..63 ]
            |> List.exists (fun k ->
                let t = float k / 64.0
                let v: Vec2<px> = { X = a.X + t * (b.X - a.X); Y = a.Y + t * (b.Y - a.Y) }
                inside source v || inside rect v)

        let pairs = Array.zip (corners source) (corners rect) |> Array.sortBy (fun (a, b) -> length a b)

        let joins =
            match pairs |> Array.filter (crosses >> not) with
            | clear when clear.Length >= 2 -> Array.take 2 clear
            | _ -> Array.take 2 pairs

        let stroke = Stroke.solid theme.AxisColor theme.AxisWidth
        let nd (v: Vec2<px>) = Layout.ndPoint extent v

        Annotated(
            { Role = RoleAnnotation
              Label = "inset zoom"
              Data = [||] },
            Group
                [| yield Scene.rect (Some stroke) None (Layout.ndRect extent source)
                   for a, b in joins do
                       yield Scene.polyline stroke [| nd a; nd b |] |]
        )

    /// Least squares by the normal equations, solved by elimination with partial pivoting. The
    /// systems here are a few grid lines wide.
    let private leastSquares (a: float[][]) (b: float[]) : float[] option =
        let n = a.[0].Length

        let m =
            Array.init n (fun i ->
                Array.init (n + 1) (fun j ->
                    if j < n then
                        a |> Array.sumBy (fun r -> r.[i] * r.[j])
                    else
                        Array.fold2 (fun t (r: float[]) bk -> t + r.[i] * bk) 0.0 a b))

        let rec eliminate k =
            if k = n then
                true
            else
                let p = seq { k .. n - 1 } |> Seq.maxBy (fun i -> abs m.[i].[k])

                if abs m.[p].[k] < 1e-12 then
                    false
                else
                    let t = m.[k]
                    m.[k] <- m.[p]
                    m.[p] <- t

                    for i in k + 1 .. n - 1 do
                        let f = m.[i].[k] / m.[k].[k]

                        for j in k .. n do
                            m.[i].[j] <- m.[i].[j] - f * m.[k].[j]

                    eliminate (k + 1)

        if not (eliminate 0) then
            None
        else
            let x = Array.zeroCreate n

            for i in n - 1 .. -1 .. 0 do
                let mutable t = m.[i].[n]

                for j in i + 1 .. n - 1 do
                    t <- t - m.[i].[j] * x.[j]

                x.[i] <- t / m.[i].[i]

            Some x

    /// **Where each grid line falls, so the plots keep their stated proportions.** In the
    /// grid, a line that only one side of a `beside` has still takes its gutters and gap across the
    /// whole figure, inside the other side's panel that spans it, and that panel's plot grows by
    /// them: 3 : 1 beside a stack of three drew 1.70 : 1. Here the lines are placed directly
    /// instead: each line `l` of `1 .. count - 1` is the edge where the regions ending on it end
    /// (the regions starting on it begin a gap later), so the panels on one line still share it
    /// and stay lined up, while two lines of different sides are free to overlap. Solved
    /// so that along every crossing line each panel's plot is its stated share times one length,
    /// with a tiny pull towards where the grid put each line, which settles a line nothing
    /// constrains.
    ///
    /// `spans` is each panel's (first row, rows, first column, columns) in the axis solved;
    /// `before` and `after` the lined-up gutters per row. Returns the line positions with the two
    /// outer ones, or `None` when nothing spans a line or the gutters leave a plot no room.
    let private placeLines
        (count: int)
        (crossCount: int)
        (weights: float[])
        (gap: float)
        (current: float[])
        (before: float[])
        (after: float[])
        (spans: (int * int * int * int)[])
        : float[] option =
        if count < 2 || spans |> Array.forall (fun (_, len, _, _) -> len <= 1) then
            None
        else
            let stated = Array.init count (weightOf weights)
            let unknowns = count - 1 + crossCount
            let line l = l - 1
            let length c = count - 1 + c
            let pull = 1e-4
            let start = current.[0]
            let finish = current.[count]

            // A panel's plot as coefficients over the unknowns, and the constant beside them.
            let plot (first: int) (len: int) =
                let row = Array.zeroCreate unknowns
                let last = first + len
                let mutable constant = -after.[last - 1] - before.[first]

                if last = count then constant <- constant + finish else row.[line last] <- 1.0

                if first = 0 then
                    constant <- constant - start
                else
                    row.[line first] <- row.[line first] - 1.0
                    constant <- constant - gap

                row, constant

            let equations =
                [| for first, len, cross, crossLen in spans do
                       let share = Array.sum stated.[first .. first + len - 1]

                       for c in cross .. min (crossCount - 1) (cross + crossLen - 1) do
                           let row, constant = plot first len
                           row.[length c] <- -share
                           yield row, -constant

                   for l in 1 .. count - 1 do
                       yield Array.init unknowns (fun j -> if j = line l then pull else 0.0), pull * current.[l]

                   for c in 0 .. crossCount - 1 do
                       yield Array.init unknowns (fun j -> if j = length c then pull else 0.0), pull * (finish - start) / Array.sum stated |]

            leastSquares (Array.map fst equations) (Array.map snd equations)
            |> Option.bind (fun x ->
                let lines = Array.init (count + 1) (fun l -> if l = 0 then start elif l = count then finish else x.[line l])

                let fits =
                    spans
                    |> Array.forall (fun (first, len, _, _) ->
                        let row, constant = plot first len
                        Array.fold2 (fun t a v -> t + a * v) constant row x > 0.0)

                let moved = Seq.exists (fun l -> abs (lines.[l] - current.[l]) > 1e-6) (seq { 1 .. count - 1 })
                if fits && moved then Some lines else None)

    /// The panels' regions moved onto the lines `placeLines` found, rows then columns. The
    /// head and a shared legend keep the places the grid gave them.
    let private placeProportionally (fig: Figure) (outer: Arrangement) (lefts: float<px>[], rights: float<px>[], tops: float<px>[], bottoms: float<px>[]) =
        let rows = max 1 fig.Rows
        let cols = max 1 fig.Cols
        let gap = float fig.Theme.PanelGap
        let floats (a: float<px>[]) = a |> Array.map float

        // Where each line's regions end, read off the tracks: rows start after the head and its
        // gap, and a lane is its gutter, its plot, its other gutter and a gap.
        let ends (origin: float<px>) (sizes: float<px>[]) (lead: int) (count: int) =
            let at = Array.scan (+) origin sizes |> floats
            Array.init (count + 1) (fun l -> if l = 0 then at.[lead] else at.[lead + 4 * l - 1])

        let spans = fig.Panels |> Array.map (fun p -> max 0 p.Row, max 1 p.RowSpan, max 0 p.Col, max 1 p.ColSpan)
        let padded = fig.Theme.FigurePadding

        let rowLines =
            placeLines rows cols fig.RowWeights gap (ends (outer.Region.Y0 + padded) outer.RowSizes 2 rows) (floats tops) (floats bottoms) spans

        let colLines =
            placeLines cols rows fig.ColWeights gap (ends (outer.Region.X0 + padded) outer.ColSizes 0 cols) (floats lefts) (floats rights) (spans |> Array.map (fun (r, rs, c, cs) -> c, cs, r, rs))

        match rowLines, colLines with
        | None, None -> outer
        | _ ->
            let edge (lines: float[] option) (first: int) (len: int) (lo: float<px>) (hi: float<px>) =
                match lines with
                | Some l -> (if first = 0 then l.[0] else l.[first] + gap) * 1.0<px>, l.[first + len] * 1.0<px>
                | None -> lo, hi

            let byKey = fig.Panels |> Array.map (fun p -> sprintf "panel-%d-%d" p.Row p.Col, p)

            let placements =
                outer.Placements
                |> Array.map (fun (key, r) ->
                    match byKey |> Array.tryFind (fun (k, _) -> k = key) with
                    | Some(_, p) ->
                        let y0, y1 = edge rowLines (max 0 p.Row) (max 1 p.RowSpan) r.Y0 r.Y1
                        let x0, x1 = edge colLines (max 0 p.Col) (max 1 p.ColSpan) r.X0 r.X1
                        key, { X0 = x0; X1 = x1; Y0 = y0; Y1 = y1 }
                    | None -> key, r)

            { outer with
                Placements = placements
                Regions = placements |> Array.map (fun (k, r) -> k, Layout.ndRect outer.Extent r) }

    /// Pass 2: the measured layouts lined up, the outer grid rebuilt around their gutters,
    /// and each panel's probe set to the plot it now has. A figure of one panel has nothing to
    /// line up with and keeps pass 1 exactly.
    ///
    /// `rescore` gives a `LegendBest` panel its corner scores again over the plot it now has
    ///: a panel that gave up width to line up with a neighbour's outside legend has a
    /// different emptiest corner. The gutters are not re-measured, so whether the legend went
    /// outside, which is a gutter, stays as pass 1 decided it.
    let private placeAligned
        (m: ITextMeasurer)
        (extent: Extent<px>)
        (head: float<px> option)
        (fig: Figure)
        (outer: Arrangement)
        (rescore: string -> Rect<px> -> PanelLayout -> float[])
        (layouts: (string * PanelLayout)[])
        : Arrangement * (string * PanelLayout)[] =
        if not (isGrid fig) then
            outer, layouts
        else
            let gutters, slackened = align fig layouts
            let outer2 = Layout.arrange m extent (alignedGrid head fig gutters) |> fun o -> placeProportionally fig o gutters

            let probed =
                slackened
                |> Array.map (fun (key, l) ->
                    match Layout.tryFindPx key outer2 with
                    | Some r ->
                        let l2 =
                            { l with
                                Probe =
                                    { Width = max 0.0<px> (Rect.width r - PanelLayout.left l - PanelLayout.right l)
                                      Height = max 0.0<px> (Rect.height r - PanelLayout.top l - PanelLayout.bottom l) } }

                        key, (if l2.LegendScores.Length = 4 then { l2 with LegendScores = rescore key r l2 } else l2)
                    | None -> key, l)

            outer2, probed

    // ---- What a shot pins besides the gutters -------------------------------------------
    //
    // A frame normalises two things to its own data: a continuous colour channel's domain is the
    // range of its values, and a quiver's length scale is set against its largest magnitude.
    // Across a film both renormalise every frame, so a field that grows tenfold looks the same
    // throughout. A shot already builds every sampled state, so the union of their domains, and the
    // largest of their quiver magnitudes, cost nothing more to take, and they are pinned here in
    // the way a prefix is pinned.

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
    /// This is a shot's cheap half, public as `Chart.arrangeFor`, because `Chart`'s surface had no
    /// way to say "lay out once and draw many times": `toScene` computed its arrangement internally
    /// and `arrangement` computed a *second* one for inspection. Both are now this function.
    let internal arrangeFor (m: ITextMeasurer) (extent: Extent<px>) (fig: Figure) : Shot =
        let fig = prepare fig
        let outer = Layout.arrange m extent (outerGrid None fig)

        let panels =
            fig.Panels
            |> Array.choose (fun p ->
                let key = sprintf "panel-%d-%d" p.Row p.Col

                match Layout.tryFindPx key outer with
                | None -> None
                | Some region ->
                    let layout, _, _ = compilePanel m fig.Theme extent region None (None, None, None, None) (letterHeight m fig) (unify (ownLegend fig p))
                    Some(key, layout))

        let rescore (key: string) (region: Rect<px>) (l: PanelLayout) =
            fig.Panels
            |> Array.filter (fun p -> sprintf "panel-%d-%d" p.Row p.Col = key)
            |> Array.map (fun p ->
                let _, needed, _ = compilePanel m fig.Theme extent region (Some l) (None, None, None, None) (letterHeight m fig) (unify (ownLegend fig p))
                (needed ()).LegendScores)
            |> Array.filter (fun s -> s.Length = 4)
            |> Array.fold (fun acc s -> if acc = [||] then s else Array.map2 max acc s) [||]
            |> fun s -> if s.Length = 4 then s else l.LegendScores

        let outer, panels = placeAligned m extent None fig outer rescore panels

        let ratioOf (key: string) =
            fig.Panels
            |> Array.filter (fun p -> sprintf "panel-%d-%d" p.Row p.Col = key)
            |> Array.map (fun p -> unify p)
            |> aspectOf

        let panels = withAspects outer ratioOf rescore panels
        let panels = Array.append panels (insetLayouts m extent fig outer panels)

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

    /// **One arrangement over states a caller lists.** `arrangeUnion` is this over samples
    /// of time; a widget calls it over the states its controls can reach (their extremes), which
    /// it knows without running anything, because the controls are known before the view runs.
    ///
    /// Everything `arrangeUnion` says about the union holds: componentwise, the prefixes pinned
    /// from the union domain, the colour domains and quiver keys held across the states.
    /// `Samples` reports how many states there were.
    let internal arrangeOver (m: ITextMeasurer) (extent: Extent<px>) (raw: Figure[]) : Shot =
        if raw.Length = 0 then
            invalidArg "raw" "Marey: a shot needs at least one state."

        let raw = raw |> Array.map prepare
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
                    |> Option.map (fun region -> key, (fig, region, unify (ownLegend fig p)))))

        // The prefix, chosen once from the union of every frame's domain rather than per
        // frame from its own. This is the same mechanism as the gutters and not a second one:
        // which decade a prefix comes from is a property of "the numbers", and across a
        // shot the numbers are all of them.
        //
        // **Chosen before anything is measured.** Each state
        // used to be measured under its own prefix and drawn under the pinned one, so a state
        // from the low end of a decade-crossing zoom was measured as `0 2 4 6 8` and drawn as
        // `0.002 … 0.008`, in a gutter sized for the shorter labels. The rectangle held still,
        // which is what a test checked, and the labels did not fit in it, which nothing checked.
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

                // A state whose panel is one constant contributes the constant and not its
                // padding, by the rule `agreeOn` uses across layers: a wave that starts flat at
                // zero is padded to ±1 m in its first frame, and that is not a number of the shot.
                let unionOver (panels: Panel[]) (packedOf: Panel -> PackedScale) (pointOf: Panel -> float option) =
                    if panels.Length = 0 then
                        None
                    else
                        panels
                        |> Array.map (fun p -> Axis.domainOfPacked (packedOf p), pointOf p)
                        |> Scale.shareDomain autoPadding
                        |> Some

                let pinOver (panels: Panel[]) (packedOf: Panel -> PackedScale) (pointOf: Panel -> float option) =
                    unionOver panels packedOf pointOf |> Option.map (fun d -> Axis.prefixForPacked d (packedOf panels.[0]))

                // A secondary axis pinned over the image of the primary's union, which is
                // the union of its images, since its map is monotone.
                let secondaryPin (axis: string) (cfgOf: Panel -> AxisConfig) (packedOf: Panel -> PackedScale) (erasedOf: Panel -> IScaleErased) (pointOf: Panel -> float option) =
                    match drawn |> Array.tryHead |> Option.bind (fun p -> (cfgOf p).Secondary) with
                    | Some sa -> unionOver drawn packedOf pointOf |> Option.map (secondaryPrefix axis sa (erasedOf drawn.[0]))
                    | None -> None

                let xPacked (p: Panel) = fst (axisScales p)
                let yPacked (p: Panel) = snd (axisScales p)

                let rightPin =
                    // The second y axis pinned the same way, over its own layers.
                    match pinOver (drawn |> Array.choose secondaryOf) yPacked (panelPoints >> snd) with
                    | Some pin -> Some pin
                    | None -> secondaryPin "y" (fun p -> p.YAxis) yPacked (axisScalesErased >> snd) (panelPoints >> snd)

                key,
                (pinOver drawn xPacked (panelPoints >> fst),
                 pinOver drawn yPacked (panelPoints >> snd),
                 rightPin,
                 secondaryPin "x" (fun p -> p.XAxis) xPacked (axisScalesErased >> fst) (panelPoints >> fst)))
            |> dict

        let panels =
            entries
            |> Array.map (fun (key, (fig, region, unified)) ->
                let layout, _, _ = compilePanel m fig.Theme extent region None pins.[key] (letterHeight m fig) unified
                key, layout)
            |> Array.groupBy fst
            |> Array.map (fun (key, measured) ->
                let merged = measured |> Array.map snd |> Array.reduce PanelLayout.union
                let pinX, pinY, pinY2, pinX2 = pins.[key]
                key, { merged with XPrefix = pinX; YPrefix = pinY; Y2Prefix = pinY2; X2Prefix = pinX2 })

        // Every state's scores over the new plot, and the largest each corner had, as `union` does.
        let rescore (key: string) (region: Rect<px>) (l: PanelLayout) =
            entries
            |> Array.filter (fun (k, _) -> k = key)
            |> Array.map (fun (_, (fig, _, unified)) ->
                let _, needed, _ = compilePanel m fig.Theme extent region (Some l) (None, None, None, None) (letterHeight m fig) unified
                (needed ()).LegendScores)
            |> Array.filter (fun s -> s.Length = 4)
            |> Array.fold (fun acc s -> if acc = [||] then s else Array.map2 max acc s) [||]
            |> fun s -> if s.Length = 4 then s else l.LegendScores

        let outer, panels = placeAligned m extent (Some head) template outer rescore panels

        // The box from the union of every state's domains, so each fits it.
        let ratioOf (key: string) =
            entries |> Array.filter (fun (k, _) -> k = key) |> Array.map (fun (_, (_, _, unified)) -> unified) |> aspectOf

        let panels = withAspects outer ratioOf rescore panels

        // Every state's insets, unioned by key as the panels are.
        let insets =
            states
            |> Array.collect (fun fig -> insetLayouts m extent fig outer panels)
            |> Array.groupBy fst
            |> Array.map (fun (key, ls) -> key, ls |> Array.map snd |> Array.reduce PanelLayout.union)

        let panels = Array.append panels insets

        { Outer = outer
          Panels = panels
          Samples = raw.Length
          ColourDomains = colourDomains
          QuiverKeys = quiverKeys }

    /// **One arrangement for the whole shot.** Sample the animation, arrange each state,
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
    /// The two rejected alternatives, since a reader will reach for both. **Re-arranging per
    /// frame makes the panel breathe** — see `PanelLayout`. **Freezing frame zero's arrangement
    /// overflows**, because a later frame's labels are wider than the gutters that were sized for
    /// frame zero. The union does neither, and tick labels still recompute per frame inside a
    /// rectangle that does not move.
    ///
    /// `samples` is stated rather than hidden, and reported back on the `Shot`, because the union
    /// is only as good as it: an animation with a spike between two samples can still overflow. The
    /// default elsewhere is 32 and this function does not supply it — a default belongs where a
    /// caller can see it.
    let internal arrangeUnion
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

        // The colour domains and quiver keys first, and every state measured with them in
        // place: the union domain's colorbar labels can be wider than any one state's.
        arrangeOver m extent (times |> Array.map sample)

    /// **Does a state fit a layout that is already decided?** A widget lays out once over
    /// the states its controls can reach at their extremes, and the reader can still choose one
    /// between them whose labels are wider, or zoom to a domain no extreme had. This is the
    /// question it asks of each new state, before drawing it in the old rectangle.
    ///
    /// Exact rather than estimated: each panel is planned the way `toSceneIn` will plan it, at the
    /// frozen probe, under the frozen prefixes and colour domains, and the gutters that plan needs
    /// are compared with the frozen ones. A panel at a grid position the shot has no region for
    /// does not fit; a panel with no layers needs nothing.
    let internal fitsIn (m: ITextMeasurer) (shot: Shot) (fig: Figure) : bool =
        let fig = prepare fig
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
                        compilePanel m fig.Theme extent region (Some f) (None, None, None, None) (letterHeight m fig) (fitAspect f (unify (pinned shot.ColourDomains shot.QuiverKeys (ownLegend fig p))))

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
                    && within n.Y2TicksW f.Y2TicksW
                    && within n.Y2TitleW f.Y2TitleW
                    && within n.X2TicksH f.X2TicksH
                    && within n.X2TitleH f.X2TitleH
                    && within n.LeftPad f.LeftPad
                    && within n.BottomPad f.BottomPad
                | _ -> false

        within (headHeight m fig) headRoom && Array.forall panelFits fig.Panels

    /// **Every drawn panel of a figure, as a pointer meets it in a shot.** The plot
    /// rectangle comes from the shot's frozen inner grid through `innerGrid`, the function the
    /// drawing pass takes it from, and the scales from the panel after `unify`. A panel with no
    /// layers, or one the shot has no layout for, has nothing to point at and is left out.
    let internal panelsIn (m: ITextMeasurer) (shot: Shot) (fig: Figure) : PanelView[] =
        let fig = prepare fig

        let viewOf (row: int, col: int) (inset: int option) (p: Panel) (plot: Rect<px>) (f: PanelLayout) =
            let unified = fitAspect f (unify p)
            let sx, sy = axisScalesErased unified
            let ax, ay = axisSpecs fig.Theme (f.XPrefix, f.YPrefix) unified
            let second = secondaryOf unified

            { Row = row
              Col = col
              Plot = plot
              Coord = p.Coord
              X = sx
              Y = sy
              XAxis = ax
              YAxis = ay
              EqualAspect = (p.Aspect = Some Aspect.Equal)
              Inset = inset
              Y2 = second |> Option.map (fun sp -> snd (axisScalesErased sp))
              Y2Axis = second |> Option.map (fun sp -> snd (axisSpecs fig.Theme (f.XPrefix, f.Y2Prefix) sp)) }

        // The insets first, so a pointer over one finds it before the host beneath.
        let insets =
            fig.Panels
            |> Array.collect (fun p ->
                let key = sprintf "panel-%d-%d" p.Row p.Col

                match Layout.tryFindPx key shot.Outer, shot.Panels |> Array.tryPick (fun (k, l) -> if k = key then Some l else None) with
                | Some region, Some hf when p.Insets.Length > 0 && p.Layers.Length > 0 ->
                    let hostPlot = Layout.findPx "plot" (innerGrid m shot.Outer.Extent region hf)

                    p.Insets
                    |> Array.mapi (fun i inset ->
                        shot.Panels
                        |> Array.tryPick (fun (k, l) -> if k = insetKey p i then Some l else None)
                        |> Option.map (fun l -> viewOf (p.Row, p.Col) (Some i) inset.Panel (insetRect hostPlot inset.At) l))
                    |> Array.choose id
                | _ -> [||])

        fig.Panels
        |> Array.choose (fun p ->
            let key = sprintf "panel-%d-%d" p.Row p.Col
            let frozen = shot.Panels |> Array.tryPick (fun (k, l) -> if k = key then Some l else None)

            match p.Layers.Length, Layout.tryFindPx key shot.Outer, frozen with
            | 0, _, _ -> None
            | _, Some region, Some f ->
                let plot = Layout.findPx "plot" (innerGrid m shot.Outer.Extent region f)
                Some(viewOf (p.Row, p.Col) None p plot f)
            | _ -> None)
        |> Array.append insets

    /// One inset's domains, stated, raw, in each axis's own unit; `None` leaves an axis.
    let internal withInsetDomains (row: int, col: int, inset: int) (dx: (float * float) option) (dy: (float * float) option) (fig: Figure) : Figure =
        { fig with
            Panels =
                fig.Panels
                |> Array.map (fun p ->
                    if p.Row = row && p.Col = col && inset >= 0 && inset < p.Insets.Length then
                        { p with
                            Insets =
                                p.Insets
                                |> Array.mapi (fun i ins ->
                                    if i = inset then
                                        { ins with
                                            Panel = { ins.Panel with Layers = ins.Panel.Layers |> Array.map (fun l -> { l with Data = withDomains dx dy l.Data }) } }
                                    else
                                        ins) }
                    else
                        p) }

    /// One panel's second y domain, stated, raw, in that axis's unit.
    let internal withPanelDomainY2 (row: int, col: int) (lo: float, hi: float) (fig: Figure) : Figure =
        { fig with
            Panels =
                fig.Panels
                |> Array.map (fun p ->
                    if p.Row = row && p.Col = col then
                        { p with
                            Layers =
                                p.Layers
                                |> Array.map (fun l -> if onSecond p l then { l with Data = withDomains None (Some(lo, hi)) l.Data } else l) }
                    else
                        p) }

    /// One panel's domains, stated: `Chart.withXDomain` for a panel other than the last. Raw
    /// values in each axis's own unit; `None` leaves an axis as it is.
    let internal withPanelDomains
        (row: int, col: int)
        (dx: (float * float) option)
        (dy: (float * float) option)
        (fig: Figure)
        : Figure =
        // On a shared axis, the domain is the group's.
        withDomainsShared (fun p -> p.Row = row && p.Col = col) dx dy fig

    /// **`Shot -> Figure -> Node`.** Draw this figure inside a layout that has already been
    /// decided — the frame renderer's entry point.
    ///
    /// There is no `Extent<px>` parameter, and the plan that asked for one was wrong: the shot
    /// already carries the surface it was arranged against, and a second extent beside it is a
    /// second chance to be inconsistent with no way to detect it. An arrangement is in device
    /// pixels for one surface; it does not travel to another.
    let internal toSceneIn (m: ITextMeasurer) (shot: Shot) (fig: Figure) : Node =
        let fig = prepare fig
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
                    let unified = unify (pinned shot.ColourDomains shot.QuiverKeys (ownLegend fig p))
                    let unified = match frozen with Some f -> fitAspect f unified | None -> unified
                    let _, _, draw = compilePanel m theme extent region frozen (None, None, None, None) (letterHeight m fig) unified

                    // The insets over the host's plot, each with its zoom drawn first.
                    let insets =
                        match frozen with
                        | Some hf when p.Insets.Length > 0 && p.Layers.Length > 0 ->
                            let hostPlot = Layout.findPx "plot" (innerGrid m extent region hf)

                            p.Insets
                            |> Array.mapi (fun i inset ->
                                match shot.Panels |> Array.tryPick (fun (k, l) -> if k = insetKey p i then Some l else None) with
                                | Some l ->
                                    let rect = insetRect hostPlot inset.At
                                    let ip = fitAspect l (unify inset.Panel)
                                    let _, _, drawInset = compilePanelKeyed (Keys.inset (Keys.panel p.Row p.Col) i) m theme extent (insetRegion rect l) (Some l) (None, None, None, None) 0.0<px> ip

                                    Group
                                        [| (if inset.Indicate then indicator theme extent unified hostPlot ip rect else Scene.empty)
                                           drawInset () |]
                                | None -> Scene.empty)
                        | _ -> [||]

                    if insets.Length = 0 then draw () else Group(Array.append [| draw () |] insets))

        // The panels' letters, in reading order, at the top left of each panel's region,
        // in the title row `compilePanel` keeps for them: above the axis title, clear of the data
        // and of a legend in the corner. Drawn here rather than by `compilePanel` because the
        // order is the figure's.
        let letters =
            match fig.Letters with
            | None -> [||]
            | Some letters ->
                let font = letterFont theme letters

                let name (i: int) =
                    let c = string (char (int 'a' + i % 26))

                    match letters.Letter with
                    | LetterParen -> "(" + c + ")"
                    | LetterPlain -> c
                    | LetterRightParen -> c + ")"
                    | LetterUpper -> c.ToUpperInvariant()

                let letterAt (i: int) (p: Panel) (region: Rect<px>) =
                    Keyed(
                        Keys.letter (Keys.panel p.Row p.Col),
                        Annotated(
                            { Role = RoleAnnotation
                              Label = name i
                              Data = [| "letter", name i |] },
                            Text
                                { Text = name i
                                  Tex = None
                                  At = Layout.ndPoint extent { X = region.X0; Y = region.Y0 }
                                  Font = font
                                  Paint = Solid theme.Foreground
                                  Anchor = AnchorStart
                                  Baseline = BaselineHanging
                                  Rotation = 0.0<rad> }
                        )
                    )

                fig.Panels
                |> Array.filter (fun p -> p.Layers.Length > 0)
                |> Array.sortBy (fun p -> p.Row, p.Col)
                |> Array.mapi (fun i p ->
                    Layout.tryFindPx (sprintf "panel-%d-%d" p.Row p.Col) shot.Outer
                    |> Option.map (letterAt i p))
                |> Array.choose id

        // The figure's shared legend, in the column or row the outer grid kept for it.
        let shared =
            match sharedLegend fig, Layout.tryFindPx "flegend" shot.Outer with
            | Some placement, Some cell ->
                let entries = figureEntries fig
                let size = Legend.measure m theme entries

                let x0 =
                    match placement with
                    | FigureLegendRight -> cell.X0
                    | FigureLegendBottom -> (cell.X0 + cell.X1 - size.Width) / 2.0

                let box: Rect<px> = { X0 = x0; Y0 = cell.Y0; X1 = x0 + size.Width; Y1 = cell.Y0 + size.Height }
                [| Keyed(Keys.figureLegend, Legend.compileIn m theme extent box 1 theme.ShowLegendFrame entries) |]
            | _ -> [||]

        Scene.prune (
            Keyed(
                Keys.figure,
                Group(Array.concat [ [| background; head |]; panels; letters; shared ])
            )
        )

    /// **`Figure -> Node`.** The one entry point a backend's caller needs.
    ///
    /// This is the composition of `arrangeFor` and `toSceneIn` and it is what
    /// every still figure in the library goes through. It is byte-for-byte what it was before the
    /// split — the two-pass tick solve, the estimate it plans against, and the grid it settles on
    /// are all unchanged, which is what the 46 golden baselines are for.
    let internal toScene (m: ITextMeasurer) (extent: Extent<px>) (fig: Figure) : Node =
        toSceneIn m (arrangeFor m extent fig) fig

    /// The layout `toScene` chose. For tests, and for anyone debugging why a label landed where
    /// it did.
    ///
    /// **This used to be a second, hand-written copy of `toScene`'s grid, and it was
    /// wrong.** It omitted the head row entirely, so every figure with a title or a subtitle
    /// reported panel regions taller than the ones it was actually drawn in — by exactly the
    /// height of the title. The class of the bug was predictable from the shape of
    /// the code ("two functions that must agree and have no test saying they do") without knowing
    /// it had already happened. There is now one grid and a test that says so.
    let internal arrangement (m: ITextMeasurer) (extent: Extent<px>) (fig: Figure) : Arrangement =
        (arrangeFor m extent fig).Outer
