# OpenAI 兼容 HTTP 层分配实测（2026-10-05）

接着 [10-02 那份](2026-10-02-真实档案五段长跑实测.md)的问题一：MEAI / SDK 逐块序列化 `choices` 那段不在我们手里（升级无效），
这次只看我们自己这一层——响应侧 `OpenAICompatibleHttpHandler` → `SseSanitizingStream` → `OpenAiCompatibleResponseFixer`，
请求侧 `OpenAICompatibleRequestPolicy` / `OpenAICompatibleRequestRewriter` / `LlmBodyLogFormat`，以及它们触发的日志落盘。

进程外基准（Release）：块形态照真实网关帧（`finish_reason:""`、`request_id`、`reasoning` / `reasoning_content`），
一次调用 = 1500 思考块 + 300 正文块 + 200 工具参数块；请求体是合成的长跑量级（37.5 万字符 / 458KB，中文约 4.4 万字）。

## 结论速览

| # | 发现 | 影响 | 处理 |
|---|---|---|---|
| 1 | SSE 清洗逐行解码成字符串再编码回去：读行一份、截前缀与 Trim 两份、拼 `data: ` 与换行两份、GetBytes 一份 | 每块 3–5KB，一次调用约 8MB | ✅ 改为字节流水线，每次调用约 80KB（两块缓冲 + 收尾日志） |
| 2 | 带 `tool_calls` 的块绕开字面快路径，逐块 `JsonNode.Parse` + `ToJsonString` | 工具参数流每块 7.2KB、3.5µs，中文参数被转成 `\uXXXX` | ✅ 修正器改为一遍 `Utf8JsonReader` + 字节拼接，字面 / 计数 / DOM 三层合成一套 |
| 3 | 策略换掉请求体后没释放 SDK 原来那份 | 流式调用里 SDK 自己那次 Dispose 发生在序列化之前，策略 `WriteTo` 时现租的 16KB 池化段（458KB 正文约 29 段）永远还不回去 | ✅ `body.Dispose()`，每次约 −0.8MB |
| 4 | 远程模型都带 `max_tokens`，`Rewrite` 的快速返回从不成立；`CompactJson` 用默认编码器，中文全转 `\uXXXX` | 发出去的请求体 458KB → 595KB（+29.8%） | ✅ 改用宽松编码器（与 SDK 同口径），455KB |
| 5 | 控制台日志汇对每条日志 `Console.WriteLine(LogItem)` | 请求体每条再拼一份（约 625KB，大对象堆），在请求线程上同步写 stdout（约 1ms） | ✅ 普通日志不再写控制台，Warning 以上照旧 |
| 6 | Bodies 落盘先插值 `$"{Text}\n\n"` 再 `GetBytes` | 每条外置正文两次大对象堆分配 | ✅ 直接编码进池化缓冲：每条 1.29MB → 约 1.5KB |
| 7 | 请求侧其余环节仍全在大对象堆：`MemoryStream` 逐次翻倍 + `GetString` + DOM 改写 + `ForLog`（DOM + 缩进串 + `StringBuilder`）+ 插值 | 每次调用约 7MB 大对象堆，gen2 回收与碎片的主要来源 | 原型已验证（见下），未做 |
| 8 | `UnescapeJsonStringNewlines` 把转义的 `\\n` 也当换行 | `"C:\\new"` 在日志里显示成 `C:\` + 换行 + `ew`，只影响显示 | 随 7 一起改 |

本轮（1–6）之后：每次调用的托管分配降 17–19%，gen2 次数降三分之一；**已提交与碎片峰值没动**——那来自 7 的大对象堆分配。

## 读数

### 响应侧：SSE 清洗（单次调用，2005 块）

| 块形态 | 修前 | 修后 |
|---|---|---|
| 网关风格 `finish_reason:""` | 8.28MB（4.3KB/块） | 81KB（41B/块） |
| 商汤 `reasoning` 字段 + `finish_reason:""` | 9.19MB（4.8KB/块） | 83KB |
| 标准 `finish_reason:null`（无需修） | 6.10MB（3.2KB/块） | 48KB |

修后每块剩下的几乎都是固定开销：两块 16KB 缓冲与 Dispose 时的两行诊断日志；单块修正本身约 1B、1–1.5µs。

修后的输出与字节版原型逐行逐字节相同；与修前逐行比，除了原先走 DOM 的块（中文被重新转义）之外逐字节相同，其余 JSON 语义相同。

### 端到端：一次流式调用（MEAI → SDK → 我们这层 → 假服务端，DeepSeek 模型）

| | 修前 | 修后（本轮） | 请求侧也字节化（原型） |
|---|---|---|---|
| 小请求 | 48.6MB | 40.5MB | 40.4MB |
| 长跑量级请求 | 59.5MB（大对象堆 9.4MB） | 48.2MB（大对象堆 7.0MB） | 41.5MB（大对象堆 ≈0） |

剩下约 40MB/次是 SDK / MEAI（10-02 问题一），不在这层。

### 连跑 120 次长跑量级调用（带约 100MB 常驻对象模拟会话数据，工作站并发 GC）

| | 修前 | 修后（本轮） | 请求侧也字节化（原型） |
|---|---|---|---|
| gen0 / gen1 / gen2 | 770 / 45–47 / 15 | 629–632 / 31–36 / 10 | 622–626 / 10–21 / 0 |
| 已提交峰值 | 210MB | 216MB | 126MB |
| 碎片峰值 | 87–90MB | 89–93MB | 10–12MB |
| 大对象堆峰值 | 165MB | 184–188MB | 13MB |

各跑两到四轮；gen2 次数每轮都一样，gen1 与碎片随轮浮动，给区间。

### 请求侧各环节（458KB 请求体，每次调用）

| 环节 | 修前 | 修后（本轮） | 字节化原型 |
|---|---|---|---|
| 策略搬运（`MemoryStream` + `GetString` + `FromString`） | 1.61MB | 1.61MB | 0.6KB（池化） |
| `Rewrite`（只加 `max_tokens`） | 1.67MB，发出 595KB | 1.22MB，发出 455KB | 0.46MB，发出 458KB |
| `Rewrite`（DeepSeek：再回填 95 段思考） | 2.19MB | 1.50MB，发出 529KB | 0.61MB，发出 533KB |
| 请求体日志 `ForLog` + 插值 | 3.67MB | 3.53MB | 0.6KB |
| Bodies 落盘（39 万字符） | 1.29MB | 1.5KB | — |
| 控制台汇 | 约 625KB + 约 1ms（请求线程上） | 0 | — |
| 思考回填收集（`LazyChatClient`，每轮思考 240 / 3000 字） | 134KB / 1.22MB | 同左 | — |

表中上百 KB 的分配都落在大对象堆（单份超过 85KB），思考回填收集除外（每段思考各是一份小对象）。

CPU 不是问题：我们这层每次调用合计十几毫秒，对比几秒的网络与推理。`IndexOf` / `SearchValues` / `Utf8JsonReader` 本身已向量化，
手写 `Unsafe` 指针循环量不出收益；杠杆在数据流——全程字节、不建 DOM、不经字符串、缓冲复用、大块不进大对象堆。

## 本轮改了什么

- **`SseSanitizingStream`**：读缓冲上直接找行界（`\n`、`\r\n`、裸 `\r` 都认，跨读的半行与被拆开的 `\r\n` 也认），
  不需要修的行原样拷贝；一次 `Read` 吐出已到手的全部整行，不等后续数据。末行诊断存字节、Dispose 时只解码一次。
  `ReadAsync` 用 `PoolingAsyncValueTaskMethodBuilder`（每块到达都真的异步一次）。
  两块缓冲每个流各自持有、**不从池里租**：并发 Dispose 时池化缓冲可能已被别人租走再被写坏，省下的却只有每次 32KB。
- **`OpenAiCompatibleResponseFixer`**：一遍 `Utf8JsonReader` 记下要改的字节区间，再由 `Utf8JsonSplice` 拼接；
  语义与原 DOM 版一致（`tool_calls` 子树里只认元素自身的 `type`，思考键等对象收尾再定夺改名还是删除）。
  原来对 DOM 重序列化产物（空白被规整）的那条断言改为逐字比对。字符串入口 `FixJson` / `FixEventStreamLine` 只留给测试。
- **`Utf8JsonSplice`**（新）：「字节区间 → 替换字节」的收集与拼接，前 4 处改动放在栈上的 `InlineArray` 里，不分配。请求侧字节化时复用。
- 非流式 JSON 响应同样按字节修（`ReadOnlyMemoryContent`），不再 `ReadAsStringAsync` → `StringContent`。
- 3–6 各一处小改，见结论速览；`ReplacedBody_IsDisposed`、`Rewriter_KeepsNonAsciiUnescaped` 两条测试在撤掉修复时会变红。

## 下一步（下个会话专门做）

### P2：请求侧字节化（结论速览 7、8）

目标：每次调用约 7MB 的大对象堆分配降到 ≈0。原型的端到端读数：长跑量级请求每次 48.3MB → 41.4MB、大对象堆 7.1MB → ≈0；
连跑 120 次 gen2 10 → 0、已提交峰值 216 → 126MB、碎片峰值约 91 → 11MB。原型都在 `src/scripts/perf/http-alloc/Proto/`，按依赖顺序：

1. **日志加 UTF-8 正文入口**（`Core/SimpleLog`）。请求体日志现在是 `string` 进日志：`ForLog` 格式化出一份（内部还有 DOM 与 `StringBuilder`）、插值拼头行再一份；落盘那份本轮已改为编码进池化缓冲。
   - `Log` / `LogManager` 加一个收「头行 + UTF-8 正文」的入口。日志是异步落盘，入队时把正文拷进池里租的缓冲，写线程写完还池；
     `LogItem` 带上这份字节（与 `Text` 二选一）。
   - `LogStore.Append` 的字节分支：外置正文直接 `Append`（正文与空行仍要一次写完，见 `AppendSpilled` 的注释）、
     预览只解码首行、`ByteLength` 直接用字节长度。头行里的「N chars」要定：改报字节数，还是 `Encoding.UTF8.GetCharCount` 算一遍。
   - `ILogger` 订阅方：App 的控制台汇 Debug 已不打印；Warning / Error 打头行加预览即可。`OnLogAppended` 派发的 `LogIndexEntry` 不变，面板不用动。
   - 测试：`LogStoreTests` 补字节正文的往返（中文按字节算偏移）与写满滚动的边界。
2. **`LlmBodyLogFormat` 字节化**：`Format(ReadOnlySpan<byte> json, IBufferWriter<byte> output)`。原型 `Utf8BodyLogFormat`：
   一遍 `Utf8JsonReader`、自己排版（两格缩进、`": "`、空容器 `{}` / `[]`，与 `Utf8JsonWriter` 缩进口径一致；
   `Utf8JsonWriter.WriteRawValue` 不缩进数字，所以不用它）；字符串按宽松编码器转义、真换行原样留下（顺带修掉 8）；base64 在解码后的字符串上判。
   大请求体输出与 `ForLog` 逐字相同。失败路径 `OpenAICompatibleHttpHandler.ReadBodySnippetAsync` 一并改走字节版；之后 `ForLog(string)` 若没有调用方就删。
3. **`OpenAICompatibleRequestRewriter` 字节化**：`Rewrite(ReadOnlySpan<byte> json, ..., IBufferWriter<byte> output) → bool`，不建 DOM。原型 `Utf8RequestRewriter`：
   - 根对象：保留的属性原样拷；要设的键（额外参数、`tool_choice`）删掉旧的、统一追加在末尾；采样参数直接删。
   - `messages`：只在要修 `"arguments":"null"` 或回填思考时逐条走进去，其余 `Skip`；回填插在消息对象的 `}` 前，已有 `"reasoning_content":null` 则只换值。
     内层改动落地时用 `Utf8JsonSplice`（插入就是起点等于终点）。
   - 插入值用宽松编码器序列化（与本轮的 `CompactJson` 同口径）。
   - 根上的键顺序会变；JSON 语义不变，前缀缓存按渲染后的 token 算，不受影响。现有 `Rewriter_ForbidsToolCalls_FromTheCallContext` 的字面断言仍成立（键本来就在末尾）。
4. **`OpenAICompatibleRequestPolicy`**：
   - `TryComputeLength` 拿长度，SDK 正文 `WriteToAsync` 进池里租的整块，取代 `MemoryStream` 逐次翻倍与 `GetString`。
   - 改写进池化缓冲 → 走 1 的字节入口记日志 → 发出去的正文切成 64KB 段的 `BinaryContent`（原型 `SegmentedContent`）。
     段**不从池里租**：HTTP/2 可能在请求体发完之前就收到响应，那时还池会把别处的数据发出去；64KB 小于 85000 字节，不进大对象堆。
     重试会再发一遍同一份正文，`WriteToAsync` 可重入。
   - 无需改写时（本地模型没有额外参数）不替换，原样用 SDK 那份。
   - 测试缝 `Func<string, string>` 换成字节版；`ChatClient_LogsTheRequestBodyOnce_AcrossRetries` 按头行里的字符数认日志，头行口径变了要跟着改。
5. **验收**：`http-alloc.sh e2e` 与 `gc current` 的读数应接近表里的原型列；`compat` 通过；跑一个冒烟场景，看日志面板里的请求体与 Bodies.txt 照常可读。

### 其余优化（小，可与 P2 同一会话做）

- **思考回填按需收集**：`LazyChatClient` 每次请求都 `CollectReasoningByCallId`，不管模型要不要回填（只有 `RequiresReasoningContentRoundtrip` 的 DeepSeek 系用得上），
  实测每次 134KB（每轮思考 240 字）到 1.22MB（3000 字）。可把 `LlmRequestContext.PendingReasoningByCallId` 换成惰性来源（例如 `Func` 或 `Lazy`），
  由改写器只在需要回填时取；一条消息只有一段思考时直接用它的 `Text`，不过 `StringBuilder`（多段拼接只为老会话的逐块碎片）。
- **改写器的非对象根**：`AsObject()` 对非对象根直接抛 `InvalidOperationException`，与注释「原样返回」不符。SDK 不会发这种正文，属潜在不一致；P2 字节版自然修掉。
- **`OpenAICompatibleHttpHandler` 的两行响应日志**：`OpenAI-compatible response: … content-type` 与 `SanitizeResponse: mediaType=…` 几乎重复，可合成一行；
  `_baseUri.AbsolutePath.Contains("chat/completions")` 每个响应算一次，可在构造时算好。量级很小，顺手。
- **`DefaultLogger`**：全仓没有引用，Debug 仍写控制台。删掉，或与 App 同口径。

## 跑测时踩到的事

- 带 `UIHARU_HOME` 指到临时目录、并在首次取 `LogManager.Instance` 之前 `LogManager.UseDirectory(...)`：
  基准里清洗流收尾会打日志，不然会写进并轮换真实日志目录。
- App 测试全量跑偶发一次 `SettingsWindowKeyboardTests` 在 Dispose 时撞 Avalonia 线程亲和性，单独跑与重跑全量都过，与本轮无关。
- `BinaryContent.Create(BinaryData)` 那种正文的**同步** `WriteTo` 会先 `ToArray()` 整份复制（`WriteToAsync` 不会）。生产走异步管道不受影响，
  但基准里拿它模拟 SDK 正文时要走 `WriteToAsync`，否则策略搬运会多算一份（先前记的 2.06MB 就含了这份，已更正为 1.61MB）。

## 复现

```bash
src/scripts/perf/http-alloc.sh sse            # 响应侧：每次调用 / 每块的分配（回归基线）
src/scripts/perf/http-alloc.sh req            # 请求侧各环节：现状对 P2 原型
src/scripts/perf/http-alloc.sh e2e            # 端到端一次流式调用：现状对 P2 原型
src/scripts/perf/http-alloc.sh gc current     # 连跑 120 次；再跑一次 gc proto 对照
src/scripts/perf/http-alloc.sh compat         # 差分核对：P2 原型与现状逐例比
```

工程在 `src/scripts/perf/http-alloc/`，不在解决方案里；程序集名借用 `UiharuMind.Core.Tests` 拿 `InternalsVisibleTo`。脚本每次把 `UIHARU_HOME` 指到新的临时目录，跑完删掉。

- 假服务端：`HttpMessageHandler` 回预先造好的 `text/event-stream` 字节；客户端照 `OpenAICompatibleChatClient.Create` 组装。
- 分配：`GC.GetTotalAllocatedBytes(precise: true)` 前后差，每项先预热三次；耗时用 `DOTNET_TieredCompilation=0` 固定代码质量。
- 大对象堆：进程内 `EventListener` 订 `Microsoft-Windows-DotNETRuntime` 的 GC 关键字（0x1），累加 `GCAllocationTick` 里 `AllocationKind = 1` 的 `AllocationAmount64`（约每 100KB 采一次，看量级）。
- 碎片与已提交：每次调用后取 `GC.GetGCMemoryInfo()` 的 `FragmentedBytes`、`TotalCommittedBytes` 与第 3 代（大对象堆）大小取峰值。
