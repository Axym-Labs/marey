namespace Marey

    [<RequireQualifiedAccess>]
    module SkiaNative =

        val tryDiagnose: e: exn -> string option

        val guard: body: (unit -> 'a) -> 'a

    type FallbackFace =
        {

          Family: string

          File: string option

          Covered: int array
        }

    type ResolvedFace =
        {

          Requested: string
          Weight: int
          Italic: bool

          Matched: string option

          Family: string

          File: string option

          Bundled: bool

          Fallbacks: FallbackFace array

          Missing: int array
        }

    [<Sealed>]
    type SkiaTextMeasurer =
        interface System.IDisposable
        interface ITextMeasurer

        new: ?fontFile: string * ?dpi: float<px/mm> * ?fontFiles: string list ->
               SkiaTextMeasurer

        member Face: spec: FontSpec -> ResolvedFace

        member MeasureUnshaped: spec: FontSpec * text: string -> float

        member Dpi: float<px/mm>

        member FontFile: string option

        member FontFiles: string list

        member ResolvedFaces: ResolvedFace array

namespace Marey

    [<RequireQualifiedAccess>]
    type SkiaFormat =

        | Png

        | Pdf

    type PdfMetadata =
        {
          Title: string
          Author: string
          Subject: string
          Keywords: string
          Creator: string
          Producer: string
        }

    [<RequireQualifiedAccess>]
    module PdfMetadata =

        val Default: PdfMetadata

    [<Struct>]
    type SkiaOptions =
        {

          Dpi: float<px/mm>

          Background: Color

          Antialias: bool

          FontFile: string option

          FontFiles: string list

          Pdf: PdfMetadata
        }

    [<RequireQualifiedAccess>]
    module SkiaOptions =

        val Default: SkiaOptions

    [<RequireQualifiedAccess>]
    module SkiaColor =

        val ofColor: c: Color -> SkiaSharp.SKColor

    [<RequireQualifiedAccess>]
    type PainterClip =

        | Unclipped

        | Rectangle of SkiaSharp.SKRect

        | Shaped

    [<Sealed>]
    type SkiaPainter =

        new: canvas: SkiaSharp.SKCanvas * shaping: SkiaTextMeasurer *
             antialias: bool * claim: (Affine<nd,px> -> Node -> bool) option ->
               SkiaPainter

        static member RootTransform: extent: Extent<px> -> Affine<nd,px>

        member Draw: ctm: Affine<nd,px> * node: Node -> unit

        member Canvas: SkiaSharp.SKCanvas

        member Clip: PainterClip

        member LayerDepth: int

    [<Sealed>]
    type SkiaBackend =
        interface System.IDisposable
        interface IBackend

        new: ?options: SkiaOptions * ?format: SkiaFormat -> SkiaBackend

        member ResolvedFaces: ResolvedFace array

        member Result: byte array

        member RootTransform: Affine<nd,px>

        member TextMeasurer: ITextMeasurer

    [<CompilationRepresentation (enum<CompilationRepresentationFlags> (4));
      RequireQualifiedAccess>]
    module SkiaBackend =

        val renderPng: extent: Extent<px> -> node: Node -> byte array

        val renderPngWith:
          options: SkiaOptions -> extent: Extent<px> -> node: Node -> byte array

        val renderPngToFile:
          path: string -> extent: Extent<px> -> node: Node -> unit

        val renderPdf: extent: Extent<px> -> node: Node -> byte array

        val renderPdfWith:
          options: SkiaOptions -> extent: Extent<px> -> node: Node -> byte array

        val renderPdfToFile:
          path: string -> extent: Extent<px> -> node: Node -> unit

namespace Marey.CSharp

    [<AbstractClass; Sealed; Class>]
    type PngRenderer =

        static member
          Render: extent: Marey.Extent<Marey.px> * node: Marey.Node ->
                    byte array

    [<AbstractClass; Sealed; Class>]
    type PdfRenderer =

        static member
          Render: extent: Marey.Extent<Marey.px> * node: Marey.Node ->
                    byte array
