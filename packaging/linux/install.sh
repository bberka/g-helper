#!/bin/sh
# Installs G-Helper from the tar.gz release archive into the system (same layout as the .deb / .rpm).
#
#   sudo ./install.sh                  install below /usr
#   DESTDIR=/tmp/stage ./install.sh    only copy the files (no group, udev or systemd actions)
#
# The archive can also be used without installing: root/usr/lib/ghelper/ghelper runs in place (without the udev
# rules and the polkit action most hardware features report "unsupported", see README.md).
set -eu

here=$(cd "$(dirname "$0")" && pwd)
root=$here/root
destdir=${DESTDIR:-}

[ -d "$root" ] || { echo "install.sh: $root not found" >&2; exit 1; }
if [ -z "$destdir" ] && [ "$(id -u)" -ne 0 ]; then
    echo "install.sh: run as root (sudo ./install.sh)" >&2
    exit 1
fi

# directories first, then files and symlinks; modes come from the archive (executable bit => 0755)
(cd "$root" && find . -type d) | while IFS= read -r d; do
    [ -d "$destdir/$d" ] || mkdir -p -m 0755 "$destdir/$d"
done
(cd "$root" && find . ! -type d) | while IFS= read -r f; do
    if [ -L "$root/$f" ]; then
        ln -sfn "$(readlink "$root/$f")" "$destdir/$f"
    elif [ -x "$root/$f" ]; then
        install -m 0755 "$root/$f" "$destdir/$f"
    else
        install -m 0644 "$root/$f" "$destdir/$f"
    fi
done

if [ -n "$destdir" ]; then
    echo "files copied to $destdir"
    exit 0
fi

if command -v systemd-sysusers >/dev/null 2>&1; then
    systemd-sysusers /usr/lib/sysusers.d/ghelper.conf || true
elif ! getent group ghelper >/dev/null 2>&1; then
    groupadd -r ghelper 2>/dev/null || addgroup -S ghelper 2>/dev/null || true
fi
if command -v udevadm >/dev/null 2>&1; then
    udevadm control --reload-rules || true
    udevadm trigger || true
fi
command -v systemctl >/dev/null 2>&1 && systemctl daemon-reload || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true

cat <<'EOF'
G-Helper installed to /usr/lib/ghelper (start it with "ghelper").
Hardware access: log in again, or add yourself to the group:  sudo usermod -aG ghelper $USER
Charge limit at boot (optional):                              sudo systemctl enable ghelper-charge.service
Details: /usr/share/doc/ghelper/README.md
EOF
