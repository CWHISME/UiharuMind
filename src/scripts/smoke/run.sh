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
#   SMOKE_FRESH=1     不复制真实配置，从全新档案起（没装引擎、没有本地模型），验新用户第一次走的那条路
#   SMOKE_NO_BUILD=1  跳过编译，直接用已有的 Release 产物
#   SMOKE_APP=路径    用这份产物跑（隐含不编译）。对照实验时把几个版本的产物各拷一份，轮流指过去。
#                     macOS 上可指向 .app 包：经 open 启动，系统授权（辅助功能、屏幕录制）算在应用自己头上，
#                     而不是启动它的终端——桌面场景（desktop-basic）要这样跑
#   SMOKE_CONTEXT=N   把副本里每个远程模型的上下文上限改成 N（用户填的值优先于预设表，三条压缩水位随之等比缩小），
#                     长会话场景（如 handoff-cache）不必真把 1M 跑满；本地模型按实际加载值算，不受影响
#   SMOKE_TIMEOUT_MINUTES=N  整场最多跑多久，默认 120，到点连本地引擎一起杀
#   SMOKE_MIN_FREE_PERCENT=N 系统空闲内存（kern.memorystatus_level）跌破它就同样杀掉，默认 12
#   SMOKE_MODEL=名字  主会话、子代理、群成员都用这个模型：注入到每个 session.new，按人数给每个 group.create 填满 models（没写的群成员也不再走卡上默认），
#                     并把副本里两档子代理默认模型改成它（子代理不跟主会话走，没点名时用设置页配的那档）。
#                     专门比模型的场景（如 persona-model-ab）别设它，会把对照组抹平

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
DESKTOP_PROJECT="$REPO_ROOT/src/UiharuMind.Desktop/UiharuMind.Desktop.csproj"
case "$(uname -s)" in
    MINGW* | MSYS* | CYGWIN*) DEFAULT_APP="$REPO_ROOT/src/UiharuMind.Desktop/bin/Release/net10.0-windows10.0.19041.0/UiharuMind.Desktop.exe" ;;
    *) DEFAULT_APP="$REPO_ROOT/src/UiharuMind.Desktop/bin/Release/net10.0/UiharuMind.Desktop" ;;
esac
APP="${SMOKE_APP:-$DEFAULT_APP}"

scenario="${1:?用法：run.sh <场景名> [输出目录]，场景见 scenarios/}"
template="$SCRIPT_DIR/scenarios/$scenario.jsonl"
[ -f "$template" ] || { echo "没有这个场景：$template" >&2; exit 1; }

out="${2:-/tmp/uiharu-smoke/$scenario-$(date +%Y%m%d-%H%M%S)}"
config="${SMOKE_CONFIG:-$HOME/.uiharu/Config}"
[ "${SMOKE_FRESH:-}" = "1" ] || [ -d "$config" ] || { echo "找不到模型配置：$config（用 SMOKE_CONFIG 指定）" >&2; exit 1; }

# 三个工作区：空目录；放了仓库规矩副本的目录（验「先读 AGENTS.md」「改文件落在哪」）；docs 副本（读得多、涨得快的长会话素材）
mkdir -p "$out/home" "$out/ws-empty" "$out/ws-repo" "$out/ws-docs"
out="$(cd "$out" && pwd)"
[ "${SMOKE_FRESH:-}" = "1" ] || cp -R "$config" "$out/home/Config"
cp "$REPO_ROOT/AGENTS.md" "$REPO_ROOT/README.md" "$out/ws-repo/"
cp -R "$REPO_ROOT/docs/." "$out/ws-docs/"
# 场景自带的档案文件（如只为这个场景准备的角色卡）：scenarios/<场景名>.home/ 原样叠进隔离档案
[ -d "$SCRIPT_DIR/scenarios/$scenario.home" ] && cp -R "$SCRIPT_DIR/scenarios/$scenario.home/." "$out/home/"

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
        'if .op == "session.new" then .args.model = $m elif .op == "group.create" then .args.models = (.args.members | map($m)) else . end' \
        "$out/scenario.jsonl" > "$out/scenario.jsonl.tmp"
    mv "$out/scenario.jsonl.tmp" "$out/scenario.jsonl"
    agent="$out/home/Config/AgentSettingConfig.json"
    [ -f "$agent" ] || echo '{}' > "$agent"
    jq --arg m "$SMOKE_MODEL" '.GeneralSubAgentModelName = $m' "$agent" > "$agent.tmp"
    mv "$agent.tmp" "$agent"
fi

if [ "${SMOKE_NO_BUILD:-}" != "1" ] && [ -z "${SMOKE_APP:-}" ]; then
    dotnet build "$DESKTOP_PROJECT" -c Release -v q -nologo
fi

echo "输出目录：$out"
# 脚本末步 quit 会直接结束进程：退出码不作数、bash 那句「Killed: 9」也吞掉，成败看报告
if [[ "$APP" == *.app ]]; then
    # -W 等它退出，-n 另起一个实例（不碰正在用的那个）；环境变量与输出经 open 转交
    open -W -n --env UIHARU_HOME="$out/home" --stdout "$out/app.log" --stderr "$out/app.log" "$APP" \
        --args --dev-script "$out/scenario.jsonl" --dev-report "$out/report.json" &
else
    UIHARU_HOME="$out/home" "$APP" --dev-script "$out/scenario.jsonl" --dev-report "$out/report.json" > "$out/app.log" 2>&1 &
fi
app_pid=$!
# 看门狗：超时或系统空闲内存跌破下限就连子进程（本地引擎）一起杀掉。本地生图曾把 16G 机器换页换到内核看门狗重启
# 引擎装在隔离档案里，按这个路径认，不碰别的实例起的引擎（固定字符串匹配：先把路径里的正则字符转义）
engines="$(printf '%s' "$out/home/External/Engine/" | sed 's/[][\\.*^$+?(){}|]/\\&/g')"
killed=""
deadline=$(( $(date +%s) + ${SMOKE_TIMEOUT_MINUTES:-120} * 60 ))
while kill -0 "$app_pid" 2> /dev/null; do
    reason=""
    [ "$(date +%s)" -ge "$deadline" ] && reason="超时"
    level=$(sysctl -n kern.memorystatus_level 2> /dev/null || echo 100)
    [ "$level" -lt "${SMOKE_MIN_FREE_PERCENT:-12}" ] && reason="空闲内存只剩 $level%"
    if [ -n "$reason" ]; then
        echo "看门狗：$reason，结束应用与本地引擎" | tee -a "$out/app.log" >&2
        pkill -9 -f "$engines" 2> /dev/null || true
        # 经 open 起的应用不是 open 的子进程：按包路径另杀
        [[ "$APP" == *.app ]] && pkill -9 -f "$APP/Contents/MacOS/" 2> /dev/null || true
        kill -9 "$app_pid" 2> /dev/null || true
        killed="$reason"
        break
    fi
    sleep 5
done
wait "$app_pid" 2> /dev/null || true
pkill -9 -f "$engines" 2> /dev/null || true

if [ ! -f "$out/report.json" ]; then
    echo "没有生成报告，看 $out/app.log" >&2
    exit 1
fi

if [ -n "$killed" ]; then
    echo "看门狗中途结束了应用（$killed），见 $out/report.json" >&2
    exit 1
fi

# 报告逐步落盘：应用中途崩了也有报告，只是没走到末步 quit
if ! grep -q '"op": "quit"' "$out/report.json"; then
    echo "应用没跑完场景就退出了，见 $out/app.log" >&2
    exit 1
fi

if grep -q '"ok": false' "$out/report.json"; then
    echo "有步骤失败，见 $out/report.json" >&2
    exit 1
fi

echo "全部步骤成功。流水："
ls "$out"/*.md
