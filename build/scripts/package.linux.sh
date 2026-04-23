#!/usr/bin/env bash

set -e
set -o
set -u
set pipefail

arch=
appimage_arch=
target=
case "$RUNTIME" in
    linux-x64)
        arch=amd64
        appimage_arch=x86_64
        target=x86_64;;
    linux-arm64)
        arch=arm64
        appimage_arch=aarch64 # AppImage usually uses aarch64
        target=aarch64;;
    *)
        echo "Unknown runtime $RUNTIME"
        exit 1;;
esac

APPIMAGETOOL_URL=https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage

cd build

ICON_SOURCE="../src/Downio/Assets/Branding/app_icon.png"
ICON_OUT="resources/_common/icons/Downio.png"

if [[ -f "$ICON_SOURCE" ]]; then
    if command -v ffmpeg >/dev/null 2>&1; then
        echo "Generating Linux icon from source: $ICON_SOURCE"
        ffmpeg -y -hide_banner -loglevel error -i "$ICON_SOURCE" -vf "scale=256:256:flags=lanczos" -frames:v 1 "$ICON_OUT"
    else
        echo "Warning: ffmpeg not found; using existing icon: $ICON_OUT"
    fi
else
    echo "Warning: icon source not found: $ICON_SOURCE (using existing icon: $ICON_OUT)"
fi

if [[ ! -f "appimagetool" ]]; then
    curl -o appimagetool -L "$APPIMAGETOOL_URL"
    chmod +x appimagetool
fi

# Cleanup unwanted binaries from publish output (Downio)
echo "Cleaning up binaries for $RUNTIME..."
find Downio/Assets/Binaries -mindepth 1 -maxdepth 1 -type d -not -name "linux" -exec rm -rf {} +
if [ "$arch" == "amd64" ]; then
    rm -rf Downio/Assets/Binaries/linux/arm64
elif [ "$arch" == "arm64" ]; then
    rm -rf Downio/Assets/Binaries/linux/x64
fi

rm -f Downio/*.dbg

mkdir -p Downio.AppDir/opt
mkdir -p Downio.AppDir/usr/share/metainfo
mkdir -p Downio.AppDir/usr/share/applications

cp -r Downio Downio.AppDir/opt/Downio
desktop-file-install resources/_common/applications/Downio.desktop --dir Downio.AppDir/usr/share/applications \
    --set-icon com.Downio.app --set-key=Exec --set-value=AppRun
mv Downio.AppDir/usr/share/applications/{Downio,com.Downio.app}.desktop
cp resources/_common/icons/Downio.png Downio.AppDir/com.Downio.app.png
ln -rsf Downio.AppDir/opt/Downio/Downio Downio.AppDir/AppRun
ln -rsf Downio.AppDir/usr/share/applications/com.Downio.app.desktop Downio.AppDir
cp resources/appimage/Downio.appdata.xml Downio.AppDir/usr/share/metainfo/com.Downio.app.appdata.xml

ARCH="$appimage_arch" ./appimagetool -v Downio.AppDir "Downio-$VERSION.linux.$appimage_arch.AppImage"

mkdir -p resources/deb/opt/Downio/
mkdir -p resources/deb/usr/bin
mkdir -p resources/deb/usr/share/applications
mkdir -p resources/deb/usr/share/icons
cp -a Downio/. resources/deb/opt/Downio/
ln -rsf resources/deb/opt/Downio/Downio resources/deb/usr/bin
cp -r resources/_common/applications resources/deb/usr/share
cp -r resources/_common/icons resources/deb/usr/share
# Calculate installed size in KB
installed_size=$(du -sk resources/deb | cut -f1)
# Update the control file
sed -i -e "s/^Version:.*/Version: $VERSION/" \
    -e "s/^Architecture:.*/Architecture: $arch/" \
    -e "s/^Installed-Size:.*/Installed-Size: $installed_size/" \
    resources/deb/DEBIAN/control
# Build deb package with gzip compression
dpkg-deb -Zgzip --root-owner-group --build resources/deb "Downio_$VERSION-1_$arch.deb"

rpmbuild -bb --target="$target" resources/rpm/SPECS/build.spec --define "_topdir $(pwd)/resources/rpm" --define "_version $VERSION"
mv "resources/rpm/RPMS/$target/Downio-$VERSION-1.$target.rpm" ./
