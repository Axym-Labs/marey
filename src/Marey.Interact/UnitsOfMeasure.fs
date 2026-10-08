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

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Control.sliderBy. Control.SliderBy(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let SliderBy (_: obj) : Control<float> = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Control.slider. Control.Slider(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Slider (_: obj) : Control<float> = invalidOp "Marey: unreachable — an error-obsolete shadow."
