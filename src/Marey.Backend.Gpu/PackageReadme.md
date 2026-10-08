# Marey.Backend.Gpu

GPU rendering for [Marey](https://github.com/Axym-Labs/marey), a dimensionally-typed
visualization system for F#. Skia draws on an OpenGL 3.3 core context, offscreen. `netstandard2.1`.

```fsharp
#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Gpu"

open Marey

use gpu = new GpuRenderer()
let extent = Chart.size 800.0 500.0

// Lay out with the renderer's own measurer, so labels sit where their gutters expect them.
let png = Chart.Scatter(xs, ys) |> Chart.toSceneWith gpu.Measurer extent |> gpu.RenderPng extent
```

## One context, any thread

A `GpuRenderer` owns a thread, and the GL context, Skia's GPU context and one text measurer live
on it until the renderer is disposed. `RenderPng` and `RenderImage` can be called from any
thread, at the same time; the calls take turns on the GPU. So `gpu.RenderPng` is a renderer
function like `SkiaBackend.renderPng`, and `Frames.render FramePass.Concurrent gpu.RenderPng`
compiles a film's frames in parallel and draws them in turn.

Keep one renderer for many renders. Creating one opens a context and resolves fonts, which is the
cost a renderer exists to pay once.

## What it draws

Everything the Skia backend draws, with the same code, on the GPU: text is shaped with HarfBuzz
and placed with the advances it was measured with. The output is close to the CPU raster's and
not identical to the byte, because a GPU antialiases glyph edges a little differently.

## Requirements

An OpenGL 3.3 core context, through GLFW. Construction fails with the driver's reason when none
can be made. On Linux with no GPU or no display, run under `xvfb-run` with Mesa's `llvmpipe`
(`libgl1-mesa-dri`); `GpuBackend.probe ()` says which driver a renderer would get.
