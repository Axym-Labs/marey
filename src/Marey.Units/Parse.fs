namespace Marey

open System

/// D110 — **a unit read from a string**, in the syntax of UDUNITS-2, Unidata's C library:
/// `"kg m s-2"`, `"V/m"`, `"kPa"`, `"degC"`.
///
/// It is the only route to a unit first seen at run time — a netCDF attribute, a CSV header, a
/// configuration file — and the spelling a C# caller will use for anything composed. Written in
/// F# rather than bound, because a native dependency would end `Marey.Units`' and the core's
/// zero-dependency rule and the byte-identical SVG with them (D42).
///
/// ### What it reads
///
/// - **A product** by juxtaposition, `.`, `*` or `·`: `"kg m"`, `"N.m"`.
/// - **A quotient** by `/`. Product and quotient bind equally and associate left, as UDUNITS-2
///   has it, so `"W/m2 K"` is `(W/m²)·K`; write `"W/m2/K"` or `"W/(m2 K)"` for the other reading.
/// - **A power** by a signed integer after the symbol, with or without `^` or `**`: `"m2"`,
///   `"s-2"`, `"s^-2"`, `"m**3"`; and the superscripts `²`, `³` and `⁻¹`.
/// - **Parentheses**, and `1` as the numerator of a reciprocal: `"1/s"`.
/// - **A prefix** on a unit that takes one, longest first: `"kPa"`, `"µs"` or `"us"`, `"dam"`.
///   A symbol that is a unit is read as that unit before it is read as a prefix and a unit, so
///   `"cd"` is the candela, `"min"` the minute and `"Pa"` the pascal.
/// - **The symbols** every witness in `U` is declared with, and the ASCII
///   spellings UDUNITS-2 uses for the ones that are not ASCII: `degC`, `degF`, `ohm`, `Angstrom`.
///
/// ### What it refuses, by name
///
/// A timestamp (`"days since 1970-01-01"`) and a logarithmic unit (`dB`, `lg(re 1 mW)`): D110
/// left both out, and each is refused with a message saying so rather than as an unknown symbol.
/// A number other than an exponent or a reciprocal's `1` is refused too — `"1000 m"` is a scale
/// factor, which is `U.scaled`'s job and is not syntax this reads.
///
/// ### What it returns
///
/// The witness the same composition in F# would produce, at measure 1: `"V/m"` is `U.div volt
/// metre`, with the factorisation `[V,1; m,-1]` (D41), not a `Dim` canonicalised into base units.
/// A single symbol returns the declared witness itself. The measure is lost, as it must be for a
/// unit first seen at run time; `U.tryAs` asserts one back, checked.
[<AutoOpen>]
module UnitParsing =

    // The witnesses a symbol can name. Declared ones only: a composite such as `m/s` is
    // something the syntax composes, not something it looks up.
    let private declared: (string * U<1>) list =
        let of' (u: U<'u>) = u.Symbol, U.erase u

        [ of' U.metre; of' U.kilogram; of' U.second; of' U.ampere; of' U.kelvin; of' U.mole
          of' U.candela; of' U.gram
          of' U.hertz; of' U.newton; of' U.pascal; of' U.joule; of' U.watt
          of' U.coulomb; of' U.volt; of' U.farad; of' U.ohm; of' U.siemens
          of' U.weber; of' U.tesla; of' U.henry; of' U.lumen; of' U.lux
          of' U.becquerel; of' U.gray; of' U.sievert; of' U.katal
          of' U.radian; of' U.steradian
          of' U.electronvolt; of' U.calorie; of' U.angstrom; of' U.astronomicalUnit
          of' U.lightYear; of' U.parsec; of' U.barn; of' U.litre; of' U.bar
          of' U.atmosphere; of' U.minute; of' U.hour; of' U.day; of' U.year
          of' U.dalton; of' U.gauss; of' U.degreeCelsius; of' U.degreeFahrenheit ]

    let private aliases =
        [ "degC", "°C"; "deg_C", "°C"; "celsius", "°C"; "degF", "°F"; "deg_F", "°F"
          "ohm", "Ω"; "Ohm", "Ω"; "Angstrom", "Å"; "l", "L"; "sec", "s" ]

    let private symbols =
        let table = dict declared
        let all = declared @ (aliases |> List.map (fun (a, s) -> a, table.[s]))
        dict all

    // A prefix goes on a unit that is coherent with its own decade: not on the kilogram (the
    // gram takes it), not on a minute or a year, and not on an affine unit, where scaling the
    // offset along with the step means nothing.
    let private prefixable =
        set [ "m"; "s"; "A"; "K"; "mol"; "cd"; "g"; "Hz"; "N"; "Pa"; "J"; "W"; "C"; "V"; "F"; "Ω"; "S"
              "Wb"; "T"; "H"; "lm"; "lx"; "Bq"; "Gy"; "Sv"; "kat"; "rad"; "sr"; "eV"; "L"; "bar"; "Da"
              "b"; "ohm"; "Ohm"; "l" ]

    // Longest first, so `da` is tried before `d`; the ASCII `u` for micro beside `µ`.
    let private prefixes =
        Prefix.all
        |> Array.filter (fun p -> p.Exponent <> 0)
        |> Array.collect (fun p -> if p.Ascii <> p.Symbol then [| p.Symbol, p; p.Ascii, p |] else [| p.Symbol, p |])
        |> Array.sortByDescending (fun (s, _) -> s.Length)

    let private fail (text: string) (why: string) : 'a =
        raise (FormatException(sprintf "Marey: cannot read \"%s\" as a unit. %s" text why))

    let private resolve (text: string) (name: string) : U<1> =
        match symbols.TryGetValue name with
        | true, u -> u
        | _ ->
            let prefixed =
                prefixes
                |> Array.tryPick (fun (ps, p) ->
                    if name.Length > ps.Length && name.StartsWith(ps, StringComparison.Ordinal) then
                        let rest = name.Substring ps.Length

                        match symbols.TryGetValue rest with
                        | true, u when prefixable.Contains rest -> Some(U.scaled (p.Symbol + u.Symbol) p.Factor u)
                        | _ -> None
                    else
                        None)

            match prefixed with
            | Some u -> u
            | None -> fail text (sprintf "'%s' is not a unit symbol Marey knows, or a prefix on one." name)

    let rec private power (u: U<1>) (n: int) : U<1> =
        if n = 0 then U.one
        elif n < 0 then U.recip (power u -n)
        elif n = 1 then u
        else U.mul (power u (n - 1)) u

    type private Token =
        | Name of string
        | Int of int
        | Times
        | Over
        | Caret
        | Open
        | Close

    let private superscripts = dict [ '²', "2"; '³', "3"; '¹', "1"; '⁻', "-" ]

    let private tokens (text: string) : Token list =
        // The superscripts become the ASCII exponent they spell, so `m²` and `s⁻¹` read as `m2`
        // and `s-1`.
        let s =
            text
            |> Seq.map (fun c ->
                match superscripts.TryGetValue c with
                | true, r -> r
                | _ -> string c)
            |> String.concat ""

        let isNameChar (c: char) = Char.IsLetter c || c = '°' || c = '_' || c = 'Å' || c = 'Ω' || c = 'µ'
        let out = ResizeArray<Token>()
        let mutable i = 0

        while i < s.Length do
            let c = s.[i]

            if Char.IsWhiteSpace c then
                // Juxtaposition is a product, but only between two things that can be multiplied:
                // a space beside an operator is just a space.
                i <- i + 1
            elif c = '.' || c = '*' && not (i + 1 < s.Length && s.[i + 1] = '*') || c = '·' then
                out.Add Times
                i <- i + 1
            elif c = '*' then
                out.Add Caret
                i <- i + 2
            elif c = '^' then
                out.Add Caret
                i <- i + 1
            elif c = '/' then
                out.Add Over
                i <- i + 1
            elif c = '(' then
                out.Add Open
                i <- i + 1
            elif c = ')' then
                out.Add Close
                i <- i + 1
            elif Char.IsDigit c || ((c = '-' || c = '+') && i + 1 < s.Length && Char.IsDigit s.[i + 1]) then
                let start = i
                i <- i + 1

                while i < s.Length && Char.IsDigit s.[i] do
                    i <- i + 1

                if i < s.Length && (s.[i] = '.' || s.[i] = 'e' || s.[i] = 'E') && i + 1 < s.Length && Char.IsDigit s.[i + 1] then
                    fail text "A number other than an integer exponent is a scale factor, which this does not read; use U.scaled."

                out.Add(Int(int (s.Substring(start, i - start))))
            elif isNameChar c then
                let start = i

                while i < s.Length && isNameChar s.[i] do
                    i <- i + 1

                out.Add(Name(s.Substring(start, i - start)))
            else
                fail text (sprintf "'%c' is not part of the unit syntax." c)

        List.ofSeq out

    // product := factor (('/' | '.' | juxtaposition) factor)*, left to right, one precedence.
    // factor  := atom exponent?
    // atom    := name | '(' product ')' | 1
    let private parseTokens (text: string) (ts: Token list) : U<1> =
        let rec product ts =
            let first, rest = factor ts
            loop first rest

        and loop acc ts =
            match ts with
            | Over :: rest ->
                let f, rest = factor rest
                loop (U.div acc f) rest
            | Times :: rest ->
                let f, rest = factor rest
                loop (U.mul acc f) rest
            | (Name _ | Open | Int _) :: _ ->
                let f, rest = factor ts
                loop (U.mul acc f) rest
            | _ -> acc, ts

        and factor ts =
            let a, rest = atom ts

            match rest with
            | Caret :: Int n :: rest -> power a n, rest
            | Caret :: _ -> fail text "'^' or '**' needs an integer exponent after it."
            | Int n :: rest when (match ts with | Int _ :: _ -> false | _ -> true) -> power a n, rest
            | _ -> a, rest

        and atom ts =
            match ts with
            | Name n :: rest -> resolve text n, rest
            | Int 1 :: rest -> U.one, rest
            | Int _ :: _ -> fail text "A number other than an integer exponent is a scale factor, which this does not read; use U.scaled."
            | Open :: rest ->
                match product rest with
                | u, Close :: rest -> u, rest
                | _ -> fail text "A '(' is not closed."
            | Close :: _ -> fail text "A ')' closes nothing."
            | _ -> fail text "A unit is missing where one was expected."

        match product ts with
        | u, [] -> u
        | _, _ -> fail text "Something follows the unit that is not part of it."

    [<RequireQualifiedAccess>]
    module U =

        /// D110 — read a unit, or say why it cannot be read. See `UnitParsing` for the syntax.
        let tryParse (text: string) : Result<U<1>, string> =
            if isNull text || String.IsNullOrWhiteSpace text then
                Error "Marey: an empty string is not a unit. Leave the unit out, or use U.unspecified, for numbers nobody named."
            elif text.Contains " since " || text.StartsWith "since " then
                Error(sprintf "Marey: \"%s\" is a timestamp, a point in time rather than a unit; convert the times to a duration first." text)
            elif text = "dB" || text = "B" || text.Contains "lg(" || text.Contains "ln(" || text.Contains "lb(" || text.StartsWith "dB" then
                Error(sprintf "Marey: \"%s\" is a logarithmic unit. Marey has no logarithmic units; convert to the linear quantity (a power ratio, a pressure) and use a log axis." text)
            else
                try
                    Ok(parseTokens text (tokens (text.Trim())))
                with :? FormatException as e ->
                    Error e.Message

        /// D110 — read a unit: `U.parse "V/m"`. Raises `FormatException` with the reason when the
        /// text is not one; `tryParse` returns it instead.
        let parse (text: string) : U<1> =
            match tryParse text with
            | Ok u -> u
            | Error why -> raise (FormatException why)

        /// D110 — **assert a measure onto a unit read at run time, checked.** `Some` when `u`
        /// has `target`'s dimension and carries an angle exactly as often (D98), as a `U<'u>`
        /// with everything `u` carries at run time — so `tryAs U.metre (U.parse "km")` is a
        /// `U<m>` whose `ToSI` is 1000, as `U.scaled "km" 1000.0 U.metre` is. `None` otherwise,
        /// and for an unstated unit, which makes no claim to check. A checked assertion, not a
        /// cast: the only way from a string to a measure.
        let tryAs (target: U<'u>) (u: IUnit) : U<'u> option =
            if isNull u || not u.Stated || not target.Stated then
                None
            elif
                u.Dim = target.Dim
                && U.angleExponentOf u.Symbol u.Factors = U.angleExponentOf target.Symbol target.Factors
            then
                Some(U<'u>(u.Symbol, u.Dim, u.ToSI, u.OffsetSI, u.Factors, true))
            else
                None
