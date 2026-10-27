#!/usr/bin/env bash
# Marey in a released Verso, end to end: the automated half headless, then the browser host for
# the half only a person can check (CHECKLIST.md).
#
#   tools/verso-e2e/verso-e2e.sh                  # packs this checkout, serves the notebook
#   tools/verso-e2e/verso-e2e.sh --no-serve       # the headless checks only
#   tools/verso-e2e/verso-e2e.sh --version 1.0.0  # the packages on nuget.org instead of a pack
#
# A local pack gets the version 0.99.<MMDDHHmm>. It is a release version, because Verso's
# Extensions panel lists no prerelease, and one that will never be published, so neither
# ~/.nuget/packages nor ~/.verso/extensions ever holds a build under a real version's name.
# Everything else lives in the work folder, which is printed at the start.
set -euo pipefail

verso_version=1.2.3
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"

version=""
serve=1
port=5050
work=""

while [ $# -gt 0 ]; do
  case "$1" in
    --version) version="$2"; shift 2 ;;
    --no-serve) serve=0; shift ;;
    --port) port="$2"; shift 2 ;;
    --work) work="$2"; shift 2 ;;
    -h|--help) sed -n 2,12p "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

for tool in dotnet jq curl; do
  command -v "$tool" >/dev/null || { echo "needs $tool on PATH" >&2; exit 2; }
done

published=0
[ -n "$version" ] && published=1
[ -n "$version" ] || version="0.99.$(date +%m%d%H%M)"
[ -n "$work" ] || work="${TMPDIR:-/tmp}/marey-verso-e2e-$version"
mkdir -p "$work"
work="$(cd "$work" && pwd)"
feed="$work/feed"
results="$work/results"
mkdir -p "$results"

echo "Marey $version in Verso $verso_version"
echo "work folder: $work"
echo

# ---- The packages ----------------------------------------------------------------------------

if [ $published = 0 ]; then
  echo "== packing $(git -C "$repo" rev-parse --short HEAD)$(git -C "$repo" diff --quiet || echo ' (with uncommitted changes)') as $version"
  dotnet pack "$repo/Marey.slnx" -c Release -o "$feed" -p:Version="$version" -v quiet -nologo >"$work/pack.log" 2>&1 \
    || { tail -30 "$work/pack.log"; echo "the pack failed; $work/pack.log has all of it" >&2; exit 1; }
  ls "$feed"/*.nupkg | wc -l | xargs echo "  packages:"
  cat >"$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="marey-e2e" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
else
  echo "== Marey $version from nuget.org"
  cat >"$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
  curl -fsSL -o /dev/null "https://api.nuget.org/v3-flatcontainer/marey.verso/$version/marey.verso.$version.nupkg" \
    || { echo "Marey.Verso $version is not on nuget.org" >&2; exit 1; }
fi


# ---- Verso, from its release ----------------------------------------------------------------

echo "== Verso.Cli $verso_version from nuget.org"
if [ ! -x "$work/verso/verso" ]; then
  dotnet tool install Verso.Cli --version "$verso_version" --tool-path "$work/verso" \
    --configfile "$work/nuget.config" >"$work/verso-install.log" 2>&1 \
    || { cat "$work/verso-install.log"; exit 1; }
fi
verso="$work/verso/verso"

for nb in e2e setup-extension-first setup-reference-first; do
  sed "s/@VERSION@/$version/g" "$here/$nb.dib" >"$work/$nb.dib"
done
cp "$here/CHECKLIST.md" "$work/"

# ---- Headless -------------------------------------------------------------------------------

failures=0
pass() { printf '  \033[32mpass\033[0m  %s\n' "$1"; }
fail() { printf '  \033[31mFAIL\033[0m  %s\n' "$1"; failures=$((failures + 1)); }
check() { # check <description> <json> <jq filter>
  if jq -e "$3" "$2" >/dev/null 2>&1; then pass "$1"; else fail "$1"; fi
}

# Each run gets a fresh TMPDIR: Verso restores `#r "nuget: …"` into a cache under it, and a
# second run at the same version would otherwise draw from the first.
run() { # run <notebook> [extra args]
  local nb="$1"; shift
  rm -rf "$work/tmp-$nb"; mkdir -p "$work/tmp-$nb"
  (cd "$work" && TMPDIR="$work/tmp-$nb" "$verso" run "$nb.dib" --output json \
     --output-file "$results/$nb.json" --timeout 900 "$@" >"$results/$nb.log" 2>&1) || true
  [ -s "$results/$nb.json" ] || { fail "$nb: verso run wrote no result (see $results/$nb.log)"; return 1; }
}

# The code cells in order, markdown left out, so the checks below can index them.
code='[.cells[] | select(.language == "fsharp")]'
html='(.mimeType == "text/html")'
widget='(.mimeType == "text/x-verso-widget")'
drawn='(.content | contains("<g id=\"figure\""))'

echo "== headless: the notebook"
if run e2e; then
  j="$results/e2e.json"
  check "the extension and the library load"                   "$j" "$code[0].status == \"Success\""
  check "a figure is drawn as SVG"                                 "$j" "$code[1].outputs | any($html and $drawn)"
  check "a film is a flipbook that animates"                       "$j" "$code[2].outputs | any($html and (.content | contains(\"<animate\")))"
  check "10^4 points are a canvas page"                            "$j" "$code[3].outputs | any($widget and (.content | contains(\"<canvas\")))"
  check "a live figure has its controls (static with no channel)"  "$j" "$code[4].outputs | any(($widget or $html) and (.content | test(\"amplitude\")))"
  check "a 10^5-point live figure is a canvas page"                "$j" "$code[5].outputs | any($widget and (.content | contains(\"<canvas\")))"
  check "a unit mismatch fails the cell"                           "$j" "$code[6].status == \"Failed\""
  check "  with F#'s unit-of-measure message"                      "$j" "$code[6].outputs | any(.content | test(\"unit of measure\"; \"i\"))"
  check "every other cell succeeds"                                "$j" "$code[0:6] | all(.status == \"Success\")"
fi

echo "== headless: the guide's one-cell setup, #!extension then #r"
if run setup-extension-first; then
  j="$results/setup-extension-first.json"
  check "the setup cell succeeds"              "$j" "$code[0].status == \"Success\""
  check "the extension draws the next figure"  "$j" "$code[1].outputs | any($html and $drawn)"
fi

# Reported, not checked: Verso 1.2.3 reads magic commands only at the top of a cell, so a
# `#!extension` below a `#r` reaches F#, which takes `#!` for a comment. The cell succeeds and the
# extension never loads; the next figure is F#'s field table.
echo "== headless: the two lines swapped, #r then #!extension"
if run setup-reference-first; then
  j="$results/setup-reference-first.json"
  if jq -e "$code[1].outputs | any($html and $drawn)" "$j" >/dev/null; then
    echo "  seen: the order does not matter, the extension loaded"
  else
    echo "  seen: the order matters, the extension did not load (setup cell: $(jq -r "$code[0].status" "$j"))"
  fi
fi

echo
if [ $failures -gt 0 ]; then
  echo "$failures headless check(s) failed. Per cell, what came back:"
  echo "  jq '.cells[] | {language, status, outputs: [.outputs[] | {mimeType, content: .content[:300]}]}' $results/<notebook>.json"
else
  echo "every headless check passed"
fi

# ---- In the browser host, by hand ------------------------------------------------------------

leftovers() {
  [ $published = 1 ] && return
  echo "afterwards, what this run left outside $work:"
  echo "  rm -rf ~/.nuget/packages/marey*/$version ~/.verso/extensions/marey.verso/$version"
}

if [ $serve = 1 ]; then
  # The browser host's notebook is a .verso file, since a .dib keeps no outputs to reopen.
  (cd "$work" && "$verso" convert e2e.dib --to verso --output e2e.verso >/dev/null)
  echo
  leftovers
  echo "== by hand: CHECKLIST.md, in the browser host"
  echo "  the notebook: $work/e2e.verso"
  echo "  the checklist: $work/CHECKLIST.md"
  echo "  Ctrl-C stops the host; '$verso serve e2e.verso --port $port' from $work starts it again"
  echo
  # Started in the work folder, so its nuget.config is the one Verso's package sources come from.
  cd "$work"
  mkdir -p "$work/tmp-serve"
  TMPDIR="$work/tmp-serve" exec "$verso" serve e2e.verso --port "$port"
fi

echo
leftovers
exit $((failures > 0))
