NACHO 0.11.0
Normalise, Adjust, Compress, Hand Over
(Spanish: Normaliza, Ajusta, Comprime y Honra el Original)
(Hungarian: Normalizál, Arányt tart, Csökkent és Hűen Odaad)

A Google Doc that is slow to open, or eats your Drive storage, because of big pictures?
NACHO makes every picture in it smaller, keeps each one in its place, and by default puts the
result in a new copy of the Doc, so your original stays exactly as it is. A real 410-page book
with 251 photos went from 524 MB to 39 MB. See SHRINK A WHOLE GOOGLE DOC just below.

NACHO opens on a start screen with both ways in side by side: Pictures and files on the left
(drop them, Add files or Paste, and pick Small, Balanced or Sharp), A whole Google Doc on the right.

NACHO also makes a small, Docs-ready copy of a single image before it goes into a Google Doc,
shrinks every picture in a Word document (.docx) at once, and makes smaller PDFs.
Your original files are never changed. Copies go to Pictures\Docs-Ready.
NACHO runs on Windows 10 and 11, and on 64-bit Linux (see LINUX below).

SHRINK A WHOLE GOOGLE DOC
  Press Google Doc (Ctrl+G). Five steps, always shown at the top of the window:
  1 Choose   Press Choose a Google Doc. Google opens in your browser: sign in the first time, allow
             NACHO, and pick the Doc. Or paste the Doc's link (Ctrl+V anywhere in NACHO, or drag the
             link onto the window); Google then asks once to confirm that Doc for NACHO.
  2 Check    NACHO downloads the pictures and makes the smaller ones on this computer. Nothing in
             Google changes yet: the window says READ ONLY. You see each picture as it is done, the
             time left, the free disk space and the connection.
  3 Decide   Where should the smaller pictures go?
             Into a new copy (recommended, the default): "<title> (smaller)", in the same Drive
               folder. The original is not touched; to undo, delete the copy. The copy has its own
               link and is private to you; comments, suggestions and sharing are not copied.
             Into this Doc: same link, sharing, comments and history. Readers see the change at
               once; to undo, open the Doc and use File > Version history.
  4 Replace  Both Docs are on screen: the original marked NOT TOUCHED, the copy BEING WRITTEN.
             For each picture NACHO puts the smaller one in your Drive for a few seconds, shared as
             "anyone with the link" so Google Docs can copy it, replaces the picture and deletes the
             file. Stop (Esc) is safe at any moment.
  5 Done     Open the copy, copy its link, open the original, or show the original pictures saved
             on this computer. Recently shrunk (in step 1) lists your last five Docs.
  Each picture keeps its size on the page and its wrapping. A Docs crop is cut into the smaller
  picture, so it looks the same. Left as they are: drawings, charts linked from Sheets, and pictures
  with Docs effects (brightness, contrast, transparency, rotation).
  What NACHO can reach: only the Docs you pick and the files it makes itself in your Drive (Google's
  drive.file permission). It never sees your password and never deletes a Doc. A view-only Doc
  works too: choose Into a new copy, and the copy is yours.
  Copies of the original pictures are saved in Docs-Ready\Google Docs originals (turn that off in
  More options > Google Docs). Use another account (step 1) lets you pick a different Google account.
  Your sign-in is kept encrypted for your Windows user (%APPDATA%\NACHO\google-signin.bin; on
  Linux ~/.config/nacho/google-signin, readable only by you). Sign out in More options removes it and
  tells Google. You can also remove NACHO's access at myaccount.google.com > Security >
  Third-party connections.

FILES
  NACHO-0.11.0-setup.exe      Windows installer (recommended). English, Spanish or Hungarian, no admin rights needed.
  NACHO.exe                  Windows portable. Runs on Windows 10 or 11 (x64) with nothing to install.
  Small\NACHO.exe            Same Windows app, much smaller, needs the .NET 10 Desktop Runtime.
  Linux\NACHO-0.11.0-x86_64.AppImage     Linux, one file: make it executable and run it.
  Linux\nacho-0.11.0-linux-x64.tar.gz    Linux, with install.sh (app menu, Open With, right-click entries).
  Linux\SHA256SUMS.txt                  Checksums for the two Linux files.
  NACHO-0.11.0-source.zip     Source, unit tests, build.ps1 (Windows) and build-linux.sh (Linux).
  NACHO-0.11.0.zip            Everything above in one package.
  licenses\                  Licences of the software NACHO includes (PDFium and its parts).

INSTALLER
  Run NACHO-0.11.0-setup.exe, pick English, Español or Magyar, then "Install for me only" (no admin
  rights; goes to %LOCALAPPDATA%\Programs\NACHO) or "Install for all users" (Program Files).
  Options on the way: the right-click entry (on by default), Send to, a desktop shortcut.
  NACHO then starts in the language you picked. You get a Start menu entry, and NACHO appears
  in Settings > Apps, where you can uninstall it.
  Uninstall removes the program, its Start menu entry, the right-click entry, Send to and the
  oxipng copy in %LOCALAPPDATA%\NACHO\tools.
  It keeps your settings (%APPDATA%\NACHO) and your Docs-Ready pictures.
  A newer setup installs over an older one and keeps your settings.
  If you used the portable NACHO.exe before with the right-click entry on, the installer
  points that entry at the installed copy.

LINUX
  Works on 64-bit PCs with any common distro: Ubuntu, Debian, Mint, Fedora, AlmaLinux, openSUSE,
  Arch and others, on GNOME, KDE Plasma, Cinnamon, MATE or Xfce. Nothing needs root.
  The window, presets, languages, preview and buttons are the same as on Windows.

  AppImage (quickest):
    chmod +x NACHO-0.11.0-x86_64.AppImage
    ./NACHO-0.11.0-x86_64.AppImage
  In More options you can add it to the app menu, to Open With and to the right-click menu.
  Those point at the AppImage file, so put it where it will live first.

  Install (adds NACHO to the app menu, Open With and the right-click menu at once):
    tar xzf nacho-0.11.0-linux-x64.tar.gz
    cd nacho-0.11.0-linux-x64
    ./install.sh                 (or ./install.sh --lang hu, --lang es)
  It goes to ~/.local/share/nacho, with the command ~/.local/bin/nacho.
  Remove it with ~/.local/share/nacho/uninstall.sh (settings and Docs-Ready pictures stay; the oxipng
  copy in ~/.cache/nacho/tools goes too).

  Right-click menu: Files (GNOME) shows Make Docs-ready under Scripts; Nemo, Caja and Dolphin show it
  directly. Every file manager also has Open With > NACHO.
  Into Google Docs: drag the thumbnail into the Doc. Copy file is for pasting into a file manager.
  For Replace image > Upload from computer: right-click the tile > Copy path, then in the file dialog
  press Ctrl+L, Ctrl+V and Enter.
  Settings: ~/.config/nacho/settings.json. Copies: Pictures/Docs-Ready (also when Pictures has a
  translated name). Problems are logged in ~/.config/nacho/nacho.log.
  Needs the ICU library (libicu), which desktop distros have. If NACHO does not start, install it:
  Ubuntu, Debian, Mint: sudo apt install libicu-dev     Fedora, AlmaLinux: sudo dnf install libicu
  Self-test: nacho --selftest /tmp/nacho-test   (or ./NACHO-0.11.0-x86_64.AppImage --selftest ...)

FIRST RUN (WINDOWS)
  The exe is not code-signed, so Windows SmartScreen may say "Windows protected your PC".
  Choose More info, then Run anyway. If you copied the zip from a download, right-click it,
  choose Properties, tick Unblock, then extract.

FOUR WAYS IN
  1. Drop images, .docx or PDF files on the window, or press Ctrl+O.
  2. Copy an image or a snip (Win+Shift+S), then press Ctrl+V in NACHO.
  3. Right-click an image in Explorer > Make Docs-ready (setup offers it, or turn it on in More options).
  4. Send to > NACHO (setup offers it, or turn it on in More options).
  A Word document (.docx) works the same way: see A WHOLE DOC AT ONCE below.
  A Google Doc already full of pictures: press Google Doc (Ctrl+G), or copy its link and press
  Ctrl+V. See SHRINK A WHOLE GOOGLE DOC above.
  Then drag the thumbnail into your Google Doc, or press Copy file and Ctrl+V in the Doc.

PRESETS
  Small     800 px long edge, up to 150 KB
  Balanced  1280 px long edge, up to 300 KB (default)
  Sharp     1920 px long edge, up to 600 KB
  Changing the preset redoes the images in the list. Images are never made bigger.

SHAPE (ASPECT RATIO)
  A copy always has the same shape as its original: never cropped, never stretched.
  Common shapes (4:3, 3:2, 16:9 and most camera sizes) come out exact. Other shapes stay within
  0.1%, very thin strips within 1%. The tile and the page-fit preview show the shape.
  This matters when you swap an image that is already in a Doc: click it in the Doc, choose
  Replace image > Upload from computer, and pick the copy. Docs fits the new image into the old
  image's frame, so a same-shape copy takes its place without stretching.
  Quicker: right-click the tile in NACHO > Copy path, then press Ctrl+V in the File name box of
  that dialog and Enter.
  Phone photos are turned upright first, so the copy has the shape you see on screen.

LANGUAGE
  EN | ES | HU buttons in the command bar switch the whole window at once (English, Spanish,
  Hungarian). More options has the same choice. Sizes follow the language: 4.8 MB in English,
  4,8 MB in Spanish and Hungarian.
  The first start uses the language picked in setup; without setup, the Windows display
  language if it is one of the three, otherwise English.

WHAT IT DOES TO EACH IMAGE
  Turns phone photos upright, resizes to the preset, removes location and camera details,
  saves photos as JPEG and screenshots as PNG, converts HEIC, WebP, AVIF, TIFF and BMP,
  keeps transparency and animation. If a copy would not be smaller and the original is
  already fine for Docs, NACHO keeps the original and says so.
  Screenshots: NACHO tries 256 colours and uses them only when the picture looks the same
  (badge "256 colours"). Where that would show banding, for example a screenshot with a photo
  in it, the copy keeps all its colours (badge "Full colour, no banding").
  Every PNG copy then goes through oxipng: the same pixels in about 5% fewer bytes (more for
  full-colour copies). It adds well under a second per screenshot.

COMPARE WITH ORIGINAL
  Select a shrunk image and press Compare with original (or Ctrl+D, or right-click the tile).
  A window shows the original left of a divider and the copy right of it. Drag the divider, or
  click where it should go; drag the picture to look around. Zoom: Fit, 100% (one picture pixel
  per screen pixel), 200% and 400%; keys 0, 1, 2 and 4, Ctrl+wheel, Esc closes. Left and Right
  move the divider; the slider under the picture does the same for keyboards and screen readers.
  The original is shown at the copy's size, so you see only what saving changed: JPEG blocks,
  banding, softer text. For a screenshot that kept full colour, the right side can switch to the
  256-colour version NACHO turned down, so you can see the banding it avoided.

A WHOLE DOC AT ONCE (.docx)
  In Google Docs choose File > Download > Microsoft Word (.docx), then drop the file on NACHO
  (or right-click it > Make Docs-ready). Every picture inside goes through the same steps as a
  dropped image, and a new "<name>-docs.docx" goes to Docs-Ready. The text and layout are copied
  untouched; pictures keep their place and size on the page. Drawings (EMF, WMF, SVG) stay as
  they are. Location data is removed from photos inside, like for single images.
  Back into Google Docs: in Google Drive choose New > File upload (or drag the tile into Drive),
  then right-click the file > Open with > Google Docs. That makes a NEW Google Doc with a new
  link. Comments and suggestions may not all come back, and version history and sharing stay with
  the old Doc, so check the new one before you retire the old one.
  Old .doc files and password-protected .docx files can't be opened; save them as .docx without a
  password first.

A SMALLER PDF
  Drop a PDF (or right-click it > Make Docs-ready). Pictures printed sharper than the preset needs
  are scaled down: Small 100 dpi, Balanced 150 dpi, Sharp 220 dpi. Photos are saved as JPEG,
  graphics without loss. A smaller "<name>-docs.pdf" goes to Docs-Ready.
  Text, fonts, links, form fields and bookmarks stay as they are. Before the copy is kept, every
  page is drawn from both files and compared: same pages, same sizes, same text, and the pages
  must look the same. If anything else changed, or the copy is not at least 2% smaller, NACHO
  keeps your PDF and says why.
  Left as they are: pictures with transparency, print colours (CMYK), black-and-white scans, and
  pictures inside drawing groups (form XObjects). Password-protected and digitally signed PDFs are
  not changed at all: a new file would remove the protection or break the signature.
  Compare with original (Ctrl+D) shows each page of the original and the smaller copy side by side;
  Page Up and Page Down move between pages.

UPDATES
  Once a day NACHO asks GitHub whether a newer release exists (turn it off in More options). It
  sends nothing about you: GitHub sees an ordinary request from "NACHO/0.11.0". When a newer version
  is out, a bar says so, with Open release page and Not now. NACHO never downloads or installs
  anything by itself. About > Check for updates asks straight away.

RIGHT-CLICK MENU AND SEND TO
  Both are off until you tick them in More options. They are added for your user only
  (no admin rights) and point at the NACHO.exe you were running when you saved. Put the exe
  where it will live first, then turn them on. Untick them to remove them again.

FILES NACHO KEEPS
  Settings: %APPDATA%\NACHO\settings.json
  Google:   %APPDATA%\NACHO\google-signin.bin (only after you sign in; encrypted for your user)
  Log:      %APPDATA%\NACHO\nacho.log (only written when something goes wrong)
  Copies:   Pictures\Docs-Ready (change it in More options)

SELF-TEST
  NACHO.exe --selftest C:\some\folder
  Builds sample images, runs them through the window off-screen, checks the results and saves
  screenshots. It uses its own settings and never touches the registry.

UNINSTALL
  Installed: Settings > Apps > NACHO > Uninstall.
  Portable: untick the right-click menu and Send to in More options, close NACHO, then delete
  its folder and %APPDATA%\NACHO.

INCLUDED SOFTWARE
  NACHO uses Magick.NET (Apache 2.0) for images and, on Linux, Avalonia (MIT) for the window.
  PDFs are read and written with PDFium (BSD-3-Clause, the PDF engine of Chrome), built by
  bblanchon/pdfium-binaries 158.0.8111. PDFium includes FreeType, libjpeg-turbo, OpenJPEG,
  Little CMS, libpng, zlib, ICU and other parts; their licences are in the licenses folder.
  It carries oxipng 10.2.1 (https://github.com/oxipng/oxipng) and unpacks it on first use to
  %LOCALAPPDATA%\NACHO\tools on Windows or ~/.cache/nacho/tools on Linux. oxipng's licence:

  The MIT License (MIT)
  Copyright (c) 2016 Joshua Holmer

  Permission is hereby granted, free of charge, to any person obtaining a copy of
  this software and associated documentation files (the "Software"), to deal in
  the Software without restriction, including without limitation the rights to
  use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
  of the Software, and to permit persons to whom the Software is furnished to do
  so, subject to the following conditions:

  The above copyright notice and this permission notice shall be included in all
  copies or substantial portions of the Software.

  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
  SOFTWARE.

Idea: Csaba Kovacs. Built with Claude.
