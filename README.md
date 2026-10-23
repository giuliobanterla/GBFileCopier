# GBFileCopier

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
tests/                      unit tests (xUnit v3)
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
