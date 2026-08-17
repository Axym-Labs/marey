# Quiver — implementation decision log

Deviations from `docs/design.md`, and the implicit consequences the design document implies
but does not spell out. Each entry says what the design says, what was actually built, and
why. Entries marked **[forced]** are cases where following the sketch literally would break
something else the same document requires.

Status: **M0 complete.** Whole solution builds warning-free; `dotnet test` is green at
**288 tests** (FsCheck property suites, §12 regression pins, and 13 golden-image snapshots).

---

## D1. `Transform` carries an `Affine`, not a function **[forced]**

**Design (§5, spike):** `Transform of (Vec2<nd> -> Vec2<nd>) * Node`.

**Built:** `Transform of Affine<nd,nd> * Node`, where `Affine` is a `[<Struct>]` record of six
floats.

**Why.** A closure has reference equality. One function field anywhere in `Node` makes every
enclosing node compare unequal to an identically-constructed copy, which:

- destroys §5.2 outright — "scene graphs are immutable F# values with structural equality …
  diff against the previous one, emit only the changed subtrees". With a closure the diff is
  always "everything changed", and `Keyed` becomes decoration.
- makes §15's file format impossible — "a `Figure` saved to JSON loses its measures; only the
  witnesses survive" presumes the rest of the scene *can* be written down.

This is verified both ways in the smoke suite: two independently-built identical scenes are
structurally equal, and changing one transform's translation makes them unequal.

**Bonus that fell out.** Typing the affine as `Affine<'a,'b>` makes the linear part
`float<'b/'a>` and the translation `float<'b>`, which is dimensionally forced by the multiply.
That makes §3's claim — "the composition `data → nd → px → device` is checked end to end" —
literally true. `Affine<nd,px>` and `Affine<px,nd>` are different types and cannot be swapped.

---

## D2. §12.1's rule also applies to `Vec2`, and the design's own §3 violates it **[forced]**

**Design (§3):**

```fsharp
static member ( * ) (k: float<'k>, v: Vec2<'u>) : Vec2<'k * 'u>
```

**Design (§12.1):** measure algebra in an operator overload emits FS0064 and "silently
over-constrains the second measure parameter after the first use … it is a landmine."

These contradict each other — §3's operator is exactly the shape §12.1 forbids. §12.1 wins.

**Built:** operators on `Vec2`/`Vec3` are restricted to measure-*preserving* operations
(`+`, `-`, unary `-`, and `*`/`/` by a plain `float`). Every measure-*changing* operation is a
module-level function with explicit `[<Measure>]` type parameters, exactly as `U.mul`/`U.div`
are: `Vec2.scaleBy`, `Vec2.divBy`, `Vec2.dot`, `Vec2.cross`.

`TreatWarningsAsErrors` is on in `Directory.Build.props` specifically so FS0064 is a build
failure rather than a warning nobody reads.

**Generalisation:** any measure-generic function a caller needs to instantiate by hand must
declare its type parameters explicitly. `Affine.rotate` had to be changed from inferred to
`rotate<[<Measure>] 'a>` because `Affine.rotate<px> theta` is otherwise rejected outright
(FS0686).

---

## D3. Collections in the IR are arrays, not lists **[forced]**

**Design (§5):** `Path of PathSeg list * …`, `Group of Node list`.
**Design (§1.3):** "Per-frame allocation of records will destroy a 60 fps loop. The hot path
(scene → backend, particle stepping) must be struct/array-based, not list-based."

A 100 000-segment polyline as a cons list is 100 000 heap objects on the exact path §1.3 names.
Arrays throughout.

Same reasoning applied to `Scale.TickHint`, which returns `float<'u>[]` rather than
`float<'u> list` — not because ticks are hot, but because `Ticks` produces arrays and
converting at every axis render is pure waste.

---

## D4. A dimensioned series is struct-of-arrays, not an array of tuples **[forced]**

**Design (§2.3):** `ISeriesConsumer.Apply<'ux,'uy> : Scale<'ux> * Scale<'uy> * (float<'ux> * float<'uy>) [] -> 'r`

**Built:** `Apply<'ux,'uy> : Series<'ux,'uy> -> 'r` where `Series` holds two parallel
`float<'u>[]` arrays.

`(float * float)[]` is an array of *reference* tuples — one heap allocation per point. Same
§1.3 problem as D3, and it also contradicts §8.3's "struct-of-arrays layout,
`System.Numerics.Vector<T>` for SIMD" rule for the same data one layer down. Parallel arrays
cost nothing, vectorise, and are what the batched `Markers` node wants anyway.

The rank-2 encoding itself is unchanged and works exactly as §12.5 reports: verified with a
`PackedSeries list` holding `(m, m/s)` beside `(s, kg·m²/s²)`, recovering both measures inside
one consumer and doing measured arithmetic on them.

---

## D5. `Markers` needs a shape

**Design (§5.1):** `Markers of Vec2<nd>[] * float<px> * Paint` — position, size, paint. No shape.

**Built:** a `MarkerBatch` record with a `MarkerShape` field (7 closed cases).

**Why.** Shape is the only redundant encoding channel for colour, so §9's colour-blind-safe
requirement needs it. Without a shape field, varying the marker shape forces one `Path` node
per point — which defeats §5.1, "the single biggest performance decision in the IR", in
precisely the case that motivated batching.

A closed enum on an existing node is the cheap way to have it: no new backend dispatch, just a
shape table each backend fills in once.

**Cost, acknowledged:** payload records mean adding a *field* no longer breaks backends at
compile time the way adding a DU *case* does. The guard property in §5 is weakened for
`Path`/`Text`/`Markers`. Node count is still the thing to defend, and it is nine.

---

## D6. `Scale` carries a `ScaleKind` tag

**Design (§4):** four function fields plus a domain and a unit.

That is enough to render and nothing else. It cannot be serialized (§15 explicitly requires a
file format), cannot be inspected in a REPL, cannot be asserted on in a test, and cannot pick
its own tick strategy without asking a closure.

One extra data field fixes all four, and it is what makes `Scale.withDomain` — the zoom/pan
primitive — possible at all: zoom rebuilds the same *kind* of scale over a new domain.

---

## D7. The unit registry is an immutable value, not global mutable state

**Design (§2.2):** `val register : Dim -> symbol: string -> unit`.

That is process-global mutable state, which is the exact flaw §1.1 indicts matplotlib for
("Global mutable state (`pyplot`)"). Rendering a figure would depend on whatever a library
happened to register earlier, and two figures in one notebook could disagree.

**Built:** `UnitRegistry` as an immutable value with an `si` default; renderers take one.

---

## D8. `IBackend` needs `MeasureText`; §5's sketch does not have it **[forced]**

§9 says layout is "a *measure–arrange* pass against `IBackend.MeasureText`", but §5's
`IBackend` is `Begin`/`Draw`/`Finish`/`Caps`. §10 says to "architect `ITextShaper` as a
capability from day one".

**Built:** `ITextMeasurer` as its own interface, exposed via `IBackend.Measurer`, plus
`ApproxTextMeasurer` — a zero-dependency Helvetica advance-width table so layout works before
SkiaSharp exists at M2. `Capabilities.TextShaping = false` is the honest advertisement of what
it is. §15 warns "text and layout eat 40% of the effort"; having one seam here is what makes
the estimate survivable.

---

## D9. `Scale.Forward` does not clamp

Not stated either way in the design. Clamping would pile out-of-range data onto the panel
edge, which is a lie about the data, and it would break the `inverse (forward x) = x`
round-trip §4 asks to be proven. Out-of-domain points project outside `[0,1]` and are removed
by the panel's `Clip` node. `Scale.clamped` is opt-in.

Consequence, and it is load-bearing: **a backend that cannot clip still gets correct output
for the common case**, because the compiler culls before the backend sees anything. That is
why `Degrade.forCaps` is allowed to simply drop a `Clip` node.

---

## D10. `Capabilities` needed something that reads it

§5 promises the compiler "queries caps and *degrades* … rather than silently producing wrong
output", but nothing in the sketch does the degrading. Without an implementation
`Capabilities` is a record nobody reads.

**Built:** `Degrade.forCaps` (gradient → solid midpoint in Oklab, drop dashes, drop clips, drop
semantics, drop rasters, unrotate text) and `Degrade.splitBatches` for the GPU backend's real
instance-buffer ceiling.

---

## D11. Things deliberately NOT added to the IR

Recorded so they read as decisions rather than oversights.

- **No group-opacity node.** A real M3 need for fade-ins. `Color.A` covers the common case and
  the two differ only where a group's own shapes overlap. When M3 forces it, that is one
  variant against six backends — a costed decision, not a reflex.
- **No arcs, no quadratic curves.** Both are representable as cubics to any tolerance. Polar
  gridlines, rounded corners and circular markers all flatten one layer up.
- **No `Node3D`.** Per §5.4, the door is left open via a future `Project3D of Camera * Node3`
  that collapses to 2-D. `Vec3`/`Mat` live in Core; nothing 3-D is in `Node`.
- **`rad` and `deg` are in Core, not Quiver.Units.** Angle is dimensionless in SI (m/m), so it
  is a *tag*, not a dimension. Giving it a measure anyway kills the degrees/radians bug at
  compile time; keeping it out of `Dim` stops anyone claiming radians have a dimension.
- **Graphics measures (`px`, `nd`, `pt`, `mm`) have no `Dim`.** A `float<px>` is a device
  coordinate, not a length. Keeping them out of the SI vector stops anyone building a
  `Scale<px>` that pretends pixels are metres.

---

## D12. Stroke widths and marker sizes are in `px`, inside an otherwise `nd` IR

The IR is resolution-independent so one `Scene` renders to a 320×200 SVG and a 4K PNG. But a
hairline rule must stay a hairline at both, so `Stroke.Width` and `MarkerBatch.Size` are
`float<px>` — device units — and must *not* be scaled by the nd→px transform.

This is the one place device units legitimately appear above the backend, and it is why
`Extent<px>` has to reach the backend at all. Every backend must handle it explicitly.

---

## D13. Text rotation is a device-space, typographic property **[forced]**

`TextRun.Rotation` had no stated coordinate space, and the obvious reading is wrong.

Pushing the direction vector `(cos θ, sin θ)` through the CTM is geometrically faithful, but
the nd→px map is anisotropic on any non-square surface: a requested 30° renders as 23.4° on
800×600, and as 16.1° on 400×200. Glyphs would also shear.

**Built:** the backend takes the *frame's* rotation from the CTM (`atan2(M21, M11)`, already an
SVG rotate angle) and applies the run's own angle on top in device space, negated when
`det(CTM) < 0` because the y-flip alone mirrors.

The justification is that text is already a device-space object — `FontSpec.Size` is in `pt`,
not `nd`, for exactly the same reason `Stroke.Width` is in `px` (D12). Rotation is a sibling of
font size, not of geometry. Independently verified: 30° renders as `rotate(-30)` on a 2:1
anisotropic surface rather than 16.1°.

---

## D14. `Capabilities.full` must be genuinely full

Shipped briefly with `Semantics = false` and `TextShaping = false`. That is a trap rather than
a default: `Degrade.forCaps` strips every `Annotated` node when `Semantics` is false, so a
backend author starting from `full` silently loses all of §5.3's accessibility and hit-testing
output with no reason to suspect it.

`full` is now every field `true`. `Capabilities.vector` is the realistic starting point for an
SVG/TikZ/PDF backend — everything except text shaping, which such a backend delegates to
whatever finally renders the file.

---

## D15. The SVG backend pre-transforms geometry instead of emitting `transform`

Scene geometry is baked into device px and no `transform` attribute is emitted for it;
IR `Transform` nodes compose into the root `Affine<nd,px>`.

The alternative — nested `<g transform>` — scales stroke widths, and `vector-effect:
non-scaling-stroke` does not fix it: it is SVG 2, and it does nothing for `Dash`, `DashOffset`
or `MarkerBatch.Size`, all of which are in `px` for D12's reason. With pre-transformed
geometry, user space *is* device space and a px is a px everywhere.

The only two `transform` attributes in the output are `rotate()` on text (D13) and
`gradientTransform` — the two places something device-sized must not be scaled.

Consequence: every `<g>` in the output carries meaning (an id, a clip, or semantics), because
`Group` emits no element of its own. That keeps golden-image diffs readable.

---

## D16. `IScaleErased` carries the *whole* erased witness, not half of it **[forced]**

Originally exposed only `Symbol` and `Dim`. That is not enough to label an axis:

- an electronvolt axis and a joule axis have **identical `Dim`**, and differ by 10^19;
- a Celsius axis and a kelvin axis have identical `Dim`, and differ by 273.15;
- `Symbol` is explicitly untrusted (§12.6), so it cannot break the tie.

§15 makes this load-bearing rather than tidy: "reloading gives you `IScaleErased`, not
`Scale<'u>`". Without `ToSI`/`OffsetSI` the information is *destroyed* at save time, not merely
hidden, and a reloaded non-SI axis can never be relabelled correctly.

`ToSI <> 1.0` is also the principled test for "this unit is not SI-coherent, so do not render
its symbol from `Dim`" — which is exactly what makes an eV axis read `keV` instead of `kJ`.

---

## D17. A `[<Measure>] type min` shadows `Operators.min` — a §12-class finding

Discovered by the build failing with `FS1133: No constructors are available for the type 'min'`
on an ordinary `min a b`.

The namespace is opened more recently than `FSharp.Core`, so in application position the
*type* wins. The consequence is not local: **every file that opens `Quiver` would lose the
`min` function.** The measure is named `minute` instead.

This is the same class of bug as §12.4 (`Lerp` colliding with `System.Double.Lerp`), one level
down — collisions with `FSharp.Core`'s auto-opened `Operators`, not just with the BCL. Before
naming any measure, check it against `min`, `max`, `abs`, `sign`, `sin`, `cos`, `tan`, `exp`,
`log`, `sqrt`, `floor`, `ceil`, `round`, `truncate`, `byte`, `char`, `id`, `ref`, `not`, `fst`,
`snd`, `hash`, `compare`, `box`, `unbox`, `ignore`. `h`, `s`, `d`, `L`, `T`, `K`, `N`, `J`, `W`
are all fine; `min` is not.

---

## D18. Registry entries carry four spellings, not one

§2.2's `register : Dim -> symbol: string -> unit` stores one string. The ohm needs `Ω`, `ohm`,
`\Omega` and `\ohm`; deriving three of those from one is exactly the string surgery §12.6
forbids. Registry values are a `UnitSymbol` record of four spellings, with the LaTeX form
stored as the content *inside* `\mathrm{…}` so a prefix can be glued in front without
re-parsing.

**"Longest-match-wins" does not survive a `Dim` key.** A `Dim` is a 7-vector and the lookup is
exact equality — there is nothing to be longest about. What the phrase would buy is partial
factorisation (entropy as `J·K⁻¹` rather than `kg·m²·s⁻²·K⁻¹`); that is deliberately not
implemented, because it is ambiguous (`J/K` vs `N·m/K`), not confluent, and a wrong
factorisation is misleading rather than merely verbose.

**Four derived units cannot live in a `Dim`-keyed registry.** `lm`/`lx` are omitted because
lumen shares `J=1` with candela — registering it would relabel every candela axis. `rad`/`sr`
are omitted because their `Dim` is zero, so registering either would label *every dimensionless
axis* `rad`. `Hz` beats `Bq` and `Gy` beats `Sv` by insertion order. `withPhotometry` restores
lm/lx opt-in.

---

## D19. Prefix selection needs the witness, not just the `Dim`

§2.2 gives the signature `Dim * (float*float) -> Prefix`. Which decade puts mantissas in
[1,1000) is a property of the numbers alone; what the `Dim` decides is only (a) dimensionless →
do not rescale, and (b) the kilogram shift, since the SI base unit for mass already carries a
prefix (1e-6 kg must render `mg`, never `µkg` — verified).

But `Dim` is *insufficient* for two further cases, and both need the `U<'u>` witness:

- **Affine units.** Celsius has `Dim.temperature`, identical to kelvin, so the refusal to
  prefix (a "milli-degree-Celsius" is meaningless) must key on `IsAffine`.
- **Non-coherent units.** `ToSI <> 1.0` means rendering the symbol from `Dim` would name the
  wrong unit — see D16.

**Refusing to rescale unprefixable units would violate §4's injectivity.** An `m²` axis over
0…5e-18 renders six ticks that *all* read `0.000000000000000`, because `Ticks.decimalsFor`
clamps at 15 places. So rescaling always happens, and the renderer emits `10⁻¹⁸·m²` when a
prefix cannot be glued on — `nm²` would mean `(nm)²`, a factor of 10⁹ out.

---

## D20. Derived units are measure *abbreviations*; non-SI units are *opaque*

Opposite choices, deliberately.

`[<Measure>] type N = kg m / s^2` means a composed `U<kg*m/s^2>` unifies with `U<N>` — verified,
and asserted permanently in `Derived.fs` rather than in a throwaway test, so making `N` opaque
breaks the build. The price is that `Hz`/`Bq`, `Gy`/`Sv` and `J`/`N·m` collapse to the same
type. The third collapse is the point; the first two are why the registry needs an insertion
order.

`[<Measure>] type eV = J` would be actively harmful: it makes `float<eV>` and `float<J>` the
same type, so a joule could be silently added to an electronvolt and the 1.602×10⁻¹⁹ would
never be applied. Non-SI units are opaque and cross over only through `U.convert`.

---

## D21. `Quiver.Plot` takes no backend reference, so `Chart.toSvg` takes a renderer

§0's diagram puts the DSLs above the IR and the backends below it. A grammar package that
references `Quiver.Backend.Svg` inverts that, and it drags an SVG writer into a GPU-only
application.

**Built:** `Chart.toSvg : (Extent<px> -> Node -> string) -> path -> extent -> Figure -> unit`.
`SvgBackend.render` has exactly that shape, so the user still writes one line —
`fig |> Chart.toSvg SvgBackend.render "plot.svg" (Chart.size 800.0 500.0)` — and the package
graph stays a DAG in the direction the design draws it.

`Chart.renderTo : IBackend -> ...` is the other door and the more complete one: it measures
against the backend's *own* `ITextMeasurer` (§9's "measure–arrange pass against
`IBackend.MeasureText`") and runs `Degrade.forCaps`/`Degrade.splitBatches` before drawing.
Verified byte-identical to the direct path on the SVG backend, which is the statement that
degradation is a no-op for a backend that can do everything.

---

## D22. Layout works in y-down device px and converts to `nd` in exactly one function

D12 says stroke widths and font sizes are `px` while positions are `nd`. Layout is the only
layer holding both, so it is the only layer allowed to convert, and converting needs the
figure `Extent<px>`.

**Convention, stated once:** `Layout` solves in **device px, origin top-left, y down** —
because that is the space text metrics arrive in and the order rows are naturally numbered in
— and `Layout.ndRect` / `Layout.ndPoint` are the *only* places the y axis flips. That mirrors
the SVG backend, which also keeps its flip in one place (D15).

The conversion is **anisotropic**: `ndX` divides by the width, `ndY` by the height, and there
is deliberately no scalar `nd` conversion that does not say which axis it means. A 3.5 px tick
mark is a different `nd` length on the bottom axis and the left axis of a 900×400 figure, and
collapsing that to one number is how tick marks end up visibly different lengths per axis.

**Consequence:** a `Theme` contains no `nd` at all. Every spacing in it is `px` or `pt`, and
resolving one into the IR requires a figure size — which is the honest statement of what a
resolution-independent IR costs.

---

## D23. Tick-label collision avoidance is a fixed ladder, and it reports which rung fired

§15: "Axis label collision avoidance, tick label formatting, legend layout, and font metrics
are where plotting libraries actually die."

**Built:** labels are measured, placed at their real positions, and checked pairwise against a
minimum gap. On collision a fixed ladder is walked and the first rung that fits wins:

1. every label, upright;
2. **every second** label, upright;
3. every label, **rotated 45°** — horizontal axes only, because rotating a left-axis label
   makes it *taller*, which is the wrong direction;
4. every 2nd…6th, rotated (horizontal) or upright (vertical);
5. first and last only.

Thinning beats rotation at stride 2 and rotation beats thinning past that: dropping more than
half the labels loses more than tilting them costs. Ticks are never moved and never
relabelled — `Quiver.Units` already made the labels injective (§4), and a thinned axis is
still a correct axis.

The winning rung is recorded in `TickPlan.Rule` and emitted as `data-tick-rule` on the axis
group, so the decision is inspectable from the output rather than being a mystery in it.

**This forces a second layout iteration.** The plan depends on the plot width; the plot width
depends on how much room the labels need. So each panel is arranged twice: once against an
estimated plot size, then re-planned and arranged against the real one. A third pass is
provably unnecessary, because the plan only ever *coarsens* the labels and a coarser plan never
needs more room.

---

## D24. The default axis quantity name is derived from the `Dim`

§9's payoff sentence is "`x (fun r -> r.Time)` where `Time : float<s>` gives you … the axis
labelled `t [s]`, **with no configuration**". In the grammar the `t` comes from the accessor
name. `Chart.line (ts, xs)` has no accessor and no field name.

**Built:** `Axes.defaultLabel`, a table from the seven-exponent vector to the conventional
physics symbol — time → `t`, length → `x`, energy → `E`, force → `F`, pressure → `p`, and so
on, falling back to the axis's own `x`/`y` for anything unlisted.

It is exactly as unit-derived as the bracketed half, and it is the only route by which
`Chart.line U.second U.metre ts xs` can print `t [s]` and `x [m]` with nothing configured.
Verified: those two strings appear literally in the rendered SVG.

---

## D25. Geoms consume `PackedSeries`; the typed constructors spend the measures first

A geom needs positions, and positions are `Vec2<nd>` with the measures already gone. Making
geoms measure-generic would push two type parameters through `Layer`, `Panel` and `Figure`, and
a figure with a metres panel beside a joules panel could then never be one value — which is the
exact problem §2.3's rank-2 encoding exists to solve.

So geoms take the erased side, and everything that *needs* a measure is computed by a typed
constructor while the `Series<'ux,'uy>` is still in hand: `Geom.errorY` takes `float<'uy>[]`
against a y axis in `'uy`, so an uncertainty in the wrong unit is a compile error; `Geom.area`
projects the baseline through the y scale, so "filled to zero" means zero and not "the bottom
of the panel".

**One runtime check remains, and it cannot be a compile-time one.** Two layers added
separately to one panel have both crossed the existential, so F# can no longer compare their
measures. `Figure.unify` compares their `Dim` values instead and raises with both dimensions
named. Within a single call the compiler still rejects it outright — `Chart.line` names both
measures — so the design's claim holds where the user actually writes the mistake.

---

## D26. Themes and colour ramps are data, not closures

A `Ramp` is `{ Name; Stops: (float * Color)[] }` with `Ramp.at` as the evaluator, not a
`float -> Color` field. A function field in `Theme` would make every theme compare unequal to
an identically-constructed copy — the same reference-equality trap D1 keeps out of `Node`, one
layer up, and it would break the "same `Figure` compiles to an equal `Scene`" property the
smoke suite asserts.

`GridCell.Measure` *is* a function, deliberately: a cell's demand is "how wide is this string in
this backend's font", which is precisely the question `ITextMeasurer` answers and layout cannot.
Layout values never enter the IR and are never serialized, so it costs nothing there.

---

## D27. `Palette.SeparationFloor` is calibrated to Okabe–Ito, because nothing else is calibrated

There is no standard for "how far apart two categorical colours must be". Picking a round
number and having the literature's own endorsed palette fail it is worse than useless, so the
floor is set to what Okabe–Ito actually achieves. Measured, in Oklab, on the eight canonical
colours:

| vision | minimum separation | closest pair |
|---|---|---|
| normal | 0.156 | orange / vermillion |
| protanopia | 0.090 | blue / reddish purple |
| deuteranopia | 0.079 | bluish green / reddish purple |
| tritanopia | **0.068** | bluish green / blue |

The tritanopia figure binds, and it is not a measurement artefact: Okabe–Ito was optimised for
the two common dichromacies. Recording the number is more useful than asserting the set is
uniformly safe.

**Separately measured and reported rather than fixed:** Okabe–Ito's yellow is 1.3:1 against
white and its sky blue is 2.0:1, both under WCAG's 3:1 non-text minimum. Reordering puts them
last in the series cycle; altering the colours would make it no longer Okabe–Ito, which is a
worse trade. `Palette.validate` reports it; `Theme.validate` does not fail on it.

The jet counter-example named in §9 is machine-checked: `Palette.isLightnessMonotone` passes
viridis, cividis and magma and rejects jet, whose Oklab lightness runs 0.270 → 0.964
non-monotonically.

---

## F1. Finding: the Oklab round-trip floor is ~1e-6, and it is not reducible

Not a design question, but it sets golden-image tolerances, so it is recorded.

Ottosson's forward and inverse LMS matrices are published to 10 decimal places, so their
product deviates from the identity by ~2.4e-10, and sRGB white does not map to exactly
`(L=1, a=0, b=0)` — its Oklab `b` is 3.7e-8. The inverse matrix rows nearly cancel for neutral
colours (4.077 − 3.308 + 0.231 = 1) and `ofLinear`'s curve has a slope of ~1500 near black,
which amplifies the residual to **9.4e-7** in sRGB. A black→white Oklab ramp is therefore very
slightly non-neutral.

That is 4000× finer than the 8-bit display quantum of 1/255 = 3.9e-3, so it is invisible.
**A colour test demanding better than 1e-5 will flap forever.**

Also measured: `x ** (1.0/3.0)` is accurate to 6.7e-16 and a Newton refinement step on top
makes it *worse*. The naive spelling is correct; `System.Math.Cbrt` is not needed and would
cost Fable compatibility.

---

## F2. Finding: `Quiver.Core` keeps the sRGB transfer function private

`Color.toLinear`/`ofLinear` are `private` in `Color.fs`. The Brettel/Viénot CVD transforms are
defined on **linear** light — applying them to gamma-encoded values, as a good many published
implementations do, makes the simulated colours too dark and the measured separations too
optimistic — so `Palette` has its own copy of the two four-line functions.

Not a bug and not worth breaking Core's API over at M0, but it is duplication in the one place
duplication is dangerous, because a change to one copy would silently disagree with the other.
If a second consumer ever appears, promote them.

---

## Verification status

`Quiver.Core` builds warning-free on `netstandard2.1` with zero package references, and 112
runtime checks pass, covering: `Dim` group laws; witness algebra across five instantiations;
`Affine` dimensional composition and inversion; `inverse (forward x) = x`, monotonicity and
endpoint mapping for all seven scale kinds; §4's three tick properties over 2000 random
domains; Oklab/Oklch round-trips; the rank-2 heterogeneous-series recovery; generic `interp`
over five types including the unitless case; scene structural equality both ways; capability
degradation; text metrics; polar/cartesian coords; and §8.1's "gradient of a potential is a
force, with the unit inferred rather than annotated".

`Quiver.Plot` builds warning-free on `netstandard2.1` with zero package references and no
backend reference, and 66 runtime checks pass against real rendered output — every scene is
rendered to SVG, parsed back with `XDocument.Parse`, and looked at. Covering:

- **The headline claim.** `float<s>` against `float<m>` puts the literal strings `t [s]` and
  `x [m]` in the document with nothing configured.
- **§2.2's worked example.** Data over 1e-9…5e-9 m gives the title `x [nm]` and the tick
  labels `1, 2, 3, 4, 5`.
- **§5.1.** A 50 000-point scatter is **one** `Markers` node: `Scene.count` = 49 for the whole
  figure against `Scene.primitiveCount` = 50 024. 2.9 ms to compile, 18.4 ms to render,
  2 992 404 bytes.
- **§5.5.** The IR cases used across every figure are `Path`, `Text`, `Markers`, `Group`,
  `Transform`, `Clip`, `Keyed`, `Annotated`. No node was added and none was needed.
- **§13's layout invariants.** No overlapping panels or grid cells; every text run's measured
  device bounding box inside the figure, in all four themes and on a 300×260 surface.
- **The affine chain.** A point at 2.5 s of a [0,10] s domain lands at 0.250000000000000 `nd`,
  and the coordinate the SVG backend emits matches the independently computed
  `data → nd → nd → px` composition to the writer's three-decimal rounding.
- Purity of every `with*` combinator, and `toScene` determinism under structural equality.
- Log scales; polar through `Coord` with no polar-aware geom; four themes with measured
  contrast (dark: text 14.5:1, spine 6.2:1); error bars; a three-entry legend; multi-panel
  layout; and siunitx titles reaching the IR.
