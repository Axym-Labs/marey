namespace Quiver

/// A tick, resolved: where it sits, what it is, and what to print.
///
/// Carrying `Position` alongside `Value` means the axis compiler never re-invokes `Project`,
/// and — more importantly — a reloaded figure whose scale is gone (§15) still knows where its
/// ticks belong.
[<Struct>]
type TickLabel =
    { Value: float
      Position: float<nd>
      Label: string }

/// §2.3 — **The erasure boundary.**
///
/// "The IR must not be measure-generic — a `Scene` holding `float<'u>` could never be put in a
/// list with a differently-united scene. Erase *once*, at the point where a series becomes a
/// scale."
///
/// Everything above this interface is `float<'u>`; everything below it is `float` plus a `Dim`.
/// There is exactly one implementation (`Erase.scale`), and that is the point: one boundary,
/// crossed in one direction, in one place.
type IScaleErased =
    /// The unit's *declared* symbol. Presentation only — see §12.6.
    abstract Symbol: string
    /// The truth about this axis's physical dimension.
    abstract Dim: Dim

    /// Multiplicative factor to SI base units, and the additive offset for affine units.
    ///
    /// These two are what make this interface the **whole** erased witness rather than half
    /// of one. `Symbol` and `Dim` alone cannot tell an electronvolt axis from a joule axis
    /// (identical `Dim`) or a Celsius axis from a kelvin one (identical `Dim`, and `Symbol`
    /// is explicitly untrusted). Labelling either from `Dim` would then be wrong by 10^19 in
    /// the first case and by 273.15 in the second.
    ///
    /// §15 makes this load-bearing rather than tidy: "reloading gives you `IScaleErased`, not
    /// `Scale<'u>`". Without these fields a reloaded non-SI axis can never be relabelled
    /// correctly, because the information was destroyed at save time rather than merely
    /// hidden. `ToSI <> 1.0` is also the principled test for "this unit is not SI-coherent,
    /// so do not render its symbol from `Dim`".
    abstract ToSI: float

    abstract OffsetSI: float

    abstract Kind: ScaleKind
    /// Raw domain endpoints, in the scale's own unit.
    abstract Domain: float * float
    /// Data value (in the scale's unit) to normalized panel space.
    abstract Project: float -> float<nd>
    /// Panel space back to a data value. Hit-testing, tooltips and drag-to-zoom need it, and
    /// omitting it is how interaction ends up reimplementing scale inversion by hand.
    abstract Invert: float<nd> -> float
    abstract Ticks: int -> TickLabel[]

/// §2.3 — A dimensioned series, struct-of-arrays.
///
/// The design sketches this as `(float<'ux> * float<'uy>) []`. An array of reference tuples
/// allocates one heap object per point: a 100 000-point scatter becomes 100 000 objects, which
/// is precisely the GC pressure §1.3 warns will "destroy a 60 fps loop", and it contradicts
/// §8.3's "struct-of-arrays layout" rule for the same data one layer down.
///
/// Two parallel `float[]`s cost nothing, vectorise, and are what the batched `Markers` node
/// (§5.1) wants anyway.
type Series<[<Measure>] 'ux, [<Measure>] 'uy> =
    { Name: string
      X: float<'ux>[]
      Y: float<'uy>[]
      XScale: Scale<'ux>
      YScale: Scale<'uy> }

/// §2.3 / §12.5 — Rank-2 consumer. Both measures come back, fully typed, inside `Apply`.
///
/// This is the mechanism that "makes a multi-panel figure with heterogeneous physical
/// quantities possible at all": F# has no existential types, so a universally quantified
/// method on an interface is the encoding, and an object expression is how you write one.
type ISeriesConsumer<'r> =
    abstract Apply<[<Measure>] 'ux, [<Measure>] 'uy> : Series<'ux, 'uy> -> 'r

/// The existential package. A `PackedSeries list` holds `(m, m/s)` next to `(s, J)`.
type PackedSeries =
    abstract Accept: ISeriesConsumer<'r> -> 'r

/// Same encoding for a single scale, for the axis compiler.
type IScaleConsumer<'r> =
    abstract Apply<[<Measure>] 'u> : Scale<'u> -> 'r

type PackedScale =
    abstract Accept: IScaleConsumer<'r> -> 'r

/// Same encoding for one dimensioned value plus its witness — a tooltip readout, a fitted
/// slope, a reported invariant.
type IQuantityConsumer<'r> =
    abstract Apply<[<Measure>] 'u> : U<'u> * float<'u> -> 'r

type PackedQuantity =
    abstract Accept: IQuantityConsumer<'r> -> 'r

[<RequireQualifiedAccess>]
module Erase =

    /// **The** erasure. The only sanctioned crossing from measured to unmeasured space.
    ///
    /// Tick labels are formatted here with the decimal count derived from the tick step, which
    /// is what makes them injective (§4). `Quiver.Units` layers prefix selection on top by
    /// wrapping the result; it does not need its own copy of this.
    let scale (s: Scale<'u>) : IScaleErased =
        { new IScaleErased with
            member _.Symbol = s.Unit.Symbol
            member _.Dim = s.Unit.Dim
            member _.ToSI = s.Unit.ToSI
            member _.OffsetSI = s.Unit.OffsetSI
            member _.Kind = s.Kind

            member _.Domain =
                let lo, hi = s.Domain
                float lo, float hi

            member _.Project x =
                s.Forward(LanguagePrimitives.FloatWithMeasure<'u> x)

            member _.Invert t = float (s.Inverse t)

            member _.Ticks n =
                let vs = s.TickHint n

                let decimals =
                    match s.Kind with
                    | ScaleKind.Log _ ->
                        // Log ticks are not on a uniform grid, so a single step-derived
                        // decimal count is wrong. Take the finest any individual tick needs.
                        vs
                        |> Array.fold
                            (fun acc v ->
                                let a = abs (float v)
                                if a = 0.0 then acc else max acc (Ticks.decimalsFor a))
                            0
                    | _ -> Ticks.decimalsFor (Ticks.linearStep n s.Domain)

                vs
                |> Array.map (fun v ->
                    { Value = float v
                      Position = s.Forward v
                      Label = Ticks.format decimals (float v) }) }

    let series (name: string) (sx: Scale<'ux>) (sy: Scale<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : PackedSeries =
        if x.Length <> y.Length then
            invalidArg "y" (sprintf "Quiver: series '%s' has %d x values and %d y values." name x.Length y.Length)

        let s: Series<'ux, 'uy> =
            { Name = name
              X = x
              Y = y
              XScale = sx
              YScale = sy }

        { new PackedSeries with
            member _.Accept c = c.Apply<'ux, 'uy> s }

    let packSeries (s: Series<'ux, 'uy>) : PackedSeries =
        { new PackedSeries with
            member _.Accept c = c.Apply<'ux, 'uy> s }

    let packScale (s: Scale<'u>) : PackedScale =
        { new PackedScale with
            member _.Accept c = c.Apply<'u> s }

    let packQuantity (u: U<'u>) (x: float<'u>) : PackedQuantity =
        { new PackedQuantity with
            member _.Accept c = c.Apply<'u>(u, x) }

    /// Project a packed series straight to normalized panel space, dropping the measures.
    /// The common path: a geom that only needs positions never has to write a consumer.
    let projectXY (p: PackedSeries) : Vec2<nd>[] =
        p.Accept
            { new ISeriesConsumer<Vec2<nd>[]> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) =
                    Array.init s.X.Length (fun i ->
                        { X = s.XScale.Forward s.X.[i]
                          Y = s.YScale.Forward s.Y.[i] }) }

    let seriesName (p: PackedSeries) =
        p.Accept
            { new ISeriesConsumer<string> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) = s.Name }

    /// The erased scales of a packed series, for the axis compiler.
    let seriesScales (p: PackedSeries) =
        p.Accept
            { new ISeriesConsumer<IScaleErased * IScaleErased> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) = scale s.XScale, scale s.YScale }
