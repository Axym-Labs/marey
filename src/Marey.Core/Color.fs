namespace Marey

/// A colour in **non-linear sRGB** with straight (non-premultiplied) alpha, components in
/// [0,1].
///
/// sRGB is the storage and interchange format because that is what every backend wants. It is
/// emphatically *not* the interpolation format — see `Oklab` and §7.1: "colour (in Oklab, not
/// sRGB — sRGB lerp goes grey through the middle)".
[<Struct>]
type Color =
    { R: float
      G: float
      B: float
      A: float }

/// Perceptually uniform colour space (Björn Ottosson, 2020). `L` is lightness in [0,1];
/// `a`/`b` are green–red and blue–yellow opponent axes, roughly in [-0.4, 0.4].
///
/// Two things in the design depend on this being here rather than bolted on later:
/// §7.1 (colour tweening that does not desaturate through the midpoint) and §9 (a
/// "perceptually-uniform diverging palette in Oklab for signed fields — because a physics
/// library that renders fields in jet is embarrassing").
[<Struct>]
type Oklab = { L: float; A: float; B: float; Alpha: float }

/// Cylindrical Oklab: lightness, chroma, hue. The space you build palettes in — constant
/// chroma and evenly spaced hue is what makes a categorical palette look like a set.
[<Struct>]
type Oklch =
    { L: float
      C: float
      /// Hue angle. Measured, so nobody feeds it degrees.
      H: float<rad>
      Alpha: float }

[<RequireQualifiedAccess>]
module Color =

    let inline private clamp01 (x: float) = if x < 0.0 then 0.0 elif x > 1.0 then 1.0 else x

    let rgba r g b a = { R = r; G = g; B = b; A = a }
    let rgb r g b = { R = r; G = g; B = b; A = 1.0 }

    let black = rgb 0.0 0.0 0.0
    let white = rgb 1.0 1.0 1.0
    let transparent = rgba 0.0 0.0 0.0 0.0

    let withAlpha (a: float) (c: Color) = { c with A = a }

    // ---- sRGB transfer function ------------------------------------------------------
    // The gamma curve. Skipping it is the other half of why naive colour blending looks
    // wrong; `Interp` for Color goes sRGB -> linear -> Oklab and back.

    let private toLinear (c: float) =
        if c <= 0.04045 then
            c / 12.92
        else
            ((c + 0.055) / 1.055) ** 2.4

    let private ofLinear (c: float) =
        if c <= 0.0031308 then
            c * 12.92
        else
            1.055 * (c ** (1.0 / 2.4)) - 0.055

    // ---- Oklab -----------------------------------------------------------------------

    /// Cube root. `System.Math.Cbrt` exists but Core must stay Fable-compatible (§13), and
    /// `**` is accurate enough here — measured at 6.7e-16 relative, which is the double
    /// precision floor. A Newton refinement step on top makes it *worse*, not better.
    ///
    /// **On the sRGB -> Oklab -> sRGB round-trip error, which is ~1e-6 and not reducible:**
    /// Ottosson's forward and inverse LMS matrices are published to 10 decimal places, so
    /// their product deviates from the identity by ~2.4e-10. The inverse matrix rows also
    /// nearly cancel for neutral colours (4.077 - 3.308 + 0.231 = 1), and `ofLinear`'s
    /// exponent curve has a slope of ~1500 near black, which amplifies that residual to
    /// ~9.4e-7 in sRGB.
    ///
    /// That is 4000x finer than the 8-bit display quantum of 1/255 = 3.9e-3, so it is
    /// invisible. Recorded here because it sets the floor for golden-image tolerances: a
    /// colour test that demands better than 1e-5 will flap forever chasing arithmetic that
    /// cannot be represented in the output.
    let private cbrt (x: float) =
        if x < 0.0 then -((-x) ** (1.0 / 3.0)) else x ** (1.0 / 3.0)

    let toOklab (c: Color) : Oklab =
        let r = toLinear c.R
        let g = toLinear c.G
        let b = toLinear c.B

        let l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b
        let m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b
        let s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b

        let l' = cbrt l
        let m' = cbrt m
        let s' = cbrt s

        { L = 0.2104542553 * l' + 0.7936177850 * m' - 0.0040720468 * s'
          A = 1.9779984951 * l' - 2.4285922050 * m' + 0.4505937099 * s'
          B = 0.0259040371 * l' + 0.7827717662 * m' - 0.8086757660 * s'
          Alpha = c.A }

    let ofOklab (c: Oklab) : Color =
        let l' = c.L + 0.3963377774 * c.A + 0.2158037573 * c.B
        let m' = c.L - 0.1055613458 * c.A - 0.0638541728 * c.B
        let s' = c.L - 0.0894841775 * c.A - 1.2914855480 * c.B

        let l = l' * l' * l'
        let m = m' * m' * m'
        let s = s' * s' * s'

        { R = clamp01 (ofLinear (4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s))
          G = clamp01 (ofLinear (-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s))
          B = clamp01 (ofLinear (-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s))
          A = clamp01 c.Alpha }

    let toOklch (c: Color) : Oklch =
        let lab = toOklab c

        { L = lab.L
          C = sqrt (lab.A * lab.A + lab.B * lab.B)
          H = atan2 lab.B lab.A * 1.0<rad>
          Alpha = lab.Alpha }

    let ofOklch (c: Oklch) : Color =
        let h = c.H / 1.0<rad>

        ofOklab
            { L = c.L
              A = c.C * cos h
              B = c.C * sin h
              Alpha = c.Alpha }

    /// Perceptual interpolation. This is what `Interp` for `Color` calls, and what every
    /// gradient stop and colour scale in the library resolves through.
    let mix (t: float) (a: Color) (b: Color) : Color =
        let x = toOklab a
        let y = toOklab b

        ofOklab
            { L = x.L + (y.L - x.L) * t
              A = x.A + (y.A - x.A) * t
              B = x.B + (y.B - x.B) * t
              Alpha = x.Alpha + (y.Alpha - x.Alpha) * t }

    // ---- Interchange -----------------------------------------------------------------

    /// `#rgb`, `#rrggbb`, `#rrggbbaa`. Returns None rather than raising: colour strings are
    /// often user data.
    let tryOfHex (s: string) : Color option =
        let s = s.Trim().TrimStart('#')

        let hex (i: int) (n: int) =
            match System.Int32.TryParse(s.Substring(i, n), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture) with
            | true, v -> Some v
            | _ -> None

        let ok3 a b c = Some(rgb (float a / 15.0) (float b / 15.0) (float c / 15.0))
        let ok4 a b c d = Some(rgba (float a / 255.0) (float b / 255.0) (float c / 255.0) (float d / 255.0))

        match s.Length with
        | 3 ->
            match hex 0 1, hex 1 1, hex 2 1 with
            | Some r, Some g, Some b -> ok3 r g b
            | _ -> None
        | 6 ->
            match hex 0 2, hex 2 2, hex 4 2 with
            | Some r, Some g, Some b -> ok4 r g b 255
            | _ -> None
        | 8 ->
            match hex 0 2, hex 2 2, hex 4 2, hex 6 2 with
            | Some r, Some g, Some b, Some a -> ok4 r g b a
            | _ -> None
        | _ -> None

    let ofHex (s: string) =
        match tryOfHex s with
        | Some c -> c
        | None -> failwithf "Marey: '%s' is not a valid hex colour." s

    let private byte01 (x: float) =
        int (System.Math.Round(clamp01 x * 255.0))

    /// `#rrggbb`. Alpha is dropped — backends carry it separately (SVG `fill-opacity`,
    /// TikZ `opacity`), because not every target supports 8-digit hex.
    let toHex (c: Color) =
        sprintf "#%02x%02x%02x" (byte01 c.R) (byte01 c.G) (byte01 c.B)

    // ---- Accessibility ---------------------------------------------------------------

    /// WCAG 2.x relative luminance.
    let relativeLuminance (c: Color) =
        0.2126 * toLinear c.R + 0.7152 * toLinear c.G + 0.0722 * toLinear c.B

    /// WCAG contrast ratio in [1, 21]. §9 asks for "a validator that checks contrast and
    /// CVD-safety"; this is the contrast half, and it belongs in Core so the theme layer and
    /// the test suite share one implementation.
    let contrastRatio (a: Color) (b: Color) =
        let la = relativeLuminance a
        let lb = relativeLuminance b
        let hi = max la lb
        let lo = min la lb
        (hi + 0.05) / (lo + 0.05)
