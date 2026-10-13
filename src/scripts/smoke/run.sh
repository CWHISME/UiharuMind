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
#   SMOKE_CODE_DIR=目录  把这个目录（不含 .git、node_modules）拷进 ws-code，研究外部项目的场景用；只动副本
#   SMOKE_MODEL=名字  主会话、子代理、群成员都用这个模型：注入到每个 session.new，按人数给每个 group.create 填满 models（没写的群成员也不再走卡上默认），
#                     并把副本里两档子代理默认模型改成它（子代理不跟主会话走，没点名时用设置页配的那档）。
#                     专门比模型的场景（如 persona-model-ab）别设它，会把对照组抹平

set -euo pipefail

# 默认模型：全冒烟统一用 Agnes-2.5-Flash-无思考（用户 2026-10-13 指定）。
# 传 SMOKE_MODEL=''（空串）可关掉注入——比模型的对照场景（persona-model-ab）用它保持分组差异
SMOKE_MODEL="${SMOKE_MODEL-Agnes-2.5-Flash-无思考}"

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
[ "${SMOKE_FRESH:-}" = "1" ] || [ -d "$config" ] || { echo "找不到模型配置：${config}（用 SMOKE_CONFIG 指定）" >&2; exit 1; }

# 桌面类冒烟互相抢真实键鼠，全局只许一个在跑（2026-10-13 三方 watcher 并跑抢桌面的教训）。
# macOS 没有 flock，用 mkdir 原子锁 + pid 残留自愈：持有者还在就拒，被 kill -9 留下的旧锁清掉重拿。
lock_dir="${SMOKE_LOCK_DIR:-/tmp/uiharu-smoke.lock}"
if [ -d "$lock_dir" ]; then
    holder="$(cat "$lock_dir/pid" 2>/dev/null || true)"
    if [ -n "$holder" ] && kill -0 "$holder" 2>/dev/null; then
        echo "已有另一个冒烟在跑（pid ${holder}）：并跑会抢桌面，等它跑完（或先 kill ${holder}）再开。" >&2
        exit 1
    fi
    rm -rf "$lock_dir" # 残留锁：持有者已不在（被 kill -9 之类），清掉重拿
fi
mkdir "$lock_dir" && printf '%s\n' "$$" > "$lock_dir/pid"
trap 'rm -rf "$lock_dir"' EXIT INT TERM

# 桌面场景跑前必须确认屏幕解锁：锁屏时前台是 loginwindow，桌面扩展枚举不到应用窗口，全场景白跑
# （2026-10-13 连跑两遍都这么栽的，见 README「读结果的口径」）。只在 macOS 与桌面场景上探。
# lsappinfo front 只给 ASN，名字要从 info 里取；不需要辅助功能授权（Apple Events 那套在锁屏下可能不可用）。
front_app_name() {
    local asn
    asn="$(lsappinfo front 2>/dev/null || true)"
    [ -z "$asn" ] && { echo ""; return; }
    lsappinfo info "$asn" 2>/dev/null | head -1 | awk '{print $1}' | tr -d '"'
}

if [[ "$scenario" == desktop-* ]] && [[ "$(uname -s)" == Darwin ]] && command -v lsappinfo > /dev/null; then
    if [ "$(front_app_name)" = "loginwindow" ]; then
        echo "屏幕锁着（前台是 loginwindow）：先解锁，桌面场景才看得到应用窗口，别白跑。解锁后再跑。" >&2
        exit 1
    fi
fi

# 桌面场景跑前清掉遗留的 TextEdit：失败留下的保存面板（未命名.rtf sheet）会一直挂在窗口列表里，
# 模型每次都撞上它、去操作它而不是新建文档（2026-10-13 两轮验证实测 w8→w7 污染）。
# 光 pkill 不够：macOS 的「恢复窗口」会在下次启动时把旧窗口全部复活（2026-10-14 verify-agnes3-3 实测
# 开局 7 个 TextEdit 窗口）。所以先关「退出保留窗口」偏好、再删已存的 savedState、最后杀进程。
# 只在 macOS 桌面场景做；会丢掉 TextEdit 未保存的内容——桌面场景本就要求桌面空着、别碰鼠标键盘。
# 偏好改动是持久的：想恢复默认用 defaults delete com.apple.TextEdit NSQuitAlwaysKeepsWindows。
if [[ "$scenario" == desktop-* ]] && [[ "$(uname -s)" == Darwin ]]; then
    defaults write com.apple.TextEdit NSQuitAlwaysKeepsWindows -bool false 2> /dev/null || true
    rm -rf "$HOME/Library/Saved Application State/com.apple.TextEdit.savedState" 2> /dev/null || true
    # 前几轮存错的 desktop-note*.rtf/.txt（存到桌面默认位置而不是工作区）会干扰判读：
    # 模型像 agnes3-1 那样开局就看到旧文件、或产物验证被它带偏（2026-10-14 实测桌面 464B .rtf）。
    # 桌面与旧工作区各清一遍；只在桌面场景做，不碰用户自己的文档。
    rm -f "$HOME/Desktop/desktop-note."* "$HOME/Desktop/desktop-note"* 2> /dev/null || true
    # 旧冒烟工作区（上一轮的 ws-empty）里的 desktop-note* 同样清掉，免得模型开局在访达里看到旧文件
    rm -f /tmp/uiharu-smoke/*/ws-empty/desktop-note* 2> /dev/null || true
    if pkill -x "TextEdit" 2>/dev/null; then
        echo "清掉遗留的 TextEdit（含恢复窗口，避免本轮被脏窗口污染）。" >&2
    fi
fi

# 三个工作区：空目录；放了仓库规矩副本的目录（验「先读 AGENTS.md」「改文件落在哪」）；docs 副本（读得多、涨得快的长会话素材）
mkdir -p "$out/home" "$out/ws-empty" "$out/ws-repo" "$out/ws-docs"
out="$(cd "$out" && pwd)"
[ "${SMOKE_FRESH:-}" = "1" ] || cp -R "$config" "$out/home/Config"
cp "$REPO_ROOT/AGENTS.md" "$REPO_ROOT/README.md" "$out/ws-repo/"
cp -R "$REPO_ROOT/docs/." "$out/ws-docs/"
# 场景要的代码素材：scenarios/<场景名>.files 每行一个仓库内路径，按原目录结构拷进 ws-code
mkdir -p "$out/ws-code"
if [ -f "$SCRIPT_DIR/scenarios/$scenario.files" ]; then
    while IFS= read -r rel; do
        [ -z "$rel" ] && continue
        mkdir -p "$out/ws-code/$(dirname "$rel")"
        cp "$REPO_ROOT/$rel" "$out/ws-code/$rel"
    done < "$SCRIPT_DIR/scenarios/$scenario.files"
fi
if [ -n "${SMOKE_CODE_DIR:-}" ]; then
    [ -d "$SMOKE_CODE_DIR" ] || { echo "SMOKE_CODE_DIR 不是目录：$SMOKE_CODE_DIR" >&2; exit 1; }
    rsync -a --exclude .git --exclude node_modules "$SMOKE_CODE_DIR/" "$out/ws-code/"
fi
# 场景自带的档案文件（如只为这个场景准备的角色卡）：scenarios/<场景名>.home/ 原样叠进隔离档案
[ -d "$SCRIPT_DIR/scenarios/$scenario.home" ] && cp -R "$SCRIPT_DIR/scenarios/$scenario.home/." "$out/home/"

if [ -n "${SMOKE_CONTEXT:-}" ]; then
    command -v jq > /dev/null || { echo "SMOKE_CONTEXT 需要 jq" >&2; exit 1; }
    remote="$out/home/Config/RemoteModelSettingConfig.json"
    jq --argjson n "$SMOKE_CONTEXT" '.ModelInfos |= map_values(.Config.ContextLength = $n)' "$remote" > "$remote.tmp"
    mv "$remote.tmp" "$remote"
fi

sed -e "s#{{OUT}}#$out#g" -e "s#{{REPO}}#$REPO_ROOT#g" -e "s#{{WS_EMPTY}}#$out/ws-empty#g" -e "s#{{WS_REPO}}#$out/ws-repo#g" \
    -e "s#{{WS_DOCS}}#$out/ws-docs#g" -e "s#{{WS_CODE}}#$out/ws-code#g" "$template" > "$out/scenario.jsonl"

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
# macOS 桌面场景：自动顶住显示器休眠与系统空闲休眠，免得跑到一半屏幕灭了应用窗口消失（2026-10-14 两次白跑的教训）。
# 只在 macOS 桌面场景起，跑完随脚本一起收。pre-flight 检查已挡了锁屏，caffeinate 管的是「别在跑的时候睡」。
caffeinate_pid=""
if [[ "$scenario" == desktop-* ]] && [[ "$(uname -s)" == Darwin ]] && command -v caffeinate > /dev/null; then
    # -d 顶显示器休眠，-i 顶系统空闲休眠；时限给足看门狗 + 缓冲
    caffeinate -d -i -t $(( ${SMOKE_TIMEOUT_MINUTES:-120} * 60 + 300 )) &
    caffeinate_pid=$!
fi
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
    # 跑一半锁屏同白跑（同 README「读结果的口径」）：桌面场景中途一锁就结束，别拖到超时
    if [[ "$scenario" == desktop-* ]] && [[ "$(uname -s)" == Darwin ]] && [ "$(front_app_name)" = "loginwindow" ]; then
        reason="屏幕锁了（前台 loginwindow）"
    fi

    if [ -n "$reason" ]; then
        echo "看门狗：${reason}，结束应用与本地引擎" | tee -a "$out/app.log" >&2
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
# 收掉脚本自己起的 caffeinate（不影响别人手起的）
[ -n "$caffeinate_pid" ] && kill "$caffeinate_pid" 2> /dev/null || true

if [ ! -f "$out/report.json" ]; then
    echo "没有生成报告，看 $out/app.log" >&2
    exit 1
fi

if [ -n "$killed" ]; then
    echo "看门狗中途结束了应用（${killed}），见 $out/report.json" >&2
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
