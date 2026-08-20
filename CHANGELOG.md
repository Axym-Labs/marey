# Changelog

Notable changes to Marey. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

All seven packages are versioned and released together, so a version number identifies the whole
set rather than any one package.

**Pre-1.0, and versioned accordingly.** The API is still free to move. Breaking changes are
listed under `Changed` or `Removed` with the reason and the replacement, and until 1.0 they may
land in a minor release.

## [Unreleased]

M2 — *Grammar* — in progress. Nothing here has shipped to nuget.org.

### Added

- `PackedChannel` and `Erase.reunify` in `Marey.Core`: one erased channel is a witness and an
  array, and two of them reunify into a `Series` with both measures typed inside one scope. The
  encoding computation expression is built on these.
- `Chart.ofPacked`, which takes a reunified series straight to a `Figure`.
- **A categorical scale.** `ScaleKind.Ordinal` and `Scale.ordinal`: `n` named levels in `n`
  equal bands, over `float<1>`. Named tick labels cross the erasure boundary with the scale, so
  a reloaded categorical axis still knows what its bands are called.
- `Levels` in `Marey.Plot`: the distinct values of a categorical key, in first-appearance order,
  with `Levels.sorted` for the alphabetical case.
- `Chart.Category.bar` and `Chart.Category.scatter`: a chart whose x is a category. The y axis is
  dimensioned as everywhere else, so `Chart.Category.bar U.metre keys values` labels its bands
  and its y unit with nothing configured.

### Changed

- **A bar chart's y domain now includes zero**, in both `Chart.bar` and `Chart.Category.bar`. A
  bar encodes magnitude by length, so a domain starting at the data's minimum drew a 50:1
  picture of a 2.4:1 ratio. Zero enters unpadded and only on the side it is missing from, so
  data straddling zero is unaffected. Charts of tightly clustered non-zero data will look
  different — and correct; `Chart.withYDomain` restores a narrow axis where that was deliberate.
- Two layers on one panel whose x or y are *different* categories now raise instead of unifying.
  A categorical scale is dimensionless, so the existing runtime dimension check could not tell
  two categories apart, and the second layer was drawn against the first layer's labels.

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

[Unreleased]: https://github.com/Jonathan-Schaefer-git/marey/compare/0.1.1...HEAD
[0.1.1]: https://github.com/Jonathan-Schaefer-git/marey/compare/0.1.0...0.1.1
[0.1.0]: https://github.com/Jonathan-Schaefer-git/marey/compare/0.1.0-rc.1...0.1.0
[0.1.0-rc.1]: https://github.com/Jonathan-Schaefer-git/marey/releases/tag/0.1.0-rc.1
