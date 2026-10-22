namespace Marey

open System
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// A RECORDING, AND SAMPLING IT
//
// `sample : Signal<'a> -> Anim<'a>` cannot be total: an `Anim` has a duration and a value at every
// instant of it, and a live widget has neither until it stops. What can be sampled is a
// **recording**: values with the times they arrived, finished.
//
// **The times are the view's.** The browser stamps each event with its own monotonic clock and
// sends the reading with the message; the kernel never reads a clock. So a recording is data, and
// the same gesture recorded once gives one film however late the messages arrived.
//
// **Sample and hold, not interpolation.** A slider's values are what the reader chose. Anything
// between two of them is a value nobody chose, and a film that shows one is not a replay.
// ======================================================================================

/// Values with the times they were reached, in seconds from the first, never decreasing.
type Recording<'a> =
    { Events: (float<s> * 'a)[] }

/// Recordings of a live session: built from its events, sampled at a time, mapped.
[<RequireQualifiedAccess>]
module Recording =

    let private finite (t: float<s>) =
        not (Double.IsNaN(float t) || Double.IsInfinity(float t))

    /// A recording from stamped values, in the order they happened. Times are rebased so the
    /// first is zero. A time that is not finite, or that runs backwards, is read as the time
    /// before it: **the order is the truth**, because it is the order the values were reached
    /// in, and a clock is only how far apart they were.
    let ofEvents (events: seq<float<s> * 'a>) : Recording<'a> =
        let events = Array.ofSeq events
        let start =
            events
            |> Array.tryPick (fun (t, _) -> if finite t then Some t else None)
            |> Option.defaultValue 0.0<s>

        let mutable last = 0.0<s>

        { Events =
            events
            |> Array.map (fun (t, v) ->
                if finite t && t - start > last then
                    last <- t - start

                last, v) }

    /// How long a recording runs: from its first value to its last.
    let duration (r: Recording<'a>) : float<s> =
        if r.Events.Length = 0 then 0.0<s> else fst r.Events.[r.Events.Length - 1]

    let map (f: 'a -> 'b) (r: Recording<'a>) : Recording<'b> =
        { Events = r.Events |> Array.map (fun (t, v) -> t, f v) }

    /// **The recording as an animation**: at each instant, the last value reached at or before it.
    /// Two values at the same instant show the later. The duration ends at the last value, which
    /// is therefore the final frame and is held by `Anim.At` past it.
    ///
    /// A recording with nothing in it has no value to show, and is refused. `Live.record` never
    /// makes one: a session with no messages records its first state.
    let sample (r: Recording<'a>) : Anim<'a> =
        let events = r.Events

        if events.Length = 0 then
            invalidArg "r" "Marey: an empty recording has no value at any time; record at least one."

        // The last index whose time is at or before `t`. Times never decrease, so a binary
        // search for the first time past `t` finds it one to the left.
        let at (t: float<s>) =
            let mutable lo = 0
            let mutable hi = events.Length

            while lo < hi do
                let mid = lo + (hi - lo) / 2

                if fst events.[mid] <= t then lo <- mid + 1 else hi <- mid

            snd events.[max 0 (lo - 1)]

        Anim.ofSeconds (duration r) at
