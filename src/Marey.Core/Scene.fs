namespace Marey

// ======================================================================================
// THE SCENE IR
//
// "The most important constraint in the project: this type is small, closed, total, and
//  backend-agnostic. Every addition costs you six backend implementations."
//
// Read this before adding anything below:
//
//   What is deliberately NOT here: axes, legends, titles, colorbars, facets, error
//   bars, box plots. Those are *compiled into* Path/Text/Markers by the layer above. "If you
//   find yourself wanting `| Axis of ...`, the design has failed."
//
// Two departures from the design sketch, both forced by other parts of the same design:
//
//   1. `Transform` carries an `Affine<nd,nd>`, not a function. A closure has reference
//      equality, which would silently destroy the structural-equality diffing that §5.2
//      depends on, and it cannot be serialized. See Affine.fs.
//
//   2. Collections are arrays, not lists. §1.3: "Per-frame allocation of records will destroy
//      a 60 fps loop. The hot path (scene -> backend, particle stepping) must be struct/array
//      -based, not list-based." A 100 000-segment path as a cons list is 100 000 heap objects.
//
// One omission, now closed, and left legible rather than tidied away. Through M2.5 there was
// **no group-opacity node**, on the argument that `Color.A` covers the common case, that the
// two differ only where a group's own shapes overlap, and that when M3 forced the issue it
// should be a costed decision — one variant, six backends — not a reflex.
//
// M3 forced it. Fading a group in is what a storyboard asks for, and per-shape alpha cannot
// express it: the moment two shapes in the group overlap, the fade shows a seam that is not in
// the figure. D67 pays the cost as it was quoted — one variant, one `Capabilities` field, one
// `Degrade` rule — and nothing else was let in alongside it.
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

/// Path segments. Four cases, and it stays four cases.
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
    { /// The plain-Unicode form of this run. **Always set**, even when `Tex` is present.
      ///
      /// This is the single source of truth for measurement — `ITextMeasurer` measures this and
      /// nothing else — and it is what every backend that is not a typesetter draws. A run whose
      /// meaning lives only in `Tex` is a run that cannot be laid out and cannot be rendered to
      /// SVG, PNG or a canvas, so this field is not optional and must not be left empty as a
      /// shortcut.
      Text: string
      /// Optional TeX source for this run, for backends that can typeset it.
      ///
      /// The problem this solves: `Chart.withUnitStyle Siunitx` produces axis titles like
      /// `\qty{}{\metre\per\second\squared}`, and a TeX backend must escape ordinary text
      /// (`%`, `_`, `&`, `#`, `$` and backslashes all break a document) while passing that
      /// through verbatim. One figure legitimately contains both, so the distinction has to be
      /// per-run and cannot be a per-render switch.
      ///
      /// No `Capabilities` flag guards this, deliberately: `Text` is always present, so a
      /// backend that cannot typeset TeX has a lossless-enough fallback by construction and
      /// there is nothing to degrade. That is what keeps `Capabilities` at ten fields.
      ///
      /// §10's three routes to mathematical typesetting all read this one field — shelling out
      /// to LaTeX, MathJax/KaTeX on the web backend at M3, and native OpenType MATH layout at
      /// Which is why it is worth a field now rather than a migration later.
      ///
      /// **Known imprecision:** layout is measured against `Text` while a TeX backend typesets
      /// `Tex`, and the two have different widths. Axis titles and tick labels are
      /// anchor-placed so a small error shifts nothing into a collision, and the tick-label
      /// collision ladder only ever coarsens, so an under-measured label cannot produce an
      /// overlapping plan. Bounded, not eliminated.
      Tex: string option
      At: Vec2<nd>
      Font: FontSpec
      Paint: Paint
      Anchor: TextAnchor
      Baseline: TextBaseline
      /// Rotation about `At`. Measured, so a degrees/radians mix-up is a compile error.
      Rotation: float<rad> }

/// Marker shapes.
///
/// The design's node is `Markers of Vec2<nd>[] * float<px> * Paint` — no shape. Shape is a
/// standard encoding channel and the *only* redundant encoding for colour, so a
/// colour-blind-safe scatter needs it. Without a shape field the compiler layer has to
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

/// "A 100 000-point scatter must be *one* IR node, not 100 000 `Path` nodes."
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
/// thing to diff. A key does not help: two rasters under the same key are not therefore
/// the same raster, and `Scene.diff` compares what is under a key. Only **reference** equality
/// lets a diff skip the bytes, so a producer that wants an unchanged raster skipped has to hand
/// back the same array, which `mapKey`'s sharing does and a field recomputed for a new state
/// does not.
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

/// Stable identity for diffing.
///
/// "Scene graphs are immutable F# values with structural equality. Re-render on interaction =
/// build a new `Scene`, diff against the previous one, emit only the changed subtrees.
/// `Keyed` supplies React-style stable identity so the diff is O(changed) rather than
/// O(scene)."
[<Struct>]
type Key = Key of string

/// What a shape *means*.
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

/// The IR. Ten cases. Guard it obsessively.
[<NoComparison>]
type Node =
    | Path of segments: PathSeg[] * stroke: Stroke option * fill: Paint option * rule: FillRule
    | Text of TextRun
    | Markers of MarkerBatch
    | Raster of RasterNode
    | Group of Node[]
    | Transform of Affine<nd, nd> * Node
    | Clip of PathSeg[] * Node
    /// Uniform opacity over the **composited** subtree. In [0,1]; `Scene.opacity` clamps.
    ///
    /// Not the same thing as multiplying `Color.A` through the subtree, and the difference is
    /// exactly where the group's own shapes overlap: with per-shape alpha an overlap shows both
    /// shapes through each other and reads darker, while group opacity composites the subtree
    /// first and fades the result, so the overlap reads like the rest of the group. A single
    /// shape does not overlap itself, which is why `Color.A` stays the right tool for one shape
    /// and this node exists for a group.
    ///
    /// `Capabilities.GroupOpacity` guards it, and `Degrade.forCaps` folds it into paint alpha
    /// for a backend that cannot composite — the approximation above, applied deliberately and
    /// with its failure mode named, rather than a silently wrong picture.
    | Opacity of alpha: float * Node
    | Keyed of Key * Node
    | Annotated of Semantics * Node

/// What changed between two scenes, as replacements at the keys that hold them.
[<NoComparison>]
type Patch =
    /// Put each node under its key, in place of what is there now: the **child** of the
    /// `Keyed` node, as `Scene.mapKey` takes it. Empty when the two scenes draw the same thing.
    | Replace of (Key * Node)[]
    /// A change no key encloses, or one a key cannot address unambiguously. Only the whole
    /// new scene describes it.
    | Whole of Node

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

    /// Fade a whole subtree, clamping `alpha` into [0,1].
    ///
    /// Clamped here rather than at each backend: an opacity of 1.4 has no compositing meaning,
    /// and three backends left to their own devices would invent three different answers to it
    /// — SVG clamps, Skia truncates a byte, TikZ passes it to a driver. One answer, at the
    /// constructor, is the only way the IR stays the thing all three agree about.
    ///
    /// **NaN is refused rather than clamped**, because it fails both comparisons a clamp is made
    /// of and would otherwise reach the IR untouched. A translucent value has a nearest legal
    /// opacity; NaN has none, which is `Anim`'s reason for validating a duration rather than
    /// clamping one.
    let opacity (alpha: float) (n: Node) =
        if System.Double.IsNaN alpha then
            invalidArg "alpha" "Marey: an opacity of NaN has no nearest value in [0,1] to clamp to."

        Opacity((if alpha < 0.0 then 0.0
                 elif alpha > 1.0 then 1.0
                 else alpha),
                n)

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
        | Opacity(a, c) -> f (Opacity(a, transform f c))
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
        | Opacity(_, c)
        | Keyed(_, c)
        | Annotated(_, c) -> fold f state c
        | Path _
        | Text _
        | Markers _
        | Raster _ -> state

    /// The subtree a key names, or `None`.
    ///
    /// Returns the **child** of the matching `Keyed` node, not the node itself, so that it
    /// pairs with `mapKey`: what you can read is what you can replace, and neither can lose
    /// the key by accident.
    ///
    /// **The first match in pre-order**, and that is a statement about scenes rather than a
    /// hedge. Keys are meant to be unique and nothing enforces it — `Keyed(Key "panel", …)`
    /// twice is a legal scene, and the SVG backend deduplicates the resulting ids by suffixing
    /// rather than refusing. So the question genuinely has more than one answer sometimes, and
    /// taking the first is the answer a diff and an animation both want: the one that was
    /// drawn first.
    ///
    /// Groups are searched left to right and abandoned as soon as something is found, which is
    /// what keeps addressing a mark cheaper than walking the figure.
    let tryFindKey (key: Key) (n: Node) : Node option =
        let rec go (n: Node) =
            match n with
            | Keyed(k, c) when k = key -> Some c
            | Group ns ->
                let mutable found = None
                let mutable i = 0

                while found.IsNone && i < ns.Length do
                    found <- go ns.[i]
                    i <- i + 1

                found
            | Transform(_, c)
            | Clip(_, c)
            | Opacity(_, c)
            | Keyed(_, c)
            | Annotated(_, c) -> go c
            | Path _
            | Text _
            | Markers _
            | Raster _ -> None

        go n

    /// Rewrite the subtree a key names, leaving the rest of the scene alone.
    ///
    /// `f` is applied to the *child* of the matching `Keyed` node and the wrapper is put back,
    /// so a rewrite cannot drop the key it was addressed by. This is what an animation holds a
    /// key for: `mapKey k (Scene.opacity 0.3)` fades one layer of one panel and touches nothing
    /// else.
    ///
    /// **Every subtree that was not rewritten is returned by reference.** That is the property
    /// worth having and not an optimisation detail: it is what makes a per-frame rewrite cost
    /// the *path* to the key rather than the figure, and what lets a structural diff of two
    /// consecutive frames stop at the first shared reference. Only the containers along that
    /// path are rebuilt.
    ///
    /// **Every** node carrying the key is rewritten, where `tryFindKey` returns only the first.
    /// The asymmetry is the honest one: "find one of them" has to pick, and "rewrite the thing
    /// named X" should not leave a second thing named X behind. On the unique keys the producer
    /// emits the two agree, and on a duplicate they each do the useful thing.
    ///
    /// Recursion stops at a match, so a duplicate key *inside* a subtree that was just replaced
    /// is not visited: rewriting the inside of something already rewritten is not a coherent
    /// request — `f` was handed that subtree and can do as it likes with it.
    let mapKey (key: Key) (f: Node -> Node) (n: Node) : Node =
        let inline same (a: Node) (b: Node) = System.Object.ReferenceEquals(a, b)

        let rec go (n: Node) =
            match n with
            | Keyed(k, c) when k = key -> Keyed(k, f c)
            | Group ns ->
                let out = Array.map go ns
                if Array.forall2 same ns out then n else Group out
            | Transform(a, c) ->
                let c' = go c
                if same c c' then n else Transform(a, c')
            | Clip(p, c) ->
                let c' = go c
                if same c c' then n else Clip(p, c')
            | Opacity(a, c) ->
                let c' = go c
                if same c c' then n else Opacity(a, c')
            | Keyed(k, c) ->
                let c' = go c
                if same c c' then n else Keyed(k, c')
            | Annotated(s, c) ->
                let c' = go c
                if same c c' then n else Annotated(s, c')
            | leaf -> leaf

        go n

    /// **The diff.** Walk both scenes together, and answer with the smallest
    /// set of keyed subtrees whose replacement turns `before` into `after`.
    ///
    /// - A **reference-equal** pair is skipped without looking inside. That is what makes a
    ///   rewrite through `mapKey` cost the path to the key rather than the scene.
    /// - A `Keyed` pair with the same key is recursed into, and so is every container whose own
    ///   fields agree (`Group` of the same length, `Transform`, `Clip`, `Opacity`, `Annotated`).
    /// - Anything else is compared structurally, and a difference replaces the **nearest
    ///   enclosing key** whole. Replacements found deeper inside that key are subsumed by it.
    ///
    /// A difference with no key above it is `Whole`. So is a replacement whose key does not
    /// occur exactly once in each scene, because `mapKey` rewrites every node carrying a key
    /// and an id addresses one element.
    ///
    /// A scene rebuilt from data shares no references with the last one, so the walk falls back
    /// to structural equality: O(scene), float arrays compared element by element. Gate 9 of
    /// M4.5 states what that costs rather than assuming it.
    let diff (before: Node) (after: Node) : Patch =
        let same (a: Node) (b: Node) = System.Object.ReferenceEquals(a, b)

        // `(replacements, dirty)`: the keyed replacements found below, and whether a difference
        // was found that no key below encloses — which the nearest key above has to absorb.
        let rec walk (a: Node) (b: Node) : (Key * Node) list * bool =
            if same a b then
                [], false
            else
                match a, b with
                | Keyed(ka, ca), Keyed(kb, cb) when ka = kb ->
                    match walk ca cb with
                    | _, true -> [ kb, cb ], false
                    | reps, false -> reps, false
                | Group xs, Group ys when xs.Length = ys.Length ->
                    let mutable reps = []
                    let mutable dirty = false
                    let mutable i = 0

                    while not dirty && i < xs.Length do
                        let r, d = walk xs.[i] ys.[i]
                        reps <- r @ reps
                        dirty <- d
                        i <- i + 1

                    (if dirty then [] else reps), dirty
                | Transform(ta, ca), Transform(tb, cb) when ta = tb -> walk ca cb
                | Clip(pa, ca), Clip(pb, cb) when pa = pb -> walk ca cb
                | Opacity(oa, ca), Opacity(ob, cb) when oa = ob -> walk ca cb
                | Annotated(sa, ca), Annotated(sb, cb) when sa = sb -> walk ca cb
                | _ -> [], a <> b

        match walk before after with
        | _, true -> Whole after
        | reps, false ->
            let reps = List.rev reps |> Array.ofList

            let occurrences (k: Key) (n: Node) =
                fold
                    (fun acc node ->
                        match node with
                        | Keyed(k', _) when k' = k -> acc + 1
                        | _ -> acc)
                    0
                    n

            if reps |> Array.forall (fun (k, _) -> occurrences k before = 1 && occurrences k after = 1) then
                Replace reps
            else
                Whole after

    /// A patch applied to the scene it was taken against. `apply (diff a b) a` draws what `b`
    /// draws, and is `b` structurally.
    let apply (patch: Patch) (before: Node) : Node =
        match patch with
        | Whole n -> n
        | Replace reps -> reps |> Array.fold (fun n (k, child) -> mapKey k (fun _ -> child) n) before

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
        // **`Opacity(0.0, _)` is not pruned**, and the temptation to prune it is a trap an
        // animation walks straight into. A group fading in from zero would have its whole
        // subtree vanish at t = 0 and reappear at the next frame, so the node count would
        // change between two frames that differ only in a float — which is exactly the diff
        // instability §5.2 exists to prevent. `prune` drops what *cannot* draw; a transparent
        // subtree is one that is not drawing *yet*.
        | Opacity(a, c) ->
            match prune c with
            | c when c = empty -> empty
            | c -> Opacity(a, c)
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
        // Both forms must be empty before a run is dropped. Testing `Text` alone would delete
        // every TeX-only run — a run whose `Text` fallback was left blank still draws on a
        // typesetting backend, and pruning it here would make it vanish from that backend only,
        // which is the worst possible shape for the bug.
        | Text t when
            System.String.IsNullOrEmpty t.Text
            && t.Tex |> Option.forall System.String.IsNullOrEmpty
            ->
            empty
        | leaf -> leaf
