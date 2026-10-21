namespace FSharp

namespace Marey

    type ScalarGrid<[<Measure>] 'x,[<Measure>] 'y,[<Measure>] 'v> =
        {

          Xs: float<'x> array

          Ys: float<'y> array
          Values: float<'v> array
        }

    type VectorGrid<[<Measure>] 'x,[<Measure>] 'y,[<Measure>] 'u,[<Measure>] 'w> =
        {
          Xs: float<'x> array
          Ys: float<'y> array
          U: float<'u> array
          W: float<'w> array
        }

    [<AutoOpen>]
    module GridAxes =

        [<RequireQualifiedAccess>]
        module Grid =

            [<Literal>]
            val DefaultCount: int = 64

            val linear:
              lo: float<'x> -> hi: float<'x> -> count: int -> float<'x> array

            val log:
              lo: float<'x> -> hi: float<'x> -> count: int -> float<'x> array

    [<RequireQualifiedAccess>]
    module ScalarGrid =

        val create:
          xs: float<'x> array ->
            ys: float<'y> array ->
            values: float<'v> array -> ScalarGrid<'x,'y,'v>

        val sample:
          xs: float<'x> array ->
            ys: float<'y> array ->
            f: (float<'x> -> float<'y> -> float<'v>) -> ScalarGrid<'x,'y,'v>

        val at:
          g: ScalarGrid<'x,'y,'v> -> x: float<'x> -> y: float<'y> -> float<'v>

        val missing: g: ScalarGrid<'x,'y,'v> -> int

        val range: g: ScalarGrid<'x,'y,'v> -> (float<'v> * float<'v>) option

        val map:
          f: (float<'v> -> float<'r>) ->
            g: ScalarGrid<'x,'y,'v> -> ScalarGrid<'x,'y,'r>

    [<RequireQualifiedAccess>]
    module VectorGrid =

        val create:
          xs: float<'x> array ->
            ys: float<'y> array ->
            u: float<'u> array -> w: float<'w> array -> VectorGrid<'x,'y,'u,'w>

        val sample:
          xs: float<'x> array ->
            ys: float<'y> array ->
            f: (float<'x> -> float<'y> -> struct (float<'u> * float<'w>)) ->
            VectorGrid<'x,'y,'u,'w>

        val at:
          g: VectorGrid<'x,'y,'u,'w> ->
            x: float<'x> -> y: float<'y> -> struct (float<'u> * float<'w>)

        val u: g: VectorGrid<'x,'y,'u,'w> -> ScalarGrid<'x,'y,'u>

        val w: g: VectorGrid<'x,'y,'u,'w> -> ScalarGrid<'x,'y,'w>

        val magnitude: g: VectorGrid<'x,'y,'v,'v> -> ScalarGrid<'x,'y,'v>

        val missing: g: VectorGrid<'x,'y,'u,'w> -> int

namespace Marey

    type Flow<[<Measure>] 'x,[<Measure>] 'y,[<Measure>] 't> =
        float<'x> -> float<'y> -> struct (float<'x/'t> * float<'y/'t>)

    type Field2<[<Measure>] 'x,[<Measure>] 'v> = Vec2<'x> -> Vec2<'v>

    type Scalar2<[<Measure>] 'x,[<Measure>] 'v> = Vec2<'x> -> float<'v>

    type Jacobian<[<Measure>] 'x,[<Measure>] 'y,[<Measure>] 'u,[<Measure>] 'w> =
        {
          DuDx: ScalarGrid<'x,'y,'u/'x>
          DuDy: ScalarGrid<'x,'y,'u/'y>
          DwDx: ScalarGrid<'x,'y,'w/'x>
          DwDy: ScalarGrid<'x,'y,'w/'y>
        }

    [<RequireQualifiedAccess>]
    module Field =

        val sampleScalar:
          xs: float<'x> array ->
            ys: float<'x> array -> f: Scalar2<'x,'v> -> ScalarGrid<'x,'x,'v>

        val sample:
          xs: float<'x> array ->
            ys: float<'x> array -> f: Field2<'x,'v> -> VectorGrid<'x,'x,'v,'v>

        val sampleFlow:
          xs: float<'x> array ->
            ys: float<'y> array ->
            f: Flow<'x,'y,'t> -> VectorGrid<'x,'y,'x/'t,'y/'t>

        val grad: g: ScalarGrid<'x,'y,'v> -> VectorGrid<'x,'y,'v/'x,'v/'y>

        val div: g: VectorGrid<'x,'y,'u,'u 'y/'x> -> ScalarGrid<'x,'y,'u/'x>

        val curl: g: VectorGrid<'x,'x,'v,'v> -> ScalarGrid<'x,'x,'v/'x>

        val laplacian: g: ScalarGrid<'x,'x,'v> -> ScalarGrid<'x,'x,'v/'x ^ 2>

        val jacobian: g: VectorGrid<'x,'y,'u,'w> -> Jacobian<'x,'y,'u,'w>

        [<Literal>]
        val DefaultSeparation: float = 16

        val licDefaults: LicOptions

    [<AbstractClass; Sealed; Class>]
    type Field =

        static member
          Bands: g: ScalarGrid<1,1,1> *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] xUnit: IUnit *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] yUnit: IUnit *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] valueUnit: IUnit -> Figure

        static member
          BandsWith: ramp: Ramp * levels: float array * g: ScalarGrid<1,1,1> *
                     [<System.Runtime.InteropServices.Optional;
                       System.Runtime.InteropServices.DefaultParameterValue
                       ((null :> obj))>] xUnit: IUnit *
                     [<System.Runtime.InteropServices.Optional;
                       System.Runtime.InteropServices.DefaultParameterValue
                       ((null :> obj))>] yUnit: IUnit *
                     [<System.Runtime.InteropServices.Optional;
                       System.Runtime.InteropServices.DefaultParameterValue
                       ((null :> obj))>] valueUnit: IUnit -> Figure

        static member
          Contours: g: ScalarGrid<1,1,1> *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] xUnit: IUnit *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          ContoursAt: levels: float array * g: ScalarGrid<1,1,1> *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] xUnit: IUnit *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Directions: g: VectorGrid<1,1,1,1> *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] xUnit: IUnit *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Heatmap: g: ScalarGrid<1,1,1> *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] xUnit: IUnit *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] yUnit: IUnit *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] valueUnit: IUnit -> Figure

        static member
          HeatmapWith: ramp: Ramp * smooth: bool * g: ScalarGrid<1,1,1> *
                       [<System.Runtime.InteropServices.Optional;
                         System.Runtime.InteropServices.DefaultParameterValue
                         ((null :> obj))>] xUnit: IUnit *
                       [<System.Runtime.InteropServices.Optional;
                         System.Runtime.InteropServices.DefaultParameterValue
                         ((null :> obj))>] yUnit: IUnit *
                       [<System.Runtime.InteropServices.Optional;
                         System.Runtime.InteropServices.DefaultParameterValue
                         ((null :> obj))>] valueUnit: IUnit -> Figure

        static member
          Lic: g: VectorGrid<1,1,1,1> *
               [<System.Runtime.InteropServices.Optional;
                 System.Runtime.InteropServices.DefaultParameterValue
                 ((null :> obj))>] xUnit: IUnit *
               [<System.Runtime.InteropServices.Optional;
                 System.Runtime.InteropServices.DefaultParameterValue
                 ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          LicBy: g: VectorGrid<1,1,1,1> *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] xUnit: IUnit *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] yUnit: IUnit *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] valueUnit: IUnit -> Figure

        static member
          LicByWith: options: LicOptions * g: VectorGrid<1,1,1,1> *
                     [<System.Runtime.InteropServices.Optional;
                       System.Runtime.InteropServices.DefaultParameterValue
                       ((null :> obj))>] xUnit: IUnit *
                     [<System.Runtime.InteropServices.Optional;
                       System.Runtime.InteropServices.DefaultParameterValue
                       ((null :> obj))>] yUnit: IUnit *
                     [<System.Runtime.InteropServices.Optional;
                       System.Runtime.InteropServices.DefaultParameterValue
                       ((null :> obj))>] valueUnit: IUnit -> Figure

        static member
          LicWith: options: LicOptions * g: VectorGrid<1,1,1,1> *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] xUnit: IUnit *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Quiver: g: VectorGrid<1,1,1,1> *
                  [<System.Runtime.InteropServices.Optional;
                    System.Runtime.InteropServices.DefaultParameterValue
                    ((null :> obj))>] xUnit: IUnit *
                  [<System.Runtime.InteropServices.Optional;
                    System.Runtime.InteropServices.DefaultParameterValue
                    ((null :> obj))>] yUnit: IUnit *
                  [<System.Runtime.InteropServices.Optional;
                    System.Runtime.InteropServices.DefaultParameterValue
                    ((null :> obj))>] valueUnit: IUnit -> Figure

        static member
          QuiverBy: g: VectorGrid<1,1,1,1> *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] xUnit: IUnit *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] yUnit: IUnit *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] valueUnit: IUnit -> Figure

        static member
          Streamlines: g: VectorGrid<1,1,1,1> *
                       [<System.Runtime.InteropServices.Optional;
                         System.Runtime.InteropServices.DefaultParameterValue
                         ((null :> obj))>] xUnit: IUnit *
                       [<System.Runtime.InteropServices.Optional;
                         System.Runtime.InteropServices.DefaultParameterValue
                         ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          StreamlinesBy: g: VectorGrid<1,1,1,1> *
                         [<System.Runtime.InteropServices.Optional;
                           System.Runtime.InteropServices.DefaultParameterValue
                           ((null :> obj))>] xUnit: IUnit *
                         [<System.Runtime.InteropServices.Optional;
                           System.Runtime.InteropServices.DefaultParameterValue
                           ((null :> obj))>] yUnit: IUnit *
                         [<System.Runtime.InteropServices.Optional;
                           System.Runtime.InteropServices.DefaultParameterValue
                           ((null :> obj))>] valueUnit: IUnit -> Figure

        static member
          StreamlinesWith: separation: float<px> * g: VectorGrid<1,1,1,1> *
                           [<System.Runtime.InteropServices.Optional;
                             System.Runtime.InteropServices.DefaultParameterValue
                             ((null :> obj))>] xUnit: IUnit *
                           [<System.Runtime.InteropServices.Optional;
                             System.Runtime.InteropServices.DefaultParameterValue
                             ((null :> obj))>] yUnit: IUnit -> Figure

namespace Marey

    [<RequireQualifiedAccess>]
    type FixedPointKind =

        | Saddle

        | Node of stable: bool

        | Focus of stable: bool

        | Centre

        | Degenerate

    [<Struct>]
    type Eigenvalue<[<Measure>] 't> =
        {
          Re: float</'t>
          Im: float</'t>
        }

    type FixedPoint<[<Measure>] 'x,[<Measure>] 'y,[<Measure>] 't> =
        {
          X: float<'x>
          Y: float<'y>
          Kind: FixedPointKind

          Trace: float</'t>

          Determinant: float</'t ^ 2>

          Eigenvalues: Eigenvalue<'t> * Eigenvalue<'t>
        }

    [<RequireQualifiedAccess>]
    module FixedPoint =

        val kindName: kind: FixedPointKind -> string

    [<RequireQualifiedAccess>]
    module Phase =

        [<Literal>]
        val CentreTolerance: float = 0.001

        val classify:
          centreTolerance: float ->
            trace: float</'t> ->
            det: float</'t ^ 2> ->
            FixedPointKind * (Eigenvalue<'t> * Eigenvalue<'t>)

        val fixedPointsWith:
          centreTolerance: float ->
            g: VectorGrid<'x,'y,'x/'t,'y/'t> -> FixedPoint<'x,'y,'t> array

        val fixedPoints:
          g: VectorGrid<'x,'y,'x/'t,'y/'t> -> FixedPoint<'x,'y,'t> array

    [<AbstractClass; Sealed; Class>]
    type Phase =

        static member
          Describe: p: FixedPoint<1,1,1> *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] xUnit: IUnit *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] yUnit: IUnit *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] timeUnit: IUnit -> string

        static member
          Marks: points: FixedPoint<1,1,1> array *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] xUnit: IUnit *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] yUnit: IUnit -> Figure option

        static member
          Nullclines: g: VectorGrid<1,1,1,1> *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] xUnit: IUnit *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Portrait: g: VectorGrid<1,1,1,1> *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] xUnit: IUnit *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          PortraitWith: separation: float<px> * g: VectorGrid<1,1,1,1> *
                        [<System.Runtime.InteropServices.Optional;
                          System.Runtime.InteropServices.DefaultParameterValue
                          ((null :> obj))>] xUnit: IUnit *
                        [<System.Runtime.InteropServices.Optional;
                          System.Runtime.InteropServices.DefaultParameterValue
                          ((null :> obj))>] yUnit: IUnit -> Figure

namespace Marey.UnitsOfMeasure

    [<RequireQualifiedAccess>]
    module Field =

        val heatmapWith:
          ramp: Marey.Ramp ->
            smooth: bool ->
            ux: Marey.U<'x> ->
            uy: Marey.U<'y> ->
            uv: Marey.U<'v> -> g: Marey.ScalarGrid<'x,'y,'v> -> Marey.Figure

        val heatmap:
          ux: Marey.U<'x> ->
            uy: Marey.U<'y> ->
            uv: Marey.U<'v> -> g: Marey.ScalarGrid<'x,'y,'v> -> Marey.Figure

        val contoursAt:
          levels: float<'v> array ->
            ux: Marey.U<'x> ->
            uy: Marey.U<'y> -> g: Marey.ScalarGrid<'x,'y,'v> -> Marey.Figure

        val contours:
          ux: Marey.U<'x> ->
            uy: Marey.U<'y> -> g: Marey.ScalarGrid<'x,'y,'v> -> Marey.Figure

        val bandsWith:
          ramp: Marey.Ramp ->
            levels: float<'v> array ->
            ux: Marey.U<'x> ->
            uy: Marey.U<'y> ->
            uv: Marey.U<'v> -> g: Marey.ScalarGrid<'x,'y,'v> -> Marey.Figure

        val bands:
          ux: Marey.U<'x> ->
            uy: Marey.U<'y> ->
            uv: Marey.U<'v> -> g: Marey.ScalarGrid<'x,'y,'v> -> Marey.Figure

        val quiver:
          ux: Marey.U<'x> ->
            uy: Marey.U<'y> ->
            uv: Marey.U<'v> -> g: Marey.VectorGrid<'x,'y,'v,'v> -> Marey.Figure

        val quiverBy:
          ux: Marey.U<'x> ->
            uy: Marey.U<'y> ->
            uv: Marey.U<'v> -> g: Marey.VectorGrid<'x,'y,'v,'v> -> Marey.Figure

        val directions:
          ux: Marey.U<'x> ->
            uy: Marey.U<'y> -> g: Marey.VectorGrid<'x,'y,'u,'w> -> Marey.Figure

        val streamlinesWith:
          separation: float<Marey.px> ->
            ux: Marey.U<'x> ->
            uy: Marey.U<'y> -> g: Marey.VectorGrid<'x,'y,'u,'w> -> Marey.Figure

        val streamlines:
          ux: Marey.U<'x> ->
            uy: Marey.U<'y> -> g: Marey.VectorGrid<'x,'y,'u,'w> -> Marey.Figure

        val streamlinesBy:
          ux: Marey.U<'x> ->
            uy: Marey.U<'y> ->
            uv: Marey.U<'v> -> g: Marey.VectorGrid<'x,'y,'v,'v> -> Marey.Figure

        val licWith:
          options: Marey.LicOptions ->
            ux: Marey.U<'x> ->
            uy: Marey.U<'y> -> g: Marey.VectorGrid<'x,'y,'u,'w> -> Marey.Figure

        val lic:
          ux: Marey.U<'x> ->
            uy: Marey.U<'y> -> g: Marey.VectorGrid<'x,'y,'u,'w> -> Marey.Figure

        val licByWith:
          options: Marey.LicOptions ->
            ux: Marey.U<'x> ->
            uy: Marey.U<'y> ->
            uv: Marey.U<'v> -> g: Marey.VectorGrid<'x,'y,'v,'v> -> Marey.Figure

        val licBy:
          ux: Marey.U<'x> ->
            uy: Marey.U<'y> ->
            uv: Marey.U<'v> -> g: Marey.VectorGrid<'x,'y,'v,'v> -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.heatmapWith. Field.HeatmapWith(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val HeatmapWith: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.heatmap. Field.Heatmap(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Heatmap: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.contoursAt. Field.ContoursAt(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val ContoursAt: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.contours. Field.Contours(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Contours: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.bandsWith. Field.BandsWith(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val BandsWith: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.bands. Field.Bands(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Bands: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.quiver. Field.Quiver(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Quiver: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.quiverBy. Field.QuiverBy(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val QuiverBy: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.directions. Field.Directions(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Directions: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.streamlinesWith. Field.StreamlinesWith(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val StreamlinesWith: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.streamlines. Field.Streamlines(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Streamlines: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.streamlinesBy. Field.StreamlinesBy(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val StreamlinesBy: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.licWith. Field.LicWith(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val LicWith: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.lic. Field.Lic(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Lic: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.licByWith. Field.LicByWith(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val LicByWith: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.licBy. Field.LicBy(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val LicBy: obj -> Marey.Figure

    [<RequireQualifiedAccess>]
    module Phase =

        val nullclines:
          ux: Marey.U<'x> ->
            uy: Marey.U<'y> -> g: Marey.VectorGrid<'x,'y,'u,'w> -> Marey.Figure

        val marks:
          ux: Marey.U<'x> ->
            uy: Marey.U<'y> ->
            points: Marey.FixedPoint<'x,'y,'t> array -> Marey.Figure option

        val portraitWith:
          separation: float<Marey.px> ->
            ux: Marey.U<'x> ->
            uy: Marey.U<'y> ->
            g: Marey.VectorGrid<'x,'y,'x/'t,'y/'t> -> Marey.Figure

        val portrait:
          ux: Marey.U<'x> ->
            uy: Marey.U<'y> ->
            g: Marey.VectorGrid<'x,'y,'x/'t,'y/'t> -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Phase.nullclines. Phase.Nullclines(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Nullclines: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Phase.marks. Phase.Marks(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Marks: obj -> Marey.Figure option

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Phase.portraitWith. Phase.PortraitWith(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val PortraitWith: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Phase.portrait. Phase.Portrait(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Portrait: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so this takes a witness per unit: FixedPoint.describe. Phase.Describe(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Describe: obj -> string

    [<RequireQualifiedAccess>]
    module FixedPoint =

        val describe:
          ux: Marey.U<'x> ->
            uy: Marey.U<'y> ->
            ut: Marey.U<'t> -> p: Marey.FixedPoint<'x,'y,'t> -> string

namespace Marey.CSharp

    [<AbstractClass; Sealed; Class>]
    type Grids =

        static member
          Curl: g: Marey.VectorGrid<1,1,1,1> -> Marey.ScalarGrid<1,1,1>

        static member
          Divergence: g: Marey.VectorGrid<1,1,1,1> -> Marey.ScalarGrid<1,1,1>

        static member
          FixedPoints: g: Marey.VectorGrid<1,1,1,1> ->
                         Marey.FixedPoint<1,1,1> array

        static member
          Gradient: g: Marey.ScalarGrid<1,1,1> -> Marey.VectorGrid<1,1,1,1>

        static member
          Laplacian: g: Marey.ScalarGrid<1,1,1> -> Marey.ScalarGrid<1,1,1>

        static member Linear: lo: float * hi: float * count: int -> float array

        static member Log: lo: float * hi: float * count: int -> float array

        static member
          Scalar: xs: float array * ys: float array *
                  f: System.Func<float,float,float> -> Marey.ScalarGrid<1,1,1>

        static member
          Vector: xs: float array * ys: float array *
                  f: System.Func<float,float,struct (float * float)> ->
                    Marey.VectorGrid<1,1,1,1>
