# NACHO pictures

Three sets: the mockup NACHO was designed from, and screenshots of the app as built on Windows and on Linux.

## Mockup (23 Oct 2026)

Drawn before any code, against Nielsen's 10 heuristics, ISO 9241-110, WCAG 2.2 AA and Microsoft's Windows guidance for commands and dialogs. Rendered at 2x from the design canvas; the font is a stand-in for Segoe UI.

Some mockup ideas are not built yet: the clipboard watcher, the first-run tour, the Docs-Outbox folder, tray notifications and "Start with Windows". The rest is in the app, sometimes in a different place (the app puts Copy path and Copy as picture on the tile's right-click menu, for example).

### Main window
Tiles with before and after sizes, the page-fit preview, and the plain-language error for a file NACHO cannot shrink.

<img src="mockup/1-main-window.png" width="900" alt="Mockup of the NACHO main window with four result tiles and a page-fit preview">

### Compact window, 400 px wide
The same window narrowed: presets become a dropdown and the preview moves behind a button.

<img src="mockup/2-compact-400px.png" width="300" alt="Mockup of the NACHO window at 400 pixels wide">

### First run
An empty drop zone as the one obvious action, with a three-step tour.

<img src="mockup/3-first-run.png" width="720" alt="Mockup of NACHO's first run with an empty drop zone and step 1 of a three-step tour">

### More options
Advanced settings kept out of the main window.

<img src="mockup/4-more-options.png" width="540" alt="Mockup of the More options window: presets, output format, privacy, where copies go">

### Tray notifications
Feedback when the window is closed, with Undo.

<img src="mockup/5-notifications.png" width="315" alt="Mockup of two NACHO tray notifications">

## The Windows app, version 0.3.0

The Windows window did not change in 0.4.0.

Taken by NACHO's own self-test (`NACHO.exe --selftest <folder>`) on Windows 11, from the sample images it generates.

### Main window: English, Spanish, Hungarian

<img src="screenshots/main-en-light.png" width="900" alt="NACHO main window in English">

<img src="screenshots/main-es-light.png" width="900" alt="NACHO main window in Spanish">

<img src="screenshots/main-hu-light.png" width="900" alt="NACHO main window in Hungarian">

### Dark theme and compact layout

<img src="screenshots/main-es-dark.png" width="900" alt="NACHO main window in Spanish, dark theme">

<img src="screenshots/main-en-narrow.png" width="300" alt="NACHO main window at 420 pixels wide">

### More options: English, Spanish, Hungarian

<img src="screenshots/options-en.png" width="300" alt="More options in English"> <img src="screenshots/options-es.png" width="300" alt="More options in Spanish"> <img src="screenshots/options-hu.png" width="300" alt="More options in Hungarian">

### About: Spanish, Hungarian

<img src="screenshots/about-es.png" width="420" alt="About window in Spanish"> <img src="screenshots/about-hu.png" width="420" alt="About window in Hungarian">

## The Linux app, version 0.4.0

The same window on Linux (Avalonia), taken by its own self-test (`nacho --selftest <folder>`) on a virtual X display. The font is Inter, which ships with NACHO so every distro shows the same window.

### Main window: English, Spanish, Hungarian

<img src="screenshots-linux/main-en-light.png" width="900" alt="NACHO for Linux, main window in English">

<img src="screenshots-linux/main-es-light.png" width="900" alt="NACHO for Linux, main window in Spanish">

<img src="screenshots-linux/main-hu-light.png" width="900" alt="NACHO for Linux, main window in Hungarian">

### Dark theme and compact layout

<img src="screenshots-linux/main-es-dark.png" width="900" alt="NACHO for Linux in Spanish, dark theme">

<img src="screenshots-linux/main-en-narrow.png" width="300" alt="NACHO for Linux at 420 pixels wide">

### More options: English, Spanish, Hungarian

<img src="screenshots-linux/options-en.png" width="300" alt="More options in English, Linux"> <img src="screenshots-linux/options-es.png" width="300" alt="More options in Spanish, Linux"> <img src="screenshots-linux/options-hu.png" width="300" alt="More options in Hungarian, Linux">

### About: Spanish, Hungarian

<img src="screenshots-linux/about-es.png" width="420" alt="About window in Spanish, Linux"> <img src="screenshots-linux/about-hu.png" width="420" alt="About window in Hungarian, Linux">
