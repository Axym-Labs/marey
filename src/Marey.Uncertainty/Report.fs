namespace Marey

open System

/// How a value and its uncertainty are written side by side (GUM 7.2.2).
[<RequireQualifiedAccess>]
type ReportForm =
    /// `9.811(44) m·s⁻²`: the uncertainty in parentheses, in units of the last digits written.
    | Concise
    /// `9.811(0.044) m·s⁻²`: the uncertainty in parentheses, written out.
    | Explicit
    /// `(9.811 ± 0.044) m·s⁻²`.
    | PlusMinus

/// How a report is written: the form, the typography of the unit, and how many significant
/// digits the uncertainty keeps. The GUM asks for at most two (7.2.6), and the estimate is
/// rounded to the place the uncertainty ends at.
type ReportFormat =
    { Form: ReportForm
      Style: UnitStyle
      Digits: int }

/// A value and its uncertainty rounded together, as text.
module internal Rounding =

    let pow10 (n: int) : float = Math.Pow(10.0, float n)

    /// ⌊log₁₀ |x|⌋ for a finite, non-zero `x`, corrected where the logarithm lands a hair off.
    let exponentOf (x: float) : int =
        let a = abs x
        let e = int (floor (Math.Log10 a))

        if pow10 (e + 1) <= a then e + 1
        elif pow10 e > a then e - 1
        else e

    /// `x` in units of `10^place`.
    let scaleTo (place: int) (x: float) : float = if place <= 0 then x * pow10 -place else x / pow10 place

    let private unsign (s: string) : string =
        if s.StartsWith "-" && s |> Seq.forall (fun c -> c = '-' || c = '0' || c = '.') then s.Substring 1 else s

    /// The texts of one rounding. `Uncertainty` is empty when there is none to write.
    type Parts =
        { /// The estimate, rounded to the uncertainty's last place.
          Value: string
          /// The uncertainty as a number: `0.044`.
          Uncertainty: string
          /// The uncertainty in units of the last digits written: `44`.
          LastDigits: string
          /// The same where the uncertainty reaches past the decimal point: `1.2` for `12.3(1.2)`.
          Parenthesised: string
          /// The power of ten the three are multiplied by, zero for none.
          Exponent: int }

    /// A number with no uncertainty, at its shortest, with any power of ten taken out.
    let private exact (value: float) : Parts =
        let s = Num.shortest value
        let at = s.IndexOf 'E'

        let mantissa, exponent =
            if at < 0 then s, 0 else s.Substring(0, at), int (s.Substring(at + 1).TrimStart '+')

        { Value = mantissa
          Uncertainty = ""
          LastDigits = ""
          Parenthesised = ""
          Exponent = exponent }

    /// `value` and `u` rounded so that `u` keeps `digits` significant digits. Numbers from 10⁻⁴ up
    /// to 10⁶ are written out, and others with a power of ten.
    let round (digits: int) (value: float) (u: float) : Parts =
        if not (u > 0.0) || Double.IsInfinity u || Double.IsNaN value || Double.IsInfinity value then
            exact value
        else
            let digits = max 1 digits
            let magnitude = exponentOf (max (abs value) u)
            let exponent = if magnitude >= 6 || magnitude < -4 then magnitude else 0
            let v = scaleTo exponent value
            let s = scaleTo exponent u
            let first = exponentOf s - (digits - 1)
            // 0.0996 at two digits rounds to 100 of the last place: it is 0.10, one place up.
            let place = if Num.roundAway (scaleTo first s) >= pow10 digits then first + 1 else first
            let n = Num.roundAway (scaleTo place s)

            if place < 0 then
                let decimals = -place
                let lastDigits = Num.fixedPoint 0 n
                let padded = lastDigits.PadLeft(decimals + 1, '0')
                let written = padded.Substring(0, padded.Length - decimals) + "." + padded.Substring(padded.Length - decimals)

                { Value = unsign (Num.fixedPoint decimals v)
                  Uncertainty = written
                  LastDigits = lastDigits
                  Parenthesised = (if lastDigits.Length > decimals then written else lastDigits)
                  Exponent = exponent }
            else
                let whole = Num.fixedPoint 0 (n * pow10 place)

                { Value = unsign (Num.fixedPoint 0 (Num.roundAway (scaleTo place v) * pow10 place))
                  Uncertainty = whole
                  LastDigits = whole
                  Parenthesised = whole
                  Exponent = exponent }

    /// One number to `digits` significant digits, with its power of ten: a contribution in a budget.
    let significant (digits: int) (x: float) : string * int =
        if x = 0.0 || Double.IsNaN x || Double.IsInfinity x then
            Num.shortest x, 0
        else
            let parts = round digits (abs x) (abs x)
            (if x < 0.0 then "-" else "") + parts.Uncertainty, parts.Exponent

    /// The unit as a style writes it, empty for a unit nobody stated and for a plain number. For
    /// siunitx it is the macros alone, to go inside `\qty`.
    let unitText (style: UnitStyle) (unit: IUnit) : string =
        let witness = U.erase unit

        if not witness.Stated then
            ""
        else
            let text = UnitDisplay.unitText style UnitRegistry.si Prefix.none witness
            let wrapper = "\\qty{}{"

            if style = Siunitx && text.StartsWith wrapper then
                text.Substring(wrapper.Length, text.Length - wrapper.Length - 1)
            else
                text

    /// A number already written, its power of ten and its unit, joined as the style joins them.
    /// `grouped` says the number is a sum that needs its parentheses before either.
    let private attach (style: UnitStyle) (grouped: bool) (number: string) (exponent: int) (unit: string) : string =
        let number = if grouped && (exponent <> 0 || unit <> "") then "(" + number + ")" else number

        match style with
        | Unicode ->
            let scaled = if exponent = 0 then number else number + " × 10" + Num.superscript exponent
            if unit = "" then scaled else scaled + " " + unit
        | Ascii ->
            let scaled = if exponent = 0 then number else number + "e" + string exponent
            if unit = "" then scaled else scaled + " " + unit
        | Latex ->
            let scaled = if exponent = 0 then number else number + " \\times 10^{" + string exponent + "}"
            if unit = "" then scaled else scaled + "\\," + unit
        | Siunitx ->
            let scaled = if exponent = 0 then number else number + "e" + string exponent
            if unit = "" then "\\num{" + scaled + "}" else "\\qty{" + scaled + "}{" + unit + "}"

    /// One number with its unit: `0.020 m·s⁻²`.
    let quantity (style: UnitStyle) (digits: int) (x: float) (unit: IUnit) : string =
        let number, exponent = significant digits x
        attach style false number exponent (unitText style unit)

    /// A value with its uncertainty and its unit, in one of the forms of GUM 7.2.2.
    let write (format: ReportFormat) (value: float) (u: float) (unit: IUnit) : string =
        let parts = round format.Digits value u
        let unit = unitText format.Style unit

        if parts.Uncertainty = "" then
            attach format.Style false parts.Value parts.Exponent unit
        else
            match format.Style, format.Form with
            // siunitx reads the uncertainty in units of the last digits, and the document chooses
            // how it is printed. Asking for ± is an option of the one call.
            | Siunitx, form ->
                let number = parts.Value + "(" + parts.LastDigits + ")"
                let scaled = if parts.Exponent = 0 then number else number + "e" + string parts.Exponent
                let options = if form = ReportForm.PlusMinus then "[separate-uncertainty=true]" else ""

                if unit = "" then
                    "\\num" + options + "{" + scaled + "}"
                else
                    "\\qty" + options + "{" + scaled + "}{" + unit + "}"
            | style, ReportForm.Concise -> attach style false (parts.Value + "(" + parts.Parenthesised + ")") parts.Exponent unit
            | style, ReportForm.Explicit -> attach style false (parts.Value + "(" + parts.Uncertainty + ")") parts.Exponent unit
            | style, ReportForm.PlusMinus ->
                let sign =
                    match style with
                    | Ascii -> " +- "
                    | Latex -> " \\pm "
                    | _ -> " ± "

                attach style true (parts.Value + sign + parts.Uncertainty) parts.Exponent unit

    /// What `report` writes: the concise form, two digits, the unit in Unicode.
    let standard: ReportFormat =
        { Form = ReportForm.Concise
          Style = Unicode
          Digits = 2 }
