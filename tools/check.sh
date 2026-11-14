#!/usr/bin/env bash
# The whole local gate in one command: tests, the single-file exe, and snapshots of the demo board.
# Usage: bash tools/check.sh        Output: dist/Laneboard.exe, dist/snap-*.png. Exit code = the verdict.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet test tests/Laneboard.Tests -c Release --nologo -v q

rm -rf dist
dotnet publish src/Laneboard -c Release -r win-x64 --self-contained true --nologo -v q \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none -o dist

snap() { ./dist/Laneboard.exe --snapshot "dist/snap-$1.png" --demo "${@:2}"; }
snap light-view --theme light --view light
snap dark-view --theme dark --view light
snap advanced --theme dark --view advanced
snap solid --theme light --view advanced --solidity 100
snap settings --theme dark --settings
if ls dist/*.error.txt >/dev/null 2>&1; then cat dist/*.error.txt; exit 1; fi

ls -la dist | grep -E "exe|png"
echo "CHECK PASSED"
