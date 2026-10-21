# Contributing

## Build and test

```
dotnet build          # warning-free; warnings are errors
dotnet test           # 1379 tests
```

`TreatWarningsAsErrors` is on for every project, set in `Directory.Build.props`. It is not
decoration: FS0064 — the unit-of-measure over-constraint warning — is a warning by default, and a
generic function that silently over-constrains its measures still compiles. Turning it into a
build failure is how that class of defect is caught at all. A change that needs a warning
suppressed needs a comment saying which warning and why.

The library targets `netstandard2.1`; the test project targets `net10.0`.

## The toolchains the tests shell out to

Programs the library does not ship are driven by the suite, and by two of the packages:

| Program | What it gates |
|---|---|
| `pdflatex` | compiles every golden `.tex` baseline — the only authority on whether emitted TeX is valid |
| `pdftoppm` | rasterises a PDF to check the page is not blank: the compiled TeX one, and since M2.5 the Skia backend's own |
| `dotnet fsi` | runs every `docs/guide/` snippet, the README's example, every `examples/*.fsx`, and the encoding-block compile checks |
| `ffmpeg` / `ffprobe` | `Marey.Tools.Video` encodes a frame sequence, and the container is read back rather than trusted |
| `latex` / `dvisvgm` | `Marey.Tools.Tex` sets a formula and converts it to outlines. `latex`, not `pdflatex`: dvisvgm reads DVI, and the two ask different questions |

All of them are resolved and run through **`Marey.Toolchain`**. The suite's own are named once in
`tests/Marey.Tests/Tools.fs`; the two packages resolve theirs for themselves, because a consumer
of `Marey.Tools.Video` has no test suite. Add a gate that needs another program by adding it there
rather than by walking `PATH` again — the walk and the `Process` were each written five times
across four files before that module existed, and one of the copies' bugs is described in
`decisions.md` D81.

Each prints a line saying what it found and **passes when the program is absent**, which is right
on a contributor's machine and wrong in CI.

The Skia tests do the same thing for a different missing thing: they probe the native library by
rendering a 4 × 4 scene, and print `SKIPPED: SkiaSharp native assets unavailable` with the actual
exception if it is not there. That one is not on `PATH`, and its absence takes out every raster,
PDF, frame and ink gate at once.

### The census, and the switch that makes it enforceable

**Read the test output rather than the green tick**: a local run that skips these has not checked
the TikZ backend, the raster one, the guide or the examples at all.

Reading is a thing a person does once and then stops doing, so it is mechanised.
`tests/Marey.Tests/Verification.fs` prints a census of all **nine** external dependencies on every
run, whether or not anything was found:

```
Marey toolchain census — 9 of 9 dependencies live
  pdflatex found at /usr/bin/pdflatex
  ...
```

An absent one is a skip locally and a **failure** in CI, because `MAREY_REQUIRE_TOOLCHAIN=1` is
set in both workflows. Set it yourself to check a change the way CI will:

```
MAREY_REQUIRE_TOOLCHAIN=1 dotnet test
```

The failure names every absent dependency at once, each with its own install sentence.

The package list and the reason for each entry are in `.github/tex-packages.txt`.

## Golden baselines

57 baselines, in SVG *and* `tikzpicture` source, tracked under `tests/Marey.Tests/golden/`. They
are text and are meant to be reviewed as a diff — that is why both formats were chosen.

There are exactly two binary baselines, and each is the sidecar half of a pair: a `.tex` names a
file and the `.png` is that file, so the two only mean anything together. `figure-image.png` is a
hand-written pixel array; `figure-field-lic.png` is a line-integral convolution computed from a
stated seed with nothing but IEEE arithmetic. Both are written by Marey's own zero-dependency PNG
encoder, so nothing outside the repository can move them. **There is no PNG baseline of a rendered figure**,
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

## The public API listing

`api/` holds one file per package: the compiler's signature of everything a user can reach, with
doc comments and private and internal declarations taken out. CI fails when it differs from the
code. A change that is meant to touch the public surface regenerates it in the same commit:

```
dotnet fsi tools/api/listing.fsx
```

The diff of `api/` is then the change to the API, for review, as a golden's diff is the change to
the picture.

## What the layering rules are

Four constraints hold across the whole tree, and a change that breaks one needs to argue for it
rather than work around it:

- **`Marey.Core` takes no dependencies.** Not a package, not a backend, nothing. Nor do
  `Marey.Units`, `Marey.Plot`, `Marey.Backend.Svg` or `Marey.Backend.Tikz`. `Marey.Backend.Skia`
  is the one package in the solution with a third-party reference, which is why it is a separate
  package, and a test asserts the other five against the built assembly graph rather than
  against the project files.
- **`Marey.Plot` references no backend.** Rendering enters as a function — this is why
  `Chart.toSvg` takes a renderer rather than importing one, and why `Frames.render` takes one too.
  It does reference `Marey.Anim`, and that edge was argued rather than assumed: the storyboard
  block names an `Anim` and a `Figure` in one signature, `Marey.Anim` is 36 KB with no
  dependencies of its own, and the other direction would put the whole plotting stack behind
  `Anim.tween`.
- **`Marey.Core` and `Marey.Anim` stay Fable-compatible.** Both `.fsproj` files say so, and it is
  why the frame writer is in `Marey.Plot` despite naming nothing above `Anim<Node>`: `System.IO`
  and `Array.Parallel` do not belong in either. A change that puts a concurrency primitive or a
  file handle in one of those two has broken something the packages promise.
- **The scene IR has ten cases and gains none casually.** Axes, legends, error bars and bars are
  *compiled into* `Path`/`Text`/`Markers` by the layer above. A new node variant is a cost against
  every backend — the tenth, `Opacity`, was added in M3 because fading a composited group cannot
  be expressed by multiplying alpha through a subtree, and it broke every backend at compile time
  on the way in. That is the intended cost.

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

- Branch from `main` and open the pull request against it. CI runs on every pull request and on `main`.
- `dotnet build` warning-free and `dotnet test` green before review.
- One logical change per pull request. A baseline move, a new geom and a refactor are three.
- New public API needs a runnable example. Every example in `docs/guide/` and `examples/` is
  extracted and executed by the test suite, so an example that stops working is a failing test
  rather than a stale document. A guide block that ends on a figure and says `output=NAME` draws
  `docs/figures/NAME.svg`; a changed drawing fails like a golden, and `MAREY_UPDATE_GOLDEN=1`
  accepts it.

## Reporting a defect

A figure that renders wrongly is best reported as the shortest F# script that produces it, plus
the output. If it involves units, include the witnesses — most surprising axis labels are a
question about which unit was passed rather than about the axis compiler.
