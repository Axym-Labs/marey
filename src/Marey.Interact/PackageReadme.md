# Marey.Interact

Interaction for [Marey](https://github.com/Axym-Labs/marey), a dimensionally-typed visualization
system for F#. **A slider over kelvin hands the view kelvin, and labels itself in kelvin.**

```fsharp
#r "nuget: Marey.Interact"
#r "nuget: Marey.Html"

open Marey
open Marey.UnitsOfMeasure

let ringdown (f: float<Hz>) (damped: bool) =
    let t = Array.init 200 (fun i -> float i * 0.01<s>)
    let decay = if damped then 1.5<1/s> else 0.0<1/s>
    let y = t |> Array.map (fun t -> exp (-decay * t) * sin (2.0 * System.Math.PI * f * t) * 1.0<m>)
    Chart.line U.second U.metre t y

let live =
    interact {
        let! f = Control.slider Derived.hertz "f" 0.5<Hz> 5.0<Hz> 1.0<Hz>
        and! damped = Control.checkbox "damped" false
        return ringdown f damped
    }
```

## Controls

| | The view receives | The label reads |
|---|---|---|
| `Control.slider u label lo hi init` | `float<'u>` | the value in `'u`, under one prefix chosen from the range |
| `Control.sliderBy u label lo hi step init` | `float<'u>` | as above, with the places the step needs |
| `Control.select label [ name, value; … ] init` | the chosen value | its name |
| `Control.checkbox label init` | `bool` | `on` or `off` |

A slider's witness is part of its type, so a slider over kelvin passed to a view over seconds does
not compile. The block is applicative (`let! … and! …`, then `return`): a control cannot be
chosen by another control's value, so the set of controls is known before the view runs.

## A live figure is a value

```fsharp
let s0 = Live.init live
let s1 = Live.step live s0 (Set(0, 3.7))       // the reader moved the slider to 3.7 Hz
let fig = Live.view live s1                    // the figure at that state
```

`Live.step` is pure and total. A message that names no control, or carries no usable value,
returns the state unchanged. A host drives it with the reader's messages. A session therefore
replays from its messages, and a test needs no notebook.

The figure is laid out once for the whole widget, over the states its controls reach at their
extremes, so the plot area does not move while the reader drags. A state that still does not fit
grows the arrangement, and `LiveState.Grown` records each one.

## Zoom, pan and a readout

A host sends the reader's pointer as messages in the figure's px, and the step answers them like
any other:

```fsharp
let panel = (Live.panels live s1).[0]
let area = { X0 = panel.Plot.X0 + 40.0<px>; Y0 = panel.Plot.Y0 + 20.0<px>; X1 = panel.Plot.X0 + 200.0<px>; Y1 = panel.Plot.Y0 + 120.0<px> }

let zoomed = Live.step live s1 (Zoom area)      // the panel's domains become what the box covered
let back = Live.step live zoomed Reset          // and back
let reading = Live.step live zoomed (Hover { X = area.X0; Y = area.Y0 }) |> Live.readout live
```

The box is read back through the panel's own scales, so a log axis zooms by decades and a phase
space by its own units. `Pan` slides the domains, and a log axis pans by a factor. A reading is one
line per axis in the prefix and unit the axis's ticks are written in, to the places one px
resolves. A hover changes no figure, and `Live.drawsAlike` says so, so a host does not redraw for
one.

## Recording

`Live.recorder live` displays as `live` and keeps every message the host steps, with the page's
own times. `Recorder.stop` hands back the log, `Live.record` replays it through the step, and
`Live.film` makes the states a `Film`, laid out over every state the session reached. `LiveLog`
writes a log as JSON and reads it back exactly.

## The document

`Html.widget live state isLive` writes the whole page: the controls at the state's values, the
figure, and a script that posts each change and swaps in the reply, or only the elements that
changed when that is at most half the figure. The script formats no number: every label is written
by the kernel. With `isLive = false` the controls are disabled.
That form suits a host with nothing to talk back to, and a saved notebook.

Depends on `Marey.Core`, `Marey.Units` and `Marey.Plot`. `netstandard2.1`, no other dependencies.
