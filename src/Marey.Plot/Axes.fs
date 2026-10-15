namespace Marey

/// Which edge of the panel an axis lives on.
type AxisSide =
    | AxisBottom
    | AxisLeft
    | AxisTop
    | AxisRight

/// The outcome of tick-label collision avoidance.
///
/// `Rule` names the rung of the ladder that succeeded, so the decision is inspectable from the
/// outside rather than being a mystery in the output. §15 is blunt that "axis label collision
/// avoidance ... is where plotting libraries actually die"; a library that thins your ticks
/// should at least be able to say why.
[<NoComparison>]
type TickPlan =
    { /// Indices into `AxisSpec.Ticks` that survive.
      Kept: int[]
      /// Counter-clockwise in `nd` (y-up). The backend converts to its own sense — D13.
      Rotation: float<rad>
      Rule: string }

/// Everything the axis compiler needs, resolved. Nothing here is decided by the backend.
[<NoComparison>]
type AxisRender =
    { /// From `Marey.Units`' `Axis.auto` — title, prefix and rescaled tick labels, already
      /// correct. This layer does not re-derive any of it.
      Spec: AxisSpec
      Side: AxisSide
      Plan: TickPlan
      /// The plot area, in **figure** nd.
      Panel: Rect<nd>
      /// The figure surface: needed for every px offset in the theme — D12.
      Extent: Extent<px>
      Theme: Theme
      ShowSpine: bool
      ShowTicks: bool
      ShowLabels: bool
      ShowTitle: bool
      /// Distance from the panel edge to the title anchor, in device px. Layout computes it
      /// from the measured tick labels; the axis compiler does not guess.
      TitleOffset: float<px>
      /// D168 — which way the marks point, and whether they are repeated, unlabelled, on the
      /// opposite side of the frame.
      Direction: TickDirection
      Mirror: bool
      /// D167 — whether `Spec.Minor` is drawn.
      ShowMinor: bool }

/// §5.5 — **an axis is compiled into `Path` and `Text`. There is no `Axis` node and there never
/// will be.**
///
/// Everything this module emits bottoms out in the nine cases of `Node`: the spine and the tick
/// marks are `Path`, the labels and the title are `Text`, gridlines are one `Path` routed
/// through `Coord.Grid` so that polar curves them without this module knowing polar exists (§4),
/// and the whole thing is wrapped in `Annotated` with `RoleAxis` or `RoleDecoration` (§5.3).
///
/// The titles and the tick labels are **not computed here.** They come from `Marey.Units`'
/// `Axis.auto`, which already owns prefix selection, rescaling and the `t [s]` convention. This
/// module's contribution is where the glyphs go.
[<RequireQualifiedAccess>]
module Axes =

    let private tag (x: float) : float<nd> = x * 1.0<nd>

    // ---- The conventional symbol for a physical quantity --------------------------------

    /// The quantity name an axis gets when the caller does not supply one.
    ///
    /// §9's payoff sentence is "`x (fun r -> r.Time)` where `Time : float(s)` gives you a linear
    /// scale over seconds with the axis labelled `t [s]`, **with no configuration**." In the
    /// grammar the `t` comes from the accessor. `Chart.line (ts, xs)` has no accessor and no
    /// field name — but it does have the `Dim`, and physics has a settled convention for what
    /// each dimension is called. A time axis is `t`. A length axis is `x`. Reading the
    /// conventional symbol out of the dimension vector is the only route by which
    /// `Chart.line (ts, positions)` can print `t [s]` and `x [m]` with nothing configured, and
    /// it is exactly as unit-derived as the bracketed part.
    ///
    /// Anything not in the table falls back to the axis's own name, `x` or `y`, which is what a
    /// plot of an unnamed compound quantity should say.
    ///
    /// D98 — `angle` is the witness's power of angle (`U.angleExponentOf`), which `Dim` cannot
    /// see. An angle is `θ`, an angular velocity `ω`, an angular acceleration `α`; any other
    /// quantity carrying an angle falls back to the axis's name rather than borrowing the name of
    /// the angle-free quantity with the same dimension — which is how `rad/s` was titled `f`.
    let defaultLabelFor (side: AxisSide) (angle: int) (d: Dim) =
        let positional =
            match side with
            | AxisBottom
            | AxisTop -> "x"
            | AxisLeft
            | AxisRight -> "y"

        if angle <> 0 then
            match angle, d.L, d.M, d.T, d.I, d.Th, d.N, d.J with
            | 1, 0, 0, 0, 0, 0, 0, 0 -> "θ"
            | 1, 0, 0, -1, 0, 0, 0, 0 -> "ω"
            | 1, 0, 0, -2, 0, 0, 0, 0 -> "α"
            | 2, 0, 0, 0, 0, 0, 0, 0 -> "Ω"
            | _ -> positional
        else

        match d.L, d.M, d.T, d.I, d.Th, d.N, d.J with
        | 0, 0, 0, 0, 0, 0, 0 -> positional
        | 0, 0, 1, 0, 0, 0, 0 -> "t"
        | 1, 0, 0, 0, 0, 0, 0 -> "x"
        | 0, 1, 0, 0, 0, 0, 0 -> "m"
        | 0, 0, 0, 1, 0, 0, 0 -> "I"
        | 0, 0, 0, 0, 1, 0, 0 -> "T"
        | 0, 0, 0, 0, 0, 1, 0 -> "n"
        | 0, 0, 0, 0, 0, 0, 1 -> "Iv"
        | 0, 0, -1, 0, 0, 0, 0 -> "f"
        | 1, 0, -1, 0, 0, 0, 0 -> "v"
        | 1, 0, -2, 0, 0, 0, 0 -> "a"
        | 2, 0, 0, 0, 0, 0, 0 -> "A"
        | 3, 0, 0, 0, 0, 0, 0 -> "V"
        | -3, 1, 0, 0, 0, 0, 0 -> "ρ"
        | 1, 1, -1, 0, 0, 0, 0 -> "p"
        | 1, 1, -2, 0, 0, 0, 0 -> "F"
        | -1, 1, -2, 0, 0, 0, 0 -> "p"
        | 2, 1, -2, 0, 0, 0, 0 -> "E"
        | 2, 1, -3, 0, 0, 0, 0 -> "P"
        | 0, 0, 1, 1, 0, 0, 0 -> "q"
        | 2, 1, -3, -1, 0, 0, 0 -> "V"
        | 2, 1, -3, -2, 0, 0, 0 -> "R"
        | 2, 1, -2, -2, 0, 0, 0 -> "L"
        | -2, -1, 4, 2, 0, 0, 0 -> "C"
        | _ -> positional

    /// The name a quantity gets from its dimension alone, for a witness that carries no angle.
    let defaultLabel (side: AxisSide) (d: Dim) = defaultLabelFor side 0 d

    /// The power of angle behind a packed scale (D98).
    let angleOf (packed: PackedScale) =
        packed.Accept
            { new IScaleConsumer<int> with
                member _.Apply<[<Measure>] 'u>(s: Scale<'u>) = U.angleExponentOf s.Unit.Symbol s.Unit.Factors }

    /// The `Dim` behind a packed scale, recovered through §2.3's rank-2 encoding.
    let dimOf (packed: PackedScale) =
        packed.Accept
            { new IScaleConsumer<Dim> with
                member _.Apply<[<Measure>] 'u>(s: Scale<'u>) = s.Unit.Dim }

    /// Build the axis spec, with the quantity name defaulted from the dimension when the caller
    /// did not name it. The unit half is entirely `Marey.Units`' work.
    /// D73 — the prefix may be decided elsewhere. The only caller that passes anything but
    /// `None` is the one laying out a whole animation against a union domain; see
    /// `Axis.ofScalePinnedWith` for why an animation needs it.
    /// D166 — `specOfPinned` with the ticks stated, in the axis's own numbers.
    let specTicked
        (ticks: TickSpec)
        (minor: MinorSpec)
        (pin: Prefix option)
        (style: UnitStyle)
        (tickCount: int)
        (label: string option)
        (side: AxisSide)
        (packed: PackedScale)
        : AxisSpec =
        let name =
            match label with
            | Some l -> l
            | None -> defaultLabelFor side (angleOf packed) (dimOf packed)

        Axis.ofPackedTicked ticks minor pin style UnitRegistry.si tickCount name packed

    let specOfPinned
        (pin: Prefix option)
        (style: UnitStyle)
        (tickCount: int)
        (label: string option)
        (side: AxisSide)
        (packed: PackedScale)
        : AxisSpec =
        specTicked TicksAuto MinorAuto pin style tickCount label side packed

    let specOf (style: UnitStyle) (tickCount: int) (label: string option) (side: AxisSide) (packed: PackedScale) : AxisSpec =
        specOfPinned None style tickCount label side packed

    // ---- Tick-label collision avoidance -------------------------------------------------

    let private isHorizontal (side: AxisSide) =
        match side with
        | AxisBottom
        | AxisTop -> true
        | AxisLeft
        | AxisRight -> false

    /// The angle a rotated tick label gets. −45° in the reader's sense; +45° counter-clockwise
    /// in `nd`, which is y-up (D13).
    let private slant = System.Math.PI / 4.0 * 1.0<rad>

    /// Footprint of a label *along the axis*, at a given rotation.
    let private footprint (m: ITextMeasurer) (font: FontSpec) (horizontal: bool) (rot: float<rad>) (text: string) =
        let b = TextMeasure.bounds m font AnchorMiddle BaselineMiddle rot text

        if horizontal then Rect.width b else Rect.height b

    /// **The collision rule.**
    ///
    /// Labels are measured, laid on the axis at their real positions, and checked pairwise for
    /// overlap with a minimum gap between them. When they collide, a fixed ladder is walked and
    /// the first rung that fits wins:
    ///
    /// 1. every label, upright;
    /// 2. **every second** label, upright — cheap, keeps horizontal type, and is what a reader
    ///    expects from a sparser axis;
    /// 3. every label, **rotated 45°** (horizontal axes only — rotating a left-axis label makes
    ///    it *taller*, which is the wrong direction);
    /// 4. every second, third, … up to every sixth, rotated (horizontal) or upright (vertical);
    /// 5. first and last only.
    ///
    /// Thinning is preferred over rotation at stride 2 and rotation is preferred over thinning
    /// beyond that, because dropping more than half the labels loses more information than
    /// tilting them costs. The rung that fired is recorded in `TickPlan.Rule`.
    ///
    /// Ticks are never *moved* and never relabelled: `Marey.Units` already guaranteed the
    /// labels are injective (§4), and a thinned axis is still a correct axis.
    let planTicks
        (m: ITextMeasurer)
        (font: FontSpec)
        (minGap: float<px>)
        (lengthPx: float<px>)
        (side: AxisSide)
        (ticks: TickLabel[])
        : TickPlan =
        let horizontal = isHorizontal side
        let n = ticks.Length

        if n = 0 then
            { Kept = [||]
              Rotation = 0.0<rad>
              Rule = "empty" }
        elif n = 1 then
            { Kept = [| 0 |]
              Rotation = 0.0<rad>
              Rule = "single" }
        else
            let centre (i: int) =
                let p = float ticks.[i].Position
                (if horizontal then p else 1.0 - p) * lengthPx

            let fits (stride: int) (rot: float<rad>) =
                let sizes = ticks |> Array.map (fun t -> footprint m font horizontal rot t.Label)

                let kept =
                    [| let mutable i = 0

                       while i < n do
                           yield i
                           i <- i + stride |]

                let mutable ok = true

                for k in 1 .. kept.Length - 1 do
                    let a = kept.[k - 1]
                    let b = kept.[k]
                    let need = (sizes.[a] + sizes.[b]) / 2.0 + minGap

                    if abs (centre b - centre a) < need then
                        ok <- false

                if ok then Some kept else None

            let plan (kept: int[]) (rot: float<rad>) (rule: string) =
                { Kept = kept
                  Rotation = rot
                  Rule = rule }

            let ladder =
                seq {
                    yield 1, 0.0<rad>, "all"
                    yield 2, 0.0<rad>, "every-2nd"

                    if horizontal then
                        yield 1, slant, "rotated-45"

                        for k in 2..6 do
                            yield k, slant, sprintf "rotated-45-every-%dth" k
                    else
                        for k in 3..6 do
                            yield k, 0.0<rad>, sprintf "every-%dth" k
                }

            let hit =
                ladder
                |> Seq.tryPick (fun (stride, rot, rule) -> fits stride rot |> Option.map (fun kept -> plan kept rot rule))

            match hit with
            | Some p -> p
            | None ->
                // Nothing fits. Two labels is the minimum that still communicates a scale, and
                // the endpoints are the two that carry the most.
                plan [| 0; n - 1 |] (if horizontal then slant else 0.0<rad>) "ends-only"

    /// The kept ticks, in order.
    let keptTicks (spec: AxisSpec) (plan: TickPlan) =
        plan.Kept
        |> Array.filter (fun i -> i >= 0 && i < spec.Ticks.Length)
        |> Array.map (fun i -> spec.Ticks.[i])

    // ---- Measurement, for the layout pass -----------------------------------------------

    /// How much room the tick labels need *across* the axis: their height on a horizontal axis,
    /// their width on a vertical one. Includes the tick mark and the label gap.
    /// The room a side's ticks and their labels take outside the plot area. An inward tick takes
    /// none: its length is inside the plot (D168).
    let tickExtentWith (direction: TickDirection) (m: ITextMeasurer) (theme: Theme) (side: AxisSide) (spec: AxisSpec) (plan: TickPlan) : float<px> =
        let kept = keptTicks spec plan

        if kept.Length = 0 then
            0.0<px>
        else
            let horizontal = isHorizontal side

            let across =
                kept
                |> Array.map (fun t ->
                    let b = TextMeasure.bounds m theme.TickFont AnchorMiddle BaselineMiddle plan.Rotation t.Label
                    if horizontal then Rect.height b else Rect.width b)
                |> Array.max

            (match direction with
             | TickIn -> 0.0<px>
             | TickOut
             | TickInOut -> theme.TickLength)
            + theme.LabelGap
            + across

    let tickExtent (m: ITextMeasurer) (theme: Theme) (side: AxisSide) (spec: AxisSpec) (plan: TickPlan) : float<px> =
        tickExtentWith theme.TickDirection m theme side spec plan

    /// How far a tick label sticks out past the *end* of the axis, on each side. A centred label
    /// at position 0 hangs half its width off the left edge of the panel, and if layout does not
    /// reserve that the figure clips it. This is half of §13's "labels inside bounds".
    let labelOverhang (m: ITextMeasurer) (theme: Theme) (side: AxisSide) (spec: AxisSpec) (plan: TickPlan) : float<px> =
        let kept = keptTicks spec plan

        if kept.Length = 0 then
            0.0<px>
        else
            let horizontal = isHorizontal side

            kept
            |> Array.map (fun t ->
                let b = TextMeasure.bounds m theme.TickFont AnchorMiddle BaselineMiddle plan.Rotation t.Label
                (if horizontal then Rect.width b else Rect.height b) / 2.0)
            |> Array.max

    /// Room for the axis title across the gutter.
    let titleExtent (m: ITextMeasurer) (theme: Theme) (title: string) : float<px> =
        if System.String.IsNullOrEmpty title then
            0.0<px>
        else
            let b =
                TextMeasure.bounds m theme.AxisTitleFont AnchorMiddle BaselineMiddle 0.0<rad> title

            theme.TitleGap + Rect.height b

    // ---- Compilation to the IR -----------------------------------------------------------

    let private alongX (panel: Rect<nd>) (p: float<nd>) =
        panel.X0 + (p / 1.0<nd>) * (panel.X1 - panel.X0)

    let private alongY (panel: Rect<nd>) (p: float<nd>) =
        panel.Y0 + (p / 1.0<nd>) * (panel.Y1 - panel.Y0)

    let private inPanel (p: float<nd>) =
        let v = float p
        v >= -1e-9 && v <= 1.0 + 1e-9

    /// Compile one axis into `Path` and `Text`.
    ///
    /// Every offset in the theme is in device px and is converted here, once, through
    /// `Layout.ndX`/`ndY` — which is anisotropic, so a 3.5 px tick on a 900x400 figure is a
    /// different `nd` length depending on which edge it hangs off. Getting that wrong is how
    /// tick marks end up visibly longer on one axis than the other.
    let compile (a: AxisRender) : Node =
        let t = a.Theme
        let e = a.Extent
        let panel = a.Panel
        let kept = keptTicks a.Spec a.Plan |> Array.filter (fun k -> inPanel k.Position)

        let axisStroke = Stroke.solid t.AxisColor t.AxisWidth

        let tickStroke =
            { Stroke.solid t.AxisColor t.TickWidth with
                Cap = Butt }

        let textPaint = Solid t.Foreground

        // Outward: away from the plot area, in nd, for any side.
        let outwardOf (side: AxisSide) =
            match side with
            | AxisBottom -> 0.0<nd>, -1.0<nd>
            | AxisTop -> 0.0<nd>, 1.0<nd>
            | AxisLeft -> -1.0<nd>, 0.0<nd>
            | AxisRight -> 1.0<nd>, 0.0<nd>

        let offsetAt (side: AxisSide) (d: float<px>) : Vec2<nd> =
            let outX, outY = outwardOf side

            { X = (outX / 1.0<nd>) * Layout.ndX e d
              Y = (outY / 1.0<nd>) * Layout.ndY e d }

        let anchorAt (side: AxisSide) (p: float<nd>) : Vec2<nd> =
            match side with
            | AxisBottom -> { X = alongX panel p; Y = panel.Y0 }
            | AxisTop -> { X = alongX panel p; Y = panel.Y1 }
            | AxisLeft -> { X = panel.X0; Y = alongY panel p }
            | AxisRight -> { X = panel.X1; Y = alongY panel p }

        let offset (d: float<px>) = offsetAt a.Side d
        let anchorPoint (p: float<nd>) = anchorAt a.Side p

        // ---- the spine ----------------------------------------------------------------
        let spine =
            if not a.ShowSpine then
                Scene.empty
            else
                let p0, p1 =
                    match a.Side with
                    | AxisBottom -> { X = panel.X0; Y = panel.Y0 }, { X = panel.X1; Y = panel.Y0 }
                    | AxisTop -> { X = panel.X0; Y = panel.Y1 }, { X = panel.X1; Y = panel.Y1 }
                    | AxisLeft -> { X = panel.X0; Y = panel.Y0 }, { X = panel.X0; Y = panel.Y1 }
                    | AxisRight -> { X = panel.X1; Y = panel.Y0 }, { X = panel.X1; Y = panel.Y1 }

                Path([| MoveTo p0; LineTo p1 |], Some axisStroke, None, NonZero)

        // ---- tick marks: one Path, many subpaths --------------------------------------
        //
        // D168 — a mark runs from `inward` lengths inside the spine to `outward` lengths outside
        // it, and a mirrored side repeats the marks on the opposite edge of the frame. D167's
        // minor marks are more subpaths of the same path, shorter.
        let outward, inward =
            match a.Direction with
            | TickOut -> 1.0, 0.0
            | TickIn -> 0.0, 1.0
            | TickInOut -> 1.0, 1.0

        let opposite =
            match a.Side with
            | AxisBottom -> AxisTop
            | AxisTop -> AxisBottom
            | AxisLeft -> AxisRight
            | AxisRight -> AxisLeft

        let markSegs (side: AxisSide) (length: float<px>) (positions: float<nd>[]) =
            let d = offsetAt side length

            [| for pos in positions do
                   let p = anchorAt side pos
                   yield MoveTo { X = p.X - d.X * inward; Y = p.Y - d.Y * inward }
                   yield LineTo { X = p.X + d.X * outward; Y = p.Y + d.Y * outward } |]

        let marks =
            let majors = kept |> Array.map (fun k -> k.Position)
            let minors = if a.ShowMinor then a.Spec.Minor |> Array.filter inPanel else [||]

            let segs =
                if not a.ShowTicks then
                    [||]
                else
                    Array.concat
                        [ markSegs a.Side t.TickLength majors
                          markSegs a.Side t.MinorTickLength minors
                          (if a.Mirror then markSegs opposite t.TickLength majors else [||])
                          (if a.Mirror then markSegs opposite t.MinorTickLength minors else [||]) ]

            if segs.Length = 0 then
                Scene.empty
            else
                Path(segs, Some tickStroke, None, NonZero)

        // ---- tick labels ---------------------------------------------------------------
        let labels =
            if not a.ShowLabels || kept.Length = 0 then
                Scene.empty
            else
                let d = offset (t.TickLength * outward + t.LabelGap)
                let rotated = a.Plan.Rotation <> 0.0<rad>

                let anchor, baseline =
                    match a.Side with
                    | AxisBottom -> (if rotated then AnchorEnd else AnchorMiddle), (if rotated then BaselineMiddle else BaselineHanging)
                    | AxisTop -> (if rotated then AnchorStart else AnchorMiddle), (if rotated then BaselineMiddle else BaselineAlphabetic)
                    | AxisLeft -> AnchorEnd, BaselineMiddle
                    | AxisRight -> AnchorStart, BaselineMiddle

                Group
                    [| for k in kept do
                           yield
                               Text
                                   { Text = k.Label
                                     // Digits, a decimal point and a minus sign typeset
                                     // identically in TeX and in plain text. A power of ten on
                                     // a wide log axis does not, and when the axis was compiled
                                     // in a TeX style its TeX form arrives with the tick.
                                     Tex = k.LabelTex
                                     At = anchorPoint k.Position + d
                                     Font = t.TickFont
                                     Paint = textPaint
                                     Anchor = anchor
                                     Baseline = baseline
                                     Rotation = a.Plan.Rotation } |]

        // ---- the title -----------------------------------------------------------------
        let title =
            if not a.ShowTitle || System.String.IsNullOrEmpty a.Spec.Title then
                Scene.empty
            else
                let mid = tag 0.5
                let d = offset a.TitleOffset

                let at, rotation, baseline =
                    match a.Side with
                    | AxisBottom -> { X = alongX panel mid; Y = panel.Y0 } + d, 0.0<rad>, BaselineHanging
                    | AxisTop -> { X = alongX panel mid; Y = panel.Y1 } + d, 0.0<rad>, BaselineAlphabetic
                    // +90 degrees counter-clockwise in nd reads bottom-to-top, and the glyph
                    // body then extends away from the panel.
                    | AxisLeft ->
                        { X = panel.X0; Y = alongY panel mid } + d, System.Math.PI / 2.0 * 1.0<rad>, BaselineAlphabetic
                    | AxisRight ->
                        { X = panel.X1; Y = alongY panel mid } + d, -System.Math.PI / 2.0 * 1.0<rad>, BaselineAlphabetic

                Text
                    { Text = a.Spec.Title
                      // The axis title is where the units story reaches a TeX backend: when the
                      // axis was compiled in the Latex or Siunitx style, `AxisSpec` carries the
                      // TeX spelling of the same title alongside the Unicode one.
                      Tex = a.Spec.TitleTex
                      At = at
                      Font = t.AxisTitleFont
                      Paint = textPaint
                      Anchor = AnchorMiddle
                      Baseline = baseline
                      Rotation = rotation }

        let sideName =
            match a.Side with
            | AxisBottom -> "bottom"
            | AxisTop -> "top"
            | AxisLeft -> "left"
            | AxisRight -> "right"

        Annotated(
            { Role = RoleAxis
              Label = a.Spec.Title
              Data =
                [| "side", sideName
                   "unit", a.Spec.UnitText
                   "prefix", a.Spec.Prefix.Name
                   "ticks", string kept.Length
                   "tick-rule", a.Plan.Rule |] },
            Scene.prune (Group [| spine; marks; labels; title |])
        )

    // ---- Gridlines, panel frame, panel background -----------------------------------------

    /// The affine that maps panel-local nd (the space `Coord` works in) into figure nd.
    let panelTransform (panel: Rect<nd>) : Affine<nd, nd> = Affine.fromRects Rect.unit panel

    /// Gridlines, **through `Coord.Grid`**.
    ///
    /// §4: "Cartesian is the identity `Coord`. Polar warps and gives circular gridlines … and
    /// nothing else in the system needs to know polar exists." That property is what this
    /// function is protecting: it asks the coord for the paths and never draws a straight line
    /// itself, so a polar panel's gridlines are arcs without a line of polar-aware code here.
    ///
    /// All of them are one `Path` node with many subpaths, not one node per line.
    let gridlines (theme: Theme) (coord: Coord) (panel: Rect<nd>) (xs: float<nd>[]) (ys: float<nd>[]) : Node =
        if xs.Length = 0 && ys.Length = 0 then
            Scene.empty
        else
            let paths = coord.Grid { X = xs; Y = ys; Samples = 64 }

            if paths.Length = 0 then
                Scene.empty
            else
                let stroke =
                    { Stroke.solid theme.GridColor theme.GridWidth with
                        Dash = theme.GridDash }

                let segs = Array.concat paths

                Annotated(
                    { Role = RoleDecoration
                      Label = "gridlines"
                      Data = [| "lines", string paths.Length |] },
                    Transform(panelTransform panel, Path(segs, Some stroke, None, NonZero))
                )

    /// The panel's own boundary shape, from the coord — a rectangle under cartesian, a circle
    /// under polar. Used for the background fill, the frame, and the `Clip` node.
    let panelShape (coord: Coord) : PathSeg[] = coord.Clip

    let panelBackground (theme: Theme) (coord: Coord) (panel: Rect<nd>) : Node =
        if theme.PanelBackground = theme.Background then
            Scene.empty
        else
            Annotated(
                { Role = RoleDecoration
                  Label = "panel"
                  Data = [||] },
                Transform(panelTransform panel, Path(panelShape coord, None, Some(Solid theme.PanelBackground), NonZero))
            )

    let panelFrame (theme: Theme) (coord: Coord) (panel: Rect<nd>) : Node =
        if not theme.PanelFrame then
            Scene.empty
        else
            Annotated(
                { Role = RoleDecoration
                  Label = "panel frame"
                  Data = [||] },
                Transform(panelTransform panel, Path(panelShape coord, Some(Stroke.solid theme.AxisColor theme.AxisWidth), None, NonZero))
            )
