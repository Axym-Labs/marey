namespace Marey

open System
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// M5 — THE WINDOW'S INPUT, AS MESSAGES (D118)
//
// The window has no interaction model of its own. A drag on the figure, a shifted drag, a double
// click and the pointer passing over it become the `LiveMsg`s the notebook widget's script sends
// for the same gestures, and the window steps them through `Live.step`, so zoom, pan, the readout
// and recording are M4.5's and are not written twice. What the window adds is what a page has
// and a GL surface does not: the controls, drawn here as a strip above the figure, worked by the
// pointer and the keyboard, each move a `Set`.
//
// **Pure, and the host calls nothing else.** `WindowSession.feed` is the whole of what happens
// between a GLFW callback and a new `LiveState`, so gate 12 scripts the events a person would
// cause and checks the state and the log without a display.
//
// Coordinates here are the window's, in px, top-left origin: what GLFW reports for the cursor.
// The figure sits under the strip, so a point on it is the window's point less the strip's
// height, which is the figure px every `LiveMsg` is stated in.
// ======================================================================================

/// A key the window answers. Anything else is ignored.
[<RequireQualifiedAccess>]
type WindowKey =
    /// The next control; with shift, the previous.
    | Tab
    /// The focused control one step down: a slider by its step, a select to the option before, a
    /// checkbox off.
    | Left
    /// One step up.
    | Right
    /// A slider to its low end, a select to its first option.
    | Home
    /// A slider to its high end, a select to its last option.
    | End
    /// A checkbox toggled; a time control played or paused.
    | Space

/// What the window's platform reports, in the window's px.
[<RequireQualifiedAccess>]
type WindowEvent =
    /// The content area has this size, in px. Sent once when the window opens and whenever it
    /// changes.
    | Resized of Extent<px>
    | Moved of Vec2<px>
    /// The primary button went down, with shift held or not.
    | Pressed of at: Vec2<px> * shift: bool
    | Released of Vec2<px>
    /// The pointer left the window.
    | Left
    | Key of WindowKey * shift: bool
    /// The frame loop's tick, which advances a time control that is playing (D137). The host
    /// sends it only while one is.
    | Frame

/// One control's row in the strip, in the window's px.
type internal WindowControlRow =
    { Index: int
      /// The whole row, the focus ring's box.
      Row: Rect<px>
      /// Where the label is written, left-aligned on the row's middle.
      Label: Vec2<px>
      /// What the pointer works: a slider's track, a select's segments end to end, a checkbox's box.
      Widget: Rect<px>
      /// Where the value is written, left-aligned on the row's middle.
      Value: Vec2<px> }

/// The control strip's layout: one row per control, above the figure.
[<RequireQualifiedAccess>]
module internal WindowStrip =

    /// A row's height.
    [<Literal>]
    let RowHeight = 28.0

    /// The margin around the strip and between a row's parts.
    [<Literal>]
    let Pad = 8.0

    /// A label's column and a value's column. A label longer than this runs under the widget,
    /// as a long label does in a narrow notebook cell.
    [<Literal>]
    let LabelWidth = 140.0

    [<Literal>]
    let ValueWidth = 120.0

    /// A time control's play button, before its track.
    [<Literal>]
    let PlayWidth = 20.0

    /// The strip's height: nothing when there are no controls, so a plain figure fills the window.
    let height (controls: ControlSpec[]) : float<px> =
        if controls.Length = 0 then
            0.0<px>
        else
            (float controls.Length * RowHeight + Pad) * 1.0<px>

    /// Every control's row, at a window width.
    let rows (controls: ControlSpec[]) (width: float<px>) : WindowControlRow[] =
        let w = float width

        controls
        |> Array.mapi (fun i c ->
            let y0 = Pad / 2.0 + float i * RowHeight
            let mid = y0 + RowHeight / 2.0
            let x0 = Pad + LabelWidth + Pad
            let x1 = max (x0 + 40.0) (w - Pad - ValueWidth - Pad)

            let box (x0: float) (y0: float) (x1: float) (y1: float) : Rect<px> =
                { X0 = x0 * 1.0<px>
                  Y0 = y0 * 1.0<px>
                  X1 = x1 * 1.0<px>
                  Y1 = y1 * 1.0<px> }

            let widget =
                match c.Kind with
                | Checkbox -> box x0 (mid - 8.0) (x0 + 16.0) (mid + 8.0)
                | Select _ -> box x0 (mid - 10.0) x1 (mid + 10.0)
                | Slider _ -> box x0 (mid - 8.0) x1 (mid + 8.0)
                // The track starts after the play button (`playButton`).
                | Time _ -> box (x0 + PlayWidth + Pad) (mid - 8.0) x1 (mid + 8.0)

            { Index = i
              Row = box (Pad / 2.0) y0 (max (x1 + Pad + ValueWidth) (w - Pad / 2.0)) (y0 + RowHeight)
              Label = { X = Pad * 1.0<px>; Y = mid * 1.0<px> }
              Widget = widget
              Value = { X = (x1 + Pad) * 1.0<px>; Y = mid * 1.0<px> } })

    /// A time control's play button: the square before its track.
    let playButton (row: WindowControlRow) : Rect<px> =
        let mid = (row.Widget.Y0 + row.Widget.Y1) / 2.0
        let x1 = row.Widget.X0 - Pad * 1.0<px>

        { X0 = x1 - PlayWidth * 1.0<px>
          Y0 = mid - PlayWidth / 2.0 * 1.0<px>
          X1 = x1
          Y1 = mid + PlayWidth / 2.0 * 1.0<px> }

    /// A select's option `k` of `n`: its segment of the widget.
    let segment (widget: Rect<px>) (n: int) (k: int) : Rect<px> =
        let w = (widget.X1 - widget.X0) / float n

        { widget with
            X0 = widget.X0 + w * float k
            X1 = widget.X0 + w * float (k + 1) }

    /// The value a slider takes with the pointer at `x`: the track's fraction of its range, which
    /// `Live.step` then clamps into it.
    let sliderValue (lo: float, hi: float) (widget: Rect<px>) (x: float<px>) : float =
        let t = (x - widget.X0) / (widget.X1 - widget.X0)
        lo + (max 0.0 (min 1.0 t)) * (hi - lo)

    /// Where a slider's knob is for a value.
    let knobX (lo: float, hi: float) (widget: Rect<px>) (v: float) : float<px> =
        widget.X0 + (widget.X1 - widget.X0) * ((v - lo) / (hi - lo))

    let internal inside (r: Rect<px>) (p: Vec2<px>) =
        p.X >= r.X0 && p.X <= r.X1 && p.Y >= r.Y0 && p.Y <= r.Y1

/// A drag on the figure, in the figure's px.
type internal WindowDrag =
    { Start: Vec2<px>
      /// Where the last pan message left off.
      Last: Vec2<px>
      /// Where the pointer is now, for the band.
      Now: Vec2<px>
      /// Shift was held when it began: a pan, not a zoom.
      Pan: bool }

/// The gesture in progress, which a `LiveState` does not hold because the notebook's page holds
/// it instead: a drag, a slider held under the pointer, the focused control, the last press (for
/// a double click) and whether the pointer is over the figure.
type internal WindowGesture =
    { Drag: WindowDrag option
      Hold: int option
      Focus: int option
      LastPress: (float<s> * Vec2<px>) option
      Over: bool
      /// D137 — a time control playing: its index, and the clock and the value play started
      /// from. Its value at a frame is the start's plus the time since, never a sum of frames.
      Playing: (int * float<s> * float) option
      /// The content area, which the strip's rows and the figure's place are laid out against.
      Window: Extent<px> }

/// A window's whole state: the live figure's, and the gesture's.
[<NoComparison>]
type internal WindowSession =
    { State: LiveState
      Gesture: WindowGesture }

[<RequireQualifiedAccess>]
module internal WindowSession =

    /// Two presses this close in time and space are a double click: a browser's defaults.
    [<Literal>]
    let DoubleClickSeconds = 0.5

    [<Literal>]
    let DoubleClickPx = 4.0

    /// A session at a live figure's first state, before the window has said its size.
    let start (live: Live) : WindowSession =
        { State = Live.init live
          Gesture =
            { Drag = None
              Hold = None
              Focus = None
              LastPress = None
              Over = false
              Playing = None
              Window = Live.DefaultSize } }

    /// Where the figure's px begin in the window's.
    let origin (live: Live) : Vec2<px> = { X = 0.0<px>; Y = WindowStrip.height live.Controls }

    /// Play a time control from its value now, or pause it if it is playing. Neither sends
    /// anything: the frames do. A control that stops at its end and stands there starts again
    /// from its beginning, as the page's button does.
    let private toggle (live: Live) (state: LiveState) (g: WindowGesture) (i: int) (t: float<s>) =
        match g.Playing, live.Controls.[i].Kind with
        | Some(j, _, _), _ when j = i -> { g with Playing = None }
        | _, Time(lo, hi, _, loop) ->
            let v = state.Values.[i]
            { g with Playing = Some(i, t, (if v >= hi && not loop then lo else v)) }
        | _ -> g

    /// The value a playing time control has at `t`, and whether it plays on.
    let private played (live: Live) (i: int) (t0: float<s>) (v0: float) (t: float<s>) =
        match live.Controls.[i].Kind with
        | Time(lo, hi, _, loop) ->
            let v = v0 + float (t - t0)

            if v <= hi then v, true
            elif loop then lo + (v - lo) % (hi - lo), true
            else hi, false
        | _ -> v0, false

    /// The messages a key sends: the focused control moved, as the page's own input would move it
    /// under the same key, or the focus moved, which sends nothing.
    let private key (live: Live) (state: LiveState) (g: WindowGesture) (k: WindowKey) (shift: bool) (t: float<s>) =
        let n = live.Controls.Length

        match k, g.Focus with
        | _ when n = 0 -> g, []
        | WindowKey.Tab, focus ->
            let next =
                match focus with
                | None -> if shift then n - 1 else 0
                | Some i -> ((if shift then i - 1 else i + 1) + n) % n

            { g with Focus = Some next }, []
        | _, None -> g, []
        | WindowKey.Space, Some i when (match live.Controls.[i].Kind with Time _ -> true | _ -> false) -> toggle live state g i t, []
        | k, Some i ->
            let v = state.Values.[i]
            // A key that moves a time control pauses it, as scrubbing does.
            let g = if (match g.Playing with Some(j, _, _) -> j = i | None -> false) then { g with Playing = None } else g

            let target =
                match live.Controls.[i].Kind, k with
                | Slider(_, _, step), WindowKey.Left -> Some(v - step)
                | Slider(_, _, step), WindowKey.Right -> Some(v + step)
                | Slider(lo, _, _), WindowKey.Home -> Some lo
                | Slider(_, hi, _), WindowKey.End -> Some hi
                | Select _, WindowKey.Left -> Some(v - 1.0)
                | Select _, WindowKey.Right -> Some(v + 1.0)
                | Select _, WindowKey.Home -> Some 0.0
                | Select options, WindowKey.End -> Some(float (options.Length - 1))
                | Checkbox, WindowKey.Left -> Some 0.0
                | Checkbox, WindowKey.Right -> Some 1.0
                | Checkbox, WindowKey.Space -> Some(1.0 - v)
                | Time(lo, hi, _, _), WindowKey.Left -> Some(v - (hi - lo) / 100.0)
                | Time(lo, hi, _, _), WindowKey.Right -> Some(v + (hi - lo) / 100.0)
                | Time(lo, _, _, _), WindowKey.Home -> Some lo
                | Time(_, hi, _, _), WindowKey.End -> Some hi
                | _ -> None

            g, (target |> Option.map (fun t -> Set(i, t)) |> Option.toList)

    /// The messages one event sends, and the gesture after it. `Live.step` is not called here.
    let translate (live: Live) (state: LiveState) (g: WindowGesture) (t: float<s>) (e: WindowEvent) : WindowGesture * LiveMsg list =
        let o = origin live
        let onFigure (p: Vec2<px>) = p.Y >= o.Y
        let rows = WindowStrip.rows live.Controls g.Window.Width

        let pressOn (p: Vec2<px>) =
            rows |> Array.tryFind (fun r -> WindowStrip.inside r.Row p)

        let set (i: int) (p: Vec2<px>) =
            let r = rows.[i]

            match live.Controls.[i].Kind with
            | Slider(lo, hi, _)
            | Time(lo, hi, _, _) -> Some(Set(i, WindowStrip.sliderValue (lo, hi) r.Widget p.X))
            | _ -> None

        match e with
        | WindowEvent.Resized size ->
            let g = { g with Window = size }
            let h = size.Height - o.Y

            // The figure's own area, as the page measures its frame and sends it. A window
            // shorter than the strip has no figure area to report.
            if size.Width > 0.0<px> && h > 0.0<px> then g, [ Resize(float size.Width, float h) ] else g, []
        | WindowEvent.Moved p ->
            match g.Hold, g.Drag with
            | Some i, _ -> g, (set i p |> Option.toList)
            | None, Some d when d.Pan ->
                let at = p - o
                { g with Drag = Some { d with Last = at; Now = at } }, [ Pan(d.Start, at - d.Last) ]
            | None, drag ->
                let at = p - o
                let g = { g with Drag = drag |> Option.map (fun d -> { d with Now = at }) }

                if onFigure p then { g with Over = true }, [ Hover at ]
                elif g.Over then { g with Over = false }, [ Leave ]
                else g, []
        | WindowEvent.Pressed(p, shift) when onFigure p ->
            let at = p - o

            { g with
                Drag = Some { Start = at; Last = at; Now = at; Pan = shift }
                Hold = None },
            []
        | WindowEvent.Pressed(p, _) ->
            match pressOn p with
            | None -> g, []
            | Some r ->
                let i = r.Index
                let g = { g with Focus = Some i; Drag = None }

                match live.Controls.[i].Kind with
                | Slider _ -> { g with Hold = Some i }, (set i p |> Option.toList)
                | Time _ when WindowStrip.inside (WindowStrip.playButton r) p -> toggle live state g i t, []
                // Scrubbing a time control pauses it.
                | Time _ ->
                    let g = if (match g.Playing with Some(j, _, _) -> j = i | None -> false) then { g with Playing = None } else g
                    { g with Hold = Some i }, (set i p |> Option.toList)
                | Select options when WindowStrip.inside r.Widget p ->
                    let k =
                        [ 0 .. options.Length - 1 ]
                        |> List.tryFind (fun k -> WindowStrip.inside (WindowStrip.segment r.Widget options.Length k) p)

                    g, (k |> Option.map (fun k -> Set(i, float k)) |> Option.toList)
                | Checkbox when WindowStrip.inside r.Widget p -> g, [ Set(i, 1.0 - state.Values.[i]) ]
                | _ -> g, []
        | WindowEvent.Released p ->
            let at = p - o
            let g' = { g with Drag = None; Hold = None }

            match g.Drag with
            | Some d when not d.Pan ->
                let zoom =
                    Zoom
                        { X0 = d.Start.X
                          Y0 = d.Start.Y
                          X1 = at.X
                          Y1 = at.Y }

                // A press and release that close to the last ones is the second half of a
                // double click: the page's `dblclick`, which arrives after the second `pointerup`.
                let near (t0: float<s>, p0: Vec2<px>) =
                    float (t - t0) <= DoubleClickSeconds
                    && abs (float (p0.X - d.Start.X)) <= DoubleClickPx
                    && abs (float (p0.Y - d.Start.Y)) <= DoubleClickPx

                match g.LastPress with
                | Some last when near last -> { g' with LastPress = None }, [ zoom; Reset ]
                | _ -> { g' with LastPress = Some(t, d.Start) }, [ zoom ]
            | _ -> g', []
        | WindowEvent.Left ->
            if g.Over && g.Drag.IsNone && g.Hold.IsNone then { g with Over = false }, [ Leave ] else g, []
        | WindowEvent.Key(k, shift) -> key live state g k shift t
        | WindowEvent.Frame ->
            match g.Playing with
            | Some(i, t0, v0) ->
                let v, on = played live i t0 v0 t
                (if on then g else { g with Playing = None }), [ Set(i, v) ]
            | None -> g, []

    /// **One event, through `Live.step`**: the session after it, and the messages it sent, in
    /// order, for a recorder to note.
    let feed (live: Live) (session: WindowSession) (t: float<s>, e: WindowEvent) : WindowSession * LiveMsg list =
        let g, msgs = translate live session.State session.Gesture t e
        let state = msgs |> List.fold (Live.step live) session.State
        { State = state; Gesture = g }, msgs
