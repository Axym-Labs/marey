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

The project references the Marey projects in this repository. An app of your own references the
packages instead, `Marey.Plot` and `Marey.Backend.Web`, and Fable compiles them from the sources
they carry.
