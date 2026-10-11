namespace Marey.UnitsOfMeasure

open System
open Marey

/// D109 — the controls that take a witness, in the unit-required mode: a slider over a quantity,
/// handing its view that quantity's measure. See `Marey.UnitsOfMeasure.Chart` for the pattern and
/// the shadows. Every other control takes no witness and is the core's in both modes.
[<RequireQualifiedAccess>]
module Control =

    /// A slider over a quantity, from `lo` to `hi` in steps of `step`. The view receives
    /// `float<'u>`; the label is written in `'u` under one prefix chosen from the range.
    let sliderBy<[<Measure>] 'u>
        (u: U<'u>)
        (label: string)
        (lo: float<'u>)
        (hi: float<'u>)
        (step: float<'u>)
        (init: float<'u>)
        : Control<float<'u>> =
        Marey.Control.sliderBy u label lo hi step init
    /// A slider with a hundred steps across its range.
    let slider<[<Measure>] 'u> (u: U<'u>) (label: string) (lo: float<'u>) (hi: float<'u>) (init: float<'u>) =
        Marey.Control.slider u label lo hi init

    /// D137 — a time control over `[lo, hi]` in seconds that loops, arranged over 60 states.
    /// The core's, in both modes; here so that this module answers for every control.
    let time (label: string) (lo: float<Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols.s>) (hi: float<Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols.s>) =
        Marey.Control.time label lo hi

    /// D137 — `time` with its sample count and whether it loops.
    let timeWith (samples: int) (loop: bool) (label: string) (lo: float<Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols.s>) (hi: float<Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols.s>) =
        Marey.Control.timeWith samples loop label lo hi

    [<Obsolete("Marey.UnitsOfMeasure is open, so a time is in seconds: Control.time \"t\" 0.0<s> 10.0<s>. Control.Time(…) is the unit-optional spelling, over plain seconds, for code that does not open this namespace.", true)>]
    let Time (_: obj) : Control<float> = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Control.sliderBy. Control.SliderBy(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let SliderBy (_: obj) : Control<float> = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Control.slider. Control.Slider(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Slider (_: obj) : Control<float> = invalidOp "Marey: unreachable — an error-obsolete shadow."
