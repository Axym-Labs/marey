# Marey — the guide

Everything the library does today, in the order you are likely to need it, with a runnable
example for each part.

**Every `fsharp` block below is executed by the test suite.** `GuideTests.fs` extracts them in
order, concatenates them into one script and runs it under `dotnet fsi`; if a block stops
compiling or starts throwing, that test fails. A guide that has drifted from the library is worse
than no guide, and this is the cheapest way to make drift impossible. Blocks marked `fsharp-error`
are the exception — those are shown *because* they do not compile, and the checker skips them.

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
11. [Output: SVG, TikZ, files](#11-output-svg-tikz-files)
12. [Notebooks](#12-notebooks)
13. [The IR, when you need to look underneath](#13-the-ir-when-you-need-to-look-underneath)
14. [Escape hatches](#14-escape-hatches)

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
| `Marey.Core` | You want the `Scene` IR, colour, scales and geometry, and nothing else. Zero dependencies. |
| `Marey.Units` | SI + 22 derived + 18 non-SI unit witnesses, and self-labelling axes. |
| `Marey.Plot` | `Chart`, geoms, themes, palettes, layout. No backend reference — see §11. |
| `Marey.Backend.Svg` | Rendering to SVG. |
| `Marey.Backend.Tikz` | Rendering to `tikzpicture` source for LaTeX. |
| `Marey.Html` | HTML renderings of nine Marey types, for a notebook cell. Host-neutral. |
| `Marey.Verso` | The [Verso](https://github.com/DataficationSDK/Verso) notebook extension. Installed *into the host*, not referenced from your code. |

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

Nothing was configured, and the figure already knows:

- that the x axis is **seconds** and the y axis is **metres**, from the two unit witnesses;
- that `x` is a length at all — `0.5 * 9.81<m/s^2> * t * t` has type `float<m>` and the compiler
  worked that out, nobody annotated it;
- what the axis titles read (`t [s]`, `x [m]`), what the tick values are, and which SI prefix to
  use for the magnitudes actually present.

That last point is worth demonstrating rather than asserting. The same call on nanometre-scale
data relabels itself:

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
This does not compile, and is here to show what the error looks like:

```fsharp-error
// error FS0001: Type mismatch. Expecting a 'float<m> []' but given a 'float<s> []'
Chart.line U.second U.metre t t
```

The interesting case is the one that *is* well-typed. Two separately-correct series can still be
wrong to put on one axis, and that is caught when the figure is compiled rather than when it is
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

Note *which* error that is. Not "cannot convert K to m" — the arrays are each fine. It is the
mistake a type system cannot see, because it only exists once you decide the two belong on one
axis.

### The unit witnesses

`U<'u>` is the runtime witness for a compile-time measure: it carries the SI dimension vector, so
the library can canonicalise, prefix and label without parsing strings.

```fsharp
printfn "newton dim:  %s" (Dim.show (U.dim Derived.newton))
printfn "joule:       %s" (U.symbol Derived.joule)
printfn "celsius affine: %b" (U.isAffine NonSi.degreeCelsius)
```

`degreeCelsius` is the interesting witness: affine, so its conversion to SI is not a factor alone.

Composing witnesses gets the *measure* right and the *name* wrong, and the difference is the
reason the registry exists. `Derived.newtonByComposition` is built with `U.mul`/`U.div` from base
witnesses rather than stated, and it is in the library precisely to stand next to the named one:

```fsharp
printfn "named:    %s" (U.symbol Derived.newton)
printfn "composed: %s" (U.symbol Derived.newtonByComposition)
printfn "same dimension: %b" (U.dim Derived.newton = U.dim Derived.newtonByComposition)
```

The composed symbol is `kg·m/s²` — correct, and not what anyone labels an axis with. The registry
is keyed by the `Dim` itself, so the canonical name is recovered from the physics rather than by
matching strings — and it comes back in all four spellings the renderers need:

```fsharp
let canonical = UnitRegistry.tryCanonical UnitRegistry.si (U.dim Derived.newtonByComposition)
printfn "canonical: %A" (canonical |> Option.map (fun u -> u.Unicode, u.Ascii, u.Siunitx))
```

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

The common case needs none of them directly: pick the kind on the figure and the domain follows
from the data.

```fsharp
let decades = [| 1.0<m>; 10.0<m>; 100.0<m>; 1000.0<m>; 10000.0<m> |]
let response = [| 1.0<V>; 0.5<V>; 0.25<V>; 0.125<V>; 0.0625<V> |]

let logFigure =
    Chart.line U.metre Derived.volt decades response
    |> Chart.withXLog
    |> Chart.withTitle "Response against distance"
```

Build one directly when you want the domain under your control:

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

Five, each with a constructor that starts a figure and an `add*` that layers onto the last panel.

```fsharp
let categories = [| 1.0<s>; 2.0<s>; 3.0<s>; 4.0<s> |]
let counts = [| 3.0<m>; 7.0<m>; 4.0<m>; 9.0<m> |]
let sigma = [| 0.4<m>; 0.6<m>; 0.3<m>; 0.8<m> |]

let lineOnly    = Chart.line     U.second U.metre t x
let scatterOnly = Chart.scatter  U.second U.metre t x
let areaOnly    = Chart.area     U.second U.metre t x
let barOnly     = Chart.bar      U.second U.metre categories counts
let errorOnly   = Chart.errorbar U.second U.metre categories counts sigma
```

`errorbar` is two layers — the bars and the centre points — and only one of them appears in the
legend. That is a single call because an error bar without its centre is not a thing anyone wants.

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

A style you set by hand survives a theme change; one you did not is recoloured. That asymmetry is
deliberate, and is what makes `withTheme` safe to apply last:

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

Titles are derived, and everything is overridable.

```fsharp
let labelled =
    fall
    |> Chart.withXLabel "elapsed"
    |> Chart.withYLabel "drop"
```

The unit *spelling* is a presentation choice, so it lives on the theme with a per-axis override.
That is what makes selecting `Theme.tikz` sufficient on its own to get `siunitx` markup:

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
| `Theme.presentation` | Large type, for four metres away. |
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
matters for layout as much as for looks: the family string is what the text measurer reads to
decide how much room a label needs, and for the TikZ backend — which emits no family at all and
lets the document supply the face — it is the *only* thing the family does.

Every theme can be measured, and the shipped ones are:

```fsharp
for theme in Theme.all do
    let r = Theme.validate theme
    printfn "%-13s text %6.2f  axis %6.2f  grid %5.2f  Ok=%b"
        r.Theme r.TextContrast r.AxisContrast r.GridContrast r.Ok
```

`GridContrast` is reported and deliberately *not* floored — a gridline that competes with the data
is chartjunk. `TextContrast` and `AxisContrast` are floored at WCAG AAA and 3:1, and a test runs
every shipped theme through this so the library cannot ship a theme that fails its own validator.

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

§9 asks for a validator "because a physics library that renders fields in jet is embarrassing".
It simulates three dichromacies, reports the worst, and names the two entries that were closest
under it.

```fsharp
let onWhite = Palette.validate Color.white Palette.okabeIto
printfn "Okabe-Ito on white:  Ok=%b  min contrast %.2f at index %d"
    onWhite.Ok onWhite.MinContrast onWhite.MinContrastIndex

let onPanel = Palette.validate Theme.dark.PanelBackground Palette.seriesDark
printfn "seriesDark on dark:  Ok=%b  min contrast %.2f" onPanel.Ok onPanel.MinContrast
```

Okabe–Ito **fails** on white, and that is the honest answer rather than a bug. It stays
distinguishable under all three dichromacies, but two of its eight colours are very light and its
yellow lands at 1.32:1 against white — far below the 3:1 WCAG asks for graphical objects. The
library does not "fix" the palette, because a corrected Okabe–Ito is not Okabe–Ito. It reorders,
and the themes are validated against the background they are actually drawn on.

---

## 11. Output: SVG, TikZ, files

`Marey.Plot` does not reference a backend. A backend is a function you pass in, which is why the
same figure goes to either without the figure knowing either exists.

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

Both backends are deterministic — two renders of one figure are byte-identical, which is what
makes the golden suite meaningful:

```fsharp
printfn "deterministic: %b" (asSvg = (fall |> Chart.render SvgBackend.render (Chart.size 720.0 450.0)))
```

TikZ degrades rather than lying when it meets something it cannot draw: a gradient becomes its
Oklab midpoint, a raster becomes a comment saying what was skipped and at what size.

---

## 12. Notebooks

In [Verso](https://github.com/DataficationSDK/Verso) with the `Marey.Verso` extension installed, a
cell whose value is a `Figure` displays as a figure. No `.Show()`, no save-then-open, no display
call. Nine types render: `Figure`, `Node`, `Color`, `Ramp`, `Color[]`, `PaletteReport`, `Dim`,
`U<'u>` and `IScaleErased`.

Without the extension, render to SVG and Verso's built-in `SvgFormatter` — which claims any string
beginning with `<svg` — draws it. That path needs nothing installed.

`Marey.Html` is the renderer, and it is host-neutral: `something -> string`, with no reference to
any notebook host. You can call it anywhere.

```fsharp
let fragment = Html.figure fall
printfn "fragment is a div: %b" (fragment.StartsWith "<div")
printfn "swatch: %b" (Html.color Palette.vermillion |> String.length > 0)
```

See [`notebooks/README.md`](notebooks/README.md) for installation and
[`notebooks/marey-tour.dib`](notebooks/marey-tour.dib) for the tour.

---

## 13. The IR, when you need to look underneath

Everything above compiles to one closed nine-case `Scene` IR, and every backend consumes only
that. Reaching it is how you find out why a figure is not what you expected.

```fsharp
let scene = Chart.toScene (Chart.size 480.0 300.0) fall
printfn "nodes: %d   primitives: %d" (Scene.count scene) (Scene.primitiveCount scene)
```

The nine cases are `Path`, `Text`, `Markers`, `Raster`, `Group`, `Transform`, `Clip`, `Keyed` and
`Annotated`. The split between `count` and `primitiveCount` is the whole point of `Markers`: a
scatter of any size is **one** node.

```fsharp
let many = Array.init 20000 (fun i -> float i * 0.001<s>)
let manyY = many |> Array.map (fun v -> sin (float v * 10.0) * 1.0<m>)
let bigScene = Chart.toScene (Chart.size 480.0 300.0) (Chart.scatter U.second U.metre many manyY)
printfn "20 000 points -> %d nodes, %d primitives" (Scene.count bigScene) (Scene.primitiveCount bigScene)
```

`Keyed` and `Annotated` carry identity and semantics through to the output — SVG gets `id`s and
ARIA roles, TikZ gets `% marey-key:` and `% marey-role:` comments — so a rendered figure can be
diffed and read rather than only looked at.

---

## 14. Escape hatches

**Bring your own scales.** `Chart.ofSeries` takes an already-scaled typed series, so you choose the
scales and everything downstream still works.

```fsharp
let raw = Chart.series "measured" U.second U.metre t x
let manual = Chart.ofSeries GeomLine raw
```

**Erase deliberately.** `Erase` is the single boundary where `float<'u>` becomes a runtime value
carrying a `Dim`. Below it there are no measures, by design — that is what lets one backend serve
every unit.

```fsharp
let erased = Erase.scale logScale
printfn "erased scale carries: %s" (Dim.show erased.Dim)
```

**Bring your own backend.** A renderer is `Extent<px> -> Node -> string`, and `Chart.render` takes
exactly that. Anything with that shape works, with no registration step:

```fsharp
let countingRenderer (_: Extent<px>) (node: Node) : string =
    sprintf "%d nodes" (Scene.count node)

printfn "%s" (fall |> Chart.render countingRenderer (Chart.size 100.0 100.0))
```

For a stateful backend there is `IBackend` and `Chart.renderTo`, which is the path
`SvgBackend.renderToFile` takes.

---

## What is not here yet

M1 is plotting. The roadmap in [`design.md`](design.md) §14 has animation (`Anim<'a>` as a pure
function of time), fields and simulation, the Skia/Fable/GPU/video backends, and the
grammar-of-graphics and scene DSLs. None of those exist today, and this guide grows when they do.
