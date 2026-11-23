# Why Marey is like this

Most plotting libraries take an axis name as a string. Marey takes a *unit witness*, and almost
everything else in the library follows from that one decision. This page makes the case for it and
for the other decisions of the same kind, with what each one costs.

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

Chart.lineIn U.second U.metre t x
```

The two figures look the same, but the programs behind them know different things.

In the Python, `0.4` and `9.81` are bare floats and `"s"` is a string. Nothing in the program
connects the three, so `"s"` is a caption placed next to some numbers and says nothing about them.

`U.second` is a value that knows it is the time dimension (`T = 1`, factor 1.0 to SI, no affine
offset), and the array it labels has type `float<s>`, which the compiler checks. Six things follow
from that, and a string can do none of them.

### 1. The label can be wrong, and nothing notices

Swapping the arguments in the Python still draws, and the axes keep saying whatever was typed. If
the data later changes to milliseconds, `"s"` stays as it was. The label and the numbers drift apart
silently, because they were never attached.

```fsharp
// error FS0001: Type mismatch. Expecting a 'float<m> []' but given a 'float<s> []'
Chart.lineIn U.second U.metre t t
```

The build fails before anything is drawn. In the working version, `x` is never declared to be a
length. `0.5 * 9.81<m/s^2> * t * t` has type `float<m>` because the compiler multiplies the
measures out, so the label comes from the physics instead of being typed beside it.

### 2. A string cannot rescale

Given data spanning 1e-9 to 5e-9, matplotlib factors the power of ten out of the ticks and prints a
small `1e-9` at the corner of the axis. The label still reads `x [m]`, and the reader has two
numbers to multiply.

Marey has the axis unit, so the factor goes into the unit. The ticks read `1, 2, 3, 4, 5` and the
title reads `x [nm]`. The reader gets the same information with one step fewer, and nothing was
configured, because the prefix is *selected from the data range against the witness*. A string has
nothing to select against.

The prefix lives on the axis and is not folded into the unit. The quantity is still in metres, and
`nm` is a choice of presentation that [`Prefix`](../src/Marey.Units/Prefix.fs) makes at the edge of
the figure. The same code prints `mg` rather than `µkg` when a mass is rescaled by 10⁻⁶, since the
kilogram is the one SI base unit whose name already carries a prefix.

### 3. A string has one spelling; a figure needs four

`"s"` is `s` everywhere, which is enough until the figure goes into a LaTeX document. The witness
renders in four typographies from the same dimension vector. Seconds look alike in all four, so the
table shows an acceleration:

| Style | `m/s²` |
|---|---|
| `Unicode` | `m·s⁻²` |
| `Ascii` | `m.s^-2` |
| `Latex` | `\mathrm{m}\,\mathrm{s}^{-2}` |
| `Siunitx` | `\qty{}{\metre\per\second\squared}` |

In practice the siunitx row matters most. In `tikzpicture` output the axis titles are set by
`siunitx` and typeset by the same engine as the surrounding document, in its fonts and under its
unit conventions. The figure becomes part of the paper instead of an image pasted into it. A string
cannot get there, because by the time a string exists, the structure needed to spell it another way
is gone.

### 4. Strings do not compose; dimensions do

Composing two witnesses gets the *dimension* right, but the naive symbol comes out as `kg·m/s/s·m`
for what is plainly `J`. So an axis is never labelled from a composed symbol. The unit registry is
keyed on the seven-element SI dimension vector, and anything that composes to a newton prints `N`,
whether it was written as `U.newton` or built out of kilograms and metres per second squared.

Where the dimension has no registered name, Marey uses the factorisation *as supplied*.
`U.div U.volt U.metre` renders `V·m⁻¹`, and `MV·m⁻¹` once prefixed, rather than the technically
correct and unreadable `kg·m·s⁻³·A⁻¹`.

### 5. A string cannot object to its neighbours

Two series can each be well typed and still be wrong to put on one axis. A `float<K>` temperature on
the same y axis as a `float<m>` displacement produces:

> `Marey: displacement has a y axis in m but temperature has one in K. Two physical dimensions
> cannot share an axis.`

A type system cannot catch this error, because it does not exist until the two are placed
together. Marey catches it when the figure is compiled and names both layers. matplotlib draws it.

### 6. A string is not there when the figure is reloaded

The witness survives erasure. A saved figure still records that an electronvolt axis is in
electronvolts and not joules (the same dimension, a factor of 1.602×10⁻¹⁹ apart), so a reloaded eV
axis prefixes to `keV` rather than `kJ`. A string survives a reload too, but only as text.

---

## The fair comparison: `pint` and `astropy.units`

Python can do units. `astropy.units` and `pint` are good libraries, and `astropy`'s
`quantity_support` labels matplotlib axes from real quantities. They differ from Marey in where the
check happens and what it costs.

| | Marey | `pint` / `astropy.units` |
|---|---|---|
| When a mismatch is caught | Compile time, mostly | Runtime, when that line executes |
| What a value is at runtime | A `float`. Measures are erased. | An object wrapping a magnitude and a unit |
| Cost of the unit system | One witness per axis | One unit per value, and per arithmetic operation |
| Coverage | Whatever the compiler can prove | Whatever the tests exercise |

F# units of measure exist only at compile time. `float<m>` *is* a `float` in the emitted IL, and an
array of a million samples costs exactly what an array of a million floats costs, so correct units
stay affordable at any data size. At run time the whole unit system costs the two `U<'u>` values
passed to `Chart.line`.

A unit known only at run time, such as a column header in a file written elsewhere, has to be
asserted, and the compiler does not check an assertion. `U.parse` reads it in UDUNITS-2's syntax,
and `U.tryAs` asserts a measure onto the result and checks it against the dimension at run time.
`pint` checks every operation at run time; Marey checks that one.

The .NET side covers part of this. FSharp.Data's CSV type provider reads units out of column
headers, and Marey's seven base measures are FSharp.Core's, so a column headed `Time (s)` arrives
already carrying its unit:

```fsharp
type Fall = CsvProvider<"fall.csv">          // header: Time (s), Distance (m)
let t : decimal<s>[] = rows |> Array.map (fun r -> r.Time)
```

That has limits. The provider resolves its sample at build time, so it covers a schema known at
compile time and not one first seen at run time, and a loaded file that disagrees with the sample is
caught when the row is read. It matches only bare SI symbols, so `Pressure (kPa)`, `Field (V/m)` and
`Temp (degC)` go unrecognised and arrive as a plain `decimal`, with no measure and no warning.

Within those limits, nothing is asserted again once the data is in, and the compiler does the
checking.

---

## The other decisions

Each of these could have gone the other way, and each has a price.

**Derived units are measure abbreviations, not fresh measures.** `N = kg m / s^2`, not an opaque
`N`, so the witness algebra can derive things, and a mass composed with an acceleration unifies with
newtons. The cost is that SI distinctions F# cannot see collapse. `Hz` and `Bq` are both `s⁻¹`, `Gy`
and `Sv` are both `J/kg`, and which name is displayed follows the order of the registry, not intent.

**Non-SI units are opaque, for the opposite reason.** `eV` is *not* an abbreviation for `J`. If it
were, a joule could be silently added to an electronvolt and the 1.602×10⁻¹⁹ would never be
applied. Coherent units should unify and converted units must not, so the cost is an explicit
conversion at the boundary.

**Nothing is inferred from a bare dimension.** A supplied factorisation is read back and a
registered name is looked up, but a dimension handed over on its own is never factorised, since the
dimension of `J/K` is also the dimension of `N·m/K`. The cost is verbosity, `U.div U.volt U.metre`
where something shorter would do.

**Affine units carry an offset from day one.** °C is a shifted kelvin, not a scaled one, so
`U<'u>` has had an `OffsetSI` field and an `IsAffine` flag since before anything needed them.
Products, quotients and prefixes on affine units are refused rather than silently computed against
absolute zero. Adding that field later would have broken every witness in the library.

**Dimensionless is an answer, and unstated is a different one.** A CO₂ mole fraction in ppm is
micromoles per mole. The moles cancel, and there is nothing for the compiler to check. `U.one` says
so. Its axis is drawn with no brackets rather than `[1]`, carries no prefix, and is refused beside
an axis in metres. Revenue in dollars, read from a spreadsheet, is neither dimensionless nor metres.
Left without a unit it is `U.unspecified`, which draws the same bare axis and makes no dimensional
claim at all, so nothing is checked against it.

**Units are opt-in, and the check is not.** `open Marey` takes plain arrays and a unit per axis
that may be stated, `Chart.Line(ts, xs, yUnit = U.metre)`, and checks a stated unit when two
layers share an axis. `open Marey.UnitsOfMeasure` takes a witness per axis and checks it at
compile time, `Chart.lineIn U.second U.metre ts xs`. One implementation serves both. The
unit-optional call reaches the measure-generic one at measure 1 and carries the unit at run time,
so a density over a stated metre still reads `m⁻¹`. Under the second `open` the first spelling is
a compile error, so a call cannot fall back to an axis with no unit by accident.

**A figure is a value, and backends are separate packages.** `Marey.Plot` has no backend
reference, and rendering is a function passed in at render time. So the same figure becomes SVG or
`tikzpicture` source by swapping a theme, and the SVG is byte-identical across renders. Output that
diffs cleanly can be reviewed.

**The documentation executes.** The test suite extracts every `fsharp` block in
[the guide](guide/README.md), concatenates them and runs them under `dotnet fsi`, then redraws and
compares each figure the guide shows. It also runs every script in [`examples/`](../examples/) on
its own, so neither can drift from the library.

*(The blocks on this page are the exception. They illustrate code that is tested in the guide, the
README and the examples.)*

---

## When not to use Marey

- **The data is not physical.** For counts, currencies, probabilities and indices the unit system
  has nothing to offer, and without `Marey.UnitsOfMeasure` the library is a normal plotting library
  with typed output. It works, but it was not made for this.
- **The units are known only at runtime, and the compile-time check is the point.** `U.parse`
  reads UDUNITS-2's syntax and `U.tryAs` asserts a measure back with a check, but a unit first seen
  at run time is checked at run time. No parser can move that check to the compiler.
- **3D is needed.** Nothing is projected. A figure can be animated, rendered frame by frame and
  encoded to video. In a notebook, a window or a browser page it can carry controls, zoom and a
  readout, but no datum can be picked yet. [Not in 1.0](not-in-1.0.md) is the current list of what
  does not exist.
- **The host is not .NET.** The argument above rests on F# units of measure, and there is no version
  of this library for a language without them.

Marey is built for a measured physical quantity plotted against another, in a figure meant for a
document.
