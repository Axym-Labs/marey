namespace Marey

open System
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// D147 — GPU SOLVERS WITH THE CPU'S TYPES.
//
// `GpuSim.stam` is `Stam.step` on a renderer's GPU: a `Step` over the same `Fluid`, so `Sim`,
// `trajectory`, films and the time control take it unchanged. Each call uploads the state, runs
// the step as fragment passes (D139), and reads the state back. Nothing carries from one call to
// the next but compiled programs and allocated textures, so two seeks of one trajectory, queued on
// the render thread one after the other, get what a straight run gets, bit for bit on one device.
//
// **The same arithmetic as the CPU.** Every pass computes the CPU step's expression for one node,
// in the same order, in float32 rather than float64. Bilinear interpolation is written out with
// texelFetch; red-black half-sweeps read only the old values, as the CPU's do; the multigrid has the
// same levels, cycle and schedule. So the CPU step is the GPU step's oracle (D140): after 200
// steps the two agree to about 10⁻⁴ of the field's maximum on radeonsi and on llvmpipe, and the
// two drivers do not agree with each other to the bit.
//
// **The transfer is the price** (D147 (a)): 0.25 ms of upload and read-back at 256². `stamWith k`
// runs k steps of h/k a call, one transfer for k steps.
// ======================================================================================

/// The lattices of the GPU solvers, as the shaders are told them.
type internal GLat =
    { Mx: int
      My: int
      Stride: int
      Rows: int
      WrapX: bool
      WrapY: bool
      FixX: bool
      FixY: bool
      Ox: float
      Oy: float }

module internal GpuStam =

    let lattices (nx: int) (ny: int) (px: bool) (py: bool) =
        let u =
            { Mx = (if px then nx else nx + 1); My = ny; Stride = nx + 1; Rows = ny
              WrapX = px; WrapY = py; FixX = not px; FixY = false; Ox = 0.0; Oy = 0.5 }

        let v =
            { Mx = nx; My = (if py then ny else ny + 1); Stride = nx; Rows = ny + 1
              WrapX = px; WrapY = py; FixX = false; FixY = not py; Ox = 0.5; Oy = 0.0 }

        let c =
            { Mx = nx; My = ny; Stride = nx; Rows = ny
              WrapX = px; WrapY = py; FixX = false; FixY = false; Ox = 0.5; Oy = 0.5 }

        u, v, c

    /// What every Stam pass shares: the lattice helpers, as `Stam`'s CPU code has them.
    let header =
        """#version 330 core
out vec4 frag;
bool isFixed(ivec4 L, ivec2 F, int i, int j) {
    return (F.x != 0 && (i == 0 || i == L.x - 1)) || (F.y != 0 && (j == 0 || j == L.y - 1));
}
// A periodic face's stored last column or row is its first.
ivec2 node(ivec4 L, ivec2 p) { return ivec2(p.x >= L.x ? p.x - L.x : p.x, p.y >= L.y ? p.y - L.y : p.y); }
float at(sampler2D t, int i, int j) { return texelFetch(t, ivec2(i, j), 0).x; }
void axis(bool wrap, int m, float a, out int i0, out int i1, out float f) {
    if (wrap) {
        float p = float(m);
        float w = a - p * floor(a / p);
        int i = int(floor(w));
        if (i >= m) { i -= m; w -= p; }
        i0 = i; i1 = (i + 1 == m) ? 0 : i + 1; f = w - float(i);
    } else {
        float c = max(0.0, min(float(m - 1), a));
        int i = min(m - 2, int(floor(c)));
        i0 = i; i1 = i + 1; f = c - float(i);
    }
}
float sampleLat(sampler2D t, ivec4 L, vec2 O, float x, float y) {
    int i0; int i1; int j0; int j1; float fx; float fy;
    axis(L.z != 0, L.x, x - O.x, i0, i1, fx);
    axis(L.w != 0, L.y, y - O.y, j0, j1, fy);
    float a00 = at(t, i0, j0); float a10 = at(t, i1, j0);
    float a01 = at(t, i0, j1); float a11 = at(t, i1, j1);
    float lo = a00 + fx * (a10 - a00);
    float hi = a01 + fx * (a11 - a01);
    return lo + fy * (hi - lo);
}
void around(sampler2D t, ivec4 L, int i, int j, out float s, out float n) {
    s = 0.0; n = 0.0;
    if (i > 0) { s += at(t, i - 1, j); n += 1.0; } else if (L.z != 0) { s += at(t, L.x - 1, j); n += 1.0; }
    if (i < L.x - 1) { s += at(t, i + 1, j); n += 1.0; } else if (L.z != 0) { s += at(t, 0, j); n += 1.0; }
    if (j > 0) { s += at(t, i, j - 1); n += 1.0; } else if (L.w != 0) { s += at(t, i, L.y - 1); n += 1.0; }
    if (j < L.y - 1) { s += at(t, i, j + 1); n += 1.0; } else if (L.w != 0) { s += at(t, i, 0); n += 1.0; }
}
"""

    let advect =
        header
        + """
uniform sampler2D U; uniform sampler2D V; uniform sampler2D A; uniform sampler2D Force; uniform sampler2D D;
uniform ivec4 LU; uniform ivec4 LV; uniform ivec4 LA; uniform ivec2 FA;
uniform vec2 OU; uniform vec2 OV; uniform vec2 OA;
uniform float hc; uniform float hs; uniform float buoy; uniform int forced; uniform int buoyant; uniform int nx; uniform int ny;
void main() {
    ivec2 q = node(LA, ivec2(gl_FragCoord.xy));
    int i = q.x; int j = q.y;
    if (isFixed(LA, FA, i, j)) { frag = vec4(0.0); return; }
    float x = float(i) + OA.x;
    float y = float(j) + OA.y;
    float px = x - hc * sampleLat(U, LU, OU, x, y);
    float py = y - hc * sampleLat(V, LV, OV, x, y);
    if (LA.z == 0) px = max(0.0, min(float(nx), px));
    if (LA.w == 0) py = max(0.0, min(float(ny), py));
    float v = sampleLat(A, LA, OA, px, py);
    if (forced != 0) {
        float extra = at(Force, i, j);
        if (buoyant != 0) {
            int below = j > 0 ? j - 1 : ny - 1;
            int above = j < ny ? j : 0;
            extra = extra + buoy * (0.5 * (at(D, i, below) + at(D, i, above)));
        }
        v = v + hs * extra;
    }
    frag = vec4(v, 0.0, 0.0, 0.0);
}
"""

    /// `A + k·B`, or `k·A`, or `A / k`, node by node.
    let arith =
        header
        + """
uniform sampler2D A; uniform sampler2D B; uniform float k; uniform int mode;
void main() {
    ivec2 p = ivec2(gl_FragCoord.xy);
    float a = at(A, p.x, p.y);
    frag = vec4(mode == 0 ? a + k * at(B, p.x, p.y) : (mode == 1 ? k * a : a / k), 0.0, 0.0, 0.0);
}
"""

    let sor =
        header
        + """
uniform sampler2D X; uniform sampler2D B; uniform ivec4 L; uniform ivec2 F;
uniform float a; uniform float c; uniform float omega; uniform int colour;
void main() {
    ivec2 q = node(L, ivec2(gl_FragCoord.xy));
    int i = q.x; int j = q.y;
    float v = at(X, i, j);
    if (!isFixed(L, F, i, j) && (i + j) % 2 == colour) {
        float s; float n;
        around(X, L, i, j, s, n);
        float gs = (at(B, i, j) + a * s) / (c + a * n);
        v = v + omega * (gs - v);
    }
    frag = vec4(v, 0.0, 0.0, 0.0);
}
"""

    let residual =
        header
        + """
uniform sampler2D X; uniform sampler2D B; uniform ivec4 L; uniform float a; uniform float c;
void main() {
    ivec2 p = ivec2(gl_FragCoord.xy);
    float s; float n;
    around(X, L, p.x, p.y, s, n);
    frag = vec4(at(B, p.x, p.y) - ((c + a * n) * at(X, p.x, p.y) - a * s), 0.0, 0.0, 0.0);
}
"""

    let restrict =
        header
        + """
uniform sampler2D R;
void main() {
    ivec2 p = ivec2(gl_FragCoord.xy) * 2;
    frag = vec4(0.25 * (at(R, p.x, p.y) + at(R, p.x + 1, p.y) + at(R, p.x, p.y + 1) + at(R, p.x + 1, p.y + 1)), 0.0, 0.0, 0.0);
}
"""

    let prolong =
        header
        + """
uniform sampler2D X; uniform sampler2D E; uniform ivec4 LC;
int side(bool wrap, int m, int p, int q) { return (q >= 0 && q < m) ? q : (wrap ? (q + m) % m : p); }
void main() {
    ivec2 f = ivec2(gl_FragCoord.xy);
    int pi = f.x / 2; int pj = f.y / 2;
    int si = side(LC.z != 0, LC.x, pi, f.x % 2 == 0 ? pi - 1 : pi + 1);
    int sj = side(LC.w != 0, LC.y, pj, f.y % 2 == 0 ? pj - 1 : pj + 1);
    float e = 0.5625 * at(E, pi, pj) + 0.1875 * at(E, si, pj) + 0.1875 * at(E, pi, sj) + 0.0625 * at(E, si, sj);
    frag = vec4(at(X, f.x, f.y) + e, 0.0, 0.0, 0.0);
}
"""

    let divergence =
        header
        + """
uniform sampler2D U; uniform sampler2D V; uniform float hx;
void main() {
    ivec2 p = ivec2(gl_FragCoord.xy);
    int i = p.x; int j = p.y;
    frag = vec4(-(at(U, i + 1, j) - at(U, i, j) + at(V, i, j + 1) - at(V, i, j)) * hx, 0.0, 0.0, 0.0);
}
"""

    /// `u −= ∇φ` on a face lattice's free nodes: `axis` 0 for the x faces, 1 for the y faces.
    let gradient =
        header
        + """
uniform sampler2D A; uniform sampler2D P; uniform ivec4 L; uniform ivec2 F; uniform int axisY; uniform int nx; uniform int ny; uniform float hx;
void main() {
    ivec2 q = node(L, ivec2(gl_FragCoord.xy));
    int i = q.x; int j = q.y;
    float u = at(A, i, j);
    if (!isFixed(L, F, i, j)) {
        if (axisY == 0) {
            int left = i > 0 ? i - 1 : nx - 1;
            u = u - (at(P, i < nx ? i : 0, j) - at(P, left, j)) / hx;
        } else {
            int below = j > 0 ? j - 1 : ny - 1;
            u = u - (at(P, i, j < ny ? j : 0) - at(P, i, below)) / hx;
        }
    }
    frag = vec4(u, 0.0, 0.0, 0.0);
}
"""

    /// The over-relaxation of each half-sweep, as `Stam`'s CPU schedule computes it.
    let omegas (rho: float) (halves: int) =
        let w = Array.zeroCreate halves
        let r2 = rho * rho

        for k in 0 .. halves - 1 do
            w.[k] <- if k = 0 then 1.0 elif k = 1 then 1.0 / (1.0 - r2 / 2.0) else 1.0 / (1.0 - r2 * w.[k - 1] / 4.0)

        w

    /// One call: upload, `substeps` steps of `hs`, read back.
    let run
        (c: GpuCompute)
        (nx: int)
        (ny: int)
        (px: bool)
        (py: bool)
        (hx: float)
        (nu: float)
        (kappa: float)
        (buoy: float)
        (cycles: int)
        (sweeps: int)
        (forcing: float[] * float[] * float[])
        (state: float[] * float[] * float[] * float[])
        (hs: float)
        (substeps: int)
        =
        let gl = c.GL
        let lu, lv, lc = lattices nx ny px py
        let fu, fv, src = forcing
        let u0, v0, d0, p0 = state

        let surface (name: string) (l: GLat) = c.Surface(name, l.Stride, l.Rows, false) |> ignore

        for n in [ "u.a"; "u.b"; "u.c"; "u.f" ] do surface ("stam." + n) lu
        for n in [ "v.a"; "v.b"; "v.c"; "v.f" ] do surface ("stam." + n) lv
        for n in [ "d.a"; "d.b"; "d.c"; "d.f"; "p" ] do surface ("stam." + n) lc

        // The multigrid's levels, as `Stam.levels` halves them.
        let levels =
            let rec go (l: GLat) (a: float) acc =
                let acc = (l, a) :: acc

                if l.Mx % 2 = 0 && l.My % 2 = 0 && l.Mx >= 4 && l.My >= 4 then
                    let mx, my = l.Mx / 2, l.My / 2
                    go { l with Mx = mx; My = my; Stride = mx; Rows = my } (a / 4.0) acc
                else
                    List.rev acc

            go lc 1.0 [] |> Array.ofList

        levels
        |> Array.iteri (fun k (l, _) ->
            for n in [ "x"; "y"; "b"; "r" ] do
                surface (sprintf "stam.mg%d.%s" k n) l)

        c.Upload("stam.u.a", u0)
        c.Upload("stam.v.a", v0)
        c.Upload("stam.d.a", d0)
        c.Upload("stam.p", p0)
        c.Upload("stam.u.f", fu)
        c.Upload("stam.v.f", fv)
        c.Upload("stam.d.f", src)

        let i1 (p: uint32) (name: string) (v: int) = gl.Uniform1(c.Uniform(p, name), v)
        let f1 (p: uint32) (name: string) (v: float) = gl.Uniform1(c.Uniform(p, name), float32 v)

        let lat (p: uint32) (name: string) (l: GLat) =
            gl.Uniform4(c.Uniform(p, "L" + name), l.Mx, l.My, (if l.WrapX then 1 else 0), (if l.WrapY then 1 else 0))
            let fix = c.Uniform(p, "F" + name)
            if fix >= 0 then gl.Uniform2(fix, (if l.FixX then 1 else 0), (if l.FixY then 1 else 0))
            let off = c.Uniform(p, "O" + name)
            if off >= 0 then gl.Uniform2(off, float32 l.Ox, float32 l.Oy)

        let pAdvect = c.Program("stam.advect", advect)
        let pArith = c.Program("stam.arith", arith)
        let pSor = c.Program("stam.sor", sor)
        let pResidual = c.Program("stam.residual", residual)
        let pRestrict = c.Program("stam.restrict", restrict)
        let pProlong = c.Program("stam.prolong", prolong)
        let pDiv = c.Program("stam.div", divergence)
        let pGrad = c.Program("stam.gradient", gradient)

        /// Red-black SOR on a lattice: from the surface `x`, ping-ponging with `other`; returns the
        /// surface holding the answer.
        let sorOn (l: GLat) (a: float) (cc: float) (rho: float) (count: int) (b: string) (x: string) (other: string) =
            let schedule = omegas rho (2 * count)
            let mutable cur, next = x, other

            for k in 0 .. 2 * count - 1 do
                c.Pass(
                    pSor,
                    next,
                    [ "X", cur; "B", b ],
                    fun p ->
                        gl.Uniform4(c.Uniform(p, "L"), l.Mx, l.My, (if l.WrapX then 1 else 0), (if l.WrapY then 1 else 0))
                        gl.Uniform2(c.Uniform(p, "F"), (if l.FixX then 1 else 0), (if l.FixY then 1 else 0))
                        f1 p "a" a
                        f1 p "c" cc
                        f1 p "omega" schedule.[k]
                        i1 p "colour" (k % 2)
                )

                let t = cur
                cur <- next
                next <- t

            cur

        let latC (p: uint32) (name: string) (l: GLat) =
            gl.Uniform4(c.Uniform(p, name), l.Mx, l.My, (if l.WrapX then 1 else 0), (if l.WrapY then 1 else 0))

        let rec vcycle (k: int) (x: string) : string =
            let l, a = levels.[k]
            let b = sprintf "stam.mg%d.b" k
            let other = if x.EndsWith ".x" then sprintf "stam.mg%d.y" k else sprintf "stam.mg%d.x" k

            if k = levels.Length - 1 then
                let m = max l.Mx l.My
                let rho = a * (2.0 + 2.0 * cos (Math.PI / float m)) / (0.0 + 4.0 * a)
                sorOn l a 0.0 rho (4 * m) b x other
            else
                let x1 = sorOn l a 0.0 0.0 2 b x other
                let r = sprintf "stam.mg%d.r" k

                c.Pass(pResidual, r, [ "X", x1; "B", b ], fun p -> latC p "L" l; f1 p "a" a; f1 p "c" 0.0)
                c.Pass(pRestrict, sprintf "stam.mg%d.b" (k + 1), [ "R", r ], ignore)
                c.Clear(sprintf "stam.mg%d.x" (k + 1))
                let ec = vcycle (k + 1) (sprintf "stam.mg%d.x" (k + 1))
                let x2 = if x1.EndsWith ".x" then sprintf "stam.mg%d.y" k else sprintf "stam.mg%d.x" k
                c.Pass(pProlong, x2, [ "X", x1; "E", ec ], fun p -> latC p "LC" (fst levels.[k + 1]))
                sorOn l a 0.0 0.0 2 b x2 x1

        let hc = hs / hx
        let mutable u, v, d = "stam.u.a", "stam.v.a", "stam.d.a"

        for _ in 1..substeps do
            let advectInto (target: string) (field: string) (l: GLat) (force: string) (forced: bool) (buoyant: bool) (dye: string) =
                c.Pass(
                    pAdvect,
                    target,
                    [ "U", u; "V", v; "A", field; "Force", force; "D", dye ],
                    fun p ->
                        lat p "U" lu
                        lat p "V" lv
                        lat p "A" l
                        f1 p "hc" hc
                        f1 p "hs" hs
                        f1 p "buoy" buoy
                        i1 p "forced" (if forced then 1 else 0)
                        i1 p "buoyant" (if buoyant then 1 else 0)
                        i1 p "nx" nx
                        i1 p "ny" ny
                )

            // Dye first, unforced, because the y faces' buoyancy reads it advected; then the
            // faces; then the source.
            let other (s: string) = if s.EndsWith ".a" then s.Replace(".a", ".b") else s.Replace(".b", ".a")
            let dAdv = other d
            advectInto dAdv d lc "stam.d.f" false false d
            let uAdv = other u
            advectInto uAdv u lu "stam.u.f" true false dAdv
            let vAdv = other v
            advectInto vAdv v lv "stam.v.f" true true dAdv
            c.Pass(pArith, d, [ "A", dAdv; "B", "stam.d.f" ], fun p -> f1 p "k" hs; i1 p "mode" 0)

            u <- uAdv
            v <- vAdv

            let a = nu * hs / (hx * hx)

            if a > 0.0 then
                // The right-hand side is the advected field, copied to `.c` while the sweeps ping-pong.
                c.Pass(pArith, "stam.u.c", [ "A", u; "B", u ], fun p -> f1 p "k" 1.0; i1 p "mode" 1)
                u <- sorOn lu a 1.0 0.0 sweeps "stam.u.c" u (other u)
                c.Pass(pArith, "stam.v.c", [ "A", v; "B", v ], fun p -> f1 p "k" 1.0; i1 p "mode" 1)
                v <- sorOn lv a 1.0 0.0 sweeps "stam.v.c" v (other v)

            let kd = kappa * hs / (hx * hx)

            if kd > 0.0 then
                c.Pass(pArith, "stam.d.c", [ "A", d; "B", d ], fun p -> f1 p "k" 1.0; i1 p "mode" 1)
                d <- sorOn lc kd 1.0 0.0 sweeps "stam.d.c" d (other d)

            // The projection.
            c.Pass(pDiv, "stam.mg0.b", [ "U", u; "V", v ], fun p -> f1 p "hx" hx)
            c.Pass(pArith, "stam.mg0.x", [ "A", "stam.p"; "B", "stam.p" ], fun p -> f1 p "k" hs; i1 p "mode" 1)
            let mutable phi = "stam.mg0.x"

            for _ in 1..cycles do
                phi <- vcycle 0 phi

            let gradientInto (target: string) (field: string) (l: GLat) (axisY: bool) =
                c.Pass(
                    pGrad,
                    target,
                    [ "A", field; "P", phi ],
                    fun p ->
                        gl.Uniform4(c.Uniform(p, "L"), l.Mx, l.My, (if l.WrapX then 1 else 0), (if l.WrapY then 1 else 0))
                        gl.Uniform2(c.Uniform(p, "F"), (if l.FixX then 1 else 0), (if l.FixY then 1 else 0))
                        i1 p "axisY" (if axisY then 1 else 0)
                        i1 p "nx" nx
                        i1 p "ny" ny
                        f1 p "hx" hx
                )

            let uNew = other u
            gradientInto uNew u lu false
            let vNew = other v
            gradientInto vNew v lv true
            u <- uNew
            v <- vNew

            if hs > 0.0 then
                c.Pass(pArith, "stam.p", [ "A", phi; "B", phi ], fun p -> f1 p "k" hs; i1 p "mode" 2)

        c.Read u, c.Read v, c.Read d, c.Read "stam.p"

/// D147 — GPU twins of `Marey.Sim`'s grid solvers, stepping the same states on a renderer's GPU.
///
/// ```fsharp
/// use gpu = new GpuRenderer()
/// let sim = Sim.ofStep 0.01<s> (GpuSim.stam gpu options) fluid
/// ```
///
/// Each call uploads the state, steps it, and reads it back; the result agrees with the CPU step
/// to about 10⁻⁴ of the field's maximum after 200 steps, and is the same on one device every time.
[<RequireQualifiedAccess>]
module GpuSim =

    /// `Stam.step` on the GPU, `substeps` steps of `h / substeps` for each call: one upload and one
    /// read-back however many steps run.
    let stamWith<[<Measure>] 'x, [<Measure>] 'q> (substeps: int) (gpu: GpuRenderer) (options: StamOptions<'x, 'q>) : Step<Fluid<'x, 'q>, s> =
        // The CPU step's checks, so the two refuse the same options.
        Stam.step options |> ignore

        if substeps < 1 then
            invalidArg "substeps" (sprintf "Marey: a GPU step runs at least one step a call; got %d." substeps)

        let kept = ref None

        fun _ h f ->
            let key = struct (f.Nx, f.Ny, float f.Cell, float f.X0, float f.Y0)

            // The forcing, sampled at the faces and centres as `Stam.step` samples it.
            let forcing =
                match kept.Value with
                | Some(k, fz) when k = key -> fz
                | _ ->
                    let hx = float f.Cell
                    let x0, y0 = float f.X0, float f.Y0
                    let w = LanguagePrimitives.FloatWithMeasure<'x>

                    let fz =
                        Array.init ((f.Nx + 1) * f.Ny) (fun k ->
                            let i, j = k % (f.Nx + 1), k / (f.Nx + 1)
                            let struct (a, _) = options.Force (w (x0 + float i * hx)) (w (y0 + (float j + 0.5) * hx))
                            float a),
                        Array.init (f.Nx * (f.Ny + 1)) (fun k ->
                            let i, j = k % f.Nx, k / f.Nx
                            let struct (_, b) = options.Force (w (x0 + (float i + 0.5) * hx)) (w (y0 + float j * hx))
                            float b),
                        Array.init (f.Nx * f.Ny) (fun k ->
                            float (options.Source (w (x0 + (float (k % f.Nx) + 0.5) * hx)) (w (y0 + (float (k / f.Nx) + 0.5) * hx))))

                    kept.Value <- Some(key, fz)
                    fz

            let plain (xs: float<'u>[]) : float[] = unbox<float[]> (box xs)
            let tagged (xs: float[]) : float<'u>[] = unbox<float<'u>[]> (box xs)
            let hs = float h / float substeps

            let u, v, d, p =
                gpu.Compute(fun c ->
                    GpuStam.run
                        c
                        f.Nx
                        f.Ny
                        (f.XEdge = FluidEdge.Periodic)
                        (f.YEdge = FluidEdge.Periodic)
                        (float f.Cell)
                        (float options.Viscosity)
                        (float options.Diffusion)
                        (float options.Buoyancy)
                        options.Cycles
                        options.Sweeps
                        forcing
                        (plain f.UFaces, plain f.VFaces, plain f.Dye, plain f.Pressure)
                        hs
                        substeps)

            { f with
                UFaces = tagged u
                VFaces = tagged v
                Dye = tagged d
                Pressure = (if hs > 0.0 then tagged p else f.Pressure) }

    /// `Stam.step` on the GPU of a renderer: the same `Fluid`, the same options, one step a call.
    let stam<[<Measure>] 'x, [<Measure>] 'q> (gpu: GpuRenderer) (options: StamOptions<'x, 'q>) : Step<Fluid<'x, 'q>, s> =
        stamWith 1 gpu options
