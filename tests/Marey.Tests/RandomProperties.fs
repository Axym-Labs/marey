/// The library's one generator, and gate 12 of `m4-plan.md`.
///
/// The reference vector is the point of the file. SplitMix64's outputs for seed 0 are published
/// alongside Vigna's reference implementation, and they are restated here as literals rather than
/// computed, so a change to any constant in `Random.fs` fails this test instead of silently
/// changing every LIC texture and every bootstrap interval the library has drawn.
module Marey.Tests.RandomProperties

open FsCheck.Xunit
open Xunit
open Marey

let private draws (n: int) (seed: uint64) =
    let mutable r = Rng.seeded seed

    [| for _ in 1 .. n do
           let struct (v, r') = Rng.next r
           r <- r'
           yield v |]

[<Fact>]
let ``seed 0 gives SplitMix64's published sequence`` () =
    Assert.Equal<uint64[]>(
        [| 0xE220A8397B1DCDAFUL
           0x6E789E6AA1B965F4UL
           0x06C45D188009454FUL
           0xF88BB8A8724C81ECUL
           0x1B39896A51A8749BUL |],
        draws 5 0UL
    )

/// A generator is a value: drawing from it does not change it, so two draws from one state agree.
[<Property>]
let ``a state draws the same number every time`` (seed: uint64) =
    let r = Rng.seeded seed
    let struct (a, _) = Rng.next r
    let struct (b, _) = Rng.next r
    a = b

[<Property>]
let ``a unit draw is in [0, 1)`` (seed: uint64) =
    let us, _ = Rng.units 64 (Rng.seeded seed)
    us |> Array.forall (fun u -> u >= 0.0 && u < 1.0)

[<Property>]
let ``a draw below n is in [0, n)`` (seed: uint64) (n: FsCheck.PositiveInt) =
    let struct (k, _) = Rng.below n.Get (Rng.seeded seed)
    k >= 0 && k < n.Get

/// Uniform enough to resample with: ten bins of 100 000 draws, each within 2% of its share. The
/// standard deviation of one bin's count is about 95, so 2% (200) is a two-sigma band that a
/// broken generator — a stuck bit, a missing finaliser — misses by orders of magnitude.
[<Fact>]
let ``draws below ten fill ten bins evenly`` () =
    let counts = Array.zeroCreate 10
    let mutable r = Rng.seeded 20261005UL

    for _ in 1 .. 100_000 do
        let struct (k, r') = Rng.below 10 r
        counts.[k] <- counts.[k] + 1
        r <- r'

    for c in counts do
        Assert.InRange(c, 9_800, 10_200)

[<Fact>]
let ``a draw below zero is refused`` () =
    Assert.Throws<System.ArgumentException>(fun () -> Rng.below 0 (Rng.seeded 1UL) |> ignore)
    |> ignore
