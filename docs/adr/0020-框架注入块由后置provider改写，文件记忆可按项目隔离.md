# 框架注入块由后置 provider 改写，文件记忆可按项目隔离

两件事收在一个 ADR 里，因为它们都出在框架的 `FileMemoryProvider` 上，且改动点相邻。

1. 新增 `InjectedContextRewriter`——一个**排在最后**的 `AIContextProvider`，
   改写框架注入的记忆索引块的措辞，并给 `file_memory_delete` 补上审批。
2. `AgentToolConfig` 上新增 `FileMemoryScope`（`Character` / `Workspace`），
   默认 `Character`，即 ADR 0002 的原始行为，**对已有数据零影响**。

## 为什么（框架事实，1.20.0 读源码 + 实测）

起因是「每轮都附加 Memory Index 会不会影响缓存命中率、会不会影响注意力」。查完源码，
两个怀疑一个成立、一个不成立：

1. **注入在尾部，前缀缓存无损。** `FileMemoryProvider.ProvideAIContextAsync` 走的是
   `aiContext.Messages`，而基类 `AIContextProvider.InvokingCoreAsync` 的合并是
   `inputMessages.Concat(providedMessages)`——追加在本轮用户消息**之后**；
   `AIContext.Instructions` 只挂 `DefaultInstructions` 常量，每轮不变。
   第 N 轮与第 N+1 轮的公共前缀止于最后一条真实消息，有没有这个块都一样。
   索引 token 每轮重算，但它不挤掉也不作废任何已缓存内容。
   （由此，观测到的命中率下降与本机制无关，另案。最可能是分母假象：
   `cached_tokens / prompt_tokens` 里注入块永远进分母不进分子。）
2. **注入消息不落盘，因此注入是索引唯一的到达路径。** 框架默认其实会落——
   `ChatHistoryProvider` 的默认落盘过滤器只排除 `ChatHistory` 来源，
   `AIContextProvider` 来源不在其中（`InMemoryChatHistoryProviderOptions` 的文档还专门提示
   「could provide a different filter, that also excludes messages from e.g. AI context providers」）。
   是我们自己的 `SessionChatHistoryProvider.IsOwnedByUs` 按 `_attribution` 挡掉的。
   净效果：历史里**零份**陈旧索引，但模型也只在被注入的那一轮看得见它。
3. **自言自语的成因是角色误用。** 那条消息是 `ChatRole.User`，措辞只有一句
   `"The following is your memory index — a list of files you have previously written…"`，
   零防御，且它是紧贴回答位置的**最后一条 user 消息**。模型于是把它当成用户刚说的话，
   回一句「已收到记忆索引，稍后整理」。对照我们自己的 `MemoryContextProvider.SnippetHeader`，
   那里有七行防御，正是为同一个坑写的。
4. **措辞改不了，但位置能改。** `FileMemoryProvider` 是 `sealed`，
   `FileMemoryProviderOptions` 只暴露 `Instructions`（系统提示那段），注入句是硬编码。
   可用的缝是顺序：`HarnessAgent.BuildContextProviders` 把 `options.AIContextProviders`
   追加在框架那批**之后**，而 provider 串行执行、每个都收到累积后的 `AIContext`。
5. **`file_memory_*` 一个都没有审批。** `FileMemoryProvider.CreateTools` 用裸
   `AIFunctionFactory.Create`；对照 `FileAccessProvider`，那边每个写工具都包了
   `ApprovalRequiredAIFunction`（注释原话 "By default, all of these tools require approval"）。
   而未包审批的函数会被 `ApprovalNotRequiredFunctionBypassingChatClient` 直接旁路。
   净效果：**连只读/计划档也拦不住模型静默删掉用户的跨会话长期记忆**。

## 本轮修正：索引正文不再每轮注入（ADR 0002/本 ADR 的后续）

上面记录了「每轮都注入全量索引」的取舍，实践后推翻这条：

**原设计的理由（每轮注入）**：曾打算「只在会话首轮注入」，但注入消息不落盘，
首轮注入等于模型瞥一眼、之后那块从上下文消失——效果等同纯工具驱动，而纯工具驱动在本项目
有确定的失败场景：`LLamaSharpChatClient` 完全忽略 `ChatOptions.Tools`，本地模型下
工具调用零支持。

**实测推翻**：本地模型反正调不动任何 `file_memory_*`，灌一份「文件名清单」它也取不到任何
内容——纯成本，零收益。而远程（能用工具）模型，`file_memory_ls` 返回的正是同一份清单，
**而且新鲜**（注入的反而是可能过期的快照，已删除的档仍躺里头）；同一条信息有两个来源，
模型去 read 一个已删档就是这个副本的代价。

**因此记忆索引消息整条不注入**：`RewriteMessages` 把 `FileMemoryProvider` 来源的那条索引消息
直接以 null 过滤（既不清单、也无一行的指针）。理由两条：记忆的存在与用法已在系统提示里常驻
（框架注入的 `## File Based Memory` 段），这份索引只是快捷清单；且注入的永远是可能过期的快照，
而 `file_memory_ls` 返回的就是同一份、还新鲜——同一条信息两个来源，模型去 read 一个已删档
就是这个过时副本的代价。原「正文一个字不能少」(以及更早的"保留但改措辞/一行指针")都被这个
更强的不变量取代：<b>消息整个不出现在上下文里</b>。

## 取舍

- **为什么是后置 provider，而不是 `AgentFileStore` 装饰器。** 装饰器也能装闸门——注入点的条件是
  `if (!string.IsNullOrWhiteSpace(indexContent))`，而正文是 `_fileStore.ReadAsync(indexPath)` 读来的，
  返回 null 就整条不发（`memories.md` 的读只有那一个调用者，另两处是写与 `IsInternalFile`，
  这个缝干净）。但它**改不动措辞**，而措辞正是病根；且它依赖框架的 private const 文件名，
  改名后闸门静默失效，收不到任何信号。后置 provider 依赖的是注册顺序，那在我们自己手里。
- **为什么重写 `InvokingCoreAsync` 而不是 `ProvideAIContextAsync`。** 后者的输入被基类
  `DefaultExternalOnlyFilter` 滤成只剩本轮外部消息，看不到别的 provider 的产出。
  这与 `MemoryContextProvider` 那条「绝不能回传 `context.AIContext`」不冲突：那条约束的是
  `ProvideAIContextAsync`（基类会把返回值再追加一遍，回传就消息翻倍），
  而 `InvokingCoreAsync` 的契约恰恰是返回完整的合并结果。
- **为什么每轮都注入，不加闸门。** 曾打算「只在会话首轮注入」，但注入消息不落盘（上面第 2 条），
  首轮注入等于模型瞥一眼、第二轮那块从上下文里彻底消失——效果等同纯工具驱动，
  而纯工具驱动在本项目有确定的失败场景：`LLamaSharpChatClient` 完全忽略 `ChatOptions.Tools`，
  **本地模型下工具调用零支持**，文件记忆会直接失效。每轮注入的代价是有界且不累积的一小块。
- **为什么保留 `ChatRole.User`。** 换 `System` 直觉上更对，但 llama.cpp 一侧的 chat template
  大多只认开头一条 system，对话中段的第二条轻则被静默丢弃、重则撑坏模板结构——
  那是「远程有记忆、本地没记忆」这类最难自查的分裂行为。与 `MemoryContextProvider`
  选 User 而不选 Tool 是同一类顾虑（纯文本 Tool 消息没有 `tool_call_id`，MEAI 会静默整条丢弃）。
- **只给 `delete` 包审批，`write` / `replace` 放过。** 后两个虽然也是覆盖写，但那是它每轮的
  正常工作（框架的系统提示段就在教它随时记录），包了等于每轮弹卡、整档能力废掉。
  删除是唯一既不可逆、又没有高频正常场景的那个。
- **Todo 与 AgentMode 暂不纳入。** 它们是同一套注入套路（也是每轮一条 user 消息），
  但待办清单每轮变化且模型确实需要每轮看见，与记忆索引不是同一种语义。
  因此改写规则做成**按来源分发**，加一档只是加一个分支。
- **防御措辞提成 `InjectedBlockGuard.Rules` 与知识库片段共用**，只收与内容无关的三条
  （非用户输入、不得提及本块、本块语言不代表回复语言）；「这块是什么」各自描述。
  已知代价：两处共用一个常量，改一处会静默影响另一处，而提示词改动没有编译期保护。

## 文件记忆的范围（ADR 0002 的修正案）

ADR 0002 的取舍段写着「为什么按角色，不按工作区」。这里**不推翻它，而是把它变成默认的一档**。

- **新信息是：文件记忆只有智能体档有。** 扮演档整个 `DisableFileMemory`（ADR 0003），
  `AgentAssemblyPlan.Resolve` 里非 agent 档直接早返回。所以「陪伴角色需要跨项目记得用户」
  这个论点其实不适用——受影响的全是智能体，而智能体的笔记大多是项目相关的。
- **两档是同一棵树的两层**：角色级 `FileMemory/{角色}_{id}/`，
  项目级 `FileMemory/{角色}_{id}/{工作区名}_{哈希}/`。
  **角色在外层**是刻意的：它保住 ADR 0002 的核心性质（角色 A 的笔记进不了 B），
  角色级就是同一棵树的上一层，因此**两档共存不需要任何迁移**；
  而 `FileMemoryLayout.Reconcile` 的改名对账是在父目录下 glob `*_{id}` 顶层目录，
  这个形状让它一行都不用改（反过来嵌套则要改成遍历每个工作区目录）。
- **默认角色级。** 默认改成项目级会让所有现存记忆在用户眼里「又丢一次」——
  文件还在磁盘上但 `WorkingFolder` 多了一段，模型看不见了。
  ADR 0002 第 2 条正是把这个体验点出来当作要避免的事。
- **工作区键是 `目录名 + 短哈希`。** 光用目录名会撞（两个项目都叫 `client`，撞了就是记忆互相污染），
  光用哈希用户在文件管理器里认不出——与 ADR 0002「目录名里带角色名」同一个取舍。
  哈希取 SHA256 而非 `string.GetHashCode`：后者每进程随机化，重启一次就换一个目录，
  表现是「记忆莫名其妙丢了，但文件还在磁盘上」。路径先归一（全路径、去尾分隔符、
  非 Linux 转小写）再算，否则同一个项目会因写法不同分裂成几份记忆。
- **选了项目级却没绑工作区：回落角色级。** 不能落进 `AppPaths.Cache.Scratch`——
  那是可丢弃的缓存树（ADR 0013），落进去就是「记了但会没」，比不记更坏；
  也不能顺手关掉文件记忆，那会让用户看到「开关开着但模型说自己没有记忆工具」。
- **`Intersect` 取隔离更强的一侧。** 子代理不该把笔记写到比父代理更共享的那一层去，
  与「子代理不能比父代理能力更大」同向。

## 已知代价：索引 50 条上限，按文件名字母序截断

`FileMemoryProvider.MaxIndexEntries = 50`，越过之后按**文件名字母序**截断（不是按时间），
靠后的记忆不再进索引，模型也就再想不起去读。配上「一角色一目录、只增不减」，
这是会真实发生的记忆丢失。

**本轮不修，只让它可见**：角色编辑页的文件记忆那一行常驻显示「已记 N 条」，
越过上限时追加一句警告。一直显示条数而不是只在超限时才提示，是因为后者会让人措手不及——
提示出现的那一刻，靠后的记忆**已经**看不见了。

不修的理由：要根治得自己按修改时间重建索引，那等于把框架的 `RebuildMemoryIndexAsync`
fork 进我们的代码，之后每次框架升级都要对账，而坏掉的方式是「模型看到什么」——静默且难查。
`50` 与两个内部文件名（`_description.md`、`memories.md`）是抄来的三个字面量，
抄错只影响提示准确度，不影响任何行为，因此不值得为它引入反射。

**也不提醒模型去清理。** 两个原因：需要清理的证据在溢出部分，而溢出部分恰好是模型看不见的，
它只能拿那 50 条里「看着没用」的下手；而且 `file_memory_delete` 在本轮之前根本没有审批，
那等于在一个不可逆的删除工具上主动给它动手的理由。清理是用户的决定，不是模型的。
目前用户也没有专门的界面入口，只能从设置页的「打开数据目录」进去手动处理——
要不要给文件记忆做查看/删除界面是另一个独立的活。
