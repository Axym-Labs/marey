namespace FSharp

namespace Marey

    [<RequireQualifiedAccess>]
    module Tikz =

        [<Literal>]
        val CoordDecimals: int = 3

        [<Literal>]
        val RatioDecimals: int = 4

        [<Literal>]
        val ColorDecimals: int = 6

        val num: v: float -> string

        val ratio: v: float -> string

        [<Literal>]
        val MaxDim: float = 16000

        val dim: v: float -> string

        val coord: x: float -> y: float -> string

        val escapeText: s: string -> string

        val runContent: tex: string option -> text: string -> string

        val comment: indent: string -> text: string -> string

        val rgbTriple: c: Color -> string

        val colorDefinition:
          indent: string -> name: string -> c: Color -> string

        val letterId: n: int -> string

        val letterPart: s: string -> string

namespace Marey

    type TikzFontMode =

        | PinSize

        | InheritFont

    [<NoComparison>]
    type TikzOptions =
        {

          Dpi: float<px/mm>
          FontMode: TikzFontMode

          Comments: bool
        }

        static member Default: TikzOptions

    [<Sealed>]
    type TikzBackend =
        interface IBackend

        new: ?options: TikzOptions -> TikzBackend

        member Assets: RenderedAsset array

        member Rendered: RenderedDocument

        member Result: string

        member RootTransform: Affine<nd,pt>

    [<CompilationRepresentation (enum<CompilationRepresentationFlags> (4));
      RequireQualifiedAccess>]
    module TikzBackend =

        [<Literal>]
        val Preamble: string
          =
          "\usepackage{tikz}
\usepackage{graphicx}
\usepackage{textcomp}
\usepackage{siunitx}
"

        val render: extent: Extent<px> -> node: Node -> string

        val renderWith:
          options: TikzOptions -> extent: Extent<px> -> node: Node -> string

        val renderWithAssets:
          extent: Extent<px> -> node: Node -> RenderedDocument

        val renderWithAssetsWith:
          options: TikzOptions ->
            extent: Extent<px> -> node: Node -> RenderedDocument

        val standalone: picture: string -> string

        val renderStandalone: extent: Extent<px> -> node: Node -> string

        val renderToFile:
          path: string -> extent: Extent<px> -> node: Node -> unit

        val renderToFileWithAssets:
          path: string -> extent: Extent<px> -> node: Node -> string array

namespace Marey.CSharp

    [<AbstractClass; Sealed; Class>]
    type TikzRenderer =

        static member
          Render: extent: Marey.Extent<Marey.px> * node: Marey.Node -> string
