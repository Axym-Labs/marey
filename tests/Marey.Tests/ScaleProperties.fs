/// §4 — the scale laws, verbatim from the design:
///
/// > * `∀ x ∈ domain. inverse (forward x) ≈ x` and `∀ t ∈ [0,1]. forward (inverse t) ≈ t`
/// > * `forward` is monotone
/// > * `TickHint n` returns between `n/2` and `2n` ticks, all within the domain
/// > * tick label rendering is injective (no two visible ticks get the same label)
///
/// The first two and monotonicity live here, over all five `ScaleKind`s. The two tick
/// properties live in `TickProperties`, which is also where the measured exceptions to §4's
/// stated count bound are recorded.
///
/// Every domain comes from a generator that respects the kind's own precondition — `log` needs
/// a positive domain, `reciprocal` must not straddle zero — because those preconditions are
/// enforced by `invalidArg` in `Scale` and a generator that ignored them would be testing the
/// exception message.
module Marey.Tests.ScaleProperties

open FsCheck.Xunit
open Xunit
open Marey
open Marey.Tests.Generators

let private fwd (s: Scale<1>) (x: float) = float (s.Forward x)
let private inv (s: Scale<1>) (t: float) = float (s.Inverse(t * 1.0<nd>))

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module RoundTrip =

    /// The endpoints are not approximate. `project` computes `(fwd lo - a) / span` where
    /// `a = fwd lo`, so the numerator is exactly zero, and `(fwd hi - a) / span` is exactly
    /// `span / span`. Measured deviation over 20 000 domains: 0.0. Asserting equality rather
    /// than approximate equality is what makes this test able to catch a regression.
    [<Property>]
    let ``forward maps the domain endpoints exactly onto 0 and 1`` (spec: ScaleSpec) =
        let s = ScaleSpec.build spec
        let lo, hi = ScaleSpec.domain spec
        fwd s lo = 0.0 && fwd s hi = 1.0

    /// §4, second half. This is the direction that matters for rendering — a panel position
    /// asked for by the layout has to come back as the same panel position — and it is
    /// well-conditioned for every kind, so it gets one uniform absolute tolerance.
    [<Property>]
    let ``forward (inverse t) = t for t in [0,1]`` (spec: ScaleSpec) (UnitT t) =
        let s = ScaleSpec.build spec
        Approx.abs' Tol.panel (fwd s (inv s t)) t

    /// §4, first half, stated as "to within a billionth of the axis".
    ///
    /// This form holds for all five kinds. The stronger *relative* form does not hold near the
    /// origin for `power` (whose inverse `y ** (1/p)` has an unbounded derivative at zero) or
    /// for `symlog` (which reconstructs small values from a projection anchored far away), so
    /// those two are covered by this property and by the relative one below only where they are
    /// well conditioned. See `Tol.scaleSpan` for the measurements.
    [<Property>]
    let ``inverse (forward x) = x to within a billionth of the span`` (spec: ScaleSpec) =
        let s = ScaleSpec.build spec
        let span = ScaleSpec.span spec

        ScaleSpec.samples 17 spec
        |> Array.forall (fun x -> Approx.scaled Tol.scaleSpan span (inv s (fwd s x)) x)

    /// §4, first half, in its strong relative form — for the kinds where it is achievable.
    [<Property>]
    let ``inverse (forward x) = x relatively, for the well-conditioned kinds`` (WellConditionedScale spec) =
        let s = ScaleSpec.build spec

        ScaleSpec.samples 17 spec
        |> Array.forall (fun x -> Approx.rel Tol.scaleRelative (inv s (fwd s x)) x)

    /// `Scale.power` is sign-preserving (`signPow`) precisely so that a signed domain works.
    /// The round trip is stated against the axis span for the reason above.
    [<Property>]
    let ``a signed power scale round-trips across zero`` (SignedPower(p, lo, hi)) =
        let s = Scale.power p U.one (lo, hi)
        let span = abs (hi - lo)

        Array.init 17 (fun i -> lo + (hi - lo) * float i / 16.0)
        |> Array.forall (fun x ->
            let rt = float (s.Inverse(s.Forward x))
            Approx.scaled Tol.scaleSpan span rt x && sign rt = sign x)

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Monotonicity =

    /// §4 — "`forward` is monotone". Non-strictly: two distinct data values may land on the same
    /// panel position once the projection has been rounded, and refusing that would be a test of
    /// float spacing rather than of the scale.
    ///
    /// Note this holds for `reciprocal` too, and non-trivially: `1/x` is *decreasing*, and it is
    /// the normalisation by a negative span that puts the axis back the right way round.
    [<Property>]
    let ``forward is monotone non-decreasing over an ascending domain`` (spec: ScaleSpec) =
        let s = ScaleSpec.build spec
        let ys = ScaleSpec.samples 65 spec |> Array.map (fwd s)
        Array.forall2 (fun a b -> a <= b) ys.[.. ys.Length - 2] ys.[1..]

    [<Property>]
    let ``inverse is monotone non-decreasing over [0,1]`` (spec: ScaleSpec) =
        let s = ScaleSpec.build spec
        let lo, hi = ScaleSpec.domain spec
        let ascending = hi >= lo
        let xs = Array.init 65 (fun i -> inv s (float i / 64.0))

        Array.forall2 (fun a b -> if ascending then a <= b else a >= b) xs.[.. xs.Length - 2] xs.[1..]

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 400, Replay = Seed, QuietOnSuccess = true)>]
module Combinators =

    /// D6 — the `ScaleKind` tag exists so a scale can be rebuilt. `withDomain` is *the* zoom/pan
    /// primitive, so "same kind, same unit, new domain" is the property that makes zooming a
    /// pure value change rather than a rebuild of the figure.
    [<Property>]
    let ``withDomain preserves kind and unit`` (spec: ScaleSpec) (LinearDomain(lo, hi)) =
        let s = ScaleSpec.build spec

        // A new domain must still satisfy the kind's own precondition, so reuse the original
        // domain shifted rather than an arbitrary one where the kind forbids it.
        let target =
            match spec with
            | SLog _
            | SPower _
            | SReciprocal _ ->
                let a, b = ScaleSpec.domain spec
                a * 1.5, b * 1.5
            | _ -> lo, hi

        let z = Scale.withDomain target s
        z.Kind = s.Kind && System.Object.ReferenceEquals(z.Unit, s.Unit) && z.Domain = target

    [<Property>]
    let ``reverse swaps the domain and flips the projection`` (spec: ScaleSpec) =
        let s = ScaleSpec.build spec
        let r = Scale.reverse s
        let lo, hi = ScaleSpec.domain spec

        r.Domain = (hi, lo)
        && r.Kind = s.Kind
        && ScaleSpec.samples 9 spec
           |> Array.forall (fun x -> Approx.abs' Tol.panel (float (r.Forward x)) (1.0 - fwd s x))

    /// D9 — "`Scale.Forward` does not clamp ... out-of-domain points project outside `[0,1]` and
    /// are removed by the panel's `Clip` node". The consequence is load-bearing: it is why a
    /// backend that cannot clip still gets correct output for the common case.
    ///
    /// Stated for linear scales because that is the kind whose domain can be extended in both
    /// directions without leaving the transform's own domain (`log` of a negative number is not
    /// a number).
    [<Property>]
    let ``forward does not clamp`` (LinearDomain(lo, hi)) =
        let s = Scale.linear U.one (lo, hi)
        let span = hi - lo
        fwd s (lo - span) < 0.0 && fwd s (hi + span) > 1.0

    [<Property>]
    let ``clamped does clamp, and only opt-in`` (LinearDomain(lo, hi)) =
        let c = Scale.clamped (Scale.linear U.one (lo, hi))
        let span = hi - lo

        let inRange x =
            let v = float (c.Forward x)
            v >= 0.0 && v <= 1.0

        inRange (lo - 10.0 * span) && inRange (hi + 10.0 * span) && inRange lo && inRange hi

    /// A zero-span domain is a real thing — one measurement, one category — and Scale.fs maps it
    /// to the panel centre rather than producing NaN. Worth a property because the natural
    /// implementation divides by zero.
    [<Property>]
    let ``a degenerate domain projects everything to the panel centre`` (FiniteFloat x) (FiniteFloat v) =
        let s = Scale.linear U.one (v, v)
        fwd s x = 0.5 && inv s 0.7 = v

    [<Property>]
    let ``domainOf ignores NaN and infinity`` (FiniteFloat a) (FiniteFloat b) =
        let xs =
            [| a; nan; b; infinity; -infinity; nan |]

        match Scale.domainOf xs with
        | None -> false
        | Some(lo, hi) -> float lo = min a b && float hi = max a b

    [<Fact>]
    let ``domainOf is None for empty and all-NaN input`` () =
        Assert.True((Scale.domainOf ([||]: float[])).IsNone)
        Assert.True((Scale.domainOf [| nan; infinity |]).IsNone)

    [<Property>]
    let ``paddedDomain never collapses to a point`` (FiniteFloat a) =
        match Scale.paddedDomain 0.05 [| a; a; a |] with
        | None -> false
        | Some(lo, hi) -> float hi > float lo

    [<Property>]
    let ``pointOf recognises paddedDomain's widening of a constant, and nothing else`` (FiniteFloat a) (LinearDomain(lo, hi)) =
        let xs = [| a; nan; a |]
        let widened = Scale.paddedDomain 0.05 xs |> Option.get

        Scale.pointOf 0.05 widened xs = Some a
        && Scale.pointOf 0.05 (lo, hi) xs = None
        && Scale.pointOf 0.05 widened [| lo; hi |] = None

    /// The 0.6.0 bug as a law: a constant inside a real span adds nothing to it, however large
    /// its own padding is — ±1 at zero, against a span that may be a millionth of that.
    [<Property>]
    let ``a widened point inside a span does not widen it`` (LinearDomain(lo, hi)) (UnitT p) =
        let v = min hi (lo + p * (hi - lo))
        let widened = Scale.paddedDomain 0.05 [| v |] |> Option.get
        Scale.shareDomain 0.05 [| (lo, hi), None; widened, Some v |] = (lo, hi)

    [<Property>]
    let ``a widened point outside the spans is inside the shared domain`` (LinearDomain(lo, hi)) (FiniteFloat v) =
        let widened = Scale.paddedDomain 0.05 [| v |] |> Option.get
        let slo, shi = Scale.shareDomain 0.05 [| (lo, hi), None; widened, Some v |]
        slo <= lo && shi >= hi && (v > slo || v = lo) && (v < shi || v = hi)

    [<Fact>]
    let ``points alone share paddedDomain's axis`` () =
        let widened v = Scale.paddedDomain 0.05 [| v |] |> Option.get
        Assert.Equal((-1.0, 1.0), Scale.shareDomain 0.05 [| widened 0.0, Some 0.0; widened 0.0, Some 0.0 |])
        Assert.Equal((-0.25, 5.25), Scale.shareDomain 0.05 [| widened 0.0, Some 0.0; widened 5.0, Some 5.0 |])

    [<Property>]
    let ``niceDomain contains the original domain`` (LinearDomain(lo, hi)) =
        let s = Scale.niceDomain 5 (Scale.linear U.one (lo, hi))
        let nlo, nhi = s.Domain
        float nlo <= lo && float nhi >= hi

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 300, Replay = Seed, QuietOnSuccess = true)>]
module Erasure =

    /// §2.3 — "erase *once*, at the point where a series becomes a scale ... there is exactly
    /// one implementation". So the erased view must be the same function as the measured one,
    /// not a re-derivation that can drift.
    [<Property>]
    let ``Erase.scale agrees with the scale it erased`` (spec: ScaleSpec) (UnitT t) =
        let s = ScaleSpec.build spec
        let e = Erase.scale s
        let lo, hi = ScaleSpec.domain spec

        e.Domain = (lo, hi)
        && e.Kind = s.Kind
        && e.Symbol = s.Unit.Symbol
        && e.Dim = s.Unit.Dim
        && e.ToSI = s.Unit.ToSI
        && e.OffsetSI = s.Unit.OffsetSI
        && (ScaleSpec.samples 9 spec
            |> Array.forall (fun x -> float (e.Project x) = fwd s x))
        && e.Invert(t * 1.0<nd>) = inv s t

    /// D16 — the erased witness carries the *whole* thing, because an electronvolt axis and a
    /// joule axis have identical `Dim` and differ by 10^19. Without `ToSI` the information is
    /// destroyed at save time (§15), not merely hidden.
    [<Fact>]
    let ``the erased witness distinguishes electronvolts from joules`` () =
        let ev = Erase.scale (Scale.linear NonSi.electronvolt (1.0<eV>, 1000.0<eV>))
        let j = Erase.scale (Scale.linear Derived.joule (1.0<J>, 1000.0<J>))

        Assert.Equal(ev.Dim, j.Dim)
        Assert.NotEqual(ev.ToSI, j.ToSI)

        let c = Erase.scale (Scale.linear NonSi.degreeCelsius (0.0<degC>, 100.0<degC>))
        let k = Erase.scale (Scale.linear U.kelvin (273.0<K>, 373.0<K>))
        Assert.Equal(c.Dim, k.Dim)
        Assert.NotEqual(c.OffsetSI, k.OffsetSI)

/// D45 — the categorical scale.
///
/// `ScaleKind.Ordinal` is the sixth kind and the only one whose ticks are named rather than
/// numbered. The claims here are what the decision rests on: that a category reaches the scale
/// as a dimensionless *index*, that its names survive erasure, and that the two combinators
/// which rebuild a scale do the categorical thing rather than the numeric one.
[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 200, Replay = Seed, QuietOnSuccess = true)>]
module Ordinal =

    let private levels = [| "control"; "treated"; "placebo"; "washout" |]

    /// Levels sit at band *centres*, so a bar of full band width fits in the panel and the
    /// first and last bands are as wide as the ones between them.
    [<Fact>]
    let ``levels project to band centres`` () =
        let s = Scale.ordinal levels
        let at i = float (s.Forward(float i))

        Assert.Equal(0.125, at 0, 12)
        Assert.Equal(0.375, at 1, 12)
        Assert.Equal(0.625, at 2, 12)
        Assert.Equal(0.875, at 3, 12)

    /// Distinct and order-preserving: the property a level set has to have for the axis to mean
    /// anything. `n` levels give `n` strictly increasing positions, whatever `n` is.
    [<Property>]
    let ``level positions are distinct and order-preserving`` (n: int) =
        let n = 1 + abs (n % 60)
        let s = Scale.ordinal (Array.init n (sprintf "level %d"))
        let ps = Array.init n (fun i -> float (s.Forward(float i)))

        Array.forall2 (<) ps.[.. n - 2] ps.[1..] || n = 1

    /// One tick per level, named, however many the caller asks for. The tick *count* is a
    /// negotiation for a continuous axis; a category has as many ticks as it has levels.
    [<Property>]
    let ``every level gets one named tick, whatever count is requested`` (requested: int) =
        let e = Erase.scale (Scale.ordinal levels)
        let ticks = e.Ticks(1 + abs (requested % 40))

        ticks.Length = levels.Length
        && Array.forall2 (fun (t: TickLabel) name -> t.Label = name) ticks levels

    /// §15 — a reloaded figure has only `IScaleErased`. The level names are in `Kind`, so they
    /// cross the boundary with everything else and no second erased interface is needed.
    [<Fact>]
    let ``the level names survive erasure`` () =
        let e = Erase.scale (Scale.ordinal levels)

        Assert.Equal(ScaleKind.Ordinal levels, e.Kind)
        Assert.Equal(Dim.zero, e.Dim)
        Assert.Equal("", e.Symbol)

    /// The measured and erased axis compilers must name a category identically — the same
    /// claim D16 makes for `ToSI`, and the reason both route through `Scale.levelLabel`.
    [<Fact>]
    let ``the measured and erased axis compilers agree on the labels`` () =
        let s = Scale.ordinal levels
        let measured = Axis.ofPacked Unicode "treatment" (Erase.packScale s)
        let erased = Axis.ofErased Unicode "treatment" (Erase.scale s)

        Assert.Equal<string[]>(levels, measured.Ticks |> Array.map (fun t -> t.Label))
        Assert.Equal<string[]>(levels, erased.Ticks |> Array.map (fun t -> t.Label))

        // No bracket: a category has no unit, and `Axis.title` drops the bracket for an empty
        // unit text rather than printing `treatment [1]`.
        Assert.Equal("treatment", measured.Title)

    /// A dimensionless axis is never rescaled — `Prefix.selectFor` refuses — so a 2000-level
    /// category cannot come back labelled in kilolevels.
    [<Fact>]
    let ``a categorical axis is never prefixed`` () =
        let s = Scale.ordinal (Array.init 2000 (sprintf "level %d"))
        let spec = Axis.ofPacked Unicode "id" (Erase.packScale s)

        Assert.Equal(0, spec.Prefix.Exponent)

    /// Zoom is meaningless on a category, and `Figure.unify` unions domains as raw numbers —
    /// so accepting a wider one here is how a shared categorical axis would end up with its
    /// bands in the wrong places.
    [<Fact>]
    let ``withDomain leaves a categorical scale alone`` () =
        let s = Scale.ordinal levels
        let stretched = Scale.withDomain (-5.0, 20.0) s

        Assert.Equal(s.Domain, stretched.Domain)
        Assert.Equal(s.Kind, stretched.Kind)

    /// Reversing a category reverses its level order. It is the one scale whose reversal is not
    /// a domain swap, because its domain carries nothing but the level count.
    [<Fact>]
    let ``reverse reverses the levels`` () =
        let s = Scale.ordinal levels
        let r = Scale.reverse s

        Assert.Equal(ScaleKind.Ordinal(Array.rev levels), r.Kind)
        Assert.Equal(s.Domain, r.Domain)

/// Preconditions that `Scale` enforces, and one that it does not.
module Preconditions =

    [<Fact>]
    let ``logBase rejects a base of one or below`` () =
        Assert.Throws<System.ArgumentException>(fun () -> Scale.logBase 1.0 U.one (1.0, 10.0) |> ignore)
        |> ignore

        Assert.Throws<System.ArgumentException>(fun () -> Scale.logBase 0.0 U.one (1.0, 10.0) |> ignore)
        |> ignore

        Assert.Throws<System.ArgumentException>(fun () -> Scale.logBase -2.0 U.one (1.0, 10.0) |> ignore)
        |> ignore

    [<Fact>]
    let ``power rejects a zero exponent`` () =
        Assert.Throws<System.ArgumentException>(fun () -> Scale.power 0.0 U.one (1.0, 10.0) |> ignore)
        |> ignore

    [<Fact>]
    let ``reciprocal rejects a domain that contains or touches zero`` () =
        Assert.Throws<System.ArgumentException>(fun () -> Scale.reciprocal U.one (-1.0, 1.0) |> ignore)
        |> ignore

        Assert.Throws<System.ArgumentException>(fun () -> Scale.reciprocal U.one (0.0, 10.0) |> ignore)
        |> ignore

    [<Fact>]
    let ``symlog rejects a non-positive linear threshold`` () =
        Assert.Throws<System.ArgumentException>(fun () -> Scale.symlog 10.0 0.0 U.one (-1.0, 1.0) |> ignore)
        |> ignore

    // ------------------------------------------------------------------ DEFECT, not a feature
    //
    // `Scale.symlog` validates `linthresh` but **not its logarithm base**, while `Scale.logBase`
    // validates both. A symlog scale built with a base of 1 divides by `log 1.0 = 0` and every
    // projection outside the linear region is NaN; a base of 0 gives `log 0.0 = -infinity` and
    // silently produces a scale that maps everything to a constant. Neither raises.
    //
    // Reported rather than fixed — `Marey.Core` is frozen. The tests below assert the
    // *current* behaviour so that the finding is visible in the suite and so that fixing it is
    // a deliberate, test-breaking act rather than a silent one.

    [<Fact>]
    let ``DEFECT symlog does not validate its base (base = 1 yields NaN)`` () =
        let s = Scale.symlog 1.0 1.0 U.one (-10.0, 10.0)
        Assert.True(System.Double.IsNaN(float (s.Forward 5.0)), "expected the current, unvalidated behaviour")

    [<Fact>]
    let ``DEFECT symlog does not validate its base (base = 0 yields a constant map)`` () =
        let s = Scale.symlog 0.0 1.0 U.one (-10.0, 10.0)
        let v = float (s.Forward 5.0)
        Assert.False(System.Double.IsNaN v)
        Assert.Equal(v, float (s.Forward 9.0), 12)
