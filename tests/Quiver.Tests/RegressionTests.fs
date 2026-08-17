/// §12 — the verified findings, locked in.
///
/// §12 opens with "Each cost real time to diagnose and each will cost you the same time if it
/// isn't written down." Writing it down in prose is necessary but not sufficient: prose does not
/// fail a build. Every finding below is one landmine that was found once, defused, and could be
/// re-armed by an innocent-looking change. These tests are what stops that.
///
/// Several of them are **compile-time** assertions: a binding whose type annotation is the
/// claim. Those are the strongest form available — the finding is re-checked on every build,
/// not on every test run — and they are the same technique `Derived.newtonByComposition` uses
/// inside the library itself (D20).
module Quiver.Tests.RegressionTests

open Xunit
open Quiver

// ======================================================================================
// §12.3 — `float<'u>` does not satisfy a `float` SRTP overload
//
//   "An `Interp` witness declared as `static member inline Interp (a: float, ...)` **rejects**
//    `float<m>`. Measures are erased at *runtime*, but they are absolutely present during
//    overload resolution. Fix: declare the overload measure-generic ... which subsumes the
//    unitless case since `float = float<1>`. **Every type class in this library must be
//    measure-generic from its first line.**"
//
// If someone ever "simplifies" `Interpolate.Interp(a: float<'u>, ...)` to `(a: float, ...)`,
// the four bindings below stop compiling. That is the whole test.
// ======================================================================================

module ``12_3 measured floats satisfy the Interp overload`` =

    let private metres: float<m> = interp 0.0<m> 10.0<m> 0.5
    let private seconds: float<s> = interp 2.0<s> 4.0<s> 0.25
    let private energy: float<J> = interp 0.0<J> 8.0<J> 0.75

    /// The unitless case still works, because `float = float<1>` — the half of §12.3 that makes
    /// the measure-generic overload a strict improvement rather than a trade.
    let private plain: float = interp 0.0 1.0 0.5

    let private velocity: Vec2<m / s> =
        interp { X = 0.0<m / s>; Y = 0.0<m / s> } { X = 4.0<m / s>; Y = 8.0<m / s> } 0.5

    [<Fact>]
    let ``interp accepts float<m> and returns the right value`` () =
        Assert.Equal(5.0, float metres, 12)
        Assert.Equal(2.5, float seconds, 12)
        Assert.Equal(6.0, float energy, 12)
        Assert.Equal(0.5, plain, 12)
        Assert.Equal(2.0, float velocity.X, 12)

    /// §12.3's own sentence, checked from the other side: "measures are erased at *runtime*, but
    /// they are absolutely present during overload resolution."
    ///
    /// The runtime half is observable — a `float<m>` boxes as a plain `Double`, and even
    /// `typeof<float<m>>` **is** `typeof<float>`, because the measure never reaches metadata.
    /// The compile-time half is not observable by reflection at all, which is precisely why the
    /// bindings above are the test: `interp 0.0<m> 10.0<m> 0.5` either compiles or it does not.
    [<Fact>]
    let ``measures are erased at runtime and present at compile time`` () =
        let boxed: obj = box 3.0<m>
        Assert.Equal(typeof<float>, boxed.GetType())
        // Erased so thoroughly that even the reflected type of the measured type is bare.
        Assert.Equal(typeof<float>, typeof<float<m>>)
        Assert.Equal(typeof<float<m>>, typeof<float<s>>)
        // And yet `metres` above could not have been written with a float-only overload, and
        // adding `let bad: float<s> = metres` here would not compile.
        Assert.Equal(5.0, float metres, 12)

// ======================================================================================
// §12.4 — `Lerp` collides with the BCL
//
//   "Candidates: - System.Double.Lerp(value1, value2, amount) : float
//                - static member Lerpable.Lerp: a: float * b: float * t: float -> float
//    .NET 9+ added `Lerp` to the numeric interfaces. **Fix:** name the SRTP member something the
//    BCL doesn't use (`Interp`)."
// ======================================================================================

module ``12_4 the SRTP member is Interp, not Lerp`` =

    open Microsoft.FSharp.Reflection

    /// The generic call resolves. Under the old name this line was an ambiguous-overload error
    /// against `System.Double.Lerp`, and it reported at the *call site*, not the declaration.
    let private resolved: float = interp 1.0 3.0 0.5

    [<Fact>]
    let ``the generic call resolves against the witness`` () = Assert.Equal(2.0, resolved, 12)

    /// The witness must not offer a member called `Lerp`. Checked by reflection rather than by
    /// trying to compile one, because the failure being guarded against is a *name*, and a name
    /// is exactly what reflection can see.
    [<Fact>]
    let ``the Interpolate witness declares Interp and never Lerp`` () =
        let members =
            typeof<Interpolate>.GetMethods(
                System.Reflection.BindingFlags.Public
                ||| System.Reflection.BindingFlags.Static
                ||| System.Reflection.BindingFlags.DeclaredOnly
            )
            |> Array.map (fun m -> m.Name)

        Assert.DoesNotContain("Lerp", members)
        Assert.Contains("Interp", members)
        // One overload per supported type: float<'u>, float32, Vec2, Vec3, Color, Rect.
        Assert.Equal(6, members |> Array.filter ((=) "Interp") |> Array.length)

    /// The BCL member the collision was with is still there, so the hazard has not gone away —
    /// it has been named around. Worth asserting so that a future rename back to `Lerp` is
    /// understood to be reintroducing a known conflict.
    [<Fact>]
    let ``System.Double still has a Lerp, which is why the witness may not`` () =
        let bcl =
            typeof<System.Double>.GetMethods(System.Reflection.BindingFlags.Public ||| System.Reflection.BindingFlags.Static)
            |> Array.exists (fun m -> m.Name = "Lerp")

        Assert.True(bcl, "if this ever fails, .NET removed Double.Lerp and §12.4's constraint relaxed")

    [<Fact>]
    let ``Interpolate is sealed and abstract, so it is a witness and not a value`` () =
        Assert.True(typeof<Interpolate>.IsSealed && typeof<Interpolate>.IsAbstract)
        Assert.False(FSharpType.IsUnion typeof<Interpolate>)

// ======================================================================================
// §12.6 — Runtime symbol composition needs canonicalisation
//
//   "`mul force metre` produced the symbol `kg·m/s/s·m` for what is `J`. The `Dim` was correct
//    (`L=2, M=1, T=-2`); only the string was wrong. Confirms §2.2: **never compose display
//    strings, always canonicalise from `Dim`.**"
//
// Both halves are asserted: the composed symbol really is junk (so the finding is real and the
// `Symbol` field really is untrusted), and canonicalisation from `Dim` really does produce `J`.
// ======================================================================================

module ``12_6 composed symbols are junk while the Dim is right`` =

    let private force = U.mul U.kilogram U.metrePerSecondSquared
    let private energy = U.mul force U.metre

    [<Fact>]
    let ``the composed symbol is wrong`` () =
        // Not asserted character-for-character: the exact junk depends on which witnesses were
        // multiplied. What matters is that it is *not* the canonical name, which is the claim.
        Assert.NotEqual<string>("J", energy.Symbol)
        Assert.Contains("kg", energy.Symbol)
        Assert.Contains("/", energy.Symbol)

    [<Fact>]
    let ``the composed Dim is right`` () =
        Assert.Equal(
            { L = 2
              M = 1
              T = -2
              I = 0
              Th = 0
              N = 0
              J = 0 },
            energy.Dim
        )

        Assert.Equal(Derived.joule.Dim, energy.Dim)

    /// The rule §2.2 and §12.6 exist to enforce, exercised through the renderer that enforces it.
    [<Fact>]
    let ``canonicalisation from Dim yields J`` () =
        Assert.Equal("J", UnitDisplay.renderSi Unicode energy.Dim)
        Assert.Equal("J", UnitDisplay.renderSi Ascii energy.Dim)
        Assert.Equal("\\mathrm{J}", UnitDisplay.renderSi Latex energy.Dim)
        Assert.Equal("\\qty{}{\\joule}", UnitDisplay.renderSi Siunitx energy.Dim)

    /// The other two single-letter base dimensions named in the brief. `N` is the *mole* field
    /// of `Dim` and `J` is the *candela* field, which is a naming trap of its own: `Dim.N` is
    /// amount of substance, not newtons, and `Dim.J` is luminous intensity, not joules.
    [<Fact>]
    let ``canonicalisation from Dim yields the base symbols for N and J fields`` () =
        Assert.Equal("mol", UnitDisplay.renderSi Unicode Dim.amount)
        Assert.Equal("cd", UnitDisplay.renderSi Unicode Dim.luminosity)

        // And the *newton* — the symbol N — comes from the force dimension, not from Dim.N.
        Assert.Equal("N", UnitDisplay.renderSi Unicode force.Dim)
        Assert.Equal(1, force.Dim.M)
        Assert.Equal(0, force.Dim.N)

    /// The base-unit fallback, which is what `renderExpanded` is for: force spelled out rather
    /// than named. §2.2's own siunitx example.
    [<Fact>]
    let ``renderExpanded spells out the base units in SI order`` () =
        Assert.Equal("kg·m·s⁻²", UnitDisplay.renderExpanded Unicode force.Dim)
        Assert.Equal("kg.m.s^-2", UnitDisplay.renderExpanded Ascii force.Dim)
        Assert.Equal("\\qty{}{\\kilogram\\metre\\per\\second\\squared}", UnitDisplay.renderExpanded Siunitx force.Dim)

    /// D18 — the ohm needs four spellings, and deriving three from one would be exactly the
    /// string surgery §12.6 forbids.
    [<Fact>]
    let ``the ohm has four independent spellings`` () =
        let dim = Derived.ohm.Dim
        Assert.Equal("Ω", UnitDisplay.renderSi Unicode dim)
        Assert.Equal("ohm", UnitDisplay.renderSi Ascii dim)
        Assert.Equal("\\mathrm{\\Omega}", UnitDisplay.renderSi Latex dim)
        Assert.Equal("\\qty{}{\\ohm}", UnitDisplay.renderSi Siunitx dim)

// ======================================================================================
// D17 — a `[<Measure>] type min` shadows `Operators.min`
//
//   "The namespace is opened more recently than `FSharp.Core`, so in application position the
//    *type* wins. The consequence is not local: **every file that opens `Quiver` would lose the
//    `min` function.** The measure is named `minute` instead."
//
// This file opens `Quiver`. If anyone renames `minute` to `min` — or adds a measure called
// `max`, `abs`, `sign`, `floor`, `round`, `id`, `not`, `fst` or `snd` — the calls below stop
// compiling with `FS1133: No constructors are available for the type ...`.
// ======================================================================================

module ``D17 Operators survive open Quiver`` =

    [<Fact>]
    let ``min and max still resolve to Operators`` () =
        Assert.Equal(3, min 3 7)
        Assert.Equal(7, max 3 7)
        Assert.Equal(3.5, min 3.5 7.5, 12)
        Assert.Equal(7.5, max 3.5 7.5, 12)

    /// D17 lists the whole set of names to check before naming a measure. Every one of them is
    /// exercised here in *application position*, which is where the shadowing bites.
    [<Fact>]
    let ``the rest of D17's watch list still resolves`` () =
        Assert.Equal(3.0, abs -3.0, 12)
        Assert.Equal(-1, sign -4)
        Assert.Equal(3.0, floor 3.7, 12)
        Assert.Equal(4.0, ceil 3.2, 12)
        Assert.Equal(4.0, round 3.5, 12)
        Assert.Equal(3.0, truncate 3.9, 12)
        Assert.Equal(2.0, sqrt 4.0, 12)
        Assert.Equal(0.0, log 1.0, 12)
        Assert.Equal(1.0, exp 0.0, 12)
        Assert.Equal(0.0, sin 0.0, 12)
        Assert.Equal(1.0, cos 0.0, 12)
        Assert.Equal(0.0, tan 0.0, 12)
        Assert.Equal(7, id 7)
        Assert.Equal(1, fst (1, 2))
        Assert.Equal(2, snd (1, 2))
        Assert.True(not false)
        Assert.Equal(5uy, byte 5)
        Assert.Equal('a', char 97)
        Assert.Equal(0, compare 1 1)

    /// The measure that would have caused it is named `minute`, and its witness still spells
    /// itself `min` — which is the point: the *symbol* may be `min`, only the F# identifier may
    /// not be.
    [<Fact>]
    let ``the minute measure is named minute and its symbol is min`` () =
        Assert.Equal("min", NonSi.minute.Symbol)
        Assert.Equal(60.0, NonSi.minute.ToSI, 12)
        let t: float<minute> = 90.0<minute>
        Assert.Equal(5400.0, U.toSI NonSi.minute t, 9)

    /// D17 also notes the names that are fine, so the rule is not read as "avoid short names".
    [<Fact>]
    let ``the short measure names D17 clears are in use and harmless`` () =
        let hours: float<h> = 2.0<h>
        let days: float<d> = 1.0<d>
        let litres: float<L> = 3.0<L>
        let teslas: float<T> = 1.0<T>
        let kelvins: float<K> = 300.0<K>
        let newtons: float<N> = 5.0<N>
        let joules: float<J> = 7.0<J>
        let watts: float<W> = 9.0<W>

        Assert.Equal(7200.0, U.toSI NonSi.hour hours, 9)
        Assert.Equal(86400.0, U.toSI NonSi.day days, 9)
        Assert.Equal(3e-3, U.toSI NonSi.litre litres, 12)
        Assert.Equal(1.0, U.toSI Derived.tesla teslas, 12)
        Assert.Equal(300.0, U.toSI U.kelvin kelvins, 12)
        Assert.Equal(5.0, U.toSI Derived.newton newtons, 12)
        Assert.Equal(7.0, U.toSI Derived.joule joules, 12)
        Assert.Equal(9.0, U.toSI Derived.watt watts, 12)

// ======================================================================================
// §12.1 / D2 — measure algebra never moves into an operator overload
// ======================================================================================

module ``12_1 measure algebra stays in module functions`` =

    /// D2 — operators on `Vec2`/`Vec3` are measure-*preserving* only. If someone adds the §3
    /// sketch's `( * ) (k: float<'k>, v: Vec2<'u>) : Vec2<'k * 'u>`, FS0064 fires and
    /// `TreatWarningsAsErrors` turns it into a build failure. These bindings assert that the
    /// sanctioned spellings still work.
    let private preserved: Vec2<m> = { X = 1.0<m>; Y = 2.0<m> } + { X = 3.0<m>; Y = 4.0<m> }
    let private byPlainFloat: Vec2<m> = 2.0 * { X = 1.0<m>; Y = 2.0<m> }
    let private changed: Vec2<m> = Vec2.scaleBy 2.0<s> { X = 1.0<m / s>; Y = 2.0<m / s> }
    let private dotted: float<m * s> = Vec2.dot { X = 1.0<m>; Y = 2.0<m> } { X = 3.0<s>; Y = 4.0<s> }

    [<Fact>]
    let ``measure-preserving operators and measure-changing functions both work`` () =
        Assert.Equal(4.0, float preserved.X, 12)
        Assert.Equal(2.0, float byPlainFloat.X, 12)
        Assert.Equal(2.0, float changed.X, 12)
        Assert.Equal(11.0, float dotted, 12)

    /// D2's generalisation: "any measure-generic function a caller needs to instantiate by hand
    /// must declare its type parameters explicitly." `Affine.rotate<px>` is the case that forced
    /// it (FS0686 otherwise), so it is spelled out here by hand.
    [<Fact>]
    let ``Affine.rotate can be instantiated by hand`` () =
        let inPx: Affine<px, px> = Affine.rotate<px> 0.0<rad>
        let inNd: Affine<nd, nd> = Affine.rotate<nd> 0.0<rad>
        Assert.Equal(1.0, float inPx.M11, 12)
        Assert.Equal(1.0, float inNd.M11, 12)

// ======================================================================================
// §12.5 — existentials over measures work, with ceremony
// ======================================================================================

module ``12_5 rank-2 recovery of two measures`` =

    /// The exact experiment §12.5 reports: a heterogeneous list holding an `(m, m/s)` series
    /// beside an `(s, kg·m²/s²)` one, with both measures recovered inside a single consumer and
    /// measured arithmetic done on them.
    [<Fact>]
    let ``a heterogeneous PackedSeries list recovers both measures`` () =
        let motion =
            Erase.series
                "motion"
                (Scale.linear U.metre (0.0<m>, 10.0<m>))
                (Scale.linear U.metrePerSecond (0.0<m / s>, 5.0<m / s>))
                [| 0.0<m>; 10.0<m> |]
                [| 0.0<m / s>; 5.0<m / s> |]

        let energyOverTime =
            Erase.series
                "energy"
                (Scale.linear U.second (0.0<s>, 2.0<s>))
                (Scale.linear Derived.joule (0.0<J>, 8.0<J>))
                [| 0.0<s>; 2.0<s> |]
                [| 0.0<J>; 8.0<J> |]

        let dims =
            [ motion; energyOverTime ]
            |> List.map (fun p ->
                p.Accept
                    { new ISeriesConsumer<Dim * Dim> with
                        member _.Apply<[<Measure>] 'ux, [<Measure>] 'uy>(s: Series<'ux, 'uy>) =
                            // Measured arithmetic inside the consumer, on the recovered measures.
                            let spanX = s.X.[s.X.Length - 1] - s.X.[0]
                            let spanY = s.Y.[s.Y.Length - 1] - s.Y.[0]
                            ignore (spanX, spanY)
                            s.XScale.Unit.Dim, s.YScale.Unit.Dim })

        Assert.Equal<(Dim * Dim) list>(
            [ Dim.length, U.metrePerSecond.Dim
              Dim.time, Derived.joule.Dim ],
            dims
        )

    /// D4 — the series is struct-of-arrays, not an array of reference tuples. §1.3's
    /// "per-frame allocation of records will destroy a 60 fps loop" is the reason, and the shape
    /// of the type is the fix, so the shape is asserted.
    [<Fact>]
    let ``Series holds two parallel arrays, not an array of tuples`` () =
        let fields =
            Microsoft.FSharp.Reflection.FSharpType.GetRecordFields typeof<Series<m, s>>
            |> Array.map (fun f -> f.Name, f.PropertyType)

        Assert.Contains(("X", typeof<float<m>[]>), fields)
        Assert.Contains(("Y", typeof<float<s>[]>), fields)

    [<Fact>]
    let ``a mismatched series is rejected at the boundary`` () =
        Assert.Throws<System.ArgumentException>(fun () ->
            Erase.series
                "bad"
                (Scale.linear U.metre (0.0<m>, 1.0<m>))
                (Scale.linear U.second (0.0<s>, 1.0<s>))
                [| 0.0<m>; 1.0<m> |]
                [| 0.0<s> |]
            |> ignore)
        |> ignore

// ======================================================================================
// D16 / Axis — a finding of this suite's own, recorded in the same style.
// ======================================================================================

module ``D16 the erased witness carries ToSI while Axis ofErased ignores it`` =

    /// D16 changed `IScaleErased` to carry the *whole* erased witness — `ToSI` and `OffsetSI`
    /// alongside `Symbol` and `Dim` — precisely because "an electronvolt axis and a joule axis
    /// have **identical `Dim`**, and differ by 10^19".
    [<Fact>]
    let ``IScaleErased exposes ToSI and OffsetSI`` () =
        let e = Erase.scale (Scale.linear NonSi.electronvolt (1.0<eV>, 5000.0<eV>))
        Assert.True(abs (e.ToSI - 1.602176634e-19) < 1e-32)
        Assert.Equal(0.0, e.OffsetSI, 12)

    /// **DEFECT (Quiver.Units).** `Axis.ofErasedWith` still selects its prefix with
    /// `Prefix.selectFor scale.Dim` and renders with `UnitDisplay.renderPrefixed`, neither of
    /// which consults `ToSI` or `OffsetSI`. So the information D16 preserved is available and
    /// unused, and a reloaded electronvolt axis (§15) is labelled `kJ` — wrong by 10^19 — while
    /// a reloaded Celsius axis is labelled `K`.
    ///
    /// The module comment on `Axis.fs` still describes this as a gap in `Quiver.Core`
    /// ("`IScaleErased` exposes `Symbol` and `Dim` but not `ToSI` or `OffsetSI`"), which was true
    /// before D16 and is not true now. Both the comment and the implementation are stale.
    ///
    /// Asserted as current behaviour, not fixed: `Quiver.Units` is frozen for this task.
    [<Fact>]
    let ``DEFECT a reloaded electronvolt axis is labelled in joules`` () =
        let s = Scale.linear NonSi.electronvolt (1.0<eV>, 5000.0<eV>)

        Assert.Equal("E [keV]", (Axis.auto Unicode "E" s).Title)
        Assert.Equal("E [kJ]", (Axis.ofErased Unicode "E" (Erase.scale s)).Title)

    [<Fact>]
    let ``DEFECT a reloaded Celsius axis is labelled in kelvin`` () =
        let s = Scale.linear NonSi.degreeCelsius (0.0<degC>, 100.0<degC>)

        Assert.Equal("T [°C]", (Axis.auto Unicode "T" s).Title)
        Assert.Equal("T [K]", (Axis.ofErased Unicode "T" (Erase.scale s)).Title)
