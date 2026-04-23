Name: Downio
Version: %_version
Release: 1
Summary: Open-source Downloader
License: MIT
URL: https://github.com/percy/Downio
Source: https://github.com/percy/Downio/archive/refs/tags/v%_version.tar.gz
Requires: libX11.so.6()(%{__isa_bits}bit)
Requires: libSM.so.6()(%{__isa_bits}bit)
Requires: libicu
Requires: xdg-utils

%define _build_id_links none

%description
Open-source Downloader

%install
mkdir -p %{buildroot}/opt/Downio
mkdir -p %{buildroot}/%{_bindir}
mkdir -p %{buildroot}/usr/share/applications
mkdir -p %{buildroot}/usr/share/icons
cp -r %{_topdir}/../../Downio/* %{buildroot}/opt/Downio/
ln -rsf %{buildroot}/opt/Downio/Downio %{buildroot}/%{_bindir}
cp -r %{_topdir}/../_common/applications %{buildroot}/%{_datadir}
cp -r %{_topdir}/../_common/icons %{buildroot}/%{_datadir}
chmod 755 -R %{buildroot}/opt/Downio
chmod 755 %{buildroot}/%{_datadir}/applications/Downio.desktop

%files
%dir /opt/Downio/
/opt/Downio/*
/usr/share/applications/Downio.desktop
/usr/share/icons/*
%{_bindir}/Downio

%changelog
# skip
