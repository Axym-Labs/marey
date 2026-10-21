namespace Marey

open System
open System.Collections.Generic
open System.Text

// ======================================================================================
// THE TIKZ WRITER: the primitives the TikZ backend is built out of.
//
// The SVG writer's opening claim applies here unchanged — **the same Scene must produce
// byte-identical output on every machine, every run** — and the number rules that guarantee it
// now live in `Marey.Core`'s `Num` module, shared by both backends. Two of them are worth
// restating because TeX raises the stakes:
//
//   * **No scientific notation, ever.** SVG merely fails to parse `1e-05` in path data. TeX
//     cannot read it as a dimension *at all*: `\path (1e-05bp, 0bp)` is a hard error, not a
//     wrong coordinate. `Num.upTo` cannot emit an exponent by construction.
//   * **Invariant culture.** A German locale writes `0,5`, and `(0,5bp, 0bp)` is a TikZ
//     coordinate with three components. Also a hard error.
//
// ---------------------------------------------------------------------------------------
// UNITS: `bp`, never `pt`. This is a landmine.
// ---------------------------------------------------------------------------------------
//
// `Space.fs` defines Core's `pt` as **1/72 inch** — the PostScript/big point. **TeX's `pt` is
// 1/72.27 inch.** They are different units that share a name, and TeX spells the 1/72-inch one
// `bp`.
//
// So emitting a Core `pt` value into a `.tex` file with a `pt` suffix is a silent 0.37% error in
// every coordinate, every stroke width and every marker size. It compiles, it renders, it looks
// right, and it is wrong — and it is invisible in a golden diff, because all the numbers are
// self-consistently wrong together. Hence `dim` below appends `bp` and there is deliberately no
// function in this file that will write a `pt` suffix on a length.
//
// The one place TeX's own `pt` is correct is `\fontsize{}{}`, which is a typographic request
// handed to the typesetter rather than a length being drawn. The backend passes `FontSpec.Size`
// there unconverted, on purpose.
//
// ---------------------------------------------------------------------------------------
// TEXT: two kinds of string, and conflating them corrupts the document.
// ---------------------------------------------------------------------------------------
//
// `TextRun.Tex` (D32) is TeX source and goes to the output verbatim. `TextRun.Text` is literal
// content and must be escaped: a title containing `50% yield` comments out the rest of the line
// and a `_` outside maths is an error. `runContent` is the only function that decides which is
// which, so no traversal code has to remember.
//
// Escaping is `Tex.escape` in Core: TeX's reserved characters, runs of Unicode superscripts and
// subscripts, and the stated set of non-ASCII characters — Greek, `·`, `Ω`, `µ`, `Å`, `°`, the
// operators — as kernel macros. It lives in Core because `Marey.Units` needs the identical rule
// for the quantity name inside a TeX axis title, which this backend passes through verbatim; a
// copy here would drift from that one.
//
// Everything else passes through as UTF-8; `Tex` says which of it pdfLaTeX can set.
// ======================================================================================

/// Number formatting, TeX escaping, colour definitions and identifier allocation for TikZ
/// output.
///
/// Public because the golden suite asserts on the formatting and escaping rules directly, not
/// only through rendered documents — the same reason `Svg` is public.
[<RequireQualifiedAccess>]
module internal Tikz =

    // ---- numbers ---------------------------------------------------------------------

    /// Decimal places for coordinates. A thousandth of a big point is ~0.35 µm on paper: far
    /// below anything a printer resolves, and coarse enough that a re-associated float multiply
    /// cannot move the digits and churn a golden diff.
    [<Literal>]
    let CoordDecimals = 3

    /// Decimal places for opacities, which live in zero to one.
    [<Literal>]
    let RatioDecimals = 4

    /// Decimal places for colour components. sRGB at 8 bits has a quantum of 1/255 = 3.9e-3, so
    /// six places is four orders of magnitude finer than anything a display can show — but it is
    /// cheap, and F1 measures the Oklab round-trip floor at 9.4e-7, which would be visible in a
    /// diff at three places and invisible at six.
    [<Literal>]
    let ColorDecimals = 6

    let num (v: float) : string = Num.upTo CoordDecimals v

    let ratio (v: float) : string = Num.upTo RatioDecimals v

    /// The largest magnitude, in `bp`, that may be written as a TeX dimension.
    ///
    /// **TeX cannot represent an arbitrary coordinate.** A dimension is a 32-bit fixed-point
    /// count of scaled points at 65536 sp per TeX point, so the maximum is 2^30 - 1 sp =
    /// 16383.99998 pt, which is 16322.06 `bp`. Past it the engine raises `Dimension too large`,
    /// and past about 2^31 the number scanner raises `Number too big` before even converting.
    /// Both are hard errors: the document does not compile.
    ///
    /// **This is not a hypothetical, and D9 is why.** `Scale.Forward` deliberately does not
    /// clamp — "clamping would pile out-of-range data onto the panel edge, which is a lie about
    /// the data" — so out-of-domain points legitimately project far outside [0,1] and are removed
    /// by the panel's `Clip` node. SVG writes those coordinates without complaint and the clip
    /// hides them. TeX refuses the file. So a backend that does not clamp cannot render a figure
    /// with a single out-of-domain point.
    ///
    /// 16000 leaves a comfortable margin below the limit and is ~28 times the width of an A4
    /// page, so it cannot affect a real figure: the largest sensible poster is a few thousand bp.
    [<Literal>]
    let MaxDim = 16000.0

    /// A TeX dimension: a number with a `bp` suffix, clamped to what TeX can represent.
    ///
    /// **`bp`, not `pt`** — see the note at the top of this file. This is the only function that
    /// should ever attach a unit to a length in this backend.
    ///
    /// Clamping is lossy and the loss is acknowledged rather than hidden: a segment running to a
    /// far-off point keeps its near end and has its far end pulled to the boundary, which changes
    /// the segment's direction when only one component is out of range. Everything affected is
    /// outside the panel and clipped away in the normal case, and the alternative is not a more
    /// faithful document but no document at all. Geometrically correct clipping belongs to the
    /// compiler layer, not to a number formatter.
    ///
    /// Non-finite values are tested for **before** clamping, and go to `Num.upTo`'s zero rather than
    /// to the boundary. `infinity > MaxDim` is true, so clamping first would send an infinity to
    /// `16000bp` while the SVG backend writes `0` for it — and the two backends agreeing on
    /// degenerate input is what lets a cross-backend test compare their coordinates at all.
    let dim (v: float) : string =
        if Double.IsNaN v || Double.IsInfinity v then
            num v + "bp"
        else
            let clamped =
                if v > MaxDim then MaxDim
                elif v < -MaxDim then -MaxDim
                else v

            num clamped + "bp"

    /// A TikZ coordinate pair, `(x,y)`, both in `bp`.
    let coord (x: float) (y: float) : string =
        String.Concat [| "("; dim x; ","; dim y; ")" |]

    // ---- text ------------------------------------------------------------------------

    /// Escape literal text so TeX typesets it as written. `Tex.escape`, named for this backend.
    let escapeText (s: string) : string = Tex.escape s

    /// What to typeset for a text run: its TeX source if it has any, otherwise its escaped plain
    /// text.
    ///
    /// D32's whole point, in one function. An empty `Tex` is treated as absent rather than as an
    /// instruction to typeset nothing, which matches `Scene.prune`'s rule that a run is empty
    /// only when *both* forms are.
    let runContent (tex: string option) (text: string) : string =
        match tex with
        | Some t when not (String.IsNullOrEmpty t) -> t
        | _ -> escapeText text

    /// A comment line.
    ///
    /// Newlines are stripped rather than escaped, and this is a correctness rule rather than
    /// tidiness: a TeX comment runs to the end of the line, so a newline inside one would end the
    /// comment and hand whatever followed to the typesetter as code. Since comments carry
    /// `Keyed` names and `Annotated` labels — both caller-supplied — that is an injection path,
    /// not a hypothetical.
    let comment (indent: string) (text: string) : string =
        let body =
            if isNull text then
                ""
            else
                let sb = StringBuilder(text.Length)

                for c in text do
                    // Anything that could end the line, plus the C0 range, becomes a space.
                    sb.Append(if c = '\n' || c = '\r' || c < ' ' then ' ' else c) |> ignore

                sb.ToString().Trim()

        String.Concat [| indent; "% "; body; "\n" |]

    // ---- colour ----------------------------------------------------------------------

    /// An `xcolor` rgb triple. Components are clamped to [0,1]: xcolor rejects anything outside
    /// it outright, and a colour arriving out of gamut from an Oklab conversion should degrade
    /// to the nearest representable colour rather than break the build.
    let rgbTriple (c: Color) : string =
        let clamp (v: float) =
            if Double.IsNaN v then 0.0
            elif v < 0.0 then 0.0
            elif v > 1.0 then 1.0
            else v

        String.Concat
            [| Num.upTo ColorDecimals (clamp c.R)
               ","
               Num.upTo ColorDecimals (clamp c.G)
               ","
               Num.upTo ColorDecimals (clamp c.B) |]

    /// A `\definecolor` line. Alpha is not part of it — TikZ carries opacity as a separate
    /// `opacity`/`fill opacity`/`draw opacity` key, exactly as SVG carries `fill-opacity`.
    let colorDefinition (indent: string) (name: string) (c: Color) : string =
        String.Concat [| indent; "\\definecolor{"; name; "}{rgb}{"; rgbTriple c; "}\n" |]

    // ---- identifiers -----------------------------------------------------------------

    /// A letters-only identifier for the *n*th generated macro: `A`, `B`, … `Z`, `AA`, `AB`, …
    ///
    /// **TeX command names may contain only letters** under standard category codes, so
    /// `\mareymark1` is not a command — it is `\mareymark` followed by a `1`, which either
    /// prints a stray digit or fails with a baffling error. Colour names are different: they sit
    /// inside braces as an argument, so digits are fine there and `qc1` is used instead.
    ///
    /// Bijective base 26, so the sequence never repeats and never produces an empty name.
    let letterId (n: int) : string =
        let mutable n = max 0 n
        let sb = StringBuilder(4)
        let mutable more = true

        while more do
            sb.Insert(0, char (int 'A' + n % 26)) |> ignore
            n <- n / 26 - 1
            more <- n >= 0

        sb.ToString()

    /// Sanitise a caller-supplied string into something safe to append to a macro name: letters
    /// only, with everything else dropped.
    ///
    /// Used to make a `Keyed` name legible in a generated macro. Returns the empty string when
    /// nothing survives, in which case the caller falls back to `letterId` alone.
    let letterPart (s: string) : string =
        if isNull s then
            ""
        else
            let sb = StringBuilder(s.Length)

            for c in s do
                if (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') then
                    sb.Append c |> ignore

            sb.ToString()

// ======================================================================================
// DOCUMENT ASSEMBLY
//
// The same shape as `SvgDocument`, and for the same reason: `IBackend.Begin` runs before any
// `Draw`, so the backend cannot know at header time which colours and marker macros the scene
// will need. The body is buffered, definitions are appended to a separate ordered list as they
// are discovered, and the preamble is written between the header and the body at compose time.
//
// Keeping the two backends structurally identical is deliberate — they then fail in the same
// ways, and a reviewer who knows one can read the other.
// ======================================================================================

/// Buffers the body of a `tikzpicture`, collects the definitions discovered during traversal,
/// and hands out unique identifiers.
[<Sealed>]
type internal TikzDocument() =

    /// Placeholder substituted into a definition when computing its deduplication key. NUL can
    /// never appear in a rendered definition, so a key can never be confused with one.
    let idProbe = String([| char 0 |])

    let body = StringBuilder()
    let defs = ResizeArray<string>()
    let defIds = Dictionary<string, string>(StringComparer.Ordinal)
    let usedIds = HashSet<string>(StringComparer.Ordinal)
    let mutable header = ""
    let mutable footer = ""
    let mutable nextId = 0

    /// Sidecar files this picture refers to but does not contain (D64). Assigned in traversal
    /// order, which is what makes `RenderedDocument.Assets` deterministic in order as well as
    /// in name — a render that produced `marey-raster-0.png` and `marey-raster-1.png` in
    /// swapped order would be a golden diff for no reason.
    let assets = ResizeArray<RenderedAsset>()

    member _.Body = body

    member _.Header
        with get () = header
        and set (v: string) = header <- v

    member _.Footer
        with get () = footer
        and set (v: string) = footer <- v

    member _.Reset() =
        body.Clear() |> ignore
        defs.Clear()
        defIds.Clear()
        usedIds.Clear()
        assets.Clear()
        header <- ""
        footer <- ""
        nextId <- 0

    /// Reserve an identifier, deriving a unique one if the stem is taken.
    ///
    /// Collisions resolve by appending letters rather than digits, because these identifiers end
    /// up in TeX command names where digits are not letters (see `Tikz.letterId`).
    member _.UniqueId(stem: string) : string =
        let mutable candidate = stem
        let mutable n = 0

        while usedIds.Contains candidate do
            candidate <- stem + Tikz.letterId n
            n <- n + 1

        usedIds.Add candidate |> ignore
        candidate

    /// A fresh letters-only identifier, independent of any caller-supplied name.
    member this.FreshId(prefix: string) : string =
        let id = this.UniqueId(prefix + Tikz.letterId nextId)
        nextId <- nextId + 1
        id

    /// Add a definition, reusing an identical one if it has already been emitted.
    ///
    /// `render` is called with the identifier to use; deduplication compares the definition
    /// rendered with a placeholder identifier, so identical definitions collapse and the key can
    /// never drift out of step with the content.
    member this.Def(prefix: string, render: string -> string) : string =
        let key = render idProbe

        match defIds.TryGetValue key with
        | true, existing -> existing
        | _ ->
            let id = this.FreshId prefix
            defIds.[key] <- id
            defs.Add(render id)
            id

    /// Register a sidecar file and return the name the document should refer to it by.
    ///
    /// The name is derived from the position in traversal order and nothing else: no hash, no
    /// GUID, no source-derived string. `\includegraphics` resolves it relative to the `.tex`,
    /// so it is a bare file name and the caller chooses the directory.
    member _.AddAsset(extension: string, bytes: byte[]) : string =
        let name = "marey-raster-" + string assets.Count + extension
        assets.Add { Name = name; Bytes = bytes }
        name

    /// The sidecar files registered so far, in traversal order.
    member _.Assets: RenderedAsset[] = assets.ToArray()

    /// Compose the finished picture: header, then definitions, then body, then footer.
    member _.Compose() : string =
        let sb = StringBuilder(header.Length + body.Length + footer.Length + 64)
        sb.Append header |> ignore

        for d in defs do
            sb.Append d |> ignore

        sb.Append body |> ignore
        sb.Append footer |> ignore
        sb.ToString()
