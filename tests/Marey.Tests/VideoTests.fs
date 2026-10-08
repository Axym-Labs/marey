/// D75 / D76 — **the mux, and what a gate on a video can actually be.**
///
/// D75 said the mux "is testable only for exit status and container-level facts". That turned
/// out to undersell it in one direction and oversell it in another, and both corrections are in
/// this file.
///
/// * **Undersold: the argv is pure, so most of this runs with no ffmpeg installed.** D76 lives
///   entirely in the argument vector — which flag precedes `-i` and which follows it — and
///   `Video.args` is a function from options to a list of strings. `TheArgv`, `TheRefusals` and
///   `TheBuildTags` need no encoder at all.
/// * **Oversold: "exit status" is a gate that passes on a broken encode.** Moving `-framerate`
///   from before the input to after it turns 24 frames at 12 fps into 13 frames, and ffmpeg
///   exits 0 both times. `TheEncode` asserts the frame count read back out of the container,
///   which is what `Video.inspect` exists for.
///
/// ### The determinism gate D76 asked for is vacuous, and this file does not use it
///
/// "Mux twice, compare bytes" passes **without** the bit-exact flags: measured on ffmpeg n9.0.1,
/// two consecutive encodes of one sequence are byte-identical whether or not `-fflags +bitexact`
/// is passed. What the flag removes — `encoder=Lavf63.1.101` on the container and
/// `encoder=Lavc63.1.101 libx264` on the stream — does not vary between two runs on one machine.
/// It varies between *builds*, which is D63's `/CreationDate` in a slower-moving currency.
///
/// So the byte-comparison is kept, because it does gate the encoder's own determinism, and the
/// gate that actually falsifies D76 is `the container names no ffmpeg build`: with the flags
/// `VideoInfo.BuildTags` is empty, and with them removed it is those two tags.
module Marey.Tests.VideoTests

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

/// A scratch directory that cleans up after itself. `FrameTests`' helper, which is private
/// there and small enough that sharing it would couple two files for four lines.
let private inTempDir (body: string -> unit) =
    let dir =
        Path.Combine(Path.GetTempPath(), "marey-video-" + Guid.NewGuid().ToString("N").Substring(0, 12))

    // Created here rather than left to the first writer: not every gate below starts by writing
    // a frame, and one that starts by writing a decoy file should fail for its own reason.
    Directory.CreateDirectory dir |> ignore

    try
        body dir
    finally
        if Directory.Exists dir then
            Directory.Delete(dir, true)

/// `n` distinct frames of `w` x `h`, written with Marey's own zero-dependency PNG encoder.
///
/// **Synthetic rather than rendered, and that is three advantages rather than a shortcut.** What
/// these gates are about is the mapping from a numbered directory to a container, and the
/// frames' content is no part of it. It lets a gate ask for an *odd* pixel size, which no chart
/// extent would naturally produce and which is exactly the case `Video.check` refuses. And it
/// means the encode is gated on a machine with no Skia native assets, so the video gate and the
/// raster gate fail independently. The end-to-end path through a real film is gated once, at the
/// bottom of the file.
let private syntheticSequence (dir: string) (w: int) (h: int) (n: int) (fps: float) : FrameSequence =
    Directory.CreateDirectory dir |> ignore
    let target = FrameTarget.into dir ".png"

    let files =
        Array.init n (fun i ->
            // Every frame different, so a muxer that encoded one frame n times could not pass
            // the frame-count gate by accident.
            let rgba =
                Array.init (w * h * 4) (fun k -> if k % 4 = 3 then 255uy else byte ((i * 37 + k * 5) % 256))

            let path = FrameTarget.fileFor target i
            File.WriteAllBytes(path, Png.encode w h rgba)
            path)

    { Directory = dir
      Pattern = FrameTarget.pattern target
      Files = files
      Fps = fps
      Extent =
        { Width = float w * 1.0<px>
          Height = float h * 1.0<px> }
      Duration = LanguagePrimitives.FloatWithMeasure<s>(float n / fps)
      Pass = FramePass.Serial
      Stale = [||] }

/// A sequence that needs no files on disk, for the gates that only read its fields.
let private paperSequence (fps: float) (n: int) : FrameSequence =
    { Directory = "/nowhere"
      Pattern = "frame-%05d.png"
      Files = Array.init n (fun i -> sprintf "/nowhere/frame-%05d.png" i)
      Fps = fps
      Extent =
        { Width = 320.0<px>
          Height = 240.0<px> }
      Duration = LanguagePrimitives.FloatWithMeasure<s>(float n / fps)
      Pass = FramePass.Concurrent
      Stale = [||] }

/// An ffmpeg that is not installed, for the gate on degrading rather than failing.
let private noFfmpeg: FfmpegReport =
    { Tool =
        { Name = "ffmpeg"
          Path = None
          Install = "a test's idea of ffmpeg" }
      Version = None }

/// Run a gate that needs the encoder, or say loudly that it was not run.
///
/// `SkiaTests.withSkia`'s shape, and D81's rule: the wording of "it is not installed" belongs to
/// one place, so this prints `Video.report ()` rather than inventing a second phrasing.
let private withFfmpeg (body: unit -> unit) =
    if (Video.ffmpeg ()).IsAvailable then
        body ()
    else
        Console.WriteLine("SKIPPED: " + Video.report ())

/// The same, for the reader.
let private withFfprobe (body: unit -> unit) =
    let tool = Video.ffprobe ()

    if tool.IsAvailable then
        body ()
    else
        Console.WriteLine("SKIPPED: " + Toolchain.report "the container was not read back" tool)

let private indexOf (needle: string) (argv: string list) = List.findIndex ((=) needle) argv

// ======================================================================================
// The argument vector, which is where D76 lives — and none of this needs ffmpeg
// ======================================================================================

module TheArgv =

    let private argv () =
        Video.args MuxOptions.Default (paperSequence 24.0 48) "/tmp/out.mp4"

    /// **The one that matters.** `-framerate` before `-i` says what rate the images are and
    /// keeps every one of them; the same number after `-i` resamples, dropping or duplicating
    /// frames against the demuxer's default of 25. Measured: 24 frames at 12 fps is 24 frames
    /// written correctly and 13 with the flag moved.
    [<Fact>]
    let ``the frame rate is an input option, ahead of the input`` () =
        let a = argv ()
        Assert.True(indexOf "-framerate" a < indexOf "-i" a)
        Assert.True(indexOf "-start_number" a < indexOf "-i" a)

    /// The mirror image, and just as silent: after the demuxer, `-fflags +bitexact` configures
    /// the *reader* and the container keeps its encoder tag anyway.
    [<Fact>]
    let ``the bit-exact flags are output options, behind the input`` () =
        let a = argv ()
        let input = indexOf "-i" a

        Assert.True(indexOf "-fflags" a > input)
        Assert.True(indexOf "-flags" a > input)
        Assert.True(indexOf "-map_metadata" a > input)
        Assert.Equal("+bitexact", a.[indexOf "-fflags" a + 1])
        Assert.Equal("+bitexact", a.[indexOf "-flags" a + 1])
        Assert.Equal("-1", a.[indexOf "-map_metadata" a + 1])

    [<Fact>]
    let ``the input is the sequence's own pattern, and the output is last`` () =
        let a = argv ()
        Assert.Equal("frame-%05d.png", a.[indexOf "-i" a + 1])
        Assert.Equal("/tmp/out.mp4", List.last a)

    /// The rate written into the container is the rate the frames were rendered at, and nothing
    /// else can say otherwise: `MuxOptions` deliberately carries no frame rate.
    [<Fact>]
    let ``the rate in the argv is the rate in the sequence`` () =
        for fps in [ 12.0; 24.0; 25.0; 30.0; 30000.0 / 1001.0; 60.0 ] do
            let a = Video.args MuxOptions.Default (paperSequence fps 10) "/tmp/out.mp4"
            let written = Double.Parse(a.[indexOf "-framerate" a + 1], Globalization.CultureInfo.InvariantCulture)

            // Twelve decimals, so 30000/1001 survives as something ffmpeg converts back to the
            // rational meant rather than to 29.97 exactly.
            Assert.True(abs (written - fps) < 1e-9, sprintf "%.12f written as %s" fps a.[indexOf "-framerate" a + 1])

    /// Either `-y` or `-n`, always. Passing neither leaves ffmpeg to ask on a stdin nothing
    /// redirects, and a question nobody can see is a process that hangs until the timeout.
    [<Fact>]
    let ``an existing output is answered rather than asked about`` () =
        let yes = Video.args MuxOptions.Default (paperSequence 24.0 4) "/tmp/o.mp4"
        let no = Video.args { MuxOptions.Default with Overwrite = false } (paperSequence 24.0 4) "/tmp/o.mp4"

        Assert.Contains("-y", yes)
        Assert.DoesNotContain("-n", yes)
        Assert.Contains("-n", no)
        Assert.DoesNotContain("-y", no)

    /// ffmpeg reads stdin for interactive keys. Under a test runner that is the runner's stdin.
    [<Fact>]
    let ``the encoder does not read stdin`` () = Assert.Contains("-nostdin", argv ())

    /// A `.gif` or a `.webm` has no use for `-c:v libx264 -crf`, and `None` means the flag is
    /// absent rather than empty.
    [<Fact>]
    let ``an encoder with no knobs is given none`` () =
        let a =
            Video.args
                { MuxOptions.Default with
                    Codec = None
                    Crf = None
                    PixelFormat = None }
                (paperSequence 24.0 4)
                "/tmp/o.gif"

        Assert.DoesNotContain("-c:v", a)
        Assert.DoesNotContain("-crf", a)
        Assert.DoesNotContain("-pix_fmt", a)

        // And the parts that are not optional are still there.
        Assert.Contains("-framerate", a)
        Assert.Contains("+bitexact", a)

    [<Fact>]
    let ``extra arguments are output options, and land before the output`` () =
        let a =
            Video.args
                { MuxOptions.Default with Extra = [ "-movflags"; "+faststart" ] }
                (paperSequence 24.0 4)
                "/tmp/o.mp4"

        Assert.True(indexOf "-movflags" a > indexOf "-i" a)
        Assert.Equal("/tmp/o.mp4", List.last a)

// ======================================================================================
// What is refused before ffmpeg is ever started
// ======================================================================================

module TheRefusals =

    /// The trap `FrameSequence.Stale` was reported for in phase 5, and this is the consumer it
    /// was reported *to*: `-i frame-%05d.png` reads until the first gap, so a leftover
    /// `frame-00060.png` from a longer render becomes a tail on a shot that no longer exists.
    [<Fact>]
    let ``a stale frame refuses the mux`` () =
        let stale =
            { paperSequence 24.0 10 with Stale = [| "/nowhere/frame-00042.png" |] }

        match Video.check MuxOptions.Default stale with
        | None -> failwith "a stale frame was not refused"
        | Some why ->
            Assert.Contains("frame-00042.png", why)
            Assert.Contains("AllowStale", why)

    [<Fact>]
    let ``a stale frame is encoded anyway when the caller says so`` () =
        let stale =
            { paperSequence 24.0 10 with Stale = [| "/nowhere/frame-00042.png" |] }

        Assert.True((Video.check { MuxOptions.Default with AllowStale = true } stale).IsNone)

    [<Fact>]
    let ``an empty sequence is refused`` () =
        match Video.check MuxOptions.Default (paperSequence 24.0 0) with
        | Some why -> Assert.Contains("nothing to encode", why)
        | None -> failwith "an empty sequence was not refused"

    /// `yuv420p` subsamples chroma and needs even dimensions. ffmpeg's own message arrives after
    /// the encoder has been built and says "width not divisible by 2"; this one names both
    /// numbers and the two ways out.
    [<Fact>]
    let ``an odd frame refuses a subsampled encode`` () =
        inTempDir (fun dir ->
            let odd = syntheticSequence dir 33 24 4 24.0

            match Video.check MuxOptions.Default odd with
            | None -> failwith "33 px wide was not refused for yuv420p"
            | Some why ->
                Assert.Contains("33 x 24", why)
                Assert.Contains("yuv420p", why)

            // The refusal is about the pixel format, not about the number: say 4:4:4 and the
            // same frames are fine.
            Assert.True((Video.check { MuxOptions.Default with PixelFormat = Some "yuv444p" } odd).IsNone)

            // And an even sequence is not refused, so the gate above is not passing on
            // everything.
            Assert.True((Video.check MuxOptions.Default (syntheticSequence dir 32 24 4 24.0)).IsNone))

    /// **The size is read out of the file, not derived from the extent.** `SkiaBackend` rounds
    /// an extent *up* to whole pixels, so a 32.5 px figure is a 33 px bitmap — and 33 is the
    /// number that decides whether the encode is legal.
    [<Fact>]
    let ``a frame's size comes from its own header`` () =
        inTempDir (fun dir ->
            let seq = syntheticSequence dir 33 25 2 24.0
            Assert.Equal(Some(33, 25), Video.frameSize seq.Files.[0])

            // A file that is not a PNG is `None` rather than a throw: the encode is about to
            // fail on the same file, in ffmpeg's own words.
            let notAnImage = Path.Combine(dir, "notes.txt")
            File.WriteAllText(notAnImage, "this is not a png")
            Assert.Equal(None, Video.frameSize notAnImage)
            Assert.Equal(None, Video.frameSize (Path.Combine(dir, "absent.png"))))

    /// A refusal is a refusal: nothing is written, and it is an outcome rather than an
    /// exception.
    [<Fact>]
    let ``a refused mux writes nothing`` () =
        inTempDir (fun dir ->
            let odd = syntheticSequence dir 33 24 4 24.0
            let output = Path.Combine(dir, "refused.mp4")

            match Video.mux MuxOptions.Default odd output with
            | MuxRefused _ -> Assert.False(File.Exists output)
            | other -> failwithf "expected a refusal, got %A" other)

// ======================================================================================
// The tag classifier, which no encode can falsify on its own
// ======================================================================================

module TheBuildTags =

    let private info (formatTags: (string * string) list) (streamTags: (string * string) list) : VideoInfo =
        { Codec = "h264"
          Width = 32
          Height = 24
          FrameCount = 8
          Fps = 24.0
          Duration = 0.3333333<s>
          FormatTags = Map.ofList formatTags
          StreamTags = Map.ofList streamTags }

    /// **Gated here rather than only through an encode**, because `the container names no ffmpeg
    /// build` asserts that this list is *empty* — so a classifier that always returned nothing
    /// would pass it, and the whole of D76 would be resting on a function nothing checks.
    [<Fact>]
    let ``a version-bearing encoder tag names a build`` () =
        let both =
            info [ "encoder", "Lavf63.1.101" ] [ "encoder", "Lavc63.1.101 libx264" ]

        Assert.Equal<(string * string) list>(
            [ "encoder", "Lavf63.1.101"; "encoder", "Lavc63.1.101 libx264" ],
            both.BuildTags
        )

    /// What `-fflags +bitexact` actually leaves behind: the library name with the version taken
    /// off. It names a library, not a build, and a classifier that flagged it would make the
    /// gate impossible to pass.
    [<Fact>]
    let ``a stripped encoder tag does not`` () =
        Assert.Empty((info [] [ "encoder", "Lavc libx264" ]).BuildTags)

        Assert.Empty(
            (info
                [ "major_brand", "isom"; "compatible_brands", "isomiso2avc1mp41" ]
                [ "handler_name", "VideoHandler"; "language", "und" ])
                .BuildTags
        )

// ======================================================================================
// Degrading, which is the behaviour D75 chose over failing
// ======================================================================================

module TheDegrade =

    /// **The headline of D75.** No ffmpeg is an outcome, not an exception, and the message says
    /// both what to install and that the frames are still there — because they are, and a
    /// numbered directory is what every editor imports.
    [<Fact>]
    let ``a missing ffmpeg leaves the frames and says so`` () =
        inTempDir (fun dir ->
            let seq = syntheticSequence dir 32 24 6 24.0
            let output = Path.Combine(dir, "never.mp4")

            match Video.muxWith noFfmpeg MuxOptions.Default seq output with
            | MuxUnavailable why ->
                Assert.Contains("ffmpeg not found on PATH", why)
                Assert.Contains("still there", why)
                Assert.Contains(seq.Pattern, why)
                Assert.False(File.Exists output)

                // The frames the message promises are on disk are on disk.
                for f in seq.Files do
                    Assert.True(File.Exists f, f)
            | other -> failwithf "expected MuxUnavailable, got %A" other)

    /// The refusal is checked *before* the tool is looked for, so a caller with a broken
    /// sequence learns about the sequence rather than about their `PATH`.
    [<Fact>]
    let ``a refusal outranks a missing encoder`` () =
        match Video.muxWith noFfmpeg MuxOptions.Default (paperSequence 24.0 0) "/tmp/never.mp4" with
        | MuxRefused _ -> ()
        | other -> failwithf "expected MuxRefused, got %A" other

    [<Fact>]
    let ``a missing ffprobe is reported rather than thrown`` () =
        let absent =
            { Name = "ffprobe"
              Path = None
              Install = "a test's idea of ffprobe" }

        match Video.inspectWith absent "/tmp/whatever.mp4" with
        | InspectUnavailable why -> Assert.Contains("ffprobe not found on PATH", why)
        | other -> failwithf "expected InspectUnavailable, got %A" other

    /// Present or absent, the probe says which and names the tool. A gate that skipped and a
    /// gate that passed are indistinguishable from outside, and this is the inside.
    [<Fact>]
    let ``the probe reports either way`` () =
        let line = Video.report ()
        Assert.Contains("ffmpeg", line)

        if (Video.ffmpeg ()).IsAvailable then
            Assert.Contains("found at", line)
        else
            Assert.Contains("To enable it", line)

        // Resolved once per process, so the second answer is the first one.
        Assert.Equal(line, Video.report ())

// ======================================================================================
// The encode, and what the container says back
// ======================================================================================

module TheEncode =

    [<Fact>]
    let ``a frame sequence becomes a playable file`` () =
        withFfmpeg (fun () ->
            inTempDir (fun dir ->
                let seq = syntheticSequence dir 32 24 12 24.0
                let output = Path.Combine(dir, "out.mp4")

                match Video.mux MuxOptions.Default seq output with
                | MuxRan r ->
                    Assert.True(r.Succeeded, sprintf "ffmpeg exited %d\n%s\n%s" r.ExitCode r.Command r.Log)
                    Assert.True(FileInfo(r.Output).Length > 0L)
                    Assert.Equal(12, r.FrameCount)
                    Assert.Equal(Some(32, 24), r.FrameSize)

                    // The command is carried because reproducing a failed encode outside the
                    // library is the first thing anybody does with one.
                    Assert.Contains("-i frame-%05d.png", r.Command)
                | other -> failwithf "expected MuxRan, got %A" other))

    /// **The gate D75's "exit status" would have missed.** Twelve frames per second, not
    /// twenty-four: at 24 the demuxer's default of 25 rounds to the same answer either way, so
    /// a misplaced `-framerate` looks correct at the rate somebody first tries. At 12 the same
    /// mistake gives 13 frames of 24, and exits 0.
    [<Fact>]
    let ``every rendered frame is in the video, exactly once`` () =
        withFfmpeg (fun () ->
            withFfprobe (fun () ->
                inTempDir (fun dir ->
                    let seq = syntheticSequence dir 32 24 24 12.0
                    let output = Path.Combine(dir, "twelve.mp4")

                    match Video.mux MuxOptions.Default seq output with
                    | MuxRan r when r.Succeeded ->
                        match Video.inspect output with
                        | Inspected info ->
                            Assert.Equal(24, info.FrameCount)
                            Assert.Equal(12.0, info.Fps, 6)
                            Assert.Equal(32, info.Width)
                            Assert.Equal(24, info.Height)
                            Assert.Equal("h264", info.Codec)
                        | other -> failwithf "expected Inspected, got %A" other
                    | other -> failwithf "expected a successful MuxRan, got %A" other)))

    /// The video plays for `frames / fps`, which is one frame period longer than the last
    /// frame's own time — because a frame *occupies* the interval that starts at its time. That
    /// is `Frames.times`' rule, arriving in the container.
    [<Fact>]
    let ``the video lasts as long as the frames do`` () =
        withFfmpeg (fun () ->
            withFfprobe (fun () ->
                inTempDir (fun dir ->
                    let seq = syntheticSequence dir 32 24 24 12.0
                    let output = Path.Combine(dir, "long.mp4")

                    match Video.mux MuxOptions.Default seq output with
                    | MuxRan r when r.Succeeded ->
                        match Video.inspect output with
                        | Inspected info ->
                            Assert.Equal(2.0, float info.Duration, 3)
                            Assert.Equal(float info.FrameCount / info.Fps, float info.Duration, 3)
                        | other -> failwithf "expected Inspected, got %A" other
                    | other -> failwithf "expected a successful MuxRan, got %A" other)))

    /// D76, scoped as the plan scoped it: asserted on the frames, best-effort at the container.
    /// This is the best-effort half, and it does hold on ffmpeg n9 — but see the header: it
    /// holds without the flags too, so it is not the gate that falsifies them.
    [<Fact>]
    let ``two encodes of one sequence are byte-identical`` () =
        withFfmpeg (fun () ->
            inTempDir (fun dir ->
                let seq = syntheticSequence dir 32 24 12 20.0
                let a = Path.Combine(dir, "a.mp4")
                let b = Path.Combine(dir, "b.mp4")

                match Video.mux MuxOptions.Default seq a, Video.mux MuxOptions.Default seq b with
                | MuxRan ra, MuxRan rb when ra.Succeeded && rb.Succeeded ->
                    let bytesA = File.ReadAllBytes a
                    let bytesB = File.ReadAllBytes b

                    if bytesA <> bytesB then
                        let at =
                            Seq.zip bytesA bytesB
                            |> Seq.tryFindIndex (fun (x, y) -> x <> y)
                            |> Option.defaultValue (min bytesA.Length bytesB.Length)

                        // Report the differing bytes rather than only their inequality: D76 says
                        // the milestone does not have to lie about a guarantee it does not
                        // control, and a reader needs to see whether this is a timestamp.
                        failwithf
                            "two encodes differ at byte %d of %d/%d: %A vs %A"
                            at
                            bytesA.Length
                            bytesB.Length
                            (bytesA.[max 0 (at - 4) .. min (bytesA.Length - 1) (at + 12)])
                            (bytesB.[max 0 (at - 4) .. min (bytesB.Length - 1) (at + 12)])
                | other -> failwithf "expected two successful encodes, got %A" other))

    /// **The gate that actually falsifies D76's flags.** Left to itself ffmpeg stamps
    /// `encoder=Lavf63.1.101` on the container and `encoder=Lavc63.1.101 libx264` on the stream;
    /// remove `-fflags +bitexact` and this list is those two entries. Neither varies between two
    /// runs, which is why the byte comparison above does not see them.
    [<Fact>]
    let ``the container names no ffmpeg build`` () =
        withFfmpeg (fun () ->
            withFfprobe (fun () ->
                inTempDir (fun dir ->
                    let seq = syntheticSequence dir 32 24 8 24.0
                    let output = Path.Combine(dir, "bitexact.mp4")

                    match Video.mux MuxOptions.Default seq output with
                    | MuxRan r when r.Succeeded ->
                        match Video.inspect output with
                        | Inspected info ->
                            Assert.True(
                                List.isEmpty info.BuildTags,
                                sprintf "the container names the build that wrote it: %A" info.BuildTags
                            )

                            // A positive control: the tags were read at all, so an empty
                            // `BuildTags` is the flags working rather than the parse failing.
                            Assert.NotEmpty info.StreamTags
                            Assert.NotEmpty info.FormatTags
                        | other -> failwithf "expected Inspected, got %A" other
                    | other -> failwithf "expected a successful MuxRan, got %A" other)))

    /// The stale trap, end to end and in both directions: refused by default, and — with the
    /// refusal turned off — demonstrably the tail D75 warned about.
    [<Fact>]
    let ``a leftover frame becomes a tail, which is why it is refused`` () =
        withFfmpeg (fun () ->
            withFfprobe (fun () ->
                inTempDir (fun dir ->
                    // A long render, then a short one into the same directory.
                    syntheticSequence dir 32 24 20 10.0 |> ignore
                    let short = syntheticSequence dir 32 24 8 10.0

                    let leftovers =
                        Directory.GetFiles(dir, "frame-*.png")
                        |> Array.filter (fun f ->
                            not (short.Files |> Array.exists (fun w -> Path.GetFullPath w = Path.GetFullPath f)))
                        |> Array.map Path.GetFullPath
                        |> Array.sort

                    Assert.Equal(12, leftovers.Length)

                    let short = { short with Stale = leftovers }
                    let output = Path.Combine(dir, "tail.mp4")

                    match Video.mux MuxOptions.Default short output with
                    | MuxRefused _ -> ()
                    | other -> failwithf "expected MuxRefused, got %A" other

                    // And this is what the refusal is protecting against. Twenty frames come
                    // out of an eight-frame render, and ffmpeg exits 0.
                    match Video.mux { MuxOptions.Default with AllowStale = true } short output with
                    | MuxRan r when r.Succeeded ->
                        match Video.inspect output with
                        | Inspected info ->
                            Assert.Equal(20, info.FrameCount)
                            Assert.Equal(8, r.FrameCount)
                        | other -> failwithf "expected Inspected, got %A" other
                    | other -> failwithf "expected a successful MuxRan, got %A" other)))

    /// **The reason `Toolchain.runArgs` exists.** `ProcessStartInfo.Arguments` is one string that
    /// .NET re-splits using Windows' quoting rules on every platform, so an unquoted path with a
    /// space in it becomes two arguments and ffmpeg is asked to write a file nobody named. A
    /// vector is handed over unsplit.
    [<Fact>]
    let ``an output path with a space in it survives`` () =
        withFfmpeg (fun () ->
            inTempDir (fun dir ->
                let seq = syntheticSequence dir 32 24 4 24.0
                let output = Path.Combine(dir, "a shot, rendered.mp4")

                match Video.mux MuxOptions.Default seq output with
                | MuxRan r ->
                    Assert.True(r.Succeeded, sprintf "%s\n%s" r.Command r.Log)
                    Assert.True(File.Exists output, output)
                | other -> failwithf "expected MuxRan, got %A" other))

    /// ffprobe on something that is not a video is a reported failure, not a throw and not a
    /// `VideoInfo` full of zeroes.
    [<Fact>]
    let ``a file that is not a video is reported`` () =
        withFfprobe (fun () ->
            inTempDir (fun dir ->
                let notAVideo = Path.Combine(dir, "notes.txt")
                File.WriteAllText(notAVideo, "there is no video here")

                match Video.inspect notAVideo with
                | InspectFailed _ -> ()
                | other -> failwithf "expected InspectFailed, got %A" other))

// ======================================================================================
// The whole path: a storyboard, rendered through Skia, encoded
// ======================================================================================

module TheWholeThing =

    /// Everything above uses synthetic frames on purpose. This one does not, so that the join
    /// between `Frames.write` and `Video.mux` is gated rather than assumed — including the part
    /// where the video's dimensions come from what the backend actually rasterised.
    [<Fact>]
    let ``a storyboard becomes a video whose frames are the film's`` () =
        SkiaTests.withSkia (fun () ->
            withFfmpeg (fun () ->
                withFfprobe (fun () ->
                    inTempDir (fun dir ->
                        let ts = Array.init 8 (fun i -> float i * 1.0<s>)

                        let points =
                            Chart.scatter U.second U.metre ts (ts |> Array.map (fun t -> float t * 2.0<m>))

                        let story =
                            scene {
                                let! c = draw points |> over 0.4<s>
                                do! c |> zoomToY (0.0, 40.0) |> over 0.6<s>
                            }

                        let film = Film.ofStory 16 (Chart.size 240.0 160.0) story

                        let seq =
                            film
                            |> Frames.write FramePass.Concurrent SkiaBackend.renderPng (FrameTarget.into dir ".png") 20.0

                        Assert.Equal(20, seq.Files.Length)

                        let output = Path.Combine(dir, "story.mp4")

                        match Video.mux MuxOptions.Default seq output with
                        | MuxRan r when r.Succeeded ->
                            match Video.inspect output with
                            | Inspected info ->
                                Assert.Equal(seq.Files.Length, info.FrameCount)
                                Assert.Equal(240, info.Width)
                                Assert.Equal(160, info.Height)
                                Assert.Equal(20.0, info.Fps, 6)
                                Assert.Empty info.BuildTags
                            | other -> failwithf "expected Inspected, got %A" other
                        | other -> failwithf "expected a successful MuxRan, got %A" other))))
