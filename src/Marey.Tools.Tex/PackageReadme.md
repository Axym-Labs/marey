# Marey.Tools.Tex

Mathematical typesetting for [Marey](https://github.com/Axym-Labs/marey), a
dimensionally-typed visualization system for F#. **TeX sets the formula; the result is geometry,
so every backend draws it.**

```fsharp
#r "nuget: Marey.Tools.Tex"

open Marey

// A scene whose runs carry TeX — which the axis compiler already fills for you when the
// unit style is `Siunitx`.
let scene = figure |> Chart.toScene extent

let drawn, note = Typeset.sceneOrPlain TexOptions.Default extent scene
printfn "%s" note                                  // says what typeset, and what did not

let png = SkiaBackend.renderPng extent drawn       // Skia has never heard of TeX
```

`TextRun.Tex` has carried TeX source since M1 and nothing could set it. This runs `latex` and
`dvisvgm` over each such run and replaces it with the **filled outlines** TeX produced — so the
SVG backend, the TikZ backend and the Skia backend all render `\frac{\partial \vec{E}}{\partial t}`
with the code they already had, and no backend needs a typesetting story of its own.

## Both tools are looked for and *reported*

`latex` writes DVI; `dvisvgm` turns DVI into paths. Each is resolved on `PATH` separately, because
a container image can carry one without the other and "which one is missing" is the useful half of
the message.

```fsharp
printfn "%s" (Typeset.report ())
// latex found at /usr/bin/latex
// dvisvgm found at /usr/bin/dvisvgm — dvisvgm 3.6.1
```

**A missing toolchain is not a failure.** `TextRun.Text` is always set — a run's plain-Unicode
form is never optional — so a figure on a machine with no TeX renders exactly as it would have
without this package, and `TypesetUnavailable` carries the sentence naming what to install. So
does a single snippet TeX rejects: that run stays as text, is reported by name with TeX's own
complaint, and every other formula on the figure still typesets.

## What it costs

About 105 ms of `latex` and `dvisvgm` per distinct snippet, memoised on the source. An ordinary
figure carries two — the axis titles — so it is a fifth of a second on an export path you opt
into, and an animation pays it once for the whole film.

The size is applied as a **scale**, not as a second engine run, so one formula at 9 pt and 18 pt
costs one invocation. That is not what a TeX engine would do at 18 pt — real optical sizing picks
a different design — and it is the same trade matplotlib's `usetex` makes.

## Installing the toolchain

- **Debian/Ubuntu** — `apt install texlive-latex-base texlive-latex-recommended texlive-science dvisvgm`
- **macOS** — `brew install --cask mactex` (or BasicTeX plus `tlmgr install dvisvgm`)
- **Windows** — MiKTeX, which ships `dvisvgm`

`dvisvgm` is its own package on Debian and Ubuntu and is not pulled in by any `texlive-*` entry.

## Why a subprocess, and why not MathJax

§10 of the design document calls shelling out to TeX *"acceptable as a plugin, unacceptable as a
dependency"*, and that is the whole design brief: this package exists so that nothing in
`Marey.Core`, `Marey.Plot` or any backend acquires a TeX installation as a requirement. It takes
no third-party `PackageReference` of any kind.

The alternative on the table was MathJax through a JavaScript engine, which the roadmap put on the
web backend — a backend that does not exist until M4, and which would have left the other three
unable to set a fraction. Outlines reach all of them.

MIT licensed. Pre-1.0: the API is still free to move.
