# The guide

A tour of Marey, in fifteen sections that build on one another: each introduces a few calls and
ends on a worked example that uses them, with what it draws. Every `fsharp` block runs, in order,
as one session (`tests/Marey.Tests/GuideTests.fs`), and every figure on these pages is drawn by the
block above it, so a page that stopped matching the library would fail the build.

**Figures**

1. [Start](01-start.md): install, a first figure, and the same figure as SVG, TikZ, PNG and PDF
2. [Units are the labels](02-units.md): witnesses, prefixes, composed and parsed units; Planck's law in two units
3. [Marks and layers](03-marks.md): the marks, styles, rules, spans and annotations; a resonance
4. [Axes and scales](04-axes.md): log scales, stated ticks, a second y axis; a Bode plot
5. [Data in a table](05-tables.md): typed statistics, channels, the `plot` block, facets; warming against CO₂
6. [A figure for a paper](06-paper.md): panels, proportions, shared axes, insets, letters, themes

**Physics**

7. [Fields](07-fields.md): grids, derivatives in their units, the field marks; a dipole and a phase space
8. [Simulation](08-simulation.md): integrators, invariants, events, sections, grids and fluids; a double pendulum and a vortex street

**Motion**

9. [Animation](09-animation.md): `Anim`, easings, films; the Bode plot sweeping its damping
10. [The storyboard](10-storyboard.md): the `scene` block, frames, video and typeset text

**Live**

11. [Notebooks](11-notebooks.md): Verso, what displays, films and large figures in a cell
12. [Interaction](12-interaction.md): controls, live figures as values, recordings; a driven pendulum's route to chaos
13. [The GPU, a window and the browser](13-gpu-window-browser.md)

**Underneath**

14. [From C#](14-csharp.md)
15. [The scene, and the way out](15-underneath.md)

Beside the guide: [Compatibility](../compatibility.md), [Not in 1.0](../not-in-1.0.md), and
[Why Marey is like this](../why.md).
