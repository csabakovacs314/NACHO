#!/bin/sh
# Installs NACHO for the current user: no root, nothing outside your home folder.
#   ./install.sh [--lang en|es|hu] [--no-context-menu] [--no-app-menu]
# Program:   ~/.local/share/nacho/app      Command: ~/.local/bin/nacho
# Menus:     app menu, Open With, and Make Docs-ready in Files, Nemo, Caja and Dolphin
# Settings (~/.config/nacho) and your Docs-Ready pictures are kept by uninstall.sh.
set -eu

HERE="$(cd "$(dirname "$0")" && pwd)"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
case "$DATA" in /*) ;; *) DATA="$HOME/.local/share" ;; esac
DEST="$DATA/nacho"
BIN="$HOME/.local/bin"
LANG_CHOICE=""
SETUP_ARGS=""

while [ $# -gt 0 ]; do
    case "$1" in
        --lang) LANG_CHOICE="${2:-}"; shift 2 ;;
        --no-context-menu) SETUP_ARGS="$SETUP_ARGS --no-context-menu"; shift ;;
        --no-app-menu) SETUP_ARGS="$SETUP_ARGS --no-app-menu"; shift ;;
        -h|--help) sed -n '2,7p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1 (try --help)" >&2; exit 2 ;;
    esac
done

if [ "$(id -u)" = "0" ] && [ "${NACHO_ALLOW_ROOT:-}" != "1" ]; then
    echo "Run install.sh as yourself, not with sudo: NACHO installs into your own home folder." >&2
    exit 1
fi
if [ "$(uname -m)" != "x86_64" ]; then
    echo "This NACHO is built for 64-bit PCs (x86_64); this machine is $(uname -m)." >&2
    exit 1
fi
if [ ! -x "$HERE/app/nacho" ]; then
    echo "The app folder is missing next to install.sh. Unpack the whole archive first." >&2
    exit 1
fi
if command -v ldconfig >/dev/null 2>&1 && ! ldconfig -p 2>/dev/null | grep -q 'libicuuc\.so'; then
    echo "Note: NACHO needs the ICU library (libicu), which most desktops have."
    echo "      If NACHO does not start: Ubuntu/Debian/Mint: sudo apt install libicu-dev   Fedora/Alma/RHEL: sudo dnf install libicu"
fi

case "$LANG_CHOICE" in
    en|es|hu) ;;
    "") ;;
    *) echo "Language must be en, es or hu." >&2; exit 2 ;;
esac

echo "Installing NACHO into $DEST"
mkdir -p "$DEST" "$BIN"
rm -rf "$DEST/app.new"
cp -R "$HERE/app" "$DEST/app.new"
rm -rf "$DEST/app"
mv "$DEST/app.new" "$DEST/app"
cp "$HERE/uninstall.sh" "$DEST/uninstall.sh"
chmod 755 "$DEST/uninstall.sh" "$DEST/app/nacho"
for f in README.txt CHANGELOG.md; do
    if [ -f "$HERE/$f" ]; then cp "$HERE/$f" "$DEST/$f"; fi
done
ln -sfn "$DEST/app/nacho" "$BIN/nacho"

if [ -n "$LANG_CHOICE" ]; then
    # shellcheck disable=SC2086
    "$DEST/app/nacho" --setup-desktop --lang "$LANG_CHOICE" $SETUP_ARGS
else
    # shellcheck disable=SC2086
    "$DEST/app/nacho" --setup-desktop $SETUP_ARGS
fi

echo
echo "Done. Start NACHO from your app menu, with 'nacho' in a terminal, or right-click an image."
case ":$PATH:" in
    *":$BIN:"*) ;;
    *) echo "Note: $BIN is not on your PATH yet; log out and back in, or use $DEST/app/nacho." ;;
esac
echo "To remove it later: $DEST/uninstall.sh"
