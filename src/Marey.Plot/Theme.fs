namespace Marey

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
      /// How unit symbols are spelled on axis titles.
      ///
      /// A theme is the right home for this because it is a *presentation* choice, exactly like a
      /// font or a rule width: the same figure wants `m·s⁻²` on screen and
      /// `\qty{}{\metre\per\second\squared}` in a LaTeX document, and nothing about the data or
      /// the scale changes between them. It is what lets `Theme.tikz` deliver M1's headline with
      /// no configuration beyond selecting the theme.
      ///
      /// `AxisConfig.Style` overrides it per axis when set.
      UnitStyle: UnitStyle

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
    ///
    /// Public so `withFontFamily` has something to be passed.
    [<Literal>]
    let Serif = "Times New Roman, Nimbus Roman, Liberation Serif, serif"

    /// A UI-grotesque stack, in the order a 2026 renderer should try.
    ///
    /// `ApproxTextMeasurer.familyFactor` returns 1.0 for this string exactly as it did for the
    /// Helvetica-first stack it replaces — its serif branch is guarded by `not (contains "sans")`
    /// and "sans-serif" trips that guard, and no Times/Georgia/Garamond name appears here. So
    /// **layout is unchanged by this string**; only the family the renderer is asked for changes.
    /// That is deliberate: a font stack that moved the measurements would have moved every
    /// baseline in the golden suite for a cosmetic reason.
    [<Literal>]
    let Sans =
        "Inter, -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif"

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

          UnitStyle = Unicode

          ShowGridX = false
          ShowGridY = false
          PanelFrame = false
          ShowLegendFrame = true
          FillOpacity = 0.25
          TickCount = Axis.DefaultTickCount
          MarkerShapes = defaultShapes }

    /// **The default.** A figure you have not themed is this one.
    ///
    /// `publication` was the default until the notebook renderer shipped and it became clear
    /// where a figure is actually first seen: on a screen, in a cell, next to the code that made
    /// it. Times New Roman at 9 pt is right in a two-column paper and wrong there, and a default
    /// should be right where the default is met. `publication` is one `Chart.withTheme` away and
    /// unchanged.
    ///
    /// ### What makes it modern is weight and space, not paleness
    ///
    /// The contemporary look — de-emphasised furniture, data foremost — is usually reached by
    /// fading the axis toward the background. **That is not available here, and the reason is
    /// this library's own validator.** A spine is a graphical object that carries information, so
    /// WCAG's 3:1 minimum applies to it, and `Theme.validate` enforces exactly that floor on
    /// `AxisContrast`. A fashionable `#C8CDD4` spine measures about 1.5:1 on white — the theme
    /// would fail the test the library ships to catch precisely this mistake in other people's
    /// themes.
    ///
    /// So the furniture recedes by getting *thinner and quieter*, never lighter: a 0.9 px spine
    /// at 9.6:1, ticks shortened to 3 px, the panel box and the legend frame gone, horizontal
    /// gridlines only, and roughly 40% more padding than `publication`. Gridlines are the one
    /// exception and legitimately so — D-level: `GridContrast` is *reported* by `validate` and
    /// deliberately not floored, because a gridline that competes with the data is chartjunk.
    ///
    /// The palette does not follow fashion either. Okabe–Ito reordered (`Palette.series`) stays,
    /// because it is measured under three dichromacies; the Tableau-style palettes this would
    /// otherwise borrow from are not.
    let modern: Theme =
        { publication with
            Name = "modern"

            TitleFont = font Sans 13.0<pt> 600 false
            SubtitleFont = font Sans 10.0<pt> 400 false
            PanelTitleFont = font Sans 10.0<pt> 500 false
            AxisTitleFont = font Sans 10.0<pt> 500 false
            TickFont = font Sans 9.0<pt> 400 false
            LegendFont = font Sans 9.5<pt> 400 false

            Background = Color.white
            // Flat: no tinted panel, so the data sits on the page rather than in a box.
            PanelBackground = Color.white
            Foreground = Color.ofHex "#14181F"
            MutedForeground = Color.ofHex "#606A78"
            AxisColor = Color.ofHex "#39414D"
            GridColor = Color.ofHex "#E7E9ED"
            LegendBackground = Color.rgba 1.0 1.0 1.0 0.9
            LegendBorder = Color.ofHex "#E1E4E9"

            AxisWidth = 0.9<px>
            GridWidth = 1.0<px>
            SeriesWidth = 2.0<px>
            TickLength = 3.0<px>
            TickWidth = 0.9<px>
            MarkerSize = 5.0<px>

            FigurePadding = 14.0<px>
            PanelGap = 30.0<px>
            LabelGap = 4.0<px>
            TitleGap = 7.0<px>
            LegendPadding = 7.0<px>
            LegendItemGap = 6.0<px>
            LegendSwatchWidth = 18.0<px>

            Sequential = Palette.viridis

            ShowGridX = false
            ShowGridY = true
            PanelFrame = false
            ShowLegendFrame = false
            FillOpacity = 0.18 }

    /// The theme a `Figure` is born with, named so that the two places that must agree about it
    /// cannot drift apart.
    ///
    /// `Chart.withTheme` re-styles a layer **only if its style still equals the outgoing theme's
    /// default for that index** — which is what lets it recolour a figure without discarding a
    /// colour the user set by hand. That test is only meaningful while `Figure.create`'s theme and
    /// the theme `Chart.layerOf` styles against are the same value. They were the same by
    /// coincidence (both spelled `Theme.publication` in two different files); now they are the
    /// same by construction.
    let initial: Theme = modern

    /// §9 — the notebook theme, and now a variation on `modern` rather than a sibling of it.
    ///
    /// What survives as its own is the tinted panel and the vertical gridlines. Both exist for one
    /// reason: a figure in a notebook is not on a page, it is in a stack of cells, and it has to
    /// read as a *distinct object* rather than as more page. `modern`'s flat white panel is right
    /// on a page and disappears into the cell background; a hair of tint gives it an edge without
    /// drawing one. The legend keeps its frame for the same reason.
    let notebook: Theme =
        { modern with
            Name = "notebook"

            PanelBackground = Color.ofHex "#F7F8FA"
            LegendBackground = Color.rgba 1.0 1.0 1.0 0.92

            ShowGridX = true
            ShowLegendFrame = true }

    /// §9 — "large type." Everything that carries meaning at four metres is bigger; everything
    /// that does not is gone.
    let presentation: Theme =
        { modern with
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
        { modern with
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

    /// §9's `tikz` theme — "inherits document fonts" — and the one that makes M1's headline a
    /// single selection: `Chart.withTheme Theme.tikz` and the axis titles come out as siunitx
    /// macros with nothing else configured.
    ///
    /// ### The two halves of D35, and why it is not simply "inherit everything"
    ///
    /// **The size is pinned; the family is inherited.** Taken literally, "inherit the document's
    /// fonts" means emit no font information at all, and then the figure is typeset in a face whose
    /// metrics have nothing to do with the `ApproxTextMeasurer` widths that the measure–arrange
    /// pass and D23's collision ladder were computed against. Labels would collide in the PDF
    /// while layout believed they fitted. So the TikZ backend emits `\fontsize{s}{1.2s}` — the size
    /// layout assumed — and no family, letting the document supply that.
    /// `TikzOptions.FontMode = InheritFont` opts out of even the size, and accepts the
    /// consequence.
    ///
    /// **The family string here is therefore a *measurement* input, not an instruction.** The TikZ
    /// backend never emits a family, so `Serif` does not put Times in the document — it tells
    /// `ApproxTextMeasurer` to apply its 0.92 width factor, which is the closer approximation to
    /// Computer Modern than a Helvetica-width assumption would be. Rendering this theme to SVG does
    /// use the family, and a serif SVG is the right sibling of a serif LaTeX figure.
    ///
    /// The type is a little smaller than `publication`'s throughout, because a figure in a 10 pt
    /// document reads better at roughly `\small`/`\footnotesize` than at body size.
    let tikz: Theme =
        { publication with
            Name = "tikz"

            TitleFont = font Serif 10.0<pt> 700 false
            SubtitleFont = font Serif 9.0<pt> 400 true
            PanelTitleFont = font Serif 9.0<pt> 400 false
            AxisTitleFont = font Serif 9.0<pt> 400 false
            TickFont = font Serif 8.0<pt> 400 false
            LegendFont = font Serif 8.0<pt> 400 false

            UnitStyle = Siunitx }

    let all = [| modern; publication; notebook; presentation; dark; tikz |]

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

    /// Set every font's family at once.
    ///
    /// A sibling of `scaleType`, and the reason it exists is layout rather than looks. The family
    /// string is what `ApproxTextMeasurer` reads to choose its width factor, so it decides how
    /// much room layout reserves for a label — and for the TikZ backend, which emits no family and
    /// lets the document supply the face, that is the *only* thing the family does.
    ///
    /// So a `Theme.tikz` figure going into a sans-serif document wants
    /// `Theme.tikz |> Theme.withFontFamily Theme.Sans`: TeX will set the labels in the
    /// document's sans face, which is ~8% wider than the serif metrics the theme otherwise
    /// assumes, and reserving the right amount of room is what keeps a y-axis title clear of its
    /// tick labels. Measured: the difference is visible on a real figure, though not a collision.
    let withFontFamily (family: string) (t: Theme) =
        let f (spec: FontSpec) = { spec with Family = family }

        { t with
            TitleFont = f t.TitleFont
            SubtitleFont = f t.SubtitleFont
            PanelTitleFont = f t.PanelTitleFont
            AxisTitleFont = f t.AxisTitleFont
            TickFont = f t.TickFont
            LegendFont = f t.LegendFont }

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
