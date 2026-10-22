namespace Marey

open System.Collections
open System.Collections.Generic

/// **One value and its unit's witness**: what `U.metrePerSecondSquared.Of 9.81<m/s^2>` makes. The
/// measure checks the value against the witness when it is made, and arithmetic with a `Q` or
/// another `Quantity` computes the witness of its result, so a derived quantity names no unit.
[<Sealed>]
type Quantity<[<Measure>] 'u>(value: float<'u>, unit: U<'u>) =

    member _.Value = value

    member _.Unit = unit

    static member (*)(a: Quantity<'u>, b: Quantity<'v>) : Quantity<'u * 'v> = Quantity(a.Value * b.Value, U.mul a.Unit b.Unit)

    static member (/)(a: Quantity<'u>, b: Quantity<'v>) : Quantity<'u / 'v> = Quantity(a.Value / b.Value, U.div a.Unit b.Unit)

    static member (*)(a: Quantity<'u>, k: float) : Quantity<'u> = Quantity(a.Value * k, a.Unit)

    static member (*)(k: float, a: Quantity<'u>) : Quantity<'u> = Quantity(k * a.Value, a.Unit)

    static member (/)(a: Quantity<'u>, k: float) : Quantity<'u> = Quantity(a.Value / k, a.Unit)

    static member (/)(k: float, a: Quantity<'u>) : Quantity<1 / 'u> = Quantity(k / a.Value, U.recip a.Unit)

    /// The sum in `a`'s unit, `b` read in it: a metre and a kilometre witness add as lengths.
    static member (+)(a: Quantity<'u>, b: Quantity<'u>) : Quantity<'u> = Quantity(a.Value + U.convert b.Unit a.Unit b.Value, a.Unit)

    static member (-)(a: Quantity<'u>, b: Quantity<'u>) : Quantity<'u> = Quantity(a.Value - U.convert b.Unit a.Unit b.Value, a.Unit)

    static member (~-)(a: Quantity<'u>) : Quantity<'u> = Quantity(-a.Value, a.Unit)

    override _.ToString() = sprintf "%g %s" (float value) unit.Symbol

/// **Values and their unit's witness**: a series that knows what it is measured in. Made with
/// `U.second.Of ts` or `Q.ofSeq U.second ts`, it is drawn without a witness, `Chart.line t x`,
/// because it carries its own. Arithmetic computes the witness of its result: `0.5 * g * t * t`,
/// with `g` a `Quantity<m/s^2>` and `t` a `Q<s>`, is a `Q<m>` that titles its axis `[m]`. Anything
/// arithmetic does not reach goes through `Q.map`, which names the result's unit.
///
/// A `Q` is a sequence of its values, so it passes wherever a `seq<float<'u>>` is taken. Its values
/// are its own: made from a copy, and read back as one.
[<Sealed>]
type Q<[<Measure>] 'u> internal (unit: U<'u>, values: float<'u>[]) =

    static let pairwise (op: string) (a: float<'a>[]) (b: float<'b>[]) =
        if a.Length <> b.Length then
            invalidArg "b" (sprintf "Marey: %s of a series of %d values and one of %d; they must be the same length." op a.Length b.Length)

    member _.Unit = unit

    /// A copy of the values.
    member _.Values = Array.copy values

    member _.Length = values.Length

    member _.Item(i: int) = values.[i]

    member internal _.Raw = values

    static member (*)(a: Q<'u>, b: Q<'v>) : Q<'u * 'v> =
        pairwise "the product" a.Raw b.Raw
        Q(U.mul a.Unit b.Unit, Array.map2 (*) a.Raw b.Raw)

    static member (/)(a: Q<'u>, b: Q<'v>) : Q<'u / 'v> =
        pairwise "the quotient" a.Raw b.Raw
        Q(U.div a.Unit b.Unit, Array.map2 (/) a.Raw b.Raw)

    static member (*)(a: Q<'u>, b: Quantity<'v>) : Q<'u * 'v> = Q(U.mul a.Unit b.Unit, a.Raw |> Array.map (fun x -> x * b.Value))

    static member (*)(a: Quantity<'v>, b: Q<'u>) : Q<'v * 'u> = Q(U.mul a.Unit b.Unit, b.Raw |> Array.map (fun x -> a.Value * x))

    static member (/)(a: Q<'u>, b: Quantity<'v>) : Q<'u / 'v> = Q(U.div a.Unit b.Unit, a.Raw |> Array.map (fun x -> x / b.Value))

    static member (/)(a: Quantity<'v>, b: Q<'u>) : Q<'v / 'u> = Q(U.div a.Unit b.Unit, b.Raw |> Array.map (fun x -> a.Value / x))

    static member (*)(a: Q<'u>, k: float) : Q<'u> = Q(a.Unit, a.Raw |> Array.map (fun x -> x * k))

    static member (*)(k: float, a: Q<'u>) : Q<'u> = Q(a.Unit, a.Raw |> Array.map (fun x -> k * x))

    static member (/)(a: Q<'u>, k: float) : Q<'u> = Q(a.Unit, a.Raw |> Array.map (fun x -> x / k))

    static member (/)(k: float, a: Q<'u>) : Q<1 / 'u> = Q(U.recip a.Unit, a.Raw |> Array.map (fun x -> k / x))

    /// The sum in `a`'s unit, `b` read in it.
    static member (+)(a: Q<'u>, b: Q<'u>) : Q<'u> =
        pairwise "the sum" a.Raw b.Raw
        Q(a.Unit, Array.map2 (fun x y -> x + U.convert b.Unit a.Unit y) a.Raw b.Raw)

    static member (-)(a: Q<'u>, b: Q<'u>) : Q<'u> =
        pairwise "the difference" a.Raw b.Raw
        Q(a.Unit, Array.map2 (fun x y -> x - U.convert b.Unit a.Unit y) a.Raw b.Raw)

    static member (+)(a: Q<'u>, b: Quantity<'u>) : Q<'u> =
        let v = U.convert b.Unit a.Unit b.Value
        Q(a.Unit, a.Raw |> Array.map (fun x -> x + v))

    static member (-)(a: Q<'u>, b: Quantity<'u>) : Q<'u> =
        let v = U.convert b.Unit a.Unit b.Value
        Q(a.Unit, a.Raw |> Array.map (fun x -> x - v))

    static member (~-)(a: Q<'u>) : Q<'u> = Q(a.Unit, a.Raw |> Array.map (fun x -> -x))

    override _.ToString() = sprintf "%d values in %s" values.Length unit.Symbol

    interface IEnumerable<float<'u>> with
        member _.GetEnumerator() : IEnumerator<float<'u>> = (values :> IEnumerable<float<'u>>).GetEnumerator()

    interface IEnumerable with
        member _.GetEnumerator() : IEnumerator = values.GetEnumerator()

/// Making and transforming a `Q`.
[<RequireQualifiedAccess>]
module Q =

    /// Values in the unit `u` names: `Q.ofSeq U.second ts`, which is `U.second.Of ts`.
    let ofSeq (u: U<'u>) (values: seq<float<'u>>) : Q<'u> = Q(u, Array.ofSeq values)

    /// `n` values, `f i` for each.
    let init (u: U<'u>) (n: int) (f: int -> float<'u>) : Q<'u> = Q(u, Array.init n f)

    /// Each value through `f`, into the unit `u` names. The way to a quantity arithmetic does not
    /// reach: `t |> Q.map U.metre (fun t -> a * exp (-t / tau) * cos (omega * t))`.
    let map (u: U<'v>) (f: float<'u> -> float<'v>) (q: Q<'u>) : Q<'v> = Q(u, Array.map f q.Raw)

    /// Two series of one length, pair by pair, into the unit `u` names.
    let map2 (u: U<'w>) (f: float<'u> -> float<'v> -> float<'w>) (a: Q<'u>) (b: Q<'v>) : Q<'w> =
        if a.Length <> b.Length then
            invalidArg "b" (sprintf "Marey: Q.map2 over a series of %d values and one of %d." a.Length b.Length)

        Q(u, Array.map2 f a.Raw b.Raw)

    /// The values, a copy.
    let values (q: Q<'u>) : float<'u>[] = q.Values

    /// The witness.
    let unit (q: Q<'u>) : U<'u> = q.Unit

    /// The same values read in another unit of their dimension: a `Q<s>` in minutes is
    /// `Q.convert U.minute t`.
    let convert (into: U<'v>) (q: Q<'u>) : Q<'v> = Q(into, q.Raw |> Array.map (U.convert q.Unit into))

/// `U.second.Of ts`: values, or one value, with the witness that names their unit.
[<AutoOpen>]
module QuantityExtensions =

    type U<[<Measure>] 'u> with

        /// The values with this unit's witness: a `Q` a chart draws without one.
        member u.Of(values: seq<float<'u>>) : Q<'u> = Q.ofSeq u values

        /// One value with this unit's witness: a constant whose products and quotients with a `Q`
        /// know their unit.
        member u.Of(value: float<'u>) : Quantity<'u> = Quantity(value, u)
