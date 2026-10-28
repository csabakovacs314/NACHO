# NACHO pictures

Screenshots of NACHO 0.12.0 on Windows and on Linux, the design mockups they were built from, and the first mockup from October 2026.

All screenshots are taken by NACHO's own self-test (`NACHO.exe --selftest <folder>` on Windows 11, `nacho --selftest <folder>` on Linux on a virtual X display). It generates its own sample pictures, a sample Word document and PDF, and talks to a pretend Google with a sample Doc ("Plantas no nativas (sample)"), a sample Word file in Drive ("Team handbook.docx") and the account tester@example.com. No real Doc or account is shown.

## Shrink a whole Google Doc

The Google Doc window shows five steps at the top: Choose, Check, Decide, Replace, Done. Nothing in Google changes before step 4, and by default the smaller pictures go into a new copy so the original stays as it is.

### Step 1, Choose

Google's own picker opens in the browser. A pasted link works too. Recently shrunk Docs are listed with Open.

<img src="screenshots/google-en-choose.png" width="560" alt="Step 1, Choose: READ ONLY strip, signed-in account, Choose a Google Doc, paste a link, recently shrunk Docs">

### Step 3, Decide

The size now and after, the counts as chips, and two choices with what each keeps and what it costs. A sentence says exactly what will be written.

<img src="screenshots/google-en-decide.png" width="560" alt="Step 3, Decide: 19.1 MB to 587 KB, Into a new copy selected and recommended">

<img src="screenshots/google-en-decide-doc.png" width="560" alt="Step 3, Decide with Into this Doc selected">

In Spanish and Hungarian:

<img src="screenshots/google-es-decide.png" width="420" alt="Step 3 in Spanish"> <img src="screenshots/google-hu-decide.png" width="420" alt="Step 3 in Hungarian">

### Step 5, Done

The result in large type, the new copy (with Copy link), the original marked not touched, and the original pictures saved on the computer.

<img src="screenshots-linux/google-en-done.png" width="560" alt="Step 5, Done: Your smaller copy is ready, 19.1 MB to 589 KB, 96% smaller">

### More options > Google Docs

Signed-in account, where the smaller pictures go by default, keep the original pictures, and the sign-in client.

<img src="screenshots/options-google-en.png" width="560" alt="More options, Google Docs section">

## Shrink a Word file (0.12.0)

The same five steps for a .docx on this PC or in Google Drive. Nothing changes before step 4; by default the smaller pictures go into a new file next to the original.

### Step 1, Choose

<img src="screenshots/word-en-choose.png" width="560" alt="Word window, step 1: On this PC or In Google Drive, the signed-in account, and recently shrunk files">

### Step 3, Decide

A file on this PC, with Into this file selected: the original is saved first in Docs-Ready > Word originals.

<img src="screenshots/word-en-decide-file.png" width="560" alt="Word window, step 3 for a file on this PC with Into this file selected">

A file in Google Drive: a new copy in the same Drive folder, or a new version of the same file.

<img src="screenshots/word-en-decide-drive.png" width="560" alt="Word window, step 3 for a file in Google Drive">

### Step 5, Done

<img src="screenshots/word-en-done.png" width="560" alt="Word window, step 5: Team handbook.docx is smaller, with the saved original and Restore original">

On Linux:

<img src="screenshots-linux/word-en-decide.png" width="420" alt="Word window on Linux, step 3"> <img src="screenshots-linux/word-en-done.png" width="420" alt="Word window on Linux, step 5">

## The main window

### Start: both ways in (0.12.0)

<img src="screenshots-linux/main-en-home.png" width="900" alt="Start screen: Pictures and PDFs on the left, A whole document (Google Doc or Word file) on the right">

<img src="screenshots-linux/main-en-home-dark.png" width="900" alt="Start screen, dark theme">

<img src="screenshots-linux/main-en-home-narrow.png" width="300" alt="Start screen at 420 pixels wide: the two cards stack">

### Results: English, Spanish, Hungarian (Windows)

Each file is a card: the old size struck through, the new size large and green, the percentage smaller, details as chips, Copy file, Show in folder and Compare. The selected card has the accent border; a file that needs attention has an amber border.

<img src="screenshots/main-en-light.png" width="900" alt="Results in English">

<img src="screenshots/main-es-light.png" width="900" alt="Results in Spanish">

<img src="screenshots/main-hu-light.png" width="900" alt="Results in Hungarian">

### Dark theme and compact layout (Windows)

<img src="screenshots/main-es-dark.png" width="900" alt="Results in Spanish, dark theme">

<img src="screenshots/main-en-narrow.png" width="300" alt="Results at 420 pixels wide">

### Compare with original

<img src="screenshots/compare-en-copy.png" width="900" alt="Compare window: the original and the copy with a divider">

### More options: English, Spanish, Hungarian (Windows)

A section list on the left that follows your scrolling. The bottom bar is Reset to defaults, then Save options, then Cancel.

<img src="screenshots/options-en.png" width="420" alt="More options in English"> <img src="screenshots/options-es.png" width="420" alt="More options in Spanish">

<img src="screenshots/options-hu.png" width="420" alt="More options in Hungarian">

### About: English, Spanish, Hungarian (Windows)

<img src="screenshots/about-en.png" width="300" alt="About in English"> <img src="screenshots/about-es.png" width="300" alt="About in Spanish"> <img src="screenshots/about-hu.png" width="300" alt="About in Hungarian">

## The Linux app, version 0.12.0

The same design on Linux (Avalonia). The font is Inter, which ships with NACHO so every distro shows the same window.

<img src="screenshots-linux/main-en-light.png" width="900" alt="NACHO for Linux, results in English">

<img src="screenshots-linux/main-es-dark.png" width="900" alt="NACHO for Linux in Spanish, dark theme">

<img src="screenshots-linux/google-en-choose.png" width="420" alt="Google Doc window on Linux, step 1"> <img src="screenshots-linux/google-en-decide.png" width="420" alt="Google Doc window on Linux, step 3">

<img src="screenshots-linux/options-en.png" width="420" alt="More options on Linux"> <img src="screenshots-linux/about-en.png" width="300" alt="About on Linux">

<img src="screenshots-linux/compare-en-copy.png" width="900" alt="Compare window on Linux">

## Design mockups for 0.11.0

Drawn before the code and approved as shown, against Nielsen's 10 heuristics, ISO 9241-110, WCAG 2.2 AA and Microsoft's Windows guidance for commands and dialogs. The font in the mockups is a stand-in.

### Start screen

<img src="mockup-0.11/mockup-start.png" width="900" alt="Mockup of the start screen">

### Results

<img src="mockup-0.11/mockup-results.png" width="900" alt="Mockup of the results view">

## The first mockup (23 Oct 2026)

What NACHO was first designed from. Some ideas are not built: the clipboard watcher, the first-run tour, the Docs-Outbox folder, tray notifications and "Start with Windows".

<img src="mockup/1-main-window.png" width="900" alt="First mockup of the main window with four result tiles and a page-fit preview">

<img src="mockup/2-compact-400px.png" width="300" alt="First mockup at 400 pixels wide"> <img src="mockup/4-more-options.png" width="540" alt="First mockup of More options">

<img src="mockup/3-first-run.png" width="720" alt="First mockup of the first run"> <img src="mockup/5-notifications.png" width="315" alt="First mockup of tray notifications">
