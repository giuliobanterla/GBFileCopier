---
name: release
description: Publish a new GBFileCopier release - version bump PR, merge, tag, then check the published packages. Use only when the maintainer runs /release.
argument-hint: <version, e.g. 0.4.0 or 0.4.0-beta.1>
disable-model-invocation: true
---

# Release GBFileCopier $ARGUMENTS

Pushing a `v*` tag runs `.github/workflows/release.yml`, which builds, signs and **publishes public
packages**. A published release cannot really be taken back: people may already have downloaded it.
So every step marked **STOP** needs an explicit yes from the maintainer in chat, for that step.
Answer in the language the maintainer uses.

## 0. Check the request

- The version is `$ARGUMENTS`, without a leading `v`, in the form `X.Y.Z` or `X.Y.Z-suffix`
  (a `-` makes a prerelease). If it is missing or malformed, ask for it and stop.
- `git fetch --tags`; the tag `v$ARGUMENTS` must not exist yet, and the version must be greater
  than the current `VersionPrefix` in `Directory.Build.props` and the latest release
  (`gh release list -L 3`).
- The working tree must be clean and `main` up to date. No open PR should be meant for this
  release: list them with `gh pr list` and ask if unsure.
- Show what goes in the release: `git log --oneline <latest tag>..origin/main`. Summarize it in a
  few lines for the maintainer.

## 1. Version bump PR

- Branch `release-$ARGUMENTS` from `origin/main`; set `<VersionPrefix>$ARGUMENTS</VersionPrefix>` in
  `Directory.Build.props` (nothing else).
- Commit `Set the version to $ARGUMENTS for the next release`, push, open the PR with `gh pr create`.
- Wait for CI: `gh run watch <run id> --exit-status` (checks `windows-x64`, `macos-arm64`,
  `macos-x64`). If it fails, report and stop.

**STOP** — ask: "CI green. Merge the bump PR?"

## 2. Merge

- `gh pr merge <n> --squash --delete-branch`, then `git checkout main && git pull`.
- Wait for the CI run of `main` on the merge commit to be green (`gh run list --branch main -L 1`,
  then `gh run watch`).

**STOP** — ask, naming the commit: "Create and push the tag `v$ARGUMENTS` on `<sha> <subject>`?
This publishes the release."

## 3. Tag

- `git tag -a v$ARGUMENTS -m "GBFileCopier $ARGUMENTS" <sha>` and `git push origin v$ARGUMENTS`.
- Follow the Release run: `gh run list --workflow Release -L 1`, then `gh run watch <id> --exit-status`.
  The macOS job includes notarization (usually a few minutes, up to ~30); when SignPath is
  configured, the Windows job may wait for a manual approval in SignPath: tell the maintainer.
- If a job fails, do not delete or move the tag on your own: report the failing step with
  `gh run view <id> --log-failed` and ask what to do.

## 4. Check the published release

In a new empty directory under the scratchpad:

- `gh release view v$ARGUMENTS` — title `GBFileCopier $ARGUMENTS`, prerelease only if the version has a `-`.
- `gh release download v$ARGUMENTS`; expected files: `GBFileCopier-$ARGUMENTS-macos.dmg`,
  `gbcopy-$ARGUMENTS-macos.tar.gz`, `GBFileCopier-$ARGUMENTS-windows-x64-setup.exe`,
  `GBFileCopier-$ARGUMENTS-windows-x64.zip`, `SHA256SUMS.txt`.
- `shasum -a 256 -c SHA256SUMS.txt` — all OK.
- macOS: `xcrun stapler validate GBFileCopier-$ARGUMENTS-macos.dmg`; mount it
  (`hdiutil attach -nobrowse -readonly`), check `CFBundleShortVersionString` in the app's
  `Info.plist` is the numeric part of `$ARGUMENTS` (`packaging/macos/build.sh` drops any `-suffix`), and
  `spctl -a -vv -t exec <app>` says "Notarized Developer ID"; then `hdiutil detach`.
- Windows signatures can only be checked on Windows (the workflow already runs
  Get-AuthenticodeSignature when SignPath is configured); just report whether signing was enabled
  in the run log ("Signing with SignPath" / "Not signing").

## 5. Report

Give the maintainer: the release URL, the direct download links of the four packages, the result of
each check, and anything unusual. If a project memory tracks releases or download links, update it.
