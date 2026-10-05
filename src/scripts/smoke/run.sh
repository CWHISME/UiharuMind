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
#   SMOKE_CONTEXT=N   把副本里每个远程模型的上下文上限改成 N（用户填的值优先于预设表，三条压缩水位随之等比缩小），
#                     长会话场景（如 handoff-cache）不必真把 1M 跑满；本地模型按实际加载值算，不受影响
#   SMOKE_MODEL=名字  主会话、子代理、群成员都用这个模型：注入到每个 session.new，覆盖 group.create 里写死的 models，
#                     并把副本里两档子代理默认模型改成它（子代理不跟主会话走，没点名时用设置页配的那档）。
#                     专门比模型的场景（如 persona-model-ab）别设它，会把对照组抹平

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

# 三个工作区：空目录；放了仓库规矩副本的目录（验「先读 AGENTS.md」「改文件落在哪」）；docs 副本（读得多、涨得快的长会话素材）
mkdir -p "$out/home" "$out/ws-empty" "$out/ws-repo" "$out/ws-docs"
cp -R "$config" "$out/home/Config"
cp "$REPO_ROOT/AGENTS.md" "$REPO_ROOT/README.md" "$out/ws-repo/"
cp -R "$REPO_ROOT/docs/." "$out/ws-docs/"

if [ -n "${SMOKE_CONTEXT:-}" ]; then
    command -v jq > /dev/null || { echo "SMOKE_CONTEXT 需要 jq" >&2; exit 1; }
    remote="$out/home/Config/RemoteModelSettingConfig.json"
    jq --argjson n "$SMOKE_CONTEXT" '.ModelInfos |= map_values(.Config.ContextLength = $n)' "$remote" > "$remote.tmp"
    mv "$remote.tmp" "$remote"
fi

sed -e "s#{{OUT}}#$out#g" -e "s#{{REPO}}#$REPO_ROOT#g" -e "s#{{WS_EMPTY}}#$out/ws-empty#g" -e "s#{{WS_REPO}}#$out/ws-repo#g" \
    -e "s#{{WS_DOCS}}#$out/ws-docs#g" "$template" > "$out/scenario.jsonl"

if [ -n "${SMOKE_MODEL:-}" ]; then
    command -v jq > /dev/null || { echo "SMOKE_MODEL 需要 jq" >&2; exit 1; }
    jq -c --arg m "$SMOKE_MODEL" \
        'if .op == "session.new" then .args.model = $m elif .op == "group.create" and .args.models then .args.models |= map($m) else . end' \
        "$out/scenario.jsonl" > "$out/scenario.jsonl.tmp"
    mv "$out/scenario.jsonl.tmp" "$out/scenario.jsonl"
    agent="$out/home/Config/AgentSettingConfig.json"
    [ -f "$agent" ] || echo '{}' > "$agent"
    jq --arg m "$SMOKE_MODEL" '.GeneralSubAgentModelName = $m | .ExplorerSubAgentModelName = $m' "$agent" > "$agent.tmp"
    mv "$agent.tmp" "$agent"
fi

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
