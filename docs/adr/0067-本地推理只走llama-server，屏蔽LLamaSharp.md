# 本地推理只走 llama-server，屏蔽 LLamaSharp

> **状态**：accepted，未实现（2026-10-06）。

本地 GGUF 原先有两个引擎：进程内的 LLamaSharp（默认）和外部 llama-server 进程。LLamaSharp 只引了 CPU 后端包，
没有 Metal/GPU、不支持工具调用、不支持视觉；llama-server 这些都有，且跟上游更新只需换一个可执行文件。
决定：**本地推理（对话与嵌入）只走 llama-server**。

「只走」是**不注册**别的本地引擎，不是把引擎写死：引擎抽象（`IModelRuntimeBackend`）保留，用哪个是配置
（`LocalEngineId`，空或未注册就落到第一个能跑的），注册的本地引擎多于一个时界面才出选择器。
发现本地 GGUF 也不归任何引擎（`LocalModelScanner`），引擎只回答「这个我能不能跑」。以后加引擎是加一个实现，不是改调用方。

LLamaSharp 不直接删：它还承担着读 GGUF 元数据（`GGufMetadataReader` 借它的原生库）。先换成纯 C# 的 GGUF 头部解析，
让它退出所有关键路径，再单独一步移除包，回滚面小。

## 后果

- 新装应用**没有任何本地引擎**，直到下载一个 llama-server。为此下载模型时若无可用引擎，顺手把推荐变体排进下载队列，
  而不是随包携带钉死版本（包体不涨；代价是首次用本地模型必须联网）。
- 引擎设置页里 llama.cpp 区块此前挂在「当前引擎是 llama.cpp」上，默认看不到；屏蔽后它常驻。
- **对话、嵌入、重排模型改为读 GGUF 头自行分类，合进同一个模型目录**，取代
  [ADR 0013](0013-磁盘布局按性质分树，旧布局就地废弃.md) 里「`EmbeddedModels/` 必须与 `Models/` 平级」那一款——那款唯一的理由是递归扫描分不清两类。
  判定：`general.architecture` 属纯编码器族（bert、nomic-bert、jina-bert、xlm-roberta、mpnet、t5encoder…），
  或 `<arch>.pooling_type > 0`（抓 Qwen3-Embedding 这种与对话模型同架构的）；其中 `pooling_type = 4`（RANK）是重排模型，两边都不列。
  老的 `EmbeddedModels/` **不再认**，不搬、不兼容，用户自己挪。
