namespace Marey

open System
open System.Runtime.InteropServices
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// M4.5 — TYPED CONTROLS (D101)
//
// A control is a source of one value, owned by the widget and read-only to the code that uses
// it. Controls compose *applicatively*: `interact { let! t = … and! on = … in return view t on }`.
// The block has `BindReturn` and `MergeSources` and no `Bind`, so a control cannot be chosen by
// the value of another one. That is the point rather than a limitation. The set of controls is
// known without running the view, which is what lets the widget lay out once against all of
// them (D106) and draw itself before the kernel has answered; and a widget whose shape was a
// function of its own state would have no stable document to patch.
//
// **The measures are spent here, once.** A slider over `float<K>` hands the view `float<K>`;
// the widget and the messages only ever see the erased `ControlSpec` and a `float`. The value's
// label is written by `UnitDisplay.quantityIn`, under a prefix chosen once from the slider's
// range, so number formatting stays in one place and in the kernel (D105): the widget's script
// formats nothing.
// ======================================================================================

/// What the widget draws for one control. Erased: every value is a `float` in the control's
/// own unit, or an index, or 0 and 1.
type ControlKind =
    /// A range input from `Lo` to `Hi` in steps of `Step`, all in the slider's unit.
    | Slider of lo: float * hi: float * step: float
    /// One of the options, by index. The strings are what the reader sees.
    | Select of options: string[]
    /// 1 for on, 0 for off.
    | Checkbox
    /// M5.5, D137 — a time in seconds from `Lo` to `Hi`, scrubbed like a slider and played by the
    /// host on its own clock, one second a second, which posts the values as `Set`s. `Samples`
    /// states along the range are what a live figure is arranged over, in place of the two ends,
    /// so a view of the current state does not re-arrange as it plays. At the end, play wraps to
    /// `Lo` when `Loop` is set and stops otherwise.
    | Time of lo: float * hi: float * samples: int * loop: bool

/// One control, with its measures spent.
[<NoEquality; NoComparison>]
type ControlSpec =
    { Label: string
      Kind: ControlKind
      /// The value the control starts at, already normalised.
      Init: float
      /// The value as the reader reads it: `300 K`, `on`, an option's name. Written in the kernel,
      /// so the widget never formats a number.
      Show: float -> string }

/// A typed source of values: the controls it draws, and how to read a `'a` from their values.
///
/// `Read` is handed exactly this control's slice of the widget's values, in `Specs` order.
[<NoEquality; NoComparison>]
type Control<'a> =
    { Specs: ControlSpec[]
      Read: float[] -> 'a }

[<RequireQualifiedAccess>]
module ControlSpec =

    /// The nearest value this control can hold. A slider is clamped into its range, an index
    /// rounded into the options, a checkbox made 0 or 1. Non-finite input is `None`, because a
    /// NaN has no nearest legal value (`Scene.opacity`'s reason).
    let normalise (spec: ControlSpec) (v: float) : float option =
        if Double.IsNaN v || Double.IsInfinity v then
            None
        else
            match spec.Kind with
            | Slider(lo, hi, _) -> Some(max lo (min hi v))
            | Select options -> Some(float (max 0 (min (options.Length - 1) (int (Num.roundEven v)))))
            | Checkbox -> Some(if v <> 0.0 then 1.0 else 0.0)
            | Time(lo, hi, _, _) -> Some(max lo (min hi v))

[<RequireQualifiedAccess>]
module Control =

    /// No control at all: a value the widget does not ask for.
    let constant (x: 'a) : Control<'a> = { Specs = [||]; Read = fun _ -> x }

    let map (f: 'a -> 'b) (c: Control<'a>) : Control<'b> = { Specs = c.Specs; Read = c.Read >> f }

    /// Both controls, in order: `a`'s first.
    let zip (a: Control<'a>) (b: Control<'b>) : Control<'a * 'b> =
        let n = a.Specs.Length

        { Specs = Array.append a.Specs b.Specs
          Read = fun vs -> a.Read(Array.sub vs 0 n), b.Read(Array.sub vs n (vs.Length - n)) }

    let private finite (x: float) = not (Double.IsNaN x || Double.IsInfinity x)

    /// The decimals a step needs, under the prefix the label is written in: a step of 0.1 kV
    /// shows one place, a step of 5 K none.
    let private decimalsFor (step: float) =
        if not (step > 0.0) || Double.IsInfinity step then 0
        else max 0 (min Num.MaxDecimals (int (Math.Ceiling(-Math.Log10 step - 1e-9))))

    let internal sliderBy<[<Measure>] 'u>
        (u: U<'u>)
        (label: string)
        (lo: float<'u>)
        (hi: float<'u>)
        (step: float<'u>)
        (init: float<'u>)
        : Control<float<'u>> =
        let lo', hi', step', init' = float lo, float hi, float step, float init

        if not (finite lo' && finite hi' && lo' < hi') then
            invalidArg "hi" (sprintf "a slider needs a finite range with lo < hi; got %g to %g" lo' hi')

        if not (step' > 0.0 && step' <= hi' - lo') then
            invalidArg "step" (sprintf "a slider's step must be positive and no wider than its range; got %g" step')

        if not (init' >= lo' && init' <= hi') then
            invalidArg "init" (sprintf "a slider starts inside its range; %g is outside %g to %g" init' lo' hi')

        let prefix = Prefix.selectForUnit u (lo', hi')
        let decimals = decimalsFor (step' / Prefix.factor prefix)

        let show (v: float) =
            UnitDisplay.quantityIn Unicode UnitRegistry.si prefix (Num.upTo decimals) u (LanguagePrimitives.FloatWithMeasure v)

        { Specs =
            [| { Label = label
                 Kind = Slider(lo', hi', step')
                 Init = init'
                 Show = show } |]
          Read = fun vs -> LanguagePrimitives.FloatWithMeasure vs.[0] }

    let internal slider<[<Measure>] 'u> (u: U<'u>) (label: string) (lo: float<'u>) (hi: float<'u>) (init: float<'u>) =
        sliderBy u label lo hi ((hi - lo) / 100.0) init

    /// One of a list of named values, starting at index `init`.
    let select (label: string) (options: (string * 'a) list) (init: int) : Control<'a> =
        let options = Array.ofList options

        if options.Length = 0 then
            invalidArg "options" "a select needs at least one option"

        if init < 0 || init >= options.Length then
            invalidArg "init" (sprintf "a select starts on one of its %d options; got index %d" options.Length init)

        let names = options |> Array.map fst

        { Specs =
            [| { Label = label
                 Kind = Select names
                 Init = float init
                 Show = fun v -> names.[max 0 (min (names.Length - 1) (int (Num.roundEven v)))] } |]
          Read = fun vs -> snd options.[max 0 (min (options.Length - 1) (int (Num.roundEven vs.[0])))] }

    /// D137 — a time control over `[lo, hi]`, starting at `lo`, arranged over `samples` states
    /// along it, looping at the end or stopping there. The view receives `float<s>`.
    let timeWith (samples: int) (loop: bool) (label: string) (lo: float<s>) (hi: float<s>) =
        let lo', hi' = float lo, float hi

        if not (finite lo' && finite hi' && lo' < hi') then
            invalidArg "hi" (sprintf "a time control needs a finite range with lo < hi; got %g s to %g s" lo' hi')

        if samples < 2 then
            invalidArg "samples" (sprintf "a time control is arranged over at least its two ends; got %d samples" samples)

        let u = U.second
        let prefix = Prefix.selectForUnit u (lo', hi')
        let decimals = decimalsFor ((hi' - lo') / 1000.0 / Prefix.factor prefix)

        let show (v: float) =
            UnitDisplay.quantityIn Unicode UnitRegistry.si prefix (Num.upTo decimals) u (LanguagePrimitives.FloatWithMeasure v)

        { Specs =
            [| { Label = label
                 Kind = Time(lo', hi', samples, loop)
                 Init = lo'
                 Show = show } |]
          Read = fun (vs: float[]) -> LanguagePrimitives.FloatWithMeasure<s> vs.[0] }

    /// D137 — a time control over `[lo, hi]` that loops, arranged over 60 states along it.
    let time (label: string) (lo: float<s>) (hi: float<s>) =
        timeWith 60 true label lo hi

    /// On or off.
    let checkbox (label: string) (init: bool) : Control<bool> =
        { Specs =
            [| { Label = label
                 Kind = Checkbox
                 Init = (if init then 1.0 else 0.0)
                 Show = fun v -> if v <> 0.0 then "on" else "off" } |]
          Read = fun vs -> vs.[0] <> 0.0 }

/// D109 — the controls that take a witness, in the unit-optional mode: a slider over plain
/// numbers, with a unit that may be stated. `Control.Slider("T", 200.0, 400.0, 300.0, unit = U.kelvin)`.
/// The other controls take no witness and are the module's in both modes.
[<AbstractClass; Sealed>]
type Control =

    static member SliderBy
        (
            label: string,
            lo: float,
            hi: float,
            step: float,
            init: float,
            [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit
        ) : Control<float> =
        Control.sliderBy (U.erase unit) label lo hi step init

    static member Slider
        (
            label: string,
            lo: float,
            hi: float,
            init: float,
            [<Optional; DefaultParameterValue(null: IUnit)>] unit: IUnit
        ) : Control<float> =
        Control.slider (U.erase unit) label lo hi init

    /// D137 — a time control over plain seconds: `Control.Time("t", 0.0, 10.0)`. It loops, and
    /// is arranged over 60 states along its range unless told otherwise.
    static member Time
        (
            label: string,
            lo: float,
            hi: float,
            [<Optional; DefaultParameterValue(60)>] samples: int,
            [<Optional; DefaultParameterValue(true)>] loop: bool
        ) : Control<float> =
        Control.map float (Control.timeWith samples loop label (lo * 1.0<s>) (hi * 1.0<s>))

