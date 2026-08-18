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
| 6 — Announce-readiness | LICENSE done (MIT). Packaging, CI and `decisions.md` not started. |

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
Rasters         = false     Semantics   = true      (as `% marey-role:` comments)
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

`Semantics = true` is cheap here and worth having: a `% marey-role: axis` comment line costs
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

Deliberately **not** pgfplots' `\pgfuseplotmark`: Marey *is* the plotting layer, and depending
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

### D34. Deterministic number formatting is promoted to `Marey.Core` **[forced]**

`SvgWriter.fs` establishes the rules that make byte-identical output possible: invariant
culture, a *custom* numeric pattern so scientific notation can never appear, negative zero
normalised, non-finite values mapped to `0`. Every one of those applies to TeX, and one of them
is **more** critical there: SVG path data merely fails to parse `1e-05`, whereas TeX cannot
interpret it as a dimension at all and raises a hard error.

Reimplementing them in the TikZ package is exactly the F2 hazard — "duplication in the one place
duplication is dangerous, because a change to one copy would silently disagree with the other" —
and F2 says to promote on the appearance of a second consumer. This is the second consumer.

**Built:** a small `Num` module in `Marey.Core` (zero dependencies, so this is free), with
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

### D38. Three characters TeX accepts and then typesets as something else

**Not planned. Found in Phase 4 by reading the `text.tex` baseline.**

D36's sibling, and the harder of the two to catch. `<`, `>` and `|` are perfectly legal in TeX text
mode. They produce no error, no warning, and no diagnostic of any kind — they are simply not the
glyphs that were requested, because in the **OT1** encoding LaTeX still uses by default those slots
hold `¡`, `¿` and `—`. An axis label reading `a < b` typesets as `a ¡ b` in a document that
compiled perfectly cleanly.

**The compile gate cannot see this, by construction.** There is nothing wrong with the TeX. Nor can
the ink gate: the ink is there, in the wrong shape. It is visible only to a person looking at a
rendered page, or to a golden baseline being read before it is accepted — which is how it was found.

**Built:** `<` → `\textless{}`, `>` → `\textgreater{}`, `|` → `\textbar{}` in
`Tex.tryEscapeChar`, so `Tex.isLiteralSafe` reports them as unsafe too. In Core rather than in the
backend, because the function's job is "what must this character become to typeset correctly", and
`¡` is not correct. Confirmed by compiling both forms and comparing the rasters.

**Deliberately not escaped:** `"`, `` ` ``, and the `--`/`---` ligatures. They render as `”`, `‘`
and dashes — conventional TeX typography and the better glyph in each case. `\textquotedbl` was
tried and raises `Command \textquotedbl unavailable in encoding OT1`, which is the useful reminder
here: a plausible-looking `\text...` macro is not necessarily a real one in the encoding the
document happens to use, so the replacement macros are put through a real engine rather than
asserted from memory.

An `\usepackage[T1]{fontenc}` in the host document also fixes the three, and a figure cannot
require anything of the document it is dropped into. Escaping is the only fix available to us.

### D39. The notebook renderer knows nothing about the notebook **[superseded in part]**

**Written when the host was .NET Interactive. Rewritten after that host was retired, because the
decision survived and the reasoning for it turned out to be worth more than it looked.**

The original entry justified `PackageReference … PrivateAssets="all"` — compile against
`Microsoft.DotNet.Interactive.Formatting`, do not ship it — on three grounds: `dotnet pack` would
otherwise fail on NU5104 with `TreatWarningsAsErrors`; a net10.0 TFM could not be loaded by an
older kernel; and two copies of a static registry is the classic notebook failure. All three were
correct. None of them matters any more: Polyglot Notebooks was retired 2026-03-27 and .NET
Interactive 2026-04-24, and `Formatters.fs`, the packaged `extension.dib` and that package
reference were deleted rather than carried.

**What survived, and is the actual decision:** the split between `Html.fs` and the registration.
`Html.fs` is `something -> string`, total, with no reference to any notebook host. That was argued
for on *testability* grounds — the interesting half should be assertable without installing a
kernel. It paid off as *portability*: when the host died, what had to be rewritten was nine lines
in a separate file, and the four hundred lines that draw things were re-pointed at Verso by adding
a bridge at the bottom and deleting nothing.

The generalisation worth keeping: **a rendering layer that names its host in a type signature has
put the host's lifetime into its own.** Neither the SVG backend nor the TikZ backend references a
browser or a TeX distribution; the notebook renderer should not have been the exception, and the
version of it that was not is the version that is still here.

The package is still called `Marey.Html`, which now names a retired Microsoft product.
Left alone deliberately, pending the naming pass that also has to settle the `Marey` package id
being taken on nuget.org by an unrelated, actively-published C# database.

### D40. The Verso extension does not reference Marey

**Design says** nothing — Verso postdates every document in this repository.

**Built:** `src/Marey.Verso/`, an `IDataFormatter` at priority 30, targeting `net8.0;net10.0`,
depending on `Verso.Abstractions` **and nothing else**. It reaches `Marey.Html` by reflection.

**Why not simply reference `Marey.Plot` and match on `typeof<Figure>`** — the obvious design, and
it cannot work. Verso loads an extension into its own collectible `AssemblyLoadContext`, sharing
only `Verso.Abstractions` from the default context; Marey arrives by a different route entirely,
the notebook's `#r`, into the kernel's context. A referenced `Figure` and the user's `Figure` would
be two unequal `System.Type`s: the `SupportedTypes` pre-filter would never match, and a cast would
throw if it did. Reflecting on the value's own type needs no shared identity, so the dependency
becomes a lookup — and the package then keeps working across Marey versions, since the only thing
it can break against is two function signatures.

Those two signatures are the cost, and they are a contract the compiler cannot see. They are
written down in `Bridge.fs`, pinned by a test that resolves them with the exact reflection the
extension uses, and probed defensively: every failure path answers "cannot render", because the
correct behaviour when the bridge is unavailable is for the F# kernel's own output to proceed
untouched. A notebook that referenced `Marey.Plot` alone is not an error state.

**Why an extension at all, rather than a `ToHtml()` member.** Verso's built-in `HtmlFormatter` is
duck-typed on a public parameterless `ToHtml() : string` and needs no extension, no install and no
consent prompt. It was the first design considered and it fails twice over:

- **It would tie.** `Verso.FSharp`'s `FSharpDataFormatter` claims *every* F# record, union and tuple
  at priority 20, and the built-in `HtmlFormatter` is also 20. `FormatterResolver` orders by
  `OrderByDescending(Priority)`, a stable sort, so an equal-priority pair resolves by registration
  order — which is not a contract. A `Figure` would render as a picture or as a field table
  depending on enumeration order. Priority 30 is the whole reason this package exists.
- **`Figure` could not carry the member.** `ToHtml()` on `Figure` puts HTML rendering inside
  `Marey.Plot`, which would need the SVG backend, which D21 forbids. `Color[]` settles it beyond
  argument: an array cannot be given a member.

**One thing the new host does better, and it is not a small one.** `IFormatterContext` carries
`MaxWidth` and `MaxHeight`. The .NET Interactive design had no way to ask how wide a cell was, so
`Html.defaultSize` picked 720×450 and hoped; `extentForHost` now clamps the host's own measurement
into a band and keeps the aspect. A guess became a measurement, which is the trade the whole
milestone has been making everywhere else.

**Worth sending upstream, and deliberately not sent as a Marey formatter** — Verso should not
depend on us. Two things underneath generalise and carry no Marey in them: that `ToHtml()` takes
no arguments, so nothing rendered through the built-in formatter can size itself to the cell even
though the context already knows the size; and that the 20/20 tie above is a latent ambiguity in
the shipped set. Both are reportable on their own terms, which is the test of whether a
contribution belongs upstream.

---

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

**Exit:** 481 tests. Unverified: that a live Verso host discovers, approves and loads the package
end to end. There is no Verso installation on this machine.

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
