namespace Quiver

/// What kind of scale this is, as *data*.
///
/// The design's `Scale<'u>` is four function fields and a domain. That is enough to render
/// and nothing else: you cannot serialize it (§15 — "design the file format for that from the
/// start"), you cannot show it in a REPL, you cannot check in a test that `Scale.log10`
/// actually built a log scale rather than a linear one that happens to agree at the endpoints,
/// and you cannot generate the right ticks without asking the closure.
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
    /// `1/x`. Arrhenius plots against `1/T`, and the reason it is on §4's ship list.
    | Reciprocal

/// §4 — A scale maps **data space to normalized panel space**.
///
/// `Forward` deliberately does *not* clamp. A point outside the domain projects outside
/// `[0,1]` and is removed by the panel's `Clip` node in the IR (§5). Clamping here would
/// silently pile out-of-range data onto the panel edge, which is a lie about the data — and
/// it would also break the `inverse (forward x) = x` round-trip that §4 asks to be proven.
type Scale<[<Measure>] 'u> =
    { /// The runtime unit witness. This is the field that makes an axis able to label itself.
      Unit: U<'u>
      /// Structural description, for serialization, tests and display.
      Kind: ScaleKind
      Domain: float<'u> * float<'u>
      Forward: float<'u> -> float<nd>
      Inverse: float<nd> -> float<'u>
      /// Ticks as an **array**, not the `list` of the design sketch. §1.3 warns that list
      /// allocation on the hot path is the difference between 60 fps and 6; ticks are not a
      /// hot path, but `Quiver.Core.Ticks` produces arrays and converting at every axis
      /// render is pure waste.
      TickHint: int -> float<'u>[] }

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
    /// §4: "log10 of a dimensioned quantity is not itself dimensioned — a subtlety the type
    /// system will correctly force you to acknowledge." That acknowledgement is the
    /// `float lo` on the way in and the `FloatWithMeasure` on the way out; there is no way to
    /// write it that hides the fact, and there should not be.
    let logBase (b: float) (u: U<'u>) (domain: float<'u> * float<'u>) : Scale<'u> =
        if b <= 0.0 || b = 1.0 then
            invalidArg "b" "Quiver: a logarithmic scale needs a positive base other than 1."

        let lnB = log b
        ofTransform (ScaleKind.Log b) u domain (fun x -> log x / lnB) (fun y -> b ** y) (fun n -> Ticks.log10 n domain)

    let log10 (u: U<'u>) (domain: float<'u> * float<'u>) = logBase 10.0 u domain
    let log2 (u: U<'u>) (domain: float<'u> * float<'u>) = logBase 2.0 u domain
    let ln (u: U<'u>) (domain: float<'u> * float<'u>) = logBase System.Math.E u domain

    /// `x^p`, sign-preserving so negative domains work. `power 0.5` is `sqrt`.
    let power (p: float) (u: U<'u>) (domain: float<'u> * float<'u>) : Scale<'u> =
        if p = 0.0 then
            invalidArg "p" "Quiver: a power scale with exponent 0 is constant and has no inverse."

        let inline signPow (e: float) (x: float) =
            if x < 0.0 then -((-x) ** e) else x ** e

        ofTransform (ScaleKind.Power p) u domain (signPow p) (signPow (1.0 / p)) (fun n -> Ticks.linear n domain)

    let sqrt (u: U<'u>) (domain: float<'u> * float<'u>) = power 0.5 u domain

    /// `1/x`. The Arrhenius scale: plot `ln k` against a reciprocal-temperature axis and read
    /// the activation energy off the slope, with the axis still labelled in kelvin.
    let reciprocal (u: U<'u>) (domain: float<'u> * float<'u>) : Scale<'u> =
        let lo, hi = domain

        if float lo * float hi <= 0.0 then
            invalidArg "domain" "Quiver: a reciprocal scale's domain must not contain or touch zero."

        ofTransform ScaleKind.Reciprocal u domain (fun x -> 1.0 / x) (fun y -> 1.0 / y) (fun n -> Ticks.linear n domain)

    /// Linear within `+/-linthresh`, logarithmic beyond. Handles zero and sign changes, which
    /// is the whole reason to reach for it.
    let symlog (b: float) (linthresh: float) (u: U<'u>) (domain: float<'u> * float<'u>) : Scale<'u> =
        if linthresh <= 0.0 then
            invalidArg "linthresh" "Quiver: symlog needs a positive linear threshold."

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

    // ---- Combinators -----------------------------------------------------------------

    /// Reverse the direction of a scale by swapping its domain. `Kind` and `Unit` survive,
    /// which is why they had to be data.
    let reverse (s: Scale<'u>) : Scale<'u> =
        let lo, hi = s.Domain
        let d = (hi, lo)

        match s.Kind with
        | ScaleKind.Linear -> linear s.Unit d
        | ScaleKind.Log b -> logBase b s.Unit d
        | ScaleKind.Symlog(b, l) -> symlog b l s.Unit d
        | ScaleKind.Power p -> power p s.Unit d
        | ScaleKind.Reciprocal -> reciprocal s.Unit d

    /// Rebuild the same kind of scale over a new domain. This is *the* zoom/pan primitive:
    /// interaction changes the domain and nothing else, so a zoom is a pure value change and
    /// the diff in §5.2 sees exactly the subtree that moved.
    let withDomain (domain: float<'u> * float<'u>) (s: Scale<'u>) : Scale<'u> =
        match s.Kind with
        | ScaleKind.Linear -> linear s.Unit domain
        | ScaleKind.Log b -> logBase b s.Unit domain
        | ScaleKind.Symlog(b, l) -> symlog b l s.Unit domain
        | ScaleKind.Power p -> power p s.Unit domain
        | ScaleKind.Reciprocal -> reciprocal s.Unit domain

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
