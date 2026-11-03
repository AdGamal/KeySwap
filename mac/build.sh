#!/bin/bash
# Builds KeySwap.app (Apple Silicon + Intel) and KeySwap-mac.zip.  Usage: ./build.sh   or   ./build.sh test
# If your Command Line Tools SDK doesn't match your Swift compiler, pass one: SDK=/path/to/MacOSX.sdk ./build.sh
set -euo pipefail
cd "$(dirname "$0")"

SDK_ARGS=()
if [ -n "${SDK:-}" ]; then SDK_ARGS=(-sdk "$SDK"); fi
BUILD=build
mkdir -p "$BUILD"

# The ${x[@]+...} form keeps macOS's bash 3.2 happy when the list is empty.
swift_build() { swiftc -swift-version 5 -O ${SDK_ARGS[@]+"${SDK_ARGS[@]}"} "$@"; }

swift_build -o "$BUILD/tests" Sources/Converter.swift Sources/Layouts.swift Tests/main.swift
"$BUILD/tests"
[ "${1:-}" = "test" ] && exit 0

APP="$BUILD/KeySwap.app"
rm -rf "$APP" "$BUILD/KeySwap-mac.zip"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

SOURCES=(Sources/Converter.swift Sources/Layouts.swift Sources/main.swift)
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
echo "Built $APP and $BUILD/KeySwap-mac.zip"
