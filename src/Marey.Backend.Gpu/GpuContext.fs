// FS0009: reading GLFW's error string is a native pointer, once, on a path that is already failing.
#nowarn "9"

namespace Marey

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Silk.NET.Core.Contexts
open Silk.NET.Maths
open Silk.NET.OpenGL
open Silk.NET.Windowing
open SkiaSharp

/// D117 — the thread a GL context belongs to.
///
/// A GL context is current on one thread, and making it current elsewhere is the expensive and
/// driver-dependent thing a GPU renderer exists to avoid. So the context, Skia's `GRContext` on
/// it, and every call that touches either live on this one thread, and a caller on any other
/// thread queues its work and waits. A call made *from* the thread runs inline, so a job may
/// call back into the renderer without deadlocking.
[<Sealed>]
type internal RenderThread(name: string) =

    let queue = new BlockingCollection<unit -> unit>()

    let thread =
        Thread((fun () ->
                   for job in queue.GetConsumingEnumerable() do
                       job ()),
               IsBackground = true,
               Name = name)

    do thread.Start()

    member _.Invoke(f: unit -> 'a) : 'a =
        if Thread.CurrentThread.ManagedThreadId = thread.ManagedThreadId then
            f ()
        else
            let result = TaskCompletionSource<'a>(TaskCreationOptions.RunContinuationsAsynchronously)

            queue.Add(fun () ->
                try
                    result.SetResult(f ())
                with e ->
                    result.SetException e)

            // `GetResult` rather than `.Result`, so an exception thrown on the render thread
            // arrives as itself and not wrapped in an `AggregateException`.
            result.Task.GetAwaiter().GetResult()

    interface IDisposable with
        member _.Dispose() =
            queue.CompleteAdding()

            if Thread.CurrentThread.ManagedThreadId <> thread.ManagedThreadId then
                thread.Join()

            queue.Dispose()

/// An offscreen colour and stencil target of one size, and the Skia surface over it.
[<Sealed>]
type internal GpuTarget(gl: GL, gr: GRContext, width: int, height: int) =

    let fbo = gl.GenFramebuffer()
    let colour = gl.GenRenderbuffer()
    let depthStencil = gl.GenRenderbuffer()

    do
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo)
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, colour)
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, uint32 width, uint32 height)
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, colour)
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthStencil)
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, uint32 width, uint32 height)
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, depthStencil)

        let status = gl.CheckFramebufferStatus FramebufferTarget.Framebuffer

        if status <> GLEnum.FramebufferComplete then
            invalidOp (sprintf "Marey.GpuRenderer: a %dx%d framebuffer is incomplete (%A)." width height status)

        // Skia has seen us change the framebuffer and renderbuffer bindings.
        gr.ResetContext()

    // 0x8058 is GL_RGBA8: the format the colour renderbuffer was allocated with, which Skia
    // needs to be told because it cannot ask a renderbuffer.
    let renderTarget = new GRBackendRenderTarget(width, height, 0, 8, GRGlFramebufferInfo(fbo, 0x8058u))

    let surface =
        SKSurface.Create(gr, renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)

    do
        if isNull surface then
            invalidOp (sprintf "Marey.GpuRenderer: Skia could not make a %dx%d surface on the framebuffer." width height)

    member _.Width = width
    member _.Height = height
    member _.Framebuffer = fbo
    member _.Surface = surface

    /// The target's pixels, RGBA8 premultiplied, top row first. GL reads from the bottom.
    member _.Read() : byte[] =
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo)
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1)
        let stride = width * 4
        let raw = Array.zeroCreate<byte> (stride * height)

        gl.ReadPixels(0, 0, uint32 width, uint32 height, PixelFormat.Rgba, PixelType.UnsignedByte, Span<byte>(raw))

        gr.ResetContext()
        let flipped = Array.zeroCreate<byte> raw.Length

        for y in 0 .. height - 1 do
            Array.blit raw ((height - 1 - y) * stride) flipped (y * stride) stride

        flipped

    /// The target's pixels as an `Image`: RGBA8, not premultiplied, top row first, as a `Raster`
    /// node carries one.
    member this.ReadImage() : Image =
        let pixels = this.Read()

        for i in 0 .. 4 .. pixels.Length - 4 do
            let a = int pixels.[i + 3]

            if a > 0 && a < 255 then
                for c in 0 .. 2 do
                    pixels.[i + c] <- byte (min 255 ((int pixels.[i + c] * 255 + a / 2) / a))

        { Width = width; Height = height; Rgba = pixels }

    interface IDisposable with
        member _.Dispose() =
            surface.Dispose()
            renderTarget.Dispose()
            gl.DeleteFramebuffer fbo
            gl.DeleteRenderbuffer colour
            gl.DeleteRenderbuffer depthStencil
            gr.ResetContext()

/// The GLFW this package ships, loaded before Silk.NET asks for one.
///
/// Silk.NET finds GLFW by name on the system's library path and not among a package's runtime
/// assets, so on a machine with no GLFW installed it finds none — a hosted CI runner, measured —
/// and on a machine with one it loads that one instead of the version this package was built
/// against. .NET's native-asset resolution knows where the package's copy is. Once it is in the
/// process, the loader answers Silk.NET's lookup by name with it, so every machine runs the same
/// GLFW.
module internal GlfwLibrary =

    let private names =
        if OperatingSystem.IsWindows() then [ "glfw3.dll"; "glfw3" ]
        elif OperatingSystem.IsMacOS() then [ "libglfw.3.dylib"; "libglfw.3" ]
        else [ "libglfw.so.3"; "glfw" ]

    let private loaded =
        lazy
            (let assembly = typeof<Silk.NET.GLFW.Glfw>.Assembly

             names
             |> List.exists (fun name ->
                 let mutable handle = 0n
                 Runtime.InteropServices.NativeLibrary.TryLoad(name, assembly, Nullable(), &handle)))

    /// Load it, once. `false` when .NET cannot find it either, and Silk.NET's own search is left
    /// to try.
    let ensure () = loaded.Force()

    /// GLFW keeps global state and is not thread-safe, and each renderer makes and destroys its
    /// window on its own render thread. Every GLFW call goes through this one lock, so two
    /// renderers starting at once take turns.
    let gate = obj ()

/// GLFW's own account of why it would not start, which Silk.NET's "couldn't find a suitable window
/// platform" does not carry: the library did not load, or `glfwInit` failed and said why (no
/// display, an X11 client library it loads at run time missing).
module internal GlfwDiagnosis =

    let why () : string =
        try
            GlfwLibrary.ensure () |> ignore
            let glfw = Silk.NET.GLFW.Glfw.GetApi()

            if lock GlfwLibrary.gate glfw.Init then
                glfw.Terminate()
                "GLFW itself initialises here"
            else
                let mutable description = NativeInterop.NativePtr.nullPtr<byte>
                let code = glfw.GetError(&description)

                let text =
                    Runtime.InteropServices.Marshal.PtrToStringAnsi(NativeInterop.NativePtr.toNativeInt description)

                sprintf "glfwInit failed (%A): %s" code text
        with e ->
            "the GLFW library did not load: " + e.Message

/// The GL side of a renderer: a window that exists for its context, the context, and Skia's
/// `GRContext` on it. Built and used on the render thread only.
///
/// The window is hidden and 16 × 16 for an offscreen renderer. `shown` gives it a title, a size
/// and a border, and puts it on the screen unless told not to, for `GpuWindow`, which draws into the same kind of
/// offscreen target and copies it to the window's own framebuffer.
[<Sealed>]
type internal GpuContext(?shown: string * int * int * bool) =

    let window =
        lock GlfwLibrary.gate (fun () ->
            GlfwLibrary.ensure () |> ignore
            Silk.NET.Windowing.Glfw.GlfwWindowing.Use()

            let mutable o = WindowOptions.Default

            match shown with
            | Some(title, width, height, visible) ->
                o.Size <- Vector2D<int>(width, height)
                o.Title <- title
                o.IsVisible <- visible
                o.WindowBorder <- WindowBorder.Resizable
                o.VSync <- true
            | None ->
                o.Size <- Vector2D<int>(16, 16)
                o.IsVisible <- false

            o.ShouldSwapAutomatically <- false
            o.API <- GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, APIVersion(3, 3))
            let w = Window.Create o
            w.Initialize()
            w)

    let gl = GL.GetApi(window :> IGLContextSource)

    let gr =
        // Skia's GL function table, built two ways because each fails on the other platform,
        // measured: under GLX (X11, Xvfb) handing Skia a lookup callback segfaults inside
        // `GRGlInterface.Create`, and under EGL (Wayland) Skia's own lookup aborts. The callback
        // uses `TryGetProcAddress`, because Silk.NET's `GetProcAddress` throws on a name the
        // driver lacks, and Skia asks for optional ones.
        let glContext = window.GLContext

        let iface =
            if window.Native.Kind.HasFlag NativeWindowFlags.X11 then
                GRGlInterface.Create()
            else
                GRGlInterface.Create(
                    GRGlGetProcedureAddressDelegate(fun name ->
                        let mutable address = 0n
                        if glContext.TryGetProcAddress(name, &address) then address else 0n)
                )

        if isNull iface || not (iface.Validate()) then
            invalidOp "Marey.GpuRenderer: Skia could not load the OpenGL functions it needs from this context."

        let c = GRContext.CreateGl iface

        if isNull c then
            invalidOp "Marey.GpuRenderer: Skia could not create a GPU context on OpenGL."

        c

    let mutable target: GpuTarget option = None

    let markers = new MarkerDrawer(gl)

    // D139: made the first time a solver asks, so a renderer that only draws compiles no shader of it.
    let mutable compute: GpuCompute option = None

    do gr.ResetContext()

    member _.GL = gl
    member _.Skia = gr
    member _.Markers = markers

    /// Run compute passes, with the GL state they assume set first and Skia told afterwards that
    /// its state was touched.
    member _.Compute(f: GpuCompute -> 'a) : 'a =
        let c =
            match compute with
            | Some c -> c
            | None ->
                let c = new GpuCompute(gl)
                compute <- Some c
                c

        c.Begin()

        try
            f c
        finally
            c.End()
            gr.ResetContext()
    member _.Window: IWindow = window

    /// What the driver says it is, for a report: "AMD Radeon RX 7900 XT (radeonsi, …)" on a
    /// desktop, "llvmpipe (LLVM …)" under Mesa's software driver.
    member val Description =
        sprintf "%s, %s" (gl.GetStringS StringName.Renderer) (gl.GetStringS StringName.Version)

    /// The target for a size, reusing the last one when the size has not changed. A film's
    /// frames are one size, so a film allocates one target.
    member _.Target(width: int, height: int) : GpuTarget =
        match target with
        | Some t when t.Width = width && t.Height = height -> t
        | previous ->
            previous |> Option.iter (fun t -> (t :> IDisposable).Dispose())
            let t = new GpuTarget(gl, gr, width, height)
            target <- Some t
            t

    /// **Draw layers on the target for a size**, each a scene under the transform that places
    /// it, in order, and flush. Every node is Skia's but a marker batch the instanced drawer can
    /// draw exactly as Skia would (D119), which is claimed and drawn between Skia's flushes, so it
    /// lands over what came before it and under what comes after. The scenes are degraded by the
    /// caller.
    member this.Paint(opts: SkiaOptions, shaping: SkiaTextMeasurer, instanced: bool, width: int, height: int, layers: (Affine<nd, px> * Node) seq) : GpuTarget =
        let target = this.Target(width, height)
        let canvas = target.Surface.Canvas
        canvas.Clear(SkiaColor.ofColor opts.Background)

        // The claim needs the painter's clip and layer depth, so it reads the painter it is
        // handed to.
        let mutable self: SkiaPainter option = None

        let claim (ctm: Affine<nd, px>) (node: Node) =
            match node, self with
            | Markers b, Some painter when MarkerDrawer.Claims(b, painter.Clip, painter.LayerDepth, opts.Antialias) ->
                // Skia's queued drawing first, then Skia is told its GL state was touched.
                target.Surface.Flush()
                gr.Flush()
                markers.Draw(target.Framebuffer, width, height, ctm, b, painter.Clip)
                gr.ResetContext()
                true
            | _ -> false

        let painter = SkiaPainter(canvas, shaping, opts.Antialias, (if instanced then Some claim else None))
        self <- Some painter

        for ctm, node in layers do
            painter.Draw(ctm, node)

        target.Surface.Flush()
        gr.Flush()
        target

    /// What a GPU surface draws: everything Skia does, as for a PNG, and nowhere to put a label.
    static member Capabilities: Capabilities =
        { Gradients = true
          RadialGradients = true
          Clipping = true
          Dashes = true
          Rasters = true
          TextShaping = true
          RotatedText = true
          Semantics = false
          GroupOpacity = true
          MaxBatch = None }

    interface IDisposable with
        member _.Dispose() =
            target |> Option.iter (fun t -> (t :> IDisposable).Dispose())
            compute |> Option.iter (fun c -> (c :> IDisposable).Dispose())
            (markers :> IDisposable).Dispose()
            gr.Dispose()
            lock GlfwLibrary.gate (fun () -> window.Dispose())
