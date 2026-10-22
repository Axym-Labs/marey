// Compare two directories of PNGs pixel for pixel, the ones tools/png renders: `dotnet fsi tools/png/compare.fsx A B`.
// Exits 1 when a file is missing or any pixel differs, naming how many and by how much.

#r "nuget: SkiaSharp, 4.151.1"
#r "nuget: SkiaSharp.NativeAssets.Linux, 4.151.1"

open System.IO
open SkiaSharp

let a, b = fsi.CommandLineArgs.[1], fsi.CommandLineArgs.[2]
let mutable failed = false

for path in Directory.GetFiles(a, "*.png") |> Array.sort do
    let name = Path.GetFileName path
    let other = Path.Combine(b, name)

    if not (File.Exists other) then
        printfn "%s: missing from %s" name b
        failed <- true
    else
        use x = SKBitmap.Decode path
        use y = SKBitmap.Decode other

        if x.Width <> y.Width || x.Height <> y.Height then
            printfn "%s: %dx%d against %dx%d" name x.Width x.Height y.Width y.Height
            failed <- true
        else
            let mutable differing = 0
            let mutable largest = 0
            let mutable box = (System.Int32.MaxValue, System.Int32.MaxValue, -1, -1)

            for row in 0 .. x.Height - 1 do
                for col in 0 .. x.Width - 1 do
                    let p, q = x.GetPixel(col, row), y.GetPixel(col, row)
                    let d = List.max [ abs (int p.Red - int q.Red); abs (int p.Green - int q.Green); abs (int p.Blue - int q.Blue); abs (int p.Alpha - int q.Alpha) ]

                    if d > 0 then
                        differing <- differing + 1
                        largest <- max largest d
                        let x0, y0, x1, y1 = box
                        box <- (min x0 col, min y0 row, max x1 col, max y1 row)

            printfn "%s: %d of %d pixels differ, by at most %d/255" name differing (x.Width * x.Height) largest

            if differing > 0 then
                let x0, y0, x1, y1 = box
                printfn "  within x %d..%d, y %d..%d" x0 x1 y0 y1
            if differing > 0 then failed <- true

exit (if failed then 1 else 0)
