namespace Marey

open System
open System.Runtime.InteropServices

/// **The quick, matplotlib-shaped front door.**
///
/// ```fsharp
/// Chart.line U.second U.metre ts positions
/// |> Chart.withTitle "Free fall"
/// |> Chart.toSvg SvgBackend.render "fall.svg" (Chart.size 800.0 500.0)
/// ```
///
/// ### Why the witnesses are arguments and cannot be anything else
///
/// There is no alternative to passing them. `float(s)` **is** `float` at runtime; a `float(s)[]`
/// carries no evidence that it is seconds. Something has to hand the library the runtime
/// witness, exactly once, and `U.second` is that something.
///
/// What is *not* configured is everything downstream of it: the scale, the domain, the prefix,
/// the tick values, the tick labels and both axis titles. `Chart.line U.second U.metre ts xs`
/// prints `t [s]` and `x [m]`, and the same call on data spanning 1e-9…5e-9 m prints `x [nm]`
/// with ticks reading 1, 2, 3, 4, 5. That is the payoff, and it survives being handed the
/// witness because the witness is the *only* thing supplied.
///
/// ### Nothing here mutates
///
/// Plotly.NET's documented caveat is exactly the one thing this module could get wrong: its
/// styling functions *mutate* the chart. Every `with*` and `add*` below returns a new
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

    /// **A bar is read as a length from zero, so zero is in the domain.**
    ///
    /// A bar chart over 7.8 … 19.1 on a 7.2 … 19.7 axis draws the smallest value as a sliver
    /// and the largest as a full-height bar — a ratio of about 50:1 for data whose real ratio
    /// is 2.4:1. That is not a styling preference, it is the mark type's encoding: length
    /// *means* magnitude, and a baseline that is not zero misstates every comparison the chart
    /// exists to support. `Geom.baselineOf` already says as much for areas, and its clamp is
    /// the fallback for a domain that could not be widened, not a licence to leave one narrow.
    ///
    /// Zero enters **unpadded**, and only on the side it is missing from: all-positive data
    /// gets `0 … hi+pad`, all-negative gets `lo-pad … 0`, and data straddling zero is
    /// unaffected because zero is already inside. Padding below zero would lift the baseline
    /// off the axis, which is the thing being fixed.
    let private withZero (lo: float<'u>, hi: float<'u>) : float<'u> * float<'u> =
        min lo (tag<'u> 0.0), max hi (tag<'u> 0.0)

    let internal series (name: string) (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Series<'ux, 'uy> =
        if x.Length <> y.Length then
            invalidArg "y" (sprintf "Marey: series '%s' has %d x values and %d y values." name x.Length y.Length)

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
          Channels = [||]
          ShowInLegend = not (System.String.IsNullOrEmpty name)
          Addressed = [||]
          RowKeys = [||]
          InDomain = (true, true)
          SecondaryY = false }

    // ---- Constructors ------------------------------------------------------------------------

    /// A figure built from an already-scaled typed series. The full-control door: pick the
    /// scales yourself and everything downstream still works.
    let ofSeries (g: Geom) (s: Series<'ux, 'uy>) : Figure =
        figureOf [| layerOf s.Name Theme.initial 0 g (Erase.packSeries s) |]

    /// A figure from an already-reunified `PackedSeries` — the door the encoding CE comes
    /// through. The reunifier lives in `Marey.Core` and terminates there; this
    /// is what lets it, so nothing in `Marey.Plot` has to write a rank-2 consumer of its own.
    let ofPacked (g: Geom) (p: PackedSeries) : Figure =
        figureOf [| layerOf (Erase.seriesName p) Theme.initial 0 g p |]

    let internal line (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        ofSeries GeomLine (series "" ux uy x y)

    let internal scatter (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        ofSeries GeomPoint (series "" ux uy x y)

    let internal area (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        let s = series "" ux uy x y
        ofSeries (Geom.area s) s

    let internal bar (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        let s = series "" ux uy x y

        let s =
            { s with
                YScale = Scale.withDomain (withZero (paddedOr 0.05 y)) s.YScale }

        ofSeries (Geom.bar 0.7 s) s

    let internal errorbar (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (err: float<'uy>[]) : Figure =
        if err.Length <> y.Length then
            invalidArg "err" (sprintf "Marey: %d y values but %d error values." y.Length err.Length)

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
            [| layerOf "" Theme.initial 0 bars packed
               { layerOf "" Theme.initial 0 GeomPoint packed with
                   ShowInLegend = false
                   Addressed = [||]
                   RowKeys = [||] } |]

    // ---- Phase 6's constructors ---------------------------------------------------------------

    let internal step (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        ofSeries (GeomStep StepAfter) (series "" ux uy x y)

    let internal stepWith (align: StepAlign) (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        ofSeries (GeomStep align) (series "" ux uy x y)

    /// The series a band is drawn against: x, the midline, and a y domain over **both** bounds.
    ///
    /// The midline is the series' y because a `PackedSeries` has to have one and the geom reads
    /// only x from it — but it is the honest choice rather than a placeholder: it is what the
    /// axis-sharing union in `Figure.unify` will compare against, and a band whose domain was
    /// taken from one bound would clip the other.
    let private ribbonSeries
        (name: string)
        (ux: U<'ux>)
        (uy: U<'uy>)
        (x: float<'ux>[])
        (low: float<'uy>[])
        (high: float<'uy>[])
        : Series<'ux, 'uy> * Geom =
        if low.Length <> x.Length || high.Length <> x.Length then
            invalidArg
                "low"
                (sprintf "Marey: %d x values but %d low and %d high values." x.Length low.Length high.Length)

        let s: Series<'ux, 'uy> =
            { Name = name
              X = x
              Y = Array.init x.Length (fun i -> (low.[i] + high.[i]) * 0.5)
              XScale = Scale.linear ux (paddedOr 0.05 x)
              YScale = Scale.linear uy (paddedOr 0.05 (Array.append low high)) }

        s, Geom.ribbon s low high

    let internal ribbon (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (low: float<'uy>[]) (high: float<'uy>[]) : Figure =
        let s, g = ribbonSeries "" ux uy x low high
        ofSeries g s

    /// The exact extent a tessellation occupies: the data's range, out to the outer edge of the
    /// outermost tile, and **unpadded**.
    ///
    /// Same rule as a histogram's x domain, for the same reason. A grid of tiles partitions a
    /// region; a margin past the last tile draws a piece of that region as if no cell covered
    /// it, and a reader takes the gap for missing data.
    let private tileDomain (v: float<'u>[]) (half: float<'u>) : float<'u> * float<'u> =
        match Scale.domainOf v with
        | Some(lo, hi) -> lo - half, hi + half
        | None -> tag<'u> 0.0, tag<'u> 1.0

    /// **The grid pitch of a coordinate array**: the smallest positive gap between two of its
    /// values.
    ///
    /// On a regular grid — which is what a tiled figure is — that gap *is* the cell size, and
    /// taking the smallest rather than the mean is what keeps a grid with a missing row from
    /// widening every tile to cover the hole.
    let private pitch (v: float<'u>[]) : float<'u> =
        let gaps =
            v
            |> Array.filter (fun x -> not (System.Double.IsNaN(float x)) && not (System.Double.IsInfinity(float x)))
            |> Array.sort
            |> Array.pairwise
            |> Array.map (fun (a, b) -> b - a)
            |> Array.filter (fun g -> float g > 0.0)

        if gaps.Length = 0 then tag<'u> 1.0 else Array.min gaps

    let internal tile
        (ux: U<'ux>)
        (uy: U<'uy>)
        (w: float<'ux>)
        (h: float<'uy>)
        (x: float<'ux>[])
        (y: float<'uy>[])
        : Figure =
        if x.Length <> y.Length then
            invalidArg "y" (sprintf "Marey: %d x values and %d y values." x.Length y.Length)

        let s: Series<'ux, 'uy> =
            { Name = ""
              X = x
              Y = y
              XScale = Scale.linear ux (tileDomain x (w * 0.5))
              YScale = Scale.linear uy (tileDomain y (h * 0.5)) }

        ofSeries (Geom.tile w h s) s

    let internal image
        (ux: U<'ux>)
        (uy: U<'uy>)
        (img: Image)
        (x0: float<'ux>)
        (y0: float<'uy>)
        (x1: float<'ux>)
        (y1: float<'uy>)
        (smooth: bool)
        : Figure =
        if img.Width <= 0 || img.Height <= 0 then
            invalidArg "img" (sprintf "Marey: an image of %dx%d has no pixels to draw." img.Width img.Height)

        let expected = int64 img.Width * int64 img.Height * 4L
        let actual = if isNull img.Rgba then 0L else int64 img.Rgba.Length

        // Checked here rather than left to the backends, which pad the missing rows with
        // transparent black and say so in a comment. That is the right behaviour for a scene
        // built by hand and the wrong one for a constructor: at this level the caller has the
        // array in their hand and a wrong stride is a bug in the line above, not a figure to
        // draw approximately.
        if actual <> expected then
            invalidArg "img" (sprintf "Marey: a %dx%d RGBA image needs %d bytes; got %d." img.Width img.Height expected actual)

        // Two points, the box's opposite corners, so the axes fit the image exactly. They are
        // also what any later layer's domain is unified against, which is what makes
        // `Chart.image … |> Chart.addScatter …` land the points on the picture.
        let s: Series<'ux, 'uy> =
            { Name = ""
              X = [| x0; x1 |]
              Y = [| y0; y1 |]
              XScale = Scale.linear ux (min x0 x1, max x0 x1)
              YScale = Scale.linear uy (min y0 y1, max y0 y1) }

        ofSeries (Geom.image s img x0 y0 x1 y1 smooth) s

    let internal segment
        (ux: U<'ux>)
        (uy: U<'uy>)
        (x0: float<'ux>[])
        (y0: float<'uy>[])
        (x1: float<'ux>[])
        (y1: float<'uy>[])
        : Figure =
        if x0.Length <> y0.Length || x1.Length <> x0.Length || y1.Length <> x0.Length then
            invalidArg "x1" (sprintf "Marey: a segment needs four arrays of one length; got %d, %d, %d, %d." x0.Length y0.Length x1.Length y1.Length)

        let s: Series<'ux, 'uy> =
            { Name = ""
              X = x0
              Y = y0
              XScale = Scale.linear ux (paddedOr 0.05 (Array.append x0 x1))
              YScale = Scale.linear uy (paddedOr 0.05 (Array.append y0 y1)) }

        ofSeries (Geom.segment s x1 y1) s

    let internal text (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (labels: string[]) : Figure =
        if labels.Length <> x.Length then
            invalidArg "labels" (sprintf "Marey: %d positions and %d labels." x.Length labels.Length)

        ofSeries (Geom.text labels) (series "" ux uy x y)

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

    /// Draw `top`'s layers over the last panel of `fig`, styles and channels as they were built.
    ///
    /// The general form of `addLine` and its siblings, for layers that were built by something
    /// other than this module — a field's heatmap and its contours (`Marey.Fields`), or any two
    /// figures over the same quantities. The panels' axes are unified at compile time and must
    /// agree in dimension, exactly as for `addLine`.
    let overlay (top: Figure) (fig: Figure) : Figure =
        match Array.tryLast top.Panels with
        | None -> fig
        | Some p -> fig |> addLayers (fun _ -> p.Layers)

    /// **Add a figure's layers as the next series**: `Chart.add (Chart.Line(xs, ys)) fig`.
    ///
    /// The unit-optional mode's one spelling for what `addLine` and its nine siblings do. Unlike
    /// `overlay`, which keeps the top figure's styles because the field marks are built to be
    /// drawn as they came, this gives every layer of `top` the *next* palette slot of `fig`'s
    /// theme, as `addLine` does — one slot for all of them, so the bars and the points of an
    /// error-bar figure stay one colour. A style set on `top` before adding is replaced; style
    /// the added layer afterwards, as after `addLine`. Names, channels and keys are kept.
    let add (top: Figure) (fig: Figure) : Figure =
        match Array.tryLast top.Panels with
        | None -> fig
        | Some p ->
            fig
            |> addLayers (fun i -> p.Layers |> Array.map (fun l -> { l with Style = Geom.styleOf fig.Theme i l.Geom }))

    let internal addLine (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        fig
        |> addLayers (fun i -> [| layerOf "" fig.Theme i GeomLine (Erase.packSeries (series "" ux uy x y)) |])

    let internal addScatter (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        fig
        |> addLayers (fun i -> [| layerOf "" fig.Theme i GeomPoint (Erase.packSeries (series "" ux uy x y)) |])

    let internal addArea (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        let s = series "" ux uy x y
        fig |> addLayers (fun i -> [| layerOf "" fig.Theme i (Geom.area s) (Erase.packSeries s) |])

    let internal addErrorbar (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (err: float<'uy>[]) (fig: Figure) : Figure =
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

    let internal addStep (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        fig
        |> addLayers (fun i -> [| layerOf "" fig.Theme i (GeomStep StepAfter) (Erase.packSeries (series "" ux uy x y)) |])

    let internal addRibbon
        (ux: U<'ux>)
        (uy: U<'uy>)
        (x: float<'ux>[])
        (low: float<'uy>[])
        (high: float<'uy>[])
        (fig: Figure)
        : Figure =
        let s, g = ribbonSeries "" ux uy x low high
        fig |> addLayers (fun i -> [| layerOf "" fig.Theme i g (Erase.packSeries s) |])

    let internal addSegment
        (ux: U<'ux>)
        (uy: U<'uy>)
        (x0: float<'ux>[])
        (y0: float<'uy>[])
        (x1: float<'ux>[])
        (y1: float<'uy>[])
        (fig: Figure)
        : Figure =
        let s: Series<'ux, 'uy> =
            { Name = ""
              X = x0
              Y = y0
              XScale = Scale.linear ux (paddedOr 0.05 (Array.append x0 x1))
              YScale = Scale.linear uy (paddedOr 0.05 (Array.append y0 y1)) }

        fig
        |> addLayers (fun i -> [| layerOf "" fig.Theme i (Geom.segment s x1 y1) (Erase.packSeries s) |])

    // ---- Rules and spans ----------------------------------------------------------------

    /// A rule's layer. Its data is one point, or two for a span, on the axis it marks and NaN
    /// on the other: enough for `unify` to check its unit against the axis and for
    /// `withDomainIncluded` to have a value to put in, and nothing for an axis it does not
    /// constrain. Out of the domain on both axes, in the rule's own style, and in the
    /// legend only once it is named.
    let private ruleLayer (theme: Theme) (i: int) (rule: RuleSpec<1>) (data: PackedSeries) : Layer =
        { layerOf "" theme i (GeomRule rule) data with
            Style = Geom.ruleStyle theme rule
            InDomain = (false, false)
            SecondaryY = false }

    let private ruleFigure (rule: RuleSpec<1>) (s: Series<'ux, 'uy>) : Figure =
        figureOf [| ruleLayer Theme.initial 0 rule (Erase.packSeries s) |]

    let private addRule (rule: RuleSpec<1>) (s: Series<'ux, 'uy>) (fig: Figure) : Figure =
        fig |> addLayers (fun i -> [| ruleLayer fig.Theme i rule (Erase.packSeries s) |])

    let private hRule (uy: U<'uy>) (y: float<'uy>) =
        RuleH(float y), series "" U.unspecified uy [| nan |] [| y |]

    let private vRule (ux: U<'ux>) (x: float<'ux>) =
        RuleV(float x), series "" ux U.unspecified [| x |] [| nan |]

    let private spanH (uy: U<'uy>) (lo: float<'uy>) (hi: float<'uy>) =
        SpanH(float lo, float hi), series "" U.unspecified uy [| nan; nan |] [| lo; hi |]

    let private spanV (ux: U<'ux>) (lo: float<'ux>) (hi: float<'ux>) =
        SpanV(float lo, float hi), series "" ux U.unspecified [| lo; hi |] [| nan; nan |]

    /// The second point is a run of `|x|` away (1 at zero), which on linear axes gives the slope
    /// exactly, whatever the run, and on a log axis a line that is straight on the page.
    let private throughRule (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>) (y: float<'uy>) (slope: float<'uy / 'ux>) =
        let run = if float x = 0.0 then tag<'ux> 1.0 else abs x
        RuleThrough(float x, float y, float (x + run), float (y + slope * run)), series "" ux uy [| x |] [| y |]

    let internal hLine (uy: U<'uy>) (y: float<'uy>) : Figure = hRule uy y ||> ruleFigure
    let internal vLine (ux: U<'ux>) (x: float<'ux>) : Figure = vRule ux x ||> ruleFigure
    let internal hSpan (uy: U<'uy>) (lo: float<'uy>) (hi: float<'uy>) : Figure = spanH uy lo hi ||> ruleFigure
    let internal vSpan (ux: U<'ux>) (lo: float<'ux>) (hi: float<'ux>) : Figure = spanV ux lo hi ||> ruleFigure

    let internal lineThrough (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>) (y: float<'uy>) (slope: float<'uy / 'ux>) : Figure =
        throughRule ux uy x y slope ||> ruleFigure

    let internal addHLine (uy: U<'uy>) (y: float<'uy>) (fig: Figure) : Figure =
        let rule, s = hRule uy y
        addRule rule s fig

    let internal addVLine (ux: U<'ux>) (x: float<'ux>) (fig: Figure) : Figure =
        let rule, s = vRule ux x
        addRule rule s fig

    let internal addHSpan (uy: U<'uy>) (lo: float<'uy>) (hi: float<'uy>) (fig: Figure) : Figure =
        let rule, s = spanH uy lo hi
        addRule rule s fig

    let internal addVSpan (ux: U<'ux>) (lo: float<'ux>) (hi: float<'ux>) (fig: Figure) : Figure =
        let rule, s = spanV ux lo hi
        addRule rule s fig

    let internal addLineThrough
        (ux: U<'ux>)
        (uy: U<'uy>)
        (x: float<'ux>)
        (y: float<'uy>)
        (slope: float<'uy / 'ux>)
        (fig: Figure)
        : Figure =
        let rule, s = throughRule ux uy x y slope
        addRule rule s fig

    // ---- Stats ----------------------------------------------------------------------

    /// The y domain of a quantity that cannot be negative and is read from zero: zero exactly,
    /// to the maximum plus breathing room *above only*.
    ///
    /// A bar's domain is widened to reach zero; this is the same rule for a quantity that never
    /// leaves the positive half-line, and it differs from `withZero (paddedOr …)` in the case
    /// that matters here. An empty histogram bin, or a density that reaches the axis, has the
    /// value zero — and padding below it draws a strip of negative count under the baseline,
    /// which is a region the quantity cannot occupy.
    let private fromZero (ys: float<'u>[]) : float<'u> * float<'u> =
        let hi =
            if Array.isEmpty ys then
                0.0
            else
                ys |> Array.fold (fun acc v -> if System.Double.IsNaN(float v) then acc else max acc (float v)) 0.0

        tag<'u> 0.0, tag<'u> (if hi <= 0.0 then 1.0 else hi * 1.05)

    /// **The bar series a histogram is drawn as**: bin centres against counts, bars
    /// touching, y from zero.
    ///
    /// **The x measure is the sample's and the y measure is `float<1>`**, and nothing had to be
    /// told so — which is the first place a stat pays out on the grammar's promise. A histogram
    /// of metres is a dimensionless count against a metre axis, and the count cannot end up on
    /// the metre axis by a transposition, because the two do not have the same type.
    ///
    /// The x domain is the outer bin edges *exactly*, unpadded, and the bars are full width.
    /// A histogram partitions an interval; a gap between the bars, or a margin past the last
    /// edge, both draw a piece of that interval as if no bin covered it.
    let histogramSeries (name: string) (h: Stat.Histogram<'u>) : Series<'u, 1> * Geom =
        let counts = h.Frequencies

        let s: Series<'u, 1> =
            { Name = name
              X = h.Centers
              Y = counts
              XScale = Scale.linear h.Unit (h.Edges.[0], h.Edges.[h.Edges.Length - 1])
              YScale = Scale.linear U.one (fromZero counts) }

        s, Geom.bar 1.0 s

    /// The line series a kernel density estimate is drawn as.
    ///
    /// **`Series<'u, 1/'u>`.** The y axis of a density over metres is in reciprocal metres, and
    /// this is where the library says it out loud: the axis labels itself `m⁻¹` off the witness
    /// `Stat.density` built, and `Figure.unify` refuses to put this layer on the same panel as
    /// the sample it summarises — which is correct, and is a mistake most plotting libraries
    /// will draw for you.
    let densitySeries (name: string) (d: Stat.Density<'u>) : Series<'u, 1 / 'u> * Geom =
        let s: Series<'u, 1 / 'u> =
            { Name = name
              X = d.X
              Y = d.Y
              XScale = Scale.linear d.Unit (d.X.[0], d.X.[d.X.Length - 1])
              YScale = Scale.linear d.DensityUnit (fromZero d.Y) }

        s, GeomLine

    /// The staircase an empirical CDF is drawn as, and the y domain it is known to occupy.
    ///
    /// Two points per distinct value — the foot and the head of its riser — because `GeomLine`
    /// through one point per value draws a diagonal where the function has a jump, and an ECDF
    /// interpolated between observations asserts data between them that was not observed.
    ///
    /// **`GeomStep` exists now and this still does not use it**, which is worth stating because
    /// the obvious reading is that it should. `GeomStep StepAfter` draws the riser
    /// *between* two points, so it has no riser to draw at the first observation — and the jump
    /// from zero to `1/n` at the smallest datum is the one an ECDF is read for. The doubled
    /// array draws it. `Chart.step` is the geom's own front door for the data that has no such
    /// edge.
    ///
    /// The y domain is `0 … 1` stated rather than measured: an ECDF's range is known before the
    /// data is, and taking it from the sample instead would start the axis at `1/n`.
    let ecdfSeries (name: string) (e: Stat.Ecdf<'u>) : Series<'u, 1> * Geom =
        let n = e.X.Length
        let xs = Array.zeroCreate<float<'u>> (2 * n)
        let ys = Array.zeroCreate<float> (2 * n)

        for i in 0 .. n - 1 do
            let below = if i = 0 then 0.0 else e.F.[i - 1]
            xs.[2 * i] <- e.X.[i]
            ys.[2 * i] <- below
            xs.[2 * i + 1] <- e.X.[i]
            ys.[2 * i + 1] <- e.F.[i]

        let s: Series<'u, 1> =
            { Name = name
              X = xs
              Y = ys
              XScale = Scale.linear e.Unit (paddedOr 0.05 e.X)
              YScale = Scale.linear U.one (0.0, 1.0) }

        s, GeomLine

    /// A figure from an already-computed histogram — the door for a caller who chose the bins.
    let ofHistogram (h: Stat.Histogram<'u>) : Figure =
        let s, g = histogramSeries "" h
        ofSeries g s

    let internal histogram (u: U<'u>) (xs: float<'u>[]) : Figure = ofHistogram (Stat.bin u xs)

    let internal histogramWith (u: U<'u>) (bins: int) (xs: float<'u>[]) : Figure = ofHistogram (Stat.binWith u bins xs)

    /// A figure from an already-computed density — the door for a caller who chose the bandwidth.
    let ofDensity (d: Stat.Density<'u>) : Figure =
        let s, g = densitySeries "" d
        ofSeries g s

    let internal density (u: U<'u>) (xs: float<'u>[]) : Figure = ofDensity (Stat.density u xs)

    /// A figure from an already-computed ECDF.
    let ofEcdf (e: Stat.Ecdf<'u>) : Figure =
        let s, g = ecdfSeries "" e
        ofSeries g s

    let internal ecdf (u: U<'u>) (xs: float<'u>[]) : Figure = ofEcdf (Stat.ecdf u xs)

    let internal addFit (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        let f = Stat.regression ux uy x y
        let fx, fy = Stat.fitLine f x

        fig
        |> addLayers (fun i -> [| layerOf "" fig.Theme i GeomLine (Erase.packSeries (series "" ux uy fx fy)) |])

    let internal addLoess (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        let l = Stat.loess ux uy x y

        fig
        |> addLayers (fun i -> [| layerOf "" fig.Theme i GeomLine (Erase.packSeries (series "" ux uy l.X l.Y)) |])

    let internal addLoessWith (span: float) (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) (fig: Figure) : Figure =
        let l = Stat.loessWith ux uy span Stat.DefaultLoessPoints x y

        fig
        |> addLayers (fun i -> [| layerOf "" fig.Theme i GeomLine (Erase.packSeries (series "" ux uy l.X l.Y)) |])

    let internal fit (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        let fx, fy = Stat.fitLine (Stat.regression ux uy x y) x
        ofSeries GeomLine (series "" ux uy fx fy)

    let internal loess (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        let l = Stat.loess ux uy x y
        ofSeries GeomLine (series "" ux uy l.X l.Y)

    let internal loessWith (span: float) (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        let l = Stat.loessWith ux uy span Stat.DefaultLoessPoints x y
        ofSeries GeomLine (series "" ux uy l.X l.Y)

    /// **The categorical front door**: a chart whose x is a category rather than a
    /// quantity.
    ///
    /// A sibling module rather than a `Chart.bars` beside `Chart.bar`, for two reasons. The
    /// names would differ by one letter while taking entirely different arguments, which is how
    /// a caller gets an error message about the wrong thing; and this module grows — a
    /// categorical scatter and a boxplot are the same axis with a different geom.
    ///
    /// The category is *stated as data*, exactly as a unit witness is: `keys` is one value per
    /// row, and the level set is derived from it in first-appearance order (`Levels.ofSeq`). The
    /// y axis is dimensioned as everywhere else, so `Chart.Category.bar U.metre keys yields`
    /// prints named bands under `x [m]` with nothing configured.
    ///
    /// Repeated keys are **not** aggregated: two rows with the same key are two bars in one
    /// band, which is honest about the data. Counting them is a stat's job, and `Category.count`
    /// is that stat with its chart — an explicit call, because doing it silently here would be
    /// the library deciding what the data means.
    [<RequireQualifiedAccess>]
    module Category =

        let internal series (name: string) (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Levels<'k> * Series<1, 'uy> =
            if keys.Length <> values.Length then
                invalidArg
                    "values"
                    (sprintf "Marey: series '%s' has %d keys and %d values." name keys.Length values.Length)

            let levels = Levels.ofSeq keys

            let s: Series<1, 'uy> =
                { Name = name
                  X = Levels.positions levels keys
                  Y = values
                  XScale = Levels.scale levels
                  YScale = Scale.linear uy (paddedOr 0.05 values) }

            levels, s

        /// The same series with the bar rule on y: zero is in the domain, because a bar is a
        /// length from it.
        let private barSeries (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) =
            let levels, s = series "" uy keys values

            levels,
            { s with
                YScale = Scale.withDomain (withZero (paddedOr 0.05 values)) s.YScale }

        /// A categorical axis is titled by its bands, so the conventional symbol is suppressed.
        ///
        /// `Axes.defaultLabel` reads the quantity name out of the dimension — `t` for a time
        /// axis, `x` for a length one — and a category's dimension is `1`, so it falls back to
        /// the axis's own name and prints a bare `x` underneath bands already labelled
        /// `control`, `treated`. Two different meanings of `x` in one figure, one of which is
        /// noise. An explicit empty label wins over the fallback, and `Chart.withXLabel` still
        /// puts a real name back.
        let private unlabelledX (fig: Figure) =
            { fig with
                Panels =
                    fig.Panels
                    |> Array.map (fun p ->
                        { p with
                            XAxis = { p.XAxis with Label = Some "" } }) }

        let internal bar (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Figure =
            let _, s = barSeries uy keys values
            ofSeries (Geom.bar 0.7 s) s |> unlabelledX

        let internal scatter (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Figure =
            let _, s = series "" uy keys values
            ofSeries GeomPoint s |> unlabelledX

        /// **`count`**: one bar per level, as tall as the number of rows that carry
        /// it.
        ///
        /// The categorical counterpart of `Chart.histogram`, and it has the same two measures:
        /// no x measure at all, because a category has none, and `float<1>` on y, because
        /// a count is a number of rows.
        ///
        /// The level order is `Stat.count`'s, which is `Levels.ofSeq`'s, which is
        /// first-appearance — so a counted axis and a plotted one put the same category in the
        /// same band.
        let count (keys: seq<'k>) : Figure =
            let pairs = Stat.count (Array.ofSeq keys)
            bar U.one (pairs |> Array.map fst) (pairs |> Array.map (snd >> float))

        /// One point per **level** rather than per row, which is what a summarising geom needs:
        /// a box plot has one box per band however many rows went into it.
        ///
        /// The x array is the level numbers `0 … n-1` in level order, so `medianSpacing` reads
        /// a pitch of exactly one band and a width fraction means what it says.
        let private bandSeries
            (uy: U<'uy>)
            (levels: Levels<'k>)
            (domain: float<'uy> * float<'uy>)
            (mid: float<'uy>[])
            : Series<1, 'uy> =
            { Name = ""
              X = Array.init levels.Values.Length float
              Y = mid
              XScale = Levels.scale levels
              YScale = Scale.linear uy domain }

        let internal boxplot (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Figure =
            let groups = Stat.groupBy keys values
            let levels = Levels.ofSeq (groups |> Array.map fst)
            let summaries = groups |> Array.map (fun (_, vs) -> Stat.summary uy vs)

            let reach =
                summaries
                |> Array.collect (fun q ->
                    let lo, hi = q.Extent
                    [| lo; hi |])

            let s =
                bandSeries uy levels (paddedOr 0.05 reach) (summaries |> Array.map (fun q -> q.Median))

            ofSeries (Geom.boxplot 0.6 s summaries) s |> unlabelledX

        let internal violin (uy: U<'uy>) (keys: 'k[]) (values: float<'uy>[]) : Figure =
            let groups = Stat.groupBy keys values
            let levels = Levels.ofSeq (groups |> Array.map fst)
            let h = tag<'uy> (Stat.frameOf values).Bandwidth

            if float h <= 0.0 then
                invalidArg
                    "values"
                    "Marey: a violin needs a sample with spread. Every value here is the same, so there is no density to estimate."

            let densities =
                groups
                |> Array.map (fun (_, vs) -> Stat.densityWith uy h Stat.DefaultDensityPoints vs)

            let reach =
                densities |> Array.collect (fun d -> [| d.X.[0]; d.X.[d.X.Length - 1] |])

            let s =
                bandSeries uy levels (paddedOr 0.05 reach) (groups |> Array.map (fun (_, vs) -> Stat.quantile 0.5 vs))

            ofSeries (Geom.violin 0.8 s densities) s |> unlabelledX

    /// The categorical front door in the unit-optional mode: `Chart.Category.Bar(keys,
    /// values, yUnit = U.metre)`. A type beside `module Category`, as `type Chart` stands beside
    /// `module Chart`, so `Chart.Category.count` still resolves to the module. The keys are data
    /// and have no unit; the values have the one stated, or none.
    [<AbstractClass; Sealed>]
    type Category =

        static member Series(name: string, keys: seq<'k>, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit) : Levels<'k> * Series<1, 1> =
            Category.series name (U.erase yUnit) (Array.ofSeq keys) (Array.ofSeq values)

        static member Bar(keys: seq<'k>, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit) : Figure =
            Category.bar (U.erase yUnit) (Array.ofSeq keys) (Array.ofSeq values)

        static member Scatter(keys: seq<'k>, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit) : Figure =
            Category.scatter (U.erase yUnit) (Array.ofSeq keys) (Array.ofSeq values)

        static member Boxplot(keys: seq<'k>, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit) : Figure =
            Category.boxplot (U.erase yUnit) (Array.ofSeq keys) (Array.ofSeq values)

        static member Violin(keys: seq<'k>, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit) : Figure =
            Category.violin (U.erase yUnit) (Array.ofSeq keys) (Array.ofSeq values)

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

        // A categorical channel's colours came off the outgoing theme's palette, so they follow
        // a retheme on exactly the rule a layer's style does: replaced when they are still what
        // the old theme would have chosen, kept when they are not. Without this, a rethemed
        // figure would draw its levels in the old palette while its unchannelled layers moved —
        // the worst of both, and silent.
        let retheme (c: Channel) =
            match c.Map with
            | ColorByLevel(levels, colors) when colors = Channel.levelColors old levels.Length ->
                { c with
                    Map = ColorByLevel(levels, Channel.levelColors t levels.Length) }
            | ShapeByLevel(levels, shapes) when shapes = Channel.levelShapes old levels.Length ->
                { c with
                    Map = ShapeByLevel(levels, Channel.levelShapes t levels.Length) }
            | _ -> c

        { fig with
            Theme = t
            Panels =
                fig.Panels
                |> Array.map (fun p ->
                    { p with
                        Layers =
                            p.Layers
                            |> Array.mapi (fun i l ->
                                let l =
                                    { l with
                                        Channels = l.Channels |> Array.map retheme }

                                if l.Style = Geom.styleOf old i l.Geom then
                                    { l with Style = Geom.styleOf t i l.Geom }
                                else
                                    l) }) }

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

    let withDash (d: seq<float<px>>) (fig: Figure) =
        mapLastLayer (fun l -> { l with Style = Geom.withDash (Array.ofSeq d) l.Style }) fig

    let withMarker (s: MarkerShape) (fig: Figure) =
        mapLastLayer (fun l -> { l with Style = Geom.withShape s l.Style }) fig

    /// **Give these rows nodes of their own**, by original row index.
    ///
    /// The IR batches a scatter's marks into one `Markers` node, so mark 42 has nothing a
    /// rewrite can address. Naming it here splits that batch three ways — before it, itself
    /// under `Keys.mark`, after it — and `Scene.mapKey (Keys.mark layer 42)` then reaches
    /// exactly one mark. Two extra nodes, not N.
    ///
    /// The storyboard's `highlight` calls this for you; it is public because addressing a mark
    /// is not an animation idea. Circling the outlier in a still figure wants the same node.
    ///
    /// Only a scatter (`GeomPoint`) can honour it — that is the geom whose marks are a batch.
    /// Rows named on a layer that draws a `Path` are carried and never asked for.
    let addressMarks (rows: seq<int>) (fig: Figure) : Figure =
        mapLastLayer (fun l -> { l with Addressed = Array.distinct (Array.append l.Addressed (Array.ofSeq rows)) }) fig

    /// **Name the rows of the most recently added series**, one key per row. Its marks
    /// are then keyed by these rather than by row: drop row 3 and every later mark keeps its
    /// name, and a storyboard's `morphTo` pairs each datum with the one of the same name. Keys
    /// must be unique; a duplicate is refused here, with the duplicated keys and their rows.
    ///
    /// `plot { key (fun r -> r.Station) }` is the same thing in the encoding block.
    let withKeys (keys: seq<string>) (fig: Figure) : Figure =
        let keys = Array.ofSeq keys
        mapLastLayer
            (fun l ->
                Keys.validate (Erase.seriesLength l.Data) keys
                { l with RowKeys = Array.copy keys })
            fig

    let withOpacity (o: float) (fig: Figure) =
        mapLastLayer (fun l -> { l with Style = Geom.withOpacity o l.Style }) fig

    /// **Let the most recently added layer move the axes.** A rule, a span or an
    /// annotation marks a place on the domain the data chose and stays out of it; this puts it
    /// back in, for the figure that must show the rule ("the panel must reach y = 0"). A data
    /// layer is in already, so on one this changes nothing.
    ///
    /// A rule joins on the axis it marks and only that one: a horizontal rule says nothing about
    /// x.
    let withDomainIncluded (fig: Figure) =
        mapLastLayer
            (fun l ->
                let inDomain =
                    match l.Geom with
                    | GeomRule(RuleH _)
                    | GeomRule(SpanH _) -> false, true
                    | GeomRule(RuleV _)
                    | GeomRule(SpanV _) -> true, false
                    | _ -> true, true

                { l with InDomain = inDomain })
            fig

    // ---- Annotations ------------------------------------------------------------

    /// An annotation's layer: one point, the place it marks, out of the domain on both axes
    ///, so labelling a point never moves the axes. The theme's foreground, not a palette
    /// colour. Text starts 6 px up and to the right of the point, so it does not cover the mark.
    let private annotationLayer (theme: Theme) (i: int) (text: string) (placement: TextPlacement<1>) (data: PackedSeries) : Layer =
        { layerOf "" theme i (GeomAnnotation(text, placement, ArrowNone)) data with
            Style = Geom.annotationStyle theme
            InDomain = (false, false)
            SecondaryY = false }

    let private besidePoint: TextPlacement<1> = TextOffset { X = 6.0<px>; Y = 6.0<px> }

    let internal annotation (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>) (y: float<'uy>) (text: string) : Figure =
        figureOf [| annotationLayer Theme.initial 0 text besidePoint (Erase.packSeries (series "" ux uy [| x |] [| y |])) |]

    let internal annotate (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>) (y: float<'uy>) (text: string) (fig: Figure) : Figure =
        fig
        |> addLayers (fun i -> [| annotationLayer fig.Theme i text besidePoint (Erase.packSeries (series "" ux uy [| x |] [| y |])) |])

    let private mapAnnotation (f: string * TextPlacement<1> * ArrowStyle -> string * TextPlacement<1> * ArrowStyle) (fig: Figure) =
        mapLastLayer
            (fun l ->
                match l.Geom with
                | GeomAnnotation(text, placement, arrow) -> { l with Geom = GeomAnnotation(f (text, placement, arrow)) }
                | _ -> l)
            fig

    /// Put the most recent annotation's text `dx`, `dy` px from its point, y up. A device
    /// offset is the default kind, because it stays put under a zoom and a change of figure size.
    let withTextOffset (dx: float<px>) (dy: float<px>) (fig: Figure) : Figure =
        mapAnnotation (fun (text, _, arrow) -> text, TextOffset { X = dx; Y = dy }, arrow) fig

    /// Put the most recent annotation's text at a place in data space, in the axes' units.
    /// `Marey.UnitsOfMeasure` has the spelling that takes a witness per axis.
    let withTextAt (x: float) (y: float) (fig: Figure) : Figure =
        mapAnnotation (fun (text, _, arrow) -> text, TextAt(x, y), arrow) fig

    /// An arrow from the most recent annotation's text to its point: `ArrowStraight`, or
    /// `ArrowArc bend` for a curve bent `bend` times its length to the left.
    let withArrow (arrow: ArrowStyle) (fig: Figure) : Figure =
        mapAnnotation (fun (text, placement, _) -> text, placement, arrow) fig

    /// Text in the last panel's plot area at (`x`, `y`), as fractions of its width and
    /// height from the bottom left: a note in a corner. It is anchored on the side it is near, so
    /// `(0.98, 0.98)` hangs from the top right corner. For panel letters, `withPanelLetters`.
    let addPanelText (x: float) (y: float) (text: string) (fig: Figure) : Figure =
        let data = Erase.packSeries (series "" U.unspecified U.unspecified [| nan |] [| nan |])
        fig |> addLayers (fun i -> [| annotationLayer fig.Theme i text (TextInPanel(x, y)) data |])

    /// **Letter the panels**, "(a)", "(b)", … in reading order (row, then column), at the
    /// top left of each plot area in the panel-title face, bold. Assigned when the figure is
    /// drawn, so a panel added afterwards by `beside` or `above` is lettered too.
    let withPanelLetters (letter: LetterStyle) (fig: Figure) : Figure =
        { fig with Letters = Some { Letter = letter; Bold = true } }

    /// `withPanelLetters`, saying whether the letters are bold.
    let withPanelLettersWith (letter: LetterStyle) (bold: bool) (fig: Figure) : Figure =
        { fig with Letters = Some { Letter = letter; Bold = bold } }

    /// Label the points of the most recently added series, one string per point.
    ///
    /// **It reuses that layer's own data and style**, which is the whole reason it is a
    /// combinator and not a constructor: the labels then sit on the marks they name, in the
    /// colour of the series they belong to, and there is no second copy of the positions to
    /// fall out of step with the first. It is not in the legend — the labels *are* the reading,
    /// so a swatch saying so adds nothing.
    ///
    /// `Chart.withStyle` on the result moves the labels, because the offset and the face live in
    /// the geom and the style: `Chart.addLabels names |> Chart.withStyle (Geom.withFont small)`.
    let addLabels (labels: seq<string>) (fig: Figure) : Figure =
        let labels = Array.ofSeq labels
        mapLast
            (fun p ->
                if p.Layers.Length = 0 then
                    p
                else
                    let last = p.Layers.[p.Layers.Length - 1]
                    let n = Erase.seriesLength last.Data

                    if labels.Length <> n then
                        invalidArg "labels" (sprintf "Marey: the series has %d points and %d labels." n labels.Length)

                    { p with
                        Layers =
                            Array.append
                                p.Layers
                                [| { last with
                                       Geom = Geom.text labels
                                       Channels = [||]
                                       ShowInLegend = false
                                       Addressed = [||] } |] })
            fig

    // ---- Non-positional channels -----------------------------------------------

    /// Attach a channel to the most recently added layer.
    ///
    /// The length check is here rather than inside `Channel` because here is where the caller's
    /// array is: a channel with the wrong number of values would otherwise silently leave the
    /// tail of a series unmapped, and unmapped points are not drawn.
    ///
    /// Attaching a channel also puts the layer **in** the legend, which is what `ShowInLegend`
    /// now means: a categorical channel gives an unnamed layer something to enumerate, and
    /// `Figure.layerEntries` reads the flag before it enumerates anything. Without this line the
    /// primary case — `Chart.scatter |> Chart.colorByKey`, where the layer's name is empty and
    /// the flag is therefore `false` — would lose its legend.
    let private withChannel (what: string) (values: int) (ch: Channel) (fig: Figure) : Figure =
        mapLastLayer
            (fun l ->
                let n = Erase.seriesLength l.Data

                if values <> n then
                    invalidArg
                        "values"
                        (sprintf "Marey: the %s channel has %d values but the series has %d points." what values n)

                { l with
                    Channels = Array.append l.Channels [| ch |]
                    ShowInLegend = true })
            fig

    /// **Colour by a category.** One colour per level, one batch per level, one legend entry
    /// per level — and the levels come from the keys themselves, in first-appearance order,
    /// exactly as `Chart.Category` derives them.
    ///
    /// ```fsharp
    /// Chart.scatter U.second U.metre ts xs
    /// |> Chart.colorByKey species
    /// |> Chart.shapeByKey species
    /// ```
    ///
    /// Applying both to the same keys is the colour-blind-safe scatter, and it is still *one
    /// batch per level*: the two channels read the same level number, so the grouping does not
    /// take their product.
    let colorByKey (keys: seq<'k>) (fig: Figure) : Figure =
        let keys = Array.ofSeq keys
        let levels = Levels.ofSeq keys

        withChannel "colour" keys.Length (Channel.colorByLevel fig.Theme levels.Labels (Levels.positions levels keys)) fig

    /// **Shape by a category** — the redundant encoding that keeps a scatter readable in
    /// greyscale and under colour-vision deficiency, which is why `MarkerShape` is on the
    /// batched node at all.
    let shapeByKey (keys: seq<'k>) (fig: Figure) : Figure =
        let keys = Array.ofSeq keys
        let levels = Levels.ofSeq keys

        withChannel "shape" keys.Length (Channel.shapeByLevel fig.Theme levels.Labels (Levels.positions levels keys)) fig

    let internal colorByWith (ramp: Ramp) (u: U<'u>) (values: float<'u>[]) (fig: Figure) : Figure =
        let domain =
            match Scale.domainOf values with
            | Some d -> d
            | None -> tag<'u> 0.0, tag<'u> 1.0

        withChannel "colour" values.Length (Channel.colorByValue "" ramp (Scale.linear u domain) values) fig

    let internal colorBy (u: U<'u>) (values: float<'u>[]) (fig: Figure) : Figure =
        colorByWith fig.Theme.Sequential u values fig

    /// Name the quantity a colour channel encodes: `T` in `T [K]`. The unit half is never
    /// configured, exactly as on an axis.
    ///
    /// Without it the name is read from the channel's *dimension* — a kelvin channel is `T`, a
    /// time channel is `t` — and a dimension with no conventional symbol leaves the bar
    /// labelled with its unit alone rather than borrowing an axis's `y`.
    let withColorLabel (label: string) (fig: Figure) : Figure =
        mapLastLayer
            (fun l ->
                { l with
                    Channels =
                        l.Channels
                        |> Array.map (fun c ->
                            match c.Map with
                            | ColorByValue _ -> { c with Label = label }
                            | _ -> c) })
            fig

    let internal withColorDomain (u: U<'u>) (lo: float<'u>) (hi: float<'u>) (fig: Figure) : Figure =
        let angle = U.angleExponentOf u.Symbol u.Factors

        let rescale (scale: PackedScale) =
            scale.Accept
                { new IScaleConsumer<PackedScale> with
                    member _.Apply<[<Measure>] 'c>(s: Scale<'c>) =
                        // Either side unstated: no claim to check and no conversion to
                        // make, so the ends are taken as the numbers they are.
                        let both = s.Unit.Stated && u.Stated

                        if both && (s.Unit.Dim <> u.Dim || U.angleExponentOf s.Unit.Symbol s.Unit.Factors <> angle) then
                            invalidArg
                                "u"
                                (sprintf
                                    "Marey: a colour domain in %s cannot apply to a colour channel in %s."
                                    (UnitDisplay.renderExpanded Unicode u.Dim)
                                    (UnitDisplay.renderExpanded Unicode s.Unit.Dim))

                        let into (v: float<'u>) =
                            if both then U.ofSI s.Unit (U.toSI u v) else LanguagePrimitives.FloatWithMeasure<'c>(float v)

                        Erase.packScale (Scale.ofKind s.Kind s.Unit (into lo, into hi)) }

        mapLastLayer
            (fun l ->
                { l with
                    Channels =
                        l.Channels
                        |> Array.map (fun c ->
                            match c.Map with
                            | ColorByValue(scale, ramp) -> { c with Map = ColorByValue(rescale scale, ramp) }
                            | _ -> c) })
            fig

    let internal heatmap
        (ux: U<'ux>)
        (uy: U<'uy>)
        (uz: U<'uz>)
        (x: float<'ux>[])
        (y: float<'uy>[])
        (z: float<'uz>[])
        : Figure =
        tile ux uy (pitch x) (pitch y) x y |> colorBy uz z

    /// **A hexbin**, drawn: one hexagon per occupied cell, coloured by its density along
    /// the theme's sequential ramp, with a colorbar in `1/('ux·'uy)`. Positions in metres give
    /// a bar in m⁻². The hexagons are polygons in data space, projected vertex by vertex, so they
    /// stay where their points are on a log axis.
    let ofHexbin (h: Stat.Hexbin<'ux, 'uy>) : Figure =
        let w = float h.Width / 2.0
        let r = float h.RowSpacing

        let corners (cx: float) (cy: float) =
            [| cx, cy + 2.0 * r / 3.0
               cx - w, cy + r / 3.0
               cx - w, cy - r / 3.0
               cx, cy - 2.0 * r / 3.0
               cx + w, cy - r / 3.0
               cx + w, cy + r / 3.0 |]

        let hexes = Array.map2 (fun (x: float<'ux>) (y: float<'uy>) -> corners (float x) (float y)) h.X h.Y
        let xs = hexes |> Array.map (Array.map fst)
        let ys = hexes |> Array.map (Array.map snd)

        let domain (cs: float[][]) (fallback: float * float) =
            let all = Array.concat cs
            if all.Length = 0 then fallback else Array.min all, Array.max all

        let x0, x1 = domain xs (0.0, 1.0)
        let y0, y1 = domain ys (0.0, 1.0)

        let s: Series<'ux, 'uy> =
            { Name = ""
              X = h.X
              Y = h.Y
              XScale = Scale.linear h.XUnit (tag<'ux> x0, tag<'ux> x1)
              YScale = Scale.linear h.YUnit (tag<'uy> y0, tag<'uy> y1) }

        ofSeries (GeomPolygon(xs, ys)) s
        |> withStyle (fun st -> { st with FillOpacity = 1.0 })
        |> colorBy h.DensityUnit h.Density

    let internal hexbinWith (ux: U<'ux>) (uy: U<'uy>) (across: int) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        ofHexbin (Stat.hexbinWith ux uy across x y)

    let internal hexbin (ux: U<'ux>) (uy: U<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : Figure =
        ofHexbin (Stat.hexbin ux uy x y)

    let internal sizeByWith (smallest: float<px>) (largest: float<px>) (u: U<'u>) (values: float<'u>[]) (fig: Figure) : Figure =
        let domain =
            match Scale.domainOf values with
            | Some d -> d
            | None -> tag<'u> 0.0, tag<'u> 1.0

        withChannel "size" values.Length (Channel.sizeByValue "" smallest largest (Scale.linear u domain) values) fig

    let internal sizeBy (u: U<'u>) (values: float<'u>[]) (fig: Figure) : Figure =
        let m = fig.Theme.MarkerSize
        sizeByWith (m * 0.5) (m * 3.0) u values fig

    // ---- Axes ---------------------------------------------------------------------------------

    /// An axis configuration named after the quantity: the unit half of the title is never
    /// configured, only the quantity name.
    let axis (label: string) : AxisConfig = { Figure.axisConfig with Label = Some label }

    let withXAxis (cfg: AxisConfig) (fig: Figure) = mapLast (fun p -> { p with XAxis = cfg }) fig
    let withYAxis (cfg: AxisConfig) (fig: Figure) = mapLast (fun p -> { p with YAxis = cfg }) fig

    /// How much of the last panel's x axis is drawn: `AxisFull`, `AxisUntitled`, `AxisUnlabelled`,
    /// `AxisBare` or `AxisHidden`. Only what is drawn changes, so stated ticks, a prefix, a domain
    /// or a reversal set before or after it stay. A shared axis still hides the inner
    /// panels' numbers and title whatever their look.
    let withXLook (look: AxisLook) (fig: Figure) =
        mapLast (fun p -> { p with XAxis = Figure.withLook look p.XAxis }) fig

    /// How much of the last panel's y axis is drawn. See `withXLook`.
    let withYLook (look: AxisLook) (fig: Figure) =
        mapLast (fun p -> { p with YAxis = Figure.withLook look p.YAxis }) fig

    /// `withXLook` and `withYLook` at once: `withAxesLook AxisUntitled` is an inset whose axes are
    /// its host's quantities, `withAxesLook AxisHidden` an image without axes.
    let withAxesLook (look: AxisLook) (fig: Figure) = fig |> withXLook look |> withYLook look

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
                    XAxis = { p.XAxis with Style = Some s }
                    YAxis = { p.YAxis with Style = Some s } })
            fig

    /// The last panel's x scale, for every layer on it and every layer added to it later.
    let withXScaleKind (k: ScaleKind) (fig: Figure) =
        mapLast (fun p -> Figure.adoptKinds { p with XAxis = { p.XAxis with Kind = Some k } }) fig

    /// The last panel's left y scale, for its layers there and those added there later.
    let withYScaleKind (k: ScaleKind) (fig: Figure) =
        mapLast (fun p -> Figure.adoptKinds { p with YAxis = { p.YAxis with Kind = Some k } }) fig

    /// The last panel's right y scale, giving the panel a right axis if it has none.
    let withRightYScaleKind (k: ScaleKind) (fig: Figure) =
        mapLast
            (fun p -> Figure.adoptKinds { p with Y2Axis = Some { (defaultArg p.Y2Axis Figure.axisConfig) with Kind = Some k } })
            fig

    /// Put the x axis on a log scale. The domain is re-derived from the positive data, because a
    /// padded linear domain usually straddles zero and a log scale cannot. The scale is the
    /// panel's: a layer added later, a rule included, is drawn on it too.
    let withXLog (fig: Figure) = withXScaleKind (ScaleKind.Log 10.0) fig

    /// Put the left y axis on a log scale. The layers on a second axis keep theirs; see
    /// `withRightYLog`.
    let withYLog (fig: Figure) = withYScaleKind (ScaleKind.Log 10.0) fig

    /// Put the right y axis on a log scale, for the layers `withRightAxis` or `toRightAxis` put
    /// there.
    let withRightYLog (fig: Figure) = withRightYScaleKind (ScaleKind.Log 10.0) fig

    /// The last panel's x domain, in raw numeric values of the axis's own unit. Raw because the
    /// measure has already been erased into the figure; use `Chart.ofSeries` with a hand-built
    /// scale when you want the domain checked. On a shared x it is the group's: every
    /// panel the axis is shared with takes it.
    let withXDomain (lo: float) (hi: float) (fig: Figure) =
        if fig.Panels.Length = 0 then
            fig
        else
            let last = fig.Panels.[lastPanel fig]
            Figure.withDomainsShared (fun p -> p.Row = last.Row && p.Col = last.Col) (Some(lo, hi)) None fig

    let withYDomain (lo: float) (hi: float) (fig: Figure) =
        if fig.Panels.Length = 0 then
            fig
        else
            let last = fig.Panels.[lastPanel fig]
            Figure.withDomainsShared (fun p -> p.Row = last.Row && p.Col = last.Col) None (Some(lo, hi)) fig

    // ---- Stated ticks, the prefix, reversal ------------------------------

    let internal withXTickSpec (ticks: TickSpec) (unit: U<1>) (fig: Figure) =
        mapLast (fun p -> { p with XAxis = { p.XAxis with Ticks = ticks; TickUnit = unit } }) fig

    let internal withYTickSpec (ticks: TickSpec) (unit: U<1>) (fig: Figure) =
        mapLast (fun p -> { p with YAxis = { p.YAxis with Ticks = ticks; TickUnit = unit } }) fig

    /// A tick at every multiple of `step` on the x axis, in the axis's own numbers (the
    /// unit its data is in, not the prefix it is labelled in): `withXTicksEvery 50e-9` on a
    /// wavelength axis in metres gives 400, 450, … under `λ [nm]`. More than 50 ticks is refused.
    /// `Marey.UnitsOfMeasure` has the spelling that takes a witness and checks it.
    let withXTicksEvery (step: float) (fig: Figure) = withXTickSpec (TicksEvery step) U.unspecified fig

    let withYTicksEvery (step: float) (fig: Figure) = withYTickSpec (TicksEvery step) U.unspecified fig

    /// Ticks at these values on the x axis, labelled as chosen ticks would be. Values
    /// outside the domain are left out.
    let withXTicksAt (values: seq<float>) (fig: Figure) = withXTickSpec (TicksAt(Array.ofSeq values)) U.unspecified fig

    let withYTicksAt (values: seq<float>) (fig: Figure) = withYTickSpec (TicksAt(Array.ofSeq values)) U.unspecified fig

    /// Ticks at these values on the x axis, each with its own label: the high-symmetry
    /// points of a band structure, `X`, `Γ`, `X`. The title keeps its unit, and a named tick is
    /// never dropped to make room (it may be tilted).
    let withXTicksNamed (ticks: seq<float * string>) (fig: Figure) = withXTickSpec (TicksNamed(Array.ofSeq ticks)) U.unspecified fig

    let withYTicksNamed (ticks: seq<float * string>) (fig: Figure) = withYTickSpec (TicksNamed(Array.ofSeq ticks)) U.unspecified fig

    /// Write the x axis in `prefix` rather than the one its domain selects:
    /// `withXPrefix Prefix.none` for an axis in plain metres, `Prefix.milli` for millimetres
    /// whatever the data's range.
    let withXPrefix (prefix: Prefix) (fig: Figure) = mapLast (fun p -> { p with XAxis = { p.XAxis with Prefix = Some prefix } }) fig

    let withYPrefix (prefix: Prefix) (fig: Figure) = mapLast (fun p -> { p with YAxis = { p.YAxis with Prefix = Some prefix } }) fig

    /// Run the x axis from its high end to its low end, over the domain it would have had.
    /// `withXDomain hi lo` reverses an axis too, over numbers you state.
    let withXReversed (fig: Figure) = mapLast (fun p -> { p with XAxis = { p.XAxis with Reversed = true } }) fig

    /// The y axis from its high end down: a depth below a surface, a magnitude.
    let withYReversed (fig: Figure) = mapLast (fun p -> { p with YAxis = { p.YAxis with Reversed = true } }) fig

    /// The x axis's minor ticks: `MinorOff`, `MinorCount 5`, `MinorEvery step` in the axis's
    /// own numbers, or `MinorAuto` to follow the theme. A stated step or count draws them under
    /// any theme.
    let withXMinorTicks (minor: MinorSpec) (fig: Figure) = mapLast (fun p -> { p with XAxis = { p.XAxis with Minor = minor } }) fig

    let withYMinorTicks (minor: MinorSpec) (fig: Figure) = mapLast (fun p -> { p with YAxis = { p.YAxis with Minor = minor } }) fig

    /// How the x axis's tick numbers are written: `FormatFixed 2`, `FormatPercent`,
    /// `FormatScientific` (a power of ten in the title, `x [10⁻³ m]`), or `TickFormat.custom f` of
    /// the number the label would show. Named ticks keep their names.
    let withXTickFormat (format: TickFormat) (fig: Figure) = mapLast (fun p -> { p with XAxis = { p.XAxis with Format = format } }) fig

    let withYTickFormat (format: TickFormat) (fig: Figure) = mapLast (fun p -> { p with YAxis = { p.YAxis with Format = format } }) fig

    let withGrid (x: bool) (y: bool) (fig: Figure) =
        mapLast
            (fun p ->
                { p with
                    XAxis = { p.XAxis with Grid = Some x }
                    YAxis = { p.YAxis with Grid = Some y } })
            fig

    let withLegend (placement: LegendPlacement) (fig: Figure) = mapLast (fun p -> { p with Legend = placement }) fig

    /// Lay the last panel's legend out in `columns` columns, filled down then across.
    let withLegendColumns (columns: int) (fig: Figure) = mapLast (fun p -> { p with LegendColumns = max 1 columns }) fig

    /// Draw the last panel's legend with or without its frame, whatever the theme says. Without
    /// one it has no background either, so it does not mask the data under it.
    let withLegendFrame (framed: bool) (fig: Figure) = mapLast (fun p -> { p with LegendFrame = Some framed }) fig

    /// **One legend for the whole figure**, beside or below its panels, gathering every
    /// panel's entries once each, in place of the panels' own. Applied when the figure is drawn,
    /// so panels added afterwards are gathered too.
    let withFigureLegend (placement: FigureLegend) (fig: Figure) = { fig with Legend = Some placement }

    /// Swap the coordinate system. `Coord.polar` turns every geom on the panel polar and no
    /// geom finds out.
    let withCoord (c: Coord) (fig: Figure) = mapLast (fun p -> { p with Coord = c }) fig

    let withPanelTitle (t: string) (fig: Figure) = mapLast (fun p -> { p with Title = t }) fig

    // ---- Multi-panel ---------------------------------------------------------------------------

    /// Two figures' weights along the axis they are joined on: each side's, or ones where
    /// it states none, so a stated pair keeps its proportion beside an unstated panel.
    let private joined (aCount: int) (a: float[]) (bCount: int) (b: float[]) =
        if a.Length = 0 && b.Length = 0 then
            [||]
        else
            let fill n (ws: float[]) = if ws.Length = 0 then Array.create n 1.0 else ws
            Array.append (fill aCount a) (fill bCount b)

    /// The lines a side's rows end on, as fractions of the side: 0, its weights (ones where
    /// it states none) summed, 1.
    let private edgesOf (count: int) (weights: float[]) =
        let ws = Array.init count (fun i -> if i < weights.Length then weights.[i] else 1.0)
        let total = Array.sum ws
        Array.scan (+) 0.0 ws |> Array.map (fun e -> e / total)

    /// Two lines closer than this are one line.
    let private sameEdge (a: float) (b: float) = abs (a - b) < 1e-9

    /// **Two sides merged along the axis they stand side by side on.** Each side's
    /// rows end at the fractions its weights put them at, and the merged grid has a line wherever
    /// either side has one, so a stated proportion is a proportion of the grid and no line of one
    /// side has to fall between two of the other's. A side that states no weights, beside one with
    /// as many rows that does, takes the other's, and the two line up row by row.
    ///
    /// Returns the merged count, its weights (empty when every track is alike and neither side
    /// stated any), and, per side, where each of its old lines went.
    let private merge (aCount: int) (aWeights: float[]) (bCount: int) (bWeights: float[]) =
        let aWeights, bWeights =
            if aCount = bCount && aWeights.Length = 0 then bWeights, bWeights
            elif aCount = bCount && bWeights.Length = 0 then aWeights, aWeights
            else aWeights, bWeights

        let ea = edgesOf aCount aWeights
        let eb = edgesOf bCount bWeights

        let edges =
            Array.append ea eb
            |> Array.sort
            |> Array.fold
                (fun (acc: float list) e ->
                    match acc with
                    | last :: _ when sameEdge last e -> acc
                    | _ -> e :: acc)
                []
            |> List.rev
            |> Array.ofList

        let lineOf (e: float) = edges |> Array.findIndex (sameEdge e)
        let count = edges.Length - 1
        let sizes = Array.init count (fun i -> edges.[i + 1] - edges.[i])
        let mean = Array.average sizes

        let weights =
            if aWeights.Length = 0 && bWeights.Length = 0 && sizes |> Array.forall (fun w -> sameEdge w mean) then
                [||]
            else
                sizes |> Array.map (fun w -> w / mean)

        count, weights, Array.map lineOf ea, Array.map lineOf eb

    /// A panel moved onto the merged grid: it starts on the line its first row started on, and
    /// spans to the line its last row ended on.
    let private onLines (lines: int[]) (start: int) (span: int) =
        let first = lines.[start]
        first, lines.[start + max 1 span] - first

    /// Put `b`'s panels to the right of `a`'s. `b`'s figure-level title is dropped; use
    /// `withPanelTitle` for per-panel headings.
    ///
    /// When the two have different numbers of rows, each side's panels span what their
    /// share covers, so a single panel beside a stack of two runs the height of both, and no cell
    /// is left empty. The rows are merged by where they end (`merge`), so a pair drawn
    /// 3 : 1 beside a stack of three keeps its 3 : 1.
    let beside (b: Figure) (a: Figure) : Figure =
        let b = Figure.offsetShares a b
        let rows, weights, la, lb = merge (max 1 a.Rows) a.RowWeights (max 1 b.Rows) b.RowWeights

        let place (lines: int[]) (dc: int) (p: Panel) =
            let row, span = onLines lines (max 0 p.Row) p.RowSpan
            { p with Row = row; RowSpan = span; Col = p.Col + dc }

        { a with
            Cols = a.Cols + b.Cols
            Rows = rows
            RowWeights = weights
            ColWeights = joined (max 1 a.Cols) a.ColWeights (max 1 b.Cols) b.ColWeights
            Panels = Array.append (a.Panels |> Array.map (place la 0)) (b.Panels |> Array.map (place lb a.Cols)) }

    /// Put `b`'s panels below `a`'s, their columns merged as `beside` merges rows.
    let above (b: Figure) (a: Figure) : Figure =
        let b = Figure.offsetShares a b
        let cols, weights, la, lb = merge (max 1 a.Cols) a.ColWeights (max 1 b.Cols) b.ColWeights

        let place (lines: int[]) (dr: int) (p: Panel) =
            let col, span = onLines lines (max 0 p.Col) p.ColSpan
            { p with Col = col; ColSpan = span; Row = p.Row + dr }

        { a with
            Rows = a.Rows + b.Rows
            Cols = cols
            ColWeights = weights
            RowWeights = joined (max 1 a.Rows) a.RowWeights (max 1 b.Rows) b.RowWeights
            Panels = Array.append (a.Panels |> Array.map (place la 0)) (b.Panels |> Array.map (place lb a.Rows)) }

    /// **A second y axis, on the right**: `left |> Chart.withRightAxis right` draws
    /// `right`'s layers on the last panel of `left`, against a y axis of their own on the right,
    /// titled from `right`'s y config. Each side is built as a figure of its own, in either mode.
    /// The two must agree on x, and each side's layers on their y; both are checked here, at the
    /// call, and a layer added to a side later is checked against that side when it is drawn.
    ///
    /// A side with exactly one series has its axis drawn in that series' colour, which is how a
    /// reader tells which curve belongs to which side. `right`'s layers take the next palette
    /// slots of `left`, unless their style was set.
    let withRightAxis (right: Figure) (fig: Figure) : Figure =
        if right.Panels.Length <> 1 then
            failwithf "Marey: the right axis takes a one-panel figure; this one has %d panels." right.Panels.Length

        let rp = right.Panels.[0]

        let merged =
            fig
            |> addLayers (fun i ->
                rp.Layers
                |> Array.mapi (fun j l ->
                    let style = if l.Style = Geom.styleOf right.Theme j l.Geom then Geom.styleOf fig.Theme (i + j) l.Geom else l.Style
                    { l with SecondaryY = true; Style = style }))
            |> mapLast (fun p -> { p with Y2Axis = Some rp.YAxis })

        Figure.unify merged.Panels.[lastPanel merged] |> ignore
        merged

    /// Move the last layer to the last panel's right y axis, giving the panel one if it
    /// has none.
    let toRightAxis (fig: Figure) : Figure =
        fig
        |> mapLastLayer (fun l -> { l with SecondaryY = true })
        |> mapLast (fun p -> { p with Y2Axis = Some(defaultArg p.Y2Axis Figure.axisConfig) })

    /// The last panel's right y domain, raw, in that axis's unit.
    let withRightYDomain (lo: float) (hi: float) (fig: Figure) : Figure =
        if fig.Panels.Length = 0 then
            fig
        else
            let p = fig.Panels.[lastPanel fig]
            Figure.withPanelDomainY2 (p.Row, p.Col) (lo, hi) fig

    /// The last panel's right y axis's name, in place of the one its unit suggests:
    /// the secondary axis's when it has one, otherwise the second y axis's.
    let withRightYLabel (label: string) (fig: Figure) : Figure =
        mapLast
            (fun p ->
                match p.YAxis.Secondary with
                | Some sa -> { p with YAxis = { p.YAxis with Secondary = Some { sa with Label = Some label } } }
                | None -> { p with Y2Axis = Some { (defaultArg p.Y2Axis Figure.axisConfig) with Label = Some label } })
            fig

    // ---- Aspect ----------------------------------------------------------------------

    /// **One unit as long on y as on x** on the last panel: the plot is shrunk to the
    /// domain's shape and centred in its cell. A frame of an animation or a zoom whose domains have
    /// another shape is widened to the plot's, so the plot does not move. Both axes must be linear
    /// and of one dimension, compared in SI; checked here and again when the figure is drawn.
    let withEqualAspect (fig: Figure) : Figure =
        let shaped = mapLast (fun p -> { p with Aspect = Some Aspect.Equal }) fig

        if shaped.Panels.Length > 0 then
            let p = Figure.unify shaped.Panels.[lastPanel shaped]

            if p.Layers.Length > 0 then
                let sx, sy = Figure.axisScalesErased p

                if sx.Dim <> sy.Dim then
                    failwithf
                        "Marey: equal aspect needs one dimension on both axes; this panel's x is in %s and its y in %s."
                        (Dim.show sx.Dim)
                        (Dim.show sy.Dim)

        shaped

    /// The last panel's plot at this height over its width, whatever its domains.
    let withBoxAspect (heightOverWidth: float) (fig: Figure) : Figure =
        if not (heightOverWidth > 0.0) || System.Double.IsInfinity heightOverWidth then
            failwithf "Marey: a box aspect is a positive height over width; got %g." heightOverWidth

        mapLast (fun p -> { p with Aspect = Some(Aspect.Box heightOverWidth) }) fig

    // ---- Insets ------------------------------------------------------------------------

    /// **A one-panel figure drawn over the last panel's plot**, its own plot at
    /// `(x0, y0, x1, y1)` in fractions of the host's plot, y up (matplotlib's `inset_axes`). Its
    /// tick labels and titles stand round that rectangle, over the host. Its axes are its own, a
    /// zoom inside it zooms it, and its key is the host's with `inset-<i>` under it. It draws no
    /// colorbar. Refused for a figure of several panels, one with insets of its own, and one
    /// whose legend is outside.
    let withInset (x0: float, y0: float, x1: float, y1: float) (inset: Figure) (fig: Figure) : Figure =
        if inset.Panels.Length <> 1 then
            failwithf "Marey: an inset is a one-panel figure; this one has %d panels." inset.Panels.Length

        let ip = inset.Panels.[0]

        if ip.Insets.Length > 0 then
            failwith "Marey: an inset has no insets of its own."

        match ip.Legend with
        | LegendOutsideRight
        | LegendOutsideBottom -> failwith "Marey: an inset draws its legend in a corner; this one's is outside."
        | _ -> ()

        let inside (v: float) = v >= 0.0 && v <= 1.0

        if not (inside x0 && inside x1 && inside y0 && inside y1 && x1 > x0 && y1 > y0) then
            failwithf "Marey: an inset is placed at fractions of the host's plot, low before high, within 0 … 1; got (%g, %g, %g, %g)." x0 y0 x1 y1

        let at: Rect<nd> = { X0 = x0 * 1.0<nd>; Y0 = y0 * 1.0<nd>; X1 = x1 * 1.0<nd>; Y1 = y1 * 1.0<nd> }
        // An inset has no room for a colorbar: its colour keys leave the key, and its colours are
        // read against the host's bar when its domain is stated to match (`withColorDomain`).
        let quiet = ip.Layers |> Array.map (fun l -> if l.Channels.Length > 0 then { l with ShowInLegend = false } else l)

        let entry: Inset =
            { Panel = { ip with Layers = quiet; Legend = (if ip.Legend = LegendBest then LegendTopRight else ip.Legend) }
              At = at
              Indicate = false }
        mapLast (fun p -> { p with Insets = Array.append p.Insets [| entry |] }) fig

    /// Draw the last inset's domain on its host as a rectangle, joined to the inset by two
    /// lines (matplotlib's `indicate_inset_zoom`). The inset's axes must be in the host's
    /// dimensions; checked when the figure is drawn.
    let withInsetZoom (fig: Figure) : Figure =
        mapLast
            (fun p ->
                if p.Insets.Length = 0 then
                    failwith "Marey: `withInsetZoom` marks the last panel's last inset, and it has none."

                { p with Insets = p.Insets |> Array.mapi (fun i ins -> if i = p.Insets.Length - 1 then { ins with Indicate = true } else ins) })
            fig

    // ---- Secondary axes -------------------------------------------------------------

    let internal withSecondary (onX: bool) (u: U<1>) (map: SecondaryMap option) (fig: Figure) : Figure =
        let sa: SecondaryAxis = { Unit = u; Map = map; Label = None }

        mapLast
            (fun p ->
                if onX then
                    { p with XAxis = { p.XAxis with Secondary = Some sa } }
                else
                    { p with YAxis = { p.YAxis with Secondary = Some sa } })
            fig

    /// **The x axis again along the top, in another unit of its dimension**: inches over
    /// millimetres, °C over kelvin. The conversion is the units' own, offset included, so nothing
    /// is trusted; another dimension is refused when the figure is drawn, naming both. The ticks
    /// are chosen in the new unit.
    let withTopAxisIn (u: U<'v>) (fig: Figure) : Figure = withSecondary true (U.erase u) None fig

    /// The y axis again along the right, in another unit of its dimension.
    let withRightAxisIn (u: U<'v>) (fig: Figure) : Figure = withSecondary false (U.erase u) None fig

    /// **The x axis read along the top through a map**: photon energy over wavelength,
    /// period over frequency. `forward` takes the x axis's raw numbers to `u`'s and `inverse` takes
    /// them back. When the figure is drawn the pair is checked on 257 points of the domain —
    /// finite, strictly monotone, and `inverse (forward x)` back at `x` — and refused at the first
    /// point that fails. The ticks are chosen evenly in `u` and placed through `inverse`, so they
    /// fall unevenly along a reciprocal.
    let withTopAxisMapped (u: U<'v>) (forward: float -> float) (inverse: float -> float) (fig: Figure) : Figure =
        withSecondary true (U.erase u) (Some(SecondaryMap(forward, inverse))) fig

    /// The y axis read along the right through a map. See `withTopAxisMapped`.
    let withRightAxisMapped (u: U<'v>) (forward: float -> float) (inverse: float -> float) (fig: Figure) : Figure =
        withSecondary false (U.erase u) (Some(SecondaryMap(forward, inverse))) fig

    /// The secondary top axis's name, in place of the one its unit suggests.
    let withTopAxisLabel (label: string) (fig: Figure) : Figure =
        mapLast
            (fun p ->
                match p.XAxis.Secondary with
                | Some sa -> { p with XAxis = { p.XAxis with Secondary = Some { sa with Label = Some label } } }
                | None -> p)
            fig

    /// **Share the x axis of every panel the figure has now**, as a facet does: one
    /// domain, checked here for one dimension, and kept one afterwards. Only the outermost panel
    /// of each column writes its x tick labels and title. A panel composed in later is not part
    /// of the sharing; call `shareX` again to take it in.
    let shareX (fig: Figure) = Figure.shareRecorded ShareX fig

    /// `shareX` for y: only the leftmost panel of each row is labelled.
    let shareY (fig: Figure) = Figure.shareRecorded ShareY fig

    /// `shareX` and `shareY` at once.
    let shareAxes (fig: Figure) = Figure.shareRecorded ShareBoth fig

    /// Weights to the mean of 1, refused unless there is one per track and each is positive.
    let private weights (what: string) (count: int) (ws: float[]) =
        if ws.Length <> count then
            failwithf "Marey: %d %s weights for a figure of %d %s. Give one per %s." ws.Length what count what (what.TrimEnd 's')

        if ws |> Array.exists (fun w -> not (w > 0.0) || System.Double.IsInfinity w) then
            failwithf "Marey: %s weights must be positive and finite; got %s." what (ws |> Array.map string |> String.concat ", ")

        let mean = Array.average ws
        ws |> Array.map (fun w -> w / mean)

    /// **The rows' plot heights, relative**: `withRowHeights [| 3.0; 1.0 |]` draws the first
    /// row's plots three times as tall as the second's, whatever their gutters (matplotlib's
    /// `height_ratios`). One weight per row of the figure as composed so far.
    let withRowHeights (heights: seq<float>) (fig: Figure) =
        { fig with RowWeights = weights "rows" (max 1 fig.Rows) (Array.ofSeq heights) }

    /// The columns' plot widths, relative (matplotlib's `width_ratios`).
    let withColWidths (widths: seq<float>) (fig: Figure) =
        { fig with ColWeights = weights "columns" (max 1 fig.Cols) (Array.ofSeq widths) }

    /// **Panels laid out by a picture of the grid.** Each character of `layout` is a cell,
    /// rows separated by `;` or a newline, `.` an empty cell; every other character names a
    /// part, whose single panel covers the rectangle of cells that carry its character.
    /// `"AAB;DEB;DCC"` is a pinwheel of four panels around a fifth, a shape `beside` and `above`
    /// cannot nest into.
    ///
    /// Refused when a character's cells are not one rectangle, when the rows are not all the
    /// same length, when a character has no part or a part has no character, and when a part
    /// holds more than one panel.
    let mosaic (layout: string) (parts: (char * Figure) list) : Figure =
        let rows =
            layout.Split([| ';'; '\n' |], System.StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun r -> r.Trim())
            |> Array.filter (fun r -> r.Length > 0)

        if rows.Length = 0 then
            failwith "Marey: a mosaic needs at least one row."

        let width = rows.[0].Length

        rows
        |> Array.iteri (fun i r ->
            if r.Length <> width then
                failwithf "Marey: mosaic row %d ('%s') has %d cells and row 0 ('%s') has %d. Every row needs the same number." i r r.Length rows.[0] width)

        let labels =
            rows |> Array.collect (fun r -> r.ToCharArray()) |> Array.filter (fun c -> c <> '.') |> Array.distinct

        let parts = Map.ofList parts

        for c in labels do
            if not (parts.ContainsKey c) then
                failwithf "Marey: mosaic cell '%c' has no part. Give one as ('%c', figure)." c c

        for KeyValue(c, f) in parts do
            if not (Array.contains c labels) then
                failwithf "Marey: the part '%c' has no cell in the mosaic \"%s\"." c layout

            if f.Panels.Length <> 1 then
                failwithf
                    "Marey: mosaic part '%c' is a %d-panel figure, and a mosaic cell holds one panel. Compose it with `beside` or `above` instead."
                    c
                    f.Panels.Length

        let panels =
            labels
            |> Array.map (fun c ->
                let cells =
                    [| for r in 0 .. rows.Length - 1 do
                           for k in 0 .. width - 1 do
                               if rows.[r].[k] = c then yield r, k |]

                let r0 = cells |> Array.map fst |> Array.min
                let r1 = cells |> Array.map fst |> Array.max
                let c0 = cells |> Array.map snd |> Array.min
                let c1 = cells |> Array.map snd |> Array.max

                if cells.Length <> (r1 - r0 + 1) * (c1 - c0 + 1) then
                    failwithf "Marey: mosaic cell '%c' does not cover a rectangle in \"%s\". A panel spans whole rows and columns." c layout

                { parts.[c].Panels.[0] with
                    XShare = 0
                    YShare = 0
                    Row = r0
                    Col = c0
                    RowSpan = r1 - r0 + 1
                    ColSpan = c1 - c0 + 1 })

        { Figure.create panels with
            Rows = rows.Length
            Cols = width
            Theme = parts.[labels.[0]].Theme }

    // ---- Facets ----------------------------------------------------------------------

    /// The wrap width a facet takes when nobody states one: as square as the panel count allows,
    /// which is what keeps each panel's aspect ratio closest to the single-panel figure the
    /// caller was looking at a moment ago.
    let wrapCols (n: int) : int =
        if n <= 1 then 1 else int (ceil (sqrt (float n)))

    /// **The key is drawn once.** Every layer outside the first drawn panel leaves the legend and
    /// the colorbar.
    ///
    /// A facet's channels are built over the whole block and then sliced, so every panel's key is
    /// the same key. Repeating it says nothing the first one did not, and a colorbar repeated
    /// takes a column out of every panel to do it.
    let private keyOnFirstPanelOnly (fig: Figure) : Figure =
        let first =
            fig.Panels |> Array.tryFindIndex (fun p -> p.Layers.Length > 0)

        match first with
        | None -> fig
        | Some k ->
            { fig with
                Panels =
                    fig.Panels
                    |> Array.mapi (fun i p ->
                        if i = k then
                            p
                        else
                            { p with
                                Layers = p.Layers |> Array.map (fun l -> { l with ShowInLegend = false }) }) }

    /// **Lay independently built figures out as a facet**: a wrapped grid of panels
    /// over shared axes.
    ///
    /// This is the *hand-composed* facet, and it is the only one whose panels can disagree
    /// dimensionally — which makes it the one place the cross-panel `Dim` check can actually fire.
    /// The block's `facetWrap` splits one dataset by a key, so every panel there reads the same
    /// column under the same witness and a mismatch is unconstructible.
    ///
    /// Each part is a **single-panel** figure and a title for it. A multi-panel figure is refused
    /// rather than flattened: a facet cell holds one panel, and silently spreading two of them
    /// across the grid would put a caller's `beside` composition somewhere they did not ask for.
    let facetWith (mode: FacetScales) (cols: int) (parts: seq<string * Figure>) : Figure =
        let parts = Array.ofSeq parts
        if parts.Length = 0 then
            failwith "Marey: a facet needs at least one panel."

        parts
        |> Array.iteri (fun i (title, f) ->
            if f.Panels.Length <> 1 then
                failwithf
                    "Marey: facet cell %d ('%s') is a %d-panel figure, and a facet cell holds one panel. Compose it with `beside` or `above` instead, or facet the parts separately."
                    i
                    title
                    f.Panels.Length)

        let n = parts.Length
        let cols = max 1 (min cols n)

        let panels =
            parts
            |> Array.mapi (fun i (title, f) ->
                { f.Panels.[0] with
                    XShare = 0
                    YShare = 0
                    Title = title
                    Row = i / cols
                    Col = i % cols })

        { Figure.create panels with
            Theme = (snd parts.[0]).Theme }
        |> Figure.shareRecorded mode
        |> keyOnFirstPanelOnly

    /// `facetWith` sharing both axes — what a facet means — over an automatic wrap width.
    let facet (parts: seq<string * Figure>) : Figure =
        let parts = Array.ofSeq parts
        facetWith ShareBoth (wrapCols parts.Length) parts

    /// `facet` at a stated wrap width.
    let facetIn (cols: int) (parts: seq<string * Figure>) : Figure =
        facetWith ShareBoth cols (Array.ofSeq parts)

    // ---- Output ---------------------------------------------------------------------------------

    /// The scene, measured with the zero-dependency default measurer.
    let toScene (extent: Extent<px>) (fig: Figure) : Node =
        Figure.toScene TextMeasure.approx extent fig

    let toSceneWith (m: ITextMeasurer) (extent: Extent<px>) (fig: Figure) : Node = Figure.toScene m extent fig

    // ---- Output, for something that moves ---------------------------------------------------

    /// The layout this figure would be drawn in. One shot, one sample.
    ///
    /// `Chart.arrangement` still hands out the outer grid on its own, which is the older and
    /// narrower question; this is the whole layout, including each panel's gutters, and it is
    /// what `toSceneIn` takes.
    let arrangeFor (m: ITextMeasurer) (extent: Extent<px>) (fig: Figure) : Shot =
        Figure.arrangeFor m extent fig

    /// **Lay out once for the whole shot, against the union of its sampled states.**
    ///
    /// The reason this exists rather than "just call `toScene` per frame": the plot
    /// rectangle is what is left after the widest tick label has taken its room, so a domain
    /// crossing `9.5 → 10.5` gains a digit on every label at once and the panel *breathes* while
    /// the data is the thing supposed to be moving. Sizing against the union gives an arrangement
    /// no frame can overflow, and tick labels still recompute per frame inside it.
    ///
    /// ```fsharp
    /// let shot = Chart.arrangeUnion 32 TextMeasure.approx extent (fun u -> Anim.atFraction u anim)
    /// let frame u = Chart.toSceneIn TextMeasure.approx shot (Anim.atFraction u anim)
    /// ```
    ///
    /// `samples` is stated because the union is only as good as it: an animation with a spike
    /// between two samples can still overflow. Thirty-two is the usual default and a good one.
    let arrangeUnion (samples: int) (m: ITextMeasurer) (extent: Extent<px>) (sample: float -> Figure) : Shot =
        Figure.arrangeUnion samples m extent sample

    /// **One layout over states a caller lists**: `arrangeUnion` without the clock. A
    /// widget passes the states its controls reach at their extremes.
    let arrangeOver (m: ITextMeasurer) (extent: Extent<px>) (states: seq<Figure>) : Shot =
        Figure.arrangeOver m extent (Array.ofSeq states)

    /// **Whether this figure's labels fit a layout already decided**, planned exactly as
    /// `toSceneIn` would plan them. A state that does not fit is drawn in a new arrangement, and
    /// the widget counts it.
    let fitsIn (m: ITextMeasurer) (shot: Shot) (fig: Figure) : bool = Figure.fitsIn m shot fig

    /// **Draw a figure inside a layout that is already decided.** The frame renderer's entry
    /// point, and the one function in this section that an animation calls per frame.
    ///
    /// No `Extent<px>`: the shot carries the surface it was arranged against, and a second one
    /// beside it could only ever disagree.
    let toSceneIn (m: ITextMeasurer) (shot: Shot) (fig: Figure) : Node = Figure.toSceneIn m shot fig

    /// **Where each panel's plot area is in a shot, with its coord and scales**, so a
    /// point on the surface can be read back as data (`PanelView.toData`) and a dragged
    /// rectangle as two domains (`PanelView.domainsOf`).
    let panelsIn (m: ITextMeasurer) (shot: Shot) (fig: Figure) : PanelView[] = Figure.panelsIn m shot fig

    /// State the domains of the panel at `(row, col)`, raw, in each axis's own unit. What
    /// `withXDomain` and `withYDomain` do to the last panel, for any panel.
    let withPanelDomains (row: int, col: int) (dx: (float * float) option) (dy: (float * float) option) (fig: Figure) =
        Figure.withPanelDomains (row, col) dx dy fig

    /// The outer grid `toScene` chose — the head row and the panel regions. For inspection.
    let arrangement (m: ITextMeasurer) (extent: Extent<px>) (fig: Figure) : Arrangement =
        Figure.arrangement m extent fig

    /// Drive any backend.
    ///
    /// This is the path that honours the backend contract in full: layout measures against the
    /// backend's **own** `ITextMeasurer` (the measure–arrange pass against
    /// `IBackend.MeasureText`), and the scene is then run through `Degrade.forCaps` and
    /// `Degrade.splitBatches` so a backend that cannot do gradients, dashes, clipping or
    /// unbounded batches gets a defensible approximation rather than wrong output.
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
    /// `Marey.Plot` takes no backend dependency, on purpose: the DSLs sit above the IR and the
    /// backends below it, and a grammar package that references a renderer inverts
    /// that. Passing the renderer in costs one identifier and keeps the layering honest.
    ///
    /// **The return type is the renderer's, not `string`.** Every function-shaped entry
    /// point once named `string`, which is a claim about the *artifact* smuggled into the
    /// signature of a function that only forwards it. A PNG has no UTF-8 encoding and a PDF's is
    /// a lie, so a raster backend does not fit through a `string`-typed hole. One type parameter
    /// removes the claim without adding an entry point: `SvgBackend.render` still gives back a
    /// `string`, and a byte-producing renderer gives back a `byte[]`.
    ///
    /// The `IBackend` protocol never had this problem — `Finish` returns `unit` and the artifact
    /// is exposed by the concrete type — so `renderTo` needed no change and got none.
    let render (renderer: Extent<px> -> Node -> 'a) (extent: Extent<px>) (fig: Figure) : 'a =
        renderer extent (toScene extent fig)

    // File output is .NET's. A Fable build has no file system to write to, and these do not
    // compile there.
#if !FABLE_COMPILER
    /// One line to a file: `fig |> Chart.toFile SvgBackend.render "plot.svg" (Chart.size 800.0 500.0)`.
    ///
    /// UTF-8 without a byte-order mark, matching `SvgBackend.renderToFile`, because these files
    /// are golden masters and a BOM appearing would read as a whole-file diff.
    ///
    /// Backend-neutral by name as well as by shape: any `Extent<px> -> Node -> string` renderer
    /// fits, so this is equally the way to write a `.tex` file.
    let toFile (renderer: Extent<px> -> Node -> string) (path: string) (extent: Extent<px>) (fig: Figure) : unit =
        let text = render renderer extent fig
        System.IO.File.WriteAllText(path, text, System.Text.UTF8Encoding(false))

    /// `toFile` under its original name.
    ///
    /// Kept because it appears in the README and in early examples, and because renaming a
    /// public entry point is not worth a breaking change. It was always renderer-shaped rather
    /// than SVG-specific — the name was the only thing that said otherwise.
    let toSvg (renderer: Extent<px> -> Node -> string) (path: string) (extent: Extent<px>) (fig: Figure) : unit =
        toFile renderer path extent fig

    /// `toFile` for a renderer that produces bytes: `fig |> Chart.toFileBytes SkiaBackend.renderPng
    /// "plot.png" (Chart.size 800.0 500.0)`.
    ///
    /// This exists even though `render` is generic, because writing a `string` and writing a
    /// `byte[]` are different calls whatever the renderer's return type is — `WriteAllText` has
    /// an encoding and `WriteAllBytes` does not. Generalising `toFile` over the same `'a` would
    /// mean branching on the runtime type inside it, which is worse than two names.
    ///
    /// Deliberately *not* encoding-aware: the bytes are written exactly as the renderer produced
    /// them, because a PNG that gained a BOM would not open.
    let toFileBytes (renderer: Extent<px> -> Node -> byte[]) (path: string) (extent: Extent<px>) (fig: Figure) : unit =
        System.IO.File.WriteAllBytes(path, render renderer extent fig)
#endif

/// **The unit-optional front door**: data as `float[]`, and a unit per axis that may
/// be stated or left out.
///
/// ```fsharp
/// Chart.Line(ts, xs, xUnit = U.second, yUnit = U.metre)
/// |> Chart.withTitle "displacement"
///
/// Chart.Bar(quarters, revenue)          // no unit stated on either axis
/// ```
///
/// A static class beside `module Chart`, so that construction is a method and everything after
/// it stays curried: `Chart.Line(…)` resolves here and `Chart.withTitle` to the module, as
/// `String.Join` and `String.concat` do. A method because only a method can have optional
/// parameters, and a parameter that may be omitted is the whole of this mode.
///
/// An omitted unit is `U.unspecified`: no bracket, no prefix, and no claim for the
/// axis-sharing check to act on. A stated one gives the axis everything a witness gives it in
/// `Marey.UnitsOfMeasure` — the bracketed label, the prefix, a derived unit for a stat, and the
/// dimension check when two layers share an axis — except that the check runs when the figure is
/// compiled rather than when the call is.
///
/// Every member is one line over the curried, measure-generic implementation at measure 1
///: there is one implementation of each chart, and this is a way of reaching it.
///
/// The units are typed `IUnit` because an optional parameter cannot be measure-generic, and are
/// declared with `Optional` and a `null` default rather than as F#'s `?xUnit`, because that is
/// the form C# also reads as optional. F# inserts the coercion from `U<m>` at the argument.
[<AbstractClass; Sealed>]
type Chart =

    static member Line
        (x: seq<float>, y: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit)
        : Figure =
        Chart.line (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x) (Array.ofSeq y)

    static member Scatter
        (x: seq<float>, y: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit)
        : Figure =
        Chart.scatter (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x) (Array.ofSeq y)

    static member Area
        (x: seq<float>, y: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit)
        : Figure =
        Chart.area (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x) (Array.ofSeq y)

    static member Bar
        (x: seq<float>, y: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit)
        : Figure =
        Chart.bar (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x) (Array.ofSeq y)

    /// `err` is in `y`'s unit.
    static member Errorbar
        (
            x: seq<float>,
            y: seq<float>,
            err: seq<float>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Chart.errorbar (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x) (Array.ofSeq y) (Array.ofSeq err)

    static member Step
        (x: seq<float>, y: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit)
        : Figure =
        Chart.step (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x) (Array.ofSeq y)

    static member StepWith
        (
            align: StepAlign,
            x: seq<float>,
            y: seq<float>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Chart.stepWith align (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x) (Array.ofSeq y)

    /// `low` and `high` are in `y`'s unit.
    static member Ribbon
        (
            x: seq<float>,
            low: seq<float>,
            high: seq<float>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Chart.ribbon (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x) (Array.ofSeq low) (Array.ofSeq high)

    /// `width` is in `x`'s unit and `height` in `y`'s.
    static member Tile
        (
            width: float,
            height: float,
            x: seq<float>,
            y: seq<float>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Chart.tile (U.erase xUnit) (U.erase yUnit) width height (Array.ofSeq x) (Array.ofSeq y)

    static member Image
        (
            img: Image,
            x0: float,
            y0: float,
            x1: float,
            y1: float,
            smooth: bool,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Chart.image (U.erase xUnit) (U.erase yUnit) img x0 y0 x1 y1 smooth

    static member Segment
        (
            x0: seq<float>,
            y0: seq<float>,
            x1: seq<float>,
            y1: seq<float>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Chart.segment (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x0) (Array.ofSeq y0) (Array.ofSeq x1) (Array.ofSeq y1)

    static member Text
        (
            x: seq<float>,
            y: seq<float>,
            labels: seq<string>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Chart.text (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x) (Array.ofSeq y) (Array.ofSeq labels)

    /// A horizontal rule at `y`, across whatever panel it is `add`ed to, without moving
    /// that panel's domain. `unit` is checked against the y axis when the figure is compiled.
    static member HLine(y: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Figure =
        Chart.hLine (U.erase unit) y

    /// A vertical rule at `x`. See `HLine`.
    static member VLine(x: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Figure =
        Chart.vLine (U.erase unit) x

    /// A horizontal band between `lo` and `hi`. See `HLine`.
    static member HSpan(lo: float, hi: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Figure =
        Chart.hSpan (U.erase unit) lo hi

    /// A vertical band between `lo` and `hi`. See `HLine`.
    static member VSpan(lo: float, hi: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Figure =
        Chart.vSpan (U.erase unit) lo hi

    /// The line through (`x`, `y`) with `slope`, in y's unit per x's, to both edges of the panel.
    static member LineThrough
        (
            x: float,
            y: float,
            slope: float,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Chart.lineThrough (U.erase xUnit) (U.erase yUnit) x y slope

    /// Text marking the point (`x`, `y`), to `add` over a figure, without moving its
    /// domain. Offset, arrow and placement are `Chart.withTextOffset`, `withArrow`, `withTextAt`.
    static member Annotation
        (
            x: float,
            y: float,
            text: string,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Chart.annotation (U.erase xUnit) (U.erase yUnit) x y text

    /// A least-squares line, to `add` over the data it was fitted to.
    static member Fit
        (x: seq<float>, y: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit)
        : Figure =
        Chart.fit (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x) (Array.ofSeq y)

    static member Loess
        (x: seq<float>, y: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit)
        : Figure =
        Chart.loess (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x) (Array.ofSeq y)

    static member LoessWith
        (
            span: float,
            x: seq<float>,
            y: seq<float>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Chart.loessWith span (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x) (Array.ofSeq y)

    /// Counts per bin. The y axis is a count whatever `unit` is.
    static member Histogram(x: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Figure =
        Chart.histogram (U.erase unit) (Array.ofSeq x)

    static member HistogramWith(bins: int, x: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Figure =
        Chart.histogramWith (U.erase unit) bins (Array.ofSeq x)

    /// A kernel density estimate. The y axis is in the reciprocal of `unit`, derived at run time,
    /// so a density over metres reads `m⁻¹`.
    static member Density(x: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Figure =
        Chart.density (U.erase unit) (Array.ofSeq x)

    static member Ecdf(x: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Figure =
        Chart.ecdf (U.erase unit) (Array.ofSeq x)

    /// One tile per `(x, y)`, coloured by `z` in `valueUnit`.
    static member Heatmap
        (
            x: seq<float>,
            y: seq<float>,
            z: seq<float>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] valueUnit: IUnit
        ) : Figure =
        Chart.heatmap (U.erase xUnit) (U.erase yUnit) (U.erase valueUnit) (Array.ofSeq x) (Array.ofSeq y) (Array.ofSeq z)

    static member Hexbin
        (x: seq<float>, y: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit, [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit)
        : Figure =
        Chart.hexbin (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x) (Array.ofSeq y)

    static member HexbinWith
        (
            across: int,
            x: seq<float>,
            y: seq<float>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Chart.hexbinWith (U.erase xUnit) (U.erase yUnit) across (Array.ofSeq x) (Array.ofSeq y)

    /// The typed series under the axes, for `Chart.ofSeries` and the stats that take one.
    static member Series
        (
            name: string,
            x: seq<float>,
            y: seq<float>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Series<1, 1> =
        Chart.series name (U.erase xUnit) (U.erase yUnit) (Array.ofSeq x) (Array.ofSeq y)

    // ---- Channels that take data -------------------------------------------------------
    //
    // A method that returns the combinator, so that it pipes as the curried form does:
    // `fig |> Chart.ColorBy(temps, unit = U.kelvin)`.

    /// Colour the last layer's marks by `values` in `unit`, on the theme's sequential ramp.
    static member ColorBy(values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Figure -> Figure =
        Chart.colorBy (U.erase unit) (Array.ofSeq values)

    static member ColorByWith
        (ramp: Ramp, values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit)
        : Figure -> Figure =
        Chart.colorByWith ramp (U.erase unit) (Array.ofSeq values)

    /// State the last layer's colour domain. With a unit, the ends are converted into the
    /// channel's; with none, or on an unstated channel, they are the numbers given.
    static member WithColorDomain
        (lo: float, hi: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit)
        : Figure -> Figure =
        Chart.withColorDomain (U.erase unit) lo hi

    static member SizeBy(values: seq<float>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Figure -> Figure =
        Chart.sizeBy (U.erase unit) (Array.ofSeq values)

    static member SizeByWith
        (
            smallest: float<px>,
            largest: float<px>,
            values: seq<float>,
            [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit
        ) : Figure -> Figure =
        Chart.sizeByWith smallest largest (U.erase unit) (Array.ofSeq values)
