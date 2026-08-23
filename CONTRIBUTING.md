# Contributing

## Build and test

```
dotnet build          # warning-free; warnings are errors
dotnet test           # 819 tests
```

`TreatWarningsAsErrors` is on for every project, set in `Directory.Build.props`. It is not
decoration: FS0064 — the unit-of-measure over-constraint warning — is a warning by default, and a
generic function that silently over-constrains its measures still compiles. Turning it into a
build failure is how that class of defect is caught at all. A change that needs a warning
suppressed needs a comment saying which warning and why.

The library targets `netstandard2.1`; the test project targets `net10.0`.

## The toolchains the tests shell out to

Two gates shell out: `pdflatex` compiles every golden `.tex` baseline, and `pdftoppm` rasterises
a PDF to check the page is not blank — the compiled one, and since M2.5 the Skia backend's own.
Both print a `SKIPPED:` line and pass when the toolchain is absent, which is right on a
contributor's machine and wrong in CI — so CI installs them and enforcement is real there.

The Skia tests do the same thing for a different missing thing: they probe the native library by
rendering a 4 × 4 scene, and print `SKIPPED: SkiaSharp native assets unavailable` with the actual
exception if it is not there.

**Read the test output rather than the green tick**: a local run that skips these has not checked
the TikZ backend or the raster one at all.

The package list and the reason for each entry are in `.github/tex-packages.txt`.

## Golden baselines

46 baselines, in SVG *and* `tikzpicture` source, tracked under `tests/Marey.Tests/golden/`. They
are text and are meant to be reviewed as a diff — that is why both formats were chosen.

There is exactly one binary baseline, `figure-image.png`, and it is the sidecar half of a pair:
`figure-image.tex` names a file and this is that file, so the two only mean anything together.
Its bytes come from Marey's own zero-dependency PNG encoder over a hand-written pixel array, so
nothing outside the repository can move them. **There is no PNG baseline of a rendered figure**,
and there will not be: a bitmap is not reviewable in a diff and would break on a Skia version
bump for reasons unrelated to Marey. The raster backend is covered by structure, determinism and
ink instead.

A failing golden writes the candidate output next to the baseline and names both paths. If the new
output is correct:

```
MAREY_UPDATE_GOLDEN=1 dotnet test
```

Accepting a baseline is a reviewable act. A pull request that moves a baseline should say in its
description what changed in the picture and why, because the diff shows what changed in the file
and those are not the same question.

`tests/Marey.Tests/README.md` documents the suite in full — the property generators, the tolerance
policy, the determinism checks and the seed sweep. Read it before adding tests.

## What the layering rules are

Three constraints hold across the whole tree, and a change that breaks one needs to argue for it
rather than work around it:

- **`Marey.Core` takes no dependencies.** Not a package, not a backend, nothing. Nor do
  `Marey.Units`, `Marey.Plot`, `Marey.Backend.Svg` or `Marey.Backend.Tikz`. `Marey.Backend.Skia`
  is the one package in the solution with a third-party reference, which is why it is a separate
  package, and a test asserts the other five against the built assembly graph rather than
  against the project files.
- **`Marey.Plot` references no backend.** Rendering enters as a function — this is why
  `Chart.toSvg` takes a renderer rather than importing one.
- **The scene IR has nine cases and gains none casually.** Axes, legends, error bars and bars are
  *compiled into* `Path`/`Text`/`Markers` by the layer above. A new node variant is a cost against
  every backend.

Comments throughout the source and tests cite decision numbers (`D18`, `D41`). They refer to the
project's implementation decision log, which is kept outside this repository; the numbers are
stable, and a comment that cites one is recording that the code is the way it is deliberately.

## Line endings

`.gitattributes` normalises to LF in the index **and** in the working tree. Both backends emit
`\n` unconditionally and the golden baselines are their recorded output, so a CRLF checkout makes
the working tree and the repository disagree about files whose whole purpose is to be compared as
text. The binaries under `golden/` and `fonts/` are marked as such in `.gitattributes` and are
not touched by any of this.

## Pull requests

- Branch from `dev`; `main` carries releases. CI runs on both.
- `dotnet build` warning-free and `dotnet test` green before review.
- One logical change per pull request. A baseline move, a new geom and a refactor are three.
- New public API needs a runnable example. Every example in `docs/guide.md` and `examples/` is
  extracted and executed by the test suite, so an example that stops working is a failing test
  rather than a stale document.

## Reporting a defect

A figure that renders wrongly is best reported as the shortest F# script that produces it, plus
the output. If it involves units, include the witnesses — most surprising axis labels are a
question about which unit was passed rather than about the axis compiler.
