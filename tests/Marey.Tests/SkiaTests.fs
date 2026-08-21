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

module private SkiaPixels =

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

let private withSkia (body: unit -> unit) =
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
    [<InlineData "all-nine-nodes">]
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

    /// Phase 3 flips this, and this assertion is the thing that will fail when it does — which
    /// is the point. Advertising shaping before the shaper is wired in would be a lie in the
    /// one place the flag exists to tell the truth.
    [<Fact>]
    let ``text shaping is not yet advertised`` () = Assert.False caps.TextShaping

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
