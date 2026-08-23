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
