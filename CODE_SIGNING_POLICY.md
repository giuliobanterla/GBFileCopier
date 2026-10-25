# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## What is signed

The Windows release files of GBFileCopier: `GBFileCopier.exe` (desktop app), `gbcopy.exe` (command
line tool) and the installer `GBFileCopier-<version>-windows-x64-setup.exe`. Libraries made by others
and shipped unchanged (SkiaSharp, HarfBuzzSharp, ANGLE) are not signed with this certificate.

The macOS packages are signed with the maintainer's Apple Developer ID and notarized by Apple.

## How releases are built

Every signed file is built from the source code in this repository by
[GitHub Actions](.github/workflows/release.yml), from a version tag on the `main` branch. Changes reach
`main` only through pull requests that pass the automated build and tests on Windows and macOS.
Nobody builds or signs release files by hand.

## Team roles

The project currently has a single maintainer, who holds every role:

- Committers and reviewers: [Giulio Banterla](https://github.com/giuliobanterla)
- Approvers: [Giulio Banterla](https://github.com/giuliobanterla)

Every signing request is approved by hand, release by release. Team members use multi-factor
authentication for GitHub and SignPath.

## Privacy

This program will not transfer any information to other networked systems unless specifically
requested by the user or the person installing or operating it.

GBFileCopier copies the files the user selects to the destination the user chooses, which can be a
network share. It has no telemetry. It looks for new versions only if the user agrees when the app
asks the first time (the choice can be changed in Settings), or chooses *Check for Updates…*: it then
reads the latest release from the GitHub API (`api.github.com`), once a day at most for the automatic
checks, and downloads a new version from the project's GitHub releases only when the user clicks
*Install and restart*. Like any web request, these reveal the computer's IP address and the app
version to GitHub; nothing else is sent. It opens the project page on GitHub only when the user clicks
a link to it.
