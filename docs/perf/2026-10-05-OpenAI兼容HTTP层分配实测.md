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
| 7 | 请求侧其余环节仍全在大对象堆：`MemoryStream` 逐次翻倍 + `GetString` + DOM 改写 + `ForLog`（DOM + 缩进串 + `StringBuilder`）+ 插值 | 每次调用约 7MB 大对象堆，gen2 回收与碎片的主要来源 | ✅ P2：请求侧全程字节，大对象堆 ≈0 |
| 8 | `UnescapeJsonStringNewlines` 把转义的 `\\n` 也当换行 | `"C:\\new"` 在日志里显示成 `C:\` + 换行 + `ew`，只影响显示 | ✅ 随 7 修掉：先解码再判换行 |

1–6 之后：每次调用的托管分配降 17–19%，gen2 次数降三分之一，但已提交与碎片峰值没动——那来自 7 的大对象堆分配。
P2（7、8）落地后：长跑量级请求每次 41.5MB、大对象堆 ≈0；连跑 120 次 gen2 0 次、已提交峰值 128MB、碎片峰值 13MB，与原型持平。

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

| | 修前 | P0 + P1 | P2 原型 | P2 落地 |
|---|---|---|---|---|
| 小请求 | 48.6MB | 40.5MB | 40.4MB | 40.5MB |
| 长跑量级请求 | 59.5MB（大对象堆 9.4MB） | 48.2MB（大对象堆 7.0MB） | 41.5MB（大对象堆 ≈0） | 41.5MB（大对象堆 ≈0） |

剩下约 40MB/次是 SDK / MEAI（10-02 问题一），不在这层。

### 连跑 120 次长跑量级调用（带约 100MB 常驻对象模拟会话数据，工作站并发 GC）

| | 修前 | P0 + P1 | P2 原型 | P2 落地 |
|---|---|---|---|---|
| gen0 / gen1 / gen2 | 770 / 45–47 / 15 | 629–632 / 31–36 / 10 | 622–626 / 10–21 / 0 | 622 / 22–23 / 0 |
| 已提交峰值 | 210MB | 216MB | 126MB | 128–129MB |
| 碎片峰值 | 87–90MB | 89–93MB | 10–12MB | 13MB |
| 大对象堆峰值 | 165MB | 184–188MB | 13MB | 15MB |

各跑两到四轮；gen2 次数每轮都一样，gen1 与碎片随轮浮动，给区间。
落地比原型多的约 2MB 大对象堆是请求体日志的池化缓冲（入队时租、写线程还）留在共享池里；原型直接写文件，没有这份。

### 请求侧各环节（458KB 请求体，每次调用）

| 环节 | 修前 | P0 + P1 | 字节化原型 | P2 落地 |
|---|---|---|---|---|
| 策略搬运（`MemoryStream` + `GetString` + `FromString`） | 1.61MB | 1.61MB | 0.6KB（池化） | 0.46MB（池化 + 64KB 切段正文，全是小对象） |
| `Rewrite`（只加 `max_tokens`） | 1.67MB，发出 595KB | 1.22MB，发出 455KB | 0.46MB，发出 458KB | 1KB（写进池化缓冲），发出 458KB |
| `Rewrite`（DeepSeek：再回填 95 段思考） | 2.19MB | 1.50MB，发出 529KB | 0.61MB，发出 533KB | 86KB（95 段思考各一份小数组），发出 533KB |
| 请求体日志 `ForLog` + 插值 | 3.67MB | 3.53MB | 0.6KB | 格式化 + 入队 27KB（连发时池里偶尔现租一块） |
| Bodies 落盘（39 万字符） | 1.29MB | 1.5KB | — | 1.1KB |
| 控制台汇 | 约 625KB + 约 1ms（请求线程上） | 0 | — | 0 |
| 思考回填收集（`LazyChatClient`，每轮思考 240 / 3000 字） | 134KB / 1.22MB | 同左 | — | 22KB / 22KB；不回填的模型 0 |

修前与 P0 + P1 两列上百 KB 的分配都落在大对象堆（单份超过 85KB），思考回填收集除外（每段思考各是一份小对象）。

CPU 不是问题：我们这层每次调用合计十几毫秒，对比几秒的网络与推理。`IndexOf` / `SearchValues` / `Utf8JsonReader` 本身已向量化，
手写 `Unsafe` 指针循环量不出收益；杠杆在数据流——全程字节、不建 DOM、不经字符串、缓冲复用、大块不进大对象堆。

## P0 + P1 改了什么

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

## P2 改了什么（结论速览 7、8）

照原型落地，落地时用差分核对逐例比过落地与原型一致；之后原型与核对模式都已删掉，基准只量正式代码，语义由单测钉住。

- **日志的 UTF-8 正文入口**：`Log.Debug` / `Log.Warning(string lead, ReadOnlySpan<byte> utf8Body, …)`。
  正文入队前拷进池里租的缓冲，写线程落完盘还池（`LogItem.ReleaseBody`）；条目的 `Text` 只是排在字节正文前的引导文字。
  `LogStore` 两个分支都走 `LogItem.CopyUtf8To`，预览只解码首行用得上的那段。
  头行的「N chars」仍报字符数（`GetCharCount` 算一遍，几十微秒），外置与否也仍按字符数判——与字符串正文同一口径。
  `ILogger` 收到的原始消息是首行预览，`LogItem.ToString()` 对字节正文也只给预览：控制台不该吃下整份请求体。
- **`LlmBodyLogFormat.Format(ReadOnlySpan<byte>, PooledByteWriter)`**：一遍 `Utf8JsonReader` 自己排版，字符串先解码再转义，
  真换行原样留下（`"C:\\new"` 不再被拆成两行）。不是 JSON 就照抄原文。`ForLog(string)` 已删，失败路径的正文也走它。
- **`OpenAICompatibleRequestRewriter.Rewrite(ReadOnlySpan<byte>, in RequestRewrite, IBufferWriter<byte>) → bool`**：
  `OpenAICompatibleRequestRewriter.For(model)` 收集模型配置与请求上下文，改写本身是纯函数（测试直接给选项，不用造模型）。
  内层改动没有用 `Utf8JsonSplice`：根对象要按「保留的 + 追加的」重拼，删相邻的两个属性时 `RemoveProperty` 的逗号会重叠，
  所以根与内层合在一趟 `Build` 里拼。回填思考在消息收尾时才记下，可能排在同一条消息里修参数那处之前，拼之前按位置排序
  （`NullReasoningBeforeToolCalls_WithArgumentFix_BothApply` 钉住）。非对象根、截断的 JSON 返回 false 照原样发。
- **`OpenAICompatibleRequestPolicy`**：SDK 正文经 `PooledByteWriter.AsStream()` 拷进池化缓冲（不依赖 `TryComputeLength` 准不准，
  它只作初始大小），改写进池化缓冲，发出去的是 `SegmentedBinaryContent`（64KB 段，不从池里租：HTTP/2 可能在请求体发完之前就收到响应，那时还池会把别处的数据发出去）。无需改写时不替换。
  请求体日志的引导文字改报字节数：`OpenAI-compatible request (17,605 bytes): `，就是发到线上的大小，不必再数字符。
- **思考回填按需收集**：`LlmRequestContext.PendingReasoningByCallId` 换成 `PendingReasoningSource`（`Func`），
  只有 `RequiresReasoningContentRoundtrip` 的模型才调；一条消息只有一段思考时直接用它，不过 `StringBuilder`。
- 顺手：响应日志两行合一（不清洗的端点在行尾标 `(not sanitized)`），路径判断挪到构造时；删掉无人引用的 `DefaultLogger`。
- 冒烟 `view-image`（Agnes-2.5-Flash-无思考）：`Bodies.txt` 三条请求体两格缩进、真换行、中文原样、图片载荷抹成
  `<170228 base64 chars>`；第二轮请求的消息前缀与上一轮逐条相同，`max_tokens` 追加在根的末尾。

### 审查后的修正

子代理拿旧实现做随机差分（修正器 20 万条、改写器 10 万条、SSE 3000 组随机切块）没有发现语义回归，另找出几处，已修并各补测试：

- 日志格式化只接 `JsonException`：孤立代理项转义（`"\ud83d"`）解码时抛 `InvalidOperationException`，会从失败日志冒进 `SendAsync`，
  盖掉真正的 4xx。现在一并接住、照抄原文；`LogFailureAsync` 再兜一层，日志出错只丢正文。
- base64 只在整个值恰是 data URL 时才抹，比原先的正则窄：嵌在文字里的（markdown 图片）、载荷后还跟着字符的、非 JSON 正文里的都漏了。
  现在在任何位置找 `data:…;base64,` 后连续 512 字节以上的载荷；非 JSON 正文也扫。
- 额外参数里有 `messages` 时，被覆盖掉的那份仍被扫、改动落到别的属性上越界（原型有 `!drop`，落地漏了）。
- SSE 流开头的 UTF-8 BOM 不再被剥（原先的 `StreamReader` 会剥），首帧不修。现在开头跳过；长行跨多次读时从上次扫到处接着找行界。
- 顺手：两个相同的 `Edit` 记录合一；改写器线程静态缓冲超过 64KB 用完即弃，不让几万字的思考常驻在线程池线程上。

## 跑测时踩到的事

- 带 `UIHARU_HOME` 指到临时目录、并在首次取 `LogManager.Instance` 之前 `LogManager.UseDirectory(...)`：
  基准里清洗流收尾会打日志，不然会写进并轮换真实日志目录。
- App 测试全量跑偶发一次 `SettingsWindowKeyboardTests` 在 Dispose 时撞 Avalonia 线程亲和性，单独跑与重跑全量都过，与本轮无关。
- `BinaryContent.Create(BinaryData)` 那种正文的**同步** `WriteTo` 会先 `ToArray()` 整份复制（`WriteToAsync` 不会）。生产走异步管道不受影响，
  但基准里拿它模拟 SDK 正文时要走 `WriteToAsync`，否则策略搬运会多算一份（先前记的 2.06MB 就含了这份，已更正为 1.61MB）。

## 复现

```bash
src/scripts/perf/http-alloc.sh sse            # 响应侧：每次调用 / 每块的分配（回归基线）
src/scripts/perf/http-alloc.sh req            # 请求侧各环节
src/scripts/perf/http-alloc.sh e2e            # 端到端一次流式调用
src/scripts/perf/http-alloc.sh gc             # 连跑 120 次长跑量级调用
```

工程在 `src/scripts/perf/http-alloc/`，不在解决方案里；程序集名借用 `UiharuMind.Core.Tests` 拿 `InternalsVisibleTo`。脚本每次把 `UIHARU_HOME` 指到新的临时目录，跑完删掉。

- 假服务端：`HttpMessageHandler` 回预先造好的 `text/event-stream` 字节；客户端直接用 `OpenAICompatibleChatClient.Create` 建（与产品同一条管道）。
- 分配：`GC.GetTotalAllocatedBytes(precise: true)` 前后差，每项先预热三次；耗时用 `DOTNET_TieredCompilation=0` 固定代码质量。
- 大对象堆：进程内 `EventListener` 订 `Microsoft-Windows-DotNETRuntime` 的 GC 关键字（0x1），累加 `GCAllocationTick` 里 `AllocationKind = 1` 的 `AllocationAmount64`（约每 100KB 采一次，看量级）。
- 碎片与已提交：每次调用后取 `GC.GetGCMemoryInfo()` 的 `FragmentedBytes`、`TotalCommittedBytes` 与第 3 代（大对象堆）大小取峰值。
