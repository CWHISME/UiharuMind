using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net.Json;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Net.Request;

/// <summary>
/// Anthropic Messages 线的思考参数：把 <c>thinking</c> 写进请求体。
///
/// <para><b>为什么不能走 SDK 的原生参数</b>：官方 SDK 的 <c>RawRepresentationFactory</c>
/// 默认返回 <c>null</c>（意思是「用默认路径造请求体」），拿不到它造好的那份——
/// 而 <c>MessageCreateParams.Thinking</c> 是 init-only 属性，只能在对象初始化器里设。
/// 想用它就得自己再造一份 <c>MessageCreateParams</c>，那意味着把 ChatMessage → MessageParam
/// 的整套翻译自己写一遍（上游 Python 侧那份实现有 1900 行）。
///
/// <para>所以走<b>字节级改写</b>——和 chat 线同一套路（那边是 <see cref="ChatCompletionsRequestRewriter"/>）。
/// 改的只是根对象上一个属性，用 <see cref="Utf8JsonSplice"/> 插进去，其余字节一字不动。</para>
///
/// <para><b>Anthropic 的形状与别家不同</b>：它要的是 <c>thinking: {type: enabled, budget_tokens: N}</c>
/// ——一个 <b>token 预算</b>，不是档位名。而且两条硬约束：
/// <list type="bullet">
/// <item><description>开了思考后 <c>temperature</c> 必须是 1（我们不发这个字段，默认就是 1）；</description></item>
/// <item><description><c>budget_tokens</c> 必须小于 <c>max_tokens</c>，且不小于 1024。</description></item>
/// </list>
/// 预算装不下时不写 <c>thinking</c>——写一个服务端会拒的组合比不思考更糟。</para>
/// </summary>
internal sealed class AnthropicThinkingHandler : DelegatingHandler
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

    /// <summary>
    /// 指定内层处理器
    /// </summary>
    /// <param name="level">思考档位 wire 值；空 = 不干预</param>
    /// <param name="maxTokens">本次请求的输出预算</param>
    /// <param name="inner">内层处理器</param>
    public AnthropicThinkingHandler(string? level, int maxTokens, HttpMessageHandler inner) : base(inner)
    {
        _level = level;
        _maxTokens = maxTokens;
    }

    /// <summary>
    /// 这一发要不要写 <c>thinking</c>。
    ///
    /// <para><b>「不干预」就是不写</b>——与另外两条线同一口径：没选档位就别替模型做决定。
    /// Anthropic 的默认是不思考。</para>
    /// </summary>
    private bool ShouldWrite => !string.IsNullOrEmpty(_level);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (ShouldWrite && request.Content != null)
        {
            // 走池化缓冲：几十万字的请求体不进大对象堆（与 chat 线同一口径）
            int hint = request.Content.Headers.ContentLength is { } length && length < int.MaxValue
                ? (int)length
                : 64 * 1024;
            using PooledByteWriter buffer = new(hint);
            await request.Content.CopyToAsync(buffer.AsStream(), cancellationToken).ConfigureAwait(false);

            using PooledByteWriter output = new(buffer.WrittenCount + 64);
            if (TryRewrite(buffer.WrittenSpan, output))
            {
                // 换掉正文时把原有的头带过去——Content-Type 丢了服务端可能不认。
                // <b>Content-Length 不能照抄</b>：上面读它取过缓冲提示，这个 getter 把旧长度
                // 缓存了下来，于是枚举时会一并带过来；而新正文比旧正文长，照抄的结果是
                // HttpClient 直接抛 "content would exceed Content-Length"（实测），请求根本发不出去。
                // 长度交给 SegmentedHttpContent 自己按正文算
                System.Net.Http.Headers.HttpContentHeaders headers = request.Content.Headers;
                request.Content = new SegmentedHttpContent(output.WrittenSpan);
                foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
                {
                    if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 在请求体根对象上追加 <c>thinking</c>。写不了就不改（原样发出）。
    /// </summary>
    /// <param name="json">SDK 造好的请求体</param>
    /// <param name="output">改写后的请求体写到这里</param>
    /// <returns>改写了返回 true</returns>
    private bool TryRewrite(ReadOnlySpan<byte> json, IBufferWriter<byte> output)
    {
        if (!TryBuildThinking(out string? thinking)) return false;

        var reader = new Utf8JsonReader(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;

        // 找到顶层对象里最后一个属性的终点，在它后面追加——与 chat 线的
        // 「追加在末尾」同一惯例（追加在前面会让前缀缓存的起点整个挪位）
        int lastPropertyEnd = -1;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            reader.Read();
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
            lastPropertyEnd = (int)reader.BytesConsumed;
        }

        if (lastPropertyEnd < 0) return false; //空对象：不改（改不出合法结果）
        if (lastPropertyEnd > json.Length) return false;

        byte[] addition = Encoding.UTF8.GetBytes($",\"thinking\":{thinking}");
        Utf8JsonSplice splice = default;
        splice.Replace(lastPropertyEnd, lastPropertyEnd, addition);
        splice.WriteTo(json, output);
        return true;
    }

    /// <summary>
    /// 造出 <c>thinking</c> 的值。装不下（预算小于最小值、或不小于输出预算）时给 null——
    /// 那时宁可不思考，也不发一个服务端会拒的组合。
    /// </summary>
    private bool TryBuildThinking(out string? thinking)
    {
        thinking = null;
        string level = _level ?? string.Empty;

        // 显式关闭：Anthropic 认 disabled
        if (ThinkingEfforts.IsOff(level))
        {
            thinking = "{\"type\":\"disabled\"}";
            return true;
        }

        int budget = BudgetFor(level);
        if (budget > _maxTokens - 1) budget = _maxTokens - 1; //必须严格小于输出预算
        if (budget < MinimumBudget) return false; //输出预算装不下思考：不写

        thinking = $"{{\"type\":\"enabled\",\"budget_tokens\":{budget}}}";
        return true;
    }
}
