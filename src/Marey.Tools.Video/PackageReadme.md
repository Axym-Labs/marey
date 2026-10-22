# Marey.Tools.Video

Video output for [Marey](https://axym-labs.github.io/marey/). **The frame sequence is the product;
the mux is a convenience over it.**

```fsharp
#r "nuget: Marey"
#r "nuget: Marey.Backend.Skia"
#r "nuget: Marey.Tools.Video"

open Marey
open Marey.UnitsOfMeasure

let film =
    Anim.ofSeconds 1.0<s> (fun t -> Chart.lineIn U.second U.metre [| 0.0<s>; 1.0<s> |] [| 0.0<m>; t * 1.0<m/s> + 0.1<m> |])
    |> Film.ofFigures 24 (Chart.size 320.0 200.0)

let sequence =
    film |> Frames.write FramePass.Concurrent SkiaBackend.renderPng (FrameTarget.into "frames" ".png") 25.0

match Video.mux MuxOptions.Default sequence "shot.mp4" with
| MuxRan run when run.Succeeded -> printfn "encoded %d frames" run.FrameCount
| MuxUnavailable why -> printfn "%s" why      // the frames are still there, and this says so
| other -> printfn "%A" other
```

`Marey.Plot` renders an animation to a numbered directory on every core there is. This encodes
that directory by running `ffmpeg`, which it looks for on `PATH` and *reports* — found, with its
version, or absent with the sentence naming what to install.

## Why a subprocess and not a binding

`FFmpeg.AutoGen` links `libav*` into your process: LGPL at best, GPL the moment the build carries
x264, which puts a licence audit on everyone who references your project. A subprocess boundary
keeps ffmpeg's licence ffmpeg's problem. The managed wrappers shell out anyway, so what they cost
is a dependency and its opinions about temporary files, and what they buy is not writing one
`ProcessStartInfo`.

**This package therefore has no third-party dependency of any kind.**

## What is in it

- **`Video.mux`**, and `Video.muxWith` for a stated ffmpeg rather than the one on `PATH`.
  A missing encoder is `MuxUnavailable` — an outcome carrying what to install and the fact that
  the frames are still on disk — not an exception. A numbered directory is something every
  editor imports.
- **`Video.args`, a pure function**, so the exact command is a value you can print, diff or paste
  into a shell. Two positions in it are load-bearing and silent when wrong: `-framerate` before
  `-i` keeps every rendered frame, and the same number after `-i` resamples — 24 frames at 12 fps
  is 24 frames written correctly and **13** with the flag moved.
- **`Video.inspect`**, which decodes the result with `ffprobe` and says what is actually in it:
  codec, dimensions, frames counted rather than claimed, and the rate the container declares.
  Exit status is a weak gate — an encoder that wrote 13 of 24 frames exits zero.
- **Reproducible containers.** `-fflags +bitexact` and its companions strip the `encoder=Lavf…`
  and `encoder=Lavc…` tags that would otherwise name the ffmpeg build that wrote the file.
  `VideoInfo.BuildTags` is how you check, and it is empty.
- **`Video.check`**, which starts no process: leftover frames from a longer render
  (which `ffmpeg -i frame-%05d.png` would silently append as a tail), and odd pixel dimensions
  under a chroma-subsampled pixel format — read out of the first frame's own header, because a
  raster backend rounds an extent up.

Depends on [`Marey.Plot`](https://www.nuget.org/packages/Marey.Plot) and
[`Marey.Toolchain`](https://www.nuget.org/packages/Marey.Toolchain), and on nothing outside this
repository.

- [The guide on frames and video](https://axym-labs.github.io/marey/guide/10-storyboard.html)
- [examples/video.fsx](https://github.com/Axym-Labs/marey/blob/main/examples/video.fsx)
