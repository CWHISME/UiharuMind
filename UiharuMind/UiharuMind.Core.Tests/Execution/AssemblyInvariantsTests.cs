using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
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
/// 扮演与工具人两档都只渲染提示词：框架的每一项能力都必须关掉、HarnessInstructions 必须为空——
/// 任何一项漏关都会向它们的上下文里悄悄注入内容,
/// 而这种污染在实机上几乎不可见(模型行为变化无法归因)。
/// </summary>
public class PromptOnlyZeroInjectionTests
{
    [Theory]
    [InlineData(ECharacterKind.Roleplay)]
    [InlineData(ECharacterKind.Tool)]
    public void BuildPromptOnlyOptions_DisablesEveryFrameworkCapability(ECharacterKind kind)
    {
        CharacterData character = new() { CharacterId = "rp", Kind = kind };
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
        Assert.Null(options.ChatOptions!.Tools); //角色扮演不装配任何工具
    }

    /// <summary>
    /// 压缩是「零注入」的唯一例外，且这个例外必须是显式给的：
    /// 它只做排除与工具结果折叠，不往上下文里添加内容，因此不违背零注入；
    /// 但没传策略时必须仍是关的，免得哪天框架给它加了默认行为就悄悄生效（ADR 0006）。
    /// </summary>
    [Fact]
    public void BuildPromptOnlyOptions_CompactionIsOptInOnly()
    {
        CharacterData character = new() { CharacterId = "rp", Kind = ECharacterKind.Roleplay };

        HarnessAgentOptions without = AgentOptionsFactory.BuildPromptOnlyOptions(
            character, new StubHistoryProvider(), [], new ChatOptions());
        Assert.True(without.DisableCompaction);
        Assert.Null(without.CompactionStrategy);

        CompactionStrategy strategy = HistoryCompaction.Create(() => 128_000);
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
/// 顺序是 角色人格(含工作循环) → 用户卡 → 对话模板 → 工具纪律与工作目录 → 工作区规矩(见 ADR 0005)。
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
        int disciplines = instructions.IndexOf("## File operations", StringComparison.Ordinal);
        Assert.True(persona >= 0, "角色人格丢了");
        Assert.True(disciplines > persona, "工具纪律必须排在角色人格之后");
        Assert.DoesNotContain("helpful AI assistant", instructions); //身份只由角色说
    }

    /// <summary>
    /// 工具纪律段挂在自己的 <c># Tools</c> 父标题之下。
    ///
    /// 这不是排版洁癖：角色段（agent 档默认角色卡）以 <c># Work loop</c> 起头，
    /// 工具纪律若像从前那样直接从 <c>## Working directory</c> 开始，
    /// 按 markdown 结构读就整个成了「工作循环」的子节——层级说了一件与事实不符的事。
    /// </summary>
    [Fact]
    public void ToolDisciplines_LiveUnderTheirOwnTopLevelHeading()
    {
        HarnessAgentOptions options = BuildAgentOptions("/tmp/uiharu-agent-test");
        string instructions = options.ChatOptions?.Instructions ?? string.Empty;

        int tools = instructions.IndexOf("# Tools", StringComparison.Ordinal);
        Assert.True(tools >= 0, "工具纪律段缺少父标题");
        //每个二级段都在父标题之后,没有一个跑到外面去
        foreach (string section in new[] { "## Working directory", "## File operations" })
        {
            int at = instructions.IndexOf(section, StringComparison.Ordinal);
            Assert.True(at > tools, $"{section} 跑到了 # Tools 之外");
        }
    }

    /// <summary>
    /// 一项工具纪律都没有时整段不出现：只挂一个空的 <c># Tools</c> 标题是纯噪声
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
        };

        HarnessAgentOptions options = BuildAgentOptions(string.Empty, nothing);

        Assert.DoesNotContain("# Tools", options.ChatOptions?.Instructions ?? string.Empty);
    }

    /// <summary>
    /// 工作区规矩排在我们这段的最尾(框架 provider 段仍在其后,那不由我们控制)。
    /// 它是"这个项目的特殊规矩"，该压在通用纪律之后。
    /// </summary>
    [Fact]
    public void AgentInstructions_PutWorkspaceRulesLast()
    {
        HarnessAgentOptions options = BuildAgentOptions("/tmp/uiharu-agent-test",
            workspaceInstructions: "never touch the vendor folder");
        string instructions = options.ChatOptions?.Instructions ?? string.Empty;

        Assert.True(instructions.IndexOf("never touch the vendor folder", StringComparison.Ordinal) >
                    instructions.IndexOf("## File operations", StringComparison.Ordinal),
            "工作区规矩必须排在工具纪律之后");
    }

    /// <summary>
    /// 工作循环必须真的在某处：它搬进了内置智能体的存档，
    /// 若哪天被顺手删掉，模型侧就只剩工具纪律而没有工作方法，同样难归因。
    /// </summary>
    [Fact]
    public void WorkspaceAgent_CarriesTheWorkLoopInItsPrompt()
    {
        DefaultCharacterManager.Instance.OnInitialize();
        CharacterData agent = DefaultCharacterManager.Instance
            .GetCharacterData(DefaultCharacter.WorkspaceAgent);

        Assert.Contains(AgentToolPrompts.AgentWorkLoop, agent.Template);
    }

    /// <summary>
    /// 主 agent 也必须被告知工作目录的绝对路径。同一个坑：路径只被拿去构造工具，
    /// 从没进过提示词，模型只能自己编。
    /// </summary>
    [Fact]
    public void AgentInstructions_StateTheWorkingDirectory()
    {
        const string workingDirectory = "/tmp/uiharu-agent-test";

        HarnessAgentOptions options = BuildAgentOptions(workingDirectory);
        string instructions = options.ChatOptions?.Instructions ?? string.Empty;

        Assert.Contains(workingDirectory, instructions);
        Assert.Contains(AgentToolPrompts.FileAccessDefault, instructions); //纪律段没吃掉工作目录段
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

        Assert.DoesNotContain("## File operations", instructions);
        Assert.DoesNotContain(AgentToolPrompts.SubAgentDefault, instructions);
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

    private static HarnessAgentOptions BuildAgentOptions(string workingDirectory,
        AgentToolConfig? tools = null, string workspaceInstructions = "", McpToolSet? mcp = null)
    {
        return BuildAgentOptions(workingDirectory, out _, tools, workspaceInstructions, mcp);
    }

    private static HarnessAgentOptions BuildAgentOptions(string workingDirectory,
        out IReadOnlyList<AgentPromptSegment> segments, AgentToolConfig? tools = null,
        string workspaceInstructions = "", McpToolSet? mcp = null)
    {
        CharacterData character = new()
        {
            CharacterId = "agent", Kind = ECharacterKind.Agent, Tools = tools ?? new AgentToolConfig(),
        };
        string skillsDir = Path.Combine(Path.GetTempPath(), "uiharu-skills-test");
        Directory.CreateDirectory(skillsDir);

        // 角色段由调用方先填好(实机里是 CharacterPromptBuilder 的产物)
        ChatOptions chatOptions = new() { Instructions = PersonaMarker };

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
            Mcp = mcp ?? McpToolSet.Empty,
        };

        return AgentOptionsFactory.BuildAgentOptions(plan, new StubHistoryProvider(), [], chatOptions,
            out segments);
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
/// 子代理在主 agent 的一次工具调用内部无头运行，<b>没有审批通道</b>——现有审批往返靠
/// 「结束本轮再带回应重跑」，而此刻主 agent 正同步阻塞在工具里，做不到。
/// 所以它的权限只能继承主 agent 的档位：完全自动档下一律放行，它才可能真的写成东西；
/// 其余档位下写/shell 必然要问用户，给了等于给一把静默失效的工具（框架遇到
/// <c>ApprovalRequiredAIFunction</c> 不执行它，只产出一条无人回应的审批请求，
/// 症状是子代理什么都没干却正常结束——最难查的那一类）。
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

    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    public void SubAgentTools_AreReadOnly_BelowFullAuto(EAgentPermissionMode mode)
    {
        List<string> names = ToolNamesOf(SubAgentAssembly.BuildSubAgentOptions(NewInput(mode: mode)));

        Assert.Contains(FileToolNames.Read, names);
        Assert.Contains(WebSearchTool.ToolName, names);
        // 名单取自工具侧的那一份,不在测试里重抄一遍:漏抄一个新增的写工具,
        // 这条不变量就会在不报错的情况下失效
        string[] mutating = [..FileToolNames.Mutating, CharacterRunnerFactory.ShellToolName];
        Assert.DoesNotContain(names, name => mutating.Contains(name));
    }

    /// <summary>
    /// 完全自动档下才给可变更工具——那一档 <see cref="ApprovalModeMapper"/> 会加
    /// 全放行规则，子代理不会卡在没人回应的审批上。
    /// </summary>
    [Fact]
    public void SubAgentTools_IncludeWriteTools_UnderFullAuto()
    {
        List<string> names = ToolNamesOf(
            SubAgentAssembly.BuildSubAgentOptions(NewInput(mode: EAgentPermissionMode.FullAuto)));

        Assert.Contains(FileToolNames.Write, names);
        Assert.Contains(FileToolNames.Edit, names);
    }

    /// <summary>
    /// 审批规则必须与主 agent 同源：档位语义只能有一处定义（<see cref="ApprovalModeMapper"/>），
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
    /// 子代理工具集里绝不能有子代理工具自身：那是无限递归，
    /// 本地模型会被层层嵌套的子代理占满。连完全自动档也不例外。
    /// </summary>
    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public void SubAgentTools_DoNotIncludeSubAgentItself(EAgentPermissionMode mode)
    {
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
    /// 子代理与主 agent 同一口径：工作循环归它自己那份指令，harness 段为空。
    /// 框架默认那段的身份句会和子代理指令开头的 "# Role" 抢身份(见 ADR 0004)。
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
    /// 点名的子智能体，人格排在那套"你是子代理"的边界与体例之前(与主 agent 同一口径,见 ADR 0005)。
    /// </summary>
    [Fact]
    public void NamedSubAgent_PutsItsPersonaFirst()
    {
        HarnessAgentOptions? options = SubAgentAssembly.BuildSubAgentOptions(
            NewInput() with { Persona = "I am the research specialist", Name = "Researcher" });

        Assert.NotNull(options);
        string instructions = options!.ChatOptions?.Instructions ?? string.Empty;
        Assert.Equal("Researcher", options.Name);
        Assert.True(instructions.IndexOf("I am the research specialist", StringComparison.Ordinal) <
                    instructions.IndexOf("# Role", StringComparison.Ordinal),
            "子智能体的人格必须排在子代理身份段之前");
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
        HarnessAgentOptions? options = SubAgentAssembly.BuildSubAgentOptions(NewInput(mode: mode));

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
    /// 子代理必须拿到与主 agent 同一份工作区规矩：它干的正是探查工作区的活，
    /// 却会是全场唯一不知道工作区规矩的人。本仓 AGENTS.md 头一条就是
    /// 「有四层同名目录，用绝对路径别数相对层数」——不知道这条的子代理会直接踩进去。
    /// </summary>
    [Fact]
    public void SubAgentInstructions_InheritWorkspaceInstructions()
    {
        const string workspaceRule = "Always use absolute paths in this repo.";
        string instructions = SubAgentAssembly
            .BuildSubAgentOptions(NewInput(workspaceInstructions: workspaceRule))!.ChatOptions!.Instructions!;

        Assert.Contains(workspaceRule, instructions);
        Assert.EndsWith(workspaceRule, instructions); //拼在最尾,权重最高
    }

    /// <summary>
    /// 报告只取<b>最后一次工具调用之后</b>的正文。框架默认工作循环要求 agent
    /// 在工具调用之间解释进展，于是全程正文里绝大部分是「我接下来去看 X」的旁白；
    /// 全拼起来交给主 agent 等于把子代理的思考过程塞回主上下文——正是委派要避免的那件事。
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
    /// 不标注的话主 agent 会把子代理的中间猜测当成它的判断。
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
        };
        session.CustomParams["tone"] = "neutral";
        return session;
    }

    private static CharacterData NewAgentCharacter(AgentToolConfig? tools = null)
    {
        return new CharacterData
        {
            CharacterId = "agent", Kind = ECharacterKind.Agent, Tools = tools ?? new AgentToolConfig(),
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
            CharacterId = id, Kind = ECharacterKind.Agent, CharacterName = name, Description = description,
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
    /// 非智能体档对能力配置免疫：它们本来就不装工具，能力配置怎么改都不该让它们重建装配。
    /// </summary>
    [Theory]
    [InlineData(ECharacterKind.Roleplay)]
    [InlineData(ECharacterKind.Tool)]
    public void PromptOnlySnapshot_IsImmuneToToolConfigChanges(ECharacterKind kind)
    {
        AgentToolConfig configA = new();
        AgentToolConfig configB = new()
        {
            EnableFileAccess = false,
            EnableShellExecution = false,
            EnableWebSearch = false,
            EnableFileMemory = false,
            EnableScheduledTasks = false,
            EnableVisionTool = false,
            EnableKnowledgeSearchTool = false,
            EnableSubAgent = false,
        };

        CharacterData a = new() { CharacterId = "rp", Kind = kind, Tools = configA };
        CharacterData b = new() { CharacterId = "rp", Kind = kind, Tools = configB };

        AgentAssemblyFacts first = AgentAssemblyFacts.Capture(a, "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, mcpRevision: 1);
        AgentAssemblyFacts second = AgentAssemblyFacts.Capture(b, "prompt", "/other",
            EAgentPermissionMode.FullAuto, ["*"], mcpRevision: 99);

        Assert.Equal(first, second);
    }
}
