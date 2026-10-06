# llama-server 参数未设置就交给上游默认，GPU 层数默认自动

> **状态**：accepted，已实现（2026-10-06）。

启动 llama-server 时，我们原先每次都显式传 `-ngl <GPU 层数>`，而这个设置的默认值是 0，界面上也写着「0 表示纯 CPU」。
于是没人动过它的话，本地模型一律跑在 CPU 上，Metal 和独显都闲着（M2 Pro 上同一个小模型，生成 43 tok/s 对比自动的 61 tok/s）。
现在的 llama-server 默认是 `-ngl auto`、`-fa auto`、`--fit on`，会按设备内存把没设的参数调到放得下。

决定：**我们只传用户改过的参数**。没改过的（等于 llama-server 默认值的）不传，让上游的默认和 `--fit` 生效。
GPU 层数分三种取法：自动（-1，不传）、纯 CPU（0）、指定层数；Flash Attention 分自动、开、关。

例外：上下文长度仍由我们自己定（自动时按模型与设备算一个保守值）并显式传 `-c`。应用要知道实际的上下文长度来做占用显示和压缩，
交给 `--fit` 去调就得另外回读，暂不值得。

## 后果

- 旧配置里的 `GpuLayers` 与 `FlashAttention` 换了 JSON 名（`GpuOffloadLayers` / `FlashAttn`），所有人回到「自动」。
  旧值绝大多数就是那个默认 0；极少数手动填过层数的人要重填一次。
- llama-server 专有选项（KV 缓存类型、加载方式、并行槽位、提示词缓存、`--fit` 余量、环境变量、额外参数等）收在
  `LLamaCppSettingConfig.Server`，与引擎无关的上下文、GPU 层数、批大小、线程留在 `ModelRuntimeSettingConfig`。
- 上游会改参数：`--mlock` / `--no-mmap` 已被 `--load-mode` 取代（Jan 还是旧的两个开关）。加选项前对着当前构建的 `--help` 核一遍，
  并真起一次 llama-server 看它认不认。
- 「额外启动参数」排在最后，与上面的设置冲突时以它为准，给上游新参数留了口子，不必每个都做界面。
- 按模型的参数（上下文、GPU 层数、批大小、线程）存在 `ModelRuntimeSettingConfig.ModelOverrides`，按模型名，null 的项跟随全局；
  不放模型清单，因为手放的模型没有清单。加载与风险估算都取 `ForModel(模型名)`。删模型文件时一并删掉它的覆写。
