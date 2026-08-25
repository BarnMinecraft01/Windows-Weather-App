#!/usr/bin/env bash
#
# Builds a macOS .app bundle for Windows Weather.
#
#   ./build-app.sh                    build for the host architecture
#   ./build-app.sh --arch osx-x64     build for Intel
#   ./build-app.sh --universal        build one binary that runs on both
#
# The two modes exist because they trade off against each other:
#
#   Per-architecture (default) publishes normally, so every native dependency --
#   Skia, HarfBuzz, the Avalonia native layer -- lands in Contents/MacOS as its
#   own dylib. That is the layout Apple's notarisation tooling expects, because
#   each dylib can be signed in place.
#
#   Universal uses the single-file publish that .NET supports, then merges the
#   two executables with lipo, which is the only way .NET produces a universal
#   binary (it cannot emit one directly). The result is one file that runs
#   everywhere, but the native libraries are extracted at runtime rather than
#   signed in place, which makes notarisation fiddlier.
#
# Both modes ad-hoc sign the result (codesign --sign -). That is enough for the
# app to launch on the machine that built it. Distributing to anyone else needs
# a real Developer ID certificate and notarisation -- see README.md.

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$PROJECT_ROOT/src/WeatherApp.Mac/WeatherApp.Mac.csproj"
PLIST="$PROJECT_ROOT/packaging/macos/Info.plist"
OUT="$PROJECT_ROOT/artifacts/macos"

APP_NAME="Windows Weather"
BINARY_NAME="WindowsWeather"

UNIVERSAL=0
ARCH=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --universal) UNIVERSAL=1; shift ;;
    --arch)      ARCH="$2"; shift 2 ;;
    -h|--help)   sed -n '2,26p' "$0"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "$ARCH" ]]; then
  case "$(uname -m)" in
    arm64) ARCH="osx-arm64" ;;
    x86_64) ARCH="osx-x64" ;;
    *) echo "Unrecognised architecture: $(uname -m)" >&2; exit 1 ;;
  esac
fi

APP="$OUT/$APP_NAME.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

if [[ "$UNIVERSAL" -eq 1 ]]; then
  echo "==> Publishing osx-arm64 and osx-x64 as single files"

  for rid in osx-arm64 osx-x64; do
    dotnet publish "$PROJECT" \
      --configuration Release \
      --runtime "$rid" \
      --self-contained true \
      -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true \
      --output "$OUT/$rid"
  done

  echo "==> Merging with lipo"
  # .NET cannot emit a universal binary; merging the two single-file hosts is
  # the documented way to get one.
  lipo -create -output "$APP/Contents/MacOS/$BINARY_NAME" \
    "$OUT/osx-arm64/$BINARY_NAME" \
    "$OUT/osx-x64/$BINARY_NAME"
else
  echo "==> Publishing $ARCH"
  rm -rf "$OUT/$ARCH"

  dotnet publish "$PROJECT" \
    --configuration Release \
    --runtime "$ARCH" \
    --self-contained true \
    --output "$OUT/$ARCH"

  # Everything the publish produced goes into the bundle: the apphost plus each
  # native dylib beside it, which is the layout codesign wants.
  cp -R "$OUT/$ARCH/." "$APP/Contents/MacOS/"
fi

cp "$PLIST" "$APP/Contents/Info.plist"
chmod +x "$APP/Contents/MacOS/$BINARY_NAME"

# Marks the directory as a bundle for older Finder versions.
printf 'APPL????' > "$APP/Contents/PkgInfo"

echo "==> Ad-hoc signing"
# --deep is deprecated for distribution signing but is the right tool for an
# ad-hoc local signature across the bundled dylibs.
codesign --force --deep --sign - "$APP" 2>/dev/null \
  || echo "    codesign unavailable; the bundle is unsigned."

echo
echo "Built: $APP"
echo
echo "This bundle is ad-hoc signed, so Gatekeeper will refuse it on any machine"
echo "other than the one that built it. To open it there anyway, right-click the"
echo "app and choose Open, or clear the quarantine flag:"
echo "    xattr -dr com.apple.quarantine \"$APP\""
echo
echo "For real distribution, sign with a Developer ID certificate and notarise."
echo "See README.md."
