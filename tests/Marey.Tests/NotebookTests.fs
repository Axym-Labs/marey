/// `Marey.Html` + `Marey.Verso` — the notebook integration.
///
/// Two halves with a reflective seam between them, so the suite is in three parts:
///
/// * **`Fragments`** — `Html` alone. Pure `something -> string`, no host, no global state. Every
///   fragment parsed as XML with the oracle the SVG golden suite uses, plus escaping, culture
///   invariance, and the two theme rules.
/// * **`VersoFormatter`** — `MareyFormatter` driven as a real `IDataFormatter` through Verso's
///   own `StubFormatterContext`. This is where the priority that settles the tie with
///   `verso.fsharp.formatter` is pinned, and where the sizing that replaced a guess is checked.
/// * **`LoadContexts`** — the part that would otherwise be a hope. The extension is loaded into a
///   genuinely separate collectible `AssemblyLoadContext`, exactly as Verso does it, and asked to
///   render a value built in this one.
///
/// The .NET Interactive registration tests that used to live here went with the host: Polyglot
/// Notebooks was retired 2026-03-27 and .NET Interactive 2026-04-24.
module Marey.Tests.NotebookTests

open System
open System.IO
open System.Text.RegularExpressions
open System.Xml.Linq
open Xunit
open Marey
open Marey.UnitsOfMeasure

// ======================================================================================
// FIXTURES
// ======================================================================================

module private Fixtures =

    let ts = Array.init 24 (fun i -> float i * 0.4<s>)

    /// Two layers whose y axes are in different dimensions. `Figure.unify` refuses this — with a
    /// message worth reading, which is the point of the test that uses it.
    let dimensionallyBroken: Figure =
        let xs = ts |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)
        let temps = ts |> Array.map (fun t -> 293.0<K> + 0.5<K / s> * t)

        Chart.lineIn U.second U.metre ts xs
        |> Chart.withName "displacement"
        |> Chart.addLineIn U.second U.kelvin ts temps
        |> Chart.withName "temperature"

    /// A title that is markup if anybody forgets to escape it.
    let hostileTitle: Figure =
        Golden.figure |> Chart.withTitle "<script>alert(\"x\")</script> a & b"

    let scene: Node = Chart.toScene Golden.figureSurface Golden.figure

    let report: PaletteReport = Palette.validate Color.white Palette.okabeIto

    let acceleration: Dim = Dim.sub Dim.length (Dim.pow 2 Dim.time)

    let erasedScale: IScaleErased =
        Erase.scale (Scale.linear U.second (0.0<s>, 10.0<s>))

    /// A short film with something that actually moves, for D83's flipbook.
    ///
    /// A zoom rather than a fade: the y domain changes, so the tick labels change, so the frames
    /// genuinely differ. A film whose frames are all the same picture would satisfy every
    /// assertion below while proving nothing about any of them.
    let film: Film =
        let xs = ts |> Array.map (fun t -> 0.5 * 9.81<m / s^2> * t * t)

        let fig =
            Chart.lineIn U.second U.metre ts xs |> Chart.withTitle "free fall"

        let story =
            Storyboard.scene {
                let! curve = Storyboard.draw fig |> Storyboard.over 0.5<s>
                do! curve |> Storyboard.zoomToY (0.0, 40.0) |> Storyboard.over 0.5<s>
            }

        Film.ofStoryWith 8 TextMeasure.approx (Chart.size 240.0 160.0) story

    let translucent: Color = Color.withAlpha 0.35 Palette.blue

    /// Names a user chose, containing the three characters that turn a fragment into markup.
    ///
    /// These exist because without them nothing in the suite exercises `Html.text` on hostile
    /// input: a figure's title is escaped by the *SVG backend* on its way through, and every
    /// other string the fragments print — a hex code, `Dim.show`, a vision model's name — is
    /// generated rather than supplied. A ramp name and a unit symbol are the two strings a user
    /// hands the library directly.
    let hostileRamp: Ramp =
        { Palette.viridis with Name = "a <b> & c" }

    let hostileUnit: U<1> = U<1>("m<sup>2</sup> & \"quoted\"", Dim.length, 1.0, 0.0)

    /// One of each fragment, named, as a thunk.
    ///
    /// Thunks rather than strings so the culture test can force the *whole* list on a foreign
    /// thread. A precomputed list would have been built on the ambient culture and the test would
    /// have compared invariant output against itself.
    let all: (string * (unit -> string)) list =
        [ "figure", fun () -> Html.figure Golden.figure
          "figure that cannot compile", fun () -> Html.figure dimensionallyBroken
          "figure with a hostile title", fun () -> Html.figure hostileTitle
          "scene", fun () -> Html.node scene
          "colour", fun () -> Html.color Palette.vermillion
          "colour with alpha", fun () -> Html.color translucent
          "ramp", fun () -> Html.ramp Palette.viridis
          "ramp with a hostile name", fun () -> Html.ramp hostileRamp
          "witness with a hostile symbol", fun () -> Html.witness hostileUnit
          "palette", fun () -> Html.palette Palette.okabeIto
          "palette report", fun () -> Html.paletteReport report
          "dimension", fun () -> Html.dim acceleration
          "unit witness", fun () -> Html.witness U.metrePerSecondSquared
          "erased scale", fun () -> Html.scale erasedScale
          // D83. Small on purpose — the sweep renders this list several times, and a
          // `FlipbookFrames`-sized film in it would dominate the suite's runtime for no extra
          // coverage: what these gates check is the wrapper, and six frames exercise it exactly
          // as well as forty-eight.
          "film", fun () -> Html.filmAt 6 film
          "film of one frame", fun () -> Html.filmAt 1 film ]

    let rendered () = all |> List.map (fun (name, f) -> name, f ())

// ======================================================================================
// THE FRAGMENTS
//
// `Html` alone: no kernel, no global state, nothing registered.
// ======================================================================================

module Fragments =

    [<Fact>]
    let ``a figure displays as inline svg`` () =
        let html = Html.figure Golden.figure

        Assert.StartsWith("<div style=\"max-width:100%;overflow-x:auto\">", html)
        Assert.Contains("<svg xmlns=\"http://www.w3.org/2000/svg\"", html)
        Assert.EndsWith("</div>", html)

        // The size is `Html.defaultSize`, not `Chart.defaultSize`: a notebook cell is narrower
        // than 800px and a figure that needs scrolling on first sight makes a bad first
        // impression. If that default moves, this is the assertion that says so.
        Assert.Contains("width=\"720\" height=\"450\"", html)

    /// §1's headline claim — `t [s]` and `x [m]` from nothing but the unit witnesses — mirrored at
    /// the last layer it passes through. The SVG suite asserts it in a file and the TikZ suite in
    /// `\qty{}{\second}`; this is the same sentence in the place a reader meets it first.
    [<Fact>]
    let ``the axis labels the units wrote survive into a notebook cell`` () =
        let html = Html.figure Golden.figure
        Assert.Contains("t [s]", html)
        Assert.Contains("x [m]", html)

    [<Fact>]
    let ``every fragment is well-formed xml`` () =
        // The SVG suite's oracle, reused. It is the reason `Html` emits no HTML named entities
        // and no unclosed tags: both would render fine in a browser and be invisible otherwise.
        for name, html in Fixtures.rendered () do
            let failure =
                try
                    XDocument.Parse html |> ignore
                    None
                with e ->
                    Some e.Message

            match failure with
            | None -> ()
            | Some why -> failwithf "the '%s' fragment is not well-formed XML: %s\n\n%s" name why html

    [<Fact>]
    let ``a hostile figure title cannot inject markup`` () =
        let html = Html.figure Fixtures.hostileTitle

        Assert.DoesNotContain("<script>", html)
        Assert.Contains("&lt;script&gt;", html)
        // The ampersand is the one that gets forgotten, because a raw `&` renders as an
        // ampersand in every browser and breaks only the parser.
        Assert.Contains("a &amp; b", html)

    /// The same rule one layer in. A figure's title is escaped by the SVG backend on its way
    /// through; a ramp's name and a unit's symbol are printed by `Html` itself, and are the two
    /// strings a user hands the library directly.
    [<Fact>]
    let ``a hostile ramp name or unit symbol cannot inject markup`` () =
        let ramp = Html.ramp Fixtures.hostileRamp

        Assert.DoesNotContain("<b>", ramp)
        Assert.Contains("a &lt;b&gt; &amp; c", ramp)

        let witness = Html.witness Fixtures.hostileUnit

        Assert.DoesNotContain("<sup>", witness)
        Assert.Contains("&lt;sup&gt;", witness)

    // ----------------------------------------------------------------------------------
    // The flipbook
    // ----------------------------------------------------------------------------------

    /// **The gate the id prefix exists for, and it can fail.**
    ///
    /// SVG ids are scoped to the document, not to a nested `<svg>`, so N frames stacked in one
    /// outer document share one id space. Without `SvgBackend.renderScoped`, every frame emits
    /// `qc`, `qm` and the same `Keyed` names, `url(#qc)` in frame 5 resolves to frame 0's clip
    /// path, and `<use xlink:href="#qm">` to frame 0's marker — which is invisible exactly while
    /// the frames agree, and wrong the moment a `highlight` makes one mark bigger.
    ///
    /// Confirmed non-vacuous by rendering the same frames unscoped: 5 ids per frame, 5 distinct
    /// across all six, against 30 distinct with the prefix.
    [<Fact>]
    let ``no two frames of a flipbook share an id`` () =
        let html = Html.filmAt 6 Fixtures.film

        let ids =
            Regex.Matches(html, "id=\"([^\"]+)\"")
            |> Seq.map (fun m -> m.Groups.[1].Value)
            |> Seq.toList

        Assert.NotEmpty ids

        Assert.Equal<string list>(List.distinct ids |> List.sort, List.sort ids)

        // And the same scene rendered the ordinary way *does* repeat them, which is what makes
        // the assertion above a gate rather than a coincidence of this fixture.
        let unscoped =
            [ for i in 0..5 ->
                SvgBackend.render Fixtures.film.Extent (Fixtures.film.Frames.At(float i / 6.0 * Fixtures.film.Duration)) ]
            |> String.concat ""

        let repeated =
            Regex.Matches(unscoped, "id=\"([^\"]+)\"")
            |> Seq.map (fun m -> m.Groups.[1].Value)
            |> Seq.toList

        Assert.True(
            (List.distinct repeated).Length < repeated.Length,
            "the unscoped render no longer repeats ids, so the scoped gate above proves nothing"
        )

    /// **The slices tile the cycle exactly once**: no gap, which would flash the empty surface,
    /// and no overlap, which would draw two frames at once.
    ///
    /// Read out of the emitted `keyTimes` rather than out of the generator, because the
    /// generator is the thing under test. Frame `i` is opaque from `i/n` and hidden again at
    /// `(i+1)/n`; the first and last frames carry two stops because `calcMode="discrete"` holds
    /// its final value to the end of the cycle.
    [<Fact>]
    let ``every instant of the cycle shows exactly one frame`` () =
        let n = 6
        let html = Html.filmAt n Fixtures.film

        let starts =
            Regex.Matches(html, "values=\"([^\"]+)\" keyTimes=\"([^\"]+)\"")
            |> Seq.map (fun m ->
                let values = m.Groups.[1].Value.Split(';')
                let times = m.Groups.[2].Value.Split(';')
                // The stop at which this frame becomes opaque.
                let i = Array.findIndex ((=) "1") values
                float times.[i])
            |> Seq.toList

        Assert.Equal(n, starts.Length)

        // One frame starts at each n-th of the cycle, in order and without repetition. Both
        // sides rounded to the six decimals the attribute carries: `1/6` is written `0.166667`,
        // and comparing that against an unrounded `0.1666…` is a test of `Num.upTo`, not of the
        // tiling.
        let round6 (t: float) = System.Math.Round(t, 6)

        Assert.Equal<float list>(
            [ for i in 0 .. n - 1 -> round6 (float i / float n) ],
            starts |> List.map round6
        )

    /// The frame count is the frame count.
    [<Fact>]
    let ``a flipbook holds the frames it was asked for`` () =
        for n in [ 2; 6; 13 ] do
            let html = Html.filmAt n Fixtures.film
            Assert.Equal(n, Regex.Matches(html, "<animate ").Count)

    /// Two renders of one film are the same string. The repeated gate of this repository, and it
    /// reaches further here than usual: the id prefix is derived from the frame index, so a
    /// generated-name scheme that reached for a GUID would fail exactly this.
    [<Fact>]
    let ``a flipbook is deterministic`` () =
        Assert.Equal(Html.filmAt 6 Fixtures.film, Html.filmAt 6 Fixtures.film)

    /// A film of one frame is a still, and says nothing about time.
    ///
    /// `keyTimes` for a one-frame cycle degenerates to a frame visible for all of it and hidden
    /// for none — an `<animate>` that animates nothing. A still is the honest rendering, and it
    /// is also what a caller asking for one frame meant.
    [<Fact>]
    let ``one frame is a still rather than an animation of one`` () =
        let html = Html.filmAt 1 Fixtures.film

        Assert.DoesNotContain("<animate", html)
        Assert.Contains("<svg xmlns=\"http://www.w3.org/2000/svg\"", html)

        // And a count below one is the same thing rather than an exception or an empty cell.
        Assert.Equal(html, Html.filmAt 0 Fixtures.film)

    /// The film is rendered at its own extent, not at `Html.defaultSize`.
    ///
    /// A film's layout was arranged once against its own surface and every frame's text was
    /// measured for it, so re-sizing here would break the one thing M3 built — and it would do
    /// it silently, because the picture still looks like a picture.
    [<Fact>]
    let ``a flipbook keeps the surface the film was arranged against`` () =
        let html = Html.filmAt 4 Fixtures.film

        Assert.Contains("width=\"240\" height=\"160\"", html)
        Assert.DoesNotContain("width=\"720\"", html)

    /// Rules 1 and 2 from `Html.fs`'s header, as assertions rather than intentions.
    ///
    /// A `<style>` element in a notebook output cell is not scoped to the cell; it restyles the
    /// reader's whole document and stacks up once per execution. And a pinned `color` or
    /// `background-color` is a coin flip against a theme nobody can query — the substring
    /// `color:` catches both spellings at once.
    [<Fact>]
    let ``no fragment carries a style element or pins a colour`` () =
        for name, html in Fixtures.rendered () do
            Assert.False(html.Contains "<style", sprintf "the '%s' fragment contains a <style> element" name)

            Assert.False(
                html.Contains "color:",
                sprintf "the '%s' fragment pins a CSS colour, which a dark notebook theme will bury" name
            )

    [<Fact>]
    let ``fragments are deterministic`` () =
        for name, f in Fixtures.all do
            Assert.Equal(f (), f ())
            Assert.Equal<string>(name, name)

    [<Fact>]
    let ``fragments do not depend on the ambient culture`` () =
        // SvgWriter.fs: "a German locale emits `0,5`, and `0,5` is not a number SVG can parse".
        // It is not a length CSS can parse either, and a gradient stop at `50,000%` is silently
        // ignored rather than reported.
        let invariant = Fixtures.rendered ()
        let german = Generators.Culture.run Generators.Culture.CommaDecimal Fixtures.rendered
        let turkish = Generators.Culture.run Generators.Culture.DotlessI Fixtures.rendered

        for (name, a), (_, b) in List.zip invariant german do
            Assert.Equal(sprintf "%s: %s" name a, sprintf "%s: %s" name b)

        for (name, a), (_, b) in List.zip invariant turkish do
            Assert.Equal(sprintf "%s: %s" name a, sprintf "%s: %s" name b)

    /// Rule 3. `Figure.unify`'s message is one of the most useful things the library says, and a
    /// formatter that throws replaces it with a kernel-level formatting failure on a value the
    /// user already constructed successfully.
    [<Fact>]
    let ``a figure that cannot compile displays its reason instead of throwing`` () =
        // First: the figure really is broken, so the test is not passing vacuously.
        Assert.Throws<Exception>(fun () ->
            Chart.render SvgBackend.render Html.defaultSize Fixtures.dimensionallyBroken |> ignore)
        |> ignore

        let html = Html.figure Fixtures.dimensionallyBroken

        Assert.Contains("Two physical dimensions cannot share an axis", html)
        Assert.Contains("displacement", html)
        Assert.Contains("temperature", html)
        Assert.DoesNotContain("<svg", html)
        XDocument.Parse html |> ignore

    [<Fact>]
    let ``a scene reports its own census`` () =
        let html = Html.node Fixtures.scene

        Assert.Contains(sprintf "%d nodes, %d primitives" (Scene.count Fixtures.scene) (Scene.primitiveCount Fixtures.scene), html)

    [<Fact>]
    let ``a colour swatch states its hex and its oklch`` () =
        let html = Html.color Palette.vermillion
        let lch = Color.toOklch Palette.vermillion

        Assert.Contains(Color.toHex Palette.vermillion, html)
        Assert.Contains("oklch(", html)
        Assert.Contains(Num.upTo 3 lch.L, html)

    /// `Color.toOklch` builds its hue with `atan2`, so blues arrive negative. Displaying a blue as
    /// `-115.95°` is correct and useless — nobody publishes hues that way, and the library's own
    /// vocabulary is 0–360: `Palette.divergingBetween` is called with 250° and 27°.
    [<Fact>]
    let ``a swatch reports its hue in the range the library itself uses`` () =
        let raw = Space.degOfRad (Color.toOklch Palette.blue).H / 1.0<deg>

        // The fixture is only interesting while the raw angle really is negative.
        Assert.True(raw < 0.0, sprintf "expected a negative raw hue for blue, got %f" raw)

        let html = Html.color Palette.blue

        Assert.DoesNotContain("-", html.Substring(html.IndexOf "oklch("))
        Assert.Contains(Num.upTo 3 (raw + 360.0), html)

    /// A swatch that renders a 35%-alpha colour as an opaque pastel is a swatch that lies. The
    /// eight-digit hex carries the alpha and the checker layer makes it visible.
    [<Fact>]
    let ``a translucent colour shows its alpha`` () =
        let html = Html.color Fixtures.translucent

        Assert.Contains(Color.toHex Fixtures.translucent + "59", html)
        Assert.Contains("alpha 0.35", html)
        Assert.Contains("#b0b0b0", html)

    /// The one substantive claim in `Html.ramp`: the bar is *resampled through* `Ramp.at`, not
    /// handed the ramp's stops.
    ///
    /// A CSS gradient interpolates in sRGB. Handing the browser two stops would display exactly
    /// the artefact `Ramp`'s Oklab stop list exists to avoid — for blue-to-yellow, an sRGB
    /// midpoint that is a desaturated grey where Oklab gives a green. So: the naive sRGB midpoint
    /// must be absent, and a colour only Oklab produces must be present.
    [<Fact>]
    let ``a ramp bar is resampled perceptually rather than left to the browser`` () =
        let blue = Palette.blue
        let yellow = Palette.yellow
        let r = Ramp.ofColors "blue-yellow" [| blue; yellow |]
        let html = Html.ramp r

        let srgbMidpoint =
            Color.toHex
                { R = (blue.R + yellow.R) / 2.0
                  G = (blue.G + yellow.G) / 2.0
                  B = (blue.B + yellow.B) / 2.0
                  A = 1.0 }

        // 64 samples, so t = 0.5 is not one of them; index 31 is the nearest below.
        let oklabNearMidpoint = Color.toHex (Ramp.at r (31.0 / 63.0))

        Assert.NotEqual<string>(srgbMidpoint, oklabNearMidpoint)
        Assert.Contains(oklabNearMidpoint, html)
        Assert.DoesNotContain(srgbMidpoint, html)
        Assert.Contains("2 stops", html)

    [<Fact>]
    let ``a palette report names the vision model that failed`` () =
        let html = Html.paletteReport Fixtures.report

        Assert.Contains("8 colours", html)
        Assert.Contains("protanopia", html)
        Assert.Contains("deuteranopia", html)
        Assert.Contains("tritanopia", html)
        Assert.Contains(Num.upTo 3 Fixtures.report.WorstSeparation.Minimum, html)

    /// `Dim.show` is Core's fallback and `UnitDisplay.renderSi` is what `Marey.Units` makes of
    /// the same value. Showing both is how a missing registry becomes visible instead of
    /// mysterious.
    [<Fact>]
    let ``a dimension shows both spellings`` () =
        let html = Html.dim Fixtures.acceleration

        Assert.Contains(Html.text (Dim.show Fixtures.acceleration), html)
        Assert.Contains(Html.text (UnitDisplay.renderSi Unicode Fixtures.acceleration), html)

    [<Fact>]
    let ``a unit witness shows its conversion to SI`` () =
        // The affine case is the one worth checking: a unit with an offset that displays only its
        // factor is telling half a truth, and °C against K is where that half-truth bites.
        let celsius = Html.witness U.degreeCelsius

        Assert.Contains("273.15", celsius)
        Assert.Contains("°C", celsius)

        let metre = Html.witness U.metre
        Assert.Contains("273.15", celsius)
        Assert.DoesNotContain("273.15", metre)
        Assert.Contains(Html.text (Dim.show Dim.length), metre)

    [<Fact>]
    let ``an erased scale shows its kind, domain and ticks`` () =
        let html = Html.scale Fixtures.erasedScale

        Assert.Contains("linear", html)
        Assert.Contains("0 .. 10", html)
        Assert.Contains("ticks", html)

        let log = Erase.scale (Scale.log10 U.metre (1.0<m>, 1000.0<m>))
        Assert.Contains("log base 10", Html.scale log)

// ======================================================================================
// THE VERSO FORMATTER
//
// `MareyFormatter` exercised as Verso exercises it: an `IDataFormatter` probed with `CanFormat`
// and then invoked with `FormatAsync`, through Verso's own `StubFormatterContext`.
//
// In this assembly everything shares one load context, so the reflective bridge resolves the easy
// way. `LoadContexts` below removes that comfort.
// ======================================================================================

module VersoFormatter =

    open Verso.Abstractions
    open Verso.Testing.Stubs
    open Marey.Verso

    let private formatter = MareyFormatter() :> IDataFormatter

    let private context (mime: string) (w: float) (h: float) =
        StubFormatterContext(MimeType = mime, MaxWidth = w, MaxHeight = h) :> IFormatterContext

    let private html () = context "text/html" 800.0 600.0

    let private format (value: obj) =
        formatter.FormatAsync(value, html ()).GetAwaiter().GetResult()

    /// **The number this whole package exists for.**
    ///
    /// `verso.fsharp.formatter` claims every F# record, union and tuple at priority 20, and
    /// `verso.formatter.html` sits at 20 too. `FormatterResolver` orders by
    /// `OrderByDescending(Priority)` — a stable sort — so two formatters at 20 are resolved by
    /// registration order, which is not a contract. A `Figure` would render as a picture or as a
    /// field table depending on enumeration order. 30 is what makes the outcome a fact.
    [<Fact>]
    let ``the formatter outranks the built-ins it competes with`` () =
        Assert.True(
            formatter.Priority > 20,
            sprintf
                "priority %d does not beat verso.fsharp.formatter and verso.formatter.html, both at 20"
                formatter.Priority
        )

        // Not a fallback: Verso lets a kernel ask for specialised formatters only, keeping its own
        // native output when none claims the value. Claiming to be generic would suppress that for
        // every non-Marey value in the notebook.
        Assert.False formatter.IsFallback

    /// The discovery contract, which no compiler checks. Without `[VersoExtension]` the class is
    /// invisible to the host; without a public parameterless constructor it cannot be activated.
    [<Fact>]
    let ``the formatter is discoverable the way the host discovers it`` () =
        let t = typeof<MareyFormatter>

        Assert.NotEmpty(t.GetCustomAttributes(typeof<VersoExtensionAttribute>, false))
        Assert.True(not (isNull (t.GetConstructor Type.EmptyTypes)), "no public parameterless constructor")
        Assert.True(typeof<IDataFormatter>.IsAssignableFrom t)

    [<Fact>]
    let ``it claims every type Html can render and nothing else`` () =
        let ctx = html ()

        let claimed: (string * obj) list =
            [ "Figure", box Golden.figure
              "Node", box Fixtures.scene
              "Color", box Palette.vermillion
              "Ramp", box Palette.viridis
              "Color[]", box Palette.okabeIto
              "PaletteReport", box Fixtures.report
              "Dim", box Fixtures.acceleration
              "U<'u>", box U.metrePerSecondSquared
              "IScaleErased", box Fixtures.erasedScale ]

        for name, value in claimed do
            Assert.True(formatter.CanFormat(value, ctx), sprintf "%s was not claimed" name)

        // Values that are not ours — including one from a Marey assembly that `Html` has no
        // rendering for. Claiming those would take over output the kernel formats better.
        for name, value in [ "int", box 42; "string", box "hello"; "Extent", box Golden.figureSurface ] do
            Assert.False(formatter.CanFormat(value, ctx), sprintf "%s should not have been claimed" name)

    /// `CanFormat` must return false for a mime type the formatter cannot emit — the resolver
    /// treats a formatter that returns a different type from the one it claimed as broken, and
    /// skips it for the rest of the resolution.
    [<Fact>]
    let ``it claims only the mime type it emits`` () =
        Assert.True(formatter.CanFormat(box Golden.figure, context "text/html" 800.0 600.0))
        Assert.False(formatter.CanFormat(box Golden.figure, context "text/plain" 800.0 600.0))
        Assert.False(formatter.CanFormat(box Golden.figure, context "image/png" 800.0 600.0))

        Assert.Equal("text/html", (format (box Golden.figure)).MimeType)

    [<Fact>]
    let ``a formatted figure is the same html the renderer produces`` () =
        let output = format (box Golden.figure)

        Assert.Equal(Html.renderForHost (800.0, 600.0, box Golden.figure), output.Content)
        Assert.Contains("<svg", output.Content)
        Assert.False output.IsError

    /// The half of the old design that was guesswork. .NET Interactive gave no way to ask how wide
    /// a cell was, so `defaultSize` picked 720x450 and hoped; `IFormatterContext` carries
    /// `MaxWidth`/`MaxHeight`, so it is a measurement now.
    [<Fact>]
    let ``a figure is compiled to the size the host offers`` () =
        let at w h =
            formatter
                .FormatAsync(box Golden.figure, context "text/html" w h)
                .GetAwaiter()
                .GetResult()
                .Content

        Assert.Contains("width=\"640\" height=\"400\"", at 640.0 600.0)
        Assert.Contains("width=\"900\" height=\"562.5\"", at 900.0 600.0)

        // Clamped at both ends: below ~320 a figure is a smudge with axis labels, above ~1100 the
        // line weights chosen for a screen figure start to look thin.
        Assert.Contains("width=\"320\"", at 120.0 600.0)
        Assert.Contains("width=\"1100\"", at 4000.0 4000.0)

        // A short cell drives the height instead, and the aspect is kept either way.
        Assert.Contains("width=\"480\" height=\"300\"", at 900.0 300.0)

        // No usable width at all falls back rather than producing a zero-size picture.
        Assert.Contains("width=\"720\" height=\"450\"", at 0.0 0.0)

    /// Rule 3 through the whole stack. `Figure.unify`'s message is the most useful thing the
    /// library says about a dimensional mistake, and a throwing formatter would replace it with a
    /// resolver-level skip — taking the kernel's own fallback output down with it.
    [<Fact>]
    let ``a figure that cannot compile formats instead of throwing`` () =
        let output = format (box Fixtures.dimensionallyBroken)

        Assert.Contains("Two physical dimensions cannot share an axis", output.Content)
        Assert.DoesNotContain("<svg", output.Content)

// ======================================================================================
// ACROSS A LOAD CONTEXT
//
// Every other test here runs with `Marey.Verso` and `Marey.Html` in one load context,
// where the bridge's reflection is a formality. In a notebook they are never in one context: Verso
// loads an extension into its own collectible `AssemblyLoadContext` and shares only
// `Verso.Abstractions`, while Marey arrives through the notebook's `#r`, in the kernel's context.
//
// That is the single assumption this package is built on, and it is the one that would fail
// silently — a `CanFormat` that quietly answers `false` looks exactly like "no formatter claimed
// it", which is a field table and no error anywhere. So it is reproduced here rather than trusted.
// ======================================================================================

module LoadContexts =

    open System.Reflection
    open System.Runtime.Loader

    /// An isolated context mirroring Verso's: the extension's own assembly loads into it, while
    /// everything else — `Verso.Abstractions` above all, so interface identity survives — defers
    /// to the default context.
    type private ExtensionContext(assemblyPath: string) as this =
        inherit AssemblyLoadContext(name = "marey-test-extension", isCollectible = true)

        let resolver = AssemblyDependencyResolver assemblyPath

        override _.Load(name: AssemblyName) =
            if name.Name = "Marey.Verso" then
                match resolver.ResolveAssemblyToPath name with
                | null -> null
                | path -> this.LoadFromAssemblyPath path
            else
                // Everything else — `Verso.Abstractions`, `FSharp.Core`, and every Marey assembly
                // — comes from the default context. Sharing the abstractions is exactly what Verso
                // does, and it is what makes `IDataFormatter` here the same type as there.
                null

    let private extensionPath =
        System.IO.Path.Combine(AppContext.BaseDirectory, "Marey.Verso.dll")

    [<Fact>]
    let ``the extension renders a value built in another load context`` () =
        Assert.True(System.IO.File.Exists extensionPath, sprintf "not found: %s" extensionPath)

        let context = ExtensionContext extensionPath

        try
            let assembly = context.LoadFromAssemblyPath extensionPath

            // Proof the isolation is real: same file, different context, different type identity.
            let isolated = assembly.GetType "Marey.Verso.MareyFormatter"
            Assert.True(not (isNull isolated), "MareyFormatter not found in the isolated context")
            Assert.NotSame(typeof<Marey.Verso.MareyFormatter>, isolated)

            Assert.NotSame(
                AssemblyLoadContext.GetLoadContext typeof<Marey.Verso.MareyFormatter>.Assembly,
                AssemblyLoadContext.GetLoadContext assembly
            )

            // `Verso.Abstractions` is shared, so the interface *is* the same type and this cast
            // succeeds. It is the line that would throw if the sharing were wrong.
            let formatter = Activator.CreateInstance isolated :?> Verso.Abstractions.IDataFormatter

            let ctx =
                Verso.Testing.Stubs.StubFormatterContext(MimeType = "text/html", MaxWidth = 640.0, MaxHeight = 600.0)
                :> Verso.Abstractions.IFormatterContext

            // A figure from *this* context, formatted by a formatter from *that* one.
            Assert.True(formatter.CanFormat(box Golden.figure, ctx), "the isolated formatter did not claim a Figure")

            let output = formatter.FormatAsync(box Golden.figure, ctx).GetAwaiter().GetResult()

            Assert.Equal("text/html", output.MimeType)
            Assert.Contains("<svg", output.Content)
            Assert.Equal(Html.renderForHost (640.0, 600.0, box Golden.figure), output.Content)
        finally
            context.Unload()

    /// The bridge's contract with `Marey.Html`, resolved by the exact reflection the extension
    /// uses. Renaming or re-shaping either function is a build-clean change that breaks every
    /// notebook, so it fails here instead.
    [<Fact>]
    let ``the two bridge functions have the signatures the extension looks for`` () =
        // The extension searches assemblies that are *already loaded*, so this test reproduces
        // that rather than calling `Assembly.Load`. But the CLR loads lazily and nothing below
        // references `Marey.Html` in a way the JIT can see, so for a while this assertion passed
        // only because some earlier test in the run happened to touch the module first. It began
        // failing the moment an unrelated change shifted the run order. Force the load here, with
        // a call that is worth asserting on its own.
        Assert.True(Html.canRenderForHost (box Golden.figure), "Html.canRenderForHost rejected a Figure")

        let htmlType =
            AppDomain.CurrentDomain.GetAssemblies()
            |> Array.tryFind (fun a -> a.GetName().Name = "Marey.Html")
            |> Option.map (fun a -> a.GetType "Marey.Html")

        Assert.True(htmlType.IsSome && not (isNull htmlType.Value), "Marey.Html not found")

        let canRender =
            htmlType.Value.GetMethod(
                "canRenderForHost",
                BindingFlags.Public ||| BindingFlags.Static,
                null,
                [| typeof<obj> |],
                null
            )

        let render =
            htmlType.Value.GetMethod(
                "renderForHost",
                BindingFlags.Public ||| BindingFlags.Static,
                null,
                [| typeof<float>; typeof<float>; typeof<obj> |],
                null
            )

        Assert.True(not (isNull canRender), "Marey.Html.canRenderForHost(obj) is gone")
        Assert.True(not (isNull render), "Marey.Html.renderForHost(float, float, obj) is gone")
        Assert.Equal(typeof<bool>, canRender.ReturnType)
        Assert.Equal(typeof<string>, render.ReturnType)

        // And the sentinel the bridge folds into "not mine".
        Assert.Equal("", render.Invoke(null, [| box 800.0; box 600.0; box 42 |]) :?> string)

    /// The claim `Html.fs` suppresses FS1240 for. If a future F# stopped erasing measures in a
    /// class's type argument, `:? U<1>` would quietly stop matching real unit witnesses and every
    /// one of them would fall back to a field table.
    [<Fact>]
    let ``measures are erased, so one type test covers every unit witness`` () =
        Assert.Equal(typeof<U<1>>, U.second.GetType())
        Assert.Equal(U.metre.GetType(), U.second.GetType())

        Assert.True(Html.canRenderForHost(box U.second))
        Assert.True(Html.canRenderForHost(box U.kelvin))
        Assert.Equal(Html.witness U.kelvin, Html.renderForHost (800.0, 600.0, box U.kelvin))

// ======================================================================================
// A LIVE FIGURE THROUGH THE EXTENSION (D99, D100; gate 6)
//
// Driven against fakes of Verso's channel host and channel, because `Verso.Testing` 1.2 has no
// channels and no cell id. The context below carries `CellId` as a property of its own type, which
// is where `Live.cellIdOf` finds it when the host's `IFormatterContext` does not declare one.
// ======================================================================================

[<Xunit.Collection(RenderModes.Collection)>]
module LiveFormatter =

    open System.Collections.Generic
    open System.Threading
    open System.Threading.Tasks
    open Verso.Abstractions
    open Verso.Testing.Stubs
    open Marey.Verso

    type private FakeChannel(id: string, cell: Guid) =
        let posted = List<string * obj>()
        let received = DelegateEvent<Func<OutputChannelMessage, Task>>()
        member val Alive = true with get, set
        member val Snapshot: Func<CancellationToken, Task<string>> = null with get, set
        member _.Posted = posted
        member _.Send(msgType: string, payload: string) =
            received.Trigger([| box (OutputChannelMessage(id, msgType, payload, [||])) |])

        interface IOutputChannel with
            member _.ChannelId = id
            member _.CellId = cell
            member this.IsAlive = this.Alive

            member _.PostMessageAsync(msgType, payload, _) =
                posted.Add((msgType, payload))
                Task.CompletedTask

            [<CLIEvent>]
            member _.MessageReceived = received.Publish

            member this.SnapshotProvider
                with get () = this.Snapshot
                and set v = this.Snapshot <- v

            member _.DisposeAsync() = ValueTask()

    type private FakeHost() =
        let opened = List<FakeChannel>()
        member _.Opened = opened

        interface IOutputChannelHost with
            member _.OpenAsync(cell, _) =
                let c = FakeChannel(sprintf "channel-%d" opened.Count, cell)
                opened.Add c
                Task.FromResult(c :> IOutputChannel)

            member _.Find(id) =
                opened |> Seq.tryFind (fun c -> (c :> IOutputChannel).ChannelId = id) |> Option.map (fun c -> c :> IOutputChannel) |> Option.toObj

            member _.ForCell(cell) =
                (opened |> Seq.filter (fun c -> (c :> IOutputChannel).CellId = cell) |> Seq.map (fun c -> c :> IOutputChannel) |> Seq.toArray)
                :> IReadOnlyList<IOutputChannel>

    /// A formatter context from a host that can reach the view, naming its cell (or not).
    ///
    /// **Not private**, unlike the fakes above: F# compiles a private type's members as internal,
    /// and `Live.cellIdOf` looks for a public `CellId` the way it finds one on a real host's
    /// context. A private fake hides the property and every live test opens nothing.
    type LiveContext(channels: IOutputChannelHost, cell: Guid option) =
        let stub = StubFormatterContext(MimeType = "text/html", MaxWidth = 800.0, MaxHeight = 600.0) :> IFormatterContext

        member _.CellId: Nullable<Guid> =
            match cell with
            | Some g -> Nullable g
            | None -> Nullable()

        interface IFormatterContext with
            member _.MimeType = stub.MimeType
            member _.MaxWidth = stub.MaxWidth
            member _.MaxHeight = stub.MaxHeight

        interface IVersoContext with
            member _.Variables = stub.Variables
            member _.CancellationToken = stub.CancellationToken
            member _.WriteOutputAsync(o) = stub.WriteOutputAsync o
            member _.Theme = stub.Theme
            member _.LayoutCapabilities = stub.LayoutCapabilities
            member _.ExtensionHost = stub.ExtensionHost
            member _.NotebookMetadata = stub.NotebookMetadata
            member _.Notebook = stub.Notebook
            member _.OutputChannels = channels

    let private formatter = MareyFormatter() :> IDataFormatter
    let private cell = Guid.Parse "5b1ce000-0000-0000-0000-000000000042"

    let private live () =
        interact {
            let! f = Control.slider U.hertz "f" 0.5<Hz> 5.0<Hz> 1.0<Hz>
            return Chart.lineIn U.second U.metre [| 0.0<s>; 1.0<s> |] [| 0.0<m>; float f * 1.0<m> |]
        }

    let private format (value: obj) (context: IFormatterContext) =
        Assert.True(formatter.CanFormat(value, context), "the formatter declined a live figure")
        formatter.FormatAsync(value, context).GetAwaiter().GetResult()

    let private payloadOf (o: obj) = o :?> Dictionary<string, obj>

    /// With a host that reaches the view and names the cell, a live figure opens one channel for
    /// that cell and is written as a live page bound to it.
    [<Fact>]
    let ``a live figure opens a channel for its cell`` () =
        let host = FakeHost()
        let output = format (box (live ())) (LiveContext(host, Some cell))

        Assert.Equal(CellOutput.WidgetMimeType, output.MimeType)
        Assert.Equal(1, host.Opened.Count)
        Assert.Equal(cell, (host.Opened.[0] :> IOutputChannel).CellId)
        Assert.Equal("channel-0", output.LiveChannelId)
        Assert.DoesNotContain("disabled=\"disabled\"", output.Content)
        XDocument.Parse output.Content |> ignore

    /// The round trip: a message steps the state, and the reply carries the figure and the
    /// labels the kernel wrote. A message that changes nothing is still answered, without the
    /// figure, so the view's one request in flight is released.
    [<Fact>]
    let ``a message is stepped and answered`` () =
        let host = FakeHost()
        let l = live ()
        format (box l) (LiveContext(host, Some cell)) |> ignore
        let channel = host.Opened.[0]

        channel.Send("set", "[0,2.5]")
        let kind, reply = channel.Posted.[0]
        Assert.Equal("render", kind)
        let p = payloadOf reply
        Assert.Equal<string[]>([| "2.5 Hz" |], p.["shown"] :?> string[])
        Assert.Equal(Html.widgetFigure l (Live.step l (Live.init l) (Set(0, 2.5))), p.["svg"] :?> string)

        channel.Send("set", "[0,2.5]")
        let p2 = payloadOf (snd channel.Posted.[1])
        Assert.False(p2.ContainsKey "svg", "an unchanged state resent its figure")
        Assert.Equal<string[]>([| "2.5 Hz" |], p2.["shown"] :?> string[])

        channel.Send("nonsense", "{")
        Assert.Equal(3, channel.Posted.Count)

    /// A step that changes one layer of a large figure is posted as a patch: the fresh
    /// defs and the elements by id, and no whole figure. The view's hello, `size`, is answered
    /// with the whole figure even when nothing changed, because it is how a view that cannot
    /// apply a patch asks to start again.
    [<Fact>]
    let ``a local change is posted as a patch, and a hello as the figure`` () =
        RenderModes.withRenderMode RenderMode.Svg (fun () ->
            let host = FakeHost()
            let l = Marey.Tests.DiffTests.``diffing is measured``.scatterWithCursor ()
            format (box l) (LiveContext(host, Some cell)) |> ignore
            let channel = host.Opened.[0]

            channel.Send("set", "[0,6]")
            let p = payloadOf (snd channel.Posted.[0])
            Assert.False(p.ContainsKey "svg", "a local change resent the whole figure")
            Assert.Equal<string[]>([| "figure_panel-0-0_layer-1" |], (p.["patch"] :?> string[]) |> Array.indexed |> Array.filter (fun (i, _) -> i % 2 = 0) |> Array.map snd)
            Assert.Contains("<defs>", p.["defs"] :?> string)
            Assert.Equal<string[]>([| "6 s" |], p.["shown"] :?> string[])

            let s1 = Live.step l (Live.init l) (Set(0, 6.0))
            channel.Send("size", sprintf "[%g,%g]" (float s1.Size.Width) (float s1.Size.Height))
            let hello = payloadOf (snd channel.Posted.[1])
            Assert.Equal(Html.widgetFigure l s1, hello.["svg"] :?> string)
            Assert.False(hello.ContainsKey "patch"))

    /// Step F — a figure above the threshold is a page in a frame, with no channel, because a
    /// host inserts an `Html` output as markup and runs none of its scripts; one below it is markup.
    [<Fact>]
    let ``a large figure is formatted as a page, a small one as markup`` () =
        RenderModes.withRenderMode RenderMode.Auto (fun () ->
            let host = FakeHost()
            let rng = Random(5)
            let big = Chart.scatterIn U.metre U.metre (Array.init 8000 (fun _ -> rng.NextDouble() * 1.0<m>)) (Array.init 8000 (fun _ -> rng.NextDouble() * 1.0<m>))
            let output = format (box big) (LiveContext(host, Some cell))
            Assert.Equal(CellOutput.WidgetMimeType, output.MimeType)
            Assert.Contains("<canvas id=\"cv\"", output.Content)
            Assert.Empty(host.Opened)

            let small = Chart.scatterIn U.metre U.metre [| 0.0<m>; 1.0<m> |] [| 0.0<m>; 1.0<m> |]
            let output = format (box small) (LiveContext(host, Some cell))
            Assert.Equal("text/html", output.MimeType))

    /// Step F — the same figure on a canvas page: a local change is posted as a patch's
    /// bytes, and a hello as the whole scene with its size.
    [<Fact>]
    let ``on a canvas page, a change is posted as patch bytes and a hello as the scene`` () =
        RenderModes.withRenderMode RenderMode.Auto (fun () ->
            let host = FakeHost()
            let l = Marey.Tests.DiffTests.``diffing is measured``.scatterWithCursor ()
            let output = format (box l) (LiveContext(host, Some cell))
            Assert.Contains("<canvas id=\"cv\"", output.Content)
            let channel = host.Opened.[0]

            channel.Send("set", "[0,6]")
            let p = payloadOf (snd channel.Posted.[0])
            Assert.False(p.ContainsKey "svg" || p.ContainsKey "scene" || p.ContainsKey "patch", "a local change resent the figure, or as SVG")
            let s0 = Live.init l
            let s1 = Live.step l s0 (Set(0, 6.0))
            let patch = Wire.decodePatch (Convert.FromBase64String(p.["wpatch"] :?> string))
            Assert.True((match patch with Ok q -> Scene.apply q (Live.scene l s0) = Live.scene l s1 | Error _ -> false))
            Assert.Equal<string[]>([| "6 s" |], p.["shown"] :?> string[])

            channel.Send("size", sprintf "[%g,%g]" (float s1.Size.Width) (float s1.Size.Height))
            let hello = payloadOf (snd channel.Posted.[1])
            Assert.False(hello.ContainsKey "svg")
            Assert.Equal(sprintf "%g" (float s1.Shot.Outer.Extent.Width), hello.["w"] :?> string)
            let scene = Wire.decode (Convert.FromBase64String(hello.["scene"] :?> string))
            Assert.True((match scene with Ok n -> n = Live.scene l s1 | Error _ -> false)))

    /// A hover is answered with the figure the view already has and what the pointer
    /// reads; leaving is answered with an empty readout, which takes it down.
    [<Fact>]
    let ``a hover is answered with a readout and no figure`` () =
        let host = FakeHost()
        let l = live ()
        format (box l) (LiveContext(host, Some cell)) |> ignore
        let channel = host.Opened.[0]
        let v = (Live.panels l (Live.init l)).[0]
        let x, y = (v.Plot.X0 + v.Plot.X1) / 2.0, (v.Plot.Y0 + v.Plot.Y1) / 2.0

        channel.Send("hover", sprintf "[%g,%g,1000]" (float x) (float y))
        let p = payloadOf (snd channel.Posted.[0])
        Assert.False(p.ContainsKey "svg", "a hover resent the figure")
        Assert.False(p.ContainsKey "patch", "a hover sent a patch")
        let readout = p.["readout"] :?> string[]
        Assert.Equal(7, readout.Length)
        Assert.StartsWith("t = ", readout.[5])

        channel.Send("leave", "[1100]")
        Assert.Empty((payloadOf (snd channel.Posted.[1])).["readout"] :?> string[])

    /// A save writes the page at the state the reader left it in, static.
    [<Fact>]
    let ``a snapshot is the current state, static`` () =
        let host = FakeHost()
        format (box (live ())) (LiveContext(host, Some cell)) |> ignore
        let channel = host.Opened.[0]
        channel.Send("set", "[0,3.7]")

        let doc = channel.Snapshot.Invoke(CancellationToken.None).GetAwaiter().GetResult()
        Assert.Contains("value=\"3.7\"", doc)
        Assert.Contains("disabled=\"disabled\"", doc)

    /// A `Recorder` goes through the extension as its `Live` does, and hands back
    /// exactly the messages the host stepped, at the view's times. After `stop` the widget still
    /// answers and nothing more is noted.
    [<Fact>]
    let ``a recorder keeps what the host stepped`` () =
        let host = FakeHost()
        let l = live ()
        let r = Live.recorder l
        let output = format (box r) (LiveContext(host, Some cell))
        Assert.Equal("channel-0", output.LiveChannelId)
        let channel = host.Opened.[0]

        let wire =
            [ "size", "[640,400,1000]"
              "set", "[0,2.5,1100]"
              "set", "[0,2.5,1150]"
              "nonsense", "{"
              "set", "[0,4,1300.5]" ]

        for t, p in wire do
            channel.Send(t, p)

        Assert.Equal(wire.Length, channel.Posted.Count)
        Assert.Equal<string[]>([| "2.5 Hz" |], (payloadOf (snd channel.Posted.[1])).["shown"] :?> string[])

        let expected =
            wire
            |> List.choose (fun (t, p) -> LiveMsg.parseStamped t p)
            |> List.map (fun (at, m) -> at.Value, m)
            |> Array.ofList
            |> Recording.ofEvents

        let log = Recorder.stop r
        Assert.Equal<(float<s> * LiveMsg)[]>(expected.Events, log.Events)
        Assert.Equal(0.3005<s>, fst log.Events.[log.Events.Length - 1])
        Assert.Equal<float[]>([| 4.0 |], (snd (Live.record l log).Events.[log.Events.Length - 1]).Values)

        channel.Send("set", "[0,1,1400]")
        Assert.Equal(wire.Length + 1, channel.Posted.Count)
        Assert.Equal(log.Events.Length, (Recorder.log r).Events.Length)

    /// A dead channel is not posted to, and its handler does not throw.
    [<Fact>]
    let ``a closed channel is left alone`` () =
        let host = FakeHost()
        format (box (live ())) (LiveContext(host, Some cell)) |> ignore
        let channel = host.Opened.[0]
        channel.Alive <- false
        channel.Send("set", "[0,2.0]")
        Assert.Empty(channel.Posted)

    /// **Gate 6.** A host with no channels, or one that does not name the cell, gets the static
    /// page and nothing is opened.
    [<Fact>]
    let ``the live path degrades to a static page and opens nothing`` () =
        let host = FakeHost()

        for context in [ StubFormatterContext(MimeType = "text/html") :> IFormatterContext; LiveContext(host, None) ] do
            let output = format (box (live ())) context
            Assert.Equal(CellOutput.WidgetMimeType, output.MimeType)
            Assert.Null(output.LiveChannelId)
            Assert.Contains("disabled=\"disabled\"", output.Content)

        Assert.Empty(host.Opened)

    /// The six live signatures the extension reflects on, pinned as the first two are.
    [<Fact>]
    let ``the live bridge functions have the signatures the extension looks for`` () =
        Assert.NotNull(Html.liveInitForHost (box (live ())))
        let htmlType =
            (AppDomain.CurrentDomain.GetAssemblies() |> Array.find (fun a -> a.GetName().Name = "Marey.Html")).GetType "Marey.Html"

        let find name (args: Type[]) =
            htmlType.GetMethod(name, Reflection.BindingFlags.Public ||| Reflection.BindingFlags.Static, null, args, null)

        let pins =
            [ "liveInitForHost", [| typeof<obj> |], typeof<obj>
              "liveStepForHost", [| typeof<obj>; typeof<obj>; typeof<string>; typeof<string> |], typeof<obj>
              "liveDocumentForHost", [| typeof<obj>; typeof<obj>; typeof<bool> |], typeof<string>
              "liveReplyForHost", [| typeof<obj>; typeof<obj> |], typeof<string[]>
              "liveAnswerForHost", [| typeof<obj>; typeof<obj>; typeof<obj> |], typeof<string[]>
              "liveReadoutForHost", [| typeof<obj>; typeof<obj> |], typeof<string[]> ]

        for name, args, ret in pins do
            let m = find name args
            Assert.True(not (isNull m), sprintf "Marey.Html.%s is gone" name)
            Assert.Equal(ret, m.ReturnType)

        Assert.Null(Html.liveInitForHost (box 42))
