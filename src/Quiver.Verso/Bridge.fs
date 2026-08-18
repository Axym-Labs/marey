namespace Quiver.Verso

open System
open System.Reflection
open System.Runtime.Loader

// ======================================================================================
// CROSSING THE LOAD-CONTEXT BOUNDARY.
//
// A Verso extension is loaded into its own collectible `AssemblyLoadContext`, and only
// `Verso.Abstractions` is shared from the default context. Quiver arrives by a completely
// different route: the notebook's `#r "nuget: Quiver.Interactive"`, into the F# kernel's context.
//
// So the two halves cannot share a type. If this assembly referenced `Quiver.Plot`, it would load
// its *own* copy, `typeof<Figure>` here would not equal the `Figure` the user just built, the
// `SupportedTypes` pre-filter would never match, and a cast would throw if it did. That is the
// "Type cast failures" row in Verso's own troubleshooting table, arrived at from a different
// direction.
//
// The way through is to touch the value only through reflection on **its own** type, which needs
// no shared identity. That turns a dependency into a lookup, and the lookup is what this file is.
// The cost is a contract the compiler cannot check — two function signatures in `Quiver.Html` —
// so it is written down here, pinned by a test on the Quiver side, and probed defensively.
//
// One deliberate non-goal: this is not a general "render anything with a ToHtml() method"
// formatter. Verso already ships one of those, and a second would be a worse copy. This claims
// Quiver values specifically, at a priority that settles the tie described in `Formatter.fs`.
// ======================================================================================

/// Locates `Quiver.Html` in whatever load context the value came from, and calls it.
module internal Bridge =

    [<Literal>]
    let RendererAssembly = "Quiver.Interactive"

    [<Literal>]
    let RendererType = "Quiver.Html"

    [<Literal>]
    let CanRender = "canRenderForHost"

    [<Literal>]
    let Render = "renderForHost"

    /// Assemblies named `Quiver.*`. The cheapest possible rejection, and the one that runs on
    /// every value a cell produces: `CanFormat` is a probe, not a commitment.
    let isQuiverValue (value: obj) =
        match value with
        | null -> false
        | v ->
            match v.GetType().Assembly.GetName().Name with
            | null -> false
            | name -> name = "Quiver" || name.StartsWith("Quiver.", StringComparison.Ordinal)

    /// Both bridge methods, resolved together, cached per resolved type.
    ///
    /// Keyed on the `Quiver.Html` type rather than on a bare flag: a host can hold several load
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

        // Already-loaded first. `Quiver.Interactive` is present exactly when the notebook
        // referenced it, and its absence is a legitimate answer: the reader pulled
        // `Quiver.Plot` alone, there is nothing here that can render a figure, and the F#
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

    /// Can Quiver render this value?
    ///
    /// Every failure path answers `false`, including the reflective ones. A formatter's probe
    /// runs against every value in the notebook, most of which have nothing to do with Quiver,
    /// and the correct behaviour when this bridge cannot be established is for the kernel's own
    /// formatting to proceed untouched.
    let canRender (value: obj) : bool =
        if not (isQuiverValue value) then
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
