# The guide

Every part of the library, with a runnable example for each.

**Every `fsharp` block below is executed by the test suite.** `GuideTests.fs` extracts them in
order, concatenates them into one script and runs it under `dotnet fsi`; if a block stops compiling
or starts throwing, that test fails. Blocks marked `fsharp-error` are the exception — they are shown
*because* they do not compile, and the checker skips them.

The blocks form one continuous session, so bindings introduced early are still in scope later.

---

## Contents

1. [Installing](#1-installing)
2. [The first figure](#2-the-first-figure)
3. [Units, and the one error a compiler cannot catch](#3-units-and-the-one-error-a-compiler-cannot-catch)
4. [Scales](#4-scales)
5. [Geoms](#5-geoms)
6. [Layers and the legend](#6-layers-and-the-legend)
7. [Axes](#7-axes)
8. [Panels](#8-panels)
9. [Themes](#9-themes)
10. [Colour](#10-colour)
11. [Categories, and the channels beyond x and y](#11-categories-and-the-channels-beyond-x-and-y)
12. [Statistics](#12-statistics)
13. [The encoding block](#13-the-encoding-block)
14. [Facets](#14-facets)
15. [Fields](#15-fields)
16. [Output: SVG, TikZ, PNG, PDF](#16-output-svg-tikz-png-pdf)
17. [Animation](#17-animation)
18. [The storyboard](#18-the-storyboard)
19. [Frames](#19-frames)
20. [Video](#20-video)
21. [Mathematical typesetting](#21-mathematical-typesetting)
22. [Notebooks](#22-notebooks)
23. [Interaction](#23-interaction)
24. [The scene IR](#24-the-scene-ir)
25. [Escape hatches](#25-escape-hatches)

---

## 1. Installing

One package reference is enough for the common case. `Marey.Html` depends on Plot, which depends
on Units and Core, so all of it arrives transitively.

```fsharp
#r "nuget: Marey.Html"

open Marey
```

| Package | Take it when |
|---|---|
| `Marey.Core` | The `Scene` IR, colour, scales and geometry, and nothing else. Zero dependencies. |
| `Marey.Units` | SI + 22 derived + 18 non-SI unit witnesses, and self-labelling axes. |
| `Marey.Plot` | `Chart`, the `plot` block, geoms, channels, statistics, facets, themes, palettes, layout, and the storyboard block (§18). No backend reference — see §16. |
| `Marey.Backend.Svg` | Rendering to SVG. |
| `Marey.Backend.Tikz` | Rendering to `tikzpicture` source for LaTeX. |
| `Marey.Backend.Skia` | Rendering to PNG or PDF, and real text shaping. The only one with third-party dependencies, and native ones at that — see §16. |
| `Marey.Anim` | Animation as a pure function of time: `Anim<'a>`, the easing catalogue and path morphing. Depends on `Marey.Core` alone, and arrives with `Marey.Plot` — see §17. |
| `Marey.Tools.Video` | Encoding a rendered frame sequence to video with `ffmpeg` — see §20. |
| `Marey.Fields` | Scalar and vector fields on dimensioned planes, their derivatives, phase portraits, and the field geoms: heatmap, contours, bands, quiver, streamlines, LIC — see §15. |
| `Marey.Tools.Tex` | Typesetting TeX in a figure as filled outlines, by shelling out to `latex` and `dvisvgm` — see §21. |
| `Marey.Toolchain` | Resolving and running the external programs Marey drives but does not ship. Zero dependencies; arrives with `Marey.Tools.Video`. |
| `Marey.Interact` | Controls on a figure, and the pure step a notebook drives: zoom, pan, a readout, recordings — see §23. Arrives with `Marey.Html`. |
| `Marey.Html` | HTML renderings of twelve Marey types for a notebook cell, a live figure's page among them. Host-neutral. |
| `Marey.Verso` | The [Verso](https://github.com/DataficationSDK/Verso) notebook extension. Installed *into the host*, not referenced from user code. |

---

## 2. The first figure

```fsharp
let t = Array.init 24 (fun i -> float i * 0.4<s>)
let x = t |> Array.map (fun t -> 0.5 * 9.81<m/s^2> * t * t)

let fall =
    Chart.line U.second U.metre t x
    |> Chart.withTitle "Free fall"
    |> Chart.withSubtitle "displacement against elapsed time"
```

Nothing was configured, and the figure already carries:

- that the x axis is **seconds** and the y axis is **metres**, from the two unit witnesses;
- that `x` is a length at all — `0.5 * 9.81<m/s^2> * t * t` has type `float<m>`, derived by the
  compiler rather than annotated;
- the axis titles (`t [s]`, `x [m]`), the tick values, and the SI prefix appropriate to the
  magnitudes actually present.

The same call on nanometre-scale data relabels itself:

```fsharp
let small = t |> Array.map (fun v -> 1.0e-9<m/s> * v)
let nm = Chart.line U.second U.metre t small
let nmSvg = nm |> Chart.render SvgBackend.render (Chart.size 480.0 300.0)

// The axis says nanometres, and the ticks are small integers rather than 1e-9.
printfn "nanometre axis: %b" (nmSvg.Contains "[nm]")
```

---

## 3. Units, and the one error a compiler cannot catch

A `float<m>` and a `float<s>` are different types, so most dimensional mistakes are build errors.
The following does not compile, and is shown for the error it produces:

```fsharp-error
// error FS0001: Type mismatch. Expecting a 'float<m> []' but given a 'float<s> []'
Chart.line U.second U.metre t t
```

The remaining case is the one that *is* well-typed. Two separately-correct series can still be
wrong to put on one axis, which is caught when the figure is compiled rather than when it is
built:

```fsharp
let temperature = t |> Array.map (fun v -> 293.0<K> + 0.5<K/s> * v)

let mixed =
    Chart.line U.second U.metre t x
    |> Chart.withName "displacement"
    |> Chart.addLine U.second U.kelvin t temperature
    |> Chart.withName "temperature"

let clash =
    try
        mixed |> Chart.render SvgBackend.render (Chart.size 400.0 250.0) |> ignore
        None
    with e ->
        Some e.Message

printfn "dimension clash reported: %b" clash.IsSome
```

That error is not a failed conversion — each array is well typed on its own. It is the one
dimensional mistake a type system cannot see, because it does not exist until the two series are
placed on a single axis.

### The unit witnesses

`U<'u>` is the runtime witness for a compile-time measure: it carries the SI dimension vector, so
the library can canonicalise, prefix and label without parsing strings.

```fsharp
printfn "newton dim:  %s" (Dim.show (U.dim Derived.newton))
printfn "joule:       %s" (U.symbol Derived.joule)
printfn "celsius affine: %b" (U.isAffine NonSi.degreeCelsius)
```

`degreeCelsius` is affine, so its conversion to SI is not a factor alone.

Composing witnesses gets the *measure* right and the *name* wrong, which is the reason the
registry exists. `Derived.newtonByComposition` is built with `U.mul`/`U.div` from base witnesses
rather than stated outright, and stands next to the named one for comparison:

```fsharp
printfn "named:    %s" (U.symbol Derived.newton)
printfn "composed: %s" (U.symbol Derived.newtonByComposition)
printfn "same dimension: %b" (U.dim Derived.newton = U.dim Derived.newtonByComposition)
```

The composed symbol is `kg·m/s²` — correct, and not what an axis is labelled with. The registry is
keyed by the `Dim` itself, so the canonical name is recovered from the dimension rather than by
matching strings, in all four spellings the renderers need:

```fsharp
let canonical = UnitRegistry.tryCanonical UnitRegistry.si (U.dim Derived.newtonByComposition)
printfn "canonical: %A" (canonical |> Option.map (fun u -> u.Unicode, u.Ascii, u.Siunitx))
```

Most compositions have no canonical name, because most combinations of dimensions are not named
units. An electric field is one: `V/m` is not in SI's table, and its dimension spelled out is
`kg·m·s⁻³·A⁻¹`, which is correct and unreadable. Where the registry has no name for the whole
dimension, the axis is labelled from the composition as written:

```fsharp
let fieldUnit = U.div Derived.volt U.metre
printfn "field: %s" (UnitDisplay.unitText Unicode UnitRegistry.si Prefix.none fieldUnit)
printfn "siunitx: %s" (UnitDisplay.unitText Siunitx UnitRegistry.si Prefix.none fieldUnit)
```

That is `V·m⁻¹` and `\qty{}{\volt\per\metre}` — and a prefix lands on the leading factor, so a
megavolt-per-metre axis reads `MV·m⁻¹`. A bare dimension is never factorised: the dimension of
`J/K` is also the dimension of `N·m/K`, so only a supplied factorisation is read back. A registered
name still wins, so `U.mul Derived.newton U.metre` is `J`.

**Except where the composition carries an angle.** The radian is dimensionless in SI, so `rad/s`
has the dimension of a hertz, and the registry would name it one. An angular velocity is not a
frequency: they differ by 2π. So a factorisation that contains `rad` or `sr` is labelled as
written, and the axis takes the angle's own quantity name:

```fsharp
let angularVelocity = U.div Derived.radian U.second
printfn "angular velocity: %s" (UnitDisplay.unitText Unicode UnitRegistry.si Prefix.none angularVelocity)
printfn "frequency:        %s" (UnitDisplay.unitText Unicode UnitRegistry.si Prefix.none Derived.hertz)
```

That is `rad·s⁻¹` and `Hz`. An axis in `rad·s⁻¹` is titled `ω`, one in `rad` `θ`, and one in
`rad·s⁻²` `α`, and a layer in `rad/s` refuses to share an axis with a layer in `Hz`, as metres
refuse joules. The angle is seen in the factors a witness was **composed** from, or in the symbol
`rad` of `Derived.radian` itself. A witness declared by hand with a compound symbol,
`U<rad/s>("rad/s", …)`, has no factors to read, so it renders and unifies as a plain s⁻¹: build
angular units with `U.div` and `U.mul` from `Derived.radian`.

Non-SI witnesses cover the units physics actually writes in: `electronvolt`, `angstrom`,
`parsec`, `lightYear`, `dalton`, `atmosphere`, `bar`, `barn`, `calorie`, `gauss`, `litre`,
`minute`, `hour`, `day`, `year`, `astronomicalUnit`, `degreeCelsius`, `degreeFahrenheit`.

---

## 4. Scales

Seven kinds, all invertible, all tested for `inverse (forward x) = x`, monotonicity and endpoint
mapping over generated domains.

| Kind | Constructor |
|---|---|
| Linear | `Scale.linear` |
| Log (base 10, 2, e, arbitrary) | `Scale.log10`, `Scale.log2`, `Scale.ln`, `Scale.logBase` |
| Symlog — log that survives zero and negatives | `Scale.symlog` |
| Power | `Scale.power`, `Scale.sqrt` |
| Reciprocal | `Scale.reciprocal` |

The common case needs none of them directly: the kind is selected on the figure and the domain
follows from the data.

```fsharp
let decades = [| 1.0<m>; 10.0<m>; 100.0<m>; 1000.0<m>; 10000.0<m> |]
let response = [| 1.0<V>; 0.5<V>; 0.25<V>; 0.125<V>; 0.0625<V> |]

let logFigure =
    Chart.line U.metre Derived.volt decades response
    |> Chart.withXLog
    |> Chart.withTitle "Response against distance"
```

Build one directly to control the domain:

```fsharp
let logScale = Scale.log10 U.metre (1.0<m>, 1000.0<m>)
printfn "stated domain: %A" logScale.Domain

// Or derive one from the data, with a margin, and let it be nice:
printfn "from data:     %A" (Scale.domainOf x)
printfn "with margin:   %A" (Scale.paddedDomain 0.05 x)
```

Domains can be pinned on the figure too:

```fsharp
let framed =
    fall
    |> Chart.withYDomain 0.0 100.0
    |> Chart.withXDomain 0.0 10.0
```

Ticks walk a 1-2-5 ladder and return what falls out, so a theme's `TickCount` is a *target*
rather than a promise:

```fsharp
printfn "ticks: %A" (Ticks.linear 5 (0.0, 9.2))
```

---

## 5. Geoms

Each has a constructor that starts a figure, and most have an `add*` that layers onto the last
panel.

```fsharp
let categories = [| 1.0<s>; 2.0<s>; 3.0<s>; 4.0<s> |]
let counts = [| 3.0<m>; 7.0<m>; 4.0<m>; 9.0<m> |]
let sigma = [| 0.4<m>; 0.6<m>; 0.3<m>; 0.8<m> |]

let lineOnly    = Chart.line     U.second U.metre t x
let scatterOnly = Chart.scatter  U.second U.metre t x
let areaOnly    = Chart.area     U.second U.metre t x
let barOnly     = Chart.bar      U.second U.metre categories counts
let errorOnly   = Chart.errorbar U.second U.metre categories counts sigma
let stepOnly    = Chart.step     U.second U.metre categories counts
```

`errorbar` is two layers in one call — the bars and the centre points — and only one of them
appears in the legend. `step` draws a staircase; `Chart.stepWith` places the riser at the previous
sample (`StepBefore`) or halfway between (`StepMid`) instead of at the sample itself.

**A band** is two y series and the region between them — a confidence interval, an envelope, a
minimum and maximum. Both bounds are in the y unit, so an interval in the wrong unit does not
compile.

```fsharp
let lo = counts |> Array.map (fun v -> v - 1.0<m>)
let hi = counts |> Array.map (fun v -> v + 1.0<m>)

let withBand =
    Chart.line U.second U.metre categories counts
    |> Chart.addRibbon U.second U.metre categories lo hi
```

**Segments** join pairs of points, and **labels** annotate the series already on the figure —
`addLabels` reuses that layer's own positions and colour, so the labels stay on their marks.

```fsharp
let displacements =
    Chart.segment U.second U.metre categories counts (categories |> Array.map ((+) 0.5<s>)) hi

let annotated =
    Chart.scatter U.second U.metre categories counts
    |> Chart.addLabels [| "a"; "b"; "c"; "d" |]
```

**A heatmap** is tiles plus a colour channel. The cell size comes from the grid's own spacing, and
the third quantity carries its own unit, so the colorbar labels itself from the witness:

```fsharp
let cells =
    [| for gy in 0.0 .. 2.0 do
           for gx in 0.0 .. 3.0 -> gx * 1.0<s>, gy * 1.0<m> |]

let field =
    Chart.heatmap
        U.second
        U.metre
        U.kelvin
        (cells |> Array.map fst)
        (cells |> Array.map snd)
        (cells |> Array.map (fun (gx, gy) -> 280.0<K> + 4.0<K/s> * gx + 7.0<K/m> * gy))
    |> Chart.withColorLabel "T"
```

**Distributions by category** are box plots and violins. Both take one value per row and a key per
row; the rows are grouped, summarised or smoothed, and drawn one band per level:

```fsharp
let subject = [| "control"; "treated"; "control"; "treated"; "control"; "treated" |]
let yield' = [| 12.4<m>; 19.1<m>; 13.0<m>; 18.2<m>; 12.9<m>; 19.6<m> |]

let boxes = Chart.Category.boxplot U.metre subject yield'
```

The whiskers reach the most extreme observation within 1.5 × IQR of the box, and anything past
them is drawn as a point rather than absorbed into the whisker. The y domain covers those points,
so an outlier is never clipped by the panel edge.

`Chart.Category.violin` draws the same split as a kernel density instead of a summary. Every
violin in a figure shares one bandwidth, taken from the undivided sample, so a sparse level is not
smoothed harder than a populous one — and the widths are normalised together, so a level with more
rows draws a wider violin.

Per-layer style overrides apply to the most recently added layer:

```fsharp
let styled =
    Chart.line U.second U.metre t x
    |> Chart.withName "model"
    |> Chart.withColor Palette.vermillion
    |> Chart.withDash [| 4.0<px>; 2.0<px> |]
    |> Chart.addScatter U.second U.metre t.[..5] x.[..5]
    |> Chart.withName "measured"
    |> Chart.withMarker MarkSquare
    |> Chart.withOpacity 0.8
```

A style set explicitly survives a theme change; one left unset is recoloured. That asymmetry is
what makes `withTheme` safe to apply last:

```fsharp
let rethemed = styled |> Chart.withTheme Theme.dark
```

---

## 6. Layers and the legend

`withName` names the layer *most recently added*, which is why it reads as a suffix rather than an
argument. An unnamed layer stays out of the legend.

```fsharp
let twoSeries =
    Chart.line U.second U.metre t x
    |> Chart.withName "model"
    |> Chart.addScatter U.second U.metre t.[..5] (x.[..5] |> Array.map (fun v -> v * 0.98))
    |> Chart.withName "measured"
    |> Chart.withLegend LegendTopLeft
    |> Chart.withGrid true true
```

Placements: `LegendTopLeft`, `LegendTopRight`, `LegendBottomLeft`, `LegendBottomRight`,
`LegendNone`.

Adding a layer unifies the domains across the panel, so the series share axes rather than each
drawing its own.

---

## 7. Axes

Titles are derived, and every part is overridable.

```fsharp
let labelled =
    fall
    |> Chart.withXLabel "elapsed"
    |> Chart.withYLabel "drop"
```

The unit *spelling* is a presentation choice, so it lives on the theme with a per-axis override,
which is what makes `Theme.tikz` sufficient on its own to produce `siunitx` markup:

```fsharp
let ascii = fall |> Chart.withUnitStyle Ascii
```

`Unicode` (`m·s⁻²`) for screens, `Ascii` for terminals, `Latex`, and `Siunitx`
(`\qty{}{\metre\per\second\squared}`) for papers.

Scale kinds and domains, set per axis:

```fsharp
let axisWork =
    Chart.line U.metre Derived.volt decades response
    |> Chart.withXScaleKind (ScaleKind.Log 10.0)
    |> Chart.withYDomain 0.0 1.2
```

---

## 8. Panels

`beside` and `above` compose whole figures, and each panel keeps its own axes and scales.

```fsharp
let row =
    Chart.line U.second U.metre t x
    |> Chart.withPanelTitle "position"
    |> Chart.beside (Chart.scatter U.second U.metre t x |> Chart.withPanelTitle "samples")

let stacked =
    row
    |> Chart.above (Chart.bar U.second U.metre categories counts |> Chart.withPanelTitle "counts")

printfn "panels: %d, rows: %d, cols: %d" stacked.Panels.Length stacked.Rows stacked.Cols
```

Layout is a measure–arrange constraint grid, so a panel gets the space its contents need rather
than an equal share of the surface.

---

## 9. Themes

Six, all resolved values rather than stylesheet lookups: reading a style is a field access, and a
missing one is a build error.

| Theme | For |
|---|---|
| `Theme.modern` | **The default.** Screen-first: sans, flat panel, horizontal gridlines, no frames. |
| `Theme.publication` | A paper. Serif, hairline rules, no gridlines, no chartjunk. |
| `Theme.notebook` | `modern` with a tinted panel and vertical gridlines, so a figure reads as an object inside a cell. |
| `Theme.presentation` | Large type, for projection. |
| `Theme.dark` | Not an inversion — `#E8E8E8` on `#16181C`, and the palette swaps so Okabe–Ito's black is not invisible. |
| `Theme.tikz` | Serif metrics and `Siunitx` unit spelling. Selecting it is the whole configuration. |

```fsharp
let onDark = fall |> Chart.withTheme Theme.dark
```

Themes are values, so adjust rather than fork:

```fsharp
let mine =
    Theme.modern
    |> Theme.withPalette Palette.okabeIto
    |> Theme.withGrid false true
    |> Theme.withTickCount 4
    |> Theme.scaleType 1.15
```

`scaleType` scales every typographic size and leaves everything else alone. `withFontFamily`
affects layout as much as appearance: the family string is what the text measurer reads to decide
how much room a label needs, and for the TikZ backend — which emits no family and lets the document
supply the face — that measurement is all the family does.

Every theme can be measured, and the shipped ones are:

```fsharp
for theme in Theme.all do
    let r = Theme.validate theme
    printfn "%-13s text %6.2f  axis %6.2f  grid %5.2f  Ok=%b"
        r.Theme r.TextContrast r.AxisContrast r.GridContrast r.Ok
```

`GridContrast` is reported and deliberately *not* floored, since a gridline is not meant to compete
with the data. `TextContrast` and `AxisContrast` are floored at WCAG AAA and 3:1, and every shipped
theme is run through this check by the test suite.

---

## 10. Colour

`Color` converts to Oklab/Oklch, so mixing and ramps are perceptual rather than sRGB arithmetic.

```fsharp
let c = Color.ofHex "#0072B2"
printfn "oklch L: %.3f" (Color.toOklch c).L
printfn "contrast on white: %.2f" (Color.contrastRatio Color.white c)
```

### Categorical

`Palette.okabeIto` is the eight-colour CVD-safe set. `Palette.series` is the same set reordered so
the two weakest entries come last; `Palette.seriesDark` swaps the black for white.

```fsharp
printfn "series: %d colours" Palette.series.Length
```

### Sequential and diverging

`viridis`, `cividis`, `magma`, and an Oklab diverging ramp that is neutral at its midpoint.

```fsharp
printfn "diverging midpoint chroma: %.4f" (Color.toOklch (Ramp.at Palette.diverging 0.5)).C

let custom = Ramp.ofColors "blue-yellow" [| Palette.blue; Palette.yellow |]
printfn "custom at 0.5: %s" (Color.toHex (Ramp.at custom 0.5))
```

### The validator

`Palette.validate` simulates three dichromacies, reports the worst separation, and names the two
entries that were closest under it.

```fsharp
let onWhite = Palette.validate Color.white Palette.okabeIto
printfn "Okabe-Ito on white:  Ok=%b  min contrast %.2f at index %d"
    onWhite.Ok onWhite.MinContrast onWhite.MinContrastIndex

let onPanel = Palette.validate Theme.dark.PanelBackground Palette.seriesDark
printfn "seriesDark on dark:  Ok=%b  min contrast %.2f" onPanel.Ok onPanel.MinContrast
```

Okabe–Ito **fails** on white. It stays distinguishable under all three dichromacies, but two of
its eight colours are very light and its yellow lands at 1.32:1 against white, far below the 3:1
WCAG asks for graphical objects. The palette is not modified — a corrected Okabe–Ito is no longer
Okabe–Ito — it is reordered, and themes are validated against the background they are actually
drawn on.

---

## 11. Categories, and the channels beyond x and y

Not every axis is a quantity. `Chart.Category` takes one key per row and lays the distinct keys
out as evenly spaced bands, in the order they first appear. The y axis is dimensioned as
everywhere else.

```fsharp
let treatment = [| "control"; "treated"; "placebo"; "washout" |]
let harvest = [| 12.4<m>; 19.1<m>; 7.8<m>; 15.0<m> |]

let byTreatment =
    Chart.Category.bar U.metre treatment harvest
    |> Chart.withXLabel "treatment"
    |> Chart.withYLabel "yield"
```

Repeated keys are not aggregated — two rows with the same key are two marks in one band.
`Chart.Category.count` is the version that counts them, and `Chart.Category.scatter`,
`.boxplot` and `.violin` draw the same axis with a different mark.

A **bar chart's y axis always includes zero**, because a bar encodes magnitude by its length and a
baseline that is not zero misstates every comparison in the figure. `Chart.withYDomain` overrides
it where that was deliberate.

Beyond position, a figure has three more channels. Each attaches to the layer most recently added:

```fsharp
let cohort = t |> Array.mapi (fun i _ -> [| "A"; "B"; "C" |].[i % 3])

let byCohort =
    Chart.scatter U.second U.metre t x
    |> Chart.colorByKey cohort
    |> Chart.shapeByKey cohort
```

`colorByKey` and `shapeByKey` take a category and give one colour or one marker shape per level,
with a legend that names the levels. Using both is deliberate: shape is the encoding that survives
being printed in greyscale or read by a colour-blind reader, so it is worth spending alongside
colour rather than instead of it.

`colorBy` and `sizeBy` take a **quantity**, which means they take a unit witness:

```fsharp
let probe = t |> Array.map (fun v -> 280.0<K> + 2.5<K/s> * v)

let byTemperature =
    Chart.scatter U.second U.metre t x
    |> Chart.colorBy U.kelvin probe
    |> Chart.withColorLabel "T"
```

A continuous colour channel gets a **colorbar** rather than a legend, in a column beside the plot,
and it labels itself `T [K]` from the witness — the same derivation the axes use, including prefix
selection. `sizeBy` gets a key in the legend showing three marks across the domain. A row whose
channel value is missing or infinite is not drawn, and the count of such rows is recorded in the
rendered document rather than being silently absorbed.

---

## 12. Statistics

Each stat is a plain function over typed arrays in `Stat`, and each has a `Chart` constructor that
draws its result. Calling the function keeps the numbers; calling the chart draws them.

```fsharp
let sample = Array.init 300 (fun i -> (float ((i * 37) % 97) * 0.11) * 1.0<m>)

let spread = Chart.histogram U.metre sample
let smoothed = Chart.density U.metre sample
let cumulative = Chart.ecdf U.metre sample
```

`Chart.histogramWith` takes a bin count; `Stat.binIn`, `Stat.binWith` and `Stat.densityWith` take
a stated domain, bin count or bandwidth, and `Chart.ofHistogram` / `Chart.ofDensity` /
`Chart.ofEcdf` draw a result you computed yourself.

**The measures follow through the stat**, and this is where they say something a caller might not
expect. A histogram of metres is a *dimensionless count* against a metre axis. A density of metres
is in **reciprocal metres** — that is what makes it integrate to one — so its y axis reads `m⁻¹`,
and putting a density on the same panel as the sample it summarises raises rather than drawing,
because they are different physical quantities.

A least-squares fit is a layer:

```fsharp
let trend =
    Chart.scatter U.second U.metre t x
    |> Chart.addFit U.second U.metre t x
```

And the fit's slope is a *typed* number, which is the point of taking the stat separately:

```fsharp
let fitted = Stat.regression U.second U.metre t x

// A fit of metres against seconds has a slope in metres per second. The annotation is checked
// by the compiler, not by a convention.
let speed: float<m/s> = fitted.Slope
```

`Stat.summary` gives the five-number summary a box plot is drawn from — quartiles, Tukey whiskers
and the outliers past them — entirely in the sample's own unit. `Stat.quantile`, `Stat.count` and
`Stat.groupBy` are the small ones. Every stat drops non-finite input and reports how many values
it dropped.

A **loess** smooth is a layer too, with R's defaults: a local quadratic over the nearest 75% of the
points, tricube-weighted. `Stat.loess` returns the curve in the data's units, and `loessWith` and
`loessIn` state the span, the points and the degree.

```fsharp
let smoothTrend =
    Chart.scatter U.second U.metre t x
    |> Chart.addLoess U.second U.metre t x
```

A **bootstrap** gives a confidence interval for any statistic, in the statistic's unit. The seed
is an argument, and the interval carries it, because an interval that changes between two runs
of one script cannot be cited. Resampling draws from `Rng`, a specified generator, so a seed names
the same interval on every runtime.

```fsharp
let slopeOf (ts: float<s>[]) (xs: float<m>[]) = (Stat.regression U.second U.metre ts xs).Slope
let slopeInterval = Stat.bootstrapPairs 2026UL slopeOf t x

// The interval of a slope in m/s is in m/s.
let slowest: float<m/s> = slopeInterval.Lo
printfn "slope %.3f m/s, 95%% in [%.3f, %.3f], seed %d"
    (float slopeInterval.Estimate) (float slopeInterval.Lo) (float slopeInterval.Hi) slopeInterval.Seed
```

A **hexbin** counts scattered points into hexagons and reports a density in `1/('x·'y)`, which
is `density`'s result in two dimensions: positions in metres against times in seconds give a
colorbar in m⁻¹·s⁻¹.

```fsharp
let cloud = Chart.hexbin U.second U.metre t x
```

The hexagons are laid out before any panel exists, so they are regular on a plot area 1.6 times
as wide as it is tall. `Stat.hexbinIn` states the lattice instead.

---

## 13. The encoding block

When the data is a table, `plot` says what each column *means* in one place rather than
threading arrays through a chain of calls.

```fsharp
type Trial =
    { Time: float<s>
      Displacement: float<m>
      Mass: float<kg>
      Sample: string }

let trials =
    Array.init 60 (fun i ->
        let time = float i * 0.2<s>

        { Time = time
          Displacement = 0.5 * 9.81<m/s^2> * time * time
          Mass = (1.0 + float (i % 5) * 0.4) * 1.0<kg>
          Sample = [| "alpha"; "beta"; "gamma" |].[i % 3] })

let block =
    plot trials {
        x U.second (fun r -> r.Time)
        y U.metre (fun r -> r.Displacement)
        color (fun r -> r.Sample)
        size U.kilogram (fun r -> r.Mass)
        layerWith GeomPoint (Geom.withOpacity 0.7)
        fit
        name "run 1"
    }
    |> Chart.withTitle "Displacement by sample"
```

Each positional or continuous channel takes its witness beside the accessor, and the accessor is
checked against it — naming `r.Displacement` under `U.second` does not compile. Categorical
channels take no witness, because a category carries no dimension.

The block returns an ordinary `Figure`, so everything in the sections above composes onto it with
`|>` and none of it has to be restated as an operation.

The operations, in full:

| | |
|---|---|
| `x` `y` | The positional channels. Both take a witness. |
| `color` `shape` | A category drives colour or marker shape. |
| `colorBy` `colorByWith` `size` `sizeWith` | A quantity drives colour or size. Both take a witness. |
| `colorLabel` | The quantity name in front of a colorbar's unit. |
| `layer` `layerWith` | Draw the encoding with a geom. Repeatable, back to front. |
| `area` `areaWith` `bars` `barsWith` | The filled geoms, measured from zero. |
| `fit` `fitWith` | A least-squares line as an extra layer. |
| `histogram` `histogramWith` `density` `densityWith` `ecdf` | The distribution stats. |
| `facetWrap` `facetWrapIn` `facetScales` | One panel per level — see §14. |
| `key` | Each row's own name, so a mark keeps it when other rows come and go — see §18. |
| `name` `theme` `coord` | The series name, the theme, the coordinate system. |

Three rules worth knowing before you meet them:

* **A block with no `layer` is a scatter.** Two positional channels say where the points are, and
  points are how that is drawn.
* **Every layer of a block is one series**, so they share one colour and one legend entry. Two
  `layer` operations are two renderings of the same encoding — `layer GeomLine` beside
  `layer GeomPoint` is a line with markers, not two series.
* **A distribution stat replaces y.** `histogram`, `density` and `ecdf` take `x` alone and produce
  y themselves, so such a block refuses a `y` operation rather than ignoring it.

```fsharp
let distribution =
    plot trials {
        x U.metre (fun r -> r.Displacement)
        density
        name "displacement"
    }
```

Everything else is the last one written: a repeated `x`, a second stat, or a second `facetWrap`
replaces the first.

---

## 14. Facets

`facetWrap` splits the rows by a key and draws one panel per level, over **shared axes**.

```fsharp
let faceted =
    plot trials {
        x U.second (fun r -> r.Time)
        y U.metre (fun r -> r.Displacement)
        facetWrap (fun r -> r.Sample)
        layer GeomLine
    }
```

Sharing is what separates a facet from a page of unrelated charts: every panel reports the same x
and y domain, so a difference between panels is a difference in the data. `facetScales` narrows
that to `ShareX` or `ShareY` where one axis makes no cross-panel claim, and `facetWrapIn` states
the number of columns instead of taking the squarest grid that fits.

Anything that has to be identical across the grid is computed before the split — the level set and
its colours, a colorbar's scale, and a histogram's bin edges, which come from the undivided sample
so the panels bin alike:

```fsharp
let facetedHistogram =
    plot trials {
        x U.metre (fun r -> r.Displacement)
        histogram
        facetWrap (fun r -> r.Sample)
    }
```

`Chart.beside` and `Chart.above` are the other composition, for panels that share nothing — see
§8.

---

## 15. Fields

`Marey.Fields` draws quantities defined over a plane: a potential, an electric field, the flow of
a dynamical system. A field arrives as a function or as gridded data, and either way it is used as
a **grid**: a function is sampled once, at a resolution you state, and every mark reads the grid.

```fsharp
#r "nuget: Marey.Fields"

// Two line charges, +1 at (0.6, −0.1) m and −1 at (−0.6, 0.1) m: a potential in volts.
let dipolePotential (p: Vec2<m>) =
    let plus = Vec2.norm { X = p.X - 0.6<m>; Y = p.Y + 0.1<m> }
    let minus = Vec2.norm { X = p.X + 0.6<m>; Y = p.Y - 0.1<m> }
    (log (float minus) - log (float plus)) * 1.0<V>

let fieldXs = Grid.linear -1.8<m> 1.8<m> 72
let fieldYs = Grid.linear -1.2<m> 1.2<m> 48
let phi = Field.sampleScalar fieldXs fieldYs dipolePotential
```

`Grid.linear` and `Grid.log` space nodes evenly on a linear or a log axis; `ScalarGrid.create` and
`VectorGrid.create` take data that is already gridded. A sample that is not finite (the potential
at a charge) is kept, counted by `ScalarGrid.missing`, and never drawn.

### Derivatives, and the units they come out in

`grad`, `div`, `curl`, `laplacian` and `jacobian` are three-point differences on the grid, exact
for a quadratic and second-order otherwise. Their units are the types':

```fsharp
let efield =
    let g = Field.grad phi
    { g with U = Array.map (~-) g.U; W = Array.map (~-) g.W }

// The gradient of a potential in volts over metres is in V/m, and its divergence in V/m².
let charge: ScalarGrid<m, m, V/m^2> = Field.div efield
```

### Drawing a scalar field

A heatmap, its contours and filled bands, each with a colorbar in the field's unit. `Chart.overlay`
puts one figure's layers over another's panel:

```fsharp
let equipotentials =
    Field.heatmap U.metre U.metre Derived.volt phi
    |> Chart.overlay (Field.contoursAt [| -1.0<V>; -0.5<V>; 0.0<V>; 0.5<V>; 1.0<V> |] U.metre U.metre phi)
    |> Chart.withYLabel "y"

let banded = Field.bands U.metre U.metre Derived.volt phi
```

Every field mark is computed when the figure is compiled, against the panel's final scales, so a
heatmap lines up with its contours and with any other layer on the panel, on a log axis as well
as a linear one. Contour levels default to the nice numbers the field's own range would be ticked
at (`Field.contours`). Saddle cells are resolved by the asymptotic decider, and bands are filled
even-odd, so holes stay holes.

### Drawing a vector field

```fsharp
let coarse = Field.sample (Grid.linear -1.8<m> 1.8<m> 19) (Grid.linear -1.2<m> 1.2<m> 13) (fun p ->
    let d = 1e-4<m>
    let ex = (dipolePotential { p with X = p.X - d } - dipolePotential { p with X = p.X + d }) / (2.0 * d)
    let ey = (dipolePotential { p with Y = p.Y - d } - dipolePotential { p with Y = p.Y + d }) / (2.0 * d)
    { X = ex; Y = ey })

let arrows = Field.quiverBy U.metre U.metre fieldUnit coarse
let lines = Field.streamlines U.metre U.metre efield
let texture = Field.licBy U.metre U.metre fieldUnit efield
```

- **`quiver`** draws an arrow per node, its length the magnitude through the quiver's own scale,
  because the arrows are in V/m and the axes in metres. A key in the corner says what one length
  means, as a round value with its prefix. `quiverBy` also colours by magnitude. Sample a quiver
  coarser than a heatmap.
- **`streamlines`** are evenly spaced on the page, 16 px apart by default (`streamlinesWith`),
  integrated on the panel so they follow the field on a log axis, with arrowheads downstream.
  There is no randomness, so the same field draws the same lines. A larger figure draws more
  lines, the same distance apart.
- **`lic`** is a line-integral convolution: noise smeared along the streamlines, so the flow shows
  at every pixel. It is a raster at the plot area's resolution (`licWith` states an oversample
  for print), and its noise is seeded, so a seed, a field and a plot size always give one image.
  It costs about 70 ms for a 480 × 300 figure.

### A phase space

A pendulum's state is an angle and an angular velocity, two units, so `Field2` with one unit on
both axes cannot hold it. A `Flow` can:

```fsharp
let swing: Flow<rad, rad/s, s> = fun th om -> struct (om, -9.81<1/s^2> * sin (float th) * 1.0<rad>)
let phaseGrid = Field.sampleFlow (Grid.linear -6.5<rad> 6.5<rad> 105) (Grid.linear -9.0<rad/s> 9.0<rad/s> 73) swing

// Phase-space area is conserved: the divergence is zero, in 1/s.
let liouville: ScalarGrid<rad, rad/s, 1/s> = Field.div phaseGrid
```

`curl` and `laplacian` need one unit on both axes, so over a phase space they do not compile, and
that is correct: ∂ω̇/∂θ and ∂θ̇/∂ω are in different units and their difference does not exist.

```fsharp-error
let nonsense = Field.curl phaseGrid
```

`Phase.fixedPoints` finds where the flow vanishes and classifies each point by its linearisation:
saddle, stable or unstable node or focus, centre, or degenerate. The eigenvalues are in `1/'t`:

```fsharp
let omegaUnit = U.div Derived.radian U.second

for p in Phase.fixedPoints phaseGrid do
    printfn "%s" (FixedPoint.describe Derived.radian omegaUnit U.second p)

let portrait = Phase.portrait Derived.radian omegaUnit phaseGrid
```

That prints a centre at 0 and ±2π and a saddle at ±π, the eigenvalues ±3.13i s⁻¹ and ±3.13 s⁻¹.
They are written in s⁻¹ rather than Hz because an eigenvalue is a rate. `Phase.portrait` draws
streamlines, both nullclines (named `dθ/dt = 0` and `dω/dt = 0` after the axes) and the fixed
points, marked by kind.

**A centre is a judgement.** Linearisation gives a centre purely imaginary eigenvalues and a weak
focus nearly imaginary ones, and only the nonlinear terms tell them apart. A point is called
`centre (linear)` when the real part is under a thousandth of the imaginary part;
`Phase.fixedPointsWith` states another tolerance.

### A field in motion

A field that changes with time is an `Anim<Figure>` whose frames are built from each instant's
grid, and it renders like any other animation (§17–§19):

```fsharp
let growing (time: float<s>) =
    Field.sampleScalar fieldXs fieldYs (fun p -> (1.0 + float time) * dipolePotential p)

let growingFilm =
    Anim.ofSeconds 2.0<s> (fun time -> Field.heatmap U.metre U.metre Derived.volt (growing time))
    |> Film.ofFigures 16 (Chart.size 480.0 300.0)
```

**The colour scale holds still for the whole film.** Each continuous colour channel is drawn
against the union of its range over the film's sampled frames, not against the frame's own range,
so a field that grows reads as growing, not as the same picture renormalised. A quiver's arrow
scale is held the same way. `Chart.withColorDomain` states a range instead. It is the right
choice when a few nodes near a singularity would otherwise set the range, as they do for the
potential above:

```fsharp
let stated =
    Field.heatmap U.metre U.metre Derived.volt phi
    |> Chart.withColorDomain Derived.volt -2.0<V> 2.0<V>
```

Streamlines are recomputed from each frame's field, and their seeds follow the field, so a film of
a rotating field redraws a different set of lines every frame. Contours from `Field.contours`
choose their levels per frame, so state them with `contoursAt` in a film.
`examples/dipole.fsx` is a rotating dipole, filmed and encoded.

---

## 16. Output: SVG, TikZ, PNG, PDF

`Marey.Plot` does not reference a backend. A backend is a function passed in at render time, which
is why the same figure goes to either without the figure knowing either exists.

```fsharp
let asSvg = fall |> Chart.render SvgBackend.render (Chart.size 720.0 450.0)

let asTikz =
    fall
    |> Chart.withTheme Theme.tikz
    |> Chart.render TikzBackend.render (Chart.size 320.0 200.0)

printfn "svg root: %b" (asSvg.StartsWith "<svg")
printfn "tikz has siunitx: %b" (asTikz.Contains "qty")
```

To a file:

```fsharp
let out = System.IO.Path.GetTempPath()

fall |> Chart.toSvg SvgBackend.render (System.IO.Path.Combine(out, "fall.svg")) (Chart.size 800.0 500.0)

fall
|> Chart.withTheme Theme.tikz
|> Chart.toFile TikzBackend.render (System.IO.Path.Combine(out, "fall.tex")) (Chart.size 320.0 200.0)
```

The TikZ output is a `tikzpicture` meant to be pasted into a document. For one that compiles on
its own:

```fsharp
let doc = TikzBackend.standalone asTikz
printfn "standalone: %b" (doc.Contains "documentclass")
```

Both backends are deterministic — two renders of one figure are byte-identical:

```fsharp
printfn "deterministic: %b" (asSvg = (fall |> Chart.render SvgBackend.render (Chart.size 720.0 450.0)))
```

Constructs TikZ cannot draw degrade visibly rather than silently: a gradient becomes its Oklab
midpoint. A raster becomes a comment naming what was skipped and at what size through
`TikzBackend.render`, and is drawn for real through `TikzBackend.renderWithAssets`, which returns
the document *and* the sidecar `.png` files it refers to.

### PNG and PDF

`Marey.Backend.Skia` is the third backend, and the first that produces **bytes** rather than a
string. It is also the first that measures text properly, which is the subject of the next part.

```fsharp
let png = fall |> Chart.render SkiaBackend.renderPng (Chart.size 720.0 450.0)
let pdf = fall |> Chart.render SkiaBackend.renderPdf (Chart.size 720.0 450.0)

printfn "png magic: %b" (png.[1..3] = "PNG"B)
printfn "pdf magic: %b" (pdf.[0..4] = "%PDF-"B)
```

To a file, `Chart.toFileBytes` is the byte-shaped sibling of `Chart.toSvg` — a separate name
because writing text has an encoding and writing bytes does not, and a PNG that picked up a
byte-order mark on the way to disk would not open.

```fsharp
fall
|> Chart.toFileBytes SkiaBackend.renderPng (System.IO.Path.Combine(out, "fall.png")) (Chart.size 800.0 500.0)

fall
|> Chart.toFileBytes SkiaBackend.renderPdf (System.IO.Path.Combine(out, "fall.pdf")) (Chart.size 800.0 500.0)
```

The PDF is **vector**, despite coming out of the backend the design calls the raster one: paths
stay paths and glyphs stay glyphs. Its page is the requested extent converted exactly — a
400 × 250 px figure is a 300 × 187.5 pt page — and it carries no timestamp, so two renders of one
figure are byte-identical.

`SkiaOptions` is the knob: resolution, background colour, antialiasing, a font file, and the PDF's
document information.

```fsharp
let opts =
    { SkiaOptions.Default with
        Background = Color.transparent
        Pdf = { PdfMetadata.Default with Title = "free fall" } }

let sprite = fall |> Chart.render (SkiaBackend.renderPngWith opts) (Chart.size 400.0 250.0)
printfn "transparent sprite: %d bytes" sprite.Length
```

### Which font a PNG is drawn in, and why it matters

SVG names a font family and lets whatever opens the file resolve it. TikZ ignores the name and
inherits the document's face. Skia has to **pick one**, from what is installed on the machine
doing the rendering — so the same figure is a slightly different PNG on your laptop and on
someone else's.

That is not hidden. A backend reports what it opened:

```fsharp
let backend = new SkiaBackend()
fall |> Chart.renderTo backend (Chart.size 400.0 250.0)

for face in backend.ResolvedFaces |> Array.distinctBy (fun f -> f.Family) do
    printfn "asked for %s, drew with %s" (face.Requested.Split(',').[0]) face.Family
```

`Matched = None` on a reported face means every entry in the theme's stack missed and Skia's
default was used — the figure looks like nothing the theme asked for, and it says so rather than
leaving you to notice.

For output that has to be identical on two machines, state a file and resolution is skipped
entirely:

```fsharp
// let pinned = { SkiaOptions.Default with FontFile = Some "/path/to/Inter-Regular.ttf" }
```

### When one face is not enough

A face holds the scripts it holds. Liberation Sans has no CJK, so `気温 (°C)` measured against it
is two missing-glyph boxes and a unit — and a PNG of that axis title has two empty rectangles in
it, where a browser opening the same figure as SVG would have walked the whole stack and found a
face that has them.

So the measurer splits a run by coverage: **per codepoint, the first face that has it**, which is
the rule CSS font matching uses. Each piece is shaped in its own face and the advances are summed.
Nothing above the backend knows this is happening — `ITextMeasurer` still answers a width — and a
run one face covers, which is every run in a Latin figure, is one piece and exactly the number it
always was.

What is new is the report. `ResolvedFace` says which faces covered what, and which codepoints
nothing covered:

```fsharp
let title = "気温 (°C)"

let titleFont: FontSpec =
    { Family = Theme.Sans
      Size = 9.0<pt>
      Weight = 400
      Italic = false }

let fallbackAware = new SkiaTextMeasurer()

printfn "%s: %.2f px" title (float ((fallbackAware :> ITextMeasurer).Measure titleFont title).Advance)

for face in fallbackAware.ResolvedFaces do
    for used in face.Fallbacks do
        printfn "  %s covered %d codepoint(s) %s does not have" used.Family used.Covered.Length face.Family

    if face.Missing.Length > 0 then
        let names = face.Missing |> Array.map (sprintf "U+%04X") |> String.concat ", "
        printfn "  nothing on this machine covers %s -- drawn as .notdef" names
```

Run that on a machine with a CJK face installed and it names the face; run it on one without and
it names the codepoints. Neither is silent, which is the point: a figure with boxes in it says so
before you find them in the PNG.

The machine is doing the choosing there, which is the previous section's problem again, one level
down — so the same answer is available. A stated **list** pins the whole chain:

```fsharp
// let pinned =
//     { SkiaOptions.Default with
//         FontFiles = [ "/path/to/Inter-Regular.ttf"; "/path/to/NotoSansJP-Regular.ttf" ] }
```

`FontFiles` is the reproducible answer to a mixed-script figure: those faces, in that order, and a
reported `.notdef` past the end of them, with the machine never consulted. `FontFile` remains the
single-face answer and **does not fall back** — one face is the whole promise, and a codepoint it
lacks is a box. Setting both is an error rather than a precedence rule.

Two things the split costs, both small and both worth knowing. Kerning is not applied across a
segment boundary — but boundaries fall where coverage fails, which is where the script changes,
and no face kerns across a script change. And a run's vertical band is the **largest** of the
faces it uses, so a CJK label is taller than the Latin label beside it. That is also what a
browser does, and reserving only the Latin band would clip the ideographs.

### Real text shaping

`Marey.Core` ships an *approximate* measurer: a Helvetica advance-width table, no kerning, no
ligatures, accurate to a few per cent. It exists because the core may not take a dependency, and
both text backends advertise `TextShaping = false` honestly.

The Skia backend advertises `true`, and it is the first thing in the project for which the flag
means anything:

```fsharp
printfn "svg shapes text:  %b" (SvgBackend() :> IBackend).Caps.TextShaping
printfn "skia shapes text: %b" (new SkiaBackend() :> IBackend).Caps.TextShaping
```

The consequence is worth stating plainly, because it looks like a bug the first time it is
noticed: **two backends of this library place an axis title at different coordinates for the same
figure**. Measured text is not estimated text. At 12 px, `AVATAR` is 48.01 px under the
approximation and 44.51 px shaped in Arial — the kerning in `AV`, `VA`, `TA` and `AT` — and a real
face reserves about a quarter more vertical room than Helvetica's ascent fraction estimates. Both
reach layout, so the panel rectangle moves. The shaped number depends on the face, which is the
previous section's point: run the snippet below and you will get whatever your machine resolved.

Layout is a measure–arrange pass against `ITextMeasurer`, and `Chart.renderTo` uses the backend's
own. That one seam is the whole mechanism — not a line of the layout code changed when the shaper
arrived — and both measurers are ordinary values you can ask directly:

```fsharp
let spec: FontSpec =
    { Family = Theme.Sans
      Size = 9.0<pt>
      Weight = 400
      Italic = false }

let shaper = new SkiaTextMeasurer() :> ITextMeasurer

printfn "approximated: %.2f px" (float (TextMeasure.approx.Measure spec "AVATAR").Advance)
printfn "shaped:       %.2f px" (float (shaper.Measure spec "AVATAR").Advance)
```

---

## 17. Animation

`Marey.Anim` holds one type, and its whole design is in the signature: **an animation is a pure
function of time, not a mutation.**

```fsharp
#r "nuget: Marey.Anim"

let animClimb = Anim.tween 2.0<s> 0.0<m> 100.0<m>

printfn "at 1 s:      %.1f m" (float (animClimb |> Anim.at 1.0<s>))
printfn "at 90%%:      %.1f m" (float (animClimb |> Anim.atFraction 0.9))
printfn "past the end: %.1f m" (float (animClimb |> Anim.at 9.0<s>))
```

Nothing is stepped and nothing accumulates, so frame 900 costs what frame 1 costs and you can ask
for them in any order. That is what a scrubbable timeline needs and what a mutable scene graph
cannot give you.

`tween` goes through the same `Interpolate` witness the rest of the library uses, so it tweens
whatever Core can interpolate — a `float<'u>`, a `Vec2`, a `Rect`, a `Color` (through Oklab, so it
does not pass through grey), and an affine transform.

### Composing

Six combinators, and the two that matter are parallel and sequential composition.

```fsharp
let animFade = Anim.tween 0.5<s> 0.0 1.0

// Parallel: the duration is the **maximum**, and the shorter one holds its final value.
let animBoth = Anim.map2 (fun x a -> x, a) animClimb animFade
printfn "both, at 2 s: %A" (animBoth |> Anim.at 2.0<s>)

// Sequential: the duration is the **sum**.
let animTwice = Anim.andThen animClimb (Anim.tween 1.0<s> 100.0<m> 0.0<m>)
printfn "up then down lasts %.1f s" (float (Anim.duration animTwice))
```

`Anim.stretch` scales a duration and `Anim.ease` reparameterises the clock. `Anim.pure'` is a
constant of zero duration.

### The clock clamps; the fraction does not

`Anim.at` clamps its argument to `[0, duration]`, which is what makes the hold above well-defined
and `andThen` total at every instant. The *normalised* clock is deliberately not clamped, and that
is the entire easing catalogue:

```fsharp
let animBack = Anim.tween 1.0<s> 0.0 1.0 |> Anim.ease Ease.backOut

printfn "backOut overshoots to %.3f" (animBack |> Anim.atFraction 0.35)
printfn "and still lands on %.3f" (animBack |> Anim.atFraction 1.0)
```

Twenty-eight easings ship — quad, cubic, quart, sine, expo, circ, back, elastic and bounce, each
in, out and in-out, plus linear. One caveat, because it is surprising rather than wrong: a
**colour** overshoot is gamut-clamped on the way back to sRGB, and alpha is clamped
unconditionally, so an eased `fadeIn` cannot overshoot its opacity.

### Morphing a shape

`Morph.paths` interpolates two paths through a common form: both are flattened to polylines and
resampled by **arc length**, so a square becomes a triangle at a steady speed rather than racing
along one edge and crawling down the next.

```fsharp
let animSquare =
    [| MoveTo { X = 0.2<nd>; Y = 0.2<nd> }
       LineTo { X = 0.8<nd>; Y = 0.2<nd> }
       LineTo { X = 0.8<nd>; Y = 0.8<nd> }
       LineTo { X = 0.2<nd>; Y = 0.8<nd> }
       ClosePath |]

let animTriangle =
    [| MoveTo { X = 0.5<nd>; Y = 0.9<nd> }
       LineTo { X = 0.9<nd>; Y = 0.1<nd> }
       LineTo { X = 0.1<nd>; Y = 0.1<nd> }
       ClosePath |]

let animMorph = Morph.anim Morph.defaults 1.0<s> animSquare animTriangle
printfn "the midpoint has %d segments" (Anim.atFraction 0.5 animMorph).Length
```

Two things about it look like bugs the first time you see them, and neither is one. Both are
pinned by tests rather than argued away.

<img src="images/morph-pairing.svg" alt="Two morphs: a square twisting into itself, and two discs swapping places" width="720">

**Top row — a closed ring's start point is not rotated to match its partner.** Those are the same
square twice, written from different corners; the morph pairs sample zero with sample zero, so it
twists a quarter turn on the way rather than sitting still. **Bottom row — subpaths pair by
index.** That is one picture written in two subpath orders: the ends agree that nothing moved, and
in between the two discs cross through each other and trade size. Matching rings by rotation, or
subpaths by proximity, are both real algorithms and neither is built.

Three other things worth knowing: the output is always a **polyline**, so a morphed curve is a
flattened one; `Morph.paths a b 0.0` is `a` *in the common form* rather than `a` itself, which is
what `Morph.prepare` hands back and what the still frame before a morph should be drawn from; and
`MorphOptions` states the sample count, the flattening resolution and what happens when an open
path meets a closed one, rather than hiding them.

### Animating a figure

An `Anim<Figure>` is what you usually want, and rendering one has exactly one subtlety. Since the
plot rectangle is what is **left over** after the widest tick label has taken its room, laying out
each frame on its own makes the panel *breathe*: a y domain climbing past 100 gains a digit on
every label at once, the gutter widens, and the axis crawls sideways while the data is the thing
that was supposed to move. Freezing the first frame's layout has the opposite failure — a later
frame's labels are wider than the gutters that were sized for frame zero.

So lay out **once, against the union of the animation's sampled states**, and draw every frame
inside an arrangement no frame can overflow:

```fsharp
let animGrow (u: float) =
    let ts = Array.init 24 (fun i -> float i * 0.4<s>)
    let xs = ts |> Array.map (fun t -> t / 1.0<s> * (1.0 + 40.0 * u) * 1.0<m>)
    Chart.line U.second U.metre ts xs

let animExtent = Chart.size 640.0 400.0
let animShot = Chart.arrangeUnion 32 TextMeasure.approx animExtent animGrow

let animFrame (u: float) = Chart.toSceneIn TextMeasure.approx animShot (animGrow u)

let animPanels = [| 0.0; 0.5; 1.0 |] |> Array.map (animFrame >> Scene.count)
printfn "every frame is the same scene shape: %b" (Array.distinct animPanels |> Array.length = 1)
printfn "the shot was sized over %d samples" animShot.Samples
```

The same union pins the **unit prefix**, so a zoom crossing a decade cannot step `mm → µm`
mid-shot and change the axis title while the data moves. Tick labels still recompute every frame;
it is the rectangle and the prefix that are held.

The sample count is stated rather than hidden because the union is only as good as it: an
animation with a spike between two samples can still overflow. Thirty-two is a sound default, and
`animShot.Samples` reports what was used. One accepted consequence: a still frame lifted out of an
animation has slightly more gutter than the same `Figure` rendered on its own, because it is
wearing the whole shot's clothes.

`Marey.Anim` mentions no `Figure` and depends only on `Marey.Core`, which is why the sampler above
is a plain `float -> Figure` rather than an `Anim<Figure>`: tweening a `float<m>` should not make
you install the grammar. `fun u -> Anim.atFraction u anim` is the bridge.

---

## 18. The storyboard

An animation is usually a *sequence*: something arrives, it changes, an axis zooms, one point is
picked out. Written as a bare `float -> Figure` that means working out which step you are in and
how far through it you are, by hand, every time. The storyboard block does that arithmetic.

```fsharp
open Marey.Storyboard

let storyT = Array.init 12 (fun i -> float i * 1.0<s>)

let storyRise =
    Chart.scatter U.second U.metre storyT (storyT |> Array.map (fun t -> float t * 1.0<m>))

let storyBend =
    Chart.scatter U.second U.metre storyT (storyT |> Array.map (fun t -> float t * float t * 0.25<m>))

let storyIntro =
    scene {
        let! curve = draw storyRise |> over 1.2<s>
        do! wait 0.5<s>

        do!
            parallel'
                [ curve |> morphTo storyBend |> over 2.0<s>
                  curve |> zoomToX (0.0, 8.0) |> over 2.0<s> ]

        do! highlight curve.[7] |> over 0.6<s>
    }

printfn "the storyboard runs for %g s" (float (Storyboard.duration storyIntro))
```

The block is a value. Nothing has been measured, nothing rendered, no time has passed.

### What `let!` binds

A **handle**: a name for something on stage, plus the duration it arrived over. Not the animation
itself — `curve` is used three times after it is bound, every time to refer to something already
drawn. A handle that *was* the animation would make `morphTo` composition rather than reference,
and the storyboard would lose the ability to say "that thing, again".

The duration is not decoration either. It is the default for every verb applied to that handle,
so `do! highlight curve.[7]` with no `over` runs for as long as `curve` took to appear — a
default that comes from your storyboard rather than from a constant inside the library.

### The verbs

| | |
|---|---|
| `draw fig` | put a figure on stage and reveal it. A zero-duration draw is a cut |
| `over d` | how long the step takes |
| `wait d` | hold |
| `parallel' [ … ]` | run branches together from the same starting stage |
| `morphTo fig` | interpolate what is on stage into another figure |
| `zoomToX (lo, hi)`, `zoomToY` | animate an axis domain |
| `highlight h.[i]` | grow one mark and ring it in the theme's foreground |
| `fadeIn h` | fade a named subtree in |

`parallel'` takes the duration of its **longest** branch, and the shorter ones hold their final
state through the tail — which is `Anim.map2`'s rule from §17, inherited rather than restated.

**`morphTo` interpolates the data, not the drawn geometry.** A `Figure` carries its own scales,
so two figures are two coordinate systems; morphing one's paths into the other's would put half
of the first figure's geometry on the second's axes, and the tick labels would be telling the
truth about neither. Interpolating the series instead leaves a real `Figure` at every instant,
whose domain, ticks and unit prefix all come from the numbers actually on screen. The two sides
need not be the same length — both are resampled to the longer — and the units may differ, as
long as the dimensions match: a series in kilometres morphs into one in metres by converting,
not by reading the numbers across.

### `curve.[7]`, and the node that had to exist for it

A scatter of 100 000 points is **one** `Markers` node. That is the single biggest performance
decision in the IR, and it is also why point 7 has nothing to highlight: there is no node with
its name on it.

Naming one splits that batch three ways — the marks before it, the mark itself under its own
key, the marks after — which costs two extra nodes rather than a hundred thousand. Nothing is
split until something is named, so every figure that highlights nothing is exactly the figure it
was.

The key is `Keys.mark`'s spelling, and the index is the datum's **original row**, not its
position among the drawn marks. Those differ the moment a non-finite value earlier in the series
goes undrawn, and a key built from the drawn position would quietly name a different point from
there on.

You do not need a storyboard to use it — `Chart.addressMarks` is the still version, which is
what circling an outlier in a static figure wants:

```fsharp
let storyRinged = storyRise |> Chart.addressMarks [| 7 |]
let storyRingedScene = Chart.toScene (Chart.size 640.0 400.0) storyRinged

printfn
    "mark 7 has a node of its own: %b"
    (Scene.tryFindKey (Keys.mark (Keys.layer (Keys.panel 0 0) 0) 7) storyRingedScene).IsSome
```

Only a scatter can honour it. A line is one `Path`, so there is no node between its vertices to
name, and asking for one raises rather than drawing nothing.

### Keys the data supplies

A row number stops naming the same datum as soon as a row before it is removed. When the rows have
names of their own, say so with `key` in the encoding block (or `Chart.withKeys`), and each mark is
keyed by its row's name instead:

```fsharp
type Station = { Name: string; Year: float<s>; Level: float<m> }

let stations =
    Array.init 12 (fun i -> { Name = sprintf "S%d" i; Year = float i * 1.0<s>; Level = float (i * 7 % 5) * 1.0<m> })

let byStation (rows: Station[]) =
    plot rows {
        x U.second (fun r -> r.Year)
        y U.metre (fun r -> r.Level)
        key (fun r -> r.Name)
    }

let withoutS3 = stations |> Array.filter (fun r -> r.Name <> "S3")

let storyFiltered =
    scene {
        let! sites = draw (byStation stations) |> over 0.5<s>
        do! sites |> morphTo (byStation withoutS3) |> over 1.0<s>
        do! highlight sites.["S6"] |> over 0.4<s>
    }

printfn "the filtered story runs for %g s" (float (Storyboard.duration storyFiltered))
```

**A morph between two keyed scatters pairs by key.** Each station moves to the station of the same
name, so the ones that stayed hold still, and `S3` fades out where it was. A datum only the target
has fades in where it is going. Without keys the morph pairs by position, and every station after
the removed one would slide one place to the left. `sites.["S6"]` names a datum by its key;
`sites.[6]` on a keyed scatter names the datum that was row 6.

A key is one escaped segment of the mark's path, so a `/` in the data cannot reach into another
layer's marks, and two rows with one key are refused where the keys are given, naming the key and
both rows. Only a scatter pairs by key; every other geom pairs by index, as before.

### Rendering

`animate` samples the storyboard, sizes one arrangement against the union of everything it saw
(§17), and hands back an `Anim<Node>` you can seek:

```fsharp
let storyAnim = animate 32 (Chart.size 640.0 400.0) storyIntro

let storyRect (n: Node) =
    n
    |> Scene.fold
        (fun acc x ->
            match x with
            | Transform(a, _) -> a :: acc
            | _ -> acc)
        []

let storyPlots =
    [| 0.0; 0.25; 0.5; 0.75; 1.0 |]
    |> Array.map (fun u -> storyRect (storyAnim.At(u * storyAnim.Duration)))
    |> Array.distinct

printfn "the panel holds still while the data moves: %b" (storyPlots.Length = 1)

printfn
    "the highlighted mark is addressed by the end: %b"
    (Scene.tryFindKey (Keys.mark (Keys.layer (Keys.panel 0 0) 0) 7) (storyAnim.At storyAnim.Duration))
        .IsSome
```

<img src="images/storyboard-filmstrip.svg" alt="Six frames of one storyboard: a ringdown fading in, morphing onto its envelope, and one mark picked out" width="720">

Six frames of `examples/storyboard.fsx`, which writes exactly this. The panel rectangle is the
same in all six — the layout was sized once — while the y tick labels run `-30 … 30` in the first
three and `0 … 40` in the last three. That is the line D72 draws: the *rectangle* is frozen, its
*contents* are not, because an animation whose numbers change and whose labels do not is not
showing the numbers changing.

`animateWith` takes a measurer, for the same reason `Chart.toSceneWith` does — a PNG or PDF
render should be arranged with the backend's own shaping (§16).

If the picture is not what you want out, `Storyboard.stages` gives the animation of `Stage`
values themselves, each carrying the `Figure` on stage at that instant. That is the door to a
still frame, a caption, or a different renderer entirely.

---

## 19. Frames

`Anim.At` is a direct evaluation, so rendering an animation is one `map` over the frame times.
There is no scheduler and no replay: frames come out in any order, on any number of threads, and
a frame rendered on its own is the frame rendered in sequence.

A **`Film`** is the pair a renderer needs — the scene at every instant, and the layout it was
arranged inside:

```fsharp
open Marey.Storyboard

let filmPoints =
    let ts = Array.init 12 (fun i -> float i * 1.0<s>)
    Chart.scatter U.second U.metre ts (ts |> Array.map (fun t -> float t * 2.0<m>))

let filmStory =
    scene {
        let! filmCurve = draw filmPoints |> over 0.6<s>
        do! filmCurve |> zoomToY (0.0, 40.0) |> over 1.0<s>
    }

let film = Film.ofStory 32 (Chart.size 320.0 220.0) filmStory

printfn "%g s on a %g x %g surface" (float film.Duration) (float film.Extent.Width) (float film.Extent.Height)
```

The surface is not an argument anywhere below this line. `Film.Extent` is the extent the film's
shot was arranged against, so a film cannot be rendered at a size its layout was not sized for —
which is silent when it happens and looks like a picture in the corner of a page.

`Frames.render` evaluates every frame and hands them back. `Frames.write` and `Frames.writeText`
also put them on disk, numbered:

```fsharp
let frameDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marey-guide-frames")

let filmSeq =
    film
    |> Frames.writeText FramePass.Concurrent SvgBackend.render (FrameTarget.into frameDir ".svg") 24.0

printfn "wrote %d frames as %s" filmSeq.Files.Length filmSeq.Pattern
printfn "nothing left over from an earlier render: %b" (Array.isEmpty filmSeq.Stale)
```

`Pattern` is the `frame-%05d.svg` shape an encoder is pointed at, and `Stale` is every file
matching it that *this* render did not write. That second one is not tidiness: a directory holding
a previous, longer render still has frames past the end of this one, `ffmpeg -i frame-%05d.png`
reads until the first gap, and the encode quietly gains a tail from a shot that no longer exists.
The files are reported rather than deleted, because deleting a directory's contents is not a
rendering function's decision to make.

### The frame clock

`Frames.times fps duration` is `round(duration * fps)` instants at `i / fps`. **The final instant
is not a frame.** A frame occupies the interval that starts at its own time, so a 2 s film at
30 fps is 60 frames ending at 1.9667 s; a sixty-first frame at `t = 2` would hold the final state
for one frame longer than the film asked for, which is a visible stutter in a loop.

```fsharp
printfn "60 frames, last at %g s: %b" (float (Array.last (Frames.times 30.0 2.0<s>))) (Frames.count 30.0 2.0<s> = 60)
```

### Serial and concurrent are the same frames

`FramePass.Concurrent` is `Array.Parallel.map` over the frame times; `FramePass.Serial` is
`Array.map`. They produce the same bytes in the same order, and a gate asserts it on both the
SVG and the PNG backend.

Two rules make that true, and they are the contract in `Backend.fs` rather than a property of the
backends that happen to be in the box:

- **An `IBackend` is single-threaded.** `Begin`/`Draw`/`Finish` is a state machine over a mutable
  surface. What crosses threads is a *renderer function*, `Extent<px> -> Node -> 'a`, and such a
  function must be safe to call concurrently. `SvgBackend.render` and `SkiaBackend.renderPng`
  both are, by constructing their backend inside the call.
- **An `ITextMeasurer` must be thread-safe, and one of them serves the whole film.** Not for
  speed: font resolution walks a family stack against whatever is installed (§16), so a measurer
  per thread is a chance for one frame of a video to be set in a different typeface from the frame
  before it — invisible in a single frame, invisible in review, and unfixable after the render.

### One measurer, memoised

Sharing one measurer means sharing whatever lock it needs, and layout asks it about every tick
label, legend entry and axis title. `MemoTextMeasurer` answers that: it wraps any measurer and
remembers `(FontSpec, string)`, so no string is ever measured twice and the hit path is a
lock-free dictionary read.

```fsharp
let filmMemo = MemoTextMeasurer TextMeasure.approx
let filmMeasured = Film.ofStoryWith 32 filmMemo (Chart.size 320.0 220.0) filmStory
Frames.render FramePass.Concurrent SvgBackend.render 24.0 filmMeasured |> ignore

printfn "%d distinct strings, %d measurements" filmMemo.Entries filmMemo.Measurements
```

Pass one of these when the measurer underneath costs something — which means whenever the film is
rendered through Skia, where every measurement is a HarfBuzz shaping run behind a mutex.

How much it saves is bounded by the *distinct strings in the shot*, not by the frames: the
arrangement is frozen once (§18), tick labels are round numbers drawn from a small set, and
quadrupling the frame rate on a two-decade zoom moved that count by one.

---

## 20. Video

`Frames.write` leaves a numbered directory, and a numbered directory is already something: every
editor imports one, every encoder reads one, and it is what a paper's supplementary material
wants anyway. `Marey.Tools.Video` turns it into one file by running `ffmpeg`.

```fsharp
#r "nuget: Marey.Tools.Video"
```

**A subprocess rather than a binding, and licence is why.** `FFmpeg.AutoGen` links `libav*` into
your process — LGPL at best, GPL as soon as the build carries x264 — which puts an audit on
everyone who references your project. A subprocess boundary keeps ffmpeg's licence ffmpeg's
problem. The managed wrappers shell out too, so what they cost is a dependency and what they buy
is not writing one `ProcessStartInfo`.

`ffmpeg` is looked for on `PATH`, once per process, and reported:

```fsharp
printfn "%s" (Video.report ())

let vidDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marey-guide-video")

let vidSeq =
    film
    |> Frames.write FramePass.Concurrent SkiaBackend.renderPng (FrameTarget.into vidDir ".png") 25.0

let vidOut = System.IO.Path.Combine(vidDir, "shot.mp4")

match Video.mux MuxOptions.Default vidSeq vidOut with
| MuxRan run when run.Succeeded -> printfn "encoded %d frames into %s" run.FrameCount run.Output
| MuxUnavailable why -> printfn "%s" why
| other -> printfn "%A" other
```

**A missing ffmpeg is an outcome, not an exception.** `MuxUnavailable` says what to install and
says the frames are still on disk, because they are. That is the same choice §16 makes about a
font it cannot find and the raster backend makes about a native library: degrade, and name what
would fix it.

### The command is a value

`Video.args` is a pure function from the options and the sequence to the argument vector, so you
can print it, diff it, or paste it into a shell:

```fsharp
printfn "ffmpeg %s" (String.concat " " (Video.args MuxOptions.Default vidSeq vidOut))
```

Two positions in that vector are load-bearing, and both are silent when wrong:

- **`-framerate` comes before `-i`.** It is an *input* option — it says what rate the images
  already are, and every one of them is kept. The same number after `-i` is an *output* option:
  the images are read at the demuxer's default 25 fps and then resampled, which drops frames or
  duplicates them. Twenty-four frames at 12 fps is twenty-four frames written correctly and
  **thirteen** with the flag moved. At 24 fps both spellings happen to agree, which is exactly
  why this is worth stating.
- **`-fflags +bitexact` comes after `-i`**, so it reaches the muxer rather than the demuxer. It
  is what stops the container carrying `encoder=Lavf63.1.101` — a stamp that does not vary
  between two runs on one machine, and does vary between ffmpeg builds.

### Read it back

Exit status is a weak gate: an encoder that put thirteen of twenty-four frames in the file exits
zero. `Video.inspect` decodes the result with `ffprobe` and says what is actually in it.

```fsharp
match Video.inspect vidOut with
| Inspected info ->
    printfn "%s, %d x %d, %d frames at %g fps" info.Codec info.Width info.Height info.FrameCount info.Fps
    printfn "names no ffmpeg build: %b" (List.isEmpty info.BuildTags)
| other -> printfn "%A" other
```

`Duration` is `FrameCount / Fps`, which is one frame period longer than the last frame's own
time — §19's rule arriving in the container.

### What it refuses, and why

`Video.check` starts no process, so you can ask it before rendering an hour of frames:

- **Leftover frames.** `FrameSequence.Stale` is every file matching the pattern this render did
  not write, `-i frame-%05d.png` reads until the first gap, and the encode would quietly gain a
  tail from a longer shot that no longer exists. Refused by default; `MuxOptions.AllowStale`
  overrides it.
- **Odd pixel dimensions.** `yuv420p` subsamples chroma and needs even width and height. The
  size is read out of the first frame's own header rather than derived from the extent, because
  the raster backend rounds an extent *up*: a 320.5 px figure is a 321 px bitmap, and 321 is the
  number that decides. The default pixel format is `yuv420p` and not negotiable by accident —
  H.264 from PNG otherwise encodes as `yuv444p`, which is a legal file that QuickTime, Safari
  and PowerPoint all refuse to play, so the author sees it work and the audience sees nothing.

---

## 21. Mathematical typesetting

A visualization library that cannot set `\frac{\partial \vec{E}}{\partial t}` is not much use for
physics, and there is no good .NET answer to setting one. `Marey.Tools.Tex` takes the answer
everybody actually uses: it runs TeX.

**What comes back is geometry, and that is the whole point.** The package walks a scene, finds
every text run that carries a TeX form, sets it with `latex`, converts it to vector outlines with
`dvisvgm`, and replaces the run with a filled `Path`. After that there is nothing left for a
backend to know — SVG, TikZ and Skia all draw the formula with the code they already had.

```fsharp
#r "nuget: Marey.Tools.Tex"

printfn "%s" (Typeset.report ())
```

That prints where `latex` and `dvisvgm` were found, or the sentence naming what to install. It is
worth printing, because the failure mode here is quiet: a figure that typeset and a figure that
did not both render and both look reasonable.

`TextRun.Tex` is the field it reads, and the axis compiler already fills it — `Chart.withUnitStyle
Siunitx` puts `\qty{}{\metre\per\second\squared}` on the axis title. You can also put a formula
there yourself:

```fsharp
let formulaFont: FontSpec =
    { Family = Theme.Serif
      Size = 18.0<pt>
      Weight = 400
      Italic = false }

let formulaScene: Node =
    Text
        { Text = "dE/dt = curl B"                     // always set, and never optional
          Tex = Some @"$\frac{\partial \vec{E}}{\partial t} = \nabla \times \vec{B}$"
          At = { X = 0.5<nd>; Y = 0.5<nd> }
          Font = formulaFont
          Paint = Solid(Color.rgb 0.1 0.1 0.12)
          Anchor = AnchorMiddle
          Baseline = BaselineAlphabetic
          Rotation = 0.0<rad> }

let formulaExtent = Chart.size 420.0 120.0
let drawn, note = Typeset.sceneOrPlain TexOptions.Default formulaExtent formulaScene
printfn "%s" note
```

`sceneOrPlain` always hands back something to draw. On a machine with TeX that is the outlines; on
one without it is the scene that went in, and `note` says which. **A missing TeX installation
costs the typesetting and nothing else**, because `TextRun.Text` is always set — a run's plain
form is not optional, which is exactly what makes this degradation possible.

```fsharp
printfn
    "the formula is now %s"
    (match drawn with
     | Path _ -> "outlines"
     | Text _ -> "still text, because there is no TeX on this machine"
     | _ -> "something else")
```

### It fails one formula at a time

A snippet TeX rejects does not take the figure with it. That run keeps its text, and
`Typeset.scene` reports it by name with TeX's own complaint, while every other formula still
typesets:

```fsharp
let broken: Node =
    Text
        { Text = "broken"
          Tex = Some @"$\notacommand{x}$"
          At = { X = 0.5<nd>; Y = 0.2<nd> }
          Font = formulaFont
          Paint = Solid(Color.rgb 0.1 0.1 0.12)
          Anchor = AnchorMiddle
          Baseline = BaselineAlphabetic
          Rotation = 0.0<rad> }

match Typeset.scene TexOptions.Default formulaExtent (Group [| formulaScene; broken |]) with
| Typeset result -> printfn "typeset %d, failed %d, engine runs %d" result.Typeset result.Failed.Length result.EngineRuns
| TypesetUnavailable why -> printfn "%s" why
```

This is why each snippet gets its own engine run rather than all of them sharing one document: one
document is about four times faster on a figure with ten distinct labels, and one bad label in it
costs you every label.

### What it costs, and the one thing it approximates

About 105 ms per **distinct** snippet, memoised on the source — so the same axis title on twelve
facets is one run. An ordinary figure carries two, and pays about a fifth of a second.

**The cache lives for one call unless you hold one.** `Typeset.scene` collapses repetition inside
a scene, not across scenes, so typesetting an animation frame by frame with it pays that fifth of
a second per frame: 1082 ms for five frames, measured. For a film, create one `TexCache` and pass
it to `Typeset.sceneCached` in every frame's renderer. The first frame typesets and the rest
reuse it: 217 ms for the same five frames. The cache is keyed on everything that shapes an
outline, the preamble and the tools included, and it is safe across the threads frames render on.

The font size is applied as a *scale* on the outline rather than as a second engine run, so one
formula at 9 pt and at 18 pt costs one invocation between them. A TeX engine asked for 18 pt
directly would pick a different optical design, with different stroke weights; the difference is
not visible in a plot, and `TexOptions.BaseSize` is where the trade is written down.

`TexOptions` also carries the preamble — `amsmath`, `textcomp` and `siunitx` by default — which is
replaced rather than appended to, because a preamble is a document's own business.

---

## 22. Notebooks

In [Verso](https://github.com/DataficationSDK/Verso) with the `Marey.Verso` extension installed, a
cell whose value is a `Figure` displays as a figure. No `.Show()`, no save-then-open, no display
call. Twelve types render: `Figure`, `Film`, `Node`, `Color`, `Ramp`, `Color[]`, `PaletteReport`,
`Dim`, `U<'u>`, `IScaleErased`, and a live figure, `Live` or `Recorder` (§23).

Without the extension, render to SVG and Verso's built-in `SvgFormatter` — which claims any string
beginning with `<svg` — draws it. That path needs nothing installed.

`Marey.Html` is the renderer, and it is host-neutral: `something -> string`, with no reference to
any notebook host, so it can be called anywhere.

```fsharp
let fragment = Html.figure fall
printfn "fragment is a div: %b" (fragment.StartsWith "<div")
printfn "swatch: %b" (Html.color Palette.vermillion |> String.length > 0)
```

### A film plays in the cell

A `Film` displays as a **flipbook**: its frames sampled into stills, stacked in one SVG, and
cycled. It needs no encoder, writes no file, and plays wherever the notebook renders.

```fsharp
let flipbook = Html.film film

printfn "the flipbook is one svg: %b" (flipbook.Contains "<svg")
printfn "it animates without a stylesheet: %b" (flipbook.Contains "<animate" && not (flipbook.Contains "<style"))
```

That last line is the whole design. A `<style>` element in a notebook output cell **is not scoped
to the cell** — it restyles the reader's entire document and stacks up once per execution — so CSS
`@keyframes`, the obvious way to build a flipbook, is the one mechanism this may not use.
`<animate>` is an SVG *element*: it travels inside the fragment it animates and cannot escape it.

The frame count is the cost. Each frame is roughly 11 KB of markup, so `Html.film` samples
`Html.FlipbookFrames` — 48 — which is about half a megabyte, or 40 KB once the notebook is
compressed. That is a **budget, not a frame rate**: the effective rate is `48 / duration`, so a
two-second film is smooth and a six-second one is visibly stepped. Say so explicitly when you want
a different trade:

```fsharp
printfn "a coarse flipbook is smaller: %b" ((Html.filmAt 12 film).Length < (Html.filmAt 48 film).Length)
```

The film renders at **its own extent** — `Film.Extent`, the surface its layout was arranged
against — and not at `Html.defaultSize`. Every frame's text was measured for that surface, and the
whole of §18's "the panel does not breathe" is a claim about it, so there is no size argument to
get wrong.

Asking for a single frame gives a still rather than an animation of one:

```fsharp
printfn "one frame is a still: %b" (not ((Html.filmAt 1 film).Contains "<animate"))
```

**What this is not.** It is not a video and not a GIF. Both would put `Marey.Tools.Video`, and
therefore ffmpeg, on the display path — so a reader without ffmpeg would see a still where a
colleague sees motion, from the same notebook, with nothing saying so. Use
[`Marey.Tools.Video`](#19-video) when you want a file to send someone; use this when you want to
see what you just built.

See [`notebooks/README.md`](notebooks/README.md) for installation and
[`notebooks/marey-tour.dib`](notebooks/marey-tour.dib) for the tour.

---

## 23. Interaction

`Marey.Interact` puts controls on a figure. In a notebook the cell becomes a live figure: the
reader moves a slider, the kernel runs the view again, and the figure that comes back is drawn by
the same code as every other figure in this guide.

```fsharp
#r "nuget: Marey.Interact"

let lineCharge = U.div Derived.coulomb U.metre
let epsilon0 = 8.8541878128e-12<F/m>

// Two line charges, +λ and −λ, a distance 2·half apart on the x axis.
let pairPotential (lambda: float<C/m>) (half: float<m>) (p: Vec2<m>) : float<V> =
    let plus = Vec2.norm { X = p.X - half; Y = p.Y }
    let minus = Vec2.norm { X = p.X + half; Y = p.Y }
    lambda / (2.0 * System.Math.PI * epsilon0) * log (minus / plus)

let pairMap (lambda: float<C/m>) (half: float<m>) (contoured: bool) =
    let grid = Field.sampleScalar fieldXs fieldYs (pairPotential lambda half)

    let map =
        Field.heatmap U.metre U.metre Derived.volt grid
        |> Chart.withColorDomain Derived.volt -100.0<V> 100.0<V>
        |> Chart.withYLabel "y"

    if contoured then
        map |> Chart.overlay (Field.contoursAt [| -50.0<V>; -20.0<V>; 0.0<V>; 20.0<V>; 50.0<V> |] U.metre U.metre grid)
    else
        map

let explorer =
    interact {
        let! lambda = Control.slider lineCharge "λ" 0.5e-9<C/m> 5.0e-9<C/m> 2.0e-9<C/m>
        and! half = Control.slider U.metre "d/2" 0.2<m> 1.0<m> 0.6<m>
        and! contoured = Control.checkbox "contours" true
        return pairMap lambda half contoured
    }
```

| | The view receives | The label reads |
|---|---|---|
| `Control.slider u label lo hi init` | `float<'u>` | the value in `'u`, under one prefix chosen from the range |
| `Control.sliderBy u label lo hi step init` | `float<'u>` | as above, with the places the step needs |
| `Control.select label [ name, value; … ] init` | the chosen value | its name |
| `Control.checkbox label init` | `bool` | `on` or `off` |

**A control is typed.** The λ slider hands the view `float<C/m>`, so passing it where the view
wants metres does not compile, and its label is written in its own unit: `2 nC·m⁻¹`, under one
prefix chosen from the range so that it does not switch prefix in the middle of a drag.

**The block is applicative**: `let! … and! …`, then `return`. A control cannot be chosen by another
control's value, so the set of controls is known before the view runs, and the widget's shape is
not a function of its state.

### In a notebook

A cell whose value is a `Live` displays as a page: the controls, the figure, and a short script.
Moving a control posts the new value to the kernel, the kernel runs the view and sends back the
figure, or only the parts of it that changed when that is at most half the size. On the figure
itself:

- **drag** a box to zoom onto it, on log axes and phase spaces as well as linear ones;
- **shift-drag** to pan;
- **double-click** to go back to the view's own domains;
- **hover** to read the data under the pointer, in a box in the plot's top corner.

The page formats no number. Every label and every reading is written by the kernel, in the units
the axes are written in.

A live figure needs a host that can talk back to the page. In Verso that is a release whose
formatter context carries output channels (newer than 1.2.2). In any other host, and in a saved
notebook, the same page is drawn with its controls disabled and a line saying it is static. A save
writes the page at the state the reader left it in.

**The view is yours, and it must be pure.** It is run again for every change, it is run for states
the reader never reaches (below), and a message can arrive while another cell is executing. Steps
for one figure run one at a time; a view that reads or writes shared mutable state can still see
another cell's half-finished work.

### A live figure is a value

What the notebook does is a fold over the reader's messages, and the same functions run without
a notebook:

```fsharp
let startState = Live.init explorer
let movedState = Live.step explorer startState (Set(0, 4.0e-9))

printfn "%s" (String.concat ", " (Live.shown explorer movedState))
printfn "the same message gives the same state: %b" (Live.step explorer startState (Set(0, 4.0e-9)) = movedState)
printfn "a message for no control changes nothing: %b" (obj.ReferenceEquals(Live.step explorer startState (Set(7, 1.0)), startState))
```

`Live.step` is total: a message naming no control, a value with no legal neighbour, or a size that
is not one returns the state it was given. A page in someone's browser sent it, and a step that can
throw is a widget that can die. `Live.view` gives the figure for any state, and `Live.scene` the
scene it is drawn as.

Zoom and the readout are messages too, in the figure's px:

```fsharp
let plotArea = (Live.panels explorer movedState).[0].Plot
let middle = { X = (plotArea.X0 + plotArea.X1) / 2.0; Y = (plotArea.Y0 + plotArea.Y1) / 2.0 }

let zoomedState =
    Live.step explorer movedState (Zoom { X0 = middle.X - 120.0<px>; Y0 = middle.Y - 60.0<px>; X1 = middle.X; Y1 = middle.Y })

match Live.step explorer zoomedState (Hover middle) |> Live.readout explorer with
| Some reading -> reading.Lines |> Array.iter (printfn "%s")
| None -> printfn "the pointer is off the plot"
```

A reading is written in the prefix and unit the axis's ticks are written in, to the places one px
resolves: a log axis gets more places near its bottom than near its top. A categorical axis reads
the level under the pointer. The reading is placed over the plot, outside the figure, so it never
moves the layout and redrawing the figure is never what a hover costs.

### The plot area holds still

The figure is laid out once for the whole widget, against the union of the states its controls
reach at their extremes: each slider's ends, every option, both states of a checkbox, every
combination while there are at most 64 of them. So the plot area does not move while the reader
drags, though a tick label gains a digit. A state that still does not fit grows the arrangement,
once, and `LiveState.Grown` records it:

```fsharp
printfn "re-arranged %d times so far" movedState.Grown.Length
```

A zoom is the exception, because it changes what the axes say. It re-arranges at once, so that
kilometres zoomed to a few hundred metres read in metres, as a still figure of that domain would.

### Recording

A session is its messages, because the step is pure. `Live.recorder` gives a handle that displays
as the live figure and keeps what the reader did, with the page's own times:

```fsharp
let recorder = Live.recorder explorer
// Display `recorder` in a cell, move things, then:
let kept = Recorder.stop recorder
```

`Live.record` replays a log through the step, and `Live.film` turns the states into a `Film`, laid
out over every state the recording reached. Time starts at the reader's first act, not when the
page appeared. A log is also data, and `LiveLog` writes it as JSON and reads it back exactly:

```fsharp
let session =
    Recording.ofEvents
        [| 0.0<s>, Set(0, 1.0e-9)
           0.8<s>, Set(0, 3.0e-9)
           1.6<s>, Set(1, 0.9)
           2.4<s>, Set(2, 0.0) |]

let replay = Live.film explorer (Live.record explorer session)

printfn "the replay runs for %g s" (float replay.Duration)
printfn "a log reads back exactly: %b" (LiveLog.ofJson (LiveLog.toJson session) = Ok session)
```

The film is an ordinary `Film`, so §19 writes its frames, §20 encodes it, and in a notebook it
plays as a flipbook (§22). The pointer is not recorded: it changes nothing a film draws.

`examples/interact.dib` is a notebook with this figure in it.

---

## 24. The scene IR

Everything above compiles to one closed ten-case `Scene` IR, and every backend consumes only that.
It is the level at which a figure that came out wrong can be inspected.

```fsharp
let scene = Chart.toScene (Chart.size 480.0 300.0) fall
printfn "nodes: %d   primitives: %d" (Scene.count scene) (Scene.primitiveCount scene)
```

The ten cases are `Path`, `Text`, `Markers`, `Raster`, `Group`, `Transform`, `Clip`, `Opacity`,
`Keyed` and `Annotated`. The split between `count` and `primitiveCount` is the whole point of `Markers`: a
scatter of any size is **one** node.

```fsharp
let many = Array.init 20000 (fun i -> float i * 0.001<s>)
let manyY = many |> Array.map (fun v -> sin (float v * 10.0) * 1.0<m>)
let bigScene = Chart.toScene (Chart.size 480.0 300.0) (Chart.scatter U.second U.metre many manyY)
printfn "20 000 points -> %d nodes, %d primitives" (Scene.count bigScene) (Scene.primitiveCount bigScene)
```

`Keyed` and `Annotated` carry identity and semantics through to the output — SVG gets `id`s and
ARIA roles, TikZ gets `% marey-key:` and `% marey-role:` comments — so a rendered figure can be
diffed and read as text.

The keys are hierarchical, and a figure assigns them: `figure`, then `figure/panel-0-0`, then
`figure/panel-0-0/layer-0` for each layer of each panel. `Keys` spells them and `Scene.mapKey`
rewrites one subtree by name, leaving every other subtree exactly as it was — which is what makes
"fade this one series" a change to the path down to that series rather than to the figure.

```fsharp
let faded =
    scene |> Scene.mapKey (Keys.layer (Keys.panel 0 0) 0) (Scene.opacity 0.25)

printfn "found: %b   nodes: %d -> %d"
    (Scene.tryFindKey (Keys.panel 0 0) scene |> Option.isSome)
    (Scene.count scene)
    (Scene.count faded)
```

`Scene.opacity` fades a **composited** subtree, which is not the same as setting alpha on each
shape inside it: the two differ wherever the group's own shapes overlap, because per-shape alpha
paints the overlap twice. For a single shape, `Color.A` is still the simpler tool.

---

## 25. Escape hatches

**Bring your own scales.** `Chart.ofSeries` takes an already-scaled typed series, so the scales are
the caller's and everything downstream still works.

```fsharp
let raw = Chart.series "measured" U.second U.metre t x
let manual = Chart.ofSeries GeomLine raw
```

**Erase deliberately.** `Erase` is the single boundary where `float<'u>` becomes a runtime value
carrying a `Dim`. Below it there are no measures, which is what lets one backend serve every
unit.

```fsharp
let erased = Erase.scale logScale
printfn "erased scale carries: %s" (Dim.show erased.Dim)
```

**Bring your own backend.** A renderer is `Extent<px> -> Node -> string`, and `Chart.render` takes
exactly that. Anything of that shape works, with no registration step:

```fsharp
let countingRenderer (_: Extent<px>) (node: Node) : string =
    sprintf "%d nodes" (Scene.count node)

printfn "%s" (fall |> Chart.render countingRenderer (Chart.size 100.0 100.0))
```

For a stateful backend there is `IBackend` and `Chart.renderTo`, which is the path
`SvgBackend.renderToFile` takes.

---

## What is not here yet

Simulation (integrating a system over physical time: trajectories, pathlines, particles), further
backends (a browser canvas and a GPU one), interaction outside a notebook, picking a datum under
the pointer, and typesetting without a TeX installation are all intended, and none of them exist
yet.
