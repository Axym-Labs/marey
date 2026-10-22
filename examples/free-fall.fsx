(*  Free fall — the smallest example that shows what Marey is for.

    Run it:  dotnet fsi examples/free-fall.fsx
    Writes:  free-fall.svg in the current directory.

    The point of this example is what is *absent* from it. Nothing configures the axes. The
    scale, the domain, the SI prefix, the tick values, the tick labels and both axis titles are
    all derived from the two unit witnesses handed to `Chart.line`. *)

#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Svg"

open Marey
open Marey.UnitsOfMeasure

// Elapsed time, and the displacement of a body released from rest.
//
// `x` is never annotated. `0.5 * 9.81<m/s^2> * t * t` has type `float<m>` because the compiler
// works it out — which is also why plotting `t` against `t` on these axes would not compile.
let t = Array.init 24 (fun i -> float i * 0.4<s>)
let x = t |> Array.map (fun t -> 0.5 * 9.81<m/s^2> * t * t)

let figure =
    Chart.lineIn U.second U.metre t x
    |> Chart.withTitle "Free fall"
    |> Chart.withSubtitle "displacement against elapsed time"

let out = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "free-fall.svg")
figure |> Chart.toSvg SvgBackend.render out (Chart.size 800.0 500.0)

printfn "wrote %s" out

// The axis titles came from the witnesses, not from a string anyone typed.
let svg = figure |> Chart.render SvgBackend.render (Chart.size 800.0 500.0)
printfn "x axis reads 't [s]':  %b" (svg.Contains "t [s]")
printfn "y axis reads 'x [m]':  %b" (svg.Contains "x [m]")
