/// Step F, D161 — `Html.renderMode` is one setting for the process, and xUnit runs test classes in
/// parallel. A test that depends on it sets it through `withRenderMode`, inside the
/// `RenderModes.Collection`, whose tests run one at a time.
module Marey.Tests.RenderModes

open Marey

[<Literal>]
let Collection = "Html.renderMode"

/// `body` under `mode`, and the mode put back whatever happens.
let withRenderMode (mode: RenderMode) (body: unit -> 'a) : 'a =
    let before = Html.renderMode
    Html.renderMode <- mode

    try
        body ()
    finally
        Html.renderMode <- before
