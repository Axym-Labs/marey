namespace FSharp

namespace Marey

    [<NoComparison>]
    type ResolvedTool =
        {

          Name: string

          Path: string option

          Install: string
        }

        member IsAvailable: bool

    [<NoComparison>]
    type ToolRun =
        {
          ExitCode: int
          Out: string
          Error: string
        }

    [<NoComparison>]
    type ToolOutcome =

        | ToolUnavailable of string

        | ToolTimedOut of string

        | ToolRan of ToolRun

    [<RequireQualifiedAccess>]
    module Toolchain =

        val onPath: name: string -> string option

        val resolve: install: string -> name: string -> ResolvedTool

        val missing: consequence: string -> tool: ResolvedTool -> string

        val report: consequence: string -> tool: ResolvedTool -> string

        val run:
          tool: ResolvedTool ->
            arguments: string ->
            workingDirectory: string -> timeoutMs: int -> ToolOutcome

        val runArgs:
          tool: ResolvedTool ->
            arguments: string seq ->
            workingDirectory: string -> timeoutMs: int -> ToolOutcome

        val commandLine: tool: ResolvedTool -> arguments: string seq -> string
