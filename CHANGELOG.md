# NACHO changelog

Versions: MAJOR.MINOR.PATCH. Minor for a feature, patch for a fix.

## 0.11.0 (2026-10-27)

**Asked for:** one design everywhere, not only in the Google Doc window (mockup first, approved as shown, both apps, all windows and Compare, English, Spanish and Hungarian).

**Changed: one look in every window** (Nielsen H1 to H10, ISO 9241-110, WCAG 2.2 AA, Microsoft guidance for commands and dialogs)
- Shared building blocks for Windows and Linux: cards, chips, segmented choices, numbered step dots, link buttons and the same bottom bar. On Windows they live in Theme.xaml (with a High Contrast fallback for every colour); on Linux in App.axaml.
- Main window, start: a header with the logo; a green line saying your originals are never changed and where the copies go (Open the folder); two equal cards side by side. Pictures and files: a large drop area, Add files and Paste, and the three presets as cards with their size (Small 800 px · 150 KB…). A whole Google Doc: the three steps, what NACHO can see, the last Doc you shrank (with Open) and Choose a Google Doc. Narrow windows stack the cards.
- Main window, results: the commands move into the header (Add files, Paste, Google Doc, the presets as one segmented control, Undo batch). Above the list: the count, chips for smaller, kept and needs attention, and the total before and after. A small drop strip stays. Each result is a card: the old size struck through, the new size large and green, a % smaller chip, details as chips, Copy file, and Show in folder and Compare as links. The selected card gets the accent border; a file that needs attention gets an amber border and a warning icon.
- More options: a section list on the left that follows your scrolling and jumps to a section; Google Docs comes first and now has "Where the smaller pictures go, by default" (a new copy, or the Doc itself). The bottom bar is Reset to defaults, then Save options (main action), then Cancel.
- About: the version in its own strip that turns green, blue or amber after Check for updates; Shrink a whole Google Doc first with the accent border; the four ways in with numbered dots; shortcuts as key caps; a link to the privacy policy.
- Compare: segmented zoom and right-side choices in a header, and Close in the bottom bar.

**Tests:** both self-tests check that the start screen shows while the list is empty and gives way to the results, and save new screenshots: main-en-home, main-en-home-dark, main-en-home-narrow and about-en.

## 0.10.1 (2026-10-27)

**Reported:** pressing Google Doc started checking the Doc used last time instead of offering the picker.

**Fixed**
- The Google Doc button, Ctrl+G and "Shrink a whole Google Doc…" now always open at step 1 (Choose). Before, a Docs link still on the clipboard (for example from Copy link) was checked straight away. Pasting a Docs link with Ctrl+V or dropping one on the window still checks that Doc at once, because that is an explicit choice.
- "Shrink another Doc" empties the link box, so the next run starts at the picker.
- Windows: the READ ONLY strip shows a shield icon (it showed a leaf-like glyph).

**Tests:** both self-tests open the window with a Docs link on the clipboard and check that it stays at Choose and reads nothing.

## 0.10.0 (2026-10-27)

**Asked for:** emphasise the Google Docs feature in the documentation; work on a copy of the original Doc and leave the original intact (with an option to change the original, chosen after discussing the trade-offs); a better window by the visibility standards, mockup first; a logo.

**Added: a new copy by default**
- Step 3 asks where the smaller pictures go. **Into a new copy** (recommended, the default): Google makes "<title> (smaller)" in the same Drive folder, NACHO replaces the pictures there, and the original is only read. **Into this Doc**: the 0.8.0 behaviour, same link, sharing and comments. The choice is remembered.
- The copy is made with the Drive API (files.copy). A live test on the 251-picture book: the copy took 4 s and kept all 251 tab and picture IDs; NACHO still matches pictures by order and size if Google ever changes the IDs, and leaves a picture alone if it can't be matched.
- A full Drive or a Doc whose owner turned off copying gets its own message, and nothing changes. A view-only Doc now works: choose Into a new copy. If the Doc itself refuses the first pictures, NACHO goes back to step 3 instead of ending.
- Stop is safe at any time; the copy request itself is never cut half way, so no unseen copy is left behind.

**Changed: the Google Doc window, five visible steps** (Nielsen H1, ISO 9241-110 self-descriptiveness, WCAG 2.2 AA)
- A step rail on top: Choose, Check, Decide, Replace, Done, with ticks for finished steps (shape, not only colour).
- A green READ ONLY line while choosing and checking. While replacing, both Docs are on screen: the original marked NOT TOUCHED, the copy BEING WRITTEN (or, in place, THIS DOC BEING CHANGED).
- Check shows a picture-by-picture grid (with a legend and counts), the last picture done, the time left, the free disk space against what the check needs, and the connection (retries so far).
- Decide shows the size before and after as bars, the counts as chips, an optional list of every picture, two choice cards with what each keeps and what it costs, a sentence that says exactly what will be written, and a time estimate.
- Replace shows phases (copy made, replacing with group and time left, clean up), the last three pictures replaced, and why Stop is safe.
- Done shows the result in large type, the copy (with Copy link), the original, and the original pictures on this computer.
- Use another account; Recently shrunk (the last five Docs, with Open); chips for the preset and originals with Change… to More options.
- The title bar shows the percentage; on Windows the taskbar button shows the progress too.

**Added: logo**
- A nacho chip holding a picture (#F2B33D chip, #005FB8 picture), drawn in three detail levels for 16 to 256 px. Used for the Windows icon (tools/make-icon.ps1), the Linux icon (Assets/nacho.svg and nacho.png) and both About windows.

**Docs**
- README starts with the Google Doc feature and its five steps. About lists it first. The main window's empty list offers "Shrink a whole Google Doc…". The privacy policy mentions the copy.

**Tests:** 31 Google unit tests (5 new: copy mode reads the original and writes only the copy; a copy with other IDs is matched by order and size; a picture missing from the copy is left alone; a full Drive and a refused copy change nothing). Both self-tests now make a copy, then replace in place, hit a full Drive and a view-only Doc, and take screenshots of every step in English, plus Decide in Spanish and Hungarian.

## 0.9.0 (2026-10-27, built, not published)

**Asked for:** anybody can use the Google Doc feature with their own Google account, without technical background.

**Changed: Google's file picker, one small permission**
- NACHO now asks Google for one permission only: the Drive files the user uses with NACHO (`drive.file`). It no longer asks to see all Google Docs. This is a "non-sensitive" permission, so Google needs no security review and there is no 100-user limit.
- Shrink a Google Doc opens with **Choose a Google Doc…** as the main button. Google opens in the browser, the user signs in (first time only), allows NACHO and picks the Doc; NACHO checks it straight away.
- A pasted or dropped link still works. If NACHO hasn't been given that Doc yet, Google's picker opens with just that Doc and the user confirms it once.
- Messages rewritten for the picker (nothing picked, permission not allowed, Doc the chosen account can't open). More options, About, README and the privacy policy describe the single permission.

**Google Cloud side**
- The app is published (In production) with only `drive.file` and the Picker API. Homepage and privacy policy: csabakovacs314.github.io. Brand verification submitted.

**Tests:** 26 Google unit tests (2 new: the picker returns the chosen Doc and only that Doc opens; closing the picker is a cancel). The pretend Google now behaves like `drive.file`: a Doc not picked answers 404. Both self-tests open the picker for a pasted link, pick from an empty link, and keep the earlier checks.

## 0.8.0 (2026-10-27)

**Asked for:** an option to change the pictures of a Google Doc in place, with the setup in NACHO's settings.

**Added: a Google Doc in place**
- New Google Doc command (Ctrl+G). Copying a Docs link and pressing Ctrl+V, or dragging the link onto the window, opens it too.
- Three steps the user controls. **Check** reads the Doc, including every tab, downloads each picture and makes the smaller copies on this computer, changing nothing. It then shows the count of pictures, how many will shrink, how many are kept or left alone, and the size before and after. **Replace** puts each copy back in the same place. **Done** shows what changed, with Open the Doc and Show the originals.
- Each picture keeps its object, size, position and wrapping in the Doc. The Doc keeps its link, comments, sharing and version history, and File > Version history is the undo.
- A Docs crop is cut into the copy before replacing, because Google resets crops on replace; the visible picture stays the same. Drawings, charts linked from Sheets and pictures with brightness, contrast, transparency or rotation are left alone.
- How a picture goes back: it is uploaded to the user's Drive as a temporary file, shared as "anyone with the link" for a few seconds, swapped in with the Docs API, then deleted. Temporary files left by an interrupted run are removed at the start of the next one.
- Copies of the original pictures go to Docs-Ready\Google Docs originals\<Doc title> <date> (on by default).
- Stop (Esc) finishes the group in progress and stops; closing the window while replacing asks to stop first.
- Clear messages for a wrong link, a Doc the account can't open, a view-only Doc, an expired sign-in, Google asking to slow down, and no connection. Busy answers are retried with backoff.

**Added: More options > Google Docs**
- Sign in with Google and Sign out, with the signed-in account shown. The sign-in happens in the browser, so NACHO never sees the password. It uses a one-time local address and PKCE.
- Permissions are limited to Google Docs and "only the files NACHO creates" in Drive.
- The sign-in is kept encrypted with Windows DPAPI, or on Linux in a file only the user can read. Sign out deletes it and revokes it at Google.
- Keep a copy of the original pictures (on by default).
- Advanced: the Google client built into NACHO, or the user's own client file (Desktop app OAuth client). Release builds include NACHO's client from a file outside the source; the source never contains it.

**Other**
- About and README explain the new feature. Ctrl+G is added to the shortcut list.
- docs/index.md and docs/privacy.md: the homepage and privacy policy Google needs before the app can leave Testing mode.

**Prototype on a real Doc:** a 410-page book with 251 pictures (126 inline, 125 wrapped, 124 of those with a side crop). The scan estimated 655.7 MB of pictures down to 31.1 MB with Balanced.
- Live replacements tested: an inline photo (5.2 MB PNG to 202 KB JPEG), a wrapped picture with a crop, and a wrapped picture without one.
- Google stored NACHO's bytes exactly. The object ID, size, margins and position stayed the same, and the crop came out visually the same.

**Tests:** 175 unit tests (22 new) against an in-memory fake Google: link parsing, client files, the Doc parser (tabs, child tabs, effects, crops), check then apply, a picture removed between check and apply, cancelling, leftover clean-up, retries and token renewal, error messages, the browser sign-in with PKCE and a forged answer, and the private sign-in file on Linux. Both self-tests sign in, check, replace and hit a view-only Doc against the same fake Google, with screenshots.

## 0.7.0 (2026-10-26)

**Asked for:** the menu named for files, not only images; smaller PDFs (pictures taken out, shrunk, and the PDF made again); a version check against GitHub.

**Changed: files, not only images**
- "Add images" is now "Add files" (ES "Añadir archivos", HU "Fájlok hozzáadása"); the drop zone, list counter, empty list, Open dialog, About and the installer's right-click option name images, Word and PDF files. The right-click entry "Make Docs-ready" now also appears on PDFs (Windows and Linux).

**Added: smaller PDFs**
- Drop a PDF: pictures printed sharper than the preset needs (Small 100, Balanced 150, Sharp 220 dpi) are scaled down, photos as JPEG and graphics without loss, and put back with PDFium; a picture used on many pages is shrunk once and replaced everywhere. Text, fonts, links, forms and bookmarks are untouched.
- Safety check before a copy is kept: same page count and page sizes, same text on every page, and every page drawn from both files must stay within 24 dB PSNR. Otherwise, or when the copy is not at least 2% smaller, the original is kept and the tile says why.
- Left as they are: transparency, CMYK and other print colour spaces, black-and-white scans (JBIG2, CCITT), pictures inside form XObjects. Password-protected and signed PDFs get a message and are not changed.
- Tile "4 of 9 pictures made smaller · 3 pages · PDF", a Whole PDF panel with document, picture and page counts, and Compare with original page by page (Page Up and Page Down).
- PDFium 158.0.8111 (BSD-3-Clause, prebuilt by bblanchon/pdfium-binaries) is bundled; its licences are in the new licenses folder.

**Added: update check**
- Once a day NACHO asks GitHub's public API for the latest release (no account, nothing about the user). A newer version shows a bar with Open release page and Not now; nothing is downloaded or installed. On and off in More options; About has Check for updates. Only NACHO's own release page is ever opened.

**Tests:** 153 unit tests (19 new: 8 for PDFs with a hand-made sample PDF, 11 for the update check with a fake GitHub). Both self-tests add a PDF, PDF page compare and the update bar, with screenshots. Prototype on 16 PDFs from reportlab, img2pdf, pikepdf and LibreOffice: every shrunk file passed qpdf --check, kept its text (poppler) and its pages looked the same; 7 got 70% to 97% smaller, the rest were kept or refused as designed.

## 0.6.0 (2026-10-26)

**Asked for:** two ideas from similar projects: a before/after slider with 100% zoom (from Squoosh and IMGo), and shrinking every picture in a document at once (the idea behind tinyoffice).

**Added: Compare with original**
- A window with the original left of a divider and the Docs-ready copy right of it. Drag the divider or click where it should go; drag the picture to look around. Fit, 100%, 200% and 400% zoom, where 100% is one picture pixel per screen pixel at any display scaling.
- The original is turned upright and shown at the copy's size with the same resize the engine uses, so the slider shows only what saving changed.
- For a screenshot that kept full colour (0.5.0's banding guard), the right side can switch to the 256-colour version NACHO turned down, with its score against the 45 dB limit.
- Opens from the preview panel, the tile's right-click menu, or Ctrl+D. Keyboard: Left and Right move the divider, 0, 1, 2 and 4 zoom, Esc closes; the divider is also a slider for screen readers. Clicking places the divider, so no dragging is needed (WCAG 2.5.7).

**Added: whole Word documents (.docx)**
- Drop a .docx (Google Docs: File > Download > Microsoft Word) and every picture inside is shrunk with the current preset and privacy options. The result is a new "<name>-docs.docx" in Docs-Ready; the original is never changed.
- A PNG photo that becomes a JPEG is renamed inside the document, and every link to it (body, headers, footers) and its content type follow. The text and layout parts are copied byte for byte. EMF, WMF and SVG drawings stay as they are. The new file is checked before anyone sees it (every link, every content type, the part count).
- The tile shows "3 of 4 pictures made smaller" and the preview panel shows document and picture sizes, the steps back into Google Docs, and a warning that this makes a new Doc with a new link.
- The right-click entry (Windows and Linux) and the Open dialog now include .docx. Old .doc and password-protected files get a clear message.

**Tests:** 134 unit tests (16 new: the document shrinker on a sample with a renamed picture, shared header picture, absolute and relative links, an Override content type, an EMF and GPS; kept, locked, old and broken documents; link resolution; the comparison pictures). Both self-tests add a document and the compare window, with screenshots. Outside NACHO: LibreOffice and python-docx open the shrunk documents with the same layout, also for .docx files written by LibreOffice and pandoc.

**Not checked yet:** a .docx downloaded from a real Google Doc and uploaded back; Word on Windows opening the result.

## 0.5.0 (2026-10-26)

**Asked for:** points 1 and 2 of the compression study: fix the banding in 256-colour screenshots, and add oxipng.

**Fixed**
- Screenshots with a photo in them (a window with thumbnails, a web page with pictures) showed banding in their 256-colour copy. NACHO now measures the 256-colour version against the full-colour picture and uses it only at 45 dB PSNR or better. Below that, the copy keeps all its colours and the tile says "Full colour, no banding" (also in Spanish and Hungarian). In the study the banding cases measured 40 to 44 dB and the clean ones 46 to 61 dB.

**Added**
- oxipng 10.2.1 (MIT) runs after every PNG copy: the same pixels in fewer bytes, about 5% on 256-colour screenshots and about 25% on full-colour ones, in well under a second. It is built into both apps and unpacked once to %LOCALAPPDATA%\NACHO\tools (Windows) or ~/.cache/nacho/tools (Linux). If it is missing or fails, NACHO keeps the PNG as Magick.NET wrote it.
- Uninstall (Windows setup and Linux uninstall.sh) also removes the unpacked oxipng copy.
- README: what NACHO does to screenshots, and the included software with oxipng's licence.

**Size effect (19 real screenshots from the study):** total -6% when every screenshot keeps 256 colours. The 8 screenshots that would band now stay in full colour, so they are 2 to 3 times bigger than before (all still under the Balanced 300 KB budget); with them the total is about +67%, the price of no banding.

**Tests:** 118 unit tests (8 new: the guard keeps a banded screenshot in full colour with identical pixels, a plain one keeps 256 colours, PSNR, oxipng shrinks a PNG with identical pixels, failures keep the original, unpacking repairs a changed copy). Both self-tests check that oxipng unpacks and runs (10.2.1), that it shrank the screenshot copy, and that a generated banded screenshot stays in full colour.

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
