/// M2 phase 5 — facets, and D48's answer to what the panels of one share.
///
/// A facet is a grid of panels **over one pair of axes**. That is the whole claim, and it is the
/// only thing separating small multiples from a page of unrelated charts: a mark halfway up one
/// panel is the same measurement as a mark halfway up the next. So most of this file reads the
/// domains back off the figure through `Erase.seriesScales` rather than trusting the path that
/// built them, which is what the plan's gate 4 asks for in as many words.
///
/// The other half is the check that comes with sharing. `Figure.unify` has carried the library's
/// one runtime `Dim` comparison since M0, across the layers of a panel; D48 makes it carry the
/// same comparison across the panels of a figure, and makes it conditional on the axis actually
/// being shared. The interesting finding is negative and is asserted here as such: on the *block*
/// path the check cannot fire, because a facet splits rows and every panel therefore reads the
/// same column under the same witness. It is the hand-composed path that can get this wrong, so
/// that is the path the refusal is tested on.
module Marey.Tests.FacetTests

open Xunit
open Marey
open Marey.UnitsOfMeasure

// ======================================================================================
// Fixtures
// ======================================================================================

type private Row =
    { Time: float<s>
      Displacement: float<m>
      Station: string
      Sample: string }

/// Four stations over deliberately different ranges, so that "shared" is a visible claim rather
/// than a coincidence of the data: station `i` runs from `3i` seconds and rises `i+1` times as
/// fast, so no two panels have the same natural domain on either axis.
let private rows =
    [| for si in 0..3 do
           let station = [| "north"; "south"; "east"; "west" |].[si]

           for i in 0..9 do
               yield
                   { Time = float (i + si * 3) * 1.0<s>
                     Displacement = float (i * (si + 1)) * 1.0<m>
                     Station = station
                     Sample = (if i % 2 = 0 then "A" else "B") } |]

let private extent = Chart.size 900.0 600.0

/// The x and y domains of every panel's first layer, read back through the erased scales.
let private domains (f: Figure) =
    f.Panels
    |> Array.map (fun p ->
        let xs, ys = Erase.seriesScales p.Layers.[0].Data
        xs.Domain, ys.Domain)

let private distinct (xs: 'a[]) = xs |> Array.distinct |> Array.length

/// The *dimension* of every panel's y axis. Two axes can run over the same two numbers and mean
/// different things, which is exactly the case the runtime check exists for.
let private dimsY (f: Figure) =
    f.Panels
    |> Array.map (fun p -> Dim.show (snd (Erase.seriesScales p.Layers.[0].Data)).Dim)
    |> Array.toList

let private baseFacet () =
    plot rows {
        x U.second (fun r -> r.Time)
        y U.metre (fun r -> r.Displacement)
        facetWrap (fun r -> r.Station)
    }

let private roleCount (role: SemanticRole) (fig: Figure) =
    Chart.toScene extent fig
    |> Scene.fold
        (fun acc n ->
            match n with
            | Annotated(s, _) when s.Role = role -> acc + 1
            | _ -> acc)
        0

// ======================================================================================
// Gate 4 — every panel of a facet reports the same domains
// ======================================================================================

module Sharing =

    /// **The phase's gate.** Read back, not assumed: the construction path could have given four
    /// panels four correct-looking domains and this is the assertion that says they are one.
    [<Fact>]
    let ``a four-level facet gives every panel one x domain and one y domain`` () =
        let fig = baseFacet ()

        Assert.Equal(4, fig.Panels.Length)
        Assert.Equal(1, distinct (domains fig |> Array.map fst))
        Assert.Equal(1, distinct (domains fig |> Array.map snd))

    /// The one domain is the **union**, not the first panel's. `north` alone reaches 9 metres;
    /// `west` reaches 36, and a shared axis that stopped at 9 would put three panels off the page.
    [<Fact>]
    let ``the shared domain covers every panel's data`` () =
        let fx, fy = (domains (baseFacet ())).[0]

        let ts = rows |> Array.map (fun r -> float r.Time)
        let ds = rows |> Array.map (fun r -> float r.Displacement)

        Assert.True(fst fx <= Array.min ts && Array.max ts <= snd fx)
        Assert.True(fst fy <= Array.min ds && Array.max ds <= snd fy)

    /// **The union is of the panels' *padded* domains**, which is the rule `Figure.unify` has
    /// used across the layers of a panel since M0, applied one level up. It has a visible
    /// consequence worth pinning rather than discovering later: a facet pads *less* than the same
    /// data unfaceted, because each panel's 5% is 5% of its own narrower span. Here the block's
    /// own x domain starts at −0.9 and the facet's at −0.45.
    ///
    /// Not a correctness problem — every panel's data is inside the shared domain either way, and
    /// the panels agree, which is what sharing is for. Recorded because the fix is not local:
    /// padding once over the whole block means computing the domain *before* the split and
    /// pushing it down, which is where `Stat.Frame` already goes.
    [<Fact>]
    let ``a shared axis pads by the panel's span, not the block's`` () =
        let whole =
            plot rows {
                x U.second (fun r -> r.Time)
                y U.metre (fun r -> r.Displacement)
            }

        let wx, _ = (domains whole).[0]
        let fx, _ = (domains (baseFacet ())).[0]

        Assert.Equal(-0.9, fst wx, 9)
        Assert.Equal(-0.45, fst fx, 9)
        Assert.Equal(18.9, snd wx, 9)
        Assert.Equal(18.45, snd fx, 9)

    [<Fact>]
    let ``ShareX leaves every panel its own y`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.Time)
                y U.metre (fun r -> r.Displacement)
                facetWrap (fun r -> r.Station)
                facetScales ShareX
            }

        Assert.Equal(1, distinct (domains fig |> Array.map fst))
        Assert.Equal(4, distinct (domains fig |> Array.map snd))

    [<Fact>]
    let ``ShareY leaves every panel its own x`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.Time)
                y U.metre (fun r -> r.Displacement)
                facetWrap (fun r -> r.Station)
                facetScales ShareY
            }

        Assert.Equal(4, distinct (domains fig |> Array.map fst))
        Assert.Equal(1, distinct (domains fig |> Array.map snd))

    /// A facet partitions. Every row is drawn, in exactly one panel — the property that separates
    /// a split from a filter, and the one a slice by row index either has or does not.
    [<Fact>]
    let ``a facet draws every row exactly once`` () =
        let fig = baseFacet ()

        let drawn =
            fig.Panels |> Array.sumBy (fun p -> Erase.seriesLength p.Layers.[0].Data)

        Assert.Equal(rows.Length, drawn)
        Assert.All(fig.Panels, fun p -> Assert.Equal(10, Erase.seriesLength p.Layers.[0].Data))

    [<Fact>]
    let ``panels are titled by level, in the order the data introduces them`` () =
        let fig = baseFacet ()

        Assert.Equal<string list>(
            [ "north"; "south"; "east"; "west" ],
            fig.Panels |> Array.map (fun p -> p.Title) |> Array.toList
        )

    [<Fact>]
    let ``the wrap width is as square as the panel count allows`` () =
        Assert.Equal(1, Chart.wrapCols 1)
        Assert.Equal(2, Chart.wrapCols 2)
        Assert.Equal(2, Chart.wrapCols 4)
        Assert.Equal(3, Chart.wrapCols 5)
        Assert.Equal(3, Chart.wrapCols 9)
        Assert.Equal(4, Chart.wrapCols 10)

        let fig = baseFacet ()
        Assert.Equal(2, fig.Rows)
        Assert.Equal(2, fig.Cols)

        Assert.Equal<(int * int) list>(
            [ (0, 0); (0, 1); (1, 0); (1, 1) ],
            fig.Panels |> Array.map (fun p -> p.Row, p.Col) |> Array.toList
        )

    [<Fact>]
    let ``a stated wrap width lays the panels out in one column`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.Time)
                y U.metre (fun r -> r.Displacement)
                facetWrapIn 1 (fun r -> r.Station)
            }

        Assert.Equal(4, fig.Rows)
        Assert.Equal(1, fig.Cols)

    /// **What makes a shared scale legible.** Sharing the numbers is only half of it: if the
    /// panels' plot rectangles were different sizes, the same value would land at a different
    /// place in each one and the reader could not compare them by eye. Measured off the
    /// `panelTransform` every panel wraps its data in, which is the rectangle itself.
    [<Fact>]
    let ``every panel of a facet has the same plot rectangle`` () =
        let boxes =
            Chart.toScene extent (baseFacet ())
            |> Scene.fold
                (fun acc n ->
                    match n with
                    | Transform(a, Clip _) ->
                        (System.Math.Round(float a.M11, 9), System.Math.Round(float a.M22, 9)) :: acc
                    | _ -> acc)
                []

        Assert.Equal(4, boxes.Length)
        Assert.Equal(1, distinct (List.toArray boxes))

// ======================================================================================
// The check that comes with sharing (D48)
// ======================================================================================

module TheCheck =

    let private line (uy: U<'u>) (a: float<'u>) (b: float<'u>) =
        Chart.line U.second uy [| 0.0<s>; 1.0<s> |] [| a; b |]

    /// The gate's second half, and it has to be asked on the hand-composed path — see the note
    /// at the top of this file. The message names the panels, because "panel 1" and "layer 1" are
    /// different mistakes.
    [<Fact>]
    let ``two dimensions cannot share an axis across panels`` () =
        let ex =
            Assert.Throws<System.Exception>(fun () ->
                Chart.facet [| "metres", line U.metre 0.0<m> 1.0<m>; "kelvin", line U.kelvin 0.0<K> 1.0<K> |]
                |> ignore)

        Assert.Contains("panel 0 ('metres')", ex.Message)
        Assert.Contains("panel 1 ('kelvin')", ex.Message)
        Assert.Contains("Two physical dimensions cannot share an axis", ex.Message)

    /// **The check follows the sharing.** A free axis makes no cross-panel claim, so there is
    /// nothing for it to be wrong about — and refusing anyway would be refusing a figure that
    /// says only what it means.
    [<Fact>]
    let ``an axis that is not shared is not checked`` () =
        let fig =
            Chart.facetWith ShareX 2 [| "metres", line U.metre 0.0<m> 1.0<m>; "kelvin", line U.kelvin 0.0<K> 1.0<K> |]

        Assert.Equal(2, fig.Panels.Length)

        // Asserted on the *dimension*, not on the domain: these two y axes run over the same two
        // numbers and differ only in what those numbers mean, which is the whole reason the check
        // has to compare `Dim` at runtime rather than trusting the values.
        Assert.Equal<string list>([ "m"; "K" ], dimsY fig)

        // x is shared, so it went through the check and came out unified.
        Assert.Equal(1, distinct (domains fig |> Array.map fst))

    /// D45's counterpart of the dimensional check, now across panels. An ordinal scale is
    /// dimensionless, so two categories have identical `Dim` and only the level sets tell them
    /// apart — and a level *number* is what the erased x array holds, so sharing an axis between
    /// two different level sets draws the right marks against the wrong labels.
    [<Fact>]
    let ``two categories over different levels cannot share an axis across panels`` () =
        let a = Chart.Category.bar U.metre [| "ctrl"; "treated" |] [| 1.0<m>; 2.0<m> |]
        let b = Chart.Category.bar U.metre [| "warm"; "cold" |] [| 1.0<m>; 2.0<m> |]

        let ex =
            Assert.Throws<System.Exception>(fun () -> Chart.facet [| "one", a; "two", b |] |> ignore)

        Assert.Contains("different sets of levels", ex.Message)

    [<Fact>]
    let ``the same levels share an axis across panels`` () =
        let a = Chart.Category.bar U.metre [| "ctrl"; "treated" |] [| 1.0<m>; 2.0<m> |]
        let b = Chart.Category.bar U.metre [| "ctrl"; "treated" |] [| 4.0<m>; 3.0<m> |]

        let fig = Chart.facet [| "one", a; "two", b |]

        Assert.Equal(1, distinct (domains fig |> Array.map snd))

    /// Not a facet, so not checked and not shared: `beside` composes two figures and says nothing
    /// about their axes. This is the case a fourth `FacetScales` arm would have duplicated.
    [<Fact>]
    let ``beside does not unify, and does not refuse`` () =
        let fig =
            Chart.beside (line U.kelvin 0.0<K> 1.0<K>) (line U.metre 0.0<m> 1.0<m>)

        Assert.Equal(2, fig.Panels.Length)
        Assert.Equal<string list>([ "m"; "K" ], dimsY fig)

// ======================================================================================
// Channels across a facet (D48, over D53)
// ======================================================================================

module Channels =

    /// Panels that do not contain the same levels. Under a map recomputed per panel, `b` would be
    /// level 1 in the first and level 0 in the second — two different colours for one sample, and
    /// the marks drawn against a legend that names neither.
    type private Uneven = { V: float<m>; Panel: string; Sample: string }

    let private uneven =
        [| { V = 1.0<m>; Panel = "left"; Sample = "a" }
           { V = 2.0<m>; Panel = "left"; Sample = "b" }
           { V = 3.0<m>; Panel = "right"; Sample = "b" }
           { V = 4.0<m>; Panel = "right"; Sample = "c" } |]

    let private unevenFig =
        plot uneven {
            x U.metre (fun r -> r.V)
            y U.metre (fun r -> r.V)
            color (fun r -> r.Sample)
            facetWrap (fun r -> r.Panel)
        }

    [<Fact>]
    let ``a facet's levels and colours are the block's, not the panel's`` () =
        let maps =
            unevenFig.Panels
            |> Array.map (fun p ->
                match p.Layers.[0].Channels.[0].Map with
                | ColorByLevel(levels, colors) -> levels, colors
                | other -> failwithf "expected a categorical colour channel, got %A" other)

        for (levels, _) in maps do
            Assert.Equal<string list>([ "a"; "b"; "c" ], List.ofArray levels)

        Assert.Equal(1, distinct (maps |> Array.map snd))

        // …and the level *numbers* in the data agree with that one map: `b` is level 1 in both
        // panels, even though it is the second value in one and the first in the other.
        Assert.Equal<float list>([ 0.0; 1.0 ], List.ofArray unevenFig.Panels.[0].Layers.[0].Channels.[0].Values)
        Assert.Equal<float list>([ 1.0; 2.0 ], List.ofArray unevenFig.Panels.[1].Layers.[0].Channels.[0].Values)

    /// The user-visible half of the same claim: the one legend enumerates every level of the
    /// block, including the two that only one panel contains.
    [<Fact>]
    let ``the legend enumerates levels no single panel has all of`` () =
        let svg = SvgBackend.render extent (Chart.toScene extent unevenFig)

        for level in [ "a"; "b"; "c" ] do
            Assert.Contains(">" + level + "<", svg)

    /// D53 says the legend entry belongs to the block's first layer; D48 says the block's first
    /// *panel*. Every panel's key would otherwise be the same key, drawn four times.
    [<Fact>]
    let ``the key is drawn once`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.Time)
                y U.metre (fun r -> r.Displacement)
                color (fun r -> r.Sample)
                facetWrap (fun r -> r.Station)
            }

        Assert.Equal<bool list>(
            [ true; false; false; false ],
            fig.Panels |> Array.map (fun p -> p.Layers.[0].ShowInLegend) |> Array.toList
        )

        Assert.Equal(1, roleCount RoleLegend fig)

        // The channel itself still reaches every panel — a suppressed key does not mean
        // suppressed colour.
        Assert.All(fig.Panels, fun p -> Assert.Equal(1, p.Layers.[0].Channels.Length))

    /// The same rule for a continuous channel, whose key is a colorbar rather than a legend
    /// (D49). This is the one that costs something: a repeated bar takes a column out of every
    /// panel, so four of them shrink the plots as well as saying the same thing four times.
    [<Fact>]
    let ``a colorbar is drawn once`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.Time)
                y U.metre (fun r -> r.Displacement)
                colorBy U.metre (fun r -> r.Displacement)
                facetWrap (fun r -> r.Station)
            }

        Assert.Equal(1, roleCount (RoleOther "colorbar") fig)

    [<Fact>]
    let ``an unfaceted figure still gets its colorbar`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.Time)
                y U.metre (fun r -> r.Displacement)
                colorBy U.metre (fun r -> r.Displacement)
            }

        Assert.Equal(1, roleCount (RoleOther "colorbar") fig)

// ======================================================================================
// Stats across a facet (D48, over D47)
// ======================================================================================

module Stats =

    let private histogram () =
        plot rows {
            x U.metre (fun r -> r.Displacement)
            facetWrap (fun r -> r.Station)
            histogram
        }

    /// **A faceted histogram bins into one grid.** Panels binned over their own ranges have
    /// different bin edges, and two bars of different widths at different places are not
    /// comparable — which is the only reason to draw them side by side. The x domain of a
    /// histogram series is its outer edges exactly, so reading the domains back reads the edges.
    [<Fact>]
    let ``a faceted histogram bins every panel into the same grid`` () =
        let fig = histogram ()

        Assert.Equal(4, fig.Panels.Length)
        Assert.Equal(1, distinct (domains fig |> Array.map fst))

        // Same number of bars in every panel, because the bins are the same bins — including the
        // empty ones, which is what makes the panels line up.
        Assert.Equal(1, distinct (fig.Panels |> Array.map (fun p -> Erase.seriesLength p.Layers.[0].Data)))

    /// And the grid is the *whole sample's*: the same bin count and the same edges an unfaceted
    /// histogram of all the rows would have chosen.
    [<Fact>]
    let ``the shared bins are the ones the whole sample asks for`` () =
        let whole =
            plot rows {
                x U.metre (fun r -> r.Displacement)
                histogram
            }

        let wx, _ = (domains whole).[0]
        let fx, _ = (domains (histogram ())).[0]

        Assert.Equal(fst wx, fst fx, 9)
        Assert.Equal(snd wx, snd fx, 9)

        Assert.Equal(
            Erase.seriesLength whole.Panels.[0].Layers.[0].Data,
            Erase.seriesLength (histogram ()).Panels.[0].Layers.[0].Data
        )

    /// `Stat.frameOf` is where that comes from, and it is worth one direct assertion: the frame
    /// of a sample is the sample's own finite range, bin count and bandwidth.
    [<Fact>]
    let ``a frame is the whole sample's own parameters`` () =
        let xs = rows |> Array.map (fun r -> r.Displacement)
        let frame = Stat.frameOf xs

        Assert.Equal(float (Array.min xs), fst frame.Domain, 9)
        Assert.Equal(float (Array.max xs), snd frame.Domain, 9)
        Assert.Equal(Stat.binCountFor xs, frame.Bins)
        Assert.Equal(float (Stat.bandwidth xs), frame.Bandwidth, 9)

    /// The failure a frame removes. Silverman's rule over one point is zero, and a density at
    /// bandwidth zero has no answer — so `Stat.density` refuses it, correctly. Under a facet the
    /// panel borrows the whole sample's bandwidth and draws the kernel over its one observation,
    /// which is what a one-row panel *is*.
    [<Fact>]
    let ``a one-row panel gets the whole sample's bandwidth`` () =
        let sparse =
            Array.append
                [| { Time = 0.0<s>; Displacement = 50.0<m>; Station = "lonely"; Sample = "A" } |]
                rows

        let fig =
            plot sparse {
                x U.metre (fun r -> r.Displacement)
                facetWrap (fun r -> r.Station)
                density
            }

        Assert.Equal(5, fig.Panels.Length)
        Assert.All(fig.Panels, fun p -> Assert.True(Erase.seriesLength p.Layers.[0].Data > 1))

        // Unfaceted, that one row on its own is exactly the case that raises.
        Assert.Throws<System.ArgumentException>(fun () -> Stat.density U.metre [| 50.0<m> |] |> ignore)
        |> ignore

    [<Fact>]
    let ``a faceted fit adds one line to every panel`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.Time)
                y U.metre (fun r -> r.Displacement)
                facetWrap (fun r -> r.Station)
                fit
            }

        Assert.All(fig.Panels, fun p -> Assert.Equal(2, p.Layers.Length))
        Assert.All(fig.Panels, fun p -> Assert.Equal(GeomLine, p.Layers.[1].Geom))

        // Two points per fitted line, and they are *this* panel's fit — the stations rise at
        // four different rates, so the four lines do.
        Assert.All(fig.Panels, fun p -> Assert.Equal(2, Erase.seriesLength p.Layers.[1].Data))

        // Read off the projected endpoints rather than the scale: the fit layer's *domain* is the
        // shared one, because sharing reaches every layer of every panel. Where the line lands
        // inside that domain is what differs.
        let ends =
            fig.Panels
            |> Array.map (fun p ->
                let pts = Erase.projectXY p.Layers.[1].Data
                System.Math.Round(float (Array.last pts).Y, 6))

        Assert.Equal(4, distinct ends)

    [<Fact>]
    let ``a faceted ecdf runs zero to one in every panel`` () =
        let fig =
            plot rows {
                x U.metre (fun r -> r.Displacement)
                facetWrap (fun r -> r.Station)
                ecdf
            }

        Assert.All(fig.Panels, fun p -> Assert.Equal((0.0, 1.0), (snd (Erase.seriesScales p.Layers.[0].Data)).Domain))

// ======================================================================================
// The refusals, and the render
// ======================================================================================

module Refusals =

    /// `facetScales` on a block with no facet is a statement about panels that do not exist.
    /// D47's rule: a caller who wrote it believes it did something.
    [<Fact>]
    let ``facetScales without a facet is refused`` () =
        let ex =
            Assert.Throws<System.Exception>(fun () ->
                plot rows {
                    x U.second (fun r -> r.Time)
                    y U.metre (fun r -> r.Displacement)
                    facetScales ShareX
                }
                |> ignore)

        Assert.Contains("has no facet", ex.Message)

    /// A facet cell holds one panel. Flattening a `beside` composition into the grid would put
    /// the caller's panels somewhere they did not ask for, and silently.
    [<Fact>]
    let ``a facet cell holds one panel`` () =
        let one = Chart.line U.second U.metre [| 0.0<s>; 1.0<s> |] [| 0.0<m>; 1.0<m> |]
        let two = Chart.beside one one

        let ex =
            Assert.Throws<System.Exception>(fun () -> Chart.facet [| "one", one; "two", two |] |> ignore)

        Assert.Contains("facet cell 1 ('two')", ex.Message)
        Assert.Contains("holds one panel", ex.Message)

    [<Fact>]
    let ``a facet needs at least one panel`` () =
        Assert.Throws<System.Exception>(fun () -> Chart.facet [||] |> ignore) |> ignore

    /// A distribution block's three refusals (D47) survive the facet: the facet is not a channel,
    /// and it does not buy the block a `y`.
    [<Fact>]
    let ``a faceted histogram still refuses a channel`` () =
        let ex =
            Assert.Throws<System.Exception>(fun () ->
                plot rows {
                    x U.metre (fun r -> r.Displacement)
                    color (fun r -> r.Sample)
                    facetWrap (fun r -> r.Station)
                    histogram
                }
                |> ignore)

        Assert.Contains("cannot carry a colour, size or shape channel", ex.Message)

module Rendering =

    [<Fact>]
    let ``a facet renders, with a panel title per level`` () =
        let svg = SvgBackend.render extent (Chart.toScene extent (baseFacet ()))

        Assert.True(svg.Length > 0)

        for level in [ "north"; "south"; "east"; "west" ] do
            Assert.Contains(">" + level + "<", svg)

    /// Gate 6, through a facet: the panel order comes from the level order and the level order
    /// comes from the data, so nothing here can be reached by enumerating a set.
    [<Fact>]
    let ``two renders of a facet are byte-identical`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.Time)
                y U.metre (fun r -> r.Displacement)
                color (fun r -> r.Sample)
                facetWrap (fun r -> r.Station)
                fit
            }

        let a = SvgBackend.render extent (Chart.toScene extent fig)
        let b = SvgBackend.render extent (Chart.toScene extent fig)

        Assert.Equal(a, b)
