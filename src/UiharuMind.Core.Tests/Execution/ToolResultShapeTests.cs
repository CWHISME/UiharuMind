using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.AI.Net.Request;
using UiharuMind.Core.AI.Net.Transport;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 工具结果在<b>内存里</b>的形态必须和读盘之后一样。
///
/// 工厂带了 <c>SerializerOptions</c>（参数要宽松解析），框架会拿同一份选项把返回值序列化成
/// <c>JsonElement</c>：字符串结果于是变成「带引号与 <c>\t</c> 字面量的 JSON 文本」，发给模型时
/// 原样上线，模型读到的是转义串；而历史读盘时 <c>ChatContentNormalizer</c> 又会把它还原成 string。
/// 同一份历史两种字节，前缀从第一条 tool 消息起就分叉——实测一次这样的分叉让后面 32 条消息全部冷启动。
/// </summary>
public class ToolResultShapeTests
{
    [Fact]
    public async Task StringResult_StaysString()
    {
        AIFunction function = ToolJson.CreateFunction(
            (string path) => Task.FromResult("1\t# AGENTS.md\n2\t\n3\t内容"), "Probe");

        object? result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["path"] = "x" }),
            TestContext.Current.CancellationToken);

        Assert.IsType<string>(result);
        Assert.Equal("1\t# AGENTS.md\n2\t\n3\t内容", result);
    }

    /// <summary>真工具走一遍：Read 的正文必须是原文，不是被序列化过的 JSON 文本</summary>
    [Fact]
    public async Task RealReadTool_ReturnsRawText()
    {
        string root = Path.Combine(Path.GetTempPath(), "uiharu-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.md"), "# 标题\n\n正文\n",
                TestContext.Current.CancellationToken);

            AITool read = new PermissiveFileAccessTools(root).Create().Single(t => t.Name == "Read");
            object? result = await ((AIFunction)read).InvokeAsync(
                new AIFunctionArguments(new Dictionary<string, object?> { ["filePath"] = "a.md" }),
                TestContext.Current.CancellationToken);

            string text = Assert.IsType<string>(result);
            Assert.Contains("# 标题", text);
            Assert.DoesNotContain("\\t", text); // 序列化过的形态里换行是 \n 字面量
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>结构化结果保持对象形态：那是工具特意给模型的结构，抹成文本会丢信息</summary>
    [Fact]
    public async Task StructuredResult_KeepsJsonShape()
    {
        AIFunction function = ToolJson.CreateFunction(
            (string path) => Task.FromResult(new Dictionary<string, object?> { ["a"] = 1 }), "ProbeStructured");

        object? result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["path"] = "x" }),
            TestContext.Current.CancellationToken);

        Assert.IsType<System.Text.Json.JsonElement>(result);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, ((System.Text.Json.JsonElement)result!).ValueKind);
    }

    /// <summary>
    /// Shell 走的是框架的 <c>AsAIFunction</c>，不经过 <see cref="ToolJson.CreateFunction"/>——
    /// 2026-10-31 实测：一次重启让 26 条 shell 结果从「带引号的转义串」翻成纯文本，
    /// 78 条消息里从第 3 条起全部冷启动（86.7k tokens 全价）。这条钉住执行器出口的形态。
    /// </summary>
    [Fact]
    public async Task ShellTool_ReturnsRawText()
    {
        await using LocalShellExecutor executor = ShellExecutorFactory.Create(Path.GetTempPath(), null);

        // 审批那层是给用户看的，测试里点不掉，取它里面的那一层
        AIFunction tool = ShellExecutorFactory.CreateGuardedTool(executor);
        object? result = await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["command"] = "echo uiharu-probe" }),
            TestContext.Current.CancellationToken);

        string text = Assert.IsType<string>(result);
        Assert.Contains("uiharu-probe", text);
        Assert.DoesNotContain("\\n", text); // 序列化过的形态里换行是 \n 字面量
    }

    /// <summary>
    /// 出口闸：不管 <c>FunctionResultContent.Result</c> 是 string 还是被工厂包出来的
    /// <c>JsonElement{String}</c>，发到线上的都必须是同一份纯文本。
    ///
    /// 这是唯一能覆盖全部工具的地方——工具来自 ToolJson、框架的 <c>AsAIFunction</c>、MCP，
    /// 逐个补一道总会漏（2026-10-29 修了 Read 那批，10-31 又栽在 Shell 上）。
    /// </summary>
    [Fact]
    public async Task WireBody_ToolResultIsPlainText_EvenWhenWrapped()
    {
        var server = new CapturingServer();
        IChatClient client = NewClient(server);

        ChatMessage tool = new(ChatRole.Tool,
            [new FunctionResultContent("call_1", JsonSerializer.SerializeToElement("a\nb\tc"))]);
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi"), tool],
            cancellationToken: TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(Assert.Single(server.Bodies));
        Assert.Equal("a\nb\tc", ToolContent(document));
    }

    /// <summary>
    /// 真正要守的不变量：<b>同一份历史，重启前后发出去的是同一份字节</b>。
    ///
    /// 事故就是这条被破坏——实时那发是转义串、读盘那发是纯文本，前缀从第一条工具消息起分叉，
    /// 服务端报 cached_tokens = 0。上面那条只验「形态对不对」，这条验「两发是不是一样」，
    /// 后者才是缓存关心的东西。
    /// </summary>
    [Fact]
    public async Task SameHistory_SendsSameBytes_AcrossRestart()
    {
        // 实时形态：结果被工厂包成 JsonElement
        ChatMessage live = new(ChatRole.Tool,
            [new FunctionResultContent("call_1", JsonSerializer.SerializeToElement("a\nb\tc"))]);
        // 落盘再读回：ParseLine 里的 Normalize 已经把它还原成 string
        ChatMessage restored = Assert.Single(HistoryJsonl.Parse(
            HistoryJsonl.SerializeLines([live]).Split('\n')));

        var server = new CapturingServer();
        IChatClient client = NewClient(server);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi"), live],
            cancellationToken: TestContext.Current.CancellationToken);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi"), restored],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, server.Bodies.Count);
        Assert.Equal(server.Bodies[0], server.Bodies[1]);
    }

    /// <summary>闸在三条协议线的<b>外面</b>：chat / responses / messages 都是同一个入口拿到的客户端</summary>
    [Fact]
    public void Gate_WrapsEveryProtocolLine()
    {
        foreach (EApiFormat format in Enum.GetValues<EApiFormat>())
        {
            IChatClient client = ChatClientFactory.Create(
                new WireHttpHandler("http://localhost/v1", new CapturingServer()),
                new ProbeModel(format), "probe", "key");
            Assert.IsType<ToolResultShapeChatClient>(client);
        }
    }

    private static IChatClient NewClient(CapturingServer server) =>
        ChatClientFactory.Create(new WireHttpHandler("http://localhost/v1", server),
            model: null, "probe", "key");

    /// <summary>取请求体里那条 tool 消息的正文（按角色找，不按下标——以后加系统提示就会错位）</summary>
    private static string? ToolContent(JsonDocument document)
    {
        foreach (JsonElement message in document.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (message.GetProperty("role").GetString() == "tool")
                return message.GetProperty("content").GetString();
        }

        return null;
    }

    /// <summary>只声明协议格式的模型：给闸的覆盖面测试用，不参与任何请求</summary>
    private sealed class ProbeModel(EApiFormat format) : ILlmModel
    {
        public string ModelName => "probe";
        public string ModelPath => "";
        public bool IsVision => false;
        public string ModelDescription => "";
        public string ModelId => "probe";
        public int Port => 0;
        public EApiFormat ApiFormat => format;
    }

    private sealed class CapturingServer : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content != null) Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"x","object":"chat.completion","created":0,"model":"m","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""",
                    Encoding.UTF8, "application/json"),
            };
        }
    }
}
