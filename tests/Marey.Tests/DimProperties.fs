/// "`Dim` group laws".
///
/// `Dim` under `add`/`neg` is the free abelian group on seven generators (Dim.fs says so in as
/// many words). These laws are cheap and they catch the sign error in `U.div` or `U.recip` that
/// would otherwise surface three layers up as a wrong axis label — which is exactly the failure
/// mode §12.6 describes and the one nobody notices in a screenshot.
module Marey.Tests.DimProperties

open FsCheck.Xunit
open Xunit
open Marey
open Marey.Tests.Generators

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module Laws =

    [<Property>]
    let ``add is associative`` (SmallDim a) (SmallDim b) (SmallDim c) =
        Dim.add (Dim.add a b) c = Dim.add a (Dim.add b c)

    [<Property>]
    let ``add is commutative`` (SmallDim a) (SmallDim b) = Dim.add a b = Dim.add b a

    [<Property>]
    let ``zero is a two-sided identity`` (SmallDim a) =
        Dim.add a Dim.zero = a && Dim.add Dim.zero a = a

    [<Property>]
    let ``neg is a two-sided inverse`` (SmallDim a) =
        Dim.add a (Dim.neg a) = Dim.zero
        && Dim.add (Dim.neg a) a = Dim.zero

    [<Property>]
    let ``neg is an involution`` (SmallDim a) = Dim.neg (Dim.neg a) = a

    [<Property>]
    let ``sub a b is add a (neg b)`` (SmallDim a) (SmallDim b) =
        Dim.sub a b = Dim.add a (Dim.neg b)

    [<Property>]
    let ``sub is the inverse of add`` (SmallDim a) (SmallDim b) =
        Dim.sub (Dim.add a b) b = a

    /// The group action of Z: `pow n` is n-fold `add`, including for `n = 0` and `n < 0`.
    [<Property>]
    let ``pow n is n-fold add`` (SmallDim a) =
        [ -5 .. 5 ]
        |> List.forall (fun n ->
            let folded =
                if n >= 0 then
                    List.fold (fun acc _ -> Dim.add acc a) Dim.zero [ 1..n ]
                else
                    List.fold (fun acc _ -> Dim.sub acc a) Dim.zero [ 1 .. -n ]

            Dim.pow n a = folded)

    [<Property>]
    let ``pow distributes over add in the exponent`` (SmallDim a) =
        [ -3 .. 3 ]
        |> List.forall (fun i ->
            [ -3 .. 3 ]
            |> List.forall (fun j -> Dim.pow (i + j) a = Dim.add (Dim.pow i a) (Dim.pow j a)))

    [<Property>]
    let ``pow 0 is zero and pow 1 is the identity`` (SmallDim a) =
        Dim.pow 0 a = Dim.zero && Dim.pow 1 a = a

    [<Property>]
    let ``pow -1 is neg`` (SmallDim a) = Dim.pow -1 a = Dim.neg a

    /// The hand-written `Equals`/`GetHashCode` on `Dim` (Dim.fs replaces the generated ones with
    /// a seven-int fast path) must agree with the structural reading. A hash that disagrees with
    /// equality silently breaks `UnitRegistry`, whose whole lookup is a `Dictionary<Dim,_>`.
    [<Property>]
    let ``equal Dims hash equally`` (SmallDim a) =
        let copy =
            { L = a.L
              M = a.M
              T = a.T
              I = a.I
              Th = a.Th
              N = a.N
              J = a.J }

        a = copy
        && a.GetHashCode() = copy.GetHashCode()
        && (a :> System.IEquatable<Dim>).Equals copy

    [<Property>]
    let ``Dims differing in one exponent are unequal`` (SmallDim a) =
        [ (fun (d: Dim) -> { d with L = d.L + 1 })
          (fun d -> { d with M = d.M + 1 })
          (fun d -> { d with T = d.T + 1 })
          (fun d -> { d with I = d.I + 1 })
          (fun d -> { d with Th = d.Th + 1 })
          (fun d -> { d with N = d.N + 1 })
          (fun d -> { d with J = d.J + 1 }) ]
        |> List.forall (fun bump -> bump a <> a)

    [<Property>]
    let ``isDimensionless holds exactly at zero`` (SmallDim a) =
        Dim.isDimensionless a = (a = Dim.zero)

    /// `Dim.exponents` is the input to every unit renderer, so its contract — no zero exponents,
    /// canonical SI order, nothing lost — is a property of `Dim`, not of the renderer.
    [<Property>]
    let ``exponents drops zeros and preserves basis order`` (SmallDim a) =
        let xs = Dim.exponents a
        let order = Dim.basis |> List.map fst
        let names = xs |> List.map fst

        xs |> List.forall (fun (_, e) -> e <> 0)
        && names = (order |> List.filter (fun n -> List.contains n names))

    [<Property>]
    let ``exponents round-trips through the basis accessors`` (SmallDim a) =
        Dim.basis
        |> List.forall (fun (sym, get) ->
            let fromExponents =
                Dim.exponents a
                |> List.tryFind (fun (s, _) -> s = sym)
                |> Option.map snd
                |> Option.defaultValue 0

            fromExponents = get a)

[<Properties(Arbitrary = [| typeof<MareyArbs> |], MaxTest = 500, Replay = Seed, QuietOnSuccess = true)>]
module BaseDimensions =

    /// The seven generators are independent: no product of the others equals any one of them.
    /// This is the "free" in "free abelian group", and it is what makes `Dim` equality a sound
    /// test of unit identity.
    [<Fact>]
    let ``the seven base dimensions are distinct and non-zero`` () =
        let bases =
            [ Dim.length
              Dim.mass
              Dim.time
              Dim.current
              Dim.temperature
              Dim.amount
              Dim.luminosity ]

        Assert.All(bases, fun d -> Assert.NotEqual(Dim.zero, d))
        Assert.Equal(7, (List.distinct bases).Length)

    [<Fact>]
    let ``show renders a pure number as 1`` () = Assert.Equal("1", Dim.show Dim.zero)

    /// §12.6's running example at the `Dim` level: force times length is energy, and the
    /// dimension arithmetic says so even though the composed symbol does not.
    [<Fact>]
    let ``force times length is energy`` () =
        let force =
            Dim.add Dim.mass (Dim.sub Dim.length (Dim.pow 2 Dim.time))

        let energy = Dim.add force Dim.length

        Assert.Equal(
            { L = 2
              M = 1
              T = -2
              I = 0
              Th = 0
              N = 0
              J = 0 },
            energy
        )
