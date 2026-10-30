# OpenAI 兼容层按职责分层，协议留出扩展点

> **状态**：accepted（2026-10-30）。

## 起因

`Core/AI/Net/` 一个目录里平铺着 18 个文件，横跨四件事：HTTP 传输、请求体改写、响应清洗、限流排队。
`OpenAICompatibleHttpHandler` 一个类 267 行，同时干改地址、写亲和头、记状态日志、记失败正文、清洗响应；
`OpenAICompatibleRequestRewriter` 436 行，把「要改什么」和「字节怎么拼」揉在一起。找东西只能靠全局搜类名。

命名空间也在说谎：处理器放在 `AI/Net/`，声明却是 `Core.Core.LLM`。

## 决定

1. **目录与命名空间一致**，按职责分六处：

   | 目录 | 命名空间 | 放什么 |
   |---|---|---|
   | `Net/Json/` | `.Json` | `Utf8JsonSplice`：字节级拼接，请求与响应两边共用 |
   | `Net/Transport/` | `.Transport` | HTTP 层、端点地址、亲和头、限流与发送闸、请求体载体（`SegmentedBinaryContent`） |
   | `Net/Request/` | `.Request` | 改写清单（`RequestRewrite`）与执行（`…Rewriter`）、请求日志策略 |
   | `Net/Response/` | `.Response` | SSE 清洗、一次性 JSON 修正、思考字段普查 |
   | `Net/Errors/` | `.Errors` | 服务端报错的自愈识别（max_tokens、缺思考字段） |
   | `Net/Diagnostics/` | `.Diagnostics` | 日志格式（请求体 / 失败正文）与成功路径那一行响应状态 |

   `OpenAICompatibleChatClient` 留在 `Net/` 根：它是组装入口，不属于任何一层。

2. **`OpenAICompatibleHttpHandler` 只剩转发**（76 行），四个归口：
   端点地址 → `ChatCompletionUri`；亲和头与其回显 → `SessionAffinityHeaders`；
   状态行与失败正文 → `Diagnostics.ResponseDiagnostics`；响应清洗 → `ResponseSanitizer`。
   构造签名不变，调用方一行没改。

3. **`RequestRewrite` 成为「要做什么」的唯一落点**：清单本体、`For(model)`（唯一的清单工厂，
   也是唯一同时读三个 `AsyncLocal` 的地方）、`SamplingParamKeys`、`DefaultReasoningField` 全在
   `Request/RequestRewrite.cs`。改写器只保留字节拼接，反向依赖清单（`IsSampling` 读清单里的键表）。
   这样「供应商知识」不会渗进字节层，下一轮要把隐式输入收成显式快照时，接缝也只有一个文件。

4. **两个与 OpenAI 无关的东西搬走了**：`ServiceCircuit`（按外部服务名记账的熔断器，7 个消费方
   全在 WebTools / ImageGeneration）移到 `Core/Core/Resilience/`；`HttpProxyServer`（本地 `HttpListener`
   反代，全仓零引用、不经过清洗与亲和）直接删除——留在 `Transport/` 会让这一层的定义变成「和网络沾边的东西」。

5. **加密思考的观测**：`SseReasoningCensus` 逐帧统计 `reasoning_content` / `reasoning_details` /
   加密条目，流结束时并进 `SseSanitizingStream disposed` 那一行。加密 blob 不进界面、MEAI 适配器又不认
   `reasoning_details`，没有这行计数时「服务端没发」与「发了我们丢了」不可区分。
   `SseSanitizingStreamTests.ReasoningCensus_IsWiredIntoTheStream` 钉住的是**接线**，不是类本身。

## 取舍

- **没做的事**：`LlmRequestContext` 的三个 `AsyncLocal` 仍是隐式输入。改成显式快照的难点不在调用点数量
  （12 个生产文件 + 5 个测试 + 1 个基准），而在 `ChatClient` 与 `PipelinePolicy` 都是按模型长期存活、
  拿不到每请求的 `ChatOptions`——要么每请求建客户端，要么另开一条 per-request 通道。第一步只做
  「不可变快照 + `Enter` 作用域」，把所有读写收进一个文件。
  → **已由 [ADR 0100](0100-每请求上下文改为不可变快照加作用域.md) 处理**（2026-10-30）。
- **没做的事**：Chat Completions 与 Responses 两种协议的抽象。CommandCode 三条端点都开着
  （`/chat/completions`、`/responses`、`/messages`），加密思考只有在 Responses 上才有票据可回传，
  但那是新增能力，不是整理。分层已把接口留出来：`RequestRewrite` 与 `ResponseSanitizer` 各是一层，
  加协议是并列实现，不是往现有分支塞 `if`。
- 目录移动用 `git mv`，测试只改动 `using` 与一处调用点改名（`FormatDiagnosticHeaders` 移到
  `Diagnostics.ResponseDiagnostics`），**没有任何断言被改**。Core 3319 条与 App 1253 条测试全绿。
