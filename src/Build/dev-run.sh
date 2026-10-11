#!/bin/bash
# 日常开发启动（ADR 0029 / AGENTS.md）：构建签名的 .app 并用 open 启动。
# 别直接跑 bin 里的 exe——它是 ad-hoc 签名，TCC（辅助功能/屏幕录制）里没有授权记录，
# 桌面扩展在 bin 那条路上查不到授权（不是 bug，是签名身份不同）。
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"
./buildMac.sh
open "Output/UiharuMind.app"