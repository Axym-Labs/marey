/// **keys the data supplies**, and the morph that pairs by them. Gate 11 of
/// `m4.5-plan.md` §4: a figure rebuilt with a row removed keeps every surviving datum's key, a
/// duplicate key is refused by name, and a `/` in the data cannot forge a path.
///
/// D68 recorded the condition as unachievable with ordinals, and the first test here is the one
/// that says why: drop a row and every later `mark-n` names a different datum.
module Marey.Tests.KeyTests

open System
open Xunit
open Marey
open Marey.Storyboard
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

type private Station = { Name: string; Year: float<s>; Level: float<m>; Region: string }

/// Ten stations. The first and last fix both domains, so removing an interior one moves nothing
/// that is still drawn, and a datum's mark can be compared across the two figures by position.
let private stations =
    Array.init 10 (fun i ->
        { Name = sprintf "S%d" i
          Year = float i * 1.0<s>
          Level = (if i = 0 then 0.0 elif i = 9 then 10.0 else 3.0 + float (i * 7 % 5)) * 1.0<m>
          Region = (if i % 2 = 0 then "north" else "south") })

let private keyed (rows: Station[]) =
    plot rows {
        x U.second (fun r -> r.Year)
        y U.metre (fun r -> r.Level)
        key (fun r -> r.Name)
    }

let private unkeyed (rows: Station[]) =
    plot rows {
        x U.second (fun r -> r.Year)
        y U.metre (fun r -> r.Level)
    }

let private surface = Chart.size 640.0 400.0
let private layerZero = Keys.layer (Keys.panel 0 0) 0

/// Every mark of the first layer given a node of its own, compiled.
let private sceneOf (fig: Figure) =
    let n = Erase.seriesLength fig.Panels.[0].Layers.[0].Data
    fig |> Chart.addressMarks [| 0 .. n - 1 |] |> Chart.toScene surface

/// Where the mark under a key is drawn, if there is one.
let private markAt (key: Key) (scene: Node) : Vec2<nd> option =
    let rec point (n: Node) =
        match n with
        | Markers b when b.Points.Length = 1 -> Some b.Points.[0]
        | Opacity(_, c)
        | Transform(_, c)
        | Annotated(_, c)
        | Keyed(_, c) -> point c
        | Group ns -> ns |> Array.tryPick point
        | _ -> None

    Scene.tryFindKey key scene |> Option.bind point

let private without (i: int) (rows: Station[]) = rows |> Array.removeAt i

// ======================================================================================
// Gate 11 — a key survives an edit
// ======================================================================================

module ``a key survives an edit`` =

    /// **Gate 11.** Remove row 4 and rebuild: every surviving station's mark is still under its
    /// own key, drawn where it was, and the removed station's key names nothing.
    [<Fact>]
    let ``a figure rebuilt with a row removed keeps every surviving datum's key`` () =
        let before = sceneOf (keyed stations)
        let after = sceneOf (keyed (without 4 stations))

        for s in without 4 stations do
            let k = Keys.datum layerZero s.Name
            let a, b = markAt k before, markAt k after
            Assert.True(a.IsSome && b.IsSome, sprintf "%s has no mark" s.Name)
            Assert.Equal(a.Value, b.Value)

        Assert.True((markAt (Keys.datum layerZero "S4") after).IsNone)

    /// D68's failure, measured, so the gate above is known to be the keys' doing: by ordinal,
    /// row 5 after the edit is the station that was row 6.
    [<Fact>]
    let ``by row, the same edit renames every later datum`` () =
        let before = sceneOf (unkeyed stations)
        let after = sceneOf (unkeyed (without 4 stations))
        Assert.Equal(markAt (Keys.mark layerZero 6) before, markAt (Keys.mark layerZero 5) after)
        Assert.NotEqual(markAt (Keys.mark layerZero 5) before, markAt (Keys.mark layerZero 5) after)

    /// A keyed figure with nothing addressed draws exactly what the unkeyed one does: keys name
    /// marks, they do not change them.
    [<Fact>]
    let ``keys change nothing drawn`` () =
        let svg (fig: Figure) = fig |> Chart.render SvgBackend.render surface
        Assert.Equal(svg (unkeyed stations), svg (keyed stations))

    /// **Gate 11.** A duplicate is refused where the keys are given, naming the key and its rows.
    [<Fact>]
    let ``a duplicate key is refused by name`` () =
        let e =
            Assert.Throws<ArgumentException>(fun () ->
                plot stations {
                    x U.second (fun r -> r.Year)
                    y U.metre (fun r -> r.Level)
                    key (fun r -> r.Region)
                }
                |> ignore)

        Assert.Contains("\"north\" (rows 0, 2, 4, 6, 8)", e.Message)
        Assert.Contains("\"south\" (rows 1, 3, 5, 7, 9)", e.Message)

        let fig = unkeyed stations
        let dup = Assert.Throws<ArgumentException>(fun () -> fig |> Chart.withKeys (Array.create 10 "x") |> ignore)
        Assert.Contains("\"x\" (rows 0, 1, 2", dup.Message)
        let short = Assert.Throws<ArgumentException>(fun () -> fig |> Chart.withKeys [| "a"; "b" |] |> ignore)
        Assert.Contains("10 rows and 2 keys", short.Message)

    /// **Gate 11.** A `/` in the data cannot forge a path: the key is one escaped segment, so it
    /// is not read as a child of another datum's key, and two keys that differ only by escaping
    /// stay two keys.
    [<Fact>]
    let ``a slash cannot forge a path`` () =
        let parent = Keys.datum layerZero "a"
        let forged = Keys.datum layerZero "a/key-b"
        Assert.False(Keys.covers parent forged)
        Assert.True(Keys.covers layerZero forged)
        let (Key k) = forged
        Assert.Equal("figure/panel-0-0/layer-0/key-a%2Fkey-b", k)
        Assert.NotEqual(Keys.datum layerZero "a/b", Keys.datum layerZero "a%2Fb")

    /// Keys with a slash, a percent sign, a quote and a space still give a well-formed SVG with
    /// one id per mark.
    [<Fact>]
    let ``awkward keys draw a well-formed document`` () =
        let names = [| "a/b"; "a%2Fb"; "q\"uote"; "two words"; "<tag>"; "é"; ""; "S7"; "S8"; "S9" |]
        let rows = Array.map2 (fun (s: Station) n -> { s with Name = n }) stations names
        let svg =
            keyed rows
            |> Chart.addressMarks [| 0 .. 9 |]
            |> Chart.render SvgBackend.render surface

        let doc = System.Xml.Linq.XDocument.Parse svg
        let ids = doc.Descendants() |> Seq.choose (fun e -> e.Attribute(System.Xml.Linq.XName.Get "id") |> Option.ofObj) |> Seq.map (fun a -> a.Value) |> Seq.toArray
        Assert.Equal(ids.Length, (Array.distinct ids).Length)

    /// The keys follow the rows into a facet's panels.
    [<Fact>]
    let ``a facet keeps each row's key`` () =
        let fig =
            plot stations {
                x U.second (fun r -> r.Year)
                y U.metre (fun r -> r.Level)
                key (fun r -> r.Name)
                facetWrap (fun r -> r.Region)
            }

        Assert.Equal<string[]>([| "S0"; "S2"; "S4"; "S6"; "S8" |], fig.Panels.[0].Layers.[0].RowKeys)
        Assert.Equal<string[]>([| "S1"; "S3"; "S5"; "S7"; "S9" |], fig.Panels.[1].Layers.[0].RowKeys)

    /// A stat makes rows of its own, so a key has nothing to name there, and is refused.
    [<Fact>]
    let ``a distribution block refuses a key`` () =
        let e =
            Assert.ThrowsAny<exn>(fun () ->
                plot stations {
                    x U.metre (fun r -> r.Level)
                    key (fun r -> r.Name)
                    histogram
                }
                |> ignore)

        Assert.Contains("cannot carry a `key`", e.Message)

// ======================================================================================
// The consumer — a morph that pairs by key, and fades what enters and leaves
// ======================================================================================

module ``a morph pairs by key`` =

    let private layerOf (s: Stage) = s.Figure.Value.Panels.[0].Layers.[0]

    let private valueOf (key: string) (l: Layer) =
        let xs, ys = Erase.seriesValues l.Data
        let i = Array.IndexOf(l.RowKeys, key)
        Assert.True(i >= 0, sprintf "%s is not in the layer" key)
        xs.[i], ys.[i]

    /// The same stations in reverse order, each a metre higher: by index, row 0 would morph into
    /// the station that was last.
    let private shuffled =
        stations |> Array.rev |> Array.map (fun s -> { s with Level = s.Level + 1.0<m> }) |> keyed

    /// Halfway through, each station is halfway between where it was and where the station of
    /// the same name is going.
    [<Fact>]
    let ``each datum moves to the datum of the same name`` () =
        let story =
            scene {
                let! curve = draw (keyed stations)
                do! curve |> morphTo shuffled |> over 1.0<s>
            }

        let mid = layerOf ((stages story).At 0.5<s>)

        for s in stations do
            let x, y = valueOf s.Name mid
            Assert.Equal(float s.Year, x, 9)
            Assert.Equal(float s.Level + 0.5, y, 9)

    /// A station only the target has fades in where it is going; one only the source has fades
    /// out where it was; at the end the layer is the target's rows, in its order, and neither
    /// fade is left on the scene.
    [<Fact>]
    let ``what enters fades in and what leaves fades out`` () =
        let target =
            Array.append (without 4 stations) [| { Name = "NEW"; Year = 4.5<s>; Level = 5.0<m>; Region = "north" } |]
            |> keyed

        let story =
            scene {
                let! curve = draw (keyed stations)
                do! curve |> morphTo target |> over 1.0<s>
            }

        let anim = animate 4 surface story
        let opacityOf (key: string) (n: Node) =
            match Scene.tryFindKey (Keys.datum layerZero key) n with
            | Some(Opacity(a, _)) -> Some a
            | Some _ -> Some 1.0
            | None -> None

        let mid = anim.At 0.25<s>
        Assert.Equal(Some 0.25, opacityOf "NEW" mid)
        Assert.Equal(Some 0.75, opacityOf "S4" mid)
        Assert.Equal(None, opacityOf "S5" mid)

        let last = anim.At 1.0<s>
        Assert.Equal(None, opacityOf "S4" last)
        Assert.Equal(None, opacityOf "NEW" last)

        let final = layerOf ((stages story).At 1.0<s>)
        Assert.Equal<string[]>(target.Panels.[0].Layers.[0].RowKeys, final.RowKeys)
        Assert.Empty(final.Addressed)

        let x, y = valueOf "S4" (layerOf ((stages story).At 0.5<s>))
        Assert.Equal((4.0, float stations.[4].Level), (x, y))

    /// The final frame of a keyed morph is the target, drawn: the same SVG as drawing the
    /// target with the source's style, which here is the same style.
    [<Fact>]
    let ``a keyed morph ends on the target`` () =
        let target = keyed (without 4 stations)

        let story =
            scene {
                let! curve = draw (keyed stations)
                do! curve |> morphTo target |> over 1.0<s>
            }

        let final = (stages story).At 1.0<s>
        let xs, ys = Erase.seriesValues (layerOf final).Data
        let xs', ys' = Erase.seriesValues target.Panels.[0].Layers.[0].Data
        Assert.Equal<float[]>(xs', xs)
        Assert.Equal<float[]>(ys', ys)

    /// `curve.["S6"]` names the datum, so a highlight after a morph rings the same station at
    /// its new row, and `curve.[6]` on a keyed scatter means the datum that was row 6.
    [<Fact>]
    let ``a highlight by key follows its datum through a morph`` () =
        let target = keyed (without 4 stations)

        let story =
            scene {
                let! curve = draw (keyed stations)
                do! curve |> morphTo target |> over 1.0<s>
                do! highlight curve.["S6"] |> over 0.5<s>
            }

        let final = layerOf ((stages story).At 1.5<s>)
        Assert.Equal<int[]>([| 5 |], final.Addressed)
        let drawn = (animate 4 surface story).At 1.5<s>
        Assert.True((Scene.tryFindKey (Keys.datum layerZero "S6") drawn).IsSome)

        let byRow =
            scene {
                let! curve = draw (keyed stations)
                do! highlight curve.[6] |> over 0.5<s>
            }

        Assert.True((Scene.tryFindKey (Keys.datum layerZero "S6") ((animate 4 surface byRow).At 0.5<s>)).IsSome)

    /// A datum that is not on stage is refused when the storyboard is built, by name.
    [<Fact>]
    let ``a highlight of a datum not on stage is refused by name`` () =
        let story =
            scene {
                let! curve = draw (keyed stations)
                do! highlight curve.["nowhere"]
            }

        let e = Assert.Throws<InvalidOperationException>(fun () -> duration story |> ignore)
        Assert.Contains("\"nowhere\"", e.Message)

    /// Without keys on both sides, the morph pairs by index as it always did.
    [<Fact>]
    let ``unkeyed, the morph pairs by index`` () =
        let story =
            scene {
                let! curve = draw (unkeyed stations)
                do! curve |> morphTo (unkeyed (Array.rev stations)) |> over 1.0<s>
            }

        let _, ys = Erase.seriesValues (layerOf ((stages story).At 0.5<s>)).Data
        Assert.Equal((float stations.[0].Level + float stations.[9].Level) / 2.0, ys.[0], 9)

    /// A colour channel comes across by level name: the target numbered its levels on its own.
    /// A region filtered out entirely keeps its colour while it fades, because the map holds the
    /// leaving level until the morph ends, and then the map is the target's.
    [<Fact>]
    let ``a channel comes across by level name`` () =
        let coloured (rows: Station[]) =
            plot rows {
                x U.second (fun r -> r.Year)
                y U.metre (fun r -> r.Level)
                key (fun r -> r.Name)
                color (fun r -> r.Region)
            }

        let source = coloured stations
        let target = coloured (stations |> Array.filter (fun s -> s.Region = "south"))

        let story =
            scene {
                let! curve = draw source
                do! curve |> morphTo target |> over 1.0<s>
            }

        let levelsOf (m: ChannelMap) =
            match m with
            | ColorByLevel(levels, colors) -> levels, colors
            | _ -> failwith "not a level map"

        let mid = layerOf ((stages story).At 0.5<s>)
        let c = mid.Channels.[0]
        let levels, colors = levelsOf c.Map
        Assert.Equal<string[]>([| "south"; "north" |], levels)
        let at (key: string) = c.Values.[Array.IndexOf(mid.RowKeys, key)]
        Assert.Equal(0.0, at "S1")
        Assert.Equal(1.0, at "S0")
        let _, sourceColors = levelsOf source.Panels.[0].Layers.[0].Channels.[0].Map
        Assert.Equal(sourceColors.[0], colors.[1])

        let final = (layerOf ((stages story).At 1.0<s>)).Channels.[0]
        Assert.Equal<float[]>(target.Panels.[0].Layers.[0].Channels.[0].Values, final.Values)
        Assert.Equal<string[]>([| "south" |], fst (levelsOf final.Map))
