namespace Marey.Verso

open System
open System.Reflection
open System.Runtime.Loader

// ======================================================================================
// CROSSING THE LOAD-CONTEXT BOUNDARY.
//
// A Verso extension is loaded into its own collectible `AssemblyLoadContext`, and only
// `Verso.Abstractions` is shared from the default context. Marey arrives by a completely
// different route: the notebook's `#r "nuget: Marey.Html"`, into the F# kernel's context.
//
// So the two halves cannot share a type. If this assembly referenced `Marey.Plot`, it would load
// its *own* copy, `typeof<Figure>` here would not equal the `Figure` the user just built, the
// `SupportedTypes` pre-filter would never match, and a cast would throw if it did. That is the
// "Type cast failures" row in Verso's own troubleshooting table, arrived at from a different
// direction.
//
// The way through is to touch the value only through reflection on **its own** type, which needs
// no shared identity. That turns a dependency into a lookup, and the lookup is what this file is.
// The cost is a contract the compiler cannot check — two function signatures in `Marey.Html` —
// so it is written down here, pinned by a test on the Marey side, and probed defensively.
//
// One deliberate non-goal: this is not a general "render anything with a ToHtml() method"
// formatter. Verso already ships one of those, and a second would be a worse copy. This claims
// Marey values specifically, at a priority that settles the tie described in `Formatter.fs`.
// ======================================================================================

/// Locates `Marey.Html` in whatever load context the value came from, and calls it.
module internal Bridge =

    [<Literal>]
    let RendererAssembly = "Marey.Html"

    [<Literal>]
    let RendererType = "Marey.Html"

    [<Literal>]
    let CanRender = "canRenderForHost"

    [<Literal>]
    let Render = "renderForHost"

    /// Step F — a page for a frame instead of markup, for a figure drawn on a canvas.
    /// Optional: a `Marey.Html` without it is always rendered as markup.
    [<Literal>]
    let Document = "documentForHost"

    // M4.5 — the live half. Four more signatures, optional as a set: a `Marey.Html` from before
    // 0.5 has none of them, and then a live figure is drawn as a figure and nothing is opened.
    [<Literal>]
    let LiveInit = "liveInitForHost"

    [<Literal>]
    let LiveStep = "liveStepForHost"

    [<Literal>]
    let LiveDocument = "liveDocumentForHost"

    [<Literal>]
    let LiveReply = "liveReplyForHost"

    // Optional on its own: without it every step is answered with the whole figure.
    [<Literal>]
    let LiveAnswer = "liveAnswerForHost"

    // Optional on its own: without it the view shows no readout.
    [<Literal>]
    let LiveReadout = "liveReadoutForHost"

    /// Step F — the whole figure at a state, in the page's kind (SVG or canvas bytes). Optional,
    /// like `LiveAnswer`: a `Marey.Html` without it is answered with `LiveReply`'s SVG.
    [<Literal>]
    let LiveWhole = "liveWholeForHost"

    /// Assemblies named `Marey.*`. The cheapest possible rejection, and the one that runs on
    /// every value a cell produces: `CanFormat` is a probe, not a commitment.
    let isMareyValue (value: obj) =
        match value with
        | null -> false
        | v ->
            match v.GetType().Assembly.GetName().Name with
            | null -> false
            | name -> name = "Marey" || name.StartsWith("Marey.", StringComparison.Ordinal)

    /// Both bridge methods, resolved together, cached per resolved type.
    ///
    /// Keyed on the `Marey.Html` type rather than on a bare flag: a host can hold several load
    /// contexts at once — two notebooks, or one notebook restarted — and a `MethodInfo` from a
    /// unloaded context is a leak at best and a stale invoke at worst.
    let private cache =
        System.Collections.Concurrent.ConcurrentDictionary<Type, (MethodInfo * MethodInfo) option>()

    let private findRendererType (value: obj) : Type option =
        let valueAssembly = value.GetType().Assembly

        let context =
            match AssemblyLoadContext.GetLoadContext valueAssembly with
            | null -> AssemblyLoadContext.Default
            | c -> c

        // Already-loaded first. `Marey.Html` is present exactly when the notebook
        // referenced it, and its absence is a legitimate answer: the reader pulled
        // `Marey.Plot` alone, there is nothing here that can render a figure, and the F#
        // kernel's own field table is then the correct output rather than a failure.
        let loaded =
            context.Assemblies
            |> Seq.tryFind (fun a -> a.GetName().Name = RendererAssembly)

        let assembly =
            match loaded with
            | Some a -> Some a
            | None ->
                // A load is still worth attempting: an assembly that has been resolved but not
                // yet touched does not appear above, and this resolves it in the *value's*
                // context rather than ours, which is the whole point.
                try
                    match context.LoadFromAssemblyName(AssemblyName RendererAssembly) with
                    | null -> None
                    | a -> Some a
                with _ ->
                    None

        assembly
        |> Option.bind (fun a ->
            match a.GetType RendererType with
            | null -> None
            | t -> Some t)

    let private methodsFor (t: Type) =
        cache.GetOrAdd(
            t,
            fun t ->
                let canRender =
                    t.GetMethod(CanRender, BindingFlags.Public ||| BindingFlags.Static, null, [| typeof<obj> |], null)

                let render =
                    t.GetMethod(
                        Render,
                        BindingFlags.Public ||| BindingFlags.Static,
                        null,
                        [| typeof<float>; typeof<float>; typeof<obj> |],
                        null
                    )

                if isNull canRender || isNull render then
                    None
                else
                    Some(canRender, render)
        )

    let private resolve (value: obj) =
        findRendererType value |> Option.bind methodsFor

    /// Can Marey render this value?
    ///
    /// Every failure path answers `false`, including the reflective ones. A formatter's probe
    /// runs against every value in the notebook, most of which have nothing to do with Marey,
    /// and the correct behaviour when this bridge cannot be established is for the kernel's own
    /// formatting to proceed untouched.
    let canRender (value: obj) : bool =
        if not (isMareyValue value) then
            false
        else
            match resolve value with
            | None -> false
            | Some(canRenderMethod, _) ->
                try
                    match canRenderMethod.Invoke(null, [| value |]) with
                    | :? bool as b -> b
                    | _ -> false
                with _ ->
                    false

    /// Render, or `None` if anything at all goes wrong.
    ///
    /// The page `Marey.Html.documentForHost` writes for a value it draws on a canvas, or `None`:
    /// for a value drawn as markup, and for a `Marey.Html` without the method.
    let document (maxWidth: float) (maxHeight: float) (value: obj) : string option =
        if not (isMareyValue value) then
            None
        else
            findRendererType value
            |> Option.bind (fun t ->
                match t.GetMethod(Document, BindingFlags.Public ||| BindingFlags.Static, null, [| typeof<float>; typeof<float>; typeof<obj> |], null) with
                | null -> None
                | m ->
                    try
                        match m.Invoke(null, [| box maxWidth; box maxHeight; value |]) with
                        | :? string as doc when doc <> "" -> Some doc
                        | _ -> None
                    with _ ->
                        None)

    /// `renderForHost` returns `""` for a value it does not know; that is folded into `None` here
    /// so the caller has one shape to handle. `Html` already guarantees the render itself cannot
    /// throw — a display formatter that throws replaces a good error message with a bad one — so
    /// the `try` is guarding the reflection, not the drawing.
    let render (maxWidth: float) (maxHeight: float) (value: obj) : string option =
        match resolve value with
        | None -> None
        | Some(_, renderMethod) ->
            try
                match renderMethod.Invoke(null, [| box maxWidth; box maxHeight; value |]) with
                | :? string as html when html <> "" -> Some html
                | _ -> None
            with _ ->
                None

    // ---- The live half (M4.5, D100) -----------------------------------------------------------

    type private LiveMethods =
        { Init: MethodInfo
          Step: MethodInfo
          Document: MethodInfo
          Reply: MethodInfo
          Answer: MethodInfo option
          Readout: MethodInfo option
          Whole: MethodInfo option }

    let private liveCache =
        System.Collections.Concurrent.ConcurrentDictionary<Type, LiveMethods option>()

    let private liveMethodsFor (t: Type) =
        liveCache.GetOrAdd(
            t,
            fun t ->
                let find name (args: Type[]) =
                    t.GetMethod(name, BindingFlags.Public ||| BindingFlags.Static, null, args, null)

                let init = find LiveInit [| typeof<obj> |]
                let step = find LiveStep [| typeof<obj>; typeof<obj>; typeof<string>; typeof<string> |]
                let document = find LiveDocument [| typeof<obj>; typeof<obj>; typeof<bool> |]
                let reply = find LiveReply [| typeof<obj>; typeof<obj> |]
                let answer = find LiveAnswer [| typeof<obj>; typeof<obj>; typeof<obj> |]
                let readout = find LiveReadout [| typeof<obj>; typeof<obj> |]
                let whole = find LiveWhole [| typeof<obj>; typeof<obj> |]

                if isNull init || isNull step || isNull document || isNull reply then
                    None
                else
                    Some
                        { Init = init
                          Step = step
                          Document = document
                          Reply = reply
                          Answer = Option.ofObj answer
                          Readout = Option.ofObj readout
                          Whole = Option.ofObj whole }
        )

    let private live (value: obj) =
        if isMareyValue value then
            findRendererType value |> Option.bind liveMethodsFor
        else
            None

    let private invoke (m: MethodInfo) (args: obj[]) =
        try
            Some(m.Invoke(null, args))
        with _ ->
            None

    /// The initial state of a live figure, held by the caller as an opaque `obj`; `None` for any
    /// other value, and for a `Marey.Html` that predates live figures.
    let liveInit (value: obj) : obj option =
        live value
        |> Option.bind (fun ms -> invoke ms.Init [| value |])
        |> Option.bind (fun s -> if isNull s then None else Some s)

    /// The state after a message. The state it was given when anything goes wrong.
    let liveStep (value: obj) (state: obj) (msgType: string) (payload: string) : obj =
        match live value |> Option.bind (fun ms -> invoke ms.Step [| value; state; box msgType; box payload |]) with
        | Some next when not (isNull next) -> next
        | _ -> state

    /// The page at a state, live or static.
    let liveDocument (value: obj) (state: obj) (isLive: bool) : string option =
        match live value |> Option.bind (fun ms -> invoke ms.Document [| value; state; box isLive |]) with
        | Some(:? string as doc) when doc <> "" -> Some doc
        | _ -> None

    /// What the view swaps in after a step: the figure's SVG, then each control's label.
    let liveReply (value: obj) (state: obj) : string[] option =
        match live value |> Option.bind (fun ms -> invoke ms.Reply [| value; state |]) with
        | Some(:? (string[]) as parts) when parts.Length > 0 -> Some parts
        | _ -> None

    /// The answer to one step, as `Marey.Html.liveAnswerForHost` lays it out: a kind (`same`,
    /// `svg` or `patch`), what it carries, then the labels. `None` for a `Marey.Html` without it.
    let liveAnswer (value: obj) (before: obj) (after: obj) : string[] option =
        match
            live value
            |> Option.bind (fun ms -> ms.Answer)
            |> Option.bind (fun m -> invoke m [| value; before; after |])
        with
        | Some(:? (string[]) as parts) when parts.Length > 0 -> Some parts
        | _ -> None

    /// The whole figure at a state, as `Marey.Html.liveWholeForHost` lays it out: a kind (`svg` or
    /// `scene`), what it carries, then the labels. `None` for a `Marey.Html` without it.
    let liveWhole (value: obj) (state: obj) : string[] option =
        match
            live value
            |> Option.bind (fun ms -> ms.Whole)
            |> Option.bind (fun m -> invoke m [| value; state |])
        with
        | Some(:? (string[]) as parts) when parts.Length > 0 -> Some parts
        | _ -> None

    /// What the pointer reads, as `Marey.Html.liveReadoutForHost` lays it out: the plot it is
    /// over, the corner, and the lines. `Some [||]` when it reads nothing, and `None` for a
    /// `Marey.Html` without it.
    let liveReadout (value: obj) (state: obj) : string[] option =
        match
            live value
            |> Option.bind (fun ms -> ms.Readout)
            |> Option.bind (fun m -> invoke m [| value; state |])
        with
        | Some(:? (string[]) as parts) -> Some parts
        | _ -> None
