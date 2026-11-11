namespace Marey

open System

/// The least-squares arithmetic behind `Fit`, on numbers: one point's coordinates in the axis's
/// unit, the normal equations, and each coefficient's sensitivity to every point.
module internal LeastSquares =

    /// One coordinate of one point as the fit reads it, in the axis's unit: the value, its
    /// standard uncertainty, and its inputs with their contributions.
    type Coordinate =
        { Value: float
          Standard: float
          Inputs: Input[]
          Parts: float[] }

    /// The values in the first unit anyone stated, as a chart reads them, and that unit.
    let coordinates (values: Uncertain<'u>[]) : U<'u> * Coordinate[] =
        let unit =
            values
            |> Array.tryFind (fun x -> x.Unit.Stated)
            |> Option.map (fun x -> x.Unit)
            |> Option.defaultWith Parts.unstated

        let read (x: Uncertain<'u>) =
            let struct (v, k) = Parts.aligned unit x.Unit x.Value

            { Value = float v
              Standard = k * float x.Uncertainty
              Inputs = x.Inputs
              Parts = Parts.scale k x.Parts }

        unit, Array.map read values

    let private dot (a: float[]) (b: float[]) =
        let mutable s = 0.0

        for i in 0 .. a.Length - 1 do
            s <- s + a.[i] * b.[i]

        s

    let private times (m: float[][]) (v: float[]) : float[] = Array.map (fun row -> dot row v) m

    /// The inverse of a symmetric positive definite matrix by Gauss–Jordan elimination with
    /// partial pivoting, or `None` where it is singular to working precision.
    let invert (m: float[][]) : float[][] option =
        let p = m.Length
        let a = Array.init p (fun i -> Array.append (Array.copy m.[i]) (Array.init p (fun j -> if i = j then 1.0 else 0.0)))
        let size = m |> Array.map (Array.map abs >> Array.max) |> Array.max
        let mutable regular = size > 0.0
        let mutable c = 0

        while regular && c < p do
            let mutable pivot = c

            for r in c + 1 .. p - 1 do
                if abs a.[r].[c] > abs a.[pivot].[c] then
                    pivot <- r

            if abs a.[pivot].[c] <= 1e-12 * size then
                regular <- false
            else
                let row = a.[pivot]
                a.[pivot] <- a.[c]
                a.[c] <- row
                let lead = row.[c]

                for j in 0 .. 2 * p - 1 do
                    row.[j] <- row.[j] / lead

                for r in 0 .. p - 1 do
                    if r <> c then
                        let k = a.[r].[c]

                        if k <> 0.0 then
                            for j in 0 .. 2 * p - 1 do
                                a.[r].[j] <- a.[r].[j] - k * row.[j]

                c <- c + 1

        if regular then Some(Array.init p (fun i -> Array.sub a.[i] p p)) else None

    /// Inputs and contributions gathered from many points into one value's, summed by input.
    let private gather (pairs: (Input * float) list) : struct (Input[] * float[]) =
        // A stable sort, so the sums are taken in one order on every platform.
        let sorted = pairs |> List.sortBy (fun (input, _) -> input.Id)
        let inputs = ResizeArray<Input>()
        let parts = ResizeArray<float>()

        for input, part in sorted do
            if inputs.Count > 0 && inputs.[inputs.Count - 1].Id = input.Id then
                parts.[parts.Count - 1] <- parts.[parts.Count - 1] + part
            else
                inputs.Add input
                parts.Add part

        struct (inputs.ToArray(), parts.ToArray())

    type Solution =
        { Coefficients: float[]
          /// Each coefficient's inputs and contributions.
          Parts: struct (Input[] * float[])[]
          Points: int
          Dof: int
          ChiSquared: float
          Scatter: float
          FromScatter: bool }

    /// A fit of `Σ aⱼ fⱼ(x)` to the points. `basis x` gives each fⱼ(x), and `slope x h` each
    /// fⱼ′(x), taken over ± h where it has to be differenced; it is asked only at an x with an
    /// uncertainty, which `h` is.
    ///
    /// The points are weighted by 1/u², with u² the effective variance u(y)² + (f′(x) u(x))², and
    /// the weights are refined with the curve until the coefficients settle. Every coefficient is
    /// linear in the y values and, to first order, in the x values, and those sensitivities carry
    /// each point's inputs into it. Where no point has an uncertainty, the points weigh equally and
    /// their uncertainty is the scatter about the curve, a type A evaluation with n − p degrees of
    /// freedom shared by every point (GUM H.3).
    let solve
        (what: string)
        (count: int)
        (basis: float -> float[])
        (slope: float -> float -> float[])
        (yUnit: IUnit)
        (xs: Coordinate[])
        (ys: Coordinate[])
        : Solution =
        let n = xs.Length

        if ys.Length <> n then
            invalidArg "y" (sprintf "Marey: %d x values and %d y values." n ys.Length)

        if n < count then
            invalidArg "y" (sprintf "Marey: %s has %d parameters and needs as many points, and has %d." what count n)

        for i in 0 .. n - 1 do
            let finite (v: float) = not (Double.IsNaN v || Double.IsInfinity v)

            if not (finite xs.[i].Value && finite ys.[i].Value) then
                invalidArg "y" (sprintf "Marey: point %d is not a finite number, and a fit drops nothing silently." (i + 1))

        let f = xs |> Array.map (fun x -> basis x.Value)

        let solveWith (w: float[]) =
            let m = Array.init count (fun j -> Array.init count (fun k -> Array.fold (+) 0.0 (Array.init n (fun i -> w.[i] * f.[i].[j] * f.[i].[k]))))
            let v = Array.init count (fun j -> Array.fold (+) 0.0 (Array.init n (fun i -> w.[i] * f.[i].[j] * ys.[i].Value)))

            match invert m with
            | Some inverse -> times inverse v, inverse
            | None ->
                invalidArg
                    "x"
                    (sprintf "Marey: %s cannot be fitted to these points: at these x values its terms cannot be told apart (for a line, x does not vary)." what)

        let curveSlope (a: float[]) (i: int) =
            if xs.[i].Standard > 0.0 then dot (slope xs.[i].Value xs.[i].Standard) a else 0.0

        let variances (a: float[]) =
            Array.init n (fun i ->
                let m = curveSlope a i
                ys.[i].Standard * ys.[i].Standard + m * m * xs.[i].Standard * xs.[i].Standard)

        let a0, inverse0 = solveWith (Array.create n 1.0)
        let v0 = variances a0
        let fromScatter = Array.forall (fun v -> v = 0.0) v0

        match Array.tryFindIndex (fun v -> v = 0.0) v0 with
        | Some i when not fromScatter ->
            invalidArg
                "y"
                (sprintf
                    "Marey: point %d has no uncertainty and others have one, so it would weigh infinitely in %s. Give every point an uncertainty, or none, and the fit takes it from the scatter."
                    (i + 1)
                    what)
        | _ -> ()

        let a, inverse, w =
            if fromScatter then
                a0, inverse0, Array.create n 1.0
            else
                let anyX = xs |> Array.exists (fun x -> x.Standard > 0.0)
                let mutable w = Array.map (fun v -> 1.0 / v) v0
                let mutable a, inverse = solveWith w
                let mutable go = anyX
                let mutable rounds = 0

                // With an uncertain x the weights hang on the curve's slope, and the slope on the
                // weights: refined until the coefficients stop moving.
                while go do
                    w <- Array.map (fun v -> 1.0 / v) (variances a)
                    let next, nextInverse = solveWith w
                    let size = next |> Array.map abs |> Array.max
                    let moved = Array.map2 (fun p q -> abs (p - q)) next a |> Array.max
                    a <- next
                    inverse <- nextInverse
                    rounds <- rounds + 1
                    go <- moved > 1e-14 * max size 1e-300 && rounds < 200

                a, inverse, w

        let residuals = Array.init n (fun i -> ys.[i].Value - dot f.[i] a)
        let dof = n - count

        if fromScatter && dof < 1 then
            invalidArg
                "y"
                (sprintf "Marey: these points have no uncertainty, so %s takes it from their scatter, which needs more points than its %d parameters." what count)

        let scatter =
            if dof < 1 then nan else sqrt (Array.sumBy (fun r -> r * r) residuals / float dof)

        // One estimate of the scatter stands behind every point's uncertainty, so the points'
        // inputs share its degrees of freedom.
        let scatterInputs =
            if not fromScatter then
                [||]
            else
                let inputs =
                    Array.init n (fun i ->
                        Input(Parts.nextId (), sprintf "Scatter about %s, point %d" what (i + 1), 0.0, scatter, float dof, InputKind.TypeA, Distribution.StudentT, yUnit))

                let pool = Parts.nextId ()

                for input in inputs do
                    input.Pool <- pool

                inputs

        let parts =
            Array.init count (fun j ->
                [ for i in 0 .. n - 1 do
                      // ∂a/∂yᵢ = wᵢ M⁻¹ f(xᵢ).
                      let dy = w.[i] * (times inverse f.[i]).[j]

                      for k in 0 .. ys.[i].Inputs.Length - 1 do
                          yield ys.[i].Inputs.[k], dy * ys.[i].Parts.[k]

                      if fromScatter then
                          yield scatterInputs.[i], dy * scatter

                      // ∂a/∂xᵢ = wᵢ M⁻¹ (f′(xᵢ) rᵢ − f(xᵢ) m(xᵢ)), from the normal equations at
                      // fixed weights; the second term is a shift along y by the curve's slope.
                      if xs.[i].Standard > 0.0 then
                          let df = slope xs.[i].Value xs.[i].Standard
                          let m = dot df a
                          let g = Array.init count (fun k -> df.[k] * residuals.[i] - f.[i].[k] * m)
                          let dx = w.[i] * (times inverse g).[j]

                          for k in 0 .. xs.[i].Inputs.Length - 1 do
                              yield xs.[i].Inputs.[k], dx * xs.[i].Parts.[k] ]
                |> gather)

        { Coefficients = a
          Parts = parts
          Points = n
          Dof = dof
          ChiSquared = (if fromScatter then nan else Array.fold (+) 0.0 (Array.init n (fun i -> w.[i] * residuals.[i] * residuals.[i])))
          Scatter = scatter
          FromScatter = fromScatter }

    let range (xs: Coordinate[]) : float * float =
        let values = xs |> Array.map (fun x -> x.Value)
        Array.min values, Array.max values

/// What a chart asks of a fitted curve. `LineFit` and `LinearFit` are both; the members have
/// names of their own because a record's fields and an interface's members share one namespace
/// in JavaScript.
type IFittedCurve<[<Measure>] 'ux, [<Measure>] 'uy> =
    /// The curve at an x, with its uncertainty.
    abstract ValueAt: float<'ux> -> Uncertain<'uy>
    /// The smallest and the largest x fitted.
    abstract Span: float<'ux> * float<'ux>
    abstract XWitness: U<'ux>
    abstract YWitness: U<'uy>

/// **A straight line fitted to uncertain points**, `y = Intercept + Slope · (x − Origin)`. The
/// slope and the intercept are uncertain values whose inputs are the points' own, so anything
/// computed from both (`fit.At x`, a ratio, a root) carries their correlation without a
/// covariance matrix being written down.
[<NoEquality; NoComparison>]
type LineFit<[<Measure>] 'ux, [<Measure>] 'uy> =
    { /// The line's value at `Origin`.
      Intercept: Uncertain<'uy>
      Slope: Uncertain<'uy / 'ux>
      /// Where the intercept is read: zero unless `Fit.lineAbout` says otherwise.
      Origin: float<'ux>
      XUnit: U<'ux>
      YUnit: U<'uy>
      Points: int
      /// The degrees of freedom of the residuals, n − 2.
      Dof: int
      /// χ² = Σ (rᵢ/uᵢ)², the residuals weighed by the points' uncertainties. Not a number where
      /// the points had none and the fit took their uncertainty from the scatter.
      ChiSquared: float
      /// The residuals' standard deviation, √(Σ rᵢ² / (n − 2)), in y's unit.
      Scatter: float<'uy>
      /// Whether the points had no uncertainty and the fit took it from their scatter.
      FromScatter: bool
      /// The smallest and the largest x fitted.
      XRange: float<'ux> * float<'ux> }

    /// χ²/ν: near 1 when the stated uncertainties describe the scatter, well above when they
    /// leave some out or the line is the wrong model, well below when they are overstated.
    member f.ReducedChiSquared: float = f.ChiSquared / float f.Dof

    /// The line at `x`, with its uncertainty from the slope's and the intercept's together.
    member f.At(x: float<'ux>) : Uncertain<'uy> =
        let dx = x - f.Origin
        let struct (inputs, parts) = Parts.combine f.Intercept.Inputs f.Intercept.Parts 1.0 f.Slope.Inputs f.Slope.Parts (float dx)
        Uncertain(f.Intercept.Value + f.Slope.Value * dx, f.YUnit, inputs, parts)

    interface IFittedCurve<'ux, 'uy> with
        member f.ValueAt x = f.At x
        member f.Span = f.XRange
        member f.XWitness = f.XUnit
        member f.YWitness = f.YUnit

/// **A curve linear in its coefficients fitted to uncertain points**, `y = Σ aⱼ fⱼ(x)`: a
/// polynomial, a sum of known shapes. The basis functions return numbers, so each coefficient is
/// in y's unit.
[<NoEquality; NoComparison>]
type LinearFit<[<Measure>] 'ux, [<Measure>] 'uy> =
    { Coefficients: Uncertain<'uy>[]
      Basis: (float<'ux> -> float)[]
      XUnit: U<'ux>
      YUnit: U<'uy>
      Points: int
      /// The degrees of freedom of the residuals, n minus the number of coefficients.
      Dof: int
      /// χ² = Σ (rᵢ/uᵢ)². Not a number where the fit took the uncertainty from the scatter.
      ChiSquared: float
      /// The residuals' standard deviation, in y's unit.
      Scatter: float<'uy>
      FromScatter: bool
      XRange: float<'ux> * float<'ux> }

    /// χ²/ν, as for a line.
    member f.ReducedChiSquared: float = f.ChiSquared / float f.Dof

    /// The curve at `x`, with its uncertainty from all the coefficients together.
    member f.At(x: float<'ux>) : Uncertain<'uy> =
        let mutable sum = Uncertain(LanguagePrimitives.FloatWithMeasure 0.0, f.YUnit, [||], [||])

        for j in 0 .. f.Coefficients.Length - 1 do
            sum <- sum + f.Coefficients.[j] * f.Basis.[j] x

        sum

    interface IFittedCurve<'ux, 'uy> with
        member f.ValueAt x = f.At x
        member f.Span = f.XRange
        member f.XWitness = f.XUnit
        member f.YWitness = f.YUnit

/// Least-squares fits to uncertain points, weighted by their uncertainties, whose coefficients
/// are uncertain values.
[<RequireQualifiedAccess>]
module Fit =

    /// **A straight line through uncertain points** with its intercept read at `origin`, which a
    /// calibration puts at a reference point (GUM H.3 fits a thermometer's correction about
    /// 20 °C). Each point weighs 1/u², where u² is its y uncertainty squared plus its x
    /// uncertainty times the slope, squared (the effective variance). Points with no uncertainty
    /// at all weigh equally and get the residuals' scatter as their uncertainty, type A with
    /// n − 2 degrees of freedom. A mixture of the two is refused.
    let lineAbout (origin: float<'ux>) (x: seq<Uncertain<'ux>>) (y: seq<Uncertain<'uy>>) : LineFit<'ux, 'uy> =
        let xUnit, xs = LeastSquares.coordinates (Array.ofSeq x)
        let yUnit, ys = LeastSquares.coordinates (Array.ofSeq y)

        // Solved about the points' mean, where slope and intercept are independent and the normal
        // equations well conditioned, and moved to the origin by the uncertain arithmetic.
        let centre = if xs.Length = 0 then 0.0 else Array.averageBy (fun (c: LeastSquares.Coordinate) -> c.Value) xs

        let s =
            LeastSquares.solve "the line" 2 (fun x -> [| 1.0; x - centre |]) (fun _ _ -> [| 0.0; 1.0 |]) yUnit xs ys

        let struct (i0, p0) = s.Parts.[0]
        let struct (i1, p1) = s.Parts.[1]
        let shift = float origin - centre
        let struct (inputs, parts) = Parts.combine i0 p0 1.0 i1 p1 shift
        let lo, hi = LeastSquares.range xs

        { Intercept = Uncertain(Parts.tag (s.Coefficients.[0] + s.Coefficients.[1] * shift), yUnit, inputs, parts)
          Slope = Uncertain(Parts.tag s.Coefficients.[1], U.div yUnit xUnit, i1, p1)
          Origin = origin
          XUnit = xUnit
          YUnit = yUnit
          Points = s.Points
          Dof = s.Dof
          ChiSquared = s.ChiSquared
          Scatter = Parts.tag s.Scatter
          FromScatter = s.FromScatter
          XRange = Parts.tag lo, Parts.tag hi }

    /// **A straight line through uncertain points**, its intercept at x = 0. See `lineAbout`.
    /// An exact x is written `Uncertain.exactly t`.
    let line (x: seq<Uncertain<'ux>>) (y: seq<Uncertain<'uy>>) : LineFit<'ux, 'uy> =
        lineAbout (LanguagePrimitives.FloatWithMeasure 0.0) x y

    /// **A curve linear in its coefficients**, `y = Σ aⱼ fⱼ(x)`, weighted as `lineAbout` weighs a
    /// line: `Fit.linear [ (fun _ -> 1.0); (fun t -> float t); (fun t -> float t ** 2.0) ] t y`
    /// for a parabola in t's unit. Where x is uncertain, each fⱼ′ is the central difference over
    /// one standard uncertainty either side, as `Uncertain.apply` takes it.
    let linear (basis: seq<float<'ux> -> float>) (x: seq<Uncertain<'ux>>) (y: seq<Uncertain<'uy>>) : LinearFit<'ux, 'uy> =
        let basis = Array.ofSeq basis

        if basis.Length = 0 then
            invalidArg "basis" "Marey: a fit needs one basis function or more."

        let xUnit, xs = LeastSquares.coordinates (Array.ofSeq x)
        let yUnit, ys = LeastSquares.coordinates (Array.ofSeq y)
        let at (x: float) = basis |> Array.map (fun f -> f (Parts.tag x))
        let slope (x: float) (h: float) = Array.map2 (fun hi lo -> (hi - lo) / (2.0 * h)) (at (x + h)) (at (x - h))
        let s = LeastSquares.solve "the curve" basis.Length at slope yUnit xs ys
        let lo, hi = LeastSquares.range xs

        { Coefficients =
            Array.init basis.Length (fun j ->
                let struct (inputs, parts) = s.Parts.[j]
                Uncertain(Parts.tag s.Coefficients.[j], yUnit, inputs, parts))
          Basis = basis
          XUnit = xUnit
          YUnit = yUnit
          Points = s.Points
          Dof = s.Dof
          ChiSquared = s.ChiSquared
          Scatter = Parts.tag s.Scatter
          FromScatter = s.FromScatter
          XRange = Parts.tag lo, Parts.tag hi }
