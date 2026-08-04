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
`SessionListItem`（列表条目）。用户可见说法是**会话**。

⚠️ 不要与 `AutoClickSession`（自动点击的录制会话）混淆——同词不同域。

### Conversation（对话渲染）

会话的**渲染与交互层**：条目流、输入区、附件、流式装配。只在 UI 侧存在。

体现为 `ConversationViewModel`（薄绑定壳）、`ConversationTranscript`（`AIContent` 流 → 条目序列）、
`ConversationItemBase` 及其派生条目、`ConversationAttachment`、`ConversationView`。

Conversation **渲染**一个 Session；Session 不知道 Conversation 存在。

⚠️ Conversation 不指数据。想说数据就说 Session。

### Chat（聊天页）

**只是页面名**，指「聊天工作台」那一页（角色扮演与助手对话），与 Agent 页并列。
体现为 `ChatPage`、`ChatPageData`。用户可见说法是**聊天工作台**。

⚠️ Chat 不指会话、不指渲染层。`ChatSession` 是唯一例外——那是历史命名，保留不动。

---

## 角色域

### Character（角色）

决定系统提示、挂载片段、可用技能的实体。有稳定标识 `CharacterId`，显示名可随意改而不断引用。

体现为 `CharacterData`、`CharacterConfig`、`CharacterManager`、`CharacterPromptBuilder`、
`CharacterPromptRenderer`；内置角色是 `DefaultCharacter` 枚举。

### ECharacterKind（角色种类）

| 值 | 含义 |
|---|---|
| `Roleplay` | 对话角色：无工具、无工作目录。既涵盖角色扮演，也涵盖纯提示词的工具型角色 |
| `Agent` | 工作区 agent：装配文件/shell/技能等工具与权限档 |

差异只在「是否装配工具与工作目录」；是否带扮演脚手架、是否注入用户卡由**挂载列表**决定。

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
**服务所有角色种类**——`Roleplay` 与 `Agent` 都走这里，只是装配的工具集不同。

唯一入口（缝）是 `ICharacterRunner`；工厂是 `CharacterRunnerFactory`；实现是 `HarnessCharacterRunner`。
子域包括工具、MCP、调度器、技能与框架绕坑收容。

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

---

## Agent 一词的三种用法

这个词无法收敛成一义（它同时是产品概念、vendor 用词、设置名），所以明确区分：

| 用法 | 指什么 |
|---|---|
| `ECharacterKind.Agent` | **一种角色**：有工具与工作目录。对应 `Roleplay` |
| `EAgentMode` / `AgentSettingConfig` / `EnableAgentMode` | agent 的**工作模式与设置** |
| `Microsoft.Agents.AI` | **vendor 的框架名**，Execution 层封装它 |

⚠️ 没有第四种用法。执行层不叫 Agent，工厂不叫 `AgentHost`——因为它们服务所有角色，
不只 Agent 那一种。

---

## 记忆域

### Memory（记忆库）

可检索的文档集合，挂到角色或会话上。体现为 `MemoryData`；检索工具是 `memory_search`。

---

## 结构术语

### Feature（功能切片）

一个用户能说出名字的功能，其 View、ViewModel 与专属控件、窗口构成一个切片。

### Shared（真共享）

被**两个以上** Feature 引用的东西。只被一个 Feature 引用的不算 Shared。

> Feature 与 Shared 的目录位置、归属判定规则与命名禁例见 [../AGENTS.md](../AGENTS.md)「结构约定」。
