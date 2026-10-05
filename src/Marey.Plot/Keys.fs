namespace Marey

// ======================================================================================
// §5.2 / D68 — THE NAMES THE PRODUCER GIVES WHAT IT DRAWS
//
// `Keyed` is the IR's stable identity, and until M3 the whole library produced exactly one of
// them: `Keyed(Key "figure", …)` at the root of every figure. So the diff §5.2 designed the IR
// around — and that D1 rejected closures in order to protect — had a granularity of *one*. Any
// change to any figure changed the one keyed subtree, and `Keyed` was decoration.
//
// The identity was never missing. It was in the *other* node: `Annotated` already carries a
// panel's `(row, col)` and a layer's name, because §5.3 needs to say what a shape means. The two
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
// `Indices`, because a batch is a *renumbered subset* of the layer's points — mark 3 of batch 2
// is not row 3. Building a key from the position within the batch is D55's defect exactly, and
// the array that avoids it is already threaded through the producer. `mark` therefore takes the
// row, and the one caller that will construct it has `b.Indices.[j]` in scope.
//
// **What is not built here.** No node carries a `mark` key yet: a mark has no node of its own
// until something addresses it, and splitting a `MarkerBatch` to give it one is D69's business
// in the storyboard phase. The spelling is fixed now, and pinned by a test, so that phase does
// not invent a second one — the cost of agreeing on a string early is a string.
// ======================================================================================

/// D68 — the hierarchical names `Marey.Plot` gives the subtrees it produces.
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

    /// The root of every figure. The one key that existed before D68.
    let figure = Key "figure"

    /// A panel, by its grid position. Not by its index in `Figure.Panels`: a panel's identity is
    /// where it sits, and `Chart.facet` may add or reorder the array without moving any panel.
    let panel (row: int) (col: int) =
        child figure (sprintf "panel-%d-%d" row col)

    /// A layer, by its **ordinal** within the panel — see the note above on why not by name.
    let layer (panelKey: Key) (ordinal: int) = child panelKey (sprintf "layer-%d" ordinal)

    /// One mark, by its **original row** in the layer's data — not by its position within a
    /// channel batch, which is a renumbered subset (D55).
    ///
    /// Nothing emits a node carrying this yet; D69's batch split is its first producer. It is
    /// spelled here so that there is one spelling.
    let mark (layerKey: Key) (row: int) = child layerKey (sprintf "mark-%d" row)

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
