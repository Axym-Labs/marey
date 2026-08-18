# Marey.Tests

§13 of `docs/design.md`, made executable:

> Golden-image snapshots against the SVG backend (text-diffable, reviewable in a PR) plus
> FsCheck property suites for scale laws, `Anim` monoid laws, `Interp` endpoint laws, `Dim`
> group laws, and layout invariants (no overlapping panels, labels inside bounds). This is the
> part where an F# plotting library can be *demonstrably* more correct than its competitors.

```
dotnet test
```

**481 tests: 155 FsCheck properties and 326 examples, ~74 700 generated cases per run.**

Around six seconds of that is `pdflatex`. The TikZ backend has no in-process oracle — the only
authority on whether a `.tex` file is valid is a TeX engine, and the only authority on whether a
figure has anything *in* it is a rasteriser — so the suite runs both. Both degrade to a printed
`SKIPPED:` line when the toolchain is absent; read the output rather than the green tick.

Targets `net10.0`; the projects under test target `netstandard2.1`. Warnings are errors here
too, inherited from `Directory.Build.props`.

Scope is all seven shipped packages: `Marey.Core`, `Marey.Units`, `Marey.Backend.Svg`,
`Marey.Backend.Tikz`, `Marey.Plot`, `Marey.Html` and `Marey.Verso`.

The one package reference that is not a test tool is `Verso.Testing`. It supplies the stub contexts
Verso ships for extension authors, which is what lets `MareyFormatter` be exercised as a real
`IDataFormatter` — `CanFormat` probed with a mime type, `FormatAsync` handed a width and a height —
without a notebook host installed. It replaced `Microsoft.DotNet.Interactive.Formatting`, which went
with the .NET Interactive support it was there to test.

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
| `RegressionTests.fs` | §12.3, §12.4, §12.5, §12.6, §12.1/D2, D16, D17, D32 and D38, locked in. |
| `TexToolchain.fs` | Drives a real `pdflatex`. **The most valuable gate in the TikZ work** — and the one that was briefly vacuous, which is why a deliberately-broken document is part of the suite. |
| `Raster.fs` | Compiles a document and counts pixels, via `pdftoppm -gray`. Measurement only, no assertions; renders a bitmap as ASCII for failure messages. |
| `TikzWriterTests.fs` | The emitter primitives: `bp` dimensions, colour definitions, escaping, superscript runs, macro naming. |
| `TikzBackendTests.fs` | The traversal: the missing y flip, pre-transformed geometry, the honest capability set, batched marker macros, and cross-backend coordinate agreement. |
| `TikzInkTests.fs` | **Somebody looks at the picture.** Compiles figures and asserts ink is where it belongs, mostly by differentials. The only gate that can see orientation. |
| `Golden.fs` | Golden-file infrastructure: location, diff reporting, regeneration. Format-agnostic — `check` compares text and derives its failure artefact from the baseline's extension. |
| `GoldenTests.fs` | The snapshot scenes, the determinism suite, and the backend contract. |
| `TikzGoldenTests.fs` | The same scenes through the TikZ backend, plus the figure baselines both backends share. |
| `PlotTests.fs` | `Marey.Plot`: self-labelling axes, layout invariants across four themes and four sizes, the 50 000-point batching claim, purity of the `with*` combinators, and palette validation. |
| `NotebookTests.fs` | The notebook integration, in three parts: `Html` fragments parsed as XML with the golden suite's own oracle, plus escaping, culture invariance and the two theme rules; `MareyFormatter` driven through Verso's `StubFormatterContext`; and the extension loaded into a genuinely separate collectible `AssemblyLoadContext`, which is the assumption the whole package rests on. |

## Golden images

Baselines live in `golden/` **in the source tree**, because that is the copy a pull request
diffs. They are plain SVG and plain `tikzpicture` source, one element per line, so a review reads
as a list of changed coordinates rather than as a wall of text.

Every scene has both: `paths.svg` and `paths.tex` are the same picture at the same surface in two
languages, from one table in `GoldenTests.Scenes`. Reading them against each other is a check in
itself — `rotated-text.svg` carries `rotate(-30 104 100)` where `rotated-text.tex` carries
`rotate=30` at `(78bp,75bp)`, because the SVG backend flips y and negates the angle while the TikZ
backend does neither, and `104 x 0.75 = 78`. Two independent derivations of one picture, visible
side by side.

`figure.svg` / `figure.tex` are the only baselines built from a `Figure` rather than a hand-made
`Node`, and the only ones that exercise layout, axes, ticks, legend and theme together.

To regenerate after an intentional rendering change:

```
# bash / zsh
MAREY_UPDATE_GOLDEN=1 dotnet test

# PowerShell
$env:MAREY_UPDATE_GOLDEN = "1"; dotnet test; Remove-Item Env:\MAREY_UPDATE_GOLDEN
```

Then **read the diff before committing it.** That is the whole point of the format: a
regenerated baseline that nobody looked at is worse than no baseline, because it launders a
regression into the repository.

On a mismatch the suite writes the rendered document next to the baseline as
`<name>.actual.<ext>` — the extension mirrors the baseline's own, so a `.tex` failure opens in a
TeX editor — and fails with the first differing line plus three lines of context on each side:

```
Marey golden mismatch: axis-composition.svg
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
