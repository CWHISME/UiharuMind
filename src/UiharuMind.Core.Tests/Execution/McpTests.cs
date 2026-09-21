/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// MCP 侧的不变量：工具怎么并、配置怎么落盘、能力名单怎么收窄。
/// 都不碰单例与网络——这几条规则容易写错，又几乎不可能在真机上稳定复现。
/// </summary>
public class McpTests
{
    private static AIFunction Tool(string name, string description = "does a thing")
    {
        return AIFunctionFactory.Create(() => "ok", name, description);
    }

    private static ResolvedMcpServer Server(string name, bool injectInstructions = true,
        string instructions = "", params string[] toolNames)
    {
        return new ResolvedMcpServer(
            new McpServerConfig { Name = name, InjectInstructions = injectInstructions },
            toolNames.Select(x => Tool(x)).ToList(),
            instructions);
    }

    //================= 工具并集与撞名 =================

    /// <summary>不撞名就不动名字：前缀会变长、会让弱模型更容易拼错，没有理由白付这笔</summary>
    [Fact]
    public void DistinctToolNames_KeepOriginalNames()
    {
        McpToolSet set = McpToolSetBuilder.Build([
            Server("unity", toolNames: ["read_scene"]),
            Server("github", toolNames: ["list_issues"]),
        ]);

        Assert.Equal(["read_scene", "list_issues"], set.Tools.Select(x => x.Name));
        Assert.All(set.Groups.SelectMany(x => x.Tools), x => Assert.False(x.IsRenamed));
    }

    /// <summary>撞名时<b>双方</b>都加前缀：只改一边的话，哪一边被改取决于遍历顺序</summary>
    [Fact]
    public void CollidingToolNames_PrefixBothSides()
    {
        McpToolSet set = McpToolSetBuilder.Build([
            Server("unity", toolNames: ["search"]),
            Server("github", toolNames: ["search"]),
        ]);

        Assert.Equal(["unity_search", "github_search"], set.Tools.Select(x => x.Name));
        Assert.All(set.Groups.SelectMany(x => x.Tools), x => Assert.True(x.IsRenamed));
        // 原名仍然记着:右栏要能说清"它本来叫什么"
        Assert.All(set.Groups.SelectMany(x => x.Tools), x => Assert.Equal("search", x.OriginalName));
    }

    /// <summary>撞名只波及撞了的那个名字，同 server 的其余工具不受连累</summary>
    [Fact]
    public void CollidingToolNames_DoNotAffectSiblings()
    {
        McpToolSet set = McpToolSetBuilder.Build([
            Server("unity", toolNames: ["search", "read_scene"]),
            Server("github", toolNames: ["search"]),
        ]);

        Assert.Equal(["unity_search", "read_scene", "github_search"], set.Tools.Select(x => x.Name));
    }

    /// <summary>server 名里的空格与短横不能进工具名</summary>
    [Fact]
    public void ServerNameWithSeparators_IsSanitizedIntoPrefix()
    {
        McpToolSet set = McpToolSetBuilder.Build([
            Server("my server-1", toolNames: ["search"]),
            Server("other", toolNames: ["search"]),
        ]);

        Assert.Contains("my_server_1_search", set.Tools.Select(x => x.Name));
    }

    /// <summary>改名要真的改到模型看见的那一份，而不只是展示层</summary>
    [Fact]
    public void RenamedTool_ReportsNewNameToModel()
    {
        McpToolSet set = McpToolSetBuilder.Build([
            Server("a", toolNames: ["search"]),
            Server("b", toolNames: ["search"]),
        ]);

        AITool mounted = set.Tools[0];
        Assert.Equal("a_search", mounted.Name);
        // 除名字外一律透传:描述与 schema 不该在包一层之后丢掉
        Assert.Equal("does a thing", mounted.Description);
    }

    //================= server 自述 =================

    /// <summary>自述按 server 分节拼接，关掉的那个不出现</summary>
    [Fact]
    public void Instructions_AreComposedPerServer_AndRespectToggle()
    {
        McpToolSet set = McpToolSetBuilder.Build([
            Server("unity", instructions: "Use read_scene first.", toolNames: ["read_scene"]),
            Server("github", injectInstructions: false, instructions: "Very long guide.",
                toolNames: ["list_issues"]),
        ]);

        Assert.Contains("## unity", set.Instructions);
        Assert.Contains("Use read_scene first.", set.Instructions);
        Assert.DoesNotContain("github", set.Instructions);
        Assert.DoesNotContain("Very long guide.", set.Instructions);

        // 关掉自述不影响它的工具:两件事是分开的
        Assert.Contains("list_issues", set.Tools.Select(x => x.Name));
    }

    /// <summary>server 没给自述时不留空标题</summary>
    [Fact]
    public void EmptyInstructions_ProduceNoSection()
    {
        McpToolSet set = McpToolSetBuilder.Build([Server("unity", toolNames: ["read_scene"])]);

        Assert.Equal(string.Empty, set.Instructions);
        Assert.False(set.Groups[0].InstructionsInjected);
        Assert.Equal(0, set.Groups[0].InstructionsEstimatedTokens);
    }

    /// <summary>token 估算要真的算进去，否则右栏那笔账是假的</summary>
    [Fact]
    public void EstimatedTokens_CoverToolsAndInstructions()
    {
        McpToolSet set = McpToolSetBuilder.Build([
            Server("unity", instructions: "Use read_scene first.", toolNames: ["read_scene"]),
        ]);

        Assert.True(set.EstimatedTokens > 0);
        Assert.True(set.Groups[0].InstructionsEstimatedTokens > 0);
        Assert.Equal(set.Groups[0].EstimatedTokens + set.Groups[0].InstructionsEstimatedTokens,
            set.Groups[0].TotalEstimatedTokens);
    }

    //================= 配置的磁盘形态 =================

    /// <summary>
    /// 生态标准的 <c>mcpServers</c> 配置能直接贴进来用。这正是换格式的理由——
    /// 用户手里已经有一份 Claude Desktop / .mcp.json 的配置
    /// </summary>
    [Fact]
    public void StandardMcpServersJson_IsParsedDirectly()
    {
        const string json = """
                            {
                              "mcpServers": {
                                "filesystem": {
                                  "command": "npx",
                                  "args": ["-y", "@modelcontextprotocol/server-filesystem", "/My Documents/notes"],
                                  "env": { "LOG_LEVEL": "debug" }
                                },
                                "remote": {
                                  "type": "http",
                                  "url": "https://example.com/mcp",
                                  "headers": { "Authorization": "Bearer token" }
                                }
                              }
                            }
                            """;

        McpServersFile file = JsonSerializer.Deserialize<McpServersFile>(json, SaveUtility.JsonOptions)!;
        List<McpServerConfig> configs = file.ToConfigs(new Dictionary<string, McpServerLocalState>());

        McpServerConfig fs = configs.Single(x => x.Name == "filesystem");
        Assert.Equal(EMcpTransportType.Stdio, fs.TransportType);
        Assert.Equal("npx", fs.Command);
        // 承重之处:含空格的路径曾被 Split(' ') 拆成两个参数,静默连不上
        Assert.Equal(["-y", "@modelcontextprotocol/server-filesystem", "/My Documents/notes"], fs.Args);
        Assert.Equal("debug", fs.EnvironmentVariables["LOG_LEVEL"]);

        McpServerConfig remote = configs.Single(x => x.Name == "remote");
        Assert.Equal(EMcpTransportType.Http, remote.TransportType);
        Assert.Equal("Bearer token", remote.Headers["Authorization"]);

        // 缺省的本项目特有两项取默认值:手贴一份标准配置进来应当立刻可用
        Assert.All(configs, x => Assert.True(x.IsEnabled));
        Assert.All(configs, x => Assert.True(x.InjectInstructions));
    }

    /// <summary>省略 type 的写法在生态里很常见，按有无 url 推断</summary>
    [Theory]
    [InlineData("{\"mcpServers\":{\"a\":{\"command\":\"npx\"}}}", EMcpTransportType.Stdio)]
    [InlineData("{\"mcpServers\":{\"a\":{\"url\":\"https://x/mcp\"}}}", EMcpTransportType.Http)]
    public void MissingType_IsInferredFromUrl(string json, EMcpTransportType expected)
    {
        McpServersFile file = JsonSerializer.Deserialize<McpServersFile>(json, SaveUtility.JsonOptions)!;
        Assert.Equal(expected, file.ToConfigs(new Dictionary<string, McpServerLocalState>())[0].TransportType);
    }

    /// <summary>写出去的那份也要能被别的客户端读懂，所以往返必须无损</summary>
    [Fact]
    public void ConfigRoundTrip_PreservesEverything()
    {
        List<McpServerConfig> original =
        [
            new()
            {
                Name = "unity", TransportType = EMcpTransportType.Stdio, Command = "dotnet",
                Args = ["run", "--project", "/path with space/Server.csproj"],
                EnvironmentVariables = { ["UNITY_PORT"] = "8080" },
                IsEnabled = false, InjectInstructions = false,
            },
            new()
            {
                Name = "remote", TransportType = EMcpTransportType.Http, Url = "https://example.com/mcp",
                Headers = { ["Authorization"] = "Bearer t" },
            },
        ];

        var (standard, states) = McpServersFile.FromConfigs(original);
        string json = JsonSerializer.Serialize(standard, SaveUtility.JsonOptions);
        McpServersFile reloaded = JsonSerializer.Deserialize<McpServersFile>(json, SaveUtility.JsonOptions)!;
        List<McpServerConfig> configs = reloaded.ToConfigs(states);

        McpServerConfig unity = configs.Single(x => x.Name == "unity");
        Assert.Equal(["run", "--project", "/path with space/Server.csproj"], unity.Args);
        Assert.Equal("8080", unity.EnvironmentVariables["UNITY_PORT"]);
        Assert.False(unity.IsEnabled);
        Assert.False(unity.InjectInstructions);

        McpServerConfig remote = configs.Single(x => x.Name == "remote");
        Assert.Equal("https://example.com/mcp", remote.Url);
        Assert.Equal("Bearer t", remote.Headers["Authorization"]);
    }

    /// <summary>标准文件里不该出现本项目特有的字段，否则互拷出去会污染别人</summary>
    [Fact]
    public void StandardFile_CarriesNoProjectSpecificFields()
    {
        var (standard, _) = McpServersFile.FromConfigs([
            new McpServerConfig { Name = "unity", Command = "npx", IsEnabled = false, InjectInstructions = false },
        ]);

        string json = JsonSerializer.Serialize(standard, SaveUtility.JsonOptions);
        Assert.DoesNotContain("IsEnabled", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InjectInstructions", json, StringComparison.OrdinalIgnoreCase);
        // 另一种传输的残留字段也不该写出去
        Assert.DoesNotContain("url", json, StringComparison.OrdinalIgnoreCase);
    }

    //================= 能力名单 =================

    //================= 能力快照 =================

    /// <summary>
    /// MCP 工具不重复列进自建工具：它们已经按 server 分好组，
    /// 两处都列会让右栏把同一个工具数一遍又数一遍
    /// </summary>
    [Fact]
    public void Capture_KeepsMcpToolsOutOfBuiltInList()
    {
        McpToolSet mcp = McpToolSetBuilder.Build([Server("unity", toolNames: ["read_scene"])]);
        List<AgentToolEntry> entries =
        [
            new(EAgentCapability.FileAccess, Tool("read_file")),
            new(EAgentCapability.Mcp, mcp.Tools[0]),
        ];

        AgentCapabilitySnapshot snapshot = AgentCapabilitySnapshot.Capture(entries, mcp);

        Assert.Equal(["read_file"], snapshot.BuiltInTools.Select(x => x.Name));
        Assert.Single(snapshot.Mcp.Groups);
    }

    /// <summary>
    /// 按能力档汇总必须能对上逐项之和。角色编辑页据此显示「关掉这一档能省多少」，
    /// 对不上就是给了个假数字
    /// </summary>
    [Fact]
    public void Capture_SumsTokensPerCapability()
    {
        List<AgentToolEntry> entries =
        [
            new(EAgentCapability.FileAccess, Tool("read_file")),
            new(EAgentCapability.FileAccess, Tool("write_file")),
            new(EAgentCapability.Shell, Tool("run_shell")),
        ];

        AgentCapabilitySnapshot snapshot = AgentCapabilitySnapshot.Capture(entries, McpToolSet.Empty);

        int fileTokens = snapshot.BuiltInTools
            .Where(x => x.Capability == EAgentCapability.FileAccess)
            .Sum(x => x.EstimatedTokens);
        Assert.Equal(fileTokens, snapshot.TokensByCapability[EAgentCapability.FileAccess]);
        Assert.Equal(snapshot.BuiltInTools.Sum(x => x.EstimatedTokens), snapshot.EstimatedTokens);
        // 归属登记在装配现场,不靠名字反推:两档不该混
        Assert.NotEqual(snapshot.TokensByCapability[EAgentCapability.FileAccess],
            snapshot.TokensByCapability[EAgentCapability.Shell]);
    }

    /// <summary>
    /// 没有工具、也没有提示词分段，快照就是空的——界面据此显示空态。
    ///
    /// 判的是<b>值</b>而不是同一个实例：早先这里在工具为空时直接早退返回
    /// <see cref="AgentCapabilitySnapshot.Empty"/>，而提示词进账之后那条捷径成了漏计——
    /// 能力全关的智能体照样有角色提示与工作区规矩，恰恰是最该看清账的场合。
    /// </summary>
    [Fact]
    public void Capture_WithoutToolsOrPrompt_IsEmpty()
    {
        foreach (AgentCapabilitySnapshot snapshot in new[]
                 {
                     AgentCapabilitySnapshot.Capture(null, McpToolSet.Empty),
                     AgentCapabilitySnapshot.Capture([], McpToolSet.Empty),
                 })
        {
            Assert.Empty(snapshot.BuiltInTools);
            Assert.Empty(snapshot.PromptSegments);
            Assert.Empty(snapshot.Mcp.Groups);
            Assert.Equal(0, snapshot.EstimatedTokens);
        }
    }

    /// <summary>
    /// 子代理不能比派活的父代理能力更大：父代理禁掉的 server，
    /// 不能靠挂一个没禁的子智能体绕回来
    /// </summary>
    [Fact]
    public void Intersect_UnionsDisabledMcpServers()
    {
        AgentToolConfig parent = new() { DisabledMcpServers = { "unity" } };
        AgentToolConfig child = new() { DisabledMcpServers = { "github" } };

        List<string> result = parent.Intersect(child).DisabledMcpServers;

        Assert.Equal(["github", "unity"], result.Order());
    }

    /// <summary>
    /// 「这一轮该管哪些 server」只有一处定义，预连与装配共用。
    ///
    /// 两条闸门是分开的：托管是连接层（全局，要不要为它拉起进程），禁用是能力层
    /// （按角色，这个智能体能不能用它）。任一关着都不该管——曾经预连只看托管，
    /// 于是角色禁掉的 server 照样被拉起子进程、照样等满超时，工具取回后再被丢掉。
    /// </summary>
    [Theory]
    [InlineData(true, null, true)] //托管开着、没禁:该管
    [InlineData(false, null, false)] //没托管
    [InlineData(true, "unity", false)] //托管着但本角色禁了:不该为它起进程、也不该等
    [InlineData(true, "UNITY", false)] //名单不区分大小写
    [InlineData(true, "github", true)] //禁的是别人
    public void IsInPlay_RequiresBothHostingAndAvailability(bool enabled, string? disabledName, bool expected)
    {
        McpServerConfig server = new() { Name = "unity", IsEnabled = enabled };
        HashSet<string> disabled = McpManager.DisabledSet(disabledName == null ? null : [disabledName]);

        Assert.Equal(expected, McpManager.IsInPlay(server, disabled));
    }

    //================= 作用域索引键 =================

    /// <summary>
    /// 同名不同工作区必须是两个键。这是项目级作用域里最硬的一条：只按名字索引时，
    /// 两个 Unity 项目各有一个叫 unity 的 server 会互相顶掉连接。
    /// </summary>
    [Fact]
    public void ServerKey_SameNameDifferentWorkspace_AreDistinct()
    {
        McpServerKey a = new("/tmp/projA", "unity");
        McpServerKey b = new("/tmp/projB", "unity");
        McpServerKey global = new(null, "unity");

        Assert.NotEqual(a, b);
        Assert.NotEqual(a, global);
        Assert.True(global.IsGlobal);
        Assert.False(a.IsGlobal);
    }

    /// <summary>
    /// 两半的比较口径不同：名字不区分大小写（手写进 json，大小写不该成为静默失配），
    /// 路径区分大小写（大小写不敏感是文件系统的属性，猜错会把两个真不同的目录当成一个）。
    /// </summary>
    [Theory]
    [InlineData("/tmp/proj", "unity", "/tmp/proj", "UNITY", true)]
    [InlineData("/tmp/proj", "unity", "/tmp/PROJ", "unity", false)]
    [InlineData(null, "unity", null, "Unity", true)]
    public void ServerKey_ComparesNameLooselyAndPathStrictly(string? wsA, string nameA,
        string? wsB, string nameB, bool expected)
    {
        McpServerKey a = new(wsA, nameA);
        McpServerKey b = new(wsB, nameB);

        Assert.Equal(expected, a.Equals(b));
        if (expected) Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>
    /// 同一个目录写成带不带末尾分隔符必须是同一个键，否则会各连一份子进程。
    /// </summary>
    [Fact]
    public void ServerKey_NormalizesTrailingSeparator()
    {
        string root = Path.GetTempPath();
        McpServerKey withSlash = new(root, "unity");
        McpServerKey withoutSlash = new(Path.TrimEndingDirectorySeparator(root), "unity");

        Assert.Equal(withSlash, withoutSlash);
    }

    /// <summary>相对路径与它的绝对形态是同一个键：装配侧传来的路径来源不一</summary>
    [Fact]
    public void ServerKey_NormalizesToAbsolutePath()
    {
        McpServerKey relative = new(".", "unity");
        McpServerKey absolute = new(Directory.GetCurrentDirectory(), "unity");

        Assert.Equal(absolute, relative);
    }

    /// <summary>分组明细要带回来源，否则面板问不到项目级那一条的状态</summary>
    [Fact]
    public void Build_CarriesWorkspaceScopeIntoGroups()
    {
        McpServerConfig config = new() { Name = "unity", WorkspacePath = "/tmp/projA" };
        McpToolSet set = McpToolSetBuilder.Build([
            new ResolvedMcpServer(config, [Tool("refresh")], string.Empty),
        ]);

        Assert.Equal("/tmp/projA", Assert.Single(set.Groups).WorkspacePath);
        Assert.True(config.IsWorkspaceScoped);
    }

    //================= 作用域合并（项目级覆盖全局） =================

    private static McpServerConfig Config(string name, string? workspace = null, string command = "npx")
    {
        return new McpServerConfig { Name = name, WorkspacePath = workspace, Command = command };
    }

    /// <summary>项目级同名胜出，且被顶掉的那条必须留在结果里——否则覆盖这件事在界面上不可见</summary>
    [Fact]
    public void Merge_WorkspaceWinsAndKeepsShadowedVisible()
    {
        List<EffectiveMcpServer> merged = McpServerScopeMerger.Merge(
            [Config("unity", command: "global-unity"), Config("github")],
            [Config("unity", "/tmp/projA", "project-unity")]);

        Assert.Equal(2, merged.Count);
        // 项目级排在前
        Assert.Equal("project-unity", merged[0].Config.Command);
        Assert.True(merged[0].ShadowsGlobal);
        Assert.Equal("global-unity", merged[0].Shadowed!.Command);

        Assert.Equal("github", merged[1].Config.Name);
        Assert.False(merged[1].ShadowsGlobal);
    }

    /// <summary>覆盖判定不区分大小写：与索引键、禁用名单同一口径</summary>
    [Fact]
    public void Merge_NameComparisonIgnoresCase()
    {
        List<EffectiveMcpServer> merged = McpServerScopeMerger.Merge(
            [Config("Unity", command: "global-unity")],
            [Config("unity", "/tmp/projA", "project-unity")]);

        EffectiveMcpServer single = Assert.Single(merged);
        Assert.Equal("project-unity", single.Config.Command);
        Assert.True(single.ShadowsGlobal);
    }

    /// <summary>没有项目级配置时，合并结果与全局那份逐条相同（行为零变化的兜底）</summary>
    [Fact]
    public void Merge_WithoutWorkspaceConfig_PassesGlobalThrough()
    {
        List<McpServerConfig> global = [Config("unity"), Config("github")];

        List<EffectiveMcpServer> merged = McpServerScopeMerger.Merge(global, []);

        Assert.Equal(2, merged.Count);
        Assert.All(merged, x => Assert.False(x.ShadowsGlobal));
        Assert.Equal(global.Select(x => x.Name), merged.Select(x => x.Config.Name));
    }

    //================= 可执行面指纹 =================

    /// <summary>
    /// 指纹只认可执行面：改名、改托管开关都不该让已给的授权作废，
    /// 而命令、参数、环境变量任一处变化都必须作废。
    /// </summary>
    [Fact]
    public void Fingerprint_CoversExecutableSurfaceOnly()
    {
        McpServerConfig baseline = new()
        {
            Name = "unity", Command = "npx", Args = ["-y", "unity-mcp"],
            EnvironmentVariables = { ["PORT"] = "6400" },
        };
        string print = McpServerFingerprint.Of(baseline);

        // 名字与本机开关不在可执行面内
        Assert.Equal(print, McpServerFingerprint.Of(new McpServerConfig
        {
            Name = "renamed", Command = "npx", Args = ["-y", "unity-mcp"],
            IsEnabled = false, InjectInstructions = false,
            EnvironmentVariables = { ["PORT"] = "6400" },
        }));

        // 命令变了
        Assert.NotEqual(print, McpServerFingerprint.Of(new McpServerConfig
        {
            Name = "unity", Command = "curl", Args = ["-y", "unity-mcp"],
            EnvironmentVariables = { ["PORT"] = "6400" },
        }));

        // 环境变量的值变了
        Assert.NotEqual(print, McpServerFingerprint.Of(new McpServerConfig
        {
            Name = "unity", Command = "npx", Args = ["-y", "unity-mcp"],
            EnvironmentVariables = { ["PORT"] = "9999" },
        }));
    }

    /// <summary>环境变量的书写顺序不是语义：调换两行不该白白重新要一次授权</summary>
    [Fact]
    public void Fingerprint_IgnoresEnvOrdering()
    {
        McpServerConfig a = new()
        {
            Name = "s", Command = "run",
            EnvironmentVariables = { ["A"] = "1", ["B"] = "2" },
        };
        McpServerConfig b = new()
        {
            Name = "s", Command = "run",
            EnvironmentVariables = { ["B"] = "2", ["A"] = "1" },
        };

        Assert.Equal(McpServerFingerprint.Of(a), McpServerFingerprint.Of(b));
    }

    /// <summary>
    /// 参数分项参与：<c>["a b"]</c> 与 <c>["a","b"]</c> 是两条不同的命令，
    /// 拼成一行会撞成同一个指纹——那等于一条已授权的命令能变形成另一条。
    /// </summary>
    [Fact]
    public void Fingerprint_DistinguishesArgumentBoundaries()
    {
        McpServerConfig joined = new() { Name = "s", Command = "run", Args = ["a b"] };
        McpServerConfig split = new() { Name = "s", Command = "run", Args = ["a", "b"] };

        Assert.NotEqual(McpServerFingerprint.Of(joined), McpServerFingerprint.Of(split));
    }

    //================= 授权匹配 =================

    /// <summary>改了命令就不再算已授权——这一条塌了，整套确认形同虚设</summary>
    [Fact]
    public void Trust_ChangedCommandInvalidatesApproval()
    {
        McpServerConfig approved = Config("unity", "/tmp/projA", "npx");
        List<McpTrustRecord> records =
        [
            new() { Name = "unity", Fingerprint = McpServerFingerprint.Of(approved) },
        ];

        Assert.True(McpTrustStore.Matches(records, approved));
        Assert.False(McpTrustStore.Matches(records, Config("unity", "/tmp/projA", "curl")));
        Assert.False(McpTrustStore.Matches(records, Config("other", "/tmp/projA", "npx")));
    }
}
