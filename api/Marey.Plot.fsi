namespace FSharp

namespace Marey

    [<RequireQualifiedAccess>]
    module Keys =

        [<Literal>]
        val Separator: char = '/'

        val figure: Key

        val figureLegend: Key

        val panel: row: int -> col: int -> Key

        val layer: panelKey: Key -> ordinal: int -> Key

        val mark: layerKey: Key -> row: int -> Key

        val escape: k: string -> string

        val datum: layerKey: Key -> k: string -> Key

        val validate: rows: int -> keys: string array -> unit

        val axisX: panelKey: Key -> Key

        val axisY: panelKey: Key -> Key

        val inset: panelKey: Key -> i: int -> Key

        val axisX2: panelKey: Key -> Key

        val axisY2: panelKey: Key -> Key

        val grid: panelKey: Key -> Key

        val legend: panelKey: Key -> Key

        val colorbar: panelKey: Key -> Key

        val letter: panelKey: Key -> Key

        val covers: Key -> Key -> bool

namespace Marey

    type Cvd =
        | Protanopia
        | Deuteranopia
        | Tritanopia

    type Ramp =
        {
          Name: string
          Stops: (float * Color) array
        }

    [<RequireQualifiedAccess>]
    module Ramp =

        val ofStops: name: string -> stops: (float * Color) array -> Ramp

        val ofColors: name: string -> cs: Color array -> Ramp

        val ofHex: name: string -> hexes: string array -> Ramp

        val at: r: Ramp -> t: float -> Color

        val reverse: r: Ramp -> Ramp

        val sample: n: int -> r: Ramp -> Color array

    [<Struct>]
    type Separation =
        {

          Vision: string

          Minimum: float

          IndexA: int
          IndexB: int
        }

    [<NoComparison>]
    type PaletteReport =
        {
          Size: int

          Separations: Separation array

          WorstSeparation: Separation

          MinContrast: float
          MinContrastIndex: int

          Ok: bool
        }

    [<RequireQualifiedAccess>]
    module Palette =

        val black: Color

        val orange: Color

        val skyBlue: Color

        val bluishGreen: Color

        val yellow: Color

        val blue: Color

        val vermillion: Color

        val reddishPurple: Color

        val okabeIto: Color array

        val okabeItoNames: string array

        val series: Color array

        val seriesDark: Color array

        val pick: cs: Color array -> i: int -> Color

        val viridis: Ramp

        val cividis: Ramp

        val magma: Ramp

        val sequential: Ramp array

        val divergingBetween:
          name: string ->
            hueLo: float<deg> ->
            hueHi: float<deg> ->
            chroma: float -> endL: float -> centreL: float -> Ramp

        val diverging: Ramp

        val divergingGreenPurple: Ramp

        val simulate: kind: Cvd -> c: Color -> Color

        val distance: a: Color -> b: Color -> float

        val minSeparation: cs: Color array -> float * int * int

        [<Literal>]
        val SeparationFloor: float = 0.065

        [<Literal>]
        val ContrastFloor: float = 3

        val validate: background: Color -> cs: Color array -> PaletteReport

        val isLightnessMonotone: tolerance: float -> r: Ramp -> bool

        val lightnessRange: r: Ramp -> float * float

namespace Marey

    type TickDirection =

        | TickOut

        | TickIn

        | TickInOut

    [<NoComparison>]
    type Theme =
        {
          Name: string
          TitleFont: FontSpec
          SubtitleFont: FontSpec
          PanelTitleFont: FontSpec
          AxisTitleFont: FontSpec
          TickFont: FontSpec
          LegendFont: FontSpec

          Background: Color

          PanelBackground: Color

          Foreground: Color

          MutedForeground: Color

          AxisColor: Color
          GridColor: Color
          LegendBackground: Color
          LegendBorder: Color
          AxisWidth: float<px>
          GridWidth: float<px>
          GridDash: float<px> array
          SeriesWidth: float<px>
          TickLength: float<px>
          TickWidth: float<px>
          MarkerSize: float<px>

          ErrorCapWidth: float<px>

          FigurePadding: float<px>

          PanelGap: float<px>

          LabelGap: float<px>

          TitleGap: float<px>
          LegendPadding: float<px>
          LegendItemGap: float<px>
          LegendSwatchWidth: float<px>

          ColorbarWidth: float<px>
          Categorical: Color array
          Sequential: Ramp
          Diverging: Ramp

          UnitStyle: UnitStyle
          ShowGridX: bool
          ShowGridY: bool

          PanelFrame: bool
          ShowLegendFrame: bool

          FillOpacity: float

          TickCount: int

          MarkerShapes: MarkerShape array
          TickDirection: TickDirection

          MirrorTicks: bool

          MinorTicks: bool
          MinorTickLength: float<px>
        }

    [<NoComparison>]
    type ThemeReport =
        {
          Theme: string

          TextContrast: float

          AxisContrast: float

          GridContrast: float

          Palette: PaletteReport

          Ok: bool
        }

    [<RequireQualifiedAccess>]
    module Theme =

        [<Literal>]
        val Serif: string
          = "Times New Roman, Nimbus Roman, Liberation Serif, serif"

        [<Literal>]
        val Sans: string
          =
          "Inter, -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif"

        val publication: Theme

        val modern: Theme

        val initial: Theme

        val notebook: Theme

        val presentation: Theme

        val dark: Theme

        val tikz: Theme

        val all: Theme array

        val tryByName: name: string -> Theme option

        val withPalette: cs: Color array -> t: Theme -> Theme

        val withTickDirection: d: TickDirection -> t: Theme -> Theme

        val withMirrorTicks: on: bool -> t: Theme -> Theme

        val withMinorTicks: on: bool -> t: Theme -> Theme

        val withSequential: r: Ramp -> t: Theme -> Theme

        val withDiverging: r: Ramp -> t: Theme -> Theme

        val withGrid: x: bool -> y: bool -> t: Theme -> Theme

        val withTickCount: n: int -> t: Theme -> Theme

        val scaleType: k: float -> t: Theme -> Theme

        val withFontFamily: family: string -> t: Theme -> Theme

        val validate: t: Theme -> ThemeReport

namespace Marey

    [<RequireQualifiedAccess>]
    type Track =

        | Fixed of size: float<px>

        | Frac of weight: float

        | Content of minimum: float<px>

    [<NoComparison>]
    type GridCell =
        {
          Key: string
          Row: int
          Col: int
          RowSpan: int
          ColSpan: int
          Measure: (ITextMeasurer -> Extent<px>)
        }

    [<NoComparison>]
    type Grid =
        {
          Rows: Track array
          Cols: Track array
          RowGap: float<px>
          ColGap: float<px>
          PadLeft: float<px>
          PadTop: float<px>
          PadRight: float<px>
          PadBottom: float<px>
          Cells: GridCell array
        }

    [<NoComparison>]
    type Arrangement =
        {

          Region: Rect<px>

          Extent: Extent<px>
          ColSizes: float<px> array
          RowSizes: float<px> array
          Placements: (string * Rect<px>) array
          Regions: (string * Rect<nd>) array
        }

    [<RequireQualifiedAccess>]
    module Layout =

        val ndX: e: Extent<px> -> x: float<px> -> float<nd>

        val ndY: e: Extent<px> -> y: float<px> -> float<nd>

        val pxX: e: Extent<px> -> x: float<nd> -> float<px>

        val pxY: e: Extent<px> -> y: float<nd> -> float<px>

        val ndRect: e: Extent<px> -> r: Rect<px> -> Rect<nd>

        val ndPoint: e: Extent<px> -> p: Vec2<px> -> Vec2<nd>

        val pxRect: e: Extent<px> -> r: Rect<nd> -> Rect<px>

        val cell:
          key: string ->
            row: int ->
            col: int -> measure: (ITextMeasurer -> Extent<px>) -> GridCell

        val fill: key: string -> row: int -> col: int -> GridCell

        val spanning: rowSpan: int -> colSpan: int -> c: GridCell -> GridCell

        val grid:
          rows: Track array ->
            cols: Track array -> cells: GridCell array -> Grid

        val withGaps: rowGap: float<px> -> colGap: float<px> -> g: Grid -> Grid

        val withPadding: p: float<px> -> g: Grid -> Grid

        val arrangeIn:
          m: ITextMeasurer ->
            extent: Extent<px> -> region: Rect<px> -> g: Grid -> Arrangement

        val arrange:
          m: ITextMeasurer -> extent: Extent<px> -> g: Grid -> Arrangement

        val tryFindPx: key: string -> a: Arrangement -> Rect<px> option

        val findPx: key: string -> a: Arrangement -> Rect<px>

        val tryFind: key: string -> a: Arrangement -> Rect<nd> option

        val find: key: string -> a: Arrangement -> Rect<nd>

        val overlaps: a: Arrangement -> (string * string) array

        val withinRegion: tolerance: float<px> -> a: Arrangement -> bool

namespace Marey

    type AxisSide =
        | AxisBottom
        | AxisLeft
        | AxisTop
        | AxisRight

    [<NoComparison>]
    type TickPlan =
        {

          Kept: int array

          Rotation: float<rad>
          Rule: string
        }

    [<NoComparison>]
    type AxisRender =
        {

          Spec: AxisSpec
          Side: AxisSide
          Plan: TickPlan

          Panel: Rect<nd>

          Extent: Extent<px>
          Theme: Theme
          ShowSpine: bool
          ShowTicks: bool
          ShowLabels: bool
          ShowTitle: bool

          TitleOffset: float<px>

          Direction: TickDirection
          Mirror: bool

          ShowMinor: bool
        }

    [<RequireQualifiedAccess>]
    module Axes =

        val defaultLabelFor: side: AxisSide -> angle: int -> d: Dim -> string

        val defaultLabel: side: AxisSide -> d: Dim -> string

        val angleOf: packed: PackedScale -> int

        val dimOf: packed: PackedScale -> Dim

        val specTicked:
          ticks: TickSpec ->
            minor: MinorSpec ->
            format: TickFormat ->
            pin: Prefix option ->
            style: UnitStyle ->
            tickCount: int ->
            label: string option ->
            side: AxisSide -> packed: PackedScale -> AxisSpec

        val specOfPinned:
          pin: Prefix option ->
            style: UnitStyle ->
            tickCount: int ->
            label: string option ->
            side: AxisSide -> packed: PackedScale -> AxisSpec

        val specOf:
          style: UnitStyle ->
            tickCount: int ->
            label: string option ->
            side: AxisSide -> packed: PackedScale -> AxisSpec

        val planTicks:
          m: ITextMeasurer ->
            font: FontSpec ->
            minGap: float<px> ->
            lengthPx: float<px> ->
            side: AxisSide -> ticks: TickLabel array -> TickPlan

        val keptTicks: spec: AxisSpec -> plan: TickPlan -> TickLabel array

        val tickExtentWith:
          direction: TickDirection ->
            m: ITextMeasurer ->
            theme: Theme ->
            side: AxisSide -> spec: AxisSpec -> plan: TickPlan -> float<px>

        val tickExtent:
          m: ITextMeasurer ->
            theme: Theme ->
            side: AxisSide -> spec: AxisSpec -> plan: TickPlan -> float<px>

        val labelOverhang:
          m: ITextMeasurer ->
            theme: Theme ->
            side: AxisSide -> spec: AxisSpec -> plan: TickPlan -> float<px>

        val titleExtent:
          m: ITextMeasurer -> theme: Theme -> title: string -> float<px>

        val compile: a: AxisRender -> Node

        val panelTransform: panel: Rect<nd> -> Affine<nd,nd>

        val gridlines:
          theme: Theme ->
            coord: Coord ->
            panel: Rect<nd> ->
            xs: float<nd> array -> ys: float<nd> array -> Node

        val panelShape: coord: Coord -> PathSeg array

        val panelBackground:
          theme: Theme -> coord: Coord -> panel: Rect<nd> -> Node

        val panelFrame: theme: Theme -> coord: Coord -> panel: Rect<nd> -> Node

namespace Marey

    [<NoComparison>]
    type LegendMark =
        | LegendLine of dash: float<px> array
        | LegendMarker of MarkerShape
        | LegendLineMarker of dash: float<px> array * shape: MarkerShape

        | LegendSwatch

        | LegendSizedMarker of shape: MarkerShape * size: float<px>

    [<NoComparison>]
    type LegendEntry =
        {
          Name: string
          Color: Color
          Mark: LegendMark
        }

    type LegendPlacement =
        | LegendNone
        | LegendTopRight
        | LegendTopLeft
        | LegendBottomRight
        | LegendBottomLeft

        | LegendOutsideRight

        | LegendOutsideBottom

        | LegendBest

    type FigureLegend =
        | FigureLegendRight
        | FigureLegendBottom

    [<RequireQualifiedAccess>]
    module Legend =

        val measureIn:
          columns: int ->
            m: ITextMeasurer ->
            theme: Theme -> entries: LegendEntry array -> Extent<px>

        val measure:
          m: ITextMeasurer ->
            theme: Theme -> entries: LegendEntry array -> Extent<px>

        val cornerBox:
          theme: Theme ->
            plot: Rect<px> ->
            size: Extent<px> -> placement: LegendPlacement -> Rect<px>

        val compileIn:
          m: ITextMeasurer ->
            theme: Theme ->
            extent: Extent<px> ->
            b: Rect<px> ->
            columns: int -> framed: bool -> entries: LegendEntry array -> Node

        val compile:
          m: ITextMeasurer ->
            theme: Theme ->
            extent: Extent<px> ->
            plot: Rect<nd> ->
            placement: LegendPlacement -> entries: LegendEntry array -> Node

namespace Marey

    type FieldMark =

        | FieldHeatmap of smooth: bool

        | FieldContours of levels: float array

        | FieldBands of levels: float array

        | FieldQuiver of key: QuiverKey option

        | FieldDirections

        | FieldStreamlines of separation: float<px>

        | FieldLic of LicOptions

    and LicOptions =
        {

          Length: float<px>

          Oversample: int

          Seed: uint64
        }

    and QuiverKey =
        {
          Value: float
          Label: string

          Largest: float
        }

    type FieldPayload<[<Measure>] 'p> =
        {
          Xs: float<'p> array
          Ys: float<'p> array
          Values: float array
          U: float array
          W: float array
        }

    [<RequireQualifiedAccess>]
    module FieldMarks =

        val arrowheadLength: width: float<px> -> float

        val arrowhead:
          head: float ->
            tx: float * ty: float ->
              dx: float * dy: float -> struct (float * float) array

        val heatmap:
          colourOf: (float -> Color option) ->
            smooth: bool -> f: FieldPayload<nd> -> Node

        val isolines:
          level: float -> f: FieldPayload<nd> -> (Vec2<nd> array * bool) array

        val contours:
          mapPt: (Vec2<nd> -> Vec2<nd>) ->
            stroke: Stroke ->
            colourOf: (float -> Color option) ->
            levels: float array -> f: FieldPayload<nd> -> Node

        val bands:
          mapPt: (Vec2<nd> -> Vec2<nd>) ->
            opacity: float ->
            colourOf: (float -> Color option) ->
            levels: float array -> f: FieldPayload<nd> -> Node

        val arrows:
          mapPt: (Vec2<nd> -> Vec2<nd>) ->
            measurer: ITextMeasurer ->
            plot: Extent<px> ->
            background: Color ->
            stroke: Stroke ->
            font: FontSpec ->
            colourOf: (float -> Color option) ->
            directionOnly: bool ->
            key: QuiverKey option -> f: FieldPayload<nd> -> Node

        val streamlinePolylines:
          plot: Extent<px> ->
            separation: float<px> ->
            f: FieldPayload<nd> -> (struct (float * float) array * bool) array

        val streamlines:
          mapPt: (Vec2<nd> -> Vec2<nd>) ->
            plot: Extent<px> ->
            stroke: Stroke ->
            colourOf: (float -> Color option) ->
            separation: float<px> -> f: FieldPayload<nd> -> Node

        val licImage:
          measurer: ITextMeasurer ->
            plot: Extent<px> ->
            colourOf: (float -> Color option) ->
            options: LicOptions ->
            f: FieldPayload<nd> -> (Image * Rect<nd>) option

        val lic:
          measurer: ITextMeasurer ->
            plot: Extent<px> ->
            colourOf: (float -> Color option) ->
            options: LicOptions -> f: FieldPayload<nd> -> Node

namespace Marey

    [<NoComparison>]
    type GeomStyle =
        {
          Color: Color
          Width: float<px>
          Dash: float<px> array

          Opacity: float

          FillOpacity: float
          Shape: MarkerShape
          MarkerSize: float<px>
          Cap: LineCap
          Join: LineJoin

          Font: FontSpec
        }

    type StepAlign =

        | StepAfter

        | StepBefore

        | StepMid

    [<NoComparison>]
    type BoxSpan<[<Measure>] 'p> =
        {
          Lower: float<'p>
          Q1: float<'p>
          Median: float<'p>
          Q3: float<'p>
          Upper: float<'p>

          Outliers: float<'p> array
        }

    [<NoComparison>]
    type ViolinProfile<[<Measure>] 'p> =
        {
          At: float<'p> array
          Width: float array
        }

    [<NoComparison>]
    type RuleSpec<[<Measure>] 'p> =

        | RuleH of y: float<'p>

        | RuleV of x: float<'p>

        | RuleThrough of
          x0: float<'p> * y0: float<'p> * x1: float<'p> * y1: float<'p>

        | SpanH of lo: float<'p> * hi: float<'p>

        | SpanV of lo: float<'p> * hi: float<'p>

    [<NoComparison>]
    type TextPlacement<[<Measure>] 'p> =

        | TextOffset of Vec2<px>

        | TextAt of x: float<'p> * y: float<'p>

        | TextInPanel of x: float * y: float

    type ArrowStyle =
        | ArrowNone
        | ArrowStraight

        | ArrowArc of bend: float

    [<NoComparison>]
    type GeomOf<[<Measure>] 'p> =
        | GeomLine
        | GeomPoint

        | GeomArea of baseline: float<'p>

        | GeomBar of baseline: float<'p> * widthFraction: float

        | GeomErrorY of low: float<'p> array * high: float<'p> array

        | GeomErrorX of low: float<'p> array * high: float<'p> array

        | GeomStep of align: StepAlign

        | GeomRibbon of low: float<'p> array * high: float<'p> array

        | GeomRect of
          x0: float<'p> array * y0: float<'p> array * x1: float<'p> array *
          y1: float<'p> array

        | GeomPolygon of xs: float<'p> array array * ys: float<'p> array array

        | GeomSegment of x1: float<'p> array * y1: float<'p> array

        | GeomText of
          labels: string array * anchor: TextAnchor * baseline: TextBaseline *
          offset: Vec2<px>

        | GeomBox of spans: BoxSpan<'p> array * widthFraction: float

        | GeomViolin of profiles: ViolinProfile<'p> array * widthFraction: float

        | GeomImage of image: Image * bounds: Rect<'p> * smooth: bool

        | GeomField of field: FieldPayload<'p> * mark: FieldMark

        | GeomRule of RuleSpec<'p>

        | GeomAnnotation of
          text: string * placement: TextPlacement<'p> * arrow: ArrowStyle

    type Geom = GeomOf<1>

    [<RequireQualifiedAccess>]
    module Geom =

        val style: theme: Theme -> color: Color -> GeomStyle

        val styleFor: theme: Theme -> i: int -> GeomStyle

        val ruleStyle: theme: Theme -> rule: RuleSpec<'p> -> GeomStyle

        val annotationStyle: theme: Theme -> GeomStyle

        val styleOf: theme: Theme -> i: int -> geom: GeomOf<'p> -> GeomStyle

        val withColor: c: Color -> s: GeomStyle -> GeomStyle

        val withWidth: w: float<px> -> s: GeomStyle -> GeomStyle

        val withDash: d: float<px> array -> s: GeomStyle -> GeomStyle

        val withOpacity: o: float -> s: GeomStyle -> GeomStyle

        val withShape: sh: MarkerShape -> s: GeomStyle -> GeomStyle

        val withMarkerSize: sz: float<px> -> s: GeomStyle -> GeomStyle

        val withFillOpacity: o: float -> s: GeomStyle -> GeomStyle

        val withFont: f: FontSpec -> s: GeomStyle -> GeomStyle

        val baselineOf: scale: Scale<'u> -> float<nd>

        val errorY: s: Series<'ux,'uy> -> err: float<'uy> array -> Geom

        val errorYBetween:
          s: Series<'ux,'uy> ->
            low: float<'uy> array -> high: float<'uy> array -> Geom

        val errorX: s: Series<'ux,'uy> -> err: float<'ux> array -> Geom

        val area: _s: Series<'ux,'uy> -> Geom

        val bar: widthFraction: float -> _s: Series<'ux,'uy> -> Geom

        val ribbon:
          s: Series<'ux,'uy> ->
            low: float<'uy> array -> high: float<'uy> array -> Geom

        val ribbonAround: s: Series<'ux,'uy> -> err: float<'uy> array -> Geom

        val rect:
          s: Series<'ux,'uy> ->
            x0: float<'ux> array ->
            y0: float<'uy> array ->
            x1: float<'ux> array -> y1: float<'uy> array -> Geom

        val tile: w: float<'ux> -> h: float<'uy> -> s: Series<'ux,'uy> -> Geom

        val image:
          _s: Series<'ux,'uy> ->
            img: Image ->
            x0: float<'ux> ->
            y0: float<'uy> ->
            x1: float<'ux> -> y1: float<'uy> -> smooth: bool -> Geom

        val segment:
          s: Series<'ux,'uy> ->
            x2: float<'ux> array -> y2: float<'uy> array -> Geom

        val text: labels: string array -> GeomOf<'p>

        val textAt:
          anchor: TextAnchor ->
            baseline: TextBaseline ->
            offset: Vec2<px> -> labels: string array -> GeomOf<'p>

        val boxplot:
          widthFraction: float ->
            s: Series<'ux,'uy> -> summaries: Stat.Summary<'uy> array -> Geom

        val violin:
          widthFraction: float ->
            s: Series<'ux,'uy> -> densities: Stat.Density<'uy> array -> Geom

        val project:
          fx: (float -> float<nd>) ->
            fy: (float -> float<nd>) -> geom: Geom -> GeomOf<nd>

        val onSeries: s: Series<'ux,'uy> -> geom: Geom -> GeomOf<nd>

        val compileField:
          coord: Coord ->
            plotSize: Extent<px> ->
            measurer: ITextMeasurer ->
            background: Color ->
            st: GeomStyle ->
            colourOf: (float -> Color option) ->
            field: FieldPayload<nd> -> mark: FieldMark -> Node

        val compileRule:
          coord: Coord -> st: GeomStyle -> rule: RuleSpec<nd> -> Node option

        val compileAnnotation:
          coord: Coord ->
            plotSize: Extent<px> ->
            st: GeomStyle ->
            text: string ->
            placement: TextPlacement<nd> ->
            arrow: ArrowStyle -> anchor: Vec2<nd> -> Node option

        val compileFor:
          address: ((int -> Key) * int array) option ->
            indices: int array option ->
            coord: Coord ->
            plotSize: Extent<px> ->
            st: GeomStyle -> geom: GeomOf<nd> -> all: Vec2<nd> array -> Node

        val compile:
          coord: Coord ->
            plotSize: Extent<px> ->
            st: GeomStyle -> geom: GeomOf<nd> -> pts: Vec2<nd> array -> Node

        val compileSubset:
          coord: Coord ->
            plotSize: Extent<px> ->
            st: GeomStyle ->
            geom: GeomOf<nd> ->
            all: Vec2<nd> array -> indices: int array -> Node

        val legendMark: geom: GeomOf<'p> -> st: GeomStyle -> LegendMark

namespace Marey

    [<NoComparison>]
    type ChannelMap =

        | ColorByLevel of levels: string array * colors: Color array

        | ShapeByLevel of levels: string array * shapes: MarkerShape array

        | ColorByValue of scale: PackedScale * ramp: Ramp

        | SizeByValue of
          scale: PackedScale * smallest: float<px> * largest: float<px>

    [<NoComparison>]
    type Channel =
        {

          Label: string

          Values: float array
          Map: ChannelMap
        }

    [<RequireQualifiedAccess>]
    module Channel =

        [<Literal>]
        val ColorSteps: int = 256

        val SizeStep: float<px>

        val sizeAt:
          smallest: float<px> -> largest: float<px> -> t: float -> float<px>

        val quantiseSize: size: float<px> -> float<px>

        val erasedScale: packed: PackedScale -> IScaleErased

        val select: rows: int array -> c: Channel -> Channel

        val bucketCount: map: ChannelMap -> int

        val bucketer: map: ChannelMap -> (float -> int)

        val bucketOf: map: ChannelMap -> value: float -> int

        val stepMidpoint: steps: int -> i: int -> float

        val restyle:
          map: ChannelMap -> bucket: int -> st: GeomStyle -> GeomStyle

        val levelsOf: ch: Channel -> string array option

        val isCategorical: ch: Channel -> bool

        [<NoComparison>]
        type Batch =
            {

              Indices: int array

              Restyle: (GeomStyle -> GeomStyle)
            }

        val split:
          channels: Channel array -> n: int -> Channel.Batch array * int

        val levelColors: theme: Theme -> n: int -> Color array

        val levelShapes: theme: Theme -> n: int -> MarkerShape array

        val colorByLevel:
          theme: Theme -> levels: string array -> values: float array -> Channel

        val shapeByLevel:
          theme: Theme -> levels: string array -> values: float array -> Channel

        val colorByValue:
          label: string ->
            ramp: Ramp -> scale: Scale<'u> -> values: float<'u> array -> Channel

        val sizeByValue:
          label: string ->
            smallest: float<px> ->
            largest: float<px> ->
            scale: Scale<'u> -> values: float<'u> array -> Channel

namespace Marey

    [<RequireQualifiedAccess>]
    module Colorbar =

        val measure:
          m: ITextMeasurer ->
            theme: Theme -> spec: AxisSpec -> plan: TickPlan -> float<px>

        val labelOf: label: string -> packed: PackedScale -> string

        val specOf:
          style: UnitStyle ->
            tickCount: int -> ch: Channel -> (AxisSpec * Ramp) option

        val planTicks:
          m: ITextMeasurer ->
            theme: Theme -> height: float<px> -> spec: AxisSpec -> TickPlan

        val steps: height: float<px> -> int

        val compile:
          m: ITextMeasurer ->
            theme: Theme ->
            extent: Extent<px> ->
            column: Rect<px> ->
            spec: AxisSpec -> plan: TickPlan -> ramp: Ramp -> Node

namespace Marey

    [<NoComparison>]
    type Layer =
        {
          Name: string
          Data: PackedSeries
          Geom: Geom
          Style: GeomStyle

          Channels: Channel array
          ShowInLegend: bool

          Addressed: int array

          RowKeys: string array

          InDomain: bool * bool

          SecondaryY: bool
        }

    [<Sealed>]
    type SecondaryMap =

        new: forward: (float -> float) * inverse: (float -> float) ->
               SecondaryMap

        member Forward: value: float -> float

        member Inverse: value: float -> float

        override ToString: unit -> string

    [<NoComparison>]
    type SecondaryAxis =
        {

          Unit: U<1>

          Map: SecondaryMap option

          Label: string option
        }

    [<NoComparison>]
    type AxisConfig =
        {

          Label: string option

          Style: UnitStyle option
          TickCount: int

          Grid: bool option
          Visible: bool
          ShowTitle: bool

          Ticks: TickSpec

          TickUnit: U<1>

          Prefix: Prefix option

          Reversed: bool

          Minor: MinorSpec

          Format: TickFormat

          ShowTickLabels: bool

          Secondary: SecondaryAxis option

          ShowTicks: bool
        }

    type AxisLook =

        | AxisFull

        | AxisUntitled

        | AxisUnlabelled

        | AxisBare

        | AxisHidden

    [<RequireQualifiedAccess>]
    type Aspect =

        | Equal

        | Box of heightOverWidth: float

    [<NoComparison>]
    type Panel =
        {
          Title: string

          Row: int
          Col: int

          RowSpan: int
          ColSpan: int

          XShare: int

          YShare: int

          Y2Axis: AxisConfig option

          Aspect: Aspect option

          Insets: Inset array
          Layers: Layer array
          XAxis: AxisConfig
          YAxis: AxisConfig

          Coord: Coord
          Legend: LegendPlacement

          LegendColumns: int

          LegendFrame: bool option
        }

    and [<NoComparison>] Inset =
        {
          Panel: Panel
          At: Rect<nd>

          Indicate: bool
        }

    type LetterStyle =

        | LetterParen

        | LetterPlain

        | LetterRightParen

        | LetterUpper

    [<NoComparison>]
    type PanelLetters =
        {
          Letter: LetterStyle
          Bold: bool
        }

    [<NoComparison>]
    type Figure =
        {
          Title: string
          Subtitle: string
          Rows: int
          Cols: int
          Panels: Panel array
          Theme: Theme

          Letters: PanelLetters option

          Legend: FigureLegend option

          RowWeights: float array
          ColWeights: float array
        }

    type FacetScales =

        | ShareBoth

        | ShareX

        | ShareY

    type PanelLayout =
        {
          TitleH: float<px>
          TopPad: float<px>
          XTicksH: float<px>
          XTitleH: float<px>
          YTitleW: float<px>
          YTicksW: float<px>
          RightPad: float<px>
          BarGap: float<px>
          BarW: float<px>

          LegendGap: float<px>
          LegendW: float<px>
          LegendH: float<px>

          LegendScores: float array

          Probe: Extent<px>

          XPrefix: Prefix option
          YPrefix: Prefix option

          SlackLeft: float<px>
          SlackRight: float<px>
          SlackTop: float<px>
          SlackBottom: float<px>

          Y2TicksW: float<px>
          Y2TitleW: float<px>

          Y2Prefix: Prefix option

          X2TicksH: float<px>
          X2TitleH: float<px>
        }

    [<RequireQualifiedAccess>]
    module PanelLayout =

        val empty: PanelLayout

        val union: a: PanelLayout -> b: PanelLayout -> PanelLayout

        val left: g: PanelLayout -> float<px>

        val right: g: PanelLayout -> float<px>

        val top: g: PanelLayout -> float<px>

        val bottom: g: PanelLayout -> float<px>

    [<NoComparison>]
    type Shot =
        {

          Outer: Arrangement

          Panels: (string * PanelLayout) array

          Samples: int

          ColourDomains: (string * (float * float)) array

          QuiverKeys: (string * QuiverKey) array
        }

    [<NoEquality; NoComparison>]
    type PanelView =
        {
          Row: int
          Col: int

          Plot: Rect<px>
          Coord: Coord
          X: IScaleErased
          Y: IScaleErased

          XAxis: AxisSpec
          YAxis: AxisSpec

          Y2: IScaleErased option
          Y2Axis: AxisSpec option

          EqualAspect: bool

          Inset: int option
        }

    [<RequireQualifiedAccess>]
    module PanelView =

        val contains: p: Vec2<px> -> v: PanelView -> bool

        val toUnit: p: Vec2<px> -> v: PanelView -> Vec2<nd> option

        val toData: p: Vec2<px> -> v: PanelView -> (float * float) option

        val fitEqual:
          v: PanelView ->
            dx: (float * float) * dy: (float * float) ->
              (float * float) * (float * float)

        val toY2: p: Vec2<px> -> v: PanelView -> float option

        val y2DomainOf: r: Rect<px> -> v: PanelView -> (float * float) option

        val domainsOf:
          r: Rect<px> ->
            v: PanelView -> ((float * float) * (float * float)) option

        val read: p: Vec2<px> -> v: PanelView -> string array option

    [<RequireQualifiedAccess>]
    module Figure =

        val markKey: l: Layer -> layerKey: Key -> row: int -> Key

        val packedScales: p: PackedSeries -> PackedScale * PackedScale

        val secondaryOf: p: Panel -> Panel option

        val axisScales: p: Panel -> PackedScale * PackedScale

        val axisScalesErased: p: Panel -> IScaleErased * IScaleErased

        val withDomains:
          dx: (float * float) option ->
            dy: (float * float) option -> p: PackedSeries -> PackedSeries

        val withKinds:
          kx: ScaleKind option ->
            ky: ScaleKind option -> p: PackedSeries -> PackedSeries

        val axisConfig: AxisConfig

        val withLook: look: AxisLook -> cfg: AxisConfig -> AxisConfig

        val panelOf: layers: Layer array -> Panel

        val create: panels: Panel array -> Figure

        val unify: p: Panel -> Panel

        val shareScales: mode: FacetScales -> fig: Figure -> Figure

        val shareRecorded: mode: FacetScales -> fig: Figure -> Figure

        val withDomainsShared:
          target: (Panel -> bool) ->
            dx: (float * float) option ->
            dy: (float * float) option -> fig: Figure -> Figure

        val offsetShares: a: Figure -> b: Figure -> Figure

        val arrangeFor:
          m: ITextMeasurer -> extent: Extent<px> -> fig: Figure -> Shot

        val arrangeOver:
          m: ITextMeasurer -> extent: Extent<px> -> raw: Figure array -> Shot

        val arrangeUnion:
          samples: int ->
            m: ITextMeasurer ->
            extent: Extent<px> -> sample: (float -> Figure) -> Shot

        val fitsIn: m: ITextMeasurer -> shot: Shot -> fig: Figure -> bool

        val panelsIn:
          m: ITextMeasurer -> shot: Shot -> fig: Figure -> PanelView array

        val withInsetDomains:
          row: int * col: int * inset: int ->
            dx: (float * float) option ->
            dy: (float * float) option -> fig: Figure -> Figure

        val withPanelDomainY2:
          row: int * col: int -> lo: float * hi: float -> fig: Figure -> Figure

        val withPanelDomains:
          row: int * col: int ->
            dx: (float * float) option ->
            dy: (float * float) option -> fig: Figure -> Figure

        val toSceneIn: m: ITextMeasurer -> shot: Shot -> fig: Figure -> Node

        val toScene:
          m: ITextMeasurer -> extent: Extent<px> -> fig: Figure -> Node

        val arrangement:
          m: ITextMeasurer -> extent: Extent<px> -> fig: Figure -> Arrangement

namespace Marey

    type Levels<'k when 'k: equality> =
        {

          Values: 'k array

          Labels: string array
        }

    [<RequireQualifiedAccess>]
    module Levels =

        val ofSeqWith:
          label: ('k -> string) -> keys: 'k seq -> Levels<'k> when 'k: equality

        val ofSeq: keys: 'k seq -> Levels<'k> when 'k: equality

        val sorted: ls: Levels<'k> -> Levels<'k> when 'k: comparison

        val reverse: ls: Levels<'k> -> Levels<'k> when 'k: equality

        val count: ls: Levels<'k> -> int when 'k: equality

        val tryIndexOf: ls: Levels<'k> -> k: 'k -> int when 'k: equality

        val positions:
          ls: Levels<'k> -> keys: 'k array -> float array when 'k: equality

        val scale: ls: Levels<'k> -> Scale<1> when 'k: equality

namespace Marey

    [<RequireQualifiedAccess>]
    module Chart =

        val size: w: float -> h: float -> Extent<px>

        val defaultSize: Extent<px>

        val ofSeries: g: Geom -> s: Series<'ux,'uy> -> Figure

        val ofPacked: g: Geom -> p: PackedSeries -> Figure

        val overlay: top: Figure -> fig: Figure -> Figure

        val add: top: Figure -> fig: Figure -> Figure

        val histogramSeries:
          name: string -> h: Stat.Histogram<'u> -> Series<'u,1> * Geom

        val densitySeries:
          name: string -> d: Stat.Density<'u> -> Series<'u,/'u> * Geom

        val ecdfSeries: name: string -> e: Stat.Ecdf<'u> -> Series<'u,1> * Geom

        val ofHistogram: h: Stat.Histogram<'u> -> Figure

        val ofDensity: d: Stat.Density<'u> -> Figure

        val ofEcdf: e: Stat.Ecdf<'u> -> Figure

        [<System.Obsolete
          ("Chart.Plain is retired (D109). Use Chart.Line(xs, ys), which leaves the units unspecified, or pass U.one where the data is dimensionless.");
          RequireQualifiedAccess>]
        module Plain =

            val line: x: float array -> y: float array -> Figure

            val scatter: x: float array -> y: float array -> Figure

            val area: x: float array -> y: float array -> Figure

            val bar: x: float array -> y: float array -> Figure

            val errorbar:
              x: float array -> y: float array -> err: float array -> Figure

            val addLine:
              x: float array -> y: float array -> fig: Figure -> Figure

            val addScatter:
              x: float array -> y: float array -> fig: Figure -> Figure

            val addArea:
              x: float array -> y: float array -> fig: Figure -> Figure

            val addErrorbar:
              x: float array ->
                y: float array -> err: float array -> fig: Figure -> Figure

            val histogram: x: float array -> Figure

            val histogramWith: bins: int -> x: float array -> Figure

            val density: x: float array -> Figure

            val ecdf: x: float array -> Figure

            val addFit:
              x: float array -> y: float array -> fig: Figure -> Figure

            val step: x: float array -> y: float array -> Figure

            val stepWith:
              align: StepAlign -> x: float array -> y: float array -> Figure

            val ribbon:
              x: float array -> low: float array -> high: float array -> Figure

            val tile:
              w: float -> h: float -> x: float array -> y: float array -> Figure

            val segment:
              x0: float array ->
                y0: float array -> x1: float array -> y1: float array -> Figure

            val text:
              x: float array -> y: float array -> labels: string array -> Figure

            val addStep:
              x: float array -> y: float array -> fig: Figure -> Figure

            val addRibbon:
              x: float array ->
                low: float array -> high: float array -> fig: Figure -> Figure

            val addSegment:
              x0: float array ->
                y0: float array ->
                x1: float array -> y1: float array -> fig: Figure -> Figure

        [<RequireQualifiedAccess>]
        module Category =

            val count: keys: 'k array -> Figure when 'k: equality

        [<AbstractClass; Sealed; Class>]
        type Category =

            static member
              Bar: keys: 'k array * values: float array *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] yUnit: IUnit -> Figure when 'k: equality

            static member
              Boxplot: keys: 'k array * values: float array *
                       [<System.Runtime.InteropServices.Optional;
                         System.Runtime.InteropServices.DefaultParameterValue
                         ((null :> obj))>] yUnit: IUnit -> Figure
                         when 'k: equality

            static member
              Scatter: keys: 'k array * values: float array *
                       [<System.Runtime.InteropServices.Optional;
                         System.Runtime.InteropServices.DefaultParameterValue
                         ((null :> obj))>] yUnit: IUnit -> Figure
                         when 'k: equality

            static member
              Series: name: string * keys: 'k array * values: float array *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] yUnit: IUnit ->
                        Levels<'k> * Series<1,1> when 'k: equality

            static member
              Violin: keys: 'k array * values: float array *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] yUnit: IUnit -> Figure
                        when 'k: equality

        val withTitle: t: string -> fig: Figure -> Figure

        val withSubtitle: t: string -> fig: Figure -> Figure

        val withTheme: t: Theme -> fig: Figure -> Figure

        val withName: n: string -> fig: Figure -> Figure

        val withColor: c: Color -> fig: Figure -> Figure

        val withStyle: f: (GeomStyle -> GeomStyle) -> fig: Figure -> Figure

        val withDash: d: float<px> array -> fig: Figure -> Figure

        val withMarker: s: MarkerShape -> fig: Figure -> Figure

        val addressMarks: rows: int array -> fig: Figure -> Figure

        val withKeys: keys: string array -> fig: Figure -> Figure

        val withOpacity: o: float -> fig: Figure -> Figure

        val withDomainIncluded: fig: Figure -> Figure

        val withTextOffset:
          dx: float<px> -> dy: float<px> -> fig: Figure -> Figure

        val withTextAt: x: float -> y: float -> fig: Figure -> Figure

        val withArrow: arrow: ArrowStyle -> fig: Figure -> Figure

        val addPanelText:
          x: float -> y: float -> text: string -> fig: Figure -> Figure

        val withPanelLetters: letter: LetterStyle -> fig: Figure -> Figure

        val withPanelLettersWith:
          letter: LetterStyle -> bold: bool -> fig: Figure -> Figure

        val addLabels: labels: string array -> fig: Figure -> Figure

        val colorByKey:
          keys: 'k array -> fig: Figure -> Figure when 'k: equality

        val shapeByKey:
          keys: 'k array -> fig: Figure -> Figure when 'k: equality

        val withColorLabel: label: string -> fig: Figure -> Figure

        val ofHexbin: h: Stat.Hexbin<'ux,'uy> -> Figure

        val axis: label: string -> AxisConfig

        val withXAxis: cfg: AxisConfig -> fig: Figure -> Figure

        val withYAxis: cfg: AxisConfig -> fig: Figure -> Figure

        val withXLook: look: AxisLook -> fig: Figure -> Figure

        val withYLook: look: AxisLook -> fig: Figure -> Figure

        val withAxesLook: look: AxisLook -> fig: Figure -> Figure

        val withXLabel: l: string -> fig: Figure -> Figure

        val withYLabel: l: string -> fig: Figure -> Figure

        val withUnitStyle: s: UnitStyle -> fig: Figure -> Figure

        val withXLog: fig: Figure -> Figure

        val withYLog: fig: Figure -> Figure

        val withXScaleKind: k: ScaleKind -> fig: Figure -> Figure

        val withYScaleKind: k: ScaleKind -> fig: Figure -> Figure

        val withXDomain: lo: float -> hi: float -> fig: Figure -> Figure

        val withYDomain: lo: float -> hi: float -> fig: Figure -> Figure

        val withXTicksEvery: step: float -> fig: Figure -> Figure

        val withYTicksEvery: step: float -> fig: Figure -> Figure

        val withXTicksAt: values: float array -> fig: Figure -> Figure

        val withYTicksAt: values: float array -> fig: Figure -> Figure

        val withXTicksNamed:
          ticks: (float * string) array -> fig: Figure -> Figure

        val withYTicksNamed:
          ticks: (float * string) array -> fig: Figure -> Figure

        val withXPrefix: prefix: Prefix -> fig: Figure -> Figure

        val withYPrefix: prefix: Prefix -> fig: Figure -> Figure

        val withXReversed: fig: Figure -> Figure

        val withYReversed: fig: Figure -> Figure

        val withXMinorTicks: minor: MinorSpec -> fig: Figure -> Figure

        val withYMinorTicks: minor: MinorSpec -> fig: Figure -> Figure

        val withXTickFormat: format: TickFormat -> fig: Figure -> Figure

        val withYTickFormat: format: TickFormat -> fig: Figure -> Figure

        val withGrid: x: bool -> y: bool -> fig: Figure -> Figure

        val withLegend: placement: LegendPlacement -> fig: Figure -> Figure

        val withLegendColumns: columns: int -> fig: Figure -> Figure

        val withLegendFrame: framed: bool -> fig: Figure -> Figure

        val withFigureLegend: placement: FigureLegend -> fig: Figure -> Figure

        val withCoord: c: Coord -> fig: Figure -> Figure

        val withPanelTitle: t: string -> fig: Figure -> Figure

        val beside: b: Figure -> a: Figure -> Figure

        val above: b: Figure -> a: Figure -> Figure

        val withRightAxis: right: Figure -> fig: Figure -> Figure

        val toRightAxis: fig: Figure -> Figure

        val withRightYDomain: lo: float -> hi: float -> fig: Figure -> Figure

        val withRightYLabel: label: string -> fig: Figure -> Figure

        val withEqualAspect: fig: Figure -> Figure

        val withBoxAspect: heightOverWidth: float -> fig: Figure -> Figure

        val withInset:
          x0: float * y0: float * x1: float * y1: float ->
            inset: Figure -> fig: Figure -> Figure

        val withInsetZoom: fig: Figure -> Figure

        val withTopAxisIn: u: U<'v> -> fig: Figure -> Figure

        val withRightAxisIn: u: U<'v> -> fig: Figure -> Figure

        val withTopAxisMapped:
          u: U<'v> ->
            forward: (float -> float) ->
            inverse: (float -> float) -> fig: Figure -> Figure

        val withRightAxisMapped:
          u: U<'v> ->
            forward: (float -> float) ->
            inverse: (float -> float) -> fig: Figure -> Figure

        val withTopAxisLabel: label: string -> fig: Figure -> Figure

        val shareX: fig: Figure -> Figure

        val shareY: fig: Figure -> Figure

        val shareAxes: fig: Figure -> Figure

        val withRowHeights: heights: float array -> fig: Figure -> Figure

        val withColWidths: widths: float array -> fig: Figure -> Figure

        val mosaic: layout: string -> parts: (char * Figure) list -> Figure

        val wrapCols: n: int -> int

        val facetWith:
          mode: FacetScales ->
            cols: int -> parts: (string * Figure) array -> Figure

        val facet: parts: (string * Figure) array -> Figure

        val facetIn: cols: int -> parts: (string * Figure) array -> Figure

        val toScene: extent: Extent<px> -> fig: Figure -> Node

        val toSceneWith:
          m: ITextMeasurer -> extent: Extent<px> -> fig: Figure -> Node

        val arrangeFor:
          m: ITextMeasurer -> extent: Extent<px> -> fig: Figure -> Shot

        val arrangeUnion:
          samples: int ->
            m: ITextMeasurer ->
            extent: Extent<px> -> sample: (float -> Figure) -> Shot

        val arrangeOver:
          m: ITextMeasurer -> extent: Extent<px> -> states: Figure array -> Shot

        val fitsIn: m: ITextMeasurer -> shot: Shot -> fig: Figure -> bool

        val toSceneIn: m: ITextMeasurer -> shot: Shot -> fig: Figure -> Node

        val panelsIn:
          m: ITextMeasurer -> shot: Shot -> fig: Figure -> PanelView array

        val withPanelDomains:
          row: int * col: int ->
            dx: (float * float) option ->
            dy: (float * float) option -> fig: Figure -> Figure

        val arrangement:
          m: ITextMeasurer -> extent: Extent<px> -> fig: Figure -> Arrangement

        val renderTo:
          backend: IBackend -> extent: Extent<px> -> fig: Figure -> unit

        val render:
          renderer: (Extent<px> -> Node -> 'a) ->
            extent: Extent<px> -> fig: Figure -> 'a

        val toFile:
          renderer: (Extent<px> -> Node -> string) ->
            path: string -> extent: Extent<px> -> fig: Figure -> unit

        val toSvg:
          renderer: (Extent<px> -> Node -> string) ->
            path: string -> extent: Extent<px> -> fig: Figure -> unit

        val toFileBytes:
          renderer: (Extent<px> -> Node -> byte array) ->
            path: string -> extent: Extent<px> -> fig: Figure -> unit

    [<AbstractClass; Sealed; Class>]
    type Chart =

        static member
          Annotation: x: float * y: float * text: string *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] xUnit: IUnit *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Area: x: float array * y: float array *
                [<System.Runtime.InteropServices.Optional;
                  System.Runtime.InteropServices.DefaultParameterValue
                  ((null :> obj))>] xUnit: IUnit *
                [<System.Runtime.InteropServices.Optional;
                  System.Runtime.InteropServices.DefaultParameterValue
                  ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Bar: x: float array * y: float array *
               [<System.Runtime.InteropServices.Optional;
                 System.Runtime.InteropServices.DefaultParameterValue
                 ((null :> obj))>] xUnit: IUnit *
               [<System.Runtime.InteropServices.Optional;
                 System.Runtime.InteropServices.DefaultParameterValue
                 ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          ColorBy: values: float array *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] unit: IUnit -> (Figure -> Figure)

        static member
          ColorByWith: ramp: Ramp * values: float array *
                       [<System.Runtime.InteropServices.Optional;
                         System.Runtime.InteropServices.DefaultParameterValue
                         ((null :> obj))>] unit: IUnit -> (Figure -> Figure)

        static member
          Density: x: float array *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] unit: IUnit -> Figure

        static member
          Ecdf: x: float array *
                [<System.Runtime.InteropServices.Optional;
                  System.Runtime.InteropServices.DefaultParameterValue
                  ((null :> obj))>] unit: IUnit -> Figure

        static member
          Errorbar: x: float array * y: float array * err: float array *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] xUnit: IUnit *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Fit: x: float array * y: float array *
               [<System.Runtime.InteropServices.Optional;
                 System.Runtime.InteropServices.DefaultParameterValue
                 ((null :> obj))>] xUnit: IUnit *
               [<System.Runtime.InteropServices.Optional;
                 System.Runtime.InteropServices.DefaultParameterValue
                 ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          HLine: y: float *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] unit: IUnit -> Figure

        static member
          HSpan: lo: float * hi: float *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] unit: IUnit -> Figure

        static member
          Heatmap: x: float array * y: float array * z: float array *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] xUnit: IUnit *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] yUnit: IUnit *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] valueUnit: IUnit -> Figure

        static member
          Hexbin: x: float array * y: float array *
                  [<System.Runtime.InteropServices.Optional;
                    System.Runtime.InteropServices.DefaultParameterValue
                    ((null :> obj))>] xUnit: IUnit *
                  [<System.Runtime.InteropServices.Optional;
                    System.Runtime.InteropServices.DefaultParameterValue
                    ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          HexbinWith: across: int * x: float array * y: float array *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] xUnit: IUnit *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Histogram: x: float array *
                     [<System.Runtime.InteropServices.Optional;
                       System.Runtime.InteropServices.DefaultParameterValue
                       ((null :> obj))>] unit: IUnit -> Figure

        static member
          HistogramWith: bins: int * x: float array *
                         [<System.Runtime.InteropServices.Optional;
                           System.Runtime.InteropServices.DefaultParameterValue
                           ((null :> obj))>] unit: IUnit -> Figure

        static member
          Image: img: Image * x0: float * y0: float * x1: float * y1: float *
                 smooth: bool *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] xUnit: IUnit *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Line: x: float array * y: float array *
                [<System.Runtime.InteropServices.Optional;
                  System.Runtime.InteropServices.DefaultParameterValue
                  ((null :> obj))>] xUnit: IUnit *
                [<System.Runtime.InteropServices.Optional;
                  System.Runtime.InteropServices.DefaultParameterValue
                  ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          LineThrough: x: float * y: float * slope: float *
                       [<System.Runtime.InteropServices.Optional;
                         System.Runtime.InteropServices.DefaultParameterValue
                         ((null :> obj))>] xUnit: IUnit *
                       [<System.Runtime.InteropServices.Optional;
                         System.Runtime.InteropServices.DefaultParameterValue
                         ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Loess: x: float array * y: float array *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] xUnit: IUnit *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          LoessWith: span: float * x: float array * y: float array *
                     [<System.Runtime.InteropServices.Optional;
                       System.Runtime.InteropServices.DefaultParameterValue
                       ((null :> obj))>] xUnit: IUnit *
                     [<System.Runtime.InteropServices.Optional;
                       System.Runtime.InteropServices.DefaultParameterValue
                       ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Ribbon: x: float array * low: float array * high: float array *
                  [<System.Runtime.InteropServices.Optional;
                    System.Runtime.InteropServices.DefaultParameterValue
                    ((null :> obj))>] xUnit: IUnit *
                  [<System.Runtime.InteropServices.Optional;
                    System.Runtime.InteropServices.DefaultParameterValue
                    ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Scatter: x: float array * y: float array *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] xUnit: IUnit *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Segment: x0: float array * y0: float array * x1: float array *
                   y1: float array *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] xUnit: IUnit *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Series: name: string * x: float array * y: float array *
                  [<System.Runtime.InteropServices.Optional;
                    System.Runtime.InteropServices.DefaultParameterValue
                    ((null :> obj))>] xUnit: IUnit *
                  [<System.Runtime.InteropServices.Optional;
                    System.Runtime.InteropServices.DefaultParameterValue
                    ((null :> obj))>] yUnit: IUnit -> Series<1,1>

        static member
          SizeBy: values: float array *
                  [<System.Runtime.InteropServices.Optional;
                    System.Runtime.InteropServices.DefaultParameterValue
                    ((null :> obj))>] unit: IUnit -> (Figure -> Figure)

        static member
          SizeByWith: smallest: float<px> * largest: float<px> *
                      values: float array *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] unit: IUnit -> (Figure -> Figure)

        static member
          Step: x: float array * y: float array *
                [<System.Runtime.InteropServices.Optional;
                  System.Runtime.InteropServices.DefaultParameterValue
                  ((null :> obj))>] xUnit: IUnit *
                [<System.Runtime.InteropServices.Optional;
                  System.Runtime.InteropServices.DefaultParameterValue
                  ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          StepWith: align: StepAlign * x: float array * y: float array *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] xUnit: IUnit *
                    [<System.Runtime.InteropServices.Optional;
                      System.Runtime.InteropServices.DefaultParameterValue
                      ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Text: x: float array * y: float array * labels: string array *
                [<System.Runtime.InteropServices.Optional;
                  System.Runtime.InteropServices.DefaultParameterValue
                  ((null :> obj))>] xUnit: IUnit *
                [<System.Runtime.InteropServices.Optional;
                  System.Runtime.InteropServices.DefaultParameterValue
                  ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          Tile: width: float * height: float * x: float array * y: float array *
                [<System.Runtime.InteropServices.Optional;
                  System.Runtime.InteropServices.DefaultParameterValue
                  ((null :> obj))>] xUnit: IUnit *
                [<System.Runtime.InteropServices.Optional;
                  System.Runtime.InteropServices.DefaultParameterValue
                  ((null :> obj))>] yUnit: IUnit -> Figure

        static member
          VLine: x: float *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] unit: IUnit -> Figure

        static member
          VSpan: lo: float * hi: float *
                 [<System.Runtime.InteropServices.Optional;
                   System.Runtime.InteropServices.DefaultParameterValue
                   ((null :> obj))>] unit: IUnit -> Figure

        static member
          WithColorDomain: lo: float * hi: float *
                           [<System.Runtime.InteropServices.Optional;
                             System.Runtime.InteropServices.DefaultParameterValue
                             ((null :> obj))>] unit: IUnit -> (Figure -> Figure)

namespace Marey

    [<NoComparison; NoEquality>]
    type StatSpec =
        | StatNone

        | StatFit of restyle: (GeomStyle -> GeomStyle)

        | StatHistogram of bins: int option

        | StatDensity of points: int option

        | StatEcdf

    [<NoComparison; NoEquality>]
    type FacetSpec =
        {

          Labels: string array

          Group: int array

          Cols: int option
        }

    type IGeomFor =

        abstract For: Series<'ux,'uy> -> Geom

        abstract NeedsZero: bool

    [<NoComparison; NoEquality>]
    type PlotSpec =
        {

          Name: string

          X: PackedChannel option
          Y: PackedChannel option

          Channels: (Theme -> Channel) list

          ColorLabel: string

          Layers: (IGeomFor * (GeomStyle -> GeomStyle)) list

          Stat: StatSpec

          Keys: string array option

          Facet: FacetSpec option

          FacetShare: FacetScales option

          Frame: Stat.Frame option
          Theme: Theme
          Coord: Coord
        }

    type PlotBuilderBase<'r> =

        new: rows: 'r array -> PlotBuilderBase<'r>

        [<CustomOperation ("area")>]
        member Area: spec: PlotSpec -> PlotSpec

        [<CustomOperation ("areaWith")>]
        member
          AreaWith: spec: PlotSpec * restyle: (GeomStyle -> GeomStyle) ->
                      PlotSpec

        [<CustomOperation ("bars")>]
        member Bars: spec: PlotSpec -> PlotSpec

        [<CustomOperation ("barsWith")>]
        member
          BarsWith: spec: PlotSpec * restyle: (GeomStyle -> GeomStyle) ->
                      PlotSpec

        [<CustomOperation ("color")>]
        member
          Color: spec: PlotSpec * key: ('r -> 'k) -> PlotSpec when 'k: equality

        [<CustomOperation ("colorBy")>]
        member
          ColorBy<[<Measure>] 'u> : spec: PlotSpec * u: U<'u> *
                                    f: ('r -> float<'u>) -> PlotSpec

        [<CustomOperation ("colorByWith")>]
        member
          ColorByWith<[<Measure>] 'u> : spec: PlotSpec * ramp: Ramp * u: U<'u> *
                                        f: ('r -> float<'u>) -> PlotSpec

        [<CustomOperation ("colorLabel")>]
        member ColorLabel: spec: PlotSpec * label: string -> PlotSpec

        [<CustomOperation ("coord")>]
        member Coord: spec: PlotSpec * c: Coord -> PlotSpec

        member Delay: f: (unit -> PlotSpec) -> PlotSpec

        [<CustomOperation ("density")>]
        member Density: spec: PlotSpec -> PlotSpec

        [<CustomOperation ("densityWith")>]
        member DensityWith: spec: PlotSpec * points: int -> PlotSpec

        [<CustomOperation ("ecdf")>]
        member Ecdf: spec: PlotSpec -> PlotSpec

        [<CustomOperation ("facetScales")>]
        member FacetScales: spec: PlotSpec * mode: FacetScales -> PlotSpec

        [<CustomOperation ("facetWrap")>]
        member
          FacetWrap: spec: PlotSpec * key: ('r -> 'k) -> PlotSpec
                       when 'k: equality

        [<CustomOperation ("facetWrapIn")>]
        member
          FacetWrapIn: spec: PlotSpec * cols: int * key: ('r -> 'k) -> PlotSpec
                         when 'k: equality

        [<CustomOperation ("fit")>]
        member Fit: spec: PlotSpec -> PlotSpec

        [<CustomOperation ("fitWith")>]
        member
          FitWith: spec: PlotSpec * restyle: (GeomStyle -> GeomStyle) ->
                     PlotSpec

        [<CustomOperation ("histogram")>]
        member Histogram: spec: PlotSpec -> PlotSpec

        [<CustomOperation ("histogramWith")>]
        member HistogramWith: spec: PlotSpec * bins: int -> PlotSpec

        [<CustomOperation ("key")>]
        member Key: spec: PlotSpec * f: ('r -> 'k) -> PlotSpec

        [<CustomOperation ("layer")>]
        member Layer: spec: PlotSpec * g: Geom -> PlotSpec

        [<CustomOperation ("layerWith")>]
        member
          LayerWith: spec: PlotSpec * g: Geom *
                     restyle: (GeomStyle -> GeomStyle) -> PlotSpec

        [<CustomOperation ("name")>]
        member Name: spec: PlotSpec * n: string -> PlotSpec

        member Run: spec: PlotSpec -> Figure

        [<CustomOperation ("shape")>]
        member
          Shape: spec: PlotSpec * key: ('r -> 'k) -> PlotSpec when 'k: equality

        [<CustomOperation ("size")>]
        member
          Size<[<Measure>] 'u> : spec: PlotSpec * u: U<'u> *
                                 f: ('r -> float<'u>) -> PlotSpec

        [<CustomOperation ("sizeWith")>]
        member
          SizeWith<[<Measure>] 'u> : spec: PlotSpec * smallest: float<px> *
                                     largest: float<px> * u: U<'u> *
                                     f: ('r -> float<'u>) -> PlotSpec

        [<CustomOperation ("theme")>]
        member Theme: spec: PlotSpec * t: Theme -> PlotSpec

        [<CustomOperation ("x")>]
        member
          X<[<Measure>] 'u> : spec: PlotSpec * u: U<'u> * f: ('r -> float<'u>) ->
                                PlotSpec

        [<CustomOperation ("y")>]
        member
          Y<[<Measure>] 'u> : spec: PlotSpec * u: U<'u> * f: ('r -> float<'u>) ->
                                PlotSpec

        member Yield: unit -> PlotSpec

        member Zero: unit -> PlotSpec

        member Rows: 'r array

    type PlotBuilder<'r> =
        inherit PlotBuilderBase<'r>

        new: rows: 'r array -> PlotBuilder<'r>

        [<CustomOperation ("colorBy")>]
        member ColorBy: spec: PlotSpec * f: ('r -> float) -> PlotSpec

        [<CustomOperation ("colorByWith")>]
        member
          ColorByWith: spec: PlotSpec * ramp: Ramp * f: ('r -> float) ->
                         PlotSpec

        [<CustomOperation ("size")>]
        member Size: spec: PlotSpec * f: ('r -> float) -> PlotSpec

        [<CustomOperation ("sizeWith")>]
        member
          SizeWith: spec: PlotSpec * smallest: float<px> * largest: float<px> *
                    f: ('r -> float) -> PlotSpec

        [<CustomOperation ("x")>]
        member X: spec: PlotSpec * f: ('r -> float) -> PlotSpec

        [<CustomOperation ("y")>]
        member Y: spec: PlotSpec * f: ('r -> float) -> PlotSpec

    [<AutoOpen>]
    module Grammar =

        val plot: data: 'r seq -> PlotBuilder<'r>

namespace Marey

    [<NoComparison; NoEquality>]
    type Stage =
        {
          Figure: Figure option
          Edits: (Key * (Node -> Node)) array
        }

    [<RequireQualifiedAccess>]
    module Stage =

        val empty: Stage

    [<RequireQualifiedAccess>]
    type MarkRef =
        | Row of int
        | Datum of string

    [<NoComparison>]
    type Handle =
        {

          Key: Key

          Introduced: float<Data.UnitSystems.SI.UnitSymbols.s>

          MarkLayer: Key option

          Mark: (Key * MarkRef) option

          MarkKeys: string array
        }

        member Item: row: int -> Handle with get

        member Item: key: string -> Handle with get

    [<NoComparison; NoEquality>]
    type Story<'a> = | Story of (Stage -> 'a * Anim<(Stage -> Stage)>)

    [<NoComparison; NoEquality>]
    type Step<'a> =
        {
          Default: float<Data.UnitSystems.SI.UnitSymbols.s>
          Build: (float<Data.UnitSystems.SI.UnitSymbols.s> -> Story<'a>)
        }

    module Storyboard =

        [<Sealed>]
        type Builder =

            new: unit -> Builder

            member Bind: s: Step<'a> * f: ('a -> Story<'b>) -> Story<'b>

            member Bind: m: Story<'a> * f: ('a -> Story<'b>) -> Story<'b>

            member Combine: a: Story<unit> * b: Story<'a> -> Story<'a>

            member Delay: f: (unit -> Story<'a>) -> Story<'a>

            member Return: v: 'a -> Story<'a>

            member ReturnFrom: s: Step<'a> -> Story<'a>

            member ReturnFrom: m: Story<'a> -> Story<'a>

            member Zero: unit -> Story<unit>

        val scene: Builder

        val over:
          d: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            step: Step<'a> -> Story<'a>

        val wait: d: float<Data.UnitSystems.SI.UnitSymbols.s> -> Story<unit>

        val ease: f: (float -> float) -> story: Story<'a> -> Story<'a>

        val parallel': branches: Story<unit> list -> Story<unit>

        val draw: fig: Figure -> Step<Handle>

        val fadeIn: h: Handle -> Step<unit>

        val morphTo: target: Figure -> h: Handle -> Step<unit>

        val zoomToX: float * float -> h: Handle -> Step<unit>

        val zoomToY: float * float -> h: Handle -> Step<unit>

        val highlight: h: Handle -> Step<unit>

        val stages: story: Story<unit> -> Anim<Stage>

        val duration:
          story: Story<unit> -> float<Data.UnitSystems.SI.UnitSymbols.s>

        val shoot:
          samples: int ->
            m: ITextMeasurer ->
            extent: Extent<px> -> story: Story<unit> -> Shot * Anim<Node>

        val animateWith:
          samples: int ->
            m: ITextMeasurer ->
            extent: Extent<px> -> story: Story<unit> -> Anim<Node>

        val animate:
          samples: int -> extent: Extent<px> -> story: Story<unit> -> Anim<Node>

namespace Marey

    [<Sealed>]
    type MemoTextMeasurer =
        interface ILicConvolver
        interface ITextMeasurer

        new: inner: ITextMeasurer -> MemoTextMeasurer

        member Entries: int

        member Inner: ITextMeasurer

        member Measurements: int

    [<RequireQualifiedAccess>]
    type FramePass =

        | Serial

        | Concurrent

    [<NoComparison; NoEquality>]
    type Film =
        {

          Frames: Anim<Node>

          Shot: Shot
        }

        member Duration: float<Data.UnitSystems.SI.UnitSymbols.s>

        member Extent: Extent<px>

    [<RequireQualifiedAccess>]
    module Film =

        val ofFiguresWith:
          samples: int ->
            m: ITextMeasurer -> extent: Extent<px> -> anim: Anim<Figure> -> Film

        val ofFigures:
          samples: int -> extent: Extent<px> -> anim: Anim<Figure> -> Film

        val ofStoryWith:
          samples: int ->
            m: ITextMeasurer -> extent: Extent<px> -> story: Story<unit> -> Film

        val ofStory:
          samples: int -> extent: Extent<px> -> story: Story<unit> -> Film

    [<NoComparison>]
    type FrameTarget =
        {

          Directory: string

          Stem: string

          Extension: string

          Digits: int
        }

    [<RequireQualifiedAccess>]
    module FrameTarget =

        val into: directory: string -> extension: string -> FrameTarget

        val pattern: t: FrameTarget -> string

        val fileFor: t: FrameTarget -> index: int -> string

    [<NoComparison>]
    type FrameSequence =
        {
          Directory: string

          Pattern: string

          Files: string array
          Fps: float
          Extent: Extent<px>

          Duration: float<Data.UnitSystems.SI.UnitSymbols.s>
          Pass: FramePass

          Stale: string array
        }

    [<RequireQualifiedAccess>]
    module Frames =

        val count:
          fps: float ->
            duration: float<Data.UnitSystems.SI.UnitSymbols.s> -> int

        val times:
          fps: float ->
            duration: float<Data.UnitSystems.SI.UnitSymbols.s> ->
            float<Data.UnitSystems.SI.UnitSymbols.s> array

        val render:
          pass: FramePass ->
            renderer: (Extent<px> -> Node -> 'a) ->
            fps: float -> film: Film -> 'a array

        val write:
          pass: FramePass ->
            renderer: (Extent<px> -> Node -> byte array) ->
            target: FrameTarget -> fps: float -> film: Film -> FrameSequence

        val writeText:
          pass: FramePass ->
            renderer: (Extent<px> -> Node -> string) ->
            target: FrameTarget -> fps: float -> film: Film -> FrameSequence

namespace Marey.UnitsOfMeasure

    [<RequireQualifiedAccess>]
    module Chart =

        val series:
          name: string ->
            ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array -> y: float<'uy> array -> Marey.Series<'ux,'uy>

        val line:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array -> y: float<'uy> array -> Marey.Figure

        val scatter:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array -> y: float<'uy> array -> Marey.Figure

        val area:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array -> y: float<'uy> array -> Marey.Figure

        val bar:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array -> y: float<'uy> array -> Marey.Figure

        val errorbar:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array ->
            y: float<'uy> array -> err: float<'uy> array -> Marey.Figure

        val step:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array -> y: float<'uy> array -> Marey.Figure

        val stepWith:
          align: Marey.StepAlign ->
            ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array -> y: float<'uy> array -> Marey.Figure

        val ribbon:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array ->
            low: float<'uy> array -> high: float<'uy> array -> Marey.Figure

        val tile:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            w: float<'ux> ->
            h: float<'uy> ->
            x: float<'ux> array -> y: float<'uy> array -> Marey.Figure

        val image:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            img: Marey.Image ->
            x0: float<'ux> ->
            y0: float<'uy> ->
            x1: float<'ux> -> y1: float<'uy> -> smooth: bool -> Marey.Figure

        val segment:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x0: float<'ux> array ->
            y0: float<'uy> array ->
            x1: float<'ux> array -> y1: float<'uy> array -> Marey.Figure

        val text:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array ->
            y: float<'uy> array -> labels: string array -> Marey.Figure

        val addLine:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array ->
            y: float<'uy> array -> fig: Marey.Figure -> Marey.Figure

        val addScatter:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array ->
            y: float<'uy> array -> fig: Marey.Figure -> Marey.Figure

        val addArea:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array ->
            y: float<'uy> array -> fig: Marey.Figure -> Marey.Figure

        val addErrorbar:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array ->
            y: float<'uy> array ->
            err: float<'uy> array -> fig: Marey.Figure -> Marey.Figure

        val addStep:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array ->
            y: float<'uy> array -> fig: Marey.Figure -> Marey.Figure

        val addRibbon:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array ->
            low: float<'uy> array ->
            high: float<'uy> array -> fig: Marey.Figure -> Marey.Figure

        val addSegment:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x0: float<'ux> array ->
            y0: float<'uy> array ->
            x1: float<'ux> array ->
            y1: float<'uy> array -> fig: Marey.Figure -> Marey.Figure

        val histogram: u: Marey.U<'u> -> xs: float<'u> array -> Marey.Figure

        val histogramWith:
          u: Marey.U<'u> -> bins: int -> xs: float<'u> array -> Marey.Figure

        val density: u: Marey.U<'u> -> xs: float<'u> array -> Marey.Figure

        val ecdf: u: Marey.U<'u> -> xs: float<'u> array -> Marey.Figure

        val addFit:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array ->
            y: float<'uy> array -> fig: Marey.Figure -> Marey.Figure

        val addLoess:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array ->
            y: float<'uy> array -> fig: Marey.Figure -> Marey.Figure

        val addLoessWith:
          span: float ->
            ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array ->
            y: float<'uy> array -> fig: Marey.Figure -> Marey.Figure

        val fit:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array -> y: float<'uy> array -> Marey.Figure

        val loess:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array -> y: float<'uy> array -> Marey.Figure

        val loessWith:
          span: float ->
            ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array -> y: float<'uy> array -> Marey.Figure

        val colorByWith:
          ramp: Marey.Ramp ->
            u: Marey.U<'u> ->
            values: float<'u> array -> fig: Marey.Figure -> Marey.Figure

        val colorBy:
          u: Marey.U<'u> ->
            values: float<'u> array -> fig: Marey.Figure -> Marey.Figure

        val withColorDomain:
          u: Marey.U<'u> ->
            lo: float<'u> -> hi: float<'u> -> fig: Marey.Figure -> Marey.Figure

        val heatmap:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            uz: Marey.U<'uz> ->
            x: float<'ux> array ->
            y: float<'uy> array -> z: float<'uz> array -> Marey.Figure

        val hexbinWith:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            across: int ->
            x: float<'ux> array -> y: float<'uy> array -> Marey.Figure

        val hexbin:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> array -> y: float<'uy> array -> Marey.Figure

        val sizeByWith:
          smallest: float<Marey.px> ->
            largest: float<Marey.px> ->
            u: Marey.U<'u> ->
            values: float<'u> array -> fig: Marey.Figure -> Marey.Figure

        val sizeBy:
          u: Marey.U<'u> ->
            values: float<'u> array -> fig: Marey.Figure -> Marey.Figure

        val withTopAxisMapped:
          u: Marey.U<'v> ->
            forward: (float<'ux> -> float<'v>) ->
            inverse: (float<'v> -> float<'ux>) ->
            fig: Marey.Figure -> Marey.Figure

        val withRightAxisMapped:
          u: Marey.U<'v> ->
            forward: (float<'uy> -> float<'v>) ->
            inverse: (float<'v> -> float<'uy>) ->
            fig: Marey.Figure -> Marey.Figure

        val addHLine:
          uy: Marey.U<'uy> -> y: float<'uy> -> fig: Marey.Figure -> Marey.Figure

        val addVLine:
          ux: Marey.U<'ux> -> x: float<'ux> -> fig: Marey.Figure -> Marey.Figure

        val addHSpan:
          uy: Marey.U<'uy> ->
            lo: float<'uy> ->
            hi: float<'uy> -> fig: Marey.Figure -> Marey.Figure

        val addVSpan:
          ux: Marey.U<'ux> ->
            lo: float<'ux> ->
            hi: float<'ux> -> fig: Marey.Figure -> Marey.Figure

        val addLineThrough:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> ->
            y: float<'uy> ->
            slope: float<'uy/'ux> -> fig: Marey.Figure -> Marey.Figure

        val hLine: uy: Marey.U<'uy> -> y: float<'uy> -> Marey.Figure

        val vLine: ux: Marey.U<'ux> -> x: float<'ux> -> Marey.Figure

        val hSpan:
          uy: Marey.U<'uy> -> lo: float<'uy> -> hi: float<'uy> -> Marey.Figure

        val vSpan:
          ux: Marey.U<'ux> -> lo: float<'ux> -> hi: float<'ux> -> Marey.Figure

        val lineThrough:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> ->
            y: float<'uy> -> slope: float<'uy/'ux> -> Marey.Figure

        val annotate:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> ->
            y: float<'uy> -> text: string -> fig: Marey.Figure -> Marey.Figure

        val annotation:
          ux: Marey.U<'ux> ->
            uy: Marey.U<'uy> ->
            x: float<'ux> -> y: float<'uy> -> text: string -> Marey.Figure

        val withTextAt:
          _ux: Marey.U<'ux> ->
            _uy: Marey.U<'uy> ->
            x: float<'ux> -> y: float<'uy> -> fig: Marey.Figure -> Marey.Figure

        val withXTicksEvery:
          u: Marey.U<'u> -> step: float<'u> -> fig: Marey.Figure -> Marey.Figure

        val withYTicksEvery:
          u: Marey.U<'u> -> step: float<'u> -> fig: Marey.Figure -> Marey.Figure

        val withXTicksAt:
          u: Marey.U<'u> ->
            values: float<'u> array -> fig: Marey.Figure -> Marey.Figure

        val withYTicksAt:
          u: Marey.U<'u> ->
            values: float<'u> array -> fig: Marey.Figure -> Marey.Figure

        val withXTicksNamed:
          u: Marey.U<'u> ->
            ticks: (float<'u> * string) array ->
            fig: Marey.Figure -> Marey.Figure

        val withYTicksNamed:
          u: Marey.U<'u> ->
            ticks: (float<'u> * string) array ->
            fig: Marey.Figure -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so an annotation takes a witness per axis: Chart.annotate or Chart.annotation. Chart.Annotation(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Annotation: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a rule takes a witness: Chart.addHLine or Chart.hLine. Chart.HLine(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val HLine: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a rule takes a witness: Chart.addVLine or Chart.vLine. Chart.VLine(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val VLine: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a span takes a witness: Chart.addHSpan or Chart.hSpan. Chart.HSpan(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val HSpan: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a span takes a witness: Chart.addVSpan or Chart.vSpan. Chart.VSpan(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val VSpan: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a rule takes a witness per axis: Chart.addLineThrough or Chart.lineThrough. Chart.LineThrough(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val LineThrough: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.line. Chart.Line(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Line: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.scatter. Chart.Scatter(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Scatter: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.area. Chart.Area(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Area: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.bar. Chart.Bar(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Bar: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.errorbar. Chart.Errorbar(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Errorbar: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.step. Chart.Step(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Step: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.stepWith. Chart.StepWith(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val StepWith: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.ribbon. Chart.Ribbon(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Ribbon: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.tile. Chart.Tile(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Tile: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.image. Chart.Image(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Image: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.segment. Chart.Segment(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Segment: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.text. Chart.Text(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Text: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.fit. Chart.Fit(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Fit: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.loess. Chart.Loess(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Loess: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.loessWith. Chart.LoessWith(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val LoessWith: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.histogram. Chart.Histogram(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Histogram: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.histogramWith. Chart.HistogramWith(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val HistogramWith: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.density. Chart.Density(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Density: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.ecdf. Chart.Ecdf(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Ecdf: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.heatmap. Chart.Heatmap(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Heatmap: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.hexbin. Chart.Hexbin(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Hexbin: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.hexbinWith. Chart.HexbinWith(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val HexbinWith: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.series. Chart.Series(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val Series: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.colorBy. Chart.ColorBy(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val ColorBy: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.colorByWith. Chart.ColorByWith(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val ColorByWith: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.withColorDomain. Chart.WithColorDomain(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val WithColorDomain: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.sizeBy. Chart.SizeBy(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val SizeBy: obj -> Marey.Figure

        [<System.Obsolete
          ("Marey.UnitsOfMeasure is open, so a chart takes a witness per axis: Chart.sizeByWith. Chart.SizeByWith(…) is the unit-optional spelling, for code that does not open this namespace.",
           true)>]
        val SizeByWith: obj -> Marey.Figure

        [<RequireQualifiedAccess>]
        module Category =

            val series:
              name: string ->
                uy: Marey.U<'uy> ->
                keys: 'k array ->
                values: float<'uy> array ->
                Marey.Levels<'k> * Marey.Series<1,'uy> when 'k: equality

            val bar:
              uy: Marey.U<'uy> ->
                keys: 'k array -> values: float<'uy> array -> Marey.Figure
                when 'k: equality

            val scatter:
              uy: Marey.U<'uy> ->
                keys: 'k array -> values: float<'uy> array -> Marey.Figure
                when 'k: equality

            val boxplot:
              uy: Marey.U<'uy> ->
                keys: 'k array -> values: float<'uy> array -> Marey.Figure
                when 'k: equality

            val violin:
              uy: Marey.U<'uy> ->
                keys: 'k array -> values: float<'uy> array -> Marey.Figure
                when 'k: equality

            [<System.Obsolete
              ("Marey.UnitsOfMeasure is open, so a categorical chart takes the values' witness: Chart.Category.series. Chart.Category.Series(…) is the unit-optional spelling, for code that does not open this namespace.",
               true)>]
            val Series: obj -> Marey.Figure

            [<System.Obsolete
              ("Marey.UnitsOfMeasure is open, so a categorical chart takes the values' witness: Chart.Category.bar. Chart.Category.Bar(…) is the unit-optional spelling, for code that does not open this namespace.",
               true)>]
            val Bar: obj -> Marey.Figure

            [<System.Obsolete
              ("Marey.UnitsOfMeasure is open, so a categorical chart takes the values' witness: Chart.Category.scatter. Chart.Category.Scatter(…) is the unit-optional spelling, for code that does not open this namespace.",
               true)>]
            val Scatter: obj -> Marey.Figure

            [<System.Obsolete
              ("Marey.UnitsOfMeasure is open, so a categorical chart takes the values' witness: Chart.Category.boxplot. Chart.Category.Boxplot(…) is the unit-optional spelling, for code that does not open this namespace.",
               true)>]
            val Boxplot: obj -> Marey.Figure

            [<System.Obsolete
              ("Marey.UnitsOfMeasure is open, so a categorical chart takes the values' witness: Chart.Category.violin. Chart.Category.Violin(…) is the unit-optional spelling, for code that does not open this namespace.",
               true)>]
            val Violin: obj -> Marey.Figure

    type PlotBuilder<'r> =
        inherit Marey.PlotBuilderBase<'r>

        new: rows: 'r array -> PlotBuilder<'r>

    [<AutoOpen>]
    module Grammar =

        val plot: data: 'r seq -> PlotBuilder<'r>

namespace Marey.CSharp

    [<System.Runtime.CompilerServices.Extension; AbstractClass; Sealed; Class>]
    type FigureExtensions =

        [<System.Runtime.CompilerServices.Extension>]
        static member
          Above: fig: Marey.Figure * below: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member Add: fig: Marey.Figure * top: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          AddLabels: fig: Marey.Figure * labels: string array -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          AddPanelText: fig: Marey.Figure * x: float * y: float * text: string ->
                          Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          Beside: fig: Marey.Figure * right: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          ColorBy: fig: Marey.Figure * values: float array *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] unit: Marey.IUnit -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          ColorByKey: fig: Marey.Figure * keys: 'k array -> Marey.Figure
                        when 'k: equality

        [<System.Runtime.CompilerServices.Extension>]
        static member
          ColorByWith: fig: Marey.Figure * ramp: Marey.Ramp *
                       values: float array *
                       [<System.Runtime.InteropServices.Optional;
                         System.Runtime.InteropServices.DefaultParameterValue
                         ((null :> obj))>] unit: Marey.IUnit -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          Overlay: fig: Marey.Figure * top: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          Render: fig: Marey.Figure *
                  renderer: System.Func<Marey.Extent<Marey.px>,Marey.Node,'a> *
                  width: float * height: float -> 'a

        [<System.Runtime.CompilerServices.Extension>]
        static member
          Save: fig: Marey.Figure *
                renderer: System.Func<Marey.Extent<Marey.px>,Marey.Node,
                                      byte array> * path: string * width: float *
                height: float -> unit

        [<System.Runtime.CompilerServices.Extension>]
        static member
          Save: fig: Marey.Figure *
                renderer: System.Func<Marey.Extent<Marey.px>,Marey.Node,string> *
                path: string * width: float * height: float -> unit

        [<System.Runtime.CompilerServices.Extension>]
        static member
          ShapeByKey: fig: Marey.Figure * keys: 'k array -> Marey.Figure
                        when 'k: equality

        [<System.Runtime.CompilerServices.Extension>]
        static member ShareAxes: fig: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member ShareX: fig: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member ShareY: fig: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          SizeBy: fig: Marey.Figure * values: float array *
                  [<System.Runtime.InteropServices.Optional;
                    System.Runtime.InteropServices.DefaultParameterValue
                    ((null :> obj))>] unit: Marey.IUnit -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          SizeByWith: fig: Marey.Figure * smallest: float<Marey.px> *
                      largest: float<Marey.px> * values: float array *
                      [<System.Runtime.InteropServices.Optional;
                        System.Runtime.InteropServices.DefaultParameterValue
                        ((null :> obj))>] unit: Marey.IUnit -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          ToScene: fig: Marey.Figure * width: float * height: float ->
                     Marey.Node

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithArrow: fig: Marey.Figure * arrow: Marey.ArrowStyle -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithAxesLook: fig: Marey.Figure * look: Marey.AxisLook -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithBoxAspect: fig: Marey.Figure * heightOverWidth: float ->
                           Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithColWidths: fig: Marey.Figure *
                         [<System.ParamArray>] widths: float array ->
                           Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithColor: fig: Marey.Figure * color: Marey.Color -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithColorDomain: fig: Marey.Figure * lo: float * hi: float *
                           [<System.Runtime.InteropServices.Optional;
                             System.Runtime.InteropServices.DefaultParameterValue
                             ((null :> obj))>] unit: Marey.IUnit -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithColorLabel: fig: Marey.Figure * label: string -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithDash: fig: Marey.Figure * dash: float<Marey.px> array ->
                      Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member WithDomainIncluded: fig: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member WithEqualAspect: fig: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithFigureLegend: fig: Marey.Figure * placement: Marey.FigureLegend ->
                              Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithGrid: fig: Marey.Figure * x: bool * y: bool -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithInset: fig: Marey.Figure * x0: float * y0: float * x1: float *
                     y1: float * inset: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member WithInsetZoom: fig: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithKeys: fig: Marey.Figure * keys: string array -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithLegend: fig: Marey.Figure * placement: Marey.LegendPlacement ->
                        Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithLegendColumns: fig: Marey.Figure * columns: int -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithLegendFrame: fig: Marey.Figure * framed: bool -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithMarker: fig: Marey.Figure * shape: Marey.MarkerShape ->
                        Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member WithName: fig: Marey.Figure * name: string -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithOpacity: fig: Marey.Figure * opacity: float -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithPanelLetters: fig: Marey.Figure * letter: Marey.LetterStyle *
                            [<System.Runtime.InteropServices.Optional;
                              System.Runtime.InteropServices.DefaultParameterValue
                              ((true :> obj))>] bold: bool -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithPanelTitle: fig: Marey.Figure * title: string -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithRightAxis: fig: Marey.Figure * right: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithRightAxisIn: fig: Marey.Figure * unit: Marey.IUnit -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithRightAxisMapped: fig: Marey.Figure * unit: Marey.IUnit *
                               forward: System.Func<float,float> *
                               inverse: System.Func<float,float> -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithRightYDomain: fig: Marey.Figure * lo: float * hi: float ->
                              Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithRightYLabel: fig: Marey.Figure * label: string -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithRowHeights: fig: Marey.Figure *
                          [<System.ParamArray>] heights: float array ->
                            Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithStyle: fig: Marey.Figure *
                     restyle: System.Func<Marey.GeomStyle,Marey.GeomStyle> ->
                       Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithSubtitle: fig: Marey.Figure * subtitle: string -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithTextAt: fig: Marey.Figure * x: float * y: float -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithTextOffset: fig: Marey.Figure * dx: float * dy: float ->
                            Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithTheme: fig: Marey.Figure * theme: Marey.Theme -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithTitle: fig: Marey.Figure * title: string -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithTopAxisIn: fig: Marey.Figure * unit: Marey.IUnit -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithTopAxisMapped: fig: Marey.Figure * unit: Marey.IUnit *
                             forward: System.Func<float,float> *
                             inverse: System.Func<float,float> -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithUnitStyle: fig: Marey.Figure * style: Marey.UnitStyle ->
                           Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithXDomain: fig: Marey.Figure * lo: float * hi: float -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithXLabel: fig: Marey.Figure * label: string -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member WithXLog: fig: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithXLook: fig: Marey.Figure * look: Marey.AxisLook -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithXMinorTicks: fig: Marey.Figure * minor: Marey.MinorSpec ->
                             Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithXPrefix: fig: Marey.Figure * prefix: Marey.Prefix -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member WithXReversed: fig: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithXTickFormat: fig: Marey.Figure * format: System.Func<float,string> ->
                             Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithXTickFormat: fig: Marey.Figure * format: Marey.TickFormat ->
                             Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithXTicksAt: fig: Marey.Figure * values: float array *
                        [<System.Runtime.InteropServices.Optional;
                          System.Runtime.InteropServices.DefaultParameterValue
                          ((null :> obj))>] unit: Marey.IUnit -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithXTicksEvery: fig: Marey.Figure * step: float *
                           [<System.Runtime.InteropServices.Optional;
                             System.Runtime.InteropServices.DefaultParameterValue
                             ((null :> obj))>] unit: Marey.IUnit -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithXTicksNamed: fig: Marey.Figure * values: float array *
                           labels: string array *
                           [<System.Runtime.InteropServices.Optional;
                             System.Runtime.InteropServices.DefaultParameterValue
                             ((null :> obj))>] unit: Marey.IUnit -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithYDomain: fig: Marey.Figure * lo: float * hi: float -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithYLabel: fig: Marey.Figure * label: string -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member WithYLog: fig: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithYLook: fig: Marey.Figure * look: Marey.AxisLook -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithYMinorTicks: fig: Marey.Figure * minor: Marey.MinorSpec ->
                             Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithYPrefix: fig: Marey.Figure * prefix: Marey.Prefix -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member WithYReversed: fig: Marey.Figure -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithYTickFormat: fig: Marey.Figure * format: System.Func<float,string> ->
                             Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithYTickFormat: fig: Marey.Figure * format: Marey.TickFormat ->
                             Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithYTicksAt: fig: Marey.Figure * values: float array *
                        [<System.Runtime.InteropServices.Optional;
                          System.Runtime.InteropServices.DefaultParameterValue
                          ((null :> obj))>] unit: Marey.IUnit -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithYTicksEvery: fig: Marey.Figure * step: float *
                           [<System.Runtime.InteropServices.Optional;
                             System.Runtime.InteropServices.DefaultParameterValue
                             ((null :> obj))>] unit: Marey.IUnit -> Marey.Figure

        [<System.Runtime.CompilerServices.Extension>]
        static member
          WithYTicksNamed: fig: Marey.Figure * values: float array *
                           labels: string array *
                           [<System.Runtime.InteropServices.Optional;
                             System.Runtime.InteropServices.DefaultParameterValue
                             ((null :> obj))>] unit: Marey.IUnit -> Marey.Figure

    [<AbstractClass; Sealed; Class>]
    type Figures =

        static member
          Facet: titles: string array * parts: Marey.Figure array ->
                   Marey.Figure

        static member
          FacetIn: cols: int * titles: string array * parts: Marey.Figure array ->
                     Marey.Figure

        static member
          FacetWith: mode: Marey.FacetScales * cols: int * titles: string array *
                     parts: Marey.Figure array -> Marey.Figure

        static member
          Mosaic: layout: string * names: string *
                  [<System.ParamArray>] parts: Marey.Figure array ->
                    Marey.Figure

    [<AbstractClass; Sealed; Class>]
    type Category =

        static member
          Bar: keys: 'k array * values: float array *
               [<System.Runtime.InteropServices.Optional;
                 System.Runtime.InteropServices.DefaultParameterValue
                 ((null :> obj))>] yUnit: Marey.IUnit -> Marey.Figure
                 when 'k: equality

        static member
          Boxplot: keys: 'k array * values: float array *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] yUnit: Marey.IUnit -> Marey.Figure
                     when 'k: equality

        static member Count: keys: 'k array -> Marey.Figure when 'k: equality

        static member
          Scatter: keys: 'k array * values: float array *
                   [<System.Runtime.InteropServices.Optional;
                     System.Runtime.InteropServices.DefaultParameterValue
                     ((null :> obj))>] yUnit: Marey.IUnit -> Marey.Figure
                     when 'k: equality

        static member
          Violin: keys: 'k array * values: float array *
                  [<System.Runtime.InteropServices.Optional;
                    System.Runtime.InteropServices.DefaultParameterValue
                    ((null :> obj))>] yUnit: Marey.IUnit -> Marey.Figure
                    when 'k: equality
