namespace Marey

open System
open Silk.NET.OpenGL
open SkiaSharp

/// `Markers` as one instanced draw, the case batching is about: Skia
/// builds and issues a path per mark on the CPU whichever surface it draws on, 212 ms for 10⁵,
/// while one instanced draw of 10⁶ is about 2.5 ms.
///
/// **The same marks as `SkiaPainter` draws.** Each shape is the outline `markerOutline` builds, as
/// a signed distance in device pixels, and the polygons are the intersections of their edges'
/// half-planes, so a stroke's corners are mitered as Skia's default join miters them. Each mark is
/// filled and then stroked and composited on its own, in one pass: `S + F·(1 − S.a)` over
/// the destination is the same as filling and then stroking it. A rectangular clip is applied in
/// the shader with an antialiased edge, as Skia's clip has.
///
/// Coordinates reach the GPU as `float32` in device pixels, after the panel transform and the
/// render scale have been applied in `float`: a coordinate on a 4000-pixel surface keeps a
/// sub-pixel of precision to spare.
///
/// **At a render scale every px length is scaled with the positions.** The canvas draws `scale`
/// device pixels per px, and a mark drawn beside it has to as well: its size and its outline
/// width are px, and so is the painter's clip. The antialiased edge stays one device pixel wide,
/// as Skia's does on a scaled canvas.
[<Sealed>]
type internal MarkerDrawer(gl: GL) =

    static let vertexSource =
        """#version 330 core
layout(location = 0) in vec2 corner;
layout(location = 1) in vec2 centre;
uniform vec2 viewport;
uniform float reach;
out vec2 local;
void main() {
    local = corner * reach;
    vec2 p = centre + local;
    gl_Position = vec4(p.x / viewport.x * 2.0 - 1.0, 1.0 - p.y / viewport.y * 2.0, 0.0, 1.0);
}
"""

    static let fragmentSource =
        """#version 330 core
in vec2 local;
uniform int shape;
uniform float radius;
uniform float halfStroke;
uniform vec4 fillColour;
uniform vec4 strokeColour;
uniform vec4 clipRect;
uniform float viewportHeight;
out vec4 frag;

// Outside distance is the largest of the edges', which miters a convex corner.
float box(vec2 p, vec2 h) {
    vec2 q = abs(p) - h;
    return max(q.x, q.y);
}

float distanceTo(vec2 p) {
    float r = radius;
    if (shape == 0) return length(p) - r;
    if (shape == 1) return box(p, vec2(r));
    if (shape == 2) return (abs(p.x) + abs(p.y) - r) * 0.70710678;
    if (shape == 3 || shape == 4) {
        // Device space is y-down; an upward triangle has its apex at (0, -r).
        vec2 q = vec2(p.x, shape == 3 ? p.y : -p.y);
        float base = q.y - 0.5 * r;
        float right = dot(vec2(0.86602540, -0.5), q) - 0.5 * r;
        float left = dot(vec2(-0.86602540, -0.5), q) - 0.5 * r;
        return max(base, max(right, left));
    }
    // Plus, and the cross, which is the plus turned by 45 degrees.
    vec2 q = shape == 6 ? vec2(p.x + p.y, p.y - p.x) * 0.70710678 : p;
    float t = r / 3.0;
    return min(box(q, vec2(r, t)), box(q, vec2(t, r)));
}

void main() {
    float d = distanceTo(local);
    float fill = clamp(0.5 - d, 0.0, 1.0);
    float stroke = halfStroke > 0.0 ? clamp(0.5 - (abs(d) - halfStroke), 0.0, 1.0) : 0.0;
    vec2 device = vec2(gl_FragCoord.x, viewportHeight - gl_FragCoord.y);
    float clipX = clamp(min(device.x - clipRect.x, clipRect.z - device.x) + 0.5, 0.0, 1.0);
    float clipY = clamp(min(device.y - clipRect.y, clipRect.w - device.y) + 0.5, 0.0, 1.0);
    vec4 f = fillColour * fill;
    vec4 s = strokeColour * stroke;
    vec4 c = (s + f * (1.0 - s.a)) * (clipX * clipY);
    if (c.a <= 0.0) discard;
    frag = c;
}
"""

    let compile (kind: ShaderType) (source: string) =
        let shader = gl.CreateShader kind
        gl.ShaderSource(shader, source)
        gl.CompileShader shader
        let mutable ok = 0
        gl.GetShader(shader, ShaderParameterName.CompileStatus, &ok)

        if ok = 0 then
            invalidOp ("Marey.GpuRenderer: the marker shader did not compile: " + gl.GetShaderInfoLog shader)

        shader

    let program =
        let vs = compile ShaderType.VertexShader vertexSource
        let fs = compile ShaderType.FragmentShader fragmentSource
        let p = gl.CreateProgram()
        gl.AttachShader(p, vs)
        gl.AttachShader(p, fs)
        gl.LinkProgram p
        let mutable ok = 0
        gl.GetProgram(p, ProgramPropertyARB.LinkStatus, &ok)

        if ok = 0 then
            invalidOp ("Marey.GpuRenderer: the marker shader did not link: " + gl.GetProgramInfoLog p)

        gl.DeleteShader vs
        gl.DeleteShader fs
        p

    let uniform (name: string) = gl.GetUniformLocation(program, name)
    let uViewport = uniform "viewport"
    let uReach = uniform "reach"
    let uShape = uniform "shape"
    let uRadius = uniform "radius"
    let uHalfStroke = uniform "halfStroke"
    let uFill = uniform "fillColour"
    let uStroke = uniform "strokeColour"
    let uClip = uniform "clipRect"
    let uViewportHeight = uniform "viewportHeight"

    let vao = gl.GenVertexArray()
    let quad = gl.GenBuffer()
    let instances = gl.GenBuffer()

    do
        gl.BindVertexArray vao
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, quad)
        gl.BufferData(BufferTargetARB.ArrayBuffer, ReadOnlySpan<float32>([| -1.0f; -1.0f; 1.0f; -1.0f; -1.0f; 1.0f; 1.0f; 1.0f |]), BufferUsageARB.StaticDraw)
        gl.EnableVertexAttribArray 0u
        gl.VertexAttribPointer(0u, 2, VertexAttribPointerType.Float, false, 8u, IntPtr.Zero.ToPointer())
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, instances)
        gl.EnableVertexAttribArray 1u
        gl.VertexAttribPointer(1u, 2, VertexAttribPointerType.Float, false, 8u, IntPtr.Zero.ToPointer())
        gl.VertexAttribDivisor(1u, 1u)
        gl.BindVertexArray 0u
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0u)

    /// A colour as the premultiplied RGBA the blend expects, each channel quantised to a byte
    /// first, as `SkiaColor.ofColor` quantises it for the CPU path.
    static let premultiplied (c: Color) =
        let k = SkiaColor.ofColor c
        let a = float32 k.Alpha / 255.0f
        float32 k.Red / 255.0f * a, float32 k.Green / 255.0f * a, float32 k.Blue / 255.0f * a, a

    /// Whether this drawer draws the batch as `SkiaPainter` would, under the given clip and
    /// layer depth. Anything it cannot match is left to Skia: a gradient, a dash, a join other
    /// than a miter long enough for the shape's corners, a clip that is not a rectangle, an
    /// opacity layer, and aliased drawing.
    static member Claims(b: MarkerBatch, clip: PainterClip, layerDepth: int, antialias: bool) : bool =
        let solid =
            function
            | Solid _ -> true
            | _ -> false

        let strokeOk =
            match b.Stroke with
            | None -> true
            | Some s ->
                solid s.Paint
                && s.Dash.Length = 0
                && float s.Width > 0.0
                && (b.Shape = MarkCircle || (s.Join = Miter && s.MiterLimit >= 2.0))

        antialias
        && layerDepth = 0
        && b.Points.Length > 0
        && (b.Fill.IsSome || b.Stroke.IsSome)
        && (match b.Fill with
            | Some p -> solid p
            | None -> true)
        && strokeOk
        && (match clip with
            | PainterClip.Shaped -> false
            | _ -> true)

    /// Draw a batch into the bound framebuffer of `width` × `height` device pixels, at `scale`
    /// device pixels per px. The caller has flushed Skia and resets Skia's view of the GL state
    /// afterwards.
    member _.Draw(framebuffer: uint32, width: int, height: int, scale: float, ctm: Affine<nd, px>, b: MarkerBatch, clip: PainterClip) =
        let centres = Array.zeroCreate<float32> (b.Points.Length * 2)

        for i in 0 .. b.Points.Length - 1 do
            let q = Affine.apply ctm b.Points.[i]
            centres.[2 * i] <- float32 (float q.X * scale)
            centres.[2 * i + 1] <- float32 (float q.Y * scale)

        let r = float32 (float b.Size * scale) / 2.0f

        let halfStroke =
            match b.Stroke with
            | Some s -> float32 (float s.Width * scale) / 2.0f
            | None -> 0.0f

        let shape =
            match b.Shape with
            | MarkCircle -> 0
            | MarkSquare -> 1
            | MarkDiamond -> 2
            | MarkTriangleUp -> 3
            | MarkTriangleDown -> 4
            | MarkPlus -> 5
            | MarkCross -> 6

        let colourOf =
            function
            | Some(Solid c) -> premultiplied c
            | _ -> 0.0f, 0.0f, 0.0f, 0.0f

        let fr, fg, fb, fa = colourOf b.Fill
        let sr, sg, sb, sa = colourOf (b.Stroke |> Option.map (fun s -> s.Paint))

        let clipRect =
            match clip with
            | PainterClip.Rectangle rect ->
                let k = float32 scale
                SKRect(rect.Left * k, rect.Top * k, rect.Right * k, rect.Bottom * k)
            | _ -> SKRect(-1.0e9f, -1.0e9f, 1.0e9f, 1.0e9f)

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer)
        gl.Viewport(0, 0, uint32 width, uint32 height)
        gl.Disable EnableCap.DepthTest
        gl.Disable EnableCap.StencilTest
        gl.Disable EnableCap.CullFace
        gl.ColorMask(true, true, true, true)
        gl.Enable EnableCap.Blend
        gl.BlendEquation BlendEquationModeEXT.FuncAdd
        gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha)

        // The scissor only saves fragments; the shader's clip is what draws the edge.
        let x0 = max 0 (int (floor clipRect.Left) - 1)
        let x1 = min width (int (ceil clipRect.Right) + 1)
        let y0 = max 0 (int (floor clipRect.Top) - 1)
        let y1 = min height (int (ceil clipRect.Bottom) + 1)
        gl.Enable EnableCap.ScissorTest
        gl.Scissor(x0, height - y1, uint32 (max 0 (x1 - x0)), uint32 (max 0 (y1 - y0)))

        gl.UseProgram program
        gl.Uniform2(uViewport, float32 width, float32 height)
        gl.Uniform1(uViewportHeight, float32 height)
        gl.Uniform1(uReach, r + halfStroke + 1.5f)
        gl.Uniform1(uShape, shape)
        gl.Uniform1(uRadius, r)
        gl.Uniform1(uHalfStroke, halfStroke)
        gl.Uniform4(uFill, fr, fg, fb, fa)
        gl.Uniform4(uStroke, sr, sg, sb, sa)
        gl.Uniform4(uClip, clipRect.Left, clipRect.Top, clipRect.Right, clipRect.Bottom)

        gl.BindVertexArray vao
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, instances)
        gl.BufferData(BufferTargetARB.ArrayBuffer, ReadOnlySpan<float32>(centres), BufferUsageARB.StreamDraw)
        gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4u, uint32 b.Points.Length)

        gl.BindVertexArray 0u
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0u)
        gl.UseProgram 0u
        gl.Disable EnableCap.ScissorTest

    interface IDisposable with
        member _.Dispose() =
            gl.DeleteBuffer quad
            gl.DeleteBuffer instances
            gl.DeleteVertexArray vao
            gl.DeleteProgram program
