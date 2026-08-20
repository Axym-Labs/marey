/// M2 phases 0 and 2 — `PackedChannel`, the two-channel reunifier, `Chart.ofPacked`, and the
/// non-positional channels that map data to appearance.
///
/// D44 finding 2 gives the encoding CE a shape the erasure boundary could not express: a
/// channel is *one* witness and *one* array, erased where it is declared, long before anything
/// knows which two channels are the positional pair. Finding 3 gives the way back out — rank-2
/// twice, nested, with both measures alive in exactly one scope and nowhere else.
///
/// The claims below are what stops that construction from being quietly weakened later. Two of
/// them are compile-time: a binding whose type annotation *is* the claim, in the style
/// `RegressionTests.fs` uses, because the measures they assert about do not survive to runtime
/// where an assertion could look at them.
module Marey.Tests.ChannelTests

open Xunit
open Marey

// ======================================================================================
// Fixtures — two channels, deliberately of different measures
// ======================================================================================

let private times = Array.init 8 (fun i -> float i * 0.5<s>)
let private displacements = times |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)

let private timeChannel () = Erase.packChannel U.second times
let private displacementChannel () = Erase.packChannel U.metre displacements

// ======================================================================================
// D44 finding 2/3 — a channel packs, and two channels reunify
// ======================================================================================

module Reunification =

    /// Phase 0's exit criterion: two channels of *different* measures, packed independently,
    /// reunified, and both witnesses still readable off the far side.
    [<Fact>]
    let ``two channels of different measures reunify into a series carrying both symbols`` () =
        let packed = Erase.channelSeries "fall" (timeChannel ()) (displacementChannel ())
        let xs, ys = Erase.seriesScales packed

        Assert.Equal("s", xs.Symbol)
        Assert.Equal("m", ys.Symbol)
        Assert.Equal(Dim.time, xs.Dim)
        Assert.Equal(Dim.length, ys.Dim)
        Assert.Equal("fall", Erase.seriesName packed)

    /// The values survive the round trip in order, and on the correct side. Packing x into y
    /// would still produce a series carrying both symbols, so the symbols alone are not enough.
    [<Fact>]
    let ``reunification keeps each channel's values on its own axis`` () =
        let packed = Erase.channelSeries "fall" (timeChannel ()) (displacementChannel ())

        let xs, ys =
            packed.Accept
                { new ISeriesConsumer<float[] * float[]> with
                    member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) =
                        Array.map float s.X, Array.map float s.Y }

        Assert.Equal<float[]>(Array.map float times, xs)
        Assert.Equal<float[]>(Array.map float displacements, ys)

    /// The reunifier's whole purpose: `'ux` and `'uy` are both *typed* inside the pair
    /// consumer, so measured arithmetic across the two channels compiles there. The annotation
    /// on `slope` is the claim — a quotient of the two input measures, which is what D47 will
    /// need for `regression` and what nothing outside this scope can name.
    [<Fact>]
    let ``both measures are typed inside the pair consumer`` () =
        let reported =
            Erase.reunify
                (timeChannel ())
                (displacementChannel ())
                { new IChannelPairConsumer<PackedQuantity> with
                    member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>
                        (ux: U<'ux>, xs: float<'ux>[], uy: U<'uy>, ys: float<'uy>[])
                        =
                        let slope: float<'uy / 'ux> = (ys.[ys.Length - 1] - ys.[0]) / (xs.[xs.Length - 1] - xs.[0])
                        Erase.packQuantity (U.div uy ux) slope }

        let symbol, value =
            reported.Accept
                { new IQuantityConsumer<string * float> with
                    member _.Apply<[<Measure>] 'u>(u: U<'u>, x: float<'u>) = u.Symbol, float x }

        // Free fall over 0 … 3.5 s: mean velocity is g·t/2.
        Assert.Equal("m/s", symbol)
        Assert.Equal(0.5 * 9.81 * 3.5, value, 9)

    /// The reunifier terminates in `Erase.series`, so the length check at the erasure boundary
    /// is the one a mismatched pair of channels hits. It is not re-implemented above it.
    [<Fact>]
    let ``channels of unequal length are rejected at the erasure boundary`` () =
        Assert.Throws<System.ArgumentException>(fun () ->
            Erase.channelSeries "ragged" (Erase.packChannel U.second [| 0.0<s>; 1.0<s> |]) (Erase.packChannel U.metre [| 0.0<m> |])
            |> ignore)
        |> ignore

// ======================================================================================
// The Core boundary — `Marey.Plot` never writes a rank-2 consumer
// ======================================================================================

module PlotBoundary =

    /// D44 finding 3's reason for `Chart.ofPacked` existing at all: the reunifier stops in
    /// `Marey.Core`, and a `PackedSeries` is all `Marey.Plot` needs to see. If this compiles
    /// and renders, the CE can be built without a second rank-2 consumer above Core.
    [<Fact>]
    let ``a reunified series renders through Chart.ofPacked, labelled from its witnesses`` () =
        let fig =
            Erase.channelSeries "" (timeChannel ()) (displacementChannel ())
            |> Chart.ofPacked GeomLine

        let extent = Chart.size 800.0 500.0
        let svg = SvgBackend.render extent (Chart.toScene extent fig)

        Assert.Contains(">t [s]<", svg)
        Assert.Contains(">x [m]<", svg)

    /// Channels are the CE's path to the same figure the front door builds, so the two paths
    /// must not diverge in the defaults they pick. Byte identity of the rendered SVG is the
    /// strongest available statement of that, and it is what `Erase.channelSeries` sharing
    /// `Chart.series`'s 5% padded linear scales buys.
    [<Fact>]
    let ``the channel path and Chart.line agree byte for byte`` () =
        let extent = Chart.size 800.0 500.0
        let render fig = SvgBackend.render extent (Chart.toScene extent fig)

        let viaChannels =
            Erase.channelSeries "" (timeChannel ()) (displacementChannel ())
            |> Chart.ofPacked GeomLine

        Assert.Equal(render (Chart.line U.second U.metre times displacements), render viaChannels)

// ======================================================================================
// D46 — the quantiser: how many batches, in what order, and what happens to a NaN
// ======================================================================================

/// The claims here are the ones D46 rests on, and every one of them is a *count* rather than a
/// picture — the whole decision is about how many IR nodes a varying colour costs, and the
/// number of groups is exactly where that shows.
module Quantisation =

    let private levels = [| "control"; "treated"; "washout" |]

    /// 30 points cycling through three levels.
    let private levelValues = Array.init 30 (fun i -> float (i % 3))

    let private colorChannel () =
        Channel.colorByLevel Theme.initial levels levelValues

    let private shapeChannel () =
        Channel.shapeByLevel Theme.initial levels levelValues

    let private temperatures = Array.init 30 (fun i -> (280.0 + float i) * 1.0<K>)

    let private tempChannel () =
        Channel.colorByValue "T" Palette.viridis (Scale.linear U.kelvin (280.0<K>, 309.0<K>)) temperatures

    /// The empty case has to be the identity, because it is the case every existing figure is
    /// in: one group of every point, styled by nothing.
    [<Fact>]
    let ``no channels is one batch of every point`` () =
        let batches, unmapped = Channel.split [||] 40

        Assert.Equal(1, batches.Length)
        Assert.Equal(40, batches.[0].Indices.Length)
        Assert.Equal(0, unmapped)

    /// Phase 2's first exit criterion, at the level the decision was made: three levels are
    /// three groups, whatever the point count.
    [<Fact>]
    let ``three levels are three batches, and they partition the points`` () =
        let batches, unmapped = Channel.split [| colorChannel () |] 30

        Assert.Equal(3, batches.Length)
        Assert.Equal(0, unmapped)

        let all = batches |> Array.collect (fun b -> b.Indices) |> Array.sort
        Assert.Equal<int[]>(Array.init 30 id, all)

    /// §9's colour-blind-safe scatter is colour *and* shape over the same key, and what makes
    /// it affordable is that it stays three batches. A product would be nine — and a product is
    /// what an implementation keyed on the visual attributes rather than the level would give.
    [<Fact>]
    let ``colour and shape over the same levels is still one batch per level`` () =
        let batches, _ = Channel.split [| colorChannel (); shapeChannel () |] 30

        Assert.Equal(3, batches.Length)

        // And the redundancy is real: the three batches differ in both attributes.
        let styled =
            batches |> Array.map (fun b -> b.Restyle(Geom.styleFor Theme.initial 0))

        Assert.Equal(3, styled |> Array.map (fun s -> Color.toHex s.Color) |> Array.distinct |> Array.length)
        Assert.Equal(3, styled |> Array.map (fun s -> s.Shape) |> Array.distinct |> Array.length)

    /// Two channels over *different* keys do take the product, and should: the reader has two
    /// things to tell apart, so the marks do too.
    [<Fact>]
    let ``two channels over different keys take their product`` () =
        let byThree = colorChannel ()

        let byTwo =
            Channel.shapeByLevel Theme.initial [| "a"; "b" |] (Array.init 30 (fun i -> float (i % 2)))

        let batches, _ = Channel.split [| byThree; byTwo |] 30

        // 30 points cycling 3 and 2 visit all six combinations.
        Assert.Equal(6, batches.Length)

    /// D46's bound. 100 000 points, 256 steps, and the assertion is `<=` rather than `=`
    /// because a domain the data does not fill leaves steps empty. A bound is what fails
    /// loudly if anybody ever reaches for one batch per point.
    [<Fact>]
    let ``a hundred thousand points cost at most 256 batches`` () =
        let n = 100_000
        let rng = System.Random 11
        let values = Array.init n (fun _ -> (280.0 + rng.NextDouble() * 25.0) * 1.0<K>)

        let ch =
            Channel.colorByValue "T" Palette.viridis (Scale.linear U.kelvin (280.0<K>, 305.0<K>)) values

        let batches, unmapped = Channel.split [| ch |] n

        Assert.True(batches.Length <= Channel.ColorSteps, sprintf "%d batches" batches.Length)
        Assert.Equal(0, unmapped)
        Assert.Equal(n, batches |> Array.sumBy (fun b -> b.Indices.Length))

    /// §13's determinism gate compares two renders byte for byte, and a `Dictionary`'s
    /// enumeration order is not a guarantee. Batches come out sorted by bucket key, which also
    /// means the drawing order of a channelled layer is the channel's own order — low values
    /// under high ones — rather than an accident of hashing.
    [<Fact>]
    let ``batches come out in ascending bucket order`` () =
        let batches, _ = Channel.split [| tempChannel () |] 30

        let heads =
            batches |> Array.map (fun b -> temperatures.[Array.min b.Indices])

        Assert.Equal<float<K>[]>(Array.sort heads, heads)

    /// A missing value has no colour, and neither quiet alternative is honest: clamping paints
    /// missing data as a real measurement, and a fallback colour invents a category. It is
    /// dropped, and the count is reported so that a figure can say so.
    [<Fact>]
    let ``a non-finite value is unmapped rather than clamped`` () =
        let values =
            Array.init 30 (fun i -> if i % 10 = 0 then nan * 1.0<K> else (280.0 + float i) * 1.0<K>)

        let ch =
            Channel.colorByValue "T" Palette.viridis (Scale.linear U.kelvin (280.0<K>, 309.0<K>)) values

        let batches, unmapped = Channel.split [| ch |] 30

        Assert.Equal(3, unmapped)
        Assert.Equal(27, batches |> Array.sumBy (fun b -> b.Indices.Length))

    /// A level number outside the level set is the same case. It reaches here rather than
    /// raising because `Levels.positions` is the door that raises (D45); this is the backstop
    /// for a channel built by hand.
    [<Fact>]
    let ``a level number outside the level set is unmapped`` () =
        let ch = Channel.colorByLevel Theme.initial levels [| 0.0; 1.0; 2.0; 7.0; -1.0 |]

        let batches, unmapped = Channel.split [| ch |] 5

        Assert.Equal(2, unmapped)
        Assert.Equal(3, batches.Length)

    /// D46's second quantum. Two marks whose diameters differ by less than half a pixel are the
    /// same mark, so they must land in the same bucket — otherwise a size channel over
    /// continuous data is one batch per point by another route.
    [<Fact>]
    let ``sizes within half a pixel share a bucket`` () =
        let scale = Scale.linear U.kilogram (0.0<kg>, 1.0<kg>)
        let map = SizeByValue(Erase.packScale scale, 4.0<px>, 12.0<px>)
        let bucket = Channel.bucketer map

        // 1000 evenly spaced values across an 8 px span cannot need more than 17 buckets.
        let buckets = Array.init 1000 (fun i -> bucket (float i / 999.0)) |> Array.distinct

        Assert.True(buckets.Length <= 17, sprintf "%d distinct sizes" buckets.Length)

    /// A size channel encodes in **area**, because area is what the eye reads off a disc. The
    /// middle of the domain is therefore the mark of middling *area*, not of middling
    /// diameter — which would be the same picture only if the reader were measuring radii.
    [<Fact>]
    let ``size interpolates area, not diameter`` () =
        let small = 4.0<px>
        let large = 12.0<px>
        let mid = Channel.sizeAt small large 0.5

        let area (d: float<px>) = float d * float d

        Assert.Equal((area small + area large) / 2.0, area mid, 6)
        Assert.NotEqual((float small + float large) / 2.0, float mid, 6)

    /// The colorbar's strips and the marks take their colour from the same call, at the
    /// midpoint of the step. This is what keeps D49's bar honest — "the bar agrees with the
    /// marks" as a fact about the code rather than about two implementations that currently
    /// happen to match.
    [<Fact>]
    let ``a bucket's colour is the midpoint of its step`` () =
        let ch = tempChannel ()
        let styled = Channel.restyle ch.Map 100 (Geom.styleFor Theme.initial 0)

        Assert.Equal(
            Color.toHex (Ramp.at Palette.viridis (Channel.stepMidpoint Channel.ColorSteps 100)),
            Color.toHex styled.Color
        )
