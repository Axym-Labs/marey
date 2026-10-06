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

/// Where a live figure is. Values are in `Live.Controls` order, each already normalised.
///
/// An array inside a record still compares structurally under F#'s `=`, so two states are equal
/// exactly when they hold the same values, which is what a replay test needs.
type LiveState = { Values: float[] }

/// What the reader can do to a live figure.
type LiveMsg =
    /// Set control `index` to `value` (its unit's value, an option's index, or 0 and 1).
    | Set of index: int * value: float

[<RequireQualifiedAccess>]
module LiveMsg =

    let private invariant = CultureInfo.InvariantCulture

    /// Read a message from the wire: a type and its JSON payload text. `set` takes `[index,value]`.
    /// Anything else is `None`, and a host ignores it rather than failing: a view one protocol
    /// version ahead should not take the widget down.
    let parse (msgType: string) (payload: string) : LiveMsg option =
        match msgType with
        | "set" when not (isNull payload) ->
            let body = payload.Trim()

            if body.Length >= 2 && body.[0] = '[' && body.[body.Length - 1] = ']' then
                match body.Substring(1, body.Length - 2).Split(',') with
                | [| i; v |] ->
                    match Int32.TryParse(i.Trim(), NumberStyles.Integer, invariant),
                          Double.TryParse(v.Trim(), NumberStyles.Float, invariant) with
                    | (true, index), (true, value) -> Some(Set(index, value))
                    | _ -> None
                | _ -> None
            else
                None
        | _ -> None

[<RequireQualifiedAccess>]
module Live =

    /// A live figure from a control over figures.
    let ofControl (c: Control<Figure>) : Live = { Controls = c.Specs; View = c.Read }

    /// Every control at its initial value.
    let init (live: Live) : LiveState =
        { Values = live.Controls |> Array.map (fun c -> c.Init) }

    /// The figure for a state.
    let view (live: Live) (state: LiveState) : Figure = live.View(Array.copy state.Values)

    /// Each control's value as the reader reads it, in `Controls` order.
    let shown (live: Live) (state: LiveState) : string[] =
        Array.map2 (fun (c: ControlSpec) v -> c.Show v) live.Controls state.Values

    /// The next state. Total: an index that names no control, or a value with no nearest legal
    /// one, leaves the state as it was. The view sent it, but the view is a page in someone's
    /// browser, and a step that can throw is a widget that can die.
    let step (live: Live) (state: LiveState) (msg: LiveMsg) : LiveState =
        match msg with
        | Set(index, value) when index >= 0 && index < live.Controls.Length ->
            match ControlSpec.normalise live.Controls.[index] value with
            | Some v when v <> state.Values.[index] ->
                let values = Array.copy state.Values
                values.[index] <- v
                { Values = values }
            | _ -> state
        | Set _ -> state

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
