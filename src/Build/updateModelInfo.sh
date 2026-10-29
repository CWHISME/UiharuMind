#!/bin/bash
# 出包前刷新构建期生成的数据表（models.dev 能力表 + 各家官网价格表）。
#
# 这些表是"重新生成即改数"：模型加了、价格变了，都得跑一遍并把 .g.cs 一起提交。
# 生成器要联网（models.dev + 各家文档站），所以刻意不接进 dotnet build——
# 构建不该因为对端文档站抽风而失败。出包之前手动跑这一条。
#
# 用法：
#   ./updateModelInfo.sh              # 全量刷新
#   ./updateModelInfo.sh --cc-only    # 只刷 CommandCode 官网价格（能力表走 models.dev，变动慢）
#
# 之后用 git diff 检视：数字变了就该一起提交，只有日期变了可以丢掉。
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
GENERATOR_DIR="$REPO_ROOT/src/scripts/modelInfoGenerator"

CC_ONLY=0
for arg in "$@"; do
    case "$arg" in
        --cc-only) CC_ONLY=1 ;;
        -h|--help) sed -n '2,14p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "未知参数：$arg" >&2; exit 2 ;;
    esac
done

# Windows 上 python 可能是 python3 / py / python，挑第一个能用的
PYTHON=""
for candidate in python3 python py; do
    if command -v "$candidate" >/dev/null 2>&1; then
        PYTHON="$candidate"
        break
    fi
done
if [ -z "$PYTHON" ]; then
    echo "找不到 python（试过 python3 / python / py），这些生成器是 Python 脚本" >&2
    exit 1
fi

cd "$GENERATOR_DIR"

if [ "$CC_ONLY" -eq 0 ]; then
    echo "== models.dev 能力表（视觉/上下文/思考档位/单价 + canonical 索引）"
    "$PYTHON" generate_modelsdev_capabilities.py
fi

echo "== CommandCode 官网价格表（models.dev 缺行的模型靠它兜底）"
"$PYTHON" generate_commandcode_pricing.py

echo
echo "生成完毕，检视改动："
echo "  git diff --stat src/UiharuMind.Core/RemoteOpenAI/ModelCatalog"
