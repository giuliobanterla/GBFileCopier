# Code signing policy

## What is signed

The macOS packages are signed with the maintainer's Apple Developer ID and notarized by Apple: the
app, its Finder extension and libraries, the disk image and the `gbcopy` command line tool.

The Windows release files are **not signed** for now: Windows SmartScreen may warn about an unknown
publisher when the installer is first run. Their integrity can be checked against `SHA256SUMS.txt`,
published with every release. The release workflow is ready to sign them with an Authenticode
certificate once the project has one.

## How releases are built

Every release file is built from the source code in this repository by
[GitHub Actions](.github/workflows/release.yml), from a version tag on the `main` branch, and signed
there (macOS). Changes reach `main` only through pull requests that pass the automated build and tests
on Windows and macOS. Nobody builds or signs release files by hand.

## Team roles

The project currently has a single maintainer, who holds every role:

- Committers and reviewers: [Giulio Banterla](https://github.com/giuliobanterla)
- Approvers: [Giulio Banterla](https://github.com/giuliobanterla)

Releases are published by hand, one version tag at a time. Team members use multi-factor
authentication for GitHub and Apple.

## Privacy

This program will not transfer any information to other networked systems unless specifically
requested by the user or the person installing or operating it.

GBFileCopier copies the files the user selects to the destination the user chooses, which can be a
network share. It has no telemetry. It looks for new versions only if the user agrees when the app
asks the first time (the choice can be changed in Settings), or chooses *Check for Updates…*: it then
reads the latest release from the GitHub API (`api.github.com`), once a day at most for the automatic
checks, and downloads a new version from the project's GitHub releases only when the user clicks
*Install and restart*. Like any web request, these reveal the computer's IP address and the app
version to GitHub; nothing else is sent. It opens the project page on GitHub, or the donation page on
PayPal, only when the user clicks a link to it.
