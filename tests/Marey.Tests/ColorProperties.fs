/// §7.1 / §9 — colour.
///
/// **decisions.md F1 governs every tolerance in this file.** The sRGB → Oklab → sRGB round trip
/// has an irreducible error floor of ~1e-6: Ottosson's forward and inverse LMS matrices are
/// published to ten decimal places so their product misses the identity by ~2.4e-10, the inverse
/// rows nearly cancel for neutrals (4.077 − 3.308 + 0.231 = 1), and `ofLinear`'s curve has a
/// slope of ~1500 near black, which amplifies the residual. Measured here at 1.65e-6 over
/// 200 000 random colours including alpha.
///
/// That is 4000× finer than the 8-bit display quantum of 1/255, so it is invisible — and F1 is
/// blunt about the consequence: **"a colour test demanding better than 1e-5 will flap forever."**
/// `Tol.colour` is 1e-5 and must not be tightened.
module Marey.Tests.ColorProperties

open FsCheck.Xunit
open Xunit
open Marey
open Marey.Tests.Generators

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 800, Replay = Seed, QuietOnSuccess = true)>]
module Oklab =

    [<Property>]
    let ``sRGB round-trips through Oklab`` (Colour c) =
        Approx.colour (Color.ofOklab (Color.toOklab c)) c

    [<Property>]
    let ``sRGB round-trips through Oklch`` (Colour c) =
        Approx.colour (Color.ofOklch (Color.toOklch c)) c

    [<Property>]
    let ``Oklch is the polar form of Oklab`` (Colour c) =
        let lab = Color.toOklab c
        let lch = Color.toOklch c

        Approx.abs' 1e-12 lch.L lab.L
        && Approx.abs' 1e-12 lch.C (sqrt (lab.A * lab.A + lab.B * lab.B))
        && Approx.abs' 1e-12 (lch.C * cos (float lch.H)) lab.A
        && Approx.abs' 1e-12 (lch.C * sin (float lch.H)) lab.B

    [<Property>]
    let ``alpha passes through unchanged`` (Colour c) =
        (Color.toOklab c).Alpha = c.A && (Color.toOklch c).Alpha = c.A

    /// Lightness is monotone in the grey ramp. This is the property that makes Oklab worth
    /// having: a sequential palette built by walking `L` must not double back.
    [<Property>]
    let ``L is monotone along the grey ramp`` (UnitT t) (UnitT u) =
        let lightness v = (Color.toOklab (Color.rgb v v v)).L
        let lo, hi = min t u, max t u
        lightness lo <= lightness hi + 1e-12

    [<Fact>]
    let ``black and white sit at the ends of L`` () =
        Assert.Equal(0.0, (Color.toOklab Color.black).L, 9)
        Assert.Equal(1.0, (Color.toOklab Color.white).L, 6)

    /// F1's own observation, recorded so the number does not drift: sRGB white does not land
    /// exactly on `(L=1, a=0, b=0)`. Its Oklab `b` is ~3.7e-8. A test asserting exact neutrality
    /// would fail; a test asserting neutrality *to display precision* is the true statement.
    [<Fact>]
    let ``white is neutral to display precision but not exactly`` () =
        let w = Color.toOklab Color.white
        Assert.True(abs w.B > 0.0, "F1: sRGB white is very slightly non-neutral in Oklab")
        Assert.True(abs w.B < 1e-6)
        Assert.True(abs w.A < 1e-6)

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 800, Replay = Seed, QuietOnSuccess = true)>]
module Mixing =

    [<Property>]
    let ``mix hits its endpoints`` (Colour a) (Colour b) =
        Approx.colour (Color.mix 0.0 a b) a && Approx.colour (Color.mix 1.0 a b) b

    [<Property>]
    let ``mix always lands in gamut`` (Colour a) (Colour b) (UnitT t) =
        let c = Color.mix t a b
        let ok (x: float) = x >= 0.0 && x <= 1.0
        ok c.R && ok c.G && ok c.B && ok c.A

    [<Property>]
    let ``mix is symmetric under reversal`` (Colour a) (Colour b) (UnitT t) =
        Approx.colour (Color.mix t a b) (Color.mix (1.0 - t) b a)

    [<Property>]
    let ``alpha mixes linearly`` (Colour a) (Colour b) (UnitT t) =
        Approx.abs' 1e-9 (Color.mix t a b).A (a.A + (b.A - a.A) * t)

    /// §7.1's justification for going through Oklab at all: "an sRGB lerp between two saturated
    /// colours passes through a desaturated grey; a perceptual one does not."
    ///
    /// Asserted by comparing chroma at the midpoint. Blue to yellow is the classic case: the
    /// naive sRGB midpoint is a muddy grey with chroma near zero.
    [<Fact>]
    let ``mixing blue and yellow does not pass through grey`` () =
        let blue = Color.rgb 0.0 0.0 1.0
        let yellow = Color.rgb 1.0 1.0 0.0
        let mid = Color.mix 0.5 blue yellow

        let naive =
            Color.rgb ((0.0 + 1.0) / 2.0) ((0.0 + 1.0) / 2.0) ((1.0 + 0.0) / 2.0)

        let chroma (c: Color) = (Color.toOklch c).C
        Assert.True(chroma naive < 0.02, "the sRGB midpoint really is grey")
        Assert.True(chroma mid > chroma naive * 2.0, "the Oklab midpoint keeps its chroma")

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 800, Replay = Seed, QuietOnSuccess = true)>]
module Contrast =

    [<Property>]
    let ``contrastRatio is symmetric`` (Colour a) (Colour b) =
        Color.contrastRatio a b = Color.contrastRatio b a

    /// §9 asks for "a validator that checks contrast and CVD-safety"; the WCAG ratio is bounded
    /// by construction, and the bound is exactly attained by black against white.
    [<Property>]
    let ``contrastRatio lies in [1, 21]`` (Colour a) (Colour b) =
        let r = Color.contrastRatio a b
        r >= 1.0 - 1e-12 && r <= 21.0 + 1e-9

    [<Property>]
    let ``a colour has no contrast against itself`` (Colour a) =
        Approx.abs' 1e-12 (Color.contrastRatio a a) 1.0

    [<Fact>]
    let ``black against white attains the maximum exactly`` () =
        Assert.Equal(21.0, Color.contrastRatio Color.black Color.white, 9)

    [<Property>]
    let ``relative luminance lies in [0,1] and is monotone in grey`` (UnitT t) (UnitT u) =
        let l v = Color.relativeLuminance (Color.rgb v v v)
        let lo, hi = min t u, max t u
        l lo >= 0.0 && l hi <= 1.0 + 1e-12 && l lo <= l hi + 1e-15

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Interchange =

    /// `toHex` quantises to 8 bits, so the round trip is only accurate to half a quantum —
    /// 1/510. That is the honest bound and it is what a golden-image diff sees, since the SVG
    /// backend writes hex.
    [<Property>]
    let ``hex round-trips to within half an 8-bit quantum`` (Colour c) =
        match Color.tryOfHex (Color.toHex c) with
        | None -> false
        | Some r ->
            Approx.abs' (1.0 / 510.0 + 1e-12) r.R c.R
            && Approx.abs' (1.0 / 510.0 + 1e-12) r.G c.G
            && Approx.abs' (1.0 / 510.0 + 1e-12) r.B c.B

    [<Property>]
    let ``toHex is always seven ASCII characters`` (Colour c) =
        let h = Color.toHex c

        h.Length = 7
        && h.[0] = '#'
        && h.Substring 1 |> Seq.forall (fun ch -> (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f'))

    /// Hex output reaches the SVG document directly, so it has to be culture-invariant like
    /// every other number the backend writes.
    [<Fact>]
    let ``toHex is culture-invariant`` () =
        let c = Color.rgb 0.5 0.25 0.75
        let baseline = Color.toHex c
        Assert.Equal(baseline, Culture.run Culture.CommaDecimal (fun () -> Color.toHex c))
        Assert.Equal(baseline, Culture.run Culture.DotlessI (fun () -> Color.toHex c))

    [<Fact>]
    let ``the three hex forms parse`` () =
        Assert.Equal(Some(Color.rgb 1.0 0.0 0.0), Color.tryOfHex "#f00")
        Assert.Equal(Some(Color.rgb 1.0 0.0 0.0), Color.tryOfHex "#ff0000")
        Assert.Equal(Some(Color.rgba 1.0 0.0 0.0 0.0), Color.tryOfHex "#ff000000")
        Assert.Equal(None, Color.tryOfHex "#gg0000")
        Assert.Equal(None, Color.tryOfHex "nonsense")
        // White space inside a field is not a digit: `Int32.TryParse` with `HexNumber` took this for #ff0000.
        Assert.Equal(None, Color.tryOfHex "ff 000")
        Assert.Equal(Some(Color.rgb 0.0 0.0 1.0), Color.tryOfHex "#0000FF")

    [<Fact>]
    let ``ofHex raises where tryOfHex returns None`` () =
        Assert.Throws<System.Exception>(fun () -> Color.ofHex "not a colour" |> ignore)
        |> ignore

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 300, Replay = Seed, QuietOnSuccess = true)>]
module Degradation =

    /// D10 — `Degrade.forCaps` is what stops `Capabilities` being "a record nobody reads". A
    /// gradient collapses to the solid colour at its Oklab midpoint, which is exactly
    /// `Interp.stops ... 0.5`, so the colour law and the degradation share one implementation.
    [<Property>]
    let ``degrading a gradient yields its Oklab midpoint`` (Colour a) (Colour b) =
        let stops =
            [| { Offset = 0.0; Color = a }; { Offset = 1.0; Color = b } |]

        let scene =
            Path([| MoveTo Vec2.zero<nd> |], None, Some(LinearGradient(Vec2.zero<nd>, { X = 1.0<nd>; Y = 0.0<nd> }, stops)), NonZero)

        let caps = { Capabilities.full with Gradients = false }

        match Degrade.forCaps caps scene with
        | Path(_, _, Some(Solid c), _) -> Approx.colour c (Color.mix 0.5 a b)
        | _ -> false

    /// D14 — "`Capabilities.full` must be genuinely full". An earlier version shipped with
    /// `Semantics = false`, which silently stripped every `Annotated` node for anyone starting
    /// from the preset. Pinned so it can never regress.
    [<Fact>]
    let ``Capabilities full is genuinely full`` () =
        let c = Capabilities.full

        Assert.True(
            c.Gradients
            && c.RadialGradients
            && c.Clipping
            && c.Dashes
            && c.Rasters
            && c.TextShaping
            && c.RotatedText
            && c.Semantics
            && c.GroupOpacity
        )

        Assert.True(c.MaxBatch.IsNone)
        Assert.False(Capabilities.vector.TextShaping)

    [<Fact>]
    let ``degrading strips exactly what the capability denies`` () =
        let semantic =
            Annotated({ Role = RoleSeries; Label = "s"; Data = [||] }, Text
                { Text = "x"
                  Tex = None
                  At = Vec2.zero<nd>
                  Font =
                    { Family = "Helvetica"
                      Size = 10.0<pt>
                      Weight = 400
                      Italic = false }
                  Paint = Solid Color.black
                  Anchor = AnchorStart
                  Baseline = BaselineAlphabetic
                  Rotation = 0.5<rad> })

        match Degrade.forCaps { Capabilities.full with Semantics = false } semantic with
        | Annotated _ -> failwith "Annotated survived a backend that cannot carry semantics"
        | _ -> ()

        match Degrade.forCaps { Capabilities.full with RotatedText = false } semantic with
        | Annotated(_, Text t) -> Assert.Equal(0.0<rad>, t.Rotation)
        | _ -> failwith "expected the run to survive, unrotated"

    /// D10's other half: the GPU backend's instance-buffer ceiling is real, so §5.1's
    /// one-node-per-batch promise is broken in the smallest possible way rather than ignored.
    [<Property>]
    let ``splitBatches preserves every point, in order`` (UnitT t) =
        let n = 1 + int (t * 200.0)

        let pts =
            Array.init n (fun i -> { X = float i * 1.0<nd>; Y = 0.0<nd> })

        let batch =
            Markers
                { Points = pts
                  Shape = MarkCircle
                  Size = 3.0<px>
                  Fill = None
                  Stroke = None }

        let split =
            Degrade.splitBatches { Capabilities.full with MaxBatch = Some 16 } batch

        let recovered =
            Scene.fold
                (fun acc node ->
                    match node with
                    | Markers b -> Array.append acc b.Points
                    | _ -> acc)
                [||]
                split

        recovered = pts
