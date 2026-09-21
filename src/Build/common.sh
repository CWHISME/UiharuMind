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

# bundle 级签名。Apple Silicon 上 arm64 可执行文件必须有签名才能运行，
# dotnet 只签了 apphost，bundle 整体（含 Info.plist 密封）还得自己来。
#
# 必须用自签名证书而不是 ad-hoc：macOS 的 TCC 把辅助功能/屏幕录制的授权钉在
# 「指定要求」上，ad-hoc 没有颁发者，系统只能退化成用 cdhash——那个值每次编译都变，
# 用户每次更新都要重新授权。证书签名的指定要求是「bundle ID + 证书」，跨版本稳定。
# 证书没有则跑 Build/generateSigningCert.sh 生成，再 security import 进 keychain。
#
# 真买了 Developer ID 之后应改成由内到外逐个签（先 dylib 再 bundle），而不是 --deep。
SIGN_IDENTITY="${SIGN_IDENTITY:-UiharuMind Self-Signed}"

sign_bundle() {
    local bundle="$1"

    if security find-identity -p codesigning 2>/dev/null | grep -qF "$SIGN_IDENTITY"; then
        codesign --force --deep --sign "$SIGN_IDENTITY" --timestamp=none "$bundle"
        codesign --verify --deep --strict "$bundle"
        echo "已签名（$SIGN_IDENTITY）：$bundle"
        return 0
    fi

    # CI 上静默回退成 ad-hoc 会发出一个身份不对的 Release，而这种错误只有用户
    # 升级时才暴露，所以这里必须红
    if [ "${CI:-}" = "true" ]; then
        echo "keychain 里找不到 '$SIGN_IDENTITY'，CI 上不允许回退到 ad-hoc" >&2
        echo "请确认 MACOS_CERTIFICATE / MACOS_CERTIFICATE_PWD / MACOS_SIGNING_IDENTITY 三个 secret 已配置" >&2
        return 1
    fi

    # 本机回退：新克隆的机器、别人的 fork 都该能构建出可跑的包
    echo "警告：keychain 里找不到 '$SIGN_IDENTITY'，回退到 ad-hoc 签名" >&2
    echo "警告：ad-hoc 包的辅助功能授权在每次重新编译后都会失效" >&2
    echo "警告：跑 Build/generateSigningCert.sh 生成证书可消除此问题" >&2
    codesign --force --deep --sign - --timestamp=none "$bundle"
    codesign --verify --deep --strict "$bundle"
    echo "已 ad-hoc 签名：$bundle"
}

# 压包必须用 ditto：zip 不保留 bundle 的扩展属性与符号链接，签名会在解包后失效。
# 第二个参数是文件名后缀（Full 包用它区分，避免覆盖 slim 包）
package_bundle() {
    local bundle="$1"
    local suffix="${2:-}"
    local archive="Output/UiharuMind-$APP_VERSION-osx-arm64$suffix.zip"

    rm -f "$archive"
    ditto -c -k --sequesterRsrc --keepParent "$bundle" "$archive"
    echo "已打包：$archive"
}
