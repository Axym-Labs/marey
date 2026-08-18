namespace Marey

open System
open System.Globalization

// ======================================================================================
// DETERMINISTIC NUMBER FORMATTING FOR TEXT BACKENDS.
//
// §13's test strategy is "golden-image snapshots against the SVG backend (text-diffable,
// reviewable in a PR)", and §6 calls the SVG backend the spec. Both require the same thing:
// **the same Scene must produce byte-identical output on every machine, every run.**
//
// These rules started life inside the SVG writer, where they are documented at length. They
// are here because a second text backend needs every one of them, and F2 states the rule for
// exactly this situation: duplication is dangerous in the one place a change to one copy would
// silently disagree with the other, so promote on the appearance of a second consumer.
//
// The rules, and why each is not negotiable:
//
//   * every number goes through InvariantCulture. A German locale emits "0,5" otherwise, and
//     "0,5" is not a number SVG can parse or TeX can read as a dimension. This is a
//     correctness bug, not a cosmetic one.
//   * no format that can ever produce scientific notation. Both "%g" and "R" switch to 1e-05
//     below 1e-4. The SVG number grammar has no exponent in path data, and TeX is *worse*:
//     it cannot interpret "1e-05" as a dimension at all, so it raises a hard error rather
//     than reading a wrong number. Every number here goes through a *custom* .NET numeric
//     format, which never uses an exponent.
//   * values are rounded to a fixed number of decimals before formatting, so float noise in
//     the last few bits of a matrix multiply cannot churn a golden diff.
//   * negative zero is normalised to zero. .NET Core 3.0+ formats -0.0 as "-0", and whether a
//     coordinate lands on -0.0 or 0.0 depends on the sign of a multiply that is otherwise
//     invisible.
//   * non-finite values become "0". A singular transform must not be able to write "NaN" into
//     a document that then fails to parse, or "inf" into a TeX dimension.
//
// Zero dependencies, so this costs `Marey.Core` nothing (§13).
// ======================================================================================

/// Culture- and platform-invariant number formatting. Every number a text backend emits goes
/// through here.
[<RequireQualifiedAccess>]
module Num =

    let private invariant = CultureInfo.InvariantCulture

    /// Custom numeric formats, indexed by decimal places. A custom format never falls back to
    /// scientific notation, which is the whole reason for not using "G" or sprintf "%g".
    let private patterns =
        [| "0"; "0.#"; "0.##"; "0.###"; "0.####"; "0.#####"; "0.######" |]

    /// The most decimal places `at` will honour. Requests above this are clamped rather than
    /// rejected: a caller asking for more precision than the format table carries wants the
    /// finest available, not an exception.
    [<Literal>]
    let MaxDecimals = 6

    /// Format a number at a given number of decimal places, invariantly, never in scientific
    /// notation, with negative zero and non-finite values normalised.
    let at (decimals: int) (v: float) : string =
        if Double.IsNaN v || Double.IsInfinity v then
            "0"
        else
            let d =
                if decimals < 0 then 0
                elif decimals >= patterns.Length then patterns.Length - 1
                else decimals

            let r = Math.Round(v, d, MidpointRounding.AwayFromZero)
            // -0.0 = 0.0 is true for floats, so this assignment replaces negative zero with
            // positive zero and stops "-0" appearing in the output.
            let r = if r = 0.0 then 0.0 else r
            r.ToString(patterns.[d], invariant)
