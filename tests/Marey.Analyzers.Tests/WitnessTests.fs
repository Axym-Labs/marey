/// Each finding on a call that does not compile, its fixes applied, and the result checked: the
/// fix is right when the compiler accepts what it wrote.
module Marey.Analyzers.Tests.WitnessTests

open Xunit
open Marey.Witnesses
open Marey.Analyzers.Tests.Scripts

let private data =
    "let t = Array.init 5 (fun i -> float i * 0.1<s>)\n"
    + "let x = t |> Array.map (fun t -> 0.5 * 9.81<m/s^2> * t * t)\n"

/// The one finding on a body, its code checked.
let private single (code: string) (body: string) =
    let c, fs = findings body
    Assert.False(c.Errors.Length = 0, "the body compiles, so it should have no finding")

    match fs with
    | [ f ] ->
        Assert.Equal(code, f.Code)
        c, f
    | fs -> failwithf "expected one finding, got %A" fs

/// The fix applied, and the script it makes checked.
let private fixedBy (c: Checked) (fix: Fix) =
    let after = checkText (apply c.Text fix)
    Assert.True(after.Errors.Length = 0, sprintf "the fix \"%s\" leaves errors:\n%s\n%s" fix.Title (errors after) (body after.Text))
    body after.Text

[<Fact>]
let ``the witnesses a call is missing are inserted`` () =
    let c, f = single "MAREY001" (data + "let fig = Chart.lineIn t x\n")
    Assert.Equal(data + "let fig = Chart.lineIn U.second U.metre t x\n", fixedBy c f.Fixes.Head)

[<Fact>]
let ``a witness given is kept and the one missing inserted`` () =
    let c, f = single "MAREY001" (data + "let fig = Chart.lineIn U.second t x\n")
    Assert.Equal(data + "let fig = Chart.lineIn U.second U.metre t x\n", fixedBy c f.Fixes.Head)

[<Fact>]
let ``a witness for another unit is replaced`` () =
    let c, f = single "MAREY002" (data + "let fig = Chart.lineIn U.metre U.metre t x\n")
    Assert.Equal(data + "let fig = Chart.lineIn U.second U.metre t x\n", fixedBy c f.Fixes.Head)

[<Fact>]
let ``raw values where carried ones are taken: the witness form or carrying values`` () =
    let c, f = single "MAREY003" (data + "let fig = Chart.line t x |> Chart.withTitle \"Free fall\"\n")

    match f.Fixes with
    | [ form; carrying ] ->
        Assert.Equal(data + "let fig = Chart.lineIn U.second U.metre t x |> Chart.withTitle \"Free fall\"\n", fixedBy c form)
        Assert.Equal(data + "let fig = Chart.line (U.second.Of t) (U.metre.Of x) |> Chart.withTitle \"Free fall\"\n", fixedBy c carrying)
    | fixes -> failwithf "expected two fixes, got %A" fixes

[<Fact>]
let ``one raw argument beside a carried one`` () =
    let c, f = single "MAREY003" (data + "let fig = Chart.line (U.second.Of t) x\n")
    let carrying = f.Fixes |> List.last
    Assert.Equal(data + "let fig = Chart.line (U.second.Of t) (U.metre.Of x)\n", fixedBy c carrying)

[<Fact>]
let ``a record field is read for its unit`` () =
    let body =
        "type Run = { Times: float<s>[]; Heights: float<m>[] }\n"
        + "let run = { Times = [| 0.0<s>; 1.0<s> |]; Heights = [| 0.0<m>; 2.0<m> |] }\n"
        + "let fig = Chart.scatterIn run.Times run.Heights\n"

    let c, f = single "MAREY001" body
    Assert.Contains("Chart.scatterIn U.second U.metre run.Times run.Heights", fixedBy c f.Fixes.Head)

[<Fact>]
let ``a call across lines keeps its layout`` () =
    let c, f = single "MAREY001" (data + "let fig =\n    Chart.lineIn\n        t\n        x\n")
    Assert.Equal(data + "let fig =\n    Chart.lineIn\n        U.second U.metre t\n        x\n", fixedBy c f.Fixes.Head)

/// Measures as written, the witness each gets, and the fix's script compiled: a witness is
/// right when the compiler takes it for the measure.
[<Fact>]
let ``each measure gets a witness the compiler takes for it`` () =
    let cases =
        [ "s", "U.second"
          "kg", "U.kilogram"
          "m/s", "U.metrePerSecond"
          "m/s^2", "U.metrePerSecondSquared"
          "1/s", "U.perSecond"
          "Hz", "U.hertz"
          "N", "U.newton"
          "m^2", "U.squareMetre"
          "eV", "U.electronvolt"
          "Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols.s", "U.second"
          "kg m/s^2", "(U.div (U.mul U.kilogram U.metre) (U.square U.second))"
          "V/m", "(U.div U.volt U.metre)"
          "N m", "(U.mul U.newton U.metre)"
          "m^4", "(U.mul (U.mul (U.mul U.metre U.metre) U.metre) U.metre)" ]

    let line (i: int) (m: string) =
        sprintf "let v%d = [| 1.0<%s> |]\nlet f%d = Chart.lineIn v%d v%d\n" i m i i i

    let body = cases |> List.mapi (fun i (m, _) -> line i m) |> String.concat ""
    let c, fs = findings body
    Assert.Equal(cases.Length, fs.Length)

    // Every fix at once: they touch separate calls.
    let all =
        { Title = "all"
          Edits = fs |> List.collect (fun f -> f.Fixes.Head.Edits) }

    let after = fixedBy c all

    cases
    |> List.iteri (fun i (_, w) -> Assert.Contains(sprintf "let f%d = Chart.lineIn %s %s v%d v%d" i w w i i, after))

[<Fact>]
let ``plain values get the unspecified witness`` () =
    let c, f = single "MAREY003" "let a = [| 1.0; 2.0 |]\nlet fig = Chart.scatter a a\n"
    Assert.Contains("Chart.scatterIn U.unspecified U.unspecified a a", fixedBy c f.Fixes.Head)

[<Fact>]
let ``a script that compiles has no finding`` () =
    let body =
        data
        + "let a = Chart.lineIn U.second U.metre t x\n"
        + "let b = Chart.line (U.second.Of t) (U.metre.Of x)\n"
        + "let q = U.second.Of t\n"
        + "let c = Chart.scatter q (0.5 * U.metrePerSecondSquared.Of 9.81<m/s^2> * q * q)\n"
        + "let d = Chart.withTitle \"t\" a\n"

    let c, fs = findings body
    Assert.True(c.Errors.Length = 0, errors c)
    Assert.Empty fs

[<Fact>]
let ``an argument of another shape is not read, and no fix is guessed`` () =
    let c, fs = findings (data + "let fig = Chart.lineIn (t |> Array.map id) x\n")
    Assert.False(c.Errors.Length = 0)
    Assert.Empty fs

[<Fact>]
let ``collapsed, a fix's edits are one replacement with the same result`` () =
    let c, f = single "MAREY003" (data + "let fig = Chart.line t x\n")

    for fix in f.Fixes do
        let one = Findings.collapse c.Source fix
        Assert.Equal(apply c.Text fix, apply c.Text { Title = ""; Edits = [ one ] })

[<Fact>]
let ``the messages name the call as written and the units as a reader writes them`` () =
    let _, missing = single "MAREY001" (data + "let fig = Chart.lineIn t x\n")
    Assert.Equal("Chart.lineIn takes the witnesses of its units before the values: U.second U.metre.", missing.Message)
    let _, wrong = single "MAREY002" (data + "let fig = Chart.lineIn U.metre U.metre t x\n")
    Assert.Equal("Chart.lineIn is given the witness of m for values in s, whose witness is U.second.", wrong.Message)
    let _, raw = single "MAREY003" (data + "let fig = Chart.line t x\n")
    Assert.Equal("Chart.line takes values that carry their witness (a Q), and is given values without one: t in s, x in m.", raw.Message)
