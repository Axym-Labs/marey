/// M4.6 — *Surface*: units opt-in (D109), and the witness a caller did not state (D127).
///
/// `U.unspecified` is what an axis carries when nobody said what its numbers are. It is not
/// `U.one`: `one` claims the quantity has no dimension, which the axis-sharing check acts on, and
/// `unspecified` claims nothing. The tests below hold both halves of that sentence, because
/// implementing the first as "dimensionless skips the check" would pass every test of it and
/// break every test of the second.
module Marey.Tests.SurfaceTests

open Xunit
open Marey
open Marey.UnitsOfMeasure

let private extent = Chart.size 600.0 400.0

let private titles (fig: Figure) =
    Chart.toScene extent fig
    |> Scene.fold
        (fun acc n ->
            match n with
            | Text t -> t.Text :: acc
            | _ -> acc)
        []

let private ts = [| 0.0<s>; 1.0<s>; 2.0<s> |]

let private plain (ys: float[]) = Chart.line U.second U.unspecified ts ys
let private metres (ys: float<m>[]) = Chart.line U.second U.metre ts ys

module TheWitness =

    [<Fact>]
    let ``every declared witness is stated, and only unspecified is not`` () =
        Assert.True(U.metre.Stated)
        Assert.True(U.one.Stated)
        Assert.True(Derived.volt.Stated)
        Assert.False(U.unspecified.Stated)

    /// What is composed from an unstated unit is unstated: a density over numbers nobody named is
    /// a density over numbers nobody named, and `1/unspecified` must not acquire a claim.
    [<Fact>]
    let ``composition keeps an unstated side unstated`` () =
        Assert.False((U.recip U.unspecified).Stated)
        Assert.False((U.div U.unspecified U.second).Stated)
        Assert.False((U.mul U.metre U.unspecified).Stated)
        Assert.False((U.square U.unspecified).Stated)
        Assert.False((U.scaled "k" 1000.0 U.unspecified).Stated)
        Assert.True((U.div U.metre U.second).Stated)

    /// D128 — the relabel. Measures are erased, so a stated witness comes back as itself, and
    /// `null`, the omitted optional parameter, is the one place `unspecified` is decided.
    [<Fact>]
    let ``erase returns the witness it was given, and null is unspecified`` () =
        Assert.Same(U.metre :> obj, U.erase U.metre :> obj)
        Assert.Same(U.unspecified, U.erase null)

    [<Fact>]
    let ``erase copies every runtime field of a unit implemented elsewhere`` () =
        let foreign =
            { new IUnit with
                member _.Symbol = "°C"
                member _.Dim = Dim.temperature
                member _.Factors = []
                member _.ToSI = 1.0
                member _.OffsetSI = 273.15
                member _.Stated = true }

        let u = U.erase foreign
        Assert.Equal("°C", u.Symbol)
        Assert.Equal(Dim.temperature, u.Dim)
        Assert.Equal(273.15, u.OffsetSI)
        Assert.True(u.Stated)

/// Gate 1 — unspecified makes no claim.
module UnspecifiedMakesNoClaim =

    [<Fact>]
    let ``an unspecified layer shares an axis with metres, in either order, and the axis reads metres`` () =
        let under = metres [| 0.0<m>; 1.0<m>; 2.0<m> |] |> Chart.overlay (plain [| 0.5; 1.5; 2.5 |])
        let over = plain [| 0.5; 1.5; 2.5 |] |> Chart.overlay (metres [| 0.0<m>; 1.0<m>; 2.0<m> |])

        for fig in [ under; over ] do
            let ts = titles fig
            Assert.Contains("x [m]", ts)

    [<Fact>]
    let ``an unspecified panel shares a facet axis with a metre panel`` () =
        let fig = Chart.facet [| "plain", plain [| 0.0; 4.0; 1.0 |]; "metres", metres [| 0.0<m>; 1.0<m>; 2.0<m> |] |]
        Assert.Equal(2, fig.Panels.Length)

    [<Fact>]
    let ``an unspecified axis prints no bracket`` () =
        // The x axis is in seconds, so exactly one title carries a bracket.
        let ts = titles (plain [| 1200.0; 3400.0; 5600.0 |])
        Assert.Equal<string list>([ "t [s]" ], ts |> List.filter (fun t -> t.Contains "["))

    /// Being first does not make an unstated layer the reference. If it were, the check would
    /// compare metres and kelvin against it, find nothing to say, and let the two that *did*
    /// state a unit disagree on one axis.
    [<Fact>]
    let ``two stated layers still disagree when an unspecified one is drawn first`` () =
        let fig =
            plain [| 0.0; 1.0; 2.0 |]
            |> Chart.overlay (metres [| 0.0<m>; 1.0<m>; 2.0<m> |])
            |> Chart.overlay (Chart.line U.second U.kelvin ts [| 0.0<K>; 1.0<K>; 2.0<K> |])

        let ex = Assert.ThrowsAny<exn>(fun () -> Figure.unify fig.Panels.[0] |> ignore)
        Assert.Contains("layer 1", ex.Message)
        Assert.Contains("layer 2", ex.Message)
        Assert.Contains("Two physical dimensions cannot share an axis", ex.Message)

    [<Fact>]
    let ``a stated colour domain applies to an unspecified channel as the numbers given`` () =
        let fig =
            plain [| 0.0; 1.0; 2.0 |]
            |> Chart.colorBy U.unspecified [| 3.0; 4.0; 5.0 |]
            |> Chart.withColorDomain NonSi.degreeCelsius 0.0<degC> 10.0<degC>

        Chart.toScene extent fig |> ignore

/// Gate 2 — `U.one` still claims.
module OneStillClaims =

    [<Fact>]
    let ``a dimensionless layer over metres still raises`` () =
        let fig = metres [| 0.0<m>; 1.0<m>; 2.0<m> |] |> Chart.overlay (Chart.line U.second U.one ts [| 0.0; 1.0; 2.0 |])
        let ex = Assert.ThrowsAny<exn>(fun () -> Figure.unify fig.Panels.[0] |> ignore)
        Assert.Contains("Two physical dimensions cannot share an axis", ex.Message)

/// Gate 4 — one implementation. The unit-optional member and the curried witness form are two
/// ways of reaching the same chart, so with the same units stated they draw the same bytes.
module OneImplementation =

    let private svg (fig: Figure) = SvgBackend.render extent (Chart.toScene extent fig)

    let private same (optional: Figure) (curried: Figure) = Assert.Equal(svg curried, svg optional)

    let private raw (xs: float<'u>[]) = xs |> Array.map float

    let private t = [| 0.0<s>; 1.0<s>; 2.0<s>; 3.5<s>; 4.0<s> |]
    let private x = [| 0.3<m>; 1.1<m>; 0.7<m>; 2.4<m>; 1.9<m> |]
    let private err = [| 0.1<m>; 0.2<m>; 0.1<m>; 0.3<m>; 0.2<m> |]
    let private hi = x |> Array.map (fun v -> v + 0.5<m>)
    let private temps = [| 12.0<degC>; 14.5<degC>; 13.0<degC>; 17.0<degC>; 16.0<degC> |]
    let private sample = Array.init 200 (fun i -> float ((i * 37) % 101) * 0.01<m>)
    let private t' = raw t
    let private x' = raw x

    [<Fact>]
    let ``the two-axis constructors`` () =
        same (Marey.Chart.Line(t', x', xUnit = U.second, yUnit = U.metre)) (Chart.line U.second U.metre t x)
        same (Marey.Chart.Scatter(t', x', xUnit = U.second, yUnit = U.metre)) (Chart.scatter U.second U.metre t x)
        same (Marey.Chart.Area(t', x', xUnit = U.second, yUnit = U.metre)) (Chart.area U.second U.metre t x)
        same (Marey.Chart.Bar(t', x', xUnit = U.second, yUnit = U.metre)) (Chart.bar U.second U.metre t x)
        same (Marey.Chart.Errorbar(t', x', raw err, xUnit = U.second, yUnit = U.metre)) (Chart.errorbar U.second U.metre t x err)
        same (Marey.Chart.Step(t', x', xUnit = U.second, yUnit = U.metre)) (Chart.step U.second U.metre t x)

        same
            (Marey.Chart.StepWith(StepBefore, t', x', xUnit = U.second, yUnit = U.metre))
            (Chart.stepWith StepBefore U.second U.metre t x)

        same (Marey.Chart.Ribbon(t', x', raw hi, xUnit = U.second, yUnit = U.metre)) (Chart.ribbon U.second U.metre t x hi)
        same (Marey.Chart.Tile(0.5, 0.2, t', x', xUnit = U.second, yUnit = U.metre)) (Chart.tile U.second U.metre 0.5<s> 0.2<m> t x)

        same
            (Marey.Chart.Segment(t', x', raw t, raw hi, xUnit = U.second, yUnit = U.metre))
            (Chart.segment U.second U.metre t x t hi)

        let labels = [| "a"; "b"; "c"; "d"; "e" |]
        same (Marey.Chart.Text(t', x', labels, xUnit = U.second, yUnit = U.metre)) (Chart.text U.second U.metre t x labels)
        same (Marey.Chart.Fit(t', x', xUnit = U.second, yUnit = U.metre)) (Chart.fit U.second U.metre t x)
        same (Marey.Chart.Loess(t', x', xUnit = U.second, yUnit = U.metre)) (Chart.loess U.second U.metre t x)

        same
            (Marey.Chart.LoessWith(0.9, t', x', xUnit = U.second, yUnit = U.metre))
            (Chart.loessWith 0.9 U.second U.metre t x)

        same (Marey.Chart.Hexbin(x', t', xUnit = U.metre, yUnit = U.second)) (Chart.hexbin U.metre U.second x t)
        same (Marey.Chart.HexbinWith(4, x', t', xUnit = U.metre, yUnit = U.second)) (Chart.hexbinWith U.metre U.second 4 x t)

    [<Fact>]
    let ``an image`` () =
        let img = { Width = 2; Height = 1; Rgba = [| 255uy; 0uy; 0uy; 255uy; 0uy; 0uy; 255uy; 255uy |] }

        same
            (Marey.Chart.Image(img, 0.0, 0.0, 2.0, 1.0, false, xUnit = U.second, yUnit = U.metre))
            (Chart.image U.second U.metre img 0.0<s> 0.0<m> 2.0<s> 1.0<m> false)

    /// The stats derive a unit, and derive it from the witness's runtime fields — which is what
    /// D128's relabel has to preserve for a density over metres to read `m⁻¹`.
    [<Fact>]
    let ``the one-axis stats, and the unit a density derives`` () =
        let sample' = raw sample
        same (Marey.Chart.Histogram(sample', unit = U.metre)) (Chart.histogram U.metre sample)
        same (Marey.Chart.HistogramWith(7, sample', unit = U.metre)) (Chart.histogramWith U.metre 7 sample)
        same (Marey.Chart.Density(sample', unit = U.metre)) (Chart.density U.metre sample)
        same (Marey.Chart.Ecdf(sample', unit = U.metre)) (Chart.ecdf U.metre sample)
        Assert.Contains("m⁻¹", svg (Marey.Chart.Density(sample', unit = U.metre)))

    /// An affine unit carries its offset through the relabel, or a colour domain stated in kelvin
    /// would land 273.15 away.
    [<Fact>]
    let ``an affine unit, a heatmap, and the channels that take data`` () =
        same (Marey.Chart.Line(t', raw temps, xUnit = U.second, yUnit = NonSi.degreeCelsius)) (Chart.line U.second NonSi.degreeCelsius t temps)

        same
            (Marey.Chart.Heatmap(t', x', raw temps, xUnit = U.second, yUnit = U.metre, valueUnit = NonSi.degreeCelsius))
            (Chart.heatmap U.second U.metre NonSi.degreeCelsius t x temps)

        let optional () = Marey.Chart.Scatter(t', x', xUnit = U.second, yUnit = U.metre)
        let curried () = Chart.scatter U.second U.metre t x

        same
            (optional () |> Marey.Chart.ColorBy(raw temps, unit = NonSi.degreeCelsius) |> Marey.Chart.WithColorDomain(280.0, 300.0, unit = U.kelvin))
            (curried () |> Chart.colorBy NonSi.degreeCelsius temps |> Chart.withColorDomain U.kelvin 280.0<K> 300.0<K>)

        same
            (optional () |> Marey.Chart.ColorByWith(Palette.viridis, raw temps, unit = NonSi.degreeCelsius))
            (curried () |> Chart.colorByWith Palette.viridis NonSi.degreeCelsius temps)

        same (optional () |> Marey.Chart.SizeBy(raw err, unit = U.metre)) (curried () |> Chart.sizeBy U.metre err)

        same
            (optional () |> Marey.Chart.SizeByWith(2.0<px>, 9.0<px>, raw err, unit = U.metre))
            (curried () |> Chart.sizeByWith 2.0<px> 9.0<px> U.metre err)

    /// D125 — `add` is `addLine` and its siblings, for every constructor: the next palette slot
    /// of the figure being added to.
    [<Fact>]
    let ``add is the add family`` () =
        let fig () = Chart.line U.second U.metre t x
        let other = x |> Array.map (fun v -> v * 0.5)
        let other' = raw other
        let add (top: Figure) = fig () |> Chart.add top

        same (add (Marey.Chart.Line(t', other', xUnit = U.second, yUnit = U.metre))) (fig () |> Chart.addLine U.second U.metre t other)
        same (add (Marey.Chart.Scatter(t', other', xUnit = U.second, yUnit = U.metre))) (fig () |> Chart.addScatter U.second U.metre t other)
        same (add (Marey.Chart.Area(t', other', xUnit = U.second, yUnit = U.metre))) (fig () |> Chart.addArea U.second U.metre t other)
        same (add (Marey.Chart.Step(t', other', xUnit = U.second, yUnit = U.metre))) (fig () |> Chart.addStep U.second U.metre t other)

        same
            (add (Marey.Chart.Ribbon(t', other', x', xUnit = U.second, yUnit = U.metre)))
            (fig () |> Chart.addRibbon U.second U.metre t other x)

        same
            (add (Marey.Chart.Segment(t', other', t', x', xUnit = U.second, yUnit = U.metre)))
            (fig () |> Chart.addSegment U.second U.metre t other t x)

        same (add (Marey.Chart.Fit(t', x', xUnit = U.second, yUnit = U.metre))) (fig () |> Chart.addFit U.second U.metre t x)
        same (add (Marey.Chart.Loess(t', x', xUnit = U.second, yUnit = U.metre))) (fig () |> Chart.addLoess U.second U.metre t x)

        same
            (add (Marey.Chart.LoessWith(0.9, t', x', xUnit = U.second, yUnit = U.metre)))
            (fig () |> Chart.addLoessWith 0.9 U.second U.metre t x)

    /// `addErrorbar` adds the bars alone; `add` of an error-bar figure adds its bars and its
    /// points, in one colour, because that is the figure `Errorbar` builds.
    [<Fact>]
    let ``an added error-bar figure keeps its bars and points in one colour`` () =
        let fig = Chart.line U.second U.metre t x |> Chart.add (Marey.Chart.Errorbar(t', x', raw err, xUnit = U.second, yUnit = U.metre))
        let layers = fig.Panels.[0].Layers
        Assert.Equal(3, layers.Length)
        Assert.Equal(layers.[1].Style, layers.[2].Style)
        Assert.NotEqual(layers.[0].Style, layers.[1].Style)

    [<Fact>]
    let ``units left out are unspecified`` () =
        same (Marey.Chart.Line(t', x')) (Chart.line U.unspecified U.unspecified t' x')
        same (Marey.Chart.Line(t', x', yUnit = U.metre)) (Chart.line U.unspecified U.metre t' x)

/// Gate 5 — coverage. Every unit-optional member has a curried counterpart in
/// `Marey.UnitsOfMeasure`, mapped by name (`Line` → `line`), and an error-obsolete shadow there;
/// and every curried function there that takes a witness is either reached from a member or
/// listed here by name, with the reason. A constructor added to one mode only fails this.
module Coverage =

    open System
    open System.Reflection

    let private lower (n: string) = string (Char.ToLowerInvariant n.[0]) + n.Substring 1

    let private methodsOf (t: Type) =
        t.GetMethods(BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.DeclaredOnly)

    let private takesWitness (m: MethodInfo) =
        m.GetParameters() |> Array.exists (fun p -> p.ParameterType = typeof<U<1>>)

    /// Curried, and no member: the add family is `Chart.add` over a constructor in the
    /// unit-optional mode (D125).
    let private curriedOnly =
        set [ "addLine"; "addScatter"; "addArea"; "addErrorbar"; "addStep"; "addRibbon"; "addSegment"; "addFit"; "addLoess"; "addLoessWith" ]

    let private pairs: (Type * Type) list =
        let plot = typeof<Marey.Figure>.Assembly
        [ typeof<Marey.Chart>, plot.GetType("Marey.UnitsOfMeasure.Chart", true) ]

    [<Fact>]
    let ``every unit-optional member has a curried counterpart and a shadow`` () =
        for optional, required in pairs do
            let members = methodsOf optional |> Array.map (fun m -> m.Name) |> Array.distinct
            Assert.NotEmpty members
            let names = methodsOf required |> Array.map (fun m -> m.Name) |> set

            for n in members do
                Assert.True(names.Contains(lower n), sprintf "%s.%s has no curried %s in Marey.UnitsOfMeasure" optional.Name n (lower n))

                let shadow = methodsOf required |> Array.tryFind (fun m -> m.Name = n)

                match shadow |> Option.bind (fun m -> m.GetCustomAttribute<ObsoleteAttribute>() |> Option.ofObj) with
                | Some o -> Assert.True(o.IsError, sprintf "the shadow of %s.%s is a warning, not an error" optional.Name n)
                | None -> failwithf "%s.%s has no error-obsolete shadow in Marey.UnitsOfMeasure" optional.Name n

    [<Fact>]
    let ``every curried function that takes a witness is reached from a member or listed`` () =
        for optional, required in pairs do
            let members = methodsOf optional |> Array.map (fun m -> lower m.Name) |> set

            for m in methodsOf required |> Array.filter takesWitness do
                Assert.True(
                    members.Contains m.Name || curriedOnly.Contains m.Name,
                    sprintf "Marey.UnitsOfMeasure.%s.%s takes a witness and has no unit-optional member" required.Name m.Name
                )

/// Gate 6 — the shadow is an error. Compiled through `dotnet fsi`, as the interaction tests
/// compile theirs, because a compile error is the thing being tested and a test assembly that
/// contains one does not build.
module TheShadowIsAnError =

    open System
    open System.IO
    open System.Text

    let private compile (name: string) (opens: string list) (body: string) =
        let tool = Tools.dotnet.Force()

        if not tool.IsAvailable then
            None
        else
            let binDir = Path.GetDirectoryName(Uri(typeof<Marey.Figure>.Assembly.Location).LocalPath)
            let sb = StringBuilder()

            for dll in [ "Marey.Core"; "Marey.Units"; "Marey.Anim"; "Marey.Plot" ] do
                sb.AppendLine(sprintf "#r @\"%s\"" (Path.Combine(binDir, dll + ".dll"))) |> ignore

            for o in opens do
                sb.AppendLine("open " + o) |> ignore

            let path = Path.Combine(Path.GetTempPath(), sprintf "marey-surface-%s.fsx" name)
            File.WriteAllText(path, sb.ToString() + body + "\n", UTF8Encoding false)

            match Toolchain.run tool (sprintf "fsi --nologo \"%s\"" path) (Path.GetTempPath()) 600_000 with
            | ToolUnavailable _ -> None
            | ToolTimedOut why -> failwith why
            | ToolRan run -> Some(run.ExitCode, path, run.Out + "\n" + run.Error)

    let private unitOptional =
        "let fig = Chart.Line([| 0.0; 1.0 |], [| 2.0; 3.0 |], yUnit = U.metre) |> Chart.withTitle \"t\"\nprintfn \"%d\" fig.Panels.Length"

    let private skipped () = Console.WriteLine("SKIPPED: dotnet not found on PATH")

    /// The positive control: the same call under `open Marey` alone. Without it, a bad `#r` makes
    /// the negative green.
    [<Fact>]
    let ``a unit-optional call compiles under open Marey`` () =
        match compile "optional" [ "Marey" ] unitOptional with
        | None -> skipped ()
        | Some(code, path, output) -> Assert.True((code = 0), sprintf "did not compile: %s\n%s" path output)

    [<Fact>]
    let ``a unit-optional call does not compile under open Marey.UnitsOfMeasure`` () =
        match compile "required" [ "Marey"; "Marey.UnitsOfMeasure" ] unitOptional with
        | None -> skipped ()
        | Some(code, path, output) ->
            Assert.True(code <> 0, sprintf "Chart.Line compiled in the unit-required mode: %s" path)
            Assert.Contains("error FS0101", output)
            Assert.Contains("Chart.line", output)

    /// A data-taking channel is shadowed too, not only the constructors.
    [<Fact>]
    let ``a unit-optional channel does not compile under open Marey.UnitsOfMeasure`` () =
        let body =
            "let fig = Chart.line U.second U.metre [| 0.0<s> |] [| 1.0<m> |] |> Chart.ColorBy([| 1.0 |])"

        match compile "required-channel" [ "Marey"; "Marey.UnitsOfMeasure" ] body with
        | None -> skipped ()
        | Some(code, path, output) ->
            Assert.True(code <> 0, sprintf "Chart.ColorBy compiled in the unit-required mode: %s" path)
            Assert.Contains("error FS0101", output)

    /// The unit-required mode's own spelling compiles under its open, with the core's combinators.
    [<Fact>]
    let ``a curried call compiles under open Marey.UnitsOfMeasure`` () =
        let body =
            "let fig = Chart.line U.second U.metre [| 0.0<s> |] [| 1.0<m> |] |> Chart.withTitle \"t\"\nprintfn \"%d\" fig.Panels.Length"

        match compile "curried" [ "Marey"; "Marey.UnitsOfMeasure" ] body with
        | None -> skipped ()
        | Some(code, path, output) -> Assert.True((code = 0), sprintf "did not compile: %s\n%s" path output)

    /// And it is not reachable without the open: the curried constructors left the core.
    [<Fact>]
    let ``a curried call does not compile under open Marey alone`` () =
        let body = "let fig = Chart.line U.second U.metre [| 0.0<s> |] [| 1.0<m> |]"

        match compile "curried-core" [ "Marey" ] body with
        | None -> skipped ()
        | Some(code, path, _) -> Assert.True(code <> 0, sprintf "Chart.line was reachable without the open: %s" path)
