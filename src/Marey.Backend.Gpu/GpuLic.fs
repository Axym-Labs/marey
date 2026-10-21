namespace Marey

open Silk.NET.OpenGL

/// Line integral convolution on the GPU: one fragment per raster pixel, each tracing its
/// own streamline both ways with `FieldMarks`' RK4 and stopping rules and averaging the noise
/// it passes. The exact convolution at the pixel's centre, where D90's fast LIC averages several
/// overlapping windows; on the same noise the two correlate 0.90 (the M5.6 inventory). 1.6 ms at
/// 1920 × 1200 on radeonsi, against 1361 ms for fast LIC on the CPU.
module internal GpuLic =

    let shader =
        """#version 330 core
uniform sampler2D X; uniform sampler2D Y; uniform sampler2D U; uniform sampler2D W; uniform sampler2D N;
uniform int nx; uniform int ny; uniform int rw; uniform int rh; uniform int n;
uniform float stepLength; uniform float slowest;
out vec4 frag;
float xs(int i) { return texelFetch(X, ivec2(i, 0), 0).x; }
float ys(int j) { return texelFetch(Y, ivec2(j, 0), 0).x; }
float coord(bool alongX, int k) { return alongX ? xs(k) : ys(k); }
// The node below a coordinate and the fraction past it, by bisection, as FieldMarks' interpolate.
bool locate(bool alongX, int m, float c, out int lo, out float f) {
    if (c < coord(alongX, 0) || c > coord(alongX, m - 1)) return false;
    lo = 0;
    int hi = m - 1;
    while (hi - lo > 1) {
        int mid = (lo + hi) / 2;
        if (coord(alongX, mid) <= c) lo = mid; else hi = mid;
    }
    float a = coord(alongX, lo);
    f = (c - a) / (coord(alongX, lo + 1) - a);
    return true;
}
float field(sampler2D T, int i, int j, float fx, float fy) {
    float a00 = texelFetch(T, ivec2(i, j), 0).x;
    float a10 = texelFetch(T, ivec2(i + 1, j), 0).x;
    float a01 = texelFetch(T, ivec2(i, j + 1), 0).x;
    float a11 = texelFetch(T, ivec2(i + 1, j + 1), 0).x;
    float a = a00 + (a10 - a00) * fx;
    float b = a01 + (a11 - a01) * fx;
    return a + (b - a) * fy;
}
bool dir(vec2 r, out vec2 d) {
    if (r.x < 0.0 || r.x > float(rw) || r.y < 0.0 || r.y > float(rh)) return false;
    float x = xs(0) + r.x / float(rw) * (xs(nx - 1) - xs(0));
    float y = ys(0) + r.y / float(rh) * (ys(ny - 1) - ys(0));
    int i; int j; float fx; float fy;
    if (!locate(true, nx, x, i, fx) || !locate(false, ny, y, j, fy)) return false;
    float u = field(U, i, j, fx, fy);
    float v = field(W, i, j, fx, fy);
    float s = sqrt(u * u + v * v);
    if (isnan(s) || isinf(s) || !(s > slowest)) return false;
    d = vec2(u / s, v / s);
    return true;
}
bool rk4(vec2 p, float s, out vec2 q) {
    vec2 a; vec2 b; vec2 c; vec2 d;
    if (!dir(p, a)) return false;
    if (!dir(p + s / 2.0 * a, b)) return false;
    if (!dir(p + s / 2.0 * b, c)) return false;
    if (!dir(p + s * c, d)) return false;
    if (dot(a, b) < 0.0 || dot(b, c) < 0.0 || dot(c, d) < 0.0) return false;
    q = p + s / 6.0 * (a + 2.0 * b + 2.0 * c + d);
    return true;
}
float noise(vec2 p) {
    ivec2 k = ivec2(min(rw - 1, max(0, int(floor(p.x)))), min(rh - 1, max(0, int(floor(p.y)))));
    return texelFetch(N, k, 0).x;
}
void main() {
    vec2 seed = gl_FragCoord.xy;
    vec2 d;
    if (!dir(seed, d)) { frag = vec4(uintBitsToFloat(0x7fc00000u)); return; }
    float sum = noise(seed);
    float count = 1.0;
    for (int side = 0; side < 2; side++) {
        float s = side == 0 ? stepLength : -stepLength;
        vec2 p = seed;
        for (int k = 0; k < n; k++) {
            vec2 q;
            if (!rk4(p, s, q)) break;
            p = q;
            sum += noise(p);
            count += 1.0;
        }
    }
    frag = vec4(sum / count, 0.0, 0.0, 0.0);
}
"""

    /// The job's intensities, or `None` when its raster is larger than a texture may be here.
    let run (c: GpuCompute) (job: LicJob) : float[] option =
        let gl = c.GL
        let largest = gl.GetInteger GetPName.MaxTextureSize
        let nx, ny = job.Xs.Length, job.Ys.Length

        if job.Width > largest || job.Height > largest || nx > largest || ny > largest then
            None
        else
            for name, w, h in [ "lic.x", nx, 1; "lic.y", ny, 1; "lic.u", nx, ny; "lic.w", nx, ny; "lic.noise", job.Width, job.Height; "lic.out", job.Width, job.Height ] do
                c.Surface(name, w, h, false) |> ignore

            c.Upload("lic.x", job.Xs)
            c.Upload("lic.y", job.Ys)
            c.Upload("lic.u", job.U)
            c.Upload("lic.w", job.W)
            c.Upload("lic.noise", job.Noise)
            let program = c.Program("lic", shader)

            c.Pass(
                program,
                "lic.out",
                [ "X", "lic.x"; "Y", "lic.y"; "U", "lic.u"; "W", "lic.w"; "N", "lic.noise" ],
                fun p ->
                    for name, v in [ "nx", nx; "ny", ny; "rw", job.Width; "rh", job.Height; "n", job.HalfLength ] do
                        gl.Uniform1(c.Uniform(p, name), v)

                    gl.Uniform1(c.Uniform(p, "stepLength"), float32 job.Step)
                    gl.Uniform1(c.Uniform(p, "slowest"), float32 job.Slowest)
            )

            Some(c.Read "lic.out")
