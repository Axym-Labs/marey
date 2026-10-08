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
    let wide = Chart.size 480.0 300.0
    let fig extent f = extent, Chart.toSceneWith m extent f

    [ for name, node, extent in GoldenScenes.Scenes.all -> name, (extent, node) ]
    @ [ "figure", fig GoldenScenes.figureSurface GoldenScenes.figure
        "figure-category", fig GoldenScenes.figureSurface GoldenScenes.categoryFigure
        "figure-channels", fig GoldenScenes.figureSurface GoldenScenes.channelFigure
        "figure-colorbar", fig GoldenScenes.figureSurface GoldenScenes.colorbarFigure
        "figure-boxplot", fig GoldenScenes.figureSurface GoldenScenes.boxplotFigure
        "figure-heatmap", fig GoldenScenes.figureSurface GoldenScenes.heatmapFigure
        "figure-image", fig GoldenScenes.figureSurface GoldenScenes.imageFigure
        "figure-hexbin", fig wide GoldenScenes.Hexbin.figure
        "figure-field-heatmap", fig wide GoldenScenes.Fields.heatmapFigure
        "figure-field-quiver", fig wide GoldenScenes.Fields.quiverFigure
        "figure-field-streamlines", fig wide GoldenScenes.Fields.streamlinePortrait
        "figure-field-portrait", fig wide GoldenScenes.Fields.phasePortrait ]

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
