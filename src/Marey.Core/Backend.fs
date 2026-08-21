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
