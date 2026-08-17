namespace Quiver

// ======================================================================================
// §5 — THE SCENE IR
//
// "The most important constraint in the project: this type is small, closed, total, and
//  backend-agnostic. Every addition costs you six backend implementations."
//
// Read this before adding anything below:
//
//   §5.5 — what is deliberately NOT here: axes, legends, titles, colorbars, facets, error
//   bars, box plots. Those are *compiled into* Path/Text/Markers by the layer above. "If you
//   find yourself wanting `| Axis of ...`, the design has failed."
//
// Two departures from the design sketch, both forced by other parts of the same design:
//
//   1. `Transform` carries an `Affine<nd,nd>`, not a function. A closure has reference
//      equality, which would silently destroy the structural-equality diffing that §5.2
//      depends on, and it cannot be serialized (§15). See Affine.fs.
//
//   2. Collections are arrays, not lists. §1.3: "Per-frame allocation of records will destroy
//      a 60 fps loop. The hot path (scene -> backend, particle stepping) must be struct/array
//      -based, not list-based." A 100 000-segment path as a cons list is 100 000 heap objects.
//
// One deliberate omission, recorded so the next person does not think it was an oversight:
// there is **no group-opacity node**. Fading a whole group is a real M3 need, `Color.A`
// covers the common case, and the two differ only where a group's own shapes overlap. When
// M3 forces the issue it is a costed decision — one variant, six backends — not a reflex.
// ======================================================================================

/// A stop in a gradient ramp. `Offset` is in [0,1] along the gradient axis.
[<Struct>]
type GradientStop = { Offset: float; Color: Color }

/// How a region is filled.
///
/// `Capabilities.Gradients` exists because TikZ's gradient support is genuinely weaker than
/// SVG's (§5, backend contract): "The compiler layer queries caps and *degrades* rather than
/// silently producing wrong output."
[<NoComparison>]
type Paint =
    | Solid of Color
    /// Axis given by two points in panel space.
    | LinearGradient of from: Vec2<nd> * until: Vec2<nd> * stops: GradientStop[]
    /// Centre, radius in panel space, stops from centre outward.
    | RadialGradient of centre: Vec2<nd> * radius: float<nd> * stops: GradientStop[]

type LineCap =
    | Butt
    | Round
    | Square

type LineJoin =
    | Miter
    | RoundJoin
    | Bevel

/// How the interior of a self-intersecting path is decided. Marching-squares contours with
/// holes and any star polygon need `EvenOdd`; everything else wants `NonZero`.
type FillRule =
    | NonZero
    | EvenOdd

[<NoComparison>]
type Stroke =
    { Paint: Paint
      /// Stroke width is in **px**, not `nd`: a hairline must stay a hairline when the figure
      /// is rendered at 4K. This is the one place device units legitimately appear inside an
      /// otherwise resolution-independent IR, and it is why `Extent<px>` reaches the backend.
      Width: float<px>
      Dash: float<px>[]
      DashOffset: float<px>
      Cap: LineCap
      Join: LineJoin
      MiterLimit: float }

/// §5 — Path segments. Four cases, and it stays four cases.
///
/// No arcs and no quadratic curves: both are exactly representable (arcs to within any
/// tolerance) as cubics, and every extra case is a case in six backends. Polar gridlines,
/// rounded corners and circular markers all flatten to cubics one layer up.
[<NoComparison>]
type PathSeg =
    | MoveTo of Vec2<nd>
    | LineTo of Vec2<nd>
    | CubicTo of c1: Vec2<nd> * c2: Vec2<nd> * until: Vec2<nd>
    | ClosePath

/// A font request. Resolution to an actual face is the backend's business — §10 makes
/// `ITextShaper` a capability precisely because the answer differs per target.
[<Struct>]
type FontSpec =
    { Family: string
      Size: float<pt>
      /// CSS-style numeric weight: 400 normal, 700 bold.
      Weight: int
      Italic: bool }

type TextAnchor =
    | AnchorStart
    | AnchorMiddle
    | AnchorEnd

type TextBaseline =
    | BaselineAlphabetic
    | BaselineMiddle
    | BaselineHanging

[<NoComparison>]
type TextRun =
    { Text: string
      At: Vec2<nd>
      Font: FontSpec
      Paint: Paint
      Anchor: TextAnchor
      Baseline: TextBaseline
      /// Rotation about `At`. Measured, so a degrees/radians mix-up is a compile error.
      Rotation: float<rad> }

/// §5.1 — Marker shapes.
///
/// The design's node is `Markers of Vec2<nd>[] * float<px> * Paint` — no shape. Shape is a
/// standard encoding channel and the *only* redundant encoding for colour, so a
/// colour-blind-safe scatter (§9) needs it. Without a shape field the compiler layer has to
/// emit one `Path` per point to vary it, which defeats §5.1 — "the single biggest performance
/// decision in the IR" — in the exact case that motivated batching.
///
/// A closed enum on an existing node is the cheap way to have it: no new backend dispatch,
/// just a shape table each backend fills once.
type MarkerShape =
    | MarkCircle
    | MarkSquare
    | MarkDiamond
    | MarkTriangleUp
    | MarkTriangleDown
    | MarkCross
    | MarkPlus

/// §5.1 — "A 100 000-point scatter must be *one* IR node, not 100 000 `Path` nodes."
///
/// The GPU backend turns this into one instanced draw call; SVG into a `<use>`/`<symbol>`
/// pair. Any geometry that can appear ten thousand times gets a batched node.
[<NoComparison>]
type MarkerBatch =
    { Points: Vec2<nd>[]
      Shape: MarkerShape
      /// Diameter in px — device units, for the same reason as `Stroke.Width`.
      Size: float<px>
      Fill: Paint option
      Stroke: Stroke option }

/// Raw RGBA8 pixels, row-major, top row first.
///
/// Note that structural equality on this is O(pixels), which makes a `Raster` an expensive
/// thing to diff (§5.2). Wrap large images in a `Keyed` node so the diff can compare keys
/// first and never look at the bytes.
[<NoComparison>]
type Image =
    { Width: int
      Height: int
      Rgba: byte[] }

[<NoComparison>]
type RasterNode =
    { Image: Image
      /// Destination box in panel space.
      Bounds: Rect<nd>
      /// Bilinear when true, nearest-neighbour when false. False is right for heatmaps of
      /// discrete data, where smoothing invents values that were never measured.
      Smooth: bool }

/// §5.2 — Stable identity for diffing.
///
/// "Scene graphs are immutable F# values with structural equality. Re-render on interaction =
/// build a new `Scene`, diff against the previous one, emit only the changed subtrees.
/// `Keyed` supplies React-style stable identity so the diff is O(changed) rather than
/// O(scene)."
[<Struct>]
type Key = Key of string

/// §5.3 — What a shape *means*.
///
/// "Hit-testing, tooltips, `aria-label`s on SVG output, and clickable legends all need the IR
/// to remember what a shape means, not just where it is. Retrofitting this is painful; it
/// costs almost nothing now."
type SemanticRole =
    | RoleSeries
    | RoleDatum
    | RoleAxis
    | RoleLegend
    | RoleAnnotation
    /// Gridlines, panel frames — everything a screen reader should skip.
    | RoleDecoration
    | RoleOther of string

[<NoComparison>]
type Semantics =
    { Role: SemanticRole
      /// Human-readable description. Becomes `aria-label` on SVG.
      Label: string
      /// Free-form metadata: the data value behind a mark, a series id for legend linking, a
      /// tooltip template. Array of pairs rather than a Map so the node stays structurally
      /// comparable and Fable-friendly.
      Data: (string * string)[] }

/// §5 — The IR. Nine cases. Guard it obsessively.
[<NoComparison>]
type Node =
    | Path of segments: PathSeg[] * stroke: Stroke option * fill: Paint option * rule: FillRule
    | Text of TextRun
    | Markers of MarkerBatch
    | Raster of RasterNode
    | Group of Node[]
    | Transform of Affine<nd, nd> * Node
    | Clip of PathSeg[] * Node
    | Keyed of Key * Node
    | Annotated of Semantics * Node

[<RequireQualifiedAccess>]
module Stroke =

    let solid (c: Color) (w: float<px>) =
        { Paint = Solid c
          Width = w
          Dash = [||]
          DashOffset = 0.0<px>
          Cap = Butt
          Join = Miter
          MiterLimit = 4.0 }

    let dashed (c: Color) (w: float<px>) (pattern: float<px>[]) = { solid c w with Dash = pattern }

[<RequireQualifiedAccess>]
module Scene =

    /// The identity of the `Group` monoid, and the right answer for "draw nothing".
    let empty = Group [||]

    let group (ns: Node seq) = Group(Array.ofSeq ns)

    let keyed (name: string) (n: Node) = Keyed(Key name, n)

    let annotate (role: SemanticRole) (label: string) (n: Node) =
        Annotated({ Role = role; Label = label; Data = [||] }, n)

    /// Polyline through the given panel-space points. The single most-used constructor in
    /// the library, so it builds its array in one pass with no intermediate list.
    let polyline (stroke: Stroke) (pts: Vec2<nd>[]) =
        if pts.Length = 0 then
            empty
        else
            let segs = Array.zeroCreate pts.Length
            segs.[0] <- MoveTo pts.[0]

            for i in 1 .. pts.Length - 1 do
                segs.[i] <- LineTo pts.[i]

            Path(segs, Some stroke, None, NonZero)

    let polygon (stroke: Stroke option) (fill: Paint option) (pts: Vec2<nd>[]) =
        if pts.Length = 0 then
            empty
        else
            let segs = Array.zeroCreate (pts.Length + 1)
            segs.[0] <- MoveTo pts.[0]

            for i in 1 .. pts.Length - 1 do
                segs.[i] <- LineTo pts.[i]

            segs.[pts.Length] <- ClosePath
            Path(segs, stroke, fill, NonZero)

    let rect (stroke: Stroke option) (fill: Paint option) (r: Rect<nd>) =
        polygon
            stroke
            fill
            [| { X = r.X0; Y = r.Y0 }
               { X = r.X1; Y = r.Y0 }
               { X = r.X1; Y = r.Y1 }
               { X = r.X0; Y = r.Y1 } |]

    /// Bottom-up rewrite. The basis for theming passes, clip hoisting, and the scene diff.
    let rec transform (f: Node -> Node) (n: Node) : Node =
        match n with
        | Group ns -> f (Group(Array.map (transform f) ns))
        | Transform(a, c) -> f (Transform(a, transform f c))
        | Clip(p, c) -> f (Clip(p, transform f c))
        | Keyed(k, c) -> f (Keyed(k, transform f c))
        | Annotated(s, c) -> f (Annotated(s, transform f c))
        | leaf -> f leaf

    /// Pre-order fold over every node, containers included.
    let rec fold (f: 'a -> Node -> 'a) (state: 'a) (n: Node) : 'a =
        let state = f state n

        match n with
        | Group ns -> Array.fold (fold f) state ns
        | Transform(_, c)
        | Clip(_, c)
        | Keyed(_, c)
        | Annotated(_, c) -> fold f state c
        | Path _
        | Text _
        | Markers _
        | Raster _ -> state

    /// Total number of nodes. A cheap sanity check in tests, and the denominator when
    /// arguing that §5.2's diff is actually O(changed).
    let count (n: Node) = fold (fun acc _ -> acc + 1) 0 n

    /// Number of *drawn primitives*, counting a batch as its element count. This is the
    /// number that predicts render cost — and the gap between it and `count` is exactly the
    /// win §5.1 is buying.
    let primitiveCount (n: Node) =
        fold
            (fun acc node ->
                match node with
                | Markers b -> acc + b.Points.Length
                | Path _
                | Text _
                | Raster _ -> acc + 1
                | _ -> acc)
            0
            n

    /// Drop subtrees that draw nothing. Worth running before diffing: an empty group compares
    /// unequal to a missing one, and generators produce plenty of both.
    let rec prune (n: Node) : Node =
        match n with
        | Group ns ->
            let kept = ns |> Array.map prune |> Array.filter (fun c -> c <> empty)
            if kept.Length = 0 then empty else Group kept
        | Transform(a, c) ->
            match prune c with
            | c when c = empty -> empty
            | c -> Transform(a, c)
        | Clip(p, c) ->
            match prune c with
            | c when c = empty -> empty
            | c -> Clip(p, c)
        | Keyed(k, c) ->
            match prune c with
            | c when c = empty -> empty
            | c -> Keyed(k, c)
        | Annotated(s, c) ->
            match prune c with
            | c when c = empty -> empty
            | c -> Annotated(s, c)
        | Path(segs, _, _, _) when segs.Length = 0 -> empty
        | Markers b when b.Points.Length = 0 -> empty
        | Text t when System.String.IsNullOrEmpty t.Text -> empty
        | leaf -> leaf
