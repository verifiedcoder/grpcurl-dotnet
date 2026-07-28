#!/usr/bin/env bash
#
# make-macos-app.sh — wrap a self-contained Studio publish directory into a
# double-clickable `GrpCurl.Net Studio.app` bundle and ad-hoc codesign it.
#
# Ad-hoc signing (`codesign -s -`) is free and is what lets an arm64 build run
# at all once the user clears Gatekeeper quarantine (xattr -dr com.apple.quarantine).
# It is NOT notarization — there is no paid Apple Developer identity here.
#
# Usage: make-macos-app.sh <publish-dir> <version> <output-app-path>
#   <publish-dir>      a `dotnet publish` output folder (osx-x64 / osx-arm64, self-contained)
#   <version>          X.Y.Z (no leading v)
#   <output-app-path>  e.g. .../dist/GrpCurl.Net Studio.app
#
# `codesign` only runs on macOS; on other hosts the bundle is assembled but left
# unsigned (the real release runs this step on a macOS runner).
set -euo pipefail

PUBLISH_DIR="$1"
VERSION="$2"
APP_PATH="$3"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

MAIN_ASSEMBLY="GrpCurl.Net.Studio"     # AssemblyName -> apphost name in the publish dir
BUNDLE_ID="net.grpcurl.studio"

if [ ! -d "$PUBLISH_DIR" ]; then
  echo "make-macos-app: publish dir not found: $PUBLISH_DIR" >&2
  exit 1
fi
if [ ! -f "$PUBLISH_DIR/$MAIN_ASSEMBLY" ]; then
  echo "make-macos-app: apphost '$MAIN_ASSEMBLY' missing in $PUBLISH_DIR" >&2
  exit 1
fi

rm -rf "$APP_PATH"
mkdir -p "$APP_PATH/Contents/MacOS" "$APP_PATH/Contents/Resources"

# Payload: the entire self-contained publish output lives in Contents/MacOS,
# with the apphost as CFBundleExecutable.
cp -R "$PUBLISH_DIR/." "$APP_PATH/Contents/MacOS/"
rm -f "$APP_PATH/Contents/MacOS/"*.pdb || true

cat > "$APP_PATH/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>            <string>GrpCurl.Net Studio</string>
  <key>CFBundleDisplayName</key>     <string>GrpCurl.Net Studio</string>
  <key>CFBundleIdentifier</key>      <string>${BUNDLE_ID}</string>
  <key>CFBundleVersion</key>         <string>${VERSION}</string>
  <key>CFBundleShortVersionString</key> <string>${VERSION}</string>
  <key>CFBundleExecutable</key>      <string>${MAIN_ASSEMBLY}</string>
  <key>CFBundlePackageType</key>     <string>APPL</string>
  <key>LSMinimumSystemVersion</key>  <string>13.0</string>
  <key>NSHighResolutionCapable</key> <true/>
  <key>LSApplicationCategoryType</key> <string>public.app-category.developer-tools</string>
</dict>
</plist>
PLIST

chmod +x "$APP_PATH/Contents/MacOS/$MAIN_ASSEMBLY"

# Legal material lives in Contents/Resources, the conventional place in a .app bundle (PRD-002).
# Copied before signing so the ad-hoc signature covers it.
if [ ! -f "$ROOT/THIRD-PARTY-NOTICES.md" ]; then
  echo "make-macos-app: THIRD-PARTY-NOTICES.md missing — run Scripts/package/generate-third-party-notices.sh" >&2
  exit 1
fi
cp "$ROOT/LICENSE" "$ROOT/THIRD-PARTY-NOTICES.md" "$APP_PATH/Contents/Resources/"

if command -v codesign >/dev/null 2>&1; then
  echo "make-macos-app: ad-hoc signing $APP_PATH"
  codesign --force --deep --sign - --timestamp=none "$APP_PATH"
  codesign --verify --deep --strict "$APP_PATH" && echo "make-macos-app: ad-hoc signature verified"
else
  echo "make-macos-app: codesign unavailable (non-macOS host) — bundle left unsigned" >&2
fi

echo "make-macos-app: created $APP_PATH"
