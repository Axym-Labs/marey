namespace Marey

    type ControlKind =

        | Slider of lo: float * hi: float * step: float

        | Select of options: string array

        | Checkbox

        | Time of lo: float * hi: float * samples: int * loop: bool

    [<NoEquality; NoComparison>]
    type ControlSpec =
        {
          Label: string
          Kind: ControlKind

          Init: float

          Show: (float -> string)
        }

    [<NoEquality; NoComparison>]
    type Control<'a> =
        {
          Specs: ControlSpec array
          Read: (float array -> 'a)
        }

    [<RequireQualifiedAccess>]
    module ControlSpec =

        val normalise: spec: ControlSpec -> v: float -> float option

    [<RequireQualifiedAccess>]
    module Control =

        val constant: x: 'a -> Control<'a>

        val map: f: ('a -> 'b) -> c: Control<'a> -> Control<'b>

        val zip: a: Control<'a> -> b: Control<'b> -> Control<'a * 'b>

        val select:
          label: string ->
            options: (string * 'a) list -> init: int -> Control<'a>

        val timeWith:
          samples: int ->
            loop: bool ->
            label: string ->
            lo: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            hi: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            Control<float<Data.UnitSystems.SI.UnitSymbols.s>>

        val time:
          label: string ->
            lo: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            hi: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            Control<float<Data.UnitSystems.SI.UnitSymbols.s>>

        val checkbox: label: string -> init: bool -> Control<bool>

    [<AbstractClass; Sealed; Class>]
    type Control =

        static member
          Slider: label: string * lo: float * hi: float * init: float *
                  [<System.Runtime.InteropServices.Optional;
                    System.Runtime.InteropServices.DefaultParameterValue
                    ((null :> obj))>] unit: IUnit -> Control<float>

        static member
          SliderBy: label: string * lo: float * hi: float * step: float *
                    init: float *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] unit: IUnit -> Control<float>

        static member
          Time: label: string * lo: float * hi: float *
                [<System.Runtime.InteropServices.Optional;
                  System.Runtime.InteropServices.DefaultParameterValue
                  ((60 :> obj))>] samples: int *
                [<System.Runtime.InteropServices.Optional;
                  System.Runtime.InteropServices.DefaultParameterValue
                  ((true :> obj))>] loop: bool -> Control<float>

namespace Marey

    type Recording<'a> =
        { Events: (float<Data.UnitSystems.SI.UnitSymbols.s> * 'a) array }

    [<RequireQualifiedAccess>]
    module Recording =

        val ofEvents:
          events: (float<Data.UnitSystems.SI.UnitSymbols.s> * 'a) array ->
            Recording<'a>

        val duration:
          r: Recording<'a> -> float<Data.UnitSystems.SI.UnitSymbols.s>

        val map: f: ('a -> 'b) -> r: Recording<'a> -> Recording<'b>

        val sample: r: Recording<'a> -> Anim<'a>

namespace Marey

    [<NoEquality; NoComparison>]
    type Live =
        {
          Controls: ControlSpec array
          View: (float array -> Figure)
        }

    [<RequireQualifiedAccess>]
    type ZoomAxis =
        | X
        | Y

        | Y2

    type LiveZoom =
        internal

    [<NoComparison>]
    type LiveState =
        {

          Values: float array

          Size: Extent<px>

          Shot: Shot

          Grown: float array array

          Zoom: LiveZoom array

          Pointer: Vec2<px> option
        }

    type LiveReadout =
        {
          Row: int
          Col: int

          Plot: Rect<px>

          Right: bool

          Lines: string array
        }

    type LiveMsg =

        | Set of index: int * value: float

        | Resize of width: float * height: float

        | Zoom of Rect<px>

        | Pan of at: Vec2<px> * by: Vec2<px>

        | Reset

        | Hover of Vec2<px>

        | Leave

    [<RequireQualifiedAccess>]
    module LiveMsg =

        val ofWire: msgType: string -> ns: float array -> LiveMsg option

        val parseStamped:
          msgType: string ->
            payload: string ->
            (float<Data.UnitSystems.SI.UnitSymbols.s> option * LiveMsg) option

        val parse: msgType: string -> payload: string -> LiveMsg option

        val toWire: msg: LiveMsg -> string * float array

    [<RequireQualifiedAccess>]
    module LiveLog =

        [<Literal>]
        val Format: string = "marey.live-log"

        [<Literal>]
        val Version: int = 1

        val toJson: log: Recording<LiveMsg> -> string

        val ofJson: json: string -> Result<Recording<LiveMsg>,string>

    [<Sealed; AllowNullLiteral>]
    type Recorder =

        member Begin: unit -> int

        member
          Note: from: int * at: float<Data.UnitSystems.SI.UnitSymbols.s> option *
                msg: LiveMsg -> unit

        member Stop: unit -> Recording<LiveMsg>

        member Live: Live

        member Log: Recording<LiveMsg>

    [<NoComparison>]
    type RecorderState =
        {
          Session: int
          State: LiveState
        }

    [<RequireQualifiedAccess>]
    module Recorder =

        val log: r: Recorder -> Recording<LiveMsg>

        val stop: r: Recorder -> Recording<LiveMsg>

    [<RequireQualifiedAccess>]
    module Live =

        val DefaultSize: Extent<px>

        [<Literal>]
        val MaxExtremes: int = 64

        val ofControl: c: Control<Figure> -> Live

        val recorder: live: Live -> Recorder

        [<Literal>]
        val MinDrag: float = 6

        val extremes: live: Live -> float array array

        val initAt: size: Extent<px> -> live: Live -> LiveState

        val init: live: Live -> LiveState

        val view: live: Live -> state: LiveState -> Figure

        val panels: live: Live -> state: LiveState -> PanelView array

        val scene: live: Live -> state: LiveState -> Node

        val drawsAlike: a: LiveState -> b: LiveState -> bool

        val readout: live: Live -> state: LiveState -> LiveReadout option

        val shown: live: Live -> state: LiveState -> string array

        val step: live: Live -> state: LiveState -> msg: LiveMsg -> LiveState

        val recordFrom:
          live: Live ->
            start: LiveState -> log: Recording<LiveMsg> -> Recording<LiveState>

        val record:
          live: Live -> log: Recording<LiveMsg> -> Recording<LiveState>

        val film: live: Live -> recording: Recording<LiveState> -> Film

    type InteractBuilder =

        new: unit -> InteractBuilder

        member BindReturn: c: Control<'a> * f: ('a -> 'b) -> Control<'b>

        member MergeSources: a: Control<'a> * b: Control<'b> -> Control<'a * 'b>

        member Return: x: 'a -> Control<'a>

        member Run: c: Control<Figure> -> Live

    [<AutoOpen>]
    module InteractBuilderInstance =

        val interact: InteractBuilder

namespace Marey.UnitsOfMeasure

    [<RequireQualifiedAccess>]
    module Control =

        val sliderBy:
          u: Marey.U<'u> ->
            label: string ->
            lo: float<'u> ->
            hi: float<'u> ->
            step: float<'u> -> init: float<'u> -> Marey.Control<float<'u>>

        val slider:
          u: Marey.U<'u> ->
            label: string ->
            lo: float<'u> ->
            hi: float<'u> -> init: float<'u> -> Marey.Control<float<'u>>

        val time:
          label: string ->
            lo: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            hi: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            Marey.Control<float<Data.UnitSystems.SI.UnitSymbols.s>>

        val timeWith:
          samples: int ->
            loop: bool ->
            label: string ->
            lo: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            hi: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            Marey.Control<float<Data.UnitSystems.SI.UnitSymbols.s>>

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a time is in seconds: Control.time "t" 0.0<s> 10.0<s>. Control.Time(…) is the unit-optional spelling, over plain seconds, for code that does not open this namespace.",
           true)>]
        val Time: obj -> Marey.Control<float>

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Control.sliderBy. Control.SliderBy(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val SliderBy: obj -> Marey.Control<float>

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Control.slider. Control.Slider(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Slider: obj -> Marey.Control<float>
