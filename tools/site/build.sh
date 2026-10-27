#!/bin/sh
# Builds the documentation site into OUT (site/ by default):
#
#     tools/site/build.sh [OUT [SCENES]]
#
# 1. The guide's session and the readmes' run as the test suite runs them, and write each
#    figure's scene for a canvas (MAREY_SITE_SCENES). A broken example fails here. Given
#    SCENES, the scenes a test run with MAREY_SITE_SCENES=SCENES already wrote, as CI's does,
#    this build reads those and does not run the sessions a second time.
# 2. tools/site/build.fsx turns the Markdown into pages around those scenes.
# 3. fsdocs writes the API reference of every package into OUT/reference.
set -eu
root=$(cd "$(dirname "$0")/../.." && pwd)
out=$(mkdir -p "${1:-$root/site}" && cd "${1:-$root/site}" && pwd)
given=${2:+$(cd "$2" && pwd)}

cd "$root"
if [ -n "$given" ]; then
    scenes=$given
    # A run that was not asked for them writes none, and the pages would quietly lose their canvases.
    ls "$scenes"/*.scene >/dev/null 2>&1 || { echo "no scenes in $scenes" >&2; exit 1; }
else
    scenes="$out.scenes"
    rm -rf "$scenes"
    MAREY_SITE_SCENES="$scenes" dotnet test tests/Marey.Tests -c Release --nologo -v q \
        --filter "FullyQualifiedName~GuideTests"
fi
dotnet fsi tools/site/build.fsx "$scenes" "$out"

dotnet tool restore >/dev/null
rm -rf "$out/reference" "$root/.fsdocs"
projects=$(ls src/*/*.fsproj | grep -v -e Marey.Html.View -e '/Marey/Marey.fsproj' | tr '\n' ' ')
# shellcheck disable=SC2086
dotnet fsdocs build --projects $projects --input tools/site/reference --output "$out/reference" \
    --mdcomments --properties Configuration=Release \
    --parameters root "../" fsdocs-collection-name Marey \
        fsdocs-logo-src "../assets/icon.png" fsdocs-logo-link "../index.html" \
        fsdocs-repository-link https://github.com/Axym-Labs/marey \
        fsdocs-license-link https://github.com/Axym-Labs/marey/blob/main/LICENSE \
        fsdocs-release-notes-link https://github.com/Axym-Labs/marey/blob/main/CHANGELOG.md
[ -n "$given" ] || rm -rf "$scenes"

# 4. Every link the README and the package readmes make into the published site names a page
#    this build wrote, and a heading on it when the link has a fragment. The site is published
#    from a release tag, so a readme that links ahead of it fails here rather than on nuget.org.
site=https://axym-labs.github.io/marey/
grep -ohE "${site}[^) \"'>]*" README.md NUGET.md src/*/PackageReadme.md | sort -u | while read -r url; do
    path=${url#"$site"}
    fragment=
    case $path in *'#'*) fragment=${path#*#}; path=${path%%#*} ;; esac
    case $path in '' | */) path="${path}index.html" ;; esac
    [ -f "$out/$path" ] || { echo "no page for $url" >&2; exit 1; }
    [ -z "$fragment" ] || grep -q "id=\"$fragment\"" "$out/$path" || { echo "no heading for $url" >&2; exit 1; }
done
echo "site: $out"
