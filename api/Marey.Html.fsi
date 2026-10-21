namespace Marey

    [<RequireQualifiedAccess>]
    type RenderMode =
        | Svg
        | Canvas
        | Auto

    [<RequireQualifiedAccess>]
    module Html =

        val text: s: string -> string

        val attr: s: string -> string

        val defaultSize: Extent<px>

        val figureAt: extent: Extent<px> -> fig: Figure -> string

        val figure: fig: Figure -> string

        val nodeAt: extent: Extent<px> -> n: Node -> string

        val node: n: Node -> string

        [<Literal>]
        val FlipbookFrames: int = 48

        val filmAt: frames: int -> film: Film -> string

        val film: f: Film -> string

        val color: c: Color -> string

        val ramp: r: Ramp -> string

        val palette: cs: Color array -> string

        val paletteReport: r: PaletteReport -> string

        val dim: d: Dim -> string

        val witness: u: U<'u> -> string

        val scale: s: IScaleErased -> string

        val viewScript: Lazy<string>

        [<Literal>]
        val CanvasThreshold: int = 5000

        val mutable renderMode: RenderMode

        val drawsOnCanvas: scene: Node -> bool

        val widgetFigure: live: Live -> state: LiveState -> string

        val widget: live: Live -> state: LiveState -> isLive: bool -> string

        val canRenderForHost: value: obj -> bool

        val extentForHost: maxWidth: float * maxHeight: float -> Extent<px>

        val renderForHost:
          maxWidth: float * maxHeight: float * value: obj -> string

        val figureDocument: extent: Extent<px> -> fig: Figure -> string option

        val documentForHost:
          maxWidth: float * maxHeight: float * value: obj -> string

        val liveInitForHost: value: obj -> obj

        val liveStepForHost:
          value: obj * state: obj * msgType: string * payload: string -> obj

        val liveDocumentForHost:
          value: obj * state: obj * isLive: bool -> string

        val widgetPatch:
          live: Live -> before: LiveState -> after: LiveState -> SvgPatch

        val liveReplyForHost: value: obj * state: obj -> string array

        val liveAnswerForHost:
          value: obj * before: obj * after: obj -> string array

        val liveWholeForHost: value: obj * state: obj -> string array

        val liveReadoutForHost: value: obj * state: obj -> string array
