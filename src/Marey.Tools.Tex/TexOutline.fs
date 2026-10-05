namespace Marey

open System
open System.Collections.Generic
open System.Globalization
open System.Xml.Linq

// ======================================================================================
// D79 — THE HALF THAT NEEDS NO TEX INSTALLED.
//
// §10 names three routes to mathematical typesetting and D79 took the first: shell out to
// LaTeX, render DVI to vector paths, and ship it as an opt-in package — "acceptable as a
// plugin, unacceptable as a dependency". The paths are the point. Once a formula is outlines,
// *every* backend draws it: SVG, TikZ and Skia alike, with no capability flag and no per-backend
// typesetting story. That is what option 1 buys over option 2, which would have worked on a web
// backend that does not exist until M4.
//
// This file is the reading half, and it is deliberately **pure**. D75's phase said the mux was
// "testable only for exit status and container-level facts" and was wrong about its own package:
// `Video.args` turned out to be a pure function, so the part where the risk actually lived was
// gated on a machine with no ffmpeg at all. The same is true here and more so. Everything that
// can silently produce a *wrong picture* — a misparsed cubic, a dropped subpath, a `<use>`
// resolved to the wrong glyph — happens in this file, against a string, with no process
// involved. `Typeset.fs` runs the tools; this reads what they said.
//
// ---------------------------------------------------------------------------------------
// WHAT DVISVGM EMITS, MEASURED RATHER THAN ASSUMED
// ---------------------------------------------------------------------------------------
//
// `dvisvgm --no-fonts --bbox=papersize` over a `standalone` document produces exactly this
// vocabulary, checked across text, fractions, roots, sums, integrals and the whole ASCII range:
//
//   <defs><path id='g0-64' d='…'/></defs>     one glyph outline per distinct glyph
//   <g id='page1'>
//     <use x='…' y='…' xlink:href='#g0-64'/>  the glyph, translated to its position
//     <rect x='…' y='…' width='…' height='…'/> a TeX rule: fraction bars, \sqrt, \underline
//   </g>
//
// with `M`, `L`, `H`, `V`, `C`, `S` and `Z` in the path data and no transforms at all —
// `\scalebox{2}` selects a larger *font* rather than emitting a scale. The parser below accepts
// more than that (relative commands, quadratics, `<g transform>`, body `<path>`) because a
// future dvisvgm or a TikZ-bearing snippet may use them, and **refuses by name** on anything
// left over.
//
// **Refusing is the whole discipline here.** D33's incident was a perfectly valid document
// containing no visible ink, and the lesson it left is that silently dropping a drawing
// instruction is the worst available failure: the caller gets a formula with a piece missing and
// nothing anywhere says so. So an unknown element, an unknown path command and an unresolvable
// `<use>` are all errors that name themselves, and none of them is a warning.
//
// ---------------------------------------------------------------------------------------
// THE FRAME, AND WHY IT IS EXACT
// ---------------------------------------------------------------------------------------
//
// `--bbox=papersize` makes the SVG's viewBox the page box, and `standalone` makes the page box
// the TeX box. Measured over five snippets — a fraction, a bare `x`, a sum with a deep
// subscript, a word of text and a number — the viewBox is `(-72, -72, wd, ht + dp)` to five
// decimal places every time, with `wd`, `ht` and `dp` the box dimensions TeX itself reports.
//
// So the frame is not inferred from the ink: the origin is the constant `(-72, -72)` — one inch
// up and left of the DVI reference point — and the **baseline is `ht` below the top of the box**.
// That matters because a formula has to sit on the same baseline as the text it replaces, and an
// ink bounding box cannot say where that is: `$x$` has no descender and `$\sum_i$` has a deep
// one, and an ink box would put the two on different lines.
//
// dvisvgm's *default* bbox mode is the ink box, and on the first snippet tried it happened to
// agree with `ht + dp` to five decimals. That coincidence is why `--bbox=papersize` is passed
// explicitly and why this comment exists.
// ======================================================================================

/// One segment of a typeset outline, in the box frame: `pt`, y **downward**, origin at the
/// top-left corner of the TeX box.
///
/// Deliberately not `PathSeg`. That type is in `nd`, and `nd` is a fraction of a surface whose
/// size this file has no business knowing — the conversion needs an extent, a dpi and the run's
/// font size, all of which arrive at placement time in `Typeset.fs`. Keeping the outline in the
/// units TeX produced it in means the reading half can be gated against numbers a person can
/// check against a `.svg` file by eye.
[<RequireQualifiedAccess>]
type TexSeg =
    | MoveTo of Vec2<pt>
    | LineTo of Vec2<pt>
    | CubicTo of c1: Vec2<pt> * c2: Vec2<pt> * until: Vec2<pt>
    | ClosePath

/// What TeX said the box measures. Reported by the engine itself through `\the\wd0` and friends,
/// not derived from the ink.
[<Struct>]
type TexBox =
    { /// The box's own width, which includes whatever blank space TeX put in it — a `\frac`
      /// carries `\nulldelimiterspace` on both sides, so this is 2.4 pt wider than the ink.
      /// That is the width the formula *occupies*, and therefore the width an anchor resolves
      /// against.
      Width: float<pt>
      /// Above the baseline.
      Height: float<pt>
      /// Below the baseline. Zero for `$x$`, 3.4 pt for `$\frac{a}{b}$`.
      Depth: float<pt> }

/// A typeset snippet: what TeX made of it, in the box's own frame.
[<NoComparison>]
type TexOutline =
    { Box: TexBox
      /// Every filled subpath of the snippet, concatenated. Glyphs and rules alike — a fraction
      /// bar is a `<rect>` and arrives here as four lines and a close, because the consumer is
      /// one `Path` node and a node cannot hold two fill rules.
      Segments: TexSeg[]
      /// Elements that carried a colour of their own, which was **not** honoured.
      ///
      /// A `TextRun` has one `Paint` and the outline inherits it, so `\textcolor{red}{x}` comes
      /// out in the run's colour rather than in red. Counted rather than ignored, and counted
      /// rather than refused: the shape of the formula is right, only its colour is the run's,
      /// and a caller who wrote `\textcolor` deserves to be told which of those happened.
      Recoloured: int }

// ======================================================================================
// SVG PATH DATA
// ======================================================================================

/// Parsing an SVG `d` attribute.
///
/// **Public, because it is where the risk is and it is pure.** A gate can hand it a string and
/// check every number, on a machine with no TeX and no dvisvgm — which is the only way the
/// arithmetic in `S` and `T`, the reflected control points, ever gets checked at all.
[<RequireQualifiedAccess>]
module SvgPathData =

    /// A number, an optional comma or whitespace, or a command letter. SVG's grammar allows
    /// numbers to run together without a separator (`.5.5` is two numbers, and `1-2` is two),
    /// which is why this is a hand-written scanner and not `String.Split`.
    let private isCommand (c: char) =
        match Char.ToUpperInvariant c with
        | 'M'
        | 'L'
        | 'H'
        | 'V'
        | 'C'
        | 'S'
        | 'Q'
        | 'T'
        | 'A'
        | 'Z' -> true
        | _ -> false

    /// Read one number starting at `i`, returning it and the index after it.
    ///
    /// `-` and `+` start a number rather than separating two, except immediately after a digit
    /// or a `.`, where they begin the next one — `1-2` is `1` then `-2`. An exponent's sign is
    /// the exception to the exception and is why `e`/`E` is tracked rather than merely allowed.
    let private readNumber (s: string) (i: int) : Result<float * int, string> =
        let mutable j = i

        if j < s.Length && (s.[j] = '-' || s.[j] = '+') then
            j <- j + 1

        let digitsFrom = j

        while j < s.Length && Char.IsDigit s.[j] do
            j <- j + 1

        if j < s.Length && s.[j] = '.' then
            j <- j + 1

            while j < s.Length && Char.IsDigit s.[j] do
                j <- j + 1

        if j = digitsFrom || (j = digitsFrom + 1 && s.[digitsFrom] = '.') then
            Error(sprintf "expected a number at offset %d of the path data" i)
        else

        if j < s.Length && (s.[j] = 'e' || s.[j] = 'E') then
            let save = j
            let mutable k = j + 1

            if k < s.Length && (s.[k] = '-' || s.[k] = '+') then
                k <- k + 1

            let expFrom = k

            while k < s.Length && Char.IsDigit s.[k] do
                k <- k + 1

            // A bare `e` with no digits after it is not an exponent — it is the next command
            // letter, and `1e` would otherwise swallow it and then fail on the number after.
            j <- if k > expFrom then k else save

        match Double.TryParse(s.Substring(i, j - i), NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, v -> Ok(v, j)
        | _ -> Error(sprintf "'%s' at offset %d is not a number" (s.Substring(i, j - i)) i)

    let private skipSeparators (s: string) (i: int) =
        let mutable j = i

        while j < s.Length && (Char.IsWhiteSpace s.[j] || s.[j] = ',') do
            j <- j + 1

        j

    /// Parse a `d` attribute into absolute segments, in the user units it was written in.
    ///
    /// Quadratics are converted to cubics **exactly** — a degree-2 Bézier is a degree-3 one with
    /// the control points at `p0 + 2/3(q - p0)` and `p2 + 2/3(q - p2)` — because `PathSeg`
    /// downstream has no quadratic case and an approximation would be a different curve.
    ///
    /// Elliptical arcs are **refused by name**. They cannot be written exactly as cubics, no
    /// glyph outline contains one, and a silent approximation of a drawing instruction is the
    /// failure this whole file is written against.
    let parse (d: string) : Result<TexSeg[], string> =
        if isNull d then
            Ok [||]
        else

        let out = ResizeArray<TexSeg>()
        let v (x: float) (y: float) : Vec2<pt> = { X = x * 1.0<pt>; Y = y * 1.0<pt> }

        // The current point, the subpath's start (where `Z` returns to), and the previous
        // command's second control point — which `S` and `T` reflect through the current point.
        let mutable cx = 0.0
        let mutable cy = 0.0
        let mutable sx = 0.0
        let mutable sy = 0.0
        let mutable prevCubicX = 0.0
        let mutable prevCubicY = 0.0
        let mutable prevQuadX = 0.0
        let mutable prevQuadY = 0.0
        let mutable hadCubic = false
        let mutable hadQuad = false

        let mutable i = 0
        let mutable command = ' '
        let mutable error : string option = None

        let quadToCubic (qx: float) (qy: float) (x: float) (y: float) =
            let c1x = cx + 2.0 / 3.0 * (qx - cx)
            let c1y = cy + 2.0 / 3.0 * (qy - cy)
            let c2x = x + 2.0 / 3.0 * (qx - x)
            let c2y = y + 2.0 / 3.0 * (qy - y)
            out.Add(TexSeg.CubicTo(v c1x c1y, v c2x c2y, v x y))

        while error.IsNone && i < d.Length do
            i <- skipSeparators d i

            if i < d.Length then
                if isCommand d.[i] then
                    command <- d.[i]
                    i <- i + 1
                elif Char.IsLetter d.[i] then
                    // A letter that is not a command. Caught here rather than left to the number
                    // reader, which would report "expected a number at offset 7" and leave the
                    // reader to work out that the offset holds an 'X'.
                    error <- Some(sprintf "'%c' at offset %d is not an SVG path command" d.[i] i)
                elif command = ' ' then
                    error <- Some(sprintf "path data starts with '%c' rather than a command" d.[i])

                if error.IsNone then
                    let relative = Char.IsLower command
                    let upper = Char.ToUpperInvariant command

                    // `Z` takes no arguments; everything else repeats until the next command
                    // letter, which is how `M` followed by three point pairs means one moveto
                    // and two linetos.
                    if upper = 'Z' then
                        out.Add TexSeg.ClosePath
                        cx <- sx
                        cy <- sy
                        hadCubic <- false
                        hadQuad <- false
                    else
                        let n =
                            match upper with
                            | 'M'
                            | 'L'
                            | 'T' -> 2
                            | 'H'
                            | 'V' -> 1
                            | 'C' -> 6
                            | 'S'
                            | 'Q' -> 4
                            | 'A' -> 7
                            | _ -> -1

                        if n < 0 then
                            error <- Some(sprintf "'%c' is not an SVG path command" command)
                        elif upper = 'A' then
                            error <-
                                Some
                                    "the path data contains an elliptical arc ('A'), which has no exact cubic form. Marey's IR has no arc segment, and approximating one silently would change the drawing."
                        else
                            let args = Array.zeroCreate<float> n
                            let mutable k = 0

                            while error.IsNone && k < n do
                                i <- skipSeparators d i

                                match readNumber d i with
                                | Ok(value, next) ->
                                    args.[k] <- value
                                    i <- next
                                    k <- k + 1
                                | Error why -> error <- Some why

                            if error.IsNone then
                                let ax j = if relative then cx + args.[j] else args.[j]
                                let ay j = if relative then cy + args.[j] else args.[j]

                                match upper with
                                | 'M' ->
                                    cx <- ax 0
                                    cy <- ay 1
                                    sx <- cx
                                    sy <- cy
                                    out.Add(TexSeg.MoveTo(v cx cy))
                                    // A repeated `M` argument pair is an implicit `L`, per the
                                    // SVG grammar. Lower-casing keeps the relative sense.
                                    command <- if relative then 'l' else 'L'
                                    hadCubic <- false
                                    hadQuad <- false
                                | 'L' ->
                                    cx <- ax 0
                                    cy <- ay 1
                                    out.Add(TexSeg.LineTo(v cx cy))
                                    hadCubic <- false
                                    hadQuad <- false
                                | 'H' ->
                                    cx <- ax 0
                                    out.Add(TexSeg.LineTo(v cx cy))
                                    hadCubic <- false
                                    hadQuad <- false
                                | 'V' ->
                                    cy <- ay 0
                                    out.Add(TexSeg.LineTo(v cx cy))
                                    hadCubic <- false
                                    hadQuad <- false
                                | 'C' ->
                                    let c1x, c1y = ax 0, ay 1
                                    let c2x, c2y = ax 2, ay 3
                                    let x, y = ax 4, ay 5
                                    out.Add(TexSeg.CubicTo(v c1x c1y, v c2x c2y, v x y))
                                    prevCubicX <- c2x
                                    prevCubicY <- c2y
                                    cx <- x
                                    cy <- y
                                    hadCubic <- true
                                    hadQuad <- false
                                | 'S' ->
                                    // The first control point is the previous cubic's second,
                                    // reflected through the current point — and the current
                                    // point itself when the previous command was not a cubic.
                                    let c1x = if hadCubic then 2.0 * cx - prevCubicX else cx
                                    let c1y = if hadCubic then 2.0 * cy - prevCubicY else cy
                                    let c2x, c2y = ax 0, ay 1
                                    let x, y = ax 2, ay 3
                                    out.Add(TexSeg.CubicTo(v c1x c1y, v c2x c2y, v x y))
                                    prevCubicX <- c2x
                                    prevCubicY <- c2y
                                    cx <- x
                                    cy <- y
                                    hadCubic <- true
                                    hadQuad <- false
                                | 'Q' ->
                                    let qx, qy = ax 0, ay 1
                                    let x, y = ax 2, ay 3
                                    quadToCubic qx qy x y
                                    prevQuadX <- qx
                                    prevQuadY <- qy
                                    cx <- x
                                    cy <- y
                                    hadQuad <- true
                                    hadCubic <- false
                                | 'T' ->
                                    let qx = if hadQuad then 2.0 * cx - prevQuadX else cx
                                    let qy = if hadQuad then 2.0 * cy - prevQuadY else cy
                                    let x, y = ax 0, ay 1
                                    quadToCubic qx qy x y
                                    prevQuadX <- qx
                                    prevQuadY <- qy
                                    cx <- x
                                    cy <- y
                                    hadQuad <- true
                                    hadCubic <- false
                                | _ -> error <- Some(sprintf "'%c' is not an SVG path command" command)

        match error with
        | Some why -> Error why
        | None -> Ok(out.ToArray())

// ======================================================================================
// THE SVG DOCUMENT
// ======================================================================================

/// Reading a dvisvgm document into an outline.
[<RequireQualifiedAccess>]
module TexOutline =

    /// SVG's namespace, and xlink's. `XDocument` keeps them, so an element is matched by
    /// `LocalName` rather than by a name that would have to carry the prefix.
    [<Literal>]
    let private Xlink = "http://www.w3.org/1999/xlink"

    let private attr (name: string) (e: XElement) =
        match e.Attribute(XName.Get name) with
        | null -> None
        | a -> Some a.Value

    let private number (name: string) (e: XElement) (fallback: float) =
        match attr name e with
        | None -> Ok fallback
        | Some raw ->
            // dvisvgm writes lengths as bare numbers inside the body and with a `pt` suffix on
            // the root's width/height. SVG's `pt` is 1/72 inch, which is exactly Marey's `pt`,
            // so the suffix is dropped rather than converted.
            let trimmed = raw.Trim().TrimEnd('p', 't', 'x', 'b')

            match Double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture) with
            | true, v -> Ok v
            | _ -> Error(sprintf "'%s' is not a length ('%s' on <%s>)" raw name e.Name.LocalName)

    /// A 2D affine as six numbers, in SVG's own order. Not `Affine<'a,'b>`: that type is
    /// `nd -> nd` by construction and this frame is `pt`, and borrowing it would mean lying
    /// about the units of every intermediate.
    type private Mat = { A: float; B: float; C: float; D: float; E: float; F: float }

    let private identity = { A = 1.0; B = 0.0; C = 0.0; D = 1.0; E = 0.0; F = 0.0 }

    let private translate dx dy = { identity with E = dx; F = dy }

    /// `m` applied after `n` — the order a nested `<g transform>` composes in.
    let private compose (outer: Mat) (inner: Mat) =
        { A = outer.A * inner.A + outer.C * inner.B
          B = outer.B * inner.A + outer.D * inner.B
          C = outer.A * inner.C + outer.C * inner.D
          D = outer.B * inner.C + outer.D * inner.D
          E = outer.A * inner.E + outer.C * inner.F + outer.E
          F = outer.B * inner.E + outer.D * inner.F + outer.F }

    let private apply (m: Mat) (p: Vec2<pt>) : Vec2<pt> =
        let x = float p.X
        let y = float p.Y

        { X = (m.A * x + m.C * y + m.E) * 1.0<pt>
          Y = (m.B * x + m.D * y + m.F) * 1.0<pt> }

    /// SVG's `transform` attribute, for the forms dvisvgm can emit.
    ///
    /// `rotate` and `skew` are refused rather than implemented: dvisvgm does not produce them
    /// for a DVI page, and a transform silently ignored is a formula drawn in the wrong place.
    let private parseTransform (raw: string) : Result<Mat, string> =
        let mutable result = Ok identity
        let mutable i = 0

        while (match result with
               | Ok _ -> true
               | Error _ -> false)
              && i < raw.Length do
            while i < raw.Length && (Char.IsWhiteSpace raw.[i] || raw.[i] = ',') do
                i <- i + 1

            if i < raw.Length then
                let openParen = raw.IndexOf('(', i)
                let closeParen = if openParen < 0 then -1 else raw.IndexOf(')', openParen)

                if openParen < 0 || closeParen < 0 then
                    result <- Error(sprintf "'%s' is not a transform list" raw)
                else
                    let name = raw.Substring(i, openParen - i).Trim()

                    let args =
                        raw
                            .Substring(openParen + 1, closeParen - openParen - 1)
                            .Split([| ' '; ','; '\t'; '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
                        |> Array.map (fun s ->
                            match Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture) with
                            | true, v -> Some v
                            | _ -> None)

                    if args |> Array.exists Option.isNone then
                        result <- Error(sprintf "'%s' has an argument that is not a number" raw)
                    else
                        let a = args |> Array.map Option.get

                        let piece =
                            match name, a.Length with
                            | "matrix", 6 -> Ok { A = a.[0]; B = a.[1]; C = a.[2]; D = a.[3]; E = a.[4]; F = a.[5] }
                            | "translate", 1 -> Ok(translate a.[0] 0.0)
                            | "translate", 2 -> Ok(translate a.[0] a.[1])
                            | "scale", 1 -> Ok { identity with A = a.[0]; D = a.[0] }
                            | "scale", 2 -> Ok { identity with A = a.[0]; D = a.[1] }
                            | _ ->
                                Error(
                                    sprintf
                                        "'%s' is a transform this reader does not implement. dvisvgm does not emit it for a DVI page, and applying it wrongly would put the formula somewhere else on the figure."
                                        name
                                )

                        match result, piece with
                        | Ok acc, Ok p -> result <- Ok(compose acc p)
                        | _, Error why -> result <- Error why
                        | Error _, _ -> ()

                    i <- closeParen + 1

        result

    /// A `<rect>` as four lines and a close, so that one `Path` node can hold a fraction bar and
    /// the glyphs on either side of it.
    let private rectSegments (x: float) (y: float) (w: float) (h: float) =
        let v a b : Vec2<pt> = { X = a * 1.0<pt>; Y = b * 1.0<pt> }

        [| TexSeg.MoveTo(v x y)
           TexSeg.LineTo(v (x + w) y)
           TexSeg.LineTo(v (x + w) (y + h))
           TexSeg.LineTo(v x (y + h))
           TexSeg.ClosePath |]

    let private transformed (m: Mat) (segs: TexSeg[]) =
        segs
        |> Array.map (function
            | TexSeg.MoveTo p -> TexSeg.MoveTo(apply m p)
            | TexSeg.LineTo p -> TexSeg.LineTo(apply m p)
            | TexSeg.CubicTo(c1, c2, p) -> TexSeg.CubicTo(apply m c1, apply m c2, apply m p)
            | TexSeg.ClosePath -> TexSeg.ClosePath)

    /// Does this element ask for a colour of its own?
    ///
    /// `fill='none'` is not a colour, it is "do not paint", and it appears on a `<g>` that
    /// carries stroked graphics. Counting it as a recolour would report a colour override on
    /// every TikZ picture that draws a line.
    let private asksForColour (e: XElement) =
        [ "fill"; "stroke"; "color" ]
        |> List.exists (fun n ->
            match attr n e with
            | Some v -> v.Trim() <> "" && v.Trim() <> "none" && v.Trim() <> "currentColor"
            | None -> false)

    /// Read a dvisvgm document into an outline, given the box TeX reported for it.
    ///
    /// The box is not read out of the SVG: `--bbox=papersize` makes the viewBox the page box and
    /// `standalone` makes the page box the TeX box, but only TeX knows how that box divides into
    /// height and depth — and the baseline is exactly that division. See this file's header.
    ///
    /// The origin is moved to the box's top-left corner on the way out, so a consumer never sees
    /// dvisvgm's `-72`.
    let ofSvg (box: TexBox) (svg: string) : Result<TexOutline, string> =
        let doc =
            try
                Ok(XDocument.Parse svg)
            with e ->
                Error(sprintf "dvisvgm produced something that is not XML: %s" e.Message)

        match doc with
        | Error why -> Error why
        | Ok doc ->

        let root = doc.Root

        if isNull root then
            Error "dvisvgm produced an empty document"
        else

        // The viewBox's origin, which the whole page is expressed relative to. Read rather than
        // assumed to be -72: the constant is what this repository *measured*, and a document
        // that disagrees should shift with its own numbers rather than be placed by a memory of
        // someone else's.
        let originResult =
            match attr "viewBox" root with
            | None -> Error "dvisvgm wrote no viewBox, so there is nothing to place the outline against"
            | Some raw ->
                let parts =
                    raw.Split([| ' '; ',' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun s ->
                        match Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture) with
                        | true, v -> Some v
                        | _ -> None)

                if parts.Length <> 4 || parts |> Array.exists Option.isNone then
                    Error(sprintf "'%s' is not a viewBox" raw)
                else
                    Ok(Option.get parts.[0], Option.get parts.[1])

        match originResult with
        | Error why -> Error why
        | Ok(originX, originY) ->

        // Glyph outlines, by id. Collected from anywhere in the document rather than only from
        // `<defs>`: SVG resolves `xlink:href` by id, and a reader that insisted on the parent
        // element would break on a document that is still perfectly valid.
        let defs = Dictionary<string, TexSeg[]>()
        let mutable failure: string option = None

        for e in root.Descendants() do
            if failure.IsNone && e.Name.LocalName = "path" then
                match attr "id" e with
                | Some id ->
                    match SvgPathData.parse (defaultArg (attr "d" e) "") with
                    | Ok segs -> defs.[id] <- segs
                    | Error why -> failure <- Some(sprintf "glyph '%s': %s" id why)
                | None -> ()

        let out = ResizeArray<TexSeg>()
        let mutable recoloured = 0

        let rec walk (m: Mat) (e: XElement) =
            if failure.IsSome then
                ()
            else

            if asksForColour e then
                recoloured <- recoloured + 1

            match e.Name.LocalName with
            | "defs" -> () // Already harvested, and never drawn where it stands.
            | "svg"
            | "g" ->
                let local =
                    match attr "transform" e with
                    | None -> Ok m
                    | Some raw -> parseTransform raw |> Result.map (compose m)

                match local with
                | Error why -> failure <- Some why
                | Ok local ->
                    for child in e.Elements() do
                        walk local child
            | "use" ->
                let href =
                    match e.Attribute(XName.Get("href", Xlink)) with
                    | null ->
                        match attr "href" e with
                        | Some h -> Some h
                        | None -> None
                    | a -> Some a.Value

                match href with
                | None -> failure <- Some "a <use> element names no glyph"
                | Some href ->
                    let id = href.TrimStart '#'

                    match defs.TryGetValue id with
                    | false, _ ->
                        // The failure this catches is a whole glyph missing from the picture,
                        // which is D33's "valid document, no visible ink" in miniature.
                        failure <- Some(sprintf "<use> refers to '%s', which is not in the document" id)
                    | true, segs ->
                        match number "x" e 0.0, number "y" e 0.0 with
                        | Ok x, Ok y -> out.AddRange(transformed (compose m (translate x y)) segs)
                        | Error why, _
                        | _, Error why -> failure <- Some why
            | "rect" ->
                match number "x" e 0.0, number "y" e 0.0, number "width" e 0.0, number "height" e 0.0 with
                | Ok x, Ok y, Ok w, Ok h -> out.AddRange(transformed m (rectSegments x y w h))
                | Error why, _, _, _
                | _, Error why, _, _
                | _, _, Error why, _
                | _, _, _, Error why -> failure <- Some why
            | "path" ->
                // A `<path>` with an id is a definition and was harvested above; one without is
                // drawn where it stands, which is what TikZ-bearing snippets produce.
                if (attr "id" e).IsNone then
                    match SvgPathData.parse (defaultArg (attr "d" e) "") with
                    | Ok segs -> out.AddRange(transformed m segs)
                    | Error why -> failure <- Some why
            | "title"
            | "desc"
            | "metadata"
            | "style" -> ()
            | other ->
                failure <-
                    Some(
                        sprintf
                            "<%s> is an element this reader does not implement, and skipping it would drop part of the formula without saying so."
                            other
                    )

        walk identity root

        match failure with
        | Some why -> Error why
        | None ->
            // Move the origin from dvisvgm's page frame to the box's top-left corner, so the
            // baseline is at `Box.Height` and a consumer never has to know about the inch.
            let shift = translate -originX -originY

            Ok
                { Box = box
                  Segments = transformed shift (out.ToArray())
                  Recoloured = recoloured }
