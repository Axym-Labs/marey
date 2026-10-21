namespace Marey

/// The seven SI base measures.
///
/// These are *physical* dimensions, which is why they live here and not in Marey.Core: Core's
/// `px`/`nd`/`pt`/`mm`/`rad`/`deg` are graphics tags with no SI dimension and no conversion to
/// SI at all (Space.fs says so explicitly). Keeping the two sets apart is what stops anyone
/// building a `Scale<px>` with a dimension vector and pretending pixels are metres.
///
/// **Note the `mm` collision.** Marey.Core already owns `mm` — *millimetres of physical
/// output*, a typography measure. It is not 10⁻³ m and it must not be treated as one. Prefixed
/// SI lengths in this library are never new measures; they are a runtime factor on `U<m>`
/// (see `Prefix` and `U.scaled`). `float<m>` is metres, always, and the prefix is a
/// presentation decision made at the axis. That is the whole point of §2.2.
///
/// **These seven are abbreviations of `FSharp.Data.UnitSystems.SI.UnitNames`, not fresh
/// primitives.** F# measures are nominal, so declared fresh they would not unify with
/// FSharp.Core's SI, and every value arriving from a library that speaks it — FSharp.Data's CSV
/// type provider reads units straight out of column headers — would have to cross the boundary
/// through `LanguagePrimitives.FloatWithMeasure`, an unchecked assertion that relabels metres as
/// seconds as happily as anything else. Aliasing removes the cast. The derived units' algebra is
/// unaffected, and so are Core's graphics tags: FSharp.Core declares no prefixed and no non-SI
/// units, so the `mm` hazard above is untouched. Binary-breaking, and taken at rc1. See D42.
[<Measure>]
type m = Microsoft.FSharp.Data.UnitSystems.SI.UnitNames.metre

/// Kilogram. **The one SI base unit whose name already carries a prefix**, which makes it a
/// genuine special case for `Prefix` — see the mass branch of `Prefix.selectFor` and
/// `UnitDisplay.renderPrefixed`, where a chosen prefix of 10⁻⁶ on `float<kg>` has to print
/// `mg`, not `µkg`.
[<Measure>]
type kg = Microsoft.FSharp.Data.UnitSystems.SI.UnitNames.kilogram

/// Second.
[<Measure>]
type s = Microsoft.FSharp.Data.UnitSystems.SI.UnitNames.second

/// Ampere.
[<Measure>]
type A = Microsoft.FSharp.Data.UnitSystems.SI.UnitNames.ampere

/// Kelvin. The *absolute* temperature measure. °C and °F are separate, affine measures in
/// `NonSi.fs`, and deliberately do not unify with this one.
[<Measure>]
type K = Microsoft.FSharp.Data.UnitSystems.SI.UnitNames.kelvin

/// Mole.
[<Measure>]
type mol = Microsoft.FSharp.Data.UnitSystems.SI.UnitNames.mole

/// Candela.
[<Measure>]
type cd = Microsoft.FSharp.Data.UnitSystems.SI.UnitNames.candela

/// SI base-unit witnesses, inside a module F# opens with the namespace.
///
/// The module `U` below merges with the algebra module `U` in Marey.Core: F# name resolution
/// keeps modules in a multi-map, so `U.mul` (Core) and `U.metre` (here) are both reachable
/// through the single path `U`, exactly as §2.1 writes them. Two modules of one name inside one
/// assembly is FS0037, which is why the derived and the non-SI witnesses are each a `U` inside a
/// module of their own (`DerivedWitnesses`, `NonSiWitnesses`), opened with the namespace like
/// this one.
///
/// **Why it is nested.** At namespace level this module compiled to a public type
/// `Marey.U`, and so does the witness class in Marey.Core: C# saw two `Marey.U` and refused
/// every call that named either (CS0433), so no C# caller could pass a witness at all. F# reads
/// an `[<AutoOpen>]` module as opened with its namespace, so `open Marey` still finds `U.metre`
/// and merges it with `U.mul`; the CLR name becomes `Marey.SiWitnesses+U`, which collides with
/// nothing. `[<CompiledName>]` would have been the smaller change, and on a module it is ignored
/// without a word. The one spelling lost is the fully qualified `Marey.U.metre`, without the
/// `open`. C# reaches every witness through `Units` instead.
[<AutoOpen>]
module SiWitnesses =

    [<RequireQualifiedAccess>]
    module U =

        /// Local `Dim` constructor. Spelling the seven exponents out at each witness is verbose
        /// but it is also the only place in the library where the dimension of a unit is *asserted*
        /// rather than derived, so it deserves to be explicit and greppable.
        let internal dimOf l ma t i th n j : Dim =
            { L = l
              M = ma
              T = t
              I = i
              Th = th
              N = n
              J = j }

        // ---- The seven base units --------------------------------------------------------

        let metre: U<m> = U<m>("m", Dim.length, 1.0)
        let kilogram: U<kg> = U<kg>("kg", Dim.mass, 1.0)
        let second: U<s> = U<s>("s", Dim.time, 1.0)
        let ampere: U<A> = U<A>("A", Dim.current, 1.0)
        let kelvin: U<K> = U<K>("K", Dim.temperature, 1.0)
        let mole: U<mol> = U<mol>("mol", Dim.amount, 1.0)
        let candela: U<cd> = U<cd>("cd", Dim.luminosity, 1.0)

        // ---- Coherent combinations that come up constantly --------------------------------
        //
        // Each of these is exactly what `U.mul`/`U.div` would produce, and the `Symbol` is
        // deliberately the same naive composition those functions build. It is a debugging aid
        // and nothing else: display goes through `Dim`.

        let squareMetre: U<m^2> = U<m^2>("m²", dimOf 2 0 0 0 0 0 0, 1.0)
        let cubicMetre: U<m^3> = U<m^3>("m³", dimOf 3 0 0 0 0 0 0, 1.0)
        let metrePerSecond: U<m / s> = U<m / s>("m/s", dimOf 1 0 -1 0 0 0 0, 1.0)
        let metrePerSecondSquared: U<m / s^2> = U<m / s^2>("m/s²", dimOf 1 0 -2 0 0 0 0, 1.0)
        let kilogramPerCubicMetre: U<kg / m^3> = U<kg / m^3>("kg/m³", dimOf -3 1 0 0 0 0 0, 1.0)
        let perSecond: U<1 / s> = U<1 / s>("1/s", dimOf 0 0 -1 0 0 0 0, 1.0)

        /// The gram, as a *scaled* kilogram. Note the measure is still `kg`: grams and kilograms
        /// are the same physical quantity and F# measures cannot express the 10⁻³ between them —
        /// that difference is runtime metadata, which is exactly what `U.scaled` is for.
        let gram: U<kg> = U<kg>("g", Dim.mass, 1e-3)
