namespace Marey

open System

/// How an input's standard uncertainty was evaluated (GUM 4.2 and 4.3): statistically from
/// repeated readings, or by any other means, such as a resolution, a certificate or a handbook.
[<RequireQualifiedAccess>]
type InputKind =
    | TypeA
    | TypeB

/// **One elementary source of uncertainty**: a reading, a resolution, a calibration. It is made
/// once and has an identity, so two results that both depend on it are known to be correlated
/// through it.
[<Sealed>]
type Input internal (id: int, label: string, value: float, standard: float, dof: float, kind: InputKind, unit: IUnit) =

    /// Unique within a run, and increasing in the order inputs are made.
    member _.Id = id

    /// What a budget calls this input. Empty for one made without a name.
    member _.Label = label

    /// The estimate, as a number in `Unit`.
    member _.Value = value

    /// The standard uncertainty, as a number in `Unit`.
    member _.Standard = standard

    /// The degrees of freedom of `Standard`. Infinite when the uncertainty is taken as exactly known.
    member _.Dof = dof

    member _.Kind = kind

    member _.Unit = unit

    /// The inputs this one was declared correlated with, by identity, and the coefficients.
    member val internal Correlated: (int * float) list = [] with get, set

    override _.ToString() = if label = "" then sprintf "input %d" id else label

/// The effective degrees of freedom of a result (GUM G.4, the Welch–Satterthwaite formula), or
/// why there are none to state.
[<RequireQualifiedAccess>]
type EffectiveDof =
    /// The degrees of freedom, infinite when every contributing input's are.
    | Known of float
    /// The formula does not hold, and `reason` says why.
    | Undefined of reason: string

/// An expanded uncertainty and what it rests on: `Estimate ± Expanded` covers the measurand with
/// probability `Coverage`, where `Expanded` is `CoverageFactor` times the standard uncertainty.
type Expanded<[<Measure>] 'u> =
    { Estimate: float<'u>
      Expanded: float<'u>
      Unit: U<'u>
      CoverageFactor: float
      Coverage: float
      /// The degrees of freedom the factor was read at: the effective ones, truncated to a whole
      /// number as the GUM does (G.4.1), or infinite for a normal distribution.
      Dof: float }

/// The contributions a value carries: its inputs, in order of identity, and beside each the signed
/// `∂y/∂xᵢ · u(xᵢ)` in the value's own unit.
module internal Parts =

    let mutable private last = 0

    /// A fresh identity. Identity only has to be unique within a run, and Fable has no threads.
    let nextId () : int =
#if FABLE_COMPILER
        last <- last + 1
        last
#else
        System.Threading.Interlocked.Increment(&last)
#endif

    let scale (k: float) (parts: float[]) : float[] = Array.map (fun p -> k * p) parts

    /// `ka · a + kb · b`, input by input.
    let combine (ia: Input[]) (pa: float[]) (ka: float) (ib: Input[]) (pb: float[]) (kb: float) : struct (Input[] * float[]) =
        if obj.ReferenceEquals(ia, ib) then
            struct (ia, Array.init pa.Length (fun i -> ka * pa.[i] + kb * pb.[i]))
        elif ib.Length = 0 then
            struct (ia, scale ka pa)
        elif ia.Length = 0 then
            struct (ib, scale kb pb)
        else
            let inputs = Array.zeroCreate<Input> (ia.Length + ib.Length)
            let parts = Array.zeroCreate<float> (ia.Length + ib.Length)
            let mutable i = 0
            let mutable j = 0
            let mutable n = 0

            while i < ia.Length || j < ib.Length do
                if j >= ib.Length || (i < ia.Length && ia.[i].Id < ib.[j].Id) then
                    inputs.[n] <- ia.[i]
                    parts.[n] <- ka * pa.[i]
                    i <- i + 1
                elif i >= ia.Length || ib.[j].Id < ia.[i].Id then
                    inputs.[n] <- ib.[j]
                    parts.[n] <- kb * pb.[j]
                    j <- j + 1
                else
                    inputs.[n] <- ia.[i]
                    parts.[n] <- ka * pa.[i] + kb * pb.[j]
                    i <- i + 1
                    j <- j + 1

                n <- n + 1

            if n = inputs.Length then
                struct (inputs, parts)
            else
                struct (Array.sub inputs 0 n, Array.sub parts 0 n)

    /// Where the input with this identity sits, or -1.
    let indexOf (inputs: Input[]) (id: int) : int =
        let mutable lo = 0
        let mutable hi = inputs.Length - 1
        let mutable found = -1

        while found < 0 && lo <= hi do
            let mid = (lo + hi) / 2
            let at = inputs.[mid].Id

            if at = id then found <- mid
            elif at < id then lo <- mid + 1
            else hi <- mid - 1

        found

    /// The covariance of two values: the products of their contributions from each shared input,
    /// and from each pair of inputs declared correlated.
    let covariance (ia: Input[]) (pa: float[]) (ib: Input[]) (pb: float[]) : float =
        let mutable sum = 0.0

        for i in 0 .. ia.Length - 1 do
            let input = ia.[i]
            let j = if obj.ReferenceEquals(ia, ib) then i else indexOf ib input.Id

            if j >= 0 then
                sum <- sum + pa.[i] * pb.[j]

            for other, r in input.Correlated do
                let k = indexOf ib other

                if k >= 0 then
                    sum <- sum + r * pa.[i] * pb.[k]

        sum

    /// Whether two inputs declared correlated both contribute.
    let correlated (inputs: Input[]) (parts: float[]) : bool =
        let mutable any = false

        for i in 0 .. inputs.Length - 1 do
            if parts.[i] <> 0.0 then
                for other, r in inputs.[i].Correlated do
                    let k = indexOf inputs other

                    if r <> 0.0 && k >= 0 && parts.[k] <> 0.0 then
                        any <- true

        any

    let inline tag<[<Measure>] 'u> (x: float) : float<'u> = LanguagePrimitives.FloatWithMeasure<'u> x

    /// The Welch–Satterthwaite formula (GUM G.4.1), which assumes independent inputs.
    let effectiveDof (inputs: Input[]) (parts: float[]) : EffectiveDof =
        if correlated inputs parts then
            EffectiveDof.Undefined
                "two inputs declared correlated both contribute, and the Welch–Satterthwaite formula holds for independent inputs only"
        else
            let mutable denominator = 0.0
            let mutable variance = 0.0

            for i in 0 .. inputs.Length - 1 do
                let p2 = parts.[i] * parts.[i]
                variance <- variance + p2

                if p2 > 0.0 then
                    denominator <- denominator + p2 * p2 / inputs.[i].Dof

            if denominator = 0.0 then
                EffectiveDof.Known Double.PositiveInfinity
            else
                EffectiveDof.Known(variance * variance / denominator)

    /// A witness that states nothing, at any measure.
    let unstated<[<Measure>] 'u> () : U<'u> = U<'u>("", Dim.zero, 1.0, 0.0, [], false)

    let one: U<1> = U.one

    /// The witness of a square root: every exponent halved, where every exponent is even. A
    /// witness that cannot be halved (the root of a declared `m²/s`) leaves the result's unit
    /// unstated, which prints no symbol, in place of a wrong one.
    let root<[<Measure>] 'v> (u: U<'v^2>) : U<'v> =
        let factors =
            if not (List.isEmpty u.Factors) then u.Factors
            elif u.Symbol = "" then []
            else [ u.Symbol, 1 ]

        let d = u.Dim
        let even (n: int) = n % 2 = 0

        if
            u.Stated
            && not u.IsAffine
            && List.forall (fun (_, e) -> even e) factors
            && List.forall even [ d.L; d.M; d.T; d.I; d.Th; d.N; d.J ]
        then
            let halved = factors |> List.map (fun (s, e) -> s, e / 2)

            let symbol =
                halved
                |> List.map (fun (s, e) -> if e = 1 then s else sprintf "%s^%d" s e)
                |> String.concat "·"

            let dim =
                { L = d.L / 2
                  M = d.M / 2
                  T = d.T / 2
                  I = d.I / 2
                  Th = d.Th / 2
                  N = d.N / 2
                  J = d.J / 2 }

            U<'v>(symbol, dim, sqrt u.ToSI, 0.0, halved, true)
        else
            unstated ()

    /// `b`'s value read in `a`'s unit, and the factor its contributions take with it. Two witnesses
    /// of one measure can differ in scale, and a sum is in the left one's unit, as it is for a
    /// `Quantity`. An unstated witness converts nothing.
    let aligned<[<Measure>] 'u> (a: U<'u>) (b: U<'u>) (x: float<'u>) : struct (float<'u> * float) =
        if obj.ReferenceEquals(a, b) || not a.Stated || not b.Stated || (a.ToSI = b.ToSI && a.OffsetSI = b.OffsetSI) then
            struct (x, 1.0)
        else
            struct (U.convert b a x, b.ToSI / a.ToSI)

    /// The witness a sum reports: the left one's, unless only the right one states a unit.
    let either<[<Measure>] 'u> (a: U<'u>) (b: U<'u>) : U<'u> = if a.Stated || not b.Stated then a else b

/// **A value with its standard uncertainty and where it comes from**: the estimate, and the
/// contribution of every elementary input it depends on. Arithmetic propagates both by the law of
/// propagation of uncertainty (GUM 5.1.2, first order), input by input, so a result knows its
/// combined standard uncertainty, its budget and its degrees of freedom.
///
/// Because each contribution is tied to its input's identity, correlation through a shared input
/// is exact: `x - x` is zero with no uncertainty, and a calibration offset common to two readings
/// cancels in their difference.
///
/// The measure is checked as it is for a `float<'u>`, and the unit's witness is carried as a
/// `Quantity` carries it: `4.0 * pi ** 2.0 * l / (t * t)` is an `Uncertain<m/s^2>` that names its unit.
[<Sealed>]
type Uncertain<[<Measure>] 'u> internal (value: float<'u>, unit: U<'u>, inputs: Input[], parts: float[]) =

    /// The estimate.
    member _.Value = value

    /// The witness of the unit. Unstated for a value made by `+-` and for what is derived from one.
    member _.Unit = unit

    member internal _.Inputs = inputs

    member internal _.Parts = parts

    /// The combined standard uncertainty u(y).
    member _.Uncertainty: float<'u> =
        let variance = Parts.covariance inputs parts inputs parts
        Parts.tag (sqrt (max 0.0 variance))

    // ---- sums ----

    static member (+)(a: Uncertain<'u>, b: Uncertain<'u>) : Uncertain<'u> =
        let struct (bv, k) = Parts.aligned a.Unit b.Unit b.Value
        let struct (inputs, parts) = Parts.combine a.Inputs a.Parts 1.0 b.Inputs b.Parts k
        Uncertain(a.Value + bv, Parts.either a.Unit b.Unit, inputs, parts)

    static member (-)(a: Uncertain<'u>, b: Uncertain<'u>) : Uncertain<'u> =
        let struct (bv, k) = Parts.aligned a.Unit b.Unit b.Value
        let struct (inputs, parts) = Parts.combine a.Inputs a.Parts 1.0 b.Inputs b.Parts -k
        Uncertain(a.Value - bv, Parts.either a.Unit b.Unit, inputs, parts)

    static member (+)(a: Uncertain<'u>, b: float<'u>) : Uncertain<'u> = Uncertain(a.Value + b, a.Unit, a.Inputs, a.Parts)

    static member (+)(a: float<'u>, b: Uncertain<'u>) : Uncertain<'u> = Uncertain(a + b.Value, b.Unit, b.Inputs, b.Parts)

    static member (-)(a: Uncertain<'u>, b: float<'u>) : Uncertain<'u> = Uncertain(a.Value - b, a.Unit, a.Inputs, a.Parts)

    static member (-)(a: float<'u>, b: Uncertain<'u>) : Uncertain<'u> =
        Uncertain(a - b.Value, b.Unit, b.Inputs, Parts.scale -1.0 b.Parts)

    static member (+)(a: Uncertain<'u>, b: Quantity<'u>) : Uncertain<'u> =
        let struct (bv, _) = Parts.aligned a.Unit b.Unit b.Value
        Uncertain(a.Value + bv, Parts.either a.Unit b.Unit, a.Inputs, a.Parts)

    static member (-)(a: Uncertain<'u>, b: Quantity<'u>) : Uncertain<'u> =
        let struct (bv, _) = Parts.aligned a.Unit b.Unit b.Value
        Uncertain(a.Value - bv, Parts.either a.Unit b.Unit, a.Inputs, a.Parts)

    static member (~-)(a: Uncertain<'u>) : Uncertain<'u> = Uncertain(-a.Value, a.Unit, a.Inputs, Parts.scale -1.0 a.Parts)

    // ---- products and quotients ----

    static member (*)(a: Uncertain<'u>, b: Uncertain<'v>) : Uncertain<'u * 'v> =
        let struct (inputs, parts) = Parts.combine a.Inputs a.Parts (float b.Value) b.Inputs b.Parts (float a.Value)
        Uncertain(a.Value * b.Value, U.mul a.Unit b.Unit, inputs, parts)

    static member (/)(a: Uncertain<'u>, b: Uncertain<'v>) : Uncertain<'u / 'v> =
        let q = a.Value / b.Value
        let struct (inputs, parts) = Parts.combine a.Inputs a.Parts (1.0 / float b.Value) b.Inputs b.Parts (-float q / float b.Value)
        Uncertain(q, U.div a.Unit b.Unit, inputs, parts)

    static member (*)(a: Uncertain<'u>, k: float) : Uncertain<'u> = Uncertain(a.Value * k, a.Unit, a.Inputs, Parts.scale k a.Parts)

    static member (*)(k: float, a: Uncertain<'u>) : Uncertain<'u> = Uncertain(k * a.Value, a.Unit, a.Inputs, Parts.scale k a.Parts)

    static member (/)(a: Uncertain<'u>, k: float) : Uncertain<'u> = Uncertain(a.Value / k, a.Unit, a.Inputs, Parts.scale (1.0 / k) a.Parts)

    static member (/)(k: float, a: Uncertain<'u>) : Uncertain<1 / 'u> =
        let q = k / a.Value
        Uncertain(q, U.recip a.Unit, a.Inputs, Parts.scale (-float q / float a.Value) a.Parts)

    static member (*)(a: Uncertain<'u>, b: Quantity<'v>) : Uncertain<'u * 'v> =
        Uncertain(a.Value * b.Value, U.mul a.Unit b.Unit, a.Inputs, Parts.scale (float b.Value) a.Parts)

    static member (*)(a: Quantity<'v>, b: Uncertain<'u>) : Uncertain<'v * 'u> =
        Uncertain(a.Value * b.Value, U.mul a.Unit b.Unit, b.Inputs, Parts.scale (float a.Value) b.Parts)

    static member (/)(a: Uncertain<'u>, b: Quantity<'v>) : Uncertain<'u / 'v> =
        Uncertain(a.Value / b.Value, U.div a.Unit b.Unit, a.Inputs, Parts.scale (1.0 / float b.Value) a.Parts)

    static member (/)(a: Quantity<'v>, b: Uncertain<'u>) : Uncertain<'v / 'u> =
        let q = a.Value / b.Value
        Uncertain(q, U.div a.Unit b.Unit, b.Inputs, Parts.scale (-float q / float b.Value) b.Parts)

    // ---- the functions FSharp.Core's `sqrt`, `abs`, `exp`, `sin`, `**` … resolve to ----

    /// `sqrt` of a squared unit: `sqrt (l * l)` is an `Uncertain<m>`. The measure is the method's
    /// own because a static member's is otherwise the class's.
    static member Sqrt<[<Measure>] 'v>(a: Uncertain<'v^2>) : Uncertain<'v> =
        let r = sqrt (float a.Value)
        Uncertain<'v>(Parts.tag r, Parts.root a.Unit, a.Inputs, Parts.scale (0.5 / r) a.Parts)

    static member Abs(a: Uncertain<'u>) : Uncertain<'u> =
        if a.Value < Parts.tag 0.0 then -a else a

    static member Exp(a: Uncertain<1>) : Uncertain<1> =
        let y = exp a.Value
        Uncertain<1>(y, Parts.one, a.Inputs, Parts.scale y a.Parts)

    static member Log(a: Uncertain<1>) : Uncertain<1> =
        Uncertain<1>(log a.Value, Parts.one, a.Inputs, Parts.scale (1.0 / a.Value) a.Parts)

    static member Log10(a: Uncertain<1>) : Uncertain<1> =
        Uncertain<1>(log10 a.Value, Parts.one, a.Inputs, Parts.scale (1.0 / (a.Value * Math.Log 10.0)) a.Parts)

    static member Sin(a: Uncertain<1>) : Uncertain<1> =
        Uncertain<1>(sin a.Value, Parts.one, a.Inputs, Parts.scale (cos a.Value) a.Parts)

    static member Cos(a: Uncertain<1>) : Uncertain<1> =
        Uncertain<1>(cos a.Value, Parts.one, a.Inputs, Parts.scale (-sin a.Value) a.Parts)

    static member Tan(a: Uncertain<1>) : Uncertain<1> =
        let c = cos a.Value
        Uncertain<1>(tan a.Value, Parts.one, a.Inputs, Parts.scale (1.0 / (c * c)) a.Parts)

    static member Asin(a: Uncertain<1>) : Uncertain<1> =
        Uncertain<1>(asin a.Value, Parts.one, a.Inputs, Parts.scale (1.0 / sqrt (1.0 - a.Value * a.Value)) a.Parts)

    static member Acos(a: Uncertain<1>) : Uncertain<1> =
        Uncertain<1>(acos a.Value, Parts.one, a.Inputs, Parts.scale (-1.0 / sqrt (1.0 - a.Value * a.Value)) a.Parts)

    static member Atan(a: Uncertain<1>) : Uncertain<1> =
        Uncertain<1>(atan a.Value, Parts.one, a.Inputs, Parts.scale (1.0 / (1.0 + a.Value * a.Value)) a.Parts)

    static member Sinh(a: Uncertain<1>) : Uncertain<1> =
        Uncertain<1>(sinh a.Value, Parts.one, a.Inputs, Parts.scale (cosh a.Value) a.Parts)

    static member Cosh(a: Uncertain<1>) : Uncertain<1> =
        Uncertain<1>(cosh a.Value, Parts.one, a.Inputs, Parts.scale (sinh a.Value) a.Parts)

    static member Tanh(a: Uncertain<1>) : Uncertain<1> =
        let t = tanh a.Value
        Uncertain<1>(t, Parts.one, a.Inputs, Parts.scale (1.0 - t * t) a.Parts)

    /// `a ** n` for a number `n`.
    static member Pow(a: Uncertain<1>, n: float) : Uncertain<1> =
        Uncertain<1>(a.Value ** n, Parts.one, a.Inputs, Parts.scale (n * a.Value ** (n - 1.0)) a.Parts)

    /// `a ** b` with both uncertain.
    static member Pow(a: Uncertain<1>, b: Uncertain<1>) : Uncertain<1> =
        let y = a.Value ** b.Value

        let struct (inputs, parts) =
            Parts.combine a.Inputs a.Parts (b.Value * a.Value ** (b.Value - 1.0)) b.Inputs b.Parts (y * log a.Value)

        Uncertain<1>(y, Parts.one, inputs, parts)

    /// The concise report: `9.811(44) m·s⁻²`.
    override this.ToString() = Rounding.write Rounding.standard (float value) (float this.Uncertainty) unit

/// One input's line in an uncertainty budget.
type BudgetRow<[<Measure>] 'u> =
    { Input: Input
      /// The sensitivity coefficient |∂y/∂xᵢ|, as a number: the result's unit per the input's.
      /// Not a number for an input with no uncertainty, which leaves nothing to read it from.
      Sensitivity: float
      /// The contribution |∂y/∂xᵢ| · u(xᵢ), in the result's unit.
      Contribution: float<'u>
      /// The contribution's part of the combined variance u(y)², between 0 and 1 for independent inputs.
      Share: float }

/// **An uncertainty budget**: what each input contributes to a result's uncertainty, largest
/// first. It is the table that says which measurement to improve.
type Budget<[<Measure>] 'u> =
    { Estimate: float<'u>
      /// The combined standard uncertainty.
      Uncertainty: float<'u>
      Unit: U<'u>
      Rows: BudgetRow<'u>[]
      /// The part of the variance that comes from inputs declared correlated: what the rows'
      /// shares leave of 1. Zero for independent inputs, and negative where the correlation
      /// narrows the result.
      Correlations: float
      Dof: EffectiveDof }

/// Writing a budget as a table.
[<RequireQualifiedAccess>]
module Budget =

    let internal ofValue (x: Uncertain<'u>) : Budget<'u> =
        let variance = float x.Uncertainty * float x.Uncertainty

        let rows =
            Array.init x.Inputs.Length (fun i ->
                let input = x.Inputs.[i]
                let part = abs x.Parts.[i]

                { Input = input
                  Sensitivity = (if input.Standard > 0.0 then part / input.Standard else nan)
                  Contribution = Parts.tag<'u> part
                  Share = (if variance > 0.0 then part * part / variance else 0.0) })
            |> Array.sortBy (fun row -> -row.Share)

        { Estimate = x.Value
          Uncertainty = x.Uncertainty
          Unit = x.Unit
          Rows = rows
          Correlations = (if variance > 0.0 then 1.0 - Array.sumBy (fun row -> row.Share) rows else 0.0)
          Dof = Parts.effectiveDof x.Inputs x.Parts }

    let private labelOf (input: Input) = if input.Label = "" then sprintf "input %d" input.Id else input.Label

    let private kindOf (input: Input) =
        match input.Kind with
        | InputKind.TypeA -> "A"
        | InputKind.TypeB -> "B"

    let private percent (share: float) = Num.fixedPoint 1 (100.0 * share)

    let private concise (style: UnitStyle) : ReportFormat = { Rounding.standard with Style = style }

    /// The cells of the table, row by row, with the heading first: input, value with its
    /// standard uncertainty, degrees of freedom, type of evaluation, contribution, share.
    let private cells (style: UnitStyle) (infinity: string) (percentSign: string) (label: string -> string) (math: string -> string) (b: Budget<'u>) =
        let dofText (dof: float) = if Double.IsPositiveInfinity dof then infinity else Num.upTo 1 dof

        [ yield [ "Input"; "Value"; math (if style = Latex || style = Siunitx then "\\nu" else "ν"); "Type"; "Contribution"; "Share" ]

          for row in b.Rows do
              yield
                  [ label (labelOf row.Input)
                    math (Rounding.write (concise style) row.Input.Value row.Input.Standard row.Input.Unit)
                    (if Double.IsPositiveInfinity row.Input.Dof then math infinity else dofText row.Input.Dof)
                    kindOf row.Input
                    math (Rounding.quantity style 2 (float row.Contribution) b.Unit)
                    percent row.Share + percentSign ]

          if abs b.Correlations > 5e-4 then
              yield [ "Correlations"; ""; ""; ""; ""; percent b.Correlations + percentSign ]

          yield
              [ "Combined"
                math (Rounding.write (concise style) (float b.Estimate) (float b.Uncertainty) b.Unit)
                (match b.Dof with
                 | EffectiveDof.Known dof when Double.IsPositiveInfinity dof -> math infinity
                 | EffectiveDof.Known dof -> dofText dof
                 | EffectiveDof.Undefined _ -> "")
                ""
                math (Rounding.quantity style 2 (float b.Uncertainty) b.Unit)
                "" ] ]

    /// The budget as aligned plain text, one input per line and the combined result last.
    let text (b: Budget<'u>) : string =
        let table = cells Unicode "∞" " %" id id b
        let widths = [ for c in 0..5 -> table |> List.map (fun row -> row.[c].Length) |> List.max ]

        table
        |> List.map (fun row ->
            (List.map2 (fun (cell: string) (width: int) -> cell.PadRight width) row widths |> String.concat "  ").TrimEnd())
        |> String.concat "\n"

    /// The budget as a LaTeX `tabular`, needing no package.
    let latex (b: Budget<'u>) : string =
        let math (s: string) = if s = "" then "" else "$" + s + "$"
        let table = cells Latex "\\infty" "\\,\\%" Tex.escape math b
        let line (row: string list) = String.concat " & " row + " \\\\"

        [ yield "\\begin{tabular}{llrcrr}"
          yield "\\hline"
          yield line table.Head
          yield "\\hline"

          for row in table.Tail |> List.take (table.Length - 2) do
              yield line row

          yield "\\hline"
          yield line (List.last table)
          yield "\\hline"
          yield "\\end{tabular}" ]
        |> String.concat "\n"

/// Making uncertain values and asking what they carry.
[<RequireQualifiedAccess>]
module Uncertain =

    let private make (label: string) (unit: U<'u>) (value: float<'u>) (u: float<'u>) (dof: float) (kind: InputKind) : Uncertain<'u> =
        if Double.IsNaN(float u) || u < Parts.tag 0.0 then
            invalidArg "u" (sprintf "Marey: a standard uncertainty is zero or positive, and %s was given %g." (if label = "" then "an input" else "\"" + label + "\"") (float u))

        if Double.IsNaN dof || dof <= 0.0 then
            invalidArg "dof" (sprintf "Marey: degrees of freedom are positive, and these are %g." dof)

        let input = Input(Parts.nextId (), label, float value, float u, dof, kind, unit)
        Uncertain(value, unit, [| input |], [| float u |])

    /// An input whose standard uncertainty is stated: `Uncertain.standard "Length" U.metre 1.000<m> 0.002<m>`.
    /// The uncertainty is taken as exactly known (infinite degrees of freedom) until `withDof` says otherwise.
    let standard (label: string) (unit: U<'u>) (value: float<'u>) (u: float<'u>) : Uncertain<'u> =
        make label unit value u Double.PositiveInfinity InputKind.TypeB

    /// Repeated readings of one quantity, a type A evaluation (GUM 4.2): the mean, with the
    /// experimental standard deviation of the mean s/√n and n − 1 degrees of freedom.
    let ofReadings (label: string) (unit: U<'u>) (readings: seq<float<'u>>) : Uncertain<'u> =
        let xs = readings |> Seq.map float |> Array.ofSeq

        if xs.Length < 2 then
            invalidArg "readings" (sprintf "Marey: \"%s\" has %d reading(s), and a standard deviation needs two or more." label xs.Length)

        let n = float xs.Length
        let mean = Array.sum xs / n
        let s2 = Array.sumBy (fun x -> (x - mean) * (x - mean)) xs / (n - 1.0)
        make label unit (Parts.tag mean) (Parts.tag (sqrt (s2 / n))) (n - 1.0) InputKind.TypeA

    /// A value known to lie within ± `halfWidth` with no place in between more likely than
    /// another (GUM 4.3.7): the standard uncertainty is a/√3.
    let rectangular (label: string) (unit: U<'u>) (value: float<'u>) (halfWidth: float<'u>) : Uncertain<'u> =
        make label unit value (abs halfWidth / sqrt 3.0) Double.PositiveInfinity InputKind.TypeB

    /// A value within ± `halfWidth` and likelier near the middle (GUM 4.3.9): a/√6.
    let triangular (label: string) (unit: U<'u>) (value: float<'u>) (halfWidth: float<'u>) : Uncertain<'u> =
        make label unit value (abs halfWidth / sqrt 6.0) Double.PositiveInfinity InputKind.TypeB

    /// A reading from a scale or a display whose smallest step is `step`: the last digit of a
    /// ruler read to 1 mm. The value lies within half a step either way, so the standard
    /// uncertainty is step/√12.
    let resolution (label: string) (unit: U<'u>) (value: float<'u>) (step: float<'u>) : Uncertain<'u> =
        make label unit value (abs step / sqrt 12.0) Double.PositiveInfinity InputKind.TypeB

    /// A value from a certificate that states an expanded uncertainty `expanded` and its coverage
    /// factor `k` (GUM 4.3.3): the standard uncertainty is U/k.
    let certificate (label: string) (unit: U<'u>) (value: float<'u>) (expanded: float<'u>) (k: float) : Uncertain<'u> =
        if not (k > 0.0) then
            invalidArg "k" (sprintf "Marey: a coverage factor is positive, and \"%s\" was given %g." label k)

        make label unit value (abs expanded / k) Double.PositiveInfinity InputKind.TypeB

    /// A value from a certificate that states an expanded uncertainty and a coverage probability
    /// (`0.95`) in place of a factor: the factor is taken from the normal distribution (GUM 4.3.4).
    let certificateAt (label: string) (unit: U<'u>) (value: float<'u>) (expanded: float<'u>) (coverage: float) : Uncertain<'u> =
        make label unit value (abs expanded / Coverage.normal coverage) Double.PositiveInfinity InputKind.TypeB

    /// A value with no uncertainty that still names its unit.
    let exact (unit: U<'u>) (value: float<'u>) : Uncertain<'u> = Uncertain(value, unit, [||], [||])

    /// The one input a value is, if it is one as it was made and not a result of arithmetic.
    let private elementary (what: string) (x: Uncertain<'u>) : Input =
        if x.Inputs.Length = 1 && x.Parts.[0] = x.Inputs.[0].Standard && float x.Value = x.Inputs.[0].Value then
            x.Inputs.[0]
        else
            invalidArg "x" (sprintf "Marey: %s takes an input as it was made, not a value computed from inputs." what)

    /// The same input with the degrees of freedom of its uncertainty stated, where it is made:
    /// `Uncertain.standard "Gauge" U.metre l u |> Uncertain.withDof 18.0`. The result is a new
    /// input, so values already computed from the old one do not share it.
    let withDof (dof: float) (x: Uncertain<'u>) : Uncertain<'u> =
        let input = elementary "Uncertain.withDof" x
        make input.Label x.Unit x.Value (Parts.tag input.Standard) dof input.Kind

    /// Declares two inputs correlated with coefficient `r`, for inputs that vary together without a
    /// cause the model names. Where the cause is known (one calibration behind two readings), make
    /// it an input both depend on instead: that is exact and needs no coefficient.
    let correlate (r: float) (a: Uncertain<'u>) (b: Uncertain<'v>) : unit =
        if Double.IsNaN r || r < -1.0 || r > 1.0 then
            invalidArg "r" (sprintf "Marey: a correlation coefficient lies between -1 and 1, and this one is %g." r)

        let ia = elementary "Uncertain.correlate" a
        let ib = elementary "Uncertain.correlate" b

        if ia.Id = ib.Id then
            invalidArg "b" "Marey: Uncertain.correlate was given the same input twice."

        ia.Correlated <- (ib.Id, r) :: (ia.Correlated |> List.filter (fun (id, _) -> id <> ib.Id))
        ib.Correlated <- (ia.Id, r) :: (ib.Correlated |> List.filter (fun (id, _) -> id <> ia.Id))

    /// The estimate.
    let value (x: Uncertain<'u>) : float<'u> = x.Value

    /// The combined standard uncertainty.
    let uncertainty (x: Uncertain<'u>) : float<'u> = x.Uncertainty

    /// The witness of the unit.
    let unit (x: Uncertain<'u>) : U<'u> = x.Unit

    /// Each input the value depends on with its contribution `∂y/∂xᵢ · u(xᵢ)`, signed, in the
    /// value's unit and in the order the inputs were made.
    let contributions (x: Uncertain<'u>) : (Input * float<'u>)[] =
        Array.init x.Inputs.Length (fun i -> x.Inputs.[i], Parts.tag<'u> x.Parts.[i])

    /// The covariance u(a, b), from the inputs the two share and those declared correlated.
    let covariance (a: Uncertain<'u>) (b: Uncertain<'v>) : float<'u * 'v> =
        Parts.tag (Parts.covariance a.Inputs a.Parts b.Inputs b.Parts)

    /// The correlation coefficient r(a, b). Zero when either has no uncertainty.
    let correlation (a: Uncertain<'u>) (b: Uncertain<'v>) : float =
        let ua = float a.Uncertainty
        let ub = float b.Uncertainty

        if ua = 0.0 || ub = 0.0 then
            0.0
        else
            Parts.covariance a.Inputs a.Parts b.Inputs b.Parts / (ua * ub)

    /// The effective degrees of freedom by the Welch–Satterthwaite formula (GUM G.4.1). The
    /// formula assumes independent inputs, so a value to which two correlated inputs both
    /// contribute has none to state.
    let dof (x: Uncertain<'u>) : EffectiveDof = Parts.effectiveDof x.Inputs x.Parts

    /// The expanded uncertainty for a coverage probability, `Uncertain.expanded 0.95 g`, with the
    /// coverage factor read from Student's t at the effective degrees of freedom (GUM G.6.4).
    /// Raises when there are no effective degrees of freedom to state, with the reason.
    let expanded (coverage: float) (x: Uncertain<'u>) : Expanded<'u> =
        match dof x with
        | EffectiveDof.Undefined reason ->
            invalidOp (
                sprintf
                    "Marey: no coverage factor for this value, because %s. Uncertain.expandedAssumingNormal takes the factor from a normal distribution, if that assumption is yours to make."
                    reason
            )
        | EffectiveDof.Known nu ->
            // Truncated to a whole number, the side on which the interval is not too narrow.
            let nu = if Double.IsPositiveInfinity nu then nu else max 1.0 (floor nu)
            let k = Coverage.studentT coverage nu

            { Estimate = x.Value
              Expanded = k * x.Uncertainty
              Unit = x.Unit
              CoverageFactor = k
              Coverage = coverage
              Dof = nu }

    /// The expanded uncertainty with the factor of a normal distribution (1.960 for 0.95),
    /// whatever the degrees of freedom: the caller's assumption, stated by the name.
    let expandedAssumingNormal (coverage: float) (x: Uncertain<'u>) : Expanded<'u> =
        let k = Coverage.normal coverage

        { Estimate = x.Value
          Expanded = k * x.Uncertainty
          Unit = x.Unit
          CoverageFactor = k
          Coverage = coverage
          Dof = Double.PositiveInfinity }

    /// The standard uncertainty relative to the estimate, u(y)/|y|.
    let relative (x: Uncertain<'u>) : float = float x.Uncertainty / abs (float x.Value)

    /// A function arithmetic does not reach, into the unit `unit` names:
    /// `Uncertain.apply U.one (fun x -> besselJ0 (float x)) x`. Its slope is the central difference
    /// over one standard uncertainty either side, (f(x + u) − f(x − u)) / 2u (GUM 5.1.3, note 2),
    /// so `f` is called at those two points and at `x`.
    let apply (unit: U<'v>) (f: float<'u> -> float<'v>) (x: Uncertain<'u>) : Uncertain<'v> =
        let h = x.Uncertainty

        if float h = 0.0 then
            Uncertain(f x.Value, unit, x.Inputs, Parts.scale 0.0 x.Parts)
        else
            let slope = float (f (x.Value + h) - f (x.Value - h)) / (2.0 * float h)
            Uncertain(f x.Value, unit, x.Inputs, Parts.scale slope x.Parts)

    /// `apply` for a function of two values, each varied by its own standard uncertainty.
    let apply2 (unit: U<'w>) (f: float<'u> -> float<'v> -> float<'w>) (a: Uncertain<'u>) (b: Uncertain<'v>) : Uncertain<'w> =
        let ha = a.Uncertainty
        let hb = b.Uncertainty

        let slopeA =
            if float ha = 0.0 then 0.0 else float (f (a.Value + ha) b.Value - f (a.Value - ha) b.Value) / (2.0 * float ha)

        let slopeB =
            if float hb = 0.0 then 0.0 else float (f a.Value (b.Value + hb) - f a.Value (b.Value - hb)) / (2.0 * float hb)

        let struct (inputs, parts) = Parts.combine a.Inputs a.Parts slopeA b.Inputs b.Parts slopeB
        Uncertain(f a.Value b.Value, unit, inputs, parts)

    /// How `report` writes: the concise form, the uncertainty to two significant digits, the unit
    /// in Unicode. Change a field for another: `{ Uncertain.reportFormat with Style = Siunitx }`.
    let reportFormat: ReportFormat = Rounding.standard

    /// The value with its standard uncertainty in a chosen form (GUM 7.2.2), rounded as the GUM
    /// asks (7.2.6): `(9.811 ± 0.044) m·s⁻²`, or `\qty{9.811(44)}{\metre\per\second\squared}` for siunitx.
    /// The LaTeX style is for math mode.
    let reportWith (format: ReportFormat) (x: Uncertain<'u>) : string =
        Rounding.write format (float x.Value) (float x.Uncertainty) x.Unit

    /// The value with its standard uncertainty, concisely: `9.811(44) m·s⁻²`.
    let report (x: Uncertain<'u>) : string = reportWith reportFormat x

    /// An expanded uncertainty with what it rests on (GUM 7.2.4):
    /// `(100.02147 ± 0.00079) g (k = 2.26, 95 %, ν = 9)`.
    let reportExpanded (e: Expanded<'u>) : string =
        let interval =
            Rounding.write { reportFormat with Form = ReportForm.PlusMinus } (float e.Estimate) (float e.Expanded) e.Unit

        let dof = if Double.IsPositiveInfinity e.Dof then "∞" else Num.upTo 1 e.Dof
        sprintf "%s (k = %s, %s %%, ν = %s)" interval (Num.fixedPoint 2 e.CoverageFactor) (Num.upTo 2 (100.0 * e.Coverage)) dof

    /// The uncertainty budget: each input's contribution and its share of the variance, largest
    /// first. `Budget.text` and `Budget.latex` write it as a table.
    let budget (x: Uncertain<'u>) : Budget<'u> = Budget.ofValue x

    /// The sine of an angle in radians.
    let sin (a: Uncertain<rad>) : Uncertain<1> =
        let v = float a.Value
        Uncertain<1>(Math.Sin v, Parts.one, a.Inputs, Parts.scale (Math.Cos v) a.Parts)

    /// The cosine of an angle in radians.
    let cos (a: Uncertain<rad>) : Uncertain<1> =
        let v = float a.Value
        Uncertain<1>(Math.Cos v, Parts.one, a.Inputs, Parts.scale (-Math.Sin v) a.Parts)

    /// The tangent of an angle in radians.
    let tan (a: Uncertain<rad>) : Uncertain<1> =
        let v = float a.Value
        let c = Math.Cos v
        Uncertain<1>(Math.Tan v, Parts.one, a.Inputs, Parts.scale (1.0 / (c * c)) a.Parts)

/// `x +- u`: a value with its standard uncertainty, written where it is used.
[<AutoOpen>]
module UncertainOperators =

    /// `9.81<m/s^2> +- 0.02<m/s^2>`: an unnamed input with standard uncertainty `u`. It binds as
    /// `+` does, so it needs parentheses inside a product. It states no unit witness, which a
    /// measure cannot supply: `Uncertain.standard` names the input and its unit.
    let (+-) (x: float<'u>) (u: float<'u>) : Uncertain<'u> = Uncertain.standard "" (Parts.unstated ()) x u
