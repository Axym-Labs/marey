(*  What ships names no internal decision.

        dotnet fsi tools/refs/check.fsx

    Marey's design history (decision numbers such as D175, sections of the design document such
    as §9, milestones such as M4.5, phases and gates) lives in the project's notes and in commit
    messages. The source, its project files, its package readmes and the XML documentation each
    package ships must read without them, to a user, a contributor and anyone who decompiles an
    assembly. This fails, naming file and line, on any that comes back.

    The XML documentation is read from `src/*/bin/Release/`, so run this after a Release build
    to check it; without one, only the sources are checked.

    A few code tokens look like references and are not: the affine matrix's fields (M11, M12,
    M21, M22), the wire format's F64, and SVG path data (`M0 0`). They are allowed by name, as is
    a link to a section of the public guide ("the guide, §20"). *)

open System
open System.IO
open System.Text.RegularExpressions

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

let reference =
    Regex(@"\bD\d{1,3}\b|§\s?\d|\bM(?:\d|10|11)(?:\.\d)?\b|\bF\d{1,2}\b|\bG\d{1,2}\b|\b(?:phase|gate) \d+\b|\bfindings? \d+\b")

let allowed = Regex(@"\bM(?:11|12|21|22)\b|\bF(?:32|64)\b|\bM0 0\b|guide, §\s?\d+")

let skipped (path: string) =
    let p = path.Replace('\\', '/')
    p.Contains "/obj/" || p.Contains "/node_modules/" || p.EndsWith "/view/marey-view.js"

let sources =
    [ "*.fs"; "*.fsi"; "*.js"; "*.ts"; "*.fsproj"; "*.props"; "*.targets"; "*.md" ]
    |> List.collect (fun pattern -> Directory.GetFiles(Path.Combine(root, "src"), pattern, SearchOption.AllDirectories) |> List.ofArray)
    |> List.filter (fun p -> not (skipped p) && not (p.Replace('\\', '/').Contains "/bin/"))

let rootFiles =
    [ "Directory.Build.props"; "Directory.Build.targets"; "NUGET.md" ]
    |> List.map (fun f -> Path.Combine(root, f))
    |> List.filter File.Exists

let docs =
    Directory.GetDirectories(Path.Combine(root, "src"))
    |> Array.collect (fun dir ->
        let release = Path.Combine(dir, "bin", "Release")
        let name = Path.GetFileName dir

        if Directory.Exists release then
            Directory.GetFiles(release, name + ".xml", SearchOption.AllDirectories)
        else
            [||])
    |> Array.groupBy Path.GetFileName
    // The newest build of each package: an old target framework's output can linger in `bin`.
    |> Array.map (fun (_, files) -> files |> Array.maxBy File.GetLastWriteTimeUtc)
    |> List.ofArray

let hits =
    [ for path in sources @ rootFiles @ docs do
          let lines = File.ReadAllLines path

          for i in 0 .. lines.Length - 1 do
              let line = allowed.Replace(lines.[i], "")

              if reference.IsMatch line then
                  yield sprintf "%s:%d: %s" (Path.GetRelativePath(root, path)) (i + 1) (lines.[i].Trim()) ]

if hits.IsEmpty then
    printfn "no internal references: %d sources, %d documentation files" (sources.Length + rootFiles.Length) docs.Length
else
    for h in hits do
        eprintfn "%s" h

    eprintfn "\n%d lines name a decision, a design section, a milestone, a phase or a gate. Say the reason instead." hits.Length
    exit 1
