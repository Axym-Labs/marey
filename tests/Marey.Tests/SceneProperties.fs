/// The Scene IR.
///
/// The headline property here is **D1**: two independently constructed, identical scenes must
/// compare equal. That is not a nicety. §5.2 defines re-rendering as "build a new `Scene`, diff
/// against the previous one, emit only the changed subtrees", and §15 wants the whole figure
/// written to a file. The spike's `Transform of (Vec2<nd> -> Vec2<nd>) * Node` makes both
/// impossible in one stroke: a closure has reference equality, so one function field anywhere in
/// the tree makes every enclosing node unequal to an identical copy, the diff degenerates to
/// "everything changed", and `Keyed` becomes decoration.
///
/// `deepCopy` below rebuilds every array and every node in the tree, so the comparison really is
/// between two distinct object graphs and not between a value and itself.
module Marey.Tests.SceneProperties

open FsCheck.Xunit
open Xunit
open Marey
open Marey.Tests.Generators

// ======================================================================================
// A genuinely independent reconstruction of a scene.
//
// Every array is rebuilt and every union case reallocated, so nothing but the *values* is
// shared with the original. Structural equality between the two is then a real statement about
// the IR rather than an accident of aliasing.
// ======================================================================================

let private copyPaint (p: Paint) =
    match p with
    | Solid c -> Solid { R = c.R; G = c.G; B = c.B; A = c.A }
    | LinearGradient(a, b, stops) -> LinearGradient(a, b, Array.map (fun s -> { Offset = s.Offset; Color = s.Color }) stops)
    | RadialGradient(c, r, stops) -> RadialGradient(c, r, Array.map (fun s -> { Offset = s.Offset; Color = s.Color }) stops)

let private copyStroke (s: Stroke) =
    { Paint = copyPaint s.Paint
      Width = s.Width
      Dash = Array.map id s.Dash
      DashOffset = s.DashOffset
      Cap = s.Cap
      Join = s.Join
      MiterLimit = s.MiterLimit }

let rec deepCopy (n: Node) : Node =
    match n with
    | Path(segs, st, fill, rule) -> Path(Array.map id segs, Option.map copyStroke st, Option.map copyPaint fill, rule)
    | Text t -> Text { t with Paint = copyPaint t.Paint }
    | Markers b ->
        Markers
            { Points = Array.map id b.Points
              Shape = b.Shape
              Size = b.Size
              Fill = Option.map copyPaint b.Fill
              Stroke = Option.map copyStroke b.Stroke }
    | Raster r ->
        Raster
            { Image =
                { Width = r.Image.Width
                  Height = r.Image.Height
                  Rgba = Array.map id r.Image.Rgba }
              Bounds = r.Bounds
              Smooth = r.Smooth }
    | Group ns -> Group(Array.map deepCopy ns)
    | Transform(a, c) -> Transform({ a with M11 = a.M11 }, deepCopy c)
    | Clip(p, c) -> Clip(Array.map id p, deepCopy c)
    // No fresh instance for the alpha: a `float` has no reference identity to duplicate, so
    // there is nothing here for structural equality to be confused about.
    | Opacity(a, c) -> Opacity(a, deepCopy c)
    // A fresh string instance, so key equality is genuinely structural rather than interned.
    | Keyed(Key k, c) -> Keyed(Key(System.String(k.ToCharArray())), deepCopy c)
    | Annotated(s, c) ->
        Annotated(
            { Role = s.Role
              Label = s.Label
              Data = Array.map (fun (a, b) -> (a, b)) s.Data },
            deepCopy c
        )

/// Whether a text run draws anything at all.
///
/// Either form counts. Testing `Text` alone would declare a TeX-only run invisible, and since
/// `Scene.prune` would then delete it, the run would vanish from typesetting backends *only* —
/// the worst possible shape for the bug, and the reason `Scene.prune` tests both fields.
let private runDraws (t: TextRun) =
    not (System.String.IsNullOrEmpty t.Text)
    || t.Tex |> Option.exists (System.String.IsNullOrEmpty >> not)

/// The primitives a scene actually *draws*, as opposed to the nodes it contains. `prune` is
/// allowed to remove anything that draws nothing and nothing else, so this is the quantity its
/// correctness is stated against.
let private drawnCount (n: Node) =
    Scene.fold
        (fun acc node ->
            match node with
            | Path(segs, _, _, _) -> if segs.Length > 0 then acc + 1 else acc
            | Text t -> if runDraws t then acc + 1 else acc
            | Markers b -> acc + b.Points.Length
            | Raster _ -> acc + 1
            | _ -> acc)
        0
        n

/// An independent recursive definition of `Scene.count`, so the test is not the implementation.
let rec private countBySpec (n: Node) =
    1
    + match n with
      | Group ns -> Array.sumBy countBySpec ns
      | Transform(_, c)
      | Clip(_, c)
      | Opacity(_, c)
      | Keyed(_, c)
      | Annotated(_, c) -> countBySpec c
      | Path _
      | Text _
      | Markers _
      | Raster _ -> 0

let rec private primitivesBySpec (n: Node) =
    match n with
    | Markers b -> b.Points.Length
    | Path _
    | Text _
    | Raster _ -> 1
    | Group ns -> Array.sumBy primitivesBySpec ns
    | Transform(_, c)
    | Clip(_, c)
    | Opacity(_, c)
    | Keyed(_, c)
    | Annotated(_, c) -> primitivesBySpec c

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 400, Replay = Seed, QuietOnSuccess = true)>]
module StructuralEquality =

    /// **D1, the single most important structural property in the codebase.**
    [<Property>]
    let ``two independently constructed identical scenes are equal`` (SceneTree n) =
        let copy = deepCopy n
        copy = n && not (System.Object.ReferenceEquals(copy, n))

    [<Property>]
    let ``equal scenes hash equally`` (SceneTree n) =
        let copy = deepCopy n
        hash copy = hash n

    /// D1's other half, verified "both ways" exactly as decisions.md says the smoke suite does:
    /// changing one transform's translation must make the scenes *unequal*. With a
    /// closure-valued `Transform` neither direction would be observable.
    [<Fact>]
    let ``changing one transform's translation makes scenes unequal`` () =
        let build (dx: float<nd>) =
            Group
                [| Keyed(
                       Key "panel",
                       Transform(
                           { Affine.identity<nd> with Dx = dx },
                           Scene.polyline
                               (Stroke.solid Color.black 1.0<px>)
                               [| { X = 0.0<nd>; Y = 0.0<nd> }; { X = 1.0<nd>; Y = 1.0<nd> } |]
                       )
                   ) |]

        Assert.Equal(build 0.0<nd>, build 0.0<nd>)
        Assert.NotEqual(build 0.0<nd>, build 0.001<nd>)

    /// The same statement at the level that matters for §5.2: a scene rebuilt with one leaf
    /// changed differs from the original only in that subtree, so a structural diff can be
    /// O(changed). Checked by comparing sibling subtrees rather than by implementing a diff.
    [<Fact>]
    let ``an unchanged sibling subtree stays equal after a change elsewhere`` () =
        let leaf (label: string) =
            Annotated(
                { Role = RoleSeries
                  Label = label
                  Data = [| "id", label |] },
                Scene.polyline (Stroke.solid Color.black 1.0<px>) [| { X = 0.0<nd>; Y = 0.0<nd> } |]
            )

        let before = Group [| leaf "a"; leaf "b" |]
        let after = Group [| leaf "a"; leaf "b-changed" |]

        match before, after with
        | Group x, Group y ->
            Assert.Equal(x.[0], y.[0])
            Assert.NotEqual(x.[1], y.[1])
        | _ -> failwith "unreachable"

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 400, Replay = Seed, QuietOnSuccess = true)>]
module Counting =

    [<Property>]
    let ``count matches an independent recursive definition`` (SceneTree n) = Scene.count n = countBySpec n

    [<Property>]
    let ``primitiveCount matches an independent recursive definition`` (SceneTree n) =
        Scene.primitiveCount n = primitivesBySpec n

    [<Property>]
    let ``count is at least one and containers add to it`` (SceneTree n) =
        Scene.count n >= 1 && Scene.count (Group [| n; n |]) = 1 + 2 * Scene.count n

    /// §5.1's whole argument in one line: a 100 000-point scatter is **one** node, not 100 000.
    /// The gap between `count` and `primitiveCount` is exactly the win being bought.
    [<Fact>]
    let ``a batched scatter is one node and many primitives`` () =
        let pts =
            Array.init 100000 (fun i -> { X = float i * 1.0e-5<nd>; Y = 0.5<nd> })

        let scene =
            Markers
                { Points = pts
                  Shape = MarkCircle
                  Size = 3.0<px>
                  Fill = Some(Solid Color.black)
                  Stroke = None }

        Assert.Equal(1, Scene.count scene)
        Assert.Equal(100000, Scene.primitiveCount scene)

    [<Property>]
    let ``fold visits exactly count nodes`` (SceneTree n) =
        Scene.fold (fun acc _ -> acc + 1) 0 n = Scene.count n

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 400, Replay = Seed, QuietOnSuccess = true)>]
module Pruning =

    [<Property>]
    let ``prune is idempotent`` (SceneTree n) =
        let once = Scene.prune n
        Scene.prune once = once

    [<Property>]
    let ``prune never grows the tree`` (SceneTree n) =
        Scene.count (Scene.prune n) <= Scene.count n

    /// Prune removes exactly the subtrees that draw nothing — no more, no less. That is the
    /// property that makes it safe to run before a diff, which is what Scene.fs recommends.
    [<Property>]
    let ``prune preserves every drawn primitive`` (SceneTree n) =
        drawnCount (Scene.prune n) = drawnCount n

    [<Property>]
    let ``a pruned tree contains no empty containers`` (SceneTree n) =
        let pruned = Scene.prune n

        pruned = Scene.empty
        || Scene.fold
            (fun ok node ->
                ok
                && match node with
                   | Group ns -> ns.Length > 0
                   | Path(segs, _, _, _) -> segs.Length > 0
                   | Markers b -> b.Points.Length > 0
                   | Text t -> runDraws t
                   | _ -> true)
            true
            pruned

    [<Property>]
    let ``pruning an already-drawing scene changes nothing`` (SceneTree n) =
        let pruned = Scene.prune n
        drawnCount pruned = 0 || Scene.prune pruned = pruned

    [<Fact>]
    let ``prune collapses a tree of empties to Scene.empty`` () =
        let messy =
            Group
                [| Group [||]
                   Transform(Affine.identity<nd>, Group [||])
                   Clip([||], Group [| Group [||] |])
                   Keyed(Key "k", Path([||], None, None, NonZero))
                   Annotated(
                       { Role = RoleDecoration
                         Label = ""
                         Data = [||] },
                       Markers
                           { Points = [||]
                             Shape = MarkCircle
                             Size = 1.0<px>
                             Fill = None
                             Stroke = None }
                   ) |]

        Assert.Equal(Scene.empty, Scene.prune messy)

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 400, Replay = Seed, QuietOnSuccess = true)>]
module Rewriting =

    [<Property>]
    let ``transform with the identity is the identity`` (SceneTree n) = Scene.transform id n = n

    /// `Scene.transform` is documented as a **bottom-up** rewrite, which is what makes theming
    /// passes and clip hoisting composable. A top-down rewrite would see the un-rewritten
    /// children, so the distinction is observable: a rewrite that replaces every `Group` with
    /// its own child count must see already-rewritten children.
    [<Property>]
    let ``transform rewrites children before their parent`` (SceneTree n) =
        let order = ResizeArray<int>()

        Scene.transform
            (fun node ->
                order.Add(Scene.count node)
                node)
            n
        |> ignore

        // The parent is visited last, so its subtree size is the maximum and it comes at the end.
        order.Count = Scene.count n && order.[order.Count - 1] = Scene.count n

    [<Property>]
    let ``transform preserves node count when the rewrite is structure-preserving`` (SceneTree n) =
        let recoloured =
            Scene.transform
                (fun node ->
                    match node with
                    | Path(segs, st, _, rule) -> Path(segs, st, Some(Solid Color.white), rule)
                    | other -> other)
                n

        Scene.count recoloured = Scene.count n

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 300, Replay = Seed, QuietOnSuccess = true)>]
module Constructors =

    [<Property>]
    let ``polyline is a MoveTo followed by LineTos`` (Vec2Nd a) (Vec2Nd b) (Vec2Nd c) =
        match Scene.polyline (Stroke.solid Color.black 1.0<px>) [| a; b; c |] with
        | Path([| MoveTo p0; LineTo p1; LineTo p2 |], Some _, None, NonZero) -> p0 = a && p1 = b && p2 = c
        | _ -> false

    [<Property>]
    let ``polygon closes its path`` (Vec2Nd a) (Vec2Nd b) (Vec2Nd c) =
        match Scene.polygon None (Some(Solid Color.black)) [| a; b; c |] with
        | Path(segs, None, Some _, NonZero) -> segs.Length = 4 && segs.[3] = ClosePath
        | _ -> false

    [<Fact>]
    let ``empty point arrays produce Scene.empty`` () =
        Assert.Equal(Scene.empty, Scene.polyline (Stroke.solid Color.black 1.0<px>) [||])
        Assert.Equal(Scene.empty, Scene.polygon None None [||])

    [<Property>]
    let ``rect is a closed four-corner polygon`` (RectNd r) =
        match Scene.rect None (Some(Solid Color.black)) r with
        | Path(segs, _, _, _) -> segs.Length = 5 && segs.[0] = MoveTo { X = r.X0; Y = r.Y0 } && segs.[4] = ClosePath
        | _ -> false

/// "**The most important constraint in the project: this type is small, closed, total, and
/// backend-agnostic. Every addition costs you six backend implementations.**" and, at the bottom
/// of Scene.fs, "Nine cases. Guard it obsessively."
///
/// So: guard it. An eleventh case is a design decision, and it should have to break a test that
/// says so out loud rather than slipping in.
///
/// **The guard worked.** It was written against nine and D67 made it ten, which meant adding
/// `Opacity` could not be done without editing this line and defending the edit — which is the
/// entire behaviour asked for. The count is not the invariant; *having to say so* is.
module CaseCounts =

    open Microsoft.FSharp.Reflection

    [<Fact>]
    let ``Node has exactly ten cases`` () =
        let cases = FSharpType.GetUnionCases typeof<Node> |> Array.map (fun c -> c.Name)

        Assert.Equal<string[]>(
            [| "Path"; "Text"; "Markers"; "Raster"; "Group"; "Transform"; "Clip"; "Opacity"; "Keyed"; "Annotated" |],
            cases
        )

    /// The shape table each backend fills in once. Seven cases, and adding an eighth is a
    /// change every backend has to make.
    [<Fact>]
    let ``MarkerShape has exactly seven cases`` () =
        Assert.Equal(7, (FSharpType.GetUnionCases typeof<MarkerShape>).Length)

    [<Fact>]
    let ``PathSeg has exactly four cases`` () =
        Assert.Equal(4, (FSharpType.GetUnionCases typeof<PathSeg>).Length)

    /// D1 again, from the type side: `Transform` must carry an `Affine`, not a function. If this
    /// ever becomes a `FSharpFunc`, every structural-equality property above becomes vacuous.
    [<Fact>]
    let ``Transform carries an Affine, not a closure`` () =
        let case =
            FSharpType.GetUnionCases typeof<Node>
            |> Array.find (fun c -> c.Name = "Transform")

        let fields = case.GetFields()
        Assert.Equal(typeof<Affine<nd, nd>>, fields.[0].PropertyType)
        Assert.False(FSharpType.IsFunction fields.[0].PropertyType)

// ======================================================================================
// THE TENTH CASE
//
// `Opacity` is a container, so most of what it has to satisfy is already stated above as a
// property over generated trees: `count`, `primitiveCount`, structural equality and the
// generator all reach it because `Generators.nodeOfDepth` produces it. What is left is the
// three things that are specific to *this* container.
// ======================================================================================

module GroupOpacity =

    let private square (c: Color) (x0: float) (x1: float) =
        Scene.rect None (Some(Solid c)) { X0 = x0 * 1.0<nd>; Y0 = 0.0<nd>; X1 = x1 * 1.0<nd>; Y1 = 1.0<nd> }

    /// The constructor clamps and the case does not, which is deliberate: a `Node` is a value
    /// anyone may build, and refusing to represent 1.4 would mean `Opacity` could not round-trip
    /// through serialization without a validating parser. Clamping at the constructor is
    /// where the three backends stop having to each invent an answer.
    [<Fact>]
    let ``Scene.opacity clamps into zero to one`` () =
        let leaf = square Color.black 0.0 1.0

        match Scene.opacity 1.4 leaf, Scene.opacity -0.2 leaf, Scene.opacity 0.25 leaf with
        | Opacity(hi, _), Opacity(lo, _), Opacity(mid, _) ->
            Assert.Equal(1.0, hi)
            Assert.Equal(0.0, lo)
            Assert.Equal(0.25, mid)
        | _ -> failwith "Scene.opacity did not produce an Opacity node"

    /// NaN fails both comparisons a clamp is made of, so it used to reach the IR untouched and
    /// each backend invented its own answer — SVG wrote `opacity="NaN"`, Skia converted it to a
    /// byte. A translucent value has a nearest legal opacity; NaN has none. Reported by review on
    /// the 0.3.0 PR.
    [<Fact>]
    let ``Scene.opacity refuses NaN rather than passing it through`` () =
        let leaf = square Color.black 0.0 1.0
        let ex = Assert.Throws<System.ArgumentException>(fun () -> Scene.opacity nan leaf |> ignore)
        Assert.Contains("NaN", ex.Message)

    /// **The trap an animation walks into.** A group fading in from zero must keep its shape at
    /// t = 0, or the node count changes between two frames that differ only in a float and the
    /// §5.2 diff sees the whole subtree appear from nowhere. `prune` drops what *cannot* draw;
    /// a transparent subtree is one that is not drawing *yet*.
    [<Fact>]
    let ``prune keeps a fully transparent group and drops an empty one`` () =
        let visible = Scene.opacity 0.0 (square Color.black 0.0 1.0)
        Assert.Equal(visible, Scene.prune visible)

        Assert.Equal(Scene.empty, Scene.prune (Scene.opacity 0.5 (Group [||])))

    /// The degrade, and the shape of its error. `foldOpacity` multiplies the constant into every
    /// alpha in the subtree, so two nested opacities compose by multiplication and the result is
    /// indistinguishable from group opacity **wherever the shapes do not overlap** — which is
    /// what the node's own comment claims and what the TikZ baseline is.
    [<Fact>]
    let ``degrading a group opacity multiplies it into the subtree, and nests`` () =
        let caps = { Capabilities.full with GroupOpacity = false }

        let nested =
            Scene.opacity 0.4 (Group [| Scene.opacity 0.5 (square (Color.rgba 1.0 0.0 0.0 0.5) 0.0 1.0) |])

        let alphas =
            Degrade.forCaps caps nested
            |> Scene.fold
                (fun acc node ->
                    match node with
                    | Path(_, _, Some(Solid c), _) -> c.A :: acc
                    | _ -> acc)
                []

        // 0.5 on the fill, times 0.5 inside, times 0.4 outside.
        Assert.Equal<float list>([ 0.1 ], alphas)

        // And the node itself is gone: a backend that advertised `GroupOpacity = false` must
        // never be handed one, or `Degrade` has not done its job and the backend's own branch
        // is load-bearing when it was written to be defensive.
        Assert.False(
            Degrade.forCaps caps nested
            |> Scene.fold
                (fun acc node ->
                    match node with
                    | Opacity _ -> true
                    | _ -> acc)
                false
        )

    /// A backend that *can* composite is handed the node untouched. Stated separately because
    /// the interesting failure is a `Degrade` rule that fires unconditionally — which would make
    /// `GroupOpacity` a field nobody reads, the exact failure D10 recorded.
    [<Fact>]
    let ``a backend that can composite keeps the node`` () =
        let scene = Scene.opacity 0.4 (square Color.black 0.0 1.0)
        Assert.Equal(scene, Degrade.forCaps Capabilities.full scene)

    /// The raster half. A half-faded group whose heatmap does not fade is the visible kind of
    /// wrong, so the fold rewrites pixel alpha too — and leaves the colour channels alone,
    /// because the compositor multiplies them, not us.
    [<Fact>]
    let ``the fold scales raster alpha and nothing else`` () =
        let image =
            { Width = 1
              Height = 2
              Rgba = [| 200uy; 100uy; 50uy; 255uy; 10uy; 20uy; 30uy; 128uy |] }

        let scene =
            Scene.opacity
                0.5
                (Raster
                    { Image = image
                      Bounds = Rect.unit
                      Smooth = false })

        match Degrade.forCaps { Capabilities.full with GroupOpacity = false } scene with
        | Raster r ->
            Assert.Equal<byte[]>([| 200uy; 100uy; 50uy; 128uy; 10uy; 20uy; 30uy; 64uy |], r.Image.Rgba)
            // The source is not mutated. It is shared with whoever built the scene, and a
            // degrade that edited it in place would fade the caller's image as a side effect.
            Assert.Equal<byte[]>([| 200uy; 100uy; 50uy; 255uy; 10uy; 20uy; 30uy; 128uy |], image.Rgba)
        | other -> failwithf "expected a Raster, got %A" other

// ======================================================================================
// ADDRESSING A SUBTREE BY KEY
//
// `Keyed` was decoration until M3: the whole library produced one of them, at the root of every
// figure, so the diff the IR was designed around had a granularity of one. These two functions
// are what a key is *for*, and the properties below are the ones a differ and an animation both
// rely on.
// ======================================================================================

module Addressing =

    let private leaf name =
        Scene.polyline (Stroke.solid Color.black 1.0<px>) [| { X = 0.0<nd>; Y = 0.0<nd> }; { X = 1.0<nd>; Y = float name * 1.0<nd> } |]

    /// figure → panel → layer, the shape the producer emits.
    let private tree =
        Keyed(
            Key "figure",
            Group
                [| Keyed(Key "figure/panel-0-0", Group [| Keyed(Key "figure/panel-0-0/layer-0", leaf 1) |])
                   Keyed(Key "figure/panel-0-1", Group [| Keyed(Key "figure/panel-0-1/layer-0", leaf 2) |]) |]
        )

    [<Fact>]
    let ``tryFindKey returns the child of the node it names`` () =
        Assert.Equal(Some(leaf 2), Scene.tryFindKey (Key "figure/panel-0-1/layer-0") tree)
        Assert.Equal(None, Scene.tryFindKey (Key "figure/panel-9-9") tree)

        // A key nested under another key is reachable, and the root's child still holds all
        // four of them: two panels and a layer inside each. The hierarchy is the point.
        match Scene.tryFindKey (Key "figure") tree with
        | Some found ->
            Assert.Equal(
                4,
                Scene.fold
                    (fun acc n ->
                        match n with
                        | Keyed _ -> acc + 1
                        | _ -> acc)
                    0
                    found
            )
        | None -> failwith "the root key was not found"

    /// The wrapper survives, so a rewrite cannot silently drop the identity it was addressed by.
    [<Fact>]
    let ``mapKey rewrites the child and keeps the key`` () =
        let faded = Scene.mapKey (Key "figure/panel-0-1/layer-0") (Scene.opacity 0.5) tree

        match Scene.tryFindKey (Key "figure/panel-0-1/layer-0") faded with
        | Some(Opacity(a, c)) ->
            Assert.Equal(0.5, a)
            Assert.Equal(leaf 2, c)
        | other -> failwithf "expected a faded leaf under the same key, got %A" other

        // The other panel is untouched, which is the whole claim.
        Assert.Equal(Some(leaf 1), Scene.tryFindKey (Key "figure/panel-0-0/layer-0") faded)

    /// **The property that makes a per-frame rewrite affordable**, and the one that would go
    /// unnoticed if it regressed: everything off the path to the key comes back by reference,
    /// so the cost is the path and a structural diff of two frames stops at the first shared
    /// node. A `mapKey` written with a plain `Scene.transform` would pass every test above and
    /// fail this one.
    [<Fact>]
    let ``mapKey shares every subtree it did not rewrite`` () =
        let untouched = Scene.tryFindKey (Key "figure/panel-0-0") tree
        let after = Scene.mapKey (Key "figure/panel-0-1/layer-0") (Scene.opacity 0.5) tree
        let afterUntouched = Scene.tryFindKey (Key "figure/panel-0-0") after

        match untouched, afterUntouched with
        | Some a, Some b -> Assert.True(System.Object.ReferenceEquals(a, b), "the untouched panel was rebuilt")
        | _ -> failwith "the untouched panel went missing"

        // And a key that matches nothing returns the original tree itself, not a copy of it.
        Assert.True(
            System.Object.ReferenceEquals(tree, Scene.mapKey (Key "nothing") (Scene.opacity 0.5) tree),
            "a miss rebuilt the whole scene"
        )

    /// Keys are *meant* to be unique and nothing enforces it — the SVG backend deduplicates the
    /// ids it generates rather than refusing — so both functions have to say what they do about
    /// a duplicate rather than leave it to be discovered.
    ///
    /// They do different things, and that is the point of pinning it: `tryFindKey` has to pick
    /// one and picks the first, while `mapKey` rewrites both, because "fade the layer named X"
    /// leaving a second unfaded thing named X behind would be the surprising answer.
    [<Fact>]
    let ``tryFindKey takes the first duplicate and mapKey rewrites every one`` () =
        let dup = Group [| Keyed(Key "k", leaf 1); Keyed(Key "k", leaf 2) |]

        Assert.Equal(Some(leaf 1), Scene.tryFindKey (Key "k") dup)

        match Scene.mapKey (Key "k") (Scene.opacity 0.5) dup with
        | Group [| Keyed(_, first); Keyed(_, second) |] ->
            Assert.Equal(Scene.opacity 0.5 (leaf 1), first)
            Assert.Equal(Scene.opacity 0.5 (leaf 2), second)
        | other -> failwithf "expected two keyed siblings, got %A" other

    /// The other half of that sentence: a match is not descended into. `f` is handed the subtree
    /// once, and if it puts the same key back the function does not chase its own output.
    [<Fact>]
    let ``mapKey does not descend into a subtree it just rewrote`` () =
        let nested = Keyed(Key "k", Group [| Keyed(Key "k", leaf 1) |])

        match Scene.mapKey (Key "k") (Scene.opacity 0.5) nested with
        | Keyed(_, Opacity(_, Group [| Keyed(_, inner) |])) -> Assert.Equal(leaf 1, inner)
        | other -> failwithf "expected only the outer key rewritten, got %A" other
