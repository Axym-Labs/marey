namespace Marey

open System
open SkiaSharp

/// The GPU renderer: Skia on an OpenGL 3.3 core context, offscreen.
///
/// ```fsharp
/// use gpu = new GpuRenderer()
/// let png = Chart.toSceneWith gpu.Measurer extent figure |> gpu.RenderPng extent
/// let frames = Frames.render FramePass.Concurrent gpu.RenderPng 30.0 film
/// ```
///
/// **One context, one thread, any caller.** The renderer owns a thread, and the GL context, Skia's
/// GPU context and one text measurer live on it for the renderer's lifetime. `RenderPng` and
/// `RenderImage` may be called from any thread, concurrently: each call queues and waits its turn.
/// That is what any renderer function owes its callers, met by serialising rather than by building
/// a backend per call, because building one is what a GPU renderer is for avoiding: a new
/// `SkiaBackend` costs about 10 ms of font resolution before it draws a pixel, and this renderer
/// pays it once. `Frames.render` with `FramePass.Concurrent` still compiles the frames' scenes in
/// parallel; only the drawing takes turns.
///
/// **The measurer is the one to lay out with.** As for `SkiaBackend`, text is drawn with the
/// advances it was measured with, so a figure compiled against `Measurer` puts its labels
/// where its gutters expect them.
///
/// The output is not the CPU raster's to the byte. The GPU antialiases glyph edges a little
/// differently, so the comparison is a stated tolerance rather than equality.
///
/// Construction fails, naming the reason, where no OpenGL 3.3 context can be made: a machine
/// with no display, or a driver too old. Under Linux with no GPU, `xvfb-run` and Mesa's
/// `llvmpipe` provide both.
///
/// **Marker batches are one instanced draw each**, where the batch is one this renderer can
/// draw exactly as Skia would: solid paints, no dash, a mitered or circular outline, outside any
/// opacity layer, under no clip or a rectangular one. Any other batch, and every other node, is
/// Skia's. `instancedMarkers = false` sends every batch to Skia, for comparison.
[<Sealed>]
type GpuRenderer(?options: SkiaOptions, ?instancedMarkers: bool) =

    let opts = defaultArg options SkiaOptions.Default
    let instanced = defaultArg instancedMarkers true

    let thread = new RenderThread("Marey GPU renderer")

    let context, shaping =
        try
            thread.Invoke(fun () ->
                let c =
                    try
                        new GpuContext()
                    with e ->
                        raise (InvalidOperationException(e.Message + " GLFW: " + GlfwDiagnosis.why () + ".", e))

                let m = new SkiaTextMeasurer(?fontFile = opts.FontFile, dpi = opts.Dpi, fontFiles = opts.FontFiles)
                c, m)
        with e ->
            (thread :> IDisposable).Dispose()

            raise (
                InvalidOperationException(
                    "Marey.GpuRenderer: no OpenGL 3.3 core context could be created. On Linux with no GPU or no "
                    + "display, run under xvfb-run with Mesa's llvmpipe installed. The driver said: "
                    + e.Message,
                    e
                )
            )

    let mutable disposed = false

    // The measurer to lay out with is also the way compilation reaches this GPU: a LIC
    // compiled against it is convolved here, per pixel, rather than traced on the CPU.
    let measurer =
        { new ITextMeasurer with
            member _.Measure spec text = (shaping :> ITextMeasurer).Measure spec text
          interface ILicConvolver with
              member _.Convolve job =
                  if disposed then
                      ValueNone
                  else
                      match thread.Invoke(fun () -> context.Compute(fun c -> GpuLic.run c job)) with
                      | Some intensities -> ValueSome intensities
                      | None -> ValueNone }

    let caps = GpuContext.Capabilities

    /// Draw on the render thread and read the pixels back, RGBA8 premultiplied, top row first.
    let draw (extent: Extent<px>) (node: Node) : int * int * byte[] =
        if disposed then
            raise (ObjectDisposedException "GpuRenderer")

        // The scene is degraded here, off the render thread, so that a film's frames do this work
        // in parallel and the queue holds only the drawing.
        let scene = node |> Degrade.forCaps caps |> Degrade.splitBatches caps

        thread.Invoke(fun () ->
            // Rounded up, as `SkiaBackend` does: a figure laid out for 400.5 px that rasterises
            // to 400 loses the half pixel at its right edge, which is where the axis line is.
            let w = max 1 (int (ceil (float extent.Width)))
            let h = max 1 (int (ceil (float extent.Height)))

            let target =
                context.Paint(opts, shaping, instanced, w, h, [ SkiaPainter.RootTransform extent, scene ])

            w, h, target.Read())

    /// Run compute passes on the renderer's context, on its thread, queued with its drawing.
    member internal _.Compute(f: GpuCompute -> 'a) : 'a =
        if disposed then
            raise (ObjectDisposedException "GpuRenderer")

        thread.Invoke(fun () -> context.Compute f)

    /// The capabilities the renderer draws with. `Degrade.forCaps` is applied for the caller.
    member _.Caps: Capabilities = caps

    /// The measurer to lay out with: the one the renderer draws text with. Thread-safe. A
    /// figure compiled with it has its LIC convolved on this GPU, per pixel.
    member _.Measurer: ITextMeasurer = measurer

    /// Every font the renderer has opened, as `SkiaBackend.ResolvedFaces` reports it.
    member _.ResolvedFaces: ResolvedFace[] = shaping.ResolvedFaces

    /// The driver, for a report: what drew the pixels.
    member _.Description: string = context.Description

    /// Render a scene to PNG bytes. Curried, so `gpu.RenderPng` is a renderer function for
    /// `Chart.render` and `Frames.render`.
    member _.RenderPng (extent: Extent<px>) (node: Node) : byte[] =
        let w, h, pixels = draw extent node
        use image = SKImage.FromPixelCopy(SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), pixels)
        use data = image.Encode(SKEncodedImageFormat.Png, 100)
        data.ToArray()

    /// Render a scene to an `Image`: RGBA8, not premultiplied, top row first, as a `Raster` node
    /// carries one.
    member _.RenderImage (extent: Extent<px>) (node: Node) : Image =
        let w, h, pixels = draw extent node

        for i in 0 .. 4 .. pixels.Length - 4 do
            let a = int pixels.[i + 3]

            if a > 0 && a < 255 then
                for c in 0 .. 2 do
                    pixels.[i + c] <- byte (min 255 ((int pixels.[i + c] * 255 + a / 2) / a))

        { Width = w; Height = h; Rgba = pixels }

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true

                thread.Invoke(fun () ->
                    (shaping :> IDisposable).Dispose()
                    (context :> IDisposable).Dispose())

                (thread :> IDisposable).Dispose()

/// Whether this machine can render on the GPU, asked once.
[<RequireQualifiedAccess>]
module GpuBackend =

    /// `Ok` with the driver's description, or `Error` with the reason no context could be made.
    /// Builds a renderer and disposes it, so the answer is the one a real render would get.
    let probe () : Result<string, string> =
        try
            use r = new GpuRenderer()
            Ok r.Description
        with e ->
            Error e.Message
