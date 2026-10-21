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
// The Unicode-to-macro table (`Δ` -> `\ensuremath{\Delta}`, `·` -> `\textperiodcentered{}`) lives
// here too, for the same reason. It began in the TikZ backend, on the argument that it is a
// rendering policy. But an axis title under `Latex` or `Siunitx` is TeX source by the time it
// leaves `Marey.Units`, and a backend passes TeX source through verbatim — so a quantity named
// `|ΔE|` reached `pdflatex` as a raw `Δ`, which it rejects. Literal text inside a TeX title needs
// the same conversion as the backend's plain-text fallback, and the table uses only kernel
// macros (`textcomp` has been part of the kernel since 2020), so there is no preamble opinion
// left to keep in a backend.
// ======================================================================================

/// TeX source escaping. Shared by `Marey.Units` (composing axis titles) and any backend that
/// emits TeX.
///
/// `escape` rewrites a stated set of non-ASCII characters and no others. A character is in the
/// set when LaTeX names it with a kernel macro and a quantity's name or unit uses it:
///
/// * the Greek alphabet, in math mode (`\ensuremath{\theta}`), because a quantity symbol is
///   set in italic;
/// * the unit symbols `µ`, `Ω`, `Å` and `°`, upright, in both their Unicode spellings;
/// * the operators `×`, `±`, `−`, `·`, `⋅`, `≤`, `≥`, `≈`, `≠`, `∂`, `∇` and `∞`;
/// * superscript and subscript digits and signs, a run at a time (`s⁻²` -> `s\textsuperscript{-2}`);
/// * the no-break and thin spaces and the en and em dashes.
///
/// Every other character passes through. Latin letters with diacritics are set by LaTeX's own
/// UTF-8 support; anything else needs a Unicode engine, and turning it into a macro would be a
/// guess about the document.
[<RequireQualifiedAccess>]
module Tex =

    /// The replacement for one TeX-special character, or `None` when the character needs none.
    ///
    /// The ten characters TeX treats specially, and the two that need a macro rather than a
    /// backslash: `~` and `^` are *active* or superscript rather than merely reserved, so
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
    let internal tryEscapeChar (c: char) : string option =
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

    /// The ASCII character a Unicode superscript stands for, or `None`.
    ///
    /// The digits are split across two Unicode blocks — ¹²³ are Latin-1 leftovers at U+00B9,
    /// U+00B2, U+00B3 while ⁰ and ⁴-⁹ live at U+2070, U+2074-U+2079 — which is exactly the kind
    /// of thing a hand-written range check gets wrong. `UnitDisplay.superscript` emits these,
    /// and U+207B SUPERSCRIPT MINUS rather than a hyphen.
    let private superscriptOf (c: char) : char option =
        match c with
        | '⁰' -> Some '0'
        | '¹' -> Some '1'
        | '²' -> Some '2'
        | '³' -> Some '3'
        | '⁴' -> Some '4'
        | '⁵' -> Some '5'
        | '⁶' -> Some '6'
        | '⁷' -> Some '7'
        | '⁸' -> Some '8'
        | '⁹' -> Some '9'
        | '⁻' -> Some '-'
        | '⁺' -> Some '+'
        | _ -> None

    /// The ASCII character a Unicode subscript stands for, or `None`. One block, U+2080-U+208B.
    let private subscriptOf (c: char) : char option =
        if c >= '₀' && c <= '₉' then Some(char (int '0' + int c - int '₀'))
        elif c = '₊' then Some '+'
        elif c = '₋' then Some '-'
        else None

    /// Greek letters, as math-mode macros. **A quantity's conventional name is Greek as often as
    /// not** — ρ for a density, θ, ω and α for an angle and its rates (D98). Math mode because a
    /// quantity symbol is set in italic, which is what `\theta` gives and what physics writes.
    ///
    /// µ and the capital omega are absent here on purpose: `tryMacro` maps them first, to the
    /// upright `\textmu` and `\textohm` a unit wants. Capitals that look like Latin letters (Α, Β,
    /// Ε, …) and the omicron have no macro of their own in LaTeX and are not mapped.
    let private greekMacro (c: char) : string option =
        match c with
        | 'α' -> Some "\\alpha"
        | 'β' -> Some "\\beta"
        | 'γ' -> Some "\\gamma"
        | 'δ' -> Some "\\delta"
        | 'ε' -> Some "\\varepsilon"
        | 'ϵ' -> Some "\\epsilon"
        | 'ζ' -> Some "\\zeta"
        | 'η' -> Some "\\eta"
        | 'θ' -> Some "\\theta"
        | 'ϑ' -> Some "\\vartheta"
        | 'ι' -> Some "\\iota"
        | 'κ' -> Some "\\kappa"
        | 'λ' -> Some "\\lambda"
        | 'ν' -> Some "\\nu"
        | 'ξ' -> Some "\\xi"
        | 'π' -> Some "\\pi"
        | 'ρ' -> Some "\\rho"
        | 'σ' -> Some "\\sigma"
        | 'ς' -> Some "\\varsigma"
        | 'τ' -> Some "\\tau"
        | 'υ' -> Some "\\upsilon"
        | 'φ' -> Some "\\varphi"
        | 'ϕ' -> Some "\\phi"
        | 'χ' -> Some "\\chi"
        | 'ψ' -> Some "\\psi"
        | 'ω' -> Some "\\omega"
        | 'Γ' -> Some "\\Gamma"
        | 'Δ' -> Some "\\Delta"
        | 'Θ' -> Some "\\Theta"
        | 'Λ' -> Some "\\Lambda"
        | 'Ξ' -> Some "\\Xi"
        | 'Π' -> Some "\\Pi"
        | 'Σ' -> Some "\\Sigma"
        | 'Υ' -> Some "\\Upsilon"
        | 'Φ' -> Some "\\Phi"
        | 'Ψ' -> Some "\\Psi"
        | _ -> None

    /// A TeX macro for a single non-ASCII character, or `None` to pass it through. The set is the
    /// one stated on this module, less the super- and subscripts, which `escape` takes a run at a
    /// time.
    ///
    /// Where Unicode has two codepoints for one glyph both are mapped: `Marey.Units` uses U+03A9
    /// GREEK CAPITAL OMEGA for the ohm and U+00C5 for the angstrom, but a caller pasting from a
    /// units table may well supply U+2126 OHM SIGN or U+212B ANGSTROM SIGN, and silently setting
    /// one of those as a missing glyph would be a baffling bug to chase.
    let private tryMacro (c: char) : string option =
        match c with
        | '·' -> Some "\\textperiodcentered{}" // · MIDDLE DOT — the unit separator
        | '°' -> Some "\\textdegree{}" // ° DEGREE SIGN
        | '\u00B5' // MICRO SIGN — what Prefix.micro uses
        | '\u03BC' -> Some "\\textmu{}" // GREEK SMALL LETTER MU
        | '\u03A9' // GREEK CAPITAL LETTER OMEGA — what U.ohm uses
        | '\u2126' -> Some "\\textohm{}" // OHM SIGN
        | '\u00C5' // LATIN CAPITAL LETTER A WITH RING ABOVE — what U.angstrom uses
        | '\u212B' -> Some "\\AA{}" // ANGSTROM SIGN
        | '−' -> Some "\\ensuremath{-}" // − MINUS SIGN, not a hyphen
        | '×' -> Some "\\ensuremath{\\times}"
        | '±' -> Some "\\ensuremath{\\pm}"
        | '⋅' -> Some "\\ensuremath{\\cdot}" // ⋅ DOT OPERATOR
        | '≤' -> Some "\\ensuremath{\\leq}"
        | '≥' -> Some "\\ensuremath{\\geq}"
        | '≈' -> Some "\\ensuremath{\\approx}"
        | '≠' -> Some "\\ensuremath{\\neq}"
        | '∂' -> Some "\\ensuremath{\\partial}"
        | '∇' -> Some "\\ensuremath{\\nabla}"
        | '∞' -> Some "\\ensuremath{\\infty}"
        | '\u00A0' -> Some "~" // NO-BREAK SPACE is a tie in TeX
        | '\u2009' -> Some "\\," // THIN SPACE
        | '–' -> Some "--" // – EN DASH
        | '—' -> Some "---" // — EM DASH
        | c -> greekMacro c |> Option.map (fun m -> "\\ensuremath{" + m + "}")

    /// Escape a string so TeX typesets it literally: the reserved characters through
    /// `tryEscapeChar`, and the non-ASCII characters stated on this module through macros.
    ///
    /// Escaping is done character by character rather than by successive string replacement,
    /// because replacing `\` with `\textbackslash{}` first and `{` with `\{` afterwards would
    /// re-escape the braces the first replacement just introduced. Super- and subscript runs are
    /// consumed greedily, so that a whole exponent becomes one `\textsuperscript`.
    let escape (s: string) : string =
        if isNull s then
            ""
        else
            let sb = StringBuilder(s.Length + 16)
            let mutable i = 0

            // Writes the run starting at `start` and returns the index after it.
            let script (macro: string) (ofChar: char -> char option) (start: int) =
                sb.Append(macro).Append('{') |> ignore
                let mutable j = start

                while j < s.Length && (ofChar s.[j]).IsSome do
                    sb.Append((ofChar s.[j]).Value) |> ignore
                    j <- j + 1

                sb.Append '}' |> ignore
                j

            while i < s.Length do
                let c = s.[i]

                if (superscriptOf c).IsSome then
                    i <- script "\\textsuperscript" superscriptOf i
                elif (subscriptOf c).IsSome then
                    i <- script "\\textsubscript" subscriptOf i
                else
                    match tryEscapeChar c with
                    | Some replacement -> sb.Append replacement |> ignore
                    | None ->
                        match tryMacro c with
                        | Some macro -> sb.Append macro |> ignore
                        | None -> sb.Append c |> ignore

                    i <- i + 1

            sb.ToString()

    /// True when `escape` would leave the string unchanged — i.e. it contains no TeX special
    /// character and none of the non-ASCII characters `escape` rewrites.
    let internal isLiteralSafe (s: string) : bool =
        isNull s
        || s
           |> Seq.forall (fun c ->
               (tryEscapeChar c).IsNone
               && (tryMacro c).IsNone
               && (superscriptOf c).IsNone
               && (subscriptOf c).IsNone)
