# UiharuMind 术语表

每个词**指什么、体现为哪些类型、不要与什么混淆**。只放词——**理由在 [`adr/`](adr/)，机制与踩坑在
对应类型的注释里**，那两处跟着代码走，抄到这里只会烂。出现分歧时可讨论。新增术语表时，尽量保持简洁。

---

## 会话域

### Session（会话）

一次完整的对话，**数据本体**：历史、标题、所属角色、知识库、工作目录、权限档、token 累计。
历史直接以 `ChatMessage` 持久化，不引入映射层。用户可见说法是**会话**。
体现为 `ChatSession`、`ChatSessionMeta`（列表索引，可重建）、`SessionManager`、
`SessionListItem`、`SessionListModel`。

⚠️ 不要与 `AutoClickSession`（自动点击的录制会话）混淆。

### 急建与懒建

会话的**创建时机**：聊天工作台**急建**（点新建就入索引、有开场白、新条目自动选中），
智能体页**懒建**（首轮发送时才入索引）。

⚠️ 两页刻意不同、不可统一，见 [ADR 0011](adr/0011-两个对话页的会话创建时机刻意不同.md)。
懒建意味着**首轮发送前那个会话不存在**。

### 旁白（Narration）

角色的台词，但不是「角色在跟你说话」。目前只有**开场白**是。
标记为 `ChatMessageAnnotations.Narration`，渲染成居中、无头像无名字的淡色气泡。

⚠️ 它是一条**货真价实的 assistant 消息**：落盘，也供给模型（否则首轮又自我介绍一遍）。
旁白只是呈现轴上的事，不改它在历史里的身份。见
[ADR 0016](adr/0016-开场白是旁白，仍入历史.md)。

⚠️ 判据是显式标记，不是「历史里的第一条」——位置会变（删条目、分叉、裁剪），
而「这句是开场白」写下那一刻就定死了。

---

## 页面壳的组成

对话页是「外壳 + 若干面板控件」，不是一个大 axaml。要加一块就是加一个控件挂进容器。

⚠️ **右栏不在两页之间共享**（智能体页是工作区与任务，聊天页是插件面板），
共享的只有左栏会话列表与五列骨架。

### Conversation（对话渲染）

会话的**渲染与交互层**：条目流、输入区、附件、流式装配。只在 UI 侧存在。

⚠️ 不指数据。想说数据就说 Session。

### Chat（聊天页）

**只是页面名**，指「聊天工作台」那一页。体现为 `ChatPage`、`ChatPageData`。

⚠️ 不指会话、不指渲染层。`ChatSession` 是历史命名的唯一例外。

---

## 角色域

### Character（角色）

决定系统提示、工具集与可用技能的实体。稳定标识是 `CharacterId`，显示名可随意改。
体现为 `CharacterData`、`CharacterConfig`、`CharacterManager`、`CharacterPromptBuilder`、
`CharacterPromptRenderer`；内置角色是 `DefaultCharacter` 枚举。

### ECharacterKind（角色档位）

角色的**唯一身份轴**，一个角色只落一档。分类、筛选、徽章、编辑表单、落在哪一页全部直读它。

| 值 | 用户可见叫法 | 一句话 |
|---|---|---|
| `Roleplay` | 角色扮演 | 有人格与开场白，不开 harness |
| `Tool` | 工具人 | 一段纯提示词只干一件事，不开 harness |
| `Agent` | 智能体 | 装配工具与工作目录，开 harness |
| `UserCard` | 用户卡 | 「我是谁」的单例，不进角色库、不能对话 |

⚠️ `Tool` 与 `Agent` 的分岔点是**要不要 harness，不是工具数量**。

⚠️ **工具、MCP、技能、子智能体名单、文件记忆一律只对 `Agent` 档有意义**，下文不再逐条重复。

⚠️ 不要再说「对话角色」——那个词曾同时指扮演与工具人两档。

### 档位的归路（CharacterKindRouting）

「哪个档走哪条装配、落在哪一页」的**唯一定义处**：`IsAgent()`、`IsChat()`、`CanStartSession()`。

⚠️ 不要手写 `Kind == Roleplay` 当「是不是 agent」用。四档定版见
[ADR 0003](adr/0003-角色档位定版为四档真旗标.md)。

### IsInternal（内部角色）

只表示**可见性**：程序按 `DefaultCharacter` 点名取用的技能角色，角色库默认不列、不进选择器候选。
身份仍由 `Kind` 说。

### 系统提示词的顺序

智能体档的整段系统提示由我们自己拼，**人格在最前**：角色人格 → 用户卡 →
工作目录与工具纪律 → 工作区 `AGENTS.md`，框架 provider 那几段排在以上全部之后。

⚠️ `HarnessInstructions` 一律为空串，不要把纪律段塞回去。见
[ADR 0005](adr/0005-系统提示词的顺序由我们拼，人格在最前.md)。

⚠️ **工作循环属于角色层**（`AgentToolPrompts.AgentWorkLoop`）。见
[ADR 0004](adr/0004-工作循环指令从框架默认搬到角色提示词.md)。

⚠️ **我们自己写的提示词散文一律中文，标题也中文，只留一份**（`AgentToolPrompts`、
`AgentPromptHeadings`、子代理体例）。工具与参数的 `[Description]` **保持英文**。
中文纪律段必须配 `AgentToolPrompts.LanguageNeutrality` 那句护栏，否则模型回复语言会被拽走。见
[ADR 0017](adr/0017-提示词散文改中文，搜索失败改结构化.md)。

⚠️ **路径的默认口径是「相对工作目录」**，只在工作目录段表态一次，别处不要再说。同上 ADR。

⚠️ **搜索失败是结构化的**（`GlobOutcome` / `GrepOutcome` 的 `Failure`），不许再把错误
塞回结果列表当假条目。「搜到 0 条」与「没搜成」必须分得开。同上 ADR。

⚠️ **挂了工具就得有纪律段**，shell 也不例外（`AgentToolPrompts.BuildShell`）。而纪律段里
用反引号指名的工具，判据一律取**装配结果**而不是配置意图——否则会指向没挂上的工具。见
[ADR 0017](adr/0017-提示词散文改中文，搜索失败改结构化.md)。

⚠️ 「对话模板」这个词已作废——那个字段是提示词的第二个抽屉，已并入 `Template`。见
[ADR 0015](adr/0015-对话模板退役，旧值不迁移.md)。

### CharacterDraft（角色草稿）

角色编辑表单绑的那份**深拷贝**。所有 setter 写的都是它，只有提交才往活实例上盖
（`CharacterData.CopyFrom`，就地覆盖不换实例——会话缓存着角色引用）。
取消就是把它丢掉。

⚠️ 与 `CharacterInfoViewData` 分工明确：**后者是列表项，只读、永远显示已提交态**。
一个类兼两职就意味着「编辑到一半」会漏进列表与正在进行的会话。见
[ADR 0014](adr/0014-角色编辑改为草稿提交，编辑器内联进工作台.md)。

⚠️ 编辑器有**两个宿主**：角色工作台右主区（内联，主入口）与 `CharacterEditWindow`
（对话侧改角色时用，不跳页）。表单视图只有一份，「保存/取消」那条栏归宿主。

### PromptSnippet（提示词片段）

可插入提示词框的一段现成文本，**只在编辑期存在**——插进去之后就是角色自己的文本。
体现为 `PromptSnippet`、`PromptSnippetManager`。

⚠️ 它**不是角色**，也不是 `MountAgents`（那是子智能体名单，见下）。

⚠️ **角色间的提示词组合一律在编辑期完成。** 运行期唯一的跨角色引用是 `InjectUserCard`
（改了用户卡，所有打开此开关的角色下一轮跟着变）；`MountPrompts`（内联挂载）已退役，
不要重新引入——它在编辑页不可见。

### AgentToolConfig（智能体的能力配置）

「这个智能体装哪些工具、禁用哪些技能」。长在角色身上（`CharacterData.Tools`），
**运行时只读这一份**，刻意没有全局总闸。

⚠️ 全局 `AgentSettingConfig` 里**没有**工具开关与技能禁用清单，它只剩新会话默认值、
最近工作目录、搜索凭据。两层 AND 为什么不要见 [ADR 0003](adr/0003-角色档位定版为四档真旗标.md)。

### MCP 的四个词

| 词 | 指什么 |
|---|---|
| **托管**（连接层） | 要不要为这个 server 建立连接——`McpServerConfig.IsEnabled`，设置页一份 |
| **可用**（能力层） | 这个智能体能不能用它的工具——`AgentToolConfig.DisabledMcpServers`，按角色 |
| **预连**（Warmup） | 装配前等托管中的 server 连上，否则第一轮模型没有那些工具 |
| **server 自述** | `initialize` 响应里 server 自带的用法说明，可单独关掉而仍保留其工具 |

⚠️ **能力真相只有「可用」**，「托管」是资源开销、不是能力闸门。分层、黑名单、撞名才改名、
`InjectInstructions` 归 server 不归角色——全部见 [ADR 0008](adr/0008-MCP托管与可用分层.md)。

⚠️ 「server 正在连」是**进程级**事实，「我这一轮在等它」是**会话级**的。要显示给用户的是后者。

### 能力占用：本会话实际 vs 预估

同一个角色两处显示的 token **本来就该不一样**：右栏说的是本会话**实际挂上**的，
角色编辑页说的是**预估**（运行期条件它不知道）。

⚠️ **`0` 与 `—` 不是一回事**：前者是「确实不占」，后者是「算不出来」。预估拿不到时显示 `—`。

### 固定开销（每轮重发的那一坨）

**系统提示 + 工具定义**，不只是工具。工作区卡片上竖排五档：角色提示 / 工作区 / 工具 / 技能 / MCP。
它同时是压缩与水位判定的输入项，分档口径见
[ADR 0009](adr/0009-占用口径分报告与有效，压缩分母扣掉固定开销.md)。

### 子智能体名单（MountAgents）

名单里每一项是一个智能体档角色，`RunSubAgent` 的 `agent` 参数据此点名。
名单为空则退回通用匿名子代理。

⚠️ 不变量：**子智能体不能比派活的那一个能力更大**，且它自己不能再有子代理。
权限档仍继承父会话。

### PromptAction（预设提示词动作）

吃用户输入 → 渲染角色模板 → 跑一次模型 → 流式吐文本。服务翻译窗、快捷对话窗与识图工具。
体现为 `PromptActionBase` 及其派生。

⚠️ **与 Agent 框架毫无关系**，也不是下面定义的 Skill。它曾叫 `AgentSkill*`，那个名字双重误导。

---

## 执行域

### Execution（执行层）

把角色装配成可运行的东西，并驱动一轮对话。基于 `Microsoft.Agents.AI`（Agent Framework）。
**服务所有角色档位**，只是装配的工具集与开不开 harness 不同。
唯一入口是 `ICharacterRunner`，工厂是 `CharacterRunnerFactory`，实现是 `HarnessCharacterRunner`。

### TurnDriver（轮次驱动）

「驱动一轮对话」这件事本身：发送 → 流式装配 → 审批回环 → 取消收尾 → 交接文档 → 存档。
它负责这一轮**历史的自洽性**。

⚠️ **`TurnNotice` 刻意不带文案**——本地化属界面层。

⚠️ 不要叫它 `ConversationTurn`（`Conversation` 是 UI 侧的词），也别叫 `TurnRunner`
（`Runner` 已经指「装配好的可运行体」）。

### 忙碌原因（ETurnBusy）

「这一轮卡在什么具名的事情上」，要说的只有一件事：**没卡死，在忙别的**。
两档：`ConnectingMcp` 与 `Compacting`，合并成 `ConversationViewModel.BusyLabel` 一处显示。

⚠️ 一个枚举不是几个 bool；作用域是**会话级**；Core 侧不带文案。理由见 `ETurnBusy` 的注释。

### 权限档（Permission Mode）

「这个会话里，哪些工具调用不用问用户」。会话级三档：`ReadOnly` / `AutoEdit` / `FullAuto`。
唯一定义处是 `ApprovalModeMapper.BuildRules`。

⚠️ **越界写入（工作区外的写入）是贯穿三档的硬规则，完全自动档也要问一次。**
放行矩阵见 [ADR 0010](adr/0010-权限档定版三档，越界写入贯穿三档.md)。

### 受管 Python 环境（Managed Python Environment）

一个由我们创建、agent 自己往里装包的 Python 虚拟环境，落在 `AppPaths.External.PythonEnv`。
**解释器归用户**（探测 PATH，或在设置页手填），**环境归我们**。
体现为 `PythonEnvironment`、`PythonEnvSettingsViewData`。

⚠️ **它不是一个工具，也不是能力开关。** agent 拿 `Shell` 去调那个解释器——本仓刻意不为跑代码
另立执行面，见 [ADR 0019](adr/0019-代码执行不设独立工具，走shell与受管venv.md)。
所以它没有 `AgentToolConfig` 开关，只有「建了没有」（`PythonEnvironment.IsReady`，进装配快照）。

⚠️ 它经 `PATH` 前置**激活**进 agent 的 shell（`PythonEnvironment.BuildActivationEnvironment`），
所以模型写的是裸 `python` / `pip`。代价是那个 shell 里**系统 Python 被遮蔽**。

⚠️ 因此**不要说「代码执行工具」「代码解释器」「Python 沙箱」**。前两个指向一个不存在的工具；
第三个是谎——它爆炸半径与 shell 同级，只是不联网。

### agent 产出（Agent Outputs）

agent **想让用户看到**的文件（跑 Python 画的图、导出的数据），落在
`AppPaths.Data.AgentOutputs`。模型在回复正文里用 `![说明](file:///…)` 引用，
markdown 渲染器据此把图显示出来，随历史持久化。

⚠️ 不要与**对话附件**（`AppPaths.Data.AgentAttachments`）混淆：那边是**用户**发进来的。

⚠️ 归 `Data` 而非 `Cache`：对话正文以链接引用它们，清掉就等于历史里留下一堆坏图。

⚠️ 引用格式是 `[![说明](file:///…)](file:///…)`，**外层那道链接不是多余的**——裸图片在
markdown 渲染器里没有 `HRef`，显示得出来但点不开。点击由 `SimpleMarkdownViewer.OnLinkClick`
接住：本地图片走贴图窗口，其余交系统。

### Skill（技能）

遵循 [agentskills.io](https://agentskills.io) `SKILL.md` 规范的技能包。
体现为 `SkillCatalog`、`SkillCatalogEntry`、`SkillInvocation`。有两条**触发链路**，见下。

⚠️ 这是本仓 `Skill` 一词的**唯一**含义。预设提示词动作叫 PromptAction。

### 点名调用（Named Invocation）

用户敲 `/技能名` 发起的链路，技能正文直接进本轮对话并常驻历史。不经框架的技能工具，
因而**对所有启用的技能一律开放**。见 [ADR 0001](adr/0001-点名调用绕开框架技能管道.md)。

### 模型自选（Model Invocation）

模型读技能的 `description` 自行决定是否加载的链路，由系统提示里的技能广告列表驱动。
技能可在 `SKILL.md` 里关掉这条链路，那样它就只剩点名调用可达。

⚠️ **不要说「主动技能 / 被动技能」。** 这两个是**链路**不是技能的类别——关掉模型自选的技能
仍能被点名调用，这是整套设计的支点。_Avoid_: 被动技能、自动技能、手动技能

**界面用词是例外**：退出模型自选的技能，UI 上标为**主动技能**（`SkillDisplayItem.IsUserInvokedOnly`）。
它只作为单个徽章出现，**从不与「被动技能」成对**。

### Runtime（推理运行时）

跑模型的后端。Execution 装配「要问什么」，Runtime 负责「怎么问到模型」。
包括本地 llama.cpp 进程、LLamaSharp、OpenAI 兼容 HTTP 等，以及 `ChatThread`。

### 上下文预算（Context Budget）

分母三个层层收缩，分子两个，**都不要混用**：

| 词 | 是什么 |
|---|---|
| **上下文上限** | 模型一次能吃下的 token 总量（`ModelContextResolver`） |
| **输入预算** | 上限减去给回复留的余量（`HistoryCompaction`） |
| **固定开销** | 系统提示 + 工具定义，每轮完整重发的那一坨 |
| **历史额度** | 输入预算 − 固定开销，历史消息真正能占的那部分 |
| **报告占用** | 服务端 usage 报的输入 token（`TurnUsageLedger`） |
| **有效占用** | `max(报告占用, 固定开销 + 历史估算)` |

⚠️ **「还剩多少空间」用有效占用，「花了多少钱」才用报告占用**——服务端报的数不一定是全量。

⚠️ **「占用」不是 `TurnUsageLedger.TurnInput`**，后者是本轮所有调用的累加（成本视角）。

三条水位谁比谁见 [ADR 0009](adr/0009-占用口径分报告与有效，压缩分母扣掉固定开销.md)；
压缩机制见 [ADR 0006](adr/0006-历史裁剪从自建改为框架在环压缩.md)。

### 交接文档（Handoff）

到水位时让**当前模型**把对话写成一份「做完了什么 / 正在做什么 / 下一步」的文档落进历史，
之后喂给模型的历史就从它开始。用户可用 `/compact` 手动触发。

三个词不要混：

| 词 | 谁在动 | 结果 |
|---|---|---|
| **交接文档** | 当前模型（多一次请求） | 前情被改写成一份文档 |
| **折叠**（tool eviction） | 纯本地 | 老的工具调用组合并成一条，用户消息不动 |
| **截断**（truncation） | 纯本地 | 直接丢弃最老的消息组，不留交代 |

⚠️ **只有交接文档会调用模型。**

---

## Agent 一词的三种用法

这个词无法收敛成一义（同时是产品概念、vendor 用词、设置名），所以明确区分：

| 用法 | 指什么 |
|---|---|
| `ECharacterKind.Agent` | 一种**角色档位**，用户可见叫法是「智能体」 |
| `EAgentMode` / `AgentSettingConfig` / `EnableAgentMode` | agent 的**工作模式与设置** |
| `Microsoft.Agents.AI` | **vendor 的框架名** |

⚠️ 没有第四种用法。执行层不叫 Agent，工厂不叫 `AgentHost`——它们服务所有角色。

---

## 记忆域

本域有**两个互不相干的「记忆」**：一个是用户给的资料，一个是 agent 自己写下的东西。

### Knowledge（知识库）

**用户提供**的可检索文档集合，挂到角色或会话上，靠嵌入做 RAG 检索。
体现为 `MemoryData`（历史命名）、`MemoryManager`、`MemorySources`；工具是 `KnowledgeSearch`。
用户可见说法目前仍是**记忆库**，认定说法是**知识库**。

⚠️ 不要与 FileMemory 混淆。知识库是别人给它看的材料。

### FileMemory（文件记忆）

**agent 自己写下**的笔记，一个角色一个目录、该角色所有会话共享。
体现为框架的 `FileMemoryProvider` 与 `FileMemoryLayout`。
按角色而非按会话分目录见 [ADR 0002](adr/0002-文件记忆按角色分目录.md)。

⚠️ 不要叫它 "AgentMemory"——去掉 `File` 后它和知识库在中文里几乎同名。

---

## 结构术语

### Feature（功能切片）

一个用户能说出名字的功能，其 View、ViewModel 与专属控件、窗口构成一个切片。

### Shared（真共享）

被**两个以上** Feature 引用的东西。但引用数只是初筛，真正的判据是**它是否绑定了某个领域概念**：
绑定领域类型或语义的归该 Feature（哪怕名字通用），语义纯通用的留在 Shared（哪怕只有一个 Feature 用）。

同理，Feature 专属的窗口打开器归各自 Feature，`UIManager` 只留通用机制。

### 界面类的后缀（ViewData / PageData / ViewModel）

界面侧的类按**是否绑定给界面**分两类，后缀由此决定：

| 后缀 | 是什么 |
|---|---|
| `*ViewModel` | 一个 Feature 的主绑定壳，**一个 Feature 只有一个** |
| `*PageData` | 页级的绑定数据 |
| `*ViewData` | 面板或子视图的界面侧子模型，由主壳持有 |
| 无后缀 | 不绑定给界面、只做编排或计算的，按职责命名（`…Runner`、`…Loader`） |

`*ViewData` **不反向持有主壳**，上下文在构造时窄依赖注入：≤2 个用 `Func<>`，≥3 个提成窄接口。

⚠️ 仓内另有三个 `*Model`（`SessionListModel`、`ChatInfoModel`、`ScheduledTaskListModel`），
语义上就是 `*ViewData`。历史遗留，**不改，也不再新增**。
