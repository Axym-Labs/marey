namespace Marey

open System
open SkiaSharp
open SkiaSharp.HarfBuzz

// ======================================================================================
// THE SKIA BACKEND: PNG, AND PDF.
//
// The third implementation of the `IBackend` contract, and the first whose artifact is **bytes**.
// Two containers share one traversal — `SkiaFormat.Png` and `SkiaFormat.Pdf` — and the whole of the
// difference is in `Begin`, `Finish` and the page unit. "Raster backend" is half wrong: the PDF
// path keeps paths as paths and glyphs as glyphs, and is what a caller wants for a publication
// figure without a TeX installation. It came after SVG and TikZ for a reason worth restating: this
// is a traversal against a contract that already has two independent implementations to be read
// against, so where this file disagrees with `SvgBackend.fs` about where something lands, this file
// is wrong until proved otherwise. The SVG backend is the spec.
//
// ---------------------------------------------------------------------------------------
// COORDINATES: pre-transformed, and the canvas matrix stays the identity.
// ---------------------------------------------------------------------------------------
//
// The two text backends do the same, and the argument is *stronger*
// here rather than weaker. `Stroke.Width`, `Stroke.Dash`, `Stroke.DashOffset` and
// `MarkerBatch.Size` are all in `px` — "a hairline must stay a hairline when the figure is
// rendered at 4K" — and Skia scales the pen with the canvas matrix exactly as SVG scales it
// with a group transform. There is no per-property opt-out for any of the four.
//
// So `Begin` builds one `Affine<nd,px>` that does the nd → px scale *and* the y flip together,
// the traversal threads it down, and every coordinate handed to Skia is already in device
// pixels. That also keeps this backend inside the cross-backend oracle: the SVG backend
// computes the same numbers by the same route, so a sign error here shows up as a
// coordinate-for-coordinate disagreement rather than as a picture nobody looks at.
//
// The **one** place the canvas matrix is used is the raster, for the same reason SVG emits a
// `transform` attribute there and nowhere else: an image is defined by a box rather than by
// points, so its mapping cannot be folded into vertices that do not exist.
//
// ---------------------------------------------------------------------------------------
// WHAT IS DIFFERENT FROM THE TWO TEXT BACKENDS
// ---------------------------------------------------------------------------------------
//
// * **`Semantics = false`.** A PNG has nowhere to put an `aria-label`. That is not a gap to be
//   filled later; it is the property `Capabilities.full`'s comment warns backend authors
//   about, because `Degrade.forCaps` strips every `Annotated` node when it is false. This is
//   the first backend for which that stripping actually happens, and the semantic structure is
//   simply not reviewable through this path — the SVG goldens keep that job.
// * **The output has no reviewable diff.** The goldens are text-diffable, and a bitmap is
//   not. So this backend is covered by structure, determinism and *ink* rather than by
//   baselines, which is the argument `tests/Marey.Tests/Raster.fs` already makes at length for
//   the TikZ compile gate.
// * **Its measurer is not the others'.** `SkiaTextMeasurer` shapes with HarfBuzz, so a figure
//   laid out through `Chart.renderTo` against this backend has *different gutters* from the
//   same figure through the SVG backend. That is a result rather than a regression and it is
//   the milestone's headline — but it is also why the cross-backend oracle below compares
//   scenes under a **stated** measurer rather than comparing whatever each backend
//   happens to lay out against.
// ======================================================================================

/// What a Skia render produces. One traversal, two containers.
///
/// The difference is smaller than it looks and it is entirely in `Begin`, `Finish` and the page
/// unit: a PDF page is measured in `pt` while the traversal is in `px`, so the canvas takes one
/// uniform scale and everything below it is unchanged. See the note on `Begin`.
[<RequireQualifiedAccess>]
type SkiaFormat =
    /// An RGBA raster, encoded as PNG.
    | Png
    /// A single-page vector PDF, at the exact `pt` size of the requested `px` extent.
    | Pdf

/// A PDF's document information dictionary — **fixed, never discovered**, so the same figure
/// writes the same bytes.
///
/// Every field here is a string the caller chose. What is *not* here is the interesting part:
/// there is no creation date and no modification date, and that is the decision rather than an
/// omission.
///
/// Skia writes `/CreationDate` and `/ModDate` from a `DateTime`, formatted with the **renderer's
/// local UTC offset** — and it does so regardless of the value's `DateTimeKind`, which was
/// measured rather than assumed: the same pinned instant renders as `+01'00'` on this machine,
/// `-23'00'` when tagged local and `-22'00'` when tagged unspecified. So a timestamp cannot be
/// pinned to a value that survives crossing a timezone, and a PDF carrying one is not
/// byte-reproducible between two machines however carefully the instant is chosen.
///
/// Both keys are optional in the PDF specification, so the answer is to write neither. A caller
/// who wants provenance has `Title`, `Author`, `Subject` and `Keywords`, all of which are
/// theirs and none of which the clock touches.
///
/// `Producer` is pinned for the same reason at one remove: left unset, Skia writes
/// `Skia/PDF m151`, which puts the rasteriser's build number in the file and moves it on every
/// SkiaSharp bump.
type PdfMetadata =
    { Title: string
      Author: string
      Subject: string
      Keywords: string
      Creator: string
      Producer: string }

[<RequireQualifiedAccess>]
module PdfMetadata =

    /// Empty everywhere the caller has not spoken, and `Marey` where the file has to name
    /// something. An empty string is written as an absent key rather than as `/Title ()`.
    let Default =
        { Title = ""
          Author = ""
          Subject = ""
          Keywords = ""
          Creator = "Marey"
          Producer = "Marey" }

/// Options for a Skia render. One record rather than a widening constructor, matching
/// `TikzOptions`.
[<Struct>]
type SkiaOptions =
    { /// Points to pixels needs a resolution, and it must agree with the measurer layout ran
      /// against or the font size in the bitmap will not be the font size the panel was sized
      /// for. 96 px/inch is the screen convention and `ApproxTextMeasurer`'s default.
      Dpi: float<px / mm>
      /// The colour the surface is cleared to before anything is drawn.
      ///
      /// **Opaque white, not transparent.** A figure is a document, and a PNG with a
      /// transparent background looks correct in every image viewer that composites onto white
      /// and unreadable in every one that composites onto grey — a difference the author never
      /// sees. `Color.transparent` is available for the caller who wants a sprite.
      Background: Color
      /// Antialiasing. On, because the alternative is a chart whose hairlines disappear at
      /// sub-pixel widths, and `Stroke.Width` is in px precisely so that hairlines exist.
      Antialias: bool
      /// A font file to use for **every** run, in place of resolving the theme's family stack
      /// against the machine. That is what makes a render reproducible on another machine.
      ///
      /// `None` resolves the stack and reports what it got on `SkiaBackend.ResolvedFaces`,
      /// which is what a user wants; `Some` is what a golden, a regression test or a paper
      /// figure wants, because it is the only way two machines produce the same pixels.
      ///
      /// **A stated file is obeyed alone and never falls back**. One face is the whole
      /// answer, and a codepoint it lacks is a `.notdef` box — reported, not hidden.
      FontFile: string option
      /// A stated *list* of font files, in priority order. The first
      /// covers what it can, the rest cover what it cannot, and a codepoint none of them has is
      /// a reported `.notdef`.
      ///
      /// This is the reproducible answer to a mixed-script figure. `FontFile` pins one face and
      /// makes a CJK title a row of boxes; the empty default resolves against the machine and
      /// renders that title on a laptop that has a CJK face and not on CI. A list is what makes
      /// the same figure the same pixels on both.
      ///
      /// **Setting this and `FontFile` together is an error**, caught at construction: they are
      /// two answers to one question, and a one-element list is exactly what `FontFile` means.
      FontFiles: string list
      /// The document information dictionary written into a PDF. Ignored by a PNG render.
      Pdf: PdfMetadata }

[<RequireQualifiedAccess>]
module SkiaOptions =

    let Default =
        { Dpi = Space.screenDpi
          Background = Color.white
          Antialias = true
          FontFile = None
          FontFiles = []
          Pdf = PdfMetadata.Default }

/// A Marey colour as Skia's, each channel rounded to a byte. Public for the GPU backend, which
/// clears its surface to the same background.
[<RequireQualifiedAccess>]
module SkiaColor =

    let ofColor (c: Color) : SKColor =
        let ch (v: float) = byte (Math.Round(Math.Max(0.0, Math.Min(1.0, v)) * 255.0))
        SKColor(ch c.R, ch c.G, ch c.B, ch c.A)

/// The clip in force where a `SkiaPainter` offers a node to its claimant, in device pixels.
///
/// A claimant drawing outside Skia has to clip as Skia would. A rectangle it can do itself, with
/// the same antialiased edge; anything else it should leave to Skia.
[<RequireQualifiedAccess>]
type PainterClip =
    /// No `Clip` node encloses the node.
    | Unclipped
    /// The intersection of the enclosing clips is this axis-aligned rectangle, which may be
    /// empty and need not lie on whole pixels.
    | Rectangle of SKRect
    /// An enclosing clip is not an axis-aligned rectangle.
    | Shaped

/// Skia's traversal of a `Node`, on a canvas the caller owns.
///
/// `SkiaBackend` creates a surface or a PDF page and hands its canvas here; the GPU backend hands
/// the canvas of a surface on its own framebuffer. Both draw with the same code, so text,
/// gradients and clips come out the same on either.
///
/// **`claim`** is offered every node before Skia draws it, with the transform it would be drawn
/// under. Returning `true` means the caller has drawn the node itself and Skia skips it, children
/// included. That is how a GPU backend takes `Markers` off Skia without a second traversal. A
/// claimant drawing outside Skia must decline while `LayerDepth` is above zero: inside an
/// `Opacity` layer the canvas is drawing into an offscreen buffer, and a mark drawn straight to
/// the framebuffer would not be faded with the rest of its group.
///
/// Not thread-safe, like the canvas it draws on. The measurer is shared, and must be the one
/// layout ran against: the traversal centres text with the advances it measured.
[<Sealed>]
type SkiaPainter(canvas: SKCanvas, shaping: SkiaTextMeasurer, antialias: bool, claim: (Affine<nd, px> -> Node -> bool) option) =

    let mutable layers = 0
    let mutable clip = PainterClip.Unclipped

    /// The clip after intersecting the current one with a device-space path.
    let intersect (path: SKPath) =
        match clip with
        | PainterClip.Shaped -> PainterClip.Shaped
        | _ when not path.IsRect -> PainterClip.Shaped
        | PainterClip.Unclipped -> PainterClip.Rectangle path.Bounds
        | PainterClip.Rectangle r ->
            let b = path.Bounds
            let x0, y0 = max r.Left b.Left, max r.Top b.Top
            PainterClip.Rectangle(SKRect(x0, y0, max x0 (min r.Right b.Right), max y0 (min r.Bottom b.Bottom)))

    // ---- geometry --------------------------------------------------------------------

    let f32 (v: float) = float32 v

    /// A panel-space point in device pixels under the current transform.
    let point (ctm: Affine<nd, px>) (p: Vec2<nd>) =
        let q = Affine.apply ctm p
        struct (f32 (float q.X), f32 (float q.Y))

    /// A `Node`'s path segments as an `SKPath`, already in device space.
    ///
    /// The caller disposes. `SKPath` is a native handle, and a traversal that leaks one per
    /// path node leaks proportionally to the size of the figure.
    let buildPath (ctm: Affine<nd, px>) (segs: PathSeg[]) (rule: FillRule) =
        // `SKPathBuilder` rather than a mutable `SKPath`: SkiaSharp 4 deprecated every
        // in-place edit on `SKPath`, and this project builds with `TreatWarningsAsErrors`, so
        // the deprecation is a compile error rather than a note to deal with later.
        use b = new SKPathBuilder()

        b.FillType <-
            match rule with
            | NonZero -> SKPathFillType.Winding
            | EvenOdd -> SKPathFillType.EvenOdd

        // A `LineTo` or a `CubicTo` with no preceding `MoveTo` is a malformed path in every
        // target, and the targets disagree about what to do with it: Skia draws from the
        // origin, which puts a stray hairline across the figure from its top-left corner,
        // while an SVG renderer ignores the segment. Tracking whether a subpath is open and
        // starting one costs a bool and draws nothing spurious either way.
        let mutable openSubpath = false

        for s in segs do
            match s with
            | MoveTo p ->
                let struct (x, y) = point ctm p
                b.MoveTo(x, y)
                openSubpath <- true
            | LineTo p ->
                let struct (x, y) = point ctm p

                if openSubpath then
                    b.LineTo(x, y)
                else
                    b.MoveTo(x, y)
                    openSubpath <- true
            | CubicTo(c1, c2, p) ->
                let struct (x1, y1) = point ctm c1
                let struct (x2, y2) = point ctm c2
                let struct (x3, y3) = point ctm p

                if not openSubpath then
                    b.MoveTo(x1, y1)
                    openSubpath <- true

                b.CubicTo(x1, y1, x2, y2, x3, y3)
            | ClosePath ->
                if openSubpath then
                    b.Close()
                    openSubpath <- false

        b.Detach()

    /// An `Affine<'a,'b>` as an `SKMatrix`.
    ///
    /// Skia's constructor takes the nine entries of a 3x3 in row-major order, so the mapping
    /// from Core's six is the same one `Svg.matrix` performs — `M12` and `M21` swap places
    /// relative to the field order, because Core stores the matrix column-major in the sense
    /// SVG's `matrix(a b c d e f)` uses.
    let skMatrix (t: Affine<'a, 'b>) =
        let m = Affine.toRaw t
        SKMatrix(f32 m.[0], f32 m.[1], f32 m.[4], f32 m.[2], f32 m.[3], f32 m.[5], 0.0f, 0.0f, 1.0f)

    // ---- paint -----------------------------------------------------------------------

    let skColor (c: Color) = SkiaColor.ofColor c

    /// A gradient's shader, in device space.
    ///
    /// The stops' *positions* are ratios and need no mapping. The geometry does, and the two
    /// cases need different treatment for the reason `SvgBackend.gradientDef` gives: a radial
    /// gradient carries a single radius, so under an anisotropic nd → px map — the normal case,
    /// since a figure is rarely square — its correct image is an **ellipse**, which a centre
    /// and a scalar radius cannot express. Both are therefore handed to Skia in `nd` with the
    /// current transform as the shader's local matrix, exactly as SVG hands both to
    /// `gradientTransform`. One code path, and the ellipse comes out right.
    let shaderOf (ctm: Affine<nd, px>) (paint: Paint) : SKShader =
        let parts (stops: GradientStop[]) =
            stops |> Array.map (fun s -> skColor s.Color), stops |> Array.map (fun s -> f32 s.Offset)

        let local = skMatrix ctm

        match paint with
        | Solid _ -> null
        | LinearGradient(a, b, stops) ->
            if stops.Length = 0 then
                null
            else
                let colors, offsets = parts stops

                SKShader.CreateLinearGradient(
                    SKPoint(f32 (float a.X), f32 (float a.Y)),
                    SKPoint(f32 (float b.X), f32 (float b.Y)),
                    colors,
                    offsets,
                    SKShaderTileMode.Clamp,
                    local
                )
        | RadialGradient(centre, radius, stops) ->
            if stops.Length = 0 then
                null
            else
                let colors, offsets = parts stops

                SKShader.CreateRadialGradient(
                    SKPoint(f32 (float centre.X), f32 (float centre.Y)),
                    f32 (float radius),
                    colors,
                    offsets,
                    SKShaderTileMode.Clamp,
                    local
                )

    /// A fill paint. The caller disposes.
    let fillPaint (ctm: Affine<nd, px>) (paint: Paint) =
        let p = new SKPaint(IsAntialias = antialias, Style = SKPaintStyle.Fill)

        match paint with
        | Solid c -> p.Color <- skColor c
        | LinearGradient _
        | RadialGradient _ ->
            let sh = shaderOf ctm paint

            if isNull sh then
                // An empty stop list is a gradient with no colours in it. `Degrade` resolves
                // that to transparent for a backend that cannot do gradients; doing the same
                // here keeps the two paths agreeing rather than painting Skia's default black.
                p.Color <- skColor Color.transparent
            else
                p.Shader <- sh

        p

    /// A stroke paint. The caller disposes.
    let strokePaint (ctm: Affine<nd, px>) (s: Stroke) =
        let p =
            new SKPaint(
                IsAntialias = antialias,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = f32 (float s.Width),
                StrokeMiter = f32 s.MiterLimit
            )

        match s.Paint with
        | Solid c -> p.Color <- skColor c
        | _ ->
            let sh = shaderOf ctm s.Paint
            if isNull sh then p.Color <- skColor Color.transparent else p.Shader <- sh

        p.StrokeCap <-
            match s.Cap with
            | Butt -> SKStrokeCap.Butt
            | Round -> SKStrokeCap.Round
            | Square -> SKStrokeCap.Square

        p.StrokeJoin <-
            match s.Join with
            | Miter -> SKStrokeJoin.Miter
            | RoundJoin -> SKStrokeJoin.Round
            | Bevel -> SKStrokeJoin.Bevel

        // Skia's dash effect requires an **even** number of intervals — it reads them as
        // on/off pairs and rejects an odd count outright. SVG's `stroke-dasharray` instead
        // repeats an odd list to make it even, which is the behaviour the IR's `Dash` field
        // inherits from having been written against SVG first. Doubling reproduces it exactly
        // rather than dropping the dash, which would silently draw a solid line.
        if s.Dash.Length > 0 then
            let intervals =
                let raw = s.Dash |> Array.map (fun d -> f32 (float d))
                if raw.Length % 2 = 0 then raw else Array.append raw raw

            // A dash array of all zeros makes no progress and Skia rejects it; SVG treats it
            // as solid. Match SVG.
            if intervals |> Array.exists (fun v -> v > 0.0f) then
                p.PathEffect <- SKPathEffect.CreateDash(intervals, f32 (float s.DashOffset))

        p

    // ---- markers ---------------------------------------------------------------------

    /// One marker's outline, centred on the origin, in **device** space with y already down —
    /// so "triangle up" points up on the screen. `Size` is the bounding extent in px, matching
    /// the field's own documentation.
    ///
    /// This is a transcription of `SvgBackend.markerGeometry`, deliberately: the two must draw
    /// the same shape at the same size, and the plus/cross outlines in particular are a
    /// twelve-vertex polygon rather than two crossed segments so that a batch with a fill and
    /// no stroke still draws something. `Fill` and `Stroke` are independent options on
    /// `MarkerBatch` and both have to work.
    let markerOutline (shape: MarkerShape) (size: float32) =
        let r = size / 2.0f
        let t = r / 3.0f

        let plusPoints =
            [| (-t, -r); (t, -r); (t, -t); (r, -t); (r, t); (t, t); (t, r); (-t, r); (-t, t); (-r, t); (-r, -t); (-t, -t) |]

        match shape with
        | MarkCircle -> Choice1Of2 r
        | MarkSquare -> Choice2Of2 [| (-r, -r); (r, -r); (r, r); (-r, r) |]
        | MarkDiamond -> Choice2Of2 [| (0.0f, -r); (r, 0.0f); (0.0f, r); (-r, 0.0f) |]
        | MarkTriangleUp ->
            let k = r * sqrt 3.0f / 2.0f
            Choice2Of2 [| (0.0f, -r); (k, r / 2.0f); (-k, r / 2.0f) |]
        | MarkTriangleDown ->
            let k = r * sqrt 3.0f / 2.0f
            Choice2Of2 [| (0.0f, r); (-k, -r / 2.0f); (k, -r / 2.0f) |]
        | MarkPlus -> Choice2Of2 plusPoints
        | MarkCross ->
            let s2 = sqrt 0.5f
            Choice2Of2(plusPoints |> Array.map (fun (x, y) -> ((x - y) * s2, (x + y) * s2)))

    // ---- text ------------------------------------------------------------------------

    /// The baseline origin Skia wants, from the anchor and baseline the IR states.
    ///
    /// Skia draws from the alphabetic baseline at the string's left edge and offers no anchor
    /// of its own, so both are resolved here — and they are resolved **the same way
    /// `TextMeasure.bounds` resolves them in Core**, because layout has already placed the run
    /// using that function's answer. A backend that centred text differently from the measurer
    /// that sized the gutter would produce labels that drift out of their own margin.
    ///
    /// The sign convention is the trap. Skia's `Ascent` is **negative** (above the baseline)
    /// while Core's `TextMetrics.Ascent` is positive, so every use of it here converts first.
    ///
    /// **The band comes from the run, not from a font.** A run is split across faces, and a
    /// mixed-script run's band is the maximum over the faces it uses — the fold `ShapedRun`
    /// carries. Reading `Metrics` off the first segment's font here would place a CJK title
    /// against a Latin ascent while layout had reserved the CJK one, which is the
    /// measuring-and-drawing-disagree bug in the vertical direction.
    let textOrigin (ascent: float32) (descent: float32) (t: TextRun) (advance: float32) (x: float32) (y: float32) =
        let ox =
            match t.Anchor with
            | AnchorStart -> x
            | AnchorMiddle -> x - advance / 2.0f
            | AnchorEnd -> x - advance

        let oy =
            match t.Baseline with
            | BaselineAlphabetic -> y
            | BaselineMiddle -> y + (ascent - descent) / 2.0f
            | BaselineHanging -> y + ascent

        ox, oy

    // ---- traversal -------------------------------------------------------------------

    let rec draw (ctm: Affine<nd, px>) (node: Node) =
        match claim with
        | Some c when c ctm node -> ()
        | _ -> drawUnclaimed ctm node

    and drawUnclaimed (ctm: Affine<nd, px>) (node: Node) =
        match node with

        | Path(segs, stroke, fill, rule) ->
            if segs.Length > 0 then
                use path = buildPath ctm segs rule

                // Fill first, then stroke, so an outline sits on top of its own fill. That is
                // the order SVG paints a single element in, and getting it backwards makes a
                // stroked-and-filled shape look half a stroke-width too small.
                match fill with
                | Some p ->
                    use paint = fillPaint ctm p
                    canvas.DrawPath(path, paint)
                | None -> ()

                match stroke with
                | Some s ->
                    use paint = strokePaint ctm s
                    canvas.DrawPath(path, paint)
                | None -> ()

        | Text t -> drawText ctm t

        | Markers b -> drawMarkers ctm b

        | Raster r -> drawRaster ctm r

        // A `Group` is the scene's identity-carrying container and has no rendering effect.
        | Group ns ->
            for child in ns do
                draw ctm child

        // Composed into the current transform rather than pushed onto the canvas. See the note
        // at the top of the file: this is what keeps px-denominated stroke widths honest.
        | Transform(a, child) -> draw (Affine.compose a ctm) child

        | Clip(segs, child) ->
            canvas.Save() |> ignore
            let outer = clip

            if segs.Length = 0 then
                // An empty clip path clips everything away, and saying so explicitly is what
                // keeps the output faithful to the scene rather than quietly reinterpreting
                // it. An empty `SKPath` clips to nothing, which is the same thing.
                use builder = new SKPathBuilder()
                use empty = builder.Detach()
                canvas.ClipPath(empty, SKClipOperation.Intersect, false)
                clip <- PainterClip.Rectangle SKRect.Empty
            else
                use path = buildPath ctm segs NonZero
                canvas.ClipPath(path, SKClipOperation.Intersect, antialias)
                clip <- intersect path

            draw ctm child
            clip <- outer
            canvas.Restore()

        // `SaveLayer` *is* group opacity: the subtree is drawn into an offscreen layer
        // and the layer is composited back with the paint's alpha, so an overlap inside the
        // group does not read darker than the rest of it. Setting `canvas.Alpha` or folding
        // the value into each paint would be the degraded picture, which is what
        // `Degrade.forCaps` produces for a backend that has no layers — and this one has.
        //
        // A layer costs a full-surface allocation, so `1.0` skips it. That is not an
        // optimisation with a caveat: at alpha 1 the composite is the identity, so the two
        // paths produce the same pixels, and an animation that opens at full opacity should
        // not pay for a buffer to multiply by one.
        | Opacity(a, child) ->
            let a = max 0.0 (min 1.0 a)

            if a >= 1.0 then
                draw ctm child
            else
                use paint = new SKPaint(Color = SKColors.White.WithAlpha(byte (Math.Round(a * 255.0))))
                canvas.SaveLayer paint |> ignore
                layers <- layers + 1
                draw ctm child
                layers <- layers - 1
                canvas.Restore()

        // A key's stable identity has nowhere to land in a bitmap and is not an error: the node
        // is a container, so its child is drawn and the name is dropped. Unlike `Annotated`
        // below, this is *not* something `Degrade` removes — a key is not a capability — so
        // the traversal has to handle it.
        | Keyed(_, child) -> draw ctm child

        // `Semantics = false`, so `Degrade.forCaps` strips these before the backend sees them
        // and this branch is defensive rather than dead: `IBackend.Draw` is public and can be
        // driven with an un-degraded scene. Drawing the child is the only honest option — a
        // PNG has nowhere to put a role or a label, and dropping the subtree because it
        // carries metadata would delete the figure.
        | Annotated(_, child) -> draw ctm child

    and drawText (ctm: Affine<nd, px>) (t: TextRun) =
        if not (String.IsNullOrEmpty t.Text) then
            let struct (x, y) = point ctm t.At

            // Not `use`: the fonts and shapers belong to the measurer, which caches them per
            // face and per size and disposes them with this backend. Disposing them here would
            // free a handle the next run is about to use.
            //
            // **One run, one *or more* faces.** `Segments` splits the string by which
            // face has each codepoint and hands back the pieces with the font and shaper that
            // draw each. A Latin run is one segment and this loop is what it always was; a CJK
            // title against a Latin theme is several, and drawing it with the first segment's
            // face would put boxes on the page.
            let run = shaping.Segments t.Font t.Text

            // **Shaped, and shaped with the objects that measured it.** The advance that places
            // an `AnchorMiddle` or `AnchorEnd` run has to be the advance the glyphs actually
            // occupy, or a centred title is off-centre by exactly the kerning. `MeasureText`
            // would give the unshaped sum — 48.01 px against the shaped 44.51 for Arial 12 px
            // "AVATAR" — so a run anchored at its end would sit 3.5 px to the left of where it
            // was drawn.
            //
            // Summed the same way `SkiaTextMeasurer.Measure` sums it, segment by segment, so
            // the total the anchor is resolved against is the total layout sized the gutter
            // with.
            let widths =
                run.Segments
                |> Array.map (fun struct (font, shaper, s) -> (shaper.Shape(s, font)).Width)

            let ox, oy = textOrigin run.Ascent run.Descent t (Array.sum widths) x y

            // ROTATION. The arithmetic is `SvgBackend.drawText`'s, and the long comment there
            // is the argument for it: the frame's rotation and the run's own rotation are
            // taken separately, because the nd → px map is anisotropic whenever the surface is
            // not square and therefore does not preserve angles. Pushing a direction vector
            // through the linear part renders a requested 30° as 23.4° on an 800x600 surface.
            //
            // Skia's `RotateDegrees` is positive clockwise in a y-down space, which is SVG's
            // convention, so the same number works unchanged.
            let frameDegrees = atan2 (float ctm.M21) (float ctm.M11) * 180.0 / Math.PI
            let ownDegrees = float t.Rotation * 180.0 / Math.PI
            let mirrored = float (Affine.determinant ctm) < 0.0

            let degrees =
                frameDegrees + (if mirrored then -ownDegrees else ownDegrees)

            let rotated = abs degrees > 1e-9

            if rotated then
                canvas.Save() |> ignore
                canvas.RotateDegrees(f32 degrees, x, y)

            // Text is filled, never stroked — the same rule as the SVG backend.
            use paint = fillPaint ctm t.Paint

            // `SKTextAlign.Left` because the anchor was already resolved above, against
            // the same rule Core's `TextMeasure.bounds` uses. Handing Skia the anchor
            // instead would put the two in different places the moment they disagreed.
            //
            // The pen walks by each segment's own shaped width, which is why the widths were
            // kept rather than recomputed: they are the numbers the anchor was resolved
            // against, and re-shaping to advance the pen would let the two drift apart.
            let mutable pen = ox

            for i in 0 .. run.Segments.Length - 1 do
                let struct (font, shaper, s) = run.Segments.[i]
                canvas.DrawShapedText(shaper, s, pen, oy, SKTextAlign.Left, font, paint)
                pen <- pen + widths.[i]

            if rotated then
                canvas.Restore()

    and drawMarkers (ctm: Affine<nd, px>) (b: MarkerBatch) =
        if b.Points.Length > 0 then
            // "a 100 000-point scatter must be *one* IR node", and one outline and one
            // pair of paints here, built once for the batch.
            //
            // **But each mark is composited on its own, filled and then stroked.** That is
            // what the SVG backend's `<use>`s and TikZ's per-mark paths do, and what a scatter
            // plot means: two translucent marks that overlap are darker where they overlap,
            // which is how a dense region shows. Until 0.4.0 this backend added the whole batch to
            // one path and filled it once, so overlaps were a union and a cloud of points at 30%
            // read as a flat 30% tint; and it drew every fill before every stroke, so a later
            // mark's fill never covered an earlier mark's outline.
            let size = f32 (float b.Size)

            use fill = b.Fill |> Option.map (fillPaint ctm) |> Option.toObj
            use stroke = b.Stroke |> Option.map (strokePaint ctm) |> Option.toObj

            match markerOutline b.Shape size with
            | Choice1Of2 r ->
                for p in b.Points do
                    let struct (x, y) = point ctm p
                    if not (isNull fill) then canvas.DrawCircle(x, y, r, fill)
                    if not (isNull stroke) then canvas.DrawCircle(x, y, r, stroke)
            | Choice2Of2 outline ->
                use builder = new SKPathBuilder()
                builder.MoveTo(fst outline.[0], snd outline.[0])

                for i in 1 .. outline.Length - 1 do
                    builder.LineTo(fst outline.[i], snd outline.[i])

                builder.Close()
                use path = builder.Detach()

                for p in b.Points do
                    let struct (x, y) = point ctm p
                    canvas.Save() |> ignore
                    canvas.Translate(x, y)
                    if not (isNull fill) then canvas.DrawPath(path, fill)
                    if not (isNull stroke) then canvas.DrawPath(path, stroke)
                    canvas.Restore()

    and drawRaster (ctm: Affine<nd, px>) (r: RasterNode) =
        let img = r.Image

        if img.Width > 0 && img.Height > 0 then
            let expected = img.Width * img.Height * 4
            let src = if isNull img.Rgba then Array.empty else img.Rgba

            // Short buffers are padded with transparent black rather than throwing, matching
            // both other backends: a backend that crashes the whole render because one image
            // was built wrong is worse than one that draws it wrong. `Chart.image` refuses a
            // mis-sized buffer at the constructor, so reaching this is a hand-built scene.
            let pixels =
                if src.Length >= expected then
                    src
                else
                    let padded = Array.zeroCreate<byte> expected
                    Array.blit src 0 padded 0 src.Length
                    padded

            use bitmap = new SKBitmap(SKImageInfo(img.Width, img.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul))

            let handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned)

            try
                bitmap.InstallPixels(bitmap.Info, handle.AddrOfPinnedObject()) |> ignore

                // **The one canvas matrix in this file**, and the same construction the SVG
                // backend hands to its `transform` attribute: an in-box y flip composed with
                // the current transform. `Image` rows run top-first while `nd` is y-up, so
                // without the flip the image lands upside down — and the panel-wide y flip
                // would hide that from anyone testing with a symmetric picture.
                //
                // It is a canvas matrix rather than pre-transformed geometry because an image
                // is defined by a box rather than by vertices, so there is nothing to
                // pre-transform. Nothing inside is stroked, so the pen-scaling argument that
                // rules the matrix out everywhere else does not apply here.
                let flip: Affine<nd, nd> =
                    { M11 = 1.0
                      M12 = 0.0
                      M21 = 0.0
                      M22 = -1.0
                      Dx = 0.0<nd>
                      Dy = r.Bounds.Y0 + r.Bounds.Y1 }

                let m = skMatrix (Affine.compose flip ctm)

                let dest =
                    SKRect(
                        f32 (min (float r.Bounds.X0) (float r.Bounds.X1)),
                        f32 (min (float r.Bounds.Y0) (float r.Bounds.Y1)),
                        f32 (max (float r.Bounds.X0) (float r.Bounds.X1)),
                        f32 (max (float r.Bounds.Y0) (float r.Bounds.Y1))
                    )

                canvas.Save() |> ignore
                canvas.Concat &m

                // "False is right for heatmaps of discrete data, where smoothing invents values
                // that were never measured" — so the flag reaches the sampler rather than being
                // dropped. This is the first backend that can honour it: SVG asks the renderer
                // with `image-rendering` and TikZ cannot ask at all.
                let sampling =
                    if r.Smooth then
                        SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)
                    else
                        SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)

                use paint = new SKPaint(IsAntialias = antialias)
                canvas.DrawBitmap(bitmap, dest, sampling, paint)
                canvas.Restore()
            finally
                handle.Free()

    /// Draw a node under a transform from panel space to device pixels.
    member _.Draw(ctm: Affine<nd, px>, node: Node) = draw ctm node

    /// The canvas being drawn on, for a claimant that needs its clip or its matrix.
    member _.Canvas: SKCanvas = canvas

    /// How many `Opacity` layers the traversal is inside. See the type's comment.
    member _.LayerDepth: int = layers

    /// The clip in force, in device pixels. See `PainterClip`.
    member _.Clip: PainterClip = clip

    /// The map from `Rect.unit` in panel space onto a device box of `extent`, y flipped: what a
    /// scene's root is drawn under. The same construction as the SVG backend's, which is what
    /// lets the cross-backend oracle compare coordinates at all.
    static member RootTransform(extent: Extent<px>) : Affine<nd, px> =
        Affine.fromRects
            Rect.unit
            { X0 = 0.0<px>
              Y0 = extent.Height
              X1 = extent.Width
              Y1 = 0.0<px> }

/// The Skia backend. Renders a `Node` to PNG bytes, or to a one-page PDF.
///
/// The artifact is exposed by the concrete type, through `Result`, because `IBackend.Finish`
/// returns `unit` by design (see `Backend.fs`) — and this backend is the reason that design was
/// right: a `string`-typed protocol could not have carried it.
///
/// **`format` comes second** so that `new SkiaBackend(options)` keeps meaning what it meant.
/// One traversal serves both: the whole difference is in `Begin`, `Finish` and the page unit.
///
/// One instance may be reused: `Begin` disposes the previous document and starts a new one, so
/// the same backend can render a sequence of scenes and two renders of one scene produce
/// byte-identical output.
[<Sealed>]
type SkiaBackend(?options: SkiaOptions, ?format: SkiaFormat) =

    let opts = defaultArg options SkiaOptions.Default
    let format = defaultArg format SkiaFormat.Png

    // **The measurer, and the same one the traversal draws with.** `Shaping` hands back the
    // cached `SKFont` and `SKShaper` behind a `FontSpec`, so `drawText` centres a run against
    // the width layout sized the gutter with, rather than against a second opinion.
    let shaping =
        new SkiaTextMeasurer(?fontFile = opts.FontFile, dpi = opts.Dpi, fontFiles = opts.FontFiles)
    let measurer = shaping :> ITextMeasurer

    let mutable started = false
    let mutable finished = false
    let mutable surface: SKSurface = null
    let mutable canvas: SKCanvas = null
    let mutable cached: byte[] = null

    // PDF only. The document owns the canvas, the stream owns the bytes, and neither exists on
    // the PNG path.
    let mutable document: SKDocument = null
    let mutable pdfStream: SKDynamicMemoryWStream = null

    // The traversal, on whichever canvas `Begin` made.
    let mutable painter: SkiaPainter option = None

    let mutable root: Affine<nd, px> =
        Affine.scaleUniform (LanguagePrimitives.FloatWithMeasure<px / nd> 1.0)

    /// The SkiaSharp shape of the pinned dictionary. An empty string is left unset, so the
    /// key is absent rather than present and empty — and `Creation`/`Modified` are never set at
    /// all, which is the decision.
    let pdfMetadata () =
        // **`SKDocumentPdfMetadata.Default`, not `SKDocumentPdfMetadata()`.** The
        // parameterless struct constructor zeroes two numeric fields that are not zero-valued:
        // `EncodingQuality` becomes 0, which is JPEG at its worst rather than the lossless 101
        // the static default carries, and would quietly destroy any embedded raster; and
        // `RasterDpi` becomes 0.
        let mutable md = SKDocumentPdfMetadata.Default

        // **`RasterDpi` decides the page box's precision, which was found by measuring.** Skia
        // snaps the `MediaBox` to a whole number of device pixels at this resolution, so a
        // 187.5 pt page is written as `188` at the default 72 and as `187.5` at 96 — where
        // 187.5 pt is exactly 250 px. Setting it from the render's own dpi is both what makes
        // the page size exact to the fractional half and what it should have been anyway: it is the
        // resolution Skia rasterises at for anything it cannot express as vectors.
        md.RasterDpi <- float32 (float (Space.pxOfPt opts.Dpi 72.0<pt>))

        let set (v: string) (assign: string -> unit) = if not (String.IsNullOrEmpty v) then assign v
        set opts.Pdf.Title (fun v -> md.Title <- v)
        set opts.Pdf.Author (fun v -> md.Author <- v)
        set opts.Pdf.Subject (fun v -> md.Subject <- v)
        set opts.Pdf.Keywords (fun v -> md.Keywords <- v)
        set opts.Pdf.Creator (fun v -> md.Creator <- v)
        set opts.Pdf.Producer (fun v -> md.Producer <- v)
        md

    /// Everything `Begin` has to let go of before it starts again, and everything `Dispose` has
    /// to let go of once. One function rather than two copies, because the PDF path added a
    /// second handle and a copy is where the second one gets forgotten.
    let release () =
        canvas <- null
        painter <- None

        if not (isNull document) then
            document.Dispose()
            document <- null

        if not (isNull pdfStream) then
            pdfStream.Dispose()
            pdfStream <- null

        if not (isNull surface) then
            surface.Dispose()
            surface <- null

    let requireStarted () =
        if not started then
            invalidOp "Marey.SkiaBackend: Begin must be called before Draw."

        if finished then
            invalidOp "Marey.SkiaBackend: Draw cannot be called after Finish; call Begin again to start a new document."

    // ---- public surface --------------------------------------------------------------

    /// The encoded document — a PNG, or a one-page PDF.
    ///
    /// On the raster path this is available as soon as `Begin` has been called and is stable
    /// afterwards, so a partial render is inspectable — the same contract the two text backends'
    /// `Result` offers.
    ///
    /// **On the PDF path it requires `Finish` first**, and the asymmetry is not a wart worth
    /// hiding: a PDF's cross-reference table is written when the document is closed, so there
    /// are no partial bytes to hand back. Saying so beats returning a truncated file.
    member _.Result: byte[] =
        if not started then
            invalidOp "Marey.SkiaBackend: Begin must be called before Result."

        match format with
        | SkiaFormat.Pdf ->
            if isNull cached then
                invalidOp
                    "Marey.SkiaBackend: Finish must be called before Result on the PDF path -- a PDF has no bytes until its cross-reference table is written."

            cached

        | SkiaFormat.Png ->
            if isNull cached then
                let bytes =
                    SkiaNative.guard (fun () ->
                        use image = surface.Snapshot()
                        use data = image.Encode(SKEncodedImageFormat.Png, 100)
                        data.ToArray())

                if finished then
                    cached <- bytes

                bytes
            else
                cached

    /// Every font this backend has actually opened, in the order the scene first asked for it.
    ///
    /// The reporting half of font resolution, and the answer to "why does this PNG look different
    /// on your machine": a `ResolvedFace` whose `Matched` is `None` fell through the whole stack
    /// onto Skia's default, and one whose `File` is `Some` came from a path the caller stated and
    /// is therefore the same face everywhere.
    ///
    /// **Empty before anything has been measured or drawn**, because resolution is lazy. Read
    /// it after `Finish`.
    member _.ResolvedFaces: ResolvedFace[] = shaping.ResolvedFaces

    /// The measurer this backend lays out against, for a caller who wants to arrange a figure
    /// against the same shaping the render will use without going through `Chart.renderTo`.
    member _.TextMeasurer: ITextMeasurer = measurer

    /// The surface size passed to `Begin`, as an nd → px map. Exposed for tests, which need to
    /// check drawn coordinates against the transform that produced them.
    member _.RootTransform: Affine<nd, px> = root

    interface IBackend with

        /// Every `false` here is a thing this backend genuinely cannot do.
        member _.Caps =
            { Gradients = true
              RadialGradients = true
              Clipping = true
              Dashes = true
              Rasters = true
              // **The first `true` for this flag in the project**, and the first time it means
              // anything: `SkiaTextMeasurer` shapes with HarfBuzz, so kerning pairs and
              // ligatures reach `Figure.arrangement` and the panel rectangle moves.
              //
              // *Nothing branches on it.* `Degrade.forCaps` does not read it, because there is
              // no degradation to perform — a backend that cannot shape still draws the same
              // string, just at slightly different widths, and rewriting the scene could not
              // help. Its readers are the tests and a caller choosing a backend. That was true
              // when the flag was invented and it is still true now that it is finally `true`
              // somewhere, which is worth saying plainly rather than inventing a reader for it.
              TextShaping = true
              RotatedText = true
              // **A PNG has nowhere to put a label.** This is the first backend in the project
              // for which `Semantics = false`, so it is the first time `Degrade.forCaps`
              // actually strips `Annotated` — which `Capabilities.full`'s comment warns about
              // and which no scene had ever had done to it. The cost is real and accepted:
              // the semantic structure cannot be reviewed through this path, and the SVG goldens
              // keep that job.
              //
              // A *tagged* PDF could carry structure, and that is not an argument for flipping
              // this to `true`: `Caps` belongs to the backend and both of its formats go
              // through the same traversal, SkiaSharp exposes no tagging API at 4.151.1, and a
              // capability advertised on one format out of two is a capability that lies half
              // the time. If tagging ever arrives, the capability should move to the entry
              // point.
              Semantics = false
              // `SKCanvas.SaveLayer` composites the subtree and fades the result, which
              // is exactly the node's meaning. Nothing to degrade.
              GroupOpacity = true
              // No instance-buffer ceiling. Skia takes a path of any size, and batching's whole
              // point is that one batch stays one batch. The GPU backend is where `MaxBatch`
              // finally has a real value.
              MaxBatch = None }

        member _.Measurer = measurer

        member _.Begin(extent: Extent<px>) =
            release ()

            started <- true
            finished <- false
            cached <- null

            // Gate 8. This is where `libSkiaSharp` is first touched on the drawing side, and
            // a machine without it should learn which package to add rather than be handed a
            // `DllNotFoundException` naming a file.
            SkiaNative.guard (fun () ->
                match format with
                | SkiaFormat.Png ->
                    // A surface has to be a whole number of pixels, and rounding *up* is the safe
                    // direction: a figure laid out for 400.5 px that rasterises to 400 loses half a
                    // pixel of its right edge, which is where the axis line is.
                    let w = max 1 (int (ceil (float extent.Width)))
                    let h = max 1 (int (ceil (float extent.Height)))

                    surface <- SKSurface.Create(SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul))

                    if isNull surface then
                        invalidOp (sprintf "Marey.SkiaBackend: could not create a %dx%d surface." w h)

                    canvas <- surface.Canvas
                    canvas.Clear(SkiaColor.ofColor opts.Background)

                | SkiaFormat.Pdf ->
                    // **The page is not rounded.** A PDF page is measured in points and points are
                    // not pixels, so 400 x 250 px at the screen convention is a 300 x 187.5 pt page
                    // — exactly, fractional part included. TikZ, whose `bp` is this same unit,
                    // makes the same claim; this is the second, unrelated producer of it.
                    let wPt = Space.ptOfPx opts.Dpi extent.Width
                    let hPt = Space.ptOfPx opts.Dpi extent.Height

                    pdfStream <- new SKDynamicMemoryWStream()
                    document <- SKDocument.CreatePdf(pdfStream, pdfMetadata ())

                    if isNull document then
                        invalidOp "Marey.SkiaBackend: could not create a PDF document."

                    canvas <- document.BeginPage(float32 (float wPt), float32 (float hPt))

                    // **The one canvas transform in the whole backend, and it is a unit
                    // conversion.** Group transforms stay out of the canvas because `Stroke.Width`,
                    // `Dash`, `DashOffset` and `MarkerBatch.Size` are in `px` and an anisotropic
                    // panel scale would stop a hairline being a hairline. This scale is the
                    // opposite case: it is uniform, it is the device resolution, and a 1 px
                    // hairline *should* become 0.75 pt on the page, because that is the same
                    // physical mark. Not scaling it would put a 1 pt rule on a page laid out for a
                    // 1 px one, 33% too heavy — and it is what TikZ already does by converting
                    // every width through `Space.ptOfPx`.
                    //
                    // Everything below this line is therefore the same traversal, in px, as the
                    // raster path runs.
                    let scale = float32 (float (Space.ptOfPx opts.Dpi 1.0<px>))
                    canvas.Scale scale

                    // `Clear` ignores the matrix and fills the clip, so it paints the whole page.
                    canvas.Clear(SkiaColor.ofColor opts.Background))

            // The y flip, byte-for-byte the SVG backend's and built once in
            // `SkiaPainter.RootTransform`: the unit panel onto a device box whose y runs from
            // Height at nd y = 0 to 0 at nd y = 1.
            //
            // **In px for both formats.** The PDF's page is in pt and its canvas carries the
            // conversion, so the traversal — and `RootTransform`, and every coordinate a test
            // checks — stays in one unit whichever container is being written.
            root <- SkiaPainter.RootTransform extent
            painter <- Some(SkiaPainter(canvas, shaping, opts.Antialias, None))

        member _.Draw(node: Node) =
            requireStarted ()
            cached <- null
            painter.Value.Draw(root, node)

        member _.Finish() =
            if not started then
                invalidOp "Marey.SkiaBackend: Begin must be called before Finish."

            finished <- true
            cached <- null

            match format with
            | SkiaFormat.Png -> ()
            | SkiaFormat.Pdf ->
                // Closing is what writes the page tree and the cross-reference table, so the
                // bytes are taken here and cached: the stream is detached by the read and there
                // is no second chance at it.
                SkiaNative.guard (fun () ->
                    document.EndPage()
                    document.Close()
                    use data = pdfStream.DetachAsData()
                    cached <- data.ToArray())

    interface IDisposable with
        member _.Dispose() =
            release ()

            // The measurer owns every `SKTypeface`, `SKFont` and `SKShaper` this render
            // opened, so it is the one thing here that frees more than a surface.
            (shaping :> IDisposable).Dispose()

/// Convenience entry points. The interface is what a heterogeneous list of backends is driven
/// through; these are what a caller with one backend actually wants.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
[<RequireQualifiedAccess>]
module SkiaBackend =

    /// Render a scene to PNG bytes at the given surface size.
    ///
    /// **Degrades against this backend's own capabilities before drawing**, so a scene that
    /// asks for more than the format can carry is drawn as it can be rather than wrongly, with
    /// more to lose than TikZ had: `Chart.render`, `Chart.toFile` and `Chart.toScene`
    /// do not degrade — only `Chart.renderTo` does — so a caller writing
    /// `fig |> Chart.toFileBytes SkiaBackend.renderPng "f.png" size` would otherwise hand an
    /// un-degraded scene to a backend advertising `Semantics = false`. It is idempotent, so
    /// going through `Chart.renderTo` as well costs nothing.
    let renderPng (extent: Extent<px>) (node: Node) : byte[] =
        use backend = new SkiaBackend()
        let sink = backend :> IBackend

        let scene =
            node |> Degrade.forCaps sink.Caps |> Degrade.splitBatches sink.Caps

        sink.Begin extent
        sink.Draw scene
        sink.Finish()
        backend.Result

    /// `renderPng` with explicit options.
    let renderPngWith (options: SkiaOptions) (extent: Extent<px>) (node: Node) : byte[] =
        use backend = new SkiaBackend(options)
        let sink = backend :> IBackend

        let scene =
            node |> Degrade.forCaps sink.Caps |> Degrade.splitBatches sink.Caps

        sink.Begin extent
        sink.Draw scene
        sink.Finish()
        backend.Result

    /// Render a scene and write it to a file as PNG.
    ///
    /// No encoding, deliberately and unlike every other `renderToFile` in the library: a PNG
    /// that acquired a byte-order mark would not open. That asymmetry is deliberate.
    let renderPngToFile (path: string) (extent: Extent<px>) (node: Node) : unit =
        IO.File.WriteAllBytes(path, renderPng extent node)

    /// Render a scene to a one-page PDF at the `pt` size of the given `px` extent.
    ///
    /// 400 x 250 px is a 300 x 187.5 pt page at the screen convention — exact, fractional part
    /// included. That is TikZ's claim too, made by a second producer that shares none of its
    /// code.
    ///
    /// **Vector output from the raster backend**, which is only a contradiction in the name:
    /// what Skia is doing here is replaying the same traversal into a PDF canvas, so the paths
    /// stay paths and the glyphs stay glyphs. It is the answer for a caller who wants a
    /// publication figure without a TeX installation.
    ///
    /// Degrades first, for `renderPng`'s reason.
    let renderPdf (extent: Extent<px>) (node: Node) : byte[] =
        use backend = new SkiaBackend(SkiaOptions.Default, SkiaFormat.Pdf)
        let sink = backend :> IBackend

        let scene =
            node |> Degrade.forCaps sink.Caps |> Degrade.splitBatches sink.Caps

        sink.Begin extent
        sink.Draw scene
        sink.Finish()
        backend.Result

    /// `renderPdf` with explicit options — which is how a caller sets the document title, and
    /// how they state a font file so the PDF is reproducible on another machine.
    let renderPdfWith (options: SkiaOptions) (extent: Extent<px>) (node: Node) : byte[] =
        use backend = new SkiaBackend(options, SkiaFormat.Pdf)
        let sink = backend :> IBackend

        let scene =
            node |> Degrade.forCaps sink.Caps |> Degrade.splitBatches sink.Caps

        sink.Begin extent
        sink.Draw scene
        sink.Finish()
        backend.Result

    /// Render a scene and write it to a file as PDF. No encoding, for `renderPngToFile`'s
    /// reason.
    let renderPdfToFile (path: string) (extent: Extent<px>) (node: Node) : unit =
        IO.File.WriteAllBytes(path, renderPdf extent node)

