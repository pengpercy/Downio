#!/usr/bin/env bash

set -e
set -o
set -u
set pipefail

cd build

if [[ ! -f "Downio/aria2c" ]]; then
    echo "Missing aria2c in the macOS publish root for $RUNTIME."
    exit 1
fi
chmod +x Downio/aria2c

case "$RUNTIME" in
    osx-arm64) arch=arm64;;
    osx-x64) arch=x86_64;;
    *) echo "Unknown runtime $RUNTIME"; exit 1;;
esac

mkdir -p Downio.app/Contents/MacOS
mkdir -p Downio.app/Contents/Resources
cp -R Downio/ Downio.app/Contents/MacOS/
rm -rf Downio
# Read-only data (CA bundle, ED2K lists, licenses) belongs in Contents/Resources, not next to the binary.
if [[ -d Downio.app/Contents/MacOS/resources ]]; then
    cp -R Downio.app/Contents/MacOS/resources/ Downio.app/Contents/Resources/
    rm -rf Downio.app/Contents/MacOS/resources
fi
rm -rf Downio.app/Contents/MacOS/*.dSYM Downio.app/Contents/MacOS/*.dsym
# SkiaSharp, HarfBuzz and Avalonia ship universal dylibs; keep only the slice this package is for.
for lib in Downio.app/Contents/MacOS/*.dylib; do
    if [[ "$(lipo -archs "$lib")" == *" "* ]]; then
        lipo -thin "$arch" "$lib" -output "$lib.thin" && mv "$lib.thin" "$lib"
    fi
done
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
find resources/app -maxdepth 1 -type d -name "*.lproj" -exec cp -R {} Downio.app/Contents/Resources/ \;

# Sign inside-out: every nested Mach-O first (deepest paths first), then the bundle, which seals the
# main executable and resources. Apple deprecated --deep for signing: it applies one set of options
# to everything and skips code it does not recognise. Without a Developer ID the bundle still gets an
# ad-hoc seal, so its resources are covered and the thinned dylibs carry a signature.
sign_args=(--force --sign "${CODESIGN_IDENTITY:--}")
if [[ -n "${CODESIGN_IDENTITY:-}" ]]; then
    sign_args+=(--options runtime --timestamp)
fi
while IFS= read -r file; do
    if [[ "$file" != "Downio.app/Contents/MacOS/Downio" ]] && file -b "$file" | grep -q '^Mach-O'; then
        codesign "${sign_args[@]}" "$file"
    fi
done < <(find Downio.app/Contents -type f | awk '{ print gsub("/", "/"), $0 }' | sort -rn | cut -d' ' -f2-)
codesign "${sign_args[@]}" Downio.app
codesign --verify --deep --strict Downio.app

# ditto keeps the bundle's symlinks, extended attributes and resource forks, which zip -r flattens.
ditto -c -k --sequesterRsrc --keepParent Downio.app "Downio_$VERSION.$RUNTIME.zip"

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

# ULMO (LZMA) is about a third smaller than UDZO (zlib) for this app and needs macOS 10.15+,
# below what .NET 10 itself requires.
create_dmg() {
    hdiutil create -volname "Downio" \
        -srcfolder "$DMG_SOURCE" \
        -ov -format ULMO \
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
