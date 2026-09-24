using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.AI.Execution.Tools.Skills;
using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 自建 load_skill(技能模型可见性总闸关闭时的框架替代)的行为:
/// 只加载广告列表内的技能,主动技能/未知技能一律 not found 并给出引导;
/// 正文与框架一致,文件技能额外附目录与取资源口径。
/// </summary>
public class LoadSkillToolTests
{
    private static AIFunction Tool(AgentSkillsSource source, bool hasFileTools = true, bool hasShell = true)
    {
        return (AIFunction)LoadSkillTool.Create(source, hasFileTools, hasShell);
    }

    private static async Task<string> Invoke(AIFunction tool, string skillName)
    {
        object? raw = await tool.InvokeAsync(new AIFunctionArguments { ["skillName"] = skillName },
            TestContext.Current.CancellationToken);
        return raw is JsonElement { ValueKind: JsonValueKind.String } element
            ? element.GetString() ?? string.Empty
            : raw?.ToString() ?? string.Empty;
    }

    private static AgentSkill Passive(string name) =>
        new AgentInlineSkill(name, $"description of {name}", $"{name} body");

    private static AgentSkill UserInvoked(string name) => new AgentInlineSkill(
        name, $"description of {name}", $"{name} body",
        metadata: new AdditionalPropertiesDictionary { [SkillCatalog.DisableModelInvocationKey] = "true" });

    [Fact]
    public async Task LoadsModelSelectableSkill_ReturnsBody()
    {
        AgentSkillsSource source = new AgentInMemorySkillsSource([Passive("tdd")]);
        string result = await Invoke(Tool(source), "tdd");

        Assert.Contains("tdd body", result);
    }

    [Fact]
    public async Task LoadsByName_IgnoringCase()
    {
        AgentSkillsSource source = new AgentInMemorySkillsSource([Passive("tdd")]);
        string result = await Invoke(Tool(source), "TDD");

        Assert.Contains("tdd body", result);
    }

    [Fact]
    public async Task UserInvokedSkill_IsNotLoadable_AndGivesGuidance()
    {
        // 与装配同款:过滤谓词即 SkillCatalog.IsAdvertised——主动技能与禁用技能一起被滤掉
        AgentSkillsSource source = new FilteringAgentSkillsSource(
            new AgentInMemorySkillsSource([Passive("tdd"), UserInvoked("implement")]),
            (skill, _) => SkillCatalog.IsAdvertised(skill, Array.Empty<string>()));

        string result = await Invoke(Tool(source), "implement");

        Assert.Contains("not found", result);
        Assert.Contains("disable-model-invocation", result);
        // 被动技能仍可加载:过滤没有误伤
        Assert.Contains("tdd body", await Invoke(Tool(source), "tdd"));
    }

    [Fact]
    public async Task UnknownSkill_ReturnsNotFound()
    {
        AgentSkillsSource source = new AgentInMemorySkillsSource([Passive("tdd")]);
        string result = await Invoke(Tool(source), "nope");

        Assert.Contains("not found", result);
    }

    [Fact]
    public async Task EmptyName_ReturnsError()
    {
        AgentSkillsSource source = new AgentInMemorySkillsSource([Passive("tdd")]);
        string result = await Invoke(Tool(source), " ");

        Assert.Contains("empty", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NullSource_ReturnsError()
    {
        string result = await Invoke(Tool(null!), "tdd");

        Assert.Contains("unavailable", result);
    }

    [Fact]
    public async Task FileSkill_AppendsDirectoryAndResourceHint()
    {
        string root = Path.Combine(Path.GetTempPath(), "uiharu-load-skill-test");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        try
        {
            string dir = Path.Combine(root, "tdd");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "SKILL.md"),
                "---\nname: tdd\ndescription: d\n---\n\ntdd body", TestContext.Current.CancellationToken);

            AgentSkillsSource source = new AgentFileSkillsSource(root);
            string result = await Invoke(Tool(source, hasFileTools: true, hasShell: true), "tdd");

            Assert.Contains("tdd body", result);
            Assert.Contains("Skill directory:", result);
            Assert.Contains("Use your file and shell tools", result);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 总闸关闭时,自建 load_skill 顶替框架 provider 挂进 ChatOptions.Tools,
    /// 主动技能正文里引用的被动技能(如 implement → tdd)才能被模型按名加载。
    /// </summary>
    [Fact]
    public async Task DisabledSkillsProvider_MountsOwnLoadSkillTool()
    {
        await using AgentHandle handle = AgentAssembler.Assemble(NewMainPlan(disableSkillsProvider: true));

        IList<AITool>? tools = handle.ChatOptions?.Tools;
        Assert.NotNull(tools);
        Assert.Contains(tools!, x => x.Name == LoadSkillTool.ToolName);
    }

    /// <summary>
    /// 总闸打开时框架 provider 挂着,它的 load_skill 不经过 ChatOptions.Tools;
    /// 自建那份不能再挂,否则模型侧出现两个同名工具。
    /// </summary>
    [Fact]
    public async Task EnabledSkillsProvider_DoesNotMountOwnLoadSkillTool()
    {
        await using AgentHandle handle = AgentAssembler.Assemble(NewMainPlan(disableSkillsProvider: false));

        IList<AITool>? tools = handle.ChatOptions?.Tools;
        Assert.DoesNotContain(tools ?? [], x => x.Name == LoadSkillTool.ToolName);
    }

    private static AgentAssemblyPlan NewMainPlan(bool disableSkillsProvider)
    {
        CharacterData character = new()
        {
            CharacterId = "agent",
            IsAgent = true,
            Tools = new AgentToolConfig { EnableShellExecution = false },
        };
        return new AgentAssemblyPlan
        {
            Profile = new AgentBuildProfile
            {
                Character = character,
                PermissionMode = EAgentPermissionMode.AutoEdit,
            },
            WorkingDirectory = TestWorkingDirectory,
            SkillsSource = new AgentFileSkillsSource(Path.Combine(Path.GetTempPath(), "uiharu-skills-test")),
            DisableSkillsProvider = disableSkillsProvider,
            Mcp = McpToolSet.Empty,
        };
    }

    private static readonly string TestWorkingDirectory =
        Path.Combine(Path.GetTempPath(), "uiharu-load-skill-assemble-test");
}
