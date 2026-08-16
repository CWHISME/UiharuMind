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
using UiharuMind.Core.AI.Execution.Mcp;

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

    /// <summary>
    /// 本会话实际挂上的 MCP 工具集与它们的 server 归属。
    ///
    /// 右栏「能力」面板据此展示。<b>归属只能从这里拿</b>：<see cref="ChatOptions"/> 里的工具已经
    /// 拍平成一个列表，哪个来自哪个 server 无从反推（撞名改过名的更是如此）。
    /// </summary>
    public McpToolSet Mcp { get; }

    /// <summary>
    /// 装配好的工具，每项带能力归属（见 <see cref="AgentToolEntry"/>）。
    /// <see cref="ChatOptions"/> 里那份是它拍平后的结果，归属只在这里。
    /// </summary>
    public IReadOnlyList<AgentToolEntry> ToolEntries { get; }

    /// <summary>
    /// <see cref="ChatOptions"/> 里那段系统提示的分段清单（拼接现场登记，见
    /// <see cref="AgentInstructionsComposer.Compose"/>）。能力面板按段报占用、并据此展示全文。
    /// </summary>
    public IReadOnlyList<AgentPromptSegment> PromptSegments { get; }

    private AgentCapabilitySnapshot? _capabilities; //惰性算一次,见 Capabilities

    /// <summary>
    /// 本次装配的能力快照，含每轮<b>固定开销</b>（系统提示 + 工具定义）的估算。
    ///
    /// <b>惰性算一次并缓存</b>：分词有代价，而现在有三个消费方都要它——能力面板、压缩层的
    /// 历史额度（输入预算减去它）、交接文档水位的有效占用。从前它只服务能力面板，
    /// 因而每次刷新现算；接进压缩之后就落在发消息路径上了，每轮现算是不能接受的。
    ///
    /// 不需要失效机制：handle 本身只在装配快照变化时重建，这份跟着一起重算。
    /// 并发首访最坏是算两遍，<c>Capture</c> 是纯函数，结果等价，因此不上锁。
    /// </summary>
    public AgentCapabilitySnapshot Capabilities =>
        _capabilities ??= AgentCapabilitySnapshot.Capture(ToolEntries, Mcp, PromptSegments);

    /// <summary>
    /// 我们自己估的本轮输入（固定开销 + 历史）。交接文档水位拿它与服务端报的取大，
    /// 进度条拿它画「服务端未计入」那一段。见 <see cref="TurnInputEstimate"/> 与 ADR 0009
    /// </summary>
    public TurnInputEstimate InputEstimate { get; }

    public AgentHandle(AIAgent agent, ShellExecutor? shellExecutor, ChatOptions? chatOptions = null,
        McpToolSet? mcp = null, IReadOnlyList<AgentToolEntry>? toolEntries = null,
        IReadOnlyList<AgentPromptSegment>? promptSegments = null,
        TurnInputEstimate? inputEstimate = null)
    {
        Agent = agent;
        _shellExecutor = shellExecutor;
        ChatOptions = chatOptions;
        Mcp = mcp ?? McpToolSet.Empty;
        ToolEntries = toolEntries ?? [];
        PromptSegments = promptSegments ?? [];
        // 自带一个空的:子代理与测试不传,读到的固定开销为 0,等于退回不扣的旧行为
        InputEstimate = inputEstimate ?? new TurnInputEstimate();
        InputEstimate.BindTo(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_shellExecutor != null) await _shellExecutor.DisposeAsync().ConfigureAwait(false);
    }
}
