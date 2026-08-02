using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Agent;
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

        HarnessAgentOptions options = AgentHost.BuildRoleplayOptions(
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
            AdditionalProperties = new AdditionalPropertiesDictionary { ["_attribution"] = "TodoProvider" },
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
        CharacterData character = new() { CharacterId = "agent", Kind = ECharacterKind.Agent };
        string skillsDir = Path.Combine(Path.GetTempPath(), "uiharu-skills-test");
        Directory.CreateDirectory(skillsDir);

        HarnessAgentOptions options = AgentHost.BuildAgentOptions(character, new AgentSettingConfig(),
            new StubHistoryProvider(), [], new ChatOptions(),
            new AgentFileSkillsSource(skillsDir), agentNotesStore: null,
            EAgentPermissionMode.AutoEdit, preAuthorizedShellPatterns: null);

        Assert.StartsWith(HarnessAgent.DefaultInstructions, options.HarnessInstructions);
        Assert.Contains("## File operations", options.HarnessInstructions); //默认开关下纪律段仍在
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
/// 不变量之四：<b>调研员子代理只读</b>。
/// 它在主 agent 的工具调用内部无头运行,没有审批通道——任何可变更工具混入都是越权。
/// </summary>
public class ResearcherReadOnlyTests
{
    [Fact]
    public void ResearcherTools_AreReadOnly()
    {
        string workingDirectory = Path.Combine(Path.GetTempPath(), "uiharu-researcher-test");
        HarnessAgentOptions? options = AgentHost.BuildResearcherOptions(new AgentSettingConfig(), workingDirectory);

        Assert.NotNull(options);
        List<string> names = options!.ChatOptions!.Tools!.OfType<AIFunction>().Select(x => x.Name).ToList();
        Assert.Contains("Read", names);
        Assert.Contains("WebSearch", names);
        string[] forbidden = ["Write", "Edit", "Replace", "Delete", AgentHost.ShellToolName];
        Assert.DoesNotContain(names, name => forbidden.Contains(name));
    }

    [Fact]
    public void Researcher_NotMounted_WhenAllReadCapabilitiesDisabled()
    {
        AgentSettingConfig config = new()
        {
            EnableFileAccess = false,
            EnableWebSearch = false,
            EnableVisionTool = false,
        };

        Assert.Null(AgentHost.BuildResearcherOptions(config, Path.GetTempPath()));
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
            EnableAgentNotes = false,
            EnableScheduledTasks = false,
            EnableVisionTool = false,
            EnableMemorySearchTool = false,
        };

        AgentAssemblySnapshot first = AgentAssemblySnapshot.Capture(roleplay, "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, configA, mcpRevision: 1);
        AgentAssemblySnapshot second = AgentAssemblySnapshot.Capture(roleplay, "prompt", "/other",
            EAgentPermissionMode.FullAuto, ["*"], configB, mcpRevision: 99);

        Assert.Equal(first, second);
    }
}
