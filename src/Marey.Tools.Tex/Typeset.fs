namespace Marey

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text
open System.Text.RegularExpressions

// ======================================================================================
// D79 — MATHEMATICAL TYPESETTING, BY THE DOOR §10 LEFT OPEN.
//
// §10: *"A maths visualization library that cannot set `\frac{\partial \vec{E}}{\partial t}` is
// not a maths visualization library."* It gives three options and calls the third the right
// answer and a multi-month subproject; M6 is where that lands. The recommendation for now is
// "ship (1) as an opt-in package and (2) automatically on the web backend at M3", and D79 found
// the contradiction in that: **M3 has no web backend.** §14 puts the Fable backend at M4 and
// both previous plans restate it, so §14's M3 line promised MathJax on a target that does not
// exist for another milestone.
//
// D79 took door 3: ship option 1 now. It needs no web backend, it reads the `TextRun.Tex` field
// D32 added at M1 for exactly this, and the machinery was already in this repository's test
// suite — M1's gate has compiled every golden `.tex` through `pdflatex` since the TikZ backend
// shipped. §14's M3 line is corrected in this phase rather than in the documents sweep, because
// a roadmap line that outlives the decision that changed it is how the next reader gets it
// wrong.
//
// ---------------------------------------------------------------------------------------
// WHAT IT PRODUCES, AND WHY THAT IS THE WHOLE POINT
// ---------------------------------------------------------------------------------------
//
// **Outlines, not an image and not a backend feature.** `typeset` is a scene-to-scene pass, the
// same shape as `Degrade.forCaps`: it walks a `Node`, and every `Text` run carrying a `Tex` form
// becomes a `Path` of filled outlines. After it, the formula is ordinary geometry — so the SVG
// backend, the TikZ backend and the Skia backend all draw it, unchanged, with no capability flag
// between them and no second implementation. That is what option 1 buys over option 2, and it is
// why the pass lives here rather than inside a backend.
//
// It also means a figure is never *worse* for the package being absent. A run keeps its plain
// `Text` — `TextRun.Text` is always set, which is D32's rule and the reason this is possible —
// so a missing TeX installation costs the typesetting and nothing else.
//
// ---------------------------------------------------------------------------------------
// TWO TOOLS, RESOLVED SEPARATELY (D75'S SHAPE, THIRD TIME)
// ---------------------------------------------------------------------------------------
//
// `latex` writes DVI; `dvisvgm` turns DVI into paths. They are separate binaries from separate
// packages and a container image can carry one without the other, so each is resolved on its own
// and `report` says which is missing — the same argument `Video` makes for `ffmpeg` and
// `ffprobe`, and the same one D62 makes for a font.
//
// **`latex`, not `pdflatex`.** DVI is what dvisvgm reads, and §10 says so: "Renders DVI → SVG
// paths". Going through PDF would mean a second converter with its own opinions about fonts.
// The test suite's own gate uses `pdflatex` and stays as it is: it is asking a different
// question — does this document compile — and the answer must not depend on this package.
//
// ---------------------------------------------------------------------------------------
// ONE ENGINE RUN PER SNIPPET, MEMOISED — AND THE ALTERNATIVE, WHICH WAS MEASURED
// ---------------------------------------------------------------------------------------
//
// Measured on this machine: 80 ms for `latex` plus 26 ms for `dvisvgm`, near enough constant
// across a fraction, a siunitx quantity, a Greek letter and a word — 106 ms a snippet. A figure
// carries two of them, because the axis compiler fills `TitleTex` for the axis titles and
// leaves tick labels alone (`Axes.fs`), so an ordinary figure costs about a fifth of a second
// and an animation costs it once, because the cache is keyed on the source rather than on the
// frame.
//
// **Batching every snippet into one multi-page document was tried and rejected.** It is roughly
// four times faster on a figure with ten distinct labels, and it fails badly: one snippet TeX
// dislikes takes down the whole document, so a single mistyped label costs *every* label on the
// figure. Per-snippet runs isolate that — the bad one is reported by name and the rest typeset —
// which is worth 300 ms on an export path a caller opts into. (`standalone` also declined to
// paginate the way the batch needed, which is how the measurement got made at all.)
//
// **The size is a scale, not a second run.** TeX sets at the document's base size and the
// outline is scaled by `Font.Size / BaseSize` at placement. So the cache is keyed on the source
// alone and an axis title at 9 pt and the same title at 18 pt cost one engine run between them.
// The imprecision that buys is stated in `TexOptions.BaseSize`.
// ======================================================================================

/// The TeX toolchain, resolved and *reported* — D62's rule in its fourth currency, after a font,
/// a native library and an encoder.
[<NoComparison>]
type TexReport =
    { /// The engine that writes DVI.
      Engine: ResolvedTool
      /// The converter that turns DVI into paths.
      Dvisvgm: ResolvedTool
      /// The first line of `dvisvgm --version`. `None` when it is absent, or present and unable
      /// to say what it is — which is a broken installation and reads differently from a missing
      /// one.
      DvisvgmVersion: string option }

    /// Whether a snippet would be typeset at all. **Both** tools, because either one missing
    /// stops the pipeline at a different point and the answer to "will this work" is one bool.
    member r.IsAvailable = r.Engine.IsAvailable && r.Dvisvgm.IsAvailable

/// How a snippet is typeset.
[<NoComparison>]
type TexOptions =
    { /// The preamble, between `\documentclass` and `\begin{document}`.
      ///
      /// The default carries `amsmath` for the mathematics, `siunitx` because that is what
      /// `UnitStyle.Siunitx` emits and it is the reason this package has anything to typeset,
      /// and `textcomp` for the same insurance the TikZ backend takes. A caller who needs
      /// `physics` or a custom `\newcommand` replaces the whole string rather than appending,
      /// because a preamble is a document's own business.
      Preamble: string
      /// The document class options. `border=0pt` is what makes the page box the TeX box, which
      /// is what makes the baseline exactly computable — see `TexOutline`'s header before
      /// changing it.
      ClassOptions: string
      /// The size the document sets at, and therefore the size the outline is scaled *from*.
      ///
      /// **Known imprecision, accepted deliberately.** Scaling 10 pt outlines to 18 pt is not
      /// what a TeX engine would do at 18 pt: real optical sizing picks a different design, with
      /// different stroke weights and different spacing. The alternative is an engine run per
      /// distinct size, which multiplies the cost by the number of font sizes in a figure and
      /// buys a difference nobody reading a plot will see. matplotlib's `usetex` makes the same
      /// trade.
      BaseSize: float<pt>
      /// Points to pixels needs a resolution, and it must be the one the backend renders at, or
      /// a formula is the wrong size against the text around it. 96 px/inch, matching
      /// `SkiaOptions.Dpi` and `ApproxTextMeasurer`.
      Dpi: float<px / mm>
      /// How long either tool may take before it is killed. Ten seconds: a snippet takes a tenth
      /// of that, and a `latex` sitting at an interactive prompt never returns at all.
      TimeoutMs: int }

[<RequireQualifiedAccess>]
module TexOptions =

    let Default =
        { Preamble = "\\usepackage{amsmath}\n\\usepackage{textcomp}\n\\usepackage{siunitx}\n"
          ClassOptions = "border=0pt"
          BaseSize = 10.0<pt>
          Dpi = Space.screenDpi
          TimeoutMs = 10_000 }

/// A run that reached the engine and did not come back with an outline.
[<NoComparison>]
type TexFailure =
    { /// The TeX source, verbatim, so the reader can paste it into a document of their own.
      Source: string
      /// What went wrong: TeX's own `!` lines, dvisvgm's complaint, or this reader's refusal.
      Reason: string }

/// A scene in which every run that could be typeset was.
[<NoComparison>]
type TypesetResult =
    { /// The scene to draw. Runs that typeset are outlines; every other node is **by reference**
      /// the node that came in, for `Scene.mapKey`'s reason — a pass that rebuilt the whole tree
      /// to change two axis titles would cost the figure rather than the titles.
      Scene: Node
      /// Runs replaced by outlines.
      Typeset: int
      /// Runs left as text because they carry no `Tex` form. Every tick label in an ordinary
      /// figure, which is why this is a count and not a warning.
      Plain: int
      /// Runs that carry a `Tex` form and did not typeset. **The scene still contains them, as
      /// text**, so the figure is complete and this is the list of what is less good than it
      /// could be.
      Failed: TexFailure list
      /// Engine invocations. Distinct sources, not runs: the same axis title on twelve facets
      /// costs one.
      EngineRuns: int
      /// Elements whose own colour was not honoured, summed over the runs. See
      /// `TexOutline.Recoloured`.
      Recoloured: int }

/// What happened when a scene was asked to typeset.
///
/// Three cases rather than a `Result`, for `ToolOutcome`'s reason: each calls for different
/// behaviour at the call site. **Unavailable is not a failure** — the scene renders, in text,
/// exactly as it would have without this package — and collapsing it into the error case is how
/// a caller ends up treating a missing optional dependency as a broken figure.
[<NoComparison>]
type TypesetOutcome =
    /// One or both tools are not installed. Carries what to install, and says the scene is
    /// unchanged and still renders.
    | TypesetUnavailable of string
    /// The tools ran. Read `Failed` — it may be empty, and usually is.
    | Typeset of TypesetResult

/// **Mathematical typesetting, by shelling out.** §10's option 1, D79's door 3.
///
/// The module is `Typeset` rather than `Tex` because `Marey.Core` already owns `Tex` — the
/// escaping table that `Marey.Units` and the TikZ backend share. Two modules of one name in one
/// namespace is not a thing F# will do, and the shorter name belongs to the one Core needs.
[<RequireQualifiedAccess>]
module Typeset =

    [<Literal>]
    let private engineInstall =
        "a TeX distribution — `apt install texlive-latex-base texlive-latex-recommended texlive-science`, `brew install --cask mactex`, or MiKTeX from https://miktex.org"

    [<Literal>]
    let private dvisvgmInstall =
        "dvisvgm — `apt install dvisvgm`, `brew install dvisvgm`, or it ships inside MiKTeX and MacTeX"

    /// The engine. `latex` rather than `pdflatex`: dvisvgm reads DVI, and §10 says "DVI → SVG
    /// paths".
    [<Literal>]
    let Engine = "latex"

    let private engineTool = lazy (Toolchain.resolve engineInstall Engine)
    let private dvisvgmTool = lazy (Toolchain.resolve dvisvgmInstall "dvisvgm")

    let private firstLine (text: string) : string option =
        text.Split('\n')
        |> Array.tryHead
        |> Option.map (fun l -> l.Trim())
        |> Option.filter (fun l -> l.Length > 0)

    let private probed =
        lazy
            (let engine = engineTool.Force()
             let dvisvgm = dvisvgmTool.Force()

             let version =
                 // The temp directory rather than the current one: a probe should not depend on
                 // where the process happens to be standing, and on a working directory that has
                 // since been deleted `Process.Start` throws rather than reporting.
                 match Toolchain.runArgs dvisvgm [ "--version" ] (Path.GetTempPath()) 20_000 with
                 | ToolRan r when r.ExitCode = 0 -> firstLine r.Out
                 | _ -> None

             { Engine = engine
               Dvisvgm = dvisvgm
               DvisvgmVersion = version })

    /// The toolchain, resolved once per process and asked what it is.
    ///
    /// **Probed at first use, not at every call.** Two `PATH` walks and a version process happen
    /// once; a suite of gates would otherwise do them per test.
    let toolchain () : TexReport = probed.Force()

    /// One line per tool saying what was found, whether or not anything was.
    ///
    /// `Toolchain.report`'s reason, stated once more because this is the package where it bites
    /// hardest: a figure typeset and a figure not typeset both render, both look fine at a
    /// glance, and only this tells them apart.
    let report () : string =
        let r = toolchain ()

        let dvisvgm =
            match r.Dvisvgm.Path, r.DvisvgmVersion with
            | Some p, Some v -> sprintf "dvisvgm found at %s — %s" p v
            | Some p, None -> sprintf "dvisvgm found at %s, but it could not say what version it is — the installation is broken" p
            | None, _ -> Toolchain.missing "no formula is typeset, and every run stays as plain text" r.Dvisvgm

        [ Toolchain.report "no formula is typeset, and every run stays as plain text" r.Engine
          dvisvgm ]
        |> String.concat "\n"

    // ---- the document ----------------------------------------------------------------

    /// The document one snippet is typeset in.
    ///
    /// `\setbox0` before `\box0` so that TeX can be asked what it made, on the line between:
    /// `\the\wd0`, `\the\ht0` and `\the\dp0` are the only source of the **baseline**, which is
    /// the division of the box's height into the part above the line and the part below it. An
    /// ink bounding box cannot supply that — `$x$` has no descender and `$\sum_i$` has a deep
    /// one, and placing both by their ink would put them on different lines.
    ///
    /// Public because it is pure, and because a caller debugging a rejected snippet wants the
    /// document TeX actually saw rather than a description of it.
    let document (options: TexOptions) (source: string) : string =
        String.Concat
            [| "\\documentclass["
               options.ClassOptions
               "]{standalone}\n"
               options.Preamble
               "\\begin{document}\n"
               "\\setbox0=\\hbox{"
               source
               "}%\n"
               "\\typeout{MAREYBOX wd=\\the\\wd0 ht=\\the\\ht0 dp=\\the\\dp0}%\n"
               "\\box0\n"
               "\\end{document}\n" |]

    /// TeX reports an error as a line beginning `! `.
    ///
    /// **Do not pass `-file-line-error` to the engine.** It rewrites errors as
    /// `./doc.tex:5: Undefined control sequence.`, which does not start with `! ` — so this
    /// filter finds nothing, every document is declared clean, and the gate becomes theatre.
    /// That exact mistake was made in `TexToolchain` and is recorded there; it is not repeated
    /// here.
    let private errorLines (log: string) =
        log.Split '\n'
        |> Array.map (fun l -> l.TrimEnd '\r')
        |> Array.filter (fun l -> l.StartsWith("! ", StringComparison.Ordinal))

    /// TeX points to `bp`, which is what SVG and Marey both call a point.
    ///
    /// `\the\wd0` is in TeX points: 72.27 to the inch, against 72 for the `pt` in this library's
    /// unit system. A factor of 1.00375 — a third of a percent, which is a quarter of a pixel on
    /// a 60 pt formula and would go unnoticed for as long as it took someone to measure a figure
    /// against a ruler.
    [<Literal>]
    let private BpPerTexPt = 72.0 / 72.27

    let private boxPattern =
        Regex(@"MAREYBOX\s+wd=(-?[\d.]+)pt\s*ht=(-?[\d.]+)pt\s*dp=(-?[\d.]+)pt", RegexOptions.Compiled)

    /// The box TeX reported, out of its own log.
    ///
    /// Public and pure for the reason the whole of `TexOutline.fs` is: this is where a factor of
    /// 1.00375 can hide, and a gate should be able to hand it a log line.
    let parseBox (log: string) : Result<TexBox, string> =
        let m = boxPattern.Match log

        if not m.Success then
            Error "the engine's log carries no MAREYBOX line, so the box dimensions are unknown"
        else
            let read (i: int) =
                Double.Parse(m.Groups.[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture)
                * BpPerTexPt
                * 1.0<pt>

            Ok
                { Width = read 1
                  Height = read 2
                  Depth = read 3 }

    // ---- running the tools -----------------------------------------------------------

    /// Typeset one snippet. Not memoised — `scene` is what holds the cache, because a cache
    /// shared between calls would be a process-wide mutable keyed on a preamble, and two callers
    /// with different preambles would silently share outlines.
    let private runOnce
        (engine: ResolvedTool)
        (dvisvgm: ResolvedTool)
        (options: TexOptions)
        (source: string)
        : Result<TexOutline, string> =
        let dir =
            Path.Combine(Path.GetTempPath(), "marey-typeset-" + Guid.NewGuid().ToString("N").Substring(0, 12))

        Directory.CreateDirectory dir |> ignore

        try
            let stem = "snippet"
            let texFile = Path.Combine(dir, stem + ".tex")
            File.WriteAllText(texFile, document options source, UTF8Encoding false)

            // `-halt-on-error` is deliberately not passed: every error the engine finds makes one
            // actionable report rather than the first of several. `-interaction=nonstopmode`
            // stops it asking a question on a stdin nothing is reading.
            match Toolchain.runArgs engine [ "-interaction=nonstopmode"; stem + ".tex" ] dir options.TimeoutMs with
            | ToolUnavailable why -> Error why
            | ToolTimedOut why -> Error why
            | ToolRan run ->
                // The log file is more complete than stdout when the engine is chatty, and it is
                // also where `\typeout` reliably lands.
                let logFile = Path.Combine(dir, stem + ".log")

                let log =
                    if File.Exists logFile then
                        try
                            File.ReadAllText logFile
                        with _ ->
                            run.Out
                    else
                        run.Out

                let dvi = Path.Combine(dir, stem + ".dvi")

                // Two independent signals, because relying on one is how the test suite's TeX
                // gate went vacuous the first time: the log gives a usable message and the exit
                // code cannot be missed by a pattern that stops matching after an engine update.
                match errorLines log, run.ExitCode with
                | errs, _ when errs.Length > 0 -> Error(String.Join("\n", errs))
                | _, code when code <> 0 ->
                    Error(sprintf "%s exited with code %d but reported no '!' error line" Engine code)
                | _ ->

                if not (File.Exists dvi) then
                    Error(sprintf "%s reported no errors but wrote no DVI" Engine)
                else

                match parseBox log with
                | Error why -> Error why
                | Ok box ->

                let svgFile = Path.Combine(dir, stem + ".svg")

                // `--no-fonts` turns every glyph into an outline instead of a font reference,
                // which is the whole reason this route produces something every backend can
                // draw. `--bbox=papersize` is what makes the frame exact — see `TexOutline`.
                match
                    Toolchain.runArgs
                        dvisvgm
                        [ "--no-fonts"; "--bbox=papersize"; "--output=" + svgFile; dvi ]
                        dir
                        options.TimeoutMs
                with
                | ToolUnavailable why -> Error why
                | ToolTimedOut why -> Error why
                | ToolRan conv when conv.ExitCode <> 0 ->
                    Error(sprintf "dvisvgm exited with code %d: %s" conv.ExitCode (conv.Error.Trim()))
                | ToolRan conv ->
                    if not (File.Exists svgFile) then
                        Error(sprintf "dvisvgm exited zero and wrote no SVG: %s" (conv.Error.Trim()))
                    else
                        TexOutline.ofSvg box (File.ReadAllText svgFile)
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    // ---- placement -------------------------------------------------------------------

    /// Turn an outline into the `Path` node that replaces a run.
    ///
    /// **The arithmetic that has to agree with `TextMeasure.bounds`.** A `TextRun` places itself
    /// by an anchor and a baseline, and the replacement has to land where the text would have —
    /// otherwise a figure typesets and every label moves. `ht` and `dp` play the parts `Ascent`
    /// and `Descent` play for a font, and the three baseline cases are the ones `textOrigin`
    /// resolves in both built backends, in the same order and with the same signs.
    ///
    /// **Rotation is applied in `pt`, before the map to `nd`.** `nd` is a fraction of a surface
    /// that is not square, so the map to it is anisotropic and does not preserve angles;
    /// rotating after it would render a requested 30° as something else and shear every glyph.
    /// The two text backends make the same distinction for the same reason.
    let private place (options: TexOptions) (extent: Extent<px>) (run: TextRun) (outline: TexOutline) : Node =
        let s = float (run.Font.Size / options.BaseSize)

        let wd = float outline.Box.Width * s * 1.0<pt>
        let ht = float outline.Box.Height * s * 1.0<pt>
        let dp = float outline.Box.Depth * s * 1.0<pt>

        // The anchor, resolved against the box's own width — which includes the blank space TeX
        // put in it. That is the width the formula occupies, and it is what a caller centring a
        // title means.
        let ox =
            match run.Anchor with
            | AnchorStart -> 0.0<pt>
            | AnchorMiddle -> -wd / 2.0
            | AnchorEnd -> -wd

        // How far below `At` the baseline sits, positive downward — `textOrigin`'s `oy`, in the
        // units this pass works in.
        let by =
            match run.Baseline with
            | BaselineAlphabetic -> 0.0<pt>
            | BaselineMiddle -> (ht - dp) / 2.0
            | BaselineHanging -> ht

        let cosR = cos (float run.Rotation)
        let sinR = sin (float run.Rotation)

        // pt to nd, per axis. Anisotropic on purpose: `nd` normalises each axis by its own side,
        // so a glyph mapped through it comes out the right shape on a surface that is not square
        // — the same trick the panel geometry uses.
        let ndx (v: float<pt>) = Space.pxOfPt options.Dpi v / extent.Width * 1.0<nd>
        let ndy (v: float<pt>) = Space.pxOfPt options.Dpi v / extent.Height * 1.0<nd>

        let point (p: Vec2<pt>) : Vec2<nd> =
            // Into a frame that is `pt`, y **up**, and centred on the run's own `At`.
            let u = ox + p.X * s
            let v = -(by + (p.Y - outline.Box.Height) * s)

            let u' = u * cosR - v * sinR
            let v' = u * sinR + v * cosR

            { X = run.At.X + ndx u'
              Y = run.At.Y + ndy v' }

        let segments =
            outline.Segments
            |> Array.map (function
                | TexSeg.MoveTo p -> MoveTo(point p)
                | TexSeg.LineTo p -> LineTo(point p)
                | TexSeg.CubicTo(c1, c2, p) -> CubicTo(point c1, point c2, point p)
                | TexSeg.ClosePath -> ClosePath)

        // **Filled, non-zero, unstroked.** A glyph outline is a filled region and its counters —
        // the hole in an `o` — are subpaths wound the other way; even-odd would agree on an `o`
        // and disagree wherever a glyph self-overlaps, which is what a bold italic does at a
        // stem join. Type 1 and TrueType both specify non-zero, so this is the rule the outline
        // was drawn under rather than a choice.
        // `Node.Path`, qualified: this file opens `System.IO` for the temporary directory, and
        // `Path` there is the path-manipulation class.
        Node.Path(segments, None, Some run.Paint, NonZero)

    // ---- the scene pass --------------------------------------------------------------

    /// Typeset every run in a scene that carries a `Tex` form, against tools the caller names.
    ///
    /// A scene-to-scene pass, the shape `Degrade.forCaps` established: what comes back is a
    /// `Node` to hand to any backend, and every subtree this pass did not change is returned
    /// **by reference**.
    ///
    /// `extent` is required and is not a smell. The outline arrives in `pt` and the IR is in
    /// `nd`, and the conversion is only defined against the surface the scene will be drawn on —
    /// it is the same number `Chart.render` already takes and the backend's `Begin` already
    /// needs. Typesetting for one extent and drawing at another gives formulas of the wrong
    /// size, which is why the extent is an argument rather than a default.
    ///
    /// **The tools are arguments so that "no TeX installed" is reachable from a machine that has
    /// it.** `Typeset.scene` is this with the process's own; see the note there.
    let sceneWith
        (engine: ResolvedTool)
        (dvisvgm: ResolvedTool)
        (options: TexOptions)
        (extent: Extent<px>)
        (node: Node)
        : TypesetOutcome =

        if not (engine.IsAvailable && dvisvgm.IsAvailable) then
            let missing =
                [ if not engine.IsAvailable then
                      Toolchain.missing "no formula is typeset" engine
                  if not dvisvgm.IsAvailable then
                      Toolchain.missing "no formula is typeset" dvisvgm ]

            TypesetUnavailable(
                String.concat "\n" missing
                + "\nThe scene is unchanged: every run keeps its plain-text form and the figure still renders."
            )
        else

        // Keyed on the source alone. The size is a scale applied at placement, so one engine run
        // serves every size the same formula appears at; the preamble cannot vary within a call.
        let cache = Dictionary<string, Result<TexOutline, string>>()
        let mutable typeset = 0
        let mutable plain = 0
        let mutable recoloured = 0
        let failures = ResizeArray<TexFailure>()

        let outlineFor (source: string) =
            match cache.TryGetValue source with
            | true, hit -> hit
            | _ ->
                let result = runOnce engine dvisvgm options source
                cache.[source] <- result
                result

        // Returns the node **by reference** when nothing beneath it changed, so a pass that
        // replaces two axis titles allocates two paths rather than a copy of the figure.
        let rec walk (n: Node) : Node =
            match n with
            | Text run ->
                match run.Tex with
                | None ->
                    plain <- plain + 1
                    n
                | Some source ->
                    match outlineFor source with
                    | Ok outline ->
                        typeset <- typeset + 1
                        recoloured <- recoloured + outline.Recoloured
                        place options extent run outline
                    | Error why ->
                        // **The run stays, as text.** A formula that would not typeset is worse
                        // than one that did and far better than a hole in the figure, and the
                        // caller is told which runs those were rather than left to compare two
                        // renders.
                        failures.Add { Source = source; Reason = why }
                        n
            | Group children ->
                let mapped = children |> Array.map walk

                if Array.forall2 (fun (a: Node) b -> obj.ReferenceEquals(a, b)) children mapped then
                    n
                else
                    // `Node.Group`, qualified: this file opens `System.Text.RegularExpressions`
                    // for the log parser, and `Group` there is a capture group.
                    Node.Group mapped
            | Transform(m, child) ->
                let mapped = walk child
                if obj.ReferenceEquals(mapped, child) then n else Transform(m, mapped)
            | Clip(segs, child) ->
                let mapped = walk child
                if obj.ReferenceEquals(mapped, child) then n else Clip(segs, mapped)
            | Opacity(alpha, child) ->
                let mapped = walk child
                if obj.ReferenceEquals(mapped, child) then n else Opacity(alpha, mapped)
            | Keyed(key, child) ->
                let mapped = walk child
                if obj.ReferenceEquals(mapped, child) then n else Keyed(key, mapped)
            | Annotated(semantics, child) ->
                let mapped = walk child

                if obj.ReferenceEquals(mapped, child) then
                    n
                else
                    Annotated(semantics, mapped)
            | Node.Path _
            | Markers _
            | Raster _ -> n

        let scene = walk node

        Typeset
            { Scene = scene
              Typeset = typeset
              Plain = plain
              Failed = List.ofSeq failures
              EngineRuns = cache.Count
              Recoloured = recoloured }

    /// Typeset every run in a scene that carries a `Tex` form.
    ///
    /// `sceneWith` with the tools this process resolved. One implementation of the pass,
    /// projected — the same rule `TexToolchain.compileWith` states in the test suite, and the
    /// reason the degrade gate below can be written at all: **a gate on a machine that has TeX
    /// installed cannot otherwise check what happens on one that does not**, and gate 13 is
    /// precisely that check. `Video.muxWith` exists for the same reason and was the precedent.
    let scene (options: TexOptions) (extent: Extent<px>) (node: Node) : TypesetOutcome =
        let tools = toolchain ()
        sceneWith tools.Engine tools.Dvisvgm options extent node

    /// The scene to draw, whatever happened, and a line saying what happened.
    ///
    /// **Degrading is the documented behaviour, so it is a function rather than a pattern every
    /// caller writes.** D62's shape a fourth time: the figure renders on a machine with no TeX,
    /// and the difference is *reported* instead of being invisible. A caller who wants to fail
    /// on a missing toolchain matches `scene`'s outcome instead — which is the point of there
    /// being two entry points rather than one that guesses.
    let sceneOrPlain (options: TexOptions) (extent: Extent<px>) (node: Node) : Node * string =
        match scene options extent node with
        | TypesetUnavailable why -> node, why
        | Typeset result ->
            let line =
                sprintf
                    "typeset %d run(s) in %d engine run(s); %d had no TeX form"
                    result.Typeset
                    result.EngineRuns
                    result.Plain

            let notes =
                [ if not result.Failed.IsEmpty then
                      yield
                          sprintf
                              "%d run(s) would not typeset and stayed as text:\n%s"
                              result.Failed.Length
                              (result.Failed
                               |> List.map (fun f -> sprintf "  %s\n    %s" f.Source (f.Reason.Replace("\n", "\n    ")))
                               |> String.concat "\n")
                  if result.Recoloured > 0 then
                      yield
                          sprintf
                              "%d element(s) asked for a colour of their own and were drawn in the run's paint instead"
                              result.Recoloured ]

            result.Scene, String.concat "\n" (line :: notes)
