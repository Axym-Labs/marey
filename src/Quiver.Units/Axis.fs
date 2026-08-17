namespace Quiver

/// §9 — a fully resolved axis: everything a backend needs to draw one, and nothing it has to
/// decide for itself.
///
/// "`x (fun r -> r.Time)` where `Time : float<s>` gives you a linear scale over seconds with
/// the axis labelled `t [s]`, with no configuration. That is the user-visible payoff of §2, and
/// it should be the first thing in the README."
type AxisSpec =
    { /// The finished title: `t [s]`, `x [nm]`, or bare `x` when the quantity is dimensionless.
      Title: string
      /// The quantity name alone, as supplied by the caller.
      Label: string
      /// The unit alone, in the requested style, prefix included. Empty when dimensionless.
      UnitText: string
      /// The prefix the data range selected. `Prefix.none` when no rescaling happened.
      Prefix: Prefix
      /// Tick values, positions and labels. **`Value` is the rescaled number that `Label`
      /// prints** — 1.0 for a tick at 1 nm, not 1e-9 — so that `Value` and `Label` can never
      /// disagree. `Position` is untouched normalized panel space: rescaling is a relabelling,
      /// it never moves a tick.
      Ticks: TickLabel[] }

/// §9 — the axis compiler. Turns a `Scale<'u>` into an `AxisSpec` with no configuration.
///
/// ### Why the input is `Scale<'u>` and not `IScaleErased`
///
/// `IScaleErased` is the right input for everything *below* the erasure boundary, and it is
/// deliberately the wrong input here, for two independent reasons.
///
/// 1. **It has already formatted the labels.** `Erase.scale` computes tick text with a decimal
///    count derived from the *unrescaled* step, so ticks at 1e-9 m come out as
///    `"0.000000001"`. §2.2 asks for `1, 2, 3, 4, 5`. Prefix selection has to happen before
///    formatting, so the axis compiler has to own the formatting, so it needs the raw
///    `TickHint`.
/// 2. **It has thrown away the metadata the prefix policy runs on.** `IScaleErased` exposes
///    `Symbol` and `Dim` but not `ToSI` or `OffsetSI` — so it cannot tell electronvolts from
///    joules (same `Dim`, factor 1.6e-19 apart) and cannot tell °C from K (same `Dim`, affine).
///    An erased eV axis would be labelled `kJ`, confidently and wrongly.
///
/// Point 2 is a **gap in `Quiver.Core`**, not a preference: `IScaleErased` should carry the
/// witness's `ToSI`/`OffsetSI` alongside `Symbol` and `Dim`, and §15's reloaded-figure story
/// ("reloading gives you `IScaleErased`, not `Scale<'u>`") means a reloaded non-SI axis
/// currently cannot be relabelled correctly. `ofErased` below is provided anyway, with that
/// limitation stated, because it is right for SI-coherent axes — which is most of them.
///
/// `ofPacked` is the third door and the correct one whenever the packed value still exists:
/// §2.3's rank-2 encoding hands the measure back, so the full `Scale<'u>` path applies.
[<RequireQualifiedAccess>]
module Axis =

    /// The default tick target.
    ///
    /// Five, not six or ten. It is a *target*, not a count — `Ticks.linear` walks the 1-2-5
    /// ladder and returns whatever falls out — but the choice is load-bearing for §2.2's worked
    /// example: over 1e-9 … 5e-9 a target of 5 picks a step of 1 nm and yields exactly
    /// `1, 2, 3, 4, 5`, while a target of 6 picks 0.5 nm and yields `1.0, 1.5, … 5.0`. Both are
    /// legitimate axes; only one is the one the design asks for, and a sparser axis is the
    /// better publication default anyway.
    [<Literal>]
    let DefaultTickCount = 5

    /// `x` + `nm` becomes `x [nm]`; `x` + nothing stays `x`. The bracket convention is the one
    /// §9 uses, and dropping it for dimensionless quantities is what keeps a probability axis
    /// from being labelled `p [1]`.
    let title (label: string) (unitText: string) =
        if System.String.IsNullOrEmpty unitText then label
        elif System.String.IsNullOrEmpty label then "[" + unitText + "]"
        else label + " [" + unitText + "]"

    /// Decimals that make the labels injective (§4) *after* prefix rescaling.
    ///
    /// Log ticks are not on a uniform grid, so a single step-derived count is wrong for them;
    /// take the finest any individual tick needs. This mirrors `Erase.scale` exactly — the
    /// difference is only that it runs on the rescaled values.
    let private decimalsFor (kind: ScaleKind) (step: float) (values: float[]) =
        match kind with
        | ScaleKind.Log _ ->
            values
            |> Array.fold
                (fun acc v ->
                    let a = abs v
                    if a = 0.0 then acc else max acc (Ticks.decimalsFor a))
                0
        | _ -> Ticks.decimalsFor step

    /// The full form: choose the registry, the style, the tick count and the label.
    let ofScaleWith<[<Measure>] 'u>
        (style: UnitStyle)
        (registry: UnitRegistry)
        (tickCount: int)
        (label: string)
        (scale: Scale<'u>)
        : AxisSpec =
        let lo, hi = scale.Domain
        let prefix = Prefix.selectForUnit scale.Unit (float lo, float hi)
        let factor = Prefix.factor prefix

        let raw = scale.TickHint tickCount
        let rescaled = raw |> Array.map (fun v -> float v / factor)

        let decimals =
            decimalsFor scale.Kind (Ticks.linearStep tickCount scale.Domain / factor) rescaled

        let ticks =
            Array.init raw.Length (fun i ->
                { Value = rescaled.[i]
                  Position = scale.Forward raw.[i]
                  Label = Ticks.format decimals rescaled.[i] })

        let unitText = UnitDisplay.unitText style registry prefix scale.Unit

        { Title = title label unitText
          Label = label
          UnitText = unitText
          Prefix = prefix
          Ticks = ticks }

    /// §9's payoff, in one call: `Axis.auto Unicode "x" xScale`.
    ///
    /// Registry is `UnitRegistry.si`, tick target is `DefaultTickCount`, prefix selection is
    /// automatic, and the tick *values* are rescaled to match the prefix — which is the whole
    /// point, and the half that a naive implementation forgets.
    let auto<[<Measure>] 'u> (style: UnitStyle) (label: string) (scale: Scale<'u>) : AxisSpec =
        ofScaleWith style UnitRegistry.si DefaultTickCount label scale

    /// Recover the measure from §2.3's rank-2 package and take the `Scale<'u>` path. This is
    /// the right way to build an axis from a heterogeneous `PackedSeries list`.
    let ofPackedWith (style: UnitStyle) (registry: UnitRegistry) (tickCount: int) (label: string) (packed: PackedScale) =
        packed.Accept
            { new IScaleConsumer<AxisSpec> with
                member _.Apply<[<Measure>] 'u>(s: Scale<'u>) =
                    ofScaleWith style registry tickCount label s }

    let ofPacked (style: UnitStyle) (label: string) (packed: PackedScale) =
        ofPackedWith style UnitRegistry.si DefaultTickCount label packed

    /// The reloaded-figure path (§15). **Correct for SI-coherent axes only** — see the module
    /// note: `IScaleErased` drops `ToSI` and `OffsetSI`, so an erased electronvolt axis is
    /// indistinguishable from a joule axis and will be labelled as one. Use `ofScaleWith` or
    /// `ofPackedWith` whenever the measure still exists.
    ///
    /// Labels are recomputed from `TickLabel.Value` rather than reused, because the erased
    /// labels were formatted before any prefix was chosen.
    let ofErasedWith
        (style: UnitStyle)
        (registry: UnitRegistry)
        (tickCount: int)
        (label: string)
        (scale: IScaleErased)
        : AxisSpec =
        let prefix = Prefix.selectFor scale.Dim scale.Domain
        let factor = Prefix.factor prefix
        let raw = scale.Ticks tickCount
        let rescaled = raw |> Array.map (fun t -> t.Value / factor)

        // `float = float<1>`, so the raw erased domain feeds the measure-generic tick helpers
        // unchanged.
        let decimals =
            decimalsFor scale.Kind (Ticks.linearStep tickCount scale.Domain / factor) rescaled

        let ticks =
            Array.init raw.Length (fun i ->
                { Value = rescaled.[i]
                  Position = raw.[i].Position
                  Label = Ticks.format decimals rescaled.[i] })

        let unitText = UnitDisplay.renderPrefixed style registry prefix scale.Dim

        { Title = title label unitText
          Label = label
          UnitText = unitText
          Prefix = prefix
          Ticks = ticks }

    let ofErased (style: UnitStyle) (label: string) (scale: IScaleErased) =
        ofErasedWith style UnitRegistry.si DefaultTickCount label scale
