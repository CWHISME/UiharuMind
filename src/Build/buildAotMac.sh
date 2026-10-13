#!/bin/bash
# AOT 发布构建：AOT publish + 组 .app bundle + 签名 + 打包（Q5 固化，让「完整 AOT 版本」可复现）。
# 与 buildMac.sh（R2R）并行的另一条发布通道；产物 UiharuMind-AOT.app，存档带 -AOT 后缀不覆盖 R2R。
#
# AOT 开关：PublishAot 在 Desktop.csproj 里按 RID + PublishAotOverride 条件开（防属性传播到
# netstandard 生成器）；AOT 与 ReadyToRun 互斥，这里显式关 R2R。
# 签名必须是最后一步（common.sh：自签名证书，bundle id 与 R2R 相同 → TCC 授权跨版本稳定）。
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"
source common.sh

TARGET_RUNTIME="osx-arm64"
AOT_PUBLISH_OUTPUT_DIRECTORY="Tmp/Mac-Aot"
APP_NAME="Output/UiharuMind-AOT.app"
INFO_PLIST="Info.plist"
ICON_FILE="../UiharuMind/Assets/Icon.png"

APP_VERSION=$(resolve_version)

# AOT publish（不跑 common.sh 的 publish_for——那是 R2R 口径）
rm -rf "$AOT_PUBLISH_OUTPUT_DIRECTORY"
dotnet publish "$DESKTOP_PROJECT" --output "$AOT_PUBLISH_OUTPUT_DIRECTORY" -r "$TARGET_RUNTIME" \
    --configuration Release -p:PublishAotOverride=true -p:PublishReadyToRun=false

# 组 .app bundle：可执行 + 依赖 dylib + runtimes 进 Contents/MacOS；
# pdb/dSYM 是调试符号，不进发布包（AOT 原生单文件 128MB，dylib 是 Skia/P/Invoke 依赖）
rm -rf "$APP_NAME"
mkdir -p "$APP_NAME/Contents/MacOS" "$APP_NAME/Contents/Resources"

TEMP_INFO_PLIST=$(mktemp)
cp "$INFO_PLIST" "$TEMP_INFO_PLIST"
/usr/libexec/PlistBuddy -c "Set CFBundleVersion $APP_VERSION" "$TEMP_INFO_PLIST"
/usr/libexec/PlistBuddy -c "Set CFBundleShortVersionString $APP_VERSION" "$TEMP_INFO_PLIST"
cp "$TEMP_INFO_PLIST" "$APP_NAME/Contents/Info.plist"
rm "$TEMP_INFO_PLIST"

cp "$ICON_FILE" "$APP_NAME/Contents/Resources/Icon.png"
cp "$AOT_PUBLISH_OUTPUT_DIRECTORY/UiharuMind.Desktop" "$APP_NAME/Contents/MacOS/"
for dylib in "$AOT_PUBLISH_OUTPUT_DIRECTORY"/*.dylib; do
    [ -f "$dylib" ] && cp "$dylib" "$APP_NAME/Contents/MacOS/"
done
if [ -d "$AOT_PUBLISH_OUTPUT_DIRECTORY/runtimes" ]; then
    cp -a "$AOT_PUBLISH_OUTPUT_DIRECTORY/runtimes" "$APP_NAME/Contents/MacOS/"
fi

sign_bundle "$APP_NAME"
package_bundle "$APP_NAME" "-AOT"
echo "AOT 发布完成：$APP_NAME / Output/UiharuMind-$APP_VERSION-osx-arm64-AOT.zip"