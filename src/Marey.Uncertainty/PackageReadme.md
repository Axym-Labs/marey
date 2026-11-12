# Marey.Uncertainty

Measurement uncertainty for [Marey](https://axym-labs.github.io/marey/), as the GUM (JCGM 100)
evaluates it. **A value carries its standard uncertainty and every input it rests on, through the
arithmetic and under F# units of measure.**

```fsharp
#r "nuget: Marey.Uncertainty"

open Marey
open Marey.UnitsOfMeasure

let length = Uncertain.resolution "Length" U.metre 0.800<m> 1e-3<m>
let tenSwings = Uncertain.ofReadings "Ten periods" U.second [ 17.951<s>; 17.901<s>; 17.971<s>; 17.921<s>; 17.911<s> ]
let stopwatch = Uncertain.certificate "Stopwatch calibration" U.second 0.0<s> 0.02<s> 2.0

let period = (tenSwings + stopwatch) / 10.0
let g = 4.0 * System.Math.PI ** 2.0 * length / (period * period)   // Uncertain<m/s^2>

Uncertain.report g                                    // 9.823(18) m·s⁻²
Uncertain.budget g |> Budget.text                     // which input to improve
Uncertain.expanded 0.95 g |> Uncertain.reportExpanded // (9.823 ± 0.041) m·s⁻² (k = 2.23, 95 %, ν = 10)
```

## What is in it

- **Inputs as a lab names them.** `ofReadings` (type A: the mean, s/√n, n − 1 degrees of
  freedom), `resolution` (a reading's step, /√12), `rectangular`, `triangular` and `arcsine`
  half-widths, `certificate` (an expanded uncertainty and its k, or `certificateAt` its coverage
  probability), `standard`, and `x +- u` for an unnamed one. `correlate` declares two inputs
  correlated without a shared cause.
- **Arithmetic that remembers.** `+ - * /` with another `Uncertain`, a `float<'u>` or a
  `Quantity`, `sqrt`, `exp`, `log`, the trigonometric and hyperbolic functions, and `apply` for a
  function arithmetic does not reach. An input is one input wherever it is used, so `x - x` is
  exactly zero and a calibration two readings share drops out of their difference. The measure
  is checked by the compiler, and the unit's witness composes as a `Quantity`'s does.
- **Reports.** The concise form `9.823(18) m·s⁻²`, `(9.823 ± 0.018) m·s⁻²`, and siunitx's
  `\qty{9.823(18)}{…}`, rounded as the GUM asks (7.2.6). Effective degrees of freedom
  (Welch–Satterthwaite), expanded uncertainties with Student's t, and the budget as text or a
  LaTeX `tabular`.
- **Charts.** `Chart.uncertain` draws uncertain values as points with their error bars, on x and on y,
  and the legend says what a bar spans. The `plot` block takes them through `xUncertain` and
  `yUncertain`.
- **Weighted fits.** `Fit.line` and `Fit.linear` weight each point by its uncertainty, x's too by
  effective variance. The coefficients are `Uncertain` values whose contributions point back to
  every point's inputs. `Chart.fitBand` draws the curve with its band.
- **A Monte Carlo check** (JCGM 101). `MonteCarlo.check` draws every input from its distribution,
  evaluates the model and says whether the law of propagation's interval holds, running in
  batches until that is settled. `Chart.monteCarlo` draws the comparison.
- **The measured physical constants**, CODATA 2022, as `Uncertain.Constants`, correlated through
  the quantities they rest on.
- **C#.** `Uncertainty`, `UncertainExtensions` and `UncertainConstants`, under `using Marey.CSharp`.
- **Fable.** The package carries its sources, and a Fable app gets .NET's numbers.

## Links

- [The repository](https://github.com/Axym-Labs/marey)
- [The guide on measurement uncertainty](https://axym-labs.github.io/marey/guide/16-uncertainty.html)
