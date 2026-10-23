NACHO 0.2.0
Normalise, Adjust, Compress, Hand Over
(Spanish: Normaliza, Ajusta, Comprime y Honra el Original)

NACHO makes a small, Docs-ready copy of an image before it goes into a Google Doc.
Your original file is never changed. The copy goes to Pictures\Docs-Ready.

FILES
  NACHO-0.2.0-setup.exe      Installer (recommended). English or Spanish, no admin rights needed.
  NACHO.exe                  Portable. Runs on Windows 10 or 11 (x64) with nothing to install.
  Small\NACHO.exe            Same app, much smaller, needs the .NET 10 Desktop Runtime.
  NACHO-0.2.0-source.zip     Source, unit tests and build.ps1.
  NACHO-0.2.0.zip            Everything above in one package.

INSTALLER
  Run NACHO-0.2.0-setup.exe, pick English or Español, then "Install for me only" (no admin
  rights; goes to %LOCALAPPDATA%\Programs\NACHO) or "Install for all users" (Program Files).
  Options on the way: the right-click entry (on by default), Send to, a desktop shortcut.
  NACHO then starts in the language you picked. You get a Start menu entry, and NACHO appears
  in Settings > Apps, where you can uninstall it.
  Uninstall removes the program, its Start menu entry, the right-click entry and Send to.
  It keeps your settings (%APPDATA%\NACHO) and your Docs-Ready pictures.
  A newer setup installs over an older one and keeps your settings.
  If you used the portable NACHO.exe before with the right-click entry on, the installer
  points that entry at the installed copy.

FIRST RUN
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
  EN | ES buttons in the command bar switch the whole window at once.

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
