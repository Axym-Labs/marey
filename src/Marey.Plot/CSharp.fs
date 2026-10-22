namespace Marey.CSharp

open System
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Marey

/// **The combinators as C# reads them**: `fig.WithTitle("t").Add(other)`.
///
/// F# pipes a figure through `Chart.withTitle "t"`; C# has no pipe and no curried call, so each
/// combinator a C# caller reaches for is an extension method here, one line over the module
/// function it names. Nothing here has logic of its own, and nothing here is visible to F# unless
/// it opens `Marey.CSharp`, so F# keeps one spelling of every combinator.
///
/// Output is `Save` and `Render`, taking a backend's renderer as a delegate:
/// `fig.Save(SvgRenderer.Render, "plot.svg", 800, 500)`. `Marey.Plot` references no backend,
/// so the renderer arrives as an argument, exactly as it does to `Chart.toFile`.
[<Extension; AbstractClass; Sealed>]
type FigureExtensions =

    [<Extension>]
    static member WithTitle(fig: Figure, title: string) = Chart.withTitle title fig

    [<Extension>]
    static member WithSubtitle(fig: Figure, subtitle: string) = Chart.withSubtitle subtitle fig

    [<Extension>]
    static member WithTheme(fig: Figure, theme: Theme) = Chart.withTheme theme fig

    [<Extension>]
    static member WithXLabel(fig: Figure, label: string) = Chart.withXLabel label fig

    [<Extension>]
    static member WithYLabel(fig: Figure, label: string) = Chart.withYLabel label fig

    [<Extension>]
    static member WithXLog(fig: Figure) = Chart.withXLog fig

    [<Extension>]
    static member WithYLog(fig: Figure) = Chart.withYLog fig

    [<Extension>]
    static member WithRightYLog(fig: Figure) = Chart.withRightYLog fig

    [<Extension>]
    static member WithXDomain(fig: Figure, lo: float, hi: float) = Chart.withXDomain lo hi fig

    [<Extension>]
    static member WithYDomain(fig: Figure, lo: float, hi: float) = Chart.withYDomain lo hi fig

    [<Extension>]
    static member WithGrid(fig: Figure, x: bool, y: bool) = Chart.withGrid x y fig

    [<Extension>]
    static member WithLegend(fig: Figure, placement: LegendPlacement) = Chart.withLegend placement fig

    [<Extension>]
    static member WithUnitStyle(fig: Figure, style: UnitStyle) = Chart.withUnitStyle style fig

    [<Extension>]
    static member WithPanelTitle(fig: Figure, title: string) = Chart.withPanelTitle title fig

    // ---- The last layer ------------------------------------------------------------------------

    [<Extension>]
    static member WithName(fig: Figure, name: string) = Chart.withName name fig

    [<Extension>]
    static member WithColor(fig: Figure, color: Color) = Chart.withColor color fig

    [<Extension>]
    static member WithOpacity(fig: Figure, opacity: float) = Chart.withOpacity opacity fig

    [<Extension>]
    static member WithDomainIncluded(fig: Figure) = Chart.withDomainIncluded fig

    [<Extension>]
    static member WithXTicksEvery(fig: Figure, step: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) =
        Chart.withXTickSpec (TicksEvery step) (U.erase unit) fig

    [<Extension>]
    static member WithYTicksEvery(fig: Figure, step: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) =
        Chart.withYTickSpec (TicksEvery step) (U.erase unit) fig

    [<Extension>]
    static member WithXTicksAt(fig: Figure, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) =
        Chart.withXTickSpec (TicksAt(Array.ofSeq values)) (U.erase unit) fig

    [<Extension>]
    static member WithYTicksAt(fig: Figure, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) =
        Chart.withYTickSpec (TicksAt(Array.ofSeq values)) (U.erase unit) fig

    [<Extension>]
    static member WithXTicksNamed(fig: Figure, values: seq<float>, labels: seq<string>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) =
        Chart.withXTickSpec (TicksNamed(Array.zip (Array.ofSeq values) (Array.ofSeq labels))) (U.erase unit) fig

    [<Extension>]
    static member WithYTicksNamed(fig: Figure, values: seq<float>, labels: seq<string>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) =
        Chart.withYTickSpec (TicksNamed(Array.zip (Array.ofSeq values) (Array.ofSeq labels))) (U.erase unit) fig

    [<Extension>]
    static member WithXPrefix(fig: Figure, prefix: Prefix) = Chart.withXPrefix prefix fig

    [<Extension>]
    static member WithYPrefix(fig: Figure, prefix: Prefix) = Chart.withYPrefix prefix fig

    [<Extension>]
    static member WithXReversed(fig: Figure) = Chart.withXReversed fig

    [<Extension>]
    static member WithXLook(fig: Figure, look: AxisLook) = Chart.withXLook look fig

    [<Extension>]
    static member WithYLook(fig: Figure, look: AxisLook) = Chart.withYLook look fig

    [<Extension>]
    static member WithAxesLook(fig: Figure, look: AxisLook) = Chart.withAxesLook look fig

    [<Extension>]
    static member WithXMinorTicks(fig: Figure, minor: MinorSpec) = Chart.withXMinorTicks minor fig

    [<Extension>]
    static member WithYMinorTicks(fig: Figure, minor: MinorSpec) = Chart.withYMinorTicks minor fig

    [<Extension>]
    static member WithLegendColumns(fig: Figure, columns: int) = Chart.withLegendColumns columns fig

    [<Extension>]
    static member WithLegendFrame(fig: Figure, framed: bool) = Chart.withLegendFrame framed fig

    [<Extension>]
    static member WithFigureLegend(fig: Figure, placement: FigureLegend) = Chart.withFigureLegend placement fig

    [<Extension>]
    static member WithXTickFormat(fig: Figure, format: TickFormat) = Chart.withXTickFormat format fig

    [<Extension>]
    static member WithYTickFormat(fig: Figure, format: TickFormat) = Chart.withYTickFormat format fig

    /// A format from a C# function of the number the label would show.
    [<Extension>]
    static member WithXTickFormat(fig: Figure, format: Func<float, string>) =
        Chart.withXTickFormat (TickFormat.custom (fun v -> format.Invoke v)) fig

    [<Extension>]
    static member WithYTickFormat(fig: Figure, format: Func<float, string>) =
        Chart.withYTickFormat (TickFormat.custom (fun v -> format.Invoke v)) fig

    [<Extension>]
    static member WithYReversed(fig: Figure) = Chart.withYReversed fig

    [<Extension>]
    static member WithTextOffset(fig: Figure, dx: float, dy: float) = Chart.withTextOffset (dx * 1.0<px>) (dy * 1.0<px>) fig

    [<Extension>]
    static member WithTextAt(fig: Figure, x: float, y: float) = Chart.withTextAt x y fig

    [<Extension>]
    static member WithArrow(fig: Figure, arrow: ArrowStyle) = Chart.withArrow arrow fig

    [<Extension>]
    static member AddPanelText(fig: Figure, x: float, y: float, text: string) = Chart.addPanelText x y text fig

    [<Extension>]
    static member WithPanelLetters(fig: Figure, letter: LetterStyle, [<Optional; DefaultParameterValue(true)>] bold: bool) =
        Chart.withPanelLettersWith letter bold fig

    [<Extension>]
    static member WithMarker(fig: Figure, shape: MarkerShape) = Chart.withMarker shape fig

    /// Dash lengths in device pixels.
    [<Extension>]
    static member WithDash(fig: Figure, dash: seq<float<px>>) = Chart.withDash dash fig

    [<Extension>]
    static member WithStyle(fig: Figure, restyle: Func<GeomStyle, GeomStyle>) = Chart.withStyle restyle.Invoke fig

    [<Extension>]
    static member WithKeys(fig: Figure, keys: seq<string>) = Chart.withKeys keys fig

    [<Extension>]
    static member AddLabels(fig: Figure, labels: seq<string>) = Chart.addLabels labels fig

    // ---- Channels --------------------------------------------------------------------------------

    [<Extension>]
    static member ColorByKey(fig: Figure, keys: seq<'k>) = Chart.colorByKey keys fig

    [<Extension>]
    static member ShapeByKey(fig: Figure, keys: seq<'k>) = Chart.shapeByKey keys fig

    [<Extension>]
    static member ColorBy(fig: Figure, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) =
        Chart.ColorBy(values, unit) fig

    [<Extension>]
    static member ColorByWith
        (fig: Figure, ramp: Ramp, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit)
        =
        Chart.ColorByWith(ramp, values, unit) fig

    [<Extension>]
    static member WithColorLabel(fig: Figure, label: string) = Chart.withColorLabel label fig

    [<Extension>]
    static member WithColorDomain
        (fig: Figure, lo: float, hi: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit)
        =
        Chart.WithColorDomain(lo, hi, unit) fig

    [<Extension>]
    static member SizeBy(fig: Figure, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) =
        Chart.SizeBy(values, unit) fig

    [<Extension>]
    static member SizeByWith
        (
            fig: Figure,
            smallest: float<px>,
            largest: float<px>,
            values: seq<float>,
            [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit
        ) =
        Chart.SizeByWith(smallest, largest, values, unit) fig

    // ---- Composition -----------------------------------------------------------------------------

    /// `top`'s layers as the next series.
    [<Extension>]
    static member Add(fig: Figure, top: Figure) = Chart.add top fig

    /// `top`'s layers over this figure's last panel, styles as they were built.
    [<Extension>]
    static member Overlay(fig: Figure, top: Figure) = Chart.overlay top fig

    [<Extension>]
    static member Beside(fig: Figure, right: Figure) = Chart.beside right fig

    [<Extension>]
    static member Above(fig: Figure, below: Figure) = Chart.above below fig

    /// `right`'s layers on this figure's last panel, against a y axis on its right.
    [<Extension>]
    static member WithRightAxis(fig: Figure, right: Figure) = Chart.withRightAxis right fig

    [<Extension>]
    static member WithRightYDomain(fig: Figure, lo: float, hi: float) = Chart.withRightYDomain lo hi fig

    [<Extension>]
    static member WithRightYLabel(fig: Figure, label: string) = Chart.withRightYLabel label fig

    /// A one-panel figure drawn over this figure's last panel, at fractions of its plot.
    [<Extension>]
    static member WithInset(fig: Figure, x0: float, y0: float, x1: float, y1: float, inset: Figure) = Chart.withInset (x0, y0, x1, y1) inset fig

    /// The last inset's domain drawn on its host.
    [<Extension>]
    static member WithInsetZoom(fig: Figure) = Chart.withInsetZoom fig

    /// One unit as long on y as on x.
    [<Extension>]
    static member WithEqualAspect(fig: Figure) = Chart.withEqualAspect fig

    /// The plot at this height over its width.
    [<Extension>]
    static member WithBoxAspect(fig: Figure, heightOverWidth: float) = Chart.withBoxAspect heightOverWidth fig

    /// The x axis again along the top, in another unit of its dimension.
    [<Extension>]
    static member WithTopAxisIn(fig: Figure, unit: IUnit) = Chart.withSecondary true (U.erase unit) None fig

    /// The y axis again along the right, in another unit of its dimension.
    [<Extension>]
    static member WithRightAxisIn(fig: Figure, unit: IUnit) = Chart.withSecondary false (U.erase unit) None fig

    /// The x axis read along the top through a map and its inverse, in raw numbers.
    [<Extension>]
    static member WithTopAxisMapped(fig: Figure, unit: IUnit, forward: System.Func<float, float>, inverse: System.Func<float, float>) =
        Chart.withSecondary true (U.erase unit) (Some(SecondaryMap(forward.Invoke, inverse.Invoke))) fig

    /// The y axis read along the right through a map and its inverse.
    [<Extension>]
    static member WithRightAxisMapped(fig: Figure, unit: IUnit, forward: System.Func<float, float>, inverse: System.Func<float, float>) =
        Chart.withSecondary false (U.erase unit) (Some(SecondaryMap(forward.Invoke, inverse.Invoke))) fig

    /// The secondary x axis's title, in place of the one its unit would give it.
    [<Extension>]
    static member WithTopAxisLabel(fig: Figure, label: string) = Chart.withTopAxisLabel label fig

    /// Move the last layer onto a right y axis of its own.
    [<Extension>]
    static member ToRightAxis(fig: Figure) = Chart.toRightAxis fig

    /// The last panel's x axis, configured whole: `fig.WithXAxis(Figures.Axis("t"))`.
    [<Extension>]
    static member WithXAxis(fig: Figure, config: AxisConfig) = Chart.withXAxis config fig

    [<Extension>]
    static member WithYAxis(fig: Figure, config: AxisConfig) = Chart.withYAxis config fig

    /// The last panel's x scale: `ScaleKind.NewLog(10.0)`, `ScaleKind.NewSymlog(1.0)`, ….
    [<Extension>]
    static member WithXScaleKind(fig: Figure, kind: ScaleKind) = Chart.withXScaleKind kind fig

    [<Extension>]
    static member WithYScaleKind(fig: Figure, kind: ScaleKind) = Chart.withYScaleKind kind fig

    [<Extension>]
    static member WithRightYScaleKind(fig: Figure, kind: ScaleKind) = Chart.withRightYScaleKind kind fig

    /// The last panel's coordinate system: `fig.WithCoord(Coords.Polar)`.
    [<Extension>]
    static member WithCoord(fig: Figure, coord: Coord) = Chart.withCoord coord fig

    /// The x domain of the panel at `row`, `col`, in the axis's own numbers.
    [<Extension>]
    static member WithPanelXDomain(fig: Figure, row: int, col: int, lo: float, hi: float) =
        Chart.withPanelDomains (row, col) (Some(lo, hi)) None fig

    /// The y domain of the panel at `row`, `col`, in the axis's own numbers.
    [<Extension>]
    static member WithPanelYDomain(fig: Figure, row: int, col: int, lo: float, hi: float) =
        Chart.withPanelDomains (row, col) None (Some(lo, hi)) fig

    /// One x axis for every panel the figure has now, labelled on the outermost.
    [<Extension>]
    static member ShareX(fig: Figure) = Chart.shareX fig

    [<Extension>]
    static member ShareY(fig: Figure) = Chart.shareY fig

    [<Extension>]
    static member ShareAxes(fig: Figure) = Chart.shareAxes fig

    /// The rows' plot heights, relative: `fig.WithRowHeights(3, 1)`.
    [<Extension>]
    static member WithRowHeights(fig: Figure, [<System.ParamArray>] heights: float[]) = Chart.withRowHeights heights fig

    /// The columns' plot widths, relative.
    [<Extension>]
    static member WithColWidths(fig: Figure, [<System.ParamArray>] widths: float[]) = Chart.withColWidths widths fig

    // ---- Output ----------------------------------------------------------------------------------

    /// The figure as a scene, at a size in device pixels.
    [<Extension>]
    static member ToScene(fig: Figure, width: float, height: float) : Node = Chart.toScene (Chart.size width height) fig

    /// The figure through a renderer: `fig.Render(SvgRenderer.Render, 800, 500)` is the SVG text.
    [<Extension>]
    static member Render(fig: Figure, renderer: Func<Extent<px>, Node, 'a>, width: float, height: float) : 'a =
        Chart.render (fun e n -> renderer.Invoke(e, n)) (Chart.size width height) fig

#if !FABLE_COMPILER
    /// Write a text format — SVG, TikZ — as UTF-8 without a byte-order mark, as `Chart.toFile` does.
    [<Extension>]
    static member Save(fig: Figure, renderer: Func<Extent<px>, Node, string>, path: string, width: float, height: float) =
        Chart.toFile (fun e n -> renderer.Invoke(e, n)) path (Chart.size width height) fig

    /// Write a binary format — PNG, PDF — exactly as the renderer produced it.
    [<Extension>]
    static member Save(fig: Figure, renderer: Func<Extent<px>, Node, byte[]>, path: string, width: float, height: float) =
        Chart.toFileBytes (fun e n -> renderer.Invoke(e, n)) path (Chart.size width height) fig
#endif

/// The arrangements that take several figures, for C#. F# passes `(title, figure)` tuples;
/// C# passes the titles and the figures as two arrays of one length.
[<AbstractClass; Sealed>]
type Figures =

    static member private Parts(titles: string[], parts: Figure[]) =
        if titles.Length <> parts.Length then
            invalidArg "parts" (sprintf "Marey: %d titles and %d figures." titles.Length parts.Length)

        Array.zip titles parts

    /// One panel per figure, sharing both axes, wrapped into a near-square grid.
    static member Facet(titles: seq<string>, parts: seq<Figure>) = Chart.facet (Figures.Parts(Array.ofSeq titles, Array.ofSeq parts))

    static member FacetIn(cols: int, titles: seq<string>, parts: seq<Figure>) = Chart.facetIn cols (Figures.Parts(Array.ofSeq titles, Array.ofSeq parts))

    static member FacetWith(mode: FacetScales, cols: int, titles: seq<string>, parts: seq<Figure>) =
        Chart.facetWith mode cols (Figures.Parts(Array.ofSeq titles, Array.ofSeq parts))

    /// Panels laid out by a picture of the grid: `Figures.Mosaic("AB;AC", "ABC", a, b, c)`,
    /// where the i-th character of `names` names the i-th figure.
    /// An axis configuration named after the quantity, for `WithXAxis` and `WithYAxis`.
    static member Axis(label: string) = Chart.axis label

    static member Mosaic(layout: string, names: string, [<System.ParamArray>] parts: Figure[]) =
        if names.Length <> parts.Length then
            invalidArg "parts" (sprintf "Marey: %d names and %d figures." names.Length parts.Length)

        Chart.mosaic layout (List.zip (List.ofSeq names) (List.ofArray parts))

/// The built-in themes, for C#: `fig.WithTheme(Themes.Publication)`.
[<AbstractClass; Sealed>]
type Themes =

    /// The default: sans-serif, light, a grid behind the data.
    static member Modern = Theme.modern

    /// The framed box of a physics journal: ticks in on four sides, minor ticks, no grid.
    static member Publication = Theme.publication

    static member Notebook = Theme.notebook

    static member Presentation = Theme.presentation

    static member Dark = Theme.dark

    /// For a LaTeX document: sizes pinned, fonts inherited, units as siunitx macros.
    static member Tikz = Theme.tikz

    /// The theme a figure starts with.
    static member Default = Theme.initial

    /// Every built-in theme.
    static member All = Theme.all

/// Coordinate systems, for C#: `fig.WithCoord(Coords.Polar)`.
[<AbstractClass; Sealed>]
type Coords =

    static member Cartesian = Coord.cartesian

    /// Angle along x, radius along y.
    static member Polar = Coord.polar

    /// A polar sector from `startAngle` to `endAngle`, in radians, with an inner radius as a
    /// fraction of the outer.
    static member PolarBetween(startAngle: float, endAngle: float, innerRadius: float) =
        Coord.polarBetween (startAngle * 1.0<rad>) (endAngle * 1.0<rad>) innerRadius

    /// `inner` with its two axes swapped.
    static member Flipped(inner: Coord) = Coord.flipped inner

/// The categorical front door for C#. F# writes `Chart.Category.Bar(…)`, a type nested in
/// `module Chart`, which C# can only reach as `ChartModule.Category`; this is the same members
/// under a name C# reads.
[<AbstractClass; Sealed>]
type Category =

    static member Bar(keys: seq<'k>, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit) =
        Chart.Category.Bar(keys, values, yUnit)

    static member Scatter(keys: seq<'k>, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit) =
        Chart.Category.Scatter(keys, values, yUnit)

    static member Boxplot(keys: seq<'k>, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit) =
        Chart.Category.Boxplot(keys, values, yUnit)

    static member Violin(keys: seq<'k>, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit) =
        Chart.Category.Violin(keys, values, yUnit)

    /// One bar per key, as tall as the number of rows with that key.
    static member Count(keys: seq<'k>) = Chart.Category.count keys
