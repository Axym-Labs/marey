/// Step F, phase 2 — the codec under Node (`wire-plan.md` §4). For every golden scene, against
/// the bytes .NET committed under `golden/wire/`:
///
/// * gate 1, Node's half: the scene compiled here comes back from the wire as itself;
/// * gate 2: .NET's bytes decode here and re-encode to the same bytes, so the two codecs agree
///   about padding, strings and float bits on one scene;
/// * gate 3: the scene decoded from .NET's bytes draws the canvas call log gate 13 committed;
/// * D156: the scene's text metrics come back, and the table answers as the layout's measurer did.
///
/// **Gate 2 compares one scene, not two.** The plan had Fable encode the scene it compiles itself
/// and match .NET's bytes. Six goldens cannot: their geometry goes through `sin` and `cos`, which
/// differ between Node and .NET in the last bit (D141), so the two runtimes compile two scenes
/// that agree to the three decimals every golden prints and not to the bit. The kernel compiles,
/// and the page decodes and never recomputes, so that is not the wire's concern. Those six are
/// named below, so a sixth fails here rather than passing quietly.
module Marey.Fable.WireGate

open Fable.Core
open Marey
open Marey.Tests

/// A fresh `Uint8Array` over the file, at offset 0 of its own buffer. Node hands small files out
/// of a shared pool at an arbitrary offset, and a decoder is entitled to an aligned buffer (D153).
[<Import("readFileSync", "node:fs")>]
let private readBuffer (_path: string) : obj = jsNative

[<Emit("new Uint8Array($0)")>]
let private copy (_buffer: obj) : byte[] = jsNative

let private readBytes (path: string) : byte[] = copy (readBuffer path)

[<Import("existsSync", "node:fs")>]
let private existsSync (_path: string) : bool = jsNative

[<Import("readFileSync", "node:fs")>]
let private readFileSync (_path: string, _encoding: string) : string = jsNative

let private firstDifference (a: byte[]) (b: byte[]) =
    let n = min a.Length b.Length
    let i = Seq.tryFind (fun i -> a.[i] <> b.[i]) (seq { 0 .. n - 1 }) |> Option.defaultValue n
    let window (x: byte[]) = [ for k in max 0 (i - 12) .. min (x.Length - 1) (i + 12) -> sprintf "%02x" x.[k] ] |> String.concat " "
    sprintf "first difference at byte %d of %d (Fable wrote %d)\n      .NET  %s\n      Fable %s" i a.Length b.Length (window a) (window b)

/// The goldens whose scene Node compiles differently in the last bit of a coordinate, from
/// `sin` and `cos` (D141): the star and the arc in `paths` and `clipping`, the fields sampled
/// from trigonometric functions, and the circle and the wedge `figure-rules` lays across a polar
/// panel.
let private platformTrig =
    set [ "paths"; "clipping"; "figure-field-heatmap"; "figure-field-quiver"; "figure-field-streamlines"; "figure-rules" ]

/// The names that fail any of the three, with why.
let check (goldenDir: string) (canvasDir: string) : string list =
    [ for name, extent, node in GoldenScenes.scenesWith TextMeasure.approx do
          let path = goldenDir + "/wire/" + name + ".mrys"

          if not (existsSync path) then
              printfn "  FAIL  wire %s, no baseline" name
              yield name
          else
              let committed = readBytes path
              let own = Wire.encode node

              let problems =
                  [ match Wire.decode own with
                    | Ok back when back = node -> ()
                    | Ok _ -> yield "the scene compiled here does not come back as itself"
                    | Error why -> yield sprintf "the scene compiled here does not decode: %s" why

                    match Wire.decode committed with
                    | Error why -> yield sprintf ".NET's bytes do not decode: %s" why
                    | Ok decoded ->
                        let again = Wire.encode decoded

                        if again <> committed then
                            yield "re-encoding .NET's scene: " + firstDifference committed again

                        let log = CanvasLog.logOf (extent, decoded)
                        let logPath = canvasDir + "/" + name + ".canvas.txt"

                        if not (existsSync logPath) || (readFileSync (logPath, "utf8")).Replace("\r\n", "\n") <> log then
                            yield "draws a different canvas log"

                    // D156 under Node: the metrics travel, and the table answers each run as the
                    // layout's measurer did.
                    match Wire.decodeWith (Wire.encodeWith TextMeasure.approx node) with
                    | Error why -> yield sprintf "with its text metrics, does not decode: %s" why
                    | Ok(back, table) ->
                        if back <> node then
                            yield "with its text metrics, does not come back as itself"

                        let m = table :> ITextMeasurer

                        if table.Entries |> Array.exists (fun (f, t, _) -> m.Measure f t <> TextMeasure.approx.Measure f t) then
                            yield "its text table answers a run otherwise than the measurer did"

                    match own = committed, platformTrig.Contains name with
                    | false, false -> yield "compiles here to other bytes than .NET's: " + firstDifference committed own
                    | true, true -> yield "now compiles here to .NET's bytes; take it off the platform-trig list"
                    | _ -> () ]

              if problems.IsEmpty then
                  printfn "  ok    wire %s" name
              else
                  printfn "  FAIL  wire %s: %s" name (String.concat "; " problems)
                  yield name ]
