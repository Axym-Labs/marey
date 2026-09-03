namespace Marey

open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// §7.2 / D69 — THE STORYBOARD, RE-SPELLED
//
// §7.2 sketched the CE in 2024's vocabulary:
//
//     let! axes  = draw (Axes.cartesian xScale yScale) |> over 0.8<s>
//     let! curve = draw (Plot.line data)               |> over 1.2<s>
//     do! parallel' [ curve |> morphTo (Plot.line data2) |> over 2.0<s>
//                     axes  |> zoomTo (0.0<m>, 5.0<m>)   |> over 2.0<s> ]
//     do! highlight curve.[42] |> over 0.6<s>
//
// D69 settled that the *shape* is preserved and left the plan to re-spell the nouns against
// the grammar M2 actually built. Three of them moved, and each move is a sentence about the
// library rather than a rename.
//
// **`Axes.cartesian` is gone, and it takes a `let!` with it.** A `Figure` carries its own
// axes, scales, theme and legend, so there is nothing to draw separately and nothing to bind.
// The CE's line count goes down, which is what D69 predicted is the sign of a correct
// re-spelling. `zoomTo` survives as `zoomToX`/`zoomToY` — M2 spells the still version
// `Chart.withXDomain`, and naming the axis in the function name is that spelling.
//
// **`morphTo` interpolates the data, not the drawn geometry** — and that is a finding, not a
// convenience. In 2024's sketch `Plot.line data` *was* a drawn thing, so morphing it meant
// morphing paths. A `Figure` carries its own scales, so two figures are two coordinate
// systems: morphing a's path into b's path would put half of a's geometry on b's axes, and
// the tick labels would be telling the truth about neither. Interpolating the *series*
// instead — `Erase.tweenSeries` — leaves a real `Figure` at every instant, whose domain, ticks
// and unit prefix are all derived from the numbers actually being shown. D74's `Morph.paths`
// is still the right tool one layer down, for two paths in one coordinate system.
//
// **`highlight curve.[42]` needs a scatter, and §7.2 highlights a line.** A line is one
// `Path`: point 42 has no node, and splitting a polyline in three changes the joins between
// the pieces rather than naming one of them. §5.1's batching is what makes a mark addressable
// at all, and `Markers` is the node that batches. So the re-spelled example scatters, which is
// what the sentence meant — highlighting a *datum* is a scatter's idea.
//
// ### What `let!` binds, and why it is not the animation
//
// A `Handle`: a `Key`, plus the duration it was introduced over. Not an `Anim<Node>` — §7.2
// uses `curve` twice after binding it, both times to refer to something already on stage, and
// a handle that was the animation would make `morphTo` composition rather than reference. D1
// resolved this tension in the IR's favour once already: the handle is a *name*, the scene is
// a *value*, and the storyboard is a function from time to a scene in which that name
// resolves.
//
// The duration is not decoration. It is every verb's **default**: `do! highlight curve.[42]`
// with no `over` runs for as long as `curve` took to arrive. A default that comes from the
// storyboard beats a constant chosen in this file.
//
// ### What the CE accumulates, which is not what §7.2 said
//
// §7.2: *"a writer monad accumulating `Anim<Scene>` under `andThen`/`map2`"*. It cannot be.
// `Anim.andThen a b` reaches `b` with no knowledge of `a`, so the second step of a storyboard
// would draw its own contribution on an empty stage and everything drawn before it would
// vanish. The accumulator is `Anim<Stage -> Stage>` — an animation of *what the stage becomes*
// — and sequential composition threads the first act's final stage into the second:
//
//     andThen act1 (Anim.map (fun g -> g << act1.At act1.Duration) act2)
//
// Parallel composition is then `Anim.map2 (>>)` with nothing added, which is the part that
// makes this worth doing: `map2`'s duration is the maximum and D70's clamp makes the shorter
// branch *hold its final value* through the tail. That is exactly what a storyboard wants
// from `parallel'`, and it is inherited rather than re-implemented.
// ======================================================================================

/// D69 — what is on stage at one instant.
///
/// One figure at a time, deliberately. M2 already spells "two charts on one surface"
/// (`Chart.beside`, `Chart.above`, `Chart.facet`) and it spells it as *one* `Figure` with two
/// panels; a second composition mechanism inside the storyboard would be a second layout
/// system, and D68's keys are rooted at `figure`, so two figures on one stage would answer to
/// the same names. A storyboard that shows two things shows a two-panel figure.
///
/// `Edits` are rewrites applied to the compiled scene, each naming the subtree it changes.
/// They are functions, which is the one place in this library where that is allowed: D1 keeps
/// closures out of the *scene*, because a scene is a value that has to diff and compare. A
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


/// D69 — a name for something on stage, and the duration it arrived over.
[<NoComparison>]
type Handle =
    { /// The subtree this handle names.
      Key: Key

      /// How long it was introduced over. The default duration of every verb applied to it,
      /// which is what stops those verbs needing a constant of their own.
      Introduced: float<s>

      /// The layer `.[i]` counts marks within. `None` when there is no single layer to count
      /// in — a figure with two scatters has two mark 42s and this refuses to guess.
      MarkLayer: Key option

      /// The layer and row this handle names, when it names one **mark** rather than a
      /// subtree. What `highlight` needs, and what tells `curve.[42]` apart from `curve`.
      Row: (Key * int) option }

    /// **`curve.[42]`** — D69's most quotable line, and the one §5.1 made structurally
    /// impossible. It resolves to `Keys.mark`'s spelling of row 42 of the layer this handle
    /// counts in; addressing it is what splits that layer's `MarkerBatch` three ways.
    ///
    /// The row is the datum's **original** row, not its position among the drawn marks. A
    /// non-finite point earlier in the series is not drawn, and counting drawn marks would
    /// name a different datum from that point on — D55's defect, which D68 wrote the rule
    /// against and `Geom.compileFor` obeys.
    member this.Item
        with get (row: int): Handle =
            match this.MarkLayer with
            | Some layer ->
                { Key = Keys.mark layer row
                  Introduced = this.Introduced
                  MarkLayer = Some layer
                  Row = Some(layer, row) }
            | None ->
                invalidOp
                    "Marey: this handle does not name a single scatter, so mark 42 is ambiguous. Only a layer drawn with GeomPoint has addressable marks."


/// D69 — the CE's value. A function from the stage a story starts on to whatever it binds and
/// an animation of what the stage becomes.
[<NoComparison; NoEquality>]
type Story<'a> = Story of (Stage -> 'a * Anim<Stage -> Stage>)

/// A step whose duration has not been chosen yet. `over` chooses it; the CE uses `Default`
/// when it is not written, and `Default` is the handle's own introduction.
[<NoComparison; NoEquality>]
type Step<'a> =
    { Default: float<s>
      Build: float<s> -> Story<'a> }


/// §7.2 / D69 — the storyboard.
///
/// **Meant to be opened.** `scene { let! curve = draw fig |> over 1.2<s> }` is the shape §7.2
/// asked for and `Storyboard.draw fig |> Storyboard.over 1.2<s>` is not, so this module is not
/// `RequireQualifiedAccess` and the names inside it are chosen to survive an `open`.
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
        Story(fun _ -> v, Anim.pure' identity)

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

    /// **Reparameterise a step's clock.** §7.2's `|> ease Ease.cubicInOut`, and `Anim.ease` with
    /// nothing added — so D71 holds here too: the result of `f` is **not** clamped, and an easing
    /// that leaves `[0, 1]` reaches the interpolation underneath and extrapolates. A morph eased
    /// with `Ease.backOut` overshoots its target and settles back onto it.
    let ease (f: float -> float) (story: Story<'a>) : Story<'a> =
        Story(fun s0 ->
            let a, act = unwrap story s0
            a, Anim.ease f act)

    /// Run every branch from the same starting stage, at the same time.
    ///
    /// The duration is the longest branch and the short ones **hold their final state** —
    /// which is `Anim.map2`'s rule, not a rule invented here. D70 clamps the seek so that a
    /// component reached past its end gives its last frame, and that is exactly what a
    /// storyboard means by "the fade finished while the zoom was still going".
    ///
    /// Branches compose left to right, onto the stage as it stands, so two branches that write
    /// the same property end with the later one's answer. §7.2's case — a morph beside a zoom —
    /// composes: the morph writes each layer's values and the zoom writes its domain. They meet
    /// on the domain, because a morph's domain follows its own data, and there the later branch
    /// wins. Order the pair the way you want that read.
    let parallel' (branches: Story<unit> list) : Story<unit> =
        Story(fun s0 ->
            let acts = branches |> List.map (fun b -> snd (unwrap b s0))

            (), acts |> List.fold (fun acc a -> Anim.map2 (fun f g -> f >> g) acc a) (Anim.pure' identity))

    // ---- Reading the stage -----------------------------------------------------------------

    let private markLayerOf (fig: Figure) : Key option =
        let candidates =
            [| for p in fig.Panels do
                   for i in 0 .. p.Layers.Length - 1 do
                       match p.Layers.[i].Geom with
                       | GeomPoint -> yield Keys.layer (Keys.panel p.Row p.Col) i
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
                    { Key = Keys.figure
                      Introduced = d
                      MarkLayer = markLayerOf fig
                      Row = None },

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

    /// **Morph what is on stage into another figure**, by interpolating the data underneath it.
    ///
    /// The handle's key chooses the scope: a figure handle morphs every layer, a panel handle
    /// morphs that panel's, a layer handle morphs one. Layers are paired by panel position and
    /// ordinal — D68's identity, and the same by-index rule D74 took for subpaths — and a layer
    /// with no counterpart is left alone rather than guessed at.
    ///
    /// The two sides need not have the same number of points: both are resampled to the longer
    /// of the two by index fraction. They *do* need matching dimensions, and the units may
    /// differ: `Erase.tweenSeries` converts through SI, so morphing kilometres into metres is a
    /// morph and not a factor of a thousand.
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
                            let blended =
                                onStage "morphTo" s
                                |> overLayers h.Key (fun lk l ->
                                    match counterpart from lk, counterpart target lk with
                                    | Some src, Some dst -> { l with Data = Erase.tweenSeries src.Data dst.Data u }
                                    | _ -> l)

                            { s with Figure = Some blended })) }

    /// **Zoom an axis**, by animating its domain. §7.2's `zoomTo`, with the axis in the name
    /// because that is how M2 spells the still version (`Chart.withXDomain`).
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

    /// Clamped, unlike a tween, and for `Scene.opacity`'s reason rather than in spite of D71: a
    /// marker of negative size has no picture to draw, so an overshooting easing has nowhere to
    /// put its overshoot. D71's rule is that the *interpolation* does not clamp; a style ramp
    /// with a floor is not an interpolation between two styles.
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
    /// Two things happen, and the first is the one D69 is about: the row is **addressed**, which
    /// splits its layer's `MarkerBatch` into before / the mark / after so that there is a
    /// subtree with that name to rewrite at all. The addressing persists for the rest of the
    /// storyboard, and so does the highlight — a highlight that undid itself at the end of its
    /// own step would be a flash, not a highlight.
    let highlight (h: Handle) : Step<unit> =
        { Default = h.Introduced
          Build =
            fun d ->
                Story(fun s0 ->
                    let layerKey, row =
                        match h.Row with
                        | Some lr -> lr
                        | None ->
                            invalidOp "Marey: highlight names one mark. Write `curve.[42]`, not `curve`."

                    let theme = (onStage "highlight" s0).Theme

                    (),
                    Anim.ofFraction d (fun u ->
                        fun s ->
                            let addressed =
                                s.Figure
                                |> Option.map (
                                    overLayers layerKey (fun lk l ->
                                        if lk = layerKey && not (Array.contains row l.Addressed) then
                                            { l with Addressed = Array.append l.Addressed [| row |] }
                                        else
                                            l)
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

    /// **The storyboard as an animation of scenes**, laid out once for the whole shot.
    ///
    /// `samples` is D72's union: the storyboard is sampled that many times, every state is
    /// arranged, and the gutters are the componentwise maximum, so the panel does not breathe
    /// while the data moves. There is deliberately no default — 32 is a good number and it
    /// belongs at the call site, where a reader can see it.
    let animateWith (samples: int) (m: ITextMeasurer) (extent: Extent<px>) (story: Story<unit>) : Anim<Node> =
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

        Anim.ofSeconds total (fun t ->
            let stage = st.At t
            let scene = Figure.toSceneIn m shot (figureAt t)

            stage.Edits |> Array.fold (fun n (k, f) -> Scene.mapKey k f n) scene)

    /// `animateWith`, measured with the zero-dependency default measurer — `Chart.toScene`'s
    /// relationship to `Chart.toSceneWith`, one milestone later.
    let animate (samples: int) (extent: Extent<px>) (story: Story<unit>) : Anim<Node> =
        animateWith samples TextMeasure.approx extent story
