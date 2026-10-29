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
  drag GBFileCopier to Applications. From version 0.3.1 the app is signed with a Developer ID and
  notarized by Apple, so it opens without warnings (up to 0.3.0 macOS blocked the first start: System
  Settings → Privacy & Security → **Open Anyway**). The command line tool is in
  `gbcopy-<version>-macos.tar.gz`, signed as well.
- **Windows** (10 or later, x64): run `GBFileCopier-<version>-windows-x64-setup.exe`; it installs
  for the current user without administrator rights (or for all users, if you choose so), with a
  Start menu entry and `gbcopy.exe` next to the app. The installer is not signed, so SmartScreen
  may warn about an unknown publisher: choose *More info* → *Run anyway*. A portable `.zip` is also available.

`SHA256SUMS.txt` lists the checksums of every file. How the release files are built and signed:
[Code signing policy](CODE_SIGNING_POLICY.md).

### Building the packages

```
packaging/macos/build.sh [version]                 # on macOS: .dmg (universal) and gbcopy .tar.gz
pwsh packaging/windows/build.ps1 [-Version x.y.z]  # on Windows, with Inno Setup 6: installer and .zip
```

Pushing a tag such as `v0.1.0` builds both on GitHub Actions and publishes a release. The release
builds are signed with the project's Developer ID and notarized. Locally, or in a fork, the macOS
app is signed ad-hoc unless these secrets are available: `MACOS_CERTIFICATE` (Developer ID
Application certificate, .p12 in base64) and `MACOS_CERTIFICATE_PASSWORD`; with `APPLE_ID`,
`APPLE_TEAM_ID` and `APPLE_APP_PASSWORD` it is also notarized.

## Desktop app

`GBFileCopier` (Windows and macOS): drop files and folders in the window, or paste them with
⌘V / Ctrl+V after copying them in the Finder or Explorer (⌥⌘V, or a cut in Explorer, makes Move
the default action). Choose the destination, then Copy or Move. When a file already exists you can
replace it, skip it or keep both, also for all the following conflicts.

Each Copy or Move becomes a job in the **Jobs** list, and the window is ready for the next one, also
while jobs are running (files sent from the Finder or Explorer are taken in the meantime too). Jobs
to the same destination drive run one after the other, in the order they were added, because two
copies to one disk at once are slower than one after the other; jobs to different drives run at the
same time. Each job can be paused or cancelled, also while it waits for its drive; select a finished
job to see its results: what failed or was skipped, and why. Closing the window cancels the jobs
still waiting or running.

Files and folders passed on the command line are added to the list.

The app remembers the last destination, the options and the window size, in
`~/Library/Application Support/GBFileCopier/settings.json` (macOS) or
`%APPDATA%\GBFileCopier\settings.json` (Windows). On macOS About and Settings (⌘,) are in the
application menu; on Windows in the File and Help menus.

The app follows the light or dark appearance of the system; Settings can force light or dark
instead, with immediate effect.

### Updates

At the first start the app asks whether it may look for new versions by itself; if you agree, it asks
GitHub once a day (the choice can be changed in Settings, and *Check for Updates…* in the Help menu,
or in the application menu on macOS, checks at any time). When a new version is out, a bar at the top
of the window offers **Install and restart**: the app downloads the package for your system, checks
it against the release's `SHA256SUMS.txt` and its signature (on macOS: signed with the project's
Developer ID and notarized; on Windows, once releases are signed, by the same publisher as the
installed app), then installs it and opens the new version: on Windows the installer runs silently, for the same
user or for all users as before (then Windows asks for administrator rights), and closes the app
itself; on macOS the app quits and is replaced in Applications.
Updates wait until no job is running. The portable `.zip` on Windows, and a copy of the app on macOS
in a folder you cannot write to, show **Download…** instead, which opens the release page. To try
the update with an older version number: `GBFILECOPIER_PRETEND_VERSION=0.1.0`.

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

### Explorer context menu (Windows)

The installer (option checked by default) adds to the Explorer context menu — on Windows 11 under
*Show more options* — the same entries as the Finder extension: **Copy or Move with GBFileCopier…**
on files and folders, **Paste Here** and **Move Here with GBFileCopier** on the background of a
folder, plus GBFileCopier in **Send to**. Explorer starts the app once per selected item: the
first start opens the window and the others hand their items over to it, so they all end up in the
same list. As on macOS, the window is only filled in: the copy starts when you press Copy or Move.

The same works from a command prompt: `GBFileCopier --add <paths>`, `GBFileCopier --paste [folder] [--move]`.

### Ctrl+V in Explorer (Windows)

With *Use GBFileCopier for Ctrl+V in Explorer* in Settings, files copied (Ctrl+C) or cut (Ctrl+X) in
Explorer and pasted with **Ctrl+V** in an Explorer window are copied or moved by GBFileCopier, at
once, with the options of the main window. GBFileCopier then keeps running in the notification area
(closing its window hides it; *Exit* there quits) and starts when you sign in to Windows.

It works with a keyboard hook in GBFileCopier itself: nothing is loaded into Explorer. Only plain
Ctrl+V in Explorer's list of files with files on the clipboard is taken; text, renaming, the address
and search boxes are untouched. When GBFileCopier cannot paste there — a virtual folder such as
*This PC*, or the folder the files come from (where Explorer makes "- Copy" files) — Ctrl+V goes
back to Explorer. Dragging files with the mouse is still done by Explorer.

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
| `--flush-network` | Force each file to disk also on network shares (much slower with many small files) |
| `--parallel <n>` | Small files (under 16 MB) copied at the same time (default 4; 1 copies one file at a time) |

Each file is written to a hidden temporary name, checked, and only then renamed into place:
an interrupted copy never leaves partial files and never damages an existing one. With `move`,
a source is deleted only after its copy has succeeded.

On Windows and macOS, native APIs make the safety checks real:

- **Verification reads from the disk**, not from the memory cache that still holds the data just
  written (`FILE_FLAG_NO_BUFFERING` on Windows; cache eviction plus `F_NOCACHE` on macOS).
- **Flushing reaches permanent storage** (`FlushFileBuffers`; `F_FULLFSYNC` on macOS, which also
  empties the drive's own cache). On macOS a copy gives each file a write barrier
  (`F_BARRIERFSYNC`: its data reaches the disk before it gets its final name, so a power cut never
  leaves a damaged file under that name) and flushes each destination drive once at the end of the
  job, also when it is cancelled (a failure is reported on the job): copies of
  many small files are about ten times faster than with a full flush per file (2,000 small files:
  4 s instead of 45 s). Moves keep a full flush per file, as each source is deleted right after its
  copy. On network shares (a NAS) files are not flushed one by one by
  default: the server must then write each file to its disks before answering, which made copies
  of many small files several times slower (150 photos of 2 MB to a Synology: 120 s instead of
  27 s). The NAS writes its cache to disk by itself within seconds; `--flush-network`, or the
  option in Settings, flushes every file there too.
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

## Code signing policy

macOS releases: signed with the maintainer's Developer ID and notarized by Apple. Windows releases are
not signed for now (check them against `SHA256SUMS.txt`). Details, team roles and privacy:
[CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).

## Support

GBFileCopier is free. If it is useful to you, you can support its development with a donation:
[paypal.me/GiulioBanterla](https://www.paypal.me/GiulioBanterla) (also from *Help → Donate with PayPal…* and
*About* in the app). Bug reports
and ideas are welcome in the [issues](https://github.com/giuliobanterla/GBFileCopier/issues).

## License

Copyright (C) 2026 Giulio Banterla

This program is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License as published by the Free Software Foundation, either version 3 of the
License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without
even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
[LICENSE](LICENSE) file for details.

TeraCopy is a trademark of its respective owner. This project is not affiliated with it.
