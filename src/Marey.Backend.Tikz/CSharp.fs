namespace Marey.CSharp

open Marey

/// D124 — the TikZ backend as a C# delegate: `fig.Save(TikzRenderer.Render, "plot.tex", 800, 500)`.
[<AbstractClass; Sealed>]
type TikzRenderer =

    /// A scene as a `tikzpicture`. `TikzBackend.render`.
    static member Render(extent: Extent<px>, node: Node) : string = TikzBackend.render extent node
