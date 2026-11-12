namespace Marey

open System

/// Pseudo-random draws that are the same on every platform: a seeded run under .NET and under
/// JavaScript gives the same numbers, so a Monte Carlo check is reproducible wherever it runs.
module internal Sampling =

    /// a · b modulo 2³², from 16-bit halves, so that no intermediate needs more than the 53 bits
    /// a JavaScript number holds exactly.
    let mul32 (a: uint32) (b: uint32) : uint32 =
        let alo = a &&& 0xFFFFu
        let ahi = a >>> 16
        let blo = b &&& 0xFFFFu
        let bhi = b >>> 16
        let cross = (ahi * blo + alo * bhi) &&& 0xFFFFu
        ((cross <<< 16) + alo * blo) &&& 0xFFFFFFFFu

    let private rotl (x: uint32) (k: int) : uint32 = (x <<< k) ||| (x >>> (32 - k))

    /// xoshiro128** (Blackman and Vigna), seeded by splitmix32.
    [<Sealed>]
    type Generator(seed: int) =
        let mutable z = uint32 seed

        let splitmix () =
            z <- (z + 0x9E3779B9u) &&& 0xFFFFFFFFu
            let mutable x = z
            x <- mul32 (x ^^^ (x >>> 16)) 0x85EBCA6Bu
            x <- mul32 (x ^^^ (x >>> 13)) 0xC2B2AE35u
            x ^^^ (x >>> 16)

        let mutable s0 = splitmix ()
        let mutable s1 = splitmix ()
        let mutable s2 = splitmix ()
        let mutable s3 = splitmix ()
        let mutable spare = nan

        /// The next 32 random bits.
        member _.Next() : uint32 =
            let result = mul32 (rotl (mul32 s1 5u) 7) 9u
            let t = (s1 <<< 9) &&& 0xFFFFFFFFu
            s2 <- s2 ^^^ s0
            s3 <- s3 ^^^ s1
            s1 <- s1 ^^^ s2
            s0 <- s0 ^^^ s3
            s2 <- s2 ^^^ t
            s3 <- rotl s3 11
            result

        /// Uniform in (0, 1), never either end, from 53 random bits.
        member g.Uniform() : float =
            let a = float (g.Next() >>> 5)
            let b = float (g.Next() >>> 6)
            (a * 67108864.0 + b + 0.5) / 9007199254740992.0

        /// Standard normal, by Box and Muller, two at a time.
        member g.Normal() : float =
            if not (Double.IsNaN spare) then
                let z = spare
                spare <- nan
                z
            else
                let r = sqrt (-2.0 * Math.Log(g.Uniform()))
                let angle = 2.0 * Math.PI * g.Uniform()
                spare <- r * Math.Sin angle
                r * Math.Cos angle

        /// Gamma with this shape and scale 1, by Marsaglia and Tsang.
        member g.Gamma(shape: float) : float =
            if shape < 1.0 then
                g.Gamma(shape + 1.0) * Math.Pow(g.Uniform(), 1.0 / shape)
            else
                let d = shape - 1.0 / 3.0
                let c = 1.0 / sqrt (9.0 * d)
                let mutable result = nan

                while Double.IsNaN result do
                    let x = g.Normal()
                    let v = 1.0 + c * x

                    if v > 0.0 then
                        let v = v * v * v
                        let u = g.Uniform()

                        if Math.Log u < 0.5 * x * x + d - d * v + d * Math.Log v then
                            result <- d * v

                result

        /// χ²/ν for ν degrees of freedom: the factor a t variable divides a normal one by, squared.
        member g.ChiSquaredPerDof(dof: float) : float = 2.0 * g.Gamma(dof / 2.0) / dof

    /// A draw from the distribution, standardised so that it is the input's deviation divided by
    /// its standard uncertainty. `scale` is √(χ²/ν) for a t, drawn by the caller so that inputs
    /// resting on one estimate of a variance share it.
    let draw (g: Generator) (distribution: Distribution) (dof: float) (scale: float) : float =
        match distribution with
        | Distribution.Normal -> g.Normal()
        | Distribution.Rectangular -> sqrt 3.0 * (2.0 * g.Uniform() - 1.0)
        | Distribution.Triangular -> sqrt 6.0 * (g.Uniform() + g.Uniform() - 1.0)
        | Distribution.Arcsine -> sqrt 2.0 * Math.Sin(Math.PI * (g.Uniform() - 0.5))
        | Distribution.StudentT -> if Double.IsPositiveInfinity dof then g.Normal() else g.Normal() / scale
