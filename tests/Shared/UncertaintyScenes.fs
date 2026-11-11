/// The figures behind the uncertainty baselines in `golden/uncertainty/`, shared by the .NET
/// suite and the Fable project so that Node draws the same figures .NET does.
module Marey.Tests.UncertaintyScenes

open Marey
open Marey.UnitsOfMeasure

let surface: Extent<px> = Chart.size 480.0 300.0

/// A spring: five extensions read off a ruler, and the force at each from three readings.
let private spring () =
    let centimetre: U<m> = U.scaled "cm" 0.01 U.metre

    let extension =
        [| 1.1; 2.0; 3.2; 3.9; 5.1 |]
        |> Array.mapi (fun i x -> Uncertain.standard (sprintf "Extension %d" (i + 1)) centimetre (x * 1.0<m>) 0.15<m>)

    let force =
        [| [ 0.52; 0.61; 0.55 ]; [ 0.98; 1.07; 1.01 ]; [ 1.55; 1.69; 1.62 ]; [ 1.90; 2.04; 1.94 ]; [ 2.49; 2.66; 2.58 ] |]
        |> Array.mapi (fun i readings -> Uncertain.ofReadings (sprintf "Force %d" (i + 1)) U.newton (readings |> List.map (fun f -> f * 1.0<N>)))

    extension, force

/// Bars on both axes, one standard uncertainty either side.
let errorbars () : Figure =
    let extension, force = spring ()
    Chart.errorbars extension force |> Chart.withTitle "A spring" |> Chart.withXLabel "extension" |> Chart.withYLabel "force"

/// The same values with 95 % bars, each from its own degrees of freedom.
let coverage () : Figure =
    let extension, force = spring ()

    Chart.errorbarsWith (ErrorBars.Coverage 0.95) extension force
    |> Chart.withTitle "A spring"
    |> Chart.withXLabel "extension"
    |> Chart.withYLabel "force"

/// Two named series over an exact x, the second added to the first.
let added () : Figure =
    let t = U.second.Of [ 1.0<s>; 2.0<s>; 3.0<s>; 4.0<s> ]
    let run (offset: float) = [| 0.9; 2.1; 2.9; 4.2 |] |> Array.map (fun v -> Uncertain.standard "" U.metre ((v + offset) * 1.0<m>) 0.2<m>)

    Chart.errorbars (Uncertain.exactly t) (run 0.0)
    |> Chart.withName "run 1"
    |> Chart.addErrorbars (Uncertain.exactly t) (run 1.5)
    |> Chart.withName "run 2"

/// The spring's line, fitted with both axes' uncertainties, over its points: the band is the
/// line's standard uncertainty.
let fitted () : Figure =
    let extension, force = spring ()

    Chart.errorbars extension force
    |> Chart.addFitBand (Fit.line extension force)
    |> Chart.withTitle "A spring and its line"
    |> Chart.withXLabel "extension"
    |> Chart.withYLabel "force"

/// JCGM 101's additive model with one rectangular input ten times wider than the rest (9.2.4):
/// the output is near rectangular, and the law of propagation's normal interval too long.
let monteCarlo () : Figure =
    let x i (u: float) = Uncertain.rectangular (sprintf "X%d" i) U.one 0.0 (u * sqrt 3.0)

    MonteCarlo.model4 (fun a b c d -> a + b + c + d) (x 1 1.0) (x 2 1.0) (x 3 1.0) (x 4 10.0)
    |> MonteCarlo.checkWith { MonteCarlo.defaults with Trials = 20_000; Adaptive = false }
    |> Chart.monteCarlo
    |> Chart.withTitle "One wide rectangular input"
    |> Chart.withXLabel "y"
    |> Chart.withYLabel "trials"

type Reading =
    { Run: string
      Extension: Uncertain<m>
      Force: Uncertain<N> }

/// The spring in two runs as rows, through the block: uncertain accessors, a colour and a fit.
let block () : Figure =
    let extension, force = spring ()

    let rows =
        [ for i in 0 .. extension.Length - 1 do
              yield { Run = "first"; Extension = extension.[i]; Force = force.[i] }
              yield { Run = "second"; Extension = extension.[i] + 0.4<m>; Force = force.[i] * 0.8 } ]

    plot rows {
        xUncertain (fun r -> r.Extension)
        yUncertain (fun r -> r.Force)
        color (fun r -> r.Run)
        fit
    }

type Sample = { Day: string; Depth: float<m>; Spread: float<m>; Time: float<s> }

/// Plot's own error channels, with no uncertain value in sight: millimetre bars on a metre
/// axis, faceted, labelled by hand.
let channels () : Figure =
    let millimetre: U<m> = U.scaled "mm" 1e-3 U.metre

    let rows =
        [ for day, shift in [ "Monday", 0.0; "Tuesday", 0.25 ] do
              for i in 0..4 ->
                  { Day = day
                    Depth = (0.4 + shift + 0.3 * float i) * 1.0<m>
                    Spread = (40.0 + 25.0 * float i) * 1.0<m>
                    Time = float i * 1.0<s> } ]

    plot rows {
        x U.second (fun r -> r.Time)
        y U.metre (fun r -> r.Depth)
        yError millimetre (fun r -> r.Spread)
        errorLabel "standard deviation"
        layer GeomLine
        layer GeomPoint
        facetWrap (fun r -> r.Day)
    }

let svg: (string * (unit -> string)) list =
    [ "errorbars", (fun () -> Chart.render SvgBackend.render surface (errorbars ()))
      "coverage", (fun () -> Chart.render SvgBackend.render surface (coverage ()))
      "added", (fun () -> Chart.render SvgBackend.render surface (added ()))
      "block", (fun () -> Chart.render SvgBackend.render surface (block ()))
      "error-channels", (fun () -> Chart.render SvgBackend.render surface (channels ()))
      "fit", (fun () -> Chart.render SvgBackend.render surface (fitted ()))
      "monte-carlo", (fun () -> Chart.render SvgBackend.render surface (monteCarlo ())) ]
