namespace FSharp

namespace Marey

    [<NoComparison>]
    type FfmpegReport =
        {
          Tool: ResolvedTool

          Version: string option
        }

        member IsAvailable: bool

    [<NoComparison>]
    type MuxOptions =
        {

          Codec: string option

          Crf: int option

          PixelFormat: string option

          Overwrite: bool

          AllowStale: bool

          LogLevel: string

          Extra: string list

          TimeoutMs: int
        }

    [<RequireQualifiedAccess>]
    module MuxOptions =

        val Default: MuxOptions

    [<NoComparison>]
    type MuxResult =
        {

          Output: string
          ExitCode: int

          Command: string

          Log: string

          FrameCount: int

          Fps: float

          FrameSize: (int * int) option
        }

        member Succeeded: bool

    [<NoComparison>]
    type MuxOutcome =

        | MuxUnavailable of string

        | MuxRefused of string

        | MuxTimedOut of string

        | MuxRan of MuxResult

    [<NoComparison>]
    type VideoInfo =
        {
          Codec: string
          Width: int
          Height: int

          FrameCount: int

          Fps: float

          Duration: float<Data.UnitSystems.SI.UnitSymbols.s>

          FormatTags: Map<string,string>

          StreamTags: Map<string,string>
        }

        member BuildTags: (string * string) list

    [<NoComparison>]
    type InspectOutcome =

        | InspectUnavailable of string

        | InspectFailed of string
        | Inspected of VideoInfo

    [<RequireQualifiedAccess>]
    module Video =

        val ffmpeg: unit -> FfmpegReport

        val ffprobe: unit -> ResolvedTool

        val report: unit -> string

        val frameSize: path: string -> (int * int) option

        val check:
          options: MuxOptions -> sequence: FrameSequence -> string option

        val args:
          options: MuxOptions ->
            sequence: FrameSequence -> output: string -> string list

        val muxWith:
          found: FfmpegReport ->
            options: MuxOptions ->
            sequence: FrameSequence -> output: string -> MuxOutcome

        val mux:
          options: MuxOptions ->
            sequence: FrameSequence -> output: string -> MuxOutcome

        val inspectWith: tool: ResolvedTool -> path: string -> InspectOutcome

        val inspect: path: string -> InspectOutcome
