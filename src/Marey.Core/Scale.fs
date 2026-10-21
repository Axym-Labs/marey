namespace Marey

/// What kind of scale this is, as *data*.
///
/// The design's `Scale<'u>` is four function fields and a domain. That is enough to render and
/// nothing else: you cannot serialize it, you cannot show it in a REPL, you cannot check in a test
/// that `Scale.log10` actually built a log scale rather than a linear one that happens to agree at
/// the endpoints, and you cannot generate the right ticks without asking the closure.
///
/// One extra tag field fixes all four. It is the cheapest thing in this file.
[<RequireQualifiedAccess>]
type ScaleKind =
    | Linear
    /// `Log 10.0`, `Log 2.0`, `Log System.Math.E`.
    | Log of logBase: float
    /// Linear within `+/-linthresh`, logarithmic outside. The scale for signed data spanning
    /// orders of magnitude — a residual plot, a divergence field.
    | Symlog of logBase: float * linthresh: float
    /// `x^exponent`. `Power 0.5` is the square-root scale.
    | Power of exponent: float
    /// `1/x`. Arrhenius plots against `1/T`.
    | Reciprocal

    /// **A categorical axis**, `n` named levels in `n` equal bands.
    ///
    /// The levels ride in the tag because a category *is* its level set, and that is data in
    /// exactly the sense the note at the top of this type argues for. It serializes (a reloaded
    /// figure keeps a categorical axis with its names intact, through `IScaleErased.Kind`, and no
    /// new erased interface is needed for it), it is inspectable in a test, and adding it here
    /// makes the compiler enumerate every consumer that has to decide what a category means — which
    /// a sibling type would not have done.
    | Ordinal of levels: string[]

/// A scale maps **data space to normalized panel space**.
///
/// `Forward` deliberately does *not* clamp. A point outside the domain projects outside
/// `[0,1]` and is removed by the panel's `Clip` node in the IR. Clamping here would
/// silently pile out-of-range data onto the panel edge, which is a lie about the data — and
/// it would also break the `inverse (forward x) = x` round-trip the tests prove.
type Scale<[<Measure>] 'u> =
    { /// The runtime unit witness. This is the field that makes an axis able to label itself.
      Unit: U<'u>
      /// Structural description, for serialization, tests and display.
      Kind: ScaleKind
      Domain: float<'u> * float<'u>
      Forward: float<'u> -> float<nd>
      Inverse: float<nd> -> float<'u>
      /// Ticks as an **array**, not a `list`. List allocation on the hot path is the difference
      /// between 60 fps and 6; ticks are not a hot path, but `Marey.Core.Ticks` produces arrays and
      /// converting at every axis render is pure waste.
      TickHint: int -> float<'u>[] }

/// Scales: linear, logarithmic, symmetric-log, power, reciprocal and categorical, and the
/// operations on them.
[<RequireQualifiedAccess>]
module Scale =

    let inline private nd (x: float) : float<nd> = x * 1.0<nd>
    let inline private unNd (x: float<nd>) : float = x / 1.0<nd>

    let inline private tag<[<Measure>] 'u> (x: float) : float<'u> =
        LanguagePrimitives.FloatWithMeasure<'u> x

    /// The general constructor: give it a monotone `float -> float` and its inverse on raw
    /// values, and it builds the measured, normalized, tick-bearing scale around them.
    ///
    /// Every named scale below is one call to this. That is the point — a new scale is a pair
    /// of raw functions plus a tick strategy, not a new type and not a backend change.
    let private ofTransform<[<Measure>] 'u>
        (kind: ScaleKind)
        (u: U<'u>)
        (lo: float<'u>, hi: float<'u>)
        (fwd: float -> float)
        (inv: float -> float)
        (ticks: int -> float<'u>[])
        : Scale<'u> =
        let a = fwd (float lo)
        let b = fwd (float hi)
        let span = b - a

        // A zero-span domain would give NaN for every point. Map everything to the panel
        // centre instead: a single-valued axis is a real thing (one measurement, one
        // category) and it should render, not produce an empty panel.
        let inline project (x: float) =
            if span = 0.0 then 0.5 else (fwd x - a) / span

        let inline unproject (t: float) =
            if span = 0.0 then float lo else inv (a + span * t)

        { Unit = u
          Kind = kind
          Domain = (lo, hi)
          Forward = fun x -> nd (project (float x))
          Inverse = fun t -> tag<'u> (unproject (unNd t))
          TickHint = ticks }

    let linear (u: U<'u>) (domain: float<'u> * float<'u>) : Scale<'u> =
        ofTransform ScaleKind.Linear u domain id id (fun n -> Ticks.linear n domain)

    /// Logarithm to an arbitrary base.
    ///
    /// "log10 of a dimensioned quantity is not itself dimensioned — a subtlety the type
    /// system will correctly force you to acknowledge." That acknowledgement is the
    /// `float lo` on the way in and the `FloatWithMeasure` on the way out; there is no way to
    /// write it that hides the fact, and there should not be.
    let logBase (b: float) (u: U<'u>) (domain: float<'u> * float<'u>) : Scale<'u> =
        if b <= 0.0 || b = 1.0 then
            invalidArg "b" "Marey: a logarithmic scale needs a positive base other than 1."

        let lnB = log b
        ofTransform (ScaleKind.Log b) u domain (fun x -> log x / lnB) (fun y -> b ** y) (fun n -> Ticks.log10 n domain)

    let log10 (u: U<'u>) (domain: float<'u> * float<'u>) = logBase 10.0 u domain
    let log2 (u: U<'u>) (domain: float<'u> * float<'u>) = logBase 2.0 u domain
    let ln (u: U<'u>) (domain: float<'u> * float<'u>) = logBase System.Math.E u domain

    /// `x^p`, sign-preserving so negative domains work. `power 0.5` is `sqrt`.
    let power (p: float) (u: U<'u>) (domain: float<'u> * float<'u>) : Scale<'u> =
        if p = 0.0 then
            invalidArg "p" "Marey: a power scale with exponent 0 is constant and has no inverse."

        let inline signPow (e: float) (x: float) =
            if x < 0.0 then -((-x) ** e) else x ** e

        ofTransform (ScaleKind.Power p) u domain (signPow p) (signPow (1.0 / p)) (fun n -> Ticks.linear n domain)

    let sqrt (u: U<'u>) (domain: float<'u> * float<'u>) = power 0.5 u domain

    /// `1/x`. The Arrhenius scale: plot `ln k` against a reciprocal-temperature axis and read
    /// the activation energy off the slope, with the axis still labelled in kelvin.
    let reciprocal (u: U<'u>) (domain: float<'u> * float<'u>) : Scale<'u> =
        let lo, hi = domain

        if float lo * float hi <= 0.0 then
            invalidArg "domain" "Marey: a reciprocal scale's domain must not contain or touch zero."

        ofTransform ScaleKind.Reciprocal u domain (fun x -> 1.0 / x) (fun y -> 1.0 / y) (fun n -> Ticks.linear n domain)

    /// Linear within `+/-linthresh`, logarithmic beyond. Handles zero and sign changes, which
    /// is the whole reason to reach for it.
    let symlog (b: float) (linthresh: float) (u: U<'u>) (domain: float<'u> * float<'u>) : Scale<'u> =
        if linthresh <= 0.0 then
            invalidArg "linthresh" "Marey: symlog needs a positive linear threshold."

        let lnB = log b

        let fwd (x: float) =
            let a = abs x

            if a <= linthresh then
                x
            else
                let s = if x < 0.0 then -1.0 else 1.0
                s * linthresh * (1.0 + log (a / linthresh) / lnB)

        let inv (y: float) =
            let a = abs y

            if a <= linthresh then
                y
            else
                let s = if y < 0.0 then -1.0 else 1.0
                s * linthresh * (b ** (a / linthresh - 1.0))

        ofTransform (ScaleKind.Symlog(b, linthresh)) u domain fwd inv (fun n -> Ticks.linear n domain)

    /// The label for a tick sitting at level index `v`, and the only place that mapping is
    /// written. `Erase.scale` and the axis compiler both format ordinal ticks through this, so
    /// the measured and erased paths cannot drift — the same argument as for `ToSI`.
    let levelLabel (levels: string[]) (v: float) =
        let i = int (Num.roundEven v)
        if i >= 0 && i < levels.Length then levels.[i] else ""

    let private ordinalIn<[<Measure>] 'u> (u: U<'u>) (levels: string[]) : Scale<'u> =
        let n = levels.Length

        if n = 0 then
            invalidArg "levels" "Marey: an ordinal scale needs at least one level."

        // Level `i` sits at index `i`, and the domain is half a band wider at each end, so the
        // levels land at band *centres* and a bar of full band width fits inside the panel.
        let lo, hi = -0.5, float n - 0.5
        let span = hi - lo

        { Unit = u
          Kind = ScaleKind.Ordinal levels
          Domain = (tag<'u> lo, tag<'u> hi)
          Forward = fun x -> nd ((float x - lo) / span)
          Inverse = fun t -> tag<'u> (lo + unNd t * span)
          // One tick per level, always. The requested count is what a *continuous* axis
          // negotiates with the tick ladder; a category has as many ticks as it has levels,
          // and thinning them is the collision-avoidance layer's job, not this one's.
          TickHint = fun _ -> Array.init n (fun i -> tag<'u> (float i)) }

    /// **A categorical axis**: `n` levels, one band each, level `i` at index `i`.
    ///
    /// The type is `Scale<1>` and not `Scale<'k>`, and that is the decision rather than an
    /// accident. A category has no measure, so a measure-indexed scale over one is a
    /// contradiction; but a category also never needs to *reach* a scale as a `'k`. The layer
    /// that knows the category type maps each value to its level index while the row type is
    /// still alive, and what arrives here is the index — a dimensionless number, which is what
    /// `float<1>` says. The names travel beside it in `Kind`, as data.
    ///
    /// Everything downstream then applies unchanged: `Prefix.selectFor` refuses to rescale a
    /// dimensionless axis, so no categorical axis ever grows a `k` prefix; `Axis.title` drops
    /// the bracket when the unit text is empty, so a categorical axis is titled by name alone;
    /// and `GeomBar`'s width comes from the median spacing between projected points, which over
    /// evenly spaced level indices is exactly one band.
    let ordinal (levels: string[]) : Scale<1> = ordinalIn U.one levels

    // ---- Combinators -----------------------------------------------------------------

    /// Rebuild a scale of a given kind, over a given domain: the one place the tag maps back
    /// to a constructor.
    ///
    /// It was once written three times — here twice and once in `Marey.Plot`'s
    /// `Figure` — and adding a sixth `ScaleKind` is what made that expensive: three matches
    /// to extend, and nothing but a build error at each to connect them. One function, so a
    /// seventh kind is one branch.
    let ofKind (k: ScaleKind) (u: U<'u>) (domain: float<'u> * float<'u>) : Scale<'u> =
        match k with
        | ScaleKind.Linear -> linear u domain
        | ScaleKind.Log b -> logBase b u domain
        | ScaleKind.Symlog(b, l) -> symlog b l u domain
        | ScaleKind.Power p -> power p u domain
        | ScaleKind.Reciprocal -> reciprocal u domain
        // The domain is not the input: a categorical one is fixed by the level count, and the
        // levels are in the kind.
        | ScaleKind.Ordinal levels -> ordinalIn u levels

    /// Reverse the direction of a scale by swapping its domain. `Kind` and `Unit` survive,
    /// which is why they had to be data.
    let reverse (s: Scale<'u>) : Scale<'u> =
        let lo, hi = s.Domain

        match s.Kind with
        // Reversing a category reverses its level *order*. It is the one scale whose reversal
        // is not a domain swap, because its domain says nothing but how many levels there are.
        | ScaleKind.Ordinal levels -> ordinalIn s.Unit (Array.rev levels)
        | k -> ofKind k s.Unit (hi, lo)

    /// Rebuild the same kind of scale over a new domain. This is *the* zoom/pan primitive:
    /// interaction changes the domain and nothing else, so a zoom is a pure value change and
    /// the scene diff sees exactly the subtree that moved.
    let withDomain (domain: float<'u> * float<'u>) (s: Scale<'u>) : Scale<'u> =
        match s.Kind with
        // A categorical axis has no domain to widen, and accepting one here is how a shared
        // categorical axis would end up with its bands in the wrong places — `Figure.unify`
        // unions domains as raw numbers and would otherwise stretch the bands of every panel
        // it touched.
        | ScaleKind.Ordinal _ -> s
        | k -> ofKind k s.Unit domain

    /// Clamp projected values into `[0,1]`. Opt-in, never the default — see the note on
    /// `Forward` above.
    let clamped (s: Scale<'u>) : Scale<'u> =
        { s with
            Forward = fun x -> nd (max 0.0 (min 1.0 (unNd (s.Forward x)))) }

    /// Widen the domain to end on nice tick values.
    let niceDomain (target: int) (s: Scale<'u>) : Scale<'u> =
        withDomain (Ticks.expandToNice target s.Domain) s

    /// Domain covering the data, or `None` for empty / all-NaN input.
    ///
    /// NaN filtering happens here rather than being the caller's problem, because
    /// `Array.min` over data containing a NaN returns NaN, which then produces an entirely
    /// blank plot with no error — one of the great time-wasters of scientific plotting.
    let domainOf (xs: float<'u>[]) : (float<'u> * float<'u>) option =
        let mutable lo = System.Double.PositiveInfinity
        let mutable hi = System.Double.NegativeInfinity
        let mutable any = false

        for x in xs do
            let v = float x

            if not (System.Double.IsNaN v) && not (System.Double.IsInfinity v) then
                any <- true
                if v < lo then lo <- v
                if v > hi then hi <- v

        if any then
            Some(tag<'u> lo, tag<'u> hi)
        else
            None

    /// Domain covering the data with a fractional margin on each side, never collapsing to a
    /// point. `padded 0.05` is the conventional 5% breathing room.
    let paddedDomain (fraction: float) (xs: float<'u>[]) : (float<'u> * float<'u>) option =
        domainOf xs
        |> Option.map (fun (lo, hi) ->
            let span = float hi - float lo
            // A single distinct value still needs a non-degenerate axis around it.
            let pad = if span = 0.0 then (if float lo = 0.0 then 1.0 else abs (float lo) * fraction) else span * fraction
            tag<'u> (float lo - pad), tag<'u> (float hi + pad))

    /// The value `domain` is `paddedDomain`'s widening of, or `None` when it is a real span.
    ///
    /// The widening around a single value is there so that a constant has an axis at all, and it
    /// says nothing about the data: at zero it is ±1 in whatever unit the axis is in. `Some v`
    /// exactly when `xs` has the one distinct finite value `v` and `domain` is still the padding
    /// `paddedDomain fraction` gives it, so a domain that was stated or widened since is a span.
    let pointOf (fraction: float) (domain: float<'u> * float<'u>) (xs: float<'u>[]) : float<'u> option =
        match domainOf xs with
        | Some(lo, hi) when lo = hi && paddedDomain fraction [| lo |] = Some domain -> Some lo
        | _ -> None

    /// The domain several series share on one axis, from each one's domain and its `pointOf`.
    ///
    /// A real span enters as it is, padding included. A widened point enters as its value only,
    /// so a flat reference line at zero cannot stretch a millimetre axis to ±1 m; a value outside
    /// every span gets `fraction` of the shared span as room, so it is not drawn on the edge of
    /// the panel. With no real span on the axis the points are padded among themselves, which is
    /// `paddedDomain` again and what keeps a panel of one constant on a non-degenerate axis.
    let shareDomain (fraction: float) (entries: ((float<'u> * float<'u>) * float<'u> option)[]) : float<'u> * float<'u> =
        let spans = entries |> Array.choose (fun (d, point) -> if point.IsNone then Some d else None)
        let points = entries |> Array.choose snd

        if spans.Length = 0 then
            match paddedDomain fraction points with
            | Some d -> d
            | None -> invalidArg "entries" "Marey: no domains to share."
        else
            let lo = spans |> Array.map fst |> Array.min
            let hi = spans |> Array.map snd |> Array.max

            if points.Length = 0 then
                lo, hi
            else
                let plo = Array.min points
                let phi = Array.max points
                let room = (max hi phi - min lo plo) * fraction
                (if plo < lo then plo - room else lo), (if phi > hi then phi + room else hi)
