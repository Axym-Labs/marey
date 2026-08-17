/// `Quiver.Interactive` — `docs/m1-plan.md` Phase 5.
///
/// The plan expected this suite to be limited to "the formatter bodies are plain functions
/// returning strings, so they are asserted directly", because `dotnet-interactive` is not
/// installed here. The tool and the library are separable: `Microsoft.DotNet.Interactive.Formatting`
/// is an ordinary NuGet package, so the *registration* half is testable for real, and the
/// wiring — which type, which mime type, which renderer — is where the bugs actually are.
///
/// Three things are checked that a "does it return a string" test cannot:
///
/// * **Every fragment parses as XML.** The same oracle the SVG golden suite uses. It catches an
///   unescaped `&` from a figure title, an unclosed `div`, and a stray HTML named entity.
/// * **`Formatter` really picks our renderer**, for all nine registered types, including the two
///   that had to be reasoned about rather than looked up: a measure-generic class and an
///   interface with one anonymous implementation.
/// * **The auto-registration hook names a function that exists.** `extension.dib` is a script
///   inside a package; nothing but this test connects the name in it to the assembly, and a
///   rename of `register` would break every user's `#r` with no build error anywhere.
module Quiver.Tests.InteractiveTests

open System
open System.IO
open System.Xml.Linq
open Xunit
open Microsoft.DotNet.Interactive.Formatting
open Quiver

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

        Chart.line U.second U.metre ts xs
        |> Chart.withName "displacement"
        |> Chart.addLine U.second U.kelvin ts temps
        |> Chart.withName "temperature"

    /// A title that is markup if anybody forgets to escape it.
    let hostileTitle: Figure =
        Golden.figure |> Chart.withTitle "<script>alert(\"x\")</script> a & b"

    let scene: Node = Chart.toScene Golden.figureSurface Golden.figure

    let report: PaletteReport = Palette.validate Color.white Palette.okabeIto

    let acceleration: Dim = Dim.sub Dim.length (Dim.pow 2 Dim.time)

    let erasedScale: IScaleErased =
        Erase.scale (Scale.linear U.second (0.0<s>, 10.0<s>))

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
          "erased scale", fun () -> Html.scale erasedScale ]

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
        Assert.Contains(Num.at 3 lch.L, html)

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
        Assert.Contains(Num.at 3 (raw + 360.0), html)

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
        Assert.Contains(Num.at 3 Fixtures.report.WorstSeparation.Minimum, html)

    /// `Dim.show` is Core's fallback and `UnitDisplay.renderSi` is what `Quiver.Units` makes of
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
        let celsius = Html.witness NonSi.degreeCelsius

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
// THE REGISTRATION
//
// `Formatter` is process-global mutable state, so **every test in this module must live in this
// module**: xUnit runs the tests of one class sequentially and different classes in parallel, and
// two tests registering different figure sizes at the same time would flake. Each test also
// registers what it needs rather than relying on another test having run, because the order
// within a class is not defined either.
// ======================================================================================

module Registration =

    let private html = HtmlFormatter.MimeType

    /// What the kernel does with a value: ask `Formatter` for its HTML.
    let private display (x: obj) = Formatter.ToDisplayString(x, html)

    [<Fact>]
    let ``every registered type displays through its own renderer`` () =
        Formatters.register ()

        let expectations: (string * obj * string) list =
            [ "Figure", box Golden.figure, Html.figure Golden.figure
              "Node", box Fixtures.scene, Html.node Fixtures.scene
              "Color", box Palette.vermillion, Html.color Palette.vermillion
              "Ramp", box Palette.viridis, Html.ramp Palette.viridis
              "Color[]", box Palette.okabeIto, Html.palette Palette.okabeIto
              "PaletteReport", box Fixtures.report, Html.paletteReport Fixtures.report
              "Dim", box Fixtures.acceleration, Html.dim Fixtures.acceleration
              "U<'u>", box U.metrePerSecondSquared, Html.witness U.metrePerSecondSquared
              "IScaleErased", box Fixtures.erasedScale, Html.scale Fixtures.erasedScale ]

        for name, value, expected in expectations do
            let actual = display value

            if actual <> expected then
                failwithf
                    "%s does not display through Html: `Formatter.ToDisplayString` returned\n\n%s\n\nand Html returned\n\n%s"
                    name
                    actual
                    expected

    /// The claim `Formatters.fs` reasons about rather than looks up: measures are erased in a
    /// *class's* type argument as thoroughly as in `float<'u>`, so `U<m>` and `U<s>` are one
    /// runtime type and one registration covers every unit there will ever be.
    ///
    /// Asserted directly, because "measures are erased" is the kind of half-truth that holds for
    /// `float<'u>` and could plausibly have failed here — in which case the registration would
    /// cover `U<1>` and nothing else, and every real unit would display as a reflection dump.
    [<Fact>]
    let ``one registration covers every unit witness`` () =
        Assert.Equal(typeof<U<1>>, U.second.GetType())
        Assert.Equal(U.metre.GetType(), U.second.GetType())

        Formatters.register ()

        Assert.Equal(Html.witness U.second, display U.second)
        Assert.Equal(Html.witness U.kelvin, display U.kelvin)
        Assert.Equal(Html.witness U.metrePerSecondSquared, display U.metrePerSecondSquared)

    /// `Node` is a nine-case union, so every case is a *subclass* of the type that was registered.
    /// Registration matches by assignability — checked here for all nine rather than assumed from
    /// one, since a union whose cases were represented differently would break silently.
    [<Fact>]
    let ``every node case displays through the Node registration`` () =
        Formatters.register ()

        let byCase =
            Scene.fold
                (fun (acc: Map<string, Node>) n ->
                    let name =
                        match n with
                        | Path _ -> "Path"
                        | Text _ -> "Text"
                        | Markers _ -> "Markers"
                        | Raster _ -> "Raster"
                        | Group _ -> "Group"
                        | Transform _ -> "Transform"
                        | Clip _ -> "Clip"
                        | Keyed _ -> "Keyed"
                        | Annotated _ -> "Annotated"

                    if acc.ContainsKey name then acc else acc.Add(name, n))
                Map.empty
                GoldenTests.Scenes.allNineCases

        Assert.Equal(9, byCase.Count)

        for KeyValue(name, n) in byCase do
            if display n <> Html.node n then
                failwithf "the %s case does not display through the Node formatter" name

    /// The composition a notebook cell actually performs: ask a type for its preferred mime
    /// types, then format with each of them. Testing the composition rather than either half is
    /// what makes this about the display behaviour instead of about our registration call.
    ///
    /// It replaced a test that asserted `GetPreferredMimeTypesFor typeof&lt;Figure&gt;` contains
    /// `text/html`. That one passed with `Formatters`' `SetPreferredMimeTypesFor` deleted, because
    /// the host answers `text/html` for every type there is, registered or not — so it proved
    /// nothing about our code. See the header of `Formatters.fs`.
    ///
    /// This version is also the canary on that upstream default. If a future Formatting release
    /// stops preferring HTML, every Quiver value silently reverts to a reflection dump in every
    /// notebook, and nothing else in the suite would notice.
    [<Fact>]
    let ``a cell returning a figure formats through Html`` () =
        Formatters.register ()

        let mimeTypes = Formatter.GetPreferredMimeTypesFor typeof<Figure> |> Seq.toList

        Assert.Contains(html, mimeTypes)

        let rendered = mimeTypes |> List.map (fun m -> Formatter.ToDisplayString(Golden.figure, m))

        Assert.Contains(Html.figure Golden.figure, rendered)

    [<Fact>]
    let ``registerWith changes the size figures are compiled at`` () =
        try
            let small: Extent<px> = { Width = 260.0<px>; Height = 160.0<px> }
            Formatters.registerWith small

            Assert.Contains("width=\"260\" height=\"160\"", display Golden.figure)
            Assert.Equal(Html.figureAt small Golden.figure, display Golden.figure)
        finally
            // Process-global state: leaving a 260px default behind would make whichever test ran
            // next depend on this one.
            Formatters.register ()

        Assert.Contains("width=\"720\" height=\"450\"", display Golden.figure)

// ======================================================================================
// THE PACKAGED HOOK
// ======================================================================================

module Packaging =

    let private read (relative: string) =
        let file = Path.Combine(Golden.repoRoot.Force(), relative)

        if not (File.Exists file) then
            failwithf "Quiver.Tests: expected '%s' to exist, looked at %s" relative file

        File.ReadAllText file

    /// The one thing connecting the script inside the package to the assembly beside it.
    ///
    /// `extension.dib` is compiled by the kernel, not by us, so renaming `Formatters.register`
    /// would leave the build green and break auto-registration for every user of the package.
    /// This test asserts the name in the file resolves against the real assembly.
    [<Fact>]
    let ``the extension hook calls a function that exists`` () =
        let dib = read "src/Quiver.Interactive/interactive-extensions/dotnet/extension.dib"

        Assert.Contains("#!fsharp", dib)
        Assert.Contains("Quiver.Formatters.register ()", dib)

        // Touch the assembly first: .NET loads on first use, and every other reference to
        // `Quiver.Interactive` in this file is inside a different test, which may not have run.
        Html.defaultSize |> ignore

        let assembly =
            AppDomain.CurrentDomain.GetAssemblies()
            |> Array.tryFind (fun a -> a.GetName().Name = "Quiver.Interactive")

        Assert.True(assembly.IsSome, "Quiver.Interactive is not loaded")

        let t = assembly.Value.GetType "Quiver.Formatters"
        Assert.True(not (isNull t), "Quiver.Interactive holds no type named Quiver.Formatters")

        let register = t.GetMethod "register"

        Assert.True(
            not (isNull register),
            "Quiver.Formatters has no `register` member — extension.dib calls one, so this rename broke every user's `#r`"
        )

        Assert.Empty(register.GetParameters())

    /// The demonstration notebook cannot be *run* here — there is no kernel — so the check is
    /// that it is present and that every cell names a kernel the notebook host knows. A typo'd
    /// directive is the failure mode that makes a notebook refuse to open, and it is invisible
    /// in a diff.
    [<Fact>]
    let ``the demonstration notebook has only valid cell directives`` () =
        let dib = read "docs/notebooks/quiver-tour.dib"

        let directives =
            dib.Split('\n')
            |> Array.map (fun l -> l.TrimEnd('\r').Trim())
            |> Array.filter (fun l -> l.StartsWith "#!")
            |> Array.toList

        Assert.NotEmpty directives

        let known = set [ "#!fsharp"; "#!markdown" ]

        for d in directives do
            Assert.True(known.Contains d, sprintf "'%s' is not a cell directive this notebook should use" d)

        Assert.Contains("#r \"nuget: Quiver.Interactive", dib)
        Assert.Contains("#!markdown", directives)
