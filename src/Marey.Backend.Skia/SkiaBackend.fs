namespace Marey

open System
open SkiaSharp

// ======================================================================================
// §6 / §14 — THE RASTER BACKEND.
//
// The third implementation of the `IBackend` contract, and the first whose artifact is
// **bytes**. §6 puts it after SVG and TikZ for a reason worth restating: this is a traversal
// against a contract that already has two independent implementations to be read against, so
// where this file disagrees with `SvgBackend.fs` about where something lands, this file is
// wrong until proved otherwise. §6 on the SVG backend: "treat it as the spec."
//
// ---------------------------------------------------------------------------------------
// COORDINATES: pre-transformed, and the canvas matrix stays the identity.
// ---------------------------------------------------------------------------------------
//
// D15 and D30 already argued this for the two text backends, and the argument is *stronger*
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
//   the first backend for which that stripping actually happens, and §5.3's structure is
//   simply not reviewable through this path — the SVG goldens keep that job.
// * **The output has no reviewable diff.** §13 wants text-diffable artefacts, and a bitmap is
//   not one. So this backend is covered by structure, determinism and *ink* rather than by
//   baselines, which is the argument `tests/Marey.Tests/Raster.fs` already makes at length for
//   the TikZ compile gate.
// * **Its measurer will not be the others'.** Not yet: this phase still lays out against
//   `ApproxTextMeasurer`, so that the traversal can be checked against the SVG backend
//   coordinate-for-coordinate before shaping is allowed to move anything. The shaper arrives
//   next, behind `TextShaping`, and moving the panel rectangle is then a *result* rather than
//   a regression.
// ======================================================================================

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
      Antialias: bool }

[<RequireQualifiedAccess>]
module SkiaOptions =

    let Default =
        { Dpi = Space.screenDpi
          Background = Color.white
          Antialias = true }

/// The raster backend. Renders a `Node` to PNG bytes.
///
/// The artifact is exposed by the concrete type, through `Result`, because `IBackend.Finish`
/// returns `unit` by design (see `Backend.fs`) — and this backend is the reason that design was
/// right: a `string`-typed protocol could not have carried it.
///
/// One instance may be reused: `Begin` disposes the previous surface and starts a new one, so
/// the same backend can render a sequence of scenes and two renders of one scene produce
/// byte-identical output.
[<Sealed>]
type SkiaBackend(?options: SkiaOptions) =

    let opts = defaultArg options SkiaOptions.Default

    let measurer = ApproxTextMeasurer(opts.Dpi) :> ITextMeasurer

    let mutable started = false
    let mutable finished = false
    let mutable surface: SKSurface = null
    let mutable canvas: SKCanvas = null
    let mutable cached: byte[] = null

    let mutable root: Affine<nd, px> =
        Affine.scaleUniform (LanguagePrimitives.FloatWithMeasure<px / nd> 1.0)

    let requireStarted () =
        if not started then
            invalidOp "Marey.SkiaBackend: Begin must be called before Draw."

        if finished then
            invalidOp "Marey.SkiaBackend: Draw cannot be called after Finish; call Begin again to start a new document."

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

    let skColor (c: Color) =
        let ch (v: float) = byte (Math.Round(Math.Max(0.0, Math.Min(1.0, v)) * 255.0))
        SKColor(ch c.R, ch c.G, ch c.B, ch c.A)

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
        let p = new SKPaint(IsAntialias = opts.Antialias, Style = SKPaintStyle.Fill)

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
                IsAntialias = opts.Antialias,
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
    let textOrigin (font: SKFont) (t: TextRun) (advance: float32) (x: float32) (y: float32) =
        let metrics = font.Metrics
        let ascent = -metrics.Ascent
        let descent = metrics.Descent

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

    /// The typeface for a run.
    ///
    /// `FontSpec.Family` is a **CSS stack** — `Theme.Sans` is "Inter, -apple-system, ..." — and
    /// Skia takes one family name. The stack is therefore walked in order and the first name
    /// that resolves wins, which is what a browser does with the same string and what makes an
    /// SVG and a PNG of one figure pick the same face where both are installed.
    ///
    /// Falling through the whole stack lands on Skia's default, which is a real face rather
    /// than nothing. **Which face was chosen is not reported here**, and that is the open half
    /// of D62: a PNG whose glyph widths depend on the renderer's font cache is not reproducible
    /// across machines. Phase 3 owns it.
    let typefaceFor (family: string) (weight: int) (italic: bool) =
        let skWeight: SKFontStyleWeight = enum weight
        let skSlant = if italic then SKFontStyleSlant.Italic else SKFontStyleSlant.Upright

        let names =
            if String.IsNullOrWhiteSpace family then
                [||]
            else
                family.Split ','
                |> Array.map (fun s -> s.Trim().Trim('\'').Trim('"'))
                |> Array.filter (fun s -> s <> "")

        let rec pick i =
            if i >= names.Length then
                SKTypeface.FromFamilyName(null, skWeight, SKFontStyleWidth.Normal, skSlant)
            else
                let tf = SKTypeface.FromFamilyName(names.[i], skWeight, SKFontStyleWidth.Normal, skSlant)

                // A miss returns Skia's default rather than null, so "did this name resolve"
                // has to be asked by comparing the family back. Without that check the first
                // entry in every stack always "succeeds" and the stack is decorative.
                if isNull tf then
                    pick (i + 1)
                elif String.Equals(tf.FamilyName, names.[i], StringComparison.OrdinalIgnoreCase) then
                    tf
                else
                    tf.Dispose()
                    pick (i + 1)

        pick 0

    // ---- traversal -------------------------------------------------------------------

    let rec draw (ctm: Affine<nd, px>) (node: Node) =
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

        // A `Group` is §5's identity-carrying container and has no rendering effect.
        | Group ns ->
            for child in ns do
                draw ctm child

        // Composed into the current transform rather than pushed onto the canvas. See the note
        // at the top of the file: this is what keeps px-denominated stroke widths honest.
        | Transform(a, child) -> draw (Affine.compose a ctm) child

        | Clip(segs, child) ->
            canvas.Save() |> ignore

            if segs.Length = 0 then
                // An empty clip path clips everything away, and saying so explicitly is what
                // keeps the output faithful to the scene rather than quietly reinterpreting
                // it. An empty `SKPath` clips to nothing, which is the same thing.
                use builder = new SKPathBuilder()
                use empty = builder.Detach()
                canvas.ClipPath(empty, SKClipOperation.Intersect, false)
            else
                use path = buildPath ctm segs NonZero
                canvas.ClipPath(path, SKClipOperation.Intersect, opts.Antialias)

            draw ctm child
            canvas.Restore()

        // §5.2's stable identity has nowhere to land in a bitmap and is not an error: the node
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

            use typeface = typefaceFor t.Font.Family t.Font.Weight t.Font.Italic
            use font = new SKFont(typeface, f32 (float (Space.pxOfPt opts.Dpi t.Font.Size)))

            // Phase 2 measures the same way `ApproxTextMeasurer` does — one advance, no
            // shaping — so that this traversal can be compared to the SVG backend's
            // coordinate-for-coordinate before phase 3 is allowed to move anything.
            let advance = font.MeasureText t.Text
            let ox, oy = textOrigin font t advance x y

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
            canvas.DrawText(t.Text, ox, oy, SKTextAlign.Left, font, paint)

            if rotated then
                canvas.Restore()

    and drawMarkers (ctm: Affine<nd, px>) (b: MarkerBatch) =
        if b.Points.Length > 0 then
            // §5.1 — "a 100 000-point scatter must be *one* IR node". It is also **one canvas
            // call per batch and per paint** here, not one per point: the whole batch is
            // accumulated into a single `SKPath` and drawn once. That is the same decision SVG
            // makes with one `<defs>` shape and N `<use>`s, and TikZ with one `\providecommand`
            // and N calls, arriving at the shape this backend can actually express.
            use builder = new SKPathBuilder()
            let size = f32 (float b.Size)

            match markerOutline b.Shape size with
            | Choice1Of2 r ->
                for p in b.Points do
                    let struct (x, y) = point ctm p
                    builder.AddCircle(x, y, r)
            | Choice2Of2 outline ->
                for p in b.Points do
                    let struct (x, y) = point ctm p
                    builder.MoveTo(x + fst outline.[0], y + snd outline.[0])

                    for i in 1 .. outline.Length - 1 do
                        builder.LineTo(x + fst outline.[i], y + snd outline.[i])

                    builder.Close()

            use path = builder.Detach()

            match b.Fill with
            | Some p ->
                use paint = fillPaint ctm p
                canvas.DrawPath(path, paint)
            | None -> ()

            match b.Stroke with
            | Some s ->
                use paint = strokePaint ctm s
                canvas.DrawPath(path, paint)
            | None -> ()

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

                use paint = new SKPaint(IsAntialias = opts.Antialias)
                canvas.DrawBitmap(bitmap, dest, sampling, paint)
                canvas.Restore()
            finally
                handle.Free()

    // ---- public surface --------------------------------------------------------------

    /// The encoded PNG.
    ///
    /// Available as soon as `Begin` has been called and stable afterwards, so a partial render
    /// is inspectable — the same contract the two text backends' `Result` offers.
    member _.Result: byte[] =
        if not started then
            invalidOp "Marey.SkiaBackend: Begin must be called before Result."

        if isNull cached then
            use image = surface.Snapshot()
            use data = image.Encode(SKEncodedImageFormat.Png, 100)
            let bytes = data.ToArray()

            if finished then
                cached <- bytes

            bytes
        else
            cached

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
              // Phase 3 flips this, and it is the first backend for which the flag will mean
              // anything. Until the shaper is wired in, advertising `true` would be a lie in
              // exactly the place the flag exists to tell the truth.
              TextShaping = false
              RotatedText = true
              // **A PNG has nowhere to put a label.** This is the first backend in the project
              // for which `Semantics = false`, so it is the first time `Degrade.forCaps`
              // actually strips `Annotated` — which `Capabilities.full`'s comment warns about
              // and which no scene had ever had done to it. The cost is real and accepted:
              // §5.3's structure cannot be reviewed through this path, and the SVG goldens
              // keep that job.
              Semantics = false
              // No instance-buffer ceiling. Skia takes a path of any size, and §5.1's whole
              // point is that one batch stays one batch. The GPU backend is where `MaxBatch`
              // finally has a real value.
              MaxBatch = None }

        member _.Measurer = measurer

        member _.Begin(extent: Extent<px>) =
            if not (isNull canvas) then
                canvas <- null

            if not (isNull surface) then
                surface.Dispose()
                surface <- null

            started <- true
            finished <- false
            cached <- null

            // A surface has to be a whole number of pixels, and rounding *up* is the safe
            // direction: a figure laid out for 400.5 px that rasterises to 400 loses half a
            // pixel of its right edge, which is where the axis line is.
            let w = max 1 (int (ceil (float extent.Width)))
            let h = max 1 (int (ceil (float extent.Height)))

            surface <- SKSurface.Create(SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul))

            if isNull surface then
                invalidOp (sprintf "Marey.SkiaBackend: could not create a %dx%d surface." w h)

            canvas <- surface.Canvas
            canvas.Clear(skColor opts.Background)

            // The one place the y flip lives, and it is byte-for-byte the SVG backend's: the
            // unit panel onto a device box whose y runs from Height at nd y = 0 to 0 at nd
            // y = 1. Sharing the construction is what lets the cross-backend oracle compare
            // the two traversals' coordinates at all.
            root <-
                Affine.fromRects
                    Rect.unit
                    { X0 = 0.0<px>
                      Y0 = extent.Height
                      X1 = extent.Width
                      Y1 = 0.0<px> }

        member _.Draw(node: Node) =
            requireStarted ()
            cached <- null
            draw root node

        member _.Finish() =
            if not started then
                invalidOp "Marey.SkiaBackend: Begin must be called before Finish."

            finished <- true
            cached <- null

    interface IDisposable with
        member _.Dispose() =
            canvas <- null

            if not (isNull surface) then
                surface.Dispose()
                surface <- null

/// Convenience entry points. The interface is what a heterogeneous list of backends is driven
/// through; these are what a caller with one backend actually wants.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
[<RequireQualifiedAccess>]
module SkiaBackend =

    /// Render a scene to PNG bytes at the given surface size.
    ///
    /// **Degrades against this backend's own capabilities before drawing**, for D31's reason
    /// and with more to lose than TikZ had: `Chart.render`, `Chart.toFile` and `Chart.toScene`
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
    /// that acquired a byte-order mark would not open. That asymmetry is the whole of D60.
    let renderPngToFile (path: string) (extent: Extent<px>) (node: Node) : unit =
        IO.File.WriteAllBytes(path, renderPng extent node)
