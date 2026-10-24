# NACHO changelog

Versions: MAJOR.MINOR.PATCH. Minor for a feature, patch for a fix.

## 0.4.0 (2026-10-25)

**Asked for:** a Linux version.

**Added: NACHO for Linux**
- A Linux app with the same window, engine and texts as the Windows one: drop, paste, presets, page-fit preview, Copy file, Show in folder, Undo, More options and About; English, Spanish and Hungarian, switched live; light and dark following the desktop; the compact layout below 900 px.
- Built with Avalonia 12.1 on .NET 10. The image engine is shared with Windows unchanged; all 78 engine tests pass on Linux too.
- Two downloads: `NACHO-0.4.0-x86_64.AppImage` (one file: make it executable and run it) and `nacho-0.4.0-linux-x64.tar.gz` with `install.sh` (for your user only, no root: app menu, Open With and the right-click entry) and `uninstall.sh`.
- Right-click entry "Make Docs-ready" for Files (under Scripts), Nemo, Caja and Dolphin, plus Open With > NACHO in every file manager. On and off in More options, like on Windows; it follows the language.
- Drag a tile into a Google Doc (X11 drag and drop, which Avalonia 12.1 added). Copy file offers the copy to file managers (text/uri-list and the GNOME file list). Copy path works with Ctrl+L in the Upload from computer dialog.
- A second launch, for example Open With on more images, hands the files to the open window.
- Linux wording where the Windows texts name Explorer, Win+Shift+S, Send to or the File name box (13 texts in each language).
- Settings in ~/.config/nacho; copies in Pictures/Docs-Ready, also when the Pictures folder has a translated name.
- Colours checked against WCAG 2.2 AA in light and dark: every text pair is at least 4.7:1.

**Changed**
- Windows: no visible change; built from the same 0.4.0 source.
- Builds no longer record the folder they were built in, so no local path ends up in the published files.

**Fixed**
- The source and package zips made by build.ps1 stored paths with backslashes, so on Linux and macOS they unpacked as flat files named like `src\Nacho\App.xaml`. build.ps1 now writes standard zips.

**Tests:** 110 unit tests, on Linux and Windows (32 new: Linux texts, XDG folders, desktop files). Linux self-test: 81 checks on a virtual X display, including the clipboard as another program sees it, the desktop files, and a language switch renaming the Files script. Linux packages, in a throwaway home folder: install.sh in Hungarian, a second launch handing a photo to the open window, uninstall.sh leaving nothing behind, and the AppImage's own self-test (81 checks, menus pointing at the .AppImage file). Windows: 65 self-test checks for the portable and small exes.

**Not checked here:** NACHO on a real Linux desktop (GNOME, KDE, Cinnamon) with its real file manager; dragging into Google Docs in a real browser; Wayland sessions (NACHO runs through XWayland there); the AppImage mounted through FUSE (tested by extracting it); the Windows installer (only its version number changed since 0.3.0).

## 0.3.0 (2026-10-24)

**Asked for:** a Hungarian UI.

**Added**
- Hungarian (Magyar) throughout: main window, tiles, status line, page-fit preview, More options, About, error messages, the right-click entry ("Docs-kész másolat készítése").
- HU button next to EN | ES in the command bar, and Magyar in More options. Switching is live, like the other two.
- NACHO in Hungarian: Normalizál, Arányt tart, Csökkent és Hűen Odaad. About shows the other two expansions.
- Hungarian number format: 4,8 MB, "1,82:1", "97%-kal kisebb".
- Setup in Hungarian (Inno Setup's own Hungarian wizard plus NACHO's task texts). Picking Magyar in setup starts NACHO in Hungarian.
- A Windows display language of Hungarian starts NACHO in Hungarian when there was no setup.

**Tests:** 78 unit tests (15 new: every language has every key, matching placeholders, no long dashes, Hungarian number format). 65 self-test checks for the portable and small exes (8 new: Hungarian title, status line, tile and shape texts, the decimal comma, and screenshots of the main window, More options and About in Hungarian); 66 for the installed copy. Installer round trip on this PC: silent install in Hungarian with the right-click and Send to options, the right-click text "Docs-kész másolat készítése" and the Language=hu setting checked, self-test from the installed copy (starts in Hungarian), silent uninstall, nothing left behind. Upgrade: 0.2.0 installed in Spanish, then 0.3.0 over it in Hungarian: same folder, one Apps entry at 0.3.0, the right-click text switched to Hungarian, then uninstalled cleanly.

**Not checked here:** the Hungarian setup wizard clicked through by hand; the Hungarian wording by a native speaker.

## 0.2.0 (2026-10-23)

**Asked for:** a Windows installer.

**Added**
- `NACHO-0.2.0-setup.exe`, built with Inno Setup 6.7. English or Spanish setup; "for me only" (default, no admin rights, %LOCALAPPDATA%\Programs\NACHO) or "for all users" (Program Files).
- Setup options: the right-click entry "Make Docs-ready" / "Preparar para Docs" (on by default), Send to, a desktop shortcut. Start menu entry and an entry in Settings > Apps.
- Uninstall removes the program, its shortcuts, the right-click entry and Send to, whoever turned them on (setup or More options). Settings and Docs-Ready pictures stay.
- A newer setup installs over an older one (same app ID) and keeps settings and chosen options.
- The installed copy runs from a normal folder rather than a single packed exe, so it starts faster.

**Changed**
- NACHO's first run uses the language picked in setup.
- More options shows the right-click and Send to state as Windows has it, so it agrees with what setup did.

**Tests:** 63 unit tests. 57 self-test checks for the portable and small exes; 58 for the installed copy, which also checks that it starts in the setup language. Installer round trip on this PC: silent install in Spanish with the right-click and Send to options, every file, shortcut and registry entry checked, self-test run from the installed copy, silent uninstall, and a check that nothing was left behind.

**Not checked here:** the setup wizard clicked through by hand, an "all users" install (needs admin), installing over 0.2.0 with a later version.

## 0.1.1 (2026-10-23)

**Asked for:** keep the ratio, because the image will be re-inserted into the document.

**Changed**
- Every size is now worked out from the original's exact shape. Common shapes (4:3, 3:2, 16:9, 16:10 and most camera sizes) come out exact, for example 4032 × 3024 to 1280 × 960 and 6000 × 4000 to 1278 × 852. Other shapes stay within 0.1%; very thin strips within 1%, or keep their full size.
- Over-budget photos: the extra shrinking step now renders again from the full-size picture at the original's shape. Before, it shrank the already shrunk copy by 85% on each side separately, so the short side could drift by up to a pixel on each step.
- The 25-megapixel limit no longer rounds both sides down separately.

**Added**
- Tile line shows the shape: "2250 × 3000 to 960 × 1280 px · shape 3:4 kept · JPEG".
- Page-fit preview has a Shape row ("3:4, exact" or "16:9, within 0.04%") and a note that Replace image in Google Docs fits a same-shape copy into the old frame without stretching.
- Right-click a tile > Copy path, to paste into the File name box of Docs' Replace image > Upload from computer.
- About explains how to replace an image already in a Doc.
- English and Spanish for all of the above.

**Fixed**
- The over-budget step could report the size of a picture it had not saved (it resized once more after the last try).

**Tests:** 63 unit tests, all passing (39 new: 16 awkward sizes at 6 target sizes, exact results for common shapes, a 30 KB budget that forces the extra shrinking step, shape names in English and Spanish). 57 self-test checks, passed by both exes from the release folder on the real window (new: shape of every copy, the Shape row in English and Spanish, the tile line).

**Not checked here:** Replace image in a live Google Doc; whether Google Docs shows phone photos upright (if one of yours shows sideways in the Doc today, the upright copy would not match its frame).

## 0.1.0 (2026-10-23)

**Asked for:** a Windows program that shrinks an image to an acceptable size before it goes into a Google Doc, so the small file is uploaded instead of the big one. English and Spanish UI, switchable. A full name for the NACHO abbreviation.

**Added**
- Name: NACHO = Normalise, Adjust, Compress, Hand Over (Spanish: Normaliza, Ajusta, Comprime y Honra el Original). Shown in the title bar and About.
- Four ways in: drop on the window, Ctrl+O, Ctrl+V (image or copied files), right-click Make Docs-ready and Send to (both opt-in, current user only).
- Three presets (Small 800 px / 150 KB, Balanced 1280 px / 300 KB, Sharp 1920 px / 600 KB). Switching redoes the list.
- Engine on Magick.NET 15.0: turns photos upright, resizes without upscaling, keeps under 25 megapixels, JPEG for photos with a size-budget search, PNG for screenshots (256 colours when that saves 30% or more), transparency and animation kept, HEIC, WebP, AVIF, TIFF, BMP converted.
- Privacy: location data and camera details removed by default, each shown as a badge.
- Never bigger: when the original is already fine for Docs and a copy would not be smaller, the original is kept and the tile says why.
- Hand-over: drag the thumbnail into the Doc, Copy file (a real file on the clipboard, so Chrome cannot turn it back into a big PNG), Copy as picture for comparison, Show in folder.
- Page-fit preview on an A4 or Letter page, with a sharpness note and an original versus copy table.
- Plain-language errors that say what, why and the fix (PDF, not an image, damaged, missing).
- Undo batch deletes only the copies NACHO just made. Originals are never touched.
- EN | ES switch in the command bar: every label, tile, status line and the title change at once.
- Light and dark Fluent theme that follows Windows; classic theme when High Contrast is on.
- Compact layout below 900 px: tile buttons move under the text, preview becomes a toggle.
- Savings counter: megabytes kept out of your Docs this month.
- Self-test mode: NACHO.exe --selftest folder.

**Tests:** 24 unit tests on the engine and the English and Spanish text. 52 self-test checks on the real window, run against both exe flavours.

**Not checked here:**
- A real HEIC photo from a phone (the samples are generated; HEIC reading comes from Magick.NET).
- Dragging or pasting into a live Google Doc in Chrome.
- Registering the right-click menu and Send to on your PC (off until you tick them).
- High Contrast mode and a full Narrator pass.
