namespace Marey

open System

/// **The stats, as pure functions over typed arrays.**
///
/// Stats are pure, composable transforms between the data and the geom. Once measures are involved,
/// that puts them *below* the erasure boundary rather than across it. A stat is called while both
/// measures are still alive — in the caller's own scope on the quick path, and inside
/// `Erase.reunify`'s inner `Apply` on the grammar path — and it returns a **typed** result. Nothing
/// here takes or returns a `PackedChannel`.
///
/// That is what makes the headline claim sayable at all. A fit of `float&lt;m&gt;`
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
/// Every stat here filters NaN and infinity out of its input and **reports how many it dropped**,
/// in a `Dropped` field. That is the library's rule — a value the pipeline cannot use is not
/// silently treated as if it were a measurement — applied one layer earlier: the alternative is a
/// mean of NaN, or a domain of NaN, and a blank figure with no explanation.
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
    /// a spike that is an artefact of the domain rather than a feature of the data, and for the
    /// same reason a colour channel does not clamp.
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
    /// The order is `Levels.ofSeq`'s and for its reason: a category usually arrives in an
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
    /// Grid points a density is evaluated at when nobody states a number. Enough that the curve
    /// is smooth at print resolution and few enough that it is not a point cloud pretending to be
    /// a line.
    [<Literal>]
    let DefaultDensityPoints = 128

    let density (u: U<'u>) (xs: float<'u>[]) : Density<'u> =
        densityWith u (bandwidth xs) DefaultDensityPoints xs

    // ======================================================================================
    // frameOf — the parameters a facet fixes before it splits
    // ======================================================================================

    /// **What a whole sample asks for, so that its parts can agree.**
    ///
    /// Raw numbers rather than measured ones, because the caller is the encoding block, which is
    /// below the erasure boundary and cannot name the measure: the values are reattached inside
    /// the per-panel `Apply`, where the unit is known again, exactly as `Figure.withDomains`
    /// reattaches a shared axis domain.
    type Frame =
        { /// The finite range of the whole sample, in its own unit.
          Domain: float * float
          /// The bin count the whole sample asks for.
          Bins: int
          /// The bandwidth the whole sample asks for.
          Bandwidth: float }

    /// The frame of a sample.
    ///
    /// **A faceted distribution takes its parameters from here rather than from its own rows**,
    /// and that is the whole point of the type. A histogram whose bin edges differ from panel to
    /// panel cannot be compared across panels, and comparing across panels is the only reason to
    /// draw them side by side; a density whose bandwidth is chosen per panel smooths the sparse
    /// ones harder and invents a difference in shape that is not in the data. Both are the
    /// quiet kind of wrong — the figure looks fine.
    ///
    /// It also removes a failure: `bandwidth` of a one-row panel is zero, and a density over it
    /// raises. Under a frame, that panel draws the whole sample's kernel over its one point,
    /// which is what it is.
    let frameOf (xs: float<'u>[]) : Frame =
        let kept, _ = finiteOf xs

        let lo, hi =
            match Scale.domainOf kept with
            | Some(lo, hi) -> float lo, float hi
            | None -> 0.0, 1.0

        { Domain = lo, hi
          Bins = binCountFor xs
          Bandwidth = float (bandwidth xs) }



    // ======================================================================================
    // summary — the five-number summary, and the whiskers a box plot draws
    // ======================================================================================

    /// A quantile of a sample, type-7 — the definition R, NumPy and every published table of
    /// quartiles agree on.
    ///
    /// Public because it is the one number in this file a caller routinely wants on its own:
    /// a median, a 95th percentile, the IQR of a residual. Non-finite values are dropped
    /// first, on the same rule as every other stat here.
    let quantile (p: float) (xs: float<'u>[]) : float<'u> =
        let kept, _ = finiteOf xs
        quantileSorted (max 0.0 (min 1.0 p)) (Array.sort kept)

    /// **A five-number summary with Tukey whiskers**, which is what a box plot is
    /// before it is a picture.
    ///
    /// Every field is `float&lt;'u&gt;`, including the outliers: a summary of a sample of
    /// metres is entirely in metres, so a box plot cannot end up drawn against an axis in some
    /// other unit — the same guarantee `errorY` gives an uncertainty, one stat up.
    ///
    /// **The whiskers are Tukey's**, `1.5 · IQR` past the box and then pulled back to the most
    /// extreme datum still inside that reach — not the min and max, and not a percentile pair.
    /// The consequence is that the whisker ends are *observations*, so the figure never draws a
    /// line reaching a value nothing in the sample attained, and everything past them is
    /// enumerated in `Outliers` rather than absorbed into the whisker. That is the same rule
    /// applied to a summary: a point the box cannot describe is reported, not hidden.
    type Summary<[<Measure>] 'u> =
        { Unit: U<'u>
          Min: float<'u>
          Q1: float<'u>
          Median: float<'u>
          Q3: float<'u>
          Max: float<'u>
          /// The smallest datum at or above `Q1 - 1.5·IQR`.
          LowerWhisker: float<'u>
          /// The largest datum at or below `Q3 + 1.5·IQR`.
          UpperWhisker: float<'u>
          /// Every value outside the whiskers, ascending. Drawn as points.
          Outliers: float<'u>[]
          /// Finite values used.
          N: int
          Dropped: int }

        member this.Iqr: float<'u> = this.Q3 - this.Q1

        /// The interval the drawn box plot occupies, whiskers and outliers included — what a y
        /// domain has to contain for nothing to be clipped.
        member this.Extent: float<'u> * float<'u> =
            let lo =
                this.Outliers |> Array.fold min this.LowerWhisker

            let hi =
                this.Outliers |> Array.fold max this.UpperWhisker

            lo, hi

    /// The summary of a sample. An empty sample gives an all-zero summary rather than raising,
    /// because a categorical box plot with one empty level should draw the other levels.
    let summary (u: U<'u>) (xs: float<'u>[]) : Summary<'u> =
        let kept, dropped = finiteOf xs
        let n = kept.Length

        if n = 0 then
            { Unit = u
              Min = tag<'u> 0.0
              Q1 = tag<'u> 0.0
              Median = tag<'u> 0.0
              Q3 = tag<'u> 0.0
              Max = tag<'u> 0.0
              LowerWhisker = tag<'u> 0.0
              UpperWhisker = tag<'u> 0.0
              Outliers = [||]
              N = 0
              Dropped = dropped }
        else
            let sorted = Array.sort kept
            let q1 = quantileSorted 0.25 sorted
            let q3 = quantileSorted 0.75 sorted
            let fence = 1.5 * float (q3 - q1)
            let lowFence = float q1 - fence
            let highFence = float q3 + fence

            let inside =
                sorted |> Array.filter (fun v -> float v >= lowFence && float v <= highFence)

            // A sample whose IQR is zero has a zero fence, so everything off the median is an
            // outlier and `inside` can still be non-empty (the ties themselves). It cannot be
            // empty: `q1` and `q3` are order statistics or interpolations between them, so at
            // least one datum always lies within the closed fences.
            { Unit = u
              Min = sorted.[0]
              Q1 = q1
              Median = quantileSorted 0.5 sorted
              Q3 = q3
              Max = sorted.[n - 1]
              LowerWhisker = (if inside.Length = 0 then q1 else inside.[0])
              UpperWhisker = (if inside.Length = 0 then q3 else inside.[inside.Length - 1])
              Outliers = sorted |> Array.filter (fun v -> float v < lowFence || float v > highFence)
              N = n
              Dropped = dropped }

    // ======================================================================================
    // groupBy — the split every per-level distribution starts with
    // ======================================================================================

    /// The values belonging to each key, in **first-appearance level order**.
    ///
    /// The same order `count` and `Levels.ofSeq` produce, and it has to be: a box plot and a
    /// counted bar chart of the same keys must put the same category in the same band, and the
    /// only way to guarantee that is for one rule to define the order everywhere.
    ///
    /// The measure survives the grouping, so `summary` and `density` can be mapped straight
    /// over the result and a box plot of metres stays in metres.
    let groupBy (keys: 'k[]) (values: float<'u>[]) : ('k * float<'u>[])[] =
        if keys.Length <> values.Length then
            invalidArg "values" (sprintf "Marey: %d keys and %d values." keys.Length values.Length)

        let order = ResizeArray<'k>()
        let buckets = ResizeArray<ResizeArray<float<'u>>>()
        let index = System.Collections.Generic.Dictionary<'k, int>(HashIdentity.Structural)

        for i in 0 .. keys.Length - 1 do
            let k = keys.[i]

            let slot =
                match index.TryGetValue k with
                | true, j -> j
                | _ ->
                    let j = order.Count
                    order.Add k
                    buckets.Add(ResizeArray<float<'u>>())
                    index.[k] <- j
                    j

            buckets.[slot].Add values.[i]

        // The dictionary is an index, never an enumeration source: the output must be
        // deterministic, and enumerating a `Dictionary` is exactly the thing that reaches the
        // output in a machine-dependent order. `order` is a list appended to in row order.
        Array.init order.Count (fun i -> order.[i], buckets.[i].ToArray())

    // ======================================================================================
    // regression — the typed slope
    // ======================================================================================

    /// An ordinary least-squares fit of y against x.
    ///
    /// **`Slope` is `float&lt;'uy/'ux&gt;`.** A fit of metres against seconds has a slope in
    /// metres per second, and this is the type that says so — the claim the grammar makes,
    /// and the one test that proves the units story survived the stat boundary rather
    /// than being dropped at it. It is checkable by *compiling* `let v: float&lt;m/s&gt; =
    /// fit.Slope`, which is what `ChannelTests` does.
    type Fit<[<Measure>] 'ux, [<Measure>] 'uy> =
        { Slope: float<'uy / 'ux>
          Intercept: float<'uy>
          /// The witness for `Slope`, `U.div uy ux`.
          ///
          /// The quotient records the factorisation the caller wrote, so a fit of `V` against `m`
          /// reports `V·m⁻¹` rather than the base-unit expansion its `Dim` alone would give.
          /// Without this the slope would be a number the library could compute and not name.
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
                (Num.upTo 6 mx)
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

    /// **The slope, on its way out of the scope that knows its measure**.
    ///
    /// Inside `Erase.reunify` both measures exist and `Slope` is `float&lt;'uy/'ux&gt;`; outside
    /// it neither does, and F# has no way to return the type. `PackedQuantity` is the encoding —
    /// the witness travels with the number, so whoever receives it can still *print* `m·s⁻¹`
    /// even though they can no longer *say* it. This is the escape the grammar path takes, and
    /// the reason `PackedQuantity` is in `Erased.fs`.
    let slopeQuantity (f: Fit<'ux, 'uy>) : PackedQuantity = Erase.packQuantity f.SlopeUnit f.Slope

    let interceptQuantity (f: Fit<'ux, 'uy>) : PackedQuantity =
        Erase.packQuantity f.InterceptUnit f.Intercept

    // ====================================================================================== hexbin
    // — a two-dimensional density
    // ======================================================================================

    /// Scattered points counted into a hexagonal lattice, and the count as a **density in
    /// `1/('ux·'uy)`**: `density`'s result in two dimensions. Each cell's density is its count
    /// over `N` times the cell's area, so it integrates to 1 over the plane, and a hexbin of
    /// positions in metres is in m⁻².
    ///
    /// Only cells holding at least one point are listed.
    type Hexbin<[<Measure>] 'ux, [<Measure>] 'uy> =
        { XUnit: U<'ux>
          YUnit: U<'uy>
          /// Cell centres.
          X: float<'ux>[]
          Y: float<'uy>[]
          Counts: int[]
          Density: float<1 / ('ux * 'uy)>[]
          /// The witness for `Density`.
          DensityUnit: U<1 / ('ux * 'uy)>
          /// Centre-to-centre spacing along a row, which is a hexagon's width flat to flat.
          Width: float<'ux>
          /// Spacing between rows; alternate rows are offset by half a width. A hexagon spans
          /// 4/3 of it vertically, and its area is `Width · RowSpacing`.
          RowSpacing: float<'uy>
          N: int
          Dropped: int }

    /// A hexagon is regular on a plot area this much wider than it is tall: the default figure's
    /// proportions, roughly. A lattice is chosen before any panel exists, so it has to assume one.
    [<Literal>]
    let HexAspect = 1.6

    /// Count into a lattice of stated `width` and `rowSpacing`, its first row's first centre at
    /// `origin`. A point belongs to the nearest centre in the metric that makes the hexagons
    /// regular, which is the hexagon it lies in.
    let hexbinIn
        (ux: U<'ux>)
        (uy: U<'uy>)
        (width: float<'ux>)
        (rowSpacing: float<'uy>)
        (origin: float<'ux> * float<'uy>)
        (xs: float<'ux>[])
        (ys: float<'uy>[])
        : Hexbin<'ux, 'uy> =
        if xs.Length <> ys.Length then
            invalidArg "ys" (sprintf "Marey: a hexbin needs matching arrays — %d x values and %d y values." xs.Length ys.Length)

        if not (float width > 0.0 && isFinite (float width)) || not (float rowSpacing > 0.0 && isFinite (float rowSpacing)) then
            invalidArg "width" "Marey: a hexagon's width and row spacing must be positive and finite."

        let ox, oy = float (fst origin), float (snd origin)
        let w, r = float width, float rowSpacing
        let rowHeight = sqrt 3.0 / 2.0

        let pairs =
            Array.init xs.Length (fun i -> float xs.[i], float ys.[i])
            |> Array.filter (fun (a, b) -> isFinite a && isFinite b)

        // In lattice units (u along a row, v across rows, scaled so that rows are √3/2 apart),
        // the cells are regular hexagons and the nearest centre is the cell.
        let cellOf (x: float, y: float) =
            let u = (x - ox) / w
            let v = (y - oy) / r * rowHeight
            let j0 = int (floor (v / rowHeight))

            [ j0; j0 + 1 ]
            |> List.map (fun j ->
                let shift = if j % 2 <> 0 then 0.5 else 0.0
                let i = int (Num.roundAway (u - shift))
                let du = u - (float i + shift)
                let dv = v - float j * rowHeight
                (i, j), du * du + dv * dv)
            |> List.minBy snd
            |> fst

        let counts = System.Collections.Generic.Dictionary<int * int, int>(HashIdentity.Structural)

        for p in pairs do
            let c = cellOf p

            match counts.TryGetValue c with
            | true, n -> counts.[c] <- n + 1
            | _ -> counts.[c] <- 1

        // Row order, then along the row: a function of the data, never of the dictionary.
        let cells = counts |> Seq.map (fun kv -> kv.Key, kv.Value) |> Seq.sortBy (fun ((i, j), _) -> j, i) |> Seq.toArray
        let n = pairs.Length
        let area = w * r

        { XUnit = ux
          YUnit = uy
          X = cells |> Array.map (fun ((i, j), _) -> tag<'ux> (ox + (float i + (if j % 2 <> 0 then 0.5 else 0.0)) * w))
          Y = cells |> Array.map (fun ((_, j), _) -> tag<'uy> (oy + float j * r))
          Counts = cells |> Array.map snd
          Density = cells |> Array.map (fun (_, c) -> LanguagePrimitives.FloatWithMeasure<1 / ('ux * 'uy)>(float c / (float n * area)))
          DensityUnit = U.recip (U.mul ux uy)
          Width = width
          RowSpacing = rowSpacing
          N = n
          Dropped = xs.Length - n }

    /// `hexbinIn` with `across` hexagons spanning the x range, rows spaced so the hexagons are
    /// regular on a plot area `HexAspect` times as wide as it is tall, and the lattice starting
    /// at the data's lower-left corner.
    let hexbinWith (ux: U<'ux>) (uy: U<'uy>) (across: int) (xs: float<'ux>[]) (ys: float<'uy>[]) : Hexbin<'ux, 'uy> =
        if across < 1 then
            invalidArg "across" (sprintf "Marey: a hexbin needs at least one hexagon across, not %d." across)

        let finiteX, _ = finiteOf xs
        let finiteY, _ = finiteOf ys

        match Scale.domainOf finiteX, Scale.domainOf finiteY with
        | Some(x0, x1), Some(y0, y1) ->
            let xr = float (x1 - x0)
            let yr = float (y1 - y0)
            let w = if xr > 0.0 then xr / float across else 1.0
            let yrEff = if yr > 0.0 then yr else (if xr > 0.0 then xr else 1.0)
            let r = sqrt 3.0 / 2.0 * w * HexAspect * yrEff / (if xr > 0.0 then xr else 1.0)
            hexbinIn ux uy (tag<'ux> w) (tag<'uy> r) (x0, y0) xs ys
        | _ -> hexbinIn ux uy (tag<'ux> 1.0) (tag<'uy> 1.0) (tag<'ux> 0.0, tag<'uy> 0.0) xs ys

    /// The hexagons across a hexbin when nobody states it.
    [<Literal>]
    let DefaultHexagons = 24

    /// `hexbinWith DefaultHexagons`.
    let hexbin (ux: U<'ux>) (uy: U<'uy>) (xs: float<'ux>[]) (ys: float<'uy>[]) : Hexbin<'ux, 'uy> =
        hexbinWith ux uy DefaultHexagons xs ys

    // ====================================================================================== loess
    // — a local regression
    // ======================================================================================

    /// A loess curve: the smooth evaluated at `X`, in the data's own units.
    type Loess<[<Measure>] 'ux, [<Measure>] 'uy> =
        { XUnit: U<'ux>
          YUnit: U<'uy>
          X: float<'ux>[]
          Y: float<'uy>[]
          /// The fraction of the points each local fit uses.
          Span: float
          /// The local polynomial's degree, 1 or 2.
          Degree: int
          N: int
          Dropped: int }

    /// The span when nobody states one: R's and Cleveland's default.
    [<Literal>]
    let DefaultSpan = 0.75

    /// The points a curve is evaluated at when nobody states them.
    [<Literal>]
    let DefaultLoessPoints = 80

    /// Solve a small dense system by Gaussian elimination with partial pivoting; `None` when it
    /// is singular to working precision.
    let private solve (a: float[][]) (b: float[]) : float[] option =
        let n = b.Length
        let m = Array.map Array.copy a
        let v = Array.copy b
        let scale = Seq.max (seq { for i in 0 .. n - 1 do for j in 0 .. n - 1 -> abs m.[i].[j] })
        let mutable ok = scale > 0.0

        for col in 0 .. n - 1 do
            if ok then
                let pivot = [ col .. n - 1 ] |> List.maxBy (fun r -> abs m.[r].[col])

                if abs m.[pivot].[col] <= 1e-12 * scale then
                    ok <- false
                else
                    if pivot <> col then
                        for j in 0 .. n - 1 do
                            let t = m.[col].[j]
                            m.[col].[j] <- m.[pivot].[j]
                            m.[pivot].[j] <- t

                        let t = v.[col]
                        v.[col] <- v.[pivot]
                        v.[pivot] <- t

                    for r in col + 1 .. n - 1 do
                        let f = m.[r].[col] / m.[col].[col]

                        for j in col .. n - 1 do
                            m.[r].[j] <- m.[r].[j] - f * m.[col].[j]

                        v.[r] <- v.[r] - f * v.[col]

        if not ok then
            None
        else
            let x = Array.zeroCreate n

            for i in n - 1 .. -1 .. 0 do
                let mutable acc = v.[i]

                for j in i + 1 .. n - 1 do
                    acc <- acc - m.[i].[j] * x.[j]

                x.[i] <- acc / m.[i].[i]

            Some x

    /// One local fit at `x0`: the `q = ⌊span·n⌋` nearest points (at least one more than the
    /// degree), weighted by the tricube of their
    /// distance over the q-th nearest distance (scaled up by `span` beyond 1, as R does), and a
    /// weighted polynomial of `degree` in `(x − x0)`. Its value at `x0` is the smooth. A
    /// neighbourhood too degenerate for the degree — fewer distinct x than coefficients — is
    /// fitted at the next degree down.
    let private loessAt (span: float) (degree: int) (xs: float[]) (ys: float[]) (x0: float) =
        let n = xs.Length
        let q = min n (max (degree + 1) (int (floor (span * float n + 1e-5))))
        let d = xs |> Array.map (fun x -> abs (x - x0))
        let sorted = Array.sort d
        let h = sorted.[q - 1] * (if span > 1.0 then span else 1.0)

        if h = 0.0 then
            // Every neighbour sits on x0.
            let near = Array.init n id |> Array.filter (fun i -> d.[i] = 0.0)
            near |> Array.averageBy (fun i -> ys.[i])
        else
            let w =
                d |> Array.map (fun di -> let r = di / h in if r < 1.0 then (let c = 1.0 - r * r * r in c * c * c) else 0.0)

            let rec fit (deg: int) =
                let k = deg + 1
                let a = Array.init k (fun _ -> Array.zeroCreate<float> k)
                let b = Array.zeroCreate k

                for i in 0 .. n - 1 do
                    if w.[i] > 0.0 then
                        // Centred and scaled by h, so the system is well conditioned at any scale.
                        let t = (xs.[i] - x0) / h
                        let pw = Array.init (2 * k - 1) (fun p -> pown t p)

                        for r in 0 .. k - 1 do
                            b.[r] <- b.[r] + w.[i] * pw.[r] * ys.[i]

                            for c in 0 .. k - 1 do
                                a.[r].[c] <- a.[r].[c] + w.[i] * pw.[r + c]

                match solve a b with
                | Some coef -> coef.[0]
                | None when deg > 0 -> fit (deg - 1)
                | None -> nan

            fit degree

    /// **Loess** (Cleveland, 1979; Cleveland and Devlin, 1988): at each evaluation point, a
    /// polynomial of `degree` fitted by weighted least squares to the nearest `span` of the
    /// points, tricube-weighted by distance. The defaults, degree 2 and span 0.75, are R's
    /// `loess`, and so is the Gaussian family: no robustness iterations, so an outlier pulls the
    /// curve as it would pull a least-squares fit. It has not been compared with R's output.
    ///
    /// A local fit of degree d reproduces any polynomial of degree d exactly, which is what the
    /// tests hold it to. `at` lists where to evaluate; non-finite pairs are dropped and counted.
    let loessIn
        (ux: U<'ux>)
        (uy: U<'uy>)
        (span: float)
        (degree: int)
        (at: float<'ux>[])
        (xs: float<'ux>[])
        (ys: float<'uy>[])
        : Loess<'ux, 'uy> =
        if xs.Length <> ys.Length then
            invalidArg "ys" (sprintf "Marey: a loess needs matching arrays — %d x values and %d y values." xs.Length ys.Length)

        if not (span > 0.0) || Double.IsInfinity span then
            invalidArg "span" (sprintf "Marey: a loess span is a positive fraction of the points, not %g." span)

        if degree <> 1 && degree <> 2 then
            invalidArg "degree" (sprintf "Marey: a loess fits a local line or a local quadratic, degree 1 or 2, not %d." degree)

        let pairs =
            Array.init xs.Length (fun i -> float xs.[i], float ys.[i])
            |> Array.filter (fun (a, b) -> isFinite a && isFinite b)

        if pairs.Length < degree + 1 then
            failwithf "Marey: a loess of degree %d needs at least %d finite (x, y) pairs; this one has %d." degree (degree + 1) pairs.Length

        let px = pairs |> Array.map fst
        let py = pairs |> Array.map snd

        { XUnit = ux
          YUnit = uy
          X = Array.copy at
          Y = at |> Array.map (fun x -> tag<'uy> (loessAt span degree px py (float x)))
          Span = span
          Degree = degree
          N = pairs.Length
          Dropped = xs.Length - pairs.Length }

    /// `loessIn` at `points` evenly spaced over the data's x range.
    let loessWith (ux: U<'ux>) (uy: U<'uy>) (span: float) (points: int) (xs: float<'ux>[]) (ys: float<'uy>[]) : Loess<'ux, 'uy> =
        let kept, _ = finiteOf xs

        let at =
            match Scale.domainOf kept with
            | Some(lo, hi) when points > 1 -> Array.init points (fun i -> lo + (hi - lo) * (float i / float (points - 1)))
            | Some(lo, _) -> [| lo |]
            | None -> [||]

        loessIn ux uy span 2 at xs ys

    /// `loessWith` at the default span, 0.75, and 80 points.
    let loess (ux: U<'ux>) (uy: U<'uy>) (xs: float<'ux>[]) (ys: float<'uy>[]) : Loess<'ux, 'uy> =
        loessWith ux uy DefaultSpan DefaultLoessPoints xs ys

    // ======================================================================================
    // bootstrap — the second consumer of randomness
    // ======================================================================================

    /// A bootstrap confidence interval for a statistic, in the statistic's own unit.
    type Interval<[<Measure>] 'v> =
        { /// The statistic of the sample itself.
          Estimate: float<'v>
          Lo: float<'v>
          Hi: float<'v>
          /// The confidence level, 0.95 for a 95% interval.
          Level: float
          /// Resamples whose statistic was finite, and how many were not.
          Resamples: int
          Dropped: int
          /// The seed, so the interval can be cited and reproduced.
          Seed: uint64 }

    /// Resamples when nobody states how many.
    [<Literal>]
    let DefaultResamples = 2000

    /// The percentile interval over `count` replicate statistics, each computed from a resample
    /// of `n` indices drawn with replacement from `rng`, in order.
    let private percentile
        (seed: uint64)
        (count: int)
        (level: float)
        (n: int)
        (estimate: float<'v>)
        (replicate: int[] -> float<'v>)
        : Interval<'v> =
        if count < 1 then
            invalidArg "resamples" (sprintf "Marey: a bootstrap needs at least one resample, not %d." count)

        if not (level > 0.0 && level < 1.0) then
            invalidArg "level" (sprintf "Marey: a confidence level is strictly between 0 and 1, not %g." level)

        if n < 1 then
            failwith "Marey: a bootstrap needs at least one finite value to resample."

        let mutable rng = Rng.seeded seed
        let stats = Array.zeroCreate count

        for b in 0 .. count - 1 do
            let idx = Array.zeroCreate n

            for i in 0 .. n - 1 do
                let struct (k, r') = Rng.below n rng
                idx.[i] <- k
                rng <- r'

            stats.[b] <- replicate idx

        let kept = stats |> Array.filter (fun v -> isFinite (float v)) |> Array.sort

        if kept.Length = 0 then
            failwith "Marey: no bootstrap resample gave a finite statistic."

        let tail = (1.0 - level) / 2.0

        { Estimate = estimate
          Lo = quantileSorted tail kept
          Hi = quantileSorted (1.0 - tail) kept
          Level = level
          Resamples = kept.Length
          Dropped = count - kept.Length
          Seed = seed }

    /// **A percentile bootstrap interval** for any statistic of one sample: resample the finite
    /// values with replacement `resamples` times, compute the statistic of each, and take the
    /// central `level` of them. The statistic keeps its unit, so an interval for a mean of
    /// metres is in metres and one for a variance in m².
    ///
    /// **Reproducible from `seed`**, which the result carries: the resamples are drawn from
    /// `Rng`, in order, so a seed names one interval on every runtime. A resample whose statistic
    /// is not finite is dropped and counted.
    ///
    /// The percentile interval is the plain one. It is first-order accurate and runs a little
    /// narrow for small samples; it is not BCa.
    let bootstrapWith
        (seed: uint64)
        (resamples: int)
        (level: float)
        (statistic: float<'u>[] -> float<'v>)
        (xs: float<'u>[])
        : Interval<'v> =
        let kept, _ = finiteOf xs
        percentile seed resamples level kept.Length (statistic kept) (fun idx -> statistic (idx |> Array.map (fun i -> kept.[i])))

    /// `bootstrapWith` at 2000 resamples and 95%.
    let bootstrap (seed: uint64) (statistic: float<'u>[] -> float<'v>) (xs: float<'u>[]) : Interval<'v> =
        bootstrapWith seed DefaultResamples 0.95 statistic xs

    /// The pairs bootstrap: a statistic of (x, y) pairs, resampled together — a regression slope
    /// in m/s, a correlation. Pairs with a non-finite coordinate are dropped first.
    let bootstrapPairsWith
        (seed: uint64)
        (resamples: int)
        (level: float)
        (statistic: float<'ux>[] -> float<'uy>[] -> float<'v>)
        (xs: float<'ux>[])
        (ys: float<'uy>[])
        : Interval<'v> =
        if xs.Length <> ys.Length then
            invalidArg "ys" (sprintf "Marey: a pairs bootstrap needs matching arrays — %d x values and %d y values." xs.Length ys.Length)

        let keep = Array.init xs.Length id |> Array.filter (fun i -> isFinite (float xs.[i]) && isFinite (float ys.[i]))
        let px = keep |> Array.map (fun i -> xs.[i])
        let py = keep |> Array.map (fun i -> ys.[i])

        percentile seed resamples level keep.Length (statistic px py) (fun idx ->
            statistic (idx |> Array.map (fun i -> px.[i])) (idx |> Array.map (fun i -> py.[i])))

    /// `bootstrapPairsWith` at 2000 resamples and 95%.
    let bootstrapPairs
        (seed: uint64)
        (statistic: float<'ux>[] -> float<'uy>[] -> float<'v>)
        (xs: float<'ux>[])
        (ys: float<'uy>[])
        : Interval<'v> =
        bootstrapPairsWith seed DefaultResamples 0.95 statistic xs ys
