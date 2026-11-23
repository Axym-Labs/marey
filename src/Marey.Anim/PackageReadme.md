# Marey.Anim

Animation for [Marey](https://axym-labs.github.io/marey/). **An animation is a pure function of
time, and nothing in it is mutated.**

```fsharp
#r "nuget: Marey.Anim"

open Marey

let zoom = Anim.tween 2.0<s> 0.0<m> 100.0<m> |> Anim.ease Ease.cubicInOut

zoom |> Anim.at 1.0<s>        // 50.0<m>, and the type says so
zoom |> Anim.atFraction 0.25  // seek anywhere, in any order, any number of times
```

An `Anim<'a>` is a duration and a function from a time to a value. Nothing is stepped and nothing
accumulates, so frame 900 can be seen without playing from the start. A mutable scene graph cannot
offer that, and a timeline you can scrub needs it.

## What is in it

- **The algebra.** `constant`, `map`, `map2`, `andThen`, `stretch`, `ease` and `tween`, built over
  `ofSeconds` and `ofFraction`. Their associativity, identity and composition laws are
  property-tested rather than asserted in a comment.
- **A seek that is total.** `Anim.at` clamps its argument to `[0, duration]`, so a shorter
  component of a `map2` holds its final value through the tail instead of running off, and
  `andThen` is well-defined at every instant. The *normalised* clock is not clamped, on
  purpose, so an overshooting ease can overshoot.
- **Twenty-eight easing functions**: quad, cubic, quart, sine, expo, circ, back, elastic and
  bounce, each in, out and in-out, plus linear. A test finds the whole catalogue by reflection and
  checks that every one starts at 0 and ends at 1, so a new one cannot be added without being
  checked. `Ease.backOut` overshoots past its target and comes back, because nothing clamps the
  fraction.
- **Arc-length path morphing.** `Morph.paths` flattens two `PathSeg[]` to a common arc-length
  parameterisation, pairs their subpaths and interpolates, so a square becomes a triangle at a
  steady speed rather than at a steady vertex index. `MorphOptions` states the pairing, the sample
  count and the closure policy.
- **Interpolation of anything `Marey.Core` can interpolate**, including affine transforms. They
  tween through a decomposition into translate, rotate, scale and skew, so a half-turn rotates
  rather than collapsing through the zero matrix.

## Rendering one

`Marey.Anim` is generic in what it animates and mentions no `Figure`: tweening a `float<m>`
does not make you install the grammar. To render an `Anim<Figure>`, lay out **once** for the
whole shot and draw each frame inside that arrangement, which is what `Film.ofFigures` in
`Marey.Plot` does:

```fsharp
#r "nuget: Marey"

open Marey.UnitsOfMeasure

let rising =
    Anim.ofSeconds 2.0<s> (fun t ->
        let ts = [| 0.0<s>; t + 1.0<s> |]
        Chart.lineIn U.second U.metre ts (ts |> Array.map (fun t -> 50.0<m/s> * t)))

let film = Film.ofFigures 32 (Chart.size 640.0 400.0) rising
printfn "a %.0f s film, laid out once" (float film.Duration)
```

Laying out again on every frame makes the panel *breathe*. The plot rectangle is what is left
after the widest tick label has taken its room, so a domain that gains a digit moves the axis, when
only the data should move. A film sizes the gutters against every sampled state at
once and pins the unit prefix to the same union, so neither the panel nor the axis title steps
mid-shot.

Depends only on [`Marey.Core`](https://www.nuget.org/packages/Marey.Core), and is
Fable-compatible.

- [The guide on animation](https://axym-labs.github.io/marey/guide/09-animation.html)
- [Examples](https://github.com/Axym-Labs/marey/tree/main/examples)
