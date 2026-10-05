# A font, committed on purpose

`LiberationSans-Regular.ttf`, Liberation Fonts 2.1.5, under the SIL Open Font License 1.1
(`LICENSE-Liberation.txt`, `AUTHORS-Liberation.txt`, both verbatim from the upstream release).
Upstream: <https://github.com/liberationfonts/liberation-fonts>.

## Why a binary is in the source tree

D62. A PNG whose glyph widths come from whatever face happens to be installed is reproducible on
one machine and nowhere else — and this repository's CI runs on `ubuntu-latest` while its author's
does not. `SkiaTextMeasurer` therefore takes an optional font *file*, and the test suite states
one, so that every number the M2.5 gates assert is a number any checkout can reproduce.

The library does **not** ship a face. `SkiaOptions.FontFile = None` resolves the theme's family
stack against the machine and reports what it found; bundling a face for every consumer would
cost package size and a licence to audit, and §5 of the M2.5 plan already accepts that a user's
PNG is reproducible on their machine rather than on everyone's. This directory is the test
suite's answer to that, not the library's.

## Why *this* face

Liberation Sans is metric-compatible with Arial, which is metric-compatible with Helvetica, which
is the advance-width table `ApproxTextMeasurer` is built from. Measured on 2026-08-23, at 12 px:

| string   | Liberation Sans, unshaped | Arial, unshaped | Liberation Sans, shaped |
|----------|---------------------------|-----------------|-------------------------|
| `AVATAR` | 48.0117                   | 48.0117         | 44.5078                 |

The two faces agree to the last digit, so gate 2's comparison isolates exactly one variable.
`ApproxTextMeasurer` computes 48.012 for the same string, which means the approximation is *exact*
against the unshaped face and the entire 7.3% difference is kerning — the thing HarfBuzz was added
to see. A face with its own metrics (DejaVu Sans, say) would have conflated kerning with a
different advance table and the gate would have proved less.

`Regular` only. A bold or an italic would double the size of this directory to test a code path —
`SKFontStyleWeight`, `SKFontStyleSlant` — that a stated file deliberately ignores.

## The second face, and why it is a subset

`MareyTestCJK-Regular.otf`, 4.7 KB: five ideographs — 中, 気, 温, 時, 間 — subset out of Noto Sans
CJK JP Regular (Adobe, © 2014–2021), under the SIL Open Font License 1.1
(`LICENSE-NotoSansCJK.txt`, verbatim from the upstream release). The OFL permits modification and
redistribution of a modified face; this one is renamed to **Marey Test CJK** because it is a
subset rather than Noto, and because a distinct family name is load-bearing in the tests.

D78's gate 14 has two halves. "An uncovered codepoint reports `.notdef` rather than measuring
silently" needs one face and Liberation Sans is it. "A covered CJK title names the face that
covered it" needs a face that *has* a CJK codepoint, and neither way of getting one from the
machine is a gate:

- **Resolve against what is installed.** `ubuntu-latest` has no CJK face, so the assertion would
  pass on the author's laptop and skip in CI — the vacuous gate the M3 plan warns about by name.
- **Add `fonts-noto-cjk` to the apt list.** 60 MB of download, on both workflows, for a face whose
  metrics are then whatever apt shipped that month. The numbers in `SkiaTests.fs` would not be
  reproducible from a checkout, which is the whole reason this directory exists.

So: a face in the tree, chosen the way Liberation Sans was chosen — for what it makes provable.

**Why these five characters, and no Latin at all.** The subset covers *no* ASCII, not even the
space. That is deliberate: it makes the coverage boundary sharp in both directions, so a mixed
title splits into segments that can be counted, and a test can tell "the fallback face drew this"
from "the primary face happened to have it". `気温` is a real axis title — temperature — rather
than a specimen character, and `時間` (time) gives a second script change in one string, which is
what makes a pen that walks by the wrong segment's width fail a gate rather than pass it.

Measured on 2026-09-08, at 12 px:

| string  | face             | shaped advance | ascent  | descent |
|---------|------------------|----------------|---------|---------|
| `気温`  | Marey Test CJK   | 24.0000        | 13.9200 | 3.4560  |
| `気温`  | Liberation Sans  | 18.0000        | 10.8633 | 2.5430  |

The second row is two missing-glyph boxes at 9.0 px each. Six pixels of advance and three of
ascent separate "the fallback ran" from "it did not", which is the margin every assertion in
`` module `font fallback` `` rests on.
