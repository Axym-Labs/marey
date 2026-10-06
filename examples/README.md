# Examples

Fourteen standalone scripts. Start with [`reel.fsx`](reel.fsx) if you want to see what the library
does before reading how; start with [`free-fall.fsx`](free-fall.fsx) if you would rather read the
smallest complete thing first.

Each file is standalone. Run one with:

```
dotnet fsi examples/free-fall.fsx
```

Each script writes its figure into the working directory. The `#r "nuget: …"` lines at the top
resolve from nuget.org, so nothing needs to be built first.

| Example | What it shows |
|---|---|
| [`free-fall.fsx`](free-fall.fsx) | The smallest complete figure. Both axis titles, the tick values and the scale are derived from two unit witnesses, and nothing is configured. |
| [`wave-packet.fsx`](wave-packet.fsx) | Prefix selection: data spanning ±15 mm and ±50 kPa labels itself in millimetres and kilopascals. Renders to SVG and to a `tikzpicture` for LaTeX. |
| [`oscillator.fsx`](oscillator.fsx) | The usual scientific figure — a model curve, measured points, error bars and a legend. Synthetic data, seeded so the output is reproducible. |
| [`co2-record.fsx`](co2-record.fsx) | Real measurements with real uncertainties, and where the units story stops: a mole fraction in ppm is dimensionless, so the label is written by hand. |
| [`temperature-anomaly.fsx`](temperature-anomaly.fsx) | Real data in kelvin, and why an anomaly is **not** degrees Celsius — a units error no compiler can catch, since both witnesses share a dimension. |
| [`shaped-text.fsx`](shaped-text.fsx) | The same figure arranged by two text measurers and drawn by the same renderer, so the only difference in the two PNGs is what measuring text properly does to a layout. Also the vector PDF, and which font the machine actually resolved. |
| [`morph.fsx`](morph.fsx) | Path morphing, and the two things about it that look like defects and are not: a closed ring's start point is not rotated to match its partner, and subpaths pair by index. Writes the filmstrip the guide uses to show both. |
| [`storyboard.fsx`](storyboard.fsx) | The `scene` block: a figure drawn, morphed and zoomed at once, then one mark picked out by name. Writes a six-frame filmstrip, and checks that the panel holds still across all of it while the data moves. |
| [`frames.fsx`](frames.fsx) | Rendering an animation: one `map` over the frame times. Checks the three things that follow from an animation being a pure function of time — a frame rendered alone is the frame rendered in sequence, a concurrent render is the serial one, and the measuring is bounded by the distinct strings in the shot rather than by the frames. |
| [`warming-rate.fsx`](warming-rate.fsx) | The encoding block on a real table: x, y and a colour declared in one place, a fitted trend, and the same record faceted into one panel per era. The fitted slope is a `float<K/yr>` — a warming rate, typed as one. |
| [`video.fsx`](video.fsx) | From a storyboard to an `.mp4`, by shelling out to `ffmpeg`. The frame sequence is the product and the mux is a convenience over it, so a machine with no encoder still writes every frame and says so. Reads the container back rather than trusting the exit code. |
| [`dipole.fsx`](dipole.fsx) | A field in motion: a rotating dipole's potential, equipotentials and field lines, recomputed every frame and encoded to `.mp4`. The panel and the colour scale stay put while the field turns, and the script checks that every frame has the same labels. |
| [`pendulum.fsx`](pendulum.fsx) | A phase space with a different unit on each axis, radians and radians per second. The fixed points are found and classified, with eigenvalues in s⁻¹: centres hanging down, saddles standing up, stable foci once there is friction. Also checks Liouville's theorem on the grid: the flow's divergence is zero, then minus the friction. |
| [`reel.fsx`](reel.fsx) | **The demo reel.** The whole library in one shot: unit witnesses, a storyboard that is a value, one arrangement for the whole shot so the panel holds still, a mark addressed by its row, TeX-set axis titles on the poster, a flipbook that plays in a browser with no encoder installed, and an `.mp4` at the end. Written for somebody who has read none of the rest. |

## The data

`data/` holds two committed datasets, both works of the US Government and not subject to
copyright. They are committed rather than downloaded so that running an example needs no network
and gives the same figure every time.

- `co2-mauna-loa.csv` — NOAA Global Monitoring Laboratory, annual mean CO₂ at Mauna Loa.
  Retains NOAA's own header, including the note about the 2022–23 eruption.
- `gistemp-annual.csv` — NASA GISS Surface Temperature Analysis (GISTEMP v4), land-ocean index.

## Tested

`tests/Marey.Tests/ExampleTests.fs` runs every file here on each `dotnet test`, against the working
tree rather than the published packages, and fails if a script does not compile, writes no figure,
or **prints a `false`**. The printed booleans are claims about the library, so a false one means
either the example is wrong or the library has moved under it.
