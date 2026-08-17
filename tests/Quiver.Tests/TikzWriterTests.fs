/// The TikZ writer primitives (D28, D32, D34).
///
/// Two things are being defended here. The first is the same determinism contract the SVG writer
/// has, because the TikZ backend is a golden-tested text emitter on identical terms. The second
/// is specific to TeX and has no SVG analogue: **a wrong string here does not render badly, it
/// fails to compile**, in the user's document, with an error naming a control sequence they never
/// wrote. So the escaping table is checked against a real engine and not only against
/// expectations.
module Quiver.Tests.TikzWriterTests

open Xunit
open Quiver
open Quiver.Tests.Generators

// ======================================================================================
// D28 — `bp`, never `pt`
// ======================================================================================

module ``D28 lengths are emitted in big points`` =

    /// Core's `pt` is 1/72 inch; TeX's `pt` is 1/72.27 inch. Emitting one as the other is a
    /// silent 0.37% error in every coordinate — self-consistently wrong, so invisible in a golden
    /// diff. The suffix is the whole defence.
    [<Fact>]
    let ``dim appends bp`` () =
        Assert.Equal("12.5bp", Tikz.dim 12.5)
        Assert.Equal("0bp", Tikz.dim 0.0)
        Assert.Equal("-3.25bp", Tikz.dim -3.25)

    /// If this ever reads "pt", the 0.37% error is back.
    [<Fact>]
    let ``no length is ever emitted with a pt suffix`` () =
        for v in [ 0.0; 1.0; -1.0; 1234.5; 1e-9 ] do
            Assert.DoesNotContain("pt", Tikz.dim v)

    /// TeX dimensions are 32-bit fixed point in scaled points, capping at 16383.99998 TeX pt =
    /// 16322 bp. Beyond that the engine raises `Dimension too large` and refuses the document.
    ///
    /// This is reachable in normal use, which is the point: D9 has `Scale.Forward` deliberately
    /// not clamping, so an out-of-domain datum projects far outside the panel and is removed by
    /// the clip. SVG writes the coordinate anyway; TeX will not. Found by the compile gate below,
    /// not by inspection.
    [<Fact>]
    let ``dimensions are clamped to what TeX can represent`` () =
        Assert.Equal(Tikz.dim Tikz.MaxDim, Tikz.dim 1e12)
        Assert.Equal(Tikz.dim -Tikz.MaxDim, Tikz.dim -1e12)
        Assert.Equal("16000bp", Tikz.dim 1e12)
        Assert.Equal("-16000bp", Tikz.dim -1e12)

    [<Fact>]
    let ``the clamp is far outside any real figure and leaves ordinary values alone`` () =
        Assert.True(Tikz.MaxDim > 5000.0)

        for v in [ 0.0; 1.0; 595.0; 842.0; 2000.0; -600.0 ] do
            Assert.Equal(Tikz.num v + "bp", Tikz.dim v)

    [<Fact>]
    let ``coord pairs both components`` () =
        Assert.Equal("(1bp,2bp)", Tikz.coord 1.0 2.0)

    /// The px → pt conversion the backend's root transform depends on. At the 96 px/inch screen
    /// convention a pixel is exactly 0.75 bp, which is what makes the cross-backend agreement
    /// test in the TikZ backend suite an exact statement rather than an approximate one.
    [<Fact>]
    let ``a pixel is three quarters of a big point at screen resolution`` () =
        let bp = Space.ptOfPx Space.screenDpi 100.0<px>
        Assert.Equal(75.0, float bp, 9)

    [<Fact>]
    let ``ptOfPx inverts pxOfPt`` () =
        for v in [ 1.0; 9.6; 72.0; 1000.0 ] do
            let round = Space.ptOfPx Space.screenDpi (Space.pxOfPt Space.screenDpi (v * 1.0<pt>))
            Assert.Equal(v, float round, 9)

// ======================================================================================
// D34 — determinism. TeX raises the stakes: a bad number is a hard error, not a wrong pixel.
// ======================================================================================

module ``D34 number emission is deterministic`` =

    /// `\path (1e-05bp, 0bp)` is not a coordinate TeX can read. It is an error, which means the
    /// no-exponent rule is load-bearing here in a way it is not quite for SVG.
    [<Fact>]
    let ``tiny and huge numbers never use scientific notation`` () =
        for v in [ 1e-9; 1e-7; 5e-8; 1e12; 1.5e15; -1e-9 ] do
            let s = Tikz.num v
            Assert.DoesNotContain("e", s)
            Assert.DoesNotContain("E", s)

    /// `(0,5bp, 0bp)` is a TikZ coordinate with three components. A German locale is a
    /// correctness problem, not a cosmetic one.
    [<Fact>]
    let ``numbers are culture-invariant`` () =
        let baseline = Tikz.dim 0.5 + Tikz.coord 1.5 -2.25 + Tikz.ratio 0.625

        let render () = Tikz.dim 0.5 + Tikz.coord 1.5 -2.25 + Tikz.ratio 0.625

        Assert.Equal(baseline, Culture.run Culture.CommaDecimal render)
        Assert.Equal(baseline, Culture.run Culture.DotlessI render)

    [<Fact>]
    let ``negative zero is normalised`` () =
        Assert.Equal("0bp", Tikz.dim -0.0)
        Assert.Equal("0bp", Tikz.dim -0.0001)

    /// A singular transform must not be able to write NaN into a document.
    [<Fact>]
    let ``non-finite values become zero`` () =
        Assert.Equal("0bp", Tikz.dim nan)
        Assert.Equal("0bp", Tikz.dim infinity)
        Assert.Equal("0bp", Tikz.dim -infinity)

// ======================================================================================
// D32 — escaping literal text, and passing TeX source through untouched
// ======================================================================================

module ``D32 text escaping`` =

    [<Fact>]
    let ``reserved characters are escaped`` () =
        Assert.Equal("50\\% yield\\_2", Tikz.escapeText "50% yield_2")
        Assert.Equal("a \\& b \\# c \\$ d", Tikz.escapeText "a & b # c $ d")
        Assert.Equal("\\{braced\\}", Tikz.escapeText "{braced}")

    /// Shared with `Quiver.Units` through `Tex.tryEscapeChar` rather than copied, so the axis-title
    /// composer and the backend cannot disagree about what needs escaping (F2).
    [<Fact>]
    let ``escaping agrees with Core for pure-ASCII input`` () =
        for s in [ "plain"; "50%"; "a_b"; "a&b"; "#1"; "$x$"; "{a}"; "~"; "^"; "\\" ] do
            Assert.Equal(Tex.escape s, Tikz.escapeText s)

    /// A whole exponent becomes one script, not one per digit: `s⁻²` must set as
    /// `s\textsuperscript{-2}`, because `s\textsuperscript{-}\textsuperscript{2}` sets the minus
    /// and the 2 as separate scripts and looks wrong.
    [<Fact>]
    let ``superscript runs collapse into a single textsuperscript`` () =
        Assert.Equal("s\\textsuperscript{-2}", Tikz.escapeText "s⁻²")
        Assert.Equal("m\\textsuperscript{2}", Tikz.escapeText "m²")
        Assert.Equal("x\\textsuperscript{-12}", Tikz.escapeText "x⁻¹²")

    [<Fact>]
    let ``the unit separator and the common unit glyphs become macros`` () =
        Assert.Equal("kg\\textperiodcentered{}m", Tikz.escapeText "kg·m")
        Assert.Equal("\\textohm{}", Tikz.escapeText "Ω")
        Assert.Equal("\\textmu{}m", Tikz.escapeText "µm")
        Assert.Equal("\\textdegree{}C", Tikz.escapeText "°C")
        Assert.Equal("\\AA{}", Tikz.escapeText "Å")

    /// Unicode has two codepoints for the ohm and for the angstrom, and two plausible ones for
    /// micro. `Quiver.Units` uses one of each, but a caller pasting from a units table may supply
    /// the other, and setting it as a missing glyph would be a baffling bug.
    [<Fact>]
    let ``both codepoints for a glyph map to the same macro`` () =
        Assert.Equal(Tikz.escapeText "\u03A9", Tikz.escapeText "\u2126") // omega / ohm sign
        Assert.Equal(Tikz.escapeText "\u00C5", Tikz.escapeText "\u212B") // A-ring / angstrom sign
        Assert.Equal(Tikz.escapeText "\u00B5", Tikz.escapeText "\u03BC") // micro sign / greek mu

    /// The realistic case: a whole axis title as `UnitDisplay` renders it in the Unicode style.
    [<Fact>]
    let ``a rendered unit string escapes to compilable TeX`` () =
        Assert.Equal(
            "E [kg\\textperiodcentered{}m\\textsuperscript{2}\\textperiodcentered{}s\\textsuperscript{-2}]",
            Tikz.escapeText "E [kg·m²·s⁻²]"
        )

    [<Fact>]
    let ``escaping is total on the empty and null cases`` () =
        Assert.Equal("", Tikz.escapeText "")
        Assert.Equal("", Tikz.escapeText null)

module ``D32 runContent chooses between the two forms`` =

    [<Fact>]
    let ``TeX source is passed through verbatim`` () =
        Assert.Equal("\\qty{}{\\metre}", Tikz.runContent (Some "\\qty{}{\\metre}") "m")

    /// The reason D32 exists: escaping the plain form of a siunitx title would produce
    /// `\textbackslash{}qty\{\}\{...\}` — visible nonsense in the output.
    [<Fact>]
    let ``the plain form is escaped when there is no TeX form`` () =
        Assert.Equal("m\\textperiodcentered{}s\\textsuperscript{-1}", Tikz.runContent None "m·s⁻¹")

    /// Matching `Scene.prune`'s rule that a run is empty only when *both* forms are.
    [<Fact>]
    let ``an empty TeX form falls back rather than typesetting nothing`` () =
        Assert.Equal("m", Tikz.runContent (Some "") "m")

// ======================================================================================
// Comments — an injection path, not a formatting detail
// ======================================================================================

module ``comments cannot escape their line`` =

    /// A TeX comment runs to end of line. `Keyed` names and `Annotated` labels are
    /// caller-supplied and reach comments, so a newline in one would end the comment and hand the
    /// remainder to the typesetter as code.
    [<Fact>]
    let ``newlines are stripped from comment bodies`` () =
        let c = Tikz.comment "  " "series\n\\directlua{os.execute('boom')}"

        Assert.Equal(1, c.Split '\n' |> Array.filter (fun l -> l <> "") |> Array.length)
        Assert.StartsWith("  % ", c)
        Assert.EndsWith("\n", c)

    [<Fact>]
    let ``carriage returns and control characters are stripped too`` () =
        let c = Tikz.comment "" "a\rb\tc"
        Assert.DoesNotContain("\r", c)
        Assert.Equal("% a b c\n", c)

    [<Fact>]
    let ``a null comment body is safe`` () =
        Assert.Equal("% \n", Tikz.comment "" null)

// ======================================================================================
// Colour
// ======================================================================================

module ``colour definitions`` =

    [<Fact>]
    let ``an rgb triple is three invariant decimals`` () =
        Assert.Equal("0,0.5,1", Tikz.rgbTriple(Color.rgb 0.0 0.5 1.0))

    /// xcolor rejects components outside [0,1] outright, and an Oklab conversion can land just
    /// outside gamut. Clamping degrades to the nearest representable colour instead of breaking
    /// the build.
    [<Fact>]
    let ``out-of-gamut components are clamped`` () =
        Assert.Equal("0,1,0", Tikz.rgbTriple(Color.rgb -0.2 1.4 0.0))
        Assert.Equal("0,0,0", Tikz.rgbTriple(Color.rgb nan nan nan))

    [<Fact>]
    let ``alpha is not part of the colour definition`` () =
        let opaque = Tikz.colorDefinition "  " "qcA" (Color.rgba 0.2 0.4 0.6 1.0)
        let translucent = Tikz.colorDefinition "  " "qcA" (Color.rgba 0.2 0.4 0.6 0.25)
        Assert.Equal(opaque, translucent)

    [<Fact>]
    let ``a definition is a complete line`` () =
        Assert.Equal("  \\definecolor{qcA}{rgb}{0.2,0.4,0.6}\n", Tikz.colorDefinition "  " "qcA" (Color.rgb 0.2 0.4 0.6))

// ======================================================================================
// Identifiers — TeX command names are letters only
// ======================================================================================

module ``generated identifiers are TeX-safe`` =

    /// `\quivermark1` is not a command: it is `\quivermark` followed by a stray `1`. Every
    /// generated macro name has to be letters-only, which is why there is a bespoke id scheme
    /// rather than a counter.
    [<Fact>]
    let ``letterId is bijective base 26`` () =
        Assert.Equal("A", Tikz.letterId 0)
        Assert.Equal("Z", Tikz.letterId 25)
        Assert.Equal("AA", Tikz.letterId 26)
        Assert.Equal("AB", Tikz.letterId 27)
        Assert.Equal("BA", Tikz.letterId 52)

    [<Fact>]
    let ``letterId is letters-only and never empty`` () =
        for n in 0..500 do
            let id = Tikz.letterId n
            Assert.NotEqual<string>("", id)
            Assert.True(id |> Seq.forall System.Char.IsLetter, "not letters-only: " + id)

    [<Fact>]
    let ``letterId is injective`` () =
        let ids = [ 0..2000 ] |> List.map Tikz.letterId
        Assert.Equal(ids.Length, (ids |> List.distinct |> List.length))

    [<Fact>]
    let ``letterId tolerates a negative index`` () =
        Assert.Equal("A", Tikz.letterId -1)

    [<Fact>]
    let ``letterPart keeps only letters`` () =
        Assert.Equal("panelxaxis", Tikz.letterPart "panel-0.x_axis 1")
        Assert.Equal("", Tikz.letterPart "123-456")
        Assert.Equal("", Tikz.letterPart null)

// ======================================================================================
// THE GATE: a real engine reads what the escaper produces
//
// Everything above asserts that the emitter produces the strings we expect. This asserts that
// those strings are *valid TeX*, which is a different claim and the one that matters — an
// expectation can be confidently wrong about a macro name.
// ======================================================================================

module ``escaped text compiles`` =

    /// Strings chosen to cover every branch of the escaper at once: reserved characters, both
    /// codepoints of the ambiguous glyphs, superscript runs, and the dashes and spaces.
    let private hostile =
        [ "plain text"
          "50% yield_2 & more #1 $x$ {braced}"
          "backslash \\ tilde ~ caret ^"
          // D38's three: legal TeX that typesets as `¡ ¿ —` unescaped. Here to prove the
          // *replacements* compile, which is the half that assertion-only tests cannot check —
          // `\textquotedbl` looks just as plausible as these three and is a hard error in OT1.
          "a < b > c | d"
          "kg·m²·s⁻²"
          "E [keV] Ω µm Å °C"
          "\u2126 \u212B \u03BC" // the alternative codepoints
          "10⁻¹⁸·m²"
          "a – b — c ± d × e − f"
          "T [°C] and 1000\u00A0m and 5\u2009mm" ]

    [<Fact>]
    let ``the engine accepts every escaped hostile string`` () =
        let body = hostile |> List.map Tikz.escapeText |> String.concat " \\par "

        if not (TexToolchain.accepts "escaping" (TexToolchain.standalone (TexToolchain.textFragment body))) then
            // No engine on PATH. Reported rather than silently passed — see TexToolchain's note.
            System.Console.WriteLine("SKIPPED: " + TexToolchain.report ())

    /// The siunitx half of the M1 headline: what `UnitStyle.Siunitx` actually produces has to
    /// typeset, and `runContent` has to pass it through rather than escape it.
    [<Fact>]
    let ``the engine accepts siunitx unit markup passed through runContent`` () =
        let titles =
            [ Some "$a$ [\\qty{}{\\metre\\per\\second\\squared}]", "a [m·s⁻²]"
              Some "$E$ [\\qty{}{\\kilo\\electronvolt}]", "E [keV]"
              Some "$R$ [\\qty{}{\\ohm}]", "R [Ω]"
              Some "$T$ [\\qty{}{\\degreeCelsius}]", "T [°C]"
              None, "50% plain_text" ]

        let body =
            titles
            |> List.map (fun (tex, plain) -> Tikz.runContent tex plain)
            |> String.concat " \\par "

        if not (TexToolchain.accepts "siunitx" (TexToolchain.standalone (TexToolchain.textFragment body))) then
            System.Console.WriteLine("SKIPPED: " + TexToolchain.report ())

    /// The dimension emitter, exercised as actual TikZ coordinates. `bp` has to be a unit the
    /// engine accepts everywhere a length is wanted (D28).
    [<Fact>]
    let ``the engine accepts emitted bp dimensions as coordinates and widths`` () =
        let fragment =
            System.String.Concat
                [| "\\begin{tikzpicture}\n"
                   Tikz.colorDefinition "  " "qcA" (Color.rgb 0.12 0.56 1.0)
                   "  \\path[draw=qcA,line width="
                   Tikz.dim 0.75
                   ",draw opacity="
                   Tikz.ratio 0.6
                   "] "
                   Tikz.coord 0.0 0.0
                   " -- "
                   Tikz.coord 72.0 36.125
                   ";\n"
                   "  \\path[fill=qcA] "
                   Tikz.coord 1e-9 1e12
                   " circle[radius="
                   Tikz.dim 1.5
                   "];\n"
                   "\\end{tikzpicture}" |]

        if not (TexToolchain.accepts "dimensions" (TexToolchain.standalone fragment)) then
            System.Console.WriteLine("SKIPPED: " + TexToolchain.report ())

    /// **A gate that cannot fail proves nothing.** If the engine wiring swallowed errors — wrong
    /// log file, wrong error pattern, exit code ignored — every gate above would pass vacuously
    /// and the whole approach would be theatre. So one document is deliberately broken.
    [<Fact>]
    let ``the gate rejects a document with an undefined control sequence`` () =
        match TexToolchain.compile "negative" (TexToolchain.standalone "\\quiverdefinitelynotacommand") with
        | TexToolchain.Unavailable -> System.Console.WriteLine("SKIPPED: " + TexToolchain.report ())
        | TexToolchain.Compiled ->
            failwith "the TeX gate accepted an undefined control sequence, so it cannot fail and proves nothing"
        | TexToolchain.Failed errs -> Assert.Contains("Undefined control sequence", errs)

    /// Not an assertion — a line in the test log saying whether the gates above actually ran.
    /// Without it a green suite on a machine with no TeX looks identical to a green suite that
    /// checked everything.
    [<Fact>]
    let ``the toolchain reports what it found`` () =
        System.Console.WriteLine("Quiver TeX gate: " + TexToolchain.report ())
        Assert.True(true)
