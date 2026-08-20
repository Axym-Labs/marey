namespace Marey

/// §9 / D45 — **an ordered level set**: the distinct values of a categorical key, in the order
/// they will be laid out.
///
/// This is the half of the discrete scale that a category type actually reaches. D45 put the
/// scale itself in `Marey.Core` as `ScaleKind.Ordinal` over `float<1>`, indexed by *level
/// number* rather than by the key — because a `Series` holds `float[]` and something has to turn
/// `"treated"` into a number before the erasure boundary. This type is that something, and it
/// lives here because here is where the key type `'k` is still known.
///
/// It carries no function fields, for D1's reason: two independently built level sets over the
/// same data must compare equal, and a closure has reference equality.
type Levels<'k when 'k: equality> =
    { /// The distinct key values, in layout order. Level `i` is `Values.[i]`.
      Values: 'k[]
      /// What the axis prints for each level, index-aligned with `Values`.
      Labels: string[] }

[<RequireQualifiedAccess>]
module Levels =

    /// **First appearance, not sorted, and this is a decision rather than an accident.**
    ///
    /// A categorical axis usually arrives in a meaningful order — `control` before `treated`
    /// before `washout`, morning before evening, an experiment's own sequence — and sorting
    /// destroys it in exactly the case where it was information. Alphabetical order also
    /// silently depends on the *labels*, so renaming a level would reorder the bands.
    ///
    /// First appearance keeps whatever order the data has, which is the order the caller can
    /// see and control, and it is deterministic for a deterministic input: the membership test
    /// is a hash set but the *order* comes from the sequence, never from enumerating one.
    /// `sorted` is one call away for the case where alphabetical is what was wanted.
    let ofSeqWith (label: 'k -> string) (keys: 'k seq) : Levels<'k> =
        let seen = System.Collections.Generic.HashSet<'k>(HashIdentity.Structural)
        let order = ResizeArray<'k>()

        for k in keys do
            if seen.Add k then order.Add k

        let values = order.ToArray()

        { Values = values
          Labels = values |> Array.map label }

    /// Level labels from `string`, which is identity for a string key and the case name for a
    /// union — the two ways a category is usually spelled in F#.
    let ofSeq (keys: 'k seq) : Levels<'k> = ofSeqWith string keys

    /// The same levels in ascending key order, for the case where the data's own order carries
    /// nothing. Sorts on the **key**, not the label, so it does not change when a level is
    /// renamed.
    let sorted (ls: Levels<'k>) : Levels<'k> when 'k: comparison =
        let pairs = Array.zip ls.Values ls.Labels |> Array.sortBy fst

        { Values = pairs |> Array.map fst
          Labels = pairs |> Array.map snd }

    /// Reverse the layout order. The counterpart of `Scale.reverse`, which reverses the level
    /// array for the same reason — a category's direction is its order, not its domain.
    let reverse (ls: Levels<'k>) : Levels<'k> =
        { Values = Array.rev ls.Values
          Labels = Array.rev ls.Labels }

    let count (ls: Levels<'k>) = ls.Values.Length

    /// The level number of a key, or `-1`. The total function, for a caller that has something
    /// better to do with an unknown key than fail.
    let tryIndexOf (ls: Levels<'k>) (k: 'k) : int =
        System.Array.IndexOf(ls.Values, k)

    /// **The x array of a categorical series**: each key as its level number.
    ///
    /// A key that is not in the level set raises rather than being dropped or clamped. Both of
    /// the quiet alternatives put a mark somewhere it does not belong — off the panel at level
    /// `-1`, or on top of an unrelated band — and a level set that does not cover its own data
    /// is a caller error worth hearing about at the point it happens.
    let positions (ls: Levels<'k>) (keys: 'k[]) : float[] =
        let index = System.Collections.Generic.Dictionary<'k, int>(HashIdentity.Structural)

        ls.Values |> Array.iteri (fun i v -> index.[v] <- i)

        keys
        |> Array.map (fun k ->
            match index.TryGetValue k with
            | true, i -> float i
            | _ ->
                failwithf
                    "Marey: '%O' is not one of the %d levels of this category (%s)."
                    k
                    ls.Values.Length
                    (System.String.Join(", ", ls.Labels)))

    /// The scale these levels lay out: one band each, level `i` at index `i` (D45).
    let scale (ls: Levels<'k>) : Scale<1> = Scale.ordinal ls.Labels
