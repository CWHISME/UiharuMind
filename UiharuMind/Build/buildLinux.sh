#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"
source common.sh

TARGET_RUNTIME="linux-x64"
STAGE_DIR="Tmp/Linux"

APP_VERSION=$(resolve_version)
# 发布进一个叫 UiharuMind 的子目录，解包时就不会把文件摊到当前目录
publish_for "$TARGET_RUNTIME" "$STAGE_DIR/UiharuMind"

mkdir -p Output
ARCHIVE="Output/UiharuMind-$APP_VERSION-$TARGET_RUNTIME.tar.gz"
rm -f "$ARCHIVE"
tar -czf "$ARCHIVE" -C "$STAGE_DIR" UiharuMind
echo "已打包：$ARCHIVE"
