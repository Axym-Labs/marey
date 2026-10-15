/// §9 / §13 — `Marey.Plot`: themes, layout, the axis compiler, and the `Chart` front door.
///
/// §13 names five property suites. This file supplies the one that needed `Marey.Plot` to
/// exist first:
///
/// > "**layout invariants (no overlapping panels, labels inside bounds)**"
///
/// It also locks in the three claims the project is actually *sold* on, because they are
/// claims about behaviour and behaviour regresses:
///
/// * **§9 / §2.2 — an axis labels itself from the unit witness with no configuration.** If
///   `t [s]` ever stops appearing, the README is lying and the whole units thesis is dead.
/// * **§5.1 — batching survives the compile pipeline**, not just the backend. A 50 000-point
///   scatter must still be *one* `Markers` node after layout, theming and clipping.
/// * **§1.1 — combinators do not mutate.** The design indicts Plotly.NET by name for exactly
///   this ("styling functions *mutate* the chart (documented caveat)"). A library that repeats
///   the flaw it was written to avoid is worse than one that never mentioned it.
///
/// What §13 still owes, and why it is not here: the `Anim` monoid laws need `Marey.Anim`
/// (§14 puts it at M3), and the `Sim` invariant-drift checks need `Marey.Sim` (M5). Those are
/// "not yet writable", not "not needed".
module Marey.Tests.PlotTests

open FsCheck.Xunit
open Xunit
open Marey
open Marey.UnitsOfMeasure

// ======================================================================================
// Fixtures
// ======================================================================================

/// Free fall: `float<s>` in, `float<m>` out. The measures are the entire configuration.
let private fallT = Array.init 40 (fun i -> float i * 0.25<s>)
let private fallX = fallT |> Array.map (fun t -> 0.5 * 9.81<m/s^2> * t * t)

let private surface = Chart.size 800.0 500.0

let private baseFigure () = Chart.line U.second U.metre fallT fallX

let private renderFig (extent: Extent<px>) (fig: Figure) =
    SvgBackend.render extent (Chart.toScene extent fig)

/// Every `Text` node in a compiled scene, with its panel-space anchor.
let private textNodes (node: Node) =
    node
    |> Scene.fold
        (fun acc n ->
            match n with
            | Text t -> t :: acc
            | _ -> acc)
        []

// ======================================================================================
// §9 / §2.2 — the headline: axes label themselves from the witness
// ======================================================================================

module SelfLabellingAxes =

    [<Fact>]
    let ``seconds against metres labels itself 't [s]' and 'x [m]' with no configuration`` () =
        let svg = renderFig surface (baseFigure ())
        Assert.Contains(">t [s]<", svg)
        Assert.Contains(">x [m]<", svg)

    /// §2.2's worked example, verbatim: "data in `float<m>` spanning `1e-9 … 5e-9` should
    /// produce ticks labelled `1, 2, 3, 4, 5` with an axis title of `x [nm]`."
    [<Fact>]
    let ``§2.2 nanometre data produces 'x [nm]' with a 1..5 tick ladder`` () =
        let x = Array.init 5 (fun i -> float i / 4.0 * 1.0<s>)
        let y = Array.init 5 (fun i -> (1.0 + float i) * 1e-9<m>)
        let svg = renderFig surface (Chart.line U.second U.metre x y)

        Assert.Contains(">x [nm]<", svg)

        for i in 1..5 do
            Assert.Contains(sprintf ">%d<" i, svg)

    /// The kilogram is the one SI base unit that already carries a prefix, so a milligram
    /// axis is the case a naive implementation gets wrong by a factor of 1000 (D19).
    [<Fact>]
    let ``a microgram-scale mass axis reads mg, never µkg`` () =
        let x = Array.init 5 (fun i -> float i * 1.0<s>)
        let y = Array.init 5 (fun i -> (1.0 + float i) * 1e-6<kg>)
        let svg = renderFig surface (Chart.line U.second U.kilogram x y)

        Assert.Contains("[mg]", svg)
        Assert.DoesNotContain("µkg", svg)

// ======================================================================================
// Plain numbers: unspecified, and dimensionless
// ======================================================================================

/// `Chart.Plain` was `Chart` at `U.one U.one`; D109 retired it for the unit-optional
/// constructors, which leave a unit out as `U.unspecified` (D127). The two witnesses differ in
/// what they *claim* — `one` says dimensionless, and the axis-sharing check acts on it — and not
/// in how a single layer is drawn, so that is pinned here, along with the honesty of a bare axis:
/// a plain `x`, never `x [1]`, and never rescaled into a prefix.
module PlainNumbers =

    let private xs = Array.init 40 (fun i -> float i / 4.0)
    let private ys = xs |> Array.map sin
    let private errs = ys |> Array.map (fun y -> abs y * 0.1)

    [<Fact>]
    let ``a single layer draws the same whether its units are unspecified or dimensionless`` () =
        let cases =
            [ "line", Marey.Chart.Line(xs, ys), Chart.line U.one U.one xs ys
              "scatter", Marey.Chart.Scatter(xs, ys), Chart.scatter U.one U.one xs ys
              "area", Marey.Chart.Area(xs, ys), Chart.area U.one U.one xs ys
              "bar", Marey.Chart.Bar(xs, ys), Chart.bar U.one U.one xs ys
              "errorbar", Marey.Chart.Errorbar(xs, ys, errs), Chart.errorbar U.one U.one xs ys errs
              "add",
              (Marey.Chart.Line(xs, ys) |> Chart.add (Marey.Chart.Scatter(xs, Array.rev ys))),
              (Chart.line U.one U.one xs ys |> Chart.addScatter U.one U.one xs (Array.rev ys)) ]

        for name, unspecified, dimensionless in cases do
            Assert.True(renderFig surface unspecified = renderFig surface dimensionless, name)

    [<Fact>]
    let ``a plain axis reads a bare x and y, never '[1]'`` () =
        for fig in [ Marey.Chart.Line(xs, ys); Chart.line U.one U.one xs ys ] do
            let svg = renderFig surface fig
            Assert.Contains(">x<", svg)
            Assert.Contains(">y<", svg)
            Assert.DoesNotContain("[1]", svg)
            Assert.DoesNotContain("[]", svg)

    /// `Prefix` refuses to rescale a quantity with no dimension, and an unspecified one has none
    /// to rescale by. A ratio spanning 1e-9 is a small ratio, not a nano-ratio.
    [<Fact>]
    let ``tiny plain data is never given an SI prefix`` () =
        let tiny = ys |> Array.map (fun y -> y * 1e-9)

        for fig in [ Marey.Chart.Line(xs, tiny); Chart.line U.one U.one xs tiny ] do
            let svg = renderFig surface fig
            Assert.Contains(">y<", svg)
            Assert.DoesNotContain("[n]", svg)
            Assert.DoesNotContain("[p]", svg)

// ======================================================================================
// D45 — a categorical axis, and the check that keeps two of them apart
// ======================================================================================

module CategoricalAxes =

    let private levels = [| "control"; "treated"; "placebo"; "washout" |]

    let private bars (ls: string[]) (ys: float<m>[]) : Series<1, m> =
        { Name = ""
          X = Array.init ls.Length float
          Y = ys
          XScale = Scale.ordinal ls
          YScale = Scale.linear U.metre (0.0<m>, 21.0<m>) }

    let private yields = [| 12.4<m>; 19.1<m>; 7.8<m>; 15.0<m> |]

    let private layerOf i (s: Series<1, m>) : Layer =
        { Name = ""
          Data = Erase.packSeries s
          Geom = GeomPoint
          Style = Geom.styleFor Theme.initial i
          Channels = [||]
          ShowInLegend = false
          Addressed = [||]
          RowKeys = [||]
          InDomain = (true, true) }

    /// The whole point of D45, rendered: a bar per level, a named tick per band, and no
    /// configuration beyond the level set.
    [<Fact>]
    let ``a categorical axis renders one named tick per level`` () =
        let s = bars levels yields
        let svg = renderFig surface (Chart.ofSeries (Geom.bar 0.7 s) s)

        for name in levels do
            Assert.Contains(">" + name + "<", svg)

    /// An ordinal scale is dimensionless, so `Figure.unify`'s runtime `Dim` check — the one
    /// runtime dimensional check in the library — cannot tell two *different* categories apart.
    /// Verified before the guard was written: the panel unified silently and the second layer's
    /// points were drawn against the first layer's labels.
    [<Fact>]
    let ``two categories cannot share an axis`` () =
        let panel =
            Figure.panelOf
                [| layerOf 0 (bars levels yields)
                   layerOf 1 (bars [| "a"; "b"; "c"; "d" |] yields) |]

        let ex = Assert.Throws<System.Exception>(fun () -> Figure.unify panel |> ignore)
        Assert.Contains("different sets of levels", ex.Message)
        // The message names both level sets, because "they differ" is not actionable.
        Assert.Contains("control", ex.Message)
        Assert.Contains("a, b, c, d", ex.Message)

    /// The same guard in its other direction: a categorical axis and a continuous one are not
    /// the same axis either, and both are dimensionless when the continuous one is a bare ratio.
    [<Fact>]
    let ``a category cannot share an axis with a continuous scale`` () =
        let continuous: Series<1, m> =
            { bars levels yields with
                XScale = Scale.linear U.one (0.0, 3.0) }

        let panel =
            Figure.panelOf [| layerOf 0 (bars levels yields); layerOf 1 continuous |]

        let ex = Assert.Throws<System.Exception>(fun () -> Figure.unify panel |> ignore)
        Assert.Contains("categorical", ex.Message)

    /// Two layers over the *same* levels are a legitimate figure — a grouped bar chart is
    /// exactly that — and the guard must not refuse them.
    [<Fact>]
    let ``two layers over the same levels unify`` () =
        let panel =
            Figure.panelOf
                [| layerOf 0 (bars levels yields)
                   layerOf 1 (bars levels [| 3.0<m>; 4.0<m>; 5.0<m>; 6.0<m> |]) |]

        let unified = Figure.unify panel
        let x, _ = Erase.seriesScales unified.Layers.[1].Data

        // Unification did not stretch the bands: a categorical domain is its level count.
        Assert.Equal(ScaleKind.Ordinal levels, x.Kind)
        Assert.Equal((-0.5, 3.5), x.Domain)

// ======================================================================================
// `Figure.unify` — the domain layers share
// ======================================================================================

module SharedDomains =

    let private t = [| 0.0<s>; 1.0<s>; 2.0<s> |]
    let private small = [| -1e-3<m>; 0.5e-3<m>; 1e-3<m> |]
    let private flat (v: float<m>) = Array.create 3 v

    let private yDomain (fig: Figure) =
        let unified = Figure.unify fig.Panels.[0]
        (snd (Erase.seriesScales unified.Layers.[0].Data)).Domain

    /// Found in 0.6.0: a constant series at exactly zero is padded to ±1 in its own unit, so
    /// that it has an axis at all, and the union across layers then took that ±1 m as data — a
    /// reference line at y = 0 turned a ±1 mm axis into a ±1 m one, titled `x [m]`.
    [<Fact>]
    let ``a flat line at zero does not widen a millimetre axis`` () =
        let fig =
            Chart.line U.second U.metre t small
            |> Chart.addLine U.second U.metre t (flat 0.0<m>)

        let lo, hi = yDomain fig
        Assert.Equal(-1.1e-3, lo, 12)
        Assert.Equal(1.1e-3, hi, 12)

        let svg = renderFig (Chart.size 400.0 300.0) fig
        Assert.Contains(">x [mm]<", svg)
        Assert.DoesNotContain(">x [m]<", svg)

    /// The point is still on the axis when it lies outside the other layers' span, with room
    /// around it rather than on the edge of the panel.
    [<Fact>]
    let ``a flat line outside the span is on the axis, inside the panel`` () =
        let fig =
            Chart.line U.second U.metre t small
            |> Chart.addLine U.second U.metre t (flat 5e-3<m>)

        let lo, hi = yDomain fig
        Assert.Equal(-1.1e-3, lo, 12)
        Assert.True(hi > 5e-3 && hi < 6e-3, sprintf "hi = %g" hi)

    /// A panel whose only data is constant still needs a non-degenerate axis, and keeps the one
    /// it always had.
    [<Fact>]
    let ``a panel of flat lines alone keeps its padded axis`` () =
        let zeros =
            Chart.line U.second U.metre t (flat 0.0<m>)
            |> Chart.addLine U.second U.metre t (flat 0.0<m>)

        Assert.Equal((-1.0, 1.0), yDomain zeros)

        let fives =
            Chart.line U.second U.metre t (flat 5.0<m>)
            |> Chart.addLine U.second U.metre t (flat 5.0<m>)

        let lo, hi = yDomain fives
        Assert.Equal(4.75, lo, 12)
        Assert.Equal(5.25, hi, 12)

    /// A stated domain is not padding, and a flat layer under it keeps it.
    [<Fact>]
    let ``a stated domain is kept`` () =
        let fig =
            Chart.line U.second U.metre t small
            |> Chart.addLine U.second U.metre t (flat 0.0<m>)
            |> Chart.withYDomain -2.0 2.0

        Assert.Equal((-2.0, 2.0), yDomain fig)

// ======================================================================================
// D163 — which layers join the domain
// ======================================================================================

[<Properties(Arbitrary = [| typeof<Marey.Tests.Generators.MareyArbs> |], MaxTest = 300, Replay = Marey.Tests.Generators.Seed, QuietOnSuccess = true)>]
module DomainParticipation =

    open Marey.Tests.Generators

    let private t = [| 0.0<s>; 1.0<s>; 2.0<s> |]

    let private out (fig: Figure) =
        let p = fig.Panels.[fig.Panels.Length - 1]
        let i = p.Layers.Length - 1

        { fig with
            Panels =
                fig.Panels
                |> Array.mapi (fun j q ->
                    if j <> fig.Panels.Length - 1 then
                        q
                    else
                        { q with
                            Layers = q.Layers |> Array.mapi (fun k l -> if k = i then { l with InDomain = (false, false) } else l) }) }

    let private domains (fig: Figure) =
        let unified = Figure.unify fig.Panels.[0]
        let sx, sy = Erase.seriesScales unified.Layers.[0].Data
        sx.Domain, sy.Domain

    let private data (LinearDomain(lo, hi)) =
        [| lo * 1.0<m>; (lo + hi) / 2.0 * 1.0<m>; hi * 1.0<m> |]

    /// The law the whole decision is for: a layer that is out on both axes never changes the
    /// domain, wherever its own values lie.
    [<Property>]
    let ``a layer out of the domain does not change it`` (d: LinearDomain) (FiniteFloat a) (FiniteFloat b) (FiniteFloat c) =
        let alone = Chart.line U.second U.metre t (data d)

        let withOut =
            alone
            |> Chart.addLine U.second U.metre [| 5.0<s>; -9.0<s>; 0.5<s> |] [| a * 1.0<m>; b * 1.0<m>; c * 1.0<m> |]
            |> out

        domains withOut = domains alone

    /// And one that is in can only widen it.
    [<Property>]
    let ``a layer in the domain never shrinks it`` (d: LinearDomain) (FiniteFloat a) (FiniteFloat b) =
        let alone = Chart.line U.second U.metre t (data d)
        let both = alone |> Chart.addLine U.second U.metre t [| a * 1.0<m>; b * 1.0<m>; a * 1.0<m> |]
        let (_, (lo0, hi0)), (_, (lo1, hi1)) = domains alone, domains both
        lo1 <= lo0 && hi1 >= hi0

    /// The check follows the axis, not the domain: a rule in kelvin is wrong on a metre axis
    /// whether or not it moves it.
    [<Fact>]
    let ``a layer out of the domain still has its unit checked`` () =
        let fig =
            Chart.line U.second U.metre t [| 1.0<m>; 2.0<m>; 3.0<m> |]
            |> Chart.addLine U.second U.kelvin t [| 1.0<K>; 1.0<K>; 1.0<K> |]
            |> out

        let e = Assert.ThrowsAny<exn>(fun () -> Figure.unify fig.Panels.[0] |> ignore)
        Assert.Contains("cannot share an axis", e.Message)

    /// A panel of marks that are all out still has axes: their own extent.
    [<Fact>]
    let ``a panel whose every layer is out takes their extent`` () =
        let fig =
            Chart.line U.second U.metre t [| 1.0<m>; 2.0<m>; 3.0<m> |]
            |> out
            |> Chart.addLine U.second U.metre t [| 7.0<m>; 8.0<m>; 9.0<m> |]
            |> out

        let _, (lo, hi) = domains fig
        Assert.True(lo < 1.0 && hi > 9.0, sprintf "(%g, %g)" lo hi)

    /// `withDomainIncluded` puts a layer back.
    [<Fact>]
    let ``withDomainIncluded lets a layer move the axes again`` () =
        let fig =
            Chart.line U.second U.metre t [| 1.0<m>; 2.0<m>; 3.0<m> |]
            |> Chart.addLine U.second U.metre t [| 9.0<m>; 9.0<m>; 9.0<m> |]
            |> out

        let _, (_, hiOut) = domains fig
        let _, (_, hiIn) = domains (fig |> Chart.withDomainIncluded)
        Assert.True(hiOut < 9.0, sprintf "out: %g" hiOut)
        Assert.True(hiIn > 9.0, sprintf "in: %g" hiIn)

    /// Facets share by the same rule: a layer that is out in one panel does not widen the
    /// others.
    [<Fact>]
    let ``a facet shares only the layers that are in`` () =
        let a = Chart.line U.second U.metre t [| 1.0<m>; 2.0<m>; 3.0<m> |]

        let b =
            Chart.line U.second U.metre t [| 1.5<m>; 2.0<m>; 2.5<m> |]
            |> Chart.addLine U.second U.metre t [| 50.0<m>; 50.0<m>; 50.0<m> |]
            |> out

        let fig = Chart.facet [| "a", a; "b", b |]
        let _, sy = Erase.seriesScales fig.Panels.[0].Layers.[0].Data
        let _, hi = sy.Domain
        Assert.True(hi < 4.0, sprintf "hi = %g" hi)

// ======================================================================================
// D162 — rules and spans
// ======================================================================================

module Rules =

    let private t = [| 0.0<s>; 1.0<s>; 2.0<s> |]
    let private small = [| -1e-3<m>; 0.5e-3<m>; 1e-3<m> |]
    let private surface = Chart.size 400.0 300.0

    /// Every `Annotated` node in a scene, outermost first.
    let rec private annotated (n: Node) : (Semantics * Node) list =
        match n with
        | Annotated(s, inner) -> (s, inner) :: annotated inner
        | Group ns -> ns |> Array.toList |> List.collect annotated
        | Transform(_, c)
        | Clip(_, c)
        | Opacity(_, c)
        | Keyed(_, c) -> annotated c
        | _ -> []

    /// The layers drawn as annotations: the rules, by the role their layer carries.
    let private rules (fig: Figure) =
        Chart.toScene surface fig
        |> annotated
        |> List.filter (fun (s, _) -> s.Role = RoleAnnotation && s.Data |> Array.exists (fun (k, _) -> k = "points"))

    let private pathPoints (n: Node) : Vec2<nd>[] =
        match n with
        | Path(segs, _, _, _) ->
            segs
            |> Array.choose (function
                | MoveTo p
                | LineTo p -> Some p
                | _ -> None)
        | _ -> [||]

    /// The extent of a rule's path along x, in panel space: (0, 1) when it crosses the panel.
    let private xExtent (n: Node) =
        let xs = pathPoints n |> Array.map (fun p -> float p.X)
        Array.min xs, Array.max xs

    let private yDomain (fig: Figure) =
        let unified = Figure.unify fig.Panels.[0]
        (snd (Erase.seriesScales unified.Layers.[0].Data)).Domain

    /// G1 — the case D163 exists for: `axhline(0)` on residuals in millimetres.
    [<Fact>]
    let ``a rule at zero leaves a millimetre axis alone`` () =
        let fig = Chart.line U.second U.metre t small |> Chart.addHLine U.metre 0.0<m>
        let lo, hi = yDomain fig
        Assert.Equal(-1.1e-3, lo, 12)
        Assert.Equal(1.1e-3, hi, 12)
        Assert.Contains(">x [mm]<", renderFig surface fig)

        // Zero is inside the data, which a constant's point (D172) would also have kept. A rule
        // past the data is the case only D163 keeps out.
        let far = Chart.line U.second U.metre t small |> Chart.addHLine U.metre 5e-3<m>
        Assert.Equal((lo, hi), yDomain far)

    /// G1 — and one asked in reaches the axis on its own axis only.
    [<Fact>]
    let ``withDomainIncluded brings a rule's value into its axis and no other`` () =
        let fig =
            Chart.line U.second U.metre t small
            |> Chart.addHLine U.metre 5e-3<m>
            |> Chart.withDomainIncluded

        let unified = Figure.unify fig.Panels.[0]
        let sx, sy = Erase.seriesScales unified.Layers.[0].Data
        Assert.True(snd sy.Domain > 5e-3, sprintf "y: %A" sy.Domain)
        Assert.Equal((-0.1, 2.1), sx.Domain)

    /// G2 — a rule past the panel is not drawn, and the layer says so.
    [<Fact>]
    let ``a rule outside the domain is counted, not drawn`` () =
        let fig = Chart.line U.second U.metre t small |> Chart.addVLine U.second 9.0<s>
        Assert.Empty(rules fig)

        let panel =
            Chart.toScene surface fig
            |> annotated
            |> List.find (fun (s, _) -> s.Role = RoleOther "panel")
            |> fst

        Assert.Contains(("dropped-marks", "1"), panel.Data)

        // And a figure that drops nothing says nothing.
        let quiet = Chart.line U.second U.metre t small |> Chart.addVLine U.second 1.0<s>

        Chart.toScene surface quiet
        |> annotated
        |> List.iter (fun (s, _) -> Assert.DoesNotContain(s.Data, fun (k, _) -> k = "dropped-marks"))

    /// G3 — a rule spans the panel whatever the panel's domain is when it is drawn: two stated
    /// domains, a facet's sharing, and the domain a zoom gives a panel.
    [<Fact>]
    let ``a rule follows the panel's final domain`` () =
        let ruled = Chart.line U.second U.metre t small |> Chart.addHLine U.metre 0.5e-3<m>

        // The rule's height on the panel, against the y domain that panel ends up with.
        let figures =
            [ ruled |> Chart.withXDomain 0.0 2.0, (-1.1e-3, 1.1e-3)
              ruled |> Chart.withXDomain -50.0 3.0, (-1.1e-3, 1.1e-3)
              Chart.facet [| "a", ruled; "b", Chart.line U.second U.metre [| 5.0<s>; 8.0<s> |] [| -3e-3<m>; 0.0<m> |] |],
              (-3.15e-3, 1.1e-3)
              ruled |> Chart.withPanelDomains (0, 0) (Some(0.5, 1.0)) (Some(-2e-4, 6e-4)), (-2e-4, 6e-4) ]

        for fig, (lo, hi) in figures do
            match rules fig with
            | (_, inner) :: _ ->
                Assert.Equal((0.0, 1.0), xExtent inner)
                let y = float (pathPoints inner).[0].Y
                Assert.Equal((0.5e-3 - lo) / (hi - lo), y, 9)
            | [] -> failwith "no rule drawn"

    /// G4 at run time: a stated unit on a rule is checked against the axis, as a layer's is.
    [<Fact>]
    let ``a rule in the wrong unit is refused, with both units named`` () =
        let fig =
            Marey.Chart.Line([| 0.0; 1.0 |], [| 1.0; 2.0 |], yUnit = U.metre)
            |> Chart.add (Marey.Chart.HLine(1.0, U.second))

        let e = Assert.ThrowsAny<exn>(fun () -> renderFig surface fig |> ignore)
        Assert.Contains("cannot share an axis", e.Message)

    /// A rule is furniture: the theme's axis colour, recoloured by a theme change, unless it
    /// was coloured by hand.
    [<Fact>]
    let ``a rule takes the theme's axis colour and follows a retheme`` () =
        let ruled = Chart.line U.second U.metre t small |> Chart.addHLine U.metre 0.0<m>
        let dark = ruled |> Chart.withTheme Theme.dark
        Assert.Equal(Theme.dark.AxisColor, dark.Panels.[0].Layers.[1].Style.Color)

        let chosen = ruled |> Chart.withColor Palette.vermillion |> Chart.withTheme Theme.dark
        Assert.Equal(Palette.vermillion, chosen.Panels.[0].Layers.[1].Style.Color)

    /// Under polar a horizontal rule is a circle, sampled before the warp rather than a chord.
    [<Fact>]
    let ``a rule under a polar coord is sampled`` () =
        let fig =
            Chart.line U.second U.metre t small
            |> Chart.addHLine U.metre 0.0<m>
            |> Chart.withCoord Coord.polar

        match rules fig with
        | (_, inner) :: _ -> Assert.True((pathPoints inner).Length > 60)
        | [] -> failwith "no rule drawn"

    /// The line through a point is cut where it leaves the panel, at both ends.
    [<Fact>]
    let ``a slope rule is clipped to the panel's edges`` () =
        let fig =
            Chart.line U.second U.metre t small
            |> Chart.addLineThrough U.second U.metre 1.0<s> 0.0<m> 1e-3<m / s>

        match rules fig with
        | (_, inner) :: _ ->
            let onEdge (v: float<nd>) = abs (float v) < 1e-9 || abs (float v - 1.0) < 1e-9

            for p in pathPoints inner do
                Assert.True(onEdge p.X || onEdge p.Y, sprintf "(%g, %g)" (float p.X) (float p.Y))
        | [] -> failwith "no rule drawn"

// ======================================================================================
// D164 / D165 — annotations, panel text and panel letters
// ======================================================================================

module Annotations =

    let private t = [| 0.0<s>; 1.0<s>; 2.0<s> |]
    let private x = [| 1.0<m>; 2.0<m>; 3.0<m> |]
    let private surface = Chart.size 400.0 300.0

    let rec private annotated (n: Node) : (Semantics * Node) list =
        match n with
        | Annotated(s, inner) -> (s, inner) :: annotated inner
        | Group ns -> ns |> Array.toList |> List.collect annotated
        | Transform(_, c)
        | Clip(_, c)
        | Opacity(_, c)
        | Keyed(_, c) -> annotated c
        | _ -> []

    let rec private texts (n: Node) : TextRun list =
        match n with
        | Text r -> [ r ]
        | Group ns -> ns |> Array.toList |> List.collect texts
        | Transform(_, c)
        | Clip(_, c)
        | Opacity(_, c)
        | Keyed(_, c)
        | Annotated(_, c) -> texts c
        | _ -> []

    /// The annotation layers, by their role and the `points` every layer writes.
    let private notes (fig: Figure) =
        Chart.toScene surface fig
        |> annotated
        |> List.filter (fun (s, _) -> s.Role = RoleAnnotation && s.Data |> Array.exists (fun (k, _) -> k = "points"))

    let private textOf (fig: Figure) (label: string) =
        notes fig |> List.collect (snd >> texts) |> List.find (fun r -> r.Text = label)

    let private peak () =
        Chart.line U.second U.metre t x |> Chart.annotate U.second U.metre 1.0<s> 2.0<m> "peak"

    /// Labelling a point never moves the axes, and labelling one off the panel draws nothing and
    /// counts it.
    [<Fact>]
    let ``an annotation stays out of the domain, and one off the panel is counted`` () =
        let alone = Figure.unify (Chart.line U.second U.metre t x).Panels.[0]
        let marked = Figure.unify (peak () |> Chart.annotate U.second U.metre 9.0<s> 9.0<m> "far").Panels.[0]
        let domains (p: Panel) =
            let sx, sy = Erase.seriesScales p.Layers.[0].Data
            sx.Domain, sy.Domain

        Assert.Equal(domains alone, domains marked)

        let panel =
            Chart.toScene surface (peak () |> Chart.annotate U.second U.metre 9.0<s> 9.0<m> "far")
            |> annotated
            |> List.find (fun (s, _) -> s.Role = RoleOther "panel")
            |> fst

        Assert.Contains(("dropped-marks", "1"), panel.Data)

    /// G11 — an annotation is read with the axes: the theme's foreground, recoloured by a
    /// retheme, unless it was coloured by hand.
    [<Fact>]
    let ``an annotation takes the theme's foreground and follows a retheme`` () =
        Assert.Equal(Solid Theme.modern.Foreground, (textOf (peak ()) "peak").Paint)
        Assert.Equal(Solid Theme.dark.Foreground, (textOf (peak () |> Chart.withTheme Theme.dark) "peak").Paint)

        let chosen = peak () |> Chart.withColor Palette.vermillion |> Chart.withTheme Theme.dark
        Assert.Equal(Solid Palette.vermillion, (textOf chosen "peak").Paint)

    /// G3 — the text moves with its point under a zoom; text placed in the panel does not.
    [<Fact>]
    let ``an annotation follows its point and panel text stays put`` () =
        let fig = peak () |> Chart.addPanelText 0.98 0.98 "note"
        let zoomed = fig |> Chart.withPanelDomains (0, 0) (Some(0.0, 1.5)) (Some(1.0, 2.8))
        Assert.NotEqual((textOf fig "peak").At, (textOf zoomed "peak").At)
        Assert.Equal((textOf fig "note").At, (textOf zoomed "note").At)
        Assert.Equal(AnchorEnd, (textOf fig "note").Anchor)
        Assert.Equal(BaselineHanging, (textOf fig "note").Baseline)

    /// The text is anchored on the side facing its point, and an arrow is drawn with a head.
    [<Fact>]
    let ``an arrow leaves the text toward its point and ends in a head`` () =
        let fig = peak () |> Chart.withTextOffset 20.0<px> 20.0<px> |> Chart.withArrow ArrowStraight
        let run = textOf fig "peak"
        Assert.Equal(AnchorStart, run.Anchor)
        Assert.Equal(BaselineAlphabetic, run.Baseline)

        let paths =
            notes fig
            |> List.collect (fun (_, n) ->
                let rec walk n =
                    match n with
                    | Path(segs, stroke, fill, _) -> [ segs, stroke.IsSome, fill.IsSome ]
                    | Group ns -> ns |> Array.toList |> List.collect walk
                    | _ -> []

                walk n)

        Assert.Contains(paths, fun (_, stroked, filled) -> stroked && not filled)
        Assert.Contains(paths, fun (segs, stroked, filled) -> filled && not stroked && segs.Length = 4)

        let arced = peak () |> Chart.withTextOffset 20.0<px> 20.0<px> |> Chart.withArrow (ArrowArc 0.3)
        let curved = notes arced |> List.exists (fun (_, n) -> (sprintf "%A" n).Contains "CubicTo")
        Assert.True curved

    /// Panel letters in reading order, assigned when the figure is drawn, so a panel added after
    /// the call is lettered too.
    [<Fact>]
    let ``panel letters go in reading order and reach panels added later`` () =
        let one = Chart.line U.second U.metre t x
        let fig = (one |> Chart.withPanelLetters LetterParen |> Chart.beside one) |> Chart.above (one |> Chart.beside one)

        let letters =
            Chart.toScene surface fig
            |> annotated
            |> List.choose (fun (s, _) -> s.Data |> Array.tryPick (fun (k, v) -> if k = "letter" then Some v else None))

        Assert.Equal<string list>([ "(a)"; "(b)"; "(c)"; "(d)" ], letters)

        let upper =
            Chart.toScene surface (one |> Chart.withPanelLetters LetterUpper)
            |> annotated
            |> List.choose (fun (s, _) -> s.Data |> Array.tryPick (fun (k, v) -> if k = "letter" then Some v else None))

        Assert.Equal<string list>([ "A" ], upper)

// ======================================================================================
// D166 / D169 / D170 — stated ticks, the prefix, reversal
// ======================================================================================

module StatedTicks =

    let private nm = 1e-9<m>
    let private surface = Chart.size 480.0 300.0

    let private spectrum () =
        Chart.line U.metre U.one [| 400.0 * nm; 700.0 * nm |] [| 0.1; 0.9 |] |> Chart.withXDomain 400e-9 700e-9

    /// The x axis as it is drawn: its spec, through the same path as the renderer.
    let private xAxis (fig: Figure) =
        let m = TextMeasure.approx
        let shot = Chart.arrangeFor m surface fig
        (Chart.panelsIn m shot fig).[0].XAxis

    let private yAxis (fig: Figure) =
        let m = TextMeasure.approx
        let shot = Chart.arrangeFor m surface fig
        (Chart.panelsIn m shot fig).[0].YAxis

    /// G5 — a step stated in metres keeps the derived prefix: `λ [nm]`, 400, 450 … 700.
    [<Fact>]
    let ``a stated step keeps the prefix`` () =
        let axis = xAxis (spectrum () |> Chart.withXTicksEvery U.metre (50.0 * nm))
        Assert.Equal("nm", axis.UnitText)
        Assert.Equal<string[]>([| "400"; "450"; "500"; "550"; "600"; "650"; "700" |], axis.Ticks |> Array.map (fun t -> t.Label))

    /// The step may be stated in another unit of the dimension, and is converted.
    [<Fact>]
    let ``ticks in another unit of the dimension are converted`` () =
        // Every half minute on a seconds axis.
        let fig =
            Chart.line U.second U.metre [| 0.0<s>; 120.0<s> |] [| 0.0<m>; 1.0<m> |]
            |> Chart.withXTicksEvery NonSi.minute 0.5<minute>
            |> Chart.withXDomain 0.0 120.0

        Assert.Equal<float[]>([| 0.0; 30.0; 60.0; 90.0; 120.0 |], (xAxis fig).Ticks |> Array.map (fun t -> t.Value))

        // And an affine one: 0 °C and 10 °C on a kelvin axis.
        let warm =
            Chart.line U.second U.kelvin [| 0.0<s>; 1.0<s> |] [| 270.0<K>; 290.0<K> |]
            |> Chart.withYTicksAt NonSi.degreeCelsius [| 0.0<degC>; 10.0<degC> |]

        let values = (yAxis warm).Ticks |> Array.map (fun t -> t.Value)
        Assert.Equal(273.15, values.[0], 9)
        Assert.Equal(283.15, values.[1], 9)

    /// G6 — named ticks replace the numbers and not the title, and none is dropped.
    [<Fact>]
    let ``named ticks replace labels, keep the unit, and are all drawn`` () =
        let edge = 8.0e9</m>

        let fig =
            Chart.line (U.recip U.metre) U.one [| -edge; edge |] [| 0.0; 1.0 |]
            |> Chart.withXTicksNamed (U.recip U.metre) [| -edge, "X"; 0.0</m>, "Γ"; edge, "X" |]
            |> Chart.withXDomain (float -edge) (float edge)

        let axis = xAxis fig
        Assert.Equal<string[]>([| "X"; "Γ"; "X" |], axis.Ticks |> Array.map (fun t -> t.Label))
        Assert.False(System.String.IsNullOrEmpty axis.UnitText)

        // In a figure too narrow for three labels, still three.
        let svg = fig |> Chart.render SvgBackend.render (Chart.size 90.0 200.0)
        Assert.Equal(2, svg.Split(">X<").Length - 1)
        Assert.Contains(">Γ<", svg)

    /// G7 — a step stated in the wrong prefix is refused, with the step and the domain named,
    /// rather than drawn as a solid bar.
    [<Fact>]
    let ``a step that would make too many ticks is refused`` () =
        let e = Assert.ThrowsAny<exn>(fun () -> xAxis (spectrum () |> Chart.withXTicksEvery U.metre 50e-12<m>) |> ignore)
        Assert.Contains("5e-11", e.Message)
        Assert.Contains("4e-07", e.Message)

    /// G4 for ticks — a step in the wrong dimension is refused with both units named.
    [<Fact>]
    let ``a step in the wrong unit is refused`` () =
        let e = Assert.ThrowsAny<exn>(fun () -> xAxis (spectrum () |> Chart.withXTicksEvery U.second 1.0<s>) |> ignore)
        Assert.Contains("stated in s", e.Message)
        Assert.Contains("axis is in m", e.Message)

    /// D169 — a stated prefix wins over the one the domain selects.
    [<Fact>]
    let ``a stated prefix is the axis's`` () =
        let fig = Chart.line U.second U.metre [| 0.0<s>; 1.0<s> |] [| 0.0<m>; 0.002<m> |]
        Assert.Equal("mm", (yAxis fig).UnitText)
        Assert.Equal("m", (yAxis (fig |> Chart.withYPrefix Prefix.none)).UnitText)

    /// D170 — a reversed axis runs over the domain it would have had, backwards.
    [<Fact>]
    let ``a reversed axis keeps its derived domain`` () =
        let fig = Chart.line U.kelvin U.metre [| 290.0<K>; 276.0<K> |] [| 0.0<m>; 800.0<m> |]
        let forward = (Figure.unify fig.Panels.[0]).Layers.[0].Data |> Erase.seriesScales |> snd
        let reversed = (Figure.unify (fig |> Chart.withYReversed).Panels.[0]).Layers.[0].Data |> Erase.seriesScales |> snd
        let lo, hi = forward.Domain
        Assert.Equal((hi, lo), reversed.Domain)

    /// A map's two lengths are named by side; a length against time stays `x`.
    [<Fact>]
    let ``two lengths are named by their sides`` () =
        let map = Chart.line U.metre U.metre [| 0.0<m>; 1.0<m> |] [| 0.0<m>; 1.0<m> |]
        Assert.Equal("x", (xAxis map).Label)
        Assert.Equal("y", (yAxis map).Label)
        let fall = Chart.line U.second U.metre [| 0.0<s>; 1.0<s> |] [| 0.0<m>; 1.0<m> |]
        Assert.Equal("x", (yAxis fall).Label)

// ======================================================================================
// D167 / D168 — minor ticks, tick direction, the publication box
// ======================================================================================

module PublicationTicks =

    let private surface = Chart.size 480.0 300.0

    let private axes (fig: Figure) =
        let m = TextMeasure.approx
        let shot = Chart.arrangeFor m surface fig
        let v = (Chart.panelsIn m shot fig).[0]
        v.XAxis, v.YAxis, v.Plot

    let private line (lo: float) (hi: float) =
        Chart.line U.second U.metre [| lo * 1.0<s>; hi * 1.0<s> |] [| 0.0<m>; 1.0<m> |] |> Chart.withXDomain lo hi

    let private minorValues (spec: AxisSpec) (lo: float) (hi: float) =
        spec.Minor |> Array.map (fun p -> lo + float p * (hi - lo)) |> Array.map (fun v -> System.Math.Round(v, 9))

    /// D167 — a step reading 1 or 5 is cut in five, one reading 2 in four.
    [<Fact>]
    let ``minor ticks divide a major interval by its step`` () =
        let x1, _, _ = axes (line 0.0 4.0 |> Chart.withXTicksEvery U.second 1.0<s>)
        Assert.Equal<float[]>([| 0.2; 0.4; 0.6; 0.8 |], minorValues x1 0.0 4.0 |> Array.take 4)
        let x2, _, _ = axes (line 0.0 8.0 |> Chart.withXTicksEvery U.second 2.0<s>)
        Assert.Equal<float[]>([| 0.5; 1.0; 1.5 |], minorValues x2 0.0 8.0 |> Array.take 3)

    /// None on a major tick; 2 … 9 per decade on a log axis; none between named ticks.
    [<Fact>]
    let ``minor ticks avoid the majors, fill decades, and leave named ticks alone`` () =
        let x, _, _ = axes (line 0.0 4.0 |> Chart.withXTicksEvery U.second 1.0<s>)
        Assert.DoesNotContain(1.0, minorValues x 0.0 4.0)

        let log =
            Chart.line U.second U.metre [| 1.0<s>; 100.0<s> |] [| 0.0<m>; 1.0<m> |] |> Chart.withXLog |> Chart.withXDomain 1.0 100.0

        let lx, _, _ = axes log
        Assert.Equal(16, lx.Minor.Length)

        let named, _, _ = axes (line 0.0 4.0 |> Chart.withXTicksNamed U.second [| 0.0<s>, "a"; 3.0<s>, "b" |])
        Assert.Empty named.Minor

    /// G12 — an inward tick gives its length back to the plot, on each labelled side.
    [<Fact>]
    let ``inward ticks give their length back to the plot`` () =
        let outward = { Theme.publication with TickDirection = TickOut }
        let _, _, inPlot = axes (line 0.0 4.0 |> Chart.withTheme Theme.publication)
        let _, _, outPlot = axes (line 0.0 4.0 |> Chart.withTheme outward)
        let width (r: Rect<px>) = float (r.X1 - r.X0)
        let height (r: Rect<px>) = float (r.Y1 - r.Y0)
        let tick = float Theme.publication.TickLength
        Assert.Equal(width outPlot + tick, width inPlot, 6)
        Assert.Equal(height outPlot + tick, height inPlot, 6)

    /// D168 — the publication box: a frame, ticks on four sides, minor ticks. Counted in the
    /// tick path of the bottom axis.
    [<Fact>]
    let ``publication draws mirrored and minor ticks, modern neither`` () =
        let rec tickPaths (n: Node) =
            match n with
            | Annotated(s, inner) when s.Role = RoleAxis -> [ inner ]
            | Annotated(_, c)
            | Transform(_, c)
            | Clip(_, c)
            | Opacity(_, c)
            | Keyed(_, c) -> tickPaths c
            | Group ns -> ns |> Array.toList |> List.collect tickPaths
            | _ -> []

        let segments (theme: Theme) =
            let scene = Chart.toScene surface (line 0.0 4.0 |> Chart.withXTicksEvery U.second 1.0<s> |> Chart.withTheme theme)

            match tickPaths scene with
            | bottom :: _ ->
                let rec moves (n: Node) =
                    match n with
                    | Path(segs, Some _, None, _) -> segs |> Array.sumBy (function MoveTo _ -> 1 | _ -> 0)
                    | Group ns -> ns |> Array.sumBy moves
                    | _ -> 0

                moves bottom
            | [] -> 0

        // Five majors, and modern's spine; publication's frame replaces the spine, it adds 4 × 4
        // minors, and mirrors both onto the top.
        Assert.Equal(5 + 1, segments Theme.modern)
        Assert.Equal(2 * (5 + 16), segments Theme.publication)

    /// G13 — the publication box still passes the theme validator, and so does TikZ's.
    [<Fact>]
    let ``publication and tikz still validate`` () =
        Assert.True((Theme.validate Theme.publication).Ok)
        Assert.True((Theme.validate Theme.tikz).Ok)

// ======================================================================================
// D169 — tick label formats
// ======================================================================================

[<Properties(Arbitrary = [| typeof<Marey.Tests.Generators.MareyArbs> |], MaxTest = 200, Replay = Marey.Tests.Generators.Seed, QuietOnSuccess = true)>]
module TickFormats =

    open Marey.Tests.Generators

    let private surface = Chart.size 480.0 300.0

    let private yAxis (fig: Figure) =
        let m = TextMeasure.approx
        let shot = Chart.arrangeFor m surface fig
        (Chart.panelsIn m shot fig).[0].YAxis

    let private over (lo: float) (hi: float) =
        Chart.line U.second U.metre [| 0.0<s>; 1.0<s> |] [| lo * 1.0<m>; hi * 1.0<m> |] |> Chart.withYDomain lo hi

    let private number (label: string) =
        System.Double.Parse(label.Replace("−", "-").TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture)

    /// The law: a format other than a custom one writes the number it was given, to its own
    /// precision — fixed to its decimals, a percentage to a hundredth of its step, a scientific
    /// axis once its power is put back.
    [<Property>]
    let ``every format but a custom one writes the tick's value`` (LinearDomain(lo, hi)) =
        let check (format: TickFormat) (tolerance: AxisSpec -> float) (read: AxisSpec -> string -> float) =
            let axis = yAxis (over lo hi |> Chart.withYTickFormat format)
            axis.Ticks |> Array.forall (fun t -> abs (read axis t.Label - t.Value) <= tolerance axis)

        let exponentOf (axis: AxisSpec) =
            match axis.UnitText.IndexOf "·" with
            | -1 -> 0.0
            | _ -> log10 (axis.Ticks |> Array.map (fun t -> abs t.Value) |> Array.max) |> floor

        check (FormatFixed 3) (fun _ -> 5e-4) (fun _ l -> number l)
        && check FormatPercent (fun a -> 0.5 * abs (a.Ticks.[1].Value - a.Ticks.[0].Value)) (fun _ l -> number l / 100.0)
        && check FormatScientific (fun a -> 1e-9 * (a.Ticks |> Array.map (fun t -> abs t.Value) |> Array.max) + 1e-12) (fun a l -> number l * 10.0 ** exponentOf a)

    /// A scientific axis carries its power in the title, beside the unit, and writes small numbers.
    [<Fact>]
    let ``a scientific axis puts its power in the title`` () =
        let axis = yAxis (over 0.0 0.004 |> Chart.withYTickFormat FormatScientific)
        Assert.Equal("10⁻³·m", axis.UnitText)
        Assert.Equal<string[]>([| "0"; "1"; "2"; "3"; "4" |], axis.Ticks |> Array.map (fun t -> t.Label))

    /// The power is a prefix by another name, so pinning a prefix as well is refused.
    [<Fact>]
    let ``a scientific axis refuses a pinned prefix`` () =
        let fig = over 0.0 0.004 |> Chart.withYTickFormat FormatScientific |> Chart.withYPrefix Prefix.milli
        let e = Assert.ThrowsAny<exn>(fun () -> yAxis fig |> ignore)
        Assert.Contains("powers of ten", e.Message)

    /// A custom format sees the number the label would have shown: after the prefix.
    [<Fact>]
    let ``a custom format sees the rescaled number`` () =
        let axis = yAxis (over 0.0 0.004 |> Chart.withYTickFormat (TickFormat.custom (sprintf "<%g>")))
        Assert.Equal("mm", axis.UnitText)
        Assert.Contains("<4>", axis.Ticks |> Array.map (fun t -> t.Label))

    /// Named ticks keep their names whatever the format.
    [<Fact>]
    let ``named ticks keep their names under a format`` () =
        let axis =
            yAxis (over 0.0 4.0 |> Chart.withYTicksNamed U.metre [| 1.0<m>, "low"; 3.0<m>, "high" |] |> Chart.withYTickFormat FormatPercent)

        Assert.Equal<string[]>([| "low"; "high" |], axis.Ticks |> Array.map (fun t -> t.Label))

    /// A custom format compares by reference, so a figure carrying one is still a value: equal
    /// to itself, and not to a figure with another function.
    [<Fact>]
    let ``a figure with a custom format still has equality`` () =
        let format = TickFormat.custom string
        let a = (over 0.0 1.0 |> Chart.withYTickFormat format).Panels.[0].YAxis.Format
        let b = (over 0.0 1.0 |> Chart.withYTickFormat format).Panels.[0].YAxis.Format
        Assert.Equal(a, b)
        Assert.NotEqual(a, TickFormat.custom string)

// ======================================================================================
// D171 — legends
// ======================================================================================

module Legends =

    let private surface = Chart.size 480.0 300.0
    let private m = TextMeasure.approx

    let rec private annotated (n: Node) : (Semantics * Node) list =
        match n with
        | Annotated(s, inner) -> (s, inner) :: annotated inner
        | Group ns -> ns |> Array.toList |> List.collect annotated
        | Transform(_, c)
        | Clip(_, c)
        | Opacity(_, c)
        | Keyed(_, c) -> annotated c
        | _ -> []

    /// Every legend's box, in px with y down, read off its frame.
    let private legendBoxes (scene: Node) =
        scene
        |> annotated
        |> List.filter (fun (s, _) -> s.Role = RoleLegend)
        |> List.choose (fun (_, inner) ->
            match inner with
            | Group nodes ->
                match nodes.[0] with
                | Path(segs, _, _, _) ->
                    let pts = segs |> Array.choose (function MoveTo p | LineTo p -> Some p | _ -> None)
                    let xs = pts |> Array.map (fun p -> float p.X * float surface.Width)
                    let ys = pts |> Array.map (fun p -> (1.0 - float p.Y) * float surface.Height)
                    Some(Array.min xs, Array.min ys, Array.max xs, Array.max ys)
                | _ -> None
            | _ -> None)

    /// Three curves rising from the bottom left to the top right, which leave the top left empty.
    let private curves () =
        let ts = Array.init 31 (fun i -> float i * 20.0<s>)
        let curve (offset: float<K>) = ts |> Array.map (fun t -> 300.0<K> - offset + 60.0<K> * (t / (t + 150.0<s>)))

        Chart.line U.second U.kelvin ts (curve 0.0<K>)
        |> Chart.withName "a"
        |> Chart.addLine U.second U.kelvin ts (curve 4.0<K>)
        |> Chart.withName "b"
        |> Chart.addLine U.second U.kelvin ts (curve 10.0<K>)
        |> Chart.withName "c"

    let private plot (fig: Figure) =
        let shot = Chart.arrangeFor m surface fig
        (Chart.panelsIn m shot fig).[0].Plot

    /// G9 — the emptiest corner: the curves leave the top left empty, and the top right, the
    /// default, is where they end.
    [<Fact>]
    let ``LegendBest finds the empty corner`` () =
        let fig = curves () |> Chart.withLegend LegendBest
        let p = plot fig

        match legendBoxes (Chart.toScene surface fig) with
        | [ (x0, y0, x1, y1) ] ->
            Assert.True((x0 + x1) / 2.0 < float (p.X0 + p.X1) / 2.0, "left half")
            Assert.True((y0 + y1) / 2.0 < float (p.Y0 + p.Y1) / 2.0, "top half")
        | other -> failwithf "%d legends" other.Length

    /// G9 — with every corner covered, the legend goes outside on the right, and the plot gives
    /// up the room for it.
    [<Fact>]
    let ``LegendBest goes outside when every corner is covered`` () =
        let xs = Array.init 400 (fun i -> float (i % 20) * 1.0<s>)
        let ys = Array.init 400 (fun i -> float (i / 20) * 1.0<m>)
        let cloud = Chart.scatter U.second U.metre xs ys |> Chart.withName "everywhere"
        let best = cloud |> Chart.withLegend LegendBest
        let inside = plot (cloud |> Chart.withLegend LegendTopRight)
        let p = plot best
        Assert.True(p.X1 < inside.X1, "the plot gave up room")

        match legendBoxes (Chart.toScene surface best) with
        | [ (x0, _, _, _) ] -> Assert.True(x0 > float p.X1, "outside the plot")
        | other -> failwithf "%d legends" other.Length

    /// The two outside placements take their room beside and below the plot, not over it.
    [<Fact>]
    let ``outside legends are beside and below the plot`` () =
        let right = curves () |> Chart.withLegend LegendOutsideRight
        let below = curves () |> Chart.withLegend LegendOutsideBottom

        match legendBoxes (Chart.toScene surface right), legendBoxes (Chart.toScene surface below) with
        | [ (rx0, _, _, _) ], [ (_, by0, _, _) ] ->
            Assert.True(rx0 > float (plot right).X1)
            Assert.True(by0 > float (plot below).Y1)
        | a, b -> failwithf "%d and %d legends" a.Length b.Length

    /// Columns are filled down then across: three entries in three columns are one row, wider
    /// than tall.
    [<Fact>]
    let ``a legend in three columns is one row`` () =
        let fig = curves () |> Chart.withLegend LegendOutsideBottom |> Chart.withLegendColumns 3

        match legendBoxes (Chart.toScene surface fig) with
        | [ (x0, y0, x1, y1) ] -> Assert.True(x1 - x0 > 3.0 * (y1 - y0))
        | other -> failwithf "%d legends" other.Length

    /// One legend for a figure, with each entry once, and no panel's own.
    [<Fact>]
    let ``a figure legend replaces the panels' own`` () =
        let fig = curves () |> Chart.beside (curves ()) |> Chart.withFigureLegend FigureLegendRight
        let legends = Chart.toScene surface fig |> annotated |> List.filter (fun (s, _) -> s.Role = RoleLegend)
        Assert.Equal(1, legends.Length)
        Assert.Contains(("entries", "3"), (fst legends.Head).Data)

    /// G8 — over an animation, the legend is placed once: the frames' corners are scored together,
    /// so a legend does not jump from corner to corner, and the plot does not breathe.
    [<Fact>]
    let ``an animated legend is placed once`` () =
        let frame (k: float) =
            let ts = Array.init 31 (fun i -> float i * 20.0<s>)
            // The curves swing from rising to falling across the shot, which moves the empty corner.
            let curve (offset: float<K>) = ts |> Array.map (fun t -> 300.0<K> - offset + k * 60.0<K> * (t / (t + 150.0<s>)))

            Chart.line U.second U.kelvin ts (curve 0.0<K>)
            |> Chart.withName "a"
            |> Chart.addLine U.second U.kelvin ts (curve 4.0<K>)
            |> Chart.withName "b"
            |> Chart.withYDomain 230.0 370.0
            |> Chart.withLegend LegendBest

        let states = [| frame 1.0; frame -1.0 |]
        let shot = Chart.arrangeOver m surface states
        let boxes = states |> Array.map (fun s -> legendBoxes (Chart.toSceneIn m shot s))
        Assert.Equal<(float * float * float * float) list>(boxes.[0], boxes.[1])

        // And each frame alone would have put it elsewhere: the union is what holds it.
        let alone = states |> Array.map (fun s -> legendBoxes (Chart.toScene surface s))
        Assert.NotEqual<(float * float * float * float) list>(alone.[0], alone.[1])

// ======================================================================================
// D45 — the level set, and the categorical front door
// ======================================================================================

module LevelSets =

    /// First appearance, not sorted: a categorical axis usually arrives in an order that is
    /// itself information, and `control, treated, washout` is the case that makes the point.
    [<Fact>]
    let ``levels are the distinct keys in first-appearance order`` () =
        let ls =
            Levels.ofSeq [| "washout"; "control"; "washout"; "treated"; "control" |]

        Assert.Equal<string[]>([| "washout"; "control"; "treated" |], ls.Values)
        Assert.Equal<string[]>(ls.Values, ls.Labels)

    /// Determinism gate 6 reaches this file: the level order must come from the sequence, never
    /// from enumerating a set. Shuffling *repeats* while keeping first appearances fixed must
    /// not move a band.
    [<Property(MaxTest = 200, Replay = Marey.Tests.Generators.Seed, QuietOnSuccess = true)>]
    let ``level order depends only on first appearances`` (xs: int list) =
        let keys = xs |> List.map (fun x -> abs x % 7) |> Array.ofList
        let once = Levels.ofSeq keys
        let twice = Levels.ofSeq (Array.append keys keys)

        once.Values = twice.Values

    /// Sorting is one call away, and it sorts on the **key**, so renaming a level cannot
    /// reorder the bands.
    [<Fact>]
    let ``sorted orders by key`` () =
        let ls = Levels.ofSeq [| 30; 10; 20 |] |> Levels.sorted

        Assert.Equal<int[]>([| 10; 20; 30 |], ls.Values)
        Assert.Equal<string[]>([| "10"; "20"; "30" |], ls.Labels)

    /// A union key spells itself by case name, which is what `string` gives and what a reader
    /// expects on the axis.
    [<Fact>]
    let ``a union key is labelled by its case name`` () =
        let ls = Levels.ofSeq [| Choice1Of2 1; Choice2Of2 "x" |]

        Assert.Equal(2, Levels.count ls)

    /// `positions` is the x array of a categorical series, so a key outside the level set has
    /// no honest answer: level -1 is off the panel and clamping puts the mark on somebody
    /// else's band.
    [<Fact>]
    let ``a key outside the level set raises rather than landing somewhere`` () =
        let ls = Levels.ofSeq [| "a"; "b" |]

        let ex =
            Assert.Throws<System.Exception>(fun () -> Levels.positions ls [| "a"; "c" |] |> ignore)

        Assert.Contains("'c' is not one of the 2 levels", ex.Message)
        Assert.Contains("a, b", ex.Message)

module CategoricalFrontDoor =

    let private keys = [| "control"; "treated"; "placebo"; "washout" |]
    let private yields = [| 12.4<m>; 19.1<m>; 7.8<m>; 15.0<m> |]

    /// Phase 1's exit criterion through the API a user actually has: one named band per level,
    /// with nothing configured but the witness on y.
    [<Fact>]
    let ``Chart.Category.bar labels one band per level`` () =
        let svg = renderFig surface (Chart.Category.bar U.metre keys yields)

        for name in keys do
            Assert.Contains(">" + name + "<", svg)

    /// The y axis is dimensioned as it is everywhere else. The categorical door does not cost
    /// the units story on the other axis.
    [<Fact>]
    let ``the y axis still labels itself from the witness`` () =
        let svg = renderFig surface (Chart.Category.bar U.metre keys yields)
        Assert.Contains(">x [m]<", svg)

    /// `Axes.defaultLabel` would print a bare `x` under bands already named `control`,
    /// `treated` — two meanings of `x` in one figure. It is suppressed, and a real name still
    /// goes back.
    [<Fact>]
    let ``a categorical axis is titled by its bands, not by a conventional symbol`` () =
        let plain = renderFig surface (Chart.Category.bar U.metre keys yields)
        Assert.DoesNotContain(">x<", plain)

        let named =
            renderFig surface (Chart.Category.bar U.metre keys yields |> Chart.withXLabel "treatment")

        Assert.Contains(">treatment<", named)

    /// **A bar is a length from zero.** Data spanning 7.8 … 19.1 on a 7.2 … 19.7 axis draws a
    /// 50:1 picture of a 2.4:1 ratio, which is the mark type lying rather than a styling
    /// preference. Both bar doors widen the domain to reach the baseline.
    [<Fact>]
    let ``a bar chart's y domain reaches zero`` () =
        let categorical =
            Chart.Category.bar U.metre keys yields |> fun f -> f.Panels.[0].Layers.[0].Data

        let _, y = Erase.seriesScales categorical
        Assert.Equal(0.0, fst y.Domain, 12)

        let continuous =
            Chart.bar U.second U.metre [| 1.0<s>; 2.0<s>; 3.0<s> |] [| 12.4<m>; 19.1<m>; 7.8<m> |]

        let _, cy = Erase.seriesScales continuous.Panels.[0].Layers.[0].Data
        Assert.Equal(0.0, fst cy.Domain, 12)

    /// Zero enters unpadded and only on the side it is missing from: all-negative data keeps
    /// its baseline at the top of the bars rather than gaining a spurious positive headroom,
    /// and data straddling zero is untouched.
    [<Fact>]
    let ``zero enters only on the side it is missing from`` () =
        let negative =
            Chart.Category.bar U.metre [| "a"; "b" |] [| -4.0<m>; -9.0<m> |]

        let _, y = Erase.seriesScales negative.Panels.[0].Layers.[0].Data
        Assert.Equal(0.0, snd y.Domain, 12)
        Assert.True(fst y.Domain < -9.0, sprintf "lower end %f should still be padded" (fst y.Domain))

        let straddling =
            Chart.Category.bar U.metre [| "a"; "b" |] [| -4.0<m>; 9.0<m> |]

        let _, sy = Erase.seriesScales straddling.Panels.[0].Layers.[0].Data
        Assert.True(fst sy.Domain < -4.0 && snd sy.Domain > 9.0)

    /// Repeated keys are two bars in one band, not a sum. Aggregation is a stat's job, and
    /// doing it here silently would be the library deciding what the data means.
    [<Fact>]
    let ``a repeated key is two marks in one band, not an aggregate`` () =
        let fig = Chart.Category.bar U.metre [| "a"; "b"; "a" |] [| 1.0<m>; 2.0<m>; 3.0<m> |]
        let x, _ = Erase.seriesScales (fig.Panels.[0].Layers.[0].Data)

        Assert.Equal(ScaleKind.Ordinal [| "a"; "b" |], x.Kind)

        let xs =
            Erase.projectXY fig.Panels.[0].Layers.[0].Data |> Array.map (fun p -> float p.X)

        // Two marks share the first band; the third sits in the second.
        Assert.Equal(xs.[0], xs.[2], 12)
        Assert.NotEqual(xs.[0], xs.[1])

// ======================================================================================
// §13 — layout invariants
// ======================================================================================

module LayoutInvariants =

    let private sizes =
        [ "800x500", Chart.size 800.0 500.0
          "300x260", Chart.size 300.0 260.0
          "1600x900", Chart.size 1600.0 900.0
          // Deliberately cramped: the case where tick labels cannot all fit and the
          // collision ladder has to do real work.
          "220x180", Chart.size 220.0 180.0 ]

    let private themes =
        [ "publication", Theme.publication
          "dark", Theme.dark
          "notebook", Theme.notebook
          "presentation", Theme.presentation ]

    let private busyFigure () =
        baseFigure ()
        |> Chart.withName "model"
        |> Chart.addScatter U.second U.metre fallT fallX
        |> Chart.withName "measured"
        |> Chart.withTitle "Free fall"
        |> Chart.withLegend LegendTopRight

    /// §13: "labels inside bounds". Panel space is nominally [0,1]²; a small tolerance
    /// allows for the axis gutter, where tick labels legitimately sit just outside the plot
    /// area but still inside the figure.
    [<Fact>]
    let ``every text label sits inside the figure at every size and theme`` () =
        for (sizeName, extent) in sizes do
            for (themeName, theme) in themes do
                let scene = Chart.toScene extent (busyFigure () |> Chart.withTheme theme)

                let strays =
                    textNodes scene
                    |> List.filter (fun t ->
                        let x = float t.At.X
                        let y = float t.At.Y
                        x < -0.05 || x > 1.05 || y < -0.05 || y > 1.05)

                Assert.True(
                    strays.IsEmpty,
                    sprintf
                        "%s / %s: %d label(s) outside the figure, e.g. %A"
                        sizeName
                        themeName
                        strays.Length
                        (strays |> List.truncate 3 |> List.map (fun t -> t.Text, float t.At.X, float t.At.Y))
                )

    /// §13: "no overlapping panels". Two panels stacked vertically must occupy disjoint
    /// regions — checked through the `Clip` nodes the compiler emits per panel, which are
    /// the authoritative statement of where a panel actually is.
    [<Fact>]
    let ``panels in a multi-panel figure do not overlap`` () =
        let twoPanel =
            baseFigure ()
            |> Chart.withPanelTitle "left"
            |> Chart.beside (Chart.line U.second U.metre fallT (Array.rev fallX) |> Chart.withPanelTitle "right")

        let scene = Chart.toScene surface twoPanel

        // A panel's `Clip` path is expressed in *panel-local* space; the enclosing `Transform`
        // is what places it in the figure. Comparing the raw clip coordinates therefore reports
        // every panel as (0,0,1,1) and every pair as overlapping — so the accumulated transform
        // has to be carried down the tree.
        //
        // Being able to do this at all is D1 paying off: `Transform` holds an `Affine` value, so
        // the test can compose it. With the spike's closure-valued transform there would be no
        // way to compute a panel's figure-space bounds from outside the renderer.
        let rec collect (ctm: Affine<nd, nd>) (node: Node) =
            match node with
            | Clip(segs, child) ->
                let pts =
                    segs
                    |> Array.choose (function
                        | MoveTo p
                        | LineTo p -> Some p
                        | CubicTo(_, _, p) -> Some p
                        | ClosePath -> None)
                    |> Array.map (Affine.apply ctm)

                let here =
                    if pts.Length = 0 then
                        []
                    else
                        let xs = pts |> Array.map (fun p -> float p.X)
                        let ys = pts |> Array.map (fun p -> float p.Y)
                        [ Array.min xs, Array.min ys, Array.max xs, Array.max ys ]

                here @ collect ctm child

            | Transform(a, child) -> collect (Affine.compose a ctm) child
            | Group ns -> ns |> Array.toList |> List.collect (collect ctm)
            | Opacity(_, child)
            | Keyed(_, child)
            | Annotated(_, child) -> collect ctm child
            | Path _
            | Text _
            | Markers _
            | Raster _ -> []

        let clipBounds = collect Affine.identity scene

        Assert.True(clipBounds.Length >= 2, sprintf "expected >= 2 panel clips, got %d" clipBounds.Length)

        let overlaps (ax0, ay0, ax1, ay1) (bx0, by0, bx1, by1) =
            ax0 < bx1 - 1e-9 && bx0 < ax1 - 1e-9 && ay0 < by1 - 1e-9 && by0 < ay1 - 1e-9

        let pairs =
            [ for i in 0 .. clipBounds.Length - 1 do
                  for j in i + 1 .. clipBounds.Length - 1 do
                      if overlaps clipBounds.[i] clipBounds.[j] then
                          yield (clipBounds.[i], clipBounds.[j]) ]

        Assert.True(pairs.IsEmpty, sprintf "overlapping panel regions: %A" pairs)

    /// A figure must compile at any plausible aspect ratio without throwing. Extreme ratios
    /// are where a measure-arrange pass divides by a track that shrank to zero.
    [<Property(MaxTest = 60)>]
    let ``compiles at any plausible surface size`` (w: int) (h: int) =
        // Map arbitrary ints into a plausible pixel range rather than filtering them out, so
        // no generated case is discarded and the shrinker still has room to work.
        // `v % 1880` is safe for Int32.MinValue where `abs v` would not be.
        let toPx v = 120 + abs (v % 1880)
        let extent = Chart.size (float (toPx w)) (float (toPx h))
        Scene.count (Chart.toScene extent (busyFigure ())) > 0

// ======================================================================================
// §5.1 — batching survives the whole compile pipeline
// ======================================================================================

module Batching =

    [<Fact>]
    let ``a 50k-point scatter compiles to exactly one Markers node`` () =
        let n = 50000
        let x = Array.init n (fun i -> float i / float n * 10.0<s>)
        let y = x |> Array.map (fun t -> sin (float t) * 1.0<m>)

        let scene = Chart.toScene surface (Chart.scatter U.second U.metre x y)

        let markerNodes =
            scene
            |> Scene.fold
                (fun acc node ->
                    match node with
                    | Markers _ -> acc + 1
                    | _ -> acc)
                0

        Assert.Equal(1, markerNodes)

        // The whole figure stays small while still accounting for every point. The gap
        // between these two numbers is precisely what §5.1 buys.
        Assert.True(Scene.count scene < 200, sprintf "figure exploded to %d nodes" (Scene.count scene))
        Assert.True(Scene.primitiveCount scene >= n)

// ======================================================================================
// §1.1 — combinators are pure. The Plotly.NET flaw, by name.
// ======================================================================================

module Immutability =

    [<Fact>]
    let ``styling a figure does not mutate the original`` () =
        let original = baseFigure ()
        let before = renderFig surface original

        let derived =
            original
            |> Chart.withTitle "MUTATED"
            |> Chart.withColor (Color.ofHex "#ff0000")
            |> Chart.withMarker MarkDiamond

        let after = renderFig surface original

        Assert.Equal(before, after)
        Assert.DoesNotContain("MUTATED", after)
        Assert.NotEqual<string>(before, renderFig surface derived)

// ======================================================================================
// Determinism through the full pipeline — the property golden images depend on
// ======================================================================================

module Determinism =

    [<Fact>]
    let ``the whole compile-and-render pipeline is deterministic`` () =
        let fig = baseFigure () |> Chart.withTitle "determinism"
        Assert.Equal(renderFig surface fig, renderFig surface fig)

    /// D15 / the SVG writer's contract, but exercised through Plot: a comma-decimal locale
    /// must not change one byte. Tick labels are generated by Plot, so this covers a code
    /// path the backend-only culture test cannot reach.
    [<Fact>]
    let ``a comma-decimal culture produces byte-identical output`` () =
        let fig = baseFigure () |> Chart.withTitle "culture"
        let previous = System.Threading.Thread.CurrentThread.CurrentCulture

        try
            System.Threading.Thread.CurrentThread.CurrentCulture <- System.Globalization.CultureInfo "en-US"
            let invariantish = renderFig surface fig

            System.Threading.Thread.CurrentThread.CurrentCulture <- System.Globalization.CultureInfo "de-DE"
            let german = renderFig surface fig

            Assert.Equal(invariantish, german)
            Assert.DoesNotContain("1,5", german)
        finally
            System.Threading.Thread.CurrentThread.CurrentCulture <- previous

// ======================================================================================
// D60 — the output surface is the renderer's shape, not `string`
//
// Every function-shaped entry point named `string` in its signature until M2.5. That is a
// claim about the *artifact* made by a function that only forwards it, and a raster backend
// does not fit through it: a PNG has no UTF-8 encoding and a PDF's is a lie.
//
// These tests are the ones that would have caught the two ways the generalisation could go
// wrong — inference silently pinning `render` to `string` at a call site, and `toFileBytes`
// acquiring an encoding on the way to disk.
// ======================================================================================

module ByteShapedOutput =

    open System.IO

    /// A byte-producing renderer of the shape a raster backend has. It ignores the scene's
    /// content on purpose: what is under test is the *path*, not a rasteriser.
    let private byteRenderer (_: Extent<px>) (node: Node) : byte[] =
        let count = node |> Scene.fold (fun acc _ -> acc + 1) 0
        // A BOM's bytes, deliberately, followed by something that is not valid UTF-8. If the
        // write path ever grows an encoding, one of the two halves will not survive it.
        [| 0xEFuy; 0xBBuy; 0xBFuy; 0xFFuy; 0xFEuy; byte count |]

    /// The generalisation itself. `render` returns what the renderer returns.
    [<Fact>]
    let ``render returns bytes for a byte-producing renderer`` () =
        let bytes = baseFigure () |> Chart.render byteRenderer surface
        Assert.NotEmpty bytes
        Assert.Equal(0xEFuy, bytes.[0])

    /// The inference risk D60 weighed against the general form, exercised rather than argued.
    /// A generic `render` must still pin to `string` from `SvgBackend.render` alone, with no
    /// annotation at the call site and no annotation on the binding — which is how every
    /// example in the repository and every snippet in the guide calls it.
    [<Fact>]
    let ``render still infers string from the renderer alone`` () =
        let svg = baseFigure () |> Chart.render SvgBackend.render surface
        // `String.length` only compiles if `svg` was inferred as `string`, not left generic
        // and not defaulted to `obj`.
        Assert.True(String.length svg > 0)
        Assert.StartsWith("<svg", svg)

    /// Both entry points must reach the same artifact, or the byte path is a second
    /// implementation of the render pipeline rather than a second exit from it.
    [<Fact>]
    let ``toFileBytes writes exactly what render produced`` () =
        let fig = baseFigure ()
        let path = Path.Combine(Path.GetTempPath(), "marey-d60-" + string (System.Guid.NewGuid()) + ".bin")

        try
            fig |> Chart.toFileBytes byteRenderer path surface
            Assert.Equal<byte[]>(fig |> Chart.render byteRenderer surface, File.ReadAllBytes path)
        finally
            if File.Exists path then File.Delete path

    /// The failure this rules out is the one that produces a file no decoder opens: a
    /// three-byte BOM prepended to a PNG signature. `WriteAllBytes` has no encoding, and this
    /// asserts that nothing on the way to it acquired one.
    [<Fact>]
    let ``toFileBytes adds no encoding preamble and alters no byte`` () =
        let path = Path.Combine(Path.GetTempPath(), "marey-d60-raw-" + string (System.Guid.NewGuid()) + ".bin")

        try
            baseFigure () |> Chart.toFileBytes byteRenderer path surface
            let onDisk = File.ReadAllBytes path
            // 0xFF 0xFE is not valid UTF-8 in any position, so a round trip through any text
            // encoding would have replaced it with U+FFFD's bytes rather than preserved it.
            Assert.Equal<byte[]>([| 0xEFuy; 0xBBuy; 0xBFuy; 0xFFuy; 0xFEuy |], onDisk.[0..4])
            Assert.Equal(6, onDisk.Length)
        finally
            if File.Exists path then File.Delete path

    /// `toFile` is unchanged and stays string-typed, which is the other half of D60: the
    /// existing path keeps its encoding guarantee (UTF-8, no BOM — these files are golden
    /// masters) and does not become generic over something it has to branch on.
    [<Fact>]
    let ``toFile still writes UTF-8 without a byte-order mark`` () =
        let path = Path.Combine(Path.GetTempPath(), "marey-d60-text-" + string (System.Guid.NewGuid()) + ".svg")

        try
            baseFigure () |> Chart.toFile SvgBackend.render path surface
            let onDisk = File.ReadAllBytes path
            Assert.NotEqual<byte[]>([| 0xEFuy; 0xBBuy; 0xBFuy |], onDisk.[0..2])
            Assert.Equal('<', char onDisk.[0])
        finally
            if File.Exists path then File.Delete path

// ======================================================================================
// §9 — the colour system, which the design says is embarrassing to get wrong
// ======================================================================================

module Palettes =

    /// §9 asks for "a validator that checks contrast and CVD-safety, because a physics
    /// library that renders fields in jet is embarrassing". Lightness monotonicity is the
    /// machine-checkable core of that: jet fails it, viridis and friends pass.
    [<Theory>]
    [<InlineData "viridis">]
    [<InlineData "cividis">]
    [<InlineData "magma">]
    let ``sequential ramps are lightness-monotone`` (name: string) =
        let ramp =
            match name with
            | "viridis" -> Palette.viridis
            | "cividis" -> Palette.cividis
            | _ -> Palette.magma

        // A small tolerance: the ramps are published as discrete stops, so interpolation
        // between them can wobble by a hair without the ramp being non-monotone in any way a
        // reader could see.
        Assert.True(Palette.isLightnessMonotone 1e-3 ramp, sprintf "%s is not lightness-monotone" name)

    /// The counter-example the design names: "a physics library that renders fields in jet is
    /// embarrassing". Jet must *fail* the same check the good ramps pass, or the validator is
    /// not actually testing anything.
    [<Fact>]
    let ``jet fails the lightness-monotonicity check that viridis passes`` () =
        let jet =
            { Name = "jet"
              Stops =
                [| 0.00, Color.ofHex "#00007F"
                   0.125, Color.ofHex "#0000FF"
                   0.375, Color.ofHex "#00FFFF"
                   0.625, Color.ofHex "#FFFF00"
                   0.875, Color.ofHex "#FF0000"
                   1.00, Color.ofHex "#7F0000" |] }

        Assert.False(Palette.isLightnessMonotone 1e-3 jet, "jet should not be lightness-monotone")
        Assert.True(Palette.isLightnessMonotone 1e-3 Palette.viridis)

    [<Fact>]
    let ``the Okabe-Ito set stays distinguishable under all three dichromacies`` () =
        let report = Palette.validate Color.white Palette.okabeIto

        // Separation only. `report.Ok` also folds in the contrast floor, which Okabe-Ito
        // genuinely fails on white — see the test below.
        Assert.True(
            report.WorstSeparation.Minimum >= Palette.SeparationFloor,
            sprintf
                "Okabe-Ito separation fell to %.4f under %s (colours %d and %d), below the %.3f floor"
                report.WorstSeparation.Minimum
                report.WorstSeparation.Vision
                report.WorstSeparation.IndexA
                report.WorstSeparation.IndexB
                Palette.SeparationFloor
        )

    /// **A known, deliberate limitation, pinned so it cannot drift silently.**
    ///
    /// Okabe–Ito is the canonical colour-blind-safe qualitative palette and it is what §9 asks
    /// for — but two of its eight colours are very light, and against a white background its
    /// yellow lands around 1.3:1, far below WCAG's 3:1 for graphical objects.
    ///
    /// The palette is not "fixed", because a corrected Okabe–Ito is not Okabe–Ito; the light
    /// series order demotes the offenders instead. This test states the trade-off out loud so
    /// that a future change to either the palette or the contrast floor has to confront it.
    [<Fact>]
    let ``Okabe-Ito fails the contrast floor on white, and that is a known trade-off`` () =
        let report = Palette.validate Color.white Palette.okabeIto

        Assert.True(
            report.MinContrast < Palette.ContrastFloor,
            "Okabe-Ito now clears the contrast floor on white. If the palette or the floor "
            + "changed deliberately, delete this test; if not, something regressed."
        )

        // Whatever else is true, the offender must stay the pale one — yellow or sky blue.
        Assert.Contains(report.MinContrastIndex, [| 2; 4 |])

    /// A diverging palette for signed fields must be neutral in the middle, or zero stops
    /// looking like zero.
    [<Fact>]
    let ``the diverging ramp is neutral at its midpoint`` () =
        let mid = Ramp.at Palette.diverging 0.5
        let lch = Color.toOklch mid
        Assert.True(lch.C < 0.03, sprintf "diverging midpoint has chroma %.4f, expected near zero" lch.C)

/// The two things the `modern` default rests on, both of which fail silently.
module Themes =

    /// §9 asks for a validator. A validator only earns its place if the themes the library
    /// itself ships are run through it — a shipped theme that fails its own contrast floor is
    /// the exact failure this test exists to make impossible.
    ///
    /// It has already done work: `Theme.modern` was first drafted with a pale `#C8CDD4` spine,
    /// which is the conventional way to make chart furniture recede. That measures about 1.5:1
    /// on white and fails here. The theme now recedes by getting thinner rather than paler.
    [<Fact>]
    let ``every shipped theme passes its own validator`` () =
        for theme in Theme.all do
            let r = Theme.validate theme

            Assert.True(
                r.Ok,
                sprintf
                    "Theme.%s fails validation: text %.2f:1 (needs 7), axis %.2f:1 (needs %.1f)"
                    r.Theme
                    r.TextContrast
                    r.AxisContrast
                    Palette.ContrastFloor
            )

    /// `Chart.withTheme` recolours a layer **only if that layer's style still equals the outgoing
    /// theme's default for its index** — which is what lets it retheme a figure without discarding
    /// a colour set by hand. The test is meaningful only while the theme a `Figure` is born with
    /// and the theme `Chart`'s constructors style their layers against are the same value.
    ///
    /// Those lived in two files and agreed by coincidence until `Theme.initial` was introduced.
    /// Had they drifted, nothing would have crashed: `withTheme` would simply have stopped
    /// working, silently, leaving every figure in the default palette.
    [<Fact>]
    let ``a fresh figure's layer styles match its own theme, so withTheme can recolour it`` () =
        let fig = Chart.line U.second U.metre fallT fallX
        Assert.Equal(Theme.initial.Name, fig.Theme.Name)

        let layer = fig.Panels.[0].Layers.[0]
        Assert.Equal(Geom.styleFor fig.Theme 0, layer.Style)

        // The consequence, end to end: a retheme must actually reach the paint.
        let dark = fig |> Chart.withTheme Theme.dark
        Assert.Equal(Geom.styleFor Theme.dark 0, dark.Panels.[0].Layers.[0].Style)
        Assert.NotEqual(layer.Style, dark.Panels.[0].Layers.[0].Style)

// ======================================================================================
// M2 phase 2 / D46 / D49 — non-positional channels, the legend, and the colorbar
// ======================================================================================

/// The phase's three exit criteria, plus the claims D49 makes about where a colorbar goes.
///
/// The first two are counts of IR nodes, because that is what D46 decided: a categorical
/// channel is one batch per level and a continuous one is one batch per *quantised step*, and
/// both of those are §5.1's batching surviving rather than being defeated. The third is a
/// golden in both backends, and it lives in `GoldenTests`/`TikzGoldenTests` — it is there
/// rather than here because the failure it guards against (a gradient degrading to one flat
/// block) is invisible to every assertion that is not a picture.
module Channels =

    let private keys =
        Array.init 30 (fun i -> [| "control"; "treated"; "washout" |].[i % 3])

    let private xs = Array.init 30 (fun i -> float i * 1.0<s>)
    let private ys = Array.init 30 (fun i -> (2.0 + sin (float i)) * 1.0<m>)

    let private temperatures = Array.init 30 (fun i -> (280.0 + float i) * 1.0<K>)

    let private scatter () = Chart.scatter U.second U.metre xs ys

    /// The `Markers` nodes belonging to the *data*, which is not every `Markers` node in the
    /// scene: a legend swatch is one too, and counting those would make a three-level scatter
    /// look like six batches.
    let private dataMarkers (fig: Figure) =
        Chart.toScene surface fig
        |> Scene.fold
            (fun acc n ->
                match n with
                | Annotated(sem, body) when sem.Role = RoleSeries ->
                    acc
                    + Scene.fold
                        (fun k node ->
                            match node with
                            | Markers _ -> k + 1
                            | _ -> k)
                        0
                        body
                | _ -> acc)
            0

    /// Every `Markers` node in the scene, wherever it sits. The bound in D46 is stated over the
    /// whole figure, so the assertion should be too.
    let private allMarkers (node: Node) =
        Scene.fold
            (fun k n ->
                match n with
                | Markers _ -> k + 1
                | _ -> k)
            0
            node

    let private markerFills (node: Node) =
        Scene.fold
            (fun acc n ->
                match n with
                | Markers b ->
                    match b.Fill with
                    | Some(Solid c) -> Set.add (Color.toHex c) acc
                    | _ -> acc
                | _ -> acc)
            Set.empty
            node

    let private annotationOf (role: SemanticRole) (node: Node) =
        Scene.fold
            (fun acc n ->
                match n with
                | Annotated(sem, _) when sem.Role = role -> Some sem
                | _ -> acc)
            None
            node

    // ---- Exit criterion 1: the categorical half ------------------------------------------

    /// **Phase 2's first exit criterion**, through the front door and counted with `Scene.count`'s
    /// own oracle: three levels, thirty points, three batches. Not thirty markers.
    [<Fact>]
    let ``a three-level scatter renders three batches, not thirty marks`` () =
        Assert.Equal(3, dataMarkers (scatter () |> Chart.colorByKey keys))

    /// The colour-blind-safe form is still three, because both channels read the same level
    /// number. A ninth of the batches is not the point — the point is that redundant encoding
    /// costs nothing, so there is no reason not to use it.
    [<Fact>]
    let ``colour and shape over one key stays three batches`` () =
        let fig = scatter () |> Chart.colorByKey keys |> Chart.shapeByKey keys
        Assert.Equal(3, dataMarkers fig)

        let shapes =
            Chart.toScene surface fig
            |> Scene.fold
                (fun acc n ->
                    match n with
                    | Markers b -> Set.add (sprintf "%A" b.Shape) acc
                    | _ -> acc)
                Set.empty

        Assert.True(shapes.Count >= 3, sprintf "expected three mark shapes, got %A" shapes)

    /// **Phase 2's other half of the first criterion**: the colours a level channel assigns are
    /// separable, to the standard `Theme.validate` holds a palette to — under protanopia,
    /// deuteranopia and tritanopia as well as normal vision.
    ///
    /// This is a real risk rather than a formality. The level colours come off the theme's
    /// categorical palette by index, and a channel with more levels than the palette has
    /// entries would wrap it — assigning two levels the same colour and saying nothing.
    [<Fact>]
    let ``the colours a categorical channel assigns pass the palette validator`` () =
        let fig = scatter () |> Chart.colorByKey keys

        let colors =
            match fig.Panels.[0].Layers.[0].Channels.[0].Map with
            | ColorByLevel(_, cs) -> cs
            | _ -> failwith "expected a categorical colour channel"

        let report = Palette.validate Theme.initial.PanelBackground colors

        Assert.True(
            report.Ok,
            sprintf
                "level colours separate by only %.3f under %s (needs %.3f)"
                report.WorstSeparation.Minimum
                report.WorstSeparation.Vision
                Palette.SeparationFloor
        )

    /// D49 — the legend enumerates the *levels*, not the series. The series name is already on
    /// the panel; what the reader cannot otherwise recover is which colour is which level.
    [<Fact>]
    let ``a categorical channel puts one legend entry per level`` () =
        let names =
            Chart.toScene surface (scatter () |> Chart.withName "yield" |> Chart.colorByKey keys)
            |> Scene.fold
                (fun acc n ->
                    match n with
                    | Annotated(sem, _) when sem.Role = RoleOther "legend-entry" -> sem.Label :: acc
                    | _ -> acc)
                []
            |> List.rev

        Assert.Equal<string list>([ "control"; "treated"; "washout" ], names)

    // ---- Exit criterion 2: the continuous half -------------------------------------------

    /// **D46's bound, end to end.** A hundred thousand points, a continuous colour channel, and
    /// at most 257 `Markers` nodes in the whole figure — 256 steps plus the one a legend could
    /// contribute. The assertion that fails loudly if anybody ever reaches for one batch per
    /// point, which is the thing the decision refused.
    [<Fact>]
    let ``a hundred thousand points with a continuous colour channel stay under 257 batches`` () =
        let n = 100_000
        let rng = System.Random 5
        let bx = Array.init n (fun _ -> rng.NextDouble() * 10.0 * 1.0<s>)
        let by = Array.init n (fun _ -> rng.NextDouble() * 10.0 * 1.0<m>)
        let t = Array.init n (fun i -> (280.0 + 25.0 * float i / float n) * 1.0<K>)

        let scene =
            Chart.toScene surface (Chart.scatter U.second U.metre bx by |> Chart.colorBy U.kelvin t)

        let markers = allMarkers scene
        Assert.True(markers <= 257, sprintf "%d Markers nodes" markers)

        // And every point is still drawn: a bound met by dropping data is not a bound met.
        let drawn =
            Scene.fold
                (fun acc node ->
                    match node with
                    | Markers b -> acc + b.Points.Length
                    | _ -> acc)
                0
                scene

        Assert.Equal(n, drawn)

    // ---- D49: the colorbar -----------------------------------------------------------------

    let private colorbarFigure () =
        scatter () |> Chart.colorBy U.kelvin temperatures |> Chart.withColorLabel "T"

    /// The whole thesis in a third place, after the axis and the legend: the bar is titled from
    /// the witness, and nothing was configured but the quantity's name.
    [<Fact>]
    let ``a continuous colour channel gets a colorbar titled from its witness`` () =
        let scene = Chart.toScene surface (colorbarFigure ())

        match annotationOf (RoleOther "colorbar") scene with
        | None -> failwith "no colorbar was compiled"
        | Some sem ->
            Assert.Equal("T [K]", sem.Label)
            Assert.Contains(("unit", "K"), sem.Data)

        let svg = renderFig surface (colorbarFigure ())
        Assert.Contains("T [K]", svg)
        Assert.Contains(">280<", svg)
        Assert.Contains(">305<", svg)

    /// Unnamed, the bar reads its name off the *dimension*, exactly as an axis does — a kelvin
    /// channel is `T`. What it must not do is borrow an axis's positional fallback: a bar
    /// labelled `y [K]` would be naming an axis that is not there.
    [<Fact>]
    let ``an unnamed colour channel takes its name from the dimension, not from an axis`` () =
        let named =
            Chart.toScene surface (scatter () |> Chart.colorBy U.kelvin temperatures)

        match annotationOf (RoleOther "colorbar") named with
        | None -> failwith "no colorbar was compiled"
        | Some sem -> Assert.Equal("T [K]", sem.Label)

        // A dimensionless channel has no conventional symbol, and prints none.
        let plain =
            Chart.toScene surface (scatter () |> Chart.colorBy U.one (Array.init 30 float))

        match annotationOf (RoleOther "colorbar") plain with
        | None -> failwith "no colorbar was compiled"
        | Some sem -> Assert.Equal("", sem.Label)

    /// D49's placement claim, measured. The bar is one more `Track.Content` column, so it takes
    /// its width out of the plot — and the plot survives it with its tick labels intact, which
    /// is the half of the claim that could have gone wrong.
    [<Fact>]
    let ``the colorbar takes a column from the plot and the x axis keeps its ticks`` () =
        let withoutBar = scatter ()
        let withBar = colorbarFigure ()

        let plotWidth (fig: Figure) =
            let scene = Chart.toScene surface fig
            ignore scene
            // The panel frame's extent is the plot area; read it from the gridline-free
            // background rect the panel compiles first.
            Scene.fold
                (fun acc n ->
                    match n with
                    | Annotated(sem, _) when sem.Role = RoleOther "panel" -> acc
                    | Path(segs, _, _, _) ->
                        let xs =
                            segs
                            |> Array.choose (fun seg ->
                                match seg with
                                | MoveTo p
                                | LineTo p -> Some(float p.X)
                                | _ -> None)

                        if xs.Length = 0 then acc else max acc (Array.max xs)
                    | _ -> acc)
                0.0
                (Chart.toScene surface fig)

        // Everything drawn stays inside the surface in both cases; the bar does not hang off.
        Assert.True(plotWidth withBar <= 1.0 + 1e-9, sprintf "%f" (plotWidth withBar))
        Assert.True(plotWidth withoutBar <= 1.0 + 1e-9)

        // The x axis still has its full tick set: the column came out of the plot's width, and
        // the tick plan was recomputed against what was left.
        let ticksOf (fig: Figure) =
            textNodes (Chart.toScene surface fig)
            |> List.filter (fun t -> t.Text = "0" || t.Text = "10" || t.Text = "20")
            |> List.length

        Assert.Equal(ticksOf withoutBar, ticksOf withBar)

    /// A figure with no continuous colour channel has no colorbar and no column for one. This
    /// is what keeps every existing golden byte-identical, and it is worth an assertion rather
    /// than a hope.
    [<Fact>]
    let ``a figure without a continuous colour channel is unchanged`` () =
        let scene = Chart.toScene surface (scatter () |> Chart.colorByKey keys)
        Assert.True((annotationOf (RoleOther "colorbar") scene).IsNone)

    /// D49's agreement claim: the bar is drawn from the same ramp call as the marks, so every
    /// mark's colour is one of the strips'. Two implementations that merely resemble each other
    /// would fail this on the first change to either.
    [<Fact>]
    let ``every mark colour appears in the colorbar`` () =
        let scene = Chart.toScene surface (colorbarFigure ())

        let stripFills =
            Scene.fold
                (fun acc n ->
                    match n with
                    | Annotated(sem, body) when sem.Role = RoleOther "colorbar" ->
                        Scene.fold
                            (fun k node ->
                                match node with
                                | Path(_, _, Some(Solid c), _) -> Set.add (Color.toHex c) k
                                | _ -> k)
                            acc
                            body
                    | _ -> acc)
                Set.empty
                scene

        let marks = markerFills scene

        Assert.False(Set.isEmpty marks)
        Assert.True(Set.isSubset marks stripFills, sprintf "marks not on the bar: %A" (Set.difference marks stripFills))

    /// The strip count is `min(256, ⌈height⌉)`: on a short bar the steps are pixels, not 256
    /// bands inside 60 px. Below the resolution of the output is where D46 stops, and the bar
    /// stops in the same place.
    [<Fact>]
    let ``a short colorbar has one strip per pixel, not two hundred and fifty-six`` () =
        let small = Chart.size 320.0 200.0

        match annotationOf (RoleOther "colorbar") (Chart.toScene small (colorbarFigure ())) with
        | None -> failwith "no colorbar was compiled"
        | Some sem ->
            let steps = sem.Data |> Array.pick (fun (k, v) -> if k = "steps" then Some(int v) else None)

            Assert.True(steps < Channel.ColorSteps, sprintf "%d steps on a 200 px figure" steps)
            Assert.True(steps > 1)

    // ---- D49: the size key ------------------------------------------------------------------

    /// A size channel is the one thing that *does* go in the legend, as `LegendSizedMarker`.
    /// Three entries, at the sizes the marks are drawn at, carrying the unit — because a legend
    /// row that reads `3.0` alone names no quantity.
    [<Fact>]
    let ``a size channel puts a three-entry key in the legend, with its unit`` () =
        let masses = Array.init 30 (fun i -> (1.0 + float i * 0.1) * 1.0<kg>)

        let names =
            Chart.toScene surface (scatter () |> Chart.sizeBy U.kilogram masses)
            |> Scene.fold
                (fun acc n ->
                    match n with
                    | Annotated(sem, _) when sem.Role = RoleOther "legend-entry" -> sem.Label :: acc
                    | _ -> acc)
                []
            |> List.rev

        Assert.Equal(3, names.Length)

        for n in names do
            Assert.EndsWith("kg", n)

        // The swatches really are different sizes: a size key drawn at one size is not a key.
        let sizes =
            Chart.toScene surface (scatter () |> Chart.sizeBy U.kilogram masses)
            |> Scene.fold
                (fun acc n ->
                    match n with
                    | Markers b -> Set.add (float b.Size) acc
                    | _ -> acc)
                Set.empty

        Assert.True(sizes.Count >= 3, sprintf "expected several mark sizes, got %A" sizes)

    /// §1.1's no-mutation rule has a corollary phase 2 could have broken quietly: a retheme has
    /// to reach a channel's colours too. They came off the outgoing theme's palette, so they
    /// follow the theme on the same rule a layer's style does — and a figure whose levels stayed
    /// in the old palette while everything else moved would be worse than one that never
    /// rethemed at all.
    [<Fact>]
    let ``a retheme recolours a categorical channel, and leaves a hand-picked one alone`` () =
        let fig = scatter () |> Chart.colorByKey keys

        let colorsOf (f: Figure) =
            match f.Panels.[0].Layers.[0].Channels.[0].Map with
            | ColorByLevel(_, cs) -> cs
            | _ -> failwith "expected a categorical colour channel"

        let dark = fig |> Chart.withTheme Theme.dark

        Assert.Equal<Color[]>(Channel.levelColors Theme.dark 3, colorsOf dark)
        Assert.NotEqual<Color[]>(colorsOf fig, colorsOf dark)

        // The original is untouched, because nothing in `Chart` mutates.
        Assert.Equal<Color[]>(Channel.levelColors Theme.initial 3, colorsOf fig)

        // A palette the caller chose is not the theme's, so it survives the retheme.
        let chosen = [| Palette.vermillion; Palette.blue; Palette.yellow |]

        let byHand =
            { fig with
                Panels =
                    fig.Panels
                    |> Array.map (fun p ->
                        { p with
                            Layers =
                                p.Layers
                                |> Array.map (fun l ->
                                    { l with
                                        Channels =
                                            l.Channels
                                            |> Array.map (fun c ->
                                                { c with
                                                    Map = ColorByLevel([| "control"; "treated"; "washout" |], chosen) }) }) }) }

        Assert.Equal<Color[]>(chosen, colorsOf (byHand |> Chart.withTheme Theme.dark))

    // ---- The front door's own guard ----------------------------------------------------------

    /// A channel with the wrong number of values would leave the tail of a series unmapped, and
    /// unmapped points are not drawn — a figure quietly missing marks. It raises instead, at
    /// the call that supplied the array.
    [<Fact>]
    let ``a channel of the wrong length is rejected`` () =
        let ex =
            Assert.Throws<System.ArgumentException>(fun () -> scatter () |> Chart.colorByKey [| "a"; "b" |] |> ignore)

        Assert.Contains("2 values", ex.Message)
        Assert.Contains("30 points", ex.Message)

// ======================================================================================
// §5.2 / D68 — THE PRODUCER NAMES WHAT IT DREW
//
// Before this milestone `Marey.Plot` produced exactly one `Keyed` node — the root — so the diff
// §5.2 designed the IR around had a granularity of one, and D1's rejection of closure-valued
// transforms was protecting a facility nothing used. These tests are the claim that the
// granularity is now the layer.
// ======================================================================================

module Keying =

    let private keysOf (n: Node) =
        Scene.fold
            (fun acc node ->
                match node with
                | Keyed(Key k, _) -> k :: acc
                | _ -> acc)
            []
            n
        |> List.rev

    /// The scheme, spelled out once so nothing downstream has to guess at it. D69's storyboard
    /// quotes `curve.[42]` as `figure/panel-0-0/layer-0/mark-42`, and this is where that string
    /// is fixed — before the phase that constructs one exists, so it cannot invent a second
    /// spelling.
    [<Fact>]
    let ``the four levels compose into a path`` () =
        let panel = Keys.panel 0 1
        let layer = Keys.layer panel 2

        Assert.Equal(Key "figure", Keys.figure)
        Assert.Equal(Key "figure/panel-0-1", panel)
        Assert.Equal(Key "figure/panel-0-1/layer-2", layer)
        Assert.Equal(Key "figure/panel-0-0/layer-0/mark-42", Keys.mark (Keys.layer (Keys.panel 0 0) 0) 42)

    [<Fact>]
    let ``a figure carries a key for its root, each panel and each layer`` () =
        let fig =
            Chart.line U.second U.metre fallT fallX
            |> Chart.addScatter U.second U.metre fallT (Array.rev fallX)
            |> Chart.beside (Chart.line U.second U.metre fallT fallX)

        Assert.Equal<string list>(
            [ "figure"
              "figure/panel-0-0"
              "figure/panel-0-0/grid"
              "figure/panel-0-0/layer-0"
              "figure/panel-0-0/layer-1"
              "figure/panel-0-0/axis-x"
              "figure/panel-0-0/axis-y"
              "figure/panel-0-1"
              "figure/panel-0-1/grid"
              "figure/panel-0-1/layer-0"
              "figure/panel-0-1/axis-x"
              "figure/panel-0-1/axis-y" ],
            keysOf (Chart.toScene surface fig)
        )

    /// **Ordinals, not names**, and this is the test that says why. Two layers of one panel may
    /// carry the same `Name` — nothing forbids it and `Chart.withName` is optional — so a key
    /// built from the name would collide, and `Scene.mapKey` would then fade both when asked to
    /// fade one. The label still says "measured" twice, because that is `Annotated`'s job.
    [<Fact>]
    let ``two layers with the same name still get different keys`` () =
        let fig =
            Chart.line U.second U.metre fallT fallX
            |> Chart.withName "measured"
            |> Chart.addLine U.second U.metre fallT (Array.rev fallX)

        let scene = Chart.toScene surface (fig |> Chart.withName "measured")

        Assert.Equal<string list>(
            [ "figure"
              "figure/panel-0-0"
              "figure/panel-0-0/grid"
              "figure/panel-0-0/layer-0"
              "figure/panel-0-0/layer-1"
              "figure/panel-0-0/axis-x"
              "figure/panel-0-0/axis-y"
              "figure/panel-0-0/legend" ],
            keysOf scene
        )

    /// The property M3 actually needs: a figure rebuilt from changed data keeps its keys, so two
    /// frames of an animation are addressable by the same names and a diff can compare subtree
    /// to subtree rather than figure to figure.
    ///
    /// **What this does not claim**, because no positional scheme can: identity across a *row
    /// removal*. Drop row 3 and every later row's index shifts, so `mark-417` names a different
    /// datum. Stable identity under an edit to the data needs an id supplied by the data, and
    /// that is the differ's problem at M4. What M3 needs is stability under restyling,
    /// re-theming and a change of values — which is what an animation does between frames.
    [<Fact>]
    let ``keys survive a restyle, a rename and a change of values`` () =
        // Named in both, so both have a legend: naming a layer for the first time adds one,
        // and a legend appearing is a change of structure rather than of identity.
        let before = Chart.toScene surface (Chart.line U.second U.metre fallT fallX |> Chart.withName "original")

        let after =
            Chart.line U.second U.metre fallT (fallX |> Array.map (fun x -> x * 0.5))
            |> Chart.withName "renamed"
            |> Chart.withTheme Theme.dark
            |> Chart.toScene surface

        Assert.Equal<string list>(keysOf before, keysOf after)

        // And the keys address something different in each, which is what makes the equality
        // above a statement about identity rather than about two identical scenes.
        Assert.NotEqual(
            Scene.tryFindKey (Keys.layer (Keys.panel 0 0) 0) before,
            Scene.tryFindKey (Keys.layer (Keys.panel 0 0) 0) after
        )

    /// One layer of one panel, faded, and nothing else moved. This is the operation the whole
    /// scheme exists to make possible, run against a real figure rather than a hand-built tree.
    [<Fact>]
    let ``one layer of one panel can be faded through its key`` () =
        let fig =
            Chart.line U.second U.metre fallT fallX
            |> Chart.addScatter U.second U.metre fallT (Array.rev fallX)

        let scene = Chart.toScene surface fig
        let target = Keys.layer (Keys.panel 0 0) 1
        let faded = Scene.mapKey target (Scene.opacity 0.25) scene

        match Scene.tryFindKey target faded with
        | Some(Opacity(a, inner)) ->
            Assert.Equal(0.25, a)
            Assert.Equal(Scene.tryFindKey target scene, Some inner)
        | other -> failwithf "expected the second layer to be wrapped in an Opacity, got %A" other

        // Exactly one node was added, and the first layer is the same object it was.
        Assert.Equal(Scene.count scene + 1, Scene.count faded)

        let untouched = Keys.layer (Keys.panel 0 0) 0

        match Scene.tryFindKey untouched scene, Scene.tryFindKey untouched faded with
        | Some a, Some b -> Assert.True(System.Object.ReferenceEquals(a, b), "the untouched layer was rebuilt")
        | _ -> failwith "the untouched layer went missing"
