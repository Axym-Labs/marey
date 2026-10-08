namespace Marey.CSharp

open Marey

/// D124 — PNG through Skia as a C# delegate: `fig.Save(PngRenderer.Render, "plot.png", 800, 500)`.
[<AbstractClass; Sealed>]
type PngRenderer =

    /// A scene as PNG bytes. `SkiaBackend.renderPng`.
    static member Render(extent: Extent<px>, node: Node) : byte[] = SkiaBackend.renderPng extent node

/// D124 — vector PDF through Skia as a C# delegate: `fig.Save(PdfRenderer.Render, "plot.pdf", 800, 500)`.
[<AbstractClass; Sealed>]
type PdfRenderer =

    /// A scene as PDF bytes. `SkiaBackend.renderPdf`.
    static member Render(extent: Extent<px>, node: Node) : byte[] = SkiaBackend.renderPdf extent node
