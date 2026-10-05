namespace Marey

// ======================================================================================
// D95 — A SPECIFIED PSEUDO-RANDOM GENERATOR.
//
// The library had no randomness until M4, and it gets exactly one generator, for two consumers:
// the noise texture a line-integral convolution smears along a field, and the resampling behind
// a bootstrap confidence interval. Both have to be reproducible from a seed the caller can
// print, because a LIC that differs between two renders cannot have a golden baseline and a
// confidence interval that differs between two runs of one script cannot be cited.
//
// **Specified rather than borrowed.** `System.Random` would be the obvious choice and is the
// wrong one: its algorithm is an implementation detail of the runtime, the seeded and unseeded
// constructors already use different algorithms, and Fable implements it differently again. A
// seed is only a reproducible result if the sequence it names is written down. SplitMix64 is
// Steele, Lea and Flood's generator ("Fast splittable pseudorandom number generators", 2014),
// in the exact form Vigna publishes as the seeder for xoshiro: a Weyl sequence through a
// 64-bit finaliser. A dozen lines, no tables, and statistically sound for the uses above.
//
// The test suite pins it against the published outputs for seed 0, so an "improvement" to
// these constants fails a test rather than silently changing every LIC in every document.
//
// Immutable, like everything else in Core: a generator is a value, and drawing from it returns
// the next one. Two callers holding the same `Rng` draw the same numbers, which is the point.
// ======================================================================================

/// A SplitMix64 generator state. Draw from it with the `Rng` module; every draw returns the
/// value and the state to draw the next one from.
[<Struct>]
type Rng = { State: uint64 }

[<RequireQualifiedAccess>]
module Rng =

    /// The generator a seed names. Every seed is valid, zero included.
    let seeded (seed: uint64) : Rng = { State = seed }

    /// The next 64 random bits, and the state after them.
    let next (r: Rng) : struct (uint64 * Rng) =
        let state = r.State + 0x9E3779B97F4A7C15UL
        let mutable z = state
        z <- (z ^^^ (z >>> 30)) * 0xBF58476D1CE4E5B9UL
        z <- (z ^^^ (z >>> 27)) * 0x94D049BB133111EBUL
        struct (z ^^^ (z >>> 31), { State = state })

    /// A float uniform on [0, 1): the top 53 bits, so every value is exactly representable and
    /// the spacing is uniform.
    let unit (r: Rng) : struct (float * Rng) =
        let struct (bits, r') = next r
        struct (float (bits >>> 11) * (1.0 / 9007199254740992.0), r')

    /// An integer uniform on [0, n). `n` must be positive.
    ///
    /// By scaling a unit draw, so the bias is at most n / 2⁵³ — far below anything a bootstrap
    /// resample could detect — and the result is the same under Fable, which has no 128-bit
    /// multiply for Lemire's unbiased method.
    let below (n: int) (r: Rng) : struct (int * Rng) =
        if n <= 0 then
            invalidArg "n" (sprintf "Marey: a draw below %d has nothing to choose from." n)

        let struct (u, r') = unit r
        struct (min (n - 1) (int (u * float n)), r')

    /// `count` unit draws, and the state after the last.
    let units (count: int) (r: Rng) : float[] * Rng =
        let out = Array.zeroCreate (max 0 count)
        let mutable s = r

        for i in 0 .. out.Length - 1 do
            let struct (u, s') = unit s
            out.[i] <- u
            s <- s'

        out, s
