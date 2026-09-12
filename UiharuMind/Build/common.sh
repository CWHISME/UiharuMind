#!/bin/bash
# buildMac.sh / buildLinux.sh 的共享部分。发布参数（自包含、R2R、单文件）
# 收在 UiharuMind.Desktop.csproj 里，这里只做脚本该做的事。

DESKTOP_PROJECT="../UiharuMind.Desktop/UiharuMind.Desktop.csproj"

# 版本号唯一来源：Directory.Build.props 的 <Version>，与 AppInfo.Version 同源
resolve_version() {
    local version
    version=$(dotnet msbuild "$DESKTOP_PROJECT" -getProperty:Version -nologo)
    if [ -z "$version" ]; then
        echo "取不到 <Version>，中止" >&2
        return 1
    fi
    echo "$version"
}

# 发布到指定目录，并清掉其它 RID 的原生库（自包含发布会把全部 RID 都摊出来）
publish_for() {
    local rid="$1"
    local output="$2"

    rm -rf "$output"
    dotnet publish "$DESKTOP_PROJECT" --output "$output" -r "$rid" --configuration Release || return 1

    if [ -d "$output/runtimes" ]; then
        find "$output/runtimes" -mindepth 1 -maxdepth 1 -type d ! -name "$rid" -exec rm -rf {} +
    fi
}

# bundle 级 ad-hoc 签名。Apple Silicon 上 arm64 可执行文件必须有签名才能运行，
# dotnet 只签了 apphost，bundle 整体（含 Info.plist 密封）还得自己来。
# ad-hoc 不是可信颁发者，用户仍需在「隐私与安全性」里放行；真买了 Developer ID
# 之后应改成由内到外逐个签（先 dylib 再 bundle），而不是 --deep。
sign_bundle() {
    local bundle="$1"

    codesign --force --deep --sign - --timestamp=none "$bundle"
    codesign --verify --deep --strict "$bundle"
    echo "已 ad-hoc 签名：$bundle"
}
