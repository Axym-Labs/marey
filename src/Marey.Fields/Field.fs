namespace Marey

open System
open System.Runtime.InteropServices

// ======================================================================================
// §8.1'S FIELDS, AND THE ONE §8.1 COULD NOT TYPE.
//
// §8.1 declares `Field2<'x,'v> = Vec2<'x> -> Vec2<'v>` and then lists phase portraits among the
// things to draw with it. A phase space has a different unit on each axis — a pendulum lives in
// (θ: rad, ω: rad/s) — and `Vec2<'x>` has one measure for both components, so §8.1's type cannot
// hold one. `Flow` can: its axes are independent, and its components are the time derivatives of
// its coordinates.
//
// As built, the function types are only the *inputs*. A field is used as a grid, and one
// `VectorGrid<'x,'y,'u,'w>` holds both a spatial field (`'x = 'y`, `'u = 'w`) and a flow (`'u =
// 'x/'t`, `'w = 'y/'t`), so D84's `asFlow` conversion turned out to have nothing to convert
// between. The distinction moved to where it matters — the derivatives' signatures:
//
//   * `div` asks that ∂u/∂x and ∂w/∂y share a unit, which a spatial field and a flow both
//     satisfy: a field in V/m over metres has a divergence in V/m², and a pendulum's flow has
//     one in 1/s — zero, as Liouville requires.
//   * `curl` and `laplacian` ask for one unit on both axes. On a phase space they are compile
//     errors, which is the right answer: ∂ω̇/∂θ and ∂θ̇/∂ω are in different units, so their
//     difference does not exist.
//
// **Every public function here declares its measure parameters.** Written with inferred ones, a
// mixed-axis curl narrowed itself silently to `'x = 'y` and compiled — only its caller saw an
// error. Declared, the narrowing is FS0064, which the build treats as an error. §12.1's lesson,
// as a rule for the whole package.
// ======================================================================================

/// A flow on a plane whose axes may differ in unit: at `(x, y)`, the rates `dx/dt` and `dy/dt`.
/// A pendulum is a `Flow<rad, rad/s, s>`.
type Flow<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't> = float<'x> -> float<'y> -> struct (float<'x / 't> * float<'y / 't>)

/// §8.1's vector field: one unit on both axes, one on both components. An electric field is a
/// `Field2<m, V/m>`.
type Field2<[<Measure>] 'x, [<Measure>] 'v> = Vec2<'x> -> Vec2<'v>

/// §8.1's scalar field. A potential is a `Scalar2<m, V>`.
type Scalar2<[<Measure>] 'x, [<Measure>] 'v> = Vec2<'x> -> float<'v>

/// The four partial derivatives of a vector grid, each in its own unit.
///
/// On a phase space the off-diagonal entries are in units that match nothing else, and that is
/// fine: the quantities a fixed point is classified by — the trace and the determinant — are
/// sums and products the units do agree on.
type Jacobian<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w> =
    { DuDx: ScalarGrid<'x, 'y, 'u / 'x>
      DuDy: ScalarGrid<'x, 'y, 'u / 'y>
      DwDx: ScalarGrid<'x, 'y, 'w / 'x>
      DwDy: ScalarGrid<'x, 'y, 'w / 'y> }

[<RequireQualifiedAccess>]
module Field =

    // ---- Sampling ----------------------------------------------------------------------------

    /// Sample a scalar field over a plane with one unit on both axes.
    let sampleScalar<[<Measure>] 'x, [<Measure>] 'v>
        (xs: float<'x>[])
        (ys: float<'x>[])
        (f: Scalar2<'x, 'v>)
        : ScalarGrid<'x, 'x, 'v> =
        ScalarGrid.sample xs ys (fun x y -> f { X = x; Y = y })

    /// Sample a vector field over a plane with one unit on both axes.
    let sample<[<Measure>] 'x, [<Measure>] 'v>
        (xs: float<'x>[])
        (ys: float<'x>[])
        (f: Field2<'x, 'v>)
        : VectorGrid<'x, 'x, 'v, 'v> =
        VectorGrid.sample xs ys (fun x y ->
            let v = f { X = x; Y = y }
            struct (v.X, v.Y))

    /// Sample a flow. The grid's components are the rates, in `'x/'t` and `'y/'t`.
    let sampleFlow<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (xs: float<'x>[])
        (ys: float<'y>[])
        (f: Flow<'x, 'y, 't>)
        : VectorGrid<'x, 'y, 'x / 't, 'y / 't> =
        VectorGrid.sample xs ys f

    // ---- Differences ------------------------------------------------------------------------
    //
    // Three-point differences on a rectilinear axis: centred inside, one-sided at the two ends,
    // and all three **exact for a quadratic** whatever the spacing. That is the property the
    // tests hold them to, and it makes `div (grad φ)` exact for a quadratic φ too, because the
    // first pass is exact and leaves a linear function, which the second pass is exact on. The
    // error for anything smoother is O(h²) in the node spacing.
    //
    // A node next to a missing value inherits it: NaN propagates through the arithmetic, so a
    // derivative is never computed across a singularity and quietly reported as finite.

    /// d/dx of `n` values along one axis with coordinates `c`, reading `get k` and writing
    /// `set k`. Needs at least two nodes; with exactly two, the one difference serves both.
    let private along (c: float[]) (get: int -> float) (set: int -> float -> unit) =
        let n = c.Length

        if n = 2 then
            let d = (get 1 - get 0) / (c.[1] - c.[0])
            set 0 d
            set 1 d
        else
            for k in 0 .. n - 1 do
                let i = if k = 0 then 1 elif k = n - 1 then n - 2 else k
                let h1 = c.[i] - c.[i - 1]
                let h2 = c.[i + 1] - c.[i]
                let fm = get (i - 1)
                let f0 = get i
                let fp = get (i + 1)

                let d =
                    if k = 0 then
                        -(2.0 * h1 + h2) / (h1 * (h1 + h2)) * fm + (h1 + h2) / (h1 * h2) * f0 - h1 / (h2 * (h1 + h2)) * fp
                    elif k = n - 1 then
                        h2 / (h1 * (h1 + h2)) * fm - (h1 + h2) / (h1 * h2) * f0 + (2.0 * h2 + h1) / (h2 * (h1 + h2)) * fp
                    else
                        -h2 / (h1 * (h1 + h2)) * fm + (h2 - h1) / (h1 * h2) * f0 + h1 / (h2 * (h1 + h2)) * fp

                set k d

    let private ddx (xs: float[]) (ny: int) (values: float[]) : float[] =
        let nx = xs.Length
        let out = Array.zeroCreate values.Length

        for j in 0 .. ny - 1 do
            along xs (fun i -> values.[j * nx + i]) (fun i d -> out.[j * nx + i] <- d)

        out

    let private ddy (ys: float[]) (nx: int) (values: float[]) : float[] =
        let out = Array.zeroCreate values.Length

        for i in 0 .. nx - 1 do
            along ys (fun j -> values.[j * nx + i]) (fun j d -> out.[j * nx + i] <- d)

        out

    let private dx<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v> (g: ScalarGrid<'x, 'y, 'v>) : ScalarGrid<'x, 'y, 'v / 'x> =
        { Xs = g.Xs
          Ys = g.Ys
          Values = Grid.tagAll<'v / 'x> (ddx (Grid.raw g.Xs) g.Ys.Length (Grid.raw g.Values)) }

    let private dy<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v> (g: ScalarGrid<'x, 'y, 'v>) : ScalarGrid<'x, 'y, 'v / 'y> =
        { Xs = g.Xs
          Ys = g.Ys
          Values = Grid.tagAll<'v / 'y> (ddy (Grid.raw g.Ys) g.Xs.Length (Grid.raw g.Values)) }

    // ---- The derived quantities -------------------------------------------------------------

    /// The gradient: `(∂φ/∂x, ∂φ/∂y)`, each component in the value's unit over its own axis's.
    /// The gradient of a potential in volts over metres is in V/m — a field, and the type says so.
    let grad<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v> (g: ScalarGrid<'x, 'y, 'v>) : VectorGrid<'x, 'y, 'v / 'x, 'v / 'y> =
        { Xs = g.Xs
          Ys = g.Ys
          U = (dx g).Values
          W = (dy g).Values }

    /// The divergence: `∂u/∂x + ∂w/∂y`.
    ///
    /// The signature asks that the two terms share a unit — `'w` must be `'u·'y/'x` — which a
    /// spatial field satisfies (`'x = 'y`, `'u = 'w`) and so does a flow (`'u = 'x/'t`, `'w =
    /// 'y/'t`). A flow's divergence is in `1/'t`: the rate its phase-space area grows.
    let div<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u> (g: VectorGrid<'x, 'y, 'u, 'u 'y / 'x>) : ScalarGrid<'x, 'y, 'u / 'x> =
        let a = dx (VectorGrid.u g)
        let b = dy (VectorGrid.w g)

        { Xs = g.Xs
          Ys = g.Ys
          Values = Array.map2 (+) a.Values b.Values }

    /// The scalar curl in the plane: `∂w/∂x − ∂u/∂y`.
    ///
    /// **Spatial fields only**, and the signature is the reason: the two terms share a unit only
    /// when both axes do and both components do. Over a phase space it does not compile.
    let curl<[<Measure>] 'x, [<Measure>] 'v> (g: VectorGrid<'x, 'x, 'v, 'v>) : ScalarGrid<'x, 'x, 'v / 'x> =
        let a = dx (VectorGrid.w g)
        let b = dy (VectorGrid.u g)

        { Xs = g.Xs
          Ys = g.Ys
          Values = Array.map2 (-) a.Values b.Values }

    /// The Laplacian, `div (grad φ)`: spatial grids only, for `curl`'s reason.
    ///
    /// **A display operator, not a solver's.** Two central first differences make the wide stencil
    /// `(f₍ᵢ₋₂₎ − 2fᵢ + f₍ᵢ₊₂₎) / 4h²`, which is exactly zero on a checkerboard: it does not see the
    /// grid's highest mode at all, so a solver stepped with it never damps odd–even noise. The
    /// solvers in `Marey.Sim` use the compact five-point stencil, `Fdm.laplacian5`.
    let laplacian<[<Measure>] 'x, [<Measure>] 'v> (g: ScalarGrid<'x, 'x, 'v>) : ScalarGrid<'x, 'x, 'v / 'x ^ 2> =
        div (grad g)

    /// All four partial derivatives of a vector grid.
    let jacobian<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Jacobian<'x, 'y, 'u, 'w> =
        let u = VectorGrid.u g
        let w = VectorGrid.w g

        { DuDx = dx u
          DuDy = dy u
          DwDx = dx w
          DwDy = dy w }

    // ---- Drawing --------------------------------------------------------------
    //
    // A field layer is a `GeomField` over a series whose points are the grid's nodes: the axes
    // cover the grid, `Figure.unify` checks its dimensions against every other layer's, and a
    // colour channel over the node values gets its colorbar from the machinery a scatter's uses.
    // The picture itself is computed on the panel at compile time (`FieldMarks`), so it follows
    // whatever domain and scale the panel ends up with.

    let inline private raw (xs: float<'u>[]) = xs |> Array.map float

    /// Half a node pitch beyond each end, so that the end rows of a heatmap are whole cells. A
    /// grid even in log is padded by half a step in log, so the padding is even on a log axis.
    let private paddedDomain<[<Measure>] 'u> (nodes: float<'u>[]) : float<'u> * float<'u> =
        let cs = raw nodes
        let n = cs.Length
        let ratios = cs |> Array.pairwise |> Array.map (fun (a, b) -> b / a)

        let geometric =
            cs.[0] > 0.0
            && ratios |> Array.forall (fun r -> abs (r - ratios.[0]) <= 1e-9 * ratios.[0])
            && ratios.[0] > 1.0 + 1e-9

        let lo, hi =
            if geometric then
                cs.[0] / sqrt ratios.[0], cs.[n - 1] * sqrt ratios.[0]
            else
                cs.[0] - (cs.[1] - cs.[0]) / 2.0, cs.[n - 1] + (cs.[n - 1] - cs.[n - 2]) / 2.0

        LanguagePrimitives.FloatWithMeasure<'u> lo, LanguagePrimitives.FloatWithMeasure<'u> hi

    /// A field layer over the grid's nodes. Only a heatmap pads its axes: its edge nodes are the
    /// centres of whole cells, while a contour or a band ends exactly at the last node and padding
    /// would leave an empty margin inside the axes.
    let private layerWith<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (mark: FieldMark)
        (u: float[])
        (w: float[])
        (g: ScalarGrid<'x, 'y, 'v>)
        : Figure =
        let nx = g.Xs.Length

        let domain (nodes: float<'u>[]) =
            match mark with
            | FieldHeatmap _ -> paddedDomain nodes
            // Arrows are centred on their nodes and can reach half a pitch beyond the last one.
            | FieldQuiver _
            | FieldDirections -> paddedDomain nodes
            | FieldContours _
            | FieldBands _
            | FieldStreamlines _
            | FieldLic _ -> nodes.[0], nodes.[nodes.Length - 1]

        let s: Series<'x, 'y> =
            { Name = ""
              X = Array.init g.Values.Length (fun k -> g.Xs.[k % nx])
              Y = Array.init g.Values.Length (fun k -> g.Ys.[k / nx])
              XScale = Scale.linear ux (domain g.Xs)
              YScale = Scale.linear uy (domain g.Ys) }

        Chart.ofSeries
            (GeomField(
                { Xs = raw g.Xs
                  Ys = raw g.Ys
                  Values = raw g.Values
                  U = u
                  W = w },
                mark
            ))
            s

    let private layerOf<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (mark: FieldMark)
        (g: ScalarGrid<'x, 'y, 'v>)
        : Figure =
        layerWith ux uy mark [||] [||] g

    /// Contour levels for a value range: the nice numbers the range's own axis would tick at.
    let private levelsFor (target: int) (lo: float, hi: float) = Ticks.linear target (lo, hi)

    let private valueRange<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v> (g: ScalarGrid<'x, 'y, 'v>) =
        match ScalarGrid.range g with
        | Some(lo, hi) when hi > lo -> float lo, float hi
        | Some(lo, _) -> float lo - 0.5, float lo + 0.5
        | None -> 0.0, 1.0

    let internal heatmapWith<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ramp: Ramp)
        (smooth: bool)
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: ScalarGrid<'x, 'y, 'v>)
        : Figure =
        layerOf ux uy (FieldHeatmap smooth) g |> Marey.UnitsOfMeasure.Chart.colorByWith ramp uv g.Values

    let internal heatmap<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: ScalarGrid<'x, 'y, 'v>)
        : Figure =
        heatmapWith Theme.initial.Sequential true ux uy uv g

    let internal contoursAt<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (levels: float<'v>[])
        (ux: U<'x>)
        (uy: U<'y>)
        (g: ScalarGrid<'x, 'y, 'v>)
        : Figure =
        let fig = layerOf ux uy (FieldContours(raw levels)) g

        fig |> Chart.withStyle (fun st -> { st with Color = fig.Theme.Foreground; Width = st.Width * 0.6 })

    let internal contours<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (g: ScalarGrid<'x, 'y, 'v>)
        : Figure =
        contoursAt (levelsFor 8 (valueRange g) |> Array.map LanguagePrimitives.FloatWithMeasure<'v>) ux uy g

    let internal bandsWith<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ramp: Ramp)
        (levels: float<'v>[])
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: ScalarGrid<'x, 'y, 'v>)
        : Figure =
        layerOf ux uy (FieldBands(raw levels)) g
        |> Chart.withStyle (fun st -> { st with FillOpacity = 1.0 })
        |> Marey.UnitsOfMeasure.Chart.colorByWith ramp uv g.Values

    let internal bands<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: ScalarGrid<'x, 'y, 'v>)
        : Figure =
        let lo, hi = Ticks.expandToNice 8 (valueRange g)
        let levels = levelsFor 8 (lo, hi) |> Array.map LanguagePrimitives.FloatWithMeasure<'v>
        bandsWith Theme.initial.Sequential levels ux uy uv g

    // ---- Arrows -----------------------------------------------------------------------

    /// A vector layer: `Values` are what colours and sizes an arrow, `U` and `W` its components.
    let private arrowLayerOf<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (mark: FieldMark)
        (values: ScalarGrid<'x, 'y, 'v>)
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Figure =
        let fig = layerWith ux uy mark (raw g.U) (raw g.W) values
        fig |> Chart.withStyle (fun st -> { st with Color = fig.Theme.Foreground; Width = st.Width * 0.8 })

    /// The largest round number — 1, 2 or 5 times a power of ten — not above `largest`: the value
    /// a quiver's key arrow stands for. It is a property of the data, not of the figure's size.
    let private keyValue (largest: float) =
        if not (largest > 0.0) || Double.IsInfinity largest then
            0.0
        else
            let decade = 10.0 ** floor (log10 largest)
            let mantissa = largest / decade
            decade * (if mantissa >= 5.0 then 5.0 elif mantissa >= 2.0 then 2.0 else 1.0)

    /// The key's label: the value under the prefix the value itself would be ticked in, and the
    /// unit in the theme's plain-text style.
    let private keyOf<[<Measure>] 'v> (uv: U<'v>) (largest: float) : QuiverKey =
        let value = keyValue largest

        { Value = value
          Label = UnitDisplay.quantity Unicode UnitRegistry.si true Ticks.formatGeneral uv (LanguagePrimitives.FloatWithMeasure value)
          Largest = largest }

    let internal quiver<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: VectorGrid<'x, 'y, 'v, 'v>)
        : Figure =
        let magnitude = VectorGrid.magnitude g

        let key =
            match ScalarGrid.range magnitude with
            | Some(_, hi) when hi > 0.0<_> -> Some(keyOf uv (float hi))
            | _ -> None

        arrowLayerOf ux uy (FieldQuiver key) magnitude g

    let internal quiverBy<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: VectorGrid<'x, 'y, 'v, 'v>)
        : Figure =
        quiver ux uy uv g |> Marey.UnitsOfMeasure.Chart.colorBy uv (VectorGrid.magnitude g).Values

    let internal directions<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Figure =
        // A node draws when both components are finite; the value only says so.
        let present: ScalarGrid<'x, 'y, 1> =
            { Xs = g.Xs
              Ys = g.Ys
              Values =
                Array.map2
                    (fun (a: float<'u>) (b: float<'w>) -> if Grid.isMissing (float a) || Grid.isMissing (float b) then nan else 1.0)
                    g.U
                    g.W }

        arrowLayerOf ux uy FieldDirections present g

    // ---- Streamlines --------------------------------------------------------------------

    /// The separation streamlines are drawn at when nobody states one, in pixels on the page.
    [<Literal>]
    let DefaultSeparation = 16.0

    let private presentOf<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w> (g: VectorGrid<'x, 'y, 'u, 'w>) : ScalarGrid<'x, 'y, 1> =
        { Xs = g.Xs
          Ys = g.Ys
          Values =
            Array.map2
                (fun (a: float<'u>) (b: float<'w>) -> if Grid.isMissing (float a) || Grid.isMissing (float b) then nan else 1.0)
                g.U
                g.W }

    let internal streamlinesWith<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (separation: float<px>)
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Figure =
        arrowLayerOf ux uy (FieldStreamlines separation) (presentOf g) g

    let internal streamlines<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Figure =
        streamlinesWith (DefaultSeparation * 1.0<px>) ux uy g

    let internal streamlinesBy<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: VectorGrid<'x, 'y, 'v, 'v>)
        : Figure =
        let magnitude = VectorGrid.magnitude g

        arrowLayerOf ux uy (FieldStreamlines(DefaultSeparation * 1.0<px>)) magnitude g
        |> Marey.UnitsOfMeasure.Chart.colorBy uv magnitude.Values

    // ---- Line-integral convolution ------------------------------------------------------

    /// What `lic` computes with when nobody states otherwise: a 20 px kernel, one raster pixel per
    /// page pixel, and seed 0.
    let licDefaults: LicOptions =
        { Length = 20.0<px>
          Oversample = 1
          Seed = 0UL }

    let private licLayerOf<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w, [<Measure>] 'v>
        (options: LicOptions)
        (ux: U<'x>)
        (uy: U<'y>)
        (values: ScalarGrid<'x, 'y, 'v>)
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Figure =
        if not (options.Length > 0.0<px>) || Double.IsInfinity(float options.Length) then
            invalidArg "options" (sprintf "Marey: a LIC kernel must be a positive length on the page; got %g px." (float options.Length))

        if options.Oversample < 1 || options.Oversample > 8 then
            invalidArg "options" (sprintf "Marey: a LIC oversample is 1 to 8 raster pixels per page pixel; got %d." options.Oversample)

        // White, so that without a colour channel the brightness is the whole picture: grey.
        layerWith ux uy (FieldLic options) (raw g.U) (raw g.W) values
        |> Chart.withStyle (fun st -> { st with Color = Color.white })

    let internal licWith<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (options: LicOptions)
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Figure =
        licLayerOf options ux uy (presentOf g) g

    let internal lic<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Figure =
        licWith licDefaults ux uy g

    let internal licByWith<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (options: LicOptions)
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: VectorGrid<'x, 'y, 'v, 'v>)
        : Figure =
        let magnitude = VectorGrid.magnitude g
        licLayerOf options ux uy magnitude g |> Marey.UnitsOfMeasure.Chart.colorBy uv magnitude.Values

    let internal licBy<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: VectorGrid<'x, 'y, 'v, 'v>)
        : Figure =
        licByWith licDefaults ux uy uv g

/// The field geoms in the unit-optional mode: a grid at measure 1, and a unit per
/// axis and for the value that may each be stated or left out. `Field.Heatmap(g, xUnit = U.metre,
/// yUnit = U.metre, valueUnit = U.kelvin)`. A type beside `module Field`, as `type Chart` stands beside
/// `module Chart`: sampling, derivatives and grids stay in the module and work at measure 1 unchanged.
/// Every member is one line over the measure-generic implementation.
[<AbstractClass; Sealed>]
type Field =

    static member HeatmapWith
        (
            ramp: Ramp,
            smooth: bool,
            g: ScalarGrid<1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] valueUnit: IUnit
        ) : Figure =
        Field.heatmapWith ramp smooth (U.erase xUnit) (U.erase yUnit) (U.erase valueUnit) g

    static member Heatmap
        (
            g: ScalarGrid<1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] valueUnit: IUnit
        ) : Figure =
        Field.heatmap (U.erase xUnit) (U.erase yUnit) (U.erase valueUnit) g

    static member ContoursAt
        (
            levels: float[],
            g: ScalarGrid<1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Field.contoursAt levels (U.erase xUnit) (U.erase yUnit) g

    static member Contours
        (
            g: ScalarGrid<1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Field.contours (U.erase xUnit) (U.erase yUnit) g

    static member BandsWith
        (
            ramp: Ramp,
            levels: float[],
            g: ScalarGrid<1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] valueUnit: IUnit
        ) : Figure =
        Field.bandsWith ramp levels (U.erase xUnit) (U.erase yUnit) (U.erase valueUnit) g

    static member Bands
        (
            g: ScalarGrid<1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] valueUnit: IUnit
        ) : Figure =
        Field.bands (U.erase xUnit) (U.erase yUnit) (U.erase valueUnit) g

    static member Quiver
        (
            g: VectorGrid<1, 1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] valueUnit: IUnit
        ) : Figure =
        Field.quiver (U.erase xUnit) (U.erase yUnit) (U.erase valueUnit) g

    static member QuiverBy
        (
            g: VectorGrid<1, 1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] valueUnit: IUnit
        ) : Figure =
        Field.quiverBy (U.erase xUnit) (U.erase yUnit) (U.erase valueUnit) g

    static member Directions
        (
            g: VectorGrid<1, 1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Field.directions (U.erase xUnit) (U.erase yUnit) g

    static member StreamlinesWith
        (
            separation: float<px>,
            g: VectorGrid<1, 1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Field.streamlinesWith separation (U.erase xUnit) (U.erase yUnit) g

    static member Streamlines
        (
            g: VectorGrid<1, 1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Field.streamlines (U.erase xUnit) (U.erase yUnit) g

    static member StreamlinesBy
        (
            g: VectorGrid<1, 1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] valueUnit: IUnit
        ) : Figure =
        Field.streamlinesBy (U.erase xUnit) (U.erase yUnit) (U.erase valueUnit) g

    static member LicWith
        (
            options: LicOptions,
            g: VectorGrid<1, 1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Field.licWith options (U.erase xUnit) (U.erase yUnit) g

    static member Lic
        (
            g: VectorGrid<1, 1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit
        ) : Figure =
        Field.lic (U.erase xUnit) (U.erase yUnit) g

    static member LicByWith
        (
            options: LicOptions,
            g: VectorGrid<1, 1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] valueUnit: IUnit
        ) : Figure =
        Field.licByWith options (U.erase xUnit) (U.erase yUnit) (U.erase valueUnit) g

    static member LicBy
        (
            g: VectorGrid<1, 1, 1, 1>,
            [<Optional; DefaultParameterValue(null: IUnit)>] xUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] yUnit: IUnit,
            [<Optional; DefaultParameterValue(null: IUnit)>] valueUnit: IUnit
        ) : Figure =
        Field.licBy (U.erase xUnit) (U.erase yUnit) (U.erase valueUnit) g

