namespace Marey.Witnesses

open System
open System.Text.RegularExpressions
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

/// One change to the text: `Text` in place of what `Range` covers. An empty range inserts.
type Edit = { Range: range; Text: string }

/// One way to make a call compile: its edits, applied together.
type Fix = { Title: string; Edits: Edit list }

/// A Marey call that does not type-check for want of the right witness, and the fixes for it.
type Finding =
    { Code: string
      Range: range
      Message: string
      Fixes: Fix list }

/// **The fixes for a checked file.**
///
/// Each curried call of a Marey function is read against its parameters: which take a witness
/// (`U<'u>`), which take values that carry one (`Q<'u>`), which take measured values
/// (`seq<float<'u>>`). An argument's unit is read from its type where it is a name, `t` or
/// `r.Time`, which the checker resolves even in a call that does not type-check; an argument of
/// another shape is not read. Three findings:
///
/// - **Witnesses missing**, `Chart.lineIn t x`: the witnesses of `t`'s and `x`'s units are
///   inserted, `Chart.lineIn U.second U.metre t x`.
/// - **A witness for another unit**, `Chart.lineIn U.metre U.metre t x` with `t` in seconds:
///   `U.second` in its place.
/// - **Measured values where carried ones are taken**, `Chart.line t x` with raw arrays: the
///   witness form, `Chart.lineIn U.second U.metre t x`, or the values made to carry their
///   witness, `Chart.line (U.second.Of t) (U.metre.Of x)`.
///
/// Each is a call the compiler already refuses, so a file that compiles has no finding.
module Findings =

    type private Param =
        | WitnessParam of string
        | CarryingParam of string
        | DataParam of string
        | OtherParam

    type private Arg =
        { Range: range
          Name: string option
          Carried: Measure.Carried option }

    let private variable = Regex(@"^'[A-Za-z_][A-Za-z0-9_]*$")

    let private paramOf (t: FSharpType) =
        match Measure.carried t with
        | Some(Measure.Witness v) when variable.IsMatch v -> WitnessParam v
        | Some(Measure.Carrying v) when variable.IsMatch v -> CarryingParam v
        | Some(Measure.Measured v) when variable.IsMatch v -> DataParam v
        | _ -> OtherParam

    /// A measure's text as a reader writes it: `m/s ^ 2` for `Marey.m/Marey.s ^ 2`.
    let private readable (text: string) =
        Regex.Replace(text, @"(?:[A-Za-z_][A-Za-z0-9_]*\.)+(?=[^\s./^]+)", "")

    /// Where a value's unit is said: `in m/s`, or `with no unit`.
    let private describe (c: Measure.Carried) =
        match c with
        | Measure.Measured m
        | Measure.Carrying m
        | Measure.Witness m -> "in " + readable m
        | Measure.Plain -> "with no unit"

    /// Whether an argument of what it carries can be passed where the parameter is.
    let private fits (p: Param) (c: Measure.Carried option) =
        match p, c with
        | _, None -> true
        | WitnessParam _, Some(Measure.Witness _) -> true
        | WitnessParam _, Some _ -> false
        | _, Some(Measure.Witness _) -> false
        | CarryingParam _, Some(Measure.Carrying _) -> true
        | CarryingParam _, Some _ -> false
        // A `Q` is a sequence of its values, so it passes where values are taken.
        | DataParam _, Some _ -> true
        | OtherParam, Some _ -> true

    /// What each measure variable stands for, from the arguments at the parameters that take
    /// values. A variable two arguments disagree on is left out.
    let private bindings (pairs: (Param * Arg) list) : Map<string, Measure.Carried> =
        let found =
            pairs
            |> List.choose (fun (p, a) ->
                match p, a.Carried with
                | (DataParam v | CarryingParam v), Some(Measure.Measured m | Measure.Carrying m) -> Some(v, Measure.Measured m)
                | DataParam v, Some Measure.Plain -> Some(v, Measure.Plain)
                | _ -> None)

        found
        |> List.groupBy fst
        |> List.choose (fun (v, cs) ->
            match cs |> List.map snd |> List.distinct with
            | [ c ] -> Some(v, c)
            | _ -> None)
        |> Map.ofList

    let private insertAt (r: range) (text: string) : Edit =
        { Range = Range.mkRange r.FileName r.Start r.Start
          Text = text }

    /// The witnesses a call is missing, inserted where they belong. The arguments are read
    /// against the parameters in order, and a parameter that takes a witness, met by an argument
    /// that is known not to be one, is a witness left out. None when an argument fits no
    /// parameter or a witness left out cannot be named.
    let private insertWitnesses (index: WitnessIndex) (ps: Param[]) (args: Arg list) : (Edit list * string list) option =
        let rec align (ps: (int * Param) list) (args: Arg list) acc =
            match args, ps with
            | [], _ -> Some(List.rev acc)
            | _, [] -> None
            | a :: _, (i, WitnessParam v) :: ps when (match a.Carried with
                                                      | Some(Measure.Witness _)
                                                      | None -> false
                                                      | Some _ -> true) ->
                align ps args ((i, WitnessParam v, None) :: acc)
            | a :: rest, (i, p) :: ps -> if fits p a.Carried then align ps rest ((i, p, Some a) :: acc) else None

        match align (ps |> Array.indexed |> List.ofArray) args [] with
        | None -> None
        | Some layout ->
            let placed = layout |> List.choose (fun (i, p, a) -> a |> Option.map (fun a -> i, p, a))
            let bound = bindings (placed |> List.map (fun (_, p, a) -> p, a))

            let named =
                layout
                |> List.choose (fun (i, p, a) ->
                    match p, a with
                    | WitnessParam v, None ->
                        Some(bound.TryFind v |> Option.bind (Witness.ofCarried index) |> Option.map (fun w -> i, Witness.atom w))
                    | _ -> None)

            if named.IsEmpty || named |> List.exists Option.isNone then
                None
            else
                let named = named |> List.choose id

                // Each witness goes in front of the first argument placed after it.
                let edits =
                    named
                    |> List.groupBy (fun (i, _) -> placed |> List.find (fun (j, _, _) -> j > i) |> fun (_, _, a) -> a)
                    |> List.map (fun (a, ws) -> insertAt a.Range (ws |> List.map (fun (_, w) -> w + " ") |> String.concat ""))

                Some(edits, named |> List.map snd)

    let private nameOf (e: SynExpr) =
        match e with
        | SynExpr.Ident id -> Some id.idText
        | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) -> Some(ids |> List.map (fun i -> i.idText) |> String.concat ".")
        | _ -> None

    let rec private unparen (e: SynExpr) =
        match e with
        | SynExpr.Paren(expr = inner) -> unparen inner
        | e -> e

    /// The findings for a parsed and checked file.
    let ofFile (parse: FSharpParseFileResults) (check: FSharpCheckFileResults) : Finding list =
        let index = lazy (WitnessIndex.ofCheck check)

        let uses =
            lazy
                (check.GetAllUsesOfAllSymbolsInFile()
                 |> Seq.groupBy (fun u -> u.Range.End)
                 |> dict)

        let symbolEndingAt (p: pos) =
            match uses.Force().TryGetValue p with
            | true, us -> us |> Seq.map (fun u -> u.Symbol) |> List.ofSeq
            | _ -> []

        let lastIdent (e: SynExpr) =
            match e with
            | SynExpr.Ident id -> Some id
            | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) when not ids.IsEmpty -> Some(List.last ids)
            | _ -> None

        let typeOf (e: SynExpr) : FSharpType option =
            lastIdent (unparen e)
            |> Option.bind (fun id ->
                symbolEndingAt id.idRange.End
                |> List.tryPick (fun s ->
                    match s with
                    | :? FSharpMemberOrFunctionOrValue as v when not v.IsFunction -> Some v.FullType
                    | :? FSharpField as f -> Some f.FieldType
                    | _ -> None))

        let argOf (e: SynExpr) : Arg =
            { Range = e.Range
              Name = nameOf (unparen e)
              Carried = typeOf e |> Option.bind Measure.carried }

        let functionOf (head: SynExpr) : FSharpMemberOrFunctionOrValue option =
            lastIdent head
            |> Option.bind (fun id ->
                symbolEndingAt id.idRange.End
                |> List.tryPick (fun s ->
                    match s with
                    | :? FSharpMemberOrFunctionOrValue as v when
                        v.IsModuleValueOrMember
                        && v.CurriedParameterGroups.Count > 0
                        && v.CurriedParameterGroups |> Seq.forall (fun g -> g.Count = 1)
                        && (match v.DeclaringEntity with
                            | Some e -> (try e.FullName with _ -> "").StartsWith "Marey"
                            | None -> false)
                        ->
                        Some v
                    | _ -> None))

        let paramsOf (v: FSharpMemberOrFunctionOrValue) =
            v.CurriedParameterGroups |> Seq.map (fun g -> paramOf g.[0].Type) |> Array.ofSeq

        // Every application whose function is not itself applied: `f a b` once, not `f a` too.
        let calls =
            ParsedInput.fold
                (fun acc (path: SyntaxVisitorPath) node ->
                    match node with
                    | SyntaxNode.SynExpr(SynExpr.App(isInfix = false) as e) ->
                        let applied =
                            match path with
                            | SyntaxNode.SynExpr(SynExpr.App(funcExpr = f)) :: _ -> obj.ReferenceEquals(f, e)
                            | _ -> false

                        if applied then acc else e :: acc
                    | _ -> acc)
                []
                parse.ParseTree
            |> List.rev

        let rec spine (e: SynExpr) (args: SynExpr list) =
            match e with
            | SynExpr.App(isInfix = false; funcExpr = f; argExpr = a) -> spine f (a :: args)
            | head -> head, args

        calls
        |> List.choose (fun call ->
            let head, args = spine call []

            match functionOf head with
            | None -> None
            | Some f ->
                let ps = paramsOf f
                let args = args |> List.map argOf

                if args.Length > ps.Length then
                    None
                else
                    let pairs = List.zip (ps |> Array.take args.Length |> List.ofArray) args
                    let index = index.Force()
                    let name = f.DisplayName

                    // As the call is written, `Chart.lineIn`, where the module is a name.
                    let called =
                        match f.DeclaringEntity with
                        | Some e when e.IsFSharpModule -> e.DisplayName + "." + name
                        | _ -> name

                    let missing =
                        pairs
                        |> List.exists (fun (p, a) ->
                            p.IsWitnessParam
                            && (match a.Carried with
                                | Some(Measure.Witness _) -> false
                                | Some _ -> true
                                | None -> false))

                    if missing then
                        insertWitnesses index ps args
                        |> Option.map (fun (edits, ws) ->
                            { Code = "MAREY001"
                              Range = call.Range
                              Message = sprintf "%s takes the witnesses of its units before the values: %s." called (String.concat " " ws)
                              Fixes =
                                [ { Title = sprintf "Insert %s" (String.concat " " ws)
                                    Edits = edits } ] })
                    else
                        let bound = bindings pairs

                        let wrong =
                            pairs
                            |> List.choose (fun (p, a) ->
                                match p, a.Carried with
                                | WitnessParam v, Some(Measure.Witness w) ->
                                    match bound.TryFind v with
                                    | Some(Measure.Measured m as c) ->
                                        let same =
                                            match Measure.parse w |> Option.bind index.Names.Expand, Measure.parse m |> Option.bind index.Names.Expand with
                                            | Some a, Some b -> a = b
                                            | _ -> true

                                        if same then None
                                        else Witness.ofCarried index c |> Option.map (fun r -> a, w, m, r)
                                    | _ -> None
                                | _ -> None)

                        let raw =
                            pairs
                            |> List.choose (fun (p, a) ->
                                match p, a.Carried with
                                | CarryingParam _, Some((Measure.Measured _ | Measure.Plain) as c) -> Some(a, c)
                                | _ -> None)

                        if not wrong.IsEmpty then
                            let edits = wrong |> List.map (fun (a, _, _, r) -> { Range = a.Range; Text = Witness.atom r })

                            let said =
                                wrong
                                |> List.map (fun (_, w, m, r) -> sprintf "the witness of %s for values in %s, whose witness is %s" (readable w) (readable m) r)
                                |> String.concat "; "

                            Some
                                { Code = "MAREY002"
                                  Range = call.Range
                                  Message = sprintf "%s is given %s." called said
                                  Fixes =
                                    [ { Title =
                                          wrong |> List.map (fun (_, _, _, r) -> r) |> String.concat ", " |> sprintf "Use %s"
                                        Edits = edits } ] }
                        elif not raw.IsEmpty then
                            // The witness form beside this function, `lineIn` beside `line`.
                            let witnessForm =
                                f.DeclaringEntity
                                |> Option.bind (fun e ->
                                    e.MembersFunctionsAndValues
                                    |> Seq.tryFind (fun m -> m.DisplayName = name + "In" && m.CurriedParameterGroups |> Seq.forall (fun g -> g.Count = 1)))
                                |> Option.bind (fun m -> insertWitnesses index (paramsOf m) args)
                                |> Option.bind (fun (edits, ws) ->
                                    lastIdent head
                                    |> Option.map (fun id ->
                                        { Title = sprintf "Use %sIn %s" called (String.concat " " ws)
                                          Edits = { Range = Range.mkRange id.idRange.FileName id.idRange.End id.idRange.End; Text = "In" } :: edits }))

                            let carrying =
                                let wrapped =
                                    raw
                                    |> List.map (fun (a, c) ->
                                        Witness.ofCarried index c
                                        |> Option.map (fun w ->
                                            [ insertAt a.Range ("(" + Witness.atom w + ".Of ")
                                              { Range = Range.mkRange a.Range.FileName a.Range.End a.Range.End
                                                Text = ")" } ]))

                                if wrapped |> List.exists Option.isNone then None
                                else
                                    Some
                                        { Title = "Make the values carry their witness"
                                          Edits = wrapped |> List.choose id |> List.concat }

                            let fixes = List.choose id [ witnessForm; carrying ]

                            if fixes.IsEmpty then
                                None
                            else
                                let said =
                                    raw
                                    |> List.map (fun (a, c) -> sprintf "%s %s" (defaultArg a.Name "an argument") (describe c))
                                    |> String.concat ", "

                                Some
                                    { Code = "MAREY003"
                                      Range = call.Range
                                      Message = sprintf "%s takes values that carry their witness (a Q), and is given values without one: %s." called said
                                      Fixes = fixes }
                        else
                            None)

    /// The text between two positions, lines 1-based and columns 0-based as the compiler counts.
    let private between (source: ISourceText) (a: pos) (b: pos) =
        if a.Line = b.Line then
            source.GetLineString(a.Line - 1).Substring(a.Column, b.Column - a.Column)
        else
            let lines =
                [ yield source.GetLineString(a.Line - 1).Substring a.Column
                  for l in a.Line + 1 .. b.Line - 1 -> source.GetLineString(l - 1)
                  yield source.GetLineString(b.Line - 1).Substring(0, b.Column) ]

            String.Join("\n", lines)

    /// The text a range covers.
    let textOf (source: ISourceText) (r: range) : string = between source r.Start r.End

    /// A fix as one edit over the span its edits cover, for a host that takes one replacement
    /// per fix (FSharp.Analyzers.SDK's).
    let collapse (source: ISourceText) (fix: Fix) : Edit =
        let edits = fix.Edits |> List.sortBy (fun e -> e.Range.Start.Line, e.Range.Start.Column)
        let first = edits |> List.head
        let last = edits |> List.maxBy (fun e -> e.Range.End.Line, e.Range.End.Column)
        let span = Range.mkRange first.Range.FileName first.Range.Start last.Range.End
        let between = between source

        let text = Text.StringBuilder()
        let mutable at = span.Start

        for e in edits do
            text.Append(between at e.Range.Start).Append(e.Text) |> ignore
            at <- e.Range.End

        { Range = span
          Text = text.ToString() }
