namespace Marey.Verso

open System
open System.Collections.Generic
open System.Reflection
open System.Threading
open System.Threading.Tasks
open Verso.Abstractions

// ======================================================================================
// M4.5 — A LIVE FIGURE IN A VERSO CELL (D99, D100).
//
// The formatter writes the page once; this keeps it in conversation with the kernel for as long
// as the session holds the channel, which is after the cell has finished — the time someone is
// actually dragging the slider. The extension holds one opaque state per channel and never looks
// inside it: `Marey.Html` steps it, renders it, and writes the page, through the bridge.
//
// **What the host decides, and what this does not.** Re-running, clearing or deleting the cell
// closes the channel, and a kernel restart closes it and tells the view (observed in M4.5's phase
// 0 spike). A save asks `SnapshotProvider` for the page at the current state, so the file holds
// what the reader was looking at, drawn static.
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

    /// A message from the view: step, then answer. **Always answered**, because the view holds
    /// one request in flight and waits for the reply before sending the next; a message that
    /// changed nothing still gets the labels back, without the figure it already has.
    member _.OnMessage(m: OutputChannelMessage) : Task =
        task {
            let before = current ()
            let after = Bridge.liveStep value before m.Type m.Payload
            lock gate (fun () -> state <- after)

            let payload = Dictionary<string, obj>()

            match Bridge.liveReply value after with
            | Some parts ->
                if not (obj.ReferenceEquals(before, after)) then
                    payload.["svg"] <- box parts.[0]

                payload.["shown"] <- box (Array.sub parts 1 (parts.Length - 1))
            | None -> ()

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
