# UiharuMind 术语表

这份文件定义本项目的**统一语言**：每个词指什么、体现为哪些类型、不要与什么混淆。
出现分歧时改代码，不改这份文件（除非这里定义错了）。

这里**只有术语**。目录约定、归属规则、构建方式等「怎么干活」的内容在 [../AGENTS.md](../AGENTS.md)。
不写文件路径——路径会随重构腐烂，类型名改了编译器会报。

---

## 会话域

### Session（会话）

一次完整的对话，**数据本体**。持有历史、标题、所属角色、记忆库、工作目录、权限档、token 累计。
历史直接以 `ChatMessage` 持久化——存储、请求与渲染共用同一个模型，**不引入映射层**，
这样才能无损承载工具调用、思考内容与审批请求。

体现为 `ChatSession`（本体）、`ChatSessionMeta`（列表索引，可重建的缓存）、`SessionManager`（管理）、
`SessionListItem`（列表条目）、`SessionListModel`（一页的列表，见下）。用户可见说法是**会话**。

⚠️ 不要与 `AutoClickSession`（自动点击的录制会话）混淆——同词不同域。

### 会话的创建时机：急建与懒建

两页不同，而且**必须不同**——这不是历史遗留：

| | 聊天工作台 | 智能体页 |
|---|---|---|
| 时机 | **急建**：点新建按钮就 `StartNewSession` → 入索引 | **懒建**：只切空态，首轮发送时 `EnsureSessionAsync` 才入索引 |
| 走哪个构造 | `ChatSession(title, character)` | 无参构造 + 手填字段 |
| 因此 | 角色的**开场白当场写进历史**，`Description` 取开场白 | 没有开场白，`Description` 取用户第一句 |
| 新会话进列表时 | **自动选中**（就该切过去看角色打招呼） | **不自动选中** |

⚠️ **不要把聊天页统一成懒建。** 角色扮演「点完新建立刻看到角色打招呼」是核心体验，
而开场白是在带角色的那个构造里写进历史的（`ChatSession.cs`），懒建绕过它。

⚠️ **也不要把智能体页统一成急建。** 懒建下 `OnSessionAdded` 那条通知落在**正在跑的那一轮中间**，
自动选中会触发重载、把正在流的回复拦腰截断。

这两条差异收在 `SessionListModel` 的构造参数 `selectNewSessions` 上，
**刻意不从 `ESessionListScope` 推导**——它取决于创建时机，与「哪一页」无关。

### SessionListModel（一页的会话列表）

条目集合、选中、运行态、条目级事件的转发。聊天页与智能体页共用一份。
`ESessionListScope` 决定归属哪一页，判定经 `CharacterKindRouting`（调用方不传裸谓词，
因此写不出 `Kind == Roleplay` 那种四档之后会漏掉工具人的判据）。

`Sync()` 是**原地对帐**而非清空重填：按标识复用条目。这一条同时管住三件事——
选中不被抹掉（不必手写抑制标志）、顺序跟上索引的倒序（说过话的会话浮到顶部）、
条目接上 `SaveMeta` 换过的那份新 `ChatSessionMeta`。

⚠️ 运行态（在跑 / 等审批）的真相在 `SessionRunRegistry`，条目只是它的投影。
而对话自己那一轮的运行态是 `ConversationViewModel.IsGenerating`——**两者是不同的问题**：
定时任务在跑的会话，registry 说忙，而你打开它时那个 `TurnDriver` 是空闲的。

渲染它的是 `SessionListView`，两页共用一份。两页**有意**保留的唯一差异是
`ShowAvatar`：角色扮演靠头像认角色，工作区页要的是密度。其余差异（描述、悬停删除、
右键菜单项）此前只是拷贝后各自演化，已经合齐。

---

## 页面壳的组成

智能体页是「外壳 + 若干面板控件」，不是一个大 axaml：

| 控件 | 管什么 |
|---|---|
| `AgentPage` | 五列骨架、两个拖拽 Thumb、顶栏、`ConversationView` 宿主 |
| `SessionListView` | 左栏会话列表（与聊天页共用） |
| `AgentWorkspacePanel` | 右栏的工作区卡片：运行态、换角色、工作目录 |
| `AgentSidePanel` | 右栏的 Todo / 定时任务页签 |
| `ToolCallCardView` · `ApprovalCardView` | 会话流里的特型条目（两页都挂） |

右栏要加一块就是加一个控件、挂进那个容器，不必再往页面文件里塞。

⚠️ **右栏不在两页之间共享。** 聊天页右栏是插件面板，智能体页是工作区与任务——
内容本就该不同。共享的只有左栏列表与五列骨架。

### Conversation（对话渲染）

会话的**渲染与交互层**：条目流、输入区、附件、流式装配。只在 UI 侧存在。

体现为 `ConversationViewModel`（薄绑定壳）、`ConversationTranscript`（`AIContent` 流 → 条目序列）、
`ConversationItemBase` 及其派生条目、`ConversationAttachment`、`ConversationView`。

Conversation **渲染**一个 Session；Session 不知道 Conversation 存在。

跑一轮的编排不在这里——那是 `TurnDriver` 的活（见执行域）。`ConversationTranscript` 是它的
渲染落点，即 `ITurnSink` 的界面侧 adapter。

⚠️ Conversation 不指数据。想说数据就说 Session。

### Chat（聊天页）

**只是页面名**，指「聊天工作台」那一页（扮演与工具人两档的会话），与智能体页并列。
体现为 `ChatPage`、`ChatPageData`。用户可见说法是**聊天工作台**。

⚠️ Chat 不指会话、不指渲染层。`ChatSession` 是唯一例外——那是历史命名，保留不动。

---

## 角色域

### Character（角色）

决定系统提示、工具集与可用技能的实体。有稳定标识 `CharacterId`，显示名可随意改而不断引用。

体现为 `CharacterData`、`CharacterConfig`、`CharacterManager`、`CharacterPromptBuilder`、
`CharacterPromptRenderer`；内置角色是 `DefaultCharacter` 枚举。

### ECharacterKind（角色档位）

角色的**唯一身份轴**：一个角色只落一档。界面的分类、筛选、徽章、编辑表单的面孔、
以及它出现在哪一页全部直读本枚举。

| 值 | 用户可见叫法 | 含义 |
|---|---|---|
| `Roleplay` | 角色扮演 | 有人格、开场白、示例对话，可注入用户卡。不开 harness |
| `Tool` | 工具人 | 一段纯系统提示词只干一件事（翻译、识图、解释）。不开 harness |
| `Agent` | 智能体 | 装配工具与工作目录，开 harness（平台指令、技能目录、压缩、审批、权限档） |
| `UserCard` | 用户卡 | 「我是谁」的单例，有专属编辑窗，不进角色库、不能对话 |

⚠️ `Tool` 与 `Agent` 的分岔点是**「要不要 agent 平台那一套」，不是工具数量**——工具全关的
智能体仍是智能体，它照样吃那段 harness 前言与压缩/审批机制。

⚠️ 不要再说「对话角色」——那个词曾同时指扮演与工具人两档，现在这两档是分开的。

### 档位的归路（CharacterKindRouting）

「哪个档走哪条装配、落在哪一页」的**唯一定义处**：`IsAgent()`（走工具/工作目录/harness）、
`IsChat()`（只渲染提示词的扮演与工具人两档）、`CanStartSession()`（用户卡不能对话）。

⚠️ **不要手写 `Kind == Roleplay` / `!= Roleplay` 当「是不是 agent」用。** 两档时代这么写是成立的，
四档之后 `Tool` 与 `UserCard` 会掉进本该只属于智能体的那一边。实机症状：翻译/识图这类工具人角色
被装上文件、shell、技能与整套 harness；它们的会话在聊天页与智能体页都不显示。
判定一律走 `CharacterKindRouting`，并有不变量测试要求每个可开会话的档位**恰好**归一页。

### IsInternal（内部角色）

只表示**可见性**：程序按 `DefaultCharacter` 点名取用的技能角色（识图、翻译、解释），
角色库默认不列，打开「显示内部角色」才可见并可编辑；
不进任何选择器的候选。身份仍由 `Kind` 说。

⚠️ 它取代了旧的 `IsHide` + `IsHideDefault`。那两个旗标曾兼职表达四件不同的事
（可对话角色 / 提示词片段 / 技能内部角色 / 用户卡），于是「谁能被挂载」根本无从判断。

### 系统提示词的顺序

智能体档的整段系统提示由 `CharacterRunnerFactory` 自己按固定顺序拼，**人格在最前**：

| # | 段 | 来源 |
|---|---|---|
| 1 | 角色人格 / 任务（含 `# Work loop`） | 角色 `Template` |
| 2 | 用户卡 | `InjectUserCard` 打开时注入 |
| 3 | 对话模板 | 角色 `DialogTemplate`（扮演档才有） |
| 4 | 工作目录、工具使用纪律 | 按**实际装配的工具集**派生 |
| 5 | 工作区规矩 | 工作目录里的 `AGENTS.md` |
| — | `## Todo Items` / `## Agent Mode` / `## File Based Memory` | **框架 provider 追加，排在以上全部之后** |

⚠️ `HarnessInstructions` 一律为空串。框架对它只做一件事——拼在 `ChatOptions.Instructions`
**之前**（1.16 实测：`harness + "\n\n" + 角色段`，无第二个用途）。人格既然要排最前，这一层就没用了。
不要把纪律段或框架默认指令塞回去：症状是小模型先读一大段英文工具纪律、角色人格被压在后面。

⚠️ 工作区规矩**不是**「整个系统提示的最尾」——provider 那三段在它之后，且 `## Agent Mode` 有 45 行。
代码注释里曾这么写过，那是错的。

⚠️ **工作循环属于角色层**（`AgentToolPrompts.AgentWorkLoop`），写在内置智能体的存档里、
新建智能体时预填、片段库有一份可插回。见 ADR 0004。

### PromptSnippet（提示词片段）

可插入提示词框的一段现成文本，只有名字与正文两个字段，整库一个 json。
**只在编辑期存在**——插进去之后就是角色自己的文本，运行期没有任何机制引用它。
体现为 `PromptSnippet`、`PromptSnippetManager`；内置的扮演脚手架（第一/第三人称）
是首次运行播下的预设数据，可改可删。

⚠️ 它**不是角色**。曾用两个隐藏的内置角色充当脚手架文本来源，那让角色又开始兼职当文本片段。

### 运行期跨角色引用只有一处

`InjectUserCard`。改了用户卡，所有打开此开关的角色下一轮跟着变。

⚠️ **已退役：`MountPrompts`（内联挂载）。** 它曾在装配时按顺序把别的角色的 `Template`
拼进系统提示。退役的理由是**不可见**——编辑页那个框里看不到被挂载的一个字，模型却先收到它。
角色间的提示词组合改到编辑期完成（插入片段），所见即所得。

⚠️ `MountAgents` 是另一件事：智能体的**可委派子智能体名单**，见下。

### AgentToolConfig（智能体的能力配置）

「这个智能体装哪些工具、禁用哪些技能」。长在角色身上（`CharacterData.Tools`），
**运行时只读这一份**——刻意没有全局总闸，两层 AND 会让「角色开了却没挂上」要查两处；
执行安全由权限档兜。字段默认值即"新建智能体的默认能力"。

⚠️ 全局 `AgentSettingConfig` 里**没有**工具开关与技能禁用清单了。它只剩新会话默认值
（权限档/工作目录/plan）、最近工作目录、搜索 API 凭据。设置页的「能力」页也只剩后者。

⚠️ 工具纪律段曾支持在设置页逐段覆盖，已退役（见 ADR 0003）。

### 子智能体名单（MountAgents）

只对智能体档有意义：名单里每一项是一个智能体档角色，`run_subagent` 的 `agent` 参数据此点名。
人格取该角色的 `Template`，能力取**它自己那份与父智能体的交集**；名单为空则退回通用匿名子代理。

不变量：**子智能体不能比派活的那一个能力更大**（`AgentToolConfig.Intersect`，禁用清单取并集）；
子智能体自己不能再有子代理；装配时按档位过滤名单而不信任存档（旧存档里可能躺着工具人）。
权限档仍继承父会话——它是会话级的，没有下沉。

### PromptAction（预设提示词动作）

吃用户输入 → 渲染角色模板 → 跑一次模型 → 流式吐文本。可升级成完整会话。
服务翻译窗、快捷对话窗、快捷结果窗与识图工具。

体现为 `PromptActionBase`、`NormalPromptAction`、`TranslationPromptAction`、
`ImageOcrPromptAction`、`ImageVisionPromptAction`、`ChainOfThoughtPromptAction` 等。

⚠️ **与 Agent 框架毫无关系**。它曾叫 `AgentSkill*`，那个名字双重误导：既不是 Agent 的东西，
也不是下面定义的 Skill。

---

## 执行域

### Execution（执行层）

把角色装配成可运行的东西，并驱动一轮对话。基于 `Microsoft.Agents.AI`（Agent Framework）实现。
**服务所有角色档位**——扮演、工具人、智能体都走这里，只是装配的工具集与开不开 harness 不同。

唯一入口（缝）是 `ICharacterRunner`；工厂是 `CharacterRunnerFactory`；实现是 `HarnessCharacterRunner`。
子域包括工具、MCP、调度器、技能与框架绕坑收容。

### TurnDriver（轮次驱动）

「驱动一轮对话」这件事本身：发送 → 流式装配 → 审批回环 → 取消收尾 → 交接文档 → 存档。
它把 `ICharacterRunner`（装配好的可运行体）跑起来，并负责这一轮**历史的自洽性**——
补孤儿工具调用的取消结果、保住半截回复、到水位写交接文档。

体现为 `TurnDriver`。一个实例服务一个调用方、跨轮存活（token 账本是跨轮累计的），
会话与执行者逐轮传入。

有**两个** adapter，缝因此是真的：界面的 `ConversationViewModel` 与无头的
`InProcessSchedulerBackend`（定时任务）。两者的差异只有三处，全在 interface 上：

| 差异点 | 界面 | 无头 |
|---|---|---|
| 渲染落点 `ITurnSink` | `ConversationTranscript` | `null`（没有要渲染的东西） |
| `ApprovalResolver` | 等用户点选 | 一律拒绝，追加轮次设上限 |
| `Action<TurnNotice>` | 映射成条目与忙碌态 | 只用来判定任务成败 |

⚠️ **`TurnNotice` 刻意不带文案。** 本地化属界面层——Core 只说发生了什么。
把措辞塞回 Core 的症状是执行层开始 `using` 本地化管理器。

⚠️ 审批**策略**不是它的参数。命中权限档或预授权的调用由装配层的自动放行规则处理，
根本不会冒成 `ToolApprovalRequestContent`；走到 resolver 的都是规则之外的那些。
见 `ApprovalModeMapper`。

⚠️ 不要叫它 `ConversationTurn` 之类——`Conversation` 是 UI 侧的词，见上。
也别叫 `TurnRunner`：`Runner` 一系已经指「装配好的可运行体」，两者混起来就分不清谁跑谁。

### Skill（技能）

遵循 [agentskills.io](https://agentskills.io) `SKILL.md` 规范的技能包，喂给 HarnessAgent。
体现为 `SkillCatalog`、`SkillCatalogEntry`、`SkillInvocation`。

⚠️ 这是本仓 `Skill` 一词的**唯一**含义。预设提示词动作叫 PromptAction，见上。

技能有两条**触发链路**，见下。

### 点名调用（Named Invocation）

用户显式发起的技能触发链路：在输入框敲 `/技能名`，技能正文直接进本轮对话并常驻历史。
不经框架的技能工具，因而**对所有启用的技能一律开放**——包括那些关掉了模型自选的。
只在 `ECharacterKind.Agent` 会话里可用。

### 模型自选（Model Invocation）

模型读技能的 `description` 自行决定要不要加载的触发链路，由框架注入系统提示的技能广告列表驱动。
技能可在自己的 `SKILL.md` 里声明关掉这条链路，那样它就只剩点名调用可达。

⚠️ **代码与文档里不要说「主动技能 / 被动技能」。** 这两个是**链路**，不是技能的两个类别——
关掉了模型自选的技能仍然能被点名调用，这正是整套设计的支点。成对使用「主动/被动」会让人
以为技能分成两拨、各只有一种触发方式，而且这个词至少有三种读法（谁主动：用户？模型？技能自己？）。
_Avoid_: 被动技能、自动技能、手动技能

**界面用词是例外：** 退出了模型自选的技能，UI 上标为**主动技能**（取游戏里「主动技能要自己放」
那个意思，比「仅点名」直观）。它只作为单个技能上的一个徽章出现，**从不与「被动技能」成对**，
因此不会带来上面那种误解。代码里对应 `SkillDisplayItem.IsUserInvokedOnly`。

### Runtime（推理运行时）

跑模型的后端。与 Execution 无关——Execution 装配「要问什么」，Runtime 负责「怎么问到模型」。
包括本地 llama.cpp 进程、LLamaSharp、OpenAI 兼容 HTTP 等后端，以及 `ChatThread`。

### 上下文预算（Context Budget）

三个层层收缩的量，**不要混用**：

| 词 | 是什么 | 从哪来 |
|---|---|---|
| **上下文上限**（context length） | 模型一次能吃下的 token 总量 | 远程：用户填的 → `ModelIdVariants` 预设表 → 200k 兜底；本地：运行期实际加载的 `ContextSize` → 8192 兜底。都收在 `ModelContextResolver` |
| **输入预算**（input budget） | 上限减去给回复留的余量 | `HistoryCompaction.InputBudgetFor`，余量是 `Clamp(上限/8, 512, 8192)` |
| **占用**（usage） | 这一刻实际吃进去了多少 | 服务端 usage 里最近一次响应的输入 token，即 `TurnUsageLedger.LastInput` |

界面上进度条的**整条是上下文上限**（用户能跟官方文档对上的那个数），
而压缩的两条水位是按**输入预算**算的——所以 tooltip 给的是绝对 token 数而不是百分比，
50%/90% 并不落在进度条的对应位置。

⚠️ **「占用」不是 `TurnUsageLedger.TurnInput`。** 后者是本轮所有调用的累加（成本视角），
一轮 agent 十几次工具往返能累到四十几万，与「现在多满」无关。

压缩本身见 [ADR 0006](adr/0006-历史裁剪从自建改为框架在环压缩.md)。

### 交接文档（Handoff）

上下文占用到 0.8 时，让**当前模型**把这段对话写成一份「做完了什么 / 正在做什么 / 下一步」的
文档，落进历史，之后喂给模型的历史就**从这份文档开始**。它之前的消息只留在会话文件与界面上。

三个词不要混：

| 词 | 谁在动 | 结果 |
|---|---|---|
| **交接文档** | 当前模型（多一次请求） | 前情被改写成一份文档，模型知道自己失忆了 |
| **折叠**（tool eviction） | 纯本地字符串操作 | 老的工具调用组合并成一条，用户消息一条不动 |
| **截断**（truncation） | 纯本地 | 直接丢弃最老的消息组，不留任何交代 |

⚠️ **只有交接文档会调用模型。** 折叠与截断都是确定性的本地操作，零延迟零成本。

用户可用 `/compact` 手动触发交接——任务的自然边界由人判断，那里压缩的质量最高。

---

## Agent 一词的三种用法

这个词无法收敛成一义（它同时是产品概念、vendor 用词、设置名），所以明确区分：

| 用法 | 指什么 |
|---|---|
| `ECharacterKind.Agent` | **一种角色档位**：有工具与工作目录，用户可见叫法是「智能体」 |
| `EAgentMode` / `AgentSettingConfig` / `EnableAgentMode` | agent 的**工作模式与设置** |
| `Microsoft.Agents.AI` | **vendor 的框架名**，Execution 层封装它 |

⚠️ 没有第四种用法。执行层不叫 Agent，工厂不叫 `AgentHost`——因为它们服务所有角色，
不只 Agent 那一种。

---

## 记忆域

本域有**两个互不相干的"记忆"**，混淆二者是这里唯一需要警惕的事：一个是用户给的资料，
一个是 agent 自己写下的东西。

### Knowledge（知识库）

**用户提供**的可检索文档集合，挂到角色或会话上，靠文本嵌入做 RAG 检索。
体现为 `MemoryData`（历史命名）、`MemoryManager`、`MemorySources`；检索工具是 `knowledge_search`，
关闭工具时退化为每轮被动注入（`MemoryContextProvider`）。

用户可见说法目前仍是**记忆库**，这是待改的历史包袱——认定说法是**知识库**（README 与产品简介
已经这么写）。

⚠️ 不要与 FileMemory 混淆。知识库是别人给它看的材料，不是它自己记下的东西。

### FileMemory（文件记忆）

**agent 自己写下**的笔记，一个角色一个目录、该角色的所有会话共享。由框架的
`FileMemoryProvider` 提供 `file_memory_*` 工具供模型主动读写；目录归属由 `FileMemoryLayout` 决定
（框架默认是每个新会话一个目录，那样就只是草稿纸，见 ADR 0002）。

用户可见说法是**文件记忆**。只有智能体档有，扮演与工具人一律不挂。

⚠️ 不要叫它 "AgentMemory"——去掉 `File` 这个限定词后，它和知识库在中文里几乎同名。

---

## 结构术语

### Feature（功能切片）

一个用户能说出名字的功能，其 View、ViewModel 与专属控件、窗口构成一个切片。

### Shared（真共享）

被**两个以上** Feature 引用的东西。只被一个 Feature 引用的不算 Shared。

> Feature 与 Shared 的目录位置、归属判定规则与命名禁例见 [../AGENTS.md](../AGENTS.md)「结构约定」。
