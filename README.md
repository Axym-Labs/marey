# Marey

A dimensionally-typed visualization system for F# — plotting, animation, and
math/physics visualization where the compiler checks your units.

> Named for **Étienne-Jules Marey** (1830–1904), who invented the graphical method in
> experimental science: record a physical quantity as a curve against time, and read the
> physics off the curve. He built the instruments that inscribe a measurement directly as a
> trace, and then invented chronophotography — motion captured as a deterministic time series
> of frames. Measurement, plotting and animation were one subject to him, which is the bet
> this library is making.
>
> It was called *Quiver* until that name turned out to be taken twice over in its own field:
> by [q.uiver.app](https://q.uiver.app/), a commutative-diagram editor that also exports TikZ
> to the same audience, and on NuGet by an unrelated C# graph database.

## Status

**M0 and M1 complete** — see [`docs/m1-plan.md`](docs/m1-plan.md). Seven packages, both
backends, the notebook extension confirmed working in a live Verso host, [the guide](docs/guide.md),
CI, and a release workflow that publishes to nuget.org over OIDC with no stored secret. The core
is real, builds warning-free, and is verified at runtime.

| Package | State |
|---|---|
| `Marey.Core` | Complete — `Dim`, `U<'u>`, `Vec2/3`, `Affine<'a,'b>`, `Scale`, `Coord`, the nine-case `Scene` IR, `Interp`, text metrics. **Zero dependencies**, `netstandard2.1`. |
| `Marey.Units` | Complete — SI + 22 derived + 18 non-SI witnesses, `Dim`-keyed canonicalisation, prefix selection, Unicode/ASCII/LaTeX/siunitx rendering, self-labelling axes. |
| `Marey.Backend.Svg` | Complete — all nine IR nodes, batched markers, gradients, clipping, a11y semantics, deterministic byte-identical output. |
| `Marey.Plot` | Complete — Okabe–Ito / viridis / cividis / magma / Oklab-diverging palettes with a CVD + contrast validator, six resolved-value themes all measured against it, a measure–arrange constraint grid, the axis and legend compilers, line/point/area/bar/errorbar geoms, and `Chart`. Zero dependencies, no backend reference. |
| `Marey.Backend.Tikz` | Complete — all nine IR nodes into `tikzpicture` source, `bp` coordinates, batched marker macros, capability degradation, siunitx axis titles, and figures that inherit the host document's font. |
| `Marey.Html` | Complete — HTML renderings of nine Marey types for a notebook cell: figures and scenes as inline SVG, colours as swatches that do not lie about alpha, ramps resampled through Oklab, palettes, the CVD/contrast report, dimensions, unit witnesses and erased scales. Host-neutral: pure `something -> string`, zero dependencies. |
| `Marey.Verso` | Complete — the [Verso](https://github.com/DataficationSDK/Verso) notebook extension. An `IDataFormatter` at priority 30, above the built-ins that would otherwise render a `Figure` as a field table. Depends on `Verso.Abstractions` alone and on no Marey assembly: it reaches the renderer by reflection, because a Verso extension and the notebook's `#r` live in different `AssemblyLoadContext`s. |
| `Marey.Tests` | **483 tests**, all green: FsCheck property suites for the `Dim` group laws, all seven scale kinds, tick properties, `Interp`, `Affine`, colour and the Scene IR; regression tests pinning every §12 landmine and every numbered decision; 29 golden baselines in SVG *and* `tikzpicture` source; a real `pdflatex` compile gate; an ink gate that rasterises the compiled figure and checks there is something in it; an XML well-formedness pass over every notebook fragment; and the Verso formatter driven across a genuinely isolated collectible `AssemblyLoadContext`. |

M0 is complete: `Marey.Core` + SVG backend + linear/log scales + line/scatter/axes/legend
+ theme record.

```fsharp
open Marey

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

Swap the theme and the same figure becomes TeX whose axis titles are typeset by `siunitx`, in the
host document's own fonts:

```fsharp
Chart.line U.second U.metre ts positions
|> Chart.withTheme Theme.tikz
|> Chart.toFile TikzBackend.render "fall.tex" (Chart.size 320.0 200.0)
```

```tex
\node[anchor=north,text=qcB,font=\fontsize{9}{10.8}\selectfont] at (133.59bp,15.825bp) {t [\qty{}{\second}]};
\node[anchor=base,text=qcB,rotate=90,font=\fontsize{9}{10.8}\selectfont] at (15.825bp,69.075bp) {x [\qty{}{\metre}]};
```

Or drop the same value into a notebook cell and look at it. In
[Verso](https://github.com/DataficationSDK/Verso) — the maintained successor to Polyglot Notebooks,
which Microsoft retired in March 2026 — there is no `.Show()`, no save-then-open and no display
call: the cell's value *is* a `Figure`, and a `Figure` knows how to be an SVG.
[`docs/notebooks/marey-tour.dib`](docs/notebooks/marey-tour.dib) is the tour and
[`docs/notebooks/README.md`](docs/notebooks/README.md) is the setup.

Documents:

- **`docs/guide.md`** — the guide: everything the library does today, with a runnable
  example for each part. Every example in it is extracted and executed by the test suite,
  so it cannot drift from the library without a test going red.
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
