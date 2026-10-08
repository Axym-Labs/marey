/// D129 — numbers rounded and formatted by Marey's arithmetic rather than the platform's, and
/// gate 2 of `m5-plan.md` on the .NET side.
///
/// Each function replaced a platform call whose output every golden depends on, so each is
/// checked against that call: the replacement has to write what the platform wrote. Under Fable
/// the platform calls are the ones that disagree, which is why the replacements exist; the Node
/// job runs the goldens against them there. `Num.shortest` is "R" itself on .NET, so only that
/// job can test it.
module Marey.Tests.NumProperties

open System
open FsCheck.Xunit
open Xunit
open Marey

let private invariant = Globalization.CultureInfo.InvariantCulture

let private patterns = [| "0"; "0.#"; "0.##"; "0.###"; "0.####"; "0.#####"; "0.######" |]

/// What `Num.at` was before D129.
let private atBefore (decimals: int) (v: float) =
    if Double.IsNaN v || Double.IsInfinity v then
        "0"
    else
        let d = max 0 (min 6 decimals)
        let r = Math.Round(v, d, MidpointRounding.AwayFromZero)
        let r = if r = 0.0 then 0.0 else r
        r.ToString(patterns.[d], invariant)

/// Doubles that land on and next to an exact decimal half, at every scale `at` and `fixedPoint`
/// are asked about, plus raw bit patterns.
let private sweep (n: int) =
    let rng = Random 129

    Array.init n (fun _ ->
        match rng.Next 5 with
        | 0 -> (rng.NextDouble() - 0.5) * 2000.0
        | 1 -> float (rng.Next(-100000, 100000)) / 200.0
        | 2 -> float (rng.Next(-100000, 100000)) / 1000.0 + 0.0005
        | 3 -> (rng.NextDouble() - 0.5) * Math.Pow(10.0, float (rng.Next(-12, 14)))
        | _ ->
            let x = BitConverter.Int64BitsToDouble(rng.NextInt64())
            if Double.IsFinite x && abs x < 1e9 then x else 0.5)

[<Property>]
let ``roundAway is Math.Round away from zero`` (FsCheck.NormalFloat x) =
    Num.roundAway x = Math.Round(x, MidpointRounding.AwayFromZero)

[<Fact>]
let ``roundAway rounds a half away from zero and nothing else`` () =
    Assert.Equal<float[]>(
        [| 1.0; -1.0; 3.0; -3.0; 0.0; 1.0 |],
        [| 0.5; -0.5; 2.5; -2.5; 0.49999999999999994; 0.5000000000000001 |] |> Array.map Num.roundAway
    )

[<Fact>]
let ``at writes what Math.Round and a custom format wrote, halves included`` () =
    let rng = Random 1

    for v in sweep 200_000 do
        let d = rng.Next 7

        if abs v * 10.0 ** float d < 1e15 then
            Assert.True((Num.at d v = atBefore d v), sprintf "at %d %.17g: %s, before %s" d v (Num.at d v) (atBefore d v))

[<Property>]
let ``at agrees with the platform at every precision`` (FsCheck.NormalFloat v) (d: byte) =
    let d = int d % 7
    abs v * 10.0 ** float d >= 1e15 || Num.at d v = atBefore d v

[<Fact>]
let ``at on the cases that differ between the runtimes`` () =
    Assert.Equal("0.13", Num.at 2 0.125)
    Assert.Equal("-0.13", Num.at 2 -0.125)
    Assert.Equal("2.68", Num.at 2 2.675)
    Assert.Equal("1.001", Num.at 3 1.0005)
    Assert.Equal("0", Num.at 3 -0.0001)
    Assert.Equal("0", Num.at 2 nan)

/// Past 10¹⁵ at the requested places the scaled integer is no longer exact, and `at` writes the
/// rounded value exactly where the custom format stopped at fifteen significant digits
/// (`4339842218598.28`). Nothing Marey draws is that large; this pins what it does.
[<Fact>]
let ``at past ten to the fifteenth writes the rounded value exactly`` () =
    Assert.Equal("4339842218598.27783", Num.at 5 4339842218598.2778)
    Assert.Equal("100000000000000000000", Num.at 3 1e20)

[<Fact>]
let ``fixedPoint writes what "F" writes, halves included`` () =
    let rng = Random 2

    for v in sweep 200_000 do
        let n = rng.Next 16
        let expected = v.ToString("F" + string n, invariant)
        Assert.True((Num.fixedPoint n v = expected), sprintf "F%d %.17g: %s, expected %s" n v (Num.fixedPoint n v) expected)

[<Property>]
let ``fixedPoint agrees with "F" on any finite double`` (FsCheck.NormalFloat v) (n: byte) =
    let n = int n % 16
    Num.fixedPoint n v = v.ToString("F" + string n, invariant)

/// The exact value decides, and an exact half goes to the even digit: `0.125` is a half,
/// `2.675` is below one and `0.005` above.
[<Fact>]
let ``fixedPoint rounds the binary value, a half to even`` () =
    Assert.Equal("0.12", Num.fixedPoint 2 0.125)
    Assert.Equal("0.88", Num.fixedPoint 2 0.875)
    Assert.Equal("2", Num.fixedPoint 0 2.5)
    Assert.Equal("2.67", Num.fixedPoint 2 2.675)
    Assert.Equal("-0.01", Num.fixedPoint 2 -0.005)
    Assert.Equal("-0.00", Num.fixedPoint 2 -0.001)
    Assert.Equal("100000000000000000000.00", Num.fixedPoint 2 1e20)
    Assert.Equal("-0", Num.fixedPoint 0 -0.0)

[<Property>]
let ``roundEven is Math.Round`` (FsCheck.NormalFloat x) = Num.roundEven x = Math.Round x

[<Fact>]
let ``roundEven breaks an exact half to even, and nothing else`` () =
    Assert.Equal<float[]>(
        [| 0.0; 2.0; 2.0; -2.0; 3.0; 3.0; 4.0; 0.0 |],
        [| 0.5; 1.5; 2.5; -2.5; 2.500000001; 3.4999999999; 3.5; -0.49999999999999994 |] |> Array.map Num.roundEven
    )

[<Fact>]
let ``roundEven agrees with Math.Round on a sweep of halves and near-halves`` () =
    for v in sweep 200_000 do
        Assert.True((Num.roundEven v = Math.Round v), sprintf "%.17g: %g, Math.Round %g" v (Num.roundEven v) (Math.Round v))
