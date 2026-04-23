#!/bin/bash
set -e

# Usage: ./package_osx.sh <runtime_id> <version> <output_dir>
RID=$1
VERSION=$2
OUTPUT_DIR=$3
APP_NAME="Downio"
PUBLISH_DIR="src/Downio/bin/Release/net10.0/$RID/publish"

if [ -z "$RID" ] || [ -z "$VERSION" ] || [ -z "$OUTPUT_DIR" ]; then
    echo "Usage: ./package_osx.sh <runtime_id> <version> <output_dir>"
    exit 1
fi

echo "Packaging for $RID version $VERSION..."

# Ensure output dir exists
mkdir -p "$OUTPUT_DIR"

# Define App Bundle paths
APP_BUNDLE="$OUTPUT_DIR/$APP_NAME.app"
CONTENTS="$APP_BUNDLE/Contents"
MACOS="$CONTENTS/MacOS"
RESOURCES="$CONTENTS/Resources"

# Clean previous build
rm -rf "$APP_BUNDLE"

# Create directory structure
mkdir -p "$MACOS"
mkdir -p "$RESOURCES"

# Copy published files
echo "Copying files from $PUBLISH_DIR..."
cp -a "$PUBLISH_DIR/"* "$MACOS/"

# Clean up unwanted binaries copied from publish dir (if they exist there)
# The publish dir contains Assets/Binaries for ALL platforms because they are Content items in .csproj
# We want to remove the Assets/Binaries folder entirely from the App Bundle root first
# and then only copy the specific one we need.
rm -rf "$MACOS/Assets/Binaries"

# Copy platform-specific binaries
echo "Copying platform-specific binaries..."
ENGINE_DIR="$MACOS/Assets/Binaries"
mkdir -p "$ENGINE_DIR"

# Only copy the specific binary for this RID and place it in the correct structure
# The app expects it at Assets/Binaries/darwin/{arch}/aria2c
if [ "$RID" == "osx-x64" ]; then
    mkdir -p "$ENGINE_DIR/darwin/x64"
    cp "src/Downio/Assets/Binaries/darwin/x64/aria2c" "$ENGINE_DIR/darwin/x64/"
    chmod +x "$ENGINE_DIR/darwin/x64/aria2c"
elif [ "$RID" == "osx-arm64" ]; then
    mkdir -p "$ENGINE_DIR/darwin/arm64"
    cp "src/Downio/Assets/Binaries/darwin/arm64/aria2c" "$ENGINE_DIR/darwin/arm64/"
    chmod +x "$ENGINE_DIR/darwin/arm64/aria2c"
fi

# Create Info.plist
echo "Creating Info.plist..."
sed "s/Downio_VERSION/$VERSION/g" build/resources/app/App.plist > "$CONTENTS/Info.plist"

# Generate .icns from PNG if available
echo "Generating App.icns..."
ICON_SOURCE="src/Downio/Assets/Branding/macOS/app_icon.png"

if [ -f "$ICON_SOURCE" ]; then
    ICONSET_DIR="build/App.iconset"
    mkdir -p "$ICONSET_DIR"

    # Resize to standard icon sizes
    sips -z 16 16     "$ICON_SOURCE" --out "$ICONSET_DIR/icon_16x16.png"
    sips -z 32 32     "$ICON_SOURCE" --out "$ICONSET_DIR/icon_16x16@2x.png"
    sips -z 32 32     "$ICON_SOURCE" --out "$ICONSET_DIR/icon_32x32.png"
    sips -z 64 64     "$ICON_SOURCE" --out "$ICONSET_DIR/icon_32x32@2x.png"
    sips -z 128 128   "$ICON_SOURCE" --out "$ICONSET_DIR/icon_128x128.png"
    sips -z 256 256   "$ICON_SOURCE" --out "$ICONSET_DIR/icon_128x128@2x.png"
    sips -z 256 256   "$ICON_SOURCE" --out "$ICONSET_DIR/icon_256x256.png"
    sips -z 512 512   "$ICON_SOURCE" --out "$ICONSET_DIR/icon_256x256@2x.png"
    sips -z 512 512   "$ICON_SOURCE" --out "$ICONSET_DIR/icon_512x512.png"
    sips -z 1024 1024 "$ICON_SOURCE" --out "$ICONSET_DIR/icon_512x512@2x.png"

    # Convert iconset to icns
    iconutil -c icns "$ICONSET_DIR" -o "$RESOURCES/App.icns"
    
    # Cleanup
    rm -rf "$ICONSET_DIR"
fi

# Remove .pdb files to save space
find "$MACOS" -name "*.pdb" -delete

# Remove .dSYM files if present (redundant safety check)
find "$MACOS" -name "*.dSYM" -exec rm -rf {} +

# Create DMG
DMG_NAME="${APP_NAME}_${VERSION}_${RID}.dmg"
DMG_PATH="$OUTPUT_DIR/$DMG_NAME"

echo "Creating DMG: $DMG_PATH"
rm -f "$DMG_PATH"

hdiutil create -volname "$APP_NAME" \
    -srcfolder "$APP_BUNDLE" \
    -ov -format UDZO \
    "$DMG_PATH"

echo "Done packaging for $RID. DMG created at $DMG_PATH"
