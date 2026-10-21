namespace FSharp

namespace Marey

    [<AllowNullLiteral>]
    type CanvasGradient =

        abstract addColorStop: offset: float * color: string -> unit

    [<AllowNullLiteral>]
    type CanvasTextMetrics =

        abstract fontBoundingBoxAscent: float

        abstract fontBoundingBoxDescent: float

        abstract width: float

    [<AllowNullLiteral>]
    type CanvasImageData =

        abstract data: byte array

    [<AllowNullLiteral>]
    type CanvasElement =

        abstract getContext: contextId: string -> CanvasContext

        abstract height: float with get, set

        abstract width: float with get, set

    and [<AllowNullLiteral; Interface>] CanvasContext =

        abstract
          arc: x: float * y: float * radius: float * startAngle: float *
               endAngle: float -> unit

        abstract beginPath: unit -> unit

        abstract
          bezierCurveTo: c1x: float * c1y: float * c2x: float * c2y: float *
                         x: float * y: float -> unit

        abstract clearRect: x: float * y: float * w: float * h: float -> unit

        abstract clip: fillRule: string -> unit

        abstract closePath: unit -> unit

        abstract
          createImageData: width: float * height: float -> CanvasImageData

        abstract
          createLinearGradient: x0: float * y0: float * x1: float * y1: float ->
                                  CanvasGradient

        abstract
          createRadialGradient: x0: float * y0: float * r0: float * x1: float *
                                y1: float * r1: float -> CanvasGradient

        abstract
          drawImage: image: CanvasElement * x: float * y: float * w: float *
                     h: float -> unit

        abstract fill: fillRule: string -> unit

        abstract fillRect: x: float * y: float * w: float * h: float -> unit

        abstract fillText: text: string * x: float * y: float -> unit

        abstract lineTo: x: float * y: float -> unit

        abstract measureText: text: string -> CanvasTextMetrics

        abstract moveTo: x: float * y: float -> unit

        abstract
          putImageData: image: CanvasImageData * x: float * y: float -> unit

        abstract restore: unit -> unit

        abstract rotate: angle: float -> unit

        abstract save: unit -> unit

        abstract setLineDash: segments: float array -> unit

        abstract
          setTransform: a: float * b: float * c: float * d: float * e: float *
                        f: float -> unit

        abstract stroke: unit -> unit

        abstract
          transform: a: float * b: float * c: float * d: float * e: float *
                     f: float -> unit

        abstract translate: x: float * y: float -> unit

        abstract canvas: CanvasElement

        abstract fillStyle: obj with get, set

        abstract font: string with get, set

        abstract globalAlpha: float with get, set

        abstract imageSmoothingEnabled: bool with get, set

        abstract lineCap: string with get, set

        abstract lineDashOffset: float with get, set

        abstract lineJoin: string with get, set

        abstract lineWidth: float with get, set

        abstract miterLimit: float with get, set

        abstract strokeStyle: obj with get, set

    [<RequireQualifiedAccess>]
    module Canvas =

        [<Fable.Core.Emit
          ("(typeof OffscreenCanvas !== 'undefined' ? new OffscreenCanvas($0, $1) : Object.assign(document.createElement('canvas'), { width: $0, height: $1 }))")>]
        val scratch: width: int -> height: int -> CanvasContext

        val context: canvas: CanvasElement -> CanvasContext

        [<Fable.Core.Emit ("document.getElementById($0)")>]
        val byId: _id: string -> CanvasElement

        [<Fable.Core.Emit
          ("if ($0.style) { $0.style.width = $1 + 'px'; $0.style.height = $2 + 'px'; }")>]
        val setCssSize:
          _canvas: CanvasElement -> _width: float -> _height: float -> unit

        [<Fable.Core.Emit
          ("(typeof window !== 'undefined' && window.devicePixelRatio) || 1")>]
        val devicePixelRatio: unit -> float

namespace Marey

    [<RequireQualifiedAccess>]
    module CssFont =

        val family: families: string -> string

        val ofSpec: font: FontSpec -> string

    [<RequireQualifiedAccess>]
    module WebFonts =

        [<Fable.Core.Emit
          ("(typeof document === 'undefined' || !document.fonts) ? 'loaded' : document.fonts.status")>]
        [<Fable.Core.Emit
          ("(typeof document === 'undefined' || !document.fonts) ? Promise.resolve() : document.fonts.ready.then(function () {})")>]
        val ready: unit -> bool

        val whenReady: unit -> Fable.Core.JS.Promise<unit>

    [<Sealed>]
    type CanvasTextMeasurer =
        interface ITextMeasurer

        new: unit -> CanvasTextMeasurer

    [<NoComparison; NoEquality>]
    type WebMeasurer =
        {
          Measurer: ITextMeasurer

          Approximated: string option
        }

    [<RequireQualifiedAccess>]
    module WebText =

        val measurerNow: unit -> WebMeasurer

        val measurer: unit -> Fable.Core.JS.Promise<ITextMeasurer>

namespace Marey

    [<Sealed>]
    type WebGlMarkers =

        new: ?minimumBatch: int -> WebGlMarkers

        member Claims: b: MarkerBatch -> bool

        member
          Draw: ctx: CanvasContext * ctm: Affine<nd,px> * b: MarkerBatch *
                scale: float -> bool

        member MinimumBatch: int

        static member Shared: WebGlMarkers

namespace Marey

    [<RequireQualifiedAccess>]
    module CssColor =

        val ofColor: c: Color -> string

    [<Sealed>]
    type CanvasPainter =

        new: measurer: ITextMeasurer * layer: (int -> int -> CanvasContext) *
             scale: float * markers: WebGlMarkers option -> CanvasPainter

        member
          Draw: ctx: CanvasContext * ctm: Affine<nd,px> * node: Node -> unit

    [<Sealed>]
    type CanvasBackend =
        interface IBackend

        new: ctx: CanvasContext * ?measurer: ITextMeasurer * ?background: Color *
             ?pixelRatio: float * ?layer: (int -> int -> CanvasContext) *
             ?webgl: bool -> CanvasBackend

        static member
          Draw: ctx: CanvasContext * extent: Extent<px> * node: Node *
                ?measurer: ITextMeasurer * ?background: Color *
                ?pixelRatio: float * ?layer: (int -> int -> CanvasContext) *
                ?webgl: bool -> unit

        static member Capabilities: Capabilities

    [<RequireQualifiedAccess>]
    module CanvasBackend =

        val draw:
          measurer: ITextMeasurer ->
            ctx: CanvasContext -> extent: Extent<px> -> node: Node -> unit
