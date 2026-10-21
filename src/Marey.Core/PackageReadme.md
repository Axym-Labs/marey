# Marey.Core

The foundation of [Marey](https://github.com/Axym-Labs/marey), a dimensionally-typed
visualization system for F#. **Zero dependencies**, `netstandard2.1`.

Take this package on its own for the types without the plotting: dimension vectors, unit
witnesses, unit-indexed geometry, scales, and the scene graph that backends consume.

```fsharp
open Marey

// A unit witness ties a compile-time [<Measure>] to runtime metadata, which is what makes an
// axis able to label itself. F# erases measures; this is how the information survives.
let metre = U.metre                  // U<m>
printfn "%A" (U.dim metre)           // the SI dimension vector

// Scales are unit-indexed, so a scale over metres cannot be handed seconds.
let scale = Scale.linear U.metre (0.0<m>, 10.0<m>)
printfn "%A" (scale.Forward 5.0<m>)  // normalized panel space
```

## What is in it

- **`Dim`** — the seven-element SI dimension vector, with group laws checked by property tests.
- **`U<'u>`** — the unit witness: a symbol, a dimension, a factor to SI, and an offset for affine
  units like degrees Celsius. `U.mul`/`div`/`recip`/`square`/`cube` compose them, carrying the
  factorisation structurally so a composed unit can still name itself.
- **`Vec2`/`Vec3`, `Affine`, `Coord`** — unit-indexed geometry and coordinate transforms.
- **`Scale`** — seven scale kinds, each with `Forward`/`Inverse` verified as mutual inverses.
- **`Scene`** — a small closed IR of ten cases. Every backend consumes this and nothing else.
- **`Wire`** — a scene or a patch as bytes and back, exactly: little-endian float64, versioned,
  with the text metrics its layout used (`TextTable`). What a kernel sends a page to draw.
- **`Interp`**, tick generation, and text metrics.

## Zero dependencies

Nothing is added to this package's references. That is what lets the same IR run in a notebook, in
a browser through Fable, and in an AOT-compiled binary. `FSharp.Core` is the language runtime, not
a dependency.

## Related packages

`Marey.Core` has no units table, no chart API and no renderer. For the usual case install
[`Marey.Plot`](https://www.nuget.org/packages/Marey.Plot) and a backend, or
[`Marey.Html`](https://www.nuget.org/packages/Marey.Html), which pulls in everything.

- [The guide on the scene](https://github.com/Axym-Labs/marey/blob/main/docs/guide/15-underneath.md)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
