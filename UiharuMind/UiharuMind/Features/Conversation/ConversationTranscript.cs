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
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Threading;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 转录器：把 <see cref="AIContent"/> 流装配成可渲染的会话条目序列。
/// 实时流与历史回放是同一个类的两个实例，区别只在构造时交代的落点集合
/// ——因此不再需要「按落点判空来分叉」这种隐式开关。
///
/// 藏在里面的复杂度：十种内容的分派、正文流里 &lt;think&gt; 段的分离、工具调用与结果按
/// CallId 的配对回写、流段的开合、审批请求的待决与本轮收集。
///
/// 流段的边界<b>不由本类推演</b>：一次服务调用结束时执行者发 <see cref="MessageBoundaryContent"/>，
/// 用户消息被消费时发 <see cref="UserMessageContent"/>——本类只照着收段、画气泡。
/// 遇到工具调用与 think/text 切换仍然收段，那是同一条消息之内的形状，回放走的也是同一规则。
///
/// 它是 <see cref="ITurnSink"/> 的界面侧实现——<see cref="TurnDriver"/> 只认那五个成员，
/// 因此不认识本类，也不认识任何条目类型。
/// </summary>
public sealed class ConversationTranscript : ITurnSink
{
    private readonly IList<ConversationItemBase> _target;
    private readonly IReadOnlyList<ConversationItemBase>? _renderedBefore; //更早已渲染出去的条目(增量装配时用于跨批配对)
    private readonly Func<TextConversationItem> _createAssistantItem;
    private readonly Func<ChatMessage, TextConversationItem?> _createUserItem;
    private readonly Action<string>? _rememberShellPattern;
    private readonly Func<string?>? _workspaceRootSource; //审批卡片预演 diff 要用它解析相对路径
    private readonly ThinkTagStreamParser _thinkParser = new();
    private readonly List<ApprovalRequestItem> _pending = new(); //待决审批(可被整体取消)
    private readonly List<ApprovalRequestItem> _round = new(); //本轮新增审批(供运行循环回应)
    private readonly bool _isUiBound; //落点是否为界面绑定集合(回放缓冲是普通 List,不是)
    private TextConversationItem? _streamingText;
    private ThinkingItem? _streamingThinking;
    private bool _offThreadReported; //跨线程写入只报一次,否则流式期间会刷屏

    /// <summary>思考段收尾时是否自动折叠（流式进行中一律保持展开）</summary>
    public bool AutoCollapseThinking { get; set; }

    /// <summary>尚未回应的审批请求</summary>
    public IReadOnlyList<ApprovalRequestItem> PendingApprovals => _pending;

    /// <summary>调用了框架内务工具（todo 之类），调用方据此刷新对应面板</summary>
    public event Action? HousekeepingToolCalled;

    /// <summary>一条用户消息被模型消费并画出来了（插话的待发提示据此撤掉）</summary>
    public event Action<ChatMessage>? UserMessageRendered;

    /// <summary>
    /// 画出了一张审批卡。子会话窗口靠它认领嵌套审批：卡片背后的请求与登记处的是同一对象，
    /// 命中即把卡的回应接到登记项上。父会话自己的审批卡命中不了（没登记过），原样不动。
    /// </summary>
    public event Action<ApprovalRequestItem>? ApprovalRequestCreated;

    /// <summary>
    /// 一张工具卡认到了自己的子会话（参数为子会话标识）。调用方据此补上它此刻的运行态
    /// ——中途挂上来的窗口收不到之前那几下状态变化。
    /// </summary>
    public event Action<string>? SubSessionAttached;

    /// <param name="target">条目落点：实时流直写界面集合，回放写入构建缓冲</param>
    /// <param name="createAssistantItem">助手气泡工厂（名字与头像取自当前会话角色）</param>
    /// <param name="createUserItem">
    /// 用户气泡工厂（消息 → 已接好来源的条目；返回 null 表示这条不画，比如框架注入的空消息）。
    /// 省略则不画用户消息——回放缓冲不需要，历史里的用户消息由调用方按种类自己画
    /// </param>
    /// <param name="rememberShellPattern">「本会话放行同类命令」的落点</param>
    /// <param name="workspaceRootSource">当前工作目录的来源（现取现用：会话中途改工作目录也能跟上）</param>
    /// <param name="renderedBefore">
    /// 本次装配<b>之前</b>就已经渲染出去的条目。只在增量装配（外驱会话每次服务调用补渲染一段）时给：
    /// 那时工具调用与它的结果落在<b>不同批</b>里，只认本批的话结果永远配不上调用，
    /// 卡片会一直停在「历史里没有这次调用的结果」，直到整份回放才对上。
    /// </param>
    public ConversationTranscript(
        IList<ConversationItemBase> target,
        Func<TextConversationItem> createAssistantItem,
        Action<string>? rememberShellPattern = null,
        Func<string?>? workspaceRootSource = null,
        IReadOnlyList<ConversationItemBase>? renderedBefore = null,
        Func<ChatMessage, TextConversationItem?>? createUserItem = null)
    {
        _target = target;
        _renderedBefore = renderedBefore;
        _isUiBound = target is INotifyCollectionChanged;
        _createAssistantItem = createAssistantItem;
        _createUserItem = createUserItem ?? (_ => null);
        _rememberShellPattern = rememberShellPattern;
        _workspaceRootSource = workspaceRootSource;
    }

    // [诊断探针] 界面绑定集合必须在 UI 线程写入:Avalonia 的弱事件簿记不是线程安全的,
    // 跨线程写会在 WeakHashList 内部抛 NullReferenceException(已实际撞到一次)。
    // 这里只观测不纠正——先确认症状是否真是线程亲和性问题,再决定把 marshal 放在哪一层。
    // 回放缓冲是普通 List,不触发通知,因此不参与判定。
    private void VerifyUiAccess()
    {
        if (!_isUiBound || _offThreadReported || Dispatcher.UIThread.CheckAccess()) return;

        _offThreadReported = true;
        Log.Warning($"ConversationTranscript applied off the UI thread (managed thread " +
                    $"{Environment.CurrentManagedThreadId}); Avalonia collection notifications are not thread-safe.");
    }

    /// <summary>
    /// 装配一段内容
    /// </summary>
    /// <param name="content">来自执行者的一段内容</param>
    public void Apply(AIContent content)
    {
        VerifyUiAccess();
        switch (content)
        {
            case TextReasoningContent reasoning:
            case TextContent text:
                // 思考边界走 ThinkingBoundary：空增量忽略、<think> 混排经解析器分离，
                // 规则与计时器同一份，改一边另一边跟着变
                ThinkingBoundary.Dispatch(content, _thinkParser, AppendText, AppendThinking, CloseSegment);
                break;

            case FunctionCallContent call:
                ThinkingBoundary.Dispatch(call, _thinkParser, AppendText, AppendThinking, CloseSegment);
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
                    ArgumentSummary = AgentContentFormatter.SummarizeArguments(call, _workspaceRootSource?.Invoke()),
                    FilePath = AgentContentFormatter.GetFilePath(call),
                    ArgumentsJson = call.Arguments == null
                        ? string.Empty
                        : string.Join("\n", call.Arguments.Select(x => $"{x.Key}: {x.Value}")),
                });
                break;

            case FunctionResultContent result:
                if (FindCall(result.CallId) is { } item)
                {
                    item.IsRunning = false;
                    // 取消补写的结果要显示成失败:它没跑完,绿点会是假消息。
                    // 判据只能取正文——Exception 带 [JsonIgnore],存盘再读回来就没了
                    item.IsSuccess = result.Exception == null && !ToolCallCancellation.IsCancelled(result);
                    item.ResultText = result.Result?.ToString() ?? result.Exception?.Message ?? string.Empty;
                    // 回放历史时 SubSessionStartedContent 早已随当时那一轮消失,
                    // 入口只能从落了盘的结果文本里认回来
                    if (!item.HasSubSession) item.SubSessionId = ToolCallItem.ParseSubSessionId(item.ResultText);
                }

                break;

            // 一次委派开始了:把子会话标识挂到对应卡片上,用户此刻就能点开看。
            // 过程本身不走这里——它是子会话自己的历史
            case SubSessionStartedContent started:
                if (FindCall(started.CallId) is { } launched)
                {
                    launched.SubSessionId = started.SubSessionId;
                    SubSessionAttached?.Invoke(started.SubSessionId);
                }

                break;

            // 一次服务调用到此为止:之后的正文属于下一条助手消息,不能续进当前气泡
            case MessageBoundaryContent boundary:
                ThinkingBoundary.Dispatch(boundary, _thinkParser, AppendText, AppendThinking, CloseSegment);
                break;

            case UserMessageContent consumed:
                // 这处的收段是有条件的:同一条已画过就早退,不收——不能走统一分发,
                // 否则重复消费会把正开着的思考段提前切断
                RenderUserMessage(consumed.Message);
                break;

            case ToolApprovalRequestContent approvalRequest:
                ThinkingBoundary.Dispatch(approvalRequest, _thinkParser, AppendText, AppendThinking, CloseSegment);
                ApprovalRequestItem approvalItem = new(approvalRequest, _workspaceRootSource?.Invoke())
                {
                    RememberShellPatternCallback = pattern => _rememberShellPattern?.Invoke(pattern),
                };
                _target.Add(approvalItem);
                _pending.Add(approvalItem);
                _round.Add(approvalItem);
                ApprovalRequestCreated?.Invoke(approvalItem);
                break;

            case ErrorContent error:
                _target.Add(new ErrorItem { Message = error.Message });
                break;

            case UsageContent:
                break; //用量走 ETurnNotice.UsageObserved 通知链路,转录器不消费
        }
    }


    /// <summary>
    /// 模型消费了一条用户消息：在<b>此刻</b>的位置画出它。
    /// 发送方那一格发送时已经用同一个实例画过（乐观显示），按引用认出来就不再画。
    /// </summary>
    private void RenderUserMessage(ChatMessage message)
    {
        if (HasUserItemFor(message)) return;

        CloseSegment();
        if (_createUserItem(message) is not { } item) return;

        _target.Add(item);
        UserMessageRendered?.Invoke(message);
    }

    private bool HasUserItemFor(ChatMessage message)
    {
        return _target.Concat(_renderedBefore ?? [])
            .Any(x => x is TextConversationItem { IsUser: true } && ReferenceEquals(x.SourceMessage, message));
    }

    /// <summary>
    /// 派出去的子会话正在等（或不再等）用户点审批：把提示挂到派活那张卡上。
    ///
    /// 状态取自 <c>SessionRunRegistry</c>（子代理那一轮本来就登记在册），这里只负责上屏——
    /// 不然盯着父会话的用户只看见一个转圈的卡片，干等到超时还莫名其妙。
    /// </summary>
    /// <param name="subSessionId">子会话标识</param>
    /// <param name="waiting">是否正在等用户点选</param>
    public void NoteSubSessionApprovalWait(string subSessionId, bool waiting)
    {
        if (subSessionId.Length == 0) return;
        foreach (ToolCallItem call in _target.OfType<ToolCallItem>())
        {
            if (call.SubSessionId == subSessionId) call.IsWaitingApproval = waiting;
        }
    }

    /// <summary>按 CallId 找回工具卡片：先看本次装配的产出，再看更早已渲染出去的那些</summary>
    private ToolCallItem? FindCall(string? callId)
    {
        return _target.OfType<ToolCallItem>().LastOrDefault(x => x.CallId == callId)
               ?? _renderedBefore?.OfType<ToolCallItem>().LastOrDefault(x => x.CallId == callId);
    }

    /// <summary>
    /// 收尾当前流段：冲刷解析器残留、标记文本气泡完成、按设置折叠思考段
    /// </summary>
    public void CloseSegment()
    {
        _thinkParser.Complete(AppendText, AppendThinking);
        // 两个条目的 Message 都是节流更新的,收尾必须显式冲刷,否则最后几个字会短暂缺失
        CloseText();
        CloseThinking();
    }

    /// <summary>
    /// 收尾并取走「当前正在流的那一段正文」。
    ///
    /// 取消时用它落库：本轮更早的那些段落已经由框架逐次服务调用各自落过盘了
    /// （每完成一次调用就持久化一次），只有正在流的这一段随着失败一起丢掉。
    /// 拿界面条目去猜是哪一段不行——条目要等整轮结束才与历史配对，
    /// 「还没配对」并不等于「还没落盘」，照那个判据会把已经落盘的段落再写一遍。
    /// </summary>
    /// <returns>正在流的正文；当时没在流正文（比如卡在工具调用上）则为 null</returns>
    public string? TakeStreamingText()
    {
        TextConversationItem? streaming = _streamingText;
        CloseSegment(); //先收尾:正文是节流更新的,不冲刷的话最后几个字还在缓冲里
        return string.IsNullOrWhiteSpace(streaming?.Message) ? null : streaming!.Message;
    }

    /// <summary>
    /// 把仍挂着「运行中」的工具卡片收掉。
    ///
    /// 工具卡片的运行态是靠 <c>FunctionResultContent</c> 落下的，而中途停止意味着那条结果
    /// 永远不会来——不收的话卡片上的转圈会一直转下去，看着像还在跑。
    /// 标成失败而不是成功：它确实没跑完，绿点会是假消息。
    /// </summary>
    /// <param name="note">写进卡片结果区的说明</param>
    public void StopRunningToolCalls(string note)
    {
        StopRunningToolCalls(_target, note);
    }

    private static void StopRunningToolCalls(IEnumerable<ConversationItemBase> items, string note)
    {
        foreach (ToolCallItem call in items.OfType<ToolCallItem>())
        {
            if (!call.IsRunning) continue;

            call.IsRunning = false;
            call.IsSuccess = false;
            if (string.IsNullOrEmpty(call.ResultText)) call.ResultText = note;
        }
    }


    /// <summary>
    /// 回放收尾：历史里的工具调用一律已结束，未回应的审批按拒绝处理
    /// </summary>
    /// <param name="unfinishedNote">
    /// 写进「历史里没有配对结果」那些卡片的说明。回放分不清它是被用户停掉的还是崩在半路，
    /// 所以措辞要中性——新的取消会由 <see cref="ToolCallCancellation"/> 补上真正的结果消息，
    /// 走到这里的多半是那之前留下的旧会话。
    /// </param>
    public void FinalizeReplay(string unfinishedNote)
    {
        CloseSegment();
        StopRunningToolCalls(unfinishedNote);

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
        CloseThinking();
        // 纯空白增量不开新气泡：部分模型调工具时 content 会带 "\n\n" 这类空白，
        // 开了气泡就是头像 + 空白边框，工具卡之外的纯噪音。有实质正文进来时再开。
        if (_streamingText == null && string.IsNullOrWhiteSpace(delta)) return;
        if (_streamingText == null) _target.Add(_streamingText = _createAssistantItem());
        _streamingText.Append(delta);
    }

    private void AppendThinking(string delta)
    {
        CloseText();
        // 流式进行中保持展开,能看到它在想什么;段落收尾时按设置折叠
        if (_streamingThinking == null) _target.Add(_streamingThinking = new ThinkingItem { IsExpanded = true });
        _streamingThinking.Append(delta);
    }

    /// <summary>
    /// 收尾正文段。<b>思考段一开始就得收</b>：条目是按到达顺序进集合的，而 <c>_streamingText</c>
    /// 指着的那条气泡排在思考卡<b>前面</b>——不收尾的话，思考之后的正文会续进那条气泡里，
    /// 界面上就成了「回答在思考过程上面」。而历史里它们是两条消息，重开会话立刻对不上
    /// （这正是子会话窗口那个「实时看是一坨、重开就分开了」）。
    ///
    /// 同一条消息里 think/text 交替也照此拆分——回放走的是同一个管线、同一套规则，
    /// 两边形状因此恒等。
    /// </summary>
    private void CloseText()
    {
        if (_streamingText is not { } text) return;

        text.Flush(); //正文是节流更新的,不冲刷的话最后几个字会留在缓冲里
        // 空白兜底：空白可能分多次增量进来（首段建泡时拦不住），收尾时整条仍是空白
        // （无图、无注入正文）就摘掉——调工具前后那几个 "\n" 不该留一个空气泡。
        // 用户气泡不走这里（工厂直建），用户亲手发的空白不受影响。
        if (string.IsNullOrWhiteSpace(text.Message) && !text.HasImage && !text.HasInjectedText)
            _target.Remove(text);
        else
            text.IsDone = true;
        _streamingText = null;
    }

    /// <summary>收尾思考段：冲刷并按设置折叠</summary>
    private void CloseThinking()
    {
        if (_streamingThinking is not { } thinking) return;

        thinking.Flush();
        if (AutoCollapseThinking) thinking.IsExpanded = false;
        _streamingThinking = null;
    }
}
