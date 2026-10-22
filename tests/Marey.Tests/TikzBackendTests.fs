/// The TikZ backend traversal.
///
/// The suite is organised around the four places this backend deliberately differs from the SVG
/// one, because those are where a bug would hide: the missing y flip, the pre-transformed
/// geometry, the honest capability set, and the batched marker macros.
///
/// Two gates carry most of the weight. **Cross-backend agreement** states the relationship
/// between the two backends' coordinates exactly, which is the only thing that catches a sign
/// regression in either one's independently-derived rotation and flip arithmetic. **The compile
/// gate** runs a real engine over a real figure, because the only authority on whether a `.tex`
/// file is valid is a TeX engine.
module Marey.Tests.TikzBackendTests

open Xunit
open Marey
open Marey.UnitsOfMeasure
open Marey.Tests.Generators

// ======================================================================================
// Fixtures
// ======================================================================================

/// The golden suite's surface: 320x200, deliberately not square so an anisotropic nd → device map
/// is the normal case (see Golden.fs).
let private surface: Extent<px> = { Width = 320.0<px>; Height = 200.0<px> }

let private ink = Color.rgb 0.1 0.1 0.12
let private accent = Color.rgb 0.12 0.56 1.0

let private pt (x: float) (y: float) : Vec2<nd> = { X = x * 1.0<nd>; Y = y * 1.0<nd> }

/// How many times a substring occurs. Non-overlapping, which is what every use below wants:
/// "one definition and one scope however many points" is a counting claim.
let private countOccurrences (needle: string) (haystack: string) =
    if System.String.IsNullOrEmpty needle then
        0
    else
        let mutable n = 0
        let mutable i = haystack.IndexOf(needle, System.StringComparison.Ordinal)

        while i >= 0 do
            n <- n + 1
            i <- haystack.IndexOf(needle, i + needle.Length, System.StringComparison.Ordinal)

        n

let private font size weight italic : FontSpec =
    { Family = "Helvetica"
      Size = size * 1.0<pt>
      Weight = weight
      Italic = italic }

let private textRun s x y anchor baseline rotation tex : TextRun =
    { Text = s
      Tex = tex
      At = pt x y
      Font = font 11.0 400 false
      Paint = Solid ink
      Anchor = anchor
      Baseline = baseline
      Rotation = rotation * 1.0<rad> }

let private render (node: Node) = TikzBackend.render surface node

// ======================================================================================
// Picture structure
// ======================================================================================

module ``picture structure`` =

    [<Fact>]
    let ``a picture opens and closes exactly once`` () =
        let out = render (Scene.polyline (Stroke.solid ink 1.0<px>) [| pt 0.1 0.1; pt 0.9 0.9 |])

        Assert.Equal(1, countOccurrences "\\begin{tikzpicture}" out)
        Assert.Equal(1, countOccurrences "\\end{tikzpicture}" out)
        Assert.StartsWith("\\begin{tikzpicture}", out)
        Assert.EndsWith("\\end{tikzpicture}\n", out)

    /// Note 3 in the backend header. A TikZ node's default `inner sep` is a third of an em, so
    /// without this every label sits slightly off the position layout computed for it —
    /// consistently, and invisibly.
    [<Fact>]
    let ``node padding is zeroed in the picture options`` () =
        let out = render (Text(textRun "x" 0.5 0.5 AnchorStart BaselineAlphabetic 0.0 None))

        Assert.Contains("inner sep=0bp", out)
        Assert.Contains("outer sep=0bp", out)

    /// Note 4. Without a declared bounding box the picture's size is the union of what it drew, so
    /// two figures of the same requested extent would come out at different sizes and neither
    /// would match the SVG at the same extent.
    [<Fact>]
    let ``the bounding box is the requested extent, not the drawn content`` () =
        // Content nowhere near the edges.
        let out = render (Scene.polyline (Stroke.solid ink 1.0<px>) [| pt 0.45 0.45; pt 0.55 0.55 |])

        // 320 px x 200 px at 96 dpi is 240 bp x 150 bp.
        Assert.Contains("\\useasboundingbox (0bp,0bp) rectangle (240bp,150bp);", out)

    [<Fact>]
    let ``an empty scene still produces a valid picture`` () =
        let out = render Scene.empty

        Assert.Contains("\\begin{tikzpicture}", out)
        Assert.Contains("\\useasboundingbox", out)
        Assert.Contains("\\end{tikzpicture}", out)

// ======================================================================================
// The transform, and the cross-backend statement of it
// ======================================================================================

module ``D29 there is no y flip`` =

    /// `nd` is y-up and so is TeX, so the root transform is a pure scale. The SVG backend flips.
    [<Fact>]
    let ``the root transform is a pure scale with positive determinant`` () =
        let backend = TikzBackend()
        let sink = backend :> IBackend
        sink.Begin surface

        let t = backend.RootTransform
        Assert.True(float (Affine.determinant t) > 0.0)
        Assert.Equal(0.0, float t.M12, 12)
        Assert.Equal(0.0, float t.M21, 12)
        // y grows upward: nd y = 1 maps to the top of the picture, not the bottom.
        Assert.Equal(0.0, float (Affine.apply t (pt 0.0 0.0)).Y, 9)
        Assert.Equal(150.0, float (Affine.apply t (pt 0.0 1.0)).Y, 9)

    /// The origin of `nd` is the *bottom* left in TikZ output and the *top* left in SVG output.
    /// One scene, both backends, opposite y — and the same picture on the page.
    [<Fact>]
    let ``the nd origin lands at the bottom left`` () =
        let out = render (Scene.polyline (Stroke.solid ink 1.0<px>) [| pt 0.0 0.0; pt 1.0 1.0 |])

        Assert.Contains("(0bp,0bp)", out)
        Assert.Contains("(240bp,150bp)", out)

module ``D30 geometry is pre-transformed`` =

    /// TikZ's `scale=` multiplies coordinates but leaves `line width` alone, and Scene.fs puts
    /// stroke widths in px precisely so a hairline stays a hairline. Combining the two produces
    /// widths that are right only by accident.
    [<Fact>]
    let ``no picture-level scale or shift is emitted`` () =
        let scene =
            Transform(Affine.translate { X = 0.25<nd>; Y = 0.25<nd> }, Scene.polyline (Stroke.solid ink 1.0<px>) [| pt 0.0 0.0; pt 0.5 0.5 |])

        let out = render scene

        Assert.DoesNotContain("scale=", out)
        Assert.DoesNotContain("xshift", out)
        Assert.DoesNotContain("yshift", out)

    /// A Transform node composes into the CTM and vanishes; its effect shows up in the numbers.
    [<Fact>]
    let ``a Transform node moves the emitted coordinates`` () =
        let line = Scene.polyline (Stroke.solid ink 1.0<px>) [| pt 0.0 0.0; pt 0.5 0.5 |]
        let moved = Transform(Affine.translate { X = 0.25<nd>; Y = 0.5<nd> }, line)

        // 0.25 nd of 240 bp = 60 bp; 0.5 nd of 150 bp = 75 bp.
        Assert.Contains("(60bp,75bp)", render moved)
        Assert.Contains("(0bp,0bp)", render line)

    /// A stroke width is a device length and must not be scaled by the nd → bp map. 1 px at
    /// 96 dpi is exactly 0.75 bp.
    [<Fact>]
    let ``stroke width is a device length in bp`` () =
        let out = render (Scene.polyline (Stroke.solid ink 2.0<px>) [| pt 0.1 0.1; pt 0.9 0.9 |])
        Assert.Contains("line width=1.5bp", out)

// ======================================================================================
// GATE 2 — cross-backend coordinate agreement
//
// D29 and D30 make the relationship between the two backends' output exact rather than
// approximate: for the same scene at the same extent,
//
//     x_bp = x_px * 72/96          y_bp = (H_px - y_px) * 72/96
//
// Anything that breaks a sign, a flip or a conversion in either backend breaks this.
// ======================================================================================

module ``cross-backend agreement`` =

    let private points =
        [ 0.0, 0.0; 0.25, 0.5; 0.5, 0.25; 1.0, 1.0; 0.125, 0.875 ]

    [<Fact>]
    let ``every emitted coordinate is the SVG coordinate scaled and y-reflected`` () =
        let factor = 72.0 / 96.0
        let hPx = float surface.Height

        for (nx, ny) in points do
            let scene = Scene.polyline (Stroke.solid ink 1.0<px>) [| pt nx ny; pt 0.5 0.5 |]

            // What the SVG backend produces: y measured down from the top.
            let svgX = nx * float surface.Width
            let svgY = (1.0 - ny) * hPx

            // What TikZ must therefore produce.
            let expected = Tikz.coord (svgX * factor) ((hPx - svgY) * factor)

            Assert.Contains(expected, render scene)
            // ...and the SVG really does put its own numbers where we assumed.
            Assert.Contains(Svg.num svgX + " " + Svg.num svgY, SvgBackend.render surface scene)

    /// The rotation formulas are genuinely different — the SVG backend negates a run's own angle
    /// under a mirrored CTM, this one does not, because its determinant is positive. Both
    /// must put the glyphs at the same angle on the page, which for a y-down target means the
    /// opposite sign.
    [<Fact>]
    let ``a rotated run gets opposite signs in the two backends`` () =
        let scene = Text(textRun "30" 0.5 0.5 AnchorStart BaselineAlphabetic (30.0 * System.Math.PI / 180.0) None)

        Assert.Contains("rotate=30", render scene)
        Assert.Contains("rotate(-30", SvgBackend.render surface scene)

// ======================================================================================
// Capabilities, and Degrade's first real customer
// ======================================================================================

module ``D31 capabilities are honest`` =

    let private caps = (TikzBackend() :> IBackend).Caps

    /// **`Rasters` inverted at M2.5, and it is the first capability in the project to go from
    /// `false` to `true`.** D31 set it `false` because "an honest raster needs a sidecar .png and
    /// `\\includegraphics`, which breaks the one-string-output contract", and named
    /// `renderWithAssets` as the door. D64 built the door: `RenderedDocument` carries the
    /// sidecar, so the reason expired rather than the judgement being wrong.
    ///
    /// `Gradients` did not follow it and will not. That `false` is about PGF shadings being
    /// two-stop and axis-constrained, which no output shape fixes.
    [<Fact>]
    let ``gradients are refused, rasters and clipping and dashes are not`` () =
        Assert.False caps.Gradients
        Assert.False caps.RadialGradients
        Assert.True caps.Rasters
        Assert.False caps.TextShaping
        Assert.True caps.Clipping
        Assert.True caps.Dashes
        Assert.True caps.RotatedText
        Assert.True caps.Semantics
        Assert.False caps.GroupOpacity
        Assert.True caps.MaxBatch.IsNone

    /// **the only `GroupOpacity = false` in the project**, and the reason the flag is a
    /// capability rather than a constant.
    ///
    /// The obvious implementation is `\begin{scope}[opacity=0.5]`, and it is wrong twice. pgf
    /// inherits `opacity` into each path rather than compositing the scope, so it is the
    /// *approximation* wearing the capability's name; and a path's own `fill opacity=` — which
    /// this backend emits for every colour carrying alpha — **overrides** the inherited value,
    /// so a half-transparent series inside a fading group would come out at its original alpha.
    /// The pgf feature that does composite is `transparency group`, which the manual gates on
    /// driver support, and this backend hands a `string` to an unknown preamble and driver.
    [<Fact>]
    let ``a group opacity is folded into paint alpha rather than emitted as a scope`` () =
        let group =
            Scene.opacity
                0.5
                (Group
                    [| Scene.rect None (Some(Solid accent)) { X0 = 0.1<nd>; Y0 = 0.1<nd>; X1 = 0.6<nd>; Y1 = 0.9<nd> }
                       Scene.rect None (Some(Solid ink)) { X0 = 0.4<nd>; Y0 = 0.1<nd>; X1 = 0.9<nd>; Y1 = 0.9<nd> } |])

        let out = render group

        Assert.Equal(2, countOccurrences "fill opacity=0.5" out)

        // No scope at all: the scene has no clip, so a `\begin{scope}` here could only be an
        // attempt to carry the opacity as a scope option, which is the thing this is not.
        Assert.Equal(0, countOccurrences "\\begin{scope}" out)
        Assert.DoesNotContain("transparency group", out)

    /// The same fold, reached the other way. `render` degrades before it draws, so the
    /// backend's own `Opacity` branch is only met by a caller driving `IBackend` by hand — the
    /// case `SkiaBackend`'s `Annotated` comment calls defensive rather than dead. It performs
    /// the identical fold, and says so in a comment, because a reviewer of a `.tex` file should
    /// be able to see that a group opacity was here.
    [<Fact>]
    let ``driving IBackend by hand with an undegraded scene folds it too`` () =
        let backend = TikzBackend()
        let b = backend :> IBackend

        b.Begin surface

        b.Draw(
            Scene.opacity 0.5 (Scene.rect None (Some(Solid accent)) { X0 = 0.1<nd>; Y0 = 0.1<nd>; X1 = 0.9<nd>; Y1 = 0.9<nd> })
        )

        b.Finish()

        Assert.Contains("marey-opacity: 0.5 -- folded into paint alpha", backend.Result)
        Assert.Contains("fill opacity=0.5", backend.Result)

    /// D10 built `Degrade.forCaps` and nothing needed it until now. A gradient must arrive as the
    /// solid colour at its midpoint rather than as a missing fill or a crash.
    [<Fact>]
    let ``a gradient degrades to a solid midpoint fill`` () =
        let gradient =
            LinearGradient(
                pt 0.0 0.0,
                pt 1.0 0.0,
                [| { Offset = 0.0; Color = Color.black }
                   { Offset = 1.0; Color = Color.white } |]
            )

        let out = render (Scene.rect None (Some gradient) { X0 = 0.1<nd>; Y0 = 0.1<nd>; X1 = 0.9<nd>; Y1 = 0.9<nd> })

        Assert.Contains("\\definecolor", out)
        Assert.Contains("fill=", out)
        // No shading machinery, and no gradient left to render.
        Assert.DoesNotContain("shade", out)

        // The midpoint of black → white in Oklab is a mid grey, so all three components agree
        // and none is 0 or 1.
        let grey = Interp.stops [| 0.0, Color.black; 1.0, Color.white |] 0.5
        Assert.Contains(Tikz.rgbTriple grey, out)

    /// `render` degrades internally, because `Chart.render`/`Chart.toFile` do not. Without it the
    /// renderer-function-shaped path would hand an un-degraded scene to a backend that advertises
    /// it cannot cope.
    [<Fact>]
    let ``the renderer-function path degrades without help from Chart`` () =
        let raster =
            Raster
                { Image = { Width = 2; Height = 2; Rgba = Array.zeroCreate 16 }
                  Bounds = { X0 = 0.0<nd>; Y0 = 0.0<nd>; X1 = 1.0<nd>; Y1 = 1.0<nd> }
                  Smooth = false }

        // D64. The backend advertises `Rasters = true`, but `render` promises a bare `string`
        // and a string has nowhere to put a sidecar — so it degrades against its own reduced
        // capabilities and the node goes away. Emitting the \includegraphics and dropping the bytes
        // would produce a `.tex` that fails to compile with a missing-file error, which is the
        // same class of wrong output as the gradient above, arriving in somebody else's build.
        let out = render raster
        Assert.DoesNotContain("includegraphics", out)
        Assert.Contains("\\end{tikzpicture}", out)

        // The same scene through the asset-shaped door: drawn, and the bytes come back.
        let rendered = TikzBackend.renderWithAssets surface raster
        Assert.Contains("includegraphics", rendered.Document)
        Assert.Single rendered.Assets |> ignore

    /// Driving `IBackend` by hand skips degradation, so the backend sees the raster it advertises
    /// it can draw — and D64 is what changed underneath this test. It read
    /// `a raster reaching the backend undegraded leaves a visible comment` until M2.5.
    [<Fact>]
    let ``a raster reaching the backend is drawn, with its bytes on the side`` () =
        let backend = TikzBackend()
        let sink = backend :> IBackend
        sink.Begin surface

        sink.Draw(
            Raster
                { Image = { Width = 4; Height = 3; Rgba = Array.zeroCreate 48 }
                  Bounds = { X0 = 0.0<nd>; Y0 = 0.0<nd>; X1 = 1.0<nd>; Y1 = 1.0<nd> }
                  Smooth = false }
        )

        sink.Finish()

        // This assertion used to read `Assert.Contains("% marey: raster 4x3 skipped", ...)`,
        // and the inversion is the point of D64: the backend can now draw one.
        Assert.Contains("\\includegraphics", backend.Result)
        Assert.DoesNotContain("skipped", backend.Result)

        // The half that would be easy to omit, and useless output if omitted: the document names
        // a file, so a file has to exist. A `.tex` referring to a `.png` nobody wrote is a
        // missing-file error in somebody else's build.
        let asset = Assert.Single backend.Assets
        Assert.Contains(asset.Name, backend.Result)
        Assert.NotEmpty asset.Bytes
        // PNG's signature, so this is an image rather than a plausible-looking blob.
        Assert.Equal<byte[]>([| 0x89uy; 0x50uy; 0x4Euy; 0x47uy |], asset.Bytes.[0..3])

// ======================================================================================
// Batched markers
// ======================================================================================

module ``D33 marker batching`` =

    let private batch shape n : Node =
        Markers
            { Points = Array.init n (fun i -> pt (float i / float n) 0.5)
              Shape = shape
              Size = 4.0<px>
              Fill = Some(Solid accent)
              Stroke = None }

    /// §5.1's promise, kept in a text format: one definition and one scope however many points.
    [<Fact>]
    let ``a thousand points produce one macro and one scope`` () =
        let out = render (batch MarkCircle 1000)

        Assert.Equal(1, countOccurrences "\\def\\mareymark" out)
        Assert.Equal(1, countOccurrences "\\begin{scope}" out)
        Assert.Equal(1000, countOccurrences "\\mareymarkA{" out)

    /// `\providecommand` does not redefine, so two pictures in one document whose first batches
    /// had different sizes would silently share the first one's geometry. `\def` always defines,
    /// and `tikzpicture` is a TeX group, so it cannot leak.
    [<Fact>]
    let ``marker macros use def, not providecommand`` () =
        let out = render (batch MarkSquare 3)

        Assert.Contains("\\def\\mareymark", out)
        Assert.DoesNotContain("providecommand", out)

    [<Fact>]
    let ``identical batches share one definition and different sizes do not`` () =
        let small =
            Markers
                { Points = [| pt 0.2 0.5 |]
                  Shape = MarkCircle
                  Size = 4.0<px>
                  Fill = Some(Solid accent)
                  Stroke = None }

        let large =
            Markers
                { Points = [| pt 0.8 0.5 |]
                  Shape = MarkCircle
                  Size = 9.0<px>
                  Fill = Some(Solid accent)
                  Stroke = None }

        Assert.Equal(1, countOccurrences "\\def\\mareymark" (render (Group [| small; small |])))
        Assert.Equal(2, countOccurrences "\\def\\mareymark" (render (Group [| small; large |])))

    /// Macro names go into TeX command names, where digits are not letters.
    [<Fact>]
    let ``every generated macro name is letters-only`` () =
        let shapes =
            [| MarkCircle; MarkSquare; MarkDiamond; MarkTriangleUp; MarkTriangleDown; MarkCross; MarkPlus |]

        let scene =
            Group(shapes |> Array.mapi (fun i s -> batch s (i + 1)))

        let out = render scene

        for line in out.Split '\n' do
            if line.Contains "\\def\\" then
                let after = line.Substring(line.IndexOf "\\def\\" + 5)
                let name = after.Substring(0, after.IndexOf '#')
                Assert.True(name |> Seq.forall System.Char.IsLetter, "macro name not letters-only: " + name)

    /// This space is y-up, so "triangle up" has its apex at *positive* y — the opposite sign from
    /// the SVG backend's defs, and the same picture on the page.
    [<Fact>]
    let ``triangle up points up in a y-up space`` () =
        let out = render (batch MarkTriangleUp 1)
        // Size 4 px = 3 bp, so r = 1.5 bp: the apex is at +1.5 bp and the base at -0.75 bp.
        Assert.Contains("(0bp,1.5bp)", out)
        Assert.Contains("-0.75bp", out)

    // ------------------------------------------------------------------------------
    // The paint-action rule, and why it has its own block.
    //
    // In TikZ, `fill=<colour>` on a *scope* sets the fill colour but does **not** make paths
    // inside it filled. A bare `\path` inside `\begin{scope}[fill=orange]` compiles perfectly and
    // draws nothing at all. The first version of this backend mirrored SVG's `<g>`/`<use>` split
    // exactly — paint on the group, geometry in the definition — and produced a figure whose
    // scatter markers and legend swatch were simply absent.
    //
    // **Every test in this file passed while that was broken**, and so did the TeX compile gate:
    // the document was valid, it just had no ink in it. Structure tests cannot see paint and
    // neither can a compiler. It was found by rendering to PDF and looking at the picture.
    // ------------------------------------------------------------------------------

    let private painted shape fill stroke : Node =
        Markers
            { Points = [| pt 0.5 0.5 |]
              Shape = shape
              Size = 6.0<px>
              Fill = fill
              Stroke = stroke }

    [<Fact>]
    let ``a fill-only batch carries the bare fill action on the path`` () =
        let out = render (painted MarkCircle (Some(Solid accent)) None)
        Assert.Contains("\\path[fill,shift={(#1,#2)}]", out)

    [<Fact>]
    let ``a stroke-only batch carries the bare draw action`` () =
        let out = render (painted MarkCircle None (Some(Stroke.solid ink 0.5<px>)))
        Assert.Contains("\\path[draw,shift={(#1,#2)}]", out)

    [<Fact>]
    let ``a filled and stroked batch carries both actions`` () =
        let out = render (painted MarkCircle (Some(Solid accent)) (Some(Stroke.solid ink 0.5<px>)))
        Assert.Contains("\\path[fill,draw,shift={(#1,#2)}]", out)

    /// The scope still supplies the *values*. Both halves are needed: an action with no colour
    /// paints in black, a colour with no action paints nothing.
    [<Fact>]
    let ``the scope supplies the paint values the macro's actions consume`` () =
        let out = render (painted MarkCircle (Some(Solid accent)) (Some(Stroke.solid ink 1.0<px>)))

        Assert.Contains("\\begin{scope}[fill=", out)
        Assert.Contains("draw=", out)
        // 1 px at 96 dpi is exactly 0.75 bp — a value that survives three-decimal rounding, so the
        // assertion is about the conversion rather than about the formatter.
        Assert.Contains("line width=0.75bp", out)

    /// The guard against the whole bug class: if a batch asks for paint, its macro must contain a
    /// paint action. Checked across every shape and every paint combination.
    [<Fact>]
    let ``no painted batch ever produces an unpainted macro`` () =
        let shapes =
            [ MarkCircle; MarkSquare; MarkDiamond; MarkTriangleUp; MarkTriangleDown; MarkCross; MarkPlus ]

        let paints =
            [ Some(Solid accent), None
              None, Some(Stroke.solid ink 0.5<px>)
              Some(Solid accent), Some(Stroke.solid ink 0.5<px>) ]

        for shape in shapes do
            for (fill, stroke) in paints do
                let out = render (painted shape fill stroke)

                let def =
                    out.Split '\n'
                    |> Array.tryFind (fun l -> l.Contains "\\def\\mareymark")

                match def with
                | None -> failwithf "no marker macro emitted for %A" shape
                | Some line ->
                    Assert.True(
                        line.Contains "[fill" || line.Contains "[draw" || line.Contains ",draw",
                        sprintf "marker macro for %A has no paint action: %s" shape line
                    )

    /// A batch with neither fill nor stroke paints nothing, which is what the SVG backend does for
    /// `fill="none"` with no stroke. Asserted so the case reads as a decision.
    [<Fact>]
    let ``an unpainted batch emits a macro with no paint action`` () =
        let out = render (painted MarkCircle None None)
        Assert.Contains("\\path[shift={(#1,#2)}]", out)

    /// Paint actions are part of a macro's identity, so two batches of the same shape and size
    /// that paint differently must not share one.
    [<Fact>]
    let ``batches differing only in paint get separate macros`` () =
        let filled = painted MarkCircle (Some(Solid accent)) None
        let stroked = painted MarkCircle None (Some(Stroke.solid ink 0.5<px>))

        Assert.Equal(2, countOccurrences "\\def\\mareymark" (render (Group [| filled; stroked |])))

// ======================================================================================
// Text
// ======================================================================================

module ``text`` =

    [<Theory>]
    [<InlineData(0, 0, "base west")>]
    [<InlineData(1, 0, "base")>]
    [<InlineData(2, 0, "base east")>]
    [<InlineData(0, 1, "west")>]
    [<InlineData(1, 1, "center")>]
    [<InlineData(2, 1, "east")>]
    [<InlineData(0, 2, "north west")>]
    [<InlineData(1, 2, "north")>]
    [<InlineData(2, 2, "north east")>]
    let ``every anchor and baseline pair maps to a TikZ anchor`` (anchorIdx: int) (baselineIdx: int) (expected: string) =
        let anchor =
            [| AnchorStart; AnchorMiddle; AnchorEnd |].[anchorIdx]

        let baseline =
            [| BaselineAlphabetic; BaselineMiddle; BaselineHanging |].[baselineIdx]

        let out = render (Text(textRun "x" 0.5 0.5 anchor baseline 0.0 None))
        // The trailing comma matters: `base` is a prefix of `base west`, so a bare
        // `Contains "anchor=base"` would pass for the wrong anchor. `text=` always follows.
        Assert.Contains("anchor=" + expected + ",", out)

    /// D32's payoff: siunitx markup reaches the document verbatim while literal text is escaped.
    [<Fact>]
    let ``TeX source passes through and plain text is escaped`` () =
        let withTex =
            render (Text(textRun "a [m·s⁻²]" 0.5 0.5 AnchorMiddle BaselineAlphabetic 0.0 (Some "$a$ [\\qty{}{\\metre\\per\\second\\squared}]")))

        Assert.Contains("{$a$ [\\qty{}{\\metre\\per\\second\\squared}]}", withTex)

        let plain = render (Text(textRun "50% of x_1" 0.5 0.5 AnchorMiddle BaselineAlphabetic 0.0 None))
        Assert.Contains("{50\\% of x\\_1}", plain)

    [<Fact>]
    let ``an empty run in both forms emits nothing`` () =
        let out = render (Text(textRun "" 0.5 0.5 AnchorStart BaselineAlphabetic 0.0 None))
        Assert.DoesNotContain("\\node", out)

    /// D35's split: the size layout assumed is the size TeX uses, and the family comes from the
    /// document.
    [<Fact>]
    let ``PinSize emits the size layout measured against`` () =
        let run =
            { textRun "x" 0.5 0.5 AnchorStart BaselineAlphabetic 0.0 None with
                Font = { Family = ""; Size = 9.6<pt>; Weight = 400; Italic = false } }

        Assert.Contains("\\fontsize{9.6}{11.52}\\selectfont", render (Text run))

    /// **No family is ever emitted, whatever the theme names.** This is the whole point of the
    /// backend, so it is asserted against a family that *would* map to a TeX command: emitting
    /// `\rmfamily` because a theme happens to name a serif stack would override the host document,
    /// and a figure dropped into a sans-serif paper would come out in the wrong face.
    ///
    /// The family is still load-bearing — `ApproxTextMeasurer` reads it to decide how much room a
    /// label needs — it just never reaches TeX.
    [<Fact>]
    let ``no font family reaches the document, whatever the theme names`` () =
        for family in [ ""; "serif"; "Times New Roman, serif"; "Helvetica, sans-serif"; "Consolas, mono" ] do
            let run =
                { textRun "x" 0.5 0.5 AnchorStart BaselineAlphabetic 0.0 None with
                    Font = { Family = family; Size = 9.0<pt>; Weight = 400; Italic = false } }

            let out = render (Text run)

            for cmd in [ "rmfamily"; "sffamily"; "ttfamily"; "fontfamily" ] do
                Assert.DoesNotContain(cmd, out)

    [<Fact>]
    let ``InheritFont emits no font information at all`` () =
        let out =
            TikzBackend.renderWith
                { TikzOptions.Default with FontMode = InheritFont }
                surface
                (Text(textRun "x" 0.5 0.5 AnchorStart BaselineAlphabetic 0.0 None))

        Assert.DoesNotContain("fontsize", out)
        Assert.DoesNotContain("font=", out)

    [<Fact>]
    let ``weight and italic become series and shape`` () =
        let bold =
            { textRun "x" 0.5 0.5 AnchorStart BaselineAlphabetic 0.0 None with
                Font = font 10.0 700 true }

        let out = render (Text bold)
        Assert.Contains("\\bfseries", out)
        Assert.Contains("\\itshape", out)

// ======================================================================================
// Semantics and containers
// ======================================================================================

module ``containers`` =

    [<Fact>]
    let ``a Group emits no container of its own`` () =
        let line = Scene.polyline (Stroke.solid ink 1.0<px>) [| pt 0.1 0.1; pt 0.9 0.9 |]
        let bare = render line
        let grouped = render (Group [| Group [| line |] |])

        Assert.Equal(bare, grouped)

    [<Fact>]
    let ``a Clip becomes a scope`` () =
        let scene =
            Clip(
                [| MoveTo(pt 0.0 0.0); LineTo(pt 0.5 0.0); LineTo(pt 0.5 0.5); ClosePath |],
                Scene.polyline (Stroke.solid ink 1.0<px>) [| pt 0.0 0.0; pt 1.0 1.0 |]
            )

        let out = render scene
        Assert.Equal(1, countOccurrences "\\begin{scope}" out)
        Assert.Equal(1, countOccurrences "\\clip " out)
        Assert.Contains("-- cycle", out)

    [<Fact>]
    let ``Keyed and Annotated become comments`` () =
        let scene =
            Keyed(
                Key "panel-0",
                Annotated(
                    { Role = RoleAxis
                      Label = "x axis"
                      Data = [| "side", "bottom" |] },
                    Scene.polyline (Stroke.solid ink 1.0<px>) [| pt 0.1 0.1; pt 0.9 0.1 |]
                )
            )

        let out = render scene
        Assert.Contains("% marey-key: panel-0", out)
        Assert.Contains("% marey-role: axis -- x axis", out)
        Assert.Contains("% marey-data: side = bottom", out)

    [<Fact>]
    let ``comments can be switched off`` () =
        let scene = Keyed(Key "k", Scene.polyline (Stroke.solid ink 1.0<px>) [| pt 0.1 0.1; pt 0.9 0.1 |])

        let out =
            TikzBackend.renderWith { TikzOptions.Default with Comments = false } surface scene

        Assert.DoesNotContain("marey-key", out)

// ======================================================================================
// Determinism — the same contract the SVG backend has
// ======================================================================================

module ``determinism`` =

    let private busy =
        Group
            [| Scene.polyline (Stroke.dashed ink 1.0<px> [| 3.0<px>; 2.0<px> |]) [| pt 0.1 0.1; pt 0.9 0.9 |]
               Markers
                   { Points = Array.init 20 (fun i -> pt (float i / 20.0) 0.5)
                     Shape = MarkDiamond
                     Size = 5.0<px>
                     Fill = Some(Solid accent)
                     Stroke = Some(Stroke.solid ink 0.5<px>) }
               Text(textRun "kg·m²·s⁻²" 0.5 0.2 AnchorMiddle BaselineHanging 0.0 None) |]

    [<Fact>]
    let ``two renders are byte-identical`` () =
        Assert.Equal(render busy, render busy)

    [<Fact>]
    let ``rendering is culture-invariant`` () =
        let baseline = render busy

        Assert.Equal(baseline, Culture.run Culture.CommaDecimal (fun () -> render busy))
        Assert.Equal(baseline, Culture.run Culture.DotlessI (fun () -> render busy))

    [<Fact>]
    let ``one backend instance can render two scenes`` () =
        let backend = TikzBackend()
        let sink = backend :> IBackend

        sink.Begin surface
        sink.Draw busy
        sink.Finish()
        let first = backend.Result

        sink.Begin surface
        sink.Draw busy
        sink.Finish()

        Assert.Equal(first, backend.Result)

    [<Fact>]
    let ``Draw before Begin is refused`` () =
        let sink = TikzBackend() :> IBackend
        Assert.Throws<System.InvalidOperationException>(fun () -> sink.Draw Scene.empty) |> ignore

// ======================================================================================
// Theme.tikz, and the headline in one selection
//
// §14 announces M1 on "unit-safe plots that drop into a LaTeX document". The measure of whether
// that landed is how much a user has to configure, and the answer has to be "the theme".
// ======================================================================================

module ``D35 Theme tikz`` =

    let private ts = Array.init 12 (fun i -> float i * 0.5<s>)
    let private xs = ts |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)

    let private plain = Chart.lineIn U.second U.metre ts xs

    /// **The headline.** No `withUnitStyle`, no axis configuration: selecting the theme is enough
    /// to get siunitx unit macros, derived from the two unit witnesses.
    [<Fact>]
    let ``selecting the theme is the only configuration siunitx needs`` () =
        let tex =
            plain |> Chart.withTheme Theme.tikz |> Chart.render TikzBackend.render (Chart.size 360.0 220.0)

        Assert.Contains("\\qty{}{\\second}", tex)
        Assert.Contains("\\qty{}{\\metre}", tex)

    /// The same figure under a screen theme must *not* carry markup — it would be drawn as literal
    /// backslashes by an SVG renderer.
    [<Fact>]
    let ``a screen theme leaves the units as plain text`` () =
        let svg =
            plain
            |> Chart.withTheme Theme.publication
            |> Chart.render SvgBackend.render (Chart.size 360.0 220.0)

        Assert.DoesNotContain("qty", svg)
        Assert.Contains(">t [s]<", svg)
        Assert.Contains(">x [m]<", svg)

    /// Resolution happens at compile time, not at construction, so re-theming a finished figure
    /// re-spells its axis titles. Anything else would make `Chart.withTheme` a half-measure.
    [<Fact>]
    let ``re-theming a finished figure changes its unit spelling`` () =
        let size = Chart.size 360.0 220.0
        let asTikz = plain |> Chart.withTheme Theme.tikz |> Chart.render TikzBackend.render size

        let backToScreen =
            plain
            |> Chart.withTheme Theme.tikz
            |> Chart.withTheme Theme.publication
            |> Chart.render TikzBackend.render size

        Assert.Contains("qty", asTikz)
        Assert.DoesNotContain("qty", backToScreen)

    /// A per-axis `Style` is an override, so it must beat the theme in both directions.
    [<Fact>]
    let ``an explicit axis style overrides the theme`` () =
        let size = Chart.size 360.0 220.0

        // Screen theme, explicitly asking for siunitx.
        let forced =
            plain
            |> Chart.withTheme Theme.publication
            |> Chart.withUnitStyle Siunitx
            |> Chart.render TikzBackend.render size

        // tikz theme, explicitly asking for plain text.
        let suppressed =
            plain
            |> Chart.withTheme Theme.tikz
            |> Chart.withUnitStyle Unicode
            |> Chart.render TikzBackend.render size

        Assert.Contains("qty", forced)
        Assert.DoesNotContain("qty", suppressed)

    /// The default must stay `Unicode` for every screen theme, or an SVG user gets markup.
    [<Fact>]
    let ``only the tikz theme defaults to a TeX unit style`` () =
        for t in Theme.all do
            if t.Name = "tikz" then
                Assert.Equal(Siunitx, t.UnitStyle)
            else
                Assert.Equal(Unicode, t.UnitStyle)

    [<Fact>]
    let ``the theme is registered and findable by name`` () =
        Assert.True(Theme.tryByName "tikz" |> Option.isSome)
        Assert.Equal("tikz", Theme.tikz.Name)

    /// A theme is a record of resolved values with no function fields, so an identically
    /// constructed one compares equal. Adding a field must not break that.
    [<Fact>]
    let ``the theme is still a comparable value`` () =
        Assert.Equal(Theme.tikz, { Theme.tikz with Name = "tikz" })

    /// It has to pass the same contrast and palette checks as every other theme.
    [<Fact>]
    let ``the theme validates`` () =
        let report = Theme.validate Theme.tikz
        Assert.True(report.Ok, sprintf "Theme.tikz failed validation: %A" report)

    /// The family is a measurement input for this backend, so it has to be reachable — a
    /// `Theme.tikz` figure in a sans document reserves ~8% too little room for its labels
    /// otherwise. Verified on a real compiled figure, which is what prompted the combinator.
    [<Fact>]
    let ``the font family is adjustable and changes only measurement`` () =
        let sans = Theme.tikz |> Theme.withFontFamily Theme.Sans

        Assert.Equal(Theme.Sans, sans.TickFont.Family)
        Assert.Equal(Theme.Sans, sans.AxisTitleFont.Family)
        // Still a TeX theme; only the metrics assumption moved.
        Assert.Equal(Siunitx, sans.UnitStyle)

        let widerLabels =
            let m = TextMeasure.approx
            let serif = m.Measure Theme.tikz.TickFont "123.4"
            let helvetica = m.Measure sans.TickFont "123.4"
            float helvetica.Advance > float serif.Advance

        Assert.True(widerLabels, "a sans family must measure wider than a serif one, or the knob does nothing")

        // ...and it still never reaches the document.
        let tex =
            plain |> Chart.withTheme sans |> Chart.render TikzBackend.render (Chart.size 360.0 220.0)

        Assert.DoesNotContain("sffamily", tex)

    [<Fact>]
    let ``the engine accepts a Theme tikz figure`` () =
        let tex =
            plain
            |> Chart.withTitle "Free fall"
            |> Chart.withTheme Theme.tikz
            |> Chart.render TikzBackend.render (Chart.size 360.0 220.0)

        if not (TexToolchain.accepts "theme-tikz" (TikzBackend.standalone tex)) then
            System.Console.WriteLine("SKIPPED: " + TexToolchain.report ())

// ======================================================================================
// THE GATE: a real engine reads a real figure
// ======================================================================================

module ``figures compile`` =

    /// Free fall, with a siunitx axis — the M1 headline, end to end.
    let private freeFall =
        let ts = Array.init 40 (fun i -> float i * 0.25<s>)
        let xs = ts |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)

        Chart.lineIn U.second U.metre ts xs
        |> Chart.withName "model"
        |> Chart.addScatterIn U.second U.metre ts.[..8] xs.[..8]
        |> Chart.withName "measured"
        |> Chart.withTitle "Free fall"
        |> Chart.withLegend LegendTopLeft
        |> Chart.withUnitStyle Siunitx

    let private figureTex =
        lazy (Chart.render TikzBackend.render (Chart.size 400.0 250.0) freeFall)

    /// Gate 3 from the plan, and the sentence the milestone is announced on: the axis title is
    /// siunitx markup, derived from the unit witness with nothing configured.
    [<Fact>]
    let ``a siunitx figure carries unit macros into the tex`` () =
        let tex = figureTex.Force()

        Assert.Contains("\\qty{}{\\second}", tex)
        Assert.Contains("\\qty{}{\\metre}", tex)
        // ...and the plain fallback did not leak markup into a non-TeX form.
        Assert.DoesNotContain("textbackslash", tex)

    [<Fact>]
    let ``a real figure is one picture with a declared bounding box`` () =
        let tex = figureTex.Force()

        Assert.Equal(1, countOccurrences "\\begin{tikzpicture}" tex)
        Assert.Contains("\\useasboundingbox (0bp,0bp) rectangle (300bp,187.5bp);", tex)

    [<Fact>]
    let ``the engine accepts a real figure`` () =
        if not (TexToolchain.accepts "figure" (TikzBackend.standalone (figureTex.Force()))) then
            System.Console.WriteLine("SKIPPED: " + TexToolchain.report ())

    /// Every IR case in one document, through a real engine. The scene is the same shape as the
    /// SVG suite's nine-case golden.
    [<Fact>]
    let ``the engine accepts a scene using every node case`` () =
        let scene =
            Keyed(
                Key "all",
                Annotated(
                    { Role = RoleSeries; Label = "everything"; Data = [| "n", "9" |] },
                    Group
                        [| Scene.rect (Some(Stroke.solid ink 0.5<px>)) (Some(Solid(Color.rgba 0.9 0.9 0.95 1.0))) { X0 = 0.02<nd>; Y0 = 0.02<nd>; X1 = 0.98<nd>; Y1 = 0.98<nd> }
                           Path(
                               [| MoveTo(pt 0.1 0.2)
                                  CubicTo(pt 0.3 0.9, pt 0.6 0.0, pt 0.9 0.6)
                                  ClosePath |],
                               Some(Stroke.dashed accent 1.25<px> [| 4.0<px>; 2.0<px> |]),
                               None,
                               EvenOdd
                           )
                           Transform(
                               Affine.translate { X = 0.0<nd>; Y = 0.05<nd> },
                               Clip(
                                   [| MoveTo(pt 0.05 0.05); LineTo(pt 0.95 0.05); LineTo(pt 0.95 0.7); LineTo(pt 0.05 0.7); ClosePath |],
                                   Markers
                                       { Points = Array.init 30 (fun i -> pt (0.08 + float i * 0.03) 0.35)
                                         Shape = MarkCross
                                         Size = 4.0<px>
                                         Fill = None
                                         Stroke = Some(Stroke.solid accent 0.6<px>) }
                               )
                           )
                           Text(textRun "kg·m²·s⁻² Ω µm Å °C" 0.5 0.9 AnchorMiddle BaselineAlphabetic 0.0 None)
                           Text(textRun "rotated" 0.9 0.5 AnchorEnd BaselineMiddle (System.Math.PI / 2.0) None) |]
                )
            )

        if not (TexToolchain.accepts "nine-cases" (TikzBackend.renderStandalone surface scene)) then
            System.Console.WriteLine("SKIPPED: " + TexToolchain.report ())

    /// D36 in situ. An out-of-domain datum projects far outside the panel, is removed by the
    /// panel clip, and — before the clamp — took the whole document down with `Dimension too
    /// large`. This is the regression test for that.
    [<Fact>]
    let ``the engine accepts a figure with out-of-domain data`` () =
        let ts = Array.init 20 (fun i -> float i * 1.0<s>)
        let xs = ts |> Array.map (fun t -> float t * 1.0<m>)

        let fig =
            Chart.lineIn U.second U.metre ts xs
            // A domain far narrower than the data, so most points project outside [0,1] by a
            // large factor.
            |> Chart.withXDomain 0.0 0.001

        let tex = Chart.render TikzBackend.render (Chart.size 300.0 200.0) fig

        // The clamp fired rather than an astronomical number reaching the file.
        Assert.DoesNotContain("e+", tex)

        if not (TexToolchain.accepts "out-of-domain" (TikzBackend.standalone tex)) then
            System.Console.WriteLine("SKIPPED: " + TexToolchain.report ())
