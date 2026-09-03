# Examples

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
| [`warming-rate.fsx`](warming-rate.fsx) | The encoding block on a real table: x, y and a colour declared in one place, a fitted trend, and the same record faceted into one panel per era. The fitted slope is a `float<K/yr>` — a warming rate, typed as one. |

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
