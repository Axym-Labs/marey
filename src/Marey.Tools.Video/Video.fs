namespace Marey

open System
open System.Globalization
open System.IO
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// D75 / D76 — THE MUX, WHICH IS THE THIN HALF
//
// **The frame sequence is the product; this is a convenience over it.** That is D75's wording
// and it is the whole design. `Frames.write` (in `Marey.Plot`) renders a film in parallel and
// leaves a numbered directory, and a numbered directory is already something: every video
// editor imports one, every encoder reads one, and it is what a paper's supplementary material
// wants anyway. This package turns it into a single file by running one program.
//
// **Why a subprocess and not a binding — licence, and D62 decided the same question already.**
// `FFmpeg.AutoGen` links `libav*` into the process: LGPL at best, GPL the moment the build
// carries x264, which puts a licence audit on every consumer of a plotting library. D62 refused
// to bundle a font on exactly that ground — "package size and a licence every consumer would
// have to audit". A subprocess boundary keeps ffmpeg's licence ffmpeg's problem. And a managed
// wrapper (FFMpegCore, Xabe) buys nothing here, because they shell out too: the cost is a
// dependency and its opinions about temporary files, in exchange for not writing one
// `ProcessStartInfo` — and the hard part would have to be taken back regardless, since D76's
// determinism is a property of the exact argv and a wrapper that builds argv is something to
// fight rather than something to use.
//
// **Missing ffmpeg degrades; it does not fail.** `MuxUnavailable` is an outcome, not an
// exception, and its message says both what to install and that the frames are still on disk.
// This is D62's shape once more, chosen over refusing to render.
//
// **What is testable, and it is more than D75 expected.** D75 said "the mux is testable only
// for exit status and container-level facts". That undersells it: `Video.args` is a pure
// function, so the argv — which is where D76's bit-exactness *lives* — is gated exactly, on a
// machine with no ffmpeg at all. What needs the binary is the determinism gate and
// `Video.inspect`, and `inspect` is what turns "exit status" into container-level facts worth
// asserting: frame count, dimensions, and the frame rate the container declares.
//
// **Two tools, not one.** `ffprobe` ships with `ffmpeg` in every distribution of it, and
// reading back what was written is the only way the encode's own claims are checked rather
// than trusted. It is resolved separately so that a build that somehow has one and not the
// other reports which.
// ======================================================================================

/// `ffmpeg`, resolved and *reported* — D62's rule in its third currency, after a font and a
/// native library.
///
/// `Version` is the first line of `ffmpeg -version`, which carries the build number and is the
/// thing worth naming when a container turns out to differ between two machines.
[<NoComparison>]
type FfmpegReport =
    { Tool: ResolvedTool
      /// The first line of `ffmpeg -version`. `None` when ffmpeg is absent, or present and
      /// unable to say what it is — which is a broken installation and reads differently from
      /// an absent one.
      Version: string option }

    /// Whether a mux would run at all.
    member r.IsAvailable = r.Tool.IsAvailable

/// How a frame sequence is encoded.
///
/// **There is no frame rate here, deliberately.** The sequence already carries the rate it was
/// rendered at, and a second one beside it is a second chance to disagree with nothing able to
/// notice — D72's argument about the extent, in a second place. A slow-motion pass is a
/// different render, not a different flag.
[<NoComparison>]
type MuxOptions =
    { /// The video encoder, as `-c:v`. `None` lets ffmpeg pick from the output extension, which
      /// is what a `.gif` or a `.webm` wants.
      Codec: string option
      /// libx264's constant rate factor. 18 is near-visually-lossless; 23 is ffmpeg's default;
      /// 0 is lossless and very large. `None` omits the flag, for an encoder that has no such
      /// knob.
      Crf: int option
      /// **`yuv420p`, and this default is not cosmetic.** H.264 from a PNG sequence otherwise
      /// encodes as `yuv444p`, which is a legal file that QuickTime, Safari and PowerPoint all
      /// refuse to play — so the author sees it work and the audience sees nothing. The cost is
      /// that chroma subsampling needs even pixel dimensions, which is what `Video.check`
      /// refuses on rather than letting ffmpeg say "width not divisible by 2".
      PixelFormat: string option
      /// Overwrite an existing output file.
      ///
      /// Either `-y` or `-n` is always passed and the value only chooses which. Passing neither
      /// leaves ffmpeg to *ask*, on a stdin this library does not redirect, and a question
      /// nobody can see is a process that hangs until the timeout kills it.
      Overwrite: bool
      /// Encode anyway when the sequence reports stale frames.
      ///
      /// `false` refuses, because the trap is silent: `-i frame-%05d.png` reads until the first
      /// gap, so a directory holding a previous longer render gives a video with a tail from a
      /// shot that no longer exists. `FrameSequence.Stale` is how phase 5 reports it and this is
      /// the consumer it was reported for.
      AllowStale: bool
      /// ffmpeg's `-loglevel`. `warning` keeps `MuxResult.Log` empty on a clean encode and full
      /// of the reason on a failed one; `info` adds the stream mapping, `verbose` adds far more.
      LogLevel: string
      /// Extra arguments, spliced in immediately before the output path — so they are output
      /// options, and an input option (anything that must precede `-i`) cannot be passed here.
      Extra: string list
      /// How long the encode may take before the process is killed. Five minutes: long enough
      /// for a few thousand frames of x264, short enough that a wedged process is a failed test
      /// rather than a hung suite.
      TimeoutMs: int }

[<RequireQualifiedAccess>]
module MuxOptions =

    /// H.264 in the container the output's extension names, near-lossless, playable everywhere.
    let Default =
        { Codec = Some "libx264"
          Crf = Some 18
          PixelFormat = Some "yuv420p"
          Overwrite = true
          AllowStale = false
          LogLevel = "warning"
          Extra = []
          TimeoutMs = 300_000 }

/// An encode that ran to completion. `ExitCode` may still be non-zero — read `Log`.
[<NoComparison>]
type MuxResult =
    { /// The absolute path written, or that ffmpeg was asked to write.
      Output: string
      ExitCode: int
      /// The exact command, as a person would type it. Display only — see
      /// `Toolchain.commandLine`. It is carried because "reproduce it outside the library" is
      /// the first thing anyone does with a failed encode.
      Command: string
      /// Everything ffmpeg said, both streams. Empty on a clean encode at the default log level.
      Log: string
      /// Frames handed to the encoder.
      FrameCount: int
      /// The rate they were rendered at, which is the rate written into the container.
      Fps: float
      /// Pixel dimensions of the first frame, read from its own header. `None` when the frames
      /// are not PNG — see `Video.frameSize`.
      FrameSize: (int * int) option }

    /// ffmpeg exited zero. It is still worth checking the file exists: an encoder that wrote
    /// nothing and said so is a different failure from one that never started.
    member r.Succeeded = r.ExitCode = 0

/// What happened when a frame sequence was asked to become a video.
///
/// Four cases rather than a `Result`, for `ToolOutcome`'s reason: each calls for different
/// behaviour at the call site. Unavailable is *fine* and the frames are still there; refused is
/// the caller's input and is fixable; timed out may have left a partial file; ran is the only
/// one carrying an exit code.
[<NoComparison>]
type MuxOutcome =
    /// ffmpeg is not installed, or is installed and could not be started. Carries what to
    /// install, and says the frames were left where they are.
    | MuxUnavailable of string
    /// Refused before ffmpeg was invoked, for something ffmpeg would have reported worse or not
    /// at all. Carries what is wrong and what to do about it.
    | MuxRefused of string
    /// ffmpeg was still running when the timeout elapsed, and was killed.
    | MuxTimedOut of string
    /// ffmpeg exited.
    | MuxRan of MuxResult

/// What a container says about itself, read back with `ffprobe`.
///
/// This is the "container-level facts" half of D75's gate. It exists because the alternative
/// gate is `ExitCode = 0`, and an encoder that wrote thirteen of twenty-four frames exits zero —
/// which is not hypothetical, it is what one misplaced `-r` does. See `Video.args`.
[<NoComparison>]
type VideoInfo =
    { Codec: string
      Width: int
      Height: int
      /// Frames actually decoded, not the count in a header — `ffprobe -count_frames`.
      FrameCount: int
      /// `avg_frame_rate`, evaluated from the rational ffprobe reports it as.
      Fps: float
      /// The container's duration. Zero when it declares none.
      ///
      /// This is `FrameCount / Fps` and is **not** the film's duration: a frame occupies the
      /// interval that starts at its own time, so the last frame's interval is inside the video
      /// and the last *instant* of the film is not a frame at all. See `Frames.times`.
      Duration: float<s>
      /// Container-level tags.
      FormatTags: Map<string, string>
      /// Tags on the video stream.
      StreamTags: Map<string, string> }

    /// **Tags naming the ffmpeg build that wrote this file**, which is what D76 is about.
    ///
    /// Left to itself, ffmpeg stamps `encoder=Lavf63.1.101` on the container and
    /// `encoder=Lavc63.1.101 libx264` on the stream. Neither varies between two runs, so a
    /// determinism gate that muxes twice and compares bytes passes without ever noticing them —
    /// they vary between *builds*, which is D63's `/CreationDate` in a slower-moving currency.
    /// `-fflags +bitexact` removes the first and strips the version from the second, and this is
    /// empty when it worked.
    member i.BuildTags: (string * string) list =
        // `Lav` + a library letter + a digit. `Lavc libx264` — what bitexact leaves behind — has
        // the letter and no digit, and is not a build.
        let namesABuild (value: string) =
            let rec scan (from: int) =
                let j = value.IndexOf("Lav", from, StringComparison.Ordinal)

                if j < 0 then false
                elif j + 4 < value.Length && Char.IsLetter value.[j + 3] && Char.IsDigit value.[j + 4] then true
                else scan (j + 3)

            scan 0

        [ yield! Map.toList i.FormatTags
          yield! Map.toList i.StreamTags ]
        |> List.filter (snd >> namesABuild)

/// What happened when a file was asked what it is.
[<NoComparison>]
type InspectOutcome =
    /// `ffprobe` is not installed. Carries what to install.
    | InspectUnavailable of string
    /// It ran and could not answer — an unreadable file, or no video stream in it.
    | InspectFailed of string
    | Inspected of VideoInfo

/// **The mux.** `ffmpeg`, resolved and reported, over a `FrameSequence`.
[<RequireQualifiedAccess>]
module Video =

    [<Literal>]
    let private install =
        "ffmpeg — `apt install ffmpeg`, `brew install ffmpeg`, `winget install ffmpeg`, or a build from https://ffmpeg.org/download.html"

    let private ffmpegTool = lazy (Toolchain.resolve install "ffmpeg")
    let private ffprobeTool = lazy (Toolchain.resolve install "ffprobe")

    /// The first line of a tool's output, trimmed, or `None` if it said nothing.
    let private firstLine (text: string) : string option =
        text.Split('\n')
        |> Array.tryHead
        |> Option.map (fun l -> l.Trim())
        |> Option.filter (fun l -> l.Length > 0)

    let private probed =
        lazy
            (let tool = ffmpegTool.Force()

             let version =
                 // The temp directory rather than the current one: a probe should not depend on
                 // where the process happens to be standing, and on a working directory that has
                 // since been deleted `Process.Start` throws rather than reporting.
                 match Toolchain.runArgs tool [ "-version" ] (Path.GetTempPath()) 20_000 with
                 | ToolRan r when r.ExitCode = 0 -> firstLine r.Out
                 | _ -> None

             { Tool = tool; Version = version })

    /// `ffmpeg`, resolved once per process and asked what it is.
    ///
    /// **Probed at first use, not at every call.** The `PATH` walk and the version process both
    /// happen once; every consumer in this repository would otherwise do them per frame
    /// sequence, and a suite of gates per test.
    let ffmpeg () : FfmpegReport = probed.Force()

    /// `ffprobe`, resolved once per process.
    ///
    /// Separate from `ffmpeg` rather than assumed alongside it: they are two files, a container
    /// image can carry one without the other, and "which one is missing" is the useful half of
    /// that message.
    let ffprobe () : ResolvedTool = ffprobeTool.Force()

    /// One line saying what was found, whether or not anything was.
    ///
    /// Printed by the gates and by `examples/video.fsx`, for `Toolchain.report`'s reason: a gate
    /// that skipped and a gate that passed look identical from outside, and this is the inside.
    let report () : string =
        let r = ffmpeg ()

        match r.Tool.Path, r.Version with
        | Some p, Some v -> sprintf "ffmpeg found at %s — %s" p v
        | Some p, None -> sprintf "ffmpeg found at %s, but it could not say what version it is — the installation is broken" p
        | None, _ ->
            Toolchain.missing "nothing is encoded, and a render still leaves its numbered frames behind" r.Tool

    /// The pixel dimensions of a frame, read from the file's own header.
    ///
    /// **Read rather than derived, and that is the point.** `FrameSequence.Extent` is the extent
    /// the film's layout was arranged against; the pixels a backend actually produced are a
    /// separate fact — `SkiaBackend` rounds *up*, so a 400.5 px extent is a 401 px bitmap, and
    /// 401 is the number that decides whether a `yuv420p` encode is legal.
    ///
    /// PNG only: 8 signature bytes, then an `IHDR` chunk whose width and height are at fixed
    /// offsets 16 and 20, big-endian, guaranteed to be the first chunk by the format. `None` for
    /// anything else, including a file that is not there.
    let frameSize (path: string) : (int * int) option =
        try
            use fs = new FileStream(path, FileMode.Open, FileAccess.Read)
            let head = Array.zeroCreate<byte> 24
            let mutable got = 0
            let mutable eof = false

            while got < 24 && not eof do
                let r = fs.Read(head, got, 24 - got)

                if r <= 0 then eof <- true else got <- got + r

            let signature = [| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy; 26uy; 10uy |]

            if got < 24 || head.[0..7] <> signature || head.[12..15] <> "IHDR"B then
                None
            else
                let be (o: int) =
                    (int head.[o] <<< 24)
                    ||| (int head.[o + 1] <<< 16)
                    ||| (int head.[o + 2] <<< 8)
                    ||| int head.[o + 3]

                Some(be 16, be 20)
        with _ ->
            // A frame that cannot be read is not this function's failure to report: the encode
            // is about to fail on the same file and will say so in ffmpeg's own words.
            None

    /// Which dimensions a pixel format needs to be even in, as `(width, height)`.
    ///
    /// The two subsampled families anybody passes. Anything else is unconstrained here and left
    /// to ffmpeg, which is the right place for a format this library has no opinion about.
    let private chromaEven (format: string) : bool * bool =
        if format.Contains "420" || format.Contains "nv12" || format.Contains "nv21" then
            true, true
        elif format.Contains "422" || format.Contains "nv16" then
            true, false
        else
            false, false

    /// Why this mux would be refused, or `None`.
    ///
    /// **No process is started**, so a caller can ask before rendering an hour of frames. It is
    /// not quite pure — the parity check reads 24 bytes of the first frame — and everything it
    /// answers is something ffmpeg would either report worse, three frames into an encode and in
    /// a message about chroma planes, or not report at all, which is the stale case.
    let check (options: MuxOptions) (sequence: FrameSequence) : string option =
        if sequence.Files.Length = 0 then
            Some "Marey.Video: the frame sequence is empty, and there is nothing to encode."
        elif not options.AllowStale && sequence.Stale.Length > 0 then
            Some(
                sprintf
                    "Marey.Video: %d file(s) in %s match '%s' but were not written by this render, starting with %s. `ffmpeg -i %s` reads until the first gap, so the video would gain a tail from an older, longer shot. Delete them, render into a fresh directory, or set MuxOptions.AllowStale."
                    sequence.Stale.Length
                    sequence.Directory
                    sequence.Pattern
                    (Path.GetFileName sequence.Stale.[0])
                    sequence.Pattern
            )
        else
            match options.PixelFormat |> Option.map chromaEven, frameSize sequence.Files.[0] with
            | Some(evenW, evenH), Some(w, h) when (evenW && w % 2 <> 0) || (evenH && h % 2 <> 0) ->
                Some(
                    sprintf
                        "Marey.Video: the frames are %d x %d px and %s subsamples chroma, which needs even dimensions. Render at an extent whose ceiling is even, or set MuxOptions.PixelFormat to yuv444p and accept that QuickTime and PowerPoint will not play the result."
                        w
                        h
                        (defaultArg options.PixelFormat "")
                )
            | _ -> None

    /// A frame rate as ffmpeg parses it.
    ///
    /// Invariant, and to twelve decimals rather than `%g`'s six significant digits: 30000/1001
    /// is 29.97002997, and `29.97` is a different rational. ffmpeg converts the decimal back to
    /// a rational itself, and giving it more digits is what makes that conversion land on the
    /// one meant.
    let private rate (fps: float) : string =
        fps.ToString("0.############", CultureInfo.InvariantCulture)

    /// **The exact command `mux` runs.** Pure, so this is where D76 is gated — and it can be
    /// gated on a machine with no ffmpeg installed at all.
    ///
    /// Every position in here was measured rather than copied, on ffmpeg n9.0.1 with libx264
    /// over a 24-frame PNG sequence:
    ///
    /// - **`-framerate` must precede `-i`.** It is an *input* option: it says what rate the
    ///   images are, and every one of them is kept. The same number as `-r` after `-i` is an
    ///   *output* option, which reads the images at the demuxer's default 25 fps and then
    ///   resamples to the asked-for rate — dropping frames or duplicating them. Measured: 24
    ///   frames at 12 fps is 24 frames and 2.000 s written correctly, and **13 frames and
    ///   1.083 s** with the flag moved; at 60 fps it is 24 frames and 0.400 s against **58
    ///   frames** and 0.967 s. The trap is that at 24 fps, next to the demuxer's default of 25,
    ///   both spellings give 24 frames — so it looks right at the rate somebody first tries and
    ///   is wrong at every other one.
    /// - **`-start_number 0`**, because this library numbers from zero. Measured as *not*
    ///   required by ffmpeg n9, which probes for the first index it can find; it is two argv
    ///   entries against a documented default that has moved before, and it makes the numbering
    ///   a stated fact rather than a recovered one.
    /// - **`-fflags +bitexact` must follow `-i`** to reach the muxer rather than the demuxer.
    ///   This is the flag that does D76's work: without it the file carries
    ///   `encoder=Lavf63.1.101` and `encoder=Lavc63.1.101 libx264`, and with it the first is
    ///   gone and the second is `Lavc libx264`. See `VideoInfo.BuildTags`.
    /// - **`-flags +bitexact`** changed nothing for libx264 beyond what `-fflags` had already
    ///   done, and changed the bytes for `-c:v mpeg4` — so it is not a no-op in general, and it
    ///   stays as the codec-level half of a claim that should not depend on which encoder the
    ///   caller picked.
    /// - **`-map_metadata -1`** has nothing to drop from a PNG sequence and is carried for the
    ///   same reason: the claim is about the output, and an input that does carry metadata is a
    ///   `MuxOptions.Extra` away.
    ///
    /// The output path is last, which is ffmpeg's grammar rather than a choice.
    let args (options: MuxOptions) (sequence: FrameSequence) (output: string) : string list =
        [ "-hide_banner"
          // ffmpeg reads stdin for interactive keys, and this library does not redirect it — so
          // without this the child competes with the parent's console for input, which under a
          // test runner is a process that consumes the runner's stdin and stops.
          "-nostdin"
          "-loglevel"
          options.LogLevel
          if options.Overwrite then "-y" else "-n"

          "-framerate"
          rate sequence.Fps
          "-start_number"
          "0"
          "-i"
          sequence.Pattern

          match options.Codec with
          | Some codec ->
              "-c:v"
              codec
          | None -> ()

          match options.Crf with
          | Some crf ->
              "-crf"
              string crf
          | None -> ()

          match options.PixelFormat with
          | Some fmt ->
              "-pix_fmt"
              fmt
          | None -> ()

          "-fflags"
          "+bitexact"
          "-flags"
          "+bitexact"
          "-map_metadata"
          "-1"

          yield! options.Extra
          output ]

    /// `mux`, against a **stated** ffmpeg rather than the one on `PATH`.
    ///
    /// D62's shape once more — resolved and reported, or stated and obeyed. A caller pinning a
    /// specific build (a bundled ffmpeg, or one whose licence they have audited) passes it here,
    /// and so does the gate that has to see what happens when there is none.
    let muxWith (found: FfmpegReport) (options: MuxOptions) (sequence: FrameSequence) (output: string) : MuxOutcome =
        match check options sequence with
        | Some why -> MuxRefused why
        | None ->
            match found.Tool.Path with
            | None ->
                MuxUnavailable(
                    Toolchain.missing
                        (sprintf
                            "the %d frames in %s were not encoded — they are still there, numbered %s, and any editor or encoder reads them"
                            sequence.Files.Length
                            sequence.Directory
                            sequence.Pattern)
                        found.Tool
                )
            | Some _ ->
                let full = Path.GetFullPath output
                let dir = Path.GetDirectoryName full

                if not (String.IsNullOrEmpty dir) then
                    Directory.CreateDirectory dir |> ignore

                let argv = args options sequence full
                let command = Toolchain.commandLine found.Tool argv

                match Toolchain.runArgs found.Tool argv sequence.Directory options.TimeoutMs with
                | ToolUnavailable why -> MuxUnavailable why
                | ToolTimedOut why -> MuxTimedOut why
                | ToolRan run ->
                    MuxRan
                        { Output = full
                          ExitCode = run.ExitCode
                          Command = command
                          Log = (run.Out + run.Error).Trim()
                          FrameCount = sequence.Files.Length
                          Fps = sequence.Fps
                          FrameSize = frameSize sequence.Files.[0] }

    /// **Encode a frame sequence.**
    ///
    /// ffmpeg runs with the sequence's directory as its working directory, so the input is the
    /// bare `frame-%05d.png` pattern and no path is interpolated into a format string. `output`
    /// is resolved against the *caller's* current directory before that happens — otherwise a
    /// relative output would land among the frames — and its directory is created if it does not
    /// exist, matching `Frames.write`.
    ///
    /// Returns rather than throws in every case, including the one where ffmpeg is not
    /// installed: the frames are on disk, numbered, and a caller who wanted a video still has
    /// something every editor and every encoder can read.
    let mux (options: MuxOptions) (sequence: FrameSequence) (output: string) : MuxOutcome =
        muxWith (ffmpeg ()) options sequence output

    let private number (text: string) : float option =
        match Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, v -> Some v
        | _ -> None

    /// `num/den`, which is how ffprobe reports a frame rate. `0/0` means it does not know.
    let private ratio (text: string) : float option =
        match text.Split '/' with
        | [| n; d |] ->
            match number n, number d with
            | Some n, Some d when d <> 0.0 -> Some(n / d)
            | _ -> None
        | _ -> number text

    /// ffprobe's default output, parsed into stream fields and the two tag sets.
    ///
    /// **The section wrappers are kept on purpose.** `noprint_wrappers=1` gives a flat list in
    /// which the container's `encoder` tag and the stream's `encoder` tag are two lines with the
    /// same name and no way to tell them apart — and telling them apart is the whole of
    /// `VideoInfo.BuildTags`. Fields are read by name rather than by position because ffprobe
    /// emits them in the stream's order, not the order they were asked for.
    let private parse (text: string) : Map<string, string> * Map<string, string> * Map<string, string> =
        let mutable section = ""
        let stream = Collections.Generic.Dictionary<string, string>()
        let streamTags = Collections.Generic.Dictionary<string, string>()
        let formatTags = Collections.Generic.Dictionary<string, string>()

        for raw in text.Split('\n') do
            let line = raw.Trim()

            if line.StartsWith("[/", StringComparison.Ordinal) then
                section <- ""
            elif line.StartsWith("[", StringComparison.Ordinal) then
                section <- line.Trim('[', ']').ToLowerInvariant()
            else
                match line.Split([| '=' |], 2) with
                | [| key; value |] when key.Length > 0 ->
                    let value = value.Trim()

                    if key.StartsWith("TAG:", StringComparison.Ordinal) then
                        let key = key.Substring 4

                        match section with
                        | "stream" -> streamTags.[key] <- value
                        | "format" -> formatTags.[key] <- value
                        | _ -> ()
                    elif section = "stream" then
                        stream.[key] <- value
                | _ -> ()

        let toMap (d: Collections.Generic.Dictionary<string, string>) =
            d |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

        toMap stream, toMap streamTags, toMap formatTags

    /// `inspect`, against a stated `ffprobe`. `muxWith`'s reason, on the reading side.
    let inspectWith (tool: ResolvedTool) (path: string) : InspectOutcome =
        match tool.Path with
        | None -> InspectUnavailable(Toolchain.missing "the video was not read back" tool)
        | Some _ ->
            let full = Path.GetFullPath path

            let argv =
                [ "-v"
                  "error"
                  "-select_streams"
                  "v:0"
                  "-count_frames"
                  "-show_entries"
                  "stream=codec_name,width,height,nb_read_frames,avg_frame_rate,duration:stream_tags:format_tags"
                  "-of"
                  "default"
                  full ]

            match Toolchain.runArgs tool argv (Path.GetTempPath()) 300_000 with
            | ToolUnavailable why -> InspectUnavailable why
            | ToolTimedOut why -> InspectFailed why
            | ToolRan run when run.ExitCode <> 0 ->
                InspectFailed(sprintf "ffprobe exited %d on %s: %s" run.ExitCode full (run.Error.Trim()))
            | ToolRan run ->
                let stream, streamTags, formatTags = parse run.Out
                let get k = Map.tryFind k stream

                match get "width" |> Option.bind number, get "height" |> Option.bind number with
                | Some w, Some h ->
                    Inspected
                        { Codec = defaultArg (get "codec_name") "unknown"
                          Width = int w
                          Height = int h
                          FrameCount =
                            get "nb_read_frames" |> Option.bind number |> Option.map int |> Option.defaultValue 0
                          Fps = get "avg_frame_rate" |> Option.bind ratio |> Option.defaultValue 0.0
                          Duration =
                            get "duration"
                            |> Option.bind number
                            |> Option.defaultValue 0.0
                            |> LanguagePrimitives.FloatWithMeasure<s>
                          FormatTags = formatTags
                          StreamTags = streamTags }
                | _ ->
                    // Exit zero and no width: the file opened and holds no video stream. A muxer
                    // that wrote a container with nothing in it exits zero too, which is the whole
                    // reason this function exists.
                    InspectFailed(sprintf "%s has no video stream ffprobe could describe." full)

    /// **Read a video back and say what it is**, with `ffprobe`.
    ///
    /// `-count_frames` decodes the file rather than trusting a header, which is the only way the
    /// frame count is a fact about the video instead of a fact about what the muxer claimed. It
    /// costs a decode, which for the lengths this library produces is a fraction of the encode.
    let inspect (path: string) : InspectOutcome = inspectWith (ffprobe ()) path
