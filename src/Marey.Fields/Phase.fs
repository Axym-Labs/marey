namespace Marey

open System
open System.Runtime.InteropServices

// ======================================================================================
// PHASE PORTRAITS: NULLCLINES, FIXED POINTS, AND WHAT KIND EACH ONE IS.
//
// **Nullclines** are where one component of the flow vanishes: the zero contours of `u` and of `w`,
// which the contour's marching squares already draws. Where they cross, both vanish, and that is a
// fixed point.
//
// **Fixed points are found on the interpolant.** A cell whose four `u` corners do not all share
// a sign, and whose four `w` corners do not either, may hold a zero of both. Newton's method on
// the cell's bilinear interpolant — a 2 × 2 system whose Jacobian is exact — finds it, started
// from the cell's centre and its four quarter-points, because a bilinear pair can have two roots
// in one cell. A root found outside its cell belongs to a neighbour and is dropped; a root on a
// shared edge is found twice and merged. What is found is a zero of the interpolant, the
// function every other mark in this package draws, so a fixed point sits exactly where its
// nullclines cross on the page. Against the sampled function it is O(h²) in the node pitch.
//
// **Classified by the linearisation's trace and determinant**, read from `Field.jacobian` at the
// point. On a phase space the Jacobian's entries are in four different units — ∂θ̇/∂θ in 1/s,
// ∂θ̇/∂ω in rad/(rad/s)/s — but the trace is in `1/'t` and the determinant in `1/'t²`, so the
// classification never adds two quantities the units disagree on, and the eigenvalues come out
// in `1/'t`: the rates and the angular frequencies of the motion near the point.
//
// **A centre is not numerically decidable.** Linearisation gives a centre purely imaginary
// eigenvalues and a weak focus nearly imaginary ones, and the nonlinear terms decide between
// them — which no Jacobian sees. The classifier calls a point a centre when the eigenvalues' real
// part is under a stated fraction of their imaginary part (default 10⁻³: an amplitude change of
// 0.6% per revolution), and says "centre (linear)" when it describes one.
// ======================================================================================

/// What the linearisation says a fixed point is.
[<RequireQualifiedAccess>]
type FixedPointKind =
    /// Real eigenvalues of opposite sign: one direction in, one out.
    | Saddle
    /// Real eigenvalues of one sign: every nearby orbit approaches (`stable`) or leaves without
    /// turning.
    | Node of stable: bool
    /// Complex eigenvalues with a real part: orbits spiral in (`stable`) or out.
    | Focus of stable: bool
    /// Purely imaginary eigenvalues, to the classifier's tolerance. Linearly a centre; the
    /// nonlinear terms, which no Jacobian sees, may make it a weak focus.
    | Centre
    /// A zero eigenvalue. The linearisation decides nothing, and the point need not be
    /// isolated.
    | Degenerate

/// One eigenvalue of a fixed point's linearisation, in `1/'t`. A real one has `Im = 0`.
[<Struct>]
type Eigenvalue<[<Measure>] 't> =
    { Re: float<1 / 't>
      Im: float<1 / 't> }

/// A zero of a flow, located on the grid's interpolant and classified by its linearisation.
type FixedPoint<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't> =
    { X: float<'x>
      Y: float<'y>
      Kind: FixedPointKind
      /// ∂u/∂x + ∂w/∂y: the rate phase-space area grows at the point.
      Trace: float<1 / 't>
      /// ∂u/∂x·∂w/∂y − ∂u/∂y·∂w/∂x.
      Determinant: float<1 / 't ^ 2>
      /// The two eigenvalues: the larger real part first, and of a complex pair the one with
      /// positive imaginary part first.
      Eigenvalues: Eigenvalue<'t> * Eigenvalue<'t> }

/// Reading a classified fixed point.
[<RequireQualifiedAccess>]
module FixedPoint =

    /// The kind as a reader says it: `"saddle"`, `"stable focus"`, `"centre (linear)"`.
    let kindName (kind: FixedPointKind) =
        match kind with
        | FixedPointKind.Saddle -> "saddle"
        | FixedPointKind.Node true -> "stable node"
        | FixedPointKind.Node false -> "unstable node"
        | FixedPointKind.Focus true -> "stable focus"
        | FixedPointKind.Focus false -> "unstable focus"
        | FixedPointKind.Centre -> "centre (linear)"
        | FixedPointKind.Degenerate -> "degenerate"

    let private number (v: float) =
        let s = (if abs v < 5e-13 then 0.0 else v).ToString("0.####", Globalization.CultureInfo.InvariantCulture)
        if s = "-0" then "0" else s

    let internal describe<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (ux: U<'x>)
        (uy: U<'y>)
        (ut: U<'t>)
        (p: FixedPoint<'x, 'y, 't>)
        : string =
        let coord (u: U<'u>) (v: float<'u>) =
            UnitDisplay.quantity Unicode UnitRegistry.si false number u v

        let rate = U.recip ut
        let si (v: float<1 / 't>) = U.toSI rate v
        let unitText = UnitDisplay.renderExpanded Unicode rate.Dim
        let withUnit (s: string) = if unitText = "" then s else s + " " + unitText
        let a, b = p.Eigenvalues

        let lambda =
            if a.Im <> 0.0<_> then
                let re = si a.Re
                let im = abs (si a.Im)

                if re = 0.0 then sprintf "±%si" (number im)
                else sprintf "%s ± %si" (number re) (number im)
            else
                sprintf "%s, %s" (number (si a.Re)) (number (si b.Re))

        sprintf "%s at (%s, %s), λ = %s" (kindName p.Kind) (coord ux p.X) (coord uy p.Y) (withUnit lambda)

/// Phase portraits: a flow's nullclines, its fixed points and what kind each one is, and
/// the three drawn together over its streamlines.
[<RequireQualifiedAccess>]
module Phase =

    /// The fraction of an eigenvalue's imaginary part its real part may be and the point still
    /// be called a centre: 10⁻³, an amplitude change of 0.6% per revolution.
    [<Literal>]
    let CentreTolerance = 1e-3

    /// The fraction of the larger eigenvalue's magnitude the smaller one's may be and the point be
    /// called degenerate. Rounding, not truncation: a zero eigenvalue the differences only
    /// approximate (ẋ = x³ at a finite pitch) is classified as whatever its O(h²) error makes it.
    [<Literal>]
    let private DegenerateTolerance = 1e-9

    /// Classify a linearisation by its trace and determinant: the kind, and the eigenvalues, the
    /// larger real part first. A zero eigenvalue is judged against the other one, the only scale
    /// in `1/'t` a 2 × 2 linearisation has that does not mix the off-diagonal units.
    let classify<[<Measure>] 't> (centreTolerance: float) (trace: float<1 / 't>) (det: float<1 / 't ^ 2>) =
        let tr = float trace
        let d = float det

        let eigen (re: float) (im: float) : Eigenvalue<'t> =
            { Re = LanguagePrimitives.FloatWithMeasure re
              Im = LanguagePrimitives.FloatWithMeasure im }

        let disc = tr * tr - 4.0 * d

        let eigenvalues =
            if disc >= 0.0 then
                let r = sqrt disc / 2.0
                eigen (tr / 2.0 + r) 0.0, eigen (tr / 2.0 - r) 0.0
            else
                let i = sqrt -disc / 2.0
                eigen (tr / 2.0) i, eigen (tr / 2.0) -i

        let magnitude (e: Eigenvalue<'t>) = sqrt (float e.Re * float e.Re + float e.Im * float e.Im)
        let m1, m2 = magnitude (fst eigenvalues), magnitude (snd eigenvalues)

        let kind =
            if min m1 m2 <= DegenerateTolerance * max m1 m2 then FixedPointKind.Degenerate
            elif d < 0.0 then FixedPointKind.Saddle
            elif abs tr <= centreTolerance * 2.0 * sqrt d then FixedPointKind.Centre
            elif disc >= 0.0 then FixedPointKind.Node(tr < 0.0)
            else FixedPointKind.Focus(tr < 0.0)

        kind, eigenvalues

    /// The zeros of both components inside one cell, in the cell's own coordinates (s, t) ∈
    /// [0, 1]², by Newton on the bilinear interpolant from five starts.
    let private rootsInCell (u00, u10, u01, u11) (w00, w10, w01, w11) =
        let cu = u00 - u10 - u01 + u11
        let cw = w00 - w10 - w01 + w11
        let roots = ResizeArray<struct (float * float)>()
        let slack = 1e-9

        for s0, t0 in [ 0.5, 0.5; 0.25, 0.25; 0.75, 0.25; 0.25, 0.75; 0.75, 0.75 ] do
            let mutable s, t = s0, t0
            let mutable go = true
            let mutable n = 0
            let mutable converged = false

            while go && n < 50 do
                n <- n + 1
                let u = u00 + (u10 - u00) * s + (u01 - u00) * t + cu * s * t
                let w = w00 + (w10 - w00) * s + (w01 - w00) * t + cw * s * t
                let us = (u10 - u00) + cu * t
                let ut = (u01 - u00) + cu * s
                let ws = (w10 - w00) + cw * t
                let wt = (w01 - w00) + cw * s
                let j = us * wt - ut * ws

                if j = 0.0 || Double.IsNaN j then
                    go <- false
                else
                    let ds = -(u * wt - ut * w) / j
                    let dt = -(us * w - u * ws) / j
                    s <- s + ds
                    t <- t + dt

                    // Far outside the cell: a neighbour's root, or none.
                    if abs s > 4.0 || abs t > 4.0 then go <- false
                    elif abs ds + abs dt < 1e-13 then
                        converged <- true
                        go <- false

            if converged && s >= -slack && s <= 1.0 + slack && t >= -slack && t <= 1.0 + slack then
                let s, t = min 1.0 (max 0.0 s), min 1.0 (max 0.0 t)

                if not (roots |> Seq.exists (fun (struct (a, b)) -> abs (a - s) < 1e-7 && abs (b - t) < 1e-7)) then
                    roots.Add(struct (s, t))

        roots

    /// The fixed points of a flow on its grid, classified, with a stated tolerance for calling
    /// one a centre (`CentreTolerance` by default). Ordered by x, then y.
    ///
    /// `g` is a flow: its components are the rates of its coordinates, `'x/'t` and `'y/'t`. A
    /// spatial field fits too, with `'t` whatever makes it one — an electric field in V/m over
    /// metres has `'t` = m²/V, and its eigenvalues are in V/m².
    let fixedPointsWith<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (centreTolerance: float)
        (g: VectorGrid<'x, 'y, 'x / 't, 'y / 't>)
        : FixedPoint<'x, 'y, 't>[] =
        let xs = Grid.raw g.Xs
        let ys = Grid.raw g.Ys
        let u = Grid.raw g.U
        let w = Grid.raw g.W
        let nx = xs.Length
        let jac = Field.jacobian g
        let found = ResizeArray<struct (float * float)>()

        let straddles (a: float) (b: float) (c: float) (d: float) =
            let lo = min (min a b) (min c d)
            let hi = max (max a b) (max c d)
            lo <= 0.0 && hi >= 0.0

        for j in 0 .. ys.Length - 2 do
            for i in 0 .. nx - 2 do
                let k00, k10, k01, k11 = j * nx + i, j * nx + i + 1, (j + 1) * nx + i, (j + 1) * nx + i + 1
                let us = u.[k00], u.[k10], u.[k01], u.[k11]
                let ws = w.[k00], w.[k10], w.[k01], w.[k11]
                let a, b, c, d = us
                let e, f, gg, h = ws
                let present = [| a; b; c; d; e; f; gg; h |] |> Array.forall (fun v -> not (Grid.isMissing v))

                if present && straddles a b c d && straddles e f gg h then
                    for struct (s, t) in rootsInCell us ws do
                        let x = xs.[i] + s * (xs.[i + 1] - xs.[i])
                        let y = ys.[j] + t * (ys.[j + 1] - ys.[j])
                        // Merged with a root a neighbouring cell found on their shared edge.
                        let tolX = 1e-7 * (xs.[i + 1] - xs.[i])
                        let tolY = 1e-7 * (ys.[j + 1] - ys.[j])

                        if not (found |> Seq.exists (fun (struct (px, py)) -> abs (px - x) <= tolX && abs (py - y) <= tolY)) then
                            found.Add(struct (x, y))

        found.ToArray()
        |> Array.sortBy (fun (struct (x, y)) -> x, y)
        |> Array.choose (fun (struct (x, y)) ->
            let x' = LanguagePrimitives.FloatWithMeasure<'x> x
            let y' = LanguagePrimitives.FloatWithMeasure<'y> y
            let a = ScalarGrid.at jac.DuDx x' y'
            let b = ScalarGrid.at jac.DuDy x' y'
            let c = ScalarGrid.at jac.DwDx x' y'
            let d = ScalarGrid.at jac.DwDy x' y'

            if [ float a; float b; float c; float d ] |> List.exists Grid.isMissing then
                None
            else
                let trace = a + d
                let det = a * d - b * c
                let kind, eigenvalues = classify centreTolerance trace det

                Some
                    { X = x'
                      Y = y'
                      Kind = kind
                      Trace = trace
                      Determinant = det
                      Eigenvalues = eigenvalues })

    /// `fixedPointsWith CentreTolerance`.
    let fixedPoints<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't> (g: VectorGrid<'x, 'y, 'x / 't, 'y / 't>) : FixedPoint<'x, 'y, 't>[] =
        fixedPointsWith CentreTolerance g

    // ---- Drawing ------------------------------------------------------------------------------

    /// The name an axis would be titled with when nobody names it: `θ` for an angle, `ω` for an
    /// angular velocity, `x` or `y` otherwise.
    let private symbolOf (side: AxisSide) (u: U<'u>) =
        Axes.defaultLabelFor side (U.angleExponentOf u.Symbol u.Factors) u.Dim

    let internal nullclines<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Figure =
        let line (name: string) (colour: Theme -> Color) (fig: Figure) =
            let theme = fig.Theme

            fig
            |> Chart.withStyle (fun st -> { st with Color = colour theme; Width = theme.SeriesWidth })
            |> Chart.withName name

        let along = symbolOf AxisBottom ux
        let up = symbolOf AxisLeft uy
        let pick k (t: Theme) = t.Categorical.[k % t.Categorical.Length]

        Field.contoursAt [| LanguagePrimitives.FloatWithMeasure<'u> 0.0 |] ux uy (VectorGrid.u g)
        |> line (sprintf "d%s/dt = 0" along) (pick 0)
        |> Chart.overlay (
            Field.contoursAt [| LanguagePrimitives.FloatWithMeasure<'w> 0.0 |] ux uy (VectorGrid.w g)
            |> line (sprintf "d%s/dt = 0" up) (pick 1)
        )

    let private shapeOf (kind: FixedPointKind) =
        match kind with
        | FixedPointKind.Saddle -> MarkDiamond
        | FixedPointKind.Node true
        | FixedPointKind.Focus true -> MarkTriangleDown
        | FixedPointKind.Node false
        | FixedPointKind.Focus false -> MarkTriangleUp
        | FixedPointKind.Centre -> MarkCircle
        | FixedPointKind.Degenerate -> MarkSquare

    /// A kind's colour is fixed rather than taken in turn, so a centre is the same colour in every
    /// portrait whichever other kinds share it. The nullclines have the series palette's first
    /// two colours. Sky blue is skipped because it does not stand out against the first nullcline's
    /// blue, and yellow is used only as a filled mark, where its low contrast on white still reads.
    let private colourIndex (kind: FixedPointKind) =
        match kind with
        | FixedPointKind.Centre -> 2
        | FixedPointKind.Saddle -> 3
        | FixedPointKind.Node true
        | FixedPointKind.Focus true -> 5
        | FixedPointKind.Node false
        | FixedPointKind.Focus false -> 6
        | FixedPointKind.Degenerate -> 7

    let internal marks<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (ux: U<'x>)
        (uy: U<'y>)
        (points: FixedPoint<'x, 'y, 't>[])
        : Figure option =
        let layers =
            points
            |> Array.groupBy (fun p -> FixedPoint.kindName p.Kind)
            |> Array.sortBy fst
            |> Array.map (fun (name, ps) ->
                let fig = Marey.UnitsOfMeasure.Chart.scatter ux uy (ps |> Array.map (fun p -> p.X)) (ps |> Array.map (fun p -> p.Y))
                let theme = fig.Theme

                fig
                |> Chart.withMarker (shapeOf ps.[0].Kind)
                |> Chart.withStyle (fun st ->
                    { st with
                        Color = theme.Categorical.[colourIndex ps.[0].Kind % theme.Categorical.Length]
                        MarkerSize = theme.MarkerSize * 3.0 })
                |> Chart.withName name)

        match Array.toList layers with
        | [] -> None
        | first :: rest -> Some(rest |> List.fold (fun acc l -> acc |> Chart.overlay l) first)

    let internal portraitWith<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (separation: float<px>)
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'x / 't, 'y / 't>)
        : Figure =
        let flow =
            Field.streamlinesWith separation ux uy g
            |> Chart.withStyle (fun st -> { st with Width = st.Width * 0.75 })
            |> Chart.overlay (nullclines ux uy g)

        match marks ux uy (fixedPoints g) with
        | Some m -> flow |> Chart.overlay m
        | None -> flow

    let internal portrait<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'x / 't, 'y / 't>)
        : Figure =
        portraitWith (Field.DefaultSeparation * 1.0<px>) ux uy g

/// Phase portraits in the unit-optional mode. See `type Field`.
[<AbstractClass; Sealed>]
type Phase =

    static member Nullclines
        (
            g: VectorGrid<1, 1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Phase.nullclines (U.erase xUnit) (U.erase yUnit) g

    static member Marks
        (
            points: seq<FixedPoint<1, 1, 1>>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure option =
        Phase.marks (U.erase xUnit) (U.erase yUnit) (Array.ofSeq points)

    static member PortraitWith
        (
            separation: float<px>,
            g: VectorGrid<1, 1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Phase.portraitWith separation (U.erase xUnit) (U.erase yUnit) g

    static member Portrait
        (
            g: VectorGrid<1, 1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Phase.portrait (U.erase xUnit) (U.erase yUnit) g

    /// `FixedPoint.describe`, in the unit-optional mode. A member here rather than on a type
    /// `FixedPoint`, which would collide with the record `FixedPoint<'x, 'y, 't>` (FS0037).
    static member Describe
        (
            p: FixedPoint<1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] timeUnit: IUnit
        ) : string =
        FixedPoint.describe (U.erase xUnit) (U.erase yUnit) (U.erase timeUnit) p
