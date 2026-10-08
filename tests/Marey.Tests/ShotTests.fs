/// D72 / D73 — **one arrangement for the whole shot**, and the two things it stops moving.
///
/// The problem this file gates is not a bug in anything: it is what correct per-frame layout
/// does when the frames are an animation. Since D61 the plot rectangle is what is *left* after
/// the widest tick label has taken its room, so a y domain climbing from `9` to `110` gains a
/// digit on every label at once, the y gutter widens, and the panel edge crawls left while the
/// data is the thing that was supposed to be moving. The same happens one level up to the axis
/// *title*: D19 picks the unit prefix from the domain, so a zoom crossing a decade steps
/// `mm → µm` mid-shot.
///
/// Both gates are written with their own positive control — the same frames laid out per frame
/// by `Chart.toScene`, asserted to *differ*. A gate that only checks the frozen path passes
/// just as well when the animation is not moving at all, and that is the failure mode these
/// two are most likely to have.
module Marey.Tests.ShotTests

open Xunit
open Marey
open Marey.UnitsOfMeasure

// ======================================================================================
// Fixtures
// ======================================================================================

let private surface = Chart.size 800.0 500.0
let private measurer = TextMeasure.approx

/// A ramp from `0` to `top`, in seconds against metres. The x domain is held still so that
/// everything these tests observe comes from the y axis alone.
let private ramp (top: float) : Figure =
    let xs = Array.init 24 (fun i -> float i / 23.0 * 10.0<s>)
    let ys = xs |> Array.map (fun t -> t / 10.0<s> * top * 1.0<m>)
    Chart.line U.second U.metre xs ys

/// The digit-gaining zoom: `0 … 9` at `u = 0`, `0 … 110` at `u = 1`. Two characters of tick
/// label, which is several pixels of y gutter.
let private digits (u: float) : Figure = ramp (9.0 + 101.0 * u)

/// The decade-crossing zoom: a domain from millimetres down to micrometres. D19 chooses the
/// prefix from the numbers, so per frame the axis title steps.
let private decades (u: float) : Figure =
    let xs = Array.init 24 (fun i -> float i / 23.0 * 10.0<s>)
    let top = 2.0e-3 * (0.001 ** u)
    let ys = xs |> Array.map (fun t -> t / 10.0<s> * top * 1.0<m>)
    Chart.line U.second U.metre xs ys

let private frames = [| 0.0; 0.2; 0.4; 0.6; 0.8; 1.0 |]

/// Every affine in a compiled scene, innermost last. The panel's data node is
/// `Transform(Axes.panelTransform plotNd, Clip …)`, so this list *is* the plot rectangle,
/// compared by the structural equality D1 bought.
let private transforms (node: Node) =
    node
    |> Scene.fold
        (fun acc n ->
            match n with
            | Transform(a, _) -> a :: acc
            | _ -> acc)
        []

/// Axis titles: the text runs carrying a unit in brackets.
let private titles (node: Node) =
    node
    |> Scene.fold
        (fun acc n ->
            match n with
            | Text t when t.Text.Contains "[" -> t.Text :: acc
            | _ -> acc)
        []
    |> List.sort

// ======================================================================================
// The duplicate that §1 predicted, and that had already happened
// ======================================================================================

module OneGrid =

    /// `Figure.arrangement` was a hand-written second copy of `toScene`'s outer grid and it
    /// omitted the head row, so a titled figure reported panel regions taller than the ones it
    /// was drawn in — by exactly the height of the title. This is that difference, asserted.
    [<Fact>]
    let ``a title takes room out of the panel, and the reported arrangement says so`` () =
        let bare = ramp 10.0
        let titled = bare |> Chart.withTitle "Free fall"

        let regionOf (fig: Figure) =
            Layout.findPx "panel-0-0" (Chart.arrangement measurer surface fig)

        let a = regionOf bare
        let b = regionOf titled

        // The head row exists at all — the old copy had no `head` cell, so this alone fails it.
        let head = Layout.findPx "head" (Chart.arrangement measurer surface titled)
        Assert.True(float (Rect.height head) > 0.0, "a titled figure has a head row")

        // And the panel is pushed down by it. Same width, less height, and the top edge moves.
        Assert.Equal(float (Rect.width a), float (Rect.width b), 9)
        Assert.True(float (Rect.height b) < float (Rect.height a), "the title costs the panel height")
        Assert.True(float b.Y0 > float a.Y0, "the panel starts below the head")

    /// The head row of an *untitled* figure is empty, which is the case the old copy got right
    /// and the reason it survived two milestones.
    [<Fact>]
    let ``an untitled figure has an empty head row`` () =
        let head = Layout.findPx "head" (Chart.arrangement measurer surface (ramp 10.0))
        Assert.Equal(0.0, float (Rect.height head), 9)

    /// The split is a refactor, not a change: `toScene` is `arrangeFor` then `toSceneIn`, and
    /// composing them by hand gives the same scene.
    [<Fact>]
    let ``toScene is arrangeFor then toSceneIn`` () =
        let fig = ramp 10.0 |> Chart.withTitle "Free fall"
        let shot = Chart.arrangeFor measurer surface fig
        Assert.Equal<Node>(Chart.toScene surface fig, Chart.toSceneIn measurer shot fig)

// ======================================================================================
// Gate 8 — the panel does not breathe
// ======================================================================================

module PanelDoesNotBreathe =

    [<Fact>]
    let ``a digit-gaining zoom moves the panel when it is laid out per frame`` () =
        // The positive control, and it has to come first: if this passes with *one* distinct
        // arrangement then the fixture is not gaining a digit and the gate below proves nothing.
        let perFrame =
            frames |> Array.map (fun u -> transforms (Chart.toScene surface (digits u)))

        Assert.True(
            (perFrame |> Array.distinct).Length > 1,
            "the fixture must actually move the panel, or the gate below is vacuous"
        )

    [<Fact>]
    let ``the plot rectangle is identical across every frame of the shot`` () =
        let shot = Chart.arrangeUnion 32 measurer surface digits

        let inShot =
            frames
            |> Array.map (fun u -> transforms (Chart.toSceneIn measurer shot (digits u)))

        Assert.Single(Array.distinct inShot) |> ignore

    /// D72's other half, and the half a "freeze frame zero" implementation would fail: the
    /// frozen gutters have to be the ones the *widest* frame needed, not the first frame's.
    [<Fact>]
    let ``the union takes the widest frame's gutters, not the first frame's`` () =
        let shot = Chart.arrangeUnion 32 measurer surface digits
        let frozen = shot.Panels |> Array.find (fun (k, _) -> k = "panel-0-0") |> snd

        let gutterAt (u: float) =
            let one = Chart.arrangeFor measurer surface (digits u)
            (one.Panels |> Array.find (fun (k, _) -> k = "panel-0-0") |> snd).YTicksW

        Assert.True(float (gutterAt 1.0) > float (gutterAt 0.0), "the last frame's labels are wider")
        Assert.Equal(float (gutterAt 1.0), float frozen.YTicksW, 9)

    /// §5's accepted imprecision, pinned rather than argued away: a still frame lifted out of an
    /// animation has more gutter than the same figure rendered alone.
    [<Fact>]
    let ``a still frame is not the same picture as the figure rendered alone`` () =
        let shot = Chart.arrangeUnion 32 measurer surface digits
        let alone = transforms (Chart.toScene surface (digits 0.0))
        let inShot = transforms (Chart.toSceneIn measurer shot (digits 0.0))
        Assert.NotEqual<Affine<nd, nd> list>(alone, inShot)

    /// **D108.** Every state of the shot fits the shot. A decade-crossing zoom pins one prefix
    /// for the whole shot, so its early frames are drawn as `0.002 … 0.008` rather than the
    /// `0 2 4 6 8` they would show alone; the union has to have measured them that way, or the
    /// rectangle holds still and the labels do not fit in it. Found by M4.5's `fitsIn`.
    [<Fact>]
    let ``every state fits the shot it was measured for`` () =
        let shot = Chart.arrangeUnion 32 measurer surface decades

        for u in frames do
            Assert.True(Chart.fitsIn measurer shot (decades u), sprintf "the frame at u = %g overflows its own shot" u)

// ======================================================================================
// Gate 9 — the prefix does not step
// ======================================================================================

module PrefixDoesNotStep =

    [<Fact>]
    let ``a decade-crossing zoom re-picks the prefix when it is laid out per frame`` () =
        let perFrame =
            frames |> Array.map (fun u -> titles (Chart.toScene surface (decades u)))

        Assert.True(
            (perFrame |> List.ofArray |> List.distinct).Length > 1,
            "the fixture must actually cross a decade, or the gate below is vacuous"
        )

    [<Fact>]
    let ``the axis title is identical across every frame of the shot`` () =
        let shot = Chart.arrangeUnion 32 measurer surface decades

        let inShot =
            frames
            |> Array.map (fun u -> titles (Chart.toSceneIn measurer shot (decades u)))
            |> List.ofArray
            |> List.distinct

        Assert.Single inShot |> ignore

    /// The prefix that gets pinned is the one the *union* domain chooses, not frame zero's and
    /// not the last frame's. Here the union runs from `2 µm` to `2 mm`, and millimetres is what
    /// covers it.
    [<Fact>]
    let ``the pinned prefix comes from the union domain`` () =
        let shot = Chart.arrangeUnion 32 measurer surface decades
        let frozen = shot.Panels |> Array.find (fun (k, _) -> k = "panel-0-0") |> snd
        Assert.True(frozen.YPrefix.IsSome, "an animation pins both axes")

        let title =
            titles (Chart.toSceneIn measurer shot (decades 1.0))
            |> List.find (fun t -> t.StartsWith "x ")

        Assert.Equal("x [mm]", title)

    /// A still figure pins nothing, which is what keeps every existing baseline byte-identical.
    [<Fact>]
    let ``a still figure chooses its prefix per figure, as it always has`` () =
        let one = Chart.arrangeFor measurer surface (decades 1.0)
        let frozen = one.Panels |> Array.find (fun (k, _) -> k = "panel-0-0") |> snd
        Assert.True(frozen.XPrefix.IsNone, "a still figure pins nothing")
        Assert.True(frozen.YPrefix.IsNone, "a still figure pins nothing")

// ======================================================================================
// The union itself
// ======================================================================================

module TheUnion =

    [<Fact>]
    let ``the sample count is reported, not assumed`` () =
        Assert.Equal(32, (Chart.arrangeUnion 32 measurer surface digits).Samples)
        Assert.Equal(1, (Chart.arrangeFor measurer surface (ramp 10.0)).Samples)

    [<Fact>]
    let ``a shot needs at least one sample`` () =
        Assert.Throws<System.ArgumentException>(fun () ->
            Chart.arrangeUnion 0 measurer surface digits |> ignore)
        |> ignore

    /// One sample is legal and lands on `u = 0` — the degenerate shot, and the one a caller
    /// reaches for when they want frame zero's layout and know the animation does not move.
    [<Fact>]
    let ``one sample is frame zero`` () =
        let shot = Chart.arrangeUnion 1 measurer surface digits
        let frozen = shot.Panels |> Array.find (fun (k, _) -> k = "panel-0-0") |> snd
        let alone = Chart.arrangeFor measurer surface (digits 0.0)
        let single = alone.Panels |> Array.find (fun (k, _) -> k = "panel-0-0") |> snd
        Assert.Equal(float single.YTicksW, float frozen.YTicksW, 9)

    /// Two layouts whose every size disagrees, and they alternate which one wins. An "argmax
    /// over samples" implementation passes every gate above and fails this one, because neither
    /// operand holds more than half the maxima — and a union that quietly forgets one field
    /// passes a test that only reads the other eight, which is why this reads all ten.
    let private oddsAndEvens =
        let a =
            { PanelLayout.empty with
                TitleH = 11.0<px>
                TopPad = 2.0<px>
                XTicksH = 13.0<px>
                XTitleH = 4.0<px>
                YTitleW = 15.0<px>
                YTicksW = 6.0<px>
                RightPad = 17.0<px>
                BarGap = 8.0<px>
                BarW = 19.0<px>
                Probe = { Width = 10.0<px>; Height = 21.0<px> } }

        let b =
            { PanelLayout.empty with
                TitleH = 1.0<px>
                TopPad = 12.0<px>
                XTicksH = 3.0<px>
                XTitleH = 14.0<px>
                YTitleW = 5.0<px>
                YTicksW = 16.0<px>
                RightPad = 7.0<px>
                BarGap = 18.0<px>
                BarW = 9.0<px>
                Probe = { Width = 20.0<px>; Height = 11.0<px> } }

        a, b

    [<Fact>]
    let ``the union is componentwise, not the widest sample`` () =
        let a, b = oddsAndEvens
        let u = PanelLayout.union a b

        let got =
            [ float u.TitleH
              float u.TopPad
              float u.XTicksH
              float u.XTitleH
              float u.YTitleW
              float u.YTicksW
              float u.RightPad
              float u.BarGap
              float u.BarW
              float u.Probe.Width
              float u.Probe.Height ]

        Assert.Equal<float list>([ 11.0; 12.0; 13.0; 14.0; 15.0; 16.0; 17.0; 18.0; 19.0; 20.0; 21.0 ], got)

    [<Fact>]
    let ``the union is symmetric in its sizes`` () =
        let a, b = oddsAndEvens
        Assert.Equal<PanelLayout>(PanelLayout.union a b, PanelLayout.union b a)

    /// A layerless panel has no numbers, so it has no domain to union and no prefix to pin.
    /// `toScene` has drawn one as `Scene.empty` since M0; the union has to answer the same case
    /// rather than index into an empty layer array.
    [<Fact>]
    let ``a panel with no layers has no prefix to pin`` () =
        let blank = Figure.create [| Figure.panelOf [||] |]
        let shot = Chart.arrangeUnion 4 measurer surface (fun _ -> blank)
        let frozen = shot.Panels |> Array.find (fun (k, _) -> k = "panel-0-0") |> snd
        Assert.True(frozen.XPrefix.IsNone && frozen.YPrefix.IsNone, "no numbers, no prefix")
        Assert.Equal<Node>(Chart.toScene surface blank, Chart.toSceneIn measurer shot blank)

    /// A panel key the shot does not carry is measured from its own data rather than throwing,
    /// so a figure that grows a panel mid-shot degrades to per-frame layout for the new one.
    [<Fact>]
    let ``a panel the shot never saw is still drawn`` () =
        let shot = Chart.arrangeUnion 4 measurer surface digits
        let two = Chart.beside (digits 0.0) (digits 1.0)
        let scene = Chart.toSceneIn measurer shot two
        Assert.True((transforms scene).Length >= 1, "the extra panel compiled")

// ======================================================================================
// A shot whose states disagree about the grid's shape
// ======================================================================================

/// The outer grid was taken from **one** state — the one with the most panels, ties broken by
/// rows — so a shot over a 1×2 state and a 2×1 state took the 2×1 grid, and the 1×2 state's
/// right-hand panel had no region in it. `toSceneIn` draws a panel with no region as nothing, so it
/// vanished from every frame that had it. Reported by review on the 0.3.0 PR.
module ``a shot whose states change the grid's shape`` =

    let private side (u: float) =
        if u < 0.5 then
            ramp 1.0 |> Chart.beside (ramp 2.0)
        else
            ramp 1.0 |> Chart.above (ramp 2.0)

    [<Fact>]
    let ``every panel of every state has a region`` () =
        let shot = Chart.arrangeUnion 2 measurer surface side

        for key in [ "panel-0-0"; "panel-0-1"; "panel-1-0" ] do
            Assert.True((Layout.tryFindPx key shot.Outer).IsSome, sprintf "%s has no region in the shot" key)

    [<Fact>]
    let ``both states draw every panel they have`` () =
        let shot = Chart.arrangeUnion 2 measurer surface side
        let besides = Chart.toSceneIn measurer shot (side 0.0)
        let stacked = Chart.toSceneIn measurer shot (side 1.0)

        Assert.True((Scene.tryFindKey (Keys.panel 0 1) besides).IsSome, "the side-by-side state's right panel was not drawn")
        Assert.True((Scene.tryFindKey (Keys.panel 1 0) stacked).IsSome, "the stacked state's lower panel was not drawn")
