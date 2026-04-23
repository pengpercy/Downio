#!/usr/bin/env bash

set -e
set -o
set -u
set pipefail

cd build

# Cleanup binaries
echo "Cleaning up binaries for $RUNTIME..."
# Downio is the directory
find Downio/Assets/Binaries -mindepth 1 -maxdepth 1 -type d -not -name "darwin" -exec rm -rf {} +
if [ "$RUNTIME" == "osx-x64" ]; then
    rm -rf Downio/Assets/Binaries/darwin/arm64
elif [ "$RUNTIME" == "osx-arm64" ]; then
    rm -rf Downio/Assets/Binaries/darwin/x64
fi

mkdir -p Downio.app/Contents/MacOS
mkdir -p Downio.app/Contents/Resources
cp -r Downio/* Downio.app/Contents/MacOS/
rm -rf Downio
ICON_SOURCE="../src/Downio/Assets/Branding/macOS/app_icon.png"
ICONSET_DIR="App.iconset"
rm -rf "$ICONSET_DIR"
mkdir -p "$ICONSET_DIR"
sips -z 16 16     "$ICON_SOURCE" --out "$ICONSET_DIR/icon_16x16.png" >/dev/null
sips -z 32 32     "$ICON_SOURCE" --out "$ICONSET_DIR/icon_16x16@2x.png" >/dev/null
sips -z 32 32     "$ICON_SOURCE" --out "$ICONSET_DIR/icon_32x32.png" >/dev/null
sips -z 64 64     "$ICON_SOURCE" --out "$ICONSET_DIR/icon_32x32@2x.png" >/dev/null
sips -z 128 128   "$ICON_SOURCE" --out "$ICONSET_DIR/icon_128x128.png" >/dev/null
sips -z 256 256   "$ICON_SOURCE" --out "$ICONSET_DIR/icon_128x128@2x.png" >/dev/null
sips -z 256 256   "$ICON_SOURCE" --out "$ICONSET_DIR/icon_256x256.png" >/dev/null
sips -z 512 512   "$ICON_SOURCE" --out "$ICONSET_DIR/icon_256x256@2x.png" >/dev/null
sips -z 512 512   "$ICON_SOURCE" --out "$ICONSET_DIR/icon_512x512.png" >/dev/null
sips -z 1024 1024 "$ICON_SOURCE" --out "$ICONSET_DIR/icon_512x512@2x.png" >/dev/null
iconutil -c icns "$ICONSET_DIR" -o Downio.app/Contents/Resources/App.icns
rm -rf "$ICONSET_DIR"
sed "s/Downio_VERSION/$VERSION/g" resources/app/App.plist > Downio.app/Contents/Info.plist
rm -rf Downio.app/Contents/MacOS/Downio.dsym

if [[ -n "${CODESIGN_IDENTITY:-}" ]]; then
    codesign --force --deep --options runtime --timestamp --sign "$CODESIGN_IDENTITY" Downio.app
fi

zip "Downio_$VERSION.$RUNTIME.zip" -r Downio.app

# Create DMG
DMG_NAME="Downio_$VERSION.$RUNTIME.dmg"
echo "Creating DMG: $DMG_NAME"
rm -f "$DMG_NAME"

# Create a temporary folder for DMG content
DMG_SOURCE="dmg_source"
mkdir -p "$DMG_SOURCE"
cp -r "Downio.app" "$DMG_SOURCE/"
ln -s /Applications "$DMG_SOURCE/Applications"

# Create DMG using hdiutil
hdiutil create -volname "Downio" \
    -srcfolder "$DMG_SOURCE" \
    -ov -format UDZO \
    "$DMG_NAME"

if [[ -n "${CODESIGN_IDENTITY:-}" ]]; then
    codesign --force --timestamp --sign "$CODESIGN_IDENTITY" "$DMG_NAME"
fi

# Cleanup
rm -rf "$DMG_SOURCE"
echo "Done packaging for $RUNTIME. Zip and DMG created."
