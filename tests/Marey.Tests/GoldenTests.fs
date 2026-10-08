/// §13 — the golden-image suite.
///
/// Every scene below is chosen to make one part of the SVG backend's behaviour visible in a
/// text diff. Between them they cover all ten `Node` cases, all seven `MarkerShape`s, both
/// gradient kinds, clipping, dashes, rotated text, semantics, and a realistic axis composition.
///
/// **Determinism is the property that makes any of this work.** SvgWriter.fs opens with it:
/// "the same Scene must produce byte-identical output on every machine, every run", and the
/// first hazard it names is a comma-decimal locale emitting `0,5`, "which is not a number SVG
/// can parse — this is a correctness bug, not a cosmetic one". So each golden scene is also
/// rendered twice and rendered again under `de-DE` and `tr-TR`, and all four results must be
/// byte-identical. Without that, a golden baseline is a record of one machine's locale.
module Marey.Tests.GoldenTests

open Xunit
open Marey
open Marey.UnitsOfMeasure
open Marey.Tests.Generators
open Marey.Tests.Golden

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
        Geom.compile Coord.cartesian Golden.surface (geomStyle i) g pts

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
        [ "paths", paths, Golden.surface
          "text", textRuns, Golden.surface
          "markers-shapes", markerShapes, Golden.surface
          "markers-batch", markerBatch, Golden.surface
          "gradients", gradients, Golden.surface
          "dashes", dashes, Golden.surface
          "clipping", clipping, Golden.surface
          "keyed-annotated", keyedAndAnnotated, Golden.surface
          "raster", raster, Golden.surface
          "groups-transforms", groupsAndTransforms, Golden.surface
          "rotated-text", rotatedText, anisotropic
          "axis-composition", axisComposition, wide
          "all-ten-nodes", allTenCases, Golden.surface
          "geoms-bands", phase6Bands, Golden.surface
          "geoms-distributions", phase6Distributions, Golden.surface ]

// ======================================================================================
// THE SNAPSHOTS
// ======================================================================================

module Snapshots =

    [<Fact>]
    let ``paths`` () = render "paths.svg" Scenes.paths

    [<Fact>]
    let ``text`` () = render "text.svg" Scenes.textRuns

    [<Fact>]
    let ``every marker shape`` () = render "markers-shapes.svg" Scenes.markerShapes

    [<Fact>]
    let ``a batched scatter`` () = render "markers-batch.svg" Scenes.markerBatch

    [<Fact>]
    let ``gradients`` () = render "gradients.svg" Scenes.gradients

    [<Fact>]
    let ``dashes, caps and joins`` () = render "dashes.svg" Scenes.dashes

    [<Fact>]
    let ``clipping`` () = render "clipping.svg" Scenes.clipping

    [<Fact>]
    let ``keyed and annotated`` () =
        render "keyed-annotated.svg" Scenes.keyedAndAnnotated

    [<Fact>]
    let ``raster`` () = render "raster.svg" Scenes.raster

    [<Fact>]
    let ``groups and transforms`` () =
        render "groups-transforms.svg" Scenes.groupsAndTransforms

    /// Rendered on a deliberately anisotropic 2:1 surface, which is where D13's rejected
    /// implementation would have differed.
    [<Fact>]
    let ``rotated text on an anisotropic surface`` () =
        renderAt Scenes.anisotropic "rotated-text.svg" Scenes.rotatedText

    [<Fact>]
    let ``a realistic axis composition`` () =
        renderAt Scenes.wide "axis-composition.svg" Scenes.axisComposition

    [<Fact>]
    let ``all ten Node cases`` () =
        assertAllTenCases Scenes.allTenCases
        render "all-ten-nodes.svg" Scenes.allTenCases

    /// M2 phase 6 — `ribbon`, `step`, `tile`, `segment` and `text`, through `Geom.compile`.
    [<Fact>]
    let ``the band geoms`` () = render "geoms-bands.svg" Scenes.phase6Bands

    /// M2 phase 6 — `boxplot` and `violin`, from real `Stat` output.
    [<Fact>]
    let ``the distribution geoms`` () =
        render "geoms-distributions.svg" Scenes.phase6Distributions

// ======================================================================================
// THE FIGURE — gate 6
//
// The gap this closes: every scene above is a hand-built `Node`, and until now `Chart` output was
// checked only by substring assertions. A figure is what users produce and the only artefact that
// exercises layout, axes, ticks, legend and theme at once, and it had no reviewable baseline in
// either backend. The TikZ half is in `TikzGoldenTests`, from the same `Golden.figure`.
// ======================================================================================

module Figures =

    [<Fact>]
    let ``a complete figure`` () =
        check "figure.svg" (Chart.render SvgBackend.render figureSurface figure)

    /// D45 — the categorical axis, as a picture. The claim under review in the diff is that the
    /// bands are evenly spaced and the bars sit one band wide inside them.
    [<Fact>]
    let ``a categorical figure`` () =
        check "figure-category.svg" (Chart.render SvgBackend.render figureSurface categoryFigure)

    /// D46 — a categorical colour channel. Three levels, three batches, and a legend that
    /// enumerates the levels rather than the series.
    [<Fact>]
    let ``a channelled figure`` () =
        check "figure-channels.svg" (Chart.render SvgBackend.render figureSurface channelFigure)

    /// D49 — the colorbar. Its pair in `TikzGoldenTests` is the one that matters: a bar that
    /// degraded to a single flat block would still produce a clean diff *here*.
    [<Fact>]
    let ``a figure with a colorbar`` () =
        check "figure-colorbar.svg" (Chart.render SvgBackend.render figureSurface colorbarFigure)

    /// D55 — the box plot as a whole figure: a categorical axis, a summary geom and an outlier.
    [<Fact>]
    let ``a box plot figure`` () =
        check "figure-boxplot.svg" (Chart.render SvgBackend.render figureSurface boxplotFigure)

    /// The tile with the colour channel that makes it mean something, and a colorbar over it.
    [<Fact>]
    let ``a heatmap figure`` () =
        check "figure-heatmap.svg" (Chart.render SvgBackend.render figureSurface heatmapFigure)

    /// M2.5, D64 — the first figure in the suite with a `Raster` in it.
    ///
    /// SVG inlines the pixels as a `data:` URI, so this baseline is self-contained and there is
    /// no sidecar on this path — deliberately, and stated in `RenderedDocument`: a single file
    /// is the property that makes SVG the notebook and golden format. The TikZ half of the pair
    /// is the one that grows a `.png` beside it.
    [<Fact>]
    let ``an image figure`` () =
        check "figure-image.svg" (Chart.render SvgBackend.render figureSurface imageFigure)

    /// A figure is a scene like any other, so it has to be one: if `Figure.toScene` ever emitted
    /// something outside the ten cases this would not compile, and if it stopped exercising them
    /// the golden above would be measuring less than it claims.
    [<Fact>]
    let ``the figure is built from the IR and nothing else`` () =
        let used = casesUsed (Chart.toScene figureSurface figure)

        // Not all ten: a figure has no `Raster` and no `Opacity` — a static figure fades
        // nothing — and no `Clip` is needed for a series that fits.
        // Asserting the *set* rather than a count keeps this honest as layout changes.
        Assert.Contains("Path", used)
        Assert.Contains("Text", used)
        Assert.Contains("Markers", used)
        Assert.Contains("Keyed", used)
        Assert.Contains("Annotated", used)

// ======================================================================================
// DETERMINISM
//
// The property that makes a golden baseline mean anything at all.
// ======================================================================================

module Determinism =

    [<Fact>]
    let ``two renders of the same scene are byte-identical`` () =
        for name, scene, extent in Scenes.all do
            let a = SvgBackend.render extent scene
            let b = SvgBackend.render extent scene
            Assert.True((a = b), name)

    /// SvgWriter.fs's opening requirement, and the bug class the backend was explicitly built to
    /// survive: under `de-DE` a naive `ToString()` writes `0,5`, which SVG cannot parse.
    [<Fact>]
    let ``rendering under a comma-decimal culture is byte-identical`` () =
        for name, scene, extent in Scenes.all do
            let baseline = SvgBackend.render extent scene
            let german = Culture.run Culture.CommaDecimal (fun () -> SvgBackend.render extent scene)
            Assert.True((baseline = german), sprintf "%s differed under %s" name Culture.CommaDecimal)

    /// Turkish, because its dotless-i changes what `ToLowerInvariant`-free code does — and
    /// `ApproxTextMeasurer` lowercases a font family name on the layout path.
    [<Fact>]
    let ``rendering under a dotless-i culture is byte-identical`` () =
        for name, scene, extent in Scenes.all do
            let baseline = SvgBackend.render extent scene
            let turkish = Culture.run Culture.DotlessI (fun () -> SvgBackend.render extent scene)
            Assert.True((baseline = turkish), sprintf "%s differed under %s" name Culture.DotlessI)

    /// One backend instance, reused. "`Begin` resets all state, so the same backend can render a
    /// sequence of scenes, and two renders of the same scene produce byte-identical strings."
    [<Fact>]
    let ``a reused backend instance produces identical output`` () =
        let backend = SvgBackend()
        let sink = backend :> IBackend

        let once scene =
            sink.Begin surface
            sink.Draw scene
            sink.Finish()
            backend.Result

        let first = once Scenes.axisComposition
        let _other = once Scenes.gradients
        let again = once Scenes.axisComposition
        Assert.Equal(first, again)

    /// A hex colour is not a number, and `#009e73` is Okabe–Ito's bluish green.
    ///
    /// The exponent guard below is a search for `digit e digit`, and every six-digit colour in
    /// the palette is a candidate false positive — which is exactly what happened the first time
    /// a golden scene was built out of `Geom.styleFor` rather than out of hand-picked colours.
    /// Stripping the colour literals first is what keeps the guard a claim about *numbers*; the
    /// alternative, picking scene colours that do not contain an `e`, makes the test pass by
    /// avoiding the input it was written to check.
    let private withoutColorLiterals (doc: string) =
        System.Text.RegularExpressions.Regex.Replace(doc, @"#[0-9a-fA-F]{3,8}\b", "#")

    /// No number in the output may carry an exponent: "the SVG number grammar has no exponent in
    /// path data", which is why every number goes through a custom .NET numeric format.
    [<Fact>]
    let ``no rendered document contains scientific notation`` () =
        for name, scene, extent in Scenes.all do
            let doc = withoutColorLiterals (SvgBackend.render extent scene)
            // Guard against digit-E-digit and digit-e-digit, which is what "R"/"%g" would emit.
            let bad =
                System.Text.RegularExpressions.Regex.IsMatch(doc, @"\d[eE][-+]?\d")

            Assert.False(bad, sprintf "%s contains scientific notation" name)

    /// The guard's own guard: a document that really does carry an exponent must still be
    /// caught after the colours are stripped, and a palette colour must not be.
    [<Fact>]
    let ``the exponent guard still sees an exponent and no longer sees a colour`` () =
        let exponential = System.Text.RegularExpressions.Regex.IsMatch(withoutColorLiterals "d=\"M1e-05 2\"", @"\d[eE][-+]?\d")

        let colored =
            System.Text.RegularExpressions.Regex.IsMatch(withoutColorLiterals "fill=\"#009e73\"", @"\d[eE][-+]?\d")

        Assert.True exponential
        Assert.False colored

    /// Tiny coordinates are the case that would trip a `%g`-style format: 1e-5 formats as
    /// "1e-05" under "R" and as "0" under the backend's three-decimal coordinate format.
    [<Fact>]
    let ``sub-resolution coordinates round rather than going exponential`` () =
        let scene =
            Scene.polyline (Stroke.solid Color.black 1.0<px>) [| pt 1e-9 1e-9; pt 2e-9 0.5 |]

        let doc = SvgBackend.render surface scene
        Assert.Contains("M0 200L0 100", doc)

        // Not a bare "e-" search: `stroke-width` contains one. The hazard is a *number* with an
        // exponent, which is what "R" and "%g" produce below 1e-4.
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(doc, @"\d[eE][-+]?\d"))

// ======================================================================================
// BACKEND CONTRACT
//
// Behaviour the golden files show but do not *state*.
// ======================================================================================

module Contract =

    [<Fact>]
    let ``the SVG backend advertises what it can do`` () =
        let caps = (SvgBackend() :> IBackend).Caps

        Assert.True(caps.Gradients && caps.RadialGradients && caps.Clipping && caps.Dashes && caps.Rasters)
        Assert.True(caps.RotatedText && caps.Semantics)
        // §10 — shaping is delegated to whatever opens the file, and the flag says so honestly.
        Assert.False(caps.TextShaping)
        // §5.1 — "a text file has no hardware limit, and one batch stays one batch".
        Assert.True(caps.MaxBatch.IsNone)

    [<Fact>]
    let ``Draw before Begin is refused`` () =
        let sink = SvgBackend() :> IBackend
        Assert.Throws<System.InvalidOperationException>(fun () -> sink.Draw Scene.empty) |> ignore

    [<Fact>]
    let ``Draw after Finish is refused until Begin is called again`` () =
        let backend = SvgBackend()
        let sink = backend :> IBackend
        sink.Begin surface
        sink.Draw Scene.empty
        sink.Finish()

        Assert.Throws<System.InvalidOperationException>(fun () -> sink.Draw Scene.empty)
        |> ignore

        sink.Begin surface
        sink.Draw Scene.empty
        sink.Finish()
        Assert.Contains("<svg", backend.Result)

    [<Fact>]
    let ``Result before Begin is refused`` () =
        let backend = SvgBackend()
        Assert.Throws<System.InvalidOperationException>(fun () -> backend.Result |> ignore) |> ignore

    /// D15 — geometry is pre-transformed and no `transform` attribute is emitted for it. The two
    /// exceptions are text rotation and `gradientTransform`, so a document with neither must
    /// contain no `transform` at all.
    [<Fact>]
    let ``geometry carries no transform attribute`` () =
        let doc = SvgBackend.render surface Scenes.groupsAndTransforms
        Assert.DoesNotContain("transform=", doc)
        Assert.Contains("<path", doc)

    /// D15's consequence: "every `<g>` in the output carries meaning (an id, a clip, or
    /// semantics), because `Group` emits no element of its own". That is what keeps the diffs
    /// readable, so it is worth asserting rather than hoping.
    [<Fact>]
    let ``a plain Group emits no element`` () =
        let plain =
            SvgBackend.render surface (Group [| Group [| Scene.polyline (Stroke.solid Color.black 1.0<px>) [| pt 0.0 0.0; pt 1.0 1.0 |] |] |])

        Assert.DoesNotContain("<g", plain)

    [<Fact>]
    let ``the root transform matches the surface`` () =
        let backend = SvgBackend()
        let sink = backend :> IBackend
        sink.Begin surface
        let t = backend.RootTransform

        let origin = Affine.apply t (pt 0.0 0.0)
        Assert.Equal(0.0, float origin.X, 9)
        Assert.Equal(200.0, float origin.Y, 9)
        Assert.True(float (Affine.determinant t) < 0.0)

    /// The one place `xlink` is used rather than SVG 2's plain `href`, on purpose: "every
    /// renderer in existence understands the xlink form".
    [<Fact>]
    let ``markers reference their symbol through xlink`` () =
        let doc = SvgBackend.render surface Scenes.markerBatch
        Assert.Contains("xlink:href=\"#qm\"", doc)
        // One definition, twenty-four uses.
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(doc, "id=\"qm\"").Count)
        Assert.Equal(24, System.Text.RegularExpressions.Regex.Matches(doc, "<use ").Count)

    [<Fact>]
    let ``identical gradients and clips are defined once`` () =
        let doc = SvgBackend.render surface Scenes.gradients
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(doc, "<linearGradient ").Count)
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(doc, "<radialGradient ").Count)

        let clipDoc = SvgBackend.render surface Scenes.clipping
        // Two identical disc clips collapse to one definition; the empty clip is a second.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(clipDoc, "<clipPath ").Count)

    [<Fact>]
    let ``duplicate keys are disambiguated deterministically`` () =
        let doc = SvgBackend.render surface Scenes.keyedAndAnnotated
        Assert.Contains("id=\"panel\"", doc)
        Assert.Contains("id=\"panel-2\"", doc)
        // A key starting with a digit is not a valid NCName; a leading underscore is prepended
        // rather than the digit being replaced, so the key stays readable.
        Assert.Contains("id=\"_9_starts_with_a_digit\"", doc)

    [<Fact>]
    let ``decoration is aria-hidden and carries no label`` () =
        let doc = SvgBackend.render surface Scenes.keyedAndAnnotated
        Assert.Contains("aria-hidden=\"true\"", doc)
        Assert.DoesNotContain("should not be emitted as a label", doc)
        // But the data survives, because hit-testing still wants it.
        Assert.Contains("data-value=\"42\"", doc)
        Assert.Contains("data-value-2=\"43\"", doc)

    [<Fact>]
    let ``text is escaped and padded runs preserve their whitespace`` () =
        let doc = SvgBackend.render surface Scenes.textRuns
        Assert.Contains("a &amp; b &lt; c &gt;", doc)
        Assert.Contains("xml:space=\"preserve\"", doc)

    /// D13, as a number rather than as a picture: a 30° run on a 2:1 surface must emit
    /// `rotate(-30 ...)`, not the 16.1° the direction-vector implementation would produce.
    [<Fact>]
    let ``a 30 degree run renders as 30 degrees on an anisotropic surface`` () =
        let scene = text "thirty" 0.5 0.5 AnchorMiddle BaselineAlphabetic (System.Math.PI / 6.0)

        let doc =
            SvgBackend.render { Width = 400.0<px>; Height = 200.0<px> } scene

        Assert.Contains("rotate(-30 ", doc)
        Assert.DoesNotContain("rotate(-16", doc)

    [<Fact>]
    let ``the document is a standalone SVG with no XML prolog`` () =
        let doc = SvgBackend.render surface Scenes.paths
        Assert.StartsWith("<svg xmlns=", doc)
        Assert.EndsWith("</svg>\n", doc)
        Assert.DoesNotContain("<?xml", doc)
        Assert.Contains("viewBox=\"0 0 320 200\"", doc)

    /// Every golden document must parse as XML. A golden file that is byte-stable but malformed
    /// is a golden file that records a bug.
    [<Fact>]
    let ``every golden document is well-formed XML`` () =
        for name, scene, extent in Scenes.all do
            let doc = SvgBackend.render extent scene

            let parsed =
                try
                    System.Xml.Linq.XDocument.Parse doc |> ignore
                    None
                with e ->
                    Some e.Message

            Assert.True(parsed.IsNone, sprintf "%s is not well-formed XML: %A" name parsed)
