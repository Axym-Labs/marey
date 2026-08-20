namespace Marey

open System

/// §9 / D47 — **the stats, as pure functions over typed arrays.**
///
/// The design lists them as "pure, composable transforms" between the data and the geom. D47
/// settles where that leaves them once measures are involved, and the answer is: *below* the
/// erasure boundary rather than across it. A stat is called while both measures are still
/// alive — in the caller's own scope on the quick path, and inside `Erase.reunify`'s inner
/// `Apply` on the grammar path (D44 finding 3) — and it returns a **typed** result. Nothing
/// here takes or returns a `PackedChannel`.
///
/// That is what makes the milestone's headline claim sayable at all. A fit of `float&lt;m&gt;`
/// against `float&lt;s&gt;` has a slope of `float&lt;m/s&gt;`, and a caller can bind it to an
/// annotated `float&lt;m/s&gt;`; had the stats been defined as a pipeline node over erased
/// channels, the slope would have been an existential to *everyone*, including the person who
/// asked for it.
///
/// ### The measures, which are the interesting part
///
/// | stat | x | y |
/// |---|---|---|
/// | `bin` | `float&lt;'u&gt;` edges | `int` counts |
/// | `count` | a category | `int` counts |
/// | `ecdf` | `float&lt;'u&gt;` | `float` — a probability, so dimensionless |
/// | `density` | `float&lt;'u&gt;` | **`float&lt;1/'u&gt;`** — per unit of x, which is why it integrates to 1 |
/// | `regression` | `float&lt;'ux&gt;` | slope `float&lt;'uy/'ux&gt;`, intercept `float&lt;'uy&gt;` |
///
/// `density`'s row is the one worth reading twice. A kernel density estimate over a metre axis
/// is measured in reciprocal metres, and the type says so — so a density curve cannot be
/// plotted against the same y axis as the data it summarises without the dimensional check in
/// `Figure.unify` raising. That is correct, and it is a thing this library can tell you and
/// most cannot.
///
/// ### Non-finite input
///
/// Every stat here filters NaN and infinity out of its input and **reports how many it
/// dropped**, in a `Dropped` field. That is D52's rule — a value the pipeline cannot use is not
/// silently treated as if it were a measurement — applied one layer earlier: the alternative is
/// a mean of NaN, or a domain of NaN, and a blank figure with no explanation.
[<RequireQualifiedAccess>]
module Stat =

    let inline private tag<[<Measure>] 'u> (x: float) : float<'u> =
        LanguagePrimitives.FloatWithMeasure<'u> x

    let inline private isFinite (x: float) =
        not (Double.IsNaN x) && not (Double.IsInfinity x)

    /// The finite values, in input order, and how many were not.
    let private finiteOf (xs: float<'u>[]) : float<'u>[] * int =
        let kept = xs |> Array.filter (fun v -> isFinite (float v))
        kept, xs.Length - kept.Length

    /// The type-7 quantile — linear interpolation between order statistics, which is what R,
    /// NumPy and every table of "the" quartiles agree on. `sorted` must be ascending.
    let private quantileSorted (p: float) (sorted: float<'u>[]) : float<'u> =
        match sorted.Length with
        | 0 -> tag<'u> 0.0
        | 1 -> sorted.[0]
        | n ->
            let h = p * float (n - 1)
            let lo = int (floor h)
            let hi = min (n - 1) (lo + 1)
            sorted.[lo] + (sorted.[hi] - sorted.[lo]) * (h - float lo)

    /// Sample standard deviation, `n-1` in the denominator. Zero for fewer than two values,
    /// which is the only answer that lets the bandwidth rule below degrade rather than divide
    /// by zero.
    let private stdevOf (xs: float<'u>[]) : float<'u> =
        let n = xs.Length

        if n < 2 then
            tag<'u> 0.0
        else
            let mean = float (Array.sum xs) / float n
            let ss = xs |> Array.sumBy (fun v -> let d = float v - mean in d * d)
            tag<'u> (sqrt (ss / float (n - 1)))

    // ======================================================================================
    // bin — a histogram
    // ======================================================================================

    /// A histogram: the edges, the counts between them, and the input it could not place.
    ///
    /// Edges rather than an array of bin records, because `Edges.Length = Counts.Length + 1`
    /// states the adjacency a bin list would have to be trusted to maintain — and because both
    /// arrays are then exactly what a geom wants, with no unpacking.
    type Histogram<[<Measure>] 'u> =
        { Unit: U<'u>
          /// Ascending, `Counts.Length + 1` of them. Bin `i` spans `Edges[i]` to `Edges[i+1]`,
          /// half-open except for the last, which is closed at the top so the maximum value is
          /// inside the histogram rather than past the end of it.
          Edges: float<'u>[]
          Counts: int[]
          /// Values dropped as non-finite, or as falling outside a **stated** domain. Zero for
          /// `bin` and `binWith`, which take their domain from the data.
          Dropped: int }

        /// Bin centres — the x array of a bar chart of this histogram.
        member this.Centers: float<'u>[] =
            Array.init this.Counts.Length (fun i -> (this.Edges.[i] + this.Edges.[i + 1]) * 0.5)

        /// Every bin is the same width, so there is one of these rather than an array.
        member this.Width: float<'u> =
            if this.Counts.Length = 0 then
                tag<'u> 0.0
            else
                this.Edges.[1] - this.Edges.[0]

        /// The counts as the dimensionless quantity they are, ready for a y channel.
        member this.Frequencies: float[] = this.Counts |> Array.map float

        member this.Total = Array.sum this.Counts

    /// **Freedman–Diaconis**, with a Sturges fallback — the bin count a histogram gets when
    /// nobody states one.
    ///
    /// FD sizes a bin at `2·IQR·n^(-1/3)`, which is robust to the outlier that would otherwise
    /// stretch one bin over the whole distribution. Its failure mode is data whose interquartile
    /// range is zero — heavily tied data, a discrete measurement — where it asks for infinitely
    /// many bins; Sturges (`ceil(log2 n) + 1`) is the fallback, because it depends on `n` alone
    /// and therefore always answers.
    let binCountFor (xs: float<'u>[]) : int =
        let kept, _ = finiteOf xs
        let n = kept.Length

        if n < 2 then
            1
        else
            let sorted = Array.sort kept
            let iqr = float (quantileSorted 0.75 sorted - quantileSorted 0.25 sorted)
            let span = float (sorted.[n - 1] - sorted.[0])

            let fd =
                if iqr > 0.0 && span > 0.0 then
                    let width = 2.0 * iqr * (float n ** (-1.0 / 3.0))
                    if width > 0.0 then Some(int (ceil (span / width))) else None
                else
                    None

            let sturges = int (ceil (Math.Log(float n, 2.0))) + 1

            match fd with
            | Some k -> max 1 (min 1000 k)
            | None -> max 1 (min 1000 sturges)

    /// A histogram over a **stated** domain and bin count. Values outside the domain are
    /// counted in `Dropped` rather than folded into the end bins, because folding them in draws
    /// a spike that is an artefact of the domain rather than a feature of the data — D52's
    /// rule, and the same reason a colour channel does not clamp.
    let binIn (u: U<'u>) (count: int) (domain: float<'u> * float<'u>) (xs: float<'u>[]) : Histogram<'u> =
        if count < 1 then
            invalidArg "count" "Marey: a histogram needs at least one bin."

        let lo0, hi0 = domain

        if not (isFinite (float lo0) && isFinite (float hi0)) then
            invalidArg "domain" "Marey: a histogram's domain must be finite."

        // A zero-width domain has no bins to speak of. Widen it so the single distinct value
        // lands in the middle of a real bin, exactly as `Scale.paddedDomain` widens a
        // single-valued axis rather than collapsing the panel.
        let lo, hi =
            if float lo0 = float hi0 then
                let pad = if float lo0 = 0.0 then 0.5 else abs (float lo0) * 0.05
                tag<'u> (float lo0 - pad), tag<'u> (float hi0 + pad)
            elif float lo0 > float hi0 then
                hi0, lo0
            else
                lo0, hi0

        let width = (float hi - float lo) / float count
        let counts = Array.zeroCreate<int> count
        let mutable dropped = 0

        for v in xs do
            let x = float v

            if not (isFinite x) || x < float lo || x > float hi then
                dropped <- dropped + 1
            else
                // The top edge is closed, so the maximum value belongs to the last bin rather
                // than to a phantom bin past the end.
                let i = min (count - 1) (int (floor ((x - float lo) / width)))
                counts.[i] <- counts.[i] + 1

        { Unit = u
          Edges = Array.init (count + 1) (fun i -> tag<'u> (float lo + width * float i))
          Counts = counts
          Dropped = dropped }

    /// A histogram over the data's own range, with a stated bin count.
    let binWith (u: U<'u>) (count: int) (xs: float<'u>[]) : Histogram<'u> =
        let kept, dropped = finiteOf xs

        let domain =
            match Scale.domainOf kept with
            | Some d -> d
            | None -> tag<'u> 0.0, tag<'u> 1.0

        let h = binIn u count domain kept
        { h with Dropped = h.Dropped + dropped }

    /// A histogram over the data's own range, binned by Freedman–Diaconis.
    let bin (u: U<'u>) (xs: float<'u>[]) : Histogram<'u> = binWith u (binCountFor xs) xs

    // ======================================================================================
    // count — the categorical counterpart of bin
    // ======================================================================================

    /// How many rows carry each distinct key, **in first-appearance order**.
    ///
    /// The order is `Levels.ofSeq`'s and for its reason (D45): a category usually arrives in an
    /// order that is itself information, and sorting destroys it in exactly that case. The two
    /// agree by construction, so counting a key set and laying it out on a categorical axis
    /// cannot disagree about which band is which.
    let count (keys: 'k[]) : ('k * int)[] =
        let index = Collections.Generic.Dictionary<'k, int>(HashIdentity.Structural)
        let order = ResizeArray<'k>()
        let counts = ResizeArray<int>()

        for k in keys do
            match index.TryGetValue k with
            | true, i -> counts.[i] <- counts.[i] + 1
            | _ ->
                index.[k] <- order.Count
                order.Add k
                counts.Add 1

        Array.init order.Count (fun i -> order.[i], counts.[i])

    // ======================================================================================
    // ecdf — the empirical distribution function
    // ======================================================================================

    /// The empirical CDF: the distinct observed values, and the fraction of the sample at or
    /// below each.
    ///
    /// `F` is dimensionless whatever `X` is measured in, which is the whole content of the
    /// sentence "a probability has no units" — and it is the one distribution stat where that
    /// is the answer rather than `density`'s `1/'u`.
    type Ecdf<[<Measure>] 'u> =
        { Unit: U<'u>
          /// Ascending, distinct.
          X: float<'u>[]
          /// `F.[i]` is the fraction of the sample at or below `X.[i]`. Ends at exactly 1.
          F: float[]
          /// Finite values used.
          N: int
          Dropped: int }

    /// The empirical CDF of a sample.
    ///
    /// Ties collapse to one point carrying the *higher* value of `F`, which is what makes this
    /// the right-continuous step function it is defined to be: one point per distinct value, at
    /// the top of its step.
    let ecdf (u: U<'u>) (xs: float<'u>[]) : Ecdf<'u> =
        let kept, dropped = finiteOf xs
        let n = kept.Length

        if n = 0 then
            { Unit = u
              X = [||]
              F = [||]
              N = 0
              Dropped = dropped }
        else
            let sorted = Array.sort kept
            let vs = ResizeArray<float<'u>>()
            let fs = ResizeArray<float>()
            let mutable i = 0

            while i < n do
                let v = float sorted.[i]
                let mutable j = i

                while j < n && float sorted.[j] = v do
                    j <- j + 1

                vs.Add sorted.[i]
                fs.Add(float j / float n)
                i <- j

            { Unit = u
              X = vs.ToArray()
              F = fs.ToArray()
              N = n
              Dropped = dropped }

    // ======================================================================================
    // density — a Gaussian kernel density estimate
    // ======================================================================================

    /// A kernel density estimate, evaluated on a grid.
    ///
    /// **`Y` is `float&lt;1/'u&gt;`, and that is not decoration.** A density is a quantity per
    /// unit of x — that is what makes the integral of `f dx` equal to a dimensionless 1 — so a
    /// KDE over a metre axis is in reciprocal metres. The consequence a user meets is that a
    /// density curve and the data it summarises cannot share a y axis, and `Figure.unify` says
    /// so rather than drawing it.
    type Density<[<Measure>] 'u> =
        { Unit: U<'u>
          /// The witness for `Y`, `U.recip Unit`. Carried rather than rebuilt so a caller can
          /// label a density axis without knowing how the reciprocal was spelled.
          DensityUnit: U<1 / 'u>
          /// The evaluation grid, ascending and evenly spaced.
          X: float<'u>[]
          Y: float<1 / 'u>[]
          Bandwidth: float<'u>
          N: int
          Dropped: int }

    /// **Silverman's rule of thumb**: `0.9 · min(sigma, IQR/1.34) · n^(-1/5)`.
    ///
    /// The `min` is what stops a single outlier inflating sigma and smearing the estimate flat;
    /// the `1.34` is the interquartile range of a standard normal, so for normal data the two
    /// terms agree and the rule is optimal. It is the default because it needs no tuning and is
    /// never badly wrong — not because it is the best bandwidth selector, which it is not.
    ///
    /// Returns zero for a sample with no spread, which `densityWith` then refuses: a
    /// distribution concentrated on one point has no density to estimate.
    let bandwidth (xs: float<'u>[]) : float<'u> =
        let kept, _ = finiteOf xs
        let n = kept.Length

        if n < 2 then
            tag<'u> 0.0
        else
            let sorted = Array.sort kept
            let sigma = float (stdevOf sorted)
            let iqr = float (quantileSorted 0.75 sorted - quantileSorted 0.25 sorted)
            let spread = if iqr > 0.0 then min sigma (iqr / 1.34) else sigma
            tag<'u> (0.9 * spread * (float n ** (-1.0 / 5.0)))

    /// A Gaussian KDE at a stated bandwidth, on a grid of `points` samples.
    ///
    /// The grid runs three bandwidths past the data at each end, which is where a Gaussian
    /// kernel has given up all but 0.14% of its mass — far enough that the curve reaches the
    /// axis rather than being cut off mid-slope, and near enough not to waste half the panel.
    let densityWith (u: U<'u>) (h: float<'u>) (points: int) (xs: float<'u>[]) : Density<'u> =
        if points < 2 then
            invalidArg "points" "Marey: a density needs at least two grid points."

        if not (isFinite (float h)) || float h <= 0.0 then
            invalidArg
                "h"
                "Marey: a density needs a positive, finite bandwidth. A sample with no spread has none — every value is the same, so there is nothing to estimate."

        let kept, dropped = finiteOf xs
        let n = kept.Length

        if n = 0 then
            invalidArg "xs" "Marey: a density needs at least one finite value."

        let lo = float (Array.min kept) - 3.0 * float h
        let hi = float (Array.max kept) + 3.0 * float h
        let step = (hi - lo) / float (points - 1)
        let grid = Array.init points (fun i -> lo + step * float i)

        // `(1/(n·h)) · sum of phi((x - xi)/h)`. The `1/h` is where the reciprocal measure comes
        // from, and it is arrived at by measured arithmetic rather than by tagging: dividing a
        // `float` by a `float<'u>` *is* a `float<1/'u>`, so the type is a consequence of the
        // formula and not an assertion about it.
        let norm = 1.0 / sqrt (2.0 * Math.PI)

        let ys =
            grid
            |> Array.map (fun g ->
                let mutable acc = 0.0

                for v in kept do
                    let z = (g - float v) / float h
                    acc <- acc + norm * exp (-0.5 * z * z)

                acc / (float n * h))

        { Unit = u
          DensityUnit = U.recip u
          X = grid |> Array.map tag<'u>
          Y = ys
          Bandwidth = h
          N = n
          Dropped = dropped }

    /// A Gaussian KDE at Silverman's bandwidth, on a 128-point grid.
    let density (u: U<'u>) (xs: float<'u>[]) : Density<'u> = densityWith u (bandwidth xs) 128 xs

    // ======================================================================================
    // regression — the typed slope, and M2's headline claim
    // ======================================================================================

    /// An ordinary least-squares fit of y against x.
    ///
    /// **`Slope` is `float&lt;'uy/'ux&gt;`.** A fit of metres against seconds has a slope in
    /// metres per second, and this is the type that says so — the claim §9 makes for the
    /// grammar, and the one gate that proves the units story survived the stat boundary rather
    /// than being dropped at it. It is checkable by *compiling* `let v: float&lt;m/s&gt; =
    /// fit.Slope`, which is what `ChannelTests` does.
    type Fit<[<Measure>] 'ux, [<Measure>] 'uy> =
        { Slope: float<'uy / 'ux>
          Intercept: float<'uy>
          /// The witness for `Slope`, `U.div uy ux`.
          ///
          /// D18's argument applies unchanged: the quotient records the factorisation the
          /// caller wrote, so a fit of `V` against `m` reports `V·m⁻¹` rather than the base-unit
          /// expansion its `Dim` alone would give. Without this the slope would be a number the
          /// library could compute and not name.
          SlopeUnit: U<'uy / 'ux>
          InterceptUnit: U<'uy>
          /// Coefficient of determination, in `[0, 1]`. Dimensionless by construction — it is a
          /// ratio of two sums of squared y.
          R2: float
          /// Finite pairs the fit was computed from.
          N: int
          /// Pairs dropped because either coordinate was not finite.
          Dropped: int }

    /// Least squares, with the measures coming *out* of it rather than being asserted about it.
    ///
    /// Raises rather than returning an option in the two cases where there is no line: fewer
    /// than two finite pairs, and an x with no variance. Both are caller errors of the same kind
    /// as `Scale.reciprocal`'s domain touching zero — and the alternative is a NaN slope drawn
    /// as an empty layer, which is the failure mode this library exists not to have.
    let regression (ux: U<'ux>) (uy: U<'uy>) (xs: float<'ux>[]) (ys: float<'uy>[]) : Fit<'ux, 'uy> =
        if xs.Length <> ys.Length then
            invalidArg "ys" (sprintf "Marey: a fit needs matching arrays — %d x values and %d y values." xs.Length ys.Length)

        let pairs =
            Array.init xs.Length (fun i -> float xs.[i], float ys.[i])
            |> Array.filter (fun (a, b) -> isFinite a && isFinite b)

        let n = pairs.Length

        if n < 2 then
            failwithf "Marey: a least-squares fit needs at least two finite (x, y) pairs; this one has %d." n

        let mx = (pairs |> Array.sumBy fst) / float n
        let my = (pairs |> Array.sumBy snd) / float n

        let mutable sxx = 0.0
        let mutable sxy = 0.0
        let mutable syy = 0.0

        for (a, b) in pairs do
            let dx = a - mx
            let dy = b - my
            sxx <- sxx + dx * dx
            sxy <- sxy + dx * dy
            syy <- syy + dy * dy

        if sxx = 0.0 then
            failwithf
                "Marey: a least-squares fit needs x to vary, and every one of these %d values is %s %s. The slope of a vertical line is not a number."
                n
                (Num.at 6 mx)
                (if String.IsNullOrEmpty ux.Symbol then "(dimensionless)" else "[" + ux.Symbol + "]")

        let slope = sxy / sxx

        { Slope = LanguagePrimitives.FloatWithMeasure<'uy / 'ux> slope
          Intercept = LanguagePrimitives.FloatWithMeasure<'uy>(my - slope * mx)
          SlopeUnit = U.div uy ux
          InterceptUnit = uy
          // Constant y is a perfect fit by a flat line: every residual is zero, and the
          // conventional `1 - SSres/SStot` is 0/0 rather than undefined in spirit.
          R2 = (if syy = 0.0 then 1.0 else (sxy * sxy) / (sxx * syy))
          N = n
          Dropped = xs.Length - n }

    /// The fitted value at an x. The measures cancel — `float&lt;'uy/'ux&gt;` times
    /// `float&lt;'ux&gt;` is `float&lt;'uy&gt;` — so this is the one-line proof that the slope's
    /// type is the right one.
    let predict (f: Fit<'ux, 'uy>) (x: float<'ux>) : float<'uy> = f.Intercept + f.Slope * x

    /// The two endpoints of the fitted line over the range of an x sample — the two-point series
    /// a `GeomLine` layer draws.
    let fitLine (f: Fit<'ux, 'uy>) (xs: float<'ux>[]) : float<'ux>[] * float<'uy>[] =
        let kept, _ = finiteOf xs

        let lo, hi =
            match Scale.domainOf kept with
            | Some d -> d
            | None -> tag<'ux> 0.0, tag<'ux> 1.0

        [| lo; hi |], [| predict f lo; predict f hi |]

    /// **The slope, on its way out of the scope that knows its measure** (D47).
    ///
    /// Inside `Erase.reunify` both measures exist and `Slope` is `float&lt;'uy/'ux&gt;`; outside
    /// it neither does, and F# has no way to return the type. `PackedQuantity` is the encoding —
    /// the witness travels with the number, so whoever receives it can still *print* `m·s⁻¹`
    /// even though they can no longer *say* it. This is the escape the grammar path takes, and
    /// the reason `PackedQuantity` has been in `Erased.fs` since M0 with no consumer.
    let slopeQuantity (f: Fit<'ux, 'uy>) : PackedQuantity = Erase.packQuantity f.SlopeUnit f.Slope

    let interceptQuantity (f: Fit<'ux, 'uy>) : PackedQuantity =
        Erase.packQuantity f.InterceptUnit f.Intercept
