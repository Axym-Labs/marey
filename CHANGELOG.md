# Changelog

Notable changes to Marey. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

All seven packages are versioned and released together, so a version number identifies the whole
set rather than any one package.

**Pre-1.0, and versioned accordingly.** The API is still free to move. Breaking changes are
listed under `Changed` or `Removed` with the reason and the replacement, and until 1.0 they may
land in a minor release.

## [Unreleased]

Nothing yet.

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
