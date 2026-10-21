namespace Marey

open Fable.Core

// ======================================================================================
// THE CANVAS, AS THE BACKEND SEES IT
//
// The members of the browser's 2-D canvas this package calls, by their JavaScript names, and
// nothing else. Declared here rather than taken from `Fable.Browser.Dom`, whose bindings predate
// `TextMetrics.fontBoundingBoxAscent`, `document.fonts` and the fill rule `fill` takes, which are
// three of the things D122 and D123 need. A caller holding `Fable.Browser.Dom`'s
// `CanvasRenderingContext2D` passes it with `unbox`: both are the same JavaScript object.
//
// Small on purpose. Gate 13 drives the backend under Node against a context that records each
// call, and a recorder has to implement every member the backend could reach.
// ======================================================================================

/// A canvas gradient, to add stops to.
[<AllowNullLiteral>]
type CanvasGradient =
    abstract addColorStop: offset: float * color: string -> unit

/// What `measureText` returns, the font box included.
[<AllowNullLiteral>]
type CanvasTextMetrics =
    abstract width: float
    /// The font's ascent above the alphabetic baseline, in px. `undefined` in a browser older
    /// than 2023, which the measurer allows for.
    abstract fontBoundingBoxAscent: float
    abstract fontBoundingBoxDescent: float

/// RGBA8 pixels, not premultiplied, top row first: an `Image`'s layout exactly.
[<AllowNullLiteral>]
type CanvasImageData =
    abstract data: byte[]

/// The canvas element, or an `OffscreenCanvas`: something with a size and a 2-D context.
[<AllowNullLiteral>]
type CanvasElement =
    abstract width: float with get, set
    abstract height: float with get, set
    abstract getContext: contextId: string -> CanvasContext

/// The 2-D context, as far as this package uses it.
and [<AllowNullLiteral>] CanvasContext =
    abstract canvas: CanvasElement
    abstract fillStyle: obj with get, set
    abstract strokeStyle: obj with get, set
    abstract lineWidth: float with get, set
    abstract lineCap: string with get, set
    abstract lineJoin: string with get, set
    abstract miterLimit: float with get, set
    abstract lineDashOffset: float with get, set
    abstract font: string with get, set
    abstract globalAlpha: float with get, set
    abstract imageSmoothingEnabled: bool with get, set
    abstract setLineDash: segments: float[] -> unit
    abstract save: unit -> unit
    abstract restore: unit -> unit
    abstract setTransform: a: float * b: float * c: float * d: float * e: float * f: float -> unit
    abstract transform: a: float * b: float * c: float * d: float * e: float * f: float -> unit
    abstract translate: x: float * y: float -> unit
    abstract rotate: angle: float -> unit
    abstract clearRect: x: float * y: float * w: float * h: float -> unit
    abstract fillRect: x: float * y: float * w: float * h: float -> unit
    abstract beginPath: unit -> unit
    abstract moveTo: x: float * y: float -> unit
    abstract lineTo: x: float * y: float -> unit
    abstract bezierCurveTo: c1x: float * c1y: float * c2x: float * c2y: float * x: float * y: float -> unit
    abstract arc: x: float * y: float * radius: float * startAngle: float * endAngle: float -> unit
    abstract closePath: unit -> unit
    /// `"nonzero"` or `"evenodd"`.
    abstract fill: fillRule: string -> unit
    abstract stroke: unit -> unit
    abstract clip: fillRule: string -> unit
    abstract fillText: text: string * x: float * y: float -> unit
    abstract measureText: text: string -> CanvasTextMetrics
    abstract createLinearGradient: x0: float * y0: float * x1: float * y1: float -> CanvasGradient
    abstract createRadialGradient: x0: float * y0: float * r0: float * x1: float * y1: float * r1: float -> CanvasGradient
    abstract createImageData: width: float * height: float -> CanvasImageData
    abstract putImageData: image: CanvasImageData * x: float * y: float -> unit
    abstract drawImage: image: CanvasElement * x: float * y: float * w: float * h: float -> unit

/// Where a backend gets the scratch surfaces an opacity group and a raster are drawn on.
[<RequireQualifiedAccess>]
module internal Canvas =

    [<Emit("(typeof OffscreenCanvas !== 'undefined' ? new OffscreenCanvas($0, $1) : Object.assign(document.createElement('canvas'), { width: $0, height: $1 }))")>]
    let private create (_width: int) (_height: int) : CanvasElement = jsNative

    /// A new surface of this size in device pixels, and its context: an `OffscreenCanvas` where
    /// there is one, a detached canvas element otherwise.
    let scratch (width: int) (height: int) : CanvasContext = (create (max 1 width) (max 1 height)).getContext "2d"

    /// The 2-D context of a canvas element, for a caller holding the element.
    let context (canvas: CanvasElement) : CanvasContext = canvas.getContext "2d"

    /// The canvas element with this id, or `null`.
    [<Emit("document.getElementById($0)")>]
    let byId (_id: string) : CanvasElement = jsNative

    /// Give a canvas element its size in CSS pixels, so a canvas drawn at a higher density shows
    /// at the size of its figure. An `OffscreenCanvas`, which has no style, is left alone.
    ///
    /// On .NET, which has no element to size, it does nothing, so the backend runs there against a
    /// context a test supplies (step F's gate 5).
    [<Emit("if ($0.style) { $0.style.width = $1 + 'px'; $0.style.height = $2 + 'px'; }")>]
    let setCssSize (_canvas: CanvasElement) (_width: float) (_height: float) : unit = ()

    /// The display's device pixels per CSS pixel, 1 where there is no window: on .NET too.
    [<Emit("(typeof window !== 'undefined' && window.devicePixelRatio) || 1")>]
    let devicePixelRatio () : float = 1.0
