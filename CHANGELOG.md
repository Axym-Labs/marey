# Changelog

Notable changes to Marey. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

All eight packages are versioned and released together, so a version number identifies the whole
set rather than any one package.

**Pre-1.0, and versioned accordingly.** The API is still free to move. Breaking changes are
listed under `Changed` or `Removed` with the reason and the replacement, and until 1.0 they may
land in a minor release.

## [Unreleased]

**M2.5 — *Raster*.** An eighth package, and the first output that is not text.

`Marey.Backend.Skia` renders a figure to PNG or to a vector PDF without a TeX installation, and
is the first backend that measures text properly rather than estimating it. Nothing in the
existing API changed shape; `Chart.render` gained a type parameter it already behaved as if it
had.

### Added

**`Marey.Backend.Skia`.** PNG through `SkiaBackend.renderPng`, PDF through
`SkiaBackend.renderPdf`, and the `IBackend` protocol through `SkiaBackend` with
`Result : byte[]`. All ten IR cases, including `Raster`, which this is the first backend to draw
natively. `SkiaOptions` carries the resolution, the background, antialiasing, an optional font
file and the PDF's document information.

The PDF is **vector** — paths stay paths and glyphs stay glyphs — and its page is the requested
extent converted exactly: a 400 × 250 px figure is a 300 × 187.5 pt page. It carries no
timestamp, so two renders of one figure are byte-identical.

**Real text shaping.** `SkiaTextMeasurer` implements `ITextMeasurer` over HarfBuzz, and
`Capabilities.TextShaping` is `true` for the first time in the project. `Marey.Core`'s
approximation is a Helvetica advance-width table with no kerning; measured against it, `AVATAR`
at 12 px is 48.01 px estimated and 44.51 px shaped in Arial, and a real face reserves about a
quarter more vertical room than the estimate. Both reach `Figure.arrangement`, which is the point.

**`ResolvedFace` and `SkiaBackend.ResolvedFaces`.** A PNG's glyph widths come from a face on the
machine that rendered it. That is reported rather than hidden: for every distinct font the scene
asked for, which entry in the theme's CSS stack matched and which family was actually opened.
`SkiaOptions.FontFile` states a file and skips resolution, which is the only way two machines
produce the same pixels.

**`Chart.image` and `Geom.image`.** An RGBA image placed in data space, with witnesses on both
axes like every other constructor. This is the first producer of a `Raster` node anywhere above
the IR — the case existed since M0 and nothing made one.

**`TikzBackend.renderWithAssets` and `renderToFileWithAssets`.** A `RenderedDocument` — the
`tikzpicture` plus the sidecar `.png` files it refers to through `\includegraphics`. TikZ's
`Capabilities.Rasters` is now `true`, the first capability in the project to go from `false` to
`true`. `TikzBackend.render` is unchanged and still degrades a raster to a comment, because a
bare `string` has nowhere to put a sidecar.

**`Chart.toFileBytes`**, for a renderer whose artifact is bytes. Separate from `Chart.toFile`
because writing text has an encoding and writing bytes does not.

**`Marey.Png`**, promoted out of the SVG backend now that it has a second consumer. The
zero-dependency PNG encoder — CRC-32, Adler-32, stored deflate — that writes TikZ's sidecar
without pulling a native dependency into a package that must not have one.

### Changed

- **`Chart.render` is generic in the renderer's result**: `(Extent<px> -> Node -> 'a) ->
  Extent<px> -> Figure -> 'a` rather than returning `string`. Source-compatible — every existing
  call still infers `string` — and it is what lets a byte-producing backend through the same
  door. `Chart.toFile` and `Chart.toSvg` are unchanged.
- **Two backends of this library now place an axis title at different coordinates for the same
  figure.** Layout is a measure–arrange pass against `ITextMeasurer`, and the Skia backend's is
  not the other two's. This is what measuring text properly means, and it is not a defect.

---

**M3 — *Motion*.** In progress, and shipping in the same release as M2.5 above. What has landed
so far is the two gaps in the IR that everything else in the milestone needs — the producer could
not name what it drew, and a group could not be faded — and the algebra the rest of the milestone
is written in.

### Added

**`Node.Opacity of float * Node` — a tenth IR case, and the first since M0.** Fades a
**composited** subtree, which is not the same thing as multiplying `Color.A` through it: the two
differ exactly where the group's own shapes overlap, because per-shape alpha paints an overlap
twice. `Scene.opacity` clamps into `[0,1]`. `Color.A` stays the right tool for a single shape.

`Capabilities.GroupOpacity` guards it, and `Degrade.forCaps` folds the node into paint alpha when
a backend cannot composite — the documented approximation above, with its failure mode named.
SVG (`<g opacity>`) and Skia (`SaveLayer`) composite; **TikZ does not**, because pgf's scope
`opacity=` is inherited per path rather than composited over the scope and is overridden by a
path's own `fill opacity=`, and the feature that does composite — `transparency group` — is
gated on driver support this backend cannot assume.

**`Scene.tryFindKey` and `Scene.mapKey`.** Read and rewrite one subtree by its `Key`. `mapKey`
returns every subtree it did not rewrite **by reference**, so a per-frame rewrite costs the path
to the key rather than the figure.

**`Keys` — hierarchical names for what `Marey.Plot` produces.** `figure`,
`figure/panel-0-1`, `figure/panel-0-1/layer-2`, `figure/panel-0-1/layer-2/mark-417`. A figure now
carries a key on its root, on every panel and on every layer; before this it carried exactly one,
at the root, so the diff §5.2 designed the IR around had a granularity of the whole figure.

Layer keys are **ordinals**, not names: a layer's `Name` is optional and may be duplicated across
layers of one panel, so a key built from it would collide. A mark's key is its **original row**,
not its position within a channel batch, which is a renumbered subset.

**`Marey.Anim` — the tenth project in the tree, and animation as a value.** `Anim<'a>` is a duration and a
function of time, so seeking is evaluation (`anim.At 12.7<s>`), frames can be rendered out of
order, and a render parallelises with `Array.Parallel.map` and no scheduler. It depends on
`Marey.Core` and nothing else, which keeps a scrubbable timeline compilable under Fable.

The algebra is `pure'`, `map`, `map2` (parallel, duration = max), `andThen` (sequential,
duration = sum), `stretch`, `ease` and `tween`, with two constructors — `ofFraction`, which
admits an overshoot, and `ofSeconds`, which clamps. `tween` goes through Core's `Interpolate`
witness, so one combinator tweens `float<m>`, `float32`, `Vec2`, `Vec3`, `Color` and `Rect` with
no boxing.

**The clock clamps at the seek and nowhere else.** `At` is total on every `float<s>` — `nan` and
both infinities included — and holds the first or last frame outside `[0, Duration]`. The
normalised clock underneath it is *not* clamped, which is what lets `Ease.backOut` overshoot past
1 and settle back; clamping there would leave a library in which the interesting half of the
easing catalogue compiles, runs and does nothing.

**`Ease`** — twenty-eight functions: linear, quad/cubic/quart, sine, expo, circ, back, elastic
and bounce, each in `In`, `Out` and `InOut`. Every one starts at 0 and ends at 1; `back` and
`elastic` leave `[0, 1]` in between, and exactly those two do.

### Changed

- **`Capabilities` has a tenth field** and `Node` a tenth case, so both break every backend
  implementation at compile time. That is the intended cost of an IR change and the reason the
  type is guarded.
- **Golden baselines moved**: `all-nine-nodes.{svg,tex}` are now `all-ten-nodes.{svg,tex}`, and
  every figure baseline gained the keyed groups described above. No geometry changed.

### Known limitations

- Keys are positional, so they are stable under a restyle, a rename, a re-theme and a change of
  values — which is what an animation does between frames — and **not** under an edit that adds
  or removes a row, which shifts every later index. Identity across a data edit needs an id the
  data supplies, and the scene differ that would consume one is M4.
- Nothing emits a node carrying a `mark` key yet. The spelling is fixed and tested so that the
  storyboard phase, which splits a `MarkerBatch` to give one mark a node of its own, does not
  invent a second one.
- **`Marey.Anim` is in the repository and not on nuget.org.** It can tween and ease a value; it
  cannot yet turn an animated figure into a picture, because that needs an entry point taking a
  pre-computed arrangement. It is packed and published in the same commit that adds one.
- An overshoot survives `tween`, `map` and `stretch` and is **absorbed** by `andThen` and `map2`,
  which reach their components through the clamped seek because that is what "the shorter one
  holds" and "the first one has finished" mean. Ease the components, not the composition.
- Colour overshoot is gamut-limited and **alpha overshoot does not exist**: `Color.ofOklab` ends
  in an unconditional `clamp01` on alpha, so a fade eased with `Ease.backOut` overshoots its hue
  and lightness and not its opacity. Correct, and surprising enough to write down.

### M2.5's known limitations, unchanged

- A PNG or PDF is reproducible on the machine that rendered it unless `SkiaOptions.FontFile`
  states a face. `ResolvedFaces` reports what was chosen so the difference is visible.
- The shaper works against one face and has no fallback list, so a codepoint the face lacks
  measures as the missing-glyph box. Every symbol the unit system produces is in any Latin face;
  a CJK axis title is not.
- `Semantics` is `false` for the Skia backend, so `Degrade.forCaps` strips every `Annotated` node
  before it draws. A PNG has nowhere to put a label, and SkiaSharp exposes no tagged-PDF API.
- A raster placed under a non-identity coordinate has its box mapped and its bounding box used;
  the pixels are not resampled along the warp. Exact under every Cartesian coordinate.
- `Marey.Backend.Skia` needs native libraries. If one is missing, the entry points raise a
  message naming the package to install. On a slim Linux image `libfontconfig1` and
  `libfreetype6` are needed as well.

## [0.2.0] — 2026-08-21

**M2 — *Grammar*.** Published to nuget.org.

`0.1.0` could draw a figure from two arrays. This release adds what a figure needs when the data is
a table instead: categorical axes, colour/shape/size channels, summary statistics, small multiples,
an encoding block that says what each column means in one place, and seven more geoms — twelve in
all. The existing `Chart` calls still work; the one change to what they draw is the bar baseline,
listed under **Changed**.

### Added

**Categorical axes.** `Chart.Category.bar`, `.scatter`, `.count`, `.boxplot` and `.violin` take one
key per row and lay the distinct keys out as evenly spaced, named bands, in the order they first
appear. The y axis keeps its unit, so `Chart.Category.bar U.metre keys values` labels both the
bands and the y unit with nothing configured. `Scale.ordinal` is the scale underneath, and
`Levels` is the ordered level set, with `Levels.sorted` for the alphabetical case.

**Colour, shape and size channels.** `Chart.colorByKey` and `Chart.shapeByKey` give one colour or
one marker shape per category, with a legend naming the levels. `Chart.colorBy` and `Chart.sizeBy`
take a *quantity*, so they take a unit witness — and a continuous colour channel gets a
**colorbar** in a column beside the plot, which derives its ticks, its SI prefix and its title from
that witness exactly as an axis does. A continuous size channel gets a key in the legend showing
three marks across the domain. Rows whose channel value is missing or non-finite are not drawn, and
the count is recorded in the rendered document.

**Statistics.** A new `Stat` module of plain functions over typed arrays — `bin`, `count`, `ecdf`,
`density`, `regression`, `summary`, `quantile`, `groupBy` — each with a `Chart` constructor that
draws its result: `Chart.histogram`, `Chart.histogramWith`, `Chart.density`, `Chart.ecdf`,
`Chart.addFit`. Call the function to keep the numbers, or the chart to draw them.

The units carry through, which is worth knowing before it surprises you. A histogram of metres is a
dimensionless count against a metre axis. A least-squares fit of metres against seconds has a slope
of type `float<m/s>` — a velocity you can bind to an annotated binding and do arithmetic with. A
kernel density of metres is in **reciprocal metres**, which is what makes it integrate to one, so
its axis reads `m⁻¹` and it cannot be drawn on the same panel as the sample it summarises. Every
stat drops non-finite input and reports how many values it dropped.

**Small multiples.** `facetWrap` splits the rows by a key and draws one panel per level over shared
axes, so a difference between panels is a difference in the data. `facetScales` shares one axis
rather than both; `facetWrapIn` states the column count. Anything that must be identical across the
grid — the level set and its colours, a colorbar's scale, a histogram's bin edges — is computed
from the undivided data rather than per panel.

**An encoding block.** `plot rows { … }` declares what each column means in one place:

```fsharp
plot trials {
    x U.second (fun r -> r.Time)
    y U.metre  (fun r -> r.Displacement)
    color      (fun r -> r.Sample)
    size U.kilogram (fun r -> r.Mass)
    layer GeomPoint
    fit
}
```

Each dimensioned channel takes its witness beside the accessor, and the accessor is checked against
it: naming `r.Displacement` under `U.second` does not compile. The block returns an ordinary
`Figure`, so titles, axes, themes and output compose onto it with `|>`. Operations: `x`, `y`,
`color`, `shape`, `colorBy`, `colorByWith`, `colorLabel`, `size`, `sizeWith`, `layer`, `layerWith`,
`area`, `areaWith`, `bars`, `barsWith`, `fit`, `fitWith`, `histogram`, `histogramWith`, `density`,
`densityWith`, `ecdf`, `facetWrap`, `facetWrapIn`, `facetScales`, `name`, `theme`, `coord`.

**Seven more geoms**, which completes the planned set apart from the field geoms (contour, quiver,
streamline, hexbin), still to come:

| | |
|---|---|
| `step` | A staircase. `Chart.step`, `Chart.stepWith` to put the riser at the previous sample or halfway between. |
| `ribbon` | A filled band between two y series — a confidence interval or an envelope. Both bounds are checked against the y unit. `Chart.ribbon`, `Chart.addRibbon`. |
| `rect`, `tile` | Rectangles from corners, or from centres and one size in the data's own units. `Chart.tile`, and `Chart.heatmap`, which sizes the cells from the grid's spacing and attaches the colour channel. |
| `segment` | A line from each point to a second point. `Chart.segment`, `Chart.addSegment`. |
| `text` | A label per point. `Chart.text`, and `Chart.addLabels`, which labels the series already on the figure using its own positions and colour. |
| `boxplot` | Quartiles, Tukey whiskers and the outliers past them. `Chart.Category.boxplot`. |
| `violin` | A mirrored kernel density per band. `Chart.Category.violin`. |

Tile corners are projected individually, so a tile is the right size on a log axis rather than only
in the middle of it. A box plot's y domain reaches its outliers, so they are never clipped by the
panel edge. Violin widths are normalised together and share one bandwidth across the figure, so a
level with more rows draws a wider violin instead of every level drawing the same one.

**Smaller additions.** `Chart.ofPacked`; `Chart.heatmap`; `Geom.withFont` and
`Geom.withFillOpacity`; `GeomStyle.Font`; `Theme.ColorbarWidth`; `Erase.seriesLength`;
`Stat.DefaultDensityPoints`. For code that builds on the library rather than calling it,
`PackedChannel` and `Erase.reunify` are the pieces the encoding block is assembled from: a channel
is one unit witness and one array, and reunifying two of them recovers both measures together.

### Changed

- **A bar chart's y domain now includes zero**, in `Chart.bar`, `Chart.Category.bar` and the
  block's `bars`. A bar encodes magnitude by length, so a domain starting at the data's minimum
  drew a 50:1 picture of a 2.4:1 ratio. Zero enters unpadded and only on the side it is missing
  from, so data straddling zero is unaffected. Charts of tightly clustered non-zero data will look
  different — and correct; `Chart.withYDomain` restores a narrow axis where that was deliberate.
- Two layers on one panel whose x or y are *different* categories now raise instead of unifying.
  Previously the second layer was drawn against the first layer's band labels.
- `Chart.withTheme` now recolours a categorical channel as well as a layer's style, on the same
  rule as before: replaced when the colours are still exactly what the outgoing theme would have
  chosen, kept when they are not. Previously a rethemed figure drew its levels in the old palette.
- `Geom` gained seven cases and `GeomStyle` gained a `Font` field. Both are additive, but code that
  matches a `Geom` exhaustively or constructs a `GeomStyle` as a record literal will need
  updating; `Geom.style` and `Geom.styleFor` are unaffected.
- `Layer` gained a `Channels` field, for the same reason and with the same caveat.

### Fixed

- **A colour or size channel over an error-bar layer attached each bar to the wrong point.**
  Channels draw a layer in batches, one per level or colour step, and each batch was reading its
  spans from the start of the array rather than from the rows it was drawing. Every bar appeared,
  in the right place, at a plausible height, measuring a different observation. It affected
  `Chart.errorbar` and `Chart.addErrorbar` under any channel; error bars without a channel were
  never affected.

## [0.1.1] — 2026-08-20

Documentation and packaging only. No code changed, and no assembly differs from `0.1.0` other
than in version.

### Added

- A package icon, packed into all seven packages rather than referenced by URL, so a gallery
  entry does not depend on a URL outliving the release.

### Changed

- Per-package `PackageReadme.md` for each of the seven packages. The gallery page is most of what
  a first-time visitor sees, and seven packages sharing one page told none of their stories.
- Rewrote the repository README, `docs/why.md`, `examples/README.md`,
  `tests/Marey.Tests/README.md` and the notebook tour.

## [0.1.0] — 2026-08-19

First release. Seven packages, published to nuget.org over trusted publishing.

### Added

- **`Marey.Core`** — the seven-element SI dimension vector, unit witnesses, unit-indexed geometry,
  affine transforms, five scale kinds over seven constructors, deterministic number formatting,
  and the nine-case scene IR. Zero dependencies.
- **`Marey.Units`** — SI base units, 22 derived and 18 non-SI unit witnesses; `Dim`-keyed
  canonicalisation, the full 24-prefix ladder with the kilogram shift, affine units carrying their
  offset, and Unicode/ASCII/LaTeX/siunitx rendering. Axis titles derived from the witness.
- **`Marey.Plot`** — `Chart`, five geoms (line, scatter, area, bar, error bar), six themes,
  colour-blind-safe palettes with a validator, constraint-based layout, and the axis and legend
  compilers. Takes no backend reference; rendering enters as a function.
- **`Marey.Backend.Svg`** — SVG output. Pure F#, zero dependencies, byte-identical across renders.
- **`Marey.Backend.Tikz`** — `tikzpicture` source for LaTeX, with `siunitx` units and fonts
  inherited from the host document.
- **`Marey.Html`** — HTML renderings of nine Marey types for a notebook cell. Host-neutral.
- **`Marey.Verso`** — the [Verso](https://github.com/DataficationSDK/Verso) notebook extension, so
  a cell whose value is a `Figure` displays as a figure.

### Known limitations

- Tick labels carry no additive-offset annotation, so a domain of 1000.000–1000.005 m yields five
  identical labels.
- TikZ output has no gradients and no rasters; both degrade visibly rather than silently.
- TikZ label layout is measured against the Unicode form of a unit string, not its siunitx
  rendering, so it is approximate in a way SVG's is not.
- Coordinates outside TeX's representable range are clamped rather than clipped.

## [0.1.0-rc.1] — 2026-08-18

Release candidate, published to validate the packaging and the publish workflow.

Superseded by `0.1.0` and not recommended for use. `Marey.Verso` in particular cannot be
installed from it: prerelease versions do not appear in Verso's Extensions panel at all, so an
rc validates the package and the formatter but never the installation path.

[Unreleased]: https://github.com/Jonathan-Schaefer-git/marey/compare/0.2.0...HEAD
[0.2.0]: https://github.com/Jonathan-Schaefer-git/marey/compare/0.1.1...0.2.0
[0.1.1]: https://github.com/Jonathan-Schaefer-git/marey/compare/0.1.0...0.1.1
[0.1.0]: https://github.com/Jonathan-Schaefer-git/marey/compare/0.1.0-rc.1...0.1.0
[0.1.0-rc.1]: https://github.com/Jonathan-Schaefer-git/marey/releases/tag/0.1.0-rc.1
