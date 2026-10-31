using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Net.Transport;

/// <summary>
/// 一条 wire 协议的事实表：端点后缀、地址怎么归一、响应要不要按行修。
///
/// <para>存在的理由是把「这个端点/这个模型说的是哪种协议」收成<b>一处数据</b>。早先这件事同时散在
/// 六个 <c>Resolve*</c>/<c>Is*</c> 方法、<see cref="WireHttpHandler"/> 的地址嗅探、
/// <c>RemoteModelManager</c> 的 switch 与 <see cref="Response.ResponseSanitizer"/> 的判据里，
/// 四处靠人手工对齐——对不上就是「模型声明走 messages、请求却打到 chat/completions」那种看不懂的 404。</para>
///
/// <para>为什么是表而不是策略接口：三条协议各自绑死一个不同的 SDK 客户端
/// （<c>ChatClient</c> / <c>ResponsesClient</c> / <c>AnthropicClient</c>），共同下界只有
/// <c>IChatClient</c>，把构造过程塞进接口只是把分支从一处搬到三处。真正重复的是<b>数据</b>，不是行为。</para>
/// </summary>
/// <param name="Format">对应的模型声明值</param>
/// <param name="EndpointSuffix">端点路径后缀，同时也是地址归一的补全目标</param>
/// <param name="SanitizesResponse">
/// 这条线的响应要不要逐行修（只有 chat 要：它得改 <c>finish_reason</c> / <c>tool_calls.type</c> /
/// 思考键名，并从 <c>reasoning_details</c> 里切票据）。另两条由各自 SDK 解析，既没有这些键，
/// 也没有 <c>[DONE]</c> 哨兵——套上去只会白扫每行 JSON，还把正常收尾误报成「连接被掐」
/// </param>
/// <param name="SessionHeaders">
/// 这条线固有要追加的会话头名。公共头由 <see cref="SessionAffinityHeaders"/> 统一发，
/// 这里只放「换成这条线就该补上的那一条」：Anthropic 认 <c>X-Claude-Code-Session-Id</c>，
/// 另两条线的 chat 形状没有自己的专名，留空即可
/// </param>
internal sealed record WireProtocol(EApiFormat Format, string EndpointSuffix, bool SanitizesResponse,
    IReadOnlyList<string>? SessionHeaders = null)
{
    /// <summary>
    /// 把用户填的地址补成这条协议的完整端点。
    ///
    /// 配置既允许填完整接口，也允许只填服务根路径（<c>https://x</c> 或 <c>https://x/v1</c>）；
    /// SDK 自己会拼标准路径，所以这里必须把三种形态收敛成同一个结果，
    /// 否则请求会打到 <c>/v1/v1/chat/completions</c>。已填到端点那一层时原样保留。
    /// </summary>
    /// <param name="address">用户配置的地址</param>
    /// <returns>补全路径后的地址构造器</returns>
    public UriBuilder Resolve(string address)
    {
        var builder = new UriBuilder(address);
        string path = builder.Path.TrimEnd('/');
        if (path.EndsWith(EndpointSuffix, StringComparison.OrdinalIgnoreCase)) return builder; //已填完整,别再补一层
        // 尾段是另一条线的端点:换成本协议的。原样发出去就是「messages 的请求体打到 chat 端点」——
        // 服务端 404/400，而配置里两个字段各自看着都对
        foreach (WireProtocol other in WireProtocols.All)
        {
            if (other == this) continue; //本协议前面已经原样返回了
            if (!path.EndsWith(other.EndpointSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            builder.Path = path[..^other.EndpointSuffix.Length] + EndpointSuffix;
            return builder;
        }

        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) builder.Path = path + EndpointSuffix;
        else if (string.IsNullOrEmpty(path)) builder.Path = "/v1" + EndpointSuffix;
        return builder;
    }

    /// <summary>这个地址是不是这条协议的端点（尾斜杠不算差别）</summary>
    /// <param name="uri">已补全的端点地址</param>
    /// <returns>是则 true</returns>
    public bool Matches(Uri uri) =>
        uri.AbsolutePath.TrimEnd('/').EndsWith(EndpointSuffix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 三条协议的登记处。<b>全仓唯一</b>的「格式 → 端点/清洗」事实来源。
/// </summary>
internal static class WireProtocols
{
    /// <summary>OpenAI 兼容的 /chat/completions</summary>
    public static readonly WireProtocol Chat = new(EApiFormat.ChatCompletions, "/chat/completions", true);

    /// <summary>OpenAI 的 /responses</summary>
    public static readonly WireProtocol Responses = new(EApiFormat.Responses, "/responses", false);

    /// <summary>Anthropic 的 /v1/messages</summary>
    public static readonly WireProtocol Messages =
        new(EApiFormat.Messages, "/messages", false, ["X-Claude-Code-Session-Id"]);

    /// <summary>三条线。地址归一用它判断「尾段是不是别的协议」，别处不必再列一遍</summary>
    public static readonly WireProtocol[] All = [Chat, Responses, Messages];

    /// <summary>按模型声明取协议；认不出的值退到 chat（默认值就是 chat）</summary>
    /// <param name="format">模型声明的格式</param>
    /// <returns>对应的协议</returns>
    public static WireProtocol For(EApiFormat format) => format switch
    {
        EApiFormat.Responses => Responses,
        EApiFormat.Messages => Messages,
        _ => Chat,
    };

    /// <summary>
    /// 按地址反查协议。<b>只给拿不到模型的调用方用</b>（本地服务、模型目录、状态探测）——
    /// 有模型时必须以 <see cref="For"/> 为准：用户把地址填成 <c>/v1</c> 时这里返回 null，
    /// 退到 chat 就会把 messages 形状的请求体打到 chat 端点上。
    /// </summary>
    /// <param name="uri">端点地址</param>
    /// <returns>认得的协议；认不出时为 null</returns>
    public static WireProtocol? Detect(Uri uri)
    {
        // 后缀越长越具体，先比长的；三条互不为后缀，顺序只为稳妥
        if (Messages.Matches(uri)) return Messages;
        if (Responses.Matches(uri)) return Responses;
        return Chat.Matches(uri) ? Chat : null;
    }
}
