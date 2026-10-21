namespace FSharp

namespace Marey

namespace Marey

namespace Marey

namespace Marey

namespace Marey

    [<Sealed>]
    type GpuRenderer =
        interface System.IDisposable

        new: ?options: SkiaOptions * ?instancedMarkers: bool -> GpuRenderer

        member RenderImage: extent: Extent<px> -> node: Node -> Image

        member RenderPng: extent: Extent<px> -> node: Node -> byte array

        member Caps: Capabilities

        member Description: string

        member Measurer: ITextMeasurer

        member ResolvedFaces: ResolvedFace array

    [<RequireQualifiedAccess>]
    module GpuBackend =

        val probe: unit -> Result<string,string>

namespace Marey

    [<RequireQualifiedAccess>]
    module GpuSim =

        val stamWith:
          substeps: int ->
            gpu: GpuRenderer ->
            options: StamOptions<'x,'q> ->
            Step<Fluid<'x,'q>,Data.UnitSystems.SI.UnitSymbols.s>

        val stam:
          gpu: GpuRenderer ->
            options: StamOptions<'x,'q> ->
            Step<Fluid<'x,'q>,Data.UnitSystems.SI.UnitSymbols.s>

        val lbmWith:
          substeps: int ->
            gpu: GpuRenderer ->
            options: LbmOptions<'x> ->
            Step<Lattice<'x>,Data.UnitSystems.SI.UnitSymbols.s>

        val lbm:
          gpu: GpuRenderer ->
            options: LbmOptions<'x> ->
            Step<Lattice<'x>,Data.UnitSystems.SI.UnitSymbols.s>

namespace Marey

    [<RequireQualifiedAccess>]
    type WindowKey =

        | Tab

        | Left

        | Right

        | Home

        | End

        | Space

    [<RequireQualifiedAccess>]
    type WindowEvent =

        | Resized of Extent<px>
        | Moved of Vec2<px>

        | Pressed of at: Vec2<px> * shift: bool
        | Released of Vec2<px>

        | Left
        | Key of WindowKey * shift: bool

        | Frame

    type WindowControlRow =
        {
          Index: int

          Row: Rect<px>

          Label: Vec2<px>

          Widget: Rect<px>

          Value: Vec2<px>
        }

    [<RequireQualifiedAccess>]
    module WindowStrip =

        [<Literal>]
        val RowHeight: float = 28

        [<Literal>]
        val Pad: float = 8

        [<Literal>]
        val LabelWidth: float = 140

        [<Literal>]
        val ValueWidth: float = 120

        [<Literal>]
        val PlayWidth: float = 20

        val height: controls: ControlSpec array -> float<px>

        val rows:
          controls: ControlSpec array ->
            width: float<px> -> WindowControlRow array

        val playButton: row: WindowControlRow -> Rect<px>

        val segment: widget: Rect<px> -> n: int -> k: int -> Rect<px>

        val sliderValue:
          lo: float * hi: float -> widget: Rect<px> -> x: float<px> -> float

        val knobX:
          lo: float * hi: float -> widget: Rect<px> -> v: float -> float<px>

    type WindowDrag =
        {
          Start: Vec2<px>

          Last: Vec2<px>

          Now: Vec2<px>

          Pan: bool
        }

    type WindowGesture =
        {
          Drag: WindowDrag option
          Hold: int option
          Focus: int option
          LastPress:
            (float<Data.UnitSystems.SI.UnitSymbols.s> * Vec2<px>) option
          Over: bool

          Playing:
            (int * float<Data.UnitSystems.SI.UnitSymbols.s> * float) option

          Window: Extent<px>
        }

    [<NoComparison>]
    type WindowSession =
        {
          State: LiveState
          Gesture: WindowGesture
        }

    [<RequireQualifiedAccess>]
    module WindowSession =

        [<Literal>]
        val DoubleClickSeconds: float = 0.5

        [<Literal>]
        val DoubleClickPx: float = 4

        val start: live: Live -> WindowSession

        val origin: live: Live -> Vec2<px>

        val translate:
          live: Live ->
            state: LiveState ->
            g: WindowGesture ->
            t: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            e: WindowEvent -> WindowGesture * LiveMsg list

        val feed:
          live: Live ->
            session: WindowSession ->
            t: float<Data.UnitSystems.SI.UnitSymbols.s> * e: WindowEvent ->
              WindowSession * LiveMsg list

namespace Marey

    [<Sealed>]
    type GpuWindow =
        interface System.IDisposable

        static member
          Open: figure: Figure * ?title: string * ?size: Extent<px> *
                ?options: SkiaOptions * ?visible: bool -> GpuWindow

        static member
          Open: recorder: Recorder * ?title: string * ?size: Extent<px> *
                ?options: SkiaOptions * ?visible: bool -> GpuWindow

        static member
          Open: live: Live * ?title: string * ?size: Extent<px> *
                ?options: SkiaOptions * ?visible: bool -> GpuWindow

        member Capture: unit -> Image

        member Close: unit -> unit

        member Post: e: WindowEvent -> unit

        member Wait: unit -> unit

        member IsOpen: bool

        member Session: WindowSession

        member State: LiveState

        static member DefaultSize: Extent<px>

    [<AutoOpen>]
    module GpuShow =

        [<RequireQualifiedAccess>]
        module Chart =

            [<CompiledName ("Show")>]
            val show: figure: Figure -> unit

        [<RequireQualifiedAccess>]
        module Live =

            [<CompiledName ("Show")>]
            val show: live: Live -> unit

        [<RequireQualifiedAccess>]
        module Recorder =

            [<CompiledName ("Show")>]
            val show: recorder: Recorder -> unit
