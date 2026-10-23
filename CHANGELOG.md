# NACHO changelog

Versions: MAJOR.MINOR.PATCH. Minor for a feature, patch for a fix.

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
