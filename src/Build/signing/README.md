# 代码签名证书

这个目录放签**每一次** macOS 构建的自签名证书 —— 本机（`buildMac.sh`）和 CI
（`.github/workflows/release.yml`）用的是同一张。

## 为什么必须是证书，不能是 ad-hoc

macOS 的 TCC（辅助功能 / 屏幕录制授权）把权限钉在 app 的**指定要求**
（Designated Requirement）上：

| 签名方式 | 指定要求 | 重新编译后 |
|---|---|---|
| ad-hoc | `cdhash H"..."` | 变了，授权失效（列表里条目还在，但不生效） |
| 自签名证书 | `identifier "com.cwhisme.uiharumind" and certificate leaf = H"..."` | 不变，授权保留 |

这不是 Developer ID：Gatekeeper 首次打开仍提示「无法验证开发者」，那部分需要
付费 Apple Developer 账号。证书只解决权限持久性。

## 文件（已 git-ignore，永不提交）

- `UiharuMind-signing.p12` —— 证书与私钥。**备份好。**
- `UiharuMind-signing.p12.base64` —— 上面那个的 base64，用于 GitHub Secret。

## 首次生成

```sh
Build/generateSigningCert.sh
```

## 新机器上启用

```sh
security import Build/signing/UiharuMind-signing.p12 \
  -k ~/Library/Keychains/login.keychain-db -P uiharumind -T /usr/bin/codesign
```

证书不在 keychain 时，`buildMac.sh` 会回退到 ad-hoc 并打警告（包能跑，但授权
不持久）；CI 上则直接失败，避免发出身份不对的 Release。

## CI

仓库 Secrets（Settings → Secrets and variables → Actions）：

- `MACOS_CERTIFICATE` —— `UiharuMind-signing.p12.base64` 的内容
- `MACOS_CERTIFICATE_PWD` —— p12 密码
- `MACOS_SIGNING_IDENTITY` —— 证书通用名，默认 `UiharuMind Self-Signed`
- `KEYCHAIN_PASSWORD` —— 任意临时字符串，CI 建临时 keychain 用

## 重要

丢掉或重新生成这张 `.p12` 会让 app 换一个身份，**所有用户都要再授权一次**。
备份好，永久复用。
