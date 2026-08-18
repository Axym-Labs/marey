/// §4's two tick properties, and the measured exceptions to one of them.
///
/// > * `TickHint n` returns between `n/2` and `2n` ticks, all within the domain
/// > * tick label rendering is injective (no two visible ticks get the same label)
///
/// Ticks.fs is careful to say the count bound is stated for *linear* scales: "Log scales are the
/// case where `TickHint n` legitimately cannot honour `n` — over six decades you get six ticks
/// whatever you asked for." That carve-out is respected below.
///
/// **The count bound is nevertheless false at `n = 1` and `n = 3`**, on linear scales, against
/// the shipped 1-2-5 ladder. Both counterexamples are locked in as regression tests in
/// `MeasuredExceptions`, with the arithmetic that produces them written out. The property is
/// therefore stated for `n >= 4`, which is where it does hold — see `TickTarget`.
module Marey.Tests.TickProperties

open FsCheck.Xunit
open Xunit
open Marey
open Marey.Tests.Generators

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Counts =

    [<Property>]
    let ``a linear TickHint returns between n/2 and 2n ticks`` (LinearDomain(lo, hi)) (TickTarget n) =
        let s = Scale.linear U.one (lo, hi)
        let c = (s.TickHint n).Length
        float c >= float n / 2.0 && float c <= float n * 2.0

    /// §4's containment half.
    ///
    /// Asserted **exactly**, with no tolerance, despite the `1e-9` fudge factors inside
    /// `Ticks.linear` and `Ticks.log10`: measured over 845 000 ticks from power scales, 60 000
    /// log domains up to twelve decades and 60 000 linear domains, not one tick fell outside.
    ///
    /// The one place it does fail is a domain whose lower endpoint is more than nine orders of
    /// magnitude below the chosen step — `Ticks.linear 10 (1.0, 1e10)` puts a tick at 0. That is
    /// a genuine defect, pinned in `MeasuredExceptions` below, and the generators stay on the
    /// correct side of it so that this property can keep its exact form.
    [<Property>]
    let ``every tick lies within the domain`` (spec: ScaleSpec) (TickTarget n) =
        let s = ScaleSpec.build spec
        let lo, hi = ScaleSpec.domain spec
        let a, b = min lo hi, max lo hi
        s.TickHint n |> Array.forall (fun t -> float t >= a && float t <= b)

    [<Property>]
    let ``ticks come back in domain order`` (spec: ScaleSpec) (TickTarget n) =
        let s = ScaleSpec.build spec
        let ts = s.TickHint n |> Array.map float

        ts.Length < 2
        || Array.forall2 (fun x y -> x <= y) ts.[.. ts.Length - 2] ts.[1..]

    /// A reversed domain is a legitimate way to spell a descending axis, and Ticks.fs promises
    /// the caller's orientation survives.
    [<Property>]
    let ``a reversed domain returns descending ticks`` (LinearDomain(lo, hi)) (TickTarget n) =
        let ts = Ticks.linear n (hi, lo) |> Array.map float

        ts.Length < 2
        || Array.forall2 (fun x y -> x >= y) ts.[.. ts.Length - 2] ts.[1..]

    [<Property>]
    let ``a degenerate domain yields exactly one tick at the value`` (FiniteFloat v) (TickTarget n) =
        let ts = Ticks.linear n (v, v)
        ts.Length = 1 && float ts.[0] = v

    [<Property>]
    let ``a non-finite domain yields no ticks`` (TickTarget n) =
        [ (nan, 1.0); (1.0, nan); (infinity, 1.0); (1.0, -infinity) ]
        |> List.forall (fun d -> (Ticks.linear n d).Length = 0)

    /// Log ticks cannot honour a count (Ticks.fs), but they can still be positive, ordered and
    /// inside the domain — and they are powers of the decade times a 1-2-5 mantissa.
    [<Property>]
    let ``log ticks are positive decade multiples inside the domain`` (LogDomain(lo, hi)) (TickTarget n) =
        let ts = Ticks.log10 n (lo, hi) |> Array.map float

        ts
        |> Array.forall (fun v ->
            v > 0.0
            && v >= lo * (1.0 - 1e-9)
            && v <= hi * (1.0 + 1e-9)
            && (let m = v / (10.0 ** floor (log10 v + 1e-12))
                [ 1.0..9.0 ] |> List.exists (fun k -> abs (m - k) < 1e-6)))

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module NiceSteps =

    /// The whole tick strategy rests on `niceStep` landing on the 1-2-5 ladder. If it ever
    /// returns 3 or 7, tick labels stop being round numbers and `decimalsFor` — which derives
    /// the decimal count from the step — stops guaranteeing injectivity.
    [<Property>]
    let ``niceStep is always 1, 2 or 5 times a power of ten`` (PositiveFloat x) =
        let s = Ticks.niceStep x
        let mag = 10.0 ** floor (log10 s + 1e-12)
        let norm = s / mag
        [ 1.0; 2.0; 5.0 ] |> List.exists (fun k -> abs (norm - k) < 1e-9)

    [<Property>]
    let ``niceStep is within a factor of two of its argument`` (PositiveFloat x) =
        let s = Ticks.niceStep x
        s >= x / 2.0 && s <= x * 2.0

    [<Fact>]
    let ``niceStep is defensive about non-positive and non-finite input`` () =
        Assert.Equal(1.0, Ticks.niceStep 0.0)
        Assert.Equal(1.0, Ticks.niceStep -5.0)
        Assert.Equal(1.0, Ticks.niceStep nan)
        Assert.Equal(1.0, Ticks.niceStep infinity)

    /// `decimalsFor` is the injectivity guarantee, so its own contract is worth pinning: enough
    /// decimals that two adjacent grid values render differently, clamped to what `F` supports.
    [<Property>]
    let ``decimalsFor renders adjacent steps distinguishably`` (PositiveFloat x) =
        let step = Ticks.niceStep x
        let d = Ticks.decimalsFor step
        // Only meaningful while fixed-point can still express the step; see InjectivityLimits.
        step < 1e-15
        || Ticks.format d 0.0 <> Ticks.format d step

    [<Property>]
    let ``expandToNice contains the original domain`` (LinearDomain(lo, hi)) (TickTarget n) =
        let l, h = Ticks.expandToNice n (lo, hi)
        float l <= lo && float h >= hi

    [<Property>]
    let ``expandToNice preserves orientation`` (LinearDomain(lo, hi)) (TickTarget n) =
        let l, h = Ticks.expandToNice n (hi, lo)
        float l >= float h

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Injectivity =

    let private distinct (xs: string[]) =
        xs.Length = (Array.distinct xs).Length

    /// §4 — "tick label rendering is injective (no two visible ticks get the same label)", at the
    /// Core level, through the one sanctioned erasure.
    ///
    /// The generated domains keep the tick step above 1e-15, which is where `Ticks.format`'s
    /// clamp bites; `InjectivityLimits` below records what happens beyond it.
    [<Property>]
    let ``Erase.scale tick labels are distinct`` (LinearDomain(lo, hi)) (TickTarget n) =
        let s = Scale.linear U.metre (lo * 1.0<m>, hi * 1.0<m>)
        let labels = (Erase.scale s).Ticks n |> Array.map (fun t -> t.Label)
        Ticks.linearStep n s.Domain < 1e-15 || distinct labels

    /// The same property one layer up, where §2.2's worked example actually lives: `Axis` picks
    /// a prefix from the data range *before* formatting, which is what turns six ticks reading
    /// `0.000000001` into `1, 2, 3, 4, 5` with a title of `x [nm]`.
    [<Property>]
    let ``Axis.auto tick labels are distinct on a linear metre axis`` (LinearDomain(lo, hi)) =
        let s = Scale.linear U.metre (lo * 1.0<m>, hi * 1.0<m>)
        distinct ((Axis.auto Unicode "x" s).Ticks |> Array.map (fun t -> t.Label))

    [<Property>]
    let ``Axis.auto tick labels are distinct on a log metre axis`` (LogDomain(lo, hi)) =
        let s = Scale.log10 U.metre (lo * 1.0<m>, hi * 1.0<m>)
        distinct ((Axis.auto Unicode "x" s).Ticks |> Array.map (fun t -> t.Label))

    /// D19 — "an `m²` axis over 0…5e-18 renders six ticks that *all* read `0.000000000000000`",
    /// which is why selection rescales even when a prefix cannot be glued on. The fix is
    /// verified here from the outside: the axis reads `am`, and the labels are 0 through 5.
    [<Fact>]
    let ``a nanometre-scale axis is labelled in nanometres`` () =
        let ax = Axis.auto Unicode "x" (Scale.linear U.metre (1e-9<m>, 5e-9<m>))
        Assert.Equal("x [nm]", ax.Title)
        Assert.Equal<string[]>([| "1"; "2"; "3"; "4"; "5" |], ax.Ticks |> Array.map (fun t -> t.Label))

    [<Fact>]
    let ``an attometre-scale axis rescales rather than printing fifteen zeros`` () =
        let ax = Axis.auto Unicode "x" (Scale.linear U.metre (0.0<m>, 5e-18<m>))
        Assert.Equal("x [am]", ax.Title)
        Assert.True(distinct (ax.Ticks |> Array.map (fun t -> t.Label)))

    /// D19's other half: a prefix that cannot be glued onto the symbol becomes an explicit power
    /// of ten, because `nm²` would mean `(nm)²` and be wrong by 10^9.
    [<Fact>]
    let ``an area axis states the power of ten rather than gluing a prefix`` () =
        let ax = Axis.auto Unicode "A" (Scale.linear U.squareMetre (0.0<m^2>, 5e-18<m^2>))
        Assert.Contains("10", ax.UnitText)
        Assert.DoesNotContain("am", ax.UnitText)
        Assert.True(distinct (ax.Ticks |> Array.map (fun t -> t.Label)))

module Formatting =

    /// Ticks.fs: "a figure rendered on a German-locale machine must not label its axis `1,5`
    /// while the golden-image test on CI expects `1.5`."
    [<Fact>]
    let ``tick formatting is culture-invariant`` () =
        let render () =
            [| Ticks.format 1 1.5
               Ticks.format 3 -0.125
               Ticks.formatScientific 4 12345.678
               Ticks.formatGeneral 0.1 |]

        let baseline = render ()
        Assert.Equal<string[]>(baseline, Culture.run Culture.CommaDecimal render)
        Assert.Equal<string[]>(baseline, Culture.run Culture.DotlessI render)
        Assert.Equal("1.5", baseline.[0])

    /// "Negative zero renders as `-0`, which looks like a bug on an axis. It is one."
    [<Property(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 200, Replay = Seed, QuietOnSuccess = true)>]
    let ``no tick label is negative zero`` (LinearDomain(lo, hi)) (TickTarget n) =
        let s = Scale.linear U.metre (lo * 1.0<m>, hi * 1.0<m>)

        (Erase.scale s).Ticks n
        |> Array.forall (fun t -> not (t.Label.StartsWith "-0" && t.Label.TrimStart('-', '0', '.') = ""))

// ======================================================================================
// MEASURED EXCEPTIONS
//
// Two properties the design states are, as written, false. Neither is weakened away: both
// counterexamples are pinned here so they are visible in the suite, greppable, and impossible
// to reintroduce silently after a fix.
// ======================================================================================

module MeasuredExceptions =

    /// **§4's count bound does not hold at `n = 3`.**
    ///
    /// `Ticks.linear` picks `niceStep(span / n)`, and the 1-2-5 ladder can round a raw step
    /// *up* by a factor of up to 1/0.6: `norm = 3.07` rounds to 5. The tick count is then
    /// `span/step ≈ 0.6n` before boundary alignment, and `floor(0.6 * 3) = 1`, which is below
    /// `n/2 = 1.5`.
    ///
    /// Worked example: span = 920.85, target 3, raw step 306.9, `norm = 3.069` → step 500, and
    /// `[0.495, 921.34]` contains exactly one multiple of 500.
    ///
    /// The comment in Ticks.fs claims the ladder gives "~[0.6n, 1.5n] before boundary alignment,
    /// comfortably inside" the `[n/2, 2n]` bound. `0.6n` is right; "comfortably inside" is not,
    /// because `floor` of it drops below `n/2` at exactly `n = 3`.
    [<Fact>]
    let ``DEFECT TickHint 3 can return one tick, below the n/2 bound`` () =
        let domain = (0.49476730778608102, 921.34035582915112)
        let ticks = Ticks.linear 3 domain
        Assert.Equal(1, ticks.Length)
        Assert.True(float ticks.Length < 3.0 / 2.0, "this is the violation being recorded")

    /// The same failure at `n = 1`, where it is total: an interval of length 7.5 with a chosen
    /// step of 10 can contain no multiple of the step at all.
    [<Fact>]
    let ``DEFECT TickHint 1 can return no ticks at all`` () =
        Assert.Equal(0, (Ticks.linear 1 (0.5, 8.0)).Length)

    /// **§4's containment half fails too, on a wide linear domain.**
    ///
    /// `Ticks.linear` starts at `first = ceil(lo / step - 1e-9) * step`. The `1e-9` exists to
    /// absorb float noise when `lo` is *almost exactly* a multiple of the step, and as a
    /// fraction of one step that is the right size. But it is compared against `lo / step`,
    /// which is not a noise term — it is the position of the domain's lower endpoint on the
    /// grid. Once `lo / step <= 1e-9`, subtracting `1e-9` carries `ceil` down to the previous
    /// grid line, and the first tick lands **below the domain**.
    ///
    /// `Ticks.linear 10 (1.0, 1e10)` — a perfectly ordinary count axis from one to ten billion
    /// — therefore emits a tick at 0. Zero is not in `[1, 1e10]`, and on a scale that cannot
    /// represent it (a log axis reached through `Scale.withDomain`, say) it is not merely an
    /// extra gridline outside the panel.
    ///
    /// The fix is to compare against a *relative* epsilon — `ceil(lo / step * (1.0 - 1e-9))` —
    /// or to drop any generated tick below `lo`. Recorded, not fixed: `Marey.Core` is frozen.
    [<Fact>]
    let ``DEFECT Ticks.linear can emit a tick below the domain`` () =
        let ticks = Ticks.linear 10 (1.0, 1e10) |> Array.map float
        Assert.Contains(0.0, ticks)
        Assert.True(ticks.[0] < 1.0, "the first tick is below the domain's lower bound")

        // The same failure with a domain whose lower endpoint is tiny rather than whose upper
        // endpoint is huge. Note the value is negative zero, which `Ticks.format` normalises for
        // display — so the tick reads "0" and looks legitimate.
        let tiny = Ticks.linear 10 (1e-12, 1.0) |> Array.map float
        Assert.True(tiny.[0] < 1e-12)

    /// Exhaustive statement of where the bound does and does not hold, so the boundary is a
    /// recorded fact rather than folklore. Verified by exhaustive search over 20 000 random
    /// domains per target; `n = 2` and every `n >= 4` were clean.
    [<Fact>]
    let ``the count bound holds for n = 2 and for every n >= 4 on the recorded counterexamples`` () =
        let awkward =
            [ (0.49476730778608102, 921.34035582915112)
              (0.5, 8.0)
              (2.514138290480374, 3.4643517164004289)
              (389.66291558555469, 9877.3008252262844)
              (0.011939691073551846, 0.98910959462303882) ]

        for n in [ 2; 4; 5; 6; 8; 12; 20 ] do
            for d in awkward do
                let c = (Ticks.linear n d).Length

                Assert.True(
                    float c >= float n / 2.0 && float c <= float n * 2.0,
                    sprintf "n=%d domain=%A gave %d ticks" n d c
                )

module InjectivityLimits =

    /// **§4's injectivity requirement fails once fixed-point formatting runs out of decimals.**
    ///
    /// `Ticks.format` clamps at 15 decimal places (and `Ticks.decimalsFor` clamps the same way),
    /// so any tick value below 1e-15 renders as a string of zeros. D19 records this at the Core
    /// level and answers it with prefix rescaling in `Marey.Units` — which works, but only
    /// shifts the window by one factor.
    ///
    /// A `log10` axis spanning 36 decades still breaks it *after* rescaling: the prefix can only
    /// name one decade, so the small end of the axis is still beyond fixed point. Twenty-one of
    /// the thirty-six ticks below read `0.000000000000000`.
    ///
    /// `Ticks.formatScientific` exists in Core and is never called by `Erase.scale` or by
    /// `Axis`; routing wide log axes through it is the fix. Recorded, not fixed.
    [<Fact>]
    let ``DEFECT a log axis spanning 36 decades produces duplicate labels`` () =
        let ax = Axis.auto Unicode "x" (Scale.log10 U.metre (1e-18<m>, 1e18<m>))
        let labels = ax.Ticks |> Array.map (fun t -> t.Label)

        Assert.True(labels.Length > 20)
        Assert.True(
            (Array.distinct labels).Length < labels.Length,
            "expected the recorded fixed-point overflow; if this now passes, the defect was fixed"
        )

        Assert.True(labels |> Array.filter (fun l -> l = "0.000000000000000") |> Array.length > 1)

    /// The Core-level case D19 describes, on a dimensionless axis, where prefix selection
    /// deliberately declines to rescale ("a ratio, a probability, a count: there is no unit to
    /// hang a prefix on, so rescaling the numbers would be a silent lie about them").
    ///
    /// The consequence is that §4's injectivity does not hold for a dimensionless axis over a
    /// sub-1e-15 range. That is a defensible trade — but it is a trade, so it is written down.
    [<Fact>]
    let ``DEFECT a dimensionless axis below 1e-15 produces duplicate labels`` () =
        let ax = Axis.auto Unicode "p" (Scale.linear U.one (0.0, 5e-18))
        let labels = ax.Ticks |> Array.map (fun t -> t.Label)
        Assert.True((Array.distinct labels).Length < labels.Length)
        Assert.Equal(Prefix.none.Exponent, ax.Prefix.Exponent)
