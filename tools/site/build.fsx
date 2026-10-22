// The documentation site: `dotnet fsi tools/site/build.fsx SCENES OUT`.
//
// The pages are the repository's Markdown, as GitHub shows them: the README as the front page, the
// guide, and the pages beside it. A figure the guide's session drew is shown by its SVG, and drawn
// over it by `Marey.Backend.Web` from the scene the session wrote to SCENES (`MAREY_SITE_SCENES`,
// see tests/Marey.Tests/GuideTests.fs); a live figure gets its control, over the states the session
// sampled. A link to another page is a link into the site, and a link to any other file in the
// repository is a link to it on GitHub. The API reference is fsdocs', built beside this into
// OUT/reference by tools/site/build.sh.

#r "nuget: Markdig, 0.40.0"

open System
open System.IO
open System.Text
open System.Text.RegularExpressions
open Markdig

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))
let scenes = Path.GetFullPath fsi.CommandLineArgs.[1]
let out = Path.GetFullPath fsi.CommandLineArgs.[2]
let repoUrl = "https://github.com/Axym-Labs/marey"

let read (path: string) = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n")
let write (path: string) (text: string) =
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllText(path, text, UTF8Encoding false)

let rel (fromDir: string) (target: string) = Path.GetRelativePath(fromDir, target).Replace('\\', '/')
let html (s: string) = Net.WebUtility.HtmlEncode s

// ---- The pages ------------------------------------------------------------------------

type Page =
    { /// The Markdown, relative to the repository.
      Source: string
      /// The HTML, relative to the site.
      Target: string
      Title: string
      Guide: bool }

let titleOf (md: string) =
    md.Split('\n') |> Array.tryFind (fun l -> l.StartsWith "# ") |> Option.map (fun l -> l.Substring(2).Trim()) |> Option.defaultValue "Marey"

let page (source: string) (target: string) (guide: bool) =
    { Source = source
      Target = target
      Title = titleOf (read (Path.Combine(root, source)))
      Guide = guide }

let guidePages =
    Directory.GetFiles(Path.Combine(root, "docs", "guide"), "*.md")
    |> Array.map Path.GetFileName
    |> Array.filter (fun f -> f <> "README.md")
    |> Array.sort
    |> Array.map (fun f -> page ("docs/guide/" + f) ("guide/" + Path.ChangeExtension(f, ".html")) true)

let pages =
    [| page "README.md" "index.html" false
       page "docs/guide/README.md" "guide/index.html" true
       yield! guidePages
       page "docs/compatibility.md" "compatibility.html" false
       page "docs/not-in-1.0.md" "not-in-1.0.html" false
       page "docs/why.md" "why.html" false
       page "docs/notebooks/README.md" "notebooks.html" false |]

let pageOfSource = pages |> Array.map (fun p -> p.Source, p) |> dict

// ---- Links and figures ----------------------------------------------------------------

/// A link in `source`'s Markdown, as the page at `target` should have it.
let link (source: string) (target: string) (href: string) =
    if href.StartsWith "#" || Regex.IsMatch(href, "^[a-z]+:") then
        href
    else
        let path, fragment =
            match href.IndexOf '#' with
            | -1 -> href, ""
            | i -> href.Substring(0, i), href.Substring i

        let resolved = Path.GetFullPath(Path.Combine(root, Path.GetDirectoryName source, Uri.UnescapeDataString path))
        let inRepo = rel root resolved

        match pageOfSource.TryGetValue inRepo with
        | true, p -> rel (Path.GetDirectoryName(Path.Combine(out, target))) (Path.Combine(out, p.Target)) + fragment
        | _ when inRepo.StartsWith "docs/figures/" -> rel (Path.GetDirectoryName(Path.Combine(out, target))) (Path.Combine(out, "figures", Path.GetFileName inRepo))
        | _ ->
            let kind = if Directory.Exists resolved then "tree" else "blob"
            sprintf "%s/%s/main/%s%s" repoUrl kind (inRepo.TrimEnd('/')) fragment

/// The width and height an SVG states.
let svgSize (svg: string) =
    let m = Regex.Match(read svg, "<svg[^>]*\\bwidth=\"([0-9.]+)\"[^>]*\\bheight=\"([0-9.]+)\"")
    if m.Success then m.Groups.[1].Value, m.Groups.[2].Value else "720", "450"

/// A figure the guide drew: its SVG, under the canvas that draws its scene when there is one.
let figure (toRoot: string) (name: string) (alt: string) =
    let svg = Path.Combine(root, "docs", "figures", name + ".svg")
    let w, h = svgSize svg
    let poster = sprintf "<img class=\"poster\" src=\"%sfigures/%s.svg\" alt=\"%s\" width=\"%s\" height=\"%s\" loading=\"lazy\">" toRoot name alt w h

    let canvas =
        if File.Exists(Path.Combine(scenes, name + ".live")) then
            sprintf "<canvas class=\"drawn\" data-live=\"%sscenes/%s.live\" data-w=\"%s\" data-h=\"%s\" hidden></canvas>" toRoot name w h
        elif File.Exists(Path.Combine(scenes, name + ".scene")) then
            sprintf "<canvas class=\"drawn\" data-scene=\"%sscenes/%s.scene\" data-w=\"%s\" data-h=\"%s\" hidden></canvas>" toRoot name w h
        else
            ""

    sprintf "<figure class=\"marey\" id=\"figure-%s\"><div class=\"stage\">%s%s</div></figure>" name poster canvas

// ---- Rendering ------------------------------------------------------------------------

let pipeline =
    MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoIdentifiers(Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
        .UseEmphasisExtras()
        .Build()

/// Every figure on a page, with the section it is under (its id and its heading), for the gallery.
let shown = ResizeArray<Page * (string * string) * string * string>()

let render (p: Page) =
    let dir = Path.GetDirectoryName(Path.Combine(out, p.Target))
    let toRoot = match rel dir out with "." -> "" | r -> r + "/"
    let md = read (Path.Combine(root, p.Source))
    let body = Markdown.ToHtml(md, pipeline)

    // The section each figure falls under, by the last heading before it.
    let mutable section = "", p.Title

    let body =
        Regex.Replace(
            body,
            "<h([1-3]) id=\"([^\"]+)\">(.*?)</h\\1>|(?:<p>)?<img src=\"([^\"]*figures/([A-Za-z0-9-]+)\\.svg)\" alt=\"([^\"]*)\"[^>]*>(?:</p>)?",
            MatchEvaluator(fun m ->
                if m.Groups.[2].Success then
                    section <- m.Groups.[2].Value, Regex.Replace(m.Groups.[3].Value, "<[^>]+>", "")
                    m.Value
                else
                    let name, alt = m.Groups.[5].Value, m.Groups.[6].Value
                    shown.Add((p, section, name, alt))
                    figure toRoot name alt)
        )

    let body =
        Regex.Replace(body, "(href|src)=\"([^\"]+)\"", MatchEvaluator(fun m ->
            if m.Value.Contains "figures/" && m.Groups.[1].Value = "src" then m.Value
            else sprintf "%s=\"%s\"" m.Groups.[1].Value (link p.Source p.Target (Net.WebUtility.HtmlDecode m.Groups.[2].Value))))

    let body = body.Replace("<pre><code class=\"language-fsharp-error\">", "<pre class=\"refused\"><code class=\"language-fsharp\">")
    toRoot, body

let nav (toRoot: string) (current: Page option) =
    let item (label: string) (target: string) (active: bool) =
        sprintf "<a href=\"%s%s\"%s>%s</a>" toRoot target (if active then " aria-current=\"page\"" else "") label

    let guide = current |> Option.exists (fun p -> p.Guide)
    let at t = current |> Option.exists (fun p -> p.Target = t)

    String.concat
        ""
        [ "<header class=\"bar\"><a class=\"brand\" href=\"" + toRoot + "index.html\">Marey</a><nav>"
          item "Guide" "guide/index.html" guide
          item "Gallery" "gallery.html" (current.IsNone)
          item "API reference" "reference/reference/index.html" false
          item "Compatibility" "compatibility.html" (at "compatibility.html")
          sprintf "<a href=\"%s\">GitHub</a>" repoUrl
          "</nav></header>" ]

let sidebar (toRoot: string) (current: Page) =
    if not current.Guide then
        ""
    else
        let items =
            guidePages
            |> Array.map (fun g ->
                sprintf "<li><a href=\"%s%s\"%s>%s</a></li>" toRoot g.Target (if g.Target = current.Target then " aria-current=\"page\"" else "") (html g.Title))

        "<aside class=\"toc\"><a href=\"" + toRoot + "guide/index.html\">The guide</a><ol>" + String.concat "" items + "</ol></aside>"

let pager (toRoot: string) (current: Page) =
    match Array.tryFindIndex (fun g -> g.Target = current.Target) guidePages with
    | None -> ""
    | Some i ->
        let side (j: int) (cls: string) =
            if j < 0 || j >= guidePages.Length then "<span></span>"
            else sprintf "<a class=\"%s\" href=\"%s%s\">%s</a>" cls toRoot guidePages.[j].Target (html guidePages.[j].Title)

        "<nav class=\"pager\">" + side (i - 1) "prev" + side (i + 1) "next" + "</nav>"

let document (toRoot: string) (title: string) (current: Page option) (body: string) =
    String.concat
        "\n"
        [ "<!DOCTYPE html>"
          "<html lang=\"en\"><head><meta charset=\"utf-8\">"
          "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
          sprintf "<title>%s</title>" (html (if title = "Marey" then "Marey" else title + " · Marey"))
          sprintf "<link rel=\"stylesheet\" href=\"%sassets/site.css\">" toRoot
          sprintf "<link rel=\"icon\" href=\"%sassets/icon.png\">" toRoot
          "</head><body>"
          nav toRoot current
          sprintf "<div class=\"layout%s\">" (if current |> Option.exists (fun p -> p.Guide) then " with-toc" else "")
          (match current with Some p -> sidebar toRoot p | None -> "")
          "<main>"
          body
          (match current with Some p -> pager toRoot p | None -> "")
          "</main></div>"
          sprintf "<script src=\"%sassets/marey-view.js\"></script>" toRoot
          sprintf "<script src=\"%sassets/site.js\"></script>" toRoot
          "</body></html>" ]

// ---- Build ----------------------------------------------------------------------------

if Directory.Exists out then
    for d in Directory.GetDirectories out do
        if Path.GetFileName d <> "reference" then Directory.Delete(d, true)

    for f in Directory.GetFiles out do
        File.Delete f

Directory.CreateDirectory out |> ignore

for p in pages do
    let toRoot, body = render p
    write (Path.Combine(out, p.Target)) (document toRoot p.Title (Some p) body)

// The gallery: every figure a page shows, linked to the section it is in.
// A figure is shown where it is drawn, the page whose block says `output=` it, in reading order.
let gallery =
    let draws (p: Page) (name: string) = Regex.IsMatch(read (Path.Combine(root, p.Source)), "```fsharp[^\n]*\\boutput=" + name + "\\b")
    let order (p: Page) = Array.findIndex (fun q -> q.Target = p.Target) pages

    shown
    |> Seq.groupBy (fun (_, _, name, _) -> name)
    |> Seq.map (fun (name, all) -> all |> Seq.tryFind (fun (p, _, _, _) -> draws p name) |> Option.defaultValue (Seq.head all))
    |> Seq.sortBy (fun (p, _, _, _) -> if p.Guide then order p else -1)
    |> Seq.map (fun (p, (id, heading), name, alt) ->
        let href = p.Target + (if id = "" then "" else "#" + id)
        let caption = if heading = p.Title then html p.Title else sprintf "%s · %s" (html p.Title) heading
        sprintf "<a class=\"tile\" href=\"%s\"><img src=\"figures/%s.svg\" alt=\"%s\" loading=\"lazy\"><span>%s</span></a>" href name alt caption)
    |> String.concat "\n"

write
    (Path.Combine(out, "gallery.html"))
    (document "" "Gallery" None ("<h1>Gallery</h1><p>Every figure the guide and the README draw, each linked to where it is made.</p><div class=\"gallery\">" + gallery + "</div>"))

let copy (from: string) (into: string) =
    Directory.CreateDirectory(Path.GetDirectoryName into) |> ignore
    File.Copy(from, into, true)

for f in Directory.GetFiles(Path.Combine(root, "docs", "figures"), "*.svg") do
    copy f (Path.Combine(out, "figures", Path.GetFileName f))

for f in Directory.GetFiles scenes do
    copy f (Path.Combine(out, "scenes", Path.GetFileName f))

copy (Path.Combine(root, "src", "Marey.Html", "view", "marey-view.js")) (Path.Combine(out, "assets", "marey-view.js"))
copy (Path.Combine(root, "assets", "icon.png")) (Path.Combine(out, "assets", "icon.png"))

for f in [ "site.css"; "site.js" ] do
    copy (Path.Combine(__SOURCE_DIRECTORY__, f)) (Path.Combine(out, "assets", f))

// The faces a canvas draws the themes' text in, the ones the PNG backend carries.
for f in Directory.GetFiles(Path.Combine(root, "src", "Marey.Backend.Skia", "fonts")) do
    copy f (Path.Combine(out, "assets", "fonts", Path.GetFileName f))

printfn "%d pages, %d figures, into %s" (pages.Length + 1) shown.Count out
