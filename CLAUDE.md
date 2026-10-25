# GBFileCopier

Reliable file copy and move for Windows and macOS, an open source alternative to TeraCopy.
.NET 10 (C#), Native AOT, Avalonia 12 UI, GPL-3.0. Public repository: everything committed here
is public, so no personal data, hostnames or secrets.

## Layout

```
src/GBFileCopier.Core       copy engine (queue, copy + hash pipeline, conflicts, retry, verification), no UI
src/GBFileCopier.Platform   native APIs for Windows and macOS (clipboard, fsync, rename moves, …)
src/GBFileCopier.Cli        command line tool (gbcopy)
src/GBFileCopier.App        desktop app (Avalonia + CommunityToolkit.Mvvm)
tests/                      xUnit v3; App tests use Avalonia.Headless and render the windows to PNG
packaging/macos             build.sh (universal .dmg, gbcopy .tar.gz), Finder Sync extension (Swift)
packaging/windows           build.ps1 + Inno Setup script, SignPath artifact configurations
tools/MakeIcons.cs          generates assets/icon/
```

## Build and test

```bash
dotnet build
dotnet test
dotnet run --project src/GBFileCopier.Cli -- --help
dotnet publish src/GBFileCopier.Cli -c Release   # Native AOT
```

- `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended` and `EnforceCodeStyleInBuild` are on:
  a style or analyzer warning breaks the build. Follow `.editorconfig` (file-scoped namespaces,
  braces, System usings first). A PostToolUse hook (`.claude/hooks/dotnet-format.sh`) runs
  `dotnet format` on each edited `.cs` file (a few seconds per edit, needs `jq`).
- Everything under `src/` must stay Native AOT compatible (`IsAotCompatible`): no unannotated
  reflection, JSON only through source-generated contexts.
- Package versions live only in `Directory.Packages.props` (central package management).
- `dotnet test` does not rebuild the CLI: rebuild `src/GBFileCopier.Cli` before checking it by hand.
- The App tests write PNG renders to `tests/GBFileCopier.App.Tests/bin/*/net10.0/screenshots/`:
  use them to check the UI instead of scripting the real windows.

## Platforms

CI (`.github/workflows/ci.yml`) builds and tests on `windows-x64`, `macos-arm64` and `macos-x64`;
these three checks are required. The maintainer's Mac is Intel x64: locally only macOS x64 code
runs, Windows and arm64 paths are verified only by CI. Keep platform code behind
`OperatingSystem.IsWindows()` / `IsMacOS()` and think through both branches.

## Localization

English and Italian. Interface texts: `src/GBFileCopier.App/Resources/Strings.resx`,
`Strings.it.resx` and a property in `Strings.cs` (used from XAML as `{x:Static res:Strings.Name}`).
Error messages: `src/GBFileCopier.Core/Resources/Messages*.resx` + `Messages.cs`. Every new text
goes in all the files; the tests check that no translation is missing and that `{0}` placeholders
match. The CLI output stays in English.

## Workflow

- `main` is protected: work on a branch, open a PR with `gh`, wait for green CI
  (`gh run watch <id> --exit-status`). Merge (squash) only when the maintainer asks.
- Never push to `main` directly. Never create or push a tag, or create/edit a release, without an
  explicit yes: a `v*` tag runs `.github/workflows/release.yml`, which publishes signed public
  packages. `.claude/settings.json` asks for confirmation on these commands.
- Release with `/release <version>` (`.claude/skills/release/SKILL.md`): PR bumping `VersionPrefix` in `Directory.Build.props` → merge → annotated tag `vX.Y.Z`
  on that commit → check the published assets against `SHA256SUMS.txt`, and the macOS notarization
  (`xcrun stapler validate`, `spctl -a -vv -t exec` → "Notarized Developer ID").
- PRs touching `packaging/**` run the release workflow in build-only mode; its artifacts can be
  used to test unreleased packages.
- Code, comments, commits and PR texts are in English.
