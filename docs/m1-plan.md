# Marey M1 — *Units end to end*: implementation plan

Roadmap reference: `docs/design.md` §14, milestone **M1**. Decision-log reference:
`docs/decisions.md` D1–D27, F1–F2.

> §14 on M1: *"`Marey.Units`, witness canonicalisation, prefix selection, auto axis titles,
> `.NET Interactive` formatter, TikZ backend. **This is the release that has no competitor.**
> Unit-safe plots that drop into a LaTeX document. Announce here."*

Status: **Phases 0–5b complete** (see §3). Decisions numbered D28+ are *proposed* entries for
`docs/decisions.md`; they move into that file as they are implemented and verified, not before.
D36 and D38 were not planned — the compile gate found one and reading a golden baseline found the
other, and both are recorded below.

| Phase | State |
|---|---|
| 0 — Foundations and truth-in-docs | **done**, 288 → 310 tests |
| 1 — `TikzWriter.fs` primitives | **done**, 310 → 348 tests |
| 2 — `TikzBackend.fs` traversal | **done**, 348 → 402 tests |
| 3 — `Theme.tikz` and siunitx wiring | **done**, 402 → 413 tests |
| 4 — Verification | **done**, 413 → 453 tests, 16 new baselines |
| 5 — `Marey.Html` | **done**, 453 → 478 tests |
| 5b — `Marey.Verso`, after the host was retired | **done**, 478 → 481 tests, seventh package |
| 6 — Announce-readiness | LICENSE (MIT), NuGet metadata and `docs/guide.md` done. CI and the `decisions.md` migration remain. |

A real figure now goes end to end: `Chart.line U.second U.metre ts xs |> Chart.withUnitStyle
Siunitx |> Chart.render TikzBackend.render` produces a `tikzpicture` that compiles under
`pdflatex` with zero errors, to a PDF page of **exactly** the requested extent (400×250 px →
300×187.5 pt), with the axis titles typeset by `siunitx` from the unit witnesses and the body font
inherited from the document.

---

## 0. Where M0 actually left off

Measured, not recalled:

| | |
|---|---|
| Build | `dotnet build` clean, warning-free, `TreatWarningsAsErrors=true` |
| Tests | `dotnet test` — **288 passed, 0 failed**, 238 ms |
| SDK | .NET **10.0.301** (`docs/design.md` §17 still says 10.0.110) |
| Packages | `Marey.Core`, `Marey.Units`, `Marey.Backend.Svg`, `Marey.Plot`, `Marey.Tests` |
| Goldens | 13 tracked SVG baselines under `tests/Marey.Tests/golden/` |

**The important discovery: M0 already built most of what §14 assigns to M1.** `Marey.Units`
ships all four `UnitStyle` renderers (`Unicode`, `Ascii`, `Latex`, `Siunitx`) with no stubbed
branch, `Dim`-keyed canonicalisation over 17 SI entries, the full 24-prefix ladder with the
kilogram shift and affine/non-coherent refusals, and `Axis.ofScaleWith` → self-labelling axis
titles. `Chart.withUnitStyle Siunitx` is already wired to both axes.

So M1's remaining scope is **narrower and sharper than §14 implies**, and this plan is written
against the actual delta rather than against the roadmap line.

### The delta

| # | Item | Size |
|---|---|---|
| 1 | `Marey.Backend.Tikz` — the headline deliverable | large |
| 2 | `Marey.Html` — .NET Interactive formatters | small |
| 3 | `Theme.tikz` — the document-font-inheriting theme §9 asks for | small |
| 4 | `TextRun.Tex` — the route by which TeX source reaches a TeX backend | medium, touches the IR |
| 5 | `Axis.ofErasedWith` ignores `ToSI`/`OffsetSI` — a live defect | small |
| 6 | Golden harness is SVG-hardcoded in three places | small |
| 7 | Announce-readiness: LICENSE, packaging, CI | medium, mechanical |
| 8 | Truth-in-docs: three stale documents | small |

---

## 1. Verified environment findings

These were established by compiling real documents on this machine, and they are what the TikZ
backend's design rests on. Recorded because re-deriving them costs an hour.

**The TikZ path compiles clean.** A document using `bp` coordinates, `line width`,
`dash pattern=on 3bp off 2bp`, `fill opacity`, a `clip` scope, `\definecolor{...}{rgb}{...}`,
and `\node[anchor=base west,rotate=30,font=\fontsize{9.6}{11.52}\selectfont]` compiles under
`pdflatex` with **zero errors**. Every TikZ construct this backend needs is available with only
`\usepackage{tikz}`.

**The `textcomp` fallback path also compiles clean.** `\textperiodcentered`,
`\textsuperscript{-2}`, `\textohm`, `\textmu`, `\textdegree` all work — so a self-contained
non-siunitx rendering of `kg·m²·s⁻²` is available as a fallback and for the `Unicode` style.

**siunitx: initially broken, now working, and the existing Marey output is valid.** The first
probe failed on `\l__siunitx_print_unit_mode_str` — `siunitx.sty` v3.5.5 (2026-05-15) against a
LaTeX format dated 2025-06-01. After a MiKTeX package update all of the following compile with
zero errors and render identically:

```
A  x [\qty{}{\metre\per\second\squared}]   ->  x [m s-2]     <- what Marey emits today
B  x [\unit{\metre\per\second\squared}]    ->  x [m s-2]
E  T [\qty{}{\degreeCelsius}]              ->  T [°C]
```

**`\qty{}{…}` is therefore not a defect.** An earlier suspicion that the empty first argument
was invalid, or that it swallowed the preceding space, did not survive testing: A and B are
indistinguishable in output. Switching to `\unit{…}` remains defensible on semantic grounds —
it is siunitx v3's documented macro for a unit standing alone, and it does not depend on an
empty mandatory argument — but it is **optional polish, not a fix**, and it is listed as such
in §6.

**Consequence for CI:** the golden-compile gate must tolerate a machine with no TeX, and must
distinguish "TeX absent" (skip) from "TeX present and the document is broken" (fail). A stale
kernel is a third state that looks like the second; the gate should report the siunitx version
it found so that failure mode is diagnosable from a CI log rather than reproducible only
locally.

---

## 2. Decisions taken up front

**Migrated.** These were drafted here as proposals and now live in
[`docs/decisions.md`](decisions.md) as built-and-verified entries, in the same house form as
D1–D27. The full text is there and only there — keeping a second copy in the plan is how two
documents start disagreeing about what was decided.

Four of the thirteen were not taken up front at all, which is the part worth remembering about
this section's title: D36 and D38 were found by a TeX engine and by reading a golden baseline,
D37 by discovering that D35 described something a `Theme` could not do, and D39 was rewritten
outright when .NET Interactive was retired mid-milestone.

| | |
|---|---|
| D28 | TikZ coordinates are emitted in `bp`, never `pt` **[forced]** |
| D29 | TikZ does not flip y; SVG does **[forced]** |
| D30 | Geometry is pre-transformed; no picture-level `scale` is emitted |
| D31 | TikZ capabilities at M1, and `Degrade`'s first real customer |
| D32 | `TextRun.Tex : string option` — how TeX source crosses the IR |
| D33 | Markers: one `\providecommand` per distinct (shape, size), then one call per point |
| D34 | Deterministic number formatting is promoted to `Marey.Core` **[forced]** |
| D35 | `Theme.tikz` pins the font *size* and inherits the font *family* |
| D36 | TeX cannot represent an arbitrary coordinate, and D9 guarantees we will hand it one |
| D37 | The unit style belongs to the theme, and the axis config overrides it |
| D38 | Three characters TeX accepts and then typesets as something else |
| D39 | The notebook renderer knows nothing about the notebook **[superseded in part]** |
| D40 | The Verso extension does not reference Marey |


## 3. Phase plan

Six phases. Each has an exit criterion that is a command, not a feeling.

### Phase 0 — Foundations and truth-in-docs

Small, unblocks everything, and clears three stale documents before they mislead further work.

1. `Marey.Core`: add the `Num` module (D34); repoint `Svg.num`/`Svg.numAt` at it.
2. `Marey.Core`: add `Tex: string option` to `TextRun` (D32); fix `Scene.prune`'s empty test;
   fix every construction site in `Marey.Plot` and the tests.
3. `Marey.Units`: `AxisSpec.TitleTex`/`UnitTextTex`; populate in `Axis.ofScaleWith`.
4. `Marey.Units`: fix `Axis.ofErasedWith` to consult `ToSI`/`OffsetSI` — route through
   `UnitDisplay.renderSymbol` when `ToSI <> 1.0 || OffsetSI <> 0.0`, mirroring what
   `UnitDisplay.unitText` already does. **Re-pin** `RegressionTests.fs`'s eV and °C cases to the
   correct labels (`E [keV]`, `T [°C]`) and delete the stale "gap in `Marey.Core`" comment at
   `Axis.fs:36-45`.
5. `Marey.Plot`: add `Chart.toFile` as the backend-neutral name for what `Chart.toSvg` does;
   keep `toSvg` as an alias so nothing breaks.
6. Golden harness: `Golden.check` is already backend-agnostic, but the failure artefact is
   hard-coded to `.actual.svg`, `Golden.render` calls `SvgBackend` directly, and the fsproj copy
   glob is `*.svg`. Parameterise the extension; add `*.tex` to the glob and `*.actual.tex` to
   `.gitignore`.
7. Docs: root `README.md` says "M0 in progress" (line 12) and documents a `spike/` directory
   **that does not exist** in the working tree, in two places. `tests/Marey.Tests/README.md`
   says `Marey.Plot` is not referenced and `PlotTests.fs` is "empty on purpose"; both are false
   — it is 392 lines of real tests — and the test count reads 272 against an actual 288.
   `docs/design.md` §17 cites SDK 10.0.110 against an installed 10.0.301.

**Exit:** `dotnet build` clean; `dotnet test` green at ≥ 288 with the two re-pinned cases now
asserting correct labels; no document asserts something the tree contradicts.

### Phase 1 — `TikzWriter.fs`: the primitives

The analogue of `SvgWriter.fs`, and written for the same two readers (the renderer and the
golden-diff reviewer).

- Number emission on top of Core's `Num` (D34), with a `bp` suffix helper (D28).
- **TeX escaping** for the `Text` fallback: `\ % $ # & _ { }` plus `~` and `^`, which need
  `\textasciitilde{}`/`\textasciicircum{}` rather than a backslash.
- **Unicode → TeX table** for the fallback path: `·`, `⁻`, superscript digits, `Ω`, `µ`, `Å`,
  `°`. Verified to compile against `textcomp` (§1).
- `\definecolor` allocation: deduplicated, deterministic, emitted in traversal order —
  `SvgDocument.Def`'s discipline, not a new one.
- Document assembly: `TikzDocument` buffering a body plus an ordered definition list, mirroring
  `SvgDocument` so the two backends fail in the same ways.

**Exit:** unit tests on escaping, the Unicode table, number formatting and colour dedup. Nothing
renders a scene yet.

### Phase 2 — `TikzBackend.fs`: the traversal

All nine IR nodes, `IBackend` implemented, caps per D31.

- `Begin` builds the unflipped root affine (D29).
- `Path` → `\path[draw=..,line width=..bp,dash pattern=..]`; `MoveTo`/`LineTo`/`CubicTo`
  (`.. controls .. and .. ..`)/`ClosePath` (`-- cycle`); `EvenOdd` → `even odd rule`.
- `Text` → `\node[anchor=,rotate=,font=]`, honouring `Tex` when present (D32), anchor and
  baseline mapped to TikZ's `anchor=` grid, rotation per D29.
- `Markers` → D33.
- `Group` emits nothing; `Transform` composes into the CTM (D30); `Clip` → `\begin{scope}\clip`;
  `Keyed` → a `% marey-key:` comment; `Annotated` → `% marey-role:` comments (D31);
  `Raster` → a visible skip comment, in the spirit of the SVG backend's raster diagnostics.
- Entry points mirroring `SvgBackend`: `member Result`, `TikzBackend.render`,
  `renderToFile`, plus `renderStandalone` (a complete `\documentclass{standalone}` document)
  and `TikzBackend.preamble` (the `\usepackage` lines a host document needs).
- `render` degrades internally (D31).

**Exit:** every one of the 13 golden scenes renders to a `.tex` string without throwing.

### Phase 3 — `Theme.tikz` and the siunitx wiring

D35, plus threading `AxisSpec.TitleTex` through `Axes.compile` into `TextRun.Tex`, and the same
for `Legend`. This is the phase where the M1 headline becomes true end to end.

**Exit:** `Chart.line U.second U.metre ts xs |> Chart.withTheme Theme.tikz |> Chart.toFile
TikzBackend.render "fall.tex" size` produces a `.tex` containing a literal
`\qty{}{\metre}`-style axis title, and that file compiles.

### Phase 4 — Verification

The gates in §4. This is the phase that makes the backend trustworthy rather than plausible, and
it is not optional in a project whose selling point is demonstrable correctness (§13).

**Done.** 453 tests. Fifteen `.tex` baselines and one new `.svg`, all read before being committed —
which is the only reason this phase found anything.

What it found:

- **D38, a silent mis-typesetting bug.** `<`, `>` and `|` are legal in TeX text mode and raise no
  error of any kind; in the OT1 encoding LaTeX still defaults to, those slots hold `¡`, `¿` and `—`.
  An axis label reading `a < b` typeset as `a ¡ b` in a document that compiled perfectly cleanly.
  Invisible to the compile gate by construction — there is nothing wrong with the TeX. Found by
  reading `text.tex`, confirmed by compiling both forms and comparing the rasters, fixed in
  `Tex.tryEscapeChar`. `"`, `` ` `` and the `--` ligatures were checked in the same probe and
  deliberately left alone: conventional TeX typography, and `	extquotedbl` is a hard error in OT1.
- **Gate 5 was overstated** and is corrected in place below.
- **`Scenes.all` now carries each scene's surface**, because a golden pair is only worth having if
  both halves describe the same picture at the same size. The determinism suite improved as a
  side effect: it used to render every scene at the default surface, including the two whose
  baselines use a different one.
- **A stale-artefact leak in `Golden.check`**: accepting a baseline left the `.actual` candidate
  from the failing run beside it, so a reviewer could end up diffing two identical files.

**Exit:** met.

### Phase 5 — `Marey.Html`

§6: *"a ~200-line package registering a .NET Interactive formatter so `figure` displays inline
as SVG. Do it at M1; it is how people will first meet the library."*

**Done.** 478 tests. `src/Marey.Html/` is `Html.fs` (pure `something -> string`, no
reference to .NET Interactive) plus `Formatters.fs` (nine registrations), an `extension.dib` inside
the package, and `docs/notebooks/marey-tour.dib`. Nine types display: `Figure`, `Node`, `Color`,
`Ramp`, `Color[]`, `PaletteReport`, `Dim`, `U<'u>` and `IScaleErased`.

Four things in the plan above were wrong, and are corrected in place rather than quietly:

- **The TFM.** `net10.0` was chosen on the grounds that `Microsoft.DotNet.Interactive.Formatting`
  "requires a modern TFM". It ships `lib/netstandard2.0` and nothing else. Worse, the reasoning had
  the dependency backwards: this assembly is loaded *into someone else's process*, and a `net10.0`
  assembly cannot be loaded by a `dotnet-interactive` running on net8.0 or net9.0 — which is
  whatever the reader installed last year. It targets **netstandard2.1**, like everything else.
- **The dependency is compiled against and not shipped** (`PrivateAssets="all"`). .NET Interactive
  has never published a stable version, so declaring the dependency earns NU5104 — and with
  `TreatWarningsAsErrors` that makes `dotnet pack` *fail*, which Phase 6 would have discovered the
  hard way. Not shipping it also avoids the classic notebook failure where `Formatter.Register`
  writes into a different assembly's static registry than the kernel reads from. Safe because every
  build of that package carries `AssemblyVersion 1.0.0.0`, checked against the assembly.
  The stated cost: `Formatters.register()` throws `FileNotFoundException` with no kernel present.
  `Html.*` does not — verified by running all seventeen notebook cells under `dotnet fsi`, where
  every one succeeded and only the final `registerWith` call raised.
- **The test scope.** The plan concluded from `dotnet-interactive` being uninstalled that only the
  string functions could be tested. The *tool* and the *library* are separable: the test project
  references the Formatting package directly, so `Formatter.ToDisplayString` proves each of the nine
  registrations really is reachable under `text/html`. That is where the bugs would have been.
- **`Palette` is not a type.** There is only the module, so the registration is for `Color[]` —
  which means `theme.Categorical` displays as a strip too, at no extra cost.

What it found:

- **`SetPreferredMimeTypesFor` does nothing, and the test that "verified" it was theatre.** The
  comment justifying the call claimed a registration without a stated preference "silently falls
  back to a reflection dump". Measured: `Formatter.GetPreferredMimeTypesFor` answers `["text/html"]`
  for *every* type — `int` included — before any registration, and keeps answering that after
  `DefaultMimeType` is set to `text/plain`. The first test asserted the preference *was* `text/html`
  and therefore **passed with the call deleted** — the same failure mode gate 1 caught in itself in
  Phase 1, found the same way: by breaking the thing and seeing nothing fail. The call stays as a
  statement of intent; the comment now says what was measured, and the test was rewritten to check
  the composition a notebook cell actually performs.
- **A negative hue angle.** `Color.toOklch` builds its hue from `atan2`, so it lands in
  (−180°, 180°] and the swatch for `Palette.blue` read `oklch(0.532 0.131 -115.95°)`. Correct and
  useless: nobody publishes hues that way, and the library's own vocabulary is 0–360 —
  `Palette.divergingBetween` is called with 250° and 27°. Normalised in `Html`, not in
  `Color.toOklch`, because it is a presentation choice and Core's consumer of that angle is
  `ofOklch`, which is periodic and does not care. **Found by rendering the fragments and looking at
  them**, exactly as D38 was.
- **A stale claim in `Golden.fs`.** Extracting `projectDir` for reuse surfaced a comment asserting
  that an update run without a source tree present is "refused below". Nothing refuses it. Corrected.

Two rules the fragments follow, both learned from how this goes wrong elsewhere: **no `<style>`
element** (it is not scoped to the output cell, so it restyles the reader's whole notebook and
stacks up once per execution) and **no pinned `color` or `background`** outside the swatches, since
a notebook is light or dark and offers no way to ask which. Both are asserted, over every fragment.

**Exit:** met, with one honest gap. Formatter tests are green — 25 of them, including a
`XDocument.Parse` well-formedness pass over every fragment, the same oracle the SVG golden suite
uses. The notebook cannot be *run* here: no kernel. Instead all seventeen of its F# cells were
extracted and evaluated against the built assemblies, and the fragments were rendered in headless
Edge against both a light and a dark background and inspected.

### Phase 5b — the host was retired between writing this and shipping it

**Polyglot Notebooks was deprecated 2026-02-11 and retired 2026-03-27; .NET Interactive followed
2026-04-24.** §6's sentence — "a ~200-line package registering a .NET Interactive formatter" —
names a product that no longer exists. The maintained successor is
[Verso](https://github.com/DataficationSDK/Verso): MIT, open source, imports `.dib` and `.ipynb`,
and every built-in feature down to the C# kernel is an extension on public interfaces.

**The symptom, reported from a real notebook:** a `Figure` renders as an expandable table of its
fields. Diagnosed from Verso's source rather than guessed. Verso resolves output through
`IDataFormatter`s ordered by priority; `Verso.FSharp`'s own formatter sits at **20** and claims
*every* F# record, union and tuple. Nothing of ours ran, and nothing could have:
`extension.dib` is a .NET Interactive mechanism with no Verso equivalent — an extension there is a
separate package the reader installs and approves, loaded into an isolated collectible
`AssemblyLoadContext`.

**Decided and done:**

- **The .NET Interactive support was deleted, not carried.** `Formatters.fs`, the packaged
  `extension.dib`, the `Microsoft.DotNet.Interactive.Formatting` reference and the registration
  tests are gone. A dependency on a retired engine is not free: it is a prerelease reference in
  every `dotnet pack` and a page of documentation pointing readers at something they cannot install.
- **`Html.fs` did not change**, which is the whole point of D39 and is now the evidence for it.
- **`src/Marey.Verso/`** is the new seventh package. D40 has the design; the short version is
  priority 30, `Verso.Abstractions` as its only dependency, and no reference to Marey at all.

What it found, beyond the obvious:

- **The cheap route is a coin flip.** Verso's built-in `HtmlFormatter` claims anything with a
  `ToHtml()` method and needs no extension — and it is *also* priority 20, against a stable sort.
  Adding `ToHtml()` to `Figure` would have produced a picture or a table depending on which
  extension the host enumerated first, and no test we could write would have caught it reliably.
- **`MaxWidth`/`MaxHeight` replace a guess.** `Html.defaultSize`'s 720×450 was the one number in
  Phase 5 with no argument behind it. Verso hands the formatter the cell's actual size.
- **`Verso.Testing` makes the extension unit-testable**, and the load-context assumption — the one
  that would fail *silently*, since a quiet `false` from `CanFormat` is indistinguishable from "no
  formatter claimed it" — is reproduced in the suite with a real isolated collectible context
  rather than trusted.

**Exit:** 481 tests, and — on 2026-08-18, in a real Verso host — the end-to-end claim the suite
cannot reach: a bare `Figure` displays as a figure, sized to the cell, with no `Chart.render` and
no explicit SVG backend. The load-context assumption the whole package rests on held in the host
exactly as the isolated-`AssemblyLoadContext` test said it would.

The delay before that confirmation was entirely a stale `Marey.Html` in the NuGet global packages
folder, which is keyed on id *and version*: a re-pack as `1.0.0` never replaces an extracted
`1.0.0`, so the notebook kept loading an assembly from before the bridge existed and `CanFormat`
kept correctly answering "no" — with no error anywhere. Written up in
`docs/notebooks/README.md`.

### Phase 6 — Announce-readiness

§14 says announce at M1, and a milestone nobody can install is not announced.

- **LICENSE** — absent from the repo today, and it is the one item that blocks everything
  downstream of "announce". Needs a deliberate choice (MIT and Apache-2.0 are the two live
  options for a .NET library; Apache-2.0 carries a patent grant, MIT is shorter and is what most
  of the F# ecosystem uses).
- Per-package NuGet metadata, `PackageReadmeFile`, `dotnet pack` verified for all six packages.
- GitHub Actions: build, test, and the pdflatex golden gate with a TeX install.
- `docs/decisions.md` gains D28–D35 as *implemented and verified*, not as proposals.
- Root `README.md` gains the TikZ story, a compiled figure, and the notebook.

**Exit:** `dotnet pack` produces six well-formed packages; CI green on a clean checkout.

---

## 4. Verification gates

Written in the register the project already uses: each gate is a claim that can fail.

1. **Every golden `.tex` compiles.** `pdflatex -interaction=nonstopmode` over each baseline,
   asserting zero errors. This is the TikZ analogue of the SVG suite's `XDocument.Parse`
   round-trip, and it is the single most valuable test in the milestone — a TikZ backend that
   emits plausible-looking broken TeX is worse than no backend. Reports what engine it found so a
   local run says plainly whether the gate ran; CI installs TeX so enforcement is real there.

   **Built in Phase 1 as `TexToolchain`, and it has already earned its keep twice.** It found
   D36 on its first run. It also caught *itself* being vacuous: the first version passed
   `-file-line-error`, which rewrites errors from `! Undefined control sequence.` to
   `./doc.tex:5: ...`, so the `"! "` filter matched nothing and **every document was declared
   clean**. Two consequences are now permanent:

   - a deliberately-broken document is part of the suite, asserting the gate *can* fail. A gate
     that cannot fail is theatre, and this one was theatre for about ten minutes.
   - failure is detected from two independent signals — an error line in the log *and* a non-zero
     exit code — so one pattern going stale after a flag change or an engine update cannot
     silently disable it again.

   `-halt-on-error` is deliberately not passed: every error the engine finds should reach one
   report, so a failure is actionable in a single pass.
2. **Cross-backend geometry agreement.** For a fixed scene, every emitted TikZ `bp` coordinate
   equals the SVG backend's `px` coordinate times `72/96`, with y measured from the opposite
   edge, to three decimals. D29 and D30 make this relationship exact, so this is a genuine
   oracle rather than a tautology — and it is the only thing that catches a sign regression in
   either backend's independently-derived rotation arithmetic.
3. **The headline claim, in TeX.** Mirroring the existing test that finds `t [s]` and `x [m]`
   literally in rendered SVG: assert that a `float<s>`-against-`float<m/s^2>` figure under
   `Theme.tikz` puts a literal `\qty{}{\metre\per\second\squared}` in the `.tex`, and that the
   file compiles.
4. **Determinism and path equivalence.** Two renders byte-identical; `Chart.renderTo` on a TikZ
   backend byte-identical to `TikzBackend.render` — the statement D21 already proves for SVG,
   which here additionally proves that D31's internal degradation and `renderTo`'s external
   degradation agree.
5. **Degradation is real.** A gradient-filled figure through TikZ produces a solid Oklab-midpoint
   fill; a `Raster` produces no markup at all. D10's first real exercise.

   **Corrected after review.** This gate originally read "a `Raster` produces a visible skip
   comment", which the `raster.tex` baseline showed to be false: the emitter does contain a branch
   writing `% marey: raster 2x2 skipped ...`, but `Degrade.forCaps` replaces an unsupported
   `Raster` with `Scene.empty` before the backend sees it, so through `TikzBackend.render` an image
   vanishes without a trace. That is Core's contract working as designed — a backend advertising
   `Rasters = false` never gets handed one — and the emitter's branch is defensive rather than dead,
   since `IBackend.Draw` is public and can be driven un-degraded. Both facts are now pinned by a
   test, and routing around `Degrade` to preserve the comment was rejected: it would make Core's
   contract conditional on the backend, in exchange for a `%` comment invisible to anyone reading
   the compiled PDF.

   `gradients.tex` is worth reading for the other half: the linear and the radial ramp are the same
   stop list, so both degrade to the *same* solid and two visually different pictures become
   identical. D10's accepted cost, now on the record rather than in a doc comment.
6. **A figure-level golden, for both backends.** There was **no** golden baseline for a `Figure` in
   either backend — all 13 were hand-built `Node`s, and `Chart` output was checked only by
   substring assertions. Closed: `Golden.figure` is one two-series figure with a title, subtitle,
   legend and grids, rendered to `figure.svg`, `figure.tex`, and `figure-siunitx.tex`.

   The third of those is the milestone's headline as a committed artefact rather than a substring
   assertion: `\qty{}{\second}` and `\qty{}{\metre}` in a file, derived from the unit witnesses
   with nothing configured but `Chart.withTheme Theme.tikz`.

   Gate 6 is also wired to gate 7 — both figure baselines are compiled and rasterised, and both
   must contain ink and must not rasterise *identically*, since `\qty{}{...}` typesetting to an
   empty box is a perfectly plausible way for the headline feature to fail silently.

7. **Somebody has to look at the picture.** This is the gap Phase 2 exposed and it deserves to be
   a numbered gate rather than a habit.

   The D33 paint bug — markers and legend swatch entirely absent — was invisible to *every*
   automated check available. Structure tests confirmed one macro, one scope and N calls, all
   true. The compile gate confirmed valid TeX, also true. Determinism, culture-invariance and
   cross-backend coordinate agreement all passed, because the coordinates were right; only the
   ink was missing. **A text emitter can be verified into a state where it emits a valid, correct,
   entirely blank document.**

   Two mitigations, one cheap and one to schedule:

   - *Cheap, done:* an invariant test asserting that a batch which asks for paint produces a macro
     containing a paint action, over every shape × paint combination. It encodes the specific
     finding rather than the general class.
   - *Built in Phase 4 as `Raster.fs` + `TikzInkTests.fs`:* compile the document, rasterise the PDF
     with `pdftoppm -gray`, count pixels. Ten tests, ~6 s.

   **What the ink gate is, precisely.** Not a golden-image suite: no bitmap is committed, because a
   bitmap is not reviewable in a pull request (§13) and would churn on every Poppler or font-metric
   update. It answers only *"is there ink, and is it where it belongs"*, and it answers it with
   **differentials** wherever possible — "deleting the markers from this figure empties its panel"
   needs no magic constant and cannot be satisfied by accident. Where an absolute threshold is
   unavoidable it is zero.

   Three things fell out that were not anticipated:

   - **96 dpi is a free coordinate correspondence.** `TikzBackend` maps `px` to points at 96 px/inch
     and `standalone` crops to the declared bounding box, so the page is exactly `extent × 72/96`
     pt — and rasterising *that* at 96 dpi returns exactly the requested extent in pixels. A
     400×250 figure produces a 400×250 bitmap whose grid **is** the SVG backend's coordinate space,
     y downward. Regions can be written in layout's own numbers, and an SVG rasteriser would be
     directly comparable with no resampling.
   - **Orientation is reachable here and nowhere else.** Gate 2 states that a TikZ y equals the SVG
     y measured from the opposite edge — which is satisfied just as well by two backends that are
     *both* upside down. A raster is not: `x = ½gt²` increases, so on a correct page the curve
     leaves the panel's lower-left corner and never enters its upper-left one. Measured: 2.157 % and
     **0.000 %**.
   - **A failure has to print the picture.** "Expected ink, found none" cannot distinguish a blank
     page from a figure drawn off the edge, so `Raster.describe` renders the bitmap as ASCII into the
     assertion message — the same instinct as `Golden.describeDifference`. It earned that on its
     first run: two tests failed, and the map showed a legend box sitting inside the panel region
     the test attributed to series geometry. Naming a layer turns the legend on, and it is drawn
     *inside* the panel. The backend was correct; the fixture no longer matched its own claim.

   **Verified against the real bug**, which is the only evidence that matters for a gate: the paint
   actions were removed from `markerMacro` and three tests failed, including the figure-level one,
   whose message printed a chart with axes and labels and an empty panel. Restored, all 423 pass.

   Not delivered: the raster-level *"the TikZ figure and the SVG figure look the same"* oracle. No
   SVG rasteriser is installed (`rsvg-convert`, ImageMagick and Inkscape are all absent; `pdftoppm`
   and `pdftocairo` come with MiKTeX). Gate 2 covers the geometry exactly and numerically, which is
   the sharper instrument for everything except orientation — and orientation is now covered
   directly. Worth revisiting only if a rasteriser becomes a reasonable CI dependency.

---

## 5. Known imprecisions, accepted

Recorded so they read as decisions rather than oversights.

- **Layout is measured against `Text`, never `Tex`** (D32). Mitigated by anchor placement and by
  D23's coarsen-only ladder; not eliminated. The honest statement is that TikZ label layout is
  approximate in a way SVG's is not, and the approximation is bounded by the width difference
  between a Unicode unit string and its siunitx rendering.
- **`Theme.tikz` pins font size** (D35), so a figure does not fully inherit document typography.
  `FontMode = InheritFont` opts out and accepts collisions.
- **Layout assumes the theme's family metrics, and TeX uses the document's face** (D35). Measured
  on a real figure: a serif-metric theme in a Helvetica document reserves ~8% too little width and
  the y-axis title tightens against its tick labels. `Theme.withFontFamily` is the fix and the
  user has to apply it; nothing can detect the host document's font from inside the picture.
- **No gradients, no rasters** in TikZ output at M1 (D31). Both degrade visibly.
- **`Prefix.select` has no additive-offset annotation.** A domain of 1000.000–1000.005 m yields
  five identical tick labels. Already documented in `Prefix.fs` as a real gap; unchanged by M1,
  and worth naming because it is the one place D19's injectivity argument does not reach.
- **Out-of-range coordinates are clamped, not clipped** (D36). Correct for anything the panel clip
  removes anyway, which is the case that occurs; wrong in principle for a segment that both leaves
  TeX's representable range *and* crosses the visible panel. Proper segment clipping is a compiler
  -layer job and is not in M1.

---

## 6. Explicitly out of scope for M1

- **`\unit{…}` instead of `\qty{}{…}`.** Verified equivalent in output (§1). A semantic
  improvement, not a fix; do it whenever `UnitDisplay` is next touched.
- Multi-stop PGF shadings; raster sidecars; pgfplots interop.
- Partial `Dim` factorisation (`J·K⁻¹` for entropy) — deliberately deferred by D18 as ambiguous
  and non-confluent.
- Everything in M2+: the grammar CE, Skia, `Anim`, fields, the unit type provider.

---

## 7. Suggested commit sequence

One commit per phase boundary at minimum, because each is independently reviewable:

```
1  core: promote deterministic number formatting to Marey.Core        (D34)
2  core: add TextRun.Tex; fix Scene.prune's empty-run test             (D32)
3  units: honour ToSI/OffsetSI in Axis.ofErasedWith; re-pin regressions
4  units: AxisSpec.TitleTex/UnitTextTex
5  tests: make the golden harness backend-agnostic
6  docs: correct README, tests/README, design.md §17
7  tikz: writer primitives                                            (D28, D33, D34)
8  tikz: backend traversal, all nine nodes                            (D29, D30, D31)
9  plot: Theme.tikz; thread Tex through Axes and Legend               (D35)
10 tests: tikz goldens + pdflatex gate + cross-backend agreement
11 interactive: formatters + notebook
12 repo: LICENSE, packaging, CI
13 docs: decisions.md D28-D35; README M1 story
```
