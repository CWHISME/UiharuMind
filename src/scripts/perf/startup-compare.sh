#!/usr/bin/env bash
# 冷启动对比（R2R vs AOT）：同口径测两个 .app 的启动到首窗时间。
# 不调模型、不花钱。AOT/R2R 发布通道改动前后各跑一次，比的是同一份副本上的数值。
# 口径见 docs/perf/2026-10-13-R2R基线启动与切会话实测.md：独立 UIHARU_HOME + startup 探针。
#
# 用法：src/scripts/perf/startup-compare.sh <app路径> <label> [样本数]
#    <app路径>：.app 包（如 Output/UiharuMind.app 或 Output/UiharuMind-AOT.app）
#    <label>   ：结果标签（如 r2r / aot），落在 /tmp/startup-compare/<label>-<n>.log
#    样本数    ：默认 3
#
# 注意：启动时 Avalonia 需要 RenderTimer，显示器休眠/锁屏下会 -6661 崩在 GUI 启动阶段，
#   整个循环请包 caffeinate -d -u 跑（唤醒并顶住已休眠的屏）。
#
# 探针：UIHARU_STARTUP_PROBE=1 环境变量开启 StartupPhaseProbe 分段打点（默认关）。
# 结果：启动分段在 <label>-<n>.log，report 在 <label>-report-<n>.json。
#
# 超时：open -W 会等应用退出、自身没有超时；应用启动挂住（-6661 或 dev-script 卡住）时会
#   永远等，整个对比连带调用链（如解锁 watcher）都阻塞。用 OPEN_TIMEOUT 秒兜底：
#   到点杀掉 open 等待进程、这个样本记超时、继续下一个。（不杀应用本体，留给下一环节清理）
set -u

APP="$1"
LABEL="$2"
SAMPLES="${3:-3}"
OPEN_TIMEOUT="${OPEN_TIMEOUT:-120}"
mkdir -p /tmp/startup-compare

for i in $(seq 1 "$SAMPLES"); do
    home="/tmp/startup-compare/$LABEL-home-$i"
    rm -rf "$home"
    mkdir -p "$home"
    if [ -d "$HOME/.uiharu/Config" ]; then
        cp -R "$HOME/.uiharu/Config" "$home/Config"
    fi
    scenario="/tmp/startup-compare/$LABEL-scenario-$i.jsonl"
    cat > "$scenario" <<'EOF'
{"op":"page.jump","args":{"page":"agent"}}
{"op":"ui.snapshot"}
{"op":"quit"}
EOF
    echo "--- $LABEL 样本 $i ---"
    UIHARU_STARTUP_PROBE=1 UIHARU_HOME="$home" \
        open -W -n --env UIHARU_HOME="$home" --env UIHARU_STARTUP_PROBE=1 --stdout "/tmp/startup-compare/$LABEL-$i.log" --stderr "/tmp/startup-compare/$LABEL-$i.log" "$APP" --args --dev-script "$scenario" --dev-report "/tmp/startup-compare/$LABEL-report-$i.json" 2>&1 &
    open_pid=$!
    waited=0
    while kill -0 "$open_pid" 2>/dev/null; do
        if [ "$waited" -ge "$OPEN_TIMEOUT" ]; then
            echo "  [startup] 样本 $i 超时（${OPEN_TIMEOUT}s），杀掉 open 等待进程"
            kill "$open_pid" 2>/dev/null || true
            break
        fi
        sleep 2
        waited=$((waited + 2))
    done
    wait "$open_pid" 2>/dev/null || true
    echo "  [startup] 关键段："
    grep -E "\[startup\]" "/tmp/startup-compare/$LABEL-$i.log" 2>/dev/null | grep -E "launch-main-window|first|settle" | head -2
    echo "  (log: /tmp/startup-compare/$LABEL-$i.log)"
done