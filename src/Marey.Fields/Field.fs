namespace Marey

// ======================================================================================
// D84 — §8.1'S FIELDS, AND THE ONE §8.1 COULD NOT TYPE.
//
// §8.1 declares `Field2<'x,'v> = Vec2<'x> -> Vec2<'v>` and then lists phase portraits among the
// things to draw with it. A phase space has a different unit on each axis — a pendulum lives in
// (θ: rad, ω: rad/s) — and `Vec2<'x>` has one measure for both components, so §8.1's type cannot
// hold one. `Flow` can: its axes are independent, and its components are the time derivatives of
// its coordinates.
//
// As built, the function types are only the *inputs*. A field is used as a grid (D85), and one
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
/// sums and products the units do agree on (D92).
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
