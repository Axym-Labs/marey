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
      Grown: float[][] }

/// What the reader can do to a live figure.
type LiveMsg =
    /// Set control `index` to `value` (its unit's value, an option's index, or 0 and 1).
    | Set of index: int * value: float
    /// The view measured its frame: draw at this width and height, in px.
    | Resize of width: float * height: float

[<RequireQualifiedAccess>]
module LiveMsg =

    let private invariant = CultureInfo.InvariantCulture

    /// `[a,b]` as two numbers, or nothing.
    let private pair (payload: string) =
        if isNull payload then
            None
        else
            let body = payload.Trim()

            if body.Length >= 2 && body.[0] = '[' && body.[body.Length - 1] = ']' then
                match body.Substring(1, body.Length - 2).Split(',') with
                | [| a; b |] ->
                    match Double.TryParse(a.Trim(), NumberStyles.Float, invariant),
                          Double.TryParse(b.Trim(), NumberStyles.Float, invariant) with
                    | (true, a), (true, b) -> Some(a, b)
                    | _ -> None
                | _ -> None
            else
                None

    /// Read a message from the wire: a type and its JSON payload text. `set` takes
    /// `[index,value]` and `size` takes `[width,height]`. Anything else is `None`, and a host
    /// ignores it rather than failing: a view one protocol version ahead should not take the
    /// widget down.
    let parse (msgType: string) (payload: string) : LiveMsg option =
        match msgType, pair payload with
        | "set", Some(i, v) when i = Math.Floor i && abs i < 1e9 -> Some(Set(int i, v))
        | "size", Some(w, h) -> Some(Resize(w, h))
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

    /// The arrangement over the extremes and whatever has grown it, at a size.
    let private arrange (live: Live) (size: Extent<px>) (grown: float[][]) : Shot =
        Array.append (extremes live) grown
        |> Array.map (figureOf live)
        |> Chart.arrangeOver measurer size

    /// Every control at its initial value, at a stated size.
    let initAt (size: Extent<px>) (live: Live) : LiveState =
        { Values = live.Controls |> Array.map (fun c -> c.Init)
          Size = size
          Shot = arrange live size [||]
          Grown = [||] }

    /// Every control at its initial value, at `DefaultSize`.
    let init (live: Live) : LiveState = initAt DefaultSize live

    /// The figure for a state.
    let view (live: Live) (state: LiveState) : Figure = figureOf live state.Values

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

    /// The next state. Total: an index that names no control, a value with no nearest legal one
    /// or a size that is not one leaves the state as it was. The view sent it, but the view is a
    /// page in someone's browser, and a step that can throw is a widget that can die.
    ///
    /// A new value that does not fit the arrangement (`Chart.fitsIn`) grows it: the values join
    /// `Grown` and the union is taken again. A new size re-arranges over the same states.
    let step (live: Live) (state: LiveState) (msg: LiveMsg) : LiveState =
        match msg with
        | Set(index, value) when index >= 0 && index < live.Controls.Length ->
            match ControlSpec.normalise live.Controls.[index] value with
            | Some v when v <> state.Values.[index] ->
                let values = Array.copy state.Values
                values.[index] <- v

                if Chart.fitsIn measurer state.Shot (figureOf live values) then
                    { state with Values = values }
                else
                    let grown = Array.append state.Grown [| values |]

                    { state with
                        Values = values
                        Shot = arrange live state.Size grown
                        Grown = grown }
            | _ -> state
        | Set _ -> state
        | Resize(width, height) ->
            match sizeOf width height with
            | Some size when size <> state.Size ->
                { state with
                    Size = size
                    Shot = arrange live size state.Grown }
            | _ -> state

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
