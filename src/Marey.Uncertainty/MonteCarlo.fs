namespace Marey

open System

/// How a Monte Carlo check runs.
type MonteCarloOptions =
    { /// The most times the model is evaluated, each time at fresh draws of its inputs: all of
      /// them unless `Adaptive`.
      Trials: int
      /// Whether to stop, in batches of 10⁴ trials, once the comparison is settled: once the
      /// interval's ends are known to a fifth of the tolerance (JCGM 101, 7.9 and 8.2), or well
      /// enough that the comparison cannot come out the other way.
      Adaptive: bool
      /// The seed of the draws. A check with the same seed and model gives the same numbers on
      /// every run and every platform.
      Seed: int
      /// The coverage probability of the two intervals compared.
      Coverage: float
      /// How many significant digits of the standard uncertainty are meant to hold. The
      /// tolerance the intervals are compared to is half a unit in the last of them (JCGM 101,
      /// 7.9.2): 0.05 for u = 2.0 at two digits.
      Digits: int }

/// One argument of a model as the check draws it: its estimate, and its inputs with their
/// contributions.
type internal Argument =
    { Value: float
      Inputs: Input[]
      Parts: float[] }

/// **A model ready for a Monte Carlo check**: a function of uncertain values and the values it is
/// applied to. Made by `MonteCarlo.model`, `model2`, `model3` or `model4`.
[<NoEquality; NoComparison>]
type MonteCarloModel<[<Measure>] 'u> =
    internal
        { Linear: Uncertain<'u>
          Arguments: Argument[]
          Evaluate: float[] -> Uncertain<'u> }

/// **What a Monte Carlo check found** (JCGM 101): the output's distribution as the model gives it
/// from draws of every input, beside the law of propagation's first-order result, and whether
/// the two coverage intervals agree within the tolerance the stated digits allow.
[<NoEquality; NoComparison>]
type MonteCarloCheck<[<Measure>] 'u> =
    { Trials: int
      Seed: int
      Coverage: float
      /// The mean of the model's values.
      Estimate: float<'u>
      /// Their standard deviation.
      Uncertainty: float<'u>
      /// The probabilistically symmetric coverage interval (JCGM 101, 7.7): as much probability
      /// below `Low` as above `High`.
      Low: float<'u>
      High: float<'u>
      Unit: U<'u>
      /// The law of propagation's result for the same model.
      Linear: Uncertain<'u>
      /// Its interval: the factor from the effective degrees of freedom, or the normal one
      /// (`Dof` infinite) where there are none to state.
      LinearInterval: Expanded<'u>
      /// δ, half a unit in the last stated digit of the linear standard uncertainty.
      Tolerance: float<'u>
      /// |y − U − Low| and |y + U − High|: how far the linear interval's ends lie from these.
      LowDifference: float<'u>
      HighDifference: float<'u>
      /// Whether both ends agree within the tolerance, so that the law of propagation holds for
      /// this model at this coverage (JCGM 101, 8.1).
      Validated: bool
      /// Whether the Monte Carlo ends are known well enough for `Validated` to be the answer:
      /// to a fifth of the tolerance, or with each difference further from it than their noise.
      /// A check that ran out of trials first is not settled, and says so.
      Settled: bool
      /// Twice the standard deviation of the Monte Carlo ends, from the spread of the batches'
      /// ends: how well they are known. Not a number with fewer than two full batches.
      Noise: float<'u>
      /// Every value the model took, in increasing order.
      Values: float<'u>[] }

/// **Monte Carlo propagation as a check of the law of propagation** (JCGM 101, Supplement 1 to
/// the GUM). Each input is drawn from its distribution, the model is evaluated at the draws as
/// many times as asked, and the interval that holds the stated share of the values is set
/// beside the first-order one. Where they differ by more than the stated digits allow, the
/// model is too curved over its inputs' spread for the law of propagation, and the Monte Carlo
/// interval is the one to report.
[<RequireQualifiedAccess>]
module MonteCarlo =

    /// Until settled, at most 10⁷ trials; seed 1, 95 % coverage, two significant digits.
    let defaults: MonteCarloOptions =
        { Trials = 10_000_000
          Adaptive = true
          Seed = 1
          Coverage = 0.95
          Digits = 2 }

    let private argument (x: Uncertain<'u>) : Argument =
        { Value = float x.Value
          Inputs = x.Inputs
          Parts = x.Parts }

    let private exactAs (x: Uncertain<'u>) (value: float) : Uncertain<'u> = Uncertain(Parts.tag value, x.Unit, [||], [||])

    /// A model of one argument: `MonteCarlo.model (fun x -> exp (x / x0)) x`. The function is the
    /// whole model, written over uncertain values, and gives the law of propagation's result as
    /// well as each trial's: make every input outside it and pass it in.
    let model (f: Uncertain<'a> -> Uncertain<'u>) (a: Uncertain<'a>) : MonteCarloModel<'u> =
        { Linear = f a
          Arguments = [| argument a |]
          Evaluate = fun v -> f (exactAs a v.[0]) }

    /// A model of two arguments: `MonteCarlo.model2 (fun l t -> 4.0 * Math.PI ** 2.0 * l / (t * t)) l t`.
    let model2 (f: Uncertain<'a> -> Uncertain<'b> -> Uncertain<'u>) (a: Uncertain<'a>) (b: Uncertain<'b>) : MonteCarloModel<'u> =
        { Linear = f a b
          Arguments = [| argument a; argument b |]
          Evaluate = fun v -> f (exactAs a v.[0]) (exactAs b v.[1]) }

    /// A model of three arguments.
    let model3
        (f: Uncertain<'a> -> Uncertain<'b> -> Uncertain<'c> -> Uncertain<'u>)
        (a: Uncertain<'a>)
        (b: Uncertain<'b>)
        (c: Uncertain<'c>)
        : MonteCarloModel<'u> =
        { Linear = f a b c
          Arguments = [| argument a; argument b; argument c |]
          Evaluate = fun v -> f (exactAs a v.[0]) (exactAs b v.[1]) (exactAs c v.[2]) }

    /// A model of four arguments.
    let model4
        (f: Uncertain<'a> -> Uncertain<'b> -> Uncertain<'c> -> Uncertain<'d> -> Uncertain<'u>)
        (a: Uncertain<'a>)
        (b: Uncertain<'b>)
        (c: Uncertain<'c>)
        (d: Uncertain<'d>)
        : MonteCarloModel<'u> =
        { Linear = f a b c d
          Arguments = [| argument a; argument b; argument c; argument d |]
          Evaluate = fun v -> f (exactAs a v.[0]) (exactAs b v.[1]) (exactAs c v.[2]) (exactAs d v.[3]) }

    /// The Cholesky factor of a correlation matrix, or `None` where it is not positive definite.
    let private cholesky (r: float[][]) : float[][] option =
        let n = r.Length
        let l = Array.init n (fun _ -> Array.zeroCreate<float> n)
        let mutable ok = true

        for i in 0 .. n - 1 do
            for j in 0..i do
                if ok then
                    let mutable sum = r.[i].[j]

                    for k in 0 .. j - 1 do
                        sum <- sum - l.[i].[k] * l.[j].[k]

                    if i = j then
                        if sum <= 1e-12 then ok <- false else l.[i].[i] <- sqrt sum
                    else
                        l.[i].[j] <- sum / l.[j].[j]

        if ok then Some l else None

    /// The tolerance δ for `u` to `digits` significant digits: write u as c × 10ˡ with c a whole
    /// number of that many digits, and δ = 10ˡ / 2 (JCGM 101, 7.9.2).
    let internal tolerance (digits: int) (u: float) : float =
        if not (u > 0.0) || Double.IsInfinity u then
            0.0
        else
            let mutable l = int (floor (log10 u)) - digits + 1

            if Math.Round(u / Math.Pow(10.0, float l)) >= Math.Pow(10.0, float digits) then
                l <- l + 1

            0.5 * Math.Pow(10.0, float l)

    /// `check` with stated options: `MonteCarlo.checkWith { MonteCarlo.defaults with Trials = 1_000_000 } m`.
    let checkWith (options: MonteCarloOptions) (m: MonteCarloModel<'u>) : MonteCarloCheck<'u> =
        if options.Trials < 2 then
            invalidArg "options" (sprintf "Marey: a Monte Carlo check needs two trials or more, and was given %d." options.Trials)

        if not (options.Coverage > 0.0 && options.Coverage < 1.0) then
            invalidArg "options" (sprintf "Marey: a coverage probability lies strictly between 0 and 1, and this one is %g." options.Coverage)

        if options.Digits < 1 then
            invalidArg "options" (sprintf "Marey: the digits that are meant to hold are one or more, and these are %d." options.Digits)

        // Every input any argument depends on, once, in the order they were made, so the draws
        // are taken in one order on every run.
        let inputs =
            m.Arguments
            |> Array.collect (fun a -> a.Inputs)
            |> Array.distinctBy (fun i -> i.Id)
            |> Array.sortBy (fun i -> i.Id)
            |> Array.filter (fun i -> i.Standard > 0.0)

        let index = inputs |> Array.mapi (fun k i -> i.Id, k) |> Map.ofArray

        // Inputs declared correlated are drawn together from a multivariate normal distribution
        // (JCGM 101, 6.4.8), which is the one joint distribution a correlation coefficient fixes.
        let correlated =
            inputs
            |> Array.filter (fun i -> i.Correlated |> List.exists (fun (other, r) -> r <> 0.0 && index.ContainsKey other))

        for input in correlated do
            if input.Distribution <> Distribution.Normal then
                invalidArg
                    "m"
                    (sprintf
                        "Marey: \"%O\" is declared correlated and has a %A distribution. A Monte Carlo check draws correlated inputs as jointly normal, which a correlation coefficient alone defines; for another shape, model the common cause as an input of its own."
                        input
                        input.Distribution)

        let factor =
            if correlated.Length = 0 then
                [||]
            else
                let position = correlated |> Array.mapi (fun k i -> i.Id, k) |> Map.ofArray

                let r =
                    Array.init correlated.Length (fun a ->
                        Array.init correlated.Length (fun b ->
                            if a = b then
                                1.0
                            else
                                correlated.[a].Correlated
                                |> List.tryFind (fun (other, _) -> position.TryFind other = Some b)
                                |> Option.map snd
                                |> Option.defaultValue 0.0))

                match cholesky r with
                | Some l -> l
                | None -> invalidArg "m" "Marey: the declared correlation coefficients cannot all hold at once: their matrix is not positive definite."

        let correlatedAt = correlated |> Array.map (fun i -> index.[i.Id])
        let isCorrelated = Array.init inputs.Length (fun k -> Array.contains k correlatedAt)

        // Inputs resting on one estimate of a variance share one χ² draw per trial.
        let pools =
            inputs
            |> Array.filter (fun i -> i.Pool <> 0 && i.Distribution = Distribution.StudentT && not (Double.IsPositiveInfinity i.Dof))
            |> Array.map (fun i -> i.Pool, i.Dof)
            |> Array.distinctBy fst

        let poolAt = pools |> Array.mapi (fun k (pool, _) -> pool, k) |> Map.ofArray

        let positions =
            m.Arguments
            |> Array.map (fun a -> Array.init a.Inputs.Length (fun j -> defaultArg (index.TryFind a.Inputs.[j].Id) -1))

        let g = Sampling.Generator options.Seed
        let d = Array.zeroCreate<float> inputs.Length
        let poolScale = Array.zeroCreate<float> pools.Length
        let normals = Array.zeroCreate<float> correlated.Length
        let samples = Array.zeroCreate<float> m.Arguments.Length

        let trial () : float =
            for p in 0 .. pools.Length - 1 do
                poolScale.[p] <- sqrt (g.ChiSquaredPerDof(snd pools.[p]))

            for k in 0 .. inputs.Length - 1 do
                let input = inputs.[k]

                if not isCorrelated.[k] then
                    let scale =
                        if input.Distribution <> Distribution.StudentT || Double.IsPositiveInfinity input.Dof then 1.0
                        else
                            match poolAt.TryFind input.Pool with
                            | Some p when input.Pool <> 0 -> poolScale.[p]
                            | _ -> sqrt (g.ChiSquaredPerDof input.Dof)

                    d.[k] <- Sampling.draw g input.Distribution input.Dof scale

            if correlated.Length > 0 then
                for a in 0 .. correlated.Length - 1 do
                    normals.[a] <- g.Normal()

                for a in 0 .. correlated.Length - 1 do
                    let mutable sum = 0.0

                    for b in 0..a do
                        sum <- sum + factor.[a].[b] * normals.[b]

                    d.[correlatedAt.[a]] <- sum

            for a in 0 .. m.Arguments.Length - 1 do
                let arg = m.Arguments.[a]
                let mutable v = arg.Value
                let at = positions.[a]

                for j in 0 .. at.Length - 1 do
                    if at.[j] >= 0 then
                        v <- v + arg.Parts.[j] * d.[at.[j]]

                samples.[a] <- v

            let y = m.Evaluate samples

            // An uncertain value the function reaches without being given it would stay at its
            // estimate in every trial, and its spread would go missing from the check unseen.
            if y.Inputs.Length > 0 then
                let names = y.Inputs |> Array.map string |> Array.distinct |> String.concat ", "

                invalidArg
                    "m"
                    (sprintf
                        "Marey: the model uses uncertain values it is not given as arguments (%s), which a Monte Carlo check would hold at their estimates. Pass them to the model."
                        names)

            float y.Value

        // JCGM 101, 7.7.2: of M sorted values, q make the interval and as many lie below it as
        // above. The positions are zero-based here.
        let p = options.Coverage

        let ends (count: int) : int * int =
            let pm = p * float count
            let q = if abs (pm - Math.Round pm) < 1e-9 then int (Math.Round pm) else int (floor (pm + 0.5))
            let q = min q (count - 1)
            let r = max 1 (if (count - q) % 2 = 0 then (count - q) / 2 else (count - q + 1) / 2)
            r - 1, min (count - 1) (r + q - 1)

        let linear = m.Linear

        let interval =
            match Uncertain.dof linear with
            | EffectiveDof.Known _ -> Uncertain.expanded p linear
            | EffectiveDof.Undefined _ -> Uncertain.expandedAssumingNormal p linear

        let y = float linear.Value
        let big = float interval.Expanded
        let delta = tolerance options.Digits (float linear.Uncertainty)

        // Batches of max(100/(1 − p), 10⁴) trials (JCGM 101, 7.9.4 b), each sorted for its own
        // interval, so the spread of the batches' ends says how well the ends are known.
        let size = max (int (ceil (100.0 / (1.0 - p)))) 10_000
        let batches = ResizeArray<float[]>()
        let lows = ResizeArray<float>()
        let highs = ResizeArray<float>()
        let mutable total = 0

        /// The standard deviation of the mean of the batches' ends (JCGM 101, 7.9.4 g).
        let spread (xs: ResizeArray<float>) =
            let h = float xs.Count
            let mean = Seq.average xs
            sqrt (Seq.sumBy (fun x -> (x - mean) * (x - mean)) xs / (h * (h - 1.0)))

        // Settled when the ends are known to δ/5, twice their spread within it, as a validation
        // asks (JCGM 101, 7.9.4 k and 8.2); or when the comparison cannot come out the other way,
        // each difference further from δ than its 95 % interval from the batches, Student's t at
        // h − 1 degrees of freedom so that a few batches cannot claim it early. The second stops
        // a model whose ends lie far from the linear ones, or whose tails are too long for its
        // spread ever to settle, long before the first.
        let settled () =
            if lows.Count < 2 then
                false
            else
                let sl, sh = spread lows, spread highs
                let k = Coverage.studentT 0.95 (float (lows.Count - 1))
                let dl = abs (y - big - Seq.average lows)
                let dh = abs (y + big - Seq.average highs)
                let stable = 2.0 * sl <= delta / 5.0 && 2.0 * sh <= delta / 5.0
                let holds = delta - dl > k * sl && delta - dh > k * sh
                let fails = dl - delta > k * sl || dh - delta > k * sh
                stable || holds || fails

        let mutable stop = false

        while not stop && total < options.Trials do
            let count = min size (options.Trials - total)
            let batch = Array.init count (fun _ -> trial ())
            Array.sortInPlace batch
            batches.Add batch
            total <- total + count

            if count = size then
                let lo, hi = ends count
                lows.Add batch.[lo]
                highs.Add batch.[hi]

            stop <- options.Adaptive && settled ()

        let values = Array.concat batches
        Array.sortInPlace values

        let n = float total
        let mean = Array.sum values / n
        let sd = sqrt (Array.sumBy (fun v -> (v - mean) * (v - mean)) values / (n - 1.0))
        let lo, hi = ends total
        let low = values.[lo]
        let high = values.[hi]
        let dLow = abs (y - big - low)
        let dHigh = abs (y + big - high)

        { Trials = total
          Seed = options.Seed
          Coverage = p
          Estimate = Parts.tag mean
          Uncertainty = Parts.tag sd
          Low = Parts.tag low
          High = Parts.tag high
          Unit = linear.Unit
          Linear = linear
          LinearInterval = interval
          Tolerance = Parts.tag delta
          LowDifference = Parts.tag dLow
          HighDifference = Parts.tag dHigh
          Validated = dLow <= delta && dHigh <= delta
          Settled = settled ()
          Noise = Parts.tag (if lows.Count < 2 then nan else 2.0 * max (spread lows) (spread highs))
          Values = Array.map Parts.tag values }

    /// **Checks the law of propagation by Monte Carlo**: in batches of 10⁴ trials until the
    /// comparison is settled, at most 10⁷; seed 1, 95 % coverage, and the comparison at two
    /// significant digits of u.
    /// `MonteCarlo.model2 (fun l t -> 4.0 * Math.PI ** 2.0 * l / (t * t)) l t |> MonteCarlo.check`.
    let check (m: MonteCarloModel<'u>) : MonteCarloCheck<'u> = checkWith defaults m

    /// The check as three lines of text: the Monte Carlo result, the law of propagation's, and
    /// whether they agree.
    let report (c: MonteCarloCheck<'u>) : string =
        let delta = float c.Tolerance

        // One digit past the tolerance's, as JCGM 101's tables give them.
        let decimals =
            if delta > 0.0 then max 0 (1 - int (Math.Round(log10 (2.0 * delta)))) else 6

        let unit = Rounding.unitText Unicode c.Unit
        let num (x: float) =
            let s = Num.fixedPoint decimals x
            // A value that rounds to zero is written without a sign.
            let s = if s.StartsWith "-" && s.TrimStart('-', '0', '.') = "" then s.Substring 1 else s
            s.Replace("-", "−")
        let withUnit (s: string) = if unit = "" then s else s + " " + unit
        let percent = Num.upTo 2 (100.0 * c.Coverage) + " %"
        let interval (lo: float) (hi: float) = withUnit ("[" + num lo + ", " + num hi + "]")

        let trials =
            let e = int (Math.Round(log10 (float c.Trials)))
            if Math.Pow(10.0, float e) = float c.Trials then "10" + Num.superscript e else string c.Trials

        let y = float c.Linear.Value
        let big = float c.LinearInterval.Expanded

        let linear =
            sprintf
                "Law of propagation: y = %s, u = %s, %s interval %s (k = %s)"
                (withUnit (num y))
                (withUnit (num (float c.Linear.Uncertainty)))
                percent
                (interval (y - big) (y + big))
                (Num.fixedPoint 2 c.LinearInterval.CoverageFactor)

        let monteCarlo =
            sprintf
                "Monte Carlo, %s trials: y = %s, u = %s, %s interval %s"
                trials
                (withUnit (num (float c.Estimate)))
                (withUnit (num (float c.Uncertainty)))
                percent
                (interval (float c.Low) (float c.High))

        let verdict =
            if c.Settled then
                sprintf
                    "The ends differ by %s and %s, %s δ = %s: the law of propagation %s."
                    (num (float c.LowDifference))
                    (num (float c.HighDifference))
                    (if c.Validated then "within" else "beyond")
                    (withUnit (Num.upTo 12 delta))
                    (if c.Validated then "holds here" else "does not hold here, and the Monte Carlo interval is the one to report")
            else
                sprintf
                    "The ends differ by %s and %s against δ = %s, and the Monte Carlo ends are known to ± %s only: more trials would tell."
                    (num (float c.LowDifference))
                    (num (float c.HighDifference))
                    (withUnit (Num.upTo 12 delta))
                    (num (float c.Noise))

        String.concat "\n" [ monteCarlo; linear; verdict ]
