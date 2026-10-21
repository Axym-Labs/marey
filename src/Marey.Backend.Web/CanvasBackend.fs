namespace Marey

open System

// ======================================================================================
// THE CANVAS2D BACKEND
//
// The whole pipeline in a browser: a Fable app builds a `Figure`, compiles it with the same
// compiler the kernel runs, and this draws the `Node` on a canvas. Nothing above `Node` is
// written again, which is the promise and what the goldens under Node already showed
// for SVG.
//
// **The traversal is the Skia painter's, transcribed.** The same rules, for the same reasons,
// because the Skia raster is what a canvas figure will be compared with by eye: geometry is
// pushed through the current transform here rather than handed to the canvas as a matrix, so a
// stroke's width stays in px; a marker batch is one outline drawn at every point, each mark
// filled then stroked; a text run's anchor and baseline are resolved against the
// measurer layout used, and the canvas draws from the left at the alphabetic baseline; and
// an opacity group is drawn on a scratch surface and composited with `globalAlpha`, the canvas's
// `SaveLayer`.
//
// **Gradients.** A fill's gradient is defined in panel space and the path is filled under the
// current transform, which is what Skia's local matrix does, so a radial gradient under an
// anisotropic map comes out the ellipse it should be. A stroke or a text run cannot be drawn
// that way, because the canvas would scale the line width or the glyphs with it, so their
// gradients are mapped into px: exact for a gradient along an axis, and for any other an
// approximation, stated here.
// ======================================================================================

/// A colour as the canvas takes it, quantised to bytes as the Skia backend does.
[<RequireQualifiedAccess>]
module internal CssColor =

    let private channel (v: float) = int (Num.roundEven (max 0.0 (min 1.0 v) * 255.0))

    let ofColor (c: Color) : string =
        sprintf "rgba(%d,%d,%d,%s)" (channel c.R) (channel c.G) (channel c.B) (Num.upTo 4 (float (channel c.A) / 255.0))

/// The canvas painter: a `Node` drawn on a 2-D context under a transform from panel space
/// to the surface's px.
///
/// `layer` makes the scratch surfaces an opacity group and a raster are drawn on, in device
/// pixels; `Canvas.scratch` is the browser's, and a test passes its own. `scale` is the device
/// pixels per px the context's own transform was set to, which a scratch layer has to share.
///
/// `markers`, where given, draws the marker batches it claims on WebGL2.
[<Sealed>]
type CanvasPainter(measurer: ITextMeasurer, layer: int -> int -> CanvasContext, scale: float, markers: WebGlMarkers option) =

    let point (ctm: Affine<nd, px>) (p: Vec2<nd>) =
        let q = Affine.apply ctm p
        float q.X, float q.Y

    /// The path's segments on the context, in px. A segment with no subpath open starts one, as
    /// the Skia painter does, rather than drawing from wherever the pen happens to be.
    let trace (ctx: CanvasContext) (ctm: Affine<nd, px>) (segs: PathSeg[]) =
        ctx.beginPath ()
        let mutable openSubpath = false

        for s in segs do
            match s with
            | MoveTo p ->
                ctx.moveTo (point ctm p)
                openSubpath <- true
            | LineTo p ->
                if openSubpath then ctx.lineTo (point ctm p) else ctx.moveTo (point ctm p)
                openSubpath <- true
            | CubicTo(c1, c2, p) ->
                let x1, y1 = point ctm c1
                let x2, y2 = point ctm c2
                let x3, y3 = point ctm p

                if not openSubpath then
                    ctx.moveTo (x1, y1)
                    openSubpath <- true

                ctx.bezierCurveTo (x1, y1, x2, y2, x3, y3)
            | ClosePath ->
                if openSubpath then
                    ctx.closePath ()
                    openSubpath <- false

    /// The canvas's matrix for an affine map: `transform(a, b, c, d, e, f)` maps (x, y) to
    /// (a·x + c·y + e, b·x + d·y + f).
    let matrix (ctx: CanvasContext) (t: Affine<'a, 'b>) =
        let m = Affine.toRaw t
        ctx.transform (m.[0], m.[2], m.[1], m.[3], m.[4], m.[5])

    let gradient (ctx: CanvasContext) (paint: Paint) (map: Vec2<nd> -> float * float) (radius: float<nd> -> float) : obj =
        let withStops (g: CanvasGradient) (stops: GradientStop[]) =
            for s in stops do
                g.addColorStop (max 0.0 (min 1.0 s.Offset), CssColor.ofColor s.Color)

            box g

        match paint with
        | Solid c -> box (CssColor.ofColor c)
        | LinearGradient(_, _, [||])
        | RadialGradient(_, _, [||]) ->
            // No colours in it: transparent, as `Degrade` and the Skia painter resolve it.
            box (CssColor.ofColor Color.transparent)
        | LinearGradient(a, b, stops) ->
            let x0, y0 = map a
            let x1, y1 = map b
            withStops (ctx.createLinearGradient (x0, y0, x1, y1)) stops
        | RadialGradient(c, r, stops) ->
            let x, y = map c
            withStops (ctx.createRadialGradient (x, y, 0.0, x, y, radius r)) stops

    /// A fill, exact: a gradient in panel space, filled under the current transform.
    let fill (ctx: CanvasContext) (ctm: Affine<nd, px>) (paint: Paint) (rule: FillRule) =
        let rule =
            match rule with
            | NonZero -> "nonzero"
            | EvenOdd -> "evenodd"

        match paint with
        | Solid c ->
            ctx.fillStyle <- box (CssColor.ofColor c)
            ctx.fill rule
        | _ ->
            ctx.save ()
            matrix ctx ctm
            ctx.fillStyle <- gradient ctx paint (fun p -> float p.X, float p.Y) float
            ctx.fill rule
            ctx.restore ()

    /// A gradient mapped into px, for a stroke or a run of text.
    let mapped (ctx: CanvasContext) (ctm: Affine<nd, px>) (paint: Paint) : obj =
        let k = sqrt (abs (float (Affine.determinant ctm)))
        gradient ctx paint (point ctm) (fun r -> float r * k)

    /// The context's stroke state for a stroke, without stroking.
    let strokeState (ctx: CanvasContext) (ctm: Affine<nd, px>) (s: Stroke) =
        ctx.strokeStyle <- mapped ctx ctm s.Paint
        ctx.lineWidth <- float s.Width
        ctx.miterLimit <- s.MiterLimit

        ctx.lineCap <-
            match s.Cap with
            | Butt -> "butt"
            | Round -> "round"
            | Square -> "square"

        ctx.lineJoin <-
            match s.Join with
            | Miter -> "miter"
            | RoundJoin -> "round"
            | Bevel -> "bevel"

        // A canvas repeats an odd dash list as SVG does, and treats all zeros as solid, so the
        // list goes over as it is.
        let dash = s.Dash |> Array.map float
        ctx.setLineDash (if dash |> Array.exists (fun d -> d > 0.0) then dash else [||])
        ctx.lineDashOffset <- float s.DashOffset

    let stroke (ctx: CanvasContext) (ctm: Affine<nd, px>) (s: Stroke) =
        strokeState ctx ctm s
        ctx.stroke ()

    /// One marker's outline about the origin, y down, as `SkiaPainter.markerOutline` has it:
    /// `None` for a circle.
    let outline (shape: MarkerShape) (size: float) : (float * float)[] option =
        let r = size / 2.0
        let t = r / 3.0

        let plus =
            [| (-t, -r); (t, -r); (t, -t); (r, -t); (r, t); (t, t); (t, r); (-t, r); (-t, t); (-r, t); (-r, -t); (-t, -t) |]

        match shape with
        | MarkCircle -> None
        | MarkSquare -> Some [| (-r, -r); (r, -r); (r, r); (-r, r) |]
        | MarkDiamond -> Some [| (0.0, -r); (r, 0.0); (0.0, r); (-r, 0.0) |]
        | MarkTriangleUp ->
            let k = r * sqrt 3.0 / 2.0
            Some [| (0.0, -r); (k, r / 2.0); (-k, r / 2.0) |]
        | MarkTriangleDown ->
            let k = r * sqrt 3.0 / 2.0
            Some [| (0.0, r); (-k, -r / 2.0); (k, -r / 2.0) |]
        | MarkPlus -> Some plus
        | MarkCross ->
            let s2 = sqrt 0.5
            Some(plus |> Array.map (fun (x, y) -> ((x - y) * s2, (x + y) * s2)))

    let rec draw (ctx: CanvasContext) (ctm: Affine<nd, px>) (node: Node) =
        match node with
        | Path(segs, s, f, rule) ->
            if segs.Length > 0 then
                trace ctx ctm segs
                // Fill, then stroke: an outline sits on its own fill.
                f |> Option.iter (fun p -> fill ctx ctm p rule)
                s |> Option.iter (stroke ctx ctm)
        | Text t -> text ctx ctm t
        | Markers b when
            (match markers with
             | Some gpu -> gpu.Claims b && gpu.Draw(ctx, ctm, b, scale)
             | None -> false)
            ->
            ()
        | Markers b -> drawMarkers ctx ctm b
        | Raster r -> raster ctx ctm r
        | Group ns ->
            for child in ns do
                draw ctx ctm child
        | Transform(a, child) -> draw ctx (Affine.compose a ctm) child
        | Clip(segs, child) ->
            ctx.save ()
            // An empty clip path clips everything away, which an empty canvas path does too.
            trace ctx ctm segs
            ctx.clip "nonzero"
            draw ctx ctm child
            ctx.restore ()
        | Opacity(a, child) ->
            let a = max 0.0 (min 1.0 a)

            if a >= 1.0 then
                draw ctx ctm child
            elif a > 0.0 then
                // Group opacity, the canvas's `SaveLayer`: the group on a surface of its own,
                // composited once at the group's alpha, so an overlap inside it does not read
                // darker.
                let surface = ctx.canvas
                let scratch = layer (int surface.width) (int surface.height)
                scratch.setTransform (scale, 0.0, 0.0, scale, 0.0, 0.0)
                draw scratch ctm child
                ctx.save ()
                ctx.globalAlpha <- a
                ctx.drawImage (scratch.canvas, 0.0, 0.0, surface.width / scale, surface.height / scale)
                ctx.restore ()
        | Keyed(_, child) -> draw ctx ctm child
        // `Semantics = false`, so `Degrade.forCaps` strips these; a scene driven in un-degraded
        // still draws its child.
        | Annotated(_, child) -> draw ctx ctm child

    and text (ctx: CanvasContext) (ctm: Affine<nd, px>) (t: TextRun) =
        if not (String.IsNullOrEmpty t.Text) then
            let x, y = point ctm t.At
            let m = measurer.Measure t.Font t.Text
            let advance, ascent, descent = float m.Advance, float m.Ascent, float m.Descent

            // `TextMeasure.bounds`' rule, which placed the run.
            let ox =
                match t.Anchor with
                | AnchorStart -> x
                | AnchorMiddle -> x - advance / 2.0
                | AnchorEnd -> x - advance

            let oy =
                match t.Baseline with
                | BaselineAlphabetic -> y
                | BaselineMiddle -> y + (ascent - descent) / 2.0
                | BaselineHanging -> y + ascent

            // The frame's rotation and the run's own, taken apart as the Skia and SVG backends
            // take them, because an anisotropic map does not preserve angles.
            let frame = atan2 (float ctm.M21) (float ctm.M11)
            let own = float t.Rotation
            let mirrored = float (Affine.determinant ctm) < 0.0
            let radians = frame + (if mirrored then -own else own)
            let rotated = abs radians > 1e-11

            ctx.save ()

            if rotated then
                ctx.translate (x, y)
                ctx.rotate radians
                ctx.translate (-x, -y)

            ctx.font <- CssFont.ofSpec t.Font
            ctx.fillStyle <- mapped ctx ctm t.Paint
            ctx.fillText (t.Text, ox, oy)
            ctx.restore ()

    and drawMarkers (ctx: CanvasContext) (ctm: Affine<nd, px>) (b: MarkerBatch) =
        if b.Points.Length > 0 then
            let shape = outline b.Shape (float b.Size)

            // One batch: the paint state is set once, and only the outline is traced per mark.
            // A gradient fill is the exception, filled under the transform mark by mark.
            let solidFill =
                match b.Fill with
                | Some(Solid c) -> Some(CssColor.ofColor c)
                | _ -> None

            ctx.save ()
            solidFill |> Option.iter (fun c -> ctx.fillStyle <- box c)
            b.Stroke |> Option.iter (strokeState ctx ctm)

            for p in b.Points do
                let x, y = point ctm p
                ctx.beginPath ()

                match shape with
                | None -> ctx.arc (x, y, float b.Size / 2.0, 0.0, 2.0 * Math.PI)
                | Some pts ->
                    let x0, y0 = pts.[0]
                    ctx.moveTo (x + x0, y + y0)

                    for i in 1 .. pts.Length - 1 do
                        let xi, yi = pts.[i]
                        ctx.lineTo (x + xi, y + yi)

                    ctx.closePath ()

                // Each mark composited on its own, filled and then stroked.
                match b.Fill, solidFill with
                | Some _, Some _ -> ctx.fill "nonzero"
                | Some f, None -> fill ctx ctm f NonZero
                | None, _ -> ()

                if b.Stroke.IsSome then
                    ctx.stroke ()

            ctx.restore ()

    and raster (ctx: CanvasContext) (ctm: Affine<nd, px>) (r: RasterNode) =
        let img = r.Image

        if img.Width > 0 && img.Height > 0 then
            let scratch = layer img.Width img.Height
            let data = scratch.createImageData (float img.Width, float img.Height)
            let src = if isNull img.Rgba then [||] else img.Rgba
            let pixels = data.data

            // A short buffer is padded with transparent black, as the other backends pad it.
            for i in 0 .. min src.Length (img.Width * img.Height * 4) - 1 do
                pixels.[i] <- src.[i]

            scratch.putImageData (data, 0.0, 0.0)

            // The image's rows run top first and panel space is y up, so the box is flipped in
            // place before the current transform: the Skia painter's one canvas matrix.
            let flip: Affine<nd, nd> =
                { M11 = 1.0
                  M12 = 0.0
                  M21 = 0.0
                  M22 = -1.0
                  Dx = 0.0<nd>
                  Dy = r.Bounds.Y0 + r.Bounds.Y1 }

            let x0, x1 = min r.Bounds.X0 r.Bounds.X1, max r.Bounds.X0 r.Bounds.X1
            let y0, y1 = min r.Bounds.Y0 r.Bounds.Y1, max r.Bounds.Y0 r.Bounds.Y1
            ctx.save ()
            matrix ctx (Affine.compose flip ctm)
            // "False is right for heatmaps of discrete data": nearest, unless the node asks.
            ctx.imageSmoothingEnabled <- r.Smooth
            ctx.drawImage (scratch.canvas, float x0, float y0, float (x1 - x0), float (y1 - y0))
            ctx.restore ()

    /// Draw a node on a context under a transform from panel space to px.
    member _.Draw(ctx: CanvasContext, ctm: Affine<nd, px>, node: Node) = draw ctx ctm node

/// **The Canvas2D backend.** Draws a scene on a 2-D context the caller owns.
///
/// ```fsharp
/// let ctx = Canvas.context (Canvas.byId "plot")
/// let m = WebText.measurerNow ()
/// let scene = Chart.toSceneWith m.Measurer extent figure
/// CanvasBackend.draw m.Measurer ctx extent scene
/// ```
///
/// `Begin` sizes the canvas to the extent at the display's pixel density, sets the context's
/// transform so that a px is a CSS pixel, and clears it to the background; `Draw` draws; `Finish`
/// does nothing, because the canvas is the artifact. **The measurer must be the one layout ran
/// against**: text is anchored with its advances.
///
/// `Semantics` is false: a canvas has no element per shape to carry a label, so `Annotated` is
/// stripped, and a page that wants one gives the canvas element a single `aria-label`.
///
/// **Marker batches of `WebGlMarkers.MinimumBatch` marks or more are drawn by WebGL2** where the
/// browser has it, composited in their place; `webgl = false` leaves every batch to the
/// canvas.
[<Sealed>]
type CanvasBackend(ctx: CanvasContext, ?measurer: ITextMeasurer, ?background: Color, ?pixelRatio: float, ?layer: int -> int -> CanvasContext, ?webgl: bool) =

    let measurer = defaultArg measurer TextMeasure.approx
    let background = defaultArg background Color.white
    let scale = defaultArg pixelRatio (Canvas.devicePixelRatio ())
    let painter =
        CanvasPainter(measurer, defaultArg layer Canvas.scratch, scale, (if defaultArg webgl true then Some WebGlMarkers.Shared else None))

    let rootFor (extent: Extent<px>) =
        Affine.fromRects
            Rect.unit
            { X0 = 0.0<px>
              Y0 = extent.Height
              X1 = extent.Width
              Y1 = 0.0<px> }

    let mutable root = rootFor { Width = 1.0<px>; Height = 1.0<px> }

    /// What a canvas draws: everything but semantics.
    static member Capabilities: Capabilities =
        { Gradients = true
          RadialGradients = true
          Clipping = true
          Dashes = true
          Rasters = true
          TextShaping = true
          RotatedText = true
          Semantics = false
          GroupOpacity = true
          MaxBatch = None }

    /// Draw a scene on a context, sized to its extent at the given density: `Begin`, `Draw` of
    /// the degraded scene, `Finish`.
    static member Draw(ctx: CanvasContext, extent: Extent<px>, node: Node, ?measurer: ITextMeasurer, ?background: Color, ?pixelRatio: float, ?layer: int -> int -> CanvasContext, ?webgl: bool) : unit =
        let b = CanvasBackend(ctx, ?measurer = measurer, ?background = background, ?pixelRatio = pixelRatio, ?layer = layer, ?webgl = webgl) :> IBackend
        b.Begin extent
        b.Draw(node |> Degrade.forCaps b.Caps)
        b.Finish()

    interface IBackend with
        member _.Caps = CanvasBackend.Capabilities
        member _.Measurer = measurer

        member _.Begin(extent: Extent<px>) =
            let w = ceil (float extent.Width * scale)
            let h = ceil (float extent.Height * scale)
            let canvas = ctx.canvas

            // Setting a canvas's size clears it and resets its state, so it is done only when
            // the size changes.
            if canvas.width <> w then canvas.width <- w
            if canvas.height <> h then canvas.height <- h
            Canvas.setCssSize canvas (float extent.Width) (float extent.Height)

            ctx.setTransform (scale, 0.0, 0.0, scale, 0.0, 0.0)
            ctx.clearRect (0.0, 0.0, float extent.Width, float extent.Height)
            ctx.fillStyle <- box (CssColor.ofColor background)
            ctx.fillRect (0.0, 0.0, float extent.Width, float extent.Height)

            root <- rootFor extent

        member _.Draw(node: Node) = painter.Draw(ctx, root, node)
        member _.Finish() = ()

/// Drawing a scene on a canvas in the browser.
[<RequireQualifiedAccess>]
module CanvasBackend =

    /// Draw a scene on a context at its extent, at the display's density, with the measurer it
    /// was laid out against. `Extent<px> -> Node -> unit` once the first two are given, which is
    /// the renderer-function shape `Chart.render` takes.
    let draw (measurer: ITextMeasurer) (ctx: CanvasContext) (extent: Extent<px>) (node: Node) : unit =
        CanvasBackend.Draw(ctx, extent, node, measurer = measurer)
