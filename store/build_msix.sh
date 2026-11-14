#!/usr/bin/env bash
# Builds the Microsoft Store package from the exe tools/check.sh just built and tested (build/app/Laneboard.exe).
#
#   bash store/build_msix.sh
#
# Output: dist/Laneboard-<version>-x64.msix (not signed: the Store signs what it publishes), build/store/layout/,
#         and the Store screenshots in dist/store/.
# Identity: store/identity.env (git-ignored) holds IDENTITY_NAME, IDENTITY_PUBLISHER and PUBLISHER_DISPLAY from
# Partner Center > Product identity. Without it the package gets a local test identity: fine to install with
# Developer Mode on (Add-AppxPackage -Register build/store/layout/AppxManifest.xml), refused by the Store.
set -euo pipefail
cd "$(dirname "$0")/.."
[ -f store/identity.env ] && . store/identity.env
IDENTITY_NAME="${IDENTITY_NAME:-Laneboard.LocalTest}"
IDENTITY_PUBLISHER="${IDENTITY_PUBLISHER:-CN=Laneboard Local Test}"
PUBLISHER_DISPLAY="${PUBLISHER_DISPLAY:-Laneboard}"
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/Laneboard/Laneboard.csproj)
echo "$VERSION" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+$' || { echo "csproj version must be X.Y.Z, got '$VERSION'"; exit 1; }
[ -f build/app/Laneboard.exe ] || { echo "build/app/Laneboard.exe missing: run tools/check.sh"; exit 1; }
w() { cygpath -w "$1" 2>/dev/null || echo "$1"; }

L=build/store/layout
rm -rf build/store; mkdir -p "$L/Laneboard" "$L/Assets" dist/store
cp build/app/Laneboard.exe "$L/Laneboard/"
sed -e "s|@IDENTITY_NAME@|$IDENTITY_NAME|" -e "s|@IDENTITY_PUBLISHER@|$IDENTITY_PUBLISHER|" \
    -e "s|@PUBLISHER_DISPLAY@|$PUBLISHER_DISPLAY|" -e "s|@VERSION@|$VERSION|" store/AppxManifest.xml > "$L/AppxManifest.xml"
! grep -q '@[A-Z_]*@' "$L/AppxManifest.xml"
python -I store/make_images.py src/Laneboard/Assets/laneboard.png "$L/Assets" build/app dist/store

MAKEAPPX="${MAKEAPPX:-$(ls -d "/c/Program Files (x86)/Windows Kits/10/bin"/10.*/x64/makeappx.exe 2>/dev/null | sort -V | tail -n1)}"
[ -x "$MAKEAPPX" ] || { echo "makeappx.exe not found: install the Windows SDK (free), or set MAKEAPPX=<path>"; exit 1; }
MSIX="dist/Laneboard-$VERSION-x64.msix"
rm -f "$MSIX"
MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' "$MAKEAPPX" pack /o /d "$(w "$PWD/$L")" /p "$(w "$PWD/$MSIX")" > build/store/makeappx.log 2>&1 \
  || { tail -n 30 build/store/makeappx.log; exit 1; }
echo "$MSIX $(du -m "$MSIX" | cut -f1) MB ($IDENTITY_NAME)"
