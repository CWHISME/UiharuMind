/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 转录器：把 <see cref="AIContent"/> 流装配成可渲染的会话条目序列。
/// 实时流与历史回放是同一个类的两个实例，区别只在构造时交代的落点集合与是否订阅
/// <see cref="UsageObserved"/>——因此不再需要「按落点判空来分叉」这种隐式开关。
///
/// 藏在里面的复杂度：八种内容的分派、正文流里 &lt;think&gt; 段的分离、工具调用与结果按
/// CallId 的配对回写、流段的开合、审批请求的待决与本轮收集。
/// </summary>
public sealed class ConversationTranscript
{
    private readonly IList<ConversationItemBase> _target;
    private readonly Func<TextConversationItem> _createAssistantItem;
    private readonly Action<string>? _rememberShellPattern;
    private readonly ThinkTagStreamParser _thinkParser = new();
    private readonly List<ApprovalRequestItem> _pending = new(); //待决审批(可被整体取消)
    private readonly List<ApprovalRequestItem> _round = new(); //本轮新增审批(供运行循环回应)
    private TextConversationItem? _streamingText;
    private ThinkingItem? _streamingThinking;

    /// <summary>思考段收尾时是否自动折叠（流式进行中一律保持展开）</summary>
    public bool AutoCollapseThinking { get; set; }

    /// <summary>尚未回应的审批请求</summary>
    public IReadOnlyList<ApprovalRequestItem> PendingApprovals => _pending;

    /// <summary>观察到响应用量（回放实例不订阅，累计口径由回放方全量统计）</summary>
    public event Action<UsageDetails>? UsageObserved;

    /// <summary>调用了框架内务工具（todo 之类），调用方据此刷新对应面板</summary>
    public event Action? HousekeepingToolCalled;

    /// <param name="target">条目落点：实时流直写界面集合，回放写入构建缓冲</param>
    /// <param name="createAssistantItem">助手气泡工厂（名字与头像取自当前会话角色）</param>
    /// <param name="rememberShellPattern">「本会话放行同类命令」的落点</param>
    public ConversationTranscript(
        IList<ConversationItemBase> target,
        Func<TextConversationItem> createAssistantItem,
        Action<string>? rememberShellPattern = null)
    {
        _target = target;
        _createAssistantItem = createAssistantItem;
        _rememberShellPattern = rememberShellPattern;
    }

    /// <summary>
    /// 装配一段内容
    /// </summary>
    /// <param name="content">来自执行者的一段内容</param>
    public void Apply(AIContent content)
    {
        switch (content)
        {
            case TextReasoningContent reasoning when !string.IsNullOrEmpty(reasoning.Text):
                AppendThinking(reasoning.Text);
                break;

            case TextContent text when !string.IsNullOrEmpty(text.Text):
                // 本地/部分远程模型把 <think> 混在正文流里,经解析器分离成思考条目
                _thinkParser.Feed(text.Text, AppendText, AppendThinking);
                break;

            case FunctionCallContent call:
                CloseSegment();
                if (AgentContentFormatter.IsHousekeepingTool(call.Name))
                {
                    HousekeepingToolCalled?.Invoke();
                    break;
                }

                _target.Add(new ToolCallItem
                {
                    CallId = call.CallId,
                    ToolName = call.Name,
                    IconGlyph = AgentContentFormatter.GetToolIcon(call.Name),
                    ArgumentSummary = AgentContentFormatter.SummarizeArguments(call),
                    ArgumentsJson = call.Arguments == null
                        ? string.Empty
                        : string.Join("\n", call.Arguments.Select(x => $"{x.Key}: {x.Value}")),
                });
                break;

            case FunctionResultContent result:
                if (_target.OfType<ToolCallItem>().LastOrDefault(x => x.CallId == result.CallId) is { } item)
                {
                    item.IsRunning = false;
                    item.IsSuccess = result.Exception == null;
                    item.ResultText = result.Result?.ToString() ?? result.Exception?.Message ?? string.Empty;
                }

                break;

            case ToolApprovalRequestContent approvalRequest:
                CloseSegment();
                ApprovalRequestItem approvalItem = new(approvalRequest)
                {
                    RememberShellPatternCallback = pattern => _rememberShellPattern?.Invoke(pattern),
                };
                _target.Add(approvalItem);
                _pending.Add(approvalItem);
                _round.Add(approvalItem);
                break;

            case ErrorContent error:
                _target.Add(new ErrorItem { Message = error.Message });
                break;

            case UsageContent usage:
                UsageObserved?.Invoke(usage.Details);
                break;
        }
    }

    /// <summary>
    /// 收尾当前流段：冲刷解析器残留、标记文本气泡完成、按设置折叠思考段
    /// </summary>
    public void CloseSegment()
    {
        _thinkParser.Complete(AppendText, AppendThinking);
        if (_streamingText != null) _streamingText.IsDone = true;
        if (_streamingThinking != null && AutoCollapseThinking) _streamingThinking.IsExpanded = false;
        _streamingText = null;
        _streamingThinking = null;
    }

    /// <summary>
    /// 回放收尾：历史里的工具调用一律已结束，未回应的审批按拒绝处理
    /// </summary>
    public void FinalizeReplay()
    {
        CloseSegment();
        foreach (ToolCallItem call in _target.OfType<ToolCallItem>().Where(x => x.IsRunning))
        {
            call.IsRunning = false;
        }

        foreach (ApprovalRequestItem approval in _target.OfType<ApprovalRequestItem>().Where(x => !x.IsResolved))
        {
            approval.CancelAsDeny();
        }
    }

    /// <summary>
    /// 取本轮新增的审批请求。它们仍留在待决清单里（回应期间用户可能点停止），
    /// 直到 <see cref="ResolveApprovals"/> 把它们移出。
    /// </summary>
    /// <returns>本轮新增的审批请求</returns>
    public IReadOnlyList<ApprovalRequestItem> TakeRoundApprovals()
    {
        List<ApprovalRequestItem> list = _round.ToList();
        _round.Clear();
        return list;
    }

    /// <summary>
    /// 标记一批审批已回应完毕
    /// </summary>
    /// <param name="approvals">已回应的审批请求</param>
    public void ResolveApprovals(IEnumerable<ApprovalRequestItem> approvals)
    {
        List<ApprovalRequestItem> resolved = approvals.ToList();
        _pending.RemoveAll(resolved.Contains);
    }

    /// <summary>
    /// 把全部待决审批按拒绝处理（用户点停止）
    /// </summary>
    public void CancelPendingApprovals()
    {
        foreach (ApprovalRequestItem approval in _pending.ToList())
        {
            approval.CancelAsDeny();
        }
    }

    /// <summary>
    /// 回到初始状态（切换会话）。不清空落点集合，由调用方决定其生命周期。
    /// </summary>
    public void Reset()
    {
        _pending.Clear();
        _round.Clear();
        _streamingText = null;
        _streamingThinking = null;
        _thinkParser.Reset();
    }

    private void AppendText(string delta)
    {
        if (_streamingText == null) _target.Add(_streamingText = _createAssistantItem());
        _streamingText.Append(delta);
    }

    private void AppendThinking(string delta)
    {
        // 流式进行中保持展开,能看到它在想什么;段落收尾时按设置折叠
        if (_streamingThinking == null) _target.Add(_streamingThinking = new ThinkingItem { IsExpanded = true });
        _streamingThinking.Append(delta);
    }
}
