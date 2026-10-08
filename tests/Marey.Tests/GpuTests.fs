/// M5 phases 4 and 5 — `Marey.Backend.Gpu`, against the Skia CPU raster it has to agree with.
///
/// One renderer serves the whole run, because a renderer is a thread, a context and a measurer,
/// and building one per test would be the cost the type exists to pay once. Where no OpenGL 3.3
/// context can be made the gates say so and skip, and `Verification.fs` names the driver in the
/// census, so `MAREY_REQUIRE_TOOLCHAIN` turns the skip into a failure (gate 11).
module Marey.Tests.GpuTests

open System
open Xunit
open Marey
open SkiaSharp

let private renderer =
    lazy
        (try
            Ok(new GpuRenderer())
         with e ->
             Error e.Message)

/// The driver a GPU gate would run on, or why there is none. Read by the census.
let driver () : Result<string, string> =
    match renderer.Force() with
    | Ok r -> Ok r.Description
    | Error why -> Error why

/// Run a gate that needs a GL context, or say loudly that it was not run.
let withGpu (body: GpuRenderer -> unit) =
    match renderer.Force() with
    | Ok r -> body r
    | Error why -> Console.WriteLine("SKIPPED: no OpenGL 3.3 context -- " + why)

/// RGBA8 straight from a PNG, top row first.
let private rgba (png: byte[]) =
    use bitmap = SKBitmap.Decode png
    use converted = bitmap.Copy SKColorType.Rgba8888
    converted.Width, converted.Height, converted.Bytes

/// D120's comparison: the largest channel difference between two renders after each is blurred
/// with a 5 × 5 box, on 0–255.
///
/// Not a per-pixel difference, because the GPU and the CPU raster antialias path edges
/// differently: measured on the 27 goldens, unblurred, up to 118 in one channel and on up to
/// 2.6% of a figure's pixels, while text agrees to within 1. That noise lives in one pixel
/// across an edge, and the blur spreads it over 25. A mark that has *moved* changes a crescent
/// along its whole edge, which the blur keeps. Measured on the M5 machine: the worst golden is
/// 23.1, a marker moved by one pixel is 60.8, and by half a pixel 35.3.
let private blurredDistance (a: byte[]) (b: byte[]) : float =
    let blurred (png: byte[]) =
        let w, h, p = rgba png
        // A summed-area table per channel, so the box is four lookups per pixel.
        let sat = Array.zeroCreate<float> ((w + 1) * (h + 1) * 4)

        for y in 0 .. h - 1 do
            for x in 0 .. w - 1 do
                for c in 0 .. 3 do
                    let at (xx: int) (yy: int) = ((yy * (w + 1)) + xx) * 4 + c

                    sat.[at (x + 1) (y + 1)] <-
                        float p.[(y * w + x) * 4 + c] + sat.[at x (y + 1)] + sat.[at (x + 1) y] - sat.[at x y]

        w,
        h,
        fun (x: int) (y: int) (c: int) ->
            let x0, y0 = max 0 (x - 2), max 0 (y - 2)
            let x1, y1 = min w (x + 3), min h (y + 3)
            let at (xx: int) (yy: int) = ((yy * (w + 1)) + xx) * 4 + c
            (sat.[at x1 y1] - sat.[at x0 y1] - sat.[at x1 y0] + sat.[at x0 y0]) / float ((x1 - x0) * (y1 - y0))

    let wa, ha, fa = blurred a
    let wb, hb, fb = blurred b
    Assert.True((wa = wb && ha = hb), sprintf "%dx%d against %dx%d" wa ha wb hb)
    let mutable largest = 0.0

    for y in 0 .. ha - 1 do
        for x in 0 .. wa - 1 do
            for c in 0 .. 3 do
                largest <- max largest (abs (fa x y c - fb x y c))

    largest

/// Gate 6's bound, between the worst golden (23.1) and a marker moved by half a pixel (35.3).
[<Literal>]
let Tolerance = 32.0

/// Every SVG golden's scene, compiled against the given measurer.
let private scenes (m: ITextMeasurer) =
    [ for name, extent, node in GoldenScenes.scenesWith m -> name, (extent, node) ]

// ======================================================================================
// Gate 6 — the GPU agrees with the CPU raster
// ======================================================================================

module ``against the CPU raster`` =

    /// Every SVG golden's scene and figure, compiled once against the renderer's measurer and
    /// drawn by both rasterisers.
    [<Fact>]
    let ``every golden is within the tolerance of the CPU raster`` () =
        withGpu (fun gpu ->
            let measured =
                [ for name, (extent, node) in scenes gpu.Measurer ->
                      name, blurredDistance (SkiaBackend.renderPng extent node) (gpu.RenderPng extent node) ]

            Console.WriteLine("GPU gate 6 on " + gpu.Description)

            for name, d in measured do
                Console.WriteLine(sprintf "  %-26s %5.1f" name d)

            let over = measured |> List.filter (fun (_, d) -> d > Tolerance)
            Assert.True(over.IsEmpty, sprintf "over %.0f: %A" Tolerance over))

    let private marks (shift: float<nd>) =
        Markers
            { Points = [| for i in 0..4 -> { X = 0.15<nd> + 0.17<nd> * float i + (if i = 2 then shift else 0.0<nd>); Y = 0.5<nd> } |]
              Shape = MarkCircle
              Size = 8.0<px>
              Fill = Some(Solid(Color.rgb 0.0 0.45 0.7))
              Stroke = None }

    /// The tolerance is not so loose that a mark can move under it.
    [<Fact>]
    let ``a marker moved by one pixel is outside the tolerance`` () =
        withGpu (fun gpu ->
            let extent: Extent<px> = { Width = 320.0<px>; Height = 200.0<px> }
            let cpu = SkiaBackend.renderPng extent (marks 0.0<nd>)
            let still = blurredDistance cpu (gpu.RenderPng extent (marks 0.0<nd>))
            let moved = blurredDistance cpu (gpu.RenderPng extent (marks (1.0<nd> / 320.0)))
            Assert.True(still <= Tolerance, sprintf "the unmoved markers are %.1f apart" still)
            Assert.True(moved > Tolerance, sprintf "a marker moved by a pixel is only %.1f from where it was" moved))

// ======================================================================================
// Gate 10 — one thread, any caller
// ======================================================================================

module ``one thread`` =

    open Marey.Storyboard

    /// `Frames.render` maps the renderer over the frames on the thread pool. The renderer queues
    /// them onto its own thread, so the frames come out as a serial pass draws them.
    [<Fact>]
    let ``a concurrent pass draws the frames a serial pass draws`` () =
        withGpu (fun gpu ->
            let story =
                scene {
                    let! c = draw GoldenScenes.figure |> over 0.5<s>
                    do! c |> zoomToY (0.0, 40.0) |> over 1.0<s>
                }

            let film = Film.ofStory 24 GoldenScenes.figureSurface story
            let serial = Frames.render FramePass.Serial gpu.RenderPng 12.0 film
            let concurrent = Frames.render FramePass.Concurrent gpu.RenderPng 12.0 film
            Assert.True(serial.Length > 10)
            Assert.Equal<byte[][]>(serial, concurrent))

    /// A renderer used from many threads at once neither deadlocks nor mixes up its answers.
    [<Fact>]
    let ``calls from many threads each get their own scene`` () =
        withGpu (fun gpu ->
            let extent: Extent<px> = { Width = 64.0<px>; Height = 64.0<px> }

            let square (i: int) =
                let c = Color.rgb (float (i % 7) / 6.0) 0.2 0.5
                Path([| MoveTo { X = 0.0<nd>; Y = 0.0<nd> }; LineTo { X = 1.0<nd>; Y = 0.0<nd> }; LineTo { X = 1.0<nd>; Y = 1.0<nd> }; LineTo { X = 0.0<nd>; Y = 1.0<nd> }; ClosePath |], None, Some(Solid c), NonZero)

            let expected = Array.init 7 (fun i -> gpu.RenderImage extent (square i))
            let got = Array.Parallel.init 64 (fun i -> i % 7, gpu.RenderImage extent (square (i % 7)))

            for i, image in got do
                Assert.Equal<byte[]>(expected.[i].Rgba, image.Rgba))

// ======================================================================================
// Phase 5 — marker batches as instanced draws (D119), gates 7, 8 and 9
// ======================================================================================

/// A second renderer that sends every batch to Skia, so a claim can be seen to engage or not.
let private skiaOnly = lazy (new GpuRenderer(instancedMarkers = false))

module ``instanced markers`` =

    let private extent: Extent<px> = { Width = 200.0<px>; Height = 120.0<px> }

    let private batch (stroke: Stroke option) =
        Markers
            { Points = [| for i in 0..8 -> { X = 0.1<nd> + 0.1<nd> * float i; Y = 0.5<nd> + 0.02<nd> * float (i % 3) } |]
              Shape = MarkTriangleUp
              Size = 18.0<px>
              Fill = Some(Solid(Color.rgba 0.1 0.4 0.8 0.6))
              Stroke = stroke }

    let private outline = Stroke.solid (Color.rgb 0.1 0.1 0.1) 1.5<px>

    /// Every shape, filled and outlined and translucent, overlapping its neighbours.
    [<Fact>]
    let ``every shape agrees with the CPU raster, outlined and overlapping`` () =
        withGpu (fun gpu ->
            for shape in [ MarkCircle; MarkSquare; MarkDiamond; MarkTriangleUp; MarkTriangleDown; MarkPlus; MarkCross ] do
                let node =
                    match batch (Some outline) with
                    | Markers b -> Markers { b with Shape = shape }
                    | n -> n

                let d = blurredDistance (SkiaBackend.renderPng extent node) (gpu.RenderPng extent node)
                Assert.True(d <= Tolerance, sprintf "%A is %.1f from the CPU raster" shape d))

    /// The claim engages for a batch it can draw, and the result is not Skia's.
    [<Fact>]
    let ``a plain batch is drawn by the instanced path`` () =
        withGpu (fun gpu ->
            let node = batch (Some outline)
            let bySkia = (skiaOnly.Force()).RenderImage extent node
            Assert.NotEqual<byte[]>(bySkia.Rgba, (gpu.RenderImage extent node).Rgba))

    /// A batch the shader cannot match is left to Skia, so the two renderers agree to the byte.
    [<Fact>]
    let ``a dashed outline and an opacity layer are left to Skia`` () =
        withGpu (fun gpu ->
            let dashed = batch (Some { outline with Dash = [| 3.0<px>; 2.0<px> |] })
            let faded = Opacity(0.5, batch (Some outline))
            let bevelled = batch (Some { outline with Join = Bevel })

            for node in [ dashed; faded; bevelled ] do
                Assert.Equal<byte[]>(((skiaOnly.Force()).RenderImage extent node).Rgba, (gpu.RenderImage extent node).Rgba))

    /// Gate 7. The marks are drawn between Skia's work before them and after them: over a
    /// gridline drawn first, and under an opaque box drawn last.
    [<Fact>]
    let ``a batch lands between what is drawn before and after it`` () =
        withGpu (fun gpu ->
            let rect (x0: float) (x1: float) (y0: float) (y1: float) colour =
                Path(
                    [| MoveTo { X = x0 * 1.0<nd>; Y = y0 * 1.0<nd> }
                       LineTo { X = x1 * 1.0<nd>; Y = y0 * 1.0<nd> }
                       LineTo { X = x1 * 1.0<nd>; Y = y1 * 1.0<nd> }
                       LineTo { X = x0 * 1.0<nd>; Y = y1 * 1.0<nd> }
                       ClosePath |],
                    None,
                    Some(Solid colour),
                    NonZero
                )

            let gridline = rect 0.0 1.0 0.45 0.55 (Color.rgb 0.0 0.0 0.0)

            let marks =
                Markers
                    { Points = [| { X = 0.25<nd>; Y = 0.5<nd> }; { X = 0.75<nd>; Y = 0.5<nd> } |]
                      Shape = MarkSquare
                      Size = 30.0<px>
                      Fill = Some(Solid(Color.rgb 1.0 0.0 0.0))
                      Stroke = None }

            let legend = rect 0.6 0.9 0.3 0.7 (Color.rgb 1.0 1.0 1.0)
            let image = gpu.RenderImage extent (Group [| gridline; marks; legend |])
            let rgb (x: int) (y: int) = let i = (y * image.Width + x) * 4 in int image.Rgba.[i], int image.Rgba.[i + 1], int image.Rgba.[i + 2]
            Assert.Equal((255, 0, 0), rgb 50 60)
            Assert.Equal((255, 255, 255), rgb 150 60))

    /// Gate 8 (D116). Data in seconds since 1970 never reaches the GPU: the panel transform is
    /// applied in `float`, and the buffer holds device pixels.
    [<Fact>]
    let ``a time axis in seconds since 1970 draws as the CPU raster does`` () =
        withGpu (fun gpu ->
            let t0 = 1.7e9
            let xs = Array.init 200 (fun i -> t0 + float i * 0.05)
            let ys = xs |> Array.map (fun t -> sin ((t - t0) * 2.0))
            let wide = Chart.size 480.0 300.0
            let node = Chart.Scatter(xs, ys, xUnit = U.second) |> Chart.toSceneWith gpu.Measurer wide
            let d = blurredDistance (SkiaBackend.renderPng wide node) (gpu.RenderPng wide node)
            Assert.True(d <= Tolerance, sprintf "%.1f from the CPU raster" d))

    /// Gate 9. A million marks in one batch, on hardware. Skipped, and said, on a software
    /// driver, where the number would measure the CPU.
    [<Fact>]
    let ``a million marks render in under fifty milliseconds`` () =
        withGpu (fun gpu ->
            if gpu.Description.Contains "llvmpipe" then
                Console.WriteLine("SKIPPED: gate 9 times a hardware GPU, and this is " + gpu.Description)
            else
                let rng = Random 9
                let wide = Chart.size 800.0 500.0

                let node =
                    Markers
                        { Points = Array.init 1_000_000 (fun _ -> { X = rng.NextDouble() * 1.0<nd>; Y = rng.NextDouble() * 1.0<nd> })
                          Shape = MarkCircle
                          Size = 3.0<px>
                          Fill = Some(Solid(Color.rgba 0.0 0.45 0.7 0.25))
                          Stroke = None }

                gpu.RenderImage wide node |> ignore
                let sw = Diagnostics.Stopwatch.StartNew()

                for _ in 1..5 do
                    gpu.RenderImage wide node |> ignore

                let ms = sw.Elapsed.TotalMilliseconds / 5.0
                Console.WriteLine(sprintf "gate 9: 10^6 marks in %.1f ms on %s" ms gpu.Description)
                Assert.True(ms < 50.0, sprintf "10^6 marks took %.1f ms" ms))
