# Compatibility

## What a version number promises

From 1.0.0, the public API is what [`api/`](../api) lists, package by package. A patch release fixes
defects and changes no public signature. A minor release adds, and removes, renames or retypes
nothing a 1.x release published. Anything else waits for a major release, with four exceptions,
which a minor release may make:

- **A new field in a public record.** `Figure`, `Panel`, `Layer`, `AxisConfig`, `Theme`,
  `PanelView` and `Shot` are built from a published value with `with` (`{ Theme.modern with … }`,
  `{ Chart.axis "t" with … }`), or only read. Code written that way keeps compiling when a field is
  added. A record expression that names every field does not, and is not covered. `PlotSpec`,
  `Handle`, `PanelLayout`, `LiveZoom`, `Story` and `Step` have no fields a user can name.
- **A new case of an open union.** `Geom`, `LegendPlacement`, `ZoomAxis` and the scene's `Node` gain
  cases as the library gains marks, places for a legend, ways to zoom and kinds of drawing. A
  `match` over one of them keeps a wildcard case; a `match` that names every case is not covered.
  A new case in any other public union waits for a major release.
- **What a figure looks like.** Drawing is not part of the API. A minor release may move a mark, a
  tick or a gutter, and lists it under `Changed` with the baselines that moved. A patch release
  moves a drawing only to fix a defect.
- **A newer Fable, SDK or toolchain** that a release is built and tested with, named under
  `Changed`.

Two formats are versioned on their own rather than by the package. A scene on the wire (`Wire`)
carries its format version, and a reader refuses one it does not know, naming the version. A
`LiveLog` saved as JSON is format version 1, which every 1.x reads. `LiveMsg`, which passes between
a kernel and a page of the same release, is not a format and may change in any release.

The packages are released together and depend on each other by exact version, so upgrading one
means upgrading all of them; referencing `Marey` does that in one line.

## What it runs on

| | |
|---|---|
| Target frameworks | netstandard2.1, except the GPU backend (net8.0) and the Verso extension (net8.0 and net10.0) |
| FSharp.Core | 8.0.100 or newer |
| .NET SDK | each release is checked on the 10.0.100 band and on the newest SDK |
| Fable | the nine packages that carry their sources are built and tested with Fable 5.20 |
| Verso | a live figure needs 1.2.3 or newer |
| The window | Linux and Windows; not macOS, which allows a window only on the main thread |
| External programs | `latex` and `dvisvgm` for typeset text, `ffmpeg` and `ffprobe` for video: found on `PATH`, reported when missing, and not required at any version |

The packages with sources for Fable are Core, Units, Anim, Plot, Fields, Interact, Sim,
Backend.Svg and Backend.Web.
