namespace Marey

open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// THE STORYBOARD
//
// The shape it was first sketched in:
//
//     let! axes  = draw (Axes.cartesian xScale yScale) |> over 0.8<s>
//     let! curve = draw (Plot.line data)               |> over 1.2<s>
//     do! parallel' [ curve |> morphTo (Plot.line data2) |> over 2.0<s>
//                     axes  |> zoomTo (0.0<m>, 5.0<m>)   |> over 2.0<s> ]
//     do! highlight curve.[42] |> over 0.6<s>
//
// The shape survives; three of the nouns moved, and each move is a sentence about the library
// rather than a rename.
//
// **There is no `Axes.cartesian`, and it takes a `let!` with it.** A `Figure` carries its own
// axes, scales, theme and legend, so there is nothing to draw separately and nothing to bind.
// The block gets shorter, which is the sign the nouns are right. `zoomTo` is `zoomToX`/`zoomToY`
// — the still version is `Chart.withXDomain`, and naming the axis in the function name is that
// spelling.
//
// **`morphTo` interpolates the data, not the drawn geometry** — and that is a finding, not a
// convenience. A `Figure` carries its own scales, so two figures are two coordinate systems:
// morphing a's path into b's path would put half of a's geometry on b's axes, and the tick
// labels would be telling the truth about neither. Interpolating the *series* instead —
// `Erase.tweenSeries` — leaves a real `Figure` at every instant, whose domain, ticks and unit
// prefix are all derived from the numbers actually being shown. `Morph.paths` is still the right
// tool one layer down, for two paths in one coordinate system.
//
// **`highlight curve.[42]` needs a scatter, though the sketch highlights a line.** A line is one
// `Path`: point 42 has no node, and splitting a polyline in three changes the joins between the
// pieces rather than naming one of them. Batching is what makes a mark addressable at all, and
// `Markers` is the node that batches. So the example scatters, which is what the sentence meant
// — highlighting a *datum* is a scatter's idea.
//
// ### What `let!` binds, and why it is not the animation
//
// A `Handle`: a `Key`, plus the duration it was introduced over. Not an `Anim<Node>` — the sketch
// uses `curve` twice after binding it, both times to refer to something already on stage, and a
// handle that was the animation would make `morphTo` composition rather than reference. The IR
// resolves this tension the same way: the handle is a *name*, the scene is a *value*, and the
// storyboard is a function from time to a scene in which that name resolves.
//
// The duration is not decoration. It is every verb's **default**: `do! highlight curve.[42]`
// with no `over` runs for as long as `curve` took to arrive. A default that comes from the
// storyboard beats a constant chosen in this file.
//
// ### What the block accumulates
//
// Not *"a writer monad accumulating `Anim<Scene>` under `andThen`/`map2`"*: it cannot be.
// `Anim.andThen a b` reaches `b` with no knowledge of `a`, so the second step of a storyboard
// would draw its own contribution on an empty stage and everything drawn before it would
// vanish. The accumulator is `Anim<Stage -> Stage>` — an animation of *what the stage becomes*
// — and sequential composition threads the first act's final stage into the second:
//
//     andThen act1 (Anim.map (fun g -> g << act1.At act1.Duration) act2)
//
// Parallel composition is then `Anim.map2 (>>)` with nothing added, which is the part that
// makes this worth doing: `map2`'s duration is the maximum and `At`'s clamp makes the shorter
// branch *hold its final value* through the tail. That is exactly what a storyboard wants from
// `parallel'`, and it is inherited rather than re-implemented.
// ======================================================================================

/// What is on stage at one instant.
///
/// One figure at a time, deliberately. The library already spells "two charts on one surface"
/// (`Chart.beside`, `Chart.above`, `Chart.facet`) and it spells it as *one* `Figure` with two
/// panels; a second composition mechanism inside the storyboard would be a second layout
/// system, and the keys are rooted at `figure`, so two figures on one stage would answer to
/// the same names. A storyboard that shows two things shows a two-panel figure.
///
/// `Edits` are rewrites applied to the compiled scene, each naming the subtree it changes.
/// They are functions, which is the one place in this library where that is allowed: closures
/// stay out of the *scene*, because a scene is a value that has to diff and compare. A
/// stage is neither — it is the storyboard's own intermediate, thrown away one frame later. A
/// closed `Edit` union would be a second IR, and the constraint on the IR is that it does not
/// grow to fit whatever arrives.
[<NoComparison; NoEquality>]
type Stage =
    { Figure: Figure option
      Edits: (Key * (Node -> Node))[] }

[<RequireQualifiedAccess>]
module Stage =

    /// Nothing drawn yet. Every storyboard starts here.
    let empty: Stage = { Figure = None; Edits = [||] }


/// Which mark a handle names: a row of the layer, or — when the data supplied keys — the
/// datum with that key, wherever it sits.
[<RequireQualifiedAccess>]
type internal MarkRef =
    | Row of int
    | Datum of string

/// A name for something on stage, and the duration it arrived over.
[<NoComparison>]
type Handle =
    internal
        { /// The subtree this handle names.
          Key: Key

          /// How long it was introduced over. The default duration of every verb applied to it,
          /// which is what stops those verbs needing a constant of their own.
          Introduced: float<s>

          /// The layer `.[i]` counts marks within. `None` when there is no single layer to count
          /// in — a figure with two scatters has two mark 42s and this refuses to guess.
          MarkLayer: Key option

          /// The layer and mark this handle names, when it names one **mark** rather than a
          /// subtree. What `highlight` needs, and what tells `curve.[42]` apart from `curve`.
          Mark: (Key * MarkRef) option

          /// The keys of `MarkLayer`'s rows when it was drawn, or empty when its data
          /// supplied none. With keys, `curve.[42]` names the datum that was row 42, and keeps
          /// naming it after a morph has moved it.
          MarkKeys: string[] }

    /// **`curve.[42]`** — the storyboard's most quotable line, and the one batching makes
    /// structurally impossible without addressing. It resolves to `Keys.mark`'s spelling of row 42
    /// of the layer this handle counts in; addressing it is what splits that layer's `MarkerBatch`
    /// three ways.
    ///
    /// The row is the datum's **original** row, not its position among the drawn marks. A
    /// non-finite point earlier in the series is not drawn, and counting drawn marks would
    /// name a different datum from that point on — a defect the addressing rule forbids and
    /// `Geom.compileFor` obeys.
    member this.Item
        with get (row: int): Handle =
            match this.MarkLayer with
            | Some _ when this.MarkKeys.Length > 0 ->
                if row < 0 || row >= this.MarkKeys.Length then
                    invalidArg "row" (sprintf "Marey: the scatter drawn has %d rows; there is no row %d." this.MarkKeys.Length row)

                this.[this.MarkKeys.[row]]
            | Some layer ->
                { this with
                    Key = Keys.mark layer row
                    Mark = Some(layer, MarkRef.Row row) }
            | None ->
                invalidOp
                    "Marey: this handle does not name a single scatter, so mark 42 is ambiguous. Only a layer drawn with GeomPoint has addressable marks."

    /// **`curve.["Bern"]`** — the mark whose datum has this key. It names the datum rather
    /// than a row, so it stays on the same station when an earlier one is filtered out. Needs a
    /// scatter whose data supplied keys (`Chart.withKeys`, or `key` in the encoding block).
    member this.Item
        with get (key: string): Handle =
            match this.MarkLayer with
            | Some layer when this.MarkKeys.Length > 0 ->
                { this with
                    Key = Keys.datum layer key
                    Mark = Some(layer, MarkRef.Datum key) }
            | Some _ ->
                invalidOp
                    "Marey: this scatter's rows have no keys, so a mark cannot be named by one. Give them keys with `Chart.withKeys` or `key` in the encoding block, or name the row: `curve.[42]`."
            | None ->
                invalidOp
                    "Marey: this handle does not name a single scatter, so a mark is ambiguous. Only a layer drawn with GeomPoint has addressable marks."


/// The CE's value. A function from the stage a story starts on to whatever it binds and
/// an animation of what the stage becomes.
[<NoComparison; NoEquality>]
type Story<'a> = internal Story of (Stage -> 'a * Anim<Stage -> Stage>)

/// A step whose duration has not been chosen yet. `over` chooses it; the CE uses `Default`
/// when it is not written, and `Default` is the handle's own introduction.
[<NoComparison; NoEquality>]
type Step<'a> =
    internal
        { Default: float<s>
          Build: float<s> -> Story<'a> }


/// The storyboard.
///
/// **Meant to be opened.** `scene { let! curve = draw fig |> over 1.2<s> }` is the shape the
/// storyboard is written in and `Storyboard.draw fig |> Storyboard.over 1.2<s>` is not, so this
/// module is not `RequireQualifiedAccess` and the names inside it are chosen to survive an `open`.
///
/// ```fsharp
/// open Marey
/// open Marey.Storyboard
///
/// let intro =
///     scene {
///         let! curve = draw (Chart.scatter U.second U.metre t y) |> over 1.2<s>
///         do! wait 0.5<s>
///         do! parallel' [ curve |> morphTo grown |> over 2.0<s>
///                         curve |> zoomToX (0.0, 5.0) |> over 2.0<s> ]
///         do! highlight curve.[42] |> over 0.6<s>
///     }
///
/// let anim = animate 32 (Chart.size 640.0 400.0) intro
/// ```
module Storyboard =

    let private unwrap (Story f) = f

    let private identity: Stage -> Stage = id

    // ---- The monad -------------------------------------------------------------------------

    let private ret (v: 'a) : Story<'a> =
        Story(fun _ -> v, Anim.constant identity)

    /// Sequential composition, and the whole of why the accumulator is a *function* of the
    /// stage: `act2` is reached having already been composed with where `act1` left the stage.
    let private bind (m: Story<'a>) (f: 'a -> Story<'b>) : Story<'b> =
        Story(fun s0 ->
            let a, act1 = unwrap m s0
            // Evaluated once, not per frame. The stage a step *ends* on is static — which
            // figure is on stage and which edits stand does not depend on the clock, only the
            // numbers inside them do.
            let after = act1.At act1.Duration
            let b, act2 = unwrap (f a) (after s0)
            b, Anim.andThen act1 (Anim.map (fun g -> g << after) act2))

    [<Sealed>]
    type Builder() =
        member _.Bind(m: Story<'a>, f: 'a -> Story<'b>) : Story<'b> = bind m f

        /// `let! x = draw fig` with no `over`: the step takes its own default.
        member _.Bind(s: Step<'a>, f: 'a -> Story<'b>) : Story<'b> = bind (s.Build s.Default) f

        member _.Return(v: 'a) : Story<'a> = ret v
        member _.ReturnFrom(m: Story<'a>) : Story<'a> = m
        member _.ReturnFrom(s: Step<'a>) : Story<'a> = s.Build s.Default
        member _.Zero() : Story<unit> = ret ()
        member _.Delay(f: unit -> Story<'a>) : Story<'a> = Story(fun s -> unwrap (f ()) s)
        member _.Combine(a: Story<unit>, b: Story<'a>) : Story<'a> = bind a (fun () -> b)

    /// The storyboard CE.
    let scene = Builder()

    // ---- Timing ----------------------------------------------------------------------------

    /// **`draw fig |> over 1.2<s>`** — how long a step takes.
    let over (d: float<s>) (step: Step<'a>) : Story<'a> = step.Build d

    /// Hold the stage. The one verb that is a `Story` rather than a `Step`, because a wait
    /// with no duration is not a wait.
    let wait (d: float<s>) : Story<unit> =
        Story(fun _ -> (), Anim.ofSeconds d (fun _ -> identity))

    /// **Reparameterise a step's clock.** `|> ease Ease.cubicInOut`, and `Anim.ease` with
    /// nothing added — so the result of `f` is **not** clamped here either, and an easing
    /// that leaves `[0, 1]` reaches the interpolation underneath and extrapolates. A morph eased
    /// with `Ease.backOut` overshoots its target and settles back onto it.
    let ease (f: float -> float) (story: Story<'a>) : Story<'a> =
        Story(fun s0 ->
            let a, act = unwrap story s0
            a, Anim.ease f act)

    /// Run every branch from the same starting stage, at the same time.
    ///
    /// The duration is the longest branch and the short ones **hold their final state** —
    /// which is `Anim.map2`'s rule, not a rule invented here. `At` clamps the seek so that a
    /// component reached past its end gives its last frame, and that is exactly what a
    /// storyboard means by "the fade finished while the zoom was still going".
    ///
    /// Branches compose left to right, onto the stage as it stands, so two branches that write
    /// the same property end with the later one's answer. The common case — a morph beside a zoom —
    /// composes: the morph writes each layer's values and the zoom writes its domain. They meet
    /// on the domain, because a morph's domain follows its own data, and there the later branch
    /// wins. Order the pair the way you want that read.
    let parallel' (branches: Story<unit> list) : Story<unit> =
        Story(fun s0 ->
            let acts = branches |> List.map (fun b -> snd (unwrap b s0))

            (), acts |> List.fold (fun acc a -> Anim.map2 (fun f g -> f >> g) acc a) (Anim.constant identity))

    // ---- Reading the stage -----------------------------------------------------------------

    let private markLayerOf (fig: Figure) : (Key * string[]) option =
        let candidates =
            [| for p in fig.Panels do
                   for i in 0 .. p.Layers.Length - 1 do
                       match p.Layers.[i].Geom with
                       | GeomPoint -> yield Keys.layer (Keys.panel p.Row p.Col) i, p.Layers.[i].RowKeys
                       | _ -> () |]

        if candidates.Length = 1 then Some candidates.[0] else None

    let private onStage (what: string) (s: Stage) : Figure =
        match s.Figure with
        | Some f -> f
        | None -> invalidOp (sprintf "Marey: %s needs something on stage — `draw` first." what)

    /// Every layer the key covers, each with its own key. `Keys.covers` is a segment-wise
    /// prefix test, so a figure key covers everything, a panel key covers that panel, and a
    /// layer key covers exactly itself — which makes this both the scope walk and the lookup.
    let private layersIn (scope: Key) (fig: Figure) : (Key * Layer)[] =
        [| for p in fig.Panels do
               let pk = Keys.panel p.Row p.Col

               for i in 0 .. p.Layers.Length - 1 do
                   let lk = Keys.layer pk i

                   if Keys.covers scope lk then
                       yield lk, p.Layers.[i] |]

    /// The layer another figure holds at the same address, if it holds one. `layersIn` with a
    /// layer key covers exactly that layer, so this is a lookup written as a scope.
    let private counterpart (other: Figure) (lk: Key) : Layer option =
        layersIn lk other |> Array.tryHead |> Option.map snd

    /// Rewrite every layer the key covers. The one traversal `morphTo`, `zoomTo` and
    /// `highlight` share.
    let private overLayers (scope: Key) (f: Key -> Layer -> Layer) (fig: Figure) : Figure =
        { fig with
            Panels =
                fig.Panels
                |> Array.map (fun p ->
                    let pk = Keys.panel p.Row p.Col

                    { p with
                        Layers =
                            p.Layers
                            |> Array.mapi (fun i l ->
                                let lk = Keys.layer pk i
                                if Keys.covers scope lk then f lk l else l) }) }

    // ---- The verbs -------------------------------------------------------------------------

    /// Put a figure on stage and reveal it over the step's duration.
    ///
    /// Replaces whatever was there — see `Stage` on why there is one. A zero-duration draw is
    /// a cut: the figure is simply there, at full opacity, on the first frame.
    let draw (fig: Figure) : Step<Handle> =
        { Default = 0.0<s>
          Build =
            fun d ->
                Story(fun _ ->
                    let marks = markLayerOf fig

                    { Key = Keys.figure
                      Introduced = d
                      MarkLayer = marks |> Option.map fst
                      Mark = None
                      MarkKeys = marks |> Option.map snd |> Option.defaultValue [||] },

                    Anim.ofFraction d (fun u ->
                        fun _ ->
                            { Figure = Some fig
                              // A completed reveal contributes *nothing*, rather than an
                              // `Opacity(1.0, _)` that would sit over the figure for the rest
                              // of the storyboard and make a drawn frame structurally unlike
                              // the same figure rendered on its own.
                              Edits =
                                if d > 0.0<s> && u < 1.0 then
                                    [| Keys.figure, Scene.opacity u |]
                                else
                                    [||] })) }

    /// Fade a named subtree in — a layer, a panel, or the whole figure.
    let fadeIn (h: Handle) : Step<unit> =
        { Default = h.Introduced
          Build =
            fun d ->
                Story(fun _ ->
                    (),
                    Anim.ofFraction d (fun u ->
                        fun s ->
                            if d > 0.0<s> && u < 1.0 then
                                { s with Edits = Array.append s.Edits [| h.Key, Scene.opacity u |] }
                            else
                                s)) }

    /// A source channel value as another map reads it. A level is matched by name,
    /// because the two figures numbered their levels separately; a level the map does not
    /// have is NaN, which is not drawn and is counted, as any unmapped value is. A value map's
    /// numbers are raw in the channel's unit and pass through.
    let private translateChannel (src: ChannelMap) (dst: ChannelMap) (v: float) : float =
        let byName (levels: string[]) (levels': string[]) =
            let i = int v

            if float i = v && i >= 0 && i < levels.Length then
                match System.Array.IndexOf(levels', levels.[i]) with
                | -1 -> nan
                | j -> float j
            else
                nan

        match src, dst with
        | ColorByLevel(levels, _), ColorByLevel(levels', _)
        | ShapeByLevel(levels, _), ShapeByLevel(levels', _) -> byName levels levels'
        | _ -> v

    let private sameKind (a: ChannelMap) (b: ChannelMap) =
        match a, b with
        | ColorByLevel _, ColorByLevel _
        | ShapeByLevel _, ShapeByLevel _
        | ColorByValue _, ColorByValue _
        | SizeByValue _, SizeByValue _ -> true
        | _ -> false

    /// **A scatter morphed by key.** Each of the target's rows, in the target's order,
    /// paired with the source row of the same key or with nothing; then the source rows whose
    /// key the target does not have. Returned as the pairing for `Erase.tweenRows`, the keys in
    /// that order, and how many of them are the target's.
    ///
    /// `None` unless both layers are scatters with keys and their channels line up kind for kind:
    /// only a scatter has a node per mark to fade, and a channel of another kind has no
    /// value to carry a row across in. The morph then pairs by index, as it always did.
    let private pairByKey (src: Layer) (dst: Layer) : (struct (int * int)[] * string[] * int) option =
        let scatter (l: Layer) =
            match l.Geom with
            | GeomPoint -> l.RowKeys.Length > 0
            | _ -> false

        let channelsAlign =
            src.Channels.Length = dst.Channels.Length
            && Array.forall2 (fun (a: Channel) (b: Channel) -> sameKind a.Map b.Map) src.Channels dst.Channels

        if not (scatter src && scatter dst && channelsAlign) then
            None
        else
            let srcRow =
                src.RowKeys |> Array.mapi (fun i k -> k, i) |> dict

            let inDst = System.Collections.Generic.HashSet<string>(dst.RowKeys)

            let rows =
                Array.append
                    (dst.RowKeys
                     |> Array.mapi (fun j k ->
                         match srcRow.TryGetValue k with
                         | true, i -> struct (i, j)
                         | _ -> struct (-1, j)))
                    [| for i in 0 .. src.RowKeys.Length - 1 do
                           if not (inDst.Contains src.RowKeys.[i]) then
                               yield struct (i, -1) |]

            let keys =
                rows
                |> Array.map (fun (struct (i, j)) -> if j >= 0 then dst.RowKeys.[j] else src.RowKeys.[i])

            Some(rows, keys, dst.RowKeys.Length)

    /// One frame of a keyed morph: the layer, and the fades its entering and leaving marks need.
    let private morphByKey
        (lk: Key)
        (src: Layer)
        (dst: Layer)
        (rows: struct (int * int)[], keys: string[], kept: int)
        (u: float)
        : Layer * (Key * (Node -> Node))[] =
        // Past the end the leaving marks have faded out; drop them, and the layer is the target's
        // rows in the target's order.
        let final = u >= 1.0
        let rows, keys = if final then Array.sub rows 0 kept, Array.sub keys 0 kept else rows, keys

        // While a level is leaving, the map holds it too: the target's levels, then the source's
        // that the target does not have, in the source's colours or shapes. A station filtered
        // out with its whole region then fades in its own colour instead of vanishing. At the
        // end the map is the target's.
        let during (cs: ChannelMap) (cd: ChannelMap) =
            let extra (levels: string[]) (levels': string[]) =
                [| for i in 0 .. levels.Length - 1 do
                       if not (Array.contains levels.[i] levels') then
                           yield i |]

            match cs, cd with
            | ColorByLevel(levels, colors), ColorByLevel(levels', colors') ->
                let add = extra levels levels'
                ColorByLevel(Array.append levels' (add |> Array.map (fun i -> levels.[i])), Array.append colors' (add |> Array.map (fun i -> colors.[i])))
            | ShapeByLevel(levels, shapes), ShapeByLevel(levels', shapes') ->
                let add = extra levels levels'
                ShapeByLevel(Array.append levels' (add |> Array.map (fun i -> levels.[i])), Array.append shapes' (add |> Array.map (fun i -> shapes.[i])))
            | _ -> cd

        let channels =
            Array.map2
                (fun (cs: Channel) (cd: Channel) ->
                    let map = if final then cd.Map else during cs.Map cd.Map

                    { cd with
                        Map = map
                        Values =
                            rows
                            |> Array.map (fun (struct (i, j)) ->
                                if j >= 0 then cd.Values.[j] else translateChannel cs.Map map cs.Values.[i]) })
                src.Channels
                dst.Channels

        let entering = [| for r in 0 .. rows.Length - 1 do match rows.[r] with struct (-1, _) -> yield r | _ -> () |]
        let leaving = [| kept .. rows.Length - 1 |]

        // A mark the source had addressed — a highlight — follows its key to its new row.
        let carried =
            src.Addressed
            |> Array.choose (fun r ->
                if r < src.RowKeys.Length then
                    match System.Array.IndexOf(keys, src.RowKeys.[r]) with
                    | -1 -> None
                    | i -> Some i
                else
                    None)

        let layer =
            { src with
                Data = Erase.tweenRows rows src.Data dst.Data u
                RowKeys = keys
                Channels = channels
                Addressed = Array.distinct (Array.concat [ carried; (if final then [||] else entering); (if final then [||] else leaving) ]) }

        let fades =
            if final then
                [||]
            else
                Array.append
                    (entering |> Array.map (fun r -> Keys.datum lk keys.[r], Scene.opacity u))
                    (leaving |> Array.map (fun r -> Keys.datum lk keys.[r], Scene.opacity (1.0 - u)))

        layer, fades

    /// **Morph what is on stage into another figure**, by interpolating the data underneath it.
    ///
    /// The handle's key chooses the scope: a figure handle morphs every layer, a panel handle
    /// morphs that panel's, a layer handle morphs one. Layers are paired by panel position and
    /// ordinal — a key's identity, and the same by-index rule a path morph takes for subpaths — and
    /// a layer with no counterpart is left alone rather than guessed at.
    ///
    /// The two sides need not have the same number of points: both are resampled to the longer
    /// of the two by index fraction. They *do* need matching dimensions, and the units may
    /// differ: `Erase.tweenSeries` converts through SI, so morphing kilometres into metres is a
    /// morph and not a factor of a thousand.
    ///
    /// **A scatter whose rows have keys on both sides is paired by key**, not by index. A
    /// datum moves to where the datum of the same name is going; one only the target has fades
    /// in where it is going, and one only the source has fades out where it was. That is what
    /// makes a filter animate truthfully: a station that was dropped leaves, and nothing slides
    /// into its place. The layer ends as the target's rows, in the target's order, with the
    /// target's channels. Every other layer pairs by index.
    let morphTo (target: Figure) (h: Handle) : Step<unit> =
        { Default = h.Introduced
          Build =
            fun d ->
                Story(fun s0 ->
                    let from = onStage "morphTo" s0

                    (),
                    Anim.ofFraction d (fun u ->
                        fun s ->
                            // Written onto the stage as it *stands*, with the two endpoints
                            // read from where the step started. A morph running in `parallel'`
                            // beside a zoom must not discard the zoom, and a morph whose source
                            // was re-read every frame would chase its own output.
                            let fades = ResizeArray()

                            let blended =
                                onStage "morphTo" s
                                |> overLayers h.Key (fun lk l ->
                                    match counterpart from lk, counterpart target lk with
                                    | Some src, Some dst ->
                                        match pairByKey src dst with
                                        | Some pairing ->
                                            let layer, fs = morphByKey lk src dst pairing u
                                            fades.AddRange fs
                                            { layer with Style = l.Style; Name = l.Name; ShowInLegend = l.ShowInLegend }
                                        | None ->
                                            let data = Erase.tweenSeries src.Data dst.Data u

                                            // Resampled to another length, the rows have no names
                                            // left to keep: index pairing does not know which is
                                            // which.
                                            { l with
                                                Data = data
                                                RowKeys =
                                                    if Erase.seriesLength data = l.RowKeys.Length then l.RowKeys else [||] }
                                    | _ -> l)

                            { s with
                                Figure = Some blended
                                Edits = Array.append s.Edits (fades.ToArray()) })) }

    /// **Zoom an axis**, by animating its domain. The axis is in the name because that is how
    /// the still version is spelled (`Chart.withXDomain`).
    ///
    /// The domain is raw numbers in the axis's own unit, for the same reason `Chart.withXDomain`
    /// takes them raw: the measure was erased into the figure and there is nothing left to check
    /// it against.
    let private zoom (name: string) (onX: bool) (target: float * float) (h: Handle) : Step<unit> =
        { Default = h.Introduced
          Build =
            fun d ->
                Story(fun s0 ->
                    let fig = onStage name s0

                    // Read once, at build time, from the stage this step *starts* on — not per
                    // frame from the stage as it stands. A zoom running in `parallel'` with a
                    // morph would otherwise chase the morph's own changing domain and never
                    // arrive.
                    let start =
                        match layersIn h.Key fig |> Array.tryHead with
                        | Some(_, l) ->
                            let sx, sy = Erase.seriesScales l.Data
                            if onX then sx.Domain else sy.Domain
                        | None -> invalidOp (sprintf "Marey: %s names no layer with an axis to zoom." name)

                    let mix (a: float) (b: float) (u: float) = a + (b - a) * u

                    (),
                    Anim.ofFraction d (fun u ->
                        fun s ->
                            let dom =
                                Some(mix (fst start) (fst target) u, mix (snd start) (snd target) u)

                            let zoomed =
                                onStage name s
                                |> overLayers h.Key (fun _ l ->
                                    { l with
                                        Data =
                                            if onX then
                                                Figure.withDomains dom None l.Data
                                            else
                                                Figure.withDomains None dom l.Data })

                            { s with Figure = Some zoomed })) }

    let zoomToX (domain: float * float) (h: Handle) : Step<unit> = zoom "zoomToX" true domain h
    let zoomToY (domain: float * float) (h: Handle) : Step<unit> = zoom "zoomToY" false domain h

    /// Clamped, unlike a tween, and for `Scene.opacity`'s reason rather than in spite of unclamped
    /// easing: a marker of negative size has no picture to draw, so an overshooting easing has
    /// nowhere to put its overshoot. The rule is that the *interpolation* does not clamp; a style
    /// ramp with a floor is not an interpolation between two styles.
    let private emphasise (theme: Theme) (u: float) (n: Node) : Node =
        let k =
            if u < 0.0 then 0.0
            elif u > 1.0 then 1.0
            else u

        match n with
        | Markers b when k > 0.0 ->
            Markers
                { b with
                    Size = b.Size * (1.0 + 1.5 * k)
                    Stroke = Some(Stroke.solid theme.Foreground (2.0<px> * k)) }
        // At `k = 0` the node is returned untouched rather than restyled with a zero-width
        // stroke, so the first frame of a highlight is the picture that was already there.
        | other -> other

    /// **`highlight curve.[42]`** — grow one mark and ring it in the theme's foreground.
    ///
    /// Two things happen, and the first is the one that matters: the row is **addressed**, which
    /// splits its layer's `MarkerBatch` into before / the mark / after so that there is a
    /// subtree with that name to rewrite at all. The addressing persists for the rest of the
    /// storyboard, and so does the highlight — a highlight that undid itself at the end of its
    /// own step would be a flash, not a highlight.
    let highlight (h: Handle) : Step<unit> =
        { Default = h.Introduced
          Build =
            fun d ->
                Story(fun s0 ->
                    let layerKey, mark =
                        match h.Mark with
                        | Some lm -> lm
                        | None ->
                            invalidOp "Marey: highlight names one mark. Write `curve.[42]`, not `curve`."

                    let theme = (onStage "highlight" s0).Theme

                    // A datum is found by its key on the stage as it stands, so a highlight
                    // after a morph lands on the same datum at its new row. One that is not on
                    // stage when the step starts is refused, by name, rather than ringing nothing.
                    match mark with
                    | MarkRef.Datum k when
                        layersIn layerKey (onStage "highlight" s0)
                        |> Array.forall (fun (_, l) -> not (Array.contains k l.RowKeys))
                        ->
                        invalidOp (sprintf "Marey: highlight names the datum \"%s\", and the scatter on stage has no row with that key." k)
                    | _ -> ()

                    let rowIn (l: Layer) =
                        match mark with
                        | MarkRef.Row r -> Some r
                        | MarkRef.Datum k ->
                            match System.Array.IndexOf(l.RowKeys, k) with
                            | -1 -> None
                            | r -> Some r

                    (),
                    Anim.ofFraction d (fun u ->
                        fun s ->
                            let addressed =
                                s.Figure
                                |> Option.map (
                                    overLayers layerKey (fun lk l ->
                                        match rowIn l with
                                        | Some row when lk = layerKey && not (Array.contains row l.Addressed) ->
                                            { l with Addressed = Array.append l.Addressed [| row |] }
                                        | _ -> l)
                                )

                            { Figure = addressed
                              Edits = Array.append s.Edits [| h.Key, emphasise theme u |] })) }

    // ---- Output ----------------------------------------------------------------------------

    /// The storyboard as an animation of stages — the thing everything below is built on, and
    /// what to reach for when the picture is not what you want to produce.
    let stages (story: Story<unit>) : Anim<Stage> =
        let _, act = unwrap story Stage.empty
        Anim.map (fun (f: Stage -> Stage) -> f Stage.empty) act

    /// How long the storyboard runs.
    let duration (story: Story<unit>) : float<s> = (stages story).Duration

    /// **The storyboard as a shot and its scenes** — one function, two answers, for
    /// `compilePanel`'s reason: the layout the whole film is drawn inside, and the frames drawn
    /// inside it. Handing back only the second would make a renderer that needs the surface
    /// take it as a second argument, and a second extent beside a decided layout is a second
    /// chance to disagree with nothing able to notice. `Film` is built from this pair.
    ///
    /// `samples` is the shot's union: the storyboard is sampled that many times, every state is
    /// arranged, and the gutters are the componentwise maximum, so the panel does not breathe
    /// while the data moves. There is deliberately no default — 32 is a good number and it
    /// belongs at the call site, where a reader can see it.
    ///
    /// **The union pass is where the fonts are resolved**, and that matters to a parallel
    /// render: it runs here, once, on the calling thread, before any frame exists. So a
    /// measurer that resolves a face against the machine has done all of its resolving
    /// by the time `Array.Parallel.map` starts, and `ResolvedFaces` is in first-seen order
    /// rather than in whichever order sixteen threads happened to ask.
    let shoot (samples: int) (m: ITextMeasurer) (extent: Extent<px>) (story: Story<unit>) : Shot * Anim<Node> =
        let st = stages story
        let total = st.Duration

        // The surface before the first `draw`, and after a storyboard that never draws. It
        // wears the theme of whatever the storyboard ends on, so that an empty opening frame is
        // the right colour rather than the default one.
        let blank =
            let b = Figure.create [| Figure.panelOf [||] |]

            match (st.At total).Figure with
            | Some f -> { b with Theme = f.Theme }
            | None -> b

        let figureAt (t: float<s>) =
            match (st.At t).Figure with
            | Some f -> f
            | None -> blank

        let shot =
            Figure.arrangeUnion samples m extent (fun u -> figureAt (u * total))

        shot,
        Anim.ofSeconds total (fun t ->
            let stage = st.At t
            let scene = Figure.toSceneIn m shot (figureAt t)

            stage.Edits |> Array.fold (fun n (k, f) -> Scene.mapKey k f n) scene)

    /// **The storyboard as an animation of scenes**, laid out once for the whole shot.
    ///
    /// `shoot` without the layout, for a caller who only wants the picture.
    let animateWith (samples: int) (m: ITextMeasurer) (extent: Extent<px>) (story: Story<unit>) : Anim<Node> =
        snd (shoot samples m extent story)

    /// `animateWith`, measured with the zero-dependency default measurer — `Chart.toScene`'s
    /// relationship to `Chart.toSceneWith`, one milestone later.
    let animate (samples: int) (extent: Extent<px>) (story: Story<unit>) : Anim<Node> =
        animateWith samples TextMeasure.approx extent story
