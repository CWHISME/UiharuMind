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
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 按需 MCP 的核心（查与调）。用假宿主，不拉起任何 server——
/// 关心的是：失败都是返回值、离线时读缓存、结果整形、以及"为什么用不了"说得清楚。
/// </summary>
public class McpBridgeTests : IDisposable
{
    private readonly string _spillDirectory = Path.Combine(Path.GetTempPath(), $"mcp-bridge-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_spillDirectory)) Directory.Delete(_spillDirectory, recursive: true);
    }

    /// 贴近真实 MCP 工具：框架的包装把单个文本块原样交出去，而不是序列化成 JSON 字符串
    private static AIFunction Tool(string name, Func<object?> result, string description = "does a thing") =>
        new TextTool(name, description, () => result() is string text ? new TextContent(text) : result());

    private McpBridge Bridge(FakeHost host, params string[] disabled) =>
        new(host, "/ws", disabled, _spillDirectory);

    //================= Help =================

    [Fact]
    public async Task Help_WithoutTool_ListsToolsAndInstructions()
    {
        FakeHost host = FakeHost.Connected("github", "Use for GitHub.",
            Tool("search_code", () => "x", "Search code in repositories.\nSecond line."), Tool("list_issues", () => "x"));

        string text = await Bridge(host).HelpAsync("github", null, TestContext.Current.CancellationToken);

        Assert.Contains("Use for GitHub.", text);
        Assert.Contains("- search_code {}: Search code in repositories.", text);
        Assert.DoesNotContain("Second line", text); //完整描述留给带 tool 的那一层
        Assert.Contains("- list_issues", text);
    }

    [Fact]
    public async Task Help_WithTool_ReturnsFullDescriptionAndSchema()
    {
        FakeHost host = FakeHost.Connected("github", "", Tool("search_code", () => "x", "Search code.\nMore detail."));

        string text = await Bridge(host).HelpAsync("github", "search_code", TestContext.Current.CancellationToken);

        Assert.Contains("# github.search_code", text);
        Assert.Contains("More detail.", text);
        Assert.Contains("参数（JSON Schema）", text);
    }

    [Fact]
    public async Task Help_ToolName_MatchesIgnoringCase()
    {
        FakeHost host = FakeHost.Connected("github", "", Tool("search_code", () => "x"));

        string text = await Bridge(host).HelpAsync("GitHub", "Search_Code", TestContext.Current.CancellationToken);

        Assert.Contains("# github.search_code", text);
    }

    [Fact]
    public async Task Help_UnknownTool_ListsAvailableNames()
    {
        FakeHost host = FakeHost.Connected("github", "", Tool("search_code", () => "x"));

        string text = await Bridge(host).HelpAsync("github", "nope", TestContext.Current.CancellationToken);

        Assert.Contains("没有可调用的工具 'nope'", text);
        Assert.Contains("search_code", text);
    }

    /// <summary>server 离线时读磁盘缓存：模型至少看得见"有什么"，再决定要不要等</summary>
    [Fact]
    public async Task Help_Offline_FallsBackToCachedCatalog()
    {
        FakeHost host = FakeHost.Offline("github", "connection refused");
        host.Catalog = new McpToolCatalog
        {
            Instructions = "Cached notes.",
            Tools = [new McpToolDescriptor { Name = "search_code", Description = "Search code." }],
            CapturedUtc = new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc),
        };

        string text = await Bridge(host).HelpAsync("github", null, TestContext.Current.CancellationToken);

        Assert.Contains("未连接（connection refused）", text);
        Assert.Contains("2026-09-01 08:30 UTC", text);
        Assert.Contains("Cached notes.", text);
        Assert.Contains("- search_code", text);
    }

    [Fact]
    public async Task Help_OfflineWithoutCatalog_SaysSoInsteadOfThrowing()
    {
        FakeHost host = FakeHost.Offline("github", "timed out");

        string text = await Bridge(host).HelpAsync("github", null, TestContext.Current.CancellationToken);

        Assert.Contains("未连接（timed out）", text);
        Assert.Contains("工具清单缓存", text);
    }

    [Fact]
    public async Task Help_HugeToolList_IsSpilledToTheRoom()
    {
        AIFunction[] tools = Enumerable.Range(0, 3000)
            .Select(i => Tool($"tool_{i}", () => "x", new string('d', 80))).ToArray();
        FakeHost host = FakeHost.Connected("github", "", tools);

        string text = await Bridge(host).HelpAsync("github", null, TestContext.Current.CancellationToken);

        Assert.Contains("已截断", text);
        Assert.Single(Directory.GetFiles(_spillDirectory, $"{ExtensionCalls.HelpName}_github_*.txt"));
    }

    /// <summary>
    /// 实机数字：Unity MCP 全开 73 个工具，清单约 12.8KB。McpHelp 是发现工具的唯一入口，
    /// 按 MCP 结果的 8KB 会让中间近三十个工具消失，所以它单独一档、能装下这个量级
    /// </summary>
    [Fact]
    public async Task Help_SeventyThreeRealisticTools_AreListedInFullWithoutTruncation()
    {
        AIFunction[] tools = Enumerable.Range(0, 73)
            .Select(i => Tool($"category-tool-name-{i:D2}", () => "x", new string('d', 150))).ToArray();
        FakeHost host = FakeHost.Connected("unity", "", tools);

        string text = await Bridge(host).HelpAsync("unity", null, TestContext.Current.CancellationToken);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(text) > ToolOutputBudget.Compact.MaxBytes,
            "这个量级本来就会超过 MCP 结果的 8KB");
        Assert.DoesNotContain("已截断", text);
        Assert.Contains("category-tool-name-00", text);
        Assert.Contains("category-tool-name-36", text); //中间的也在
        Assert.Contains("category-tool-name-72", text);
        Assert.Empty(Directory.Exists(_spillDirectory) ? Directory.GetFiles(_spillDirectory) : []);
    }

    //================= 查找失败要说清楚 =================

    [Theory]
    [InlineData("DisabledByCharacter", "被本智能体禁用")]
    [InlineData("NeedsApproval", "尚未经用户授权")]
    [InlineData("HostingOff", "在应用的设置里被关掉了")]
    [InlineData("NotFound", "没有叫")]
    public async Task Refusals_ExplainWhyTheServerCannotBeUsed(string statusName, string expected)
    {
        EMcpLookupStatus status = Enum.Parse<EMcpLookupStatus>(statusName);
        FakeHost host = new() { Lookup = new McpServerLookup(status, null, ["unity", "github"]) };
        McpBridge bridge = Bridge(host);

        string help = await bridge.HelpAsync("x", null, TestContext.Current.CancellationToken);
        object call = await bridge.CallAsync("x", "t", null, TestContext.Current.CancellationToken);

        Assert.Contains(expected, help);
        Assert.Contains(expected, Assert.IsType<string>(call));
        Assert.Equal(0, host.ConnectCount); //拒绝之前不该为它起连接
    }

    [Fact]
    public async Task UnknownServer_PointsToTheFullList()
    {
        FakeHost host = new() { Lookup = new McpServerLookup(EMcpLookupStatus.NotFound, null, ["unity", "github"]) };

        string text = await Bridge(host).HelpAsync("gitlab", null, TestContext.Current.CancellationToken);

        Assert.Contains($"{ExtensionCalls.HelpName}（不带参数）", text);
    }

    [Fact]
    public async Task MissingParameters_AreReportedNotThrown()
    {
        McpBridge bridge = Bridge(new FakeHost());

        Assert.Contains("`name` 必填", await bridge.HelpAsync(" ", null, TestContext.Current.CancellationToken));
        Assert.Contains("`name` 必填",
            Assert.IsType<string>(await bridge.CallAsync(null, "t", null, TestContext.Current.CancellationToken)));
        Assert.Contains("`name` 必填",
            Assert.IsType<string>(await bridge.CallAsync("github", "", null, TestContext.Current.CancellationToken)));
    }

    //================= Call =================

    [Fact]
    public async Task Call_ReturnsToolResultText()
    {
        FakeHost host = FakeHost.Connected("github", "", Tool("ping", () => "pong"));

        object result = await Bridge(host).CallAsync("github", "ping", null, TestContext.Current.CancellationToken);

        Assert.Equal("pong", result);
    }

    [Fact]
    public async Task Call_PassesArgumentsThrough()
    {
        object? received = null;
        AIFunction echo = AIFunctionFactory.Create((string query) =>
        {
            received = query;
            return "ok";
        }, "search", "search");
        FakeHost host = FakeHost.Connected("github", "", echo);
        Dictionary<string, JsonElement> args = new() { ["query"] = JsonSerializer.SerializeToElement("needle") };

        await Bridge(host).CallAsync("github", "search", args, TestContext.Current.CancellationToken);

        Assert.Equal("needle", received);
    }

    /// <summary>server 没连上是一条工具结果（只追加、不改前缀），不是异常</summary>
    [Fact]
    public async Task Call_NotConnected_ReturnsMessageWithReason()
    {
        FakeHost host = FakeHost.Offline("github", "still connecting after 10s");

        object result = await Bridge(host).CallAsync("github", "ping", null, TestContext.Current.CancellationToken);

        string text = Assert.IsType<string>(result);
        Assert.Contains("未连接：still connecting after 10s", text);
    }

    [Fact]
    public async Task Call_UnknownTool_ListsAvailableNames()
    {
        FakeHost host = FakeHost.Connected("github", "", Tool("ping", () => "pong"));

        string text = Assert.IsType<string>(await Bridge(host).CallAsync("github", "pnig", null, TestContext.Current.CancellationToken));

        Assert.Contains("没有可调用的工具 'pnig'", text);
        Assert.Contains("ping", text);
    }

    /// <summary>快照里没有 ≠ 真没有：server 运行中可能多出了工具，点名时重取一次再判断</summary>
    [Fact]
    public async Task Call_MissingTool_RefreshesOnceAndFindsLateArrival()
    {
        FakeHost host = FakeHost.Connected("github", "", Tool("ping", () => "pong"));
        host.AfterRefresh = new McpServerConnection
        {
            IsConnected = true, Tools = [Tool("ping", () => "pong"), Tool("late", () => "arrived")],
        };

        object result = await Bridge(host).CallAsync("github", "late", null, TestContext.Current.CancellationToken);

        Assert.Equal("arrived", result);
        Assert.Equal(1, host.RefreshCount);
    }

    [Fact]
    public async Task Call_TrulyMissingTool_RefreshesOnceThenReportsIt()
    {
        FakeHost host = FakeHost.Connected("github", "", Tool("ping", () => "pong"));

        string text = Assert.IsType<string>(
            await Bridge(host).CallAsync("github", "nope", null, TestContext.Current.CancellationToken));

        Assert.Contains("没有可调用的工具 'nope'", text);
        Assert.Equal(1, host.RefreshCount);
    }

    [Fact]
    public async Task Call_FoundTool_NeverRefreshes()
    {
        FakeHost host = FakeHost.Connected("github", "", Tool("ping", () => "pong"));

        await Bridge(host).CallAsync("github", "ping", null, TestContext.Current.CancellationToken);

        Assert.Equal(0, host.RefreshCount);
    }

    [Fact]
    public async Task Help_MissingTool_RefreshesAndFindsLateArrival()
    {
        FakeHost host = FakeHost.Connected("github", "", Tool("ping", () => "pong"));
        host.AfterRefresh = new McpServerConnection
        {
            IsConnected = true, Tools = [Tool("ping", () => "pong"), Tool("late", () => "x", "Arrived late.")],
        };

        string text = await Bridge(host).HelpAsync("github", "late", TestContext.Current.CancellationToken);

        Assert.Contains("# github.late", text);
        Assert.Equal(1, host.RefreshCount);
    }

    [Fact]
    public async Task Help_MissingToolWhileOffline_DoesNotTryToRefresh()
    {
        FakeHost host = FakeHost.Offline("github", "down");
        host.Catalog = new McpToolCatalog { Tools = [new McpToolDescriptor { Name = "ping" }] };

        await Bridge(host).HelpAsync("github", "nope", TestContext.Current.CancellationToken);

        Assert.Equal(0, host.RefreshCount);
    }

    /// <summary>
    /// 实机踩到的：模型拿 server 自带"列出工具"（含被关掉的）里的名字去调，并行发出两个。
    /// 工具多时报错不倒整份清单，也<b>不硬凑"相似"</b>——只因含 get 这种通用词的建议纯属误导，
    /// 真实原因是这个工具没开放，报错要明说这一点并叫模型别重试
    /// </summary>
    [Fact]
    public async Task Call_MissingToolOnBigServer_DoesNotDumpTheListOrInventSimilarOnes()
    {
        string[] names =
        [
            "assets-get-data", "assets-modify", "console-get-logs", "gameobject-create", "gameobject-find",
            "object-get-data", "scene-save", "script-execute", "tests-run", "unity-tool-list",
        ];
        FakeHost host = FakeHost.Connected("unity", "", names.Select(x => Tool(x, () => "ok")).ToArray());

        string text = Assert.IsType<string>(await Bridge(host).CallAsync("unity", "editor-application-get-state",
            null, TestContext.Current.CancellationToken));

        Assert.Contains("没有可调用的工具 'editor-application-get-state'", text);
        Assert.Contains("共暴露 10 个", text);
        Assert.DoesNotContain("相似的可调用命令", text); //只因都含 get 的不算相似
        Assert.Contains("在 server 端被关掉了", text);
        Assert.Contains("不要重试，告知用户到 server 端开启", text);
        Assert.DoesNotContain("tests-run", text);
    }

    /// <summary>大小写、kebab/snake 写混了：直接当成同一个工具，不必让模型多绕一轮</summary>
    [Theory]
    [InlineData("Game_Object-Create")]
    [InlineData("gameobject_create")]
    [InlineData("GAMEOBJECT-CREATE")]
    public async Task Call_ToolNameWithDifferentCaseOrSeparators_StillResolves(string typed)
    {
        FakeHost host = FakeHost.Connected("unity", "", Tool("gameobject-create", () => "created"));

        object result = await Bridge(host).CallAsync("unity", typed, null, TestContext.Current.CancellationToken);

        Assert.Equal("created", result);
    }

    /// <summary>两个工具去掉分隔符后撞成同一个名字时不替模型挑：宁可报未命中</summary>
    [Fact]
    public async Task Call_AmbiguousLooseName_IsNotGuessed()
    {
        FakeHost host = FakeHost.Connected("unity", "", Tool("a-b", () => "1"), Tool("a_b", () => "2"),
            Tool("ab", () => "3"));

        string text = Assert.IsType<string>(
            await Bridge(host).CallAsync("unity", "A.B", null, TestContext.Current.CancellationToken));

        Assert.Contains("没有可调用的工具 'A.B'", text);
    }

    /// <summary>真正的拼写近似（差一两个字母）才建议</summary>
    [Theory]
    [InlineData("gameobject-creat")]
    [InlineData("gameobject-craete")]
    public async Task Call_MisspelledTool_SuggestsTheRealOne(string typed)
    {
        string[] names = ["assets-modify", "console-get-logs", "gameobject-create", "gameobject-find", "scene-save",
            "script-execute", "tests-run", "unity-tool-list", "object-get-data", "assets-find"];
        FakeHost host = FakeHost.Connected("unity", "", names.Select(x => Tool(x, () => "ok")).ToArray());

        string text = Assert.IsType<string>(
            await Bridge(host).CallAsync("unity", typed, null, TestContext.Current.CancellationToken));

        Assert.Contains("相似的可调用命令：gameobject-create", text);
    }

    [Fact]
    public async Task Call_MissingToolWithNothingSimilar_OnlyPointsToTheHelp()
    {
        FakeHost host = FakeHost.Connected("unity",
            "", Enumerable.Range(0, 12).Select(i => Tool($"alpha-{i}", () => "ok")).ToArray());

        string text = Assert.IsType<string>(await Bridge(host).CallAsync("unity", "zzz", null,
            TestContext.Current.CancellationToken));

        Assert.DoesNotContain("相似的可调用命令", text);
        Assert.Contains(ExtensionCalls.HelpName, text);
    }

    [Fact]
    public async Task Call_ToolThrows_BecomesFailureText()
    {
        AIFunction boom = AIFunctionFactory.Create((Func<string>)(() => throw new InvalidOperationException("kaput")),
            "boom", "boom");
        FakeHost host = FakeHost.Connected("github", "", boom);

        string text = Assert.IsType<string>(await Bridge(host).CallAsync("github", "boom", null, TestContext.Current.CancellationToken));

        Assert.Contains("github.boom 失败", text);
        Assert.Contains("kaput", text);
    }

    /// <summary>传输层故障（server 进程死、管道断）：丢连接重连一次并重试——协议/业务错误不这么走</summary>
    [Fact]
    public async Task Call_ConnectionDeath_ReconnectsOnceAndRetries()
    {
        int calls = 0;
        FakeHost host = FakeHost.Connected("github", "", Tool("ping", () =>
        {
            //第一笔:连接刚死,冒成 IOException;重连落地后同一工具活着
            return Interlocked.Increment(ref calls) == 1
                ? throw new IOException("pipe is closed")
                : "pong";
        }));
        host.AfterReconnect = new McpServerConnection { IsConnected = true, Tools = host.Connection.Tools };

        object? result = await Bridge(host).CallAsync("github", "ping", null, TestContext.Current.CancellationToken);

        Assert.Equal("pong", result);
        Assert.Equal(1, host.ReconnectCount);
        Assert.Equal(2, Interlocked.CompareExchange(ref calls, 0, 0));
    }

    /// <summary>有意的对照：普通业务错误（server 正常回话）不触发重连，只如实报错</summary>
    [Fact]
    public async Task Call_BusinessError_DoesNotReconnect()
    {
        int calls = 0;
        FakeHost host = FakeHost.Connected("github", "", Tool("boomer", () =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("kaput");
        }));

        string text = Assert.IsType<string>(await Bridge(host).CallAsync("github", "boomer", null, TestContext.Current.CancellationToken));

        Assert.Contains("github.boomer 失败", text);
        Assert.Equal(0, host.ReconnectCount);
        Assert.Equal(1, Interlocked.CompareExchange(ref calls, 0, 0));
    }

    [Fact]
    public async Task Call_ReconnectFails_ReportsBothErrors()
    {
        FakeHost host = FakeHost.Connected("github", "", Tool("ping", () => throw new IOException("pipe is closed")));
        host.AfterReconnect = new McpServerConnection { IsConnected = false, Error = "no route to host" };

        string text = Assert.IsType<string>(await Bridge(host).CallAsync("github", "ping", null, TestContext.Current.CancellationToken));

        Assert.Contains("pipe is closed", text);
        Assert.Contains("no route to host", text);
        Assert.Contains("不要用自己写的脚本", text);
        Assert.Equal(1, host.ReconnectCount);
    }

    /// <summary>重连连上了、但这个工具已不在新清单里:报未知工具,不拿挂在死 client 上的旧句柄重试</summary>
    [Fact]
    public async Task Call_ReconnectSucceedsButToolGone_ReportsUnknownTool()
    {
        FakeHost host = FakeHost.Connected("github", "", Tool("ping", () => throw new IOException("pipe is closed")));
        host.AfterReconnect = new McpServerConnection { IsConnected = true, Tools = [Tool("other", () => "x")] };

        string text = Assert.IsType<string>(await Bridge(host).CallAsync("github", "ping", null, TestContext.Current.CancellationToken));

        Assert.Contains("没有可调用的工具 'ping'", text);
        Assert.Equal(1, host.ReconnectCount);
    }

    /// <summary>判据的钉桩:传输层链(new IOException 包在里层)算"连接死了";协议/普通业务错误不是。
    /// SDK 的 <c>ClientTransportClosedException</c> 自身就是 <c>IOException</c> 子类,天然被这条覆盖</summary>
    [Fact]
    public void ConnectionFailureJudgement_SplitsTransportFromProtocol()
    {
        var transport = new InvalidOperationException("outer", new IOException("broken pipe"));
        var protocol = new InvalidOperationException("outer", new ModelContextProtocol.McpException("tool blew up"));
        var disposed = new InvalidOperationException("outer", new ObjectDisposedException("client"));
        // 裸的套接字错(SocketException 继承 Win32Exception,不是 IOException):连接没通,算连接死
        var socket = new InvalidOperationException("outer",
            new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused));
        // 工具自己要读的文件不在:是业务错误,认成连接死会白重启 server 并把工具重跑一遍
        var businessIo = new InvalidOperationException("outer", new FileNotFoundException("nope.txt"));
        // 协议错误压着传输异常:server 有回话,仍按业务错误算
        var protocolOverTransport = new ModelContextProtocol.McpException("tool blew up", new IOException("pipe"));
        // HTTP:连都没连上(拿不到状态码)算连接死;4xx 是业务/权限错,不算;5xx/超时算
        var httpNoStatus = new HttpRequestException("connection refused");
        var httpBadRequest = new HttpRequestException("business", null, System.Net.HttpStatusCode.BadRequest);
        var httpUnauthorized = new HttpRequestException("auth", null, System.Net.HttpStatusCode.Unauthorized);
        var httpServerError = new HttpRequestException("boom", null, System.Net.HttpStatusCode.BadGateway);

        Assert.True(McpBridge.IsConnectionFailure(transport));
        Assert.True(McpBridge.IsConnectionFailure(disposed));
        Assert.True(McpBridge.IsConnectionFailure(socket));
        Assert.True(McpBridge.IsConnectionFailure(httpNoStatus));
        Assert.True(McpBridge.IsConnectionFailure(httpServerError));
        Assert.False(McpBridge.IsConnectionFailure(protocol));
        Assert.False(McpBridge.IsConnectionFailure(businessIo));
        Assert.False(McpBridge.IsConnectionFailure(protocolOverTransport));
        Assert.False(McpBridge.IsConnectionFailure(httpBadRequest));
        Assert.False(McpBridge.IsConnectionFailure(httpUnauthorized));
        Assert.False(McpBridge.IsConnectionFailure(new NotSupportedException()));
    }

    [Fact]
    public async Task Call_Cancellation_Propagates()
    {
        FakeHost host = FakeHost.Connected("github", "", Tool("ping", () => "pong"));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        host.ThrowOnCancelledConnect = true;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Bridge(host).CallAsync("github", "ping", null, cancellation.Token));
    }

    [Fact]
    public async Task Call_HugeResult_SpillsToRoomAndKeepsHeadAndTail()
    {
        string huge = string.Join('\n', Enumerable.Range(0, 20000).Select(i => $"row {i} {new string('x', 20)}"));
        FakeHost host = FakeHost.Connected("github", "", Tool("dump", () => huge));

        string text = Assert.IsType<string>(await Bridge(host).CallAsync("github", "dump", null, TestContext.Current.CancellationToken));

        Assert.Contains("row 0 ", text);
        Assert.Contains("row 19999 ", text); //尾部
        Assert.Contains("全文存于", text);
        string saved = Assert.Single(Directory.GetFiles(_spillDirectory, $"{ExtensionCalls.CallName}_github_dump_*.txt"));
        Assert.Equal(huge, File.ReadAllText(saved));
    }

    private sealed class TextTool : AIFunction
    {
        private static readonly JsonElement Schema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();
        private readonly Func<object?> _result;

        public TextTool(string name, string description, Func<object?> result)
        {
            Name = name;
            Description = description;
            _result = result;
        }

        public override string Name { get; }
        public override string Description { get; }
        public override JsonElement JsonSchema => Schema;

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments,
            CancellationToken cancellationToken) => ValueTask.FromResult(_result());
    }

    // ---- 假宿主 ----

    private sealed class FakeHost : IMcpServerHost
    {
        public McpServerLookup Lookup { get; set; } = new(EMcpLookupStatus.NotFound, null, []);
        public McpServerConnection Connection { get; set; } = new() { IsConnected = false, Error = "unset" };
        public McpToolCatalog? Catalog { get; set; }
        public bool ThrowOnCancelledConnect { get; set; }
        public int ConnectCount { get; private set; }

        public static FakeHost Connected(string name, string instructions, params AIFunction[] tools) => new()
        {
            Lookup = new McpServerLookup(EMcpLookupStatus.Found, new McpServerConfig { Name = name }, [name]),
            Connection = new McpServerConnection { IsConnected = true, Tools = tools, Instructions = instructions },
        };

        public static FakeHost Offline(string name, string error) => new()
        {
            Lookup = new McpServerLookup(EMcpLookupStatus.Found, new McpServerConfig { Name = name }, [name]),
            Connection = new McpServerConnection { IsConnected = false, Error = error },
        };

        public McpServerLookup Find(string? workspacePath, IEnumerable<string>? disabledServers, string serverName) =>
            Lookup;

        public Task<McpServerConnection> ConnectAsync(McpServerConfig server, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ConnectCount++;
            if (ThrowOnCancelledConnect) cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Connection);
        }

        public McpServerConnection? AfterRefresh { get; set; }
        public int RefreshCount { get; private set; }

        public Task<McpServerConnection> RefreshToolsAsync(McpServerConfig server, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            RefreshCount++;
            Connection = AfterRefresh ?? Connection;
            return Task.FromResult(Connection);
        }

        public McpToolCatalog? LoadCatalog(McpServerConfig server) => Catalog;

        public IReadOnlyList<McpServerBrief> List(string? workspacePath, IEnumerable<string>? disabledServers) => [];

        public IReadOnlyList<McpServerBrief> Overview(string? workspacePath, IEnumerable<string>? disabledServers) =>
            OverviewServers;

        public IReadOnlyList<McpServerBrief> OverviewServers { get; set; } = [];

        public int ReconnectCount { get; private set; }
        public McpServerConnection? AfterReconnect { get; set; }

        public Task<McpServerConnection> ReconnectAsync(McpServerConfig server, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ReconnectCount++;
            Connection = AfterReconnect ?? new McpServerConnection { IsConnected = false, Error = "reconnect failed" };
            return Task.FromResult(Connection);
        }
    }
}
