/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 一个构建完成的 agent 及宿主需要的配套句柄
/// </summary>
public sealed class AgentHandle : IAsyncDisposable
{
    /// <summary>Harness agent(标准 AIAgent 契约)</summary>
    public AIAgent Agent { get; }

    private readonly ShellExecutor? _shellExecutor;

    /// <summary>todo 提供器(侧栏进度)</summary>
    public TodoProvider? Todos => Agent.GetService<TodoProvider>();

    /// <summary>plan/execute 模式提供器</summary>
    public AgentModeProvider? Mode => Agent.GetService<AgentModeProvider>();

    /// <summary>运行中插话通道</summary>
    public MessageInjectingChatClient? MessageInjector => Agent.GetService<MessageInjectingChatClient>();

    /// <summary>
    /// 本会话装配好的对话选项（系统提示词、工具集与采样参数）。
    ///
    /// 旁路请求（如写交接文档）必须带上同一份：请求体的前缀是「system + 工具定义 + 消息」，
    /// 少任何一段，前缀都会从那里开始与常规轮次岔开，服务端的前缀缓存整个作废——
    /// 而旁路的那一发恰好是占用最高时发出的、最大的一次请求。
    /// </summary>
    public ChatOptions? ChatOptions { get; }

    public AgentHandle(AIAgent agent, ShellExecutor? shellExecutor, ChatOptions? chatOptions = null)
    {
        Agent = agent;
        _shellExecutor = shellExecutor;
        ChatOptions = chatOptions;
    }

    public async ValueTask DisposeAsync()
    {
        if (_shellExecutor != null) await _shellExecutor.DisposeAsync().ConfigureAwait(false);
    }
}
