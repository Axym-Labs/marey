namespace Marey.Witnesses

open System
open System.Collections.Concurrent
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols

/// One witness Marey declares: `U.second`, its measure's printed text and that measure in base
/// measures.
type WitnessEntry =
    { Name: string
      Text: string
      Base: Measure.Factor list }

/// **Marey's witnesses, by measure**, as the assemblies a checked file references declare them:
/// every value of type `U<…>` in a module named `U`, in declaration order.
///
/// Read from symbols rather than kept as a table, so a witness Marey adds is found without a
/// change here, and the index is the one the file was checked against. Where two witnesses share
/// a measure (`U.kilogram` and `U.gram` are both `U<kg>`), the first declared is the measure's,
/// which is the coherent SI one: the SI witnesses are declared before the others.
[<Sealed>]
type WitnessIndex internal (entries: WitnessEntry list, unspecified: string option, names: Measure.Names) =

    member _.Entries = entries

    member internal _.Names = names

    /// The witness of a value with no unit, `U.unspecified`, where the referenced Marey has it.
    member _.Unspecified = unspecified

    /// The first witness declared for a measure's printed text.
    member _.Exact(text: string) : WitnessEntry option = entries |> List.tryFind (fun e -> e.Text = text)

    /// The first witness declared for a measure in base measures.
    member _.Equivalent(factors: Measure.Factor list) : WitnessEntry option =
        entries |> List.tryFind (fun e -> e.Base = factors)

/// Reading the index from a checked file.
module WitnessIndex =

    let private isWitness (v: FSharpMemberOrFunctionOrValue) =
        v.CurriedParameterGroups.Count = 0
        && not v.IsFunction
        && v.GenericParameters.Count = 0
        && not (v.Attributes |> Seq.exists (fun a -> a.AttributeType.DisplayName = "ObsoleteAttribute"))

    let rec private modules (e: FSharpEntity) : FSharpEntity seq =
        seq {
            if e.IsFSharpModule && e.DisplayName = "U" then
                yield e

            for n in e.NestedEntities do
                yield! modules n
        }

    let private read (assembly: FSharpAssembly) : (string * string) list =
        assembly.Contents.Entities
        |> Seq.collect modules
        |> Seq.collect (fun m -> m.MembersFunctionsAndValues)
        |> Seq.filter isWitness
        |> Seq.choose (fun v ->
            match Measure.carried v.FullType with
            | Some(Measure.Witness text) -> Some("U." + v.DisplayName, text)
            | _ -> None)
        |> List.ofSeq

    // An assembly's witnesses, by its file and when it was written: a host checks a file on
    // every keystroke, and the referenced assemblies do not change between checks.
    let private cache = ConcurrentDictionary<string * DateTime, (string * string) list>()

    let private witnessesOf (assembly: FSharpAssembly) =
        match assembly.FileName with
        | Some file when IO.File.Exists file -> cache.GetOrAdd((file, IO.File.GetLastWriteTimeUtc file), fun _ -> read assembly)
        | _ -> read assembly

    /// The index of the Marey assemblies a checked file references, in their reference order;
    /// Marey.Core, which declares `U.unspecified`, among them.
    let ofCheck (check: FSharpCheckFileResults) : WitnessIndex =
        let names = Measure.Names check

        let witnesses =
            check.ProjectContext.GetReferencedAssemblies()
            |> List.filter (fun a -> a.SimpleName = "Marey" || a.SimpleName.StartsWith "Marey.")
            // The SI witnesses first: where two units share a measure, the coherent one is the
            // measure's witness.
            |> List.sortBy (fun a -> if a.SimpleName = "Marey.Units" then 0 else 1)
            |> List.collect witnessesOf

        let dimensionless = [ "U.one"; "U.unspecified" ]

        let entries =
            witnesses
            |> List.filter (fun (n, _) -> not (List.contains n dimensionless))
            |> List.choose (fun (name, text) ->
                Measure.parse text
                |> Option.bind names.Expand
                |> Option.map (fun b -> { Name = name; Text = text; Base = b }))

        let unspecified = witnesses |> List.tryFind (fun (n, _) -> n = "U.unspecified") |> Option.map fst
        WitnessIndex(entries, unspecified, names)

/// **The F# expression of a measure's witness.**
///
/// The witness Marey declares for the measure as written, if there is one (`U.metrePerSecond`
/// for `m/s`); else composed from the written factors (`U.div U.volt U.metre` for `V/m`, which
/// keeps the reading the code chose: a torque in `N m` is not drawn as joules); else the one
/// declared for an equivalent measure; else composed from base measures.
module Witness =

    /// An argument's text: an expression with a space in it, parenthesised.
    let atom (expression: string) =
        if expression.Contains " " then "(" + expression + ")" else expression

    let private power (w: string) (n: int) =
        match n with
        | 1 -> w
        | 2 -> "U.square " + atom w
        | 3 -> "U.cube " + atom w
        | n -> List.replicate (n - 1) (atom w) |> List.fold (fun acc x -> "U.mul " + atom acc + " " + x) w

    let private product (ws: string list) =
        match ws with
        | [] -> None
        | first :: rest -> Some(rest |> List.fold (fun acc w -> "U.mul " + atom acc + " " + atom w) first)

    let private compose (single: string -> string option) (factors: Measure.Factor list) : string option =
        let part sign =
            factors
            |> List.filter (fun (_, e) -> sign * e > 0)
            |> List.map (fun (n, e) -> single n |> Option.map (fun w -> power w (abs e)))

        let up, down = part 1, part -1

        if up @ down |> List.exists Option.isNone then
            None
        else
            match product (List.choose id up), product (List.choose id down) with
            | Some n, None -> Some n
            | None, Some d -> Some("U.recip " + atom d)
            | Some n, Some d -> Some("U.div " + atom n + " " + atom d)
            | None, None -> None

    /// The witness expression for a measure's printed text, unparenthesised.
    let ofMeasure (index: WitnessIndex) (text: string) : string option =
        let names = index.Names

        match index.Exact text with
        | Some e -> Some e.Name
        | None ->
            match Measure.parse text with
            | None -> None
            | Some written ->
                let single (name: string) =
                    match index.Exact name with
                    | Some e -> Some e.Name
                    | None -> names.Base name |> Option.bind index.Equivalent |> Option.map (fun e -> e.Name)

                let bases = names.Expand written

                match compose single written with
                | Some w -> Some w
                | None ->
                    match bases |> Option.bind index.Equivalent with
                    | Some e -> Some e.Name
                    | None ->
                        bases
                        |> Option.bind (compose (fun b -> index.Equivalent [ b, 1 ] |> Option.map (fun e -> e.Name)))

    /// The witness expression for what a value carries: its measure's, or `U.unspecified` for a
    /// plain float.
    let ofCarried (index: WitnessIndex) (c: Measure.Carried) : string option =
        match c with
        | Measure.Measured text -> ofMeasure index text
        | Measure.Plain -> index.Unspecified
        | Measure.Carrying _
        | Measure.Witness _ -> None
