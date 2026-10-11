#!/bin/sh
# Removes a G-Helper installation made with install.sh (the file list is the root/ tree of this archive).
#   sudo ./uninstall.sh
# The "ghelper" group and the user configuration (~/.config/ghelper) are left alone.
set -eu

here=$(cd "$(dirname "$0")" && pwd)
root=$here/root
destdir=${DESTDIR:-}

[ -d "$root" ] || { echo "uninstall.sh: $root not found" >&2; exit 1; }
if [ -z "$destdir" ] && [ "$(id -u)" -ne 0 ]; then
    echo "uninstall.sh: run as root (sudo ./uninstall.sh)" >&2
    exit 1
fi

if [ -z "$destdir" ] && command -v systemctl >/dev/null 2>&1; then
    systemctl disable --now ghelper-charge.service >/dev/null 2>&1 || true
fi

(cd "$root" && find . ! -type d) | while IFS= read -r f; do
    rm -f "$destdir/$f"
done
# only directories that belong to G-Helper alone
rmdir "$destdir/usr/lib/ghelper" "$destdir/usr/share/doc/ghelper" 2>/dev/null || true

if [ -z "$destdir" ]; then
    command -v udevadm >/dev/null 2>&1 && udevadm control --reload-rules || true
    command -v systemctl >/dev/null 2>&1 && systemctl daemon-reload || true
fi
echo "G-Helper removed"
