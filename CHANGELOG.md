# Changelog

Notable changes to Marey. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

All twelve packages are versioned and released together, so a version number identifies the whole
set rather than any one package. Four are new in this release: `Marey.Anim`, `Marey.Tools.Video`,
`Marey.Tools.Tex`, and `Marey.Toolchain` — which ships only because the two tool packages depend
on it, and a published package may not depend on an unpublished one.

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
not name what it drew, and a group could not be faded — the algebra the rest of the milestone is
written in, the two interpolations that algebra had nothing to say about (a transform and a
shape), the thing none of them can do on their own — **hold the layout still while the data
moves** — and, since motion was complete, the encoder that turns the frames into a file.

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

[Unreleased]: https://github.com/Jonathan-Schaefer-git/marey/compare/0.2.0...HEAD
[0.2.0]: https://github.com/Jonathan-Schaefer-git/marey/compare/0.1.1...0.2.0
[0.1.1]: https://github.com/Jonathan-Schaefer-git/marey/compare/0.1.0...0.1.1
[0.1.0]: https://github.com/Jonathan-Schaefer-git/marey/compare/0.1.0-rc.1...0.1.0
[0.1.0-rc.1]: https://github.com/Jonathan-Schaefer-git/marey/releases/tag/0.1.0-rc.1
