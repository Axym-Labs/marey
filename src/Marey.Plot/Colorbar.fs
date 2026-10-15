namespace Marey

/// D49 — **the key a continuous colour channel gets, and why it is not a legend entry.**
///
/// A legend *enumerates*: `n` rows, each a name beside a swatch. A colorbar *maps*: a domain
/// along a strip, read by position. D46 makes that split unavoidable rather than stylistic — a
/// continuous colour channel is 256 quantised steps, and enumerating 256 rows is not a legend.
/// So this is a sibling of `Legend`, sharing the theme, the placement and `Layout`, and sharing
/// with it the fact that neither is an IR node (§5.5).
///
/// ### Three things this module does not do, and each is a decision
///
/// **It never emits a gradient.** The obvious implementation is one rect with a
/// `LinearGradient` fill, and run through the TikZ backend's own capabilities that rect comes
/// out as a single flat `#23908c` block: `Gradients = false`, so `Degrade.forCaps` collapses
/// the ramp to its Oklab midpoint (D10). Valid TeX, clean golden diff, no colorbar — D33's
/// failure mode exactly. The bar is strips, and the strips come from the same
/// `Channel.stepMidpoint` the marks do, so the bar and the data cannot disagree.
///
/// **It does not label itself.** Ticks, values, prefix and title are `Axes.compile`'s, against
/// the strip as its panel. A colour channel over `float<K>` prints `T [K]` with nothing
/// configured, which is §2's payoff arriving in a third place after the axis and the legend.
///
/// **It does not choose where it goes.** `Figure` gives it a `Track.Content` column and hands
/// it the rect. `Legend.fs` records that outside placement was kept out of M0 because it
/// changes the panel's width and the tick plan has to be recomputed against it; that objection
/// does not transfer here, because a colorbar's width is its strip, its labels and its title —
/// none of which depends on the plot's width, since its axis runs vertically while the track it
/// adds is horizontal.
[<RequireQualifiedAccess>]
module Colorbar =

    /// The strip, the gap, the tick labels and the title — measured with the axis compiler's
    /// own helpers, so a colorbar's labels are budgeted by the code that budgets an axis's.
    let measure (m: ITextMeasurer) (theme: Theme) (spec: AxisSpec) (plan: TickPlan) : float<px> =
        theme.ColorbarWidth
        + theme.LabelGap
        + Axes.tickExtentWith TickOut m theme AxisRight spec plan
        + Axes.titleExtent m theme spec.Title

    /// The quantity name a colour channel puts in front of its unit.
    ///
    /// The same dimension table an axis uses, minus its positional fallback: an unnamed kelvin
    /// channel is `T`, and an unnamed channel of no recognised dimension is nameless rather
    /// than being called `y`. A colorbar is not a y axis and must not claim to be one.
    let labelOf (label: string) (packed: PackedScale) =
        if not (System.String.IsNullOrEmpty label) then
            label
        else
            match Axes.defaultLabelFor AxisRight (Axes.angleOf packed) (Axes.dimOf packed) with
            | "y" -> ""
            | l -> l

    /// The axis spec for a continuous colour channel, or `None` for any other channel.
    let specOf (style: UnitStyle) (tickCount: int) (ch: Channel) : (AxisSpec * Ramp) option =
        match ch.Map with
        | ColorByValue(scale, ramp) ->
            Some(Axis.ofPackedWith style UnitRegistry.si tickCount (labelOf ch.Label scale) scale, ramp)
        | ColorByLevel _
        | ShapeByLevel _
        | SizeByValue _ -> None

    /// The tick plan for a bar of a given height. The bar's own axis, planned against its own
    /// length, exactly as a y axis is.
    let planTicks (m: ITextMeasurer) (theme: Theme) (height: float<px>) (spec: AxisSpec) : TickPlan =
        Axes.planTicks m theme.TickFont theme.LabelGap height AxisRight spec.Ticks

    /// D46 / D49 — **`min(256, ⌈height in px⌉)`**.
    ///
    /// The same argument D46 makes for the marks: do not encode below the resolution of the
    /// output. Two strips inside one device pixel cannot differ on screen or on paper, and a
    /// reader takes a *position* off a colorbar, not a colour identity.
    let steps (height: float<px>) : int =
        let px = int (ceil (height / 1.0<px>))
        max 1 (min Channel.ColorSteps px)

    /// Compile the bar into the column `Figure` reserved for it.
    ///
    /// `column` is in device px and spans the plot's rows; the strip takes
    /// `Theme.ColorbarWidth` of its left edge and the axis hangs off the strip's right.
    let compile
        (m: ITextMeasurer)
        (theme: Theme)
        (extent: Extent<px>)
        (column: Rect<px>)
        (spec: AxisSpec)
        (plan: TickPlan)
        (ramp: Ramp)
        : Node =
        let height = Rect.height column

        if height <= 0.0<px> || Rect.width column <= 0.0<px> then
            Scene.empty
        else
            let stripPx: Rect<px> =
                { X0 = column.X0
                  Y0 = column.Y0
                  X1 = column.X0 + theme.ColorbarWidth
                  Y1 = column.Y1 }

            let strip = Layout.ndRect extent stripPx
            let n = steps height
            let span = strip.Y1 - strip.Y0

            let bands =
                [| for i in 0 .. n - 1 do
                       let t0 = float i / float n
                       let t1 = float (i + 1) / float n

                       // A hairline of overlap. Adjacent strips that merely touch leave a
                       // seam in every renderer that antialiases edges, and the seam is the
                       // background showing through a bar that is meant to be continuous.
                       let bleed = if i = n - 1 then 0.0 else 0.35 / float n

                       yield
                           Scene.rect
                               None
                               (Some(Solid(Ramp.at ramp (Channel.stepMidpoint n i))))
                               { X0 = strip.X0
                                 Y0 = strip.Y0 + span * t0
                                 X1 = strip.X1
                                 Y1 = strip.Y0 + span * (t1 + bleed) } |]

            let frame =
                Scene.rect (Some(Stroke.solid theme.AxisColor theme.AxisWidth)) None strip

            let axis =
                Axes.compile
                    { Spec = spec
                      Side = AxisRight
                      Plan = plan
                      Panel = strip
                      Extent = extent
                      Theme = theme
                      // The strip's own frame is the spine; a second rule beside it is one
                      // line too many.
                      ShowSpine = false
                      ShowTicks = true
                      ShowLabels = true
                      ShowTitle = true
                      TitleOffset = Axes.tickExtentWith TickOut m theme AxisRight spec plan + theme.TitleGap
                      // A colorbar's ticks point out of the strip whatever the panels do: an inward
                      // tick would draw over the colours it labels.
                      Direction = TickOut
                      Mirror = false
                      ShowMinor = false }

            Annotated(
                { Role = RoleOther "colorbar"
                  Label = spec.Title
                  Data =
                    [| "title", spec.Title
                       "unit", spec.UnitText
                       "steps", string n |] },
                Group [| Group bands; frame; axis |]
            )
