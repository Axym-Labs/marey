namespace Marey

    [<RequireQualifiedAccess>]
    module Ease =

        val linear: u: float -> float

        val quadIn: u: float -> float

        val quadOut: u: float -> float

        val quadInOut: u: float -> float

        val cubicIn: u: float -> float

        val cubicOut: u: float -> float

        val cubicInOut: u: float -> float

        val quartIn: u: float -> float

        val quartOut: u: float -> float

        val quartInOut: u: float -> float

        val sineIn: u: float -> float

        val sineOut: u: float -> float

        val sineInOut: u: float -> float

        val expoIn: u: float -> float

        val expoOut: u: float -> float

        val expoInOut: u: float -> float

        val circIn: u: float -> float

        val circOut: u: float -> float

        val circInOut: u: float -> float

        val backIn: u: float -> float

        val backOut: u: float -> float

        val backInOut: u: float -> float

        val elasticIn: u: float -> float

        val elasticOut: u: float -> float

        val elasticInOut: u: float -> float

        val bounceOut: u: float -> float

        val bounceIn: u: float -> float

        val bounceInOut: u: float -> float

namespace Marey

    [<Sealed>]
    type Anim<'a> =

        new: duration: float<Data.UnitSystems.SI.UnitSymbols.s> *
             atSeconds: (float<Data.UnitSystems.SI.UnitSymbols.s> -> 'a) ->
               Anim<'a>

        member At: t: float<Data.UnitSystems.SI.UnitSymbols.s> -> 'a

        member AtFraction: u: float -> 'a

        member AtSeconds: t: float<Data.UnitSystems.SI.UnitSymbols.s> -> 'a

        member Duration: float<Data.UnitSystems.SI.UnitSymbols.s>

    [<RequireQualifiedAccess>]
    module Anim =

        val ofFraction:
          d: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            f: (float -> 'a) -> Anim<'a>

        val ofSeconds:
          d: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            f: (float<Data.UnitSystems.SI.UnitSymbols.s> -> 'a) -> Anim<'a>

        val duration: a: Anim<'a> -> float<Data.UnitSystems.SI.UnitSymbols.s>

        val at: t: float<Data.UnitSystems.SI.UnitSymbols.s> -> a: Anim<'a> -> 'a

        val atFraction: u: float -> a: Anim<'a> -> 'a

        val pure': v: 'a -> Anim<'a>

        val map: f: ('a -> 'b) -> a: Anim<'a> -> Anim<'b>

        val map2: f: ('a -> 'b -> 'c) -> a: Anim<'a> -> b: Anim<'b> -> Anim<'c>

        val andThen: a: Anim<'a> -> b: Anim<'a> -> Anim<'a>

        val stretch: k: float -> a: Anim<'a> -> Anim<'a>

        val ease: f: (float -> float) -> a: Anim<'a> -> Anim<'a>

        val inline tween:
          d: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            a: ^T -> b: ^T -> Anim<^T>
            when (Interpolate or ^T) :
                   (static member Interp: ^T * ^T * float -> ^T)

namespace Marey

    type SubpathPadding =

        | GrowFromCentroid

        | GrowFromStart

    type ClosurePolicy =

        | CloseWhenBoth

        | CloseWhenEither

    [<NoComparison>]
    type MorphOptions =
        {

          Samples: int

          Flatten: int
          Padding: SubpathPadding
          Closure: ClosurePolicy
        }

    [<RequireQualifiedAccess>]
    module Morph =

        val defaults: MorphOptions

        val prepare:
          opts: MorphOptions ->
            a: PathSeg array ->
            b: PathSeg array -> PathSeg array * PathSeg array

        val paths:
          opts: MorphOptions ->
            a: PathSeg array -> b: PathSeg array -> t: float -> PathSeg array

        val anim:
          opts: MorphOptions ->
            d: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            a: PathSeg array -> b: PathSeg array -> Anim<PathSeg array>
