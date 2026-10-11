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

        Assert.Contains("找不到技能", result);
        Assert.Contains("disable-model-invocation", result);
        // 被动技能仍可加载:过滤没有误伤
        Assert.Contains("tdd body", await Invoke(Tool(source), "tdd"));
    }

    [Fact]
    public async Task UnknownSkill_ReturnsNotFound()
    {
        AgentSkillsSource source = new AgentInMemorySkillsSource([Passive("tdd")]);
        string result = await Invoke(Tool(source), "nope");

        Assert.Contains("找不到技能", result);
    }

    [Fact]
    public async Task EmptyName_ReturnsError()
    {
        AgentSkillsSource source = new AgentInMemorySkillsSource([Passive("tdd")]);
        string result = await Invoke(Tool(source), " ");

        Assert.Contains("技能名不能为空", result);
    }

    [Fact]
    public async Task NullSource_ReturnsError()
    {
        string result = await Invoke(Tool(null!), "tdd");

        Assert.Contains("技能源不可用", result);
    }

    /// <summary>
    /// 有附加文件的技能:注入里带目录、资源绝对路径与工具提示,模型照着清单一步到位。
    /// </summary>
    [Fact]
    public async Task FileSkill_WithResources_AppendsDirectoryAndToolHint()
    {
        string root = Path.Combine(Path.GetTempPath(), "uiharu-load-skill-test-with-resources");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        try
        {
            string dir = Path.Combine(root, "tdd");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "SKILL.md"),
                "---\nname: tdd\ndescription: d\n---\n\ntdd body", TestContext.Current.CancellationToken);
            Directory.CreateDirectory(Path.Combine(dir, "refs"));
            await File.WriteAllTextAsync(Path.Combine(dir, "refs", "notes.md"), "# notes",
                TestContext.Current.CancellationToken);

            AgentSkillsSource source = new AgentFileSkillsSource(root);
            string result = await Invoke(Tool(source, hasFileTools: true, hasShell: true), "tdd");

            Assert.Contains("tdd body", result);
            Assert.Contains("技能目录：", result);
            Assert.Contains("你的文件工具和 shell 工具", result);
            Assert.Contains("无需重复读取技能目录中的 SKILL.md", result);
            Assert.Contains("- 文件 refs/notes.md", result);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 只有 SKILL.md 的技能没有可读资源:目录与工具提示都是噪音,一个都不该出现,
    /// 只留「正文已完整、勿重复读取」的声明。
    /// </summary>
    [Fact]
    public async Task FileSkill_OnlySkillMd_SkipsDirectoryAndToolHint()
    {
        string root = Path.Combine(Path.GetTempPath(), "uiharu-load-skill-test-only-skill");
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
            Assert.Contains("无需重复读取技能目录中的 SKILL.md", result);
            Assert.DoesNotContain("技能目录：", result);
            Assert.DoesNotContain("你的文件工具和 shell 工具", result);
            Assert.DoesNotContain("技能资源", result);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 清单里的资源/脚本输出相对技能目录的路径行(技能目录已在注入头给出),
    /// agents/ 目录是 Claude Code 生态的元数据,应被过滤。
    /// </summary>
    [Fact]
    public void BuildResourcePathLines_OutputsRelativePaths()
    {
        string content = """
            正文

            <available_resources>
              <resource name="agents/openai.yaml"/>
              <resource name="refs/notes.md"/>
            </available_resources>

            <available_scripts>
              <script name="scripts/run.sh"/>
            </available_scripts>
            """;

        string lines = SkillCatalog.BuildResourcePathLines(content);

        Assert.DoesNotContain("agents", lines);
        Assert.Contains("- 文件 refs/notes.md", lines);
        Assert.Contains("- 脚本 scripts/run.sh", lines);
    }

    /// <summary>正文里没有清单块时不出资源行</summary>
    [Fact]
    public void BuildResourcePathLines_EmptyWithoutBlocks()
    {
        Assert.Equal(string.Empty, SkillCatalog.BuildResourcePathLines("plain body"));
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
