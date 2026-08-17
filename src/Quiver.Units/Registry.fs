namespace Quiver

/// §2.2 — one unit's preferred symbol, in every typography the library can emit.
///
/// **The design sketches the registry as `Dim -> string`. One string is not enough**, and the
/// ohm proves it in four different ways: `Ω` (Unicode), `ohm` (ASCII), `\Omega` (LaTeX math),
/// `\ohm` (siunitx macro). Storing one spelling and deriving the others means string surgery
/// on symbols, which is precisely the composition §12.6 forbids. So the registry value is a
/// record of the four spellings, chosen once, by whoever registered the unit.
///
/// `LatexCore` is the content that goes *inside* `\mathrm{…}`, not the wrapped form, so a
/// prefix can be glued in front of it (`\mathrm{k\Omega}`) without re-parsing anything.
/// `Siunitx` is the bare macro (`\ohm`), because whether it ends up inside `\qty{}{…}` or
/// `\qty{1e-9}{…}` is the renderer's decision, not the registry's.
type UnitSymbol =
    { /// `Ω`, `µm`, `kg`
      Unicode: string
      /// `ohm`, `um`, `kg` — 7-bit safe, for CSV headers, log files and terminals.
      Ascii: string
      /// Content for `\mathrm{…}`: `\Omega`, `J`, `kg`.
      LatexCore: string
      /// Bare siunitx macro: `\ohm`, `\joule`, `\kilogram`.
      Siunitx: string }

[<RequireQualifiedAccess>]
module UnitSymbol =

    /// A symbol whose Unicode, ASCII and LaTeX spellings are all the same plain text — which
    /// is true of every SI symbol except the ohm and the micro prefix.
    let plain (text: string) (siunitx: string) =
        { Unicode = text
          Ascii = text
          LatexCore = text
          Siunitx = siunitx }

    let create unicode ascii latexCore siunitx =
        { Unicode = unicode
          Ascii = ascii
          LatexCore = latexCore
          Siunitx = siunitx }

/// §2.2 — canonical-symbol lookup, keyed on `Dim`.
///
/// **Deliberate correction to the design.** §2.2 sketches
///
/// ```fsharp
/// val register : Dim -> symbol: string -> unit
/// ```
///
/// which is a process-global mutable table. That is the exact defect §1.1 indicts matplotlib
/// for — "Global mutable state (`pyplot`)" — and it would poison this library specifically:
/// two figures in one notebook would fight over the registry, a golden-image test would depend
/// on test ordering, and `Quiver.Core`'s "immutable values with structural equality" story
/// would have a hole in it at the exact point where axis labels are produced.
///
/// So the registry is a **value**. `add` returns a new registry, renderers take one as a
/// parameter, and `UnitRegistry.si` is a shared immutable default. Nothing here is `mutable`
/// and there is no `static let mutable`.
///
/// ### On "longest-match-wins"
///
/// §2.2 says the match should be "longest-match-wins". That phrase presupposes a *string* key,
/// where `kg·m·s⁻²` could match a longer or shorter prefix of the symbol. Keyed on `Dim` there
/// is no such ordering: a `Dim` is a 7-vector of exponents and the lookup is exact equality on
/// all seven. There is nothing to be longest about, so the concept does not survive the move
/// to a dimension key — and the dimension key is right, because it is the only thing that
/// makes `mul force metre` come out as `J` (§12.6).
///
/// What "longest match" *would* buy, and what is deliberately not implemented here, is
/// **partial factorisation**: rendering entropy `kg·m²·s⁻²·K⁻¹` as `J·K⁻¹` by peeling off the
/// largest registered unit that divides the dimension. That is a real readability win, but it
/// is ambiguous (`J/K` vs `N·m/K`), it is not confluent in general, and picking a wrong
/// factorisation produces a label that is *misleading* rather than merely verbose. Exact match
/// plus a full base-unit fallback is always right; factorisation is a feature that can be added
/// later behind its own function without changing this type.
///
/// The residual ambiguity that *is* real: several SI units share one `Dim`. Hz and Bq are both
/// s⁻¹; Gy and Sv are both J·kg⁻¹; lm and cd are both `J=1`. A `Dim`-keyed table can hold only
/// one of each. `si` resolves those by insertion order (last write wins) and by omission — see
/// its documentation.
[<Sealed>]
type UnitRegistry(entries: (Dim * UnitSymbol)[]) =

    // A Dictionary built once inside the constructor and never handed out. `Dim` supplies a
    // hand-written GetHashCode and IEquatable, so this is a genuine O(1) lookup rather than a
    // trip through generic structural equality. `Map` is not an option: `Dim` is
    // `[<NoComparison>]`, and rightly so — dimension vectors have no meaningful order.
    let index =
        let t = System.Collections.Generic.Dictionary<Dim, UnitSymbol>(entries.Length)
        // Later entries overwrite earlier ones: that is the documented tie-break for the
        // Hz/Bq and Gy/Sv dimension collisions.
        for (k, v) in entries do
            t.[k] <- v

        t

    /// The registrations, in insertion order. Duplicates are preserved here even though the
    /// lookup keeps only the last, so `add` can rebuild without losing provenance.
    member _.Entries = entries

    /// Number of *distinct dimensions* covered, which is not `Entries.Length` when a dimension
    /// was registered twice.
    member _.Count = index.Count

    member _.TryCanonical(dim: Dim) : UnitSymbol option =
        match index.TryGetValue dim with
        | true, v -> Some v
        | _ -> None

[<RequireQualifiedAccess>]
module UnitRegistry =

    let empty = UnitRegistry([||])

    /// Register a preferred symbol for a dimension. Returns a **new** registry; the argument
    /// is untouched. Registry-last so it pipes:
    /// `UnitRegistry.si |> UnitRegistry.add myDim mySymbol`.
    let add (dim: Dim) (symbol: UnitSymbol) (registry: UnitRegistry) =
        UnitRegistry(Array.append registry.Entries [| dim, symbol |])

    let ofList (entries: (Dim * UnitSymbol) list) = UnitRegistry(List.toArray entries)

    let ofSeq (entries: (Dim * UnitSymbol) seq) = UnitRegistry(Array.ofSeq entries)

    let toList (registry: UnitRegistry) = List.ofArray registry.Entries

    /// Merge two registries; the second wins on any dimension both define.
    let merge (a: UnitRegistry) (b: UnitRegistry) =
        UnitRegistry(Array.append a.Entries b.Entries)

    /// The preferred symbol for a dimension, if one is registered. Registry-*first*, matching
    /// the argument order of `UnitDisplay.render`, because here the `Dim` is the query and the
    /// registry is the environment.
    let tryCanonical (registry: UnitRegistry) (dim: Dim) = registry.TryCanonical dim

    let contains (registry: UnitRegistry) (dim: Dim) = (registry.TryCanonical dim).IsSome

    // ---- The default -----------------------------------------------------------------

    /// The named SI derived units, keyed by the `Dim` of their own witnesses — so the registry
    /// and the witnesses cannot drift apart, because there is only one place the dimension is
    /// written down.
    ///
    /// **Four of the 22 are deliberately absent, and two collisions are resolved by order.**
    /// A `Dim`-keyed table is exact; where SI gives one dimension two names, something has to
    /// give:
    ///
    /// * `Bq` is registered before `Hz`, so **Hz wins** s⁻¹. A frequency axis is far more
    ///   common than an activity axis, and a becquerel axis mislabelled `Hz` is at least the
    ///   right dimension. Add `Bq` back with `add` if you are plotting radioactivity.
    /// * `Sv` is registered before `Gy`, so **Gy wins** J·kg⁻¹ — absorbed dose is the physical
    ///   quantity, equivalent dose is a weighted derived one.
    /// * `lm` is **omitted entirely**: lumen and candela have the same dimension (`J=1`, sr
    ///   being dimensionless), so registering it would relabel every candela axis `lm`. That
    ///   is not a tie-break, it is a bug, and the base-unit fallback gets `cd` right.
    /// * `lx` is **omitted** for the same reason one level up: lux and candela-per-square-metre
    ///   (luminance, the nit) share `J=1, L=-2`.
    /// * `rad` and `sr` are **omitted** because their dimension is `Dim.zero`. Registering
    ///   either would label every ratio, probability and dimensionless axis in the library
    ///   `rad`. Angle is carried by the *measure tag* `rad`, not by the dimension vector, which
    ///   is exactly why Core made it a tag.
    ///
    /// `withPhotometry` puts `lm` and `lx` back for callers who know their axes are photometric.
    let si =
        ofList
            [ Derived.becquerel.Dim, UnitSymbol.plain "Bq" "\\becquerel"
              Derived.hertz.Dim, UnitSymbol.plain "Hz" "\\hertz"
              Derived.newton.Dim, UnitSymbol.plain "N" "\\newton"
              Derived.pascal.Dim, UnitSymbol.plain "Pa" "\\pascal"
              Derived.joule.Dim, UnitSymbol.plain "J" "\\joule"
              Derived.watt.Dim, UnitSymbol.plain "W" "\\watt"
              Derived.coulomb.Dim, UnitSymbol.plain "C" "\\coulomb"
              Derived.volt.Dim, UnitSymbol.plain "V" "\\volt"
              Derived.farad.Dim, UnitSymbol.plain "F" "\\farad"
              Derived.ohm.Dim, UnitSymbol.create "Ω" "ohm" "\\Omega" "\\ohm"
              Derived.siemens.Dim, UnitSymbol.plain "S" "\\siemens"
              Derived.weber.Dim, UnitSymbol.plain "Wb" "\\weber"
              Derived.tesla.Dim, UnitSymbol.plain "T" "\\tesla"
              Derived.henry.Dim, UnitSymbol.plain "H" "\\henry"
              Derived.sievert.Dim, UnitSymbol.plain "Sv" "\\sievert"
              Derived.gray.Dim, UnitSymbol.plain "Gy" "\\gray"
              Derived.katal.Dim, UnitSymbol.plain "kat" "\\katal" ]

    /// `si` plus lumen and lux. Opt-in, because it makes candela axes render as lumens and
    /// luminance axes render as lux — correct only if you already know the axis is photometric.
    let withPhotometry (registry: UnitRegistry) =
        registry
        |> add Derived.lumen.Dim (UnitSymbol.plain "lm" "\\lumen")
        |> add Derived.lux.Dim (UnitSymbol.plain "lx" "\\lux")
