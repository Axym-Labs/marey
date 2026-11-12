(*  g from a pendulum, with its uncertainty — a lab report's arithmetic, done by the types.

    Run it:  dotnet fsi examples/pendulum-uncertainty.fsx
    Writes:  pendulum-uncertainty.svg in the current directory.

    Five lengths read off a tape marked in millimetres, each timed five times over ten swings
    with one stopwatch, whose calibration certificate states 0.02 s at k = 2. Every reading is an
    input with its own standard uncertainty; the stopwatch's calibration is one input shared by
    all twenty-five timings. The arithmetic carries each input's contribution to the end, so g
    comes with its budget, its degrees of freedom and an expanded uncertainty whose k says what
    it rests on.

    Then the slope of T² against L, a weighted fit whose slope is an uncertain value like any
    other, and a Monte Carlo check (JCGM 101) of the one quantity in the lab that the law of
    propagation gets wrong: how high the bob starts, from an angle read as "between 0 and 0.1
    rad". Its first-order interval goes below the bob's lowest point. *)

#r "nuget: Marey.Uncertainty"
#r "nuget: Marey.Backend.Svg"

open System
open Marey
open Marey.UnitsOfMeasure

let stopwatch = Uncertain.certificate "Stopwatch calibration" U.second 0.0<s> 0.02<s> 2.0

let lengths =
    [| 0.400<m>; 0.600<m>; 0.800<m>; 1.000<m>; 1.200<m> |]
    |> Array.mapi (fun i l -> Uncertain.resolution (sprintf "Length %d" (i + 1)) U.metre l 1e-3<m>)

let timingsOfTen =
    [| [ 12.692<s>; 12.642<s>; 12.712<s>; 12.662<s>; 12.652<s> ]
       [ 15.576<s>; 15.526<s>; 15.596<s>; 15.546<s>; 15.536<s> ]
       [ 17.951<s>; 17.901<s>; 17.971<s>; 17.921<s>; 17.911<s> ]
       [ 20.099<s>; 20.049<s>; 20.119<s>; 20.069<s>; 20.059<s> ]
       [ 21.978<s>; 21.928<s>; 21.998<s>; 21.948<s>; 21.938<s> ] |]

let periods =
    timingsOfTen
    |> Array.mapi (fun i timings -> (Uncertain.ofReadings (sprintf "Ten periods %d" (i + 1)) U.second timings + stopwatch) / 10.0)

// ---- One length -------------------------------------------------------------------------

let gOf (l: Uncertain<m>) (t: Uncertain<s>) = 4.0 * Math.PI ** 2.0 * l / (t * t)
let gOne = gOf lengths.[2] periods.[2]

printfn "g at 0.8 m: %s" (Uncertain.report gOne)
printfn "%s\n" (Budget.text (Uncertain.budget gOne))
printfn "%s" (Uncertain.reportExpanded (Uncertain.expanded 0.95 gOne))

// ---- The line through T² against L ------------------------------------------------------

let squares = periods |> Array.map (fun t -> t * t)
let line = Fit.line lengths squares
let gLine = 4.0 * Math.PI ** 2.0 / line.Slope |> Uncertain.convert U.metrePerSecondSquared

printfn "\nslope %s, intercept %s, χ²/ν = %.2f" (Uncertain.report line.Slope) (Uncertain.report line.Intercept) line.ReducedChiSquared
printfn "g from the slope: %s" (Uncertain.report gLine)
printfn "%s\n" (Budget.text (Uncertain.budget gLine))

let residuals = Array.map2 (fun (l: Uncertain<m>) (t2: Uncertain<s^2>) -> t2 - (line.At l.Value).Value) lengths squares

let figure =
    Chart.errorbars lengths squares
    |> Chart.addFitBand line
    |> Chart.withYLabel "T²"
    |> Chart.withLegend LegendTopLeft
    |> Chart.withTitle "g from a pendulum"
    |> Chart.withSubtitle (sprintf "g = %s from the slope of T² against L" (Uncertain.report gLine))
    |> Chart.above (
        Chart.errorbars lengths residuals
        |> Chart.addHLine (U.square U.second) 0.0<s^2>
        |> Chart.withXLabel "L"
        |> Chart.withYLabel "residual"
        |> Chart.withLegend LegendNone
    )
    |> Chart.shareX

let out = IO.Path.Combine(IO.Directory.GetCurrentDirectory(), "pendulum-uncertainty.svg")
figure |> Chart.toSvg SvgBackend.render out (Chart.size 800.0 580.0)
printfn "wrote %s\n" out

// ---- Where first order fails ------------------------------------------------------------

let millimetre = U.scaled "mm" 1e-3 U.metre
let angle = Uncertain.rectangular "Release angle" U.radian 0.05<rad> 0.05<rad>
let height (l: Uncertain<m>) (a: Uncertain<rad>) = l * (1.0 - Uncertain.cos a) |> Uncertain.convert millimetre

let release = MonteCarlo.model2 height lengths.[2] angle |> MonteCarlo.check
printfn "%s\n" (MonteCarlo.report release)

// What the lab claims, as checks.
let within (x: Uncertain<'u>) (y: Uncertain<'u>) = abs (float (x.Value - y.Value)) < 2.0 * float (x.Uncertainty + y.Uncertainty)
printfn "the two g agree:                       %b" (within gOne gLine)
printfn "the intercept is zero within its u:    %b" (abs (float line.Intercept.Value) < 2.0 * float line.Intercept.Uncertainty)
printfn "the stopwatch is one row of g's budget:%b" ((Uncertain.budget gLine).Rows |> Array.filter (fun r -> r.Input.Label = "Stopwatch calibration") |> Array.length = 1)
printfn "first order puts the bob below zero:   %b" (float release.LinearInterval.Estimate - float release.LinearInterval.Expanded < 0.0)
printfn "no trial does:                         %b" (float release.Low >= 0.0)
printfn "so the Monte Carlo interval stands:    %b" (not release.Validated)
