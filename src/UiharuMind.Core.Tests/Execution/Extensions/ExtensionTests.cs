using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Core.Core;
using UiharuMind.Core.Tests.Agent;

namespace UiharuMind.Core.Tests.Execution.Extensions;

/// <summary>
/// 扩展（ADR 0079）：两把元工具恒定、帮助与调用、拆开调用、风险分级与本会话放行。都不碰网络
/// </summary>
public class ExtensionTests
{
    private static readonly AgentPathResolver Paths = new("/tmp/uiharu-ws", "/tmp/uiharu-ws-room", string.Empty);

    private static ExtensionHub Hub(params BuiltinExtension[] builtins) => new(builtins, null);

    private static BuiltinExtension Echo(string? unavailable = null) => new(BuiltinExtensions.Audio, "Usage notes.",
        [new ExtensionCommand(BuiltinExtensions.AudioTranscribe, AIFunctionFactory.Create(
            (string path, bool subtitles = false) => $"heard {path} {subtitles}", TranscribeAudioTool.ToolName,
            "Transcribe an audio file."))])
    {
        Unavailable = unavailable,
    };

    private static Dictionary<string, JsonElement> Args(object value) =>
        JsonSerializer.SerializeToElement(value).EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone());

    //================= 元工具 =================

    /// <summary>缓存前缀稳定的根：定义不随有哪些扩展、连没连上而变</summary>
    [Fact]
    public void MetaTools_AreIdenticalWhateverIsBehindThem()
    {
        static string Describe(IEnumerable<AITool> tools) => string.Join('\n', tools.Cast<AIFunction>()
            .Select(x => $"{x.Name}|{x.Description}|{x.JsonSchema.GetRawText()}"));

        Assert.Equal(Describe(ExtensionMetaTools.Create(Hub())), Describe(ExtensionMetaTools.Create(Hub(Echo()))));
    }

    /// <summary>帮助不审批；调用一律经审批中间件，放不放交给逐条的规则</summary>
    [Fact]
    public void OnlyTheCallIsBehindApproval()
    {
        IReadOnlyList<AITool> tools = ExtensionMetaTools.Create(Hub());

        Assert.IsNotType<ApprovalRequiredAIFunction>(tools.Single(x => x.Name == ExtensionCalls.HelpName));
        Assert.IsType<ApprovalRequiredAIFunction>(tools.Single(x => x.Name == ExtensionCalls.CallName));
    }

    //================= 帮助与调用 =================

    [Fact]
    public async Task Help_ListsEveryExtension_WithWhyOneIsUnavailable()
    {
        BuiltinExtension image = new(BuiltinExtensions.Image, "g", []) { Unavailable = "No image model is configured." };

        string text = await Hub(Echo(), image).HelpAsync(null, null, TestContext.Current.CancellationToken);

        Assert.Contains("- audio (built-in)", text);
        Assert.Contains("- image (built-in, not available: No image model is configured.)", text);
    }

    [Fact]
    public async Task Help_ByNameAndCommand_UsesCommandNamesNotToolNames()
    {
        ExtensionHub hub = Hub(Echo());

        string overview = await hub.HelpAsync("AUDIO", null, TestContext.Current.CancellationToken);
        string detail = await hub.HelpAsync("audio", "transcribe", TestContext.Current.CancellationToken);

        Assert.Contains("Usage notes.", overview);
        Assert.Contains("- transcribe {path, subtitles?}: Transcribe an audio file.", overview); //参数签名随概览给出
        Assert.DoesNotContain(TranscribeAudioTool.ToolName, overview);
        Assert.StartsWith("# audio.transcribe", detail);
        Assert.Contains("\"path\"", detail);
    }

    [Fact]
    public async Task Call_BindsArgumentsAndRunsTheCommand()
    {
        object? result = await Hub(Echo()).CallAsync("audio", "transcribe", Args(new { path = "a.wav", subtitles = true }),
            TestContext.Current.CancellationToken);

        Assert.Equal("heard a.wav True", result?.ToString());
    }

    [Fact]
    public async Task Call_Failures_AreToolResultsNotExceptions()
    {
        ExtensionHub hub = Hub(Echo());
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.Contains("no command 'record'", (string)(await hub.CallAsync("audio", "record", null, ct))!);
        Assert.Contains("Unknown extension 'video'", (string)(await hub.CallAsync("video", "play", null, ct))!);
        Assert.Contains("turned off for this agent", (string)(await hub.CallAsync("browser", "snapshot", null, ct))!);
        Assert.Contains("Invalid arguments for audio.transcribe",
            (string)(await hub.CallAsync("audio", "transcribe", null, ct))!); //缺必填 path
        Assert.Contains("not available: busy",
            (string)(await Hub(Echo("busy")).CallAsync("audio", "transcribe", Args(new { path = "a" }), ct))!);
    }

    /// <summary>内置扩展的命令一一对上原工具：工具改名而对照表没跟上，造扩展时当场炸</summary>
    [Fact]
    public void BuiltinExtensions_BindEveryCommandToItsTool()
    {
        BuiltinExtension browser = BuiltinExtensions.CreateBrowser(PuppeteerBrowserHost.Shared, "s1", "/tmp/uiharu-spill",
            EReadImageMode.None);
        BuiltinExtension audio = BuiltinExtensions.CreateAudio(Paths, new CharacterData { CharacterName = "Me" });
        BuiltinExtension image = BuiltinExtensions.CreateImage(Paths, null, configured: false);

        Assert.Equal(["snapshot", "navigate", "act", "handoff"], browser.Commands.Select(x => x.Name));
        Assert.Equal(["synthesize", "transcribe"], audio.Commands.Select(x => x.Name));
        Assert.Equal(["generate"], image.Commands.Select(x => x.Name));
        Assert.NotNull(image.Unavailable);
        Assert.Equal(["transcribe"], BuiltinExtensions.CreateAudio(Paths, null).Commands.Select(x => x.Name));
    }

    /// <summary>ExtensionCall 描述里那份速查是手写常量：每条内置命令的签名都得与它背后工具的 schema 对上</summary>
    [Fact]
    public void CallUsage_MatchesEveryBuiltinCommandSchema()
    {
        BuiltinExtension[] builtins =
        [
            BuiltinExtensions.CreateBrowser(PuppeteerBrowserHost.Shared, "s1", "/tmp/uiharu-spill", EReadImageMode.None),
            BuiltinExtensions.CreateImage(Paths, null, configured: true),
            BuiltinExtensions.CreateAudio(Paths, new CharacterData { CharacterName = "Me" }),
        ];

        foreach (BuiltinExtension builtin in builtins)
        {
            foreach (ExtensionCommand command in builtin.Commands)
            {
                string signature = McpHelpFormatter.Signature(command.Function.JsonSchema);
                Assert.Contains($"{builtin.Name}.{command.Name} {signature}", BuiltinExtensions.CallUsage);
            }
        }
    }

    /// <summary>把 command 塞进 arguments 是弱模型的常见错：报错要指出放错了层，并给可照抄的写法</summary>
    [Fact]
    public async Task Call_CommandInsideArguments_SaysItBelongsAtTheTopLevel()
    {
        string text = (string)(await Hub(Echo()).CallAsync("audio", null,
            Args(new { command = "transcribe", args = new { path = "a.wav" } }), TestContext.Current.CancellationToken))!;

        Assert.Contains("put inside `arguments`", text);
        Assert.Contains(BuiltinExtensions.CallExample, text);
    }

    /// <summary>参数名猜错（ref/text）时，除了缺什么，还要说哪些不认识、正确签名是什么</summary>
    [Fact]
    public async Task Call_WrongParameterNames_ListsUnknownOnesAndTheSignature()
    {
        string text = (string)(await Hub(Echo()).CallAsync("audio", "transcribe", Args(new { file = "a.wav" }),
            TestContext.Current.CancellationToken))!;

        Assert.Contains("missing path; unknown file", text);
        Assert.Contains("Parameters: {path, subtitles?}", text);
    }

    //================= 拆开调用 =================

    [Fact]
    public void Unwrap_GivesTheInnerToolNameAndArguments_KeepingTheCallId()
    {
        FunctionCallContent call = ExtensionCallOf.Make("Browser", "BrowserAct", new { action = "click", target = "e2" },
            callId: "x9");

        FunctionCallContent inner = ExtensionCalls.Unwrap(call);

        Assert.Equal("x9", inner.CallId);
        Assert.Equal(BrowserTools.ActToolName, inner.Name); //大小写与旧工具名都认,换成规范写法
        Assert.Equal("click", inner.Arguments!["action"]!.ToString());
        Assert.Equal("unity.read_scene",
            ExtensionCalls.Unwrap(ExtensionCallOf.Make("unity", "read_scene")).Name);
    }

    [Fact]
    public void Unwrap_LeavesOtherCallsAlone()
    {
        FunctionCallContent shell = new("c1", "Shell", new Dictionary<string, object?> { ["command"] = "ls" });
        FunctionCallContent broken = new("c2", ExtensionCalls.CallName, new Dictionary<string, object?>());

        Assert.Same(shell, ExtensionCalls.Unwrap(shell));
        Assert.Same(broken, ExtensionCalls.Unwrap(broken));
    }

    //================= 风险与放行 =================

    [Theory]
    [InlineData("browser", "snapshot", EExtensionRisk.ReadOnly)]
    [InlineData("browser", "act", EExtensionRisk.Standard)]
    [InlineData("browser", "handoff", EExtensionRisk.AlwaysAsk)]
    [InlineData("image", "generate", EExtensionRisk.Standard)]
    [InlineData("audio", "transcribe", EExtensionRisk.Standard)]
    [InlineData("browser", "goto", EExtensionRisk.ReadOnly)] //没这条命令,什么也不会跑
    [InlineData("browser", "hand_off", EExtensionRisk.ReadOnly)]
    [InlineData("unity", "read_scene", EExtensionRisk.ReadOnly)] //server 自己声明了只读
    [InlineData("unity", "delete_scene", EExtensionRisk.Standard)]
    public void Classify(string extension, string command, EExtensionRisk expected)
    {
        EExtensionRisk? risk = ExtensionRisk.Classify(ExtensionCallOf.Make(extension, command),
            (server, tool) => server == "unity" && tool == "read_scene");

        Assert.Equal(expected, risk);
    }

    [Fact]
    public void Classify_IgnoresOtherTools_AndLetsBrokenCallsThrough()
    {
        Assert.Null(ExtensionRisk.Classify(new FunctionCallContent("c1", "Shell")));
        // 缺扩展名的调用到工具里只会报错:不为它弹一张空卡
        Assert.Equal(EExtensionRisk.ReadOnly,
            ExtensionRisk.Classify(new FunctionCallContent("c2", ExtensionCalls.CallName, new Dictionary<string, object?>())));
    }

    [Fact]
    public async Task ScheduledRunsAllowEverythingButTheAlwaysAskOnes()
    {
        IReadOnlyList<string> all = [ExtensionCalls.AllExtensions];
        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> rules =
            ApprovalModeMapper.BuildRules(EAgentPermissionMode.AutoEdit, "/tmp/uiharu-ws",
                sessionExtensionApprovalSource: () => all);

        Assert.True(await ApprovalRuleProbe.IsApprovedAsync(rules, ExtensionCallOf.Make("image", "generate")));
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(rules, ExtensionCallOf.Make("browser", "handoff")));
    }

    [Fact]
    public void SessionApprovals_MatchByNameOrStar()
    {
        Assert.True(ExtensionRisk.IsApproved(["Browser"], "browser"));
        Assert.True(ExtensionRisk.IsApproved(["*"], "unity"));
        Assert.False(ExtensionRisk.IsApproved(["image"], "browser"));
        Assert.False(ExtensionRisk.IsApproved(null, "browser"));
    }

    //================= 名单 =================

    [Fact]
    public void Roster_ListsUndisabledBuiltinsThenServers_SkippingNameClashes()
    {
        AgentToolConfig config = new() { DisabledExtensions = { "image" } };
        McpToolSet mcp = new()
        {
            Servers = [new McpRosterServer { Name = "unity", Description = "d" }, new McpRosterServer { Name = "browser" }],
        };

        IReadOnlyList<(string Name, string Description)> roster = ExtensionRoster.Of(config, mcp);

        Assert.Equal(["browser", "audio", "unity"], roster.Select(x => x.Name));
        Assert.Equal(string.Empty, ExtensionRoster.Section(
            new AgentToolConfig { DisabledExtensions = [..BuiltinExtensions.Names] }, McpToolSet.Empty));
    }

    /// <summary>旧档案的 MCP 禁用名单读进新名单，写出时不再带旧字段</summary>
    [Fact]
    public void LegacyDisabledMcpServers_AreReadIntoDisabledExtensions()
    {
        AgentToolConfig config = JsonSerializer.Deserialize<AgentToolConfig>(
            """{"EnableBrowser":true,"DisabledMcpServers":["unity"]}""", SaveUtility.JsonOptions)!;

        Assert.Equal(["unity"], config.DisabledExtensions);
        string json = JsonSerializer.Serialize(config, SaveUtility.JsonOptions);
        Assert.DoesNotContain("DisabledMcpServers", json);
        Assert.DoesNotContain("EnableBrowser", json);
    }

    //================= 审查补的口子 =================

    /// <summary>
    /// 审批与执行认同一个命令名：执行这边若模糊匹配（hand_off → handoff），审批按「没这条命令」判成一般级，
    /// 放行过浏览器的会话就会不问用户直接接管
    /// </summary>
    [Theory]
    [InlineData("hand_off")]
    [InlineData("Hand-Off")]
    public async Task LooseCommandNames_NeitherRunNorSlipPastTheHardAsk(string command)
    {
        BuiltinExtension browser = BuiltinExtensions.CreateBrowser(PuppeteerBrowserHost.Shared, "s1", "/tmp/uiharu-spill",
            EReadImageMode.None);

        object? result = await Hub(browser).CallAsync("browser", command, null, TestContext.Current.CancellationToken);

        Assert.Contains("no command", Assert.IsType<string>(result));
    }

    /// <summary>记忆目录那条规则只认写工具：调用里顺手带一个指向记忆目录的 filePath，不能把接管或 shell 放过去</summary>
    [Fact]
    public async Task MemoryRule_DoesNotApproveOtherToolsCarryingAMemoryPath()
    {
        const string root = "/tmp/uiharu-ws";
        const string memory = "/tmp/uiharu-data/Memory";
        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> rules = ApprovalModeMapper.BuildRules(
            EAgentPermissionMode.FullAuto, root, approvedWriteRoot: "", memoryWriteRoot: memory,
            sessionExtensionApprovalSource: () => ["*"]);
        FunctionCallContent handoff = ExtensionCallOf.Make("browser", "handoff", new { task = "x" });
        handoff.Arguments!["filePath"] = memory + "/note.md";
        FunctionCallContent shell = new("c2", "Shell", new Dictionary<string, object?>
        {
            ["command"] = "rm -rf /", ["filePath"] = memory + "/note.md",
        });

        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(rules, handoff));
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(
            ApprovalModeMapper.BuildRules(EAgentPermissionMode.ReadOnly, root, memoryWriteRoot: memory), shell));
    }

    /// <summary>参数对象写坏了就别执行：按「没参数」跑，全可选的命令会拿默认值真的去做一件事</summary>
    [Fact]
    public async Task BrokenArgumentsObject_RunsNothing()
    {
        AIFunction call = (AIFunction)ExtensionMetaTools.Create(Hub(Echo())).Single(x => x.Name == ExtensionCalls.CallName);

        object? result = await call.InvokeAsync(new AIFunctionArguments
        {
            ["extension"] = "audio", ["command"] = "transcribe", ["arguments"] = "{not json",
        }, TestContext.Current.CancellationToken);

        Assert.Contains("Nothing was run", Assert.IsType<string>(result));
    }

    /// <summary>命令跑起来出的错不说成「参数不对」：那会诱导模型换参数重试，可能白花钱</summary>
    [Fact]
    public async Task FailuresWhileRunning_AreNotReportedAsBadArguments()
    {
        BuiltinExtension failing = new(BuiltinExtensions.Audio, "g",
            [new ExtensionCommand(BuiltinExtensions.AudioTranscribe, AIFunctionFactory.Create(
                string (string path) => throw new InvalidOperationException("Audio stream error"), TranscribeAudioTool.ToolName))]);

        string result = Assert.IsType<string>(await Hub(failing).CallAsync("audio", "transcribe", Args(new { path = "a" }),
            TestContext.Current.CancellationToken));

        Assert.Equal("audio.transcribe failed: Audio stream error", result);
    }
}
