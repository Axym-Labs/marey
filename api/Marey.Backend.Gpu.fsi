namespace Marey

    [<Sealed>]
    type GpuRenderer =
        interface System.IDisposable

        new: ?options: SkiaOptions * ?instancedMarkers: bool -> GpuRenderer

        member RenderImage: extent: Extent<px> -> node: Node -> Image

        member
          RenderImageAt: scale: float ->
                           extent: Extent<px> -> node: Node -> Image

        member RenderPng: extent: Extent<px> -> node: Node -> byte array

        member
          RenderPngAt: scale: float ->
                         extent: Extent<px> -> node: Node -> byte array

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
            Stepper<Fluid<'x,'q>,Data.UnitSystems.SI.UnitSymbols.s>

        val stam:
          gpu: GpuRenderer ->
            options: StamOptions<'x,'q> ->
            Stepper<Fluid<'x,'q>,Data.UnitSystems.SI.UnitSymbols.s>

        val lbmWith:
          substeps: int ->
            gpu: GpuRenderer ->
            options: LbmOptions<'x> ->
            Stepper<Lattice<'x>,Data.UnitSystems.SI.UnitSymbols.s>

        val lbm:
          gpu: GpuRenderer ->
            options: LbmOptions<'x> ->
            Stepper<Lattice<'x>,Data.UnitSystems.SI.UnitSymbols.s>

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
