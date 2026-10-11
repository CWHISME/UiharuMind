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
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Mcp;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// MCP 经扩展送达的端到端（ADR 0079）：真实的 <see cref="McpManager"/> 连一个手写的最小 stdio MCP server 桩，
/// 模型那一侧走 <c>ExtensionHelp</c> / <c>ExtensionCall</c>。钉的是<b>核心承诺</b>——
/// 连接起落不改变配置修订号（缓存前缀因此稳定）。没有 python3 的机器上跳过。
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
                tools = [{"name":"echo","description":"Echo the text back.","annotations":{"readOnlyHint":True},
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

    private McpServerConfig AddStub(string name, params string[] extraArgs)
    {
        Directory.CreateDirectory(_directory);
        string script = Path.Combine(_directory, "stub_server.py");
        File.WriteAllText(script, StubServer);
        McpServerConfig config = new()
        {
            Name = name, Command = "python3", Args = [script, ..extraArgs], IsEnabled = true,
            Description = "stub for tests",
        };
        _manager.SaveServer(config);
        _serversToDelete.Add(name);
        return config;
    }

    // 只挂 MCP 那一半：内置扩展另有测试
    private static AIFunction Named(McpToolSet set, string name) =>
        (AIFunction)ExtensionMetaTools.Create(new ExtensionHub([], set.Bridge)).Single(x => x.Name == name);

    private static AIFunctionArguments Ask(string extension, string? command = null, object? arguments = null)
    {
        AIFunctionArguments args = new() { ["extension"] = extension };
        if (command != null) args["command"] = command;
        if (arguments != null) args["arguments"] = JsonSerializer.SerializeToElement(arguments);
        return args;
    }

    [Fact]
    public async Task Server_ConnectsLazily_ServesHelpAndCall_WithoutTouchingRevision()
    {
        if (!HasPython()) Assert.Skip("python3 is not available");
        CancellationToken ct = TestContext.Current.CancellationToken;
        McpServerConfig config = AddStub("stub-ondemand");
        int revisionBefore = _manager.Revision;

        // 装配:不连接、不等待,只出名单与桥接
        McpToolSet set = _manager.Resolve(null, null, _directory);
        Assert.Equal("stub-ondemand", Assert.Single(set.Servers).Name);
        Assert.NotNull(set.Bridge);
        Assert.Equal(EMcpConnectionState.Disconnected, _manager.GetServerStatus("stub-ondemand").State);

        // 不带参数:列出它,还没连
        string all = Assert.IsType<string>(await Named(set, ExtensionCalls.HelpName).InvokeAsync(new AIFunctionArguments(), ct));
        Assert.Contains("- stub-ondemand（MCP，尚未连接", all);

        // 第一次查它才连
        string helpText = Assert.IsType<string>(await Named(set, ExtensionCalls.HelpName).InvokeAsync(Ask("stub-ondemand"), ct));
        Assert.Contains("Stub usage notes.", helpText);
        Assert.Contains("- echo {text}: Echo the text back.", helpText);
        Assert.Equal(EMcpConnectionState.Connected, _manager.GetServerStatus("stub-ondemand").State);

        // 带命令:完整 schema
        string detail = Assert.IsType<string>(await Named(set, ExtensionCalls.HelpName).InvokeAsync(
            Ask("stub-ondemand", "echo"), ct));
        Assert.Contains("\"required\":[\"text\"]", detail);

        // 调用走通,参数对象以 JsonElement 传入
        object? called = await Named(set, ExtensionCalls.CallName).InvokeAsync(
            Ask("stub-ondemand", "echo", new { text = "hi" }), ct);
        Assert.Equal("echo:hi", called);

        // 核心承诺:连上之后修订号纹丝不动,前缀不会因此失效
        Assert.Equal(revisionBefore, _manager.Revision);

        // 清单已缓存,离线时帮助还能读;再列一次就报已连上
        Assert.NotNull(((IMcpServerHost)_manager).LoadCatalog(config));
        all = Assert.IsType<string>(await Named(set, ExtensionCalls.HelpName).InvokeAsync(new AIFunctionArguments(), ct));
        Assert.Contains("- stub-ondemand（MCP，已连接，2 条命令）：stub for tests", all);
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
        AddStub("stub-silent", "silent");
        McpToolSet set = _manager.Resolve(null, null, _directory);

        Stopwatch watch = Stopwatch.StartNew();
        string help = Assert.IsType<string>(await Named(set, ExtensionCalls.HelpName).InvokeAsync(Ask("stub-silent"), ct));
        watch.Stop();

        Assert.Contains("- echo {text}:", help);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4.5), $"first connect took {watch.Elapsed.TotalSeconds:0.0}s");
    }

    /// <summary>连上之后 server 才多出来的工具：点名它时重取一遍再判断，不该一口咬定"没有"</summary>
    [Fact]
    public async Task ToolAddedAfterConnect_IsFoundByRefreshOnMiss()
    {
        if (!HasPython()) Assert.Skip("python3 is not available");
        CancellationToken ct = TestContext.Current.CancellationToken;
        AddStub("stub-late");
        McpToolSet set = _manager.Resolve(null, null, _directory);
        AIFunction help = Named(set, ExtensionCalls.HelpName);
        AIFunction call = Named(set, ExtensionCalls.CallName);

        string before = Assert.IsType<string>(await help.InvokeAsync(Ask("stub-late"), ct));
        Assert.DoesNotContain("- late", before);

        File.WriteAllText(Path.Combine(_directory, "stub_server.py.late"), "");

        object? called = await call.InvokeAsync(Ask("stub-late", "late", new { text = "x" }), ct);
        Assert.Equal("late:x", called);

        string after = Assert.IsType<string>(await help.InvokeAsync(Ask("stub-late"), ct));
        Assert.Contains("- late", after);
    }

    /// <summary>
    /// 带结构化内容的结果：框架会把整个 CallToolResult 序列化，同一份数据在 content 与 structuredContent 里各一遍，
    /// 且 content 里的 JSON 文本满是 \u0022 转义。模型应只看到一份干净的文本
    /// </summary>
    [Fact]
    public async Task StructuredResult_ReachesTheModelOnceAndUnescaped()
    {
        if (!HasPython()) Assert.Skip("python3 is not available");
        CancellationToken ct = TestContext.Current.CancellationToken;
        AddStub("stub-structured");
        McpToolSet set = _manager.Resolve(null, null, _directory);

        object? result = await Named(set, ExtensionCalls.CallName).InvokeAsync(Ask("stub-structured", "structured"), ct);

        string text = Assert.IsType<string>(result);
        Assert.Equal("""{"result": [{"name": "a"}, {"name": "b"}]}""", text);
        Assert.DoesNotContain("\\u0022", text);
        Assert.DoesNotContain("structuredContent", text);
    }

    [Fact]
    public async Task UnknownServerAndDisabledServer_AreToolResultsNotExceptions()
    {
        if (!HasPython()) Assert.Skip("python3 is not available");
        CancellationToken ct = TestContext.Current.CancellationToken;
        AddStub("stub-blocked");
        McpToolSet set = _manager.Resolve(null, ["stub-blocked"], _directory);

        // 唯一的 server 被角色禁掉:名单为空,也没有桥接
        Assert.Empty(set.Servers);
        Assert.Null(set.Bridge);

        // 但若桥接是在禁用之前拿到的(旧快照),调用时仍按最新名单拒绝
        McpBridge bridge = new(_manager, null, ["stub-blocked"], _directory);
        string refused = await bridge.HelpAsync("stub-blocked", null, ct);
        Assert.Contains("disabled for this agent", refused);
    }

    /// <summary>只读声明（readOnlyHint）要在连上时记下：审批据此放过只读的调用</summary>
    [Fact]
    public async Task ReadOnlyHint_IsRememberedOnConnect()
    {
        if (!HasPython()) Assert.Skip("python3 is not available");
        CancellationToken ct = TestContext.Current.CancellationToken;
        AddStub("stub-hints");
        McpToolSet set = _manager.Resolve(null, null, _directory);
        Assert.False(_manager.IsReadOnlyTool("stub-hints", "echo", null)); //没连过:按不是只读

        await Named(set, ExtensionCalls.HelpName).InvokeAsync(Ask("stub-hints"), ct);

        Assert.True(_manager.IsReadOnlyTool("stub-hints", "echo", null));
        Assert.False(_manager.IsReadOnlyTool("stub-hints", "structured", null));
    }
}
