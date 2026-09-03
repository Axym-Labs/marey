# Marey.Anim

Animation for [Marey](https://github.com/Jonathan-Schaefer-git/marey), a dimensionally-typed
visualization system for F#. **An animation is a pure function of time, not a mutation.**

```fsharp
#r "nuget: Marey.Anim"

open Marey

let zoom = Anim.tween 2.0<s> 0.0<m> 100.0<m> |> Anim.ease Ease.cubicInOut

zoom |> Anim.at 1.0<s>        // 50.0<m>, and the type says so
zoom |> Anim.atFraction 0.25  // seek anywhere, in any order, any number of times
```

An `Anim<'a>` is a duration and a function from a time to a value. Nothing is stepped, nothing
accumulates, and nothing has to be played from the start to see frame 900 — which is what a
mutable scene graph cannot offer, and what a scrubbable timeline needs.

## What is in it

- **The algebra.** `pure'`, `map`, `map2`, `andThen`, `stretch`, `ease` and `tween`, built over
  `ofSeconds` and `ofFraction` — with the associativity, identity and composition laws
  property-tested rather than asserted in a comment.
- **A seek that is total.** `Anim.at` clamps its argument to `[0, duration]`, so a shorter
  component of a `map2` holds its final value through the tail instead of running off, and
  `andThen` is well-defined at every instant. The *normalised* clock is deliberately not
  clamped, which is what makes an overshooting ease overshoot.
- **Twenty-eight easing functions** — quad, cubic, quart, sine, expo, circ, back, elastic and
  bounce, each in, out and in-out, plus linear. The law that every one of them starts at 0 and
  ends at 1 is quantified over the whole catalogue by reflection, so a new one cannot be added
  without being checked. `Ease.backOut` overshoots past its target and
  comes back, because nothing clamps the fraction.
- **Arc-length path morphing.** `Morph.paths` flattens two `PathSeg[]` to a common arc-length
  parameterisation, pairs their subpaths, and interpolates — so a square becomes a triangle at
  a steady speed rather than at a steady vertex index. The pairing, sample count and closure
  policy are stated in `MorphOptions` rather than hidden.
- **Interpolation of anything `Marey.Core` can interpolate**, including affine transforms,
  which tween through a decomposition into translate/rotate/scale/skew — so a half-turn rotates
  rather than collapsing through the zero matrix.

## Rendering one

`Marey.Anim` is generic in what it animates and mentions no `Figure`: tweening a `float<m>`
does not make you install the grammar. To render an `Anim<Figure>`, lay out **once** for the
whole shot and draw each frame inside that arrangement:

```fsharp
let shot = Chart.arrangeUnion 32 TextMeasure.approx extent (fun u -> Anim.atFraction u anim)
let frame u = Chart.toSceneIn TextMeasure.approx shot (Anim.atFraction u anim)
```

Re-laying out per frame makes the panel *breathe* — the plot rectangle is what is left after the
widest tick label has taken its room, so a domain gaining a digit moves the axis while the data
is what is supposed to be moving. `arrangeUnion` sizes the gutters against every sampled state at
once and pins the unit prefix to the same union, so neither the panel nor the axis title steps
mid-shot.

Depends only on [`Marey.Core`](https://www.nuget.org/packages/Marey.Core), and is
Fable-compatible.

- [The guide](https://github.com/Jonathan-Schaefer-git/marey/blob/main/docs/guide.md)
- [Examples](https://github.com/Jonathan-Schaefer-git/marey/tree/main/examples)
