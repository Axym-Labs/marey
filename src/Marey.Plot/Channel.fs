namespace Marey

/// D46 / D49 — **how one non-positional channel turns data into appearance.**
///
/// Four cases, and the pairing is not an accident: a category can drive colour or shape, a
/// quantity can drive colour or size, and nothing else is possible. There is no continuous
/// shape (a shape does not interpolate) and no categorical size (a size that is not ordered
/// says nothing), so the DU has exactly the four arms the two halves of §9's channel list
/// allow.
///
/// The **categorical** arms index by level number, exactly as `ScaleKind.Ordinal` does (D45):
/// `Levels` is what the key reached before the erasure boundary, and a channel's values are
/// level numbers by the time they arrive here. The **continuous** arms carry a `PackedScale`
/// rather than a domain pair, because the scale is what gives the colorbar its ticks, its
/// prefix and its unit — a colour channel over `float<K>` labels its bar `T [K]` because the
/// witness came this far (D49).
[<NoComparison>]
type ChannelMap =
    /// One colour per level. `levels` is index-aligned with `colors` and is what the legend
    /// prints.
    | ColorByLevel of levels: string[] * colors: Color[]
    /// One marker shape per level — §9's redundant encoding, and the reason `MarkerShape` is
    /// on the batched node at all (D5).
    | ShapeByLevel of levels: string[] * shapes: MarkerShape[]
    /// A ramp over a dimensioned scale. Values are raw, in the scale's own unit.
    | ColorByValue of scale: PackedScale * ramp: Ramp
    /// Marker diameter interpolated between two device sizes over a dimensioned scale.
    | SizeByValue of scale: PackedScale * smallest: float<px> * largest: float<px>

/// §9 — **one non-positional channel**: a value per point, a mapping, and the quantity's name.
///
/// A record rather than a tuple because the name is load-bearing on the continuous arms: it is
/// the `Label` the axis compiler puts in front of the unit, so a colorbar over kelvin can read
/// `T [K]` rather than `[K]`.
[<NoComparison>]
type Channel =
    { /// The quantity name, for a colorbar's title or a size key's entries. Empty is
      /// legitimate — an unnamed continuous channel labels its bar `[K]`, the same way an
      /// unnamed axis would.
      Label: string
      /// One value per point of the layer this channel belongs to: a **level number** under a
      /// categorical map, a raw data value in the scale's unit under a continuous one.
      Values: float[]
      Map: ChannelMap }

/// D46 — **the quantiser, and the grouping it makes possible.**
///
/// The problem this module exists for is stated in §5.1 and D11: `MarkerBatch` carries one
/// paint for the whole batch, so a colour that varies per point has nowhere to live. The two
/// obvious answers are both bad — per-point arrays on the IR node is one variant against six
/// backends, and one batch per point defeats the batching in exactly the case that motivated
/// it (measured: 310 ms and 13 MB of TeX for 100 000 points).
///
/// D46's answer is that neither is needed, because a continuous channel does not have to reach
/// the output at higher resolution than the output has. A 256-step ramp is off by 0.00180 in
/// Oklab; the 8-bit hex both backends write is off by 0.00187 anyway. So the channel is
/// **quantised into buckets**, points are grouped by bucket, and each group is one ordinary
/// batch with one ordinary paint. 100 000 points cost at most 257 nodes and no IR change.
///
/// The same argument gives the size step: half a device pixel, because two marks whose
/// diameters differ by less than that are the same mark on screen and on paper.
///
/// ### Why the bucket key is an integer and not a tuple
///
/// Several channels can drive one layer at once — colour *and* shape over the same levels is
/// §9's colour-blind-safe scatter, and it must not produce the product of the two batch counts
/// when both key off the same levels. So a point's group is the combination of its buckets
/// across every channel, mixed into one `int64` by known strides. A tuple or a list would be
/// correct and would allocate one object per point, which is the GC pressure §1.3 warns about
/// in the one place this library promises not to create it.
[<RequireQualifiedAccess>]
module Channel =

    /// D46 — steps along a colour ramp. Below the 8-bit hex the backends write, so the
    /// quantisation is invisible in the output by construction.
    [<Literal>]
    let ColorSteps = 256

    /// D46 — the size quantum. Half a device pixel.
    let SizeStep = 0.5<px>

    let private clamp01 (x: float) = if x < 0.0 then 0.0 elif x > 1.0 then 1.0 else x

    /// **The diameter a size channel gives to a value at normalized position `t`.**
    ///
    /// Interpolated in **area**, not in diameter, and that is the whole content of the
    /// function: what the eye reads off a circle is roughly its area, so a linear ramp of
    /// diameters overstates the top of the range by the square. A value halfway up the domain
    /// gets a mark of halfway-between area, which is the encoding a reader will actually
    /// decode. Cleveland's ranking puts area some way below position for accuracy, which is why
    /// size is a channel of last resort here rather than a default.
    let sizeAt (smallest: float<px>) (largest: float<px>) (t: float) : float<px> =
        let a0 = smallest * smallest
        let a1 = largest * largest
        sqrt (float (a0 + (a1 - a0) * clamp01 t)) * 1.0<px>

    /// A marker diameter, rounded to the size quantum. The one place `SizeStep` is applied, so
    /// that a size key's swatch is drawn at the size the marks it describes are drawn at.
    let quantiseSize (size: float<px>) : float<px> =
        float (max 0 (int (round (size / SizeStep)))) * SizeStep

    let private finite (x: float) =
        not (System.Double.IsNaN x) && not (System.Double.IsInfinity x)

    /// The erased side of a packed scale — the projection a channel needs, and the same
    /// interface a reloaded figure would have (§15).
    let erasedScale (packed: PackedScale) : IScaleErased =
        packed.Accept
            { new IScaleConsumer<IScaleErased> with
                member _.Apply<[<Measure>] 'u>(s: Scale<'u>) = Erase.scale s }

    /// §9 / D48 — **one facet's share of a channel**: the same mapping, over a subset of the
    /// rows.
    ///
    /// `Map` is deliberately untouched. It carries the level set and its colours, or the scale
    /// and its ramp, and all of those were computed over the *whole* block before it was split —
    /// so `treated` is the same colour in every panel, the colorbar covers the whole sample, and
    /// a panel that happens to contain none of a level still leaves room for it in the legend.
    /// Recomputing the map per panel would renumber the levels underneath values that are already
    /// level *numbers* (D45), which is the one way a facet can quietly draw the right marks
    /// against the wrong labels.
    let select (rows: int[]) (c: Channel) : Channel =
        { c with
            Values = rows |> Array.map (fun i -> c.Values.[i]) }

    /// How many buckets a mapping has. The stride of every channel to its left, and the bound
    /// the exit criterion is written against.
    let bucketCount (map: ChannelMap) =
        match map with
        | ColorByLevel(_, colors) -> max 1 colors.Length
        | ShapeByLevel(_, shapes) -> max 1 shapes.Length
        | ColorByValue _ -> ColorSteps
        | SizeByValue(_, _, largest) -> int (ceil (largest / SizeStep)) + 1

    /// **The bucketing function for a mapping**, with everything scale-dependent computed once.
    ///
    /// A function returned rather than a function of two arguments, because the continuous arms
    /// have to cross the erasure boundary to project a value and that crossing is an object
    /// expression plus a rank-2 dispatch. Doing it per point would put 100 000 allocations in
    /// the one path this library promises not to allocate in (§1.3, §8.3); doing it per
    /// *channel* costs one.
    ///
    /// The result is `-1` for a value that has **no bucket**, and non-finite is the case that
    /// matters: a NaN in a colour channel has no colour, and the two quiet alternatives are
    /// both lies — clamping it to the ramp's low end paints missing data as a real
    /// measurement, and giving it the foreground colour invents a category. So it is dropped
    /// from every batch, which is what a masked value does elsewhere in the field and the only
    /// option that does not put a wrong mark on the page. `split` reports how many.
    let bucketer (map: ChannelMap) : float -> int =
        let level (n: int) (value: float) =
            let i = int (round value)
            if i < 0 || i >= n then -1 else i

        match map with
        | ColorByLevel(_, colors) -> fun v -> if finite v then level colors.Length v else -1
        | ShapeByLevel(_, shapes) -> fun v -> if finite v then level shapes.Length v else -1
        | ColorByValue(scale, _) ->
            let erased = erasedScale scale

            fun v ->
                if not (finite v) then
                    -1
                else
                    let t = clamp01 (float (erased.Project v))
                    min (ColorSteps - 1) (int (t * float ColorSteps))
        | SizeByValue(scale, smallest, largest) ->
            let erased = erasedScale scale

            fun v ->
                if not (finite v) then
                    -1
                else
                    let t = float (erased.Project v)
                    int (quantiseSize (sizeAt smallest largest t) / SizeStep)

    /// One value's bucket. The convenient form; `bucketer` is the one a loop should use.
    let bucketOf (map: ChannelMap) (value: float) : int = bucketer map value

    /// Where a bucket sits along its ramp: the **midpoint** of the step, never its edge.
    ///
    /// The midpoint is what makes D49's colorbar agree with the marks rather than merely
    /// resemble them. A strip drawn for bucket `i` and a mark assigned bucket `i` take their
    /// colour from the same call, so the two cannot drift apart when the step count changes.
    let stepMidpoint (steps: int) (i: int) = (float i + 0.5) / float steps

    /// Apply one bucket of one channel to a style. Every channel is a fold step over the
    /// layer's base style, which is what lets colour and shape stack on one layer.
    let restyle (map: ChannelMap) (bucket: int) (st: GeomStyle) : GeomStyle =
        if bucket < 0 then
            st
        else
            match map with
            | ColorByLevel(_, colors) -> { st with Color = colors.[bucket % colors.Length] }
            | ShapeByLevel(_, shapes) -> { st with Shape = shapes.[bucket % shapes.Length] }
            | ColorByValue(_, ramp) ->
                { st with
                    Color = Ramp.at ramp (stepMidpoint ColorSteps bucket) }
            | SizeByValue _ ->
                { st with
                    MarkerSize = float bucket * SizeStep }

    /// The levels a categorical mapping enumerates, for the legend.
    let levelsOf (ch: Channel) =
        match ch.Map with
        | ColorByLevel(levels, _)
        | ShapeByLevel(levels, _) -> Some levels
        | ColorByValue _
        | SizeByValue _ -> None

    let isCategorical (ch: Channel) = (levelsOf ch).IsSome

    /// One batch of a split layer: which points, and how their style differs from the layer's.
    [<NoComparison>]
    type Batch =
        { /// Indices into the layer's point array, ascending.
          Indices: int[]
          /// The layer's base style with every channel's bucket applied.
          Restyle: GeomStyle -> GeomStyle }

    /// **Group `n` points into batches by the combination of every channel's bucket.**
    ///
    /// Batches come out in ascending key order, which makes the output a function of the data
    /// alone: §13's determinism gate compares two renders of the same figure byte for byte, and
    /// a `Dictionary`'s enumeration order would not survive that.
    ///
    /// Returns the batches and the number of points that had no bucket — see `bucketOf`.
    let split (channels: Channel[]) (n: int) : Batch[] * int =
        if channels.Length = 0 then
            [| { Indices = Array.init n id
                 Restyle = id } |],
            0
        else
            let maps = channels |> Array.map (fun c -> c.Map)
            let counts = maps |> Array.map bucketCount
            let bucketers = maps |> Array.map bucketer

            // Strides, right to left: bucket 3 of channel 1 and bucket 3 of channel 0 must not
            // collide, and the key must stay inside an int64 for any bucket count this module
            // can produce (256 colours x 7 shapes x a few hundred sizes).
            let strides = Array.create channels.Length 1L

            for i in channels.Length - 2 .. -1 .. 0 do
                strides.[i] <- strides.[i + 1] * int64 counts.[i + 1]

            let groups = System.Collections.Generic.Dictionary<int64, ResizeArray<int>>()
            let mutable unmapped = 0

            for p in 0 .. n - 1 do
                let mutable key = 0L
                let mutable ok = true

                for c in 0 .. channels.Length - 1 do
                    let values = channels.[c].Values

                    let b =
                        if p < values.Length then
                            bucketers.[c] values.[p]
                        else
                            -1

                    if b < 0 then ok <- false else key <- key + int64 b * strides.[c]

                if not ok then
                    unmapped <- unmapped + 1
                else
                    match groups.TryGetValue key with
                    | true, xs -> xs.Add p
                    | _ ->
                        let xs = ResizeArray<int>()
                        xs.Add p
                        groups.[key] <- xs

            let batches =
                groups
                |> Seq.sortBy (fun kv -> kv.Key)
                |> Seq.map (fun kv ->
                    let key = kv.Key

                    let restyle (st: GeomStyle) =
                        let mutable acc = st

                        for c in 0 .. channels.Length - 1 do
                            let b = int ((key / strides.[c]) % int64 counts.[c])
                            acc <- restyle maps.[c] b acc

                        acc

                    { Indices = kv.Value.ToArray()
                      Restyle = restyle })
                |> Seq.toArray

            batches, unmapped

    // ---- Constructors ---------------------------------------------------------------------

    /// **The colours a theme gives `n` levels.**
    ///
    /// Named rather than inlined because two callers have to agree on it: the constructor
    /// below, and `Chart.withTheme`, which recolours a channel only when its colours are still
    /// exactly what the outgoing theme would have chosen. That is the same machine-checkable
    /// definition of "the user did not pick these" that the layer styles use, and it needs both
    /// sides to compute the answer the same way.
    let levelColors (theme: Theme) (n: int) : Color[] =
        Array.init n (Palette.pick theme.Categorical)

    /// The shapes a theme gives `n` levels, cycling its shape list alongside its palette (D5).
    let levelShapes (theme: Theme) (n: int) : MarkerShape[] =
        let shapes =
            if theme.MarkerShapes.Length = 0 then
                [| MarkCircle |]
            else
                theme.MarkerShapes

        Array.init n (fun i -> shapes.[i % shapes.Length])

    /// A categorical colour channel over the theme's categorical palette.
    let colorByLevel (theme: Theme) (levels: string[]) (values: float[]) : Channel =
        { Label = ""
          Values = values
          Map = ColorByLevel(levels, levelColors theme levels.Length) }

    /// A categorical shape channel over the theme's shape cycle. Paired with `colorByLevel` on
    /// the same levels it is §9's redundant encoding, and the two produce one batch per level
    /// rather than one per combination — the level number is the same in both.
    let shapeByLevel (theme: Theme) (levels: string[]) (values: float[]) : Channel =
        { Label = ""
          Values = values
          Map = ShapeByLevel(levels, levelShapes theme levels.Length) }

    /// A continuous colour channel. The scale carries the unit, so the colorbar labels itself.
    let colorByValue (label: string) (ramp: Ramp) (scale: Scale<'u>) (values: float<'u>[]) : Channel =
        { Label = label
          Values = values |> Array.map float
          Map = ColorByValue(Erase.packScale scale, ramp) }

    /// A continuous size channel, between two device diameters.
    let sizeByValue
        (label: string)
        (smallest: float<px>)
        (largest: float<px>)
        (scale: Scale<'u>)
        (values: float<'u>[])
        : Channel =
        { Label = label
          Values = values |> Array.map float
          Map = SizeByValue(Erase.packScale scale, smallest, largest) }
