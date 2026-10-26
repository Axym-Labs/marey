/// M4 phase 1 — `Marey.Fields`: grids, sampling and the derived quantities. Gates 2, 3 and 4 of
/// `m4-plan.md`.
///
/// **The differences are exact on quadratics, on any rectilinear grid.** That is a stronger
/// claim than "close", and it is what the property tests hold them to: three-point differences
/// with the spacing written into the coefficients reproduce the derivative of a quadratic to
/// rounding, however unevenly the nodes are spaced. For anything smoother the error is O(h²),
/// and the convergence test measures the order rather than asserting a tolerance.
///
/// **The units are checked by compiling.** A binding annotated with the unit a derivative must
/// have is an assertion the compiler makes; the ones that must *not* compile go through `fsi`
/// and are asserted on its error.
module Marey.Tests.FieldTests

open System
open System.IO
open System.Text
open FsCheck.Xunit
open Xunit
open Marey
open Marey.UnitsOfMeasure

// ======================================================================================
// Fixtures
// ======================================================================================

/// A strictly increasing axis of `n` nodes from `seed`, with gaps between 0.2 and 1.2 — uneven,
/// so the tests exercise the spacing in the coefficients rather than a uniform special case.
let private unevenAxis (seed: uint64) (n: int) : float[] =
    let gaps, _ = Rng.units n (Rng.seeded seed)
    gaps |> Array.scan (fun acc g -> acc + 0.2 + g) (-3.0) |> Array.take n

let private close (tol: float) (expected: float) (actual: float) =
    abs (expected - actual) <= tol * (1.0 + abs expected)

/// A quadratic in two variables, `a x² + b x y + c y² + d x + e y + f`, from six small integers.
type private Quadratic =
    { A: float; B: float; C: float; D: float; E: float; F: float }

    member q.At (x: float) (y: float) = q.A * x * x + q.B * x * y + q.C * y * y + q.D * x + q.E * y + q.F
    member q.Dx (x: float) (y: float) = 2.0 * q.A * x + q.B * y + q.D
    member q.Dy (x: float) (y: float) = q.B * x + 2.0 * q.C * y + q.E

let private quadratic (cs: int[]) =
    let c i = if i < cs.Length then float (cs.[i] % 7) else 1.0

    { A = c 0; B = c 1; C = c 2; D = c 3; E = c 4; F = c 5 }

let private gridOf (seed: uint64) (q: Quadratic) =
    let xs = unevenAxis seed 7
    let ys = unevenAxis (seed + 1UL) 6
    ScalarGrid.sample xs ys (fun x y -> q.At x y)

// ======================================================================================
// Gate 2 — the derivatives' values
// ======================================================================================

module ``the differences are exact on quadratics`` =

    [<Property>]
    let ``grad`` (seed: uint64) (cs: int[]) =
        let q = quadratic cs
        let g = Field.grad (gridOf seed q)
        let nx = g.Xs.Length

        Array.forall id
            [| for k in 0 .. g.U.Length - 1 do
                   let x, y = g.Xs.[k % nx], g.Ys.[k / nx]
                   yield close 1e-9 (q.Dx x y) g.U.[k] && close 1e-9 (q.Dy x y) g.W.[k] |]

    /// `div (grad φ)` is exact too, because the first pass leaves a linear function and the
    /// second pass is exact on that.
    [<Property>]
    let ``the laplacian`` (seed: uint64) (cs: int[]) =
        let q = quadratic cs
        let l = Field.laplacian (gridOf seed q)
        l.Values |> Array.forall (close 1e-8 (2.0 * q.A + 2.0 * q.C))

    [<Property>]
    let ``the curl of a gradient is zero`` (seed: uint64) (cs: int[]) =
        let q = quadratic cs
        let c = Field.curl (Field.grad (gridOf seed q))
        c.Values |> Array.forall (fun v -> abs v < 1e-8 * (1.0 + abs q.B))

    [<Fact>]
    let ``the Jacobian's four entries`` () =
        // u = 3x + 2y, w = −x + 5y: constant partials, exact everywhere.
        let xs = unevenAxis 7UL 5
        let ys = unevenAxis 8UL 4
        let g = VectorGrid.sample xs ys (fun x y -> struct (3.0 * x + 2.0 * y, -x + 5.0 * y))
        let j = Field.jacobian g

        for v in j.DuDx.Values do Assert.Equal(3.0, v, 9)
        for v in j.DuDy.Values do Assert.Equal(2.0, v, 9)
        for v in j.DwDx.Values do Assert.Equal(-1.0, v, 9)
        for v in j.DwDy.Values do Assert.Equal(5.0, v, 9)

/// O(h²), measured as an order rather than assumed as a tolerance: halving the spacing divides
/// the worst error by about four, boundary nodes included.
[<Fact>]
let ``the error is second order in the spacing`` () =
    let worst n =
        let xs = Grid.linear 0.0 1.0 n
        let g = Field.grad (ScalarGrid.sample xs xs (fun x y -> sin (3.0 * x) * cos (2.0 * y)))
        let nx = g.Xs.Length

        Array.init g.U.Length (fun k ->
            let x, y = g.Xs.[k % nx], g.Ys.[k / nx]
            abs (g.U.[k] - 3.0 * cos (3.0 * x) * cos (2.0 * y)))
        |> Array.max

    let ratio = worst 17 / worst 33
    Assert.InRange(ratio, 3.5, 4.5)

// ======================================================================================
// Gate 2 — the derivatives' units, asserted by compiling
// ======================================================================================

module ``the units the types work out`` =

    /// §8.1's thesis in a test: the field of a potential in volts over metres is in V/m, and its
    /// divergence — the charge density over ε₀ — is in V/m². φ = r² · 1 V/m² gives E = −2r and
    /// ∇·E = −4 everywhere, exactly, because φ is quadratic.
    [<Fact>]
    let ``a potential's field and its divergence`` () =
        let xs = Grid.linear -1.0<m> 1.0<m> 9
        let phi: ScalarGrid<m, m, V> = Field.sampleScalar xs xs (fun p -> 1.0<V / m ^ 2> * Vec2.normSq p)
        let e: VectorGrid<m, m, V / m, V / m> = Field.grad phi |> fun g -> { g with U = Array.map (~-) g.U; W = Array.map (~-) g.W }
        let rho: ScalarGrid<m, m, V / m ^ 2> = Field.div e

        for v in rho.Values do
            Assert.Equal(-4.0, float v, 9)

    /// A pendulum's phase flow, on axes in different units. Its divergence is in 1/s and is
    /// zero — Liouville: a Hamiltonian flow preserves phase-space area.
    [<Fact>]
    let ``a pendulum's flow has a divergence in 1/s, and it is zero`` () =
        let g = 9.81<1 / s ^ 2>

        let pendulum: Flow<rad, rad / s, s> =
            fun th om -> struct (om, -g * sin (float th) * 1.0<rad>)

        let flow = Field.sampleFlow (Grid.linear -3.0<rad> 3.0<rad> 13) (Grid.linear -4.0<rad / s> 4.0<rad / s> 11) pendulum
        let d: ScalarGrid<rad, rad / s, 1 / s> = Field.div flow

        for v in d.Values do
            Assert.Equal(0.0, float v, 9)

    // ---- what must not compile -----------------------------------------------------------

    let private preamble () =
        let binDir = Path.GetDirectoryName(Uri(typeof<Marey.Figure>.Assembly.Location).LocalPath)
        let sb = StringBuilder()

        for name in [ "Marey.Core"; "Marey.Units"; "Marey.Plot"; "Marey.Fields" ] do
            sb.AppendLine(sprintf "#r @\"%s\"" (Path.Combine(binDir, name + ".dll"))) |> ignore

        sb.AppendLine("open Marey").ToString()

    let private compile (name: string) (body: string) =
        let tool = Tools.dotnet.Force()

        if not tool.IsAvailable then
            None
        else
            let path = Path.Combine(Path.GetTempPath(), sprintf "marey-field-%s.fsx" name)
            File.WriteAllText(path, preamble () + "\n" + body + "\n", UTF8Encoding false)

            match Toolchain.run tool (sprintf "fsi --nologo \"%s\"" path) (Path.GetTempPath()) 600_000 with
            | ToolUnavailable _ -> None
            | ToolTimedOut why -> failwith why
            | ToolRan run -> Some(run.ExitCode, path, run.Out + "\n" + run.Error)

    let private phaseSpace =
        String.concat
            "\n"
            [ "let flow: VectorGrid<rad, rad/s, rad/s, rad/s^2> ="
              "    VectorGrid.sample (Grid.linear -1.0<rad> 1.0<rad> 3) (Grid.linear -1.0<rad/s> 1.0<rad/s> 3) (fun th om -> struct (om, -1.0<1/s^2> * th))" ]

    /// The positive control: the same preamble and grid compile, and its divergence is fine.
    [<Fact>]
    let ``a phase space's divergence compiles`` () =
        match compile "div" (phaseSpace + "\nlet d: ScalarGrid<rad, rad/s, 1/s> = Field.div flow\nprintfn \"ok\"") with
        | None -> Console.WriteLine("SKIPPED: dotnet not found on PATH")
        | Some(code, path, output) -> Assert.True((code = 0), sprintf "the control did not compile: %s\n%s" path output)

    /// ∂ω̇/∂θ and ∂θ̇/∂ω are in different units, so a phase space has no curl, and saying so
    /// is a compile error rather than a number.
    [<Fact>]
    let ``a phase space's curl does not compile`` () =
        match compile "curl" (phaseSpace + "\nlet c = Field.curl flow") with
        | None -> Console.WriteLine("SKIPPED: dotnet not found on PATH")
        | Some(code, path, output) ->
            Assert.True(code <> 0, sprintf "a curl over phase space compiled: %s" path)
            Assert.Contains("error FS0001", output)

    [<Fact>]
    let ``a phase space's laplacian does not compile`` () =
        match compile "laplacian" (phaseSpace + "\nlet l = Field.laplacian (VectorGrid.u flow)") with
        | None -> Console.WriteLine("SKIPPED: dotnet not found on PATH")
        | Some(code, path, output) ->
            Assert.True(code <> 0, sprintf "a laplacian over phase space compiled: %s" path)
            Assert.Contains("error FS0001", output)

    /// **Gate 3, the rule's premise.** Every public function in `Marey.Fields` declares its
    /// measure parameters, because a function that infers them can narrow itself silently: this
    /// mixed-unit difference compiles, with `'x` and `'y` unified into one, and only a caller with
    /// two units finds out. Declared, the same body is a hard error in the library.
    [<Fact>]
    let ``declared measure parameters turn a silent narrowing into an error`` () =
        let inferred = "let diff (a: float<'x>) (b: float<'y>) = a - b\nprintfn \"compiled\""
        let declared = "let diff<[<Measure>] 'x, [<Measure>] 'y> (a: float<'x>) (b: float<'y>) = a - b"

        match compile "inferred" inferred, compile "declared" declared with
        | Some(ci, pi, oi), Some(cd, pd, od) ->
            Assert.True((ci = 0), sprintf "the inferred version was expected to narrow silently and compile: %s\n%s" pi oi)
            Assert.True(cd <> 0, sprintf "the declared version compiled: %s" pd)
            Assert.Contains("error FS0001", od)
        | _ -> Console.WriteLine("SKIPPED: dotnet not found on PATH")

// ======================================================================================
// Gate 4 — sampling in panel space, and values that are not values
// ======================================================================================

module ``sampling`` =

    /// A log axis's nodes are evenly spaced in log, so a field sampled for a log panel does not
    /// put half its nodes in the top decade.
    [<Fact>]
    let ``a log axis is uniform in log`` () =
        let xs = Grid.log 1.0<m> 1000.0<m> 7
        let steps = xs |> Array.pairwise |> Array.map (fun (a, b) -> log10 (float b) - log10 (float a))

        Assert.Equal(1.0<m>, xs.[0])
        Assert.Equal(1000.0<m>, xs.[6])

        for st in steps do
            Assert.Equal(0.5, st, 12)

    [<Fact>]
    let ``a linear axis ends exactly where it was asked to`` () =
        let xs = Grid.linear 0.1<m> 0.7<m> 7
        Assert.Equal(0.1<m>, xs.[0])
        Assert.Equal(0.7<m>, xs.[6])

    /// A point charge's potential is infinite at the charge. The node is kept, counted, and
    /// its neighbours' derivatives inherit it rather than being computed across it.
    [<Fact>]
    let ``a singular node is counted, and its neighbours' derivatives say so`` () =
        let xs = Grid.linear -1.0<m> 1.0<m> 5
        let phi = Field.sampleScalar xs xs (fun p -> 1.0<V * m> / Vec2.norm p)

        Assert.Equal(1, ScalarGrid.missing phi)

        let e = Field.grad phi
        Assert.True(VectorGrid.missing e > 1, "no derivative inherited the singularity")
        Assert.True(VectorGrid.missing e < e.U.Length, "every derivative inherited the singularity")

    [<Fact>]
    let ``a bilinear read is exact on a bilinear field and refuses to extrapolate`` () =
        let xs = Grid.linear 0.0<m> 2.0<m> 3
        let ys = Grid.linear 0.0<s> 1.0<s> 4
        let f (x: float<m>) (y: float<s>) = 1.0<K> + 2.0<K / m> * x + 3.0<K / s> * y + 4.0<K / (m * s)> * x * y
        let g = ScalarGrid.sample xs ys f

        for x, y in [ 0.0<m>, 0.0<s>; 0.3<m>, 0.7<s>; 1.9<m>, 0.05<s>; 2.0<m>, 1.0<s> ] do
            Assert.Equal(float (f x y), float (ScalarGrid.at g x y), 12)

        Assert.True(Double.IsNaN(float (ScalarGrid.at g 2.5<m> 0.5<s>)))

    [<Fact>]
    let ``a grid from data is checked`` () =
        Assert.Throws<ArgumentException>(fun () -> ScalarGrid.create [| 0.0; 1.0 |] [| 0.0; 0.0 |] [| 1.0; 2.0; 3.0; 4.0 |] |> ignore)
        |> ignore

        Assert.Throws<ArgumentException>(fun () -> ScalarGrid.create [| 0.0; 1.0 |] [| 0.0; 1.0 |] [| 1.0; 2.0; 3.0 |] |> ignore)
        |> ignore

// ======================================================================================
// Phase 2 — the heatmap, contours and bands. Gates 5, 6 and 7.
// ======================================================================================

/// A grid as a field layer's projected payload, with the panel taken as the data itself — the
/// geometry `FieldMarks` works in, without a figure around it.
let private onPanel (g: ScalarGrid<1, 1, 1>) : FieldPayload<nd> =
    { Xs = g.Xs |> Array.map (fun x -> x * 1.0<nd>)
      Ys = g.Ys |> Array.map (fun y -> y * 1.0<nd>)
      Values = g.Values
      U = [||]
      W = [||] }

module ``contours`` =

    /// **Gate 5, first half.** Every vertex is a crossing on a grid edge, interpolated along it,
    /// so the bilinear field everything else reads is exactly the level there — on an uneven grid
    /// and for a field that is not itself bilinear.
    [<Property>]
    let ``every vertex is on its level`` (seed: uint64) =
        let xs = unevenAxis seed 9
        let ys = unevenAxis (seed + 7UL) 8
        let g = ScalarGrid.sample xs ys (fun x y -> x * x + 2.0 * y * y + x * y)
        let level = 4.0

        FieldMarks.isolines level (onPanel g)
        |> Array.forall (fun (pts, _) ->
            pts |> Array.forall (fun p -> abs (ScalarGrid.at g (float p.X) (float p.Y) - level) < 1e-9))

    /// **Gate 5, the saddle.** One cell whose two high corners are joined if the cell's mean is
    /// the judge and separated if the bilinear saddle value is: corners 10 and 0.05 above zero,
    /// −1 and −1 below, mean 2.01, saddle −0.0415. The asymptotic decider separates them, so the
    /// level-zero contour is two segments, each cutting off one high corner.
    [<Fact>]
    let ``a saddle is decided by the interpolant, not the mean`` () =
        let g = ScalarGrid.create [| 0.0; 1.0 |] [| 0.0; 1.0 |] [| 10.0; -1.0; -1.0; 0.05 |]
        let lines = FieldMarks.isolines 0.0 (onPanel g)

        Assert.Equal(2, lines.Length)

        let nearCorner (cx: float) (cy: float) (pts: Vec2<nd>[]) =
            pts |> Array.forall (fun p -> abs (float p.X - cx) < 1.0 && abs (float p.Y - cy) < 1.0 && (float p.X = cx || float p.Y = cy))

        Assert.True(lines |> Array.exists (fun (pts, _) -> nearCorner 0.0 0.0 pts), "no segment cuts off the (0,0) corner")
        Assert.True(lines |> Array.exists (fun (pts, _) -> nearCorner 1.0 1.0 pts), "no segment cuts off the (1,1) corner")

    /// **Gate 6.** A level set that is closed is one closed path: the circle r² = 1 is one ring,
    /// not an arc per cell and not two halves that happen to meet.
    [<Fact>]
    let ``a closed level set is one closed ring`` () =
        let xs = Grid.linear -2.0 2.0 21
        let g = ScalarGrid.sample xs xs (fun x y -> x * x + y * y)

        match FieldMarks.isolines 1.0 (onPanel g) with
        | [| pts, closed |] ->
            Assert.True(closed, "the ring is open")
            Assert.True(pts.Length > 12, sprintf "the ring has only %d vertices" pts.Length)
        | other -> failwithf "expected one ring, got %d polylines" other.Length

    /// The orientation every band relies on: walking a contour, the region above its level is on
    /// the left. On the circle r² = 1 with the inside low, the walk is clockwise.
    [<Fact>]
    let ``a contour has the region above it on its left`` () =
        let xs = Grid.linear -2.0 2.0 21
        let g = ScalarGrid.sample xs xs (fun x y -> x * x + y * y)
        let pts, _ = (FieldMarks.isolines 1.0 (onPanel g)).[0]

        let signedArea =
            Array.append pts [| pts.[0] |]
            |> Array.pairwise
            |> Array.sumBy (fun (a, b) -> float a.X * float b.Y - float b.X * float a.Y)

        Assert.True(signedArea < 0.0, "the ring around a minimum runs counter-clockwise, so the low side is on its left")

    /// A missing node removes the cells around it from every contour, rather than a line being
    /// drawn through a value nobody measured.
    [<Fact>]
    let ``no contour crosses a cell with a missing corner`` () =
        let xs = Grid.linear -2.0 2.0 9
        let g = ScalarGrid.sample xs xs (fun x y -> if x = 0.0 && y = 0.0 then nan else x)

        for pts, _ in FieldMarks.isolines 0.25 (onPanel g) do
            for p in pts do
                Assert.False(abs (float p.X) < 0.5 && abs (float p.Y) < 0.5, sprintf "a vertex at (%f, %f) is in a cell with the missing node" (float p.X) (float p.Y))

module ``a field on a figure`` =

    let private extent = Chart.size 480.0 300.0

    let rec private find (pick: Node -> 'a option) (n: Node) : 'a list =
        let here = Option.toList (pick n)

        let below =
            match n with
            | Group ns -> ns |> Array.toList |> List.collect (find pick)
            | Transform(_, c)
            | Clip(_, c)
            | Keyed(_, c)
            | Annotated(_, c)
            | Opacity(_, c) -> find pick c
            | _ -> []

        here @ below

    /// **Gate 7.** The heatmap's pixels and the bands' fills are coloured by one call — the colour
    /// channel's own quantiser — so a band and a pixel holding its midpoint value are one colour.
    /// f = x with nodes at the band midpoints 0.5, 1.5 and 2.5.
    [<Fact>]
    let ``a band and a pixel at its midpoint are one colour`` () =
        let xs = [| 0.0<m>; 0.5<m>; 1.0<m>; 1.5<m>; 2.0<m>; 2.5<m>; 3.0<m> |]
        let ys = Grid.linear 0.0<m> 1.0<m> 3
        let g = ScalarGrid.sample xs ys (fun x _ -> float x * 1.0<K>)
        let levels = [| 0.0<K>; 1.0<K>; 2.0<K>; 3.0<K> |]

        let fig =
            Field.heatmapWith Theme.initial.Sequential false U.metre U.metre U.kelvin g
            |> Chart.overlay (Field.bandsWith Theme.initial.Sequential levels U.metre U.metre U.kelvin g)

        let scene = Chart.toScene extent fig

        let raster =
            find (function Raster r -> Some r.Image | _ -> None) scene |> List.exactlyOne

        let fills =
            find (function Path(_, None, Some(Solid c), EvenOdd) -> Some c | _ -> None) scene

        Assert.Equal(3, fills.Length)

        let byte01 (c: float) = byte (System.Math.Round(c * 255.0))

        for band, column in [ 0, 1; 1, 3; 2, 5 ] do
            let k = column * 4
            let pixel = raster.Rgba.[k .. k + 2]
            let fill = fills.[band]

            Assert.Equal<byte[]>(pixel, [| byte01 fill.R; byte01 fill.G; byte01 fill.B |])

    /// D97 for the field: a heatmap beside a wider layer is drawn where its grid is, not stretched
    /// over the panel. The image box covers the grid's half-padded extent, a fifth of the x axis.
    [<Fact>]
    let ``a field follows the panel's final scales`` () =
        let xs = Grid.linear 0.0<m> 1.0<m> 5
        let g = ScalarGrid.sample xs xs (fun x y -> float (x + y) * 1.0<K>)

        let fig =
            Field.heatmap U.metre U.metre U.kelvin g
            |> Chart.addScatterIn U.metre U.metre [| 0.0<m>; 5.0<m> |] [| 0.0<m>; 1.0<m> |]

        let box =
            find (function Raster r -> Some r.Bounds | _ -> None) (Chart.toScene extent fig) |> List.exactlyOne

        let width = float (box.X1 - box.X0)
        Assert.InRange(width, 0.15, 0.30)

    let private potential = GoldenScenes.Fields.potential

    /// Two line charges: their potential as a heatmap with its equipotentials. The SVG baseline
    /// carries the 13 × 9 image inline, which is the size it is chosen at.
    [<Fact>]
    let ``golden: a potential, its heatmap and its equipotentials (svg)`` () =
        Golden.check "figure-field-heatmap.svg" (Chart.render SvgBackend.render extent GoldenScenes.Fields.heatmapFigure)

    /// The same potential in bands, which is vector all the way down, so its TikZ baseline needs
    /// no sidecar and compiles with the other `.tex` baselines.
    [<Fact>]
    let ``golden: the same potential in bands (tikz)`` () =
        let fig =
            Field.bands U.metre U.metre U.volt potential
            |> Chart.overlay (Field.contours U.metre U.metre potential)
            |> Chart.withYLabel "y"
            |> Chart.withTitle "Two line charges"

        let doc = Chart.render TikzBackend.render extent fig
        Golden.check "figure-field-bands.tex" doc

        // Every band is an even-odd path with holes, which TikZ spells `even odd rule`; the
        // engine is the authority on whether that spelling, and everything else, is valid.
        match TexToolchain.compile "field-bands" (TikzBackend.standalone doc) with
        | TexToolchain.Unavailable -> Console.WriteLine("SKIPPED: " + TexToolchain.report ())
        | TexToolchain.Compiled -> ()
        | TexToolchain.Failed why -> failwithf "the bands baseline does not compile:\n%s" why

// ======================================================================================
// Phase 3 — the quiver. Gate 8.
// ======================================================================================

module ``arrows`` =

    let rec private nodes (n: Node) : Node list =
        n
        :: (match n with
            | Group ns -> ns |> Array.toList |> List.collect nodes
            | Transform(_, c)
            | Clip(_, c)
            | Keyed(_, c)
            | Opacity(_, c) -> nodes c
            | Annotated(a, _) when a.Label = "quiver key" -> []
            | Annotated(_, c) -> nodes c
            | _ -> [])

    let private keyOf (scene: Node) =
        let rec go (n: Node) =
            match n with
            | Annotated(a, c) when a.Label = "quiver key" -> Some c
            | Group ns -> ns |> Array.tryPick go
            | Transform(_, c)
            | Clip(_, c)
            | Keyed(_, c)
            | Opacity(_, c)
            | Annotated(_, c) -> go c
            | _ -> None

        go scene

    let private pt (seg: PathSeg) =
        match seg with
        | MoveTo p
        | LineTo p -> p
        | other -> failwithf "unexpected %A" other

    /// Every arrow as (tail, tip), in panel `nd`. An arrow is six segments: the shaft's move and
    /// line, then the head's move to the tip, two lines and a close.
    let private arrowsIn (n: Node) =
        [ for node in nodes n do
              match node with
              | Path(segs, Some _, Some _, NonZero) when segs.Length % 6 = 0 && segs.Length > 0 && (match segs.[5] with ClosePath -> true | _ -> false) ->
                  for k in 0 .. segs.Length / 6 - 1 do
                      yield pt segs.[6 * k], pt segs.[6 * k + 2]
              | _ -> () ]

    let private keyText (scene: Node) =
        match keyOf scene with
        | Some(Group parts) -> parts |> Array.pick (function Text t -> Some t.Text | _ -> None)
        | _ -> failwith "no quiver key"

    let private keyArrow (scene: Node) =
        match keyOf scene with
        | Some k -> arrowsIn k |> List.exactlyOne
        | None -> failwith "no quiver key"

    /// A uniform field of 3.7 kV/m along x: every arrow is the longest, horizontal.
    let private uniform =
        VectorGrid.sample (Grid.linear 0.0<m> 1.0<m> 6) (Grid.linear 0.0<m> 1.0<m> 5) (fun _ _ -> struct (3700.0<V / m>, 0.0<V / m>))

    let private fieldUnit = GoldenScenes.Fields.fieldUnit

    /// **Gate 8.** The key says what one arrow length means, and it is true: its arrow is to the
    /// longest arrow as its value is to the largest magnitude. The value is the largest 1, 2 or
    /// 5 times a power of ten not above 3.7 kV/m — 2 kV/m — and the label carries the prefix.
    [<Fact>]
    let ``the key is true, and labelled in the field's unit`` () =
        let scene = Chart.toScene (Chart.size 480.0 300.0) (Field.quiver U.metre U.metre fieldUnit uniform)
        let longest = arrowsIn scene |> List.map (fun (a, b) -> float (b.X - a.X)) |> List.max
        let key = keyArrow scene |> fun (a, b) -> float (b.X - a.X)

        Assert.Equal(2.0 / 3.7, key / longest, 9)
        Assert.Equal("2 kV·m⁻¹", keyText scene)

    /// The label is a property of the data and the arrow a property of the panel: at two sizes
    /// the label is the same and the ratio still holds.
    [<Fact>]
    let ``the key's label does not change with the figure's size`` () =
        for w, h in [ 320.0, 200.0; 960.0, 700.0 ] do
            let scene = Chart.toScene (Chart.size w h) (Field.quiver U.metre U.metre fieldUnit uniform)
            let longest = arrowsIn scene |> List.map (fun (a, b) -> float (b.X - a.X)) |> List.max
            let key = keyArrow scene |> fun (a, b) -> float (b.X - a.X)

            Assert.Equal("2 kV·m⁻¹", keyText scene)
            Assert.Equal(2.0 / 3.7, key / longest, 9)

    /// **M4.5 gate 1.** One quantity formatter. The key's label is `UnitDisplay.quantity`'s,
    /// which a control's label also uses, so the two cannot drift; and the same function without
    /// the prefix is `FixedPoint.describe`'s form.
    [<Fact>]
    let ``the key's label is the one quantity formatter's`` () =
        let scene = Chart.toScene (Chart.size 480.0 300.0) (Field.quiver U.metre U.metre fieldUnit uniform)
        let quantity prefixed = UnitDisplay.quantity Unicode UnitRegistry.si prefixed Ticks.formatGeneral fieldUnit

        Assert.Equal(quantity true 2000.0<_>, keyText scene)
        Assert.Equal("2000 V·m⁻¹", quantity false 2000.0<_>)

    /// An arrow is tangent to the field on the panel, a log axis included: its slope in `nd` is
    /// the components' ratio times the ratio of the scales' derivatives at its node.
    [<Fact>]
    let ``an arrow points along the field on a log axis`` () =
        let g = VectorGrid.sample (Grid.log 1.0<m> 100.0<m> 7) (Grid.linear 0.0<m> 50.0<m> 6) (fun x y -> struct (x * 1.0<1 / s>, (y + 10.0<m>) * 1.0<1 / s>))
        let fig = Field.quiver U.metre U.metre (U.div U.metre U.second) g |> Chart.withXLog
        let scene = Chart.toScene (Chart.size 480.0 300.0) fig
        let sx, sy = Erase.seriesScales (Figure.unify fig.Panels.[0]).Layers.[0].Data

        let slope (s: IScaleErased) (v: float) =
            let h = 1e-6 * max 1.0 (abs v)
            float (s.Project(v + h) - s.Project(v - h)) / (2.0 * h)

        let arrows = arrowsIn scene
        Assert.True(arrows.Length > 30, sprintf "only %d arrows were drawn" arrows.Length)

        // The arrow is built in pixels and stored in `nd`, and the plot area's two pixel scales
        // cancel out of its angle in `nd`, so the comparison needs neither. The bound is the
        // central difference `Geom.project` takes the derivative by: a thousandth of the node
        // spacing, so an error of order 1e-7 on a log axis. Stated as a distance.
        for tail, tip in arrows do
            let cx = sx.Invert((tail.X + tip.X) / 2.0)
            let cy = sy.Invert((tail.Y + tip.Y) / 2.0)
            let expected = atan2 ((cy + 10.0) * slope sy cy) (cx * slope sx cx)
            let actual = atan2 (float (tip.Y - tail.Y)) (float (tip.X - tail.X))
            Assert.True(abs (expected - actual) < 1e-5, sprintf "an arrow at (%g, %g) points at %g rad, the field at %g" cx cy actual expected)

    /// A direction field has no magnitude, so every arrow is one length and there is no key.
    [<Fact>]
    let ``a direction field's arrows are all one length, and it has no key`` () =
        let pendulum: Flow<rad, rad / s, s> = fun th om -> struct (om, -9.81<1 / s ^ 2> * sin (float th) * 1.0<rad>)
        let flow = Field.sampleFlow (Grid.linear -3.0<rad> 3.0<rad> 7) (Grid.linear -4.0<rad / s> 4.0<rad / s> 6) pendulum
        let scene = Chart.toScene (Chart.size 480.0 300.0) (Field.directions U.radian (U.div U.radian U.second) flow)

        Assert.True((keyOf scene).IsNone, "a direction field drew a key")

        // One length L in pixels on a plot area W × H means a·dx² + b·dy² = 1 for every arrow,
        // with a = (W/L)² and b = (H/L)², in `nd`. Two arrows of different slope fix a and b;
        // every other arrow has to agree. That needs neither the plot area's size nor L.
        let d =
            arrowsIn scene |> List.map (fun (t, p) -> float (p.X - t.X), float (p.Y - t.Y)) |> List.toArray

        Assert.True(d.Length >= 40, sprintf "only %d arrows were drawn" d.Length)

        let flat = d |> Array.maxBy (fun (x, y) -> abs x - abs y)
        let steep = d |> Array.maxBy (fun (x, y) -> abs y - abs x)
        let (x1, y1), (x2, y2) = flat, steep
        let det = x1 * x1 * y2 * y2 - x2 * x2 * y1 * y1
        let a = (y2 * y2 - y1 * y1) / det
        let b = (x1 * x1 - x2 * x2) / det

        for x, y in d do
            Assert.True(abs (a * x * x + b * y * y - 1.0) < 1e-9, sprintf "an arrow (%g, %g) is not the common length" x y)

    let private chargePair = GoldenScenes.Fields.chargePair

    let private eField = GoldenScenes.Fields.eField

    let private quiverFigure = GoldenScenes.Fields.quiverFigure

    [<Fact>]
    let ``golden: a quiver coloured by magnitude, with its key (svg)`` () =
        Golden.check "figure-field-quiver.svg" (Chart.render SvgBackend.render (Chart.size 480.0 300.0) quiverFigure)

    /// The key's label is plain Unicode — `V·m⁻¹` — so the TikZ baseline is also the check that
    /// a text run carrying a middle dot and superscripts survives the engine.
    [<Fact>]
    let ``golden: the same quiver (tikz)`` () =
        let doc = Chart.render TikzBackend.render (Chart.size 480.0 300.0) quiverFigure
        Golden.check "figure-field-quiver.tex" doc

        match TexToolchain.compile "field-quiver" (TikzBackend.standalone doc) with
        | TexToolchain.Unavailable -> Console.WriteLine("SKIPPED: " + TexToolchain.report ())
        | TexToolchain.Compiled -> ()
        | TexToolchain.Failed why -> failwithf "the quiver baseline does not compile:\n%s" why

// ======================================================================================
// Phase 4 — streamlines. Gates 9 and 10.
// ======================================================================================

module ``streamlines`` =

    let private plot: Extent<px> = { Width = 400.0<px>; Height = 250.0<px> }

    /// A vector field as a projected payload over the unit panel, components already in `nd`.
    /// Linear fields only below: bilinear interpolation reproduces them exactly, so what a test
    /// measures is the integrator rather than the interpolation.
    let private payload (n: int) (f: float -> float -> float * float) : FieldPayload<nd> =
        let cs = Grid.linear 0.0 1.0 n

        let uw =
            [| for j in 0 .. n - 1 do
                   for i in 0 .. n - 1 -> f cs.[i] cs.[j] |]

        { Xs = cs |> Array.map (fun c -> c * 1.0<nd>)
          Ys = cs |> Array.map (fun c -> c * 1.0<nd>)
          Values = Array.create (n * n) 1.0
          U = uw |> Array.map fst
          W = uw |> Array.map snd }

    /// A rotation about the panel's centre: every streamline is a closed orbit.
    let private rotation = payload 11 (fun x y -> -(y - 0.5), x - 0.5)

    let private lines = lazy (FieldMarks.streamlinePolylines plot 16.0<px> rotation)

    /// **Gate 9.** Every chord of every line points along the field at its midpoint, to within
    /// a thousandth of a radian — compared on the page, where the field's direction is
    /// (u·W, w·H).
    [<Fact>]
    let ``every chord is tangent to the field`` () =
        let ls = lines.Force()
        Assert.True(ls.Length > 10, sprintf "only %d lines" ls.Length)

        for pts, _ in ls do
            for k in 1 .. pts.Length - 1 do
                let struct (ax, ay) = pts.[k - 1]
                let struct (bx, by) = pts.[k]
                let mx, my = (ax + bx) / 2.0 / 400.0, (ay + by) / 2.0 / 250.0
                let expected = atan2 ((mx - 0.5) * 250.0) (-(my - 0.5) * 400.0)
                let actual = atan2 (by - ay) (bx - ax)
                let diff = abs (atan2 (sin (expected - actual)) (cos (expected - actual)))
                Assert.True(diff < 1e-3, sprintf "a chord at (%g, %g) is %g rad off the field" mx my diff)

    /// **Gate 10.** No two points on different lines are closer than `dTest`, half the separation.
    ///
    /// Over fields whose lines **converge** — a sink and a saddle. A rotation's orbits are
    /// concentric and never approach each other, so the separation test is never what stops one,
    /// and the first version of this gate passed with that test halved.
    [<Theory>]
    [<InlineData("sink")>]
    [<InlineData("saddle")>]
    let ``no two lines come closer than half the separation`` (shape: string) =
        let field =
            match shape with
            | "sink" -> payload 11 (fun x y -> -(x - 0.5), -(y - 0.5))
            | _ -> payload 11 (fun x y -> x - 0.5, -(y - 0.5))

        let ls = FieldMarks.streamlinePolylines plot 16.0<px> field
        Assert.True(ls.Length > 10, sprintf "only %d lines" ls.Length)

        let tagged =
            ls |> Array.mapi (fun l (pts, _) -> pts |> Array.map (fun (struct (x, y)) -> l, x, y)) |> Array.concat

        let mutable closest = infinity

        for a in 0 .. tagged.Length - 1 do
            let la, xa, ya = tagged.[a]

            for b in a + 1 .. tagged.Length - 1 do
                let lb, xb, yb = tagged.[b]

                if la <> lb then
                    closest <- min closest (sqrt ((xa - xb) ** 2.0 + (ya - yb) ** 2.0))

        Assert.True(closest >= 8.0 * (1.0 - 1e-9), sprintf "two lines come within %g px" closest)

    /// A centre's orbits close: a line that comes back to where it started is a ring, and its
    /// last point is within a step and a half of its first.
    [<Fact>]
    let ``a centre's orbits are closed rings`` () =
        let closed = lines.Force() |> Array.filter snd
        Assert.True(closed.Length >= 3, sprintf "only %d closed orbits" closed.Length)

        for pts, _ in closed do
            let struct (ax, ay) = pts.[0]
            let struct (bx, by) = pts.[pts.Length - 1]
            // Closed when a step lands within 1.5 steps (3 px at this separation) of the seed.
            Assert.True(sqrt ((ax - bx) ** 2.0 + (ay - by) ** 2.0) < 3.0, "an orbit's ends are not together")

    /// No randomness anywhere: two runs give the same lines, point for point (gate 11's half).
    [<Fact>]
    let ``the same field gives the same lines`` () =
        let again = FieldMarks.streamlinePolylines plot 16.0<px> rotation
        Assert.Equal(lines.Force().Length, again.Length)

        for (a, ca), (b, cb) in Array.zip (lines.Force()) again do
            Assert.Equal(ca, cb)
            Assert.True((a = b), "a line differs between two runs")

    /// The payload as a zoom by `k` about the panel's centre projects it: the nodes `k` times as
    /// far from the centre, and the components `k` times as long, as `Geom.project` scales them.
    let private zoomed (k: float) (f: FieldPayload<nd>) : FieldPayload<nd> =
        let out (c: float<nd>) = (c - 0.5<nd>) * k + 0.5<nd>

        { f with
            Xs = f.Xs |> Array.map out
            Ys = f.Ys |> Array.map out
            U = f.U |> Array.map (fun u -> u * k)
            W = f.W |> Array.map (fun w -> w * k) }

    let private points (ls: (struct (float * float)[] * bool)[]) = ls |> Array.sumBy (fun (pts, _) -> pts.Length)

    /// Zoomed in, only the grid's part in the plot area is traced, so the lines cost what the
    /// plot area holds rather than what the zoom puts past it: at 10 and 100 times no more points
    /// than twice the unzoomed field's, where tracing the whole grid made 100 and 10⁴ times as
    /// many. Every point is on the plot area, and every chord is still tangent to the field.
    [<Theory>]
    [<InlineData(10.0)>]
    [<InlineData(100.0)>]
    let ``a zoomed field traces only the plot area's lines`` (k: float) =
        let unzoomed = points (lines.Force())
        let clock = Diagnostics.Stopwatch.StartNew()
        let ls = FieldMarks.streamlinePolylines plot 16.0<px> (zoomed k rotation)
        let took = clock.Elapsed.TotalSeconds

        Assert.True(ls.Length > 10, sprintf "only %d lines" ls.Length)
        Assert.True(points ls <= 2 * unzoomed, sprintf "%d points zoomed %g times, %d unzoomed" (points ls) k unzoomed)
        Assert.True(took < 1.0, sprintf "%g s zoomed %g times" took k)

        for pts, _ in ls do
            for struct (x, y) in pts do
                // Within a step of it: the last step before an edge can end a little past it.
                Assert.True(x >= -1.0 && x <= 401.0 && y >= -1.0 && y <= 251.0, sprintf "a point at (%g, %g) is off the plot area" x y)

            for j in 1 .. pts.Length - 1 do
                let struct (ax, ay) = pts.[j - 1]
                let struct (bx, by) = pts.[j]
                let mx, my = (ax + bx) / 2.0 / 400.0, (ay + by) / 2.0 / 250.0
                let expected = atan2 ((mx - 0.5) * 250.0) (-(my - 0.5) * 400.0)
                let actual = atan2 (by - ay) (bx - ax)
                let diff = abs (atan2 (sin (expected - actual)) (cos (expected - actual)))
                Assert.True(diff < 1e-3, sprintf "a chord at (%g, %g) is %g rad off the field" mx my diff)

    /// The plot area's edge cuts the grid's cells exactly: a missing node's cells stay empty, and
    /// a whole cell beside them, cut by the edge on the row the missing node's cell ends at, is
    /// still traced. Interpolated across that row instead of along it, the cut would take the
    /// missing node's NaN into the whole cell.
    [<Fact>]
    let ``the plot area's edge cuts a cell beside a missing value exactly`` () =
        let big: Extent<px> = { Width = 800.0<px>; Height = 800.0<px> }
        let field = payload 11 (fun _ _ -> 0.0, 1.0)
        // The node at x = 0.3, y = 0.5 is missing; the grid moved 0.35 left puts the plot area's
        // left edge at x = 0.35 of the grid, inside the cells either side of it.
        let missing = 5 * 11 + 3

        let cut =
            { field with
                Xs = field.Xs |> Array.map (fun x -> x - 0.35<nd>)
                U = field.U |> Array.mapi (fun k u -> if k = missing then nan else u)
                W = field.W |> Array.mapi (fun k w -> if k = missing then nan else w) }

        let pts = FieldMarks.streamlinePolylines big 6.0<px> cut |> Array.collect fst
        let within (x0, x1) (y0, y1) = pts |> Array.exists (fun (struct (x, y)) -> x > x0 && x < x1 && y > y0 && y < y1)

        // The missing node's cells: x in [0.2, 0.4] of the grid, y in [0.4, 0.6]; on the page,
        // x in [0, 0.05] of the plot area. A line ends at the cells' edge, so their inside stays empty.
        Assert.False(within (1.0, 39.0) (0.4 * 800.0 + 1.0, 0.6 * 800.0 - 1.0), "a line crosses a missing node's cell")
        // The whole cell below them, y in [0.3, 0.4], cut by the plot area's left edge.
        Assert.True(within (0.0, 40.0) (0.3 * 800.0, 0.4 * 800.0), "the cut cell beside the missing one has no line")

    /// A grid moved wholly off the plot area draws no line, and no texture.
    [<Fact>]
    let ``a grid off the plot area draws nothing`` () =
        let away = { rotation with Xs = rotation.Xs |> Array.map (fun x -> x + 2.0<nd>) }
        Assert.Empty(FieldMarks.streamlinePolylines plot 16.0<px> away)
        Assert.True((FieldMarks.licImage TextMeasure.approx plot (fun _ -> Some Color.white) Field.licDefaults away).IsNone)

    /// On a log axis the lines are still the field's: a radial field u = x, w = y is a fan of
    /// straight lines through the origin in data space, and on a log-x panel each chord points
    /// along the field's direction there, J·v.
    [<Fact>]
    let ``a streamline follows the field on a log axis`` () =
        let g = VectorGrid.sample (Grid.log 1.0<m> 100.0<m> 9) (Grid.linear 1.0<m> 50.0<m> 8) (fun x y -> struct (x * 1.0<1 / s>, y * 1.0<1 / s>))
        let fig = Field.streamlines U.metre U.metre g |> Chart.withXLog
        let scene = Chart.toScene (Chart.size 480.0 300.0) fig
        let sx, sy = Erase.seriesScales (Figure.unify fig.Panels.[0]).Layers.[0].Data

        let slope (s: IScaleErased) (v: float) =
            let h = 1e-6 * max 1.0 (abs v)
            float (s.Project(v + h) - s.Project(v - h)) / (2.0 * h)

        let rec strokes (n: Node) =
            match n with
            | Path(segs, Some _, None, _) -> [ segs ]
            | Group ns -> ns |> Array.toList |> List.collect strokes
            | Transform(_, c)
            | Clip(_, c)
            | Keyed(_, c)
            | Annotated(_, c)
            | Opacity(_, c) -> strokes c
            | _ -> []

        // The field layer's strokes only: the axes and gridlines are stroked paths too.
        let layer =
            match Scene.tryFindKey (Keys.layer (Keys.panel 0 0) 0) scene with
            | Some n -> n
            | None -> failwith "the field layer has no node"

        let points =
            strokes layer
            |> List.collect (fun segs ->
                segs
                |> Array.toList
                |> List.choose (function
                    | MoveTo p -> Some(true, p)
                    | LineTo p -> Some(false, p)
                    | _ -> None))

        let chords =
            points |> List.pairwise |> List.choose (fun ((_, a), (start, b)) -> if start then None else Some(a, b))

        Assert.True(chords.Length > 100, sprintf "only %d chords" chords.Length)

        for a, b in chords do
            let cx = sx.Invert((a.X + b.X) / 2.0)
            let cy = sy.Invert((a.Y + b.Y) / 2.0)
            let expected = atan2 (cy * slope sy cy) (cx * slope sx cx)
            let actual = atan2 (float (b.Y - a.Y)) (float (b.X - a.X))
            let diff = abs (atan2 (sin (expected - actual)) (cos (expected - actual)))
            Assert.True(diff < 1e-3, sprintf "a chord at (%g, %g) is %g rad off the field" cx cy diff)

    let private pendulum = GoldenScenes.Fields.streamlinePendulum

    let private portrait = GoldenScenes.Fields.streamlinePortrait

    [<Fact>]
    let ``golden: a pendulum's phase portrait (svg)`` () =
        Golden.check "figure-field-streamlines.svg" (Chart.render SvgBackend.render (Chart.size 480.0 300.0) portrait)

    [<Fact>]
    let ``golden: the same portrait (tikz)`` () =
        let doc = Chart.render TikzBackend.render (Chart.size 480.0 300.0) portrait
        Golden.check "figure-field-streamlines.tex" doc

        match TexToolchain.compile "field-streamlines" (TikzBackend.standalone doc) with
        | TexToolchain.Unavailable -> Console.WriteLine("SKIPPED: " + TexToolchain.report ())
        | TexToolchain.Compiled -> ()
        | TexToolchain.Failed why -> failwithf "the streamlines baseline does not compile:\n%s" why

// ======================================================================================
// The layer's opacity on a field mark with no colour channel.
// ======================================================================================

module ``opacity without a colour channel`` =

    let rec private collect (pick: Node -> 'a list) (n: Node) : 'a list =
        let below =
            match n with
            | Group ns -> ns |> Array.toList |> List.collect (collect pick)
            | Transform(_, c)
            | Clip(_, c)
            | Keyed(_, c)
            | Annotated(_, c)
            | Opacity(_, c) -> collect pick c
            | _ -> []

        pick n @ below

    /// A colour nothing else on the figure is drawn in, so every paint in it is the field's.
    let private ink = Color.rgb 0.8 0.2 0.6

    let private rotation =
        let cs = Grid.linear -1.0<m> 1.0<m> 9
        VectorGrid.sample cs cs (fun x y -> struct (-(float y) * 1.0<V / m>, float x * 1.0<V / m>))

    let private figure (mark: string) : Figure =
        match mark with
        | "streamlines" -> Field.streamlines U.metre U.metre rotation
        | "quiver" -> Field.quiver U.metre U.metre GoldenScenes.Fields.fieldUnit rotation
        | "directions" -> Field.directions U.metre U.metre rotation
        | "contours" -> Field.contours U.metre U.metre GoldenScenes.Fields.potential
        | _ -> Field.lic U.metre U.metre rotation

    /// `Field.streamlines … |> Chart.withOpacity 0.3` once drew at full strength: the stroke
    /// carried the opacity, but every segment was painted in the layer's colour without it.
    [<Theory>]
    [<InlineData("streamlines")>]
    [<InlineData("quiver")>]
    [<InlineData("directions")>]
    [<InlineData("contours")>]
    [<InlineData("lic")>]
    let ``the layer's opacity reaches every mark`` (mark: string) =
        let scene =
            figure mark
            |> Chart.withStyle (fun st -> { st with Color = ink })
            |> Chart.withOpacity 0.3
            |> Chart.toScene (Chart.size 480.0 300.0)

        let alphas =
            scene
            |> collect (function
                | Path(_, stroke, fill, _) ->
                    [ match stroke with
                      | Some { Paint = Solid c } when (c.R, c.G, c.B) = (ink.R, ink.G, ink.B) -> yield c.A
                      | _ -> ()
                      match fill with
                      | Some(Solid c) when (c.R, c.G, c.B) = (ink.R, ink.G, ink.B) -> yield c.A
                      | _ -> () ]
                | Raster r ->
                    [ for k in 3..4 .. r.Image.Rgba.Length - 1 do
                          if r.Image.Rgba.[k] > 0uy then
                              yield float r.Image.Rgba.[k] / 255.0 ]
                | _ -> [])

        Assert.NotEmpty alphas

        for a in alphas do
            Assert.InRange(a, 0.29, 0.31)

// ======================================================================================
// Phase 5 — line-integral convolution. Gate 11, and the PNG baseline.
// ======================================================================================

module ``line-integral convolution`` =

    let private plot: Extent<px> = { Width = 200.0<px>; Height = 120.0<px> }

    /// A uniform field over the unit panel, components already in `nd`.
    let private uniform (u: float) (w: float) : FieldPayload<nd> =
        let cs = Grid.linear 0.0 1.0 5

        { Xs = cs |> Array.map (fun c -> c * 1.0<nd>)
          Ys = cs |> Array.map (fun c -> c * 1.0<nd>)
          Values = Array.create 25 1.0
          U = Array.create 25 u
          W = Array.create 25 w }

    /// A rotation about the panel's centre, on a grid fine enough to curve.
    let private rotation: FieldPayload<nd> =
        let cs = Grid.linear 0.0 1.0 21

        { Xs = cs |> Array.map (fun c -> c * 1.0<nd>)
          Ys = cs |> Array.map (fun c -> c * 1.0<nd>)
          Values = Array.create (21 * 21) 1.0
          U = [| for j in 0..20 do for _ in 0..20 -> -(cs.[j] - 0.5) |]
          W = [| for _ in 0..20 do for i in 0..20 -> cs.[i] - 0.5 |] }

    let private white (v: float) = if Double.IsNaN v then None else Some Color.white

    let private image (options: LicOptions) (f: FieldPayload<nd>) =
        match FieldMarks.licImage TextMeasure.approx plot white options f with
        | Some(img, _) -> img
        | None -> failwith "no image"

    /// The red channel, as brightness, at raster column `i` and row `j` counted from the top.
    let private bright (img: Image) (i: int) (j: int) = float img.Rgba.[(j * img.Width + i) * 4]

    /// Mean absolute difference between neighbours one pixel apart along x and along y, over
    /// the interior, clear of the kernel's reach from the edges.
    let private roughness (img: Image) =
        let mutable dx, dy, n = 0.0, 0.0, 0

        for j in 25 .. img.Height - 26 do
            for i in 25 .. img.Width - 26 do
                dx <- dx + abs (bright img (i + 1) j - bright img i j)
                dy <- dy + abs (bright img i (j + 1) - bright img i j)
                n <- n + 1

        dx / float n, dy / float n

    /// The convolution runs **along** the field: in a horizontal flow neighbours along a row
    /// average nearly the same noise and neighbours across rows do not, and in a vertical flow
    /// the other way round. A transposed raster, swapped components or a kernel convolved across
    /// the flow instead of along it all fail one of the two.
    [<Theory>]
    [<InlineData(true)>]
    [<InlineData(false)>]
    let ``the texture is smooth along the flow and rough across it`` (horizontal: bool) =
        let img = image Field.licDefaults (if horizontal then uniform 1.0 0.0 else uniform 0.0 1.0)
        let along, across = if horizontal then roughness img else (fun (a, b) -> b, a) (roughness img)
        Assert.True(across > 10.0, sprintf "the texture has no grain across the flow (%g)" across)
        Assert.True(along < 0.25 * across, sprintf "along the flow %g, across it %g" along across)

    /// The raster is the plot area's pixels over the grid's extent, times the oversample.
    [<Fact>]
    let ``the raster is at the panel's resolution times the oversample`` () =
        let one = image Field.licDefaults rotation
        Assert.Equal((200, 120), (one.Width, one.Height))

        let three = image { Field.licDefaults with Oversample = 3 } rotation
        Assert.Equal((600, 360), (three.Width, three.Height))

    /// Zoomed in, the raster covers only the grid's part in the plot area: the plot area's
    /// pixels, not the 10⁴ times as many the whole grid would take at 100 times.
    [<Fact>]
    let ``a zoomed field is rastered over the plot area only`` () =
        let k = 100.0
        let out (c: float<nd>) = (c - 0.5<nd>) * k + 0.5<nd>

        let zoomed =
            { rotation with
                Xs = rotation.Xs |> Array.map out
                Ys = rotation.Ys |> Array.map out
                U = rotation.U |> Array.map (fun u -> u * k)
                W = rotation.W |> Array.map (fun w -> w * k) }

        match FieldMarks.licImage TextMeasure.approx plot white Field.licDefaults zoomed with
        | Some(img, bounds) ->
            Assert.Equal((200, 120), (img.Width, img.Height))
            Assert.Equal((0.0, 0.0, 1.0, 1.0), (float bounds.X0, float bounds.Y0, float bounds.X1, float bounds.Y1))
        | None -> failwith "no image"

    /// **Gate 11.** A seed, a field and a plot size name one image: two renders are identical
    /// byte for byte, so are four computed at once on the thread pool, and a different seed
    /// draws a different one.
    [<Fact>]
    let ``the same seed draws the same image, and another seed does not`` () =
        let first = image Field.licDefaults rotation
        let again = image Field.licDefaults rotation
        Assert.Equal<byte[]>(first.Rgba, again.Rgba)

        let parallel' = Array.Parallel.init 4 (fun _ -> image Field.licDefaults rotation)

        for img in parallel' do
            Assert.Equal<byte[]>(first.Rgba, img.Rgba)

        let other = image { Field.licDefaults with Seed = 1UL } rotation
        Assert.False((first.Rgba = other.Rgba), "seed 1 drew seed 0's image")

    /// A missing value is not drawn: its pixels are transparent and the rest are opaque.
    [<Fact>]
    let ``a missing region is transparent`` () =
        let f = uniform 1.0 0.0
        // The left column of nodes is missing, so the left quarter of cells cannot be read.
        let gone = Array.init 25 (fun k -> if k % 5 = 0 then nan else 1.0)
        let img = image Field.licDefaults { f with U = Array.map2 (fun g u -> g * u) gone f.U }
        let alpha i j = img.Rgba.[(j * img.Width + i) * 4 + 3]

        Assert.Equal(0uy, alpha 10 60)
        Assert.Equal(255uy, alpha 150 60)

    /// The raster is the right way up: with the bottom row of nodes missing, the transparent
    /// pixels are the image's last rows, because image rows run top first and `nd` is y-up.
    [<Fact>]
    let ``the raster is the right way up`` () =
        let f = uniform 1.0 0.0
        let gone = Array.init 25 (fun k -> if k < 5 then nan else 1.0)
        let img = image Field.licDefaults { f with U = Array.map2 (fun g u -> g * u) gone f.U }
        let alpha i j = img.Rgba.[(j * img.Width + i) * 4 + 3]

        Assert.Equal(255uy, alpha 100 10)
        Assert.Equal(0uy, alpha 100 (img.Height - 10))

    [<Fact>]
    let ``an oversample or a kernel that means nothing is refused`` () =
        let g = VectorGrid.sample (Grid.linear 0.0<m> 1.0<m> 3) (Grid.linear 0.0<m> 1.0<m> 3) (fun _ _ -> struct (1.0<m / s>, 0.0<m / s>))

        Assert.Throws<ArgumentException>(fun () -> Field.licWith { Field.licDefaults with Oversample = 0 } U.metre U.metre g |> ignore)
        |> ignore

        Assert.Throws<ArgumentException>(fun () -> Field.licWith { Field.licDefaults with Length = 0.0<px> } U.metre U.metre g |> ignore)
        |> ignore

    let private pendulum =
        let flow: Flow<rad, rad / s, s> = fun th om -> struct (om, -9.81<1 / s ^ 2> * sin (float th) * 1.0<rad>)
        Field.sampleFlow (Grid.linear -6.5<rad> 6.5<rad> 53) (Grid.linear -9.0<rad / s> 9.0<rad / s> 37) flow

    /// Small and untitled, because the sidecar is noise and the encoder stores it uncompressed.
    let private portrait = Field.lic U.radian (U.div U.radian U.second) pendulum

    /// **The PNG baseline.** The `.tex` and the sidecar it names, as D64's image pair is: the
    /// bytes come from Marey's own encoder over pixels computed from a stated seed, so nothing
    /// outside the repository can move them, and the pair is compiled by a real engine.
    [<Fact>]
    let ``golden: a pendulum's phase portrait by LIC (tikz and png)`` () =
        let rendered = Chart.render TikzBackend.renderWithAssets (Chart.size 280.0 190.0) portrait
        Golden.check "figure-field-lic.tex" rendered.Document

        let asset = Assert.Single rendered.Assets
        Assert.Contains(asset.Name, rendered.Document)
        Golden.checkBytes "figure-field-lic.png" asset.Bytes

        match TexToolchain.compileAssetsWith "field-lic" (TikzBackend.standalone rendered.Document) rendered.Assets (fun _ -> Ok()) with
        | TexToolchain.PdfUnavailable _ -> Console.WriteLine("SKIPPED: " + TexToolchain.report ())
        | TexToolchain.PdfProduced() -> ()
        | TexToolchain.PdfFailed why -> failwithf "the LIC baseline does not compile:\n%s" why

// ======================================================================================
// Phase 6 — phase portraits (D92, second half). Gate 13.
// ======================================================================================

module ``phase portraits`` =

    /// A linear flow ẋ = A (x − x₀) over [−1, 1]² in metres, per second.
    let private linear (n: int) (x0: float, y0: float) (a: float, b: float, c: float, d: float) =
        let flow: Flow<m, m, s> =
            fun x y ->
                let dx, dy = float x - x0, float y - y0
                struct ((a * dx + b * dy) * 1.0<m / s>, (c * dx + d * dy) * 1.0<m / s>)

        Field.sampleFlow (Grid.linear -1.0<m> 1.0<m> n) (Grid.linear -1.0<m> 1.0<m> n) flow

    let private systems =
        [ "saddle", (1.0, 0.0, 0.0, -2.0), FixedPointKind.Saddle
          "stable node", (-1.0, 0.0, 0.0, -3.0), FixedPointKind.Node true
          "unstable node", (2.0, 1.0, 0.0, 1.0), FixedPointKind.Node false
          "stable focus", (-0.5, -2.0, 2.0, -0.5), FixedPointKind.Focus true
          "unstable focus", (0.5, 2.0, -2.0, 0.5), FixedPointKind.Focus false
          "centre (linear)", (0.0, 1.0, -4.0, 0.0), FixedPointKind.Centre ]

    let systemCases: obj[] seq =
        seq {
            for name, _, _ in systems do
                yield [| box name; box true |]
                yield [| box name; box false |]
        }

    /// **Gate 13, the canonical linear systems.** Each is found exactly once, where it is, of
    /// its kind, with its eigenvalues — with the fixed point on a grid node (shared by four cells,
    /// so found four times and merged) and in the middle of a cell. A linear field is exact
    /// under both the interpolation and the differences, so the tolerances are rounding.
    [<Theory>]
    [<MemberData(nameof systemCases)>]
    let ``a linear system's fixed point is found once, located, and classified`` (name: string) (onNode: bool) =
        let _, (a, b, c, d), kind = systems |> List.find (fun (n, _, _) -> n = name)
        let x0, y0 = if onNode then 0.0, 0.0 else 0.3, -0.2
        let points = Phase.fixedPoints (linear 21 (x0, y0) (a, b, c, d))

        let p = Assert.Single points
        Assert.Equal(kind, p.Kind)
        Assert.Equal(name, FixedPoint.kindName p.Kind)
        Assert.True(abs (float p.X - x0) < 1e-9 && abs (float p.Y - y0) < 1e-9, sprintf "found at (%g, %g)" (float p.X) (float p.Y))

        // The eigenvalues, against the closed form for a 2 × 2 matrix, in 1/s — by type.
        let tr, det = a + d, a * d - b * c
        let disc = tr * tr - 4.0 * det
        let l1, l2 = p.Eigenvalues
        let re1: float<1 / s> = l1.Re
        let im1: float<1 / s> = l1.Im

        if disc >= 0.0 then
            Assert.True(abs (float re1 - (tr + sqrt disc) / 2.0) < 1e-9 && abs (float l2.Re - (tr - sqrt disc) / 2.0) < 1e-9)
            Assert.Equal(0.0, float im1)
        else
            Assert.True(abs (float re1 - tr / 2.0) < 1e-9 && abs (float im1 - sqrt -disc / 2.0) < 1e-9)
            Assert.True(abs (float l2.Im + sqrt -disc / 2.0) < 1e-9)

    let private pendulum (n: int) = GoldenScenes.Fields.portraitPendulum n

    let private omega = sqrt 9.81

    /// **Gate 13, the pendulum.** Centres at 0 and ±2π, saddles at ±π, on (rad, rad/s) — a phase
    /// space whose Jacobian has four entries in four units — with eigenvalues ±i√g/L at a centre
    /// and ±√g/L at a saddle, in 1/s.
    [<Fact>]
    let ``a pendulum has centres at 0 and 2π and saddles at π`` () =
        let points = Phase.fixedPoints (pendulum 53)

        let expected =
            [ -2.0 * Math.PI, FixedPointKind.Centre
              -Math.PI, FixedPointKind.Saddle
              0.0, FixedPointKind.Centre
              Math.PI, FixedPointKind.Saddle
              2.0 * Math.PI, FixedPointKind.Centre ]

        Assert.Equal(expected.Length, points.Length)

        for (x, kind), p in List.zip expected (Array.toList points) do
            Assert.Equal(kind, p.Kind)
            // The interpolant's root, O(h²) from the function's at a pitch of 0.25 rad.
            Assert.True(abs (float p.X - x) < 1e-3, sprintf "a %A at %g, not %g" kind (float p.X) x)
            Assert.True(abs (float p.Y) < 1e-9)

            let l1, l2 = p.Eigenvalues
            let rate: float<1 / s> = if kind = FixedPointKind.Centre then l1.Im else l1.Re
            // Central differences of sin over a 0.25 rad pitch: sin(h)/h under the root, 0.5% low.
            Assert.True(abs (float rate - omega) < 0.01 * omega, sprintf "%A: %g, not %g" kind (float rate) omega)
            Assert.True(abs (float l1.Re + float l2.Re - float p.Trace) < 1e-9)

    /// The eigenvalues converge as O(h²): halving the pitch quarters the error.
    [<Fact>]
    let ``the eigenvalues converge at second order`` () =
        let error n =
            let centre = Phase.fixedPoints (pendulum n) |> Array.find (fun p -> abs (float p.X) < 0.1)
            abs (float (fst centre.Eigenvalues).Im - omega)

        let ratio = error 53 / error 105
        Assert.True(ratio > 3.5 && ratio < 4.5, sprintf "the error fell by %g, not 4" ratio)

    /// A fixed point is a zero of the interpolant every mark draws, so it sits exactly where the
    /// drawn nullclines cross.
    [<Fact>]
    let ``a fixed point is a zero of the interpolant`` () =
        let g = pendulum 53

        for p in Phase.fixedPoints g do
            let struct (u, w) = VectorGrid.at g p.X p.Y
            Assert.True(abs (float u) < 1e-9 && abs (float w) < 1e-9, sprintf "(%g, %g) at %g" (float u) (float w) (float p.X))

    /// A bilinear pair can have two roots in one cell, so Newton is started from five points.
    /// Here u = xy − 0.1 and w = x − y on a single cell: zeros at ±(√0.1, √0.1), a saddle and a
    /// stable focus, and from the cell's centre, where both partials of u vanish, Newton cannot
    /// start at all.
    [<Fact>]
    let ``two fixed points in one cell are both found`` () =
        let flow: Flow<m, m, s> =
            fun x y -> struct ((float x * float y - 0.1) * 1.0<m / s>, (float x - float y) * 1.0<m / s>)

        let g = Field.sampleFlow (Grid.linear -1.0<m> 1.0<m> 2) (Grid.linear -1.0<m> 1.0<m> 2) flow
        let points = Phase.fixedPoints g
        let r = sqrt 0.1

        Assert.Equal(2, points.Length)
        Assert.True(abs (float points.[0].X + r) < 1e-9 && abs (float points.[1].X - r) < 1e-9)
        Assert.Equal(FixedPointKind.Focus true, points.[0].Kind)
        Assert.Equal(FixedPointKind.Saddle, points.[1].Kind)

    /// A centre is a judgement. A focus whose real part is a ten-thousandth of its imaginary part
    /// is a centre at the default tolerance, and a focus at a tighter one.
    [<Fact>]
    let ``a weak focus is a centre at the default tolerance and not at a tighter one`` () =
        let g = linear 21 (0.3, -0.2) (-2e-4, 2.0, -2.0, -2e-4)
        Assert.Equal(FixedPointKind.Centre, (Assert.Single(Phase.fixedPoints g)).Kind)
        Assert.Equal(FixedPointKind.Focus true, (Assert.Single(Phase.fixedPointsWith 1e-5 g)).Kind)

    /// A zero eigenvalue decides nothing: ẋ = x², ẏ = −y has a fixed point the linearisation
    /// cannot classify.
    [<Fact>]
    let ``a zero eigenvalue is degenerate`` () =
        let flow: Flow<m, m, s> = fun x y -> struct (float x * float x * 1.0<m / s>, -float y * 1.0<m / s>)
        let g = Field.sampleFlow (Grid.linear -1.0<m> 1.0<m> 21) (Grid.linear -1.0<m> 1.0<m> 21) flow
        Assert.Equal(FixedPointKind.Degenerate, (Assert.Single(Phase.fixedPoints g)).Kind)

    /// The description prints the eigenvalues in base units: s⁻¹, never `Hz`, which is what the
    /// registry would call it and what an eigenvalue is not.
    [<Fact>]
    let ``a fixed point describes itself, its eigenvalues in s⁻¹`` () =
        let centre = Phase.fixedPoints (pendulum 53) |> Array.find (fun p -> abs (float p.X) < 0.1)
        let text = FixedPoint.describe U.radian (U.div U.radian U.second) U.second centre
        Assert.StartsWith("centre (linear) at (0 rad, 0 rad·s⁻¹), λ = ±3.11", text)
        Assert.EndsWith("i s⁻¹", text)
        Assert.DoesNotContain("Hz", text)

    let private portrait = GoldenScenes.Fields.phasePortrait

    [<Fact>]
    let ``golden: a phase portrait with nullclines and fixed points (svg)`` () =
        Golden.check "figure-field-portrait.svg" (Chart.render SvgBackend.render (Chart.size 480.0 300.0) portrait)

    /// The legend carries `dθ/dt = 0` and `dω/dt = 0`, Greek in a text run, so the TikZ baseline is
    /// compiled.
    [<Fact>]
    let ``golden: the same portrait (tikz)`` () =
        let doc = Chart.render TikzBackend.render (Chart.size 480.0 300.0) portrait
        Golden.check "figure-field-portrait.tex" doc

        match TexToolchain.compile "field-portrait" (TikzBackend.standalone doc) with
        | TexToolchain.Unavailable -> Console.WriteLine("SKIPPED: " + TexToolchain.report ())
        | TexToolchain.Compiled -> ()
        | TexToolchain.Failed why -> failwithf "the portrait baseline does not compile:\n%s" why

    /// A portrait zoomed in twice on a live view, as a window's drag zooms it, draws no more
    /// than twice the path segments it draws unzoomed, in well under a second. Traced over the
    /// whole grid, the second zoom took seconds and drew a scene some 200 times as large, and its
    /// PDF ran to tens of megabytes.
    [<Fact>]
    let ``a portrait zoomed in on a live view costs what the plot area holds`` () =
        let live = Live.ofControl (Control.constant portrait)
        let zoom (s: LiveState) (x0, y0, x1, y1) = Live.step live s (Zoom { X0 = x0 * 1.0<px>; Y0 = y0 * 1.0<px>; X1 = x1 * 1.0<px>; Y1 = y1 * 1.0<px> })
        let size = Chart.size 1200.0 800.0

        let rec segments (n: Node) =
            match n with
            | Path(segs, _, _, _) -> segs.Length
            | Group ns -> ns |> Array.sumBy segments
            | Transform(_, c)
            | Clip(_, c)
            | Keyed(_, c)
            | Annotated(_, c)
            | Opacity(_, c) -> segments c
            | _ -> 0

        let s0 = Live.init live
        let unzoomed = segments (Chart.toScene size (Live.view live s0))
        let twice = zoom (zoom s0 (300.0, 200.0, 500.0, 350.0)) (500.0, 300.0, 560.0, 340.0)
        let clock = Diagnostics.Stopwatch.StartNew()
        let drawn = segments (Chart.toScene size (Live.view live twice))
        let took = clock.Elapsed.TotalSeconds

        Assert.True(drawn <= 2 * unzoomed, sprintf "%d path segments zoomed, %d unzoomed" drawn unzoomed)
        Assert.True(took < 1.0, sprintf "the zoomed scene took %g s" took)

// ======================================================================================
// Phase 7 — a field in motion. Gate 14.
// ======================================================================================

module ``a field in motion`` =

    let private extent = Chart.size 480.0 300.0

    /// A potential in volts that grows tenfold over one second: (1 + 9t)·xy.
    let private growing (t: float<s>) =
        Field.sampleScalar (Grid.linear -1.0<m> 1.0<m> 11) (Grid.linear -1.0<m> 1.0<m> 11) (fun p ->
            (1.0 + 9.0 * float t) * float p.X * float p.Y * 1.0<V>)

    let private heatmapFilm =
        Anim.ofSeconds 1.0<s> (fun t -> Field.heatmap U.metre U.metre U.volt (growing t) |> Chart.withTitle "growing")
        |> Film.ofFigures 8 extent

    let private quiverFilm =
        Anim.ofSeconds 1.0<s> (fun t -> Field.quiver U.metre U.metre (U.div U.volt U.metre) (Field.grad (growing t)))
        |> Film.ofFigures 8 extent

    let private frames (film: Film) = Frames.times 10.0 film.Duration |> Array.map film.Frames.At

    let rec private collect (pick: Node -> 'a option) (n: Node) : 'a list =
        let here = pick n |> Option.toList

        let below =
            match n with
            | Group ns -> ns |> Array.toList |> List.collect (collect pick)
            | Transform(_, c)
            | Clip(_, c)
            | Keyed(_, c)
            | Annotated(_, c)
            | Opacity(_, c) -> collect pick c
            | _ -> []

        here @ below

    let private texts = collect (function Text t -> Some t.Text | _ -> None)
    let private clips = collect (function Clip(segs, _) -> Some segs | _ -> None)
    let private rasters = collect (function Raster r -> Some r | _ -> None)

    /// **Gate 14.** Every frame has the same plot rectangle and the same words — tick labels,
    /// colorbar labels and all — so neither the panel nor the colour scale moves while the field
    /// grows.
    [<Fact>]
    let ``the plot rectangle and the colour scale are the same in every frame`` () =
        let fs = frames heatmapFilm
        Assert.True(fs.Length >= 10)

        for f in fs do
            Assert.Equal<string list>(texts fs.[0], texts f)
            Assert.True((clips fs.[0] = clips f), "the plot area moved between frames")

        // The union over the samples, which include t = 1 s: ±10 V.
        Assert.Equal((-10.0, 10.0), heatmapFilm.Shot.ColourDomains |> Array.exactlyOne |> snd |> fun (a, b) -> Math.Round(a, 9), Math.Round(b, 9))

    /// And the field is seen to grow: a corner pixel is pale at the start and saturated at the
    /// end. Renormalised per frame, the two images would be the same.
    [<Fact>]
    let ``a growing field is drawn growing`` () =
        let fs = frames heatmapFilm
        let first = rasters fs.[0] |> List.exactlyOne
        let last = rasters fs.[fs.Length - 1] |> List.exactlyOne
        Assert.False((first.Image.Rgba = last.Image.Rgba), "the first and last frames draw the same image")

        // The top-right node, at +1 V·(1 + 9t): the ramp's middle-upper part early, its end late.
        let corner (r: RasterNode) = r.Image.Rgba.[(r.Image.Width - 1) * 4 .. (r.Image.Width - 1) * 4 + 2]
        Assert.NotEqual<byte[]>(corner first, corner last)

    /// The quiver's arrows grow with the field — tenfold, as it does — under one key in every
    /// frame. Each frame used to set its longest arrow to 0.9 of the pitch.
    [<Fact>]
    let ``a growing field's arrows grow, under one key`` () =
        let fs = frames quiverFilm

        // Tail to tip of every arrow: six segments each, batched by colour into one path. The
        // key is skipped — it is the one arrow that must not change.
        let rec arrowLengths (n: Node) : float list =
            match n with
            | Annotated(a, _) when a.Label = "quiver key" -> []
            | Path(segs, Some _, Some _, _) ->
                [ for k in 0..6 .. segs.Length - 6 do
                      match segs.[k], segs.[k + 2] with
                      | MoveTo a, MoveTo b -> yield sqrt (float ((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)))
                      | _ -> () ]
            | Group ns -> ns |> Array.toList |> List.collect arrowLengths
            | Transform(_, c)
            | Clip(_, c)
            | Keyed(_, c)
            | Annotated(_, c)
            | Opacity(_, c) -> arrowLengths c
            | _ -> []

        let longest (n: Node) = arrowLengths n |> List.max

        // The key the shot pinned is the largest frame's: |∇(10xy)| at a corner, 10√2 V/m.
        let _, key = quiverFilm.Shot.QuiverKeys |> Array.exactlyOne
        Assert.True(abs (key.Largest - 10.0 * sqrt 2.0) < 1e-9, sprintf "pinned against %g" key.Largest)

        // The first frame is t = 0, field ×1; the last is t = 0.9, field ×9.1.
        let ratio = longest fs.[fs.Length - 1] / longest fs.[0]
        Assert.True(abs (ratio - 9.1) < 0.05, sprintf "the longest arrow grew by %g, not 9.1" ratio)

        for f in fs do
            Assert.Equal<string list>(texts fs.[0], texts f)

    /// A stated domain wins over the union, for a film as for a still.
    [<Fact>]
    let ``a stated colour domain is the domain`` () =
        let film =
            Anim.ofSeconds 1.0<s> (fun t ->
                Field.heatmap U.metre U.metre U.volt (growing t) |> Chart.withColorDomain U.volt -20.0<V> 20.0<V>)
            |> Film.ofFigures 8 extent

        Assert.Equal((-20.0, 20.0), film.Shot.ColourDomains |> Array.exactlyOne |> snd)

    [<Fact>]
    let ``a colour domain in another unit is converted, and in another dimension refused`` () =
        let fig = Field.heatmap U.metre U.metre U.volt (growing 0.0<s>)
        let mv = U.scaled "mV" 1e-3 U.volt
        let shot = Chart.arrangeFor TextMeasure.approx extent (fig |> Chart.withColorDomain mv -500.0<V> 500.0<V>)
        ignore shot

        let channelDomain (f: Figure) =
            f.Panels.[0].Layers.[0].Channels.[0].Map
            |> function
                | ColorByValue(scale, _) -> Axis.domainOfPacked scale
                | _ -> failwith "no colour channel"

        let lo, hi = channelDomain (fig |> Chart.withColorDomain mv -500.0<V> 500.0<V>)
        Assert.True(abs (lo + 0.5) < 1e-12 && abs (hi - 0.5) < 1e-12, sprintf "(%g, %g)" lo hi)

        Assert.Throws<ArgumentException>(fun () -> fig |> Chart.withColorDomain U.metre 0.0<m> 1.0<m> |> ignore) |> ignore
