using System.Net.Http.Headers;
using UiharuMind.Core.AI;

namespace UiharuMind.Core.AI.Net.Transport;

/// <summary>
/// 会话亲和头的写与说：<b>公共头 + 协议追加 + 供应商追加</b>，三处合起来才是这一发要写的头名。
///
/// <para>分层是刻意的。公共头（<see cref="SessionAffinity.CommonHeaders"/>）广谱，所有远程模型默认都发；
/// 协议层补这条线固有的那一条（Anthropic 线认 <c>X-Claude-Code-Session-Id</c>）；
/// 供应商层补自己独有的（opencode 系认 <c>x-opencode-session</c>，缺了直接 400）。
/// 早先只有供应商那一层，结果是「没声明 = 一个都不发」——绝大多数远程模型连最通用的那条都没带上。</para>
///
/// <para>头名与值都只在这里碰：HTTP 处理器不该知道「opencode 认 x-opencode-session」这类供应商知识，
/// 也不该猜端点。本地模型关掉整层，一个头都不发。</para>
/// </summary>
internal sealed class SessionAffinityHeaders
{
    private readonly string[] _names; //本端点要写的头名；空=不发

    /// <param name="sends">这家发不发亲和（整体开关，关掉时三个来源一起不发）</param>
    /// <param name="protocol">协议层追加的头名，如 Anthropic 线的 <c>X-Claude-Code-Session-Id</c></param>
    /// <param name="extra">供应商声明的追加头名；空或 null 表示这家没有特有的</param>
    public SessionAffinityHeaders(bool sends, IReadOnlyList<string>? protocol, IReadOnlyList<string>? extra) =>
        _names = sends ? Merge(protocol, extra) : [];

    /// <summary>
    /// 公共集打头，再协议、再供应商，<b>按大小写不敏感去重</b>：先到先得，
    /// 供应商把 <c>x-session-id</c> 再写一遍也只是同一个头，不会变成两个。
    /// </summary>
    private static string[] Merge(IReadOnlyList<string>? protocol, IReadOnlyList<string>? extra)
    {
        List<string> names = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        Append(SessionAffinity.CommonHeaders);
        Append(protocol);
        Append(extra);
        return [.. names];

        void Append(IReadOnlyList<string>? source)
        {
            if (source == null) return;
            foreach (string name in source)
            {
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name)) names.Add(name);
            }
        }
    }

    /// <summary>
    /// 把本次请求的亲和值写到每一个声明的头上。
    ///
    /// <b>不动请求体</b>：头不参与前缀缓存，加它不会让任何一份已有缓存失效。
    /// 值恒为 32 个十六进制字符（<see cref="SessionAffinity.Of"/>），不需要在发请求这层校验。
    /// </summary>
    /// <param name="request">待发出的请求</param>
    /// <param name="affinity">本次的亲和值；空表示本发没有会话，一个头都不写</param>
    public void Apply(HttpRequestMessage request, string? affinity)
    {
        if (_names.Length == 0 || string.IsNullOrEmpty(affinity)) return;

        foreach (string header in _names)
        {
            request.Headers.TryAddWithoutValidation(header, affinity);
        }
    }

    /// <summary>
    /// 这一发的亲和<b>到底发出去了什么</b>，三种情况分得清——命中率不对时，它们的修法完全不同：
    /// <c>(off)</c> 这一层整个关着（本地模型、显式关掉的供应商），一个都没发，别去查网关为什么不认；
    /// <c>(none)</c> 开着但本次没有会话（能力预览），也发不出去；
    /// 否则是真正写到网线上的那个值。
    ///
    /// <b>不能只打传进来的亲和值</b>：那是「手上有的值」，
    /// 与「实际发了什么」不是一回事——没声明头名的供应商照样有值，打出来会让人以为发了。
    /// 三种取值都不带空格，好让 <c>cache-audit.py</c> 按整词分组。
    /// </summary>
    /// <param name="affinity">本次的亲和值，与 <see cref="Apply"/> 收到的是同一个</param>
    /// <returns>日志里要显示的亲和</returns>
    public string Describe(string? affinity)
    {
        if (_names.Length == 0) return "(off)";
        return affinity ?? "(none)";
    }

    /// <summary>
    /// 响应头上与亲和有关的那个头（名字里含 affinity 的），有就返回 <c>名字=值</c>，没有返回 null。
    ///
    /// 两种语义完全不同的亲和光看名字分不出来：<b>回声</b>——网关把发出去的值原样带回来，没增加信息；
    /// <b>服务端票据</b>——网关自己签发一个值，客户端下一发该带回去才落到同一台后端。
    /// 后者客户端算不出来（值不是会话 id 的函数），现在也没有供应商声明要回传它。
    /// Sensenova 的响应里出现过 <c>affinity</c> 头，属于哪一种，拿发出去的值与它对照一次就定了，
    /// 所以和 <see cref="Describe"/> 打在同一行。用 <c>=</c> 而不是 <c>:</c> 是给 <c>cache-audit.py</c> 让路——
    /// 它按 <c>affinity: 值</c> 分组，这一行不该被认成第二个亲和值。
    /// </summary>
    /// <param name="response">服务端响应</param>
    /// <returns>形如 <c>affinity=…</c> 的文本；一个都没命中为 null</returns>
    public static string? Echo(HttpResponseMessage response) =>
        Find(response.Headers) ?? Find(response.Content?.Headers);

    private static string? Find(HttpHeaders? headers)
    {
        if (headers == null) return null;
        foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
        {
            if (header.Key.Contains("affinity", StringComparison.OrdinalIgnoreCase))
                return $"{header.Key}={string.Join(',', header.Value)}";
        }

        return null;
    }
}
