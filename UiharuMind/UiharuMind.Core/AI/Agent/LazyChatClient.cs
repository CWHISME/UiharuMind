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
using UiharuMind.Core.AI.Core;

namespace UiharuMind.Core.AI.Agent;

/// <summary>
/// 惰性模型客户端:构建 agent 时不要求模型就绪,每次请求时解析全局当前模型。
/// 使会话历史的加载/回放与模型状态解耦(重启后无模型也能看历史),
/// 顶栏切换模型后无需重建 agent,下一次请求自动生效。
/// </summary>
public class LazyChatClient : IChatClient
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    private readonly Func<ModelRunningData?>? _sessionModelSource; //会话级模型来源,优先于全局当前模型

    public LazyChatClient(Func<ModelRunningData?>? sessionModelSource = null)
    {
        _sessionModelSource = sessionModelSource;
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        IChatClient client = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        return await client.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IChatClient client = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        await foreach (ChatResponseUpdate update in client
                           .GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return LlmManager.Instance.CurrentRunningModel?.ChatClient?.GetService(serviceType, serviceKey);
    }

    public void Dispose()
    {
        // 不持有底层客户端所有权
    }

    /// <summary>
    /// 解析当前模型客户端;远程模型启动中时限时等待就绪
    /// </summary>
    private async Task<IChatClient> ResolveAsync(CancellationToken cancellationToken)
    {
        // 会话绑定的模型优先(如识图技能为临时会话解析的视觉模型),否则用全局当前模型
        ModelRunningData? model = _sessionModelSource?.Invoke() ?? LlmManager.Instance.CurrentRunningModel;
        // 无模型时按偏好自动选一个(优先运行中/收藏/远程);已有模型但远程未启动时顺带拉起
        if (model == null) LlmManager.Instance.TryCheckModelRunning(false, ref model);
        if (model == null) throw new InvalidOperationException("Model is not running.");

        DateTimeOffset deadline = DateTimeOffset.Now + ReadyTimeout;
        while (model.ChatClient == null && DateTimeOffset.Now < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return model.ChatClient ?? throw new InvalidOperationException("Model is not running.");
    }
}