(*  Shaped text — what "the first backend that can measure text properly" actually changes.

    Run it:  dotnet fsi examples/shaped-text.fsx
    Writes:  shaped-text-approximated.png and shaped-text-shaped.png in the current directory.

    Marey's layout is a measure–arrange pass against an `ITextMeasurer`. For two years there was
    exactly one implementation of that interface: a Helvetica advance-width table with no kerning
    and no shaping, which is what a library with a zero-dependency core can manage on its own.

    `Marey.Backend.Skia` brings a second one, over HarfBuzz. The difference is invisible in prose
    and obvious in two pictures, so this script draws both — the *same figure*, through the *same
    renderer*, differing only in which measurer arranged it. *)

#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Skia"

open Marey
open Marey.UnitsOfMeasure

// A title chosen for its kerning pairs. AV, VA, TA and AT are where a real face pulls letters
// together and an advance-width table cannot.
let t = Array.init 40 (fun i -> float i * 0.25<s>)
let x = t |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)

let figure =
    Chart.lineIn U.second U.metre t x
    |> Chart.withTitle "AVATAR: Wave To Yank"
    |> Chart.withSubtitle "the same figure, arranged by two measurers"

let size = Chart.size 720.0 450.0
let here = System.IO.Directory.GetCurrentDirectory()

// ----------------------------------------------------------------------------------------
// The two renders.
//
// `Chart.renderTo` lays out against the backend's own measurer, which for this backend is the
// shaper. `Chart.toSceneWith` takes one explicitly, which is how the approximated layout is
// produced without changing anything else about the render.
// ----------------------------------------------------------------------------------------

let shaped =
    let backend = new SkiaBackend()
    figure |> Chart.renderTo backend size
    backend.Result

let approximated =
    figure |> Chart.toSceneWith TextMeasure.approx size |> SkiaBackend.renderPng size

let write (name: string) (bytes: byte[]) =
    let path = System.IO.Path.Combine(here, name)
    System.IO.File.WriteAllBytes(path, bytes)
    printfn "wrote %s" path

write "shaped-text-shaped.png" shaped
write "shaped-text-approximated.png" approximated

// Same picture, same renderer, different pixels — because the gutters are different widths.
printfn "the two layouts differ:                %b" (shaped <> approximated)

// ----------------------------------------------------------------------------------------
// The numbers behind the difference.
//
// The approximation and the shaper differ for two reasons at once: the face this machine
// opened need not have Helvetica's widths, and the shaper kerns. Measuring the same face a
// third time, summing its advances without shaping, separates the two.
// ----------------------------------------------------------------------------------------

let spec: FontSpec =
    { Family = Theme.Sans
      Size = 9.0<pt> // 12 px at the screen convention
      Weight = 400
      Italic = false }

let skia = new SkiaTextMeasurer()
let shaper = skia :> ITextMeasurer

// The face the shaper resolved, measured without shaping: linear metrics, because a default
// `SKFont` hints and rounds every advance to a whole pixel, which the shaper does not.
let unshaped (text: string) = skia.MeasureUnshaped(spec, text)

let measureAll (text: string) =
    float (TextMeasure.approx.Measure spec text).Advance, unshaped text, float (shaper.Measure spec text).Advance

let approxAdvance, unshapedAdvance, shapedAdvance = measureAll "AVATAR"

printfn ""
printfn "AVATAR at 12 px, in %s" (skia.Face spec).Family
printfn "  approximated (Helvetica's table): %6.2f px" approxAdvance
printfn "  this face, unshaped:              %6.2f px" unshapedAdvance
printfn "  this face, shaped:                %6.2f px" shapedAdvance
printfn "  of which widths:                  %+6.2f px" (unshapedAdvance - approxAdvance)
printfn "  of which kerning:                 %+6.2f px" (shapedAdvance - unshapedAdvance)

// Narrower, because every one of those pairs kerns. Compared within one face, so it holds
// whatever face this machine opened. Against the approximation it holds only where the face has
// Helvetica's widths — Arial and Liberation Sans do, and the "of which widths" line is then zero.
printfn "kerning made this string narrower:     %b" (shapedAdvance < unshapedAdvance)

// A string with nothing to kern shapes to its summed width, which is what makes the AVATAR
// number above attributable to kerning rather than to anything else the shaper does. One glyph
// repeated, because a face can kern digits: Inter tightens some pairs of its proportional ones.
//
// "Shapes to" within the shaper's grid: SkiaSharp runs HarfBuzz at 512 units to the font size,
// so each advance comes back rounded to 12/512 px, and is at most half that away from the face's
// own value. Ten glyphs can drift by ten half-steps and no more.
let _, digitsUnshaped, digitsShaped = measureAll "0000000000"

printfn "shaping left an unkerned run alone:     %b" (abs (digitsShaped - digitsUnshaped) <= 10.0 * 12.0 / 1024.0)

// ----------------------------------------------------------------------------------------
// Which face this actually drew with.
//
// A PNG's glyph widths come from a face on *this* machine. Marey does not hide that: the
// backend reports what it opened, and `SkiaOptions.FontFile` is how a figure that has to be
// identical on two machines states one instead.
// ----------------------------------------------------------------------------------------

let reporting = new SkiaBackend()
figure |> Chart.renderTo reporting size

printfn ""

for face in reporting.ResolvedFaces |> Array.distinctBy (fun f -> f.Family) do
    match face.Matched with
    | Some entry -> printfn "the theme's stack resolved to '%s' via '%s'" face.Family entry
    | None -> printfn "the theme's stack matched nothing; Skia's default '%s' was used" face.Family

printfn "a face was opened:                     %b" (reporting.ResolvedFaces.Length > 0)
// The theme's stack names Inter, which the package carries, so the face is the same on every
// machine and so is the PNG.
printfn "the theme's face is the one carried:   %b" (reporting.ResolvedFaces |> Array.forall (fun f -> f.Bundled && f.Family = "Inter"))

// ----------------------------------------------------------------------------------------
// And the other thing this backend brings: a vector PDF, without a TeX installation.
//
// The page is the requested extent converted exactly — 720 x 450 px is 540 x 337.5 pt — and it
// carries no timestamp, so two renders of one figure are byte-identical.
// ----------------------------------------------------------------------------------------

let pdf = figure |> Chart.render SkiaBackend.renderPdf size
write "shaped-text.pdf" pdf

printfn "the pdf page is 540 x 337.5 pt:        %b" ((System.Text.Encoding.Latin1.GetString pdf).Contains "/MediaBox [0 0 540 337.5]")
printfn "two pdf renders are identical:         %b" (pdf = (figure |> Chart.render SkiaBackend.renderPdf size))
