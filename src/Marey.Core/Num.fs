namespace Marey

open System
open System.Globalization
open System.Numerics

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
//     than reading a wrong number. `at` writes the digits of an integer and places the point.
//   * values are rounded to a fixed number of decimals before formatting, so float noise in
//     the last few bits of a matrix multiply cannot churn a golden diff.
//   * negative zero is normalised to zero. .NET Core 3.0+ formats -0.0 as "-0", and whether a
//     coordinate lands on -0.0 or 0.0 depends on the sign of a multiply that is otherwise
//     invisible.
//   * non-finite values become "0". A singular transform must not be able to write "NaN" into
//     a document that then fails to parse, or "inf" into a TeX dimension.
//
//   * D129 — no rounding and no formatting is left to the platform. Under Fable the "same
//     compiler" is JavaScript's arithmetic, and the two runtimes disagree on exact halves:
//     Fable reads `Math.Round(x, MidpointRounding.AwayFromZero)` as rounding to one decimal
//     place, ignores the mode in `Math.Round(x, d, mode)`, calls anything within 10⁻⁸ of a half
//     a tie in `Math.Round(x)`, and JavaScript's `toFixed` rounds a
//     half the other way from .NET's "F". So rounding is done here, in operations IEEE 754
//     makes exact on both, and each function below reproduces what .NET produced before it,
//     which is what keeps every golden byte-identical.
//
// Zero dependencies, so this costs `Marey.Core` nothing (§13).
// ======================================================================================

/// Culture- and platform-invariant number formatting. Every number a text backend emits goes
/// through here.
[<RequireQualifiedAccess>]
module Num =

#if !FABLE_COMPILER
    let private invariant = CultureInfo.InvariantCulture
#endif

    /// The nearest integer, a half rounded away from zero. `Math.Round(x,
    /// MidpointRounding.AwayFromZero)` without the platform (D129): the fraction is exact, so the
    /// comparison with one half is too.
    let roundAway (x: float) : float =
        let t = Math.Truncate x

        if abs (x - t) >= 0.5 then t + float (sign x) else t

    /// The nearest integer, a half rounded to the even one. `Math.Round(x)` without the platform
    /// (D129): Fable's version calls anything within 10⁻⁸ of a half a tie, so it rounds 2.500000001
    /// down and 3.4999999999 up. The fraction is exact, so a tie here is an exact half.
    let roundEven (x: float) : float =
        let t = Math.Truncate x
        let f = abs (x - t)

        if f > 0.5 then t + float (sign x)
        elif f < 0.5 then t
        elif t % 2.0 = 0.0 then t
        else t + float (sign x)

    /// The most decimal places `at` will honour. Requests above this are clamped rather than
    /// rejected: a caller asking for more precision than a document needs wants the finest
    /// available, not an exception.
    [<Literal>]
    let MaxDecimals = 6

    let private powers = [| 1.0; 10.0; 100.0; 1000.0; 1e4; 1e5; 1e6 |]

    /// `x` at `decimals` places, the exact binary value rounded half to even, with the trailing
    /// zeros kept: what .NET's "F" format writes, for any `x`. A negative value keeps its sign
    /// when it rounds to zero (`-0.00`), and so does negative zero, as in "F".
    ///
    /// Exact because the double is `m · 2^e` and the rounding is done on `m · 10^decimals`
    /// against `2^-e` in integers. Measured against "F" on four million doubles, every
    /// magnitude and every exact half: identical.
    let fixedPoint (decimals: int) (x: float) : string =
        if Double.IsNaN x then
            "NaN"
        elif Double.IsPositiveInfinity x then
            "Infinity"
        elif Double.IsNegativeInfinity x then
            "-Infinity"
        else
            let n = max 0 decimals
            let bits = BitConverter.DoubleToInt64Bits x
            let biased = int ((bits >>> 52) &&& 0x7FFL)
            let fraction = bits &&& 0xFFFFFFFFFFFFFL

            let m, e =
                if biased = 0 then fraction, -1074 else fraction ||| (1L <<< 52), biased - 1075

            let scaled = BigInteger m * BigInteger.Pow(BigInteger 10, n)

            let q =
                if e >= 0 then
                    scaled * BigInteger.Pow(BigInteger 2, e)
                else
                    let den = BigInteger.Pow(BigInteger 2, -e)
                    let q = scaled / den
                    let twice = (scaled - q * den) * BigInteger 2

                    if twice > den || (twice = den && not (q % BigInteger 2).IsZero) then
                        q + BigInteger.One
                    else
                        q

            let digits = q.ToString().PadLeft(n + 1, '0')

            let body =
                if n = 0 then
                    digits
                else
                    digits.Substring(0, digits.Length - n) + "." + digits.Substring(digits.Length - n)

            if bits < 0L then "-" + body else body

    /// Format a number at a given number of decimal places, invariantly, never in scientific
    /// notation, with negative zero and non-finite values normalised. A half rounds away from
    /// zero.
    ///
    /// The value is scaled by `10^d`, rounded with `roundAway`, and the integer written with the
    /// point placed `d` digits from the right and trailing zeros dropped. That is exactly what
    /// `Math.Round(v, d, AwayFromZero)` and the custom format "0.###" did together, measured on
    /// three million doubles, halves included. Past 10¹⁵ the integer is no longer exact in a
    /// double, so the value itself is written exactly at `d` places (`fixedPoint`) rather than to
    /// the fifteen significant digits the custom format used. No coordinate is that large.
    let at (decimals: int) (v: float) : string =
        if Double.IsNaN v || Double.IsInfinity v then
            "0"
        else
            let d = max 0 (min MaxDecimals decimals)
            let k = roundAway (v * powers.[d])

            // -0.0 = 0.0 is true for floats, so this stops "-0" appearing in the output.
            if k = 0.0 then
                "0"
            elif abs k < 1e15 then
                let digits = (string (int64 (abs k))).PadLeft(d + 1, '0')
                let whole = digits.Substring(0, digits.Length - d)
                let places = digits.Substring(digits.Length - d).TrimEnd '0'
                let body = if places = "" then whole else whole + "." + places
                if k < 0.0 then "-" + body else body
            else
                let s = fixedPoint d v
                if d = 0 then s else s.TrimEnd('0').TrimEnd('.')

#if FABLE_COMPILER
    /// JavaScript's shortest round-trip string, as (negative, digits, point): the value is
    /// `0.digits × 10^point`. Its notation is `1e-7` or `1.5e+21` where .NET's is `1E-07`.
    let private shortestParts (s: string) =
        let negative = s.StartsWith "-"
        let s = s.TrimStart '-'

        let mantissa, exponent =
            match s.IndexOfAny [| 'e'; 'E' |] with
            | -1 -> s, 0
            | i -> s.Substring(0, i), int (s.Substring(i + 1).TrimStart '+')

        let whole, fraction =
            match mantissa.IndexOf '.' with
            | -1 -> mantissa, ""
            | i -> mantissa.Substring(0, i), mantissa.Substring(i + 1)

        let all = whole + fraction
        let leading = all.Length - all.TrimStart('0').Length
        let digits = all.TrimStart('0').TrimEnd('0')
        negative, digits, whole.Length - leading + exponent
#endif

    /// The shortest string that reads back as `x`, in .NET's "R" notation: fixed point from
    /// 10⁻⁴ up to 10¹⁷, `d.dddE±XX` outside it.
    ///
    /// Both runtimes specify the same digits — the shortest that round-trip, the closest when
    /// two are as short — and differ only in how they are written, so under Fable JavaScript's
    /// string is rewritten in .NET's notation (D129). The notation rule was measured against
    /// "R" on four million doubles.
    let shortest (x: float) : string =
#if FABLE_COMPILER
        if Double.IsNaN x then
            "NaN"
        elif Double.IsPositiveInfinity x then
            "Infinity"
        elif Double.IsNegativeInfinity x then
            "-Infinity"
        elif x = 0.0 then
            (if BitConverter.DoubleToInt64Bits x < 0L then "-0" else "0")
        else
            let negative, digits, point = shortestParts (string x)
            let exponent = point - 1

            let body =
                if exponent < -4 || exponent >= 17 then
                    let m = if digits.Length = 1 then digits else digits.Substring(0, 1) + "." + digits.Substring 1
                    m + "E" + (if exponent < 0 then "-" else "+") + (string (abs exponent)).PadLeft(2, '0')
                elif point <= 0 then
                    "0." + String('0', -point) + digits
                elif point >= digits.Length then
                    digits + String('0', point - digits.Length)
                else
                    digits.Substring(0, point) + "." + digits.Substring point

            if negative then "-" + body else body
#else
        x.ToString("R", invariant)
#endif

    let private superDigits =
        [| '⁰'; '¹'; '²'; '³'; '⁴'; '⁵'; '⁶'; '⁷'; '⁸'; '⁹' |]

    /// Unicode superscript for an integer exponent: `-2` becomes `⁻²`.
    ///
    /// U+207B SUPERSCRIPT MINUS, not a hyphen — the hyphen sits on the baseline next to
    /// superscript digits and looks like a typesetting bug in every font. Here rather than in
    /// `Marey.Units` because a log axis's `10⁻⁸` is written in Core (`Ticks.formatPower`) and a
    /// unit's `s⁻²` in `UnitDisplay`, and the two must not drift apart (F2).
    let superscript (n: int) : string =
        let digits = string (abs (int64 n))
        let sb = System.Text.StringBuilder()

        if n < 0 then
            sb.Append('⁻') |> ignore

        for c in digits do
            sb.Append(superDigits.[int c - int '0']) |> ignore

        sb.ToString()
