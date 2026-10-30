# OpenAI 兼容层分层，并把每请求上下文收成快照

> **状态**：accepted（2026-10-30）。一次重构的两半：先把目录摆整齐，再把隐式输入收口。

## 起因

**一半是找不着东西。** `Core/AI/Net/` 一个目录里平铺着 18 个文件，横跨四件事：HTTP 传输、请求体改写、
响应清洗、限流排队。`OpenAICompatibleHttpHandler` 一个类 267 行，同时干改地址、写亲和头、记状态日志、
记失败正文、清洗响应；`OpenAICompatibleRequestRewriter` 436 行，把「要改什么」和「字节怎么拼」揉在一起。
找东西只能靠全局搜类名。命名空间也在说谎：处理器放在 `AI/Net/`，声明却是 `Core.Core.LLM`。

**另一半是上下文靠静态透传。** `LlmRequestContext` 原本是三个平铺的 `AsyncLocal` 加三个可写静态属性
（`ForbidToolCalls`、`PendingReasoningSource`、`SessionAffinity`），每个字段独立读写、没有作用域：

1. **漏复位**：上一发的「思考正文来源」闭包会一直挂在那条异步流上，下一次任何绕开 `LazyChatClient`
   的调用都会拿到它，把上一轮历史的思考贴进毫不相干的请求体。网关报的是「历史对不上」，离真正的原因隔着两层。
2. **手工复位会抹掉外层**：`HistoryHandoff` 的写法是 `= true` 发一发、`finally { = false }`，
   把「进来时是什么」一并写成 false；嵌套一次就把外层禁着的工具悄悄放开。
3. **写三处、读五处**：想知道「这一发的亲和值从哪来」得把链路走一遍；测试断言前还得先摆一个异步流。

## 决定

### 一、目录与命名空间一致，按职责分六处

| 目录 | 命名空间 | 放什么 |
|---|---|---|
| `Net/Json/` | `.Json` | `Utf8JsonSplice`：字节级拼接，请求与响应两边共用 |
| `Net/Transport/` | `.Transport` | HTTP 层、端点地址、亲和头、限流与发送闸、请求体载体（`SegmentedBinaryContent`） |
| `Net/Request/` | `.Request` | 改写清单（`RequestRewrite`）与执行（`…Rewriter`）、请求日志策略 |
| `Net/Response/` | `.Response` | SSE 清洗、一次性 JSON 修正、思考字段普查 |
| `Net/Errors/` | `.Errors` | 服务端报错的自愈识别（max_tokens、缺思考字段） |
| `Net/Diagnostics/` | `.Diagnostics` | 日志格式（请求体 / 失败正文）与成功路径那一行响应状态 |

`OpenAICompatibleChatClient` 留在 `Net/` 根：它是组装入口，不属于任何一层。

1. **`OpenAICompatibleHttpHandler` 只剩转发**（76 行），四个归口：端点地址 → `ChatCompletionUri`；
   亲和头与其回显 → `SessionAffinityHeaders`；状态行与失败正文 → `Diagnostics.ResponseDiagnostics`；
   响应清洗 → `ResponseSanitizer`。构造签名不变，调用方一行没改。
2. **`RequestRewrite` 成为「要做什么」的唯一落点**：清单本体、`For(model)`（唯一的清单工厂，也是唯一
   读请求上下文的地方）、`SamplingParamKeys`、`DefaultReasoningField` 全在 `Request/RequestRewrite.cs`。
   改写器只保留字节拼接，反向依赖清单（`IsSampling` 读清单里的键表），供应商知识不再渗进字节层。
3. **两个与 OpenAI 无关的东西搬走了**：`ServiceCircuit`（按外部服务名记账的熔断器，7 个消费方全在
   WebTools / ImageGeneration）移到 `Core/Core/Resilience/`；`HttpProxyServer`（本地 `HttpListener` 反代，
   全仓零引用、不经过清洗与亲和）直接删除——留在 `Transport/` 会让这一层的定义变成「和网络沾边的东西」。
4. **加密思考的观测**：`SseReasoningCensus` 逐帧统计 `reasoning_content` / `reasoning_details` / 加密条目，
   流结束时并进 `SseSanitizingStream disposed` 那一行。加密 blob 不进界面、MEAI 适配器又不认
   `reasoning_details`，没有这行计数时「服务端没发」与「发了我们丢了」不可区分。
   `SseSanitizingStreamTests.ReasoningCensus_IsWiredIntoTheStream` 钉住的是**接线**，不是类本身。

### 二、每请求上下文：值与环境载体分开，各有唯一一处

5. **值**：`LlmRequestContext` 变成不可变 record（三个 `init` 属性），**没有静态可写成员**——想改就得
   拿出完整的一份新快照。**载体**：`LlmRequestScope` 是全仓唯一持有那个 `AsyncLocal` 的地方，只有
   `Current` 读、`Push` 在 `using` 作用域内写；释放时还原成**挂之前的那整份引用**（含「之前没有」）。
   于是三条语义成立：退出即还原、嵌套回到外层、内层 `Current with { … }` 只叠加自己关心的字段。
   七条 `LlmRequestScopeTests` 钉住它们（含并发各挂各的、流式迭代器里也读得到）。
6. **写入点收敛到两处**：`LazyChatClient`（流式与非流式各一处，用 `with` 合并，因此自动保住外层设的
   `ForbidToolCalls`——写交接文档那一发正靠这个）；`HistoryHandoff` 用一个作用域只在那一发禁工具，
   删掉 `finally` 手工复位。
7. **读取点收敛**：`RequestRewrite.For(model)` 仍是生产入口（读 `Current` 一次），另加
   `For(model, context)` 纯函数给测试；`SessionAffinityHeaders.Apply/Describe` 改成**收值**，
   HTTP 处理器在 `SendAsync` 里取一次，写头与写日志用的是同一份值。

## 取舍

- **上下文为什么仍是环境传递**：`ChatOptions` 到 `PipelineMessage` 之间没有可插入的通道，改写发生在管道
  最里层、拿不到调用栈上的局部变量。所以改的不是「用不用 `AsyncLocal`」，而是把它关进一个文件、只留一个
  有作用域的入口。改成显式传参需要每请求建客户端（连接池与序列化开销全重来），不划算。
- **异步迭代器里的边界**：流式的作用域在迭代器体内挂上，覆盖整段枚举（含中途重发），但**调用方看不到
  内层设的值**——与改造前一致，写进注释免得下次误判。
- **没做的事：Chat / Responses / Messages 的协议抽象。** CommandCode 三条端点都开着，加密思考只有
  Responses 上有票据可回传。分层留出的缝是**补丁形状**（`RequestRewrite` 与 `ResponseSanitizer` 都是
  「在 SDK 序列化好的 chat 体上改几处」）：对 Responses 够用，对 Anthropic Messages 不够——那边要的是
  整份翻译（`system` 提到顶层、`content` 变 block 数组、`tool_result` 换角色、SSE 事件逐类翻译），
  思考票据是 `thinking.signature` 而不是一个字符串字段。另立一项，不塞进这一轮。
- **验证**：`UiharuMind.Core.Tests` 3337 条、`UiharuMind.App.Tests` 1253 条全绿，Core 与基准项目 0 警告。
  测试改动除 `using` 外只有两处性质变化：`ReasoningSource_IsCollected…` 改用纯函数入口；
  `Client_KeepsTheContextItWasCalledFrom` 把「客户端不抹掉外层」从碰巧成立（三个独立字段）
  变成明确保证（`with` 合并）。
