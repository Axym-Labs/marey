(*  Every public type and module of every package has a doc comment.

        dotnet fsi tools/api/undocumented.fsx

    Reads each package's Release assembly with System.Reflection.Metadata (nothing is loaded, so
    native dependencies do not matter) and its XML documentation, and lists the public types and
    modules with no `<summary>`. Union cases' generated classes and compiler-generated types are
    not declarations a reader meets, and are skipped. Run after a Release build. *)

open System
open System.IO
open System.Reflection
open System.Reflection.Metadata
open System.Reflection.PortableExecutable
open System.Xml.Linq

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

let packages =
    Directory.GetDirectories(Path.Combine(root, "src"))
    |> Array.choose (fun dir ->
        let name = Path.GetFileName dir
        let proj = Path.Combine(dir, name + ".fsproj")

        let text = if File.Exists proj then File.ReadAllText proj else ""

        // `Marey` packs no assembly, and `Marey.Analyzers` none a user calls, so there is
        // nothing of them to document.
        if File.Exists proj
           && not (text.Contains "<IsPackable>false</IsPackable>")
           && not (text.Contains "<IncludeBuildOutput>false</IncludeBuildOutput>") then
            let release = Path.Combine(dir, "bin", "Release")

            if Directory.Exists release then
                Directory.GetFiles(release, name + ".dll", SearchOption.AllDirectories)
                |> Array.sortByDescending File.GetLastWriteTimeUtc
                |> Array.tryHead
                |> Option.map (fun dll -> name, dll)
            else
                None
        else
            None)

/// The F# construct a type was compiled from, from its `CompilationMappingAttribute`.
let constructOf (md: MetadataReader) (t: TypeDefinition) =
    t.GetCustomAttributes()
    |> Seq.tryPick (fun h ->
        let a = md.GetCustomAttribute h

        let typeName =
            match a.Constructor.Kind with
            | HandleKind.MemberReference ->
                let mr = md.GetMemberReference(MemberReferenceHandle.op_Explicit a.Constructor)

                match mr.Parent.Kind with
                | HandleKind.TypeReference -> md.GetString((md.GetTypeReference(TypeReferenceHandle.op_Explicit mr.Parent)).Name)
                | _ -> ""
            | _ -> ""

        if typeName = "CompilationMappingAttribute" then
            let blob = md.GetBlobReader a.Value
            blob.ReadUInt16() |> ignore
            Some(blob.ReadInt32() &&& 31)
        else
            None)

let mutable total = 0
let mutable missing = 0

for name, dll in packages do
    use stream = File.OpenRead dll
    use pe = new PEReader(stream)
    let md = pe.GetMetadataReader()
    let xmlPath = Path.ChangeExtension(dll, ".xml")

    let documented =
        if File.Exists xmlPath then
            XDocument.Load(xmlPath).Descendants(XName.Get "member")
            |> Seq.filter (fun m -> m.Element(XName.Get "summary") <> null && m.Element(XName.Get "summary").Value.Trim() <> "")
            |> Seq.map (fun m -> m.Attribute(XName.Get "name").Value)
            |> Set.ofSeq
        else
            Set.empty

    let rec fullName (h: TypeDefinitionHandle) =
        let t = md.GetTypeDefinition h
        let n = md.GetString t.Name
        let declaring = t.GetDeclaringType()

        if declaring.IsNil then
            let ns = md.GetString t.Namespace
            if ns = "" then n else ns + "." + n
        else
            fullName declaring + "." + n

    let rec isPublic (h: TypeDefinitionHandle) =
        let t = md.GetTypeDefinition h
        let vis = t.Attributes &&& TypeAttributes.VisibilityMask

        if t.GetDeclaringType().IsNil then
            vis = TypeAttributes.Public
        else
            vis = TypeAttributes.NestedPublic && isPublic (t.GetDeclaringType())

    let gaps =
        [ for h in md.TypeDefinitions do
              let t = md.GetTypeDefinition h
              let n = md.GetString t.Name
              let parent = t.GetDeclaringType()

              let parentIsUnion =
                  not parent.IsNil && constructOf md (md.GetTypeDefinition parent) = Some 1

              let generated = n.Contains "@" || n.Contains "<" || n = "Tags" || n.StartsWith "<"

              if isPublic h && not generated && not parentIsUnion && constructOf md t <> None then
                  let id = "T:" + (fullName h).Replace('+', '.').Split('`').[0]
                  let id = if (fullName h).Contains "`" then "T:" + (fullName h).Replace('+', '.') else id
                  total <- total + 1

                  if not (documented.Contains id) then
                      yield id.Substring 2 ]

    missing <- missing + gaps.Length

    if not gaps.IsEmpty then
        printfn "== %s: %d undocumented" name gaps.Length

        for g in gaps |> List.sort do
            printfn "   %s" g

printfn "\n%d of %d public types and modules have no doc comment" missing total
if missing > 0 then exit 1
