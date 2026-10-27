# Marey.Backend.Gpu

GPU rendering for [Marey](https://axym-labs.github.io/marey/). Skia draws on an OpenGL 3.3 core
context, offscreen or in a window. `net8.0`.

```fsharp
#r "nuget: Marey.Plot"
#r "nuget: Marey.Backend.Gpu"

open Marey

let numbers, _ = Rng.units 200_000 (Rng.seeded 1UL)
let xs, ys = numbers.[.. 99_999], numbers.[100_000 ..]

match GpuBackend.probe () with
| Ok driver ->
    use gpu = new GpuRenderer()
    let extent = Chart.size 800.0 500.0
    // Lay out with the renderer's own measurer, so labels sit where their gutters expect them.
    let png = Chart.Scatter(xs, ys) |> Chart.toSceneWith gpu.Measurer extent |> gpu.RenderPng extent
    printfn "%d bytes of PNG from %s" png.Length driver
| Error why -> printfn "no OpenGL 3.3 context: %s" why
```

## One context, any thread

A `GpuRenderer` owns a thread, and the GL context, Skia's GPU context and one text measurer live
on it until the renderer is disposed. `RenderPng` and `RenderImage` can be called from any
thread, at the same time; the calls take turns on the GPU. So `gpu.RenderPng` is a renderer
function like `SkiaBackend.renderPng`, and `Frames.render FramePass.Concurrent gpu.RenderPng`
compiles a film's frames in parallel and draws them in turn.

On a HiDPI screen, `gpu.RenderImageAt 2.0 extent scene` draws the scene as a window at a scaling
of 2 shows it: twice the extent's pixels each way, with strokes, marks and text scaled with the
geometry. `RenderPngAt` is the same for PNG bytes.

Keep one renderer for many renders. Creating one opens a context and resolves fonts, which is the
cost a renderer exists to pay once.

## What it draws

Everything the Skia backend draws, with the same code, on the GPU: text is shaped with HarfBuzz
and placed with the advances it was measured with. The output is close to the CPU raster's and
not identical to the byte, because a GPU antialiases path edges a little differently.

Marker batches are the exception, and the reason for the package. Skia draws a marker as a path,
one at a time, so 10⁵ of them take about a fifth of a second on any surface. This renderer draws
a batch as one instanced draw, each mark's shape computed in a shader to Skia's own outline: a
million marks take about 5 ms on a desktop GPU. A batch the shader cannot match (a gradient fill,
a dashed or non-mitered outline, inside an opacity layer, under a clip that is not a rectangle)
is left to Skia.

## Simulation and LIC on the GPU

```fsharp norun
let step = GpuSim.stam gpu options   // the same Fluid as Stam.step, on this GPU
let lattice = GpuSim.lbm gpu water   // the same Lattice as Lbm.step
```

`GpuSim.stam` and `GpuSim.lbm` step `Marey.Sim`'s fluid and lattice states by fragment shaders, so
`Sim.trajectory` and films take them unchanged. A call uploads the state, steps it in float32 and
reads it back; `stamWith k` and `lbmWith k` run k steps a call. The shaders compute the CPU
steps' expressions in the same order, and agree with them to a few parts in a million of a field's
largest value after 200 steps. One device repeats itself to the bit.

A figure compiled with `gpu.Measurer` has its LIC convolved on the GPU, one streamline per pixel:
1.6 ms at 1920 × 1200 where the CPU's fast LIC takes over a second. The texture is the same as the
CPU's, the pixels are not.

## In a window

```fsharp norun
Chart.show figure          // returns when the window is closed
Live.show (interact { … }) // a live figure, its controls in a strip above it
```

Drag to zoom, shift-drag to pan, double-click to reset, hover for a reading in the axes' units, Tab
and the arrow keys for the controls, Escape to close. The window steps a live figure with
`Live.step`, exactly as a notebook does, so `Recorder.show` records a session a notebook would
replay. `GpuWindow.Open` opens one without waiting. Linux and Windows; not macOS, where GLFW
requires the main thread.

## Requirements

An OpenGL 3.3 core context, through GLFW. Construction fails with the driver's reason when none
can be made. On Linux with no GPU or no display, run under `xvfb-run` with Mesa's `llvmpipe`
(`libgl1-mesa-dri`); `GpuBackend.probe ()` says which driver a renderer would get.
