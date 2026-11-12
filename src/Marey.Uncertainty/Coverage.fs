namespace Marey

open System

/// Coverage factors: the quantiles of the normal and of Student's t distribution, which turn a
/// standard uncertainty into an interval with a stated coverage probability (GUM annex G).
[<RequireQualifiedAccess>]
module Coverage =

    // Lanczos, g = 7, nine coefficients: ln Γ(x) to about 1e-15 for x > 0.
    let private lanczos =
        [| 0.99999999999980993
           676.5203681218851
           -1259.1392167224028
           771.32342877765313
           -176.61502916214059
           12.507343278686905
           -0.13857109526572012
           9.9843695780195716e-6
           1.5056327351493116e-7 |]

    let internal logGamma (x: float) =
        let x = x - 1.0
        let mutable a = lanczos.[0]

        for i in 1..8 do
            a <- a + lanczos.[i] / (x + float i)

        let t = x + 7.5
        0.5 * Math.Log(2.0 * Math.PI) + (x + 0.5) * Math.Log t - t + Math.Log a

    /// The continued fraction of the incomplete beta function, by the modified Lentz method.
    let private betaFraction (a: float) (b: float) (x: float) =
        let tiny = 1e-300
        let clamp v = if abs v < tiny then tiny else v
        let mutable c = 1.0
        let mutable d = 1.0 / clamp (1.0 - (a + b) * x / (a + 1.0))
        let mutable h = d
        let mutable m = 1
        let mutable go = true

        while go && m <= 10000 do
            let fm = float m
            let even = fm * (b - fm) * x / ((a + 2.0 * fm - 1.0) * (a + 2.0 * fm))
            d <- 1.0 / clamp (1.0 + even * d)
            c <- clamp (1.0 + even / c)
            h <- h * d * c
            let odd = -(a + fm) * (a + b + fm) * x / ((a + 2.0 * fm) * (a + 2.0 * fm + 1.0))
            d <- 1.0 / clamp (1.0 + odd * d)
            c <- clamp (1.0 + odd / c)
            let step = d * c
            h <- h * step
            go <- abs (step - 1.0) > 1e-15
            m <- m + 1

        h

    /// The regularized incomplete beta function I_x(a, b).
    let private incompleteBeta (a: float) (b: float) (x: float) =
        if x <= 0.0 then
            0.0
        elif x >= 1.0 then
            1.0
        else
            let front = Math.Exp(logGamma (a + b) - logGamma a - logGamma b + a * Math.Log x + b * Math.Log(1.0 - x))

            if x < (a + 1.0) / (a + b + 2.0) then
                front * betaFraction a b x / a
            else
                1.0 - front * betaFraction b a (1.0 - x) / b

    /// The probability that a Student's t variable with `dof` degrees of freedom lies outside ±t.
    let private tails (dof: float) (t: float) = incompleteBeta (dof / 2.0) 0.5 (dof / (dof + t * t))

    let private checkProbability (p: float) =
        if not (p > 0.0 && p < 1.0) then
            invalidArg "p" (sprintf "Marey: a coverage probability lies strictly between 0 and 1, and this one is %g." p)

    /// The factor k for which a normal variable lies within ±k standard deviations with
    /// probability `p`: 1.960 for 0.95, 2.576 for 0.99.
    let normal (p: float) : float =
        checkProbability p
        // The lower quantile at (1 + p)/2, by Acklam's rational approximation (relative error
        // below 1.2e-9), which is far inside what a coverage factor is quoted to.
        let q = (1.0 + p) / 2.0

        let a = [| -3.969683028665376e+01; 2.209460984245205e+02; -2.759285104469687e+02; 1.383577518672690e+02; -3.066479806614716e+01; 2.506628277459239e+00 |]
        let b = [| -5.447609879822406e+01; 1.615858368580409e+02; -1.556989798598866e+02; 6.680131188771972e+01; -1.328068155288572e+01 |]
        let c = [| -7.784894002430293e-03; -3.223964580411365e-01; -2.400758277161838e+00; -2.549732539343734e+00; 4.374664141464968e+00; 2.938163982698783e+00 |]
        let d = [| 7.784695709041462e-03; 3.224671290700398e-01; 2.445134137142996e+00; 3.754408661907416e+00 |]

        // q is in [0.5, 1), so only the central and the upper branch occur.
        if q <= 1.0 - 0.02425 then
            let u = q - 0.5
            let r = u * u

            (((((a.[0] * r + a.[1]) * r + a.[2]) * r + a.[3]) * r + a.[4]) * r + a.[5]) * u
            / (((((b.[0] * r + b.[1]) * r + b.[2]) * r + b.[3]) * r + b.[4]) * r + 1.0)
        else
            let u = sqrt (-2.0 * Math.Log(1.0 - q))

            -(((((c.[0] * u + c.[1]) * u + c.[2]) * u + c.[3]) * u + c.[4]) * u + c.[5])
            / ((((d.[0] * u + d.[1]) * u + d.[2]) * u + d.[3]) * u + 1.0)

    /// The factor t_p(ν) for which a Student's t variable with `dof` degrees of freedom lies within
    /// ±t with probability `p`: 2.92 for 0.99 and 16 degrees of freedom (GUM table G.2). Infinite
    /// degrees of freedom give the normal factor.
    let studentT (p: float) (dof: float) : float =
        checkProbability p

        if Double.IsNaN dof || dof <= 0.0 then
            invalidArg "dof" (sprintf "Marey: degrees of freedom are positive, and these are %g." dof)

        if Double.IsPositiveInfinity dof then
            normal p
        else
            let target = 1.0 - p
            let mutable lo = 0.0
            let mutable hi = 1.0

            while tails dof hi > target do
                lo <- hi
                hi <- hi * 2.0

            // Bisection: the tail probability falls monotonically in t, and 80 halvings of an
            // interval this size reach the last bit of a double.
            for _ in 1..80 do
                let mid = 0.5 * (lo + hi)
                if tails dof mid > target then lo <- mid else hi <- mid

            0.5 * (lo + hi)
