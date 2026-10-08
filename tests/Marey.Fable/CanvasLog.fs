/// Gate 13 — the Canvas2D backend draws the IR (D122). Every golden scene is drawn on a context
/// that records each call and each property it is given, one line apiece, and the log is compared
/// with `tests/Marey.Fable/canvas/<name>.canvas.txt`. A node case the backend dropped, or a capability it
/// stopped honouring, changes the log of every scene that has one.
///
/// The figures are laid out against `TextMeasure.approx` and drawn with it, as a page whose fonts
/// have not arrived would be; Node has no fonts to measure.
module Marey.Fable.CanvasLog

open System
open Fable.Core
open Marey
open Marey.Tests

/// A context that writes down what it is told. Scratch layers are contexts too, numbered in the
/// order they are made, and write to the same log under their number.
type private Recorder(log: ResizeArray<string>, name: string, width: float, height: float, layers: int ref) =

    let n (x: float) = Num.at 3 x
    let line (s: string) = log.Add(name + s)

    let mutable fillStyle: obj = null
    let mutable strokeStyle: obj = null
    let mutable lineWidth = 1.0
    let mutable lineCap = "butt"
    let mutable lineJoin = "miter"
    let mutable miterLimit = 10.0
    let mutable lineDashOffset = 0.0
    let mutable font = "10px sans-serif"
    let mutable globalAlpha = 1.0
    let mutable smoothing = true
    let mutable w = width
    let mutable h = height

    let style (o: obj) =
        match o with
        | :? string as s -> s
        | :? GradientRecord as g -> g.Describe()
        | _ -> string o

    member _.Layer (width: int) (height: int) : CanvasContext =
        layers.Value <- layers.Value + 1
        let r = Recorder(log, sprintf "L%d " layers.Value, float width, float height, layers)
        line (sprintf "layer L%d %dx%d" layers.Value width height)
        r :> CanvasContext

    interface CanvasElement with
        member _.width with get () = w and set v = w <- v; line (sprintf "canvas.width = %s" (n v))
        member _.height with get () = h and set v = h <- v; line (sprintf "canvas.height = %s" (n v))
        member this.getContext _ = this :> CanvasContext

    interface CanvasContext with
        member this.canvas = this :> CanvasElement
        member _.fillStyle with get () = fillStyle and set v = fillStyle <- v; line ("fillStyle = " + style v)
        member _.strokeStyle with get () = strokeStyle and set v = strokeStyle <- v; line ("strokeStyle = " + style v)
        member _.lineWidth with get () = lineWidth and set v = lineWidth <- v; line ("lineWidth = " + n v)
        member _.lineCap with get () = lineCap and set v = lineCap <- v; line ("lineCap = " + v)
        member _.lineJoin with get () = lineJoin and set v = lineJoin <- v; line ("lineJoin = " + v)
        member _.miterLimit with get () = miterLimit and set v = miterLimit <- v; line ("miterLimit = " + n v)
        member _.lineDashOffset with get () = lineDashOffset and set v = lineDashOffset <- v; line ("lineDashOffset = " + n v)
        member _.font with get () = font and set v = font <- v; line ("font = " + v)
        member _.globalAlpha with get () = globalAlpha and set v = globalAlpha <- v; line ("globalAlpha = " + n v)
        member _.imageSmoothingEnabled with get () = smoothing and set v = smoothing <- v; line (sprintf "imageSmoothingEnabled = %b" v)
        member _.setLineDash d = line ("setLineDash [" + (d |> Array.map n |> String.concat " ") + "]")
        member _.save() = line "save"
        member _.restore() = line "restore"
        member _.setTransform(a, b, c, d, e, f) = line (sprintf "setTransform %s %s %s %s %s %s" (n a) (n b) (n c) (n d) (n e) (n f))
        member _.transform(a, b, c, d, e, f) = line (sprintf "transform %s %s %s %s %s %s" (n a) (n b) (n c) (n d) (n e) (n f))
        member _.translate(x, y) = line (sprintf "translate %s %s" (n x) (n y))
        member _.rotate a = line ("rotate " + n a)
        member _.clearRect(x, y, w, h) = line (sprintf "clearRect %s %s %s %s" (n x) (n y) (n w) (n h))
        member _.fillRect(x, y, w, h) = line (sprintf "fillRect %s %s %s %s" (n x) (n y) (n w) (n h))
        member _.beginPath() = line "beginPath"
        member _.moveTo(x, y) = line (sprintf "moveTo %s %s" (n x) (n y))
        member _.lineTo(x, y) = line (sprintf "lineTo %s %s" (n x) (n y))
        member _.bezierCurveTo(a, b, c, d, e, f) = line (sprintf "bezierCurveTo %s %s %s %s %s %s" (n a) (n b) (n c) (n d) (n e) (n f))
        member _.arc(x, y, r, a0, a1) = line (sprintf "arc %s %s %s %s %s" (n x) (n y) (n r) (n a0) (n a1))
        member _.closePath() = line "closePath"
        member _.fill rule = line ("fill " + rule)
        member _.stroke() = line "stroke"
        member _.clip rule = line ("clip " + rule)
        member _.fillText(t, x, y) = line (sprintf "fillText %s %s %A" (n x) (n y) t)
        member _.measureText t = failwithf "the canvas gate lays out with TextMeasure.approx and does not measure: %s" t
        member _.createLinearGradient(x0, y0, x1, y1) = GradientRecord(sprintf "linear %s %s %s %s" (n x0) (n y0) (n x1) (n y1)) :> CanvasGradient
        member _.createRadialGradient(x0, y0, r0, x1, y1, r1) = GradientRecord(sprintf "radial %s %s %s %s %s %s" (n x0) (n y0) (n r0) (n x1) (n y1) (n r1)) :> CanvasGradient
        member _.createImageData(w, h) = ImageRecord(int w, int h) :> CanvasImageData
        member _.putImageData(image, x, y) = line (sprintf "putImageData %s at %s %s" ((image :?> ImageRecord).Describe()) (n x) (n y))
        member _.drawImage(_, x, y, w, h) = line (sprintf "drawImage %s %s %s %s" (n x) (n y) (n w) (n h))

/// A gradient remembers its geometry and stops, and is written as one value.
and private GradientRecord(geometry: string) =
    let stops = ResizeArray<string>()
    member _.Describe() = geometry + " [" + String.concat ", " stops + "]"

    interface CanvasGradient with
        member _.addColorStop(offset, color) = stops.Add(Num.at 3 offset + " " + color)

/// Image data whose pixels are summarised by a checksum, so a log is not a hex dump.
and private ImageRecord(w: int, h: int) =
    let data = Array.zeroCreate<byte> (w * h * 4)

    member _.Describe() =
        let mutable sum = 0u

        for b in data do
            sum <- (sum * 31u + uint32 b) % 1000000007u

        sprintf "%dx%d #%d" w h sum

    interface CanvasImageData with
        member _.data = data

[<Import("readFileSync", "node:fs")>]
let private readFileSync (_path: string, _encoding: string) : string = jsNative

[<Import("writeFileSync", "node:fs")>]
let private writeFileSync (_path: string, _text: string) : unit = jsNative

[<Import("existsSync", "node:fs")>]
let private existsSync (_path: string) : bool = jsNative

/// The log of one scene drawn by the backend, at a pixel ratio of 2 so that the layer and the
/// canvas sizes are seen to scale.
let logOf (extent: Extent<px>, node: Node) : string =
    let log = ResizeArray<string>()
    let layers = ref 0
    let root = Recorder(log, "", 300.0, 150.0, layers)

    CanvasBackend.Draw(
        root :> CanvasContext,
        extent,
        node,
        measurer = TextMeasure.approx,
        pixelRatio = 2.0,
        layer = root.Layer,
        // The canvas's own drawing is what is logged; WebGL2's is checked in a browser.
        webgl = false
    )

    String.concat "\n" log + "\n"

/// Every golden scene against its log in `dir`; the names that differ.
let check (dir: string) : string list =
    [ for name, extent, node in GoldenScenes.scenesWith TextMeasure.approx do
          let actual = logOf (extent, node)
          let path = dir + "/" + name + ".canvas.txt"

          if existsSync path && (readFileSync (path, "utf8")).Replace("\r\n", "\n") = actual then
              printfn "  ok    %s" name
          else
              writeFileSync (dir + "/" + name + ".actual.txt", actual)
              printfn "  FAIL  %s%s" name (if existsSync path then "" else ", no baseline")
              yield name ]
