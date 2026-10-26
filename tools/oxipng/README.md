# oxipng 10.2.1 (bundled)

NACHO runs oxipng after writing a PNG: same pixels, about 5% fewer bytes on screenshots
(about 25% on full-colour PNGs). See the compression study in the project notes.

- Project: https://github.com/oxipng/oxipng (MIT licence, see LICENSE in this folder)
- Windows x64: oxipng-10.2.1-x86_64-pc-windows-msvc.zip
  archive SHA-256 7e940f83ee46874b73f53031f96a15834cb70b220af27391fb06fe7b4dd798e1
  oxipng.exe SHA-256 35ae3980ab831af64f4aecc98f69b81bfa146fa750a61d17ebbb12520128cbc8
- Linux x64 (static, runs on any distro): oxipng-10.2.1-x86_64-unknown-linux-musl.tar.gz
  archive SHA-256 1813750ef592c5350ca79c88f98b2c0876d05c826dc7156245256d4255c1ad17
  oxipng SHA-256 df8f3e79e6ef06d8515d4949d19d872ae4e82fe16bc1fe6ad1efe181e3a21780
- Downloaded from https://github.com/oxipng/oxipng/releases/tag/v10.2.1 on 2026-10-26. The Linux
  build gave byte-identical results to oxipng 10.2.1 built from crates.io on 19 test screenshots.

The apps embed these files and unpack them once to a cache folder:
Windows %LOCALAPPDATA%\NACHO\tools, Linux ~/.cache/nacho/tools.
