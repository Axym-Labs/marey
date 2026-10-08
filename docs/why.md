# Why Marey is like this

Most plotting libraries take an axis name as a string. Marey takes a *unit witness*, and almost
everything else in the library follows from that one decision. This page states the argument for
it, and for the other decisions of the same kind, including what each one costs.

---

## `U.second` against `"s"`

Free fall in matplotlib, carefully written:

```python
import numpy as np
import matplotlib.pyplot as plt

t = np.arange(24) * 0.4
x = 0.5 * 9.81 * t**2

fig, ax = plt.subplots()
ax.plot(t, x)
ax.set_xlabel("t [s]")
ax.set_ylabel("x [m]")
```

And in Marey:

```fsharp
let t = Array.init 24 (fun i -> float i * 0.4<s>)
let x = t |> Array.map (fun t -> 0.5 * 9.81<m/s^2> * t * t)

Chart.line U.second U.metre t x
```

The two figures look the same. They are not the same object.

In the Python, `0.4` is a bare float, `9.81` is a bare float, and `"s"` is a string. Nothing in the
program connects the three: `"s"` is a caption placed next to some numbers rather than a statement
about them.

`U.second` is a value that knows it is the time dimension — `T = 1`, factor 1.0 to SI, no affine
offset — and the array it labels has type `float<s>`, which the compiler checks. Six things follow
from that, and none of them are available to a string.

### 1. The label can be wrong, and nothing notices

Swapping the arguments in the Python still draws; the axes keep asserting whatever was typed.
Changing the data to milliseconds later does not change `"s"` with it. The label and the numbers
drift apart silently, because they were never attached.

```fsharp
// error FS0001: Type mismatch. Expecting a 'float<m> []' but given a 'float<s> []'
Chart.line U.second U.metre t t
```

That is a build failure, before anything is drawn. Note what is *not* annotated in the working
version: `x` is never declared to be a length. `0.5 * 9.81<m/s^2> * t * t` has type `float<m>`
because the compiler multiplies the measures out. The label is derived from the physics rather than
asserted alongside it.

### 2. A string cannot rescale

Given data spanning 1e-9 to 5e-9, matplotlib factors the power of ten out of the ticks and prints a
small `1e-9` at the corner of the axis. The label still reads `x [m]`, leaving the reader two
numbers to multiply.

Marey has the axis unit, so the factor goes into the unit: ticks read `1, 2, 3, 4, 5` and the title
reads `x [nm]`. The same information, one step fewer for the reader, and no configuration, because
the prefix is *selected from the data range against the witness*. A string has nothing to select
against.

The prefix is therefore carried at the axis rather than folded into the unit: the quantity is still
metres, and `nm` is a presentation decision made at the edge of the figure by
[`Prefix`](../src/Marey.Units/Prefix.fs). The same machinery prints `mg` rather than `µkg` when a
mass is rescaled by 10⁻⁶, the kilogram being the one SI base unit whose name already carries a
prefix.

### 3. A string has one spelling; a figure needs four

`"s"` is `s` everywhere, which suffices until the figure goes into a LaTeX document. The witness
renders in four typographies from the same dimension vector. The difference is invisible for
seconds, so an acceleration shows it:

| Style | `m/s²` |
|---|---|
| `Unicode` | `m·s⁻²` |
| `Ascii` | `m.s^-2` |
| `Latex` | `\mathrm{m}\,\mathrm{s}^{-2}` |
| `Siunitx` | `\qty{}{\metre\per\second\squared}` |

The siunitx row is the one that matters in practice. In `tikzpicture` output the axis titles are set
by `siunitx`, typeset by the same engine as the surrounding document, in the document's fonts and
under the document's unit conventions — part of the paper rather than a foreign image pasted into
it. A string cannot reach that, because the structure needed to re-spell it is gone by the time a
string exists.

### 4. Strings do not compose; dimensions do

Composing two witnesses gets the *dimension* right while the naive symbol comes out as `kg·m/s/s·m`
for what is plainly `J`, which is why an axis is never labelled from a composed symbol. The unit
registry is keyed on the seven-element SI dimension vector, so anything that composes to a newton
prints `N`, whether it was written as `Derived.newton` or built out of kilograms and metres per
second squared.

Where the dimension has no registered name, the factorisation *as supplied* is used instead:
`U.div Derived.volt U.metre` renders `V·m⁻¹`, and `MV·m⁻¹` once prefixed, rather than the
technically correct and unreadable `kg·m·s⁻³·A⁻¹`.

### 5. A string cannot object to its neighbours

Two series can each be well typed and still be wrong to put on one axis. A `float<K>` temperature on
the same y axis as a `float<m>` displacement produces:

> `Marey: displacement has a y axis in m but temperature has one in K. Two physical dimensions
> cannot share an axis.`

This is the one error a type system cannot catch, because it does not exist until the two are
placed together, so it is caught when the figure is compiled, with both layers named. matplotlib
draws it.

### 6. A string is not there when the figure is reloaded

The witness survives erasure. A saved figure still records that an electronvolt axis is in
electronvolts and not joules — the same dimension, a factor of 1.602×10⁻¹⁹ apart — so a reloaded eV
axis prefixes to `keV` rather than `kJ`. Strings round-trip; the meaning behind them does not.

---

## The fair comparison: `pint` and `astropy.units`

The section above is not an argument that Python cannot do units. `astropy.units` and `pint` are
good libraries, and `astropy`'s `quantity_support` labels matplotlib axes from real quantities.

The difference is where the check happens and what it costs.

| | Marey | `pint` / `astropy.units` |
|---|---|---|
| When a mismatch is caught | Compile time, mostly | Runtime, when that line executes |
| What a value is at runtime | A `float`. Measures are erased. | An object wrapping a magnitude and a unit |
| Cost of the unit system | One witness per axis | One unit per value, and per arithmetic operation |
| Coverage | Whatever the compiler can prove | Whatever the tests exercise |

F# units of measure are a compile-time construct: `float<m>` *is* a `float` in the emitted IL, and an
array of a million samples costs exactly what an array of a million floats costs, which is what
makes unit correctness affordable at any data size. The runtime cost of the entire unit system is
the two `U<'u>` values passed to `Chart.line`.

Where a unit is known only at runtime — a column header in a file written elsewhere — it has to be
asserted, and an assertion is not checked by the compiler. There is no UDUNITS-style parser;
`pint` covers that case and Marey does not.

Part of it is covered from the .NET side. FSharp.Data's CSV type provider reads units out of column
headers, and Marey's seven base measures are FSharp.Core's, so a column headed `Time (s)` arrives
already carrying its unit:

```fsharp
type Fall = CsvProvider<"fall.csv">          // header: Time (s), Distance (m)
let t : decimal<s>[] = rows |> Array.map (fun r -> r.Time)
```

The limits apply before that can be relied on. The provider's sample is resolved at build time, so
this covers a schema known at compile time rather than one first seen at runtime; a loaded file
that disagrees with the sample is detected when the row is read. Only bare SI symbols are matched,
so `Pressure (kPa)`, `Field (V/m)` and `Temp (degC)` are each unrecognised and arrive as a plain
`decimal`, with no measure and no warning.

Within those limits, nothing is asserted again past the point where the data enters: the check is
the compiler's.

---

## The other decisions

Each of these could have gone the other way. What each buys, and what it costs:

**Derived units are measure abbreviations, not fresh measures.** `N = kg m / s^2` rather than an
opaque `N`. This is what lets the witness algebra actually derive things: compose a mass with an
acceleration and the result unifies with newtons. The cost is that SI distinctions F# cannot see
collapse — `Hz` and `Bq` are both `s⁻¹`, `Gy` and `Sv` are both `J/kg` — and those display
collisions are resolved by registry ordering rather than by intent.

**Non-SI units are opaque, for the opposite reason.** `eV` is *not* an abbreviation for `J`: if it
were, a joule could be silently added to an electronvolt and the 1.602×10⁻¹⁹ would never be
applied. Coherent units should unify; converted units must not. The cost is an explicit conversion
at the boundary.

**Nothing is inferred from a bare dimension.** A supplied factorisation is read back and a
registered name is looked up, but a dimension handed over on its own is never factorised, since the
dimension of `J/K` is also the dimension of `N·m/K`. The cost is verbosity: `U.div Derived.volt
U.metre` rather than something shorter.

**Affine units carry an offset from day one.** °C is not a scaled kelvin, it is a shifted one, so
`U<'u>` has had an `OffsetSI` field and an `IsAffine` flag since before anything needed them.
Products, quotients and prefixes are refused on affine units rather than silently computed against
absolute zero. Adding that field later would have been a breaking change to every witness in the
library.

**Dimensionless is an answer, not a failure.** A CO₂ mole fraction in ppm is micromoles per mole;
the moles cancel and there is nothing for the compiler to check. Such an axis is drawn with no unit
brackets rather than `[1]`, carries no prefix, and takes its label from the caller.
`Chart.Plain.line` exists for this data, and is deliberately neither the default spelling nor an
overload, so that data which *does* have a dimension still has to state it.

**A figure is a value, and backends are separate packages.** `Marey.Plot` has no backend reference;
rendering is a function passed in at render time. That is why the same figure becomes SVG or
`tikzpicture` source by swapping a theme, and why the SVG is byte-identical across renders — output
that diffs cleanly is output that can be reviewed.

**The documentation executes.** Every `fsharp` block in [the guide](guide.md) is extracted,
concatenated and run under `dotnet fsi` by the test suite, and every script in
[`examples/`](../examples/) is run on its own, so neither can drift from the library.

*(The blocks on this page are the exception. They are illustrative, and mirror code that is tested
in the guide, the README and the examples.)*

---

## When not to use Marey

- **The data is not physical.** Counts, currencies, probabilities, mole fractions, indices — the
  unit system has nothing to offer here, and `Chart.Plain` is a thin veneer over a normal plotting
  library. The library still works; it is simply not *for* this.
- **The units are known only at runtime.** There is no UDUNITS-2 parser. The witness carries the
  fields such a parser would need, but the parser does not exist.
- **Interactivity outside a notebook, or 3D, is needed.** Thirteen geoms and three backends. A
  figure can be animated, rendered frame by frame and encoded to video, and in a Verso notebook it
  can carry controls, zoom and a readout. But there is no interactive canvas outside a notebook, no
  datum can be picked, and nothing is projected. The
  [guide's closing section](guide.md#what-is-not-here-yet) is the current list of what does not
  exist.
- **The host is not .NET.** The argument above rests on F# units of measure, and there is no version
  of this library for a language without them.

What remains is the case the library is built for: a measured physical quantity plotted against
another measured physical quantity, in a figure destined for a document.
