/// D69 — **the storyboard CE**, and the two claims underneath it.
///
/// The first is about composition: what `let!` binds, what a step's duration defaults to, and
/// that `parallel'` inherits `Anim.map2`'s rule rather than restating it.
///
/// The second is the one §5.1 made structurally impossible. A scatter is *one* `Markers` node
/// holding N positions — "the single biggest performance decision in the IR" — so mark 42 has
/// no node to highlight. Addressing it splits that batch three ways, and the tests that matter
/// most here are the ones that say the split still draws the same picture, and that the mark it
/// names is the datum's **original row** rather than its position among the drawn marks. That
/// second one is D55's defect wearing new clothes, and `Geom.compileFor` is where it would
/// happen: a single non-finite point earlier in the series moves every later mark by one.
module Marey.Tests.StoryboardTests

open System
open Xunit
open Marey
open Marey.UnitsOfMeasure
open Marey.Storyboard
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// Fixtures
// ======================================================================================

let private surface = Chart.size 640.0 400.0

/// Ten seconds against ten metres, as a scatter — the geom whose marks are a batch.
let private points (f: float -> float) : Figure =
    let t = Array.init 10 (fun i -> float i * 1.0<s>)
    Chart.scatter U.second U.metre t (t |> Array.map (fun v -> f (float v) * 1.0<m>))

let private ramp = points id
let private squared = points (fun v -> v * v * 0.1)

/// The same shape as a **line**, which has no addressable mark: a line is one `Path`, and
/// splitting a polyline in three changes the joins between the pieces rather than naming one.
let private lineRamp: Figure =
    let t = Array.init 10 (fun i -> float i * 1.0<s>)
    Chart.line U.second U.metre t (t |> Array.map (fun v -> float v * 1.0<m>))

/// Every `MarkerBatch` in a scene, in draw order.
let private batches (n: Node) : MarkerBatch list =
    let rec go n =
        match n with
        | Markers b -> [ b ]
        | Group ns -> ns |> Array.toList |> List.collect go
        | Transform(_, c)
        | Clip(_, c)
        | Opacity(_, c)
        | Keyed(_, c)
        | Annotated(_, c) -> go c
        | _ -> []

    go n

/// The batches of the **first layer**, which is what every gate here is about. A scatter with
/// a colour channel also draws marker swatches in its legend, and counting those would make
/// "one batch until a mark is addressed" quietly false for a reason that has nothing to do
/// with D69.
let private layerBatches (n: Node) : MarkerBatch list =
    match Scene.tryFindKey (Keys.layer (Keys.panel 0 0) 0) n with
    | Some sub -> batches sub
    | None -> []

let private marks (n: Node) =
    layerBatches n |> List.collect (fun b -> List.ofArray b.Points)

/// The house rule for an endpoint, from `InterpProperties`: `a + (b - a) * t` does not land on
/// `b` exactly and never has, so the law is stated against the scale of the larger endpoint
/// rather than pretended away. `morphTo` inherits that, because it inherits the arithmetic.
let private approx (want: float[]) (got: float[]) =
    Assert.Equal(want.Length, got.Length)

    Array.iter2
        (fun (a: float) (b: float) -> Assert.True(abs (a - b) <= 1e-12 * max 1.0 (max (abs a) (abs b)), sprintf "%.17g <> %.17g" a b))
        want
        got

let private hasOpacity (n: Node) =
    n
    |> Scene.fold
        (fun acc x ->
            match x with
            | Opacity _ -> true
            | _ -> acc)
        false

/// The raw numbers a stage's first layer holds, in its own unit.
let private valuesOf (s: Stage) =
    match s.Figure with
    | Some f -> Erase.seriesValues f.Panels.[0].Layers.[0].Data
    | None -> failwith "nothing on stage"

let private domainOf (s: Stage) =
    match s.Figure with
    | Some f ->
        let sx, sy = Erase.seriesScales f.Panels.[0].Layers.[0].Data
        sx.Domain, sy.Domain
    | None -> failwith "nothing on stage"

/// Every affine in a scene — the panel's data node is `Transform(panelTransform, Clip …)`, so
/// this list *is* the plot rectangle, by the structural equality D1 bought.
let private transforms (n: Node) =
    n
    |> Scene.fold
        (fun acc x ->
            match x with
            | Transform(a, _) -> a :: acc
            | _ -> acc)
        []

let private layerZero = Keys.layer (Keys.panel 0 0) 0

// ======================================================================================
// The grammar — what the CE composes, and how long it takes
// ======================================================================================

module TheGrammar =

    [<Fact>]
    let ``a storyboard runs for the sum of its steps`` () =
        let story =
            scene {
                let! curve = draw ramp |> over 1.2<s>
                do! wait 0.5<s>
                do! curve |> morphTo squared |> over 2.0<s>
            }

        Assert.Equal(3.7, float (duration story), 9)

    [<Fact>]
    let ``parallel' takes the longest branch, not the sum`` () =
        let story =
            scene {
                let! curve = draw ramp |> over 1.0<s>

                do!
                    parallel'
                        [ curve |> morphTo squared |> over 2.0<s>
                          curve |> zoomToX (0.0, 5.0) |> over 0.5<s> ]
            }

        Assert.Equal(3.0, float (duration story), 9)

    /// D69's reason for the handle carrying a duration at all. Without it every verb needs a
    /// constant of its own, chosen in the library rather than in the storyboard.
    [<Fact>]
    let ``a step with no over runs for as long as its handle took to arrive`` () =
        let story =
            scene {
                let! curve = draw ramp |> over 1.2<s>
                do! highlight curve.[4]
            }

        Assert.Equal(2.4, float (duration story), 9)

    [<Fact>]
    let ``the stage a step ends on is where the next one starts`` () =
        let story =
            scene {
                let! curve = draw ramp |> over 1.0<s>
                do! curve |> morphTo squared |> over 1.0<s>
                // Morphing *back* only lands on `ramp` if this step started from `squared`.
                do! curve |> morphTo ramp |> over 1.0<s>
            }

        let st = stages story
        let _, mid = valuesOf (st.At 2.0<s>)
        let _, final = valuesOf (st.At 3.0<s>)
        let _, want = Erase.seriesValues squared.Panels.[0].Layers.[0].Data
        let _, start = Erase.seriesValues ramp.Panels.[0].Layers.[0].Data

        approx want mid
        approx start final

    [<Fact>]
    let ``a wait holds the stage`` () =
        let story =
            scene {
                let! _ = draw ramp
                do! wait 2.0<s>
            }

        let anim = animate 4 surface story
        Assert.Equal<Node>(anim.At 0.0<s>, anim.At 2.0<s>)

    /// D70's clamp, arriving where a storyboard needs it: `map2` reaches its components through
    /// the *seek*, so a branch asked for a time past its end gives its last frame.
    [<Fact>]
    let ``a short parallel branch holds its final value through the tail`` () =
        let story =
            scene {
                let! curve = draw ramp |> over 0.5<s>

                do!
                    parallel'
                        [ curve |> morphTo squared |> over 0.5<s>
                          curve |> zoomToY (0.0, 100.0) |> over 2.0<s> ]
            }

        let st = stages story
        let _, atEndOfMorph = valuesOf (st.At 1.0<s>)
        let _, atEnd = valuesOf (st.At 2.5<s>)

        approx atEndOfMorph atEnd

    [<Fact>]
    let ``parallel' composes its branches rather than overwriting`` () =
        let story =
            scene {
                let! curve = draw ramp |> over 0.5<s>

                do!
                    parallel'
                        [ curve |> morphTo squared |> over 1.0<s>
                          curve |> zoomToX (-4.0, 4.0) |> over 1.0<s> ]
            }

        let st = stages story
        let (xLo, xHi), _ = domainOf (st.At 1.5<s>)
        let _, ys = valuesOf (st.At 1.5<s>)
        let _, want = Erase.seriesValues squared.Panels.[0].Layers.[0].Data

        // The zoom arrived *and* the morph arrived.
        Assert.Equal(-4.0, xLo, 9)
        Assert.Equal(4.0, xHi, 9)
        approx want ys

// ======================================================================================
// The batch split — D69's headline, and §5.1's cost of it
// ======================================================================================

module TheBatchSplit =

    [<Fact>]
    let ``a scatter is one Markers node until a mark is addressed`` () =
        Assert.Single(layerBatches (Chart.toScene surface ramp)) |> ignore

    [<Fact>]
    let ``addressing a mark splits the batch three ways`` () =
        let scene = Chart.toScene surface (Chart.addressMarks [| 4 |] ramp)

        Assert.Equal(3, (layerBatches scene).Length)
        Assert.True((Scene.tryFindKey (Keys.mark layerZero 4) scene).IsSome)

    /// The cost is two extra nodes, not N — and not a different picture.
    [<Fact>]
    let ``the split draws the same marks, in the same order`` () =
        let plain = marks (Chart.toScene surface ramp)
        let split = marks (Chart.toScene surface (Chart.addressMarks [| 4 |] ramp))

        Assert.Equal<Vec2<nd> list>(plain, split)

    [<Fact>]
    let ``the addressed mark is the one that was named`` () =
        let scene = Chart.toScene surface (Chart.addressMarks [| 4 |] ramp)
        let plain = marks (Chart.toScene surface ramp)

        match Scene.tryFindKey (Keys.mark layerZero 4) scene with
        | Some(Markers b) ->
            Assert.Single b.Points |> ignore
            Assert.Equal(plain.[4], b.Points.[0])
        | other -> failwithf "expected one addressed mark, got %A" other

    /// **D55's defect, in the one place M3 could reintroduce it.** A non-finite point is not
    /// drawn, so every later mark sits one place earlier in the batch than its row. A key built
    /// from the position within the batch names the wrong datum from that point on — and the
    /// figure still looks entirely reasonable, which is what makes it worth a gate.
    [<Fact>]
    let ``a mark's key is its original row, not its position among the drawn marks`` () =
        let t = Array.init 10 (fun i -> float i * 1.0<s>)

        let ys =
            t |> Array.mapi (fun i v -> if i = 2 then nan * 1.0<m> else float v * 1.0<m>)

        let gappy = Chart.scatter U.second U.metre t ys
        let plain = marks (Chart.toScene surface gappy)
        let scene = Chart.toScene surface (Chart.addressMarks [| 5 |] gappy)

        // Nine marks drawn, and row 5 is the fifth of them.
        Assert.Equal(9, plain.Length)

        match Scene.tryFindKey (Keys.mark layerZero 5) scene with
        | Some(Markers b) ->
            Assert.Equal(plain.[4], b.Points.[0])
            Assert.NotEqual(plain.[5], b.Points.[0])
        | other -> failwithf "expected one addressed mark, got %A" other

    [<Fact>]
    let ``addressing the last mark costs two nodes, not three`` () =
        let scene = Chart.toScene surface (Chart.addressMarks [| 9 |] ramp)
        Assert.Equal(2, (layerBatches scene).Length)

    [<Fact>]
    let ``addressing every mark is one node each`` () =
        let scene = Chart.toScene surface (Chart.addressMarks [| 0 .. 9 |] ramp)
        let bs = layerBatches scene

        Assert.Equal(10, bs.Length)
        Assert.All(bs, fun b -> Assert.Single b.Points |> ignore)

    /// A line is one `Path`. The rows are carried and never asked for, which is the honest
    /// answer: there is no node between the vertices to name.
    [<Fact>]
    let ``a line has no mark to address, and says so by not moving`` () =
        let plain = Chart.toScene surface lineRamp
        let asked = Chart.toScene surface (Chart.addressMarks [| 4 |] lineRamp)

        Assert.Equal<Node>(plain, asked)

    [<Fact>]
    let ``a row that was never drawn changes nothing`` () =
        let plain = Chart.toScene surface ramp
        let asked = Chart.toScene surface (Chart.addressMarks [| 99 |] ramp)

        Assert.Equal<Node>(plain, asked)

    /// D46's channel batches are renumbered subsets, so the split has to happen *inside* the
    /// batch that holds the row — and only that one.
    [<Fact>]
    let ``only the channel batch holding the row is split`` () =
        let keys = Array.init 10 (fun i -> if i < 5 then "a" else "b")

        let coloured = ramp |> Chart.colorByKey keys
        let plain = layerBatches (Chart.toScene surface coloured)
        let split = layerBatches (Chart.toScene surface (Chart.addressMarks [| 7 |] coloured))

        Assert.Equal(2, plain.Length)
        // The untouched batch, plus the three the addressed one became.
        Assert.Equal(4, split.Length)
        Assert.Equal<Vec2<nd> list>(
            plain |> List.collect (fun b -> List.ofArray b.Points),
            split |> List.collect (fun b -> List.ofArray b.Points)
        )

// ======================================================================================
// The verbs
// ======================================================================================

module TheVerbs =

    [<Fact>]
    let ``draw reveals over its duration, and stops`` () =
        let story = scene { let! _ = draw ramp |> over 1.0<s> in () }
        let anim = animate 4 surface story

        Assert.True(hasOpacity (anim.At 0.5<s>))
        Assert.False(hasOpacity (anim.At 1.0<s>))

    [<Fact>]
    let ``a zero-duration draw is a cut`` () =
        let story = scene { let! _ = draw ramp in () }
        let anim = animate 4 surface story

        Assert.False(hasOpacity (anim.At 0.0<s>))
        Assert.Equal(0.0, float anim.Duration, 9)

    [<Fact>]
    let ``highlight grows exactly one mark`` () =
        let story =
            scene {
                let! curve = draw ramp
                do! highlight curve.[4] |> over 1.0<s>
            }

        let anim = animate 4 surface story
        let sizes = layerBatches (anim.At 1.0<s>) |> List.map (fun b -> float b.Size)

        Assert.Equal(3, sizes.Length)
        Assert.Equal(sizes.[0], sizes.[2], 9)
        Assert.True(sizes.[1] > sizes.[0], "the addressed mark should be the big one")

    /// The first frame of a highlight is the picture that was already there. Without it a
    /// highlight would pop on at the instant it started, which is the failure a ramp is for.
    [<Fact>]
    let ``at the instant a highlight starts it changes nothing but the node count`` () =
        let story =
            scene {
                let! curve = draw ramp
                do! highlight curve.[4] |> over 1.0<s>
            }

        let anim = animate 4 surface story
        let at0 = anim.At 0.0<s>
        let plain = List.exactlyOne (layerBatches (Chart.toScene surface ramp))

        Assert.Equal<Vec2<nd> list>(marks (Chart.toScene surface ramp), marks at0)

        // Every field, not only the size. A ramp that started at a zero-width stroke rather than
        // at no stroke would leave the sizes alone and still change the picture — which is the
        // mutation this assertion exists for.
        Assert.All(
            layerBatches at0,
            fun b ->
                Assert.Equal(plain.Size, b.Size)
                Assert.Equal(plain.Shape, b.Shape)
                Assert.Equal(plain.Fill, b.Fill)
                Assert.Equal(plain.Stroke, b.Stroke)
        )

    [<Fact>]
    let ``the highlight stays after its step ends`` () =
        let story =
            scene {
                let! curve = draw ramp
                do! highlight curve.[4] |> over 1.0<s>
                do! wait 1.0<s>
            }

        let anim = animate 4 surface story
        let sizes t = layerBatches (anim.At t) |> List.map (fun b -> float b.Size)

        Assert.Equal<float list>(sizes 1.0<s>, sizes 2.0<s>)

    [<Fact>]
    let ``highlight names one mark, not a subtree`` () =
        let story =
            scene {
                let! curve = draw ramp
                do! highlight curve |> over 1.0<s>
            }

        Assert.Throws<InvalidOperationException>(fun () -> duration story |> ignore) |> ignore

    [<Fact>]
    let ``a figure with two scatters has two mark 4s, and refuses to guess`` () =
        let t = Array.init 10 (fun i -> float i * 1.0<s>)
        let two = ramp |> Chart.addScatter U.second U.metre t (t |> Array.map (fun v -> float v * 2.0<m>))

        let story =
            scene {
                let! curve = draw two
                do! highlight curve.[4] |> over 1.0<s>
            }

        Assert.Throws<InvalidOperationException>(fun () -> duration story |> ignore) |> ignore

    /// A line is one `Path`, so there is no mark to name and the handle says so at the point
    /// the storyboard is written rather than by drawing nothing later.
    [<Fact>]
    let ``a line's marks cannot be addressed from a storyboard`` () =
        let story =
            scene {
                let! curve = draw lineRamp
                do! highlight curve.[4] |> over 1.0<s>
            }

        Assert.Throws<InvalidOperationException>(fun () -> duration story |> ignore) |> ignore

    [<Fact>]
    let ``morphTo lands on the target's data`` () =
        let story =
            scene {
                let! curve = draw ramp
                do! curve |> morphTo squared |> over 1.0<s>
            }

        let _, got = valuesOf ((stages story).At 1.0<s>)
        let _, want = Erase.seriesValues squared.Panels.[0].Layers.[0].Data

        approx want got

    /// The target's numbers are converted through SI, not read across. A morph into a series
    /// written in kilometres is a morph, not a factor of a thousand.
    [<Fact>]
    let ``morphTo converts the target into the unit on stage`` () =
        let km = U.scaled "km" 1000.0 U.metre
        let t = Array.init 10 (fun i -> float i * 1.0<s>)
        let inKm = Chart.scatter U.second km t (t |> Array.map (fun v -> float v * 1.0<m>))

        let story =
            scene {
                let! curve = draw ramp
                do! curve |> morphTo inKm |> over 1.0<s>
            }

        let _, got = valuesOf ((stages story).At 1.0<s>)

        approx (Array.init 10 (fun i -> float i * 1000.0)) got

    [<Fact>]
    let ``morphTo resamples to the longer side`` () =
        let t = Array.init 25 (fun i -> float i * 0.4<s>)
        let long = Chart.scatter U.second U.metre t (t |> Array.map (fun v -> float v * 1.0<m>))

        let story =
            scene {
                let! curve = draw ramp
                do! curve |> morphTo long |> over 1.0<s>
            }

        let _, got = valuesOf ((stages story).At 0.5<s>)
        Assert.Equal(25, got.Length)

    [<Fact>]
    let ``morphTo across dimensions is refused`` () =
        let t = Array.init 10 (fun i -> float i * 1.0<s>)
        let seconds = Chart.scatter U.second U.second t t

        let story =
            scene {
                let! curve = draw ramp
                do! curve |> morphTo seconds |> over 1.0<s>
            }

        // Refused when the storyboard is *built*, not when it is rendered: `bind` evaluates
        // each act at its own end to find the stage the next step starts from, so an
        // unplayable step is found before a single frame is asked for.
        Assert.Throws<ArgumentException>(fun () -> duration story |> ignore) |> ignore

    /// §7.2's `|> ease Ease.cubicInOut`, which is `Anim.ease` with nothing added. Asserted
    /// against the *unmutated* clock rather than against a hand-computed number: an eased step
    /// halfway through is the plain step at `f 0.5`.
    [<Fact>]
    let ``ease reparameterises the step's clock`` () =
        let build (f: (float -> float) option) =
            scene {
                let! curve = draw ramp

                do!
                    match f with
                    | Some g -> curve |> morphTo squared |> over 1.0<s> |> ease g
                    | None -> curve |> morphTo squared |> over 1.0<s>
            }

        let _, eased = valuesOf ((stages (build (Some Ease.quadIn))).At 0.5<s>)
        let _, plain = valuesOf ((stages (build None)).At 0.25<s>)

        approx plain eased

    /// D71 reaching all the way through: `ease` does not clamp its result, so an easing that
    /// leaves `[0, 1]` extrapolates the morph underneath it. A library where `Ease.backOut`
    /// compiles, runs and does nothing is the failure this pins.
    [<Fact>]
    let ``an overshooting easing overshoots the morph`` () =
        let story =
            scene {
                let! curve = draw ramp
                do! curve |> morphTo squared |> over 1.0<s> |> ease Ease.backOut
            }

        let st = stages story
        let _, from = Erase.seriesValues ramp.Panels.[0].Layers.[0].Data
        let _, target = Erase.seriesValues squared.Panels.[0].Layers.[0].Data

        // A morph on a linear clock is a convex combination, so every value stays between its
        // two endpoints. Overshoot is exactly leaving that interval — stated that way rather
        // than as "greater than the target", which is true of the first half of any morph that
        // happens to run downhill.
        let outside =
            [| 0.1 .. 0.05 .. 0.95 |]
            |> Array.exists (fun u ->
                let _, ys = valuesOf (st.At(u * 1.0<s>))

                Array.mapi (fun i (y: float) -> y, min from.[i] target.[i], max from.[i] target.[i]) ys
                |> Array.exists (fun (y, lo, hi) -> y < lo - 1e-9 || y > hi + 1e-9))

        Assert.True(outside, "backOut should carry the morph outside its own endpoints and settle back")

    [<Fact>]
    let ``zoomToX arrives at the domain it named`` () =
        let story =
            scene {
                let! curve = draw ramp
                do! curve |> zoomToX (2.0, 4.0) |> over 1.0<s>
            }

        let st = stages story
        let (lo0, _), _ = domainOf (st.At 0.0<s>)
        let (lo1, hi1), _ = domainOf (st.At 1.0<s>)

        Assert.Equal(2.0, lo1, 9)
        Assert.Equal(4.0, hi1, 9)
        Assert.NotEqual(lo0, lo1)

    [<Fact>]
    let ``a verb before anything is drawn is refused`` () =
        let story = scene { do! wait 1.0<s> }
        let handle: Handle =
            { Key = Keys.figure
              Introduced = 1.0<s>
              MarkLayer = None
              Mark = None
              MarkKeys = [||] }

        let bad =
            scene {
                do! story
                do! handle |> morphTo ramp |> over 1.0<s>
            }

        Assert.Throws<InvalidOperationException>(fun () -> duration bad |> ignore) |> ignore

// ======================================================================================
// The scope test the whole file rests on
// ======================================================================================

module Scoping =

    /// A key is a path, so "covers" is a prefix test on **segments**. On characters it is one
    /// grid position away from being wrong: `figure/panel-0-1` is not an ancestor of
    /// `figure/panel-0-10`, and a facet of a year of months has both.
    [<Fact>]
    let ``a panel key does not cover its double-digit neighbour`` () =
        Assert.True(Keys.covers (Keys.panel 0 1) (Keys.layer (Keys.panel 0 1) 2))
        Assert.True(Keys.covers Keys.figure (Keys.panel 0 10))
        Assert.True(Keys.covers (Keys.panel 0 1) (Keys.panel 0 1))
        Assert.False(Keys.covers (Keys.panel 0 1) (Keys.panel 0 10))

// ======================================================================================
// The shot — D72, reached through the CE
// ======================================================================================

module TheShot =

    /// A morph that gains a digit on every y label is exactly the fixture D72 was built
    /// against, so the storyboard has to inherit the answer rather than lay out per frame.
    let private small = points (fun v -> v)
    let private large = points (fun v -> v * 12.0)

    let private story =
        scene {
            let! curve = draw small
            do! curve |> morphTo large |> over 1.0<s>
        }

    let private frames = [| 0.0; 0.25; 0.5; 0.75; 1.0 |]

    [<Fact>]
    let ``the plot rectangle is identical across every frame of a storyboard`` () =
        let anim = animate 16 surface story

        let seen =
            frames
            |> Array.map (fun u -> transforms (anim.At (u * anim.Duration)))
            |> Array.distinct

        Assert.Single seen |> ignore

    /// The positive control: the same frames, laid out one at a time, do move. Without it the
    /// gate above passes just as well on a fixture that is not changing.
    [<Fact>]
    let ``the same frames laid out per frame do move`` () =
        let st = stages story

        let seen =
            frames
            |> Array.map (fun u ->
                match (st.At (u * st.Duration)).Figure with
                | Some f -> transforms (Chart.toScene surface f)
                | None -> [])
            |> Array.distinct

        Assert.True(seen.Length > 1, "the fixture is not moving; the frozen gate would be vacuous")

    [<Fact>]
    let ``an empty stage still has a surface`` () =
        let waited = scene { do! wait 1.0<s> }
        let anim = animate 4 surface waited

        Assert.NotEqual<Node>(Scene.empty, anim.At 0.0<s>)
