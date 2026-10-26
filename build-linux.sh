#!/usr/bin/env bash
# Builds NACHO for Linux into out/linux-release:
#   NACHO-<version>-x86_64.AppImage     one file, runs on most 64-bit distros
#   nacho-<version>-linux-x64.tar.gz    the same program with install.sh and uninstall.sh
# Steps: unit tests, publish, self-test on a virtual display, packages, then install/uninstall,
# second-launch and AppImage checks in a throwaway home folder.
# Needs: .NET 10 SDK, xvfb-run, curl (the first run downloads appimagetool into out/tools).
set -euo pipefail

VERSION="${1:-0.5.0}"
ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="$ROOT/out/linux"
REL="$ROOT/out/linux-release"
TOOLS="$ROOT/out/tools"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 AVALONIA_TELEMETRY_OPTOUT=1
XVFB=(xvfb-run -a -s "-screen 0 1920x1080x24")

rm -rf "$OUT" "$REL"
mkdir -p "$OUT" "$REL" "$TOOLS"

echo "1/6 Unit tests"
dotnet test "$ROOT/tests/Nacho.Tests/Nacho.Tests.csproj" -c Release --nologo

echo "2/6 Publish linux-x64, self-contained"
dotnet publish "$ROOT/src/Nacho.Linux/Nacho.Linux.csproj" -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=false -o "$OUT/app" --nologo
find "$OUT/app" -name '*.pdb' -delete
if grep -rqa "$ROOT" "$OUT/app"; then
    echo "A build path leaked into the published files:" >&2
    grep -rla "$ROOT" "$OUT/app" >&2
    exit 1
fi

echo "3/6 Self-test on a virtual display"
mkdir -p "$OUT/home-selftest"
if ! env -u XDG_CONFIG_HOME -u XDG_DATA_HOME HOME="$OUT/home-selftest" LANG=en_IE.UTF-8 \
        "${XVFB[@]}" "$OUT/app/nacho" --selftest "$OUT/selftest" > "$OUT/selftest.log" 2>&1; then
    tail -40 "$OUT/selftest.log"
    exit 1
fi
grep -c '^PASS' "$OUT/selftest/selftest-report.txt" | sed 's/^/   checks passed: /'

echo "4/6 tar.gz with install.sh"
PKG="nacho-$VERSION-linux-x64"
mkdir -p "$OUT/$PKG"
cp -R "$OUT/app" "$OUT/$PKG/app"
cp "$ROOT/packaging/linux/install.sh" "$ROOT/packaging/linux/uninstall.sh" "$ROOT/README.txt" "$ROOT/CHANGELOG.md" "$OUT/$PKG/"
chmod 755 "$OUT/$PKG/install.sh" "$OUT/$PKG/uninstall.sh" "$OUT/$PKG/app/nacho"
tar -C "$OUT" --owner=0 --group=0 --numeric-owner -czf "$REL/$PKG.tar.gz" "$PKG"

echo "5/6 AppImage"
APPDIR="$OUT/NACHO.AppDir"
mkdir -p "$APPDIR/usr/lib" "$APPDIR/usr/share/applications" \
    "$APPDIR/usr/share/icons/hicolor/256x256/apps" "$APPDIR/usr/share/icons/hicolor/scalable/apps"
cp -R "$OUT/app" "$APPDIR/usr/lib/nacho"
install -m 755 "$ROOT/packaging/linux/AppRun" "$APPDIR/AppRun"
sed "s/@VERSION@/$VERSION/" "$ROOT/packaging/linux/nacho.desktop" > "$APPDIR/nacho.desktop"
cp "$APPDIR/nacho.desktop" "$APPDIR/usr/share/applications/nacho.desktop"
cp "$ROOT/src/Nacho.Linux/Assets/nacho.png" "$APPDIR/nacho.png"
cp "$ROOT/src/Nacho.Linux/Assets/nacho.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/nacho.png"
cp "$ROOT/src/Nacho.Linux/Assets/nacho.svg" "$APPDIR/usr/share/icons/hicolor/scalable/apps/nacho.svg"
mkdir -p "$APPDIR/usr/share/doc/nacho"
cp "$ROOT/README.txt" "$ROOT/CHANGELOG.md" "$ROOT/tools/oxipng/LICENSE" "$APPDIR/usr/share/doc/nacho/"
mv "$APPDIR/usr/share/doc/nacho/LICENSE" "$APPDIR/usr/share/doc/nacho/oxipng-LICENSE"
ln -sf nacho.png "$APPDIR/.DirIcon"
if [ ! -s "$TOOLS/appimagetool-x86_64.AppImage" ]; then
    curl -fsSL -o "$TOOLS/appimagetool-x86_64.AppImage" \
        https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
    curl -fsSL -o "$TOOLS/runtime-x86_64" \
        https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-x86_64
    chmod 755 "$TOOLS/appimagetool-x86_64.AppImage"
fi
sha256sum "$TOOLS/appimagetool-x86_64.AppImage" "$TOOLS/runtime-x86_64" | sed 's/^/   tool /'
APPIMAGE_FILE="$REL/NACHO-$VERSION-x86_64.AppImage"
ARCH=x86_64 "$TOOLS/appimagetool-x86_64.AppImage" --appimage-extract-and-run --no-appstream \
    --runtime-file "$TOOLS/runtime-x86_64" "$APPDIR" "$APPIMAGE_FILE" > "$OUT/appimagetool.log" 2>&1 \
    || { tail -30 "$OUT/appimagetool.log"; exit 1; }
chmod 755 "$APPIMAGE_FILE"

echo "6/6 Install, second launch and AppImage checks (throwaway home folder)"
T="$OUT/home-checks"
rm -rf "$T"
mkdir -p "$T/run" "$T/unpack"
chmod 700 "$T/run"
fail() { echo "   FAIL $1" >&2; exit 1; }
pass() { echo "   PASS $1"; }
check() { if eval "$1"; then pass "$2"; else fail "$2"; fi; }
tar -C "$T/unpack" -xzf "$REL/$PKG.tar.gz"
H=(env -u XDG_CONFIG_HOME -u XDG_DATA_HOME HOME="$T" XDG_RUNTIME_DIR="$T/run" LANG=hu_HU.UTF-8 NACHO_ALLOW_ROOT=1)

"${H[@]}" sh "$T/unpack/$PKG/install.sh" --lang hu > "$T/install.log" 2>&1 || { cat "$T/install.log"; fail "install.sh"; }
[ -x "$T/.local/share/nacho/app/nacho" ] && pass "program installed" || fail "program installed"
[ "$(readlink "$T/.local/bin/nacho")" = "$T/.local/share/nacho/app/nacho" ] && pass "nacho command linked" || fail "nacho command linked"
grep -q "Exec=\"$T/.local/share/nacho/app/nacho\" %F" "$T/.local/share/applications/nacho.desktop" && pass "app menu entry points at the program" || fail "app menu entry"
desktop-file-validate "$T/.local/share/applications/nacho.desktop" > /dev/null 2>&1 && pass "app menu entry is valid" || echo "   INFO desktop-file-validate not available or warned"
[ -x "$T/.local/share/nautilus/scripts/Docs-kész másolat készítése" ] && pass "Files script in Hungarian" || fail "Files script in Hungarian"
[ -x "$T/.local/share/kio/servicemenus/nacho.desktop" ] && pass "Dolphin menu, executable" || fail "Dolphin menu"
[ -f "$T/.local/share/nemo/actions/nacho.nemo_action" ] && pass "Nemo action" || fail "Nemo action"
grep -q '"Language": "hu"' "$T/.config/nacho/settings.json" && pass "first run language: Hungarian" || fail "first run language"
"${H[@]}" "$T/.local/bin/nacho" --version | grep -q "NACHO $VERSION" && pass "nacho --version says $VERSION" || fail "version"

# A second launch hands its files to the running window and exits.
cp "$OUT/selftest/samples/IMG_4821.jpg" "$T/photo.jpg"
"${H[@]}" "${XVFB[@]}" sh -c '
    "$HOME/.local/bin/nacho" & first=$!
    sleep 6
    start=$(date +%s)
    "$HOME/.local/bin/nacho" "$HOME/photo.jpg"; code=$?
    took=$(( $(date +%s) - start ))
    for i in $(seq 1 30); do [ -n "$(ls "$HOME/Pictures/Docs-Ready" 2>/dev/null)" ] && break; sleep 1; done
    kill $first 2>/dev/null; wait $first 2>/dev/null
    echo "second=$code took=$took"
' > "$T/second.log" 2>&1 || true
grep -q "second=0" "$T/second.log" && pass "second launch exits at once ($(grep -o 'took=[0-9]*' "$T/second.log")s)" || { cat "$T/second.log"; fail "second launch"; }
ls "$T/Pictures/Docs-Ready/"*photo-docs* > /dev/null 2>&1 && pass "the running window made photo-docs from the second launch" || fail "copy from second launch"

"${H[@]}" sh "$T/.local/share/nacho/uninstall.sh" > "$T/uninstall.log" 2>&1 || { cat "$T/uninstall.log"; fail "uninstall.sh"; }
# mimeinfo.cache is the desktop's own index of the applications folder; it stays, but must not name NACHO.
left=$(find "$T/.local" -type f ! -name mimeinfo.cache 2>/dev/null | wc -l)
[ "$left" = "0" ] && pass "uninstall leaves no NACHO file in ~/.local" || { find "$T/.local" -type f; fail "uninstall left $left files"; }
if grep -qs nacho "$T/.local/share/applications/mimeinfo.cache"; then fail "the desktop's Open With index still names NACHO"; else pass "the desktop's Open With index no longer names NACHO"; fi
[ -f "$T/.config/nacho/settings.json" ] && pass "uninstall keeps the settings" || fail "settings kept"
[ ! -e "$T/.cache/nacho" ] && pass "uninstall removes the unpacked oxipng" || { find "$T/.cache/nacho"; fail "oxipng copy left"; }

# The AppImage runs, and its menus point at the AppImage file itself.
"${H[@]}" "$APPIMAGE_FILE" --appimage-extract-and-run --version | grep -q "NACHO $VERSION" && pass "AppImage starts" || fail "AppImage starts"
"${H[@]}" "${XVFB[@]}" "$APPIMAGE_FILE" --appimage-extract-and-run --selftest "$T/appimage-selftest" > "$T/appimage-selftest.log" 2>&1 \
    && pass "AppImage self-test: $(grep -c '^PASS' "$T/appimage-selftest/selftest-report.txt") checks" || { tail -20 "$T/appimage-selftest.log"; fail "AppImage self-test"; }
grep -q "menus point at: $APPIMAGE_FILE" "$T/appimage-selftest/selftest-report.txt" && pass "AppImage menus point at the .AppImage file" || fail "AppImage menu target"

(cd "$REL" && sha256sum ./*.AppImage ./*.tar.gz > SHA256SUMS.txt)
ls -l "$REL"
echo "Done: $REL"
