namespace Marey

open System
open Fable.Core

// ======================================================================================
// TEXT IN THE BROWSER
//
// The browser lays out and draws, so the measurer is the canvas's own `measureText`: synchronous,
// and answered in the font the canvas will draw with. The trap is a web font that has not
// arrived. `measureText` then measures the fallback face, layout fixes its gutters against it,
// and the real face lands a moment later into labels sized for another one: a font
// resolved once, broken by a race nobody sees in development, where the font is cached.
//
// So the canvas measurer refuses to measure until `document.fonts` has finished loading, and the
// way to get a measurer *now* says which one it is: the canvas's when the fonts are in, and
// `ApproxTextMeasurer` with a note otherwise. A figure laid out against the second is a degraded
// figure and is reported as one, as any other is.
// ======================================================================================

/// A font as CSS writes it, from the IR's `FontSpec`.
[<RequireQualifiedAccess>]
module internal CssFont =

    let private generic =
        set [ "serif"; "sans-serif"; "monospace"; "cursive"; "fantasy"; "system-ui"; "ui-serif"; "ui-sans-serif"; "ui-monospace"; "math" ]

    /// A family list with every name that is not a CSS keyword quoted, so `Segoe UI` and
    /// `-apple-system` survive the parser as the names they are.
    let family (families: string) : string =
        families.Split ','
        |> Array.map (fun f -> f.Trim())
        |> Array.filter (fun f -> f <> "")
        |> Array.map (fun f ->
            if generic.Contains(f.ToLowerInvariant()) || f.StartsWith "\"" then f
            else "\"" + f.Replace("\"", "") + "\"")
        |> String.concat ", "

    /// `italic 700 13.333px "Inter", sans-serif`: the canvas's `font` for a spec, at the
    /// screen's 96 px to the inch, which is the resolution layout measured at.
    let ofSpec (font: FontSpec) : string =
        let px = float (Space.pxOfPt Space.screenDpi font.Size)

        sprintf
            "%s%d %spx %s"
            (if font.Italic then "italic " else "")
            font.Weight
            (Num.upTo 3 px)
            (family font.Family)

/// Whether the document's web fonts have arrived.
[<RequireQualifiedAccess>]
module WebFonts =

    [<Emit("(typeof document === 'undefined' || !document.fonts) ? 'loaded' : document.fonts.status")>]
    let private status () : string = jsNative

    [<Emit("(typeof document === 'undefined' || !document.fonts) ? Promise.resolve() : document.fonts.ready.then(function () {})")>]
    let private readyPromise () : JS.Promise<unit> = jsNative

    /// True once `document.fonts` has nothing left to load, and where there is no document to
    /// load any.
    let ready () : bool = status () = "loaded"

    /// Resolves when `ready` is true.
    let whenReady () : JS.Promise<unit> = readyPromise ()

/// **The canvas's own measurer**, which refuses until the fonts are ready.
///
/// Advances are `measureText`'s width; the ascent and descent are the font's box, as Skia's
/// measurer reports a face's, so a centred label sits where the canvas will draw it. A browser
/// too old to report the box gets the em fractions `ApproxTextMeasurer` uses.
///
/// Measures on a scratch surface of its own, so a measurement in the middle of a draw does not
/// disturb the drawing context's font.
[<Sealed>]
type CanvasTextMeasurer() =

    let scratch = lazy (Canvas.scratch 1 1)
    let cache = Collections.Generic.Dictionary<string, TextMetrics>()

    static member internal Fallback (font: FontSpec) (text: string) = TextMeasure.approx.Measure font text

    interface ITextMeasurer with
        member _.Measure (font: FontSpec) (text: string) =
            if not (WebFonts.ready ()) then
                invalidOp (
                    "Marey: the document's fonts are still loading, so a measurement now would be of the fallback face. "
                    + "Lay out after WebFonts.whenReady (), or with WebText.measurerNow, which says when it has approximated."
                )

            let css = CssFont.ofSpec font
            let key = css + "\u0000" + text

            match cache.TryGetValue key with
            | true, m -> m
            | _ ->
                let ctx = scratch.Force()
                ctx.font <- css
                let m = ctx.measureText text
                let approx = CanvasTextMeasurer.Fallback font text
                let finite (x: float) = not (isNull (box x)) && not (Double.IsNaN x)

                let metrics =
                    { Advance = m.width * 1.0<px>
                      Ascent = (if finite m.fontBoundingBoxAscent then m.fontBoundingBoxAscent * 1.0<px> else approx.Ascent)
                      Descent = (if finite m.fontBoundingBoxDescent then m.fontBoundingBoxDescent * 1.0<px> else approx.Descent) }

                cache.[key] <- metrics
                metrics

/// The measurer a figure was laid out with, and whether it was the real one.
[<NoComparison; NoEquality>]
type WebMeasurer =
    { Measurer: ITextMeasurer
      /// `None` for the canvas's own measurer. `Some reason` when layout fell back to
      /// `ApproxTextMeasurer`: a figure compiled with it is approximated, and should say so.
      Approximated: string option }

[<RequireQualifiedAccess>]
module WebText =

    [<Emit("$0.then($1)")>]
    let private andThen (_p: JS.Promise<'a>) (_f: 'a -> 'b) : JS.Promise<'b> = jsNative

    let private canvas = lazy (CanvasTextMeasurer() :> ITextMeasurer)

    /// **A measurer for now.** The canvas's when the document's fonts have arrived; otherwise
    /// `ApproxTextMeasurer`, with the reason, so the figure laid out against it can be reported
    /// as approximated rather than looking right.
    let measurerNow () : WebMeasurer =
        if WebFonts.ready () then
            { Measurer = canvas.Force(); Approximated = None }
        else
            { Measurer = TextMeasure.approx
              Approximated =
                Some "the document's fonts were still loading, so the layout used Marey's approximate advance widths" }

    /// **The canvas's measurer, once the fonts have arrived.** What a page that can wait a frame
    /// should lay out with.
    let measurer () : JS.Promise<ITextMeasurer> =
        andThen (WebFonts.whenReady ()) (fun () -> canvas.Force())
