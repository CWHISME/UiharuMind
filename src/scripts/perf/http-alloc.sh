#!/usr/bin/env bash
#
# OpenAI 兼容 HTTP 层的分配基准：进程外调用 UiharuMind.Core，量我们这层（SSE 清洗、请求体改写、请求体日志、日志落盘）
# 每次调用的托管分配、大对象堆分配，以及连跑时的 GC 行为。不调模型、不走网络、不碰真实档案。
# 实测记录与各轮读数见 docs/perf/2026-10-05-OpenAI兼容HTTP层分配实测.md。
#
# 用法：src/scripts/perf/http-alloc.sh <模式> [参数]
#   sse                  响应侧：SSE 清洗每次调用 / 每块的分配（现状，回归基线）
#   req                  请求侧各环节（改写、请求体日志、策略搬运、Bodies 落盘、思考回填收集、带图抹 base64）
#   e2e                  端到端一次流式调用
#   gc                   连跑 120 次长跑量级调用：各代回收次数、已提交 / 碎片 / 大对象堆峰值
#   all                  sse + req + e2e
#
# 数据隔离：UIHARU_HOME 每次指到一个新的临时目录（清洗流收尾会打日志，不隔离就会写进并轮换真实日志），跑完删掉。
#
# 环境变量：
#   HTTP_ALLOC_KEEP=1    跑完保留临时目录（看日志与 Bodies.txt），路径打印在最后

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/http-alloc/HttpAllocBench.csproj"

UIHARU_HOME="$(mktemp -d "${TMPDIR:-/tmp}/uiharu-http-alloc.XXXXXX")"
export UIHARU_HOME
if [[ "${HTTP_ALLOC_KEEP:-}" == "1" ]]; then
  trap 'echo "临时目录：$UIHARU_HOME"' EXIT
else
  trap 'rm -rf "$UIHARU_HOME"' EXIT
fi

# 构建成功时不出声，失败时把构建输出整份打出来
dotnet build "$PROJECT" -c Release -nologo -v q > "$UIHARU_HOME/build.log" 2>&1 || { cat "$UIHARU_HOME/build.log"; exit 1; }
# 关掉分层编译：耗时按优化后的代码量，不混进 Tier0 的读数
DOTNET_TieredCompilation=0 dotnet run -c Release --no-build --project "$PROJECT" -- "$@"
