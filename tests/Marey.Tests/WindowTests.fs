/// M5 phase 6 — the window, gate 12: a scripted sequence of window events gives the
/// `LiveState` and the `Recording` the equivalent messages give through `Live.step`.
///
/// The translation is pure (`WindowSession`) and is checked without a display. The host is then
/// driven with the same events through `GpuWindow.Post`, hidden, where an OpenGL context can be
/// made, and has to reach the same state, which is what shows it calls nothing else.
module Marey.Tests.WindowTests

open System
open Xunit
open Marey
open Marey.UnitsOfMeasure

let private wave (f: float<1 / s>) (damped: bool) =
    let t = Array.init 50 (fun i -> float i * 0.02<s>)
    let decay = if damped then 1.0<1 / s> else 0.0<1 / s>
    let y = t |> Array.map (fun t -> exp (-decay * t) * sin (2.0 * Math.PI * f * t) * 1.0<m>)
    Chart.line U.second U.metre t y

let private live () =
    interact {
        let! f = Control.slider U.hertz "f" 0.5<1 / s> 5.0<1 / s> 1.0<1 / s>
        and! damped = Control.checkbox "damped" false
        return wave f damped
    }

let private px (x: float) (y: float) : Vec2<px> = { X = x * 1.0<px>; Y = y * 1.0<px> }

/// A session a person could have had, as window events, and the messages the notebook page would
/// have sent for the same gestures, written out by hand.
let private script (l: Live) =
    let strip = float (WindowStrip.height l.Controls)
    let size: Extent<px> = { Width = 800.0<px>; Height = 500.0<px> + WindowStrip.height l.Controls }
    let rows = WindowStrip.rows l.Controls size.Width
    let track = rows.[0].Widget
    let along (t: float) = track.X0 + t * (track.X1 - track.X0)
    let mid = (track.Y0 + track.Y1) / 2.0
    let box = rows.[1].Widget
    let boxMid = px (float (box.X0 + box.X1) / 2.0) (float (box.Y0 + box.Y1) / 2.0)

    // The first panel's plot area once the window has said its size: where a drag means something.
    let state = Live.step l (Live.init l) (Resize(800.0, 500.0))
    let v = (Live.panels l state).[0]
    let inPlot (fx: float) (fy: float) = px (float (v.Plot.X0 + fx * (v.Plot.X1 - v.Plot.X0))) (float (v.Plot.Y0 + fy * (v.Plot.Y1 - v.Plot.Y0)))
    let onWindow (p: Vec2<px>) = { p with Y = p.Y + strip * 1.0<px> }
    let a, b = inPlot 0.25 0.2, inPlot 0.7 0.8
    let c = inPlot 0.5 0.5
    let value (t: float) = 0.5 + t * 4.5

    [ WindowEvent.Resized size, [ Resize(800.0, 500.0) ]
      // The slider: pressed at three quarters, dragged to a half, let go.
      WindowEvent.Pressed({ X = along 0.75; Y = mid }, false), [ Set(0, value 0.75) ]
      WindowEvent.Moved { X = along 0.5; Y = mid }, [ Set(0, value 0.5) ]
      WindowEvent.Released { X = along 0.5; Y = mid }, []
      // The checkbox, clicked.
      WindowEvent.Pressed(boxMid, false), [ Set(1, 1.0) ]
      WindowEvent.Released boxMid, []
      // Over the figure, then a drag that zooms.
      WindowEvent.Moved(onWindow a), [ Hover a ]
      WindowEvent.Pressed(onWindow a, false), []
      WindowEvent.Moved(onWindow b), [ Hover b ]
      WindowEvent.Released(onWindow b), [ Zoom { X0 = a.X; Y0 = a.Y; X1 = b.X; Y1 = b.Y } ]
      // A shifted drag that pans, in two moves.
      WindowEvent.Pressed(onWindow c, true), []
      WindowEvent.Moved(onWindow (c + px -8.0 3.0)), [ Pan(c, px -8.0 3.0) ]
      WindowEvent.Moved(onWindow (c + px -12.5 4.0)), [ Pan(c, px -4.5 1.0) ]
      WindowEvent.Released(onWindow (c + px -12.5 4.0)), []
      // A double click: two clicks that zoom nothing, then the reset.
      WindowEvent.Pressed(onWindow c, false), []
      WindowEvent.Released(onWindow c), [ Zoom { X0 = c.X; Y0 = c.Y; X1 = c.X; Y1 = c.Y } ]
      WindowEvent.Pressed(onWindow c, false), []
      WindowEvent.Released(onWindow c), [ Zoom { X0 = c.X; Y0 = c.Y; X1 = c.X; Y1 = c.Y }; Reset ]
      // The keyboard: Tab to the first control, one step up, Tab to the checkbox, space.
      WindowEvent.Key(WindowKey.Tab, false), []
      WindowEvent.Key(WindowKey.Right, false), [ Set(0, value 0.5 + 0.01) ]
      WindowEvent.Key(WindowKey.Tab, false), []
      WindowEvent.Key(WindowKey.Space, false), [ Set(1, 0.0) ]
      // And away.
      WindowEvent.Left, [ Leave ] ]

/// The slider's step, which `Control.slider` chooses.
let private stepOf (l: Live) =
    match l.Controls.[0].Kind with
    | Slider(_, _, step) -> step
    | _ -> failwith "not a slider"

/// The script with the slider's real step in place of the hundredth it assumes.
let private scripted (l: Live) =
    let step = stepOf l

    script l
    |> List.map (fun (e, msgs) ->
        e,
        msgs
        |> List.map (fun m ->
            match e, m with
            | WindowEvent.Key(WindowKey.Right, _), Set(0, v) -> Set(0, v - 0.01 + step)
            | _ -> m))

module ``gate 12`` =

    /// Each event sends exactly the page's messages, and the state after them is `Live.step`'s.
    [<Fact>]
    let ``window events send the widget's messages and reach its state`` () =
        let l = live ()
        let events = scripted l

        let session, sent =
            events
            |> List.mapi (fun i (e, _) -> float i * 0.1<s>, e)
            |> List.fold
                (fun (session, sent) (t, e) ->
                    let next, msgs = WindowSession.feed l session (t, e)
                    next, sent @ [ msgs ])
                (WindowSession.start l, [])

        List.iteri2
            (fun i (e, expected: LiveMsg list) (got: LiveMsg list) ->
                Assert.True((expected = got), sprintf "event %d (%A) sent %A, not %A" i e got expected))
            events
            sent

        let expected = events |> List.collect snd |> List.fold (Live.step l) (Live.init l)
        Assert.True((session.State = expected), "the window's state is not the one the messages give")
        // The session moved: a zoom was reset, the slider and the checkbox are where they were left.
        Assert.Empty(session.State.Zoom)
        Assert.Equal(2.75 + stepOf l, session.State.Values.[0], 9)
        Assert.Equal(0.0, session.State.Values.[1])
        Assert.Equal(None, session.State.Pointer)

    /// A recorder noting what the window sent holds the log the page would have sent, and that log
    /// replays to the window's last state.
    [<Fact>]
    let ``a session recorded in the window replays as a notebook's`` () =
        let l = live ()
        let r = Live.recorder l
        let from = r.Begin()
        let events = scripted l
        let mutable session = WindowSession.start l

        events
        |> List.iteri (fun i (e, _) ->
            let t = float i * 0.1<s>
            let next, msgs = WindowSession.feed l session (t, e)
            msgs |> List.iter (fun m -> r.Note(from, Some t, m))
            session <- next)

        let log = Recorder.stop r

        let expected =
            events
            |> List.mapi (fun i (_, msgs) -> msgs |> List.map (fun m -> float i * 0.1<s>, m))
            |> List.concat
            |> List.filter (fun (_, m) ->
                match m with
                | Hover _
                | Leave -> false
                | _ -> true)
            |> Array.ofList
            |> Recording.ofEvents

        Assert.Equal<(float<s> * LiveMsg)[]>(expected.Events, log.Events)

        let replayed = Live.record l log
        let last = snd replayed.Events.[replayed.Events.Length - 1]
        Assert.True((last = session.State), "the replayed log ends somewhere the window did not")

    /// A pointer that leaves the window mid-drag does not end the drag: the release still zooms.
    [<Fact>]
    let ``leaving mid-drag keeps the drag`` () =
        let l = live ()
        let s0, _ = WindowSession.feed l (WindowSession.start l) (0.0<s>, WindowEvent.Resized { Width = 800.0<px>; Height = 564.0<px> })
        let o = WindowSession.origin l
        let s1, _ = WindowSession.feed l s0 (0.1<s>, WindowEvent.Pressed(px 300.0 300.0, false))
        let s2, left = WindowSession.feed l s1 (0.2<s>, WindowEvent.Left)
        let _, up = WindowSession.feed l s2 (0.3<s>, WindowEvent.Released(px 500.0 400.0))
        Assert.Empty(left)

        Assert.Equal<LiveMsg list>(
            [ Zoom { X0 = 300.0<px> - o.X; Y0 = 300.0<px> - o.Y; X1 = 500.0<px> - o.X; Y1 = 400.0<px> - o.Y } ],
            up
        )

    /// The keyboard does nothing to a figure with no controls, and a select steps through its
    /// options and stops at the ends.
    [<Fact>]
    let ``keys move the focused control within its range`` () =
        let plain = interact { return wave 1.0<1 / s> false }
        let _, none = WindowSession.feed plain (WindowSession.start plain) (0.0<s>, WindowEvent.Key(WindowKey.Right, false))
        Assert.Empty(none)

        let chooser =
            interact {
                let! k = Control.select "kind" [ "a", 1.0<1 / s>; "b", 2.0<1 / s>; "c", 3.0<1 / s> ] 0
                return wave k false
            }

        let keys =
            [ WindowKey.Tab; WindowKey.Right; WindowKey.Right; WindowKey.Right; WindowKey.Home; WindowKey.End ]

        let session =
            keys
            |> List.fold (fun s k -> fst (WindowSession.feed chooser s (0.0<s>, WindowEvent.Key(k, false)))) (WindowSession.start chooser)

        Assert.Equal(2.0, session.State.Values.[0])

    /// The window draws `Live.readout` of the state its events reach, so a pointer moved to the
    /// bottom of a wide log axis reads there as the page does: in powers of ten, not as fifteen
    /// zeros.
    [<Fact>]
    let ``the window reads a wide log axis in powers of ten`` () =
        let t = Array.init 27 (fun i -> float i * 1.0<s>)
        let e = Array.init 27 (fun i -> 10.0 ** float (i - 18) * 1.0<J>)
        let l = interact { return Chart.line U.second U.joule t e |> Chart.withYLog }
        let strip = WindowStrip.height l.Controls
        let size: Extent<px> = { Width = 800.0<px>; Height = 500.0<px> + strip }
        let sized, _ = WindowSession.feed l (WindowSession.start l) (0.0<s>, WindowEvent.Resized size)
        let v = (Live.panels l sized.State).[0]
        let bottom = { X = (v.Plot.X0 + v.Plot.X1) / 2.0; Y = v.Plot.Y1 }
        let over, sent = WindowSession.feed l sized (0.1<s>, WindowEvent.Moved { bottom with Y = bottom.Y + strip })
        Assert.True((sent = [ Hover bottom ]), sprintf "sent %A" sent)
        Assert.Matches(@"^E = (\d(\.\d+)?×)?10⁻¹⁸ J$", (Live.readout l over.State).Value.Lines.[1])

// ======================================================================================
// The host, driven through `Post`
// ======================================================================================

module ``the host`` =

    /// The window reaches the state the pure session does, from the same events, and draws the
    /// figure it holds: the figure's part of the window is the GPU renderer's picture of the same
    /// scene.
    [<Fact>]
    let ``a hidden window steps the scripted events as the session does`` () =
        GpuTests.withGpu (fun gpu ->
            let l = live ()
            let events = scripted l |> List.tail

            use w = GpuWindow.Open(l, visible = false)

            // The window says its own size first, as a page does when it mounts. Compared after
            // every event, because a later reset would hide a pan that went wrong.
            let mutable expected = Live.step l (Live.init l) (Resize(800.0, 500.0))

            for i, (e, msgs) in List.indexed events do
                w.Post e
                expected <- msgs |> List.fold (Live.step l) expected
                Assert.True((w.State = expected), sprintf "after event %d (%A) the window's state is not the messages'" i e)

            let got = w.State

            let frame = w.Capture()
            let strip = int (WindowStrip.height l.Controls)
            Assert.Equal(800, frame.Width)
            Assert.Equal(500 + strip, frame.Height)

            let figure = gpu.RenderImage got.Shot.Outer.Extent (Live.scene l got)
            let fw = figure.Width
            let mutable differing = 0

            for y in 0 .. figure.Height - 1 do
                for x in 0 .. fw - 1 do
                    for c in 0 .. 3 do
                        let a = figure.Rgba.[(y * fw + x) * 4 + c]
                        let b = frame.Rgba.[((y + strip) * frame.Width + x) * 4 + c]

                        if abs (int a - int b) > 2 then
                            differing <- differing + 1

            Assert.True((differing = 0), sprintf "%d channels of the figure differ from the renderer's" differing))

    /// A hidden window plays a time control on its own loop: Tab and space, and the value
    /// moves on by the window's clock without another event, then holds once paused.
    [<Fact>]
    let ``a hidden window plays a time control on its frame loop`` () =
        GpuTests.withGpu (fun _ ->
            let l = interact {
                        let! t = Control.time "t" 0.0<s> 100.0<s>
                        return wave (1.0<1 / s> + t * 0.01<1 / s^2>) false
                    }

            use w = GpuWindow.Open(l, visible = false)
            w.Post(WindowEvent.Key(WindowKey.Tab, false))
            w.Post(WindowEvent.Key(WindowKey.Space, false))
            Threading.Thread.Sleep 400
            let playing = w.Session
            Assert.True(playing.Gesture.Playing.IsSome, "the window is not playing")
            Assert.True(playing.State.Values.[0] > 0.1, sprintf "after 0.4 s of play the time is %g s" playing.State.Values.[0])
            w.Post(WindowEvent.Key(WindowKey.Space, false))
            let paused = w.State.Values.[0]
            Threading.Thread.Sleep 200
            Assert.Equal(paused, w.State.Values.[0]))

    /// Closing returns from `Wait`, and a closed window refuses to be asked anything.
    [<Fact>]
    let ``close ends the wait`` () =
        GpuTests.withGpu (fun _ ->
            let w = GpuWindow.Open(GoldenScenes.figure, visible = false)
            Assert.True(w.IsOpen)
            w.Close()
            w.Wait()
            Assert.False(w.IsOpen)
            Assert.Throws<InvalidOperationException>(fun () -> w.Capture() |> ignore) |> ignore)

    /// `Chart.show`, `Live.show` and `Recorder.show` resolve beside the modules they extend, under
    /// `open Marey` alone, and the core's functions of those modules still resolve beside them.
    [<Fact>]
    let ``the entry points sit beside the modules they extend`` () =
        let entries: obj list =
            [ box (Chart.show: Figure -> unit)
              box (Live.show: Live -> unit)
              box (Recorder.show: Recorder -> unit)
              box (Chart.toScene: Extent<px> -> Figure -> Node)
              box (Live.init: Live -> LiveState)
              box (Recorder.stop: Recorder -> Recording<LiveMsg>) ]

        Assert.All(entries, (fun e -> Assert.NotNull e))

/// M5.5 phase 7, gate 15 — the window plays a time control: play, pause and scrub send the
/// `Set`s the page would, stamped by the window's clock, and reach `Live.step`'s state.
module ``gate 15`` =

    let private timed () =
        interact {
            let! t = Control.time "t" 0.0<s> 4.0<s>
            and! damped = Control.checkbox "damped" false
            return wave (1.0<1 / s> + t * 0.5<1 / s^2>) damped
        }

    /// The events with their times, and the messages each must send.
    let private script (l: Live) =
        let size: Extent<px> = { Width = 800.0<px>; Height = 500.0<px> + WindowStrip.height l.Controls }
        let rows = WindowStrip.rows l.Controls size.Width
        let track = rows.[0].Widget
        let mid = (track.Y0 + track.Y1) / 2.0
        let along (f: float) : Vec2<px> = { X = track.X0 + f * (track.X1 - track.X0); Y = mid }
        let button = WindowStrip.playButton rows.[0]
        let press: Vec2<px> = { X = (button.X0 + button.X1) / 2.0; Y = (button.Y0 + button.Y1) / 2.0 }

        [ 0.0<s>, WindowEvent.Resized size, [ Resize(800.0, 500.0) ]
          // Tab to the time control and play it with the space bar at 1 s.
          0.5<s>, WindowEvent.Key(WindowKey.Tab, false), []
          1.0<s>, WindowEvent.Key(WindowKey.Space, false), []
          1.5<s>, WindowEvent.Frame, [ Set(0, 0.5) ]
          2.25<s>, WindowEvent.Frame, [ Set(0, 1.25) ]
          // Pause: the next frame, if the host sent one, sends nothing.
          2.5<s>, WindowEvent.Key(WindowKey.Space, false), []
          3.0<s>, WindowEvent.Frame, []
          // Scrub to a quarter of the track, and let go.
          3.5<s>, WindowEvent.Pressed(along 0.25, false), [ Set(0, 1.0) ]
          3.6<s>, WindowEvent.Released(along 0.25), []
          // Play from there with the button; past the end it wraps to the start.
          4.0<s>, WindowEvent.Pressed(press, false), []
          4.1<s>, WindowEvent.Released press, []
          6.5<s>, WindowEvent.Frame, [ Set(0, 3.5) ]
          8.0<s>, WindowEvent.Frame, [ Set(0, 1.0) ]
          // Scrubbing while it plays pauses it.
          8.2<s>, WindowEvent.Pressed(along 0.5, false), [ Set(0, 2.0) ]
          8.3<s>, WindowEvent.Released(along 0.5), []
          9.0<s>, WindowEvent.Frame, [] ]

    [<Fact>]
    let ``play, pause and scrub send the page's messages and reach its state`` () =
        let l = timed ()
        let events = script l
        let r = Live.recorder l
        let from = r.Begin()
        let mutable session = WindowSession.start l

        for t, e, expected in events do
            let next, msgs = WindowSession.feed l session (t, e)
            Assert.True((msgs = expected), sprintf "at %g s, %A sent %A, not %A" (float t) e msgs expected)
            msgs |> List.iter (fun m -> r.Note(from, Some t, m))
            session <- next

        Assert.Equal(None, session.Gesture.Playing)
        let expected = events |> List.collect (fun (_, _, m) -> m) |> List.fold (Live.step l) (Live.init l)
        Assert.True((session.State = expected), "the window's state is not the one the messages give")
        Assert.Equal(2.0, session.State.Values.[0])

        // And the recording is the page's, replaying to the same state.
        let log = Recorder.stop r
        let page = events |> List.collect (fun (t, _, msgs) -> msgs |> List.map (fun m -> t, m)) |> Array.ofList |> Recording.ofEvents
        Assert.Equal<(float<s> * LiveMsg)[]>(page.Events, log.Events)
        let replayed = Live.record l log
        Assert.True((snd replayed.Events.[replayed.Events.Length - 1] = session.State))

    /// A time control that stops at its end does stop there, and plays again from the start.
    [<Fact>]
    let ``a time control that does not loop stops at its end`` () =
        let l = interact {
                    let! t = Control.timeWith 10 false "t" 0.0<s> 2.0<s>
                    return wave (1.0<1 / s> + t * 0.5<1 / s^2>) false
                }

        let feed (s, _) (t, e) = WindowSession.feed l s (t, e)
        let s0 = WindowSession.start l
        let s1, _ = feed (s0, []) (0.0<s>, WindowEvent.Key(WindowKey.Tab, false))
        let s2, _ = feed (s1, []) (1.0<s>, WindowEvent.Key(WindowKey.Space, false))
        let s3, m3 = feed (s2, []) (5.0<s>, WindowEvent.Frame)
        Assert.Equal<LiveMsg list>([ Set(0, 2.0) ], m3)
        Assert.Equal(None, s3.Gesture.Playing)
        let s4, _ = feed (s3, []) (6.0<s>, WindowEvent.Key(WindowKey.Space, false))
        let _, m5 = feed (s4, []) (6.5<s>, WindowEvent.Frame)
        Assert.Equal<LiveMsg list>([ Set(0, 0.5) ], m5)
