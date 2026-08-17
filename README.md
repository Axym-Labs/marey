# Quiver

A dimensionally-typed visualization system for F# — plotting, animation, and
math/physics visualization where the compiler checks your units.

> Working name. A *quiver plot* is the canonical vector-field visualization; a *quiver*
> in category theory is a directed multigraph, the free generator of a category.
> The library is about both.

## Status

**M0 complete. M1 in progress** — see [`docs/m1-plan.md`](docs/m1-plan.md). The core is real,
builds warning-free, and is verified at runtime.

| Package | State |
|---|---|
| `Quiver.Core` | Complete — `Dim`, `U<'u>`, `Vec2/3`, `Affine<'a,'b>`, `Scale`, `Coord`, the nine-case `Scene` IR, `Interp`, text metrics. **Zero dependencies**, `netstandard2.1`. |
| `Quiver.Units` | Complete — SI + 22 derived + 18 non-SI witnesses, `Dim`-keyed canonicalisation, prefix selection, Unicode/ASCII/LaTeX/siunitx rendering, self-labelling axes. |
| `Quiver.Backend.Svg` | Complete — all nine IR nodes, batched markers, gradients, clipping, a11y semantics, deterministic byte-identical output. |
| `Quiver.Plot` | Complete — Okabe–Ito / viridis / cividis / magma / Oklab-diverging palettes with a CVD + contrast validator, four resolved-value themes, a measure–arrange constraint grid, the axis and legend compilers, line/point/area/bar/errorbar geoms, and `Chart`. Zero dependencies, no backend reference. |
| `Quiver.Backend.Tikz` | Complete — all nine IR nodes into `tikzpicture` source, `bp` coordinates, batched marker macros, capability degradation, siunitx axis titles, and figures that inherit the host document's font. |
| `Quiver.Tests` | **453 tests**, all green: FsCheck property suites for the `Dim` group laws, all seven scale kinds, tick properties, `Interp`, `Affine`, colour and the Scene IR; regression tests pinning every §12 landmine and every numbered decision; 29 golden baselines in SVG *and* `tikzpicture` source; a real `pdflatex` compile gate; and an ink gate that rasterises the compiled figure and checks there is something in it. |

M0 is complete: `Quiver.Core` + SVG backend + linear/log scales + line/scatter/axes/legend
+ theme record.

```fsharp
open Quiver

Chart.line U.second U.metre ts positions
|> Chart.withName "model"
|> Chart.addScatter U.second U.metre sampleT sampleX
|> Chart.withName "measured"
|> Chart.withTitle "Free fall"
|> Chart.toSvg SvgBackend.render "fall.svg" (Chart.size 800.0 500.0)
```

That prints an x axis reading `t [s]` and a y axis reading `x [m]` with nothing
configured — the scale, the domain, the prefix, the tick values, the tick labels and both
titles are all derived from the two unit witnesses. The same call on data spanning
1e-9…5e-9 m prints `x [nm]` with ticks `1, 2, 3, 4, 5`.

Documents:

- **`docs/design.md`** — the full design document: architecture, layer-by-layer type
  design, backend strategy, animation and simulation models, roadmap, and a list of the
  F# type-system constraints that shape all of it.
- **`docs/decisions.md`** — the implementation decision log. Where the design document
  contradicts itself or under-specifies, what was decided, and why. Read this before
  changing anything structural.
- **`docs/m1-plan.md`** — the M1 implementation plan: what M0 actually left, the decisions
  taken up front, the phase order, and the verification gates.

```
dotnet build          # whole solution, warning-free
dotnet test           # FsCheck properties + golden images
```

## The one-paragraph version

Build a pure compilation pipeline, not a chart wrapper. User-facing DSLs (grammar of
graphics / Manim-style scene / field & simulation) all produce a single small, closed,
backend-agnostic `Scene` IR. Six backends consume it: SVG (pure F#, zero deps, doubles as
the test oracle), TikZ (figures that inherit your LaTeX document's fonts and `siunitx`
units), Skia (PNG/PDF/desktop), Fable (Canvas/WebGL), Silk.NET (GPU), and video.

`float<'u>` flows through everything above the IR and is erased exactly once, at a single
well-defined boundary, into a runtime unit witness carrying the SI dimension vector. That
is what makes `x (fun r -> r.Time)` produce a seconds axis labelled `t [s]` with no
configuration, and what makes plotting metres against seconds a compile error.

Animation is `Anim<'a> = { Duration: float<s>; At: float<s> -> 'a }` — a pure function of
time rather than a mutating timeline. That buys seeking, deterministic frames, and
embarrassingly parallel rendering, none of which Manim can structurally offer.

## What the test suite verifies

Requires the .NET 10 SDK; built and tested against **F# 10 / .NET SDK 10.0.301** with zero
compiler warnings (`TreatWarningsAsErrors` is on, specifically so FS0064 — §12.1's measure
over-constraint — is a build failure rather than a warning nobody reads).

`dotnet test` is not a smoke screen; it is where the design's load-bearing claims are
checked. §13: *"This is the part where an F# plotting library can be demonstrably more
correct than its competitors, and it should be a stated selling point, not an afterthought."*

| | |
|---|---|
| §2 | Compile-time measure algebra with a runtime witness; `J` and `N` canonicalised from the SI dimension vector, next to the naive string composition that gets it wrong |
| §2.2 | Data over 1e-9…5e-9 m yields the axis title `x [nm]` and tick labels `1, 2, 3, 4, 5` |
| §2.3 | An `(m, m/s)` series and an `(s, kg·m²/s²)` series in one list, both measures recovered inside a rank-2 consumer |
| §4 | `inverse (forward x) = x`, monotonicity and endpoint mapping for all seven scale kinds; the three tick properties over 2000 random domains |
| §5 | All nine IR cases through the SVG backend, with structural equality and scene diffing asserted both ways |
| §5.1 | A 50 000-point scatter is **one** `Markers` node — 49 nodes for the whole figure against 50 024 primitives |
| §8.1 | `d/dx` of a spring potential `½kx²` returning **newtons, with the unit inferred rather than annotated** |
| §9 | `float<s>` against `float<m>` puts the literal strings `t [s]` and `x [m]` in the document with nothing configured |
| §12 | Every landmine in §12 pinned, several as *compile-time* assertions — a binding whose type annotation is the claim |
| §13 | 13 golden SVG snapshots, byte-identical and reviewable as text; layout invariants across four themes and four surface sizes |

## Four things that failed first

These are in `docs/design.md` §12 in full, and they are the real constraints on the design:

1. **Measure algebra in operator overloads emits FS0064 and silently over-constrains.**
   Put it in module functions with explicit `[<Measure>]` type parameters.
2. **SRTP constraints must be restated at every wrapper level**, or the first call site
   monomorphises the chain — and the errors surface at the *other* call sites.
3. **`float<'u>` does not satisfy a `float` SRTP overload.** Every numeric type class must
   be measure-generic from its first line.
4. **`Lerp` collides with `System.Double.Lerp`** (.NET 9+). Check `Clamp`, `Sign`, `Abs`
   before naming any SRTP member.

## Where to start building

`docs/design.md` §14 sequences this so every milestone ships something useful on its own.
The recommendation there is to build **M0 + M1 and nothing else** for the first several
months: core types, SVG and TikZ backends, linear/log scales, line/scatter/errorbar,
unit-derived axis labels, and a .NET Interactive formatter.

It's finishable, and it's the only part of the roadmap that nothing in any ecosystem
currently does.
