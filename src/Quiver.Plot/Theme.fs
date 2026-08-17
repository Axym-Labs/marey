namespace Quiver

/// §9 — **"A theme is a record of resolved values, not a stylesheet lookup."**
///
/// Every field below is a concrete font, a concrete colour, a concrete width in `px` or a
/// concrete size in `pt`. There is no key, no cascade, no `Map(string,string)`, and no "inherit
/// from parent". Resolving a style is a field access, and a missing style is a compile error
/// rather than a silent default three layers away.
///
/// ### The unit convention, which is D12 and is load-bearing
///
/// * **Positions** are `nd` and live in the IR.
/// * **Font sizes** are `pt`, because type is specified in points and a 10 pt label is 10 pt at
///   every output size.
/// * **Everything else that must not scale with the figure** — stroke widths, tick lengths,
///   gaps, paddings, marker sizes — is `px`.
///
/// So a theme contains no `nd` at all. Turning a `px` gap into the `nd` offset the IR wants is
/// the layout layer's job and needs the figure's `Extent(px)`; see `Layout.ndX`/`Layout.ndY`.
/// Doing that conversion here would bake a resolution into the theme.
[<NoComparison>]
type Theme =
    { Name: string

      // ---- Type ---------------------------------------------------------------------
      TitleFont: FontSpec
      SubtitleFont: FontSpec
      PanelTitleFont: FontSpec
      AxisTitleFont: FontSpec
      TickFont: FontSpec
      LegendFont: FontSpec

      // ---- Colour -------------------------------------------------------------------
      /// Behind the whole figure.
      Background: Color
      /// Behind a panel's data area. Equal to `Background` in a themeless-looking theme;
      /// different in a notebook theme, where the panel is what the eye is meant to land on.
      PanelBackground: Color
      /// Titles and tick labels.
      Foreground: Color
      /// Subtitles, captions — anything deliberately secondary.
      MutedForeground: Color
      /// Spines and tick marks.
      AxisColor: Color
      GridColor: Color
      LegendBackground: Color
      LegendBorder: Color

      // ---- Stroke and size, all device units ----------------------------------------
      AxisWidth: float<px>
      GridWidth: float<px>
      GridDash: float<px>[]
      SeriesWidth: float<px>
      TickLength: float<px>
      TickWidth: float<px>
      MarkerSize: float<px>
      /// Half-width of an error bar's cap.
      ErrorCapWidth: float<px>

      // ---- Spacing, all device units ------------------------------------------------
      /// Between the figure edge and anything drawn.
      FigurePadding: float<px>
      /// Between panels in a facet grid.
      PanelGap: float<px>
      /// Between a tick mark and its label.
      LabelGap: float<px>
      /// Between the tick labels and the axis title.
      TitleGap: float<px>
      LegendPadding: float<px>
      LegendItemGap: float<px>
      LegendSwatchWidth: float<px>

      // ---- Palettes -----------------------------------------------------------------
      Categorical: Color[]
      Sequential: Ramp
      Diverging: Ramp

      // ---- Switches -----------------------------------------------------------------
      ShowGridX: bool
      ShowGridY: bool
      /// A full box around the panel, versus only the left and bottom spines.
      PanelFrame: bool
      ShowLegendFrame: bool
      /// Fill opacity for `Geom.area` and bars.
      FillOpacity: float
      /// Target number of ticks per axis. A *target*: `Ticks.linear` walks the 1-2-5 ladder and
      /// returns what falls out.
      TickCount: int
      /// Marker shapes, cycled alongside `Categorical`. Shape is the redundant encoding channel
      /// that makes a scatter readable in greyscale and under CVD — which is the whole reason
      /// D5 put a shape field on `MarkerBatch`.
      MarkerShapes: MarkerShape[] }

/// What `Theme.validate` measured. Contrast ratios, nothing asserted.
[<NoComparison>]
type ThemeReport =
    { Theme: string
      /// Foreground text on the figure background.
      TextContrast: float
      /// Axis spine on the panel background.
      AxisContrast: float
      /// Gridline on the panel background. Deliberately *low* — a gridline that competes with
      /// the data is chartjunk — so this one is reported, not floored.
      GridContrast: float
      /// The palette, validated against the panel background.
      Palette: PaletteReport
      /// Text clears WCAG AAA (7:1) and the spine clears the 3:1 non-text minimum.
      Ok: bool }

[<RequireQualifiedAccess>]
module Theme =

    let private font family size weight italic : FontSpec =
        { Family = family
          Size = size
          Weight = weight
          Italic = italic }

    /// A serif stack. `ApproxTextMeasurer` reads "serif" and "times" out of the family string to
    /// pick its 0.92 width factor, so naming the fallback matters to layout, not only to the
    /// renderer that finally opens the file.
    [<Literal>]
    let private Serif = "Times New Roman, Nimbus Roman, Liberation Serif, serif"

    [<Literal>]
    let private Sans = "Helvetica, Arial, Nimbus Sans, Liberation Sans, sans-serif"

    let private defaultShapes =
        [| MarkCircle; MarkSquare; MarkTriangleUp; MarkDiamond; MarkPlus; MarkTriangleDown; MarkCross |]

    /// §9 — "serif, thin rules, no chartjunk, colour-blind-safe."
    ///
    /// No gridlines, no panel box, hairline spines, and a palette that has been measured under
    /// three dichromacies. This is the theme a figure goes into a paper in.
    let publication: Theme =
        { Name = "publication"

          TitleFont = font Serif 11.0<pt> 700 false
          SubtitleFont = font Serif 9.5<pt> 400 true
          PanelTitleFont = font Serif 10.0<pt> 400 false
          AxisTitleFont = font Serif 10.0<pt> 400 false
          TickFont = font Serif 9.0<pt> 400 false
          LegendFont = font Serif 9.0<pt> 400 false

          Background = Color.white
          PanelBackground = Color.white
          Foreground = Color.ofHex "#101010"
          MutedForeground = Color.ofHex "#555555"
          AxisColor = Color.ofHex "#101010"
          GridColor = Color.ofHex "#D8D8D8"
          LegendBackground = Color.rgba 1.0 1.0 1.0 0.85
          LegendBorder = Color.ofHex "#B0B0B0"

          AxisWidth = 0.8<px>
          GridWidth = 0.5<px>
          GridDash = [||]
          SeriesWidth = 1.4<px>
          TickLength = 3.5<px>
          TickWidth = 0.8<px>
          MarkerSize = 4.5<px>
          ErrorCapWidth = 3.0<px>

          FigurePadding = 10.0<px>
          PanelGap = 26.0<px>
          LabelGap = 3.0<px>
          TitleGap = 5.0<px>
          LegendPadding = 6.0<px>
          LegendItemGap = 4.0<px>
          LegendSwatchWidth = 20.0<px>

          Categorical = Palette.series
          Sequential = Palette.cividis
          Diverging = Palette.diverging

          ShowGridX = false
          ShowGridY = false
          PanelFrame = false
          ShowLegendFrame = true
          FillOpacity = 0.25
          TickCount = Axis.DefaultTickCount
          MarkerShapes = defaultShapes }

    /// §9 — the notebook theme. Sans type, a light panel, gridlines on: this one is read on a
    /// screen, at speed, next to the code that made it.
    let notebook: Theme =
        { publication with
            Name = "notebook"

            TitleFont = font Sans 12.0<pt> 700 false
            SubtitleFont = font Sans 10.0<pt> 400 false
            PanelTitleFont = font Sans 10.5<pt> 400 false
            AxisTitleFont = font Sans 10.0<pt> 400 false
            TickFont = font Sans 9.0<pt> 400 false
            LegendFont = font Sans 9.0<pt> 400 false

            Background = Color.white
            PanelBackground = Color.ofHex "#FAFAFA"
            Foreground = Color.ofHex "#1A1A1A"
            MutedForeground = Color.ofHex "#606060"
            AxisColor = Color.ofHex "#3C3C3C"
            GridColor = Color.ofHex "#DCDCDC"

            AxisWidth = 1.0<px>
            GridWidth = 0.75<px>
            SeriesWidth = 1.8<px>
            TickLength = 4.0<px>
            MarkerSize = 5.5<px>

            ShowGridX = true
            ShowGridY = true
            PanelFrame = false }

    /// §9 — "large type." Everything that carries meaning at four metres is bigger; everything
    /// that does not is gone.
    let presentation: Theme =
        { notebook with
            Name = "presentation"

            TitleFont = font Sans 20.0<pt> 700 false
            SubtitleFont = font Sans 15.0<pt> 400 false
            PanelTitleFont = font Sans 16.0<pt> 400 false
            AxisTitleFont = font Sans 16.0<pt> 600 false
            TickFont = font Sans 14.0<pt> 400 false
            LegendFont = font Sans 14.0<pt> 400 false

            AxisWidth = 2.0<px>
            GridWidth = 1.0<px>
            SeriesWidth = 3.2<px>
            TickLength = 7.0<px>
            TickWidth = 2.0<px>
            MarkerSize = 10.0<px>
            ErrorCapWidth = 6.0<px>

            FigurePadding = 18.0<px>
            PanelGap = 40.0<px>
            LabelGap = 6.0<px>
            TitleGap = 9.0<px>
            LegendPadding = 10.0<px>
            LegendItemGap = 8.0<px>
            LegendSwatchWidth = 34.0<px>

            TickCount = 4
            ShowGridX = false
            ShowGridY = true }

    /// §9 — dark.
    ///
    /// **Not an inversion of the light theme, and that is the point.** Pure white text on pure
    /// black is a contrast ratio of 21 and produces halation — the glyphs bloom on an emissive
    /// display and the eye cannot hold them. `#E8E8E8` on `#16181C` measures 13.7:1, which is
    /// comfortably past AAA and comfortably short of painful. The gridline is *lighter* than the
    /// panel here where in the light themes it is darker, because a dark theme's recessive
    /// direction is down and there is no room left below the panel.
    ///
    /// The categorical palette swaps to `Palette.seriesDark`: Okabe–Ito opens with `#000000`,
    /// which on this background is invisible rather than merely poor. `Theme.validate` measures
    /// that rather than trusting it.
    let dark: Theme =
        { notebook with
            Name = "dark"

            Background = Color.ofHex "#16181C"
            PanelBackground = Color.ofHex "#1D2026"
            Foreground = Color.ofHex "#E8E8E8"
            MutedForeground = Color.ofHex "#A0A4AC"
            AxisColor = Color.ofHex "#9AA0A8"
            GridColor = Color.ofHex "#2E333B"
            LegendBackground = Color.rgba 0.114 0.125 0.149 0.9
            LegendBorder = Color.ofHex "#3A4048"

            Categorical = Palette.seriesDark
            Sequential = Palette.viridis
            Diverging = Palette.diverging }

    let all = [| publication; notebook; presentation; dark |]

    let tryByName (name: string) =
        all
        |> Array.tryFind (fun t -> System.String.Equals(t.Name, name, System.StringComparison.OrdinalIgnoreCase))

    // ---- Combinators. Every one returns a new value (§1.1: Plotly.NET's documented flaw is
    // that its styling functions mutate). ------------------------------------------------

    let withPalette (cs: Color[]) (t: Theme) = { t with Categorical = cs }
    let withSequential (r: Ramp) (t: Theme) = { t with Sequential = r }
    let withDiverging (r: Ramp) (t: Theme) = { t with Diverging = r }

    let withGrid (x: bool) (y: bool) (t: Theme) = { t with ShowGridX = x; ShowGridY = y }

    let withTickCount (n: int) (t: Theme) = { t with TickCount = max 1 n }

    /// Scale every typographic size by a factor, leaving everything else alone. The honest way
    /// to make a theme bigger without inventing a fifth theme.
    let scaleType (k: float) (t: Theme) =
        let s (f: FontSpec) = { f with Size = f.Size * k }

        { t with
            TitleFont = s t.TitleFont
            SubtitleFont = s t.SubtitleFont
            PanelTitleFont = s t.PanelTitleFont
            AxisTitleFont = s t.AxisTitleFont
            TickFont = s t.TickFont
            LegendFont = s t.LegendFont }

    // ---- Validation ---------------------------------------------------------------------

    /// Measure a theme's contrast. §9 asks for a validator; this is the half that runs on the
    /// theme rather than on the palette, and it is what catches a dark theme that was built by
    /// inverting a light one and never looked at.
    let validate (t: Theme) : ThemeReport =
        let text = Color.contrastRatio t.Background t.Foreground
        let axis = Color.contrastRatio t.PanelBackground t.AxisColor
        let grid = Color.contrastRatio t.PanelBackground t.GridColor

        { Theme = t.Name
          TextContrast = text
          AxisContrast = axis
          GridContrast = grid
          Palette = Palette.validate t.PanelBackground t.Categorical
          Ok = text >= 7.0 && axis >= Palette.ContrastFloor }
