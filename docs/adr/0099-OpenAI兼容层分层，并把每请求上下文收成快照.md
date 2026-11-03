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
   八条 `LlmRequestScopeTests` 钉住它们（含并发各挂各的、`Bind` 逐次 MoveNext 挂载）。
6. **写入点收敛到两处**：`LazyChatClient`（非流式 `Push`；流式构造一份上下文交给 `Bind` 逐次挂上，
   见文末修订。两条都用 `with` 合并，因此自动保住外层设的 `ForbidToolCalls`——写交接文档那一发正靠这个）；
   `HistoryHandoff` 用一个作用域只在那一发禁工具，删掉 `finally` 手工复位。
7. **读取点收敛**：`RequestRewrite.For(model)` 仍是生产入口（读 `Current` 一次），另加
   `For(model, context)` 纯函数给测试；`SessionAffinityHeaders.Apply/Describe` 改成**收值**，
   HTTP 处理器在 `SendAsync` 里取一次，写头与写日志用的是同一份值。

## 取舍

- **上下文为什么仍是环境传递**：`ChatOptions` 到 `PipelineMessage` 之间没有可插入的通道，改写发生在管道
  最里层、拿不到调用栈上的局部变量。所以改的不是「用不用 `AsyncLocal`」，而是把它关进一个文件、只留一个
  有作用域的入口。改成显式传参需要每请求建客户端（连接池与序列化开销全重来），不划算。
- **异步迭代器里的边界**：流式的作用域在迭代器体内挂上**只管当轮 MoveNext**，`yield` 之后回到调用方
  的执行上下文——**重发那一发读不到内层设的值**（实测与日志都这么显示，见文末修订）。所以现在不用
  「枚举外面套一个 using」，改由 `LlmRequestScope.Bind` 在每一次 `MoveNext` 上重新挂；调用方仍看不到
  内层设的值。
- **没做的事：Chat / Responses / Messages 的协议抽象。** CommandCode 三条端点都开着，加密思考只有
  Responses 上有票据可回传。分层留出的缝是**补丁形状**（`RequestRewrite` 与 `ResponseSanitizer` 都是
  「在 SDK 序列化好的 chat 体上改几处」）：对 Responses 够用，对 Anthropic Messages 不够——那边要的是
  整份翻译（`system` 提到顶层、`content` 变 block 数组、`tool_result` 换角色、SSE 事件逐类翻译），
  思考票据是 `thinking.signature` 而不是一个字符串字段。另立一项，不塞进这一轮。
- **验证**：`UiharuMind.Core.Tests` 3337 条、`UiharuMind.App.Tests` 1253 条全绿，Core 与基准项目 0 警告。
  测试改动除 `using` 外只有两处性质变化：`ReasoningSource_IsCollected…` 改用纯函数入口；
  `Client_KeepsTheContextItWasCalledFrom` 把「客户端不抹掉外层」从碰巧成立（三个独立字段）
  变成明确保证（`with` 合并）。

## 修订（2026-11-03）：流式作用域改为逐次 MoveNext 挂载

上面「覆盖整段枚举（含中途重发）」的说法**是错的**，实测与线上日志都证伪：

- **日志**：`Log.txt` 一天 1375 条响应里 14 条 `affinity: (none)`，其中 13 条紧跟 `retry 1/6`——
  每一次断线重发都没带会话亲和头（首发都带）。141 次重发里首次之外的那几次因此各自冷启动一遍前缀缓存，
  1–2 MB 的请求体白付 prefill。同一缺陷也让重发那一发拿不到票据回调（加密思考白拿）。
- **实测**：`AsyncLocal` 在异步迭代器里跨 `yield` 不保留——写下的值只在当轮 `MoveNext` 有效，
  下一次 `MoveNext` 回到调用方的执行上下文。所以「枚举外面套一个 using」对重发无效。
  外层设的 `ForbidToolCalls` / `RetryTally` 不受影响（它们本来就在调用方的上下文里），
  丢的恰好是 `LazyChatClient` 自己 `with` 进去的那两项。

改动：

1. 新增 `LlmRequestScope.Bind(context, source)`：把 push 放进**每一次** `MoveNext`，
   作用域开合与它要覆盖的那次调用同生共死，与调用方是否可见无关。
2. `LazyChatClient` 的流式路径不再在外层 push，改为把 `Current with { … }` 的上下文实参传进
   `StreamOnceAsync`，由 `Bind` 逐次挂上；`NoteRetry` 改读那份实参。
3. `LlmRequestScopeTests.Bind_PutsTheContextOnEveryMoveNext` 钉住「跨 yield 之后仍读得到」；
   `StreamRetryTests` 钉住重发那一发带亲和、带票据回调、且不漏给调用方。

同批修的另一处（不属本 ADR，一并记在这里）：`LazyChatClient` 的断线重试原先只认 `HttpIOException`，
接不住 SslStream 的裸 `IOException`（同一种断流，实测约 8:1），后者会整轮作废；判据收进
`Net/Errors/TransportFault`（另把「不带状态码的 `HttpRequestException`」也算进来——发送阶段被 reset
与流中段被掐是同一类，且前者没花掉 token，重发更划算）。

**试过又撤掉的一处**：曾加「已吐出完整工具调用就不再重发」的闸，理由是怕同一次调用被执行两遍。
实测证伪——`FunctionInvokingChatClient` 要等流走完才执行累积的调用，流中途失败时一次都不执行
（假客户端先吐 `FunctionCallContent` 再抛 IOException：执行 0 次；正常收尾：执行 1 次并继续迭代）。
这道闸只会把本来能恢复的断流变成硬失败，已删除；断流重发的真实代价只剩「上一发已经画出来的东西」

**重画的收口**：两发的内容在流里是连着的，而界面按追加装配，于是第二发会续进第一发画出的
思考卡/气泡——实际表现是同一段思考出现两遍，或者中途收过一次段、多出一张一样的卡。
重发前现在发一条 `StreamRestartContent`，`ConversationTranscript` 把本段（`_segmentFirstItem`
之后）的条目全摘掉再重画，用户自己插话的气泡不在其中；这条标记落盘前由
`SessionChatHistoryProvider` 摘掉（与加密思考信号同一处），不进历史、不回喂模型。
