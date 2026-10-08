namespace Marey

open System

// ======================================================================================
// D85 — A FIELD IS SAMPLED ONTO A GRID, AND THE GRID IS THE REPRESENTATION.
//
// Every field algorithm in this package reads values at points it chooses: marching squares at
// grid nodes, RK4 at stage points, a line-integral convolution along every pixel's streamline. A
// field can arrive as a function, which is exact anywhere and of unknown cost, or as a grid,
// which is how real data arrives — a simulation's output, a file, every solver M5 will ship. One
// input type for every geom means the grid: a function is sampled into one, once, at a stated
// resolution, and a grid from data is used as it is.
//
// **Rectilinear, not uniform.** The node coordinates are arrays, strictly increasing, and need
// not be evenly spaced. That is what lets a function be sampled uniformly in *panel* space on a
// log axis (`Grid.log`) rather than crowding most of its samples into the top decade, and what
// lets gridded data with uneven spacing in, unresampled.
//
// **Two shapes, and the units are in the types.** A scalar grid holds one value per node in
// `'v`. A vector grid holds two components, `'u` along x and `'w` along y, and the four measure
// parameters are independent: a spatial field is `VectorGrid<m, m, V/m, V/m>` and a pendulum's
// phase flow is `VectorGrid<rad, rad/s, rad/s, rad/s²>`. Which derived quantities exist is then
// a question the type system answers — see `Field.div` and `Field.curl` (D84).
//
// **Values that are not values** (D52). A point charge's field is infinite at the charge. A
// sample that is not finite is stored as it is, is not drawn, and is counted: `missing`.
// ======================================================================================

/// Values on the nodes of a rectilinear grid.
///
/// `Values.[j * Xs.Length + i]` is the value at `(Xs.[i], Ys.[j])` — row-major, x varying
/// fastest. Build one with `ScalarGrid.create` or `ScalarGrid.sample`, which check the shape.
type ScalarGrid<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v> =
    { /// Node x coordinates, strictly increasing, at least two.
      Xs: float<'x>[]
      /// Node y coordinates, strictly increasing, at least two.
      Ys: float<'y>[]
      Values: float<'v>[] }

/// Two components on the nodes of a rectilinear grid: `U` along x, in `'u`, and `W` along y, in
/// `'w`. Laid out as `ScalarGrid.Values` is.
type VectorGrid<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w> =
    { Xs: float<'x>[]
      Ys: float<'y>[]
      U: float<'u>[]
      W: float<'w>[] }

/// D124 — inside a module F# opens with the namespace, so `Grid.linear` reads as it always has
/// while the CLR name, `Marey.GridAxes+Grid`, stops colliding with Marey.Plot's layout type
/// `Marey.Grid`, which C# could not tell apart from it (CS0433). See `SiWitnesses` in Marey.Units.
[<AutoOpen>]
module GridAxes =

    /// Node coordinates for one grid axis.
    [<RequireQualifiedAccess>]
    module Grid =

        /// The resolution a function is sampled at when nobody states one: 64 nodes a side. Fixed,
        /// not derived from the panel's pixel size, so the same figure at two sizes shows the same
        /// field (D85).
        [<Literal>]
        let DefaultCount = 64

        /// `count` nodes evenly spaced from `lo` to `hi`, both included. Uniform in panel space on a
        /// linear axis.
        let linear<[<Measure>] 'x> (lo: float<'x>) (hi: float<'x>) (count: int) : float<'x>[] =
            if count < 2 then
                invalidArg "count" (sprintf "Marey: a grid axis needs at least two nodes, not %d." count)

            if not (hi > lo) then
                invalidArg "hi" "Marey: a grid axis needs hi > lo."

            Array.init count (fun i ->
                if i = count - 1 then hi
                else lo + (hi - lo) * (float i / float (count - 1)))

        /// `count` nodes evenly spaced in log from `lo` to `hi`, both positive and both included.
        /// Uniform in panel space on a log axis, of any base.
        let log<[<Measure>] 'x> (lo: float<'x>) (hi: float<'x>) (count: int) : float<'x>[] =
            if count < 2 then
                invalidArg "count" (sprintf "Marey: a grid axis needs at least two nodes, not %d." count)

            if not (float lo > 0.0 && hi > lo) then
                invalidArg "lo" "Marey: a log grid axis needs 0 < lo < hi."

            let a = Math.Log(float lo)
            let b = Math.Log(float hi)

            Array.init count (fun i ->
                if i = 0 then lo
                elif i = count - 1 then hi
                else LanguagePrimitives.FloatWithMeasure<'x>(Math.Exp(a + (b - a) * (float i / float (count - 1)))))

        /// Check an axis: at least two nodes, finite, strictly increasing.
        let internal check (name: string) (xs: float[]) =
            if xs.Length < 2 then
                invalidArg name (sprintf "Marey: a grid axis needs at least two nodes; %s has %d." name xs.Length)

            for i in 0 .. xs.Length - 1 do
                if Double.IsNaN xs.[i] || Double.IsInfinity xs.[i] then
                    invalidArg name (sprintf "Marey: %s.[%d] is not finite." name i)

                if i > 0 && not (xs.[i] > xs.[i - 1]) then
                    invalidArg name (sprintf "Marey: %s must be strictly increasing; %s.[%d] is not." name name i)

        /// The cell `[xs.[k], xs.[k+1]]` holding `x`, and the fraction across it; `None` outside the
        /// axis. Binary search rather than `Array.BinarySearch`, which Fable does not carry.
        let internal locate (xs: float[]) (x: float) : struct (int * float) voption =
            let n = xs.Length

            if Double.IsNaN x || x < xs.[0] || x > xs.[n - 1] then
                ValueNone
            else
                let mutable lo = 0
                let mutable hi = n - 1

                while hi - lo > 1 do
                    let mid = (lo + hi) / 2

                    if xs.[mid] <= x then lo <- mid else hi <- mid

                ValueSome(struct (lo, (x - xs.[lo]) / (xs.[lo + 1] - xs.[lo])))

        let inline internal raw (xs: float<'u>[]) : float[] = xs |> Array.map float

        let inline internal tagAll<[<Measure>] 'u> (xs: float[]) : float<'u>[] =
            xs |> Array.map LanguagePrimitives.FloatWithMeasure<'u>

        let internal isMissing (v: float) = Double.IsNaN v || Double.IsInfinity v

        /// Bilinear read of row-major `values` on the grid `xs` × `ys`. NaN outside the grid: a
        /// field is not extrapolated past where it was sampled.
        let internal bilinear (xs: float[]) (ys: float[]) (values: float[]) (x: float) (y: float) : float =
            match locate xs x, locate ys y with
            | ValueSome(struct (i, fx)), ValueSome(struct (j, fy)) ->
                let nx = xs.Length
                let v00 = values.[j * nx + i]
                let v10 = values.[j * nx + i + 1]
                let v01 = values.[(j + 1) * nx + i]
                let v11 = values.[(j + 1) * nx + i + 1]
                let a = v00 + (v10 - v00) * fx
                let b = v01 + (v11 - v01) * fx
                a + (b - a) * fy
            | _ -> nan

[<RequireQualifiedAccess>]
module ScalarGrid =

    /// A grid from data. Checks that both axes are strictly increasing and that there is one
    /// value per node.
    let create<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (xs: float<'x>[])
        (ys: float<'y>[])
        (values: float<'v>[])
        : ScalarGrid<'x, 'y, 'v> =
        Grid.check "xs" (Grid.raw xs)
        Grid.check "ys" (Grid.raw ys)

        if values.Length <> xs.Length * ys.Length then
            invalidArg
                "values"
                (sprintf "Marey: a %d × %d grid has %d nodes, but %d values were given." xs.Length ys.Length (xs.Length * ys.Length) values.Length)

        { Xs = Array.copy xs
          Ys = Array.copy ys
          Values = Array.copy values }

    /// Sample a function of two coordinates at every node. The function is called once per
    /// node, in row-major order.
    let sample<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (xs: float<'x>[])
        (ys: float<'y>[])
        (f: float<'x> -> float<'y> -> float<'v>)
        : ScalarGrid<'x, 'y, 'v> =
        Grid.check "xs" (Grid.raw xs)
        Grid.check "ys" (Grid.raw ys)
        let nx = xs.Length

        { Xs = Array.copy xs
          Ys = Array.copy ys
          Values = Array.init (nx * ys.Length) (fun k -> f xs.[k % nx] ys.[k / nx]) }

    /// The value at `(x, y)`, bilinear between nodes, exact on them, NaN outside the grid.
    let at<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (g: ScalarGrid<'x, 'y, 'v>)
        (x: float<'x>)
        (y: float<'y>)
        : float<'v> =
        LanguagePrimitives.FloatWithMeasure<'v>(Grid.bilinear (Grid.raw g.Xs) (Grid.raw g.Ys) (Grid.raw g.Values) (float x) (float y))

    /// How many nodes hold a value that is not finite (D52): not drawn, and reported.
    let missing<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v> (g: ScalarGrid<'x, 'y, 'v>) : int =
        g.Values |> Array.sumBy (fun v -> if Grid.isMissing (float v) then 1 else 0)

    /// The smallest and largest finite values; `None` when there are none.
    let range<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v> (g: ScalarGrid<'x, 'y, 'v>) : (float<'v> * float<'v>) option =
        let finite = g.Values |> Array.filter (fun v -> not (Grid.isMissing (float v)))

        if finite.Length = 0 then None else Some(Array.min finite, Array.max finite)

    /// Apply a function to every value, keeping the nodes.
    let map<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v, [<Measure>] 'r>
        (f: float<'v> -> float<'r>)
        (g: ScalarGrid<'x, 'y, 'v>)
        : ScalarGrid<'x, 'y, 'r> =
        { Xs = g.Xs
          Ys = g.Ys
          Values = Array.map f g.Values }

[<RequireQualifiedAccess>]
module VectorGrid =

    /// A grid from data: one `u` and one `w` per node.
    let create<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (xs: float<'x>[])
        (ys: float<'y>[])
        (u: float<'u>[])
        (w: float<'w>[])
        : VectorGrid<'x, 'y, 'u, 'w> =
        Grid.check "xs" (Grid.raw xs)
        Grid.check "ys" (Grid.raw ys)
        let n = xs.Length * ys.Length

        if u.Length <> n || w.Length <> n then
            invalidArg
                "u"
                (sprintf "Marey: a %d × %d grid has %d nodes, but %d u and %d w values were given." xs.Length ys.Length n u.Length w.Length)

        { Xs = Array.copy xs
          Ys = Array.copy ys
          U = Array.copy u
          W = Array.copy w }

    /// Sample a function returning both components at every node, called once per node in
    /// row-major order.
    let sample<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (xs: float<'x>[])
        (ys: float<'y>[])
        (f: float<'x> -> float<'y> -> struct (float<'u> * float<'w>))
        : VectorGrid<'x, 'y, 'u, 'w> =
        Grid.check "xs" (Grid.raw xs)
        Grid.check "ys" (Grid.raw ys)
        let nx = xs.Length
        let n = nx * ys.Length
        let u = Array.zeroCreate n
        let w = Array.zeroCreate n

        for k in 0 .. n - 1 do
            let struct (a, b) = f xs.[k % nx] ys.[k / nx]
            u.[k] <- a
            w.[k] <- b

        { Xs = Array.copy xs
          Ys = Array.copy ys
          U = u
          W = w }

    /// Both components at `(x, y)`, bilinear between nodes, NaN outside the grid.
    let at<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        (x: float<'x>)
        (y: float<'y>)
        : struct (float<'u> * float<'w>) =
        let xs = Grid.raw g.Xs
        let ys = Grid.raw g.Ys

        struct (LanguagePrimitives.FloatWithMeasure<'u>(Grid.bilinear xs ys (Grid.raw g.U) (float x) (float y)),
                LanguagePrimitives.FloatWithMeasure<'w>(Grid.bilinear xs ys (Grid.raw g.W) (float x) (float y)))

    /// The x component as a scalar grid.
    let u<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w> (g: VectorGrid<'x, 'y, 'u, 'w>) : ScalarGrid<'x, 'y, 'u> =
        { Xs = g.Xs; Ys = g.Ys; Values = g.U }

    /// The y component as a scalar grid.
    let w<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w> (g: VectorGrid<'x, 'y, 'u, 'w>) : ScalarGrid<'x, 'y, 'w> =
        { Xs = g.Xs; Ys = g.Ys; Values = g.W }

    /// The length of the vector at every node — defined only when both components are in one
    /// unit, which is what the signature says. A phase flow's (rad/s, rad/s²) has no length.
    let magnitude<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v> (g: VectorGrid<'x, 'y, 'v, 'v>) : ScalarGrid<'x, 'y, 'v> =
        { Xs = g.Xs
          Ys = g.Ys
          Values = Array.map2 (fun (a: float<'v>) (b: float<'v>) -> sqrt (a * a + b * b)) g.U g.W }

    /// How many nodes have a component that is not finite (D52).
    let missing<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w> (g: VectorGrid<'x, 'y, 'u, 'w>) : int =
        Array.fold2
            (fun acc (a: float<'u>) (b: float<'w>) -> if Grid.isMissing (float a) || Grid.isMissing (float b) then acc + 1 else acc)
            0
            g.U
            g.W
