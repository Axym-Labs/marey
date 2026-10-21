(*  Fields: two line charges, drawn every way Marey.Fields draws a field.

    Run it:  dotnet fsi examples/fields.fsx
    Writes:  fields-potential.svg, fields-lines.svg, fields-quiver.svg and fields-lic.png in the
             current directory.

    A potential in volts over a plane in metres. The field is its negative gradient, and the
    library works out that it is in V/m and that the field's divergence is in V/m². Between the
    charges the divergence is zero, which is Gauss's law with no charge enclosed, and the
    script checks that on the grid.

    Every mark is computed once the panel's scales are known, so the heatmap, its contours and the
    field lines over them agree on where everything is. *)

#r "nuget: Marey.Fields"
#r "nuget: Marey.Backend.Svg"
#r "nuget: Marey.Backend.Skia"

open Marey
open Marey.UnitsOfMeasure

// +1 at (0.6, −0.1) m and −1 at (−0.6, 0.1) m, unit line charges: φ = ln(r₋ / r₊), in volts.
let plus = { X = 0.6<m>; Y = -0.1<m> }
let minus = { X = -0.6<m>; Y = 0.1<m> }

let potential (p: Vec2<m>) =
    (log (float (Vec2.norm (p - minus))) - log (float (Vec2.norm (p - plus)))) * 1.0<V>

// An even node count on each axis, so no node lands on a charge.
let phi = Field.sampleScalar (Grid.linear -1.8<m> 1.8<m> 144) (Grid.linear -1.2<m> 1.2<m> 96) potential

let e =
    let g = Field.grad phi
    { g with U = Array.map (~-) g.U; W = Array.map (~-) g.W }

let divergence: ScalarGrid<m, m, V/m^2> = Field.div e
let fieldUnit = U.div U.volt U.metre
let size = Chart.size 640.0 420.0

let write (file: string) (fig: Figure) =
    let path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), file)
    fig |> Chart.toSvg SvgBackend.render path size
    printfn "wrote %s" path
    Chart.render SvgBackend.render size fig

// The potential, its equipotentials at round numbers of volts, and a colour scale stated rather
// than taken from the data: near each charge the potential diverges, and the grid nodes closest
// to them would otherwise stretch the scale until everything else is the ramp's middle.
let potentialSvg =
    Field.heatmapWith Theme.initial.Diverging true U.metre U.metre U.volt phi
    |> Chart.withColorDomain U.volt -2.0<V> 2.0<V>
    |> Chart.withColorLabel "φ"
    |> Chart.overlay (Field.contoursAt [| -1.5<V> .. 0.5<V> .. 1.5<V> |] U.metre U.metre phi)
    |> Chart.withYLabel "y"
    |> Chart.withTitle "Two line charges: the potential"
    |> write "fields-potential.svg"

// Field lines over filled bands of the potential. The outermost levels are far beyond the stated
// colour domain, so the bands around each charge are filled in the ramp's end colour rather than
// left empty.
let bandLevels = Array.concat [ [| -20.0<V> |]; [| -2.0<V> .. 0.5<V> .. 2.0<V> |]; [| 20.0<V> |] ]

Field.bandsWith Theme.initial.Diverging bandLevels U.metre U.metre U.volt phi
|> Chart.withColorDomain U.volt -2.0<V> 2.0<V>
|> Chart.withColorLabel "φ"
|> Chart.overlay (Field.streamlinesWith 18.0<px> U.metre U.metre e)
|> Chart.withYLabel "y"
|> Chart.withTitle "The field lines"
|> write "fields-lines.svg"
|> ignore

// A quiver on a coarser grid, coloured by magnitude, with a key saying what one length means.
let coarse = Field.sample (Grid.linear -1.65<m> 1.65<m> 12) (Grid.linear -1.05<m> 1.05<m> 8) (fun p ->
    let d = 1e-5<m>
    { X = (potential { p with X = p.X - d } - potential { p with X = p.X + d }) / (2.0 * d)
      Y = (potential { p with Y = p.Y - d } - potential { p with Y = p.Y + d }) / (2.0 * d) })

let quiverSvg =
    Field.quiverBy U.metre U.metre fieldUnit coarse
    |> Chart.withYLabel "y"
    |> Chart.withTitle "The field, as arrows"
    |> write "fields-quiver.svg"

// Line-integral convolution, coloured by the field's magnitude: a raster at the plot area's
// resolution, its grain from a seeded generator.
let lic =
    Field.licBy U.metre U.metre fieldUnit e
    |> Chart.withColorDomain fieldUnit 0.0<V/m> 4.0<V/m>
    |> Chart.withYLabel "y"
    |> Chart.withTitle "The field, by line-integral convolution"

let licPng = Chart.render SkiaBackend.renderPng size lic
System.IO.File.WriteAllBytes("fields-lic.png", licPng)
printfn "wrote fields-lic.png, %d bytes" licPng.Length

// ---- The claims ----------------------------------------------------------------------------

printfn "the potential's colorbar is labelled in volts: %b" (potentialSvg.Contains "φ [V]")
printfn "the quiver's key is in V/m: %b" (quiverSvg.Contains "V·m⁻¹")

// Gauss's law with no charge enclosed: away from both charges, ∇·E vanishes up to the grid's
// O(h²) differencing error, here under 1% of |E|/h, the scale a single difference has.
let h = 3.6 / 143.0
let clear (x: float<m>) (y: float<m>) =
    let p = { X = x; Y = y }
    float (Vec2.norm (p - plus)) > 0.3 && float (Vec2.norm (p - minus)) > 0.3

let worst =
    [| for j in 0 .. divergence.Ys.Length - 1 do
           for i in 0 .. divergence.Xs.Length - 1 do
               let x, y = divergence.Xs.[i], divergence.Ys.[j]
               let k = j * divergence.Xs.Length + i

               if clear x y && i > 0 && j > 0 && i < divergence.Xs.Length - 1 && j < divergence.Ys.Length - 1 then
                   let magnitude = sqrt (float e.U.[k] ** 2.0 + float e.W.[k] ** 2.0)
                   yield abs (float divergence.Values.[k]) * h / magnitude |]
    |> Array.max

printfn "away from the charges, the field's divergence is zero to %.2g of |E|/h" worst
printfn "Gauss's law holds on the grid, to the differences' order: %b" (worst < 0.01)

printfn "the same seed draws the same texture: %b" (Chart.render SkiaBackend.renderPng size lic = licPng)
