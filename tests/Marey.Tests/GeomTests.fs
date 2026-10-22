/// M2 phase 6 — the rest of §9's geom list, and the two claims that make it a phase rather
/// than a pile of `Path` builders.
///
/// **The measures are spent in the constructor.** Every geom below is built from a
/// `Series<'ux,'uy>` and arrays in that series' own units, so a ribbon in seconds around a
/// displacement, a tile whose width is given in kelvin, or a box plot of metres against a joule
/// axis are all compile errors rather than pictures. That is `Geom.errorY`'s guarantee from M0
/// applied seven more times, and it is asserted here the only way a measure can be — by
/// *compiling* a binding whose annotation is the claim.
///
/// **Nothing new reached the IR.** §5.5 says axes, legends, error bars and box plots are
/// "compiled into `Path`/`Text`/`Markers` by the layer above", and a box plot is the one it
/// names that had never been built. It is three nodes, and no `Node` case was added.
///
/// The third thing in this file is a regression, and it is the reason the phase touched
/// `compileWith`'s signature. A geom carrying a per-point array of its own indexes it *by row*,
/// while a channel batch hands the compiler a renumbered subset of the rows — so before
/// this phase, the second colour batch of an error bar read the first batch's spans. Every bar
/// was still drawn and the figure still looked reasonable, which is why it needed a test rather
/// than a reading.
module Marey.Tests.GeomTests

open Xunit
open Marey
open Marey.UnitsOfMeasure
open Marey.Tests.Generators

// ======================================================================================
// Fixtures
// ======================================================================================

let private surface: Extent<px> = { Width = 320.0<px>; Height = 200.0<px> }

let private style = Geom.styleFor Theme.initial 0

let private pt (x: float) (y: float) : Vec2<nd> = { X = x * 1.0<nd>; Y = y * 1.0<nd> }

let private nd (x: float) : float<nd> = x * 1.0<nd>

let private compile (g: GeomOf<nd>) (pts: Vec2<nd>[]) =
    Geom.compile Coord.cartesian surface style g pts

/// Every point a node's **paths** pass through, in order. The cross-backend oracle is about
/// path coordinates, and a `Markers` batch or a `Text` run reaches the backends through code
/// the marker and text goldens already pin.
let rec private pathPoints (n: Node) : Vec2<nd> list =
    match n with
    | Path(segs, _, _, _) ->
        [ for s in segs do
              match s with
              | MoveTo p
              | LineTo p -> yield p
              | CubicTo(a, b, c) ->
                  yield a
                  yield b
                  yield c
              | ClosePath -> () ]
    | Group ns -> ns |> Array.toList |> List.collect pathPoints
    | Transform(_, c)
    | Clip(_, c)
    | Opacity(_, c)
    | Keyed(_, c)
    | Annotated(_, c) -> pathPoints c
    | Text _
    | Markers _
    | Raster _ -> []

/// The subpaths of a node's first path, as vertex lists. One entry per `MoveTo`.
let rec private subpaths (n: Node) : Vec2<nd> list list =
    match n with
    | Path(segs, _, _, _) ->
        let out = ResizeArray<ResizeArray<Vec2<nd>>>()

        for s in segs do
            match s with
            | MoveTo p ->
                let run = ResizeArray<Vec2<nd>>()
                run.Add p
                out.Add run
            | LineTo p -> if out.Count > 0 then out.[out.Count - 1].Add p
            | CubicTo(_, _, p) -> if out.Count > 0 then out.[out.Count - 1].Add p
            | ClosePath -> ()

        out |> Seq.map List.ofSeq |> List.ofSeq
    | Group ns -> ns |> Array.toList |> List.collect subpaths
    | Transform(_, c)
    | Clip(_, c)
    | Keyed(_, c)
    | Annotated(_, c) -> subpaths c
    | _ -> []

let rec private textRuns (n: Node) : TextRun list =
    match n with
    | Text t -> [ t ]
    | Group ns -> ns |> Array.toList |> List.collect textRuns
    | Transform(_, c)
    | Clip(_, c)
    | Keyed(_, c)
    | Annotated(_, c) -> textRuns c
    | _ -> []

let rec private markerBatches (n: Node) : MarkerBatch list =
    match n with
    | Markers b -> [ b ]
    | Group ns -> ns |> Array.toList |> List.collect markerBatches
    | Transform(_, c)
    | Clip(_, c)
    | Keyed(_, c)
    | Annotated(_, c) -> markerBatches c
    | _ -> []

let private xs (ps: Vec2<nd> list) = ps |> List.map (fun p -> float p.X)
let private ys (ps: Vec2<nd> list) = ps |> List.map (fun p -> float p.Y)

/// A unit-square series, so a projected coordinate *is* the data value and a failure reads as
/// arithmetic rather than as scale trivia.
let private unitSeries (name: string) (x: float[]) (y: float[]) : Series<1, 1> =
    { Name = name
      X = x
      Y = y
      XScale = Scale.linear U.one (0.0, 1.0)
      YScale = Scale.linear U.one (0.0, 1.0) }

// ======================================================================================
// Stat.summary and Stat.groupBy — the numbers a box plot and a violin are made of
// ======================================================================================

module ``the five-number summary`` =

    /// Type-7, which is what R's `quantile`, NumPy's default and every published quartile table
    /// agree on. For 1…9 the quartiles land exactly on order statistics, so this is a closed
    /// form and not a tolerance.
    [<Fact>]
    let ``the quartiles of one to nine are three, five and seven`` () =
        let s = Stat.summary U.one [| 1.0 .. 9.0 |]

        Assert.Equal(3.0, float s.Q1, 12)
        Assert.Equal(5.0, float s.Median, 12)
        Assert.Equal(7.0, float s.Q3, 12)
        Assert.Equal(4.0, float s.Iqr, 12)

    [<Fact>]
    let ``interpolation between order statistics is linear`` () =
        // n = 4, p = 0.25 -> h = 0.75, so the answer is three quarters of the way from 1 to 2.
        Assert.Equal(1.75, float (Stat.quantile 0.25 [| 1.0; 2.0; 3.0; 4.0 |]), 12)

    /// The whisker ends are **observations**, so the figure never draws a line reaching a value
    /// nothing in the sample attained — and everything past them is enumerated rather than
    /// absorbed.
    [<Fact>]
    let ``a Tukey whisker stops at the last datum inside the fence`` () =
        let s = Stat.summary U.one (Array.append [| 1.0 .. 9.0 |] [| 100.0 |])

        // q1 = 3.25, q3 = 7.75, IQR = 4.5, fence = 6.75 -> the high fence is 14.5.
        Assert.Equal(3.25, float s.Q1, 12)
        Assert.Equal(7.75, float s.Q3, 12)
        Assert.Equal(1.0, float s.LowerWhisker, 12)
        Assert.Equal(9.0, float s.UpperWhisker, 12)
        Assert.Equal<float[]>([| 100.0 |], s.Outliers)
        // The extremes are still reported; they are simply not what the whisker draws.
        Assert.Equal(100.0, float s.Max, 12)

    /// D52's rule, one layer earlier: a value the stat cannot use is counted, not absorbed.
    [<Fact>]
    let ``non-finite values are dropped and counted`` () =
        let s = Stat.summary U.one [| 1.0; nan; 2.0; infinity; 3.0 |]

        Assert.Equal(3, s.N)
        Assert.Equal(2, s.Dropped)
        Assert.Equal(2.0, float s.Median, 12)

    /// A categorical box plot with one empty level must still draw the other levels, so an
    /// empty sample summarises rather than raising.
    [<Fact>]
    let ``an empty sample summarises to zero`` () =
        let s = Stat.summary U.metre [||]

        Assert.Equal(0, s.N)
        Assert.Equal(0.0, float s.Median, 12)
        Assert.Empty s.Outliers

    /// Tied data has a zero IQR and therefore a zero fence, which is the case that divides by
    /// nothing if the whiskers are computed carelessly.
    [<Fact>]
    let ``a sample with no spread has whiskers on the median`` () =
        let s = Stat.summary U.one (Array.create 7 4.0)

        Assert.Equal(4.0, float s.LowerWhisker, 12)
        Assert.Equal(4.0, float s.UpperWhisker, 12)
        Assert.Empty s.Outliers

    /// **The measure claim**, asserted by compiling it: a summary of metres is entirely in
    /// metres, outliers included. If any of these fields were `float` this file would not build.
    [<Fact>]
    let ``a summary of metres is in metres`` () =
        let s = Stat.summary U.metre [| 1.0<m>; 2.0<m>; 3.0<m>; 40.0<m> |]

        let median: float<m> = s.Median
        let iqr: float<m> = s.Iqr
        let whisker: float<m> = s.UpperWhisker
        let outlier: float<m> = s.Outliers.[0]
        let lo, hi = s.Extent
        let reach: float<m> = hi - lo

        Assert.Equal(2.5, float median, 12)
        Assert.True(float iqr > 0.0)
        Assert.Equal(3.0, float whisker, 12)
        Assert.Equal(40.0, float outlier, 12)
        Assert.Equal(39.0, float reach, 12)

module ``groupBy`` =

    [<Fact>]
    let ``the level order is first appearance, and every row lands`` () =
        let keys = [| "b"; "a"; "b"; "c"; "a" |]
        let values = [| 1.0; 2.0; 3.0; 4.0; 5.0 |]
        let groups = Stat.groupBy keys values

        Assert.Equal<string[]>([| "b"; "a"; "c" |], groups |> Array.map fst)
        Assert.Equal<float[]>([| 1.0; 3.0 |], snd groups.[0])
        Assert.Equal<float[]>([| 2.0; 5.0 |], snd groups.[1])
        Assert.Equal<float[]>([| 4.0 |], snd groups.[2])
        Assert.Equal(values.Length, groups |> Array.sumBy (fun (_, v) -> v.Length))

    /// The same order `Stat.count` and `Levels.ofSeq` give, which is what puts a counted bar and
    /// a box plot of the same keys in the same band.
    [<Fact>]
    let ``the order agrees with count and with Levels`` () =
        let keys = [| "washout"; "control"; "treated"; "control" |]

        let grouped = Stat.groupBy keys (Array.init keys.Length float) |> Array.map fst

        Assert.Equal<string[]>(Stat.count keys |> Array.map fst, grouped)
        Assert.Equal<string[]>((Levels.ofSeq keys).Values, grouped)

    [<Fact>]
    let ``the measure survives the grouping`` () =
        let groups = Stat.groupBy [| "a"; "a" |] [| 3.0<m>; 5.0<m> |]
        let first: float<m> = (snd groups.[0]).[0]
        Assert.Equal(3.0, float first, 12)

    [<Fact>]
    let ``mismatched lengths are refused`` () =
        Assert.Throws<System.ArgumentException>(fun () -> Stat.groupBy [| "a"; "b" |] [| 1.0 |] |> ignore)
        |> ignore

// ======================================================================================
// The geoms, one at a time
// ======================================================================================

module ``step`` =

    let private run (align: StepAlign) =
        compile (GeomStep align) [| pt 0.0 0.0; pt 1.0 1.0 |]
        |> subpaths
        |> List.exactlyOne

    /// The riser is the whole content of the geom, so each case is checked by where it is.
    [<Fact>]
    let ``StepAfter goes across and then up`` () =
        Assert.Equal<float list>([ 0.0; 1.0; 1.0 ], xs (run StepAfter))
        Assert.Equal<float list>([ 0.0; 0.0; 1.0 ], ys (run StepAfter))

    [<Fact>]
    let ``StepBefore goes up and then across`` () =
        Assert.Equal<float list>([ 0.0; 0.0; 1.0 ], xs (run StepBefore))
        Assert.Equal<float list>([ 0.0; 1.0; 1.0 ], ys (run StepBefore))

    [<Fact>]
    let ``StepMid rises halfway between the samples`` () =
        Assert.Equal<float list>([ 0.0; 0.5; 0.5; 1.0 ], xs (run StepMid))
        Assert.Equal<float list>([ 0.0; 0.0; 1.0; 1.0 ], ys (run StepMid))

    /// A gap in the data breaks the staircase, exactly as it breaks a line: drawing through a
    /// NaN asserts a value that was not measured.
    [<Fact>]
    let ``a gap breaks the staircase`` () =
        let node =
            compile (GeomStep StepAfter) [| pt 0.0 0.0; pt 0.25 0.5; pt 0.5 nan; pt 0.75 0.25; pt 1.0 1.0 |]

        Assert.Equal(2, (subpaths node).Length)

module ``ribbon`` =

    let private band =
        let s = unitSeries "" [| 0.0; 0.5; 1.0 |] [| 0.5; 0.5; 0.5 |]
        Geom.ribbon s [| 0.2; 0.3; 0.1 |] [| 0.8; 0.9; 0.7 |] |> Geom.onSeries s

    [<Fact>]
    let ``the band is one closed subpath: the upper bound out, the lower bound back`` () =
        let run = compile band [| pt 0.0 0.5; pt 0.5 0.5; pt 1.0 0.5 |] |> subpaths |> List.exactlyOne

        Assert.Equal<float list>([ 0.0; 0.5; 1.0; 1.0; 0.5; 0.0 ], xs run)
        Assert.Equal<float list>([ 0.8; 0.9; 0.7; 0.1; 0.3; 0.2 ], ys run)

    /// A band is a fill. Stroking it would draw the two bounds as if they had been measured
    /// point by point, which for a smoothed interval they were not.
    [<Fact>]
    let ``a ribbon is filled and not stroked`` () =
        match compile band [| pt 0.0 0.5; pt 0.5 0.5; pt 1.0 0.5 |] with
        | Path(_, stroke, fill, _) ->
            Assert.True(stroke.IsNone)
            Assert.True(fill.IsSome)
        | other -> failwithf "expected a Path, got %A" other

    /// A gap in *either* bound breaks the band, not only a gap in x. A band drawn across a
    /// missing bound asserts a spread nobody measured.
    [<Fact>]
    let ``a missing bound breaks the band`` () =
        let s = unitSeries "" [| 0.0; 0.25; 0.5; 0.75; 1.0 |] (Array.create 5 0.5)
        let g = Geom.ribbon s [| 0.2; 0.2; nan; 0.2; 0.2 |] (Array.create 5 0.8) |> Geom.onSeries s
        let pts = Array.init 5 (fun i -> pt (float i / 4.0) 0.5)

        Assert.Equal(2, (subpaths (compile g pts)).Length)

    /// The signature is the point, as it is for `errorY`: an uncertainty is in the y unit.
    [<Fact>]
    let ``a band around a metre series takes metres`` () =
        let s: Series<s, m> =
            { Name = ""
              X = [| 0.0<s>; 1.0<s> |]
              Y = [| 2.0<m>; 4.0<m> |]
              XScale = Scale.linear U.second (0.0<s>, 1.0<s>)
              YScale = Scale.linear U.metre (0.0<m>, 8.0<m>) }

        match Geom.ribbonAround s [| 1.0<m>; 2.0<m> |] |> Geom.onSeries s with
        | GeomRibbon(low, high) ->
            Assert.Equal(1.0 / 8.0, float low.[0], 12)
            Assert.Equal(6.0 / 8.0, float high.[1], 12)
        | other -> failwithf "expected a GeomRibbon, got %A" other

module ``rect and tile`` =

    [<Fact>]
    let ``a tile is a closed rectangle around its centre`` () =
        let s = unitSeries "" [| 0.5 |] [| 0.5 |]
        let run = compile (Geom.tile 0.2 0.4 s |> Geom.onSeries s) [| pt 0.5 0.5 |] |> subpaths |> List.exactlyOne

        Assert.Equal<float list>([ 0.4; 0.6; 0.6; 0.4 ], xs run)
        Assert.Equal<float list>([ 0.3; 0.3; 0.7; 0.7 ], ys run)

    /// **The decision the geom exists to get right.** A constant width in data units is not a
    /// constant width on a log panel, so the corners are projected one at a time. Converting a
    /// half-width once would make these two tiles identical.
    [<Fact>]
    let ``on a log axis a tile of constant data width narrows to the right`` () =
        let s: Series<1, 1> =
            { Name = ""
              X = [| 2.0; 20.0 |]
              Y = [| 0.5; 0.5 |]
              XScale = Scale.log10 U.one (1.0, 100.0)
              YScale = Scale.linear U.one (0.0, 1.0) }

        let pts = [| pt (float (s.XScale.Forward 2.0)) 0.5; pt (float (s.XScale.Forward 20.0)) 0.5 |]

        let widths =
            compile (Geom.tile 1.0 0.2 s |> Geom.onSeries s) pts
            |> subpaths
            |> List.map (fun run ->
                let vs = xs run
                List.max vs - List.min vs)

        Assert.Equal(2, widths.Length)
        Assert.True(
            widths.[0] > widths.[1] * 1.5,
            sprintf "a log tile should narrow with x; got %f then %f" widths.[0] widths.[1]
        )

    /// Tiles tessellate, so an outline in the *stroke* colour draws a grid nobody asked for —
    /// but no outline at all leaves an antialiasing seam between neighbours that reads as
    /// structure. The outline is therefore the fill's own paint.
    [<Fact>]
    let ``a tile is outlined in its own fill`` () =
        let s = unitSeries "" [| 0.5 |] [| 0.5 |]

        match compile (Geom.tile 0.2 0.2 s |> Geom.onSeries s) [| pt 0.5 0.5 |] with
        | Path(_, Some stroke, Some fill, _) -> Assert.Equal(fill, stroke.Paint)
        | other -> failwithf "expected a stroked and filled Path, got %A" other

    [<Fact>]
    let ``a rectangle takes each measure on its own axis`` () =
        let s: Series<s, m> =
            { Name = ""
              X = [| 1.0<s> |]
              Y = [| 1.0<m> |]
              XScale = Scale.linear U.second (0.0<s>, 4.0<s>)
              YScale = Scale.linear U.metre (0.0<m>, 10.0<m>) }

        match Geom.rect s [| 1.0<s> |] [| 2.0<m> |] [| 3.0<s> |] [| 8.0<m> |] |> Geom.onSeries s with
        | GeomRect(x0, y0, x1, y1) ->
            Assert.Equal(0.25, float x0.[0], 12)
            Assert.Equal(0.2, float y0.[0], 12)
            Assert.Equal(0.75, float x1.[0], 12)
            Assert.Equal(0.8, float y1.[0], 12)
        | other -> failwithf "expected a GeomRect, got %A" other

module ``segment`` =

    [<Fact>]
    let ``each segment is a move and a line`` () =
        let s = unitSeries "" [| 0.0; 0.5 |] [| 0.0; 0.0 |]
        let g = Geom.segment s [| 0.5; 1.0 |] [| 1.0; 0.5 |] |> Geom.onSeries s
        let runs = compile g [| pt 0.0 0.0; pt 0.5 0.0 |] |> subpaths

        Assert.Equal(2, runs.Length)
        Assert.Equal<float list>([ 0.0; 0.5 ], xs runs.[0])
        Assert.Equal<float list>([ 0.0; 1.0 ], ys runs.[0])
        Assert.Equal<float list>([ 0.5; 1.0 ], xs runs.[1])
        Assert.Equal<float list>([ 0.0; 0.5 ], ys runs.[1])

    [<Fact>]
    let ``a segment with a non-finite end is not drawn`` () =
        let s = unitSeries "" [| 0.0; 0.5 |] [| 0.0; 0.0 |]
        let g = Geom.segment s [| 0.5; nan |] [| 1.0; 0.5 |] |> Geom.onSeries s

        Assert.Equal(1, (subpaths (compile g [| pt 0.0 0.0; pt 0.5 0.0 |])).Length)

module ``text`` =

    [<Fact>]
    let ``one run per non-empty label, in the layer's colour and face`` () =
        let runs = compile (Geom.text [| "a"; ""; "c" |]) [| pt 0.0 0.0; pt 0.5 0.5; pt 1.0 1.0 |] |> textRuns

        Assert.Equal(2, runs.Length)
        Assert.Equal<string>("a", runs.[0].Text)
        Assert.Equal<string>("c", runs.[1].Text)
        Assert.Equal(style.Font, runs.[0].Font)
        Assert.Equal(Solid style.Color, runs.[0].Paint)

    /// D12's rule, in the one place phase 6 needed it: the offset is a device length, so it is
    /// divided by the panel's own pixel size before it joins a coordinate about to be scaled by
    /// the panel transform. A 7 px lift is 7/200 nd on this surface.
    [<Fact>]
    let ``the offset is a device length, not a fraction of the panel`` () =
        let run =
            compile (Geom.text [| "x" |]) [| pt 0.5 0.5 |] |> textRuns |> List.exactlyOne

        Assert.Equal(0.5, float run.At.X, 12)
        Assert.Equal(0.5 + 7.0 / 200.0, float run.At.Y, 12)

    [<Fact>]
    let ``a stated placement is used verbatim`` () =
        let g =
            Geom.textAt AnchorEnd BaselineHanging { X = -4.0<px>; Y = 0.0<px> } [| "x" |]

        let run = compile g [| pt 0.5 0.5 |] |> textRuns |> List.exactlyOne

        Assert.Equal(AnchorEnd, run.Anchor)
        Assert.Equal(BaselineHanging, run.Baseline)
        Assert.Equal(0.5 - 4.0 / 320.0, float run.At.X, 12)

    [<Fact>]
    let ``a label at a non-finite point is not drawn`` () =
        let runs = compile (Geom.text [| "a"; "b" |]) [| pt 0.0 nan; pt 1.0 1.0 |] |> textRuns

        Assert.Equal<string list>([ "b" ], runs |> List.map (fun r -> r.Text))

module ``boxplot`` =

    let private summaries =
        [| Stat.summary U.one [| 1.0 .. 9.0 |]
           Stat.summary U.one (Array.append [| 1.0 .. 9.0 |] [| 100.0 |]) |]

    let private series: Series<1, 1> =
        { Name = ""
          X = [| 0.0; 1.0 |]
          Y = [| 5.0; 5.0 |]
          XScale = Scale.ordinal [| "a"; "b" |]
          YScale = Scale.linear U.one (0.0, 100.0) }

    let private node =
        compile
            (Geom.boxplot 0.6 series summaries |> Geom.onSeries series)
            [| pt (float (series.XScale.Forward 0.0)) 0.05
               pt (float (series.XScale.Forward 1.0)) 0.05 |]

    /// §5.5 names the box plot as the thing that must *not* be an IR case. It is a filled path
    /// for the boxes, a stroked path for the median and whiskers, and one batch for the
    /// outliers — three nodes, and nine `Node` cases still.
    [<Fact>]
    let ``a box plot is two paths and a marker batch`` () =
        match node with
        | Group parts ->
            Assert.Equal(3, parts.Length)

            match parts.[0], parts.[1], parts.[2] with
            | Path(_, Some _, Some _, _), Path(_, Some _, None, _), Markers _ -> ()
            | a, b, c -> failwithf "unexpected shape: %A / %A / %A" a b c
        | other -> failwithf "expected a Group, got %A" other

    [<Fact>]
    let ``there is one box per band and the boxes span the quartiles`` () =
        let boxes =
            match node with
            | Group parts -> subpaths parts.[0]
            | _ -> []

        Assert.Equal(2, boxes.Length)
        // The first sample's quartiles are 3 and 7 on a 0…100 axis.
        Assert.Equal<float list>([ 0.03; 0.03; 0.07; 0.07 ], ys boxes.[0])

    /// Outliers are drawn — D52's rule — and drawn hollow, so one sitting on a whisker cap is
    /// still countable.
    [<Fact>]
    let ``the outlier is drawn, hollow, and only for the sample that has one`` () =
        let batch = markerBatches node |> List.exactlyOne

        Assert.Equal(1, batch.Points.Length)
        Assert.Equal(1.0, float batch.Points.[0].Y, 12)
        Assert.True(batch.Fill.IsNone)
        Assert.True(batch.Stroke.IsSome)

    /// The whiskers reach the last observation inside the fence, not the extreme — which for the
    /// second sample means 9 out of 100, at 0.09 rather than 1.0.
    [<Fact>]
    let ``the whisker stops short of the outlier`` () =
        let rules =
            match node with
            | Group parts -> pathPoints parts.[1] |> List.map (fun p -> float p.Y)
            | _ -> []

        Assert.True(List.max rules < 0.1, sprintf "a whisker reached %f" (List.max rules))

    /// **The measure claim.** A box plot of metres takes summaries of metres.
    [<Fact>]
    let ``a box plot of metres takes a summary of metres`` () =
        let s: Series<1, m> =
            { Name = ""
              X = [| 0.0 |]
              Y = [| 1.0<m> |]
              XScale = Scale.ordinal [| "a" |]
              YScale = Scale.linear U.metre (0.0<m>, 10.0<m>) }

        match Geom.boxplot 0.6 s [| Stat.summary U.metre [| 2.0<m>; 4.0<m>; 6.0<m> |] |] |> Geom.onSeries s with
        | GeomBox(spans, _) -> Assert.Equal(0.4, float spans.[0].Median, 12)
        | other -> failwithf "expected a GeomBox, got %A" other

module ``violin`` =

    let private series: Series<1, 1> =
        { Name = ""
          X = [| 0.0; 1.0 |]
          Y = [| 0.0; 0.0 |]
          XScale = Scale.ordinal [| "a"; "b" |]
          YScale = Scale.linear U.one (-4.0, 4.0) }

    /// **The normalisation is shared, and this is the test that says so.** A tight sample and a
    /// broad one at the same bandwidth have very different peak densities; normalising each to
    /// its own peak makes them the same width and erases the comparison.
    [<Fact>]
    let ``a peakier density draws a wider violin`` () =
        let tight = Array.init 40 (fun i -> float i * 0.005 - 0.1)
        let broad = Array.init 40 (fun i -> float i * 0.1 - 2.0)
        let h = 0.2

        let g =
            Geom.violin
                0.8
                series
                [| Stat.densityWith U.one h 64 tight
                   Stat.densityWith U.one h 64 broad |]

        let pts =
            [| pt (float (series.XScale.Forward 0.0)) 0.5
               pt (float (series.XScale.Forward 1.0)) 0.5 |]

        let widths =
            compile (Geom.onSeries series g) pts
            |> subpaths
            |> List.map (fun run ->
                let vs = xs run
                List.max vs - List.min vs)

        Assert.Equal(2, widths.Length)
        Assert.True(
            widths.[0] > widths.[1] * 2.0,
            sprintf "the tight sample should be much the wider violin; got %f and %f" widths.[0] widths.[1]
        )

    [<Fact>]
    let ``a violin is a closed, filled polygon per band`` () =
        let g =
            Geom.violin 0.8 series (Array.create 2 (Stat.densityWith U.one 0.3 32 [| -1.0; 0.0; 1.0 |]))
            |> Geom.onSeries series

        let pts =
            [| pt (float (series.XScale.Forward 0.0)) 0.5
               pt (float (series.XScale.Forward 1.0)) 0.5 |]

        match compile g pts with
        | Path(segs, Some _, Some _, _) ->
            Assert.Equal(2, segs |> Array.filter (fun s -> s = ClosePath) |> Array.length)
            // Both sides of the profile, so twice the grid.
            Assert.Equal(2 * 2 * 32, segs |> Array.filter (fun s -> match s with MoveTo _ | LineTo _ -> true | _ -> false) |> Array.length)
        | other -> failwithf "expected a stroked, filled Path, got %A" other

    /// The violin is symmetric about its band, which is the only thing the mirroring has to get
    /// right and the thing a sign error would break.
    [<Fact>]
    let ``a violin is symmetric about its band`` () =
        let g =
            Geom.violin 0.8 series (Array.create 2 (Stat.densityWith U.one 0.3 32 [| -1.0; 0.0; 1.0 |]))
            |> Geom.onSeries series

        let centre = float (series.XScale.Forward 0.0)
        let run = compile g [| pt centre 0.5; pt centre 0.5 |] |> subpaths |> List.head
        let vs = xs run

        Assert.Equal(centre - List.min vs, List.max vs - centre, 9)

// ======================================================================================
// THE IMAGE
//
// The only geom in the library that does not bottom out in `Path`, `Markers` or `Text`, and the
// only producer of a `Raster` node anywhere above the IR. Until it existed, `renderWithAssets`
// had no customer — which is D10's failure mode, and the reason D64 insists a sidecar API and
// its producer ship together.
// ======================================================================================

module ``image`` =

    let private img: Image =
        { Width = 2
          Height = 1
          Rgba = [| 255uy; 0uy; 0uy; 255uy; 0uy; 0uy; 255uy; 255uy |] }

    let private figure () =
        Chart.image U.second U.metre img 1.0<s> 2.0<m> 5.0<s> 6.0<m> false

    let private rasterOf (n: Node) =
        n
        |> Scene.fold
            (fun acc node ->
                match node with
                | Raster r -> r :: acc
                | _ -> acc)
            []

    /// §5.5's standing claim is that every geom compiles to `Path`, `Text` or `Markers`. This is
    /// the one exception, and it is the exception `Raster` was added to the IR for — so the node
    /// count stays nine and this geom is why the ninth case is not dead weight.
    [<Fact>]
    let ``an image compiles to a Raster node`` () =
        let r = Assert.Single(rasterOf (Chart.toScene surface (figure ())))
        Assert.Equal(2, r.Image.Width)
        Assert.Equal(1, r.Image.Height)
        Assert.False r.Smooth

    /// The domain is the box and nothing more, on a tessellation's rule: an image partitions the
    /// region it covers, so a margin past its edge draws part of that region as if the image did
    /// not cover it.
    [<Fact>]
    let ``the axes fit the image exactly, unpadded`` () =
        let fig = figure ()
        let panel = fig.Panels.[0]

        let sx, sy = Erase.seriesScales panel.Layers.[0].Data

        Assert.Equal((1.0, 5.0), sx.Domain)
        Assert.Equal((2.0, 6.0), sy.Domain)

    /// The box is projected corner by corner, so it fills the panel it was told to fill. A
    /// half-size converted once would be right only in the middle of a log axis — D57's
    /// correction, applied here before it could become the same bug a third time.
    [<Fact>]
    let ``the raster box is the panel rectangle`` () =
        let r = Assert.Single(rasterOf (Chart.toScene surface (figure ())))

        // The image occupies the whole data range, and the data range is the whole panel.
        Assert.Equal(0.0, float r.Bounds.X0, 6)
        Assert.Equal(0.0, float r.Bounds.Y0, 6)
        Assert.Equal(1.0, float r.Bounds.X1, 6)
        Assert.Equal(1.0, float r.Bounds.Y1, 6)

    /// Both backends draw it, by different routes, and neither is the other's fallback: SVG
    /// inlines a `data:` URI because a self-contained file is what makes it the notebook and
    /// golden format, and TikZ writes a sidecar because a `.tex` cannot carry bytes.
    [<Fact>]
    let ``svg inlines the pixels and tikz writes a sidecar`` () =
        let fig = figure ()

        let svg = Chart.render SvgBackend.render surface fig
        Assert.Contains("data:image/png;base64,", svg)

        let tex = Chart.render TikzBackend.renderWithAssets surface fig
        Assert.Contains("\\includegraphics", tex.Document)
        Assert.DoesNotContain("base64", tex.Document)
        Assert.Single tex.Assets |> ignore

    /// A wrong stride is a bug in the line above the call, not a figure to draw approximately.
    /// The backends pad a short buffer with transparent black and say so in a comment, which is
    /// right for a scene built by hand and wrong for a constructor that has the array in hand.
    [<Fact>]
    let ``a mis-sized pixel buffer is refused at the constructor`` () =
        let short: Image = { Width = 4; Height = 4; Rgba = Array.zeroCreate 16 }

        let ex =
            Assert.Throws<System.ArgumentException>(fun () ->
                Chart.image U.second U.metre short 0.0<s> 0.0<m> 1.0<s> 1.0<m> false |> ignore)

        Assert.Contains("needs 64 bytes", ex.Message)
        Assert.Contains("got 16", ex.Message)

    [<Fact>]
    let ``an image with no pixels is refused`` () =
        let empty: Image = { Width = 0; Height = 3; Rgba = [||] }

        Assert.Throws<System.ArgumentException>(fun () ->
            Chart.image U.second U.metre empty 0.0<s> 0.0<m> 1.0<s> 1.0<m> false |> ignore)
        |> ignore

// ======================================================================================
// THE ROW-INDEXING GATE
//
// A geom carrying a per-point array indexes it by *row*; a channel batch hands the
// compiler a renumbered subset of the rows. The bug this catches draws every mark, in the right
// place, attached to the wrong datum.
// ======================================================================================

module ``a channel batch reads its own rows`` =

    let private pts = Array.init 4 (fun i -> pt (float i / 4.0) 0.5)

    let private second = [| 2; 3 |]

    let private subset (g: GeomOf<nd>) =
        Geom.compileSubset Coord.cartesian surface style g pts second

    [<Fact>]
    let ``an error bar's span belongs to the point it is drawn on`` () =
        let low = [| 0.0; 0.1; 0.2; 0.3 |] |> Array.map nd
        let high = [| 0.5; 0.6; 0.7; 0.8 |] |> Array.map nd

        let runs = subset (GeomErrorY(low, high)) |> subpaths

        // Three subpaths per bar — the vertical and two caps — so the first of each triple is
        // the span itself.
        Assert.Equal(6, runs.Length)
        Assert.Equal<float list>([ 0.2; 0.7 ], ys runs.[0])
        Assert.Equal<float list>([ 0.3; 0.8 ], ys runs.[3])

    [<Fact>]
    let ``a ribbon's bounds belong to the points it is drawn over`` () =
        let low = [| 0.0; 0.1; 0.2; 0.3 |] |> Array.map nd
        let high = [| 0.5; 0.6; 0.7; 0.8 |] |> Array.map nd

        let run = subset (GeomRibbon(low, high)) |> subpaths |> List.exactlyOne

        Assert.Equal<float list>([ 0.7; 0.8; 0.3; 0.2 ], ys run)

    [<Fact>]
    let ``a rectangle's corners belong to the row`` () =
        let a = Array.init 4 (fun i -> nd (float i * 0.1))
        let b = Array.init 4 (fun i -> nd (float i * 0.1 + 0.05))

        let run = subset (GeomRect(a, a, b, b)) |> subpaths

        Assert.Equal(2, run.Length)
        Assert.Equal<float list>([ 0.2; 0.25; 0.25; 0.2 ], xs run.[0])

    [<Fact>]
    let ``a label belongs to the row`` () =
        let runs = subset (Geom.text [| "0"; "1"; "2"; "3" |]) |> textRuns

        Assert.Equal<string list>([ "2"; "3" ], runs |> List.map (fun r -> r.Text))

    [<Fact>]
    let ``a box belongs to the row`` () =
        let spans =
            Array.init 4 (fun i ->
                { Lower = nd (float i * 0.1)
                  Q1 = nd (float i * 0.1 + 0.01)
                  Median = nd (float i * 0.1 + 0.02)
                  Q3 = nd (float i * 0.1 + 0.03)
                  Upper = nd (float i * 0.1 + 0.04)
                  Outliers = [||] })

        let boxes =
            match subset (GeomBox(spans, 0.6)) with
            | Group parts -> subpaths parts.[0]
            | other -> failwithf "expected a Group, got %A" other

        Assert.Equal(2, boxes.Length)
        Assert.Equal(0.21, List.min (ys boxes.[0]), 9)

    /// The other half of `compileSubset`'s contract, and the reason it exists: the *layout* a
    /// channel changes is nothing. Three coloured batches of a box plot are not three box plots,
    /// so the band width still comes from the whole layer's rhythm.
    [<Fact>]
    let ``the band width still comes from the whole layer`` () =
        let spans =
            Array.init 4 (fun _ ->
                { Lower = nd 0.1
                  Q1 = nd 0.2
                  Median = nd 0.3
                  Q3 = nd 0.4
                  Upper = nd 0.5
                  Outliers = [||] })

        let widthOf (node: Node) =
            match node with
            | Group parts ->
                subpaths parts.[0]
                |> List.head
                |> xs
                |> fun vs -> List.max vs - List.min vs
            | other -> failwithf "expected a Group, got %A" other

        let whole = widthOf (compile (GeomBox(spans, 0.6)) pts)
        let batch = widthOf (subset (GeomBox(spans, 0.6)))

        Assert.Equal(whole, batch, 12)

// ======================================================================================
// GATE 5 — cross-backend geometry agreement, extended to every phase 6 geom
//
// The M2 plan's fifth gate: "the existing oracle — TikZ `bp` equals SVG `px` x 72/96 with y from
// the opposite edge — is the only check that catches a sign regression in either backend's
// independently-derived arithmetic. Every Phase 6 geom joins it."
// ======================================================================================

module ``cross-backend geometry agreement`` =

    let private factor = 72.0 / 96.0

    /// Every path coordinate the geom emits, checked in both documents. Not a sample of them:
    /// a flip that only affects the closing edge of a ribbon is exactly the kind of bug an
    /// eyeballed subset misses.
    let private agree (name: string) (node: Node) =
        let svg = SvgBackend.render surface node
        let tex = TikzBackend.render surface node
        let points = pathPoints node

        Assert.True(points.Length > 0, name + " emitted no path geometry")

        for p in points do
            let svgX = float p.X * float surface.Width
            let svgY = (1.0 - float p.Y) * float surface.Height

            Assert.True(
                svg.Contains(Svg.num svgX + " " + Svg.num svgY),
                sprintf "%s: SVG is missing (%f, %f)" name svgX svgY
            )

            Assert.True(
                tex.Contains(Tikz.coord (svgX * factor) ((float surface.Height - svgY) * factor)),
                sprintf "%s: TikZ is missing the reflection of (%f, %f)" name svgX svgY
            )

    let private series = unitSeries "" [| 0.125; 0.5; 0.875 |] [| 0.25; 0.6; 0.4 |]

    let private pts = [| pt 0.125 0.25; pt 0.5 0.6; pt 0.875 0.4 |]

    let private geoms: (string * Geom) list =
        [ "step", GeomStep StepAfter
          "step-mid", GeomStep StepMid
          "ribbon", Geom.ribbon series [| 0.1; 0.4; 0.2 |] [| 0.5; 0.8; 0.7 |]
          "tile", Geom.tile 0.1 0.2 series
          "rect", Geom.rect series [| 0.0; 0.3; 0.6 |] [| 0.0; 0.1; 0.2 |] [| 0.25; 0.55; 0.85 |] [| 0.3; 0.5; 0.7 |]
          "segment", Geom.segment series [| 0.25; 0.75; 1.0 |] [| 0.5; 0.2; 0.9 |]
          "boxplot",
          Geom.boxplot
              0.6
              series
              [| Stat.summary U.one [| 0.1; 0.2; 0.3; 0.4; 0.5 |]
                 Stat.summary U.one [| 0.4; 0.5; 0.6; 0.7; 0.8 |]
                 Stat.summary U.one [| 0.2; 0.3; 0.4; 0.5; 0.9 |] |]
          "violin", Geom.violin 0.8 series (Array.create 3 (Stat.densityWith U.one 0.15 16 [| 0.3; 0.5; 0.7 |])) ]

    [<Fact>]
    let ``every phase 6 geom agrees across the backends`` () =
        for name, g in geoms do
            agree name (compile (Geom.onSeries series g) pts)

    /// A `GeomText` layer emits no path geometry, so it joins the gate through the one thing it
    /// does emit: the two backends must place the run at the same point on the page.
    [<Fact>]
    let ``a labelled point agrees across the backends`` () =
        let node = compile (Geom.text [| "a"; "b"; "c" |]) pts
        let run = textRuns node |> List.head
        let svgX = float run.At.X * float surface.Width
        let svgY = (1.0 - float run.At.Y) * float surface.Height

        Assert.Contains(Svg.num svgX, SvgBackend.render surface node)
        Assert.Contains(Tikz.coord (svgX * factor) ((float surface.Height - svgY) * factor), TikzBackend.render surface node)

    /// Gate 6, for the new geoms: two renders byte-identical, and nothing in any of them reaches
    /// the output by enumerating a set.
    [<Fact>]
    let ``every phase 6 geom renders deterministically`` () =
        for name, g in geoms do
            let node = compile (Geom.onSeries series g) pts
            Assert.True((SvgBackend.render surface node = SvgBackend.render surface node), name)
            Assert.True((TikzBackend.render surface node = TikzBackend.render surface node), name)

// ======================================================================================
// The front doors
// ======================================================================================

module ``the Chart constructors`` =

    let private geomOf (fig: Figure) = fig.Panels.[0].Layers.[0].Geom

    let private domainOfY (fig: Figure) =
        let _, y = Erase.seriesScales fig.Panels.[0].Layers.[0].Data
        y.Domain

    [<Fact>]
    let ``a band's domain covers both bounds`` () =
        let fig = Chart.ribbonIn U.second U.metre [| 0.0<s>; 1.0<s> |] [| 1.0<m>; 2.0<m> |] [| 8.0<m>; 9.0<m> |]
        let lo, hi = domainOfY fig

        Assert.True(lo <= 1.0, sprintf "the lower bound is outside the domain: %f" lo)
        Assert.True(hi >= 9.0, sprintf "the upper bound is outside the domain: %f" hi)

    /// The same rule a histogram's x domain plays by: a tessellation partitions a region, so a
    /// margin past the last tile draws part of that region as if no cell covered it.
    [<Fact>]
    let ``a tiled figure's domain is the tiles' own edges, unpadded`` () =
        let fig = Chart.tileIn U.one U.one 2.0 1.0 [| 0.0; 2.0; 4.0 |] [| 0.0; 1.0; 2.0 |]
        let x, y = Erase.seriesScales fig.Panels.[0].Layers.[0].Data

        Assert.Equal((-1.0, 5.0), x.Domain)
        Assert.Equal((-0.5, 2.5), y.Domain)

    /// The heatmap derives its cell size from the grid rather than asking, and takes the
    /// *smallest* gap so a grid with a hole in it does not widen every tile to cover it.
    [<Fact>]
    let ``a heatmap sizes its cells from the grid's own pitch`` () =
        let x = [| 0.0; 1.0; 3.0 |]
        let y = [| 0.0; 1.0; 2.0 |]
        let fig = Chart.heatmapIn U.one U.one U.kelvin x y [| 300.0<K>; 310.0<K>; 320.0<K> |]
        let sx, _ = Erase.seriesScales fig.Panels.[0].Layers.[0].Data

        // A pitch of 1, so the tiles reach half a cell past 0 and past 3.
        Assert.Equal((-0.5, 3.5), sx.Domain)
        // And the third quantity became a colour channel with its own witness.
        Assert.Equal(1, fig.Panels.[0].Layers.[0].Channels.Length)

    [<Fact>]
    let ``a segment figure holds both ends inside the domain`` () =
        let fig = Chart.segmentIn U.one U.one [| 0.0; 1.0 |] [| 0.0; 1.0 |] [| 5.0; 6.0 |] [| 9.0; 2.0 |]
        let x, y = Erase.seriesScales fig.Panels.[0].Layers.[0].Data

        Assert.True(fst x.Domain <= 0.0 && snd x.Domain >= 6.0)
        Assert.True(fst y.Domain <= 0.0 && snd y.Domain >= 9.0)

    /// `addLabels` reuses the layer it labels, which is what keeps the labels on the marks: one
    /// set of positions, two layers.
    [<Fact>]
    let ``addLabels reuses the labelled series and stays out of the legend`` () =
        let fig =
            Chart.scatterIn U.one U.one [| 0.0; 1.0; 2.0 |] [| 1.0; 4.0; 9.0 |]
            |> Chart.withName "measured"
            |> Chart.addLabels [| "a"; "b"; "c" |]

        let layers = fig.Panels.[0].Layers

        Assert.Equal(2, layers.Length)
        Assert.Equal(layers.[0].Style, layers.[1].Style)
        Assert.False layers.[1].ShowInLegend

        match layers.[1].Geom with
        | GeomText(labels, _, _, _) -> Assert.Equal<string[]>([| "a"; "b"; "c" |], labels)
        | other -> failwithf "expected a GeomText, got %A" other

    [<Fact>]
    let ``addLabels refuses a mismatched count`` () =
        Assert.Throws<System.ArgumentException>(fun () ->
            Chart.scatterIn U.one U.one [| 0.0; 1.0 |] [| 1.0; 2.0 |]
            |> Chart.addLabels [| "only one" |]
            |> ignore)
        |> ignore

    [<Fact>]
    let ``a step figure is a staircase`` () =
        Assert.Equal(GeomStep StepAfter, geomOf (Chart.stepIn U.one U.one [| 0.0; 1.0 |] [| 1.0; 2.0 |]))
        Assert.Equal(GeomStep StepBefore, geomOf (Chart.stepWithIn StepBefore U.one U.one [| 0.0; 1.0 |] [| 1.0; 2.0 |]))

module ``the categorical distributions`` =

    let private keys =
        Array.init 60 (fun i -> [| "control"; "treated"; "washout" |].[i % 3])

    let private values =
        Array.init 60 (fun i -> (10.0 + float (i % 7) + float (i % 3) * 4.0) * 1.0<m>)

    [<Fact>]
    let ``a box plot has one box per level, in first-appearance order`` () =
        let fig = Chart.Category.boxplotIn U.metre keys values
        let layer = fig.Panels.[0].Layers.[0]

        match layer.Geom with
        | GeomBox(spans, _) -> Assert.Equal(3, spans.Length)
        | other -> failwithf "expected a GeomBox, got %A" other

        Assert.Equal(3, Erase.seriesLength layer.Data)

        let x, _ = Erase.seriesScales layer.Data

        Assert.Equal<string[]>(
            [| "control"; "treated"; "washout" |],
            x.Ticks 3 |> Array.map (fun t -> t.Label)
        )

    /// The domain has to reach the outliers, because they are exactly what a box plot is read to
    /// find and a tighter domain hides them behind the panel edge.
    [<Fact>]
    let ``the y domain reaches an outlier`` () =
        let withOutlier = Array.append values [| 400.0<m> |]
        let withKey = Array.append keys [| "control" |]

        let fig = Chart.Category.boxplotIn U.metre withKey withOutlier
        let _, y = Erase.seriesScales fig.Panels.[0].Layers.[0].Data

        Assert.True(snd y.Domain >= 400.0, sprintf "the outlier is outside the domain: %f" (snd y.Domain))

    /// D48's argument arriving in a second place: the bandwidth is the whole sample's, so the
    /// groups are smoothed alike and a one-row group does not raise.
    [<Fact>]
    let ``a violin shares one bandwidth across the levels`` () =
        let sparse = Array.append keys [| "single" |]
        let sparseValues = Array.append values [| 12.0<m> |]

        let fig = Chart.Category.violinIn U.metre sparse sparseValues

        match fig.Panels.[0].Layers.[0].Geom with
        | GeomViolin(profiles, _) ->
            Assert.Equal(4, profiles.Length)
            // The one-row level still has a profile rather than an exception.
            Assert.True(profiles.[3].At.Length > 2)
        | other -> failwithf "expected a GeomViolin, got %A" other

    [<Fact>]
    let ``a violin of a sample with no spread says so`` () =
        Assert.Throws<System.ArgumentException>(fun () ->
            Chart.Category.violinIn U.metre [| "a"; "b" |] [| 3.0<m>; 3.0<m> |] |> ignore)
        |> ignore

    /// The x axis is titled by its bands, so the conventional symbol stays suppressed — the same
    /// rule `Chart.Category.bar` plays by, and easy to lose when a new constructor is added.
    [<Fact>]
    let ``a categorical distribution suppresses the x symbol`` () =
        Assert.Equal(Some "", (Chart.Category.boxplotIn U.metre keys values).Panels.[0].XAxis.Label)
        Assert.Equal(Some "", (Chart.Category.violinIn U.metre keys values).Panels.[0].XAxis.Label)

// ======================================================================================
// The block's own filled geoms
// ======================================================================================

module ``area and bars from a block`` =

    type private Row = { T: float<s>; P: float<m> }

    let private rows =
        Array.init 8 (fun i ->
            { T = float i * 1.0<s>
              P = (4.0 + float i) * 1.0<m> })

    /// D50 through the block. The imprecision this closes read: "`GeomArea` and `GeomBar` are
    /// not usefully reachable from a block — their baseline comes from the y scale, which does
    /// not exist until `Run`."
    [<Fact>]
    let ``a block's bars are measured from zero`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.T)
                y U.metre (fun r -> r.P)
                bars
            }

        let layer = fig.Panels.[0].Layers.[0]
        let _, y = Erase.seriesScales layer.Data

        Assert.Equal(0.0, fst y.Domain, 12)

        match layer.Geom with
        | GeomBar(baseline, _) -> Assert.Equal(0.0, float baseline, 12)
        | other -> failwithf "expected a GeomBar, got %A" other

    [<Fact>]
    let ``a block's area is filled to zero`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.T)
                y U.metre (fun r -> r.P)
                area
                layer GeomLine
            }

        Assert.Equal(2, fig.Panels.[0].Layers.Length)

        match fig.Panels.[0].Layers.[0].Geom with
        | GeomArea baseline -> Assert.Equal(0.0, float baseline, 12)
        | other -> failwithf "expected a GeomArea, got %A" other

    /// The widening is conditional: a block with no filled layer keeps the padded domain it
    /// always had, so nothing that predates D56 moves.
    [<Fact>]
    let ``a block without a filled layer keeps its padded domain`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.T)
                y U.metre (fun r -> r.P)
                layer GeomLine
            }

        let _, y = Erase.seriesScales fig.Panels.[0].Layers.[0].Data

        Assert.True(fst y.Domain > 0.0, sprintf "the domain reached zero without being asked: %f" (fst y.Domain))

    /// A geom that is a value still goes through `layer`, and the two spellings compose.
    [<Fact>]
    let ``a step and a label are ordinary layers of a block`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.T)
                y U.metre (fun r -> r.P)
                layer (GeomStep StepAfter)
                layer (Geom.text (rows |> Array.map (fun r -> sprintf "%.0f" (float r.P))))
            }

        Assert.Equal(2, fig.Panels.[0].Layers.Length)
        Assert.Equal(GeomStep StepAfter, fig.Panels.[0].Layers.[0].Geom)

    /// Every layer of a block is one series, so a filled one takes the block's base style rather
    /// than advancing the palette.
    [<Fact>]
    let ``a filled layer shares the block's one style`` () =
        let fig =
            plot rows {
                x U.second (fun r -> r.T)
                y U.metre (fun r -> r.P)
                areaWith (Geom.withOpacity 0.5)
                layer GeomLine
            }

        let layers = fig.Panels.[0].Layers

        Assert.Equal(layers.[0].Style.Color, layers.[1].Style.Color)
        Assert.Equal(0.5, layers.[0].Style.Opacity, 12)

// ======================================================================================
// A geom draws where its data is, whatever the panel's domain became after it was built
// ======================================================================================

/// **Gate 1 of `m4-plan.md`.** Ten of the fifteen geom cases carry arrays of their own — the span
/// of an error bar, the corners of a tile, a box's quartiles, an area's baseline. Until D97 those
/// arrays were projected into the panel **at construction**, against the series' scales as they
/// were at that moment, while the layer's points were projected at compile time against the
/// scales the panel ended up with. Anything that changed a domain in between — a second layer
/// (`Figure.unify`), `Chart.withXDomain`, a switch to a log axis — moved the points and left the
/// geom's own arrays where they were. A ±0.5 m error bar added to a 0–10 m line spanned 0–10 m.
///
/// The oracle is the same geom built against a series whose scales **already are** the final
/// ones, put through the same operation. A geom that is projected when its points are draws
/// identically either way; one projected at construction does not.
module ``a geom draws where its data is`` =

    let private extent = Chart.size 400.0 300.0
    let private x = [| 4.0<m>; 5.0<m>; 6.0<m> |]
    let private y = [| 4.5<m>; 5.0<m>; 5.5<m> |]

    let private geoms: (string * (Series<m, m> -> Geom)) list =
        let sample = [| 3.0<m>; 4.0<m>; 4.5<m>; 5.0<m>; 5.2<m>; 5.5<m>; 6.0<m>; 7.0<m> |]
        let pixel = { Width = 1; Height = 1; Rgba = [| 255uy; 0uy; 0uy; 255uy |] }

        [ "errorY", (fun s -> Geom.errorY s [| 0.5<m>; 0.5<m>; 0.5<m> |])
          "errorX", (fun s -> Geom.errorX s [| 0.5<m>; 0.5<m>; 0.5<m> |])
          "ribbon", (fun s -> Geom.ribbonAround s [| 0.5<m>; 0.5<m>; 0.5<m> |])
          "tile", Geom.tile 0.4<m> 0.4<m>
          "segment", (fun s -> Geom.segment s [| 4.5<m>; 5.5<m>; 6.5<m> |] [| 5.0<m>; 5.5<m>; 6.0<m> |])
          "boxplot", (fun s -> Geom.boxplot 0.5 s (Array.create 3 (Stat.summary U.metre sample)))
          "violin", (fun s -> Geom.violin 0.5 s (Array.create 3 (Stat.density U.metre sample)))
          "image", (fun s -> Geom.image s pixel 4.2<m> 4.2<m> 5.8<m> 5.8<m> false)
          "area", Geom.area
          "bar", Geom.bar 0.5
          // A field carries its own grid rather than the series' points, and its node
          // coordinates are arrays of exactly the kind D97 is about.
          "field contours",
          (fun _ ->
              GeomField(
                  { Xs = [| 4.0; 5.0; 6.0 |]
                    Ys = [| 4.0; 5.0; 6.0 |]
                    Values = [| 0.0; 1.0; 2.0; 1.0; 2.0; 3.0; 2.0; 3.0; 4.0 |]
                    U = [||]
                    W = [||] },
                  FieldContours [| 1.5; 2.5 |]
              ))
          "field heatmap",
          (fun _ ->
              GeomField(
                  { Xs = [| 4.0; 5.0; 6.0 |]
                    Ys = [| 4.0; 5.0; 6.0 |]
                    Values = [| 0.0; 1.0; 2.0; 1.0; 2.0; 3.0; 2.0; 3.0; 4.0 |]
                    U = [||]
                    W = [||] },
                  FieldHeatmap false
              )) ]

    let private operations: (string * (Figure -> Figure)) list =
        // Both domains reach below zero, so an area's and a bar's baseline — clamped to the panel
        // edge while zero was outside the domain they were built against — has somewhere to move
        // to. Without that, those two cases pass whether or not the baseline follows.
        [ "a wider second layer", Chart.addScatterIn U.metre U.metre [| -2.0<m>; 10.0<m> |] [| -2.0<m>; 10.0<m> |]
          "a stated domain", Chart.withXDomain -1.0 9.0 >> Chart.withYDomain -1.0 9.0
          "a log axis", Chart.withXLog >> Chart.withYLog ]

    let cases: System.Collections.Generic.IEnumerable<obj[]> =
        [ for g, _ in geoms do
              for o, _ in operations do
                  yield [| box g; box o |] ]

    let private svgOf (fig: Figure) =
        SvgBackend.render extent (Chart.toScene extent fig)

    [<Theory>]
    [<MemberData(nameof cases)>]
    let ``the geom's own arrays follow the panel's final scales`` (geom: string) (operation: string) =
        let mk = geoms |> List.find (fun (n, _) -> n = geom) |> snd
        let op = operations |> List.find (fun (n, _) -> n = operation) |> snd

        let s = Chart.seriesIn "" U.metre U.metre x y
        let actual = Chart.ofSeries (mk s) s |> op

        // The scales the geom's layer ends up drawn against, read off the figure the way the
        // compiler reads them: after `unify`.
        let fx, fy = Erase.seriesScales (Figure.unify actual.Panels.[0]).Layers.[0].Data

        let final (e: IScaleErased) =
            Scale.ofKind e.Kind U.metre (LanguagePrimitives.FloatWithMeasure<m> (fst e.Domain), LanguagePrimitives.FloatWithMeasure<m> (snd e.Domain))

        let s' = { s with XScale = final fx; YScale = final fy }
        let expected = Chart.ofSeries (mk s') s' |> op

        Assert.True(
            svgOf expected = svgOf actual,
            sprintf
                "the %s geom under %s was drawn against the domain it was built with rather than the one \
                 the panel ended up with (D97)"
                geom
                operation
        )
