namespace Marey.CSharp

open Marey

/// D124 — the SVG backend as a C# delegate: `fig.Save(SvgRenderer.Render, "plot.svg", 800, 500)`.
/// A method, so that C# converts the method group to the `Func` that `Save` and `Render` take.
[<AbstractClass; Sealed>]
type SvgRenderer =

    /// A scene as SVG text. `SvgBackend.render`.
    static member Render(extent: Extent<px>, node: Node) : string = SvgBackend.render extent node
