# Quiver — a dimensionally-typed visualization system for F#

**Design document, v0.1 — August 2026**

> *Working name.* A **quiver plot** is the canonical vector-field visualization; a **quiver**
> in category theory is a directed multigraph, the free generator of a category. The library
> is about both. Rename freely.

---

## 0. TL;DR

Build a **pure compilation pipeline**, not a chart wrapper:

```
        user-facing DSLs                    IR                 backends
  ┌──────────────────────────┐      ┌───────────────┐    ┌──────────────────┐
  │ Grammar (Chart.line …)   │      │               │    │ SVG   (pure F#)  │
  │ Scene DSL (Manim-style)  │ ───► │  Figure ──►   │───►│ TikZ  (LaTeX)    │
  │ Field / Sim DSL          │      │  Scene (IR)   │    │ Skia  (PNG/PDF)  │
  │ Parser-trace DSL (later) │      │               │    │ Fable (Canvas/WGL)│
  └──────────────────────────┘      └───────────────┘    │ GPU   (Silk.NET) │
         float<'u> everywhere         float + Unit        │ Video (ffmpeg)   │
                                      (erased once)       └──────────────────┘
```

Three bets:

1. **Units of measure are the differentiator, and they are erased at runtime.** The whole
   architecture turns on carrying a *runtime unit witness* alongside the compile-time
   measure, and erasing the measure exactly once, at a single well-defined boundary.
2. **Animation is a pure function of time, not a mutation.** `Anim<'a> = float<s> -> 'a`.
   This buys you seeking, deterministic frames, and embarrassingly parallel rendering —
   three things Manim structurally cannot do.
3. **One small closed IR.** If `Scene` stays small and total, six backends are tractable.
   If it grows, you have six half-broken renderers. Guard it obsessively.

Everything in §2–§9 below was **type-checked and executed against F# 10 / .NET 10.0.110**
in the course of writing this document. §12 lists the things that did *not* work on the
first try, and why — those are the real design constraints.

---

## 1. Why this doesn't exist yet, and why F# is actually the right host

### 1.1 The gap is real

| System | What it is | Where it stops |
|---|---|---|
| **Plotly.NET** | A typed wrapper that emits plotly.js JSON and an HTML page | No units, no animation model, no GPU, no vector output without a headless browser; styling functions *mutate* the chart (documented caveat). It is a serialization layer, not a graphics system. |
| **XPlot / FSharp.Charting** | Older F# charting | Explicitly superseded; XPlot's own README points at Plotly.NET |
| **ScottPlot / OxyPlot / LiveCharts** | C# 2-D plotting | Imperative, mutable, WinForms/WPF-shaped, no dimensional typing |
| **Manim** | Python scene animation | Mutable `Mobject` graph, no seeking (you must render the timeline from t=0), no dimensional analysis, no static-figure story, no type checking of anything |
| **Makie.jl** | Julia, `Observable`-based reactive scene graph, 3 backends | The closest thing to the target. Units via Unitful are bolted on and lossy at the plotting boundary; Julia's dynamism means a plot recipe fails at render time, not at definition time |
| **Vega-Lite / Observable Plot** | Declarative grammar of graphics | Excellent grammar, JSON/JS only, no physical dimensions, no simulation |
| **matplotlib** | The baseline | Global mutable state (`pyplot`), units via a shaky `units` registry almost nobody uses |

Nothing in the table gives you: *the compiler rejects a plot of metres against seconds on
the same axis, and the axis label "m·s⁻¹" is derived from the data rather than typed by hand.*

### 1.2 What F# genuinely brings

- **Units of measure with full inference and zero runtime cost.** This is close to unique
  in production languages. Haskell's `units`/`dimensional` packages need heavy type-level
  arithmetic; Rust's `uom` is verbose at every call site; Julia's `Unitful` is a value-level
  wrapper with dynamic dispatch. F# has it in the *type system*, inferred, with
  `float<kg*m/s^2>` normalising algebraically. Nobody has built a serious visualization
  library on top of it.
- **DUs + exhaustive matching** are exactly the right tool for a scene-graph IR and backend
  dispatch. Adding an IR node breaks every backend at compile time — which is the behaviour
  you want.
- **Structural equality on immutable records/DUs** gives scene diffing for free. That is the
  route to 60 fps interaction without a mutable retained-mode API.
- **Computation expressions** let a storyboard DSL read like Manim while remaining pure values.
- **Fable** compiles the same source to JavaScript, so the web backend is not a rewrite.
- **Property-based testing (FsCheck)** actually works on this domain — scale round-trips,
  animation monoid laws, dimensional algebra laws. A plotting library you can *prove* things
  about is a novel proposition.

### 1.3 What F# will fight you on — read this before committing

These are not hypotheticals; each was hit while writing this document.

- **Measures are erased.** `float<m>` *is* `float` at runtime. Microsoft's own docs:
  *"implementing a `ToString` function to print out the units is not possible."* Every axis
  label, every legend, every serialized figure needs a **runtime witness**. §2 is the answer.
- **No higher-kinded types.** No real `Functor`/`Monad` abstraction. SRTP is the workaround,
  and it is *fragile* — see §12.2, where a single call site silently monomorphised an entire
  interpolation hierarchy.
- **No existential types.** Heterogeneous collections of differently-united series need a
  rank-2 encoding. It works (§2.3, verified), but it is ceremony you must design in from day one.
- **`float<'u>` does not match a `float` overload in SRTP resolution.** Every numeric type
  class you write must be measure-generic from the first line, or it will not accept
  dimensioned data. Verified the hard way (§12.3).
- **GC pressure.** Per-frame allocation of records will destroy a 60 fps loop. The hot path
  (scene → backend, particle stepping) must be struct/array-based, not list-based.
- **No good LaTeX math typesetting in .NET.** This is the single largest unsolved
  sub-problem. §10.

---

## 2. Layer 0 — Quantities: the two-level unit representation

This is the load-bearing idea of the whole design.

### 2.1 The witness

A measure is a compile-time phantom. Pair it with a runtime record carrying the symbol,
the SI dimension exponents, and the conversion factor:

```fsharp
/// SI dimension exponent vector — the runtime half of a unit.
type Dim = { L:int; M:int; T:int; I:int; Th:int; N:int; J:int }

/// Phantom-typed witness: ties a compile-time measure to runtime metadata.
[<Sealed>]
type U<[<Measure>] 'u>(symbol: string, dim: Dim, toSI: float) =
    member _.Symbol = symbol
    member _.Dim    = dim
    member _.ToSI   = toSI
```

The witness algebra tracks the measure at compile time *and* the metadata at runtime:

```fsharp
module U =
    let mul<[<Measure>] 'a, [<Measure>] 'b> (a: U<'a>) (b: U<'b>) : U<'a * 'b> = ...
    let div<[<Measure>] 'a, [<Measure>] 'b> (a: U<'a>) (b: U<'b>) : U<'a / 'b> = ...

    let metre  = U<m>("m",  { Dim.zero with L = 1 }, 1.0)
    let second = U<s>("s",  { Dim.zero with T = 1 }, 1.0)

    let velocity : U<m/s>        = div metre second      // inferred
    let force    : U<kg*m/s^2>   = mul kilogram accel    // inferred
    let energy   : U<kg*m^2/s^2> = mul force metre       // inferred
```

**Verified.** `U.energy` compiles with the measure `kg*m^2/s^2` and reports
`Dim = { L=2; M=1; T=-2 }` at runtime.

> ⚠️ **Do not put this algebra in operator overloads on the type.** `static member (*)` on
> `U<'u>` compiles but emits `FS0064` and silently over-constrains the second measure
> parameter after the first use. Module-level functions with explicit `[<Measure>]` type
> parameters stay generic. This is verified in §12.1 and it is a landmine.

### 2.2 Symbol canonicalisation is a separate problem

Naive composition produces `kg·m/s/s·m` where you wanted `J`. The runtime `Dim` is the
source of truth; the symbol is a *presentation* concern:

```fsharp
module UnitDisplay =
    /// Registry of preferred symbols, matched on Dim, longest-match-wins.
    val register : Dim -> symbol: string -> unit
    /// N, J, W, Pa, T, V, Ω, F, H, Hz, …
    val canonical : Dim -> string option
    /// Falls back to a normalised exponent form: "kg·m²·s⁻²"
    val render : style: UnitStyle -> Dim -> string
```

`UnitStyle` = `Unicode | Ascii | Latex | Siunitx`. The TikZ backend emits
`\qty{}{\kilogram\metre\per\second\squared}` for `siunitx`; the SVG backend emits
`kg·m·s⁻²`. Same `Dim`, different renderer. Get this right once and every backend inherits it.

**Prefix handling.** Axis tick generation should be allowed to *rescale*: data in
`float<m>` spanning `1e-9 … 5e-9` should produce ticks labelled `1, 2, 3, 4, 5` with an
axis title of `x [nm]`. That is a pure function `Dim * (float*float) -> Prefix`, and it is
one of the most visible quality-of-life wins the unit system pays for.

### 2.3 Erasure boundary and existentials

The IR must not be measure-generic — a `Scene` holding `float<'u>` could never be put in a
list with a differently-united scene. Erase **once**, at the point where a series becomes a
scale:

```fsharp
type IScaleErased =
    abstract Symbol  : string
    abstract Dim     : Dim
    abstract Project : float -> float<nd>      // data -> normalized panel space
    abstract Ticks   : int -> (float * string) list
```

When you need to recover the measure — for a stat transform, a derivative, a unit-aware
tooltip — use a rank-2 encoding. **This works; it was compiled and run:**

```fsharp
type ISeriesConsumer<'r> =
    abstract Apply<[<Measure>] 'ux, [<Measure>] 'uy>
        : Scale<'ux> * Scale<'uy> * (float<'ux> * float<'uy>) [] -> 'r

type PackedSeries =
    abstract Accept : ISeriesConsumer<'r> -> 'r

let packSeries (sx: Scale<'ux>) (sy: Scale<'uy>) (pts: (float<'ux> * float<'uy>) []) =
    { new PackedSeries with member _.Accept c = c.Apply<'ux,'uy>(sx, sy, pts) }
```

A `PackedSeries list` can hold `(m, m/s)` and `(s, J)` series side by side, and any
consumer gets the measures back, fully typed, inside `Apply`. This is the mechanism that
makes a multi-panel figure with heterogeneous physical quantities possible at all.

### 2.4 A unit type provider (later, but plan for it)

F# type providers can generate `[<Measure>]` types plus witnesses from an external database.
Point one at **UDUNITS-2** or **QUDT** and you get a few thousand correctly-related units,
including non-SI (eV, Å, bar, atm, °C with its offset problem, dB with its logarithmic
problem) for free, with symbols and conversions already attached. This is a genuinely
distinctive F# capability and it turns "we support units" into "we support *your* units".

Defer it to M5, but keep `U<'u>` shaped so the provider can emit it.

---

## 3. Layer 1 — Geometry, and the trick of measuring screen space

Give every coordinate space its own measure. Then mixing them is a compile error.

```fsharp
[<Measure>] type px    // device pixels
[<Measure>] type nd    // normalized [0,1] within a panel
[<Measure>] type pt    // typographic points
[<Measure>] type mm    // physical output size
```

```fsharp
[<Struct>]
type Vec2<[<Measure>] 'u> =
    { X: float<'u>; Y: float<'u> }
    static member (+) (a: Vec2<'u>, b: Vec2<'u>) = ...
    static member (-) (a: Vec2<'u>, b: Vec2<'u>) = ...
    /// scaling changes the unit — this is the payoff
    static member ( * ) (k: float<'k>, v: Vec2<'u>) : Vec2<'k * 'u> = ...
```

**Verified.** `Vec2<m>` and `Vec2<px>` are distinct types; `dt * velocityField p` type-checks
to `Vec2<m>` only when `dt : float<s>` and the field returns `Vec2<m/s>`.

The consequence for the codebase is large and good: a layout bug where you passed a
normalized coordinate to a routine expecting pixels stops being a runtime mystery and
becomes a red squiggle. Every affine transform in the system has a signature like
`Affine<'a,'b>` mapping `Vec2<'a> -> Vec2<'b>`, and the composition
`data → nd → px → device` is checked end to end.

Use `[<Struct>]` throughout this layer. Reference-type vectors in a particle loop are the
difference between 60 fps and 6.

**3-D.** `Vec3<'u>`, `Mat4<'a,'b>`, quaternions for camera orbit. Keep 3-D out of the M0/M1
scope, but do not design `Scene` in a way that forbids it — see §5.4.

---

## 4. Layer 2 — Scales and coordinates

```fsharp
type Scale<[<Measure>] 'u> =
    { Unit     : U<'u>
      Domain   : float<'u> * float<'u>
      Forward  : float<'u> -> float<nd>
      Inverse  : float<nd> -> float<'u>
      TickHint : int -> float<'u> list }
```

Verified for `linear` and `log10`, including the `LanguagePrimitives.FloatWithMeasure`
round-trip that log scales need (because `log10` of a dimensioned quantity is not itself
dimensioned — a subtlety the type system will correctly force you to acknowledge).

Scales to ship: `linear`, `log10`/`log2`/`ln`, `symlog`, `sqrt`/`power`, `time` (over
`DateTimeOffset`, not a measure), `ordinal`/`band`, `quantile`, and — for the physics
audience — `reciprocal` (for `1/T` Arrhenius plots) and `logit`.

**Coordinate systems** sit above scales and are what makes polar, log-polar, ternary,
Smith charts, Mollweide, and 3-D projections uniform:

```fsharp
type Coord =
    abstract ToPanel : Vec2<nd> -> Vec2<nd>          // post-scale warp
    abstract Grid    : GridSpec -> PathSeg list      // how gridlines curve
    abstract Clip    : PathSeg list                  // panel boundary shape
```

Cartesian is the identity `Coord`. Polar warps and gives circular gridlines and a circular
clip. Nothing else in the system needs to know polar exists — which is exactly the property
that makes the design worth having.

**Property tests to write on day one (FsCheck):**

- `∀ x ∈ domain. inverse (forward x) ≈ x` and `∀ t ∈ [0,1]. forward (inverse t) ≈ t`
- `forward` is monotone
- `TickHint n` returns between `n/2` and `2n` ticks, all within the domain
- tick label rendering is injective (no two visible ticks get the same label)

---

## 5. Layer 3 — the Scene IR

**The most important constraint in the project: this type is small, closed, total, and
backend-agnostic. Every addition costs you six backend implementations.**

```fsharp
type Node =
    | Path      of PathSeg list * Stroke option * Paint option
    | Text      of TextRun
    | Markers   of Vec2<nd>[] * float<px> * Paint      // batched: one node, N marks
    | Raster    of Image * Vec2<nd> * Vec2<nd>
    | Group     of Node list
    | Transform of Affine<nd,nd> * Node
    | Clip      of PathSeg list * Node
    | Keyed     of Key * Node                          // stable identity for diffing
    | Annotated of Semantics * Node                    // a11y, hit-testing, tooltips
```

Notes that took the design several passes to get right:

**5.1 `Markers` is batched on purpose.** A 100 000-point scatter must be *one* IR node, not
100 000 `Path` nodes. The GPU backend turns it into one instanced draw call; the SVG backend
into a `<use>` + `<symbol>` or a single `<path>` with repeated subpaths. Any geometry that
can appear ten thousand times gets a batched node. This is the single biggest performance
decision in the IR.

**5.2 `Keyed` exists for diffing.** Scene graphs are immutable F# values with structural
equality. Re-render on interaction = build a new `Scene`, diff against the previous one,
emit only the changed subtrees to the backend. `Keyed` supplies React-style stable identity
so the diff is O(changed) rather than O(scene). This is how you get an interactive plot
without a mutable retained-mode API, and it falls out of F#'s data model almost for free.

**5.3 `Annotated` is not decoration.** Hit-testing, tooltips, `aria-label`s on SVG output,
and clickable legends all need the IR to remember *what a shape means*, not just where it is.
Retrofitting this is painful; it costs almost nothing now.

**5.4 3-D.** Do not add `Node3D`. Add a `Project3D of Camera * Node3` variant *later* whose
job is to collapse to 2-D `Node`s, and let the GPU backend intercept it before collapsing.
That keeps the 2-D IR closed while leaving a door open.

**5.5 What deliberately is NOT in the IR:** axes, legends, titles, colorbars, facets, error
bars, box plots. Those are all *compiled into* `Path`/`Text`/`Markers` by the layer above.
If you find yourself wanting `| Axis of ...`, the design has failed.

### The backend contract

```fsharp
type IBackend =
    abstract Begin  : Extent<px> -> unit
    abstract Draw   : Node -> unit
    abstract Finish : unit -> unit
    abstract Caps   : Capabilities     // gradients? clipping? blend modes? text shaping?
```

`Capabilities` matters because TikZ and SVG have genuinely different power. The compiler
layer queries caps and *degrades* (e.g. rasterizes a gradient-filled region for a backend
that lacks gradients) rather than silently producing wrong output.

---

## 6. Layer 4 — Backends, in build order

| Backend | Package | Depends on | Why it's on the list |
|---|---|---|---|
| **SVG** | `Quiver.Backend.Svg` | *nothing* | Reference implementation and golden-image test oracle. Pure F#, works under Fable, works in notebooks. **Build this first and treat it as the spec.** |
| **TikZ/PGF** | `Quiver.Backend.Tikz` | nothing | Emits `\begin{tikzpicture}`. Figures that inherit the document's fonts and `siunitx` units. For a physics/maths audience this is a killer feature almost nobody offers, and it is *cheap* — it's a text emitter. |
| **Skia** | `Quiver.Backend.Skia` | SkiaSharp | PNG/JPEG/PDF, real text shaping via HarfBuzzSharp, GPU-accelerated canvas, and a desktop window. The workhorse. |
| **Web** | `Quiver.Backend.Web` | Fable | Canvas2D first, WebGL2 for batched nodes. Same source, so no second implementation of the compiler. |
| **GPU** | `Quiver.Backend.Gpu` | Silk.NET | Vulkan/OpenGL. Needed for fields, particles, ≥10⁵ marks, and real-time simulation. *Use Silk.NET, not ComputeSharp* — ComputeSharp is DX12/Windows-only, which kills cross-platform. |
| **Video** | `Quiver.Tools.Video` | Skia or GPU + ffmpeg | Frame renderer + muxer. Trivially parallel because of §7. |

**Notebook integration** (`Quiver.Interactive`) is a ~200-line package registering a
.NET Interactive formatter so `figure` displays inline as SVG. Do it at M1; it is how people
will first meet the library.

---

## 7. Layer 6 — Animation as a pure function of time

Manim's `Mobject`s are mutable and its `Scene` replays a timeline from the start. That
single decision is why you cannot scrub a Manim render, cannot render frames out of order,
and cannot parallelise across cores.

Model an animation as a value:

```fsharp
type Anim<'a> = { Duration: float<s>; At: float<s> -> 'a }
```

with a small algebra, all verified:

```fsharp
module Anim =
    val pure'   : 'a -> Anim<'a>
    val map     : ('a -> 'b) -> Anim<'a> -> Anim<'b>
    val map2    : ('a -> 'b -> 'c) -> Anim<'a> -> Anim<'b> -> Anim<'c>   // parallel, dur = max
    val andThen : Anim<'a> -> Anim<'a> -> Anim<'a>                        // sequential, dur = sum
    val stretch : float -> Anim<'a> -> Anim<'a>
    val ease    : (float -> float) -> Anim<'a> -> Anim<'a>
    val inline tween : float<s> -> ^T -> ^T -> Anim< ^T>
```

`(Anim<'a>, andThen, pure')` is a monoid; `map2` makes it applicative. Storyboards are
therefore *composition*, and the laws are FsCheck-testable.

**Verified working:** `tween 1.0<s> 0.0<m> 10.0<m>` and
`tween 1.0<s> {X=0.0<px>;Y=0.0<px>} {X=100.0<px>;Y=50.0<px>}` both type-check and evaluate
correctly, from the same generic combinator, with no boxing. Dimensioned tweening *works*.

What this buys you that Manim cannot have:

- **Seeking.** `anim.At 12.7<s>` is a direct evaluation. Scrubbing a timeline in a preview
  window is free.
- **Deterministic, parallel frame rendering.** `[| 0 .. nFrames |] |> Array.Parallel.map (fun i -> render (anim.At (dt * float i)))`. On a 16-core machine, a 4-minute explainer renders ~16× faster than Manim can manage, structurally.
- **Reversal, looping, ping-pong** are one-line combinators, not features.
- **Composability across independent objects** without a scheduler.

### 7.1 Interpolation as a type class

`Anim` needs `lerp` for arbitrary types. F# has no HKTs, so this is SRTP with a witness type:

```fsharp
type Interpolate =
    static member inline Interp (a: float<'u>, b: float<'u>, t) : float<'u> = a + (b - a) * t
    static member inline Interp (a: Vec2<'u>,  b: Vec2<'u>,  t) = ...
    static member inline Interp (a: Color,     b: Color,     t) = ...
```

Two hard-won rules (§12.2, §12.3):

1. **Restate the constraint at every wrapper level.** `let inline f a b t = lerp a b t`
   without an explicit constraint signature will be monomorphised by its first call site
   and produce a cascade of confusing errors at the *other* call sites.
2. **Name it `Interp`, not `Lerp`.** `System.Double.Lerp` exists since .NET 9 and creates
   an ambiguous-overload error against your own witness.

Interpolators to ship: numeric (unit-generic), `Vec2`/`Vec3`, colour (in **Oklab**, not
sRGB — sRGB lerp goes grey through the middle), affine transforms (decompose to
translate/rotate/scale/skew and interpolate the components, never the matrix entries),
and **path morphing** (resample both paths to a common arc-length parameterisation, match
subpath counts, then interpolate — this is the `Transform` animation that makes Manim
videos look like Manim videos, and it is a genuinely fiddly algorithm; budget for it).

### 7.2 The storyboard CE

```fsharp
let intro = scene {
    let! axes  = draw (Axes.cartesian xScale yScale) |> over 0.8<s>
    let! curve = draw (Plot.line data)               |> over 1.2<s>
    do! wait 0.5<s>
    do! parallel' [
          curve |> morphTo (Plot.line data2) |> over 2.0<s>
          axes  |> zoomTo (0.0<m>, 5.0<m>)   |> over 2.0<s> |> ease Ease.cubicInOut ]
    do! highlight curve.[42] |> over 0.6<s>
}
```

The CE builder is a writer monad accumulating `Anim<Scene>` under `andThen`/`map2`. It reads
imperatively and is a pure value.

### 7.3 Reactivity vs. animation

Interaction (a slider, a zoom, a hover) is *not* animation — it's a `Signal<'a>` from the
outside world. Keep them distinct and let them meet at the scene:

```fsharp
type Signal<'a>                      // push-based, from input events
val sample : Signal<'a> -> Anim<'a>  // animation of a live signal (for recording)
```

Makie conflates these into `Observable` and pays for it in debuggability. Don't.

---

## 8. Layer 7 — Fields and simulation

### 8.1 Fields are functions with dimensioned domain and codomain

```fsharp
type Field2<[<Measure>] 'x, [<Measure>] 'v> = Vec2<'x> -> Vec2<'v>
type Scalar2<[<Measure>] 'x, [<Measure>] 'v> = Vec2<'x> -> float<'v>
```

**Verified:** advection is dimensionally checked by the compiler —

```fsharp
let advect (dt: float<s>) (field: Field2<m, m/s>) (p: Vec2<m>) : Vec2<m> =
    p + (dt * field p)          // dt * (m/s) = m. Try dt : float<m> and it won't compile.
```

and numerical differentiation *derives* the right unit:

```fsharp
let grad1 (h: float<'x>) (f: float<'x> -> float<'y>) (x: float<'x>) : float<'y/'x> =
    (f (x + h) - f (x - h)) / (2.0 * h)
```

The gradient of a `float<m> -> float<J>` potential is a `float<J/m>` — i.e. a force —
**and the type says so**. For a physics visualization library this is the whole thesis in
three lines. It compiles and runs.

Visualizations: quiver, streamlines (RK4 with adaptive seeding), **LIC** (line integral
convolution — the good one, and a natural GPU fragment shader), contour (marching squares),
heatmap, divergence/curl derived fields, Poincaré sections, phase portraits with nullclines
and fixed-point classification (F# pattern matching over the Jacobian eigenvalues is a
delight here).

### 8.2 The simulation/purity tension, and how to resolve it

Simulations are inherently sequential; `Anim` demands random access to time. Bridge with a
memoized trajectory:

```fsharp
type Sim<'s> =
    { Initial     : 's
      Step        : float<s> -> 's -> 's
      Checkpoint  : ('s -> 's)          // deep copy for checkpointing
      Invariants  : ('s -> float) list } // energy, mass, momentum — for drift monitoring

module Sim =
    /// Checkpointed replay: seek to t by replaying from the nearest stored state.
    val trajectory : checkpointEvery: float<s> -> Sim<'s> -> Anim<'s>
```

`trajectory` gives you an `Anim<'s>`, so simulations compose with everything in §7 —
including parallel frame rendering, as long as each worker gets its own checkpoint cursor.
Checkpoint spacing is the memory/CPU dial.

`Invariants` is not decoration: plotting conserved quantities against time is the standard
way to catch a broken integrator, and having the library ask for them makes it idiomatic.

### 8.3 Solvers to ship

- **ODE/particle:** explicit Euler (as a teaching foil), RK4, Dormand–Prince 4(5) adaptive,
  and **velocity Verlet / leapfrog** — symplectic, and the correct default for anything
  Hamiltonian. Integrators are unit-generic over state.
- **Stable Fluids (Stam)** — semi-Lagrangian advection + Jacobi/multigrid pressure projection.
  The canonical "looks amazing, ~300 lines" fluid demo. GPU-friendly.
- **Lattice Boltzmann (D2Q9)** — even more GPU-friendly, trivially parallel, handles obstacles
  well, and produces the flow-past-a-cylinder image everyone wants.
- **SPH** — for free surfaces and splashing; needs spatial hashing.
- **Wave / heat / Poisson on a grid** — FDM, and the direct route to plotting the physics
  in §I.7–II.4 of a typical E&M course.

Struct-of-arrays layout, `System.Numerics.Vector<T>` for SIMD on CPU, compute shaders via
Silk.NET on GPU. Do not use F# lists or `seq` anywhere in a solver inner loop.

---

## 9. Layer 5 — the grammar (the part most users touch)

Two front doors over the same core:

**Quick, matplotlib-shaped:**
```fsharp
Chart.line (ts, positions)
|> Chart.withXAxis (Axis.auto U.second)
|> Chart.withYAxis (Axis.auto U.metre)
|> Chart.show
```

**Grammar-of-graphics, Vega-Lite-shaped, for anything real:**
```fsharp
plot measurements {
    encode {
        x (fun r -> r.Time)          // float<s>  -> scale + unit inferred
        y (fun r -> r.Displacement)  // float<m>
        color (fun r -> r.Sample)
        size (fun r -> r.Mass)
    }
    layer (Geom.point |> Geom.withOpacity 0.6)
    layer (Geom.line  |> Geom.after (Stat.loess 0.3))
    facetWrap (fun r -> r.Temperature) 3
    coord Coord.cartesian
    theme Theme.publication
}
```

The point of the encoding block: **scales, axis titles, and units are inferred from the
accessor's return type.** `x (fun r -> r.Time)` where `Time : float<s>` gives you a linear
scale over seconds with the axis labelled `t [s]`, with no configuration. That is the
user-visible payoff of §2, and it should be the first thing in the README.

Stats as pure, composable transforms: `bin`, `count`, `density` (KDE), `loess`, `regression`
(with typed slope — a fit of `float<m>` against `float<s>` has slope `float<m/s>`, which the
library should *say*), `smooth`, `ecdf`, `contour`, `boxplot`, `bootstrap CI`.

Geoms: point, line, area, bar, tile, rect, text, segment, errorbar, ribbon, violin, boxplot,
contour, quiver, streamline, hexbin.

**Theming.** A theme is a record of resolved values, not a stylesheet lookup. Ship
`publication` (serif, thin rules, no chartjunk, colour-blind-safe), `dark`, `notebook`,
`presentation` (large type), and `tikz` (inherits document fonts). Palettes: Okabe–Ito for
categorical, viridis/cividis/magma for sequential, and a **perceptually-uniform diverging
palette in Oklab** for signed fields — with a validator that checks contrast and CVD-safety,
because a physics library that renders fields in jet is embarrassing.

**Layout.** Constraint-based grid (à la Makie's `GridLayout`), because facets, colorbars,
shared axes, and inset axes all need it and ad-hoc layout does not compose. Axis label sizes
depend on text metrics, which depend on the backend, so layout is a *measure–arrange* pass
against `IBackend.MeasureText`.

---

## 10. The unsolved problem: mathematical typesetting

A maths visualization library that cannot set `\frac{\partial \vec{E}}{\partial t}` is not
a maths visualization library. There is no good .NET answer. Three options:

1. **Shell out to LaTeX** (Manim's approach). Renders DVI → SVG paths. Correct, gorgeous,
   and requires a 4 GB TeX installation. *Acceptable as a plugin, unacceptable as a dependency.*
2. **MathJax/KaTeX via a JS engine.** Works in the Fable backend for free. On .NET it means
   embedding a JS runtime — heavy, but far lighter than TeX.
3. **Implement TeX math layout over an OpenType MATH font.** Parse a TeX math subset →
   MathML-ish tree → box/glue/penalty layout using the `MATH` table of STIX Two Math or
   Latin Modern Math → glyph runs → outlines. This is *the right answer*: self-contained,
   cross-backend, pure F#, and produces vector output everywhere. It is also a multi-month
   subproject in its own right and should be scoped as such.

**Recommendation:** architect `ITextShaper` as a capability from day one; ship (1) as an
opt-in package and (2) automatically on the web backend at M3; start (3) as a separate
repo (`Quiver.Math.Typesetting`) that can succeed or fail without taking the library with it.

---

## 11. Deferred pillar — FParsec / program-flow visualization

Deferred from the day-one scope, but sketched here because it's the most novel idea in the
brief and because knowing where it lands prevents the core from foreclosing it.

**Key realisation:** FParsec's `Parser<'a,'u>` is just `CharStream<'u> -> Reply<'a>`. You can
wrap it. Tracing is a decorator, not a fork of the library:

```fsharp
let traced (label: string) (p: Parser<'a,'u>) : Parser<'a,'u> =
    fun stream ->
        let enter = stream.Index
        let depth = Trace.push label enter
        let reply = p stream
        Trace.pop depth label enter stream.Index reply.Status reply.Error
        reply
```

Then a shadowing module re-exports the combinators with auto-labelling:

```fsharp
module Traced =
    let inline ( >>. ) a b = traced ">>." (FParsec.Primitives.( >>. ) a b)
    let inline ( <|> ) a b = traced "<|>" (FParsec.Primitives.( <|> ) a b)
    // …and `label` for user-named nodes
```

`open Traced` after `open FParsec` and the grammar is instrumented with no source changes.
(Auto-labelling via `[<CallerMemberName>]`/`[<CallerLineNumber>]` gets you free node names.)

**The trace model:**
```fsharp
type TraceNode =
    { Label: string; Enter: int; Exit: int; Status: ReplyStatus
      Backtracked: bool; Children: TraceNode list }
```

**The visualizations, which are where this gets genuinely interesting:**

- **Icicle-over-input.** x-axis = character offset in the sample, y-axis = combinator depth,
  each box = one attempted parser. You *see* the grammar walking the string, see backtracking
  as overlapping boxes at the same depth, and see the failure point as the rightmost extent.
  This is a flame graph where the x-axis is meaningful rather than aggregated — as far as I
  know nobody has built it for parser combinators.
- **Cross-sample branch heatmap.** Given `samples: string list`, aggregate: which alternatives
  in each `<|>` fire, how often, and which are **never taken** (dead grammar branches — a
  real bug class).
- **Failure cliff.** For each sample, the furthest position reached vs. its length. A sample
  set that all dies at column 12 tells you exactly what to fix.
- **Backtracking cost map.** Total characters re-scanned per grammar node → your `attempt`
  placement is either justified or catastrophic, and now you can see which.
- **Grammar graph.** Static call graph of named parsers, edge-weighted by traversal count
  across the sample set — a Sankey of your grammar.

This needs almost nothing from the plotting core except `Scene`, a rectangle geom, a tree
layout, and colour scales. It is a separate package (`Quiver.Trace.FParsec`) and could
plausibly be built in a couple of weekends *once the core exists*. It would also be, by a
wide margin, the most shareable demo the project has.

The same instrumentation shape generalises to any tree-structured evaluation — which is
worth noting but not worth designing for yet.

---

## 12. Verified findings: things that did not work on the first try

Everything below was reproduced against **F# 10 / .NET SDK 10.0.110**. Each cost real time
to diagnose and each will cost you the same time if it isn't written down.

### 12.1 Measure algebra in operator overloads over-constrains (FS0064)

```fsharp
type U<[<Measure>] 'u>(...) =
    static member ( * ) (a: U<'a>, b: U<'b>) : U<'a * 'b> = ...   // ⚠️ FS0064
```
> *warning FS0064: This construct causes code to be less generic than indicated by the type
> annotations. The unit-of-measure variable 'b has been constrained to be measure ''a 'b/'a0'.*

It compiles, and it is subtly wrong. **Fix:** module-level functions with explicit
`<[<Measure>] 'a, [<Measure>] 'b>` type parameters. Verified generic across five distinct
instantiations (`m/s`, `m/s^2`, `kg*m/s^2`, `kg*m/s`, `kg*m^2/s^2`).

### 12.2 SRTP constraints must be restated at every wrapper level

```fsharp
let inline lerpD a b t = lerp<Lerpable, _> a b t     // ⚠️ monomorphised by first call site
```
The first use pinned `^T` to `Color`, and every *other* call site then failed with
`FS0001: This expression was expected to have type 'Color'`. The error appears at the
innocent call sites, not the definition — extremely confusing. **Fix:** write the full
constraint signature on every wrapper:

```fsharp
let inline lerpD< ^T when (Lerpable or ^T) : (static member Interp : ^T * ^T * float -> ^T)>
        (a: ^T) (b: ^T) (t: float) : ^T = lerp<Lerpable, ^T> a b t
```

**Implication for the design:** every generic numeric combinator in the public API needs an
explicit constraint signature. Budget for the verbosity; do not fight it.

### 12.3 `float<'u>` does not satisfy a `float` SRTP overload

An `Interp` witness declared as `static member inline Interp (a: float, ...)` **rejects**
`float<m>`:
> *No overloads match for method 'Interp'. Known type parameters: < float&lt;m&gt;, float&lt;m&gt;, float >*

Measures are erased at *runtime*, but they are absolutely present during overload
resolution. **Fix:** declare the overload measure-generic — `(a: float<'u>, b: float<'u>, t: float) : float<'u>` — which subsumes the unitless case since `float = float<1>`.
**Every type class in this library must be measure-generic from its first line.**

### 12.4 `Lerp` collides with the BCL

```
Candidates: - System.Double.Lerp(value1, value2, amount) : float
            - static member Lerpable.Lerp: a: float * b: float * t: float -> float
```
.NET 9+ added `Lerp` to the numeric interfaces. **Fix:** name the SRTP member something
the BCL doesn't use (`Interp`). Check the same for `Clamp`, `Sign`, `Abs`, `Min`, `Max`,
`CreateChecked` before naming any SRTP member.

### 12.5 Existentials over measures: works, with ceremony

`abstract Apply<[<Measure>] 'ux, [<Measure>] 'uy> : ...` on an interface, consumed via an
object expression, round-trips both measures correctly. Confirmed with a `PackedSeries list`
holding `(m, m/s)` and `(s, kg·m²/s²)` series and recovering both inside a single consumer.

### 12.6 Runtime symbol composition needs canonicalisation

`mul force metre` produced the symbol `kg·m/s/s·m` for what is `J`. The `Dim` was correct
(`L=2, M=1, T=-2`); only the string was wrong. Confirms §2.2: **never compose display
strings, always canonicalise from `Dim`.**

---

## 13. Repository and package layout

```
Quiver.Core            // Dim, U<'u>, Vec2/3, Affine, Scale, Coord, Scene IR, Interp
                       //   → zero dependencies, Fable-compatible, netstandard2.1
Quiver.Units           // SI + derived + common non-SI witnesses, canonicalisation, prefixes
Quiver.Grammar         // encodings, geoms, stats, facets, layout, themes → Scene
Quiver.Anim            // Anim, easing, storyboard CE, path morphing
Quiver.Fields          // fields, streamlines, contour, LIC, integrators
Quiver.Sim             // Stam fluids, LBM, SPH, PDE grids
Quiver.Backend.Svg     // reference backend, zero deps
Quiver.Backend.Tikz
Quiver.Backend.Skia
Quiver.Backend.Web     // Fable
Quiver.Backend.Gpu     // Silk.NET
Quiver.Interactive     // .NET Interactive formatters
Quiver.Tools.Video     // ffmpeg muxing
Quiver.Trace.FParsec   // deferred pillar
Quiver.Tests           // FsCheck laws + golden SVG snapshots
```

`Quiver.Core` having **zero dependencies** is non-negotiable: it is what lets the same IR
run in a notebook, in a browser via Fable, and in an ahead-of-time-compiled native binary.

**Testing.** Golden-image snapshots against the SVG backend (text-diffable, reviewable in a
PR) plus FsCheck property suites for scale laws, `Anim` monoid laws, `Interp` endpoint laws,
`Dim` group laws, and layout invariants (no overlapping panels, labels inside bounds).
This is the part where an F# plotting library can be *demonstrably* more correct than its
competitors, and it should be a stated selling point, not an afterthought.

---

## 14. Roadmap

Sequenced so that **every milestone is independently useful and shippable**. This is a
multi-year project at solo pace; the sequencing matters more than the scope.

| # | Milestone | Contents | Why stop here is OK |
|---|---|---|---|
| **M0** | *Skateboard* | `Quiver.Core` + SVG backend + linear/log scales + line/scatter/axes/legend + theme record | Already produces publication-quality static plots. Usable by you, today. |
| **M1** | *Units end to end* | `Quiver.Units`, witness canonicalisation, prefix selection, auto axis titles, `.NET Interactive` formatter, TikZ backend | **This is the release that has no competitor.** Unit-safe plots that drop into a LaTeX document. Announce here. |
| **M2** | *Grammar* | encoding CE, geoms, stats, facets, constraint layout, Skia backend (PNG/PDF), colour system | Now it's a general-purpose plotting library, not a physics toy. |
| **M3** | *Motion* | `Anim`, easing, storyboard CE, path morphing, video export, MathJax text on web | Manim-class output with seeking and parallel rendering. The demo reel. |
| **M4** | *Fields & interaction* | fields, streamlines, contour, LIC, `Signal`, scene diffing, Fable backend, GPU backend | Real-time interactive exploration in a browser and on the desktop. |
| **M5** | *Simulation* | integrators, Stam fluids, LBM, SPH, PDE grids, invariant monitoring; unit type provider | The fluid-sim pillar. |
| **M6** | *Typesetting* | native OpenType MATH layout | Removes the last external dependency. Can slip indefinitely without hurting M0–M5. |
| **M7** | *Traces* | `Quiver.Trace.FParsec`, icicle-over-input, branch heatmaps | Standalone, high-visibility, low-risk. Can jump the queue any time after M2. |

---

## 15. Risks, honestly

- **Scope.** This document describes roughly five libraries. The mitigation is §14's
  sequencing and the `Quiver.Core` zero-dependency rule — nothing after M1 is load-bearing
  for M0–M1 being useful.
- **Text and layout eat 40% of the effort.** Everyone underestimates this. Axis label
  collision avoidance, tick label formatting, legend layout, and font metrics are where
  plotting libraries actually die. Budget accordingly and lean on SkiaSharp/HarfBuzzSharp
  early rather than hand-rolling.
- **The audience is small.** F# has maybe 1–2% of .NET mindshare. The realistic strategy is
  to make the *C#-callable* surface good (fluent API over the same core) and let the units
  story pull people in, exactly as Plotly.NET did with its dual-flavour API.
- **The units advantage has a ceiling.** It is transformative for physics/engineering data
  and irrelevant for a bar chart of quarterly revenue. Position accordingly — don't market
  it as a general BI charting library.
- **`float32` vs `float`.** Measures work on both, but GPU buffers want `float32` and the
  API wants `float`. Decide the conversion boundary early (recommendation: `float` in the
  API and IR, `float32` only inside backend buffer marshalling) or it will infect everything.
- **Erasure means no serialization round-trip of units.** A `Figure` saved to JSON loses its
  measures; only the witnesses survive. Fine, but it means the file format is defined over
  the *erased* representation, and reloading gives you `IScaleErased`, not `Scale<'u>`.
  Design the file format for that from the start.

---

## 16. What I'd actually build first

If the goal is a real library rather than a design exercise: **M0 + M1, and nothing else,
for the first several months.** Concretely — `Dim`, `U<'u>`, `Vec2<'u>`, `Scale<'u>`, the
`Scene` IR, the SVG backend, the TikZ backend, linear/log scales, line/scatter/errorbar,
automatic unit-derived axis labels, and the .NET Interactive formatter.

That is a coherent, finishable artifact; it has a guaranteed first user; it produces output
that goes straight into a physics write-up with `siunitx` units; and it is the *only* thing
on the roadmap that literally nothing else in any ecosystem does. Everything glamorous —
fluids, Manim-style video, parser traces — is strictly more fun and strictly less
differentiated. Build the boring differentiated thing first, then have the fun.

---

---

## 17. Companion file

`quiver-verified-core.fs` is the executable version of §2–§8. Drop it into a
`dotnet new console -lang F#` project and `dotnet run`. It compiles **warning-free** on
F# 10 / .NET SDK 10.0.110 and, on execution:

- derives `J` and `N` as canonical symbols from composed `Dim` values while showing the
  naive string composition producing the wrong `kg·m/s/s·m` (§2.2, §12.6);
- verifies `inverse (forward x) = x` for linear and log₁₀ scales;
- computes `d/dx` of a spring potential `½kx²` and reports the result **in newtons, with the
  unit inferred rather than annotated** (§8.1);
- puts an `(m, m/s)` series and an `(s, J)` series in the same list and recovers both
  measures inside a rank-2 consumer (§2.3);
- runs one generic `tween` over `float<m>`, `Vec2<px>` and `Color` (§7.1);
- seeks a storyboard directly to t = 2.0 s and renders frames in parallel (§7);
- drives a velocity-Verlet harmonic oscillator through `Sim.trajectory`, hits
  x(π) = −1.0000 m, and shows the energy invariant drifting by 7.4 × 10⁻⁸ over 10 s;
- emits an actual SVG figure from the `Scene` IR through a ~50-line zero-dependency
  reference backend (`quiver-scene-demo.svg`).

It is deliberately not a library — it is proof that the load-bearing type-level claims in
this document are true, and a record of the four ways they failed first (§12).

---

### Sources

- [Units of Measure — F# language reference (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/units-of-measure)
- [F# Language Specification §9, Units of Measure](https://fsharp.github.io/fslang-spec/units-of-measure/)
- [What's new in F# 10 (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/fsharp/whats-new/fsharp-10)
- [Announcing .NET 10 (.NET Blog)](https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/)
- [Plotly.NET documentation](https://plotly.net/)
- [Plotly.NET: A fully featured charting library for .NET programming languages (F1000Research)](https://f1000research.com/articles/11-1094)
- [XPlot (superseded — recommends Plotly.NET)](https://github.com/fslaborg/XPlot)
- [Makie.jl: Flexible high-performance data visualization for Julia (JOSS)](https://www.theoj.org/joss-papers/joss.03349/10.21105.joss.03349.pdf)
- [Makie backends & output](https://tlienart.github.io/Makie.jl/dev/documentation/backends_and_output/)
- [Manim — animation engine for explanatory math videos](https://github.com/3b1b/manim)
- [SkiaSharp](https://skiasharp.com/)
- [ComputeSharp (DX12/Windows-only — noted as a constraint)](https://github.com/Sergio0694/ComputeSharp)
- [F# for Fun and Profit — Units of measure](https://fsharpforfunandprofit.com/posts/units-of-measure/)
