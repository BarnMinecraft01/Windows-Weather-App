#!/usr/bin/env bash
#
# Builds Linux packages for Windows Weather.
#
#   ./build-linux.sh                      build for the host architecture
#   ./build-linux.sh --arch linux-arm64   build for 64-bit ARM
#
# Always produces a self-contained tarball. Additionally produces an AppImage
# when appimagetool can be obtained, because that is the format that runs on
# any distribution without a package manager or root.
#
# The tarball is the guaranteed output and the AppImage is best-effort: the
# tool needs FUSE, which is frequently unavailable in containers and CI, and a
# missing AppImage should not fail a build that otherwise succeeded.

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$PROJECT_ROOT/src/WeatherApp.Desktop/WeatherApp.Desktop.csproj"
ICON_SCRIPT="$PROJECT_ROOT/packaging/icon/make-icon.py"
OUT="$PROJECT_ROOT/artifacts/linux"

BINARY_NAME="WindowsWeather"
APP_ID="windows-weather"
DISPLAY_NAME="Windows Weather"

ARCH=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --arch) ARCH="$2"; shift 2 ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "$ARCH" ]]; then
  case "$(uname -m)" in
    x86_64)  ARCH="linux-x64" ;;
    aarch64) ARCH="linux-arm64" ;;
    *) echo "Unrecognised architecture: $(uname -m)" >&2; exit 1 ;;
  esac
fi

echo "==> Publishing $ARCH"
rm -rf "$OUT/$ARCH" "$OUT/AppDir"

dotnet publish "$PROJECT" \
  --configuration Release \
  --runtime "$ARCH" \
  --self-contained true \
  --output "$OUT/$ARCH"

# ---- AppDir -----------------------------------------------------------------
APPDIR="$OUT/AppDir"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/icons/hicolor/512x512/apps"

cp -R "$OUT/$ARCH/." "$APPDIR/usr/bin/"
chmod +x "$APPDIR/usr/bin/$BINARY_NAME"

echo "==> Generating icon"
python3 "$ICON_SCRIPT" "$APPDIR/$APP_ID.png"
cp "$APPDIR/$APP_ID.png" "$APPDIR/usr/share/icons/hicolor/512x512/apps/$APP_ID.png"

cat > "$APPDIR/$APP_ID.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=$DISPLAY_NAME
Comment=Jet stream, radar, precipitation and NWS alerts
Exec=$BINARY_NAME
Icon=$APP_ID
Categories=Utility;Science;
Terminal=false
StartupNotify=true
DESKTOP
cp "$APPDIR/$APP_ID.desktop" "$APPDIR/usr/share/applications/"

# AppRun resolves its own location so the binary can find the runtime beside it.
cat > "$APPDIR/AppRun" <<'APPRUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/WindowsWeather" "$@"
APPRUN
chmod +x "$APPDIR/AppRun"

# ---- tarball (always) -------------------------------------------------------
TARBALL="$OUT/$DISPLAY_NAME-$ARCH.tar.gz"
echo "==> Building tarball"
tar -czf "$TARBALL" -C "$OUT" "$ARCH"
echo "    $TARBALL"

# ---- AppImage (best effort) -------------------------------------------------
echo "==> Building AppImage"
TOOL="$OUT/appimagetool"

# appimagetool names architectures differently from .NET runtime identifiers,
# and it reads the target from an ARCH environment variable. Keep that in its
# own name so the .NET RID in $ARCH stays intact.
case "$ARCH" in
  linux-x64)   TOOL_ARCH="x86_64" ;;
  linux-arm64) TOOL_ARCH="aarch64" ;;
  *)           TOOL_ARCH="" ;;
esac

if [[ -z "$TOOL_ARCH" ]]; then
  echo "    no appimagetool build for $ARCH; skipping AppImage."
elif [[ ! -x "$TOOL" ]]; then
  TOOL_URL="https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-$TOOL_ARCH.AppImage"
  if curl -fsSL -o "$TOOL" "$TOOL_URL"; then
    chmod +x "$TOOL"
  else
    echo "    could not download appimagetool; skipping AppImage."
  fi
fi

if [[ -x "$TOOL" ]]; then
  # --appimage-extract-and-run avoids needing FUSE, which containers rarely have.
  if ARCH="$TOOL_ARCH" "$TOOL" --appimage-extract-and-run "$APPDIR" "$OUT/$DISPLAY_NAME.AppImage"; then
    echo "    $OUT/$DISPLAY_NAME.AppImage"
  else
    echo "    appimagetool failed; the tarball above is still usable."
  fi
fi

echo
echo "Done. Outputs in $OUT"
