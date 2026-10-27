(*  The public API listing: one file per package under `api/`, the compiler's own
    signature of the package with everything a user cannot reach taken out.

        dotnet fsi tools/api/listing.fsx           rewrites api/*.fsi
        dotnet fsi tools/api/listing.fsx --check   fails, with the diff, when they differ

    Either builds the solution to have the compiler write the signatures. `--signatures <dir>`
    reads them from a build that already wrote them there instead, as CI's build does. That build
    has to compile every package, so a clean one: a project that is up to date writes nothing,
    and its signature in `<dir>` would be whatever an earlier build left there.

        dotnet build Marey.slnx -c Release -p:MareyApiSig=<dir>
        dotnet fsi tools/api/listing.fsx --check --signatures <dir>

    The signature comes from `fsc --sig` (hooked in Directory.Build.targets as `MareyApiSig`), not
    from reflection, because reflection erases units of measure: `float<m>` and `float<s>` are
    both `Double` to it, and a change between them is exactly the break this listing is for. The
    compiler prints in source order, so the listing is stable and reads like the code.

    Taken out: doc comments (a reworded comment is not an API change), and every declaration
    marked `private` or `internal`, with its body and its attributes, and the fields of a record
    or union whose representation is hidden. Kept: attributes on what stays, `[<Obsolete>]`
    among them, and the bare `internal` or `private` that says the representation is hidden.

    The listing is the compiler's spelling, so another compiler may spell the same API another
    way. CI checks it on the SDK band the repository is built and released with (`global.json`),
    and the listing is regenerated when that band moves. *)

open System
open System.IO
open System.Diagnostics
open System.Text.RegularExpressions

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))
let check = fsi.CommandLineArgs |> Array.contains "--check"
let listingDir = Path.Combine(root, "api")

let run (file: string) (args: string list) =
    let psi = ProcessStartInfo(file, WorkingDirectory = root, UseShellExecute = false)
    args |> List.iter psi.ArgumentList.Add
    use p = Process.Start psi
    p.WaitForExit()
    p.ExitCode

/// The packages: every project under `src/` that does not say `IsPackable=false`, less the one
/// that packs no assembly (`Marey`, which only gathers the others) and so has no API.
let packages =
    Directory.GetDirectories(Path.Combine(root, "src"))
    |> Array.choose (fun dir ->
        let name = Path.GetFileName dir
        let proj = Path.Combine(dir, name + ".fsproj")

        let text = if File.Exists proj then File.ReadAllText proj else ""

        if File.Exists proj
           && not (text.Contains "<IsPackable>false</IsPackable>")
           && not (text.Contains "<IncludeBuildOutput>false</IncludeBuildOutput>") then
            Some name
        else
            None)
    |> Array.sort

let given =
    fsi.CommandLineArgs
    |> Array.tryFindIndex ((=) "--signatures")
    |> Option.map (fun i ->
        if i + 1 >= fsi.CommandLineArgs.Length then
            eprintfn "--signatures needs the directory the build wrote them to"
            exit 2

        Path.GetFullPath fsi.CommandLineArgs.[i + 1])

let sigDir =
    match given with
    | Some dir ->
        if not (Directory.Exists dir) then
            eprintfn "no signatures at %s: build with -p:MareyApiSig=%s first" dir dir
            exit 2

        dir
    | None ->
        let dir = Path.Combine(Path.GetTempPath(), sprintf "marey-api-%d" (Process.GetCurrentProcess().Id))
        Directory.CreateDirectory dir |> ignore
        AppDomain.CurrentDomain.ProcessExit.Add(fun _ -> try Directory.Delete(dir, true) with _ -> ())

        // `--no-incremental`, because a project that is up to date is not compiled and writes no
        // signature.
        let built =
            run "dotnet" [ "build"; "Marey.slnx"; "-c"; "Release"; "--no-incremental"; "-nologo"; "-v:q"; sprintf "-p:MareyApiSig=%s" dir ]

        if built <> 0 then
            eprintfn "the build failed (exit %d)" built
            exit built

        dir

/// A declaration a user cannot reach: `val private`, `member internal`, `type private`,
/// `module internal`, … where the accessibility follows the keyword and any modifiers, or
/// `private new`, where it comes first. A member too long for one line is printed as a bare
/// `member` with the accessibility starting the next, which `clean` joins.
let hidden =
    Regex(@"^\s*(?:(?:static\s+)?(?:val|member|type|module|new|abstract|override|default|and|exception)\s+(?:(?:inline|rec|mutable)\s+)*(?:private|internal)\b|(?:private|internal)\s)")

let bareMember = Regex(@"^\s*(?:static\s+|abstract\s+|override\s+|default\s+)?member\s*$")

let indentOf (line: string) = line.Length - line.TrimStart().Length

let clean (text: string) =
    let out = Collections.Generic.List<string>()
    let attrs = Collections.Generic.List<string>()
    let mutable skipping: int option = None
    let mutable header: string option = None
    let mutable skippedBlank = false
    let mutable inAttribute = false

    for raw in text.Replace("\r\n", "\n").Split('\n') do
        let line = raw.TrimEnd()
        let trimmed = line.TrimStart()

        match skipping with
        | Some k when trimmed = "" || indentOf line > k -> if trimmed = "" then skippedBlank <- true
        | _ ->
            if skipping.IsSome && skippedBlank then out.Add ""
            skipping <- None
            skippedBlank <- false

            if trimmed.StartsWith "///" then
                ()
            elif inAttribute || trimmed.StartsWith "[<" then
                // An attribute, over one line or several, belongs to the declaration it precedes.
                attrs.Add line
                inAttribute <- not (trimmed.Contains ">]")
            elif trimmed = "internal" || trimmed = "private" then
                // A hidden representation: that it is hidden is part of the API, its fields are not.
                out.AddRange attrs
                attrs.Clear()
                out.Add line
                skipping <- Some(indentOf line)
            elif header.IsNone && bareMember.IsMatch line then
                header <- Some line
            elif hidden.IsMatch line then
                skipping <- Some(match header with Some h -> indentOf h | None -> indentOf line)
                header <- None
                attrs.Clear()
            else
                out.AddRange attrs
                attrs.Clear()
                header |> Option.iter out.Add
                header <- None
                out.Add line

    out.AddRange attrs

    // A namespace with nothing in it (the SDK's generated AssemblyInfo opens `namespace FSharp`)
    // says nothing about the API.
    let lines = List.ofSeq out

    let rec dropEmpty (ls: string list) =
        match ls with
        | l :: rest when l.StartsWith "namespace " ->
            match rest |> List.skipWhile ((=) "") with
            | [] -> []
            | next :: _ when next.StartsWith "namespace " -> dropEmpty rest
            | _ -> l :: dropEmpty rest
        | l :: rest -> l :: dropEmpty rest
        | [] -> []

    // One blank line at most between declarations, none at either end.
    dropEmpty lines
    |> Seq.fold
        (fun (acc: string list) l ->
            match acc with
            | "" :: _ when l = "" -> acc
            | [] when l = "" -> acc
            | _ -> l :: acc)
        []
    |> List.skipWhile ((=) "")
    |> List.rev
    |> String.concat "\n"
    |> fun s -> s + "\n"

let signatureOf (name: string) =
    [ "netstandard2.1"; "net8.0"; "net10.0" ]
    |> List.map (fun tfm -> Path.Combine(sigDir, sprintf "%s.%s.fsi" name tfm))
    |> List.tryFind File.Exists
    |> Option.map (File.ReadAllText >> clean)

let listings =
    packages
    |> Array.map (fun name ->
        match signatureOf name with
        | Some text -> name, text
        | None -> failwithf "no signature was written for %s" name)

if check then
    let fresh = Path.Combine(sigDir, "listing")

    if Directory.Exists fresh then
        Directory.Delete(fresh, true)

    Directory.CreateDirectory fresh |> ignore

    for name, text in listings do
        File.WriteAllText(Path.Combine(fresh, name + ".fsi"), text)

    let differs = run "git" [ "diff"; "--no-index"; "--stat"; "--exit-code"; listingDir; fresh ]

    if differs <> 0 then
        run "git" [ "--no-pager"; "diff"; "--no-index"; listingDir; fresh ] |> ignore
        eprintfn "\nThe public API differs from api/. If the change is meant, run `dotnet fsi tools/api/listing.fsx` and commit api/."
        exit 1
    else
        printfn "api/: %d packages, unchanged" listings.Length
else
    Directory.CreateDirectory listingDir |> ignore

    for f in Directory.GetFiles(listingDir, "*.fsi") do
        File.Delete f

    for name, text in listings do
        File.WriteAllText(Path.Combine(listingDir, name + ".fsi"), text)

    printfn "api/: %d packages written" listings.Length
