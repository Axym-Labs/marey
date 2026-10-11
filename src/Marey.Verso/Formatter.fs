namespace Marey.Verso

open System
open System.Threading.Tasks
open Verso.Abstractions

// ======================================================================================
// THE FORMATTER.
//
// ---------------------------------------------------------------------------------------
// WHY THIS PACKAGE HAS TO EXIST, RATHER THAN A `ToHtml()` MEMBER.
// ---------------------------------------------------------------------------------------
//
// Verso's built-in `HtmlFormatter` is duck-typed: it claims any value whose type has a public
// parameterless `ToHtml() : string`. That looks like the cheap way in — no extension, no install,
// no consent prompt — and it does not work here, for two independent reasons.
//
// **It would tie.** `Verso.FSharp`'s own `FSharpDataFormatter` sits at `Priority = 20` and claims
// *every* F# record, union and tuple. `Figure` is a record, `Node` a union, `Color` a record. The
// built-in `HtmlFormatter` is also `Priority = 20`. `FormatterResolver` orders by
// `OrderByDescending(Priority)` — a stable sort — so an equal-priority pair is resolved by
// registration order, which is not a contract. A figure would render as a picture or as a field
// table depending on which extension the host happened to enumerate first. That is not a bug we
// would be able to fix from the outside, and it is not a coin we should be flipping.
//
// **And `Figure` could not have the member anyway.** `ToHtml()` on `Figure` would put HTML
// rendering inside `Marey.Plot`, which would need the SVG backend to do it — and D21 says
// `Marey.Plot` does not reference a backend. `Color[]` settles it beyond argument: an array
// cannot be given a member at all.
//
// So: our own `IDataFormatter`, at a priority that resolves the tie by being higher rather than
// by being lucky.
//
// ---------------------------------------------------------------------------------------
// WHAT IS WORTH SENDING UPSTREAM, AND WHAT IS NOT.
// ---------------------------------------------------------------------------------------
//
// Not this. Verso should not depend on Marey, and a Marey-shaped formatter in Verso's tree
// would be a maintenance burden for someone else. Two things underneath it generalise, though:
//
//   1. **`ToHtml()` takes no arguments.** `IFormatterContext` already carries `MaxWidth` and
//      `MaxHeight`, and the duck-typed probe throws that away, so nothing rendered through the
//      built-in `HtmlFormatter` can size itself to the cell. Probing for a context-aware overload
//      before the parameterless one is a small, backward-compatible change that would let every
//      library with a visual output do what `extentForHost` does here.
//   2. **The priority tie above** is a latent ambiguity in the shipped set, not a Marey problem.
//
// Both are reportable without a line of Marey in them, which is the test of whether a
// contribution belongs upstream.
// ======================================================================================

/// Displays Marey values as HTML in a Verso notebook.
[<VersoExtension>]
[<Sealed>]
type MareyFormatter() =

    // ---- IExtension ------------------------------------------------------------------

    interface IExtension with
        member _.ExtensionId = "dev.marey.formatter"
        member _.Name = "Marey"
        member _.Version = "1.0.0"
        member _.Author = "Marey contributors"

        member _.Description =
            "Renders Marey figures, films, scenes, colours, ramps, palettes, dimensions, unit witnesses and erased scales as inline HTML. A film plays in the cell as a flipbook, and a live figure's controls answer from the kernel."

        member _.OnLoadedAsync(_context: IExtensionHostContext) = Task.CompletedTask
        member _.OnUnloadedAsync() = Task.CompletedTask

    // ---- IDataFormatter --------------------------------------------------------------

    interface IDataFormatter with

        /// `obj`, because the types this claims live in an assembly this one deliberately does not
        /// reference — naming them would defeat the whole design. `SupportedTypes` is documented
        /// as a fast pre-filter rather than a contract, and `CanFormat` does the real work.
        member _.SupportedTypes = [| typeof<obj> |] :> System.Collections.Generic.IReadOnlyList<Type>

        /// Above the 20 shared by `verso.fsharp.formatter` and `verso.formatter.html`, which is
        /// the entire reason this number is not 10. Not higher than necessary: a reader who
        /// installs something more specific for a Marey type should still win.
        member _.Priority = 30

        /// Explicitly not a fallback. Verso lets a language kernel ask for specialised formatters
        /// only, keeping its own native output when none claims the value — and a formatter that
        /// answers for a narrow set of types must say so, or it suppresses that behaviour for
        /// everything else in the notebook.
        member _.IsFallback = false

        member _.CanFormat(value: obj, context: IFormatterContext) =
            not (isNull value)
            && String.Equals(context.MimeType, "text/html", StringComparison.OrdinalIgnoreCase)
            && Bridge.canRender value

        member _.FormatAsync(value: obj, context: IFormatterContext) =
            // M4.5 — a live figure is a page, live over a channel when the host offers one and
            // static otherwise. See `Live.fs`.
            match Live.format value context with
            | Some output -> output
            | None ->
            // Step F (D160) — a figure drawn on a canvas is a page in a frame, because an `Html`
            // output is inserted as markup and its script would never run.
            match Bridge.document context.MaxWidth context.MaxHeight value with
            | Some doc -> Task.FromResult(CellOutput.Widget doc)
            | None ->
                // `CanFormat` said yes, but it said yes a moment ago and against a probe. If the
                // render comes back empty the honest move is an empty HTML output rather than a
                // throw: the resolver treats a throwing formatter as broken and skips it for the rest
                // of the resolution, which would take the kernel's own table down with it.
                let html =
                    match Bridge.render context.MaxWidth context.MaxHeight value with
                    | Some html -> html
                    | None -> ""

                Task.FromResult(CellOutput.Html html)
