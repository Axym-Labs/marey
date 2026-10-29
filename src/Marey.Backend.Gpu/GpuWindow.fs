// FS0009: GLFW's callbacks take the window as a native pointer.
#nowarn "9"

namespace Marey

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Threading
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols
open Microsoft.FSharp.NativeInterop
open Silk.NET.GLFW
open Silk.NET.OpenGL

/// What the window draws over the figure: the control strip, the zoom band and the readout, as
/// nodes in the window's own space, so the same painter draws them.
module internal WindowScene =

    let private sans (size: float<pt>) (weight: int) : FontSpec =
        { Family = Theme.Sans
          Size = size
          Weight = weight
          Italic = false }

    let private ink = Color.ofHex "#222222"
    let private quiet = Color.ofHex "#8a8a8a"
    let private rule = Color.ofHex "#c8c8c8"
    let private accent = Color.ofHex "#2f6db5"

    let private stroke (c: Color) (w: float<px>) : Stroke =
        { Paint = Solid c
          Width = w
          Dash = [||]
          DashOffset = 0.0<px>
          Cap = Butt
          Join = Miter
          MiterLimit = 4.0 }

    /// The layers for one frame: the figure placed under the strip, then the overlay.
    let layers
        (measurer: ITextMeasurer)
        (live: Live)
        (session: WindowSession)
        (figure: Node)
        : (Affine<nd, px> * Node) list =
        let window = session.Gesture.Window
        let state = session.State
        let w, h = float window.Width, float window.Height
        let o = WindowSession.origin live
        let figureExtent = state.Shot.Outer.Extent

        // The window's px, top-left origin, as the unit square the root transform maps.
        let nd (x: float<px>) (y: float<px>) : Vec2<nd> =
            { X = float x / w * 1.0<nd>
              Y = (1.0 - float y / h) * 1.0<nd> }

        let rectOf (r: Rect<px>) : Rect<nd> =
            let a, b = nd r.X0 r.Y0, nd r.X1 r.Y1
            { X0 = a.X; Y0 = b.Y; X1 = b.X; Y1 = a.Y }

        let text (at: Vec2<px>) (anchor: TextAnchor) (font: FontSpec) (c: Color) (s: string) =
            Text
                { Text = s
                  Tex = None
                  At = nd at.X at.Y
                  Font = font
                  Paint = Solid c
                  Anchor = anchor
                  Baseline = BaselineMiddle
                  Rotation = 0.0<rad> }

        let strip =
            WindowStrip.rows live.Controls window.Width
            |> Array.map (fun r ->
                let c = live.Controls.[r.Index]
                let v = state.Values.[r.Index]
                let mid = (r.Widget.Y0 + r.Widget.Y1) / 2.0

                let focus =
                    if session.Gesture.Focus = Some r.Index then
                        Scene.rect (Some(stroke (Color.withAlpha 0.6 accent) 1.0<px>)) None (rectOf r.Row)
                    else
                        Scene.empty

                let slider (lo: float) (hi: float) =
                    let knob = WindowStrip.knobX (lo, hi) r.Widget v
                    let track (x0: float<px>) (x1: float<px>) (col: Color) =
                        Scene.rect None (Some(Solid col)) (rectOf { X0 = x0; Y0 = mid - 2.0<px>; X1 = x1; Y1 = mid + 2.0<px> })

                    Scene.group
                        [ track r.Widget.X0 r.Widget.X1 rule
                          track r.Widget.X0 knob accent
                          Markers
                              { Points = [| nd knob mid |]
                                Shape = MarkCircle
                                Size = 14.0<px>
                                Fill = Some(Solid accent)
                                Stroke = Some(stroke Color.white 2.0<px>) } ]

                let widget =
                    match c.Kind with
                    | Slider(lo, hi, _) -> slider lo hi
                    // A slider with a play button: a triangle, or two bars while it plays.
                    | Time(lo, hi, _, _) ->
                        let b = WindowStrip.playButton r
                        let cx, cy = (b.X0 + b.X1) / 2.0, (b.Y0 + b.Y1) / 2.0

                        let glyph =
                            match session.Gesture.Playing with
                            | Some(i, _, _) when i = r.Index ->
                                Scene.group
                                    [ Scene.rect None (Some(Solid accent)) (rectOf { X0 = cx - 5.0<px>; Y0 = cy - 6.0<px>; X1 = cx - 1.5<px>; Y1 = cy + 6.0<px> })
                                      Scene.rect None (Some(Solid accent)) (rectOf { X0 = cx + 1.5<px>; Y0 = cy - 6.0<px>; X1 = cx + 5.0<px>; Y1 = cy + 6.0<px> }) ]
                            | _ ->
                                Scene.polygon
                                    None
                                    (Some(Solid accent))
                                    [| nd (cx - 4.0<px>) (cy - 6.0<px>); nd (cx + 6.0<px>) cy; nd (cx - 4.0<px>) (cy + 6.0<px>) |]

                        Scene.group [ glyph; slider lo hi ]
                    | Select options ->
                        let chosen = int (Num.roundEven v)

                        options
                        |> Array.mapi (fun k name ->
                            let s = WindowStrip.segment r.Widget options.Length k
                            let on = k = chosen

                            Scene.group
                                [ Scene.rect
                                      (Some(stroke (if on then accent else rule) 1.0<px>))
                                      (Some(Solid(if on then accent else Color.white)))
                                      (rectOf s)
                                  text
                                      { X = (s.X0 + s.X1) / 2.0; Y = mid }
                                      AnchorMiddle
                                      (sans 9.0<pt> 400)
                                      (if on then Color.white else ink)
                                      name ])
                        |> Scene.group
                    | Checkbox ->
                        let on = v <> 0.0
                        let b = r.Widget

                        Scene.group
                            [ Scene.rect
                                  (Some(stroke (if on then accent else quiet) 1.0<px>))
                                  (Some(Solid(if on then accent else Color.white)))
                                  (rectOf b)
                              if on then
                                  Scene.polyline
                                      { stroke Color.white 2.0<px> with Cap = Round; Join = RoundJoin }
                                      [| nd (b.X0 + 3.5<px>) mid
                                         nd (b.X0 + 6.5<px>) (mid + 3.5<px>)
                                         nd (b.X1 - 3.0<px>) (mid - 4.0<px>) |] ]

                Scene.group
                    [ focus
                      text r.Label AnchorStart (sans 10.0<pt> 400) ink c.Label
                      widget
                      text r.Value AnchorStart (sans 10.0<pt> 400) ink (c.Show v) ])
            |> Scene.group

        let band =
            match session.Gesture.Drag with
            | Some d when not d.Pan && d.Now <> d.Start ->
                let a, b = d.Start + o, d.Now + o

                Scene.rect
                    (Some { stroke quiet 1.0<px> with Dash = [| 4.0<px>; 3.0<px> |] })
                    (Some(Solid(Color.withAlpha 0.12 quiet)))
                    (rectOf
                        { X0 = min a.X b.X
                          Y0 = min a.Y b.Y
                          X1 = max a.X b.X
                          Y1 = max a.Y b.Y })
            | _ -> Scene.empty

        let readout =
            match (try Live.readout live state with _ -> None) with
            | None -> Scene.empty
            | Some r ->
                let font = sans 9.0<pt> 400
                let lineHeight = 15.0<px>
                let widest = r.Lines |> Array.map (fun l -> (measurer.Measure font l).Advance) |> Array.fold max 0.0<px>
                let bw, bh = widest + 12.0<px>, lineHeight * float r.Lines.Length + 6.0<px>
                let x0 = if r.Right then r.Plot.X1 - 4.0<px> - bw else r.Plot.X0 + 4.0<px>
                let box = { X0 = x0 + o.X; Y0 = r.Plot.Y0 + 4.0<px> + o.Y; X1 = x0 + o.X + bw; Y1 = r.Plot.Y0 + 4.0<px> + o.Y + bh }

                Scene.group
                    [ yield Scene.rect (Some(stroke rule 1.0<px>)) (Some(Solid(Color.withAlpha 0.92 Color.white))) (rectOf box)
                      for k, line in Array.indexed r.Lines ->
                          text { X = box.X0 + 6.0<px>; Y = box.Y0 + 3.0<px> + lineHeight * (float k + 0.5) } AnchorStart font ink line ]

        let root = SkiaPainter.RootTransform window

        let placed =
            Affine.fromRects
                Rect.unit
                { X0 = o.X
                  Y0 = o.Y + figureExtent.Height
                  X1 = o.X + figureExtent.Width
                  Y1 = o.Y }

        let caps = GpuContext.Capabilities
        let degrade (n: Node) = n |> Degrade.forCaps caps |> Degrade.splitBatches caps

        [ root, degrade strip; placed, figure; root, degrade (Scene.group [ band; readout ]) ]

/// **A live figure, or a figure, in a window on the screen.**
///
/// ```fsharp
/// Chart.show fig                 // blocks until the window is closed
/// Live.show (interact { … })
/// use w = GpuWindow.Open live    // or keep the prompt: the window runs on its own thread
/// ```
///
/// **The window speaks `LiveMsg`.** A drag on the figure zooms the panel it began on, a shifted
/// drag pans, a double click resets, and the pointer reads the data under it: the notebook
/// widget's gestures, turned into the widget's messages and stepped by `Live.step`
/// (`WindowSession`). The controls are a strip above the figure, worked with the pointer, or with
/// Tab and the arrow, Home, End and space keys. Escape closes the window. A `Recorder` shown here
/// records as it does in a notebook, by this window's clock.
///
/// **Laid out by the widget's measurer, drawn by Skia.** A live state's arrangement is
/// `Live.step`'s, which measures with `TextMeasure.approx` as the notebook does, so a session in
/// the window steps to exactly the states the same messages give anywhere else. The text is then
/// drawn with real fonts at the places that measurer chose, which is the SVG widget's
/// imprecision, unchanged.
///
/// **Its own thread, its own context.** The window, its GL context and every GLFW callback live
/// on one thread the window starts, so the caller's thread is free: `Open` returns at once and
/// `Wait` blocks until the window closes. `Chart.show` and `Live.show` are `Open` then `Wait`,
/// which in `dotnet fsi` is what a script wants, since a script that returned would close the
/// window with the process. On Linux and Windows GLFW accepts a window off the main thread;
/// macOS does not, and is not supported here.
///
/// The figure is laid out at the window's size in its own px and drawn at the screen's pixels per
/// px, so on a high-density display its lines and text are as sharp as the screen.
[<Sealed>]
type GpuWindow private (live: Live, recorder: Recorder option, title: string, size: Extent<px>, options: SkiaOptions, visible: bool) =

    let events = ConcurrentQueue<float<s> * WindowEvent>()
    let jobs = ConcurrentQueue<unit -> unit>()
    let started = new ManualResetEventSlim(false)
    let closed = new ManualResetEventSlim(false)
    let clock = Stopwatch.StartNew()
    let mutable closing = false
    let mutable failure: exn option = None
    let mutable session = WindowSession.start live
    let mutable readFrame: unit -> Marey.Image = fun () -> invalidOp "Marey.GpuWindow: nothing has been drawn yet."

    let now () = clock.Elapsed.TotalSeconds * 1.0<s>
    let post (e: WindowEvent) = events.Enqueue((now (), e))

    let keyOf (k: Keys) =
        match k with
        | Keys.Tab -> Some WindowKey.Tab
        | Keys.Left
        | Keys.Down -> Some WindowKey.Left
        | Keys.Right
        | Keys.Up -> Some WindowKey.Right
        | Keys.Home -> Some WindowKey.Home
        | Keys.End -> Some WindowKey.End
        | Keys.Space -> Some WindowKey.Space
        | _ -> None

    let loop () =
        try
            let context =
                try
                    new GpuContext(shown = (title, int (ceil (float size.Width)), int (ceil (float size.Height + float (WindowStrip.height live.Controls))), visible))
                with e ->
                    raise (
                        InvalidOperationException(
                            "Marey.GpuWindow: no window with an OpenGL 3.3 core context could be opened. GLFW: "
                            + GlfwDiagnosis.why ()
                            + ". The driver said: "
                            + e.Message,
                            e
                        )
                    )

            use context = context
            use shaping = new SkiaTextMeasurer(?fontFile = options.FontFile, dpi = options.Dpi, fontFiles = options.FontFiles)
            let window = context.Window
            let gl = context.GL

            let glfw = Glfw.GetApi()
            let handle = NativePtr.ofNativeInt<WindowHandle> window.Native.Glfw.Value
            let mutable cursor: Vec2<px> = { X = 0.0<px>; Y = 0.0<px> }

            // Kept in fields of this closure for the window's lifetime: GLFW holds a function
            // pointer, and a collected delegate behind it is a crash on the next event.
            let onCursor =
                GlfwCallbacks.CursorPosCallback(fun _ x y ->
                    cursor <- { X = x * 1.0<px>; Y = y * 1.0<px> }
                    post (WindowEvent.Moved cursor))

            let onButton =
                GlfwCallbacks.MouseButtonCallback(fun _ button action mods ->
                    if button = MouseButton.Left then
                        match action with
                        | InputAction.Press -> post (WindowEvent.Pressed(cursor, mods.HasFlag KeyModifiers.Shift))
                        | InputAction.Release -> post (WindowEvent.Released cursor)
                        | _ -> ())

            let onEnter =
                GlfwCallbacks.CursorEnterCallback(fun _ entered -> if not entered then post WindowEvent.Left)

            let onKey =
                GlfwCallbacks.KeyCallback(fun _ key _ action mods ->
                    if action <> InputAction.Release then
                        if key = Keys.Escape then
                            closing <- true
                        else
                            keyOf key
                            |> Option.iter (fun k -> post (WindowEvent.Key(k, mods.HasFlag KeyModifiers.Shift))))

            lock GlfwLibrary.gate (fun () ->
                glfw.SetCursorPosCallback(handle, onCursor) |> ignore
                glfw.SetMouseButtonCallback(handle, onButton) |> ignore
                glfw.SetCursorEnterCallback(handle, onEnter) |> ignore
                glfw.SetKeyCallback(handle, onKey) |> ignore)

            let from = recorder |> Option.map (fun r -> r.Begin())
            let mutable shownSize: Extent<px> = { Width = 0.0<px>; Height = 0.0<px> }
            let mutable figure: (LiveState * Node) option = None
            let mutable dirty = true
            let mutable painted = (1, 1)

            let sizeNow () : Extent<px> =
                let s = window.Size
                { Width = float s.X * 1.0<px>; Height = float s.Y * 1.0<px> }

            // The window's first size is its first event, ahead of anything posted once `Open`
            // has returned: the page's hello, before the reader can touch it.
            shownSize <- sizeNow ()
            post (WindowEvent.Resized shownSize)
            started.Set()

            let draw () =
                let g = session.Gesture.Window
                let fb = window.FramebufferSize

                // The window's size is its px and the framebuffer its screen's pixels, more of
                // them on a high-density display. The frame is drawn at that ratio, at the
                // framebuffer's size, so it reaches the screen pixel for pixel. A minimised
                // window has no framebuffer, and is drawn at one pixel per px.
                let w, h, scale =
                    if fb.X > 0 && fb.Y > 0 && g.Width > 0.0<px> then
                        fb.X, fb.Y, float fb.X / float g.Width
                    else
                        max 1 (int (ceil (float g.Width))), max 1 (int (ceil (float g.Height))), 1.0

                let scene =
                    match figure with
                    | Some(before, node) when Live.drawsAlike before session.State -> node
                    | _ ->
                        let caps = GpuContext.Capabilities

                        let node =
                            try
                                Live.scene live session.State |> Degrade.forCaps caps |> Degrade.splitBatches caps
                            with e ->
                                eprintfn "Marey.GpuWindow: the view failed, and the window shows nothing for this state: %s" e.Message
                                Scene.empty

                        figure <- Some(session.State, node)
                        node

                let target = context.Paint(options, shaping, true, w, h, scale, WindowScene.layers shaping live session scene)
                painted <- (w, h)
                gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, target.Framebuffer)
                gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0u)
                gl.Viewport(0, 0, uint32 fb.X, uint32 fb.Y)

                gl.BlitFramebuffer(
                    0, 0, w, h,
                    0, 0, fb.X, fb.Y,
                    ClearBufferMask.ColorBufferBit,
                    (if fb.X = w && fb.Y = h then BlitFramebufferFilter.Nearest else BlitFramebufferFilter.Linear)
                )

                context.Skia.ResetContext()
                window.GLContext.SwapBuffers()
                dirty <- false

            readFrame <- fun () -> (context.Target painted).ReadImage()

            // Every event waiting, through the session; then a frame, if anything changed.
            let pump () =
                let size = sizeNow ()

                if size <> shownSize then
                    shownSize <- size
                    post (WindowEvent.Resized size)

                let mutable item = Unchecked.defaultof<float<s> * WindowEvent>

                while events.TryDequeue(&item) do
                    let t, e = item

                    let next, msgs =
                        try
                            WindowSession.feed live session (t, e)
                        with ex ->
                            eprintfn "Marey.GpuWindow: a step failed and was skipped: %s" ex.Message
                            session, []

                    match recorder, from with
                    | Some r, Some f -> msgs |> List.iter (fun m -> r.Note(f, Some t, m))
                    | _ -> ()

                    session <- next
                    dirty <- true

                if dirty then
                    draw ()

            while not closing && not window.IsClosing do
                lock GlfwLibrary.gate (fun () -> glfw.WaitEventsTimeout 0.01)

                // A time control playing advances on this loop, by this clock, through
                // the same feed as everything else, so a recording replays the playback.
                if session.Gesture.Playing.IsSome then
                    post WindowEvent.Frame

                pump ()
                let mutable job = Unchecked.defaultof<unit -> unit>

                while jobs.TryDequeue(&job) do
                    pump ()
                    job ()

            // A job that arrived as the window closed still gets an answer.
            let mutable job = Unchecked.defaultof<unit -> unit>

            while jobs.TryDequeue(&job) do
                job ()
        with e ->
            failure <- Some e
            started.Set()

        closing <- true
        closed.Set()

    let thread = Thread(loop, IsBackground = true, Name = "Marey window")

    do
        thread.Start()
        started.Wait()

        match failure with
        | Some e -> raise e
        | None -> ()

    /// Run on the window's thread, after every event posted before it, and wait.
    let invoke (f: unit -> 'a) : 'a =
        if closed.IsSet then
            invalidOp "Marey.GpuWindow: the window is closed."

        let result = System.Threading.Tasks.TaskCompletionSource<'a>()

        jobs.Enqueue(fun () ->
            try
                result.SetResult(f ())
            with e ->
                result.SetException e)

        result.Task.GetAwaiter().GetResult()

    /// Open a live figure in a window. Returns at once; the window runs on its own thread.
    /// `visible = false` opens it hidden, for a test that drives it with `Post`.
    static member Open(live: Live, ?title: string, ?size: Extent<px>, ?options: SkiaOptions, ?visible: bool) : GpuWindow =
        new GpuWindow(live, None, defaultArg title "Marey", defaultArg size GpuWindow.DefaultSize, defaultArg options SkiaOptions.Default, defaultArg visible true)

    /// Open a recorder's live figure, noting what the reader does as a notebook would.
    /// `Recorder.stop` gives the log, once the window is closed or while it is open.
    static member Open(recorder: Recorder, ?title: string, ?size: Extent<px>, ?options: SkiaOptions, ?visible: bool) : GpuWindow =
        new GpuWindow(recorder.Live, Some recorder, defaultArg title "Marey", defaultArg size GpuWindow.DefaultSize, defaultArg options SkiaOptions.Default, defaultArg visible true)

    /// Open a figure in a window: a live figure with no controls, so it zooms, pans and reads
    /// out as one does.
    static member Open(figure: Figure, ?title: string, ?size: Extent<px>, ?options: SkiaOptions, ?visible: bool) : GpuWindow =
        let live = { Controls = [||]; View = fun _ -> figure }
        new GpuWindow(live, None, defaultArg title "Marey", defaultArg size GpuWindow.DefaultSize, defaultArg options SkiaOptions.Default, defaultArg visible true)

    /// The figure's area a window opens with, under any control strip: 8:5.
    static member DefaultSize: Extent<px> = { Width = 800.0<px>; Height = 500.0<px> }

    /// Deliver an event as if the platform had reported it, stamped by the window's clock now.
    member _.Post(e: WindowEvent) : unit = post e

    /// The session as it stands, once every event posted before the call has been stepped.
    member internal _.Session: WindowSession = invoke (fun () -> session)

    /// The live state as it stands, as `Session`.
    member this.State: LiveState = this.Session.State

    /// The frame on the screen, as an `Image` in the screen's pixels (twice the window's px each
    /// way at a scaling of 200 %), once every event posted before the call has been stepped and
    /// drawn.
    member _.Capture() : Marey.Image =
        invoke (fun () -> readFrame ())

    /// Whether the window is still open.
    member _.IsOpen: bool = not closed.IsSet

    /// Ask the window to close. Returns at once; `Wait` for it.
    member _.Close() : unit = closing <- true

    /// Block until the window is closed, by the reader or by `Close`. A failure on the window's
    /// thread is raised here.
    member _.Wait() : unit =
        closed.Wait()

        match failure with
        | Some e -> raise e
        | None -> ()

    interface IDisposable with
        member this.Dispose() =
            this.Close()
            closed.Wait()

/// The window's entry points, beside the modules they extend. Nested in a module of their own so
/// that no two Marey assemblies export one type name: a C# caller reaches them as
/// `GpuShow.Chart.Show`, and an F# caller, for whom this module opens with `Marey`, as `Chart.show`.
[<AutoOpen>]
module GpuShow =

    /// `Chart.show`, beside the core's `Chart`: F# resolves `Chart.x` across every `Chart` in scope.
    [<RequireQualifiedAccess>]
    module Chart =

        /// Show a figure in a window and wait until it is closed (`GpuWindow`). Drag to zoom,
        /// shift-drag to pan, double-click to reset, Escape to close.
        [<CompiledName("Show")>]
        let show (figure: Figure) : unit =
            use w = GpuWindow.Open figure
            w.Wait()

    /// `Live.show`, beside `Marey.Interact`'s `Live`.
    [<RequireQualifiedAccess>]
    module Live =

        /// Show a live figure in a window, its controls above it, and wait until it is closed
        /// (`GpuWindow`).
        [<CompiledName("Show")>]
        let show (live: Live) : unit =
            use w = GpuWindow.Open live
            w.Wait()

    /// `Recorder.show`, beside `Marey.Interact`'s `Recorder`.
    [<RequireQualifiedAccess>]
    module Recorder =

        /// Show a recorder's live figure in a window and wait until it is closed; `Recorder.stop`
        /// then gives what the reader did, by the window's clock, as a notebook session's log.
        [<CompiledName("Show")>]
        let show (recorder: Recorder) : unit =
            use w = GpuWindow.Open recorder
            w.Wait()
