# 13. The GPU, a window and the browser

Every backend so far turns a scene into a document or a file. Three more turn it into pixels on a
screen: `Marey.Backend.Gpu` draws on the GPU, offscreen or in a window, and `Marey.Backend.Web`
draws on a canvas in a browser page, where the figure is compiled too. Nothing above the scene
differs between them.

## On the GPU

`GpuRenderer` draws with the Skia backend's code on an OpenGL 3.3 context, so text, gradients and
clips come out as they do in a PNG, except that a marker batch is one instanced draw, each mark's
shape computed in a shader. A million points take milliseconds. `GpuBackend.probe ()` names the
driver a renderer would get, or says why there is none; on Linux without a GPU, `xvfb-run` and
Mesa's software driver provide one.

```fsharp
#r "nuget: Marey.Backend.Gpu"

let cloudCount = 1_000_000
let cloudNumbers, _ = Rng.units (2 * cloudCount) (Rng.seeded 11UL)
let cloudX = Array.init cloudCount (fun i -> cloudNumbers.[2 * i] * 1.0<s>)
let cloudY = Array.init cloudCount (fun i -> (cloudNumbers.[2 * i + 1] - 0.5) * 1.0<m>)
let cloud = Chart.scatter U.second U.metre cloudX cloudY

match GpuBackend.probe () with
| Ok driver ->
    use gpu = new GpuRenderer()
    let extent = Chart.size 800.0 500.0
    let png = cloud |> Chart.toSceneWith gpu.Measurer extent |> gpu.RenderPng extent
    printfn "a million points on %s: %d bytes of PNG" driver png.Length
| Error why -> printfn "no OpenGL 3.3 context here: %s" why
```

One renderer owns one thread, one context and one text measurer, and any thread may call it. Keep
one for many renders. `GpuSim.stam` and `GpuSim.lbm` step §8's fluids on it, over the same states,
and agree with the CPU's steps to a few parts in a million.

## In a window

`Chart.show fig` opens a figure in a window and returns when it is closed; `Live.show` does the
same for a live figure, with its controls in a strip above it. Drag to zoom, shift-drag to pan,
double-click to reset, hover to read, Tab and the arrows for the controls, Escape to close. The
window's pointer and keys become the messages a notebook page sends for the same gestures, stepped
by `Live.step`, so a session in a window reaches the states a notebook session would, and a
recording made in one replays in the other.

```fsharp norun
Live.show explorer
```

`GpuWindow.Open` returns at once and drives the same window from code. A window runs on a thread of
its own, which Linux and Windows allow and macOS does not.

## In a browser

`Marey.Backend.Web` is for [Fable](https://fable.io), which compiles F# to JavaScript. A Fable app
references `Marey` and `Marey.Backend.Web` from NuGet, builds a figure with the code of this guide,
and draws it on a canvas:

```fsharp norun
[<Emit("document.getElementById($0)")>]
let canvasById (_id: string) : CanvasElement = jsNative

let ctx = (canvasById "plot").getContext "2d"
let measured = WebText.measurerNow ()
let scene = Chart.toSceneWith measured.Measurer extent figure
CanvasBackend.draw measured.Measurer ctx extent scene
```

The compiler is the same F#, so the browser lays a figure out as a kernel does: the test suite's
40 SVG baselines are reproduced byte for byte under Node. The canvas measures text in the font it
draws with, and `WebText.measurerNow ()` says whether the page's web fonts have arrived. A batch of
2000 marks or more is drawn by WebGL2, composited in place. `Marey.Sim` compiles under Fable too, so
a simulation runs in the page from the same source; a chaotic one diverges from .NET's after some
seconds, because JavaScript's `sin` and `exp` differ in the last bit. `examples/web/` is a page with
two sliders and a cloud of up to a million points.
