namespace Marey.CSharp

open System
open Marey

/// D124 — **fields from C#**: grids built from delegates, at measure 1.
///
/// F# samples a field with a function, `ScalarGrid.sample xs ys (fun x y -> …)`, and C# cannot
/// pass an F# function without building one by hand. Each member here takes a `Func` instead and
/// is one line over the module function it names. The grids are F#'s own types at measure 1, so
/// they go straight to `Field.Heatmap(g, xUnit: …)` and the rest of the unit-optional members.
[<AbstractClass; Sealed>]
type Grids =

    /// `count` nodes from `lo` to `hi`, evenly spaced.
    static member Linear(lo: float, hi: float, count: int) : float[] = Grid.linear lo hi count

    /// `count` nodes from `lo` to `hi`, evenly spaced in the logarithm.
    static member Log(lo: float, hi: float, count: int) : float[] = Grid.log lo hi count

    /// A scalar field sampled at every node: `f(x, y)`.
    static member Scalar(xs: float[], ys: float[], f: Func<float, float, float>) : ScalarGrid<1, 1, 1> =
        ScalarGrid.sample xs ys (fun x y -> f.Invoke(x, y))

    /// A vector field sampled at every node: `f(x, y)` is the pair `(u, w)`.
    static member Vector(xs: float[], ys: float[], f: Func<float, float, struct (float * float)>) : VectorGrid<1, 1, 1, 1> =
        VectorGrid.sample xs ys (fun x y -> f.Invoke(x, y))

    static member Gradient(g: ScalarGrid<1, 1, 1>) : VectorGrid<1, 1, 1, 1> = Field.grad g

    static member Divergence(g: VectorGrid<1, 1, 1, 1>) : ScalarGrid<1, 1, 1> = Field.div g

    static member Curl(g: VectorGrid<1, 1, 1, 1>) : ScalarGrid<1, 1, 1> = Field.curl g

    static member Laplacian(g: ScalarGrid<1, 1, 1>) : ScalarGrid<1, 1, 1> = Field.laplacian g

    /// The fixed points of a flow, classified — for `Phase.Marks` and `Phase.Describe`.
    static member FixedPoints(g: VectorGrid<1, 1, 1, 1>) : FixedPoint<1, 1, 1>[] = Phase.fixedPoints g
