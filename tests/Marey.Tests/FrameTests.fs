/// **frames, and the contract that makes rendering them in parallel honest.**
///
/// §7's headline is structural: an animation is a pure function of time, so frames come out in
/// any order and `Array.Parallel.map` is the whole renderer. Three things have to be true for
/// that to be more than a slogan, and this file is those three.
///
/// * **Seeking is evaluation.** A frame rendered alone is the frame rendered in sequence, and
///   the sequence rendered backwards is the same sequence. Both have a positive control, since
///   a gate that only compares a renderer with itself passes on a renderer that returns a
///   constant.
/// * **Parallel is serial.** Byte for byte, every frame, on the two backends that can be built
///   here — and the Skia half is the one that would catch the failure D77 was actually written
///   about, because a typeface resolved differently on another thread is a different PNG.
/// * **The measuring is bounded.** Not by the frames. `MemoTextMeasurer` is what makes one
///   shared measurer affordable, and its promise, stated exactly, is that no string is ever
///   measured twice.
///
/// ### One of D77's gates is false as it was written, and the truth is more useful
///
/// The plan's gate 4 says "frames after the first cause zero measurements", on the reasoning
/// that D72's frozen arrangement makes every frame ask for the same strings. The arrangement is
/// frozen; the *tick values inside it* are not, and a domain that sweeps produces labels the
/// union pass never sampled. Measured on a morph-then-zoom of 126 frames: 39 measurements after
/// frame 0, 64 by the end.
///
/// What is true is the stronger statement, and it is what `TheCache` gates: the measuring is
/// bounded by the **distinct strings in the shot**, not by the frames. Quadrupling the frame
/// rate on that same film took the count from 64 to 65.
module Marey.Tests.FrameTests

open System
open System.IO
open Xunit
open Marey
open Marey.UnitsOfMeasure
open Marey.Storyboard
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// Fixtures
// ======================================================================================

let private surface = Chart.size 640.0 400.0

let private times = Array.init 10 (fun i -> float i * 1.0<s>)

let private points (f: float -> float) : Figure =
    Chart.scatterIn U.second U.metre times (times |> Array.map (fun v -> f (float v) * 1.0<m>))

let private ramp = points id
let private steep = points (fun v -> v * v * 7.3)

/// A film whose **text never changes**: one figure, drawn, held, and one mark picked out. The
/// domain does not move, so neither does a tick label.
let private stillText: Story<unit> =
    scene {
        let! c = draw ramp |> over 0.4<s>
        do! wait 0.4<s>
        do! highlight c.[3] |> over 0.4<s>
    }

/// A film whose text changes constantly: the data morphs and then the y axis sweeps two
/// decades, so the tick labels are a moving set of strings.
let private movingText: Story<unit> =
    scene {
        let! c = draw ramp |> over 0.2<s>
        do! c |> morphTo steep |> over 2.0<s>
        do! c |> zoomToY (0.0, 1234.0) |> over 2.0<s>
    }

let private filmOf (story: Story<unit>) = Film.ofStory 32 surface story

/// A scratch directory that cleans up after itself.
let private inTempDir (body: string -> unit) =
    let dir =
        Path.Combine(Path.GetTempPath(), "marey-frames-" + Guid.NewGuid().ToString("N").Substring(0, 12))

    try
        body dir
    finally
        if Directory.Exists dir then
            Directory.Delete(dir, true)

/// A measurer that records the **first** `FontSpec` it is asked about, in order. Behind a
/// `MemoTextMeasurer` it therefore records the order in which faces were first resolved, which
/// is what `SkiaTextMeasurer.ResolvedFaces` reports and what D77 requires not to depend on how
/// many threads were rendering.
[<Sealed>]
type private RecordingMeasurer(inner: ITextMeasurer) =
    let gate = obj ()
    let seen = ResizeArray<FontSpec>()

    member _.Specs: FontSpec[] = lock gate (fun () -> seen.ToArray())

    interface ITextMeasurer with
        member _.Measure (spec: FontSpec) (text: string) =
            lock gate (fun () ->
                if not (seen.Contains spec) then
                    seen.Add spec)

            inner.Measure spec text

// ======================================================================================
// The clock — how many frames there are, and when
// ======================================================================================

module TheClock =

    [<Fact>]
    let ``a still is one frame, not none`` () =
        Assert.Equal(1, Frames.count 30.0 0.0<s>)
        Assert.Equal<float<s>[]>([| 0.0<s> |], Frames.times 30.0 0.0<s>)

    /// Two seconds at thirty is sixty frames, and the sixtieth is at 59/30 rather than at 2.
    /// A frame occupies the interval that starts at its own time, so a sixty-first frame at
    /// `t = 2` would hold the final state for an extra frame — a visible stutter in a loop.
    [<Fact>]
    let ``the final instant is not a frame`` () =
        let ts = Frames.times 30.0 2.0<s>
        Assert.Equal(60, ts.Length)
        Assert.Equal(0.0<s>, ts.[0])
        Assert.Equal(59.0 / 30.0, float ts.[59], 12)
        Assert.True(float ts.[59] < 2.0)

    [<Fact>]
    let ``frame i is at i over fps`` () =
        let fps = 24.0
        let ts = Frames.times fps 3.0<s>
        Assert.Equal(72, ts.Length)

        ts
        |> Array.iteri (fun i t -> Assert.Equal(float i / fps, float t, 12))

    [<Fact>]
    let ``a frame rate that is not a positive finite number is refused`` () =
        for bad in [ 0.0; -30.0; nan; infinity ] do
            Assert.Throws<ArgumentException>(fun () -> Frames.count bad 1.0<s> |> ignore)
            |> ignore

    [<Fact>]
    let ``a render nobody meant to start is refused rather than attempted`` () =
        Assert.Throws<ArgumentException>(fun () -> Frames.count 60.0 1.0e9<s> |> ignore)
        |> ignore

// ======================================================================================
// Gate 1 — seeking is evaluation
// ======================================================================================

module Seeking =

    /// The gate: the renderer accumulates nothing between frames. A frame rendered on its own
    /// is byte-identical to the same frame inside a sequence.
    [<Fact>]
    let ``a frame rendered alone is the frame rendered in sequence`` () =
        let film = filmOf movingText
        let all = Frames.render FramePass.Serial SvgBackend.render 12.0 film
        let ts = Frames.times 12.0 film.Duration

        for i in [ 0; 3; 11; ts.Length - 1 ] do
            let alone = SvgBackend.render film.Extent (film.Frames.At ts.[i])
            Assert.Equal(all.[i], alone)

    /// The positive control for the gate above: the frames are not all the same string, so
    /// "rendered alone equals rendered in sequence" is a claim about state rather than a
    /// tautology about a constant renderer.
    [<Fact>]
    let ``the frames of a moving film are not all the same frame`` () =
        let all = Frames.render FramePass.Serial SvgBackend.render 12.0 (filmOf movingText)
        Assert.True(all |> Array.distinct |> Array.length > 1)

    /// Out of order is the same as in order, which is the property Manim's replayed timeline
    /// cannot have.
    [<Fact>]
    let ``the sequence rendered backwards is the same sequence`` () =
        let film = filmOf movingText
        let ts = Frames.times 12.0 film.Duration
        let forwards = ts |> Array.map (fun t -> SvgBackend.render film.Extent (film.Frames.At t))

        let backwards =
            Array.init ts.Length (fun i -> ts.Length - 1 - i)
            |> Array.map (fun i -> i, SvgBackend.render film.Extent (film.Frames.At ts.[i]))
            |> Array.sortBy fst
            |> Array.map snd

        Assert.Equal<string[]>(forwards, backwards)

// ======================================================================================
// Gate 2 and gate 10 — parallel equals serial, and a render equals itself
// ======================================================================================

module TheTwoPasses =

    [<Fact>]
    let ``a concurrent render is the serial one, frame for frame`` () =
        let film = filmOf movingText
        let serial = Frames.render FramePass.Serial SvgBackend.render 24.0 film
        let concurrent = Frames.render FramePass.Concurrent SvgBackend.render 24.0 film
        Assert.Equal<string[]>(serial, concurrent)

    /// Gate 10. Two renders of one film, byte for byte — the gate that fails the moment a clock
    /// or a hash order leaks into the output.
    [<Fact>]
    let ``two renders of one film are identical`` () =
        let film = filmOf movingText
        let first = Frames.render FramePass.Concurrent SvgBackend.render 24.0 film
        let second = Frames.render FramePass.Concurrent SvgBackend.render 24.0 film
        Assert.Equal<string[]>(first, second)

    /// Two *films* built separately from one story, rendered by different passes. This is the
    /// stronger version: it re-runs the union arrangement as well as the frame loop, so it also
    /// says the layout is not accumulating anything.
    [<Fact>]
    let ``two films of one story are identical`` () =
        let serial = Frames.render FramePass.Serial SvgBackend.render 24.0 (filmOf movingText)
        let concurrent = Frames.render FramePass.Concurrent SvgBackend.render 24.0 (filmOf movingText)
        Assert.Equal<string[]>(serial, concurrent)

    /// The Skia half, and the one that would actually catch D77's failure: a face resolved
    /// differently on another thread is a different PNG, and no amount of SVG comparison sees
    /// it because the SVG names a family and lets the viewer resolve it.
    [<Fact>]
    let ``a concurrent png render is the serial one, byte for byte`` () =
        SkiaTests.withSkia (fun () ->
            let film = Film.ofStory 8 surface stillText
            let serial = Frames.render FramePass.Serial SkiaBackend.renderPng 8.0 film
            let concurrent = Frames.render FramePass.Concurrent SkiaBackend.renderPng 8.0 film
            Assert.Equal(serial.Length, concurrent.Length)
            Array.iter2 (fun (a: byte[]) (b: byte[]) -> Assert.Equal<byte[]>(a, b)) serial concurrent)

// ======================================================================================
// Gate 3 — one face for the whole shot
// ======================================================================================

module OneFace =

    /// The portable half, which runs everywhere rather than only where SkiaSharp loads: the
    /// **order** in which distinct faces are first asked for is the same under both passes.
    ///
    /// It is deterministic for a reason worth knowing rather than relying on: `Film.ofStory`
    /// runs D72's union arrangement on the calling thread before any frame exists, and that
    /// pass touches every `FontSpec` the figure uses. By the time sixteen threads start, there
    /// is nothing left to resolve and therefore nothing to race over.
    [<Fact>]
    let ``the faces are resolved in one order, whichever pass renders`` () =
        let run pass =
            let recorder = RecordingMeasurer(TextMeasure.approx)
            let film = Film.ofStoryWith 32 (MemoTextMeasurer recorder) surface movingText
            Frames.render pass SvgBackend.render 24.0 film |> ignore
            recorder.Specs

        let serial = run FramePass.Serial
        let concurrent = run FramePass.Concurrent
        Assert.True(serial.Length > 1, "a figure with one font is not a test of face ordering")
        Assert.Equal<FontSpec[]>(serial, concurrent)

    /// The Skia half: `ResolvedFaces` is D62's report, and D77's requirement is that a render
    /// has exactly one of them. Same measurer, both passes, same report in the same order.
    [<Fact>]
    let ``the resolved faces after a concurrent render are the serial ones`` () =
        SkiaTests.withSkia (fun () ->
            let run pass =
                use inner = new SkiaTextMeasurer()
                let film = Film.ofStoryWith 8 (MemoTextMeasurer inner) surface stillText
                Frames.render pass SvgBackend.render 8.0 film |> ignore
                inner.ResolvedFaces

            let serial = run FramePass.Serial
            let concurrent = run FramePass.Concurrent
            Assert.NotEmpty serial
            Assert.Equal<ResolvedFace[]>(serial, concurrent))

// ======================================================================================
// Gate 4 — the measuring is bounded, and by what
// ======================================================================================

module TheCache =

    /// The cache's promise, stated exactly: in a serial render every string reaches the
    /// measurer underneath **once**. `Measurements` counts what got through; `Entries` counts
    /// what was asked. They are equal or the cache is not a cache.
    [<Fact>]
    let ``no string is measured twice`` () =
        let memo = MemoTextMeasurer TextMeasure.approx
        let film = Film.ofStoryWith 32 memo surface movingText
        Frames.render FramePass.Serial SvgBackend.render 30.0 film |> ignore
        Assert.True(memo.Entries > 0)
        Assert.Equal(memo.Entries, memo.Measurements)

    /// D77's own gate, in the case where its premise holds: a film whose text does not change
    /// asks nothing new after the first frame.
    [<Fact>]
    let ``a film whose text never changes measures nothing after the first frame`` () =
        let memo = MemoTextMeasurer TextMeasure.approx
        let film = Film.ofStoryWith 32 memo surface stillText
        let ts = Frames.times 30.0 film.Duration
        Assert.True(ts.Length > 10)

        SvgBackend.render film.Extent (film.Frames.At ts.[0]) |> ignore
        let afterFirst = memo.Measurements

        for i in 1 .. ts.Length - 1 do
            SvgBackend.render film.Extent (film.Frames.At ts.[i]) |> ignore

        Assert.Equal(afterFirst, memo.Measurements)

    /// The correction. A film whose **domain sweeps** produces tick labels the union pass never
    /// sampled, so the count above does move — and a gate that did not say so would be
    /// describing a library that does not exist.
    [<Fact>]
    let ``a film whose text moves does measure again`` () =
        let memo = MemoTextMeasurer TextMeasure.approx
        let film = Film.ofStoryWith 32 memo surface movingText
        let ts = Frames.times 30.0 film.Duration

        SvgBackend.render film.Extent (film.Frames.At ts.[0]) |> ignore
        let afterFirst = memo.Measurements

        for i in 1 .. ts.Length - 1 do
            SvgBackend.render film.Extent (film.Frames.At ts.[i]) |> ignore

        Assert.True(
            memo.Measurements > afterFirst,
            "a two-decade zoom that measures nothing new is a zoom whose labels are not moving"
        )

    /// And what *is* bounded. Four times the frames over the same film, and the measuring
    /// barely moves, because it is bounded by the distinct strings in the shot rather than by
    /// the frames. Without the cache this is the difference between 126 and 504 layouts' worth
    /// of measuring.
    [<Fact>]
    let ``four times the frames is not four times the measuring`` () =
        let measure fps =
            let memo = MemoTextMeasurer TextMeasure.approx
            let film = Film.ofStoryWith 32 memo surface movingText
            let n = (Frames.render FramePass.Serial SvgBackend.render fps film).Length
            n, memo.Measurements

        let n30, m30 = measure 30.0
        let n120, m120 = measure 120.0

        Assert.Equal(4 * n30, n120)
        Assert.True(m120 < 2 * m30, sprintf "%d frames measured %d times; %d frames measured %d" n30 m30 n120 m120)

    [<Fact>]
    let ``a memoised measurer answers what the one underneath answers`` () =
        let inner = TextMeasure.approx
        let memo = MemoTextMeasurer inner :> ITextMeasurer

        let spec: FontSpec =
            { Family = Theme.Sans
              Size = 11.0<pt>
              Weight = 400
              Italic = false }

        for text in [ ""; "0"; "120"; "t [s]"; "x [m·s⁻¹]"; "a much longer label than the others" ] do
            let want = inner.Measure spec text
            Assert.Equal(want, memo.Measure spec text)
            Assert.Equal(want, memo.Measure spec text)

    /// The key is the pair, not the string: the same text at a different size is a different
    /// question, and a cache that dropped the `FontSpec` would answer the second with the
    /// first's width.
    [<Fact>]
    let ``the same string at two sizes is two questions`` () =
        let memo = MemoTextMeasurer TextMeasure.approx

        let small: FontSpec =
            { Family = Theme.Sans
              Size = 8.0<pt>
              Weight = 400
              Italic = false }

        let large = { small with Size = 24.0<pt> }
        let m = memo :> ITextMeasurer
        let a = m.Measure small "1000"
        let b = m.Measure large "1000"
        Assert.Equal(2, memo.Entries)
        Assert.True(b.Advance > a.Advance)

    /// Hammered from every thread at once: the answers are the inner measurer's, and the cache
    /// never invents one. `Measurements` may exceed `Entries` here — two threads missing on one
    /// key at one instant both compute it — and that is the documented cost of leaving the miss
    /// path unlocked.
    [<Fact>]
    let ``a memoised measurer is safe to share`` () =
        let inner = TextMeasure.approx
        let memo = MemoTextMeasurer inner
        let m = memo :> ITextMeasurer

        let specs =
            [| 400; 700 |]
            |> Array.map (fun w ->
                { Family = Theme.Sans
                  Size = 9.0<pt>
                  Weight = w
                  Italic = false }: FontSpec)

        let texts = Array.init 40 (fun i -> string (i * 7))

        let work =
            Array.init 64 (fun j ->
                fun () ->
                    for i in 0 .. texts.Length - 1 do
                        let spec = specs.[(i + j) % specs.Length]
                        Assert.Equal(inner.Measure spec texts.[i], m.Measure spec texts.[i]))

        Array.Parallel.iter (fun f -> f ()) work

        Assert.Equal(specs.Length * texts.Length, memo.Entries)
        Assert.True(memo.Measurements >= memo.Entries)

// ======================================================================================
// The writer
// ======================================================================================

module TheWriter =

    [<Fact>]
    let ``the names are the pattern an encoder is pointed at`` () =
        let target = FrameTarget.into "/tmp/nowhere" ".png"
        Assert.Equal("frame-%05d.png", FrameTarget.pattern target)
        Assert.EndsWith("frame-00000.png", FrameTarget.fileFor target 0)
        Assert.EndsWith("frame-00042.png", FrameTarget.fileFor target 42)

    /// An extension without its dot is a name like `frame-000001svg`, which nothing opens.
    [<Fact>]
    let ``an extension is given its dot`` () =
        Assert.Equal("frame-%05d.svg", FrameTarget.pattern (FrameTarget.into "/tmp/nowhere" "svg"))

    [<Fact>]
    let ``every frame is written, numbered, in order`` () =
        inTempDir (fun dir ->
            let film = filmOf stillText
            let seq' = Frames.writeText FramePass.Concurrent SvgBackend.render (FrameTarget.into dir ".svg") 10.0 film

            Assert.Equal(Frames.count 10.0 film.Duration, seq'.Files.Length)
            Assert.Empty seq'.Stale
            Assert.Equal(film.Extent, seq'.Extent)
            Assert.Equal(10.0, seq'.Fps)

            let onDisk = Directory.GetFiles(dir, "*.svg") |> Array.sort
            Assert.Equal<string[]>(seq'.Files |> Array.map Path.GetFullPath, onDisk |> Array.map Path.GetFullPath)

            // The files are the frames, in order, and not one file written many times.
            let written = seq'.Files |> Array.map File.ReadAllText
            let rendered = Frames.render FramePass.Serial SvgBackend.render 10.0 film
            Assert.Equal<string[]>(rendered, written))

    /// UTF-8 with no byte-order mark, matching `SvgBackend.renderToFile`. A BOM in frame 0 and
    /// not in frame 1 is the kind of difference that only shows up in a diff of two goldens.
    [<Fact>]
    let ``text frames are written without a byte-order mark`` () =
        inTempDir (fun dir ->
            Frames.writeText FramePass.Serial SvgBackend.render (FrameTarget.into dir ".svg") 4.0 (filmOf stillText)
            |> ignore

            let first = Directory.GetFiles(dir, "*.svg") |> Array.sort |> Array.head
            let bytes = File.ReadAllBytes first
            Assert.NotEqual<byte[]>([| 0xEFuy; 0xBBuy; 0xBFuy |], bytes.[0..2]))

    /// The trap this reports: a directory holding a previous, longer render still has frames
    /// past the end of this one, and `ffmpeg -i frame-%05d.png` reads until the first gap. The
    /// files are left alone — deciding to delete somebody's directory is not the writer's call
    /// — and named, so the caller can.
    [<Fact>]
    let ``frames left over from a longer render are reported and not deleted`` () =
        inTempDir (fun dir ->
            let target = FrameTarget.into dir ".svg"
            let long' = Frames.writeText FramePass.Serial SvgBackend.render target 20.0 (filmOf stillText)
            let short' = Frames.writeText FramePass.Serial SvgBackend.render target 5.0 (filmOf stillText)

            Assert.True(long'.Files.Length > short'.Files.Length)
            Assert.Equal(long'.Files.Length - short'.Files.Length, short'.Stale.Length)
            Assert.All(short'.Stale, fun p -> Assert.True(File.Exists p))

            let expected =
                long'.Files.[short'.Files.Length ..] |> Array.map Path.GetFullPath |> Array.sort

            Assert.Equal<string[]>(expected, short'.Stale))

    /// More frames than the index can name would write two of them to one file, and the render
    /// would look like it worked.
    [<Fact>]
    let ``more frames than digits is refused`` () =
        inTempDir (fun dir ->
            let target = { FrameTarget.into dir ".svg" with Digits = 1 }

            Assert.Throws<ArgumentException>(fun () ->
                Frames.writeText FramePass.Serial SvgBackend.render target 30.0 (filmOf stillText)
                |> ignore)
            |> ignore)

    [<Fact>]
    let ``the directory is created rather than demanded`` () =
        inTempDir (fun dir ->
            let nested = Path.Combine(dir, "a", "b")
            Assert.False(Directory.Exists nested)

            Frames.writeText FramePass.Serial SvgBackend.render (FrameTarget.into nested ".svg") 4.0 (filmOf stillText)
            |> ignore

            Assert.True(Directory.Exists nested))

// ======================================================================================
// Gate 15 — the SVG oracle, and the film's own surface
// ======================================================================================

module TheFilm =

    /// §6 treats SVG as the spec, and the cheapest thing that claim buys is an oracle: every
    /// frame of a sampled animation has to be well-formed XML. A scene that breaks a backend's
    /// escaping only at one instant of one animation is exactly the defect this catches.
    [<Fact>]
    let ``every frame of a film is well-formed xml`` () =
        let film = filmOf movingText

        Frames.render FramePass.Concurrent SvgBackend.render 24.0 film
        |> Array.iteri (fun i svg ->
            let doc = Xml.Linq.XDocument.Parse svg
            Assert.Equal("svg", doc.Root.Name.LocalName)
            Assert.True(svg.Length > 100, sprintf "frame %d is %d characters" i svg.Length))

    /// The reason `Film` is a type rather than two arguments: the surface a film is rendered at
    /// is the one its shot was arranged against, and there is no second place to state it.
    [<Fact>]
    let ``a film is rendered at the surface it was arranged for`` () =
        let film = filmOf stillText
        Assert.Equal(surface, film.Extent)
        Assert.Equal(32, film.Shot.Samples)
        Assert.Equal(Storyboard.duration stillText, film.Duration)

    /// D72's primitive without the block: an `Anim<Figure>` is a film too, and it is laid out
    /// against the same union.
    [<Fact>]
    let ``an animation of figures is a film`` () =
        let anim = Anim.ofFraction 1.0<s> (fun u -> points (fun v -> v * (1.0 + 9.0 * u)))
        let film = Film.ofFigures 16 surface anim
        Assert.Equal(surface, film.Extent)
        Assert.Equal(16, film.Shot.Samples)
        Assert.Equal(1.0<s>, film.Duration)

        let frames = Frames.render FramePass.Serial SvgBackend.render 12.0 film
        Assert.Equal(12, frames.Length)
        Assert.True(frames |> Array.distinct |> Array.length > 1)

    /// The panel does not breathe here either. `ShotTests` gates this for a `Shot`; a film adds
    /// the frame loop around it, and the frame loop is where an arrangement would escape back
    /// into per-frame layout without anything else noticing.
    [<Fact>]
    let ``the plot rectangle is identical across every frame of a film`` () =
        let film = filmOf movingText

        let boxes =
            Frames.render FramePass.Serial (fun _ n -> n) 24.0 film
            |> Array.map (fun n ->
                n
                |> Scene.fold
                    (fun acc x ->
                        match x with
                        | Transform(a, _) -> a :: acc
                        | _ -> acc)
                    [])

        Assert.True(boxes.Length > 20)
        Assert.Single(boxes |> Array.distinct) |> ignore
