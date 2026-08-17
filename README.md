# Quiver

A dimensionally-typed visualization system for F# — plotting, animation, and
math/physics visualization where the compiler checks your units.

> Working name. A *quiver plot* is the canonical vector-field visualization; a *quiver*
> in category theory is a directed multigraph, the free generator of a category.
> The library is about both.

## Status

**M0 in progress.** The core is real, builds warning-free, and is verified at runtime.

| Package | State |
|---|---|
| `Quiver.Core` | Complete — `Dim`, `U<'u>`, `Vec2/3`, `Affine<'a,'b>`, `Scale`, `Coord`, the nine-case `Scene` IR, `Interp`, text metrics. **Zero dependencies**, `netstandard2.1`. |
| `Quiver.Units` | Complete — SI + 22 derived + 18 non-SI witnesses, `Dim`-keyed canonicalisation, prefix selection, Unicode/ASCII/LaTeX/siunitx rendering, self-labelling axes. |
| `Quiver.Backend.Svg` | Complete — all nine IR nodes, batched markers, gradients, clipping, a11y semantics, deterministic byte-identical output. |
| `Quiver.Plot` | Complete — Okabe–Ito / viridis / cividis / magma / Oklab-diverging palettes with a CVD + contrast validator, four resolved-value themes, a measure–arrange constraint grid, the axis and legend compilers, line/point/area/bar/errorbar geoms, and `Chart`. Zero dependencies, no backend reference. |
| `Quiver.Tests` | Complete for M0 — **288 tests**, all green: FsCheck property suites for the `Dim` group laws, all seven scale kinds, tick properties, `Interp`, `Affine`, colour and the Scene IR; regression tests pinning every §12 landmine; 13 golden-image snapshots. |

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
- **`spike/`** — an executable spike proving the load-bearing type-level claims. Not a
  library; a record of what compiles and what doesn't. Superseded by `src/` but kept as
  the original evidence.

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

## Running the spike

```
cd spike
dotnet run
```

Requires the .NET 10 SDK. Verified against **F# 10 / .NET SDK 10.0.110**, and it builds
with zero compiler warnings (`TreatWarningsAsErrors` is on).

It will print a walkthrough of each verified claim and write `scene.svg` — a real figure
emitted from the `Scene` IR through a ~50-line zero-dependency reference backend.

What it demonstrates:

| | |
|---|---|
| §2 | Compile-time measure algebra with a runtime witness; `J` and `N` canonicalised from the SI dimension vector, next to the naive string composition that gets it wrong |
| §4 | Linear and log₁₀ scales, with the `inverse (forward x) = x` law checked |
| §8 | `d/dx` of a spring potential `½kx²` returning **newtons, with the unit inferred rather than annotated** |
| §2.3 | An `(m, m/s)` series and an `(s, J)` series in one list, both measures recovered inside a rank-2 consumer |
| §7 | One generic `tween` over `float<m>`, `Vec2<px>` and `Color`; a storyboard seeked directly to t = 2.0 s; frames rendered in parallel |
| §8.2 | A velocity-Verlet oscillator via checkpointed replay — x(π) = −1.0000 m, energy drift 7.4 × 10⁻⁸ over 10 s |
| §5 | The `Scene` IR through the SVG reference backend |

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
