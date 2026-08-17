# Quiver M1 — *Units end to end*: implementation plan

Roadmap reference: `docs/design.md` §14, milestone **M1**. Decision-log reference:
`docs/decisions.md` D1–D27, F1–F2.

> §14 on M1: *"`Quiver.Units`, witness canonicalisation, prefix selection, auto axis titles,
> `.NET Interactive` formatter, TikZ backend. **This is the release that has no competitor.**
> Unit-safe plots that drop into a LaTeX document. Announce here."*

Status: **Phase 0 and Phase 1 complete** (see §3). Decisions numbered D28+ are *proposed* entries
for `docs/decisions.md`; they move into that file as they are implemented and verified, not
before. D36 was not planned — it was found by the compile gate and is recorded below.

| Phase | State |
|---|---|
| 0 — Foundations and truth-in-docs | **done**, 288 → 310 tests |
| 1 — `TikzWriter.fs` primitives | **done**, 310 → 348 tests |
| 2 — `TikzBackend.fs` traversal | **done**, 348 → 402 tests |
| 3 — `Theme.tikz` and siunitx wiring | **done**, 402 → 413 tests |
| 4 — Verification (goldens, ink gate) | next |
| 5–6 | not started |

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
| Packages | `Quiver.Core`, `Quiver.Units`, `Quiver.Backend.Svg`, `Quiver.Plot`, `Quiver.Tests` |
| Goldens | 13 tracked SVG baselines under `tests/Quiver.Tests/golden/` |

**The important discovery: M0 already built most of what §14 assigns to M1.** `Quiver.Units`
ships all four `UnitStyle` renderers (`Unicode`, `Ascii`, `Latex`, `Siunitx`) with no stubbed
branch, `Dim`-keyed canonicalisation over 17 SI entries, the full 24-prefix ladder with the
kilogram shift and affine/non-coherent refusals, and `Axis.ofScaleWith` → self-labelling axis
titles. `Chart.withUnitStyle Siunitx` is already wired to both axes.

So M1's remaining scope is **narrower and sharper than §14 implies**, and this plan is written
against the actual delta rather than against the roadmap line.

### The delta

| # | Item | Size |
|---|---|---|
| 1 | `Quiver.Backend.Tikz` — the headline deliverable | large |
| 2 | `Quiver.Interactive` — .NET Interactive formatters | small |
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

**siunitx: initially broken, now working, and the existing Quiver output is valid.** The first
probe failed on `\l__siunitx_print_unit_mode_str` — `siunitx.sty` v3.5.5 (2026-05-15) against a
LaTeX format dated 2025-06-01. After a MiKTeX package update all of the following compile with
zero errors and render identically:

```
A  x [\qty{}{\metre\per\second\squared}]   ->  x [m s-2]     <- what Quiver emits today
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

Proposed `docs/decisions.md` entries. Each follows the existing house form: what the design
says, what will be built, why. **[forced]** marks cases where following the sketch literally
breaks something else the same document requires.

### D28. TikZ coordinates are emitted in `bp`, never `pt` **[forced]**

`Space.fs` defines Core's `pt` as **1/72 inch** — the PostScript/big point. TeX's `pt` is
**1/72.27 inch**. Emitting a Core `pt` value into a `.tex` file as `pt` is a silent 0.37 %
error in every coordinate, font size and stroke width, and it is invisible in a golden diff
because the numbers are all self-consistently wrong.

`bp` is exactly 1/72 inch and TeX accepts it as a dimension everywhere `pt` is accepted. All
geometry is emitted in `bp`. Font sizes stay in `pt` inside `\fontsize{}{}` — which is TeX's
`pt`, so a 9.6 pt font is `\fontsize{9.6}{...}` and is *not* converted, because `FontSpec.Size`
is a typographic request being handed to a typesetter rather than a length being drawn.

This is a §12-class landmine: it compiles, it looks right, and it is wrong by 0.37 %.

### D29. TikZ does not flip y; SVG does **[forced]**

`Affine.fromRects` leaves y unflipped and D15 makes the flip the SVG backend's business,
applied once in `Begin`. **TeX's canvas is y-up, exactly like `nd`** — so the TikZ backend's
root transform is `Affine.fromRects Rect.unit { X0=0; Y0=0; X1=w; Y1=h }` with **no flip at
all**.

The consequences run further than the sign of one matrix entry:

- `det(CTM) > 0` always, so D13's mirror-negation of text rotation **does not apply**. TikZ's
  `rotate=` is counter-clockwise-positive in a y-up space, which is precisely `TextRun.Rotation`
  in `rad` converted to degrees, plus the frame's own `atan2(M21, M11)`. Fewer sign traps than
  SVG, arrived at by an opposite formula.
- Therefore a test asserting that the two backends *agree* about a rotated label is load-bearing
  rather than redundant: they reach the same visual result through different arithmetic, and
  only a cross-backend assertion catches a regression in either.

### D30. Geometry is pre-transformed; no picture-level `scale` is emitted

Same decision as D15, for a stronger reason. TikZ's `scale=` does **not** scale `line width`,
`dash pattern` or node font sizes by default, while it does scale coordinates. Combining a
picture scale with D12's px-denominated stroke widths therefore produces widths that are correct
only by accident, and marker sizes that are correct only at scale 1.

Coordinates are baked into device `bp`. A bp is a bp everywhere in the output, the emitted
numbers are checkable against a ruler, and — per D29 — they are the SVG backend's numbers times
`72/96`, with y measured from the bottom instead of the top. That relationship is exactly what
makes the cross-backend agreement test in §4 possible.

### D31. TikZ capabilities at M1, and `Degrade`'s first real customer

```
Gradients       = false     RotatedText = true
RadialGradients = false     TextShaping = false     (delegated to TeX, per Capabilities.vector)
Rasters         = false     Semantics   = true      (as `% quiver-role:` comments)
Clipping        = true      Dashes      = true      MaxBatch = None
```

`Gradients = false` is honest rather than lazy: PGF shadings are two-stop and axis-constrained,
and a multi-stop `\pgfdeclarehorizontalshading` only covers the axis-aligned linear case.
Advertising `true` and quietly emitting a two-stop approximation of a five-stop ramp is the
"silently producing wrong output" §5 exists to prevent. `Degrade.forCaps` already collapses a
gradient to its Oklab midpoint (D10) — **this is the first backend that actually needs it**,
which retires D10's status as a facility nobody reads.

`Rasters = false` for the same reason: an honest raster needs a sidecar `.png` and
`\includegraphics`, which breaks the one-string-output contract and adds a `graphicx`
requirement. Heatmaps are M2/M4 work; `renderWithAssets` is the M2 door.

`Semantics = true` is cheap here and worth having: a `% quiver-role: axis` comment line costs
nothing, keeps golden diffs readable, and — decisively — `Degrade.forCaps` **strips every
`Annotated` node** when `Semantics = false`, which would silently discard structure the golden
diff is there to review.

**The trap this creates, and the fix.** `Chart.render`, `Chart.toSvg` and `Chart.toScene` do
**not** degrade — only `Chart.renderTo` does. A caller writing
`fig |> Chart.toSvg TikzBackend.render "f.tex" size` would therefore hand an un-degraded scene
to a backend advertising `Gradients = false`, and the gradient would reach a renderer with no
code path for it. So **`TikzBackend.render` runs `Degrade.forCaps` and `Degrade.splitBatches`
against its own caps internally**, making the function-shaped path safe by construction. It is a
no-op on the SVG backend (whose caps are effectively full), so the two backends stay symmetric
in behaviour even though only one of them needs it.

### D32. `TextRun.Tex : string option` — how TeX source crosses the IR

**The problem.** `Chart.withUnitStyle Siunitx` already puts `\qty{}{\metre}` into
`TextRun.Text`. A TikZ backend that escapes text — which it must, because user titles contain
`%`, `_`, `&`, `#`, `$` and backslashes — turns that into `\textbackslash{}qty\{\}\{...\}`.
A backend that does not escape corrupts the document on the first `50% yield` title. Neither
behaviour is acceptable, and no per-render switch fixes it: one figure legitimately contains
both a siunitx axis title and a literal user string.

**Built:** one new field on `TextRun`.

```fsharp
type TextRun =
    { Text: string           // plain-Unicode fallback. ALWAYS set. What ITextMeasurer measures.
      Tex: string option     // TeX source for this run, when the producer has one.
      At: Vec2<nd>
      ... }
```

`Text` remains the single source of truth for measurement and for every backend that is not a
typesetter. `Tex` is consumed by backends that can typeset it and ignored by the rest, so no
capability flag is needed: the fallback is always present, which is what makes this safe to add
without a tenth `Capabilities` field.

**Why a field rather than the alternatives.** Carrying it in `Semantics.Data` needs no IR change
but puts the payload behind a wrapper that `Degrade.forCaps` deletes when `Semantics = false`,
and forces the backend to reach through a container into its child. A per-render
`TikzOptions.TexText: bool` needs no IR change but cannot mix literal and TeX text in one
figure, which is the actual requirement.

**Why it pays for itself beyond M1.** §10's three-option strategy for mathematical typesetting
all consumes this one field: shelling out to LaTeX (option 1) reads `Tex`; MathJax/KaTeX on the
web backend at M3 (option 2) reads `Tex`; native OpenType MATH layout at M6 (option 3) reads
`Tex`. Adding it now costs one field; adding it at M3 costs a migration of every producer.

**Costs, stated rather than discovered later:**

- D5's acknowledged weakening applies: a *field* addition does not break backends at compile
  time the way a *case* addition does. Every backend must be reviewed by hand, not by the
  compiler. Node count stays nine.
- **Every `TextRun` construction site breaks at compile time** — `Axes.fs`, `Legend.fs`,
  `Figure.fs`, `Golden.fs`'s `text` helper, and the tests. That break is desirable: it forces a
  conscious choice of `Tex` at each site.
- `Scene.prune` currently drops `Text t when String.IsNullOrEmpty t.Text`. It must drop only
  when **both** `Text` and `Tex` are empty, or a TeX-only run silently vanishes from a pruned
  scene. This is a real subtlety and gets its own test.
- **Layout is measured against `Text`, not `Tex`.** `ITextMeasurer` measures the Unicode
  fallback while TeX typesets the `Tex` form, and the two have different widths. Mitigation:
  axis titles and tick labels are anchor-placed, so a small width error shifts nothing into a
  collision; and D23's collision ladder only ever *coarsens*, so an under-measured label cannot
  produce an overlapping plan. Recorded as a known imprecision in §5, not papered over.

**Who sets it.** `AxisSpec` gains `TitleTex: string option` and `UnitTextTex: string option`,
populated by `Axis.ofScaleWith` calling the units renderer **twice** — once with `Unicode` for
`Text`, once with the configured style for `Tex`. When `AxisConfig.Style` is `Unicode` or
`Ascii`, `Tex` is `None` and the TikZ backend escapes the fallback, which is correct. Tick
labels are ASCII digits and need no TeX form — **except** when D19's prefix-unattachable
fallback fires and a label becomes `10⁻¹⁸·m²`, which does need one.

### D33. Markers: one `\providecommand` per distinct (shape, size), then one call per point

The direct mirror of SVG's `<defs>` + `<use>` + `<g>` structure (D15, §5.1). A `\begin{scope}`
carries the batch's fill and draw, each point costs one short macro call that inherits them, and
the definition is deduplicated deterministically by (shape, size) in traversal order.

The alternative — one `\path ... circle[radius=..]` per point, each repeating its own style — is
what §5.1 calls "the single biggest performance decision in the IR", decided wrongly. A text
backend cannot escape one line per point, but it can and must escape one *style declaration*
per point.

Deliberately **not** pgfplots' `\pgfuseplotmark`: Quiver *is* the plotting layer, and depending
on pgfplots would import a second, disagreeing opinion about axes and scaling.

**Amended in Phase 2 — the SVG analogy stops short of the paint.** In TikZ, `fill=<colour>` on a
*scope* sets the fill colour but does **not** make paths inside it filled. A bare `\path` inside
`\begin{scope}[fill=orange]` compiles perfectly and draws nothing whatsoever.

The first implementation mirrored SVG's `<g>` + `<use>` split exactly — paint on the group,
geometry in the definition — and produced a figure with **no scatter markers and no legend
swatch**. So the split is: the macro carries the paint *actions* (a bare `fill` and/or `draw`,
values omitted), and the enclosing scope carries the *values* (colour, opacity, line width). Both
halves are load-bearing — an action with no colour paints in black, a colour with no action paints
nothing — and which actions are needed becomes part of the macro's identity alongside shape and
size, so a fill-only and a stroke-only batch of the same shape are different macros.

**Every test passed while this was broken, and so did the compile gate.** The document was
perfectly valid TeX; it just had no ink in it. That is the subject of the note added to §4.

### D34. Deterministic number formatting is promoted to `Quiver.Core` **[forced]**

`SvgWriter.fs` establishes the rules that make byte-identical output possible: invariant
culture, a *custom* numeric pattern so scientific notation can never appear, negative zero
normalised, non-finite values mapped to `0`. Every one of those applies to TeX, and one of them
is **more** critical there: SVG path data merely fails to parse `1e-05`, whereas TeX cannot
interpret it as a dimension at all and raises a hard error.

Reimplementing them in the TikZ package is exactly the F2 hazard — "duplication in the one place
duplication is dangerous, because a change to one copy would silently disagree with the other" —
and F2 says to promote on the appearance of a second consumer. This is the second consumer.

**Built:** a small `Num` module in `Quiver.Core` (zero dependencies, so this is free), with
`Svg.num`/`Svg.numAt` retained as thin forwarders so the 13 existing goldens and the formatting
tests that assert on them are untouched.

### D35. `Theme.tikz` pins the font *size* and inherits the font *family*

§9 asks for a `tikz` theme that "inherits document fonts", and §6 sells the TikZ backend on
"figures that inherit the document's fonts and `siunitx` units". Taken literally — emit no font
information at all — the figure is typeset in a face whose metrics have nothing to do with the
`ApproxTextMeasurer` widths that D23's collision ladder and the whole measure–arrange pass were
computed against. Labels would collide in the PDF while the layout believed they fit.

**Built:** `Theme.tikz` sets `FontSpec.Family = ""`, which the backend reads as "emit no family,
inherit the document's" — and the backend still emits `font=\fontsize{s}{1.2s}\selectfont`, so
the *size* layout assumed is the size TeX uses. The family inherits, which is the feature; the
size is pinned, which is the correctness. `TikzOptions.FontMode = PinSize | Inherit` exposes the
fully-inheriting behaviour for anyone who wants it and accepts the consequence.

**Built, with the family half corrected.** The Phase 2 implementation emitted `\rmfamily` /
`\sffamily` when a theme named a family, which is the opposite of the feature: a figure dropped
into a sans-serif paper would have come out in the theme's serif rather than the document's sans,
and §6's one promise would have been the thing it got wrong. **The TikZ backend now emits no
family at all.** Series and shape are still emitted — `\bfseries` and `\itshape` are orthogonal to
family, so a bold title stays bold in any document.

`FontSpec.Family` is therefore not dead for this backend, it is *only* a layout input: it is what
`ApproxTextMeasurer` reads to choose its width factor, so it decides how much room a label gets
reserved. `Theme.tikz` names a serif stack because a 0.92 factor approximates Computer Modern
better than a Helvetica assumption does.

**And that knob has to be reachable, which the compiled figures proved.** The same picture in a
`\usepackage{helvet}` document sets ~8% wider than the serif metrics assumed, and the y-axis title
visibly tightens against its tick labels — not a collision, but not right either. Hence
`Theme.withFontFamily`, a sibling of `scaleType`, and the two family stacks are now public:
`Theme.tikz |> Theme.withFontFamily Theme.Sans` for a sans document. Verified by rendering both
and comparing.

### D37. The unit style belongs to the theme, and the axis config overrides it

D35 said "`Theme.tikz` also sets `UnitStyle` to `Siunitx` at the axis-config level", which is not a
thing a theme can do: `Theme` had no unit style, and `AxisConfig.Style` was a plain `UnitStyle`
defaulting to `Unicode`.

**Built:** `Theme.UnitStyle`, and `AxisConfig.Style` becomes `UnitStyle option`.

**Why the option is forced.** With a plain `UnitStyle` on the axis config there is no way to
distinguish "the user explicitly asked for `Unicode`" from "nobody said", so a theme could never
supply a different default without overriding an explicit choice. `None` means "take the theme's",
and `Chart.withUnitStyle` sets `Some`.

**Why the theme is the right home.** Unit spelling is a presentation choice, exactly like a font
or a rule width — the same figure wants `m·s⁻²` on screen and
`\qty{}{\metre\per\second\squared}` in a paper, and nothing about the data or the scale changes
between them. It is also what reduces the headline to one selection, which is the measure of
whether M1 landed: `Chart.withTheme Theme.tikz` and nothing else.

Resolution happens in `Figure.compilePanel`, at compile time rather than at construction, so
re-theming a finished figure re-spells its axis titles. Anything else would make `Chart.withTheme`
a half-measure.

### D36. TeX cannot represent an arbitrary coordinate, and D9 guarantees we will hand it one

**Not planned. Found by the compile gate in Phase 1, on the first run.**

A TeX dimension is a 32-bit fixed-point count of scaled points at 65536 sp per TeX point, so the
largest representable length is 2^30 − 1 sp = 16383.99998 pt = **16322.06 `bp`**. Past it the
engine raises `Dimension too large`; past ~2^31 the number scanner raises `Number too big` before
it even converts. Both are hard errors — the document does not compile.

**This is reachable in ordinary use, and D9 is the reason.** `Scale.Forward` deliberately does not
clamp: "clamping would pile out-of-range data onto the panel edge, which is a lie about the data."
So a single out-of-domain datum projects far outside `[0,1]` and is removed by the panel's `Clip`
node. The SVG backend writes the coordinate without complaint and the clip hides it. **TeX refuses
the file.** A TikZ backend that does not clamp therefore cannot render any figure containing one
out-of-domain point — which is most real data.

**Built:** `Tikz.dim` clamps to ±16000 bp, ~28 A4 widths and comfortably below the limit, so it
cannot affect a real figure. The loss is acknowledged: a segment running to a far-off point keeps
its near end and has its far end pulled to the boundary, which changes the segment's direction
when only one component is out of range. Everything affected is outside the panel and clipped in
the normal case, and the alternative is not a more faithful document but no document at all.
Geometrically correct clipping belongs to the compiler layer, not to a number formatter.

**Non-finite values are tested before clamping**, and map to `0` rather than to the boundary,
because `infinity > MaxDim` is true and clamping first would send an infinity to `16000bp` while
SVG writes `0`. The two backends agreeing on degenerate input is what makes gate 2 in §4 —
comparing their coordinates — possible at all.

This is a §12-class finding: it compiles in every other backend, it is invisible to inspection,
and it fails at the user's `pdflatex` rather than in our test run.

---

## 3. Phase plan

Six phases. Each has an exit criterion that is a command, not a feeling.

### Phase 0 — Foundations and truth-in-docs

Small, unblocks everything, and clears three stale documents before they mislead further work.

1. `Quiver.Core`: add the `Num` module (D34); repoint `Svg.num`/`Svg.numAt` at it.
2. `Quiver.Core`: add `Tex: string option` to `TextRun` (D32); fix `Scene.prune`'s empty test;
   fix every construction site in `Quiver.Plot` and the tests.
3. `Quiver.Units`: `AxisSpec.TitleTex`/`UnitTextTex`; populate in `Axis.ofScaleWith`.
4. `Quiver.Units`: fix `Axis.ofErasedWith` to consult `ToSI`/`OffsetSI` — route through
   `UnitDisplay.renderSymbol` when `ToSI <> 1.0 || OffsetSI <> 0.0`, mirroring what
   `UnitDisplay.unitText` already does. **Re-pin** `RegressionTests.fs`'s eV and °C cases to the
   correct labels (`E [keV]`, `T [°C]`) and delete the stale "gap in `Quiver.Core`" comment at
   `Axis.fs:36-45`.
5. `Quiver.Plot`: add `Chart.toFile` as the backend-neutral name for what `Chart.toSvg` does;
   keep `toSvg` as an alias so nothing breaks.
6. Golden harness: `Golden.check` is already backend-agnostic, but the failure artefact is
   hard-coded to `.actual.svg`, `Golden.render` calls `SvgBackend` directly, and the fsproj copy
   glob is `*.svg`. Parameterise the extension; add `*.tex` to the glob and `*.actual.tex` to
   `.gitignore`.
7. Docs: root `README.md` says "M0 in progress" (line 12) and documents a `spike/` directory
   **that does not exist** in the working tree, in two places. `tests/Quiver.Tests/README.md`
   says `Quiver.Plot` is not referenced and `PlotTests.fs` is "empty on purpose"; both are false
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
  `Keyed` → a `% quiver-key:` comment; `Annotated` → `% quiver-role:` comments (D31);
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

**Exit:** all gates green; 13 `.tex` baselines committed and reviewed as text.

### Phase 5 — `Quiver.Interactive`

§6: *"a ~200-line package registering a .NET Interactive formatter so `figure` displays inline
as SVG. Do it at M1; it is how people will first meet the library."*

- New project targeting `net10.0` — **not** `netstandard2.1`. It is a notebook host integration,
  never a Fable target, and `Microsoft.DotNet.Interactive.Formatting` requires a modern TFM.
  It sits at the top of the DAG and may reference Core, Units, Svg and Plot, which does not
  violate D21 (that rule constrains `Quiver.Plot`, not a leaf integration package).
- `Formatters.register()` registering `text/html` formatters for `Figure` (compiled at a default
  size and inlined as SVG), `Node`, and — cheaply and delightfully — `Color` as a swatch,
  `Ramp`/`Palette` as a swatch strip, `Dim`, `U<'u>`, and `IScaleErased`.
- Auto-registration via an `interactive-extensions/dotnet/extension.dib` in the package, so
  `#r "nuget: Quiver.Interactive"` is sufficient; `register()` stays public for the explicit
  path.
- **Testable without the `dotnet-interactive` tool** (which is not installed here): the
  formatter bodies are plain functions returning strings, so they are asserted directly. A
  demonstration notebook under `docs/notebooks/` covers the integration by hand.

**Exit:** formatter unit tests green; a notebook that renders a figure inline.

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
   fill; a `Raster` produces a visible skip comment and no broken markup. D10's first real
   exercise.
6. **A figure-level golden, for both backends.** There is currently **no** golden baseline for a
   `Figure` — all 13 are hand-built `Node`s, and `Chart` output is checked only by substring
   assertions. That gap is worth closing while adding a second backend, because a figure is what
   users actually produce and it is the only artefact that exercises layout, axes, legend and
   theme together.

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
   - *To schedule in Phase 4:* rasterise the compiled PDF (`pdftoppm`, already present alongside
     the TeX install) and assert non-background ink coverage within the panel. That is the only
     check in this list that would have caught the bug on its own, and it generalises — it is also
     the natural oracle for "the TikZ figure and the SVG figure look the same", which nothing
     currently tests.

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
1  core: promote deterministic number formatting to Quiver.Core        (D34)
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
