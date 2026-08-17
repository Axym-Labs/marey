namespace Quiver

open System
open System.Text

// ======================================================================================
// THE TIKZ BACKEND.
//
// §6: "Emits `\begin{tikzpicture}`. Figures that inherit the document's fonts and `siunitx`
// units. For a physics/maths audience this is a killer feature almost nobody offers, and it is
// *cheap* — it's a text emitter."
//
// Written to be read next to `SvgBackend.fs`. The two are deliberately structurally identical —
// same traversal shape, same definition-dedup mechanism, same "every container in the output
// carries meaning" rule — because then they fail in the same ways and a reviewer who knows one
// can read the other. Where they differ, they differ for a stated reason, and there are four.
//
// ---------------------------------------------------------------------------------------
// 1. TEX'S CANVAS IS Y-UP, SO THERE IS NO FLIP.
// ---------------------------------------------------------------------------------------
//
// The IR is entirely in `nd`, y-up. The SVG backend therefore builds one `Affine<nd,px>` in
// `Begin` that scales *and* flips y, because SVG's device space is y-down. **TeX's is y-up, like
// `nd`**, so this backend's root transform is a pure scale into points and nothing is inverted.
//
// That is a smaller change than it sounds and a bigger one than it looks. Smaller: one matrix.
// Bigger: it removes the mirror from the text-rotation problem. D13 has the SVG backend negating
// a run's own rotation when `det(CTM) < 0`, because the y flip alone mirrors and a mirror reverses
// the sense of a rotation. Here the determinant is positive, TikZ's `rotate=` is
// counter-clockwise-positive in a y-up space, and `TextRun.Rotation` in `rad` converted to degrees
// is *directly* the right number.
//
// So the two backends reach the same visual result through opposite arithmetic, which makes a
// cross-backend rotation test load-bearing rather than redundant. Same for `MarkTriangleUp`: SVG
// puts its apex at negative y, this backend at positive y, and both point up on the page.
//
// ---------------------------------------------------------------------------------------
// 2. GEOMETRY IS PRE-TRANSFORMED; NO PICTURE-LEVEL `scale` IS EMITTED.
// ---------------------------------------------------------------------------------------
//
// D15's decision, for a stronger reason. TikZ's `scale=` multiplies coordinates but leaves
// `line width`, `dash pattern` and node font sizes alone. Since Scene.fs puts stroke widths and
// marker sizes in **px** precisely so a hairline stays a hairline at any output size, combining a
// picture scale with them produces widths that are right only by accident and marker sizes that
// are right only at scale 1.
//
// So coordinates are baked into device points and written out as `bp`. A bp is a bp everywhere in
// the output, and — by construction — the emitted numbers are the SVG backend's numbers times
// 72/96, with y measured from the bottom instead of the top.
//
// ---------------------------------------------------------------------------------------
// 3. NODES NEED THEIR PADDING TURNED OFF, OR EVERY LABEL MOVES.
// ---------------------------------------------------------------------------------------
//
// A TikZ node has `inner sep` (0.3333em by default) and `outer sep`, so `\node[anchor=base west]
// at (0,0)` does *not* put the text's baseline-left at the origin — it puts it a third of an em
// away. Every axis label in the figure would sit slightly off the position layout computed for it,
// consistently and invisibly.
//
// Both are zeroed in the picture options. Picture options are applied after any style the host
// document set, so this wins over a document-level `every node` style rather than fighting it.
//
// ---------------------------------------------------------------------------------------
// 4. THE BOUNDING BOX IS DECLARED, NOT INFERRED.
// ---------------------------------------------------------------------------------------
//
// A tikzpicture's natural size is the union of what it drew. Two figures of the same requested
// extent whose content happens to reach different distances from the edge would therefore come out
// at different sizes — and neither would match the SVG at the same `Extent<px>`. `\useasboundingbox`
// pins the picture to exactly the requested rectangle, which is what makes "the same figure, two
// backends" true rather than approximately true.
// ======================================================================================

/// How much of the host document's typography a figure inherits.
type TikzFontMode =
    /// Emit `\fontsize{s}{1.2s}\selectfont` so the size layout assumed is the size TeX uses, and
    /// let the *family* come from the document. The honest middle, and the default.
    ///
    /// §6 sells this backend on figures that "inherit the document's fonts", and taken literally —
    /// emit no font information at all — the figure is typeset in a face whose metrics have nothing
    /// to do with the `ApproxTextMeasurer` widths that the measure–arrange pass and D23's collision
    /// ladder were computed against. Labels would collide in the PDF while layout believed they fit.
    | PinSize
    /// Emit no font information whatsoever: the figure takes the document's size as well as its
    /// family. Maximum integration, and the caller accepts that layout was computed against a
    /// different size than the one used.
    | InheritFont

[<NoComparison>]
type TikzOptions =
    { /// Resolution used to turn the px-denominated parts of the IR — stroke widths, marker
      /// sizes, the surface extent — into points. The default matches `ApproxTextMeasurer`, so
      /// the measurer layout consults and the sizes that land in the document agree.
      Dpi: float<px / mm>
      FontMode: TikzFontMode
      /// Emit `%` comments for `Keyed` and `Annotated` nodes. On by default: it costs a line,
      /// keeps a golden diff readable, and is how §5.3's structure survives into a format with no
      /// `aria-label`.
      Comments: bool }

    static member Default =
        { Dpi = Space.screenDpi
          FontMode = PinSize
          Comments = true }

/// Renders a `Node` to a `tikzpicture`.
///
/// The rendered picture is exposed by the concrete type through `Result`, because
/// `IBackend.Finish` returns unit by design (Backend.fs): backends produce genuinely different
/// artifacts and only the protocol is shared.
///
/// One instance may be reused: `Begin` resets all state, so the same backend can render a
/// sequence of scenes, and two renders of the same scene produce byte-identical strings.
[<Sealed>]
type TikzBackend(?options: TikzOptions) =

    let opts = defaultArg options TikzOptions.Default

    let measurer = ApproxTextMeasurer(opts.Dpi) :> ITextMeasurer

    let doc = TikzDocument()

    let mutable started = false
    let mutable finished = false
    let mutable cached: string = null

    /// The nd → pt map established in `Begin`. A pure scale: no flip (see note 1).
    let mutable root: Affine<nd, pt> =
        Affine.scaleUniform (LanguagePrimitives.FloatWithMeasure<pt / nd> 1.0)

    let sb = doc.Body

    // ---- small emission helpers ------------------------------------------------------

    let indent (depth: int) =
        if depth <= 0 then "" else String(' ', depth * 2)

    let requireStarted () =
        if not started then
            invalidOp "Quiver.TikzBackend: Begin must be called before Draw."

        if finished then
            invalidOp "Quiver.TikzBackend: Draw cannot be called after Finish; call Begin again to start a new document."

    /// px → bp, for the parts of the IR that are in device units (D12).
    let bp (v: float<px>) = float (Space.ptOfPx opts.Dpi v)

    // ---- geometry --------------------------------------------------------------------

    /// A panel-space point in device points under the current transform.
    let point (ctm: Affine<nd, pt>) (p: Vec2<nd>) =
        let q = Affine.apply ctm p
        struct (float q.X, float q.Y)

    let coordOf (ctm: Affine<nd, pt>) (p: Vec2<nd>) =
        let struct (x, y) = point ctm p
        Tikz.coord x y

    /// Path data.
    ///
    /// A bare coordinate starts a new subpath in TikZ, which is exactly a moveto, so a multi-
    /// subpath `Path` node needs no special handling beyond not writing an operator before it.
    let pathData (ctm: Affine<nd, pt>) (segs: PathSeg[]) =
        let d = StringBuilder(segs.Length * 20)
        let mutable atStart = true

        for s in segs do
            match s with
            | MoveTo p ->
                if not atStart then
                    d.Append ' ' |> ignore

                d.Append(coordOf ctm p) |> ignore
                atStart <- false
            | LineTo p ->
                d.Append(" -- ").Append(coordOf ctm p) |> ignore
                atStart <- false
            | CubicTo(c1, c2, p) ->
                d.Append(" .. controls ").Append(coordOf ctm c1) |> ignore
                d.Append(" and ").Append(coordOf ctm c2) |> ignore
                d.Append(" .. ").Append(coordOf ctm p) |> ignore
                atStart <- false
            | ClosePath ->
                d.Append(" -- cycle") |> ignore
                atStart <- false

        d.ToString()

    // ---- paint -----------------------------------------------------------------------

    /// Collapse a gradient to the solid colour at its midpoint.
    ///
    /// The same rule `Degrade.forCaps` applies, and normally it has already applied it: this
    /// backend advertises `Gradients = false`, and both `TikzBackend.render` and
    /// `Chart.renderTo` degrade before drawing. This is here for the caller who drives
    /// `IBackend` by hand and skips that step, so that the output is a defensible approximation
    /// rather than a crash or a silently missing fill.
    let flatten (p: Paint) =
        match p with
        | Solid c -> c
        | LinearGradient(_, _, stops)
        | RadialGradient(_, _, stops) ->
            if stops.Length = 0 then
                Color.transparent
            else
                Interp.stops (stops |> Array.map (fun s -> s.Offset, s.Color)) 0.5

    /// The name of a colour, defining it if this is the first time it has been seen.
    let colorName (c: Color) =
        doc.Def("qc", fun id -> Tikz.colorDefinition "  " id c)

    // ---- stroke and fill options -----------------------------------------------------

    let capName (c: LineCap) =
        match c with
        | Butt -> None // TikZ's default
        | Round -> Some "round"
        | Square -> Some "rect"

    let joinName (j: LineJoin) =
        match j with
        | Miter -> None // TikZ's default
        | RoundJoin -> Some "round"
        | Bevel -> Some "bevel"

    /// A TikZ dash pattern.
    ///
    /// SVG repeats an odd-length dash array to make it even; TikZ needs explicit on/off pairs, so
    /// the same doubling is done here rather than emitting a pattern that ends mid-phase and
    /// diverges from the SVG rendering of the same scene.
    let dashPattern (dash: float<px>[]) =
        let arr =
            if dash.Length % 2 = 1 then Array.append dash dash else dash

        arr
        |> Array.mapi (fun i d -> (if i % 2 = 0 then "on " else "off ") + Tikz.dim (bp d))
        |> String.concat " "

    let strokeOptions (s: Stroke) =
        [ yield "draw=" + colorName (flatten s.Paint)

          let a = (flatten s.Paint).A

          if a < 1.0 then
              yield "draw opacity=" + Tikz.ratio a

          yield "line width=" + Tikz.dim (bp s.Width)

          if s.Dash.Length > 0 then
              yield "dash pattern=" + dashPattern s.Dash

              if float s.DashOffset <> 0.0 then
                  yield "dash phase=" + Tikz.dim (bp s.DashOffset)

          match capName s.Cap with
          | Some c -> yield "line cap=" + c
          | None -> ()

          match joinName s.Join with
          | Some j -> yield "line join=" + j
          | None -> ()

          if s.Join = Miter && s.MiterLimit <> 4.0 then
              yield "miter limit=" + Tikz.num s.MiterLimit ]

    let fillOptions (p: Paint) =
        let c = flatten p

        [ yield "fill=" + colorName c

          if c.A < 1.0 then
              yield "fill opacity=" + Tikz.ratio c.A ]

    let optionList (opts: string list) =
        if List.isEmpty opts then
            ""
        else
            "[" + String.Join(",", opts) + "]"

    // ---- markers ---------------------------------------------------------------------

    // D33 / §5.1. One macro per distinct (shape, size), then one call per point inside a scope
    // that carries the batch's paint. The direct mirror of SVG's `<defs>` + `<use>` + `<g>`.
    //
    // `\def` rather than `\providecommand`, and this matters: `\providecommand` does *not*
    // redefine an existing macro, so a second picture in the same document whose first batch had
    // a different size would silently reuse the first picture's geometry. `\def` always defines,
    // and since `tikzpicture` is an environment and therefore a TeX group, the definition is
    // local to the picture and cannot leak into the document.
    //
    // Shapes are built around the origin and placed with `shift`, so each definition is a small
    // fixed path exactly as in the SVG `<defs>`. Note the y signs: this space is y-up, so
    // "triangle up" has its apex at *positive* y — the opposite of the SVG backend, and the same
    // picture on the page.
    //
    // **The macro carries the paint *actions*; the scope carries the paint *values*.** This is
    // where the SVG analogy stops holding, and it is not obvious: in TikZ, `fill=<colour>` on a
    // *scope* sets the fill colour but does **not** make paths inside it filled. A bare `\path`
    // inside `\begin{scope}[fill=orange]` compiles perfectly and draws absolutely nothing. So each
    // macro's path carries a bare `fill` and/or `draw` — the action with no value — and inherits
    // the colour, opacity and line width from the enclosing scope.
    //
    // Which actions are needed is therefore part of the macro's identity, alongside shape and
    // size: a fill-only batch and a stroke-only batch of the same shape are different macros.
    //
    // Verified against a real engine by rendering all three combinations to PDF and looking at
    // them. It cost a bug to find, because the broken version compiled without a murmur — see the
    // note on the marker tests.
    let markerMacro (shape: MarkerShape) (size: float) (hasFill: bool) (hasStroke: bool) (id: string) =
        let r = size / 2.0
        let n = Tikz.dim

        // Half-thickness of the arms of a plus or a cross. A twelve-vertex outline rather than
        // two crossing segments, so a batch with a fill and no stroke still draws something.
        let t = r / 3.0

        let poly (pts: (float * float)[]) =
            let d = StringBuilder()

            pts
            |> Array.iteri (fun i (x, y) ->
                if i > 0 then
                    d.Append " -- " |> ignore

                d.Append(Tikz.coord x y) |> ignore)

            d.Append(" -- cycle").ToString()

        let plusPoints =
            [| (-t, -r); (t, -r); (t, -t); (r, -t); (r, t); (t, t); (t, r); (-t, r); (-t, t); (-r, t); (-r, -t); (-t, -t) |]

        let geometry =
            match shape with
            | MarkCircle -> "(0bp,0bp) circle[radius=" + n r + "]"
            | MarkSquare -> Tikz.coord -r -r + " rectangle " + Tikz.coord r r
            | MarkDiamond -> poly [| (0.0, -r); (r, 0.0); (0.0, r); (-r, 0.0) |]
            | MarkTriangleUp ->
                let k = r * sqrt 3.0 / 2.0
                poly [| (0.0, r); (-k, -r / 2.0); (k, -r / 2.0) |]
            | MarkTriangleDown ->
                let k = r * sqrt 3.0 / 2.0
                poly [| (0.0, -r); (k, r / 2.0); (-k, r / 2.0) |]
            | MarkPlus -> poly plusPoints
            | MarkCross ->
                let s2 = sqrt 0.5

                poly (plusPoints |> Array.map (fun (x, y) -> ((x - y) * s2, (x + y) * s2)))

        // The bare actions. Order matches the `Path` case (fill before draw) so a reviewer sees
        // the same convention throughout. A batch with neither a fill nor a stroke paints nothing,
        // which is also what the SVG backend does for `fill="none"` and no stroke.
        let actions =
            [ if hasFill then yield "fill"
              if hasStroke then yield "draw"
              yield "shift={(#1,#2)}" ]

        String.Concat
            [| "  \\def\\"
               id
               "#1#2{\\path"
               optionList actions
               " "
               geometry
               ";}\n" |]

    // ---- text ------------------------------------------------------------------------

    /// The TikZ anchor that puts the requested point of the text box at the coordinate.
    ///
    /// A clean 3x3 over the IR's two independent alignments. `base` rather than `south` for the
    /// alphabetic baseline, because that is what a baseline *is* — `south` is the bottom of the
    /// box, which sits below the descenders.
    let anchorName (a: TextAnchor) (b: TextBaseline) =
        match b, a with
        | BaselineAlphabetic, AnchorStart -> "base west"
        | BaselineAlphabetic, AnchorMiddle -> "base"
        | BaselineAlphabetic, AnchorEnd -> "base east"
        | BaselineMiddle, AnchorStart -> "west"
        | BaselineMiddle, AnchorMiddle -> "center"
        | BaselineMiddle, AnchorEnd -> "east"
        | BaselineHanging, AnchorStart -> "north west"
        | BaselineHanging, AnchorMiddle -> "north"
        | BaselineHanging, AnchorEnd -> "north east"

    /// The `font=` value.
    ///
    /// **No font family is ever emitted, and that is the feature.** §6 sells this backend on
    /// figures that "inherit the document's fonts", and D35 states the split: the *size* is pinned
    /// and the *family* is inherited. Emitting `\rmfamily` because a `Theme` happens to name a
    /// serif stack would override the host document — a figure dropped into a sans-serif paper
    /// would come out in the wrong face, and the one thing this backend exists to do would be the
    /// thing it got wrong.
    ///
    /// So `FontSpec.Family` reaches TeX not at all. It is still load-bearing here: it is what
    /// `ApproxTextMeasurer` reads to pick its width factor, so it decides how much room layout
    /// reserves for a label. In `Theme.tikz` it names a serif stack for exactly that reason — a
    /// 0.92 factor is the closer approximation to Computer Modern — while TeX supplies the face.
    ///
    /// Series and shape *are* emitted: `\bfseries` and `\itshape` are orthogonal to family, so a
    /// bold title stays bold in any document, which is what a theme asking for bold means.
    ///
    /// `FontSpec.Size` goes to `\fontsize` **unconverted**, deliberately rather than as an
    /// oversight about D28: TeX's `pt` is 1/72.27 inch against Core's 1/72, but a font size is a
    /// typographic request handed to the typesetter, not a length being drawn. The consequence is
    /// that TeX sets the text 0.37% smaller than layout measured it — the safe direction, since a
    /// label narrower than measured cannot collide with its neighbour.
    let fontOption (f: FontSpec) =
        match opts.FontMode with
        | InheritFont -> None
        | PinSize ->
            let series = if f.Weight >= 600 then "\\bfseries" else ""
            let shape = if f.Italic then "\\itshape" else ""
            let size = float f.Size

            Some(
                String.Concat
                    [| series
                       shape
                       "\\fontsize{"
                       Tikz.num size
                       "}{"
                       Tikz.num (size * 1.2)
                       "}\\selectfont" |]
            )

    // ---- semantics -------------------------------------------------------------------

    let roleName (role: SemanticRole) =
        match role with
        | RoleSeries -> "series"
        | RoleDatum -> "datum"
        | RoleAxis -> "axis"
        | RoleLegend -> "legend"
        | RoleAnnotation -> "annotation"
        | RoleDecoration -> "decoration"
        | RoleOther s -> "other:" + (if isNull s then "" else s)

    // ---- traversal -------------------------------------------------------------------

    let rec draw (ctm: Affine<nd, pt>) (depth: int) (node: Node) =
        match node with

        | Path(segs, stroke, fill, rule) ->
            if segs.Length > 0 then
                let options =
                    [ match fill with
                      | Some p -> yield! fillOptions p
                      | None -> ()

                      match stroke with
                      | Some s -> yield! strokeOptions s
                      | None -> ()

                      match rule with
                      | NonZero -> () // TikZ's default
                      | EvenOdd -> yield "even odd rule" ]

                // A path with neither a stroke nor a fill draws nothing in TikZ, exactly as in
                // SVG, so it is emitted as-is rather than special-cased: the output stays a
                // faithful transcript of the scene.
                sb
                    .Append(indent depth)
                    .Append("\\path")
                    .Append(optionList options)
                    .Append(' ')
                    .Append(pathData ctm segs)
                    .Append(";\n")
                |> ignore

        | Text t -> drawText ctm depth t

        | Markers b -> drawMarkers ctm depth b

        // D31: `Rasters = false`, so `Degrade.forCaps` normally removes these before they arrive.
        // A visible comment rather than silence, in the spirit of the SVG backend's raster
        // diagnostics: the reviewer of a golden diff sees why nothing was drawn.
        | Raster r ->
            sb.Append(
                Tikz.comment
                    (indent depth)
                    (sprintf "quiver: raster %dx%d skipped -- the TikZ backend cannot embed images" r.Image.Width r.Image.Height)
            )
            |> ignore

        // A Group is §5's identity-carrying container and has no rendering effect, so it emits
        // nothing. Every scope in the output therefore carries meaning.
        | Group ns ->
            for child in ns do
                draw ctm depth child

        // Composed into the current transform rather than emitted. See note 2 at the top.
        | Transform(a, child) -> draw (Affine.compose a ctm) depth child

        | Clip(segs, child) ->
            if segs.Length = 0 then
                // An empty clip path clips everything away. The scope is still emitted so the
                // output stays faithful to the scene rather than quietly reinterpreting it.
                sb.Append(indent depth).Append("\\begin{scope}\n") |> ignore
                sb.Append(Tikz.comment (indent (depth + 1)) "quiver: empty clip -- nothing inside is visible") |> ignore
                sb.Append(indent (depth + 1)).Append("\\clip (0bp,0bp) rectangle (0bp,0bp);\n") |> ignore
                draw ctm (depth + 1) child
                sb.Append(indent depth).Append("\\end{scope}\n") |> ignore
            else
                sb.Append(indent depth).Append("\\begin{scope}\n") |> ignore

                sb
                    .Append(indent (depth + 1))
                    .Append("\\clip ")
                    .Append(pathData ctm segs)
                    .Append(";\n")
                |> ignore

                draw ctm (depth + 1) child
                sb.Append(indent depth).Append("\\end{scope}\n") |> ignore

        // §5.2 — stable identity. There is no `id` attribute in TeX, so the key becomes a comment:
        // it survives into the output for a reviewer and for anyone diffing two pictures, which is
        // what the key is for at this stage.
        | Keyed(Key name, child) ->
            if opts.Comments then
                sb.Append(Tikz.comment (indent depth) ("quiver-key: " + name)) |> ignore

            draw ctm depth child

        | Annotated(s, child) ->
            if opts.Comments then
                let label =
                    if String.IsNullOrEmpty s.Label then
                        ""
                    else
                        " -- " + s.Label

                sb.Append(Tikz.comment (indent depth) ("quiver-role: " + roleName s.Role + label))
                |> ignore

                for (k, v) in s.Data do
                    sb.Append(Tikz.comment (indent depth) ("quiver-data: " + k + " = " + v)) |> ignore

            draw ctm depth child

    and drawText (ctm: Affine<nd, pt>) (depth: int) (t: TextRun) =
        let content = Tikz.runContent t.Tex t.Text

        if content <> "" then
            let options =
                [ yield "anchor=" + anchorName t.Anchor t.Baseline

                  let c = flatten t.Paint
                  yield "text=" + colorName c

                  if c.A < 1.0 then
                      yield "text opacity=" + Tikz.ratio c.A

                  // ROTATION. See note 1: TikZ's `rotate` is counter-clockwise-positive in a
                  // y-up space, which is the same convention `TextRun.Rotation` uses, and the
                  // root transform has a positive determinant, so — unlike the SVG backend —
                  // there is no mirror to compensate for and no sign to flip.
                  //
                  // The frame's own rotation still contributes, taken from the CTM as the angle
                  // of the image of the x axis. It is read from the matrix rather than from the
                  // requested angle because a `Transform` node may have rotated the panel.
                  let frameDegrees = atan2 (float ctm.M21) (float ctm.M11) * 180.0 / Math.PI
                  let ownDegrees = float t.Rotation * 180.0 / Math.PI
                  let degrees = frameDegrees + ownDegrees

                  if Tikz.num degrees <> "0" then
                      yield "rotate=" + Tikz.num degrees

                  match fontOption t.Font with
                  | Some f -> yield "font=" + f
                  | None -> () ]

            sb
                .Append(indent depth)
                .Append("\\node")
                .Append(optionList options)
                .Append(" at ")
                .Append(coordOf ctm t.At)
                .Append(" {")
                .Append(content)
                .Append("};\n")
            |> ignore

    and drawMarkers (ctm: Affine<nd, pt>) (depth: int) (b: MarkerBatch) =
        if b.Points.Length > 0 then
            let size = bp b.Size

            // The paint actions are part of the macro's identity, not just its shape and size —
            // see the note on `markerMacro`. `Def` dedups on rendered content, so a fill-only and
            // a stroke-only batch of the same shape correctly get separate macros.
            let id =
                doc.Def("quivermark", markerMacro b.Shape size b.Fill.IsSome b.Stroke.IsSome)

            let options =
                [ match b.Fill with
                  | Some p -> yield! fillOptions p
                  | None -> ()

                  match b.Stroke with
                  | Some s -> yield! strokeOptions s
                  | None -> () ]

            sb
                .Append(indent depth)
                .Append("\\begin{scope}")
                .Append(optionList options)
                .Append('\n')
            |> ignore

            let inner = indent (depth + 1)

            for p in b.Points do
                let struct (x, y) = point ctm p

                sb
                    .Append(inner)
                    .Append('\\')
                    .Append(id)
                    .Append('{')
                    .Append(Tikz.dim x)
                    .Append("}{")
                    .Append(Tikz.dim y)
                    .Append("}\n")
                |> ignore

            sb.Append(indent depth).Append("\\end{scope}\n") |> ignore

    // ---- public surface --------------------------------------------------------------

    /// The rendered picture.
    ///
    /// Available as soon as `Begin` has been called, and stable afterwards: calling it before
    /// `Finish` composes whatever has been drawn so far, which is what makes a partial render
    /// debuggable.
    member _.Result: string =
        if not started then
            invalidOp "Quiver.TikzBackend: Begin must be called before Result."

        if isNull cached then
            let s = doc.Compose()

            if finished then
                cached <- s

            s
        else
            cached

    /// The surface size passed to `Begin`, as an nd → pt map. Exposed for tests, which need to
    /// check emitted coordinates against the transform that produced them.
    member _.RootTransform: Affine<nd, pt> = root

    interface IBackend with

        /// D31. Every `false` here is a thing this backend genuinely cannot do, not a switch.
        member _.Caps =
            { // PGF shadings are two-stop and axis-constrained. A multi-stop
              // `\pgfdeclarehorizontalshading` covers only the axis-aligned linear case, so
              // advertising `true` and quietly emitting a two-stop approximation of a five-stop
              // ramp is exactly the "silently producing wrong output" §5 exists to prevent.
              // `Degrade.forCaps` collapses a gradient to its Oklab midpoint instead — and this
              // is the first backend that actually needs it, which retires D10 from being a
              // facility nobody reads.
              Gradients = false
              RadialGradients = false
              Clipping = true
              Dashes = true
              // An honest raster needs a sidecar .png and `\includegraphics`, which breaks the
              // one-string-output contract and adds a graphicx requirement. Heatmaps are M2/M4
              // work; `renderWithAssets` is the door.
              Rasters = false
              // The whole point: shaping is delegated to TeX, which is better at it than we are.
              TextShaping = false
              RotatedText = true
              // Emitted as `%` comments. Cheap, keeps a golden diff readable, and decisively:
              // `Degrade.forCaps` strips every `Annotated` node when this is false, which would
              // discard the structure the golden diff is there to review.
              Semantics = true
              // A text file has no instance-buffer ceiling, and §5.1's whole point is that one
              // batch stays one batch.
              MaxBatch = None }

        member _.Measurer = measurer

        member _.Begin(extent: Extent<px>) =
            doc.Reset()
            started <- true
            finished <- false
            cached <- null

            let w = Space.ptOfPx opts.Dpi extent.Width
            let h = Space.ptOfPx opts.Dpi extent.Height

            // No flip: `nd` is y-up and so is TeX. See note 1.
            root <-
                Affine.fromRects
                    Rect.unit
                    { X0 = 0.0<pt>
                      Y0 = 0.0<pt>
                      X1 = w
                      Y1 = h }

            // See notes 3 and 4 for why both of these are not optional.
            doc.Header <- "\\begin{tikzpicture}[every node/.style={inner sep=0bp,outer sep=0bp}]\n"

            doc.Body
                .Append("  \\useasboundingbox (0bp,0bp) rectangle ")
                .Append(Tikz.coord (float w) (float h))
                .Append(";\n")
            |> ignore

            doc.Footer <- "\\end{tikzpicture}\n"

        member _.Draw(node: Node) =
            requireStarted ()
            cached <- null
            draw root 1 node

        member _.Finish() =
            if not started then
                invalidOp "Quiver.TikzBackend: Begin must be called before Finish."

            finished <- true
            cached <- null

/// Convenience entry points. The interface is what a heterogeneous list of backends is driven
/// through; these are what a caller with one backend actually wants.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
[<RequireQualifiedAccess>]
module TikzBackend =

    /// The packages a host document needs before it can `\input` a Quiver picture.
    ///
    /// `siunitx` is what makes `UnitStyle.Siunitx` axis titles typeset. `textcomp` has been part
    /// of the LaTeX kernel since 2020 and loading it is a no-op on a current distribution, but it
    /// is cheap insurance on an older one and it documents which macros the emitter relies on.
    [<Literal>]
    let Preamble =
        "\\usepackage{tikz}\n\\usepackage{textcomp}\n\\usepackage{siunitx}\n"

    /// Render a scene to a `tikzpicture` at the given surface size.
    ///
    /// **Degrades against this backend's own capabilities before drawing**, and that is not
    /// belt-and-braces. `Chart.render`, `Chart.toFile` and `Chart.toScene` do *not* degrade —
    /// only `Chart.renderTo` does — so a caller writing
    /// `fig |> Chart.toFile TikzBackend.render "f.tex" size` would otherwise hand an
    /// un-degraded scene to a backend advertising `Gradients = false`. Doing it here makes the
    /// renderer-function-shaped path safe by construction, and it is idempotent, so going
    /// through `Chart.renderTo` as well costs nothing.
    let render (extent: Extent<px>) (node: Node) : string =
        let backend = TikzBackend()
        let sink = backend :> IBackend

        let scene =
            node |> Degrade.forCaps sink.Caps |> Degrade.splitBatches sink.Caps

        sink.Begin extent
        sink.Draw scene
        sink.Finish()
        backend.Result

    /// Render with explicit options.
    let renderWith (options: TikzOptions) (extent: Extent<px>) (node: Node) : string =
        let backend = TikzBackend(options)
        let sink = backend :> IBackend

        let scene =
            node |> Degrade.forCaps sink.Caps |> Degrade.splitBatches sink.Caps

        sink.Begin extent
        sink.Draw scene
        sink.Finish()
        backend.Result

    /// Wrap a picture in a complete `standalone` document, ready to compile on its own.
    ///
    /// `standalone` crops the page to the picture, so the `\useasboundingbox` the backend emits
    /// becomes the page size and the output is exactly the requested extent.
    let standalone (picture: string) : string =
        String.Concat
            [| "\\documentclass{standalone}\n"
               Preamble
               "\\begin{document}\n"
               picture
               "\\end{document}\n" |]

    /// Render a scene as a complete compilable document.
    let renderStandalone (extent: Extent<px>) (node: Node) : string = standalone (render extent node)

    /// Render a scene and write the picture to a file as UTF-8 with no byte-order mark.
    ///
    /// The encoding is pinned rather than left to the default because these files are golden
    /// masters (§13): a BOM appearing or disappearing would show up as a whole-file diff.
    let renderToFile (path: string) (extent: Extent<px>) (node: Node) : unit =
        IO.File.WriteAllText(path, render extent node, UTF8Encoding false)
