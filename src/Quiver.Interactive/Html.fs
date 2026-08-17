namespace Quiver

open System.Text

// ======================================================================================
// HTML FRAGMENTS FOR A NOTEBOOK CELL.
//
// §6: "a ~200-line package registering a .NET Interactive formatter so `figure` displays inline
// as SVG. Do it at M1; it is how people will first meet the library."
//
// This file is the *what to show* half and holds no reference to .NET Interactive at all. Every
// function here is `something -> string`, total, deterministic, and callable from an ordinary
// program. `Formatters.fs` is the *how to hook it up* half, and it is the only file that knows
// a kernel exists. The split is not tidiness: it is what makes the interesting half testable
// without installing a notebook host.
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
// **4. Every number goes through `Num.at`.**
//   Same rule as both backends, same reason: a German locale writes `0,5`, and `0,5` is not a
//   length CSS can parse. `Num.at` is invariant and never emits an exponent.
//
// One more thing this file deliberately does not do: rewrite the SVG it embeds. It would be easy
// to strip the `width`/`height` attributes and let the `viewBox` scale the figure to the cell,
// and it would silently break every figure whose text was measured at the size that was asked
// for. A figure that is wider than the cell scrolls; it does not shrink.
// ======================================================================================

/// HTML renderings of Quiver's values, for a notebook cell or anywhere else HTML is wanted.
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
    let text (s: string) : string = Svg.escapeText s

    /// Escape a string for use inside a double-quoted attribute value.
    let attr (s: string) : string = Svg.escapeAttr s

    // ---- Numbers and colours ---------------------------------------------------------

    let private num (v: float) = Num.at 3 v

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
            .Append("\">Quiver could not render this ")
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

    // ---- Colour ----------------------------------------------------------------------

    /// A colour: the chip, its hex, and its Oklch coordinates.
    ///
    /// Oklch is shown rather than HSL because it is the space the library actually works in
    /// (§7.1) — `Color.mix`, every ramp, and the whole palette validator are Oklab. A hue in
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
    /// Not in the plan's list, and included anyway: §9 asks for "a validator that checks
    /// contrast and CVD-safety, because a physics library that renders fields in jet is
    /// embarrassing", and a validator whose output is a record you have to squint at is only
    /// half of that. Four rows, one per vision model, is the whole story.
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
    /// is `Quiver.Units` answering a question `Quiver.Core` cannot, and seeing both side by
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

    /// A unit witness: the value that carries a measure's meaning at runtime (§2.2).
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
