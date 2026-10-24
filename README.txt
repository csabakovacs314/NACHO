NACHO 0.4.0
Normalise, Adjust, Compress, Hand Over
(Spanish: Normaliza, Ajusta, Comprime y Honra el Original)
(Hungarian: Normalizál, Arányt tart, Csökkent és Hűen Odaad)

NACHO makes a small, Docs-ready copy of an image before it goes into a Google Doc.
Your original file is never changed. The copy goes to Pictures\Docs-Ready.
NACHO runs on Windows 10 and 11, and on 64-bit Linux (see LINUX below).

FILES
  NACHO-0.4.0-setup.exe      Windows installer (recommended). English, Spanish or Hungarian, no admin rights needed.
  NACHO.exe                  Windows portable. Runs on Windows 10 or 11 (x64) with nothing to install.
  Small\NACHO.exe            Same Windows app, much smaller, needs the .NET 10 Desktop Runtime.
  Linux\NACHO-0.4.0-x86_64.AppImage     Linux, one file: make it executable and run it.
  Linux\nacho-0.4.0-linux-x64.tar.gz    Linux, with install.sh (app menu, Open With, right-click entries).
  Linux\SHA256SUMS.txt                  Checksums for the two Linux files.
  NACHO-0.4.0-source.zip     Source, unit tests, build.ps1 (Windows) and build-linux.sh (Linux).
  NACHO-0.4.0.zip            Everything above in one package.

INSTALLER
  Run NACHO-0.4.0-setup.exe, pick English, Español or Magyar, then "Install for me only" (no admin
  rights; goes to %LOCALAPPDATA%\Programs\NACHO) or "Install for all users" (Program Files).
  Options on the way: the right-click entry (on by default), Send to, a desktop shortcut.
  NACHO then starts in the language you picked. You get a Start menu entry, and NACHO appears
  in Settings > Apps, where you can uninstall it.
  Uninstall removes the program, its Start menu entry, the right-click entry and Send to.
  It keeps your settings (%APPDATA%\NACHO) and your Docs-Ready pictures.
  A newer setup installs over an older one and keeps your settings.
  If you used the portable NACHO.exe before with the right-click entry on, the installer
  points that entry at the installed copy.

LINUX
  Works on 64-bit PCs with any common distro: Ubuntu, Debian, Mint, Fedora, AlmaLinux, openSUSE,
  Arch and others, on GNOME, KDE Plasma, Cinnamon, MATE or Xfce. Nothing needs root.
  The window, presets, languages, preview and buttons are the same as on Windows.

  AppImage (quickest):
    chmod +x NACHO-0.4.0-x86_64.AppImage
    ./NACHO-0.4.0-x86_64.AppImage
  In More options you can add it to the app menu, to Open With and to the right-click menu.
  Those point at the AppImage file, so put it where it will live first.

  Install (adds NACHO to the app menu, Open With and the right-click menu at once):
    tar xzf nacho-0.4.0-linux-x64.tar.gz
    cd nacho-0.4.0-linux-x64
    ./install.sh                 (or ./install.sh --lang hu, --lang es)
  It goes to ~/.local/share/nacho, with the command ~/.local/bin/nacho.
  Remove it with ~/.local/share/nacho/uninstall.sh (settings and Docs-Ready pictures stay).

  Right-click menu: Files (GNOME) shows Make Docs-ready under Scripts; Nemo, Caja and Dolphin show it
  directly. Every file manager also has Open With > NACHO.
  Into Google Docs: drag the thumbnail into the Doc. Copy file is for pasting into a file manager.
  For Replace image > Upload from computer: right-click the tile > Copy path, then in the file dialog
  press Ctrl+L, Ctrl+V and Enter.
  Settings: ~/.config/nacho/settings.json. Copies: Pictures/Docs-Ready (also when Pictures has a
  translated name). Problems are logged in ~/.config/nacho/nacho.log.
  Needs the ICU library (libicu), which desktop distros have. If NACHO does not start, install it:
  Ubuntu, Debian, Mint: sudo apt install libicu-dev     Fedora, AlmaLinux: sudo dnf install libicu
  Self-test: nacho --selftest /tmp/nacho-test   (or ./NACHO-0.4.0-x86_64.AppImage --selftest ...)

FIRST RUN (WINDOWS)
  The exe is not code-signed, so Windows SmartScreen may say "Windows protected your PC".
  Choose More info, then Run anyway. If you copied the zip from a download, right-click it,
  choose Properties, tick Unblock, then extract.

FOUR WAYS IN
  1. Drop images on the window, or press Ctrl+O.
  2. Copy an image or a snip (Win+Shift+S), then press Ctrl+V in NACHO.
  3. Right-click an image in Explorer > Make Docs-ready (setup offers it, or turn it on in More options).
  4. Send to > NACHO (setup offers it, or turn it on in More options).
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

RIGHT-CLICK MENU AND SEND TO
  Both are off until you tick them in More options. They are added for your user only
  (no admin rights) and point at the NACHO.exe you were running when you saved. Put the exe
  where it will live first, then turn them on. Untick them to remove them again.

FILES NACHO KEEPS
  Settings: %APPDATA%\NACHO\settings.json
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

Idea: Csaba Kovacs. Built with Claude.
