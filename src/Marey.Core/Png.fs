namespace Marey

open System

// ======================================================================================
// PNG ENCODING, BY HAND
//
// The `Raster` node has to become bytes: `<image href="data:image/png;base64,...">` in SVG,
// and a sidecar `.png` beside a `.tex` for TikZ's `\includegraphics`. Neither path may take a
// dependency: Core takes none, and the SVG backend's selling point is that it is pure F#, works
// under Fable and works in notebooks.
//
// So the encoder is written out. It is genuinely short because it uses **stored** deflate
// blocks: the DEFLATE format allows an uncompressed block type, and zlib is just a two-byte
// header plus deflate plus an Adler-32 checksum. The result is a completely valid PNG that any
// decoder reads, at the cost of being slightly *larger* than the raw pixels (five bytes of
// block header per 65 535 bytes of data, plus one filter byte per row).
//
// That trade is deliberate and it is bounded: correctness and zero dependencies now, a real
// Huffman encoder later if raster output ever becomes a size-sensitive path.
//
// It lives here rather than in the SVG backend, where it was first written on the reasoning that
// the Skia backend writes PNGs through libpng anyway. That reasoning holds for Skia and fails for
// TikZ's sidecar, which must not acquire a native dependency to write a `.png` next to a `.tex`.
// Second consumer, so the promote-on-the-second-consumer rule applies.
//
// The promotion breaks nothing and needed no alias, because `Png` was a *sibling* of `Svg` in
// `namespace Marey` rather than nested inside it. Its fully-qualified name was `Marey.Png`
// before the move and is `Marey.Png` after it; the SVG backend's `Png.dataUri` call resolves to
// this file with no edit, and so does any caller downstream.
// ======================================================================================

/// A minimal, dependency-free PNG encoder for RGBA8 images.
[<RequireQualifiedAccess>]
module internal Png =

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
