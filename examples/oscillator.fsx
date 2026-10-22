(*  A damped oscillator: model against measurement, with uncertainties.

    Run it:  dotnet fsi examples/oscillator.fsx
    Writes:  oscillator.svg in the current directory.

    This is the shape most scientific figures actually have — a smooth curve you predicted, a
    handful of points you measured, and error bars saying how much you trust them. It shows
    layering, per-layer naming and styling, and the legend that names only the layers you named.

    The measurements here are **synthetic**: a deterministic pseudo-random wobble added to the
    model, seeded so the figure is byte-identical on every run. Nothing is being claimed about a
    real oscillator; the two datasets on real measurements are `co2-record.fsx` and
    `temperature-anomaly.fsx`. *)

#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Svg"

open Marey
open Marey.UnitsOfMeasure

// x(t) = A·e^(−t/τ)·cos(ωt) — a mass on a spring with light damping.
let amplitude = 12.0e-3<m>
let tau = 1.8<s>
let omega = 2.0 * System.Math.PI * 1.4<1/s>

let model (t: float<s>) = amplitude * exp (-t / tau) * cos (omega * t)

let ts = Array.init 400 (fun i -> float i * 0.0125<s>)
let xs = ts |> Array.map model

// A deterministic wobble. A real `System.Random` would make the output differ run to run, and
// this library's whole test story rests on renders being reproducible.
let mutable seed = 20260819

let noise () =
    seed <- (1103515245 * seed + 12345) &&& 0x7FFFFFFF
    float seed / 2147483647.0 - 0.5

let sampleT = Array.init 14 (fun i -> float i * 0.35<s>)
let sampleX = sampleT |> Array.map (fun t -> model t + noise () * 1.1e-3<m>)
let sigma = sampleT |> Array.map (fun _ -> 0.6e-3<m>)

let figure =
    Chart.lineIn U.second U.metre ts xs
    |> Chart.withName "model"
    |> Chart.addErrorbarIn U.second U.metre sampleT sampleX sigma
    |> Chart.withName "measured"
    |> Chart.withLegend LegendTopRight
    |> Chart.withGrid true true
    |> Chart.withTitle "Damped oscillator"
    |> Chart.withSubtitle "model against measurement"

let out = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "oscillator.svg")
figure |> Chart.toSvg SvgBackend.render out (Chart.size 800.0 500.0)

printfn "wrote %s" out

let svg = figure |> Chart.render SvgBackend.render (Chart.size 800.0 500.0)

// Displacement is in the low tens of millimetres, so the y axis says so without being asked.
printfn "y axis chose millimetres: %b" (svg.Contains "[mm]")

// `withName` names the layer most recently added, and an unnamed layer stays out of the legend.
printfn "legend names the model:   %b" (svg.Contains "model")
printfn "legend names the data:    %b" (svg.Contains "measured")

// Two renders of one figure are byte-identical. That is what makes the golden suite meaningful,
// and it is why the wobble above is seeded rather than random.
printfn "deterministic:            %b" (svg = (figure |> Chart.render SvgBackend.render (Chart.size 800.0 500.0)))
