/// §2.1 / §12.1 — the witness algebra.
///
/// §12.1 is the finding that measure algebra in an *operator overload* silently over-constrains
/// the second measure variable (FS0064), and that the fix is module-level functions with
/// explicit `[<Measure>]` type parameters, "verified generic across five distinct
/// instantiations". Two halves are checked here:
///
/// * the **runtime** half — `U.mul`/`U.div`/`recip`/`square`/`cube` must move `Dim` and `ToSI`
///   the way the group laws say, over many random combinations;
/// * the **compile-time** half — the five instantiations still infer. Those are bindings with
///   explicit type annotations rather than assertions: if the algebra stops being generic, this
///   file stops compiling, which is a better failure than a red test.
module Quiver.Tests.WitnessProperties

open FsCheck.Xunit
open Xunit
open Quiver
open Quiver.Tests.Generators

/// Build a dimensionless-measure witness carrying an arbitrary `Dim`. The *measure* is erased
/// at runtime anyway (§1.3), so this is the honest way to quantify over dimensions: the runtime
/// algebra sees exactly what it would see for any other measure.
let private witness (d: Dim) (toSI: float) = U<1>("u", d, toSI)

[<Properties(Arbitrary = [| typeof<QuiverArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Algebra =

    [<Property>]
    let ``mul adds dimensions`` (SmallDim a) (SmallDim b) =
        (U.mul (witness a 1.0) (witness b 1.0)).Dim = Dim.add a b

    [<Property>]
    let ``div subtracts dimensions`` (SmallDim a) (SmallDim b) =
        (U.div (witness a 1.0) (witness b 1.0)).Dim = Dim.sub a b

    [<Property>]
    let ``recip negates the dimension`` (SmallDim a) =
        (U.recip (witness a 1.0)).Dim = Dim.neg a

    [<Property>]
    let ``square and cube are pow 2 and pow 3`` (SmallDim a) =
        let u = witness a 1.0
        (U.square u).Dim = Dim.pow 2 a && (U.cube u).Dim = Dim.pow 3 a

    [<Property>]
    let ``mul then div by the same unit restores the dimension`` (SmallDim a) (SmallDim b) =
        let u = witness a 1.0
        let v = witness b 1.0
        (U.div (U.mul u v) v).Dim = a

    [<Property>]
    let ``mul is commutative and associative on dimensions`` (SmallDim a) (SmallDim b) (SmallDim c) =
        let ua, ub, uc = witness a 1.0, witness b 1.0, witness c 1.0

        (U.mul ua ub).Dim = (U.mul ub ua).Dim
        && (U.mul (U.mul ua ub) uc).Dim = (U.mul ua (U.mul ub uc)).Dim

    /// The conversion factors multiply and divide alongside the dimensions. A witness whose
    /// `Dim` is right and whose `ToSI` is wrong is the eV-labelled-as-J bug (D16) waiting to
    /// happen, so it is worth its own law.
    [<Property>]
    let ``mul and div compose the SI factors`` (SmallDim a) (SmallDim b) (PositiveFloat ka) (PositiveFloat kb) =
        let u = witness a ka
        let v = witness b kb

        Approx.rel Tol.arith (U.mul u v).ToSI (ka * kb)
        && Approx.rel Tol.arith (U.div u v).ToSI (ka / kb)
        && Approx.rel Tol.arith (U.recip u).ToSI (1.0 / ka)
        && Approx.rel Tol.arith (U.square u).ToSI (ka * ka)

    /// `U.mul`/`U.div` hard-code a zero offset, and NonSi.fs argues that is correct rather than
    /// lazy: a rate of change of an affine quantity has no offset. Asserted so that "correct
    /// rather than lazy" stays a decision instead of drifting.
    [<Property>]
    let ``products and quotients are never affine`` (SmallDim a) (SmallDim b) =
        let celsius = U<1>("degC", a, 1.0, 273.15)
        let other = witness b 2.0

        not (U.mul celsius other).IsAffine
        && not (U.div celsius other).IsAffine
        && not (U.recip celsius).IsAffine

    [<Property>]
    let ``scaled keeps the dimension and the offset, and scales the factor``
        (SmallDim a)
        (PositiveFloat k)
        (PositiveFloat f)
        =
        let u = U<1>("u", a, k, 7.5)
        let s = U.scaled "u'" f u

        s.Dim = a
        && s.OffsetSI = 7.5
        && Approx.rel Tol.arith s.ToSI (k * f)
        && s.Symbol = "u'"

    /// `ofSI (toSI x) = x` for a multiplicative unit — every SI and non-SI unit except °C/°F.
    [<Property>]
    let ``toSI and ofSI round-trip for a multiplicative unit`` (SmallDim a) (PositiveFloat k) (FiniteFloat x) =
        let u = U<1>("u", a, k)
        Approx.scaled 1e-12 (abs x) (float (U.ofSI u (U.toSI u x))) x

    /// The same law for an **affine** unit, which is the case that makes °C and °F safe to carry
    /// as data rather than as a special case in every renderer.
    ///
    /// The offset is generated as a multiple of the value's own SI magnitude rather than
    /// independently, and that is a statement about affine units rather than a convenience:
    /// a real one has an offset of the same order as its values — 273.15 K against temperatures
    /// of a few hundred kelvin. Let the two float free and `(x*k + offset) - offset` becomes a
    /// total cancellation: with `k = 3.9e-6`, `offset = 261498` and `x = 0.094`, the SI value is
    /// `261498.0000003654`, whose ulp is 2.9e-11, and the round trip comes back with a relative
    /// error of 2.9e-5. That is the conditioning of the affine map, not a defect in `U`.
    [<Property>]
    let ``toSI and ofSI round-trip for an affine unit`` (SmallDim a) (PositiveFloat k) (FiniteFloat ratio) (FiniteFloat x) =
        // |offset| <= 1000 * |x * k|, which bounds the cancellation at three decimal digits.
        let offset = (ratio % 1000.0) * x * k
        let u = U<1>("u", a, k, offset)
        Approx.scaled 1e-11 (abs x) (float (U.ofSI u (U.toSI u x))) x

    /// The two affine units the library actually ships, at values a reader would recognise.
    [<Fact>]
    let ``the real affine units round-trip exactly`` () =
        Assert.Equal(37.0, float (U.ofSI NonSi.degreeCelsius (U.toSI NonSi.degreeCelsius 37.0<degC>)), 12)
        Assert.Equal(-40.0, float (U.ofSI NonSi.degreeCelsius (U.toSI NonSi.degreeCelsius -40.0<degC>)), 12)
        Assert.Equal(98.6, float (U.ofSI NonSi.degreeFahrenheit (U.toSI NonSi.degreeFahrenheit 98.6<degF>)), 12)
        Assert.Equal(-459.67, float (U.ofSI NonSi.degreeFahrenheit (U.toSI NonSi.degreeFahrenheit -459.67<degF>)), 9)

[<Properties(Arbitrary = [| typeof<QuiverArbs> |], MaxTest = 300, Replay = Seed, QuietOnSuccess = true)>]
module Conversion =

    /// `U.convert` is "the one place in the library where a unit error is a runtime error", so
    /// both halves of that sentence get a test: it round-trips within a dimension, and it
    /// raises across dimensions.
    [<Property>]
    let ``convert round-trips between two units of one dimension`` (SmallDim d) (PositiveFloat ka) (PositiveFloat kb) (FiniteFloat x) =
        let a = U<1>("a", d, ka)
        let b = U<1>("b", d, kb)
        Approx.rel 1e-8 (float (U.convert b a (U.convert a b x))) x

    [<Fact>]
    let ``convert raises across dimensions`` () =
        let ex =
            Assert.Throws<System.Exception>(fun () -> U.convert U.metre U.second 1.0<m> |> ignore)

        Assert.Contains("different physical dimensions", ex.Message)

    [<Fact>]
    let ``electronvolts convert to joules`` () =
        let j = U.convert NonSi.electronvolt Derived.joule 1.0<eV>
        Assert.True(Approx.rel 1e-12 (float j) 1.602176634e-19)

    /// The affine units are the ones a `ToSI`-only witness would get wrong, which is why
    /// NonSi.fs calls Fahrenheit "the better regression test of the two".
    [<Fact>]
    let ``celsius and fahrenheit convert through kelvin`` () =
        let k = U.convert NonSi.degreeCelsius U.kelvin 0.0<degC>
        Assert.True(Approx.rel 1e-12 (float k) 273.15)

        let f = U.convert NonSi.degreeFahrenheit U.kelvin 212.0<degF>
        Assert.True(Approx.abs' 1e-9 (float f) 373.15)

        let c = U.convert NonSi.degreeFahrenheit NonSi.degreeCelsius 98.6<degF>
        Assert.True(Approx.abs' 1e-9 (float c) 37.0)

/// §12.1 — "verified generic across five distinct instantiations". These are bindings, not
/// assertions: their *types* are the test. Making `U.mul` less generic breaks the build here.
///
/// D20 makes the same trick load-bearing one level up — `Derived.newtonByComposition` is a
/// permanent compile-time assertion inside the library itself — so this module extends the idea
/// rather than duplicating it.
module TypeLevel =

    let private velocity: U<m / s> = U.div U.metre U.second
    let private acceleration: U<m / s^2> = U.div velocity U.second
    let private force: U<kg * m / s^2> = U.mul U.kilogram acceleration
    let private momentum: U<kg * m / s> = U.mul U.kilogram velocity
    let private energy: U<kg * m^2 / s^2> = U.mul force U.metre

    /// D20: the derived measures are *abbreviations*, so the composed type unifies with the
    /// named one. If someone makes `N` opaque, this line stops compiling.
    let private forceIsNewton: U<N> = force

    let private energyIsJoule: U<J> = energy
    let private areaIsSquareMetre: U<m^2> = U.square U.metre
    let private volumeIsCubicMetre: U<m^3> = U.cube U.metre
    let private frequencyIsHertz: U<Hz> = U.recip U.second

    /// `U.dimOf` is internal to Quiver.Units, so the expected dimensions are spelled out here.
    /// Spelling them out is better anyway: these are the numbers a reader checks against SI.
    let private dim l ma t : Dim =
        { L = l
          M = ma
          T = t
          I = 0
          Th = 0
          N = 0
          J = 0 }

    [<Fact>]
    let ``the five instantiations carry the right dimensions`` () =
        Assert.Equal(dim 1 0 -1, velocity.Dim)
        Assert.Equal(dim 1 0 -2, acceleration.Dim)
        Assert.Equal(dim 1 1 -2, force.Dim)
        Assert.Equal(dim 1 1 -1, momentum.Dim)
        Assert.Equal(dim 2 1 -2, energy.Dim)

    [<Fact>]
    let ``composed witnesses agree with the declared derived witnesses`` () =
        Assert.Equal(Derived.newton.Dim, forceIsNewton.Dim)
        Assert.Equal(Derived.joule.Dim, energyIsJoule.Dim)
        Assert.Equal(Derived.hertz.Dim, frequencyIsHertz.Dim)
        Assert.Equal(U.squareMetre.Dim, areaIsSquareMetre.Dim)
        Assert.Equal(U.cubicMetre.Dim, volumeIsCubicMetre.Dim)

    /// D20 again, from the other side: `Hz` and `Bq` are the *same measure*, and so are `Gy` and
    /// `Sv`. Recorded as a test so the collision is a known cost rather than a surprise.
    [<Fact>]
    let ``hertz and becquerel share a dimension, as do gray and sievert`` () =
        Assert.Equal(Derived.hertz.Dim, Derived.becquerel.Dim)
        Assert.Equal(Derived.gray.Dim, Derived.sievert.Dim)
        Assert.Equal(Derived.lumen.Dim, U.candela.Dim)

    /// §8.1's claim: "the gradient of a potential is a force, with the unit inferred rather than
    /// annotated". The annotation on the left is the assertion; nothing on the right says `N`.
    [<Fact>]
    let ``a potential gradient is a force, inferred`` () =
        let potential = 12.0<J>
        let distance = 3.0<m>
        let gradient: float<N> = potential / distance
        Assert.Equal(4.0, float gradient, 12)
