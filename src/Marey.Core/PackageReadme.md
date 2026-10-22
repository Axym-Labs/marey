# Marey.Core

The types under [Marey](https://github.com/Axym-Labs/marey): dimensions, unit witnesses, values
that carry their unit, unit-indexed geometry, scales, typed statistics, and the scene every backend
draws. No dependencies, `netstandard2.1`, and it compiles under Fable.

```fsharp
open Marey

// A witness is the runtime half of a measure: F# erases `m`, and `U<m>` keeps its symbol,
// dimension and factor to SI, which is what an axis titles itself from.
printfn "%s" (Dim.show (U.dim U.metre))

// Scales are indexed by the unit, so a scale over metres cannot be handed seconds.
let scale = Scale.linear U.metre (0.0<m>, 10.0<m>)
printfn "%g" (float (scale.Forward 5.0<m>))

// Values with their witness: arithmetic composes the witnesses as it composes the measures.
let travelled = U.metre.Of [| 0.0<m>; 2.0<m>; 8.0<m> |]
let speed = travelled / U.second.Of 2.0<s>
printfn "%s" speed.Unit.Symbol
```

- **`Dim`**, the seven SI exponents, and **`U<'u>`**, the witness: a symbol, a dimension, a factor
  to SI and an offset for an affine unit such as degrees Celsius. `U.mul`, `U.div` and `U.recip`
  compose witnesses and keep the factorisation, so `V·m⁻¹` still reads as itself.
- **`Q<'u>` and `Quantity<'u>`**, values and one value with their witness, made by `U.second.Of`.
  A chart over them takes no witness, and their products, quotients, sums and differences compute
  the witness of the result.
- **`Stat`**: bins, densities, ECDFs, quantiles and summaries, regression, loess, hexbins and
  bootstrap intervals, each in the units of its input. A fit of metres against seconds has a slope
  of type `float<m/s>`.
- **`Scale`**: linear, log, symlog, power, reciprocal and categorical, each `Forward` and
  `Inverse` checked as mutual inverses. **`Vec2`**, **`Affine`** and **`Coord`**: geometry and
  coordinate systems indexed by unit.
- **`Scene`**, the closed set of ten nodes every backend draws, and **`Wire`**, which writes a
  scene or a patch to bytes and back exactly, with the text metrics its layout used: what a kernel
  sends a page to draw.

Most projects reference [`Marey`](https://www.nuget.org/packages/Marey), which brings this with the
plotting grammar, units table and outputs. On its own this package has no units table (that is
[`Marey.Units`](https://www.nuget.org/packages/Marey.Units)), no charts and no renderer.

- [The guide on the scene](https://github.com/Axym-Labs/marey/blob/main/docs/guide/15-underneath.md)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
