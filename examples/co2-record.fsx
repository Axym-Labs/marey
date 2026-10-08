(*  The Mauna Loa CO₂ record — real measurements, real uncertainties, and a unit SI does not have.

    Run it:  dotnet fsi examples/co2-record.fsx
    Writes:  co2-record.svg in the current directory.

    Data:    examples/data/co2-mauna-loa.csv — NOAA Global Monitoring Laboratory, annual means
             from the Mauna Loa Observatory. A work of the US Government, not subject to
             copyright. The file carries NOAA's own header, including the note that the 2022-23
             observations came from Maunakea while Mauna Loa was erupting.

    This example exists to be honest about where the units story stops.

    A CO₂ mole fraction in ppm is **dimensionless** — micromoles per mole, and the moles cancel.
    There is no SI unit to derive an axis title from, no prefix to select, and nothing for the
    compiler to check. Marey renders a dimensionless axis with no unit brackets at all rather than
    printing `[1]`, and you write the label yourself. That is the right answer, and pretending
    otherwise would be the library overselling itself. *)

#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Svg"

open Marey
open System.Globalization

let dataPath =
    System.IO.Path.Combine(__SOURCE_DIRECTORY__, "data", "co2-mauna-loa.csv")

/// NOAA's file is a block of `#` comment lines, then `year,mean,unc`.
let rows =
    System.IO.File.ReadAllLines dataPath
    |> Array.filter (fun l -> not (l.StartsWith "#") && l <> "" && not (l.StartsWith "year"))
    |> Array.map (fun l ->
        let parts = l.Split ','
        let f (s: string) = System.Double.Parse(s, CultureInfo.InvariantCulture)
        f parts.[0], f parts.[1], f parts.[2])

let years = rows |> Array.map (fun (y, _, _) -> y)
let mean = rows |> Array.map (fun (_, m, _) -> m)
let uncertainty = rows |> Array.map (fun (_, _, u) -> u)

printfn "%d annual means, %g to %g" rows.Length (Array.head years) (Array.last years)

// No unit is stated on either axis, so neither claims one: no bracket, no prefix, and the labels
// are yours because there is no unit to derive them from.
let figure =
    Chart.Errorbar(years, mean, uncertainty)
    |> Chart.withName "annual mean ±1σ"
    |> Chart.withXLabel "year"
    |> Chart.withYLabel "CO₂ mole fraction [ppm]"
    |> Chart.withLegend LegendTopLeft
    |> Chart.withGrid false true
    |> Chart.withTitle "Atmospheric CO₂ at Mauna Loa"
    |> Chart.withSubtitle "NOAA Global Monitoring Laboratory, annual means"

let out = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "co2-record.svg")
figure |> Chart.toSvg SvgBackend.render out (Chart.size 820.0 480.0)

printfn "wrote %s" out

let svg = figure |> Chart.render SvgBackend.render (Chart.size 820.0 480.0)

// The label is the one written above, and there are no unit brackets attached to it: a
// dimensionless axis renders as the empty unit, not as `[1]`.
printfn "y label is the one supplied: %b" (svg.Contains "CO₂ mole fraction [ppm]")
printfn "no bogus [1] unit bracket:   %b" (not (svg.Contains "[1]"))

// A dimensionless axis is also the one case where Marey refuses to rescale the values. There is
// no unit to hang a prefix or a 10ⁿ multiplier on, so silently dividing the numbers through
// would be a lie with nowhere to record it — the ticks stay in ppm as measured.
printfn "ticks left in ppm:           %b" (svg.Contains "420" || svg.Contains "400")
