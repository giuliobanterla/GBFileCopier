# GBFileCopier

[![CI](https://github.com/giuliobanterla/GBFileCopier/actions/workflows/ci.yml/badge.svg)](https://github.com/giuliobanterla/GBFileCopier/actions/workflows/ci.yml)

A free and open source file copy utility for **Windows** and **macOS**, inspired by TeraCopy.

> ⚠️ Early development — nothing usable yet.

## Planned features

- Fast copy and move, with asynchronous I/O and a queue per destination drive
- Integrity verification with checksums (xxHash, CRC32, MD5, SHA-1, SHA-256), computed while copying
- Errors don't stop the job: failed files are skipped, listed and can be retried
- Pause and resume of long transfers
- Rules for name conflicts (overwrite, overwrite if older, skip, rename, keep both)
- Safe move: sources are deleted only after the copy has been verified
- Preservation of metadata (timestamps, attributes, extended attributes, ACLs)
- Instant clones on APFS (macOS)
- Transfer history and exportable reports
- Command line interface for scripts and scheduled tasks
- Shell integration (Explorer context menu on Windows, Finder extension on macOS)

## Installation

Download the packages from the [Releases](https://github.com/giuliobanterla/GBFileCopier/releases) page.

- **macOS** (13 or later, Apple Silicon and Intel): open `GBFileCopier-<version>-macos.dmg` and
  drag GBFileCopier to Applications. Until the app is notarized, macOS blocks the first start: open
  System Settings → Privacy & Security and choose **Open Anyway** (or right-click the app → Open).
  The command line tool is in `gbcopy-<version>-macos.tar.gz`.
- **Windows** (10 or later, x64): run `GBFileCopier-<version>-windows-x64-setup.exe`; it installs
  for the current user without administrator rights (or for all users, if you choose so), with a
  Start menu entry and `gbcopy.exe` next to the app. SmartScreen may warn about an unknown publisher
  until the installer is signed: choose *More info* → *Run anyway*. A portable `.zip` is also available.

`SHA256SUMS.txt` lists the checksums of every file.

### Building the packages

```
packaging/macos/build.sh [version]                 # on macOS: .dmg (universal) and gbcopy .tar.gz
pwsh packaging/windows/build.ps1 [-Version x.y.z]  # on Windows, with Inno Setup 6: installer and .zip
```

Pushing a tag such as `v0.1.0` builds both on GitHub Actions and publishes a release. The macOS
app is signed ad-hoc unless the repository has the secrets `MACOS_CERTIFICATE` (Developer ID
Application certificate, .p12 in base64) and `MACOS_CERTIFICATE_PASSWORD`; with `APPLE_ID`,
`APPLE_TEAM_ID` and `APPLE_APP_PASSWORD` it is also notarized.

## Desktop app

`GBFileCopier` (Windows and macOS): drop files and folders in the window, or paste them with
⌘V / Ctrl+V after copying them in the Finder or Explorer (⌥⌘V, or a cut in Explorer, makes Move
the default action). Choose the destination, then Copy or Move. While copying you can pause and
cancel; at the end the results list shows what failed or was skipped, and why. When a file already
exists you can replace it, skip it or keep both, also for all the following conflicts.

Files and folders passed on the command line are added to the list.

The app remembers the last destination, the options and the window size, in
`~/Library/Application Support/GBFileCopier/settings.json` (macOS) or
`%APPDATA%\GBFileCopier\settings.json` (Windows). On macOS About and Settings (⌘,) are in the
application menu; on Windows in the File and Help menus.

The app follows the light or dark appearance of the system; Settings can force light or dark
instead, with immediate effect.

### Finder extension (macOS)

The app includes a Finder extension that adds GBFileCopier to the context menu:

- on selected files and folders: **Copy or Move with GBFileCopier…** — they become the sources;
- on the background of a folder, when files have been copied with ⌘C: **Paste Here** and
  **Move Here with GBFileCopier** — the folder becomes the destination.

Enable it once in System Settings → General → Login Items & Extensions → Extensions → Finder
(GBFileCopier must be in Applications and opened at least once). The extension only fills in the
window; the copy starts when you press Copy or Move. The same requests can be sent with links like
`gbfilecopier://add?source=/path` and `gbfilecopier://paste?destination=/folder&move=1`, and files
can be dropped on the Dock icon. To see what the app receives:
`open --env GBFILECOPIER_LOG=/tmp/gbfilecopier.log -a GBFileCopier`.

### Languages

The app is available in **English** and **Italian**. It follows the system language (on macOS the
first of the preferred languages, not the region), unless another one is chosen in Settings;
`GBFILECOPIER_LANG` (for example `GBFILECOPIER_LANG=it`) overrides both. The command line stays in English, so that its output is stable
for scripts.

Translations live in `src/GBFileCopier.App/Resources/Strings.<language>.resx` (interface) and
`src/GBFileCopier.Core/Resources/Messages.<language>.resx` (error messages). To add a language,
copy the two English `.resx` files with the language code in the name and translate the values;
the tests check that no text is missing and that the `{0}` placeholders match.

## Command line

```
gbcopy copy <sources>... <destination> [options]
gbcopy move <sources>... <destination> [options]
gbcopy paste [<destination>] [--move | --copy] [options]
gbcopy hash <files>... [--hash <algorithm>]
```

| Option | |
|---|---|
| `-v`, `--verify` | Read every copy back and compare its checksum with the source |
| `--hash <algorithm>` | `none`, `crc32`, `crc64`, `xxh64`, `xxh3` (default), `xxh128`, `md5`, `sha1`, `sha256`, `sha512` |
| `-c`, `--on-conflict <policy>` | `ask` (default when interactive), `overwrite`, `newer`, `skip` (default otherwise), `keep-both` |
| `--retries <n>` | Attempts after a transient error (default 2) |
| `--no-flush` | Don't force each file to disk (faster, less safe) |

Each file is written to a hidden temporary name, checked, and only then renamed into place:
an interrupted copy never leaves partial files and never damages an existing one. With `move`,
a source is deleted only after its copy has succeeded.

On Windows and macOS, native APIs make the safety checks real:

- **Verification reads from the disk**, not from the memory cache that still holds the data just
  written (`FILE_FLAG_NO_BUFFERING` on Windows; cache eviction plus `F_NOCACHE` on macOS).
- **Flushing reaches permanent storage** (`FlushFileBuffers`; `F_FULLFSYNC` on macOS, which also
  empties the drive's own cache).
- **Moves on the same volume are instant renames**; across volumes they become copy, verify, delete.

### Copy (or cut) in the file manager, paste with GBFileCopier

`gbcopy paste` takes the files from the system clipboard, as copied in the Finder or in Explorer,
and pastes them into the destination (the current folder by default) with all the checks above.

- **macOS**: the Finder has no "cut" for files; it decides when pasting (⌘V copies, ⌥⌘V moves).
  So: copy with ⌘C in the Finder, then `gbcopy paste` to copy or `gbcopy paste --move` to move.
- **Windows**: cut (Ctrl+X) in Explorer is recognized and the files are moved; `--copy` copies them
  anyway. After a successful cut and paste the clipboard is emptied, as Explorer does.

Exit codes: `0` success, `1` some files failed, `3` cancelled.

## Technology

- .NET (C#), Native AOT
- [Avalonia UI](https://avaloniaui.net/) for the cross-platform interface
- Native components for shell integration (Swift for the Finder extension)

## Repository layout

```
src/GBFileCopier.Core       copy engine: queue, copy + hash pipeline, conflicts, retry, verification (no UI)
src/GBFileCopier.Platform   native APIs for Windows and macOS
src/GBFileCopier.Cli        command line tool (gbcopy)
src/GBFileCopier.App        desktop app (Avalonia)
tests/                      unit tests (xUnit v3); the App tests also render the windows to PNG
assets/icon/                app icon in every format, generated by tools/MakeIcons.cs
```

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build
dotnet test
dotnet run --project src/GBFileCopier.Cli -- --help
dotnet run --project src/GBFileCopier.App
```

Native AOT build of the CLI (single executable, no runtime required):

```
dotnet publish src/GBFileCopier.Cli -c Release
```

## License

Copyright (C) 2026 Giulio Banterla

This program is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License as published by the Free Software Foundation, either version 3 of the
License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without
even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
[LICENSE](LICENSE) file for details.

TeraCopy is a trademark of its respective owner. This project is not affiliated with it.
