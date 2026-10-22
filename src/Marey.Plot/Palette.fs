namespace Marey

/// The three dichromacies, for a validator that checks contrast and colour-vision safety,
/// because a physics library that renders fields in jet is embarrassing.
///
/// Anomalous trichromacy (protanomaly and friends) is deliberately absent: it is a *severity*
/// parameter on the same axis, and the dichromatic end of that axis is the conservative test.
/// A palette that survives full protanopia survives protanomaly.
type Cvd =
    | Protanopia
    | Deuteranopia
    | Tritanopia

/// A colour ramp as **data**, not a closure.
///
/// A function field would make every enclosing record — `Theme` above all — compare unequal to
/// an identically-constructed copy, which is the same reference-equality trap kept out of
/// the IR. Stops are a value; `Ramp.at` is the evaluator.
///
/// Stops are in ascending position order over [0,1] and are interpolated by `Interp.stops`,
/// which goes through `Color.mix` — i.e. **Oklab**, not sRGB. A ten-stop ramp resampled
/// perceptually is indistinguishable from the 256-entry table it came from.
type Ramp =
    { Name: string
      Stops: (float * Color)[] }

/// Colour ramps: built from stops, colours or hex, sampled at a position, reversed.
[<RequireQualifiedAccess>]
module Ramp =

    let ofStops (name: string) (stops: seq<float * Color>) = { Name = name; Stops = Array.ofSeq stops }

    /// Evenly spaced stops from a list of colours.
    let ofColors (name: string) (cs: seq<Color>) =
        let cs = Array.ofSeq cs
        if cs.Length = 0 then
            invalidArg "cs" "Marey: a ramp needs at least one colour."
        elif cs.Length = 1 then
            { Name = name; Stops = [| 0.0, cs.[0] |] }
        else
            { Name = name
              Stops = cs |> Array.mapi (fun i c -> float i / float (cs.Length - 1), c) }

    let ofHex (name: string) (hexes: seq<string>) =
        ofColors name (Array.ofSeq hexes |> Array.map Color.ofHex)

    /// Evaluate at any `t`. Out-of-range `t` clamps to the end stops, which is what a colour
    /// scale should do — unlike a positional scale, an out-of-gamut colour has nowhere to go.
    let at (r: Ramp) (t: float) : Color = Interp.stops r.Stops t

    let reverse (r: Ramp) =
        { r with
            Stops = r.Stops |> Array.map (fun (p, c) -> 1.0 - p, c) |> Array.rev }

    let sample (n: int) (r: Ramp) =
        if n <= 1 then
            [| at r 0.5 |]
        else
            Array.init n (fun i -> at r (float i / float (n - 1)))

/// The measured separation of a palette under one vision model.
[<Struct>]
type Separation =
    { /// `"normal"`, `"protanopia"`, …
      Vision: string
      /// Smallest Oklab distance between any two entries, after simulation.
      Minimum: float
      /// Which two entries were closest.
      IndexA: int
      IndexB: int }

/// What `Palette.validate` found. Every number here is measured, none is asserted.
[<NoComparison>]
type PaletteReport =
    { Size: int
      /// Normal vision first, then protanopia, deuteranopia, tritanopia.
      Separations: Separation[]
      /// The worst of the four.
      WorstSeparation: Separation
      /// WCAG contrast of the *weakest* entry against the background it was validated on.
      MinContrast: float
      MinContrastIndex: int
      /// True when every pair stays `SeparationFloor` apart under all four vision models and
      /// every entry clears `ContrastFloor` against the background.
      Ok: bool }

/// The colour system.
///
/// Categorical: **Okabe–Ito**, the eight-colour set designed for dichromats. Sequential:
/// viridis / cividis / magma, all perceptually ordered. Diverging: built here, in Oklab, rather
/// than quoted from a table — a signed field needs a map whose two arms are lightness-symmetric
/// about a neutral centre, and that is a two-line construction in a perceptual space and a
/// hand-tuned mess outside one.
///
/// Colour *maths* is Core's (`Color.mix`, `toOklch`/`ofOklch`, `contrastRatio`). The one
/// exception is the sRGB transfer function, which Core keeps private and which the
/// Brettel/Viénot simulation genuinely needs, because that transform is defined on **linear**
/// light. Applying it to gamma-encoded values — as a good many published implementations do —
/// makes the simulated colours too dark and the measured separations too optimistic.
[<RequireQualifiedAccess>]
module Palette =

    // ---- Categorical: Okabe–Ito -------------------------------------------------------
    //
    // Okabe & Ito, "Color Universal Design" (2002). Eight hues chosen so that each pair stays
    // distinguishable under protanopia, deuteranopia and tritanopia. `Palette.validate` below
    // measures whether that is actually true in Oklab rather than taking it on trust.

    let black = Color.ofHex "#000000"
    let orange = Color.ofHex "#E69F00"
    let skyBlue = Color.ofHex "#56B4E9"
    let bluishGreen = Color.ofHex "#009E73"
    let yellow = Color.ofHex "#F0E442"
    let blue = Color.ofHex "#0072B2"
    let vermillion = Color.ofHex "#D55E00"
    let reddishPurple = Color.ofHex "#CC79A7"

    /// The canonical eight, in Okabe–Ito's own order.
    let okabeIto =
        [| black; orange; skyBlue; bluishGreen; yellow; blue; vermillion; reddishPurple |]

    let okabeItoNames =
        [| "black"; "orange"; "sky blue"; "bluish green"; "yellow"; "blue"; "vermillion"; "reddish purple" |]

    /// Series order for a **light** background.
    ///
    /// Same eight colours, reordered. The canonical order opens with black, which is right for a
    /// legend swatch chart and wrong for a first data series — black is the axis colour, so
    /// series one would be indistinguishable from the frame. Yellow moves to the back because
    /// `#F0E442` on white is a contrast ratio of 1.2: legible as a filled area, marginal as a
    /// 1 px line, and a plot with seven series has bigger problems.
    let series =
        [| blue; vermillion; bluishGreen; orange; skyBlue; reddishPurple; yellow; black |]

    /// Series order for a **dark** background: black is unusable, so white takes its slot, and
    /// the two darkest hues move to the back.
    let seriesDark =
        [| skyBlue; orange; bluishGreen; vermillion; reddishPurple; yellow; Color.white; blue |]

    /// Cycle a categorical palette. Series eight and series sixteen share a colour, which is
    /// honest: eight is the number of categories a reader can actually hold.
    let pick (cs: seq<Color>) (i: int) =
        let cs = Array.ofSeq cs
        if cs.Length = 0 then Color.black else cs.[((i % cs.Length) + cs.Length) % cs.Length]

    // ---- Sequential ramps --------------------------------------------------------------
    //
    // Ten-stop quotations of the standard 256-entry tables, resampled in Oklab. The endpoints
    // and the ordering are what matter for a sequential map, and both survive exactly.

    let viridis =
        Ramp.ofHex
            "viridis"
            [| "#440154"; "#482878"; "#3E4A89"; "#31688E"; "#26828E"; "#1F9E89"; "#35B779"; "#6ECE58"; "#B5DE2B"; "#FDE725" |]

    /// The CVD-optimised member of the family: designed so that a deuteranope and a normal
    /// trichromat read the *same* ordering. The right default for a printed figure whose
    /// audience you do not know.
    let cividis =
        Ramp.ofHex
            "cividis"
            [| "#00204D"; "#00306F"; "#39486B"; "#575D6D"; "#707173"; "#8A8779"; "#A69D75"; "#C4B56C"; "#E4CF5B"; "#FFEA46" |]

    let magma =
        Ramp.ofHex
            "magma"
            [| "#000004"; "#180F3D"; "#440F76"; "#721F81"; "#9E2F7F"; "#CD4071"; "#F1605D"; "#FD9668"; "#FEC98D"; "#FCFDBF" |]

    let sequential = [| viridis; cividis; magma |]

    // ---- Diverging, constructed in Oklab -----------------------------------------------

    /// Build a diverging ramp between two hues through a light neutral.
    ///
    /// The construction is the whole argument for having a perceptual space: lightness is a
    /// linear ramp from `centreL` at the middle to `endL` at both ends, and chroma is a function
    /// of |2t−1| alone. Both arms therefore have *identical* lightness profiles, so neither one
    /// reads as "more" than the other — which is the failure that makes a hand-built
    /// red-white-blue map lie about the sign of a field. Chroma reaching zero exactly at the
    /// centre is what makes the hue switch invisible.
    ///
    /// The 0.85 exponent on chroma is the one aesthetic constant: pure |2t−1| leaves the
    /// mid-range washed out, because chroma perception is compressive.
    let divergingBetween (name: string) (hueLo: float<deg>) (hueHi: float<deg>) (chroma: float) (endL: float) (centreL: float) =
        let n = 33

        let stops =
            Array.init n (fun i ->
                let t = float i / float (n - 1)
                let d = abs (2.0 * t - 1.0)
                let l = centreL + (endL - centreL) * d
                let c = chroma * (d ** 0.85)
                let h = if t < 0.5 then hueLo else hueHi

                t,
                Color.ofOklch
                    { L = l
                      C = c
                      H = Space.radOfDeg h
                      Alpha = 1.0 })

        { Name = name; Stops = stops }

    /// The default for signed fields: cold below, warm above, near-white at zero.
    let diverging = divergingBetween "diverging" 250.0<deg> 27.0<deg> 0.135 0.47 0.965

    /// Green–purple. The alternative when blue and red already mean something in the figure —
    /// and the one to reach for when the reader may be a protanope, since green/purple survives
    /// the loss of the L cone better than blue/red does.
    let divergingGreenPurple =
        divergingBetween "diverging-gp" 150.0<deg> 320.0<deg> 0.115 0.47 0.965

    // ---- sRGB transfer function --------------------------------------------------------
    //
    // Core has these and keeps them private (Color.fs). Reproduced here because the CVD
    // transforms below are defined on linear light and there is no other way in.

    let private toLinear (c: float) =
        if c <= 0.04045 then c / 12.92 else ((c + 0.055) / 1.055) ** 2.4

    let private ofLinear (c: float) =
        if c <= 0.0031308 then
            c * 12.92
        else
            1.055 * (c ** (1.0 / 2.4)) - 0.055

    let inline private clamp01 (x: float) =
        if x < 0.0 then 0.0
        elif x > 1.0 then 1.0
        else x

    // ---- CVD simulation: Brettel / Viénot ----------------------------------------------
    //
    // Viénot, Brettel & Mollon (1999), "Digital video colourmaps for checking the legibility of
    // displays by dichromats". Linear RGB -> LMS on the Smith–Pokorny fundamentals, collapse the
    // missing cone onto the plane spanned by the other two, invert.
    //
    // Protanopia and deuteranopia use the single-plane reduction, which is exact for those two
    // because the confusion lines converge on a copunctal point outside the gamut. Tritanopia
    // genuinely needs Brettel's two half-planes hinged on the neutral axis; the single-plane
    // version of it is visibly wrong in the blues, which are exactly the colours a tritanope is
    // being tested on. Both are implemented.

    let private rgbToLms (r: float, g: float, b: float) =
        (17.8824 * r + 43.5161 * g + 4.11935 * b, 3.45565 * r + 27.1554 * g + 3.86714 * b, 0.0299566 * r + 0.184309 * g + 1.46709 * b)

    let private lmsToRgb (l: float, m: float, s: float) =
        (0.080944479 * l - 0.13050440 * m + 0.11672061 * s,
         -0.010248533 * l + 0.054019326 * m - 0.11362159 * s,
         -0.000365294 * l - 0.0041216147 * m + 0.69351294 * s)

    /// Simulate dichromatic vision. Round-trips through linear light, so the result is a real
    /// sRGB colour that can be handed to a backend and looked at.
    let simulate (kind: Cvd) (c: Color) : Color =
        let r = toLinear c.R
        let g = toLinear c.G
        let b = toLinear c.B
        let (l, m, s) = rgbToLms (r, g, b)

        let (l', m', s') =
            match kind with
            | Protanopia -> (2.02344 * m - 2.52581 * s, m, s)
            | Deuteranopia -> (l, 0.494207 * l + 1.24827 * s, s)
            | Tritanopia ->
                // Two half-planes hinged on the neutral axis; which one applies is decided by
                // the sign of the blue–yellow deviation, exactly as Brettel specifies.
                if s / (if l = 0.0 then 1e-12 else l) < 0.0117 then
                    (l, m, -0.012245 * l + 0.072035 * m)
                else
                    (l, m, -0.86744736 * l + 1.86727089 * m)

        let (r', g', b') = lmsToRgb (l', m', s')

        { R = clamp01 (ofLinear (clamp01 r'))
          G = clamp01 (ofLinear (clamp01 g'))
          B = clamp01 (ofLinear (clamp01 b'))
          A = c.A }

    // ---- Measurement --------------------------------------------------------------------

    /// Perceptual distance: plain Euclidean in Oklab, which is what "perceptually uniform"
    /// means and the only reason to have built the space.
    ///
    /// For scale: 0.02 is the just-noticeable step on a gradient, ~0.10 is "obviously two
    /// different colours in adjacent swatches", and the eight Okabe–Ito hues sit far above that
    /// under normal vision.
    let distance (a: Color) (b: Color) =
        let x = Color.toOklab a
        let y = Color.toOklab b
        let dl = x.L - y.L
        let da = x.A - y.A
        let db = x.B - y.B
        sqrt (dl * dl + da * da + db * db)

    /// Minimum pairwise Oklab distance, and which pair achieved it.
    let minSeparation (cs: seq<Color>) : float * int * int =
        let cs = Array.ofSeq cs
        let mutable best = System.Double.PositiveInfinity
        let mutable ia = -1
        let mutable ib = -1

        for i in 0 .. cs.Length - 1 do
            for j in i + 1 .. cs.Length - 1 do
                let d = distance cs.[i] cs.[j]

                if d < best then
                    best <- d
                    ia <- i
                    ib <- j

        (if cs.Length < 2 then System.Double.PositiveInfinity else best), ia, ib

    let private separationUnder (name: string) (kind: Cvd option) (cs: Color[]) =
        let simulated =
            match kind with
            | None -> cs
            | Some k -> cs |> Array.map (simulate k)

        let (d, a, b) = minSeparation simulated

        { Vision = name
          Minimum = d
          IndexA = a
          IndexB = b }

    /// Below this Oklab distance two categorical colours stop being reliably tellable apart in
    /// adjacent 2 px marks. There is no standard for this; the number is **calibrated against
    /// Okabe–Ito itself**, because that is the set the literature endorses and a floor it fails
    /// would be a floor with no constituency.
    ///
    /// Measured, on the eight canonical colours:
    ///
    /// | vision | minimum | closest pair |
    /// |---|---|---|
    /// | normal | 0.156 | orange / vermillion |
    /// | protanopia | 0.090 | blue / reddish purple |
    /// | deuteranopia | 0.079 | bluish green / reddish purple |
    /// | tritanopia | 0.068 | bluish green / blue |
    ///
    /// The tritanopia figure is the binding one and it is *not* a defect in the measurement:
    /// Okabe–Ito was optimised for the two common dichromacies, and tritanopia — which affects
    /// roughly 1 in 10 000 people and is not sex-linked — is where it is weakest. Recording the
    /// number is more useful than pretending the set is uniformly safe.
    [<Literal>]
    let SeparationFloor = 0.065

    /// WCAG 2.x non-text contrast minimum. A 1 px line at less than this against the panel is
    /// not a stylistic choice.
    [<Literal>]
    let ContrastFloor = 3.0

    /// The validator. Measures rather than asserts: every field of the report is a number you
    /// can put in a commit message.
    let validate (background: Color) (cs: seq<Color>) : PaletteReport =
        let cs = Array.ofSeq cs
        let seps =
            [| separationUnder "normal" None cs
               separationUnder "protanopia" (Some Protanopia) cs
               separationUnder "deuteranopia" (Some Deuteranopia) cs
               separationUnder "tritanopia" (Some Tritanopia) cs |]

        let worst = seps |> Array.minBy (fun s -> s.Minimum)

        let mutable minC = System.Double.PositiveInfinity
        let mutable minCi = -1

        for i in 0 .. cs.Length - 1 do
            let c = Color.contrastRatio background cs.[i]

            if c < minC then
                minC <- c
                minCi <- i

        { Size = cs.Length
          Separations = seps
          WorstSeparation = worst
          MinContrast = (if cs.Length = 0 then System.Double.PositiveInfinity else minC)
          MinContrastIndex = minCi
          Ok = worst.Minimum >= SeparationFloor && minC >= ContrastFloor }

    /// Is this ramp monotone in lightness?
    ///
    /// **This is the jet test.** A sequential map has exactly one job — more of the quantity
    /// must look like more — and the way jet fails it is that its lightness goes up, down and up
    /// again, inventing a bright band at cyan and another at yellow that readers report as
    /// features of the data. Monotone Oklab L is the machine-checkable version of that
    /// complaint, and viridis/cividis/magma all pass it while jet cannot.
    ///
    /// `tolerance` allows the tiny non-monotonicity that quantising a published table
    /// introduces; it is not a licence for a bump.
    let isLightnessMonotone (tolerance: float) (r: Ramp) =
        let n = 64
        let mutable ok = true
        let mutable prev = (Color.toOklab (Ramp.at r 0.0)).L

        for i in 1 .. n - 1 do
            let l = (Color.toOklab (Ramp.at r (float i / float (n - 1)))).L

            if l < prev - tolerance then
                ok <- false

            prev <- l

        ok

    /// Total lightness travel of a ramp, end to end. A diverging map should come back to where
    /// it started; a sequential map should not.
    let lightnessRange (r: Ramp) =
        let ls = Ramp.sample 64 r |> Array.map (fun c -> (Color.toOklab c).L)
        Array.min ls, Array.max ls
