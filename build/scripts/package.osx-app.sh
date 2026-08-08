#!/usr/bin/env bash

set -e
set -o
set -u
set pipefail

cd build

if [[ ! -d "Downio.app" ]]; then
    echo "Missing SDK-generated Downio.app for $RUNTIME."
    exit 1
fi

mkdir -p Downio.app/Contents/Resources
ARIA2_PATH="$(find Downio.app -type f -name aria2c -print -quit)"
if [[ -z "$ARIA2_PATH" ]]; then
    echo "Missing aria2c in the macOS app bundle for $RUNTIME."
    exit 1
fi
chmod +x "$ARIA2_PATH"

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
BUNDLE_SHORT_VERSION="${VERSION%%-*}"
BUNDLE_BUILD_VERSION="${GITHUB_RUN_NUMBER:-$BUNDLE_SHORT_VERSION}"
PLIST="Downio.app/Contents/Info.plist"
set_plist_string() {
    local key="$1"
    local value="$2"
    /usr/libexec/PlistBuddy -c "Set :$key $value" "$PLIST" 2>/dev/null || \
        /usr/libexec/PlistBuddy -c "Add :$key string $value" "$PLIST"
}
set_plist_string CFBundleIconFile App
set_plist_string CFBundleIdentifier com.Downio.app
set_plist_string CFBundleName Downio
set_plist_string CFBundleDisplayName Downio
set_plist_string CFBundleVersion "$BUNDLE_BUILD_VERSION"
set_plist_string CFBundleShortVersionString "$BUNDLE_SHORT_VERSION"
set_plist_string LSMinimumSystemVersion 12.0
/usr/libexec/PlistBuddy -c "Delete :CFBundleLocalizations" "$PLIST" 2>/dev/null || true
/usr/libexec/PlistBuddy -c "Add :CFBundleLocalizations array" "$PLIST"
/usr/libexec/PlistBuddy -c "Add :CFBundleLocalizations:0 string en" "$PLIST"
/usr/libexec/PlistBuddy -c "Add :CFBundleLocalizations:1 string zh-Hans" "$PLIST"
find resources/app -maxdepth 1 -type d -name "*.lproj" -exec cp -R {} Downio.app/Contents/Resources/ \;
find Downio.app -type d -name "*.dSYM" -prune -exec rm -rf {} +

if [[ -n "${CODESIGN_IDENTITY:-}" ]]; then
    codesign --force --deep --options runtime --timestamp --sign "$CODESIGN_IDENTITY" Downio.app
fi

zip "Downio_$VERSION.$RUNTIME.zip" -r Downio.app

# Create DMG
DMG_NAME="Downio_$VERSION.$RUNTIME.dmg"
echo "Creating DMG: $DMG_NAME"
rm -f "$DMG_NAME"

# Create a temporary folder for DMG content
DMG_SOURCE="$(mktemp -d "${TMPDIR:-/tmp}/downio-dmg-source.XXXXXX")"
cleanup() {
    rm -rf "$DMG_SOURCE"
}
trap cleanup EXIT

cp -R "Downio.app" "$DMG_SOURCE/"
ln -s /Applications "$DMG_SOURCE/Applications"

create_dmg() {
    hdiutil create -volname "Downio" \
        -srcfolder "$DMG_SOURCE" \
        -ov -format UDZO \
        "$DMG_NAME"
}

attempt=1
max_attempts=3
until create_dmg; do
    if [ "$attempt" -ge "$max_attempts" ]; then
        echo "Failed to create DMG after $attempt attempts."
        exit 1
    fi

    echo "hdiutil create failed, retrying in 5 seconds ($attempt/$max_attempts)..."
    sleep 5
    attempt=$((attempt + 1))
done

if [[ -n "${CODESIGN_IDENTITY:-}" ]]; then
    codesign --force --timestamp --sign "$CODESIGN_IDENTITY" "$DMG_NAME"
fi

echo "Done packaging for $RUNTIME. Zip and DMG created."
