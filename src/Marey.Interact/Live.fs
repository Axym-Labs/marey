namespace Marey

open System
open System.Globalization
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// M4.5 — THE PURE STEP (D100)
//
// A live figure is a value: its controls, and a view from their values to a `Figure`. What a
// host does with it is a fold. `Live.init` gives the first state, `Live.step` the next one for
// each message the reader sends, and `Live.view` the figure for any state. Nothing here is
// mutable and nothing names a host, so a session can be replayed from its messages, tested
// without a notebook, and snapshotted by rendering the current state.
//
// **The view is re-run whole on every change.** There is no graph of derived signals, so there
// is no order in which they update and no moment at which one of them is stale (D101). What the
// re-run costs is the cost of building a figure, which the library already pays per frame of a
// film; what it would cost to send is the differ's business (D103), not this file's.
//
// **The wire form is deliberately small.** Verso hands an extension the payload as JSON text,
// and this package may not take a JSON dependency. A message is a type and a payload short
// enough to read by hand — `set` with `[index,value]` — so the grammar is the parser.
// ======================================================================================

/// A figure with controls: what the `interact { }` block produces.
[<NoEquality; NoComparison>]
type Live =
    { Controls: ControlSpec[]
      View: float[] -> Figure }

/// Which axis of a panel a zoom states.
[<RequireQualifiedAccess>]
type ZoomAxis =
    | X
    | Y

/// D105 — one axis the reader has zoomed or panned: the panel it was done on, which axis, and the
/// domain, raw, in that axis's own unit.
type LiveZoom =
    { Row: int
      Col: int
      Axis: ZoomAxis
      Lo: float
      Hi: float }

/// Where a live figure is.
///
/// Arrays inside a record still compare structurally under F#'s `=`, so two states are equal
/// exactly when they hold the same values, size and arrangement, which is what a replay test needs.
[<NoComparison>]
type LiveState =
    { /// In `Live.Controls` order, each already normalised.
      Values: float[]
      /// The surface the widget draws on: the frame's width as the view measured it, at the
      /// figure's aspect, or `Live.DefaultSize` until the view has said.
      Size: Extent<px>
      /// D106 — the one arrangement every state is drawn in, laid out over the controls'
      /// extremes and any state in `Grown`.
      Shot: Shot
      /// D106 — the states that did not fit an earlier arrangement and were added to its union.
      /// Its length is how many times the widget re-arranged, which is reported rather than
      /// hidden: each one is a jump the reader saw.
      Grown: float[][]
      /// D105 — the axes the reader has zoomed or panned, oldest first, at most one per panel and
      /// axis. Applied over whatever the view returns, so a slider moved while zoomed stays zoomed.
      Zoom: LiveZoom[]
      /// D105 — where the pointer is over the figure, in the figure's px, while it is over it.
      /// Nothing is drawn from it: `Live.readout` reads it, and the figure is the same with or
      /// without it (`Live.drawsAlike`).
      Pointer: Vec2<px> option }

/// D105 — what the pointer reads, and where it is shown: inside the top corner of the plot it is
/// over, away from the legend, so it never moves the layout.
type LiveReadout =
    { Row: int
      Col: int
      /// The plot rectangle the pointer is over, in the figure's px.
      Plot: Rect<px>
      /// The top-right corner rather than the top-left, because the panel's legend is there.
      Right: bool
      /// One line per axis, x first (`PanelView.read`).
      Lines: string[] }

/// What the reader can do to a live figure.
type LiveMsg =
    /// Set control `index` to `value` (its unit's value, an option's index, or 0 and 1).
    | Set of index: int * value: float
    /// The view measured its frame: draw at this width and height, in px.
    | Resize of width: float * height: float
    /// D105 — a drag on the figure, from `(X0, Y0)` where the pointer went down to `(X1, Y1)`
    /// where it came up, in the figure's px. Zooms the panel it started on to what it covered.
    | Zoom of Rect<px>
    /// D105 — a shifted drag: move what was under `at` by `by`, in px, on the panel under `at`.
    | Pan of at: Vec2<px> * by: Vec2<px>
    /// D105 — a double click: every axis back to the view's own domain.
    | Reset
    /// D105 — the pointer moved to here, in the figure's px.
    | Hover of Vec2<px>
    /// D105 — the pointer left the figure.
    | Leave

[<RequireQualifiedAccess>]
module LiveMsg =

#if !FABLE_COMPILER
    let private invariant = CultureInfo.InvariantCulture
#endif

    /// `[a,b,…]` as numbers, or nothing.
    let private numbers (payload: string) : float[] option =
        if isNull payload then
            None
        else
            let body = payload.Trim()

            if body.Length >= 2 && body.[0] = '[' && body.[body.Length - 1] = ']' then
                let inner = body.Substring(1, body.Length - 2)

                if inner.Trim() = "" then
                    Some [||]
                else
                    let parsed =
                        inner.Split(',')
#if FABLE_COMPILER
                        |> Array.map (fun a -> Double.TryParse(a.Trim()))
#else
                        |> Array.map (fun a -> Double.TryParse(a.Trim(), NumberStyles.Float, invariant))
#endif

                    if parsed |> Array.forall fst then Some(parsed |> Array.map snd) else None
            else
                None

    /// How many numbers a message of this type carries before the view's clock reading.
    let private arity (msgType: string) =
        match msgType with
        | "set"
        | "size"
        | "hover" -> Some 2
        | "zoom"
        | "pan" -> Some 4
        | "reset"
        | "leave" -> Some 0
        | _ -> None

    /// A message from its wire type and numbers, without a time: `toWire`'s inverse.
    let ofWire (msgType: string) (ns: float[]) : LiveMsg option =
        let px (v: float) = v * 1.0<px>

        match msgType, ns with
        | "set", [| i; v |] when i = Math.Floor i && abs i < 1e9 -> Some(Set(int i, v))
        | "size", [| w; h |] -> Some(Resize(w, h))
        | "zoom", [| x0; y0; x1; y1 |] -> Some(Zoom { X0 = px x0; Y0 = px y0; X1 = px x1; Y1 = px y1 })
        | "pan", [| x; y; dx; dy |] -> Some(Pan({ X = px x; Y = px y }, { X = px dx; Y = px dy }))
        | "reset", _ -> Some Reset
        | "hover", [| x; y |] -> Some(Hover { X = px x; Y = px y })
        | "leave", _ -> Some Leave
        | _ -> None

    /// Read a message from the wire, with the view's clock reading if it sent one (D102): a type
    /// and its JSON payload text. `set` takes `[index,value]`, `size` `[width,height]`, `zoom`
    /// `[x0,y0,x1,y1]`, `pan` `[x,y,dx,dy]`, `hover` `[x,y]`, and `reset` and `leave` anything;
    /// each may carry one number more, the event's `timeStamp` in milliseconds, which comes back
    /// in seconds. Anything else is `None`, and a host ignores it rather than failing: a view one
    /// protocol version ahead should not take the widget down.
    let parseStamped (msgType: string) (payload: string) : (float<s> option * LiveMsg) option =
        match arity msgType, numbers payload with
        | Some 0, ns ->
            let t =
                match ns with
                | Some [| t |] -> Some(t / 1000.0 * 1.0<s>)
                | _ -> None

            ofWire msgType [||] |> Option.map (fun m -> t, m)
        | Some n, Some ns when ns.Length = n -> ofWire msgType ns |> Option.map (fun m -> None, m)
        | Some n, Some ns when ns.Length = n + 1 ->
            ofWire msgType (Array.sub ns 0 n)
            |> Option.map (fun m -> Some(ns.[n] / 1000.0 * 1.0<s>), m)
        | _ -> None

    /// `parseStamped`, without the time.
    let parse (msgType: string) (payload: string) : LiveMsg option =
        parseStamped msgType payload |> Option.map snd

    /// A message as its wire type and numbers, without a time: `parse`'s inverse.
    let toWire (msg: LiveMsg) : string * float[] =
        let f (v: float<px>) = float v

        match msg with
        | Set(i, v) -> "set", [| float i; v |]
        | Resize(w, h) -> "size", [| w; h |]
        | Zoom r -> "zoom", [| f r.X0; f r.Y0; f r.X1; f r.Y1 |]
        | Pan(at, by) -> "pan", [| f at.X; f at.Y; f by.X; f by.Y |]
        | Reset -> "reset", [||]
        | Hover at -> "hover", [| f at.X; f at.Y |]
        | Leave -> "leave", [||]

/// **A session's messages as data (D102)**: what the reader did and when, by the view's clock. A
/// log replayed through `Live.record` is the session again, because the step is pure; so the
/// log is what is kept and written down, and the states are recomputed from it.
///
/// The JSON is written and read here rather than through a library, because this package may
/// not take a dependency. Numbers are written to round-trip exactly, and the three that JSON
/// has no number for as the strings `"NaN"`, `"Infinity"` and `"-Infinity"`, so a log that went
/// out to a file comes back bit for bit.
[<RequireQualifiedAccess>]
module LiveLog =

#if !FABLE_COMPILER
    let private invariant = CultureInfo.InvariantCulture
#endif

    [<Literal>]
    let Format = "marey.live-log"

    [<Literal>]
    let Version = 1

    let private number (x: float) =
        if Double.IsNaN x then "\"NaN\""
        elif Double.IsPositiveInfinity x then "\"Infinity\""
        elif Double.IsNegativeInfinity x then "\"-Infinity\""
        else Num.shortest x

    /// The log as one line of JSON:
    /// `{"format":"marey.live-log","version":1,"events":[[0,"set",[0,2.5]],…]}`, each event its
    /// time in seconds, its wire type and its numbers.
    let toJson (log: Recording<LiveMsg>) : string =
        let event (t: float<s>, msg: LiveMsg) =
            let kind, ns = LiveMsg.toWire msg
            sprintf "[%s,\"%s\",[%s]]" (number (float t)) kind (ns |> Array.map number |> String.concat ",")

        sprintf
            "{\"format\":\"%s\",\"version\":%d,\"events\":[%s]}"
            Format
            Version
            (log.Events |> Array.map event |> String.concat ",")

    type private Json =
        | JNum of float
        | JStr of string
        | JArr of Json list
        | JObj of (string * Json) list
        | JLit of string

    /// Just enough JSON to read a log back: a value, or `None` for anything malformed.
    let private parseJson (text: string) : Json option =
        let n = text.Length
        let mutable i = 0
        let ws () = while i < n && Char.IsWhiteSpace text.[i] do i <- i + 1
        let fail () = raise (FormatException())

        let expect (c: char) =
            ws ()
            if i < n && text.[i] = c then i <- i + 1 else fail ()

        let str () =
            expect '"'
            let sb = Text.StringBuilder()

            while (if i < n then text.[i] <> '"' else fail ()) do
                if text.[i] = '\\' && i + 1 < n then
                    match text.[i + 1] with
                    | 'u' when i + 5 < n ->
                        sb.Append(char (Convert.ToInt32(text.Substring(i + 2, 4), 16))) |> ignore
                        i <- i + 6
                    | c ->
                        sb.Append(
                            match c with
                            | 'n' -> '\n'
                            | 't' -> '\t'
                            | 'r' -> '\r'
                            | 'b' -> '\b'
                            | 'f' -> '\f'
                            | c -> c
                        )
                        |> ignore

                        i <- i + 2
                else
                    sb.Append(text.[i]) |> ignore
                    i <- i + 1

            i <- i + 1
            sb.ToString()

        let rec value () : Json =
            ws ()

            if i >= n then
                fail ()

            match text.[i] with
            | '"' -> JStr(str ())
            | '[' ->
                i <- i + 1
                ws ()

                if i < n && text.[i] = ']' then
                    i <- i + 1
                    JArr []
                else
                    let items = ResizeArray [ value () ]
                    ws ()

                    while i < n && text.[i] = ',' do
                        i <- i + 1
                        items.Add(value ())
                        ws ()

                    expect ']'
                    JArr(List.ofSeq items)
            | '{' ->
                i <- i + 1
                ws ()

                if i < n && text.[i] = '}' then
                    i <- i + 1
                    JObj []
                else
                    let pair () =
                        ws ()
                        let k = str ()
                        expect ':'
                        k, value ()

                    let items = ResizeArray [ pair () ]
                    ws ()

                    while i < n && text.[i] = ',' do
                        i <- i + 1
                        items.Add(pair ())
                        ws ()

                    expect '}'
                    JObj(List.ofSeq items)
            | c when Char.IsLetter c ->
                let start = i
                while i < n && Char.IsLetter text.[i] do i <- i + 1
                JLit(text.Substring(start, i - start))
            | _ ->
                let start = i

                while i < n && (Char.IsDigit text.[i] || "+-.eE".IndexOf text.[i] >= 0) do
                    i <- i + 1

#if FABLE_COMPILER
                // JavaScript parses one number syntax, with no culture: `NumberStyles.Float` and
                // the invariant culture are what it does already, and Fable refuses to be told.
                match Double.TryParse(text.Substring(start, i - start)) with
#else
                match Double.TryParse(text.Substring(start, i - start), NumberStyles.Float, invariant) with
#endif
                | true, x when i > start -> JNum x
                | _ -> fail ()

        if isNull text then
            None
        else
            try
                let v = value ()
                ws ()
                if i = n then Some v else None
            with
            | :? FormatException
            | :? ArgumentException
            | :? OverflowException -> None

    let private numberOf (j: Json) =
        match j with
        | JNum x -> Some x
        | JStr "NaN" -> Some Double.NaN
        | JStr "Infinity" -> Some Double.PositiveInfinity
        | JStr "-Infinity" -> Some Double.NegativeInfinity
        | _ -> None

    /// A log back from `toJson`'s text. `Error` names what is wrong: not JSON, not a Marey log,
    /// a version this library does not read, or the first event it cannot read, by index. An
    /// unreadable event is refused rather than skipped, because a replay with a step missing is
    /// a different session.
    let ofJson (json: string) : Result<Recording<LiveMsg>, string> =
        let field k fields = fields |> List.tryFind (fst >> (=) k) |> Option.map snd

        match parseJson json with
        | Some(JObj fields) ->
            match field "format" fields, field "version" fields, field "events" fields with
            | Some(JStr Format), Some(JNum v), Some(JArr events) when v = float Version ->
                let read (j: Json) =
                    match j with
                    | JArr [ t; JStr kind; JArr ns ] ->
                        let ns = ns |> List.map numberOf

                        match numberOf t with
                        | Some t when ns |> List.forall Option.isSome ->
                            LiveMsg.ofWire kind (ns |> List.map Option.get |> Array.ofList)
                            |> Option.map (fun m -> t * 1.0<s>, m)
                        | _ -> None
                    | _ -> None

                let read = events |> List.map read

                match read |> List.tryFindIndex Option.isNone with
                | Some k -> Error(sprintf "event %d is not a message this library reads" k)
                | None -> Ok { Events = read |> List.map Option.get |> Array.ofList }
            | Some(JStr Format), Some(JNum v), _ when v <> float Version ->
                Error(sprintf "a version %g log; this library reads version %d" v Version)
            | _ -> Error "not a Marey live log: it needs format, version and events"
        | Some _ -> Error "not a Marey live log: it needs format, version and events"
        | None -> Error "not JSON"

/// **A live figure that keeps what the reader did (D102).** Displayed, it is the same widget as
/// its `Live`; the host steps it exactly as it would, and each message the step is given is
/// noted with the view's time. `Recorder.stop` gives the log, and `Live.record` and `Live.film`
/// turn it into a film.
///
/// This is the one mutable thing in the package, and it is mutable because the host holds the
/// session and the notebook holds the handle: the log has to be somewhere both can reach. The
/// step stays pure; the recorder only watches what it was given.
///
/// **Each display records afresh.** Displaying the handle again starts a new recording, and a
/// message from an earlier display of it is stepped but not noted, so two cells showing one
/// handle cannot interleave into one log.
[<Sealed; AllowNullLiteral>]
type Recorder internal (live: Live) =
    let gate = obj ()
    let events = ResizeArray<float<s> * LiveMsg>()
    let mutable session = 0
    let mutable stopped = false

    let snapshot () = Recording.ofEvents (events.ToArray())

    /// The live figure being recorded.
    member _.Live: Live = live

    /// Host plumbing: a display began. Clears the log and returns the session its messages
    /// are noted under.
    member _.Begin() : int =
        lock gate (fun () ->
            session <- session + 1
            events.Clear()
            stopped <- false
            session)

    /// Host plumbing: a message the step was given, with the view's time if it sent one. A
    /// message without one is held at the time before it (`Recording.ofEvents`).
    ///
    /// The pointer's comings and goings are not noted. They move the readout and nothing a film
    /// draws, and a reader who reads values off a figure for a minute sends a few thousand of
    /// them; a log of them would be mostly the pointer.
    member _.Note(from: int, at: float<s> option, msg: LiveMsg) : unit =
        match msg with
        | Hover _
        | Leave -> ()
        | _ ->
            lock gate (fun () ->
                if from = session && not stopped then
                    events.Add(defaultArg at (nan * 1.0<s>), msg))

    /// The log so far, without stopping.
    member _.Log: Recording<LiveMsg> = lock gate snapshot

    /// The log, and nothing more is noted until the handle is displayed again. The widget keeps
    /// working.
    member _.Stop() : Recording<LiveMsg> =
        lock gate (fun () ->
            stopped <- true
            snapshot ())

/// Host plumbing: the state a host holds for a displayed `Recorder` — the widget's state, and the
/// display it belongs to.
[<NoComparison>]
type RecorderState =
    { Session: int
      State: LiveState }

[<RequireQualifiedAccess>]
module Recorder =

    /// The log so far; the recording carries on.
    let log (r: Recorder) : Recording<LiveMsg> = r.Log

    /// The log, and the recording stops. `Live.record r.Live` replays it.
    let stop (r: Recorder) : Recording<LiveMsg> = r.Stop()

[<RequireQualifiedAccess>]
module Live =

    /// The size a widget draws at before its view has measured the frame: 8:5, as
    /// `Html.defaultSize`.
    let DefaultSize: Extent<px> = { Width = 640.0<px>; Height = 400.0<px> }

    /// The most states the union is taken over before the extremes stop being combined (D106).
    [<Literal>]
    let MaxExtremes = 64

    /// The widget draws SVG, and every SVG Marey writes is measured by the zero-dependency
    /// default. One measurer for the whole widget, D77's rule.
    let private measurer = TextMeasure.approx

    /// A live figure from a control over figures.
    let ofControl (c: Control<Figure>) : Live = { Controls = c.Specs; View = c.Read }

    /// A handle that displays as this live figure and keeps the reader's messages, for
    /// `Recorder.stop` to hand back (D102).
    let recorder (live: Live) : Recorder = Recorder live

    /// The figure for a set of values.
    let private figureOf (live: Live) (values: float[]) = live.View(Array.copy values)

    /// The shortest side, in px, a drag needs before it zooms that axis. A drag across the panel
    /// that wanders a few px vertically zooms x alone; one shorter both ways is a click.
    [<Literal>]
    let MinDrag = 6.0

    let private finite (x: float) = not (Double.IsNaN x || Double.IsInfinity x)

    let private axisOf (axis: ZoomAxis) (sx: IScaleErased, sy: IScaleErased) =
        match axis with
        | ZoomAxis.X -> sx
        | ZoomAxis.Y -> sy

    /// **D105 — a zoom applied to a figure.** Each entry states its axis on the panel it was made
    /// on, and on every panel whose same axis the view drew identically: the same domain in the
    /// same unit. Facet panels share their axes by being built with equal domains (D48), and a
    /// facet zoomed in one panel and not in its neighbours has stopped being a facet; a `beside`
    /// whose two panels happen to agree is linked the same way, which is the reading the agreement
    /// invites. Later entries win where two reach the same axis.
    let private zoomed (zoom: LiveZoom[]) (fig: Figure) : Figure =
        if zoom.Length = 0 then
            fig
        else
            // The axes as the view drew them: which panels agree is a property of the view's
            // figure, not of whatever an earlier entry already did to it.
            let axes =
                fig.Panels
                |> Array.choose (fun p ->
                    if p.Layers.Length = 0 then
                        None
                    else
                        Some((p.Row, p.Col), Figure.axisScalesErased (Figure.unify p)))

            let same (a: IScaleErased) (b: IScaleErased) =
                a.Domain = b.Domain && a.Dim = b.Dim && a.ToSI = b.ToSI && a.OffsetSI = b.OffsetSI

            zoom
            |> Array.fold
                (fun fig z ->
                    match axes |> Array.tryFind (fun (k, _) -> k = (z.Row, z.Col)) with
                    | None -> fig
                    | Some(_, target) ->
                        let t = axisOf z.Axis target
                        let d = Some(z.Lo, z.Hi)

                        axes
                        |> Array.fold
                            (fun fig (k, scales) ->
                                if same t (axisOf z.Axis scales) then
                                    match z.Axis with
                                    | ZoomAxis.X -> Chart.withPanelDomains k d None fig
                                    | ZoomAxis.Y -> Chart.withPanelDomains k None d fig
                                else
                                    fig)
                            fig)
                fig

    /// The figure for a set of values under a zoom.
    let private figureIn (live: Live) (zoom: LiveZoom[]) (values: float[]) = zoomed zoom (figureOf live values)

    /// **D106 — the states the controls can reach at their extremes**, the initial state first: a
    /// slider's ends, every option of a select, both states of a checkbox, and a time control's
    /// samples along its range (D137). Every combination
    /// while there are at most `MaxExtremes` of them; past that, one control at a time with the
    /// others at their initial values, which keeps the count linear in the controls.
    let extremes (live: Live) : float[][] =
        let init = live.Controls |> Array.map (fun c -> c.Init)

        let ends (c: ControlSpec) =
            match c.Kind with
            | Slider(lo, hi, _) -> [| lo; hi |]
            | Select options -> Array.init options.Length float
            | Checkbox -> [| 0.0; 1.0 |]
            // D137 — a time control is played, so the states it passes through are arranged
            // for, not only its ends. Over `MaxExtremes`, the fallback below takes one control
            // at a time, which keeps all of these.
            | Time(lo, hi, samples, _) -> Array.init samples (fun k -> if k = samples - 1 then hi else lo + (hi - lo) * float k / float (samples - 1))

        let choices = live.Controls |> Array.map ends
        let product = choices |> Array.fold (fun n c -> if n > MaxExtremes then n else n * c.Length) 1

        let combos =
            if product <= MaxExtremes then
                choices
                |> Array.fold
                    (fun acc options -> [| for prefix in acc do for o in options -> Array.append prefix [| o |] |])
                    [| [||] |]
            else
                [| for i in 0 .. choices.Length - 1 do
                       for o in choices.[i] ->
                           let v = Array.copy init
                           v.[i] <- o
                           v |]

        Array.append [| init |] combos |> Array.distinct

    /// The arrangement over the extremes and whatever has grown it, at a size, every state under
    /// the same zoom. A zoomed axis therefore has one domain across the whole union, and D73 pins
    /// its prefix from that domain rather than from the view's own.
    let private arrange (live: Live) (size: Extent<px>) (grown: float[][]) (zoom: LiveZoom[]) : Shot =
        Array.append (extremes live) grown
        |> Array.map (figureIn live zoom)
        |> Chart.arrangeOver measurer size

    /// Every control at its initial value, at a stated size.
    let initAt (size: Extent<px>) (live: Live) : LiveState =
        { Values = live.Controls |> Array.map (fun c -> c.Init)
          Size = size
          Shot = arrange live size [||] [||]
          Grown = [||]
          Zoom = [||]
          Pointer = None }

    /// Every control at its initial value, at `DefaultSize`.
    let init (live: Live) : LiveState = initAt DefaultSize live

    /// The figure for a state: the view's, under the state's zoom.
    let view (live: Live) (state: LiveState) : Figure = figureIn live state.Zoom state.Values

    /// Every drawn panel of a state, where it is on the widget's surface, with the scales a
    /// pointer is read back through.
    let panels (live: Live) (state: LiveState) : PanelView[] =
        Chart.panelsIn measurer state.Shot (view live state)

    /// The scene for a state, drawn in the state's arrangement.
    let scene (live: Live) (state: LiveState) : Node =
        Chart.toSceneIn measurer state.Shot (view live state)

    /// **Do two states draw the same figure?** True when they differ in nothing a figure is made
    /// from, which leaves the pointer. Decided by reference, so it costs nothing and never claims
    /// more than it knows: every step that changes the figure builds a new `Values`, `Zoom` or
    /// `Shot`, and a `false` for two states that happen to agree costs a render, not a wrong
    /// answer. A host answers a hover with the figure it already sent.
    let drawsAlike (a: LiveState) (b: LiveState) : bool =
        obj.ReferenceEquals(a.Values, b.Values)
        && obj.ReferenceEquals(a.Zoom, b.Zoom)
        && obj.ReferenceEquals(a.Shot, b.Shot)
        && a.Size = b.Size

    /// **D105 — what the pointer reads**: the data under it on the panel it is over, written by
    /// the kernel in the axes' own prefixes and units (`PanelView.read`), and the corner it is
    /// shown in. `None` when the pointer is off the figure or between plots.
    ///
    /// Read from the state, so a slider moved under a still pointer reads the new value under
    /// it, and a zoom reads what the pointer is over once the axes have moved.
    let readout (live: Live) (state: LiveState) : LiveReadout option =
        match state.Pointer with
        | None -> None
        | Some at ->
            let fig = view live state

            Chart.panelsIn measurer state.Shot fig
            |> Array.tryFind (PanelView.contains at)
            |> Option.bind (fun v ->
                PanelView.read at v
                |> Option.map (fun lines ->
                    let legendLeft =
                        fig.Panels
                        |> Array.exists (fun p -> p.Row = v.Row && p.Col = v.Col && p.Legend = LegendTopLeft)

                    { Row = v.Row
                      Col = v.Col
                      Plot = v.Plot
                      Right = legendLeft
                      Lines = lines }))

    /// Each control's value as the reader reads it, in `Controls` order.
    let shown (live: Live) (state: LiveState) : string[] =
        Array.map2 (fun (c: ControlSpec) v -> c.Show v) live.Controls state.Values

    /// The smallest and largest surface a widget draws at. Below the first a figure is a smudge
    /// with axis labels, `Html.extentForHost`'s reason; the second bounds what a view can ask
    /// the kernel to lay out.
    let private sizeOf (width: float) (height: float) : Extent<px> option =
        let ok (x: float) = not (Double.IsNaN x || Double.IsInfinity x) && x > 0.0

        if ok width && ok height then
            Some
                { Width = max 240.0 (min 2400.0 width) * 1.0<px>
                  Height = max 150.0 (min 2400.0 height) * 1.0<px> }
        else
            None

    /// The state at new values and zoom, in the old arrangement if the figure fits it (`Chart.
    /// fitsIn`) and otherwise in one grown to hold it: the values join `Grown` and the union is
    /// taken again.
    let private fitOrGrow (live: Live) (state: LiveState) (values: float[]) (zoom: LiveZoom[]) : LiveState =
        if Chart.fitsIn measurer state.Shot (figureIn live zoom values) then
            { state with Values = values; Zoom = zoom }
        else
            let grown = Array.append state.Grown [| values |]

            { state with
                Values = values
                Zoom = zoom
                Shot = arrange live state.Size grown zoom
                Grown = grown }

    /// A zoom with new domains for some axes of one panel, each replacing that panel's earlier
    /// entry for the axis. A categorical axis has no domain to state (`Scale.withDomain`), and a
    /// domain with no finite width, or narrower than nine significant digits can label, is not
    /// taken. `None` when no axis is left.
    let private withEntries (zoom: LiveZoom[]) (v: PanelView) (entries: (ZoomAxis * (float * float)) list) =
        let usable (axis: ZoomAxis) (lo: float, hi: float) =
            match (axisOf axis (v.X, v.Y)).Kind with
            | ScaleKind.Ordinal _ -> false
            | _ -> finite lo && finite hi && hi - lo > 1e-9 * max (abs lo) (abs hi)

        match entries |> List.filter (fun (axis, d) -> usable axis d) with
        | [] -> None
        | kept ->
            kept
            |> List.fold
                (fun (z: LiveZoom[]) (axis, (lo, hi)) ->
                    Array.append
                        (z |> Array.filter (fun e -> not (e.Row = v.Row && e.Col = v.Col && e.Axis = axis)))
                        [| { Row = v.Row
                             Col = v.Col
                             Axis = axis
                             Lo = lo
                             Hi = hi } |])
                zoom
            |> Some

    /// The next state. Total: an index that names no control, a value with no nearest legal one
    /// or a size that is not one leaves the state as it was. The view sent it, but the view is a
    /// page in someone's browser, and a step that can throw is a widget that can die.
    ///
    /// A new value that does not fit the arrangement (`Chart.fitsIn`) grows it: the values join
    /// `Grown` and the union is taken again. A new size re-arranges over the same states. A zoom
    /// or a reset re-arranges; a pan grows the arrangement as a value does. A drag that starts
    /// outside every plot area, or a pan off the end of a coord, leaves the state as it was.
    ///
    /// A hover moves the pointer and nothing else, so its state draws alike (`drawsAlike`). A new
    /// size forgets the pointer: the px it was at are the old arrangement's, and the view sends
    /// the next one as soon as the pointer moves.
    let step (live: Live) (state: LiveState) (msg: LiveMsg) : LiveState =
        match msg with
        | Set(index, value) when index >= 0 && index < live.Controls.Length ->
            match ControlSpec.normalise live.Controls.[index] value with
            | Some v when v <> state.Values.[index] ->
                let values = Array.copy state.Values
                values.[index] <- v

                fitOrGrow live state values state.Zoom
            | _ -> state
        | Set _ -> state
        | Resize(width, height) ->
            match sizeOf width height with
            | Some size when size <> state.Size ->
                { state with
                    Size = size
                    Shot = arrange live size state.Grown state.Zoom
                    Pointer = None }
            | _ -> state
        | Zoom r ->
            let start: Vec2<px> = { X = r.X0; Y = r.Y0 }

            match panels live state |> Array.tryFind (PanelView.contains start) with
            | None -> state
            | Some v ->
                // The drag as a rectangle, kept inside the panel it started on: a drag that runs
                // off the plot zooms to the plot's edge rather than past it.
                let clamp (lo: float<px>) (hi: float<px>) (x: float<px>) = max lo (min hi x)
                let x0, x1 = clamp v.Plot.X0 v.Plot.X1 (min r.X0 r.X1), clamp v.Plot.X0 v.Plot.X1 (max r.X0 r.X1)
                let y0, y1 = clamp v.Plot.Y0 v.Plot.Y1 (min r.Y0 r.Y1), clamp v.Plot.Y0 v.Plot.Y1 (max r.Y0 r.Y1)

                match PanelView.domainsOf { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1 } v with
                | None -> state
                | Some(dx, dy) ->
                    let entries =
                        [ if x1 - x0 >= MinDrag * 1.0<px> then ZoomAxis.X, dx
                          if y1 - y0 >= MinDrag * 1.0<px> then ZoomAxis.Y, dy ]

                    match withEntries state.Zoom v entries with
                    | Some zoom ->
                        // A zoom is a jump the reader asked for, and it can change an axis's
                        // prefix (D73), so it re-arranges rather than waiting to overflow.
                        { state with
                            Zoom = zoom
                            Shot = arrange live state.Size state.Grown zoom }
                    | None -> state
        | Pan(at, by) ->
            match panels live state |> Array.tryFind (PanelView.contains at) with
            | None -> state
            | Some v ->
                match PanelView.toUnit at v, PanelView.toUnit (at + by) v with
                | Some a, Some b ->
                    // What was under `a` comes to rest under `b`: the unit square slides by
                    // `a - b`, and its ends are read back through each scale, so a log axis pans
                    // by a factor rather than by a difference.
                    let slide (s: IScaleErased) (d: float<nd>) =
                        let lo, hi = s.Invert(0.0<nd> - d), s.Invert(1.0<nd> - d)
                        min lo hi, max lo hi

                    let entries =
                        [ if b.X <> a.X then ZoomAxis.X, slide v.X (b.X - a.X)
                          if b.Y <> a.Y then ZoomAxis.Y, slide v.Y (b.Y - a.Y) ]

                    match withEntries state.Zoom v entries with
                    // A pan is continuous, so it keeps the arrangement until a label overflows it.
                    | Some zoom -> fitOrGrow live state state.Values zoom
                    | None -> state
                | _ -> state
        | Reset ->
            if state.Zoom.Length = 0 then
                state
            else
                { state with
                    Zoom = [||]
                    Shot = arrange live state.Size state.Grown [||] }
        | Hover at when finite (float at.X) && finite (float at.Y) ->
            if state.Pointer = Some at then state else { state with Pointer = Some at }
        | Hover _ -> state
        | Leave -> if state.Pointer.IsNone then state else { state with Pointer = None }

    /// **A session again, from its log (D102)**: the state after each message, at the time the
    /// view stamped it, from `start`. Every time is the view's, so the kernel's clock appears
    /// nowhere. A log with no messages records `start` alone, so the result can always be
    /// sampled.
    ///
    /// **Time starts at the reader's first act**, not at the view's hello. A view sends its size
    /// when it mounts, and the reader may not touch it for a minute; the sizes before the first
    /// other message are stepped at time zero, so a film does not open on that minute. A pointer
    /// passing over the figure is not an act either: it changes nothing a film draws.
    let recordFrom (live: Live) (start: LiveState) (log: Recording<LiveMsg>) : Recording<LiveState> =
        if log.Events.Length = 0 then
            { Events = [| 0.0<s>, start |] }
        else
            let origin =
                log.Events
                |> Array.tryPick (fun (t, msg) ->
                    match msg with
                    | Resize _
                    | Hover _
                    | Leave -> None
                    | _ -> Some t)

            let mutable state = start

            log.Events
            |> Array.map (fun (t, msg) ->
                state <- step live state msg

                match origin with
                | Some o when t < o -> o, state
                | _ -> t, state)
            |> Recording.ofEvents

    /// `recordFrom` the first state at `DefaultSize`, which is where every widget starts.
    let record (live: Live) (log: Recording<LiveMsg>) : Recording<LiveState> = recordFrom live (init live) log

    /// **A recording as a film**: each state's figure, held until the next, drawn in one
    /// arrangement laid out over **every** state the recording reached rather than over samples
    /// of it, at the size the session ended at. The states are known, so D72's union is exact
    /// here: a state a sampled union would have stepped over cannot overflow. Laying out costs
    /// one figure per distinct state, which for a long drag is hundreds.
    let film (live: Live) (recording: Recording<LiveState>) : Film =
        let states = recording.Events |> Array.map snd

        if states.Length = 0 then
            invalidArg "recording" "Marey: an empty recording has nothing to film."

        let size = states.[states.Length - 1].Size

        let shot =
            states
            |> Array.distinctBy (fun st -> st.Values, st.Zoom)
            |> Array.map (view live)
            |> Chart.arrangeOver measurer size

        let figures = recording |> Recording.map (view live) |> Recording.sample

        { Shot = shot
          Frames = Anim.ofSeconds figures.Duration (fun t -> Chart.toSceneIn measurer shot (figures.At t)) }

/// The `interact { }` block. Applicative: `let! … and! …` and a `return`, never a second `let!`
/// that could depend on the first.
type InteractBuilder() =
    member _.BindReturn(c: Control<'a>, f: 'a -> 'b) : Control<'b> = Control.map f c
    member _.MergeSources(a: Control<'a>, b: Control<'b>) : Control<'a * 'b> = Control.zip a b
    member _.Return(x: 'a) : Control<'a> = Control.constant x
    member _.Run(c: Control<Figure>) : Live = Live.ofControl c

[<AutoOpen>]
module InteractBuilderInstance =
    /// A figure with controls:
    ///
    /// ```fsharp
    /// interact {
    ///     let! t = Control.slider U.kelvin "T" 200.0<K> 400.0<K> 300.0<K>
    ///     and! damped = Control.checkbox "damped" true
    ///     return view t damped
    /// }
    /// ```
    let interact = InteractBuilder()
