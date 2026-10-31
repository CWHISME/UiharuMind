namespace UiharuMind.Core.AI.Net.Request;

/// <summary>
/// 缓存断点要打在哪。<b>协议无关的决策</b>：这里只说「圈到哪个位置为止」，
/// 至于怎么落到 wire 上（Anthropic 的 <c>cache_control</c>、别家的 <c>cache</c> 标记）
/// 由各条线自己渲染。
///
/// <para>只有显式断点型的服务端需要它。自动前缀型（DeepSeek、多数 OpenAI 兼容服务）由服务端
/// 自己按内容找前缀，客户端打什么标记都不改变命中。</para>
/// </summary>
internal enum ECacheBreakpoint
{
    /// <summary>最后一个工具定义：把「系统提示 + 工具 schema」整段圈进来</summary>
    ToolLast,

    /// <summary>最后一条消息的最后一个内容块：把整段历史圈进来</summary>
    MessageLast,
}

/// <summary>
/// 默认断点清单与拼装用的常量。
///
/// <para><b>为什么只有两个</b>：这两处已经覆盖「系统提示 + 工具 + 全部历史」这整段前缀，
/// 中间再插断点不增加可复用范围，只多占额度（Anthropic 一次最多 4 个）。</para>
/// </summary>
internal static class CacheBreakpointSet
{
    /// <summary>默认清单，顺序即落盘顺序</summary>
    internal static readonly ECacheBreakpoint[] Default = [ECacheBreakpoint.ToolLast, ECacheBreakpoint.MessageLast];

    /// <summary><see cref="Models.ECacheRetention.Long"/> 对应的 ttl 值</summary>
    internal const string LongTtl = "1h";
}
