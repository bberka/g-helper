#!/bin/sh
# Builds the Linux distribution packages of G-Helper from a `dotnet publish` output directory.
#
#   packaging/linux/build-packages.sh --rid linux-x64 [--version 0.286.0] [--publish-dir DIR] [--out DIR]
#                                     [--formats tar,deb,rpm]
#
#   1. dotnet publish src/GHelper.App/GHelper.App.csproj -r linux-x64 -c Release -o artifacts/publish/linux-x64
#   2. packaging/linux/build-packages.sh --rid linux-x64
#
# Output (default artifacts/dist):
#   ghelper-<version>-<rid>.tar.gz         always; contains root/ (the FHS tree), install.sh, uninstall.sh
#   ghelper_<version>_<debarch>.deb        glibc RIDs only; built with ar + tar, no dpkg-deb needed
#   ghelper-<version>-1.<rpmarch>.rpm      glibc RIDs only; needs rpmbuild (skipped with a notice when missing)
#
# Run it on Linux: the staging tree contains a symlink (/usr/bin/ghelper) and file modes, neither of which survive
# a Windows file system. Requirements: sh, tar (GNU), gzip, install, find, sed, od, wc.
set -eu
umask 022

rid=
version=
publish_dir=
out_dir=
formats=tar,deb,rpm

die() { echo "build-packages: $*" >&2; exit 1; }

while [ $# -gt 0 ]; do
    case "$1" in
        --rid) rid=$2; shift 2 ;;
        --version) version=$2; shift 2 ;;
        --publish-dir) publish_dir=$2; shift 2 ;;
        --out) out_dir=$2; shift 2 ;;
        --formats) formats=$2; shift 2 ;;
        -h|--help) sed -n '2,17p' "$0"; exit 0 ;;
        *) die "unknown argument: $1" ;;
    esac
done

script_dir=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$script_dir/../.." && pwd)

[ -n "$rid" ] || die "--rid is required (linux-x64, linux-arm64, linux-arm, linux-musl-x64, linux-musl-arm64)"
[ -n "$version" ] || version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$repo/Directory.Build.props" | head -n 1)
[ -n "$version" ] || die "cannot determine the version, pass --version"
version=${version#v}
[ -n "$publish_dir" ] || publish_dir=$repo/artifacts/publish/$rid
[ -n "$out_dir" ] || out_dir=$repo/artifacts/dist

case "$rid" in
    linux-x64)   deb_arch=amd64; rpm_arch=x86_64 ;;
    linux-arm64) deb_arch=arm64; rpm_arch=aarch64 ;;
    linux-arm)   deb_arch=armhf; rpm_arch=armv7hl ;;
    linux-musl-x64|linux-musl-arm64) deb_arch=; rpm_arch= ;;
    *) die "unsupported rid: $rid" ;;
esac

[ -f "$publish_dir/GHelper" ] || die "$publish_dir/GHelper not found: run dotnet publish -r $rid first"
magic=$(head -c 4 "$publish_dir/GHelper" | od -An -c | tr -d ' ')
[ "$magic" = "177ELF" ] || die "$publish_dir/GHelper is not an ELF binary (published for the wrong RID?)"

# prerelease versions: 0.286.0-rc1 -> deb 0.286.0~rc1, rpm Version 0.286.0 Release 0.rc1
base_version=${version%%-*}
if [ "$base_version" = "$version" ]; then
    deb_version=$version
    rpm_release=1
else
    pre=${version#*-}
    deb_version=$base_version~$pre
    rpm_release=0.$(echo "$pre" | tr -c 'A-Za-z0-9.\n' '.')
fi

# reproducible timestamps: SOURCE_DATE_EPOCH, else the last commit, else now
sde=${SOURCE_DATE_EPOCH:-}
[ -n "$sde" ] || sde=$(git -C "$repo" log -1 --format=%ct 2>/dev/null || true)
[ -n "$sde" ] || sde=$(date +%s)

work=$(mktemp -d "${TMPDIR:-/tmp}/ghelper-pkg.XXXXXX")
trap 'rm -rf "$work"' EXIT INT TERM
mkdir -p "$out_dir"
out_dir=$(cd "$out_dir" && pwd)

# ---------------------------------------------------------------------------------------------------------------
# staging: the FHS tree shared by every format
# ---------------------------------------------------------------------------------------------------------------
stage=$work/root
lib=$stage/usr/lib/ghelper
install -d -m 0755 "$stage/usr/bin" "$lib" "$stage/usr/lib/udev/rules.d" "$stage/usr/lib/sysusers.d" \
    "$stage/usr/lib/systemd/system" "$stage/usr/lib/systemd/user" "$stage/usr/share/applications" \
    "$stage/usr/share/polkit-1/actions" "$stage/usr/share/icons/hicolor/256x256/apps" \
    "$stage/usr/share/doc/ghelper"

for f in "$publish_dir"/*; do
    base=$(basename "$f")
    case "$base" in
        GHelper) install -m 0755 "$f" "$lib/ghelper" ;;
        *.pdb) ;;
        *) [ -f "$f" ] && install -m 0644 "$f" "$lib/$base" ;;
    esac
done

install -m 0755 "$script_dir/ghelper-root-launcher" "$lib/ghelper-root-launcher"
install -m 0755 "$script_dir/ghelper-udev-perms" "$lib/ghelper-udev-perms"
install -m 0644 "$script_dir/99-ghelper.rules" "$stage/usr/lib/udev/rules.d/99-ghelper.rules"
install -m 0644 "$script_dir/ghelper.sysusers.conf" "$stage/usr/lib/sysusers.d/ghelper.conf"
install -m 0644 "$script_dir/ghelper-charge.service" "$stage/usr/lib/systemd/system/ghelper-charge.service"
install -m 0644 "$script_dir/ghelper.service" "$stage/usr/lib/systemd/user/ghelper.service"
install -m 0644 "$script_dir/ghelper.desktop" "$stage/usr/share/applications/ghelper.desktop"
install -m 0644 "$script_dir/org.ghelper.policy" "$stage/usr/share/polkit-1/actions/org.ghelper.policy"
install -m 0644 "$script_dir/ghelper.png" "$stage/usr/share/icons/hicolor/256x256/apps/ghelper.png"
install -m 0644 "$script_dir/README.md" "$stage/usr/share/doc/ghelper/README.md"
install -m 0644 "$repo/LICENSE" "$stage/usr/share/doc/ghelper/copyright"

ln -s ../lib/ghelper/ghelper "$stage/usr/bin/ghelper"
if [ ! -L "$stage/usr/bin/ghelper" ] && [ "${ALLOW_NO_SYMLINKS:-}" != 1 ]; then
    die "this file system cannot create symlinks; build the packages on Linux (ALLOW_NO_SYMLINKS=1 for a layout test only)"
fi

# deterministic archive: sorted, root owned, fixed mtime
mktar() { # <directory> <entry>... ; writes an uncompressed tar to stdout
    dir=$1; shift
    tar --sort=name --owner=0 --group=0 --numeric-owner --mtime="@$sde" -C "$dir" -cf - "$@"
}

has_format() { case ",$formats," in *",$1,"*) return 0 ;; esac; return 1; }

# ---------------------------------------------------------------------------------------------------------------
# tar.gz
# ---------------------------------------------------------------------------------------------------------------
tar_name=ghelper-$version-$rid
# the rpm build needs the archive too, so it is always created
tardir=$work/tar/$tar_name
mkdir -p "$tardir"
cp -R -P "$stage" "$tardir/root"
install -m 0755 "$script_dir/install.sh" "$tardir/install.sh"
install -m 0755 "$script_dir/uninstall.sh" "$tardir/uninstall.sh"
install -m 0644 "$script_dir/README.md" "$tardir/README.md"
install -m 0644 "$repo/LICENSE" "$tardir/LICENSE"
mktar "$work/tar" "$tar_name" | gzip -9n > "$out_dir/$tar_name.tar.gz"
echo "built $out_dir/$tar_name.tar.gz"

# ---------------------------------------------------------------------------------------------------------------
# deb
# ---------------------------------------------------------------------------------------------------------------
ar_member() { # <member name> <file>
    size=$(wc -c < "$2" | tr -d ' ')
    printf '%-16s%-12s%-6s%-6s%-8s%-10s`\n' "$1" "$sde" 0 0 100644 "$size"
    cat "$2"
    [ $((size % 2)) -eq 0 ] || printf '\n'
}

build_deb() {
    ctl=$work/deb-control
    mkdir -p "$ctl"
    size_kb=$(du -sk "$stage" | cut -f1)
    sed -e "s/@VERSION@/$deb_version/" -e "s/@ARCH@/$deb_arch/" -e "s/@SIZE@/$size_kb/" \
        "$script_dir/deb/control" > "$ctl/control"
    for s in postinst prerm postrm; do
        install -m 0755 "$script_dir/deb/$s" "$ctl/$s"
    done
    (cd "$stage" && find usr -type f -exec md5sum {} +) | sed 's/^\([0-9a-f]\{32\}\) [ *]/\1  /' > "$ctl/md5sums"
    chmod 0644 "$ctl/control" "$ctl/md5sums"

    mkdir -p "$work/deb"
    (cd "$ctl" && mktar "$ctl" ./control ./md5sums ./postinst ./prerm ./postrm) | gzip -9n > "$work/deb/control.tar.gz"
    mktar "$stage" . | gzip -9n > "$work/deb/data.tar.gz"
    printf '2.0\n' > "$work/deb/debian-binary"

    deb=$out_dir/ghelper_${deb_version}_$deb_arch.deb
    {
        printf '!<arch>\n'
        ar_member debian-binary "$work/deb/debian-binary"
        ar_member control.tar.gz "$work/deb/control.tar.gz"
        ar_member data.tar.gz "$work/deb/data.tar.gz"
    } > "$deb"
    echo "built $deb"
}

# ---------------------------------------------------------------------------------------------------------------
# rpm
# ---------------------------------------------------------------------------------------------------------------
build_rpm() {
    command -v rpmbuild >/dev/null 2>&1 || { echo "rpmbuild not found, skipping the rpm (apt install rpm / dnf install rpm-build)"; return 0; }
    top=$work/rpm
    mkdir -p "$top/SOURCES" "$top/BUILD" "$top/RPMS" "$top/SPECS" "$top/BUILDROOT"
    cp "$out_dir/$tar_name.tar.gz" "$top/SOURCES/"
    rpmbuild -bb "$script_dir/rpm/ghelper.spec" --target "$rpm_arch" \
        --define "_topdir $top" \
        --define "ghelper_version $base_version" \
        --define "ghelper_release $rpm_release" \
        --define "ghelper_tarball $tar_name" \
        --define "_binary_payload w9.gzdio"
    find "$top/RPMS" -name '*.rpm' -exec cp {} "$out_dir/" \;
    echo "built rpm(s) in $out_dir"
}

if has_format deb; then
    if [ -n "$deb_arch" ]; then build_deb; else echo "no deb for $rid (musl is not a Debian target)"; fi
fi
if has_format rpm; then
    if [ -n "$rpm_arch" ]; then build_rpm; else echo "no rpm for $rid (musl is not an RPM target)"; fi
fi
