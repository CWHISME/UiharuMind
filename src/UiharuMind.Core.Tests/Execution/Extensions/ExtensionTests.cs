using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Core.AI.Execution.Tools.Desktop;
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

    /// <summary>测试用的扩展：直接给命令与缺前提原因，绕开装配（规格/装配体都有完整元数据）</summary>
    private sealed class StubExtension : BuiltinExtension
    {
        private readonly string _name;
        private readonly string _guide;
        private readonly string? _unavailable;

        public StubExtension(string name, string guide, IReadOnlyList<ExtensionCommand> commands,
            string? unavailable = null)
        {
            _name = name;
            _guide = guide;
            _unavailable = unavailable;
            SetCommands(commands);
        }

        public override string Name => _name;
        public override string Guide => _guide;
        public override string Summary => _name;
        public override string? Unavailable => _unavailable;
        public override IReadOnlyList<(string Command, string ToolName)> CommandNames =>
            Commands.Select(x => (x.Name, x.Function.Name)).ToList();
        protected override BuiltinExtension CreateAssembled(AgentToolResources resources,
            EReadImageMode imageMode, bool mountFiles) => throw new NotSupportedException();
    }

    private static BuiltinExtension Echo(string? unavailable = null) => new StubExtension(BuiltinExtensions.Audio,
        "Usage notes.",
        [new ExtensionCommand(BuiltinExtensions.AudioTranscribe, AIFunctionFactory.Create(
            (string path, bool subtitles = false) => $"heard {path} {subtitles}", TranscribeAudioTool.ToolName,
            "Transcribe an audio file."))], unavailable);

    private static Dictionary<string, JsonElement> Args(object value) =>
        JsonSerializer.SerializeToElement(value).EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone());

    /// <summary>装配一份扩展要用的资源桩（缺省不配：浏览器/桌面/会话/角色都没有）</summary>
    private static AgentToolResources Resources(IBrowserHost? browser = null, DesktopHost? desktop = null,
        Func<ChatSession?>? session = null, CharacterData? character = null) => new()
    {
        Paths = Paths,
        Browser = browser,
        Desktop = desktop,
        Session = session,
        Character = character,
    };

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
        BuiltinExtension image = new StubExtension(BuiltinExtensions.Image, "g", [], "No image model is configured.");

        string text = await Hub(Echo(), image).HelpAsync(null, null, TestContext.Current.CancellationToken);

        Assert.Contains("- audio（内置", text);
        Assert.Contains("- image（内置，不可用：No image model is configured", text);
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

    /// <summary>命令详情里的参数说明是给模型读的：中文要原样出来，不能是一串 \uXXXX</summary>
    [Fact]
    public async Task Help_Detail_KeepsNonAsciiDescriptionsReadable()
    {
        ExtensionHub hub = Hub(AudioExtension.Spec.Assemble(Resources(), EReadImageMode.None, mountFiles: true));

        string detail = await hub.HelpAsync("audio", "transcribe", TestContext.Current.CancellationToken);

        Assert.Contains("音频文件的路径", detail);
        Assert.DoesNotContain("\\u", detail);
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

        Assert.Contains("没有 'record' 这条命令", (string)(await hub.CallAsync("audio", "record", null, ct))!);
        Assert.Contains("未知的扩展 'video'", (string)(await hub.CallAsync("video", "play", null, ct))!);
        Assert.Contains("扩展已为本智能体关闭", (string)(await hub.CallAsync("browser", "snapshot", null, ct))!);
        Assert.Contains("无效参数（audio.transcribe",
            (string)(await hub.CallAsync("audio", "transcribe", null, ct))!); //缺必填 path
        Assert.Contains("当前不可用：busy",
            (string)(await Hub(Echo("busy")).CallAsync("audio", "transcribe", Args(new { path = "a" }), ct))!);
    }

    /// <summary>内置扩展的命令一一对上原工具：工具改名而声明没跟上，装配时当场炸</summary>
    [Fact]
    public void BuiltinExtensions_BindEveryCommandToItsTool()
    {
        BuiltinExtension browser = BrowserExtension.Spec.Assemble(Resources(browser: PuppeteerBrowserHost.Shared),
            EReadImageMode.None, mountFiles: true);
        BuiltinExtension audio = AudioExtension.Spec.Assemble(Resources(character: new CharacterData { CharacterName = "Me" }),
            EReadImageMode.None, mountFiles: true);
        BuiltinExtension image = ImageExtension.Spec.Assemble(Resources(), EReadImageMode.None, mountFiles: true);
        BuiltinExtension desktop = DesktopExtension.Spec.Assemble(Resources(desktop: new DesktopHost(null)),
            EReadImageMode.None, mountFiles: true);
        BuiltinExtension worktree = WorktreeExtension.Spec.Assemble(Resources(session: () => new ChatSession { IsTransient = true }),
            EReadImageMode.None, mountFiles: true);

        Assert.Equal(["snapshot", "navigate", "act", "handoff"], browser.Commands.Select(x => x.Name));
        Assert.Equal(["windows", "snapshot", "act", "launch", "handoff"], desktop.Commands.Select(x => x.Name));
        Assert.Equal(["synthesize", "transcribe"], audio.Commands.Select(x => x.Name));
        Assert.Equal(["generate"], image.Commands.Select(x => x.Name));
        Assert.Equal(["create", "set", "sync", "remove", "list", "status"], worktree.Commands.Select(x => x.Name));
        Assert.NotNull(image.Unavailable);
        Assert.Equal(["transcribe"],
            AudioExtension.Spec.Assemble(Resources(), EReadImageMode.None, mountFiles: true).Commands.Select(x => x.Name));
    }

    /// <summary>把 command 塞进 arguments 是弱模型的常见错：报错要指出放错了层，并给可照抄的写法</summary>
    [Fact]
    public async Task Call_WithoutCommandName_SaysHowToWriteIt()
    {
        string text = (string)(await Hub(Echo()).CallAsync("audio", null,
            Args(new { path = "a.wav" }), TestContext.Current.CancellationToken))!;

        Assert.Contains("扩展名.命令名", text);
        Assert.Contains(BuiltinExtensions.CallExample, text);
    }

    /// <summary>全名按第一个点号切：命令名里可以再有点号；只给扩展名时命令为空；分开写的旧字段照认</summary>
    [Theory]
    [InlineData("""{"name":"unity.scene.read"}""", "unity", "scene.read")]
    [InlineData("""{"name":"browser"}""", "browser", null)]
    [InlineData("""{"command":"browser.snapshot"}""", "browser", "snapshot")]
    [InlineData("""{"extension":"audio","command":"transcribe"}""", "audio", "transcribe")]
    [InlineData("""{"extension":"audio","command":"audio.transcribe"}""", "audio", "transcribe")]
    public void ReadParts_SplitsTheFullNameAtTheFirstDot(string json, string extension, string? command)
    {
        Dictionary<string, object?> arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!
            .ToDictionary(x => x.Key, object? (x) => x.Value);

        ExtensionCallParts parts = ExtensionCalls.ReadParts(arguments);

        Assert.Equal((extension, command), (parts.Extension, parts.Command));
        Assert.Equal(("browser", "snapshot"),
            ExtensionCalls.ReadHelpTarget(new Dictionary<string, object?> { ["name"] = "browser.snapshot" }));
    }

    /// <summary>server 名里的点号在模型那边换成下划线，全名才切得开；模型写回来时认得</summary>
    [Fact]
    public void McpServerWithDotInName_IsReachableByItsModelName()
    {
        McpServerConfig server = new() { Name = "com.unity.mcp" };

        McpServerLookup lookup = McpServerFinder.Find([server], [], _ => true, ExtensionCalls.ModelName(server.Name));

        Assert.Equal("com_unity_mcp", ExtensionCalls.ModelName(server.Name));
        Assert.Same(server, lookup.Server);
    }

    /// <summary>参数名猜错（ref/text）时，除了缺什么，还要说哪些不认识、正确签名是什么</summary>
    [Fact]
    public async Task Call_WrongParameterNames_ListsUnknownOnesAndTheSignature()
    {
        string text = (string)(await Hub(Echo()).CallAsync("audio", "transcribe", Args(new { file = "a.wav" }),
            TestContext.Current.CancellationToken))!;

        Assert.Contains("缺少 path；未知：file", text);
        Assert.Contains("参数：{path, subtitles?}", text);
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
    [InlineData("worktree", "status", EExtensionRisk.ReadOnly)] //纯查询
    [InlineData("worktree", "list", EExtensionRisk.ReadOnly)] //纯查询
    [InlineData("worktree", "reset", EExtensionRisk.ReadOnly)] //已临时注释(2026-10-24),未知命令什么也不会跑
    [InlineData("worktree", "create", EExtensionRisk.FullAutoOnly)] //隔离副本不影响主工作区，全自动档免审批
    [InlineData("worktree", "set", EExtensionRisk.PerScope)] //登记任意已有目录为免审批，要求额外审批
    [InlineData("worktree", "sync", EExtensionRisk.Standard)] //改工作树/子模块，会动文件
    [InlineData("worktree", "remove", EExtensionRisk.PerScope)] //删目录，要求额外审批
    [InlineData("worktree", "frobnicate", EExtensionRisk.ReadOnly)] //没这条命令,什么也不会跑
    [InlineData("chatsession", "list", EExtensionRisk.ReadOnly)] //纯查询
    [InlineData("chatsession", "list-groups", EExtensionRisk.ReadOnly)] //纯查询（在跑时顺带写一份流水快照）
    [InlineData("chatsession", "create-group", EExtensionRisk.Standard)] //建一份新群，要问
    [InlineData("chatsession", "list-models", EExtensionRisk.ReadOnly)] //纯查询
    [InlineData("chatsession", "list-characters", EExtensionRisk.ReadOnly)] //纯查询
    [InlineData("chatsession", "stop", EExtensionRisk.Standard)] //取消一轮并写收尾历史,要问
    [InlineData("chatsession", "join", EExtensionRisk.ReadOnly)] //没这条命令,什么也不会跑
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

    //================= worktree 审批档 =================

    /// <summary>create 跟随全自动档：FullAuto 直接放行，其余档要审批</summary>
    [Theory]
    [InlineData(EAgentPermissionMode.FullAuto, true)]
    [InlineData(EAgentPermissionMode.AutoEdit, false)]
    [InlineData(EAgentPermissionMode.ReadOnly, false)]
    public async Task WorktreeCreate_FollowsFullAutoOnly(EAgentPermissionMode mode, bool expected)
    {
        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> rules =
            ApprovalModeMapper.BuildRules(mode, "/tmp/uiharu-ws");

        Assert.Equal(expected,
            await ApprovalRuleProbe.IsApprovedAsync(rules, ExtensionCallOf.Make("worktree", "create")));
    }

    /// <summary>create 非全自动档按 Standard：本会话放行过 worktree 后也不再问</summary>
    [Fact]
    public async Task WorktreeCreate_OutsideFullAuto_HonorsSessionApproval()
    {
        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> rules =
            ApprovalModeMapper.BuildRules(EAgentPermissionMode.AutoEdit, "/tmp/uiharu-ws",
                sessionExtensionApprovalSource: () => ["worktree"]);

        Assert.True(await ApprovalRuleProbe.IsApprovedAsync(rules, ExtensionCallOf.Make("worktree", "create")));
    }

    /// <summary>remove/set 是逐项：放行 create（记在 worktree 整格）不连带它们；单独放行各自那格才通过</summary>
    [Fact]
    public async Task WorktreeRemoveSet_RequireTheirOwnApprovalSlot()
    {
        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> createOnly =
            ApprovalModeMapper.BuildRules(EAgentPermissionMode.AutoEdit, "/tmp/uiharu-ws",
                sessionExtensionApprovalSource: () => ["worktree"]);

        // 只放行了 worktree 整格（create 会记在这格）：remove/set 不被连带
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(createOnly, ExtensionCallOf.Make("worktree", "remove")));
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(createOnly, ExtensionCallOf.Make("worktree", "set")));

        // 单独放行各自那格才通过
        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> removeOnly =
            ApprovalModeMapper.BuildRules(EAgentPermissionMode.AutoEdit, "/tmp/uiharu-ws",
                sessionExtensionApprovalSource: () => ["worktree:remove"]);
        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> setOnly =
            ApprovalModeMapper.BuildRules(EAgentPermissionMode.AutoEdit, "/tmp/uiharu-ws",
                sessionExtensionApprovalSource: () => ["worktree:set"]);
        Assert.True(await ApprovalRuleProbe.IsApprovedAsync(removeOnly, ExtensionCallOf.Make("worktree", "remove")));
        Assert.True(await ApprovalRuleProbe.IsApprovedAsync(setOnly, ExtensionCallOf.Make("worktree", "set")));
        // 放行 remove 那格不连带 set，反之亦然
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(removeOnly, ExtensionCallOf.Make("worktree", "set")));
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(setOnly, ExtensionCallOf.Make("worktree", "remove")));
    }

    //================= 注册表 =================

    /// <summary>Catalog 是唯一的登记处：六个可见扩展 + 隐藏的目标模式；按名取回都要走得到</summary>
    [Fact]
    public void Catalog_RegistersEveryVisibleExtension_AndResolvesNames()
    {
        Assert.Equal(["browser", "desktop", "image", "audio", "worktree", "chatsession"], BuiltinExtensions.Names);
        Assert.Equal(7, BuiltinExtensions.Catalog.Count); //含隐藏的目标模式
        Assert.True(BuiltinExtensions.Catalog.Single(x => x.IsHidden).Name == BuiltinExtensions.Goal);
        Assert.Equal("browser", BuiltinExtensions.Canonical("BROWSER")); //大小写都归一到规范写法
        Assert.Equal("snapshot", BuiltinExtensions.CanonicalCommand("browser", "BrowserSnapshot")); //旧工具名也认
        Assert.Null(BuiltinExtensions.Canonical("unity"));
        Assert.Equal(string.Empty, BuiltinExtensions.Summary("nope"));
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

        IReadOnlyList<(string Name, string Description)> roster = ExtensionRoster.Of(config, mcp, EAgentToolScope.Main);

        Assert.Equal(["browser", "desktop", "audio", "worktree", "chatsession", "unity"], roster.Select(x => x.Name));
        Assert.Equal(string.Empty, ExtensionRoster.Section(
            new AgentToolConfig { DisabledExtensions = [..BuiltinExtensions.Names] }, McpToolSet.Empty, EAgentToolScope.Main));
    }

    /// <summary>worktree 只进挂它的场合名单：主代理/子代理/群成员/化身。
    /// 群成员与化身要建自己的树：群里有人离席、化身值守时，越界写入被化身硬拒，有自己的 worktree 才有地方改</summary>
    [Theory]
    [InlineData(EAgentToolScope.Main, true)]
    [InlineData(EAgentToolScope.SubAgent, true)]
    [InlineData(EAgentToolScope.GroupMember, true)]
    [InlineData(EAgentToolScope.GroupAvatar, true)]
    [InlineData(EAgentToolScope.ScheduledRun, false)]
    [InlineData(EAgentToolScope.Brain, false)]
    public void Roster_WorktreeOnlyWhereItIsMounted(EAgentToolScope scope, bool included)
    {
        IReadOnlyList<(string Name, string Description)> roster = ExtensionRoster.Of(new AgentToolConfig(), McpToolSet.Empty, scope);

        Assert.Equal(included, roster.Any(x => x.Name == BuiltinExtensions.Worktree));
    }

    /// <summary>chatsession 只进能派出子代理、会话持久的场合：主代理/群成员/化身。
    /// 子代理与脑拿的是任务书、不往下委派；定时任务跑完就收工——都没有自己的委派可查</summary>
    [Theory]
    [InlineData(EAgentToolScope.Main, true)]
    [InlineData(EAgentToolScope.GroupMember, true)]
    [InlineData(EAgentToolScope.GroupAvatar, true)]
    [InlineData(EAgentToolScope.SubAgent, false)]
    [InlineData(EAgentToolScope.ScheduledRun, false)]
    [InlineData(EAgentToolScope.Brain, false)]
    public void Roster_ChatSessionOnlyWhereItCanHaveSubAgents(EAgentToolScope scope, bool included)
    {
        IReadOnlyList<(string Name, string Description)> roster = ExtensionRoster.Of(new AgentToolConfig(), McpToolSet.Empty, scope);

        Assert.Equal(included, roster.Any(x => x.Name == BuiltinExtensions.ChatSession));
    }

    /// <summary>旧档案的 MCP 禁用名单读进新名单，写出时不再带旧字段</summary>
    [Fact]
    public void LegacyDisabledMcpServers_AreReadIntoDisabledExtensions()
    {
        AgentToolConfig config = JsonSerializer.Deserialize<AgentToolConfig>(
            """{"EnableBrowser":true,"DisabledMcpServers":["unity"]}""", SaveUtility.SaveJsonOptions)!;

        Assert.Equal(["unity"], config.DisabledExtensions);
        string json = JsonSerializer.Serialize(config, SaveUtility.SaveJsonOptions);
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
        BuiltinExtension browser = BrowserExtension.Spec.Assemble(Resources(browser: PuppeteerBrowserHost.Shared),
            EReadImageMode.None, mountFiles: true);

        object? result = await Hub(browser).CallAsync("browser", command, null, TestContext.Current.CancellationToken);

        Assert.Contains("这条命令", Assert.IsType<string>(result));
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

    /// <summary>
    /// 整个调用套进 arguments 的写法：执行与审批都拆出来按同一次调用认，结果里提醒写法
    /// </summary>
    [Fact]
    public async Task CallNestedInArguments_IsUnwrapped_ForBothRunAndApproval()
    {
        AIFunction call = (AIFunction)ExtensionMetaTools.Create(Hub(Echo())).Single(x => x.Name == ExtensionCalls.CallName);
        Dictionary<string, object?> nested = new()
        {
            ["arguments"] = JsonSerializer.SerializeToElement(new
            {
                extension = "audio", command = "transcribe", arguments = new { path = "a.wav" },
            }),
        };

        string text = Assert.IsType<string>(await call.InvokeAsync(new AIFunctionArguments(nested),
            TestContext.Current.CancellationToken));
        Assert.StartsWith("（注意：", text);
        Assert.Contains("heard a.wav", text);

        Assert.True(ExtensionCalls.TryRead(new FunctionCallContent("c1", ExtensionCalls.CallName, nested),
            out ExtensionInvocation? invocation));
        Assert.Equal(("audio", "transcribe", "a.wav"),
            (invocation.Extension, invocation.Command, invocation.Arguments!["path"].GetString()));
    }

    /// <summary>内层没有 arguments 时，除扩展名、命令名之外的字段就是参数；只有参数没有命令的照常报错</summary>
    [Fact]
    public void ReadParts_FlatNestedCall_TakesTheRestAsArguments_AndLeavesBareArgumentsAlone()
    {
        ExtensionCallParts flat = ExtensionCalls.ReadParts(new Dictionary<string, object?>
        {
            ["arguments"] = JsonSerializer.SerializeToElement(new { extension = "desktop", command = "act", window = "w1" }),
        });
        Assert.True(flat.Unwrapped);
        Assert.Equal(("desktop", "act"), (flat.Extension, flat.Command));
        Assert.Equal(["window"], flat.Arguments!.Keys);

        ExtensionCallParts bare = ExtensionCalls.ReadParts(new Dictionary<string, object?>
        {
            ["arguments"] = JsonSerializer.SerializeToElement(new { window = "w1" }),
        });
        Assert.False(bare.Unwrapped);
        Assert.Null(bare.Extension);
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

        Assert.Contains("什么都没运行", Assert.IsType<string>(result));
    }

    /// <summary>命令跑起来出的错不说成「参数不对」：那会诱导模型换参数重试，可能白花钱</summary>
    [Fact]
    public async Task FailuresWhileRunning_AreNotReportedAsBadArguments()
    {
        BuiltinExtension failing = new StubExtension(BuiltinExtensions.Audio, "g",
            [new ExtensionCommand(BuiltinExtensions.AudioTranscribe, AIFunctionFactory.Create(
                string (string path) => throw new InvalidOperationException("Audio stream error"), TranscribeAudioTool.ToolName))]);

        string result = Assert.IsType<string>(await Hub(failing).CallAsync("audio", "transcribe", Args(new { path = "a" }),
            TestContext.Current.CancellationToken));

        Assert.Equal("audio.transcribe 失败：Audio stream error", result);
    }
}
