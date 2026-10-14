# AOT 门：编译进 CI，冒烟留本地

AOT 旁路通道（osx-arm64，`buildAotMac.sh`）的门禁分两层，各判各的、不互相冒充。

## 为什么分两层

「零反射 + 裁剪分析」要证两件事：

1. **代码改了 AOT 还能编过** —— 纯编译，无头 runner 能诚实判绿。
2. **AOT 产物跑起来不静默挂** —— 需要真实模型 + GUI + 签名 .app + TCC 授权，
   无头 runner 缺这三样，判不了。

硬把第 2 件塞进 release job，结果只会是「job 恒红」或「假绿后发出去才坏」——
比人肉验证更糟。所以第 1 件进 CI、第 2 件做发版前本地必经一步。

## 第 1 层：CI 编译门禁

- Workflow：[`.github/workflows/aot-gate.yml`](../../../.github/workflows/aot-gate.yml)
- 触发：push / PR 到 `main`（与 ci.yml 同口径，含并发取消）。
- 跑：`dotnet publish -r osx-arm64 -p:PublishAotOverride=true -p:PublishReadyToRun=false`，
  参数与 `buildAotMac.sh` 完全一致（`PublishAotOverride` 是 csproj 里的条件开关，
  命令行传 `-p:PublishAot=true` 会污染 netstandard 生成器报 NETSDK1207，别改回）。
- 只到 publish 为止：不签名、不出包、不上传产物。签名在 CI 会因缺证书硬失败，
  而「能不能编」不需要签名——签名是发布通道的事，不是门禁的事。
- 第三方 trim/AOT 分析器 warning（Ursa、PuppeteerSharp 等）不阻断，只挡「编不过 + 静态分析报错」。
- osx-only：Native AOT 走 osx-arm64，Windows/Linux 没有 AOT 通道，此门禁不覆盖它们。

## 第 2 层：发版前本地冒烟 gate

- 脚本：[`aot-gate.sh`](aot-gate.sh)
- 跑：`buildAotMac.sh`（编译 + 签名 + 打包）→ 用产出的 AOT .app 跑 `brain` 冒烟 → 判绿才过。
- 标准是**冒烟跑绿，不是编译过**——编译绿只过第 1 层，运行时静默挂要第 2 层兜。
- 前置：本机 keychain 有签名证书（`buildAotMac.sh` 本机不传 CI，会自签名）、
  `~/.uiharu/Config` 有可用模型（`brain` 真调模型，花钱）。

```bash
src/scripts/aot/aot-gate.sh            # 完整：重新 buildAotMac + brain 冒烟
src/scripts/aot/aot-gate.sh --skip-build   # 只用已有 .app 跑冒烟（刚 buildAotMac 过后省 6 分钟）
```

## 发版纪律

打 tag 触发 release 前，本机先跑一次完整 `aot-gate.sh` 且绿。
「AOT 还能编」CI 每笔提交都盯着，「AOT 跑得动」发版那一刻本地确认一次。

## 现状（2026-10-14）

- 第 1 层：`aot-gate.yml` 已落，待 push 后首跑确认。
- 第 2 层：`aot-gate.sh` 完整跑过一次全绿（buildAotMac 编译+签名+打包 + brain 冒烟
  「全部步骤成功」，产物 `Output/UiharuMind-AOT.app`，冒烟输出
  `/tmp/uiharu-smoke/brain-20261014-105408`）。代码比 .app 新后得重跑。
- 注：`--skip-build` 只在你**刚**跑过 `buildAotMac.sh` 且代码没动过时用；
  代码比 .app 新时用它会验到旧二进制，等于没验。
