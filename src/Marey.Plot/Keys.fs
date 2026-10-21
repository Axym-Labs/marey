namespace Marey

// ======================================================================================
// THE NAMES THE PRODUCER GIVES WHAT IT DRAWS
//
// `Keyed` is the IR's stable identity. With only `Keyed(Key "figure", …)` at the root of every
// figure, the diff the IR is designed around — and that keeping closures out of the IR protects
// — would have a granularity of *one*: any change to any figure changes the one keyed subtree,
// and `Keyed` is decoration.
//
// The identity was never missing. It was in the *other* node: `Annotated` already carries a
// panel's `(row, col)` and a layer's name, because the semantics say what a shape means. The two
// nodes answer different questions — "what is this" and "which one is this" — and the producer
// was answering only the first. This module is the second answer, from the information the
// producer already had in hand.
//
// **Ordinals, not names.** A layer's `Name` is caller-supplied, is frequently empty (the
// producer substitutes "series"), and may be *duplicated* across layers of one panel. A key
// built from it is not unique and is not stable under `Chart.withName`. The layer's position in
// `Panel.Layers` is both.
//
// **A mark's key is its original row.** `Figure.fs` hands `Geom.compileSubset` a channel batch's
// `Indices`, because a batch is a *renumbered subset* of the layer's points — mark 3 of batch 2 is
// not row 3. Building a key from the position within the batch is exactly the defect to avoid, and
// the array that avoids it is already threaded through the producer. `mark` therefore takes the
// row, and the one caller that will construct it has `b.Indices.[j]` in scope.
//
// **What is not built here.** A mark has no node of its own until something addresses it, and
// splitting a `MarkerBatch` to give it one is the storyboard's business. The spelling is fixed
// here, and pinned by a test, so the storyboard does not invent a second one — the cost of agreeing
// on a string early is a string.
// ======================================================================================

/// The hierarchical names `Marey.Plot` gives the subtrees it produces.
///
/// ```
/// figure
/// figure/panel-0-1
/// figure/panel-0-1/layer-2
/// figure/panel-0-1/layer-2/mark-417
/// ```
///
/// Each level is built from its parent, so a key is a path and a prefix of a key names an
/// ancestor. Address a subtree with `Scene.tryFindKey` and rewrite one with `Scene.mapKey`.
[<RequireQualifiedAccess>]
module Keys =

    /// The path separator. `/` rather than `.` or `-`: the two levels below a panel already
    /// contain `-` in their own names (`panel-0-1`, `layer-2`), so a `-` separator would make
    /// the path ambiguous to read and to split.
    [<Literal>]
    let Separator = '/'

    let private child (Key parent) (suffix: string) =
        Key(parent + string Separator + suffix)

    /// The root of every figure.
    let figure = Key "figure"

    /// The legend a figure shares among its panels.
    let figureLegend = child figure "legend"

    /// A panel, by its grid position. Not by its index in `Figure.Panels`: a panel's identity is
    /// where it sits, and `Chart.facet` may add or reorder the array without moving any panel.
    let panel (row: int) (col: int) =
        child figure (sprintf "panel-%d-%d" row col)

    /// A layer, by its **ordinal** within the panel — see the note above on why not by name.
    let layer (panelKey: Key) (ordinal: int) = child panelKey (sprintf "layer-%d" ordinal)

    /// One mark, by its **original row** in the layer's data — not by its position within a
    /// channel batch, which is a renumbered subset.
    ///
    /// A layer whose data supplied keys names its marks by `datum` instead.
    let mark (layerKey: Key) (row: int) = child layerKey (sprintf "mark-%d" row)

    /// A key the data supplied, written as one path segment. `%`, `/` and the control
    /// characters are percent-encoded and nothing else is, so a station called `Bern/Ost` is one
    /// segment, `key-Bern%2FOst`, and cannot forge a path that `covers` would read as a child.
    /// Distinct keys stay distinct, because the encoding is reversible.
    let escape (k: string) : string =
        let sb = System.Text.StringBuilder(k.Length)

        for c in k do
            if c = '%' || c = Separator || System.Char.IsControl c then
                sb.Append('%').Append((int c).ToString("X2")) |> ignore
            else
                sb.Append(c) |> ignore

        sb.ToString()

    /// One mark, by the key its datum carries rather than by its row. A row ordinal names
    /// a different datum as soon as an earlier row is removed; a supplied key does not.
    /// `key-` rather than `mark-`, so the two spellings cannot meet inside one layer.
    let datum (layerKey: Key) (k: string) = child layerKey ("key-" + escape k)

    /// **Keys for a layer's rows, checked.** One per row, and no two alike, because a duplicate is
    /// the failure keys exist to prevent: one name for two marks, and `Scene.mapKey` touching both
    /// when asked for one. The duplicates are named, with their rows, up to five of them.
    let validate (rows: int) (keys: string[]) : unit =
        if isNull keys then
            invalidArg "keys" "Marey: keys cannot be null."

        if keys.Length <> rows then
            invalidArg "keys" (sprintf "Marey: the series has %d rows and %d keys; a key names one row." rows keys.Length)

        let dupes =
            keys
            |> Array.indexed
            |> Array.groupBy snd
            |> Array.filter (fun (_, xs) -> xs.Length > 1)

        if dupes.Length > 0 then
            let named =
                dupes
                |> Array.truncate 5
                |> Array.map (fun (k, xs) -> sprintf "\"%s\" (rows %s)" k (xs |> Array.map (fst >> string) |> String.concat ", "))
                |> String.concat "; "

            let more = if dupes.Length > 5 then sprintf ", and %d more" (dupes.Length - 5) else ""

            invalidArg "keys" (sprintf "Marey: keys must be unique within a layer; duplicated: %s%s." named more)

    /// The parts of a panel that are not its layers, each under its own key so that a
    /// change to one is a change to it and not to the whole panel. A slider moving one
    /// annotation over a fixed scatter leaves the axes, the gridlines and the keys alone, and
    /// a diff can only see that when they are not all one subtree.
    let axisX (panelKey: Key) = child panelKey "axis-x"

    let axisY (panelKey: Key) = child panelKey "axis-y"

    /// A panel's `i`th inset, under the panel it is drawn over.
    let inset (panelKey: Key) (i: int) = child panelKey (sprintf "inset-%d" i)

    /// A panel's secondary x axis, on its top.
    let axisX2 (panelKey: Key) = child panelKey "axis-x2"

    /// A panel's second y axis, on its right, or its secondary y axis.
    let axisY2 (panelKey: Key) = child panelKey "axis-y2"

    let grid (panelKey: Key) = child panelKey "grid"

    let legend (panelKey: Key) = child panelKey "legend"

    let colorbar (panelKey: Key) = child panelKey "colorbar"

    /// The panel's letter, "(a)", a sibling of its axes rather than one of its layers.
    let letter (panelKey: Key) = child panelKey "letter"

    /// Does `ancestor` name `key`, or something `key` sits under?
    ///
    /// A key is a path, so this is a prefix test — but on *segments*, not on characters.
    /// `figure/panel-0-1` is not an ancestor of `figure/panel-0-10`, and a bare
    /// `String.StartsWith` says it is. That is one grid position away from being wrong on a
    /// figure with ten columns, which is a facet of a year of months.
    let covers (Key ancestor) (Key key) =
        key = ancestor
        || (key.Length > ancestor.Length
            && key.[ancestor.Length] = Separator
            && key.StartsWith(ancestor, System.StringComparison.Ordinal))
