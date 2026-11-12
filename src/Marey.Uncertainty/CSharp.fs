namespace Marey.CSharp

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Marey

/// **Values with their uncertainty, for C#**: `Uncertainty.Standard("Length", 1.000, 0.002, Units.Metre)`.
///
/// F# makes an input as `Uncertain.standard "Length" U.metre 1.000<m> 0.002<m>`. C# has no
/// measures, so the unit is an optional argument here, last, as a chart's is: left out, the input
/// states none. A stated unit titles an axis and a report as it does in F#, and adding values of
/// different dimensions throws where F# would not compile.
///
/// The arithmetic needs nothing here: `+`, `-`, `*` and `/` are operators of `Uncertain` itself,
/// with another `Uncertain`, a `double` or a `Quantity`, and so are `Uncertain.Sqrt`,
/// `Uncertain.Exp`, `Uncertain.Log`, `Uncertain.Sin` and the rest. What F# reaches through the
/// module `Uncertain` is here and in `UncertainExtensions`, one line over the function it names.
[<AbstractClass; Sealed>]
type Uncertainty =

    static member private Witness(unit: IUnit) : U<1> =
        if isNull (box unit) then U.unspecified else U.erase unit

    // ---- Inputs -----------------------------------------------------------------------

    /// An input whose standard uncertainty is stated, with infinite degrees of freedom until
    /// `WithDof` says otherwise. See `Uncertain.standard`.
    static member Standard(label: string, value: float, u: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Uncertain<1> =
        Uncertain.standard label (Uncertainty.Witness unit) value u

    /// Repeated readings of one quantity, a type A evaluation: the mean, with the standard
    /// deviation of the mean and n − 1 degrees of freedom. See `Uncertain.ofReadings`.
    static member OfReadings(label: string, readings: IEnumerable<float>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Uncertain<1> =
        Uncertain.ofReadings label (Uncertainty.Witness unit) readings

    /// A reading of an instrument that shows steps of `step`: u = step/√12. See `Uncertain.resolution`.
    static member Resolution(label: string, value: float, step: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Uncertain<1> =
        Uncertain.resolution label (Uncertainty.Witness unit) value step

    /// A value within ± `halfWidth`, no place in between more likely: u = a/√3. See `Uncertain.rectangular`.
    static member Rectangular(label: string, value: float, halfWidth: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Uncertain<1> =
        Uncertain.rectangular label (Uncertainty.Witness unit) value halfWidth

    /// A value within ± `halfWidth`, most likely at its centre: u = a/√6. See `Uncertain.triangular`.
    static member Triangular(label: string, value: float, halfWidth: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Uncertain<1> =
        Uncertain.triangular label (Uncertainty.Witness unit) value halfWidth

    /// A value swinging within ± `halfWidth`, most often near its ends: u = a/√2. See `Uncertain.arcsine`.
    static member Arcsine(label: string, value: float, halfWidth: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Uncertain<1> =
        Uncertain.arcsine label (Uncertainty.Witness unit) value halfWidth

    /// A calibration certificate's expanded uncertainty and its coverage factor k. See
    /// `Uncertain.certificate`.
    static member Certificate(label: string, value: float, expanded: float, k: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Uncertain<1> =
        Uncertain.certificate label (Uncertainty.Witness unit) value expanded k

    /// A certificate's expanded uncertainty and its coverage probability, read as normal. See
    /// `Uncertain.certificateAt`.
    static member CertificateAt(label: string, value: float, expanded: float, coverage: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Uncertain<1> =
        Uncertain.certificateAt label (Uncertainty.Witness unit) value expanded coverage

    /// A value with no uncertainty. See `Uncertain.exact`.
    static member Exact(value: float, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Uncertain<1> =
        Uncertain.exact (Uncertainty.Witness unit) value

    /// Values with no uncertainty, for the exact axis of a fit or a chart. See `Uncertain.exactly`.
    static member Exactly(values: IEnumerable<float>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Uncertain<1>[] =
        let witness = Uncertainty.Witness unit
        values |> Seq.map (Uncertain.exact witness) |> Array.ofSeq

    /// Declares two inputs correlated by `r`. See `Uncertain.correlate`.
    static member Correlate(r: float, a: Uncertain<1>, b: Uncertain<1>) : unit = Uncertain.correlate r a b

    /// A function of one value, its sensitivity read from the function over ± u. See `Uncertain.apply`.
    static member Apply(f: Func<float, float>, x: Uncertain<1>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Uncertain<1> =
        Uncertain.apply (Uncertainty.Witness unit) f.Invoke x

    /// A function of two values. See `Uncertain.apply2`.
    static member Apply(f: Func<float, float, float>, a: Uncertain<1>, b: Uncertain<1>, [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit) : Uncertain<1> =
        Uncertain.apply2 (Uncertainty.Witness unit) (fun x y -> f.Invoke(x, y)) a b

    // ---- What a value is made of ---------------------------------------------------------

    /// The covariance of two values, through the inputs they share and those declared correlated.
    static member Covariance(a: Uncertain<1>, b: Uncertain<1>) : float = Uncertain.covariance a b

    /// The correlation coefficient of two values.
    static member Correlation(a: Uncertain<1>, b: Uncertain<1>) : float = Uncertain.correlation a b

    /// The coverage factor of a normal distribution for probability `p`: 1.96 for 0.95.
    static member NormalFactor(p: float) : float = Coverage.normal p

    /// The coverage factor of Student's t for probability `p` and `dof` degrees of freedom.
    static member StudentFactor(p: float, dof: float) : float = Coverage.studentT p dof

    /// How `Report` writes by default: the concise form, two significant digits, Unicode.
    /// `new ReportFormat(ReportForm.PlusMinus, UnitStyle.Siunitx, 2)` is another.
    static member ReportFormat: ReportFormat = Uncertain.reportFormat

    // ---- Fits ----------------------------------------------------------------------------

    /// A straight line through points with uncertain x and y, weighted by their uncertainties.
    /// See `Fit.line`.
    static member FitLine(x: IEnumerable<Uncertain<1>>, y: IEnumerable<Uncertain<1>>) : LineFit<1, 1> = Fit.line x y

    /// `FitLine` with the intercept read at `origin`, not at zero. See `Fit.lineAbout`.
    static member FitLineAbout(origin: float, x: IEnumerable<Uncertain<1>>, y: IEnumerable<Uncertain<1>>) : LineFit<1, 1> =
        Fit.lineAbout origin x y

    /// A weighted fit of y to a sum of basis functions of x, one coefficient each. See `Fit.linear`.
    static member FitLinear(basis: IEnumerable<Func<float, float>>, x: IEnumerable<Uncertain<1>>, y: IEnumerable<Uncertain<1>>) : LinearFit<1, 1> =
        Fit.linear (basis |> Seq.map (fun f -> f.Invoke)) x y

    // ---- The Monte Carlo check -----------------------------------------------------------

    /// A model of one argument, for `Check`. See `MonteCarlo.model`.
    static member Model(f: Func<Uncertain<1>, Uncertain<1>>, a: Uncertain<1>) : MonteCarloModel<1> = MonteCarlo.model f.Invoke a

    /// A model of two arguments.
    static member Model(f: Func<Uncertain<1>, Uncertain<1>, Uncertain<1>>, a: Uncertain<1>, b: Uncertain<1>) : MonteCarloModel<1> =
        MonteCarlo.model2 (fun a b -> f.Invoke(a, b)) a b

    /// A model of three arguments.
    static member Model
        (f: Func<Uncertain<1>, Uncertain<1>, Uncertain<1>, Uncertain<1>>, a: Uncertain<1>, b: Uncertain<1>, c: Uncertain<1>)
        : MonteCarloModel<1> =
        MonteCarlo.model3 (fun a b c -> f.Invoke(a, b, c)) a b c

    /// A model of four arguments.
    static member Model
        (
            f: Func<Uncertain<1>, Uncertain<1>, Uncertain<1>, Uncertain<1>, Uncertain<1>>,
            a: Uncertain<1>,
            b: Uncertain<1>,
            c: Uncertain<1>,
            d: Uncertain<1>
        ) : MonteCarloModel<1> =
        MonteCarlo.model4 (fun a b c d -> f.Invoke(a, b, c, d)) a b c d

    /// The options `Check` takes by default: adaptive, 95 %, two digits.
    static member MonteCarloDefaults: MonteCarloOptions = MonteCarlo.defaults

    /// Checks the law of propagation against a Monte Carlo evaluation of the model (JCGM 101).
    /// See `MonteCarlo.check`.
    static member Check(model: MonteCarloModel<1>) : MonteCarloCheck<1> = MonteCarlo.check model

    /// `Check` with options of its own. See `MonteCarlo.checkWith`.
    static member Check(model: MonteCarloModel<1>, options: MonteCarloOptions) : MonteCarloCheck<1> = MonteCarlo.checkWith options model

    // ---- Charts --------------------------------------------------------------------------

    /// Points with the error bars their values carry, on x and on y: one standard uncertainty
    /// either side, which the legend says. See
    /// `Chart.errorbars`.
    static member Errorbars(x: IEnumerable<Uncertain<1>>, y: IEnumerable<Uncertain<1>>) : Figure = Chart.errorbars x y

    /// `Errorbars` spanning what `bars` says: `ErrorBars.NewCoverage(0.95)`. See `Chart.errorbarsWith`.
    static member Errorbars(x: IEnumerable<Uncertain<1>>, y: IEnumerable<Uncertain<1>>, bars: ErrorBars) : Figure =
        Chart.errorbarsWith bars x y

    /// A fitted curve with its band, one standard uncertainty either side. See `Chart.fitBand`.
    static member FitBand(fit: IFittedCurve<1, 1>) : Figure = Chart.fitBand fit

    /// `FitBand` spanning what `band` says. See `Chart.fitBandWith`.
    static member FitBand(fit: IFittedCurve<1, 1>, band: ErrorBars) : Figure = Chart.fitBandWith band fit

    /// The Monte Carlo check drawn: its histogram, the linear density and both intervals. See
    /// `Chart.monteCarlo`.
    static member MonteCarloChart(check: MonteCarloCheck<1>) : Figure = Chart.monteCarlo check

/// **What C# asks of an uncertain value**: `g.Report()`, `g.Budget().Text()`, `g.Expanded(0.95)`.
[<Extension; AbstractClass; Sealed>]
type UncertainExtensions =

    /// The input with `dof` degrees of freedom, before it is used. See `Uncertain.withDof`.
    [<Extension>]
    static member WithDof(x: Uncertain<1>, dof: float) : Uncertain<1> = Uncertain.withDof dof x

    /// The value with its standard uncertainty, concisely and rounded as the GUM asks: `9.811(44) m·s⁻²`.
    [<Extension>]
    static member Report(x: Uncertain<1>) : string = Uncertain.report x

    /// The value with its standard uncertainty in a chosen form. See `Uncertain.reportWith`.
    [<Extension>]
    static member Report(x: Uncertain<1>, format: ReportFormat) : string = Uncertain.reportWith format x

    /// The value in another unit of its dimension: `h.Convert(Units.Scaled("mm", 1e-3, Units.Metre))`.
    /// See `Uncertain.convert`.
    [<Extension>]
    static member Convert(x: Uncertain<1>, into: IUnit) : Uncertain<1> = Uncertain.convert (U.erase into) x

    /// The relative standard uncertainty, u/|y|.
    [<Extension>]
    static member Relative(x: Uncertain<1>) : float = Uncertain.relative x

    /// The effective degrees of freedom (Welch–Satterthwaite). See `Uncertain.dof`.
    [<Extension>]
    static member Dof(x: Uncertain<1>) : EffectiveDof = Uncertain.dof x

    /// Each input with its signed contribution to the value. See `Uncertain.contributions`.
    [<Extension>]
    static member Contributions(x: Uncertain<1>) : struct (Input * float)[] =
        Uncertain.contributions x |> Array.map (fun (input, c) -> struct (input, float c))

    /// The expanded uncertainty for coverage probability `p`, its factor read from Student's t at
    /// the effective degrees of freedom. See `Uncertain.expanded`.
    [<Extension>]
    static member Expanded(x: Uncertain<1>, p: float) : Expanded<1> = Uncertain.expanded p x

    /// The expanded uncertainty with a normal distribution's factor. See `Uncertain.expandedAssumingNormal`.
    [<Extension>]
    static member ExpandedAssumingNormal(x: Uncertain<1>, p: float) : Expanded<1> = Uncertain.expandedAssumingNormal p x

    /// The uncertainty budget, largest share first. See `Uncertain.budget`.
    [<Extension>]
    static member Budget(x: Uncertain<1>) : Budget<1> = Uncertain.budget x

    /// An expanded uncertainty with what it rests on: `(100.02147 ± 0.00079) g (k = 2.26, 95 %, ν = 9)`.
    [<Extension>]
    static member Report(e: Expanded<1>) : string = Uncertain.reportExpanded e

    /// The budget as a table of text. See `Budget.text`.
    [<Extension>]
    static member Text(b: Budget<1>) : string = Budget.text b

    /// The budget as a LaTeX `tabular`. See `Budget.latex`.
    [<Extension>]
    static member Latex(b: Budget<1>) : string = Budget.latex b

    /// What the Monte Carlo check found, in a sentence or two. See `MonteCarlo.report`.
    [<Extension>]
    static member Report(c: MonteCarloCheck<1>) : string = MonteCarlo.report c

    /// `Uncertainty.Errorbars` as the next series of a figure, in the next colour.
    [<Extension>]
    static member AddErrorbars(fig: Figure, x: IEnumerable<Uncertain<1>>, y: IEnumerable<Uncertain<1>>) : Figure =
        Chart.addErrorbars x y fig

    /// `AddErrorbars` spanning what `bars` says.
    [<Extension>]
    static member AddErrorbars(fig: Figure, x: IEnumerable<Uncertain<1>>, y: IEnumerable<Uncertain<1>>, bars: ErrorBars) : Figure =
        Chart.addErrorbarsWith bars x y fig

    /// `Uncertainty.FitBand` added to a figure: the line and its band over the points.
    [<Extension>]
    static member AddFitBand(fig: Figure, fit: IFittedCurve<1, 1>) : Figure = Chart.addFitBand fit fig

    /// `AddFitBand` spanning what `band` says.
    [<Extension>]
    static member AddFitBand(fig: Figure, fit: IFittedCurve<1, 1>, band: ErrorBars) : Figure = Chart.addFitBandWith band fit fig

/// **The measured physical constants with their standard uncertainties, for C#**:
/// `UncertainConstants.ElectronMass`, F#'s `Uncertain.Constants.me` itself. The names are
/// `PhysicalConstants`', where the exact constants are, as `Quantity` values an `Uncertain`
/// multiplies directly. See `Uncertain.Constants` for how they are correlated. Each is the F# value
/// itself, so `UncertainConstants.ElectronMass / UncertainConstants.ElectronMass` is exact.
[<AbstractClass; Sealed>]
type UncertainConstants =

    /// G, the Newtonian constant of gravitation, 6.67430(15) × 10⁻¹¹ m³·kg⁻¹·s⁻².
    static member Gravitation = Uncertain.Constants.G

    /// α, the fine-structure constant, 7.2973525643(11) × 10⁻³.
    static member FineStructure = Uncertain.Constants.fineStructure

    /// μ₀, the vacuum magnetic permeability, 1.25663706127(20) × 10⁻⁶ N·A⁻².
    static member VacuumPermeability = Uncertain.Constants.mu0

    /// ε₀, the vacuum electric permittivity, 8.8541878188(14) × 10⁻¹² F·m⁻¹.
    static member VacuumPermittivity = Uncertain.Constants.eps0

    /// m_e, the electron mass, 9.1093837139(28) × 10⁻³¹ kg.
    static member ElectronMass = Uncertain.Constants.me

    /// m_p, the proton mass, 1.67262192595(52) × 10⁻²⁷ kg.
    static member ProtonMass = Uncertain.Constants.mp

    /// m_n, the neutron mass, 1.67492750056(85) × 10⁻²⁷ kg.
    static member NeutronMass = Uncertain.Constants.mn

    /// m_u, the atomic mass constant, 1.66053906892(52) × 10⁻²⁷ kg.
    static member AtomicMass = Uncertain.Constants.atomicMass

    /// R∞, the Rydberg constant, 10973731.568157(12) m⁻¹.
    static member Rydberg = Uncertain.Constants.rydberg

    /// a₀, the Bohr radius, 5.29177210544(82) × 10⁻¹¹ m.
    static member BohrRadius = Uncertain.Constants.bohrRadius

    /// μ_B, the Bohr magneton, 9.2740100657(29) × 10⁻²⁴ J·T⁻¹.
    static member BohrMagneton = Uncertain.Constants.bohrMagneton
