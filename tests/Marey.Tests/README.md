# Marey.Tests

Golden-file snapshots of both backends, text-diffable and reviewable in a pull request, plus
FsCheck property suites for the `Dim` group laws, scale laws, `Interp` endpoint laws and layout
invariants.

```
dotnet test
```

**1461 tests: 198 FsCheck properties and 1263 example-based tests**, and 12 more in the C# project
`tests/Marey.CSharp.Tests`, which `dotnet test Marey.slnx` runs beside this one.

Around six seconds of that is `pdflatex`. The TikZ backend has no in-process oracle — the only
authority on whether a `.tex` file is valid is a TeX engine, and the only authority on whether the
compiled page contains ink is a rasteriser — so the suite runs both. Both degrade to a printed
`SKIPPED:` line when the toolchain is absent, so the output should be read rather than the green
tick alone.

Targets `net10.0`; the projects under test target `netstandard2.1`. Warnings are errors here
too, inherited from `Directory.Build.props`.

Scope is all fifteen shipped packages: `Marey.Core`, `Marey.Units`, `Marey.Plot`, `Marey.Anim`,
`Marey.Fields`, `Marey.Interact`, the four backends (`Svg`, `Tikz`, `Skia`, `Gpu`), `Marey.Html`,
`Marey.Verso`, `Marey.Toolchain`, `Marey.Tools.Video` and `Marey.Tools.Tex`.

The one package reference that is not a test tool is `Verso.Testing`. It supplies the stub contexts
Verso ships for extension authors, which is what lets `MareyFormatter` be exercised as a real
`IDataFormatter` — `CanFormat` probed with a mime type, `FormatAsync` handed a width and a height —
without a notebook host installed.

## Layout

| File | What it holds |
| --- | --- |
| `Generators.fs` | **Start here.** Custom FsCheck generators, the tolerance policy, and the culture-isolation helper. |
| `DimProperties.fs` | `Dim` group laws — associativity, identity, inverse, commutativity, `pow` as n-fold `add`. |
| `WitnessProperties.fs` | Witness algebra: `U.mul`/`U.div`/`recip`/`square` over `Dim` and `ToSI`, plus compile-time instantiation checks. |
| `ScaleProperties.fs` | Round-trip, endpoint and monotonicity laws over all six `ScaleKind`s. |
| `TickProperties.fs` | Tick count, containment and **label injectivity**, plus the measured exceptions. |
| `InterpProperties.fs` | Endpoint and containment laws for `float<'u>`, `Vec2`, `Vec3`, `Color`, `Rect`, and the affine tween. |
| `AffineProperties.fs` | Composition order, associativity, units, inversion — across three coordinate spaces. |
| `GeometryProperties.fs` | `Rect` algebra, `mapRectBounds` under rotation, panel-subdivision invariants, text bounds. |
| `ColorProperties.fs` | Oklab/Oklch round trips, `Color.mix`, `contrastRatio`, capability degradation. |
| `SceneProperties.fs` | **D1** structural equality, `count`/`primitiveCount`, `prune`, and the ten-case guard. |
| `AnimProperties.fs` | The animation algebra: `andThen` associativity and its two identities, `map2` as an applicative, `stretch` composition, a total seek, and the overshoot gate — per `Interp` instance, including the colour case where the answer is different. |
| `RandomProperties.fs` | D95 — `Rng`, SplitMix64 pinned against its published outputs for seed 0, and the ranges of `unit` and `below`. |
| `NumProperties.fs` | D129 — `Num.roundAway`, `Num.upTo` and `Num.fixedPoint` against the .NET calls they replace, on exact halves and on random doubles. |
| `MorphProperties.fs` | Path morphing: the endpoint law stated against `Morph.prepare`, arc-length sampling against vertex-index sampling, subpath pairing and padding, closure policy, overshoot, and totality over deliberately malformed paths. |
| `RegressionTests.fs` | The regression pins: §12's six type-system findings, and D16, D17, D32, D38 and D41, locked in. |
| `Tools.fs` | The registry of the three external programs the suite drives, resolved through `Marey.Toolchain`. |
| `ToolchainTests.fs` | `Marey.Toolchain` itself, including the pipe-buffer deadlock gate — checked against a deliberately restored sequential implementation rather than assumed. |
| `TexToolchain.fs` | Drives a real `pdflatex`. A deliberately-broken document is part of the suite, so that the gate is shown to be able to fail. |
| `Raster.fs` | Compiles a document and counts pixels, via `pdftoppm -gray`. Measurement only, no assertions; renders a bitmap as ASCII for failure messages. |
| `TikzWriterTests.fs` | The emitter primitives: `bp` dimensions, colour definitions, escaping, superscript runs, macro naming. |
| `TikzBackendTests.fs` | The traversal: the missing y flip, pre-transformed geometry, the honest capability set, batched marker macros, and cross-backend coordinate agreement. |
| `TikzInkTests.fs` | Compiles figures and asserts that ink is where it belongs, mostly by differentials. The only gate that can detect orientation. |
| `../Shared/GoldenScenes.fs` | The scenes and figures behind every SVG baseline, and `svg`, the table naming all 28. Compiled here and by `tests/Marey.Fable`. |
| `Golden.fs` | Golden-file infrastructure: location, diff reporting, regeneration. Format-agnostic — `check` compares text and derives its failure artefact from the baseline's extension. |
| `GoldenTests.fs` | The snapshots, the determinism suite, the backend contract, and the check that the shared table covers every SVG baseline. |
| `TikzGoldenTests.fs` | The same scenes through the TikZ backend, plus the figure baselines both backends share. |
| `GuideTests.fs` | Extracts every `fsharp` block from the pages of `docs/guide/`, concatenates them into one script and runs it under `dotnet fsi`, redrawing each figure a page shows into `docs/figures/` and comparing; the README's example likewise. |
| `SkiaTests.fs` | The raster backend: byte determinism for PNG and PDF, the page extent, ink and orientation, the shaping gate, the painter's claim (D131), and the zero-dependency assertion over the built package graph. |
| `GpuTests.fs` | M5 — the GPU renderer against the Skia CPU raster under a stated tolerance (a 5 × 5 blur, then 32 of 255), the tolerance shown to see a marker moved by a pixel, and one renderer driven from many threads. Skips, and says so, where no OpenGL 3.3 context can be made; CI runs it on llvmpipe under `xvfb-run`. |
| `WindowTests.fs` | M5 — the window speaks `LiveMsg`: a scripted session of pointer and key events sends exactly the notebook page's messages and reaches `Live.step`'s state, a recorder in the window keeps the log a notebook would, and a hidden window driven through `Post` matches the pure session after every event and draws the figure the GPU renderer draws. The host half skips where no context can be made. |
| `PlotTests.fs` | `Marey.Plot`: self-labelling axes, layout invariants across four themes and four sizes, the 50 000-point batching claim, purity of the `with*` combinators, and palette validation. |
| `ShotTests.fs` | D72/D73 — one arrangement for a whole animation. That the plot rectangle and the axis title are identical across every frame of a shot, each with its own positive control asserting that per-frame layout *does* move them, plus the union's componentwise rule. |
| `StoryboardTests.fs` | D69 — the storyboard CE. What `let!` binds and what a step's duration defaults to, that `parallel'` inherits `Anim.map2`'s rule rather than restating it, and the `MarkerBatch` split: that addressing a mark still draws the same picture, and that the mark it names is the datum's **original row** rather than its position among the drawn marks. |
| `FrameTests.fs` | D77 — rendering a film. That a frame rendered alone is the frame rendered in sequence and that the sequence rendered backwards is the same sequence; that a concurrent render is the serial one byte for byte, on both the SVG and the PNG backend; that the faces resolve in one order whichever pass renders; and the measurer cache, whose promise is stated exactly — in a serial render `Measurements` equals `Entries`, so no string is ever measured twice. |
| `ChannelTests.fs` | The colour, shape and size channels; the legend and the colorbar they produce; the encoding block, including three `dotnet fsi` snippets that must *fail* to compile. |
| `StatTests.fs` | The statistics, each against a closed form, and the measures that come out of them — a dimensionless count, a probability, a reciprocal-unit density, and a typed slope. Hexbin's points checked against the hexagons the figure draws, loess against the closed form of a tricube-weighted line, and the bootstrap's coverage over 300 seeded trials. |
| `FacetTests.fs` | Small multiples: that every panel reports the same domains and the same plot rectangle, and that a faceted histogram bins every panel into one grid. |
| `GeomTests.fs` | The geoms: the five-number summary, each geom's compiled geometry, the row-alignment gate for channelled layers, and cross-backend coordinate agreement over every geom. |
| `FieldTests.fs` | `Marey.Fields`, and gates 2–11, 13 and 14 of `m4-plan.md`: differences exact on quadratics, units checked by compiling (curl over a phase space must not), contours on their level, the quiver key true to its arrows, streamlines tangent and evenly spaced, LIC deterministic and smooth along the flow, fixed points located and classified, and a film whose colour scale holds still. |
| `TypesetTests.fs` | `Marey.Tools.Tex`: the SVG path reader, the outline's baseline, the scene pass, the degrade on a machine without TeX, and the caches — per call, and the `TexCache` a film holds. |
| `VideoTests.fs` | `Marey.Tools.Video`: the argument vector as a value, a bit-exact encode read back with `ffprobe`, the build tags kept out of the container, and the degrade without `ffmpeg`. |
| `InteractTests.fs` | `Marey.Interact`: one quantity formatter, typed controls (a kelvin slider feeding a view over seconds must not compile), the pure step replayed and run in parallel, zoom and pan read back through linear, log and phase-space scales, the readout checked against the datum under the pointer, the widget page parsed and searched for a number formatted in the browser, a plot rectangle that holds still across every control extreme, and a recording written out, read back and filmed byte for byte. |
| `DiffTests.fs` | D103 — the scene diff and the patch: a patch applied to the old document is the new document byte for byte, a renumbered def sends the whole document, and on a 50 000-point scatter a moving cursor patches under 5% of the document while a zoom sends all of it. |
| `SurfaceTests.fs` | M4.6 — units opt-in (D109–D111, D124–D128). `U.unspecified` makes no claim and `U.one` still does; every unit-optional member draws the bytes its curried form draws; by reflection, every member has a counterpart and an error-obsolete shadow in `Marey.UnitsOfMeasure`, and under that `open` a unit-optional call or a witness-free channel does not compile (through `dotnet fsi`); no two packages export one type name, and nothing in `Marey.CSharp` is ambiguous against `Marey`; `U.parse` composes as `U.div` does and refuses what it cannot read by name. |
| `KeyTests.fs` | D104 — keys the data supplies: a datum keeps its mark's key when another row is removed, a duplicate is refused by name, a `/` cannot forge a path, and a morph pairs a keyed scatter by key, fading what enters and leaves. |
| `ExampleTests.fs` | Runs each script in `examples/` on its own under `dotnet fsi`, and the notebook as its F# cells in order, and fails if one prints a `false`. |
| `NotebookTests.fs` | The notebook integration, in four parts: `Html` fragments parsed as XML with the golden suite's own oracle, plus escaping, culture invariance and the two theme rules; `MareyFormatter` driven through Verso's `StubFormatterContext`; the extension loaded into a genuinely separate collectible `AssemblyLoadContext`, which is the assumption the whole package rests on; and a live figure through fake output channels, which degrades to a static page on a host that has none. |
| `Verification.fs` | The census of every external dependency the suite needs, printed on every run; `MAREY_REQUIRE_TOOLCHAIN`, which turns an absent one into a failure in CI; and gate 15, that both workflows expect the number of packages the solution packs. |

## Golden images

Baselines live in `golden/` **in the source tree**, because that is the copy a pull request diffs.
They are plain SVG and plain `tikzpicture` source, one element per line, so a review reads as a
list of changed coordinates rather than as a wall of text.

Every scene has both: `paths.svg` and `paths.tex` are the same picture at the same surface in two
languages, from one table in `GoldenScenes.Scenes`. Reading them against each other is a check in
itself — `rotated-text.svg` carries `rotate(-30 104 100)` where `rotated-text.tex` carries
`rotate=30` at `(78bp,75bp)`, because the SVG backend flips y and negates the angle while the TikZ
backend does neither, and `104 x 0.75 = 78`.

The `figure-*` baselines are the ones built from a `Figure` rather than from a hand-made `Node`,
and so the only ones that exercise layout, axes, ticks, legend and theme together: `figure` itself,
plus the categorical axis, a channelled figure, a colorbar, a box plot and a heatmap.

To regenerate after an intentional rendering change:

```
# bash / zsh
MAREY_UPDATE_GOLDEN=1 dotnet test

# PowerShell
$env:MAREY_UPDATE_GOLDEN = "1"; dotnet test; Remove-Item Env:\MAREY_UPDATE_GOLDEN
```

Then **read the diff before committing it.** An unread regenerated baseline launders a regression
into the repository, which is what the text format exists to prevent.

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

### Under Node

`tests/Marey.Fable` compiles `tests/Shared/GoldenScenes.fs` and the projects below it with Fable,
and Node renders the 28 SVG baselines and compares them byte for byte. The same run draws every
golden scene with the canvas backend on a context that records each call, and compares the log
with `tests/Marey.Fable/canvas/<name>.canvas.txt`; and it checks that the canvas's text measurer
refuses while a page's fonts are loading. CI runs it as the `fable` job. Locally, with Node on
`PATH`:

```
dotnet tool restore
dotnet fable tests/Marey.Fable -o tests/Marey.Fable/out
node tests/Marey.Fable/out/Main.js tests/Marey.Tests/golden tests/Marey.Fable/canvas
```

A difference writes `<name>.actual.svg` or `<name>.actual.txt` beside the baseline, as the .NET
suite does.

The same project builds against the packed packages instead of the projects when
`MAREY_FROM_PACKAGES` names a version and `MAREY_PACKAGE_SOURCE` a directory holding the
`.nupkg`s. Fable then compiles the sources each package carries under `fable/`, which is what a
Fable app referencing Marey from NuGet gets. The `fable` job runs both.

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

## Generators

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

> **`Tol.colour = 1e-5` — do not tighten.** The sRGB → Oklab → sRGB round trip has an irreducible
> floor of ~1e-6 (measured here at 1.65e-6), because Ottosson's matrices are published to ten
> decimal places and `ofLinear`'s curve has a slope of ~1500 near black. A colour test demanding
> better than 1e-5 will flap indefinitely.

## Tests that assert a defect

A handful of tests are named `DEFECT ...`. Each records known behaviour that contradicts the
intended semantics, asserted **as it currently is**, so that the finding stays visible in the suite
and fixing it is a deliberate, test-breaking act rather than a silent one. They are:

| Test | Finding |
| --- | --- |
| `ScaleProperties.Preconditions` | `Scale.symlog` does not validate its logarithm base, while `Scale.logBase` does. Base 1 gives NaN for every projection outside the linear region; base 0 silently gives a constant map. |
| `TickProperties.MeasuredExceptions` (count) | The `[n/2, 2n]` tick-count bound is false at `n = 1` and `n = 3`. `Ticks.linear 3 (0.49, 921.3)` returns one tick; `Ticks.linear 1 (0.5, 8.0)` returns none. |
| `TickProperties.MeasuredExceptions` (containment) | `Ticks.linear` can emit a tick **below the domain**. `first = ceil(lo/step - 1e-9) * step` compares an absolute epsilon against `lo/step`, so once `lo/step <= 1e-9` the `ceil` drops a grid line. `Ticks.linear 10 (1.0, 1e10)` puts a tick at 0. |
| `TickProperties.InjectivityLimits` | Label injectivity fails once fixed-point formatting runs out of decimals — `Ticks.format` clamps at 15 places — on a dimensionless linear axis below 1e-15, which takes no prefix. A log axis no longer reaches it: past three decades it is written in powers of ten. |
None of them is a weakened property. The properties they sit beside are stated in full, with the
precondition under which they hold made explicit, and every precondition is either a documented
numerical-conditioning bound or a pointer to the defect test that owns the excluded case.

## The seed sweep

A fixed seed can hide a property that is only true for the numbers it happened to draw, so the
suite is also run under ten different replay seeds and once at ten times the case count (~747 000
generated cases). Both sweeps are clean. **Re-run the sweep after adding properties** — an earlier
run of it caught `compose (invert t) t is the identity` holding only for some seeds, because the
composed translation is a three-term cancellation whose error scales with the translation being
cancelled rather than with 1.
