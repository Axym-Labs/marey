# Quiver.Tests

§13 of `docs/design.md`, made executable:

> Golden-image snapshots against the SVG backend (text-diffable, reviewable in a PR) plus
> FsCheck property suites for scale laws, `Anim` monoid laws, `Interp` endpoint laws, `Dim`
> group laws, and layout invariants (no overlapping panels, labels inside bounds). This is the
> part where an F# plotting library can be *demonstrably* more correct than its competitors.

```
dotnet test
```

**310 tests: 155 FsCheck properties and 155 examples, ~74 700 generated cases per run.**

Targets `net10.0`; the projects under test target `netstandard2.1`. Warnings are errors here
too, inherited from `Directory.Build.props`.

Scope is all four shipped packages: `Quiver.Core`, `Quiver.Units`, `Quiver.Backend.Svg` and
`Quiver.Plot`.

## Layout

| File | What it holds |
| --- | --- |
| `Generators.fs` | **Read this first.** Custom FsCheck generators, the tolerance policy, and the culture-isolation helper. |
| `DimProperties.fs` | `Dim` group laws — associativity, identity, inverse, commutativity, `pow` as n-fold `add`. |
| `WitnessProperties.fs` | §12.1 witness algebra: `U.mul`/`U.div`/`recip`/`square` over `Dim` and `ToSI`, plus compile-time instantiation checks. |
| `ScaleProperties.fs` | §4 round-trip, endpoint and monotonicity laws over all five `ScaleKind`s. |
| `TickProperties.fs` | §4 tick count, containment and **label injectivity**, plus the measured exceptions. |
| `InterpProperties.fs` | §7.1 endpoint and containment laws for `float<'u>`, `Vec2`, `Vec3`, `Color`, `Rect`, and the affine tween. |
| `AffineProperties.fs` | Composition order, associativity, units, inversion — across three coordinate spaces. |
| `GeometryProperties.fs` | `Rect` algebra, `mapRectBounds` under rotation, panel-subdivision invariants, text bounds. |
| `ColorProperties.fs` | Oklab/Oklch round trips, `Color.mix`, `contrastRatio`, capability degradation. |
| `SceneProperties.fs` | **D1** structural equality, `count`/`primitiveCount`, `prune`, and the nine-case guard. |
| `RegressionTests.fs` | §12.3, §12.4, §12.5, §12.6, §12.1/D2, D16, D17 and D32, locked in. |
| `Golden.fs` | Golden-file infrastructure: location, diff reporting, regeneration. Format-agnostic — `check` compares text and derives its failure artefact from the baseline's extension. |
| `GoldenTests.fs` | The snapshot scenes, the determinism suite, and the backend contract. |
| `PlotTests.fs` | `Quiver.Plot`: self-labelling axes, layout invariants across four themes and four sizes, the 50 000-point batching claim, purity of the `with*` combinators, and palette validation. |

## Golden images

Baselines live in `golden/` **in the source tree**, because that is the copy a pull request
diffs. They are plain SVG, one element per line, so a review reads as a list of changed
coordinates rather than as a wall of text.

To regenerate after an intentional rendering change:

```
# bash / zsh
QUIVER_UPDATE_GOLDEN=1 dotnet test

# PowerShell
$env:QUIVER_UPDATE_GOLDEN = "1"; dotnet test; Remove-Item Env:\QUIVER_UPDATE_GOLDEN
```

Then **read the diff before committing it.** That is the whole point of the format: a
regenerated baseline that nobody looked at is worse than no baseline, because it launders a
regression into the repository.

On a mismatch the suite writes the rendered document next to the baseline as
`<name>.actual.<ext>` — the extension mirrors the baseline's own, so a `.tex` failure opens in a
TeX editor — and fails with the first differing line plus three lines of context on each side:

```
Quiver golden mismatch: axis-composition.svg
  expected: .../golden/axis-composition.svg
  actual:   .../golden/axis-composition.svg.actual.svg

First difference at line 14, column 23.

      11 |   <path d="..." .../>
      12 |   <path d="..." .../>
      13 |   <path d="..." .../>
  -   14 |   <text x="160" y="180" ...>2.0</text>
  +   14 |   <text x="160" y="181" ...>2.0</text>
      15 |   <text x="220" y="180" ...>2.5</text>

(37 lines expected, 37 lines actual)
```

`*.actual.svg` and `*.actual.tex` are git-ignored, so a failed run can never accidentally be
committed as a baseline.

## Determinism

Three hard rules, all enforced by tests rather than by convention:

1. **Byte-identical across renders.** Every golden scene is rendered twice and compared.
2. **Byte-identical across locales.** Every golden scene is rendered again under `de-DE`
   (comma decimal separator) and `tr-TR` (dotless i) and compared. `SvgWriter.fs` was built for
   this — a German locale emitting `0,5` is a document SVG cannot parse, which is a correctness
   bug, not a cosmetic one. The culture is applied on a dedicated thread, because xUnit runs
   collections in parallel and mutating the ambient culture would leak into other tests.
3. **Fixed FsCheck seed.** Every property runs with `Replay = "17081337,29082027"`. FsCheck
   seeds from the clock by default, which turns a one-in-fifty failure into a test that fails on
   someone else's machine months later. Coverage is bought back with a high `MaxTest` instead.

No test touches the network, the clock, or an unseeded `Random`.

## Generators, and why they are the interesting part

FsCheck's default `float` generator produces NaN, infinity and subnormals. All three are outside
the domain of every law here — a scale cannot round-trip NaN, `prune` cannot be idempotent on a
tree containing one (`nan <> nan` structurally), and an infinite domain has zero span. Feeding
that generator to `Scale.log10` produces a wall of failures that says nothing.

So `Generators.fs` defines a generator per input class, each wrapped in its own single-case type
so a property states in its *signature* what it is quantified over. Several carry a numerical
conditioning bound, and each one records the measurement that set it — for example, `1/x` over
twelve decades has a measured relative round-trip error of 8.8e-5 because the projection is an
exact cancellation of twenty-four decades, so `ReciprocalDomain` spans at most three.

There are no shrinkers, deliberately: every generator maintains an invariant the default
shrinkers would break (a shrunk log domain can contain zero, a shrunk invertible matrix is
singular), and a "minimal counterexample" outside the precondition is not a counterexample.
Generated values print as data instead — `ScaleSpec` is a DU, `Mat` is six floats.

### Tolerances

Every tolerance in `Tol` records the worst error actually measured against the frozen build, and
the headroom it leaves. One is set by a documented finding rather than by measurement:

> **`Tol.colour = 1e-5` — never tighten this.** `decisions.md` F1: the sRGB → Oklab → sRGB round
> trip has an irreducible floor of ~1e-6 (measured here at 1.65e-6), because Ottosson's matrices
> are published to ten decimal places and `ofLinear`'s curve has a slope of ~1500 near black.
> "A colour test demanding better than 1e-5 will flap forever."

## Tests that assert a defect

A handful of tests are named `DEFECT ...`. Each records behaviour that contradicts the design
document, asserted **as it currently is**, so that the finding is visible in the suite and so
that fixing it is a deliberate, test-breaking act rather than a silent one. They are:

| Test | Finding |
| --- | --- |
| `ScaleProperties.Preconditions` | `Scale.symlog` does not validate its logarithm base, while `Scale.logBase` does. Base 1 gives NaN for every projection outside the linear region; base 0 silently gives a constant map. |
| `TickProperties.MeasuredExceptions` (count) | §4's `[n/2, 2n]` tick-count bound is false at `n = 1` and `n = 3`. `Ticks.linear 3 (0.49, 921.3)` returns one tick; `Ticks.linear 1 (0.5, 8.0)` returns none. |
| `TickProperties.MeasuredExceptions` (containment) | `Ticks.linear` can emit a tick **below the domain**. `first = ceil(lo/step - 1e-9) * step` compares an absolute epsilon against `lo/step`, so once `lo/step <= 1e-9` the `ceil` drops a grid line. `Ticks.linear 10 (1.0, 1e10)` puts a tick at 0. |
| `TickProperties.InjectivityLimits` | §4's label injectivity fails once fixed-point formatting runs out of decimals — `Ticks.format` clamps at 15 places. A log axis spanning 36 decades yields 21 ticks all reading `0.000000000000000`; so does a dimensionless axis below 1e-15. `Ticks.formatScientific` exists and is never called. |
**Fixed at M1, and no longer in the table:** `Axis.ofErasedWith` used to ignore the
`ToSI`/`OffsetSI` that D16 added to `IScaleErased`, labelling a reloaded electronvolt axis `kJ`
(wrong by 10¹⁹) and a reloaded Celsius axis `K`. Both decisions now route through
`Prefix.selectForErased` and `UnitDisplay.unitTextErased` — the same functions the measured path
uses — and `RegressionTests.D16 ...` now asserts that the erased and measured labels *agree*,
which is the property that makes §15's reloaded figure survivable.

None of them is a weakened property. The properties they sit beside are stated in full, with the
precondition under which they hold made explicit, and every precondition is a documented
numerical-conditioning bound or a pointer to the defect test that owns the excluded case.

## Why you can trust the fixed seed

A fixed seed can hide a property that is only true for the numbers it happened to draw. Before
being committed, the whole suite was run under ten different replay seeds and once at ten times
the case count (~747 000 generated cases). Both sweeps were clean — and the first version of the
sweep was not: it found that `compose (invert t) t is the identity` held only for some seeds,
because the composed translation is a three-term cancellation whose error scales with the
translation being cancelled, not with 1. That property now states its tolerance against the right
scale. Re-run the sweep after adding properties.
