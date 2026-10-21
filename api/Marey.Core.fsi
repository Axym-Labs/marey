namespace Marey

    [<RequireQualifiedAccess>]
    module Num =

        val roundAway: x: float -> float

        val roundEven: x: float -> float

        [<Literal>]
        val MaxDecimals: int = 6

        val fixedPoint: decimals: int -> x: float -> string

        val at: decimals: int -> v: float -> string

        val shortest: x: float -> string

        val superscript: n: int -> string

namespace Marey

    [<Struct>]
    type Rng =
        { State: uint64 }

    [<RequireQualifiedAccess>]
    module Rng =

        val seeded: seed: uint64 -> Rng

        val next: r: Rng -> struct (uint64 * Rng)

        val unit: r: Rng -> struct (float * Rng)

        val below: n: int -> r: Rng -> struct (int * Rng)

        val units: count: int -> r: Rng -> float array * Rng

namespace Marey

    [<RequireQualifiedAccess>]
    module Tex =

        val tryEscapeChar: c: char -> string option

        val escape: s: string -> string

        val isLiteralSafe: s: string -> bool

namespace Marey

    [<Measure>]
    type px

    [<Measure>]
    type nd

    [<Measure>]
    type pt

    [<Measure>]
    type mm

    [<Measure>]
    type rad

    [<Measure>]
    type deg

    module Space =

        val mmPerPt: float<mm/pt>

        val ptPerMm: float<pt/mm>

        val inline mmOfPt: x: float<pt> -> float<mm>

        val inline ptOfMm: x: float<mm> -> float<pt>

        val inline pxOfPt: dpi: float<px/mm> -> x: float<pt> -> float<px>

        val inline ptOfPx: dpi: float<px/mm> -> x: float<px> -> float<pt>

        val screenDpi: float<px/mm>

        val radPerDeg: float<rad/deg>

        val inline radOfDeg: a: float<deg> -> float<rad>

        val inline degOfRad: a: float<rad> -> float<deg>

        val inline ndValue: x: float<nd> -> float

        val inline nd: x: float -> float<nd>

namespace Marey

    [<CustomEquality; NoComparison; Struct>]
    type Dim =
        {

          L: int

          M: int

          T: int

          I: int

          Th: int

          N: int

          J: int
        }
        interface System.IEquatable<Dim>

        override Equals: o: obj -> bool

        override GetHashCode: unit -> int

    [<RequireQualifiedAccess>]
    module Dim =

        val zero: Dim

        val add: a: Dim -> b: Dim -> Dim

        val neg: a: Dim -> Dim

        val sub: a: Dim -> b: Dim -> Dim

        val pow: n: int -> a: Dim -> Dim

        val isDimensionless: a: Dim -> bool

        val basis: (string * (Dim -> int)) list

        val exponents: d: Dim -> (string * int) list

        val show: d: Dim -> string

        val length: Dim

        val mass: Dim

        val time: Dim

        val current: Dim

        val temperature: Dim

        val amount: Dim

        val luminosity: Dim

namespace Marey

    [<AllowNullLiteral>]
    type IUnit =

        abstract Dim: Dim

        abstract Factors: (string * int) list

        abstract OffsetSI: float

        abstract Stated: bool

        abstract Symbol: string

        abstract ToSI: float

    [<Sealed>]
    type U<[<Measure>] 'u> =
        interface IUnit

        new: symbol: string * dim: Dim * toSI: float -> U<'u>

        new: symbol: string * dim: Dim * toSI: float * offsetSI: float -> U<'u>

        new: symbol: string * dim: Dim * toSI: float * offsetSI: float *
             factors: (string * int) list -> U<'u>

        new: symbol: string * dim: Dim * toSI: float * offsetSI: float *
             factors: (string * int) list * stated: bool -> U<'u>

        override ToString: unit -> string

        member Dim: Dim

        member Factors: (string * int) list

        member IsAffine: bool

        member OffsetSI: float

        member Stated: bool

        member Symbol: string

        member ToSI: float

    [<RequireQualifiedAccess>]
    module U =

        val angleExponentOf:
          symbol: string -> factors: (string * int) list -> int

        val mul: a: U<'a> -> b: U<'b> -> U<'a 'b>

        val div: a: U<'a> -> b: U<'b> -> U<'a/'b>

        val recip: a: U<'a> -> U</'a>

        val square: a: U<'a> -> U<'a ^ 2>

        val cube: a: U<'a> -> U<'a ^ 3>

        val one: U<1>

        val unspecified: U<1>

        val erase: u: IUnit -> U<1>

        val symbol: u: U<'u> -> string

        val dim: u: U<'u> -> Dim

        val isAffine: u: U<'u> -> bool

        val scaled: newSymbol: string -> factor: float -> a: U<'a> -> U<'a>

        val toSI: u: U<'u> -> x: float<'u> -> float

        val ofSI: u: U<'u> -> siValue: float -> float<'u>

        val convert: from: U<'a> -> into: U<'b> -> x: float<'a> -> float<'b>

namespace Marey

    [<Struct>]
    type Color =
        {
          R: float
          G: float
          B: float
          A: float
        }

    [<Struct>]
    type Oklab =
        {
          L: float
          A: float
          B: float
          Alpha: float
        }

    [<Struct>]
    type Oklch =
        {
          L: float
          C: float

          H: float<rad>
          Alpha: float
        }

    [<RequireQualifiedAccess>]
    module Color =

        val rgba: r: float -> g: float -> b: float -> a: float -> Color

        val rgb: r: float -> g: float -> b: float -> Color

        val black: Color

        val white: Color

        val transparent: Color

        val withAlpha: a: float -> c: Color -> Color

        val toOklab: c: Color -> Oklab

        val ofOklab: c: Oklab -> Color

        val toOklch: c: Color -> Oklch

        val ofOklch: c: Oklch -> Color

        val mix: t: float -> a: Color -> b: Color -> Color

        val tryOfHex: s: string -> Color option

        val ofHex: s: string -> Color

        val toHex: c: Color -> string

        val relativeLuminance: c: Color -> float

        val contrastRatio: a: Color -> b: Color -> float

namespace Marey

    [<Struct>]
    type Vec2<[<Measure>] 'u> =
        {
          X: float<'u>
          Y: float<'u>
        }

        static member ( * ) : a: Vec2<'u> * k: float -> Vec2<'u>

        static member ( * ) : k: float * a: Vec2<'u> -> Vec2<'u>

        static member (+) : a: Vec2<'u> * b: Vec2<'u> -> Vec2<'u>

        static member (-) : a: Vec2<'u> * b: Vec2<'u> -> Vec2<'u>

        static member (/) : a: Vec2<'u> * k: float -> Vec2<'u>

        static member (~-) : a: Vec2<'u> -> Vec2<'u>

    [<Struct>]
    type Vec3<[<Measure>] 'u> =
        {
          X: float<'u>
          Y: float<'u>
          Z: float<'u>
        }

        static member ( * ) : a: Vec3<'u> * k: float -> Vec3<'u>

        static member ( * ) : k: float * a: Vec3<'u> -> Vec3<'u>

        static member (+) : a: Vec3<'u> * b: Vec3<'u> -> Vec3<'u>

        static member (-) : a: Vec3<'u> * b: Vec3<'u> -> Vec3<'u>

        static member (/) : a: Vec3<'u> * k: float -> Vec3<'u>

        static member (~-) : a: Vec3<'u> -> Vec3<'u>

    [<Struct>]
    type Rect<[<Measure>] 'u> =
        {
          X0: float<'u>
          Y0: float<'u>
          X1: float<'u>
          Y1: float<'u>
        }

    [<Struct>]
    type Extent<[<Measure>] 'u> =
        {
          Width: float<'u>
          Height: float<'u>
        }

    [<RequireQualifiedAccess>]
    module Vec2 =

        val inline create: x: float<'u> -> y: float<'u> -> Vec2<'u>

        val zero<[<Measure>] 'u> : Vec2<'u>

        val inline map: f: (float<'u> -> float<'v>) -> a: Vec2<'u> -> Vec2<'v>

        val scaleBy: k: float<'k> -> a: Vec2<'u> -> Vec2<'k 'u>

        val divBy: k: float<'k> -> a: Vec2<'u> -> Vec2<'u/'k>

        val dot: u: Vec2<'a> -> v: Vec2<'b> -> float<'a 'b>

        val cross: u: Vec2<'a> -> v: Vec2<'b> -> float<'a 'b>

        val normSq: a: Vec2<'u> -> float<'u ^ 2>

        val norm: a: Vec2<'u> -> float<'u>

        val normalize: a: Vec2<'u> -> Vec2<1>

        val rotate: theta: float<rad> -> a: Vec2<'u> -> Vec2<'u>

        val perp: a: Vec2<'u> -> Vec2<'u>

        val angle: a: Vec2<'u> -> float<rad>

        val lerp: t: float -> a: Vec2<'u> -> b: Vec2<'u> -> Vec2<'u>

        val ofRaw: x: float * y: float -> Vec2<'u>

        val toRaw: a: Vec2<'u> -> float * float

    [<RequireQualifiedAccess>]
    module Vec3 =

        val inline create:
          x: float<'u> -> y: float<'u> -> z: float<'u> -> Vec3<'u>

        val dot: u: Vec3<'a> -> v: Vec3<'b> -> float<'a 'b>

        val cross: u: Vec3<'a> -> v: Vec3<'b> -> Vec3<'a 'b>

        val normSq: a: Vec3<'u> -> float<'u ^ 2>

        val norm: a: Vec3<'u> -> float<'u>

        val normalize: a: Vec3<'u> -> Vec3<1>

        val lerp: t: float -> a: Vec3<'u> -> b: Vec3<'u> -> Vec3<'u>

    [<RequireQualifiedAccess>]
    module Rect =

        val inline create:
          x0: float<'u> ->
            y0: float<'u> -> x1: float<'u> -> y1: float<'u> -> Rect<'u>

        val width: r: Rect<'u> -> float<'u>

        val height: r: Rect<'u> -> float<'u>

        val area: r: Rect<'u> -> float<'u ^ 2>

        val centre: r: Rect<'u> -> Vec2<'u>

        val contains: p: Vec2<'u> -> r: Rect<'u> -> bool

        val intersects: a: Rect<'u> -> b: Rect<'u> -> bool

        val inset: d: float<'u> -> r: Rect<'u> -> Rect<'u>

        val insetBy:
          left: float<'u> ->
            top: float<'u> ->
            right: float<'u> -> bottom: float<'u> -> r: Rect<'u> -> Rect<'u>

        val unit: Rect<nd>

namespace Marey

    [<Struct>]
    type Affine<[<Measure>] 'a,[<Measure>] 'b> =
        {
          M11: float<'b/'a>
          M12: float<'b/'a>
          M21: float<'b/'a>
          M22: float<'b/'a>
          Dx: float<'b>
          Dy: float<'b>
        }

    [<RequireQualifiedAccess>]
    module Affine =

        val identity<[<Measure>] 'a> : Affine<'a,'a>

        val apply: t: Affine<'a,'b> -> p: Vec2<'a> -> Vec2<'b>

        val applyVector: t: Affine<'a,'b> -> p: Vec2<'a> -> Vec2<'b>

        val compose: f: Affine<'a,'b> -> g: Affine<'b,'c> -> Affine<'a,'c>

        val determinant: t: Affine<'a,'b> -> float<'b ^ 2/'a ^ 2>

        val tryInvert: t: Affine<'a,'b> -> Affine<'b,'a> option

        val translate: d: Vec2<'a> -> Affine<'a,'a>

        val scaleUniform: k: float<'b> -> Affine<'a,'a 'b>

        val scale: kx: float<'b> -> ky: float<'b> -> Affine<'a,'a 'b>

        val rotate: theta: float<rad> -> Affine<'a,'a>

        val rotateAbout: theta: float<rad> -> centre: Vec2<'a> -> Affine<'a,'a>

        val fromRects: src: Rect<'a> -> dst: Rect<'b> -> Affine<'a,'b>

        val mapRectBounds: t: Affine<'a,'b> -> r: Rect<'a> -> Rect<'b>

        val toRaw: t: Affine<'a,'b> -> float array

namespace Marey

    [<RequireQualifiedAccess>]
    module AffineInterp =

        [<Struct>]
        type Decomposed<[<Measure>] 'a,[<Measure>] 'b> =
            {
              Translate: Vec2<'b>
              Rotation: float<rad>
              ScaleX: float<'b/'a>
              ScaleY: float<'b/'a>

              Shear: float
            }

        val decompose: t: Affine<'a,'b> -> Decomposed<'a,'b>

        val recompose: d: Decomposed<'a,'b> -> Affine<'a,'b>

        val lerp:
          t: float -> a: Affine<'a,'b> -> b: Affine<'a,'b> -> Affine<'a,'b>

    [<Sealed; AbstractClass>]
    type Interpolate =

        static member
          inline Interp: a: Affine<'a,'b> * b: Affine<'a,'b> * t: float ->
                           Affine<'a,'b>

        static member
          inline Interp: a: Rect<'u> * b: Rect<'u> * t: float -> Rect<'u>

        static member inline Interp: a: Color * b: Color * t: float -> Color

        static member
          inline Interp: a: Vec3<'u> * b: Vec3<'u> * t: float -> Vec3<'u>

        static member
          inline Interp: a: Vec2<'u> * b: Vec2<'u> * t: float -> Vec2<'u>

        static member
          inline Interp: a: float32 * b: float32 * t: float -> float32

        static member
          inline Interp: a: float<'u> * b: float<'u> * t: float -> float<'u>

    [<AutoOpen>]
    module InterpolateOperators =

        val inline interpWith:
          a: ^T -> b: ^T -> t: float -> ^T
            when (^W or ^T) : (static member Interp: ^T * ^T * float -> ^T)

        val inline interp:
          a: ^T -> b: ^T -> t: float -> ^T
            when (Interpolate or ^T) :
                   (static member Interp: ^T * ^T * float -> ^T)

    [<RequireQualifiedAccess>]
    module Interp =

        val inline between:
          a: ^T -> b: ^T -> t: float -> ^T
            when (Interpolate or ^T) :
                   (static member Interp: ^T * ^T * float -> ^T)

        val inline stops:
          xs: (float * ^T) array -> t: float -> ^T
            when (Interpolate or ^T) :
                   (static member Interp: ^T * ^T * float -> ^T)

namespace Marey

    [<RequireQualifiedAccess>]
    module Ticks =

        val niceStep: raw: float -> float

        val decimalsFor: step: float -> int

        val linear:
          target: int -> lo: float<'u> * hi: float<'u> -> float<'u> array

        val linearStep: target: int -> lo: float<'u> * hi: float<'u> -> float

        val log10:
          target: int -> lo: float<'u> * hi: float<'u> -> float<'u> array

        val expandToNice:
          target: int -> lo: float<'u> * hi: float<'u> -> float<'u> * float<'u>

        val format: decimals: int -> x: float -> string

        val formatScientific: sigFigs: int -> x: float -> string

        val formatGeneral: x: float -> string

        [<Literal>]
        val WideLogDecades: float = 3

        val isWideLog: lo: float * hi: float -> bool

        val logInPowers: float * float -> values: float array -> bool

        val powerOfTen: x: float -> int * int

        val formatPower: x: float -> string

        val formatPowerTo: resolution: float -> x: float -> string

        val formatOwn: x: float -> string

namespace Marey

    [<RequireQualifiedAccess>]
    type ScaleKind =
        | Linear

        | Log of logBase: float

        | Symlog of logBase: float * linthresh: float

        | Power of exponent: float

        | Reciprocal

        | Ordinal of levels: string array

    type Scale<[<Measure>] 'u> =
        {

          Unit: U<'u>

          Kind: ScaleKind
          Domain: float<'u> * float<'u>
          Forward: (float<'u> -> float<nd>)
          Inverse: (float<nd> -> float<'u>)

          TickHint: (int -> float<'u> array)
        }

    [<RequireQualifiedAccess>]
    module Scale =

        val linear: u: U<'u> -> float<'u> * float<'u> -> Scale<'u>

        val logBase: b: float -> u: U<'u> -> float<'u> * float<'u> -> Scale<'u>

        val log10: u: U<'u> -> float<'u> * float<'u> -> Scale<'u>

        val log2: u: U<'u> -> float<'u> * float<'u> -> Scale<'u>

        val ln: u: U<'u> -> float<'u> * float<'u> -> Scale<'u>

        val power: p: float -> u: U<'u> -> float<'u> * float<'u> -> Scale<'u>

        val sqrt: u: U<'u> -> float<'u> * float<'u> -> Scale<'u>

        val reciprocal: u: U<'u> -> float<'u> * float<'u> -> Scale<'u>

        val symlog:
          b: float ->
            linthresh: float -> u: U<'u> -> float<'u> * float<'u> -> Scale<'u>

        val levelLabel: levels: string array -> v: float -> string

        val ordinal: levels: string array -> Scale<1>

        val ofKind:
          k: ScaleKind -> u: U<'u> -> float<'u> * float<'u> -> Scale<'u>

        val reverse: s: Scale<'u> -> Scale<'u>

        val withDomain: float<'u> * float<'u> -> s: Scale<'u> -> Scale<'u>

        val clamped: s: Scale<'u> -> Scale<'u>

        val niceDomain: target: int -> s: Scale<'u> -> Scale<'u>

        val domainOf: xs: float<'u> array -> (float<'u> * float<'u>) option

        val paddedDomain:
          fraction: float ->
            xs: float<'u> array -> (float<'u> * float<'u>) option

        val pointOf:
          fraction: float ->
            float<'u> * float<'u> -> xs: float<'u> array -> float<'u> option

        val shareDomain:
          fraction: float ->
            entries: ((float<'u> * float<'u>) * float<'u> option) array ->
            float<'u> * float<'u>

namespace Marey

    [<Struct>]
    type TickLabel =
        {
          Value: float
          Position: float<nd>

          Label: string

          LabelTex: string option
        }

    type IScaleErased =

        abstract Invert: float<nd> -> float

        abstract Project: float -> float<nd>

        abstract Ticks: int -> TickLabel array

        abstract Dim: Dim

        abstract Domain: float * float

        abstract Factors: (string * int) list

        abstract Kind: ScaleKind

        abstract OffsetSI: float

        abstract Stated: bool

        abstract Symbol: string

        abstract ToSI: float

    type Series<[<Measure>] 'ux,[<Measure>] 'uy> =
        {
          Name: string
          X: float<'ux> array
          Y: float<'uy> array
          XScale: Scale<'ux>
          YScale: Scale<'uy>
        }

    type ISeriesConsumer<'r> =

        abstract Apply: Series<'ux,'uy> -> 'r

    type PackedSeries =

        abstract Accept: ISeriesConsumer<'r> -> 'r

    type IScaleConsumer<'r> =

        abstract Apply: Scale<'u> -> 'r

    type PackedScale =

        abstract Accept: IScaleConsumer<'r> -> 'r

    type IQuantityConsumer<'r> =

        abstract Apply: U<'u> * float<'u> -> 'r

    type PackedQuantity =

        abstract Accept: IQuantityConsumer<'r> -> 'r

    type IChannelConsumer<'res> =

        abstract Apply: U<'u> * float<'u> array -> 'res

    type PackedChannel =

        abstract Accept: IChannelConsumer<'res> -> 'res

    type IChannelPairConsumer<'res> =

        abstract
          Apply: U<'ux> * float<'ux> array * U<'uy> * float<'uy> array -> 'res

    [<RequireQualifiedAccess>]
    module Erase =

        val autoScale: u: U<'u> -> xs: float<'u> array -> Scale<'u>

        val scale: s: Scale<'u> -> IScaleErased

        val series:
          name: string ->
            sx: Scale<'ux> ->
            sy: Scale<'uy> ->
            x: float<'ux> array -> y: float<'uy> array -> PackedSeries

        val packSeries: s: Series<'ux,'uy> -> PackedSeries

        val packScale: s: Scale<'u> -> PackedScale

        val packQuantity: u: U<'u> -> x: float<'u> -> PackedQuantity

        val projectXY: p: PackedSeries -> Vec2<nd> array

        val seriesLength: p: PackedSeries -> int

        val seriesName: p: PackedSeries -> string

        val seriesScales: p: PackedSeries -> IScaleErased * IScaleErased

        val packChannel: u: U<'u> -> values: float<'u> array -> PackedChannel

        val selectChannel: rows: int array -> c: PackedChannel -> PackedChannel

        val reunify:
          x: PackedChannel ->
            y: PackedChannel -> k: IChannelPairConsumer<'res> -> 'res

        val channelSeries:
          name: string -> x: PackedChannel -> y: PackedChannel -> PackedSeries

        val seriesValues: p: PackedSeries -> float array * float array

        val tweenSeries:
          a: PackedSeries -> b: PackedSeries -> t: float -> PackedSeries

        val tweenRows:
          rows: struct (int * int) array ->
            a: PackedSeries -> b: PackedSeries -> t: float -> PackedSeries

namespace Marey

    [<RequireQualifiedAccess>]
    module Stat =

        type Histogram<[<Measure>] 'u> =
            {
              Unit: U<'u>

              Edges: float<'u> array
              Counts: int array

              Dropped: int
            }

            member Centers: float<'u> array

            member Frequencies: float array

            member Total: int

            member Width: float<'u>

        val binCountFor: xs: float<'u> array -> int

        val binIn:
          u: U<'u> ->
            count: int ->
            float<'u> * float<'u> -> xs: float<'u> array -> Histogram<'u>

        val binWith:
          u: U<'u> -> count: int -> xs: float<'u> array -> Histogram<'u>

        val bin: u: U<'u> -> xs: float<'u> array -> Histogram<'u>

        val count: keys: 'k array -> ('k * int) array when 'k: equality

        type Ecdf<[<Measure>] 'u> =
            {
              Unit: U<'u>

              X: float<'u> array

              F: float array

              N: int
              Dropped: int
            }

        val ecdf: u: U<'u> -> xs: float<'u> array -> Ecdf<'u>

        type Density<[<Measure>] 'u> =
            {
              Unit: U<'u>

              DensityUnit: U</'u>

              X: float<'u> array
              Y: float</'u> array
              Bandwidth: float<'u>
              N: int
              Dropped: int
            }

        val bandwidth: xs: float<'u> array -> float<'u>

        val densityWith:
          u: U<'u> ->
            h: float<'u> -> points: int -> xs: float<'u> array -> Density<'u>

        [<Literal>]
        val DefaultDensityPoints: int = 128

        val density: u: U<'u> -> xs: float<'u> array -> Density<'u>

        type Frame =
            {

              Domain: float * float

              Bins: int

              Bandwidth: float
            }

        val frameOf: xs: float<'u> array -> Frame

        val quantile: p: float -> xs: float<'u> array -> float<'u>

        type Summary<[<Measure>] 'u> =
            {
              Unit: U<'u>
              Min: float<'u>
              Q1: float<'u>
              Median: float<'u>
              Q3: float<'u>
              Max: float<'u>

              LowerWhisker: float<'u>

              UpperWhisker: float<'u>

              Outliers: float<'u> array

              N: int
              Dropped: int
            }

            member Extent: float<'u> * float<'u>

            member Iqr: float<'u>

        val summary: u: U<'u> -> xs: float<'u> array -> Summary<'u>

        val groupBy:
          keys: 'k array ->
            values: float<'u> array -> ('k * float<'u> array) array
            when 'k: equality

        type Fit<[<Measure>] 'ux,[<Measure>] 'uy> =
            {
              Slope: float<'uy/'ux>
              Intercept: float<'uy>

              SlopeUnit: U<'uy/'ux>
              InterceptUnit: U<'uy>

              R2: float

              N: int

              Dropped: int
            }

        val regression:
          ux: U<'ux> ->
            uy: U<'uy> ->
            xs: float<'ux> array -> ys: float<'uy> array -> Fit<'ux,'uy>

        val predict: f: Fit<'ux,'uy> -> x: float<'ux> -> float<'uy>

        val fitLine:
          f: Fit<'ux,'uy> ->
            xs: float<'ux> array -> float<'ux> array * float<'uy> array

        val slopeQuantity: f: Fit<'ux,'uy> -> PackedQuantity

        val interceptQuantity: f: Fit<'ux,'uy> -> PackedQuantity

        type Hexbin<[<Measure>] 'ux,[<Measure>] 'uy> =
            {
              XUnit: U<'ux>
              YUnit: U<'uy>

              X: float<'ux> array
              Y: float<'uy> array
              Counts: int array
              Density: float</('ux 'uy)> array

              DensityUnit: U</('ux 'uy)>

              Width: float<'ux>

              RowSpacing: float<'uy>
              N: int
              Dropped: int
            }

        [<Literal>]
        val HexAspect: float = 1.6

        val hexbinIn:
          ux: U<'ux> ->
            uy: U<'uy> ->
            width: float<'ux> ->
            rowSpacing: float<'uy> ->
            float<'ux> * float<'uy> ->
              xs: float<'ux> array -> ys: float<'uy> array -> Hexbin<'ux,'uy>

        val hexbinWith:
          ux: U<'ux> ->
            uy: U<'uy> ->
            across: int ->
            xs: float<'ux> array -> ys: float<'uy> array -> Hexbin<'ux,'uy>

        [<Literal>]
        val DefaultHexagons: int = 24

        val hexbin:
          ux: U<'ux> ->
            uy: U<'uy> ->
            xs: float<'ux> array -> ys: float<'uy> array -> Hexbin<'ux,'uy>

        type Loess<[<Measure>] 'ux,[<Measure>] 'uy> =
            {
              XUnit: U<'ux>
              YUnit: U<'uy>
              X: float<'ux> array
              Y: float<'uy> array

              Span: float

              Degree: int
              N: int
              Dropped: int
            }

        [<Literal>]
        val DefaultSpan: float = 0.75

        [<Literal>]
        val DefaultLoessPoints: int = 80

        val loessIn:
          ux: U<'ux> ->
            uy: U<'uy> ->
            span: float ->
            degree: int ->
            at: float<'ux> array ->
            xs: float<'ux> array -> ys: float<'uy> array -> Loess<'ux,'uy>

        val loessWith:
          ux: U<'ux> ->
            uy: U<'uy> ->
            span: float ->
            points: int ->
            xs: float<'ux> array -> ys: float<'uy> array -> Loess<'ux,'uy>

        val loess:
          ux: U<'ux> ->
            uy: U<'uy> ->
            xs: float<'ux> array -> ys: float<'uy> array -> Loess<'ux,'uy>

        type Interval<[<Measure>] 'v> =
            {

              Estimate: float<'v>
              Lo: float<'v>
              Hi: float<'v>

              Level: float

              Resamples: int
              Dropped: int

              Seed: uint64
            }

        [<Literal>]
        val DefaultResamples: int = 2000

        val bootstrapWith:
          seed: uint64 ->
            resamples: int ->
            level: float ->
            statistic: (float<'u> array -> float<'v>) ->
            xs: float<'u> array -> Interval<'v>

        val bootstrap:
          seed: uint64 ->
            statistic: (float<'u> array -> float<'v>) ->
            xs: float<'u> array -> Interval<'v>

        val bootstrapPairsWith:
          seed: uint64 ->
            resamples: int ->
            level: float ->
            statistic: (float<'ux> array -> float<'uy> array -> float<'v>) ->
            xs: float<'ux> array -> ys: float<'uy> array -> Interval<'v>

        val bootstrapPairs:
          seed: uint64 ->
            statistic: (float<'ux> array -> float<'uy> array -> float<'v>) ->
            xs: float<'ux> array -> ys: float<'uy> array -> Interval<'v>

namespace Marey

    [<Struct>]
    type GradientStop =
        {
          Offset: float
          Color: Color
        }

    [<NoComparison>]
    type Paint =
        | Solid of Color

        | LinearGradient of
          from: Vec2<nd> * until: Vec2<nd> * stops: GradientStop array

        | RadialGradient of
          centre: Vec2<nd> * radius: float<nd> * stops: GradientStop array

    type LineCap =
        | Butt
        | Round
        | Square

    type LineJoin =
        | Miter
        | RoundJoin
        | Bevel

    type FillRule =
        | NonZero
        | EvenOdd

    [<NoComparison>]
    type Stroke =
        {
          Paint: Paint

          Width: float<px>
          Dash: float<px> array
          DashOffset: float<px>
          Cap: LineCap
          Join: LineJoin
          MiterLimit: float
        }

    [<NoComparison>]
    type PathSeg =
        | MoveTo of Vec2<nd>
        | LineTo of Vec2<nd>
        | CubicTo of c1: Vec2<nd> * c2: Vec2<nd> * until: Vec2<nd>
        | ClosePath

    [<Struct>]
    type FontSpec =
        {
          Family: string
          Size: float<pt>

          Weight: int
          Italic: bool
        }

    type TextAnchor =
        | AnchorStart
        | AnchorMiddle
        | AnchorEnd

    type TextBaseline =
        | BaselineAlphabetic
        | BaselineMiddle
        | BaselineHanging

    [<NoComparison>]
    type TextRun =
        {

          Text: string

          Tex: string option
          At: Vec2<nd>
          Font: FontSpec
          Paint: Paint
          Anchor: TextAnchor
          Baseline: TextBaseline

          Rotation: float<rad>
        }

    type MarkerShape =
        | MarkCircle
        | MarkSquare
        | MarkDiamond
        | MarkTriangleUp
        | MarkTriangleDown
        | MarkCross
        | MarkPlus

    [<NoComparison>]
    type MarkerBatch =
        {
          Points: Vec2<nd> array
          Shape: MarkerShape

          Size: float<px>
          Fill: Paint option
          Stroke: Stroke option
        }

    [<NoComparison>]
    type Image =
        {
          Width: int
          Height: int
          Rgba: byte array
        }

    [<NoComparison>]
    type RasterNode =
        {
          Image: Image

          Bounds: Rect<nd>

          Smooth: bool
        }

    [<Struct>]
    type Key = | Key of string

    type SemanticRole =
        | RoleSeries
        | RoleDatum
        | RoleAxis
        | RoleLegend
        | RoleAnnotation

        | RoleDecoration
        | RoleOther of string

    [<NoComparison>]
    type Semantics =
        {
          Role: SemanticRole

          Label: string

          Data: (string * string) array
        }

    [<NoComparison>]
    type Node =
        | Path of
          segments: PathSeg array * stroke: Stroke option * fill: Paint option *
          rule: FillRule
        | Text of TextRun
        | Markers of MarkerBatch
        | Raster of RasterNode
        | Group of Node array
        | Transform of Affine<nd,nd> * Node
        | Clip of PathSeg array * Node

        | Opacity of alpha: float * Node
        | Keyed of Key * Node
        | Annotated of Semantics * Node

    [<NoComparison>]
    type Patch =

        | Replace of (Key * Node) array

        | Whole of Node

    [<RequireQualifiedAccess>]
    module Stroke =

        val solid: c: Color -> w: float<px> -> Stroke

        val dashed:
          c: Color -> w: float<px> -> pattern: float<px> array -> Stroke

    [<RequireQualifiedAccess>]
    module Scene =

        val empty: Node

        val group: ns: Node seq -> Node

        val keyed: name: string -> n: Node -> Node

        val opacity: alpha: float -> n: Node -> Node

        val annotate: role: SemanticRole -> label: string -> n: Node -> Node

        val polyline: stroke: Stroke -> pts: Vec2<nd> array -> Node

        val polygon:
          stroke: Stroke option ->
            fill: Paint option -> pts: Vec2<nd> array -> Node

        val rect:
          stroke: Stroke option -> fill: Paint option -> r: Rect<nd> -> Node

        val transform: f: (Node -> Node) -> n: Node -> Node

        val fold: f: ('a -> Node -> 'a) -> state: 'a -> n: Node -> 'a

        val tryFindKey: key: Key -> n: Node -> Node option

        val mapKey: key: Key -> f: (Node -> Node) -> n: Node -> Node

        val diff: before: Node -> after: Node -> Patch

        val apply: patch: Patch -> before: Node -> Node

        val count: n: Node -> int

        val primitiveCount: n: Node -> int

        val prune: n: Node -> Node

namespace Marey

    [<RequireQualifiedAccess>]
    module Png =

        val encode: width: int -> height: int -> rgba: byte array -> byte array

        val dataUri: img: Image -> string

namespace Marey

    [<NoComparison>]
    type GridSpec =
        {

          X: float<nd> array

          Y: float<nd> array

          Samples: int
        }

    type Coord =

        abstract FromPanel: Vec2<nd> -> Vec2<nd> option

        abstract Grid: GridSpec -> PathSeg array array

        abstract ToPanel: Vec2<nd> -> Vec2<nd>

        abstract Clip: PathSeg array

    [<RequireQualifiedAccess>]
    module Coord =

        val cartesian: Coord

        val polarBetween:
          startAngle: float<rad> ->
            endAngle: float<rad> -> innerRadius: float -> Coord

        val polar: Coord

        val flipped: inner: Coord -> Coord

        val warpPolyline:
          c: Coord ->
            samplesPerSegment: int -> pts: Vec2<nd> array -> Vec2<nd> array

        val isIdentity: c: Coord -> bool

namespace Marey

    [<Struct>]
    type TextMetrics =
        {
          Advance: float<px>
          Ascent: float<px>
          Descent: float<px>
        }

    type ITextMeasurer =

        abstract Measure: FontSpec -> string -> TextMetrics

    type LicJob =
        {

          Xs: float array
          Ys: float array

          U: float array
          W: float array

          Width: int
          Height: int

          Noise: float array

          HalfLength: int
          Step: float

          Slowest: float
        }

    type ILicConvolver =

        abstract Convolve: LicJob -> float array voption

    [<Struct>]
    type Capabilities =
        {
          Gradients: bool
          RadialGradients: bool
          Clipping: bool
          Dashes: bool
          Rasters: bool

          TextShaping: bool
          RotatedText: bool

          Semantics: bool

          GroupOpacity: bool

          MaxBatch: int option
        }

    type IBackend =

        abstract Begin: Extent<px> -> unit

        abstract Draw: Node -> unit

        abstract Finish: unit -> unit

        abstract Caps: Capabilities

        abstract Measurer: ITextMeasurer

    [<NoComparison>]
    type RenderedAsset =
        {

          Name: string
          Bytes: byte array
        }

    [<NoComparison>]
    type RenderedDocument =
        {
          Document: string
          Assets: RenderedAsset array
        }

    [<RequireQualifiedAccess>]
    module Capabilities =

        val full: Capabilities

        val vector: Capabilities

    [<RequireQualifiedAccess>]
    module Degrade =

        val foldOpacity: a: float -> n: Node -> Node

        val forCaps: caps: Capabilities -> n: Node -> Node

        val splitBatches: caps: Capabilities -> n: Node -> Node

    [<Sealed>]
    type ApproxTextMeasurer =
        interface ITextMeasurer

        new: ?dpi: float<px/mm> -> ApproxTextMeasurer

    [<RequireQualifiedAccess>]
    module TextMeasure =

        val approx: ITextMeasurer

        val bounds:
          m: ITextMeasurer ->
            font: FontSpec ->
            anchor: TextAnchor ->
            baseline: TextBaseline ->
            rotation: float<rad> -> text: string -> Rect<px>

namespace Marey

    [<Sealed>]
    type TextTable =
        interface ITextMeasurer

        new: entries: (FontSpec * string * TextMetrics) array -> TextTable

        member Merge: other: TextTable -> TextTable

        member Entries: (FontSpec * string * TextMetrics) array

        static member Empty: TextTable

    [<RequireQualifiedAccess>]
    module Wire =

        [<Literal>]
        val Version: int = 1

        val encode: n: Node -> byte array

        val encodeWith: m: ITextMeasurer -> n: Node -> byte array

        val encodePatch: p: Patch -> byte array

        val encodePatchWith: m: ITextMeasurer -> p: Patch -> byte array

        val decodeWith: bytes: byte array -> Result<(Node * TextTable),string>

        val decodePatchWith:
          bytes: byte array -> Result<(Patch * TextTable),string>

        val decode: bytes: byte array -> Result<Node,string>

        val decodePatch: bytes: byte array -> Result<Patch,string>
