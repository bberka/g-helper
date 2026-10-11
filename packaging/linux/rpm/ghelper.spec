# RPM spec for G-Helper. It repackages the tar.gz built by build-packages.sh (self-contained .NET single file),
# so it needs no .NET SDK. Build through packaging/linux/build-packages.sh, or by hand:
#
#   mkdir -p rpmbuild/SOURCES && cp ghelper-0.286.0-linux-x64.tar.gz rpmbuild/SOURCES/
#   rpmbuild -bb packaging/linux/rpm/ghelper.spec --target x86_64 \
#     --define "_topdir $PWD/rpmbuild" --define "ghelper_version 0.286.0" --define "ghelper_release 1" \
#     --define "ghelper_tarball ghelper-0.286.0-linux-x64"

# The program is a bundle appended to the apphost: stripping or rewriting it corrupts it.
%global debug_package %{nil}
%global __strip /bin/true
%global __os_install_post %{nil}
%global _build_id_links none
AutoReqProv: no

Name:           ghelper
Version:        %{ghelper_version}
Release:        %{ghelper_release}%{?dist}
Summary:        Control tool for ASUS laptops (performance modes, fans, GPU mode, battery limit, lighting)
License:        GPL-3.0-only
URL:            https://github.com/bberka/g-helper
Source0:        %{ghelper_tarball}.tar.gz

Requires:       glibc
Requires:       libstdc++
Requires:       zlib
Requires:       fontconfig
Requires:       libX11
Requires:       libICE
Requires:       libSM
Recommends:     polkit
Recommends:     libicu
Recommends:     xdg-utils
Recommends:     power-profiles-daemon

%description
Cross-platform fork of G-Helper: performance modes, fan curves, power limits, GPU mode, battery charge limit,
keyboard lighting and ASUS peripherals through the asus-wmi kernel driver. Self-contained: ships its own .NET runtime.

Installs udev rules and a polkit action; members of the "ghelper" group (and the user logged in at the seat) can
access the hardware without root.

%prep
%setup -q -n %{ghelper_tarball}

%build

%install
mkdir -p %{buildroot}
cp -a root/. %{buildroot}/

%files
%license /usr/share/doc/ghelper/copyright
%doc /usr/share/doc/ghelper/README.md
/usr/bin/ghelper
%dir /usr/lib/ghelper
/usr/lib/ghelper/*
/usr/lib/udev/rules.d/99-ghelper.rules
/usr/lib/sysusers.d/ghelper.conf
/usr/lib/systemd/system/ghelper-charge.service
/usr/lib/systemd/user/ghelper.service
/usr/share/applications/ghelper.desktop
/usr/share/polkit-1/actions/org.ghelper.policy
/usr/share/icons/hicolor/256x256/apps/ghelper.png
%dir /usr/share/doc/ghelper

%post
if [ -x /usr/bin/systemd-sysusers ]; then
    /usr/bin/systemd-sysusers /usr/lib/sysusers.d/ghelper.conf || :
elif ! getent group ghelper >/dev/null 2>&1; then
    groupadd -r ghelper 2>/dev/null || :
fi
if [ -x /usr/bin/udevadm ]; then
    /usr/bin/udevadm control --reload-rules || :
    /usr/bin/udevadm trigger || :
fi
if [ -x /usr/bin/systemctl ]; then
    /usr/bin/systemctl daemon-reload || :
fi
/usr/bin/gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor 2>/dev/null || :

%preun
# $1 == 0: final removal, not an upgrade
if [ "$1" -eq 0 ] && [ -x /usr/bin/systemctl ]; then
    /usr/bin/systemctl disable --now ghelper-charge.service >/dev/null 2>&1 || :
fi

%postun
if [ -x /usr/bin/udevadm ]; then
    /usr/bin/udevadm control --reload-rules || :
fi
if [ -x /usr/bin/systemctl ]; then
    /usr/bin/systemctl daemon-reload || :
fi

%changelog
* Sat Oct 10 2026 G-Helper contributors <ghelper@users.noreply.github.com> - %{ghelper_version}-%{ghelper_release}
- Packaged from the cross-platform fork, see the release notes on GitHub
