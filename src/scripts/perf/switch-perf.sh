#!/usr/bin/env bash
#
# 切会话性能基线：在真实档案的副本上轮流打开几个最长的智能体会话，汇总探针打点与内存。
# 不调模型、不花钱。重构或做性能优化前后各跑一次，比的是同一份副本上的数。
# ⚠️ 末圈托管堆是双峰的：同一份构建实测在约 200MB 与约 220MB 之间跳，单跑一次的差值不作数，
# 比内存至少前后各跑三次；探针耗时的中位数稳定得多。
#
# 用法：src/scripts/perf/switch-perf.sh [输出目录]
#   输出目录：默认 /tmp/uiharu-perf/switch-<时间戳>
#
# 数据隔离：UIHARU_HOME 指到输出目录下一份新档案，复制 Config 与 Data（不带 Data/Agent：
# 那是工作区房间，几个 G，回放用不上）。脚本改的只是副本。
#
# 环境变量：
#   PERF_SOURCE       档案来源，默认 ~/.uiharu
#   PERF_SESSIONS=N   打开几个会话，默认 4（取历史文件最大的 N 个顶层智能体会话）
#   PERF_ROUNDS=N     轮流打开几圈，默认 3（第一圈是冷装载，之后是缓存实例切回）
#   PERF_SETTLE_MS=N  每次打开后等多久再记快照，默认 1500
#   PERF_NO_BUILD=1   跳过编译，直接用已有的 Release 产物

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
DESKTOP_PROJECT="$REPO_ROOT/src/UiharuMind.Desktop/UiharuMind.Desktop.csproj"
APP="$REPO_ROOT/src/UiharuMind.Desktop/bin/Release/net10.0/UiharuMind.Desktop"

command -v jq > /dev/null || { echo "需要 jq" >&2; exit 1; }

source_home="${PERF_SOURCE:-$HOME/.uiharu}"
count="${PERF_SESSIONS:-4}"
rounds="${PERF_ROUNDS:-3}"
settle="${PERF_SETTLE_MS:-1500}"
out="${1:-/tmp/uiharu-perf/switch-$(date +%Y%m%d-%H%M%S)}"
[ -d "$source_home/Data/Sessions" ] || { echo "找不到档案：$source_home（用 PERF_SOURCE 指定）" >&2; exit 1; }

mkdir -p "$out/home"
cp -R "$source_home/Config" "$out/home/Config"
rsync -a --exclude '/Agent/' "$source_home/Data/" "$out/home/Data/"

# 挑会话：智能体一侧的顶层会话（非群、非子会话、非群成员），按历史文件大小降序
sessions="$out/home/Data/Sessions"
ids=()
while read -r id; do
    meta="$sessions/$id.meta.json"
    [ -f "$meta" ] && [ -f "$sessions/$id.history.jsonl" ] || continue
    jq -e '(.parentSessionId // "") == "" and (.groupId // "") == ""' "$meta" > /dev/null || continue
    ids+=("$id")
    [ "${#ids[@]}" -ge "$count" ] && break
done < <(
    jq -r '.[] | select(.isAgentForm == true and .isGroup == false) | .sessionId' "$sessions/index.json" |
        while read -r id; do
            f="$sessions/$id.history.jsonl"
            [ -f "$f" ] && echo "$(wc -c < "$f") $id"
        done | sort -rn | awk '{print $2}'
)
[ "${#ids[@]}" -gt 0 ] || { echo "副本里没有可打开的智能体会话" >&2; exit 1; }

scenario="$out/scenario.jsonl"
{
    echo '{"op":"page.jump","args":{"page":"agent"}}'
    echo '{"op":"wait","args":{"ms":3000}}'
    echo '{"op":"diag.memory","args":{"collect":true}}'
    for ((round = 1; round <= rounds; round++)); do
        for id in "${ids[@]}"; do
            echo "{\"op\":\"session.open\",\"args\":{\"id\":\"$id\"}}"
            echo "{\"op\":\"wait\",\"args\":{\"ms\":$settle}}"
            echo '{"op":"ui.snapshot"}'
        done
        echo '{"op":"diag.memory","args":{"collect":true}}'
    done
    echo '{"op":"wait","args":{"ms":1500}}'
    echo '{"op":"quit"}'
} > "$scenario"

if [ "${PERF_NO_BUILD:-}" != "1" ]; then
    dotnet build "$DESKTOP_PROJECT" -c Release -v q -nologo
fi

echo "输出目录：$out"
echo "会话：${ids[*]}"
# 末步 quit 会直接结束进程：退出码不作数、bash 那句「Killed: 9」也吞掉，成败看报告
( UIHARU_HOME="$out/home" UIHARU_STARTUP_PROBE=1 "$APP" --dev-script "$scenario" --dev-report "$out/report.json" \
    > "$out/app.log" 2>&1 || true ) 2>/dev/null

[ -f "$out/report.json" ] || { echo "没有生成报告，看 $out/app.log" >&2; exit 1; }
if jq -e 'any(.[]; .ok == false)' "$out/report.json" > /dev/null; then
    echo "有步骤失败，见 $out/report.json" >&2
fi

# 探针打点按阶段名聚合（冒号后是变化的明细，如条目数），报次数、中位数、最大值
summary="$out/summary.txt"
{
    echo "== 探针（ms）：阶段 次数 中位 最大 =="
    grep -h -o 'conversation/[^ ]* =[0-9]*ms' "$out/home/Logs/"*.txt 2>/dev/null |
        sed -E 's#^([^: ]*)[^ ]* =([0-9]+)ms#\1 \2#' |
        sort -k1,1 -k2,2n |
        awk '{ v[$1] = v[$1] " " $2; n[$1]++ }
             END { for (k in n) { split(substr(v[k], 2), a, " "); printf "%-36s %4d %6d %6d\n", k, n[k], a[int((n[k] + 1) / 2)], a[n[k]] } }' |
        sort
    echo
    echo "== 内存（MB）：托管堆 已提交 工作集 已加载会话 驻留历史 =="
    jq -r '.[] | select(.op == "diag.memory" and .ok) | .result |
        "\(.managedHeapMb) \(.committedMb) \(.workingSetMb) \(.loadedSessions) \(.residentHistories)"' "$out/report.json"
    echo
    echo "== 切换后条目数：会话 条目 有更早 =="
    jq -r '.[] | select(.op == "ui.snapshot" and .ok) | .result |
        "\(.sessionId // "-" | .[0:8]) \(.items) \(.hasEarlierMessages)"' "$out/report.json"
} > "$summary"
cat "$summary"
