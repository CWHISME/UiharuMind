# 子代理运行落成一等会话，改走 TurnDriver

两件事合写，因为它们是同一个转向的两面：正因为要落盘成会话，才有条件走 `TurnDriver`；
正因为走了 `TurnDriver`，审批通道才成立。拆开写，两边都得先把对方解释一遍。

决定：

1. 一次 `RunSubAgent` 调用建一个真 `ChatSession`（**子会话**），带 `ParentSessionId`。
2. 子代理不再跑裸 `RunStreamingAsync` 单遍循环，改由 Core 建一个 `TurnDriver` 驱动。
3. 子会话可续跑、可再对话；用户与主 agent 都能往里发消息。
4. 子代理由此获得审批通道，非完全自动档下不再被削成只读
   （`SubAgentAssembly.cs:220` 那条 `canMutate = FullAuto` 的硬裁剪解除）。
5. 新增**外驱**模式：`ConversationViewModel` 能认领一个正在别处被驱动的会话。

## 起因

三个症状——数据未落盘、没法对话、中途终止无法继续——是同一件事的三个面：
一次子代理运行**没有身份**。它只是 `SubAgentTool.RunAsync`（`:103-159`）栈上的局部变量，
`AgentSession` 与 `AgentHandle` 随方法退出即弃，返回一个 string 就蒸发。
落盘、对话、续跑要指着一个东西说话，而那个东西不存在。

## 为什么这比看上去便宜：审批墙的成因不是同步阻塞

`SubAgentTool` 的类注释（`:26-31`）把它论证成一条死结：

> 子代理**没有审批通道**——它在主 agent 的一次工具调用内部无头运行，而现有审批往返靠
> 「结束本轮再带回应重跑」，同步阻塞在工具里做不到。

这描述的是现象，不是成因。`TurnDriver.RunAsync`（`:143-182`）的实际机制是：
流里收集 `ToolApprovalRequestContent` → **原地 `await resolver(...)`** 并 `BeginApprovalWait` →
拿到回应当作 `nextMessages`，**用同一个内存 `AgentSession` 再调一次** `RunStreamingAsync`。
这个「重跑」从头到尾没离开过那次 `await`，它不需要「本轮结束」，它就是在一次异步调用内部转了个圈。

真正的成因是 `SubAgentTool` 用的是**没有回环的裸循环**（`:124-126`），
于是框架产出的审批请求没人接。把子代理换成 `TurnDriver` 驱动，通道自动就有了。

## 为什么是 Session，而不是第三种实体

要的三件事（落盘 / 续跑 / 对话）恰好是 `Session` 的三个既有能力，而且三条设施已经指着这个方向：

1. **定时任务已经是「非用户驱动的普通会话」的先例。**
   `InProcessSchedulerBackend.CreateRunSession`（`:248-266`）建的就是普通 `ChatSession`、
   `SessionManager.Add` 进索引、正常落盘，然后走**同一个** `TurnDriver`，
   只是 `sink=null` 加 `ApprovalResolver = DenyUnauthorizedApprovals`。
   `TurnDriver.cs:20-28` 原话：「界面与无头执行跑的是同一份编排，差异只有两处」。
2. **「能续」的底座已经建好。** `TurnDriver.SettleInterruptedTurn`（`:346-358`）在取消与异常时
   调 `ToolCallCancellation.CloseUnansweredAtTail`，给每个悬空的 `FunctionCallContent`
   补一条 `[cancelled]` 结果，**历史留在自洽状态**；进程被杀另有 `SettleAllForShutdown` 兜底。
   所以「续」等于往子会话再发一条消息，**零新机制**。
3. **历史是逐次服务调用增量 append 的**（`SessionChatHistoryProvider.StoreChatHistoryAsync:123-146`
   → `SaveAppended`），不是轮末一次性写。子代理跑到一半被杀，已产出的部分就在盘上。

造第三种实体等于把存储、索引、渲染各抄一遍，去换一个「它不是会话」的说法。

## 取舍

- **为什么不进左栏。** 左栏是「我要回到哪段对话」的跨会话导航；子会话的入口天然是
  **它被派出去的那个位置**。`SessionListView` 是纯 `ItemsControl`，不支持分组或缩进，
  改成树要动三个文件却换不来更好的入口；而平铺（照抄定时任务的 `⏰`）会被一个主会话
  十几个子会话淹掉。
- **但只靠卡片不够，所以右栏加一个常驻面板。** 跑了几十轮的会话里那张卡片早滚没了。
  `AgentSidePanel.axaml` 是裸 `TabControl`，加一个 `TabItem` 不需要任何管线改动，
  列表数据按 `ParentSessionId` 从 `index.json` 过滤即得，**不需要新存储**。
  **常驻显示、不条件隐藏**：`AgentPageData.cs:29-34` 显式绑 `SelectedSidePanelIndex`，
  正是因为 Todo 会条件隐藏导致 tab 位置飘（`AgentSidePanel.axaml:191`），再加一个会复利。
- **为什么废掉 `ToolCallItem.NestedItems`。** 子会话成真之后同一批内容会有两个住处，
  而会话域第一条写着「历史直接以 `ChatMessage` 持久化，**不引入映射层**」。
  废掉它还顺手解决了两件事：卡片重开时能按 `SubSessionId` 把过程加载回来（内存那份重启就没了），
  以及「这是一次子代理委派」从靠 `NestedItems.Count > 0` **推断**变成**事实**。
- **窗口用 `QuickChatViewWindow`，删掉 `SubAgentActivityWindow`。**
  前者（`:22-25`）已经是「给一个 `ChatSession`，还你一个能看能聊的浮窗」，
  `QuickChatResultWindow.axaml.cs:251-262` 就是现成的同形先例。
  后者的全部身家是 `ItemsSource = item.NestedItems`（`:79`），数据源没了它就没了；
  它的类注释「只读是按构造成立的——窗口里根本没有输入框」正是本 ADR 要推翻的东西。
  **代价**：`ToolCallItem` 与 `ApprovalRequestItem` 两个 `DataTemplate` 现在在
  `ConversationPageShell.axaml:18-25`（当初的理由是「聊天页角色不产工具调用」），
  而 `QuickChatViewWindow` 不经 shell——必须下沉到 `ConversationView.axaml`，
  否则子会话窗口里**子代理的对话主体（工具调用）一片空白**。
- **为什么 Core 建 driver，不是 UI。** `SubAgentTool` 在 Core，`ConversationViewModel` 在 UI，
  且定时任务里跑子代理时无 UI 可依赖。照抄 `InProcessSchedulerBackend`：Core 建 driver，
  `sink` 由现有的 `AgentBuildProfile.ActivitySink`（`HarnessCharacterRunner.cs:101`）包一层，
  `resolver` 由调用方注入——交互场景指向界面卡片，无人值守用 `DenyUnauthorizedApprovals`。
  **副作用要认**：「子代理能不能写文件」在有人与无人两种场景下行为不同。
- **外驱做成通用能力，不是子代理专用补丁。** Core 驱动 + UI 只渲染，意味着子会话那个
  `ConversationViewModel` 自己的 `_driver` 是闲置的，而 `IsGenerating` 取的正是
  `_driver.IsRunning`（`ConversationViewModel.cs:259`）——会误判成空闲：不出停止按钮，
  且用户打字走**正常发送**而非插话（`:640-650`），排进了下一轮却什么都不说。
  **不会写坏历史**：执行者挂在会话本体上（`ChatSession.cs:433` 的惰性单例，
  而 `SessionManager._loaded` 一个 id 一份本体），两个界面壳拿到的是同一个 runner，
  请求在它的闸门上串行——`SessionRunRegistry` 的类注释把这个场景当作设计内的
  （「界面那一轮与定时任务的无头那一轮就会在执行者的闸门上排队」），引用计数正是为它准备的。
  因此**修的是判据不是并发**：`IsGenerating` 兼看 `SessionRunRegistry.IsBusy`（`:68`）
  并订阅 `StateChanged`，`CurrentRunner`（`:1286`）本来就指向对的 runner，无需改动。
  而这个判据问题**本来就在**：重复开同一会话、点进正在跑的定时任务会话，一样撞。
  做成专用补丁等于把这个坑写进术语表。
- **四个有状态 provider 维持全关。** `AgentOptionsFactory.CreateSubAgentBaseOptions`（`:53-71`）
  的 `DisableStatefulProviders` 不动。「能续」由历史提供，provider 一个都不需要；
  放开任何一个都要配界面（子代理的 todo 显示在哪？右栏那个 Todo tab 是父会话的）
  和新噪音源（子代理写的文件记忆会流进该角色**所有**会话）。
- **仍然不许递归。** 本地单 slot 下同步阻塞会层层叠加，三层嵌套就是串行三段；
  且 `AssemblyInvariantsTests.cs` 已钉住这条。没有需求推着就别动。
- **后续报告落成真消息，不走注入队列。** 那次工具调用结束之后子会话才产出的结论，
  无处可回——`TryInjectAsync` 看似对口，但它不落盘（队列挂在内存 `AgentSession` 上）、
  拿不到 `MessageInjector` 时**静默返回 false**（`HarnessCharacterRunner.cs:342`），
  而 `SendCoreAsync:641-650` 对这个 false 毫无兜底。用它送一份来之不易的结论风险不对等。
  改成往父会话历史追加一条带注记的真消息——形状同**旁白**（落盘、供给模型、只是渲染不同），
  且这条通道正是下一期非阻塞缺的那块。
- **报告是手动交回，不是自动。** 用户开那个窗口未必是为了帮主 agent 干活，
  手动那一下就是表态。
- **交回之后不起新轮。** 让主 agent 在用户没要求时自己动起来，
  「这一轮的用户消息是什么」又没法回答了——那是 ADR 0022 决定推迟的那类问题，别从侧门放进来。
- **匿名子代理给内置身份卡（`GeneralSubAgent` / `ExploreSubAgent`，内部角色，两档各一张）。**
  子会话既然能被打开，就必须有名字和头像；沿用派活者的角色会让窗口看起来像在跟主智能体说话。
  这张卡只承担身份：提示词由 `BuildSubAgentInstructions` 现拼，**能力直接取派活者那一份、
  不走交集**——卡上的 `Tools` 一旦参与计算，将来 `AgentToolConfig` 新增一个默认关闭的能力，
  匿名子代理就会悄悄少一样东西而没有任何地方报错。点名的那一种才走
  「自己的 ∩ 派活者的」，那是真的要防「开着 shell 的子智能体给关了 shell 的派活者开后门」。
  两档不共用一张：探索档恒定只读、另配轻量模型，顶同一个名字用户分不清这次委派能不能改东西。
  卡上的 `Template` **必须保持为空**——人格由装配置空，写了是无操作，
  而"改了存了看起来该变了、模型那边一个字没动"是最难查的一类缺陷，因此也钉了测试。
  代价：多两个 `DefaultCharacter` 枚举项与两份内置 json，漏了 csproj 的
  `EmbeddedResource` 就是**启动崩溃**——已由 `DefaultCharacterResourceTests` 钉住。

- **卡片状态收进枚举。** 跑着 / 成功 / 失败 / 已派出待回，四态用 `IsRunning` + `IsSuccess`
  两个 bool 表达不了。理由同 `ETurnBusy`：一个枚举不是几个 bool。

## 对话的实际形态

用户 → 子代理是**实时**的（走子会话自己的 runner 与 `TryInjectAsync`，输入框照搬主会话行为——
它本来就没被禁用过，`ConversationView.axaml:284-294`）。

主 agent → 子代理是**回合制**的：它此刻正阻塞在那次工具调用里，没有任何机会再发起别的调用。
多轮只能靠共用的 `ContinueSubAgent` 工具（跑完之后再派一轮）。
**不要按实时插话去设计界面。**

## 已知代价

- **子会话在盘上只增不减**，直到父会话被删（级联）。没有数量上限、没有自动清理。
  「清理过程」（只删子会话、不动父会话历史）随时可以单独补，不影响本 ADR 的任何结构。
- **用户插话会改变委派的性质**，而主 agent 不知道。因此 `ReportAccumulator.Build` 要补两种措辞
  （用户曾插话、被用户中止），现在只有超时那一种。理由同该类既有取舍：
  不让主 agent 把不该当结论的东西当结论。
- **解除只读裁剪会碰 `AssemblyInvariantsTests.cs`** 里钉着的那条不变量，需同步改测试。
- **`SelectedSidePanelIndex` 更脆了**（多一个 tab）。靠「常驻不隐藏」压住。

## 外驱为什么必须「不挂接」：执行者的闸门

`HarnessCharacterRunner` 用一个 `SemaphoreSlim _gate` 串行化挂接/运行/存档/释放，而
**`RunAsync` 整轮持有它**。于是想在子代理跑着的时候给它开一个 `ConversationViewModel`，
`LoadSessionAsync → AttachAsync` 会一直等到那一轮跑完——表现是窗口一片空白。

四个方法碰闸门：`AttachAsync` / `SaveStateAsync` / `RunAsync` / `DisposeAsync`。
**`TryInjectAsync`、`GetHistory`、`GetModeAsync`、`GetCapabilities` 都不碰**——
这正好是外驱视图需要的全部，所以它跳过挂接就能工作：历史读盘、插话走注入器、
忙碌态取 `SessionRunRegistry`。也不写回工作目录与权限档：那是正在跑的那一轮的配置，
观察者不该改它。

实时性由 **`ChatSession.HistoryAppended`** 提供（`SaveAppended` 是唯一漏斗，加一行）。
粒度是**每次服务调用**而不是逐 token：观察者看到的是"一条消息/一次工具调用完成了"，
没有打字机效果。这是刻意的取舍——想要逐 token 就得再开一条内容流，
而那正是 `ToolActivityContent` 当初做的事，它让同一批内容有了两个住处。

一次性的**标识交接**仍然保留（`SubSessionStartedContent`）：子会话 id 虽然也缀在工具结果里，
但结果要等跑完才有，而"跑着的时候点开看看"正是这件事的重点。一条，不是一条流。

⚠️ 停止按钮在外驱视图上必须真能停，所以有 `TurnDriver.CancelSession(sessionId)`——
界面壳自己的 `Cancel` 对别处那一轮无效，而显示出来却按不动的按钮是骗人的。

## 勘误

初稿说「`SessionRunRegistry` 要扩展成持有 runner」——**不需要**。
`CurrentRunner` 本来就指向会话本体那个共享执行者，`IsBusy` 已经够用。

初稿把外驱的病因写成「两个 `AgentSession` 并发写同一份 `.history.jsonl`」，**那是错的**。
执行者是会话本体的单例、请求在它的闸门上串行，历史不会被写坏。真实症状是**判据错**
（自己的 driver 闲着就以为会话空闲）。连带地，`SessionRunRegistry` **不需要**扩展成
持有 runner——它已有的 `IsBusy` 就够了。上面的取舍条目已按事实改写，此处留痕以免再犯。
