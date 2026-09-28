/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Harness;

// [MFA绕坑] 绕:插话被哪次服务调用带走,靠叶子在请求发出前报一声再去问队列 因:MessageInjectingChatClient 排空队列时不通知任何人 删除条件:框架提供「注入消息已被消费」回调
// [MFA绕坑] 绕:群插话不让说完的人续轮,靠叶子在说完的回复交回注入层之前报一声、由群轮把群插话撤掉 因:MessageInjectingChatClient 说完后见队列非空就续轮,不分插话来源 删除条件:框架允许按消息标记「不触发续轮」
/// <summary>
/// 挂在最内层的客户端：每次服务调用真正发出之前报一声；一次调用说完（不带要执行的工具调用）、
/// 交回注入层之前再报一声。
///
/// 存在的理由是插话气泡的时机：框架在调用下层之前就排空了注入队列，所以这一刻问队列，
/// 「已不在队列里」的就是被这次调用带走的。从前等这次调用的首段输出到了才问，
/// 气泡要晚一个首字延迟（上下文大时十几秒）。挂在叶子而不是紧贴注入层之下，
/// 是因为那一段管线由 Harness 自己拼、插不进去；中间隔着的几层都不碰注入队列，时刻等价。
///
/// 说完那一声的时机同理：注入层拿到回复之后才看队列空不空、要不要续轮，
/// 这一刻撤掉的插话就不会让模型为它再说一句（群轮用它，ADR 0049 修订）。
/// </summary>
internal sealed class ServiceCallSignalingChatClient : DelegatingChatClient
{
    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="innerClient">真正发请求的客户端</param>
    public ServiceCallSignalingChatClient(IChatClient innerClient) : base(innerClient)
    {
    }

    /// <summary>服务调用发出之前的回调；由运行方在一轮期间挂上，没有为 null</summary>
    public Func<CancellationToken, Task>? Starting { get; set; }

    /// <summary>服务调用说完、交回注入层之前的回调；由运行方在一轮期间挂上，没有为 null</summary>
    public Func<CancellationToken, Task>? Finishing { get; set; }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        await SignalAsync(Starting, cancellationToken).ConfigureAwait(false);
        ChatResponse response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        if (!response.Messages.Any(x => HasActionableFunctionCalls(x.Contents)))
            await SignalAsync(Finishing, cancellationToken).ConfigureAwait(false);
        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await SignalAsync(Starting, cancellationToken).ConfigureAwait(false);
        bool actionable = false;
        await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            actionable = actionable || HasActionableFunctionCalls(update.Contents);
            yield return update;
        }

        // 流在这里之后才对注入层结束，它此时才去看队列
        if (!actionable) await SignalAsync(Finishing, cancellationToken).ConfigureAwait(false);
    }

    // 与 MessageInjectingChatClient 判「要不要交给工具循环」同一判据
    private static bool HasActionableFunctionCalls(IList<AIContent> contents) =>
        contents.Any(x => x is FunctionCallContent { InformationalOnly: false });

    private static async Task SignalAsync(Func<CancellationToken, Task>? signal, CancellationToken cancellationToken)
    {
        if (signal == null) return;

        // 报信失败不能让这次请求跟着失败：最坏是插话气泡晚到，或说完的人多回一句
        try
        {
            await signal(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Warning($"Service call signal failed: {e.Message}");
        }
    }
}
