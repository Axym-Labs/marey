# Marey.Analyzers

F# analyzers for [Marey](https://axym-labs.github.io/marey/)'s unit witnesses, for Ionide and the
`fsharp-analyzers` CLI. Each offers a fix for a call the compiler refuses for want of the right
witness:

```fsharp-error
let t = Array.init 24 (fun i -> float i * 0.025<s>)
let x = t |> Array.map (fun t -> 0.5 * 9.81<m/s^2> * t * t)

Chart.lineIn t x                  // MAREY001: inserts U.second U.metre
Chart.lineIn U.metre U.metre t x  // MAREY002: U.second for the first
Chart.line t x                    // MAREY003: Chart.lineIn U.second U.metre t x,
                                  //   or Chart.line (U.second.Of t) (U.metre.Of x)
```

A witness is the one Marey declares for the measure as written, `U.metrePerSecondSquared` for
`m/s^2`. A measure Marey declares no witness for is composed as it is written: `V/m` is
`U.div U.volt U.metre`, and a torque in `N m` stays `U.mul U.newton U.metre` rather than becoming
joules. A plain `float` gets `U.unspecified`. An argument's unit is read from its type where the
argument is a name (`t`, `run.Times`); for an argument of another shape, no fix is offered.

A file that compiles gets no message.

## Using it

The package is a development dependency with nothing to reference: its analyzers are in
`analyzers/dotnet/fs`, where a host loads them.

```xml
<PackageReference Include="Marey.Analyzers" Version="…" PrivateAssets="all" />
```

The CLI runs them over a project:

```
dotnet tool install fsharp-analyzers
dotnet fsharp-analyzers --project App.fsproj --analyzers-path ~/.nuget/packages/marey.analyzers/<version>/analyzers/dotnet/fs
```

In VS Code, Ionide runs them as you type and offers the fixes as quick fixes, with
`"FSharp.enableAnalyzers": true` and the same folder in `"FSharp.analyzersPath"`.

Built against FSharp.Analyzers.SDK 0.41.0, which is what a host has to run: a host loads only
analyzers built against its own SDK. The fixes themselves are `Marey.Witnesses`, which a tool
that checks F# itself calls directly.
