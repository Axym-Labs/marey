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
// Gate 2, .NET: the format's goldens
// ======================================================================================

/// Every golden scene's bytes, committed under `golden/wire/`. Node writes them too and checks
/// them against the same files (`tests/Marey.Fable`), so the two runtimes agree about padding,
/// strings and float bits, and any change to the encoding shows as a changed baseline: the version
/// discipline, made mechanical.
module ``the format's goldens`` =

    let goldens: obj[] seq = identity.goldens

    [<Theory>]
    [<MemberData(nameof goldens)>]
    let ``every golden scene encodes to its committed bytes`` (name: string) =
        Golden.checkBytes ("wire/" + name + ".mrys") (Wire.encode (golden name))

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


// ======================================================================================
// Gate 5: the text metrics travel with the scene (D156)
// ======================================================================================

/// A canvas that records what anchors text: the transform, the font and each `fillText`. Enough of
/// a canvas for `CanvasBackend` to run on .NET with WebGL off, a pixel ratio given and its scratch
/// layers made here.
type private TextRecorder(log: ResizeArray<string>) =
    let mutable fillStyle: obj = null
    let mutable strokeStyle: obj = null
    let mutable font = ""
    let mutable width = 300.0
    let mutable height = 150.0
    let num (x: float) = x.ToString("R", Globalization.CultureInfo.InvariantCulture)

    member this.Layer (_: int) (_: int) : CanvasContext = TextRecorder(log) :> CanvasContext

    interface CanvasElement with
        member _.width with get () = width and set v = width <- v
        member _.height with get () = height and set v = height <- v
        member this.getContext(_: string) = this :> CanvasContext

    interface CanvasContext with
        member this.canvas = this :> CanvasElement
        member _.fillStyle with get () = fillStyle and set v = fillStyle <- v
        member _.strokeStyle with get () = strokeStyle and set v = strokeStyle <- v
        member val lineWidth = 1.0 with get, set
        member val lineCap = "butt" with get, set
        member val lineJoin = "miter" with get, set
        member val miterLimit = 10.0 with get, set
        member val lineDashOffset = 0.0 with get, set
        member _.font with get () = font and set v = font <- v
        member val globalAlpha = 1.0 with get, set
        member val imageSmoothingEnabled = true with get, set
        member _.setLineDash _ = ()
        member _.save() = log.Add "save"
        member _.restore() = log.Add "restore"
        member _.setTransform(a, b, c, d, e, f) = log.Add(sprintf "setTransform %s %s %s %s %s %s" (num a) (num b) (num c) (num d) (num e) (num f))
        member _.transform(a, b, c, d, e, f) = log.Add(sprintf "transform %s %s %s %s %s %s" (num a) (num b) (num c) (num d) (num e) (num f))
        member _.translate(x, y) = log.Add(sprintf "translate %s %s" (num x) (num y))
        member _.rotate a = log.Add(sprintf "rotate %s" (num a))
        member _.clearRect(_, _, _, _) = ()
        member _.fillRect(_, _, _, _) = ()
        member _.beginPath() = ()
        member _.moveTo(_, _) = ()
        member _.lineTo(_, _) = ()
        member _.bezierCurveTo(_, _, _, _, _, _) = ()
        member _.arc(_, _, _, _, _) = ()
        member _.closePath() = ()
        member _.fill _ = ()
        member _.stroke() = ()
        member _.clip _ = ()
        member _.fillText(text, x, y) = log.Add(sprintf "fillText %s at %s %s in %s" text (num x) (num y) font)
        member _.measureText _ = null
        member _.createLinearGradient(_, _, _, _) = { new CanvasGradient with member _.addColorStop(_, _) = () }
        member _.createRadialGradient(_, _, _, _, _, _) = { new CanvasGradient with member _.addColorStop(_, _) = () }
        member _.createImageData(w, h) = let d = Array.zeroCreate<byte> (int w * int h * 4) in { new CanvasImageData with member _.data = d }
        member _.putImageData(_, _, _) = ()
        member _.drawImage(_, _, _, _, _) = ()

let private textLog (m: ITextMeasurer) (extent: Extent<px>) (scene: Node) : string list =
    let log = ResizeArray<string>()
    let root = TextRecorder(log)
    CanvasBackend.Draw(root :> CanvasContext, extent, scene, measurer = m, pixelRatio = 2.0, layer = root.Layer, webgl = false)
    List.ofSeq log

module ``text metrics`` =

    let private fontFile =
        lazy (IO.Path.Combine(Golden.projectDir.Force(), "fonts", "LiberationSans-Regular.ttf"))

    /// Every golden figure laid out against Skia's measurer, which a page has no copy of, drawn on
    /// a canvas through the table its bytes carry: the same text, at the same places, as drawing
    /// with Skia itself. And not vacuously: the default measurer would have put some of it
    /// elsewhere.
    [<Fact>]
    let ``a scene laid out with Skia draws on the canvas with the kernel's anchors`` () =
        SkiaTests.withSkia (fun () ->
            use skia = new SkiaTextMeasurer(fontFile = fontFile.Force())
            let m = skia :> ITextMeasurer
            let mutable approxDiffers = 0

            for name, extent, scene in GoldenScenes.scenesWith m do
                let expected = textLog m extent scene
                let decoded, table = ok (Wire.decodeWith (Wire.encodeWith m scene))
                Assert.True((decoded = scene), sprintf "%s did not survive the wire" name)
                Assert.True((textLog table extent decoded = expected), sprintf "%s: the canvas anchored its text elsewhere than Skia did" name)

                if textLog TextMeasure.approx extent decoded <> expected then
                    approxDiffers <- approxDiffers + 1

            Assert.True(approxDiffers >= 10, sprintf "only %d scenes would have drawn differently without the table" approxDiffers))

    /// One entry per distinct font and string, in the order the scene first uses them; the scene's
    /// bytes without metrics are unchanged by carrying them.
    [<Fact>]
    let ``the table holds each run once, and leaves the scene's own bytes alone`` () =
        let scene = golden "figure"
        let _, table = ok (Wire.decodeWith (Wire.encodeWith TextMeasure.approx scene))
        let keys = table.Entries |> Array.map (fun (f, t, _) -> f, t)
        Assert.Equal(keys.Length, (Array.distinct keys).Length)
        Assert.NotEmpty keys

        for (f, t, metrics) in table.Entries do
            Assert.Equal(TextMeasure.approx.Measure f t, metrics)

        let _, none = ok (Wire.decodeWith (Wire.encode scene))
        Assert.Empty none.Entries

    /// A patch carries the metrics of what it replaces, and a page merges them over what it holds.
    [<Fact>]
    let ``a patch's table merges over the one the page holds`` () =
        let font: FontSpec = { Family = "sans-serif"; Size = 9.0<pt>; Weight = 400; Italic = false }
        let at (a: float) : TextMetrics = { Advance = a * 1.0<px>; Ascent = 9.0<px>; Descent = 2.0<px> }
        let held = TextTable([| font, "a", at 1.0; font, "b", at 2.0 |])
        let arriving = TextTable([| font, "b", at 5.0; font, "c", at 3.0 |])
        let merged = held.Merge arriving :> ITextMeasurer
        Assert.Equal(at 1.0, merged.Measure font "a")
        Assert.Equal(at 5.0, merged.Measure font "b")
        Assert.Equal(at 3.0, merged.Measure font "c")
        Assert.Equal(TextMeasure.approx.Measure font "d", merged.Measure font "d")

// ======================================================================================
// Phase 4: the page's script (D157)
// ======================================================================================

module ``the page's script`` =

    let private committed =
        lazy (IO.File.ReadAllText(IO.Path.Combine(Golden.repoRoot.Force(), "src", "Marey.Html", "view", "marey-view.js")))

    /// What `Marey.Html` carries is the file CI rebuilds and compares (gate 7).
    [<Fact>]
    let ``Marey.Html carries the committed script`` () =
        Assert.Equal(committed.Force(), Html.viewScript.Force())

    /// It defines one global with the two calls the widget makes.
    [<Fact>]
    let ``it defines MareyView with show and patch`` () =
        let s = Html.viewScript.Force()
        Assert.Contains("var MareyView=", s)
        Assert.Matches(@"[{,]show:", s)
        Assert.Matches(@"[{,]patch:", s)

    /// It goes inline into a page that is HTML and well-formed XML at once, inside a CDATA section,
    /// so it may contain neither the end of a script element nor the end of a CDATA section.
    [<Fact>]
    let ``it can be inlined`` () =
        let s = Html.viewScript.Force()
        Assert.DoesNotContain("</script", s.ToLowerInvariant())
        Assert.DoesNotContain("]]>", s)

// ======================================================================================
// Gate 9: below the threshold, the SVG path is as 0.6.0 drew it
// ======================================================================================

/// Three live figures under the canvas threshold, each written out as everything the host bridge
/// gives Verso: the live and the static page, the reply after a resize, and the answers to a
/// slider, a zoom and a hover. Recorded before phase 5 touched `Html.fs`, so a change to the SVG
/// path shows as a changed baseline.
module ``the SVG path below the threshold`` =

    let private slider () =
        interact {
            let! f = Control.slider Derived.hertz "f" 0.5<Hz> 5.0<Hz> 1.0<Hz>
            return Chart.line U.second U.metre [| 0.0<s>; 1.0<s> |] [| 0.0<m>; float f * 1.0<m> |]
        }

    let private time () =
        interact {
            let! t = Control.time "t" 0.0<s> 10.0<s>
            let ts = Array.init 50 (fun i -> float i * 0.2<s>)
            return Chart.line U.second U.metre ts (ts |> Array.map (fun s -> sin (float (s - t)) * 1.0<m>))
        }

    let private cloud () =
        interact {
            let! k = Control.slider U.one "spread" 0.1 2.0 1.0
            let rng = Random(7)
            let x = Array.init 2000 (fun _ -> rng.NextDouble() * 1.0<m>)
            let y = Array.init 2000 (fun _ -> (rng.NextDouble() - 0.5) * k * 1.0<V>)
            return Chart.scatter U.metre Derived.volt x y
        }

    let private transcript (value: obj) =
        let s0 = Html.liveInitForHost value
        let step (state: obj) (kind: string) (payload: string) = Html.liveStepForHost (value, state, kind, payload)
        let sized = step s0 "size" "[640,400]"
        let set = step sized "set" "[0,1.5]"
        let zoomed = step set "zoom" "[150,100,400,300]"
        let hovered = step zoomed "hover" "[300,200]"

        String.concat
            "\n----\n"
            [ Html.liveDocumentForHost (value, s0, true)
              Html.liveDocumentForHost (value, s0, false)
              String.concat "\n" (Html.liveReplyForHost (value, sized))
              String.concat "\n" (Html.liveAnswerForHost (value, sized, set))
              String.concat "\n" (Html.liveAnswerForHost (value, set, zoomed))
              String.concat "\n" (Html.liveAnswerForHost (value, zoomed, hovered))
              String.concat "\n" (Html.liveReadoutForHost (value, hovered)) ]

    [<Fact>]
    let ``a slider figure`` () = Golden.check "widget/slider.txt" (transcript (box (slider ())))

    [<Fact>]
    let ``a time control`` () = Golden.check "widget/time.txt" (transcript (box (time ())))

    [<Fact>]
    let ``two thousand points`` () = Golden.check "widget/cloud-2000.txt" (transcript (box (cloud ())))
