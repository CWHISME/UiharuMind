#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

source buildMac.sh

# 随包带上本机已备好的运行时与模型。这些资产不在仓库里（体积与许可都不合适），
# 只有备齐的机器才出得了 Full 包；缺哪份就跳过哪份，而不是静默拷失败。
copy_internal_asset() {
    local source_dir="$1"
    local target_name="$2"

    if [ ! -d "$source_dir" ]; then
        echo "跳过 $target_name：$source_dir 不存在" >&2
        return 0
    fi

    cp -a "$source_dir" "$APP_NAME/Contents/MacOS/$target_name"
    echo "已带入 $target_name"
}

SUPPORT_DIR="${UIHARU_HOME:-$HOME/.uiharu}"

copy_internal_asset "$SUPPORT_DIR/EmbededModels" "InternalEmbededModels"
copy_internal_asset "$SUPPORT_DIR/Runtime" "InternalRuntime"
copy_internal_asset "$HOME/Documents/Studys/LLMModel/InternalModels" "InternalModels"

# buildMac.sh 已经签过一次，但上面往 bundle 里拷了东西，密封失效，必须重签后重新打包
sign_bundle "$APP_NAME"
package_bundle "$APP_NAME" "-full"
