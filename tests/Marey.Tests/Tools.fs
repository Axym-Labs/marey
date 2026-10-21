/// The external programs this suite drives, named once.
///
/// Three gates shell out — `pdflatex` compiles every golden `.tex`, `pdftoppm` rasterises the
/// compiled page and the Skia PDF for the ink gate, and `dotnet fsi` runs every example and every
/// guide snippet — and before this module each of them answered "is it there?" for itself.
/// `TexToolchain` and `GuideTests` held two spellings of the same PATH walk, and `Raster` and
/// `ExampleTests` reached into `TexToolchain` for its copy, which coupled the ink gate and the
/// example runner to the TeX module for no reason beyond where the function happened to live.
///
/// `Toolchain` (in `Marey.Toolchain`) is the mechanism; this is the list. Keeping the two apart
/// is what lets D75's `ffmpeg` and D79's TeX engine declare themselves in *their* packages
/// without this file, or the test suite, knowing anything about them.
///
/// **Each tool is `Lazy`, so a suite walks `PATH` once per tool rather than once per test.** The
/// install strings match `.github/tex-packages.txt`, which is what CI actually installs — so a
/// reader who follows the failure message reproduces the CI environment rather than guessing at
/// it.
module Marey.Tests.Tools

open Marey

/// The TeX engine. `pdflatex` rather than `lualatex` or `xelatex` because it is the strictest of
/// the three about Unicode input, so a document that satisfies it satisfies all three.
let pdflatex: Lazy<ResolvedTool> =
    lazy
        Toolchain.resolve
            "a TeX distribution with texlive-latex-base, texlive-latex-extra, texlive-latex-recommended, texlive-pictures and texlive-science (see .github/tex-packages.txt)"
            "pdflatex"

/// The rasteriser behind the ink gate, which since M2.5 reads the Skia PDF as well as the
/// compiled TeX.
let pdftoppm: Lazy<ResolvedTool> =
    lazy Toolchain.resolve "poppler-utils (see .github/tex-packages.txt)" "pdftoppm"

/// The DVI engine and the DVI-to-paths converter behind `Marey.Tools.Tex`.
///
/// `latex` rather than `pdflatex`, which is a *different* entry from the one above and
/// deliberately so: the TeX gates ask "does this document compile" through `pdflatex`, and the
/// typesetting package reads DVI. Two questions, two binaries, two lines in a report.
let latex: Lazy<ResolvedTool> =
    lazy
        Toolchain.resolve
            "a TeX distribution with texlive-latex-base, texlive-latex-recommended and texlive-science (see .github/tex-packages.txt)"
            "latex"

let dvisvgm: Lazy<ResolvedTool> =
    lazy Toolchain.resolve "dvisvgm (see .github/tex-packages.txt)" "dvisvgm"

/// The SDK, for `dotnet fsi`. Present by definition when the suite is running under `dotnet
/// test` — but the suite can be run from a published test binary, so it is asked for rather than
/// assumed.
let dotnet: Lazy<ResolvedTool> =
    lazy Toolchain.resolve "the .NET SDK, from https://dotnet.microsoft.com/download" "dotnet"

/// What the three resolved to, as lines, for a run that wants to say what it checked.
///
/// `TexToolchain`'s header states the rule this serves: **read the test output rather than the
/// green tick.** A gate that skipped is indistinguishable from a gate that passed, from the
/// outside, and this is the inside.
let report () : string =
    [| Toolchain.report "the TeX gates did not run" (pdflatex.Force())
       Toolchain.report "the ink gates did not run" (pdftoppm.Force())
       Toolchain.report "the typesetting gates did not run" (latex.Force())
       Toolchain.report "the typesetting gates did not run" (dvisvgm.Force())
       Toolchain.report "the guide and example gates did not run" (dotnet.Force()) |]
    |> String.concat "\n"
