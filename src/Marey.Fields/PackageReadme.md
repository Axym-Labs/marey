# Marey.Fields

Fields for [Marey](https://github.com/Axym-Labs/marey), a dimensionally-typed visualization
system for F#. **The gradient of a potential in volts over metres is a field in V/m, and the type
says so.**

```fsharp
#r "nuget: Marey.Fields"
#r "nuget: Marey.Backend.Svg"

open Marey

// Two line charges: a potential in volts, sampled over metres.
let phi (p: Vec2<m>) =
    let d1 = Vec2.norm { X = p.X - 0.5<m>; Y = p.Y }
    let d2 = Vec2.norm { X = p.X + 0.5<m>; Y = p.Y }
    (log (float d2) - log (float d1)) * 1.0<V>

let xs = Grid.linear -2.0<m> 2.0<m> 81
let ys = Grid.linear -1.5<m> 1.5<m> 61
let potential = Field.sampleScalar xs ys phi            // ScalarGrid<m, m, V>

let e = Field.grad potential                             // VectorGrid<m, m, V/m, V/m>
let rho = Field.div e                                    // ScalarGrid<m, m, V/m²>

Field.heatmap U.metre U.metre Derived.volt potential
|> Chart.overlay (Field.contours U.metre U.metre potential)
|> Chart.render SvgBackend.render (Chart.size 640.0 420.0)
```

## A phase space is a domain

A pendulum lives in (θ, ω): radians on one axis and radians per second on the other. A `Flow`
has a separate unit on each axis, and its grid's components are the rates of change.

```fsharp
let pendulum: Flow<rad, rad/s, s> = fun th om -> struct (om, -9.81<1/s^2> * sin (float th) * 1.0<rad>)

let flow = Field.sampleFlow (Grid.linear -3.0<rad> 3.0<rad> 61) (Grid.linear -6.0<rad/s> 6.0<rad/s> 61) pendulum
let d: ScalarGrid<rad, rad/s, 1/s> = Field.div flow      // zero: phase-space area is conserved
// Field.curl flow                                       // does not compile
```

The derived quantities say in their signatures which domains they exist on. `div` asks that ∂u/∂x
and ∂w/∂y share a unit, which a spatial field and a flow both satisfy. `curl` and `laplacian` ask
for one unit on both axes, so over a phase space they are compile errors. That is correct,
because ∂ω̇/∂θ and ∂θ̇/∂ω are in different units and their difference does not exist.

## What is in it

- **Grids.** `ScalarGrid` and `VectorGrid` on rectilinear nodes, from data or sampled from a
  function. `Grid.linear` and `Grid.log` space the nodes evenly on a linear or a log axis.
  Bilinear reads do not extrapolate. A node that is not finite is kept, counted (`missing`) and
  not drawn.
- **Derivatives.** `grad`, `div`, `curl`, `laplacian` and `jacobian`, by three-point differences
  that are exact for a quadratic on any rectilinear grid and second-order otherwise.
- **Drawing.** `Field.heatmap`, `Field.contours` and `Field.bands`, with a colorbar in the field's
  unit. The picture is computed once the panel's scales are final, so a field lines up with every
  other layer on a linear or a log axis. Contour levels are the nice numbers the field's own range
  would be ticked at. Saddle cells are resolved by the asymptotic decider, and bands are filled
  even-odd, holes included.
- **Arrows.** `Field.quiver` sizes each arrow by its magnitude through the quiver's own scale,
  because the arrows are in V/m and the axes in metres, and adds a key that says what one length
  means: `2 kV·m⁻¹`. `Field.directions` draws equal arrows for a phase space, where the two
  components share no unit and a length would mean nothing. Arrows follow the field on the
  panel, so they stay tangent to it on a log axis.
- **Streamlines.** `Field.streamlines` draws them evenly spaced on the page, a separation in pixels
  apart, so a phase portrait has its closed orbits around a centre and its separatrices through
  a saddle with nothing configured. They are integrated on the panel, so they stay the field's
  curves on a log axis. No randomness is involved, so the same field draws the same lines.
- **Line-integral convolution.** `Field.lic` smears a noise texture along the streamlines, so the
  flow shows at every pixel rather than along a chosen few lines. `licBy` colours it by
  magnitude. It is a raster at the plot area's resolution, with an oversample for print. Its
  noise is seeded, so a seed names one image.
- **Phase portraits.** `Phase.fixedPoints` finds where a flow vanishes and classifies each point
  by its linearisation: saddle, node, focus or centre, with eigenvalues in `1/'t`.
  `FixedPoint.describe` prints one: `saddle at (3.1416 rad, 0 rad·s⁻¹), λ = 3.1266, -3.1266 s⁻¹`.
  `Phase.portrait` draws the streamlines, the nullclines and the fixed points together.
- **Fields in motion.** A field that changes with time is an `Anim<Figure>` like any other, and
  `Film.ofFigures` renders it. The colour scale and a quiver's arrow scale are held for the whole
  film, so a growing field is drawn growing. `Chart.withColorDomain` states the range instead.

## Links

- [The repository](https://github.com/Axym-Labs/marey)
- [The guide](https://github.com/Axym-Labs/marey/blob/main/docs/guide.md)
