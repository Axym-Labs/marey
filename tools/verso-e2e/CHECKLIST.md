# Marey in Verso, by hand

What `verso-e2e.sh` cannot check headless: a channel to the kernel, the Extensions panel, a save
and a reopen. The script leaves the browser host running on `e2e.verso` in its work folder.

Note what you see against each item; the failures and their wording are what an upstream issue or
a fix needs.

## The extension

The notebook's first cell loads it with `#!extension`, in the guide's order.

- [ ] Running the first cell asks for consent once, naming `Marey.Verso` and its version
- [ ] Denied, or switched off in the Extensions panel: a figure cell shows a field table, not a
      figure (the readme's "If something does not display", item 1)
- [ ] Optional: the Extensions panel lists `Marey.Verso` at the version under test and installs
      it. It lists no prerelease; whether it searches the work folder's local feed is unchecked

## Run all

- [ ] The first cell restores `Marey` and its packages without an error
- [ ] **A figure**: drawn, as wide as the cell, axis titles read `time [s]` and `position [cm]`
      or `[mm]`
- [ ] **A film**: loops in the cell without a click
- [ ] **Ten thousand points**: drawn on a canvas, labels where an SVG would put them
- [ ] **A live figure**, the `amplitude` and `damping` sliders:
  - [ ] dragging either redraws the curve while dragging, not only on release
  - [ ] the slider labels carry their units (`m`, `Hz`), with a prefix that does not change mid-drag
  - [ ] a dragged box zooms; shift-drag pans; a double-click goes back
  - [ ] hovering reads the position under the pointer, in units
- [ ] **A large live figure**: the `t` slider moves the large marker without a visible redraw of
      the 10⁵ points
- [ ] **An error**: the cell fails with F#'s unit-of-measure mismatch, pointing at its one line;
      every cell above is unaffected

## Save and reopen

- [ ] Move both sliders of the live figure away from their defaults, then save
- [ ] Close the tab, stop the host (Ctrl-C), start it again with the command the script printed,
      open `e2e.verso`
- [ ] Before running anything: every figure shows as it was saved, the live figure at the state
      it was left in, its controls disabled and a line saying it is static until the kernel runs
- [ ] The static figures (the first, the film, ten thousand points) show no "Showing saved state"
      banner. If they do, that is Verso's banner on any widget without a channel, not a Marey fault
- [ ] Run all again: the live figure answers its sliders again

## The two setup lines swapped (optional)

The script runs both orders headless. On Verso 1.2.3 the order matters: with `#r` first, the
`#!extension` line is silently ignored.

- [ ] In the notebook's first cell with the two lines swapped: no error, and a field table in
      place of each figure, as headless

## The clip

- [ ] A screen clip of the live figure answering a dragged slider, for the README in place of the
      recorded replay

## Afterwards

The script printed a `rm -rf` for what a local pack left in `~/.nuget/packages` and
`~/.verso/extensions`; nothing else outside its work folder was written.
