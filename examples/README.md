# Examples

Twenty-two standalone scripts, one notebook, and one browser app. Start with [`reel.fsx`](reel.fsx) if you want to see what the library
does before reading how; start with [`free-fall.fsx`](free-fall.fsx) if you would rather read the
smallest complete thing first.

Each file is standalone. Run one with:

```
dotnet fsi examples/free-fall.fsx
```

Each script writes its figure into the working directory. The `#r "nuget: …"` lines at the top
resolve from nuget.org, so nothing needs to be built first. The notebook, `interact.dib`, opens in
Verso with the Marey extension installed; its figure is live in Verso 1.2.3 or newer, and
static anywhere else.

| Example | What it shows |
|---|---|
| [`free-fall.fsx`](free-fall.fsx) | The smallest complete figure. Both axis titles, the tick values and the scale are derived from two unit witnesses, and nothing is configured. |
| [`wave-packet.fsx`](wave-packet.fsx) | Prefix selection: data spanning ±15 mm and ±50 kPa labels itself in millimetres and kilopascals. Renders to SVG and to a `tikzpicture` for LaTeX. |
| [`oscillator.fsx`](oscillator.fsx) | The usual scientific figure — a model curve, measured points, error bars and a legend. Synthetic data, seeded so the output is reproducible. |
| [`paper-figure.fsx`](paper-figure.fsx) | The last ten minutes of a figure for a paper, done by the library, as one figure: a fit over its residuals sharing their time axis, three to one in height, with a rule at zero that moves neither axis; beside them, spanning both rows, a spectrum with its lines marked by arrows, ticks every 50 nm stated in metres and written in nanometres, minor ticks, a shaded passband, photon energy along the top and the legend outside. Lettered in reading order, under `Theme.publication`. |
| [`co2-record.fsx`](co2-record.fsx) | Real measurements with real uncertainties, and where the units story stops: a mole fraction in ppm is dimensionless, so the label is written by hand. |
| [`temperature-anomaly.fsx`](temperature-anomaly.fsx) | Real data in kelvin, and why an anomaly is **not** degrees Celsius — a units error no compiler can catch, since both witnesses share a dimension. |
| [`shaped-text.fsx`](shaped-text.fsx) | The same figure arranged by two text measurers and drawn by the same renderer, so the only difference in the two PNGs is what measuring text properly does to a layout. Also the vector PDF, and which font the machine actually resolved. |
| [`morph.fsx`](morph.fsx) | Path morphing, and the two things about it that look like defects and are not: a closed ring's start point is not rotated to match its partner, and subpaths pair by index. Writes the filmstrip the guide uses to show both. |
| [`storyboard.fsx`](storyboard.fsx) | The `scene` block: a figure drawn, morphed and zoomed at once, then one mark picked out by name. Writes a six-frame filmstrip, and checks that the panel holds still across all of it while the data moves. |
| [`frames.fsx`](frames.fsx) | Rendering an animation: one `map` over the frame times. Checks the three things that follow from an animation being a pure function of time — a frame rendered alone is the frame rendered in sequence, a concurrent render is the serial one, and the measuring is bounded by the distinct strings in the shot rather than by the frames. |
| [`warming-rate.fsx`](warming-rate.fsx) | The encoding block on a real table: x, y and a colour declared in one place, a fitted trend, and the same record faceted into one panel per era. The fitted slope is a `float<K/yr>` — a warming rate, typed as one. |
| [`video.fsx`](video.fsx) | From a storyboard to an `.mp4`, by shelling out to `ffmpeg`. The frame sequence is the product and the mux is a convenience over it, so a machine with no encoder still writes every frame and says so. Reads the container back rather than trusting the exit code. |
| [`fields.fsx`](fields.fsx) | Two line charges, drawn every way `Marey.Fields` draws a field: the potential and its equipotentials, field lines over filled bands, a quiver with its key, and line-integral convolution. The field comes out in V/m and its divergence in V/m², and the script checks Gauss's law on the grid. |
| [`dipole.fsx`](dipole.fsx) | A field in motion: a rotating dipole's potential, equipotentials and field lines, recomputed every frame and encoded to `.mp4`. The panel and the colour scale stay put while the field turns, and the script checks that every frame has the same labels. |
| [`pendulum.fsx`](pendulum.fsx) | A phase space with a different unit on each axis, radians and radians per second. The fixed points are found and classified, with eigenvalues in s⁻¹: centres hanging down, saddles standing up, stable foci once there is friction. Also checks Liouville's theorem on the grid: the flow's divergence is zero, then minus the friction. |
| [`integrators.fsx`](integrators.fsx) | Forward Euler, RK4 and velocity Verlet on one oscillator for 100 periods, and the energy each gains or loses, in joules. Euler's grows exponentially, RK4's falls steadily, Verlet's stays in a band. The acceleration is typed in m/s², and each claim is checked. |
| [`heat.fsx`](heat.fsx) | Heat in a steel plate, stepped by Crank–Nicolson at eight times the explicit scheme's limit, which forward Euler refuses with both numbers. The temperature is in kelvin and the diffusivity in m²/s, so the limit is a time. A film with one colour scale throughout, and the same trajectory under a time control as a live figure. |
| [`double-gyre.fsx`](double-gyre.fsx) | A field that changes in time and what it carries: the instantaneous streamlines, three pathlines and a streak released from one point, as a film. In a steady field the three would coincide; here the streak folds into the gyres. Checks that nothing crosses the walls and that a small triangle keeps its area. |
| [`fluid.fsx`](fluid.fsx) | Warm air rising in a closed box: Stam's stable fluids on a staggered grid, the heat a dye in kelvin with a buoyancy per kelvin. The warm air pools under the ceiling and its lower edge descends as the box fills. On the GPU when there is one, with the CPU and GPU step times printed; checks that the flow stays divergence-free, the air rises, nothing crosses the walls, and the GPU agrees with the CPU. |
| [`cylinder.fsx`](cylinder.fsx) | Flow past a cylinder at a Reynolds number of 100 by lattice Boltzmann: the wake sheds vortices from either side in turn. Drawn as its vorticity with LIC laid over it at partial opacity, the LIC convolved on the GPU when the figure is compiled with its measurer; a film of the street. Checks the Strouhal number, and the GPU against the CPU. |
| [`double-pendulum.fsx`](double-pendulum.fsx) | A chaotic system, three ways: the first arm's path over a single pendulum's phase portrait, a Poincaré section of the second arm each time the first swings through the vertical, and a film of the pendulum. The state is a record of angles and rates stepped through a `Tangent`. A start a nanoradian away is a different pendulum within half a minute, which is also why a browser gives a different one. |
| [`interact.dib`](interact.dib) | **A live figure**, in a notebook. Two line charges with a slider over the charge per length, typed `float<C/m>` and labelled `2 nC·m⁻¹`, a slider over their separation and a choice of mark. Drag to zoom onto a charge and the axes and the readout change to millimetres. A recorder keeps what the reader did and replays it as a film. |
| [`reel.fsx`](reel.fsx) | **The demo reel.** The whole library in one shot: unit witnesses, a storyboard that is a value, one arrangement for the whole shot so the panel holds still, a mark addressed by its row, TeX-set axis titles on the poster, a flipbook that plays in a browser with no encoder installed, and an `.mp4` at the end. Written for somebody who has read none of the rest. |

[`web/`](web/) is a page that compiles and draws its figures in the browser, through Fable and
`Marey.Backend.Web`: two sliders over a damped oscillation, and a cloud of up to a million points
drawn by WebGL2. Its README says how to build and serve it. It is compiled in CI and not run, since
running it takes a browser.

## The data

`data/` holds two committed datasets, both works of the US Government and not subject to
copyright. They are committed rather than downloaded so that running an example needs no network
and gives the same figure every time.

- `co2-mauna-loa.csv` — NOAA Global Monitoring Laboratory, annual mean CO₂ at Mauna Loa.
  Retains NOAA's own header, including the note about the 2022–23 eruption.
- `gistemp-annual.csv` — NASA GISS Surface Temperature Analysis (GISTEMP v4), land-ocean index.

## Tested

`tests/Marey.Tests/ExampleTests.fs` runs every file here on each `dotnet test` (the notebook as its
F# cells in order), against the working tree rather than the published packages, and fails if a script does not compile, writes no figure,
or **prints a `false`**. The printed booleans are claims about the library, so a false one means
either the example is wrong or the library has moved under it.
