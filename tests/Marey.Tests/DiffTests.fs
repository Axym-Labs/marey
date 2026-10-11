module Marey.Tests.DiffTests

// M4.5 phase 5 — the differ (D103). `Scene.diff` on the IR, `SvgBackend.renderPatch` on the
// document, and the reply a live figure sends. Gates 8 and 9 of `m4.5-plan.md` §4.

open System
open System.Text.RegularExpressions
open Xunit
open Marey
open Marey.UnitsOfMeasure

let private surface = Chart.size 480.0 300.0
let private measurer = TextMeasure.approx
let private sceneOf (fig: Figure) = Chart.toSceneIn measurer (Chart.arrangeOver measurer surface [| fig |]) fig

let private t = Array.init 40 (fun i -> float i * 0.05<s>)
let private x (k: float) = t |> Array.map (fun t -> k * float t * float t * 1.0<m>)

/// A line and a scatter over the same times: two layers, one panel, one arrangement.
let private twoLayers (k: float) =
    Chart.line U.second U.metre t (x 1.0) |> Chart.addScatter U.second U.metre t (x k)

/// The same two layers drawn in one arrangement, so a change to one cannot move the other.
let private scenes (a: Figure) (b: Figure) =
    let shot = Chart.arrangeOver measurer surface [| a; b |]
    Chart.toSceneIn measurer shot a, Chart.toSceneIn measurer shot b

let private keysOf (p: Patch) =
    match p with
    | Replace reps -> reps |> Array.map (fun (Key k, _) -> k)
    | Whole _ -> [| "<whole>" |]

// ======================================================================================
// The diff on the IR
// ======================================================================================

module ``the scene diff`` =

    [<Fact>]
    let ``a scene against itself is no change`` () =
        let s = sceneOf (twoLayers 1.0)
        Assert.Empty(keysOf (Scene.diff s s))

    /// A rewrite through `mapKey` shares everything but the path to the key, so the walk stops at
    /// the first shared reference and names the key it rewrote.
    [<Fact>]
    let ``a rewrite at a key is a replacement at that key`` () =
        let s = sceneOf (twoLayers 1.0)
        let k = Keys.layer (Keys.panel 0 0) 1
        let s' = Scene.mapKey k (Scene.opacity 0.3) s
        let p = Scene.diff s s'
        Assert.Equal<string[]>([| "figure/panel-0-0/layer-1" |], keysOf p)
        Assert.True(Scene.apply p s = s')

    /// A figure rebuilt from data shares no references; the walk falls back to structure and
    /// still names only the layer whose data moved.
    [<Fact>]
    let ``a rebuilt figure with one layer moved replaces that layer`` () =
        let a, b = scenes (twoLayers 1.0) (twoLayers 0.9)
        let p = Scene.diff a b
        Assert.Equal<string[]>([| "figure/panel-0-0/layer-1" |], keysOf p)
        Assert.True(Scene.apply p a = b)

    /// A change no key below the figure holds, such as its title, is absorbed by the figure's key.
    [<Fact>]
    let ``a change outside the panels replaces the figure`` () =
        let a, b = scenes (twoLayers 1.0 |> Chart.withTitle "one") (twoLayers 1.0 |> Chart.withTitle "two")
        Assert.Equal<string[]>([| "figure" |], keysOf (Scene.diff a b))

    /// No key above a difference, or a key that does not name one subtree, is the whole scene.
    [<Fact>]
    let ``what no key addresses is the whole scene`` () =
        let leaf (c: Color) = Scene.rect None (Some(Solid c)) Rect.unit
        Assert.Equal<string[]>([| "<whole>" |], keysOf (Scene.diff (leaf Color.black) (leaf Color.white)))

        let twice (c: Color) = Group [| Keyed(Key "a", leaf c); Keyed(Key "a", leaf Color.black) |]
        Assert.Equal<string[]>([| "<whole>" |], keysOf (Scene.diff (twice Color.black) (twice Color.white)))

// ======================================================================================
// Gate 8 — a patch is the new scene
// ======================================================================================

/// What the view does with a patch, done to the text: the `<defs>` block replaced whole, and
/// each element swapped for the markup with its id. The script does it to a DOM; this does it
/// to the document the DOM was parsed from, so the result can be held to `render after` byte
/// for byte.
let applyText (doc: string) (p: SvgPatch) : string =
    let headerEnd = doc.IndexOf('\n') + 1
    let defsStart = headerEnd
    let hasDefs = doc.Substring(headerEnd).StartsWith "  <defs>\n"
    let defsEnd = if hasDefs then doc.IndexOf("  </defs>\n", headerEnd) + "  </defs>\n".Length else headerEnd
    let mutable out = doc.Substring(0, defsStart) + p.Defs + doc.Substring(defsEnd)

    for id, markup in defaultArg p.Elements [||] do
        let tag = sprintf "<g id=\"%s\">" id
        let at = out.IndexOf tag
        Assert.True(at >= 0, sprintf "no element %s to replace" id)
        let lineStart = out.LastIndexOf('\n', at) + 1
        // The matching close: count `<g` openings and `</g>` closings from the element on.
        let rx = Regex("<g[ >]|</g>")
        let mutable depth = 0
        let mutable stop = -1
        let mutable m = rx.Match(out, at)

        while stop < 0 && m.Success do
            depth <- depth + (if m.Value = "</g>" then -1 else 1)
            if depth = 0 then stop <- m.Index + m.Length + 1
            m <- m.NextMatch()

        out <- out.Substring(0, lineStart) + markup + out.Substring(stop)

    out

module ``a patch is the new scene`` =

    let private gate (a: Node) (b: Node) =
        let p = SvgBackend.renderPatch surface a b
        Assert.Equal(SvgBackend.render surface b, p.Document)
        Assert.True(p.Elements.IsSome, "no patch was taken")
        Assert.Equal(SvgBackend.render surface b, applyText (SvgBackend.render surface a) p)
        p

    /// **Gate 8.** One layer moved: the patch is that layer, and applying it to the old document
    /// gives the new one byte for byte.
    [<Fact>]
    let ``a moved layer patches to the new document`` () =
        let a, b = scenes (twoLayers 1.0) (twoLayers 0.9)
        let p = gate a b
        Assert.Equal<string[]>([| "figure_panel-0-0_layer-1" |], p.Elements.Value |> Array.map fst)

    /// A change that adds a marker definition: the old document has no `qm`, and the patch's
    /// fresh `<defs>` is what brings it.
    [<Fact>]
    let ``a patch that adds a marker carries its definition`` () =
        let lines = Chart.line U.second U.metre t (x 1.0) |> Chart.addLine U.second U.metre t (x 0.9)
        let a, b = scenes lines (twoLayers 0.9)
        let before = SvgBackend.render surface a
        let p = gate a b
        Assert.DoesNotContain("id=\"qm\"", before)
        Assert.Contains("id=\"qm\"", p.Defs)

    /// The numbering trap. Two scatters share one marker (`qm`) until the first grows; then the
    /// first is `qm` and the second, which did not change, is `qm-2`. The IR names one layer and
    /// the text disagrees, so no patch is taken and the whole document goes.
    [<Fact>]
    let ``a def renumbered under an unchanged layer sends the whole document`` () =
        let two (size: float<px>) =
            Chart.scatter U.second U.metre t (x 1.0)
            |> Chart.withStyle (Geom.withMarkerSize size)
            |> Chart.withMarker MarkCircle
            |> Chart.addScatter U.second U.metre t (x 0.9)
            |> Chart.withStyle (Geom.withMarkerSize 5.0<px>)
            |> Chart.withMarker MarkCircle

        let a, b = scenes (two 5.0<px>) (two 8.0<px>)
        Assert.DoesNotContain("#qm-2", SvgBackend.render surface a)
        Assert.Equal<string[]>([| "figure/panel-0-0/layer-0" |], keysOf (Scene.diff a b))
        Assert.Contains("#qm-2", SvgBackend.render surface b)
        let p = SvgBackend.renderPatch surface a b
        Assert.True(p.Elements.IsNone, "a patch was taken against renumbered defs")
        Assert.Equal(SvgBackend.render surface b, p.Document)

// ======================================================================================
// Gate 9 — diffing is measured, not assumed
// ======================================================================================

[<Xunit.Collection(RenderModes.Collection)>]
module ``diffing is measured`` =

    /// 50 000 scattered points that no control moves, and one marked point a slider moves along
    /// the curve: the case diffing exists for.
    let scatterWithCursor () =
        let n = 50_000
        let xs = Array.init n (fun i -> float i / float n * 10.0<s>)
        let ys = xs |> Array.mapi (fun i t -> (sin (float t) + 0.3 * sin (float i * 12.9898)) * 1.0<m>)

        interact {
            let! at = Control.slider U.second "t" 1.0<s> 9.0<s> 5.0<s>
            return
                Chart.scatter U.second U.metre xs ys
                |> Chart.addScatter U.second U.metre [| at |] [| sin (float at) * 1.0<m> |]
                |> Chart.withStyle (Geom.withMarkerSize 10.0<px>)
        }

    /// **Gate 9.** On the stated figure the patch is under 5% of the document, and the reply a
    /// live figure sends is that patch.
    [<Fact>]
    let ``a cursor over 50 000 points patches in under 5% of the document`` () =
        RenderModes.withRenderMode RenderMode.Svg (fun () ->
            let l = scatterWithCursor ()
            let s0 = Live.init l
            let s1 = Live.step l s0 (Set(0, 6.0))
            let p = Html.widgetPatch l s0 s1
            let els = p.Elements.Value
            let bytes = p.Defs.Length + (els |> Array.sumBy (fun (id, m) -> id.Length + m.Length))
            Assert.Equal<string[]>([| "figure_panel-0-0_layer-1" |], els |> Array.map fst)
            Assert.True(bytes * 20 < p.Document.Length, sprintf "patch %d bytes of a %d-byte document" bytes p.Document.Length)
            Assert.Equal("patch", (Html.liveAnswerForHost (box l, box s0, box s1)).[0]))

    /// What the keys below the panel are for. The cursor's legend entry names where it is, so a
    /// move changes the legend as well as the layer. With the legend under the panel's own key
    /// the change would be the whole panel, 50 000 points and all.
    [<Fact>]
    let ``a cursor that names itself patches its legend, not its panel`` () =
        let named =
            let l = scatterWithCursor ()
            { l with View = fun vs -> l.View vs |> Chart.withName (sprintf "cursor at %g s" vs.[0]) }

        let s0 = Live.init named
        let s1 = Live.step named s0 (Set(0, 6.0))
        let p = Html.widgetPatch named s0 s1
        Assert.Equal<string[]>([| "figure_panel-0-0_layer-1"; "figure_panel-0-0_legend" |], p.Elements.Value |> Array.map fst)
        Assert.True(p.Elements.Value |> Array.sumBy (fun (_, m) -> m.Length) < p.Document.Length / 20)

    /// A zoom moves every tick and every mark, so the patch is nearly the document and the
    /// whole document is sent.
    [<Fact>]
    let ``a zoom sends the whole document`` () =
        RenderModes.withRenderMode RenderMode.Svg (fun () ->
            let l = scatterWithCursor ()
            let s0 = Live.init l
            let v = (Live.panels l s0).[0]
            let c = { X = (v.Plot.X0 + v.Plot.X1) / 2.0; Y = (v.Plot.Y0 + v.Plot.Y1) / 2.0 }
            let s1 = Live.step l s0 (Zoom { X0 = c.X - 80.0<px>; Y0 = c.Y - 50.0<px>; X1 = c.X + 80.0<px>; Y1 = c.Y + 50.0<px> })
            Assert.NotSame(s0, s1)
            let answer = Html.liveAnswerForHost (box l, box s0, box s1)
            Assert.Equal("svg", answer.[0])
            Assert.Equal(Html.widgetFigure l s1, answer.[1]))

    /// An unchanged state is answered with its labels and no figure.
    [<Fact>]
    let ``an unchanged state is the same`` () =
        let l = scatterWithCursor ()
        let s0 = Live.init l
        Assert.Equal<string[]>([| "same"; "5 s" |], Html.liveAnswerForHost (box l, box s0, box s0))
