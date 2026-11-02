namespace Marey.Witnesses

    module Measure =

        type Factor = string * int

        val parse: text: string -> Factor list option

        type Carried =

            | Measured of string

            | Plain

            | Carrying of string

            | Witness of string

        val carried: t: FSharp.Compiler.Symbols.FSharpType -> Carried option

namespace Marey.Witnesses

    type WitnessEntry =
        {
          Name: string
          Text: string
          Base: Measure.Factor list
        }

    [<Sealed>]
    type WitnessIndex =

        member Equivalent: factors: Measure.Factor list -> WitnessEntry option

        member Exact: text: string -> WitnessEntry option

        member Entries: WitnessEntry list

        member Unspecified: string option

    module WitnessIndex =

        val ofCheck:
          check: FSharp.Compiler.CodeAnalysis.FSharpCheckFileResults ->
            WitnessIndex

    module Witness =

        val atom: expression: string -> string

        val ofMeasure: index: WitnessIndex -> text: string -> string option

        val ofCarried:
          index: WitnessIndex -> c: Measure.Carried -> string option

namespace Marey.Witnesses

    type Edit =
        {
          Range: FSharp.Compiler.Text.range
          Text: string
        }

    type Fix =
        {
          Title: string
          Edits: Edit list
        }

    type Finding =
        {
          Code: string
          Range: FSharp.Compiler.Text.range
          Message: string
          Fixes: Fix list
        }

    module Findings =

        val ofFile:
          parse: FSharp.Compiler.CodeAnalysis.FSharpParseFileResults ->
            check: FSharp.Compiler.CodeAnalysis.FSharpCheckFileResults ->
            Finding list

        val textOf:
          source: FSharp.Compiler.Text.ISourceText ->
            r: FSharp.Compiler.Text.range -> string

        val collapse:
          source: FSharp.Compiler.Text.ISourceText -> fix: Fix -> Edit
