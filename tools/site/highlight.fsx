// The site's code in colour, at build time: `#load`ed by tools/site/build.fsx.
//
// An F# block is coloured by FCS, as an editor colours it: its tokenizer for keywords, strings,
// numbers, comments and operators, and its classification of the session the block is part of
// for what each name is, a type, a unit of measure, a module, a function, a value or a union
// case. The blocks of a session are checked as one script, against the assemblies the guide's
// test runs them with, so a name a block took from an earlier one is coloured for what it is. A
// block that must not run here, or is meant not to compile, is checked in a module of its own, so
// what it binds does not reach the blocks after it. A C# block is coloured from Roslyn's semantic
// model of its page's blocks. Each kind is a class; site.css colours them in both themes.

#r "FSharp.Compiler.Service"
#r "nuget: Microsoft.CodeAnalysis.CSharp, 4.14.0"

open System
open System.IO
open System.Text
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.EditorServices
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text
open FSharp.Compiler.Tokenization
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp

/// A block's code as HTML: each run of one kind a `<span>` of its class, the rest as it is.
let private html (code: string) (kinds: string[]) =
    let sb = StringBuilder()
    let mutable i = 0

    while i < code.Length do
        let k = kinds.[i]
        let s = i

        while i < code.Length && kinds.[i] = k do
            i <- i + 1

        let text = Net.WebUtility.HtmlEncode(code.Substring(s, i - s))

        if isNull k then sb.Append text |> ignore
        else sb.Append("<span class=\"").Append(k).Append("\">").Append(text).Append("</span>") |> ignore

    sb.ToString()

/// Where each line of `text` begins.
let private lineStarts (text: string) =
    [| yield 0
       for i in 0 .. text.Length - 1 do
           if text.[i] = '\n' then yield i + 1 |]

// ---- F# --------------------------------------------------------------------------------

let private checker = lazy (FSharpChecker.Create())

/// The kind a lexical class is drawn as.
let private lexical (c: FSharpTokenColorKind) =
    match c with
    | FSharpTokenColorKind.Keyword
    | FSharpTokenColorKind.PreprocessorKeyword -> "kw"
    | FSharpTokenColorKind.String -> "str"
    | FSharpTokenColorKind.Number -> "num"
    | FSharpTokenColorKind.Comment
    | FSharpTokenColorKind.InactiveCode -> "com"
    | FSharpTokenColorKind.Operator -> "op"
    | _ -> null

/// The kind a name of one of FCS's classes is drawn as, as Tambour draws it.
let private semantic (c: SemanticClassificationType) =
    match c with
    | SemanticClassificationType.ReferenceType
    | SemanticClassificationType.ConstructorForReferenceType
    | SemanticClassificationType.DisposableType
    | SemanticClassificationType.Type
    | SemanticClassificationType.TypeDef
    | SemanticClassificationType.Exception
    | SemanticClassificationType.Delegate
    | SemanticClassificationType.Interface
    | SemanticClassificationType.TypeArgument -> "ty"
    | SemanticClassificationType.ValueType
    | SemanticClassificationType.ConstructorForValueType
    | SemanticClassificationType.Enumeration -> "st"
    | SemanticClassificationType.Module
    | SemanticClassificationType.Namespace -> "mod"
    | SemanticClassificationType.UnionCase -> "case"
    | SemanticClassificationType.Function
    | SemanticClassificationType.IntrinsicFunction
    | SemanticClassificationType.Method
    | SemanticClassificationType.ExtensionMethod -> "fn"
    | SemanticClassificationType.Property
    | SemanticClassificationType.Field
    | SemanticClassificationType.RecordField
    | SemanticClassificationType.RecordFieldAsFunction
    | SemanticClassificationType.UnionCaseField
    | SemanticClassificationType.MutableRecordField
    | SemanticClassificationType.Event -> "prop"
    | SemanticClassificationType.Literal
    | SemanticClassificationType.Value
    | SemanticClassificationType.LocalValue
    | SemanticClassificationType.MutableVar
    | SemanticClassificationType.DisposableTopLevelValue
    | SemanticClassificationType.DisposableLocalValue
    | SemanticClassificationType.NamedArgument -> "val"
    | SemanticClassificationType.Operator -> "op"
    | SemanticClassificationType.ComputationExpression -> "kw"
    | _ -> null

/// A block of a session: its code, and whether it is checked aside, in a module of its own.
type FSharpBlock = { Code: string; Aside: bool }

/// The lexical kinds of a block, line by line as FCS's tokenizer reads it.
let private tokenized (code: string) =
    let kinds: string[] = Array.zeroCreate code.Length
    let tokenizer = FSharpSourceTokenizer([], Some "block.fsx", None, None)
    let mutable state = FSharpTokenizerLexState.Initial

    for start, line in Array.zip (lineStarts code) (code.Split '\n') do
        let lexer = tokenizer.CreateLineTokenizer line
        let mutable more = true

        while more do
            match lexer.ScanToken state with
            | Some t, next ->
                state <- next
                let k = lexical t.ColorClass

                if not (isNull k) then
                    for c in t.LeftColumn .. min t.RightColumn (line.Length - 1) do
                        kinds.[start + c] <- k
            | None, next ->
                state <- next
                more <- false

    kinds

/// Each block of a session as HTML. `references` are the assemblies it is checked against, and
/// `prelude` is code ahead of its first block.
let fsharp (references: string list) (prelude: string) (blocks: FSharpBlock list) =
    let script = StringBuilder()

    for r in references do
        script.AppendLine(sprintf "#r @\"%s\"" r) |> ignore

    script.AppendLine prelude |> ignore

    // Where each block's first line is in the script (FCS counts lines from 1), and how far in.
    let placed =
        blocks
        |> List.mapi (fun i b ->
            let indent = if b.Aside then 4 else 0
            if b.Aside then script.AppendLine(sprintf "module Aside%d =" i) |> ignore
            let first = (script.ToString() |> Seq.filter ((=) '\n') |> Seq.length) + 1

            for line in b.Code.Split '\n' do
                // A package reference is the reader's; the script is checked against the build.
                let line = if line.StartsWith("#r \"nuget:", StringComparison.Ordinal) then "" else line
                script.AppendLine(if line = "" then "" else String(' ', indent) + line) |> ignore

            b, first, indent)

    let source = SourceText.ofString (script.ToString())
    let path = Path.Combine(Path.GetTempPath(), "marey-site-session.fsx")
    let check = checker.Force()

    let options, _ =
        check.GetProjectOptionsFromScript(path, source, assumeDotNetFramework = false) |> Async.RunSynchronously

    let classes, units =
        match check.ParseAndCheckFileInProject(path, 0, source, options) |> Async.RunSynchronously with
        | _, FSharpCheckFileAnswer.Succeeded r ->
            let units =
                r.GetAllUsesOfAllSymbolsInFile()
                |> Seq.filter (fun u ->
                    match u.Symbol with
                    | :? FSharpEntity as e -> e.IsMeasure
                    | _ -> false)
                |> Seq.map (fun u -> u.Range)
                |> Array.ofSeq

            r.GetSemanticClassification None, units
        | _ -> failwith "FCS did not check the session"

    [ for b, first, indent in placed ->
          let kinds = tokenized b.Code
          let starts = lineStarts b.Code
          let lineCount = starts.Length

          // Colour a range of the script, when it lies on one of the block's lines, over a name's
          // letters: not a comment or a string, nor the dots of a name it qualifies.
          let paint (r: range) (kind: string) =
              let line = r.StartLine - first

              if r.StartLine = r.EndLine && line >= 0 && line < lineCount then
                  let length = (if line + 1 < lineCount then starts.[line + 1] - 1 else b.Code.Length) - starts.[line]

                  for c in max 0 (r.StartColumn - indent) .. min length (r.EndColumn - indent) - 1 do
                      let at = starts.[line] + c
                      if kinds.[at] <> "com" && kinds.[at] <> "str" && b.Code.[at] <> '.' then kinds.[at] <- kind

          // The wider first, so where two classes overlap the narrower is drawn.
          for item in classes |> Array.sortByDescending (fun i -> i.Range.EndColumn - i.Range.StartColumn) do
              let kind = semantic item.Type
              if not (isNull kind) then paint item.Range kind

          for r in units do
              paint r "unit"

          html b.Code kinds ]

// ---- C# --------------------------------------------------------------------------------

/// The kind a name Roslyn resolved is drawn as.
let private symbolKind (s: ISymbol) =
    match s with
    | :? INamedTypeSymbol as t when t.TypeKind = TypeKind.Struct || t.TypeKind = TypeKind.Enum -> "st"
    | :? INamedTypeSymbol
    | :? ITypeParameterSymbol -> "ty"
    | :? INamespaceSymbol -> "mod"
    | :? IMethodSymbol as m when m.MethodKind = MethodKind.Constructor -> "ty"
    | :? IMethodSymbol -> "fn"
    | :? IFieldSymbol as f when f.ContainingType.TypeKind = TypeKind.Enum -> "case"
    | :? IPropertySymbol
    | :? IFieldSymbol
    | :? IEventSymbol -> "prop"
    | :? ILocalSymbol
    | :? IParameterSymbol
    | :? IRangeVariableSymbol -> "val"
    | _ -> null

/// Each C# block of a page as HTML. The page's blocks are one program, as a reader running them
/// in order has them, with .NET's implicit usings.
let csharp (references: string list) (blocks: string list) =
    let starts = blocks |> List.scan (fun at (b: string) -> at + b.Length + 1) 0
    let text = String.concat "\n" blocks
    let tree = CSharpSyntaxTree.ParseText(text, CSharpParseOptions(LanguageVersion.Latest))
    let usings = CSharpSyntaxTree.ParseText "global using System; global using System.Linq; global using System.Collections.Generic;"

    let platform =
        (AppContext.GetData "TRUSTED_PLATFORM_ASSEMBLIES" :?> string).Split(Path.PathSeparator) |> List.ofArray

    let compilation =
        CSharpCompilation.Create(
            "page",
            [ usings; tree ],
            platform @ references |> List.map (fun p -> MetadataReference.CreateFromFile p :> MetadataReference),
            CSharpCompilationOptions OutputKind.ConsoleApplication
        )

    let model = compilation.GetSemanticModel(tree, false)
    let kinds: string[] = Array.zeroCreate text.Length

    let paint (span: Text.TextSpan) (kind: string) =
        for i in span.Start .. span.End - 1 do
            kinds.[i] <- kind

    let root = tree.GetRoot()

    for trivia in root.DescendantTrivia() do
        match trivia.Kind() with
        | SyntaxKind.SingleLineCommentTrivia
        | SyntaxKind.MultiLineCommentTrivia
        | SyntaxKind.SingleLineDocumentationCommentTrivia -> paint trivia.Span "com"
        | _ -> ()

    for token in root.DescendantTokens() do
        let kind = token.Kind()

        let k =
            if SyntaxFacts.IsKeywordKind kind then
                "kw"
            else
                match kind with
                | SyntaxKind.StringLiteralToken
                | SyntaxKind.CharacterLiteralToken
                | SyntaxKind.InterpolatedStringStartToken
                | SyntaxKind.InterpolatedStringEndToken
                | SyntaxKind.InterpolatedStringTextToken -> "str"
                | SyntaxKind.NumericLiteralToken -> "num"
                | SyntaxKind.EqualsGreaterThanToken -> "op"
                | _ when SyntaxFacts.IsBinaryExpressionOperatorToken kind
                         || SyntaxFacts.IsPrefixUnaryExpressionOperatorToken kind
                         || SyntaxFacts.IsAssignmentExpressionOperatorToken kind -> "op"
                | SyntaxKind.IdentifierToken ->
                    (match token.Parent with
                     | :? Syntax.IdentifierNameSyntax as n when n.IsVar -> "kw"
                     | node ->
                         let symbol =
                             match model.GetDeclaredSymbol node with
                             | null ->
                                 let info = model.GetSymbolInfo node
                                 if isNull info.Symbol then Seq.tryHead info.CandidateSymbols |> Option.toObj else info.Symbol
                             | s -> s

                         if isNull symbol then null else symbolKind symbol)
                | _ -> null

        if not (isNull k) then paint token.Span k

    blocks |> List.mapi (fun i b -> html b kinds.[starts.[i] .. starts.[i] + b.Length - 1])
