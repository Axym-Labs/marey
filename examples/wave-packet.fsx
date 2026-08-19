(*  A Gaussian wave packet — prefix selection on both axes, and the figure a paper wants.

    Run it:  dotnet fsi examples/wave-packet.fsx
    Writes:  wave-packet.svg and wave-packet.tex in the current directory.

    Free fall shows the axis titles being derived. This one shows the *magnitudes* being derived.
    The data spans ±1.5e-2 m and ±5e4 Pa, and nobody scales it, divides it through by a power of
    ten, or writes "(mm)" anywhere: the x axis labels itself in millimetres with small integer
    ticks, and the y axis in kilopascals.

    It is also the example that goes into a LaTeX document, so it renders both ways. *)

#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Svg"
#r "nuget: Marey.Backend.Tikz"

open Marey

// An ultrasound burst: a 1.5 mm carrier inside a 4 mm Gaussian envelope, peaking at 50 kPa.
let sigma = 4.0e-3<m>
let lambda = 1.5e-3<m>
let p0 = 5.0e4<Pa>

// `k` is 2π/λ, so its type is float<1/m>. That is what makes `k * x` dimensionless below, and
// therefore what makes it legal to hand to `cos` — a wave number mistyped as a length would fail
// here rather than produce a quietly wrong picture.
let k = 2.0 * System.Math.PI / lambda

let xs = Array.init 801 (fun i -> (-15.0 + 0.0375 * float i) * 1.0e-3<m>)

let envelope = xs |> Array.map (fun x -> p0 * exp (-(x * x) / (2.0 * sigma * sigma)))
let pressure = Array.map2 (fun (p: float<Pa>) x -> p * cos (k * x)) envelope xs

let figure =
    Chart.line U.metre Derived.pascal xs pressure
    |> Chart.withName "p(x)"
    |> Chart.addLine U.metre Derived.pascal xs envelope
    |> Chart.withName "envelope"
    |> Chart.withDash [| 4.0<px>; 3.0<px> |]
    |> Chart.withLegend LegendTopRight
    |> Chart.withTitle "Gaussian wave packet"
    |> Chart.withSubtitle "1.5 mm carrier, 4 mm envelope"

let here = System.IO.Directory.GetCurrentDirectory()
let svgPath = System.IO.Path.Combine(here, "wave-packet.svg")
let texPath = System.IO.Path.Combine(here, "wave-packet.tex")

figure |> Chart.toSvg SvgBackend.render svgPath (Chart.size 800.0 500.0)

// The same figure, as a `tikzpicture` to paste into a document: the axis titles are typeset by
// siunitx and the body font is inherited from the document, not baked in here.
figure
|> Chart.withTheme Theme.tikz
|> Chart.toFile TikzBackend.render texPath (Chart.size 340.0 210.0)

printfn "wrote %s" svgPath
printfn "wrote %s" texPath

let svg = figure |> Chart.render SvgBackend.render (Chart.size 800.0 500.0)
printfn "x axis chose millimetres: %b" (svg.Contains "[mm]")
printfn "y axis chose kilopascals: %b" (svg.Contains "[kPa]")

(*  A note on units that have no name.

    Pressure works here because `Pa` is a named SI entry, looked up from the dimension. Units with
    no name work too, as long as you say what they are made of. Had this been an electromagnetic
    packet in volts per metre, `U.div Derived.volt U.metre` labels the axis `MV·m⁻¹` — not because
    Marey worked out that the dimension `kg·m·s⁻³·A⁻¹` factors that way, which would be a guess
    (the same dimension is `J/K` and `N·m/K`), but because the composition is the one you wrote. *)

let fieldUnit = U.div Derived.volt U.metre
printfn "a unit with no SI name still reads well: %s" (UnitDisplay.unitText Unicode UnitRegistry.si Prefix.none fieldUnit)
