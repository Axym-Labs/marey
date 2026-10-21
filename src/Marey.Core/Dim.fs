namespace Marey

/// The SI dimension exponent vector: the *runtime* half of a unit.
///
/// Measures are erased at runtime, so this record is the only thing that survives to
/// the point where an axis label has to be printed. It is also the single source of truth for
/// unit *identity*: two units are dimensionally the same iff their `Dim` values are equal.
///
/// §12.6 is the rule this type exists to enforce: **never compose display strings, always
/// canonicalise from `Dim`.** `mul force metre` composes the symbol `kg·m/s/s·m` for what is
/// plainly `J`, while the `Dim` comes out exactly right at `L=2, M=1, T=-2`.
[<Struct; CustomEquality; NoComparison>]
type Dim =
    { /// Length (metre)
      L: int
      /// Mass (kilogram)
      M: int
      /// Time (second)
      T: int
      /// Electric current (ampere)
      I: int
      /// Thermodynamic temperature (kelvin)
      Th: int
      /// Amount of substance (mole)
      N: int
      /// Luminous intensity (candela)
      J: int }

    // Hand-written equality and hash. The generated structural versions are correct but go
    // through the generic equality machinery; `Dim` is compared on every canonicalisation
    // lookup and every axis-compatibility check, so it is worth the seven-int fast path.
    override this.Equals(o: obj) =
        match o with
        | :? Dim as d ->
            this.L = d.L
            && this.M = d.M
            && this.T = d.T
            && this.I = d.I
            && this.Th = d.Th
            && this.N = d.N
            && this.J = d.J
        | _ -> false

    override this.GetHashCode() =
        // Exponents are small; pack them into a single int with 8 bits of headroom each.
        let mutable h = 17
        h <- h * 31 + this.L
        h <- h * 31 + this.M
        h <- h * 31 + this.T
        h <- h * 31 + this.I
        h <- h * 31 + this.Th
        h <- h * 31 + this.N
        h <- h * 31 + this.J
        h

    interface System.IEquatable<Dim> with
        member this.Equals(d: Dim) =
            this.L = d.L
            && this.M = d.M
            && this.T = d.T
            && this.I = d.I
            && this.Th = d.Th
            && this.N = d.N
            && this.J = d.J

/// `Dim` under `add`/`neg` is the free abelian group on seven generators. §13 calls for
/// FsCheck tests of exactly those group laws; they are cheap and they catch sign errors in
/// the witness algebra that would otherwise surface as a wrong axis label three layers up.
[<RequireQualifiedAccess>]
module Dim =

    /// The dimension of a pure number. Identity of the group.
    let zero =
        { L = 0
          M = 0
          T = 0
          I = 0
          Th = 0
          N = 0
          J = 0 }

    let add (a: Dim) (b: Dim) =
        { L = a.L + b.L
          M = a.M + b.M
          T = a.T + b.T
          I = a.I + b.I
          Th = a.Th + b.Th
          N = a.N + b.N
          J = a.J + b.J }

    let neg (a: Dim) =
        { L = -a.L
          M = -a.M
          T = -a.T
          I = -a.I
          Th = -a.Th
          N = -a.N
          J = -a.J }

    let sub (a: Dim) (b: Dim) = add a (neg b)

    /// Raise to an integer power — the group action of Z. `pow 2 metre = m^2`.
    let pow (n: int) (a: Dim) =
        { L = a.L * n
          M = a.M * n
          T = a.T * n
          I = a.I * n
          Th = a.Th * n
          N = a.N * n
          J = a.J * n }

    /// True for a pure number. Load-bearing: the argument of `log`, `exp`, `sin` must be
    /// dimensionless, and a scale that takes a logarithm has to say so.
    let isDimensionless (a: Dim) = a = zero

    /// The seven base dimensions, in SI order, paired with their base-unit symbols.
    /// The canonical iteration order for any renderer.
    let basis: (string * (Dim -> int)) list =
        [ "kg", fun d -> d.M
          "m", fun d -> d.L
          "s", fun d -> d.T
          "A", fun d -> d.I
          "K", fun d -> d.Th
          "mol", fun d -> d.N
          "cd", fun d -> d.J ]

    /// Exponents as a list, skipping zeros. The input to every `UnitStyle` renderer; keeping
    /// the decomposition here means Marey.Units only decides *typography*, never *content*.
    let exponents (d: Dim) =
        basis
        |> List.choose (fun (sym, get) ->
            let e = get d
            if e = 0 then None else Some(sym, e))

    /// A deliberately plain fallback rendering: `kg·m^2·s^-2`. Marey.Units replaces this
    /// with prefix-aware, style-aware output; Core keeps one so it is never *unable* to
    /// print a unit, even with no registry loaded.
    let show (d: Dim) =
        match exponents d with
        | [] -> "1"
        | xs ->
            xs
            |> List.map (fun (sym, e) -> if e = 1 then sym else sprintf "%s^%d" sym e)
            |> String.concat "·"

    // ---- Base dimensions -------------------------------------------------------------

    let length = { zero with L = 1 }
    let mass = { zero with M = 1 }
    let time = { zero with T = 1 }
    let current = { zero with I = 1 }
    let temperature = { zero with Th = 1 }
    let amount = { zero with N = 1 }
    let luminosity = { zero with J = 1 }
