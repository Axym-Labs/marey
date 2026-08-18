namespace Marey

open System.Text

// ======================================================================================
// TEX ESCAPING.
//
// `TextRun.Tex` (Scene.fs) carries TeX source for backends that can typeset it. Two very
// different producers have to put well-formed TeX into that field:
//
//   * `Marey.Units`, composing an axis title out of a caller-supplied quantity name and a
//     `\qty{}{\metre\per\second\squared}`-style unit. The unit half is already TeX; the name
//     half is arbitrary text and may contain `%`, `_` or `&`.
//   * a TeX backend, escaping the plain `TextRun.Text` fallback of every run that has no `Tex`
//     form at all.
//
// `Marey.Core` is the only ancestor those two share — a backend must not depend on
// `Marey.Units` (a renderer has no business knowing about physical dimensions), and
// `Marey.Units` must not depend on a backend. So this lives here, for exactly the reason `Num`
// does: two consumers, no other common ancestor, zero dependencies, and F2's rule that shared
// text-emission rules get promoted rather than copied.
//
// Deliberately *not* here: the Unicode-to-macro table (`·` -> `\textperiodcentered`, `Ω` ->
// `\textohm`). That is a rendering policy — which package a backend expects you to load, and
// whether it prefers `textcomp` or `\ensuremath` — so it belongs to the backend that has an
// opinion about its own preamble, not to Core.
// ======================================================================================

/// TeX source escaping. Shared by `Marey.Units` (composing axis titles) and any backend that
/// emits TeX.
[<RequireQualifiedAccess>]
module Tex =

    /// The replacement for one TeX-special character, or `None` when the character needs none.
    ///
    /// The ten characters TeX treats specially, and the two that need a macro rather than a
    /// backslash: `~` and `^` are *active*/superscript rather than merely reserved, so
    /// `\~` and `\^` are accents that would silently swallow the following character instead of
    /// printing a tilde or a caret.
    ///
    /// **And three that TeX does not treat specially at all, which is the problem.** `<`, `>` and
    /// `|` are perfectly legal in text mode and produce no error whatsoever — they simply are not
    /// the glyphs you asked for. In the OT1 encoding LaTeX still uses by default, those slots hold
    /// `¡`, `¿` and `—`, so an axis label reading `a < b` typesets as `a ¡ b` in a document that
    /// compiled without a single warning. This was found by rendering the golden `text.tex` and
    /// looking at it, which is the only way it *could* have been found: it is invisible to the
    /// compile gate by construction, because there is nothing wrong with the TeX.
    ///
    /// `"` is deliberately left alone. It renders as a curly `”`, which is what a TeX author
    /// expects and is typographically the better glyph — and `\textquotedbl`, the only macro that
    /// would give a straight one, raises `Command \textquotedbl unavailable in encoding OT1`. The
    /// same goes for `` ` `` and for the `--`/`---` ligatures: conventional TeX behaviour, not a
    /// defect.
    ///
    /// This is the single table, exposed per character rather than only per string, so that a
    /// backend interleaving its own substitutions — a Unicode-to-macro map, superscript runs —
    /// can reuse it instead of keeping a second copy that drifts. Returning `None` for the
    /// common case keeps the string path allocation-free.
    let tryEscapeChar (c: char) : string option =
        match c with
        | '\\' -> Some "\\textbackslash{}"
        | '{' -> Some "\\{"
        | '}' -> Some "\\}"
        | '$' -> Some "\\$"
        | '&' -> Some "\\&"
        | '#' -> Some "\\#"
        | '%' -> Some "\\%"
        | '_' -> Some "\\_"
        | '~' -> Some "\\textasciitilde{}"
        | '^' -> Some "\\textasciicircum{}"
        // Legal, silent, and wrong. See above.
        | '<' -> Some "\\textless{}"
        | '>' -> Some "\\textgreater{}"
        | '|' -> Some "\\textbar{}"
        | _ -> None

    /// Escape a string so TeX typesets it literally.
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
                match tryEscapeChar c with
                | Some replacement -> sb.Append replacement |> ignore
                | None -> sb.Append c |> ignore

            sb.ToString()

    /// True when `escape` would leave the string unchanged — i.e. it contains no TeX special
    /// character and can be passed through verbatim.
    let isLiteralSafe (s: string) : bool =
        isNull s || s |> Seq.forall (fun c -> (tryEscapeChar c).IsNone)
