namespace Quiver

open System
open Microsoft.DotNet.Interactive.Formatting

// ======================================================================================
// THE KERNEL HOOK.
//
// The only file in the repository that references .NET Interactive. Everything it displays comes
// from `Html.fs`, which knows nothing about kernels; this file is nine lines of registration and
// a long comment about the two places it can go wrong.
//
// ---------------------------------------------------------------------------------------
// WHY A MEASURE-GENERIC TYPE NEEDS ONLY ONE REGISTRATION.
// ---------------------------------------------------------------------------------------
//
// `Formatter` dispatches on `System.Type`, so a type that is generic in a *measure* looks like a
// problem: `U<m>` and `U<s>` are different types to the F# compiler, and registering one of them
// would leave the other displaying as a reflection dump.
//
// It is not a problem, and the reason is worth stating because it is easy to assume the opposite.
// Measures are erased at runtime *including in a class's type arguments* — `typeof<U<m>>` and
// `typeof<U<s>>` are the same `System.Type`, whose name is plain `Quiver.U` with no arity suffix.
// One registration at an arbitrary instantiation therefore covers every unit that will ever
// exist. `U<1>` is used below because it needs no `open` of the SI measures to write down.
//
// (This was checked against the assembly rather than assumed, and it is checked again by a test,
// because "measures are erased" is the sort of half-truth that holds for `float<'u>` and could
// plausibly have failed for a generic class.)
//
// ---------------------------------------------------------------------------------------
// WHAT `SetPreferredMimeTypesFor` ACTUALLY DOES: NOTHING, TODAY.
// ---------------------------------------------------------------------------------------
//
// The reasoning that put it here was that `Register` says *how* to render a type as HTML without
// saying that HTML is the rendering to use, so a registration without a stated preference would
// work in the common case and fall back to a reflection dump in the others.
//
// That is wrong, and it is worth recording as wrong rather than quietly deleting. Measured
// against the package: `Formatter.GetPreferredMimeTypesFor` answers `["text/html"]` for **every**
// type — `int`, an unregistered record, anything — before a single registration happens, and it
// keeps answering that even after `Formatter.DefaultMimeType` is set to `text/plain`. So the
// preference this line declares is the preference that already held, and removing the line
// changes no observable behaviour anywhere in the suite. The first version of the accompanying
// test asserted the preference *was* `text/html` and therefore passed with the line deleted —
// theatre, of exactly the kind gate 1 caught itself committing in Phase 1.
//
// The line stays, for one reason that is not verifiable from here: it is the documented way to
// state the intent, and a host that ships a different default is the case it covers. The test
// beside it was rewritten to check the thing that matters instead — that the *composition* a
// notebook cell performs (ask for a type's preferred mime types, format with each) produces our
// HTML — which fails both if our registration breaks and if that upstream default ever moves.
// ======================================================================================

/// Registers Quiver's types with .NET Interactive so they display as HTML in a notebook cell.
///
/// A package reference is normally enough — `interactive-extensions/dotnet/extension.dib` calls
/// `register()` when the kernel loads the package. These entry points are for the explicit path:
/// a kernel that does not run extensions, or a session that wants a different figure size.
///
/// **Requires a .NET Interactive kernel.** This package compiles against
/// `Microsoft.DotNet.Interactive.Formatting` but deliberately does not ship it — see the
/// `.fsproj` for why — so calling this from a process with no kernel raises
/// `FileNotFoundException`. `Html` has no such requirement and works anywhere.
[<RequireQualifiedAccess>]
module Formatters =

    /// Not named `Html`: that is the module this file's renderings come from, and shadowing it
    /// here would be a confusing way to save four characters.
    let private htmlMime = HtmlFormatter.MimeType

    /// One type, one renderer, and a preference that is already the default — see the header.
    let private register1<'a> (render: 'a -> string) =
        Formatter.Register<'a>(Func<'a, string>(render), htmlMime)
        Formatter.SetPreferredMimeTypesFor(typeof<'a>, [| htmlMime |])

    /// Register every formatter, compiling figures and scenes at the given size.
    let registerWith (extent: Extent<px>) : unit =
        register1<Figure> (Html.figureAt extent)
        register1<Node> (Html.nodeAt extent)
        register1<Color> Html.color
        register1<Ramp> Html.ramp
        // A palette is a `Color[]` in this library — there is no `Palette` type, only the module
        // — so this is the registration that makes `Palette.okabeIto` and `theme.Categorical`
        // display as strips.
        register1<Color[]> Html.palette
        register1<PaletteReport> Html.paletteReport
        register1<Dim> Html.dim
        register1<U<1>> Html.witness
        register1<IScaleErased> Html.scale

    /// Register every formatter at `Html.defaultSize`. Safe to call more than once: a second
    /// registration for a type replaces the first.
    let register () : unit = registerWith Html.defaultSize
