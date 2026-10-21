namespace Marey

    [<Sealed>]
    type SvgBackend =
        interface IBackend

        new: ?dpi: float<px/mm> * ?idPrefix: string -> SvgBackend

        member Result: string

        member RootTransform: Affine<nd,px>

    [<NoEquality; NoComparison>]
    type SvgPatch =
        {

          Document: string

          Defs: string

          Elements: (string * string) array option
        }

    [<CompilationRepresentation (enum<CompilationRepresentationFlags> (4));
      RequireQualifiedAccess>]
    module SvgBackend =

        val renderScoped:
          idPrefix: string -> extent: Extent<px> -> node: Node -> string

        val render: extent: Extent<px> -> node: Node -> string

        val renderPatch:
          extent: Extent<px> -> before: Node -> after: Node -> SvgPatch

        val renderToFile:
          path: string -> extent: Extent<px> -> node: Node -> unit

namespace Marey.CSharp

    [<AbstractClass; Sealed; Class>]
    type SvgRenderer =

        static member
          Render: extent: Marey.Extent<Marey.px> * node: Marey.Node -> string
