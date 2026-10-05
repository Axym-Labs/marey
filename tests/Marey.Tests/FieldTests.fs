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

    /// D52: a point charge's potential is infinite at the charge. The node is kept, counted, and
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
