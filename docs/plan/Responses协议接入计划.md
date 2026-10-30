# Responses 协议接入计划

> 状态：待实施。chat 侧票据回填**已落地**（提交 `9dabb647`，随消息落盘），
> 本文只讲 Responses 这条增量路。
> 前置结论见文末「实测依据」。
>
> **注意**：票据回填本身（chat 侧）已由 [ADR 0100](../adr/0100-加密思考票据按消息各挂各的，随消息落盘，收成协议无关的思考模块.md)
> 定案——模块边界、四条不变量、「上次为什么错」的教训都在那里。
> 本文只写 Responses 这条**增量路**（对应 ADR 0100「本期不做」那一节）。

## 探测结果（已跑，2026-10-30）

网关六项全通，阻断项解除：

| 检查 | 结果 |
|---|---|
| 声明端点 | `/chat/completions`、`/responses` 两个都在 |
| `/chat/completions` 响应 | `reasoning_details` 1 条：`{type:"reasoning.encrypted", data:"Q-PaD…"（5522 字符）, id:"rs_…:rs_…", format:"…", index:0}` |
| 流式 delta | 4 帧里有 1 帧带同一条 `reasoning_details`——应用走的这条路上拿得到 |
| 票据原样塞回下一轮 chat | **200**，正常回 |
| `/responses` + `include` | 同样下发 `encrypted_content`（同一个 blob），`summary: []` |
| Responses 侧回放 | **200** |
| 把某模型的票据发给另一个模型 | **也 200** —— 网关不校验模型归属 |

与真实日志吻合：本地请求体日志里的票据 `data` 1426 字符、
`id` 形如 `rs_<响应id>:rs_<条目id>`、`index: 0`。

### 探测结果带来的三个修订

**修订一　responses 也要回填密文。** 「换 responses 就不用自己回填了」这个前提不成立——
responses 侧同样下发 `encrypted_content`、同样要塞回 `input`。opencode 也是这么做的
（`packages/ai/src/protocols/open-responses.ts:655-665`）。

**修订二　客户端门禁不能省，且必须更严。** 网关不校验模型归属（把票发给别的模型也 200），
说明「发错了不会报错，只会安静地错」。门禁必须是 **provider + model 双等**
（对齐 opencode `packages/core/src/session/runner/to-llm-message.ts:156-161`）。

**修订三　真正的解法与协议无关。** 上次翻车的根因不是「chat 协议不行」，而是
「只挂最后一条 + 用一次即消费」这个生命周期设计错了。正解是
**每条助手消息永久挂自己那张票，逐轮原样重发**——chat 与 responses 同理。
换协议不能替代这个修正。

### 关于 item id 的一个澄清

原计划写「responses 侧票据钉在 item id 上，位置天然稳定」——**这个说法不准确**。
实测 `id` 形如 `rs_<响应id>:rs_<条目id>`，**每轮都不同**（每轮是新 response、新 item）。

opencode 的做法澄清了正确语义：**id 与密文分开存**（`open-responses.ts:956`、`:1011`
把 `itemId` 与 `reasoningEncryptedContent` 并列存进 providerMetadata），回填时
id 从本地存的来、密文也从本地存的来——**id 不需要跨轮稳定**，它只是这一条 item 的标识。

所以定位稳定性的真正来源不是 id，而是**「每条消息各挂各的票、不动别人的」**这条不变量。
这在 chat 侧同样做得到（不必换协议），代价只是没有 item id 这种天然锚点。

## 背景

`3f51fca1` + `e71469da` 实现了 chat 侧的加密思考票据回填（`reasoning_details` 原样切下再拼回）。
实测证明它**每轮都在破坏前缀缓存**，已还原：

| 证据 | 数据 |
|---|---|
| 真实日志（同一会话连续三发） | 票挂下标 2 → 4 → 6，每轮移动；分叉点恰好是上一轮挂票的位置 |
| 跨模型统计 | 纯追加请求 249 样本，真命中 221 发，平均 **88.2%**；真实分叉 3 样本，平均 **35.3%** |
| 分发点命中 | `#12` 71.1% → `#13` **1.5%** → `#14` 无用量（冷） |

根因：票据只挂**最后一条**助手消息且**用一次即消费**，位置逐轮漂移 → 上一轮挂过票的那条消息字节变了 → 从那条起缓存全冷。

还原后历史字节从头到尾稳定（无票 → 无变化），前缀恢复纯追加。

## 目标

两件事，**顺序不能颠倒**：

1. **把加密思考接回来**，且不重蹈分叉覆辙
2. **走 `/responses` 端点**（顺带拿到的收益，不作为前提）

### 为什么 responses 仍然值得做

不是因为「免回填」（探测已否），而是这三条：

- **流式可用**：探测证实流式 delta 里就有 `reasoning_details`（4 帧里 1 帧），
  应用当前走的就是流式这条路
- **item 粒度原生**：responses 的 `input` 是 item 数组，每条 reasoning item 自带 `id`，
  票据挂在 item 上比挂在「第几条助手消息」上更不容易错位——虽然这个优势**不如原来想的那么大**
  （id 每轮都变，见上文澄清）
- **明文摘要**：`summary` 字段能给界面显示思考摘要（本次探测是空数组，但字段存在）

### 真正解决分叉的东西（与协议无关）

上次失败的根因是生命周期设计，不是协议。正确设计三条不变量：

1. **每条助手消息永久挂自己那张票**——不挂最后一条，不漂移
2. **逐轮原样重发**——历史消息的票据字节从产生那刻起就不再变
3. **换模型时整条丢弃**——provider + model 双等，不一致就不带

这三条在 chat 侧同样做得到。所以**第 3、4 步（票据回填）的核心逻辑可以先在 chat 侧落地**，
协议换不换都不影响；responses 只是多一条路，不是前置条件。

### responses 侧的协议约束

- `store: false` 下 `previous_response_id` 不可用（"Response with id=… not found"，
  opencode `packages/ai/src/providers/xai.ts:48-50`）→ 走 `store:false` + 回填
- 必须带 `include: ["reasoning.encrypted_content"]` 才会下发密文
- `summary: []` —— 本次探测没有明文摘要，界面上只能开空卡

## 现状探测结论

### 已留好的接口

| 位置 | 作用 | 怎么用 |
|---|---|---|
| `AI/Net/Transport/ChatCompletionUri.cs` | 地址归一 + 判响应形状 | 加 `ResolveResponses` / `IsResponses`，与 `Resolve` / `IsChatCompletions` 并列 |
| `AI/Net/OpenAICompatibleChatClient.cs` | 建 SDK 客户端、装 pipeline 策略 | 加一个建 Responses 客户端的分支 |
| `AI/Net/Request/OpenAICompatibleRequestPolicy.cs` | 请求体按字节改写 | Responses 的 `input` 是 item 数组，与 `messages` 完全不同，需要独立的改写器 |
| `AI/Net/Response/ResponseSanitizer.cs` | 响应清洗（流式 + 一次性） | responses 的 SSE 事件逐类翻译，与 chat 的 `delta` 完全不同，需要独立的解析 |
| `AI/Models/ILLMModel.cs` | 供应商能力声明 | 加一项决定走哪个协议（chat / responses） |
| `AI/Runtime/Backends/OpenAICompatible/Configs/RemoteCommandCodeModelConfig.cs` | CommandCode 配置 | 已有 `SupportsPromptCacheKey => true`，可作协议开关的落点 |

### 没有的

- **没有协议抽象**。ADR 0099 明确写了：「没做的事：Chat / Responses / Messages 的协议抽象」，
  并指出当前分层是**补丁形状**（`RequestRewrite` 与 `ResponseSanitizer` 都是「在 SDK 序列化好的 chat 体上改几处」），
  「对 Responses 够用，对 Anthropic Messages 不够」。→ Responses 要新写请求体与响应解析，**不是**在现有改写器上加字段。
- **Core 层没有任何 `/responses` 端点代码**。
- **`ChatCompletionUri.IsChatCompletions` 已经有 `/v1/responses` 的排除用例**
  （`ChatCompletionUriTests.cs:41`）——说明「知道 responses 这个端点存在」，但没有实现。
- **缓存 TTL：Core 层零实现**。`prompt_cache_key` 有（`RequestRewrite.cs:93`，由
  `SupportsPromptCacheKey` 控制，CommandCode 与 OpenRouter 打开），但 TTL 参数
  （`prompt_cache_retention` / `prompt_cache_options.ttl`）只在 `src/scripts/probe/cache/ttl-probe.py` 里被探测过，
  从未进过产品代码。

## 待决问题

### 1. ~~网关给不给 `encrypted_content`？~~ → 已解决

探测已跑（见上），六项全通，responses 路线可行。**此项不再是阻断项。**

### 2. 缓存 TTL 参数网关认不认？（独立优化项，未跑）

`ttl-probe.py` 已写好 6 组对照（基准 / `prompt_cache_key` / `retention=24h` / `retention=in-memory` /
`options.ttl=24h` / `retention=1h` 非法值对照），需要 `CMD_API_KEY` 跑。

**结论（2026-10-30 实测）：TTL 不是当前瓶颈，此项降级为背景知识，不再单独排期。**

原探测（`ttl-probe.py` 六组连发）**测不出 TTL**，因为设计有两个错：

1. **每组换了 `prompt_cache_key` 的值**（`ttl-probe-0001` … `0005`）→ 每组都是不同前缀，
   本该命中却拿到 0；后三组拿到 753 是因为它们碰巧共享了可命中的前缀，与参数无关
2. **六次连发几乎没有时间跨度** → TTL 是时间维度的事，零间隔测不出任何 TTL 语义

真实日志给出的答案（315 发、883 分钟跨度、同亲和值内相邻两发）：

| 间隔 | 样本 | 平均命中 | 中位 |
|---|---|---|---|
| 0–30 秒 | 90 | 79.4% | 93.5% |
| 30–120 秒 | 114 | 87.2% | 95.8% |
| 2–10 分钟 | 30 | 86.1% | 96.4% |
| 10–60 分钟 | 2 | 64.2% | — |
| >60 分钟 | 1 | 0% | — |

具体样本：间隔 456 秒命中 91.1%、397 秒 99.5%、1635 秒（27 分钟）98.5%、3951 秒（65 分钟）0%。

**即：十分钟内命中不随时间衰减**，网关的前缀缓存 TTL 明显长于 OpenAI 官方默认（5–10 分钟），
或者按会话亲和做了延长。这印证了仓库里那句「命中率高但前缀不稳说明锅在路由」——
**当前的锅是前缀分叉，不是 TTL**（append 88.2% vs break 35.3%）。

若将来仍要精确测 TTL，正确的做法是：**同一份 body、同一个 key**，
按 30 秒 / 2 分钟 / 6 分钟 / 12 分钟 / 30 分钟的间隔各重发一次，看 cached 何时归零。
`ttl-probe.py` 现版是错的（换 key + 连发），要改。

### 3. 协议切换粒度：按模型还是按供应商？

建议**按模型**（`ILlmModel` 加一项），因为同一个网关下不同模型对 responses 的支持度可能不同
（闭源模型有加密思考，开源模型没有）。

### 4. 票据落不落盘？—— ✅ 已定：落盘

**已落地**（提交 `9dabb647`）：票据随消息落盘，承载在
`TextReasoningContent.ProtectedData`（上游 agent-framework 的做法），
产出方身份放同一条思考内容的 `AdditionalProperties`。

代价是存储线性增长（真实日志 `data` 1426 字符 / 条，探测那次 5522 字符），
但**只影响加密思考模型**——明文思考模型压根不产生票据，存储零增长。

顺带解决了两件事：不只是扛重启，还消掉了「序号映射会随历史裁剪而错位」这个隐患。

### 5. 票据进分词后占多少 prompt？

未测。`data` 1426 字符约 400 token/条，长会话累积可观。
待有新的运行样本后从 `prompt_tokens` 的增量直接量（同一会话前后两发的差值）。
若占比过高，回填要重新权衡。

### 6. 可读摘能不能拿到？—— ✅ 已验：是模型之间有差异

**实测结论（2026-10-30，直连网关）**：

| 模型类型 | `reasoning_details` 条目类型 | 摘要 |
|---|---|---|
| 只给密文的闭源模型 | `['reasoning.encrypted']` | 没有，只给密文 |
| 给摘要的闭源模型 | `['reasoning.summary']` | **有**，一句可读概括 |
| 明文思考模型 | 无（走 `reasoning_content`） | 明文全文 |

**chat 端点自发给摘要，不需要任何参数**——`reasoning.summary`
（`{type, summary, id, format, index}`）是裸请求就有的。
所以「要显式开摘要」那条只适用于 **Responses 侧**（`summary: "auto"` /
`ReasoningSummaryVerbosity`），chat 侧不需要机制。

结论：**摘要是跟着模型走的**，接口已接住（给了就显示、不给就开空卡），
不需要为它加开关。Responses 侧那个参数随第 3 步一起做。

### 7. 整块原样回放 —— ✅ 实测通过，推理链真的续上了

```
第一轮  reasoning_details: 一条 reasoning.encrypted
        reasoning_tokens: 159
第二轮  整块原样塞回 → 200，回复 "392"
        reasoning_tokens: 656   ← 推理链续上了
```

**`reasoning_tokens` 涨了**，说明模型接着上一轮的思路继续想，
不是把票当字符串忽略了。这是票据机制价值的直接证据。

另外两条实测细节：

- **流式与非流式的密文形状一致**：流式也是**整块一帧**（`data` 长度相同），不是分片
  ——「只切不解析」在流式侧成立，一帧就是完整一张票
- **`data` 长度随思考量变**：实测见过 2791 与 5522 两档。存储代价按最坏一档估
- **`id` 每轮都变**（形如 `rs_<响应id>:rs_<条目id>`，实测三轮三个不同值）
  ——证实「id 不跨轮稳定」，回放不能靠 id 定位，得靠消息本身

## 实施步骤

**重排后的顺序**。原计划是「先换协议、后修票据」，现在改成反过来——因为探测已证实
chat 侧同样能拿到票、同样能回放（200），而分叉的根因是生命周期设计，与协议无关。
先在 chat 侧把票据正确接回来（收益立刻兑现、风险小），再决定要不要换协议。

| 步骤 | 内容 | 风险 | 依赖 | 状态 |
|---|---|---|---|---|
| **第 0 步** | chat 侧票据回填（修正版 + 落盘） | 中 | 无 | ✅ 已完成 |
| 第 1 步 | 协议能力声明 | 无 | 无 | 待做 |
| 第 2 步 | 地址与响应形状分流 | 小 | 第 1 步 | 待做 |
| 第 3 步 | Responses 请求体与响应解析 | 高 | 第 2 步 | 待做 |
| 第 4 步 | responses 侧票据回填 | 中 | 第 3 步 |

**第 0 步可以独立交付**：它不依赖 responses，落地即让闭源模型的思考链接回来，
且把缓存分叉从根上修掉。后四步是「多一条路」的增量。

### 第 0 步　chat 侧票据回填（修正版）—— ✅ 已完成

**已由 [ADR 0100](../adr/0100-加密思考票据按消息各挂各的，随消息落盘，收成协议无关的思考模块.md) 定案并落地**
（提交 `7fd30340` 内存版、`9dabb647` 落盘版）。这里只留执行摘要，正文与理由见那份 ADR。

核心改动：把生命周期从「一次即消费」改成「**每条消息挂自己那张、随消息落盘**」。

**四条不变量**（每条都有测试钉住）：

1. **每条助手消息永久挂自己那张票** —— 票长在消息上，结构上就不会被挪走
2. **逐轮原样重发** —— 票据字节从产生那刻起不再变，前缀才是纯追加
3. **换模型时整条丢弃** —— provider + model 双等（网关不校验，这道门禁是唯一的）
4. **跨重启不丢** —— 随消息落盘，重启后读历史即得 ✅（这条已满足）

**落点**（模块边界见 ADR 0100 决定 1）：

```
AI/Reasoning/ReasoningDetailExtractor.cs   只切不解析地取出 reasoning_details
AI/Reasoning/ReasoningTicketCarrier.cs      票据挂在消息上：存取与产出方身份
AI/Reasoning/ReasoningGate.cs               provider + model 双等
AI/Reasoning/ReasoningSummary.cs            摘 reasoning.summary 明文给界面（不回填）
```

协议相关的拼票留在 chat 改写器里；`LlmRequestScope` 上挂字段，外部改动点收到两处。

**验收结果**：

- ✅ 单测：连续三轮，第 1 轮那条消息的票据在第 2、3 轮**逐字节相同**
- ✅ 单测：**跨重启**（消息序列化再读回来）后依然逐字节相同 —— 内存版在这里必挂
- ✅ 单测：换模型后该消息**不带**票据（body 里确实没有）
- ✅ 真实档案跑 `verify-ticket-prefix.py`：新做法分叉数 0（旧做法 2）
- ⏳ 真实档案跑 `cache-audit.py`：需要新的运行样本才能确认 `break` 数为 0
- ⏳ 命中基线：还原后的纯追加平均 88.2%，回填后不应低于此

**待验的两条**：

- **票据进分词占多少 prompt**（约 400 token/条）—— 待落地后从 `prompt_tokens` 增量直接量
- **可读摘能不能拿到** —— OpenAI Responses API 的摘要在 Responses 侧要显式开
  （`summary: "auto"`），之前那轮探测只带了 `include`、没带 `summary`，
  所以「summary 是空数组」不能证明「模型没有摘要」。探测脚本已补第 6、7 步


### 第 1 步　协议能力声明（无风险）

1. `ILlmModel` 加 `public WireProtocol Protocol => WireProtocol.Chat;`（新枚举 `WireProtocol { Chat, Responses }`）
2. `BaseRemoteModelConfig` 加同名可覆写属性，默认 `Chat`
3. `RemoteCommandCodeModelConfig` 暂不动（默认 Chat，等第 3 步做完再开）
4. 测试：`ILlmModel` 默认值 + 各 config 的覆写关系

**验收**：`dotnet build` 通过；Core 测试全绿；行为与还原后**完全一致**（没有任何请求变化）。

### 第 2 步　地址与响应形状分流（小）

1. `ChatCompletionUri` 加 `ResolveResponses(address)`（把 `/v1` 归一到 `/v1/responses`，
   与 `Resolve` 同样的三种形态收敛规则）
2. 加 `IsResponses(uri)`（判 `/responses` 路径）
3. `OpenAICompatibleHttpHandler` 的 `_isChatCompletions` 改成「是不是需要清洗的形状」，
   两种协议各自清洗
4. 测试：三种地址形态归一 + `/v1/responses` 不被当成 chat

**验收**：现有 URI 测试全绿；新增用例覆盖 responses 路径。

### 第 3 步　Responses 请求体与响应解析（主体工作量）

**请求侧**：
- 新增 `AI/Net/Request/ResponsesRequestRewriter.cs`
- `messages` → `input` item 数组：`user` → `{role, content:[{type:"input_text",text}]}`，
  `assistant` → `{role:"assistant", content:[{type:"output_text",text}]}`，
  `tool_call` / `tool_result` 各有对应 item 形状
- 思考字段：responses 用 `reasoning: {summary:[...]}`，与 chat 的 `reasoning_content` 字段名不同
- 工具定义：`tools` 形状与 chat 不同（`{type:"function", name, parameters, strict}`）
- 必须带 `include: ["reasoning.encrypted_content"]`（探测证实不带就没有密文）

**响应侧**：
- 新增 `AI/Net/Response/ResponsesEventParser.cs`
- SSE 事件逐类翻译：`response.output_item.added` / `.done`（reasoning item）、
  `response.output_text.delta`、`response.function_call_arguments.delta`、
  `response.completed`（usage）
- 一次性响应：`output` 数组 → `ChatResponse`

**这一层最像 opencode 的 `open-responses.ts`，可以对着抄结构。**

**验收**：用录制响应做单测（本仓放在 `Core.Tests/AI/Net/`）；流式与非流式各一组。

### 第 4 步　responses 侧票据回填

**复用第 0 步的逻辑**，只是挂载点从「助手消息序号」换成「reasoning item」：

1. 响应解析时收集 `encrypted_content`，连同 item `id` 存进映射
2. 请求改写时把密文挂回对应的 reasoning item
3. 门禁与不变量与第 0 步完全一致（provider + model 双等、永久各挂各的、逐轮原样重发）

**注意**：responses 侧的 reasoning item 会自然带 `id`，这是比 chat 侧更干净的锚点，
但**不要依赖 id 跨轮稳定**（实测每轮都变，opencode 也是分开存的）。

**验收**：同第 0 步的那几条不变量测试，在 responses 侧再跑一遍。

## 风险与边界

| 风险 | 应对 |
|---|---|
| 网关不给 `encrypted_content` | ~~第 0 步探测就拦住~~ → **已排除**（探测证实两条路都给） |
| 网关不校验模型归属（实测） | 客户端门禁必须 provider + model 双等，且要有测试 |
| `previous_response_id` 不可用 | 不依赖它，走 `store:false` + 回填 |
| 改动面大（响应解析基本是重写） | 分步做，第 0 步可独立交付；每步都有测试 |
| 又一次位置漂移 | 第 0 步的四条不变量写成测试（不是第 4 步） |
| 落盘存储增长 | 先只在内存（会话存活期），跑通再评估；密文实测 1426–5522 字符/条 |
| 票据进分词后占 prompt | 待测：`data` 1426 字符约 400 token/条，长会话累积可观 |

## 验收口径

- **缓存**：真实档案上跑 `src/scripts/perf/cache-audit.py`，前缀分类应为「纯追加」，`break` 数为 0
- **命中**：`cached_tokens` 中位数应回到 88% 以上（还原后的基线就是纯追加 ~88%）
- **思考质量**：闭源模型的思考卡能开出内容（而不是只有一张空卡）
- **回归**：`UiharuMind.Core.Tests` 与 `UiharuMind.App.Tests` 全绿

## 实测依据

| 结论 | 依据 |
|---|---|
| 网关两条路都给票据 | 探测：chat 响应有 `reasoning_details`；responses 加 `include` 有 `encrypted_content`；两边回放都 200 |
| 网关不校验模型归属 | 探测：把票发给另一个模型也 200 |
| 票据「用一次即消费」每轮分叉 | 本地真实请求体：三发，票下标 2→4→6 |
| 分叉确实吃缓存 | 跨模型：append 88.2%（221/249）vs break 35.3%（3 样本） |
| 真实票据结构 | 日志：`{type:"reasoning.encrypted", data(1426字符), id:"rs_…:rs_…", format:"…", index:0}` |
| id 逐轮变化 | 日志三发三个不同 id（`rs_响应id:rs_条目id` 两侧都变） |
| opencode 也分开存 id 与密文 | `open-responses.ts:956`、`:1011`——`itemId` 与 `reasoningEncryptedContent` 并列 |
| responses 也需回填密文 | opencode `open-responses.ts:655-665` 逐条 push `encrypted_content` |
| `previous_response_id` 需 store | opencode `providers/xai.ts:48-50` |
| 协议抽象尚未做 | ADR 0099「没做的事」段 |
| TTL 不是瓶颈 | 真日志同亲和值内：2–10 分钟 30 样本平均 86.1%（中位 96.4%）；27 分钟 98.5%；65 分钟 0% |
| 原 TTL 探测无效 | 每组换了 `prompt_cache_key` 值 → 前缀不同；且六次连发无时间跨度 |
| 还原后彻底 | `git diff 3f51fca1^ HEAD -- src/` 为空；票据符号残留 0 处 |
| Core 测试基线 | 3340 条，3293 通过 / 47 跳过，0 失败 |
| 缓存 TTL 未进产品 | Core 层搜不到 TTL；只在 `probe/ttl-probe.py` |
