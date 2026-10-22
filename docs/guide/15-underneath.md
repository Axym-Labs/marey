# 15. The scene, and the way out

Everything in this guide compiles to one scene, a tree of ten kinds of node, and every backend
draws only that: `Path`, `Text`, `Markers`, `Raster`, `Group`, `Transform`, `Clip`, `Opacity`,
`Keyed` and `Annotated`. It is the level at which a figure that came out wrong can be read.

```fsharp
let fallScene = Chart.toScene size fall
printfn "the dropped ball is %d nodes and %d primitives" (Scene.count fallScene) (Scene.primitiveCount fallScene)
```

A scatter of any size is one `Markers` node, which is what lets the GPU draw a million points as
one instanced call. `Keyed` and `Annotated` carry identity and meaning through to the output: SVG
gets ids and ARIA roles, TikZ gets comments, so a rendered figure can be diffed and read as text.
A figure names its parts `figure`, `figure/panel-0-0`, `figure/panel-0-0/layer-0`, spelled by
`Keys`, and `Scene.mapKey` rewrites one part by name and leaves every other part as it was:

```fsharp
let faded = fallScene |> Scene.mapKey (Keys.layer (Keys.panel 0 0) 0) (Scene.opacity 0.25)
printfn "the model curve found and faded: %b" (Scene.tryFindKey (Keys.layer (Keys.panel 0 0) 0) faded).IsSome
```

`Scene.opacity` fades a group as one, which differs from setting each shape's alpha wherever the
group's shapes overlap.

## The way out

**Your own scales.** `Chart.series` builds a typed series against scales you state, and
`Chart.ofSeries` draws it, so everything downstream works on scales of your choosing.

**Erasing on purpose.** `Erase` is the one place a `float<'u>` becomes a number with a runtime
`Dim`. Below it there are no measures, which is how one backend serves every unit.

```fsharp
let statedScale = Scale.log10 U.metre (1.0<m>, 1000.0<m>)
let ownSeries = Chart.seriesIn "measured" U.second U.metre tModel xModel
let ownFigure = Chart.ofSeries GeomLine ownSeries

printfn "an erased scale carries %s" (Dim.show (Erase.scale statedScale).Dim)
```

**Your own backend.** A renderer is a function `Extent<px> -> Node -> string`, or bytes, and
`Chart.render` takes any such function, with nothing to register:

```fsharp
let countingRenderer (_: Extent<px>) (node: Node) = sprintf "%d nodes" (Scene.count node)

printfn "%s" (fall |> Chart.render countingRenderer (Chart.size 100.0 100.0))
```

A backend that keeps state implements `IBackend`, a begin, draw and finish over a surface, and
`Chart.renderTo` drives it. What a backend can do is its `Capabilities`, and what it cannot is
degraded in a stated way: the SVG backend estimates text widths where Skia shapes them.

What Marey does not do yet is on its own page, [Not in 1.0](../not-in-1.0.md), and what a version
number promises is on [Compatibility](../compatibility.md).
