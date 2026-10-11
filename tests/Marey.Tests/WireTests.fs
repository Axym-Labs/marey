module Marey.Tests.WireTests

// Step F, phase 1 — the codec (D153–D155). Gate 1's .NET half (decode (encode n) = n) and gate 4
// (a newer version refused by name, an unknown block read past) of `wire-plan.md` §4.

open System
open Xunit
open Marey
open Marey.UnitsOfMeasure

let private ok (r: Result<'a, string>) =
    match r with
    | Ok v -> v
    | Error why -> failwithf "expected a value, got: %s" why

let private failure (r: Result<'a, string>) =
    match r with
    | Ok _ -> failwith "expected an error, got a value"
    | Error why -> why

let private golden (name: string) =
    GoldenScenes.scenesWith TextMeasure.approx
    |> List.find (fun (n, _, _) -> n = name)
    |> fun (_, _, node) -> node

let private header (version: int) (body: int) (blocks: byte[] list) =
    [ yield! [| 0x4Duy; 0x52uy; 0x59uy; 0x53uy |]
      yield byte (version &&& 0xFF)
      yield byte (version >>> 8)
      yield byte body
      yield 0uy
      yield byte blocks.Length
      for b in blocks do
          yield! b ]

// ======================================================================================
// Gate 1, .NET: decode (encode n) = n
// ======================================================================================

module ``identity`` =

    /// Every golden scene: the hand-built ones and the figures compiled with the default measurer.
    let goldens: obj[] seq =
        GoldenScenes.scenesWith TextMeasure.approx |> Seq.map (fun (name, _, _) -> [| box name |])

    [<Theory>]
    [<MemberData(nameof goldens)>]
    let ``every golden scene comes back as itself`` (name: string) =
        let n = golden name
        Assert.True(ok (Wire.decode (Wire.encode n)) = n, sprintf "%s did not survive the wire" name)

    /// A real patch, from the differ, and the whole-scene form.
    [<Fact>]
    let ``a patch comes back as itself`` () =
        let surface = Chart.size 480.0 300.0
        let m = TextMeasure.approx
        let t = Array.init 40 (fun i -> float i * 0.05<s>)
        let x (k: float) = t |> Array.map (fun t -> k * float t * float t * 1.0<m>)
        let fig k = Chart.line U.second U.metre t (x 1.0) |> Chart.addScatter U.second U.metre t (x k)
        let shot = Chart.arrangeOver m surface [| fig 1.0; fig 0.9 |]
        let a, b = Chart.toSceneIn m shot (fig 1.0), Chart.toSceneIn m shot (fig 0.9)
        let p = Scene.diff a b

        match p with
        | Replace parts -> Assert.NotEmpty parts
        | Whole _ -> failwith "the fixture should give a replacement"

        Assert.True(ok (Wire.decodePatch (Wire.encodePatch p)) = p)
        Assert.True(ok (Wire.decodePatch (Wire.encodePatch (Whole b))) = Whole b)
        Assert.True(Scene.apply (ok (Wire.decodePatch (Wire.encodePatch p))) a = b)

    /// The values `=` cannot vouch for: NaN is not equal to itself, and −0 equals 0.
    [<Fact>]
    let ``awkward floats keep their bits, and strings their code points`` () =
        let bits (x: float) = BitConverter.DoubleToInt64Bits x
        let values = [| nan; -0.0; Double.Epsilon; 1e-310; Double.MaxValue; infinity; -infinity; 0.1 |]

        let scene =
            Group(
                values
                |> Array.map (fun v -> Opacity(v, Scene.empty))
            )

        match ok (Wire.decode (Wire.encode scene)) with
        | Group back ->
            let got = back |> Array.map (function Opacity(v, _) -> bits v | _ -> 0L)
            Assert.Equal<int64[]>(values |> Array.map bits, got)
        | _ -> failwith "not a group"

        let text = "ΔE · µs⁻¹ 漢字 \U0001F600"
        let run = Keyed(Key text, Scene.empty)
        Assert.True(ok (Wire.decode (Wire.encode run)) = run)

    /// D153: a page reads a batch's points through a `Float64Array` view, which needs them at a
    /// multiple of 8 from the start of the buffer, whatever precedes them.
    [<Fact>]
    let ``a batch's points start on a multiple of eight`` () =
        let marker = 1234.5678

        for prefix in 0..9 do
            let batch =
                Markers
                    { Points = [| { X = marker * 1.0<nd>; Y = 0.0<nd> } |]
                      Shape = MarkCircle
                      Size = 3.0<px>
                      Fill = None
                      Stroke = None }

            let scene = Keyed(Key(String('k', prefix)), batch)
            let bytes = Wire.encode scene
            let needle = BitConverter.GetBytes marker
            let at = Seq.windowed 8 bytes |> Seq.findIndex (fun w -> w = needle)
            Assert.Equal(0, at % 8)
            Assert.True(ok (Wire.decode bytes) = scene)

// ======================================================================================
// Gate 4: versions and blocks
// ======================================================================================

module ``versions and blocks`` =

    [<Fact>]
    let ``a newer version is refused, naming both`` () =
        let bytes = Wire.encode (golden "paths")
        bytes.[4] <- 2uy
        let why = failure (Wire.decode bytes)
        Assert.Contains("format version 2", why)
        Assert.Contains("reads 1 to 1", why)

    [<Fact>]
    let ``an unknown block after a node's tag is read past`` () =
        // A group of no children, with a three-byte block on its tag.
        let bytes = Array.ofList (header 1 0 [] @ [ 0x84uy; 3uy; 7uy; 8uy; 9uy; 0uy ])
        Assert.True(ok (Wire.decode bytes) = Group [||])

    [<Fact>]
    let ``an unknown header block is read past`` () =
        let bytes = Array.ofList (header 1 0 [ [| 42uy; 2uy; 1uy; 2uy |] ] @ [ 0x04uy; 0uy ])
        Assert.True(ok (Wire.decode bytes) = Group [||])

    [<Fact>]
    let ``what is not a scene says what it is`` () =
        Assert.Contains("MRYS", failure (Wire.decode [| 1uy; 2uy; 3uy; 4uy; 5uy; 6uy; 7uy; 8uy |]))

        let whole = Wire.encode (golden "markers-batch")
        Assert.Contains("the bytes end", failure (Wire.decode (Array.sub whole 0 (whole.Length - 5))))
        Assert.Contains("after the end", failure (Wire.decode (Array.append whole [| 0uy |])))
        Assert.Contains("where a patch was expected", failure (Wire.decodePatch whole))

    [<Fact>]
    let ``a raster stored in a form this version does not know is refused by name`` () =
        let image = { Width = 1; Height = 1; Rgba = [| 1uy; 2uy; 3uy; 4uy |] }
        let scene = Raster { Image = image; Bounds = { X0 = 0.0<nd>; Y0 = 0.0<nd>; X1 = 1.0<nd>; Y1 = 1.0<nd> }; Smooth = true }
        let bytes = Wire.encode scene
        // Header (9 bytes), the tag, width, height: the storage byte is the twelfth.
        Assert.Equal(0uy, bytes.[12])
        bytes.[12] <- 1uy
        Assert.Contains("form 1", failure (Wire.decode bytes))

