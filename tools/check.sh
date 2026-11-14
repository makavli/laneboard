#!/usr/bin/env bash
# The whole local gate in one command: tests, the single-file exe, snapshots of the demo board.
# Usage: bash tools/check.sh        Exit code = the verdict.
# The exe is built in build/app/ (a running copy in dist/ never blocks the build) and copied to dist/ when free.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet test tests/Laneboard.Tests -c Release --nologo -v q

rm -rf build/app
dotnet publish src/Laneboard -c Release -r win-x64 --self-contained true --nologo -v q \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ${COMPRESS:+-p:EnableCompressionInSingleFile=true} \
  -p:DebugType=none -o build/app

snap() { ./build/app/Laneboard.exe --snapshot "build/app/snap-$1.png" --demo "${@:2}"; }
snap light-view --theme light --view light
snap dark-view --theme dark --view light
snap advanced --theme dark --view advanced
snap solid --theme light --view advanced --solidity 100
snap settings --theme dark --settings
if ls build/app/*.error.txt >/dev/null 2>&1; then cat build/app/*.error.txt; exit 1; fi

mkdir -p dist
if cp build/app/Laneboard.exe dist/Laneboard.exe 2>/dev/null; then
  echo "dist/Laneboard.exe updated"
else
  echo "NOTE: dist/Laneboard.exe is in use (Laneboard running from dist?): the new exe is build/app/Laneboard.exe"
fi
ls -la build/app | grep -E "exe|png"
echo "CHECK PASSED"
