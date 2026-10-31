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
    /// <summary>头名由三层合成：公共头 + 协议头 + 供应商追加，给了就都写，一个不多</summary>
    [Fact]
    public async Task DeclaredHeaders_AreWrittenWithTheSessionValue()
    {
        CapturingServer server = new();
        using HttpClient client = new(new WireHttpHandler("http://localhost/v1", server,
            affinityHeaders: ["x-opencode-session"], sendsAffinity: true));

        await PostAsync(client);

        Assert.Equal("affinity-123", server.Header("X-Session-Id"));
        Assert.Equal("affinity-123", server.Header("x-opencode-session"));
        Assert.Null(server.Header("session_id")); //body 字段，不在头里
    }

    /// <summary>整层关掉（本地服务、显式关掉的供应商）时一个头都不发</summary>
    [Fact]
    public async Task DisabledAffinity_SendsNothing()
    {
        CapturingServer server = new();
        using HttpClient client = new(new WireHttpHandler("http://localhost/v1", server));

        await PostAsync(client);

        Assert.Empty(server.SentHeaderNames);
    }

    /// <summary>
    /// 公共集默认发：没声明任何追加头的远程模型也带上 <c>X-Session-Id</c> 与 <c>x-session-affinity</c>。
    /// 广谱的那几条漏了就是整段冷启动，而「漏了哪个」从命中率上看不出来
    /// </summary>
    [Fact]
    public async Task CommonHeaders_AreSentWithoutAnyVendorDeclaration()
    {
        CapturingServer server = new();
        using HttpClient client = new(new WireHttpHandler("http://localhost/v1", server, sendsAffinity: true));

        await PostAsync(client);

        Assert.Equal(SessionAffinity.CommonHeaders.Length, server.SentHeaderNames.Length);
        foreach (string name in SessionAffinity.CommonHeaders)
        {
            Assert.Equal("affinity-123", server.Header(name));
        }
    }

    /// <summary>
    /// 大小写不是两回事：HTTP 头名不敏感，供应商把 <c>x-session-id</c> 再追加一遍也只是同一个头，
    /// 写成两个同名头、值再对不上，等于把会话打散
    /// </summary>
    [Fact]
    public async Task DuplicateHeaderNames_AreCollapsedIgnoringCase()
    {
        CapturingServer server = new();
        using HttpClient client = new(new WireHttpHandler("http://localhost/v1", server,
            affinityHeaders: ["x-session-id", "X-Session-Id"], sendsAffinity: true));

        await PostAsync(client);

        Assert.Equal(SessionAffinity.CommonHeaders.Length, server.SentHeaderNames.Length);
        Assert.Single(server.ValuesOf("x-session-id")); //同名头只写一次，值不会被叠成 "a, a"
    }

    /// <summary>没有会话（能力预览、旁路探测）时不写头：值都没有，写个空的只会让网关把它当另一个会话</summary>
    [Fact]
    public async Task NoAffinity_LeavesHeadersOff()
    {
        CapturingServer server = new();
        using HttpClient client = new(new WireHttpHandler("http://localhost/v1", server, sendsAffinity: true));

        using HttpResponseMessage response = await client.PostAsync(
            "http://localhost/v1/chat/completions", new StringContent("{}", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Null(server.Header("X-Session-Id"));
    }

    /// <summary>
    /// 公共层默认发、供应商只补自己独有的。远程模型一律开（本地模型另算）；
    /// 追加头只有 opencode 系那一条，其余都是 null——它们认的正是公共头。
    /// </summary>
    [Fact]
    public void RemoteModels_SendCommonHeaderAndOnlyVendorSpecificExtras()
    {
        foreach (Type type in RemoteModelConfigRegistry.ProviderTypes)
        {
            BaseRemoteModelConfig config = RemoteModelConfigRegistry.CreateDefault(type)!;
            Assert.True(config.SendsSessionAffinity); //远程模型默认开
        }

        Assert.Equal(["x-opencode-session"], new RemoteOpenCodeModelConfig().SessionAffinityHeaders);
        Assert.Null(new RemoteOpenRouterModelConfig().SessionAffinityHeaders);
        Assert.Null(new RemoteCommandCodeModelConfig().SessionAffinityHeaders);

        // body 字段会改字节（已有缓存失效一次），只由认它的供应商打开
        Assert.Equal(["session_id", "prompt_cache_key"], new RemoteOpenRouterModelConfig().SessionBodyFields);
        Assert.Equal(["prompt_cache_key"], new RemoteCommandCodeModelConfig().SessionBodyFields);
        Assert.Null(new RemoteOpenCodeModelConfig().SessionBodyFields);
        Assert.Null(new RemoteDeepSeekModelConfig().SessionBodyFields);
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
        private readonly List<(string Name, string Value)> _all = []; //原样抄下每个头名与值，重复的也留着

        public string? Header(string name) =>
            _all.LastOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

        /// <summary>这一发实际带出去的亲和头名（去重后，便于断言「没声明就不写」）</summary>
        public string[] SentHeaderNames =>
            [.. _all.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase)];

        /// <summary>某个头名下的全部值；写了两遍就有两个——同名头写重了在网线上会拼成 "a, a"</summary>
        public string[] ValuesOf(string name) =>
            [.. _all.Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Value)];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // 请求对象发完就被回收,取值要在这一刻抄下来
            foreach (var header in request.Headers)
            {
                foreach (string value in header.Value) _all.Add((header.Key, value));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }
    }
}
