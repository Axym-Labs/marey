/// D130 — the 27 SVG goldens under Node; M5's gates 13 and 14, the canvas backend's call logs
/// and the fonts, which need Node for the same reason; and M5.5's gate 10, the simulations.
///
/// Fable compiles `GoldenScenes`, the same file the .NET suite renders its baselines from, and
/// the seven projects below it; Node runs the result and compares each document with its file in
/// `tests/Marey.Tests/golden/`, byte for byte. A difference writes `<name>.actual.svg` beside the
/// baseline, as `Golden.check` does, and sets a failing exit code.
///
///     dotnet tool restore
///     dotnet fable tests/Marey.Fable -o tests/Marey.Fable/out
///     node tests/Marey.Fable/out/Main.js tests/Marey.Tests/golden tests/Marey.Fable/canvas
module Marey.Fable.Main

open Fable.Core
open Marey.Tests

[<Import("readFileSync", "node:fs")>]
let private readFileSync (_path: string, _encoding: string) : string = jsNative

[<Import("writeFileSync", "node:fs")>]
let private writeFileSync (_path: string, _text: string) : unit = jsNative

[<Emit("process.argv[$0]")>]
let private argument (_index: int) : string = jsNative

[<Emit("process.exitCode = $0")>]
let private setExitCode (_code: int) : unit = jsNative

let private normalise (s: string) = s.Replace("\r\n", "\n").Replace("\r", "\n")

/// The first line that differs, with the line on each side, so a failure in the log is readable
/// without opening the files.
let private firstDifference (expected: string) (actual: string) =
    let e = expected.Split '\n'
    let a = actual.Split '\n'
    let n = min e.Length a.Length
    let i = Seq.tryFind (fun i -> e.[i] <> a.[i]) (seq { 0 .. n - 1 }) |> Option.defaultValue n
    let line (lines: string[]) = if i < lines.Length then lines.[i] else "<end of document>"
    sprintf "line %d\n    expected: %s\n    actual:   %s" (i + 1) (line e) (line a)

let private dir =
    match argument 2 with
    | null
    | "" -> "tests/Marey.Tests/golden"
    | d -> d

let private canvasDir =
    match argument 3 with
    | null
    | "" -> "tests/Marey.Fable/canvas"
    | d -> d

let private failures =
    [ for name, render in GoldenScenes.svg do
          let expected = normalise (readFileSync (dir + "/" + name + ".svg", "utf8"))
          let actual = normalise (render ())

          if actual = expected then
              printfn "  ok    %s" name
          else
              writeFileSync (dir + "/" + name + ".actual.svg", actual)
              printfn "  FAIL  %s, %s" name (firstDifference expected actual)
              yield name ]

printfn "%d of %d SVG goldens byte-identical under Node" (GoldenScenes.svg.Length - failures.Length) GoldenScenes.svg.Length

// Gate 13: the canvas backend's call log per golden scene.
let private canvasFailures = CanvasLog.check canvasDir
let private canvasCount = (GoldenScenes.scenesWith Marey.TextMeasure.approx).Length
printfn "%d of %d canvas call logs identical" (canvasCount - canvasFailures.Length) canvasCount

// Gate 14: no layout against a fallback face.
let private fontFailures = FontsGate.check ()

for f in fontFailures do
    printfn "  FAIL  fonts: %s" f

printfn "the fonts gate: %s" (if fontFailures.IsEmpty then "holds" else "fails")

// M5.5 gate 10: the simulations, printed to twelve decimals, against the file .NET checks too.
let private simFailures =
    let expected = normalise (readFileSync (dir + "/sim-runs.txt", "utf8"))
    let actual = normalise (SimRuns.text ())

    if actual = expected then
        printfn "the simulations: identical to .NET's, %d lines" (expected.Split('\n').Length - 1)
        []
    else
        writeFileSync (dir + "/sim-runs.actual.txt", actual)
        printfn "  FAIL  the simulations, %s" (firstDifference expected actual)
        [ "sim-runs" ]

if not (failures.IsEmpty && canvasFailures.IsEmpty && fontFailures.IsEmpty && simFailures.IsEmpty) then
    setExitCode 1
