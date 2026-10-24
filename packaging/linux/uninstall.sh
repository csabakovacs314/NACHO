#!/bin/sh
# Removes NACHO installed by install.sh: the program, the nacho command, the app menu entry,
# Open With and the file manager menus. Keeps your settings (~/.config/nacho) and Docs-Ready pictures.
set -eu

DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
case "$DATA" in /*) ;; *) DATA="$HOME/.local/share" ;; esac
DEST="$DATA/nacho"
BIN="$HOME/.local/bin"

if [ "$(id -u)" = "0" ] && [ "${NACHO_ALLOW_ROOT:-}" != "1" ]; then
    echo "Run uninstall.sh as yourself, not with sudo." >&2
    exit 1
fi
if [ ! -d "$DEST/app" ]; then
    echo "NACHO is not installed in $DEST."
    exit 0
fi

"$DEST/app/nacho" --remove-desktop || true
if [ -L "$BIN/nacho" ] && [ "$(readlink "$BIN/nacho")" = "$DEST/app/nacho" ]; then
    rm -f "$BIN/nacho"
fi
rm -rf "$DEST/app" "$DEST/README.txt" "$DEST/CHANGELOG.md"
rm -f "$DEST/uninstall.sh"
rmdir "$DEST" 2>/dev/null || true
echo "NACHO removed. Your settings (~/.config/nacho) and Docs-Ready pictures are still there."
