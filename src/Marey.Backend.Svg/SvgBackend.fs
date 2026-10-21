namespace Marey

open System
open System.Text

// ======================================================================================
// THE REFERENCE BACKEND.
//
// "Build this first and treat it as the spec." Everything in this file is written for two
// readers: the SVG renderer that has to draw it, and the person reviewing a golden-image diff
// in a pull request. Where those two pull in different directions the reviewer wins,
// because the reviewer is the one who catches the bug.
//
// ---------------------------------------------------------------------------------------
// COORDINATES: the transform is applied to the geometry, never emitted as a group transform.
// ---------------------------------------------------------------------------------------
//
// The IR is entirely in `nd`, y-up (Coord.fs, Affine.fromRects: "the y axis is *not* flipped
// ... the flip to y-down device space is the backend's business, applied once in Begin"). This
// backend therefore builds one `Affine<nd,px>` in `Begin` that does the nd to px scale *and*
// the y flip together, and threads it down the traversal as a current transform. `Transform`
// nodes compose into it with `Affine.compose child parent` — "f then g", so the child's nd to
// nd map runs first and the root mapping last.
//
// Every coordinate is then written out already in device pixels, and **no `transform`
// attribute is emitted for scene geometry at all.**
//
// That is a deliberate choice over the alternative — nesting `<g transform="matrix(...)">` and
// patching stroke widths — and the reason is `Stroke.Width` and `MarkerBatch.Size`. Scene.fs is
// explicit that these are in px, not nd: "a hairline must stay a hairline when the figure is
// rendered at 4K". SVG scales the *pen* along with the geometry under a group transform, so a
// 1 px hairline inside `matrix(800 0 0 -600 0 600)` is drawn 800 px wide horizontally and 600
// px wide vertically. `vector-effect="non-scaling-stroke"` does fix the width, but it is an
// SVG 2 property that older renderers ignore, and it does nothing at all for dash lengths,
// dash offsets, or marker sizes — all of which are in px for exactly the same reason. There is
// no attribute that un-scales those.
//
// Pre-transforming the geometry sidesteps all of it: the document's user space *is* device
// space, so a px is a px everywhere, `stroke-width="1"` means one pixel, and the emitted
// numbers are the numbers a reviewer can check against a ruler. The cost is that a `Transform`
// node does not survive as a nameable thing in the output, which is a cost worth paying: `Keyed`
// has that job, and `Keyed` does emit a group.
//
// The two places a transform attribute *does* appear are the two where geometry is not being
// mapped: `rotate(...)` on text, whose size is typographic and must not scale, and
// `gradientTransform` on a gradient, which is how a gradient defined in nd is carried into
// device space without pre-transforming its (possibly anisotropic) coordinate system by hand.
// ======================================================================================

/// The reference backend. Renders a `Node` to a standalone SVG document.
///
/// The rendered document is exposed by the concrete type, through `Result`, because
/// `IBackend.Finish` returns unit by design (see Backend.fs): backends produce genuinely
/// different artifacts and only the protocol is shared.
///
/// One instance may be reused: `Begin` resets all state, so the same backend can render a
/// sequence of scenes, and two renders of the same scene produce byte-identical strings.
[<Sealed>]
type SvgBackend(?dpi: float<px / mm>, ?idPrefix: string) =

    /// Points to pixels needs a resolution. 96 px per inch is the screen convention and the
    /// default of `ApproxTextMeasurer`, so the measurer that layout consults and the font size
    /// that lands in the document agree by construction. Passing a print resolution changes
    /// both together.
    let dpi = defaultArg dpi Space.screenDpi

    let measurer = ApproxTextMeasurer(dpi) :> ITextMeasurer

    /// Namespaces every id this render emits. Empty for an ordinary render; set by
    /// `renderScoped` so that N frames can be stacked in one document without their clip paths,
    /// markers and `Keyed` names colliding. See `SvgDocument`.
    let doc = SvgDocument(defaultArg idPrefix "")

    /// Where each `Keyed` element sits in the body: its key, its id, and the offsets of
    /// its markup from the start of its line to the end of its closing tag's line.
    let spans = ResizeArray<struct (string * string * int * int)>()

    let mutable started = false
    let mutable finished = false
    let mutable cached: string = null

    /// The nd to px map established in `Begin`: scale to the surface and flip y, in one step.
    let mutable root: Affine<nd, px> = Affine.scaleUniform (LanguagePrimitives.FloatWithMeasure<px / nd> 1.0)

    // ---- small emission helpers ------------------------------------------------------

    let sb = doc.Body

    let attr (name: string) (value: string) =
        sb.Append(' ').Append(name).Append("=\"").Append(value).Append('"') |> ignore

    let attrEsc (name: string) (value: string) = attr name (Svg.escapeAttr value)

    let line (depth: int) (text: string) =
        sb.Append(Svg.indent depth).Append(text) |> ignore

    let openTag (depth: int) (name: string) =
        sb.Append(Svg.indent depth).Append('<').Append(name) |> ignore

    let closeEmpty () = sb.Append("/>\n") |> ignore
    let closeOpen () = sb.Append(">\n") |> ignore

    let endTag (depth: int) (name: string) =
        sb.Append(Svg.indent depth).Append("</").Append(name).Append(">\n") |> ignore

    let requireStarted () =
        if not started then
            invalidOp "Marey.SvgBackend: Begin must be called before Draw."

        if finished then
            invalidOp "Marey.SvgBackend: Draw cannot be called after Finish; call Begin again to start a new document."

    // ---- geometry --------------------------------------------------------------------

    /// A panel-space point in device pixels under the current transform.
    let point (ctm: Affine<nd, px>) (p: Vec2<nd>) =
        let q = Affine.apply ctm p
        struct (float q.X, float q.Y)

    /// Path data. Commands are written out one per segment rather than using SVG's implicit
    /// repeat ("L1 2 3 4"), because a golden diff that shows one command per segment is a
    /// golden diff a human can read. The saving would have been one byte per point.
    /// Nothing here allocates per segment beyond the formatted numbers themselves. The hot
    /// path cannot afford it — "a 100 000-segment path as a cons list is 100 000 heap
    /// objects" — and a writer that allocates a tuple array per segment would put them
    /// straight back.
    let pathData (ctm: Affine<nd, px>) (segs: PathSeg[]) =
        let d = StringBuilder(segs.Length * 16)

        let coord (separate: bool) (p: Vec2<nd>) =
            let struct (x, y) = point ctm p

            if separate then
                d.Append(' ') |> ignore

            d.Append(Svg.num x).Append(' ').Append(Svg.num y) |> ignore

        for s in segs do
            match s with
            | MoveTo p ->
                d.Append('M') |> ignore
                coord false p
            | LineTo p ->
                d.Append('L') |> ignore
                coord false p
            | CubicTo(c1, c2, p) ->
                d.Append('C') |> ignore
                coord false c1
                coord true c2
                coord true p
            | ClosePath -> d.Append('Z') |> ignore

        d.ToString()

    // ---- paint -----------------------------------------------------------------------

    /// Render a gradient definition.
    ///
    /// The gradient's own coordinates stay in nd and the current transform is handed to SVG as
    /// `gradientTransform`. Transforming them by hand would work for the linear case but not
    /// the radial one: a radial gradient has a single radius, so under an anisotropic nd to px
    /// map — which is the normal case, since a figure is rarely square — the correct image is
    /// an ellipse, and only a gradient transform can express that. Doing both the same way
    /// keeps one code path.
    let gradientDef (ctm: Affine<nd, px>) (paint: Paint) (id: string) =
        let stopsOf (stops: GradientStop[]) =
            let b = StringBuilder()

            for s in stops do
                b.Append("      <stop offset=\"").Append(Svg.ratio s.Offset) |> ignore
                b.Append("\" stop-color=\"").Append(Color.toHex s.Color) |> ignore

                if s.Color.A < 1.0 then
                    b.Append("\" stop-opacity=\"").Append(Svg.ratio s.Color.A) |> ignore

                b.Append("\"/>\n") |> ignore

            b.ToString()

        match paint with
        | Solid _ -> ""
        | LinearGradient(a, b, stops) ->
            String.Concat
                [| "    <linearGradient id=\""
                   id
                   "\" gradientUnits=\"userSpaceOnUse\" gradientTransform=\""
                   Svg.matrix ctm
                   "\" x1=\""
                   Svg.ratio (float a.X)
                   "\" y1=\""
                   Svg.ratio (float a.Y)
                   "\" x2=\""
                   Svg.ratio (float b.X)
                   "\" y2=\""
                   Svg.ratio (float b.Y)
                   "\">\n"
                   stopsOf stops
                   "    </linearGradient>\n" |]
        | RadialGradient(centre, radius, stops) ->
            String.Concat
                [| "    <radialGradient id=\""
                   id
                   "\" gradientUnits=\"userSpaceOnUse\" gradientTransform=\""
                   Svg.matrix ctm
                   "\" cx=\""
                   Svg.ratio (float centre.X)
                   "\" cy=\""
                   Svg.ratio (float centre.Y)
                   "\" r=\""
                   Svg.ratio (float radius)
                   "\">\n"
                   stopsOf stops
                   "    </radialGradient>\n" |]

    /// The value for a `fill` or `stroke` attribute, plus the separate opacity SVG needs.
    ///
    /// `Color.toHex` drops alpha on purpose ("backends carry it separately"), so alpha comes
    /// back here as `fill-opacity` / `stroke-opacity`.
    let paintRef (ctm: Affine<nd, px>) (paint: Paint) : string * string option =
        match paint with
        | Solid c -> Color.toHex c, (if c.A >= 1.0 then None else Some(Svg.ratio c.A))
        | LinearGradient _ -> "url(#" + doc.Def("qg", gradientDef ctm paint) + ")", None
        | RadialGradient _ -> "url(#" + doc.Def("qg", gradientDef ctm paint) + ")", None

    /// Write `fill` and `fill-opacity`. `none` is written explicitly when there is no fill:
    /// SVG's initial fill is *black*, so omitting it turns every unfilled stroked path into a
    /// solid blob.
    let writeFill (ctm: Affine<nd, px>) (fill: Paint option) =
        match fill with
        | None -> attr "fill" "none"
        | Some p ->
            let value, opacity = paintRef ctm p
            attr "fill" value

            match opacity with
            | Some o -> attr "fill-opacity" o
            | None -> ()

    /// Write the stroke attributes. Attributes whose value is the SVG default are omitted, so
    /// that a golden file only ever contains the parts of the style that were actually chosen.
    let writeStroke (ctm: Affine<nd, px>) (stroke: Stroke option) =
        match stroke with
        | None -> ()
        | Some s ->
            let value, opacity = paintRef ctm s.Paint
            attr "stroke" value

            match opacity with
            | Some o -> attr "stroke-opacity" o
            | None -> ()

            attr "stroke-width" (Svg.num (float s.Width))

            if s.Dash.Length > 0 then
                let parts = s.Dash |> Array.map (fun d -> Svg.num (float d))
                attr "stroke-dasharray" (String.Join(" ", parts))

                if float s.DashOffset <> 0.0 then
                    attr "stroke-dashoffset" (Svg.num (float s.DashOffset))

            match s.Cap with
            | Butt -> () // the SVG default
            | Round -> attr "stroke-linecap" "round"
            | Square -> attr "stroke-linecap" "square"

            match s.Join with
            | Miter -> () // the SVG default
            | RoundJoin -> attr "stroke-linejoin" "round"
            | Bevel -> attr "stroke-linejoin" "bevel"

            if s.Join = Miter && s.MiterLimit <> 4.0 then
                attr "stroke-miterlimit" (Svg.num s.MiterLimit)

    // ---- markers ---------------------------------------------------------------------

    // Batching is the single biggest performance decision in the IR, and it is decided here.
    //
    // One batch produces exactly one shape definition and one group; the group carries the
    // paint, and each point costs a single `<use>` that inherits it. A hundred thousand points
    // is a hundred thousand small elements referencing one path — not a hundred thousand
    // independent shapes each repeating its own fill, stroke and geometry.
    //
    // The definition is a bare `<path>`/`<circle>`/`<rect>` rather than the `<symbol>` the
    // design sketch mentions, on purpose: `<symbol>` establishes a new viewport, and a symbol
    // with no viewBox and no width/height inherits the *parent* viewport's size, which scales
    // and clips the content in ways that differ between renderers. A bare shape in `<defs>`
    // has none of that machinery and draws identically everywhere.
    //
    // `xlink:href` rather than SVG 2's plain `href` because this backend is the oracle: every
    // renderer in existence understands the xlink form, and six extra bytes per marker is not
    // a reason to lose a renderer.
    //
    // Shapes are defined in device space (y already down) so that "triangle up" points up on
    // screen, and are sized so that `Size` is the bounding extent in px, matching the field's
    // documentation ("Diameter in px").
    let markerGeometry (shape: MarkerShape) (size: float) (id: string) =
        let r = size / 2.0
        let n = Svg.num

        // Half-thickness of the arms of a plus or a cross. A twelve-vertex outline rather than
        // two line segments, so that a batch with a fill and no stroke still draws something:
        // Fill and Stroke are independent options on MarkerBatch and both have to work.
        let t = r / 3.0

        let poly (pts: (float * float)[]) =
            let d = StringBuilder()

            pts
            |> Array.iteri (fun i (x, y) ->
                d.Append(if i = 0 then 'M' else 'L').Append(n x).Append(' ').Append(n y)
                |> ignore)

            d.Append('Z').ToString()

        let plusPoints =
            [| (-t, -r); (t, -r); (t, -t); (r, -t); (r, t); (t, t); (t, r); (-t, r); (-t, t); (-r, t); (-r, -t); (-t, -t) |]

        let shapeXml =
            match shape with
            | MarkCircle -> String.Concat [| "<circle r=\""; n r; "\"/>" |]
            | MarkSquare ->
                String.Concat
                    [| "<rect x=\""; n -r; "\" y=\""; n -r; "\" width=\""; n size; "\" height=\""; n size; "\"/>" |]
            | MarkDiamond -> "<path d=\"" + poly [| (0.0, -r); (r, 0.0); (0.0, r); (-r, 0.0) |] + "\"/>"
            | MarkTriangleUp ->
                // Vertices on the circumscribed circle at -90, 30 and 150 degrees; y is down,
                // so the apex is at negative y.
                let k = r * sqrt 3.0 / 2.0
                "<path d=\"" + poly [| (0.0, -r); (k, r / 2.0); (-k, r / 2.0) |] + "\"/>"
            | MarkTriangleDown ->
                let k = r * sqrt 3.0 / 2.0
                "<path d=\"" + poly [| (0.0, r); (-k, -r / 2.0); (k, -r / 2.0) |] + "\"/>"
            | MarkPlus -> "<path d=\"" + poly plusPoints + "\"/>"
            | MarkCross ->
                // The same outline turned 45 degrees. Rotating the vertices here rather than
                // emitting a transform keeps the definition a plain path, so a `<use>` of it
                // needs no compensation.
                let s2 = sqrt 0.5

                let rotated =
                    plusPoints |> Array.map (fun (x, y) -> ((x - y) * s2, (x + y) * s2))

                "<path d=\"" + poly rotated + "\"/>"

        // Insert the id as the first attribute of whatever element the shape produced. The
        // first space always separates the element name from its first attribute.
        let cut = shapeXml.IndexOf ' '
        "    " + shapeXml.Insert(cut, " id=\"" + id + "\"") + "\n"

    // ---- raster ----------------------------------------------------------------------

    /// A raster's destination box in nd, mapped to device space.
    ///
    /// An image's rows run top-first while nd is y-up, so the local y axis has to be flipped
    /// *inside the destination box* before the current transform is applied — otherwise the
    /// image lands upside down, which the panel-wide y flip would otherwise hide from anyone
    /// testing with a symmetric picture.
    let rasterTransform (ctm: Affine<nd, px>) (bounds: Rect<nd>) : Affine<nd, px> =
        let flip: Affine<nd, nd> =
            { M11 = 1.0
              M12 = 0.0
              M21 = 0.0
              M22 = -1.0
              Dx = 0.0<nd>
              Dy = bounds.Y0 + bounds.Y1 }

        Affine.compose flip ctm

    // ---- semantics -------------------------------------------------------------------

    // "Annotated is not decoration ... hit-testing, tooltips, aria-labels on SVG output,
    // and clickable legends all need the IR to remember what a shape means."
    //
    // Two attributes come out of the role, not one. `role` is a real ARIA role from the WAI
    // graphics module, which is what an assistive technology reads; `data-marey-role` is the
    // exact IR case, which is what a future hit-tester or legend-linker reads, and which the
    // ARIA vocabulary is too coarse to carry (Series and Axis both map to graphics-object).
    let ariaRole (role: SemanticRole) =
        match role with
        | RoleSeries -> "graphics-object"
        | RoleDatum -> "graphics-symbol"
        | RoleAxis -> "graphics-object"
        | RoleLegend -> "group"
        | RoleAnnotation -> "note"
        | RoleDecoration -> "presentation"
        | RoleOther _ -> "graphics-object"

    let roleName (role: SemanticRole) =
        match role with
        | RoleSeries -> "series"
        | RoleDatum -> "datum"
        | RoleAxis -> "axis"
        | RoleLegend -> "legend"
        | RoleAnnotation -> "annotation"
        | RoleDecoration -> "decoration"
        | RoleOther s -> "other:" + (if isNull s then "" else s)

    /// Write `data-*` attributes for the free-form metadata.
    ///
    /// Duplicate keys would produce duplicate attributes, which is not merely undesirable but
    /// malformed XML, so a repeat is suffixed rather than dropped: the data survives and the
    /// document parses.
    let writeData (pairs: (string * string)[]) =
        let seen = System.Collections.Generic.HashSet<string>()

        for (k, v) in pairs do
            let stem = "data-" + Svg.namePart k
            let mutable name = stem
            let mutable n = 1

            while not (seen.Add name) do
                n <- n + 1
                name <- stem + "-" + string n

            attrEsc name v

    // ---- text ------------------------------------------------------------------------

    let anchorName (a: TextAnchor) =
        match a with
        | AnchorStart -> "start"
        | AnchorMiddle -> "middle"
        | AnchorEnd -> "end"

    // `central` rather than `middle`: `middle` is defined against half the x-height, whereas
    // `central` is the centre of the em box, which is what Core's TextMeasure.bounds computes
    // for BaselineMiddle ("-h/2, h/2" over ascent plus descent). Layout and rendering agreeing
    // matters more than matching any particular other library's habit.
    let baselineAttr (b: TextBaseline) =
        match b with
        | BaselineAlphabetic -> None // the SVG default
        | BaselineMiddle -> Some "central"
        | BaselineHanging -> Some "hanging"

    // ---- traversal -------------------------------------------------------------------

    let rec draw (ctm: Affine<nd, px>) (depth: int) (node: Node) =
        match node with

        | Path(segs, stroke, fill, rule) ->
            if segs.Length > 0 then
                openTag depth "path"
                attr "d" (pathData ctm segs)
                writeFill ctm fill

                match rule with
                | NonZero -> () // the SVG default
                | EvenOdd -> attr "fill-rule" "evenodd"

                writeStroke ctm stroke
                closeEmpty ()

        | Text t -> drawText ctm depth t

        | Markers b -> drawMarkers ctm depth b

        | Raster r -> drawRaster ctm depth r

        // A Group is the identity-carrying container of the scene's monoid and has no rendering
        // effect of its own, so it emits no element. Every `<g>` in the output therefore
        // carries meaning: an id, a clip, or semantics.
        | Group ns ->
            for child in ns do
                draw ctm depth child

        // Composed into the current transform rather than emitted. See the note at the top of
        // the file: this is what keeps px-denominated stroke widths and marker sizes honest.
        | Transform(a, child) -> draw (Affine.compose a ctm) depth child

        | Clip(segs, child) ->
            if segs.Length = 0 then
                // An empty clip path clips everything away. Emitting the group anyway keeps
                // the output faithful to the scene rather than quietly reinterpreting it.
                let id =
                    doc.Def("qc", fun id -> "    <clipPath id=\"" + id + "\" clipPathUnits=\"userSpaceOnUse\"/>\n")

                openTag depth "g"
                attr "clip-path" ("url(#" + id + ")")
                closeOpen ()
                draw ctm (depth + 1) child
                endTag depth "g"
            else
                let d = pathData ctm segs

                let id =
                    doc.Def(
                        "qc",
                        fun id ->
                            "    <clipPath id=\"" + id + "\" clipPathUnits=\"userSpaceOnUse\"><path d=\"" + d + "\"/></clipPath>\n"
                    )

                openTag depth "g"
                attr "clip-path" ("url(#" + id + ")")
                closeOpen ()
                draw ctm (depth + 1) child
                endTag depth "g"

        // Group opacity, which is what `<g opacity>` means in SVG: the subtree is
        // composited into its own buffer and *that* is faded, so an overlap inside the group
        // does not read darker than the rest of it. This is the case `Degrade.forCaps`
        // approximates for a backend without it; here it is exact and no rewrite happens.
        //
        // The attribute is written even when it is 1, unlike `fill-rule` and the baseline
        // attributes, which are omitted at their SVG defaults. Those two omissions describe a
        // property the scene did not ask about; an `Opacity` node is something the scene said,
        // and a golden that shows what the scene said is worth one redundant attribute.
        | Opacity(a, child) ->
            openTag depth "g"
            attr "opacity" (Svg.ratio (max 0.0 (min 1.0 a)))
            closeOpen ()
            draw ctm (depth + 1) child
            endTag depth "g"

        // Stable identity, so a future diff can address this subtree by name.
        | Keyed(Key name, child) ->
            let id = doc.UniqueId(Svg.ncname name)
            let start = sb.Length
            openTag depth "g"
            attr "id" id
            closeOpen ()
            draw ctm (depth + 1) child
            endTag depth "g"
            spans.Add(struct (name, id, start, sb.Length))

        | Annotated(s, child) ->
            openTag depth "g"
            attr "role" (ariaRole s.Role)
            attr "data-marey-role" (Svg.escapeAttr (roleName s.Role))

            match s.Role with
            | RoleDecoration ->
                // Gridlines and panel frames: "everything a screen reader should skip". A
                // label on a hidden element is contradictory, so it is not emitted; the data
                // pairs are, because hit-testing still wants them.
                attr "aria-hidden" "true"
            | _ ->
                if not (String.IsNullOrEmpty s.Label) then
                    attrEsc "aria-label" s.Label

            writeData s.Data
            closeOpen ()
            draw ctm (depth + 1) child
            endTag depth "g"

    and drawText (ctm: Affine<nd, px>) (depth: int) (t: TextRun) =
        if not (String.IsNullOrEmpty t.Text) then
            let struct (x, y) = point ctm t.At

            openTag depth "text"
            attr "x" (Svg.num x)
            attr "y" (Svg.num y)

            // ROTATION. `TextRun.Rotation` is measured in nd, which is y-up, while SVG's
            // rotate is positive *clockwise* in a y-down space, so a sign flip is needed; and
            // a Transform node that rotates the panel has to rotate its labels with it, so the
            // current transform contributes too.
            //
            // The obvious implementation - push the direction vector (cos, sin) through the
            // linear part and read back atan2 - is wrong, and wrong in a way that only shows
            // up on non-square figures. The nd to px map is anisotropic whenever the surface
            // is not square, so it does not preserve angles: on an 800x600 surface it turns a
            // requested 30 degrees into 23.4. Asking for a 30 degree tick label and measuring
            // 23.4 on the screen is a bug report.
            //
            // So the frame's rotation and the run's own rotation are taken separately. The
            // frame contributes `atan2(M21, M11)`, which is the angle of the image of the x
            // axis read in y-down coordinates - already an SVG rotate angle. The run's own
            // angle is applied on top in device space, negated when the frame is mirrored
            // (a negative determinant, which the y flip alone produces), because a mirror
            // reverses the sense of a rotation.
            //
            // This treats rotation as the typographic property it is - a sibling of the font
            // size, which is likewise in device units and likewise not scaled by the panel
            // mapping - rather than as a direction in panel space.
            let frameDegrees = atan2 (float ctm.M21) (float ctm.M11) * 180.0 / Math.PI
            let ownDegrees = float t.Rotation * 180.0 / Math.PI
            let mirrored = float (Affine.determinant ctm) < 0.0

            let degrees =
                frameDegrees + (if mirrored then -ownDegrees else ownDegrees)

            let degreesText = Svg.num degrees

            if degreesText <> "0" then
                attr "transform" (String.Concat [| "rotate("; degreesText; " "; Svg.num x; " "; Svg.num y; ")" |])

            if not (String.IsNullOrEmpty t.Font.Family) then
                attrEsc "font-family" t.Font.Family

            attr "font-size" (Svg.num (float (Space.pxOfPt dpi t.Font.Size)))

            if t.Font.Weight <> 400 then
                attr "font-weight" (string t.Font.Weight)

            if t.Font.Italic then
                attr "font-style" "italic"

            attr "text-anchor" (anchorName t.Anchor)

            match baselineAttr t.Baseline with
            | Some b -> attr "dominant-baseline" b
            | None -> ()

            // Text is filled, never stroked. A gradient-painted run resolves against the
            // element's user space, which for a rotated run includes the rotation - an
            // acknowledged corner: a gradient under rotated text turns with the text.
            writeFill ctm (Some t.Paint)

            // An XML parser collapses leading and trailing whitespace in element content
            // unless told not to. Tick labels padded with a space are a real thing.
            if t.Text <> t.Text.Trim() then
                attr "xml:space" "preserve"

            sb.Append('>').Append(Svg.escapeText t.Text).Append("</text>\n") |> ignore

    and drawMarkers (ctm: Affine<nd, px>) (depth: int) (b: MarkerBatch) =
        if b.Points.Length > 0 then
            let size = float b.Size
            let id = doc.Def("qm", markerGeometry b.Shape size)

            openTag depth "g"
            writeFill ctm b.Fill
            writeStroke ctm b.Stroke
            closeOpen ()

            let inner = Svg.indent (depth + 1)

            for p in b.Points do
                let struct (x, y) = point ctm p

                sb
                    .Append(inner)
                    .Append("<use xlink:href=\"#")
                    .Append(id)
                    .Append("\" x=\"")
                    .Append(Svg.num x)
                    .Append("\" y=\"")
                    .Append(Svg.num y)
                    .Append("\"/>\n")
                |> ignore

            endTag depth "g"

    and drawRaster (ctm: Affine<nd, px>) (depth: int) (r: RasterNode) =
        let img = r.Image
        let expected = int64 img.Width * int64 img.Height * 4L
        let actual = if isNull img.Rgba then 0L else int64 img.Rgba.Length

        if img.Width <= 0 || img.Height <= 0 then
            // Visible rather than silent, in the spirit of Degrade: the reviewer of the golden
            // diff sees why nothing was drawn.
            line depth (String.Concat [| "<!-- marey: raster skipped, "; string img.Width; "x"; string img.Height; " has no pixels -->\n" |])
        else
            if actual <> expected then
                line
                    depth
                    (String.Concat
                        [| "<!-- marey: raster pixel buffer is "
                           string actual
                           " bytes, expected "
                           string expected
                           "; missing rows are transparent -->\n" |])

            // Both extents are taken as absolute: a destination box given with X1 below X0
            // would otherwise become a negative width, which SVG rejects outright.
            let x0 = min (float r.Bounds.X0) (float r.Bounds.X1)
            let y0 = min (float r.Bounds.Y0) (float r.Bounds.Y1)
            let w = abs (float r.Bounds.X1 - float r.Bounds.X0)
            let h = abs (float r.Bounds.Y1 - float r.Bounds.Y0)

            openTag depth "image"
            attr "x" (Svg.ratio x0)
            attr "y" (Svg.ratio y0)
            attr "width" (Svg.ratio w)
            attr "height" (Svg.ratio h)
            attr "transform" (Svg.matrix (rasterTransform ctm r.Bounds))
            // The destination is a box, not a suggestion: the image fills it exactly.
            attr "preserveAspectRatio" "none"
            // "False is right for heatmaps of discrete data, where smoothing invents values
            // that were never measured" - so say so to the renderer rather than hoping.
            attr "image-rendering" (if r.Smooth then "optimizeQuality" else "pixelated")
            attr "xlink:href" (Png.dataUri img)
            closeEmpty ()

    // ---- IBackend --------------------------------------------------------------------

    /// The rendered document.
    ///
    /// Available as soon as `Begin` has been called, and stable afterwards: calling it before
    /// `Finish` composes whatever has been drawn so far, which is what makes a partial render
    /// debuggable.
    member _.Result: string =
        if not started then
            invalidOp "Marey.SvgBackend: Begin must be called before Result."

        if isNull cached then
            let s = doc.Compose()

            if finished then
                cached <- s

            s
        else
            cached

    /// The document in its three parts, and every keyed element's place in the body.
    member internal _.Pieces =
        doc.Header, doc.DefsText(), doc.Body.ToString(), spans.ToArray()

    /// The surface size passed to `Begin`, as an nd to px map. Exposed for tests, which need
    /// to check emitted coordinates against the transform that produced them.
    member _.RootTransform: Affine<nd, px> = root

    interface IBackend with

        /// SVG is a full vector target: the only thing on the list it cannot do is real text
        /// shaping, because it does not lay the text out at all - it hands the string to
        /// whatever opens the file and lets that renderer shape it.
        member _.Caps =
            { Gradients = true
              RadialGradients = true
              Clipping = true
              Dashes = true
              Rasters = true
              // Backend.fs: "False for the zero-dependency SVG backend, which delegates
              // shaping to whatever opens the file." Kerning and ligatures do happen in the
              // output; this backend simply does not know where they land, which is exactly
              // what the flag is warning the layout layer about.
              TextShaping = false
              RotatedText = true
              // Semantics are implemented, not stubbed: role, aria-label and data-* all reach the
              // document. `Capabilities.full` has this false because it describes a generic
              // vector backend; this one earns it.
              Semantics = true
              // `<g opacity>` is group opacity by definition: SVG composites the group
              // into its own buffer and fades the result. Nothing to degrade.
              GroupOpacity = true
              // No instance-buffer ceiling: a text file has no hardware limit, and batching's
              // whole point is that one batch stays one batch.
              MaxBatch = None }

        member _.Measurer = measurer

        member _.Begin(extent: Extent<px>) =
            doc.Reset()
            spans.Clear()
            started <- true
            finished <- false
            cached <- null

            // The one place the y flip lives. `fromRects` maps the unit panel onto a device
            // box whose y runs from Height at nd y = 0 to 0 at nd y = 1, so the flip and the
            // scale are a single matrix and no later stage has to remember to invert anything.
            root <-
                Affine.fromRects
                    Rect.unit
                    { X0 = 0.0<px>
                      Y0 = extent.Height
                      X1 = extent.Width
                      Y1 = 0.0<px> }

            let w = Svg.num (float extent.Width)
            let h = Svg.num (float extent.Height)

            // No XML declaration: this markup is as likely to be inlined into a notebook cell
            // or an HTML page as written to a file, and a stray prolog is invalid there.
            // A viewBox in the same units as width and height is what lets the same document
            // scale to any container without re-rendering.
            doc.Header <-
                String.Concat
                    [| "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\""
                       w
                       "\" height=\""
                       h
                       "\" viewBox=\"0 0 "
                       w
                       " "
                       h
                       "\">\n" |]

        member _.Draw(node: Node) =
            requireStarted ()
            cached <- null
            draw root 1 node

        member _.Finish() =
            if not started then
                invalidOp "Marey.SvgBackend: Begin must be called before Finish."

            finished <- true
            cached <- null

/// A new scene as a change to the document the old one rendered to.
///
/// The view swaps each element in `Elements` in by its id, and replaces its `<defs>` with `Defs`
/// whole. `Elements` is `None` when no set of keyed elements describes the change, and the view
/// is sent `Document` instead.
[<NoEquality; NoComparison>]
type SvgPatch =
    { /// The new scene's whole document, exactly as `SvgBackend.render` writes it.
      Document: string
      /// The new scene's `<defs>` block, or `""` when it has none. Sent with every patch,
      /// because def ids are numbered in the order the whole document discovers them.
      Defs: string
      /// Each replaced element's id, and its markup as it stands in `Document`.
      Elements: (string * string)[] option }

/// Convenience entry points. The interface is what a heterogeneous list of backends is driven
/// through; these are what a caller with one backend actually wants.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
[<RequireQualifiedAccess>]
module SvgBackend =

    /// Render a scene to an SVG document at the given surface size, with every id it emits
    /// namespaced by `idPrefix`.
    ///
    /// **For stacking renders inside one document, and nothing else.** SVG ids are scoped to the
    /// document, not to a nested `<svg>` element, so N frames of a film in one outer `<svg>`
    /// share one id space: without this, frame 5's `url(#qc)` resolves to frame 0's clip path and
    /// its `<use xlink:href="#qm">` to frame 0's marker — which is visible exactly when the two
    /// differ, so a `highlight` that grows one mark renders at the wrong size and everything else
    /// looks correct. `Html.filmAt` is the caller.
    ///
    /// The prefix is written verbatim, so it has to be a legal NCName start. `Html` uses
    /// `f0-`, `f1-`, … and nothing else in the repository calls this.
    let renderScoped (idPrefix: string) (extent: Extent<px>) (node: Node) : string =
        let backend = SvgBackend(idPrefix = idPrefix)
        let sink = backend :> IBackend
        sink.Begin extent
        sink.Draw node
        sink.Finish()
        backend.Result

    /// Render a scene to an SVG document at the given surface size.
    let render (extent: Extent<px>) (node: Node) : string = renderScoped "" extent node

    /// **The change from `before` to `after`, as elements to swap by id.**
    ///
    /// `Scene.diff` chooses the keys. Each is written from a render of the **whole** new scene,
    /// never from a render of the subtree alone, because the ids a subtree refers to (`qc`, `qm`,
    /// `qg`) are numbered in the order the whole document discovers them, and a subtree drawn
    /// alone would number them from one.
    ///
    /// The same numbering is why the IR's answer is checked against the text before it is
    /// believed. A layer that did not change can still render differently, when a marker
    /// earlier in the document changed size and the def ids after it moved up one. So both
    /// scenes are rendered, each replaced element is swapped back for its old markup, and the
    /// patch is kept only if that gives the old body byte for byte. Otherwise `Elements` is
    /// `None`, and the whole document goes. Applying a patch to `render before` therefore gives
    /// `render after` exactly, whatever the IR missed.
    let renderPatch (extent: Extent<px>) (before: Node) (after: Node) : SvgPatch =
        let pieces (node: Node) =
            let backend = SvgBackend()
            let sink = backend :> IBackend
            sink.Begin extent
            sink.Draw node
            sink.Finish()
            backend.Result, backend.Pieces

        let document, (header, defs, body, spans) = pieces after
        let whole = { Document = document; Defs = defs; Elements = None }

        match Scene.diff before after with
        | Whole _ -> whole
        | Replace reps when reps.Length = 0 -> { whole with Elements = Some [||] }
        | Replace reps ->
            let _, (header', _, body', spans') = pieces before

            let spanOf (all: struct (string * string * int * int)[]) (Key k) =
                all |> Array.tryFind (fun (struct (name, _, _, _)) -> name = k)

            let pairs = reps |> Array.map (fun (k, _) -> spanOf spans k, spanOf spans' k)

            let matched =
                pairs
                |> Array.choose (fun (a, b) ->
                    match a, b with
                    | Some(struct (_, id, s0, s1)), Some(struct (_, id', t0, t1)) when id = id' -> Some(id, s0, s1, t0, t1)
                    | _ -> None)
                |> Array.sortBy (fun (_, s0, _, _, _) -> s0)

            if header <> header' || matched.Length <> pairs.Length then
                whole
            else
                let back = StringBuilder(body'.Length)
                let mutable pos = 0
                let mutable disjoint = true

                for (_, s0, s1, t0, t1) in matched do
                    if s0 < pos then
                        disjoint <- false
                    else
                        back.Append(body, pos, s0 - pos).Append(body', t0, t1 - t0) |> ignore
                        pos <- s1

                back.Append(body, pos, body.Length - pos) |> ignore

                if disjoint && back.ToString() = body' then
                    { whole with Elements = Some(matched |> Array.map (fun (id, s0, s1, _, _) -> id, body.Substring(s0, s1 - s0))) }
                else
                    whole

    // File output is .NET's. A Fable build has no file system to write to, and these do not
    // compile there.
#if !FABLE_COMPILER
    /// Render a scene and write it to a file as UTF-8 with no byte-order mark.
    ///
    /// The encoding is pinned rather than left to the default because these files are golden
    /// masters: a BOM appearing or disappearing would show up as a whole-file diff.
    let renderToFile (path: string) (extent: Extent<px>) (node: Node) : unit =
        let text = render extent node
        IO.File.WriteAllText(path, text, UTF8Encoding(false))
#endif
