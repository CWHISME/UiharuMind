using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Scheduler;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.WebTools;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死装配的三个不变量之一：<b>角色扮演档零注入</b>。
/// 框架的每一项能力都必须关掉、HarnessInstructions 必须为空——
/// 任何一项漏关都会向角色扮演的上下文里悄悄注入内容,
/// 而这种污染在实机上几乎不可见(模型行为变化无法归因)。
/// </summary>
public class RoleplayZeroInjectionTests
{
    [Fact]
    public void BuildRoleplayOptions_DisablesEveryFrameworkCapability()
    {
        CharacterData character = new() { CharacterId = "rp", Kind = ECharacterKind.Roleplay };
        ChatOptions chatOptions = new();

        HarnessAgentOptions options = CharacterRunnerFactory.BuildRoleplayOptions(
            character, new StubHistoryProvider(), [], chatOptions);

        Assert.Equal(string.Empty, options.HarnessInstructions);
        Assert.True(options.DisableWebSearch);
        Assert.Null(options.FileAccessStore); //1.16:框架文件工具只随 FileAccessStore 出现
        Assert.True(options.DisableFileMemory);
        Assert.True(options.DisableTodoProvider);
        Assert.True(options.DisableAgentModeProvider);
        Assert.True(options.DisableAgentSkillsProvider);
        Assert.True(options.DisableCompaction);
        Assert.True(options.DisableToolAutoApproval);
        Assert.True(options.DisableOpenTelemetry);
        Assert.Null(options.ChatOptions!.Tools); //角色扮演不装配任何工具
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
}

/// <summary>
/// 不变量之五：<b>harness 指令以框架默认工作循环开头</b>。
/// 显式设置 HarnessInstructions 会顶掉框架默认(先想再做/边做边说/失败换路/收尾总结),
/// 那段恰是弱模型最依赖的部分——一旦拼接被删,症状是"笨模型不会用工具",实机难归因。
/// </summary>
public class HarnessInstructionsCompositionTests
{
    [Fact]
    public void AgentHarnessInstructions_StartWithFrameworkDefaults()
    {
        HarnessAgentOptions options = BuildAgentOptions("/tmp/uiharu-agent-test");

        Assert.StartsWith(HarnessAgent.DefaultInstructions, options.HarnessInstructions);
        Assert.Contains("## File operations", options.HarnessInstructions); //默认开关下纪律段仍在
    }

    /// <summary>
    /// 主 agent 也必须被告知工作目录的绝对路径。同一个坑：路径只被拿去构造工具，
    /// 从没进过提示词，模型只能自己编。这一段刻意<b>不可</b>被设置页覆盖——
    /// 它是事实而非建议，用户改写文件工具的纪律文案不该顺带删掉"根目录在哪"。
    /// </summary>
    [Fact]
    public void AgentHarnessInstructions_StateTheWorkingDirectory()
    {
        const string workingDirectory = "/tmp/uiharu-agent-test";
        AgentSettingConfig config = new() { FileAccessPrompt = "custom file prompt" };

        HarnessAgentOptions options = BuildAgentOptions(workingDirectory, config);

        Assert.Contains(workingDirectory, options.HarnessInstructions);
        Assert.Contains("custom file prompt", options.HarnessInstructions); //覆盖生效,但没吃掉工作目录段
    }

    private static HarnessAgentOptions BuildAgentOptions(string workingDirectory,
        AgentSettingConfig? config = null)
    {
        CharacterData character = new() { CharacterId = "agent", Kind = ECharacterKind.Agent };
        string skillsDir = Path.Combine(Path.GetTempPath(), "uiharu-skills-test");
        Directory.CreateDirectory(skillsDir);

        return CharacterRunnerFactory.BuildAgentOptions(character, config ?? new AgentSettingConfig(),
            new StubHistoryProvider(), [], new ChatOptions(),
            new AgentFileSkillsSource(skillsDir), fileMemoryStore: null,
            EAgentPermissionMode.AutoEdit, preAuthorizedShellPatterns: null,
            workingDirectory: workingDirectory);
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

    private static CharacterRunnerFactory.SubAgentAssemblyInput NewInput(
        AgentSettingConfig? config = null,
        EAgentPermissionMode mode = EAgentPermissionMode.AutoEdit,
        string workspaceInstructions = "")
    {
        return new CharacterRunnerFactory.SubAgentAssemblyInput
        {
            Config = config ?? new AgentSettingConfig(),
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
        List<string> names = ToolNamesOf(CharacterRunnerFactory.BuildSubAgentOptions(NewInput(mode: mode)));

        Assert.Contains(PermissiveFileAccessTools.ReadToolName, names);
        Assert.Contains(WebSearchTool.ToolName, names);
        string[] mutating =
        [
            PermissiveFileAccessTools.WriteToolName, PermissiveFileAccessTools.EditToolName,
            PermissiveFileAccessTools.ReplaceToolName, PermissiveFileAccessTools.DeleteToolName,
            CharacterRunnerFactory.ShellToolName,
        ];
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
            CharacterRunnerFactory.BuildSubAgentOptions(NewInput(mode: EAgentPermissionMode.FullAuto)));

        Assert.Contains(PermissiveFileAccessTools.WriteToolName, names);
        Assert.Contains(PermissiveFileAccessTools.EditToolName, names);
    }

    /// <summary>
    /// 审批规则必须与主 agent 同源：档位语义只能有一处定义（<see cref="ApprovalModeMapper"/>），
    /// 否则「完全自动」在主代理与子代理身上会渐渐变成两个意思。
    /// </summary>
    [Fact]
    public void SubAgent_ApprovalRules_ComeFromTheSameMapper()
    {
        HarnessAgentOptions? options =
            CharacterRunnerFactory.BuildSubAgentOptions(NewInput(mode: EAgentPermissionMode.FullAuto));

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
            ToolNamesOf(CharacterRunnerFactory.BuildSubAgentOptions(NewInput(mode: mode))));
    }

    /// <summary>
    /// 主代理特有的那批不下放给子代理：它拿的是一份任务书，不需要再自己装载指令
    /// （技能）、排定时任务，也没有会话可供检索记忆。
    /// </summary>
    [Fact]
    public void SubAgentTools_ExcludeMainAgentOnlyTools()
    {
        AgentSettingConfig config = new() { EnableKnowledgeSearchTool = true, EnableScheduledTasks = true };
        List<string> names = ToolNamesOf(CharacterRunnerFactory.BuildSubAgentOptions(
            NewInput(config, EAgentPermissionMode.FullAuto)));

        Assert.DoesNotContain(KnowledgeTool.ToolName, names);
        Assert.DoesNotContain(SchedulerTools.ToolName, names);

        HarnessAgentOptions? options = CharacterRunnerFactory.BuildSubAgentOptions(
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
        HarnessAgentOptions? options = CharacterRunnerFactory.BuildSubAgentOptions(NewInput());

        Assert.NotNull(options);
        Assert.Equal(SubAgentTool.MaxIterations, options!.MaximumIterationsPerRequest);
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
        HarnessAgentOptions? options = CharacterRunnerFactory.BuildSubAgentOptions(NewInput(mode: mode));

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
        string instructions = CharacterRunnerFactory
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
        string instructions = CharacterRunnerFactory
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
        AgentSettingConfig config = new()
        {
            EnableFileAccess = false,
            EnableWebSearch = false,
            EnableVisionTool = false,
        };

        Assert.Null(CharacterRunnerFactory.BuildSubAgentOptions(NewInput(config)));
    }
}

/// <summary>
/// 不变量之三：<b>装配快照的相等性语义</b>。
/// 相等 = 不重建;任一装配输入变化 = 重建;
/// 角色扮演档对 agent 侧配置免疫(工具类输入归零)。
/// </summary>
public class AssemblySnapshotTests
{
    private static CharacterData NewAgentCharacter()
    {
        return new CharacterData { CharacterId = "agent", Kind = ECharacterKind.Agent };
    }

    [Fact]
    public void SameInputs_ProduceEqualSnapshots()
    {
        AgentSettingConfig config = new();
        AgentAssemblySnapshot first = AgentAssemblySnapshot.Capture(NewAgentCharacter(), "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, config, mcpRevision: 1);
        AgentAssemblySnapshot second = AgentAssemblySnapshot.Capture(NewAgentCharacter(), "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, config, mcpRevision: 1);

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
    [InlineData("prompt-override")]
    [InlineData("subagent")]
    [InlineData("subagent-prompt-override")]
    public void ChangedInput_ProducesDifferentSnapshot(string dimension)
    {
        AgentSettingConfig config = new();
        AgentAssemblySnapshot baseline = AgentAssemblySnapshot.Capture(NewAgentCharacter(), "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, config, mcpRevision: 1);

        AgentSettingConfig changedConfig = new();
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
            case "prompt-override": changedConfig.VisionToolPrompt = "custom vision prompt"; break;
            case "subagent": changedConfig.EnableSubAgent = !changedConfig.EnableSubAgent; break;
            case "subagent-prompt-override": changedConfig.SubAgentPrompt = "custom subagent prompt"; break;
        }

        AgentAssemblySnapshot changed = AgentAssemblySnapshot.Capture(NewAgentCharacter(), instructions,
            workspace, permission, preAuthorized, changedConfig, mcpRevision,
            modelSupportsVision: modelSupportsVision);

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void RoleplaySnapshot_IsImmuneToAgentConfigChanges()
    {
        CharacterData roleplay = new() { CharacterId = "rp", Kind = ECharacterKind.Roleplay };
        AgentSettingConfig configA = new();
        AgentSettingConfig configB = new()
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

        AgentAssemblySnapshot first = AgentAssemblySnapshot.Capture(roleplay, "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, configA, mcpRevision: 1);
        AgentAssemblySnapshot second = AgentAssemblySnapshot.Capture(roleplay, "prompt", "/other",
            EAgentPermissionMode.FullAuto, ["*"], configB, mcpRevision: 99);

        Assert.Equal(first, second);
    }
}
