namespace Marey.UnitsOfMeasure

open System
open Marey

/// D109 — **the unit-required mode**: `open Marey.UnitsOfMeasure`, and every chart takes a witness
/// per axis, checked at compile time.
///
/// ```fsharp
/// open Marey
/// open Marey.UnitsOfMeasure
///
/// Chart.line U.second U.metre ts xs     // float<s>[] against float<m>[]
/// |> Chart.withTitle "Free fall"
/// ```
///
/// On the pattern of Flips' `Flips.UnitsOfMeasure`: a module named as the core's, so that F#,
/// which resolves `Chart.x` across every `Chart` in scope with the latest open first, finds the
/// data entry here and everything else — the combinators, the arrangement, the output — in the
/// core. Nothing is duplicated but the door. Each function here is the core's measure-generic
/// implementation, reached directly.
///
/// ### The shadows
///
/// `float` is `float<1>`, so plain arrays type-check against either mode, and a unit-optional
/// call written here would compile and draw an axis with no unit — the one failure this mode
/// exists to make impossible. So each unit-optional constructor is shadowed below by a value of
/// the same name marked obsolete-as-error: under this `open`, `Chart.Line(xs, ys)` does not
/// compile, and says which spelling to use instead. A shadowing *type* would not do it, because
/// F# merges the members of two types of one name. `Marey.Chart.Line(…)`, written out, still
/// reaches the core, for the file that means it.
///
/// `SurfaceTests`' coverage gate holds the two lists together: every unit-optional member has a
/// curried counterpart here and a shadow.
[<RequireQualifiedAccess>]
module Chart =

    /// A typed series over linear scales with 5% breathing room. The last point at which the
    /// measures are alive on this path.
    let series (name: string) (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Series<'ux, 'uy> =
        Marey.Chart.series name ux uy x y

    let line (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        Marey.Chart.line ux uy x y

    /// Scatter. **One `Markers` node for the whole series**, however many points — §5.1, "the
    /// single biggest performance decision in the IR". Verifiable with `Scene.count`.
    let scatter (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        Marey.Chart.scatter ux uy x y

    let area (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        Marey.Chart.area ux uy x y

    /// Bars from zero — see `withZero`. The y domain is the data's, widened to reach the
    /// baseline the marks are measured from.
    let bar (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        Marey.Chart.bar ux uy x y

    /// Error bars with symmetric uncertainties.
    ///
    /// `err` is `float('uy)[]` — the same measure as `y`. An uncertainty in the wrong unit does
    /// not compile, which is the whole thesis applied to the one place experimental data most
    /// often gets it wrong. The y domain is taken over `y ± err`, so the caps are inside the
    /// panel rather than clipped by it.
    let errorbar (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (err: float<'uy>[]) : Figure =
        Marey.Chart.errorbar ux uy x y err

    /// A staircase: the same points as `line`, with the claim that nothing happened between
    /// them.
    ///
    /// The default is `StepAfter` — the value holds from each sample until the next — which is
    /// what a sampled-and-held signal, a counter and a piecewise-constant control input all do.
    /// `stepWith` states the other two.
    let step (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        Marey.Chart.step ux uy x y

    let stepWith (align: StepAlign) (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        Marey.Chart.stepWith align ux uy x y

    /// A filled band between two y series — a confidence interval, an envelope, a min/max
    /// spread.
    ///
    /// The band and nothing else. `Chart.line … |> Chart.addRibbon …` is the fit-with-interval
    /// figure, and the order matters: the band is added second and drawn over the line, so it
    /// wants the theme's fill opacity, which it has.
    let ribbon (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (low: float<'uy>[]) (high: float<'uy>[]) : Figure =
        Marey.Chart.ribbon ux uy x low high

    /// Tiles of a stated size, centred on the given points — the heatmap geom.
    ///
    /// Both sizes are in the data's own units, so a cell 0.5 s wide and 2 K tall is written as
    /// `0.5&lt;s&gt;` and `2.0&lt;K&gt;` and cannot be transposed: the compiler will not accept
    /// a width in kelvin. What makes the tiles *mean* anything is a colour channel —
    /// `Chart.tile … |> Chart.colorBy U.kelvin temps`, which is what `Chart.heatmap` is.
    let tile
        (ux: U<'ux>)
        (uy: U<'uy>)
        (w: float<'ux>)
        (h: float<'uy>)
        (x: float<'ux>[])
        (y: float<'uy>[])
        : Figure =
        Marey.Chart.tile ux uy w h x y

    /// An RGBA image placed in data space — a micrograph under its scale bar, a map under a
    /// track, a reference plate beside a measurement (D64).
    ///
    /// The box is given in the axes' own units, so the image lands where the data says it does
    /// and the axes label themselves from the witnesses as they do for anything else. `smooth`
    /// is bilinear interpolation: `false` is right for anything whose pixels are measurements,
    /// where smoothing invents values that were never recorded.
    ///
    /// **This is the only thing in Marey that produces a `Raster`**, and until it existed
    /// `renderWithAssets` had no customer — which is D10's failure mode, and the reason D64
    /// insists the two ship together. `Chart.heatmap` is *tiles*, not a raster, and always was.
    ///
    /// Both backends draw it: SVG inlines a `data:` URI, and TikZ writes a sidecar `.png`
    /// through `TikzBackend.renderToFileWithAssets`. `TikzBackend.render` returns a bare string
    /// and so cannot carry the sidecar; it degrades the image away rather than emitting an
    /// `\\includegraphics` of a file nobody wrote.
    ///
    /// The domain is the box, unpadded, on the same rule as a tessellation's: an image
    /// partitions the region it covers, and a margin past its edge draws part of that region as
    /// if the image did not cover it.
    let image
        (ux: U<'ux>)
        (uy: U<'uy>)
        (img: Image)
        (x0: float<'ux>)
        (y0: float<'uy>)
        (x1: float<'ux>)
        (y1: float<'uy>)
        (smooth: bool)
        : Figure =
        Marey.Chart.image ux uy img x0 y0 x1 y1 smooth

    /// A line from each `(x0, y0)` to the matching `(x1, y1)` — a displacement, an edge of a
    /// graph, a before-and-after pair.
    ///
    /// Both ends are inside the domain, which is the one thing a caller cannot fix afterwards:
    /// a segment whose far end is off the panel is clipped to a line pointing at nothing.
    let segment
        (ux: U<'ux>)
        (uy: U<'uy>)
        (x0: float<'ux>[])
        (y0: float<'uy>[])
        (x1: float<'ux>[])
        (y1: float<'uy>[])
        : Figure =
        Marey.Chart.segment ux uy x0 y0 x1 y1

    /// Labels at stated positions — an annotated point, a named landmark, a value printed on
    /// its own mark.
    ///
    /// `Chart.addLabels` is the form that labels a series already on the figure, and is what
    /// most callers want; this one is for text that is the whole layer.
    let text (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (labels: string[]) : Figure =
        Marey.Chart.text ux uy x y labels

    /// Add a second (third, …) series to the last panel.
    ///
    /// The panel's axes are then shared: `Figure.unify` unions the domains at compile time and
    /// rebuilds every layer's scale over the union. It also checks that the added series has the
    /// *same physical dimension* as the existing ones and raises if not — the one runtime
    /// dimensional check in the library, and it is runtime only because the two series have
    /// already crossed §2.3's existential and F# can no longer compare their measures.
    let addLine (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        Marey.Chart.addLine ux uy x y fig

    let addScatter (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        Marey.Chart.addScatter ux uy x y fig

    let addArea (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        Marey.Chart.addArea ux uy x y fig

    let addErrorbar (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (err: float<'uy>[]) (fig: Figure) : Figure =
        Marey.Chart.addErrorbar ux uy x y err fig

    let addStep (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        Marey.Chart.addStep ux uy x y fig

    /// Add a band between two y series to the last panel — the interval around a fit that was
    /// already drawn.
    ///
    /// It goes in the palette like any other added series, so a band added to a line comes out
    /// in the *next* colour rather than the line's. That is deliberate and matches
    /// `addErrorbar`: the two arrays are a second thing that was measured, and a caller who
    /// means it as a shading of the first says so with `Chart.withStyle`.
    let addRibbon
        (ux: U<'ux>)
        (uy: U<'uy>)
        (x: float<'ux>[])
        (low: float<'uy>[])
        (high: float<'uy>[])
        (fig: Figure)
        : Figure =
        Marey.Chart.addRibbon ux uy x low high fig

    let addSegment
        (ux: U<'ux>)
        (uy: U<'uy>)
        (x0: float<'ux>[])
        (y0: float<'uy>[])
        (x1: float<'ux>[])
        (y1: float<'uy>[])
        (fig: Figure)
        : Figure =
        Marey.Chart.addSegment ux uy x0 y0 x1 y1 fig

    /// A histogram of a sample, binned by Freedman–Diaconis.
    let histogram (u: U<'u>) (xs: float<'u>[]) : Figure = Marey.Chart.histogram u xs

    /// A histogram of a sample with a stated bin count.
    let histogramWith (u: U<'u>) (bins: int) (xs: float<'u>[]) : Figure = Marey.Chart.histogramWith u bins xs

    /// A Gaussian kernel density estimate of a sample, at Silverman's bandwidth.
    let density (u: U<'u>) (xs: float<'u>[]) : Figure = Marey.Chart.density u xs

    /// The empirical CDF of a sample, drawn as the step function it is.
    let ecdf (u: U<'u>) (xs: float<'u>[]) : Figure = Marey.Chart.ecdf u xs

    /// Add a least-squares fit of y against x to the last panel, as a two-point line over the
    /// range of the data.
    ///
    /// **The slope is not returned, and that is D47's split rather than an omission.** The fit
    /// is a number — `float<'uy/'ux>`, and for a displacement against a time that is a velocity
    /// the caller can bind, print, and do arithmetic with. Handing it back through a `Figure`
    /// would mean packing it into an existential to get it out of this function, so a caller
    /// who wants the number calls `Stat.regression` and keeps it typed; this draws it.
    ///
    /// ```fsharp
    /// let fit = Stat.regression U.second U.metre ts xs
    /// let v: float<m/s> = fit.Slope
    /// Chart.scatter U.second U.metre ts xs |> Chart.addFit U.second U.metre ts xs
    /// ```
    let addFit (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        Marey.Chart.addFit ux uy x y fig

    /// Add a loess smooth of y against x to the last panel: R's defaults, span 0.75 and a local
    /// quadratic, at 80 points over the data's range. `Stat.loess` returns the curve itself.
    let addLoess (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        Marey.Chart.addLoess ux uy x y fig

    /// `addLoess` with a stated span: the fraction of the points each local fit uses.
    let addLoessWith (span: float) (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        Marey.Chart.addLoessWith span ux uy x y fig

    /// The least-squares line through `(x, y)`, on its own: what `addFit` draws, as a figure that
    /// `add` can put over the data.
    let fit (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        Marey.Chart.fit ux uy x y

    /// The loess smooth of `(x, y)`, on its own — see `fit`.
    let loess (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        Marey.Chart.loess ux uy x y

    let loessWith (span: float) (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        Marey.Chart.loessWith span ux uy x y

    /// **Colour by a quantity**, over a named ramp.
    ///
    /// The witness is an argument for the same reason it is one on `Chart.line`: `float<K>` is
    /// `float` at runtime, and something has to hand the library the evidence exactly once.
    /// What it buys is the same thing it buys on an axis — the colorbar labels itself `T [K]`,
    /// picks its own prefix, and its ticks are the axis compiler's (D49).
    ///
    /// The domain is the data's own range, **unpadded**: an axis pads so that the extreme
    /// points are not on the frame, and a colour ramp has no frame to sit on — padding it would
    /// simply throw away both ends of the ramp.
    let colorByWith (ramp: Ramp) (u: U<'u>) (values: float<'u>[]) (fig: Figure) : Figure =
        Marey.Chart.colorByWith ramp u values fig

    /// `colorByWith` over the theme's sequential ramp — viridis-family, perceptually ordered
    /// and lightness-monotone, so it survives greyscale printing.
    let colorBy (u: U<'u>) (values: float<'u>[]) (fig: Figure) : Figure =
        Marey.Chart.colorBy u values fig

    /// **State a continuous colour channel's domain** rather than taking its values' range: the
    /// most recent layer's colour scale runs from `lo` to `hi`, in `u`, and a value beyond either
    /// end takes the ramp's end colour.
    ///
    /// For two figures that must share a colour scale, and for a film whose colours should mean
    /// a stated range rather than the union of its sampled frames (D94). `u` need not be the
    /// channel's own unit, only of its dimension: the ends are converted. A different dimension,
    /// or an angle against no angle (D98), raises, as `Figure.unify` does for an axis. When
    /// either is `U.unspecified` there is nothing to check or convert, and the ends are used as
    /// given (D127).
    let withColorDomain (u: U<'u>) (lo: float<'u>) (hi: float<'u>) (fig: Figure) : Figure =
        Marey.Chart.withColorDomain u lo hi fig

    /// **Size by a quantity**, between two device diameters.
    ///
    /// Interpolated in area rather than in diameter, because area is what the eye reads off a
    /// disc. Size is the weakest of the channels for accurate reading, so it is the one to
    /// reach for last: the key it produces is three marks in the legend, not a bar.
    /// §9 — **a heatmap**: one tile per row, sized to the grid's own pitch, coloured by a third
    /// quantity.
    ///
    /// Three witnesses and three measures, and the middle one is what a heatmap usually gets
    /// wrong: `z` has its own unit, so the colorbar labels itself `T [K]` off the same witness
    /// the values were declared with (D49), and a `z` handed the x array does not compile.
    ///
    /// The cell size is derived rather than asked for, because it is a property of the grid and
    /// not a choice: the smallest positive gap between two x values is the column pitch, and a
    /// tile that size tessellates. `Chart.tile` takes the size for the case where the grid is
    /// irregular and the caller knows what the cells are.
    let heatmap
        (ux: U<'ux>)
        (uy: U<'uy>)
        (uz: U<'uz>)
        (x: float<'ux>[])
        (y: float<'uy>[])
        (z: float<'uz>[])
        : Figure =
        Marey.Chart.heatmap ux uy uz x y z

    /// `ofHexbin` over `Stat.hexbinWith across`.
    let hexbinWith (ux: U<'ux>) (uy: U<'uy>) (across: int) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        Marey.Chart.hexbinWith ux uy across x y

    /// `ofHexbin` over `Stat.hexbin`: 24 hexagons across.
    let hexbin (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        Marey.Chart.hexbin ux uy x y

    let sizeByWith (smallest: float<px>) (largest: float<px>) (u: U<'u>) (values: float<'u>[]) (fig: Figure) : Figure =
        Marey.Chart.sizeByWith smallest largest u values fig

    /// `sizeByWith` over a range centred on the theme's marker size: half of it to three times
    /// it, which is a visible span without producing marks that swallow their neighbours.
    let sizeBy (u: U<'u>) (values: float<'u>[]) (fig: Figure) : Figure =
        Marey.Chart.sizeBy u values fig

    // ---- Secondary axes (D178) ----------------------------------------------------------------

    /// D178 — the x axis read along the top through a map, typed: `forward` takes the x axis's
    /// unit to `u`, and the compiler checks both directions. See `Marey.Chart.withTopAxisMapped`.
    let withTopAxisMapped (u: U<'v>) (forward: float<'ux> -> float<'v>) (inverse: float<'v> -> float<'ux>) (fig: Figure) : Figure =
        Marey.Chart.withTopAxisMapped
            u
            (fun x -> float (forward (LanguagePrimitives.FloatWithMeasure<'ux> x)))
            (fun w -> float (inverse (LanguagePrimitives.FloatWithMeasure<'v> w)))
            fig

    /// D178 — the y axis read along the right through a map, typed. See `withTopAxisMapped`.
    let withRightAxisMapped (u: U<'v>) (forward: float<'uy> -> float<'v>) (inverse: float<'v> -> float<'uy>) (fig: Figure) : Figure =
        Marey.Chart.withRightAxisMapped
            u
            (fun y -> float (forward (LanguagePrimitives.FloatWithMeasure<'uy> y)))
            (fun w -> float (inverse (LanguagePrimitives.FloatWithMeasure<'v> w)))
            fig

    // ---- Rules and spans (D162) ----------------------------------------------------------------

    /// A horizontal rule at `y`, across the last panel wherever its domain ends up, and without
    /// moving that domain (D163; `withDomainIncluded` lets it). The witness is the y axis's, so a
    /// rule at `9.81<m/s^2>` on a metre axis does not compile. Unnamed it is furniture and stays
    /// out of the legend; `withName` puts it in.
    let addHLine (uy: U<'uy>) (y: float<'uy>) (fig: Figure) : Figure = Marey.Chart.addHLine uy y fig

    /// A vertical rule at `x`. See `addHLine`.
    let addVLine (ux: U<'ux>) (x: float<'ux>) (fig: Figure) : Figure = Marey.Chart.addVLine ux x fig

    /// A horizontal band between `lo` and `hi`, across the panel. See `addHLine`.
    let addHSpan (uy: U<'uy>) (lo: float<'uy>) (hi: float<'uy>) (fig: Figure) : Figure = Marey.Chart.addHSpan uy lo hi fig

    /// A vertical band between `lo` and `hi`, up the panel. See `addHLine`.
    let addVSpan (ux: U<'ux>) (lo: float<'ux>) (hi: float<'ux>) (fig: Figure) : Figure = Marey.Chart.addVSpan ux lo hi fig

    /// The line through (`x`, `y`) with `slope`, to both edges of the panel. The slope is in the
    /// y unit per x unit, so a speed is the slope of a line of metres against seconds and a
    /// slope in the wrong units does not compile. On a log axis it is straight on the page.
    let addLineThrough (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>) (y: float<'uy>) (slope: float<'uy / 'ux>) (fig: Figure) : Figure =
        Marey.Chart.addLineThrough ux uy x y slope fig

    /// A figure that is only a horizontal rule, for `Chart.add` or `Chart.overlay`.
    let hLine (uy: U<'uy>) (y: float<'uy>) : Figure = Marey.Chart.hLine uy y

    let vLine (ux: U<'ux>) (x: float<'ux>) : Figure = Marey.Chart.vLine ux x

    let hSpan (uy: U<'uy>) (lo: float<'uy>) (hi: float<'uy>) : Figure = Marey.Chart.hSpan uy lo hi

    let vSpan (ux: U<'ux>) (lo: float<'ux>) (hi: float<'ux>) : Figure = Marey.Chart.vSpan ux lo hi

    let lineThrough (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>) (y: float<'uy>) (slope: float<'uy / 'ux>) : Figure =
        Marey.Chart.lineThrough ux uy x y slope

    // ---- Annotations (D164) -------------------------------------------------------------------

    /// Text marking the point (`x`, `y`) on the last panel, 6 px up and to the right of it,
    /// without moving the domain. `withTextOffset`, `withTextAt` and `withArrow` place it and
    /// point at the point; `withColor` and `withStyle` restyle it.
    let annotate (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>) (y: float<'uy>) (text: string) (fig: Figure) : Figure =
        Marey.Chart.annotate ux uy x y text fig

    /// A figure that is only an annotation, for `Chart.add`.
    let annotation (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>) (y: float<'uy>) (text: string) : Figure =
        Marey.Chart.annotation ux uy x y text

    /// Put the most recent annotation's text at a place in data space, checked against the axes'
    /// witnesses.
    let withTextAt (_ux: U<'ux>) (_uy: U<'uy>) (x: float<'ux>) (y: float<'uy>) (fig: Figure) : Figure =
        Marey.Chart.withTextAt (float x) (float y) fig

    // ---- Stated ticks (D166) -------------------------------------------------------------------

    let private erased (u: U<'u>) : U<1> = U.erase (u :> IUnit)

    /// A tick at every multiple of `step` on the x axis. The step is checked against the axis's
    /// unit when the figure is compiled, and converted when it is another unit of the same
    /// dimension: `withXTicksEvery U.kilometre 0.5<km>` on a metre axis ticks every 500 m.
    let withXTicksEvery (u: U<'u>) (step: float<'u>) (fig: Figure) : Figure =
        Marey.Chart.withXTickSpec (TicksEvery(float step)) (erased u) fig

    let withYTicksEvery (u: U<'u>) (step: float<'u>) (fig: Figure) : Figure =
        Marey.Chart.withYTickSpec (TicksEvery(float step)) (erased u) fig

    /// Ticks at these values on the x axis, labelled as chosen ticks would be.
    let withXTicksAt (u: U<'u>) (values: float<'u>[]) (fig: Figure) : Figure =
        Marey.Chart.withXTickSpec (TicksAt(values |> Array.map float)) (erased u) fig

    let withYTicksAt (u: U<'u>) (values: float<'u>[]) (fig: Figure) : Figure =
        Marey.Chart.withYTickSpec (TicksAt(values |> Array.map float)) (erased u) fig

    /// Ticks at these values on the x axis, each with its own label — `X`, `Γ`, `X`.
    let withXTicksNamed (u: U<'u>) (ticks: (float<'u> * string)[]) (fig: Figure) : Figure =
        Marey.Chart.withXTickSpec (TicksNamed(ticks |> Array.map (fun (v, n) -> float v, n))) (erased u) fig

    let withYTicksNamed (u: U<'u>) (ticks: (float<'u> * string)[]) (fig: Figure) : Figure =
        Marey.Chart.withYTickSpec (TicksNamed(ticks |> Array.map (fun (v, n) -> float v, n))) (erased u) fig

    // ---- The shadows -------------------------------------------------------------------------

    [<Obsolete("Marey.UnitsOfMeasure is open, so an annotation takes a witness per axis: Chart.annotate or Chart.annotation. Chart.Annotation(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Annotation (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a rule takes a witness: Chart.addHLine or Chart.hLine. Chart.HLine(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let HLine (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a rule takes a witness: Chart.addVLine or Chart.vLine. Chart.VLine(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let VLine (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a span takes a witness: Chart.addHSpan or Chart.hSpan. Chart.HSpan(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let HSpan (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a span takes a witness: Chart.addVSpan or Chart.vSpan. Chart.VSpan(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let VSpan (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a rule takes a witness per axis: Chart.addLineThrough or Chart.lineThrough. Chart.LineThrough(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let LineThrough (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.line. Chart.Line(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Line (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.scatter. Chart.Scatter(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Scatter (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.area. Chart.Area(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Area (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.bar. Chart.Bar(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Bar (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.errorbar. Chart.Errorbar(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Errorbar (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.step. Chart.Step(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Step (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.stepWith. Chart.StepWith(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let StepWith (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.ribbon. Chart.Ribbon(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Ribbon (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.tile. Chart.Tile(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Tile (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.image. Chart.Image(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Image (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.segment. Chart.Segment(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Segment (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.text. Chart.Text(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Text (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.fit. Chart.Fit(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Fit (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.loess. Chart.Loess(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Loess (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.loessWith. Chart.LoessWith(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let LoessWith (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.histogram. Chart.Histogram(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Histogram (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.histogramWith. Chart.HistogramWith(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let HistogramWith (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.density. Chart.Density(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Density (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.ecdf. Chart.Ecdf(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Ecdf (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.heatmap. Chart.Heatmap(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Heatmap (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.hexbin. Chart.Hexbin(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Hexbin (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.hexbinWith. Chart.HexbinWith(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let HexbinWith (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.series. Chart.Series(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Series (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.colorBy. Chart.ColorBy(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let ColorBy (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.colorByWith. Chart.ColorByWith(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let ColorByWith (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.withColorDomain. Chart.WithColorDomain(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let WithColorDomain (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.sizeBy. Chart.SizeBy(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let SizeBy (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.sizeByWith. Chart.SizeByWith(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let SizeByWith (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    /// D45's categorical door in the unit-required mode: the values take a witness, the keys are
    /// data. `Chart.Category.count`, which takes no witness, is the core's and resolves there.
    [<RequireQualifiedAccess>]
    module Category =

        /// The typed categorical series, over the levels the keys themselves define.
        let series (name: string) (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Levels<'k> * Series<1, 'uy> =
            Marey.Chart.Category.series name uy keys values

        /// A bar per row, in the band its key names, measured from zero.
        let bar (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Figure =
            Marey.Chart.Category.bar uy keys values

        /// The same axis with points instead of bars — a dot plot, and the shape a categorical
        /// colour channel will hang off in phase 2.
        let scatter (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Figure =
            Marey.Chart.Category.scatter uy keys values

        /// §9 / D55 — **a box plot per level**, from the rows that carry it.
        ///
        /// The measures again do the work nobody had to ask for: `Stat.summary` of a
        /// `float&lt;'uy&gt;[]` is entirely in `float&lt;'uy&gt;`, so the box, the whiskers and
        /// the outliers all project through the same y scale and the axis labels itself off the
        /// same witness the values were declared with.
        ///
        /// The y domain covers the whiskers **and the outliers**, padded. A domain taken from
        /// the boxes alone is the tidier picture and it is a lie by omission: the points that
        /// fall outside it are exactly the ones a box plot is read to find, and clipping them
        /// hides them behind the panel edge rather than dropping them somewhere visible.
        let boxplot (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Figure =
            Marey.Chart.Category.boxplot uy keys values

        /// §9 / D55 — **a violin per level**: the same split, drawn as a density instead of a
        /// summary.
        ///
        /// **The bandwidth comes from the undivided sample** (`Stat.frameOf`), not from each
        /// group, and that is D48's facet argument arriving in a second place. A bandwidth
        /// chosen per group smooths the sparse ones harder and invents a difference in *shape*
        /// that is not in the data — the quiet kind of wrong, because the figure looks fine. It
        /// also removes a failure: a group of one value has no bandwidth of its own and a
        /// density over it raises, where under a shared one it draws the whole sample's kernel
        /// over its single point, which is what it is.
        ///
        /// A sample with no spread at all still has no density to estimate, and says so.
        let violin (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Figure =
            Marey.Chart.Category.violin uy keys values

        [<Obsolete("Marey.UnitsOfMeasure is open, so a categorical chart takes the values' witness: Chart.Category.series. Chart.Category.Series(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
        let Series (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

        [<Obsolete("Marey.UnitsOfMeasure is open, so a categorical chart takes the values' witness: Chart.Category.bar. Chart.Category.Bar(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
        let Bar (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

        [<Obsolete("Marey.UnitsOfMeasure is open, so a categorical chart takes the values' witness: Chart.Category.scatter. Chart.Category.Scatter(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
        let Scatter (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

        [<Obsolete("Marey.UnitsOfMeasure is open, so a categorical chart takes the values' witness: Chart.Category.boxplot. Chart.Category.Boxplot(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
        let Boxplot (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

        [<Obsolete("Marey.UnitsOfMeasure is open, so a categorical chart takes the values' witness: Chart.Category.violin. Chart.Category.Violin(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
        let Violin (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

/// D109 — **the encoding block in the unit-required mode**: every channel that takes a witness
/// takes one, and an accessor without a measure stated for it is FS3099 rather than an unspecified
/// axis. The block's other operations are inherited from the core's; only the witness-free forms
/// `Marey.PlotBuilder` adds are missing, which is the whole difference.
type PlotBuilder<'r>(rows: 'r[]) =
    inherit PlotBuilderBase<'r>(rows)

/// §9 — the encoding block's entry point in the unit-required mode. It shadows the core's `plot`,
/// which is how the block, a class whose members cannot be shadowed one by one, changes mode with
/// the `open`.
[<AutoOpen>]
module Grammar =

    /// Open an encoding block over a dataset, every channel with its witness.
    let plot (data: 'r seq) : PlotBuilder<'r> = PlotBuilder<'r>(Seq.toArray data)
