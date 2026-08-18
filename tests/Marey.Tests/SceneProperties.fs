/// §5 / §5.2 / D1 — the Scene IR.
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

/// §5 — "**The most important constraint in the project: this type is small, closed, total, and
/// backend-agnostic. Every addition costs you six backend implementations.**" and, at the bottom
/// of Scene.fs, "Nine cases. Guard it obsessively."
///
/// So: guard it. A tenth case is a design decision, and it should have to break a test that says
/// so out loud rather than slipping in.
module CaseCounts =

    open Microsoft.FSharp.Reflection

    [<Fact>]
    let ``Node has exactly nine cases`` () =
        let cases = FSharpType.GetUnionCases typeof<Node> |> Array.map (fun c -> c.Name)
        Assert.Equal<string[]>([| "Path"; "Text"; "Markers"; "Raster"; "Group"; "Transform"; "Clip"; "Keyed"; "Annotated" |], cases)

    /// D5 — the shape table each backend fills in once. Seven cases, and adding an eighth is a
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
