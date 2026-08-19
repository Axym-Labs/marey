# Why Marey is like this

Every plotting library asks you to name your axes. Marey asks you to hand it a *unit witness*
instead of a string, and almost everything else in the library follows from that one decision.
This page is the argument for it — and for the other bets, including the places where they cost
you something.

---

## `U.second` against `"s"`

Here is free fall in matplotlib, written the way a careful person writes it:

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

In the Python, `0.4` is a bare float, `9.81` is a bare float, `"s"` is a string a human typed, and
the only thing joining the three is that the human was concentrating. `"s"` is a caption sitting
next to some numbers. It is not *about* them, and nothing in the program can tell that it is
supposed to be.

`U.second` is a value that knows it is the time dimension — `T = 1`, factor 1.0 to SI, no affine
offset — and the array it labels has type `float<s>`, which the compiler checks. Six things follow
from that, and none of them are available to a string.

### 1. The label can be wrong, and nothing notices

Swap the arguments in the Python and it draws happily; the axes keep asserting whatever you typed.
Change your data to milliseconds three months later and `"s"` does not change with it. The label
and the numbers drift apart silently, because they were never attached.

```fsharp
// error FS0001: Type mismatch. Expecting a 'float<m> []' but given a 'float<s> []'
Chart.line U.second U.metre t t
```

That is a build failure, before anything is drawn. And note what is *not* annotated in the working
version: `x` is never declared to be a length. `0.5 * 9.81<m/s^2> * t * t` has type `float<m>`
because the compiler multiplies the measures out. The label is derived from the physics rather
than asserted alongside it.

### 2. A string cannot rescale

Give matplotlib data spanning 1e-9 to 5e-9 and it does something reasonable by its own lights: it
factors the power of ten out of the ticks and prints a small `1e-9` at the corner of the axis. Your
label still reads `x [m]`. The reader is now holding two numbers and has to multiply them.

Marey knows the axis is in metres, so it puts the factor where it belongs — in the unit. Ticks read
`1, 2, 3, 4, 5`; the title reads `x [nm]`. Same information, one fewer step for the reader, and it
happens with no configuration because the prefix is *selected from the data range against the
witness*. A string has nothing to select against.

This is also why the prefix is a value carried at the axis rather than folded into the unit: the
quantity is still metres. `nm` is a presentation decision made at the edge of the figure, and
[`Prefix`](../src/Marey.Units/Prefix.fs) is the module that makes it. The same machinery is what
prints `mg` rather than `µkg` when a mass is rescaled by 10⁻⁶ — the one SI base unit whose name
already carries a prefix, handled as the special case it is.

### 3. A string has one spelling; a figure needs four

`"s"` is `s` everywhere, which is fine until the figure has to go into a LaTeX document. Marey's
witness renders in four typographies from the same dimension vector — and the difference is
invisible for seconds, so look at an acceleration:

| Style | `m/s²` |
|---|---|
| `Unicode` | `m·s⁻²` |
| `Ascii` | `m.s^-2` |
| `Latex` | `\mathrm{m}\,\mathrm{s}^{-2}` |
| `Siunitx` | `\qty{}{\metre\per\second\squared}` |

The siunitx row is the one that matters in practice. Emit `tikzpicture` source and the axis titles are set by
`siunitx`, typeset by the same engine as the surrounding document, in the document's fonts, obeying
the document's unit conventions. A figure stops being a foreign image pasted into a paper and
starts being part of it. You cannot get there from a string, because by the time you have a string
the structure that would have told you how to re-spell it is gone.

### 4. Strings do not compose; dimensions do

Compose two witnesses and the *dimension* comes out right while the naive symbol comes out as
`kg·m/s/s·m` for what is plainly `J`. That is precisely why Marey never labels an axis from a
composed symbol. The unit registry is keyed on the seven-element SI dimension vector, so anything
that composes to a newton prints `N`, whether you said `Derived.newton` or built it out of
kilograms and metres per second squared.

Where the dimension has no registered name, Marey falls back to the factorisation *you wrote*:
`U.div Derived.volt U.metre` renders `V·m⁻¹`, and `MV·m⁻¹` once prefixed, rather than the
technically-correct and unreadable `kg·m·s⁻³·A⁻¹`.

### 5. A string cannot object to its neighbours

Two series can each be perfectly well typed and still be wrong to put on one axis. Plot a
`float<K>` temperature onto the same y axis as a `float<m>` displacement and you get:

> `Marey: displacement has a y axis in m but temperature has one in K. Two physical dimensions
> cannot share an axis.`

This is the one error a type system genuinely cannot catch, because it does not exist until you
decide the two belong together — so Marey catches it when the figure is compiled, and names both
layers when it does. matplotlib's answer is to draw it.

### 6. A string is not there when the figure is reloaded

The witness survives erasure. A saved figure still knows an electronvolt axis is in electronvolts
and not joules — same dimension, a factor of 1.602×10⁻¹⁹ apart — so a reloaded eV axis prefixes to
`keV` rather than `kJ`. Strings round-trip fine; the meaning behind them does not.

---

## The fair comparison: `pint` and `astropy.units`

The section above is not an argument that Python cannot do units. It obviously can:
`astropy.units` and `pint` are good libraries, and `astropy`'s `quantity_support` will label
matplotlib axes from real quantities. If you are in Python, use them.

The difference is where the check happens and what it costs.

| | Marey | `pint` / `astropy.units` |
|---|---|---|
| When a mismatch is caught | Compile time, mostly | Runtime, when that line executes |
| What a value is at runtime | A `float`. Measures are erased. | An object wrapping a magnitude and a unit |
| Cost of the unit system | One witness per axis | One unit per value, and per arithmetic operation |
| Coverage | Whatever the compiler can prove | Whatever your tests actually exercise |

F# units of measure are a compile-time fiction: `float<m>` *is* a `float` in the emitted IL, and an
array of a million samples costs exactly what an array of a million floats costs. That is the whole
reason Marey can afford to be unit-correct on data of any size. The runtime cost of the entire unit
system is the two `U<'u>` values you hand to `Chart.line`.

Where a unit is known only at runtime — a column header in a file you did not write — Marey has to
be told, and being told is an assertion the compiler cannot check. There is no UDUNITS-style parser.
`pint` handles that case and Marey does not.

Part of it is covered from the .NET side. FSharp.Data's CSV type provider reads units out of column
headers, and Marey's seven base measures are FSharp.Core's, so a column headed `Time (s)` arrives
already carrying its unit:

```fsharp
type Fall = CsvProvider<"fall.csv">          // header: Time (s), Distance (m)
let t : decimal<s>[] = rows |> Array.map (fun r -> r.Time)
```

The limits matter before you rely on it. The provider's sample is resolved at build time, so this
covers a schema you know when you compile rather than one you first see at runtime; if the file you
load disagrees with the sample, you find out when the row is read. Only bare SI symbols are
matched, so `Pressure (kPa)`, `Field (V/m)` and `Temp (degC)` are each unrecognised and arrive as a
plain `decimal`, with no measure and no warning.

What the restriction buys is that past the point where data enters, nothing is asserted again. The
check is the compiler's, not a test somebody has to remember to write.

---

## The other bets

Each of these is a decision that could have gone the other way, with what it buys and what it costs.

**Derived units are measure abbreviations, not fresh measures.** `N = kg m / s^2` rather than an
opaque `N`. This is what lets the witness algebra actually derive things: compose a mass with an
acceleration and the result unifies with newtons. The cost is that SI distinctions F# cannot see
collapse — `Hz` and `Bq` are both `s⁻¹`, `Gy` and `Sv` are both `J/kg`, and the library resolves
those display collisions by registry ordering rather than by knowing which you meant.

**Non-SI units are opaque, for exactly the opposite reason.** `eV` is *not* an abbreviation for
`J`, because if it were, a joule could be silently added to an electronvolt and the 1.602×10⁻¹⁹
would never be applied. Coherent units want to unify; converted units must not. The cost is an
explicit conversion at the boundary, which is the correct amount of friction.

**Nothing is inferred from a bare dimension.** Marey will read back a factorisation you supplied,
and it will look up a registered name, but it will never factorise a dimension it was handed cold —
because the dimension of `J/K` is also the dimension of `N·m/K`, and choosing one would be a guess
presented as a fact. The cost is verbosity: `U.div Derived.volt U.metre` rather than something
shorter. A confident guess is worse than a verbose truth on an axis somebody is going to cite.

**Affine units carry an offset from day one.** °C is not a scaled kelvin, it is a shifted one, so
`U<'u>` has had an `OffsetSI` field and an `IsAffine` flag since before anything needed them.
Products, quotients and prefixes are refused on affine units rather than silently computed against
absolute zero. Adding that field later would have been a breaking change to every witness in the
library.

**Dimensionless is an answer, not a failure.** A CO₂ mole fraction in ppm is micromoles per mole;
the moles cancel and there is nothing for the compiler to check. Marey draws that axis with no unit
brackets at all rather than printing `[1]`, refuses to hang a prefix on it, and lets you write the
label. `Chart.Plain.line` exists for exactly this data — but it is deliberately not the default
spelling and not an overload, because data that *does* have a dimension should have to say so. A
shim that let it stay silent would cost the library the one thing it is for.

**A figure is a value, and backends are separate packages.** `Marey.Plot` has no backend reference;
rendering is a function you pass in. Which is why the same figure becomes SVG or `tikzpicture`
source by swapping a theme, and why the SVG is byte-identical across renders — a figure that diffs
cleanly in git is a figure you can review.

**The documentation executes.** Every `fsharp` block in [the guide](guide.md) is extracted,
concatenated and run under `dotnet fsi` by the test suite, and every script in
[`examples/`](../examples/) is run on its own. Documentation that has drifted from the library is
worse than no documentation, and this is the cheapest way to make drift impossible.

*(The blocks on this page are the exception — they are illustrative, and mirror code that is tested
in the guide, the README and the examples.)*

---

## When not to use Marey

- **Your data is not physical.** Counts, currencies, probabilities, mole fractions, indices — the
  unit system has nothing to offer, and `Chart.Plain` is a thin veneer over a normal plotting
  library. Marey is still fine here. It is just not *for* here.
- **You need units known only at runtime.** No UDUNITS-2 parser yet. The witness has carried the
  fields such a parser would need since the beginning, but it does not exist today.
- **You need interactivity, animation, 3D, or a large geom vocabulary.** Five geoms, two backends,
  static output. The [guide's closing section](guide.md#what-is-not-here-yet) is the current list
  of what does not exist.
- **You are not on .NET.** The whole argument above rests on F# units of measure. There is no
  version of this library for a language without them.

What is left, when you subtract all of that, is the case Marey was built for: a measured physical
quantity, plotted against another measured physical quantity, in a figure that is going to end up
in a document somebody else will read. For that figure, `U.second` is worth the extra word.
