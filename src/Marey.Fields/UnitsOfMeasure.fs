namespace Marey.UnitsOfMeasure

open System
open Marey

/// The field geoms in the unit-required mode: a witness per axis and for the value,
/// checked at compile time against the grid's measures. See `Marey.UnitsOfMeasure.Chart` for the
/// pattern and the shadows. Sampling, derivatives and grids are the core's in both modes.
[<RequireQualifiedAccess>]
module Field =

    /// A scalar grid as an image coloured along `ramp`, with a colorbar in `'v`. `smooth`
    /// interpolates between nodes; otherwise every node is a flat cell.
    let heatmapWith<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ramp: Ramp)
        (smooth: bool)
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: ScalarGrid<'x, 'y, 'v>)
        : Figure =
        Marey.Field.heatmapWith ramp smooth ux uy uv g

    /// `heatmapWith` along the theme's sequential ramp, interpolated between nodes.
    let heatmap<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: ScalarGrid<'x, 'y, 'v>)
        : Figure =
        Marey.Field.heatmap ux uy uv g

    /// Contour lines at stated levels, in the theme's foreground colour.
    let contoursAt<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (levels: float<'v>[])
        (ux: U<'x>)
        (uy: U<'y>)
        (g: ScalarGrid<'x, 'y, 'v>)
        : Figure =
        Marey.Field.contoursAt levels ux uy g

    /// Contour lines at the nice numbers the field's own range would be ticked at — about eight
    /// of them. A field in volts gets equipotentials at round numbers of volts.
    let contours<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (g: ScalarGrid<'x, 'y, 'v>)
        : Figure =
        Marey.Field.contours ux uy g

    /// Filled bands between stated levels, coloured along `ramp` with a colorbar in `'v`.
    let bandsWith<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ramp: Ramp)
        (levels: float<'v>[])
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: ScalarGrid<'x, 'y, 'v>)
        : Figure =
        Marey.Field.bandsWith ramp levels ux uy uv g

    /// Filled bands at nice levels widened to cover the field's whole range, along the theme's
    /// sequential ramp.
    let bands<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: ScalarGrid<'x, 'y, 'v>)
        : Figure =
        Marey.Field.bands ux uy uv g

    /// A quiver: an arrow at every node, its length the vector's magnitude through the quiver's
    /// own scale, and a key in the plot area's corner saying what one length means — `2 m/s`,
    /// set with the prefix the value would be ticked in. Both components must be in one
    /// unit, `'v`, because a length is a magnitude; a phase space has `directions` instead.
    ///
    /// Arrows are drawn at every node, so sample a quiver at the density it should be read at — a
    /// coarser grid than a heatmap's.
    let quiver<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: VectorGrid<'x, 'y, 'v, 'v>)
        : Figure =
        Marey.Field.quiver ux uy uv g

    /// `quiver`, with each arrow also coloured by its magnitude along the theme's sequential
    /// ramp, and a colorbar in `'v`.
    let quiverBy<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: VectorGrid<'x, 'y, 'v, 'v>)
        : Figure =
        Marey.Field.quiverBy ux uy uv g

    /// A direction field: an arrow at every node, all one length. Any vector grid, including a
    /// phase space whose two components are in different units and so has no magnitude to draw.
    let directions<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Figure =
        Marey.Field.directions ux uy g

    /// Evenly spaced streamlines `separation` apart on the page, with arrowheads pointing
    /// downstream. Any vector grid, a phase space included: a streamline is a curve, and
    /// needs a direction, not a magnitude.
    ///
    /// The separation is in **pixels**, because evenly spaced is a visual property. The same
    /// figure at a larger size therefore draws more lines, each the same distance apart.
    let streamlinesWith<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (separation: float<px>)
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Figure =
        Marey.Field.streamlinesWith separation ux uy g

    /// `streamlinesWith` at `DefaultSeparation`.
    let streamlines<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Figure =
        Marey.Field.streamlines ux uy g

    /// Streamlines coloured by the field's magnitude — each line by its mean along it — with a
    /// colorbar in `'v`. Both components in one unit, for `quiver`'s reason.
    let streamlinesBy<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: VectorGrid<'x, 'y, 'v, 'v>)
        : Figure =
        Marey.Field.streamlinesBy ux uy uv g

    /// A line-integral convolution in grey: noise smeared along the field's streamlines, so the
    /// texture is the flow at every pixel. Any vector grid, a phase space included, because
    /// it needs a direction and not a magnitude.
    ///
    /// **It is a raster at the plot area's resolution** times `options.Oversample`, on every
    /// backend. SVG embeds it and TikZ writes it beside the document; neither can do better than
    /// the pixels it was computed at, so a figure meant for print states an oversample of 2 or 3.
    ///
    /// **It is deterministic.** The noise comes from `Rng` seeded with `options.Seed`, so a seed,
    /// a field and a plot size draw the same image every time, on every runtime. A different seed
    /// draws the same flow in different grain.
    ///
    /// **It costs** 70 ms for a 480 × 300 figure, 0.3 s at 960 × 600 and 1.4 s at 1920 × 1200,
    /// measured on one core of a Ryzen 9 9950X with the default 20 px kernel. The cost is close to
    /// linear in the raster's pixel count, so it quadruples with each doubling of the
    /// oversample.
    let licWith<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (options: LicOptions)
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Figure =
        Marey.Field.licWith options ux uy g

    /// `licWith licDefaults`.
    let lic<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Figure =
        Marey.Field.lic ux uy g

    /// `licWith`, coloured by the field's magnitude along the theme's sequential ramp, with a
    /// colorbar in `'v`. The texture darkens a pixel to no less than a fifth of its colour, so
    /// the colorbar still reads in the darkest strokes. Both components in one unit, for
    /// `quiver`'s reason.
    let licByWith<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (options: LicOptions)
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: VectorGrid<'x, 'y, 'v, 'v>)
        : Figure =
        Marey.Field.licByWith options ux uy uv g

    /// `licByWith licDefaults`.
    let licBy<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'v>
        (ux: U<'x>)
        (uy: U<'y>)
        (uv: U<'v>)
        (g: VectorGrid<'x, 'y, 'v, 'v>)
        : Figure =
        Marey.Field.licBy ux uy uv g

    // ---- The shadows -------------------------------------------------------------------------

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.heatmapWith. Field.HeatmapWith(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let HeatmapWith (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.heatmap. Field.Heatmap(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Heatmap (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.contoursAt. Field.ContoursAt(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let ContoursAt (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.contours. Field.Contours(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Contours (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.bandsWith. Field.BandsWith(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let BandsWith (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.bands. Field.Bands(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Bands (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.quiver. Field.Quiver(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Quiver (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.quiverBy. Field.QuiverBy(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let QuiverBy (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.directions. Field.Directions(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Directions (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.streamlinesWith. Field.StreamlinesWith(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let StreamlinesWith (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.streamlines. Field.Streamlines(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Streamlines (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.streamlinesBy. Field.StreamlinesBy(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let StreamlinesBy (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.licWith. Field.LicWith(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let LicWith (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.lic. Field.Lic(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Lic (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.licByWith. Field.LicByWith(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let LicByWith (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Field.licBy. Field.LicBy(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let LicBy (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

/// Phase portraits in the unit-required mode. See `Field` above.
[<RequireQualifiedAccess>]
module Phase =

    /// The two nullclines: where `u` vanishes and where `w` does, each the zero contour of its
    /// component, in the theme's first two categorical colours and named in the legend
    /// `dθ/dt = 0`, after the quantity each axis would be titled with.
    let nullclines<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 'u, [<Measure>] 'w>
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'u, 'w>)
        : Figure =
        Marey.Phase.nullclines ux uy g

    /// The fixed points as marks, one layer per kind present, named by kind in the legend: a
    /// diamond for a saddle, a circle for a centre, a triangle down for a stable node or focus and
    /// up for an unstable one, a square for a degenerate point. Each kind has its own colour
    /// (`colourIndex`). `None` when there are none.
    let marks<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (ux: U<'x>)
        (uy: U<'y>)
        (points: FixedPoint<'x, 'y, 't>[])
        : Figure option =
        Marey.Phase.marks ux uy points

    /// A phase portrait: streamlines `separation` apart on the page, the nullclines over them,
    /// and the fixed points over both, named by kind in the legend. The legend sits in a corner of
    /// the plot area, where a portrait has data too; `Chart.withLegend` moves it.
    let portraitWith<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (separation: float<px>)
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'x / 't, 'y / 't>)
        : Figure =
        Marey.Phase.portraitWith separation ux uy g

    /// `portraitWith` at the streamlines' default separation.
    let portrait<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (ux: U<'x>)
        (uy: U<'y>)
        (g: VectorGrid<'x, 'y, 'x / 't, 'y / 't>)
        : Figure =
        Marey.Phase.portrait ux uy g

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Phase.nullclines. Phase.Nullclines(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Nullclines (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Phase.marks. Phase.Marks(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Marks (_: obj) : Figure option = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Phase.portraitWith. Phase.PortraitWith(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let PortraitWith (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: Phase.portrait. Phase.Portrait(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Portrait (_: obj) : Figure = invalidOp "Marey: unreachable — an error-obsolete shadow."

    [<Obsolete("Marey.UnitsOfMeasure is open, so this takes a witness per unit: FixedPoint.describe. Phase.Describe(…) is the unit-optional spelling, for code that does not open this namespace.", true)>]
    let Describe (_: obj) : string = invalidOp "Marey: unreachable — an error-obsolete shadow."

/// `describe` in the unit-required mode. See `Field` above.
[<RequireQualifiedAccess>]
module FixedPoint =

    /// One line describing a fixed point, the coordinates in their axes' units and the
    /// eigenvalues in `1/'t`:
    ///
    ///     centre (linear) at (0 rad, 0 rad·s⁻¹), λ = ±3.116i s⁻¹
    ///
    /// The eigenvalues are converted to SI and written in base units, never through the registry,
    /// because the registry names s⁻¹ hertz and an eigenvalue is a rate, not a frequency.
    let describe<[<Measure>] 'x, [<Measure>] 'y, [<Measure>] 't>
        (ux: U<'x>)
        (uy: U<'y>)
        (ut: U<'t>)
        (p: FixedPoint<'x, 'y, 't>)
        : string =
        Marey.FixedPoint.describe ux uy ut p
