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
using UiharuMind.Core.AI.Execution.ToolCall;

namespace UiharuMind.Core.AI.Execution;

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
        ChatResponse response = await client.GetResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);

        HashSet<string>? toolNames = CollectToolNames(options);
        if (toolNames == null) return response;
        foreach (ChatMessage message in response.Messages)
        {
            message.Contents = RecoverTextToolCalls(message.Contents, toolNames);
        }

        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IChatClient client = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        HashSet<string>? toolNames = CollectToolNames(options);
        if (toolNames == null)
        {
            await foreach (ChatResponseUpdate update in client
                               .GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }

            yield break;
        }

        // 文本工具调用恢复:GLM 线格式的调用可能以纯文本漏进正文或思考通道
        // (服务端未翻译成结构化 tool_calls 时),在此转回 FunctionCallContent,
        // 使上层框架的函数调用循环照常执行。两通道各持解析器,互不串流。
        TextToolCallStreamParser textParser = new(toolNames);
        TextToolCallStreamParser reasoningParser = new(toolNames);

        await foreach (ChatResponseUpdate update in client
                           .GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            List<AIContent> rebuilt = new(update.Contents.Count);
            foreach (AIContent content in update.Contents)
            {
                switch (content)
                {
                    case TextContent { Text.Length: > 0 } tc:
                        Append(rebuilt, textParser.Feed(tc.Text), isReasoning: false);
                        break;
                    case TextReasoningContent { Text.Length: > 0 } rc:
                        Append(rebuilt, reasoningParser.Feed(rc.Text), isReasoning: true);
                        break;
                    default:
                        rebuilt.Add(content);
                        break;
                }
            }

            update.Contents = rebuilt;
            yield return update;
        }

        List<AIContent> tail = new();
        Append(tail, textParser.Flush(), isReasoning: false);
        Append(tail, reasoningParser.Flush(), isReasoning: true);
        if (tail.Count > 0)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, tail);
        }
    }

    /// <summary>
    /// 请求带工具时收集工具名集合(恢复器的启用条件与命中闸);无工具返回 null
    /// </summary>
    private static HashSet<string>? CollectToolNames(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 } tools) return null;
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (AITool tool in tools)
        {
            if (!string.IsNullOrEmpty(tool.Name)) names.Add(tool.Name);
        }

        return names.Count > 0 ? names : null;
    }

    private static void Append(List<AIContent> target,
        (string Text, List<FunctionCallContent> Calls) parsed, bool isReasoning)
    {
        if (parsed.Text.Length > 0)
        {
            target.Add(isReasoning ? new TextReasoningContent(parsed.Text) : new TextContent(parsed.Text));
        }

        target.AddRange(parsed.Calls);
    }

    /// <summary>非流式响应的同款恢复(每条消息独立解析并冲刷)</summary>
    private static IList<AIContent> RecoverTextToolCalls(IList<AIContent> contents, HashSet<string> toolNames)
    {
        TextToolCallStreamParser textParser = new(toolNames);
        TextToolCallStreamParser reasoningParser = new(toolNames);
        List<AIContent> rebuilt = new(contents.Count);
        foreach (AIContent content in contents)
        {
            switch (content)
            {
                case TextContent { Text.Length: > 0 } tc:
                    Append(rebuilt, textParser.Feed(tc.Text), isReasoning: false);
                    break;
                case TextReasoningContent { Text.Length: > 0 } rc:
                    Append(rebuilt, reasoningParser.Feed(rc.Text), isReasoning: true);
                    break;
                default:
                    rebuilt.Add(content);
                    break;
            }
        }

        Append(rebuilt, textParser.Flush(), isReasoning: false);
        Append(rebuilt, reasoningParser.Flush(), isReasoning: true);
        return rebuilt;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        // 与 ResolveAsync 同一优先级:会话绑定模型优先,否则框架读到的能力元数据来自另一个模型
        ModelRunningData? model = _sessionModelSource?.Invoke() ?? LlmManager.Instance.CurrentRunningModel;
        return model?.ChatClient?.GetService(serviceType, serviceKey);
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
        // 无模型时按偏好自动选一个(优先运行中/收藏/远程);已有模型但远程未启动时顺带拉起。
        // 必须走会写回全局的那个重载:传局部变量的 ref 只会填上局部变量,
        // 全局当前模型仍是空——顶栏因此不跟着变,token 统计也拿不到模型与上下文上限
        if (model == null)
        {
            LlmManager.Instance.TryCheckModelRunning(false);
            model = LlmManager.Instance.CurrentRunningModel;
        }
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