# Marey — implementation decision log

Deviations from `docs/design.md`, and the implicit consequences the design document implies
but does not spell out. Each entry says what the design says, what was actually built, and
why. Entries marked **[forced]** are cases where following the sketch literally would break
something else the same document requires.

Status: **M0 and M1 complete.** Whole solution builds warning-free across seven packages;
`dotnet test` is green at **485 tests** — FsCheck property suites, §12 regression pins, 29 golden
baselines in SVG *and* `tikzpicture` source, a real `pdflatex` compile gate, an ink gate that
rasterises the compiled page, the Verso formatter driven across an isolated collectible
`AssemblyLoadContext`, and every example in `docs/guide.md` executed under `dotnet fsi`.

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
- **`rad` and `deg` are in Core, not Marey.Units.** Angle is dimensionless in SI (m/m), so it
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
*type* wins. The consequence is not local: **every file that opens `Marey` would lose the
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

## D21. `Marey.Plot` takes no backend reference, so `Chart.toSvg` takes a renderer

§0's diagram puts the DSLs above the IR and the backends below it. A grammar package that
references `Marey.Backend.Svg` inverts that, and it drags an SVG writer into a GPU-only
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
relabelled — `Marey.Units` already made the labels injective (§4), and a thinned axis is
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

## M1 — the TikZ backend and the notebook

D28 onward were taken during M1 and are recorded here **as built and verified**, not as
proposals. `docs/m1-plan.md` drafted them; where the implementation disagreed with the draft the
entry says so in place, because a decision log that quietly reflects only the final answer loses
the thing it exists to record. Four were not planned at all — D36 and D38 were found by a TeX
engine and by reading a baseline, D37 by discovering that D35 described something a `Theme` could
not do, and D39 was rewritten outright when its host was retired mid-milestone.

They are in numeric order here. The plan grouped D37 with D35, which it amends; the log is
numerically ordered throughout and the cross-reference in D37 carries the connection.

## D28. TikZ coordinates are emitted in `bp`, never `pt` **[forced]**

`Space.fs` defines Core's `pt` as **1/72 inch** — the PostScript/big point. TeX's `pt` is
**1/72.27 inch**. Emitting a Core `pt` value into a `.tex` file as `pt` is a silent 0.37 %
error in every coordinate, font size and stroke width, and it is invisible in a golden diff
because the numbers are all self-consistently wrong.

`bp` is exactly 1/72 inch and TeX accepts it as a dimension everywhere `pt` is accepted. All
geometry is emitted in `bp`. Font sizes stay in `pt` inside `\fontsize{}{}` — which is TeX's
`pt`, so a 9.6 pt font is `\fontsize{9.6}{...}` and is *not* converted, because `FontSpec.Size`
is a typographic request being handed to a typesetter rather than a length being drawn.

This is a §12-class landmine: it compiles, it looks right, and it is wrong by 0.37 %.

## D29. TikZ does not flip y; SVG does **[forced]**

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

## D30. Geometry is pre-transformed; no picture-level `scale` is emitted

Same decision as D15, for a stronger reason. TikZ's `scale=` does **not** scale `line width`,
`dash pattern` or node font sizes by default, while it does scale coordinates. Combining a
picture scale with D12's px-denominated stroke widths therefore produces widths that are correct
only by accident, and marker sizes that are correct only at scale 1.

Coordinates are baked into device `bp`. A bp is a bp everywhere in the output, the emitted
numbers are checkable against a ruler, and — per D29 — they are the SVG backend's numbers times
`72/96`, with y measured from the bottom instead of the top. That relationship is exactly what
makes the cross-backend agreement test in §4 possible.

## D31. TikZ capabilities at M1, and `Degrade`'s first real customer

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

## D32. `TextRun.Tex : string option` — how TeX source crosses the IR

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

## D33. Markers: one `\providecommand` per distinct (shape, size), then one call per point

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

## D34. Deterministic number formatting is promoted to `Marey.Core` **[forced]**

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

## D35. `Theme.tikz` pins the font *size* and inherits the font *family*

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

## D36. TeX cannot represent an arbitrary coordinate, and D9 guarantees we will hand it one

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

## D37. The unit style belongs to the theme, and the axis config overrides it

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

## D38. Three characters TeX accepts and then typesets as something else

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

## D39. The notebook renderer knows nothing about the notebook **[superseded in part]**

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

## D40. The Verso extension does not reference Marey

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

## F2. Finding: `Marey.Core` keeps the sRGB transfer function private

`Color.toLinear`/`ofLinear` are `private` in `Color.fs`. The Brettel/Viénot CVD transforms are
defined on **linear** light — applying them to gamma-encoded values, as a good many published
implementations do, makes the simulated colours too dark and the measured separations too
optimistic — so `Palette` has its own copy of the two four-line functions.

Not a bug and not worth breaking Core's API over at M0, but it is duplication in the one place
duplication is dangerous, because a change to one copy would silently disagree with the other.
If a second consumer ever appears, promote them.

---

## Verification status — M0

`Marey.Core` builds warning-free on `netstandard2.1` with zero package references, and 112
runtime checks pass, covering: `Dim` group laws; witness algebra across five instantiations;
`Affine` dimensional composition and inversion; `inverse (forward x) = x`, monotonicity and
endpoint mapping for all seven scale kinds; §4's three tick properties over 2000 random
domains; Oklab/Oklch round-trips; the rank-2 heterogeneous-series recovery; generic `interp`
over five types including the unitless case; scene structural equality both ways; capability
degradation; text metrics; polar/cartesian coords; and §8.1's "gradient of a potential is a
force, with the unit inferred rather than annotated".

`Marey.Plot` builds warning-free on `netstandard2.1` with zero package references and no
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
  contrast (dark: text 14.5:1, spine 6.2:1); error bars; a three-entry legend; and multi-panel
  layout.

**One M0 claim above was overstated and is corrected here rather than deleted.** This list
originally ended "and siunitx titles reaching the IR". That was literally true and materially
misleading. `Chart.withUnitStyle Siunitx` did put `\qty{}{\metre}` into the IR — into
`TextRun.Text`, the field D32 later designated the *plain-Unicode fallback*, the one
`ITextMeasurer` measures and every non-typesetting backend renders verbatim. So at M0 the markup
reached the IR in the wrong field, was measured as though `\qty{}{\metre}` were the label's
literal glyphs, and no backend existed that could typeset it. `TextRun.Tex` and the TikZ backend
both postdate the commit that wrote the claim.

The correct statement of what M0 had is: *the units layer could render siunitx markup, and a
figure could carry it as text.* What made it true in the sense a reader would assume is D32,
D35 and D37, in M1.

---

## Verification status — M1

Seven packages build warning-free; **485 tests**, zero warnings, `TreatWarningsAsErrors` on.

What M1 added to the checks above:

- **The headline claim, in TeX.** A `float<s>`-against-`float<m/s^2>` figure under `Theme.tikz`
  and nothing else configured puts a literal `\qty{}{\metre\per\second\squared}` in the
  `.tex`, and the file compiles.
- **A real engine reads every baseline.** `pdflatex -interaction=nonstopmode` over each golden
  `.tex`, asserting zero errors, with a deliberately-broken document in the suite proving the
  gate can fail — it was vacuous for about ten minutes once, and now detects failure from two
  independent signals. It found D36 on its first run.
- **Ink.** The compiled PDF is rasterised and checked to contain something, because D33's
  first implementation produced a perfectly valid document with no scatter markers in it, and
  every other test passed while that was true.
- **Cross-backend geometry agreement.** Every emitted TikZ `bp` coordinate equals the SVG
  backend's `px` times 72/96 with y from the opposite edge, to three decimals. D29 and D30 make
  this exact, so it is an oracle rather than a tautology — and it is the only check that catches
  a sign regression in either backend's independently-derived rotation arithmetic.
- **The Verso formatter across an `AssemblyLoadContext` boundary**, reproduced with a real
  collectible context rather than trusted, because D40's assumption fails *silently*: a
  `CanFormat` answering `false` is indistinguishable from "nothing claimed it".
- **Every example in `docs/guide.md`**, extracted and executed under `dotnet fsi`.
- **Every shipped theme through `Theme.validate`**, which caught `Theme.modern`'s first draft
  at 1.60:1 on the spine.

Verified by hand, because no test can reach it: on 2026-08-18, in a live Verso host, a bare
`Figure` displays as a figure, sized to the cell, with no `Chart.render` and no explicit SVG
backend.
