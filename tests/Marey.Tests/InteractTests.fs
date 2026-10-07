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
    let ``zoom, pan and reset read their numbers`` () =
        Assert.Equal(Some(Zoom { X0 = 1.0<px>; Y0 = 2.0<px>; X1 = 3.5<px>; Y1 = 4.0<px> }), LiveMsg.parse "zoom" "[1,2,3.5,4]")
        Assert.Equal(Some(Pan({ X = 10.0<px>; Y = 20.0<px> }, { X = -3.0<px>; Y = 0.5<px> })), LiveMsg.parse "pan" "[10,20,-3,0.5]")
        Assert.Equal(Some Reset, LiveMsg.parse "reset" "[]")
        Assert.Equal(Some Reset, LiveMsg.parse "reset" null)

    [<Fact>]
    let ``anything else is ignored rather than fatal`` () =
        for t, p in [ "set", "2.5"; "set", "[1]"; "set", "[a,1]"; "set", null; "zoom", "[1,2]"; "pan", "[1,2,3]"; "zoom", "[1,2,x,4]"; "set", "{\"i\":1}" ] do
            Assert.Equal(None, LiveMsg.parse t p)

// ======================================================================================
// Gate 7 — zoom selects what was dragged
// ======================================================================================

module ``zoom and pan`` =

    let private still (fig: Figure) : Live = interact { return fig }

    let private first (l: Live) (s: LiveState) = (Live.panels l s).[0]

    /// The px a data point is drawn at, through the panel's own forward scales: the y flip once,
    /// here, and the step's inverse has to undo it.
    let private pxOf (v: PanelView) (x: float, y: float) : Vec2<px> =
        { X = v.Plot.X0 + float (v.X.Project x) * (v.Plot.X1 - v.Plot.X0)
          Y = v.Plot.Y1 - float (v.Y.Project y) * (v.Plot.Y1 - v.Plot.Y0) }

    /// Drag from the top-left of a data box to its bottom-right.
    let private drag (l: Live) (s: LiveState) ((xa, xb), (ya, yb)) =
        let v = first l s
        let a = pxOf v (xa, yb)
        let b = pxOf v (xb, ya)
        Live.step l s (Zoom { X0 = a.X; Y0 = a.Y; X1 = b.X; Y1 = b.Y })

    let private near (expected: float * float) (actual: float * float) =
        let (lo, hi), (lo', hi') = expected, actual
        let tol = 1e-9 * abs (hi - lo)
        Assert.True(abs (lo - lo') <= tol && abs (hi - hi') <= tol, sprintf "expected %A, got %A" expected actual)

    /// A box at stated fractions of a domain, in the scale's own spacing: linear on a linear
    /// axis, by factors on a log one.
    let private within (s: IScaleErased) (f0: float) (f1: float) =
        s.Invert(f0 * 1.0<nd>), s.Invert(f1 * 1.0<nd>)

    /// **Gate 7.** After a drag, the panel's domains are the dragged rectangle read back through
    /// its scales. The y box sits low in the plot, so a lost flip zooms onto the wrong band.
    let private gate (l: Live) =
        let s0 = Live.init l
        let v = first l s0
        let target = within v.X 0.2 0.45, within v.Y 0.1 0.3
        let s1 = drag l s0 target
        let v1 = first l s1
        near (fst target) v1.X.Domain
        near (snd target) v1.Y.Domain

    [<Fact>]
    let ``a drag zooms a linear panel onto what it covered`` () = gate (still (wave 1.0<1 / s> false))

    [<Fact>]
    let ``a drag zooms a log axis onto what it covered`` () =
        let t = Array.init 60 (fun i -> 10.0 ** (float i / 15.0 - 3.0) * 1.0<s>)
        let x = t |> Array.map (fun t -> sqrt (float t) * 1.0<m>)
        let l = still (Chart.line U.second U.metre t x |> Chart.withXLog)
        Assert.Equal(ScaleKind.Log 10.0, (first l (Live.init l)).X.Kind)
        gate l

    [<Fact>]
    let ``a drag zooms a phase space onto what it covered`` () =
        let flow: Flow<rad, rad / s, s> = fun th om -> struct (om, -9.81<1 / s^2> * sin (float th) * 1.0<rad>)
        let grid = Field.sampleFlow (Grid.linear -6.5<rad> 6.5<rad> 41) (Grid.linear -9.0<rad / s> 9.0<rad / s> 31) flow
        gate (still (Field.directions Derived.radian (U.div Derived.radian U.second) grid))

    /// A drag that starts outside every plot area, and a click, leave the state alone.
    [<Fact>]
    let ``a click or a drag off the plot changes nothing`` () =
        let l = still (wave 1.0<1 / s> false)
        let s0 = Live.init l
        let v = first l s0
        Assert.Same(s0, Live.step l s0 (Zoom { X0 = 1.0<px>; Y0 = 1.0<px>; X1 = v.Plot.X1; Y1 = v.Plot.Y1 }))
        let c = { X = (v.Plot.X0 + v.Plot.X1) / 2.0; Y = (v.Plot.Y0 + v.Plot.Y1) / 2.0 }
        Assert.Same(s0, Live.step l s0 (Zoom { X0 = c.X; Y0 = c.Y; X1 = c.X + 2.0<px>; Y1 = c.Y + 3.0<px> }))

    /// A drag along the axis that wanders less than `MinDrag` across it zooms that axis alone.
    [<Fact>]
    let ``a flat drag zooms one axis`` () =
        let l = still (wave 1.0<1 / s> false)
        let s0 = Live.init l
        let v = first l s0
        let y = (v.Plot.Y0 + v.Plot.Y1) / 2.0
        let s1 = Live.step l s0 (Zoom { X0 = v.Plot.X0 + 40.0<px>; Y0 = y; X1 = v.Plot.X0 + 120.0<px>; Y1 = y + 3.0<px> })
        Assert.Equal<ZoomAxis[]>([| ZoomAxis.X |], s1.Zoom |> Array.map (fun z -> z.Axis))
        Assert.Equal(v.Y.Domain, (first l s1).Y.Domain)

    /// D73 under a zoom: the prefix is the zoomed domain's. Two drags take a metre axis down to
    /// millimetres, and the title follows rather than labelling `0.001 m`.
    [<Fact>]
    let ``a zoom takes the prefix of what it shows`` () =
        let l = still (wave 1.0<1 / s> false)
        let s0 = Live.init l
        Assert.DoesNotContain("[mm]", Html.widgetFigure l s0)
        let v0 = first l s0
        let s1 = drag l s0 (within v0.X 0.1 0.9, (0.0, 0.1))
        let v1 = first l s1
        let s2 = drag l s1 (within v1.X 0.1 0.9, (0.002, 0.006))
        near (0.002, 0.006) (first l s2).Y.Domain
        Assert.Contains("[mm]", Html.widgetFigure l s2)

    /// The case the overflow rule cannot see. Kilometres zoomed to a few hundred metres label
    /// `0.2 0.4 0.6` under the old prefix, which is no wider than `200 400 600` and so fits the
    /// old gutters. A still figure of that domain says metres, and so does the zoom, because a
    /// zoom re-arranges rather than waiting for a label to overflow.
    [<Fact>]
    let ``a zoom that fits still takes its own prefix`` () =
        let t = Array.init 20 (fun i -> float i * 0.05<s>)
        let l = still (Chart.line U.second U.metre t (t |> Array.map (fun t -> t * 2000.0<m/s>)))
        let s0 = Live.init l
        Assert.Contains("[km]", Html.widgetFigure l s0)
        let v0 = first l s0
        let s1 = drag l s0 (within v0.X 0.1 0.9, (150.0, 850.0))
        Assert.True(Chart.fitsIn TextMeasure.approx s0.Shot (Live.view l s1), "the zoomed figure fits the old arrangement")
        Assert.Contains("[m]", Html.widgetFigure l s1)
        Assert.DoesNotContain("[km]", Html.widgetFigure l s1)

    /// A pan on a log axis slides by a factor: the ratio of the ends is kept, and panning back
    /// returns the domain it started from.
    [<Fact>]
    let ``a pan slides a log axis by a factor, and back`` () =
        let t = Array.init 60 (fun i -> 10.0 ** (float i / 15.0 - 3.0) * 1.0<s>)
        let l = still (Chart.line U.second U.metre t (t |> Array.map (fun t -> float t * 1.0<m>)) |> Chart.withXLog)
        let s0 = Live.init l
        let v0 = first l s0
        let c = { X = (v0.Plot.X0 + v0.Plot.X1) / 2.0; Y = (v0.Plot.Y0 + v0.Plot.Y1) / 2.0 }
        let s1 = Live.step l s0 (Pan(c, { X = 50.0<px>; Y = 0.0<px> }))
        let lo0, hi0 = v0.X.Domain
        let lo1, hi1 = (first l s1).X.Domain
        Assert.True(lo1 < lo0, "dragging right shows what was left of the panel")
        Assert.True(abs (hi1 / lo1 - hi0 / lo0) <= 1e-9 * (hi0 / lo0), sprintf "ratio %g, was %g" (hi1 / lo1) (hi0 / lo0))
        let s2 = Live.step l s1 (Pan(c, { X = -50.0<px>; Y = 0.0<px> }))
        near v0.X.Domain (first l s2).X.Domain

    /// A double click puts back the state before the zoom, arrangement and all.
    [<Fact>]
    let ``a reset returns to the view's own domains`` () =
        let l = live ()
        let s0 = Live.init l
        let v = first l s0
        let zoomed = drag l s0 (within v.X 0.2 0.6, within v.Y 0.2 0.6)
        Assert.NotEqual<LiveState>(s0, zoomed)
        Assert.Equal<LiveState>(s0, Live.step l zoomed Reset)
        Assert.Same(s0, Live.step l s0 Reset)

    /// A zoom outlives the controls: a slider moved while zoomed draws the new view in the old box.
    [<Fact>]
    let ``a zoom survives a control`` () =
        let l = live ()
        let s0 = Live.init l
        let v = first l s0
        let box = within v.X 0.2 0.6, within v.Y 0.2 0.6
        let s1 = Live.step l (drag l s0 box) (Set(0, 3.0))
        near (fst box) (first l s1).X.Domain
        near (snd box) (first l s1).Y.Domain

    /// Panels whose axis the view drew identically zoom together, a facet stays a facet, and a
    /// panel over a different domain is left alone.
    [<Fact>]
    let ``panels that agree on an axis zoom together`` () =
        let longer =
            let t = Array.init 50 (fun i -> float i * 0.04<s>)
            Chart.line U.second U.metre t (t |> Array.map (fun t -> float t * 1.0<m>))

        let fig = wave 1.0<1 / s> false |> Chart.beside (wave 2.0<1 / s> false) |> Chart.beside longer
        let l = still fig
        let s0 = Live.init l
        let views = Live.panels l s0
        Assert.Equal(3, views.Length)
        Assert.Equal(views.[0].X.Domain, views.[1].X.Domain)
        let v = views.[0]
        let y = (v.Plot.Y0 + v.Plot.Y1) / 2.0
        let x0, x1 = v.Plot.X0 + 30.0<px>, v.Plot.X0 + 90.0<px>
        let s1 = Live.step l s0 (Zoom { X0 = x0; Y0 = y; X1 = x1; Y1 = y })
        let after = Live.panels l s1
        Assert.NotEqual(views.[0].X.Domain, after.[0].X.Domain)
        Assert.Equal(after.[0].X.Domain, after.[1].X.Domain)
        Assert.Equal(views.[2].X.Domain, after.[2].X.Domain)
        Assert.Equal(views.[1].Y.Domain, after.[1].Y.Domain)

    /// Zooms and pans replay like any other message: same states, same figures, on two threads.
    [<Fact>]
    let ``zooms and pans replay`` () =
        let run (l: Live) =
            let s0 = Live.init l
            let v = first l s0
            let c = { X = (v.Plot.X0 + v.Plot.X1) / 2.0; Y = (v.Plot.Y0 + v.Plot.Y1) / 2.0 }

            [ Zoom { X0 = c.X - 60.0<px>; Y0 = c.Y - 40.0<px>; X1 = c.X + 30.0<px>; Y1 = c.Y + 50.0<px> }
              Set(0, 3.0)
              Pan(c, { X = 12.0<px>; Y = -7.0<px> })
              Set(1, 1.0)
              Reset ]
            |> List.scan (Live.step l) s0
            |> List.map (fun s -> s, Html.widgetFigure l s)

        let runs = [| live (); live (); live () |] |> Array.Parallel.map run

        for other in runs.[1..] do
            List.iter2 (fun (a: LiveState, x: string) (b: LiveState, y: string) -> Assert.Equal<LiveState>(a, b); Assert.Equal(x, y)) runs.[0] other

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

// ======================================================================================
// Gate 10 — live equals replayed (D102)
// ======================================================================================

module ``live equals replayed`` =

    /// A drag across the middle of the first panel, as the view would send it: two corners in px.
    let private dragPx (l: Live) =
        let v = (Live.panels l (Live.init l)).[0]
        let at (fx: float) (fy: float) = v.Plot.X0 + fx * (v.Plot.X1 - v.Plot.X0), v.Plot.Y0 + fy * (v.Plot.Y1 - v.Plot.Y0)
        let (x0, y0), (x1, y1) = at 0.25 0.2, at 0.7 0.8
        sprintf "[%.17g,%.17g,%.17g,%.17g" (float x0) (float y0) (float x1) (float y1)

    /// A session as the kernel received it: wire types and payloads, each stamped by the page's
    /// clock in ms, which starts wherever the page did. The values are ones a lossy number
    /// format would change: e, and a time that is not a short decimal.
    let private wire (l: Live) =
        [ "size", "[640,400,1523.25]"
          "set", "[0,2.718281828459045,1610.0000000000002]"
          "set", "[0,3.3,1650.5]"
          "set", "[1,1,1800.125]"
          "zoom", dragPx l + ",2400]"
          "pan", "[300,200,-12.5,4,2700.75]"
          "reset", "[3100]"
          "set", "[0,0.75,3333.3333333333335]" ]

    let private log (l: Live) =
        wire l
        |> List.map (fun (t, p) ->
            match LiveMsg.parseStamped t p with
            | Some(Some at, msg) -> at, msg
            | other -> failwithf "%s %s read as %A" t p other)
        |> Array.ofList
        |> Recording.ofEvents

    let private frames (l: Live) (recording: Recording<LiveState>) =
        Live.film l recording |> Frames.render FramePass.Serial SvgBackend.render 30.0

    /// **Gate 10.** A log written to JSON, read back and replayed gives a film whose every frame
    /// is byte-identical to the original's. The film moves — several distinct frames — so the
    /// gate cannot hold by both sides being one still.
    [<Fact>]
    let ``a replayed log films the same frames`` () =
        let l = live ()
        let original = log l

        let replayed =
            match LiveLog.ofJson (LiveLog.toJson original) with
            | Ok r -> r
            | Error e -> failwith e

        let a = frames l (Live.record l original)
        let b = frames (live ()) (Live.record (live ()) replayed)

        Assert.Equal(a.Length, b.Length)
        Assert.True(a.Length > 40, sprintf "%d frames" a.Length)
        Assert.True((Array.distinct a).Length >= 5, sprintf "%d distinct frames" (Array.distinct a).Length)

        Array.iteri2 (fun i (x: string) (y: string) -> Assert.True((x = y), sprintf "frame %d differs" i)) a b

    /// The log comes back bit for bit: every time and every number, the awkward ones included.
    [<Fact>]
    let ``a log round-trips exactly`` () =
        let l = live ()
        let original = log l

        let back =
            match LiveLog.ofJson (LiveLog.toJson original) with
            | Ok r -> r
            | Error e -> failwith e

        Assert.Equal<(float<s> * LiveMsg)[]>(original.Events, back.Events)

        let odd = Recording.ofEvents [| 0.0<s>, Set(0, nan); 0.1<s> + 0.2<s>, Resize(infinity, -infinity); 1e-300<s>, Set(1, 5e-324) |]

        match LiveLog.ofJson (LiveLog.toJson odd) with
        | Ok r -> Assert.Equal(LiveLog.toJson odd, LiveLog.toJson r)
        | Error e -> failwith e

    /// The times are the view's: the trailing number is the event's `timeStamp` in ms, read in
    /// seconds, rebased to the first message; `parse` drops it, and a message without one still
    /// reads.
    [<Fact>]
    let ``the times are the view's`` () =
        Assert.Equal(Some(Some 1.5<s>, Set(1, 2.5)), LiveMsg.parseStamped "set" "[1,2.5,1500]")
        Assert.Equal(Some(None, Set(1, 2.5)), LiveMsg.parseStamped "set" "[1,2.5]")
        Assert.Equal(Some(Some 2.0<s>, Reset), LiveMsg.parseStamped "reset" "[2000]")
        Assert.Equal(Some(Set(1, 2.5)), LiveMsg.parse "set" "[1,2.5,1500]")
        Assert.Equal(None, LiveMsg.parse "set" "[1,2.5,1500,7]")

        let times = (log (live ())).Events |> Array.map fst
        Assert.Equal(0.0<s>, times.[0])
        Assert.Equal(3.3333333333333335 - 1.52325, float times.[times.Length - 1], 12)

    /// The order is the truth: a time that runs backwards, or is not a number, holds the one
    /// before it rather than reordering the values.
    [<Fact>]
    let ``a clock that runs backwards holds`` () =
        let r = Recording.ofEvents [| 10.0<s>, "a"; 12.0<s>, "b"; 11.0<s>, "c"; nan * 1.0<s>, "d"; 13.0<s>, "e" |]
        Assert.Equal<(float<s> * string)[]>([| 0.0<s>, "a"; 2.0<s>, "b"; 2.0<s>, "c"; 2.0<s>, "d"; 3.0<s>, "e" |], r.Events)

    /// Sample and hold: between two values the earlier one, at a value's time that value, at a
    /// shared time the later, and the last value at the end and past it.
    [<Fact>]
    let ``sample holds`` () =
        let a = Recording.sample (Recording.ofEvents [| 0.0<s>, 1; 1.0<s>, 2; 1.0<s>, 3; 2.5<s>, 4 |])
        Assert.Equal(2.5<s>, a.Duration)
        Assert.Equal(1, a.At 0.0<s>)
        Assert.Equal(1, a.At 0.999<s>)
        Assert.Equal(3, a.At 1.0<s>)
        Assert.Equal(3, a.At 2.4<s>)
        Assert.Equal(4, a.At 2.5<s>)
        Assert.Equal(4, a.At 99.0<s>)
        Assert.Throws<ArgumentException>(fun () -> Recording.sample { Events = [||] } |> ignore) |> ignore

    /// A session with no messages records its first state, so it can always be sampled: a
    /// still, of zero length.
    [<Fact>]
    let ``no messages is the first state`` () =
        let l = live ()
        let r = Live.record l { Events = [||] }
        Assert.Equal(1, r.Events.Length)
        Assert.Equal<LiveState>(Live.init l, snd r.Events.[0])
        Assert.Equal(0.0<s>, (Live.film l r).Duration)

    /// The film is laid out over every state the recording reached, so none overflows it.
    [<Fact>]
    let ``every recorded state fits the film`` () =
        let l = live ()
        let r = Live.record l (log l)
        let film = Live.film l r

        for _, st in r.Events do
            Assert.True(Chart.fitsIn TextMeasure.approx film.Shot (Live.view l st))

    /// A log that cannot be read is refused, saying why; an event it cannot read is named by
    /// index rather than skipped, because a replay with a step missing is another session.
    [<Fact>]
    let ``a bad log is refused by name`` () =
        let err (json: string) =
            match LiveLog.ofJson json with
            | Ok _ -> failwithf "read %s" json
            | Error e -> e

        Assert.Equal("not JSON", err "{\"format\":")
        Assert.Contains("not a Marey live log", err "[1,2,3]")
        Assert.Contains("version 2", err "{\"format\":\"marey.live-log\",\"version\":2,\"events\":[]}")
        Assert.Contains("event 1", err "{\"format\":\"marey.live-log\",\"version\":1,\"events\":[[0,\"set\",[0,1]],[1,\"set\",[0]]]}")

    /// The view stamps what it posts, by the page's clock, and reads no other.
    [<Fact>]
    let ``the view stamps every message`` () =
        let l = live ()
        let doc = Html.widget l (Live.init l) true
        Assert.Contains("ch.post(m.type, m.payload.concat([m.t]))", doc)
        Assert.Contains("performance.now()", doc)
        Assert.DoesNotContain("Date.now", doc)
        Assert.DoesNotContain("new Date", doc)
