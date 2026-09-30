#!/usr/bin/env bash
#
# 用真实模型跑一段冒烟场景（开发脚本的用法见仓库根 AGENTS.md）。会真的调用模型、花钱。
#
# 用法：src/scripts/smoke/run.sh <场景名> [输出目录]
#   场景名：scenarios/ 下的文件名，不带 .jsonl，如 toaru-group
#   输出目录：默认 /tmp/uiharu-smoke/<场景名>-<时间戳>
#
# 数据隔离：UIHARU_HOME 指到输出目录下一份新档案，只从真实档案复制 Config（模型与密钥），
# 会话、记忆一概不带；工作区也是临时目录，放的是仓库文件的副本。脚本改的只是这些副本。
#
# 环境变量：
#   SMOKE_CONFIG      模型配置来源，默认 ~/.uiharu/Config
#   SMOKE_NO_BUILD=1  跳过编译，直接用已有的 Release 产物

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
DESKTOP_PROJECT="$REPO_ROOT/src/UiharuMind.Desktop/UiharuMind.Desktop.csproj"
APP="$REPO_ROOT/src/UiharuMind.Desktop/bin/Release/net10.0/UiharuMind.Desktop"

scenario="${1:?用法：run.sh <场景名> [输出目录]，场景见 scenarios/}"
template="$SCRIPT_DIR/scenarios/$scenario.jsonl"
[ -f "$template" ] || { echo "没有这个场景：$template" >&2; exit 1; }

out="${2:-/tmp/uiharu-smoke/$scenario-$(date +%Y%m%d-%H%M%S)}"
config="${SMOKE_CONFIG:-$HOME/.uiharu/Config}"
[ -d "$config" ] || { echo "找不到模型配置：$config（用 SMOKE_CONFIG 指定）" >&2; exit 1; }

# 两个工作区：空目录；放了仓库规矩副本的目录（验「先读 AGENTS.md」「改文件落在哪」）
mkdir -p "$out/home" "$out/ws-empty" "$out/ws-repo"
cp -R "$config" "$out/home/Config"
cp "$REPO_ROOT/AGENTS.md" "$REPO_ROOT/README.md" "$out/ws-repo/"

sed -e "s#{{OUT}}#$out#g" -e "s#{{WS_EMPTY}}#$out/ws-empty#g" -e "s#{{WS_REPO}}#$out/ws-repo#g" \
    "$template" > "$out/scenario.jsonl"

if [ "${SMOKE_NO_BUILD:-}" != "1" ]; then
    dotnet build "$DESKTOP_PROJECT" -c Release -v q -nologo
fi

echo "输出目录：$out"
# 脚本末步 quit 会直接结束进程：退出码不作数、bash 那句「Killed: 9」也吞掉，成败看报告
( UIHARU_HOME="$out/home" "$APP" --dev-script "$out/scenario.jsonl" --dev-report "$out/report.json" > "$out/app.log" 2>&1 || true ) 2>/dev/null

if [ ! -f "$out/report.json" ]; then
    echo "没有生成报告，看 $out/app.log" >&2
    exit 1
fi

if grep -q '"ok": false' "$out/report.json"; then
    echo "有步骤失败，见 $out/report.json" >&2
    exit 1
fi

echo "全部步骤成功。流水："
ls "$out"/*.md
