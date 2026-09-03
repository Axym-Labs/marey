namespace Marey

/// Measured extents of a laid-out string, in device units.
[<Struct>]
type TextMetrics =
    { Advance: float<px>
      Ascent: float<px>
      Descent: float<px> }

/// §9 / §10 — Text measurement, abstracted from day one.
///
/// §9: "Axis label sizes depend on text metrics, which depend on the backend, so layout is a
/// *measure–arrange* pass against `IBackend.MeasureText`." The design's `IBackend` has no such
/// member, so this is that member, split out as its own capability because §10 asks for
/// exactly that: "architect `ITextShaper` as a capability from day one".
///
/// §15 puts the stakes plainly: "Text and layout eat 40% of the effort. Everyone
/// underestimates this." Having one seam here is what makes it possible to start with an
/// approximation and swap in HarfBuzz at M2 without touching the layout code.
///
/// ### D77 — an implementation must be thread-safe, and one instance serves a whole render
///
/// This is a requirement on the implementer, not an observation about the implementations. A
/// measurer is called from layout, and since M3 layout runs once per frame of an animation
/// across `Array.Parallel.map`. `Measure` must therefore be safe to call from several threads
/// at once. `ApproxTextMeasurer` is stateless and satisfies it for free; `SkiaTextMeasurer`
/// caches native handles and satisfies it with a lock.
///
/// **And exactly one instance is shared across every frame of one render.** The alternative —
/// a measurer per thread — is not a performance question, it is a correctness one: font
/// resolution walks a family stack against whatever is installed (D62), so N independent
/// resolutions are N chances for one frame of a video to be set in a different typeface from
/// the frame before it. That is invisible in a single frame, invisible in review, and
/// unfixable after the render. One measurer means one resolution and one `ResolvedFaces`
/// report for the whole shot.
///
/// The cost of sharing is contention on whatever lock the implementation needs, and the answer
/// is a cache rather than more measurers: `MemoTextMeasurer` in `Marey.Plot` memoises this
/// method on `(FontSpec, string)`, which is enough because a shot is laid out once (D72) and
/// every frame therefore asks about the same strings.
type ITextMeasurer =
    abstract Measure: FontSpec -> string -> TextMetrics

/// §5 — "`Capabilities` matters because TikZ and SVG have genuinely different power. The
/// compiler layer queries caps and *degrades* rather than silently producing wrong output."
///
/// Every field is a thing some real backend cannot do, not a speculative switch.
[<Struct>]
type Capabilities =
    { Gradients: bool
      RadialGradients: bool
      Clipping: bool
      Dashes: bool
      Rasters: bool
      /// Real shaping (kerning, ligatures, complex scripts) as opposed to advance-width
      /// estimation. False for the zero-dependency SVG backend, which delegates shaping to
      /// whatever opens the file.
      TextShaping: bool
      RotatedText: bool
      /// Can carry `Annotated` metadata into the output — `aria-label` on SVG, nothing on a
      /// raster target.
      Semantics: bool
      /// D67 — can composite a subtree and fade the *result*: SVG's `opacity` on a `<g>`,
      /// Skia's `SaveLayer` with an alpha paint. False for a backend that can only set alpha
      /// per shape, which is a visibly different picture wherever the group's own shapes
      /// overlap. `Degrade.forCaps` folds the node into paint alpha when this is false, which
      /// is that different picture, chosen deliberately.
      ///
      /// **TikZ is the backend that cannot**, which is why this is a field and not a constant.
      /// pgf's `opacity=` on a scope is *inherited by each path*, not composited over the
      /// scope, so emitting it would be the degraded picture wearing the capability's name.
      /// The pgf feature that does composite — `transparency group` — is documented as
      /// requiring driver support, and this backend's contract is a `string` that has to
      /// compile under a caller's own preamble and driver.
      GroupOpacity: bool
      /// Largest batch the backend will take in one node; `None` for unlimited. The GPU
      /// backend has a real instance-buffer ceiling and the compiler must split at it.
      MaxBatch: int option }

/// §5 — The backend contract. Adding a `Node` case breaks every implementation at compile
/// time, which is the behaviour we want.
///
/// `Finish` returns `unit`, not the rendered artifact. Backends produce genuinely different
/// things — a string, a file, a GPU swapchain present — so the *result* is exposed by the
/// concrete type (`SvgBackend.Result`) while the *protocol* stays uniform enough to hold a
/// heterogeneous list of backends and drive them all from one scene.
///
/// ### D77 — the concurrency contract
///
/// **An `IBackend` is single-threaded. One instance per thread, and never two threads at
/// once.** `Begin` / `Draw` / `Finish` is a state machine over a mutable surface, so this is
/// what the interface has always meant; it was never written down, and M3 renders frames in
/// parallel, so it is written down here where an implementer reads it. An instance may be
/// *reused* serially — `Begin` starts a new document — and both built backends support that.
///
/// **What crosses threads is a renderer function**, `Extent<px> -> Node -> 'a`: the shape
/// `SvgBackend.render` and `SkiaBackend.renderPng` already have, and the shape `Chart.render`
/// and `Frames.render` consume. Such a function **must be safe to call concurrently**, and the
/// way both built renderers achieve it is to construct their backend *inside* the call, so no
/// instance is ever shared. A backend author adding a third renderer either does the same or
/// says in its doc comment that it does not.
///
/// **`Measurer` is the exception, and it is deliberate.** An `ITextMeasurer` is required to be
/// thread-safe (see above) precisely so that one of them can be shared by every frame of a
/// render while the backends around it are not.
type IBackend =
    abstract Caps: Capabilities
    abstract Measurer: ITextMeasurer
    abstract Begin: Extent<px> -> unit
    abstract Draw: Node -> unit
    abstract Finish: unit -> unit

/// One file that a rendered document refers to but does not contain.
///
/// `Bytes` is compared structurally and that is O(bytes), for the same reason `Image` carries
/// the warning it does (§5.2). Compare `Name` first if you are diffing.
[<NoComparison>]
type RenderedAsset =
    { /// A file name, not a path. The backend that emitted it chose it; the caller decides
      /// which directory it lands in, because a `.tex` and its sidecar have to agree on a
      /// relative name and only the caller knows where the `.tex` is going.
      Name: string
      Bytes: byte[] }

/// A document that does not stand alone: the text, plus the files it refers to.
///
/// **D64 — this ships with a producer.** `renderWithAssets` was named at M1 as "the M2 door"
/// for TikZ's `Rasters = false`, and D10 records what happens to a facility with no caller:
/// `Capabilities` was "a record nobody reads" until the TikZ backend needed it. So this type
/// arrives in the same milestone as `Chart.image`, which is the thing that finally puts a
/// `Raster` node into a scene — nothing above the IR produced one before M2.5, which is why
/// `golden/figure-heatmap.svg` contains no raster and never did.
///
/// **Not a member of `IBackend`.** A backend that needs no sidecar does not implement this and
/// is not made to: the SVG backend keeps inlining a `data:` URI, because a single
/// self-contained file is the property that makes SVG the notebook and golden format. The
/// asymmetry is deliberate and is stated here rather than discovered.
///
/// `Assets` is deterministic in **both** name and order. Two renders of one figure that
/// produced `plot-0.png` and `plot-1.png` in swapped order would be a golden diff for no
/// reason, so names are assigned in traversal order and the array is emitted in that order.
[<NoComparison>]
type RenderedDocument =
    { Document: string
      Assets: RenderedAsset[] }

[<RequireQualifiedAccess>]
module Capabilities =

    /// Everything supported, every field `true`.
    ///
    /// It must stay that way. An earlier version had `Semantics = false` here, which is a
    /// trap rather than a default: `Degrade.forCaps` strips every `Annotated` node when
    /// `Semantics` is false, so a backend author starting from `full` would silently lose all
    /// of §5.3's accessibility and hit-testing output and have no reason to suspect it.
    ///
    /// A preset named `full` that is not full is worse than no preset. Advertise less by
    /// starting from `vector` or by overriding explicitly.
    let full =
        { Gradients = true
          RadialGradients = true
          Clipping = true
          Dashes = true
          Rasters = true
          TextShaping = true
          RotatedText = true
          Semantics = true
          GroupOpacity = true
          MaxBatch = None }

    /// The realistic starting point for a vector backend (SVG, TikZ, PDF): everything except
    /// text shaping, which such a backend delegates to whatever finally renders the file.
    let vector = { full with TextShaping = false }

/// §5 — Capability degradation.
///
/// Without this, `Capabilities` is a field nobody reads. These rewrites are the difference
/// between "TikZ silently drops your gradient" and "TikZ renders a defensible approximation
/// of it". Each one is lossy on purpose and each one is visible in the output rather than
/// silent.
[<RequireQualifiedAccess>]
module Degrade =

    /// Collapse a gradient to the solid colour at its midpoint. Crude, but it preserves hue
    /// and rough lightness, which is what a fill is usually carrying.
    let private flattenPaint (p: Paint) =
        match p with
        | Solid _ -> p
        | LinearGradient(_, _, stops)
        | RadialGradient(_, _, stops) ->
            if stops.Length = 0 then
                Solid Color.transparent
            else
                let mid = Interp.stops (stops |> Array.map (fun s -> s.Offset, s.Color)) 0.5
                Solid mid

    /// Multiply a constant into every alpha a subtree paints with — D67's approximation of
    /// group opacity, for a target that cannot composite a subtree and fade the result.
    ///
    /// This is D11's `Color.A` route, applied to a whole group at once, and it is the
    /// approximation `Node.Opacity` names: identical to true group opacity everywhere the
    /// group's shapes do not overlap, and darker than it where they do, because each shape is
    /// composited separately and an overlap is therefore painted twice.
    ///
    /// Rasters are included. Skipping them would be cheaper — it is the one case that copies a
    /// buffer rather than rewriting a record — but a half-faded group containing a heatmap
    /// whose heatmap does not fade is the *visible* kind of wrong, and the approximation is
    /// only defensible while it is uniform.
    /// **Public because a backend needs it directly.** `IBackend.Draw` can be driven with an
    /// un-degraded scene, so a backend whose `GroupOpacity` is false still meets an `Opacity`
    /// node and has to do something with it. Re-deriving this fold there would put a second
    /// implementation of one approximation in the tree, and the TikZ backend's version would
    /// be the subtly wrong one: pgf's scope `opacity=` is *inherited* by each path and is
    /// therefore **overridden** by a path's own `fill opacity=`, so a subtree that already
    /// carries alpha would come out unfaded exactly where it was already translucent.
    let foldOpacity (a: float) (n: Node) : Node =
        let a = if a < 0.0 then 0.0 elif a > 1.0 then 1.0 else a

        if a >= 1.0 then
            n
        else
            let colour (c: Color) = { c with A = c.A * a }

            let paint (p: Paint) =
                match p with
                | Solid c -> Solid(colour c)
                | LinearGradient(f, u, stops) ->
                    LinearGradient(f, u, stops |> Array.map (fun s -> { s with Color = colour s.Color }))
                | RadialGradient(centre, r, stops) ->
                    RadialGradient(centre, r, stops |> Array.map (fun s -> { s with Color = colour s.Color }))

            let stroke (s: Stroke) = { s with Paint = paint s.Paint }

            n
            |> Scene.transform (fun node ->
                match node with
                | Path(segs, st, fill, rule) -> Path(segs, Option.map stroke st, Option.map paint fill, rule)
                | Text t -> Text { t with Paint = paint t.Paint }
                | Markers b ->
                    Markers
                        { b with
                            Fill = Option.map paint b.Fill
                            Stroke = Option.map stroke b.Stroke }
                | Raster r ->
                    let src = r.Image.Rgba

                    if isNull src then
                        node
                    else
                        let dst = Array.copy src
                        let mutable i = 3

                        while i < dst.Length do
                            dst.[i] <- byte (System.Math.Round(float dst.[i] * a))
                            i <- i + 4

                        Raster { r with Image = { r.Image with Rgba = dst } }
                | other -> other)

    let private degradeStroke (caps: Capabilities) (s: Stroke) =
        let s =
            if caps.Gradients then
                s
            else
                { s with Paint = flattenPaint s.Paint }

        if caps.Dashes then s else { s with Dash = [||]; DashOffset = 0.0<px> }

    /// Rewrite a scene so it uses only what the backend can actually do.
    ///
    /// Clipping is the one case with no honest fallback: dropping the clip draws outside the
    /// panel, and rasterizing the region needs a backend that can rasterize. We drop it and
    /// rely on the compiler layer having already culled out-of-domain data — which it does,
    /// because `Scale.Forward` does not clamp (see Scale.fs).
    let forCaps (caps: Capabilities) (n: Node) : Node =
        let rewrite (node: Node) =
            match node with
            | Path(segs, stroke, fill, rule) ->
                let stroke = stroke |> Option.map (degradeStroke caps)

                let fill =
                    match fill with
                    | Some p when not caps.Gradients -> Some(flattenPaint p)
                    | Some(RadialGradient _ as p) when not caps.RadialGradients -> Some(flattenPaint p)
                    | f -> f

                Path(segs, stroke, fill, rule)

            | Markers b ->
                Markers
                    { b with
                        Fill = b.Fill |> Option.map (fun p -> if caps.Gradients then p else flattenPaint p)
                        Stroke = b.Stroke |> Option.map (degradeStroke caps) }

            | Text t when not caps.RotatedText && t.Rotation <> 0.0<rad> -> Text { t with Rotation = 0.0<rad> }

            | Text t when not caps.Gradients -> Text { t with Paint = flattenPaint t.Paint }

            | Raster _ when not caps.Rasters -> Scene.empty

            | Clip(_, child) when not caps.Clipping -> child

            // D67. Note that this fires *bottom-up*, so nested opacities compose the way they
            // read: an inner `Opacity(0.5, x)` is already folded into `x`'s paints by the time
            // the enclosing `Opacity(0.4, _)` gets here, and the result carries 0.2.
            | Opacity(a, child) when not caps.GroupOpacity -> foldOpacity a child

            | Annotated(_, child) when not caps.Semantics -> child

            | other -> other

        Scene.transform rewrite n

    /// Split oversized marker batches to fit `MaxBatch`. Keeps §5.1's one-node-per-batch
    /// promise everywhere it can be kept, and breaks it in the smallest possible way where
    /// hardware forces the issue.
    let splitBatches (caps: Capabilities) (n: Node) : Node =
        match caps.MaxBatch with
        | None -> n
        | Some maxBatch when maxBatch <= 0 -> n
        | Some maxBatch ->
            n
            |> Scene.transform (fun node ->
                match node with
                | Markers b when b.Points.Length > maxBatch ->
                    Group(b.Points |> Array.chunkBySize maxBatch |> Array.map (fun chunk -> Markers { b with Points = chunk }))
                | other -> other)

/// A zero-dependency text measurer built on the Helvetica advance-width table.
///
/// This is an *approximation*, and calling it one is important: it has no kerning, no
/// ligatures, no shaping, and it assumes the renderer picks a metric-compatible face. It is
/// accurate to a few percent for Latin text, which is enough for axis-label collision
/// avoidance and legend sizing.
///
/// It exists because `Marey.Core` may not take a dependency (§13) and because layout cannot
/// wait for M2's SkiaSharp. `Capabilities.TextShaping = false` is the honest advertisement of
/// what this is.
[<Sealed>]
type ApproxTextMeasurer(?dpi: float<px / mm>) =

    let dpi = defaultArg dpi Space.screenDpi

    /// Helvetica advance widths per 1000 units of em, for ASCII 32..126.
    static let widths =
        [| 278; 278; 355; 556; 556; 889; 667; 191; 333; 333; 389; 584; 278; 333; 278; 278 // 32..47
           556; 556; 556; 556; 556; 556; 556; 556; 556; 556; 278; 278; 584; 584; 584; 556 // 48..63
           1015; 667; 667; 722; 722; 667; 611; 778; 722; 278; 500; 667; 556; 833; 722; 778 // 64..79
           667; 778; 722; 667; 611; 722; 667; 944; 667; 667; 611; 278; 278; 278; 469; 556 // 80..95
           333; 556; 556; 500; 556; 556; 278; 556; 556; 222; 222; 500; 222; 833; 556; 556 // 96..111
           556; 556; 333; 500; 278; 556; 500; 722; 500; 500; 500; 334; 260; 334; 584 |] // 112..126

    /// Ascent and descent as fractions of the em, from Helvetica's own metrics.
    [<Literal>]
    static let AscentEm = 0.718

    [<Literal>]
    static let DescentEm = 0.207

    /// Width for characters outside the table. Unit symbols are full of these — `·`, `²`,
    /// `⁻`, `Ω`, `µ`, `Å` — so guessing badly here shows up directly in axis-label layout.
    static let fallbackWidth (c: char) =
        match c with
        | '·'
        | ' ' // thin space
        | ' ' -> 278 // non-breaking space
        | '²'
        | '³'
        | '¹' -> 333 // superscript 2, 3, 1
        | c when c >= '⁰' && c <= 'ⁿ' -> 333 // superscript digits and signs
        | c when c >= 'Ͱ' && c <= 'Ͽ' -> 600 // Greek
        | c when c >= '←' && c <= '⇿' -> 800 // arrows
        | '°' -> 400 // degree
        | 'Å' -> 667 // angstrom
        | _ -> 556

    /// Family-shape correction. Times is narrower than Helvetica by roughly 8%; a monospace
    /// face is a flat 600/1000 em by definition.
    static let familyFactor (family: string) =
        let f = family.ToLowerInvariant()

        if f.Contains "mono" || f.Contains "courier" || f.Contains "consol" then
            0.0 // sentinel: use the fixed advance instead
        elif f.Contains "serif" && not (f.Contains "sans") then
            0.92
        elif f.Contains "times" || f.Contains "georgia" || f.Contains "garamond" then
            0.92
        else
            1.0

    interface ITextMeasurer with
        member _.Measure (font: FontSpec) (text: string) =
            let sizePx = Space.pxOfPt dpi font.Size
            let factor = familyFactor font.Family

            let permille =
                if factor = 0.0 then
                    600 * text.Length
                else
                    let mutable total = 0

                    for c in text do
                        let w =
                            if c >= ' ' && c <= '~' then
                                widths.[int c - 32]
                            else
                                fallbackWidth c

                        total <- total + w

                    total

            // Bold faces run wider. 3% is the usual difference between Helvetica and
            // Helvetica-Bold across a mixed-case string.
            let weightFactor = if font.Weight >= 600 then 1.03 else 1.0
            let scale = (if factor = 0.0 then 1.0 else factor) * weightFactor

            { Advance = float permille / 1000.0 * scale * sizePx
              Ascent = AscentEm * sizePx
              Descent = DescentEm * sizePx }

[<RequireQualifiedAccess>]
module TextMeasure =

    /// The default measurer. Shared and stateless.
    let approx: ITextMeasurer = ApproxTextMeasurer() :> ITextMeasurer

    /// Bounding box of a text run in device space, honouring anchor, baseline and rotation.
    ///
    /// Layout's actual question is never "how wide is this string" but "does this label
    /// overlap that one", and answering it consistently in one place is what stops rotated
    /// tick labels from being subtly wrong in every backend independently.
    let bounds (m: ITextMeasurer) (font: FontSpec) (anchor: TextAnchor) (baseline: TextBaseline) (rotation: float<rad>) (text: string) : Rect<px> =
        let tm = m.Measure font text

        let x0 =
            match anchor with
            | AnchorStart -> 0.0<px>
            | AnchorMiddle -> -tm.Advance / 2.0
            | AnchorEnd -> -tm.Advance

        let y0, y1 =
            match baseline with
            | BaselineAlphabetic -> -tm.Ascent, tm.Descent
            | BaselineMiddle ->
                let h = tm.Ascent + tm.Descent
                -h / 2.0, h / 2.0
            | BaselineHanging -> 0.0<px>, tm.Ascent + tm.Descent

        let r: Rect<px> =
            { X0 = x0
              Y0 = y0
              X1 = x0 + tm.Advance
              Y1 = y1 }

        if rotation = 0.0<rad> then
            r
        else
            Affine.mapRectBounds (Affine.rotate<px> rotation) r
