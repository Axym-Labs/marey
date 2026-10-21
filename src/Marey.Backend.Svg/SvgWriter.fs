namespace Marey

open System
open System.Collections.Generic
open System.Text

// ======================================================================================
// THE SVG WRITER: the primitives the reference backend is built out of.
//
// The SVG backend is the spec, and the tests are "golden-image snapshots against the SVG backend
// (text-diffable, reviewable in a PR)". Both sentences point at the same requirement, and it is the
// one this file exists to satisfy: **the same Scene must produce byte-identical output on every
// machine, every run.**
//
// That rules out more than it looks like:
//
//   * every number goes through InvariantCulture (a German locale emits "0,5" otherwise, and
//     "0,5" is not a number SVG can parse — this is a correctness bug, not a cosmetic one);
//   * no format that can produce scientific notation. `%g` and "R" both switch to `1e-05`
//     below 1e-4, and the SVG number grammar has no exponent in path data. Every number here
//     goes through a *custom* .NET numeric format, which never uses an exponent;
//   * coordinates are rounded to a fixed number of decimals before formatting, so that float
//     noise in the last few bits of a matrix multiply cannot churn the diff;
//   * negative zero is normalised to zero (.NET Core 3.0+ formats -0.0 as "-0", and whether a
//     coordinate lands on -0.0 or 0.0 depends on the sign of a multiply that is otherwise
//     invisible);
//
// Those five rules now live in `Marey.Core`'s `Num` module, because the TikZ backend needs
// every one of them and the rule is to promote shared formatting on the appearance of a second
// consumer rather than to copy it. `numAt` below is a forwarder, kept because the golden suite
// asserts on the SVG spellings directly and because `CoordDecimals`/`RatioDecimals`/
// `MatrixDecimals` are SVG's own policy, not Core's.
//
//   * line endings are always "\n", never Environment.NewLine;
//   * nothing is ever emitted from an unordered iteration. Dictionaries in this file are only
//     ever *looked up*; the emission order always comes from a ResizeArray filled in traversal
//     order.
// ======================================================================================

/// Number formatting, XML escaping and identifier sanitisation for SVG output.
///
/// Everything the backend prints goes through this module. It is public because the golden
/// test suite needs to assert on the formatting rules directly, not only through rendered
/// documents.
[<RequireQualifiedAccess>]
module internal Svg =

    /// Decimal places for device-space coordinates. A thousandth of a pixel is far below what
    /// any renderer resolves, and it is coarse enough that a re-associated float multiply
    /// cannot move the digits.
    [<Literal>]
    let CoordDecimals = 3

    /// Decimal places for opacities and gradient stop offsets, which live in zero to one.
    [<Literal>]
    let RatioDecimals = 4

    /// Decimal places for transform-matrix entries. The linear part of the nd to px map is of
    /// order the figure size, so it wants more absolute precision than a coordinate does.
    [<Literal>]
    let MatrixDecimals = 6

    /// Format a number at a given number of decimal places. Non-finite values become zero:
    /// a singular transform must not be able to write "NaN" into a document that then fails
    /// to parse.
    ///
    /// A forwarder onto `Num.upTo` since the TikZ backend needed the same rules
    /// (promote on the second consumer). Kept as a member of this module because the golden
    /// suite asserts on the SVG spelling directly.
    let numAt (decimals: int) (v: float) : string = Num.upTo decimals v

    /// Format a device-space coordinate.
    let num (v: float) : string = numAt CoordDecimals v

    /// Format a ratio in zero to one, such as an opacity or a gradient stop offset.
    let ratio (v: float) : string = numAt RatioDecimals v

    // XML 1.0 forbids most C0 control characters outright. A document containing one is not
    // merely ugly, it is malformed, so they are dropped rather than escaped. Surrogate code
    // units are deliberately *allowed* through: a well-formed .NET string carries astral
    // characters as surrogate pairs, and those are legal XML.
    let private isXmlLegal (c: char) =
        c = '\t' || c = '\n' || c = '\r' || (c >= ' ' && int c < 0xFFFE)

    /// Escape a string for use as XML element content.
    let escapeText (s: string) : string =
        if isNull s then
            ""
        else
            let sb = StringBuilder(s.Length + 8)

            for c in s do
                match c with
                | '&' -> sb.Append "&amp;" |> ignore
                | '<' -> sb.Append "&lt;" |> ignore
                | '>' -> sb.Append "&gt;" |> ignore
                | c when isXmlLegal c -> sb.Append c |> ignore
                | _ -> ()

            sb.ToString()

    /// Escape a string for use inside a double-quoted XML attribute value.
    ///
    /// Tab, newline and carriage return are escaped numerically rather than passed through:
    /// an XML parser normalises literal whitespace inside an attribute value to a space, so a
    /// label containing a newline would silently change meaning on round-trip.
    let escapeAttr (s: string) : string =
        if isNull s then
            ""
        else
            let sb = StringBuilder(s.Length + 8)

            for c in s do
                match c with
                | '&' -> sb.Append "&amp;" |> ignore
                | '<' -> sb.Append "&lt;" |> ignore
                | '>' -> sb.Append "&gt;" |> ignore
                | '"' -> sb.Append "&quot;" |> ignore
                | '\'' -> sb.Append "&apos;" |> ignore
                | '\t' -> sb.Append "&#9;" |> ignore
                | '\n' -> sb.Append "&#10;" |> ignore
                | '\r' -> sb.Append "&#13;" |> ignore
                | c when isXmlLegal c -> sb.Append c |> ignore
                | _ -> ()

            sb.ToString()

    let private isAsciiLetter (c: char) =
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')

    let private isAsciiDigit (c: char) = c >= '0' && c <= '9'

    /// Sanitise the body of an XML name: ASCII letters, digits, underscore, hyphen and dot
    /// survive, everything else becomes an underscore.
    ///
    /// Deliberately ASCII-only. The XML NameChar production admits large swathes of Unicode,
    /// but the exact set differs between XML 1.0 fourth and fifth editions, and a golden file
    /// that is pure ASCII is one that no editor, terminal or diff tool can mangle.
    let namePart (s: string) : string =
        let src = if isNull s then "" else s
        let sb = StringBuilder(src.Length)

        for c in src do
            let ok =
                isAsciiLetter c || isAsciiDigit c || c = '_' || c = '-' || c = '.'

            sb.Append(if ok then c else '_') |> ignore

        sb.ToString()

    /// Sanitise an arbitrary string into a valid XML NCName, suitable for an id attribute.
    ///
    /// A name may not start with a digit, a hyphen or a dot, so a leading underscore is
    /// prepended rather than the offending character being replaced. That keeps the key
    /// readable in the output, which is the point of emitting it at all.
    let ncname (s: string) : string =
        let body = namePart s

        if body.Length = 0 then "_"
        else
            let first = body.[0]
            if isAsciiLetter first || first = '_' then body else "_" + body

    /// The SVG form of an affine map.
    ///
    /// **This is the one place the reordering happens, and it is a trap.** `Affine.toRaw` is
    /// documented as row-major `[M11; M12; M21; M22; Dx; Dy]`. SVG's `matrix(a b c d e f)` is
    /// column-major and means
    ///
    ///     x' = a*x + c*y + e
    ///     y' = b*x + d*y + f
    ///
    /// so a = M11, b = M21, c = M12, d = M22 — indices 0, 2, 1, 3, 4, 5 of the raw array.
    /// Transposing the linear part by accident is invisible for a pure scale (M12 = M21 = 0)
    /// and mirrors the figure the moment anything rotates or shears.
    let matrix (t: Affine<'a, 'b>) : string =
        let m = Affine.toRaw t
        let f (v: float) = numAt MatrixDecimals v

        String.Concat
            [| "matrix("; f m.[0]; " "; f m.[2]; " "; f m.[1]; " "; f m.[3]; " "; f m.[4]; " "; f m.[5]; ")" |]

    let private indents = Array.init 32 (fun i -> String(' ', i * 2))

    /// Two spaces per level of nesting.
    let indent (depth: int) : string =
        if depth < 0 then ""
        elif depth < indents.Length then indents.[depth]
        else String(' ', depth * 2)

// ======================================================================================
// DOCUMENT ASSEMBLY
//
// `IBackend.Begin` runs before any `Draw`, so the backend cannot know at header time which
// gradients, clip paths and marker symbols the scene will need. The fix is not to guess and
// not to patch the string afterwards: the body is buffered, definitions are appended to a
// separate ordered list as they are discovered, and `<defs>` is written between the header and
// the body when the document is finally composed.
//
// Identity allocation is shared between generated ids and user-supplied `Keyed` names, so the
// two can never collide however adversarial the key. Collisions resolve by appending "-2",
// "-3", ... in traversal order — deterministic, and no GUIDs.
// ======================================================================================

/// Buffers the body of an SVG document, collects `defs` discovered during traversal, and hands
/// out unique element ids.
///
/// `idPrefix` namespaces every id this document hands out — both the generated ones (`qc`, `qm`)
/// and the `Keyed` names. Empty by default, which is every ordinary render. It exists for the
/// flipbook: SVG ids are scoped to the *document*, not to a nested `<svg>` element, so
/// stacking N independently rendered frames inside one outer document makes frame 5's
/// `url(#qc)` resolve to frame 0's clip path. Prefixing here rather than at the call sites is
/// what makes that airtight — every id in this backend is allocated by `UniqueId`, including the
/// ones `Def` reserves, so there is exactly one place it can leak from.
[<Sealed>]
type internal SvgDocument(idPrefix: string) =

    /// Placeholder substituted into a definition when computing its deduplication key. NUL is
    /// a character `Svg.ncname` can never produce, so a key can never be confused with a
    /// rendered definition.
    let idProbe = System.String([| char 0 |])

    let body = StringBuilder()
    let defs = ResizeArray<string>()
    let defIds = Dictionary<string, string>()
    let usedIds = HashSet<string>()
    let mutable header = ""

    member _.Body = body

    member _.Header
        with get () = header
        and set (v: string) = header <- v

    member _.Reset() =
        body.Clear() |> ignore
        defs.Clear()
        defIds.Clear()
        usedIds.Clear()
        header <- ""

    /// Reserve an id, deriving a unique one if the stem is already taken.
    ///
    /// Uniqueness is tracked on the *prefixed* name, so two documents with different prefixes
    /// cannot collide and one document's own suffixing (`-2`, `-3`) is unaffected by the prefix.
    member _.UniqueId(stem: string) : string =
        let mutable candidate = idPrefix + stem
        let mutable n = 1

        while usedIds.Contains candidate do
            n <- n + 1
            candidate <- idPrefix + stem + "-" + string n

        usedIds.Add candidate |> ignore
        candidate

    /// Add a definition, reusing an identical one if it has already been emitted.
    ///
    /// `render` is called with the id to use; deduplication compares the definition rendered
    /// with a placeholder id, so identical definitions collapse and the key can never drift
    /// out of step with the content.
    member this.Def(idStem: string, render: string -> string) : string =
        let key = render idProbe

        match defIds.TryGetValue key with
        | true, existing -> existing
        | _ ->
            let id = this.UniqueId idStem
            defIds.[key] <- id
            defs.Add(render id)
            id

    /// The `<defs>` block as `Compose` writes it, or `""` when there are none.
    member _.DefsText() : string =
        if defs.Count = 0 then
            ""
        else
            let sb = StringBuilder()
            sb.Append("  <defs>\n") |> ignore

            for d in defs do
                sb.Append(d) |> ignore

            sb.Append("  </defs>\n").ToString()

    /// Compose the finished document: header, then defs, then body, then the closing tag.
    member _.Compose() : string =
        let sb = StringBuilder(header.Length + body.Length + 64)
        sb.Append(header) |> ignore

        if defs.Count > 0 then
            sb.Append("  <defs>\n") |> ignore

            for d in defs do
                sb.Append(d) |> ignore

            sb.Append("  </defs>\n") |> ignore

        sb.Append(body) |> ignore
        sb.Append("</svg>\n") |> ignore
        sb.ToString()
