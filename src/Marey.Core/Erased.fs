namespace Marey

/// A tick, resolved: where it sits, what it is, and what to print.
///
/// Carrying `Position` alongside `Value` means the axis compiler never re-invokes `Project`,
/// and — more importantly — a reloaded figure whose scale is gone (§15) still knows where its
/// ticks belong.
[<Struct>]
type TickLabel =
    { Value: float
      Position: float<nd>
      /// The label as plain text, the `Unicode` spelling when a TeX one was asked for.
      Label: string
      /// The label as TeX source, for the axis compiled in the `Latex` or `Siunitx` style whose
      /// label needs markup: `$10^{-8}$` where `Label` is `10⁻⁸`. `None` for plain digits, which
      /// typeset identically either way, and always `None` from `Erase.scale`, which has no
      /// style. The rule is `AxisSpec.TitleTex`'s.
      LabelTex: string option }

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

    /// The unit's composition, when it had one — see `U.Factors`. Carried across the boundary
    /// for the same reason `ToSI` is: a factorisation the caller supplied is information that
    /// `Dim` cannot reconstruct, so dropping it here would mean a reloaded `V·m⁻¹` axis
    /// relabelled itself `kg·m·s⁻³·A⁻¹` while the live one did not. §15 requires the two paths
    /// to agree.
    abstract Factors: (string * int) list

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

    /// D127 — whether the unit was stated, or is `U.unspecified`. An unstated axis makes no
    /// dimensional claim, so the checks that compare two axes skip it — see `U.Stated`.
    abstract Stated: bool

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

/// D44 finding 2 — **one witness and one array**: a single encoding channel, packed before
/// anything knows which pair of channels will become a series.
///
/// `PackedQuantity` above carries one witness and one *value*; `PackedSeries` carries a witness
/// *pair* and two arrays. A channel is neither, and the gap is what the encoding CE (§9) walks
/// into: it accumulates `x`, `y`, `color` … independently, each erased where it is declared,
/// and only at the end decides which two of them are the positional pair. So this is a fourth
/// instance of the rank-2 pattern this file already argues for three times, not a new idea.
type IChannelConsumer<'res> =
    abstract Apply<[<Measure>] 'u> : U<'u> * float<'u>[] -> 'res

type PackedChannel =
    abstract Accept: IChannelConsumer<'res> -> 'res

/// Both measures of a channel *pair*, recovered together.
///
/// D44 finding 3: reunification is rank-2 twice, nested, and the two measures exist together in
/// exactly one scope — the body of the inner `Apply` — and cannot be returned from it. Anything
/// that wants the typed pair (`Erase.series`, `Chart.ofSeries`, a regression whose slope is
/// `float<'uy/'ux>`) has to be *called* at that innermost point. This interface names that call
/// site once, so no caller has to write the nesting a second time.
type IChannelPairConsumer<'res> =
    abstract Apply<[<Measure>] 'ux, [<Measure>] 'uy> : U<'ux> * float<'ux>[] * U<'uy> * float<'uy>[] -> 'res

[<RequireQualifiedAccess>]
module Erase =

    /// The linear scale a channel gets when nobody picks one: the data's range with 5%
    /// breathing room, and a unit interval when there is no data to take a range from. Kept
    /// identical to `Chart.series`'s default, because a series built through the channels must
    /// not render differently from the same series built through the front door.
    ///
    /// Public since M2 phase 4, on the rule stated in `Num.fs`: it acquired a second consumer.
    /// A stat that builds its own series inside `reunify` — a fitted line, which is two points
    /// and not the caller's data — needs the same default, and a private copy of it in
    /// `Marey.Plot` is exactly the duplication that goes silently out of step.
    let autoScale (u: U<'u>) (xs: float<'u>[]) : Scale<'u> =
        match Scale.paddedDomain 0.05 xs with
        | Some d -> Scale.linear u d
        | None -> Scale.linear u (LanguagePrimitives.FloatWithMeasure<'u> 0.0, LanguagePrimitives.FloatWithMeasure<'u> 1.0)

    /// **The** erasure. The only sanctioned crossing from measured to unmeasured space.
    ///
    /// Tick labels are formatted here with the decimal count derived from the tick step, which
    /// is what makes them injective (§4). `Marey.Units` layers prefix selection on top by
    /// wrapping the result; it does not need its own copy of this.
    let scale (s: Scale<'u>) : IScaleErased =
        { new IScaleErased with
            member _.Symbol = s.Unit.Symbol
            member _.Dim = s.Unit.Dim
            member _.Factors = s.Unit.Factors
            member _.ToSI = s.Unit.ToSI
            member _.OffsetSI = s.Unit.OffsetSI
            member _.Stated = s.Unit.Stated
            member _.Kind = s.Kind

            member _.Domain =
                let lo, hi = s.Domain
                float lo, float hi

            member _.Project x =
                s.Forward(LanguagePrimitives.FloatWithMeasure<'u> x)

            member _.Invert t = float (s.Inverse t)

            member _.Ticks n =
                let vs = s.TickHint n

                // How the ticks become text, which is the one thing a categorical axis does
                // differently from every other kind: its labels are names and there is no
                // decimal count to derive. D45 puts the level array in `Kind` precisely so that
                // this path — the erased one, all a reloaded figure has (§15) — can still read
                // them.
                let labels =
                    match s.Kind with
                    | ScaleKind.Ordinal levels -> vs |> Array.map (fun v -> Scale.levelLabel levels (float v))
                    | ScaleKind.Log _ ->
                        // Log ticks are not on a uniform grid, so each takes its own decimals,
                        // and a wide axis is written in powers of ten.
                        let lo, hi = s.Domain
                        Ticks.logLabels (float lo, float hi) (vs |> Array.map float)
                    | _ ->
                        let label = Ticks.format (Ticks.decimalsFor (Ticks.linearStep n s.Domain))
                        vs |> Array.map (fun v -> label (float v))

                vs
                |> Array.mapi (fun i v ->
                    { Value = float v
                      Position = s.Forward v
                      Label = labels.[i]
                      LabelTex = None }) }

    let series (name: string) (sx: Scale<'ux>) (sy: Scale<'uy>) (x: float<'ux>[]) (y: float<'uy>[]) : PackedSeries =
        if x.Length <> y.Length then
            invalidArg "y" (sprintf "Marey: series '%s' has %d x values and %d y values." name x.Length y.Length)

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

    /// How many points a packed series holds. Cheaper than projecting it to find out, which is
    /// what a caller checking that a channel is the same length as its layer would otherwise do.
    let seriesLength (p: PackedSeries) =
        p.Accept
            { new ISeriesConsumer<int> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) = s.X.Length }

    let seriesName (p: PackedSeries) =
        p.Accept
            { new ISeriesConsumer<string> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) = s.Name }

    /// The erased scales of a packed series, for the axis compiler.
    let seriesScales (p: PackedSeries) =
        p.Accept
            { new ISeriesConsumer<IScaleErased * IScaleErased> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) = scale s.XScale, scale s.YScale }

    /// Erase one channel — the witness and the values it binds — for later reunification.
    let packChannel (u: U<'u>) (values: float<'u>[]) : PackedChannel =
        { new PackedChannel with
            member _.Accept c = c.Apply<'u>(u, values) }

    /// **A subset of a channel, taken by row index** — the operation a facet is made of (D48).
    ///
    /// A facet does not re-read the data: by the time the block runs, every array in it has
    /// already been erased where it was declared (D44 finding 2) and the accessors are gone. But
    /// each of those arrays is one `Array.map` over the *same* rows — which is D53's argument for
    /// a channel belonging to the block — so they are all index-aligned, and splitting the block
    /// by a key is splitting one `int[]` of row indices and applying it to each.
    ///
    /// The measure is reattached inside `Apply`, where it is known, so this is measure-safe for a
    /// caller who could not name the measure it selects from.
    let selectChannel (rows: int[]) (c: PackedChannel) : PackedChannel =
        c.Accept
            { new IChannelConsumer<PackedChannel> with
                member _.Apply<[<Measure>] 'u>(u: U<'u>, values: float<'u>[]) =
                    packChannel u (rows |> Array.map (fun i -> values.[i])) }

    /// **The two-channel reunifier.** `IChannelConsumer` nested inside `IChannelConsumer`, the
    /// only construction in which two independently erased measures are both in scope, and `k`
    /// is invoked where they are.
    let reunify (x: PackedChannel) (y: PackedChannel) (k: IChannelPairConsumer<'res>) : 'res =
        x.Accept
            { new IChannelConsumer<'res> with
                member _.Apply<[<Measure>] 'ux>(ux: U<'ux>, xs: float<'ux>[]) =
                    y.Accept
                        { new IChannelConsumer<'res> with
                            member _.Apply<[<Measure>] 'uy>(uy: U<'uy>, ys: float<'uy>[]) = k.Apply<'ux, 'uy>(ux, xs, uy, ys) } }

    /// Reunify two channels into a series over automatic linear scales — the termination the
    /// CE takes when no scale was configured. A caller that wants log, or a domain of its own,
    /// calls `reunify` with its own consumer and builds the scales inside it; there is nowhere
    /// else the measures are available to build them.
    let channelSeries (name: string) (x: PackedChannel) (y: PackedChannel) : PackedSeries =
        reunify
            x
            y
            { new IChannelPairConsumer<PackedSeries> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(ux: U<'ux>, xs: float<'ux>[], uy: U<'uy>, ys: float<'uy>[]) =
                    series name (autoScale ux xs) (autoScale uy ys) xs ys }

    /// The numbers a packed series holds, **in its own unit** — `seriesLength`'s sibling.
    ///
    /// The measures are erased on the way out, which is safe only because `seriesScales` hands
    /// back the two `IScaleErased` that say what they were. Read the two together or not at all:
    /// a bare `float[]` from a series in kilometres is not a length, it is a number.
    let seriesValues (p: PackedSeries) : float[] * float[] =
        p.Accept
            { new ISeriesConsumer<float[] * float[]> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) =
                    s.X |> Array.map float, s.Y |> Array.map float }

    /// Resample to `n` points by **index fraction**, linearly.
    ///
    /// Not by arc length, which is what `Morph` does one layer down and what would be wrong
    /// here: arc length in data space is `sqrt(dx² + dy²)` over a metre and a second, which is
    /// not a quantity. The index is the one parameterisation a series has that does not mix its
    /// two units, and pairing by index is the same rule D74 took for subpaths.
    let private resampleTo (n: int) (xs: float[]) : float[] =
        if xs.Length = n then xs
        elif xs.Length = 0 then Array.zeroCreate n
        elif xs.Length = 1 then Array.create n xs.[0]
        else
            // `n >= xs.Length >= 2` at every call, so `n - 1` and `Length - 1` are both positive.
            Array.init n (fun i ->
                let pos = (float i / float (n - 1)) * float (xs.Length - 1)
                let j = min (xs.Length - 2) (int (floor pos))
                let f = pos - float j
                xs.[j] + (xs.[j + 1] - xs.[j]) * f)

    /// The one interpolation behind `tweenSeries` and `tweenRows`: `b` converted into `a`'s
    /// units, `align` lining the two up point for point, and both interpolated, domains
    /// included. `align` receives `a`'s numbers and `b`'s already converted, and returns four
    /// arrays of one length.
    let private tweenAligned
        (align: float[] * float[] * float[] * float[] -> float[] * float[] * float[] * float[])
        (a: PackedSeries)
        (b: PackedSeries)
        (t: float)
        : PackedSeries =
        let bx, by = seriesScales b
        let bxs, bys = seriesValues b

        a.Accept
            { new ISeriesConsumer<PackedSeries> with
                member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) =
                    // Written twice rather than once over a pair: `'ux` and `'uy` are
                    // different type variables and one shared helper would be monomorphised by
                    // whichever axis reached it first (§12.2).
                    // D127 — an unstated side makes no claim to check.
                    if s.XScale.Unit.Stated && bx.Stated && s.XScale.Unit.Dim <> bx.Dim then
                        invalidArg
                            "b"
                            (sprintf
                                "Marey: cannot morph an x axis in %s into one in %s — different dimensions."
                                bx.Symbol
                                s.XScale.Unit.Symbol)

                    if s.YScale.Unit.Stated && by.Stated && s.YScale.Unit.Dim <> by.Dim then
                        invalidArg
                            "b"
                            (sprintf
                                "Marey: cannot morph a y axis in %s into one in %s — different dimensions."
                                by.Symbol
                                s.YScale.Unit.Symbol)

                    let intoX (v: float) = float (U.ofSI s.XScale.Unit (v * bx.ToSI + bx.OffsetSI))
                    let intoY (v: float) = float (U.ofSI s.YScale.Unit (v * by.ToSI + by.OffsetSI))

                    let ax, ay, px, py =
                        align (s.X |> Array.map float, s.Y |> Array.map float, bxs |> Array.map intoX, bys |> Array.map intoY)

                    let n = ax.Length

                    let inline mix (u: float) (v: float) = u + (v - u) * t

                    let bxLo, bxHi = bx.Domain
                    let byLo, byHi = by.Domain
                    let axLo, axHi = s.XScale.Domain
                    let ayLo, ayHi = s.YScale.Domain

                    let xScale =
                        Scale.withDomain
                            (LanguagePrimitives.FloatWithMeasure<'ux>(mix (float axLo) (intoX bxLo)),
                             LanguagePrimitives.FloatWithMeasure<'ux>(mix (float axHi) (intoX bxHi)))
                            s.XScale

                    let yScale =
                        Scale.withDomain
                            (LanguagePrimitives.FloatWithMeasure<'uy>(mix (float ayLo) (intoY byLo)),
                             LanguagePrimitives.FloatWithMeasure<'uy>(mix (float ayHi) (intoY byHi)))
                            s.YScale

                    packSeries
                        { Name = s.Name
                          X = Array.init n (fun i -> LanguagePrimitives.FloatWithMeasure<'ux>(mix ax.[i] px.[i]))
                          Y = Array.init n (fun i -> LanguagePrimitives.FloatWithMeasure<'uy>(mix ay.[i] py.[i]))
                          XScale = xScale
                          YScale = yScale } }

    /// **Interpolate one packed series into another**, D69's `morphTo` one layer down.
    ///
    /// The result wears `a`'s name, `a`'s units and `a`'s scale kinds, and holds
    /// `max (length a) (length b)` points: both sides are resampled to the longer of the two by
    /// index fraction and then interpolated pointwise, domains included.
    ///
    /// **`b`'s numbers are converted into `a`'s unit through SI**, not read as if they were
    /// already in it. Morphing a series in kilometres into one in metres is a legal thing to
    /// ask for and reading the numbers across would be wrong by a factor of a thousand — the
    /// same hazard `U.convert` exists for, in the one place where neither measure is nameable.
    /// The dimensions must match, and that is the only check there is to make: this is the
    /// runtime edge of the type system, exactly like `U.convert`.
    ///
    /// `t` is **not** clamped (D71): an easing that overshoots reaches here and extrapolates,
    /// which is what makes `Ease.backOut` visible on a morph.
    let tweenSeries (a: PackedSeries) (b: PackedSeries) (t: float) : PackedSeries =
        tweenAligned
            (fun (ax, ay, bx, by) ->
                let n = max ax.Length bx.Length
                resampleTo n ax, resampleTo n ay, resampleTo n bx, resampleTo n by)
            a
            b
            t

    /// **D104 — interpolate row by row, as the caller pairs them.** Each entry of `rows` is a
    /// row of `a` and a row of `b`, either of which may be `-1`: a row of `a` alone holds
    /// still where it is, and a row of `b` alone holds still where it is going. The result has
    /// one point per entry, in that order, and is otherwise `tweenSeries`: `a`'s name and units,
    /// `b` converted through SI, the domains interpolated, `t` not clamped.
    ///
    /// A pairing by key is the caller's: this knows rows, not names.
    let tweenRows (rows: struct (int * int)[]) (a: PackedSeries) (b: PackedSeries) (t: float) : PackedSeries =
        tweenAligned
            (fun (ax, ay, bx, by) ->
                let pick (own: float[]) (other: float[]) (i: int) (j: int) = if i >= 0 then own.[i] else other.[j]

                let side (xs: float[]) (ys: float[]) (xs': float[]) (ys': float[]) (first: bool) =
                    rows
                    |> Array.map (fun (struct (i, j)) -> if first then pick xs xs' i j else pick xs' xs j i),
                    rows
                    |> Array.map (fun (struct (i, j)) -> if first then pick ys ys' i j else pick ys' ys j i)

                if rows |> Array.exists (fun (struct (i, j)) -> (i < 0 && j < 0) || i >= ax.Length || j >= bx.Length) then
                    invalidArg "rows" "Marey: every paired row names a row of one side or both, and no row past the end."

                let ax', ay' = side ax ay bx by true
                let bx', by' = side ax ay bx by false
                ax', ay', bx', by')
            a
            b
            t
