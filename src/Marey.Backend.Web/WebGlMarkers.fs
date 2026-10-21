namespace Marey

open System
open Fable.Core

// ======================================================================================
// M5 — MARKER BATCHES ON WEBGL2
//
// The GPU backend's instanced draw, in the browser: one quad per mark, each shape a signed
// distance in device pixels, filled then stroked in one pass and composited on its own.
// The shader is `MarkerDrawer`'s, ported to GLSL ES 3.0, so a mark here is the mark the desktop
// draws, and both are Skia's outline.
//
// **The z-order question is answered by not having one.** The plan put two answers: a stack of
// canvases split at every `Markers` node, or WebGL only for marks that nothing but the panel
// lies under. Neither is needed. The batch is drawn on an offscreen WebGL2 canvas, cleared to
// transparent, and that canvas is drawn into the 2-D context with `drawImage` at the batch's
// place in the traversal, so what came before is under it and what comes after is over it, as
// the GPU backend's flush-draw-reset does it on one context. "Over" is associative, so the
// marks composited into a layer and the layer composited once is the marks composited one by
// one, to the byte's rounding.
//
// And it is drawn *through* the 2-D context, so the context's clip and the opacity layer it may
// be drawing into apply to it as to anything else: the shaped-clip and opacity-layer exceptions
// the desktop drawer has do not exist here.
//
// What it costs is a clear and a full-surface `drawImage` per batch, so a batch smaller than
// `MinimumBatch` is left to the canvas. Measured in Chromium on the M5 machine, median of seven:
// the two are level at 2000 marks (0.4 ms each), the canvas is ahead below and WebGL2 above,
// 7.2 against 77 ms at 10⁵ and 61 against 912 ms at 10⁶. Where WebGL2 is not available the canvas draws every batch, and says nothing, because the
// picture is the same.
// ======================================================================================

/// The members of `WebGL2RenderingContext` the marker drawer calls, by their JavaScript names.
[<AllowNullLiteral>]
type internal WebGl2 =
    abstract canvas: CanvasElement
    abstract createShader: kind: int -> obj
    abstract shaderSource: shader: obj * source: string -> unit
    abstract compileShader: shader: obj -> unit
    abstract getShaderParameter: shader: obj * name: int -> obj
    abstract getShaderInfoLog: shader: obj -> string
    abstract createProgram: unit -> obj
    abstract attachShader: program: obj * shader: obj -> unit
    abstract linkProgram: program: obj -> unit
    abstract getProgramParameter: program: obj * name: int -> obj
    abstract getProgramInfoLog: program: obj -> string
    abstract deleteShader: shader: obj -> unit
    abstract getUniformLocation: program: obj * name: string -> obj
    abstract createVertexArray: unit -> obj
    abstract bindVertexArray: vao: obj -> unit
    abstract createBuffer: unit -> obj
    abstract bindBuffer: target: int * buffer: obj -> unit
    abstract bufferData: target: int * data: float32[] * usage: int -> unit
    abstract enableVertexAttribArray: index: int -> unit
    abstract vertexAttribPointer: index: int * size: int * kind: int * normalized: bool * stride: int * offset: int -> unit
    abstract vertexAttribDivisor: index: int * divisor: int -> unit
    abstract viewport: x: int * y: int * w: int * h: int -> unit
    abstract clearColor: r: float * g: float * b: float * a: float -> unit
    abstract clear: mask: int -> unit
    abstract enable: cap: int -> unit
    abstract blendFunc: src: int * dst: int -> unit
    abstract useProgram: program: obj -> unit
    abstract uniform1f: location: obj * x: float -> unit
    abstract uniform1i: location: obj * x: int -> unit
    abstract uniform2f: location: obj * x: float * y: float -> unit
    abstract uniform4f: location: obj * x: float * y: float * z: float * w: float -> unit
    abstract drawArraysInstanced: mode: int * first: int * count: int * instances: int -> unit

module internal WebGlSurface =

    /// A WebGL2 context on a surface of its own, or `null` where there is none.
    [<Emit("(function (w, h) { var c = typeof OffscreenCanvas !== 'undefined' ? new OffscreenCanvas(w, h) : (typeof document !== 'undefined' ? Object.assign(document.createElement('canvas'), { width: w, height: h }) : null); return c && c.getContext('webgl2', { premultipliedAlpha: true, antialias: false, alpha: true }); })($0, $1)")>]
    let create (_width: int) (_height: int) : WebGl2 = jsNative

/// Marker batches drawn by WebGL2 and composited into a 2-D context.
[<Sealed>]
type WebGlMarkers(?minimumBatch: int) =

    static let vertexSource =
        """#version 300 es
precision highp float;
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

    // `MarkerDrawer`'s fragment shader without its clip, which the 2-D context applies.
    static let fragmentSource =
        """#version 300 es
precision highp float;
in vec2 local;
uniform int shape;
uniform float radius;
uniform float halfStroke;
uniform vec4 fillColour;
uniform vec4 strokeColour;
out vec4 frag;

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
        vec2 q = vec2(p.x, shape == 3 ? p.y : -p.y);
        float base = q.y - 0.5 * r;
        float right = dot(vec2(0.86602540, -0.5), q) - 0.5 * r;
        float left = dot(vec2(-0.86602540, -0.5), q) - 0.5 * r;
        return max(base, max(right, left));
    }
    vec2 q = shape == 6 ? vec2(p.x + p.y, p.y - p.x) * 0.70710678 : p;
    float t = r / 3.0;
    return min(box(q, vec2(r, t)), box(q, vec2(t, r)));
}

void main() {
    float d = distanceTo(local);
    float fill = clamp(0.5 - d, 0.0, 1.0);
    float stroke = halfStroke > 0.0 ? clamp(0.5 - (abs(d) - halfStroke), 0.0, 1.0) : 0.0;
    vec4 f = fillColour * fill;
    vec4 s = strokeColour * stroke;
    vec4 c = s + f * (1.0 - s.a);
    if (c.a <= 0.0) discard;
    frag = c;
}
"""

    let minimum = defaultArg minimumBatch 2000

    /// The context, the program and its locations, made on the first batch; `None` for good once
    /// WebGL2 has been asked for and refused.
    let mutable state: (WebGl2 * obj * (string -> obj)) option option = None

    let build () : (WebGl2 * obj * (string -> obj)) option =
        let gl = WebGlSurface.create 1 1

        if isNull gl then
            None
        else
            let compile (kind: int) (source: string) =
                let s = gl.createShader kind
                gl.shaderSource (s, source)
                gl.compileShader s

                if not (unbox<bool> (gl.getShaderParameter (s, 0x8B81))) then
                    invalidOp ("Marey: the WebGL2 marker shader did not compile: " + gl.getShaderInfoLog s)

                s

            let vs = compile 0x8B31 vertexSource
            let fs = compile 0x8B30 fragmentSource
            let program = gl.createProgram ()
            gl.attachShader (program, vs)
            gl.attachShader (program, fs)
            gl.linkProgram program

            if not (unbox<bool> (gl.getProgramParameter (program, 0x8B82))) then
                invalidOp ("Marey: the WebGL2 marker shader did not link: " + gl.getProgramInfoLog program)

            gl.deleteShader vs
            gl.deleteShader fs

            let vao = gl.createVertexArray ()
            gl.bindVertexArray vao
            let quad = gl.createBuffer ()
            gl.bindBuffer (0x8892, quad)
            gl.bufferData (0x8892, [| -1.0f; -1.0f; 1.0f; -1.0f; -1.0f; 1.0f; 1.0f; 1.0f |], 0x88E4)
            gl.enableVertexAttribArray 0
            gl.vertexAttribPointer (0, 2, 0x1406, false, 8, 0)
            let instances = gl.createBuffer ()
            gl.bindBuffer (0x8892, instances)
            gl.enableVertexAttribArray 1
            gl.vertexAttribPointer (1, 2, 0x1406, false, 8, 0)
            gl.vertexAttribDivisor (1, 1)

            let locations = Collections.Generic.Dictionary<string, obj>()

            let location (name: string) =
                match locations.TryGetValue name with
                | true, l -> l
                | _ ->
                    let l = gl.getUniformLocation (program, name)
                    locations.[name] <- l
                    l

            Some(gl, program, location)

    let context () =
        match state with
        | Some s -> s
        | None ->
            let s =
                try
                    build ()
                with _ ->
                    None

            state <- Some s
            s

    /// A colour as the premultiplied RGBA the blend expects, each channel a byte first, as
    /// `CssColor` writes it for the canvas.
    static let premultiplied (c: Color) =
        let ch (v: float) = Num.roundEven (max 0.0 (min 1.0 v) * 255.0) / 255.0
        let a = ch c.A
        ch c.R * a, ch c.G * a, ch c.B * a, a

    /// The batches drawn here rather than by the canvas: as large as `MinimumBatch`, solid paints,
    /// no dash, and an outline whose corners a miter of at least 2 draws, as the desktop drawer
    /// claims them.
    member _.Claims(b: MarkerBatch) : bool =
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

        b.Points.Length >= minimum
        && (b.Fill.IsSome || b.Stroke.IsSome)
        && (match b.Fill with
            | Some p -> solid p
            | None -> true)
        && strokeOk

    /// The smallest batch worth a GPU round trip.
    member _.MinimumBatch: int = minimum

    /// The drawer every `CanvasBackend` shares unless told otherwise: one WebGL2 context for the
    /// page, since a browser allows a page only a handful.
    static member val Shared: WebGlMarkers = WebGlMarkers()

    /// Draw a batch on the WebGL2 surface and composite it into `ctx` at the current point of
    /// the traversal. `scale` is the device pixels per px the context's transform is set to.
    /// False, having drawn nothing, where there is no WebGL2.
    member _.Draw(ctx: CanvasContext, ctm: Affine<nd, px>, b: MarkerBatch, scale: float) : bool =
        match context () with
        | None -> false
        | Some(gl, program, location) ->
            let target = ctx.canvas
            let w, h = int target.width, int target.height
            let surface = gl.canvas

            if int surface.width <> w then surface.width <- float w
            if int surface.height <> h then surface.height <- float h

            // Device pixels, in `float` until here.
            let centres = Array.zeroCreate<float32> (b.Points.Length * 2)

            for i in 0 .. b.Points.Length - 1 do
                let q = Affine.apply ctm b.Points.[i]
                centres.[2 * i] <- float32 (float q.X * scale)
                centres.[2 * i + 1] <- float32 (float q.Y * scale)

            let r = float b.Size / 2.0 * scale

            let halfStroke =
                match b.Stroke with
                | Some s -> float s.Width / 2.0 * scale
                | None -> 0.0

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
                | _ -> 0.0, 0.0, 0.0, 0.0

            let fr, fg, fb, fa = colourOf b.Fill
            let sr, sg, sb, sa = colourOf (b.Stroke |> Option.map (fun s -> s.Paint))

            gl.viewport (0, 0, w, h)
            gl.clearColor (0.0, 0.0, 0.0, 0.0)
            gl.clear 0x4000
            gl.enable 0x0BE2
            gl.blendFunc (1, 0x0303)
            gl.useProgram program
            gl.uniform2f (location "viewport", float w, float h)
            gl.uniform1f (location "reach", r + halfStroke + 1.5)
            gl.uniform1i (location "shape", shape)
            gl.uniform1f (location "radius", r)
            gl.uniform1f (location "halfStroke", halfStroke)
            gl.uniform4f (location "fillColour", fr, fg, fb, fa)
            gl.uniform4f (location "strokeColour", sr, sg, sb, sa)
            gl.bufferData (0x8892, centres, 0x88E0)
            gl.drawArraysInstanced (5, 0, 4, b.Points.Length)

            // Under the context's base transform, a device pixel to a device pixel; under its
            // clip and into whatever layer it is drawing.
            ctx.drawImage (surface, 0.0, 0.0, float w / scale, float h / scale)
            true
