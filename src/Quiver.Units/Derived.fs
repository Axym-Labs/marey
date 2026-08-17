namespace Quiver

// ---------------------------------------------------------------------------------------
// §2 — The 22 named SI derived units, as measure **abbreviations**.
//
// Abbreviations rather than opaque measures is the load-bearing choice. `[<Measure>] type N`
// on its own would be a fresh, incompatible measure, and `U.mul U.kilogram
// U.metrePerSecondSquared` would not be a `U<N>` — the witness algebra of §2.1 would stop
// being able to *derive* anything. As abbreviations they normalise to the same measure the
// algebra produces, so the compiler unifies them and §2.1's "inferred" comments come true.
// `Derived.newtonByComposition` and `Derived.jouleByComposition` at the bottom of this file
// are that claim written as a compile-time assertion.
//
// Three consequences fall out of abbreviating, and all three are real:
//
//   * `Hz` and `Bq` are both s⁻¹, so they are the **same measure**. So are `Gy` and `Sv`
//     (both J/kg). F# cannot tell them apart and neither can `Dim`; SI itself only separates
//     them by convention. `UnitRegistry.si` resolves the display collision by ordering.
//   * `J` and `N·m` are the same measure, which is the desired half of the same coin.
//   * A measure abbreviation is transparent, so `float<J>` and `float<kg m^2/s^2>` are
//     interchangeable at every call site. That is correct for *coherent* units and it is
//     exactly why non-SI units in `NonSi.fs` are opaque instead.
// ---------------------------------------------------------------------------------------

/// Hertz — s⁻¹.
[<Measure>]
type Hz = 1 / s

/// Newton — kg·m·s⁻².
[<Measure>]
type N = kg m / s^2

/// Pascal — N·m⁻².
[<Measure>]
type Pa = N / m^2

/// Joule — N·m. §12.6's running example: `U.mul force metre` composes the *symbol*
/// `kg·m/s/s·m` for what is plainly `J`, while the `Dim` comes out right. Never compose
/// display strings.
[<Measure>]
type J = N m

/// Watt — J·s⁻¹.
[<Measure>]
type W = J / s

/// Coulomb — A·s.
[<Measure>]
type C = A s

/// Volt — W·A⁻¹.
[<Measure>]
type V = W / A

/// Farad — C·V⁻¹.
[<Measure>]
type F = C / V

/// Ohm — V·A⁻¹. Named `ohm` and not `Ω`: the measure name is an F# identifier, the *symbol*
/// `Ω` is a presentation concern and lives in `UnitRegistry`.
[<Measure>]
type ohm = V / A

/// Siemens — A·V⁻¹. Note `S` (siemens) and `s` (second) differ only by case; that is SI's
/// choice, not ours, and F# is case-sensitive so it works.
[<Measure>]
type S = A / V

/// Weber — V·s.
[<Measure>]
type Wb = V s

/// Tesla — Wb·m⁻².
[<Measure>]
type T = Wb / m^2

/// Henry — Wb·A⁻¹.
[<Measure>]
type H = Wb / A

/// Steradian — solid angle.
///
/// Quiver.Core already defines `rad` as a *graphics tag* with `Dim.zero`, and Space.fs is
/// explicit that this is deliberate ("Angle is dimensionless in SI (m/m), so this is a tag,
/// not a dimension"). `rad` is therefore **not redefined here**; `sr` is defined in terms of
/// it, which is the true relationship (solid angle is angle squared) and which keeps the
/// degrees/radians compile error Core bought with the tag.
///
/// The *dimension* of both is `Dim.zero`, exactly as SI's dimensional analysis says. The
/// measure tag and the dimension vector disagree on purpose: the tag catches unit bugs, the
/// vector drives display.
[<Measure>]
type sr = rad^2

/// Lumen — cd·sr. Dimensionally this is just cd (sr is dimensionless), which is precisely why
/// `UnitRegistry.si` cannot contain it — see the note there.
[<Measure>]
type lm = cd sr

/// Lux — lm·m⁻².
[<Measure>]
type lx = lm / m^2

/// Becquerel — s⁻¹. **Identical measure to `Hz`**; see the header note.
[<Measure>]
type Bq = 1 / s

/// Gray — J·kg⁻¹.
[<Measure>]
type Gy = J / kg

/// Sievert — J·kg⁻¹. **Identical measure to `Gy`**; see the header note.
[<Measure>]
type Sv = J / kg

/// Katal — mol·s⁻¹.
[<Measure>]
type kat = mol / s

/// §2.1 — Witnesses for the named SI derived units.
///
/// Every `Dim` here is written out rather than derived through `U.mul`/`U.div`, so that the
/// witness is a *statement* of the unit's dimension that a reader can check against SI, not a
/// value that happens to fall out of an expression. `newtonByComposition` and
/// `jouleByComposition` then check the two representations agree at compile time.
[<RequireQualifiedAccess>]
module Derived =

    let private d = U.dimOf

    //                                              L   M   T   I  Th   N   J
    let hertz: U<Hz> = U<Hz>("Hz", d 0 0 -1 0 0 0 0, 1.0)
    let newton: U<N> = U<N>("N", d 1 1 -2 0 0 0 0, 1.0)
    let pascal: U<Pa> = U<Pa>("Pa", d -1 1 -2 0 0 0 0, 1.0)
    let joule: U<J> = U<J>("J", d 2 1 -2 0 0 0 0, 1.0)
    let watt: U<W> = U<W>("W", d 2 1 -3 0 0 0 0, 1.0)
    let coulomb: U<C> = U<C>("C", d 0 0 1 1 0 0 0, 1.0)
    let volt: U<V> = U<V>("V", d 2 1 -3 -1 0 0 0, 1.0)
    let farad: U<F> = U<F>("F", d -2 -1 4 2 0 0 0, 1.0)
    let ohm: U<ohm> = U<ohm>("Ω", d 2 1 -3 -2 0 0 0, 1.0)
    let siemens: U<S> = U<S>("S", d -2 -1 3 2 0 0 0, 1.0)
    let weber: U<Wb> = U<Wb>("Wb", d 2 1 -2 -1 0 0 0, 1.0)
    let tesla: U<T> = U<T>("T", d 0 1 -2 -1 0 0 0, 1.0)
    let henry: U<H> = U<H>("H", d 2 1 -2 -2 0 0 0, 1.0)
    let lumen: U<lm> = U<lm>("lm", d 0 0 0 0 0 0 1, 1.0)
    let lux: U<lx> = U<lx>("lx", d -2 0 0 0 0 0 1, 1.0)
    let becquerel: U<Bq> = U<Bq>("Bq", d 0 0 -1 0 0 0 0, 1.0)
    let gray: U<Gy> = U<Gy>("Gy", d 2 0 -2 0 0 0 0, 1.0)
    let sievert: U<Sv> = U<Sv>("Sv", d 2 0 -2 0 0 0 0, 1.0)
    let katal: U<kat> = U<kat>("kat", d 0 0 -1 0 0 1 0, 1.0)

    /// Radian. The witness for Core's graphics tag, so an angle axis can label itself. `Dim`
    /// is zero because angle is dimensionless in SI.
    let radian: U<rad> = U<rad>("rad", Dim.zero, 1.0)

    /// Steradian. Dimensionless, like `radian`.
    let steradian: U<sr> = U<sr>("sr", Dim.zero, 1.0)

    // ---- Compile-time proof that the abbreviations unify with the witness algebra ------
    //
    // These are not tests-that-run; they are tests-that-typecheck. If someone ever turns `N`
    // into an opaque measure, the build breaks here rather than three layers up in a plot.

    /// §2.1 — `mul kilogram accel` **is** a `U<N>`, inferred, not annotated into place.
    let newtonByComposition: U<N> = U.mul U.kilogram U.metrePerSecondSquared

    /// §2.1 / §12.6 — `mul force metre` **is** a `U<J>` at the type level, while its `Symbol`
    /// is the useless `kg·m/s²·m`. The `Dim` is right; the string is not. That is the entire
    /// argument for `UnitDisplay`.
    let jouleByComposition: U<J> = U.mul newtonByComposition U.metre

    /// `div joule second` is a watt.
    let wattByComposition: U<W> = U.div jouleByComposition U.second

    /// `recip second` is a hertz — "frequency, and the type of a frame rate".
    let hertzByComposition: U<Hz> = U.recip U.second

    /// `mul candela steradian` is a lumen.
    let lumenByComposition: U<lm> = U.mul U.candela steradian
