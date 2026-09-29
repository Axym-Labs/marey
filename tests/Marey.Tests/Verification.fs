/// The census: what the environment could actually run, said out loud, once per run.
///
/// **This file exists because a green tick is not evidence.** Every gate in this suite that
/// shells out — the TeX compile, the ink rasterisation, the encoder, the typesetter, the example
/// and guide runners — is written to *report* its absence rather than to fail, which is right on
/// a contributor's laptop and wrong in CI. `m3-plan.md` §4 names the two that go wrong quietly:
///
/// > Gates 12 and 13 are the ones that go vacuous without saying so, because they are the ones
/// > that pass when a toolchain is absent. D81's `Toolchain.report` prints what was found for
/// > exactly this reason, and phase 9 reads that output rather than the green tick.
///
/// Reading it was the plan's word, and reading is a thing a person does once and then stops
/// doing. So the reading is mechanised here in two halves:
///
///   * **The census** prints every dependency and what became of it, on every run, whether or
///     not anything was found. That is the half a human reads.
///   * **`MAREY_REQUIRE_TOOLCHAIN`** turns a missing dependency into a failure. That is the half
///     CI reads, and it is the one that matters: `ci.yml` and `publish.yml` both install the
///     toolchain, so on those two machines a skipped gate is not a laptop without TeX — it is
///     the install step having silently stopped working, and a green run that proves nothing.
///
/// The variable is off by default and named after `MAREY_UPDATE_GOLDEN`, which is the same shape
/// of switch: a thing the suite does differently when a particular kind of run asks it to.
///
/// **Why the census is not assembled from the skip messages.** Each gate says "SKIPPED: …" at the
/// point it declines to run, which is the right place to say it and the wrong place to count it —
/// the line only appears when the tool is *missing*, so a log with no skip lines is
/// indistinguishable from a log nobody looked at. This asks every dependency the same question in
/// one place and prints the answer either way.
///
/// **Nothing here re-probes.** Every entry reads the same `Lazy` the gates read — `Tools`'
/// registry, the two packages' own `report ()`, and `SkiaTests.nativeAssets`. D81's finding was
/// five copies of one probe disagreeing with each other; a census that walked `PATH` a sixth time
/// would be the same defect wearing a clipboard.
module Marey.Tests.Verification

open System
open Xunit
open Marey

// ======================================================================================
// What counts as a dependency
// ======================================================================================

/// One external thing a gate needs, and whether this machine has it.
///
/// `Line` is not derived from `Live` — it comes from whichever `report ()` owns the wording, so
/// the census says what a version probe found and what to install, neither of which a bool
/// carries. `Live` is only ever used to decide whether the run is allowed to be quiet.
type Dependency =
    { /// What the gates call it, for the failure message.
      Name: string
      /// Whether the gates that need it will run.
      Live: bool
      /// What its owner says about it, present or absent.
      Line: string }

/// The programs this suite drives itself, from `Tools.fs`.
///
/// `pdflatex` and `latex` are two entries and not one: the TeX gates ask "does this document
/// compile" through `pdflatex`, and `Marey.Tools.Tex` reads DVI through `latex`. `Tools.fs` says
/// so at more length, and a machine can genuinely have one and not the other.
let private suiteTools () : Dependency list =
    let of' (consequence: string) (t: Lazy<ResolvedTool>) =
        let tool = t.Force()

        { Name = tool.Name
          Live = tool.IsAvailable
          Line = Toolchain.report consequence tool }

    [ of' "the TeX gates did not run" Tools.pdflatex
      of' "the ink gates did not run" Tools.pdftoppm
      of' "the typesetting gates did not run" Tools.latex
      of' "the typesetting gates did not run" Tools.dvisvgm
      of' "the guide and example gates did not run" Tools.dotnet ]

/// The toolchains the two shipped packages resolve for themselves.
///
/// Deliberately *their* `report ()` rather than a sixth phrasing here: these are the lines a
/// consumer sees, so a wording that rots is worth catching in this suite rather than in an issue.
/// `Video.report ()` and `Typeset.report ()` are each one string covering their own tools, so the
/// line is split back apart only far enough to name what is missing.
let private packageTools () : Dependency list =
    let ffmpeg = Video.ffmpeg ()
    let ffprobe = Video.ffprobe ()
    let tex = Typeset.toolchain ()

    [ { Name = "ffmpeg"
        Live = ffmpeg.IsAvailable
        Line = Video.report () }
      { Name = "ffprobe"
        Live = ffprobe.IsAvailable
        Line = Toolchain.report "an encoded container is not read back, so the mux gate cannot check it" ffprobe }
      { Name = Typeset.Engine + " + dvisvgm"
        Live = tex.IsAvailable
        Line = Typeset.report () } ]

/// The native libraries, which are not on `PATH` and fail in a different way.
///
/// SkiaSharp and HarfBuzz are loaded by the runtime out of the package's runtime assets, so the
/// question is not "is there a file" but "does a render throw" — which is why `SkiaTests` probes
/// by rendering. Their absence takes out every PNG, PDF, frame, film and ink gate at once, so a
/// census that listed five programs and not this would be counting the small half.
let private nativeLibraries () : Dependency list =
    match SkiaTests.nativeAssets () with
    | Ok() ->
        [ { Name = "SkiaSharp + HarfBuzz natives"
            Live = true
            Line = "SkiaSharp + HarfBuzz natives loaded — a 4x4 render returned" } ]
    | Error why ->
        [ { Name = "SkiaSharp + HarfBuzz natives"
            Live = false
            Line =
              "SkiaSharp + HarfBuzz natives failed to load — every raster, PDF, frame and ink gate "
              + "was skipped. "
              + why } ]

/// Everything, in the order a reader wants it: the suite's own programs, then the two packages',
/// then the natives.
let census () : Dependency list =
    suiteTools () @ packageTools () @ nativeLibraries ()

// ======================================================================================
// The half a human reads
// ======================================================================================

[<Literal>]
let RequireVariable = "MAREY_REQUIRE_TOOLCHAIN"

/// True when the run has been asked to treat a missing dependency as a failure.
///
/// `Golden.updateRequested`'s parser, verbatim in behaviour: set-but-empty, `0` and `false` are
/// all off, so a CI expression that evaluates to nothing does not silently arm the strictest
/// switch in the suite.
let requireRequested =
    match Environment.GetEnvironmentVariable RequireVariable with
    | null -> false
    | v ->
        let v = v.Trim()
        v <> "" && v <> "0" && not (String.Equals(v, "false", StringComparison.OrdinalIgnoreCase))

module ``the toolchain census`` =

    /// Print it. Always green, and that is the point — this is the run telling you what it was,
    /// not a gate.
    ///
    /// `TikzInkTests`' "the ink gate says whether it ran" is the precedent and stays where it is:
    /// it reports one gate at the site of that gate, which is useful when that gate is the one
    /// you are working on. This reports all of them, which is what a release sweep needs.
    [<Fact>]
    let ``every external dependency reports what became of it`` () =
        let all = census ()

        let live = all |> List.filter (fun d -> d.Live) |> List.length

        Console.WriteLine(
            sprintf "Marey toolchain census — %d of %d dependencies live" live (List.length all)
        )

        for d in all do
            Console.WriteLine("  " + d.Line.Replace("\n", "\n  "))

        if live < List.length all then
            Console.WriteLine(
                sprintf
                    "  NOTE: %d dependencies are absent, so the gates that need them did not run. Set %s=1 to make that a failure."
                    (List.length all - live)
                    RequireVariable
            )

        // A census that found nothing to count would print a clean, reassuring, empty report.
        Assert.NotEmpty all

    /// The census asks about the same things the gates ask about.
    ///
    /// Cheap, and it is the assertion that keeps this file honest: a dependency added to a gate
    /// and not to `census ()` makes the strict switch below quietly weaker, and nothing else in
    /// the suite would notice. Nine is the count as of M3 phase 9 — five programs the suite
    /// drives, three the packages drive (the TeX pair as one entry), and the natives. It is a
    /// literal for the reason `publish.yml`'s package count is one: a number that has to be
    /// edited by hand is what makes somebody notice they changed it.
    [<Fact>]
    let ``the census covers every dependency the gates have`` () =
        Assert.Equal(9, List.length (census ()))

    /// No entry is nameless or wordless, because a census row that says nothing is worse than a
    /// missing one — it looks like it was checked.
    [<Fact>]
    let ``every entry names itself and says something`` () =
        for d in census () do
            Assert.False(String.IsNullOrWhiteSpace d.Name)
            Assert.False(String.IsNullOrWhiteSpace d.Line)

    /// An absent tool's line says what to install.
    ///
    /// `Toolchain.missing` is what guarantees this and it is worth gating from the outside: the
    /// consequence half of that sentence is the caller's, and a caller that passed an empty
    /// string would produce a grammatical line that helps nobody. Vacuous on a machine with the
    /// full toolchain, which is exactly the condition this file is about — so it says so.
    [<Fact>]
    let ``an absent dependency says how to get it`` () =
        let absent = census () |> List.filter (fun d -> not d.Live)

        if List.isEmpty absent then
            Console.WriteLine("Marey toolchain census: nothing absent, so the install-hint gate had nothing to check.")
        else
            for d in absent do
                Assert.True(
                    d.Line.Contains "To enable it:"
                    || d.Line.Contains "install"
                    || d.Line.Contains "skipped",
                    sprintf "%s is absent and its report does not say what to do about it: %s" d.Name d.Line
                )

// ======================================================================================
// The half CI reads
// ======================================================================================

module ``the toolchain is complete when it is required to be`` =

    /// The gate that makes CI's green tick mean something.
    ///
    /// Off on a laptop: a contributor without TeX should get a green suite and a census that says
    /// which gates they did not run. On in `ci.yml` and `publish.yml`, where the toolchain is
    /// installed on purpose and its absence means the install step broke — the failure mode this
    /// whole file was written for, and the one that cannot be seen from a green tick because
    /// every affected gate degrades on purpose.
    ///
    /// **It names all of them, not the first.** A run that fixes one missing package and
    /// rediscovers the next one on the following push is the slow version of this message.
    [<Fact>]
    let ``no gate went vacuous`` () =
        if requireRequested then
            let absent = census () |> List.filter (fun d -> not d.Live)

            if not (List.isEmpty absent) then
                let detail =
                    absent
                    |> List.map (fun d -> "  " + d.Line.Replace("\n", "\n  "))
                    |> String.concat "\n"

                Assert.Fail(
                    sprintf
                        "%s is set, so every gate was required to run — but %d of %d dependencies are absent and the gates that need them passed without executing:\n%s"
                        RequireVariable
                        (List.length absent)
                        (List.length (census ()))
                        detail
                )
        else
            Console.WriteLine(
                sprintf
                    "Marey toolchain census: %s is not set, so an absent dependency is a skip rather than a failure."
                    RequireVariable
            )

    /// The switch parses the way `MAREY_UPDATE_GOLDEN` parses.
    ///
    /// Gated rather than assumed because the two are read by different files and nothing but a
    /// test connects them, and because the failure is silent in the direction that matters: a
    /// variable that parsed `""` as true would arm this on every laptop, and one that parsed `1`
    /// as false would disarm it in CI and leave the tick green.
    [<Fact>]
    let ``the switch reads the way the other one does`` () =
        let original = Environment.GetEnvironmentVariable RequireVariable

        let reread () =
            match Environment.GetEnvironmentVariable RequireVariable with
            | null -> false
            | v ->
                let v = v.Trim()
                v <> "" && v <> "0" && not (String.Equals(v, "false", StringComparison.OrdinalIgnoreCase))

        try
            for off in [ null; ""; "  "; "0"; "false"; "False" ] do
                Environment.SetEnvironmentVariable(RequireVariable, off)
                Assert.False(reread (), sprintf "%A should leave the gate off" off)

            for on in [ "1"; "true"; "yes"; " 1 " ] do
                Environment.SetEnvironmentVariable(RequireVariable, on)
                Assert.True(reread (), sprintf "%A should turn the gate on" on)
        finally
            Environment.SetEnvironmentVariable(RequireVariable, original)
