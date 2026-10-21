// FS0009: a texture allocated without data takes a null pointer.
#nowarn "9"

namespace Marey

open System
open System.Collections.Generic
open Microsoft.FSharp.NativeInterop
open Silk.NET.OpenGL

/// The compute seam: fragment shaders writing float textures, in OpenGL 3.3 core.
///
/// **One pass is one full-screen triangle** into a framebuffer over one or more `R32F` or
/// `RGBA32F` textures; a shader reads its inputs with `texelFetch` and writes one value per texel.
/// Nothing scatters, so no compute shader is needed and OpenGL 3.3 and CI's llvmpipe suffice.
/// Measured: a 256² Stam step in 43 passes took 0.084 ms on radeonsi and
/// 3.8 ms on llvmpipe, and a rerun was bit-identical on each.
///
/// **Values cross as `float32`**: uploaded from `float[]`, read back into `float[]`. A GPU
/// result is held to its CPU twin by a stated bound, never by bytes.
///
/// **Owned by a `GpuContext`** and used on its render thread only, between Skia's drawing. Every
/// call leaves the GL state Skia expects to have to reset, and the caller resets Skia.
[<Sealed>]
type internal GpuCompute(gl: GL) =

    static let vertexSource =
        """#version 330 core
void main() {
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}
"""

    let vao = gl.GenVertexArray()
    let programs = Dictionary<string, uint32>()
    let locations = Dictionary<uint32, Dictionary<string, int>>()
    let textures = Dictionary<string, uint32 * uint32 * int * int * bool>()
    let groups = Dictionary<string, uint32 * string list * int * int>()

    let compileShader (kind: ShaderType) (name: string) (source: string) =
        let shader = gl.CreateShader kind
        gl.ShaderSource(shader, source)
        gl.CompileShader shader
        let mutable ok = 0
        gl.GetShader(shader, ShaderParameterName.CompileStatus, &ok)

        if ok = 0 then
            invalidOp (sprintf "Marey.GpuCompute: the shader '%s' did not compile: %s" name (gl.GetShaderInfoLog shader))

        shader

    /// A program by name, compiled from its fragment source the first time it is asked for.
    member _.Program(name: string, fragment: string) : uint32 =
        match programs.TryGetValue name with
        | true, p -> p
        | _ ->
            let vs = compileShader ShaderType.VertexShader "vertex" vertexSource
            let fs = compileShader ShaderType.FragmentShader name fragment
            let p = gl.CreateProgram()
            gl.AttachShader(p, vs)
            gl.AttachShader(p, fs)
            gl.LinkProgram p
            let mutable ok = 0
            gl.GetProgram(p, ProgramPropertyARB.LinkStatus, &ok)

            if ok = 0 then
                invalidOp (sprintf "Marey.GpuCompute: the program '%s' did not link: %s" name (gl.GetProgramInfoLog p))

            gl.DeleteShader vs
            gl.DeleteShader fs
            programs.[name] <- p
            locations.[p] <- Dictionary()
            p

    /// A uniform's location in a program, looked up once.
    member _.Uniform(program: uint32, name: string) : int =
        let cache = locations.[program]

        match cache.TryGetValue name with
        | true, l -> l
        | _ ->
            let l = gl.GetUniformLocation(program, name)
            cache.[name] <- l
            l

    /// A texture and the framebuffer over it, by name, of a size and a channel count (1 or 4);
    /// reused while the size stays, rebuilt when it changes.
    member _.Surface(name: string, width: int, height: int, rgba: bool) : uint32 =
        match textures.TryGetValue name with
        | true, (t, _, w, h, c) when w = width && h = height && c = rgba -> t
        | found, old ->
            if found then
                let t, f, _, _, _ = old
                gl.DeleteFramebuffer f
                gl.DeleteTexture t

                // A group over the old texture would keep writing it: GL may give the new texture
                // the old one's name, so a group is dropped with any surface under it, not
                // checked by name.
                for g in [ for KeyValue(g, (_, under, _, _)) in groups do if List.contains name under then yield g ] do
                    let gf, _, _, _ = groups.[g]
                    gl.DeleteFramebuffer gf
                    groups.Remove g |> ignore

            let t = gl.GenTexture()
            gl.BindTexture(TextureTarget.Texture2D, t)
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, int GLEnum.Nearest)
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, int GLEnum.Nearest)
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, int GLEnum.ClampToEdge)
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, int GLEnum.ClampToEdge)

            let internalFormat, format = if rgba then InternalFormat.Rgba32f, PixelFormat.Rgba else InternalFormat.R32f, PixelFormat.Red

            gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                internalFormat,
                uint32 width,
                uint32 height,
                0,
                format,
                PixelType.Float,
                NativePtr.toVoidPtr NativePtr.nullPtr<byte>
            )

            let f = gl.GenFramebuffer()
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, f)
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, t, 0)
            let status = gl.CheckFramebufferStatus FramebufferTarget.Framebuffer

            if status <> GLEnum.FramebufferComplete then
                invalidOp (sprintf "Marey.GpuCompute: a %d × %d float target is incomplete here (%A)." width height status)

            textures.[name] <- (t, f, width, height, rgba)
            t

    member private _.Entry(name: string) =
        match textures.TryGetValue name with
        | true, e -> e
        | _ -> invalidOp (sprintf "Marey.GpuCompute: no surface named '%s'." name)

    /// Values into a surface, as `float32`: one per texel, or four for an RGBA surface.
    member this.Upload(name: string, values: float[]) =
        let t, _, w, h, rgba = this.Entry name
        let data = Array.map float32 values
        gl.BindTexture(TextureTarget.Texture2D, t)
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1)
        gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, uint32 w, uint32 h, (if rgba then PixelFormat.Rgba else PixelFormat.Red), PixelType.Float, ReadOnlySpan<float32>(data))

    /// A surface's values, as `float`.
    member this.Read(name: string) : float[] =
        let _, f, w, h, rgba = this.Entry name
        let data = Array.zeroCreate<float32> (w * h * (if rgba then 4 else 1))
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, f)
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1)
        gl.ReadPixels(0, 0, uint32 w, uint32 h, (if rgba then PixelFormat.Rgba else PixelFormat.Red), PixelType.Float, Span<float32>(data))
        Array.map float data

    /// A surface set to zero.
    member this.Clear(name: string) =
        let _, f, w, h, _ = this.Entry name
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, f)
        gl.Viewport(0, 0, uint32 w, uint32 h)
        gl.ClearColor(0.0f, 0.0f, 0.0f, 0.0f)
        gl.Clear ClearBufferMask.ColorBufferBit

    /// A framebuffer over several surfaces of one size, by name, for a pass with as many outputs:
    /// `layout(location = k)` writes the k-th. Dropped when a surface under it is rebuilt.
    member this.Group(name: string, surfaces: string list) =
        let entries = surfaces |> List.map this.Entry
        let ids = entries |> List.map (fun (t, _, _, _, _) -> t)
        let _, _, w, h, _ = List.head entries

        match groups.TryGetValue name with
        | true, (_, under, _, _) when under = surfaces -> ()
        | found, old ->
            if found then
                let f, _, _, _ = old
                gl.DeleteFramebuffer f

            let f = gl.GenFramebuffer()
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, f)

            ids
            |> List.iteri (fun k t ->
                gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, enum<FramebufferAttachment> (int FramebufferAttachment.ColorAttachment0 + k), TextureTarget.Texture2D, t, 0))

            let buffers = Array.init ids.Length (fun k -> enum<GLEnum> (int GLEnum.ColorAttachment0 + k))
            gl.DrawBuffers(uint32 buffers.Length, ReadOnlySpan<GLEnum>(buffers))
            let status = gl.CheckFramebufferStatus FramebufferTarget.Framebuffer

            if status <> GLEnum.FramebufferComplete then
                invalidOp (sprintf "Marey.GpuCompute: a group of %d float targets is incomplete here (%A)." ids.Length status)

            groups.[name] <- (f, surfaces, w, h)

    /// One pass: `program` drawn over all of `target`, a surface or a group, with `inputs` bound as
    /// samplers by name and `set` given the chance to set the other uniforms.
    member this.Pass(program: uint32, target: string, inputs: (string * string) list, set: uint32 -> unit) =
        let f, w, h =
            match groups.TryGetValue target with
            | true, (f, _, w, h) -> f, w, h
            | _ ->
                let _, f, w, h, _ = this.Entry target
                f, w, h

        gl.UseProgram program

        inputs
        |> List.iteri (fun unit (sampler, surface) ->
            let t, _, _, _, _ = this.Entry surface
            gl.ActiveTexture(enum<TextureUnit> (int TextureUnit.Texture0 + unit))
            gl.BindTexture(TextureTarget.Texture2D, t)
            gl.Uniform1(this.Uniform(program, sampler), unit))

        set program
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, f)
        gl.Viewport(0, 0, uint32 w, uint32 h)
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3u)

    /// The GL state a pass assumes, set before a batch of them: no blending, depth, stencil,
    /// scissor or culling, every channel written, and the empty vertex array.
    member _.Begin() =
        gl.Disable EnableCap.Blend
        gl.Disable EnableCap.DepthTest
        gl.Disable EnableCap.StencilTest
        gl.Disable EnableCap.ScissorTest
        gl.Disable EnableCap.CullFace
        gl.ColorMask(true, true, true, true)
        gl.BindVertexArray vao

    /// Bindings back to none after a batch; the caller resets Skia.
    member _.End() =
        gl.BindVertexArray 0u
        gl.UseProgram 0u
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0u)
        gl.ActiveTexture TextureUnit.Texture0
        gl.BindTexture(TextureTarget.Texture2D, 0u)

    member _.GL = gl

    interface IDisposable with
        member _.Dispose() =
            for f, _, _, _ in groups.Values do
                gl.DeleteFramebuffer f

            for t, f, _, _, _ in textures.Values do
                gl.DeleteFramebuffer f
                gl.DeleteTexture t

            for p in programs.Values do
                gl.DeleteProgram p

            gl.DeleteVertexArray vao
