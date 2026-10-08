/// M4.6 — *Surface*: units opt-in (D109), and the witness a caller did not state (D127).
///
/// `U.unspecified` is what an axis carries when nobody said what its numbers are. It is not
/// `U.one`: `one` claims the quantity has no dimension, which the axis-sharing check acts on, and
/// `unspecified` claims nothing. The tests below hold both halves of that sentence, because
/// implementing the first as "dimensionless skips the check" would pass every test of it and
/// break every test of the second.
module Marey.Tests.SurfaceTests

open Xunit
open Marey

let private extent = Chart.size 600.0 400.0

let private titles (fig: Figure) =
    Chart.toScene extent fig
    |> Scene.fold
        (fun acc n ->
            match n with
            | Text t -> t.Text :: acc
            | _ -> acc)
        []

let private ts = [| 0.0<s>; 1.0<s>; 2.0<s> |]

let private plain (ys: float[]) = Chart.line U.second U.unspecified ts ys
let private metres (ys: float<m>[]) = Chart.line U.second U.metre ts ys

module TheWitness =

    [<Fact>]
    let ``every declared witness is stated, and only unspecified is not`` () =
        Assert.True(U.metre.Stated)
        Assert.True(U.one.Stated)
        Assert.True(Derived.volt.Stated)
        Assert.False(U.unspecified.Stated)

    /// What is composed from an unstated unit is unstated: a density over numbers nobody named is
    /// a density over numbers nobody named, and `1/unspecified` must not acquire a claim.
    [<Fact>]
    let ``composition keeps an unstated side unstated`` () =
        Assert.False((U.recip U.unspecified).Stated)
        Assert.False((U.div U.unspecified U.second).Stated)
        Assert.False((U.mul U.metre U.unspecified).Stated)
        Assert.False((U.square U.unspecified).Stated)
        Assert.False((U.scaled "k" 1000.0 U.unspecified).Stated)
        Assert.True((U.div U.metre U.second).Stated)

    /// D128 — the relabel. Measures are erased, so a stated witness comes back as itself, and
    /// `null`, the omitted optional parameter, is the one place `unspecified` is decided.
    [<Fact>]
    let ``erase returns the witness it was given, and null is unspecified`` () =
        Assert.Same(U.metre :> obj, U.erase U.metre :> obj)
        Assert.Same(U.unspecified, U.erase null)

    [<Fact>]
    let ``erase copies every runtime field of a unit implemented elsewhere`` () =
        let foreign =
            { new IUnit with
                member _.Symbol = "°C"
                member _.Dim = Dim.temperature
                member _.Factors = []
                member _.ToSI = 1.0
                member _.OffsetSI = 273.15
                member _.Stated = true }

        let u = U.erase foreign
        Assert.Equal("°C", u.Symbol)
        Assert.Equal(Dim.temperature, u.Dim)
        Assert.Equal(273.15, u.OffsetSI)
        Assert.True(u.Stated)

/// Gate 1 — unspecified makes no claim.
module UnspecifiedMakesNoClaim =

    [<Fact>]
    let ``an unspecified layer shares an axis with metres, in either order, and the axis reads metres`` () =
        let under = metres [| 0.0<m>; 1.0<m>; 2.0<m> |] |> Chart.overlay (plain [| 0.5; 1.5; 2.5 |])
        let over = plain [| 0.5; 1.5; 2.5 |] |> Chart.overlay (metres [| 0.0<m>; 1.0<m>; 2.0<m> |])

        for fig in [ under; over ] do
            let ts = titles fig
            Assert.Contains("x [m]", ts)

    [<Fact>]
    let ``an unspecified panel shares a facet axis with a metre panel`` () =
        let fig = Chart.facet [| "plain", plain [| 0.0; 4.0; 1.0 |]; "metres", metres [| 0.0<m>; 1.0<m>; 2.0<m> |] |]
        Assert.Equal(2, fig.Panels.Length)

    [<Fact>]
    let ``an unspecified axis prints no bracket`` () =
        // The x axis is in seconds, so exactly one title carries a bracket.
        let ts = titles (plain [| 1200.0; 3400.0; 5600.0 |])
        Assert.Equal<string list>([ "t [s]" ], ts |> List.filter (fun t -> t.Contains "["))

    /// Being first does not make an unstated layer the reference. If it were, the check would
    /// compare metres and kelvin against it, find nothing to say, and let the two that *did*
    /// state a unit disagree on one axis.
    [<Fact>]
    let ``two stated layers still disagree when an unspecified one is drawn first`` () =
        let fig =
            plain [| 0.0; 1.0; 2.0 |]
            |> Chart.overlay (metres [| 0.0<m>; 1.0<m>; 2.0<m> |])
            |> Chart.overlay (Chart.line U.second U.kelvin ts [| 0.0<K>; 1.0<K>; 2.0<K> |])

        let ex = Assert.ThrowsAny<exn>(fun () -> Figure.unify fig.Panels.[0] |> ignore)
        Assert.Contains("layer 1", ex.Message)
        Assert.Contains("layer 2", ex.Message)
        Assert.Contains("Two physical dimensions cannot share an axis", ex.Message)

    [<Fact>]
    let ``a stated colour domain applies to an unspecified channel as the numbers given`` () =
        let fig =
            plain [| 0.0; 1.0; 2.0 |]
            |> Chart.colorBy U.unspecified [| 3.0; 4.0; 5.0 |]
            |> Chart.withColorDomain NonSi.degreeCelsius 0.0<degC> 10.0<degC>

        Chart.toScene extent fig |> ignore

/// Gate 2 — `U.one` still claims.
module OneStillClaims =

    [<Fact>]
    let ``a dimensionless layer over metres still raises`` () =
        let fig = metres [| 0.0<m>; 1.0<m>; 2.0<m> |] |> Chart.overlay (Chart.line U.second U.one ts [| 0.0; 1.0; 2.0 |])
        let ex = Assert.ThrowsAny<exn>(fun () -> Figure.unify fig.Panels.[0] |> ignore)
        Assert.Contains("Two physical dimensions cannot share an axis", ex.Message)
