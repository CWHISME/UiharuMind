# 打包与发版，macOS 签名必须用自签名证书

打包与发版的口径定版并收在一处：**版本号唯一来源**、**发布参数收在 csproj 按 RID 生效**、
**构建脚本与 CI 共用一套打包逻辑**、**macOS 签名必须用自签名证书而不是 ad-hoc**。

## 为什么

- **版本号不能有第二份。** `AppInfo.Version` 从程序集读、`Info.plist` 由脚本填、CI 拿 tag 校验，
  一旦有人另写一份，tag、包名、应用内版本就各说各话。
- **发布参数不能每个脚本各抄一份。** 三平台打包参数不同，收在 csproj 里按 RID 生效后，
  脚本与 CI 只给 `-r <RID>`，打包参数只有一份，不会漂移。
- **不能开 `PublishTrimmed` / NativeAOT。** 全仓默认反射绑定
  （`AvaloniaUseCompiledBindingsByDefault=false`，90 个 axaml 只有 30 个有 `x:DataType`）
  加上反射 JSON，裁剪与 AOT 造成的不是编译期错误，而是**静默的运行时失败**——发出去才坏。
- **macOS 签名不能用 ad-hoc。** TCC（辅助功能 / 屏幕录制授权）把权限钉在 app 的**指定要求**
  （Designated Requirement）上。ad-hoc 没有颁发者，系统退化成用 cdhash 当身份，而 cdhash
  每次编译都变——表现是「权限列表里条目还在，但权限已失效」。自签名证书的指定要求是
  `identifier "com.cwhisme.uiharumind" and certificate leaf = H"..."`，cdhash 照变但
  DR 不变，授权跨版本保留。

## 决定

1. **版本号唯一来源**是 `UiharuMind/Directory.Build.props` 的 `<Version>`。`AppInfo.Version`
   从程序集读、`Info.plist` 由脚本填、CI 拿它校验 tag——都别再写第二份。
2. **发布参数收在 `UiharuMind.Desktop.csproj`**（自包含、ReadyToRun、单文件），按 RID 生效；
   脚本与 CI 只需给 `-r <RID>`。
3. **不开 `PublishTrimmed`，也不上 NativeAOT**。前置条件是先把编译绑定（`x:DataType`）与
   JSON source-gen 做完，那之前别碰。
4. **`UiharuMind/Build/` 下四脚本一张表**，共享部分在 `common.sh`（取版本、发布、签名、打包）：

   | 脚本 | 产物 |
   |---|---|
   | `buildMac.sh` | `Output/UiharuMind-<版本>-osx-arm64.zip`（.app bundle，自签名证书签名） |
   | `buildMacFull.sh` | 同上加 `-full` 后缀，额外带入本机的 Runtime/模型（缺则跳过） |
   | `buildLinux.sh` | `Output/UiharuMind-<版本>-linux-x64.tar.gz` |
   | `buildWin.bat` | `Output/UiharuMind-<版本>-win-x64.zip` |

5. **macOS 的 bundle 签名必须是最后一步**：签完再往里拷东西，密封就失效。
6. **macOS 签名必须用自签名证书，不能用 ad-hoc**。证书本地 `openssl` 生成、永久复用，
   不需要 Apple 账号（Gatekeeper 的「无法验证开发者」不受影响，那需要付费 Developer ID）。
   生成与启用见 [Build/signing/README.md](UiharuMind/Build/signing/README.md)。
   **别重新生成**——换证书等于换身份，所有用户要再授权一次。
7. **证书不在 keychain 时**：本机回退 ad-hoc 并打警告（fork 与新机器仍能构建）；
   CI 上（`CI=true`）硬失败，避免发出身份不对的 Release。
8. **CI（`.github/workflows/`）**：`ci.yml` 在 push/PR 上构建加测试；`release.yml` 由
   `v*` tag 触发，先断言 tag 与 `<Version>` 一致，再在三平台上**调用 Build/ 脚本**
   （打包逻辑只有一份），产物加 `SHA256SUMS` 发成草稿 Release。

## 取舍

- **自签名证书 vs 付费 Developer ID**：Developer ID 才能过 Gatekeeper 的「无法验证开发者」，
  需要付费账号；自签名本地生成、永久复用、零成本，代价是用户首次打开需手动放行。
- **ad-hoc 看似省事，但身份不稳**：每次编译 cdhash 都变，TCC 授权跨版本失效；
  自签名证书的 DR 不变，授权跨版本保留。
- **为什么三平台打包逻辑只有一份**：CI 直接调用 `Build/` 脚本，而不是在 workflow 里再写
  一套，避免两处漂移。

## 已知代价

- **Mac 包未经公证**，用户首次打开需手动放行——放行说明写在 Release notes 里。
- **fork 与新机器没有证书时回退 ad-hoc**：本机能构建、能跑，但产出的包身份不对，
  TCC 授权跨版本不保留。
- **证书换一次 = 换一次身份**，所有用户要重新授权，所以「别重新生成」。

## 关联

- [ADR 0027](0027-应用根搬到-uiharu.md)：`buildMacFull.sh` 的硬编码旧根随应用根搬迁
  同步更新，脚本细节以 `Build/` 为准。
