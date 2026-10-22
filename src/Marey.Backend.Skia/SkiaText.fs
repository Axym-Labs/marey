namespace Marey

open System
open System.Collections.Generic
open SkiaSharp
open SkiaSharp.HarfBuzz

// ======================================================================================
// "ARCHITECT `ITextShaper` AS A CAPABILITY FROM DAY ONE", CASHED IN.
//
// `ITextMeasurer` began with exactly one implementation behind it, and the comment on that seam
// said what it was for: "having one seam here is what makes it possible to start with an
// approximation and swap in HarfBuzz without touching the layout code."
// This file is the second implementation, and that claim is now tested rather than asserted.
//
// `ApproxTextMeasurer` sums a Helvetica advance-width table. It has no kerning, no ligatures and no
// shaping, and text and layout are where most of a plotting library's effort goes. What replaces it
// here is HarfBuzz, through `SKShaper`, against a real face opened from the machine or from a file
// the caller names.
//
// ---------------------------------------------------------------------------------------
// A FONT IS RESOLVED AND *REPORTED*, OR STATED AND OBEYED.
// ---------------------------------------------------------------------------------------
//
// `Theme.Sans` is a CSS stack: "Inter, -apple-system, BlinkMacSystemFont, Segoe UI, Roboto,
// Helvetica Neue, Arial, sans-serif". SVG hands that string to whatever opens the file; TikZ
// ignores it and inherits the document's font. Skia has to pick one real face, and it
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
// The third, with the second available as `SkiaOptions.FontFile` — and the first for the themes'
// own stacks. The package carries Inter, the first name in `Theme.Sans`, and Liberation Serif,
// the metric twin of Times New Roman in `Theme.Serif` (both SIL OFL 1.1, `fonts/` in the
// package), and a stack that names a carried family resolves to it before anything installed.
// So a figure in a built-in theme is the same PNG on every machine; a stack that names no carried
// family still resolves against the machine and is reported. The test suite states a file
// (`tests/Marey.Tests/fonts/LiberationSans-Regular.ttf`) so its numbers do not move with the
// carried faces.
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

// ======================================================================================
// GATE 8 — A MISSING NATIVE ASSET FAILS LOUDLY.
//
// This is the only package in the solution that needs something the .NET runtime cannot supply
// on its own, and there are **two** native libraries behind it on **two** version lines:
// `libSkiaSharp` and `libHarfBuzzSharp`. Each ships as a per-platform asset package, and
// neither pulls the Linux one in by default — which is the omission this project has now made
// twice and caught twice by reading `dotnet list package`.
//
// Left alone, the third time surfaces as a `DllNotFoundException` from three frames inside a
// render, on somebody else's CI, naming a file rather than a package. An earlier incident was a
// valid document with no visible ink; this is the same class of failure with a different cause,
// and the fix is the same one: say what is wrong at the point it can still be acted on.
// ======================================================================================

/// Diagnosing a native-loading failure, and turning it into a sentence that names the fix.
[<RequireQualifiedAccess>]
module SkiaNative =

    /// The asset packages for the platform actually being run on.
    ///
    /// Named rather than inferred from a RID string: a caller reading this message wants
    /// something they can paste into `dotnet add package`, and `linux-musl-x64` is not that.
    let private packagesFor () =
        if Runtime.InteropServices.RuntimeInformation.IsOSPlatform Runtime.InteropServices.OSPlatform.Linux then
            "SkiaSharp.NativeAssets.Linux and HarfBuzzSharp.NativeAssets.Linux",
            " On a slim container image the packages are not enough on their own: libfontconfig1 and libfreetype6 have to be installed too."
        elif Runtime.InteropServices.RuntimeInformation.IsOSPlatform Runtime.InteropServices.OSPlatform.OSX then
            "SkiaSharp.NativeAssets.macOS and HarfBuzzSharp.NativeAssets.macOS", ""
        elif Runtime.InteropServices.RuntimeInformation.IsOSPlatform Runtime.InteropServices.OSPlatform.Windows then
            "SkiaSharp.NativeAssets.Win32 and HarfBuzzSharp.NativeAssets.Win32", ""
        else
            "the SkiaSharp.NativeAssets and HarfBuzzSharp.NativeAssets package for this platform", ""

    /// Is this exception, or anything it wraps, a failure to load a native library?
    ///
    /// The chain matters. A static initialiser that touches Skia surfaces the real cause as the
    /// `InnerException` of a `TypeInitializationException`, and a test that only looked at the
    /// outer type would miss the most common shape of this failure entirely.
    let rec private isNativeLoad (e: exn) =
        match e with
        | :? DllNotFoundException
        | :? EntryPointNotFoundException
        | :? BadImageFormatException -> true
        | null -> false
        | _ -> not (isNull e.InnerException) && isNativeLoad e.InnerException

    /// A message naming the package to install, for a native-loading failure — and `None` for
    /// anything else, so an ordinary bug is never dressed up as a missing dependency.
    let tryDiagnose (e: exn) : string option =
        if isNativeLoad e then
            let packages, extra = packagesFor ()

            Some(
                sprintf
                    "Marey.Backend.Skia: the native SkiaSharp or HarfBuzz library could not be loaded. Add a PackageReference to %s.%s (The underlying error was: %s)"
                    packages
                    extra
                    (e.GetBaseException().Message)
            )
        else
            None

    /// Run something that touches the native libraries, translating a loading failure into a
    /// message that names the fix.
    ///
    /// Wrapped at the points where a native library is *first* touched — `Begin`, `Finish`,
    /// `Result`, and opening a face — rather than around every call, because once `Begin` has
    /// produced a surface the library is loaded and a closure per `Draw` would be an allocation
    /// on the hot path for a failure that can no longer happen.
    let guard (body: unit -> 'a) : 'a =
        try
            body ()
        with e ->
            match tryDiagnose e with
            | Some message -> raise (PlatformNotSupportedException(message, e))
            | None -> reraise ()


// ======================================================================================
// THE SEAM DOES NOT MOVE; THE SPLITTING HIDES IN THE MEASURER; THE FALLBACK REPORTS.
//
// `SKShaper` shapes against **one** typeface and has no fallback list, so a codepoint that face
// lacks becomes glyph 0 and measures as the missing-glyph box — 9.0 px at 12 px in Liberation
// Sans — where a browser opening the same figure as SVG walks the whole stack and finds a face
// that has it. This is the fix.
//
// It does not reach the unit system: `·`, `²`, `⁻¹`, `Ω`, `µ` and `Å` are in any Latin face and
// a test says so. It reaches a CJK axis title, and the fix is a *list* of faces rather than one.
//
// Three things were decided, and the first is the one that costs nothing:
//
//   * **`ITextMeasurer` does not move.** The seam holds without a line of `Layout.fs` or
//     `Figure.fs` changing, and segmentation is an implementation detail of a measurer that
//     owns real faces — the approximate measurer has one advance table and no notion of
//     coverage at all. So `Measure` still answers a width, and the splitting happens behind it.
//   * **The backend gets the richer view internally**, exactly as `Shaping` served it before.
//     Measuring and drawing must agree, or an `AnchorMiddle` title lands 3.5 px off. A
//     backend that measured a segmented run and drew it with one face would be that bug with
//     more steps.
//   * **And it reports.** Font resolution is "resolved and *reported*, or stated and obeyed".
//     A fallback that silently swapped a face would have abandoned the first half, so
//     `ResolvedFace` carries which faces covered what, and which codepoints nothing covered.
//
// **THE RULE IS PER CODEPOINT, FIRST FACE THAT HAS IT** — CSS font matching's rule.
// It has a consequence worth stating rather than discovering: a space comes back to
// the primary face, because the primary has a space, so a CJK title with a space in it is three
// segments and not one. That is what a browser does with the same string. The alternative —
// letting a fallback hold the run until it fails — sets Latin text in a CJK face whenever the
// two are adjacent, which is the more visible error.
//
// **A SEGMENT BOUNDARY IS A SHAPING BOUNDARY**, and that is a real cost: kerning is not applied
// across it. It is small because boundaries fall where coverage fails, which is where the script
// changes, and no face kerns across a script change.
// ======================================================================================

/// A face that covered part of a run its primary face could not: the "and it reports" half.
///
/// One of these exists on a `ResolvedFace` for every face that actually drew something, and
/// none for a run the primary covered on its own — which is every run in a Latin figure, so the
/// common case reports an empty array rather than a story about itself.
type FallbackFace =
    { /// The family name of the face that was opened.
      Family: string
      /// The file it came from, when the caller stated a list rather than letting the machine
      /// answer. `Some` means this render is reproducible on another machine.
      File: string option
      /// The codepoints it covered, **ascending**. Ascending rather than first-seen, because
      /// this is a report a reader diffs between two renders, and first-seen order would make
      /// two identical figures differ by the order their labels happened to be laid out in.
      Covered: int[] }

/// What a font family stack resolved to on this machine: the "and report it" half.
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
      File: string option
      /// The face is one this package carries, so the render is the same on every machine.
      Bundled: bool
      /// The faces that covered codepoints this one lacks, in first-seen order. Empty
      /// when this face drew every run it was asked for, which is the ordinary case.
      Fallbacks: FallbackFace[]
      /// The codepoints **nothing** covered, ascending. These were drawn as `.notdef` in
      /// the face above, and this array is the difference between a figure that fell back and a
      /// figure that has boxes in it.
      Missing: int[] }

/// A face this measurer has opened: the typeface, the shaper bound to it, and its sized fonts.
///
/// One of these per distinct face — the primary, each stated fallback, each face the machine
/// answered with — created once and registered once, so that disposal can walk a single list
/// and free everything exactly once. The alternative, which is what stood here before a stated
/// face could be shared by more than one entry, was a `if stated.IsNone` branch in `Dispose`
/// guarding against a double free; a handle that is created in one place cannot be freed twice.
[<NoComparison; NoEquality>]
type internal FaceHandle =
    { Typeface: SKTypeface
      Shaper: SKShaper
      /// The file it was opened from, when the caller stated one.
      File: string option
      /// Opened from a face this package carries.
      Bundled: bool
      /// Sized fonts, keyed by the exact bits of the size, because two sizes differing in the
      /// last place are two different `SKFont`s to Skia.
      Fonts: Dictionary<int, SKFont> }

/// One `(family, weight, italic)` a render has asked for: the face it resolved to, and the
/// account of everything fallback has had to do for it since.
[<NoComparison; NoEquality>]
type internal FaceSlot =
    { Primary: FaceHandle
      Requested: string
      Weight: int
      Italic: bool
      Matched: string option
      /// Fallback faces actually used, in first-seen order, each with what it covered.
      Fallbacks: ResizeArray<FaceHandle * SortedSet<int>>
      /// Codepoints no face covered.
      Missing: SortedSet<int>
      /// Which face draws a codepoint: `-1` the primary — which is also where a `.notdef` box
      /// is drawn — otherwise an index into `Fallbacks`.
      ///
      /// **Decided once and never revisited.** One measurer serves a whole film
      /// precisely so that a video cannot change typeface on a frame boundary, and a fallback
      /// that asked the font manager again on frame 200 would put that back.
      Decision: Dictionary<int, int> }

/// A run of text split by coverage, and the vertical band the whole of it shares.
///
/// Internal because it puts SkiaSharp types in a signature, and the only caller that needs them
/// is the backend in the next file, which must draw with exactly what it measured.
[<NoComparison; NoEquality>]
type internal ShapedRun =
    { /// The pieces, in order: concatenating the strings gives the original back. Empty for an
      /// empty string.
      Segments: struct (SKFont * SKShaper * string)[]
      /// Ascent and descent, **positive and y-up** the way `TextMetrics` wants them, taken as
      /// the maximum over the primary face and every face a segment uses.
      ///
      /// The maximum, and the primary is always in it. Font-wide metrics rather than the
      /// string's ink box is what stops `e` and `E` centring at different heights (see
      /// `Measure`); a *run*-wide maximum is the same argument one level up — a run that
      /// reserved only the Latin band would have its CJK glyphs hang out of it, and a run that
      /// reserved only the CJK band would make a Latin label taller than the label beside it.
      /// Including the primary unconditionally is what keeps every number measured without
      /// fallback unchanged: a run the primary covers on its own folds over exactly one face.
      Ascent: float32
      Descent: float32 }

/// **The faces this package carries**: Inter at the four weights the themes use, and Liberation
/// Serif upright, italic and bold. A stack that names one of these families resolves to it before
/// the machine is asked, wherever the name stands in the stack, which is what makes a built-in
/// theme the same PNG on every machine. Inter is subset to Latin, Greek, punctuation, super- and
/// subscripts, letterlike symbols, arrows and the mathematical operators; a codepoint outside that
/// falls back to the machine as any other does. Liberation Serif is carried whole, as its licence
/// asks of a face that keeps its name.
module internal BundledFaces =

    type Face =
        { Family: string
          Weight: int
          Italic: bool
          Resource: string }

    let private face family weight italic file =
        { Family = family
          Weight = weight
          Italic = italic
          Resource = "Marey.Fonts." + file }

    let faces =
        [| face "Inter" 400 false "Inter-Regular.ttf"
           face "Inter" 500 false "Inter-Medium.ttf"
           face "Inter" 600 false "Inter-SemiBold.ttf"
           face "Inter" 700 false "Inter-Bold.ttf"
           face "Liberation Serif" 400 false "LiberationSerif-Regular.ttf"
           face "Liberation Serif" 400 true "LiberationSerif-Italic.ttf"
           face "Liberation Serif" 700 false "LiberationSerif-Bold.ttf" |]

    let private bytes = System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>()

    let private read (resource: string) =
        use stream = typeof<Face>.Assembly.GetManifestResourceStream resource

        if isNull stream then
            invalidOp (sprintf "Marey.Backend.Skia: the carried face '%s' is missing from the assembly." resource)

        use copy = new IO.MemoryStream()
        stream.CopyTo copy
        copy.ToArray()

    /// The carried face of this family nearest the weight, of this slant; `None` when the family is
    /// not carried or has no face of the slant.
    let tryFind (family: string) (weight: int) (italic: bool) : Face option =
        let candidates =
            faces |> Array.filter (fun f -> String.Equals(f.Family, family, StringComparison.OrdinalIgnoreCase) && f.Italic = italic)

        if candidates.Length = 0 then
            None
        else
            // Nearer first; between two equally near, the heavier for a bold request and the
            // lighter otherwise, as CSS matches a weight.
            candidates
            |> Array.minBy (fun f -> abs (f.Weight - weight), (if weight > 500 then -f.Weight else f.Weight))
            |> Some

    /// A new typeface over the face's bytes, which are read from the assembly once.
    let typeface (f: Face) : SKTypeface =
        use data = SKData.CreateCopy(bytes.GetOrAdd(f.Resource, read))
        SKTypeface.FromData data

/// A real text measurer: HarfBuzz shaping through `SKShaper`, against a resolved face.
///
/// This is the first `ITextMeasurer` other than the approximation, and it is the reason
/// `SkiaBackend` advertises `TextShaping = true`. It measures **shaped** advances, so kerning
/// pairs and ligatures reach `Figure.arrangement` and therefore the panel rectangle: a figure
/// laid out through this measurer has different gutters from the same figure laid out through
/// `TextMeasure.approx`, and neither is wrong.
///
/// **`fontFile` and `fontFiles` are two answers to one question and only one may be given.**
/// A single stated file is obeyed alone and never falls back — that is what "stated and obeyed"
/// means, and a one-element list is exactly what it says. A stated *list* means
/// those faces, in that order, and `.notdef` past the end of them. Neither consults the
/// machine. With neither, the theme's stack resolves against what is installed and fallback
/// asks the machine's font manager per codepoint — which is the case a real user is in, and the
/// case that makes a CJK axis title render at all.
///
/// Locked rather than accidentally single-threaded — a measurer is shared with the backend that
/// owns it, and a caller can reasonably measure from elsewhere.
[<Sealed>]
type SkiaTextMeasurer(?fontFile: string, ?dpi: float<px / mm>, ?fontFiles: string list) =

    let dpi = defaultArg dpi Space.screenDpi
    let fontFile = fontFile |> Option.filter (String.IsNullOrWhiteSpace >> not)

    let fontFiles =
        defaultArg fontFiles [] |> List.filter (String.IsNullOrWhiteSpace >> not)

    do
        if fontFile.IsSome && not (List.isEmpty fontFiles) then
            invalidArg
                "fontFiles"
                "Marey.SkiaTextMeasurer: a stated file and a stated list are two answers to one question. Pass fontFiles alone -- a one-element list is what fontFile means."

    /// Every native handle this measurer has opened, in creation order. Disposal walks exactly
    /// this, so nothing is freed twice and nothing is missed.
    let handles = ResizeArray<FaceHandle>()

    let gate = obj ()
    let mutable disposed = false

    /// Register a typeface: one shaper, one font cache, one entry in the disposal list.
    let handleWith (bundled: bool) (tf: SKTypeface) (file: string option) =
        let h =
            { Typeface = tf
              Shaper = new SKShaper(tf)
              File = file
              Bundled = bundled
              Fonts = Dictionary<int, SKFont>() }

        handles.Add h
        h

    let handleOf (tf: SKTypeface) (file: string option) = handleWith false tf file

    /// Open a stated file, eagerly: a bad path should fail at construction with a message
    /// naming the path, not on the first tick label with a null dereference.
    let openStated (path: string) =
        if not (IO.File.Exists path) then
            invalidArg "fontFile" (sprintf "Marey.SkiaTextMeasurer: no font file at '%s'." path)

        let tf = SKTypeface.FromFile path

        if isNull tf then
            invalidArg "fontFile" (sprintf "Marey.SkiaTextMeasurer: '%s' is not a font file Skia can open." path)

        handleOf tf (Some path)

    /// The stated chain: empty when nothing was stated, one entry for a stated file, n for a
    /// stated list. Its head is the primary for **every** spec — a stated face short-circuits
    /// resolution entirely, **including the weight and the slant**: a caller who names
    /// `LiberationSans-Regular.ttf` and then asks for bold gets the regular face, because that
    /// is the file they named. Synthesising a bold from it would put a face in the figure that
    /// is not the one on disk, and silently so.
    let statedChain =
        // Gate 8: opening a face is where `libSkiaSharp` is first touched, and `new SKShaper` is
        // where `libHarfBuzzSharp` is.
        SkiaNative.guard (fun () ->
            match fontFile with
            | Some path -> [| openStated path |]
            | None -> fontFiles |> List.map openStated |> Array.ofList)

    /// Faces the machine answered a fallback question with, so that two slots asking for the
    /// same family share one typeface and one shaper.
    let machine = Dictionary<struct (string * int * bool), FaceHandle>()

    /// Slots, keyed by what was asked for rather than by what was got: two stacks that resolve
    /// to the same family are still two questions, and the second is a cache hit only if the
    /// question was the same.
    let slots = Dictionary<struct (string * int * bool), FaceSlot>()

    /// First-seen order, which is traversal order, which is what makes the report readable
    /// against the scene that produced it.
    let order = ResizeArray<FaceSlot>()

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

        let carried =
            names |> Array.tryPick (fun n -> BundledFaces.tryFind n weight italic |> Option.map (fun f -> n, f))

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

        match carried with
        | Some(name, face) -> BundledFaces.typeface face, Some name, true
        | None ->
            let tf, matched = pick 0
            tf, matched, false

    let slotOf (spec: FontSpec) =
        let key = struct (spec.Family, spec.Weight, spec.Italic)

        match slots.TryGetValue key with
        | true, hit -> hit
        | _ ->
            let primary, matched =
                if statedChain.Length > 0 then
                    statedChain.[0], None
                else
                    SkiaNative.guard (fun () ->
                        let tf, matched, bundled = resolveStack spec.Family spec.Weight spec.Italic
                        handleWith bundled tf None, matched)

            let slot =
                { Primary = primary
                  Requested = spec.Family
                  Weight = spec.Weight
                  Italic = spec.Italic
                  Matched = matched
                  Fallbacks = ResizeArray()
                  Missing = SortedSet<int>()
                  Decision = Dictionary<int, int>() }

            slots.[key] <- slot
            order.Add slot
            slot

    /// Ask the machine which installed face has this codepoint, at the weight and slant the run
    /// asked for.
    ///
    /// **The answer is verified rather than trusted.** `MatchCharacter` goes to fontconfig on
    /// Linux, and fontconfig answers a *best match*, which can be a face that does not have the
    /// character at all — the same trap `resolveStack` above disarms by comparing the family
    /// name back. Without the check, `Missing` would always be empty and its test
    /// would pass on a fallback that had silently drawn a box.
    let matchOnMachine (slot: FaceSlot) (cp: int) =
        let skWeight: SKFontStyleWeight = enum slot.Weight
        let skSlant = if slot.Italic then SKFontStyleSlant.Italic else SKFontStyleSlant.Upright

        let tf =
            SKFontManager.Default.MatchCharacter(null, skWeight, SKFontStyleWidth.Normal, skSlant, null, cp)

        // Coverage is asked of an `SKFont` rather than of the typeface: `SKTypeface.ContainsGlyph`
        // is deprecated in SkiaSharp 4.151 and the project builds warnings as errors. A throwaway
        // font at the default size is enough — a cmap does not depend on a point size — and this
        // face is about to be discarded if the answer is no, so caching one would be caching a
        // face that failed.
        let covered = not (isNull tf) && (use probe = new SKFont(tf) in probe.ContainsGlyph cp)

        if isNull tf then None
        elif not covered then
            tf.Dispose()
            None
        else
            let key = struct (tf.FamilyName, slot.Weight, slot.Italic)

            match machine.TryGetValue key with
            | true, hit ->
                // Another slot already opened this family. Two typefaces for one face would
                // mean two shapers and two font caches for the same glyphs.
                tf.Dispose()
                Some hit
            | _ ->
                let h = handleOf tf None
                machine.[key] <- h
                Some h

    /// Which face draws this codepoint: `-1` the primary, otherwise an index into the slot's
    /// fallbacks. Decided once and cached, so that frame 200 of a film cannot answer it
    /// differently from frame 0.
    let decide (slot: FaceSlot) (cp: int) (covers: FaceHandle -> int -> bool) =
        match slot.Decision.TryGetValue cp with
        | true, d -> d
        | _ ->
            let d =
                if covers slot.Primary cp then
                    -1
                else
                    let found =
                        if statedChain.Length > 0 then
                            // The stated chain, and **only** the stated chain: a caller who
                            // names their faces gets those faces or a box, never a face the
                            // machine chose for them. A stated single file is a chain of one,
                            // so "a stated file gets no fallback" is this line rather than a
                            // special case. Index 0 is the primary and has already missed;
                            // asking it again is one `ContainsGlyph` against a cached face.
                            statedChain |> Array.tryFind (fun h -> covers h cp)
                        else
                            SkiaNative.guard (fun () -> matchOnMachine slot cp)

                    match found with
                    | None ->
                        slot.Missing.Add cp |> ignore
                        // A `.notdef` is drawn in the face that was going to draw the text
                        // anyway, so a missing codepoint does not break a segment.
                        -1
                    | Some h ->
                        let existing =
                            slot.Fallbacks
                            |> Seq.tryFindIndex (fun (x, _) -> obj.ReferenceEquals(x, h))

                        let i =
                            match existing with
                            | Some i -> i
                            | None ->
                                slot.Fallbacks.Add(h, SortedSet<int>())
                                slot.Fallbacks.Count - 1

                        (snd slot.Fallbacks.[i]).Add cp |> ignore
                        i

            slot.Decision.[cp] <- d
            d

    let fontOf (h: FaceHandle) (sizePx: float32) =
        let key = BitConverter.SingleToInt32Bits sizePx

        match h.Fonts.TryGetValue key with
        | true, hit -> hit
        | _ ->
            let font = new SKFont(h.Typeface, sizePx)
            h.Fonts.[key] <- font
            font

    /// Split a string into maximal runs one face draws, and fold the band they share.
    ///
    /// Codepoints, not chars: a surrogate pair is one question to a face, and asking it as two
    /// would report a missing glyph for every character outside the BMP. An *unpaired*
    /// surrogate is asked as itself, misses every face, and lands in `Missing` — which is a
    /// truthful answer to a malformed string and, more to the point, not an exception.
    let runOf (spec: FontSpec) (text: string) =
        let slot = slotOf spec
        let sizePx = float32 (float (Space.pxOfPt dpi spec.Size))
        let primaryFont = fontOf slot.Primary sizePx

        // Coverage, asked of a sized font because `SKTypeface.ContainsGlyph` is deprecated. The
        // fonts are cached per face and per size, so every question after the first is a
        // dictionary lookup, and the answer itself is cached per codepoint in `slot.Decision`.
        let covers (h: FaceHandle) (cp: int) = (fontOf h sizePx).ContainsGlyph cp

        let pieces = ResizeArray<struct (int * int * int)>()
        let mutable i = 0
        let mutable current = Int32.MinValue
        let mutable start = 0

        while i < text.Length do
            let paired =
                Char.IsHighSurrogate text.[i]
                && i + 1 < text.Length
                && Char.IsLowSurrogate text.[i + 1]

            let cp =
                if paired then
                    Char.ConvertToUtf32(text.[i], text.[i + 1])
                else
                    int text.[i]

            let d = decide slot cp covers

            if d <> current then
                if i > start then
                    pieces.Add(struct (current, start, i - start))

                current <- d
                start <- i

            i <- i + (if paired then 2 else 1)

        if text.Length > start then
            pieces.Add(struct (current, start, text.Length - start))

        let mutable ascent = -primaryFont.Metrics.Ascent
        let mutable descent = primaryFont.Metrics.Descent

        let segments =
            pieces.ToArray()
            |> Array.map (fun struct (d, s, len) ->
                let h = if d < 0 then slot.Primary else fst slot.Fallbacks.[d]
                let font = fontOf h sizePx
                ascent <- max ascent -font.Metrics.Ascent
                descent <- max descent font.Metrics.Descent
                struct (font, h.Shaper, text.Substring(s, len)))

        { Segments = segments
          Ascent = ascent
          Descent = descent }

    let snapshot (slot: FaceSlot) =
        { Requested = slot.Requested
          Weight = slot.Weight
          Italic = slot.Italic
          Matched = slot.Matched
          Family = (if isNull slot.Primary.Typeface then "" else slot.Primary.Typeface.FamilyName)
          File = slot.Primary.File
          Bundled = slot.Primary.Bundled
          Fallbacks =
            slot.Fallbacks.ToArray()
            |> Array.map (fun (h, covered) ->
                { Family = h.Typeface.FamilyName
                  File = h.File
                  Covered = Seq.toArray covered })
          Missing = Seq.toArray slot.Missing }

    let check () =
        if disposed then
            raise (ObjectDisposedException "Marey.SkiaTextMeasurer")

    /// The dpi this measurer converts `pt` to `px` at. It must be the one the backend renders
    /// at, or the font size in the bitmap is not the font size the panel was sized for.
    member _.Dpi = dpi

    /// The font file this measurer was told to use, if it was told one. `Some` is the
    /// answer that makes a render reproducible on another machine.
    member _.FontFile = fontFile

    /// The font files this measurer was told to use, in order, if it was told a list;
    /// empty when the theme's stack is resolving against the machine instead.
    member _.FontFiles = fontFiles

    /// Every distinct face this measurer has been asked for, in first-seen order, each with the
    /// account of its fallbacks.
    ///
    /// **Empty until something has been measured.** Resolution is lazy because the alternative
    /// is opening a face for a theme the scene never uses; read it after a render, which is
    /// when "what did this actually draw with" is the question being asked.
    ///
    /// A face's `Fallbacks` and `Missing` likewise only fill in as text goes past — asking
    /// before a render says the figure fell back nowhere, which is true of a figure that has
    /// not been drawn.
    member _.ResolvedFaces: ResolvedFace[] =
        lock gate (fun () -> order.ToArray() |> Array.map snapshot)

    /// Resolve a spec and report the face, without measuring anything.
    member _.Face(spec: FontSpec) : ResolvedFace =
        check ()
        lock gate (fun () -> slotOf spec |> snapshot)

    /// The advance of `text` in the primary face `spec` resolves to, in px, summed glyph by glyph
    /// without shaping and without hinting: what kerning and ligatures are measured against,
    /// `Measure`'s advance less this one.
    member _.MeasureUnshaped(spec: FontSpec, text: string) : float =
        check ()

        lock gate (fun () ->
            let slot = slotOf spec
            use font = new SKFont(slot.Primary.Typeface, float32 (float (Space.pxOfPt dpi spec.Size)))
            font.LinearMetrics <- true
            float (font.MeasureText text))

    /// The run split by coverage, with the fonts and shapers to draw each piece with, and the
    /// vertical band `Measure` reported for the whole of it. **Owned by this measurer** — the
    /// caller must not dispose anything in it, and must not use it after this measurer is
    /// disposed.
    ///
    /// Internal because it puts SkiaSharp types in a signature, and the only caller that needs
    /// them is the backend in the next file, which must draw with exactly what it measured.
    ///
    /// **This replaced `Shaping`, which handed back one font and one shaper.** A segmented run
    /// cannot be drawn with one of either, and the obvious signature for this member —
    /// `FontSpec -> string -> struct (SKFont * string)[]` — is a font short of drawable:
    /// `SKCanvas.DrawShapedText` takes the shaper, and a shaper is bound to a typeface, so a
    /// segment has to carry its own. The metrics ride along for the same reason the segments
    /// do: a backend that folded its own band would be free to fold it differently from the
    /// layout that placed the run.
    member internal _.Segments(spec: FontSpec) (text: string) : ShapedRun =
        check ()
        lock gate (fun () -> runOf spec text)

    interface ITextMeasurer with

        member _.Measure (spec: FontSpec) (text: string) =
            check ()

            lock gate (fun () ->
                let run = runOf spec text

                let advance =
                    // Shaping an empty string returns a zero-glyph run, which is right; asking
                    // first is cheaper, and blank labels are common on a sparse axis.
                    if String.IsNullOrEmpty text then
                        0.0f
                    else
                        // **Summed over the segments, and each shaped in its own face.** For a run
                        // one face covers — every run in a Latin figure — this is one segment and
                        // exactly the number this measurer reported before fallback existed.
                        //
                        // The cost of the split is that kerning does not cross a segment
                        // boundary. Boundaries fall where coverage fails, which is where the
                        // script changes, and no face kerns across a script change.
                        let mutable total = 0.0f

                        for struct (font, shaper, s) in run.Segments do
                            total <- total + (shaper.Shape(s, font)).Width

                        total

                // **Font-wide ascent and descent, not the string's ink box.** `TextMetrics` is
                // consumed by `TextMeasure.bounds`, which centres a `BaselineMiddle` run on
                // `(Ascent + Descent) / 2`; measuring the tight box per string would make `e`
                // and `E` centre at different heights and every tick label would sit at its own
                // baseline.
                //
                // Skia's `Ascent` is **negative** (above the baseline in a y-down space) while
                // Core's is positive; `ShapedRun` has already made the conversion and taken the
                // maximum over the faces the run uses, which is the same fold
                // `SkiaBackend.drawText` places the baseline with. Getting it right in one place
                // and wrong in the other would put labels a full line-height out.
                { Advance = float advance * 1.0<px>
                  Ascent = float run.Ascent * 1.0<px>
                  Descent = float run.Descent * 1.0<px> })

    interface IDisposable with
        member _.Dispose() =
            lock gate (fun () ->
                if not disposed then
                    disposed <- true

                    // One list, one pass, everything once. A stated face is shared by every
                    // slot that asked for it and by every fallback chain it appears in, and it
                    // is in `handles` exactly once, so sharing costs no special case here.
                    for h in handles do
                        for KeyValue(_, font) in h.Fonts do
                            font.Dispose()

                        h.Fonts.Clear()
                        h.Shaper.Dispose()

                        if not (isNull h.Typeface) then
                            h.Typeface.Dispose()

                    handles.Clear()
                    machine.Clear()
                    slots.Clear()
                    order.Clear())
