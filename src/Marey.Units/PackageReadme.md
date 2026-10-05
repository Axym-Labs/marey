# Marey.Units

Unit witnesses and unit rendering for [Marey](https://github.com/Axym-Labs/marey), a
dimensionally-typed visualization system for F#.

SI base units, 22 derived units and 18 common non-SI units, each as a witness carrying its symbol,
dimension, conversion factor and — where it needs one — its affine offset, together with the
rendering machinery that lets an axis write its own title.

```fsharp
open Marey

// Canonicalisation is keyed on the dimension, not on strings. A newton composed out of base
// units and a newton stated outright are the same unit, and print the same symbol.
let composed = U.mul U.kilogram U.metrePerSecondSquared
printfn "%s" (UnitDisplay.unitText Unicode UnitRegistry.si Prefix.none composed)   // N

// Four spellings, because a LaTeX document and a terminal want different things.
printfn "%s" (UnitDisplay.unitText Siunitx UnitRegistry.si Prefix.none composed)   // \qty{}{\newton}
```

## What it does

- **Canonical symbols from physics, not from string matching.** The registry is keyed on the
  seven-element dimension vector, so anything that composes to a newton prints `N`.
- **Prefix selection.** A domain spanning 1e-9 to 5e-9 metres yields an axis labelled `nm` with
  ticks `1, 2, 3, 4, 5`. The kilogram is handled correctly — a prefix of 10⁻⁶ on `float<kg>`
  prints `mg`, against the gram, not `µkg`.
- **Composed units still name themselves.** `U.div Derived.volt U.metre` has no SI name and its
  dimension spells out as `kg·m·s⁻³·A⁻¹`. It renders `V·m⁻¹`, and `MV·m⁻¹` when prefixed, because
  the witness records the factorisation it was built from. Nothing is guessed: a bare dimension is
  never factorised, since the same vector is both `J/K` and `N·m/K`.
- **Four rendering styles** — Unicode, ASCII, LaTeX and `siunitx`.
- **Affine units.** Degrees Celsius and Fahrenheit carry an offset, and `IsAffine` reports it.
  A composed unit built from an affine one drops the offset, because the quotient of an affine
  unit is not affine.
- **Non-SI units physics actually writes in** — `electronvolt`, `angstrom`, `parsec`, `lightYear`,
  `dalton`, `atmosphere`, `bar`, `barn`, `calorie`, `gauss`, `litre`, `minute`, `hour`, `day`,
  `year`. An electronvolt axis reads `keV`, not `kJ`.

Depends on [`Marey.Core`](https://www.nuget.org/packages/Marey.Core) and nothing else.

- [The guide](https://github.com/Axym-Labs/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
