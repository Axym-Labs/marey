# Marey in the browser

A figure built, laid out and drawn in the page by the same F# that runs in a notebook kernel,
compiled to JavaScript by [Fable](https://fable.io). Two sliders move a damped oscillation, and
every move runs the whole pipeline in the browser.

```
dotnet tool restore
dotnet fable examples/web -o examples/web/out
python3 -m http.server -d examples/web
```

Then open <http://localhost:8000>. Any static file server will do: the output is ES modules and
needs no bundler. The page is drawn once straight away, laid out with Marey's approximate text
widths if the page's fonts have not arrived yet (it says so under the figure), and again with the
canvas's own measurements once they have.

The second figure is a cloud of up to a million points, compiled in the page like the first. A
batch of 2000 marks or more is drawn by WebGL2 and composited into the canvas in its place; the
checkbox turns that off, so the two can be timed against each other and compared by eye.

The third is a simulation in the page: a mass on a spring stepped by velocity Verlet, played in
phase space at one second a second. Under it are its position and velocity at 10 s to twelve
decimals, the same digits the .NET suite prints for the same run (`tests/Marey.Tests/golden/
sim-runs.txt`). A chaotic system would not agree with .NET after half a minute, because the
browser's `sin` and `exp` differ in the last bit; this one has no such call in its step.

The project references the Marey projects in this repository. An app of your own references the
packages instead, `Marey.Plot`, `Marey.Backend.Web` and `Marey.Sim`, and Fable compiles them from
the sources they carry.
