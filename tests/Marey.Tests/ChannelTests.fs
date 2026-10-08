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
open Marey.UnitsOfMeasure

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

// ======================================================================================
// Phase 3 — the encoding block (§9, D43, D44)
// ======================================================================================

/// The row type every block below encodes.
///
/// A record with fields named nothing like the channels that read them, so that a passing test
/// cannot be a coincidence of naming: `x U.second (fun r -> r.Elapsed)` binds the measure from
/// the witness and checks the field against it, and neither half is inferred from the other.
type private Fall =
    { Elapsed: float<s>
      Drop: float<m>
      Sample: string
      Mass: float<kg> }

let private fallRows =
    [ for i in 0..11 ->
        let t = float i * 0.5<s>

        { Elapsed = t
          Drop = 0.5 * 9.81<m / s^2> * t * t
          Sample = [| "control"; "treated"; "washout" |].[i % 3]
          Mass = (1.0 + float i) * 1.0<kg> } ]

/// What the block is *for*, per D43: composition, not inference. Every claim here is about a
/// figure the quick front door cannot express in one call — several channels, several layers
/// over one dataset — or about the block agreeing exactly with the front door where it can.
module EncodingBlock =

    let private extent = Chart.size 800.0 500.0

    let private render (fig: Figure) =
        SvgBackend.render extent (Chart.toScene extent fig)

    /// `Markers` nodes in a figure whose legend is off, so the count is the *data's* batches and
    /// not the legend's swatches — which are `Markers` too, one per entry.
    let private markerBatches (fig: Figure) =
        Chart.toScene extent (Chart.withLegend LegendNone fig)
        |> Scene.fold
            (fun acc n ->
                match n with
                | Markers _ -> acc + 1
                | _ -> acc)
            0

    let private occurrences (needle: string) (haystack: string) =
        let mutable count = 0
        let mutable i = haystack.IndexOf(needle, System.StringComparison.Ordinal)

        while i >= 0 do
            count <- count + 1
            i <- haystack.IndexOf(needle, i + 1, System.StringComparison.Ordinal)

        count

    // ---- The exit criterion ----------------------------------------------------------------

    /// **Phase 3's exit criterion**: §9's corrected example, as close as the library's real
    /// names allow, compiles and renders — and both axis titles are derived from the witnesses
    /// across two independent erasures and a reunification, with nothing configured.
    ///
    /// This is D44's end-to-end claim restated as a test rather than as a spike that was not
    /// checked in.
    [<Fact>]
    let ``the block titles both axes from its witnesses alone`` () =
        let fig =
            plot fallRows {
                x U.second (fun r -> r.Elapsed)
                y U.metre (fun r -> r.Drop)
                color (fun r -> r.Sample)
                shape (fun r -> r.Sample)
                layerWith GeomPoint (Geom.withOpacity 0.6)
                layer GeomLine
                coord Coord.cartesian
                theme Theme.publication
            }

        let svg = render fig

        Assert.Contains(">t [s]<", svg)
        Assert.Contains(">x [m]<", svg)

    /// The block and the front door must not diverge in the defaults they pick, or the same data
    /// would render two ways depending on which door it came through. Byte identity is the
    /// strongest available statement of that, and it is what `Erase.channelSeries` sharing
    /// `Chart.series`'s 5% padded linear scales buys — now checked through the CE rather than
    /// through `Erase` directly.
    [<Fact>]
    let ``a two-channel block is byte-identical with Chart.line`` () =
        let viaBlock =
            plot fallRows {
                x U.second (fun r -> r.Elapsed)
                y U.metre (fun r -> r.Drop)
                layer GeomLine
            }

        let ts = fallRows |> List.map (fun r -> r.Elapsed) |> Array.ofList
        let xs = fallRows |> List.map (fun r -> r.Drop) |> Array.ofList

        Assert.Equal(render (Chart.line U.second U.metre ts xs), render viaBlock)

    /// A block with no `layer` is a scatter — two positional channels and nothing else say where
    /// the points are, and points are how that is drawn. Pinned against `Chart.scatter` so the
    /// default cannot drift into something that additionally asserts adjacency.
    [<Fact>]
    let ``a block with no layer is a scatter`` () =
        let viaBlock =
            plot fallRows {
                x U.second (fun r -> r.Elapsed)
                y U.metre (fun r -> r.Drop)
            }

        let ts = fallRows |> List.map (fun r -> r.Elapsed) |> Array.ofList
        let xs = fallRows |> List.map (fun r -> r.Drop) |> Array.ofList

        Assert.Equal(render (Chart.scatter U.second U.metre ts xs), render viaBlock)

    // ---- Gate 2, restated through the block --------------------------------------------------

    /// §5.1's batching decision, reached through the CE this time: a categorical colour channel
    /// costs one `Markers` node per *level*, not one per point. D46 is quietly violated the day
    /// this number becomes 12.
    [<Fact>]
    let ``a categorical colour channel through the block is one batch per level`` () =
        let fig =
            plot fallRows {
                x U.second (fun r -> r.Elapsed)
                y U.metre (fun r -> r.Drop)
                color (fun r -> r.Sample)
            }

        Assert.Equal(3, markerBatches fig)

    /// Colour and shape over the same key is §9's colour-blind-safe scatter, and it stays three
    /// batches rather than nine: both channels read the same level number.
    [<Fact>]
    let ``colour and shape over one key stay one batch per level in a block`` () =
        let fig =
            plot fallRows {
                x U.second (fun r -> r.Elapsed)
                y U.metre (fun r -> r.Drop)
                color (fun r -> r.Sample)
                shape (fun r -> r.Sample)
            }

        Assert.Equal(3, markerBatches fig)

    /// D49 through the block: a continuous colour channel gets a colorbar, and the bar labels
    /// itself from the witness the channel was given — `m [kg]`, quantity name from the
    /// dimension and unit from the witness, neither configured.
    [<Fact>]
    let ``a continuous colour channel labels its colorbar from its witness`` () =
        let fig =
            plot fallRows {
                x U.second (fun r -> r.Elapsed)
                y U.metre (fun r -> r.Drop)
                colorBy U.kilogram (fun r -> r.Mass)
            }

        Assert.Contains(">m [kg]<", render fig)

    /// And `colorLabel` names the quantity, exactly as `Chart.withColorLabel` does — the unit
    /// half is still never configured.
    [<Fact>]
    let ``colorLabel names the quantity and leaves the unit alone`` () =
        let fig =
            plot fallRows {
                x U.second (fun r -> r.Elapsed)
                y U.metre (fun r -> r.Drop)
                colorBy U.kilogram (fun r -> r.Mass)
                colorLabel "payload"
            }

        Assert.Contains(">payload [kg]<", render fig)

    // ---- The legend, which is where per-block channels could double-count --------------------

    /// The block's channels belong to the *block*, so every layer carries them — which is what
    /// makes a two-layer block the case where three levels could enumerate themselves six times.
    /// One entry per level, whatever the layer count.
    [<Fact>]
    let ``two layers over three levels give three legend entries`` () =
        let svg =
            render (
                plot fallRows {
                    x U.second (fun r -> r.Elapsed)
                    y U.metre (fun r -> r.Drop)
                    color (fun r -> r.Sample)
                    layer GeomPoint
                    layer GeomLine
                }
            )

        Assert.Equal(1, occurrences ">control<" svg)
        Assert.Equal(1, occurrences ">treated<" svg)
        Assert.Equal(1, occurrences ">washout<" svg)

    /// The same for the series name: one entry, however many layers draw the series.
    [<Fact>]
    let ``a named two-layer block gives one legend entry`` () =
        let svg =
            render (
                plot fallRows {
                    x U.second (fun r -> r.Elapsed)
                    y U.metre (fun r -> r.Drop)
                    name "free fall"
                    layer GeomLine
                    layer GeomPoint
                }
            )

        Assert.Equal(1, occurrences ">free fall<" svg)

    /// The quick front door must not have lost its legend to the rule that makes the above work.
    /// `Chart.scatter` names its layer `""`, so its `ShowInLegend` is `false` until a channel is
    /// attached — this is the case that would break silently.
    [<Fact>]
    let ``attaching a channel to an unnamed layer still gives it a legend`` () =
        let ts = fallRows |> List.map (fun r -> r.Elapsed) |> Array.ofList
        let xs = fallRows |> List.map (fun r -> r.Drop) |> Array.ofList
        let keys = fallRows |> List.map (fun r -> r.Sample) |> Array.ofList

        let svg =
            SvgBackend.render extent (Chart.toScene extent (Chart.scatter U.second U.metre ts xs |> Chart.colorByKey keys))

        Assert.Equal(1, occurrences ">control<" svg)

    // ---- The block is order-free, and its layers are one series ------------------------------

    /// **`theme` may appear anywhere.** A categorical channel's colours come off the theme's
    /// palette, and the block accumulates channel *constructors* rather than channels precisely
    /// so that a `theme` three lines below a `color` still reaches it. The reversed palette is
    /// what makes this decisive: it changes nothing but the level colours.
    [<Fact>]
    let ``theme reaches a channel declared above it`` () =
        let reversed =
            Theme.initial
            |> Theme.withPalette (Array.rev (Array.truncate 3 Theme.initial.Categorical))

        let themeFirst =
            plot fallRows {
                x U.second (fun r -> r.Elapsed)
                y U.metre (fun r -> r.Drop)
                theme reversed
                color (fun r -> r.Sample)
            }

        let themeLast =
            plot fallRows {
                x U.second (fun r -> r.Elapsed)
                y U.metre (fun r -> r.Drop)
                color (fun r -> r.Sample)
                theme reversed
            }

        let untouched =
            plot fallRows {
                x U.second (fun r -> r.Elapsed)
                y U.metre (fun r -> r.Drop)
                color (fun r -> r.Sample)
            }

        Assert.Equal(render themeFirst, render themeLast)
        // Non-vacuity: the palette really did move the level colours.
        Assert.NotEqual<string>(render untouched, render themeLast)

    /// The layers of one block are alternative renderings of **one** series, so they share one
    /// base style. `Chart.addLine` advances the palette because each of its calls adds a
    /// different series; a line and its markers are not two things to tell apart.
    [<Fact>]
    let ``every layer of a block shares one base style`` () =
        let fig =
            plot fallRows {
                x U.second (fun r -> r.Elapsed)
                y U.metre (fun r -> r.Drop)
                layer GeomLine
                layer GeomPoint
            }

        let colors =
            fig.Panels.[0].Layers
            |> Array.map (fun l -> Color.toHex l.Style.Color)
            |> Array.distinct

        Assert.Equal(2, fig.Panels.[0].Layers.Length)
        Assert.Equal<string[]>([| Color.toHex (Geom.styleFor Theme.initial 0).Color |], colors)

    /// Gate 6, through the CE: two renders of one block are byte-identical. The builder
    /// accumulates through a `Dictionary` in `Channel.split` and a `HashSet` in `Levels.ofSeq`,
    /// and neither enumeration order is allowed to reach the output.
    [<Fact>]
    let ``two renders of one block are byte-identical`` () =
        let build () =
            plot fallRows {
                x U.second (fun r -> r.Elapsed)
                y U.metre (fun r -> r.Drop)
                color (fun r -> r.Sample)
                size U.kilogram (fun r -> r.Mass)
                layer GeomPoint
            }

        Assert.Equal(render (build ()), render (build ()))

    /// The one mistake the block cannot make a compile error: the state has the same type
    /// whether or not a channel was written to it. So the message has to name the channel and
    /// show the operation that supplies it.
    [<Fact>]
    let ``a block with no y channel says which channel is missing`` () =
        let ex =
            Assert.Throws<System.Exception>(fun () ->
                plot fallRows {
                    x U.second (fun r -> r.Elapsed)
                    layer GeomPoint
                }
                |> ignore)

        Assert.Contains("no y channel", ex.Message)

    /// A `seq` is enumerated exactly once, however many channels read it. A single-shot sequence
    /// is the case that fails loudly if it is not — and a lazy sequence with a side effect is
    /// how that is observed without depending on one.
    [<Fact>]
    let ``a lazy sequence is enumerated once`` () =
        let mutable enumerations = 0

        let once =
            seq {
                enumerations <- enumerations + 1
                yield! fallRows
            }

        let fig =
            plot once {
                x U.second (fun r -> r.Elapsed)
                y U.metre (fun r -> r.Drop)
                color (fun r -> r.Sample)
                size U.kilogram (fun r -> r.Mass)
            }

        render fig |> ignore
        Assert.Equal(1, enumerations)

/// **Gate 3 — the row type cannot be mismatched.**
///
/// D44 finding 4 is the reason the block is flat, and the finding is about something that
/// *compiles*: a nested `encode { … }` reaches its builder as a generic value, so its `'r` is
/// inferred from the accessors inside the block and never unified with the data's. A block whose
/// accessors take an unrelated record then compiles, runs, and fails at the unbox — or does not
/// fail at all.
///
/// A passing test cannot demonstrate that, because the code it would have to contain is code
/// that must not compile. So these run the snippet under `dotnet fsi` against the build in hand
/// and assert on the compiler's own diagnostic. D44's closing note asked for exactly this: "the
/// row-type check in Finding 4 should be a compile-failure test rather than prose."
///
/// The positive control is not optional. A snippet that fails to compile because of a bad `#r`
/// would make both negative cases vacuously green, which is the failure mode `notebooks.md`
/// records costing a day.
module BlockTypeChecking =

    open System
    open System.Diagnostics
    open System.IO
    open System.Text

    /// The assemblies a block needs, resolved from the test binary's own directory so the
    /// snippets are checked against this working tree rather than against nuget.org.
    let private preamble () =
        let binDir = Path.GetDirectoryName(Uri(typeof<Marey.Figure>.Assembly.Location).LocalPath)
        let sb = StringBuilder()

        for name in [ "Marey.Core"; "Marey.Units"; "Marey.Plot" ] do
            sb.AppendLine(sprintf "#r @\"%s\"" (Path.Combine(binDir, name + ".dll"))) |> ignore

        sb.AppendLine("open Marey").ToString()

    /// The row types every snippet below shares. Two records over the same measures, so the only
    /// thing that can distinguish them is the row type itself.
    let private rowTypes =
        String.concat
            "\n"
            [ "type Row = { Time: float<s>; Displacement: float<m> }"
              "type Other = { Elapsed: float<s> }"
              "let rows = [ { Time = 0.0<s>; Displacement = 1.0<m> } ]" ]

    /// Compile one snippet and return fsi's exit code and its combined output.
    let private compileSnippet (name: string) (body: string) =
        let tool = Tools.dotnet.Force()

        if not tool.IsAvailable then
            None
        else
            let scriptPath = Path.Combine(Path.GetTempPath(), sprintf "marey-block-%s.fsx" name)
            File.WriteAllText(scriptPath, preamble () + "\n" + rowTypes + "\n" + body + "\n", UTF8Encoding false)

            match Toolchain.run tool (sprintf "fsi --nologo \"%s\"" scriptPath) (Path.GetTempPath()) 600_000 with
            | ToolUnavailable _ -> None
            | ToolTimedOut why -> failwith why
            | ToolRan run -> Some(run.ExitCode, scriptPath, run.Out + "\n" + run.Error)

    /// The positive control: the same block, correct, must compile and run.
    [<Fact>]
    let ``the reference block compiles`` () =
        match
            compileSnippet
                "ok"
                (String.concat
                    "\n"
                    [ "let fig ="
                      "    plot rows {"
                      "        x U.second (fun r -> r.Time)"
                      "        y U.metre (fun r -> r.Displacement)"
                      "    }"
                      "printfn \"panels %d\" fig.Panels.Length" ])
        with
        | None -> Assert.True(true, "dotnet not found on PATH; the block was not compiled")
        | Some(exitCode, path, output) ->
            Assert.True((exitCode = 0), sprintf "the reference block at %s did not compile.\n%s" path output)

    /// D44 finding 4, as a compile failure: an accessor over an unrelated record type.
    ///
    /// The asserted message is the one the decision quotes, because the *shape* of the error is
    /// the guarantee — a caller has to be told that the row is wrong, not handed a mismatch
    /// somewhere inside a builder they did not write.
    [<Fact>]
    let ``an accessor over an unrelated row type does not compile`` () =
        match
            compileSnippet
                "row"
                (String.concat
                    "\n"
                    [ "let fig ="
                      "    plot rows {"
                      "        x U.second (fun (o: Other) -> o.Elapsed)"
                      "        y U.metre (fun r -> r.Displacement)"
                      "    }"
                      "printfn \"panels %d\" fig.Panels.Length" ])
        with
        | None -> Assert.True(true, "dotnet not found on PATH; the block was not compiled")
        | Some(exitCode, path, output) ->
            Assert.True(exitCode <> 0, sprintf "a block over an unrelated row type compiled. Script: %s\n%s" path output)
            Assert.Contains("error FS0001", output)
            Assert.Contains("'Other'", output)

    /// D43, as a compile failure: the witness binds the measure and the accessor is checked
    /// against it, so naming the wrong field is an error **in the measures** rather than a
    /// mislabelled axis. This is the claim that the block is no weaker than `Chart.line`.
    [<Fact>]
    let ``an accessor that disagrees with its witness does not compile`` () =
        match
            compileSnippet
                "measure"
                (String.concat
                    "\n"
                    [ "let fig ="
                      "    plot rows {"
                      "        x U.second (fun r -> r.Displacement)"
                      "        y U.metre (fun r -> r.Displacement)"
                      "    }"
                      "printfn \"panels %d\" fig.Panels.Length" ])
        with
        | None -> Assert.True(true, "dotnet not found on PATH; the block was not compiled")
        | Some(exitCode, path, output) ->
            Assert.True(exitCode <> 0, sprintf "metres under a second witness compiled. Script: %s\n%s" path output)
            Assert.Contains("error FS0001", output)
            Assert.Contains("does not match the unit of measure", output)
