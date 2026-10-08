namespace Marey

open System
open System.Collections.Concurrent
open System.Text
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// §7 / D77 — FRAMES, AND WHAT MAKES RENDERING THEM IN PARALLEL HONEST
//
// `Anim.fs`'s header makes the claim this file cashes in:
//
//     [| 0 .. n |] |> Array.Parallel.map (fun i -> render (anim.At (dt * float i)))
//
// — "is the whole renderer". It very nearly is. What it leaves out is the two things a
// *shared* renderer needs to agree about, and both of them are text.
//
// **The contract.** An `IBackend` is a state machine over a mutable surface and is therefore
// single-threaded; an `ITextMeasurer` is required to be thread-safe and exactly one of them
// serves a whole render. That asymmetry is not an accident of the implementations — it is the
// only arrangement in which font resolution happens once. Both halves are now written into
// `Backend.fs`, where a backend author reads them rather than where a plan records them.
//
// **The cost of sharing, and the cache that answers it.** One measurer means one lock.
// `SkiaTextMeasurer` takes it per string, and layout asks per tick label, legend entry and
// axis title — "hundreds of times for an ordinary figure", says its own comment. Sixteen
// threads rendering sixteen frames would serialise on that mutex hundreds of times each
// before any of them drew a pixel. So `MemoTextMeasurer` memoises `Measure` on
// `(FontSpec, string)`: after frame 0 the hit path is a lock-free dictionary read.
//
// That works *because of D72*. A shot is laid out once against the union of every sampled
// state, so every frame asks the same questions about the same strings. Without the frozen
// arrangement the cache would still be correct and would mostly miss.
//
// ---------------------------------------------------------------------------------------
// WHAT THIS FILE IS NOT
// ---------------------------------------------------------------------------------------
//
// It does not name a backend, for the reason `Chart.render` does not: rendering enters as a
// function. It does not degrade the scene against a backend's capabilities either — that is
// `Chart.renderTo`'s job and `SkiaBackend.renderPng`'s, and doing it here would do it twice.
//
// And it is not the video encoder. `FrameSequence` is deliberately the product (D75): a
// numbered directory of frames is something a user can inspect, re-encode, and hand to a tool
// this library does not ship. `Marey.Tools.Video` is thin over it.
//
// ---------------------------------------------------------------------------------------
// WHY THIS LIVES IN `Marey.Plot`
// ---------------------------------------------------------------------------------------
//
// It names `Anim<Node>` and nothing above it, so `Marey.Anim` looks like the right home and
// is not: that project's `.fsproj` states that it is Fable-compatible and must stay that way,
// and this file is `System.IO` and `Array.Parallel` from end to end. `Marey.Core` is the
// Fable floor for the same reason and is worse, because it is also where the `ITextMeasurer`
// seam is declared and a decorator over a seam is not the seam.
//
// `Marey.Plot` already references `Marey.Anim` (D69), already writes files (`Chart.toFile`),
// and is where the producer of an `Anim<Node>` lives. The graph does not move.
// ======================================================================================

/// **A memoising decorator over any `ITextMeasurer`** — D77's answer to the contention that
/// sharing one measurer across a parallel render creates.
///
/// It measures nothing itself. Every distinct `(FontSpec, string)` reaches `inner` exactly
/// once in a serial render, and the answer is kept forever after.
///
/// ### What the two counters are for
///
/// `Measurements` is the number of calls that reached `inner`; `Entries` is the number of
/// distinct questions asked. In a **serial** render they are equal, and that equality is the
/// cache's whole promise stated as an assertion: no string is ever measured twice. In a
/// parallel one `Measurements` may exceed `Entries`, because two threads that miss on the same
/// key at the same instant both compute it — the second write wins and the answer is the same
/// either way, so the race costs a duplicate measurement and nothing else. Locking the miss
/// path to remove it would put a lock back on a path taken once per string per render, to save
/// work proportional to the number of threads.
///
/// Neither counter resets. A test takes two readings and subtracts, which is also the only
/// honest way to ask "did *these* frames measure anything".
///
/// ### Lifetime
///
/// This does not own `inner` and does not dispose it. A `SkiaTextMeasurer` behind one of these
/// is still the caller's to `use`.
[<Sealed>]
type MemoTextMeasurer(inner: ITextMeasurer) =

#if FABLE_COMPILER
    // One thread under JavaScript, so nothing to be concurrent about.
    let cache = Collections.Generic.Dictionary<struct (FontSpec * string), TextMetrics>()
#else
    let cache = ConcurrentDictionary<struct (FontSpec * string), TextMetrics>()
#endif
    let mutable measurements = 0

    /// The measurer underneath, as the interface. The decorator forwards nothing but `Measure`,
    /// so it cannot go stale when the thing it wraps grows a member — and a caller who needs a
    /// concrete measurer's own surface, `SkiaTextMeasurer.ResolvedFaces` above all, keeps their
    /// own reference to it rather than downcasting this.
    member _.Inner: ITextMeasurer = inner

    /// Calls that reached the inner measurer.
#if FABLE_COMPILER
    member _.Measurements: int = measurements
#else
    member _.Measurements: int = Threading.Volatile.Read &measurements
#endif

    /// Distinct `(FontSpec, string)` questions answered.
    member _.Entries: int = cache.Count

    interface ITextMeasurer with
        member _.Measure (spec: FontSpec) (text: string) =
            let key = struct (spec, text)

            match cache.TryGetValue key with
            | true, hit -> hit
            | _ ->
#if FABLE_COMPILER
                measurements <- measurements + 1
#else
                Threading.Interlocked.Increment &measurements |> ignore
#endif
                let m = inner.Measure spec text
                cache.[key] <- m
                m

/// How a frame sequence is rendered.
///
/// Both passes produce the same bytes in the same order — `Array.Parallel.map` places results
/// by index — and a gate asserts exactly that. `Serial` exists so the gate can, and for a
/// caller debugging a backend that turns out not to honour the contract in `Backend.fs`.
[<RequireQualifiedAccess>]
type FramePass =
    /// One frame at a time, on the calling thread.
    | Serial
    /// `Array.Parallel.map` over the frame times.
    | Concurrent

/// **An animation and the layout its frames are drawn inside**, which between them decide
/// everything about a frame except when it is.
///
/// The pair is a type rather than two arguments for D72's reason, restated one layer up: the
/// surface a shot was arranged against is already in the `Shot`, and a second `Extent<px>`
/// passed beside it to a renderer is a second chance to disagree with nothing able to notice.
/// Rendering a film laid out for 320 x 220 onto an 800 x 500 surface is not an error anything
/// would catch; it is a picture in the corner of a page.
[<NoComparison; NoEquality>]
type Film =
    { /// The scene at any instant. Total on every `float<s>` — `Anim.At` clamps (D70).
      Frames: Anim<Node>
      /// The frozen layout every frame is drawn inside, and the surface it was arranged
      /// against.
      Shot: Shot }

    /// The surface every frame must be rendered at.
    member this.Extent: Extent<px> = this.Shot.Outer.Extent

    /// How long the film runs.
    member this.Duration: float<s> = this.Frames.Duration

[<RequireQualifiedAccess>]
module Film =

    /// A film from an `Anim<Figure>` — D72's primitive, without the storyboard block.
    ///
    /// `samples` is the union: that many states are arranged and the gutters are the
    /// componentwise maximum. No default, for `Chart.arrangeUnion`'s reason.
    let ofFiguresWith (samples: int) (m: ITextMeasurer) (extent: Extent<px>) (anim: Anim<Figure>) : Film =
        let shot =
            Figure.arrangeUnion samples m extent (fun u -> anim.At(u * anim.Duration))

        { Shot = shot
          Frames = Anim.ofSeconds anim.Duration (fun t -> Figure.toSceneIn m shot (anim.At t)) }

    /// `ofFiguresWith`, measured with the zero-dependency default.
    let ofFigures (samples: int) (extent: Extent<px>) (anim: Anim<Figure>) : Film =
        ofFiguresWith samples TextMeasure.approx extent anim

    /// A film from a storyboard.
    ///
    /// **Pass a `MemoTextMeasurer` here** when the measurer behind it costs anything — this is
    /// the one place a whole render's measuring is decided, and D77 is the reason it is one
    /// place.
    let ofStoryWith (samples: int) (m: ITextMeasurer) (extent: Extent<px>) (story: Story<unit>) : Film =
        let shot, frames = Storyboard.shoot samples m extent story
        { Shot = shot; Frames = frames }

    /// `ofStoryWith`, measured with the zero-dependency default.
    let ofStory (samples: int) (extent: Extent<px>) (story: Story<unit>) : Film =
        ofStoryWith samples TextMeasure.approx extent story

/// Where a frame sequence lands, and under what names.
///
/// The names are the point. `ffmpeg -i frame-%05d.png` is how the frames become a video, and
/// that pattern is a fact about the file names rather than about the encoder — so it is
/// derived here (`FrameTarget.pattern`) and reported on the result, and `Marey.Tools.Video`
/// will read it rather than reinvent it.
[<NoComparison>]
type FrameTarget =
    { /// Created if it does not exist. Existing files are not removed — see
      /// `FrameSequence.Stale`.
      Directory: string
      /// Everything before the frame number. `"frame-"` gives `frame-00000.png`.
      Stem: string
      /// The extension, dot included.
      Extension: string
      /// Digits the frame number is padded to. Five holds 99 999 frames, which is 27 minutes
      /// at 60 fps; a render that needs more says so and gets a message rather than a
      /// directory that sorts wrongly and a video that stops early.
      Digits: int }

[<RequireQualifiedAccess>]
module FrameTarget =

    /// The usual target: `frame-00000<ext>` in `directory`, five digits.
    let into (directory: string) (extension: string) : FrameTarget =
        { Directory = directory
          Stem = "frame-"
          Extension = (if extension.StartsWith "." then extension else "." + extension)
          Digits = 5 }

    /// The `printf`-style pattern the names follow — `frame-%05d.png`. This is an ffmpeg
    /// argument, not a .NET format string.
    let pattern (t: FrameTarget) : string =
        sprintf "%s%%0%dd%s" t.Stem t.Digits t.Extension

    /// The path frame `index` is written to.
    let fileFor (t: FrameTarget) (index: int) : string =
        IO.Path.Combine(t.Directory, t.Stem + (string index).PadLeft(t.Digits, '0') + t.Extension)

/// What a render wrote, and what it found already there.
[<NoComparison>]
type FrameSequence =
    { Directory: string
      /// `frame-%05d.png` — what an encoder is pointed at.
      Pattern: string
      /// Every file written, in frame order.
      Files: string[]
      Fps: float
      Extent: Extent<px>
      /// The film's duration. The sequence *plays* for `Files.Length / Fps`, which is within
      /// one frame period of it and is not the same number — see `Frames.times`.
      Duration: float<s>
      Pass: FramePass
      /// **Files matching this target's pattern that this render did not write.** Left in
      /// place rather than deleted, and reported rather than ignored.
      ///
      /// The hazard is specific and would otherwise be found by watching the video: a
      /// directory holding a previous, longer render still has `frame-00060.png` onwards in
      /// it, `ffmpeg -i frame-%05d.png` reads until the first gap, and the encode silently
      /// gains a tail from a shot that no longer exists. A caller who reuses a directory
      /// should check this is empty; deciding to delete somebody's files is not this
      /// function's call to make.
      Stale: string[] }

/// **The frame writer.** §7's parallel render, with D77's contract around it.
[<RequireQualifiedAccess>]
module Frames =

    /// Frames in a sequence of `duration` at `fps`.
    ///
    /// `round(duration * fps)`, and at least one — a still is one frame, not none.
    let count (fps: float) (duration: float<s>) : int =
        if Double.IsNaN fps || Double.IsInfinity fps || fps <= 0.0 then
            invalidArg "fps" (sprintf "Marey.Frames: fps must be positive and finite, not %g." fps)

        let d = float duration

        if Double.IsNaN d || d < 0.0 then
            invalidArg "duration" (sprintf "Marey.Frames: a film's duration must be zero or more, not %g s." d)

        let n = d * fps

        if n > 1e7 then
            invalidArg
                "fps"
                (sprintf "Marey.Frames: %g s at %g fps is %g frames, which is not a render this library will start." d fps n)

        max 1 (int (Num.roundAway n))

    /// The instants a sequence of `fps` frames samples a film of this duration at.
    ///
    /// **The final instant is not a frame.** Frame `i` is at `i / fps` and there are
    /// `round(duration * fps)` of them, so a 2 s film at 30 fps is 60 frames ending at
    /// 1.9667 s. A frame *occupies* the interval that starts at its own time, so rendering
    /// `t = 2.0` as a 61st frame would hold the final state for an extra frame — visible
    /// immediately in a loop, and an off-by-one in every duration computed from the result.
    let times (fps: float) (duration: float<s>) : float<s>[] =
        let n = count fps duration
        Array.init n (fun i -> LanguagePrimitives.FloatWithMeasure<s>(float i / fps))

    /// **Render every frame**, in order.
    ///
    /// The renderer is called once per frame and must be safe to call concurrently under
    /// `FramePass.Concurrent` — which is the contract in `Backend.fs`, and which both
    /// `SvgBackend.render` and `SkiaBackend.renderPng` meet by constructing their backend
    /// inside the call.
    ///
    /// Holds every frame in memory. For anything longer than a few seconds of raster output,
    /// `write` is the one that streams.
    let render (pass: FramePass) (renderer: Extent<px> -> Node -> 'a) (fps: float) (film: Film) : 'a[] =
        let ts = times fps film.Duration
        let extent = film.Extent
        let frames = film.Frames
        let one (t: float<s>) = renderer extent (frames.At t)

        match pass with
        | FramePass.Serial -> ts |> Array.map one
        | FramePass.Concurrent -> ts |> Array.Parallel.map one

    // D113 — file output is .NET's. A Fable build has no file system to write to, and these do not
    // compile there.
#if !FABLE_COMPILER
    let private writeAll
        (pass: FramePass)
        (renderer: Extent<px> -> Node -> 'a)
        (emit: string -> 'a -> unit)
        (target: FrameTarget)
        (fps: float)
        (film: Film)
        : FrameSequence =
        let ts = times fps film.Duration

        if target.Digits < 1 then
            invalidArg "target" "Marey.Frames: a frame target needs at least one digit of index."

        if ts.Length > pown 10 (min target.Digits 9) then
            invalidArg
                "target"
                (sprintf
                    "Marey.Frames: %d frames do not fit in %d digits, and '%s' would name two of them the same. Widen FrameTarget.Digits."
                    ts.Length
                    target.Digits
                    (FrameTarget.pattern target))

        IO.Directory.CreateDirectory target.Directory |> ignore

        let files = Array.init ts.Length (FrameTarget.fileFor target)

        let one i = emit files.[i] (renderer film.Extent (film.Frames.At ts.[i]))

        match pass with
        | FramePass.Serial ->
            for i in 0 .. ts.Length - 1 do
                one i
        | FramePass.Concurrent -> Array.Parallel.iter one [| 0 .. ts.Length - 1 |]

        // Read back *after* writing, so a file this render produced is never called stale.
        let written = Set.ofArray (files |> Array.map IO.Path.GetFullPath)

        let stale =
            IO.Directory.GetFiles(target.Directory, target.Stem + "*" + target.Extension)
            |> Array.map IO.Path.GetFullPath
            |> Array.filter (written.Contains >> not)
            |> Array.sort

        { Directory = target.Directory
          Pattern = FrameTarget.pattern target
          Files = files
          Fps = fps
          Extent = film.Extent
          Duration = film.Duration
          Pass = pass
          Stale = stale }

    /// **Render every frame and write it**, numbered, into a directory.
    ///
    /// For a renderer that produces bytes — `SkiaBackend.renderPng` has exactly this shape.
    /// The bytes are written as the renderer produced them, with no encoding applied, for
    /// `Chart.toFileBytes`' reason: a PNG that acquired a byte-order mark would not open.
    let write
        (pass: FramePass)
        (renderer: Extent<px> -> Node -> byte[])
        (target: FrameTarget)
        (fps: float)
        (film: Film)
        : FrameSequence =
        writeAll pass renderer (fun path bytes -> IO.File.WriteAllBytes(path, bytes)) target fps film

    /// `write` for a renderer that produces text — `SvgBackend.render` has exactly this shape.
    ///
    /// UTF-8 without a byte-order mark, matching `SvgBackend.renderToFile`. Two functions
    /// rather than one generic in the renderer's result, for `Chart.toFile`'s reason: writing
    /// a `string` and writing a `byte[]` are different calls whatever the renderer returns.
    let writeText
        (pass: FramePass)
        (renderer: Extent<px> -> Node -> string)
        (target: FrameTarget)
        (fps: float)
        (film: Film)
        : FrameSequence =
        writeAll pass renderer (fun path text -> IO.File.WriteAllText(path, text, UTF8Encoding false)) target fps film
#endif
