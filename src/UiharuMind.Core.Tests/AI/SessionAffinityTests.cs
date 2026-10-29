using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.Core.LLM;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 会话亲和：同一会话的请求带同一组头，网关据此把它们路由到同一后端。
/// 前缀缓存按副本各存一份，落到没建过该前缀的副本上就是整段冷启动——
/// 命中率看着像坏了，其实是另一台机器在答。
///
/// 头不参与前缀缓存，所以这条链路加东西不会让任何一份已有缓存失效；
/// 反过来说，它能否奏效只能靠实测命中率验证，这里只钉住「发了什么」。
/// </summary>
public class SessionAffinityTests
{
    [Fact]
    public async Task Affinity_IsSentOnEveryRequestHeader()
    {
        CapturingServer server = new();
        using HttpClient client = new(new OpenAICompatibleHttpHandler("http://localhost/v1", server));

        LlmRequestContext.SessionAffinity = "affinity-123";
        try
        {
            using HttpResponseMessage response = await client.PostAsync(
                "http://localhost/v1/chat/completions", new StringContent("{}", Encoding.UTF8, "application/json"),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            LlmRequestContext.SessionAffinity = null;
        }

        Assert.Equal("affinity-123", server.Header("x-session-affinity"));
        Assert.Equal("affinity-123", server.Header("x-client-request-id"));
        Assert.Equal("affinity-123", server.Header("session_id"));
        Assert.Equal("affinity-123", server.Header("x-session-id")); //OpenRouter 只认这一条
    }

    [Fact]
    public async Task NoAffinity_LeavesHeadersOff()
    {
        CapturingServer server = new();
        using HttpClient client = new(new OpenAICompatibleHttpHandler("http://localhost/v1", server));

        using HttpResponseMessage response = await client.PostAsync(
            "http://localhost/v1/chat/completions", new StringContent("{}", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Null(server.Header("x-session-affinity"));
        Assert.Null(server.Header("x-client-request-id"));
        Assert.Null(server.Header("session_id"));
        Assert.Null(server.Header("x-session-id"));
    }

    /// <summary>
    /// 不合法的亲和值一律不发头。会话标识是可反序列化的字段，手改过的会话文件能塞进 CR/LF——
    /// <c>TryAddWithoutValidation</c> 不校验，那种值会让请求在写网线时直接抛，等于整个会话发不出请求。
    /// </summary>
    [Theory]
    [InlineData("bad\r\nX-Injected: 1")]
    [InlineData("has space")]
    [InlineData("")]
    public async Task MalformedAffinity_IsNotSentAtAll(string affinity)
    {
        CapturingServer server = new();
        using HttpClient client = new(new OpenAICompatibleHttpHandler("http://localhost/v1", server));

        LlmRequestContext.SessionAffinity = affinity;
        try
        {
            using HttpResponseMessage response = await client.PostAsync(
                "http://localhost/v1/chat/completions", new StringContent("{}", Encoding.UTF8, "application/json"),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            LlmRequestContext.SessionAffinity = null;
        }

        Assert.Null(server.Header("x-session-affinity"));
        Assert.Null(server.Header("session_id"));
    }

    /// <summary>
    /// 子会话用自己的，<b>不继承父会话</b>。与 pi / opencode 不同，那两家的子会话是同一份上下文
    /// 的分支；我们的子会话是一等会话，工具集按档位裁剪、角色卡可以另配，而工具块在 prompt 里
    /// 通常排在系统提示之前——前缀从第一个 token 就分叉，绑同一个亲和值换不来命中，
    /// 只会把两套不同的前缀挤到同一台副本上。
    /// </summary>
    [Fact]
    public void SubSession_KeepsItsOwnAffinity()
    {
        ChatSession parent = new("父", new CharacterData { CharacterId = "parent" });
        ChatSession child = new("子", new CharacterData { CharacterId = "child" }) { ParentSessionId = parent.SessionId };

        Assert.NotEqual(parent.CacheAffinity, child.CacheAffinity);
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
    /// 同一个会话有两条推导路径：主代理走 <see cref="AgentBuildProfile.CacheAffinity"/>，
    /// 旁路（压缩、群背景、审批判定）走 <see cref="ChatSession.CacheAffinity"/>。
    /// 两处一旦算出不同的值，主代理请求与旁路请求就落到不同副本上——比不带亲和更糟。
    /// </summary>
    [Fact]
    public void BothDerivations_Agree()
    {
        ChatSession session = new("会话", new CharacterData { CharacterId = "c" });

        Assert.Equal(session.CacheAffinity, AgentBuildProfile.FromSession(session).CacheAffinity);
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

    private sealed class CapturingServer : HttpMessageHandler
    {
        private readonly Dictionary<string, string?> _headers = new(StringComparer.OrdinalIgnoreCase);

        public string? Header(string name) => _headers.GetValueOrDefault(name);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // 请求对象发完就被回收,取值要在这一刻抄下来
            foreach (string name in new[] { "x-session-affinity", "x-client-request-id", "session_id", "x-session-id" })
            {
                _headers[name] = request.Headers.TryGetValues(name, out IEnumerable<string>? values)
                    ? values.FirstOrDefault()
                    : null;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }
    }
}
