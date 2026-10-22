#!/bin/sh
# Builds the documentation site into OUT (site/ by default):
#
#     tools/site/build.sh [OUT]
#
# 1. The guide's session and the readmes' run as the test suite runs them, and write each
#    figure's scene for a canvas (MAREY_SITE_SCENES). A broken example fails here.
# 2. tools/site/build.fsx turns the Markdown into pages around those scenes.
# 3. fsdocs writes the API reference of every package into OUT/reference.
set -eu
root=$(cd "$(dirname "$0")/../.." && pwd)
out=$(mkdir -p "${1:-$root/site}" && cd "${1:-$root/site}" && pwd)
scenes="$out.scenes"

cd "$root"
rm -rf "$scenes"
MAREY_SITE_SCENES="$scenes" dotnet test tests/Marey.Tests -c Release --nologo -v q \
    --filter "FullyQualifiedName~GuideTests"
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
rm -rf "$scenes"
echo "site: $out"
