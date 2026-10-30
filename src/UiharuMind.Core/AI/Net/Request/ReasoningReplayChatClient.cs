/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Reasoning;

namespace UiharuMind.Core.AI.Net.Request;

/// <summary>
/// 发请求前把历史里的思考<b>绑到它自己那条消息上</b>（ADR 0101）。
///
/// <para>只挂 chat 线（<c>ApiFormat.ChatCompletions</c>）：responses 与 messages 两条线的密文
/// 由 SDK 自己从 <see cref="TextReasoningContent.ProtectedData"/> 读出来往返，本来就没有要补的东西。</para>
///
/// <para><b>零克隆是常态</b>：模型不要回填、或者历史里一条思考都没有时，原样返回入参那一份；
/// 只有真有消息要绑时才重建列表，且只重建被绑的那几条。</para>
/// </summary>
internal sealed class ReasoningReplayChatClient : DelegatingChatClient
{
    private readonly ILlmModel? _model;

    /// <param name="innerClient">底层 chat 客户端</param>
    /// <param name="model">目标模型：决定要不要回填、回填到哪个键名</param>
    public ReasoningReplayChatClient(IChatClient innerClient, ILlmModel? model) : base(innerClient)
    {
        _model = model;
    }

    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(Bind(messages), options, cancellationToken);

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(Bind(messages), options, cancellationToken);

    /// <summary>
    /// 把带思考的消息换成挂好思考的那一份。换不了（复刻不了这条消息）就原样留着——
    /// 这一轮这条不回填，与旧做法在数量闸门触发时的口径一致：宁缺毋滥。
    /// </summary>
    /// <param name="messages">请求消息</param>
    /// <returns>要发出去的消息；无需绑定时与入参同一份</returns>
    private IEnumerable<ChatMessage> Bind(IEnumerable<ChatMessage> messages)
    {
        if (!ReasoningReplayPolicy.ShouldReplay(_model)) return messages;

        IReadOnlyList<ChatMessage> source = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        string reasoningField = ReasoningReplayPolicy.FieldName(_model);

        List<ChatMessage>? bound = null;
        for (int i = 0; i < source.Count; i++)
        {
            ChatMessage message = source[i];
            (string? text, string? ticket) = ReasoningReplay.Read(message);
            ChatMessage? attached = text is null && ticket is null
                ? null
                : ChatCompletionMessageBinder.Attach(message, text, ticket, reasoningField);

            if (attached is null)
            {
                bound?.Add(message);
                continue;
            }

            if (bound is null)
            {
                bound = new List<ChatMessage>(source.Count);
                for (int head = 0; head < i; head++) bound.Add(source[head]);
            }

            bound.Add(attached);
        }

        return bound ?? source;
    }
}
