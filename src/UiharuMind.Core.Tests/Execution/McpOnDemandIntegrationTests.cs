/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Mcp;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 按需送达的端到端：真实的 <see cref="McpManager"/> 连一个手写的最小 stdio MCP server 桩。
/// 钉的是这套设计的<b>核心承诺</b>——按需 server 的连接起落不改变装配相关的修订号
/// （缓存前缀因此稳定），而直挂的会。没有 python3 的机器上跳过。
/// </summary>
public class McpOnDemandIntegrationTests : IDisposable
{
    private const string StubServer = """
        import sys, json, os
        silent_discover = len(sys.argv) > 1 and sys.argv[1] == "silent"
        def send(o):
            sys.stdout.write(json.dumps(o) + "\n"); sys.stdout.flush()
        for line in sys.stdin:
            line = line.strip()
            if not line: continue
            msg = json.loads(line)
            m = msg.get("method"); i = msg.get("id")
            if silent_discover and m == "server/discover": continue
            if m == "initialize":
                send({"jsonrpc":"2.0","id":i,"result":{"protocolVersion":msg["params"]["protocolVersion"],
                      "capabilities":{"tools":{}},"serverInfo":{"name":"stub","version":"1"},
                      "instructions":"Stub usage notes."}})
            elif m == "tools/list":
                tools = [{"name":"echo","description":"Echo the text back.",
                          "inputSchema":{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}},
                         {"name":"structured","description":"Returns structured content.","inputSchema":{"type":"object"}}]
                if os.path.exists(__file__ + ".late"):
                    tools.append({"name":"late","description":"Registered after connect.","inputSchema":{"type":"object"}})
                send({"jsonrpc":"2.0","id":i,"result":{"tools":tools}})
            elif m == "tools/call":
                t = msg["params"].get("arguments", {}).get("text", "")
                name = msg["params"].get("name")
                if name == "structured":
                    payload = {"result": [{"name": "a"}, {"name": "b"}]}
                    send({"jsonrpc":"2.0","id":i,"result":{"content":[{"type":"text","text":json.dumps(payload)}],
                          "structuredContent":payload}})
                    continue
                send({"jsonrpc":"2.0","id":i,"result":{"content":[{"type":"text","text":name + ":" + t}]}})
            elif m == "ping":
                send({"jsonrpc":"2.0","id":i,"result":{}})
            elif i is not None:
                send({"jsonrpc":"2.0","id":i,"error":{"code":-32601,"message":"Method not found"}})
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"mcp-e2e-{Guid.NewGuid():N}");
    private readonly McpManager _manager = new();
    private readonly List<string> _serversToDelete = new();

    public void Dispose()
    {
        // 删除配置即断开连接、结束桩进程
        foreach (string name in _serversToDelete) _manager.DeleteServer(name);
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static bool HasPython()
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo("python3", "--version")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
            });
            process?.WaitForExit(5000);
            return process is { ExitCode: 0 };
        }
        catch
        {
            return false;
        }
    }

    private McpServerConfig AddStub(string name, EMcpMountMode mode, params string[] extraArgs)
    {
        Directory.CreateDirectory(_directory);
        string script = Path.Combine(_directory, "stub_server.py");
        File.WriteAllText(script, StubServer);
        McpServerConfig config = new()
        {
            Name = name, Command = "python3", Args = [script, ..extraArgs], MountMode = mode, IsEnabled = true,
            Description = "stub for tests",
        };
        _manager.SaveServer(config);
        _serversToDelete.Add(name);
        return config;
    }

    private static AIFunction Named(McpToolSet set, string name) =>
        (AIFunction)set.Tools.Single(x => x.Name == name);

    [Fact]
    public async Task OnDemand_ConnectsLazily_ServesHelpAndCall_WithoutTouchingRevision()
    {
        if (!HasPython()) Assert.Skip("python3 is not available");
        CancellationToken ct = TestContext.Current.CancellationToken;
        McpServerConfig config = AddStub("stub-ondemand", EMcpMountMode.OnDemand);
        int revisionBefore = _manager.Revision;

        // 装配:不连接、不等待,只出元工具与名单
        McpToolSet set = _manager.Resolve(null, null, _directory);
        Assert.Equal([McpMetaTools.HelpName, McpMetaTools.CallName], set.Tools.Select(x => x.Name));
        Assert.Contains("stub-ondemand：stub for tests", set.Instructions);
        Assert.Equal(EMcpConnectionState.Disconnected, _manager.GetServerStatus("stub-ondemand").State);

        // 第一次 McpHelp 才连
        object? help = await Named(set, McpMetaTools.HelpName).InvokeAsync(
            new AIFunctionArguments { ["server"] = "stub-ondemand" }, ct);
        string helpText = Assert.IsType<string>(help);
        Assert.Contains("Stub usage notes.", helpText);
        Assert.Contains("- echo: Echo the text back.", helpText);
        Assert.Equal(EMcpConnectionState.Connected, _manager.GetServerStatus("stub-ondemand").State);

        // 带 tool:完整 schema
        string detail = Assert.IsType<string>(await Named(set, McpMetaTools.HelpName).InvokeAsync(
            new AIFunctionArguments { ["server"] = "stub-ondemand", ["tool"] = "echo" }, ct));
        Assert.Contains("\"required\":[\"text\"]", detail);

        // McpCall 走通,参数对象以 JsonElement 传入
        JsonElement args = JsonSerializer.SerializeToElement(new { text = "hi" });
        object? called = await Named(set, McpMetaTools.CallName).InvokeAsync(
            new AIFunctionArguments { ["server"] = "stub-ondemand", ["tool"] = "echo", ["arguments"] = args }, ct);
        Assert.Equal("echo:hi", called);

        // 核心承诺:连上之后装配相关的修订号纹丝不动,前缀不会因此失效
        Assert.Equal(revisionBefore, _manager.Revision);

        // 清单已缓存,离线时 McpHelp 还能读
        Assert.NotNull(((IMcpServerHost)_manager).LoadCatalog(config));
    }

    [Fact]
    public async Task Direct_ConnectionChangesRevision_UnlikeOnDemand()
    {
        if (!HasPython()) Assert.Skip("python3 is not available");
        CancellationToken ct = TestContext.Current.CancellationToken;
        AddStub("stub-direct", EMcpMountMode.Direct);
        int revisionBefore = _manager.Revision;

        await _manager.WarmupAsync(cancellationToken: ct);
        McpToolSet set = _manager.Resolve();

        Assert.Equal(["echo", "structured"], set.Tools.Select(x => x.Name));
        Assert.True(_manager.Revision > revisionBefore, "直挂的工具取回必须触发重建，否则模型永远看不见新工具");
    }

    /// <summary>
    /// 不认识 server/discover 的 server（Unity MCP 就是）会吞掉这个探测，SDK 默认白等 5 秒。
    /// 探测超时缩到 2 秒之后，首次连接应明显快于那个数
    /// </summary>
    [Fact]
    public async Task FirstConnect_AgainstServerThatSwallowsDiscover_DoesNotWaitFiveSeconds()
    {
        if (!HasPython()) Assert.Skip("python3 is not available");
        CancellationToken ct = TestContext.Current.CancellationToken;
        AddStub("stub-silent", EMcpMountMode.OnDemand, "silent");
        McpToolSet set = _manager.Resolve(null, null, _directory);

        Stopwatch watch = Stopwatch.StartNew();
        string help = Assert.IsType<string>(await Named(set, McpMetaTools.HelpName).InvokeAsync(
            new AIFunctionArguments { ["server"] = "stub-silent" }, ct));
        watch.Stop();

        Assert.Contains("- echo:", help);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4.5), $"first connect took {watch.Elapsed.TotalSeconds:0.0}s");
    }

    /// <summary>连上之后 server 才多出来的工具：点名它时重取一遍再判断，不该一口咬定"没有"</summary>
    [Fact]
    public async Task ToolAddedAfterConnect_IsFoundByRefreshOnMiss()
    {
        if (!HasPython()) Assert.Skip("python3 is not available");
        CancellationToken ct = TestContext.Current.CancellationToken;
        AddStub("stub-late", EMcpMountMode.OnDemand);
        McpToolSet set = _manager.Resolve(null, null, _directory);
        AIFunction help = Named(set, McpMetaTools.HelpName);
        AIFunction call = Named(set, McpMetaTools.CallName);

        string before = Assert.IsType<string>(await help.InvokeAsync(
            new AIFunctionArguments { ["server"] = "stub-late" }, ct));
        Assert.DoesNotContain("- late", before);

        File.WriteAllText(Path.Combine(_directory, "stub_server.py.late"), "");

        object? called = await call.InvokeAsync(new AIFunctionArguments
        {
            ["server"] = "stub-late", ["tool"] = "late",
            ["arguments"] = JsonSerializer.SerializeToElement(new { text = "x" }),
        }, ct);
        Assert.Equal("late:x", called);

        string after = Assert.IsType<string>(await help.InvokeAsync(
            new AIFunctionArguments { ["server"] = "stub-late" }, ct));
        Assert.Contains("- late", after);
    }

    /// <summary>
    /// 带结构化内容的结果：框架会把整个 CallToolResult 序列化，同一份数据在 content 与 structuredContent 里各一遍，
    /// 且 content 里的 JSON 文本满是 \u0022 转义。模型应只看到一份干净的文本。两种送达方式都一样。
    /// </summary>
    [Theory]
    [InlineData(EMcpMountMode.OnDemand)]
    [InlineData(EMcpMountMode.Direct)]
    public async Task StructuredResult_ReachesTheModelOnceAndUnescaped(EMcpMountMode mode)
    {
        if (!HasPython()) Assert.Skip("python3 is not available");
        CancellationToken ct = TestContext.Current.CancellationToken;
        AddStub("stub-structured", mode);
        await _manager.WarmupAsync(cancellationToken: ct);
        McpToolSet set = _manager.Resolve(null, null, _directory);

        object? result = mode == EMcpMountMode.OnDemand
            ? await Named(set, McpMetaTools.CallName).InvokeAsync(new AIFunctionArguments
            {
                ["server"] = "stub-structured", ["tool"] = "structured",
            }, ct)
            : await Named(set, "structured").InvokeAsync(new AIFunctionArguments(), ct);

        string text = Assert.IsType<string>(result);
        Assert.Equal("""{"result": [{"name": "a"}, {"name": "b"}]}""", text);
        Assert.DoesNotContain("\\u0022", text);
        Assert.DoesNotContain("structuredContent", text);
    }

    [Fact]
    public async Task OnDemand_UnknownServerAndDisabledServer_AreToolResultsNotExceptions()
    {
        if (!HasPython()) Assert.Skip("python3 is not available");
        CancellationToken ct = TestContext.Current.CancellationToken;
        AddStub("stub-blocked", EMcpMountMode.OnDemand);
        McpToolSet set = _manager.Resolve(null, ["stub-blocked"], _directory);

        // 唯一的按需 server 被角色禁掉:名单为空,元工具也就不挂
        Assert.Empty(set.Tools);

        // 但若元工具是在禁用之前拿到的(旧快照),调用时仍按最新名单拒绝
        McpToolSet before = _manager.Resolve(null, null, _directory);
        McpBridge bridge = new(_manager, null, ["stub-blocked"], _directory);
        string refused = await bridge.HelpAsync("stub-blocked", null, ct);
        Assert.Contains("disabled for this agent", refused);
        Assert.Equal(2, before.Tools.Count);
    }
}
