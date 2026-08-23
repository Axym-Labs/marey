namespace Marey

open System
open System.Collections.Generic
open SkiaSharp
open SkiaSharp.HarfBuzz

// ======================================================================================
// §10 — "ARCHITECT `ITextShaper` AS A CAPABILITY FROM DAY ONE", CASHED IN.
//
// `ITextMeasurer` has existed since M0 with exactly one implementation behind it, and M0's
// comment on that seam said what it was for: "having one seam here is what makes it possible
// to start with an approximation and swap in HarfBuzz at M2 without touching the layout code."
// This file is the second implementation, and that claim is now tested rather than asserted.
//
// `ApproxTextMeasurer` sums a Helvetica advance-width table. It has no kerning, no ligatures
// and no shaping, and §15 warns that "text and layout eat 40% of the effort". What replaces it
// here is HarfBuzz, through `SKShaper`, against a real face opened from the machine or from a
// file the caller names.
//
// ---------------------------------------------------------------------------------------
// D62 — A FONT IS RESOLVED AND *REPORTED*, OR STATED AND OBEYED.
// ---------------------------------------------------------------------------------------
//
// `Theme.Sans` is a CSS stack: "Inter, -apple-system, BlinkMacSystemFont, Segoe UI, Roboto,
// Helvetica Neue, Arial, sans-serif". SVG hands that string to whatever opens the file; TikZ
// ignores it and inherits the document's font (D35). Skia has to pick one real face, and it
// picks it from what happens to be installed — so the same figure is a different PNG on a
// laptop, on a colleague's laptop and in CI.
//
// Three answers were on the table and the middle one is a trap:
//
//   * **Bundle a face.** Fixes reproducibility, costs package size and a licence to audit for
//     every consumer of the library.
//   * **Refuse without a stated font file.** Correct and hostile: rendering a figure would
//     stop working for the reason that a font was not named, which is not a reason a caller of
//     a plotting library expects.
//   * **Resolve, and report what was chosen.** The figure still renders anywhere; what changes
//     is that the machine-dependence is *visible* — `SkiaBackend.ResolvedFaces` names the face
//     behind every run in the scene — and a caller who needs reproducibility states a file and
//     gets it.
//
// The third, with the second available as `SkiaOptions.FontFile`. The test suite states a file
// (`tests/Marey.Tests/fonts/LiberationSans-Regular.ttf`) precisely so this milestone's numbers
// are reproducible even though a user's figure is not — which is §5's accepted imprecision,
// written down rather than discovered.
//
// ---------------------------------------------------------------------------------------
// OWNERSHIP
// ---------------------------------------------------------------------------------------
//
// `SKTypeface`, `SKFont` and `SKShaper` are native handles, and layout calls `Measure` once per
// tick label, legend entry and axis title — hundreds of times for an ordinary figure. So they
// are cached here and owned here: the measurer disposes them and a caller must not. That is
// also what makes measuring and *drawing* agree, because `SkiaBackend.drawText` shapes with the
// same `SKFont` and the same `SKShaper` this hands back. A backend that measured with one face
// and drew with another would centre every label against the wrong width.
// ======================================================================================

/// What a font family stack resolved to on this machine. D62's "and report it" half.
///
/// One of these exists for every distinct `(family, weight, italic)` a render actually asked
/// for, in the order it first asked, on `SkiaBackend.ResolvedFaces`.
type ResolvedFace =
    { /// The stack as the scene asked for it, verbatim.
      Requested: string
      Weight: int
      Italic: bool
      /// The entry in the stack that matched, or `None` when every entry missed and Skia's own
      /// default face was used. `None` is the interesting case: it means the figure looks like
      /// nothing the theme asked for.
      Matched: string option
      /// The family name of the face that was actually opened.
      Family: string
      /// The file it came from, when the caller stated one rather than letting the stack
      /// resolve. `Some` means this render is reproducible on another machine.
      File: string option }

/// A real text measurer: HarfBuzz shaping through `SKShaper`, against a resolved face.
///
/// This is the first `ITextMeasurer` other than the approximation, and it is the reason
/// `SkiaBackend` advertises `TextShaping = true`. It measures **shaped** advances, so kerning
/// pairs and ligatures reach `Figure.arrangement` and therefore the panel rectangle: a figure
/// laid out through this measurer has different gutters from the same figure laid out through
/// `TextMeasure.approx`, and neither is wrong.
///
/// Locked rather than accidentally single-threaded — a measurer is shared with the backend that
/// owns it, and a caller can reasonably measure from elsewhere.
[<Sealed>]
type SkiaTextMeasurer(?fontFile: string, ?dpi: float<px / mm>) =

    let dpi = defaultArg dpi Space.screenDpi
    let fontFile = fontFile |> Option.filter (String.IsNullOrWhiteSpace >> not)

    /// The stated face, opened once and eagerly: a bad path should fail at construction with a
    /// message naming the path, not on the first tick label with a null dereference.
    let stated =
        fontFile
        |> Option.map (fun path ->
            if not (IO.File.Exists path) then
                invalidArg "fontFile" (sprintf "Marey.SkiaTextMeasurer: no font file at '%s'." path)

            let tf = SKTypeface.FromFile path

            if isNull tf then
                invalidArg "fontFile" (sprintf "Marey.SkiaTextMeasurer: '%s' is not a font file Skia can open." path)

            tf)

    /// Faces, keyed by what was asked for rather than by what was got: two stacks that resolve
    /// to the same family are still two questions, and the second is a cache hit only if the
    /// question was the same.
    let faces = Dictionary<struct (string * int * bool), SKTypeface * SKShaper * ResolvedFace>()

    /// Sized fonts. The size is keyed by its exact bits rather than by the float, because two
    /// sizes differing in the last place are two different `SKFont`s to Skia.
    let fonts = Dictionary<struct (string * int * bool * int), SKFont>()

    /// First-seen order, which is traversal order, which is what makes the report readable
    /// against the scene that produced it.
    let order = ResizeArray<ResolvedFace>()

    let gate = obj ()
    let mutable disposed = false

    /// The CSS stack, split the way a browser splits it.
    let stackOf (family: string) =
        if String.IsNullOrWhiteSpace family then
            [||]
        else
            family.Split ','
            |> Array.map (fun s -> s.Trim().Trim('\'').Trim('"'))
            |> Array.filter (fun s -> s <> "")

    /// Resolve one `(family, weight, italic)` against the machine, reporting what was chosen.
    let resolveStack (family: string) (weight: int) (italic: bool) =
        let skWeight: SKFontStyleWeight = enum weight
        let skSlant = if italic then SKFontStyleSlant.Italic else SKFontStyleSlant.Upright
        let names = stackOf family

        let rec pick i =
            if i >= names.Length then
                // Falling through the whole stack lands on Skia's default, which is a real face
                // rather than nothing. `Matched = None` is how the caller finds out.
                SKTypeface.FromFamilyName(null, skWeight, SKFontStyleWidth.Normal, skSlant), None
            else
                let tf = SKTypeface.FromFamilyName(names.[i], skWeight, SKFontStyleWidth.Normal, skSlant)

                // A miss returns Skia's default rather than null, so "did this name resolve"
                // has to be asked by comparing the family back. Without that check the first
                // entry in every stack always "succeeds" and the stack is decorative.
                if isNull tf then
                    pick (i + 1)
                elif String.Equals(tf.FamilyName, names.[i], StringComparison.OrdinalIgnoreCase) then
                    tf, Some names.[i]
                else
                    tf.Dispose()
                    pick (i + 1)

        let tf, matched = pick 0

        tf,
        { Requested = family
          Weight = weight
          Italic = italic
          Matched = matched
          Family = (if isNull tf then "" else tf.FamilyName)
          File = None }

    /// A stated file short-circuits resolution entirely, **including the weight and the
    /// slant**: a caller who names `LiberationSans-Regular.ttf` and then asks for bold gets the
    /// regular face, because that is the file they named. Synthesising a bold from it would put
    /// a face in the figure that is not the one on disk, and silently so.
    let resolve (family: string) (weight: int) (italic: bool) =
        let tf, face =
            match stated with
            | Some tf ->
                tf,
                { Requested = family
                  Weight = weight
                  Italic = italic
                  Matched = None
                  Family = tf.FamilyName
                  File = fontFile }
            | None -> resolveStack family weight italic

        tf, new SKShaper(tf), face

    let faceOf (spec: FontSpec) =
        let key = struct (spec.Family, spec.Weight, spec.Italic)

        match faces.TryGetValue key with
        | true, hit -> hit
        | _ ->
            let hit = resolve spec.Family spec.Weight spec.Italic
            faces.[key] <- hit
            let (_, _, face) = hit
            order.Add face
            hit

    let fontOf (spec: FontSpec) =
        let sizePx = float32 (float (Space.pxOfPt dpi spec.Size))
        let key = struct (spec.Family, spec.Weight, spec.Italic, BitConverter.SingleToInt32Bits sizePx)

        match fonts.TryGetValue key with
        | true, hit -> hit
        | _ ->
            let (tf, _, _) = faceOf spec
            let font = new SKFont(tf, sizePx)
            fonts.[key] <- font
            font

    let check () =
        if disposed then
            raise (ObjectDisposedException "Marey.SkiaTextMeasurer")

    /// The dpi this measurer converts `pt` to `px` at. It must be the one the backend renders
    /// at, or the font size in the bitmap is not the font size the panel was sized for.
    member _.Dpi = dpi

    /// The font file this measurer was told to use, if it was told one. `Some` is the D62
    /// answer that makes a render reproducible on another machine.
    member _.FontFile = fontFile

    /// Every distinct face this measurer has been asked for, in first-seen order.
    ///
    /// **Empty until something has been measured.** Resolution is lazy because the alternative
    /// is opening a face for a theme the scene never uses; read it after a render, which is
    /// when "what did this actually draw with" is the question being asked.
    member _.ResolvedFaces: ResolvedFace[] = lock gate (fun () -> order.ToArray())

    /// Resolve a spec and report the face, without measuring anything.
    member _.Face(spec: FontSpec) : ResolvedFace =
        check ()

        lock gate (fun () ->
            let (_, _, face) = faceOf spec
            face)

    /// The cached `SKFont` and `SKShaper` for a spec. **Owned by this measurer** — the caller
    /// must not dispose either, and must not use them after this measurer is disposed.
    ///
    /// Internal because it puts SkiaSharp types in a signature, and the only caller that needs
    /// them is the backend in the next file, which must draw with exactly what it measured.
    member internal _.Shaping(spec: FontSpec) : struct (SKFont * SKShaper) =
        check ()

        lock gate (fun () ->
            let font = fontOf spec
            let (_, shaper, _) = faceOf spec
            struct (font, shaper))

    interface ITextMeasurer with

        member this.Measure (spec: FontSpec) (text: string) =
            let struct (font, shaper) = this.Shaping spec

            lock gate (fun () ->
                let advance =
                    // Shaping an empty string returns a zero-glyph run, which is right; asking
                    // first is cheaper, and blank labels are common on a sparse axis.
                    if String.IsNullOrEmpty text then
                        0.0f
                    else
                        (shaper.Shape(text, font)).Width

                // **Font-wide ascent and descent, not the string's ink box.** `TextMetrics` is
                // consumed by `TextMeasure.bounds`, which centres a `BaselineMiddle` run on
                // `(Ascent + Descent) / 2`; measuring the tight box per string would make `e`
                // and `E` centre at different heights and every tick label would sit at its own
                // baseline.
                //
                // Skia's `Ascent` is **negative** (above the baseline in a y-down space) while
                // Core's is positive, so it is negated here — the same conversion
                // `SkiaBackend.textOrigin` makes, for the same reason. Getting it right in one
                // place and wrong in the other would put labels a full line-height out.
                let m = font.Metrics

                { Advance = float advance * 1.0<px>
                  Ascent = float -m.Ascent * 1.0<px>
                  Descent = float m.Descent * 1.0<px> })

    interface IDisposable with
        member _.Dispose() =
            lock gate (fun () ->
                if not disposed then
                    disposed <- true

                    for KeyValue(_, font) in fonts do
                        font.Dispose()

                    fonts.Clear()

                    for KeyValue(_, (tf, shaper, _)) in faces do
                        shaper.Dispose()

                        // A stated face is shared by every entry, so disposing it per entry
                        // would double-free. It is disposed once, below.
                        if stated.IsNone && not (isNull tf) then
                            tf.Dispose()

                    faces.Clear()

                    match stated with
                    | Some tf -> tf.Dispose()
                    | None -> ())
