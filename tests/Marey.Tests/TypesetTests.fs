/// D79 — mathematical typesetting, and the two halves it splits into.
///
/// **Most of this file needs no TeX installed**, and that is deliberate rather than convenient.
/// Phase 6 recorded the lesson: D75 claimed its package was "testable only for exit status and
/// container-level facts" and was wrong about itself, because `Video.args` turned out to be a
/// pure function and the flags that mattered were gated on a machine with no encoder. The same
/// split is sharper here. Everything that can silently produce a *wrong picture* — a misparsed
/// cubic, a reflected control point on the wrong side, a `<use>` resolved to nothing, a factor
/// of 1.00375 between TeX's point and everyone else's — is a pure function over a string.
///
/// What genuinely needs the toolchain is: does a real snippet come back as ink, does a bad one
/// fail without taking the figure with it, and does the absence of TeX degrade. The last of
/// those is **gate 13**, and it is checked on this machine *because* TeX is installed here:
/// `Typeset.sceneWith` takes the tools, so "not installed" is reachable by handing it a tool
/// that is not.
module Marey.Tests.TypesetTests

open System
open Xunit
open Marey

// ======================================================================================
// Fixtures
// ======================================================================================

let private extent: Extent<px> = { Width = 400.0<px>; Height = 250.0<px> }

let private ink = Color.rgb 0.1 0.1 0.12

let private run (tex: string option) (plain: string) : TextRun =
    { Text = plain
      Tex = tex
      At = { X = 0.5<nd>; Y = 0.5<nd> }
      Font =
        { Family = "serif"
          Size = 10.0<pt>
          Weight = 400
          Italic = false }
      Paint = Solid ink
      Anchor = AnchorStart
      Baseline = BaselineAlphabetic
      Rotation = 0.0<rad> }

/// A tool that is not there, for the gate that has to reach the branch a machine with a TeX
/// installation never takes.
let private absent (name: string) : ResolvedTool =
    { Name = name
      Path = None
      Install = "a test's idea of " + name }

let private available =
    lazy ((Tools.latex.Force()).IsAvailable && (Tools.dvisvgm.Force()).IsAvailable)

/// Run a gate that needs the real toolchain, or say loudly that it was not run.
///
/// `TexToolchain`'s rule, which this package makes load-bearing: a figure that typeset and a
/// figure that did not both render and both look reasonable, so the green tick says less here
/// than anywhere else in the suite. Read the output.
let private withTex (body: unit -> unit) =
    if available.Force() then
        body ()
    else
        Console.WriteLine("SKIPPED: " + Typeset.report ())

let private endPoints (segs: PathSeg[]) =
    segs
    |> Array.choose (function
        | MoveTo p
        | LineTo p
        | CubicTo(_, _, p) -> Some p
        | ClosePath -> None)

// ======================================================================================
// SVG PATH DATA — the pure half, and where a wrong picture would come from
// ======================================================================================

module ``svg path data`` =

    let private pt (x: float) (y: float) : Vec2<pt> = { X = x * 1.0<pt>; Y = y * 1.0<pt> }

    let private ok (d: string) =
        match SvgPathData.parse d with
        | Ok segs -> segs
        | Error why -> failwithf "'%s' did not parse: %s" d why

    let private near (expected: float) (actual: float<pt>) name =
        Assert.True(
            abs (expected - float actual) < 1e-9,
            sprintf "%s was %.12f, expected %.12f" name (float actual) expected
        )

    [<Fact>]
    let ``moveto lineto and cubic, absolute`` () =
        let segs = ok "M1 2 L3 4 C5 6 7 8 9 10 Z"

        Assert.Equal(4, segs.Length)
        Assert.Equal<TexSeg>(TexSeg.MoveTo(pt 1.0 2.0), segs.[0])
        Assert.Equal<TexSeg>(TexSeg.LineTo(pt 3.0 4.0), segs.[1])
        Assert.Equal<TexSeg>(TexSeg.CubicTo(pt 5.0 6.0, pt 7.0 8.0, pt 9.0 10.0), segs.[2])
        Assert.Equal<TexSeg>(TexSeg.ClosePath, segs.[3])

    /// Relative commands accumulate, and `z` returns the current point to the subpath's start —
    /// which the *next* relative command is measured from. Getting that wrong draws the second
    /// subpath somewhere else entirely.
    [<Fact>]
    let ``relative commands accumulate, and close returns to the subpath start`` () =
        let segs = ok "m1 1 l2 0 l0 2 z l5 5"

        Assert.Equal<TexSeg>(TexSeg.MoveTo(pt 1.0 1.0), segs.[0])
        Assert.Equal<TexSeg>(TexSeg.LineTo(pt 3.0 1.0), segs.[1])
        Assert.Equal<TexSeg>(TexSeg.LineTo(pt 3.0 3.0), segs.[2])
        Assert.Equal<TexSeg>(TexSeg.ClosePath, segs.[3])
        // Back at (1,1), not at (3,3).
        Assert.Equal<TexSeg>(TexSeg.LineTo(pt 6.0 6.0), segs.[4])

    [<Fact>]
    let ``horizontal and vertical keep the other ordinate`` () =
        let segs = ok "M10 20 H30 V40 h5 v5"

        Assert.Equal<TexSeg>(TexSeg.LineTo(pt 30.0 20.0), segs.[1])
        Assert.Equal<TexSeg>(TexSeg.LineTo(pt 30.0 40.0), segs.[2])
        Assert.Equal<TexSeg>(TexSeg.LineTo(pt 35.0 40.0), segs.[3])
        Assert.Equal<TexSeg>(TexSeg.LineTo(pt 35.0 45.0), segs.[4])

    /// **`S` reflects the previous cubic's second control point through the current point**, and
    /// dvisvgm uses it in almost every glyph. A reader that took the control point unreflected,
    /// or reflected it through the origin, produces a curve that is smooth in the wrong
    /// direction — a letter with a kink in it, which is exactly the kind of wrongness nobody
    /// spots in a 9 pt axis title.
    [<Fact>]
    let ``a smooth cubic reflects the previous control point through the current point`` () =
        let segs = ok "M0 0 C1 1 2 2 3 3 S8 4 9 5"

        match segs.[2] with
        | TexSeg.CubicTo(c1, c2, p) ->
            // Previous second control point (2,2), current point (3,3): 2*3 - 2 = 4.
            near 4.0 c1.X "reflected c1.X"
            near 4.0 c1.Y "reflected c1.Y"
            near 8.0 c2.X "c2.X"
            near 5.0 p.Y "endpoint y"
        | other -> failwithf "expected a cubic, got %A" other

    /// After anything that is not a cubic, `S`'s first control point is the current point itself.
    [<Fact>]
    let ``a smooth cubic after a line uses the current point`` () =
        let segs = ok "M0 0 L3 3 S8 4 9 5"

        match segs.[2] with
        | TexSeg.CubicTo(c1, _, _) ->
            near 3.0 c1.X "c1.X"
            near 3.0 c1.Y "c1.Y"
        | other -> failwithf "expected a cubic, got %A" other

    /// **A quadratic is a cubic, exactly.** `p0 + 2/3(q - p0)` and `p2 + 2/3(q - p2)` is not an
    /// approximation and the numbers say so: from (0,0) through (3,3) to (6,0), the control
    /// points are (2,2) and (4,2).
    [<Fact>]
    let ``a quadratic becomes the cubic it is`` () =
        let segs = ok "M0 0 Q3 3 6 0"

        match segs.[1] with
        | TexSeg.CubicTo(c1, c2, p) ->
            near 2.0 c1.X "c1.X"
            near 2.0 c1.Y "c1.Y"
            near 4.0 c2.X "c2.X"
            near 2.0 c2.Y "c2.Y"
            near 6.0 p.X "endpoint x"
            near 0.0 p.Y "endpoint y"
        | other -> failwithf "expected a cubic, got %A" other

    [<Fact>]
    let ``a smooth quadratic reflects the previous quadratic's control point`` () =
        let segs = ok "M0 0 Q3 3 6 0 T12 0"

        match segs.[2] with
        | TexSeg.CubicTo(c1, _, _) ->
            // Reflected control point is (9,-3); the cubic's first control is 6 + 2/3*(9-6) = 8.
            near 8.0 c1.X "c1.X"
            near -2.0 c1.Y "c1.Y"
        | other -> failwithf "expected a cubic, got %A" other

    /// **A repeated argument pair after `M` is a lineto**, per SVG's grammar, and dvisvgm relies
    /// on it. A reader that treated them as more movetos would leave a glyph as a scatter of
    /// disconnected points and fill nothing.
    [<Fact>]
    let ``extra coordinates after a moveto are linetos`` () =
        let segs = ok "M1 1 2 2 3 3"

        Assert.Equal(3, segs.Length)
        Assert.Equal<TexSeg>(TexSeg.MoveTo(pt 1.0 1.0), segs.[0])
        Assert.Equal<TexSeg>(TexSeg.LineTo(pt 2.0 2.0), segs.[1])
        Assert.Equal<TexSeg>(TexSeg.LineTo(pt 3.0 3.0), segs.[2])

    /// **Numbers run together without separators, and this is not a corner case** — it is what
    /// dvisvgm actually writes. `.5.5` is two numbers and `1-2` is two, because a sign is a
    /// separator once a number has started. `String.Split` on whitespace gets both wrong.
    [<Fact>]
    let ``numbers may run together without a separator`` () =
        let segs = ok "M.5.5L1-2"

        Assert.Equal<TexSeg>(TexSeg.MoveTo(pt 0.5 0.5), segs.[0])
        Assert.Equal<TexSeg>(TexSeg.LineTo(pt 1.0 -2.0), segs.[1])

    [<Fact>]
    let ``an exponent is part of the number and a bare e is not`` () =
        Assert.Equal<TexSeg>(TexSeg.MoveTo(pt 1500.0 0.002), (ok "M1.5e3 2e-3").[0])

    /// **An elliptical arc is refused by name.** It has no exact cubic form, `PathSeg` has no arc
    /// case, and approximating a drawing instruction silently is the failure D33's incident was
    /// about: a valid document with the wrong ink in it.
    [<Fact>]
    let ``an elliptical arc is refused rather than approximated`` () =
        match SvgPathData.parse "M0 0 A5 5 0 0 1 10 10" with
        | Error why ->
            Assert.Contains("arc", why)
            Assert.Contains("silently", why)
        | Ok _ -> failwith "an arc was accepted, so it was approximated by something"

    [<Fact>]
    let ``an unknown command is refused by name`` () =
        match SvgPathData.parse "M0 0 X5 5" with
        | Error why -> Assert.Contains("'X'", why)
        | Ok _ -> failwith "'X' was accepted"

    [<Fact>]
    let ``path data that does not start with a command is refused`` () =
        match SvgPathData.parse "5 5 L1 1" with
        | Error why -> Assert.Contains("command", why)
        | Ok _ -> failwith "coordinates without a command were accepted"

    /// `Z` takes no numbers, so a number after it is malformed. It used to be read as a repeat
    /// of `Z`, which consumed nothing and repeated forever. Reported by review on the 0.3.0 PR;
    /// run on a worker with a deadline so a regression fails this test instead of hanging the
    /// suite.
    [<Fact>]
    let ``a number after Z is refused rather than looped over`` () =
        let work = System.Threading.Tasks.Task.Run(fun () -> SvgPathData.parse "M0 0 Z 1")

        Assert.True(work.Wait 5000, "parsing 'M0 0 Z 1' did not return")

        match work.Result with
        | Error why -> Assert.Contains("'Z'", why)
        | Ok segs -> failwithf "parsed to %d segments" segs.Length

    [<Fact>]
    let ``a truncated command is refused rather than half-applied`` () =
        match SvgPathData.parse "M0 0 C1 1 2 2" with
        | Error why -> Assert.Contains("number", why)
        | Ok _ -> failwith "a cubic with four of its six arguments was accepted"

// ======================================================================================
// THE DVISVGM DOCUMENT — defs, use, rect, and the origin
// ======================================================================================

module ``the svg document`` =

    /// A document shaped exactly like dvisvgm's, small enough to check by hand.
    ///
    /// The origin is `(-72, -72)`, which is what `--bbox=papersize` produces and what the
    /// package's header records measuring. A glyph outline is a unit square at the *glyph*
    /// origin, so `<use>` moving it is visible in every coordinate.
    let private doc =
        "<svg version='1.1' xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' "
        + "width='10pt' height='20pt' viewBox='-72 -72 10 20'>"
        + "<defs><path id='g0-1' d='M0 0 L1 0 L1 -1 L0 -1 Z'/></defs>"
        + "<g id='page1'>"
        + "<use x='-70' y='-60' xlink:href='#g0-1'/>"
        + "<rect x='-71' y='-55' width='4' height='0.5'/>"
        + "</g></svg>"

    let private box: TexBox =
        { Width = 10.0<pt>
          Height = 12.0<pt>
          Depth = 8.0<pt> }

    let private read (svg: string) =
        match TexOutline.ofSvg box svg with
        | Ok o -> o
        | Error why -> failwithf "the document did not read: %s" why

    /// **The origin moves to the box's top-left, and nothing downstream ever sees the inch.**
    /// A reader that forgot the shift would place every formula 72 pt up and to the left of
    /// where it belongs — off the figure entirely, which is at least visible; a reader that
    /// shifted by a hard-coded 72 instead of the viewBox's own numbers would be right here and
    /// wrong on the first document that disagreed.
    [<Fact>]
    let ``a use is placed by its own offset, in the box's frame`` () =
        let outline = read doc

        // The glyph's first point is its origin, at `use` (-70, -60), which is (2, 12) once the
        // viewBox origin (-72, -72) is subtracted.
        match outline.Segments.[0] with
        | TexSeg.MoveTo p ->
            Assert.Equal(2.0, float p.X, 9)
            Assert.Equal(12.0, float p.Y, 9)
        | other -> failwithf "expected a moveto, got %A" other

    /// A `<rect>` is a TeX rule — a fraction bar, a `\sqrt` overbar, an `\underline` — and it
    /// has to end up in the same `Path` node as the glyphs, because a node carries one fill
    /// rule and one paint. Four lines and a close.
    [<Fact>]
    let ``a rect becomes a closed rectangle in the same outline`` () =
        let outline = read doc

        // Five segments for the glyph (M, L, L, L, Z) and five for the rule.
        Assert.Equal(10, outline.Segments.Length)

        match outline.Segments.[5], outline.Segments.[6], outline.Segments.[9] with
        | TexSeg.MoveTo a, TexSeg.LineTo b, TexSeg.ClosePath ->
            Assert.Equal(1.0, float a.X, 9)
            Assert.Equal(17.0, float a.Y, 9)
            Assert.Equal(5.0, float b.X, 9)
        | a, b, c -> failwithf "expected moveto/lineto/close, got %A %A %A" a b c

    /// The box comes from TeX, not from the SVG, and is carried through untouched. This is the
    /// assertion that says the reader did not helpfully derive it from the viewBox — which would
    /// be an ink box, and an ink box cannot say where the baseline is.
    [<Fact>]
    let ``the box is the one TeX reported, not one derived from the ink`` () =
        let outline = read doc

        Assert.Equal(10.0, float outline.Box.Width, 9)
        Assert.Equal(12.0, float outline.Box.Height, 9)
        Assert.Equal(8.0, float outline.Box.Depth, 9)

    /// **A `<use>` pointing at nothing is a missing glyph, and a missing glyph is D33's
    /// incident in miniature**: a document that is still perfectly valid, rendered without a
    /// letter in it, and nothing anywhere says so.
    [<Fact>]
    let ``a use that refers to nothing is refused rather than skipped`` () =
        let broken = doc.Replace("#g0-1'/>", "#g0-9'/>")

        match TexOutline.ofSvg box broken with
        | Error why ->
            Assert.Contains("g0-9", why)
            Assert.Contains("not in the document", why)
        | Ok _ -> failwith "a use with no target was accepted, so a glyph was dropped in silence"

    [<Fact>]
    let ``an element this reader does not implement is refused by name`` () =
        let broken = doc.Replace("</g></svg>", "<image href='x.png'/></g></svg>")

        match TexOutline.ofSvg box broken with
        | Error why ->
            Assert.Contains("<image>", why)
            Assert.Contains("without saying so", why)
        | Ok _ -> failwith "an unknown element was skipped in silence"

    /// A `<g transform>` composes onto everything under it. dvisvgm does not emit one for a DVI
    /// page, which is exactly why this is tested: the branch has no other exercise, and a
    /// transform that composed in the wrong order would be found by a user rather than here.
    [<Fact>]
    let ``a group transform composes onto its children`` () =
        let translated = doc.Replace("<g id='page1'>", "<g id='page1' transform='translate(3,5)'>")
        let outline = read translated

        match outline.Segments.[0] with
        | TexSeg.MoveTo p ->
            Assert.Equal(5.0, float p.X, 9)
            Assert.Equal(17.0, float p.Y, 9)
        | other -> failwithf "expected a moveto, got %A" other

    [<Fact>]
    let ``a transform this reader does not implement is refused rather than ignored`` () =
        let rotated = doc.Replace("<g id='page1'>", "<g id='page1' transform='rotate(30)'>")

        match TexOutline.ofSvg box rotated with
        | Error why -> Assert.Contains("rotate", why)
        | Ok _ -> failwith "a rotate was ignored, so the formula would be drawn unrotated"

    /// A colour of its own is **counted, not honoured and not refused**: the shape is right and
    /// only the paint is the run's, which is a smaller thing to be wrong about than a missing
    /// glyph. `fill='none'` is not a colour and must not be counted, or every stroked TikZ
    /// picture would report a colour override.
    [<Fact>]
    let ``an element's own colour is counted, and fill none is not a colour`` () =
        let coloured = doc.Replace("<rect x=", "<rect fill='#ff0000' x=")
        Assert.Equal(1, (read coloured).Recoloured)

        let none = doc.Replace("<rect x=", "<rect fill='none' x=")
        Assert.Equal(0, (read none).Recoloured)
        Assert.Equal(0, (read doc).Recoloured)

    [<Fact>]
    let ``a document with no viewBox is refused, because there is nothing to place against`` () =
        let broken = doc.Replace("viewBox='-72 -72 10 20'", "")

        match TexOutline.ofSvg box broken with
        | Error why -> Assert.Contains("viewBox", why)
        | Ok _ -> failwith "a document with no frame was accepted"

// ======================================================================================
// THE DOCUMENT AND THE LOG — still no engine required
// ======================================================================================

module ``the document and the box`` =

    /// The `\setbox`/`\typeout`/`\box` sandwich is the only source of the baseline, so its shape
    /// is worth pinning: TeX has to be *asked* between making the box and shipping it.
    [<Fact>]
    let ``the document carries the source, the preamble and the question`` () =
        let doc = Typeset.document TexOptions.Default "$x^2$"

        Assert.Contains("\\documentclass[border=0pt]{standalone}", doc)
        Assert.Contains("\\usepackage{siunitx}", doc)
        Assert.Contains("\\setbox0=\\hbox{$x^2$}", doc)
        Assert.Contains("\\typeout{MAREYBOX wd=\\the\\wd0 ht=\\the\\ht0 dp=\\the\\dp0}", doc)

        // Asked *after* the box is made and *before* it is shipped. Either way round and the
        // dimensions are of the wrong thing, or of nothing.
        Assert.True(doc.IndexOf "\\setbox0" < doc.IndexOf "MAREYBOX")
        Assert.True(doc.IndexOf "MAREYBOX" < doc.IndexOf "\\box0\n")

    [<Fact>]
    let ``the preamble is the caller's, replaced rather than appended to`` () =
        let doc =
            Typeset.document { TexOptions.Default with Preamble = "\\usepackage{physics}\n" } "$x$"

        Assert.Contains("\\usepackage{physics}", doc)
        Assert.DoesNotContain("siunitx", doc)

    /// **The factor of 1.00375, which is the whole reason this is a function and not a
    /// `Double.Parse`.** TeX's point is 1/72.27 inch; the `pt` in this library's unit system, in
    /// SVG, and in PostScript is 1/72. Reading `\the\wd0` as if it were the latter makes every
    /// formula a third of a percent too big — a quarter of a pixel on a 60 pt formula, and
    /// invisible until somebody measures a figure against a ruler.
    [<Fact>]
    let ``the box is converted from TeX points to the points everyone else means`` () =
        match Typeset.parseBox "MAREYBOX wd=72.27pt ht=36.135pt dp=0.0pt\n" with
        | Ok box ->
            Assert.Equal(72.0, float box.Width, 6)
            Assert.Equal(36.0, float box.Height, 6)
            Assert.Equal(0.0, float box.Depth, 9)
        | Error why -> failwith why

    [<Fact>]
    let ``a log with no box line is an error rather than a zero box`` () =
        match Typeset.parseBox "This is pdfTeX, Version 3.141592653\nOutput written on x.dvi\n" with
        | Error why -> Assert.Contains("MAREYBOX", why)
        | Ok box -> failwithf "a box was invented from a log that has none: %A" box

// ======================================================================================
// GATE 13 — MISSING TEX DEGRADES, IN THE SHAPE D75 SET
// ======================================================================================

module ``the degrade`` =

    /// **Gate 13, and the reason `sceneWith` takes its tools.** This machine has TeX, so the
    /// branch a machine without it takes is unreachable through `Typeset.scene` — and an
    /// unreachable branch is one nobody has ever run. `Video.muxWith` set the precedent for
    /// exactly this and phase 6 named it.
    ///
    /// The requirement is D62's shape a fourth time: the figure still renders, the scene is
    /// **the same object** that went in, and the message says what to install and that nothing
    /// was lost.
    [<Fact>]
    let ``a missing engine leaves the scene alone and says so`` () =
        let scene = Group [| Text(run (Some "$x$") "x") |]

        match Typeset.sceneWith (absent "latex") (Tools.dvisvgm.Force()) TexOptions.Default extent scene with
        | TypesetUnavailable why ->
            Assert.Contains("latex not found on PATH", why)
            Assert.Contains("still renders", why)
        | other -> failwithf "expected TypesetUnavailable, got %A" other

    /// Two tools, resolved separately, so "which one is missing" is the useful half of the
    /// message. A package that assumed dvisvgm alongside the engine would report the engine.
    [<Fact>]
    let ``a missing dvisvgm is named, not blamed on the engine`` () =
        match Typeset.sceneWith (Tools.latex.Force()) (absent "dvisvgm") TexOptions.Default extent Scene.empty with
        | TypesetUnavailable why ->
            Assert.Contains("dvisvgm not found on PATH", why)
            Assert.DoesNotContain("latex not found", why)
        | other -> failwithf "expected TypesetUnavailable, got %A" other

    /// `sceneOrPlain` is the degrade as a function rather than as a pattern every caller writes,
    /// so the scene it hands back on a machine with no TeX has to be the one that went in —
    /// **by reference**, because a caller comparing them is asking whether anything happened.
    [<Fact>]
    let ``sceneOrPlain hands back the original scene when there is no toolchain`` () =
        let scene = Group [| Text(run (Some "$x$") "x") |]
        let engine = absent "latex"

        match Typeset.sceneWith engine (absent "dvisvgm") TexOptions.Default extent scene with
        | TypesetUnavailable _ -> ()
        | other -> failwithf "expected TypesetUnavailable, got %A" other

        let drawn, note = Typeset.sceneOrPlain TexOptions.Default extent scene

        // On a machine *with* TeX this typesets; on one without it degrades. Either way the
        // note says which, and either way there is a scene to draw.
        Assert.False(String.IsNullOrWhiteSpace note)
        Assert.NotNull(box drawn)

    /// **A formula cannot run a shell command**, even on a machine whose TeX enables shell
    /// escape. A formula reaches the engine through the public `TextRun.Tex` field, and a run
    /// set from data — an axis title read from a file — is a string the library did not write.
    /// TeX Live's default (restricted) already refuses `touch`; a distribution configured with
    /// `shell_escape = t` did not, until the engine was started with `--disable-write18`.
    /// Reported by review on the 0.3.0 PR.
    ///
    /// The engine handed to `sceneWith` is a wrapper that runs the real one with shell escape
    /// switched on, and the control run shows the wrapper does exactly that — so the test cannot
    /// pass because escape happened to be off.
    [<Fact>]
    let ``a formula cannot run a shell command, even where shell escape is enabled`` () =
        withTex (fun () ->
            if OperatingSystem.IsWindows() then
                Console.WriteLine("SKIPPED: the escape gate's wrapper engine is a POSIX shell script")
            else
                let dir = IO.Directory.CreateTempSubdirectory("marey-escape").FullName

                try
                    let real = (Tools.latex.Force()).Path.Value
                    let wrapper = IO.Path.Combine(dir, "latex-with-escape")
                    IO.File.WriteAllText(wrapper, sprintf "#!/bin/sh\nshell_escape=t exec '%s' \"$@\"\n" real)
                    IO.File.SetUnixFileMode(wrapper, IO.UnixFileMode.UserRead ||| IO.UnixFileMode.UserWrite ||| IO.UnixFileMode.UserExecute)

                    let engine: ResolvedTool =
                        { Name = "latex"
                          Path = Some wrapper
                          Install = "" }

                    let escaped name =
                        sprintf "\\immediate\\write18{touch %s}" (IO.Path.Combine(dir, name))

                    // The control: the wrapper, run as a plain engine on a plain document,
                    // does execute the command.
                    IO.File.WriteAllText(
                        IO.Path.Combine(dir, "control.tex"),
                        "\\documentclass{article}\\begin{document}" + escaped "control" + "x\\end{document}"
                    )

                    match Toolchain.runArgs engine [ "-interaction=nonstopmode"; "control.tex" ] dir 60_000 with
                    | ToolRan _ -> ()
                    | other -> failwithf "the control run did not run: %A" other

                    Assert.True(IO.File.Exists(IO.Path.Combine(dir, "control")), "the wrapper did not enable shell escape, so this gate proves nothing")

                    // The gate: the same command inside a formula, through the library.
                    let scene = Group [| Text(run (Some(escaped "formula" + "$x$")) "x") |]
                    Typeset.sceneWith engine (Tools.dvisvgm.Force()) TexOptions.Default extent scene |> ignore

                    Assert.False(IO.File.Exists(IO.Path.Combine(dir, "formula")), "a formula ran a shell command")
                finally
                    IO.Directory.Delete(dir, true))

    /// Present or absent, the probe says which and names both tools.
    [<Fact>]
    let ``the probe reports either way, and once`` () =
        let line = Typeset.report ()
        Assert.Contains("latex", line)
        Assert.Contains("dvisvgm", line)

        if (Typeset.toolchain ()).Engine.IsAvailable then
            Assert.Contains("found at", line)
        else
            Assert.Contains("To enable it", line)

        // Resolved once per process, so the second answer is the first one.
        Assert.Equal(line, Typeset.report ())

    /// **The cache collapses repetition inside a call and does not span calls.**
    ///
    /// Both halves, because the header claimed the second one and it was not true: an earlier
    /// draft said "an animation costs it once, because the cache is keyed on the source rather
    /// than on the frame", and the guide repeated it. The cache is a local in `sceneWith`, so a
    /// caller typesetting an animation frame by frame pays a figure's worth per frame. Found at
    /// M3 phase 9 by writing the demo reel around the claim and timing it.
    ///
    /// Stated in engine runs rather than in milliseconds: the number of times `latex` was
    /// started is the fact, and a wall clock on a loaded machine is not.
    [<Fact>]
    let ``the cache is per call, so repetition is free and a second call is not`` () =
        withTex (fun () ->
            // Four runs, one distinct source. Whatever the pass costs, it is one engine run:
            // this is the facets case, and it is the half that was always true.
            let one = run (Some "$x^2 + y^2$") "x2 + y2"

            let repeated =
                Group [| for i in 0..3 -> Text { one with At = { X = 0.2<nd>; Y = float i * 0.2<nd> } } |]

            match Typeset.scene TexOptions.Default extent repeated with
            | Typeset r ->
                Assert.Equal(4, r.Typeset)
                Assert.Equal(1, r.EngineRuns)
            | outcome -> failwithf "the repeated scene did not typeset: %A" outcome

            // The same source again, in a second call. A cache that spanned calls would report
            // zero engine runs here; it reports one, because the second call has its own.
            match Typeset.scene TexOptions.Default extent (Text one) with
            | Typeset r ->
                Assert.Equal(1, r.Typeset)

                Assert.Equal(
                    1,
                    r.EngineRuns
                )
            | outcome -> failwithf "the second call did not typeset: %A" outcome)

// ======================================================================================
// THE PASS — what happens to a scene, with and without an engine behind it
// ======================================================================================

module ``the scene pass`` =

    /// A run with no `Tex` form is every tick label in an ordinary figure. It must come back
    /// **by reference**: a pass that rebuilt the tree to change two axis titles would cost the
    /// figure rather than the titles, which is `Scene.mapKey`'s rule from phase 0.
    [<Fact>]
    let ``runs without a tex form are counted and returned by reference`` () =
        withTex (fun () ->
            let plain = Text(run None "9.81")
            let scene = Group [| plain; plain |]

            match Typeset.scene TexOptions.Default extent scene with
            | Typeset result ->
                Assert.Equal(2, result.Plain)
                Assert.Equal(0, result.Typeset)
                Assert.Equal(0, result.EngineRuns)
                Assert.True(obj.ReferenceEquals(scene, result.Scene), "the scene was rebuilt for nothing")
            | other -> failwithf "expected Typeset, got %A" other)

    /// The headline: a run with a `Tex` form becomes filled outlines, and the outlines have ink
    /// in them. **Non-zero winding**, because a glyph's counters are subpaths wound the other
    /// way and even-odd would disagree wherever a glyph self-overlaps.
    [<Fact>]
    let ``a tex run becomes a filled path with the run's own paint`` () =
        withTex (fun () ->
            match Typeset.scene TexOptions.Default extent (Text(run (Some "$x^2$") "x^2")) with
            | Typeset result ->
                Assert.Equal(1, result.Typeset)
                Assert.Equal(1, result.EngineRuns)
                Assert.Empty result.Failed

                match result.Scene with
                | Path(segs, stroke, fill, rule) ->
                    Assert.True(segs.Length > 8, sprintf "a superscripted x came back as %d segments" segs.Length)
                    Assert.Equal<Stroke option>(None, stroke)
                    Assert.Equal<Paint option>(Some(Solid ink), fill)
                    Assert.Equal(NonZero, rule)
                | other -> failwithf "expected a Path, got %A" other
            | other -> failwithf "expected Typeset, got %A" other)

    /// **The placement, checked against the numbers TeX itself reports** — and against the one
    /// distinction that is easy to get wrong in the assertion rather than in the code.
    ///
    /// `$x$` has `\dp = 0`: TeX's box has no depth. That does **not** mean no ink below the
    /// baseline, because `\dp` is a *metric* and a glyph's outline is free to exceed it — an
    /// italic `x` in Computer Modern dips 0.15 px at 10 pt for the tail of its right stroke.
    /// The first spelling of this gate asserted no ink at all below the line and failed on that
    /// serif, which is the assertion being wrong rather than the placement.
    ///
    /// So the claim is the one that is actually true and still falsifiable: a formula with no
    /// depth sits *on* the line, within a pixel of it, while ~29 px of it stands above.
    [<Fact>]
    let ``an outline sits on the baseline the box declares`` () =
        withTex (fun () ->
            match Typeset.scene TexOptions.Default extent (Text(run (Some "$x$") "x")) with
            | Typeset result ->
                match result.Scene with
                | Path(segs, _, _, _) ->
                    let ys = endPoints segs |> Array.map (fun p -> float p.Y)
                    let xs = endPoints segs |> Array.map (fun p -> float p.X)

                    // `nd` is y-up, the baseline is at `At.Y` = 0.5, and the surface is 250 px
                    // tall — so a pixel is 0.004 in these numbers.
                    let below = (0.5 - Array.min ys) * float extent.Height
                    let above = (Array.max ys - 0.5) * float extent.Height

                    Assert.True(
                        below < 1.0,
                        sprintf "'$x$' has %.3f px of ink below its baseline, which is more than a serif" below
                    )

                    Assert.True(above > 2.0, sprintf "'$x$' left %.3f px of ink above its baseline" above)

                    // AnchorStart: the box starts at `At.X` and ink cannot precede it.
                    Assert.True(
                        Array.min xs >= 0.5 - 1e-6,
                        sprintf "ink starts %.6f left of the anchor" (0.5 - Array.min xs)
                    )
                | other -> failwithf "expected a Path, got %A" other
            | other -> failwithf "expected Typeset, got %A" other)

    /// **`$\sum_i$` is the counter-example that makes the test above mean something.** It has a
    /// deep descender, so ink below the baseline is *required* — and a pass that clamped
    /// everything above the line, or that placed by an ink box, would pass the previous gate and
    /// fail this one.
    [<Fact>]
    let ``a run with a descender puts ink below the baseline`` () =
        withTex (fun () ->
            match Typeset.scene TexOptions.Default extent (Text(run (Some "$\\sum_{i=0}^{n}$") "sum")) with
            | Typeset result ->
                match result.Scene with
                | Path(segs, _, _, _) ->
                    let ys = endPoints segs |> Array.map (fun p -> float p.Y)
                    Assert.True(Array.min ys < 0.5, "a summation with a subscript left no ink below the baseline")
                    Assert.True(Array.max ys > 0.5, "it left no ink above the baseline either")
                | other -> failwithf "expected a Path, got %A" other
            | other -> failwithf "expected Typeset, got %A" other)

    /// **A phantom changes the box and must not move the ink — which is what `--bbox=papersize`
    /// is for.**
    ///
    /// `\vphantom{\int_0^\infty}` reserves the integral's height and depth and draws nothing, so
    /// `$\vphantom{\int_0^\infty}x$` is an `x` in a box three times too tall. Where the `x`
    /// lands is unchanged, because it still sits on the baseline — and the baseline is `ht`
    /// below the top of the *box*, which only TeX knows.
    ///
    /// Take `--bbox=papersize` away and dvisvgm reports the **ink** box instead. Every formula
    /// in this suite until this one has ink that nearly fills its box, so the two agree to
    /// within a pixel and the flag looks decorative: the mutation that removed it passed every
    /// other gate here. This is the snippet where they cannot agree, and it is the reason the
    /// package's header says the flag is load-bearing rather than merely present.
    [<Fact>]
    let ``a phantom changes the box and leaves the ink where it was`` () =
        withTex (fun () ->
            let inkOf (tex: string) =
                match Typeset.scene TexOptions.Default extent (Text(run (Some tex) "x")) with
                | Typeset { Scene = Path(segs, _, _, _) } ->
                    let ps = endPoints segs
                    let ys = ps |> Array.map (fun p -> float p.Y)
                    Array.min ys, Array.max ys
                | other -> failwithf "expected a Path, got %A" other

            let plainLow, plainHigh = inkOf "$x$"
            // A **verbatim** string. `"\v"` in an ordinary F# literal is a vertical tab, so the
            // first spelling of this line sent TeX `$phantom{...}` and the gate failed on a
            // snippet that had never existed. Every TeX literal in this file is either verbatim
            // or doubly escaped for that reason.
            let phantomLow, phantomHigh = inkOf @"$\vphantom{\int_0^\infty}x$"

            // A pixel is 0.004 in these numbers on a 250 px surface.
            let px (v: float) = v * float extent.Height

            Assert.True(
                abs (px (phantomLow - plainLow)) < 1.0,
                sprintf "the phantom moved the bottom of the ink by %.2f px" (px (phantomLow - plainLow))
            )

            Assert.True(
                abs (px (phantomHigh - plainHigh)) < 1.0,
                sprintf "the phantom moved the top of the ink by %.2f px" (px (phantomHigh - plainHigh))
            ))

    /// The anchor is resolved against the box, so `AnchorEnd` puts the same formula entirely to
    /// the left of the point `AnchorStart` puts it to the right of. Stated as a relation between
    /// two renders rather than as a number, because the number is the face's.
    [<Fact>]
    let ``the anchor moves the outline by the box's own width`` () =
        withTex (fun () ->
            let at (anchor: TextAnchor) =
                let r = { run (Some "$x$") "x" with Anchor = anchor }

                match Typeset.scene TexOptions.Default extent (Text r) with
                | Typeset { Scene = Path(segs, _, _, _) } -> endPoints segs |> Array.map (fun p -> float p.X)
                | other -> failwithf "expected a Path, got %A" other

            let start = at AnchorStart
            let finish = at AnchorEnd
            let middle = at AnchorMiddle

            Assert.True(Array.max finish <= 0.5 + 1e-6, "AnchorEnd left ink to the right of the anchor")
            Assert.True(Array.min start >= 0.5 - 1e-6, "AnchorStart left ink to the left of the anchor")

            // Middle is halfway between the two, to the precision the box width allows.
            let shift = Array.min start - Array.min middle
            let half = (Array.min start - Array.min finish) / 2.0
            Assert.Equal(half, shift, 9))

    /// The size is a **scale**, not a second engine run — which is what lets the cache be keyed
    /// on the source alone. Doubling the font size doubles the outline and costs nothing.
    [<Fact>]
    let ``the same formula at two sizes costs one engine run and scales exactly`` () =
        withTex (fun () ->
            let small = Text(run (Some "$x$") "x")

            let large =
                Text
                    { run (Some "$x$") "x" with
                        Font =
                            { Family = "serif"
                              Size = 20.0<pt>
                              Weight = 400
                              Italic = false } }

            match Typeset.scene TexOptions.Default extent (Group [| small; large |]) with
            | Typeset result ->
                Assert.Equal(2, result.Typeset)
                Assert.Equal(1, result.EngineRuns)

                match result.Scene with
                | Group [| Path(a, _, _, _); Path(b, _, _, _) |] ->
                    let height (segs: PathSeg[]) =
                        let ys = endPoints segs |> Array.map (fun p -> float p.Y)
                        Array.max ys - Array.min ys

                    // 10 pt to 20 pt. Exactly two, because it is one multiplication.
                    Assert.Equal(2.0, height b / height a, 9)
                | other -> failwithf "expected two paths, got %A" other
            | other -> failwithf "expected Typeset, got %A" other)

    /// **A snippet TeX rejects fails alone.** This is the argument against batching every
    /// snippet into one document, stated as a test: the bad run keeps its text, is reported by
    /// name with TeX's own complaint, and the good one beside it still typesets.
    [<Fact>]
    let ``a rejected snippet stays as text, is named, and does not take the figure with it`` () =
        withTex (fun () ->
            let bad = Text(run (Some "$\\notacommand{x}$") "broken")
            let good = Text(run (Some "$x$") "x")

            match Typeset.scene TexOptions.Default extent (Group [| bad; good |]) with
            | Typeset result ->
                Assert.Equal(1, result.Typeset)
                let failure = Assert.Single result.Failed
                Assert.Contains("notacommand", failure.Source)
                Assert.Contains("!", failure.Reason)

                match result.Scene with
                | Group [| Text t; Path _ |] ->
                    // The bad run is still there, still drawable, still carrying its plain form.
                    Assert.Equal("broken", t.Text)
                | other -> failwithf "expected a text and a path, got %A" other
            | other -> failwithf "expected Typeset, got %A" other)

    /// One engine run per *distinct* source. The same axis title on twelve facets is one run,
    /// which is the difference between a faceted figure taking a fifth of a second and taking
    /// two and a half.
    [<Fact>]
    let ``the same source twice is one engine run`` () =
        withTex (fun () ->
            let r = Text(run (Some "$\\alpha$") "alpha")

            match Typeset.scene TexOptions.Default extent (Group(Array.replicate 6 r)) with
            | Typeset result ->
                Assert.Equal(6, result.Typeset)
                Assert.Equal(1, result.EngineRuns)
            | other -> failwithf "expected Typeset, got %A" other)

    /// A subtree with nothing to typeset in it comes back by reference even when a sibling
    /// changed — the `mapKey` rule, one level down, and the thing that makes this pass cheap on
    /// a real figure where two runs out of two hundred nodes carry TeX.
    [<Fact>]
    let ``an untouched subtree survives by reference beside one that changed`` () =
        withTex (fun () ->
            let untouched = Group [| Text(run None "tick"); Text(run None "tick") |]
            let scene = Group [| untouched; Text(run (Some "$x$") "x") |]

            match Typeset.scene TexOptions.Default extent scene with
            | Typeset { Scene = Group [| survived; _ |] } ->
                Assert.True(obj.ReferenceEquals(untouched, survived), "an unchanged subtree was rebuilt")
            | other -> failwithf "expected a group of two, got %A" other)

// ======================================================================================
// END TO END — the picture, because a valid document with no ink is this project's ghost
// ======================================================================================

module ``the picture`` =

    /// **D33's incident is the reason this gate exists.** Every assertion above is about
    /// numbers, and numbers were all that was checked the day a perfectly valid TikZ document
    /// came out containing no visible markers. So this one rasterises: the formula reaches a
    /// PNG, through a backend that has never heard of TeX, and there is ink where the formula
    /// was put and none where it was not.
    [<Fact>]
    let ``a typeset formula reaches a raster, through a backend that knows nothing about tex`` () =
        withTex (fun () ->
            SkiaTests.withSkia (fun () ->
                let r =
                    { run (Some "$\\frac{\\partial \\vec{E}}{\\partial t}$") "dE/dt" with
                        At = { X = 0.5<nd>; Y = 0.5<nd> }
                        Anchor = AnchorMiddle
                        Font =
                            { Family = "serif"
                              Size = 24.0<pt>
                              Weight = 400
                              Italic = false } }

                let node, note = Typeset.sceneOrPlain TexOptions.Default extent (Text r)
                Assert.Contains("typeset 1 run", note)

                let png = SkiaBackend.renderPng extent node
                let gray = SkiaTests.SkiaPixels.decode png

                let inked =
                    seq {
                        for y in 0 .. gray.Height - 1 do
                            for x in 0 .. gray.Width - 1 do
                                if int gray.Pixels.[y * gray.Width + x] < Raster.InkThreshold then
                                    yield x, y
                    }
                    |> Seq.toArray

                Assert.True(inked.Length > 50, sprintf "the formula rasterised to %d inked pixels" inked.Length)

                // **Where the ink is, checked against the geometry that produced it rather than
                // against numbers written down by hand.** The pass emitted a path in `nd`; the
                // backend mapped it to pixels. If those two agree, the placement arithmetic and
                // the raster agree, and a magic constant in this test cannot be quietly keeping
                // a wrong answer green.
                let pathBounds =
                    match node with
                    | Path(segs, _, _, _) ->
                        let ps = endPoints segs
                        let xs = ps |> Array.map (fun p -> float p.X * float extent.Width)
                        // `nd` is y-up and a raster's rows run down.
                        let ys = ps |> Array.map (fun p -> (1.0 - float p.Y) * float extent.Height)
                        Array.min xs, Array.max xs, Array.min ys, Array.max ys
                    | other -> failwithf "expected a Path, got %A" other

                let px0, px1, py0, py1 = pathBounds
                let ix0 = float (inked |> Array.map fst |> Array.min)
                let ix1 = float (inked |> Array.map fst |> Array.max)
                let iy0 = float (inked |> Array.map snd |> Array.min)
                let iy1 = float (inked |> Array.map snd |> Array.max)

                // The path's bounds are taken from **endpoints only**, and a cubic bulges past
                // its endpoints, so the ink may reach a little further on every side and may not
                // fall short on any. Three pixels of slack for the bulge and the antialiased
                // edge; a formula placed at the wrong anchor or the wrong scale misses by tens.
                let agrees (name: string) (fromPath: float) (fromInk: float) (slack: float) =
                    Assert.True(
                        abs (fromPath - fromInk) <= slack,
                        sprintf "%s: the path says %.1f px and the ink is at %.1f px" name fromPath fromInk
                    )

                agrees "left" px0 ix0 3.0
                agrees "right" px1 ix1 3.0
                agrees "top" py0 iy0 3.0
                agrees "bottom" py1 iy1 3.0

                // And it really is where it was asked to go: centred on nd (0.5, 0.5).
                Assert.True(ix0 < 200.0 && ix1 > 200.0, "the centred formula does not straddle the middle")))
