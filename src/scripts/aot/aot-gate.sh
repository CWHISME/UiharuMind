#!/bin/bash
# 发版前本地必经一步（AOT 门）：buildAotMac.sh 出签名 AOT .app → 用它跑 brain 冒烟判绿。
# 标准是冒烟跑绿，不是编译过——CI 的 aot-gate 只判「AOT 还能编」，运行时那半道只能靠这条兜。
#
# 用法：src/scripts/aot/aot-gate.sh [--skip-build]
#   默认    buildAotMac.sh（编译+签名+打包）→ 跑 brain 冒烟
#   --skip-build  直接用已存在的 Output/UiharuMind-AOT.app 跑冒烟（刚 buildAotMac.sh 过后省 6 分钟）
#
# 前提：本机 keychain 有签名证书（buildAotMac.sh 不传 CI 时本机签名）、~/.uiharu/Config 有可用模型。
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# 本脚本在 src/scripts/aot/：Build 在 src/Build（上两层），smoke 在 src/scripts/smoke（上一层）
BUILD_DIR="$SCRIPT_DIR/../../Build"
SMOKE_DIR="$SCRIPT_DIR/../smoke"
SKIP_BUILD="${1:-}"

if [ "$SKIP_BUILD" = "--skip-build" ]; then
    echo "== 跳过构建，用已有 AOT .app 跑 brain 冒烟 =="
else
    echo "== 1/2 buildAotMac.sh（AOT 编译 + 签名 + 打包）=="
    bash "$BUILD_DIR/buildAotMac.sh"
fi

AOT_APP="$BUILD_DIR/Output/UiharuMind-AOT.app"
[ -d "$AOT_APP" ] || { echo "AOT .app 不在：$AOT_APP（不带 --skip-build 时应由 buildAotMac.sh 生成）" >&2; exit 1; }

echo "== 2/2 用 AOT .app 跑 brain 冒烟（真模型、花钱，判绿才过）=="
# SMOKE_APP 指 .app：run.sh 经 open 起，TCC 算应用自己；brain 纯模型调用不碰桌面，不需辅助功能授权
SMOKE_APP="$AOT_APP" bash "$SMOKE_DIR/run.sh" brain

echo
echo "AOT 本地门：brain 冒烟绿。这一版 AOT 能发（R2R 仍是发布终点，AOT 是旁路对照）。"
