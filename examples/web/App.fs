/// A figure compiled in the browser by the same compiler the kernel runs, drawn on a canvas.
///
/// A damped oscillation whose frequency and damping follow two range inputs. Every change runs
/// the whole pipeline in the page: the figure is built, laid out against the canvas's own text
/// measurer once the fonts have arrived, and drawn. No server is asked anything.
module Marey.Example.Web.App

open System
open Fable.Core
open Marey
open Marey.UnitsOfMeasure

[<Emit("document.getElementById($0)")>]
let private element (_id: string) : obj = jsNative

[<Emit("Number($0.value)")>]
let private valueOf (_input: obj) : float = jsNative

[<Emit("$0.addEventListener('input', $1)")>]
let private onInput (_input: obj) (_handler: unit -> unit) : unit = jsNative

[<Emit("$0.textContent = $1")>]
let private setText (_element: obj) (_text: string) : unit = jsNative

[<Emit("performance.now()")>]
let private now () : float = jsNative

/// The figure for a frequency and a damping rate: a model curve and samples of it, in metres,
/// which the axis labels in millimetres because that is where the numbers are.
let figure (f: float<1 / s>) (gamma: float<1 / s>) : Figure =
    let t = Array.init 400 (fun i -> float i * 0.005<s>)
    let x = t |> Array.map (fun t -> 0.012<m> * exp (-gamma * t) * cos (2.0 * Math.PI * f * t))
    let ts = Array.init 25 (fun i -> float i * 0.08<s>)
    let xs = ts |> Array.map (fun t -> 0.012<m> * exp (-gamma * t) * cos (2.0 * Math.PI * f * t))

    Chart.line U.second U.metre t x
    |> Chart.overlay (Chart.scatter U.second U.metre ts xs)
    |> Chart.withTitle "A damped oscillator, compiled in the browser"

let private canvas = Canvas.byId "plot"
let private ctx = Canvas.context canvas
let private status = element "status"
let private frequency = element "f"
let private damping = element "gamma"
let private extent = Chart.size 720.0 450.0

let private draw (m: WebMeasurer) =
    let started = now ()
    let fig = figure (valueOf frequency * 1.0<1 / s>) (valueOf damping * 1.0<1 / s>)
    let scene = Chart.toSceneWith m.Measurer extent fig
    CanvasBackend.draw m.Measurer ctx extent scene

    let took = Num.at 1 (now () - started)

    setText
        status
        (match m.Approximated with
         | None -> sprintf "Compiled and drawn in %s ms." took
         | Some why -> sprintf "Compiled and drawn in %s ms, approximated: %s." took why)

// Drawn at once, approximated if the fonts are still on their way and saying so, then again with
// the canvas's measurer when they have arrived, and on every move of either slider after that.
draw (WebText.measurerNow ())

WebText.measurer ()
|> fun p ->
    p.``then`` (fun measurer ->
        let m = { Measurer = measurer; Approximated = None }
        draw m
        onInput frequency (fun () -> draw m)
        onInput damping (fun () -> draw m))
|> ignore
