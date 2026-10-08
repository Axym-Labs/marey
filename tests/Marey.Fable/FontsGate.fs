/// Gate 14 — no layout against a fallback face (D123). Node has no `document`, so the gate puts
/// one in place: a `document.fonts` that is still loading, then one that has finished, and an
/// `OffscreenCanvas` whose `measureText` answers in a face of its own. Before the fonts arrive the
/// canvas measurer refuses and `WebText.measurerNow` hands back `ApproxTextMeasurer`, saying so;
/// after, it hands back the canvas's measurer, and a figure laid out with it is laid out in the
/// canvas's face, which the approximation is not.
module Marey.Fable.FontsGate

open Fable.Core
open Marey
open Marey.Tests

[<Emit("globalThis.document = { fonts: { status: $0, ready: Promise.resolve() } }")>]
let private setFontsStatus (_status: string) : unit = jsNative

/// A canvas whose every glyph is 7 px wide with a 9 px ascent and a 3 px descent: wider than the
/// approximation's sans-serif, so a layout in it is visibly not one in the approximation.
[<Emit("""globalThis.OffscreenCanvas = class {
  constructor(w, h) { this.width = w; this.height = h; }
  getContext() {
    return { font: '', measureText: (t) => ({ width: 7 * t.length, fontBoundingBoxAscent: 9, fontBoundingBoxDescent: 3 }) };
  }
}""")>]
let private installCanvas () : unit = jsNative

[<Emit("delete globalThis.document; delete globalThis.OffscreenCanvas")>]
let private restore () : unit = jsNative

let private font: FontSpec =
    { Family = "Inter, sans-serif"
      Size = 9.0<pt>
      Weight = 400
      Italic = false }

/// The failures, by description; empty when the gate holds.
let check () : string list =
    installCanvas ()
    setFontsStatus "loading"
    let early = WebText.measurerNow ()

    let refused =
        try
            (CanvasTextMeasurer() :> ITextMeasurer).Measure font "x" |> ignore
            false
        with _ ->
            true

    let earlyScene = Chart.toSceneWith early.Measurer GoldenScenes.figureSurface GoldenScenes.figure

    setFontsStatus "loaded"
    let late = WebText.measurerNow ()
    let measured = (CanvasTextMeasurer() :> ITextMeasurer).Measure font "label"
    let lateScene = Chart.toSceneWith late.Measurer GoldenScenes.figureSurface GoldenScenes.figure
    restore ()

    [ if not refused then "the canvas measurer measured while the fonts were loading"
      if early.Approximated.IsNone then "a measurer handed out while the fonts were loading did not say it was approximate"
      if not (obj.ReferenceEquals(early.Measurer, TextMeasure.approx)) then "the measurer handed out while loading is not the approximation"
      if late.Approximated.IsSome then "the measurer handed out once the fonts were in says it is approximate"
      if measured.Advance <> 35.0<px> || measured.Ascent <> 9.0<px> || measured.Descent <> 3.0<px> then
          sprintf "the canvas measurer read %A, not the canvas's 35, 9 and 3" measured
      if earlyScene = lateScene then "a figure laid out in the canvas's face is the approximation's figure" ]
