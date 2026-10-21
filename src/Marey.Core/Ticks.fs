namespace Marey

/// Tick generation.
///
/// The spike's `TickHint` returns `n+1` evenly spaced values across the domain, which puts
/// ticks at 3.7142857 and is not shippable. Real tick selection is the "1-2-5" nice-number
/// search: pick the coarsest step from {1, 2, 5} x 10^k that still yields roughly the
/// requested count.
///
/// This module is where the property tests bite:
///
/// * `TickHint n` returns between `n/2` and `2n` ticks — the 1-2-5 ladder gives ~[0.6n, 1.5n]
///   before boundary alignment, comfortably inside.
/// * all ticks lie within the domain.
/// * **tick label rendering is injective** — no two visible ticks may render to the same
///   string. That one is not automatic: it is a constraint on the *decimal count*, and it is
///   why `decimalsFor` is derived from the step rather than hardcoded.
///
/// Everything here is measure-generic. Label *text* is decided here only as far as the number
/// goes — decimals, and powers of ten on a wide log axis. Prefix selection and unit symbols live
/// in `Marey.Units`, because they need the `Dim`, and Core does not know what a kilometre is.
[<RequireQualifiedAccess>]
module internal Ticks =

    /// The coarsest "nice" step at most `raw`-ish. Rounds `raw` to the nearest member of the
    /// 1-2-5 ladder rather than always rounding up, which is what keeps the count centred on
    /// the request instead of always undershooting.
    let niceStep (raw: float) : float =
        if raw <= 0.0 || System.Double.IsNaN raw || System.Double.IsInfinity raw then
            1.0
        else
            let mag = 10.0 ** floor (log10 raw)
            let norm = raw / mag

            let step =
                if norm < 1.5 then 1.0
                elif norm < 3.0 then 2.0
                elif norm < 7.0 then 5.0
                else 10.0

            step * mag

    /// Decimal places needed to render values on a grid of `step` without two of them
    /// colliding. `step = 0.25` needs 2; `step = 20` needs 0.
    ///
    /// This is the injectivity guarantee, and it is the reason the step is a return
    /// value of tick generation rather than an implementation detail.
    let decimalsFor (step: float) =
        if step <= 0.0 || System.Double.IsNaN step || System.Double.IsInfinity step then
            0
        else
            let d = -int (floor (log10 step + 1e-9))
            max 0 (min 15 d)

    /// Snap a value to a multiple of `step`, killing the float noise that turns 0.30000000004
    /// into a four-decimal tick label.
    let private snap (step: float) (x: float) =
        let k = Num.roundEven (x / step)
        k * step

    /// Nice ticks over `[lo, hi]`, aiming for `target` of them. Measure-generic: the ticks come
    /// back in the same unit the domain was given in.
    ///
    /// A reversed domain (`hi < lo`, a legitimate way to spell a descending axis) is generated
    /// ascending and then reversed, so the caller's orientation survives.
    let linear<[<Measure>] 'u> (target: int) (lo: float<'u>, hi: float<'u>) : float<'u>[] =
        let a = float lo
        let b = float hi
        let reversed = b < a
        let lo', hi' = (min a b), (max a b)

        if System.Double.IsNaN lo' || System.Double.IsNaN hi' || System.Double.IsInfinity lo' || System.Double.IsInfinity hi' then
            [||]
        elif lo' = hi' then
            // Degenerate domain: one tick at the value is the only honest answer.
            [| LanguagePrimitives.FloatWithMeasure<'u> lo' |]
        else
            let n = max 1 target
            let step = niceStep ((hi' - lo') / float n)
            let first = ceil (lo' / step - 1e-9) * step

            let values =
                [| let mutable v = first

                   while v <= hi' + step * 1e-9 do
                       yield LanguagePrimitives.FloatWithMeasure<'u>(snap step v)
                       v <- v + step |]

            if reversed then Array.rev values else values

    /// The step `linear` chose. Feed it to `decimalsFor` to format the labels.
    let linearStep (target: int) (lo: float<'u>, hi: float<'u>) : float =
        let span = abs (float hi - float lo)

        if span = 0.0 then
            1.0
        else
            niceStep (span / float (max 1 target))

    /// Decade ticks for a log scale, with 1-2-5 minor subdivision when the span is narrow
    /// enough that bare decades would leave a nearly empty axis.
    ///
    /// Log scales are the case where `TickHint n` legitimately cannot honour `n`: over six
    /// decades you get six ticks whatever you asked for, because anything else would be
    /// dishonest about where the data is. The property test's bound is on *linear* scales.
    let log10<[<Measure>] 'u> (target: int) (lo: float<'u>, hi: float<'u>) : float<'u>[] =
        let a = float lo
        let b = float hi

        if a <= 0.0 || b <= 0.0 || System.Double.IsNaN a || System.Double.IsNaN b then
            [||]
        else
            let reversed = b < a
            let lo', hi' = (min a b), (max a b)
            let d0 = int (floor (System.Math.Log10 lo'))
            let d1 = int (ceil (System.Math.Log10 hi'))
            let decades = d1 - d0

            let mantissas =
                if decades = 0 then [| 1.0; 2.0; 3.0; 4.0; 5.0; 6.0; 7.0; 8.0; 9.0 |]
                elif decades <= max 1 (target / 3) then [| 1.0; 2.0; 5.0 |]
                else [| 1.0 |]

            let values =
                [| for d in d0..d1 do
                       for m in mantissas do
                           let v = m * (10.0 ** float d)

                           if v >= lo' * (1.0 - 1e-9) && v <= hi' * (1.0 + 1e-9) then
                               yield LanguagePrimitives.FloatWithMeasure<'u> v |]

            if reversed then Array.rev values else values

    /// Round a domain outward to the nearest nice step, so the axis ends *on* a tick.
    /// Optional by design — clipping the domain to the data is often the more honest choice,
    /// and this is the layer that should not decide that for the user.
    let expandToNice<[<Measure>] 'u> (target: int) (lo: float<'u>, hi: float<'u>) : float<'u> * float<'u> =
        let a = float lo
        let b = float hi

        if a = b || System.Double.IsNaN a || System.Double.IsNaN b then
            (lo, hi)
        else
            let reversed = b < a
            let lo', hi' = (min a b), (max a b)
            let step = niceStep ((hi' - lo') / float (max 1 target))
            let l = floor (lo' / step + 1e-9) * step
            let h = ceil (hi' / step - 1e-9) * step

            let l' = LanguagePrimitives.FloatWithMeasure<'u> l
            let h' = LanguagePrimitives.FloatWithMeasure<'u> h

            if reversed then (h', l') else (l', h')

    /// Fixed-point formatting with the given decimals, invariant culture.
    ///
    /// Invariant culture is not a detail: a figure rendered on a German-locale machine must
    /// not label its axis `1,5` while the golden-image test on CI expects `1.5`. Every number
    /// that reaches a backend goes through here or through `sprintf "%g"`, both invariant.
    let format (decimals: int) (x: float) =
        // Negative zero renders as "-0", which looks like a bug on an axis. It is one.
        let x = if x = 0.0 then 0.0 else x
        Num.fixedPoint (max 0 (min 15 decimals)) x

    /// Scientific notation, `1.235E+004`. Not used for tick labels: a log axis too wide for
    /// fixed point is written in powers of ten (`formatPower`).
    let formatScientific (sigFigs: int) (x: float) =
        x.ToString("E" + string (max 0 (min 15 (sigFigs - 1))), System.Globalization.CultureInfo.InvariantCulture)

    /// Shortest round-trippable rendering. The default for anything that is not a tick label.
    let formatGeneral (x: float) = Num.shortest x

    // ---- log tick labels ---------------------------------------------------------------
    //
    // A log axis's ticks are not on a grid, so no one decimal count suits them all: the finest
    // tick's count, shared, gave `0.000001 … 1.000000` over six decades, and past fifteen decades
    // the clamp in `format` wrote the lowest ticks as the same row of zeros. Each tick here takes
    // its own decimals, and an axis too wide for that is written in powers of ten.

    /// A log axis whose domain spans more than this many decades is written in powers of ten
    /// and takes no SI prefix.
    ///
    /// Three because a prefix spans three. Over at most three decades, a prefix chosen from the
    /// top of the domain leaves every tick in [10⁻³, 10³), which fixed point writes in at most
    /// five characters (`0.001`, `500`). Each further decade adds a character to the longest
    /// label while `10⁻⁵` stays four, and no one prefix suits both ends of the axis.
    [<Literal>]
    let WideLogDecades = 3.0

    /// Whether a log axis over `(lo, hi)` spans more than `WideLogDecades`. False for a domain
    /// that is not positive and finite, which a log scale cannot draw anyway.
    let isWideLog (lo: float, hi: float) : bool =
        lo > 0.0
        && hi > 0.0
        && not (System.Double.IsInfinity lo || System.Double.IsInfinity hi)
        && abs (System.Math.Log10 hi - System.Math.Log10 lo) > WideLogDecades + 1e-9

    /// Whether fixed point writes a log tick in at most three decimals and four digits.
    let private brief (x: float) =
        let a = abs x
        a >= 1e-3 * (1.0 - 1e-9) && a <= 1e3 * (1.0 + 1e-9)

    /// Whether a log axis over `domain`, with its ticks at `values`, is written in powers of ten:
    /// when the domain is wide, or when a tick lies outside [10⁻³, 10³].
    ///
    /// The second condition covers a narrow axis no prefix brought near 1: a dimensionless one,
    /// one in a unit that takes no prefix (`h`), or one whose prefix was pinned elsewhere.
    /// [10⁻³, 10³] is the range a prefix leaves a narrow axis in, so a tick outside it is a
    /// number a prefix would have shortened.
    let logInPowers (domain: float * float) (values: float[]) : bool =
        isWideLog domain || not (Array.forall brief values)

    /// A positive `x` as `m × 10^e` with `m` from 1 to 9.
    ///
    /// The mantissa is rounded, not truncated: a log tick is a whole mantissa times a decade
    /// (`log10`), and dividing by a prefix leaves it as `2.9999999999999996` as often as `3`.
    /// `(0, 0)` for zero, a negative or a non-finite `x`, none of which a log axis has.
    let powerOfTen (x: float) : int * int =
        if not (x > 0.0) || System.Double.IsInfinity x then
            0, 0
        else
            let e = int (floor (System.Math.Log10 x + 1e-9))
            let m = Num.roundAway (x / 10.0 ** float e)

            if m >= 10.0 then 1, e + 1 else int m, e

    /// A log tick as a power of ten, in Unicode: `10⁻⁸`, `2×10⁻⁸`. Every exponent is written,
    /// `10⁰` and `10¹` included, so the labels of one axis read as one series.
    let formatPower (x: float) : string =
        match powerOfTen x with
        | 0, _ -> "0"
        | 1, e -> "10" + Num.superscript e
        | m, e -> string m + "×10" + Num.superscript e

    /// A reading on an axis written in powers of ten, in Unicode, its mantissa to the places
    /// `resolution` resolves: `1.1×10⁻¹⁸` where the reading is good to a tenth of 10⁻¹⁸. A
    /// mantissa of 1 with no places is written `10⁻¹⁸`, as the tick is. A resolution coarser than
    /// the decade is rounded to the nearest power, since no digit of the mantissa is known.
    ///
    /// The mantissa is rounded half away from zero, as `format` rounds, and one that rounds up to
    /// 10 moves to the next decade with one place more, so that it claims the same precision.
    let formatPowerTo (resolution: float) (x: float) : string =
        if not (x > 0.0) || System.Double.IsInfinity x then
            format 0 x
        else
            let e = int (floor (System.Math.Log10 x + 1e-9))
            let r = resolution / 10.0 ** float e

            if r >= 10.0 && not (System.Double.IsInfinity r) then
                "10" + Num.superscript (int (Num.roundAway (System.Math.Log10 x)))
            else
                let d = decimalsFor r
                let m = Num.roundAway (x / 10.0 ** float e * 10.0 ** float d) / 10.0 ** float d
                let m, e, d = if m >= 10.0 then m / 10.0, e + 1, min 15 (d + 1) else m, e, d

                if d = 0 && m = 1.0 then
                    "10" + Num.superscript e
                else
                    format d m + "×10" + Num.superscript e

    /// `x` with the decimals it needs itself rather than those of the finest tick on its axis:
    /// `0.01`, `1`, `100`. Injective over log ticks in [10⁻³, 10³], which differ in their mantissa
    /// or their decade and need at most three decimals.
    let formatOwn (x: float) : string = format (decimalsFor (abs x)) x

    /// The labels of a log axis's ticks, in Unicode: each tick's own decimals, or powers of ten
    /// when `logInPowers`. What `Erase.scale` writes; `Marey.Units` applies the same rule after
    /// choosing a prefix, and in the requested `UnitStyle`.
    let internal logLabels (domain: float * float) (values: float[]) : string[] =
        let label = if logInPowers domain values then formatPower else formatOwn
        values |> Array.map label
