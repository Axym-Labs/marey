# Marey.Units

The units table of [Marey](https://github.com/Axym-Labs/marey), and how a unit is written. The
seven SI base units, the named derived units and the non-SI units physics writes in, each a witness
with its symbol, dimension, factor to SI and, for an affine unit, its offset.

```fsharp
open Marey

// A unit is named from its dimension, not from its spelling: a newton composed from base units
// prints as one.
let composed = U.mul U.kilogram U.metrePerSecondSquared
printfn "%s" (UnitDisplay.unitText Unicode UnitRegistry.si Prefix.none composed)

// Four spellings, for a screen, a terminal, LaTeX and siunitx.
printfn "%s" (UnitDisplay.unitText Siunitx UnitRegistry.si Prefix.none composed)

// Read from text, in UDUNITS-2's syntax.
printfn "%s" (UnitDisplay.unitText Unicode UnitRegistry.si Prefix.none (U.parse "kV/mm"))
```

- **Names from dimensions.** Anything that composes to a newton prints `N`. A composed unit without
  an SI name reads as it was composed, `V·m⁻¹`, and takes a prefix on its leading factor, `MV·m⁻¹`;
  a bare dimension is never factorised, since one vector is both `J/K` and `N·m/K`.
- **Prefixes from the data.** An axis over 1–5 nm reads `nm` with ticks `1 … 5`. A mass takes its
  prefix against the gram: 10⁻⁶ kg is `mg`. A rate reads `s⁻¹` and a frequency `Hz`, and a
  wavenumber `nm⁻¹`.
- **Angles.** The radian and `U.degree` are angles, so an axis in either reads `θ`, and `rad/s` or
  `°/s` never shares an axis with `Hz`, which is 2π away.
- **Non-SI units**: the electronvolt (an axis reads `keV`, not `kJ`), ångström, astronomical unit,
  light year, parsec, barn, litre, bar, atmosphere, minute, hour, day, year, dalton, gauss, calorie,
  degree, and degrees Celsius and Fahrenheit with their offsets.
- **Units from text.** `U.parse "V/m"` reads products, quotients, integer powers, SI prefixes and
  `degC`, and composes as `U.div` would; `U.tryAs U.metre (U.parse "km")` asserts the measure back,
  checked on the dimension.
- **C#.** `Units.Metre` and the rest under `Marey.CSharp`, with the algebra and `Units.Parse`.

Depends on [`Marey.Core`](https://www.nuget.org/packages/Marey.Core) and nothing else.

- [The guide on units](https://github.com/Axym-Labs/marey/blob/main/docs/guide/02-units.md)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
