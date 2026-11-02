namespace Marey.Witnesses

open System
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols

/// **A measure as the compiler prints it**, `Marey.kg Marey.m/Marey.s ^ 2`, read back into its
/// factors.
///
/// The compiler service has no typed view of a power: `float<m^2>`'s measure argument is
/// not a named type to it, and asking for its parts throws. Its printed form, with full names,
/// is complete and is what a diagnostic says, so a measure is read from that text. A measure the
/// reader does not follow (a rational power, a measure variable) is `None`, and no fix is
/// offered for it.
module Measure =

    /// A factor and its exponent, as written: `Marey.s, -2`.
    type Factor = string * int

    /// Full names, so a measure's text names the entity it resolves to.
    let internal display = FSharpDisplayContext.Empty.WithShortTypeNames(false)

    /// The text between a generic type's outermost angle brackets: `Marey.s` of
    /// `Microsoft.FSharp.Core.float<Marey.s>`. None for a type with no argument.
    let internal argumentText (formatted: string) : string option =
        let o = formatted.IndexOf '<'
        let c = formatted.LastIndexOf '>'
        if o >= 0 && c > o then Some(formatted.Substring(o + 1, c - o - 1).Trim()) else None

    /// The factors of a measure's printed text, in the order printed: the numerator's, then the
    /// denominator's, negated. `1` is no factor.
    let parse (text: string) : Factor list option =
        if text.Contains "(" || text.Contains "'" || text.Contains "<" then
            None
        else
            let side (sign: int) (part: string) : Factor list option =
                let tokens = part.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

                let rec go (acc: Factor list) (tokens: string list) =
                    match tokens with
                    | [] -> Some(List.rev acc)
                    | "1" :: rest -> go acc rest
                    | name :: "^" :: power :: rest ->
                        match Int32.TryParse power with
                        | true, n -> go ((name, sign * n) :: acc) rest
                        | _ -> None
                    | name :: rest when name.Contains "^" ->
                        match name.Split '^' with
                        | [| n; p |] ->
                            match Int32.TryParse p with
                            | true, e -> go ((n, sign * e) :: acc) rest
                            | _ -> None
                        | _ -> None
                    | name :: rest -> go ((name, sign) :: acc) rest

                go [] tokens

            match text.Split '/' with
            | [| numerator |] -> side 1 numerator
            | [| numerator; denominator |] ->
                match side 1 numerator, side -1 denominator with
                | Some n, Some d -> Some(n @ d)
                | _ -> None
            | _ -> None

    /// Exponents summed per name, zeros dropped, sorted: a measure's identity once every name is
    /// a base measure.
    let internal normal (factors: Factor list) : Factor list =
        factors
        |> List.groupBy fst
        |> List.map (fun (n, fs) -> n, fs |> List.sumBy snd)
        |> List.filter (fun (_, e) -> e <> 0)
        |> List.sortBy fst

    /// Where measure names resolve: the checked file's own signature, then every assembly it
    /// references, FSharp.Core among them.
    type internal Names(check: FSharpCheckFileResults) =

        let assemblies =
            lazy
                (check.PartialAssemblySignature
                 :: (check.ProjectContext.GetReferencedAssemblies() |> List.map (fun a -> a.Contents)))

        let resolved = Collections.Generic.Dictionary<string, Factor list option>()

        let find (name: string) : FSharpEntity option =
            let path = name.Split '.' |> List.ofArray

            assemblies.Force()
            |> List.tryPick (fun sigs ->
                try
                    sigs.FindEntityByPath path
                with _ ->
                    None)

        /// A name's factors in base measures: an abbreviation expanded, as the compiler equates
        /// them, so `UnitSymbols.s`, `Marey.s` and `UnitNames.second` are one measure, and
        /// `Marey.N` is `kg m/s^2`. A name that resolves to nothing stands for itself.
        member this.Base(name: string) : Factor list option =
            match resolved.TryGetValue name with
            | true, r -> r
            | _ ->
                // A cycle cannot be written in F#, but a name is marked before it is expanded
                // so that a malformed reference could not recurse without end.
                resolved.[name] <- None

                let r =
                    match find name with
                    | Some e when e.IsFSharpAbbreviation ->
                        let text = e.AbbreviatedType.Format display
                        parse text |> Option.bind this.Expand
                    | Some e -> Some [ (try e.FullName with _ -> name), 1 ]
                    | None -> Some [ name, 1 ]

                resolved.[name] <- r
                r

        /// Written factors in base measures, normal.
        member this.Expand(factors: Factor list) : Factor list option =
            let parts = factors |> List.map (fun (n, e) -> this.Base n |> Option.map (List.map (fun (b, x) -> b, x * e)))

            if parts |> List.exists Option.isNone then None
            else Some(parts |> List.choose id |> List.concat |> normal)

    /// What a value's type says of its unit, read from the type of one value or of a collection.
    type Carried =
        /// `float<s>`, or a sequence of them: the measure's printed text.
        | Measured of string
        /// A plain `float`, or a sequence of them.
        | Plain
        /// A `Q<s>` or a `Quantity<s>`: the values carry their witness.
        | Carrying of string
        /// A witness, `U<s>`.
        | Witness of string

    let private isPlainFloat (formatted: string) =
        formatted = "Microsoft.FSharp.Core.float" || formatted = "System.Double"

    /// The measure of a type printed as `prefix<…>` and nothing else: an array prints postfix,
    /// `float<Marey.s> Microsoft.FSharp.Core.array`, and is not one value.
    let private applied (prefix: string) (formatted: string) =
        if formatted.StartsWith(prefix + "<") && formatted.EndsWith ">" then
            argumentText formatted |> Option.filter (fun m -> not (m.Contains "<" || m.Contains ">"))
        else
            None

    let private measuredFloat = applied "Microsoft.FSharp.Core.float"

    /// What a type carries of a unit, or None for a type that is no measured value.
    let carried (t: FSharpType) : Carried option =
        try
            let formatted = t.Format display

            match applied "Marey.U" formatted, applied "Marey.Q" formatted, applied "Marey.Quantity" formatted with
            | Some w, _, _ -> Some(Witness w)
            | _, Some q, _
            | _, _, Some q -> Some(Carrying q)
            | _ ->
                if isPlainFloat formatted then
                    Some Plain
                else
                    match measuredFloat formatted with
                    | Some m -> Some(Measured m)
                    | None ->
                        // A sequence of one element type: an array, a list, a seq, a ResizeArray.
                        if t.HasTypeDefinition && t.GenericArguments.Count = 1 then
                            let element = t.GenericArguments.[0].Format display

                            if isPlainFloat element then Some Plain
                            else measuredFloat element |> Option.map Measured
                        else
                            None
        with _ ->
            None
