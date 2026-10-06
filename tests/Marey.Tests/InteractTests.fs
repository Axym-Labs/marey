module Marey.Tests.InteractTests

// M4.5 — `Marey.Interact`: typed controls, the applicative block, and the pure step (D100,
// D101). Gates 1, 2, 3 and 12 of `m4.5-plan.md` §4.

open System
open System.IO
open System.Text
open Xunit
open Marey

let private fieldUnit = U.div Derived.volt U.metre

let private wave (f: float<1 / s>) (damped: bool) =
    let t = Array.init 50 (fun i -> float i * 0.02<s>)
    let decay = if damped then 1.0<1 / s> else 0.0<1 / s>
    let y = t |> Array.map (fun t -> exp (-decay * t) * sin (2.0 * Math.PI * f * t) * 1.0<m>)
    Chart.line U.second U.metre t y

let private live () =
    interact {
        let! f = Control.slider (U.recip U.second) "f" 0.5<1 / s> 5.0<1 / s> 1.0<1 / s>
        and! damped = Control.checkbox "damped" false
        return wave f damped
    }

let private svgOf (fig: Figure) =
    fig |> Chart.render SvgBackend.render (Chart.size 320.0 200.0)

// ======================================================================================
// Gate 1 — one quantity formatter
// ======================================================================================

module ``one quantity formatter`` =

    /// A slider's label and the quiver key are written by the same function: the same quantity
    /// under the same prefix gives the same string (`2 kV·m⁻¹`, the key `FieldTests` pins).
    [<Fact>]
    let ``a slider's label is the quiver key's string`` () =
        let c = Control.sliderBy fieldUnit "E" 0.0<V / m> 5000.0<V / m> 100.0<V / m> 2000.0<V / m>
        Assert.Equal("2 kV·m⁻¹", c.Specs.[0].Show 2000.0)

    /// The prefix is the range's, chosen once, so the label does not change prefix mid-drag.
    [<Fact>]
    let ``a slider's prefix does not move with its value`` () =
        let c = Control.sliderBy fieldUnit "E" 0.0<V / m> 5000.0<V / m> 100.0<V / m> 2000.0<V / m>
        Assert.Equal("0.9 kV·m⁻¹", c.Specs.[0].Show 900.0)
        Assert.Equal("0 kV·m⁻¹", c.Specs.[0].Show 0.0)

    /// The step sets the places: a step of 0.1 shows one, a step of 5 shows none.
    [<Fact>]
    let ``a slider shows the places its step needs`` () =
        let fine = Control.sliderBy U.kelvin "T" 0.0<K> 10.0<K> 0.1<K> 2.5<K>
        let coarse = Control.sliderBy U.kelvin "T" 200.0<K> 400.0<K> 5.0<K> 300.0<K>
        Assert.Equal("2.5 K", fine.Specs.[0].Show 2.5)
        Assert.Equal("300 K", coarse.Specs.[0].Show 300.0)

// ======================================================================================
// Gate 2 — a control is typed, and the block is applicative
// ======================================================================================

module ``a control is typed`` =

    let private preamble () =
        let binDir = Path.GetDirectoryName(Uri(typeof<Marey.Figure>.Assembly.Location).LocalPath)
        let sb = StringBuilder()

        for name in [ "Marey.Core"; "Marey.Units"; "Marey.Anim"; "Marey.Plot"; "Marey.Interact" ] do
            sb.AppendLine(sprintf "#r @\"%s\"" (Path.Combine(binDir, name + ".dll"))) |> ignore

        sb.AppendLine("open Marey").ToString()

    let private compile (name: string) (body: string) =
        let tool = Tools.dotnet.Force()

        if not tool.IsAvailable then
            None
        else
            let path = Path.Combine(Path.GetTempPath(), sprintf "marey-interact-%s.fsx" name)
            File.WriteAllText(path, preamble () + "\n" + body + "\n", UTF8Encoding false)

            match Toolchain.run tool (sprintf "fsi --nologo \"%s\"" path) (Path.GetTempPath()) 600_000 with
            | ToolUnavailable _ -> None
            | ToolTimedOut why -> failwith why
            | ToolRan run -> Some(run.ExitCode, path, run.Out + "\n" + run.Error)

    let private view =
        "let view (t: float<K>) (on: bool) = Chart.line U.kelvin U.metre [| t |] [| (if on then 1.0<m> else 0.0<m>) |]"

    /// The positive control. Without it a bad `#r` would make both negatives green.
    [<Fact>]
    let ``the reference block compiles`` () =
        let body =
            String.concat
                "\n"
                [ view
                  "let live ="
                  "    interact {"
                  "        let! t = Control.slider U.kelvin \"T\" 200.0<K> 400.0<K> 300.0<K>"
                  "        and! on = Control.checkbox \"on\" true"
                  "        return view t on"
                  "    }"
                  "printfn \"controls %d\" live.Controls.Length" ]

        match compile "ok" body with
        | None -> Console.WriteLine("SKIPPED: dotnet not found on PATH")
        | Some(code, path, output) -> Assert.True((code = 0), sprintf "the control did not compile: %s\n%s" path output)

    /// **Gate 2.** A slider over kelvin hands its view kelvin, and a view over seconds is a
    /// compile error, not a figure with the wrong axis.
    [<Fact>]
    let ``a kelvin slider cannot feed a view over seconds`` () =
        let body =
            String.concat
                "\n"
                [ "let view (t: float<s>) = Chart.line U.second U.metre [| t |] [| 1.0<m> |]"
                  "let live ="
                  "    interact {"
                  "        let! t = Control.slider U.kelvin \"T\" 200.0<K> 400.0<K> 300.0<K>"
                  "        return view t"
                  "    }" ]

        match compile "kelvin-seconds" body with
        | None -> Console.WriteLine("SKIPPED: dotnet not found on PATH")
        | Some(code, path, output) ->
            Assert.True(code <> 0, sprintf "a kelvin slider fed a view over seconds: %s" path)
            Assert.Contains("error FS0001", output)

    /// The block is applicative: a second `let!` that could depend on the first has no `Bind`
    /// to compile against, so a widget's controls never depend on its state.
    [<Fact>]
    let ``a control cannot be chosen by another control's value`` () =
        let body =
            String.concat
                "\n"
                [ view
                  "let live ="
                  "    interact {"
                  "        let! on = Control.checkbox \"on\" true"
                  "        let! t = if on then Control.slider U.kelvin \"T\" 200.0<K> 400.0<K> 300.0<K> else Control.constant 0.0<K>"
                  "        return view t on"
                  "    }" ]

        match compile "dependent" body with
        | None -> Console.WriteLine("SKIPPED: dotnet not found on PATH")
        | Some(code, path, output) ->
            Assert.True(code <> 0, sprintf "a dependent let! compiled: %s" path)
            Assert.Contains("Bind", output)

// ======================================================================================
// Gate 3 — the step is pure
// ======================================================================================

module ``the step`` =

    let private script =
        [ Set(0, 2.0); Set(1, 1.0); Set(0, 3.7); Set(0, 3.7); Set(1, 0.0); Set(0, 0.5) ]

    let private replay (l: Live) =
        script |> List.scan (Live.step l) (Live.init l) |> List.map (fun s -> s, svgOf (Live.view l s))

    /// **Gate 3.** The same messages from the same state give the same states and byte-identical
    /// figures, twice in a row and on two threads at once.
    [<Fact>]
    let ``the same messages give the same figures`` () =
        let first = replay (live ())
        let second = replay (live ())
        let parallel' = [| live (); live () |] |> Array.Parallel.map replay

        Assert.Equal(first.Length, script.Length + 1)

        for run in [ second; parallel'.[0]; parallel'.[1] ] do
            List.iter2
                (fun (s1: LiveState, svg1: string) (s2: LiveState, svg2: string) ->
                    Assert.Equal<LiveState>(s1, s2)
                    Assert.Equal(svg1, svg2))
                first
                run

    /// Each state's figure is the view of its values: the step changes nothing else.
    [<Fact>]
    let ``a state's figure is the view of its values`` () =
        let l = live ()
        let s = script |> List.fold (Live.step l) (Live.init l)
        Assert.Equal<float[]>([| 0.5; 0.0 |], s.Values)
        Assert.Equal(svgOf (wave 0.5<1 / s> false), svgOf (Live.view l s))

    /// The step is total: an index that names no control and a NaN leave the state alone, a
    /// slider is clamped into its range, and a value equal to the current one is the same state.
    [<Fact>]
    let ``the step is total`` () =
        let l = live ()
        let s0 = Live.init l
        Assert.Same(s0, Live.step l s0 (Set(7, 1.0)))
        Assert.Same(s0, Live.step l s0 (Set(-1, 1.0)))
        Assert.Same(s0, Live.step l s0 (Set(0, nan)))
        Assert.Same(s0, Live.step l s0 (Set(0, 1.0)))
        Assert.Equal(5.0, (Live.step l s0 (Set(0, 99.0))).Values.[0])
        Assert.Equal(1.0, (Live.step l s0 (Set(1, 0.3))).Values.[1])

    /// The step never mutates the state it was given.
    [<Fact>]
    let ``the step copies`` () =
        let l = live ()
        let s0 = Live.init l
        let _ = Live.step l s0 (Set(0, 4.0))
        Assert.Equal<float[]>([| 1.0; 0.0 |], s0.Values)

    /// A select reads its option by index, rounded into range.
    [<Fact>]
    let ``a select reads its option`` () =
        let l =
            interact {
                let! f = Control.select "f" [ "slow", 0.5<1 / s>; "fast", 4.0<1 / s> ] 0
                return wave f false
            }

        let s = Live.step l (Live.init l) (Set(0, 1.4))
        Assert.Equal<float[]>([| 1.0 |], s.Values)
        Assert.Equal<string[]>([| "fast" |], Live.shown l s)
        Assert.Equal(svgOf (wave 4.0<1 / s> false), svgOf (Live.view l s))

    /// Controls keep the block's order, so a message's index names the control the reader moved.
    [<Fact>]
    let ``controls keep the block's order`` () =
        let l = live ()
        Assert.Equal<string[]>([| "f"; "damped" |], l.Controls |> Array.map (fun c -> c.Label))
        Assert.Equal<string[]>([| "1 Hz"; "off" |], Live.shown l (Live.init l))

    /// A slider refuses a range, step or start it cannot honour, by name.
    [<Fact>]
    let ``a slider refuses what it cannot draw`` () =
        Assert.Throws<ArgumentException>(fun () -> Control.slider U.kelvin "T" 400.0<K> 200.0<K> 300.0<K> |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> Control.slider U.kelvin "T" 200.0<K> 400.0<K> 500.0<K> |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> Control.sliderBy U.kelvin "T" 200.0<K> 400.0<K> 0.0<K> 300.0<K> |> ignore) |> ignore

module ``the wire`` =

    [<Fact>]
    let ``set reads an index and a value`` () =
        Assert.Equal(Some(Set(1, 2.5)), LiveMsg.parse "set" "[1,2.5]")
        Assert.Equal(Some(Set(0, -3.0)), LiveMsg.parse "set" " [ 0 , -3 ] ")

    [<Fact>]
    let ``anything else is ignored rather than fatal`` () =
        for t, p in [ "set", "2.5"; "set", "[1]"; "set", "[a,1]"; "set", null; "zoom", "[1,2]"; "set", "{\"i\":1}" ] do
            Assert.Equal(None, LiveMsg.parse t p)

// ======================================================================================
// Gate 12 — `Marey.Anim` stays put
// ======================================================================================

module ``anim stays put`` =

    /// **Gate 12.** D80 promised that `sample` could be added without `Anim` moving. Interaction
    /// lives beside animation, not inside it: `Marey.Anim` references nothing that interacts.
    [<Fact>]
    let ``Marey.Anim does not reference Marey.Interact`` () =
        let fsproj = File.ReadAllText(Path.Combine(Golden.repoRoot.Force(), "src", "Marey.Anim", "Marey.Anim.fsproj"))
        Assert.DoesNotContain("Marey.Interact", fsproj)
        Assert.DoesNotContain("Marey.Interact", typeof<Anim<float>>.Assembly.GetReferencedAssemblies() |> Array.map (fun a -> a.Name) |> String.concat ";")

// ======================================================================================
// Gate 4 — the document is well-formed, and formats nothing
// ======================================================================================

module ``the widget document`` =

    open System.Xml.Linq

    let private at (msgs: LiveMsg list) =
        let l = live ()
        l, msgs |> List.fold (Live.step l) (Live.init l)

    /// **Gate 4.** Both forms parse as XML, the oracle every fragment `Marey.Html` writes is held to.
    [<Fact>]
    let ``both forms parse`` () =
        let l, s = at []

        for isLive in [ true; false ] do
            XDocument.Parse(Html.widget l s isLive) |> ignore

    /// No number is formatted in the browser: every label arrives written by the kernel.
    [<Fact>]
    let ``the script formats no number`` () =
        let l, s = at []
        let doc = Html.widget l s true

        for formatter in [ "toFixed"; "toPrecision"; "toLocaleString"; "Intl."; "toExponential" ] do
            Assert.DoesNotContain(formatter, doc)

    /// The static form disables every control, says so, and touches the channel only behind
    /// the check that it exists.
    [<Fact>]
    let ``the static form is disabled and guarded`` () =
        let l, s = at []
        let doc = Html.widget l s false
        let disabled = doc.Split([| "disabled=\"disabled\"" |], StringSplitOptions.None).Length - 1
        Assert.Equal(l.Controls.Length, disabled)
        Assert.Contains("Static", doc)
        Assert.Contains("if (!ch) { disable(null); return; }", doc)
        Assert.True(doc.IndexOf "if (!ch)" < doc.IndexOf "ch.post", "the guard precedes the first use")

    /// The controls are written from the state, so a snapshot's slider is where its figure says.
    /// The phase 0 spike drew a 3.7 Hz figure under a slider at 1 Hz.
    [<Fact>]
    let ``the controls are written from the state`` () =
        let l, s = at [ Set(0, 3.7); Set(1, 1.0) ]
        let doc = Html.widget l s false
        Assert.Contains("value=\"3.7\"", doc)
        Assert.Contains("<output id=\"v0\">3.7 Hz</output>", doc)
        Assert.Contains("checked=\"checked\"", doc)
        Assert.Contains(Html.widgetFigure l s, doc)

    /// A label is text, not markup.
    [<Fact>]
    let ``a label is escaped`` () =
        let l =
            interact {
                let! on = Control.checkbox "<b>&amp" true
                return wave 1.0<1 / s> on
            }

        let doc = Html.widget l (Live.init l) true
        Assert.DoesNotContain("<b>&amp", doc)
        XDocument.Parse doc |> ignore

    /// The view announces its width at the state's aspect, so the kernel lays out for the frame
    /// it is drawn in; a resize re-arranges, and a nonsense size is ignored.
    [<Fact>]
    let ``a size re-arranges, and a nonsense one is ignored`` () =
        let l, s = at []
        let wide = Live.step l s (Resize(960.0, 600.0))
        Assert.Equal(960.0, float wide.Shot.Outer.Extent.Width)
        Assert.Same(s, Live.step l s (Resize(nan, 400.0)))
        Assert.Same(s, Live.step l s (Resize(-5.0, 400.0)))
        Assert.Contains("[w, Math.round(w * 0.625)]", Html.widget l s true)

// ======================================================================================
// Gate 5 — a widget does not breathe
// ======================================================================================

module ``a widget does not breathe`` =

    /// A line whose peak gains a digit across the slider (`ShotTests`' fixture, as a control).
    let private ramp (top: float) =
        let xs = Array.init 24 (fun i -> float i / 23.0 * 10.0<s>)
        Chart.line U.second U.metre xs (xs |> Array.map (fun t -> t / 10.0<s> * top * 1.0<m>))

    let private digits =
        interact {
            let! top = Control.sliderBy U.one "top" 9.0 110.0 1.0 9.0
            return ramp top
        }

    /// Its peak is at the slider's middle, so the extremes cannot see it: the union over them
    /// is too narrow for the middle state, and only the overflow path can save it.
    let private hump =
        interact {
            let! u = Control.sliderBy U.one "u" 0.0 1.0 0.01 0.0
            return ramp (9.0 + 4000.0 * u * (1.0 - u))
        }

    let private transforms (node: Node) =
        node |> Scene.fold (fun acc n -> match n with Transform(a, _) -> a :: acc | _ -> acc) []

    let private sceneAt (l: Live) (s: LiveState) (v: float) =
        let s' = Live.step l s (Set(0, v))
        s', Live.scene l s'

    /// The positive control: laid out per state, the fixture moves the panel.
    [<Fact>]
    let ``laid out per state, the fixture moves the panel`` () =
        let perState = [ 9.0; 60.0; 110.0 ] |> List.map (fun top -> transforms (Chart.toScene Live.DefaultSize (ramp top)))
        Assert.True((List.distinct perState).Length > 1, "the fixture must move the panel, or the gate is vacuous")

    /// **Gate 5.** Inside the widget the plot rectangle is the same at every state the slider
    /// can reach, and nothing re-arranged to make it so.
    [<Fact>]
    let ``the plot rectangle is the same at every state`` () =
        let s0 = Live.init digits

        let rects =
            [ 9.0; 10.0; 50.0; 99.0; 100.0; 110.0 ]
            |> List.map (fun v -> let s, n = sceneAt digits s0 v in s.Grown.Length, transforms n)

        Assert.All(rects, fun (grown, _) -> Assert.Equal(0, grown))
        Assert.Single(rects |> List.map snd |> List.distinct) |> ignore

    /// D106's second half. A state the extremes did not foresee does not fit, is not drawn
    /// clipped, and is counted; the grown arrangement then holds it and the extremes alike.
    [<Fact>]
    let ``a state that overflows is counted, and grows the arrangement`` () =
        let s0 = Live.init hump
        Assert.False(Chart.fitsIn TextMeasure.approx s0.Shot (Live.view hump (Live.step hump s0 (Set(0, 0.5)) |> fun s -> { s0 with Values = s.Values })))

        let grown = Live.step hump s0 (Set(0, 0.5))
        Assert.Equal(1, grown.Grown.Length)
        Assert.NotEqual<Affine<nd, nd> list>(transforms (Live.scene hump s0), transforms (Live.scene hump grown))

        for v in [ 0.0; 0.25; 0.5; 1.0 ] do
            let s = Live.step hump grown (Set(0, v))
            Assert.Equal(1, s.Grown.Length)
            Assert.True(Chart.fitsIn TextMeasure.approx s.Shot (Live.view hump s), sprintf "u = %g fits the grown arrangement" v)
