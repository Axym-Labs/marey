namespace Marey

open System
open System.Globalization

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
      Zoom: LiveZoom[] }

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

[<RequireQualifiedAccess>]
module LiveMsg =

    let private invariant = CultureInfo.InvariantCulture

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
                        |> Array.map (fun a -> Double.TryParse(a.Trim(), NumberStyles.Float, invariant))

                    if parsed |> Array.forall fst then Some(parsed |> Array.map snd) else None
            else
                None

    /// Read a message from the wire: a type and its JSON payload text. `set` takes
    /// `[index,value]`, `size` `[width,height]`, `zoom` `[x0,y0,x1,y1]`, `pan` `[x,y,dx,dy]`, and
    /// `reset` anything. Anything else is `None`, and a host ignores it rather than failing: a
    /// view one protocol version ahead should not take the widget down.
    let parse (msgType: string) (payload: string) : LiveMsg option =
        let px (v: float) = v * 1.0<px>

        match msgType, numbers payload with
        | "set", Some [| i; v |] when i = Math.Floor i && abs i < 1e9 -> Some(Set(int i, v))
        | "size", Some [| w; h |] -> Some(Resize(w, h))
        | "zoom", Some [| x0; y0; x1; y1 |] ->
            Some(Zoom { X0 = px x0; Y0 = px y0; X1 = px x1; Y1 = px y1 })
        | "pan", Some [| x; y; dx; dy |] -> Some(Pan({ X = px x; Y = px y }, { X = px dx; Y = px dy }))
        | "reset", _ -> Some Reset
        | _ -> None

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
                        Some((p.Row, p.Col), Erase.seriesScales (Figure.unify p).Layers.[0].Data))

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
    /// slider's ends, every option of a select, both states of a checkbox. Every combination
    /// while there are at most `MaxExtremes` of them; past that, one control at a time with the
    /// others at their initial values, which keeps the count linear in the controls.
    let extremes (live: Live) : float[][] =
        let init = live.Controls |> Array.map (fun c -> c.Init)

        let ends (c: ControlSpec) =
            match c.Kind with
            | Slider(lo, hi, _) -> [| lo; hi |]
            | Select options -> Array.init options.Length float
            | Checkbox -> [| 0.0; 1.0 |]

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
          Zoom = [||] }

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
                    Shot = arrange live size state.Grown state.Zoom }
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
