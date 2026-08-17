namespace Quiver

open System.Text

// ======================================================================================
// TEX ESCAPING.
//
// `TextRun.Tex` (Scene.fs) carries TeX source for backends that can typeset it. Two very
// different producers have to put well-formed TeX into that field:
//
//   * `Quiver.Units`, composing an axis title out of a caller-supplied quantity name and a
//     `\qty{}{\metre\per\second\squared}`-style unit. The unit half is already TeX; the name
//     half is arbitrary text and may contain `%`, `_` or `&`.
//   * a TeX backend, escaping the plain `TextRun.Text` fallback of every run that has no `Tex`
//     form at all.
//
// `Quiver.Core` is the only ancestor those two share — a backend must not depend on
// `Quiver.Units` (a renderer has no business knowing about physical dimensions), and
// `Quiver.Units` must not depend on a backend. So this lives here, for exactly the reason `Num`
// does: two consumers, no other common ancestor, zero dependencies, and F2's rule that shared
// text-emission rules get promoted rather than copied.
//
// Deliberately *not* here: the Unicode-to-macro table (`·` -> `\textperiodcentered`, `Ω` ->
// `\textohm`). That is a rendering policy — which package a backend expects you to load, and
// whether it prefers `textcomp` or `\ensuremath` — so it belongs to the backend that has an
// opinion about its own preamble, not to Core.
// ======================================================================================

/// TeX source escaping. Shared by `Quiver.Units` (composing axis titles) and any backend that
/// emits TeX.
[<RequireQualifiedAccess>]
module Tex =

    /// Escape a string so TeX typesets it literally.
    ///
    /// The ten characters TeX treats specially, and the two that need a macro rather than a
    /// backslash: `~` and `^` are *active*/superscript rather than merely reserved, so
    /// `\~` and `\^` are accents that would silently swallow the following character instead of
    /// printing a tilde or a caret.
    ///
    /// Escaping is done character by character rather than by successive string replacement,
    /// because replacing `\` with `\textbackslash{}` first and `{` with `\{` afterwards would
    /// re-escape the braces the first replacement just introduced.
    let escape (s: string) : string =
        if isNull s then
            ""
        else
            let sb = StringBuilder(s.Length + 8)

            for c in s do
                match c with
                | '\\' -> sb.Append "\\textbackslash{}" |> ignore
                | '{' -> sb.Append "\\{" |> ignore
                | '}' -> sb.Append "\\}" |> ignore
                | '$' -> sb.Append "\\$" |> ignore
                | '&' -> sb.Append "\\&" |> ignore
                | '#' -> sb.Append "\\#" |> ignore
                | '%' -> sb.Append "\\%" |> ignore
                | '_' -> sb.Append "\\_" |> ignore
                | '~' -> sb.Append "\\textasciitilde{}" |> ignore
                | '^' -> sb.Append "\\textasciicircum{}" |> ignore
                | c -> sb.Append c |> ignore

            sb.ToString()

    /// True when `escape` would change the string — i.e. it contains no TeX special character
    /// and can be passed through verbatim.
    let isLiteralSafe (s: string) : bool =
        if isNull s then
            true
        else
            s
            |> Seq.forall (fun c ->
                match c with
                | '\\'
                | '{'
                | '}'
                | '$'
                | '&'
                | '#'
                | '%'
                | '_'
                | '~'
                | '^' -> false
                | _ -> true)
