using System.Net;
using System.Text;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.AI.Net.Transport;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 会话亲和：同一会话的请求带同一组头，网关据此把它们钉到同一台后端——
/// 前缀缓存按副本各存一份，落到没建过该前缀的副本上就是整段冷启动，
/// 命中率看着像坏了，其实是另一台机器在答。
///
/// 头不参与前缀缓存，所以加它不会让任何一份已有缓存失效；反过来说，
/// 它能否奏效只能靠实测命中率验证，这里只钉住「发了什么、谁声明的」。
/// </summary>
public class SessionAffinityTests
{
    /// <summary>头名由供应商声明，HTTP 层照单写：给了两个就写两个，一个不多</summary>
    [Fact]
    public async Task DeclaredHeaders_AreWrittenWithTheSessionValue()
    {
        CapturingServer server = new();
        using HttpClient client = new(new WireHttpHandler("http://localhost/v1", server,
            affinityHeaders: ["x-session-id", "x-session-affinity"]));

        await PostAsync(client);

        Assert.Equal("affinity-123", server.Header("x-session-id"));
        Assert.Equal("affinity-123", server.Header("x-session-affinity"));
        Assert.Null(server.Header("session_id")); //没声明就不写
    }

    /// <summary>没声明头名的供应商（绝大多数）一个都不发：未知头只是噪音，还可能撞上别家的语义</summary>
    [Fact]
    public async Task UndeclaredHeaders_SendNothing()
    {
        CapturingServer server = new();
        using HttpClient client = new(new WireHttpHandler("http://localhost/v1", server));

        await PostAsync(client);

        Assert.Empty(server.SentHeaderNames);
    }

    [Fact]
    public async Task NoAffinity_LeavesHeadersOff()
    {
        CapturingServer server = new();
        using HttpClient client = new(new WireHttpHandler("http://localhost/v1", server,
            affinityHeaders: ["x-session-id"]));

        using HttpResponseMessage response = await client.PostAsync(
            "http://localhost/v1/chat/completions", new StringContent("{}", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Null(server.Header("x-session-id"));
    }

    /// <summary>
    /// 只有聚合网关声明亲和头：认它的才发，认错了只是噪音——而且多发会撞上别家的语义。
    /// 这两家的模型目录与 id 都是 OpenRouter 式命名（<c>meta/…</c>、<c>reflection/…</c>），按同一组处理。
    /// </summary>
    [Fact]
    public void OnlyAggregatorGateways_DeclareAffinityHeaders()
    {
        Assert.Equal(["x-session-id"], new RemoteOpenRouterModelConfig().SessionAffinityHeaders);
        Assert.Equal(["x-session-id"], new RemoteCommandCodeModelConfig().SessionAffinityHeaders);
        Assert.True(new RemoteOpenRouterModelConfig().SupportsPromptCacheKey);
        Assert.True(new RemoteCommandCodeModelConfig().SupportsPromptCacheKey);

        Type[] declaring = [typeof(RemoteOpenRouterModelConfig), typeof(RemoteCommandCodeModelConfig)];
        foreach (Type type in RemoteModelConfigRegistry.ProviderTypes)
        {
            if (declaring.Contains(type)) continue;
            BaseRemoteModelConfig config = RemoteModelConfigRegistry.CreateDefault(type)!;
            Assert.Null(config.SessionAffinityHeaders);
            Assert.False(config.SupportsPromptCacheKey); //改字节的字段默认关,严格服务端会因未知字段 400
        }
    }

    /// <summary>
    /// 亲和值是会话标识的<b>哈希</b>，不是原值：形态固定、确定性、不可反查。
    /// 固定形态让「能不能当请求头值」变成构造保证；不可反查则让磁盘目录名不会被顺手发给第三方网关。
    /// </summary>
    [Fact]
    public void Affinity_IsHashedFromSessionId()
    {
        ChatSession session = new("会话", new CharacterData { CharacterId = "c" });

        string affinity = session.CacheAffinity;

        Assert.Equal(32, affinity.Length);
        Assert.All(affinity, ch => Assert.True(char.IsAsciiDigit(ch) || ch is >= 'a' and <= 'f'));
        Assert.Equal(affinity, SessionAffinity.Of(session.SessionId)); //确定性：每次算都一样
        Assert.NotEqual(session.SessionId, affinity);                  //不是原值
    }

    /// <summary>
    /// 会话标识里混进控制字符也不影响发请求：哈希之后网线上的值恒合法，
    /// 不需要在发请求那一层做校验、更不需要放弃这一发。
    /// </summary>
    [Fact]
    public void WeirdSessionId_StillYieldsALegalValue()
    {
        ChatSession session = new("会话", new CharacterData { CharacterId = "c" })
        {
            SessionId = "bad id\r\nX-Injected: 1",
        };

        string affinity = session.CacheAffinity;

        Assert.Equal(32, affinity.Length);
        Assert.DoesNotContain('\r', affinity);
        Assert.DoesNotContain(' ', affinity);
    }

    /// <summary>
    /// 子会话用自己的，<b>不继承父会话</b>。与 pi / opencode 不同，那两家的子会话是同一份上下文
    /// 的分支；我们的子会话是一等会话，工具集按档位裁剪、角色卡可以另配，
    /// 前缀从第一个 token 就分叉，绑同一个亲和值只会把两套前缀挤到同一台副本上。
    /// </summary>
    [Fact]
    public void SubSession_KeepsItsOwnAffinity()
    {
        ChatSession parent = new("父", new CharacterData { CharacterId = "parent" });
        ChatSession child = new("子", new CharacterData { CharacterId = "child" }) { ParentSessionId = parent.SessionId };

        Assert.NotEqual(parent.CacheAffinity, child.CacheAffinity);
    }

    /// <summary>
    /// 装配那份是会话值的<b>搬运</b>，不是另算一遍：主代理请求与旁路请求（压缩、群背景、审批判定）
    /// 一旦落到不同副本上，比不带亲和更糟。
    /// </summary>
    [Fact]
    public void ProfileFromSession_CarriesTheSameValue()
    {
        ChatSession session = new("会话", new CharacterData { CharacterId = "c" });

        Assert.Equal(session.CacheAffinity, AgentBuildProfile.FromSession(session).CacheAffinity);
    }

    private static async Task PostAsync(HttpClient client)
    {
        using LlmRequestScope.Scope scope =
            LlmRequestScope.Push(new LlmRequestContext { SessionAffinity = "affinity-123" });
        using HttpResponseMessage response = await client.PostAsync(
            "http://localhost/v1/chat/completions", new StringContent("{}", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class CapturingServer : HttpMessageHandler
    {
        private readonly Dictionary<string, string?> _headers = new(StringComparer.OrdinalIgnoreCase);

        public string? Header(string name) => _headers.GetValueOrDefault(name);

        /// <summary>这一发实际带出去的亲和头名（去重后，便于断言「没声明就不写」）</summary>
        public string[] SentHeaderNames => [.. _headers.Where(x => x.Value != null).Select(x => x.Key)];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // 请求对象发完就被回收,取值要在这一刻抄下来
            foreach (var header in request.Headers)
            {
                _headers[header.Key] = header.Value.FirstOrDefault();
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }
    }
}
