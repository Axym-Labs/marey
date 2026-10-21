namespace Marey

open System
open System.Text

// ======================================================================================
// THE WIRE
//
// A scene as bytes, for a page that draws what a kernel compiled. The IR has no closures so that it
// can be written down, and this is the file that writes it.
//
// **decode (encode n) = n**, field for field, for every scene. That is the property the format is
// built around and the reason it is float64 throughout: rounding the goldens' coordinates through
// float32 moves 11 of the 28 SVG baselines by a thousandth of a px, and the moment a decoded scene
// is not the scene, every golden stops being a test of the wire.
//
// The layout, little-endian throughout:
//
//   header   "MRYS", u16 version, u16 body (0 a scene, 1 a patch)
//            LEB128 count of header blocks, each: u8 id, LEB128 length, bytes
//   node     u8 tag (0–9, the `Node` cases in declaration order); with its top bit set, a block
//            follows the tag: LEB128 length, bytes. Then the case's fields.
//
// **Blocks are the room the format leaves itself.** A decoder skips a block whose id it does
// not know, so a later version can add a content hash per subtree or a table of text metrics
// without a reader that predates it failing on the scene. A version it does not know it refuses
// by name rather than misreads.
//
// **Float arrays are aligned to 8 bytes from the start of the buffer** (`MarkerBatch.Points`,
// `Stroke.Dash`): a page can then read them through a `Float64Array` view, four times faster under
// Fable than one `BitConverter` call a number. Padding is zero bytes, and a decoder computes
// it from the same absolute position the encoder did, so it never has to be written down.
//
// Everything else is a count (LEB128), a string (LEB128 byte count, UTF-8), a byte for a case or a
// flag, or a float64. No reflection and no hashing of union cases: each case is written out, so
// reordering a union is a compile-time change here rather than a silent change on the wire.
// ======================================================================================

/// What a decoder found wrong with a buffer. Internal: `Wire.decode` turns it into an `Error`.
exception internal WireError of string

[<Sealed>]
type internal WireWriter() =
    let mutable buffer: byte[] = Array.zeroCreate 256
    let mutable length = 0

    member private _.Room(n: int) =
        if length + n > buffer.Length then
            let next: byte[] = Array.zeroCreate (max (buffer.Length * 2) (length + n))
            Array.blit buffer 0 next 0 length
            buffer <- next

    member this.Byte(b: byte) =
        this.Room 1
        buffer.[length] <- b
        length <- length + 1

    member this.Bytes(bs: byte[]) =
        this.Room bs.Length
        Array.blit bs 0 buffer length bs.Length
        length <- length + bs.Length

    member this.U16(v: int) =
        this.Byte(byte (v &&& 0xFF))
        this.Byte(byte ((v >>> 8) &&& 0xFF))

    /// LEB128, for a count or a length. Never negative.
    member this.Count(v: int) =
        let mutable x = uint32 v

        while x >= 0x80u do
            this.Byte(byte (x &&& 0x7Fu) ||| 0x80uy)
            x <- x >>> 7

        this.Byte(byte x)

    /// A signed 32-bit integer, for the one field that may be negative (`FontSpec.Weight`).
    member this.I32(v: int) =
        this.Byte(byte (v &&& 0xFF))
        this.Byte(byte ((v >>> 8) &&& 0xFF))
        this.Byte(byte ((v >>> 16) &&& 0xFF))
        this.Byte(byte ((v >>> 24) &&& 0xFF))

    member this.F64(v: float) =
        this.Room 8
#if FABLE_COMPILER
        let bs = BitConverter.GetBytes v
        Array.blit bs 0 buffer length 8
#else
        Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(Span<byte>(buffer, length, 8), BitConverter.DoubleToInt64Bits v)
#endif
        length <- length + 8

    /// Zero bytes up to the next multiple of 8 from the start of the buffer.
    member this.Align() =
        while length % 8 <> 0 do
            this.Byte 0uy

    member this.Str(s: string) =
        let bs = Encoding.UTF8.GetBytes s
        this.Count bs.Length
        this.Bytes bs

    member _.ToArray() : byte[] = Array.sub buffer 0 length

[<Sealed>]
type internal WireReader(buffer: byte[]) =
    let mutable pos = 0

    member _.Position = pos

    member _.Need(n: int) (what: string) =
        if n < 0 || pos + n > buffer.Length then
            raise (WireError(sprintf "the bytes end at %d, inside %s" buffer.Length what))

    member this.Byte(what: string) : byte =
        this.Need 1 what
        let b = buffer.[pos]
        pos <- pos + 1
        b

    member this.Bytes (n: int) (what: string) : byte[] =
        this.Need n what
        let bs = Array.sub buffer pos n
        pos <- pos + n
        bs

    member this.U16(what: string) : int =
        let lo = int (this.Byte what)
        let hi = int (this.Byte what)
        lo ||| (hi <<< 8)

    member this.Count(what: string) : int =
        let mutable result = 0
        let mutable shift = 0
        let mutable go = true

        while go do
            if shift > 28 then
                raise (WireError(sprintf "a count at %d is longer than 32 bits, inside %s" pos what))

            let b = this.Byte what
            result <- result ||| (int (b &&& 0x7Fuy) <<< shift)
            shift <- shift + 7
            go <- b &&& 0x80uy <> 0uy

        if result < 0 then
            raise (WireError(sprintf "a count of %d, inside %s" result what))

        result

    member this.I32(what: string) : int =
        let b0 = int (this.Byte what)
        let b1 = int (this.Byte what)
        let b2 = int (this.Byte what)
        let b3 = int (this.Byte what)
        b0 ||| (b1 <<< 8) ||| (b2 <<< 16) ||| (b3 <<< 24)

    member this.F64(what: string) : float =
        this.Need 8 what
#if FABLE_COMPILER
        let v = BitConverter.ToDouble(buffer, pos)
#else
        let v = BitConverter.Int64BitsToDouble(Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(ReadOnlySpan<byte>(buffer, pos, 8)))
#endif
        pos <- pos + 8
        v

    member this.Align(what: string) =
        let pad = (8 - pos % 8) % 8
        this.Need pad what
        pos <- pos + pad

    member this.Str(what: string) : string =
        let n = this.Count what
        this.Need n what
        let s = Encoding.UTF8.GetString(buffer, pos, n)
        pos <- pos + n
        s

    member this.AtEnd = pos = buffer.Length

/// **The text metrics a scene's layout used**, carried with it.
///
/// A canvas anchors every run with its measurer's advance, ascent and descent, and that measurer
/// must be the one layout ran against. Layout runs in the kernel; the page cannot run Skia.
/// So the scene carries, once per distinct font and string, what the kernel measured, and the page
/// draws through this table. A run the table does not hold is measured by `TextMeasure.approx`.
[<Sealed>]
type TextTable(entries: (FontSpec * string * TextMetrics)[]) =
    static let keyOf (font: FontSpec) (text: string) =
        String.concat "\u0000" [ font.Family; Num.shortest (float font.Size); string font.Weight; (if font.Italic then "i" else "n"); text ]

    let index =
        let d = Collections.Generic.Dictionary<string, TextMetrics>()

        for (font, text, m) in entries do
            d.[keyOf font text] <- m

        d

    /// Every entry, in the order the scene first used it.
    member _.Entries = entries

    /// This table with `other`'s entries over it: what a page holds after applying a patch.
    member _.Merge(other: TextTable) : TextTable =
        let replaced = other.Entries |> Array.map (fun (f, t, _) -> keyOf f t) |> Set.ofArray
        TextTable(Array.append (entries |> Array.filter (fun (f, t, _) -> not (replaced.Contains(keyOf f t)))) other.Entries)

    static member Empty = TextTable([||])

    interface ITextMeasurer with
        member _.Measure (font: FontSpec) (text: string) =
            match index.TryGetValue(keyOf font text) with
            | true, m -> m
            | _ -> TextMeasure.approx.Measure font text

[<RequireQualifiedAccess>]
module Wire =

    /// The format version this build writes, and the newest it reads.
    [<Literal>]
    let Version = 1

    let private magic = [| 0x4Duy; 0x52uy; 0x59uy; 0x53uy |] // "MRYS"

    [<Literal>]
    let private BodyScene = 0

    [<Literal>]
    let private BodyPatch = 1

    let private nd (x: float) : float<nd> = LanguagePrimitives.FloatWithMeasure x
    let private px (x: float) : float<px> = LanguagePrimitives.FloatWithMeasure x

    // ---- Writing ------------------------------------------------------------------------------

    let private color (w: WireWriter) (c: Color) =
        w.F64 c.R
        w.F64 c.G
        w.F64 c.B
        w.F64 c.A

    let private vec (w: WireWriter) (v: Vec2<nd>) =
        w.F64(float v.X)
        w.F64(float v.Y)

    let private stops (w: WireWriter) (ss: GradientStop[]) =
        w.Count ss.Length

        for s in ss do
            w.F64 s.Offset
            color w s.Color

    let private paint (w: WireWriter) (p: Paint) =
        match p with
        | Solid c ->
            w.Byte 0uy
            color w c
        | LinearGradient(from, until, ss) ->
            w.Byte 1uy
            vec w from
            vec w until
            stops w ss
        | RadialGradient(centre, radius, ss) ->
            w.Byte 2uy
            vec w centre
            w.F64(float radius)
            stops w ss

    let private option (w: WireWriter) (write: WireWriter -> 'a -> unit) (o: 'a option) =
        match o with
        | None -> w.Byte 0uy
        | Some v ->
            w.Byte 1uy
            write w v

    let private floats (w: WireWriter) (xs: float<px>[]) =
        w.Count xs.Length
        w.Align()

        for x in xs do
            w.F64(float x)

    let private stroke (w: WireWriter) (s: Stroke) =
        paint w s.Paint
        w.F64(float s.Width)
        floats w s.Dash
        w.F64(float s.DashOffset)

        w.Byte(
            match s.Cap with
            | Butt -> 0uy
            | Round -> 1uy
            | Square -> 2uy
        )

        w.Byte(
            match s.Join with
            | Miter -> 0uy
            | RoundJoin -> 1uy
            | Bevel -> 2uy
        )

        w.F64 s.MiterLimit

    let private segments (w: WireWriter) (ss: PathSeg[]) =
        w.Count ss.Length

        for s in ss do
            match s with
            | MoveTo p ->
                w.Byte 0uy
                vec w p
            | LineTo p ->
                w.Byte 1uy
                vec w p
            | CubicTo(c1, c2, until) ->
                w.Byte 2uy
                vec w c1
                vec w c2
                vec w until
            | ClosePath -> w.Byte 3uy

    let private shapeByte (s: MarkerShape) =
        match s with
        | MarkCircle -> 0uy
        | MarkSquare -> 1uy
        | MarkDiamond -> 2uy
        | MarkTriangleUp -> 3uy
        | MarkTriangleDown -> 4uy
        | MarkCross -> 5uy
        | MarkPlus -> 6uy

    let private role (w: WireWriter) (r: SemanticRole) =
        match r with
        | RoleSeries -> w.Byte 0uy
        | RoleDatum -> w.Byte 1uy
        | RoleAxis -> w.Byte 2uy
        | RoleLegend -> w.Byte 3uy
        | RoleAnnotation -> w.Byte 4uy
        | RoleDecoration -> w.Byte 5uy
        | RoleOther s ->
            w.Byte 6uy
            w.Str s

    let rec private node (w: WireWriter) (n: Node) =
        match n with
        | Path(ss, st, fill, rule) ->
            w.Byte 0uy
            segments w ss
            option w stroke st
            option w paint fill
            w.Byte(if rule = NonZero then 0uy else 1uy)
        | Text t ->
            w.Byte 1uy
            w.Str t.Text
            option w (fun w s -> w.Str s) t.Tex
            vec w t.At
            w.Str t.Font.Family
            w.F64(float t.Font.Size)
            w.I32 t.Font.Weight
            w.Byte(if t.Font.Italic then 1uy else 0uy)
            paint w t.Paint

            w.Byte(
                match t.Anchor with
                | AnchorStart -> 0uy
                | AnchorMiddle -> 1uy
                | AnchorEnd -> 2uy
            )

            w.Byte(
                match t.Baseline with
                | BaselineAlphabetic -> 0uy
                | BaselineMiddle -> 1uy
                | BaselineHanging -> 2uy
            )

            w.F64(float t.Rotation)
        | Markers m ->
            w.Byte 2uy
            w.Count m.Points.Length
            w.Align()

            for p in m.Points do
                vec w p

            w.Byte(shapeByte m.Shape)
            w.F64(float m.Size)
            option w paint m.Fill
            option w stroke m.Stroke
        | Raster r ->
            w.Byte 3uy
            w.Count r.Image.Width
            w.Count r.Image.Height
            // How the pixels are stored: 0, the RGBA bytes as they are. Phase 7 adds
            // a compressed form under another value, which a reader of version 1 refuses by name.
            w.Byte 0uy
            w.Count r.Image.Rgba.Length
            w.Bytes r.Image.Rgba
            w.F64(float r.Bounds.X0)
            w.F64(float r.Bounds.Y0)
            w.F64(float r.Bounds.X1)
            w.F64(float r.Bounds.Y1)
            w.Byte(if r.Smooth then 1uy else 0uy)
        | Group children ->
            w.Byte 4uy
            w.Count children.Length

            for c in children do
                node w c
        | Transform(a, child) ->
            w.Byte 5uy
            w.F64(float a.M11)
            w.F64(float a.M12)
            w.F64(float a.M21)
            w.F64(float a.M22)
            w.F64(float a.Dx)
            w.F64(float a.Dy)
            node w child
        | Clip(ss, child) ->
            w.Byte 6uy
            segments w ss
            node w child
        | Opacity(alpha, child) ->
            w.Byte 7uy
            w.F64 alpha
            node w child
        | Keyed(Key k, child) ->
            w.Byte 8uy
            w.Str k
            node w child
        | Annotated(s, child) ->
            w.Byte 9uy
            role w s.Role
            w.Str s.Label
            w.Count s.Data.Length

            for (k, v) in s.Data do
                w.Str k
                w.Str v

            node w child

    [<Literal>]
    let private BlockMetrics = 1uy

    /// Every text run's font and string, once each, in the order a depth-first walk meets them.
    let private runs (roots: Node seq) : (FontSpec * string)[] =
        let seen = Collections.Generic.HashSet<string>()
        let found = ResizeArray<FontSpec * string>()

        let rec walk (n: Node) =
            match n with
            | Text t ->
                if seen.Add(String.concat "\u0000" [ t.Font.Family; Num.shortest (float t.Font.Size); string t.Font.Weight; string t.Font.Italic; t.Text ]) then
                    found.Add((t.Font, t.Text))
            | Group cs -> Array.iter walk cs
            | Transform(_, c)
            | Clip(_, c)
            | Opacity(_, c)
            | Keyed(_, c)
            | Annotated(_, c) -> walk c
            | Path _
            | Markers _
            | Raster _ -> ()

        Seq.iter walk roots
        found.ToArray()

    let private header (w: WireWriter) (body: int) (measured: (FontSpec * string * TextMetrics)[]) =
        w.Bytes magic
        w.U16 Version
        w.U16 body

        if measured.Length = 0 then
            w.Count 0
        else
            w.Count 1
            // The block is written into its own buffer first, for its length. It holds no float
            // array, so nothing in it is aligned, and nothing after it depends on its size.
            let b = WireWriter()
            b.Count measured.Length

            for (font, text, m) in measured do
                b.Str font.Family
                b.F64(float font.Size)
                b.I32 font.Weight
                b.Byte(if font.Italic then 1uy else 0uy)
                b.Str text
                b.F64(float m.Advance)
                b.F64(float m.Ascent)
                b.F64(float m.Descent)

            let bytes = b.ToArray()
            w.Byte BlockMetrics
            w.Count bytes.Length
            w.Bytes bytes

    let private measure (m: ITextMeasurer) (roots: Node seq) =
        runs roots |> Array.map (fun (font, text) -> font, text, m.Measure font text)

    let private sceneBytes (measured: (FontSpec * string * TextMetrics)[]) (n: Node) =
        let w = WireWriter()
        header w BodyScene measured
        node w n
        w.ToArray()

    let private patchBytes (measured: (FontSpec * string * TextMetrics)[]) (p: Patch) =
        let w = WireWriter()
        header w BodyPatch measured

        match p with
        | Replace parts ->
            w.Byte 0uy
            w.Count parts.Length

            for (Key k, n) in parts do
                w.Str k
                node w n
        | Whole n ->
            w.Byte 1uy
            node w n

        w.ToArray()

    let private patchRoots (p: Patch) =
        match p with
        | Replace parts -> parts |> Array.map snd
        | Whole n -> [| n |]

    /// A scene as bytes, with no text metrics: for a reader that lays nothing out against the
    /// kernel's measurer. The format's goldens are these.
    let encode (n: Node) : byte[] = sceneBytes [||] n

    /// **A scene as bytes, with the metrics `m` gives each of its text runs**: what a page
    /// that draws the scene needs, because its text was anchored against `m`.
    let encodeWith (m: ITextMeasurer) (n: Node) : byte[] = sceneBytes (measure m [ n ]) n

    /// A patch as bytes: `Replace`'s keys and nodes in order, or `Whole`'s scene.
    let encodePatch (p: Patch) : byte[] = patchBytes [||] p

    /// A patch as bytes, with the metrics of the runs its replacements carry.
    let encodePatchWith (m: ITextMeasurer) (p: Patch) : byte[] = patchBytes (measure m (patchRoots p)) p

    // ---- Reading ------------------------------------------------------------------------------

    let private readColor (r: WireReader) : Color =
        let red = r.F64 "a colour"
        let green = r.F64 "a colour"
        let blue = r.F64 "a colour"
        let alpha = r.F64 "a colour"
        { R = red; G = green; B = blue; A = alpha }

    let private readVec (r: WireReader) (what: string) : Vec2<nd> =
        let x = r.F64 what
        let y = r.F64 what
        { X = nd x; Y = nd y }

    let private readStops (r: WireReader) : GradientStop[] =
        let n = r.Count "a gradient"

        Array.init n (fun _ ->
            let offset = r.F64 "a gradient stop"
            { Offset = offset; Color = readColor r })

    let private readPaint (r: WireReader) : Paint =
        match r.Byte "a paint" with
        | 0uy -> Solid(readColor r)
        | 1uy ->
            let from = readVec r "a gradient"
            let until = readVec r "a gradient"
            LinearGradient(from, until, readStops r)
        | 2uy ->
            let centre = readVec r "a gradient"
            let radius = r.F64 "a gradient"
            RadialGradient(centre, nd radius, readStops r)
        | b -> raise (WireError(sprintf "a paint of kind %d at %d" b (r.Position - 1)))

    let private readOption (r: WireReader) (what: string) (read: WireReader -> 'a) : 'a option =
        match r.Byte what with
        | 0uy -> None
        | 1uy -> Some(read r)
        | b -> raise (WireError(sprintf "a presence byte of %d at %d, in %s" b (r.Position - 1) what))

    let private readFloats (r: WireReader) (what: string) : float<px>[] =
        let n = r.Count what
        r.Align what
        r.Need (8 * n) what
        Array.init n (fun _ -> px (r.F64 what))

    let private readStroke (r: WireReader) : Stroke =
        let p = readPaint r
        let width = r.F64 "a stroke"
        let dash = readFloats r "a stroke's dashes"
        let offset = r.F64 "a stroke"

        let cap =
            match r.Byte "a stroke" with
            | 0uy -> Butt
            | 1uy -> Round
            | 2uy -> Square
            | b -> raise (WireError(sprintf "a line cap of %d" b))

        let join =
            match r.Byte "a stroke" with
            | 0uy -> Miter
            | 1uy -> RoundJoin
            | 2uy -> Bevel
            | b -> raise (WireError(sprintf "a line join of %d" b))

        let limit = r.F64 "a stroke"

        { Paint = p
          Width = px width
          Dash = dash
          DashOffset = px offset
          Cap = cap
          Join = join
          MiterLimit = limit }

    let private readSegments (r: WireReader) : PathSeg[] =
        let n = r.Count "a path"

        Array.init n (fun _ ->
            match r.Byte "a path" with
            | 0uy -> MoveTo(readVec r "a path")
            | 1uy -> LineTo(readVec r "a path")
            | 2uy ->
                let c1 = readVec r "a path"
                let c2 = readVec r "a path"
                CubicTo(c1, c2, readVec r "a path")
            | 3uy -> ClosePath
            | b -> raise (WireError(sprintf "a path segment of kind %d at %d" b (r.Position - 1))))

    let private readShape (b: byte) =
        match b with
        | 0uy -> MarkCircle
        | 1uy -> MarkSquare
        | 2uy -> MarkDiamond
        | 3uy -> MarkTriangleUp
        | 4uy -> MarkTriangleDown
        | 5uy -> MarkCross
        | 6uy -> MarkPlus
        | b -> raise (WireError(sprintf "a marker shape of %d" b))

    let private readRole (r: WireReader) : SemanticRole =
        match r.Byte "an annotation" with
        | 0uy -> RoleSeries
        | 1uy -> RoleDatum
        | 2uy -> RoleAxis
        | 3uy -> RoleLegend
        | 4uy -> RoleAnnotation
        | 5uy -> RoleDecoration
        | 6uy -> RoleOther(r.Str "an annotation")
        | b -> raise (WireError(sprintf "a semantic role of %d" b))

    let rec private readNode (r: WireReader) : Node =
        let at = r.Position
        let tag = r.Byte "a node"

        // A block after the tag, which this version reads past.
        if tag &&& 0x80uy <> 0uy then
            let n = r.Count "a node's block"
            r.Bytes n "a node's block" |> ignore

        match tag &&& 0x7Fuy with
        | 0uy ->
            let ss = readSegments r
            let st = readOption r "a path" readStroke
            let fill = readOption r "a path" readPaint

            let rule =
                match r.Byte "a path" with
                | 0uy -> NonZero
                | 1uy -> EvenOdd
                | b -> raise (WireError(sprintf "a fill rule of %d" b))

            Path(ss, st, fill, rule)
        | 1uy ->
            let text = r.Str "a text run"
            let tex = readOption r "a text run" (fun r -> r.Str "a text run")
            let position = readVec r "a text run"
            let family = r.Str "a font"
            let size = r.F64 "a font"
            let weight = r.I32 "a font"
            let italic = r.Byte "a font" <> 0uy
            let p = readPaint r

            let anchor =
                match r.Byte "a text run" with
                | 0uy -> AnchorStart
                | 1uy -> AnchorMiddle
                | 2uy -> AnchorEnd
                | b -> raise (WireError(sprintf "a text anchor of %d" b))

            let baseline =
                match r.Byte "a text run" with
                | 0uy -> BaselineAlphabetic
                | 1uy -> BaselineMiddle
                | 2uy -> BaselineHanging
                | b -> raise (WireError(sprintf "a text baseline of %d" b))

            let rotation = r.F64 "a text run"

            Text
                { Text = text
                  Tex = tex
                  At = position
                  Font =
                    { Family = family
                      Size = LanguagePrimitives.FloatWithMeasure size
                      Weight = weight
                      Italic = italic }
                  Paint = p
                  Anchor = anchor
                  Baseline = baseline
                  Rotation = LanguagePrimitives.FloatWithMeasure rotation }
        | 2uy ->
            let n = r.Count "a marker batch"
            r.Align "a marker batch"
            r.Need (16 * n) "a marker batch"
            let points = Array.init n (fun _ -> readVec r "a marker batch")
            let shape = readShape (r.Byte "a marker batch")
            let size = r.F64 "a marker batch"
            let fill = readOption r "a marker batch" readPaint
            let st = readOption r "a marker batch" readStroke

            Markers
                { Points = points
                  Shape = shape
                  Size = px size
                  Fill = fill
                  Stroke = st }
        | 3uy ->
            let width = r.Count "a raster"
            let height = r.Count "a raster"

            match r.Byte "a raster" with
            | 0uy -> ()
            | b -> raise (WireError(sprintf "a raster stored in form %d, which format version %d does not read" b Version))

            let n = r.Count "a raster"
            let rgba = r.Bytes n "a raster's pixels"
            let x0 = r.F64 "a raster"
            let y0 = r.F64 "a raster"
            let x1 = r.F64 "a raster"
            let y1 = r.F64 "a raster"
            let smooth = r.Byte "a raster" <> 0uy

            Raster
                { Image =
                    { Width = width
                      Height = height
                      Rgba = rgba }
                  Bounds =
                    { X0 = nd x0
                      Y0 = nd y0
                      X1 = nd x1
                      Y1 = nd y1 }
                  Smooth = smooth }
        | 4uy ->
            let n = r.Count "a group"
            Group(Array.init n (fun _ -> readNode r))
        | 5uy ->
            let m11 = r.F64 "a transform"
            let m12 = r.F64 "a transform"
            let m21 = r.F64 "a transform"
            let m22 = r.F64 "a transform"
            let dx = r.F64 "a transform"
            let dy = r.F64 "a transform"

            let a: Affine<nd, nd> =
                { M11 = LanguagePrimitives.FloatWithMeasure m11
                  M12 = LanguagePrimitives.FloatWithMeasure m12
                  M21 = LanguagePrimitives.FloatWithMeasure m21
                  M22 = LanguagePrimitives.FloatWithMeasure m22
                  Dx = nd dx
                  Dy = nd dy }

            Transform(a, readNode r)
        | 6uy ->
            let ss = readSegments r
            Clip(ss, readNode r)
        | 7uy ->
            let alpha = r.F64 "an opacity"
            Opacity(alpha, readNode r)
        | 8uy ->
            let k = r.Str "a key"
            Keyed(Key k, readNode r)
        | 9uy ->
            let role = readRole r
            let label = r.Str "an annotation"
            let n = r.Count "an annotation"

            let data =
                Array.init n (fun _ ->
                    let k = r.Str "an annotation"
                    k, r.Str "an annotation")

            Annotated({ Role = role; Label = label; Data = data }, readNode r)
        | t -> raise (WireError(sprintf "a node of kind %d at %d" t at))

    let private readMetrics (r: WireReader) : TextTable =
        let n = r.Count "the text metrics"

        TextTable(
            Array.init n (fun _ ->
                let family = r.Str "the text metrics"
                let size = r.F64 "the text metrics"
                let weight = r.I32 "the text metrics"
                let italic = r.Byte "the text metrics" <> 0uy
                let text = r.Str "the text metrics"
                let advance = r.F64 "the text metrics"
                let ascent = r.F64 "the text metrics"
                let descent = r.F64 "the text metrics"

                let font: FontSpec =
                    { Family = family
                      Size = LanguagePrimitives.FloatWithMeasure size
                      Weight = weight
                      Italic = italic }

                font, text, { Advance = px advance; Ascent = px ascent; Descent = px descent })
        )

    /// Reads the header: the body's kind, and the text metrics if it carries them.
    let private readHeader (r: WireReader) : int * TextTable =
        let m = r.Bytes 4 "the header"

        if m <> magic then
            raise (WireError "not a Marey scene: the bytes do not begin with MRYS")

        let version = r.U16 "the header"

        if version < 1 || version > Version then
            raise (WireError(sprintf "a Marey scene of format version %d; this build reads 1 to %d" version Version))

        let body = r.U16 "the header"
        let blocks = r.Count "the header"
        let mutable table = TextTable.Empty

        for _ in 1..blocks do
            let id = r.Byte "a header block"
            let n = r.Count "a header block"

            if id = BlockMetrics then
                let start = r.Position
                table <- readMetrics r

                if r.Position <> start + n then
                    raise (WireError(sprintf "the text metrics block says %d bytes and holds %d" n (r.Position - start)))
            else
                // A header block this version does not know is read past.
                r.Bytes n "a header block" |> ignore

        body, table

    let private run (bytes: byte[]) (expect: int) (body: WireReader -> 'a) : Result<'a * TextTable, string> =
        if isNull bytes then
            Error "no bytes"
        else
            try
                let r = WireReader bytes
                let kind, table = readHeader r

                if kind <> expect then
                    Error(
                        sprintf
                            "a Marey %s where a %s was expected"
                            (if kind = BodyPatch then "patch" elif kind = BodyScene then "scene" else sprintf "body of kind %d" kind)
                            (if expect = BodyPatch then "patch" else "scene")
                    )
                else
                    let v = body r

                    if r.AtEnd then
                        Ok(v, table)
                    else
                        Error(sprintf "%d bytes after the end of the %s" (bytes.Length - r.Position) (if expect = BodyPatch then "patch" else "scene"))
            with
            | WireError why -> Error why

    /// **A scene back from its bytes, and the text metrics it carries** (`TextTable.Empty` when it
    /// carries none). `Error` says what is wrong: not a Marey scene, a format version this build
    /// does not read (naming both), or where the bytes ended early.
    let decodeWith (bytes: byte[]) : Result<Node * TextTable, string> = run bytes BodyScene readNode

    let private readPatch (r: WireReader) : Patch =
        match r.Byte "a patch" with
        | 0uy ->
            let n = r.Count "a patch"

            Replace(
                Array.init n (fun _ ->
                    let k = r.Str "a patch"
                    Key k, readNode r)
            )
        | 1uy -> Whole(readNode r)
        | b -> raise (WireError(sprintf "a patch of kind %d" b))

    /// A patch back from `encodePatchWith`'s bytes, and the text metrics it carries.
    let decodePatchWith (bytes: byte[]) : Result<Patch * TextTable, string> = run bytes BodyPatch readPatch

    /// A scene back from its bytes, its text metrics, if any, set aside.
    let decode (bytes: byte[]) : Result<Node, string> = decodeWith bytes |> Result.map fst

    /// A patch back from `encodePatch`'s bytes.
    let decodePatch (bytes: byte[]) : Result<Patch, string> = decodePatchWith bytes |> Result.map fst
