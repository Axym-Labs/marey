namespace Marey.Verso

open System
open System.Collections.Generic
open System.Reflection
open System.Threading
open System.Threading.Tasks
open Verso.Abstractions

// ======================================================================================
// A LIVE FIGURE IN A VERSO CELL.
//
// The formatter writes the page once; this keeps it in conversation with the kernel for as long
// as the session holds the channel, which is after the cell has finished — the time someone is
// actually dragging the slider. The extension holds one opaque state per channel and never looks
// inside it: `Marey.Html` steps it, renders it, and writes the page, through the bridge.
//
// **What the host decides, and what this does not.** Re-running, clearing or deleting the cell
// closes the channel, and a kernel restart closes it and tells the view (observed). A save asks
// `SnapshotProvider` for the page at the current state, so the file holds what the reader was
// looking at, drawn static.
//
// **The cell id is read by reflection.** A formatter learns its cell from `IFormatterContext.
// CellId`, which a Verso with the formatter-context change carries and Verso 1.2 does not. This
// assembly compiles against 1.2, so it asks the interface the *host* loaded for the property,
// then the context's own type, and treats an absent property exactly as an absent channel host:
// the page is written static, and nothing is opened.
// ======================================================================================

/// One live figure's channel, and the state it is at.
[<Sealed>]
type internal LiveSession(value: obj, initial: obj, channel: IOutputChannel) =

    let gate = obj ()
    let mutable state = initial

    let current () = lock gate (fun () -> state)

    /// Step, and build the answer. Outside the `task` so that the state machine stays
    /// statically compilable (FS3511): nothing here awaits.
    let respond (m: OutputChannelMessage) : Dictionary<string, obj> =
        let before = current ()
        let after = Bridge.liveStep value before m.Type m.Payload
        lock gate (fun () -> state <- after)

        let payload = Dictionary<string, obj>()

        // An answer laid out as `Marey.Html` lays them out: a kind, what it carries, the labels.
        // False for a layout this build does not know.
        let put (parts: string[]) : bool =
            let rest (from: int) = box (Array.sub parts from (parts.Length - from))

            match parts.[0] with
            | "svg" when parts.Length >= 2 ->
                payload.["svg"] <- box parts.[1]
                payload.["shown"] <- rest 2
                true
            // Step F: a canvas page's whole scene, its size and its bytes, and a patch's bytes.
            | "scene" when parts.Length >= 4 ->
                payload.["w"] <- box parts.[1]
                payload.["h"] <- box parts.[2]
                payload.["scene"] <- box parts.[3]
                payload.["shown"] <- rest 4
                true
            | "wpatch" when parts.Length >= 2 ->
                payload.["wpatch"] <- box parts.[1]
                payload.["shown"] <- rest 2
                true
            | "same" ->
                payload.["shown"] <- rest 1
                true
            | _ -> false

        let whole (always: bool) =
            let changed = always || not (obj.ReferenceEquals(before, after))

            match (if changed then Bridge.liveWhole value after else None) with
            | Some parts when put parts -> ()
            | _ ->
            match Bridge.liveReply value after with
            | Some parts ->
                if always || not (obj.ReferenceEquals(before, after)) then
                    payload.["svg"] <- box parts.[0]

                payload.["shown"] <- box (Array.sub parts 1 (parts.Length - 1))
            | None -> ()

        // `size` is the view's hello, sent when it mounts and again when it cannot
        // apply a patch, so it is always answered with the whole figure: whatever the view
        // is showing, it is not something a patch can be taken against.
        let answer =
            if m.Type = "size" then None else Bridge.liveAnswer value before after

        match answer with
        | Some parts ->
            let rest (from: int) = box (Array.sub parts from (parts.Length - from))

            match parts.[0] with
            | "patch" when parts.Length >= 3 ->
                match Int32.TryParse parts.[2] with
                | true, n when n >= 0 && parts.Length >= 3 + 2 * n ->
                    payload.["defs"] <- box parts.[1]
                    payload.["patch"] <- box (Array.sub parts 3 (2 * n))
                    payload.["shown"] <- rest (3 + 2 * n)
                | _ -> whole true
            | _ -> if not (put parts) then whole true
        | None -> whole (m.Type = "size")

        // Every answer says what the pointer reads, an empty one included, so a
        // readout the pointer has left is taken down by the reply that says so.
        match Bridge.liveReadout value after with
        | Some parts -> payload.["readout"] <- box parts
        | None -> ()

        payload

    /// A message from the view: step, then answer. **Always answered**, because the view holds
    /// one request in flight and waits for the reply before sending the next; a message that
    /// changed nothing still gets the labels back, without the figure it already has.
    member _.OnMessage(m: OutputChannelMessage) : Task =
        task {
            let payload = respond m

            // A dead channel throws rather than drop the message. The view has gone, and so has
            // anyone to tell.
            if channel.IsAlive then
                try
                    do! channel.PostMessageAsync("render", payload, CancellationToken.None)
                with _ ->
                    ()
        }
        :> Task

    /// The page at the current state, drawn static, for a save.
    member _.Snapshot(_: CancellationToken) : Task<string> =
        Task.FromResult(
            match Bridge.liveDocument value (current ()) false with
            | Some doc -> doc
            | None -> null
        )

module internal Live =

    /// The cell a formatter is formatting for, if the host says.
    let cellIdOf (context: IFormatterContext) : Guid option =
        let read (p: PropertyInfo) =
            match p with
            | null -> None
            | p ->
                try
                    match p.GetValue(context) with
                    | :? Guid as g -> Some g
                    | _ -> None
                with _ ->
                    None

        match read (typeof<IFormatterContext>.GetProperty "CellId") with
        | Some g -> Some g
        | None -> read (context.GetType().GetProperty "CellId")

    /// The output for a live figure: a live page over a new channel when the host can reach the
    /// view and names the cell, and the static page otherwise. `None` when the value is not live.
    let format (value: obj) (context: IFormatterContext) : Task<CellOutput> option =
        Bridge.liveInit value
        |> Option.map (fun state ->
            let staticPage () =
                match Bridge.liveDocument value state false with
                | Some doc -> CellOutput.Widget doc
                | None -> CellOutput.Html ""

            match context.OutputChannels, cellIdOf context with
            | null, _
            | _, None -> Task.FromResult(staticPage ())
            | channels, Some cell ->
                task {
                    let! opened =
                        task {
                            try
                                let! channel = channels.OpenAsync(cell, CancellationToken.None)
                                return Some channel
                            with _ ->
                                return None
                        }

                    match opened, Bridge.liveDocument value state true with
                    | Some channel, Some doc ->
                        let session = LiveSession(value, state, channel)
                        channel.add_MessageReceived (Func<OutputChannelMessage, Task>(session.OnMessage))
                        channel.SnapshotProvider <- Func<CancellationToken, Task<string>>(session.Snapshot)
                        return CellOutput.Widget(doc, channel.ChannelId)
                    | Some channel, None ->
                        do! channel.DisposeAsync()
                        return staticPage ()
                    | None, _ -> return staticPage ()
                })
