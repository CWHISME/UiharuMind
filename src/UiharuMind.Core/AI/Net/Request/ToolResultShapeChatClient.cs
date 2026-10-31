/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Net.Request;

/// <summary>
/// 发请求前把工具结果的形态统一成<b>纯文本</b>。
///
/// <para><b>要修的是什么</b>：工具结果是 <c>FunctionResultContent.Result</c>。字符串结果在两条路上形态不同——
/// 刚跑完时框架把它包成 <c>JsonElement{String}</c>（带引号、<c>\n</c> 与 <c>\"</c> 字面量），
/// 读盘时 <see cref="ChatContentNormalizer"/> 又还原成 <see cref="string"/>。于是同一条历史有两种字节，
/// 前缀从第一条工具消息起就分叉，整段缓存作废。2026-10-29（Read/Grep 那批）与 2026-10-31（Shell）各踩一次。</para>
///
/// <para><b>为什么挂在客户端出口而不是工具工厂</b>：工具有一堆来源——<c>ToolJson</c> 造的、框架
/// <c>AsAIFunction</c> 造的（shell）、MCP 客户端给的、以后还会加。逐个工具补一道，
/// 补漏一个就是一次全价重算，而且补没补看不出来。这里只有一道：<b>凡是走 HTTP 的模型，每一发请求都经过它</b>，
/// 与工具是谁造的、走哪条协议线（chat / responses / messages）都无关。
/// （进程内的 <c>LLamaSharpChatClient</c> 不经此处：它把 tool 角色映射成 <c>Unknown</c>、没有工具调用，
/// 不会收到工具结果。）</para>
///
/// <para><b>就地改而不是克隆</b>：改的就是历史里那一个对象，与读盘时 <see cref="ChatContentNormalizer"/>
/// 的做法同口径——两边都落到同一个形态，才谈得上「同一条历史一种字节」。
/// 幂等：还原过一次之后 <see cref="FunctionResultContent.Result"/> 已经是 string，下一发原样跳过。</para>
///
/// <para><b>只动 tool 消息</b>：助手消息的空正文与思考碎片在流式侧已经处理过（见 <c>LazyChatClient</c>），
/// 这里再扫一遍只会白花时间，且可能动到不该动的消息。</para>
/// </summary>
internal sealed class ToolResultShapeChatClient : DelegatingChatClient
{
    /// <param name="innerClient">底层 chat 客户端</param>
    public ToolResultShapeChatClient(IChatClient innerClient) : base(innerClient)
    {
    }

    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(Normalize(messages), options, cancellationToken);

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(Normalize(messages), options, cancellationToken);

    /// <summary>
    /// 就地把 tool 消息里被序列化成 <c>JsonElement{String}</c> 的结果还原成 <see cref="string"/>。
    /// 已经是列表时零分配（常见情形：历史本身就是一份列表）。
    /// </summary>
    /// <param name="messages">请求消息</param>
    /// <returns>同一份消息</returns>
    private static IReadOnlyList<ChatMessage> Normalize(IEnumerable<ChatMessage> messages)
    {
        IReadOnlyList<ChatMessage> source = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        foreach (ChatMessage message in source)
        {
            if (message.Role == ChatRole.Tool) ChatContentNormalizer.Normalize(message);
        }

        return source;
    }
}
