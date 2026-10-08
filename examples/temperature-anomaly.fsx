(*  Global temperature anomaly — real data, a real SI unit, and why it is not degrees Celsius.

    Run it:  dotnet fsi examples/temperature-anomaly.fsx
    Writes:  temperature-anomaly.svg and temperature-anomaly.tex in the current directory.

    Data:    examples/data/gistemp-annual.csv — NASA GISS Surface Temperature Analysis (GISTEMP
             v4), land-ocean index, annual means against the 1951-1980 base period. A work of the
             US Government, not subject to copyright.

    **The unit is kelvin, and that is the point of the example.**

    An anomaly is a temperature *difference*, and a difference is not a temperature. Degrees
    Celsius is an affine unit: converting it to SI needs an offset of 273.15 as well as a factor,
    which is exactly what `NonSi.degreeCelsius.IsAffine` reports. That offset is meaningful for a
    reading and meaningless for an interval — a rise of 1.2 °C is a rise of 1.2 K, not of 274.35.
    So an anomaly is kelvin, and Marey's affine witness would be the wrong tool by 273.15.

    This is a units error a compiler cannot catch for you, because both witnesses have the same
    dimension. It is the kind of thing the library is *for* noticing, not the kind it can decide. *)

#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Svg"
#r "nuget: Marey.Backend.Tikz"

open Marey
open Marey.UnitsOfMeasure
open System.Globalization

let dataPath =
    System.IO.Path.Combine(__SOURCE_DIRECTORY__, "data", "gistemp-annual.csv")

let rows =
    System.IO.File.ReadAllLines dataPath
    |> Array.filter (fun l -> not (l.StartsWith "#") && l <> "" && not (l.StartsWith "year"))
    |> Array.map (fun l ->
        let parts = l.Split ','
        let f (s: string) = System.Double.Parse(s, CultureInfo.InvariantCulture)
        f parts.[0], f parts.[1])

let years = rows |> Array.map fst

// The anomaly, tagged as kelvin — see the header. `1.0<K>` is the tag; the number is unchanged,
// because a Celsius interval and a kelvin interval are the same size.
let anomaly = rows |> Array.map (fun (_, a) -> a * 1.0<K>)

printfn "%d annual anomalies, %g to %g" rows.Length (Array.head years) (Array.last years)

// The witnesses differ on the x and y axes: x is a bare year number with no dimension, y is a
// temperature difference in kelvin. `U.one` is the dimensionless witness, and it is what lets a
// unitless axis sit beside a unitful one in the same call.
let figure =
    Chart.line U.one U.kelvin years anomaly
    |> Chart.withName "GISTEMP L-OTI"
    |> Chart.withXLabel "year"
    |> Chart.withLegend LegendTopLeft
    |> Chart.withGrid false true
    |> Chart.withTitle "Global temperature anomaly"
    |> Chart.withSubtitle "annual mean, relative to 1951–1980"

let here = System.IO.Directory.GetCurrentDirectory()
let svgPath = System.IO.Path.Combine(here, "temperature-anomaly.svg")
let texPath = System.IO.Path.Combine(here, "temperature-anomaly.tex")

figure |> Chart.toSvg SvgBackend.render svgPath (Chart.size 820.0 480.0)

figure
|> Chart.withTheme Theme.tikz
|> Chart.toFile TikzBackend.render texPath (Chart.size 340.0 200.0)

printfn "wrote %s" svgPath
printfn "wrote %s" texPath

let svg = figure |> Chart.render SvgBackend.render (Chart.size 820.0 480.0)

// The y axis derived `[K]` from the witness. The x axis has no unit and gets no brackets.
printfn "y axis reads kelvin:      %b" (svg.Contains "[K]")

// The distinction the example is about, stated as code rather than as a comment.
printfn "celsius is affine:        %b" (NonSi.degreeCelsius.IsAffine)
printfn "kelvin is not:            %b" (not U.kelvin.IsAffine)
printfn "same dimension, though:   %b" (U.dim NonSi.degreeCelsius = U.dim U.kelvin)

// And the LaTeX route, since this is a figure that belongs in a document.
let tex = System.IO.File.ReadAllText texPath
printfn "tikz typesets the kelvin: %b" (tex.Contains "kelvin")
