using System.Buffers;
using System.Text;
using System.Text.Json;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net.Json;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Net.Request;

/// <summary>
/// Anthropic Messages 线的请求体改写：写 <c>thinking</c>、按清单打缓存断点。
///
/// <para><b>为什么不能走 SDK 的原生参数</b>：官方 SDK 的 <c>RawRepresentationFactory</c> 虽然被适配层认，
/// 但它要的是<b>整份</b> <c>MessageCreateParams</c>（<c>MaxTokens</c>/<c>Model</c>/<c>Messages</c> 三个必填），
/// 适配层还会把自己翻译的消息追加在后面——用它就等于自己把 ChatMessage → MessageParam 翻一遍。
/// 而 <c>cache_control</c> 要打在 system / tools / messages 的<b>块</b>上，那些块也是适配层内部造的，
/// 类型化的路根本够不着。</para>
///
/// <para>所以走<b>字节级改写</b>——和 chat 线同一套路（那边是 <see cref="ChatCompletionsRequestRewriter"/>）。
/// 只插入、不改写已有字节，原文的转义与空白一字不动。</para>
///
/// <para><b>Anthropic 的思考形状与别家不同</b>：它要的是
/// <c>thinking: {type: enabled, budget_tokens: N}</c>——一个 <b>token 预算</b>，不是档位名。两条硬约束：
/// <c>budget_tokens</c> 必须小于 <c>max_tokens</c>、且不小于 1024，装不下就不写
/// （写一个服务端会拒的组合比不思考更糟）。</para>
///
/// <para><b>缓存断点</b>：Anthropic 不会自动缓存，客户端必须在「希望缓存到此为止」的块上放
/// <c>cache_control</c>。落点见 <see cref="ECacheBreakpoint"/>——只打最后一条工具定义与最后一条消息的
/// 最后一个块，这两处已覆盖「系统提示 + 工具 + 全部历史」。</para>
/// </summary>
internal sealed class AnthropicRequestHandler : DelegatingHandler
{
    /// <summary>Anthropic 允许的最小思考预算</summary>
    private const int MinimumBudget = 1024;

    /// <summary>档位 → token 预算</summary>
    private static int BudgetFor(string level) => level switch
    {
        "low" => 2048,
        "medium" => 8192,
        "high" => 16384,
        "xhigh" => 32768,
        "max" => 65535,
        _ => 8192,
    };

    private readonly string? _level; //档位值；空 = 不干预（沿用模型默认，即不思考）
    private readonly int _maxTokens; //本次请求的输出预算（thinking 的预算必须小于它）
    private readonly IReadOnlyList<ECacheBreakpoint>? _breakpoints; //要打哪些断点；空 = 一个都不打
    private readonly bool _sendsAffinity; //是否把会话亲和写进 metadata.user_id
    private readonly byte[] _cacheControl; //预拼好的 `,"cache_control":{…}`，插入时直接拼
    private readonly byte[] _cacheControlBare; //去掉前导逗号的那份：往空对象 `{}` 里插时用

    /// <summary>
    /// 指定内层处理器
    /// </summary>
    /// <param name="level">思考档位 wire 值；空 = 不干预</param>
    /// <param name="maxTokens">本次请求的输出预算</param>
    /// <param name="breakpoints">缓存断点清单；空 = 不打</param>
    /// <param name="cacheTtl">断点寿命（如 <c>1h</c>）；空 = 服务端默认</param>
    /// <param name="sendsAffinity">
    /// 是否把会话亲和写进 <c>metadata.user_id</c>。值与亲和头同源——网关按第一个匹配的信号认会话，
    /// 两处给不同值等于把自己分裂成两个会话
    /// </param>
    /// <param name="inner">内层处理器</param>
    public AnthropicRequestHandler(string? level, int maxTokens, IReadOnlyList<ECacheBreakpoint>? breakpoints,
        string? cacheTtl, bool sendsAffinity, HttpMessageHandler inner) : base(inner)
    {
        _level = level;
        _maxTokens = maxTokens;
        _breakpoints = breakpoints;
        _sendsAffinity = sendsAffinity;
        string ttl = string.IsNullOrEmpty(cacheTtl) ? string.Empty : $",\"ttl\":\"{cacheTtl}\"";
        _cacheControl = Encoding.UTF8.GetBytes($",\"cache_control\":{{\"type\":\"ephemeral\"{ttl}}}");
        _cacheControlBare = _cacheControl[1..];
    }

    /// <summary>
    /// 这一发要不要改写。<b>「不干预」就是不写</b>——与另外两条线同一口径：没选档位就别替模型做决定，
    /// Anthropic 的默认是不思考。
    /// </summary>
    private bool ShouldWrite => !string.IsNullOrEmpty(_level) || _breakpoints is { Count: > 0 } || _sendsAffinity;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!ShouldWrite || request.Content == null) return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // 走池化缓冲：几十万字的请求体不进大对象堆（与 chat 线同一口径）
        int hint = request.Content.Headers.ContentLength is { } length && length < int.MaxValue
            ? (int)length
            : 64 * 1024;
        using PooledByteWriter buffer = new(hint);
        await request.Content.CopyToAsync(buffer.AsStream(), cancellationToken).ConfigureAwait(false);

        using PooledByteWriter output = new(buffer.WrittenCount + 64);
        // <b>改写失败也必须把正文重新挂回去</b>：上面那次 CopyToAsync 已经把原正文读走了，
        // 有些内容类型读第二次是空的——不重挂就是「发了个空请求体」这种最难查的失败。
        // 所以两条路共用同一次替换，区别只在用改写后的字节还是原始字节。
        // 改写本身也可能抛（reader 深度、切片越界），一并落到「发原始字节」这条路上
        bool rewritten;
        try
        {
            rewritten = TryRewrite(buffer.WrittenSpan, output);
        }
        catch (Exception e)
        {
            Log.Warning($"Anthropic request rewrite failed, sending the original body: {e.Message}");
            rewritten = false;
        }

        // 换掉正文时把原有的头带过去——Content-Type 丢了服务端可能不认。
        // <b>Content-Length 不能照抄</b>：上面读它取过缓冲提示，这个 getter 把旧长度
        // 缓存了下来，于是枚举时会一并带过来；而新正文与旧正文长度不同，照抄的结果是
        // HttpClient 直接抛 "content would exceed Content-Length"（实测），请求根本发不出去。
        // 长度交给 SegmentedHttpContent 自己按正文算
        System.Net.Http.Headers.HttpContentHeaders headers = request.Content.Headers;
        request.Content = new SegmentedHttpContent(rewritten ? output.WrittenSpan : buffer.WrittenSpan);
        foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
        {
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 在请求体上落下两件事：根对象追加 <c>thinking</c>、按清单在块上插入 <c>cache_control</c>。
    /// 一处都落不下就不改（原样发出）。
    /// </summary>
    /// <param name="json">SDK 造好的请求体</param>
    /// <param name="output">改写后的请求体写到这里</param>
    /// <returns>改写了返回 true</returns>
    private bool TryRewrite(ReadOnlySpan<byte> json, IBufferWriter<byte> output)
    {
        string? thinking = TryBuildThinking();
        // 空串当没有：写个空 user_id 只会让网关把几发请求并成同一个人，比不写更糟
        string? affinity = _sendsAffinity && !string.IsNullOrEmpty(LlmRequestScope.Current.SessionAffinity)
            ? LlmRequestScope.Current.SessionAffinity
            : null;
        bool wantsTools = Wants(ECacheBreakpoint.ToolLast);
        bool wantsMessages = Wants(ECacheBreakpoint.MessageLast);
        if (thinking == null && affinity == null && !wantsTools && !wantsMessages) return false;

        var reader = new Utf8JsonReader(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;

        Utf8JsonSplice splice = default;
        // 找顶层对象里最后一个属性的终点：根级新增（thinking / metadata）追加在它后面——与 chat 线的
        // 「追加在末尾」同一惯例（追加在前面会让前缀缓存的起点整个挪位）
        int lastPropertyEnd = -1;
        bool hasMetadata = false;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isTools = wantsTools && reader.ValueTextEquals("tools"u8);
            bool isMessages = wantsMessages && reader.ValueTextEquals("messages"u8);
            if (!hasMetadata && reader.ValueTextEquals("metadata"u8)) hasMetadata = true;
            reader.Read();
            if (isTools && reader.TokenType == JsonTokenType.StartArray) MarkLastTool(ref reader, json, ref splice);
            else if (isMessages && reader.TokenType == JsonTokenType.StartArray) MarkLastMessage(ref reader, json, ref splice);
            else SkipValue(ref reader);
            lastPropertyEnd = (int)reader.BytesConsumed;
        }

        if (lastPropertyEnd < 0 || lastPropertyEnd > json.Length) return false;

        // 根级新增拼成一段一次插入：同一位置插两次的先后顺序不值得依赖
        StringBuilder root = new();
        if (thinking != null) root.Append(",\"thinking\":").Append(thinking);
        // metadata.user_id 是 Anthropic 路径的会话亲和信号（Claude Code 就把会话 UUID 放这里）。
        // 已经带了就不覆盖——那是调用方显式给的，比我们推的优先
        if (affinity != null && !hasMetadata)
        {
            root.Append(",\"metadata\":{\"user_id\":\"")
                .Append(JsonEncodedText.Encode(affinity))
                .Append("\"}");
        }

        if (root.Length > 0) splice.Replace(lastPropertyEnd, lastPropertyEnd, Encoding.UTF8.GetBytes(root.ToString()));
        if (splice.Count == 0) return false;
        splice.WriteTo(json, output);
        return true;
    }

    /// <summary>清单里有没有这一项</summary>
    private bool Wants(ECacheBreakpoint breakpoint) => _breakpoints?.Contains(breakpoint) == true;

    /// <summary>
    /// 给最后一个工具定义打标：把「系统提示 + 工具 schema」整段圈进缓存。
    /// 没有工具（纯提示词会话）就什么都不做。
    /// </summary>
    private void MarkLastTool(ref Utf8JsonReader reader, ReadOnlySpan<byte> json, ref Utf8JsonSplice splice)
    {
        int lastEnd = -1;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                SkipValue(ref reader);
                continue;
            }

            reader.Skip();
            lastEnd = (int)reader.BytesConsumed;
        }

        if (lastEnd > 0) InsertBeforeClose(json, lastEnd, ref splice);
    }

    /// <summary>
    /// 给最后一条消息的最后一个内容块打标：把整段历史圈进缓存。
    /// 只看最后一条——前面每一条打了也复用不到（下一轮它们更靠前，断点会跟着往后移）。
    /// </summary>
    private void MarkLastMessage(ref Utf8JsonReader reader, ReadOnlySpan<byte> json, ref Utf8JsonSplice splice)
    {
        int lastStart = -1;
        int lastEnd = -1;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                SkipValue(ref reader);
                continue;
            }

            lastStart = (int)reader.TokenStartIndex;
            reader.Skip();
            lastEnd = (int)reader.BytesConsumed;
        }

        if (lastStart >= 0) MarkMessageContent(json, lastStart, lastEnd, ref splice);
    }

    /// <summary>
    /// 在一条消息的 <c>content</c> 上找可打标的块。三种形态都要认：
    /// 块数组（常态）、裸字符串（单条文本的简写）、以及其它（不动）。
    /// </summary>
    /// <param name="json">整份请求体</param>
    /// <param name="messageStart">这条消息对象的起点</param>
    /// <param name="messageEnd">这条消息对象的终点</param>
    /// <param name="splice">改动收集器</param>
    private void MarkMessageContent(ReadOnlySpan<byte> json, int messageStart, int messageEnd,
        ref Utf8JsonSplice splice)
    {
        var reader = new Utf8JsonReader(json[messageStart..messageEnd]);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isContent = reader.ValueTextEquals("content"u8);
            reader.Read();
            if (!isContent)
            {
                SkipValue(ref reader);
                continue;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                // 裸字符串没地方挂键，先包成块数组——只有最后一条会走到这里，
                // 前面的历史一个字节都不动
                int start = messageStart + (int)reader.TokenStartIndex;
                int end = messageStart + (int)reader.BytesConsumed;
                splice.Replace(start, end, WrapTextBlock(json[start..end], asArray: true));
                return;
            }

            if (reader.TokenType == JsonTokenType.StartArray) MarkLastBlock(ref reader, json, messageStart, ref splice);
            return;
        }
    }

    /// <summary>
    /// 在块数组里找<b>最后一个可打标的块</b>。跳过 thinking / redacted_thinking——
    /// 那两个位置服务端不认 cache_control，打在它们上面等于白打。
    /// </summary>
    private void MarkLastBlock(ref Utf8JsonReader reader, ReadOnlySpan<byte> json, int baseOffset,
        ref Utf8JsonSplice splice)
    {
        int blockStart = -1;
        int blockEnd = -1;
        bool isString = false;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                blockStart = baseOffset + (int)reader.TokenStartIndex;
                blockEnd = baseOffset + (int)reader.BytesConsumed;
                isString = true;
                continue;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                SkipValue(ref reader);
                continue;
            }

            int start = baseOffset + (int)reader.TokenStartIndex;
            reader.Skip();
            int end = baseOffset + (int)reader.BytesConsumed;
            if (!IsCacheableBlock(json[start..end])) continue;
            blockStart = start;
            blockEnd = end;
            isString = false;
        }

        if (blockStart < 0) return;
        if (isString) splice.Replace(blockStart, blockEnd, WrapTextBlock(json[blockStart..blockEnd], asArray: false));
        else InsertBeforeClose(json, blockEnd, ref splice);
    }

    /// <summary>
    /// 这个块能不能挂 <c>cache_control</c>：文本、图片、工具调用/结果、文档可以；
    /// thinking 与 redacted_thinking 不行。
    /// </summary>
    private static bool IsCacheableBlock(ReadOnlySpan<byte> block)
    {
        var reader = new Utf8JsonReader(block);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isType = reader.ValueTextEquals("type"u8);
            reader.Read();
            if (isType && reader.TokenType == JsonTokenType.String)
            {
                return reader.ValueTextEquals("text"u8) || reader.ValueTextEquals("tool_result"u8) ||
                       reader.ValueTextEquals("tool_use"u8) || reader.ValueTextEquals("image"u8) ||
                       reader.ValueTextEquals("document"u8);
            }

            SkipValue(ref reader);
        }

        return false;
    }

    /// <summary>
    /// 把一段裸字符串包成文本块（<paramref name="asArray"/> 时再包一层数组），
    /// 原字符串的字节原样保留——转义与空白都不重新编码。
    /// </summary>
    /// <param name="rawString">带引号的原始字符串字节</param>
    /// <param name="asArray">是否包成 <c>[{…}]</c>（<c>content</c> 本身是字符串时用）</param>
    private byte[] WrapTextBlock(ReadOnlySpan<byte> rawString, bool asArray)
    {
        ReadOnlySpan<byte> prefix = asArray ? "[{\"type\":\"text\",\"text\":"u8 : "{\"type\":\"text\",\"text\":"u8;
        ReadOnlySpan<byte> suffix = asArray ? "}]"u8 : "}"u8;

        byte[] result = new byte[prefix.Length + rawString.Length + _cacheControl.Length + suffix.Length];
        int at = 0;
        prefix.CopyTo(result);
        at += prefix.Length;
        rawString.CopyTo(result.AsSpan(at));
        at += rawString.Length;
        _cacheControl.CopyTo(result.AsSpan(at));
        at += _cacheControl.Length;
        suffix.CopyTo(result.AsSpan(at));
        return result;
    }

    /// <summary>
    /// 在一个对象的收尾花括号前插入 <c>cache_control</c>。<paramref name="end"/> 是对象之后的位置
    /// （<see cref="Utf8JsonReader.BytesConsumed"/>），往前跳过空白就是那个 <c>}</c>。
    ///
    /// <b>空对象要换一份不带前导逗号的标记</b>：<c>{}</c> 里插 <c>,"cache_control":…</c>
    /// 拼出来是 <c>{,"cache_control":…}</c>，那不是合法 JSON——整发请求会被服务端拒掉。
    /// </summary>
    private void InsertBeforeClose(ReadOnlySpan<byte> json, int end, ref Utf8JsonSplice splice)
    {
        int close = end - 1;
        while (close >= 0 && json[close] is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t') close--;
        if (close < 0 || json[close] != (byte)'}') return;

        int previous = close - 1;
        while (previous >= 0 && json[previous] is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t') previous--;
        bool isEmptyObject = previous >= 0 && json[previous] == (byte)'{';
        splice.Replace(close, close, isEmptyObject ? _cacheControlBare : _cacheControl);
    }

    /// <summary>
    /// 造出 <c>thinking</c> 的值。装不下（预算小于最小值、或不小于输出预算）时给 null——
    /// 那时宁可不思考，也不发一个服务端会拒的组合。
    /// </summary>
    private string? TryBuildThinking()
    {
        string level = _level ?? string.Empty;
        if (level.Length == 0) return null;

        // 显式关闭：Anthropic 认 disabled
        if (ThinkingEfforts.IsOff(level)) return "{\"type\":\"disabled\"}";

        int budget = BudgetFor(level);
        if (budget > _maxTokens - 1) budget = _maxTokens - 1; //必须严格小于输出预算
        if (budget < MinimumBudget) return null; //输出预算装不下思考：不写

        return $"{{\"type\":\"enabled\",\"budget_tokens\":{budget}}}";
    }

    // 停在值上时调用:容器整个跳过,标量什么都不用做
    private static void SkipValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
    }
}
