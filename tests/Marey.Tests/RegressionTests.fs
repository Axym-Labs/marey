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
module Marey.Tests.RegressionTests

open Xunit
open Marey
open Marey.UnitsOfMeasure

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
        // One overload per supported type: float<'u>, float32, Vec2, Vec3, Color, Rect, Affine.
        //
        // The count is a pin, not a fact about arity, and it caught D74 adding the seventh — which
        // is what it is for. A new overload is a decision about the witness's surface and has to
        // be acknowledged here; a *renamed* one is the failure §12.4 is actually guarding.
        Assert.Equal(7, members |> Array.filter ((=) "Interp") |> Array.length)

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
//    *type* wins. The consequence is not local: **every file that opens `Marey` would lose the
//    `min` function.** The measure is named `minute` instead."
//
// This file opens `Marey`. If anyone renames `minute` to `min` — or adds a measure called
// `max`, `abs`, `sign`, `floor`, `round`, `id`, `not`, `fst` or `snd` — the calls below stop
// compiling with `FS1133: No constructors are available for the type ...`.
// ======================================================================================

module ``D17 Operators survive open Marey`` =

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

module ``D16 the erased witness carries ToSI and Axis ofErased honours it`` =

    /// D16 changed `IScaleErased` to carry the *whole* erased witness — `ToSI` and `OffsetSI`
    /// alongside `Symbol` and `Dim` — precisely because "an electronvolt axis and a joule axis
    /// have **identical `Dim`**, and differ by 10^19".
    [<Fact>]
    let ``IScaleErased exposes ToSI and OffsetSI`` () =
        let e = Erase.scale (Scale.linear NonSi.electronvolt (1.0<eV>, 5000.0<eV>))
        Assert.True(abs (e.ToSI - 1.602176634e-19) < 1e-32)
        Assert.Equal(0.0, e.OffsetSI, 12)

    /// **Fixed at M1.** This test previously pinned the defect rather than the fix: because
    /// `Axis.ofErasedWith` selected its prefix with `Prefix.selectFor scale.Dim` and rendered
    /// with `UnitDisplay.renderPrefixed`, neither of which consults `ToSI` or `OffsetSI`, the
    /// metadata D16 went to the trouble of preserving was available and unused — and a reloaded
    /// electronvolt axis (§15) came out as `E [kJ]`, wrong by 10^19.
    ///
    /// Both decisions now route through `Prefix.selectForErased` and
    /// `UnitDisplay.unitTextErased`, which are the same functions the measured path uses, so the
    /// erased and measured labels agree by construction rather than by coincidence.
    ///
    /// **The load-bearing assertion is that the two paths agree**, which is what makes §15's
    /// "reloading gives you `IScaleErased`, not `Scale<'u>`" a survivable statement about units.
    [<Fact>]
    let ``a reloaded electronvolt axis is still labelled in electronvolts`` () =
        let s = Scale.linear NonSi.electronvolt (1.0<eV>, 5000.0<eV>)
        let measured = (Axis.auto Unicode "E" s).Title
        let reloaded = (Axis.ofErased Unicode "E" (Erase.scale s)).Title

        Assert.Equal("E [keV]", measured)
        Assert.Equal("E [keV]", reloaded)
        Assert.Equal(measured, reloaded)

    [<Fact>]
    let ``a reloaded Celsius axis is still labelled in degrees Celsius`` () =
        let s = Scale.linear NonSi.degreeCelsius (0.0<degC>, 100.0<degC>)
        let measured = (Axis.auto Unicode "T" s).Title
        let reloaded = (Axis.ofErased Unicode "T" (Erase.scale s)).Title

        Assert.Equal("T [°C]", measured)
        Assert.Equal("T [°C]", reloaded)
        Assert.Equal(measured, reloaded)

    /// The affine refusal survives erasure too: a prefix must not be glued onto an affine unit,
    /// because the prefix does not distribute over the offset (a "milli-degree-Celsius" would
    /// have to rescale 273.15 as well as the mantissa).
    [<Fact>]
    let ``an erased affine axis refuses a prefix`` () =
        let s = Scale.linear NonSi.degreeCelsius (0.0<degC>, 100.0<degC>)
        let spec = Axis.ofErased Unicode "T" (Erase.scale s)

        Assert.Equal(0, spec.Prefix.Exponent)

    /// An SI-coherent axis was always correct on the erased path, and must stay correct: the fix
    /// is a new branch, so the branch it did not take needs pinning as well.
    [<Fact>]
    let ``a reloaded SI-coherent axis is unaffected by the fix`` () =
        let s = Scale.linear U.metre (0.0<m>, 5e-9<m>)
        let measured = (Axis.auto Unicode "x" s).Title
        let reloaded = (Axis.ofErased Unicode "x" (Erase.scale s)).Title

        Assert.Equal("x [nm]", measured)
        Assert.Equal(measured, reloaded)

// ======================================================================================
// D32 — TeX markup travels in its own field, never in the plain one
//
// `AxisSpec.Title` is always displayable text and `AxisSpec.TitleTex` carries the markup, so an
// SVG renderer never draws a literal backslash and a TeX backend never has to guess whether a
// string is source or content. Before this split, `Axis.auto Siunitx` put `\qty{}{\metre}`
// straight into `Title`, which every non-TeX backend then drew verbatim.
//
// Nothing tested this path before M1: the siunitx assertions above all call `UnitDisplay`
// directly, so the whole route from a `UnitStyle` to a `TextRun` was unpinned.
// ======================================================================================

module ``D32 TeX markup is separated from displayable text`` =

    let private accel = Scale.linear U.metrePerSecondSquared (0.0<m/s^2>, 20.0<m/s^2>)

    /// The load-bearing property, and the one that needs no knowledge of the exact strings:
    /// **asking for a TeX style must not change the plain title at all.** It only adds.
    [<Fact>]
    let ``a TeX style leaves the plain title identical to the Unicode one`` () =
        let plain = Axis.auto Unicode "a" accel

        for style in [ Latex; Siunitx ] do
            let tex = Axis.auto style "a" accel
            Assert.Equal(plain.Title, tex.Title)
            Assert.Equal(plain.UnitText, tex.UnitText)

    [<Fact>]
    let ``a TeX style fills TitleTex and a plain style does not`` () =
        Assert.True((Axis.auto Siunitx "a" accel).TitleTex.IsSome)
        Assert.True((Axis.auto Latex "a" accel).TitleTex.IsSome)
        Assert.True((Axis.auto Unicode "a" accel).TitleTex.IsNone)
        Assert.True((Axis.auto Ascii "a" accel).TitleTex.IsNone)

    /// The plain title must be drawable as-is. A backslash in it means markup leaked.
    [<Fact>]
    let ``the plain title never contains markup`` () =
        for style in [ Unicode; Ascii; Latex; Siunitx ] do
            let spec = Axis.auto style "a" accel
            Assert.DoesNotContain("\\", spec.Title)

    [<Fact>]
    let ``the siunitx title carries the unit macros`` () =
        let spec = Axis.auto Siunitx "a" accel
        Assert.Equal("a [\\qty{}{\\metre\\per\\second\\squared}]", spec.TitleTex.Value)
        Assert.Equal("\\qty{}{\\metre\\per\\second\\squared}", spec.UnitTextTex.Value)

    /// The caller's quantity name sits next to already-escaped unit markup, so it has to be
    /// escaped itself or the document breaks on the first percent sign.
    [<Fact>]
    let ``the quantity name is TeX-escaped in the TeX title`` () =
        let spec = Axis.auto Siunitx "50% yield_2" accel

        Assert.Equal("50\\% yield\\_2 [\\qty{}{\\metre\\per\\second\\squared}]", spec.TitleTex.Value)
        // ...and left alone in the plain one.
        Assert.Contains("50% yield_2", spec.Title)

    /// The erased path must make the same split, or a reloaded figure loses its TeX titles.
    [<Fact>]
    let ``the erased path splits markup the same way`` () =
        let erased = Erase.scale accel
        let plain = Axis.ofErased Unicode "a" erased
        let tex = Axis.ofErased Siunitx "a" erased

        Assert.Equal(plain.Title, tex.Title)
        Assert.True(tex.TitleTex.IsSome)
        Assert.Contains("\\qty", tex.TitleTex.Value)
        Assert.True(plain.TitleTex.IsNone)

module ``D32 Tex escaping`` =

    [<Theory>]
    [<InlineData("plain", "plain")>]
    [<InlineData("50%", "50\\%")>]
    [<InlineData("a_b", "a\\_b")>]
    [<InlineData("a&b", "a\\&b")>]
    [<InlineData("#1", "\\#1")>]
    [<InlineData("$x$", "\\$x\\$")>]
    [<InlineData("{a}", "\\{a\\}")>]
    let ``escape covers the reserved characters`` (input: string) (expected: string) =
        Assert.Equal(expected, Tex.escape input)

    /// `~` and `^` are active/superscript rather than merely reserved, so a backslash is an
    /// accent that would swallow the next character instead of printing the glyph.
    [<Fact>]
    let ``tilde and caret become macros, not backslash escapes`` () =
        Assert.Equal("\\textasciitilde{}", Tex.escape "~")
        Assert.Equal("\\textasciicircum{}", Tex.escape "^")

    /// Escaping character by character rather than by successive replacement: replacing `\`
    /// first and `{` afterwards would re-escape the braces the first replacement introduced.
    [<Fact>]
    let ``a backslash does not get its own braces re-escaped`` () =
        Assert.Equal("\\textbackslash{}", Tex.escape "\\")

    [<Fact>]
    let ``escape leaves unicode alone`` () =
        Assert.Equal("kg·m·s⁻² Ω µm", Tex.escape "kg·m·s⁻² Ω µm")

    [<Fact>]
    let ``isLiteralSafe agrees with escape`` () =
        for s in [ "plain"; "50%"; "a_b"; "kg·m"; "\\qty"; "~"; "^"; "" ] do
            Assert.Equal(Tex.isLiteralSafe s, Tex.escape s = s)

// ======================================================================================
// D38 — the three characters TeX accepts and then typesets as something else
//
// Found by rendering the golden `text.tex` and *looking* at it, which is the only way it could
// have been found. `<`, `>` and `|` are legal in TeX text mode and raise no error, warning or
// diagnostic of any kind — they are simply not the glyphs that were asked for, because in the OT1
// encoding LaTeX still defaults to, those slots hold `¡`, `¿` and `—`. An axis label reading
// `a < b` typeset as `a ¡ b` in a document that compiled perfectly cleanly.
//
// The compile gate cannot see this, by construction: there is nothing wrong with the TeX. Verified
// by compiling both forms and comparing the rasters.
// ======================================================================================

module ``D38 characters that compile but mis-typeset`` =

    [<Theory>]
    [<InlineData('<', "\\textless{}")>]
    [<InlineData('>', "\\textgreater{}")>]
    [<InlineData('|', "\\textbar{}")>]
    let ``the silent three are escaped to macros`` (c: char) (expected: string) =
        Assert.Equal(Some expected, Tex.tryEscapeChar c)
        Assert.Equal(expected, Tex.escape (string c))
        // ...and they are therefore *not* safe to pass through verbatim, which is the half of
        // this that a caller consults.
        Assert.False(Tex.isLiteralSafe (string c))

    /// `"`, `` ` `` and the `--`/`---` ligatures are **not** in the table, and that is a decision
    /// rather than an omission. They render as `”`, `‘` and dashes: conventional TeX typography,
    /// and the better glyph in every case. `\textquotedbl` is also unavailable in OT1 — it raises
    /// a hard error — so escaping the quote would trade a cosmetic difference for a broken
    /// document.
    [<Fact>]
    let ``conventional TeX typography is left alone`` () =
        for s in [ "\""; "`"; "--"; "---"; "'" ] do
            Assert.True(Tex.isLiteralSafe s, s)

// The replacement macros are put through a real engine by `TikzWriterTests.escaped text
// compiles`, which is where the toolchain is available — `\textquotedbl` is a standing reminder
// that not every `\text...` command exists in every encoding, so asserting the macro names
// without compiling them would be asserting a guess.

// ======================================================================================
// D32 — pruning must not delete a run that only a typesetting backend can draw
// ======================================================================================

module ``D32 pruning respects the TeX form`` =

    let private run text tex : Node =
        Text
            { Text = text
              Tex = tex
              At = Vec2.zero<nd>
              Font =
                { Family = "Helvetica"
                  Size = 10.0<pt>
                  Weight = 400
                  Italic = false }
              Paint = Solid Color.black
              Anchor = AnchorStart
              Baseline = BaselineAlphabetic
              Rotation = 0.0<rad> }

    [<Fact>]
    let ``a TeX-only run survives pruning`` () =
        let n = run "" (Some "\\qty{}{\\metre}")
        Assert.Equal(n, Scene.prune n)

    [<Fact>]
    let ``a run empty in both forms is pruned`` () =
        Assert.Equal(Scene.empty, Scene.prune (run "" None))
        Assert.Equal(Scene.empty, Scene.prune (run "" (Some "")))

    [<Fact>]
    let ``a plain-only run survives pruning`` () =
        let n = run "x" None
        Assert.Equal(n, Scene.prune n)

// ---------------------------------------------------------------------------------------------
// D41 — a caller-supplied factorisation is displayed, and §12.6 stays fixed.
// ---------------------------------------------------------------------------------------------

/// D41 gave `U<'u>` a `Factors` list and taught `UnitDisplay` to render from it when the registry
/// has no name for the whole dimension. The tests below are the boundary, because the feature sits
/// one inch from two rules that must not move:
///
/// * **§12.6** — `mul force metre` once produced the string `kg·m/s/s·m`. That bug is why display
///   goes through `Dim`. `Factors` is structure rather than a string and merges exponents per
///   symbol, so the same call cannot reproduce it; `the 12.6 composition still canonicalises`
///   runs the original expression and demands the canonical answer.
/// * **D18** — no factorisation may be *inferred* from a bare `Dim`, because the same vector is
///   `J/K` and `N·m/K`. Nothing here infers: the caller wrote `div volt metre`. The registry
///   still wins on a hit, which is what `a registered name still beats the factorisation` pins.
module ``D41 a supplied factorisation is displayed`` =

    let private text style p u =
        UnitDisplay.unitText style UnitRegistry.si p u

    let private mega = Prefix.ofExponent 6 |> Option.get

    let private voltPerMetre = U.div Derived.volt U.metre

    [<Fact>]
    let ``a composed unit renders from its factors, not from base units`` () =
        // The motivating case: before D41 this read `kg·m·s⁻³·A⁻¹`, on an axis whose reader
        // wanted `V·m⁻¹`.
        Assert.Equal("V·m⁻¹", text Unicode Prefix.none voltPerMetre)

    [<Fact>]
    let ``a prefix attaches to the leading factor`` () =
        Assert.Equal("MV·m⁻¹", text Unicode mega voltPerMetre)

    [<Fact>]
    let ``siunitx gets real unit macros, not text fallbacks`` () =
        // `\text{V}` would typeset and would be wrong: the siunitx path exists so that a
        // physicist's own macros are used.
        Assert.Equal("\\qty{}{\\volt\\per\\metre}", text Siunitx Prefix.none voltPerMetre)
        Assert.Equal("\\qty{}{\\mega\\volt\\per\\metre}", text Siunitx mega voltPerMetre)

    [<Fact>]
    let ``the prefixed and unprefixed forms name the same unit`` () =
        // The bug found while building this: the `Exponent = 0` path short-circuited to a
        // `Dim`-only renderer, so an unprefixed axis read `kg·m·s⁻³·A⁻¹` while a prefixed one
        // read `MV·m⁻¹` — the same quantity, spelled two different ways depending on its range.
        Assert.EndsWith("V·m⁻¹", text Unicode Prefix.none voltPerMetre)
        Assert.EndsWith("V·m⁻¹", text Unicode mega voltPerMetre)

    [<Fact>]
    let ``a registered name still beats the factorisation`` () =
        // D18's rule, and the reason the registry is consulted first: `N·m` is a joule.
        Assert.Equal("J", text Unicode Prefix.none (U.mul Derived.newton U.metre))

    [<Fact>]
    let ``the 12.6 composition still canonicalises`` () =
        // §12.6 verbatim: `mul force metre`, which produced `kg·m/s/s·m` as a string.
        let force = U.mul U.kilogram (U.div U.metre (U.square U.second))
        Assert.Equal("J", text Unicode Prefix.none (U.mul force U.metre))

    [<Fact>]
    let ``like factors combine rather than repeat`` () =
        // The property the string concatenation lacked. `m·m` is not an answer.
        Assert.Equal("m²", text Unicode Prefix.none (U.mul U.metre U.metre))

    [<Fact>]
    let ``factors that cancel disappear`` () =
        let ratio = U.div U.metre U.metre
        Assert.Equal<(string * int) list>([], ratio.Factors)
        // Dimensionless, so there is no unit to print at all.
        Assert.Equal("", text Unicode Prefix.none ratio)

    [<Fact>]
    let ``the kilogram refuses a prefix it cannot carry`` () =
        // `Mkg` is not a unit. The honest `10ⁿ·` multiplier is the documented fallback, and the
        // gram shift above it only applies to a bare mass.
        Assert.Equal("10⁶·kg·s⁻¹", text Unicode mega (U.div U.kilogram U.second))

    [<Fact>]
    let ``a declared unit is unaffected`` () =
        // `U.metrePerSecond` is declared, not composed, so it carries no factors and renders
        // exactly as it did before D41. This is what kept all 29 golden baselines still.
        Assert.Equal<(string * int) list>([], U.metrePerSecond.Factors)
        Assert.Equal("m·s⁻¹", text Unicode Prefix.none U.metrePerSecond)

    [<Fact>]
    let ``the erased path agrees with the measured one`` () =
        // §15: a reloaded figure has only `IScaleErased`. If `Factors` did not cross that
        // boundary, a saved field axis would relabel itself `kg·m·s⁻³·A⁻¹` on load.
        let s = Scale.linear voltPerMetre (0.0<_>, 1.0<_>)
        let erased = Erase.scale s

        Assert.Equal<(string * int) list>(voltPerMetre.Factors, erased.Factors)

        Assert.Equal(
            text Unicode mega voltPerMetre,
            UnitDisplay.unitTextErasedWith
                Unicode
                UnitRegistry.si
                mega
                erased.Symbol
                erased.Factors
                erased.Dim
                erased.ToSI
                erased.OffsetSI
        )

// ======================================================================================
// D98 — an angle survives the erasure that `Dim` cannot see it through
// ======================================================================================

/// Angle is dimensionless in SI, so `rad/s` and `Hz` share the dimension s⁻¹. Until D98 the
/// renderer named `rad/s` by its dimension — `Hz` — the name table called the axis `f`, and
/// `Figure.unify` let a rad/s layer and a Hz layer share an axis: an angular velocity and a
/// frequency, a factor of 2π apart, plotted on one scale without a word. The witness had always
/// carried the angle in its factors; nothing read them.
module ``D98 an angle is not a frequency`` =

    let private radPerSecond = U.div Derived.radian U.second
    let private text (u: U<'u>) = UnitDisplay.unitText Unicode UnitRegistry.si Prefix.none u

    [<Fact>]
    let ``rad/s renders as rad/s, and a hertz is still a hertz`` () =
        Assert.Equal("rad·s⁻¹", text radPerSecond)
        Assert.Equal("rad·s⁻²", text (U.div radPerSecond U.second))
        Assert.Equal("Hz", text Derived.hertz)
        Assert.Equal("Hz", text (U.recip U.second))
        Assert.Equal("rad", text Derived.radian)

    /// The erased path reads the same factors, so a reloaded figure labels itself the same (§15).
    [<Fact>]
    let ``the erased witness renders the angle too`` () =
        let erased = Erase.scale (Scale.linear radPerSecond (0.0<rad / s>, 1.0<rad / s>))

        Assert.Equal(
            "rad·s⁻¹",
            UnitDisplay.unitTextErasedWith Unicode UnitRegistry.si Prefix.none erased.Symbol erased.Factors erased.Dim erased.ToSI erased.OffsetSI
        )

    [<Fact>]
    let ``an angle, an angular velocity and an angular acceleration have their own names`` () =
        let named (u: U<'u>) = Axes.defaultLabelFor AxisLeft (U.angleExponentOf u.Symbol u.Factors) u.Dim

        Assert.Equal("θ", named Derived.radian)
        Assert.Equal("ω", named radPerSecond)
        Assert.Equal("α", named (U.div radPerSecond U.second))
        Assert.Equal("f", named Derived.hertz)

    [<Fact>]
    let ``a phase-space axis is titled ω [rad·s⁻¹]`` () =
        let fig = Chart.line Derived.radian radPerSecond [| 0.0<rad>; 1.0<rad> |] [| 0.0<rad / s>; 2.0<rad / s> |]
        let scene = Chart.toScene (Chart.size 400.0 300.0) fig

        let titles =
            scene
            |> Scene.fold
                (fun acc n ->
                    match n with
                    | Text t -> t.Text :: acc
                    | _ -> acc)
                []

        Assert.Contains("ω [rad·s⁻¹]", titles)
        Assert.Contains("θ [rad]", titles)

    [<Fact>]
    let ``an angular velocity and a frequency cannot share an axis`` () =
        let ts = [| 0.0<s>; 1.0<s> |]

        let fig =
            Chart.line U.second radPerSecond ts [| 0.0<rad / s>; 6.28<rad / s> |]
            |> Chart.addLine U.second Derived.hertz ts [| 0.0<Hz>; 1.0<Hz> |]

        let ex = Assert.ThrowsAny<exn>(fun () -> Figure.unify fig.Panels.[0] |> ignore)
        Assert.Contains("2π", ex.Message)
        Assert.Contains("rad·s⁻¹", ex.Message)
        Assert.Contains("Hz", ex.Message)

    /// Two angular velocities share an axis, as they always did.
    [<Fact>]
    let ``two angular velocities still share an axis`` () =
        let ts = [| 0.0<s>; 1.0<s> |]

        let fig =
            Chart.line U.second radPerSecond ts [| 0.0<rad / s>; 6.28<rad / s> |]
            |> Chart.addLine U.second radPerSecond ts [| 1.0<rad / s>; 2.0<rad / s> |]

        Assert.Equal(2, (Figure.unify fig.Panels.[0]).Layers.Length)

    /// The documented gap: a witness declared by hand with a compound symbol carries no factors,
    /// and its symbol is not `rad`, so the angle is not seen. Pinned so the gap is a decision
    /// rather than a surprise; compose angular units instead.
    [<Fact>]
    let ``a hand-declared compound angular witness is not recognised`` () =
        let declared = U<rad / s>("rad/s", Dim.neg Dim.time, 1.0)
        Assert.Equal(0, U.angleExponentOf declared.Symbol declared.Factors)
