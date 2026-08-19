/// M2 phase 0 — `PackedChannel`, the two-channel reunifier, and `Chart.ofPacked`.
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
