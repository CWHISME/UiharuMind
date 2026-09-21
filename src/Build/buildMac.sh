#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"
source common.sh

TARGET_RUNTIME="osx-arm64"
PUBLISH_OUTPUT_DIRECTORY="Tmp/Mac"
APP_NAME="Output/UiharuMind.app"
INFO_PLIST="Info.plist"
ICON_FILE="../UiharuMind/Assets/Icon.png"

APP_VERSION=$(resolve_version)
publish_for "$TARGET_RUNTIME" "$PUBLISH_OUTPUT_DIRECTORY"

# 组 .app bundle
rm -rf "$APP_NAME"
mkdir -p "$APP_NAME/Contents/MacOS" "$APP_NAME/Contents/Resources"

TEMP_INFO_PLIST=$(mktemp)
cp "$INFO_PLIST" "$TEMP_INFO_PLIST"
/usr/libexec/PlistBuddy -c "Set CFBundleVersion $APP_VERSION" "$TEMP_INFO_PLIST"
/usr/libexec/PlistBuddy -c "Set CFBundleShortVersionString $APP_VERSION" "$TEMP_INFO_PLIST"
cp "$TEMP_INFO_PLIST" "$APP_NAME/Contents/Info.plist"
rm "$TEMP_INFO_PLIST"

cp "$ICON_FILE" "$APP_NAME/Contents/Resources/Icon.png"
cp -a "$PUBLISH_OUTPUT_DIRECTORY/." "$APP_NAME/Contents/MacOS"

# 签名必须是最后一步：签完再往 bundle 里拷任何东西都会让密封失效。
# buildMacFull.sh 在它之后还要拷模型，所以那边会重新调一次 sign_bundle
sign_bundle "$APP_NAME"
package_bundle "$APP_NAME"
