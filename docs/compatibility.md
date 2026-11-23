# Compatibility

## What a version number promises

From 1.0.0, the public API is what [`api/`](../api) lists, package by package. A patch release fixes
defects and changes no public signature. A minor release adds, and removes, renames or retypes
nothing a 1.x release published. Anything else waits for a major release, with five exceptions,
which a minor release may make:

- **A new field in a public record.** `Figure`, `Panel`, `Layer`, `AxisConfig`, `Theme`,
  `PanelView`, `Shot`, `GeomStyle` and `Channel` are built from a published value with `with`
  (`{ Theme.modern with … }`, `{ Chart.axis "t" with … }`), or only read. Code written that way keeps compiling when a field is
  added. A record expression that names every field does not, and is not covered. `PlotSpec`,
  `Handle`, `PanelLayout`, `LiveZoom`, `Story` and `Step` have no fields a user can name.
- **A new case of an open union.** These gain cases as the library grows: `Geom` and `FieldMark`,
  for marks; `ChannelMap`, for ways a channel maps data to appearance; `ScaleKind`, for kinds of
  scale; `TickSpec`, `MinorSpec` and `TickFormat`, for ways to place and write ticks;
  `LegendPlacement`, for places to put a legend; `ZoomAxis`, for ways to zoom; and the scene's
  `Node`, `MarkerShape` and `Paint`, for kinds of drawing, marker and fill. A union a later release
  adds is open when its doc comment says so. A `match` over an open union keeps a wildcard case; a
  `match` that names every case is not covered. A new case in any other public union waits for a
  major release.
- **A new member of an interface only Marey implements.** `IScaleErased`, `PackedSeries` and
  `PackedScale` are made by the library and handed to code that reads them. An implementation of
  one outside Marey is not covered.
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
| Fable | the ten packages that carry their sources are built and tested with Fable 5.28 |
| Verso | a live figure needs 1.2.3 or newer |
| The window | Linux and Windows; not macOS, which allows a window only on the main thread |
| External programs | `latex` and `dvisvgm` for typeset text, `ffmpeg` and `ffprobe` for video: found on `PATH`, reported when missing, and not required at any version |

The packages with sources for Fable are Core, Units, Anim, Plot, Fields, Interact, Sim,
Uncertainty, Backend.Svg and Backend.Web.
