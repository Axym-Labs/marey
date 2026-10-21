namespace Marey

    [<RequireQualifiedAccess>]
    type TexSeg =
        | MoveTo of Vec2<pt>
        | LineTo of Vec2<pt>
        | CubicTo of c1: Vec2<pt> * c2: Vec2<pt> * until: Vec2<pt>
        | ClosePath

    [<Struct>]
    type TexBox =
        {

          Width: float<pt>

          Height: float<pt>

          Depth: float<pt>
        }

    [<NoComparison>]
    type TexOutline =
        {
          Box: TexBox

          Segments: TexSeg array

          Recoloured: int
        }

    [<RequireQualifiedAccess>]
    module TexOutline =

namespace Marey

    [<NoComparison>]
    type TexReport =
        {

          Engine: ResolvedTool

          Dvisvgm: ResolvedTool

          DvisvgmVersion: string option
        }

        member IsAvailable: bool

    [<NoComparison>]
    type TexOptions =
        {

          Preamble: string

          ClassOptions: string

          BaseSize: float<pt>

          Dpi: float<px/mm>

          TimeoutMs: int
        }

    [<RequireQualifiedAccess>]
    module TexOptions =

        val Default: TexOptions

    [<NoComparison>]
    type TexFailure =
        {

          Source: string

          Reason: string
        }

    [<NoComparison>]
    type TypesetResult =
        {

          Scene: Node

          Typeset: int

          Plain: int

          Failed: TexFailure list

          EngineRuns: int

          Recoloured: int
        }

    [<Sealed>]
    type TexCache =

        new: unit -> TexCache

        member EngineRuns: int

        member Entries: int

    [<NoComparison>]
    type TypesetOutcome =

        | TypesetUnavailable of string

        | Typeset of TypesetResult

    [<RequireQualifiedAccess>]
    module Typeset =

        [<Literal>]
        val Engine: string = "latex"

        val toolchain: unit -> TexReport

        val report: unit -> string

        val sceneWithCache:
          engine: ResolvedTool ->
            dvisvgm: ResolvedTool ->
            cache: TexCache ->
            options: TexOptions ->
            extent: Extent<px> -> node: Node -> TypesetOutcome

        val sceneWith:
          engine: ResolvedTool ->
            dvisvgm: ResolvedTool ->
            options: TexOptions ->
            extent: Extent<px> -> node: Node -> TypesetOutcome

        val scene:
          options: TexOptions ->
            extent: Extent<px> -> node: Node -> TypesetOutcome

        val sceneCached:
          cache: TexCache ->
            options: TexOptions ->
            extent: Extent<px> -> node: Node -> TypesetOutcome

        val sceneOrPlain:
          options: TexOptions ->
            extent: Extent<px> -> node: Node -> Node * string
