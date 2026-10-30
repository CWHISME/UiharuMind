using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core.LLM;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 会话亲和的<b>端到端</b>验证：LazyChatClient → 真实 OpenAI 兼容管线（改写 + SDK 序列化 + 重试策略）
/// → <see cref="OpenAICompatibleHttpHandler"/> → 假网卡，看头上到底有没有值。
///
/// 与 <see cref="LazyChatClientSessionAffinityTests"/>（假客户端读环境值）和
/// <see cref="SessionAffinityTests"/>（直接设值看头）分开：那两份各测一半，
/// 中间断掉（赋值被挪到第一个 await 之后、中间插进一个 Task.Run、流式路径忘了设）两份都还是绿的。
/// </summary>
public class SessionAffinityEndToEndTests
{
    /// <summary>这一家的亲和头名；换成别的供应商就是别的一组，公共层不认识具体名字</summary>
    private static readonly string[] AffinityHeaders = ["x-session-id"];

    private const string NonStreamingJson =
        """{"id":"1","created":1,"model":"affinity-test","object":"chat.completion","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""";

    private const string StreamingSse =
        "data: {\"id\":\"1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"affinity-test\"," +
        "\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":null}]}\n\n" +
        "data: {\"id\":\"1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"affinity-test\"," +
        "\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
        "data: [DONE]\n\n";

    [Fact]
    public async Task NonStreaming_ReachesTheWire()
    {
        RecordingHandler wire = new();
        LazyChatClient client = Client(wire, "affinity-a");

        await client.GetResponseAsync([User("alpha")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["affinity-a"], wire.AffinitiesOf("alpha"));
    }

    /// <summary>
    /// 流式：赋值发生在<b>迭代器</b>里（<c>StreamWithRetryAsync</c> 顶部），
    /// 请求在第一次 MoveNext 之后才发出——这条路径此前一份测试都没有
    /// </summary>
    [Fact]
    public async Task Streaming_ReachesTheWire()
    {
        RecordingHandler wire = new();
        LazyChatClient client = Client(wire, "affinity-a");

        await foreach (ChatResponseUpdate _ in client.GetStreamingResponseAsync([User("alpha")],
                           cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal(["affinity-a"], wire.AffinitiesOf("alpha"));
    }

    /// <summary>
    /// 两个会话并发（一个流式、一个非流式，服务端故意拖住制造交错）：各带各的，不串号。
    /// 串号比不带更糟——那一发会被路由到别人的副本上。
    /// </summary>
    [Fact]
    public async Task ConcurrentSessions_DoNotCrossOnTheWire()
    {
        RecordingHandler wire = new(delayMs: 30);
        LazyChatClient alpha = Client(wire, "affinity-a");
        LazyChatClient beta = Client(wire, "affinity-b");

        async Task StreamAsync()
        {
            await foreach (ChatResponseUpdate _ in alpha.GetStreamingResponseAsync([User("alpha")],
                               cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        }

        await Task.WhenAll(StreamAsync(),
            beta.GetResponseAsync([User("beta")], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(["affinity-a"], wire.AffinitiesOf("alpha"));
        Assert.Equal(["affinity-b"], wire.AffinitiesOf("beta"));
        Assert.True(wire.PeakInFlight >= 2, $"两发没有真正重叠（峰值 {wire.PeakInFlight}），这条测试就退化成串行了");
    }

    /// <summary>
    /// 没有会话的客户端（能力预览那种）不能继承同一流程上上一次请求的值。
    /// 流式路径尤其要钉住：值是在迭代器里设的，迭代器恢复执行上下文的行为没有写进语言规范。
    /// </summary>
    [Fact]
    public async Task ClientWithoutAffinity_DoesNotInheritFromThePreviousRequest()
    {
        RecordingHandler wire = new();
        LazyChatClient withAffinity = Client(wire, "affinity-a");
        LazyChatClient withoutAffinity = Client(wire, null);

        await foreach (ChatResponseUpdate _ in withAffinity.GetStreamingResponseAsync([User("alpha")],
                           cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        await foreach (ChatResponseUpdate _ in withoutAffinity.GetStreamingResponseAsync([User("beta")],
                           cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal(["affinity-a"], wire.AffinitiesOf("alpha"));
        Assert.Equal([string.Empty], wire.AffinitiesOf("beta"));
    }

    /// <summary>
    /// 每个头只能有一个值。SDK 的重试、SendGate 的排队都在这一层之外，
    /// 万一哪条路径把同一个请求对象重发一次，<c>TryAddWithoutValidation</c> 会<b>追加</b>而不是覆盖——
    /// 那时网线上就是 <c>x-session-id: a, a</c>，网关解析出什么全看它怎么切。
    /// </summary>
    [Fact]
    public async Task Headers_CarryExactlyOneValueEach()
    {
        RecordingHandler wire = new();
        LazyChatClient client = Client(wire, "affinity-a");

        await client.GetResponseAsync([User("alpha")], cancellationToken: TestContext.Current.CancellationToken);

        IReadOnlyList<(string Name, string[] Values)> headers = wire.HeadersOf("alpha");
        foreach (string name in AffinityHeaders)
        {
            (string Name, string[] Values) hit = headers.Single(h =>
                string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(["affinity-a"], hit.Values);
        }
    }

    private static ChatMessage User(string marker) => new(ChatRole.User, marker);

    private static LazyChatClient Client(RecordingHandler wire, string? affinity)
    {
        OpenAICompatibleHttpHandler handler = new("http://localhost/v1/chat/completions", wire,
            affinityHeaders: AffinityHeaders);
        IChatClient sdk = OpenAICompatibleChatClient.Create(handler, null, "affinity-test", "probe");
        return new LazyChatClient(() => Running(sdk), affinity);
    }

    private static ModelRunningData Running(IChatClient inner)
    {
        RemoteModelInfo remote = new()
        {
            Config = new RemoteAgnesModelConfig
            {
                ModelName = "affinity-test",
                ModelId = "affinity-test",
            },
        };
        ModelRunningData data = new(remote);
        data.CompleteLoading(inner, loadToken: CancellationToken.None);
        return data;
    }

    /// <summary>假网卡：记下每一发的正文与亲和头（请求对象发完就回收，取值要在这一刻抄下来）</summary>
    private sealed class RecordingHandler(int delayMs = 0) : HttpMessageHandler
    {
        private readonly List<(string Body, string Affinity)> _requests = [];
        private readonly List<(string Marker, (string Name, string[] Values)[] Headers)> _headers = [];
        private int _inFlight;

        /// <summary>同时在飞的请求峰值：用来确认「并发」那条测试真的并发了</summary>
        public int PeakInFlight { get; private set; }

        public IReadOnlyList<string> AffinitiesOf(string marker)
        {
            lock (_requests)
            {
                return _requests.Where(x => x.Body.Contains(marker, StringComparison.Ordinal))
                    .Select(x => x.Affinity).ToArray();
            }
        }

        /// <summary>那一发的全部请求头（同名多值会展开成数组，重复就是两个元素）</summary>
        public IReadOnlyList<(string Name, string[] Values)> HeadersOf(string marker)
        {
            lock (_headers)
            {
                return _headers.First(x => x.Marker == marker).Headers;
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            string affinity = request.Headers.TryGetValues(AffinityHeaders[0], out IEnumerable<string>? values)
                ? values.First()
                : string.Empty;
            (string Name, string[] Values)[] headers = request.Headers
                .Select(h => (h.Key, h.Value.ToArray())).ToArray();
            string marker = body.Contains("alpha", StringComparison.Ordinal) ? "alpha" : "beta";

            lock (_requests)
            {
                _inFlight++;
                if (_inFlight > PeakInFlight) PeakInFlight = _inFlight;
            }

            try
            {
                if (delayMs > 0) await Task.Delay(delayMs, cancellationToken);
            }
            finally
            {
                lock (_requests) _inFlight--;
            }

            lock (_requests) _requests.Add((body, affinity));
            lock (_headers) _headers.Add((marker, headers));

            bool streaming = body.Contains("\"stream\":true", StringComparison.Ordinal);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(streaming ? StreamingSse : NonStreamingJson, Encoding.UTF8,
                    streaming ? "text/event-stream" : "application/json"),
            };
        }
    }
}
