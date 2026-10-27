# Changelog

Notable changes to Marey. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Every package is versioned and released together, so a version number identifies the whole set
rather than any one package. Each release's notes say which packages it adds.

**Pre-1.0, and versioned accordingly.** The API is still free to move. Breaking changes are
listed under `Changed` or `Removed` with the reason and the replacement, and until 1.0 they may
land in a minor release.

## [Unreleased]

<!-- A draft. At 1.0.0 the statement below replaces the "Pre-1.0" paragraph above. -->

**What a version number promises, from 1.0.0.** The public API is what `api/` lists, package by
package. A patch release fixes defects and changes no public signature. A minor release adds, and
removes, renames or retypes nothing a 1.x release published. Anything else waits for a major
release, with four exceptions, which a minor release may make:

- **A new field in a public record.** `Figure`, `Panel`, `Layer`, `AxisConfig`, `Theme`,
  `PanelView` and `Shot` are built from a published value with `with` (`{ Theme.modern with … }`,
  `{ Chart.axis "t" with … }`), or only read. Code written that way keeps compiling when a field is
  added. A record expression that names every field does not, and is not covered. `PlotSpec`,
  `Handle`, `PanelLayout`, `LiveZoom`, `Story` and `Step` have no fields a user can name.
- **A new case of an open union.** `Geom`, `LegendPlacement`, `ZoomAxis` and the scene's `Node` gain
  cases as the library gains marks, places for a legend, ways to zoom and kinds of drawing. A
  `match` over one of them keeps a wildcard case; a `match` that names every case is not covered.
  A new case in any other public union waits for a major release.
- **What a figure looks like.** Drawing is not part of the API. A minor release may move a mark, a
  tick or a gutter, and lists it under `Changed` with the baselines that moved. A patch release
  moves a drawing only to fix a defect.
- **A newer Fable, SDK or toolchain** that a release is built and tested with, named under
  `Changed`.

Two formats are versioned on their own rather than by the package. A scene on the wire (`Wire`)
carries its format version, and a reader refuses one it does not know, naming the version. A
`LiveLog` saved as JSON is format version 1, which every 1.x reads. `LiveMsg`, which passes between
a kernel and a page of the same release, is not a format and may change in any release.

The packages are released together and depend on each other by exact version, so upgrading one
means upgrading all of them; referencing `Marey` does that in one line.

Every package targets netstandard2.1, except the GPU backend (net8.0) and the Verso extension
(net8.0 and net10.0), and requires FSharp.Core 8.0.100 or newer. Each release is checked on the .NET
SDK 10.0.100 band and on the newest SDK. The nine packages that carry their sources for Fable are
built and tested with Fable 5.20. A live figure in a notebook needs Verso 1.2.3 or newer. The GPU
window runs on Linux and Windows, not on macOS. `latex`, `dvisvgm` and `ffmpeg` are found on `PATH`
and reported when missing; Marey does not require a version of them.

### Added

- **Symbol packages with SourceLink.** Each package with code ships a `.snupkg` whose PDBs map
  its sources to the commit on GitHub, so a debugger steps into Marey's source.
- **One PNG on every machine.** `Marey.Backend.Skia` carries Inter (weights 400 to 700, subset to
  Latin, Greek and the symbols units use) and Liberation Serif (upright, italic and bold), both SIL
  OFL 1.1 with their licences in the package's `fonts/`, and a stack that names one of them
  resolves to it before anything installed. The built-in themes' PNGs and PDFs no longer depend on
  the machine's fonts; CI compares one script's PNGs from Linux and Windows pixel for pixel. A PNG
  and the GPU window draw text as the glyphs' outlines, which Skia fills itself, since FreeType
  and DirectWrite smooth one glyph differently; a PDF keeps its text as text.
  `ResolvedFace.Bundled` says when a carried face was used, and
  `SkiaTextMeasurer.MeasureUnshaped` gives a run's advance without shaping. The package grows by
  about 1 MB.
- **Values that carry their unit.** `U.second.Of ts` is a `Q<s>`, the values and their witness,
  and `U.metrePerSecondSquared.Of 9.81<m/s^2>` a `Quantity`, one value with it. Under
  `open Marey.UnitsOfMeasure` a chart over `Q`s takes no witness: `Chart.line t x`. Arithmetic
  computes the witness of its result (`0.5 * g * t * t` is a `Q<m>` titled `[m]`), `+` and `-`
  read the right operand in the left one's unit, and `Q.map` names the unit arithmetic does not
  reach. A `Q` is a `seq<float<'u>>`, so it passes wherever data is taken.
- **`Marey`, the library in one reference.** `#r "nuget: Marey"` or `dotnet add package Marey`
  brings units, the plotting grammar, fields, simulations, animation, live figures, and SVG, TikZ
  and HTML output: Core, Units, Plot, Anim, Fields, Sim, Interact, Backend.Svg, Backend.Tikz and
  Html. It carries no code. What needs a native library or an external program stays a separate
  reference: `Marey.Backend.Skia` for PNG and PDF, `Marey.Backend.Gpu` for a window,
  `Marey.Tools.Tex` and `Marey.Tools.Video`. A Fable app can reference it too, and compiles the
  packages in it that carry their sources. Eighteen packages.
- **`WebCanvas.byId` and `WebCanvas.context`** in `Marey.Backend.Web`: the page's canvas element
  by id, and its 2-D context, which `CanvasBackend.draw` takes. A Fable app no longer declares an
  `Emit` for `getElementById`.
- **`U.degree`**, π/180 rad, for a phase, an angle of incidence or a latitude in degrees (C#
  `Units.Degree`; `U.parse "deg"` or `"°"`). It is an angle as the radian is: an axis in it
  reads `θ [°]` and takes no prefix, and `°/s` does not share an axis with `Hz`.
- **`GpuRenderer.RenderImageAt` and `RenderPngAt`**, a scene at a scale, for a HiDPI window:
  `gpu.RenderImageAt 2.0 extent scene` is an image of twice the extent's pixels each way, with
  strokes, marks and text twice their px size as well as the geometry, as a window at a scaling of
  2 shows it. `RenderImage` stretched to the window was soft; at 2× the scaled image is within the
  GPU's tolerance of a CPU raster canvas scaled by 2 on every golden. Instanced marker batches
  take the scale too.

### Changed

- **A series constructor's witness form is named with `In`**: `Chart.lineIn U.second U.metre ts
  xs`, `Chart.addErrorbarIn`, `Chart.histogramIn`, `Chart.Category.barIn` and the rest, since
  `Chart.line t x` is now the form over values that carry their unit. Functions that take one
  value (rules, annotations, ticks, a colour domain) keep their names and witnesses.
- **`U.squareMetre`, `U.cubicMetre`, `U.metrePerSecond`, `U.metrePerSecondSquared` and
  `U.kilogramPerCubicMetre` carry their factors**, as the units `U.mul` and `U.div` compose do, so
  a product with one cancels: `m/s²` times `s²` reads `m`. They draw as before.
- **Data is taken as any sequence.** Every chart constructor, `Stat` function, grid, field
  sampler, colour and size channel, set of keys, labels or ticks, facet and palette takes a
  `seq<_>` where it took an array, so an F# list, a `ResizeArray` or a C# `List<double>` is passed
  as it is (C# members take `IEnumerable<T>`). An array still compiles where it did. The sequence
  is read once, when the call is made, and copied: a figure no longer changes when the caller's
  array is changed after it was built. Results (statistics, trajectories, grids' nodes) stay
  arrays, and so do the scene, the wire format and the simulations' in-place steppers.
- **A scale kind is the panel's axis's, not each layer's.** `withXLog`, `withYLog` and the
  `ScaleKind` verbs record the kind on the axis (`AxisConfig.Kind`, new), and a layer added later,
  a rule included, is drawn on it; before, it kept a linear scale under a log axis, so a rule at
  1 kHz on a log 10 Hz … 100 kHz axis stood near the left edge. `withYLog` now acts on the left y
  axis only: after `withRightAxis` it also logged the right side's layers, and a phase in −π … 0
  there was not drawn. `withRightYLog` and `withRightYScaleKind` (C# `WithRightYLog`,
  `WithRightYScaleKind`) are new and set the right axis's.
- **A rate reads `s⁻¹`, a frequency `Hz`.** A unit written as the reciprocal of one base unit
  (`U.perSecond`, `U.recip U.second`) is shown as written, so a vorticity, a decay constant or a
  growth rate is no longer labelled as a frequency; `U.hertz` still reads `Hz`. Draw a frequency
  with `U.hertz`. A small rate keeps its power of ten rather than reading `ks⁻¹`.
- **A reciprocal axis takes the inverse prefix on its unit:** a wavenumber reads `k [nm⁻¹]`, where
  it read `k [10⁹·m⁻¹]`, as SI reads a prefix before the exponent. The kilogram keeps the
  multiplier. `figure-ticks` moves.
- **`withLegendFrame false` takes the legend's background with it**, as matplotlib's
  `frameon=False` does, so a frameless legend no longer masks the data under it. A theme whose
  own legend is borderless keeps its fill. `figure-legends` is the one baseline that moves.
- **Every unit witness is in `U`.** `U.joule`, `U.tesla`, `U.electronvolt`, `U.hour`,
  `U.degreeCelsius` and the rest, where they were `Derived.…` and `NonSi.…`; the two modules are
  gone. C#'s `Units.*` is unchanged. `Derived.newtonByComposition` and its four siblings are no
  longer public: compose the unit, `U.mul U.kilogram U.metrePerSecondSquared`.
- **Renamed:** `Num.at` is `Num.upTo` (up to that many decimals, trailing zeros dropped);
  `Anim.pure'` is `Anim.constant`; `Anim<'a>.AtSeconds` is `AtUnclamped`, which says how it differs
  from `At`; `Marey.Sim`'s `Step<'s,'t>` is `Stepper<'s,'t>`, so it no longer shares a name with the
  storyboard's `Step`. The auto-opened `interp` and `interpWith` are gone: `Interp.between` and
  `Interp.betweenWith`.
- **C# reaches every verb.** New: `WithXAxis`/`WithYAxis` with `Figures.Axis(label)`,
  `WithXScaleKind`/`WithYScaleKind`, `WithCoord`, `ToRightAxis`, `WithTopAxisLabel`,
  `WithPanelXDomain`/`WithPanelYDomain`; and `Themes.Publication`, `Coords.Polar` and
  `Prefixes.Nano` in place of `ThemeModule.publication`, `CoordModule.polar` and
  `PrefixModule.nano`.
- **A smaller public surface.** What turns a figure into a scene is internal: the layout, axis,
  legend, colorbar, field-mark and channel modules, the geom compilers, the arranging functions
  `Chart.*` already exposes, the tick generator, the SVG and TikZ writers' helpers, the GPU window's
  control strip and input session, and the TeX pipeline's parsers. `PlotSpec`, `Handle`,
  `PanelLayout`, `LiveZoom`, `Story` and `Step` keep their names with their fields hidden. Marey's
  packages now depend on each other by exact version, since they share internals.
- **A stated proportion holds beside a side of another shape.** `withRowHeights [| 3.0; 1.0 |]`
  beside a stack of three drew 1.70 : 1, and the stack uneven, because `beside` subdivided both
  sides to the least common multiple of their rows and every line of one side then crossed the
  other's panels with its gutters. The two sides' rows are now merged where they end, and the lines
  are placed so that each side's plots keep their proportions. `above` does the same for
  columns. A figure composed from sides of different shapes has a different number of rows or
  columns than before (three beside two has four rows, not six), so `withRowHeights` after such a
  `beside` takes that many weights, and its plots move. Figures whose sides line up row for row
  draw exactly as before.
- **A secondary axis keeps one prefix through an animation.** Its prefix was chosen per frame from
  its own numbers, so a zoom across a decade stepped its title (`x [mm]` to `x [µm]`) beside a
  primary title that held still. A shot now pins it over the image of the primary's union, as it
  already pinned the primary. `PanelLayout` gains `X2Prefix`; `Y2Prefix` now pins whichever axis is on
  the right.
- **An annotation's text past the plot is given room.** It was drawn unclipped but nothing
  reserved space for it, so text beyond the plot's edge was cut by the figure's (P4's `E_F` at
  the right end of a Fermi level). The panel's gutter on that side now grows to hold it.
  `PanelLayout` gains `LeftPad` and `BottomPad` beside `RightPad` and `TopPad`.
- **The pointer's readout keeps clear of a `LegendBest` that went top left.** It moved to the right
  only for a legend stated as `LegendTopLeft`. It now resolves `LegendBest` as the drawing pass
  does, through `PanelLayout.legendPlacement`, which is new.

### Removed

- **`Chart.Plain`**, deprecated in 0.4. Use `Chart.Line(xs, ys)`, which leaves the units
  unspecified, or pass `U.one` where the data is dimensionless.

### Fixed

- **Two units of one dimension on one axis are converted.** A layer in minutes added to an axis
  in seconds was drawn in its own numbers, so 4 min stood at 4 s; so were degrees beside radians,
  °C beside K and a kilometre panel sharing x with a metre one. The domain an axis shares
  or is given (`withXDomain`, a zoom, an inset's, a storyboard's) is now in the axis's unit,
  the first stated layer's, and each layer reads it in its own, offsets included.
- **A zoomed field drew its streamlines over the whole grid.** Streamlines are spaced in pixels,
  and they were traced, and the LIC texture rastered, over the grid's whole extent on the page, so
  zoomed in k times a portrait traced k² times the lines and clipped all but the plot area's
  share. A pendulum's portrait zoomed twice on a live view took 6.5 s to lay out and wrote a
  93 MB PDF. Both marks now cover only the grid's part in the plot area: 0.05 s and 519 KB. An
  unzoomed figure draws as it did. Zoomed, a LIC texture's noise now belongs to the visible part,
  so panning redraws the texture rather than sliding it.
- **A flipped panel turns its axes too.** Under `Chart.withCoord (Coord.flipped Coord.cartesian)`
  the marks moved and the axes stayed: a categorical scatter kept its band names under the x axis
  and its values up the left, so every mark stood against the wrong numbers. The axis reading x now
  stands on the left and the one reading y along the bottom, each with its own title, ticks, plan
  and gutter, so the categories of a flipped `Chart.Category` figure run down the left with their
  names upright. A secondary axis on a flipped panel is refused, as a second y axis already was.
- **A categorical axis names every band.** Seven model names at 800 px were thinned to every
  other one, and a skipped band name cannot be read off its neighbours as a skipped number can.
  An axis over an ordinal scale, or with named ticks, is never thinned: crowded labels are turned
  45°, then 90°, which keeps each name whole and the figure's one type size, and the gutter grows
  to hold them. Named ticks that collided used to stay upright and overlap; they turn now too.


## [0.7.0] — 2026-10-16

**Paper figures.** What a figure for a paper adds last, and where its panels sit. Rules, spans and
annotations with arrows that leave the axes the data chose, panel letters, stated and minor ticks,
tick formats, reversed axes, and legends outside the plot, in the emptiest corner or shared. Panels
that line up on one grid, spans and mosaics, plot ratios, shared axes labelled once, a second y
axis, secondary axes in another unit or through a map, equal aspect, and insets. `Theme.publication`
is now the framed box physics journals print. A scene also travels as bytes (`Wire`), and a figure
above 5000 drawn elements is drawn on a canvas in a notebook, so a 10⁵-point live figure steps in
tens of milliseconds; a live figure in Verso needs 1.2.3 or newer. Seventeen packages, none new.
Under `Changed`, what can break a build: a record expression that builds `AxisConfig`, `Panel`,
`Figure`, `Layer`, `PanelLayout`, `PanelView`, `LiveZoom`, `AxisSpec`, `AxisRender` or `Theme`
without `with`, and a match that names every case of `LegendPlacement`, `Geom` or `ZoomAxis`.
Multi-panel figures, facets and `Theme.publication` draw differently.

### Added

- **`Wire`** in `Marey.Core`: a scene (`Wire.encode`, `decode`) or a patch (`encodePatch`,
  `decodePatch`) as bytes and back, field for field. Little-endian float64 throughout, float arrays
  aligned to 8 bytes, a header with a format version that a newer reader refuses by name, and
  blocks a reader skips, so the format can grow without a new version. `Wire.encodeWith` carries
  the text metrics the scene was laid out with, and `decodeWith` returns them as a `TextTable`, a
  measurer that answers from them: a page anchors text as the kernel measured it. It compiles under
  Fable.
- **A large figure in a notebook is drawn on a canvas.** `RenderMode` (`Svg`, `Canvas`, `Auto`),
  `Html.renderMode`, and `Html.CanvasThreshold`, 5000 drawn elements, measured. Above it a live
  figure's page holds a `<canvas>`, the scene's bytes and `Html.viewScript`, Marey's canvas backend
  compiled by Fable into one script that `Marey.Html` embeds, and each step is answered with a
  patch's bytes or the whole scene's. A 10⁵-point scatter is 2.1 MB rather than 6.4 MB of SVG, and
  a step takes tens of milliseconds rather than about a second. `Html.documentForHost` writes a
  figure without controls the same way; `Marey.Verso` shows it as a page, because an HTML output
  runs no script. Every canvas page carries the figure's SVG without its large marker batches,
  for a viewer that runs no script.
- `Html.liveWholeForHost`, the whole figure in its page's kind, which `Marey.Verso` asks for a
  view's first message.
- **`examples/interact.dib`** gains a live figure of 10⁵ points.
- **Rules and spans** (D162): `Chart.addHLine`, `addVLine`, `addHSpan`, `addVSpan` and
  `addLineThrough`, whose slope is in the y unit per x unit; `hLine` … `lineThrough` as figures;
  `Chart.HLine(y, ?unit)` and its siblings in the unit-optional mode. A rule is laid across the
  panel when the figure is drawn, so it spans it after a facet's sharing, a stated domain or a
  zoom, and under `Coord.polar` a horizontal rule is a circle. It stays out of the domain, is
  drawn in the theme's axis colour, dashed, and joins the legend only when named. One that falls
  outside the panel is not drawn and is counted on the panel as `dropped-marks`.
- **Layers that stay out of the domain** (D163): `Layer.InDomain`, and `Chart.withDomainIncluded`
  to bring the last layer back in. Its unit is still checked against the axis.
- **Annotations** (D164): `Chart.annotate` marks a point with text; `withTextOffset` (px),
  `withTextAt` (data space) and `withArrow` (`ArrowStraight`, `ArrowArc bend`) place it and point
  at the point. In the theme's foreground, out of the domain, and not clipped to the plot.
  `Chart.Annotation(…)` in the unit-optional mode.
- **Panel text and panel letters** (D165): `Chart.addPanelText` by fractions of the plot area;
  `Chart.withPanelLetters LetterParen` letters the panels (a), (b), … in reading order, above each
  panel's top left, when the figure is drawn.
- **Stated ticks** (D166): `withXTicksEvery`, `withXTicksAt` and `withXTicksNamed`, and their `Y`
  forms, in the axis's own numbers; the prefix and the unit in the title are still chosen. A step
  that would give more than 50 ticks is refused, a step in another unit of the dimension is
  converted, and named ticks are never dropped to make room. `TickSpec` and `Axis.ofScaleTicked`
  in `Marey.Units`.
- **Minor ticks** (D167): `withXMinorTicks` (`MinorAuto`, `MinorOff`, `MinorCount`, `MinorEvery`)
  and `AxisSpec.Minor`.
- **Tick direction and mirrored ticks** (D168): `Theme.TickDirection` (`TickOut`, `TickIn`,
  `TickInOut`), `MirrorTicks`, `MinorTicks`, `MinorTickLength`, and `Theme.withTickDirection`,
  `withMirrorTicks`, `withMinorTicks`. An inward tick gives its length back to the plot.
- **Tick formats and a stated prefix** (D169): `withXTickFormat` (`FormatFixed`, `FormatPercent`,
  `FormatScientific`, `TickFormat.custom`) and `withXPrefix`.
- **Reversed axes** (D170): `withXReversed` and `withYReversed`, over the domain the axis would
  have had.
- **Legends** (D171): `LegendOutsideRight`, `LegendOutsideBottom` and `LegendBest` (the emptiest
  corner, chosen once for an animation); `withLegendColumns`, `withLegendFrame`, and
  `withFigureLegend` for one legend shared by every panel.
- **Spans** (D174): `Panel.RowSpan` and `ColSpan`; `Chart.mosaic "AB;AC" [ 'A', a; … ]` lays
  single-panel figures out by a picture of the grid, `.` for an empty cell.
- **Row and column ratios** (D175): `Chart.withRowHeights` and `withColWidths`, as ratios of the
  plots rather than of the cells. Exact unless `beside` or `above` subdivides the weighted rows to
  meet a side with more of them, where they are approximate.
- **Shared axes on any composition** (D176): `Chart.shareX`, `shareY` and `shareAxes` share the
  axes of every panel the figure has, checked for one dimension at the call and kept shared: a
  domain stated on one of the panels, or a zoom of one, is the group's. `AxisConfig.ShowTickLabels`.
- **A second y axis** (D177): `Chart.withRightAxis right` draws a one-panel figure's layers on
  the right of the last panel, against a y axis of their own, each side's unit checked among its
  own layers; `toRightAxis`, `withRightYDomain`, `withRightYLabel`. A side with one series is drawn
  in its colour. The pointer reads the second axis too, and a zoom or a pan moves it with the
  first. `Panel.Y2Axis`, `Layer.SecondaryY`, `PanelView.Y2`, `ZoomAxis.Y2`, `Keys.axisY2`.
- **Secondary axes** (D178): `Chart.withTopAxisIn` and `withRightAxisIn` read an axis in another
  unit of its dimension (°C over kelvin), converted by the units; `withTopAxisMapped` and
  `withRightAxisMapped` through a map and its inverse (photon energy over wavelength), typed in
  both directions under `Marey.UnitsOfMeasure` and checked on the domain when drawn: finite,
  monotone, and inverse to each other. Ticks are chosen in the secondary unit and placed through
  the inverse. `withTopAxisLabel`; `AxisConfig.Secondary`, `SecondaryAxis`, `SecondaryMap`,
  `Keys.axisX2`.
- **Equal aspect** (D179): `Chart.withEqualAspect` holds one unit as long on y as on x, the plot
  shrunk to the domain's shape and centred in its cell; both axes linear and of one dimension,
  compared in SI. An animation keeps one box over the union of its frames and widens each frame's
  domain to it; a live zoom is widened to the plot's shape, so the plot does not move.
  `withBoxAspect` states the plot's height over width. `Panel.Aspect`, `PanelView.EqualAspect`,
  `PanelView.fitEqual`.
- **Insets** (D180): `Chart.withInset (x0, y0, x1, y1) inset` draws a one-panel figure over the
  last panel's plot, its own plot at those fractions of the host's, its axes its own;
  `withInsetZoom` draws its domain on the host as a rectangle joined to it by the two corner lines
  that cross neither. A pointer over an inset reads and zooms the inset. Keyed under its host,
  `panel-r-c/inset-i`. `Panel.Insets`, `Inset`, `PanelView.Inset`, `LiveZoom.Inset`, `Keys.inset`,
  `Figure.withInsetDomains`.
- **Axis looks**: `Chart.withXLook`, `withYLook` and `withAxesLook` with `AxisFull`,
  `AxisUntitled`, `AxisUnlabelled`, `AxisBare` and `AxisHidden` set how much of an axis is drawn,
  and only that, so they compose with the other axis verbs in either order. An axis without tick
  marks reserves no room for them. `AxisLook`, `AxisConfig.ShowTicks`.
- C# extensions for every verb above, and `Figures.Mosaic`.
- **`examples/paper-figure.fsx`**: one figure for a paper. A fit over its residuals, sharing t,
  three to one, beside a spectrum spanning both rows with its lines marked and photon energy along
  the top.

### Changed

- **A `Figure` above 5000 drawn elements displays in Verso as a page in a frame**, not as inline
  SVG. `Html.renderMode <- RenderMode.Svg` restores the SVG for every figure.
- **`Theme.publication` is the box physics journals print** (D168): framed, with ticks inside on
  all four sides and minor ticks between them. `Theme.tikz` is `publication` and follows it, which
  is also pgfplots' default. The previous look is `Theme.publication` with `PanelFrame = false`,
  `TickDirection = TickOut`, `MirrorTicks = false` and `MinorTicks = false`.
- **A panel whose two axes are both lengths titles them `x` and `y`**, by side; both were `x`. A
  length against anything else is still `x`.
- **The plots of a multi-panel figure line up** (D173): every panel in a column has one left and
  one right plot edge, every panel in a row one top and one bottom, and equal rows or columns get
  equal plots rather than equal cells. A panel next to one with a colorbar or an outside legend
  gives up that room too. A figure of one panel is placed as before.
- **`beside` and `above` no longer leave empty cells** (D174): two figures with different numbers
  of rows (or columns) are composed over the least common multiple, and each side's panels span
  their share. Figures with equal counts compose as before.
- **A facet labels a shared axis once** (D176): only the panels at the foot of each column write
  their x tick labels and title, and only those at the left of each row their y ones. The ticks
  stay. A zoom or a stated domain on one panel of a facet now applies to its shared axis.
- New record fields, which break a record built without `with`: `AxisConfig` (`Ticks`,
  `TickUnit`, `Prefix`, `Reversed`, `Minor`, `Format`), `AxisSpec.Minor`, `Layer.InDomain`,
  `Panel` (`LegendColumns`, `LegendFrame`), `Figure` (`Letters`, `Legend`), `PanelLayout`
  (`LegendGap`, `LegendW`, `LegendH`, `LegendScores`), `AxisRender` (`Direction`, `Mirror`,
  `ShowMinor`), and `Theme` (`TickDirection`, `MirrorTicks`, `MinorTicks`, `MinorTickLength`).
  M9 adds `Layer.SecondaryY`, `Panel` (`RowSpan`, `ColSpan`, `XShare`, `YShare`, `Y2Axis`,
  `Aspect`, `Insets`), `Figure` (`RowWeights`, `ColWeights`), `AxisConfig` (`ShowTickLabels`,
  `Secondary`, `ShowTicks`), `PanelLayout` (`SlackLeft`, `SlackRight`, `SlackTop`, `SlackBottom`, `Y2TicksW`,
  `Y2TitleW`, `Y2Prefix`, `X2TicksH`, `X2TitleH`), `PanelView` (`Y2`, `Y2Axis`, `EqualAspect`,
  `Inset`) and `LiveZoom.Inset`. `LegendPlacement` gains three cases, `Geom` two (`GeomRule`,
  `GeomAnnotation`) and `ZoomAxis` one (`Y2`), each of which breaks an exhaustive `match` over
  it.

### Fixed

- `Canvas.setCssSize` and `Canvas.devicePixelRatio` in `Marey.Backend.Web` threw on .NET; they do
  nothing and return 1 there, so `CanvasBackend` draws on a context a .NET caller supplies.
- **A flat series at zero widened every other layer's axis to ±1.** A constant series is padded
  so that it has an axis of its own, and at zero that padding is ±1 in the axis's unit. Layers on
  one panel, and panels sharing a facet axis, took the union of their padded domains, so a
  reference line at y = 0 beside data spanning ±1 mm turned the axis from `x [mm]` at ±1.1 mm into
  `x [m]` at ±1 m. A constant now contributes its value to a shared axis and not its padding. A
  value outside the other layers' span gets 5 % of the shared span as room. A panel whose layers
  are all constant keeps the padded axis it had. A domain set with `Chart.withYDomain` or its
  siblings is kept as it was. The same applies across the states of a shot: a wave that starts
  flat at zero pinned every frame of `Chart.arrangeUnion` to `x [m]`, and now pins `x [mm]`.

## [0.6.0] — 2026-10-11

**Simulation.** `Marey.Sim` steps a physical system with its units checked: ODE integrators,
events and hard-disc collisions, Poincaré sections, particles in an unsteady field, finite
differences for heat, waves and Poisson's equation, and two fluid solvers, Stam's stable fluids
and lattice Boltzmann. A simulation is an `Anim` that seeks to any time and gives the state a
straight run reaches, bit for bit, and a time control plays it in a notebook or the window.
`Marey.Backend.Gpu` steps both fluids and convolves LIC on the GPU. Seventeen packages, one of them
new. Three entries under `Changed` can break a build: a match over `ControlKind` that names every
case, a call to `FieldMarks.lic` or `FieldMarks.licImage`, and a record expression that builds a
`TickLabel`. Every package now requires FSharp.Core ≥ 8.0.100, so a project on SDK 10.0.1xx
restores without a downgrade warning.

### Added

- **`Marey.Sim`**, the seventeenth package: ODE integrators typed by units of measure, and a
  simulation as a step function. `Ode.verlet` and `Ode.leapfrog` integrate a second-order state,
  positions in one unit and velocities in that unit per second; `Ode.euler` and `Ode.rk4` a 2-D
  flow with time (`Flow2T`); `Ode.eulerWith` and `Ode.rk4With` any state given a `Tangent`, the
  two operations that advance it and combine its rates. An acceleration or a rate in the wrong
  unit does not compile. `Ode.verletInPlace` steps the caller's arrays and allocates nothing.
  `Sim.ofStep`, `Sim.run` and `Sim.states` run a step with time computed as `k · h`.
  `Ode.dopri5` (a 2-D flow, an absolute tolerance per axis) and `Ode.dopri5With` (any state, with
  an `ErrorNorm` that weighs each component by its own tolerance) are Dormand–Prince 4(5) with a PI
  controller and fourth-order dense output; `Sim.ofAdaptive` runs one.
  `Sim.trajectory every horizon` is the simulation as an `Anim`: a seek to any time replays from
  the nearest checkpoint and gives the state a straight run reaches, bit for bit, on any thread. A
  checkpoint table estimated over 512 MiB is refused, naming an interval that fits.
  Events (`Event.create`, `Sim.withEvents`): a function of the state crossing zero in a stated
  direction, located to a tolerance in time (on the dense output for `dopri5`, by re-stepping for
  a fixed step), with a response that replaces the state or stops the run, and a minimum
  separation that applies a rest response or stops instead of looping (Zeno). `Sim.events` lists
  the events a run passed; `Sim.at` is the straight run to a time. `Response.reflect` for a
  second-order state. `Ode.withMaxStep` caps an adaptive step. `Collide.step` steps hard discs
  in free flight with every contact inside the step resolved exactly in time order, against walls,
  on a torus or in the open, with a grid broad phase above 256 discs and a per-step contact cap
  whose hits are counted.
  `Poincare.stroboscopic period trajectory` is the state at every multiple of a period, each by a
  seek; `Poincare.section direction surface horizon sim` is every crossing of a surface in a
  direction, located by the event machinery, so each point is the integrator's own state on the
  surface.
  `Unsteady` is a velocity field that depends on time; `Unsteady.ofSnapshots` reads grids at
  stated times, bilinear in space and linear in time, and `Unsteady.ofFunction` a grid built for
  each time asked. `Particles.step` carries many particles together, stage by stage, so a field
  built per stage time is built once per stage, not once per particle; `Particles.streakStep`
  releases particles from seeds every step, and `Particles.pathlines` reads each particle's path
  from a trajectory.
  `Fdm`: finite differences on a uniform `ScalarGrid` with typed boundaries per side (`Boundary.
  Dirichlet` in the field's unit, `Neumann` in its unit per metre, `Periodic`). `Fdm.laplacian5`
  is the compact five-point stencil; `Fdm.heat` steps by forward Euler and refuses a step past
  `Fdm.explicitLimit`; `Fdm.heatImplicit` is Crank–Nicolson by ADI, stable at any step;
  `Fdm.wave` is leapfrog under `Fdm.waveLimit`; `Fdm.poisson` solves by over-relaxation and reports
  its sweeps and residual.
  `Sim.withInvariant` attaches a conserved quantity (with a unit witness under
  `open Marey.UnitsOfMeasure`), `Sim.drift` samples each one's drift over a trajectory, relative
  to its start or absolute where the start is zero, and `Drift.chart` draws them. The package
  claims Fable.
- **Fluids in `Marey.Sim`.** `Stam.step` is Stam's stable fluids on a staggered grid (`Fluid`):
  semi-Lagrangian advection, implicit viscosity and dye diffusion, a buoyancy per unit of dye, a
  steady force and source, and a projection by multigrid that leaves only its solver's residual.
  `Fluid.box`, `withVelocity`, `withDye`, and `velocity`, `dye`, `divergence`, `vorticity`,
  `pressure` to read it. `Lbm.step` is lattice Boltzmann D2Q9 (`Lattice`) in deviation form, with
  obstacles and per side a periodic edge, a wall, an inflow or an outflow; `Lbm.validate` refuses a
  relaxation time at or below ½, an inflow past 0.3 of the lattice's speed and a step other than
  the tick, with the numbers. Both compile under Fable.
- **`GpuSim` in `Marey.Backend.Gpu`**: `GpuSim.stam` and `GpuSim.lbm` step the same `Fluid` and
  `Lattice` on a `GpuRenderer`'s GPU by fragment shaders in OpenGL 3.3, agreeing with the CPU steps
  to a few parts in a million after 200 steps; `stamWith` and `lbmWith` run several steps a call.
  `Marey.Backend.Gpu` now references `Marey.Sim`.
- **LIC on the GPU.** A figure compiled with `GpuRenderer.Measurer` has its LIC convolved on the
  GPU, one streamline per pixel. `LicJob` and `ILicConvolver` in `Marey.Core` are how a measurer
  offers it; `MemoTextMeasurer` forwards it. Any other measurer draws LIC as before, to the byte.
- **`examples/fluid.fsx`** (warm air rising in a box) and **`examples/cylinder.fsx`** (a Kármán
  street at Re 100), each on the GPU when there is one and on the CPU otherwise.
- **A time control**: `Control.time "t" 0.0<s> 10.0<s>` (and `Control.timeWith samples loop`,
  `Control.Time("t", 0.0, 10.0)` over plain seconds). The notebook widget draws a range input and
  a play button, and plays it on the page's clock; the window plays it on its frame loop, with
  space to play and pause and a button before the track. Every frame played is an ordinary `Set`,
  so a recording replays the playback. A live figure is arranged over 60 states along the time
  control's range rather than its two ends, so a view whose widest state lies inside the range
  does not re-arrange as it plays.
- **`examples/integrators.fsx`**: Euler, RK4 and Verlet on one oscillator, and the energy drift of
  each over 100 periods.
- **`examples/heat.fsx`**: heat in a plate by Crank–Nicolson, as a film and as a live figure with a
  time control.
- **`examples/double-gyre.fsx`**: pathlines, a streakline and particles in an unsteady field, as a
  film.
- **`examples/double-pendulum.fsx`**: a chaotic system's path over a phase portrait, a Poincaré
  section, and a film.
- **`TickLabel.LabelTex`**, a tick's label as TeX source. Set on a power of ten when the axis is
  compiled in the `Latex` or `Siunitx` style, as `AxisSpec.TitleTex` is for the title, and drawn
  by the TikZ backend in math.
- **`Ticks.WideLogDecades`, `Ticks.isWideLog`, `Ticks.logInPowers`, `Ticks.powerOfTen`,
  `Ticks.formatPower` and `Ticks.formatOwn`**, the rule a log axis's tick labels follow, and
  `Num.superscript`, which `UnitDisplay.superscript` now calls.
- **`Ticks.formatPowerTo`**, a reading in powers of ten with its mantissa to a given resolution:
  `1.1×10⁻¹⁸`.

### Changed

- **`ControlKind` gained a case, `Time of lo * hi * samples * loop`.** A match over it that names
  every case without a wildcard no longer compiles until it handles the new one.
- **`FieldMarks.licImage` and `FieldMarks.lic` take the compile measurer first**, so a backend's
  measurer can convolve.
- **`TickLabel` has a fourth field, `LabelTex`.** A record expression that builds one needs
  `LabelTex = None`.

### Fixed

- **`ScalarGrid.at` and `VectorGrid.at` copied the whole grid on every call.** One lookup on a
  512 × 512 grid took about 0.4 ms for a scalar grid and 0.9 ms for a vector grid; it now takes
  under 0.1 µs. The grid's arrays are read in place rather than copied without their units.
- **A field mark with no colour channel ignored the layer's opacity.** `Field.streamlines …
  |> Chart.withOpacity 0.3` drew at full strength, and so did quivers, direction fields, contours
  and line-integral convolutions. The opacity now applies with or without a channel, and a
  channel-coloured mark draws as before.
- **`Field.laplacian`'s documentation says it is a display operator**: its wide stencil is zero on
  a checkerboard, so it is not what a solver should step with.
- **A Greek letter in an axis title stopped `pdflatex` under the `Latex` and `Siunitx` unit
  styles.** Those titles are TeX source, which the TikZ backend passes through unescaped, and the
  quantity name in them was escaped for TeX's reserved characters only: `Chart.withYLabel "|ΔE|"`
  under `Theme.tikz` wrote a raw `Δ`. The Unicode-to-macro table moved from the TikZ backend into
  `Tex.escape`, so a TeX title and a plain text run are escaped alike, and `Tex` states the set:
  Greek, `µ Ω Å °`, `× ± − · ⋅ ≤ ≥ ≈ ≠ ∂ ∇ ∞`, super- and subscript digits and signs, two spaces
  and two dashes. Subscripts and the operators from `⋅` on are new to the table, and now also
  compile in plain text runs. The plain `Title` is unchanged.
- **Every package requires FSharp.Core ≥ 8.0.100**, stated in the build, instead of the version
  bundled with the SDK that packed it. Every `0.5.0` package required ≥ 10.1.401, so a project on
  SDK 10.0.1xx restored any of them with a downgrade warning (NU1605).
- **A log axis gave every tick the decimals of its finest one, and took its prefix from the top
  of its domain.** Over 10⁻³ … 10³ J the ticks read `0.000001` … `1.000000` in kJ. Over 10⁻¹⁸ …
  10⁸ J the axis was in MJ and every tick below 10⁻¹⁵ MJ read `0.000000000000000`, so the labels
  were not distinct. Each tick now takes the decimals it needs: `0.01`, `1`, `100`. A log axis
  spanning more than three decades is written in powers of ten, `10⁻⁸`, and takes no prefix. A
  narrower one takes its prefix as a linear axis does, and is written in powers of ten when a tick
  still lies outside [10⁻³, 10³]. `Erase.scale` follows the same rule.
- **The pointer's readout on a log axis written in powers of ten was in fixed point.** Over
  10⁻¹⁸ … 10⁸ J it read `0.000000000000000 J` at the bottom, `0.000010 J` in the middle and
  `100000000 J` at the top. It is now written in powers of ten as the ticks are, its mantissa to the
  places a pixel resolves: `1.0×10⁻¹⁸ J`, `1.0×10⁻⁵ J`, `1.0×10⁸ J`, in the widget and in the
  window.

## [0.5.0] — 2026-10-10

**Two backends that draw pixels on a screen.** `Marey.Backend.Gpu` renders on an OpenGL 3.3
context, offscreen or in a desktop window that runs a live figure; `Marey.Backend.Web` draws on a
canvas in a browser page, where a Fable app compiles the figure with the same code. Sixteen
packages, two of them new. Nothing breaks for a .NET caller: the two entries under `Changed`
concern numbers past 10¹⁵ and what is compiled under Fable.

### Added

- **`Num.roundAway`, `Num.roundEven`, `Num.fixedPoint` and `Num.shortest`**: a half rounded away
  from zero, a half rounded to even, .NET's "F" format and its "R" format, written in Marey's own
  arithmetic so that they mean the same under Fable. Every rounding in the projects that claim
  Fable goes through them. `Num.at`, `Ticks.format` and `Ticks.formatGeneral` are built on them, and write
  what they wrote before.

- **Fable can consume the packages.** `Marey.Core`, `Marey.Units`, `Marey.Anim`, `Marey.Plot`,
  `Marey.Fields`, `Marey.Interact`, `Marey.Backend.Svg` and `Marey.Backend.Web` carry their sources and project file
  under `fable/` and the `fable-library` tags, so a Fable app references them from NuGet as a
  .NET app does.
- **`SkiaPainter`**, the Skia backend's traversal on a canvas the caller owns, with a `claim`
  through which the caller draws a node itself and Skia skips it. `SkiaBackend` draws through
  it, and its PNG and PDF output is unchanged to the byte.

- **`Marey.Backend.Gpu`, a fifteenth package: GPU rendering.** `GpuRenderer` draws through the
  Skia backend's own code on an OpenGL 3.3 core context, offscreen, and reads the pixels back as
  a PNG (`RenderPng`) or an `Image` (`RenderImage`). One renderer keeps one context, one GPU
  context and one text measurer on its own thread, and any thread may call it, so
  `Frames.render FramePass.Concurrent gpu.RenderPng` works as it does for `SkiaBackend.renderPng`.
  Lay out with `gpu.Measurer`. The output agrees with the CPU raster to within edge
  antialiasing, not to the byte. `GpuBackend.probe ()` says which driver a renderer would get.
  A marker batch is one instanced draw: a million points render in about 5 ms on a desktop GPU,
  where the CPU raster takes 3.6 s. Batches the shader cannot draw as Skia would (a gradient, a
  dash, a non-mitered outline, inside an opacity layer, under a clip that is not a rectangle) are
  drawn by Skia. `instancedMarkers = false` sends every batch to Skia.
- **A window**: `Chart.show fig` and `Live.show live`, in `Marey.Backend.Gpu`, open a figure or a
  live figure on screen and return when it is closed. Drag to zoom, shift-drag to pan,
  double-click to reset, hover for a reading, Tab and the arrow keys for the controls, which are
  drawn in a strip above the figure. The window's pointer and keys become the `LiveMsg`s a
  notebook page sends and go through `Live.step`, so a session in a window steps to the states a
  notebook session would, and `Recorder.show` records a log that replays the same.
  `GpuWindow.Open` opens one without waiting and `Post` drives it from code. Linux and Windows.
- **`Marey.Backend.Web`, a sixteenth package: a canvas in a browser.** A Fable app compiles a
  figure in the page with the same code a .NET process runs, and `CanvasBackend` draws the scene
  on a 2-D canvas at the display's pixel density: all ten node cases, by the Skia backend's rules.
  `WebText.measurerNow ()` lays out with the canvas's own text measurements once `document.fonts`
  has loaded, and with the approximate widths before then, saying so in `Approximated`;
  `WebText.measurer ()` waits for the fonts. Marker batches of 2000 marks or more are drawn by
  WebGL2 and composited in place, about ten times faster than the canvas at 10⁵ marks. Its
  dependency is `Fable.Core`; it carries its sources under `fable/` like the other seven.

### Changed

- **`Num.at` past 10¹⁵ at the requested places** writes the value exactly where it used to stop
  at fifteen significant digits: `Num.at 5 4339842218598.2778` is `4339842218598.27783`, not
  `4339842218598.28`.
- **Under Fable**, `Chart.toFile`, `Chart.toSvg`, `Chart.toFileBytes`, `Frames.write`,
  `Frames.writeText` and `SvgBackend.renderToFile` are not compiled, because there is no file
  system to write to. A .NET caller sees no change.

### Fixed

- **The Skia backend drew a marker batch as one shape.** Two translucent markers that overlapped
  were no darker than one, so a dense cloud of points at 30% opacity read as a flat 30% tint, and
  every fill was drawn before every outline. Each mark is now filled and stroked on its own, as
  the SVG and TikZ backends always did. Batches of polygon markers draw about a third slower;
  circles are unchanged.
- **`Color.tryOfHex` accepted white space inside a field**: `"ff 000"` read as `#ff0000`. It is
  now `None`.
- **The projects that claim Fable compile under Fable 5.** They did not: a round-trip format, a
  rectangular array, a concurrent dictionary, an ordinal string comparer, file output, and a
  doc comment containing `*/` each stopped the compile or the module's load. With those gone,
  the 27 SVG baselines render byte for byte under Node, and CI checks that they still do.

## [0.4.0] — 2026-10-08

**Units are opt-in.** `open Marey` takes plain arrays and a unit per axis that may be stated or
left out; `open Marey.UnitsOfMeasure` is the mode every release so far has had, a witness per axis
checked at compile time. Code written against 0.3 that passes witnesses adds `open
Marey.UnitsOfMeasure` after `open Marey`; the other breaking changes are under `Changed`.

### Added

- **The unit-optional constructors.** `Chart.Line(xs, ys, xUnit = U.second, yUnit = U.metre)`, and
  one static member for each data-entry function: `Scatter`, `Area`, `Bar`, `Errorbar`, `Step`,
  `StepWith`, `Ribbon`, `Tile`, `Image`, `Segment`, `Text`, `Fit`, `Loess`, `LoessWith`,
  `Histogram`, `HistogramWith`, `Density`, `Ecdf`, `Heatmap`, `Hexbin`, `HexbinWith`, `Series`;
  `Chart.Category.Bar`, `Scatter`, `Boxplot`, `Violin`; `Field.Heatmap` and every field geom over
  a grid at measure 1; `Phase.Portrait`, `PortraitWith`, `Nullclines`, `Marks`, `Describe`;
  `Control.Slider`, `SliderBy`. A unit is an optional named parameter. Given one, the axis gets
  everything a witness gives it — the bracketed label, the prefix, a stat's derived unit, the
  dimension check when two layers share an axis — and the check runs when the figure is compiled.
- **`U.unspecified`**, the unit nobody stated: no bracket, no prefix, and no dimensional claim, so
  an unspecified layer shares an axis with anything and the axis takes the stated layer's unit.
  It is not `U.one`, which claims the data is dimensionless and is refused beside metres as before.
- **`Chart.add top fig`**, a figure's layers as the next series: the unit-optional spelling of
  `addLine` and its siblings. `Chart.fit`, `loess` and `loessWith` draw a smooth on its own, to be
  added over the data.
- **Channels without a witness in `plot { }`**: `x (fun r -> r.V)`, `y`, `colorBy`,
  `colorByWith`, `size`, `sizeWith` over an accessor returning plain `float`.
- **`U.parse "V/m"`**: a unit read from a string in UDUNITS-2's syntax — products, quotients,
  integer powers, parentheses, SI prefixes, and the ASCII spellings `degC`, `ohm`, `Angstrom`. It
  composes as `U.div` does, so the axis label is the same. Timestamps, logarithmic units and scale
  factors are refused with the reason. `U.tryParse` returns the reason instead of raising.
- **`U.tryAs target u`**: a parsed unit asserted back to a measure, checked on dimension and
  angle. `U.tryAs U.metre (U.parse "km")` is a `U<m>` with `ToSI = 1000`.
- **`IUnit`**, the witness as a value that may be absent, and `U.erase`, which reads one at
  measure 1 with `null` as `U.unspecified`.
- **A C# surface**, `using Marey.CSharp`, in the existing packages: `Units.Metre` and every other
  witness under one name with `Units.Div`, `Units.Parse` and the rest of the algebra; extension
  methods on `Figure` (`WithTitle`, `Add`, `ColorBy`, …); `fig.Save(SvgRenderer.Render, path, w,
  h)` with `SvgRenderer`, `TikzRenderer`, `PngRenderer` and `PdfRenderer`; `Category`, `Figures`
  for facets, and `Grids` for fields sampled from delegates.
- **`Marey.Interact`, a fourteenth package: a figure with controls, live in a notebook.** The
  guide's new §23 walks through it, and `examples/interact.dib` is a notebook built on it.
  - **Typed controls.** `Control.slider u label lo hi init` hands the view a `float<'u>` and
    labels itself in `'u`, under one prefix chosen from the range: a slider over coulombs per
    metre reads `2 nC·m⁻¹`, and passing it to a view over seconds does not compile.
    `Control.sliderBy` states the step, `Control.select` chooses among values by name, and
    `Control.checkbox` is a `bool`.
  - **The `interact { }` block** composes controls with `let! … and!` and a `return`. It is
    applicative, so a control cannot be chosen by another control's value and the set of controls
    is known before the view runs.
  - **A live figure is a value.** `Live.init`, `Live.step` and `Live.view` are pure: a session is
    a fold over the reader's messages (`LiveMsg`: `Set`, `Resize`, `Zoom`, `Pan`, `Reset`,
    `Hover`, `Leave`), replays from them, and runs in a test with no notebook. `Live.step` is
    total: a message it cannot use returns the state it was given.
  - **One arrangement for the whole widget.** The figure is laid out over the states the controls
    reach at their extremes (every combination up to 64, else one control at a time), so the plot
    area holds still while the reader drags. A state that still overflows grows the arrangement,
    and `LiveState.Grown` records each one.
  - **Zoom, pan and reset.** A dragged box becomes the panel's domains through the panel's own
    scales, on log axes and phase spaces as on linear ones; a pan slides them, by a factor on a
    log axis; a reset returns the view's. A zoom re-arranges, so the axes take the prefix of what
    they show. Panels whose axis the view drew identically zoom together, so a facet stays a facet.
  - **A readout.** `Live.readout` reads the data under the pointer, one line per axis, in the
    prefix and unit the axis's ticks are written in and to the places one pixel resolves. A hover
    changes no figure (`Live.drawsAlike`), so nothing is redrawn for one.
  - **Recordings.** `Live.recorder` displays as its live figure and keeps the messages the host
    steps, with the page's own times. `Recorder.stop` hands back the log, `Live.record` replays it
    from the reader's first act, and `Live.film` makes it a `Film`, laid out over every state the
    session reached. `LiveLog.toJson` and `ofJson` write and read a log exactly.
    `Recording.sample` turns any `Recording<'a>` into an `Anim<'a>`, sample-and-hold.
- **A live figure in Verso.** `Marey.Verso` opens an output channel for a cell whose value is a
  `Live` or a `Recorder`, steps the figure in the kernel on each message and answers with the
  figure, or with only the elements that changed when that is at most half the figure. A save
  writes the page at the state the reader left it in, with its controls disabled. This needs a
  Verso whose formatter context carries output channels and the cell's id, which 1.2.2 does not;
  on such a host, or with no channels, the page is written static and nothing is opened.
- **`Html.widget`**, a live figure's whole page: the controls at a state's values, the figure, and
  a script that posts the reader's changes, drags and pointer and swaps in the answer. The script
  formats no number. `Html.widgetFigure` and `Html.widgetPatch` are the figure and the patch it
  is sent.
- **The scene diff.** `Scene.diff` compares two scenes, by reference first and then by key, and
  names the keyed subtrees to replace. `SvgBackend.renderPatch` writes them as `(id, svg)` pairs
  with fresh `<defs>`, and keeps the patch only if swapping it into the old document gives the new
  one byte for byte. On a 50 000-point scatter, a moving cursor is a 577-byte patch of a 3.2 MB
  document.
- **Keys the data supplies.** `key` in the encoding block, or `Chart.withKeys`, names each row, and
  its mark is keyed by the name (`Keys.datum`), so it keeps its key when another row is removed.
  A key is one escaped segment, so a `/` in the data cannot forge a path, and a duplicate is
  refused where the keys are given, naming the key and its rows. `morphTo` pairs two keyed
  scatters by key: a datum moves to the datum of the same name, and one that only one side has
  fades in or out where it is. A handle names a datum by key, `curve.["S6"]`.
- **A panel as a pointer meets it.** `Chart.panelsIn` gives each drawn panel's plot rectangle,
  coord, scales and axes in a shot; `PanelView.toData` reads a point back as data,
  `PanelView.domainsOf` a rectangle as domains, and `PanelView.read` a point as text.
  `Chart.withPanelDomains` states one panel's domains.
- **`Chart.fitsIn`** says whether a figure fits a shot already laid out, and
  `Chart.arrangeOver` lays a shot out over a stated set of figures.
- **`UnitDisplay.quantity` and `quantityIn`**, the one place a value and its unit are written for
  a reader: `2 kV·m⁻¹`, `300 K`. The quiver key, `FixedPoint.describe` and a control's label all
  use it, and `UnitDisplay.withUnit` is where a number meets its unit text.
- **`Marey.Fields`, a thirteenth package: fields on dimensioned planes.** The guide's new §15
  walks through it.
  - **Grids.** `ScalarGrid` and `VectorGrid` on rectilinear nodes, sampled from a function
    (`Field.sampleScalar`, `sample`, `sampleFlow`) or built from data, with `Grid.linear` and
    `Grid.log` for nodes even on either kind of axis. Non-finite samples are kept, counted and
    never drawn.
  - **Derivatives in the units the types work out.** `Field.grad`, `div`, `curl`, `laplacian`
    and `jacobian`: the gradient of a potential in volts over metres is in V/m, and its
    divergence in V/m². A `Flow` has a different unit on each axis, so a phase space is a
    domain: a pendulum's divergence is in 1/s, and its curl does not compile.
  - **Scalar marks.** `Field.heatmap`, `contours` and `bands`, with a colorbar in the field's
    unit. Contour levels are the nice numbers of the field's own range, saddles are resolved by
    the asymptotic decider, and bands are filled even-odd with their holes.
  - **Arrows.** `Field.quiver` sizes each arrow by its magnitude through the quiver's own scale,
    with a key in the plot area's corner saying what one length means (`2 kV·m⁻¹`); `quiverBy`
    also colours by magnitude. `Field.directions` draws equal arrows for any vector grid, phase
    spaces included.
  - **Streamlines.** `Field.streamlines` draws evenly spaced lines 16 px apart (`streamlinesWith`
    states the separation), integrated on the page so they follow the field on a log axis or a
    phase space, closed where they return to their start, with no randomness. `streamlinesBy`
    colours each line by the field's magnitude.
  - **Line-integral convolution.** `Field.lic` smears a noise texture along the streamlines, so
    the flow shows at every pixel; `licBy` colours it by magnitude. A raster at the plot area's
    resolution times a stated oversample (`licWith`, `Field.licDefaults`), seeded from `Rng`, so
    a seed, a field and a plot size always give the same image. About 70 ms for a 480 × 300
    figure.
  - **Phase portraits.** `Phase.fixedPoints` finds a flow's fixed points where its nullclines
    cross and classifies each by its linearisation: saddle, stable or unstable node or focus,
    centre, or degenerate, with eigenvalues in `1/'t`. A centre is called one when the real part
    is under 10⁻³ of the imaginary (`fixedPointsWith` states another tolerance), because
    linearisation cannot tell a centre from a weak focus. `FixedPoint.describe` prints
    `centre (linear) at (0 rad, 0 rad·s⁻¹), λ = ±3.128i s⁻¹`. `Phase.nullclines` and
    `Phase.portrait` draw them.
- **`Stat.hexbin` and `Chart.hexbin`**: scattered points counted into hexagons, as a density in
  `1/('x·'y)` that integrates to one; positions in metres give a colorbar in m⁻². `hexbinWith`
  states the hexagons across and `Stat.hexbinIn` the lattice. The rows are spaced so the hexagons
  are regular on a plot area 1.6 times as wide as it is tall (`Stat.HexAspect`).
- **`Stat.loess` and `Chart.addLoess`**: a local regression smooth with R's defaults (span 0.75, a
  local quadratic, tricube weights, no robustness iterations), in the data's own units.
  `loessWith` states the span and the points, `loessIn` where to evaluate and the degree.
- **`Stat.bootstrap` and `Stat.bootstrapPairs`**: percentile bootstrap confidence intervals for
  any statistic of a sample or of (x, y) pairs, in the statistic's own unit, so a slope's interval
  is in m/s. The seed is an argument and the result carries it; resamples come from `Rng`, so a
  seed names one interval on every runtime. `bootstrapWith` and `bootstrapPairsWith` state the
  resamples and the level (defaults 2000 and 95%).
- **`Rng`, a specified pseudo-random generator, in `Marey.Core`.** SplitMix64, immutable, seeded
  explicitly: `Rng.seeded`, `next`, `unit`, `below`, `units`. Specified rather than taken from
  `System.Random`, so that a seed names the same sequence on every runtime, Fable included.
  Pinned against the published outputs for seed 0.
- **`TexCache` and `Typeset.sceneCached`**: typeset outlines that outlive one call, for a film.
  Pass one cache to every frame's pass and only the first frame runs `latex`: five frames with two
  formulas take 217 ms instead of 1082 ms. Keyed on the source, the preamble, the class options,
  the base size and the tools, so callers with different preambles never share an outline, and
  safe across threads. `Typeset.scene` keeps its per-call cache.
- **`Chart.withColorDomain`** states a continuous colour channel's domain rather than taking its
  values' range, in any unit of the channel's dimension: for two figures that must share a colour
  scale, and for a film.
- **`Chart.overlay`** draws one figure's layers over another's last panel: a heatmap under its
  contours, or any two figures over the same quantities.
- **Three examples.** `fields.fsx` draws two line charges every way the package draws a field
  and checks Gauss's law on the grid. `pendulum.fsx` is a phase portrait with and without
  friction, its fixed points classified, with Liouville's theorem checked. `dipole.fsx` films a
  rotating dipole and encodes it to `.mp4` with the colour scale held for the whole film.

### Changed

- **Breaking: the curried constructors that take a witness moved to `Marey.UnitsOfMeasure`.**
  `Chart.line U.second U.metre ts xs` and every function listed above in its curried form —
  together with the `add*` family, `colorBy`, `sizeBy` and `withColorDomain` — compile under
  `open Marey.UnitsOfMeasure` and not under `open Marey` alone. Under that `open`, the
  unit-optional members are compile errors that name the spelling to use, so a call cannot fall
  through to an axis with no unit.
- **Breaking: `plot` under `open Marey.UnitsOfMeasure` returns `Marey.UnitsOfMeasure.PlotBuilder`**,
  whose channels take their witness; leaving one out is a compile error there.
- `Marey.Units`' module `U` and `Marey.Fields`' module `Grid` are nested in `[<AutoOpen>]` modules
  (`SiWitnesses`, `GridAxes`). F# spells them as before after `open Marey`; the fully qualified
  `Marey.U.metre` without the `open` no longer resolves.
- **The axes, the gridlines, the legend and the colorbar have keys of their own** in the scene
  (`Keys.axisX`, `axisY`, `grid`, `legend`, `colorbar`, below the panel's), so a diff can replace
  one of them without the panel. An SVG gains a `<g id="…">` around each, and a TikZ file a
  comment; 25 baselines moved, and nothing else in them did.
- **`Handle.Row` is now `Handle.Mark`**, a `(Key * MarkRef) option`, where a `MarkRef` is a row or
  a datum, because a handle can name a datum by its key. `curve.[n]` builds a row as before.
- **`Layer` gained `RowKeys`**, the rows' own keys, empty when the data supplies none. A record
  expression that builds a `Layer` by hand needs the field.
- **A layer in rad/s refuses to share an axis with a layer in Hz.** Angle is dimensionless in SI,
  so the two have one dimension and `Figure.unify` let them share an axis, plotting an angular
  velocity and a frequency, a factor of 2π apart, on one scale. It now raises, naming both units,
  as it does for metres against joules. The angle is read from the witness's factors, so build
  angular units by composition (`U.div Derived.radian U.second`). A witness declared by hand with
  a compound symbol carries no factors and is not recognised; the guide says so.
- **A film holds its colour scale still.** An animated figure's continuous colour channels are
  drawn against the union of their domains over the shot's sampled frames, as the panel's gutters
  and prefixes already were, rather than against each frame's own range. A heatmap of a field
  that grows tenfold used to look the same in every frame, and now reads as growing. A quiver's
  arrows are scaled against the largest magnitude in the shot, for the same reason. `Shot` gained
  `ColourDomains` and `QuiverKeys`, and `QuiverKey` gained `Largest`. A still is unchanged.
- **`GeomOf` gained two cases.** `GeomField` is the geom a field layer is: a grid and what to
  draw of it, computed on the panel at compile time. `GeomPolygon` is a closed polygon per point,
  its vertices in data space, as a hexbin's hexagons are. A `match` over `Geom` that was
  exhaustive needs an arm for each.

### Deprecated

- **`Chart.Plain`**, for one release. Use `Chart.Line(xs, ys)`, which leaves the units
  unspecified, or pass `U.one` where the data is dimensionless.

### Fixed

- **A C# caller could not pass a witness.** `Marey.Core` and `Marey.Units` each exported a public
  type `Marey.U`, so C# refused every call naming one (CS0433); `Marey.Plot` and `Marey.Fields` did
  the same with `Marey.Grid`. No two packages now export one type name.
- **A Greek letter in a TikZ text run stopped `pdflatex`.** The escaper mapped µ and Ω and
  nothing else of the Greek block, so an axis whose conventional name is Greek, a density's `ρ`
  among them, was a hard "Unicode character" error. Greek letters are now set in math italic
  (`\ensuremath{\theta}`), as a quantity symbol is; µ and Ω keep their upright unit forms.
- **An axis in rad/s was titled `f [Hz]`.** The renderer named the unit by its dimension, s⁻¹,
  and the dimension's conventional name is a frequency's. A unit whose factors carry an angle is
  now written as composed (`rad·s⁻¹`), and the axis is named for the angle: `θ` for rad, `ω` for
  rad/s, `α` for rad/s². `Hz` and `f` are unchanged for anything without an angle.
- **`Marey.Tools.Tex`'s package readme still said an animation typesets once for the whole
  film**, which was not true of `Typeset.scene`, whose cache lives for one call. The guide was
  corrected at 0.3.0 and the readme was missed. Both now say how a film typesets once:
  `TexCache`, above.
- **An animation's early frames could overflow their own layout.** `Chart.arrangeUnion` pins one
  SI prefix for a whole shot, from the union of its states' domains, but measured each state
  under the prefix that state would choose alone. In a zoom that crosses a decade, the early
  frames were measured as `0 2 4 6 8` and drawn as `0.002 … 0.008`, in a gutter sized for the
  shorter labels. The prefixes are now chosen first and every state is measured under them.
  `Chart.fitsIn` reports whether a figure fits a shot, and every state of a shot now does.

## [0.3.1] — 2026-10-05

### Fixed

- **A geom's own arrays now follow the panel's final scales.** Error bars, ribbons, tiles and
  rectangles, segments, box plots, violins, an image's bounds, and the baselines of areas and bars
  were projected onto the panel when the geom was built, against the series' scales as they were
  then. A second layer with a wider domain, `Chart.withXDomain`/`withYDomain`, or a switch to a
  log axis then moved the layer's points and left those arrays behind. `Chart.addErrorbar` of
  ±0.5 m on a 0–10 m line drew a bar spanning 0–10 m, and a tile beside a wider scatter filled the
  panel. Present since 0.2.0. Single-layer figures, and layers that happened to share a domain,
  were always right, which is why no baseline moved with the fix.
- **A shot over states with differently shaped grids keeps every panel.** `Chart.arrangeUnion`
  took its outer grid from the one state with the most panels, so an animation between a 1×2
  and a 2×1 figure arranged against one shape, and the other state's extra panel had no region
  and was not drawn. The grid is now the union of every state's rows, columns and panel
  positions.
- **`SvgPathData.parse` no longer hangs on a number after `Z`.** `Z` takes no arguments, and a
  number following it was read as another `Z` that consumed nothing, so `M0 0 Z 1` looped forever,
  appending `ClosePath` until memory ran out. It is now refused by name, like every other
  malformed path.
- **`Scene.opacity nan` is refused.** NaN fails both comparisons the clamp is made of, so it
  reached the IR unchanged and each backend handled it differently. It now raises
  `ArgumentException`; every other value is still clamped into [0, 1].

### Security

- **A formula cannot run a shell command.** `Marey.Tools.Tex` starts `latex` with
  `--disable-write18`. The formula is the public `TextRun.Tex` field and may come from data, and
  on a TeX installation configured with `shell_escape = t`, `\immediate\write18{…}` in it ran
  the command. TeX Live's and MiKTeX's restricted default already refused commands outside their
  allow-list, so default installations were not affected.

### Changed

- **`Geom` carries data, and `GeomOf<nd>` is what gets drawn.** The union is now
  `GeomOf<'p>`, generic over the space its numbers live in; `Geom` is `GeomOf<1>`, data values
  with their measure spent by the typed constructor exactly as before. `Geom.project` puts one on
  a panel through a pair of scales, and the figure compiler calls it with the panel's final scales
  beside the points. `Geom.compile`, `compileFor` and `compileSubset` take a `GeomOf<nd>`; to draw
  a geom outside a figure, `Geom.onSeries s` projects it through its own series' scales.
  `BoxSpan` and `ViolinProfile` gained the same parameter. `Geom.text` and `Geom.textAt` carry no
  numbers and are generic in it. A layer's area or bar baseline is now the data value zero, and
  `Geom.project` clamps it into the panel.

## [0.3.0] — 2026-10-05

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

**M3 — *Motion*.** Complete, and shipping in the same release as M2.5 above. It closes the two
gaps in the IR that everything else in the milestone needs — the producer could not name what it
drew, and a group could not be faded — then adds the algebra the rest of the milestone is written
in, the two interpolations that algebra had nothing to say about (a transform and a shape), the
thing none of them can do on their own — **hold the layout still while the data moves** — the
storyboard block that says all of it in one place, the encoder that turns the frames into a file,
a text run that is no longer confined to one face, and a package that can set a fraction.

Twelve packages now, four of them new: `Marey.Anim`, `Marey.Tools.Video`, `Marey.Tools.Tex`, and
`Marey.Toolchain`, which ships only because the two tool packages depend on it.

[`examples/reel.fsx`](examples/reel.fsx) is the demo reel — the whole library in one shot, ending
in an `.mp4` — and it is the one file written for somebody who has read none of the rest.

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

**`Affine` joins the interpolation witness — a seventh overload, and the only one that is not
componentwise.** `interp a b t` on a transform decomposes both sides to
translate/rotate/scale/skew, interpolates those, and recomposes; the componentwise answer between
a 0° and a 180° rotation passes through the *zero matrix*, so a shape collapses to a point and
pops back out. The decomposition was already in `Marey.Core`; what is new is that it is reachable
through `interp`, `Interp.between`, `Interp.stops` and `Anim.tween` rather than only by name.

Rotations take the **shortest arc**, so a 350° → 10° tween goes forwards through 0°. A matrix does
not remember which way round its angle was written, so `rotate 181°` and `rotate -179°` are the
same six floats and tween the same way; the long way round needs two tweens or an explicit angle
track.

**`Morph` — path morphing as a function, with its policies visible.** `Morph.paths` flattens both
paths to polylines, splits them into subpaths, resamples each subpath to a fixed number of points
spaced evenly **by arc length**, and interpolates point by point. `Morph.anim` is the same morph
with the preparation hoisted out of the frame loop, and `Morph.prepare` returns both sides in the
common form so a still frame before the morph can be drawn without a pop.

It is a function rather than an eighth `Interp` overload because it has to choose three times, and
a `static member Interp(a, b, t)` has nowhere to record that it chose: how subpaths are paired
when the counts differ (`SubpathPadding`), how many samples the common parameterisation gets, and
what a closed path morphing into an open one produces (`ClosurePolicy`). Unmatched subpaths grow
out of a point of their own, so a second shape blooms where it belongs rather than flying in from
wherever the first one sits.

**One arrangement for a whole animation.** `Chart.arrangeUnion` samples an animation, lays out
every state, and takes the union: the arrangement no frame can overflow. `Chart.toSceneIn` then
draws each frame inside it, and `Chart.arrangeFor` gives the same thing for a single figure.

This exists because correct per-frame layout is wrong for animation. The plot rectangle is what is
**left over** once the widest tick label has taken its room, so a y domain climbing past 100 gains
a digit on every label at once, the gutter widens, and the axis crawls sideways while the data is
the thing that was supposed to be moving. Freezing the first frame's layout fails the other way,
since a later frame's labels are wider than the gutters that were sized for frame zero. The union
does neither, and tick labels still recompute every frame inside a rectangle that does not move.

The same union pins the **unit prefix**, so a zoom crossing a decade cannot step `mm → µm` and
change the axis title mid-shot. `Shot` reports how many samples it was taken over, because the
union is only as good as them.

**A ninth package: `Marey.Anim` is published.** It was in the repository from the algebra commit
and held back until something shipped could turn an animated figure into a picture. The two
functions above are that, so it ships. It still depends on `Marey.Core` alone and mentions no
`Figure`: the sampler `Chart.arrangeUnion` takes is a plain `float -> Figure`, so tweening a
`float<m>` does not make anyone install the grammar.

**The storyboard block.** `scene { … }` in `Marey.Storyboard` — §7.2's shape, re-spelled against
M2's grammar:

```fsharp
let intro =
    scene {
        let! curve = draw measured |> over 1.2<s>
        do! wait 0.5<s>
        do! parallel' [ curve |> morphTo fitted |> over 2.0<s> |> ease Ease.cubicInOut
                        curve |> zoomToX (0.0, 8.0) |> over 2.0<s> ]
        do! highlight curve.[42]
    }

let anim = animate 32 (Chart.size 640.0 400.0) intro
```

`let!` binds a **handle** — a name for something on stage, plus the duration it arrived over,
which is the default for every later verb applied to it. Not the animation itself: the handle is
a name and the scene is a value. The verbs are `draw`, `over`, `wait`, `parallel'`, `ease`,
`morphTo`, `zoomToX`/`zoomToY`, `highlight` and `fadeIn`; `Storyboard.animate` renders one into an
`Anim<Node>` laid out once against the union of everything it saw, and `Storyboard.stages` gives
the `Figure` on stage at any instant for anything else.

`parallel'` runs its branches from the same stage and takes the longest one's duration, with the
shorter ones holding their final state — `Anim.map2`'s rule, inherited rather than restated.

**`Chart.addressMarks`, and a mark that has a node of its own.** A scatter is *one* `Markers` node
holding N positions, which is why mark 42 had nothing to highlight. Naming a row splits that batch
three ways — before it, the mark under `Keys.mark`, after it — for two extra nodes rather than N.
Nothing splits until something is named, so every figure that addresses nothing is exactly the
figure it was.

The index is the datum's **original row**, not its position among the drawn marks: those differ
the moment a non-finite value earlier in the series goes undrawn. Only a scatter can honour it —
a line is one `Path`, and there is no node between its vertices to name.

**`Erase.tweenSeries`, `Erase.seriesValues` and `Keys.covers`.** Interpolating one packed series
into another is what `morphTo` is: both sides are resampled to the longer by index fraction and
interpolated pointwise, domains included, with the target converted into the unit on stage through
SI. `Keys.covers` is the segment-wise prefix test that makes a key a scope.

**Two guide chapters and two worked examples.** `docs/guide.md` §16 covers the algebra, the clock,
the easing catalogue, morphing and animating a figure; §17 covers the storyboard.
`examples/morph.fsx` draws the two morph behaviours that look like defects and are not, and
`examples/storyboard.fsx` writes a six-frame filmstrip of one storyboard and checks that the panel
holds still across all of it.

**The frame writer, and the concurrency contract underneath it.** `Frames.render` evaluates every
frame of a film; `Frames.write` and `Frames.writeText` also number them onto disk. `FramePass`
chooses between `Array.Parallel.map` and `Array.map`, and the two produce the same bytes in the
same order — asserted frame for frame on both the SVG and the PNG backend, because a face resolved
differently on another thread is a different PNG and no amount of SVG comparison would see it.

A **`Film`** is the pair a renderer needs: the scene at every instant, and the shot it was
arranged inside. It is a type rather than two arguments because the surface is already in the
`Shot`, and passing a second one beside it is a chance to render a film at a size its layout was
never sized for — which nothing would catch and which looks like a picture in the corner of a
page. `Film.ofStory` builds one from a storyboard, `Film.ofFigures` from a bare `Anim<Figure>`.

`Frames.times` is `round(duration * fps)` instants at `i / fps`, and **the final instant is not a
frame**: a frame occupies the interval that starts at its own time, so a 2 s film at 30 fps is 60
frames ending at 1.9667 s.

`FrameSequence` reports the `frame-%05d.png` pattern an encoder is pointed at, and `Stale` — every
file matching that pattern which this render did **not** write. A directory holding a previous,
longer render still has frames past the end of this one, `ffmpeg -i frame-%05d.png` reads until
the first gap, and the encode silently gains a tail from a shot that no longer exists. Reported
rather than deleted: emptying a directory is not a rendering function's decision.

**The contract is now in `Backend.fs`, where a backend author reads it.** An `IBackend` is
single-threaded — `Begin`/`Draw`/`Finish` is a state machine over a mutable surface — and what
crosses threads is a renderer *function*, `Extent<px> -> Node -> 'a`, which must be safe to call
concurrently; both built renderers achieve that by constructing their backend inside the call. An
`ITextMeasurer`, by contrast, **must** be thread-safe, and exactly one of them serves a whole
film. That asymmetry is not about speed: font resolution walks a family stack against whatever is
installed, so a measurer per thread is a chance for one frame of a video to be set in a different
typeface from the frame before it — invisible in a frame, invisible in review, unfixable after the
render.

**`MemoTextMeasurer`** is what makes sharing one affordable. It wraps any `ITextMeasurer` and
remembers `(FontSpec, string)`, so no string is ever measured twice and the hit path is a
lock-free dictionary read. `Measurements` and `Entries` are counters a test can read: in a serial
render they are equal, which is the cache's whole promise stated as an assertion.

**`Storyboard.shoot`** returns the `Shot` beside the `Anim<Node>` — one function, two answers, the
shape `compilePanel` already had. `Storyboard.animateWith` is `shoot` without the layout and is
unchanged.

**`Marey.Tools.Video`, a tenth package — a frame sequence becomes a video.** `Video.mux` runs
`ffmpeg`, which is resolved on `PATH` and *reported*: found with its version, or absent with the
sentence naming what to install. **A missing ffmpeg degrades rather than fails.**
`MuxUnavailable` is an outcome carrying both what to install and the fact that the frames are
still on disk, because they are — and a numbered directory is what every editor imports.

A subprocess rather than a binding, and licence is the deciding argument: `FFmpeg.AutoGen` links
`libav*` into the process, LGPL at best and GPL as soon as the build carries x264, which would
put an audit on every consumer of a plotting library. The package therefore has no third-party
dependency of any kind.

`Video.args` is a **pure function** from the options and the sequence to the argument vector, so
the exact command is a value that can be printed, diffed and gated — and it is gated, on a
machine with no ffmpeg installed. Two positions in it are load-bearing and silent when wrong:
`-framerate` before `-i` keeps every rendered frame, where the same number after `-i` resamples
(24 frames at 12 fps is 24 frames written correctly and **13** with the flag moved), and
`-fflags +bitexact` after `-i` is what stops the container carrying `encoder=Lavf63.1.101`.

`Video.inspect` reads the result back with `ffprobe` — codec, dimensions, frames *counted* rather
than claimed, and the declared rate — because exit status is a weak gate: an encoder that wrote
13 of 24 frames exits zero. `VideoInfo.BuildTags` is the reproducibility check and is empty.

`Video.check` refuses, before ffmpeg is started, on a sequence with leftover frames from a longer
render (the `Stale` trap above, and this is the consumer it was reported for) and on odd pixel
dimensions under a chroma-subsampled format — read out of the first frame's own PNG header rather
than derived from the extent, because the raster backend rounds an extent up.

**`Marey.Toolchain` is published, as an eleventh package.** It was in the repository from M2.5
and held back because nothing shipped depended on it. `Marey.Tools.Video` does, and a published
package may not depend on an unpublished one. It gained `Toolchain.runArgs`, which hands the
argument vector to the process unsplit — `ProcessStartInfo.Arguments` is one string that .NET
re-splits using Windows' quoting rules on every platform — and `Toolchain.commandLine`, which
renders a command for a log and is deliberately not round-trippable.

**Font fallback, and it reports.** `SkiaTextMeasurer` splits a run by coverage — per codepoint,
the first face that has it, which is CSS font matching's rule — shapes each piece in its own face
and sums the advances. Before this a codepoint outside the resolved face was the missing-glyph
box: 9.0 px at 12 px in Liberation Sans, and two empty rectangles where a CJK axis title should
be. It does not reach the unit system, every symbol of which is in any Latin face; it reached
every non-Latin title in the library.

`ITextMeasurer` did not move, and neither did `Layout.fs` or `Figure.fs`. The seam declared at M0
has now survived a shaper and a fallback list without a line of the layout code changing.

**`ResolvedFace` carries the account**: `Fallbacks`, the faces that covered what the primary could
not and the codepoints each covered, and `Missing`, the codepoints nothing covered and which are
therefore `.notdef`. Both are empty for a Latin figure, so the ordinary case reports nothing
rather than a story about itself. A resolved fallback has `File = None`, which is the same
admission `Matched = None` makes one level up: this render is not reproducible on another machine.

**`SkiaOptions.FontFiles`** states the whole chain — those faces, in that order, `.notdef` past
the end of them, and the machine never consulted. It is the reproducible answer to a mixed-script
figure, where `FontFile` pins one face and the empty default resolves against what is installed.
`FontFile` is unchanged and **does not fall back**: one face is the whole promise, and "stated and
obeyed" would mean less if a second face could arrive behind it. Setting both is an
`ArgumentException` at construction rather than a precedence rule nobody would guess.

A run's vertical band is now the **maximum** over the primary face and every face the run uses, so
a CJK label is taller than the Latin label beside it — which is what a browser does, and reserving
only the Latin band would clip the ideographs. A run one face covers folds over one face and is
unchanged, which is why every number M2.5 measured still holds.

**`Marey.Tools.Tex` — mathematical typesetting, as a twelfth package.** §10 of the design document
puts it plainly: a library that cannot set `\frac{\partial \vec{E}}{\partial t}` is not a maths
visualization library. `Typeset.scene` walks a scene, sets every run carrying a `TextRun.Tex` form
with `latex`, converts it to vector outlines with `dvisvgm`, and replaces the run with a filled
`Path`.

**The result is geometry, and that is what makes it worth having.** Once a formula is outlines,
the SVG, TikZ and Skia backends all draw it with the code they already had — no capability flag,
no per-backend typesetting story, and nothing above the IR involved. `TextRun.Tex` has carried TeX
source since M1 (D32) and nothing could set it until now.

**Missing TeX degrades and does not fail.** `TextRun.Text` is always set, so a figure on a machine
with no TeX renders exactly as it would have without the package, and `TypesetUnavailable` names
what to install. A single snippet TeX *rejects* degrades the same way and alone: that run keeps
its text and is reported with TeX's own error, while every other formula on the figure typesets.
That isolation is the reason each snippet gets its own engine run rather than sharing one document
— batching is about four times faster on ten labels and loses all ten to one bad one.

Two tools, resolved separately, because a container image can carry one without the other and
"which is missing" is the useful half of the message. `Typeset.report` prints what was found.
About 105 ms per **distinct** snippet, memoised on the source; the font size is applied as a scale
on the outline rather than as a second engine run, so one formula at two sizes costs one
invocation. No third-party `PackageReference` of any kind: §10 sanctions this route as
"acceptable as a plugin, unacceptable as a dependency", which is D75's argument for `ffmpeg` and
D62's for a font, a third time.

`SvgPathData.parse` and `TexOutline.ofSvg` are public and pure, because that is where a wrong
*picture* would come from and it is gateable with no TeX installed. Both refuse by name rather
than skipping: an elliptical arc (no exact cubic form), an unknown element, a `<use>` that resolves
to nothing. Dropping a drawing instruction in silence is D33's incident, and it is not repeated.

### Changed

- **§14's M3 roadmap line is corrected**, in the milestone it describes rather than in the
  documents sweep after it. It promised "MathJax text on web" — §10's option 2 — on a web backend
  that does not exist until M4, which §14's own M4 row and two previous plans all say. What ships
  is option 1, the LaTeX shell-out, which needs no new backend at all.
- **`Capabilities` has a tenth field** and `Node` a tenth case, so both break every backend
  implementation at compile time. That is the intended cost of an IR change and the reason the
  type is guarded.
- **`Layer` has a new field, `Addressed: int[]`** — the rows that get nodes of their own. Empty on
  every layer nothing names, which is why no rendered output moved.
- **`Marey.Plot` now depends on `Marey.Anim`.** The storyboard names `Anim` and `Figure` in one
  signature and there is no third place for it to stand. The direction is chosen by weight:
  `Marey.Anim` is 36 KB with no dependencies of its own, so a static-only consumer of the main
  package carries ten percent more assembly and no new package reference, where the other
  direction would put the whole plotting stack behind `Anim.tween`.
- **`Geom.compile` and `Geom.compileSubset` are one body.** Both now delegate to `Geom.compileFor`,
  which takes the channel batch and the addressed rows as options. Neither signature changed.
- **`Chart.arrangement` and `Figure.arrangement` now report the layout the figure is actually
  drawn in.** They were a second, hand-written copy of the grid `toScene` builds, and the copy
  omitted the figure's head row — so for any figure with a title or a subtitle they reported panel
  regions taller than the real ones, by exactly the height of the title. Untitled figures were
  unaffected, which is why it went unnoticed. There is now one grid, built once, and a test that
  says so. No rendered output changed.
- **Golden baselines moved**: `all-nine-nodes.{svg,tex}` are now `all-ten-nodes.{svg,tex}`, and
  every figure baseline gained the keyed groups described above. No geometry changed.

**`Marey.Html` renders a `Film` as a flipbook that plays**, and `Film` joins the Verso bridge —
taking it from nine displayed types to ten. An animation built with the `scene { … }` block now
displays as motion in a notebook cell instead of as a record field table, which is where the rest
of this milestone was previously invisible.

The frames are sampled into stills, stacked in one SVG, and cycled with SMIL `<animate>` on each
frame's opacity. **No `<style>` block** — a notebook output cell does not scope one, so CSS
`@keyframes` is the one mechanism `Marey.Html` may not use, and `<animate>` is an SVG element that
travels inside the fragment it animates. **No external tool**: a GIF or an `<video>` would put
ffmpeg on the display path, so a reader without it would see a still where a colleague sees motion,
from the same notebook, with nothing saying which.

`Html.film` samples `Html.FlipbookFrames` — 48, a *budget* rather than a frame rate, about half a
megabyte of markup and 40 KB gzipped. The effective rate is `48 / duration`, so a two-second film
is smooth and a six-second one is visibly stepped; `Html.filmAt` takes the count. A film renders at
`Film.Extent` and has no size argument, because its layout was arranged once against that surface
and every frame's text measured for it. One frame, zero frames, or a film of zero duration render a
still rather than an animation of one.

**`SvgBackend.renderScoped`**, which exists for the flipbook and nothing else. It namespaces every
id a render emits. SVG ids are scoped to the document rather than to a nested `<svg>`, so stacking
frames in one document made frame 5's `url(#qc)` resolve to frame 0's clip path and its
`<use xlink:href="#qm">` to frame 0's marker — invisible while the frames agree, and wrong exactly
where `highlight` makes one mark bigger. `SvgBackend.render` is `renderScoped ""` and every golden
baseline is byte-identical.

### Fixed

- **The documented cost of typesetting an animation was wrong, and is corrected.**
  `Marey.Tools.Tex` said its outline cache made an animation "cost it once, because the cache is
  keyed on the source rather than on the frame", and the guide repeated it. The cache is a local
  in `sceneWith`, so it collapses repetition *within* a scene — twelve facets sharing an axis
  title are one engine run, which was always true — and does not span calls. Typesetting a film
  frame by frame therefore pays a figure's worth of `latex` per frame: measured at 235 ms for the
  first frame and 1060 ms for five. No behaviour changed; the claim did. A test now pins both
  halves so it cannot drift back, and the guide says to typeset the still you publish rather than
  every frame of the reel. There is deliberately no film-level entry point yet — a shared cache
  that outlives a call needs a decision about what it is keyed on, and the close of a milestone is
  the wrong place to take it.

### Known limitations

- **Parallel scaling is sub-linear, and the number is measured rather than promised.** The
  measurer cache removes the per-frame cost of measuring, not the per-render one, and the union
  arrangement that warms it is serial by construction.
- **"Frames after the first cause zero measurements" is true only of a film whose text does not
  change.** The arrangement is frozen; the tick *values* inside it are not, so a sweeping domain
  produces labels the union pass never sampled. What holds is the bound: measuring is proportional
  to the distinct strings in the shot rather than to the frames — on a morph-then-zoom of 126
  frames, quadrupling the frame rate moved the count from 64 to 65.
- **The Skia backend still resolves its *drawing* face per frame.** One measurer serves the whole
  film's layout, which is where a `ResolvedFaces` report comes from; `SkiaBackend.renderPng`
  constructs its own backend, and therefore its own measurer, inside each call, which is also what
  makes it safe to call concurrently. Within one process the resolution is stable, and the gate
  that would catch it if it were not is stronger than the contract: the PNG frames of a concurrent
  render are byte-identical to the serial ones. Sharing one measurer across the draw path would
  serialise every text run in every frame behind one HarfBuzz lock, so it is a trade rather than a
  fix, and it is not taken here.
- Keys are positional, so they are stable under a restyle, a rename, a re-theme and a change of
  values — which is what an animation does between frames — and **not** under an edit that adds
  or removes a row, which shifts every later index. Identity across a data edit needs an id the
  data supplies, and the scene differ that would consume one is M4.
- **Only a scatter has addressable marks.** `Markers` is the one node that batches N data points
  into one; a `Path` is a single shape, and splitting a polyline in three would change the joins
  between the pieces rather than name one of them. `Chart.addressMarks` on a line layer carries
  the rows and never asks for them; a storyboard's `curve.[i]` on one raises instead.
- **A storyboard shows one figure at a time.** Two charts on one surface is a two-panel `Figure`,
  which `Chart.beside`, `Chart.above` and `Chart.facet` already spell; a second composition
  mechanism inside the storyboard would be a second layout system.
- **`parallel'` composes its branches left to right onto the stage as it stands**, so two branches
  that write the same property end with the later one's answer. A morph and a zoom meet on the
  domain, because a morph's domain follows its own data.
- **A morph does not land exactly on its target**, for the same reason `interp a b 1.0` does not
  land exactly on `b`: `a + (b - a) * t` is not exact at the endpoints. The endpoint law is stated
  against the scale of the larger endpoint here as everywhere else in the library.
- **A still frame lifted out of an animation is not the same picture as the figure rendered on
  its own.** The union arrangement sizes the gutters for the worst frame in the shot, so a frame
  extracted from one wears the whole shot's clothes. Accepted rather than argued away, and pinned
  by a test.
- **The union is only as good as its samples.** An animation with a spike between two sample times
  can still overflow the arrangement. The sample count is a stated argument to
  `Chart.arrangeUnion` and is reported back on the `Shot`; the default anyone should reach for is
  32.
- **A shot cannot freeze a panel it never saw.** `Chart.toSceneIn` measures any panel key the shot
  does not carry from that frame's own data, so an animation that grows a panel mid-shot gets
  per-frame layout for the new one rather than an exception.
- An overshoot survives `tween`, `map` and `stretch` and is **absorbed** by `andThen` and `map2`,
  which reach their components through the clamped seek because that is what "the shorter one
  holds" and "the first one has finished" mean. Ease the components, not the composition.
- Colour overshoot is gamut-limited and **alpha overshoot does not exist**: `Color.ofOklab` ends
  in an unconditional `clamp01` on alpha, so a fade eased with `Ease.backOut` overshoots its hue
  and lightness and not its opacity. Correct, and surprising enough to write down.
- **A morph of two curves is not a curve.** The output is always a polyline: an arc-length
  correspondence between two arbitrary cubics has no cubic answer, and fitting one would mean
  calling a residual zero. `Samples` is the resolution, and 64 holds a circle to about 0.06% of
  its radius.
- **A closed subpath's last morph sample repeats its first**, because the sampler traces the
  closing edge — otherwise a square would be morphed over three of its four sides — and lands back
  where it started. One point in `Samples`, invisible in every renderer.
- **Subpaths are paired by index, not by position**, so two paths that list their subpaths in
  different orders morph by crossing over. Pairing by proximity is a better default and a worse
  policy: it is unstable under exactly the small motions an animation is made of.
- **The start point of a closed subpath is not rotated to match its partner**, so a circle morphing
  into a square whose first vertex is a quarter-turn away twists as it goes. Ring alignment is a
  fourth policy and is not in this milestone.
- **A reflection cannot be tweened into a rotation without collapsing.** The orientation-reversing
  matrices are not connected to the orientation-preserving ones, so a decomposed tween between a
  transform and its mirror passes through a frame with zero determinant — the shape flattens to a
  line and comes back mirrored. That is the honest answer rather than a defect; what is gated is
  that it stays finite throughout.
- **A transform whose first column is zero loses its shear through the decomposition.** Such a
  matrix maps the plane onto a line, and no rotation-scale-shear factorisation records the
  direction the collapsed axis came from. `decompose` preserves what is left rather than producing
  NaNs that would propagate through a whole animation.

### M2.5's known limitations, unchanged

- A PNG or PDF is reproducible on the machine that rendered it unless `SkiaOptions.FontFile`
  states a face. `ResolvedFaces` reports what was chosen so the difference is visible.
- ~~The shaper works against one face and has no fallback list, so a codepoint the face lacks
  measures as the missing-glyph box.~~ **Closed by M3's font fallback, above.** What remains of it
  is deliberate: a *stated* `FontFile` is still obeyed alone and still draws a box for a codepoint
  it lacks — now reported on `ResolvedFace.Missing` rather than left to be found in the PNG.
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

[Unreleased]: https://github.com/Axym-Labs/marey/compare/0.7.0...HEAD
[0.7.0]: https://github.com/Axym-Labs/marey/compare/0.6.0...0.7.0
[0.6.0]: https://github.com/Axym-Labs/marey/compare/0.5.0...0.6.0
[0.5.0]: https://github.com/Axym-Labs/marey/compare/0.4.0...0.5.0
[0.4.0]: https://github.com/Axym-Labs/marey/compare/0.3.1...0.4.0
[0.3.1]: https://github.com/Axym-Labs/marey/compare/0.3.0...0.3.1
[0.3.0]: https://github.com/Axym-Labs/marey/compare/0.2.0...0.3.0
[0.2.0]: https://github.com/Axym-Labs/marey/compare/0.1.1...0.2.0
[0.1.1]: https://github.com/Axym-Labs/marey/compare/0.1.0...0.1.1
[0.1.0]: https://github.com/Axym-Labs/marey/compare/0.1.0-rc.1...0.1.0
[0.1.0-rc.1]: https://github.com/Axym-Labs/marey/releases/tag/0.1.0-rc.1
