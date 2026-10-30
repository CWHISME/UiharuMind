# Anthropic Messages 协议接入计划

> 状态：**第一步已落地**（接线三处 + 客户端 + 思考块归一）。
> 本文记录这条增量路的实测依据、已做的部分与还欠着的部分。
>
> **前置**：模块边界、四条不变量、「上次为什么错」的教训都在
> [ADR 0100](../adr/0100-加密思考票据按消息各挂各的，随消息落盘，收成协议无关的思考模块.md) 里。
> 本文只写 Messages 这条**增量路**——对应 ADR 0100「本期不做」那一节，现在那一节要改口了。

## 一、上游是怎么做的（先看清再动手）

上游 agent-framework 的 Anthropic 支持分两个语言、两种形态：

| | 位置 | 形态 |
|---|---|---|
| **Python** | `python/packages/anthropic/agent_framework_anthropic/_chat_client.py` | **1904 行手写协议实现** |
| **.NET** | `dotnet/src/Microsoft.Agents.AI.Anthropic/` | **4 个文件薄封装**，实际协议在官方 `Anthropic` NuGet 包（上游钉 12.53.0）的 `AsIChatClient()` 里 |

所以 .NET 侧落地必然走官方 SDK——**与 Responses 线用 OpenAI SDK 是同一个套路**。
Python 侧那 1904 行不是要照抄的模板，是**语义参考**。

### 参考里最要紧的一段：票据与思考正文是同一个块

Anthropic 的 thinking 是**一个块**，签名（`signature`）是块里的字段，不是独立的块。
上游 Python 的请求组装（`_chat_client.py:1062-1084`）：

```python
case "text_reasoning":
    if content.text is None:                       # 只有签名的一条
        if content.protected_data and a_content and a_content[-1].get("type") == "thinking" \
           and "signature" not in a_content[-1]:
            a_content[-1]["signature"] = content.protected_data   # ← 补回上一块
        continue
    thinking_block = {"type": "thinking", "thinking": content.text}
    if content.protected_data:
        thinking_block["signature"] = content.protected_data
    a_content.append(thinking_block)
```

响应侧（`:1703-1718`）：

```python
case "thinking" | "thinking_delta":
    Content.from_text_reasoning(text=content_block.thinking,
                                protected_data=getattr(content_block, "signature", None), ...)
case "signature_delta":
    Content.from_text_reasoning(text=None, protected_data=content_block.signature, ...)
```

**`protected_data` ↔ `thinking.signature`**——和我们 ADR 0100 用的
`TextReasoningContent.ProtectedData` 是**同一个字段、同一个语义**。
所以 `AI/Reasoning/` 一行都不用改：门禁、落盘载体、四条不变量原样成立。

## 二、实测（官方 SDK 13.3.0 + MEAI 10.10.0，本机假 HttpClient 抓请求体）

> 实测用的是本机假 `HttpClient`（不发真实请求、不花钱），结论**已由
> `AnthropicMessagesWireTests` 13 条测试钉死**——SDK 换版本时测试会先红，
> 所以不再另留一份探针脚本（`probe/` 下那批是打真实端点的，形态不同）。

### 请求侧：`ProtectedData` → `signature`，完全同构

| 消息里的思考内容 | SDK 发出的 wire |
|---|---|
| 正文，无签名 | `{"type":"thinking","thinking":"正文","signature":""}` |
| 正文 + `ProtectedData="SIG"` | `{"type":"thinking","thinking":"正文","signature":"SIG"}` ✓ |
| **拆开的两条**（正文一条、签名一条） | **两个块**：`{"thinking":"正文","signature":""}` + `{"thinking":"","signature":"SIG"}` |
| 只有签名 | `{"type":"thinking","thinking":"","signature":"SIG"}` |

### 响应侧：非流式一条，流式三条

- **非流式**：`TextReasoningContent(Text="思考正文", ProtectedData="SIG123")`——一条，内容完整 ✓
- **流式**：**三条**——`Text="" ProtectedData=""`（`content_block_start`）、
  `Text="思考正文" ProtectedData=""`（`thinking_delta`）、
  `Text="" ProtectedData="SIG123"`（`signature_delta`）

### 三个由此坐实的结论

1. **官方 SDK 不做合并。** 拆开的两条原样回填 → 请求体里两个 thinking 块，
   头一个 `signature` 是**空串**——Anthropic 拒收带空签名的 thinking 块，整发 400。
   **合并逻辑必须我们自己写。**
2. **`redacted_thinking` 靠 `RawRepresentation` 认。** SDK 自己解出来的脱敏块回填时
   发成 `{"type":"redacted_thinking","data":"…"}` ✓；但落盘往返后
   `RawRepresentation`（provider 原始对象引用）没了，就退化成「正文为空的普通 thinking 块」
   ——与服务端给的原块对不上，同样 400。
3. **正文为空的密文就是脱敏思考。** Anthropic 的普通 thinking 块必有正文
   （流式第一帧的空 thinking 块**不带签名**，会被空思考判据丢掉），
   所以「正文为空 + 有密文」在这条线上唯一可能的来源就是 `redacted_thinking`。

### SDK 侧的接线点

| 要点 | 结论 |
|---|---|
| 自定义传输层 | `ClientOptions.HttpClient` ✓——我们的 `WireHttpHandler` 能直接当内层 |
| 发送闸 / 重试策略 | **不是 System.ClientModel 管道**，插不进 `SendGatePolicy` / `RateLimitAwareRetryPolicy` |
| 思考预算 | `AsIChatClient(..., thinkingMode)` 有 `Adaptive`/`Extended` 两档；预算本身要经 `RawRepresentationFactory` 设 `MessageCreateParams.Thinking` |
| 输出预算 | `AsIChatClient(modelId, maxOutputTokens)` ✓ |

## 三、已落地（第一步）

| 改动 | 位置 |
|---|---|
| 包引用（官方 SDK，Apache-2.0） | `UiharuMind.Core.csproj` |
| 地址归一：`ResolveMessages` / `IsMessages` | `AI/Net/Transport/WireProtocol.cs` |
| 格式识别与地址分流 | `AI/Net/Transport/WireHttpHandler.cs` |
| 客户端分流 + 思考块归一层 | `AI/Net/ChatClientFactory.cs` |
| **思考块归一**（正文与签名合成一条；脱敏块还原） | `AI/Net/AnthropicThinkingNormalizer.cs`（新） |
| 发送闸下沉到 HTTP 层 | `AI/Net/Transport/SendGateHandler.cs`（新） |
| **门禁统一过滤**（三条线共用，摘掉换模型后的旧票） | `AI/Reasoning/ReasoningGateFilter.cs`（新） |
| 目录推导：`/messages` → `EApiFormat.Messages` | `Features/Models/RemoteModelEditViewData.cs` |
| 端点按模型声明的格式补全 | `AI/Runtime/Backends/OpenAICompatible/RemoteModelManager.cs` |
| 输出预算口径统一 | `AI/Models/ILLMModel.cs`（新增 `MaxTokens`） |
| `ProviderId` 取真正的供应商配置类名 | `AI/Models/RemoteModelInfo.cs`、`Configs/Core/BaseRemoteModelConfig.cs` |
| 思考参数注入（token 预算） | `AI/Net/Request/AnthropicThinkingHandler.cs`（新） |
| 分段请求体（HttpContent 版） | `AI/Net/Transport/SegmentedHttpContent.cs`（新） |

**目录侧一处老决定要改口**：`/messages`-only 的条目原来被过滤掉（"v1 只支持 chat 线"）。
Anthropic 线接上后不再滤，改为推成 `EApiFormat.Messages`。

## 四、还欠着的（按优先级）

### ① 门禁在 SDK 直读 `ProtectedData` 的协议上不生效 —— **已修**（跨三条线）

门禁（provider + model 双等）原先**只在 `LazyChatClient.CollectTickets` 一处**生效，
那是 chat 线专用的（它把票据按助手序号收集起来，交给 chat 的字节级改写器拼回）。

而 **responses 线与 messages 线的密文是 SDK 自己从消息里读的**
（`ProtectedData` → `encrypted_content` / `signature`），**不经过那道门禁**。
后果：换模型后，旧模型签的票会被一起发过去——正是 ADR 0100 不变量 3 要防的事。

实测过本网关**不校验票据归属**（把票发给别的模型照样 200），所以发错了不会报错，
只会安静地错（模型拿到读不懂的密文，轻则忽略、重则误解上下文）。

**修法**：新增 `AI/Reasoning/ReasoningGateFilter.cs`，在 `LazyChatClient` 把消息交给
底层客户端**之前**统一过滤——门禁不过就把密文摘掉。

两条口径：

- **摘掉的是发给底层客户端的那一份**，历史里的原消息一个字节都不动。
  所以换回原模型时票还在、还能接上（ADR 0100：门禁不过时不能删票，只是这一轮不带）。
- **密文摘掉后正文也空了的，整条丢掉**——各协议都不收空思考块
  （responses 侧那是一条没有 `encrypted_content` 的 reasoning item，
  Anthropic 侧是一个空签名的 thinking 块——后者直接被服务端拒）。

零克隆是常态：门禁通过时原样返回入参那一份，只有真要摘时才重建受影响的那几条。

#### 顺带修掉一个真 bug：`ProviderId` 取的是容器名

门禁要比 provider，而 `ProviderId` 原先取的是 `GetType().Name`。
`RemoteModelInfo` 没覆盖它 → 对**所有**远程供应商都叫 `"RemoteModelInfo"`。
也就是说 ADR 0100 定的「provider + model 双等」里，**provider 那一半一直形同虚设**——
只有 model 在起作用。

修法：`RemoteModelInfo.ProviderId` 委托给 `Config`（真正的供应商配置类，
如 `RemoteAgnesModelConfig` / `RemoteCommandCodeModelConfig`）。

**老数据兼容**：老会话落盘的票 provider 是 `"RemoteModelInfo"`——那个值**没有区分力**
（所有供应商都一样），这类票只能退到「只比 model」。
不这么兼容的代价是所有老会话的加密票据一次性全失效、推理链集体断掉，
而它们本来就没被门禁保护过。

### ② 限流与重试的对齐 —— **闸已接，重试未对齐**

`SendGate` 的核心逻辑（间隔、排队、429 记账）是协议无关的，只有挂它的
`SendGatePolicy` 是 System.ClientModel 特有的。闸已经下沉到 HTTP 层
（`SendGateHandler`），这条线上的请求不再不受闸约束。

还差：

- **重试策略未对齐**：Anthropic SDK 自己实现重试（默认 2 次、指数退避、认 `Retry-After`），
  我们的 `RateLimitAwareRetryPolicy` 用不上。两者叠加时的行为要单独验一遍。
- **重试不插队**：HTTP 层认不出「这次是重试」（SDK 的重试是重新发一次 HTTP 请求，
  从这里看就是一次全新的发送），只能排到队尾。管道版能排到队首。

### ③ 思考参数 —— **已落地**

Anthropic 要 `thinking: {type:"enabled", budget_tokens: N}`，不是 `reasoning_effort`。
且开了 thinking 之后：`temperature` 必须为 1（我们不发这个字段，默认就是 1）、
`max_tokens` 必须严格大于 `budget_tokens`、且预算不小于 1024。

**走不了 SDK 的原生参数入口**：官方 SDK 的 `RawRepresentationFactory` 默认返回 `null`
（意思是「用默认路径造请求体」），拿不到它造好的那份；而 `MessageCreateParams.Thinking`
是 init-only 属性，只能在对象初始化器里设——要用它就得自己再造一份 `MessageCreateParams`，
那意味着把 ChatMessage → MessageParam 的整套翻译自己写一遍。

所以走**字节级改写**（`AnthropicThinkingHandler`，和 chat 线同一套路），
在请求体根对象末尾追加一个 `thinking` 属性，其余字节一字不动。

档位 → token 预算的映射（`AnthropicThinkingHandler` 里）：

| 档位 | 预算 |
|---|---|
| low | 2048 |
| medium | 8192 |
| high | 16384 |
| xhigh | 32768 |
| max | 65535 |

两条兜底：**「不干预」不写 `thinking`**（与另外两条线同一口径：没选档位就别替模型做决定，
Anthropic 的默认是不思考）；**预算装不下就不写**（输出预算装不下最小预算时，
发一个服务端会拒的组合比不思考更糟）。

另有一处：**输出预算的默认值不能用 SDK 的 1024**——思考预算必须严格小于输出预算，
而 Anthropic 的最小思考预算也是 1024，两者相等就永远开不了思考。
没填 `MaxTokens` 时用 32000（Claude 各版本都接受的量级，也给思考留得下 16k 以上）。

### ④ 改名（可读性，不改行为）—— ✅ 已完成

`AI/Net/` 下三个名字混着两类东西，三条协议都接上之后「OpenAICompatible」这个名字
已经指着三个不同的协议，纯属误导：

| 原名 | 实际职责 | 现名 |
|---|---|---|
| `ChatClientFactory` | 按格式分派 | `ChatClientFactory` |
| `WireHttpHandler` | 改地址/亲和头/日志——**协议无关** | `WireHttpHandler` |
| `ChatCompletionsRequestRewriter` | 拼 `reasoning_details`/`tool_calls`——**chat 特有** | `ChatCompletionsRequestRewriter` |
| `ChatCompletionsResponseFixer` | `finish_reason`/`tool_calls.type` 改名——**chat 特有** | `ChatCompletionsResponseFixer` |

目录结构不动（沿用 ADR 0100 决定 1「按语义切，不按协议切目录」）。
原计划估「约 10 处引用」，实际是 32 个文件、141 处——估少了十倍。

**顺带收掉的一处结构问题**：wire 格式原先有两个来源（`RemoteModelManager` 按
`model.ApiFormat` 补一次地址、`WireHttpHandler` 再按地址字符串猜一次），两处得手工对齐。
现在 `WireHttpHandler` 收 `EApiFormat?`：**有模型就传模型的声明，不传才退到按地址猜**
（只给拿不到模型的调用方：本地服务、模型目录、状态探测）。
同时 `ResponseSanitizer` 的判据从「不是 Messages 就洗」改成「只有 chat 洗」——
responses 与 messages 的响应形状由各自 SDK 解析，套上 chat 那层不只是白扫每行 JSON，
`SseSanitizingStream` 还会因为找不到 `data: [DONE]` 把正常收尾误报成「连接被掐」。

## 五、已知代价

- **`redacted_thinking` 靠形态判**：落盘往返后 `RawRepresentation` 没了，
  归一器按「正文为空 + 有密文」把它还原成脱敏块。判错的话两边都 400，
  但按形态判至少把真脱敏那一种修好了。
- **地址要填到端点那一层**：格式按地址判（handler 按端点长期存活，地址才是它实际要打的），
  与 responses 线同规矩。`RemoteModelManager` 会按模型声明的格式补地址，
  但用户自己填了别的路径时以用户为准。
- **真实调用未验证**：这条线只做了「假 HttpClient 抓请求体」的实测——
  官方 SDK 的行为（签名往返、块形状、参数写法）都验过了，但**没有真的打过一次 Anthropic**。
  这条线的思考预算、输出预算上限、签名在真实服务端的接受度都还需要一次真实调用确认。
- **重试策略未对齐**：Anthropic SDK 自己实现重试（默认 2 次、指数退避、认 `Retry-After`），
  我们的 `RateLimitAwareRetryPolicy` 用不上——`ClientOptions` 只有 `MaxRetries` 一个数字，
  没有策略注入点。**但超时对齐了**：SDK 默认 10 分钟且管到「读完响应体」，
  与另两条线的 `Timeout.InfiniteTimeSpan` 冲突，已显式关掉。
