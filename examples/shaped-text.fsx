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

// A title chosen for its kerning pairs. AV, VA, TA and AT are where a real face pulls letters
// together and an advance-width table cannot.
let t = Array.init 40 (fun i -> float i * 0.25<s>)
let x = t |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)

let figure =
    Chart.line U.second U.metre t x
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
// ----------------------------------------------------------------------------------------

let spec: FontSpec =
    { Family = Theme.Sans
      Size = 9.0<pt> // 12 px at the screen convention
      Weight = 400
      Italic = false }

let shaper = new SkiaTextMeasurer() :> ITextMeasurer

let approxAdvance = float (TextMeasure.approx.Measure spec "AVATAR").Advance
let shapedAdvance = float (shaper.Measure spec "AVATAR").Advance

printfn ""
printfn "AVATAR at 12 px"
printfn "  approximated: %6.2f px" approxAdvance
printfn "  shaped:       %6.2f px" shapedAdvance
printfn "  difference:   %6.2f px (%.1f%%)" (approxAdvance - shapedAdvance) ((approxAdvance - shapedAdvance) / approxAdvance * 100.0)

// Narrower, because every one of those pairs kerns. Not a general law — for a string with
// nothing to kern the two agree to within rounding, and which side of it they land on is
// arbitrary, which is exactly what the digits below check.
printfn "kerning made this string narrower:     %b" (shapedAdvance < approxAdvance)

// Digits have nothing to kern, so the two agree there — which is what makes the number above
// attributable to kerning rather than to a disagreement about how wide a glyph is.
let digitsApprox = float (TextMeasure.approx.Measure spec "0123456789").Advance
let digitsShaped = float (shaper.Measure spec "0123456789").Advance

printfn "digits agree within 5%%:                %b" (abs (digitsApprox - digitsShaped) / digitsApprox < 0.05)

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
