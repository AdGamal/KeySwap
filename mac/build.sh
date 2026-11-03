#!/bin/bash
# Builds KeySwap-mac.zip (Apple Silicon + Intel).  Usage: ./build.sh  |  ./build.sh test  |  ./build.sh install
# "install" also puts KeySwap in /Applications and opens it.
# If your Command Line Tools SDK doesn't match your Swift compiler, pass one: SDK=/path/to/MacOSX.sdk ./build.sh
set -euo pipefail
cd "$(dirname "$0")"

SDK_ARGS=()
if [ -n "${SDK:-}" ]; then SDK_ARGS=(-sdk "$SDK"); fi
BUILD=build
mkdir -p "$BUILD"

# The ${x[@]+...} form keeps macOS's bash 3.2 happy when the list is empty.
swift_build() { swiftc -swift-version 5 -O ${SDK_ARGS[@]+"${SDK_ARGS[@]}"} "$@"; }

swift_build -o "$BUILD/tests" Sources/Converter.swift Sources/AutoFix.swift Sources/CommonWords.swift Sources/Layouts.swift Tests/main.swift
"$BUILD/tests"
[ "${1:-}" = "test" ] && exit 0

APP="$BUILD/KeySwap.app"
rm -rf "$APP" "$BUILD/KeySwap-mac.zip"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

SOURCES=(Sources/Converter.swift Sources/AutoFix.swift Sources/CommonWords.swift Sources/Layouts.swift Sources/main.swift)
swift_build -target arm64-apple-macos11 -o "$BUILD/KeySwap-arm64" "${SOURCES[@]}"
swift_build -target x86_64-apple-macos11 -o "$BUILD/KeySwap-x86_64" "${SOURCES[@]}"
lipo -create -output "$APP/Contents/MacOS/KeySwap" "$BUILD/KeySwap-arm64" "$BUILD/KeySwap-x86_64"
cp Info.plist "$APP/Contents/Info.plist"

ICONSET="$BUILD/AppIcon.iconset"
rm -rf "$ICONSET" && mkdir -p "$ICONSET"
swift_build -o "$BUILD/make-icon" Tools/make-icon.swift
"$BUILD/make-icon" "$ICONSET"
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

codesign --force --deep --sign - "$APP"
ditto -c -k --keepParent "$APP" "$BUILD/KeySwap-mac.zip"
# Keep only the zip: a second KeySwap.app on disk confuses macOS about which copy to open and
# which copy has Accessibility permission.
rm -rf "$APP"
echo "Built $BUILD/KeySwap-mac.zip"

if [ "${1:-}" = "install" ]; then
  pkill -x KeySwap || true
  sleep 1
  rm -rf /Applications/KeySwap.app
  ditto -x -k "$BUILD/KeySwap-mac.zip" /Applications/
  open /Applications/KeySwap.app
  echo "Installed /Applications/KeySwap.app"
fi
