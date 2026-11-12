namespace Marey

    [<RequireQualifiedAccess>]
    module Coverage =

        val normal: p: float -> float

        val studentT: p: float -> dof: float -> float

namespace Marey

    [<RequireQualifiedAccess>]
    type ReportForm =

        | Concise

        | Explicit

        | PlusMinus

    type ReportFormat =
        {
          Form: ReportForm
          Style: UnitStyle
          Digits: int
        }

namespace Marey

    [<RequireQualifiedAccess>]
    type InputKind =
        | TypeA
        | TypeB

    [<RequireQualifiedAccess>]
    type Distribution =

        | Normal

        | Rectangular

        | Triangular

        | Arcsine

        | StudentT

    [<Sealed>]
    type Input =

        override ToString: unit -> string

        member Distribution: Distribution

        member Dof: float

        member Id: int

        member Kind: InputKind

        member Label: string

        member Standard: float

        member Unit: IUnit

        member Value: float

    [<RequireQualifiedAccess>]
    type EffectiveDof =

        | Known of float

        | Undefined of reason: string

    type Expanded<[<Measure>] 'u> =
        {
          Estimate: float<'u>
          Expanded: float<'u>
          Unit: U<'u>
          CoverageFactor: float
          Coverage: float

          Dof: float
        }

    [<Sealed>]
    type Uncertain<[<Measure>] 'u> =

        static member
          ( * ) : a: Quantity<'v> * b: Uncertain<'u> -> Uncertain<'u 'v>

        static member
          ( * ) : a: Uncertain<'u> * b: Quantity<'v> -> Uncertain<'u 'v>

        static member ( * ) : k: float * a: Uncertain<'u> -> Uncertain<'u>

        static member ( * ) : a: Uncertain<'u> * k: float -> Uncertain<'u>

        static member
          ( * ) : a: Uncertain<'u> * b: Uncertain<'v> -> Uncertain<'u 'v>

        static member (+) : a: Uncertain<'u> * b: Quantity<'u> -> Uncertain<'u>

        static member (+) : a: float<'u> * b: Uncertain<'u> -> Uncertain<'u>

        static member (+) : a: Uncertain<'u> * b: float<'u> -> Uncertain<'u>

        static member (+) : a: Uncertain<'u> * b: Uncertain<'u> -> Uncertain<'u>

        static member (-) : a: Uncertain<'u> * b: Quantity<'u> -> Uncertain<'u>

        static member (-) : a: float<'u> * b: Uncertain<'u> -> Uncertain<'u>

        static member (-) : a: Uncertain<'u> * b: float<'u> -> Uncertain<'u>

        static member (-) : a: Uncertain<'u> * b: Uncertain<'u> -> Uncertain<'u>

        static member
          (/) : a: Quantity<'v> * b: Uncertain<'u> -> Uncertain<'v/'u>

        static member
          (/) : a: Uncertain<'u> * b: Quantity<'v> -> Uncertain<'u/'v>

        static member (/) : k: float * a: Uncertain<'u> -> Uncertain</'u>

        static member (/) : a: Uncertain<'u> * k: float -> Uncertain<'u>

        static member
          (/) : a: Uncertain<'u> * b: Uncertain<'v> -> Uncertain<'u/'v>

        static member (~-) : a: Uncertain<'u> -> Uncertain<'u>

        static member Abs: a: Uncertain<'u> -> Uncertain<'u>

        static member Acos: a: Uncertain<1> -> Uncertain<1>

        static member Asin: a: Uncertain<1> -> Uncertain<1>

        static member Atan: a: Uncertain<1> -> Uncertain<1>

        static member Cos: a: Uncertain<1> -> Uncertain<1>

        static member Cosh: a: Uncertain<1> -> Uncertain<1>

        static member Exp: a: Uncertain<1> -> Uncertain<1>

        static member Log: a: Uncertain<1> -> Uncertain<1>

        static member Log10: a: Uncertain<1> -> Uncertain<1>

        static member Pow: a: Uncertain<1> * b: Uncertain<1> -> Uncertain<1>

        static member Pow: a: Uncertain<1> * n: float -> Uncertain<1>

        static member Sin: a: Uncertain<1> -> Uncertain<1>

        static member Sinh: a: Uncertain<1> -> Uncertain<1>

        static member Sqrt: a: Uncertain<'v ^ 2> -> Uncertain<'v>

        static member Tan: a: Uncertain<1> -> Uncertain<1>

        static member Tanh: a: Uncertain<1> -> Uncertain<1>

        override ToString: unit -> string

        member Uncertainty: float<'u>

        member Unit: U<'u>

        member Value: float<'u>

    type BudgetRow<[<Measure>] 'u> =
        {
          Input: Input

          Sensitivity: float

          Contribution: float<'u>

          Share: float
        }

    type Budget<[<Measure>] 'u> =
        {
          Estimate: float<'u>

          Uncertainty: float<'u>
          Unit: U<'u>
          Rows: BudgetRow<'u> array

          Correlations: float
          Dof: EffectiveDof
        }

    [<RequireQualifiedAccess>]
    module Budget =

        val text: b: Budget<'u> -> string

        val latex: b: Budget<'u> -> string

    [<RequireQualifiedAccess>]
    module Uncertain =

        val standard:
          label: string ->
            unit: U<'u> -> value: float<'u> -> u: float<'u> -> Uncertain<'u>

        val ofReadings:
          label: string ->
            unit: U<'u> -> readings: float<'u> seq -> Uncertain<'u>

        val rectangular:
          label: string ->
            unit: U<'u> ->
            value: float<'u> -> halfWidth: float<'u> -> Uncertain<'u>

        val triangular:
          label: string ->
            unit: U<'u> ->
            value: float<'u> -> halfWidth: float<'u> -> Uncertain<'u>

        val arcsine:
          label: string ->
            unit: U<'u> ->
            value: float<'u> -> halfWidth: float<'u> -> Uncertain<'u>

        val resolution:
          label: string ->
            unit: U<'u> -> value: float<'u> -> step: float<'u> -> Uncertain<'u>

        val certificate:
          label: string ->
            unit: U<'u> ->
            value: float<'u> -> expanded: float<'u> -> k: float -> Uncertain<'u>

        val certificateAt:
          label: string ->
            unit: U<'u> ->
            value: float<'u> ->
            expanded: float<'u> -> coverage: float -> Uncertain<'u>

        val exact: unit: U<'u> -> value: float<'u> -> Uncertain<'u>

        val exactly: values: Q<'u> -> Uncertain<'u> array

        val withDof: dof: float -> x: Uncertain<'u> -> Uncertain<'u>

        val correlate: r: float -> a: Uncertain<'u> -> b: Uncertain<'v> -> unit

        val value: x: Uncertain<'u> -> float<'u>

        val uncertainty: x: Uncertain<'u> -> float<'u>

        val unit: x: Uncertain<'u> -> U<'u>

        val convert: into: U<'u> -> x: Uncertain<'u> -> Uncertain<'u>

        val contributions: x: Uncertain<'u> -> (Input * float<'u>) array

        val covariance: a: Uncertain<'u> -> b: Uncertain<'v> -> float<'u 'v>

        val correlation: a: Uncertain<'u> -> b: Uncertain<'v> -> float

        val dof: x: Uncertain<'u> -> EffectiveDof

        val expanded: coverage: float -> x: Uncertain<'u> -> Expanded<'u>

        val expandedAssumingNormal:
          coverage: float -> x: Uncertain<'u> -> Expanded<'u>

        val relative: x: Uncertain<'u> -> float

        val apply:
          unit: U<'v> ->
            f: (float<'u> -> float<'v>) -> x: Uncertain<'u> -> Uncertain<'v>

        val apply2:
          unit: U<'w> ->
            f: (float<'u> -> float<'v> -> float<'w>) ->
            a: Uncertain<'u> -> b: Uncertain<'v> -> Uncertain<'w>

        val reportFormat: ReportFormat

        val reportWith: format: ReportFormat -> x: Uncertain<'u> -> string

        val report: x: Uncertain<'u> -> string

        val reportExpanded: e: Expanded<'u> -> string

        val budget: x: Uncertain<'u> -> Budget<'u>

        val sin: a: Uncertain<rad> -> Uncertain<1>

        val cos: a: Uncertain<rad> -> Uncertain<1>

        val tan: a: Uncertain<rad> -> Uncertain<1>

        module Constants =

            val fineStructure: Uncertain<1>

            val rydberg: Uncertain</m>

            val G: Uncertain<m ^ 3/(kg s ^ 2)>

            val mu0: Uncertain<N/A ^ 2>

            val eps0: Uncertain<F/m>

            val me: Uncertain<kg>

            val atomicMass: Uncertain<kg>

            val mp: Uncertain<kg>

            val mn: Uncertain<kg>

            val bohrRadius: Uncertain<m>

            val bohrMagneton: Uncertain<J/T>

    [<AutoOpen>]
    module UncertainOperators =

        val (+-) : x: float<'u> -> u: float<'u> -> Uncertain<'u>

namespace Marey

    type IFittedCurve<[<Measure>] 'ux,[<Measure>] 'uy> =

        abstract ValueAt: float<'ux> -> Uncertain<'uy>

        abstract Span: float<'ux> * float<'ux>

        abstract XWitness: U<'ux>

        abstract YWitness: U<'uy>

    [<NoEquality; NoComparison>]
    type LineFit<[<Measure>] 'ux,[<Measure>] 'uy> =
        {

          Intercept: Uncertain<'uy>
          Slope: Uncertain<'uy/'ux>

          Origin: float<'ux>
          XUnit: U<'ux>
          YUnit: U<'uy>
          Points: int

          Dof: int

          ChiSquared: float

          Scatter: float<'uy>

          FromScatter: bool

          XRange: float<'ux> * float<'ux>
        }
        interface IFittedCurve<'ux,'uy>

        member At: x: float<'ux> -> Uncertain<'uy>

        member ReducedChiSquared: float

    [<NoEquality; NoComparison>]
    type LinearFit<[<Measure>] 'ux,[<Measure>] 'uy> =
        {
          Coefficients: Uncertain<'uy> array
          Basis: (float<'ux> -> float) array
          XUnit: U<'ux>
          YUnit: U<'uy>
          Points: int

          Dof: int

          ChiSquared: float

          Scatter: float<'uy>
          FromScatter: bool
          XRange: float<'ux> * float<'ux>
        }
        interface IFittedCurve<'ux,'uy>

        member At: x: float<'ux> -> Uncertain<'uy>

        member ReducedChiSquared: float

    [<RequireQualifiedAccess>]
    module Fit =

        val lineAbout:
          origin: float<'ux> ->
            x: Uncertain<'ux> seq -> y: Uncertain<'uy> seq -> LineFit<'ux,'uy>

        val line:
          x: Uncertain<'ux> seq -> y: Uncertain<'uy> seq -> LineFit<'ux,'uy>

        val linear:
          basis: (float<'ux> -> float) seq ->
            x: Uncertain<'ux> seq -> y: Uncertain<'uy> seq -> LinearFit<'ux,'uy>

namespace Marey

    type MonteCarloOptions =
        {

          Trials: int

          Adaptive: bool

          Seed: int

          Coverage: float

          Digits: int
        }

    [<NoEquality; NoComparison>]
    type MonteCarloModel<[<Measure>] 'u> =

    [<NoEquality; NoComparison>]
    type MonteCarloCheck<[<Measure>] 'u> =
        {
          Trials: int
          Seed: int
          Coverage: float

          Estimate: float<'u>

          Uncertainty: float<'u>

          Low: float<'u>
          High: float<'u>
          Unit: U<'u>

          Linear: Uncertain<'u>

          LinearInterval: Expanded<'u>

          Tolerance: float<'u>

          LowDifference: float<'u>
          HighDifference: float<'u>

          Validated: bool

          Settled: bool

          Noise: float<'u>

          Values: float<'u> array
        }

    [<RequireQualifiedAccess>]
    module MonteCarlo =

        val defaults: MonteCarloOptions

        val model:
          f: (Uncertain<'a> -> Uncertain<'u>) ->
            a: Uncertain<'a> -> MonteCarloModel<'u>

        val model2:
          f: (Uncertain<'a> -> Uncertain<'b> -> Uncertain<'u>) ->
            a: Uncertain<'a> -> b: Uncertain<'b> -> MonteCarloModel<'u>

        val model3:
          f: (Uncertain<'a> -> Uncertain<'b> -> Uncertain<'c> -> Uncertain<'u>) ->
            a: Uncertain<'a> ->
            b: Uncertain<'b> -> c: Uncertain<'c> -> MonteCarloModel<'u>

        val model4:
          f: (Uncertain<'a> ->
                Uncertain<'b> -> Uncertain<'c> -> Uncertain<'d> -> Uncertain<'u>) ->
            a: Uncertain<'a> ->
            b: Uncertain<'b> ->
            c: Uncertain<'c> -> d: Uncertain<'d> -> MonteCarloModel<'u>

        val checkWith:
          options: MonteCarloOptions ->
            m: MonteCarloModel<'u> -> MonteCarloCheck<'u>

        val check: m: MonteCarloModel<'u> -> MonteCarloCheck<'u>

        val report: c: MonteCarloCheck<'u> -> string

namespace Marey

    [<RequireQualifiedAccess>]
    type ErrorBars =

        | Standard

        | Coverage of float

        | CoverageAssumingNormal of float

    [<AutoOpen>]
    module UncertaintyCharts =

        [<RequireQualifiedAccess>]
        module Chart =

            val errorbarsWith:
              bars: ErrorBars ->
                x: Uncertain<'ux> seq -> y: Uncertain<'uy> seq -> Figure

            val errorbars:
              x: Uncertain<'ux> seq -> y: Uncertain<'uy> seq -> Figure

            val addErrorbarsWith:
              bars: ErrorBars ->
                x: Uncertain<'ux> seq ->
                y: Uncertain<'uy> seq -> fig: Figure -> Figure

            val addErrorbars:
              x: Uncertain<'ux> seq ->
                y: Uncertain<'uy> seq -> fig: Figure -> Figure

            val fitBandWith:
              band: ErrorBars -> fit: IFittedCurve<'ux,'uy> -> Figure

            val fitBand: fit: IFittedCurve<'ux,'uy> -> Figure

            val monteCarlo: c: MonteCarloCheck<'u> -> Figure

            val addFitBandWith:
              band: ErrorBars ->
                fit: IFittedCurve<'ux,'uy> -> fig: Figure -> Figure

            val addFitBand: fit: IFittedCurve<'ux,'uy> -> fig: Figure -> Figure

    [<AutoOpen>]
    module UncertaintyPlot =

        type PlotBuilderBase<'r> with

            [<CustomOperation ("xUncertain")>]
            member
              XUncertain: spec: PlotSpec * f: ('r -> Uncertain<'u>) -> PlotSpec
        type PlotBuilderBase<'r> with

            [<CustomOperation ("yUncertain")>]
            member
              YUncertain: spec: PlotSpec * f: ('r -> Uncertain<'u>) -> PlotSpec

namespace Marey.CSharp

    [<AbstractClass; Sealed; Class>]
    type Uncertainty =

        static member
          Apply: f: System.Func<float,float> * x: Marey.Uncertain<1> *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] unit: Marey.IUnit -> Marey.Uncertain<1>

        static member
          Apply: f: System.Func<float,float,float> * a: Marey.Uncertain<1> *
                 b: Marey.Uncertain<1> *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] unit: Marey.IUnit -> Marey.Uncertain<1>

        static member
          Arcsine: label: string * value: float * halfWidth: float *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] unit: Marey.IUnit -> Marey.Uncertain<1>

        static member
          Certificate: label: string * value: float * expanded: float * k: float *
                       [<System.Runtime.InteropServices.Optional;
                         System.Runtime.InteropServices.DefaultParameterValue
                         ((null :> obj))>] unit: Marey.IUnit ->
                         Marey.Uncertain<1>

        static member
          CertificateAt: label: string * value: float * expanded: float *
                         coverage: float *
                         [<System.Runtime.InteropServices.Optional;
                           System.Runtime.InteropServices.DefaultParameterValue
                           ((null :> obj))>] unit: Marey.IUnit ->
                           Marey.Uncertain<1>

        static member
          Check: model: Marey.MonteCarloModel<1> -> Marey.MonteCarloCheck<1>

        static member
          Check: model: Marey.MonteCarloModel<1> *
                 options: Marey.MonteCarloOptions -> Marey.MonteCarloCheck<1>

        static member
          Correlate: r: float * a: Marey.Uncertain<1> * b: Marey.Uncertain<1> ->
                       unit

        static member
          Correlation: a: Marey.Uncertain<1> * b: Marey.Uncertain<1> -> float

        static member
          Covariance: a: Marey.Uncertain<1> * b: Marey.Uncertain<1> -> float

        static member
          Errorbars: x: System.Collections.Generic.IEnumerable<Marey.Uncertain<1>> *
                     y: System.Collections.Generic.IEnumerable<Marey.Uncertain<1>> ->
                       Marey.Figure

        static member
          Errorbars: x: System.Collections.Generic.IEnumerable<Marey.Uncertain<1>> *
                     y: System.Collections.Generic.IEnumerable<Marey.Uncertain<1>> *
                     bars: Marey.ErrorBars -> Marey.Figure

        static member
          Exact: value: float *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] unit: Marey.IUnit -> Marey.Uncertain<1>

        static member
          Exactly: values: System.Collections.Generic.IEnumerable<float> *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] unit: Marey.IUnit ->
                     Marey.Uncertain<1> array

        static member FitBand: fit: Marey.IFittedCurve<1,1> -> Marey.Figure

        static member
          FitBand: fit: Marey.IFittedCurve<1,1> * band: Marey.ErrorBars ->
                     Marey.Figure

        static member
          FitLine: x: System.Collections.Generic.IEnumerable<Marey.Uncertain<1>> *
                   y: System.Collections.Generic.IEnumerable<Marey.Uncertain<1>> ->
                     Marey.LineFit<1,1>

        static member
          FitLineAbout: origin: float *
                        x: System.Collections.Generic.IEnumerable<Marey.Uncertain<1>> *
                        y: System.Collections.Generic.IEnumerable<Marey.Uncertain<1>> ->
                          Marey.LineFit<1,1>

        static member
          FitLinear: basis: System.Collections.Generic.IEnumerable<System.Func<float,
                                                                               float>> *
                     x: System.Collections.Generic.IEnumerable<Marey.Uncertain<1>> *
                     y: System.Collections.Generic.IEnumerable<Marey.Uncertain<1>> ->
                       Marey.LinearFit<1,1>

        static member
          Model: f: System.Func<Marey.Uncertain<1>,Marey.Uncertain<1>> *
                 a: Marey.Uncertain<1> -> Marey.MonteCarloModel<1>

        static member
          Model: f: System.Func<Marey.Uncertain<1>,Marey.Uncertain<1>,
                                Marey.Uncertain<1>> * a: Marey.Uncertain<1> *
                 b: Marey.Uncertain<1> -> Marey.MonteCarloModel<1>

        static member
          Model: f: System.Func<Marey.Uncertain<1>,Marey.Uncertain<1>,
                                Marey.Uncertain<1>,Marey.Uncertain<1>> *
                 a: Marey.Uncertain<1> * b: Marey.Uncertain<1> *
                 c: Marey.Uncertain<1> -> Marey.MonteCarloModel<1>

        static member
          Model: f: System.Func<Marey.Uncertain<1>,Marey.Uncertain<1>,
                                Marey.Uncertain<1>,Marey.Uncertain<1>,
                                Marey.Uncertain<1>> * a: Marey.Uncertain<1> *
                 b: Marey.Uncertain<1> * c: Marey.Uncertain<1> *
                 d: Marey.Uncertain<1> -> Marey.MonteCarloModel<1>

        static member
          MonteCarloChart: check: Marey.MonteCarloCheck<1> -> Marey.Figure

        static member NormalFactor: p: float -> float

        static member
          OfReadings: label: string *
                      readings: System.Collections.Generic.IEnumerable<float> *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] unit: Marey.IUnit ->
                        Marey.Uncertain<1>

        static member
          Rectangular: label: string * value: float * halfWidth: float *
                       [<System.Runtime.InteropServices.Optional;
                         System.Runtime.InteropServices.DefaultParameterValue
                         ((null :> obj))>] unit: Marey.IUnit ->
                         Marey.Uncertain<1>

        static member
          Resolution: label: string * value: float * step: float *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] unit: Marey.IUnit ->
                        Marey.Uncertain<1>

        static member
          Standard: label: string * value: float * u: float *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] unit: Marey.IUnit -> Marey.Uncertain<1>

        static member StudentFactor: p: float * dof: float -> float

        static member
          Triangular: label: string * value: float * halfWidth: float *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] unit: Marey.IUnit ->
                        Marey.Uncertain<1>

        static member MonteCarloDefaults: Marey.MonteCarloOptions

        static member ReportFormat: Marey.ReportFormat

    [<System.Runtime.CompilerServices.Extension; AbstractClass; Sealed; Class>]
    type UncertainExtensions =

        [<System.Runtime.CompilerServices.Extension>]
        static member
          AddErrorbars: fig: Marey.Figure *
                        x: System.Collections.Generic.IEnumerable<Marey.Uncertain<1>> *
                        y: System.Collections.Generic.IEnumerable<Marey.Uncertain<1>> ->
                          Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          AddErrorbars: fig: Marey.Figure *
                        x: System.Collections.Generic.IEnumerable<Marey.Uncertain<1>> *
                        y: System.Collections.Generic.IEnumerable<Marey.Uncertain<1>> *
                        bars: Marey.ErrorBars -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          AddFitBand: fig: Marey.Figure * fit: Marey.IFittedCurve<1,1> ->
                        Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          AddFitBand: fig: Marey.Figure * fit: Marey.IFittedCurve<1,1> *
                      band: Marey.ErrorBars -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member Budget: x: Marey.Uncertain<1> -> Marey.Budget<1>

        [<System.Runtime.CompilerServices.Extension>]
        static member
          Contributions: x: Marey.Uncertain<1> ->
                           struct (Marey.Input * float) array

        [<System.Runtime.CompilerServices.Extension>]
        static member
          Convert: x: Marey.Uncertain<1> * into: Marey.IUnit ->
                     Marey.Uncertain<1>

        [<System.Runtime.CompilerServices.Extension>]
        static member Dof: x: Marey.Uncertain<1> -> Marey.EffectiveDof

        [<System.Runtime.CompilerServices.Extension>]
        static member
          Expanded: x: Marey.Uncertain<1> * p: float -> Marey.Expanded<1>

        [<System.Runtime.CompilerServices.Extension>]
        static member
          ExpandedAssumingNormal: x: Marey.Uncertain<1> * p: float ->
                                    Marey.Expanded<1>

        [<System.Runtime.CompilerServices.Extension>]
        static member Latex: b: Marey.Budget<1> -> string

        [<System.Runtime.CompilerServices.Extension>]
        static member Relative: x: Marey.Uncertain<1> -> float

        [<System.Runtime.CompilerServices.Extension>]
        static member Report: c: Marey.MonteCarloCheck<1> -> string

        [<System.Runtime.CompilerServices.Extension>]
        static member Report: e: Marey.Expanded<1> -> string

        [<System.Runtime.CompilerServices.Extension>]
        static member Report: x: Marey.Uncertain<1> -> string

        [<System.Runtime.CompilerServices.Extension>]
        static member
          Report: x: Marey.Uncertain<1> * format: Marey.ReportFormat -> string

        [<System.Runtime.CompilerServices.Extension>]
        static member Text: b: Marey.Budget<1> -> string

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithDof: x: Marey.Uncertain<1> * dof: float -> Marey.Uncertain<1>

    [<AbstractClass; Sealed; Class>]
    type UncertainConstants =

        static member AtomicMass: Marey.Uncertain<Marey.kg>

        static member BohrMagneton: Marey.Uncertain<Marey.J/Marey.T>

        static member BohrRadius: Marey.Uncertain<Marey.m>

        static member ElectronMass: Marey.Uncertain<Marey.kg>

        static member FineStructure: Marey.Uncertain<1>

        static member
          Gravitation: Marey.Uncertain<Marey.m ^ 3/(Marey.kg Marey.s ^ 2)>

        static member NeutronMass: Marey.Uncertain<Marey.kg>

        static member ProtonMass: Marey.Uncertain<Marey.kg>

        static member Rydberg: Marey.Uncertain</Marey.m>

        static member VacuumPermeability: Marey.Uncertain<Marey.N/Marey.A ^ 2>

        static member VacuumPermittivity: Marey.Uncertain<Marey.F/Marey.m>
