(*  The encoding block on real data — channels, a fit, and a facet.

    Run it:  dotnet fsi examples/warming-rate.fsx
    Writes:  warming-rate.svg and warming-rate-by-era.svg in the current directory.

    Data:    examples/data/gistemp-annual.csv — NASA GISS Surface Temperature Analysis (GISTEMP
             v4), land-ocean index, annual means against the 1951-1980 base period. A work of the
             US Government, not subject to copyright.

    **What this example is for.** The other examples build a figure a call at a time. This one
    starts from a table of rows and says what each column *means* — x, y, and a colour — in one
    block, then asks for a trend line. That is the shape to reach for when the data is a table
    and the figure has more than two things in it.

    **And the fitted slope has a unit.** x is elapsed time in years and y is a temperature
    difference in kelvin, so the least-squares slope is `float<K/yr>`: a warming rate, typed as
    one. Binding it to an annotated `float<K/yr>` below is a claim the compiler checks, and the
    same witness is what prints `K·yr⁻¹` on an axis.

    **Why elapsed years and not the calendar year.** A calendar year is a point on a scale with a
    zero somebody chose; a rate is measured over an interval. Subtracting 1880 makes x an
    interval, which is what makes the slope a rate — and it puts the fitted intercept at 1880,
    where it means something, rather than at year zero, where it does not. *)

#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Svg"

open Marey
open System.Globalization

let dataPath =
    System.IO.Path.Combine(__SOURCE_DIRECTORY__, "data", "gistemp-annual.csv")

/// One row of the table. The block below reads it with accessors, so the field names are what
/// the figure is written in terms of.
type Reading =
    { Year: float
      /// Years since 1880 — an interval, so a slope against it is a rate.
      Elapsed: float<yr>
      /// The anomaly is a temperature *difference*, so it is kelvin and not degrees Celsius.
      /// `temperature-anomaly.fsx` is the example about that distinction.
      Anomaly: float<K>
      Era: string }

let era (year: float) =
    if year < 1940.0 then "1880–1939"
    elif year < 1980.0 then "1940–1979"
    else "1980–2025"

let readings =
    System.IO.File.ReadAllLines dataPath
    |> Array.filter (fun l -> not (l.StartsWith "#") && l <> "" && not (l.StartsWith "year"))
    |> Array.map (fun l ->
        let parts = l.Split ','
        let f (s: string) = System.Double.Parse(s, CultureInfo.InvariantCulture)
        let year = f parts.[0]

        { Year = year
          Elapsed = (year - 1880.0) * 1.0<yr>
          Anomaly = f parts.[1] * 1.0<K>
          Era = era year })

printfn "%d annual anomalies, %g to %g" readings.Length readings.[0].Year (Array.last readings).Year

// ---------------------------------------------------------------------------------------
// The block
// ---------------------------------------------------------------------------------------

// Three channels and a stat. `color` takes no unit witness because a category has no dimension;
// `x` and `y` take theirs, and the accessor is checked against the witness beside it — naming
// `r.Anomaly` under `U.year` would not compile.
let figure =
    plot readings {
        x U.year (fun r -> r.Elapsed)
        y U.kelvin (fun r -> r.Anomaly)
        color (fun r -> r.Era)
        layer GeomPoint
        fit
    }
    |> Chart.withXLabel "years since 1880"
    |> Chart.withYLabel "anomaly"
    |> Chart.withTitle "Global temperature anomaly"
    |> Chart.withSubtitle "annual mean against the 1951–1980 base period, with a least-squares trend"
    |> Chart.withLegend LegendTopLeft
    |> Chart.withGrid false true

// One panel per era, over shared axes, so the three periods are compared rather than merely
// shown. A facet computes the shared domains before it splits, so every panel is drawn on the
// same scale and the panels can be read against each other.
let byEra =
    plot readings {
        x U.year (fun r -> r.Elapsed)
        y U.kelvin (fun r -> r.Anomaly)
        facetWrap (fun r -> r.Era)
        layer GeomLine
    }
    |> Chart.withXLabel "years since 1880"
    |> Chart.withYLabel "anomaly"
    |> Chart.withTitle "The same record, one panel per era"

// ---------------------------------------------------------------------------------------
// The number the figure draws
// ---------------------------------------------------------------------------------------

// The block draws the trend and does not hand back its slope. Asking for the number is a
// separate call over the same two arrays, and it comes back typed.
let trend =
    Stat.regression U.year U.kelvin (readings |> Array.map (fun r -> r.Elapsed)) (readings |> Array.map (fun r -> r.Anomaly))

// The annotation is the claim: a fit of kelvin against years has a slope in kelvin per year, and
// this line does not compile if that ever stops being true.
let rate: float<K / yr> = trend.Slope

printfn "warming rate:             %.4f K/yr (%.2f K per century)" (float rate) (float rate * 100.0)
printfn "R²:                       %.3f" trend.R2

// The same witness the number carries is what an axis prints for it, and the prefix ladder still
// applies on the way: an axis running 0 to 0.02 K/yr labels itself in millikelvin per year.
let rateAxis =
    Axis.auto UnitStyle.Unicode "rate" (Scale.linear trend.SlopeUnit (0.0<K / yr>, 0.02<K / yr>))

printfn "the rate as an axis unit: %s" rateAxis.UnitText

// ---------------------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------------------

let here = System.IO.Directory.GetCurrentDirectory()
let svgPath = System.IO.Path.Combine(here, "warming-rate.svg")
let facetPath = System.IO.Path.Combine(here, "warming-rate-by-era.svg")

figure |> Chart.toSvg SvgBackend.render svgPath (Chart.size 820.0 480.0)
byEra |> Chart.toSvg SvgBackend.render facetPath (Chart.size 900.0 340.0)

printfn "wrote %s" svgPath
printfn "wrote %s" facetPath

// ---------------------------------------------------------------------------------------
// Claims. A printed `false` fails the test suite.
// ---------------------------------------------------------------------------------------

let svg = figure |> Chart.render SvgBackend.render (Chart.size 820.0 480.0)

// The y axis derived `[K]` from the witness; the x axis derived `[yr]` from the other one.
printfn "y axis reads kelvin:      %b" (svg.Contains "[K]")
printfn "x axis reads years:       %b" (svg.Contains "[yr]")

// The colour channel put the three eras in the legend, by name.
printfn "the legend names the eras: %b" (readings |> Array.map (fun r -> r.Era) |> Array.distinct |> Array.forall svg.Contains)

// The slope is a rate rather than a temperature: kelvin over time, not kelvin.
printfn "the slope is K per time:  %b" (trend.SlopeUnit.Dim = Dim.sub (U.dim U.kelvin) Dim.time)

// One block, one panel; the fit is an extra layer over the same encoding.
printfn "the block is one panel:   %b" (figure.Panels.Length = 1)
printfn "the fit added a layer:    %b" (figure.Panels.[0].Layers.Length = 2)

// The facet is one panel per era, and every panel is on the same axes.
let domains =
    byEra.Panels
    |> Array.map (fun p -> Erase.seriesScales p.Layers.[0].Data |> fun (sx, sy) -> sx.Domain, sy.Domain)

printfn "one panel per era:        %b" (byEra.Panels.Length = 3)
printfn "every panel shares axes:  %b" (domains |> Array.distinct |> Array.length = 1)

// And the record really is warming, which is the only claim here that is about the world.
printfn "the trend is positive:    %b" (float rate > 0.0)
