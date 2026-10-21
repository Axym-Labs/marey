/// Step F, D157 and D159 — what a notebook page draws a scene with.
///
/// The bundle esbuild makes of this file is a classic script that defines one global,
/// `MareyView`, so the widget's own script, which runs in document order, can call it at once.
/// The page holds one scene: the one it was shown last, with the text metrics its layout used
///, and it redraws the whole canvas on every change, because a canvas has no element to
/// replace.
module Marey.View

open System
open Marey

type private Shown =
    { Canvas: CanvasElement
      Scene: Node
      Metrics: TextTable
      Extent: Extent<px> }

let mutable private shown: Shown option = None

let private redraw (s: Shown) =
    CanvasBackend.Draw(Canvas.context s.Canvas, s.Extent, s.Scene, measurer = s.Metrics, webgl = true)

/// **Show a scene** on `canvas` at `width` × `height` px: its bytes from `Wire.encodeWith`, as
/// base64. `""` when it is drawn, and otherwise what is wrong with the bytes.
let show (canvas: CanvasElement) (base64: string) (width: float) (height: float) : string =
    match Wire.decodeWith (Convert.FromBase64String base64) with
    | Error why -> why
    | Ok(scene, metrics) ->
        let s =
            { Canvas = canvas
              Scene = scene
              Metrics = metrics
              Extent = { Width = width * 1.0<px>; Height = height * 1.0<px> } }

        shown <- Some s
        redraw s
        ""

/// **Apply a patch** to the scene shown: its bytes from `Wire.encodePatchWith`, as base64.
/// `false` when nothing is shown, the bytes do not decode, or the patch replaces a key the scene
/// does not hold. Then the page and the kernel disagree about what is drawn, and the page asks
/// for the whole scene again (D103's resync).
let patch (base64: string) : bool =
    match shown, Wire.decodePatchWith (Convert.FromBase64String base64) with
    | Some s, Ok(Whole scene, metrics) ->
        let next = { s with Scene = scene; Metrics = metrics }
        shown <- Some next
        redraw next
        true
    | Some s, Ok(Replace parts as p, metrics) when parts |> Array.forall (fun (k, _) -> (Scene.tryFindKey k s.Scene).IsSome) ->
        let next =
            { s with
                Scene = Scene.apply p s.Scene
                Metrics = s.Metrics.Merge metrics }

        shown <- Some next
        redraw next
        true
    | _ -> false
