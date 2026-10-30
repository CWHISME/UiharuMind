using System.Buffers;
using UiharuMind.Core.AI.Net.Diagnostics;
using UiharuMind.Core.AI.Reasoning;

namespace UiharuMind.Core.AI.Net.Response;

/// <summary>
/// 把「OpenAI 兼容」服务端不规范的响应修成 SDK 能解析的样子，再交给下游。
///
/// 分成两路，按响应媒体类型定：流式（<c>text/event-stream</c>）逐行走
/// <see cref="SseSanitizingStream"/>；一次性 JSON 在这里就地替换正文。
/// 只有对话类端点的响应需要这一步——其它路径（模型目录、生图）的正文形状由各自的调用方负责。
/// </summary>
internal sealed class ResponseSanitizer
{
    private readonly bool _enabled; //不是需要修的协议就原样放过
    private readonly Action<string>? _reasoningFieldObserver; //观测到思考字段原名时的回调(按模型记录回传键名)

    /// <param name="enabled">
    /// 这条线要不要修。<b>只有 chat 要</b>：它得改 <c>finish_reason</c> / <c>tool_calls.type</c> /
    /// 思考键名，并从 <c>reasoning_details</c> 里切票据。responses 与 messages 的形状由各自 SDK 解析，
    /// 既没有这些键也没有 <c>[DONE]</c> 哨兵——套上去只会白扫每行 JSON，
    /// 还会让 <see cref="SseSanitizingStream"/> 把正常收尾误报成「连接被掐」。
    /// 这个判断来自 <see cref="Transport.WireProtocol.SanitizesResponse"/>，不在这里重推一遍
    /// </param>
    /// <param name="reasoningFieldObserver">思考字段观测回调；为 null 表示不关心</param>
    public ResponseSanitizer(bool enabled, Action<string>? reasoningFieldObserver)
    {
        _enabled = enabled;
        _reasoningFieldObserver = reasoningFieldObserver;
    }

    /// <summary>
    /// 按媒体类型清洗响应（就地换掉 <paramref name="response"/> 的正文）
    /// </summary>
    /// <param name="response">服务端响应</param>
    /// <param name="cancellationToken">取消标记</param>
    /// <returns>同一个响应对象（可能已换过正文）</returns>
    public async Task<HttpResponseMessage> SanitizeAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!_enabled) return response;

        // 这一发的「看见加密思考」回调：由 LazyChatClient 每发一次挂上，界面据此先开一张没有正文的思考卡。
        // 在这里取而不是构造时取——清洗器按端点长期存活，回调是逐请求的
        Action<byte[]>? encryptedReasoning = LlmRequestScope.Current.EncryptedReasoningObserved;

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType == "text/event-stream")
        {
            response.Content = new SseSanitizingContent(response.Content, _reasoningFieldObserver, encryptedReasoning);
        }
        else if (mediaType == "application/json")
        {
            HttpContent original = response.Content;
            byte[] json = await original.ReadAsByteArrayAsync(cancellationToken);
            // 一次性响应里出现加密条目同样是「它思考过」的证据，只是没有流式那个时机可言。
            // 交出的是原文（整段 reasoning_details 数组），上层原样存着下一发带回
            if (encryptedReasoning != null && ReasoningDetailExtractor.TryExtract(json, out byte[] ticket))
                encryptedReasoning(ticket);
            var fixedJson = new ArrayBufferWriter<byte>(json.Length + 64);
            if (ChatCompletionsResponseFixer.TryFix(json, fixedJson, out string? reasoningField))
            {
                response.Content = new ReadOnlyMemoryContent(fixedJson.WrittenMemory);
                response.Content.Headers.ContentType = original.Headers.ContentType;
                if (reasoningField != null) _reasoningFieldObserver?.Invoke(reasoningField);
                original.Dispose();
            }
        }

        return response;
    }
}
