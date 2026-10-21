namespace Marey

open System

/// The easing catalogue.
///
/// An easing function reparameterises an animation's **normalised** clock: `float` in, `float`
/// out, both nominally in `[0, 1]`. It never sees a duration, which is what lets one
/// `Ease.cubicInOut` ease a one-second fade and a four-minute zoom.
///
/// **Every function here satisfies `f 0.0 = 0.0` and `f 1.0 = 1.0`**, to within the rounding of
/// its own formula. `AnimProperties` states that as a law over the catalogue found by
/// reflection rather than once per function, so an easing added here is enrolled in the law by
/// existing, not by someone remembering.
///
/// Nothing is promised about the interval between the endpoints, and that is deliberate.
/// `back` and `elastic` leave `[0, 1]` and come back; the overshoot is the entire visual
/// effect, and D71 is the decision that lets them keep it:
///
/// > `Anim.At` clamps **seconds**, never the fraction. An eased animation therefore evaluates
/// > its interpolation outside its own range by design, `Interp` extrapolates because none of
/// > its instances clamp `t`, and `Ease.backOut` overshoots. Clamping here would leave a
/// > library in which `Ease.backOut` compiles, runs, and does nothing.
///
/// **Outside `[0, 1]` the formulas are evaluated as written**, which is well-defined for every
/// family except `circ` — a circular ease is a quarter circle, and there is no circle beyond
/// its radius, so `circIn 1.4` is `nan`. That is the honest answer rather than a silent clamp,
/// and it only arises from easing an already-eased animation.
///
/// The formulas are the Penner set in the canonical form collected at easings.net, with the
/// nine power functions factored through three implementations rather than written out nine
/// times — which is how a library ends up with `quartOut` subtly wrong and nothing to notice.
[<RequireQualifiedAccess>]
module Ease =

    // ==================================================================================
    // THE SHARED SHAPES
    //
    // Private, so the reflection-driven catalogue law does not see them. They are not easing
    // functions; they are the two-argument definitions the easing functions are names for.
    // ==================================================================================

    let private powIn (n: float) (u: float) = u ** n

    let private powOut (n: float) (u: float) = 1.0 - (1.0 - u) ** n

    let private powInOut (n: float) (u: float) =
        if u < 0.5 then
            2.0 ** (n - 1.0) * u ** n
        else
            1.0 - (2.0 - 2.0 * u) ** n / 2.0

    /// Penner's overshoot constant. 1.70158 is the value for which `backOut` overshoots by
    /// almost exactly 10% of the travel; it is a magic number in the literature and stays one
    /// here rather than becoming a parameter nobody would tune.
    let private Back = 1.70158

    /// The in-out variant needs a larger constant to overshoot visibly across half the travel.
    let private BackHalf = Back * 1.525

    /// `elastic` oscillates at a period of 2pi/3 of the normalised clock — three quarters of an
    /// oscillation over the whole animation, which is what makes it read as one spring rather
    /// than as a vibration.
    let private ElasticPeriod = 2.0 * Math.PI / 3.0

    /// The in-out variant halves the travel, so its period is scaled to keep the same number of
    /// visible oscillations across the whole.
    let private ElasticPeriodHalf = 2.0 * Math.PI / 4.5

    let private BounceScale = 7.5625
    let private BounceSplit = 2.75

    // ==================================================================================
    // THE CATALOGUE
    //
    // Every one of these is written as a function of `u` rather than as a partial application
    // (`let quadIn = powIn 2.0`), because the law in `AnimProperties` finds the catalogue by
    // reflection: a syntactic function compiles to a static method and a partially applied
    // value compiles to a property, and a catalogue half of which is invisible to its own law
    // is worse than no law.
    // ==================================================================================

    /// The identity. Present because "no easing" is a choice a storyboard should be able to
    /// name, rather than the absence of one.
    let linear (u: float) : float = u

    let quadIn (u: float) : float = powIn 2.0 u
    let quadOut (u: float) : float = powOut 2.0 u
    let quadInOut (u: float) : float = powInOut 2.0 u

    let cubicIn (u: float) : float = powIn 3.0 u
    let cubicOut (u: float) : float = powOut 3.0 u

    /// The default an animator reaches for. Slow at both ends, fastest in the middle, and
    /// symmetric — which is why it is the one easing that never draws attention to itself.
    let cubicInOut (u: float) : float = powInOut 3.0 u

    let quartIn (u: float) : float = powIn 4.0 u
    let quartOut (u: float) : float = powOut 4.0 u
    let quartInOut (u: float) : float = powInOut 4.0 u

    let sineIn (u: float) : float = 1.0 - cos (u * Math.PI / 2.0)
    let sineOut (u: float) : float = sin (u * Math.PI / 2.0)
    let sineInOut (u: float) : float = -(cos (Math.PI * u) - 1.0) / 2.0

    // The exponential family is the one place a special case is not defensive dressing:
    // `2.0 ** (10.0 * 0.0 - 10.0)` is 1/1024, not 0, so without the endpoint branch `expoIn`
    // starts a tenth of a percent along and the law below would fail on a real error.
    let expoIn (u: float) : float =
        if u = 0.0 then 0.0 else 2.0 ** (10.0 * u - 10.0)

    let expoOut (u: float) : float =
        if u = 1.0 then 1.0 else 1.0 - 2.0 ** (-10.0 * u)

    let expoInOut (u: float) : float =
        if u = 0.0 then 0.0
        elif u = 1.0 then 1.0
        elif u < 0.5 then 2.0 ** (20.0 * u - 10.0) / 2.0
        else (2.0 - 2.0 ** (10.0 - 20.0 * u)) / 2.0

    let circIn (u: float) : float = 1.0 - sqrt (1.0 - u * u)

    let circOut (u: float) : float =
        let v = u - 1.0
        sqrt (1.0 - v * v)

    let circInOut (u: float) : float =
        if u < 0.5 then
            (1.0 - sqrt (1.0 - 4.0 * u * u)) / 2.0
        else
            let v = 2.0 - 2.0 * u
            (sqrt (1.0 - v * v) + 1.0) / 2.0

    /// Anticipation: pulls **below** zero before moving forwards. The dip is the effect.
    let backIn (u: float) : float =
        (Back + 1.0) * u * u * u - Back * u * u

    /// Overshoot: rises **past** one and settles back. D71 exists so that this survives the
    /// clock, and `AnimProperties` asserts that it does.
    let backOut (u: float) : float =
        let v = u - 1.0
        1.0 + (Back + 1.0) * v * v * v + Back * v * v

    let backInOut (u: float) : float =
        if u < 0.5 then
            let v = 2.0 * u
            v * v * ((BackHalf + 1.0) * v - BackHalf) / 2.0
        else
            let v = 2.0 * u - 2.0
            (v * v * ((BackHalf + 1.0) * v + BackHalf) + 2.0) / 2.0

    let elasticIn (u: float) : float =
        if u = 0.0 then 0.0
        elif u = 1.0 then 1.0
        else -(2.0 ** (10.0 * u - 10.0)) * sin ((u * 10.0 - 10.75) * ElasticPeriod)

    let elasticOut (u: float) : float =
        if u = 0.0 then 0.0
        elif u = 1.0 then 1.0
        else 2.0 ** (-10.0 * u) * sin ((u * 10.0 - 0.75) * ElasticPeriod) + 1.0

    let elasticInOut (u: float) : float =
        if u = 0.0 then 0.0
        elif u = 1.0 then 1.0
        elif u < 0.5 then
            -(2.0 ** (20.0 * u - 10.0) * sin ((20.0 * u - 11.125) * ElasticPeriodHalf)) / 2.0
        else
            2.0 ** (-20.0 * u + 10.0) * sin ((20.0 * u - 11.125) * ElasticPeriodHalf) / 2.0
            + 1.0

    /// Four parabolic arcs of decreasing height. The constants are the standard ones: the
    /// split points are `1/2.75`, `2/2.75` and `2.5/2.75`, and each arc's offset is chosen so
    /// the curve is continuous at every seam and lands exactly on 1.
    let bounceOut (u: float) : float =
        if u < 1.0 / BounceSplit then
            BounceScale * u * u
        elif u < 2.0 / BounceSplit then
            let v = u - 1.5 / BounceSplit
            BounceScale * v * v + 0.75
        elif u < 2.5 / BounceSplit then
            let v = u - 2.25 / BounceSplit
            BounceScale * v * v + 0.9375
        else
            let v = u - 2.625 / BounceSplit
            BounceScale * v * v + 0.984375

    let bounceIn (u: float) : float = 1.0 - bounceOut (1.0 - u)

    let bounceInOut (u: float) : float =
        if u < 0.5 then
            (1.0 - bounceOut (1.0 - 2.0 * u)) / 2.0
        else
            (1.0 + bounceOut (2.0 * u - 1.0)) / 2.0
