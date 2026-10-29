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
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Mcp;

namespace UiharuMind.Core.Tests.Execution.Extensions;

/// <summary>
/// mcp 扩展（ADR 0096）：一台可用的 server 都没有时，模型也得知道 MCP 这回事，
/// 并且能经 <c>mcp.list</c> 看清「配了什么、被哪道闸挡着」，经 <c>mcp.connect</c> 现连一次。
/// 全程假宿主，不拉起任何进程。
/// </summary>
public class McpExtensionTests : IDisposable
{
    private static readonly AgentPathResolver Paths = new("/tmp/uiharu-ws", "/tmp/uiharu-ws-room", string.Empty);

    private readonly string _spillDirectory = Path.Combine(Path.GetTempPath(), $"mcp-admin-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_spillDirectory)) Directory.Delete(_spillDirectory, recursive: true);
    }

    private McpBridge Bridge(FakeHost host) => new(host, "/ws", null, _spillDirectory);

    private static AgentToolResources Resources(McpBridge? bridge) => new()
    {
        Paths = Paths,
        Mcp = new McpToolSet { Bridge = bridge },
    };

    private ExtensionHub Hub(FakeHost host)
    {
        McpBridge bridge = Bridge(host);
        return new ExtensionHub([McpExtension.Spec.Assemble(Resources(bridge), EReadImageMode.None, false)], bridge);
    }

    private static McpServerBrief Server(string name, EMcpLookupStatus? blocked = null,
        EMcpConnectionState state = EMcpConnectionState.Disconnected, int tools = 0, string? error = null) => new(
        name, string.Empty,
        new McpServerStatus { State = state, ToolCount = tools, Error = error })
    {
        Blocked = blocked,
    };

    private static IReadOnlyDictionary<string, JsonElement> Args(string server) =>
        new Dictionary<string, JsonElement> { ["server"] = JsonSerializer.SerializeToElement(server) };

    /// <summary>没有任何配置时也要说得清「现在没有」，而不是一句空话</summary>
    [Fact]
    public async Task List_WithoutAnyServer_SaysNothingIsConfigured()
    {
        string text = Text(await Hub(new FakeHost()).CallAsync(BuiltinExtensions.Mcp,
            BuiltinExtensions.McpList, null, TestContext.Current.CancellationToken));

        Assert.Contains("还没配置任何 MCP server", text);
        Assert.Contains("设置 → MCP", text);
    }

    /// <summary>配了但用不上的，逐台说清是哪道闸挡着——模型才能让用户去打开托管 / 去授权</summary>
    [Fact]
    public async Task List_ShowsBlockedServersAndWhy()
    {
        FakeHost host = new()
        {
            OverviewServers =
            [
                Server("unity", state: EMcpConnectionState.Connected, tools: 12),
                Server("github", state: EMcpConnectionState.Failed, error: "spawn failed"),
                Server("fs", EMcpLookupStatus.HostingOff),
                Server("pg", EMcpLookupStatus.NeedsApproval),
                Server("secret", EMcpLookupStatus.DisabledByCharacter),
            ],
        };

        string text = Text(await Hub(host).CallAsync(BuiltinExtensions.Mcp,
            BuiltinExtensions.McpList, null, TestContext.Current.CancellationToken));

        Assert.Contains("- unity：已连接，12 条命令", text);
        Assert.Contains("- github：离线：spawn failed", text);
        Assert.Contains("- fs：没开", text);
        Assert.Contains("- pg：待授权", text);
        Assert.Contains("- secret：被本智能体禁用", text);
    }

    /// <summary>连上了就报命令数，并指回 ExHelp：命令清单不在这一步倒</summary>
    [Fact]
    public async Task Connect_WhenOnline_ReportsToolCount()
    {
        FakeHost host = new()
        {
            Lookup = new McpServerLookup(EMcpLookupStatus.Found, new McpServerConfig { Name = "unity" }, ["unity"]),
            Connection = new McpServerConnection { IsConnected = true, Tools = [Echo()] },
        };

        string text = Text(await Hub(host).CallAsync(BuiltinExtensions.Mcp,
            BuiltinExtensions.McpConnect, Args("unity"), TestContext.Current.CancellationToken));

        Assert.Contains("已连上 'unity'：1 条命令", text);
        Assert.Contains(ExtensionCalls.HelpName, text);
    }

    /// <summary>关着托管的照实拒绝：这条路上不替用户改设置</summary>
    [Fact]
    public async Task Connect_WhenHostingOff_PointsAtTheSetting()
    {
        FakeHost host = new() { Lookup = new McpServerLookup(EMcpLookupStatus.HostingOff, null, []) };

        string text = Text(await Hub(host).CallAsync(BuiltinExtensions.Mcp,
            BuiltinExtensions.McpConnect, Args("fs"), TestContext.Current.CancellationToken));

        Assert.Contains("设置 → MCP", text);
        Assert.Equal(0, host.ConnectCount); //拒绝得早于连接
    }

    /// <summary>两条都只读：connect 做的正是查一个 server 时本来就会做的事，两者同一级别</summary>
    [Theory]
    [InlineData(BuiltinExtensions.McpList)]
    [InlineData(BuiltinExtensions.McpConnect)]
    public void Commands_AreReadOnly(string command)
    {
        FunctionCallContent call = new("call-1", ExtensionCalls.CallName,
            new Dictionary<string, object?> { ["name"] = $"{BuiltinExtensions.Mcp}.{command}" });

        Assert.Equal(EExtensionRisk.ReadOnly, ExtensionRisk.Classify(call));
    }

    /// <summary>一台 server 都没有时，ExHelp（不带参数）与系统提示名单里也得有 mcp 这一行</summary>
    [Fact]
    public async Task RosterAndHelp_NeverLeaveMcpOut()
    {
        Assert.Contains((BuiltinExtensions.Mcp, McpExtension.Spec.Summary),
            ExtensionRoster.Of(new AgentToolConfig(), McpToolSet.Empty, EAgentToolScope.Main));

        string all = Text(await Hub(new FakeHost()).HelpAsync(null, null, TestContext.Current.CancellationToken));

        Assert.Contains("- mcp（内置）", all);
        Assert.DoesNotContain("MCP server 是用户运行的应用", all); //一台都没配:下一步是去加一台,不是「再连一次」

        string help = Text(await Hub(new FakeHost()).HelpAsync(BuiltinExtensions.Mcp, null,
            TestContext.Current.CancellationToken));
        Assert.Contains("# mcp", help);
        Assert.Contains("mcp.connect", help); //命令名只在查这个扩展时给（ADR 0079：名单里不写命令）
        Assert.Contains("- list {}", help);
        Assert.Contains("- connect {server}", help);
    }

    /// <summary>这次装配拿不到桥接时如实说不可用，而不是让两条命令凭空消失</summary>
    [Fact]
    public void AssembledWithoutBridge_IsListedAsUnavailable()
    {
        BuiltinExtension assembled = McpExtension.Spec.Assemble(Resources(null), EReadImageMode.None, false);

        Assert.Empty(assembled.Commands);
        Assert.Contains("没有接上 MCP 管理器", assembled.Unavailable);
    }

    /// <summary>工具结果可能以 JSON 字符串回来（框架按 <c>ToolJson</c> 的口径序列化过），两种都认</summary>
    private static string Text(object? result) => result switch
    {
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()!,
        _ => throw new Xunit.Sdk.XunitException($"期望文本结果，实际是 {result?.GetType().Name ?? "null"}"),
    };

    private static AIFunction Echo() => AIFunctionFactory.Create(() => "pong", "echo");

    private sealed class FakeHost : IMcpServerHost
    {
        public McpServerLookup Lookup { get; set; } = new(EMcpLookupStatus.NotFound, null, []);
        public McpServerConnection Connection { get; set; } = new() { IsConnected = false, Error = "unset" };
        public IReadOnlyList<McpServerBrief> OverviewServers { get; set; } = [];
        public int ConnectCount { get; private set; }

        public IReadOnlyList<McpServerBrief> List(string? workspacePath, IEnumerable<string>? disabledServers) => [];

        public IReadOnlyList<McpServerBrief> Overview(string? workspacePath, IEnumerable<string>? disabledServers) =>
            OverviewServers;

        public McpServerLookup Find(string? workspacePath, IEnumerable<string>? disabledServers, string serverName) =>
            Lookup;

        public Task<McpServerConnection> ConnectAsync(McpServerConfig server, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ConnectCount++;
            return Task.FromResult(Connection);
        }

        public Task<McpServerConnection> RefreshToolsAsync(McpServerConfig server, TimeSpan timeout,
            CancellationToken cancellationToken) => Task.FromResult(Connection);

        public Task<McpServerConnection> ReconnectAsync(McpServerConfig server, TimeSpan timeout,
            CancellationToken cancellationToken) => Task.FromResult(Connection);

        public McpToolCatalog? LoadCatalog(McpServerConfig server) => null;
    }
}
