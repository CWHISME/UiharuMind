#!/bin/bash
#
# 生成一张自签名的代码签名证书，供本机与 CI 复用。
#
# 为什么需要它：
#   macOS 的 TCC（辅助功能 / 屏幕录制授权）把权限钉在 app 的**代码签名身份**上。
#   ad-hoc 签名没有颁发者，系统只能退化成用 cdhash 当身份——那个值每次编译都变，
#   于是「权限列表里条目还在，但权限已经失效」。
#   用同一张自签名证书签每一次构建，指定要求（Designated Requirement）就固定成
#   「bundle ID + 这张证书」，授权跨版本保留。
#
#   这不是 Developer ID：Gatekeeper 首次打开仍会提示「无法验证开发者」，
#   那部分没有付费账号解决不了。
#
# 只需跑一次，产物长期复用。重新生成会让所有用户再授权一次。
#
# 用法：
#   Build/generateSigningCert.sh [输出目录]
# 环境变量：
#   CERT_PASSWORD  p12 的密码（默认 uiharumind）
#   CERT_NAME      证书通用名（默认 UiharuMind Self-Signed）
#   CERT_DAYS      有效期天数（默认 3650）

set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
OUT_DIR="${1:-$SCRIPT_DIR/signing}"
CERT_NAME="${CERT_NAME:-UiharuMind Self-Signed}"
CERT_PASSWORD="${CERT_PASSWORD:-uiharumind}"
CERT_DAYS="${CERT_DAYS:-3650}"

mkdir -p "$OUT_DIR"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# codeSigning 扩展用途是必须的，否则 codesign 不认这张证书当身份
cat > "$WORK/cert.cnf" <<CNF
[req]
distinguished_name = dn
x509_extensions    = v3
prompt             = no

[dn]
CN = $CERT_NAME

[v3]
basicConstraints   = critical, CA:false
keyUsage           = critical, digitalSignature
extendedKeyUsage   = critical, codeSigning
CNF

echo "==> 生成 RSA 密钥与自签名证书（$CERT_DAYS 天）"
openssl req -x509 -newkey rsa:2048 -sha256 \
    -keyout "$WORK/key.pem" -out "$WORK/cert.pem" \
    -days "$CERT_DAYS" -nodes -config "$WORK/cert.cnf" >/dev/null 2>&1

# macOS 的 security import 只吃旧式 PKCS#12 加密。OpenSSL 3 默认用新式，得加 -legacy；
# 而 macOS 自带的是 LibreSSL，它默认就是旧式且不认 -legacy。两种都要能跑。
P12="$OUT_DIR/UiharuMind-signing.p12"
echo "==> 打包成 $P12"
if openssl pkcs12 -help 2>&1 | grep -q -- "-legacy"; then
    openssl pkcs12 -export -legacy \
        -inkey "$WORK/key.pem" -in "$WORK/cert.pem" \
        -name "$CERT_NAME" -out "$P12" -passout "pass:$CERT_PASSWORD"
else
    openssl pkcs12 -export \
        -inkey "$WORK/key.pem" -in "$WORK/cert.pem" \
        -name "$CERT_NAME" -out "$P12" -passout "pass:$CERT_PASSWORD"
fi

B64="$P12.base64"
base64 -i "$P12" -o "$B64"

cat <<DONE

✅ 完成
   证书   : $P12
   base64 : $B64

下一步（都需要你本人执行）：

1) 导入本机 keychain，让 codesign 能用这个身份：

   security import "$P12" \\
     -k ~/Library/Keychains/login.keychain-db -P '$CERT_PASSWORD' -T /usr/bin/codesign

2) 在 GitHub 仓库设置 Secrets（Settings → Secrets and variables → Actions）：

   MACOS_CERTIFICATE      = $B64 的内容
   MACOS_CERTIFICATE_PWD  = $CERT_PASSWORD
   MACOS_SIGNING_IDENTITY = $CERT_NAME
   KEYCHAIN_PASSWORD      = 任意一串临时密码

把 $P12 备份好，永久复用。丢了或重新生成，所有用户都要再授权一次。
DONE
