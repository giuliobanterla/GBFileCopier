#!/bin/bash
# Builds the macOS packages: GBFileCopier.app (universal: Apple Silicon + Intel) in a .dmg,
# and the gbcopy command line tool (universal) in a .tar.gz.
#
#   packaging/macos/build.sh [version] [output folder]
#
# Signing: ad-hoc by default (the app runs, but Gatekeeper warns on first start).
# With these environment variables it is signed with a Developer ID and notarized:
#   MACOS_SIGN_IDENTITY   "Developer ID Application: Name (TEAMID)", already in the keychain
#   APPLE_ID, APPLE_TEAM_ID, APPLE_APP_PASSWORD   notarization (app-specific password)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="${1:-$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' "$ROOT/Directory.Build.props")}"
# Bundle versions must be numeric: 0.2.0-beta.1 -> 0.2.0
NUMERIC_VERSION="${VERSION%%[-+]*}"
OUT="$(mkdir -p "${2:-$ROOT/artifacts}" && cd "${2:-$ROOT/artifacts}" && pwd)"
WORK="$OUT/macos-work"
APP="$WORK/GBFileCopier.app"
IDENTITY="${MACOS_SIGN_IDENTITY:--}"

echo "==> GBFileCopier $VERSION for macOS"
rm -rf "$WORK"
mkdir -p "$WORK"

# 1. Native AOT builds for both architectures (cross-compiling works in both directions)
for rid in osx-arm64 osx-x64; do
  for project in GBFileCopier.App GBFileCopier.Cli; do
    dotnet publish "$ROOT/src/$project" -c Release -r "$rid" -p:Version="$VERSION" \
      -o "$WORK/$rid/$project" -nologo -v quiet
  done
done

# 2. Universal executables; the native libraries from NuGet are already universal
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
lipo -create "$WORK/osx-arm64/GBFileCopier.App/GBFileCopier" "$WORK/osx-x64/GBFileCopier.App/GBFileCopier" \
  -output "$APP/Contents/MacOS/GBFileCopier"
lipo -create "$WORK/osx-arm64/GBFileCopier.Cli/gbcopy" "$WORK/osx-x64/GBFileCopier.Cli/gbcopy" -output "$WORK/gbcopy"
for lib in "$WORK"/osx-arm64/GBFileCopier.App/*.dylib; do
  lipo "$lib" -verify_arch arm64 x86_64
  cp "$lib" "$APP/Contents/MacOS/"
done

# 3. Bundle resources: icon, and one folder per language so that macOS lists the app under
#    System Settings → Language & Region → Applications and localizes its system dialogs
cp "$ROOT/assets/icon/icon.icns" "$APP/Contents/Resources/GBFileCopier.icns"
for language in en it; do
  mkdir -p "$APP/Contents/Resources/$language.lproj"
  printf '"CFBundleDisplayName" = "GBFileCopier";\n' > "$APP/Contents/Resources/$language.lproj/InfoPlist.strings"
done
sed -e "s/@VERSION@/$NUMERIC_VERSION/g" -e "s/@YEAR@/$(date +%Y)/g" "$ROOT/packaging/macos/Info.plist" > "$APP/Contents/Info.plist"
plutil -lint "$APP/Contents/Info.plist" >/dev/null

# 4. Signing: libraries first, then the executables, then the bundle (no --deep: it is deprecated)
sign() {
  if [ "$IDENTITY" = "-" ]; then
    codesign --force --sign - "$@"
  else
    codesign --force --sign "$IDENTITY" --options runtime --timestamp "$@"
  fi
}
for file in "$APP"/Contents/MacOS/*.dylib; do sign "$file"; done
sign "$APP/Contents/MacOS/GBFileCopier"
sign "$APP"
sign "$WORK/gbcopy"
codesign --verify --strict "$APP"

# 5. Disk image with a link to Applications, for drag-and-drop installation
DMG="$OUT/GBFileCopier-$VERSION-macos.dmg"
mkdir -p "$WORK/dmg"
cp -R "$APP" "$WORK/dmg/"
ln -s /Applications "$WORK/dmg/Applications"
rm -f "$DMG"
hdiutil create -volname "GBFileCopier $VERSION" -srcfolder "$WORK/dmg" -ov -format UDZO "$DMG" -quiet
[ "$IDENTITY" = "-" ] || sign "$DMG"

# 6. Command line tool
CLI="$OUT/gbcopy-$VERSION-macos.tar.gz"
tar -czf "$CLI" -C "$WORK" gbcopy

# 7. Notarization, only with a Developer ID and the credentials
if [ "$IDENTITY" != "-" ] && [ -n "${APPLE_ID:-}" ] && [ -n "${APPLE_TEAM_ID:-}" ] && [ -n "${APPLE_APP_PASSWORD:-}" ]; then
  echo "==> Notarizing"
  xcrun notarytool submit "$DMG" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD" --wait
  xcrun stapler staple "$DMG"
else
  echo "==> Not notarized (no Developer ID or credentials)"
fi

rm -rf "$WORK"
echo "==> $DMG"
echo "==> $CLI"
