namespace Quiver

/// §4 — Tick generation.
///
/// The spike's `TickHint` returns `n+1` evenly spaced values across the domain, which puts
/// ticks at 3.7142857 and is not shippable. Real tick selection is the "1-2-5" nice-number
/// search: pick the coarsest step from {1, 2, 5} x 10^k that still yields roughly the
/// requested count.
///
/// This module is where §4's property tests bite:
///
/// * `TickHint n` returns between `n/2` and `2n` ticks — the 1-2-5 ladder gives ~[0.6n, 1.5n]
///   before boundary alignment, comfortably inside.
/// * all ticks lie within the domain.
/// * **tick label rendering is injective** — no two visible ticks may render to the same
///   string. That one is not automatic: it is a constraint on the *decimal count*, and it is
///   why `decimalsFor` is derived from the step rather than hardcoded.
///
/// Everything here is measure-generic. Label *text* is not decided here: prefix selection and
/// unit symbols live in `Quiver.Units`, because they need the `Dim`, and Core does not know
/// what a kilometre is.
[<RequireQualifiedAccess>]
module Ticks =

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
    /// This is the injectivity guarantee from §4, and it is the reason the step is a return
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
        let k = System.Math.Round(x / step)
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
        x.ToString("F" + string (max 0 (min 15 decimals)), System.Globalization.CultureInfo.InvariantCulture)

    /// Scientific notation, used when the exponent range is too wide for fixed point.
    let formatScientific (sigFigs: int) (x: float) =
        x.ToString("E" + string (max 0 (min 15 (sigFigs - 1))), System.Globalization.CultureInfo.InvariantCulture)

    /// Shortest round-trippable rendering. The default for anything that is not a tick label.
    let formatGeneral (x: float) =
        x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
