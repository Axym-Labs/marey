namespace Quiver

open System
open System.Collections.Generic
open System.Text

// ======================================================================================
// §6 — THE SVG WRITER: the primitives the reference backend is built out of.
//
// §6 says of the SVG backend: "Build this first and treat it as the spec." §13 says the test
// strategy is "golden-image snapshots against the SVG backend (text-diffable, reviewable in a
// PR)". Both sentences point at the same requirement, and it is the one this file exists to
// satisfy: **the same Scene must produce byte-identical output on every machine, every run.**
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
// Those five rules now live in `Quiver.Core`'s `Num` module, because the TikZ backend needs
// every one of them and F2's rule is to promote shared formatting on the appearance of a second
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
module Svg =

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
    /// A forwarder onto `Num.at` since the TikZ backend needed the same rules (F2's
    /// promote-on-second-consumer rule). Kept as a member of this module because the golden
    /// suite asserts on the SVG spelling directly.
    let numAt (decimals: int) (v: float) : string = Num.at decimals v

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
// PNG ENCODING, BY HAND
//
// The `Raster` node has to become `<image href="data:image/png;base64,...">`, and this package
// has zero dependencies by design (§13 makes that non-negotiable for Core; §6 makes it a
// selling point for this backend — "Pure F#, works under Fable, works in notebooks").
//
// So the encoder is written out. It is genuinely short because it uses **stored** deflate
// blocks: the DEFLATE format allows an uncompressed block type, and zlib is just a two-byte
// header plus deflate plus an Adler-32 checksum. The result is a completely valid PNG that any
// decoder reads, at the cost of being slightly *larger* than the raw pixels (five bytes of
// block header per 65 535 bytes of data, plus one filter byte per row).
//
// That trade is deliberate and it is bounded: correctness and zero dependencies now, a real
// Huffman encoder later if raster output ever becomes a size-sensitive path. M2's Skia backend
// writes PNGs through libpng anyway, so this may well never need to grow.
// ======================================================================================

/// A minimal, dependency-free PNG encoder for RGBA8 images.
[<RequireQualifiedAccess>]
module Png =

    let private crcTable =
        let t = Array.zeroCreate<uint32> 256

        for n in 0..255 do
            let mutable c = uint32 n

            for _ in 0..7 do
                c <- if c &&& 1u <> 0u then 0xEDB88320u ^^^ (c >>> 1) else c >>> 1

            t.[n] <- c

        t

    let private crc32 (data: byte[]) =
        let mutable c = 0xFFFFFFFFu

        for i in 0 .. data.Length - 1 do
            c <- crcTable.[int ((c ^^^ uint32 data.[i]) &&& 0xFFu)] ^^^ (c >>> 8)

        c ^^^ 0xFFFFFFFFu

    /// Adler-32 over the *uncompressed* stream, as zlib requires. The largest prime below
    /// 65 536 is the modulus; taking it every byte is slower than the usual 5552-byte
    /// deferral and is not worth the extra code here.
    let private adler32 (data: byte[]) =
        let mutable a = 1u
        let mutable b = 0u

        for i in 0 .. data.Length - 1 do
            a <- (a + uint32 data.[i]) % 65521u
            b <- (b + a) % 65521u

        (b <<< 16) ||| a

    let private putBE32 (out: ResizeArray<byte>) (v: uint32) =
        out.Add(byte (v >>> 24))
        out.Add(byte (v >>> 16))
        out.Add(byte (v >>> 8))
        out.Add(byte v)

    let private chunk (out: ResizeArray<byte>) (tag: string) (data: byte[]) =
        putBE32 out (uint32 data.Length)
        // The CRC covers the type tag and the payload, but not the length field.
        let tagged = Array.zeroCreate<byte> (4 + data.Length)

        for i in 0..3 do
            tagged.[i] <- byte tag.[i]

        Array.blit data 0 tagged 4 data.Length
        out.AddRange tagged
        putBE32 out (crc32 tagged)

    /// zlib container around stored (uncompressed) DEFLATE blocks.
    let private zlibStored (raw: byte[]) =
        let out = ResizeArray<byte>(raw.Length + raw.Length / 65535 * 5 + 16)
        // CMF = 0x78: deflate, 32 KiB window. FLG = 0x01 makes 0x7801 divisible by 31, which
        // is what the header check requires, with FLEVEL = 0 (fastest) and no preset dict.
        out.Add 0x78uy
        out.Add 0x01uy

        let mutable offset = 0

        if raw.Length = 0 then
            // A zero-length stream still needs one final empty stored block.
            out.Add 0x01uy
            out.Add 0uy
            out.Add 0uy
            out.Add 0xFFuy
            out.Add 0xFFuy
        else
            while offset < raw.Length do
                let len = min 65535 (raw.Length - offset)
                let final = offset + len >= raw.Length
                out.Add(if final then 1uy else 0uy) // BFINAL bit, BTYPE = 00 (stored)
                out.Add(byte (len &&& 0xFF)) // LEN, little-endian
                out.Add(byte ((len >>> 8) &&& 0xFF))
                let nlen = (~~~len) &&& 0xFFFF
                out.Add(byte (nlen &&& 0xFF)) // NLEN = one's complement of LEN
                out.Add(byte ((nlen >>> 8) &&& 0xFF))

                for i in offset .. offset + len - 1 do
                    out.Add raw.[i]

                offset <- offset + len

        putBE32 out (adler32 raw)
        out.ToArray()

    /// Encode raw RGBA8 pixels, row-major with the top row first, as a PNG byte stream.
    ///
    /// A short pixel array is padded with transparent black rather than throwing: a backend
    /// that crashes the whole render because one heatmap was built wrong is worse than one
    /// that draws the heatmap wrong. The caller is expected to have checked.
    let encode (width: int) (height: int) (rgba: byte[]) : byte[] =
        let w = max 1 width
        let h = max 1 height
        let src = if isNull rgba then Array.empty else rgba
        let stride = w * 4
        // One filter-type byte per scanline; 0 means "no filtering", which is what makes the
        // stored-block trick work without a filter heuristic.
        let raw = Array.zeroCreate<byte> ((stride + 1) * h)

        for y in 0 .. h - 1 do
            let srcStart = y * stride
            let available = max 0 (min stride (src.Length - srcStart))

            if available > 0 then
                Array.blit src srcStart raw (y * (stride + 1) + 1) available

        let out = ResizeArray<byte>(raw.Length + 256)
        out.AddRange [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]

        let ihdr =
            [| byte (w >>> 24)
               byte (w >>> 16)
               byte (w >>> 8)
               byte w
               byte (h >>> 24)
               byte (h >>> 16)
               byte (h >>> 8)
               byte h
               8uy // bit depth
               6uy // colour type 6 = truecolour with alpha
               0uy // compression method: deflate
               0uy // filter method: adaptive, the only one defined
               0uy |] // interlace: none

        chunk out "IHDR" ihdr
        chunk out "IDAT" (zlibStored raw)
        chunk out "IEND" Array.empty
        out.ToArray()

    /// Encode an image as a `data:` URI ready to drop into an href attribute.
    let dataUri (img: Image) : string =
        "data:image/png;base64," + Convert.ToBase64String(encode img.Width img.Height img.Rgba)

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
[<Sealed>]
type internal SvgDocument() =

    /// Placeholder substituted into a definition when computing its deduplication key. NUL is
    /// a character `Svg.ncname` can never produce, so a key can never be confused with a
    /// rendered definition.
    let idProbe = System.String([| char 0 |])

    let body = StringBuilder()
    let defs = ResizeArray<string>()
    let defIds = Dictionary<string, string>(StringComparer.Ordinal)
    let usedIds = HashSet<string>(StringComparer.Ordinal)
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
    member _.UniqueId(stem: string) : string =
        let mutable candidate = stem
        let mutable n = 1

        while usedIds.Contains candidate do
            n <- n + 1
            candidate <- stem + "-" + string n

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
