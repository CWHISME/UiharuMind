#!/usr/bin/env bash
# 群聊一轮观察（性能 + 日志 + 群流水 + 异常检测），每 30 分钟一轮时复用。
# 只采集与记录，不做介入（发消息/停止离席由观察者人工判断）。
#
# 用法:
#   group-observe.sh --group <群名> --dump-dir <目录> [--doc <追加文档>] [--state <状态文件>]
#
# 输出: 摘要一行到 stdout；给 --doc 时把本轮的详细行追加进去（首次启动写文件头）。
# 注意: 本机 macOS bash 3.2 在 set -u 下 $VAR 后紧跟中文标点会误并字节，变量引用一律用 ${VAR}。
# 从上一会话草稿目录的 watch.sh 抽出通用段（2026-10-13，收编进 src/scripts/perf）。
set -uo pipefail

REPO="$(cd "$(dirname "$0")/../../.." && pwd)"
CLI="${REPO}/src/UiharuMind.CLI/bin/Debug/net10.0/UiharuMind.CLI"
LOG_FILE="${HOME}/.uiharu/Logs/Log.txt"
DETECT="$(cd "$(dirname "$0")" && pwd)/group-observe-detect.py"

GROUP=""
DUMP_DIR=""
DOC=""
STATE=""

while [ $# -gt 0 ]; do
  case "$1" in
    --group) GROUP="$2"; shift 2;;
    --dump-dir) DUMP_DIR="$2"; shift 2;;
    --doc) DOC="$2"; shift 2;;
    --state) STATE="$2"; shift 2;;
    *) echo "未知参数: $1" >&2; exit 2;;
  esac
done

[ -z "${GROUP}" ] && { echo "缺 --group" >&2; exit 2; }
[ -z "${DUMP_DIR}" ] && DUMP_DIR="/tmp/group-observe"
mkdir -p "${DUMP_DIR}"

# ---------- 读取/初始化状态 ----------
if [ -n "${STATE}" ] && [ -f "${STATE}" ]; then
  last_posts=$(jq -r '.last_posts' "${STATE}")
  last_reply=$(jq -r '.log_reply' "${STATE}")
  last_retry=$(jq -r '.log_retry' "${STATE}")
  last_429=$(jq -r '.log_429' "${STATE}")
  last_trunc=$(jq -r '.log_trunc' "${STATE}")
  round=$(jq -r '.round' "${STATE}")
else
  last_posts=-1; last_reply=-1; last_retry=-1; last_429=-1; last_trunc=-1; round=0
fi
round=$((round + 1))
ts_short=$(date '+%H:%M')
ts_full=$(date '+%Y-%m-%d %H:%M:%S')

# ---------- 1. 应用进程 ----------
ping_out=$("${CLI}" app call app.ping 2>/dev/null || echo '{"ok":false}')
APP_PID=$(echo "${ping_out}" | jq -r '.result.pid // empty' 2>/dev/null || echo "")
if [ -z "${APP_PID}" ] || [ "${APP_PID}" = "null" ]; then
  echo "app.ping 失败，跳过本轮"
  exit 0
fi

# ---------- 2. 性能采集 ----------
mem_out=$("${CLI}" app call diag.memory 2>/dev/null || echo '{"ok":false,"result":{}}')
memr=$(echo "${mem_out}" | jq -c '.result' 2>/dev/null || echo '{}')
managed=$(echo "${memr}" | jq -r '.managedHeapMb // 0')
committed=$(echo "${memr}" | jq -r '.committedMb // 0')
frag=$(echo "${memr}" | jq -r '.fragmentedMb // 0')
ws=$(echo "${memr}" | jq -r '.workingSetMb // 0')
gen2=$(echo "${memr}" | jq -r '.gen2Collections // 0')
loaded=$(echo "${memr}" | jq -r '.loadedSessions // 0')
resident=$(echo "${memr}" | jq -r '.residentHistories // 0')

psline=$(ps -o rss=,pcpu=,etime= -p "${APP_PID}" 2>/dev/null | awk '{print $1"|"$2"|"$3}')
rss_kb=$(echo "${psline}" | cut -d'|' -f1)
pcpu=$(echo "${psline}" | cut -d'|' -f2)
etime=$(echo "${psline}" | cut -d'|' -f3)
thr=$(ps -M "${APP_PID}" 2>/dev/null | wc -l | tr -d ' ')
[ -z "${thr}" ] && thr=0
if [ "${thr}" -gt 0 ]; then thr=$((thr - 1)); fi
rss_mb=$(( rss_kb / 1024 ))

# ---------- 3. 日志区间统计 ----------
reply_now=$(grep -c '^\[stream\] assistant reply ended' "${LOG_FILE}" 2>/dev/null); reply_now=${reply_now:-0}
retry_now=$(grep -c 'rate limited (429), retry #' "${LOG_FILE}" 2>/dev/null); retry_now=${retry_now:-0}
http429_now=$(grep -c 'OpenAI-compatible response: 429' "${LOG_FILE}" 2>/dev/null); http429_now=${http429_now:-0}
trunc_now=$(grep -cE 'finish_reason=none|finish_reason=\(none\)' "${LOG_FILE}" 2>/dev/null); trunc_now=${trunc_now:-0}

delta_reply=$(( reply_now - last_reply )); [ "${delta_reply}" -lt 0 ] && delta_reply=0
delta_retry=$(( retry_now - last_retry )); [ "${delta_retry}" -lt 0 ] && delta_retry=0
delta_429=$(( http429_now - last_429 )); [ "${delta_429}" -lt 0 ] && delta_429=0
delta_trunc=$(( trunc_now - last_trunc )); [ "${delta_trunc}" -lt 0 ] && delta_trunc=0
rotated=""
if [ "${reply_now}" -lt "${last_reply}" ]; then rotated="（日志可能轮换）"; fi

# ---------- 4. 群聊状态 ----------
dump_file="${DUMP_DIR}/round-${round}-${ts_short}.md"
dump_out=$("${CLI}" app call group.dump --args "{\"group\":\"${GROUP}\",\"path\":\"${dump_file}\"}" 2>/dev/null || echo '{"ok":false}')
posts=$(echo "${dump_out}" | jq -r '.result.posts // 0')
artifacts=$(echo "${dump_out}" | jq -r '.result.artifacts // 0')

# ---------- 5. 异常检测 ----------
if [ "${last_posts}" -ge 0 ]; then
  delta_posts=$(( posts - last_posts ))
else
  delta_posts=0
fi
if [ "${last_posts}" -ge 0 ]; then
  detect_out=$(python3 "${DETECT}" "${dump_file}" "${delta_posts}" "${delta_reply}" "${delta_429}" "${DUMP_DIR}/prev.txt" 2>/dev/null || echo '{"anomaly":"none","detail":"检测失败"}')
else
  detect_out='{"anomaly":"none","detail":"首轮基线"}'
fi
anomaly=$(echo "${detect_out}" | jq -r '.anomaly // "none"')
anomaly_detail=$(echo "${detect_out}" | jq -r '.detail // ""')

# ---------- 6. 落盘（可选） ----------
if [ -n "${DOC}" ]; then
  if [ ! -f "${DOC}" ]; then
    cat > "${DOC}" <<EOF
# 群观察巡检（${GROUP}，$(date '+%Y-%m-%d') 起）

基线：应用 PID ${APP_PID}，托管堆 ${managed}MB / 已提交 ${committed}MB / 工作集 ${ws}MB / gen2 ${gen2} 次 / 驻留会话 ${loaded}。
RSS ${rss_mb}MB，CPU ${pcpu}%，posts=${posts} artifacts=${artifacts}。

## 巡检记录
EOF
  fi
  cat >> "${DOC}" <<EOF

### ${ts_short}（第 ${round} 轮）${rotated}

- **性能**：托管堆 ${managed}MB / 已提交 ${committed}MB / 碎片 ${frag}MB / 工作集 ${ws}MB / gen2 ${gen2} 次；
  RSS ${rss_mb}MB / CPU ${pcpu}% / 线程 ${thr} / 运行 ${etime}；驻留会话 ${loaded}。
- **日志区间**：回复结束 ${delta_reply}（累计 ${reply_now}）、429 响应 ${delta_429}（累计 ${http429_now}）、重试 ${delta_retry}、截断 ${delta_trunc}。
- **群聊**：posts=${posts}（本轮 +${delta_posts}）artifacts=${artifacts}。
- **检测**：${anomaly} —— ${anomaly_detail}
EOF
fi

# ---------- 7. 更新状态 ----------
if [ -n "${STATE}" ]; then
  jq -n \
    --arg ts "${ts_full}" \
    --argjson posts "${posts}" --argjson artifacts "${artifacts}" \
    --argjson reply "${reply_now}" --argjson retry "${retry_now}" --argjson h429 "${http429_now}" --argjson trunc "${trunc_now}" \
    --argjson round "${round}" \
    '{ts:$ts, last_posts:$posts, last_artifacts:$artifacts, log_reply:$reply, log_retry:$retry, log_429:$h429, log_trunc:$trunc, round:$round}' > "${STATE}"
fi

echo "round ${round} done: anomaly=${anomaly} posts=${posts} (+${delta_posts}) rss=${rss_mb}MB ${rotated}"