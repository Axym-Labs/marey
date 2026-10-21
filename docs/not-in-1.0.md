# Not in 1.0

What Marey does not do yet. Each is meant to come in a 1.x release, as an addition: a new verb, a
new field on a record built with `with`, a new case of an open union (see
[Compatibility](compatibility.md)).

**Colour.** Colour norms (log, symmetric log, two-slope around a centre), cyclic ramps for phases
and angles, stated colorbar ticks, and a horizontal colorbar.

**Marks.** Grouped, stacked and horizontal bars; hatching; marker edges in their own colour;
contour labels; a 2-D histogram; triangulated data, and fields on non-uniform grids.

**Axes.** Dates and times on an axis; logit and asinh scales; a twin x axis; equal aspect on log
axes; a legend marker for a side of a twin axis with several series.

**Composition.** An inset with its own legend or colorbar.

**Output.** Rasterised layers inside a vector figure, EPS, and TeX in annotations.

**Interaction.** Picking a datum under the pointer; a raster in a live figure's bytes on the wire;
the window on macOS.

**Simulation.** Stiff solvers.

**Typesetting.** Mathematics without a TeX installation. Today a formula is set by `latex` and
`dvisvgm` (§10 of the guide), and without them its plain text is drawn.

**Animation.** Scene-level animation in the manner of manim (shapes and equations that write,
transform and move on their own timeline); the storyboard animates figures and their data.

**Under Fable.** Speed: a figure compiles in the browser, but more slowly than under .NET.
