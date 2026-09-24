using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Prompts;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.WebTools;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Tools.Scheduler;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死装配的三个不变量之一：<b>非智能体档零注入</b>。
/// 普通角色只渲染提示词：框架的每一项能力都必须关掉、HarnessInstructions 必须为空——
/// 任何一项漏关都会向它们的上下文里悄悄注入内容,
/// 而这种污染在实机上几乎不可见(模型行为变化无法归因)。
/// </summary>
public class PromptOnlyZeroInjectionTests
{
    [Fact]
    public void BuildPromptOnlyOptions_DisablesEveryFrameworkCapability()
    {
        CharacterData character = new() { CharacterId = "rp", IsAgent = false };
        ChatOptions chatOptions = new();

        HarnessAgentOptions options = AgentOptionsFactory.BuildPromptOnlyOptions(
            character, new StubHistoryProvider(), [], chatOptions);

        Assert.Equal(string.Empty, options.HarnessInstructions);
        Assert.True(options.DisableWebSearch);
        Assert.Null(options.FileAccessStore); //1.16:框架文件工具只随 FileAccessStore 出现
        Assert.True(options.DisableFileMemory);
        Assert.True(options.DisableTodoProvider);
        Assert.True(options.DisableAgentModeProvider);
        Assert.True(options.DisableAgentSkillsProvider);
        Assert.True(options.DisableToolAutoApproval);
        Assert.True(options.DisableOpenTelemetry);
        Assert.Null(options.ChatOptions!.Tools); //普通角色不装配任何工具
    }

    /// <summary>
    /// 压缩是「零注入」的唯一例外，且这个例外必须是显式给的：
    /// 它只做排除与工具结果折叠，不往上下文里添加内容，因此不违背零注入；
    /// 但没传策略时必须仍是关的，免得哪天框架给它加了默认行为就悄悄生效（ADR 0006）。
    /// </summary>
    [Fact]
    public void BuildPromptOnlyOptions_CompactionIsOptInOnly()
    {
        CharacterData character = new() { CharacterId = "rp", IsAgent = false };

        HarnessAgentOptions without = AgentOptionsFactory.BuildPromptOnlyOptions(
            character, new StubHistoryProvider(), [], new ChatOptions());
        Assert.True(without.DisableCompaction);
        Assert.Null(without.CompactionStrategy);

        CompactionStrategy strategy = HistoryCompaction.Create(() => 128_000, new TurnInputEstimate());
        HarnessAgentOptions with = AgentOptionsFactory.BuildPromptOnlyOptions(
            character, new StubHistoryProvider(), [], new ChatOptions(), strategy);
        Assert.False(with.DisableCompaction);
        Assert.Same(strategy, with.CompactionStrategy);
    }

    private sealed class StubHistoryProvider : ChatHistoryProvider
    {
        public override IReadOnlyList<string> StateKeys => [];

        protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
            InvokingContext context, CancellationToken cancellationToken = default)
        {
            return new ValueTask<IEnumerable<ChatMessage>>([]);
        }

        protected override ValueTask StoreChatHistoryAsync(
            InvokedContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}

/// <summary>
/// 不变量之二：<b>历史只写我们自己的消息</b>。
/// 框架各 provider 注入的消息(todo 快照、mode 通知、记忆片段)带 _attribution 溯源标记,
/// 一旦写进历史就会逐轮累积并被回灌,历史文件以指数式膨胀。
/// </summary>
public class HistoryAttributionTests
{
    [Fact]
    public void OwnMessages_AreOwnedByUs()
    {
        Assert.True(SessionChatHistoryProvider.IsOwnedByUs(new ChatMessage(ChatRole.User, "hello")));
        Assert.True(SessionChatHistoryProvider.IsOwnedByUs(new ChatMessage(ChatRole.Assistant, "hi")));
    }

    [Fact]
    public void FrameworkInjectedMessages_AreFilteredOut()
    {
        ChatMessage injected = new(ChatRole.User, "todo snapshot")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.Attribution] = "TodoProvider",
            },
        };

        Assert.False(SessionChatHistoryProvider.IsOwnedByUs(injected));
    }

    /// <summary>
    /// 重新生成把跑过一轮的原消息重新当输入送进来，而框架此刻已在它身上就地盖了 _attribution。
    /// 摘章是 <c>TurnDriver.RunAsync</c> 的开场动作——漏了就是「气泡还在、重开会话没了」，
    /// 见 bug 记录（首条点名调用重新生成之后从历史里消失）。
    /// </summary>
    [Fact]
    public void RerunInput_AfterAttributionStamp_IsOwnedAgain()
    {
        ChatMessage input = new(ChatRole.User, "# Skill: wayfinder\n...")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.NamedSkill] = "wayfinder",
                [ChatMessageAnnotations.Attribution] = "ChatHistory",
            },
        };

        Assert.False(SessionChatHistoryProvider.IsOwnedByUs(input));

        ChatMessageAnnotations.ClearAttribution(input);

        Assert.True(SessionChatHistoryProvider.IsOwnedByUs(input));
        //摘的只是溯源章,呈现轴上的标记不能跟着丢——气泡要靠它折叠成用户敲的那一行
        Assert.True(input.AdditionalProperties!.ContainsKey(ChatMessageAnnotations.NamedSkill));
    }

    /// <summary>
    /// 知识库检索片段是「存而不供」：由 <c>StoreChatHistoryAsync</c> 直接写进历史，
    /// 供给时被滤掉，因此不该再从 RequestMessages 那条路进来一次。
    /// 漏了这道就是逐轮翻倍——与交接文档同一类隐患。
    /// </summary>
    [Fact]
    public void KnowledgeSnippets_AreNeverAppendedAgain()
    {
        ChatMessage snippets = new(ChatRole.Tool, "SourceName: doc\nSimilarity: 0.427\nContent: ...")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.Knowledge] = true,
            },
        };

        Assert.True(ChatMessageAnnotations.IsKnowledge(snippets));
        Assert.False(SessionChatHistoryProvider.IsOwnedByUs(snippets));
        Assert.False(ChatMessageAnnotations.IsKnowledge(new ChatMessage(ChatRole.User, "hello")));
    }

    /// <summary>
    /// 派活方插话的来源标记不能进 <c>Attribution</c> 那条过滤：它是子会话历史的一部分，
    /// 要落盘、要供给模型（只是来源需要被认出来）。复用溯源键的话，
    /// 这条纠偏在消费它的那次调用结束时根本落不了盘，等于白插。
    /// </summary>
    [Fact]
    public void ParentInterjection_IsOwnedByUs()
    {
        ChatMessage injection = new(ChatRole.User, "【发信人】先别管性能，把正确性修对");
        Assert.False(ChatMessageAnnotations.IsParentInterjection(injection));

        ChatMessageAnnotations.MarkParentInterjection(injection);

        Assert.True(ChatMessageAnnotations.IsParentInterjection(injection));
        Assert.True(SessionChatHistoryProvider.IsOwnedByUs(injection));
        Assert.False(ChatMessageAnnotations.IsParentInterjection(new ChatMessage(ChatRole.User, "hello")));
    }

    /// <summary>
    /// 框架产出的消息不带 CreatedAt，落历史时必须补上——<c>ChatSession.LastTime</c> 读的正是它。
    /// 不补的话缺失会被当成"现在"，会话列表那一行时间每次刷新都跳成刚刚
    /// </summary>
    [Fact]
    public void AppendedMessages_GetATimestamp_WhenTheFrameworkLeftItBlank()
    {
        DateTimeOffset fallback = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        ChatSession session = new() { IsTransient = true };
        ChatMessage fromFramework = new(ChatRole.Assistant, "framework reply");
        Assert.Null(fromFramework.CreatedAt);

        SessionChatHistoryProvider.AppendOwned(session, [fromFramework], fallback);

        Assert.Equal(fallback, fromFramework.CreatedAt);
        Assert.Equal(fallback.LocalDateTime, session.LastTime);
    }

    [Fact]
    public void AppendedMessages_KeepATimestampTheyAlreadyHad()
    {
        DateTimeOffset stamped = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        ChatSession session = new() { IsTransient = true };
        ChatMessage own = new(ChatRole.User, "hello") { CreatedAt = stamped };

        SessionChatHistoryProvider.AppendOwned(session, [own], stamped.AddMinutes(9));

        Assert.Equal(stamped, own.CreatedAt);
    }

    /// <summary>
    /// 请求消息回落到<b>本轮开始</b>而非落盘时刻。
    /// 框架交给持久化的请求消息是重建的副本、丢了时间戳，而落盘发生在一轮跑完之后——
    /// 两者共用落盘时刻的话，长回复跑过一分钟就会让用户消息显示得比模型回复还晚
    /// </summary>
    [Fact]
    public void RequestMessages_FallBackToTheTurnStart_NotTheStoreTime()
    {
        DateTimeOffset turnStart = new(2026, 1, 2, 10, 56, 0, TimeSpan.Zero);
        DateTimeOffset responseAt = turnStart.AddMinutes(2);
        DateTimeOffset storedAt = turnStart.AddMinutes(3);
        ChatSession session = new() { IsTransient = true };

        ChatMessage rebuiltUserMessage = new(ChatRole.User, "问题");
        ChatMessage reply = new(ChatRole.Assistant, "回答") { CreatedAt = responseAt };
        SessionChatHistoryProvider.AppendOwned(session, [rebuiltUserMessage], turnStart);
        SessionChatHistoryProvider.AppendOwned(session, [reply], storedAt);

        Assert.Equal(turnStart, rebuiltUserMessage.CreatedAt);
        Assert.True(rebuiltUserMessage.CreatedAt < reply.CreatedAt);
    }

    /// <summary>
    /// 缺时间戳的旧存档消息不能回落"现在":那会让同一条消息每次读到不同的时间
    /// </summary>
    [Fact]
    public void LastTime_ForAMessageWithoutATimestamp_FallsBackToTheSession_NotNow()
    {
        DateTimeOffset updated = DateTimeOffset.Now.AddDays(-5);
        ChatSession session = new() { IsTransient = true, UpdatedAt = updated };
        session.History.Add(new ChatMessage(ChatRole.Assistant, "no timestamp"));

        Assert.Equal(updated.LocalDateTime, session.LastTime);
    }
}

/// <summary>
/// 不变量之五：<b>整段系统提示由我们按固定顺序拼，人格在最前，不带第二个身份</b>。
/// 顺序是 基座(所有角色共用、系统锁定) → 角色人格(含工作循环) → 用户卡 → 对话模板 → 工具纪律与工作目录 → 工作区规矩(见 ADR 0005)。
/// 框架对 HarnessInstructions 只做一件事——拼在角色段<b>之前</b>，因此那一层必须留空；
/// 一旦有人把纪律段或框架默认塞回 HarnessInstructions，症状是小模型先读一大段英文工具纪律、
/// 角色人格被压在后面，实机极难归因。
/// </summary>
public class HarnessInstructionsCompositionTests
{
    private const string PersonaMarker = "I am the persona line";

    [Fact]
    public void AgentInstructions_PutThePersonaBeforeTheToolDisciplines()
    {
        HarnessAgentOptions options = BuildAgentOptions("/tmp/uiharu-agent-test");
        string instructions = options.ChatOptions?.Instructions ?? string.Empty;

        Assert.Equal(string.Empty, options.HarnessInstructions); //框架分层弃用,整段自己拼
        int persona = instructions.IndexOf(PersonaMarker, StringComparison.Ordinal);
        int disciplines = instructions.IndexOf(AgentPromptHeadings.FileOperations, StringComparison.Ordinal);
        Assert.True(persona >= 0, "角色人格丢了");
        Assert.True(disciplines > persona, "工具纪律必须排在角色人格之后");
        Assert.DoesNotContain("helpful AI assistant", instructions); //身份只由角色说
    }

    /// <summary>
    /// 基座层是 agent 系统提示的<b>固定第一段</b>（文档 §7 组装顺序：基座 → 人格 → 身份 → 场景 → 配置）。
    /// 它不随角色卡与能力配置而消失——所有 agent 角色共用、系统锁定，只对 agent 档主代理注入。
    /// 断言只认结构（第一段 + 人格在后），不认基座正文措辞——措辞是提示词作者的家务事。
    /// </summary>
    [Fact]
    public void AgentInstructions_PutTheBaseBeforeThePersona()
    {
        HarnessAgentOptions options = BuildAgentOptions("/tmp/uiharu-agent-test", out var segments);
        string instructions = options.ChatOptions?.Instructions ?? string.Empty;

        AgentPromptSegment baseSeg = Assert.Single(segments, x => x.Section == EPromptSection.Base);
        Assert.StartsWith(baseSeg.Text, instructions);
        int persona = instructions.IndexOf(PersonaMarker, StringComparison.Ordinal);
        Assert.True(persona > baseSeg.Text.Length, "基座必须排在角色人格之前");
    }

    /// <summary>
    /// 裸角色卡（正文不带一级标题）由装配层补上 <c># 角色</c>，排在基座之后、工具纪律之前。
    /// 基座第 4 条「以『角色』节为准」因此有了字面对得上的落点，不再是一个悬空引用。
    /// </summary>
    [Fact]
    public void AgentInstructions_BarePersonaGetsItsOwnTopLevelHeading()
    {
        HarnessAgentOptions options = BuildAgentOptions("/tmp/uiharu-agent-test", out var segments);
        string instructions = options.ChatOptions?.Instructions ?? string.Empty;

        AgentPromptSegment baseSeg = Assert.Single(segments, x => x.Section == EPromptSection.Base);
        int heading = instructions.IndexOf(AgentPromptHeadings.Character, StringComparison.Ordinal);
        int persona = instructions.IndexOf(PersonaMarker, StringComparison.Ordinal);
        Assert.True(heading >= 0, $"裸角色卡缺 {AgentPromptHeadings.Character} 标题");
        Assert.True(heading > baseSeg.Text.Length, "人格标题必须排在基座之后");
        Assert.True(persona > heading, "人格标题必须排在人格正文之前");
    }

    /// <summary>
    /// 角色卡自带一级标题时（ChenXi 卡 <c># 角色</c>、新建智能体预填 <c># 工作循环</c>），
    /// 装配层不再补插——再插就是一个提示词里角色段出现两个并列一级标题。
    /// </summary>
    [Fact]
    public void AgentInstructions_CardOwnHeadingIsNotDuplicated()
    {
        const string cardHead = "# 工作循环\n- 先把事实弄清楚再动手";
        HarnessAgentOptions options = BuildAgentOptions("/tmp/uiharu-agent-test", persona: cardHead);
        string instructions = options.ChatOptions?.Instructions ?? string.Empty;

        Assert.Contains(cardHead, instructions);
        Assert.DoesNotContain(AgentPromptHeadings.Character, instructions);
    }

    /// <summary>
    /// 人格 coda：系统提示的最后一个声音，自动取卡片的名与描述拼成
    /// <c>你是…</c>（<c>CharacterData.GetPersonaCoda</c>），不用填字段。
    /// 排在工作区规矩之后——吃结尾权重，长工具循环里人格才不漂。
    /// </summary>
    [Fact]
    public void AgentInstructions_EndsWithPersonaCoda()
    {
        const string coda = "你是晨曦，活泼、有冲劲";
        string instructions = BuildAgentOptions("/tmp/uiharu-agent-test",
            characterName: "晨曦", characterDescription: "活泼、有冲劲").ChatOptions?.Instructions ?? string.Empty;

        Assert.EndsWith(coda, instructions);
    }

    /// <summary>coda 登记在角色段名下：它就是人格的压缩，能力面板的「角色提示」档理应含它</summary>
    [Fact]
    public void PersonaCoda_RegisteredAsCharacterSegment()
    {
        const string coda = "你是晨曦，活泼、有冲劲";
        HarnessAgentOptions options = BuildAgentOptions("/tmp/uiharu-agent-test", out var segments,
            characterName: "晨曦", characterDescription: "活泼、有冲劲");

        Assert.Contains(segments,
            x => x.Section == EPromptSection.Character && x.Text == coda);
        Assert.EndsWith(coda, options.ChatOptions?.Instructions ?? string.Empty);
    }

    /// <summary>
    /// 无名卡不发 coda：没名字钉什么身份。描述是 coda 进提示词的唯一入口，
    /// 它没出现即证明空段没入册。
    /// </summary>
    [Fact]
    public void PersonaCoda_Absent_WithoutCharacterName()
    {
        string instructions = BuildAgentOptions("/tmp/uiharu-agent-test",
            characterDescription: "活泼、有冲劲").ChatOptions?.Instructions ?? string.Empty;

        Assert.DoesNotContain("活泼、有冲劲", instructions);
    }

    /// <summary>
    /// 工作区段只要指针不要正文(试行):系统提示每轮重发,全文放这里等于每轮交税;
    /// 模型自读进历史,付一次摊全场。主代理与子代理同一口径,子代理侧见 SubAgentBoundaryTests。
    /// </summary>
    [Fact]
    public void MainAgentWorkspaceSection_IsPointerOnly()
    {
        string body = new string('z', AgentInstructionsComposer.MaxWorkspaceInstructionsChars * 2);
        string main = BuildAgentOptions("/tmp/uiharu-agent-test",
            workspaceInstructions: body).ChatOptions?.Instructions ?? string.Empty;

        Assert.Contains("动手前先读一遍全文", main);
        Assert.DoesNotContain(body[..100], main);
    }

    /// <summary>
    /// 技能模型可见性的全局总闸(ADR 0003 例外):关掉后框架 provider 不挂,
    /// 广告列表与框架的 load_skill 三个工具一起从模型侧消失,由自建同名 load_skill
    /// 顶上(仍能按名加载广告列表内的被动技能,见 AgentAssembler.BuildTools);默认开着,
    /// 点名调用不依赖这一路,关上后照常可用。
    /// </summary>
    [Fact]
    public void AgentSkillsProvider_RespectsGlobalModelSkillsSwitch()
    {
        // 默认:agent 档挂技能 provider
        HarnessAgentOptions enabled = BuildAgentOptions("/tmp/uiharu-agent-test");
        Assert.False(enabled.DisableAgentSkillsProvider);

        // 全局关闭:provider 不挂
        HarnessAgentOptions disabled = BuildAgentOptions("/tmp/uiharu-agent-test", disableSkillsProvider: true);
        Assert.True(disabled.DisableAgentSkillsProvider);
    }

    /// <summary>
    /// 全局技能开关进入装配快照:切换后必须重建,否则模型继续按旧装配(挂/不挂)
    /// 收发消息。与 <c>DisabledSkills</c> 同口径——都属装配输入。
    /// </summary>
    [Fact]
    public void ChangedModelSkillsEnabled_ProduceDifferentSnapshot()
    {
        CharacterData character = new() { CharacterId = "agent", IsAgent = true };

        AgentAssemblyFacts on = AgentAssemblyFacts.Capture(character, "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, 1, modelSkillsEnabled: true);
        AgentAssemblyFacts off = AgentAssemblyFacts.Capture(character, "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, 1, modelSkillsEnabled: false);

        Assert.NotEqual(on, off); //开关切换 → 下一次挂接重建装配
    }

    /// <summary>
    /// 工具纪律段挂在自己的 <c># 工具</c> 父标题之下。
    ///
    /// 这不是排版洁癖：角色段（agent 档默认角色卡）以 <c># 工作循环</c> 起头，
    /// 工具纪律若像从前那样直接从 <c>## 工作目录</c> 开始，
    /// 按 markdown 结构读就整个成了「工作循环」的子节——层级说了一件与事实不符的事。
    /// </summary>
    [Fact]
    public void ToolDisciplines_LiveUnderTheirOwnTopLevelHeading()
    {
        HarnessAgentOptions options = BuildAgentOptions("/tmp/uiharu-agent-test");
        string instructions = options.ChatOptions?.Instructions ?? string.Empty;

        int tools = instructions.IndexOf(AgentPromptHeadings.Tools, StringComparison.Ordinal);
        Assert.True(tools >= 0, "工具纪律段缺少父标题");
        //每个二级段都在父标题之后,没有一个跑到外面去
        foreach (string section in new[]
                 { AgentPromptHeadings.WorkingDirectory("##"), AgentPromptHeadings.FileOperations })
        {
            int at = instructions.IndexOf(section, StringComparison.Ordinal);
            Assert.True(at > tools, $"{section} 跑到了 {AgentPromptHeadings.Tools} 之外");
        }
    }

    /// <summary>
    /// 一项工具纪律都没有时整段不出现：只挂一个空的父标题是纯噪声
    /// </summary>
    [Fact]
    public void ToolDisciplines_AreOmittedEntirely_WhenNothingIsMounted()
    {
        AgentToolConfig nothing = new()
        {
            EnableFileAccess = false,
            EnableVisionTool = false,
            EnableKnowledgeSearchTool = false,
            EnableSubAgent = false,
            // 这一项从前漏在这里:它默认为 true,于是这份"什么都没挂"的配置其实挂着 shell。
            // 从前看不出来是因为 shell 没有纪律段——它是唯一挂了工具却零指示的能力
            EnableShellExecution = false,
            // 同一个坑的第二次:联网也默认为 true,而它从前同样没有纪律段
            // (主代理挂了 WebSearch/WebFetch 却零指示)。段落清单两档共用之后它有了,
            // 于是这份"什么都没挂"的配置又变成挂着联网
            EnableWebSearch = false,
        };

        HarnessAgentOptions options = BuildAgentOptions(string.Empty, nothing);

        Assert.DoesNotContain(AgentPromptHeadings.Tools, options.ChatOptions?.Instructions ?? string.Empty);
    }

    /// <summary>
    /// 命令行纪律段<b>不许指名文件工具</b>，除非文件工具也在场。
    ///
    /// 「shell 开、文件访问关」是这条的关键组合：那一段前两条讲的是「这件事该归 `Shell`
    /// 还是归文件工具」，会指名 Read/Edit/Write，而那三个只随 <c>EnableFileAccess</c> 出现。
    /// 少了这条，它们会在文件工具缺席时照样发出去，指挥模型去调不存在的工具——
    /// 而这种失败在实机上极难归因（表现只是一次工具调用失败）。
    ///
    /// 主代理的通用版（按真实工具集校验反引号）做不了：装配一份真工具集要一个 chat client，
    /// 本套测试的助手只拼提示词。子代理那侧有通用版，见
    /// <c>SubAgentInstructions_OnlyNameToolsThatExist</c>。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShellDiscipline_NamesFileTools_OnlyWhenFileAccessIsMounted(bool fileAccess)
    {
        AgentToolConfig config = new()
        {
            EnableFileAccess = fileAccess,
            EnableShellExecution = true,
            EnableVisionTool = false,
            EnableKnowledgeSearchTool = false,
            EnableSubAgent = false,
        };

        HarnessAgentOptions options = BuildAgentOptions("/tmp/uiharu-agent-test", config);
        string instructions = options.ChatOptions?.Instructions ?? string.Empty;

        Assert.Contains(AgentPromptHeadings.Shell, instructions); //shell 开着,这一节必须在
        Assert.Contains($"`{CharacterRunnerFactory.ShellToolName}`", instructions);

        foreach (string fileTool in new[] { FileToolNames.Read, FileToolNames.Edit, FileToolNames.Write })
        {
            if (fileAccess) continue;
            Assert.DoesNotContain($"`{fileTool}`", instructions);
        }
    }

    /// <summary>
    /// 受管 Python 环境的纪律段<b>只在环境真的就绪时出现</b>，且必须寄生在命令行那一节之下。
    ///
    /// 两条都是承重的：Python 不是一个工具，是 <c>Shell</c> 的一个分项（见 ADR 0019）。
    /// 环境没建就把解释器路径写进提示词，模型会照着调然后白烧一次调用——
    /// 与 ADR 0017「判据取装配结果而非配置意图」是同一条道理。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PythonDiscipline_AppearsOnlyWhenEnvironmentIsReady(bool ready)
    {
        AgentToolConfig config = new() { EnableShellExecution = true, EnableFileAccess = true };
        string interpreter = ready ? "/tmp/uiharu-python-test/bin/python" : string.Empty;

        HarnessAgentOptions options = BuildAgentOptions("/tmp/uiharu-agent-test", config,
            pythonInterpreter: interpreter);
        string instructions = options.ChatOptions?.Instructions ?? string.Empty;

        if (!ready)
        {
            Assert.DoesNotContain(AgentPromptHeadings.Python, instructions);
            return;
        }

        Assert.Contains(AgentPromptHeadings.Python, instructions);
        // 反过来钉:解释器的绝对路径<b>不许</b>出现。环境由 PATH 前置激活,写进提示词就等于
        // 要求模型每次给一个含空格的长路径加引号(见 ADR 0019)
        Assert.DoesNotContain(interpreter, instructions);
        Assert.Contains("pip install", instructions);
        Assert.True(
            instructions.IndexOf(AgentPromptHeadings.Python, StringComparison.Ordinal) >
            instructions.IndexOf(AgentPromptHeadings.Shell, StringComparison.Ordinal),
            "Python 段必须排在命令行段之后——它是那一节的分项");
    }

    /// <summary>
    /// 没挂 shell 就<b>绝不</b>发 Python 段：没有任何工具跑得动那个解释器，
    /// 说了纯属噪声，还会诱导模型去找一个不存在的执行途径。
    /// </summary>
    [Fact]
    public void PythonDiscipline_NeverAppearsWithoutShell()
    {
        AgentToolConfig config = new() { EnableShellExecution = false, EnableFileAccess = true };

        HarnessAgentOptions options = BuildAgentOptions("/tmp/uiharu-agent-test", config,
            pythonInterpreter: "/tmp/uiharu-python-test/bin/python");

        Assert.DoesNotContain(AgentPromptHeadings.Python,
            options.ChatOptions?.Instructions ?? string.Empty);
    }

    /// <summary>
    /// 产出的 <c>file://</c> 前缀由我们算好写进提示词，<b>不让模型自己拼 URI</b>——
    /// Windows 上 <c>C:\a\b</c> 要变成 <c>file:///C:/a/b</c>，反斜杠与盘符两处都得改，
    /// 拼错的表现是对话里一张图都不出现，而且完全看不出为什么。
    /// 前缀住在通用草稿段（file/shell 独占的 agent 也要引用房间里的文件），
    /// 引用用裸图：渲染库给图片设了 HRef，点得开，不必再包一层链接。
    /// </summary>
    [Fact]
    public void OutputRoom_GivesFileUriPrefix_AndBareImageFormat()
    {
        const string room = "/tmp/uiharu-room-test/ws/12345678";

        string instructions = BuildAgentOptions("/tmp/uiharu-agent-test", outputRoom: room)
            .ChatOptions?.Instructions ?? string.Empty;

        Assert.Contains(AgentPromptHeadings.OutputRoom("##"), instructions);
        Assert.Contains(new Uri(room + Path.DirectorySeparatorChar).AbsoluteUri, instructions);
        Assert.Contains("![说明](", instructions);
        Assert.DoesNotContain("[![", instructions);
    }

    /// <summary>无房间（无会话）时草稿段不出现：指一个不存在的目录比不说更糟</summary>
    [Fact]
    public void OutputRoom_IsAbsent_WhenNoRoom()
    {
        string instructions = BuildAgentOptions("/tmp/uiharu-agent-test")
            .ChatOptions?.Instructions ?? string.Empty;

        Assert.DoesNotContain(AgentPromptHeadings.OutputRoom("##"), instructions);
    }

    /// <summary>
    /// Python 段只讲环境与跑法，不再复述房间与引用格式（那是通用草稿段的事）——
    /// 同一个路径与同一套格式每轮印两遍是纯粹的固定开销。
    /// 去重的本质：file URI 前缀整段只出现一次；旧复述句消失。
    /// 无房间时（无会话的能力预览，不跑轮次）连前缀也没有——那条路本来也执行不了
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PythonDiscipline_DoesNotRepeatTheRoom(bool roomKnown)
    {
        AgentToolConfig config = new() { EnableShellExecution = true, EnableFileAccess = true };
        const string room = "/tmp/uiharu-room-test/ws/12345678";
        string roomUri = new Uri(room + Path.DirectorySeparatorChar).AbsoluteUri;

        string instructions = BuildAgentOptions("/tmp/uiharu-agent-test", config,
            pythonInterpreter: "/tmp/uiharu-python-test/bin/python",
            outputRoom: roomKnown ? room : string.Empty)
            .ChatOptions?.Instructions ?? string.Empty;

        Assert.Contains(AgentPromptHeadings.Python, instructions);
        Assert.Contains("pip install", instructions);
        Assert.DoesNotContain("写到这个目录", instructions);
        Assert.Equal(roomKnown ? 1 : 0, instructions.Split(roomUri).Length - 1);
    }

    /// <summary>
    /// 工作区规矩排在我们这段的最尾(框架 provider 段仍在其后,那不由我们控制)。
    /// 它是"这个项目的特殊规矩"，该压在通用纪律之后。
    /// 主路现在只要指针不要正文(试行),所以这里钉的是指针的位置,不是正文。
    /// </summary>
    [Fact]
    public void AgentInstructions_PutWorkspaceRulesLast()
    {
        HarnessAgentOptions options = BuildAgentOptions("/tmp/uiharu-agent-test",
            workspaceInstructions: "never touch the vendor folder");
        string instructions = options.ChatOptions?.Instructions ?? string.Empty;

        Assert.True(instructions.IndexOf("动手前先读一遍全文", StringComparison.Ordinal) >
                    instructions.IndexOf(AgentPromptHeadings.FileOperations, StringComparison.Ordinal),
            "工作区规矩必须排在工具纪律之后");
    }

    /// <summary>
    /// 主代理也必须被告知工作目录的绝对路径。同一个坑：路径只被拿去构造工具，
    /// 从没进过提示词，模型只能自己编。
    /// </summary>
    [Fact]
    public void AgentInstructions_StateTheWorkingDirectory()
    {
        const string workingDirectory = "/tmp/uiharu-agent-test";

        HarnessAgentOptions options = BuildAgentOptions(workingDirectory);
        string instructions = options.ChatOptions?.Instructions ?? string.Empty;

        Assert.Contains(workingDirectory, instructions);
        Assert.Contains(AgentToolPrompts.FileReadDefault, instructions); //纪律段没吃掉工作目录段
    }

    /// <summary>
    /// 关掉的工具其纪律段必须一并消失：留着就是纯噪声，还会指挥模型去调不存在的工具。
    /// 能力配置来自角色，这条同时验证装配确实读的是角色那份。
    /// </summary>
    [Fact]
    public void AgentInstructions_OmitDisciplinesOfDisabledTools()
    {
        AgentToolConfig tools = new() { EnableFileAccess = false, EnableSubAgent = false };

        string instructions = BuildAgentOptions("/tmp/uiharu-agent-test", tools)
            .ChatOptions?.Instructions ?? string.Empty;

        Assert.DoesNotContain(AgentPromptHeadings.FileOperations, instructions);
        Assert.DoesNotContain(AgentToolPrompts.BuildDelegation(string.Empty), instructions);
    }

    /// <summary>
    /// 分段清单必须逐字覆盖真正发出去的那段提示，且空段不入册。
    ///
    /// 能力面板按段报占用，靠的就是这份清单。清单一旦与整串脱节，症状是面板上的分项之和
    /// 与合计对不上——而那正是这块面板唯一的用处。
    /// </summary>
    [Fact]
    public void PromptSegments_AreRegisteredVerbatim_AndSkipEmptySections()
    {
        HarnessAgentOptions options = BuildAgentOptions("/tmp/uiharu-agent-test",
            out IReadOnlyList<AgentPromptSegment> segments,
            workspaceInstructions: "never touch the vendor folder");
        string instructions = options.ChatOptions?.Instructions ?? string.Empty;

        foreach (AgentPromptSegment segment in segments)
        {
            Assert.Contains(segment.Text, instructions, StringComparison.Ordinal);
        }

        Assert.Contains(segments, x => x.Section == EPromptSection.Character);
        Assert.Contains(segments, x => x.Section == EPromptSection.ToolDisciplines);
        Assert.Contains(segments, x => x.Section == EPromptSection.Workspace);
        Assert.Contains(segments, x => x.Section == EPromptSection.Base); //基座恒在册,能力面板要按段报占用
        Assert.DoesNotContain(segments, x => x.Section == EPromptSection.Mcp); //本例没接 MCP
    }

    /// <summary>
    /// MCP 自述那一段登记在册但<b>不计入合计</b>：它已经在 MCP 那一档里算过一次。
    /// 少了这条，接一个带长自述的 server 会让固定开销凭空翻倍。
    /// </summary>
    [Fact]
    public void PromptSegments_ExcludeMcpNotesFromTheTotal()
    {
        McpToolSet mcp = new() { Instructions = "## demo\nuse the demo server for demos." };

        BuildAgentOptions("/tmp/uiharu-agent-test", out IReadOnlyList<AgentPromptSegment> segments, mcp: mcp);

        AgentPromptSegment notes = Assert.Single(segments, x => x.Section == EPromptSection.Mcp);
        Assert.False(notes.CountsTowardTotal);
        Assert.All(segments.Where(x => x.Section != EPromptSection.Mcp), x => Assert.True(x.CountsTowardTotal));
    }

    /// <summary>记忆目录段(ADR 0028)出现时给出绝对路径，并教模型先 Glob 再 Read</summary>
    [Fact]
    public void MemorySection_Appears_WithMemoryDirectory()
    {
        const string memory = "/tmp/uiharu-data/Agent/Workspaces/ws/Memory";

        string instructions = BuildAgentOptions("/tmp/uiharu-agent-test", memoryDirectory: memory)
            .ChatOptions?.Instructions ?? string.Empty;

        Assert.Contains(AgentPromptHeadings.Memory("##"), instructions);
        Assert.Contains(memory, instructions);
        Assert.Contains("`Glob`", instructions);
    }

    /// <summary>无记忆目录(回退档或无工作区上下文)时记忆段不出现</summary>
    [Fact]
    public void MemorySection_IsAbsent_WithoutMemoryDirectory()
    {
        string instructions = BuildAgentOptions("/tmp/uiharu-agent-test")
            .ChatOptions?.Instructions ?? string.Empty;

        Assert.DoesNotContain(AgentPromptHeadings.Memory("##"), instructions);
    }

    /// <summary>记忆段只指名真实在场的能力：shell 关掉时不提"删记忆走 Shell"</summary>
    [Fact]
    public void MemorySection_OmitsShellHint_WhenShellIsMountedOff()
    {
        AgentToolConfig config = new() { EnableShellExecution = false };
        const string memory = "/tmp/uiharu-data/Agent/Workspaces/ws/Memory";

        string instructions = BuildAgentOptions("/tmp/uiharu-agent-test", config, memoryDirectory: memory)
            .ChatOptions?.Instructions ?? string.Empty;

        Assert.Contains(AgentPromptHeadings.Memory("##"), instructions);
        Assert.DoesNotContain("删记忆文件走", instructions);
    }

    /// <summary>产出目录:本套测试不碰真实 AppPaths,只要是个合法绝对路径就够</summary>
    private static readonly string PythonOutputDirectory =
        Path.Combine(Path.GetTempPath(), "uiharu-agent-outputs-test");

    private static HarnessAgentOptions BuildAgentOptions(string workingDirectory,
        AgentToolConfig? tools = null, string workspaceInstructions = "", McpToolSet? mcp = null,
        string pythonInterpreter = "", string outputRoom = "", string memoryDirectory = "",
        bool disableSkillsProvider = false, string characterName = "", string characterDescription = "",
        string? persona = null)
    {
        return BuildAgentOptions(workingDirectory, out _, tools, workspaceInstructions, mcp,
            pythonInterpreter, outputRoom, memoryDirectory, disableSkillsProvider,
            characterName, characterDescription, persona);
    }

    private static HarnessAgentOptions BuildAgentOptions(string workingDirectory,
        out IReadOnlyList<AgentPromptSegment> segments, AgentToolConfig? tools = null,
        string workspaceInstructions = "", McpToolSet? mcp = null, string pythonInterpreter = "",
        string outputRoom = "", string memoryDirectory = "", bool disableSkillsProvider = false,
        string characterName = "", string characterDescription = "", string? persona = null)
    {
        CharacterData character = new()
        {
            CharacterId = "agent", IsAgent = true, Tools = tools ?? new AgentToolConfig(),
            CharacterName = characterName, Description = characterDescription,
        };
        string skillsDir = Path.Combine(Path.GetTempPath(), "uiharu-skills-test");
        Directory.CreateDirectory(skillsDir);

        // 角色段由调用方先填好(实机里是 CharacterPromptBuilder 的产物)
        ChatOptions chatOptions = new() { Instructions = persona ?? PersonaMarker };

        // 装配计划取代原先那 14 个位置参数:每一项写着自己的名字,加字段也不必改所有调用点
        AgentAssemblyPlan plan = new()
        {
            Profile = new AgentBuildProfile
            {
                Character = character,
                PermissionMode = EAgentPermissionMode.AutoEdit,
            },
            WorkingDirectory = workingDirectory,
            WorkspaceInstructions = workspaceInstructions,
            SkillsSource = new AgentFileSkillsSource(skillsDir),
            DisableSkillsProvider = disableSkillsProvider,
            Mcp = mcp ?? McpToolSet.Empty,
            PythonInterpreterPath = pythonInterpreter,
            PythonOutputDirectory = pythonInterpreter.Length > 0 ? PythonOutputDirectory : string.Empty,
            OutputRoomDirectory = outputRoom,
            MemoryDirectory = memoryDirectory,
        };

        // shell 路径固定给一个假值:本套测试只校验拼接与顺序,真解析出来的 shell 因机而异
        return AgentOptionsFactory.BuildAgentOptions(plan, new StubHistoryProvider(), [], chatOptions,
            "/bin/bash", out segments);
    }

    /// <summary>
    /// 主代理指令里凡反引号包裹的内容必须是已知工具名。
    /// 子代理侧能按真实装配的工具集反向校验(见
    /// <c>SubAgentBoundaryTests.SubAgentInstructions_OnlyNameToolsThatExist</c>);
    /// 主代理侧装配真工具集要 chat client,所以退而求其次用「全量已知工具名」做反向校验——
    /// 不在集合里的反引号内容(python、[sub-session: …] 这类历史违例)直接失败。
    /// 集合刻意不写字面量:全部取各 Tool 的 ToolName 常量,新增工具时漏加会在这里暴露。
    /// </summary>
    [Fact]
    public void MainAgentInstructions_BacktickNamesMustBeKnownTools()
    {
        string instructions = BuildAgentOptions("/tmp/uiharu-agent-test")
            .ChatOptions?.Instructions ?? string.Empty;

        HashSet<string> known = new(StringComparer.Ordinal)
        {
            FileToolNames.Read, FileToolNames.Write, FileToolNames.Edit, FileToolNames.Glob,
            FileToolNames.Grep, CharacterRunnerFactory.ShellToolName,
            WebSearchTool.ToolName, WebFetchTool.ToolName, VisionTool.ToolName, KnowledgeTool.ToolName,
            SchedulerTools.ToolName,
            SubAgentTool.ToolName,
        };

        MatchCollection mentioned = Regex.Matches(instructions, "`([^`]+)`");
        Assert.NotEmpty(mentioned); //反引号写法本身也要在,否则这条不变量会空转
        foreach (Match match in mentioned)
        {
            Assert.True(known.Contains(match.Groups[1].Value),
                $"主代理指令里反引号内容 `{match.Groups[1].Value}` 不是已知工具名");
        }
    }

    private sealed class StubHistoryProvider : ChatHistoryProvider
    {
        public override IReadOnlyList<string> StateKeys => [];

        protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
            InvokingContext context, CancellationToken cancellationToken = default)
        {
            return new ValueTask<IEnumerable<ChatMessage>>([]);
        }

        protected override ValueTask StoreChatHistoryAsync(
            InvokedContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}

/// <summary>
/// 不变量之四：<b>子代理不越权、不递归</b>。
///
/// 子代理现在跑<b>它自己的</b> <c>TurnDriver</c>（一次委派就是一个子会话），
/// 审批请求冒到派活者这一轮的回应口，所以「非完全自动档必须只读」那条硬裁剪已经解除：
/// 挂什么由能力配置定，能不能动手由 <see cref="ApprovalModeMapper"/> 定，
/// 与主代理完全同一口径（见 ADR 0021）。
///
/// 仍然钉死的两条：<b>探索档恒定只读</b>（产品决定——调研不该顺手改东西），
/// 以及<b>工具集绝不含子代理工具自身</b>（无限递归）。
/// </summary>
public class SubAgentBoundaryTests
{
    private const string TestWorkingDirectory = "/tmp/uiharu-subagent-test";

    private static SubAgentAssembly.SubAgentAssemblyInput NewInput(
        AgentToolConfig? config = null,
        EAgentPermissionMode mode = EAgentPermissionMode.AutoEdit,
        string workspaceInstructions = "")
    {
        return new SubAgentAssembly.SubAgentAssemblyInput
        {
            Config = config ?? new AgentToolConfig(),
            WorkingDirectory = TestWorkingDirectory,
            PermissionMode = mode,
            WorkspaceInstructions = workspaceInstructions,
        };
    }

    private static List<string> ToolNamesOf(HarnessAgentOptions? options)
    {
        Assert.NotNull(options);
        return options!.ChatOptions!.Tools!.OfType<AIFunction>().Select(x => x.Name).ToList();
    }

    /// <summary>
    /// 子代理的采样参数跟着输入走:输入给了就进 ChatOptions,不给就保持旧行为(空)。
    /// 从前 <c>BuildSubAgentOptions</c> 里是裸 <c>new ChatOptions</c>,卡上的 temperature
    /// 改了也到不了子代理——主代理那条路(Assemble 62 行)一直是对的,只有这条漏了。
    /// </summary>
    [Fact]
    public void SubAgentOptions_CarrySamplingParams_FromInput()
    {
        SubAgentAssembly.SubAgentAssemblyInput input = NewInput() with
        {
            Sampling = new ChatPromptExecutionSettings
            {
                OmitSamplingParams = false, Temperature = 0.7, TopP = 0.8,
            },
        };

        HarnessAgentOptions? options = SubAgentAssembly.BuildSubAgentOptions(input);

        Assert.NotNull(options);
        Assert.Equal(0.7f, options!.ChatOptions!.Temperature);
        Assert.Equal(0.8f, options.ChatOptions.TopP);
    }

    /// <summary>输入没给采样时保持旧行为:ChatOptions 里不带采样参数,不替调用方编值</summary>
    [Fact]
    public void SubAgentOptions_OmitSampling_WhenInputHasNone()
    {
        HarnessAgentOptions? options = SubAgentAssembly.BuildSubAgentOptions(NewInput());

        Assert.NotNull(options);
        Assert.Null(options!.ChatOptions!.Temperature);
        Assert.Null(options.ChatOptions.TopP);
    }

    /// <summary>
    /// 探索档恒定只读，<b>与档位无关</b>。这是产品决定不是技术限制：
    /// 一次"去看一眼"不该顺手改掉东西。
    /// </summary>
    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public void SubAgentTools_AreReadOnly_ForExplorer(EAgentPermissionMode mode)
    {
        List<string> names = ToolNamesOf(SubAgentAssembly.BuildSubAgentOptions(
            NewInput(mode: mode) with { SubAgentProfile = SubAgentProfile.Explorer }));

        // 探索档的技能面钉死:Glob/Grep/Read 三件套之外,不挂联网、不挂写工具
        Assert.Contains(FileToolNames.Read, names);
        Assert.Contains(FileToolNames.Glob, names);
        Assert.Contains(FileToolNames.Grep, names);
        Assert.DoesNotContain(WebSearchTool.ToolName, names);
        Assert.DoesNotContain(WebFetchTool.ToolName, names);
        // 名单取自工具侧的那一份,不在测试里重抄一遍:漏抄一个新增的写工具,
        // 这条不变量就会在不报错的情况下失效
        string[] mutating = [..FileToolNames.Mutating, CharacterRunnerFactory.ShellToolName];
        Assert.DoesNotContain(names, name => mutating.Contains(name));
    }

    /// <summary>
    /// 通用档在<b>每一个</b>权限档下都挂写工具——能不能真的动手交给
    /// <see cref="ApprovalModeMapper"/>，与主代理同一口径。
    /// 这条从前是反的（非完全自动档削成只读），改动理由见 ADR 0021。
    /// </summary>
    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public void SubAgentTools_IncludeWriteTools_ForGeneral(EAgentPermissionMode mode)
    {
        List<string> names = ToolNamesOf(SubAgentAssembly.BuildSubAgentOptions(NewInput(mode: mode)));

        Assert.Contains(FileToolNames.Write, names);
        Assert.Contains(FileToolNames.Edit, names);
    }

    /// <summary>
    /// 审批规则必须与主代理同源：档位语义只能有一处定义（<see cref="ApprovalModeMapper"/>），
    /// 否则「完全自动」在主代理与子代理身上会渐渐变成两个意思。
    /// </summary>
    [Fact]
    public void SubAgent_ApprovalRules_ComeFromTheSameMapper()
    {
        HarnessAgentOptions? options =
            SubAgentAssembly.BuildSubAgentOptions(NewInput(mode: EAgentPermissionMode.FullAuto));

        Assert.NotNull(options);
        Assert.False(options!.DisableToolAutoApproval); //中间件必须在,否则规则等于没设
        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> rules =
            options.ToolApprovalAgentOptions!.AutoApprovalRules!.ToList();
        Assert.Equal(ApprovalModeMapper.BuildRules(EAgentPermissionMode.FullAuto).Count, rules.Count);
    }

    /// <summary>
    /// <b>子会话必须接上历史持久化。</b>
    ///
    /// 这条不是洁癖：<c>AgentOptionsFactory.CreateSubAgentBaseOptions</c> 不设
    /// <c>ChatHistoryProvider</c>（从前子代理是一次性的纯工具循环，不需要落盘），
    /// 所以子会话这条路<b>必须自己补上</b>。漏了的表现是完全静默——子代理照跑、日志照出，
    /// 而 <c>ChatSession.History</c> 恒空：窗口一片空白、连派出去的那条任务都看不见、
    /// <c>HistoryAppended</c> 永不触发、续跑没有上下文可续。实机踩过一次。
    /// </summary>
    [Fact]
    public void SubSession_AlwaysPersistsItsHistory()
    {
        AgentAssemblyPlan plan = NewSubSessionPlan();

        SubAgentAssembly.SubSessionAssembly assembled = SubAgentAssembly.BuildSubSessionAssembly(plan);

        Assert.NotNull(assembled.Options.ChatHistoryProvider);
    }

    /// <summary>
    /// <b>点名的子智能体翻回普通角色后，它的老子会话不能装回工具。</b>
    ///
    /// 翻转时 <c>Tools</c> 刻意不清（翻回来不丢配置），而子会话分支从前只看
    /// <c>profile.SubAgent</c>、不查身份——于是一个陪聊角色会按残留配置挂上 shell，
    /// 工作目录还是空串（ADR 0043「已定」）。界面锁住了翻转，但老 JSON 与用户改过的内置卡副本绕得过去。
    /// </summary>
    [Fact]
    public async Task SubSession_OfCharacterNoLongerAgent_MountsNoTools()
    {
        CharacterData character = new()
        {
            CharacterId = "writer",
            IsAgent = false,
            Tools = new AgentToolConfig { EnableShellExecution = true, EnableFileAccess = true },
        };
        AgentAssemblyPlan plan = new()
        {
            Profile = new AgentBuildProfile
            {
                Character = character,
                PermissionMode = EAgentPermissionMode.AutoEdit,
                SubAgent = new SubAgentIdentity("parent-1", ESubAgentType.General, "Writer"),
            },
            WorkingDirectory = TestWorkingDirectory,
        };

        await using AgentHandle handle = AgentAssembler.Assemble(plan);

        Assert.Null(handle.ChatOptions?.Tools);
    }

    /// <summary>
    /// 一个子会话的装配计划：匿名通用档，关掉 shell 免得测试真去解析本机 shell。
    /// </summary>
    private static AgentAssemblyPlan NewSubSessionPlan()
    {
        CharacterData character = new()
        {
            CharacterId = nameof(DefaultCharacter.AnonymousAgent),
            IsAgent = true,
            Tools = new AgentToolConfig { EnableShellExecution = false },
        };
        return new AgentAssemblyPlan
        {
            Profile = new AgentBuildProfile
            {
                Character = character,
                PermissionMode = EAgentPermissionMode.AutoEdit,
                SubAgent = new SubAgentIdentity("parent-1", ESubAgentType.General, string.Empty),
            },
            WorkingDirectory = TestWorkingDirectory,
        };
    }

    /// <summary>
    /// 子代理工具集里绝不能有子代理工具自身：那是无限递归，
    /// 本地模型会被层层嵌套的子代理占满。连完全自动档也不例外。
    /// </summary>
    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public void SubAgentTools_DoNotIncludeSubAgentItself(EAgentPermissionMode mode)
    {
        // ADR 0044 归一之后只剩一把工具要查。不变量本身一个字没变：
        // 子会话的工具集里绝不能有委派工具，否则可以无限套娃。
        Assert.DoesNotContain(SubAgentTool.ToolName,
            ToolNamesOf(SubAgentAssembly.BuildSubAgentOptions(NewInput(mode: mode))));
    }

    /// <summary>
    /// 主代理特有的那批不下放给子代理：它拿的是一份任务书，不需要再自己装载指令
    /// （技能）、排定时任务，也没有会话可供检索记忆。
    /// </summary>
    [Fact]
    public void SubAgentTools_ExcludeMainAgentOnlyTools()
    {
        AgentToolConfig config = new() { EnableKnowledgeSearchTool = true, EnableScheduledTasks = true };
        List<string> names = ToolNamesOf(SubAgentAssembly.BuildSubAgentOptions(
            NewInput(config, EAgentPermissionMode.FullAuto)));

        Assert.DoesNotContain(KnowledgeTool.ToolName, names);
        Assert.DoesNotContain(SchedulerTools.ToolName, names);

        HarnessAgentOptions? options = SubAgentAssembly.BuildSubAgentOptions(
            NewInput(config, EAgentPermissionMode.FullAuto));
        Assert.True(options!.DisableAgentSkillsProvider);
        Assert.True(options.DisableTodoProvider);
        Assert.True(options.DisableFileMemory);
    }

    /// <summary>
    /// 无人值守兜底必须真的设上：定时任务到点后没人按停止，
    /// 一个死循环的子代理会一直烧本地模型。子代理能改东西之后这条更承重。
    /// </summary>
    [Fact]
    public void SubAgent_HasIterationCap()
    {
        HarnessAgentOptions? options = SubAgentAssembly.BuildSubAgentOptions(NewInput());

        Assert.NotNull(options);
        Assert.Equal(SubAgentTool.MaxIterations, options!.MaximumIterationsPerRequest);
    }

    /// <summary>
    /// 子代理与主代理同一口径：工作循环归它自己那份指令，harness 段为空。
    /// 框架默认那段的身份句会和子代理指令开头的「# 角色」抢身份(见 ADR 0004)。
    /// </summary>
    [Fact]
    public void SubAgent_KeepsTheWorkLoopInItsOwnInstructions()
    {
        HarnessAgentOptions? options = SubAgentAssembly.BuildSubAgentOptions(NewInput());

        Assert.NotNull(options);
        Assert.Equal(string.Empty, options!.HarnessInstructions);
        Assert.Contains(AgentToolPrompts.AgentWorkLoop, options.ChatOptions?.Instructions);
        Assert.DoesNotContain("helpful AI assistant", options.ChatOptions?.Instructions);
    }

    /// <summary>
    /// 子智能体<b>不能比派活的那一个能力更大</b>。名单里挂一个开着 shell 的子智能体，
    /// 不该给关掉了 shell 的父智能体开后门——生效配置取两者交集。
    /// </summary>
    [Fact]
    public void SubAgentCapabilities_NeverExceedTheParent()
    {
        AgentToolConfig parent = new() { EnableShellExecution = false, EnableWebSearch = false };
        AgentToolConfig child = new() { EnableShellExecution = true, EnableWebSearch = true };

        AgentToolConfig effective = child.Intersect(parent);

        Assert.False(effective.EnableShellExecution);
        Assert.False(effective.EnableWebSearch);
        Assert.True(effective.EnableFileAccess); //两边都开的仍然开
    }

    /// <summary>
    /// 交集对禁用清单取<b>并集</b>：任一侧禁掉的技能都不该出现在子智能体那儿。
    /// </summary>
    [Fact]
    public void SubAgentDisabledSkills_AreTheUnionOfBothSides()
    {
        AgentToolConfig parent = new() { DisabledSkills = { "a" } };
        AgentToolConfig child = new() { DisabledSkills = { "b" } };

        List<string> effective = child.Intersect(parent).DisabledSkills;

        Assert.Contains("a", effective);
        Assert.Contains("b", effective);
    }

    /// <summary>
    /// 点名的子智能体：人格是身份本身（角色卡已说"我是谁"）。无 role 时 # 角色 段没有
    /// 可说的身份信息、整段省略（与主代理同一口径，见 ADR 0005）。
    /// </summary>
    [Fact]
    public void NamedSubAgent_PutsItsPersonaFirst()
    {
        HarnessAgentOptions? options = SubAgentAssembly.BuildSubAgentOptions(
            NewInput() with { Persona = "I am the research specialist", Name = "Researcher" });

        Assert.NotNull(options);
        string instructions = options!.ChatOptions?.Instructions ?? string.Empty;
        Assert.Equal("Researcher", options.Name);
        Assert.StartsWith("I am the research specialist", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain(AgentPromptHeadings.SubAgentRole, instructions);
    }

    /// <summary>给了 role 时，role 收进 # 角色 段且排在人格之后——"role 放角色段"这个设计被钉住</summary>
    [Fact]
    public void NamedSubAgent_RoleGoesIntoTheRoleSection()
    {
        HarnessAgentOptions? options = SubAgentAssembly.BuildSubAgentOptions(
            NewInput() with
            {
                Persona = "I am the research specialist",
                Name = "Researcher",
                Role = "senior C# reviewer",
            });

        Assert.NotNull(options);
        string instructions = options!.ChatOptions?.Instructions ?? string.Empty;
        int persona = instructions.IndexOf("I am the research specialist", StringComparison.Ordinal);
        int roleSection = instructions.IndexOf(AgentPromptHeadings.SubAgentRole, StringComparison.Ordinal);
        Assert.True(persona >= 0, "人格必须在场");
        Assert.True(roleSection > persona, "role 段必须排在人格之后");
        Assert.Contains("senior C# reviewer", instructions);
    }

    /// <summary>
    /// 提示语里用反引号指名的工具，必须真实存在于同一份装配出来的工具集里。
    /// 这条曾经不成立：子代理指令写着 <c>web_search</c> / <c>web_fetch</c>，
    /// 而工具实际叫 WebSearch / WebFetch——指挥模型去调不存在的工具，
    /// 只会换来一次工具调用失败，实机上极难归因。
    /// 约定因此是：提示语提到工具一律写 `反引号 + 工具的 ToolName 常量`。
    ///
    /// 这条不变量同时也是「子代理提示词不开放给调用方 AI」的理由：
    /// 固定段里的工具名由我们保证存在，而调用方看不见子代理挂了哪些工具。
    /// </summary>
    [Theory]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public void SubAgentInstructions_OnlyNameToolsThatExist(EAgentPermissionMode mode)
    {
        // 输入拼到能拼的最满:命令行/Python/房间段都进被校验的指令。
        // 从前 ShellTool/PythonOutputDirectory 不设,这几段根本不出现,
        // 于是它们的反引号违例(如裸 `python`)绕过了这条不变量
        HarnessAgentOptions? options = SubAgentAssembly.BuildSubAgentOptions(
            NewInput(mode: mode) with
            {
                ShellTool = StubShellTool(),
                PythonOutputDirectory = "/tmp/uiharu-room-test/ws/12345678",
                OutputFolderName = "ws-seg/12345678",
            });

        Assert.NotNull(options);
        ChatOptions chatOptions = options!.ChatOptions!;
        HashSet<string> mounted = new(chatOptions.Tools!.OfType<AIFunction>().Select(x => x.Name),
            StringComparer.Ordinal);

        MatchCollection mentioned = Regex.Matches(chatOptions.Instructions ?? string.Empty, "`([^`]+)`");
        Assert.NotEmpty(mentioned); //反引号写法本身也要在,否则这条不变量会空转

        foreach (Match match in mentioned)
        {
            string name = match.Groups[1].Value;
            Assert.True(mounted.Contains(name),
                $"子代理指令里指名了工具 `{name}`,但装配出来的工具集里没有它。已装配:{string.Join(", ", mounted)}");
        }
    }

    /// <summary>
    /// 子代理必须被告知工作目录的绝对路径。这条曾经不成立：<c>workingDirectory</c> 只被拿去
    /// 构造工具，从没进过任何提示词——于是模型自己编一个占位路径，实机见过
    /// <c>Glob(pattern: "*.*", root: "/path/to/project")</c>，白烧一次工具调用。
    /// 子代理比主代理更需要这段：它连一句用户原话都看不到，没有任何线索能反推出根在哪。
    /// </summary>
    [Fact]
    public void SubAgentInstructions_StateTheWorkingDirectory()
    {
        string instructions = SubAgentAssembly
            .BuildSubAgentOptions(NewInput())!.ChatOptions!.Instructions!;

        Assert.Contains(TestWorkingDirectory, instructions);
    }

    /// <summary>
    /// 子代理也认得草稿目录（派活者那一间）：需要审批的操作本就问到用户那里，
    /// 测试脚本有地方去，就不必为它们多弹一次。身上有写能力才说——
    /// 探索档无写工具又无 shell，说了也只是指一个写不进去的目录
    /// </summary>
    [Fact]
    public void SubAgentInstructions_StateTheOutputRoom_WhenItCanWrite()
    {
        string instructions = SubAgentAssembly.BuildSubAgentOptions(
                NewInput(mode: EAgentPermissionMode.FullAuto) with { OutputFolderName = "ws-seg/12345678" })!
            .ChatOptions!.Instructions!;

        Assert.Contains(AgentPromptHeadings.OutputRoom("#"), instructions);
        Assert.Contains("12345678", instructions);
    }

    /// <summary>子代理的 Python 段同样不复述房间与引用格式</summary>
    [Fact]
    public void SubAgentPythonDiscipline_DoesNotRepeatTheRoom()
    {
        SubAgentAssembly.SubAgentAssemblyInput input = NewInput(mode: EAgentPermissionMode.FullAuto) with
        {
            ShellTool = StubShellTool(),
            PythonOutputDirectory = "/tmp/uiharu-room-test/ws/12345678",
            OutputFolderName = "ws-seg/12345678",
        };

        string instructions = SubAgentAssembly.BuildSubAgentOptions(input)!
            .ChatOptions!.Instructions!;

        // 房间 id8 恰出现一次：草稿目录段正文里那一次。
        // 子代理那份草稿目录段不再给 markdown 图片语法(它的正文不进用户对话,
        // 交出去的是报告,展示归派活方),于是派生的 file URI 那一次也没有了
        Assert.Contains("pip install", instructions);
        Assert.Equal(1, instructions.Split("12345678").Length - 1);
        Assert.DoesNotContain("写到这个目录", instructions);
    }

    /// <summary>
    /// 同一段提示词<b>不得出现两次</b>。
    ///
    /// 这条曾经不成立，而且是静默的：新建智能体时工作循环那一段会被预填进角色卡
    /// （<c>HomePageData.NewCharacterAsync</c> → <c>CharacterData.Template</c>，ADR 0004），
    /// 而子代理装配又在末尾无条件追加同一份常量——于是点名一个子智能体时，
    /// 「# 工作循环」在同一份系统提示里逐字出现两遍，每轮都多付一遍钱。
    ///
    /// 只断「不得两次」而<b>不断「必须一次」</b>：用户可以把角色卡改得面目全非，
    /// 那时跳过追加是对的，强断存在反而会把合法用法判成错误。
    /// </summary>
    [Fact]
    public void NamedSubAgent_DoesNotRepeatTheWorkLoop()
    {
        HarnessAgentOptions? options = SubAgentAssembly.BuildSubAgentOptions(
            NewInput() with
            {
                Persona = "I am the research specialist\n\n" + AgentToolPrompts.AgentWorkLoop,
                Name = "Researcher",
            });

        string instructions = options!.ChatOptions?.Instructions ?? string.Empty;
        Assert.Equal(1, instructions.Split(AgentToolPrompts.AgentWorkLoop).Length - 1);
    }

    /// <summary>
    /// 子代理的段序必须与主代理<b>同构</b>：路径事实排在所有纪律之前。
    ///
    /// 这条曾经不成立，而两边的说法是直接矛盾的——主代理侧注释写着
    /// 「工作目录排在最前，后面每一段纪律都以路径怎么写为前提」，
    /// 子代理侧却把工作目录放在整段 shell 纪律<b>之后</b>。
    /// 两处各手写一套 <c>StringBuilder</c> 编排时，没有任何东西拦得住这种漂移。
    /// </summary>
    [Fact]
    public void SubAgentInstructions_PutPathFactsBeforeDisciplines()
    {
        string instructions = SubAgentAssembly.BuildSubAgentOptions(
                NewInput(mode: EAgentPermissionMode.FullAuto) with { ShellTool = StubShellTool() })!
            .ChatOptions!.Instructions!;

        int workingDirectory = instructions.IndexOf(AgentPromptHeadings.WorkingDirectory("##"),
            StringComparison.Ordinal);
        Assert.True(workingDirectory >= 0, "子代理必须有工作目录段");

        foreach (string section in new[] { AgentPromptHeadings.FileOperations, AgentPromptHeadings.Shell })
        {
            int at = instructions.IndexOf(section, StringComparison.Ordinal);
            Assert.True(at > workingDirectory, $"{section} 跑到了工作目录段之前");
        }
    }

    /// <summary>
    /// 探索档<b>只读</b>，所以它的提示词里不许出现写工具。
    ///
    /// 这条曾经不成立，而且缺口是双向的：探索档连一句文件纪律都拿不到
    /// （上下文卫生、contextLines、limit=-1 全都没有），而通用档虽有 `Edit`/`Write`
    /// 却同样拿不到修改纪律——它收到的 shell 纪律里反倒指名了 `Edit`。
    /// 读、写两半拆开之后，两档各拿该拿的那半。
    /// </summary>
    [Fact]
    public void ExplorerSubAgent_GetsReadDisciplineButNotWriteDiscipline()
    {
        string instructions = SubAgentAssembly.BuildSubAgentOptions(
                NewInput(mode: EAgentPermissionMode.FullAuto) with
                {
                    SubAgentProfile = SubAgentProfile.Explorer,
                })!
            .ChatOptions!.Instructions!;

        Assert.Contains(AgentPromptHeadings.FileOperations, instructions); //读那一半必须在
        Assert.DoesNotContain(AgentPromptHeadings.FileModifications, instructions);
        Assert.DoesNotContain("`Edit`", instructions);
        Assert.DoesNotContain("`Write`", instructions);
        Assert.DoesNotContain(AgentPromptHeadings.WebAccess, instructions); //不挂联网,联网纪律段不得出现
    }

    /// <summary>
    /// 反过来的一半：能改东西的子代理<b>必须</b>拿到修改纪律。
    ///
    /// 没有这条，<see cref="SubAgentInstructions_OnlyNameToolsThatExist"/> 会空转——
    /// 写侧段落整个缺席时，「指名的工具都存在」当然成立，而缺席正是从前的缺陷本身。
    /// </summary>
    [Fact]
    public void MutatingSubAgent_GetsWriteDiscipline()
    {
        string instructions = SubAgentAssembly.BuildSubAgentOptions(
            NewInput(mode: EAgentPermissionMode.FullAuto))!.ChatOptions!.Instructions!;

        Assert.Contains(AgentPromptHeadings.FileModifications, instructions);
        Assert.Contains(AgentToolPrompts.FileWriteDefault, instructions);
    }

    private static AITool StubShellTool() => AIFunctionFactory.Create((string command) => command,
        new AIFunctionFactoryOptions { Name = CharacterRunnerFactory.ShellToolName });

    /// <summary>探索档不认草稿目录：只读的它本来也写不进去</summary>
    [Fact]
    public void SubAgentInstructions_OmitOutputRoom_ForExplorer()
    {
        string instructions = SubAgentAssembly.BuildSubAgentOptions(
                NewInput(mode: EAgentPermissionMode.FullAuto) with
                {
                    SubAgentProfile = SubAgentProfile.Explorer, OutputFolderName = "ws-seg/12345678",
                })!
            .ChatOptions!.Instructions!;

        Assert.DoesNotContain(AgentPromptHeadings.OutputRoom("#"), instructions);
    }

    /// <summary>
    /// 工作区说明超限时按行截断 + 指路指针,不再全文重发。AGENTS.md 是固定开销里最大的一块
    /// (本仓 4469 字,过半),而规范/协作口径并不是每轮都要重读——模型手里有文件工具,
    /// 提交/规范事项前按指针自己去读全文。
    /// </summary>
    [Fact]
    public void WorkspaceInstructions_TruncateBeyondTheLimit_WithAPointer()
    {
        string head = "line1\nline2\n";
        string tail = new string('x', AgentInstructionsComposer.MaxWorkspaceInstructionsChars);
        string section = AgentInstructionsComposer.WorkspaceSection(head + tail);

        Assert.Contains("line1", section);
        Assert.Contains("AGENTS.md", section); //指针在,模型找得到全文
        Assert.DoesNotContain(tail, section);
    }

    /// <summary>限内短文本原样返回:截断语义的调用侧测试见 SubAgentInstructions_InheritWorkspaceInstructions</summary>
    [Fact]
    public void WorkspaceInstructions_ShortTextPassesThrough()
    {
        const string rule = "Always use absolute paths in this repo.";
        Assert.Equal($"{AgentPromptHeadings.Workspace}\n{rule}",
            AgentInstructionsComposer.WorkspaceSection(rule));
    }

    /// <summary>截断点落在行边界上,不把一行拦腰砍断</summary>
    [Fact]
    public void WorkspaceInstructions_CutFallsOnALineBoundary()
    {
        string head = string.Join('\n', Enumerable.Range(0, 100).Select(i => $"rule-{i}"));
        string section = AgentInstructionsComposer.TruncateWorkspaceInstructions(
            head + "\n" + new string('y', AgentInstructionsComposer.MaxWorkspaceInstructionsChars));

        Assert.Contains("rule-99", section);
        Assert.DoesNotContain("rule-99\nrule-99", section); //行完整,无半截
        Assert.DoesNotContain(new string('y', 100), section);
    }

    /// <summary>
    /// 子代理侧与主代理统一走指针:超长正文不进系统提示。
    /// 截断版实现保留在 AgentInstructionsComposer.WorkspaceSection,供"弱模型没读就编"的 case 切回,
    /// 其纯函数语义由上方 WorkspaceInstructions_* 一组测试钉住。
    /// </summary>
    [Fact]
    public void SubAgentWorkspaceSection_FollowsPointerLikeMainAgent()
    {
        string body = new string('z', AgentInstructionsComposer.MaxWorkspaceInstructionsChars) + "\nTAIL-MARKER";
        string instructions = SubAgentAssembly
            .BuildSubAgentOptions(NewInput(workspaceInstructions: body))!.ChatOptions!.Instructions!;

        Assert.Contains("动手前先读一遍全文", instructions);
        Assert.DoesNotContain(body[..100], instructions); //正文整段不进系统提示
    }

    /// <summary>指路指针本身够短:它才是每轮重发的那部分</summary>
    [Fact]
    public void WorkspacePointerSection_StaysShort()
    {
        string section = AgentInstructionsComposer.WorkspacePointerSection();

        Assert.Contains(AgentPromptHeadings.Workspace, section);
        Assert.True(section.Length < 300, $"指针膨胀到 {section.Length} 字,失去了全摘的意义");
    }

    /// <summary>
    /// 报告只取<b>最后一次工具调用之后</b>的正文。框架默认工作循环要求 agent
    /// 在工具调用之间解释进展，于是全程正文里绝大部分是「我接下来去看 X」的旁白；
    /// 全拼起来交给主代理等于把子代理的思考过程塞回主上下文——正是委派要避免的那件事。
    /// </summary>
    [Fact]
    public void Report_TakesOnlyTheTextAfterTheLastToolCall()
    {
        SubAgentTool.ReportAccumulator accumulator = new();
        accumulator.Add(new TextContent("Let me look at the config first."));
        accumulator.Add(new FunctionCallContent("c1", "Read", new Dictionary<string, object?>()));
        accumulator.Add(new TextContent("Found it. Now checking the tests."));
        accumulator.Add(new FunctionCallContent("c2", "Grep", new Dictionary<string, object?>()));
        accumulator.Add(new TextContent("Conclusion: the flag defaults to true."));

        string report = accumulator.Build(timedOut: false);

        Assert.Equal("Conclusion: the flag defaults to true.", report);
        Assert.DoesNotContain("Let me look", report);
        Assert.DoesNotContain("Now checking", report);
    }

    /// <summary>思考段属过程，永远不进报告（否则主上下文里会多出一份子代理的内心戏）。</summary>
    [Fact]
    public void Report_ExcludesReasoning()
    {
        SubAgentTool.ReportAccumulator accumulator = new();
        accumulator.Add(new TextReasoningContent("Hmm, maybe it is in Configs?"));
        accumulator.Add(new TextContent("The flag lives in AgentSettingConfig."));

        Assert.Equal("The flag lives in AgentSettingConfig.", accumulator.Build(timedOut: false));
    }

    /// <summary>
    /// 收尾总结缺失时（轮次到顶/超时）退回全程旁白，但必须明确标注那不是结论——
    /// 不标注的话主代理会把子代理的中间猜测当成它的判断。
    /// </summary>
    [Fact]
    public void Report_FallsBackToCommentary_AndSaysSo()
    {
        SubAgentTool.ReportAccumulator accumulator = new();
        accumulator.Add(new TextContent("Checking the first file."));
        accumulator.Add(new FunctionCallContent("c1", "Read", new Dictionary<string, object?>()));

        string report = accumulator.Build(timedOut: false);

        Assert.Contains("not a conclusion", report);
        Assert.Contains("Checking the first file.", report);
    }

    [Fact]
    public void Report_NotesTheTimeout()
    {
        SubAgentTool.ReportAccumulator accumulator = new();
        accumulator.Add(new TextContent("Partial findings."));

        string report = accumulator.Build(timedOut: true);

        Assert.Contains("Partial findings.", report);
        Assert.Contains("time limit", report);
    }

    [Fact]
    public void SubAgent_NotMounted_WhenAllCapabilitiesDisabled()
    {
        AgentToolConfig config = new()
        {
            EnableFileAccess = false,
            EnableWebSearch = false,
            EnableVisionTool = false,
        };

        Assert.Null(SubAgentAssembly.BuildSubAgentOptions(NewInput(config)));
    }
}

/// <summary>
/// 不变量之三：<b>装配快照的相等性语义</b>。
/// 相等 = 不重建;任一装配输入变化 = 重建;
/// 角色扮演档对 agent 侧配置免疫(工具类输入归零)。
/// </summary>
public class AssemblySnapshotTests
{
    /// <summary>
    /// 快照与装配现在<b>同一个入参</b>（profile），而 profile 只由这一个方法从会话构造。
    /// 这条测试守的是那道接缝：漏搬一个字段，症状是「改了设置不重建 agent」——
    /// 实机表现为改完权限档/工作目录不生效，而且极难归因。
    /// </summary>
    [Theory]
    [InlineData("workspace")]
    [InlineData("permission")]
    [InlineData("preauth")]
    [InlineData("params")]
    public void FactsFromAProfile_ReactToEverySessionFieldTheyDependOn(string dimension)
    {
        AgentAssemblyFacts baseline = AgentAssemblyFacts.Capture(
            AgentBuildProfile.FromSession(NewSession()));

        ChatSession changed = NewSession();
        switch (dimension)
        {
            case "workspace": changed.WorkspacePath = "/other"; break;
            case "permission": changed.PermissionModeIndex = 2; break;
            case "preauth": changed.PreAuthorizedShellPatterns = ["git status*"]; break;
            //会话参数经模板渲染进系统提示,故要用一个真的引用了它的模板
            case "params": changed.CustomParams["tone"] = "curt"; break;
        }

        Assert.NotEqual(baseline, AgentAssemblyFacts.Capture(AgentBuildProfile.FromSession(changed)));
    }

    /// <summary>
    /// ADR 0026:产出房间名只认 id8,改标题既不搬目录也不换目录,不该触发重建。
    /// 旧布局「标题进目录名」会在这里 <b>应当相等</b>——那条用例已随新布局删除。
    /// </summary>
    [Fact]
    public void FactsFromAProfile_AreStable_WhenOnlyTitleChanges()
    {
        ChatSession changed = NewSession();
        changed.Title = "另一个标题";

        Assert.Equal(
            AgentAssemblyFacts.Capture(AgentBuildProfile.FromSession(NewSession())),
            AgentAssemblyFacts.Capture(AgentBuildProfile.FromSession(changed)));
    }

    [Fact]
    public void FactsFromAProfile_AreStable_WhenNothingChanged()
    {
        Assert.Equal(
            AgentAssemblyFacts.Capture(AgentBuildProfile.FromSession(NewSession())),
            AgentAssemblyFacts.Capture(AgentBuildProfile.FromSession(NewSession())));
    }

    /// 带 CharacterData 的构造:让会话当场就位,不去问全局角色库(那有并发初始化隐患)
    private static ChatSession NewSession()
    {
        CharacterData character = NewAgentCharacter();
        character.Template = "语气：{{$tone}}"; //会话参数只有被模板引用才会进系统提示
        ChatSession session = new("t", character)
        {
            IsTransient = true,
            WorkspacePath = "/ws",
            PermissionModeIndex = 1,
            // 会话 id 写死:它进快照(产出房间名按会话分,见 AgentOutputLayout),
            // 每次新建都换一个的话,「改了某个字段就该重建」那几条会因为 id 不同而<b>假绿</b>
            SessionId = "0123456789abcdef0123456789abcdef",
        };
        session.CustomParams["tone"] = "neutral";
        return session;
    }

    private static CharacterData NewAgentCharacter(AgentToolConfig? tools = null)
    {
        return new CharacterData
        {
            CharacterId = "agent", IsAgent = true, Tools = tools ?? new AgentToolConfig(),
        };
    }

    [Fact]
    public void SameInputs_ProduceEqualSnapshots()
    {
        AgentAssemblyFacts first = AgentAssemblyFacts.Capture(NewAgentCharacter(), "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, mcpRevision: 1);
        AgentAssemblyFacts second = AgentAssemblyFacts.Capture(NewAgentCharacter(), "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, mcpRevision: 1);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("shell")]
    [InlineData("mcp")]
    [InlineData("permission")]
    [InlineData("workspace")]
    [InlineData("preauth")]
    [InlineData("instructions")]
    [InlineData("todolist")]
    [InlineData("agentmode")]
    [InlineData("vision-model")]
    [InlineData("subagent")]
    [InlineData("skills")]
    [InlineData("mcp-servers")]
    public void ChangedInput_ProducesDifferentSnapshot(string dimension)
    {
        AgentAssemblyFacts baseline = AgentAssemblyFacts.Capture(NewAgentCharacter(), "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, mcpRevision: 1);

        AgentToolConfig changedConfig = new();
        string instructions = "prompt";
        string? workspace = "/ws";
        EAgentPermissionMode permission = EAgentPermissionMode.AutoEdit;
        IReadOnlyList<string>? preAuthorized = null;
        int mcpRevision = 1;
        bool modelSupportsVision = false;
        // 布尔维度一律翻转默认值,不写死 true/false——默认值调整不该让测试失效
        switch (dimension)
        {
            case "shell": changedConfig.EnableShellExecution = !changedConfig.EnableShellExecution; break;
            case "mcp": mcpRevision = 2; break;
            case "permission": permission = EAgentPermissionMode.FullAuto; break;
            case "workspace": workspace = "/other"; break;
            case "preauth": preAuthorized = ["git status*"]; break;
            case "instructions": instructions = "edited prompt"; break; //角色卡/会话参数编辑经此显形
            case "todolist": changedConfig.EnableTodoList = !changedConfig.EnableTodoList; break;
            case "agentmode": changedConfig.EnableAgentMode = !changedConfig.EnableAgentMode; break;
            case "vision-model": modelSupportsVision = true; break; //视觉↔非视觉模型切换触发重建
            case "subagent": changedConfig.EnableSubAgent = !changedConfig.EnableSubAgent; break;
            case "skills": changedConfig.DisabledSkills.Add("some-skill"); break;
            //改完 MCP 名单不重建的话,回来仍按旧名单挂工具——与子智能体名单同一类坑
            case "mcp-servers": changedConfig.DisabledMcpServers.Add("some-server"); break;
        }

        AgentAssemblyFacts changed = AgentAssemblyFacts.Capture(NewAgentCharacter(changedConfig),
            instructions, workspace, permission, preAuthorized, mcpRevision,
            modelSupportsVision: modelSupportsVision);

        Assert.NotEqual(baseline, changed);
    }

    private static CharacterData NewSubAgent(string id, string name, string description = "")
    {
        return new CharacterData
        {
            CharacterId = id, IsAgent = true, CharacterName = name, Description = description,
        };
    }

    /// <summary>
    /// 子智能体名单是装配输入。名单连同各自的名字与描述会固化进花名册，
    /// 不入快照的话：不关会话改完名单，回来派活仍按旧名单走
    /// </summary>
    [Fact]
    public void ChangedSubAgentRoster_ProducesDifferentSnapshot()
    {
        AgentAssemblyFacts baseline = AgentAssemblyFacts.Capture(NewAgentCharacter(), "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, mcpRevision: 1,
            mountedAgents: [NewSubAgent("helper", "Helper")]);
        AgentAssemblyFacts changed = AgentAssemblyFacts.Capture(NewAgentCharacter(), "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, mcpRevision: 1,
            mountedAgents: [NewSubAgent("helper", "Helper"), NewSubAgent("writer", "Writer")]);

        Assert.NotEqual(baseline, changed);
    }

    [Theory]
    [InlineData("Renamed", "")]
    [InlineData("Helper", "改过的描述")]
    public void RenamedOrRedescribedSubAgent_ProducesDifferentSnapshot(string name, string description)
    {
        //花名册给模型看的就是名字与描述,改了它们模型也该重新看见
        AgentAssemblyFacts baseline = AgentAssemblyFacts.Capture(NewAgentCharacter(), "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, mcpRevision: 1,
            mountedAgents: [NewSubAgent("helper", "Helper")]);
        AgentAssemblyFacts changed = AgentAssemblyFacts.Capture(NewAgentCharacter(), "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, mcpRevision: 1,
            mountedAgents: [NewSubAgent("helper", name, description)]);

        Assert.NotEqual(baseline, changed);
    }

    /// <summary>
    /// 关掉子代理工具就不装花名册，此时名单怎么改都不该重建
    /// </summary>
    [Fact]
    public void SubAgentRoster_IsIgnored_WhenTheToolIsOff()
    {
        AgentToolConfig off = new() { EnableSubAgent = false };
        AgentAssemblyFacts first = AgentAssemblyFacts.Capture(NewAgentCharacter(off), "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, mcpRevision: 1,
            mountedAgents: [NewSubAgent("helper", "Helper")]);
        AgentAssemblyFacts second = AgentAssemblyFacts.Capture(NewAgentCharacter(off), "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, mcpRevision: 1,
            mountedAgents: [NewSubAgent("writer", "Writer")]);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// 普通角色对能力配置免疫：它们本来就不装工具，能力配置怎么改都不该让它们重建装配。
    /// </summary>
    [Fact]
    public void PromptOnlySnapshot_IsImmuneToToolConfigChanges()
    {
        AgentToolConfig configA = new();
        AgentToolConfig configB = new()
        {
            EnableFileAccess = false,
            EnableShellExecution = false,
            EnableWebSearch = false,
            EnableScheduledTasks = false,
            EnableVisionTool = false,
            EnableKnowledgeSearchTool = false,
            EnableSubAgent = false,
        };

        CharacterData a = new() { CharacterId = "rp", IsAgent = false, Tools = configA };
        CharacterData b = new() { CharacterId = "rp", IsAgent = false, Tools = configB };

        AgentAssemblyFacts first = AgentAssemblyFacts.Capture(a, "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, mcpRevision: 1);
        AgentAssemblyFacts second = AgentAssemblyFacts.Capture(b, "prompt", "/other",
            EAgentPermissionMode.FullAuto, ["*"], mcpRevision: 99);

        Assert.Equal(first, second);
    }
}
