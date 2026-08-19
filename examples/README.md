# Examples

Each file is standalone. Run one with:

```
dotnet fsi examples/free-fall.fsx
```

It writes its figure into the directory you ran it from. The `#r "nuget: …"` lines at the top
resolve from nuget.org, so nothing needs to be built first.

| Example | What it shows |
|---|---|
| [`free-fall.fsx`](free-fall.fsx) | The smallest complete figure. Both axis titles, the tick values and the scale are derived from two unit witnesses, and nothing is configured. |
| [`wave-packet.fsx`](wave-packet.fsx) | Prefix selection: data spanning ±15 mm and ±50 kPa labels itself in millimetres and kilopascals. Renders to SVG and to a `tikzpicture` for LaTeX. |
| [`oscillator.fsx`](oscillator.fsx) | The usual scientific figure — a model curve, measured points, error bars and a legend. Synthetic data, seeded so the output is reproducible. |
| [`co2-record.fsx`](co2-record.fsx) | Real measurements with real uncertainties, and an honest look at where the units story stops: a mole fraction in ppm is dimensionless, so you write the label yourself. |
| [`temperature-anomaly.fsx`](temperature-anomaly.fsx) | Real data in kelvin, and why an anomaly is **not** degrees Celsius — a units error no compiler can catch, since both witnesses share a dimension. |

## The data

`data/` holds two committed datasets, both works of the US Government and not subject to
copyright. They are committed rather than downloaded so that running an example needs no network
and gives the same figure every time.

- `co2-mauna-loa.csv` — NOAA Global Monitoring Laboratory, annual mean CO₂ at Mauna Loa.
  Retains NOAA's own header, including the note about the 2022–23 eruption.
- `gistemp-annual.csv` — NASA GISS Surface Temperature Analysis (GISTEMP v4), land-ocean index.

## These are tested

`tests/Marey.Tests/ExampleTests.fs` runs every file here on each `dotnet test`, against the
working tree rather than the published packages, and fails if one does not compile, writes no
figure, or **prints a `false`**. The printed booleans are claims about the library aimed at a
reader; a false one means either the example is wrong or the library moved under it.

That gate is not theoretical. `wave-packet.fsx` first claimed that a `V/m` axis would label
itself `MV/m`. It printed `false`, exited 0, and the suite was green.
