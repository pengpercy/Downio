#!/bin/bash
set -e

# Usage: ./package_linux.sh <rid> <version> <output_dir> <publish_dir>
# Example: ./package_linux.sh linux-x64 1.0.0 build_output src/Downio/bin/Release/net10.0/linux-x64/publish

RID=$1
VERSION=$2
OUTPUT_DIR=$3
PUBLISH_DIR=$4
APP_NAME="Downio"
BIN_NAME="Downio"
DESC="A modern, open-source download manager."
MAINTAINER="Downio Contributors"
LICENSE="MIT"
URL="https://github.com/pengpercy/Downio"

mkdir -p "$OUTPUT_DIR"

# Determine Architecture
ARCH=""
if [[ "$RID" == "linux-x64" ]]; then
    ARCH="amd64"
    RPM_ARCH="x86_64"
elif [[ "$RID" == "linux-arm64" ]]; then
    ARCH="arm64"
    RPM_ARCH="aarch64"
else
    echo "Unsupported RID: $RID"
    exit 1
fi

echo "Packaging for $RID ($ARCH)..."

# ==============================================================================
# 1. Prepare Directory Structure for FPM (DEB & RPM)
# ==============================================================================
PKG_ROOT="build_temp/$RID/root"
mkdir -p "$PKG_ROOT/usr/bin"
mkdir -p "$PKG_ROOT/usr/share/applications"
mkdir -p "$PKG_ROOT/usr/share/icons/hicolor/128x128/apps"
mkdir -p "$PKG_ROOT/usr/share/$APP_NAME"

# Copy binary and resources
cp -r "$PUBLISH_DIR"/* "$PKG_ROOT/usr/share/$APP_NAME/"
chmod +x "$PKG_ROOT/usr/share/$APP_NAME/$BIN_NAME"

# Create symlink
ln -s "/usr/share/$APP_NAME/$BIN_NAME" "$PKG_ROOT/usr/bin/$APP_NAME"

# Create Desktop Entry
cat > "$PKG_ROOT/usr/share/applications/$APP_NAME.desktop" <<EOF
[Desktop Entry]
Name=$APP_NAME
Comment=$DESC
Exec=$APP_NAME
Icon=$APP_NAME
Type=Application
Categories=Network;FileTransfer;
Terminal=false
StartupNotify=true
EOF

# Copy Icon (Assuming icon exists in source)
# We need to find the icon. It should be in the repo.
ICON_SRC="src/Downio/Assets/app_ico.png"
if [ -f "$ICON_SRC" ]; then
    cp "$ICON_SRC" "$PKG_ROOT/usr/share/icons/hicolor/128x128/apps/$APP_NAME.png"
else
    echo "Warning: Icon not found at $ICON_SRC"
fi

# ==============================================================================
# 2. Build DEB
# ==============================================================================
echo "Building DEB..."
fpm -s dir -t deb \
    -n "$APP_NAME" \
    -v "$VERSION" \
    -a "$ARCH" \
    -m "$MAINTAINER" \
    --url "$URL" \
    --description "$DESC" \
    --license "$LICENSE" \
    -C "$PKG_ROOT" \
    -p "$OUTPUT_DIR/${APP_NAME}_${VERSION}_${ARCH}.deb" \
    .

# ==============================================================================
# 3. Build RPM
# ==============================================================================
echo "Building RPM..."
fpm -s dir -t rpm \
    -n "$APP_NAME" \
    -v "$VERSION" \
    -a "$RPM_ARCH" \
    -m "$MAINTAINER" \
    --url "$URL" \
    --description "$DESC" \
    --license "$LICENSE" \
    --rpm-os linux \
    -C "$PKG_ROOT" \
    -p "$OUTPUT_DIR/${APP_NAME}-${VERSION}-1.${RPM_ARCH}.rpm" \
    .

# ==============================================================================
# 4. Build AppImage (Only for x64 currently to keep it simple and reliable)
# ==============================================================================
# Cross-compiling AppImage is complex. We will only build AppImage for the host architecture if it matches.
# Or if we have tools.
# For now, let's try to build AppImage for x64 only.
if [[ "$RID" == "linux-x64" ]]; then
    echo "Building AppImage..."
    
    # Download linuxdeploy if not present
    if [ ! -f "linuxdeploy-x86_64.AppImage" ]; then
        wget -q https://github.com/linuxdeploy/linuxdeploy/releases/download/continuous/linuxdeploy-x86_64.AppImage
        chmod +x linuxdeploy-x86_64.AppImage
    fi

    APPDIR="build_temp/$RID/AppDir"
    mkdir -p "$APPDIR"
    
    # Run linuxdeploy
    # We need to set environment variables for linuxdeploy to find resources
    # But since we are .NET AOT, we just need the binary and basic structure.
    
    # Use the root we prepared for FPM as base, but move things to AppDir structure
    # AppDir/usr/bin
    # AppDir/usr/share
    cp -r "$PKG_ROOT/usr" "$APPDIR/"
    
    # Desktop file and Icon must be in root of AppDir as well for linuxdeploy to pick them up easily
    cp "$PKG_ROOT/usr/share/applications/$APP_NAME.desktop" "$APPDIR/"
    cp "$PKG_ROOT/usr/share/icons/hicolor/128x128/apps/$APP_NAME.png" "$APPDIR/$APP_NAME.png"

    # Run linuxdeploy
    # --executable points to the binary to be launched
    # --desktop-file points to the desktop entry
    # --icon points to the icon
    
    ./linuxdeploy-x86_64.AppImage \
        --appdir "$APPDIR" \
        --executable "$APPDIR/usr/bin/$APP_NAME" \
        --desktop-file "$APPDIR/$APP_NAME.desktop" \
        --icon-file "$APPDIR/$APP_NAME.png" \
        --output appimage
        
    mv "$APP_NAME"*.AppImage "$OUTPUT_DIR/${APP_NAME}-${VERSION}-${ARCH}.AppImage"
fi

# Clean up temp
rm -rf "build_temp/$RID"
