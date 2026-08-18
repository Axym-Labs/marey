/// `Quiver.Interactive` + `Quiver.Verso` — the notebook integration.
///
/// Two halves with a reflective seam between them, so the suite is in three parts:
///
/// * **`Fragments`** — `Html` alone. Pure `something -> string`, no host, no global state. Every
///   fragment parsed as XML with the oracle the SVG golden suite uses, plus escaping, culture
///   invariance, and the two theme rules.
/// * **`VersoFormatter`** — `QuiverFormatter` driven as a real `IDataFormatter` through Verso's
///   own `StubFormatterContext`. This is where the priority that settles the tie with
///   `verso.fsharp.formatter` is pinned, and where the sizing that replaced a guess is checked.
/// * **`LoadContexts`** — the part that would otherwise be a hope. The extension is loaded into a
///   genuinely separate collectible `AssemblyLoadContext`, exactly as Verso does it, and asked to
///   render a value built in this one.
///
/// The .NET Interactive registration tests that used to live here went with the host: Polyglot
/// Notebooks was retired 2026-03-27 and .NET Interactive 2026-04-24.
module Quiver.Tests.NotebookTests

open System
open System.IO
open System.Xml.Linq
open Xunit
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
// THE VERSO FORMATTER
//
// `QuiverFormatter` exercised as Verso exercises it: an `IDataFormatter` probed with `CanFormat`
// and then invoked with `FormatAsync`, through Verso's own `StubFormatterContext`.
//
// In this assembly everything shares one load context, so the reflective bridge resolves the easy
// way. `LoadContexts` below removes that comfort.
// ======================================================================================

module VersoFormatter =

    open Verso.Abstractions
    open Verso.Testing.Stubs
    open Quiver.Verso

    let private formatter = QuiverFormatter() :> IDataFormatter

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
        // every non-Quiver value in the notebook.
        Assert.False formatter.IsFallback

    /// The discovery contract, which no compiler checks. Without `[VersoExtension]` the class is
    /// invisible to the host; without a public parameterless constructor it cannot be activated.
    [<Fact>]
    let ``the formatter is discoverable the way the host discovers it`` () =
        let t = typeof<QuiverFormatter>

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

        // Values that are not ours — including one from a Quiver assembly that `Html` has no
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
// Every other test here runs with `Quiver.Verso` and `Quiver.Interactive` in one load context,
// where the bridge's reflection is a formality. In a notebook they are never in one context: Verso
// loads an extension into its own collectible `AssemblyLoadContext` and shares only
// `Verso.Abstractions`, while Quiver arrives through the notebook's `#r`, in the kernel's context.
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
        inherit AssemblyLoadContext(name = "quiver-test-extension", isCollectible = true)

        let resolver = AssemblyDependencyResolver assemblyPath

        override _.Load(name: AssemblyName) =
            if name.Name = "Quiver.Verso" then
                match resolver.ResolveAssemblyToPath name with
                | null -> null
                | path -> this.LoadFromAssemblyPath path
            else
                // Everything else — `Verso.Abstractions`, `FSharp.Core`, and every Quiver assembly
                // — comes from the default context. Sharing the abstractions is exactly what Verso
                // does, and it is what makes `IDataFormatter` here the same type as there.
                null

    let private extensionPath =
        System.IO.Path.Combine(AppContext.BaseDirectory, "Quiver.Verso.dll")

    [<Fact>]
    let ``the extension renders a value built in another load context`` () =
        Assert.True(System.IO.File.Exists extensionPath, sprintf "not found: %s" extensionPath)

        let context = ExtensionContext extensionPath

        try
            let assembly = context.LoadFromAssemblyPath extensionPath

            // Proof the isolation is real: same file, different context, different type identity.
            let isolated = assembly.GetType "Quiver.Verso.QuiverFormatter"
            Assert.True(not (isNull isolated), "QuiverFormatter not found in the isolated context")
            Assert.NotSame(typeof<Quiver.Verso.QuiverFormatter>, isolated)

            Assert.NotSame(
                AssemblyLoadContext.GetLoadContext typeof<Quiver.Verso.QuiverFormatter>.Assembly,
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

    /// The bridge's contract with `Quiver.Html`, resolved by the exact reflection the extension
    /// uses. Renaming or re-shaping either function is a build-clean change that breaks every
    /// notebook, so it fails here instead.
    [<Fact>]
    let ``the two bridge functions have the signatures the extension looks for`` () =
        let htmlType =
            AppDomain.CurrentDomain.GetAssemblies()
            |> Array.tryFind (fun a -> a.GetName().Name = "Quiver.Interactive")
            |> Option.map (fun a -> a.GetType "Quiver.Html")

        Assert.True(htmlType.IsSome && not (isNull htmlType.Value), "Quiver.Html not found")

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

        Assert.True(not (isNull canRender), "Quiver.Html.canRenderForHost(obj) is gone")
        Assert.True(not (isNull render), "Quiver.Html.renderForHost(float, float, obj) is gone")
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
