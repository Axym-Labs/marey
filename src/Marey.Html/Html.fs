namespace Marey

// FS1240 — "this type test or downcast will ignore the unit-of-measure" — fires twice in the host
// bridge at the bottom of this file, and both times it is reporting the property the bridge is
// built on rather than a mistake. `U<'u>` erases to one runtime type, so `:? U<1>` matches a
// `U<m>` and a `U<s>` alike, and that is exactly why a single case covers every unit witness
// there will ever be. Suppressed here rather than worked around because every alternative
// spelling (a `GetType()` comparison, an `unbox`) warns identically and reads worse.
//
// The erasure is not taken on faith: `InteractiveTests` asserts `typeof<U<1>> = U.second.GetType()`
// directly, so if a future F# ever stopped erasing measures in a class's type argument, a test
// fails rather than a silently-narrowed match shipping.
#nowarn "1240"

open System.Text

// ======================================================================================
// HTML FRAGMENTS FOR A NOTEBOOK CELL.
//
// This began as a .NET Interactive formatter so a figure displays inline as SVG, since a
// notebook is how people first meet the library.
//
// **That host no longer exists.** Polyglot Notebooks was deprecated on
// 2026-02-11 and retired on 2026-03-27; .NET Interactive followed on 2026-04-24. This file
// outlived both without a line changing, and the reason is the only architectural claim worth
// making about it: **it holds no reference to any notebook host.** Every function is
// `something -> string`, total, deterministic, and callable from an ordinary program.
//
// That was originally a testability argument — the interesting half should be assertable without
// installing a kernel. It turned out to be a portability argument. When the host died, the part
// that had to be rewritten was nine lines of registration in a separate file; this one was
// re-pointed at Verso by adding a bridge at the bottom and deleting nothing.
//
// The bridge is the *only* host-facing surface here, and it is deliberately two functions rather
// than an interface: `Marey.Verso` invokes them **by reflection, across an
// AssemblyLoadContext**, so a shared type would not have shared identity. See `renderForHost`.
//
// ---------------------------------------------------------------------------------------
// FOUR RULES, EACH ONE LEARNED FROM A WAY THIS GOES WRONG.
// ---------------------------------------------------------------------------------------
//
// **1. Inline styles only. No `<style>` block, ever.**
//   A `<style>` element in a notebook output cell is not scoped to that cell. It leaks into the
//   whole document, outlives the cell that produced it, and stacks up once per execution. A
//   library that restyles the reader's notebook because they displayed a colour is a library
//   they uninstall. Everything here is a `style=` attribute, which cannot escape its element.
//
// **2. Never pin a foreground without pinning a background.**
//   A notebook is light or dark and offers no way to ask which. `color:#111` renders invisible
//   on a dark theme, and hardcoding a background to protect the foreground produces a white
//   card in the middle of a dark page. So: **no `color` and no `background` outside the
//   swatches, where the colour *is* the content.** Text inherits. Borders use a mid grey with
//   alpha, which reads acceptably against either extreme, and emphasis is carried by `opacity`,
//   which is relative to whatever the theme already chose.
//
//   The figure itself is exempt and needs no help: a `Theme` carries its own background, so an
//   SVG figure is self-contained by construction. A `Theme.dark` figure looks like a dark card
//   on a light notebook because that is what it *is*.
//
// **3. A formatter must not throw.**
//   `Figure.unify` raises on two physical dimensions sharing an axis — deliberately, with a good
//   message. But an exception escaping a *display formatter* does not surface that message; it
//   surfaces as a kernel-level formatting failure, with the real reason buried or lost, on a
//   value the user has already successfully constructed. So every entry point that can compile a
//   figure is wrapped, and the exception is rendered *as the output*. See `guarded`.
//
// **4. Every number goes through `Num.upTo`.**
//   Same rule as both backends, same reason: a German locale writes `0,5`, and `0,5` is not a
//   length CSS can parse. `Num.upTo` is invariant and never emits an exponent.
//
// One more thing this file deliberately does not do: rewrite the SVG it embeds. It would be easy
// to strip the `width`/`height` attributes and let the `viewBox` scale the figure to the cell,
// and it would silently break every figure whose text was measured at the size that was asked
// for. A figure that is wider than the cell scrolls; it does not shrink.
// ======================================================================================

/// **How a notebook page draws a figure.** `Auto` draws on a canvas from
/// `Html.CanvasThreshold` drawn elements on (`Scene.primitiveCount`: each mark, path and text run),
/// and as SVG below it; `Svg` and `Canvas` choose for every figure.
[<RequireQualifiedAccess>]
type RenderMode =
    | Svg
    | Canvas
    | Auto

/// HTML renderings of Marey's values, for a notebook cell or anywhere else HTML is wanted.
[<RequireQualifiedAccess>]
module Html =

    // ---- Shared style vocabulary -----------------------------------------------------

    /// Grey with alpha rather than a theme colour: see rule 2 in the header.
    [<Literal>]
    let private Border = "1px solid rgba(128,128,128,0.45)"

    [<Literal>]
    let private Mono = "ui-monospace,SFMono-Regular,Menlo,Consolas,monospace"

    /// A row of things, vertically centred, that wraps rather than overflowing a narrow cell.
    [<Literal>]
    let private Row = "display:flex;flex-wrap:wrap;gap:8px;align-items:center"

    /// The secondary text style. `opacity` rather than a colour, per rule 2.
    [<Literal>]
    let private Muted = "font-size:0.85em;opacity:0.7"

    // ---- Escaping --------------------------------------------------------------------
    //
    // `Svg.escapeText`/`escapeAttr` are reused rather than reimplemented. They were written for
    // XML, and XML escaping is strictly stronger than HTML escaping — every character they
    // escape needs escaping here too, and `&apos;` and `&quot;` are both named references in
    // HTML5. Reusing them also means the fragments this file emits parse as XML, which is what
    // lets the test suite check them with the same `XDocument.Parse` oracle the SVG golden
    // suite uses.
    //
    // Keeping that property costs one small discipline: **no HTML named entities.** XML defines
    // five (`&amp;` `&lt;` `&gt;` `&quot;` `&apos;`) and nothing else, so an `&mdash;` would make
    // every fragment containing it unparseable as XML while rendering perfectly in a browser —
    // a defect visible only to the test that would then no longer run. Punctuation is written as
    // the literal character (`—`, `·`, `×`, `°`, `Δ`), which is both valid XML and shorter.

    /// Escape a string for use as element content.
    let internal text (s: string) : string = Svg.escapeText s

    /// Escape a string for use inside a double-quoted attribute value.
    let internal attr (s: string) : string = Svg.escapeAttr s

    // ---- Numbers and colours ---------------------------------------------------------

    let private num (v: float) = Num.upTo 3 v

    /// `StringBuilder.Append(int)` formats through the *ambient* culture, which supplies the
    /// negative sign and the group separator. Neither differs for the small non-negative counts
    /// this file appends today, so this is guarding a latent bug rather than a live one — but it
    /// is the same latent bug rule 4 exists for, and `UnitDisplay` already spells it exactly this
    /// way.
    let private intText (n: int) = n.ToString(System.Globalization.CultureInfo.InvariantCulture)

    let private byte01 (x: float) =
        let clamped =
            if x < 0.0 then 0.0
            elif x > 1.0 then 1.0
            else x

        int (System.Math.Round(clamped * 255.0))

    /// A CSS colour. Eight-digit hex when there is alpha to carry, because `Color.toHex` drops
    /// it — backends carry alpha separately and CSS does not have to.
    let private cssColor (c: Color) =
        if c.A >= 1.0 then
            Color.toHex c
        else
            Color.toHex c + sprintf "%02x" (byte01 c.A)

    /// A colour chip.
    ///
    /// The two-layer background is not decoration. Layer one is the colour; layer two is a
    /// diagonal light/dark split *behind* it, so a colour with alpha shows its transparency
    /// instead of quietly reading as a lighter opaque colour. A swatch that lies about alpha is
    /// worse than no swatch.
    let private chip (size: int) (c: Color) =
        sprintf
            "<span style=\"display:inline-block;width:%dpx;height:%dpx;vertical-align:middle;border:%s;border-radius:3px;background-image:linear-gradient(%s,%s),linear-gradient(135deg,#ffffff 0%%,#ffffff 50%%,#b0b0b0 50%%,#b0b0b0 100%%)\"></span>"
            size
            size
            Border
            (cssColor c)
            (cssColor c)

    // ---- Failure ---------------------------------------------------------------------

    /// Rule 3: the exception becomes the output.
    ///
    /// `Figure.unify`'s dimensional-mismatch message is one of the most useful things this
    /// library says. Losing it to a formatter crash would be a poor trade, so it is displayed
    /// with the same prominence a figure would have had.
    let private errorBox (what: string) (e: exn) =
        let b = StringBuilder(256)

        b
            .Append("<div style=\"")
            .Append("padding:10px 12px;border:")
            .Append(Border)
            .Append(";border-left-width:4px;border-radius:4px;font-family:")
            .Append(Mono)
            .Append(";font-size:0.9em\">")
        |> ignore

        b
            .Append("<div style=\"")
            .Append(Muted)
            .Append("\">Marey could not render this ")
            .Append(text what)
            .Append(" — ")
            .Append(text (e.GetType().Name))
            .Append("</div>")
        |> ignore

        b.Append("<div style=\"margin-top:6px;white-space:pre-wrap\">").Append(text e.Message).Append("</div>")
        |> ignore

        b.Append("</div>").ToString()

    let private guarded (what: string) (f: unit -> string) : string =
        try
            f ()
        with e ->
            errorBox what e

    // ---- Figures and scenes ----------------------------------------------------------

    /// The size a figure is compiled at when nobody says otherwise.
    ///
    /// 8:5, the same aspect as `Chart.defaultSize`, but smaller: 800px is wider than a notebook
    /// output cell at default zoom in both VS Code and Jupyter, and a figure that needs
    /// horizontal scrolling on first sight makes a bad first impression. Override per-cell with
    /// `Html.figureAt`, or globally with `Formatters.registerWith`.
    let defaultSize: Extent<px> = { Width = 720.0<px>; Height = 450.0<px> }

    /// The wrapper every embedded SVG gets. `overflow-x:auto` rather than a scale transform:
    /// see the last paragraph of the header.
    let private scroller (svg: string) =
        "<div style=\"max-width:100%;overflow-x:auto\">" + svg + "</div>"

    /// A figure, compiled to SVG at the given size and inlined.
    let figureAt (extent: Extent<px>) (fig: Figure) : string =
        guarded "figure" (fun () -> scroller (Chart.render SvgBackend.render extent fig))

    /// A figure at `defaultSize`.
    let figure (fig: Figure) : string = figureAt defaultSize fig

    /// A raw scene, with its node census.
    ///
    /// The census is the reason to have this at all. A `Node` displayed as a picture answers
    /// "what does it look like"; the counts answer "how much of it is there", which is the
    /// question you are actually asking when you inspect the IR by hand. `Scene.count` counts
    /// every node including containers, `primitiveCount` only the four that draw.
    let nodeAt (extent: Extent<px>) (n: Node) : string =
        guarded "scene" (fun () ->
            let svg = SvgBackend.render extent n

            let census =
                sprintf
                    "<div style=\"%s;font-family:%s;margin-top:4px\">%d nodes, %d primitives</div>"
                    Muted
                    Mono
                    (Scene.count n)
                    (Scene.primitiveCount n)

            "<div>" + scroller svg + census + "</div>")

    /// A scene at `defaultSize`.
    let node (n: Node) : string = nodeAt defaultSize n

    // ---- Films -----------------------------------------------------------------------
    //
    // A FILM PLAYS IN THE CELL, AS A FLIPBOOK OF ITS OWN FRAMES.
    //
    // An animation has three ways out: a numbered directory, an `.mp4` and — with
    // `Codec = None` — a `.gif`. All three end in a file, and two of them need ffmpeg. None of
    // them is a notebook, which is where people first meet the library, so without this `Anim`,
    // `Film`, `Story` and `Shot` would display as a record field table, invisible in the one
    // place they should be easiest to see.
    //
    // **Why SMIL and not CSS.** Rule 1 of this file's header is absolute — no `<style>` block,
    // ever, because a notebook output cell does not scope one: it leaks into the whole document,
    // outlives the cell, and stacks up once per execution. CSS `@keyframes` is an at-rule and
    // cannot be written as a `style=` attribute, so the obvious flipbook is the one thing this
    // file may not do. `<animate>` is an SVG *element*: it lives inside the document fragment it
    // animates, is carried by the same string, and cannot escape it. Rule 1 is what chose the
    // mechanism, and it chose a better one.
    //
    // **Why not a GIF or an `<img>` of a video.** Both would put `Marey.Tools.Video` — and
    // therefore ffmpeg — on the *display* path. A reader without ffmpeg would then get a still
    // where a colleague gets motion, from the same notebook, with nothing saying so. That is the
    // silent degradation refused for a font and for an encoder, in the one place
    // where the reader cannot check. A flipbook needs no tool at all: `Marey.Html` already
    // depends on `Marey.Backend.Svg`, and this adds no reference to anything.
    //
    // **Why the ids are prefixed.** SVG ids are scoped to the document, not to a nested `<svg>`,
    // so N independently rendered frames in one outer document share one id space. Frame 5's
    // `url(#qc)` would resolve to frame 0's clip path and its `<use xlink:href="#qm">` to frame
    // 0's marker. That is invisible exactly while the frames agree — and `highlight` makes one
    // mark bigger, so the reel is a scene where they do not. `SvgBackend.renderScoped` exists
    // for this and for nothing else.
    //
    // **What this does not do: rewrite the SVG it embeds.** The frames are wrapped, never
    // edited — each one goes inside a `<g>` that carries the `<animate>`, which is why the film
    // renders at `Film.Extent` and takes no size argument. That is the header's last paragraph
    // holding: a film wider than the cell scrolls, it does not shrink, because its text was
    // measured at the size it was arranged for.

    /// How many frames a film is sampled into when nobody says otherwise.
    ///
    /// **A budget, not a frame rate**, and it is the honest way round: the cost of a flipbook is
    /// linear in frames at about 11 KB of SVG each, and what a notebook cannot afford is total
    /// size rather than a low rate. 48 frames is roughly half a megabyte of markup — which gzip
    /// takes to about 40 KB, because consecutive frames are nearly identical text.
    ///
    /// The consequence is stated rather than hidden: the effective frame rate is
    /// `48 / duration`, so a 2 s film is smooth at 24 fps and a 5.5 s one is visibly stepped at
    /// 8.7. Raise it with `filmAt` when the film is long and the cell can carry the bytes.
    [<Literal>]
    let FlipbookFrames = 48

    /// A film, sampled into `frames` stills and played as a flipbook.
    ///
    /// Rendered at the film's own extent, because that is the surface its layout was arranged
    /// against and every frame's text was measured for.
    ///
    /// `frames` has no default here on purpose — the same reason `Chart.arrangeUnion` has no
    /// default sample count. It is a real trade against cell size and a caller should see the
    /// number they are choosing; `film` is where the choice is written down.
    let filmAt (frames: int) (film: Film) : string =
        guarded "film" (fun () ->
            let n = max 1 frames
            let extent = film.Extent
            let w = float extent.Width
            let h = float extent.Height

            // One frame is a still. Not a special case for tidiness: `keyTimes` for a
            // single-frame cycle degenerates to `0;1`, which is a frame that is visible for the
            // whole cycle and then hidden for none of it — an `<animate>` that animates nothing,
            // emitted so a reader can wonder what it is for.
            if n = 1 then
                scroller (SvgBackend.render extent (film.Frames.At(0.0<s>)))
            else

            let duration = float film.Duration

            // A film of zero length has no cycle to divide. It is a still of its only instant,
            // and `dur="0s"` is what SMIL calls indefinite — which would freeze on frame 0 and
            // look like a bug in the animation rather than a property of the film.
            if duration <= 0.0 then
                scroller (SvgBackend.render extent (film.Frames.At(0.0<s>)))
            else

            let sb = StringBuilder(n * 12_000)

            sb
                .Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"")
                .Append(num w)
                .Append("\" height=\"")
                .Append(num h)
                .Append("\" viewBox=\"0 0 ")
                .Append(num w)
                .Append(" ")
                .Append(num h)
                .Append("\">\n")
            |> ignore

            for i in 0 .. n - 1 do
                let node = film.Frames.At(float i / float n * film.Duration)

                // `f0-`, `f1-`, … — a legal NCName start, and the only thing that keeps frame
                // i's clip path, marker and `Keyed` names out of frame j's.
                let svg = SvgBackend.renderScoped ("f" + intText i + "-") extent node

                // Each frame is opaque for exactly its own slice of the cycle. `calcMode` is
                // discrete because a frame is either shown or not — interpolating opacity would
                // cross-fade every pair of neighbours, which is a different film.
                //
                // `keyTimes` for discrete need not end at 1; the last value is held to the end
                // of the cycle, which is why the first and last frames take two stops and every
                // frame between them takes three.
                let t0 = Num.upTo 6 (float i / float n)
                let t1 = Num.upTo 6 (float (i + 1) / float n)

                let values, keyTimes =
                    if i = 0 then "1;0", "0;" + t1
                    elif i = n - 1 then "0;1", "0;" + t0
                    else "0;1;0", "0;" + t0 + ";" + t1

                sb
                    .Append("<g opacity=\"0\"><animate attributeName=\"opacity\" values=\"")
                    .Append(values)
                    .Append("\" keyTimes=\"")
                    .Append(keyTimes)
                    .Append("\" calcMode=\"discrete\" dur=\"")
                    .Append(Num.upTo 3 duration)
                    .Append("s\" repeatCount=\"indefinite\"/>")
                    .Append(svg)
                    .Append("</g>\n")
                |> ignore

            sb.Append("</svg>\n") |> ignore

            let caption =
                sprintf
                    "<div style=\"%s;font-family:%s;margin-top:4px\">%d frames · %ss · %s × %s px</div>"
                    Muted
                    Mono
                    n
                    (num duration)
                    (num w)
                    (num h)

            "<div>" + scroller (sb.ToString()) + caption + "</div>")

    /// A film at `FlipbookFrames`.
    let film (f: Film) : string = filmAt FlipbookFrames f

    // ---- Colour ----------------------------------------------------------------------

    /// A colour: the chip, its hex, and its Oklch coordinates.
    ///
    /// Oklch is shown rather than HSL because it is the space the library actually works in
    /// — `Color.mix`, every ramp, and the whole palette validator are Oklab. A hue in
    /// degrees and a chroma you can compare between two colours is worth more here than a
    /// second spelling of the same sRGB triple.
    let color (c: Color) : string =
        let lch = Color.toOklch c

        // `Color.toOklch` builds its hue from `atan2`, so it arrives in (-180°, 180°]. Displaying
        // `-115.95°` for a blue is correct and useless: nobody publishes hues that way, and the
        // library's own vocabulary is 0–360 — `Palette.divergingBetween` is called with 250° and
        // 27°. Normalising here rather than in `Color.toOklch` keeps a presentation choice out of
        // Core, where the value feeds `ofOklch` and cares only about being an angle.
        let hue =
            let d = Space.degOfRad lch.H / 1.0<deg>
            let wrapped = d % 360.0

            if wrapped < 0.0 then wrapped + 360.0 else wrapped

        let alpha =
            if c.A >= 1.0 then
                ""
            else
                sprintf " · alpha %s" (num c.A)

        sprintf
            "<div style=\"%s;font-family:%s;font-size:0.9em\">%s<span>%s</span><span style=\"%s\">oklch(%s %s %s°)%s</span></div>"
            Row
            Mono
            (chip 22 c)
            (text (Color.toHex c))
            Muted
            (num lch.L)
            (num lch.C)
            (num hue)
            alpha

    // ---- Ramps and palettes ----------------------------------------------------------

    /// How many samples a ramp bar is drawn from.
    ///
    /// **Not** the ramp's own stop list. A CSS gradient interpolates in sRGB, and the entire
    /// reason `Ramp` stores Oklab stops is that sRGB interpolation across a diverging or
    /// viridis ramp goes muddy through the middle — handing the browser the stops directly
    /// would display precisely the artefact the type exists to avoid. Resampling through
    /// `Ramp.at` puts Oklab-correct colours 1.6% apart and leaves the browser nothing to guess
    /// at, which is the same argument `Ramp`'s own doc comment makes about resampling a
    /// ten-stop ramp into a 256-entry table.
    [<Literal>]
    let private RampSamples = 64

    let private rampGradient (r: Ramp) =
        let stops =
            Array.init RampSamples (fun i ->
                let t = float i / float (RampSamples - 1)
                sprintf "%s %s%%" (cssColor (Ramp.at r t)) (num (t * 100.0)))

        "linear-gradient(to right," + System.String.Join(",", stops) + ")"

    /// A ramp: its name, a bar, and its stop count.
    let ramp (r: Ramp) : string =
        sprintf
            "<div style=\"font-family:%s;font-size:0.9em\"><div style=\"%s\"><span>%s</span><span style=\"%s\">%d stops</span></div><div style=\"height:22px;margin-top:4px;border:%s;border-radius:3px;background-image:%s\"></div></div>"
            Mono
            Row
            (text r.Name)
            Muted
            r.Stops.Length
            Border
            (rampGradient r)

    /// A categorical palette: one chip per entry, indexed.
    ///
    /// Registered against `Color[]`, which is what a palette *is* in this library — there is no
    /// `Palette` type, only the module. That means `theme.Categorical` displays as a strip too,
    /// which is the right answer and was not extra work.
    let palette (cs: Color[]) : string =
        let b = StringBuilder(128 + cs.Length * 200)

        b.Append("<div style=\"").Append(Row).Append(";font-family:").Append(Mono).Append(";font-size:0.9em\">")
        |> ignore

        cs
        |> Array.iteri (fun i c ->
            b
                .Append("<div style=\"text-align:center\">")
                .Append(chip 26 c)
                .Append("<div style=\"")
                .Append(Muted)
                .Append("\">")
                .Append(intText i)
                .Append("</div><div style=\"")
                .Append(Muted)
                .Append("\">")
                .Append(text (Color.toHex c))
                .Append("</div></div>")
            |> ignore)

        b.Append("</div>").ToString()

    /// What `Palette.validate` found.
    ///
    /// Included because a theme is validated for contrast and colour-vision safety, "because a
    /// physics library that renders fields in jet is embarrassing", and a validator whose output is
    /// a record you have to squint at is only half of that. Four rows, one per vision model, is the
    /// whole story.
    let paletteReport (r: PaletteReport) : string =
        let b = StringBuilder(512)

        let verdict =
            if r.Ok then
                "passes"
            else
                "FAILS"

        b
            .Append("<div style=\"font-family:")
            .Append(Mono)
            .Append(";font-size:0.9em\"><div style=\"")
            .Append(Row)
            .Append("\"><span>")
            .Append(intText r.Size)
            .Append(" colours, ")
            .Append(verdict)
            .Append("</span><span style=\"")
            .Append(Muted)
            .Append("\">floor ")
            .Append(num Palette.SeparationFloor)
            .Append(" separation, ")
            .Append(num Palette.ContrastFloor)
            .Append(":1 contrast</span></div>")
        |> ignore

        b
            .Append("<table style=\"border-collapse:collapse;margin-top:6px\"><tr style=\"")
            .Append(Muted)
            .Append("\"><th style=\"text-align:left;padding:2px 10px 2px 0\">vision</th><th style=\"text-align:right;padding:2px 10px 2px 0\">min Δ</th><th style=\"text-align:left;padding:2px 0\">closest pair</th></tr>")
        |> ignore

        for s in r.Separations do
            // The worst row is the one the verdict came from, so it is the one worth finding at
            // a glance. Bold rather than red: red on a dark theme is unreadable, and rule 2
            // forbids pinning a colour anyway.
            let weight =
                if s = r.WorstSeparation then
                    "font-weight:600"
                else
                    ""

            b
                .Append("<tr style=\"")
                .Append(weight)
                .Append("\"><td style=\"padding:2px 10px 2px 0\">")
                .Append(text s.Vision)
                .Append("</td><td style=\"text-align:right;padding:2px 10px 2px 0\">")
                .Append(num s.Minimum)
                .Append("</td><td style=\"padding:2px 0\">")
                .Append(intText s.IndexA)
                .Append(" &amp; ")
                .Append(intText s.IndexB)
                .Append("</td></tr>")
            |> ignore

        b
            .Append("</table><div style=\"")
            .Append(Muted)
            .Append(";margin-top:4px\">weakest contrast ")
            .Append(num r.MinContrast)
            .Append(":1 at entry ")
            .Append(intText r.MinContrastIndex)
            .Append("</div></div>")
        |> ignore

        b.ToString()

    // ---- Dimensions, unit witnesses, scales ------------------------------------------

    /// A small definition list: a term, then its value. The shape shared by the three
    /// type-level things below, all of which are "a handful of named facts".
    let private facts (pairs: (string * string) list) =
        let b = StringBuilder(128 + pairs.Length * 96)

        b
            .Append("<div style=\"display:grid;grid-template-columns:auto auto;gap:2px 12px;font-family:")
            .Append(Mono)
            .Append(";font-size:0.9em;justify-content:start\">")
        |> ignore

        for (k, v) in pairs do
            b.Append("<div style=\"").Append(Muted).Append("\">").Append(text k).Append("</div><div>").Append(v).Append("</div>")
            |> ignore

        b.Append("</div>").ToString()

    /// A physical dimension, in three spellings: Core's fallback, the SI unit it implies, and
    /// the base-dimension exponents.
    ///
    /// The middle one is the interesting one — `Dim.length / Dim.time^2` displaying as `m/s²`
    /// is `Marey.Units` answering a question `Marey.Core` cannot, and seeing both side by
    /// side is how you notice a registry is not loaded.
    let dim (d: Dim) : string =
        let exps =
            match Dim.exponents d with
            | [] -> "dimensionless"
            | xs -> xs |> List.map (fun (s, e) -> sprintf "%s=%d" s e) |> String.concat ", "

        facts
            [ "dimension", text (Dim.show d)
              "SI unit", text (UnitDisplay.renderSi Unicode d)
              "exponents", text exps ]

    /// A unit witness: the value that carries a measure's meaning at runtime.
    ///
    /// Generic in the measure, which costs nothing here: F# erases a measure in a *class's*
    /// type argument as thoroughly as in `float<'u>`, so `U<m>` and `U<s>` are one runtime type
    /// and one registration covers every unit there will ever be. `Formatters.fs` relies on
    /// that; this signature is why it is allowed to.
    let witness (u: U<'u>) : string =
        let symbol =
            if System.String.IsNullOrEmpty u.Symbol then
                "(dimensionless)"
            else
                u.Symbol

        let conversion =
            if u.IsAffine then
                sprintf "x × %s + %s" (num u.ToSI) (num u.OffsetSI)
            else
                sprintf "x × %s" (num u.ToSI)

        facts
            [ "symbol", text symbol
              "dimension", text (Dim.show u.Dim)
              "to SI", conversion ]

    /// An erased scale: the axis as the backend sees it.
    let scale (s: IScaleErased) : string =
        let kind =
            match s.Kind with
            | ScaleKind.Linear -> "linear"
            | ScaleKind.Log b -> sprintf "log base %s" (num b)
            | ScaleKind.Symlog(b, t) -> sprintf "symlog base %s, linthresh %s" (num b) (num t)
            | ScaleKind.Power e -> sprintf "power %s" (num e)
            | ScaleKind.Reciprocal -> "reciprocal"
            | ScaleKind.Ordinal levels -> sprintf "ordinal, %d levels" levels.Length

        let (lo, hi) = s.Domain

        // Six is the point where the list stops being readable and starts being a table; the
        // count is always shown so a truncated list never looks like the whole one.
        let ticks = s.Ticks 6

        let tickText =
            if ticks.Length = 0 then
                "none"
            else
                sprintf
                    "%s (%d)"
                    (ticks |> Array.map (fun t -> t.Label) |> String.concat ", ")
                    ticks.Length

        facts
            [ "unit", text (if System.String.IsNullOrEmpty s.Symbol then "(dimensionless)" else s.Symbol)
              "dimension", text (Dim.show s.Dim)
              "scale", text kind
              "domain", text (sprintf "%s .. %s" (num lo) (num hi))
              "ticks", text tickText ]


    // ======================================================================================
    // THE HOST BRIDGE
    //
    // Two functions, called by reflection from `Marey.Verso`. **Their names and signatures are
    // load-bearing in a way the compiler cannot see**, which is why they are pinned by a test
    // that resolves them exactly as the extension does.
    //
    // Reflection rather than an interface, and not for want of taste. A Verso extension loads
    // into its own collectible `AssemblyLoadContext`; only `Verso.Abstractions` is shared from
    // the default context. Marey arrives in the *kernel's* context through the notebook's
    // `#r`. So a `Figure` the user made and a `Figure` the extension referenced would be two
    // unequal `System.Type`s, an interface implemented here would not be the interface the
    // extension knows, and a cast between them throws. Reflecting on the value's own type
    // sidesteps all of it: `MethodInfo.Invoke` needs no shared identity.
    //
    // The consequence, stated so it is not discovered later: `Marey.Verso` has **no compile-time
    // dependency on Marey at all**. It is a bridge, not an integration, and it keeps working
    // across Marey versions as long as these two signatures hold.
    // ======================================================================================
    // A LIVE FIGURE'S DOCUMENT
    //
    // Not a fragment: a whole page, which a host draws in a frame of its own (Verso's
    // `text/x-verso-widget`). Rule 1 therefore does not bind inside it — a `<style>` in a framed
    // document styles that document and nothing else — and the page uses one. Rules 2 to 4 still
    // hold. Rule 2 has a better answer here than in a fragment, because the host passes its theme
    // into the frame as custom properties: foreground and background are taken together from the
    // host, and fall back to pinning nothing.
    //
    // **The script formats no number.** Every label it shows arrives from the kernel, written by
    // `UnitDisplay.quantityIn`; the script reads an input's value, posts it, and swaps in what
    // comes back. That keeps one formatter in one language until the Fable backend can make it
    // two languages and still one formatter.
    //
    // **Well-formed XML as well as HTML**, so the oracle `NotebookTests` holds every fragment to
    // holds this page too: self-closed inputs, valued boolean attributes, and the script in a
    // CDATA section, because `&&` is an error in XML and ordinary in JavaScript.
    //
    // **The controls are written from the state**, not from their initial values. A snapshot
    // taken after the reader moved a slider is a document of that state, and its slider has to
    // be where the figure says it is. An early version showed a 3.7 Hz figure under a slider
    // at 1 Hz, which is the bug this sentence is about.
    // ======================================================================================

    /// A number for an attribute a browser parses rather than a person reads: round-trip
    /// exact, invariant. A slider over nanometres has a step of 1E-10, which `Num.upTo` would round
    /// to zero, and an HTML number may carry an exponent.
    let private exact (v: float) = v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)

    let private controlHtml (enabled: bool) (i: int) (c: ControlSpec) (v: float) =
        let disabled = if enabled then "" else " disabled=\"disabled\""

        let input =
            match c.Kind with
            | Slider(lo, hi, step) ->
                sprintf
                    "<input type=\"range\" data-i=\"%d\" min=\"%s\" max=\"%s\" step=\"%s\" value=\"%s\"%s/>"
                    i (exact lo) (exact hi) (exact step) (exact v) disabled
            | Select options ->
                let chosen = int (System.Math.Round v)

                let opts =
                    options
                    |> Array.mapi (fun k o ->
                        sprintf "<option value=\"%d\"%s>%s</option>" k (if k = chosen then " selected=\"selected\"" else "") (text o))
                    |> String.concat ""

                sprintf "<select data-i=\"%d\"%s>%s</select>" i disabled opts
            | Checkbox -> sprintf "<input type=\"checkbox\" data-i=\"%d\"%s%s/>" i (if v <> 0.0 then " checked=\"checked\"" else "") disabled
            // A range input the page's script plays on its own clock: the button starts
            // and stops it, and every frame it plays is a `set`, as a drag of the input is.
            | Time(lo, hi, _, loop) ->
                sprintf
                    "<input type=\"range\" data-i=\"%d\" min=\"%s\" max=\"%s\" step=\"%s\" value=\"%s\"%s/><button type=\"button\" data-play=\"%d\" data-loop=\"%d\" title=\"Play\"%s>\u25B6</button>"
                    i (exact lo) (exact hi) (exact ((hi - lo) / 1000.0)) (exact v) disabled i (if loop then 1 else 0) disabled

        sprintf "<label><span>%s</span>%s<output id=\"v%d\">%s</output></label>" (text c.Label) input i (text (c.Show v))

    /// The page's style. Scoped by being inside its own document.
    let private widgetStyle =
        String.concat
            ""
            [ "html,body{margin:0;padding:0}"
              "body{color:var(--verso-cell-output-foreground,inherit);background:var(--verso-cell-output-background,transparent);"
              "font-family:var(--verso-ui-font-family,system-ui,sans-serif);font-size:var(--verso-ui-font-size,14px)}"
              "#controls{" + Row + ";margin:0 0 6px}"
              "#controls label{display:inline-flex;gap:6px;align-items:center}"
              "#controls output{font-variant-numeric:tabular-nums;min-width:5em}"
              "#controls button{font:inherit;min-width:2.2em}"
              "#stage{position:relative}"
              "#fig{max-width:100%;overflow-x:auto;touch-action:none;user-select:none;-webkit-user-select:none}"
              "#band{position:absolute;display:none;pointer-events:none;border:1px dashed currentColor;background:rgba(127,127,127,0.12)}"
              // Over the data, so it needs a ground of its own: the host's background under a grey
              // wash, and the wash alone where the host gives none, since rule 2 pins no colour.
              "#readout{position:absolute;display:none;pointer-events:none;padding:2px 6px;border-radius:3px;"
              "border:" + Border + ";background:linear-gradient(rgba(127,127,127,0.14),rgba(127,127,127,0.14)),"
              "var(--verso-cell-output-background,transparent);font-size:0.85em;font-variant-numeric:tabular-nums;white-space:nowrap}"
              "#status{" + Muted + "}" ]

    /// The view's half of the protocol. It measures the frame and says how wide it is, reports
    /// itself ready, posts a control's value whenever it moves with one request in flight and
    /// the newest value of each control waiting behind it, swaps in whatever the kernel sends,
    /// and disables the controls when the channel closes. With no channel it disables them at
    /// once and does nothing else.
    ///
    /// A reply carries either the whole figure (`svg`) or a patch: `defs`, which replaces
    /// the figure's `<defs>` whole, and `patch`, ids and markup in turn, each swapped in for the
    /// element with that id. A patch naming an element the view does not have means the view
    /// and the kernel disagree about what is drawn, so the view says hello again, and the
    /// kernel answers a `size` with the whole figure.
    ///
    /// A drag on the figure draws a band and posts its two corners in the figure's own
    /// px when the pointer comes up; with shift held it posts the pointer's movement instead,
    /// summing what waits behind the request in flight so no movement is dropped; a double
    /// click posts a reset. The script works out which pixel the pointer is on and nothing else:
    /// which panel that is, and what it measures, is the kernel's.
    ///
    /// The pointer's position over the figure is posted as it moves, under one key so
    /// only the newest waits, and its leaving is posted under the same key. What it reads comes
    /// back as `readout`: the plot rectangle in figure px, the corner, and the lines, which the
    /// script places and does not read.
    ///
    /// Every message carries one number more than its type needs: the event's
    /// `timeStamp`, by the page's monotonic clock, in milliseconds. A message waiting behind the
    /// one in flight carries the newest event's time with the newest value. The kernel reads no
    /// clock, so a recording's times are the reader's.
    ///
    /// On a canvas page (`canvas = true`) the figure is a `<canvas>` that `MareyView`
    /// draws from the scene's bytes in the page, first and with or without a channel. A reply
    /// carries `scene` with its size, or `wpatch`, in place of `svg` and `patch`, and a pixel is
    /// placed against the canvas's size in px. An SVG page's script is unchanged, line for line.
    let private widgetScript (aspect: float) (canvas: bool) =
        String.concat
            "\n"
            [ yield "<script>//<![CDATA["
              yield "(function () {"
              yield "  var ch = window.versoChannel;"
              yield "  var fig = document.getElementById('fig');"
              yield "  var status = document.getElementById('status');"
              yield "  var inputs = Array.prototype.slice.call(document.querySelectorAll('[data-i]'));"
              yield "  var plays = Array.prototype.slice.call(document.querySelectorAll('[data-play]'));"

              if canvas then
                  yield "  var cv = document.getElementById('cv');"
                  yield "  var poster = document.getElementById('poster');"
                  yield "  function draw(b64, w, h) { cv.setAttribute('data-w', w); cv.setAttribute('data-h', h); var why = MareyView.show(cv, b64, Number(w), Number(h)); if (why) { status.textContent = why; } else if (poster) { poster.remove(); poster = null; } }"
                  yield "  draw(document.getElementById('scene').textContent, cv.getAttribute('data-w'), cv.getAttribute('data-h'));"

              yield "  function disable(why) {"
              yield "    inputs.concat(plays).forEach(function (el) { el.disabled = true; });"
              yield "    if (why) { status.textContent = why; }"
              yield "  }"
              yield "  if (!ch) { disable(null); return; }"
              yield "  var busy = false, waiting = {}, order = [];"
              yield "  function flush() {"
              yield "    if (busy || order.length === 0) { return; }"
              yield "    var key = order.shift(), m = waiting[key];"
              yield "    delete waiting[key];"
              yield "    busy = ch.post(m.type, m.payload.concat([m.t]));"
              yield "  }"
              yield "  function send(key, type, payload, t, merge) {"
              yield "    if (!(key in waiting)) { order.push(key); }"
              yield "    else if (merge) { payload = merge(waiting[key].payload, payload); }"
              yield "    waiting[key] = { type: type, payload: payload, t: t };"
              yield "    flush();"
              yield "  }"
              yield "  function valueOf(el) { return el.type === 'checkbox' ? (el.checked ? 1 : 0) : Number(el.value); }"
              yield "  inputs.forEach(function (el) {"
              yield "    var i = Number(el.getAttribute('data-i'));"
              yield "    el.addEventListener(el.type === 'range' ? 'input' : 'change', function (e) {"
              yield "      send('c' + i, 'set', [i, valueOf(el)], e.timeStamp);"
              yield "    });"
              yield "  });"
              // Play advances a time control one second a second from where it was, on
              // the page's clock, and posts each frame's value as a `set` stamped with that
              // frame's time. Scrubbing the input pauses it. The value sent is the exact one; the
              // input shows it rounded to its step.
              yield "  var playing = {};"
              yield "  plays.forEach(function (b) {"
              yield "    var i = Number(b.getAttribute('data-play')), loop = b.getAttribute('data-loop') === '1';"
              yield "    var el = inputs.filter(function (x) { return Number(x.getAttribute('data-i')) === i; })[0];"
              yield "    var lo = Number(el.min), hi = Number(el.max);"
              yield "    function stop() { delete playing[i]; b.textContent = '\u25B6'; b.title = 'Play'; }"
              yield "    function tick(now) {"
              yield "      var p = playing[i];"
              yield "      if (!p || closed) { return; }"
              yield "      var v = p.from + (now - p.start) / 1000;"
              yield "      if (v > hi) { if (loop) { v = lo + (v - lo) % (hi - lo); } else { v = hi; stop(); } }"
              yield "      el.value = v;"
              yield "      send('c' + i, 'set', [i, v], now);"
              yield "      if (playing[i]) { requestAnimationFrame(tick); }"
              yield "    }"
              yield "    b.addEventListener('click', function () {"
              yield "      if (playing[i]) { stop(); return; }"
              yield "      var from = valueOf(el);"
              yield "      if (from >= hi && !loop) { from = lo; }"
              yield "      playing[i] = { start: performance.now(), from: from };"
              yield "      b.textContent = '\u275A\u275A'; b.title = 'Pause';"
              yield "      requestAnimationFrame(tick);"
              yield "    });"
              yield "    el.addEventListener('input', function (e) { if (e.isTrusted && playing[i]) { stop(); } });"
              yield "  });"
              yield "  var SVG = 'http://www.w3.org/2000/svg';"
              yield "  function parse(text) {"
              yield "    var d = new DOMParser().parseFromString('<svg xmlns=\"' + SVG + '\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">' + text + '</svg>', 'image/svg+xml');"
              yield "    var el = d.documentElement.firstElementChild;"
              yield "    return el && el.namespaceURI === SVG ? document.importNode(el, true) : null;"
              yield "  }"
              yield "  function patch(defs, parts) {"
              yield "    var svg = fig.querySelector('svg');"
              yield "    if (!svg) { return false; }"
              yield "    for (var i = 0; i < parts.length; i += 2) { if (!document.getElementById(parts[i])) { return false; } }"
              yield "    var old = svg.querySelector('defs'), fresh = defs ? parse(defs) : null;"
              yield "    if (defs && !fresh) { return false; }"
              yield "    if (old) { old.remove(); }"
              yield "    if (fresh) { svg.insertBefore(fresh, svg.firstChild); }"
              yield "    for (var j = 0; j < parts.length; j += 2) {"
              yield "      var el = parse(parts[j + 1]);"
              yield "      if (!el) { return false; }"
              yield "      document.getElementById(parts[j]).replaceWith(el);"
              yield "    }"
              yield "    return true;"
              yield "  }"
              yield "  function hello() {"
              yield "    var w = Math.round(fig.clientWidth || document.documentElement.clientWidth || 0);"
              yield sprintf "    if (w > 0) { send('size', 'size', [w, Math.round(w * %s)], performance.now()); }" (exact aspect)
              yield "  }"
              yield "  window.addEventListener('verso:channelmessage', function (e) {"
              yield "    var d = e.detail || {}, p = d.payload || {};"
              yield "    if (d.type !== 'render') { return; }"
              if canvas then
                  yield "    if (typeof p.scene === 'string') { draw(p.scene, p.w, p.h); }"
                  yield "    else if (typeof p.wpatch === 'string' && !MareyView.patch(p.wpatch)) { hello(); }"
              else
                  yield "    if (typeof p.svg === 'string') { fig.innerHTML = p.svg; }"
                  yield "    else if (p.patch && !patch(p.defs || '', p.patch)) { hello(); }"
              yield "    (p.shown || []).forEach(function (s, i) {"
              yield "      var o = document.getElementById('v' + i);"
              yield "      if (o) { o.textContent = s; }"
              yield "    });"
              yield "    show(p.readout);"
              yield "    busy = false;"
              yield "    flush();"
              yield "  });"
              yield "  var stage = document.getElementById('stage'), band = document.getElementById('band');"
              yield "  var readout = document.getElementById('readout');"
              yield "  var drag = null, closed = false;"
              yield "  function frame() {"
              if canvas then
                  yield "    var r = cv.getBoundingClientRect(), s = stage.getBoundingClientRect();"
                  yield "    var w = Number(cv.getAttribute('data-w')), h = Number(cv.getAttribute('data-h'));"
                  yield "    var kx = w && r.width ? w / r.width : 1;"
                  yield "    var ky = h && r.height ? h / r.height : 1;"
              else
                  yield "    var svg = fig.querySelector('svg');"
                  yield "    if (!svg) { return null; }"
                  yield "    var r = svg.getBoundingClientRect(), s = stage.getBoundingClientRect();"
                  yield "    var vb = svg.viewBox && svg.viewBox.baseVal;"
                  yield "    var kx = vb && vb.width && r.width ? vb.width / r.width : 1;"
                  yield "    var ky = vb && vb.height && r.height ? vb.height / r.height : 1;"
              yield "    return { r: r, s: s, kx: kx, ky: ky };"
              yield "  }"
              yield "  function at(e) {"
              yield "    var f = frame();"
              yield "    if (!f) { return null; }"
              yield "    return { x: (e.clientX - f.r.left) * f.kx, y: (e.clientY - f.r.top) * f.ky, bx: e.clientX - f.s.left, by: e.clientY - f.s.top };"
              yield "  }"
              yield "  function show(r) {"
              yield "    var f = frame();"
              yield "    if (closed || !f || !r || r.length < 6) { readout.style.display = 'none'; return; }"
              yield "    readout.textContent = '';"
              yield "    r.slice(5).forEach(function (line) {"
              yield "      var d = document.createElement('div');"
              yield "      d.textContent = line;"
              yield "      readout.appendChild(d);"
              yield "    });"
              yield "    var right = r[4] === 'right', x = Number(right ? r[2] : r[0]) / f.kx + f.r.left - f.s.left;"
              yield "    readout.style.left = (right ? x - 4 : x + 4) + 'px';"
              yield "    readout.style.top = (Number(r[1]) / f.ky + f.r.top - f.s.top + 4) + 'px';"
              yield "    readout.style.transform = right ? 'translateX(-100%)' : '';"
              yield "    readout.style.display = 'block';"
              yield "  }"
              yield "  function sum(a, b) { return [a[0], a[1], a[2] + b[2], a[3] + b[3]]; }"
              yield "  fig.addEventListener('pointerdown', function (e) {"
              yield "    if (e.button !== 0 || closed) { return; }"
              yield "    var p = at(e);"
              yield "    if (!p) { return; }"
              yield "    drag = { start: p, last: p, pan: e.shiftKey };"
              yield "    fig.setPointerCapture(e.pointerId);"
              yield "    e.preventDefault();"
              yield "  });"
              yield "  fig.addEventListener('pointermove', function (e) {"
              yield "    var p = at(e), a = drag && drag.start;"
              yield "    if (!p || closed) { return; }"
              yield "    if (drag && drag.pan) {"
              yield "      send('pan', 'pan', [a.x, a.y, p.x - drag.last.x, p.y - drag.last.y], e.timeStamp, sum);"
              yield "      drag.last = p;"
              yield "      return;"
              yield "    }"
              yield "    send('hover', 'hover', [p.x, p.y], e.timeStamp);"
              yield "    if (!drag) { return; }"
              yield "    band.style.left = Math.min(a.bx, p.bx) + 'px';"
              yield "    band.style.top = Math.min(a.by, p.by) + 'px';"
              yield "    band.style.width = Math.abs(p.bx - a.bx) + 'px';"
              yield "    band.style.height = Math.abs(p.by - a.by) + 'px';"
              yield "    band.style.display = 'block';"
              yield "  });"
              yield "  function release(e) {"
              yield "    if (!drag) { return; }"
              yield "    var p = at(e), a = drag.start, pan = drag.pan;"
              yield "    drag = null;"
              yield "    band.style.display = 'none';"
              yield "    if (p && !pan && e.type === 'pointerup') { send('zoom', 'zoom', [a.x, a.y, p.x, p.y], e.timeStamp); }"
              yield "  }"
              yield "  fig.addEventListener('pointerup', release);"
              yield "  fig.addEventListener('pointercancel', release);"
              yield "  fig.addEventListener('dblclick', function (e) { if (!closed) { send('reset', 'reset', [], e.timeStamp); } });"
              yield "  fig.addEventListener('pointerleave', function (e) { if (!closed) { send('hover', 'leave', [], e.timeStamp); } });"
              yield "  window.addEventListener('verso:channelclosed', function (e) {"
              yield "    closed = true;"
              yield "    show(null);"
              yield "    disable((e.detail && e.detail.reason) || 'closed');"
              yield "  });"
              yield "  ch.ready('1.0');"
              yield "  hello();"
              yield "})();"
              yield "//]]></script>" ]

    /// **The script a canvas page draws a scene with**: `Marey.Core` and the canvas painter
    /// compiled by Fable from `src/Marey.Html.View`, bundled into one classic script that defines
    /// `MareyView.show` and `MareyView.patch`. Embedded in this assembly, read once.
    let viewScript: Lazy<string> =
        lazy
            (use stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream "marey-view.js"
             use reader = new System.IO.StreamReader(stream, Encoding.UTF8)
             reader.ReadToEnd())

    /// **The drawn elements from which `RenderMode.Auto` uses a canvas.** Measured in the
    /// built-in browser on 2026-10-11 (Chromium 152): a scatter's SVG and its canvas page, script
    /// included, are ready in about the same time near 3000 marks, the canvas three times sooner
    /// at 10⁴ and twenty at 10⁵. Below 5000 the SVG is never the slower.
    [<Literal>]
    let CanvasThreshold = 5000

    /// How figures are drawn from now on; `RenderMode.Auto` unless set.
    let mutable renderMode = RenderMode.Auto

    /// Whether a scene is drawn on a canvas under the current `renderMode`.
    let drawsOnCanvas (scene: Node) : bool =
        match renderMode with
        | RenderMode.Svg -> false
        | RenderMode.Canvas -> true
        | RenderMode.Auto -> Scene.primitiveCount scene >= CanvasThreshold

    /// The measurer a live figure is laid out with (`Live.fs`, one for the whole widget), whose
    /// metrics a canvas page draws its text with.
    let private liveMeasurer = TextMeasure.approx

    /// A scene's bytes for a canvas page, as base64: the scene and its text metrics.
    let private sceneBase64 (scene: Node) = System.Convert.ToBase64String(Wire.encodeWith liveMeasurer scene)

    /// A marker batch larger than this is left out of a canvas page's poster. A legend's
    /// swatch is a batch of one, so the poster keeps the legend.
    [<Literal>]
    let private PosterBatch = 16

    /// The scene without its large marker batches, and how many marks that left out.
    let private posterOf (scene: Node) : Node * int =
        let mutable dropped = 0

        let rec go (n: Node) =
            match n with
            | Markers m when m.Points.Length > PosterBatch ->
                dropped <- dropped + m.Points.Length
                Scene.empty
            | Group cs -> Group(Array.map go cs)
            | Transform(a, c) -> Transform(a, go c)
            | Clip(p, c) -> Clip(p, go c)
            | Opacity(a, c) -> Opacity(a, go c)
            | Keyed(k, c) -> Keyed(k, go c)
            | Annotated(a, c) -> Annotated(a, go c)
            | Path _
            | Text _
            | Markers _
            | Raster _ -> n

        let poster = go scene
        poster, dropped

    /// **A canvas figure, and what stands in for it without a script**: the poster, which
    /// is the scene's SVG without its large batches, under a canvas the page's script draws and
    /// which then removes the poster; and a line, shown only with scripting off, saying how many
    /// marks the poster leaves out. A reader whose viewer runs no script sees the axes, the titles
    /// and every small layer.
    let private canvasFigure (extent: Extent<px>) (scene: Node) : string =
        let poster, dropped = posterOf scene
        let w, h = exact (float extent.Width), exact (float extent.Height)

        sprintf "<div style=\"position:relative;width:%spx;height:%spx\"><div id=\"poster\">%s</div>" w h (SvgBackend.render extent poster)
        + sprintf "<canvas id=\"cv\" data-w=\"%s\" data-h=\"%s\" style=\"position:absolute;left:0;top:0\"></canvas></div>" w h
        + sprintf "<noscript><div style=\"%s\">%s marks are drawn by this page's script, which is not running here.</div></noscript>" Muted (intText dropped)

    /// The scene's bytes and the script that draws them, for a canvas page.
    let private canvasDrawing (scene: Node) : string =
        "<script type=\"text/plain\" id=\"scene\">" + sceneBase64 scene + "</script>"
        + "<script>//<![CDATA[\n" + viewScript.Force() + "\n//]]></script>"

    /// A live figure's SVG at a state, drawn in the state's arrangement. What a host sends back
    /// after each step; the document embeds the same thing.
    let widgetFigure (live: Live) (state: LiveState) : string =
        guarded "live figure" (fun () -> SvgBackend.render state.Shot.Outer.Extent (Live.scene live state))

    /// **Whether a live figure's page is a canvas**: decided by its scene the first time it
    /// is asked, and kept for as long as the figure lives, so a slider that carries the scene
    /// across the threshold does not leave the page and the kernel speaking two languages.
    let private pageKinds = System.Runtime.CompilerServices.ConditionalWeakTable<Live, obj>()

    let private onCanvas (live: Live) (state: LiveState) : bool =
        pageKinds.GetValue(live, fun l -> box (drawsOnCanvas (Live.scene l state))) |> unbox<bool>

    /// The whole scene for a canvas page: `scene`, its width and height in px, and its bytes.
    let private canvasWhole (live: Live) (state: LiveState) : string[] =
        let e = state.Shot.Outer.Extent
        [| "scene"; exact (float e.Width); exact (float e.Height); sceneBase64 (Live.scene live state) |]

    /// **A live figure as a whole page**: its controls, written from `state`, the figure at that
    /// state, and the view's script. `live = false` is the static form, for a host with nothing
    /// to talk back to and for a snapshot: the same page with the controls disabled and a line
    /// saying so.
    let widget (live: Live) (state: LiveState) (isLive: bool) : string =
        let controls =
            Array.mapi2 (controlHtml isLive) live.Controls state.Values
            |> String.concat ""

        let aspect = float state.Size.Height / float state.Size.Width
        let canvas = onCanvas live state

        // A canvas page carries its scene's bytes and the script that draws them, ahead of
        // the widget's own script, which calls it.
        let figure, drawing =
            if canvas then
                let scene = Live.scene live state
                canvasFigure state.Shot.Outer.Extent scene, canvasDrawing scene
            else
                widgetFigure live state, ""

        String.concat
            ""
            [ "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>"
              "<style>" + widgetStyle + "</style></head><body>"
              "<div id=\"controls\">" + controls + "</div>"
              "<div id=\"stage\"><div id=\"fig\""
              + (if isLive then " title=\"Drag to zoom, shift-drag to pan, double-click to reset\"" else "")
              + ">" + figure + "</div><div id=\"band\"></div><div id=\"readout\"></div></div>"
              "<div id=\"status\">" + (if isLive then "" else "Static: run the cell to move the controls.") + "</div>"
              drawing
              widgetScript aspect canvas
              "</body></html>" ]

    // ======================================================================================

    /// Every type this module can display. The match a host uses to decide whether to claim a
    /// value, kept separate from `renderForHost` so the decision costs a type test rather than a
    /// full render — a formatter's `CanFormat` is probed for every value a cell produces.
    let canRenderForHost (value: obj) : bool =
        match value with
        | :? Figure
        | :? Film
        | :? Node
        | :? Color
        | :? Ramp
        | :? (Color[])
        | :? PaletteReport
        | :? Dim
        // Erased measures make this one type test cover every unit witness there will ever be.
        | :? U<1>
        | :? IScaleErased
        | :? Live
        | :? Recorder -> true
        | _ -> false

    /// The size to compile a figure at, given what the host says is available.
    ///
    /// The .NET Interactive design had no way to ask how wide the cell was, so `defaultSize`
    /// picked 720x450 and hoped. Verso's `IFormatterContext` carries `MaxWidth`/`MaxHeight`, but
    /// on its F# path they are the constants 800 and 600, so the guess moved into the host and
    /// is still a guess. A live figure does better: its page measures its own frame and sends the
    /// width with its first message, and the kernel lays the figure out for that.
    ///
    /// Clamped at both ends. Below ~320px a figure is not a figure, it is a smudge with axis
    /// labels; above ~1100 the line weights and font sizes chosen for a screen figure start to
    /// look thin. Inside that band the cell decides.
    let extentForHost (maxWidth: float, maxHeight: float) : Extent<px> =
        if maxWidth <= 0.0 then
            defaultSize
        else
            let w = max 320.0 (min 1100.0 maxWidth)
            let h = w * 5.0 / 8.0

            if maxHeight > 0.0 && h > maxHeight then
                let h = max 200.0 maxHeight
                { Width = h * 8.0 / 5.0 * 1.0<px>
                  Height = h * 1.0<px> }
            else
                { Width = w * 1.0<px>; Height = h * 1.0<px> }

    /// Render any value `canRenderForHost` accepts. Returns `""` for anything else.
    ///
    /// An empty string rather than an option or a null: an `FSharpOption` crossing an
    /// `AssemblyLoadContext` boundary is a different type on each side, and null-handling through
    /// `MethodInfo.Invoke` is the kind of subtlety that fails once, in production, in somebody
    /// else's notebook. No renderer here can legitimately produce `""`, so the sentinel is
    /// unambiguous.
    ///
    /// Tupled deliberately: it compiles to a plain three-argument static method, which is what
    /// `Type.GetMethod` on the other side can find by signature.
    let renderForHost (maxWidth: float, maxHeight: float, value: obj) : string =
        let extent = extentForHost (maxWidth, maxHeight)

        match value with
        | :? Figure as f -> figureAt extent f
        // Not `extent`: a film's layout was arranged once against its own surface, and every
        // frame's text was measured for it. Re-sizing here would be the one thing the header
        // says this file does not do, applied to the value least able to survive it.
        | :? Film as f -> film f
        | :? Node as n -> nodeAt extent n
        | :? Color as c -> color c
        | :? Ramp as r -> ramp r
        | :? (Color[]) as cs -> palette cs
        | :? PaletteReport as r -> paletteReport r
        | :? Dim as d -> dim d
        | :? U<1> as u -> witness u
        | :? IScaleErased as sc -> scale sc
        // A host that can only take a fragment gets the figure at the controls' initial values,
        // in the widget's own arrangement. A host with a frame for a page uses `liveDocumentForHost`.
        | :? Live as l -> guarded "live figure" (fun () -> scroller (widgetFigure l (Live.init l)))
        | :? Recorder as r -> guarded "live figure" (fun () -> scroller (widgetFigure r.Live (Live.init r.Live)))
        | _ -> ""

    /// **A figure as a canvas page**, for a host that shows a page in a frame: the poster,
    /// the canvas, the scene's bytes and the script that draws them, and no controls. `None` when
    /// the figure is drawn as SVG under the current `renderMode`, which is what an `Html` output
    /// is for, because a host inserts that as markup and never runs its scripts.
    let figureDocument (extent: Extent<px>) (fig: Figure) : string option =
        let scene = Chart.toSceneWith liveMeasurer extent fig

        if drawsOnCanvas scene then
            Some(
                String.concat
                    ""
                    [ "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>"
                      "<style>html,body{margin:0;padding:0}body{color:var(--verso-cell-output-foreground,inherit);"
                      "background:var(--verso-cell-output-background,transparent);font-family:var(--verso-ui-font-family,system-ui,sans-serif)}"
                      "#fig{max-width:100%;overflow-x:auto}</style></head><body>"
                      "<div id=\"fig\">" + canvasFigure extent scene + "</div><div id=\"status\"></div>"
                      canvasDrawing scene
                      // The marker ends its line: the function after it on the same line would be
                      // inside the `//` comment, and never run.
                      "<script>//<![CDATA[\n"
                      "(function () { var cv = document.getElementById('cv'), poster = document.getElementById('poster');"
                      " var why = MareyView.show(cv, document.getElementById('scene').textContent, Number(cv.getAttribute('data-w')), Number(cv.getAttribute('data-h')));"
                      " if (why) { document.getElementById('status').textContent = why; } else if (poster) { poster.remove(); } })();"
                      "\n//]]></script></body></html>" ]
            )
        else
            None

    /// The page a host should show in a frame instead of `renderForHost`'s markup, or `""` when the
    /// markup is right: a `Figure` drawn on a canvas. Tupled for the bridge, as
    /// `renderForHost` is.
    let documentForHost (maxWidth: float, maxHeight: float, value: obj) : string =
        match value with
        // Not `guarded`: an error box is not empty, and would be shown as the page. A figure that
        // cannot be drawn here falls through to `renderForHost`, which shows the box in place.
        | :? Figure as f ->
            try
                defaultArg (figureDocument (extentForHost (maxWidth, maxHeight)) f) ""
            with _ ->
                ""
        | _ -> ""

    // ---- The live half of the bridge ------------------------------------------
    //
    // Six more functions the extension reaches by reflection, for the same reason as the two
    // above: nothing typed in a Marey assembly can cross the load-context boundary, so every
    // argument and result is `obj`, `string`, `string[]` or `bool`. The extension holds the state
    // as an `obj` it never looks inside, and hands it back.
    //
    // **None of them throws.** A view function is the reader's code, and a step that throws would
    // take the widget down from inside a message handler, where nobody sees the exception. Each
    // answers what it can: no state for a value that is not live, the state unchanged for a
    // message that cannot be stepped, and an error box in place of a figure that cannot be drawn.

    /// The live figure and the widget state a host is holding, through a `Recorder` too.
    let private liveAt (value: obj) (state: obj) : (Live * LiveState) option =
        match value, state with
        | (:? Live as l), (:? LiveState as s) -> Some(l, s)
        | (:? Recorder as r), (:? RecorderState as s) -> Some(r.Live, s.State)
        | _ -> None

    /// The initial state of a live figure, or `null` for any other value. A `Recorder` begins a
    /// new recording here, once per display.
    let liveInitForHost (value: obj) : obj =
        try
            match value with
            | :? Live as l -> box (Live.init l)
            | :? Recorder as r ->
                let state = Live.init r.Live
                box { Session = r.Begin(); State = state }
            | _ -> null
        with _ ->
            null

    /// The next state after a message from the view, or `state` itself when nothing changed, the
    /// message cannot be read, or the step failed. A `Recorder` notes the message, with the
    /// view's time, before it is stepped.
    let liveStepForHost (value: obj, state: obj, msgType: string, payload: string) : obj =
        match value, state with
        | (:? Live as l), (:? LiveState as s) ->
            match LiveMsg.parse msgType payload with
            | Some msg ->
                try
                    box (Live.step l s msg)
                with _ ->
                    state
            | None -> state
        | (:? Recorder as r), (:? RecorderState as rs) ->
            match LiveMsg.parseStamped msgType payload with
            | Some(at, msg) ->
                try
                    r.Note(rs.Session, at, msg)
                    let next = Live.step r.Live rs.State msg
                    // The same object when nothing changed: a host answers `same` by reference.
                    if obj.ReferenceEquals(next, rs.State) then state else box { rs with State = next }
                with _ ->
                    state
            | None -> state
        | _ -> state

    /// The whole page at a state, live or static. `""` for anything that is not a live figure.
    let liveDocumentForHost (value: obj, state: obj, isLive: bool) : string =
        match liveAt value state with
        | Some(l, s) -> widget l s isLive
        | None -> ""

    /// **The step from `before` to `after` as the view should receive it.** A patch when
    /// one describes the change and is at most half the document; the whole figure otherwise.
    ///
    /// Half, rather than merely smaller, because a patch is not free at the other end: each
    /// element is parsed on its own, and the defs are replaced whole. A slider that moves one
    /// annotation over a large scatter leaves the patch a few percent of the document, and a
    /// zoom, which moves every tick and every mark, leaves it nearly all of it. Nothing lives
    /// usefully between the two.
    let widgetPatch (live: Live) (before: LiveState) (after: LiveState) : SvgPatch =
        let extent = after.Shot.Outer.Extent
        let next = Live.scene live after

        let p =
            if before.Shot.Outer.Extent = extent then
                SvgBackend.renderPatch extent (Live.scene live before) next
            else
                { Document = SvgBackend.render extent next
                  Defs = ""
                  Elements = None }

        match p.Elements with
        | Some els ->
            let bytes = p.Defs.Length + (els |> Array.sumBy (fun (id, svg) -> id.Length + svg.Length))
            if 2 * bytes <= p.Document.Length then p else { p with Elements = None }
        | None -> p

    /// What the view swaps in after a step: the figure's SVG first, then each control's label.
    /// Empty for anything that is not a live figure.
    let liveReplyForHost (value: obj, state: obj) : string[] =
        match liveAt value state with
        | Some(l, s) ->
            let shown =
                try
                    Live.shown l s
                with _ ->
                    Array.create l.Controls.Length ""

            Array.append [| widgetFigure l s |] shown
        | _ -> [||]

    /// **A step on a canvas page**: `wpatch` and a patch's bytes when the arrangement held
    /// and the patch is at most half the whole scene's bytes, and the whole scene
    /// otherwise. A view that cannot be drawn is answered `same`: a canvas page has no place for
    /// the error box an SVG page shows, and the labels still move.
    let private canvasAnswer (live: Live) (before: LiveState) (after: LiveState) : string[] =
        try
            let next = Live.scene live after

            let whole () =
                let e = after.Shot.Outer.Extent
                [| "scene"; exact (float e.Width); exact (float e.Height); sceneBase64 next |]

            if before.Shot.Outer.Extent <> after.Shot.Outer.Extent then
                whole ()
            else
                match Scene.diff (Live.scene live before) next with
                | Replace _ as p ->
                    let bytes = Wire.encodePatchWith liveMeasurer p
                    let all = Wire.encodeWith liveMeasurer next

                    if 2 * bytes.Length <= all.Length then
                        [| "wpatch"; System.Convert.ToBase64String bytes |]
                    else
                        let e = after.Shot.Outer.Extent
                        [| "scene"; exact (float e.Width); exact (float e.Height); System.Convert.ToBase64String all |]
                | Whole _ -> whole ()
        with _ ->
            [| "same" |]

    /// **The answer to one message**, laid out for the bridge: a kind, what the kind
    /// carries, and then each control's label.
    ///
    /// - `same; labels…` — the figure did not change, and the view keeps it. A hover is answered
    ///   this way: it moved the pointer, which the figure is not drawn from.
    /// - `svg; document; labels…` — the whole figure.
    /// - `patch; defs; n; id₁; markup₁; … idₙ; markupₙ; labels…` — `SvgPatch`'s elements.
    /// - on a canvas page, `scene; width; height; bytes; labels…` or `wpatch; bytes;
    ///   labels…`, the bytes `Wire`'s, as base64.
    ///
    /// Empty for anything that is not a live figure. A figure that cannot be drawn, or a patch
    /// that cannot be taken, is answered with the whole figure, which `widgetFigure` turns into
    /// an error box if it has to.
    let liveAnswerForHost (value: obj, before: obj, after: obj) : string[] =
        match liveAt value before, liveAt value after with
        | Some(l, b), Some(_, a) ->
            let shown =
                try
                    Live.shown l a
                with _ ->
                    Array.create l.Controls.Length ""

            let body =
                if obj.ReferenceEquals(b, a) || Live.drawsAlike b a then
                    [| "same" |]
                elif onCanvas l b then
                    canvasAnswer l b a
                else
                    match (try Some(widgetPatch l b a) with _ -> None) with
                    | Some { Defs = defs; Elements = Some els } ->
                        Array.append
                            [| "patch"; defs; string els.Length |]
                            (els |> Array.collect (fun (id, svg) -> [| id; svg |]))
                    | Some { Document = doc } -> [| "svg"; doc |]
                    | None -> [| "svg"; widgetFigure l a |]

            Array.append body shown
        | _ -> [||]

    /// **The whole figure at a state, in the page's kind**, for a view's hello (`size`):
    /// `svg; document; labels…` on an SVG page and `scene; width; height; bytes; labels…` on a
    /// canvas page. Empty for anything that is not a live figure.
    let liveWholeForHost (value: obj, state: obj) : string[] =
        match liveAt value state with
        | Some(l, s) ->
            let shown =
                try
                    Live.shown l s
                with _ ->
                    Array.create l.Controls.Length ""

            let body =
                if onCanvas l s then
                    (try canvasWhole l s with _ -> [| "same" |])
                else
                    [| "svg"; widgetFigure l s |]

            Array.append body shown
        | None -> [||]

    /// **What the pointer reads**, laid out for the bridge: the plot rectangle it is over
    /// in figure px (`x0; y0; x1; y1`, round-trip exact), `left` or `right` for the corner the
    /// readout sits in, then one line per axis. Empty when the pointer is off every plot, and
    /// for anything that is not a live figure. The view places the lines and does not read them.
    let liveReadoutForHost (value: obj, state: obj) : string[] =
        match liveAt value state with
        | Some(l, s) ->
            match (try Live.readout l s with _ -> None) with
            | Some r ->
                Array.append
                    [| exact (float r.Plot.X0)
                       exact (float r.Plot.Y0)
                       exact (float r.Plot.X1)
                       exact (float r.Plot.Y1)
                       (if r.Right then "right" else "left") |]
                    r.Lines
            | None -> [||]
        | None -> [||]
