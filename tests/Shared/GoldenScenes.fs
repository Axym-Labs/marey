/// The definitions behind the 28 SVG golden baselines, shared by the .NET suite and the Fable
/// project in `tests/Marey.Fable` (D130), so that "the same compiler under Node" is checked
/// against the same scenes and figures rather than a copy of them.
///
/// Nothing here asserts. `svg` is the table the Node job walks, and `GoldenTests` checks that it
/// names every SVG baseline in `golden/` and renders each one as its own test does.
module Marey.Tests.GoldenScenes

open Marey
open Marey.UnitsOfMeasure

/// The standard surface for golden documents.
///
/// Deliberately **not square and not round**: 320x200 is 8:5, so an anisotropic nd → px map is
/// the normal case rather than the exception. D13's text-rotation finding — that pushing the
/// direction vector through the CTM renders a requested 30° as 23.4° on 800x600 — is invisible
/// on a square surface, and a golden suite that only ever renders squares would never have
/// caught it.
let surface: Extent<px> = { Width = 320.0<px>; Height = 200.0<px> }

// ======================================================================================
// SHARED SCENE VOCABULARY
//
// Small constructors so the golden scenes read as pictures rather than as record literals.
// ======================================================================================

let font size weight italic : FontSpec =
    { Family = "Helvetica"
      Size = size * 1.0<pt>
      Weight = weight
      Italic = italic }

let text (s: string) (x: float) (y: float) (anchor: TextAnchor) (baseline: TextBaseline) (rotation: float) =
    Text
        { Text = s
          Tex = None
          At = { X = x * 1.0<nd>; Y = y * 1.0<nd> }
          Font = font 11.0 400 false
          Paint = Solid(Color.rgb 0.1 0.1 0.12)
          Anchor = anchor
          Baseline = baseline
          Rotation = rotation * 1.0<rad> }

let pt (x: float) (y: float) : Vec2<nd> = { X = x * 1.0<nd>; Y = y * 1.0<nd> }

let stops (cs: (float * Color) list) =
    cs |> List.map (fun (o, c) -> { Offset = o; Color = c }) |> Array.ofList

// ======================================================================================
// THE SHARED FIGURE
//
// Gate 6 in the M1 plan: until now there was **no** golden baseline for a `Figure` in
// either backend. All thirteen scenes are hand-built `Node`s, and `Chart` output was checked only
// by substring assertions — so the one artefact users actually produce, and the only one that
// exercises layout, axes, ticks, legend and theme together, had no reviewable record at all.
//
// One figure, rendered by both backends, so a layout change shows up as a diff in two files that
// can be read against each other.
// ======================================================================================

/// A two-series figure with everything a real one has: a title, a subtitle, a legend, grids on
/// both axes, unit-derived axis titles, a line and a scatter.
///
/// `UnitStyle` is left at the theme default (`Unicode`) deliberately. The siunitx spelling is a
/// TeX-only artefact and would put raw macros into the SVG baseline; it gets its own TikZ-only
/// golden instead, so this pair stays a comparison of *the same picture* through two backends.
let figure: Figure =
    let ts = Array.init 24 (fun i -> float i * 0.4<s>)
    let xs = ts |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)

    Chart.line U.second U.metre ts xs
    |> Chart.withName "model"
    |> Chart.addScatter U.second U.metre ts.[..5] (ts.[..5] |> Array.map (fun t -> 0.5 * 9.6<m / s^2> * t * t))
    |> Chart.withName "measured"
    |> Chart.withTitle "Free fall"
    |> Chart.withSubtitle "displacement against elapsed time"
    |> Chart.withLegend LegendTopLeft
    |> Chart.withGrid true true

/// D45 — the categorical figure, and the second thing in this file that a user actually
/// produces.
///
/// Four string levels, one bar each, through `Chart.Category.bar`. What the baseline records
/// that no substring assertion can: that the bands are evenly spaced and centred, that the bars
/// are one band wide, that the level names land under them in order, and that the x axis carries
/// no conventional symbol while the y axis still carries its unit. A shift in any of those is a
/// diff in two files.
let categoryFigure: Figure =
    Chart.Category.bar
        U.metre
        [| "control"; "treated"; "placebo"; "washout" |]
        [| 12.4<m>; 19.1<m>; 7.8<m>; 15.0<m> |]
    |> Chart.withXLabel "treatment"
    |> Chart.withYLabel "yield"
    |> Chart.withTitle "Yield by treatment"
    |> Chart.withGrid false true

/// D46 / D49 — a categorical colour channel with shape as its redundant encoding.
///
/// One series, three levels, and the baseline records what the counts cannot: that the legend
/// enumerates the *levels* rather than the series, that each level's marks carry both its colour
/// and its shape, and that a channelled layer is still one batch per level in the emitted
/// document rather than one element per point.
let channelFigure: Figure =
    let keys = Array.init 24 (fun i -> [| "control"; "treated"; "washout" |].[i % 3])
    let ts = Array.init 24 (fun i -> float i * 0.4<s>)
    let xs = ts |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)

    Chart.scatter U.second U.metre ts xs
    |> Chart.colorByKey keys
    |> Chart.shapeByKey keys
    |> Chart.withTitle "Fall by treatment"
    |> Chart.withLegend LegendTopLeft

/// D49 — **the colorbar, in both backends, and this is the baseline the decision asked for.**
///
/// The failure it exists to catch is one no assertion can see. A gradient-filled bar is valid in
/// SVG and comes out of the TikZ backend as a single flat `#23908c` block, because
/// `Gradients = false` makes `Degrade.forCaps` collapse the ramp to its Oklab midpoint — valid
/// TeX, clean text diff, and no colorbar. So the bar is strips, and here are the strips, in a
/// file a reviewer can count.
///
/// It also records the placement claim: the bar is one more content column, the plot gives up
/// its width to it, and the x axis keeps its ticks.
let colorbarFigure: Figure =
    let ts = Array.init 24 (fun i -> float i * 0.4<s>)
    let xs = ts |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)
    let temps = ts |> Array.map (fun t -> (280.0 + 2.5 * float t) * 1.0<K>)

    Chart.scatter U.second U.metre ts xs
    |> Chart.colorBy U.kelvin temps
    |> Chart.withColorLabel "T"
    |> Chart.withTitle "Fall by temperature"

/// M2 phase 6 / D55 — **the box plot, as a picture**, and the first figure in this file whose
/// marks are a *summary* rather than the data.
///
/// What the baseline records that no assertion reaches: that the whiskers stop at an
/// observation rather than at the extreme, that the outlier is drawn outside them and is
/// countable, that the median rule spans the box exactly, and that the three bands are the same
/// width and evenly spaced. Every one of those is a thing that still renders when it is wrong.
///
/// The sample is written out rather than generated, because a golden master over a generated
/// sample is a record of a generator.
let boxplotFigure: Figure =
    let control = [| 12.1; 12.8; 13.4; 13.9; 14.2; 14.6; 15.1; 15.9; 22.5 |]
    let treated = [| 16.4; 17.0; 17.3; 17.9; 18.4; 18.8; 19.2; 19.9; 20.6 |]
    let washout = [| 11.8; 12.4; 13.1; 13.6; 14.0; 14.9; 15.4; 16.2; 16.8 |]

    let keys =
        Array.concat
            [ Array.create control.Length "control"
              Array.create treated.Length "treated"
              Array.create washout.Length "washout" ]

    let values =
        Array.concat [ control; treated; washout ] |> Array.map (fun v -> v * 1.0<m>)

    Chart.Category.boxplot U.metre keys values
    |> Chart.withYLabel "yield"
    |> Chart.withTitle "Yield by treatment"
    |> Chart.withGrid false true

/// M2 phase 6 — **the tile, and the colorbar over it.**
///
/// A heatmap is the geom that is worthless without a colour channel, so this is also the second
/// figure in the suite carrying a continuous ramp — and the one where the TikZ half matters for
/// a reason `figure-colorbar` does not cover: the tiles themselves are one `Path` per quantised
/// colour step (D46), so a batching regression shows up here as a file that is many times longer.
let heatmapFigure: Figure =
    let xs = [| 0.0 .. 5.0 |]
    let ys = [| 0.0 .. 3.0 |]

    let grid =
        [| for gy in ys do
               for gx in xs do
                   yield gx, gy |]

    let temps =
        grid |> Array.map (fun (gx, gy) -> (280.0 + 4.0 * gx + 7.0 * gy) * 1.0<K>)

    Chart.heatmap U.second U.metre U.kelvin (grid |> Array.map (fst >> (*) 1.0<s>)) (grid |> Array.map (snd >> (*) 1.0<m>)) temps
    |> Chart.withColorLabel "T"
    |> Chart.withTitle "Temperature field"

/// M2.5 phase 1 — **the image, and the sidecar it makes the TikZ backend write** (D64).
///
/// The first figure in the suite that puts a `Raster` into a scene. Nothing above the IR
/// produced one before `Chart.image`, which is why `renderWithAssets` had no customer and
/// `figure-heatmap` — which *looks* like the raster figure — contains no raster at all: a
/// heatmap is tiles.
///
/// **The image is deliberately asymmetric in both axes and in colour**, and that is the whole
/// design of the fixture. Orientation is the one property no coordinate test can reach, because
/// a flip applied wrongly to both backends still satisfies cross-backend agreement — so the top
/// row is bright and the bottom row is dark, and the left column differs from the right. A
/// symmetric test image would hide exactly the bug this figure exists to catch, in both the
/// `data:` URI path and the `\includegraphics` one.
///
/// Six pixels rather than six hundred: the sidecar is a committed baseline, and the encoder's
/// output is what is under review.
let imageFigure: Figure =
    let px r g b = [| byte r; byte g; byte b; 255uy |]

    let rgba =
        Array.concat
            [| px 250 240 60; px 250 140 40; px 230 40 40 // top row, bright, left to right
               px 40 60 150; px 30 40 110; px 15 20 70 |] // bottom row, dark

    Chart.image U.second U.metre { Width = 3; Height = 2; Rgba = rgba } 0.0<s> 0.0<m> 6.0<s> 4.0<m> false
    |> Chart.withTitle "Placed image"

/// A wide log axis: two integrators' energy drift over eleven decades, in joules, written in
/// powers of ten with no prefix (`Ticks.WideLogDecades`). Its TikZ golden is set under
/// `Theme.tikz`, so the ticks reach TeX as `$10^{-8}$` rather than as superscript text.
///
/// The values are products of exact constants, not `exp`: Node and .NET differ in the last bit of
/// `exp` (D141), and this figure is also rendered under Node byte for byte.
let logFigure: Figure =
    let ts = Array.init 11 (fun i -> float i * 1.0<s>)
    let euler = Array.scan (fun e _ -> e * 10.0) 1e-14<J> [| 1..10 |]
    let leapfrog = ts |> Array.mapi (fun i _ -> if i % 2 = 0 then 2e-15<J> else 5e-15<J>)

    Chart.line U.second Derived.joule ts euler
    |> Chart.withName "Euler"
    |> Chart.addLine U.second Derived.joule ts leapfrog
    |> Chart.withName "leapfrog"
    |> Chart.withYLog
    |> Chart.withYLabel "drift"
    |> Chart.withTitle "Energy drift"
    |> Chart.withLegend LegendTopLeft

/// D162 — rules and spans, out of the domain (D163). A residual panel in millimetres with a rule
/// at zero, a named band, a slope rule and a vertical rule past the data that is counted and
/// not drawn; and beside it a polar panel, where a horizontal rule is a circle and a vertical
/// span a wedge. The residuals are exact decimals rather than `sin` (D141).
let rulesFigure: Figure =
    let ts = Array.init 13 (fun i -> float i * 0.5<s>)

    let residuals =
        [| 0.4; -0.7; 0.9; 0.1; -0.3; -0.8; 0.6; 0.2; -0.5; 0.7; -0.1; -0.9; 0.3 |]
        |> Array.map (fun v -> v * 1e-3<m>)

    let left =
        Chart.scatter U.second U.metre ts residuals
        |> Chart.withName "residual"
        |> Chart.addHSpan U.metre -0.5e-3<m> 0.5e-3<m>
        |> Chart.withName "±0.5 mm"
        |> Chart.addHLine U.metre 0.0<m>
        |> Chart.addLineThrough U.second U.metre 0.0<s> -1e-3<m> 0.25e-3<m / s>
        |> Chart.addVLine U.second 9.0<s>
        |> Chart.withLegend LegendTopLeft
        |> Chart.withPanelTitle "rules"

    let right =
        Chart.line Derived.radian U.metre [| 0.0<rad>; 6.0<rad> |] [| 1.0<m>; 3.0<m> |]
        |> Chart.addHLine U.metre 2.0<m>
        |> Chart.addVSpan Derived.radian 1.0<rad> 1.5<rad>
        |> Chart.withCoord Coord.polar
        |> Chart.withPanelTitle "polar"

    left |> Chart.beside right |> Chart.withTitle "Rules and spans"

/// Two panels side by side.
let rulesSurface: Extent<px> = { Width = 760.0<px>; Height = 320.0<px> }

/// D164 / D165 — annotations and panel letters. Four emission lines marked with straight arrows
/// (one label above the plot area, unclipped), one with an arc to a place in data space, a note
/// in a corner, and the two panels lettered "(a)", "(b)" in reading order. The lines are drawn
/// as triangles over exact values, not Gaussians, so Node compiles the same scene (D141).
let annotateFigure: Figure =
    let nm = 1e-9<m>
    let lines = [| "Hδ", 410.0, 0.2; "Hγ", 434.0, 0.3; "Hβ", 486.0, 0.55; "Hα", 656.0, 1.0 |]

    let lam, intensity =
        [| yield 400.0, 0.03
           for _, c, h in lines do
               yield c - 2.0, 0.03
               yield c, h
               yield c + 2.0, 0.03
           yield 700.0, 0.03 |]
        |> Array.unzip

    let mutable spectrum =
        Chart.line U.metre U.one (lam |> Array.map (fun l -> l * nm)) intensity
        |> Chart.withName "observed"
        |> Chart.withLegend LegendTopLeft

    for name, c, h in lines do
        spectrum <-
            spectrum
            |> Chart.annotate U.metre U.one (c * nm) h name
            |> Chart.withTextOffset 10.0<px> 16.0<px>
            |> Chart.withArrow ArrowStraight

    let spectrum =
        spectrum
        |> Chart.annotate U.metre U.one (486.0 * nm) 0.3 "filter edge"
        |> Chart.withTextAt U.metre U.one (560.0 * nm) 0.6
        |> Chart.withArrow (ArrowArc 0.3)
        |> Chart.addPanelText 0.98 0.5 "T = 10⁴ K"

    let decay =
        Chart.scatter U.second U.metre [| 0.0<s>; 1.0<s>; 2.0<s>; 3.0<s> |] [| 4.0<m>; 2.0<m>; 1.0<m>; 0.5<m> |]
        |> Chart.annotate U.second U.metre 2.0<s> 1.0<m> "half-life"
        |> Chart.withTextOffset 12.0<px> 12.0<px>
        |> Chart.withArrow ArrowStraight

    spectrum |> Chart.withPanelLetters LetterParen |> Chart.beside decay

let annotateSurface: Extent<px> = { Width = 760.0<px>; Height = 320.0<px> }

/// D166 / D169 / D170 — stated ticks. A wavelength axis ticked every 50 nm, stated in metres
/// and labelled in nanometres; a band, as a parabola over exact values, under the named ticks
/// X, Γ, X; a depth profile on a reversed axis; and an axis pinned to plain metres.
let ticksFigure: Figure =
    let nm = 1e-9<m>

    let spectrum =
        Chart.line U.metre U.one [| 400.0 * nm; 550.0 * nm; 700.0 * nm |] [| 0.1; 0.7; 0.4 |]
        |> Chart.withXTicksEvery U.metre (50.0 * nm)
        |> Chart.withXDomain 400e-9 700e-9
        |> Chart.withXLabel "λ"
        |> Chart.withPanelTitle "every 50 nm"

    let edge = 8.0e9</m>
    let k = Array.init 17 (fun i -> -edge + float i * edge / 8.0)
    let energy = k |> Array.map (fun k -> 2.0<eV> * (k / edge) * (k / edge) - 1.0<eV>)

    let band =
        Chart.line (U.recip U.metre) NonSi.electronvolt k energy
        |> Chart.withXTicksNamed (U.recip U.metre) [| -edge, "X"; 0.0</m>, "Γ"; edge, "X" |]
        |> Chart.withXDomain (float -edge) (float edge)
        |> Chart.withXLabel "k"
        |> Chart.withPanelTitle "named"

    let depth =
        Chart.line U.kelvin U.metre [| 290.0<K>; 284.0<K>; 277.0<K>; 276.0<K> |] [| 0.0<m>; 100.0<m>; 400.0<m>; 800.0<m> |]
        |> Chart.withYReversed
        |> Chart.withYLabel "depth"
        |> Chart.withPanelTitle "reversed"

    let pinned =
        Chart.line U.second U.metre [| 0.0<s>; 1.0<s> |] [| 0.0<m>; 0.002<m> |]
        |> Chart.withYPrefix Prefix.none
        |> Chart.withPanelTitle "plain metres"

    (spectrum |> Chart.beside band) |> Chart.above (depth |> Chart.beside pinned)

let ticksSurface: Extent<px> = { Width = 760.0<px>; Height = 520.0<px> }

/// D167 / D168 — P1 of the M8 port: a fit over measurements with a residual panel below it,
/// under `Theme.publication` — framed, ticks inside on four sides, minor ticks — lettered, with a
/// rule at zero and an axis stated every second. The model is a polynomial over exact values.
let publicationFigure: Figure =
    let ts = Array.init 13 (fun i -> float i * 0.5<s>)
    let model (t: float<s>) = 1.0e-3<m> * (float t - 3.0) * (float t - 3.0) - 4.0e-3<m>
    let wobble = [| 0.4; -0.7; 0.9; 0.1; -0.3; -0.8; 0.6; 0.2; -0.5; 0.7; -0.1; -0.9; 0.3 |]
    let measured = Array.mapi (fun i t -> model t + wobble.[i] * 0.4e-3<m>) ts
    let sigma = Array.create ts.Length 0.4e-3<m>
    let fine = Array.init 61 (fun i -> float i * 0.1<s>)

    let fit =
        Chart.errorbar U.second U.metre ts measured sigma
        |> Chart.withName "measured"
        |> Chart.addLine U.second U.metre fine (fine |> Array.map model)
        |> Chart.withName "model"
        |> Chart.withXTicksEvery U.second 1.0<s>
        |> Chart.withLegend LegendBottomRight

    let residual =
        Chart.errorbar U.second U.metre ts (Array.map2 (-) measured (ts |> Array.map model)) sigma
        |> Chart.addHLine U.metre 0.0<m>
        |> Chart.withXTicksEvery U.second 1.0<s>
        |> Chart.withYLabel "residual"

    fit
    |> Chart.above residual
    |> Chart.withPanelLetters LetterParen
    |> Chart.withTheme Theme.publication

let publicationSurface: Extent<px> = { Width = 480.0<px>; Height = 420.0<px> }

/// D171 — legends. Three warming curves (rational, not `exp`, D141) four ways: a legend in its own
/// column, one in its own row in three columns, the emptiest corner, and a corner without a frame.
let private warming (title: string) =
    let ts = Array.init 31 (fun i -> float i * 20.0<s>)
    let curve (offset: float<K>) = ts |> Array.map (fun t -> 300.0<K> - offset + 60.0<K> * (t / (t + 150.0<s>)))

    Chart.line U.second U.kelvin ts (curve 0.0<K>)
    |> Chart.withName "chamber"
    |> Chart.addLine U.second U.kelvin ts (curve 4.0<K>)
    |> Chart.withName "probe"
    |> Chart.addLine U.second U.kelvin ts (curve 10.0<K>)
    |> Chart.withName "wall"
    |> Chart.withPanelTitle title

let legendsFigure: Figure =
    let right = warming "outside right" |> Chart.withLegend LegendOutsideRight
    let below = warming "below, three columns" |> Chart.withLegend LegendOutsideBottom |> Chart.withLegendColumns 3
    let best = warming "best" |> Chart.withLegend LegendBest
    let bare = warming "no frame" |> Chart.withLegendFrame false
    (right |> Chart.beside below) |> Chart.above (best |> Chart.beside bare) |> Chart.withTheme Theme.publication

let legendsSurface: Extent<px> = { Width = 760.0<px>; Height = 560.0<px> }

/// D171 — one legend for three panels, beside them.
let sharedLegendFigure: Figure =
    warming "one" |> Chart.beside (warming "two") |> Chart.beside (warming "three") |> Chart.withFigureLegend FigureLegendRight

let sharedLegendSurface: Extent<px> = { Width = 760.0<px>; Height = 280.0<px> }

/// D174 — a mosaic: one panel down the left, two stacked beside it whose y labels differ in
/// width, lettered in reading order. The spanning panel's plot runs from the top of (b)'s to the
/// bottom of (c)'s.
let mosaicFigure: Figure =
    let ts = Array.init 31 (fun i -> float i * 20.0<s>)
    let rise (k: float) = ts |> Array.map (fun t -> k * 1e-3<m> * (t / (t + 150.0<s>)))

    Chart.mosaic
        "AB;AC"
        [ 'A', warming "a panel over two rows"
          'B', Chart.line U.second U.metre ts (rise 1.0)
          'C', Chart.line U.second U.metre ts (rise 100.0) |> Chart.withYLabel "deflection" ]
    |> Chart.withPanelLetters LetterParen
    |> Chart.withTheme Theme.publication

let mosaicSurface: Extent<px> = { Width = 760.0<px>; Height = 420.0<px> }

/// The figure surface. Larger than `surface` because a figure has margins, ticks and a legend to
/// fit, and a cramped one tests the collision ladder rather than the layout.
let figureSurface: Extent<px> = { Width = 480.0<px>; Height = 300.0<px> }

// ======================================================================================
// SCENES
//
// Defined as values rather than inline so the determinism suite can iterate over all of them
// without restating any of it.
// ======================================================================================

module Scenes =

    let private ink = Color.rgb 0.11 0.12 0.15
    let private accent = Color.rgb 0.16 0.38 0.86
    let private warm = Color.rgb 0.90 0.36 0.18

    let private rampStops =
        stops
            [ 0.0, Color.rgba 0.99 0.85 0.20 1.0
              0.5, Color.rgb 0.90 0.36 0.18
              1.0, Color.rgba 0.30 0.05 0.35 0.85 ]

    /// All four `PathSeg` cases, both `FillRule`s, and a stroked path with no fill — which is
    /// the case that goes wrong if `fill="none"` is ever omitted, because SVG's initial fill is
    /// black and every unfilled outline becomes a blob.
    let paths =
        Group
            [| Path(
                   [| MoveTo(pt 0.08 0.15)
                      LineTo(pt 0.28 0.15)
                      CubicTo(pt 0.36 0.15, pt 0.36 0.55, pt 0.28 0.55)
                      LineTo(pt 0.08 0.55)
                      ClosePath |],
                   Some(Stroke.solid ink 1.5<px>),
                   Some(Solid(Color.rgba 0.16 0.38 0.86 0.4)),
                   NonZero
               )
               // A five-pointed star: the shape whose interior differs between the two fill
               // rules, so the rule is actually visible in the rendered document.
               Path(
                   [| let cx, cy, r = 0.62, 0.36, 0.2

                      for i in 0..4 do
                          let a = -System.Math.PI / 2.0 + float i * 4.0 * System.Math.PI / 5.0
                          let p = pt (cx + r * cos a) (cy + r * sin a * 1.6)
                          yield (if i = 0 then MoveTo p else LineTo p)

                      yield ClosePath |],
                   Some(Stroke.solid warm 2.0<px>),
                   Some(Solid(Color.rgba 0.90 0.36 0.18 0.55)),
                   EvenOdd
               )
               // Stroked, unfilled.
               Path(
                   [| MoveTo(pt 0.05 0.85); CubicTo(pt 0.35 0.98, pt 0.65 0.72, pt 0.95 0.85) |],
                   Some
                       { Paint = Solid accent
                         Width = 3.0<px>
                         Dash = [||]
                         DashOffset = 0.0<px>
                         Cap = Round
                         Join = RoundJoin
                         MiterLimit = 4.0 },
                   None,
                   NonZero
               ) |]

    /// Anchors, baselines, weights, italics, XML escaping, and the leading/trailing whitespace
    /// that forces `xml:space="preserve"` — "tick labels padded with a space are a real thing".
    let textRuns =
        Group
            [| text "start" 0.05 0.9 AnchorStart BaselineAlphabetic 0.0
               text "middle" 0.5 0.9 AnchorMiddle BaselineAlphabetic 0.0
               text "end" 0.95 0.9 AnchorEnd BaselineAlphabetic 0.0
               text "hanging" 0.05 0.72 AnchorStart BaselineHanging 0.0
               text "central" 0.05 0.58 AnchorStart BaselineMiddle 0.0
               Text
                   { Text = "bold 700"
                     Tex = None
                     At = pt 0.05 0.44
                     Font = font 13.0 700 false
                     Paint = Solid ink
                     Anchor = AnchorStart
                     Baseline = BaselineAlphabetic
                     Rotation = 0.0<rad> }
               Text
                   { Text = "italic"
                     Tex = None
                     At = pt 0.5 0.44
                     Font = font 13.0 400 true
                     Paint = Solid warm
                     Anchor = AnchorStart
                     Baseline = BaselineAlphabetic
                     Rotation = 0.0<rad> }
               // Escaping: ampersand, angle brackets, quotes, and an astral-plane character.
               text "a & b < c > \"d\" 'e'" 0.05 0.28 AnchorStart BaselineAlphabetic 0.0
               text "kg·m·s⁻² · µm · Ω · Å" 0.05 0.16 AnchorStart BaselineAlphabetic 0.0
               text "  padded  " 0.05 0.05 AnchorStart BaselineAlphabetic 0.0 |]

    /// All seven `MarkerShape` cases, one batch each, so the shape table in `markerGeometry`
    /// is covered case by case. D5: shape is "the only redundant encoding channel for colour",
    /// so §9's colour-blind-safe scatter depends on every one of these being right.
    let markerShapes =
        let shapes =
            [ MarkCircle; MarkSquare; MarkDiamond; MarkTriangleUp; MarkTriangleDown; MarkCross; MarkPlus ]

        Group
            [| for i, shape in List.indexed shapes do
                   let x = 0.09 + float i * 0.135

                   yield
                       Markers
                           { Points = [| pt x 0.68 |]
                             Shape = shape
                             Size = 18.0<px>
                             Fill = Some(Solid(Color.rgba 0.16 0.38 0.86 0.75))
                             Stroke = Some(Stroke.solid ink 1.0<px>) }

                   // The same shape filled only, to prove Fill and Stroke are independent
                   // options and that a fill-only batch still draws (the twelve-vertex plus/cross
                   // outline exists for exactly this).
                   yield
                       Markers
                           { Points = [| pt x 0.32 |]
                             Shape = shape
                             Size = 14.0<px>
                             Fill = Some(Solid warm)
                             Stroke = None } |]

    /// A batch of many points: one shape definition, one group, one `<use>` per point. §5.1's
    /// "single biggest performance decision in the IR", made visible.
    let markerBatch =
        Markers
            { Points = Array.init 24 (fun i -> pt (0.05 + float i * 0.039) (0.5 + 0.35 * sin (float i * 0.5)))
              Shape = MarkCircle
              Size = 6.0<px>
              Fill = Some(Solid accent)
              Stroke = None }

    /// Both gradient kinds, plus a deliberate duplicate: the second linear gradient is
    /// identical to the first and must collapse to one `<defs>` entry, because `SvgDocument.Def`
    /// deduplicates on the rendered definition.
    let gradients =
        let linear =
            LinearGradient(pt 0.0 0.0, pt 1.0 1.0, rampStops)

        Group
            [| Scene.rect None (Some linear) { X0 = 0.04<nd>; Y0 = 0.55<nd>; X1 = 0.47<nd>; Y1 = 0.95<nd> }
               Scene.rect
                   (Some(Stroke.solid ink 1.0<px>))
                   (Some(RadialGradient(pt 0.75 0.75, 0.22<nd>, rampStops)))
                   { X0 = 0.53<nd>
                     Y0 = 0.55<nd>
                     X1 = 0.96<nd>
                     Y1 = 0.95<nd> }
               // Identical to the first: must reuse the same gradient id.
               Scene.rect None (Some linear) { X0 = 0.04<nd>; Y0 = 0.05<nd>; X1 = 0.47<nd>; Y1 = 0.45<nd> }
               // Gradient-painted text: an acknowledged corner in the backend.
               Text
                   { Text = "gradient text"
                     Tex = None
                     At = pt 0.75 0.25
                     Font = font 14.0 700 false
                     Paint = linear
                     Anchor = AnchorMiddle
                     Baseline = BaselineMiddle
                     Rotation = 0.0<rad> } |]

    /// Dash patterns, offsets, both non-default caps and both non-default joins, and a
    /// non-default miter limit — every branch of `writeStroke` that is not the SVG default.
    let dashes =
        let zig y =
            [| MoveTo(pt 0.05 y)
               LineTo(pt 0.3 (y + 0.08))
               LineTo(pt 0.55 (y - 0.08))
               LineTo(pt 0.95 y) |]

        Group
            [| Path(
                   zig 0.82,
                   Some
                       { Paint = Solid ink
                         Width = 2.0<px>
                         Dash = [| 6.0<px>; 3.0<px> |]
                         DashOffset = 0.0<px>
                         Cap = Butt
                         Join = Miter
                         MiterLimit = 4.0 },
                   None,
                   NonZero
               )
               Path(
                   zig 0.58,
                   Some
                       { Paint = Solid accent
                         Width = 3.0<px>
                         Dash = [| 1.0<px>; 4.0<px> |]
                         DashOffset = 2.5<px>
                         Cap = Round
                         Join = RoundJoin
                         MiterLimit = 4.0 },
                   None,
                   NonZero
               )
               Path(
                   zig 0.34,
                   Some
                       { Paint = Solid warm
                         Width = 4.0<px>
                         Dash = [| 10.0<px>; 2.0<px>; 2.0<px>; 2.0<px> |]
                         DashOffset = 1.0<px>
                         Cap = Square
                         Join = Bevel
                         MiterLimit = 4.0 },
                   None,
                   NonZero
               )
               // Miter limit only shows up when the join is a miter and the limit is not 4.
               Path(
                   zig 0.1,
                   Some
                       { Paint = Solid ink
                         Width = 5.0<px>
                         Dash = [||]
                         DashOffset = 0.0<px>
                         Cap = Butt
                         Join = Miter
                         MiterLimit = 1.5 },
                   None,
                   NonZero
               ) |]

    /// Clipping, including the empty clip path — which clips everything away, and which the
    /// backend emits faithfully rather than quietly reinterpreting.
    let clipping =
        let disc cx cy r =
            [| yield MoveTo(pt (cx + r) cy)
               for i in 1..48 do
                   let a = float i / 48.0 * 2.0 * System.Math.PI
                   yield LineTo(pt (cx + r * cos a) (cy + r * sin a))
               yield ClosePath |]

        Group
            [| Clip(
                   disc 0.3 0.5 0.22,
                   Group
                       [| Scene.rect None (Some(Solid accent)) { X0 = 0.1<nd>; Y0 = 0.3<nd>; X1 = 0.5<nd>; Y1 = 0.7<nd> }
                          Scene.polyline
                              (Stroke.solid Color.white 2.0<px>)
                              [| pt 0.1 0.35; pt 0.5 0.65 |] |]
               )
               // The same clip path again: deduplicated in <defs>.
               Clip(disc 0.3 0.5 0.22, Scene.rect None (Some(Solid(Color.rgba 1.0 1.0 1.0 0.25))) Rect.unit)
               Clip(
                   [||],
                   Scene.rect None (Some(Solid warm)) { X0 = 0.6<nd>; Y0 = 0.3<nd>; X1 = 0.9<nd>; Y1 = 0.7<nd> }
               ) |]

    /// `Keyed` and `Annotated`: every `SemanticRole`, an `aria-label`, `data-*` metadata, a
    /// duplicate data key (suffixed rather than dropped, because a repeated attribute is
    /// malformed XML), and two identical keys (resolved by appending `-2`).
    let keyedAndAnnotated =
        let mark y role label =
            Annotated(
                { Role = role
                  Label = label
                  Data = [| "value", "42"; "value", "43"; "series/id", "a&b" |] },
                Scene.polyline (Stroke.solid ink 1.0<px>) [| pt 0.06 y; pt 0.94 y |]
            )

        Group
            [| Keyed(Key "panel", mark 0.92 RoleSeries "measured series")
               Keyed(Key "panel", mark 0.80 RoleDatum "a single datum")
               Keyed(Key "9 starts with a digit", mark 0.68 RoleAxis "x axis")
               Keyed(Key "", mark 0.56 RoleLegend "legend")
               mark 0.44 RoleAnnotation "an annotation"
               // Decoration is aria-hidden, and a label on a hidden element is contradictory,
               // so the backend drops the label and keeps the data.
               mark 0.32 RoleDecoration "should not be emitted as a label"
               mark 0.20 (RoleOther "custom role") "custom" |]

    /// A 2x2 RGBA image with a transparent pixel, drawn without smoothing. Rows run top-first
    /// while `nd` is y-up, so the backend flips inside the destination box; a symmetric test
    /// image would hide that, so this one is deliberately asymmetric.
    let raster =
        Group
            [| Raster
                   { Image =
                       { Width = 2
                         Height = 2
                         Rgba =
                           [| 255uy; 0uy; 0uy; 255uy // top-left  red
                              0uy; 255uy; 0uy; 255uy // top-right green
                              0uy; 0uy; 255uy; 255uy // bottom-left  blue
                              255uy; 255uy; 0uy; 0uy |] } // bottom-right transparent
                     Bounds = { X0 = 0.1<nd>; Y0 = 0.15<nd>; X1 = 0.55<nd>; Y1 = 0.85<nd> }
                     Smooth = false }
               Raster
                   { Image =
                       { Width = 3
                         Height = 1
                         Rgba = [| 20uy; 20uy; 30uy; 255uy; 120uy; 40uy; 20uy; 200uy; 250uy; 220uy; 40uy; 255uy |] }
                     Bounds = { X0 = 0.62<nd>; Y0 = 0.35<nd>; X1 = 0.92<nd>; Y1 = 0.65<nd> }
                     Smooth = true } |]

    /// Nested `Transform`s composing into the root map, with `Group` carrying no element of its
    /// own. D15: no `transform` attribute is emitted for geometry, so what a reviewer reads in
    /// the diff is the already-composed device coordinates.
    let groupsAndTransforms =
        let unitSquare =
            Scene.rect (Some(Stroke.solid ink 1.0<px>)) (Some(Solid(Color.rgba 0.16 0.38 0.86 0.3))) { X0 = -0.1<nd>; Y0 = -0.1<nd>; X1 = 0.1<nd>; Y1 = 0.1<nd> }

        Group
            [| for i in 0..4 do
                   let a = float i * 0.3

                   yield
                       Transform(
                           Affine.compose (Affine.rotate<nd> (a * 1.0<rad>)) (Affine.translate (pt (0.2 + float i * 0.15) 0.5)),
                           Group [| unitSquare; Scene.polyline (Stroke.solid warm 1.0<px>) [| pt 0.0 0.0; pt 0.1 0.0 |] |]
                       ) |]

    /// D13 — text rotation is a device-space typographic property.
    ///
    /// "Pushing the direction vector `(cos θ, sin θ)` through the CTM is geometrically faithful,
    /// but the nd→px map is anisotropic on any non-square surface: a requested 30° renders as
    /// 23.4° on 800×600, and as 16.1° on 400×200." The golden document records the emitted
    /// `rotate(...)` angles, so a regression to the direction-vector implementation shows up as
    /// a changed number rather than as a subtly wrong picture.
    let rotatedText =
        Group
            [| for i in 0..5 do
                   let deg = float i * 30.0
                   let rad = deg * System.Math.PI / 180.0

                   yield
                       Text
                           { Text = sprintf "%g deg" deg
                             Tex = None
                             At = pt (0.1 + float i * 0.16) 0.5
                             Font = font 11.0 400 false
                             Paint = Solid ink
                             Anchor = AnchorStart
                             Baseline = BaselineAlphabetic
                             Rotation = rad * 1.0<rad> }

               // The frame's own rotation contributes on top of the run's, taken from the CTM.
               yield
                   Transform(
                       Affine.rotateAbout (0.25<rad>) (pt 0.5 0.15),
                       text "rotated frame, upright run" 0.5 0.15 AnchorMiddle BaselineAlphabetic 0.0
                   ) |]

    /// A realistic axis composition: a real `Scale`, real ticks from `Ticks.linear`, real labels
    /// from `Axis.auto` including prefix selection, gridlines, a frame, a data series as one
    /// batched `Markers` node, and semantics on each part.
    ///
    /// This is the document a reviewer would actually look at in a pull request, and it is an
    /// integration test of Core, Units and the backend at once: if prefix selection, tick
    /// formatting, or the nd → px map changes, this file changes.
    let axisComposition =
        let xs = Scale.linear U.second (0.0<s>, 2.5<s>)
        let ys = Scale.linear U.metre (0.0<m>, 4.0e-9<m>)

        let xAxis = Axis.auto Unicode "t" xs
        let yAxis = Axis.auto Unicode "y" ys

        let panel: Rect<nd> =
            { X0 = 0.16<nd>
              Y0 = 0.16<nd>
              X1 = 0.97<nd>
              Y1 = 0.94<nd> }

        let toPanel (u: float<nd>) (v: float<nd>) =
            pt
                (float panel.X0 + float u * (float panel.X1 - float panel.X0))
                (float panel.Y0 + float v * (float panel.Y1 - float panel.Y0))

        let gridPaint = Stroke.solid (Color.rgba 0.0 0.0 0.0 0.12) 1.0<px>
        let axisPaint = Stroke.solid ink 1.0<px>

        let gridlines =
            Annotated(
                { Role = RoleDecoration
                  Label = ""
                  Data = [||] },
                Group
                    [| for t in xAxis.Ticks do
                           yield Scene.polyline gridPaint [| toPanel t.Position 0.0<nd>; toPanel t.Position 1.0<nd> |]
                       for t in yAxis.Ticks do
                           yield Scene.polyline gridPaint [| toPanel 0.0<nd> t.Position; toPanel 1.0<nd> t.Position |] |]
            )

        let ticksAndLabels =
            Annotated(
                { Role = RoleAxis
                  Label = xAxis.Title
                  Data = [| "unit", xAxis.UnitText |] },
                Group
                    [| for t in xAxis.Ticks do
                           let p = toPanel t.Position 0.0<nd>

                           yield
                               Scene.polyline
                                   axisPaint
                                   [| p; { X = p.X; Y = p.Y - 0.025<nd> } |]

                           yield text t.Label (float p.X) (float p.Y - 0.055) AnchorMiddle BaselineHanging 0.0
                       yield text xAxis.Title 0.565 0.03 AnchorMiddle BaselineHanging 0.0 |]
            )

        let yTicksAndLabels =
            Annotated(
                { Role = RoleAxis
                  Label = yAxis.Title
                  Data = [| "unit", yAxis.UnitText |] },
                Group
                    [| for t in yAxis.Ticks do
                           let p = toPanel 0.0<nd> t.Position

                           yield
                               Scene.polyline
                                   axisPaint
                                   [| p; { X = p.X - 0.02<nd>; Y = p.Y } |]

                           yield text t.Label (float p.X - 0.03) (float p.Y) AnchorEnd BaselineMiddle 0.0
                       yield
                           Transform(
                               Affine.identity<nd>,
                               text yAxis.Title 0.04 0.55 AnchorMiddle BaselineAlphabetic (System.Math.PI / 2.0)
                           ) |]
            )

        // A damped oscillation, projected through the real scales.
        let data =
            Array.init 40 (fun i ->
                let t = float i / 39.0 * 2.5<s>
                let y = 4.0e-9<m> * (0.5 + 0.45 * exp (-float t * 0.8) * sin (float t * 6.0))
                toPanel (xs.Forward t) (ys.Forward y))

        Keyed(
            Key "figure",
            Group
                [| Scene.rect (Some axisPaint) (Some(Solid(Color.rgb 0.99 0.99 1.0))) panel
                   gridlines
                   Clip(
                       [| MoveTo { X = panel.X0; Y = panel.Y0 }
                          LineTo { X = panel.X1; Y = panel.Y0 }
                          LineTo { X = panel.X1; Y = panel.Y1 }
                          LineTo { X = panel.X0; Y = panel.Y1 }
                          ClosePath |],
                       Annotated(
                           { Role = RoleSeries
                             Label = "displacement"
                             Data = [| "n", "40" |] },
                           Group
                               [| Scene.polyline (Stroke.solid accent 1.75<px>) data
                                  Markers
                                      { Points = data
                                        Shape = MarkDiamond
                                        Size = 5.0<px>
                                        Fill = Some(Solid warm)
                                        Stroke = None } |]
                       )
                   )
                   ticksAndLabels
                   yTicksAndLabels |]
        )

    /// Every one of the ten `Node` cases in a single document, asserted rather than asserted-by-
    /// eye. §5: "this type is small, closed, total" — ten cases, and the reference backend has
    /// to handle all ten.
    let allTenCases =
        Group
            [| Keyed(
                   Key "all-ten",
                   Annotated(
                       { Role = RoleSeries
                         Label = "everything"
                         Data = [| "cases", "10" |] },
                       Group
                           [| Transform(
                                  Affine.translate (pt 0.0 0.05),
                                  Clip(
                                      [| MoveTo(pt 0.02 0.02)
                                         LineTo(pt 0.98 0.02)
                                         LineTo(pt 0.98 0.9)
                                         LineTo(pt 0.02 0.9)
                                         ClosePath |],
                                      Group
                                          [| Path(
                                                 [| MoveTo(pt 0.05 0.2); CubicTo(pt 0.3 0.8, pt 0.6 0.05, pt 0.9 0.6) |],
                                                 Some(Stroke.solid accent 2.0<px>),
                                                 None,
                                                 NonZero
                                             )
                                             Markers
                                                 { Points = [| pt 0.2 0.4; pt 0.5 0.5; pt 0.8 0.6 |]
                                                   Shape = MarkSquare
                                                   Size = 8.0<px>
                                                   Fill = Some(Solid warm)
                                                   Stroke = Some(Stroke.solid ink 0.75<px>) }
                                             Raster
                                                 { Image =
                                                     { Width = 2
                                                       Height = 2
                                                       Rgba =
                                                         [| 200uy; 30uy; 30uy; 255uy; 30uy; 200uy; 30uy; 255uy
                                                            30uy; 30uy; 200uy; 255uy; 200uy; 200uy; 30uy; 128uy |] }
                                                   Bounds = { X0 = 0.6<nd>; Y0 = 0.68<nd>; X1 = 0.9<nd>; Y1 = 0.86<nd> }
                                                   Smooth = false }
                                             // D67, and the reason group opacity is a node
                                             // rather than an alpha on each fill: these two
                                             // rectangles *overlap*. `Opacity` composites the
                                             // pair and fades the result, so the overlap reads
                                             // like the rest of the group; folding 0.5 into
                                             // each fill instead paints the overlap twice and
                                             // it comes out darker.
                                             //
                                             // The three baselines are where that is legible.
                                             // SVG and Skia composite, TikZ advertises
                                             // `GroupOpacity = false` and gets the fold, so
                                             // `all-ten-nodes.tex` and `all-ten-nodes.svg`
                                             // disagree about exactly one rectangle-sized
                                             // region — which is what the capability costs,
                                             // stated in a file a reviewer can open.
                                             Scene.opacity
                                                 0.5
                                                 (Group
                                                     [| Scene.rect
                                                            None
                                                            (Some(Solid accent))
                                                            { X0 = 0.10<nd>
                                                              Y0 = 0.10<nd>
                                                              X1 = 0.30<nd>
                                                              Y1 = 0.26<nd> }
                                                        Scene.rect
                                                            None
                                                            (Some(Solid warm))
                                                            { X0 = 0.22<nd>
                                                              Y0 = 0.17<nd>
                                                              X1 = 0.42<nd>
                                                              Y1 = 0.33<nd> } |])
                                             text "all ten" 0.06 0.08 AnchorStart BaselineAlphabetic 0.0 |]
                                  )
                              ) |]
                   )
               ) |]

    /// A deliberately anisotropic 2:1 surface — where D13's rejected rotation implementation
    /// would have differed.
    let anisotropic: Extent<px> = { Width = 400.0<px>; Height = 200.0<px> }

    /// Room for a realistic axis composition to be legible.
    let wide: Extent<px> = { Width = 480.0<px>; Height = 320.0<px> }

    // ======================================================================================
    // M2 PHASE 6 — the rest of §9's geom list
    //
    // Two scenes rather than one, because the two halves are read for different things: the
    // band geoms are read for where their *edges* are, and the distribution geoms for whether a
    // summary is drawn correctly. Both go through `Geom.compile` rather than being hand-built,
    // so what a reviewer diffs is the geom compiler's output and not a restatement of it.
    // ======================================================================================

    /// A nine-point series on the unit square, so every coordinate in the baseline is the data
    /// value scaled by the surface and a wrong number can be read rather than reverse-engineered.
    let private wave: Series<1, 1> =
        let n = 9
        let x = Array.init n (fun i -> float i / float (n - 1))

        { Name = "wave"
          X = x
          Y = x |> Array.map (fun v -> 0.45 + 0.22 * sin (v * System.Math.PI * 1.5))
          XScale = Scale.linear U.one (0.0, 1.0)
          YScale = Scale.linear U.one (0.0, 1.0) }

    let private wavePts =
        Array.init wave.X.Length (fun i -> pt wave.X.[i] wave.Y.[i])

    let private geomStyle i =
        Geom.styleFor Theme.initial i |> Geom.withMarkerSize 5.0<px>

    let private drawn i (g: GeomOf<nd>) (pts: Vec2<nd>[]) =
        Geom.compile Coord.cartesian surface (geomStyle i) g pts

    /// `ribbon`, `step`, `segment`, `tile` and `text` in one picture.
    ///
    /// The band is drawn first and the staircase over it, which is the composition
    /// `Chart.line |> Chart.addRibbon` produces and the order a reviewer has to be able to see:
    /// a ribbon painted last hides the line it was drawn for.
    let phase6Bands =
        let low = wave.Y |> Array.map (fun v -> v - 0.12)
        let high = wave.Y |> Array.map (fun v -> v + 0.12)

        let labels =
            wave.Y |> Array.mapi (fun i v -> if i % 4 = 0 then sprintf "%.2f" v else "")

        Group
            [| drawn 2 (Geom.ribbon wave low high |> Geom.onSeries wave) wavePts
               drawn 0 (GeomStep StepAfter) wavePts
               // A tile grid along the bottom, at a size that tessellates exactly: nine tiles of
               // 1/9 filling the unit interval, so there is neither a seam nor an overlap and
               // nothing hangs off the surface — a scene has no panel to clip it.
               //
               // The corners come from the *series*, not from the points `compile` is handed —
               // `Geom.tile` spends the measures at construction — so the shifted centres go in
               // the series and the two must agree. That asymmetry is the one thing about this
               // geom that is easy to get wrong twice.
               (let centres = wave.X |> Array.map (fun x -> 1.0 / 18.0 + x * 8.0 / 9.0)

                let strip =
                    { wave with
                        X = centres
                        Y = Array.create centres.Length 0.08 }

                drawn 1 (Geom.tile (1.0 / 9.0) 0.09 strip |> Geom.onSeries strip) (centres |> Array.map (fun x -> pt x 0.08)))
               // Each point joined to the one below it on the axis — the geom read for its
               // direction, so both ends have to be right and not only the start.
               drawn 3 (Geom.segment wave wave.X (Array.create wave.X.Length 0.2) |> Geom.onSeries wave) wavePts
               drawn 0 (Geom.text labels) wavePts |]

    /// `boxplot` and `violin`, side by side over four evenly spaced bands.
    ///
    /// Both are compiled from real `Stat` output rather than from hand-written spans, so the
    /// baseline also pins the quartile definition and the KDE grid: a change to either shows up
    /// here as a diff before it shows up as a wrong figure.
    let phase6Distributions =
        let bands: Series<1, 1> =
            { Name = "bands"
              X = [| 0.0; 1.0; 2.0; 3.0 |]
              Y = Array.create 4 0.5
              XScale = Scale.linear U.one (-0.5, 3.5)
              // Wide enough for the KDE grid, which runs three bandwidths past the sample: the
              // outlier at 22.5 plus 3 x 1.2 is 26.1, and a scene has no panel to clip it.
              YScale = Scale.linear U.one (4.0, 28.0) }

        let bandPts =
            bands.X |> Array.map (fun x -> pt (float (bands.XScale.Forward x)) 0.5)

        let control = [| 12.1; 12.8; 13.4; 13.9; 14.2; 14.6; 15.1; 15.9; 22.5 |]
        let treated = [| 16.4; 17.0; 17.3; 17.9; 18.4; 18.8; 19.2; 19.9; 20.6 |]

        let summaries =
            [| Stat.summary U.one control
               Stat.summary U.one treated
               Stat.summary U.one control
               Stat.summary U.one treated |]

        // One stated bandwidth for every violin, which is what makes the four comparable — and
        // a stated one rather than Silverman's, because a golden master must not move when a
        // bandwidth rule is tuned.
        let densities =
            [| control; treated; control; treated |]
            |> Array.map (Stat.densityWith U.one 1.2 32)

        Group
            [| drawn 0 (Geom.boxplot 0.6 bands summaries |> Geom.onSeries bands) bandPts.[..1]
               drawn 1 (Geom.violin 0.8 bands densities |> Geom.onSeries bands) bandPts.[2..] |]

    /// Base name, scene, and the surface it is rendered at.
    ///
    /// The surface belongs in this table rather than in each test because there are now **two**
    /// backends rendering these scenes, and a golden pair is only worth having if both halves
    /// describe the same picture. Naming the extents once is what makes `paths.svg` and
    /// `paths.tex` comparable line by line.
    ///
    /// The name carries no extension, for the same reason.
    let all: (string * Node * Extent<px>) list =
        [ "paths", paths, surface
          "text", textRuns, surface
          "markers-shapes", markerShapes, surface
          "markers-batch", markerBatch, surface
          "gradients", gradients, surface
          "dashes", dashes, surface
          "clipping", clipping, surface
          "keyed-annotated", keyedAndAnnotated, surface
          "raster", raster, surface
          "groups-transforms", groupsAndTransforms, surface
          "rotated-text", rotatedText, anisotropic
          "axis-composition", axisComposition, wide
          "all-ten-nodes", allTenCases, surface
          "geoms-bands", phase6Bands, surface
          "geoms-distributions", phase6Distributions, surface ]

// ======================================================================================
// THE STATISTICS AND FIELD FIGURES
// ======================================================================================

module Hexbin =

    /// A seeded two-dimensional normal sample, by Box–Muller over `Rng`: x in metres with a
    /// spread of 2 m, y in seconds with a spread of 0.5 s.
    let sample (n: int) =
        let us, _ = Rng.units (2 * n) (Rng.seeded 7UL)

        Array.init n (fun i ->
            let a, b = us.[2 * i], us.[2 * i + 1]
            let r = sqrt (-2.0 * log (1.0 - a))
            r * cos (2.0 * System.Math.PI * b) * 2.0<m>, r * sin (2.0 * System.Math.PI * b) * 0.5<s>)
        |> Array.unzip

    let xs, ys = sample 2000
    let h = Stat.hexbin U.metre U.second xs ys

    let figure = Chart.ofHexbin h |> Chart.withTitle "A two-dimensional normal sample"

module Fields =

    let potential =
        let xs = Grid.linear -2.0<m> 2.0<m> 13
        let ys = Grid.linear -1.5<m> 1.5<m> 9

        Field.sampleScalar xs ys (fun p ->
            let d1 = Vec2.norm { X = p.X - 0.6<m>; Y = p.Y + 0.1<m> }
            let d2 = Vec2.norm { X = p.X + 0.6<m>; Y = p.Y - 0.1<m> }
            (log (float d2) - log (float d1)) * 1.0<V>)

    let fieldUnit = U.div Derived.volt U.metre

    let chargePair =
        let xs = Grid.linear -1.8<m> 1.8<m> 10
        let ys = Grid.linear -1.2<m> 1.2<m> 7

        Field.sampleScalar xs ys (fun p ->
            let d1 = Vec2.norm { X = p.X - 0.6<m>; Y = p.Y + 0.1<m> }
            let d2 = Vec2.norm { X = p.X + 0.6<m>; Y = p.Y - 0.1<m> }
            (log (float d2) - log (float d1)) * 1.0<V>)

    let eField =
        let g = Field.grad chargePair
        { g with U = Array.map (~-) g.U; W = Array.map (~-) g.W }

    let quiverFigure =
        Field.quiverBy U.metre U.metre fieldUnit eField
        |> Chart.withYLabel "y"
        |> Chart.withTitle "The field of two line charges"

    let streamlinePendulum =
        let flow: Flow<rad, rad / s, s> = fun th om -> struct (om, -9.81<1 / s ^ 2> * sin (float th) * 1.0<rad>)
        Field.sampleFlow (Grid.linear -6.5<rad> 6.5<rad> 53) (Grid.linear -9.0<rad / s> 9.0<rad / s> 37) flow

    let streamlinePortrait =
        Field.streamlinesWith 18.0<px> Derived.radian (U.div Derived.radian U.second) streamlinePendulum
        |> Chart.withTitle "A pendulum's phase portrait"

    let portraitPendulum (n: int) =
        let flow: Flow<rad, rad / s, s> = fun th om -> struct (om, -9.81<1 / s ^ 2> * sin (float th) * 1.0<rad>)
        Field.sampleFlow (Grid.linear -6.5<rad> 6.5<rad> n) (Grid.linear -9.0<rad / s> 9.0<rad / s> (n * 37 / 53)) flow

    let phasePortrait =
        Phase.portraitWith 18.0<px> Derived.radian (U.div Derived.radian U.second) (portraitPendulum 53)
        |> Chart.withTitle "A pendulum's phase portrait"

    let heatmapFigure =
        Field.heatmap U.metre U.metre Derived.volt potential
        |> Chart.overlay (Field.contours U.metre U.metre potential)
        |> Chart.withYLabel "y"
        |> Chart.withTitle "Two line charges"

/// Every SVG baseline, by file name without the extension, and how to render it.
/// Every golden's scene, the figures compiled against a measurer: what a backend other than SVG
/// draws (the GPU's gate 6, the canvas's gate 13).
let scenesWith (m: ITextMeasurer) : (string * Extent<px> * Node) list =
    let wide = Chart.size 480.0 300.0
    let fig name extent f = name, extent, Chart.toSceneWith m extent f

    [ for name, node, extent in Scenes.all -> name, extent, node ]
    @ [ fig "figure" figureSurface figure
        fig "figure-category" figureSurface categoryFigure
        fig "figure-channels" figureSurface channelFigure
        fig "figure-colorbar" figureSurface colorbarFigure
        fig "figure-boxplot" figureSurface boxplotFigure
        fig "figure-heatmap" figureSurface heatmapFigure
        fig "figure-image" figureSurface imageFigure
        fig "figure-log" figureSurface logFigure
        fig "figure-hexbin" wide Hexbin.figure
        fig "figure-field-heatmap" wide Fields.heatmapFigure
        fig "figure-field-quiver" wide Fields.quiverFigure
        fig "figure-field-streamlines" wide Fields.streamlinePortrait
        fig "figure-field-portrait" wide Fields.phasePortrait
        fig "figure-rules" rulesSurface rulesFigure
        fig "figure-annotate" annotateSurface annotateFigure
        fig "figure-ticks" ticksSurface ticksFigure
        fig "figure-publication" publicationSurface publicationFigure
        fig "figure-legends" legendsSurface legendsFigure
        fig "figure-legend-shared" sharedLegendSurface sharedLegendFigure
        fig "figure-mosaic" mosaicSurface mosaicFigure ]

let svg: (string * (unit -> string)) list =
    let wide = Chart.size 480.0 300.0
    let figureAt extent fig = fun () -> Chart.render SvgBackend.render extent fig

    [ for name, node, extent in Scenes.all -> name, (fun () -> SvgBackend.render extent node) ]
    @ [ "figure", figureAt figureSurface figure
        "figure-category", figureAt figureSurface categoryFigure
        "figure-channels", figureAt figureSurface channelFigure
        "figure-colorbar", figureAt figureSurface colorbarFigure
        "figure-boxplot", figureAt figureSurface boxplotFigure
        "figure-heatmap", figureAt figureSurface heatmapFigure
        "figure-image", figureAt figureSurface imageFigure
        "figure-log", figureAt figureSurface logFigure
        "figure-hexbin", figureAt wide Hexbin.figure
        "figure-field-heatmap", figureAt wide Fields.heatmapFigure
        "figure-field-quiver", figureAt wide Fields.quiverFigure
        "figure-field-streamlines", figureAt wide Fields.streamlinePortrait
        "figure-field-portrait", figureAt wide Fields.phasePortrait
        "figure-rules", figureAt rulesSurface rulesFigure
        "figure-annotate", figureAt annotateSurface annotateFigure
        "figure-ticks", figureAt ticksSurface ticksFigure
        "figure-publication", figureAt publicationSurface publicationFigure
        "figure-legends", figureAt legendsSurface legendsFigure
        "figure-legend-shared", figureAt sharedLegendSurface sharedLegendFigure
        "figure-mosaic", figureAt mosaicSurface mosaicFigure ]
