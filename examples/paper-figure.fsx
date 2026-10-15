(*  A figure for a paper: the last ten minutes, done in the library rather than by hand.

    Run it:  dotnet fsi examples/paper-figure.fsx
    Writes:  paper-figure.svg, paper-figure.tex and paper-spectrum.svg in the current directory.

    Two figures that physics papers carry. A fit over measurements with its residuals below it,
    lettered (a) and (b), with a rule at zero; and an emission spectrum with its lines marked by
    arrows, ticks every 50 nm with minor ticks between, a shaded passband, and the legend outside
    the plot. Under `Theme.publication` both are the box journals print: framed, ticks inside on
    all four sides.

    Everything a reader would otherwise draw by hand — the rule, the arrows, the letters, the tick
    step, the legend's place — is a call, and none of it moves the axes: a rule or a label marks a
    place on the domain the data chose. The data are synthetic and deterministic. *)

#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Svg"
#r "nuget: Marey.Backend.Tikz"

open Marey
open Marey.UnitsOfMeasure

// ---- (a) and (b): a fit and its residuals -----------------------------------------------------

// x(t) = A·e^(−t/τ)·cos(ωt), sampled with a seeded wobble standing in for noise.
let amplitude, tau, frequency = 12e-3<m>, 1.8<s>, 1.4<1/s>
let model (t: float<s>) = amplitude * exp (-t / tau) * cos (2.0 * System.Math.PI * frequency * t)

let wobble (i: int) =
    let x = sin (float i * 12.9898) * 43758.5453
    2.0 * (x - floor x) - 1.0

let ts = Array.init 31 (fun i -> float i * 0.2<s>)
let sigma = Array.create ts.Length 0.6e-3<m>
let measured = ts |> Array.mapi (fun i t -> model t + sigma.[i] * wobble i)
let fine = Array.init 400 (fun i -> float i * 6.0<s> / 399.0)

let fit =
    Chart.errorbar U.second U.metre ts measured sigma
    |> Chart.withName "measured"
    |> Chart.addLine U.second U.metre fine (fine |> Array.map model)
    |> Chart.withName "model"
    |> Chart.withXTicksEvery U.second 1.0<s>
    |> Chart.withXDomain 0.0 6.0
    |> Chart.withLegendFrame false

let residuals =
    Chart.errorbar U.second U.metre ts (Array.map2 (fun x t -> x - model t) measured ts) sigma
    // The rule at zero stays out of the domain, so the residuals keep their own millimetres.
    |> Chart.addHLine U.metre 0.0<m>
    |> Chart.withXTicksEvery U.second 1.0<s>
    |> Chart.withXDomain 0.0 6.0
    |> Chart.withYLabel "residual"

// ---- A spectrum --------------------------------------------------------------------------------

let nm = 1e-9<m>
let balmer = [| "Hδ", 410.2, 0.18; "Hγ", 434.0, 0.32; "Hβ", 486.1, 0.55; "Hα", 656.3, 1.0 |]
let wavelengths = Array.init 1201 (fun i -> (400.0 + float i * 0.25) * nm)

let intensity =
    wavelengths
    |> Array.map (fun l ->
        let l = float (l / nm)
        0.03 + (balmer |> Array.sumBy (fun (_, centre, height) -> height * exp (-0.5 * ((l - centre) / 1.2) ** 2.0))))

let spectrum =
    let mutable fig =
        Chart.line U.metre U.one wavelengths intensity
        |> Chart.withName "observed"
        |> Chart.addVSpan U.metre (480.0 * nm) (500.0 * nm)
        |> Chart.withName "filter passband"

    for name, centre, height in balmer do
        fig <-
            fig
            |> Chart.annotate U.metre U.one (centre * nm) (height + 0.03) name
            |> Chart.withTextOffset 14.0<px> 22.0<px>
            |> Chart.withArrow ArrowStraight

    fig
    // Stated in metres, written in nanometres: the step keeps the prefix the axis chose.
    |> Chart.withXTicksEvery U.metre (50.0 * nm)
    |> Chart.withXMinorTicks (MinorEvery 10e-9)
    |> Chart.withXDomain 400e-9 700e-9
    |> Chart.withYDomain 0.0 1.3
    |> Chart.withXLabel "λ"
    |> Chart.withYLabel "intensity"
    |> Chart.withLegend LegendOutsideRight
    |> Chart.withLegendFrame false

// The fit over its residuals, lettered in reading order, and the spectrum on its own.
let figure =
    fit
    |> Chart.above residuals
    |> Chart.withPanelLetters LetterParen
    |> Chart.withTheme Theme.publication

let spectrumFigure = spectrum |> Chart.withTheme Theme.publication

let here = System.IO.Directory.GetCurrentDirectory()
let size = Chart.size 560.0 480.0
let wide = Chart.size 720.0 340.0

figure |> Chart.toSvg SvgBackend.render (System.IO.Path.Combine(here, "paper-figure.svg")) size
figure |> Chart.toFile TikzBackend.render (System.IO.Path.Combine(here, "paper-figure.tex")) size
spectrumFigure |> Chart.toSvg SvgBackend.render (System.IO.Path.Combine(here, "paper-spectrum.svg")) wide
printfn "wrote paper-figure.svg, paper-figure.tex and paper-spectrum.svg"

let svg = figure |> Chart.render SvgBackend.render size
let spectrumSvg = spectrumFigure |> Chart.render SvgBackend.render wide

// The residual axis is in millimetres and the fit's too: the rule at zero moved neither.
printfn "residuals in millimetres: %b" (svg.Contains ">residual [mm]<")
printfn "lettered:                 %b" (svg.Contains ">(a)<" && svg.Contains ">(b)<")
// The spectrum's ticks were stated in metres and are written in nanometres.
printfn "every 50 nm:              %b" (spectrumSvg.Contains ">450<" && spectrumSvg.Contains ">λ [nm]<")
printfn "the lines are marked:     %b" (spectrumSvg.Contains ">Hα<")
