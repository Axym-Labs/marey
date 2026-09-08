/// The raster backend — M2.5 phase 2.
///
/// ### What this suite can check, and what it deliberately does not
///
/// **No baselines.** §13 wants text-diffable artefacts reviewable in a pull request, and a
/// committed bitmap is neither: it would break on a Skia version bump for reasons unrelated to
/// Marey, and no reviewer can read the diff. That is the argument `Raster.fs` already makes at
/// length for the TikZ compile gate, and it applies with more force here because the output is
/// *only* pixels. So this backend is covered by **structure, determinism, ink and cross-backend
/// agreement**, and the SVG goldens stay the oracle.
///
/// **The sharpest test is gate 1.** The Skia traversal was derived independently from the SVG
/// one, so the two agreeing coordinate-for-coordinate is the thing that catches a sign, a flip
/// or an inverted y in either. This phase leaves Skia laying out against `ApproxTextMeasurer`
/// precisely so that comparison is available *before* shaping is allowed to move anything —
/// when the panel rectangle moves in phase 3, it will be a result rather than a regression.
///
/// ### When the native assets are absent (D66)
///
/// These tests live in `Marey.Tests` rather than in a project of their own, because a second
/// test project is a second place for a gate to rot — and the precedent for "the real
/// enforcement is in CI, and a missing toolchain skips loudly" is set twice over by
/// `TexToolchain.fs` and `Raster.fs`. A machine with no `libSkiaSharp` prints why and returns
/// without asserting. Read the test output rather than the green tick.
module Marey.Tests.SkiaTests

open Xunit
open Marey

// ======================================================================================
// READING THE PIXELS BACK
//
// `Raster.fs` shells out to `pdftoppm` because a PDF needs a rasteriser this project does not
// have. A PNG is already pixels, so this is twenty lines instead of three hundred — which is
// exactly what the plan predicted when it listed the ink gate as "built ... and directly
// reusable, with the rasteriser removed".
//
// **Decoding with SkiaSharp is deliberate and is not circular for the question being asked.**
// What these helpers answer is "what did the *canvas* end up containing", and the canvas is
// Skia's either way. Whether the *encoder* produced a well-formed file is asked separately, of
// the raw bytes, by the signature and IHDR assertions below — which is the half a round trip
// through the same library genuinely could not check.
// ======================================================================================

/// **Public, and reused by `TypesetTests`**, for the reason `withSkia` below is: D81 found five
/// copies of one `PATH` probe that had drifted into three spellings, and a second reader of a PNG
/// would drift the same way — two ideas of what counts as ink, and one of them starts passing for
/// the wrong reason.
module SkiaPixels =

    open SkiaSharp

    /// A decoded image as greyscale bytes, 0 black to 255 white, row-major from the top left —
    /// the same representation and the same orientation `Raster.Gray` uses, so a reader moving
    /// between the two gates is not also switching conventions.
    type Gray =
        { Width: int
          Height: int
          Pixels: byte[] }

    let decode (png: byte[]) : Gray =
        use bitmap = SKBitmap.Decode png

        if isNull bitmap then
            failwith "the raster backend produced bytes no PNG decoder would read"

        let pixels = Array.zeroCreate<byte> (bitmap.Width * bitmap.Height)

        for y in 0 .. bitmap.Height - 1 do
            for x in 0 .. bitmap.Width - 1 do
                let c = bitmap.GetPixel(x, y)
                // Rec. 601 luma. The exact weights do not matter for "is this white", and this
                // is the set every other tool a reader might compare against uses.
                let luma = 0.299 * float c.Red + 0.587 * float c.Green + 0.114 * float c.Blue
                pixels.[y * bitmap.Width + x] <- byte (int (System.Math.Round luma))

        { Width = bitmap.Width
          Height = bitmap.Height
          Pixels = pixels }

    /// Anything not essentially white counts as ink, on `Raster.InkThreshold`'s calibration and
    /// for its reasons.
    let private isInk (v: byte) = int v < Raster.InkThreshold

    let private bounds (g: Gray) (l: float) (t: float) (r: float) (b: float) =
        let clamp lo hi v = max lo (min hi v)
        clamp 0 g.Width (int (l * float g.Width)),
        clamp 0 g.Height (int (t * float g.Height)),
        clamp 0 g.Width (int (r * float g.Width)),
        clamp 0 g.Height (int (b * float g.Height))

    /// The mean grey level of a region given in fractions of the image, y **downward**.
    let meanIn (g: Gray) (l: float) (t: float) (r: float) (b: float) : float =
        let x0, y0, x1, y1 = bounds g l t r b
        let mutable total = 0.0
        let mutable n = 0

        for y in y0 .. y1 - 1 do
            for x in x0 .. x1 - 1 do
                total <- total + float g.Pixels.[y * g.Width + x]
                n <- n + 1

        if n = 0 then 255.0 else total / float n

    /// One pixel's colour, by fraction of the image, y **downward**. Greyscale answers "is
    /// there ink"; compositing questions need the channels apart, because the two ways of
    /// fading a group differ in hue and not only in lightness.
    let rgbAt (png: byte[]) (fx: float) (fy: float) : int * int * int =
        use bitmap = SKBitmap.Decode png

        if isNull bitmap then
            failwith "the raster backend produced bytes no PNG decoder would read"

        let x = max 0 (min (bitmap.Width - 1) (int (fx * float bitmap.Width)))
        let y = max 0 (min (bitmap.Height - 1) (int (fy * float bitmap.Height)))
        let c = bitmap.GetPixel(x, y)
        int c.Red, int c.Green, int c.Blue

    /// The fraction of the whole image carrying ink, in 0..1.
    let coverage (g: Gray) : float =
        let inked = g.Pixels |> Array.sumBy (fun v -> if isInk v then 1 else 0)
        if g.Pixels.Length = 0 then 0.0 else float inked / float g.Pixels.Length

// ======================================================================================
// Fixtures
// ======================================================================================

/// Integral, and the same 8:5 shape the golden suite uses, so an anisotropic nd → px map is the
/// normal case rather than the exception.
let private surface: Extent<px> = { Width = 400.0<px>; Height = 250.0<px> }

let private ink = Color.rgb 0.1 0.1 0.12

let private at (x: float) (y: float) : Vec2<nd> = { X = x * 1.0<nd>; Y = y * 1.0<nd> }

let private ts = Array.init 40 (fun i -> float i * 0.25<s>)
let private xs = ts |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)

let private figure () =
    Chart.line U.second U.metre ts xs
    |> Chart.withTitle "free fall"

/// Whether the native library loaded.
///
/// Probed by doing the smallest real thing rather than by looking for a file: the failure this
/// guards against is a `DllNotFoundException` from three frames down inside a render, and the
/// only reliable way to ask is to render.
let private available =
    lazy
        (try
            SkiaBackend.renderPng { Width = 4.0<px>; Height = 4.0<px> } Scene.empty |> ignore
            Ok()
         with e ->
             Error(e.GetType().Name + ": " + e.Message))

/// Run a gate that needs the native libraries, or say loudly that it was not run.
///
/// **Public, and reused by `FrameTests`.** D81's lesson applied to a probe rather than to a
/// `PATH` walk: the way this goes wrong is that a second file writes its own version, the two
/// disagree about what "available" means, and one of them starts passing for the wrong reason.
let withSkia (body: unit -> unit) =
    match available.Force() with
    | Ok() -> body ()
    | Error why -> System.Console.WriteLine("SKIPPED: SkiaSharp native assets unavailable -- " + why)

// ======================================================================================
// The artifact is a PNG
// ======================================================================================

module ``the output is a png`` =

    /// The eight-byte signature, so this is an image rather than a plausible-looking blob.
    /// Every other assertion in this file would pass on a buffer of zeros.
    [<Fact>]
    let ``the bytes carry a PNG signature and an IEND`` () =
        withSkia (fun () ->
            let png = Chart.render SkiaBackend.renderPng surface (figure ())

            Assert.Equal<byte[]>([| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |], png.[0..7])

            // The chunk that says the stream is complete. A truncated encode would still start
            // with a valid signature.
            let tail = png.[png.Length - 8 ..]
            Assert.Equal<byte[]>([| 0x49uy; 0x45uy; 0x4Euy; 0x44uy |], tail.[0..3]))

    /// The IHDR's width and height, read out of the bytes rather than trusted. A backend that
    /// rendered at the wrong size would still produce a valid PNG.
    [<Fact>]
    let ``the image is exactly the requested extent`` () =
        withSkia (fun () ->
            let png = Chart.render SkiaBackend.renderPng surface (figure ())
            let be32 (o: int) = (int png.[o] <<< 24) ||| (int png.[o + 1] <<< 16) ||| (int png.[o + 2] <<< 8) ||| int png.[o + 3]

            // IHDR's payload starts at byte 16: eight bytes of signature, four of length, four
            // of type tag.
            Assert.Equal(400, be32 16)
            Assert.Equal(250, be32 20))

    /// D60's other half, exercised through the convenience path rather than the backend: a PNG
    /// that acquired a byte-order mark on its way to disk would not open.
    [<Fact>]
    let ``toFileBytes writes a file a decoder would accept`` () =
        withSkia (fun () ->
            let path =
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marey-skia-" + string (System.Guid.NewGuid()) + ".png")

            try
                figure () |> Chart.toFileBytes SkiaBackend.renderPng path surface
                let onDisk = System.IO.File.ReadAllBytes path
                Assert.Equal(0x89uy, onDisk.[0])
                Assert.Equal<byte[]>(Chart.render SkiaBackend.renderPng surface (figure ()), onDisk)
            finally
                if System.IO.File.Exists path then
                    System.IO.File.Delete path)

// ======================================================================================
// GATE 3 — determinism, in bytes
// ======================================================================================

module ``determinism`` =

    /// Two renders of one figure are byte-identical. The whole library's claim, applied to the
    /// one backend whose output nobody can review by eye.
    [<Fact>]
    let ``two renders of one figure are byte-identical`` () =
        withSkia (fun () ->
            let a = Chart.render SkiaBackend.renderPng surface (figure ())
            let b = Chart.render SkiaBackend.renderPng surface (figure ())
            Assert.Equal<byte[]>(a, b))

    /// D34's rule reaching a backend that formats no numbers at all. It still has to hold:
    /// the *layout* formats numbers — tick labels are generated by `Marey.Plot` — and a
    /// comma-decimal locale that changed a label would change the pixels.
    [<Fact>]
    let ``a comma-decimal culture produces byte-identical output`` () =
        withSkia (fun () ->
            let previous = System.Threading.Thread.CurrentThread.CurrentCulture

            try
                System.Threading.Thread.CurrentThread.CurrentCulture <- System.Globalization.CultureInfo "en-US"
                let english = Chart.render SkiaBackend.renderPng surface (figure ())

                System.Threading.Thread.CurrentThread.CurrentCulture <- System.Globalization.CultureInfo "de-DE"
                let german = Chart.render SkiaBackend.renderPng surface (figure ())

                Assert.Equal<byte[]>(english, german)
            finally
                System.Threading.Thread.CurrentThread.CurrentCulture <- previous)

    /// One instance, two scenes. `Begin` has to reset everything, or the second render carries
    /// the first one's ink — which is the failure mode a cached surface produces and which no
    /// single-render test can see.
    [<Fact>]
    let ``a reused backend does not carry ink between renders`` () =
        withSkia (fun () ->
            use backend = new SkiaBackend()
            let sink = backend :> IBackend

            let render (node: Node) =
                sink.Begin surface
                sink.Draw node
                sink.Finish()
                backend.Result

            let withLine =
                render (Scene.polyline (Stroke.solid ink 2.0<px>) [| at 0.1 0.1; at 0.9 0.9 |])

            let blank = render Scene.empty
            let blankAgain = render Scene.empty

            Assert.Equal<byte[]>(blank, blankAgain)
            Assert.NotEqual<byte[]>(withLine, blank))

// ======================================================================================
// GATE 1 — cross-backend agreement
//
// The Skia traversal was derived independently of the SVG one. Where they agree, both are
// probably right; where they disagree, one of them is wrong and the disagreement is visible in
// a number rather than in a picture. This is M1's gate 2 extended to a third backend.
// ======================================================================================

module ``the traversal agrees with the reference backend`` =

    /// The root transform, which is where a y flip would go wrong, stated exactly.
    ///
    /// Skia's device space is y-down like SVG's, so this backend's root is the **same matrix**
    /// as the SVG backend's — unlike TikZ's, which does not flip because TeX is y-up (D29). A
    /// test that only checked "the two backends agree" would be satisfied by both being flipped
    /// wrongly, so the absolute claim is made here as well.
    [<Fact>]
    let ``the root transform matches the SVG backend's, flip included`` () =
        withSkia (fun () ->
            use skia = new SkiaBackend()
            let svg = SvgBackend()
            (skia :> IBackend).Begin surface
            (svg :> IBackend).Begin surface

            let a = skia.RootTransform
            let b = svg.RootTransform

            Assert.Equal(float b.M11, float a.M11, 9)
            Assert.Equal(float b.M22, float a.M22, 9)
            Assert.Equal(float b.Dx, float a.Dx, 9)
            Assert.Equal(float b.Dy, float a.Dy, 9)

            // The absolute half: nd y = 0 is the *bottom* of the figure, so it maps to the
            // largest device y. A backend that forgot the flip would agree with an SVG backend
            // that forgot it too.
            Assert.Equal(250.0, float (Affine.apply a (at 0.0 0.0)).Y, 9)
            Assert.Equal(0.0, float (Affine.apply a (at 0.0 1.0)).Y, 9)
            Assert.True(float (Affine.determinant a) < 0.0, "the root transform should mirror, because nd is y-up and the device is y-down"))

    /// Ink where the SVG backend puts geometry, and no ink where it puts none.
    ///
    /// A coordinate comparison is impossible against a bitmap, so the claim is made
    /// positionally instead: a line drawn from the lower left to the upper right of the panel
    /// must darken the lower-left and upper-right quadrants of the *image* and leave the other
    /// two alone. That is enough to catch a flip in either axis, which is what this gate is for.
    [<Fact>]
    let ``a rising line inks the corners a rising line should`` () =
        withSkia (fun () ->
            let scene =
                Scene.polyline (Stroke.solid ink 3.0<px>) [| at 0.05 0.05; at 0.95 0.95 |]

            let png = SkiaBackend.renderPng surface scene
            let gray = SkiaPixels.decode png

            // In *image* coordinates y grows downward, so a line rising in nd runs from the
            // bottom-left of the picture to the top-right.
            let bottomLeft = SkiaPixels.meanIn gray 0.02 0.60 0.40 0.98
            let topRight = SkiaPixels.meanIn gray 0.60 0.02 0.98 0.40
            let topLeft = SkiaPixels.meanIn gray 0.02 0.02 0.40 0.40
            let bottomRight = SkiaPixels.meanIn gray 0.60 0.60 0.98 0.98

            Assert.True(bottomLeft < 250.0, sprintf "no ink in the bottom-left quadrant (mean %.1f)" bottomLeft)
            Assert.True(topRight < 250.0, sprintf "no ink in the top-right quadrant (mean %.1f)" topRight)
            Assert.True(topLeft > 254.0, sprintf "unexpected ink in the top-left quadrant (mean %.1f)" topLeft)
            Assert.True(bottomRight > 254.0, sprintf "unexpected ink in the bottom-right quadrant (mean %.1f)" bottomRight))

    /// **Gate 1's absolute half, one vertex at a time.**
    ///
    /// The quadrant test above catches a flip. It does not catch a scale that is off by a few
    /// per cent, or a half-pixel offset, or an anisotropy applied to the wrong axis — all of
    /// which leave a rising line rising. A raster has no coordinates to compare, so the
    /// comparison is made the other way round: take the nd vertex, push it through
    /// `RootTransform` — the same matrix the SVG backend builds, asserted equal to it above —
    /// and look at that pixel.
    ///
    /// This is as close to M1's gate 2 as a bitmap allows, and it is an *absolute* claim rather
    /// than a cross-backend one: it fails if Skia and Core disagree, whatever SVG does.
    [<Fact>]
    let ``ink lands on the pixel the root transform names`` () =
        withSkia (fun () ->
            // An asymmetric zig-zag: every vertex is at a different fraction of both axes, so
            // no swap of the two axes and no reflection leaves the set of points unchanged.
            let vertices =
                [| at 0.10 0.20; at 0.35 0.80; at 0.55 0.30; at 0.90 0.65 |]

            let scene = Scene.polyline (Stroke.solid ink 3.0<px>) vertices
            let png = SkiaBackend.renderPng surface scene
            let gray = SkiaPixels.decode png

            use backend = new SkiaBackend()
            (backend :> IBackend).Begin surface
            let root = backend.RootTransform

            /// The darkest pixel within a small window, because a 3 px antialiased stroke does
            /// not put its centre exactly on an integer pixel and demanding that it does would
            /// be testing Skia's rasteriser rather than this traversal.
            let darkestNear (x: int) (y: int) =
                let mutable best = 255

                for dy in -2 .. 2 do
                    for dx in -2 .. 2 do
                        let px = x + dx
                        let py = y + dy

                        if px >= 0 && py >= 0 && px < gray.Width && py < gray.Height then
                            best <- min best (int gray.Pixels.[py * gray.Width + px])

                best

            for v in vertices do
                let p = Affine.apply root v
                let x = int (round (float p.X))
                let y = int (round (float p.Y))

                Assert.True(
                    darkestNear x y < Raster.InkThreshold,
                    sprintf
                        "nd (%.2f, %.2f) maps to device (%d, %d) and there is no ink there -- the traversal and the root transform disagree"
                        (float v.X)
                        (float v.Y)
                        x
                        y
                )

            // The negative half. The zig-zag's vertices are inked; the point reflected through
            // the centre of the surface is not, for any of them — which is what a flipped or
            // transposed traversal would have produced instead.
            for v in vertices do
                let p = Affine.apply root v
                let x = int (round (float surface.Width - float p.X))
                let y = int (round (float surface.Height - float p.Y))

                Assert.True(
                    darkestNear x y >= Raster.InkThreshold,
                    sprintf "there is ink at the point-reflection of nd (%.2f, %.2f), which a correct traversal never draws" (float v.X) (float v.Y)
                ))

// ======================================================================================
// GATE 5 — ink, and every scene in the corpus
// ======================================================================================

module ``ink`` =

    /// Phase 2's exit criterion: every scene in the golden corpus renders without throwing and
    /// puts ink on the page.
    ///
    /// The second half is the one that matters. D33's incident was a backend that emitted a
    /// **valid, correct, entirely blank** document while every structural test passed, and the
    /// only thing that caught it was counting pixels. A raster backend can fail the same way —
    /// a paint that never gets a colour, a clip that clips everything — and it produces a
    /// perfectly good PNG of nothing.
    [<Theory>]
    [<InlineData "paths">]
    [<InlineData "text">]
    [<InlineData "markers-shapes">]
    [<InlineData "markers-batch">]
    [<InlineData "gradients">]
    [<InlineData "dashes">]
    [<InlineData "clipping">]
    [<InlineData "keyed-annotated">]
    [<InlineData "raster">]
    [<InlineData "groups-transforms">]
    [<InlineData "rotated-text">]
    [<InlineData "axis-composition">]
    [<InlineData "all-ten-nodes">]
    [<InlineData "geoms-bands">]
    [<InlineData "geoms-distributions">]
    let ``every golden scene renders with ink in it`` (name: string) =
        withSkia (fun () ->
            let _, scene, extent =
                GoldenTests.Scenes.all |> List.find (fun (n, _, _) -> n = name)

            let png = SkiaBackend.renderPng extent scene
            let gray = SkiaPixels.decode png
            let inked = SkiaPixels.coverage gray

            Assert.True(inked > 0.0, sprintf "'%s' rendered blank" name))

    /// The figure, through the third backend. `figure.svg` and `figure.tex` are the same picture
    /// in two languages; this is the same picture in pixels.
    [<Fact>]
    let ``a complete figure has ink in its panel and in its margins`` () =
        withSkia (fun () ->
            let png =
                Chart.render SkiaBackend.renderPng Golden.figureSurface Golden.figure

            let gray = SkiaPixels.decode png

            // Axis labels and the axis title live down the left-hand margin; series geometry
            // lives inside the axes. Both being inked is what distinguishes a drawn figure from
            // a drawn panel with no furniture.
            Assert.True(SkiaPixels.meanIn gray 0.0 0.0 0.14 0.85 < 254.0, "no ink in the left margin")
            Assert.True(SkiaPixels.meanIn gray 0.25 0.10 0.95 0.75 < 254.0, "no ink in the panel"))

    /// **Gate 5's second half: the right way up.**
    ///
    /// Orientation is the one property no coordinate test can reach. Cross-backend agreement is
    /// satisfied by a flip applied wrongly to *both* sides, and `coverage` cannot see it either
    /// — a flipped image has ink in the same box and the same amount of it, with only the light
    /// and dark halves exchanged. So the fixture's top row is bright and its bottom row dark,
    /// and the halves are compared by mean level rather than by coverage.
    ///
    /// The same claim D64 makes for the TikZ sidecar and phase 4 makes for the PDF page, made
    /// here against the PNG directly. Three containers, one flip, three chances to get it
    /// wrong independently.
    [<Fact>]
    let ``a placed image is the right way up`` () =
        withSkia (fun () ->
            let png =
                Chart.render SkiaBackend.renderPng Golden.figureSurface Golden.imageFigure

            let gray = SkiaPixels.decode png

            // Inside the panel and clear of the axis lines. y runs downward in the image, so
            // "upper" is the fixture's bright row.
            let upper = SkiaPixels.meanIn gray 0.25 0.30 0.85 0.45
            let lower = SkiaPixels.meanIn gray 0.25 0.60 0.85 0.75

            Assert.True(
                upper > lower + 20.0,
                sprintf "the image is upside down or missing: upper mean %.1f, lower mean %.1f" upper lower
            ))

// ======================================================================================
// The capability set, and what it costs
// ======================================================================================

module ``capabilities`` =

    let private caps = (new SkiaBackend() :> IBackend).Caps

    /// **`Semantics = false` is the interesting one**, and it is the first `false` of its kind
    /// in the project. A PNG has nowhere to put an `aria-label`, and `Degrade.forCaps` therefore
    /// strips every `Annotated` node before this backend sees one — which
    /// `Capabilities.full`'s comment warns backend authors about and which no scene had ever
    /// had done to it until now.
    [<Fact>]
    let ``semantics are refused and everything else is offered`` () =
        Assert.False caps.Semantics
        Assert.True caps.Gradients
        Assert.True caps.RadialGradients
        Assert.True caps.Clipping
        Assert.True caps.Dashes
        Assert.True caps.Rasters
        Assert.True caps.RotatedText
        Assert.True caps.MaxBatch.IsNone

    /// **Inverted in phase 3, and it was written to be.** The previous version of this test was
    /// `text shaping is not yet advertised`, asserting `False`, with a comment saying that
    /// flipping the flag is what would make it fail. It failed, on the commit that wired
    /// `SkiaTextMeasurer` in, which is the only kind of notice a capability flag deserves.
    ///
    /// This is the first `true` for `TextShaping` in the project. The flag has three readers
    /// and they are all assertions; nothing branches on it, because there is no degradation to
    /// perform — see the comment on `Caps` in `SkiaBackend.fs`.
    [<Fact>]
    let ``text shaping is advertised, and this backend is the first to advertise it`` () =
        Assert.True caps.TextShaping
        Assert.False (SvgBackend() :> IBackend).Caps.TextShaping
        Assert.False (TikzBackend() :> IBackend).Caps.TextShaping

    /// D31's trap, in the backend that has the most to lose from it. `Chart.render` and
    /// `Chart.toFileBytes` do not degrade — only `Chart.renderTo` does — so `renderPng` has to
    /// do it itself or an `Annotated` node reaches a traversal that advertises it cannot carry
    /// one.
    ///
    /// Here that is survivable rather than fatal, because the traversal draws the child anyway;
    /// the test is that the degradation happens at all, since the same trap with a *gradient*
    /// would not have been survivable.
    [<Fact>]
    let ``the renderer-function path degrades before drawing`` () =
        withSkia (fun () ->
            let annotated =
                Annotated(
                    { Role = RoleSeries
                      Label = "series"
                      Data = [| "n", "1" |] },
                    Scene.polyline (Stroke.solid ink 2.0<px>) [| at 0.1 0.1; at 0.9 0.9 |]
                )

            let bare = Scene.polyline (Stroke.solid ink 2.0<px>) [| at 0.1 0.1; at 0.9 0.9 |]

            // Degrading removed the wrapper, so the two scenes draw the same pixels.
            Assert.Equal<byte[]>(SkiaBackend.renderPng surface annotated, SkiaBackend.renderPng surface bare))


// ======================================================================================
// GATE 8 — A MISSING NATIVE ASSET FAILS LOUDLY
//
// **What can and cannot be tested here, stated rather than implied.** A process that has
// already loaded `libSkiaSharp` cannot un-load it, so no test in this suite can make the real
// failure happen. What *can* be tested is the whole of the machinery that turns it into a
// sentence: which exception shapes are recognised, which are deliberately not, and what the
// message says. `guard` takes a thunk precisely so that half is reachable — the placement of
// the calls is verified by reading, and §5 of the M2.5 plan says so.
//
// The negative case is the one that keeps this honest. A gate that translated *every* exception
// into "install SkiaSharp.NativeAssets" would pass every positive test here and mislead every
// reader of a real stack trace.
// ======================================================================================

module ``the native asset gate`` =

    open System

    /// The shape a missing native library actually arrives in.
    let private missing () =
        DllNotFoundException "Unable to load shared library 'libSkiaSharp' or one of its dependencies."

    /// And the shape it arrives in when the first touch is inside a static initialiser, which
    /// is the *common* case: the real cause is two levels down.
    let private missingNested () =
        TypeInitializationException("SkiaSharp.SKSurface", missing ()) :> exn

    [<Fact>]
    let ``a missing native library is diagnosed, however deeply it is wrapped`` () =
        for e in [ missing () :> exn; missingNested (); InvalidOperationException("render failed", missingNested ()) :> exn ] do
            match SkiaNative.tryDiagnose e with
            | None -> failwithf "a %s wrapping a DllNotFoundException was not recognised" (e.GetType().Name)
            | Some message ->
                // The message has to name a package, not a file. `libSkiaSharp` is not
                // something anyone can install; `SkiaSharp.NativeAssets.Linux` is.
                Assert.Contains("SkiaSharp.NativeAssets", message)
                Assert.Contains("HarfBuzzSharp.NativeAssets", message)

                // Two native libraries on two version lines, so naming only the first would
                // send a reader to add a package and hit the same wall one frame later.
                Assert.Contains("PackageReference", message)

                // And it keeps the original text, because the package name is the fix and the
                // loader's own message is the evidence.
                Assert.Contains("libSkiaSharp", message)

    [<Fact>]
    let ``the package named is the one for this platform`` () =
        match SkiaNative.tryDiagnose (missing ()) with
        | None -> failwith "the diagnosis did not fire"
        | Some message ->
            let expected =
                if Runtime.InteropServices.RuntimeInformation.IsOSPlatform Runtime.InteropServices.OSPlatform.Linux then
                    "SkiaSharp.NativeAssets.Linux"
                elif Runtime.InteropServices.RuntimeInformation.IsOSPlatform Runtime.InteropServices.OSPlatform.OSX then
                    "SkiaSharp.NativeAssets.macOS"
                else
                    "SkiaSharp.NativeAssets.Win32"

            Assert.Contains(expected, message)

    /// **The half that stops the gate being a liar.**
    [<Fact>]
    let ``an ordinary bug is not dressed up as a missing dependency`` () =
        for e in
            [ InvalidOperationException "Begin must be called before Draw" :> exn
              ArgumentException "bad extent" :> exn
              NullReferenceException() :> exn
              Exception("outer", InvalidOperationException "inner") ] do
            Assert.True(
                (SkiaNative.tryDiagnose e).IsNone,
                sprintf "a %s was misreported as a missing native library" (e.GetType().Name)
            )

    [<Fact>]
    let ``guard translates a native failure and leaves everything else alone`` () =
        let translated =
            Assert.Throws<PlatformNotSupportedException>(fun () -> SkiaNative.guard (fun () -> raise (missingNested ())) |> ignore)

        Assert.Contains("SkiaSharp.NativeAssets", translated.Message)

        // The original is kept as the inner exception, so a stack trace still leads to the
        // frame that failed rather than to this translation.
        Assert.NotNull translated.InnerException

        // Anything else passes through with its own type, which is what makes the rest of the
        // backend's `invalidOp` messages still mean what they say.
        Assert.Throws<InvalidOperationException>(fun () -> SkiaNative.guard (fun () -> invalidOp "not a native problem") |> ignore)
        |> ignore

        // And the success path returns the value rather than swallowing it.
        Assert.Equal(7, SkiaNative.guard (fun () -> 7))

    /// The guard is on the entry points, so the entry points still work when the library *is*
    /// present. Trivial, and it is the thing that would break if `guard` were ever written to
    /// catch and return a default instead of rethrowing.
    [<Fact>]
    let ``the guarded entry points still render`` () =
        withSkia (fun () ->
            Assert.NotEmpty(Chart.render SkiaBackend.renderPng surface (figure ()))
            Assert.NotEmpty(Chart.render SkiaBackend.renderPdf surface (figure ())))

// ======================================================================================
// GATE 6 — the zero-dependency rule
//
// §13: "Marey.Core having zero dependencies is non-negotiable: it is what lets the same IR run
// in a notebook, in a browser via Fable, and in an AOT-compiled native binary."
//
// This is the gate most likely to fail by accident, from a test project's reference leaking
// through a ProjectReference chain — which is exactly the shape of thing this file just added.
// ======================================================================================

module ``the zero-dependency rule`` =

    /// Load by name rather than search `AppDomain.CurrentDomain.GetAssemblies()`.
    ///
    /// The search was the first spelling and it is **order-dependent**: .NET loads assemblies
    /// lazily, so whether `Marey.Backend.Tikz` is in that list depends on whether some earlier
    /// test happened to touch it. Under `--filter` it was not there at all. A gate that reports
    /// "not loaded" when run alone and passes when run with the suite is not a gate, and the
    /// version of this that quietly skipped the missing ones would have been worse still —
    /// §13's rule would have been checked for whichever packages the runner felt like.
    ///
    /// `Assembly.Load` resolves through the deps file, which is the same path a consumer's
    /// application takes, so it answers the question actually being asked.
    let private load (name: string) =
        System.Reflection.Assembly.Load(System.Reflection.AssemblyName name)

    /// The five packages that must never see SkiaSharp, asserted against the **built assembly
    /// graph** rather than against the project files. A `.fsproj` says what was asked for; the
    /// assembly's reference list says what was actually linked, which is what a consumer gets.
    [<Theory>]
    [<InlineData "Marey.Core">]
    [<InlineData "Marey.Units">]
    [<InlineData "Marey.Plot">]
    [<InlineData "Marey.Backend.Svg">]
    [<InlineData "Marey.Backend.Tikz">]
    // Not a vector backend, but it makes the same zero-dependency claim in its .fsproj and the
    // packages that will consume it (D75, D79) each drag a toolchain already.
    [<InlineData "Marey.Toolchain">]
    // Nor is this one, and its claim is stronger still: `Marey.Anim` references `Marey.Core`
    // and nothing else, which is what keeps a scrubbable timeline compilable under Fable.
    [<InlineData "Marey.Anim">]
    let ``no vector package references SkiaSharp`` (name: string) =
        let a = load name

        let offending =
            a.GetReferencedAssemblies()
            |> Array.map (fun r -> r.Name)
            |> Array.filter (fun n ->
                not (isNull n)
                && (n.StartsWith("SkiaSharp", System.StringComparison.OrdinalIgnoreCase)
                    || n.StartsWith("HarfBuzz", System.StringComparison.OrdinalIgnoreCase)))

        Assert.True(
            offending.Length = 0,
            sprintf "%s references %s -- §13 makes the zero-dependency rule non-negotiable" name (String.concat ", " offending)
        )

    /// The gate has to be able to fail, and the way this one could go vacuous is by finding
    /// nothing to look at. So the backend that *does* depend on SkiaSharp is asserted to depend
    /// on it, by the same instrument: if this passes and the five above pass, the instrument
    /// works and the five are a real result rather than five empty reference lists.
    [<Fact>]
    let ``the Skia backend does reference SkiaSharp`` () =
        let referenced =
            (load "Marey.Backend.Skia").GetReferencedAssemblies()
            |> Array.map (fun r -> r.Name)

        Assert.Contains("SkiaSharp", referenced)

    /// And Core references nothing at all beyond the language runtime, which is the stronger
    /// claim its own project file makes: "NOTHING may be added to PackageReference in this file.
    /// Ever. FSharp.Core is the language runtime, not a dependency."
    [<Fact>]
    let ``Marey Core references only the runtime`` () =
        let core = load "Marey.Core"

        let unexpected =
            core.GetReferencedAssemblies()
            |> Array.map (fun r -> r.Name)
            |> Array.filter (fun n ->
                not (isNull n)
                && n <> "FSharp.Core"
                && not (n.StartsWith("System", System.StringComparison.Ordinal))
                && n <> "netstandard"
                && n <> "mscorlib")

        Assert.True(unexpected.Length = 0, "Marey.Core references " + String.concat ", " unexpected)


// ======================================================================================
// PHASE 4 — PDF
//
// GATE 3 (the PDF half): two renders of one figure are byte-identical. It is the half the plan
// said would fail without D63, and it would have: Skia writes `/CreationDate` and `/ModDate`
// from a `DateTime` formatted with the **renderer's local UTC offset**, and does so regardless
// of the value's `DateTimeKind` — the same pinned instant renders `+01'00'`, `-23'00'` or
// `-22'00'` depending only on how it was tagged and where the machine is. So there is no
// timestamp that survives crossing a timezone, and both keys are omitted.
//
// GATE 4: the page is exactly the requested extent. 400 x 250 px is 300 x 187.5 pt — including
// the fractional part, which is the half of the claim a rounding bug would eat. M1 proved this
// for TikZ, whose `bp` is this same unit; Skia is a second producer that shares none of its code.
// ======================================================================================

module ``pdf`` =

    /// Latin-1 rather than UTF-8: a PDF is a byte format with binary streams in it, and decoding
    /// those as UTF-8 either throws or silently replaces bytes, which would make a search for a
    /// key report the wrong answer rather than fail.
    let private text (pdf: byte[]) =
        System.Text.Encoding.Latin1.GetString pdf

    let private fontFile =
        lazy (System.IO.Path.Combine(Golden.projectDir.Force(), "fonts", "LiberationSans-Regular.ttf"))

    /// A stated face, so the embedded font subset is the same on every machine. Without it the
    /// bytes are still deterministic *here*, which is what gate 3 asks, but the file would carry
    /// whatever face the machine resolved and a reader comparing two checkouts would be misled.
    let private options =
        { SkiaOptions.Default with
            FontFile = Some(fontFile.Force())
            Pdf =
                { PdfMetadata.Default with
                    Title = "free fall" } }

    [<Fact>]
    let ``the artifact is a pdf`` () =
        withSkia (fun () ->
            let pdf = Chart.render (SkiaBackend.renderPdfWith options) surface (figure ())
            let s = text pdf

            Assert.StartsWith("%PDF-", s)
            Assert.Contains("%%EOF", s)
            Assert.Contains("/MediaBox", s)

            // One page, because a figure is one page. A backend that opened a page per `Draw`
            // would still produce a valid PDF and a reader would find the figure on page 1 — so
            // the page tree's own count is the thing to read.
            Assert.Contains("/Count 1", s))

    /// **Gate 3, the half that needed D63.**
    [<Fact>]
    let ``two renders of one figure are byte-identical`` () =
        withSkia (fun () ->
            let a = Chart.render (SkiaBackend.renderPdfWith options) surface (figure ())
            let b = Chart.render (SkiaBackend.renderPdfWith options) surface (figure ())
            Assert.Equal(a.Length, b.Length)
            Assert.Equal<byte[]>(a, b))

    /// D63, asserted against the file rather than against the intention. Three keys, and each
    /// one of them moves between machines or between SkiaSharp versions.
    [<Fact>]
    let ``the document carries no clock and no toolchain version`` () =
        withSkia (fun () ->
            let s = text (Chart.render (SkiaBackend.renderPdfWith options) surface (figure ()))

            Assert.DoesNotContain("/CreationDate", s)
            Assert.DoesNotContain("/ModDate", s)

            // Left unset, Skia writes `/Producer (Skia/PDF m151)`, which puts the rasteriser's
            // build number in every file this library produces.
            Assert.DoesNotContain("Skia/PDF", s)
            Assert.Contains("/Producer (Marey)", s))

    [<Fact>]
    let ``metadata the caller states reaches the file`` () =
        withSkia (fun () ->
            let stated =
                { options with
                    Pdf =
                        { Title = "free fall"
                          Author = "Marey"
                          Subject = "a test"
                          Keywords = "kinematics"
                          Creator = "Marey"
                          Producer = "Marey" } }

            let s = text (Chart.render (SkiaBackend.renderPdfWith stated) surface (figure ()))

            Assert.Contains("/Title (free fall)", s)
            Assert.Contains("/Subject (a test)", s)
            Assert.Contains("/Keywords (kinematics)", s)

            // And an empty field is an absent key rather than `/Title ()`.
            let bare =
                { options with
                    Pdf = { PdfMetadata.Default with Creator = "" } }

            let t = text (Chart.render (SkiaBackend.renderPdfWith bare) surface (figure ()))
            Assert.DoesNotContain("/Creator", t))

    /// **Gate 4.** The fractional half is the point: 250 px is 187.5 pt, and a producer that
    /// rounded its page to whole points would still pass a test written at 400 x 240.
    [<Theory>]
    [<InlineData(400.0, 250.0, "300", "187.5")>]
    [<InlineData(800.0, 600.0, "600", "450")>]
    [<InlineData(300.0, 300.0, "225", "225")>]
    let ``the page is exactly the requested extent`` (w: float, h: float, expectW: string, expectH: string) =
        withSkia (fun () ->
            let extent: Extent<px> =
                { Width = w * 1.0<px>
                  Height = h * 1.0<px> }

            let s = text (Chart.render (SkiaBackend.renderPdfWith options) extent (figure ()))
            let expected = sprintf "/MediaBox [0 0 %s %s]" expectW expectH

            Assert.Contains(expected, s))

    /// A PDF has no bytes until its cross-reference table is written, so there is nothing honest
    /// to return from a partial render. The raster path *can* answer, and does — the asymmetry is
    /// stated rather than smoothed over.
    [<Fact>]
    let ``Result before Finish is refused on the pdf path and allowed on the raster one`` () =
        withSkia (fun () ->
            use pdf = new SkiaBackend(options, SkiaFormat.Pdf)
            (pdf :> IBackend).Begin surface
            Assert.Throws<System.InvalidOperationException>(fun () -> pdf.Result |> ignore) |> ignore

            use png = new SkiaBackend(options)
            (png :> IBackend).Begin surface
            Assert.NotEmpty png.Result)

    /// **Gate 5, through the second container.** D33's incident was a valid document containing
    /// no visible ink, and the vector path is a fresh opportunity for it: a PDF canvas that
    /// dropped every paint would produce a perfectly well-formed 900-byte file.
    ///
    /// Rasterised at 96 dpi, where the page's own `pt` size converts back to exactly the
    /// requested pixel extent — so the regions below are written in the numbers the layout used.
    [<Fact>]
    let ``the page has ink in it, and the right way up`` () =
        withSkia (fun () ->
            let pdf = Chart.render (SkiaBackend.renderPdfWith options) surface (figure ())

            match Raster.ofPdfBytes "skia-figure" pdf with
            | TexToolchain.PdfUnavailable _ -> System.Console.WriteLine("SKIPPED: " + Raster.report ())
            | TexToolchain.PdfFailed why -> failwithf "the Skia PDF did not rasterise: %s" why
            | TexToolchain.PdfProduced page ->
                // The page came back at the size the extent asked for, which is gate 4 again
                // through an independent instrument: Poppler measured the MediaBox this time.
                Assert.Equal(400, page.Width)
                Assert.Equal(250, page.Height)

                let coverage = Raster.coverage page

                Assert.True(
                    coverage > 0.005,
                    sprintf "the PDF page is essentially blank (%.4f%% ink)\n%s" (coverage * 100.0) (Raster.inkMap 72 page)
                )

                // The free-fall curve is convex and rising, so in *page* coordinates — y
                // downward — the bottom-left of the panel carries the curve and the top-left
                // carries nothing but a gridline. Same claim the PNG path makes, made again
                // against a different rasteriser and a different container.
                let region l t r b : Raster.Region =
                    { Left = l
                      Top = t
                      Right = r
                      Bottom = b }

                let lowerLeft = Raster.coverageIn (region 0.20 0.55 0.50 0.85) page
                let upperLeft = Raster.coverageIn (region 0.20 0.15 0.50 0.40) page

                Assert.True(
                    lowerLeft > upperLeft,
                    sprintf
                        "the curve is not where a rising convex curve should be: lower-left %.4f, upper-left %.4f\n%s"
                        lowerLeft
                        upperLeft
                        (Raster.inkMap 72 page)
                ))


    /// **A raster inside a vector page, which is the one thing the page scale could break.**
    ///
    /// `drawRaster` is the only place in this backend that touches the canvas matrix, and the
    /// PDF path puts a `Scale` under it. Using `SetMatrix` there instead of `Save` and `Concat`
    /// would *replace* the page's pt-per-px conversion and land the image at 4/3 its size,
    /// off the right edge — a bug the PNG path cannot see, because its page scale is the
    /// identity.
    ///
    /// The orientation half is D64's, restated here because a flip is exactly the kind of thing
    /// that survives one container and not the other: the fixture's top row is bright and its
    /// bottom row dark.
    [<Fact>]
    let ``a placed image survives the page scale, and the right way up`` () =
        withSkia (fun () ->
            let pdf =
                Chart.render (SkiaBackend.renderPdfWith options) Golden.figureSurface Golden.imageFigure

            match Raster.ofPdfBytes "skia-image" pdf with
            | TexToolchain.PdfUnavailable _ -> System.Console.WriteLine("SKIPPED: " + Raster.report ())
            | TexToolchain.PdfFailed why -> failwithf "the Skia PDF did not rasterise: %s" why
            | TexToolchain.PdfProduced page ->
                let region l t r b : Raster.Region =
                    { Left = l
                      Top = t
                      Right = r
                      Bottom = b }

                // Inside the panel, well clear of the axis lines on every side.
                let upper = Raster.meanLevel page (region 0.25 0.30 0.85 0.45)
                let lower = Raster.meanLevel page (region 0.25 0.60 0.85 0.75)

                Assert.True(
                    upper > lower + 20.0,
                    sprintf "the image is upside down, missing, or scaled off the page: upper mean %.1f, lower mean %.1f\n%s" upper lower (Raster.inkMap 72 page)
                ))

// ======================================================================================
// GATE 2 — SHAPING ACTUALLY CHANGES THE LAYOUT, AND THE AMOUNT IS WRITTEN DOWN
//
// The M2.5 plan states the gate and states what its *failure* would mean: "If this gate fails
// — if the numbers agree — then either the shaper is not being used or the approximation was
// already exact, and both are worth knowing before the milestone claims anything."
//
// It half-fails, in the interesting direction, and that is the finding of this phase. The
// approximation is **exact** against the unshaped face — 48.012 against 48.0117 for Arial 12 px
// "AVATAR" — because `ApproxTextMeasurer`'s table is Helvetica's and Arial is metric-compatible
// with Helvetica. So every bit of the 7.3% difference is kerning, and none of it is a
// coincidentally-cancelling pair of errors in the advance table. That attribution is what the
// first two tests below establish, and it is a stronger claim than "the numbers differ".
//
// D62's stated face is what makes the numbers reproducible: `tests/Marey.Tests/fonts/` carries
// Liberation Sans, which is metric-compatible with Arial to the last digit measured here. See
// that directory's README for why a binary is in the source tree.
// ======================================================================================

module ``text shaping`` =

    open SkiaSharp

    /// D62's stated face, found the way the baselines are found.
    let private fontFile =
        lazy (System.IO.Path.Combine(Golden.projectDir.Force(), "fonts", "LiberationSans-Regular.ttf"))

    /// 9 pt is 12 px at the screen dpi both measurers default to, and 12 px is the size every
    /// number in this file and in the decision log was measured at.
    let private spec: FontSpec =
        { Family = "Liberation Sans"
          Size = 9.0<pt>
          Weight = 400
          Italic = false }

    /// The string the plan picked, and it earns its place: `AV`, `VA`, `TA` and `AT` are four of
    /// the five kerning pairs any Latin face has, so the effect is large enough to survive
    /// rounding and small enough to be believable.
    [<Literal>]
    let private Kerned = "AVATAR"

    /// Digits are the control. A face that kerned these would be a broken face, so this string
    /// is where the shaped and approximated numbers *should* agree — and if it disagreed by as
    /// much as `AVATAR` does, the difference would be an advance-table error rather than
    /// kerning and every claim in this section would be about the wrong thing.
    [<Literal>]
    let private Unkerned = "0123456789"

    [<Fact>]
    let ``the approximation is exact against the unshaped face, so the difference is kerning alone`` () =
        withSkia (fun () ->
            use tf = SKTypeface.FromFile(fontFile.Force())
            Assert.False(isNull tf, "the stated face did not open")
            use font = new SKFont(tf, 12.0f)

            // **Linear metrics, or this measures Skia's grid fitting rather than the face.** A
            // default `SKFont` hints, and hinting rounds every glyph advance to a whole pixel:
            // "AVATAR" then measures 48.0 and the digits control measures 70.0 against a true
            // 66.74. Neither is a number Marey ever computes — `SkiaTextMeasurer` measures
            // through `SKShaper`, which is unhinted — so comparing the table against them would
            // be a claim about one rasteriser's grid fit, not about the advance table, and it
            // is the shaper's numbers the other tests in this section use.
            font.LinearMetrics <- true

            let approximated = float (TextMeasure.approx.Measure spec Kerned).Advance
            let unshaped = float (font.MeasureText Kerned)

            // 48.012 against 48.0117. `ApproxTextMeasurer`'s table is Helvetica's, Arial is
            // metric-compatible with Helvetica, and Liberation Sans is metric-compatible with
            // Arial — so this holds by construction rather than by luck.
            //
            // **Stated as a difference, and not as `Assert.Equal(_, _, 2)`.** That overload
            // rounds both sides to a number of decimal places and compares the rounded values,
            // so its real tolerance is however far the two happen to sit from a bucket edge —
            // not a bound anyone chose, and it can reject a pair closer together than one it
            // accepts. It was what stood here, and it rejected 48.0 against 48.012.
            //
            // The bound: the table stores 1/1000 em, which is 0.012 px at 12 px, and these two
            // agree to a *tenth* of that — 0.00028 px, or 1/42000 em. A tenth of a unit rather
            // than a whole one, because the hinted measurement this test used to take is out by
            // exactly one unit: `< 0.012` would separate them only by double rounding, four
            // parts in 1e16, which is not a margin an argument can rest on.
            Assert.True(
                abs (unshaped - approximated) < 0.0012,
                sprintf
                    "the advance tables disagree by %.4f px, so a shaped-vs-approximated difference could not be attributed to kerning"
                    (abs (unshaped - approximated))
            ))

    [<Fact>]
    let ``a kerned string measures shorter shaped than approximated, by 7.3 per cent`` () =
        withSkia (fun () ->
            use skia = new SkiaTextMeasurer(fontFile = fontFile.Force())
            let shaped = float ((skia :> ITextMeasurer).Measure spec Kerned).Advance
            let approximated = float (TextMeasure.approx.Measure spec Kerned).Advance

            // The numbers, stated rather than described: 44.5078 shaped against 48.012
            // approximated, 3.504 px and 7.30% at 12 px.
            Assert.Equal(44.5078, shaped, 3)
            Assert.Equal(48.012, approximated, 3)

            let ratio = (approximated - shaped) / approximated

            Assert.True(
                ratio > 0.05 && ratio < 0.10,
                sprintf "expected a kerning difference near 7.3%%, got %.2f%% (shaped %.4f, approximated %.4f)" (ratio * 100.0) shaped approximated
            ))

    [<Fact>]
    let ``a string with nothing to kern measures the same either way`` () =
        withSkia (fun () ->
            use skia = new SkiaTextMeasurer(fontFile = fontFile.Force())
            let shaped = float ((skia :> ITextMeasurer).Measure spec Unkerned).Advance
            let approximated = float (TextMeasure.approx.Measure spec Unkerned).Advance
            let ratio = abs (approximated - shaped) / approximated

            Assert.True(
                ratio < 0.005,
                sprintf
                    "the digits disagree by %.2f%% (shaped %.4f, approximated %.4f) -- a difference here is an advance-table error, not kerning"
                    (ratio * 100.0)
                    shaped
                    approximated
            ))

    /// **The vertical half, and it is the larger one.**
    ///
    /// `ApproxTextMeasurer` reports Helvetica's own ascent and descent as fractions of the em —
    /// 0.718 and 0.207. A real face reports its `hhea` metrics, which include room for accents
    /// and internal leading, and Liberation Sans's come to 0.905 and 0.212 of the em. So the
    /// *height* of a laid-out string changes by more than its width does, and that is what
    /// actually moves a panel rectangle: gutters are sized by label heights at least as much as
    /// by label widths.
    ///
    /// Neither number is wrong. They answer different questions — "how tall is Helvetica's
    /// caps-and-descenders band" and "how much vertical room does this face reserve" — and the
    /// second is the one a renderer that owns the glyphs can actually answer.
    [<Fact>]
    let ``a real face reserves more vertical room than the approximation estimates`` () =
        withSkia (fun () ->
            use skia = new SkiaTextMeasurer(fontFile = fontFile.Force())
            let shaped = (skia :> ITextMeasurer).Measure spec Kerned
            let approximated = TextMeasure.approx.Measure spec Kerned

            Assert.True(
                float shaped.Ascent > float approximated.Ascent,
                sprintf "shaped ascent %.4f is not greater than the approximated %.4f" (float shaped.Ascent) (float approximated.Ascent)
            )

            // 10.863 against 8.616 at 12 px, and 2.543 against 2.484.
            Assert.Equal(10.8633, float shaped.Ascent, 3)
            Assert.Equal(2.543, float shaped.Descent, 3))


    /// **A glyph the face does not have is `.notdef`, and there is no fallback.** Recorded as a
    /// test rather than as a comment, because it is a real regression in one narrow place.
    ///
    /// `SKShaper` shapes against **one** typeface. A codepoint outside it becomes glyph 0 and
    /// measures as the missing-glyph box — 9.0 px at 12 px in Liberation Sans — where the SVG
    /// backend would have handed the string to a viewer that walks the whole font stack, and
    /// `ApproxTextMeasurer` would have guessed 6.7 px from its fallback width. All three are
    /// different and only the browser's is right.
    ///
    /// It does not reach the unit system: every symbol the `U` module produces — `·`, `²`,
    /// `⁻¹`, `Ω`, `µ`, `Å` — is in any Latin face, and the probe that measured this checked
    /// them. It reaches a CJK axis title, which is M3's problem and wants a font-fallback list
    /// rather than a single face.
    [<Fact>]
    let ``a codepoint the face lacks measures as the missing-glyph box`` () =
        withSkia (fun () ->
            use skia = new SkiaTextMeasurer(fontFile = fontFile.Force())
            let m = skia :> ITextMeasurer

            // In the face: the unit system's whole alphabet, and none of it degenerate.
            for symbol in [ "µm"; "Ω"; "m·s⁻¹"; "Å" ] do
                let shaped = float (m.Measure spec symbol).Advance
                let approximated = float (TextMeasure.approx.Measure spec symbol).Advance

                Assert.True(
                    abs (shaped - approximated) / approximated < 0.25,
                    sprintf "'%s' measured %.4f shaped against %.4f approximated -- a unit symbol should not be a missing glyph" symbol shaped approximated
                )

            // Not in the face, and it shows: one `.notdef` box, not a fallback face's glyph.
            let cjk = float (m.Measure spec "中").Advance
            Assert.Equal(9.0, cjk, 3))

    /// The nd bounding box of the longest polyline in a scene.
    ///
    /// **This is the panel rectangle, observed through what is drawn in it.** The data curve
    /// spans the plot area by construction — `Scale.linear` maps the domain onto the panel's
    /// full width and height — so its extent in nd *is* the panel, and reading it from the
    /// scene asks the question the way a reader of the picture would. `Figure.arrangement`
    /// cannot be used: it reports the *outer* grid, whose only cell is `panel-0-0`, and the
    /// plot rectangle is chosen inside `Figure.compilePanel`, which is private.
    let private curveBounds (scene: Node) =
        let mutable x0, y0, x1, y1 = infinity, infinity, -infinity, -infinity
        let mutable best = 0

        // **The transform has to be threaded, not ignored.** D15/D30: a panel's geometry is
        // authored in panel-local nd and placed by a `Transform` node, so the raw coordinates
        // on the curve are the *domain padding* — 0.0455 to 0.9545 whatever the layout does —
        // and reading them without composing the transform measures the padding constant
        // instead of the panel. That was the first spelling of this helper and it reported the
        // same box under both measurers, which is exactly the false pass a gate must not give.
        let rec walk (ctm: Affine<nd, nd>) node =
            match node with
            | Path(segs, _, _, _) when segs.Length > best ->
                best <- segs.Length
                x0 <- infinity
                y0 <- infinity
                x1 <- -infinity
                y1 <- -infinity

                let take (p: Vec2<nd>) =
                    let q = Affine.apply ctm p
                    x0 <- min x0 (float q.X)
                    y0 <- min y0 (float q.Y)
                    x1 <- max x1 (float q.X)
                    y1 <- max y1 (float q.Y)

                for seg in segs do
                    match seg with
                    | MoveTo p
                    | LineTo p -> take p
                    | CubicTo(_, _, p) -> take p
                    | ClosePath -> ()
            | Path _
            | Text _
            | Markers _
            | Raster _ -> ()
            | Group ns -> Array.iter (walk ctm) ns
            | Transform(a, c) -> walk (Affine.compose a ctm) c
            | Clip(_, c)
            | Opacity(_, c)
            | Keyed(_, c)
            | Annotated(_, c) -> walk ctm c

        walk Affine.identity scene
        x0, y0, x1, y1

    /// **Gate 2's second half: the difference reaches the layout.**
    ///
    /// A measurement that changed and a layout that did not would mean the seam is decorative,
    /// which is precisely what M0's comment on `ITextMeasurer` promised it was not: "having one
    /// seam here is what makes it possible to start with an approximation and swap in HarfBuzz
    /// at M2 without touching the layout code." Not one line of `Layout.fs` or `Figure.fs`
    /// changed this milestone, and the panel rectangle moved anyway.
    [<Fact>]
    let ``the difference reaches the panel rectangle`` () =
        withSkia (fun () ->
            use skia = new SkiaTextMeasurer(fontFile = fontFile.Force())
            let fig = figure ()

            let ax0, ay0, ax1, ay1 = curveBounds (Chart.toSceneWith TextMeasure.approx surface fig)
            let sx0, sy0, sx1, sy1 = curveBounds (Chart.toSceneWith (skia :> ITextMeasurer) surface fig)

            let show (a, b, c, d) = sprintf "(%.4f, %.4f)-(%.4f, %.4f)" a b c d

            // Both edges the gutters control, so the claim is not satisfied by one of them
            // moving: the left edge is driven by the y tick labels' *advances* and the top edge
            // by the title's and x labels' *heights*, and shaping changes both.
            Assert.True(
                abs (ax0 - sx0) > 1e-6,
                sprintf "the panel's left edge did not move: approx %s -- shaped %s" (show (ax0, ay0, ax1, ay1)) (show (sx0, sy0, sx1, sy1))
            )

            Assert.True(
                abs (ay1 - sy1) > 1e-6,
                sprintf "the panel's top edge did not move: approx %s -- shaped %s" (show (ax0, ay0, ax1, ay1)) (show (sx0, sy0, sx1, sy1))
            )

            // And the panel is still a sane rectangle inside the unit square, because "it
            // moved" is also satisfied by a layout that collapsed.
            Assert.InRange(sx0, 0.0, 1.0)
            Assert.InRange(sy1, 0.0, 1.0)
            Assert.True(sx1 > sx0)
            Assert.True(sy1 > sy0)

            System.Console.WriteLine(
                sprintf "panel in nd: approx %s -- shaped %s" (show (ax0, ay0, ax1, ay1)) (show (sx0, sy0, sx1, sy1))
            ))

    /// D8's claim, from M0: `Chart.renderTo` lays out against the backend's own measurer, "so
    /// nothing needs to change when SkiaSharp arrives at M2". Never tested, because until now
    /// every backend had the same measurer. This is the test.
    [<Fact>]
    let ``renderTo lays out against the backend's own measurer, unchanged`` () =
        withSkia (fun () ->
            let options =
                { SkiaOptions.Default with
                    FontFile = Some(fontFile.Force()) }

            use backend = new SkiaBackend(options)
            Chart.renderTo backend surface (figure ())

            // Not one line of `Chart.renderTo` changed this milestone. What changed is which
            // measurer it picks up, and the proof is that the scene it laid out is the shaped
            // one rather than the approximated one.
            let ax0, _, _, _ =
                curveBounds (Chart.toSceneWith TextMeasure.approx surface (figure ()))

            let sx0, _, _, _ =
                curveBounds (Chart.toSceneWith backend.TextMeasurer surface (figure ()))

            Assert.True(abs (ax0 - sx0) > 1e-6)
            Assert.True(backend.Result.Length > 0))

// ======================================================================================
// D62 — THE FONT IS RESOLVED AND REPORTED, OR STATED AND OBEYED
// ======================================================================================

module ``the resolved face`` =

    let private fontFile =
        lazy (System.IO.Path.Combine(Golden.projectDir.Force(), "fonts", "LiberationSans-Regular.ttf"))

    let private sans: FontSpec =
        { Family = Theme.Sans
          Size = 9.0<pt>
          Weight = 400
          Italic = false }

    [<Fact>]
    let ``a stated file is the face that is used, and it says so`` () =
        withSkia (fun () ->
            use m = new SkiaTextMeasurer(fontFile = fontFile.Force())
            let face = m.Face sans

            // The theme asked for a seven-entry CSS stack and got none of it, because a stated
            // file overrides resolution entirely. `Matched = None` and `File = Some` together
            // are how a reader tells that apart from "the stack fell through".
            Assert.Equal("Liberation Sans", face.Family)
            Assert.Equal<string option>(Some(fontFile.Force()), face.File)
            Assert.Equal<string option>(None, face.Matched)
            Assert.Equal(Theme.Sans, face.Requested))

    [<Fact>]
    let ``a stack that matches nothing falls through to a real face and admits it`` () =
        withSkia (fun () ->
            use m = new SkiaTextMeasurer()

            let face =
                m.Face
                    { sans with
                        Family = "Marey No Such Face, Also Not This One" }

            // The fall-through must still produce something drawable — a null typeface here is
            // a blank figure, which is D33's failure mode.
            Assert.False(System.String.IsNullOrEmpty face.Family)
            Assert.Equal<string option>(None, face.Matched)
            Assert.Equal<string option>(None, face.File))

    /// Machine-dependent by nature, so the assertion is the *invariant* rather than the answer:
    /// whatever matched must be a name that was actually asked for, and must be the family that
    /// was actually opened. On a machine with none of the stack installed both are `None`, and
    /// that is a pass — CI runs on a different font set from the author's laptop and this gate
    /// has to mean the same thing on both.
    [<Fact>]
    let ``a resolved stack reports an entry it was actually asked for`` () =
        withSkia (fun () ->
            use m = new SkiaTextMeasurer()
            let face = m.Face sans

            match face.Matched with
            | None -> Assert.False(System.String.IsNullOrEmpty face.Family)
            | Some name ->
                Assert.Contains(name, Theme.Sans)
                Assert.Equal(name.ToLowerInvariant(), face.Family.ToLowerInvariant()))

    [<Fact>]
    let ``the backend reports every face the scene asked for, and nothing it did not`` () =
        withSkia (fun () ->
            let options =
                { SkiaOptions.Default with
                    FontFile = Some(fontFile.Force()) }

            use backend = new SkiaBackend(options)

            // Lazy resolution: nothing has been measured, so nothing has been opened.
            Assert.Empty backend.ResolvedFaces

            Chart.renderTo backend surface (figure ())

            Assert.NotEmpty backend.ResolvedFaces

            for face in backend.ResolvedFaces do
                Assert.Equal<string option>(Some(fontFile.Force()), face.File)
                Assert.Equal("Liberation Sans", face.Family))

    [<Fact>]
    let ``a font file that is not there fails at construction, naming the path`` () =
        withSkia (fun () ->
            let missing =
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marey-no-such-font.ttf")

            let e =
                Assert.Throws<System.ArgumentException>(fun () -> (new SkiaTextMeasurer(fontFile = missing)) |> ignore)

            // Gate 8's shape, applied to a font: a message that names what is wrong beats a
            // null dereference on the first tick label three frames down.
            Assert.Contains(missing, e.Message))

    /// The measurer owns native handles and hands them out without ownership, so using one after
    /// disposal is a use-after-free waiting to happen. It throws instead.
    [<Fact>]
    let ``a disposed measurer refuses to measure`` () =
        withSkia (fun () ->
            let m = new SkiaTextMeasurer(fontFile = fontFile.Force())
            (m :> ITextMeasurer).Measure sans "x" |> ignore
            (m :> System.IDisposable).Dispose()

            Assert.Throws<System.ObjectDisposedException>(fun () -> (m :> ITextMeasurer).Measure sans "x" |> ignore)
            |> ignore)

    /// A stated face is the only way two machines produce the same pixels, so the property has
    /// to hold across *instances* rather than within one — a cache that returned the same bytes
    /// because it never re-rendered would satisfy a weaker test.
    [<Fact>]
    let ``a stated face renders the same bytes from two separate backends`` () =
        withSkia (fun () ->
            let options =
                { SkiaOptions.Default with
                    FontFile = Some(fontFile.Force()) }

            let once () =
                use backend = new SkiaBackend(options)
                Chart.renderTo backend surface (figure ())
                backend.Result

            Assert.Equal<byte[]>(once (), once ()))

// ======================================================================================
// D67 — GROUP OPACITY IS COMPOSITING, NOT AN ALPHA ON EACH SHAPE
//
// This backend advertises `GroupOpacity = true`, and the claim behind that flag is not "the
// group comes out fainter". It is that the subtree is composited *first* and the result faded,
// which differs from folding the constant into each paint exactly where the group's own shapes
// overlap. A pixel in the overlap is where the two answers separate, so that is where this
// looks — the flag is otherwise indistinguishable from the degraded path, and a capability
// nothing can tell apart from its own fallback is a capability nobody should trust.
// ======================================================================================

module ``group opacity composites`` =

    let private box (c: Color) (x0: float) (y0: float) (x1: float) (y1: float) =
        Scene.rect
            None
            (Some(Solid c))
            { X0 = x0 * 1.0<nd>
              Y0 = y0 * 1.0<nd>
              X1 = x1 * 1.0<nd>
              Y1 = y1 * 1.0<nd> }

    /// Opaque red, then opaque blue over it, both inside one `Opacity 0.5`, on a white page.
    ///
    /// * **Composited** — blue wins the overlap outright, then the group is faded once against
    ///   white: `(127, 127, 255)`.
    /// * **Folded into each paint** — red at half over white gives `(255, 127, 127)`, and blue
    ///   at half over *that* gives `(127, 64, 191)`.
    ///
    /// Sixty-three levels apart on green and blue both. There is no tolerance argument to have
    /// here: the two answers are different colours, and the test names the one it expects rather
    /// than asserting that something changed.
    [<Fact>]
    let ``the overlap of a faded group is the composite, not the double fade`` () =
        withSkia (fun () ->
            let scene =
                Scene.opacity
                    0.5
                    (Group
                        [| box (Color.rgb 1.0 0.0 0.0) 0.1 0.1 0.6 0.9
                           box (Color.rgb 0.0 0.0 1.0) 0.4 0.1 0.9 0.9 |])

            let png = SkiaBackend.renderPng surface scene

            // Inside the overlap: x in [0.4, 0.6] of the panel, and `nd` is y-up while the
            // reader's y runs down — the band spans the full height, so the flip does not
            // matter here and is not being relied on.
            let r, g, b = SkiaPixels.rgbAt png 0.5 0.5

            let near (expected: int) (actual: int) name =
                Assert.True(
                    abs (expected - actual) <= 2,
                    sprintf "%s was %d, expected %d -- the group was faded per shape rather than composited" name actual expected
                )

            near 127 r "red"
            near 127 g "green"
            near 255 b "blue"

            // The red-only band, where both answers agree, so a failure above is about
            // compositing and not about the fade happening at all.
            let r2, g2, b2 = SkiaPixels.rgbAt png 0.2 0.5
            near 255 r2 "red (no overlap)"
            near 127 g2 "green (no overlap)"
            near 127 b2 "blue (no overlap)")

    /// **The gate can fail.** The same scene put through `Degrade.forCaps` with the capability
    /// denied produces the other colour, from the same backend and the same reader — so the test
    /// above is measuring compositing rather than measuring whatever Skia happens to do.
    [<Fact>]
    let ``the degraded scene produces the double fade instead`` () =
        withSkia (fun () ->
            let scene =
                Scene.opacity
                    0.5
                    (Group
                        [| box (Color.rgb 1.0 0.0 0.0) 0.1 0.1 0.6 0.9
                           box (Color.rgb 0.0 0.0 1.0) 0.4 0.1 0.9 0.9 |])

            let degraded = Degrade.forCaps { Capabilities.full with GroupOpacity = false } scene
            let png = SkiaBackend.renderPng surface degraded
            let r, g, b = SkiaPixels.rgbAt png 0.5 0.5

            Assert.True(
                abs (r - 127) <= 2 && abs (g - 64) <= 2 && abs (b - 191) <= 2,
                sprintf "the folded scene rendered (%d, %d, %d); the double fade is (127, 64, 191)" r g b
            ))

// ======================================================================================
// D78 — FONT FALLBACK: THE SEGMENTATION, AND THE REPORT THAT MAKES IT HONEST
//
// M2.5 left one sentence open and `m2.5-plan.md` §5 named it M3's: `SKShaper` shapes against a
// single typeface, so a codepoint that face lacks measures as the missing-glyph box — 9.0 px at
// 12 px in Liberation Sans — where a browser opening the same figure as SVG walks the whole
// stack and finds a face that has it. `a codepoint the face lacks measures as the missing-glyph
// box`, above, is that defect recorded as a test. It still passes, and it should: a *stated*
// file is obeyed alone, and this section is what changed around it.
//
// **The gates here are reproducible on any checkout, and that took a second binary.** The
// covered half of gate 14 needs a face that has a CJK codepoint, and neither of the two ways of
// getting one from the machine is a gate: `ubuntu-latest` has no CJK face, so a machine-resolved
// assertion would skip on CI and pass on the author's laptop, and adding `fonts-noto-cjk` to the
// apt list buys 60 MB of download for a result that still depends on which face apt installed.
// `fonts/MareyTestCJK-Regular.otf` is a 4.7 KB subset of Noto Sans CJK JP carrying five
// ideographs and no Latin at all — see that directory's README. Its family name is *not* Noto's,
// which is what lets a gate tell "the stated file was used" from "the machine happened to have
// Noto installed".
//
// The machine path is gated too, and it is gated as a **dichotomy** rather than as a skip: a
// codepoint either resolves to a named face or is reported missing, and the test asserts
// whichever of those the machine produces. Gates 12 and 13 go vacuous when a toolchain is
// absent and `Toolchain.report` exists so a reader can see that they did; this one cannot go
// vacuous, because there is no third answer.
// ======================================================================================

module ``font fallback`` =

    /// The two stated faces, found the way the baselines are found.
    let private latin =
        lazy (System.IO.Path.Combine(Golden.projectDir.Force(), "fonts", "LiberationSans-Regular.ttf"))

    let private cjk =
        lazy (System.IO.Path.Combine(Golden.projectDir.Force(), "fonts", "MareyTestCJK-Regular.otf"))

    /// 9 pt is 12 px at the screen dpi both measurers default to, and 12 px is the size every
    /// number in this file and in the decision log was measured at.
    let private spec: FontSpec =
        { Family = "Liberation Sans"
          Size = 9.0<pt>
          Weight = 400
          Italic = false }

    /// `気温` — "temperature", and a real axis title rather than a lone specimen character. Both
    /// ideographs are in the subset face and neither is in Liberation Sans.
    [<Literal>]
    let private Cjk = "気温"

    /// The same title as a figure would actually carry it: a quantity in one script and its unit
    /// in another. This is the string the whole feature exists for.
    [<Literal>]
    let private Mixed = "気温 (°C)"

    /// **Three segments, and the third is what makes the pen walk testable.** `Mixed` splits in
    /// two, and a pen that advanced by the *first* segment's width every time would place a
    /// two-segment run correctly and be wrong from the third onwards — a mutation that survived
    /// this gate until the string grew a second script change. CJK, Latin, CJK.
    [<Literal>]
    let private Twice = "気温 (°C) 時間"

    let private measure (m: SkiaTextMeasurer) (s: string) = (m :> ITextMeasurer).Measure spec s

    /// **Gate 14, the half that reports nothing covered it.**
    ///
    /// A stated file is obeyed *alone*: D62's "stated and obeyed" is a promise that the figure
    /// contains the face the caller named and no other, and a fallback that quietly opened a
    /// second one would have broken it. So the advance is still the missing-glyph box, exactly
    /// as it was before D78 — and what is new is that the report says so instead of leaving the
    /// caller to notice a row of boxes in a PNG.
    [<Fact>]
    let ``a stated file is obeyed alone, and what it lacks is a reported notdef`` () =
        withSkia (fun () ->
            use m = new SkiaTextMeasurer(fontFile = latin.Force())

            // 9.0 px at 12 px, the number `a codepoint the face lacks measures as the
            // missing-glyph box` pinned at M2.5. Two boxes, so 18.0.
            Assert.Equal(18.0, float (measure m Cjk).Advance, 3)

            let face = Assert.Single m.ResolvedFaces
            Assert.Empty face.Fallbacks
            Assert.Equal<int[]>([| 0x6C17; 0x6E29 |], face.Missing)

            // The band did not move either: nothing but Liberation Sans was opened.
            Assert.Equal(10.8633, float (measure m Cjk).Ascent, 3))

    /// **Gate 14, the half that names the face that covered it.**
    ///
    /// The same string, the same primary face, one more entry on the list — and the boxes become
    /// glyphs: 24.0 px against the 18.0 above, because the subset face's ideographs are full-width
    /// at 12 px where the missing-glyph box is 9.0.
    [<Fact>]
    let ``a stated list covers what the first face cannot, and names the face that covered it`` () =
        withSkia (fun () ->
            use m = new SkiaTextMeasurer(fontFiles = [ latin.Force(); cjk.Force() ])

            Assert.Equal(24.0, float (measure m Cjk).Advance, 3)

            let face = Assert.Single m.ResolvedFaces
            Assert.Equal("Liberation Sans", face.Family)
            Assert.Empty face.Missing

            let fallback = Assert.Single face.Fallbacks

            // The name, and it is the *subset's* name rather than Noto's. A machine-resolved
            // fallback on a laptop that has Noto installed would report "Noto Sans CJK JP", so
            // this assertion cannot pass for the wrong reason.
            Assert.Equal("Marey Test CJK", fallback.Family)
            Assert.Equal<string option>(Some(cjk.Force()), fallback.File)
            Assert.Equal<int[]>([| 0x6C17; 0x6E29 |], fallback.Covered))

    /// One run, one codepoint the list covers and one it does not, reported in different places.
    ///
    /// `中華` — the first ideograph is in the subset face, the second deliberately is not. A
    /// report that lumped them together, or that stopped at the first miss, would fail here and
    /// nowhere else: 21.0 px is 12.0 for the covered glyph plus the 9.0 box for the other.
    [<Fact>]
    let ``a covered codepoint and an uncovered one in one run are reported separately`` () =
        withSkia (fun () ->
            use m = new SkiaTextMeasurer(fontFiles = [ latin.Force(); cjk.Force() ])

            Assert.Equal(21.0, float (measure m "中華").Advance, 3)

            let face = Assert.Single m.ResolvedFaces
            Assert.Equal<int[]>([| 0x83EF |], face.Missing)
            Assert.Equal<int[]>([| 0x4E2D |], (Assert.Single face.Fallbacks).Covered))

    /// **The segmentation arithmetic, stated as an equality rather than as a tolerance.**
    ///
    /// `気温 (°C)` splits at the space — per codepoint, first face that has it, so the space
    /// comes back to the primary because the primary has a space. Measured through the list it
    /// is 48.8203 px; measured as its two pieces through the two faces alone, 24.0 and 24.8203.
    /// Those are equal to the last digit, which is the whole claim: a segmented run is its
    /// pieces, and nothing is lost or double-counted at a boundary.
    [<Fact>]
    let ``a mixed run measures exactly the sum of its pieces measured alone`` () =
        withSkia (fun () ->
            use both = new SkiaTextMeasurer(fontFiles = [ latin.Force(); cjk.Force() ])
            use onlyLatin = new SkiaTextMeasurer(fontFile = latin.Force())
            use onlyCjk = new SkiaTextMeasurer(fontFile = cjk.Force())

            let whole = float (measure both Mixed).Advance
            let pieces = float (measure onlyCjk Cjk).Advance + float (measure onlyLatin " (°C)").Advance

            Assert.Equal(48.8203, whole, 3)
            Assert.Equal(pieces, whole, 4))

    /// **The vertical half, and it is the one a backend can silently get wrong.**
    ///
    /// The subset face reserves 13.92 px of ascent at 12 px against Liberation Sans's 10.8633,
    /// so a run that uses it is taller than a Latin run beside it — which is true, and is what a
    /// browser does. The fold is a maximum *including the primary*, so a Latin run is unchanged
    /// and every number M2.5 measured still holds; the assertion below is what says the maximum
    /// is being taken rather than the last segment's metrics being kept.
    [<Fact>]
    let ``the run's band is the maximum over the faces it uses`` () =
        withSkia (fun () ->
            use m = new SkiaTextMeasurer(fontFiles = [ latin.Force(); cjk.Force() ])

            let latinOnly = measure m "AVATAR"
            Assert.Equal(10.8633, float latinOnly.Ascent, 3)
            Assert.Equal(2.543, float latinOnly.Descent, 3)

            // The mixed run uses both faces, and takes the larger of each — not the primary's,
            // which would clip the ideographs, and not the fallback's alone, which is what
            // keeping the last segment's font would give.
            let mixed = measure m Mixed
            Assert.Equal(13.92, float mixed.Ascent, 3)
            Assert.Equal(3.456, float mixed.Descent, 3))

    /// A Latin run under a fallback list is one segment, and one segment is what the milestone
    /// before this one measured. The regression this guards is the whole of M2.5's shaping
    /// evidence quietly moving because a run is now assembled rather than shaped in one go.
    [<Fact>]
    let ``adding a fallback face moves no number in a Latin run`` () =
        withSkia (fun () ->
            use alone = new SkiaTextMeasurer(fontFile = latin.Force())
            use listed = new SkiaTextMeasurer(fontFiles = [ latin.Force(); cjk.Force() ])

            // 44.5078, the shaped `AVATAR` this suite has asserted since M2.5.
            Assert.Equal(float (measure alone "AVATAR").Advance, float (measure listed "AVATAR").Advance, 6)
            Assert.Equal(float (measure alone "0123456789").Advance, float (measure listed "0123456789").Advance, 6)

            // And no fallback was consulted, so the report stays empty rather than naming a face
            // that drew nothing.
            Assert.Empty (Assert.Single listed.ResolvedFaces).Fallbacks)

    /// Two answers to one question, refused at construction rather than silently ranked.
    ///
    /// The alternative — letting one win — is a caller who states a list, gets a single file's
    /// behaviour, and finds out from a row of boxes in a PNG.
    [<Fact>]
    let ``a stated file and a stated list are two answers to one question`` () =
        withSkia (fun () ->
            Assert.Throws<System.ArgumentException>(fun () ->
                (new SkiaTextMeasurer(fontFile = latin.Force(), fontFiles = [ cjk.Force() ]))
                |> ignore)
            |> ignore)

    /// **The machine path, and the branch it takes is decided by something other than itself.**
    ///
    /// With nothing stated, fallback asks the machine's font manager, and the answer depends on
    /// what is installed — the case a real user is in, and the case that makes a CJK axis title
    /// render at all. There is no reproducible number to assert here, so the shape of the gate
    /// is: ask the font manager *directly* whether this machine can cover U+4E2D, then require
    /// the measurer's report to agree.
    ///
    /// Asking independently is the whole point. A dichotomy that only checked "either a face is
    /// named or the codepoint is missing" is satisfied by a measurer with no fallback at all —
    /// it would take the missing branch everywhere and look right — which is what a mutation run
    /// found. The oracle below is a second opinion rather than the same opinion twice, and
    /// `ubuntu-latest` and a developer's laptop land on opposite branches of it.
    [<Fact>]
    let ``machine fallback names a face exactly when the machine has one`` () =
        withSkia (fun () ->
            use m = new SkiaTextMeasurer()

            let spec = { spec with Family = Theme.Sans }
            let advance = float ((m :> ITextMeasurer).Measure spec "中").Advance
            let face = Assert.Single m.ResolvedFaces

            // The oracle: SkiaSharp's own font manager, asked the same question by a different
            // route from the one the measurer took.
            let installed =
                use probe =
                    SkiaSharp.SKFontManager.Default.MatchCharacter(
                        null,
                        SkiaSharp.SKFontStyleWeight.Normal,
                        SkiaSharp.SKFontStyleWidth.Normal,
                        SkiaSharp.SKFontStyleSlant.Upright,
                        null,
                        0x4E2D
                    )

                not (isNull probe)

            Assert.Equal(installed, face.Fallbacks.Length = 1)

            match face.Fallbacks with
            | [||] ->
                Assert.Equal<int[]>([| 0x4E2D |], face.Missing)

                System.Console.WriteLine
                    "font fallback: no installed face has U+4E2D; the codepoint is reported missing."
            | fallbacks ->
                let fallback = Assert.Single fallbacks

                // Resolved rather than stated, so there is no file behind it -- which is exactly
                // what makes this render irreproducible on another machine and exactly why D62
                // makes the measurer say so.
                Assert.Equal<string option>(None, fallback.File)
                Assert.False(System.String.IsNullOrWhiteSpace fallback.Family)
                Assert.Equal<int[]>([| 0x4E2D |], fallback.Covered)
                Assert.Empty face.Missing

                // A real glyph, not the 9.0 px box the primary would have drawn.
                Assert.True(
                    advance > 9.5,
                    sprintf "%s reported covering U+4E2D but the advance is %.4f, which is the missing-glyph box" fallback.Family advance
                )

                System.Console.WriteLine(sprintf "font fallback: U+4E2D resolved to %s." fallback.Family))

    /// **D77 and gate 3, one level down.** One measurer serves a whole film so that a video
    /// cannot change typeface on a frame boundary; a fallback that asked the font manager afresh
    /// on frame 200 would put that back, invisibly, in a file nobody re-reads.
    ///
    /// So the decision is cached per codepoint, and this is what says so: the same codepoint
    /// asked a hundred times over reports one fallback face, not a hundred, and the answer does
    /// not depend on which run it arrived in.
    [<Fact>]
    let ``a codepoint resolves to one face however often it is asked`` () =
        withSkia (fun () ->
            use m = new SkiaTextMeasurer(fontFiles = [ latin.Force(); cjk.Force() ])

            let cjkFirst = float (measure m Cjk).Advance
            let mixedFirst = float (measure m Mixed).Advance

            // Alternated rather than repeated, so a cache keyed by the *string* would not be
            // what makes this pass: the two runs share codepoints and the question is whether
            // the codepoints resolve the same way whichever run they arrive in.
            for i in 1..100 do
                if i % 2 = 0 then
                    Assert.Equal(cjkFirst, float (measure m Cjk).Advance, 6)
                else
                    Assert.Equal(mixedFirst, float (measure m Mixed).Advance, 6)

            let face = Assert.Single m.ResolvedFaces

            // And the covered set is a set: 気 arrived a hundred times and is listed once.
            Assert.Equal<int[]>([| 0x6C17; 0x6E29 |], (Assert.Single face.Fallbacks).Covered))

    /// **The empty string still has a band, and it is the primary's.**
    ///
    /// A blank tick label is common on a sparse axis, and it has no segments at all — so the
    /// vertical fold is over nothing, and only the primary face standing in it unconditionally
    /// keeps the answer from being zero. A gutter sized from a zero-height label is a label that
    /// collides with the panel.
    ///
    /// This is also the *only* place the "always include the primary" rule is observable: the
    /// subset face is taller than Liberation Sans in both directions, so for every non-empty run
    /// the maximum is the same with the primary in the fold or out of it. Stated here rather
    /// than left as a rule with no test behind it.
    [<Fact>]
    let ``a run with no segments reports the primary face's band`` () =
        withSkia (fun () ->
            use m = new SkiaTextMeasurer(fontFiles = [ latin.Force(); cjk.Force() ])
            let blank = measure m ""

            Assert.Equal(0.0, float blank.Advance, 6)
            Assert.Equal(10.8633, float blank.Ascent, 3)
            Assert.Equal(2.543, float blank.Descent, 3))

    /// **A codepoint is a codepoint, not a UTF-16 char.**
    ///
    /// `𝄞` is U+1D11E, outside the BMP, and .NET holds it as the surrogate pair D834 DD1E. A
    /// walk over `char`s would ask each half of that pair separately, find neither in any face,
    /// and report two missing codepoints that do not exist. This is the assertion that says the
    /// walk pairs them — and it is the cheapest place in the suite to catch it, because the
    /// alternative surfaces as a garbled label in somebody's figure.
    [<Fact>]
    let ``an astral codepoint is reported as one codepoint and not as two surrogates`` () =
        withSkia (fun () ->
            use m = new SkiaTextMeasurer(fontFiles = [ latin.Force(); cjk.Force() ])

            measure m "A\U0001D11EA" |> ignore

            let face = Assert.Single m.ResolvedFaces
            Assert.Equal<int[]>([| 0x1D11E |], face.Missing))

    /// **D62's rule in the place D78 put it at risk: measuring and drawing must agree.**
    ///
    /// The measurer sums segment advances and the backend walks a pen across the same segments,
    /// and there is no compiler keeping the two folds equal. So this reads the pixels back: the
    /// ink of an `AnchorStart` run has to begin where the run was placed and end an advance
    /// later. A backend that drew the whole string in the primary face would put 42.82 px of ink
    /// where 48.82 is measured, and a pen that advanced by anything but each segment's own width
    /// would pile the segments up or spread them out.
    [<Fact>]
    let ``the ink a mixed run leaves spans the advance the measurer reported`` () =
        withSkia (fun () ->
            let options =
                { SkiaOptions.Default with
                    FontFiles = [ latin.Force(); cjk.Force() ] }

            // Placed a fifth of the way across a 400 px surface: 80 px, clear of both edges with
            // room for the run and for a wrong answer to be visible rather than clipped.
            let scene =
                Text
                    { Text = Twice
                      Tex = None
                      At = { X = 0.2<nd>; Y = 0.5<nd> }
                      Font = spec
                      Paint = Solid ink
                      Anchor = AnchorStart
                      Baseline = BaselineAlphabetic
                      Rotation = 0.0<rad> }

            let gray = SkiaPixels.decode (SkiaBackend.renderPngWith options surface scene)

            let inkedColumns =
                [| 0 .. gray.Width - 1 |]
                |> Array.filter (fun x ->
                    [| 0 .. gray.Height - 1 |]
                    |> Array.exists (fun y -> int gray.Pixels.[y * gray.Width + x] < Raster.InkThreshold))

            Assert.NotEmpty inkedColumns
            let left = float (Array.min inkedColumns)
            let right = float (Array.max inkedColumns) + 1.0

            use m = new SkiaTextMeasurer(fontFiles = [ latin.Force(); cjk.Force() ])
            let advance = float (measure m Twice).Advance

            // **Two pixels, and every failure this gate is for moves an edge much further.**
            // Ink is inset from the advance by the first glyph's left side bearing and by the
            // last one's right, so this cannot be an equality — and it does not need to be:
            // drawing the run in one face costs 6 px on a string this short, and a pen that
            // walks by the wrong segment's width costs a whole segment.
            Assert.True(
                abs (left - 80.0) <= 2.0,
                sprintf "the run was placed at x = 80 and its ink starts at %.0f" left
            )

            Assert.True(
                abs (right - left - advance) <= 2.0,
                sprintf
                    "the ink spans %.1f px and the measurer reported %.4f -- drawing and measuring disagree, which is what puts an anchored label outside its own margin"
                    (right - left)
                    advance
            ))
