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
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Generated;

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
    private readonly Func<ChatMessage, IReadOnlyList<TextConversationItem>> _createUserItems;
    private readonly Action<string>? _rememberShellPattern;
    private readonly Action<string>? _approveExtension; //「本会话允许某个扩展」的落点
    private readonly Func<AgentPathResolver?>? _pathsSource; //审批卡片预演 diff 要用它解析路径
    private readonly Func<string?>? _sessionIdSource; //审批卡片说清浏览器动作、接管切窗口要认会话
    private readonly Func<IReadOnlyList<ChatSession.SessionWorktreeGrant>?>? _worktreeGrantsSource; //卡片据此把 worktree 里的文件页面当界内
    private readonly ThinkTagStreamParser _thinkParser = new();
    private readonly List<ApprovalRequestItem> _pending = new(); //待决审批(可被整体取消)
    private readonly List<ApprovalRequestItem> _round = new(); //本轮新增审批(供运行循环回应)
    private readonly bool _isUiBound; //落点是否为界面绑定集合(回放缓冲是普通 List,不是)
    private TextConversationItem? _streamingText;
    private ThinkingItem? _streamingThinking;
    private int _segmentFirstItem = -1; //本段第一条条目在落点里的下标,-1=本段还没有条目
    private bool _segmentHadThinking; //本段已经有过带正文的思考卡(含已收尾的)
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

    /// <summary>
    /// 一次服务调用的内容到此为止（历史此刻已落盘）。
    ///
    /// 存在的理由只有一个：<b>一轮可以很长</b>。落盘通知（<c>ETurnNotice.Persisted</c>）
    /// 一整轮才来一次，而一轮 agent 回复能跑几十次工具调用、堆出几百个条目——
    /// 只在轮末回填与裁剪，等于这一整轮里条目上限根本不存在。消息边界是轮内唯一
    /// 「历史与界面都自洽」的时刻，回填与裁剪要插手就只能插在这里。
    /// </summary>
    public event Action? MessageBoundaryReached;

    /// <param name="target">条目落点：实时流直写界面集合，回放写入构建缓冲</param>
    /// <param name="createAssistantItem">助手气泡工厂（名字与头像取自当前会话角色）</param>
    /// <param name="createUserItems">
    /// 用户气泡工厂（消息 → 已接好来源的条目；返回空表示这条不画，比如框架注入的空消息；
    /// 成员会话里的群投递会拆成几个发言人的气泡）。
    /// 省略则不画用户消息——回放缓冲不需要，历史里的用户消息由调用方按种类自己画
    /// </param>
    /// <param name="rememberShellPattern">「本会话放行同类命令」的落点</param>
    /// <param name="pathsSource">当前会话工具的路径口径来源（现取现用：会话中途改工作目录也能跟上）</param>
    /// <param name="renderedBefore">
    /// 本次装配<b>之前</b>就已经渲染出去的条目。只在增量装配（外驱会话每次服务调用补渲染一段）时给：
    /// 那时工具调用与它的结果落在<b>不同批</b>里，只认本批的话结果永远配不上调用，
    /// 卡片会一直停在「历史里没有这次调用的结果」，直到整份回放才对上。
    /// </param>
    /// <param name="sessionIdSource">当前会话 id 来源：浏览器调用的审批卡按它的标签页把编号翻成元素</param>
    /// <param name="approveExtension">扩展卡上「本会话允许」的落点（参数是扩展名）</param>
    /// <param name="worktreeGrantsSource">当前会话登记的 worktree 来源：卡片据此把 worktree 里的文件页面当界内</param>
    public ConversationTranscript(
        IList<ConversationItemBase> target,
        Func<TextConversationItem> createAssistantItem,
        Action<string>? rememberShellPattern = null,
        Func<AgentPathResolver?>? pathsSource = null,
        IReadOnlyList<ConversationItemBase>? renderedBefore = null,
        Func<ChatMessage, IReadOnlyList<TextConversationItem>>? createUserItems = null,
        Func<string?>? sessionIdSource = null,
        Action<string>? approveExtension = null,
        Func<IReadOnlyList<ChatSession.SessionWorktreeGrant>?>? worktreeGrantsSource = null)
    {
        _target = target;
        _renderedBefore = renderedBefore;
        _isUiBound = target is INotifyCollectionChanged;
        _createAssistantItem = createAssistantItem;
        _createUserItems = createUserItems ?? (_ => []);
        _rememberShellPattern = rememberShellPattern;
        _pathsSource = pathsSource;
        _sessionIdSource = sessionIdSource;
        _approveExtension = approveExtension;
        _worktreeGrantsSource = worktreeGrantsSource;
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
        if (content is not MessageBoundaryContent && _segmentFirstItem < 0)
        {
            _segmentFirstItem = _target.Count;
            _segmentHadThinking = false;
        }
        switch (content)
        {
            // 这一发整段重来：把它画出来的东西摘掉，新的一发从头画。放在最前面，
            // 因为它在语义上是「撤销本段」而不是一种要装配的内容
            case StreamRestartContent:
                DiscardSegment();
                break;

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

                // CallId 唯一：已有这张卡（观察中途挂上来，历史回放已画过）就不再画第二张，结果照常回写到它上面
                if (!string.IsNullOrEmpty(call.CallId) && FindCall(call.CallId) != null) break;

                // 扩展调用按内层那次画（ADR 0079）：生图缩略图、配音播放、浏览器卡都认原工具名
                FunctionCallContent shown = ExtensionCalls.Unwrap(call);
                ToolCallItem card = new()
                {
                    CallId = shown.CallId,
                    ToolName = shown.Name,
                    IconName = AgentContentFormatter.GetToolIconName(shown.Name),
                    ArgumentSummary = AgentContentFormatter.SummarizeArguments(shown, _pathsSource?.Invoke()?.WorkspaceRoot),
                    FilePath = AgentContentFormatter.GetFilePath(shown),
                    ReadOffset = AgentContentFormatter.GetReadOffset(shown),
                    ArgumentsJson = shown.Arguments == null
                        ? string.Empty
                        : string.Join("\n", shown.Arguments.Select(x => $"{x.Key}: {x.Value}")),
                };
                _target.Add(card);
                // 跑表只在 live 起：回放卡没有“跑的过程”，一起跳只会刷出 0.1s 的假耗时
                if (_isUiBound) card.BeginLiveTiming();
                break;

            case FunctionResultContent result:
                ApplyResultToExisting(result);
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

            case ImageProgressContent image:
                FindCall(image.CallId)?.ApplyImageProgress(image.Progress);
                break;

            // 模型开始思考但一个字都没有：开一张没有正文的卡让它转起来。
            // 两种证据都走这里——流里看见了加密条目，或服务端报了思考 token 却一个字没回
            case EncryptedReasoningContent:
                ThinkingStarted();
                break;

            // 同一张卡，只是这一条还带着「它想了多少」
            case ThinkingNotReturnedContent notReturned:
                ThinkingStarted(notReturned.ReasoningTokens);
                break;

            // 一次服务调用到此为止:之后的正文属于下一条助手消息,不能续进当前气泡
            case MessageBoundaryContent boundary:
                ThinkingBoundary.Dispatch(boundary, _thinkParser, AppendText, AppendThinking, CloseSegment);
                _segmentFirstItem = -1;
                MessageBoundaryReached?.Invoke();
                break;

            case UserMessageContent consumed:
                // 这处的收段是有条件的:同一条已画过就早退,不收——不能走统一分发,
                // 否则重复消费会把正开着的思考段提前切断
                RenderUserMessage(consumed.Message);
                break;

            case ToolApprovalRequestContent approvalRequest:
                ThinkingBoundary.Dispatch(approvalRequest, _thinkParser, AppendText, AppendThinking, CloseSegment);
                ApprovalRequestItem approvalItem = new(approvalRequest, _pathsSource?.Invoke(), _sessionIdSource?.Invoke(),
                    worktreeGrantsSource: _worktreeGrantsSource)
                {
                    RememberShellPatternCallback = pattern => _rememberShellPattern?.Invoke(pattern),
                    ApproveExtensionCallback = extension => _approveExtension?.Invoke(extension),
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
        IReadOnlyList<TextConversationItem> items = _createUserItems(message);
        if (items.Count == 0) return;

        foreach (TextConversationItem item in items) _target.Add(item);
        UserMessageRendered?.Invoke(message);
    }

    private bool HasUserItemFor(ChatMessage message)
    {
        return _target.Concat(_renderedBefore ?? [])
            .Any(x => x is TextConversationItem && ReferenceEquals(x.SourceMessage, message)); //群投递拆出的成员气泡不是 IsUser
    }

    /// <summary>
    /// 重刷每张委派卡的「已派出 / 结果待回」。
    ///
    /// 整体重刷而不是只更新某一张：触发它的是父会话级的信号（名下的后台委派多了或少了一个），
    /// 而一个父会话名下常态就有好几张卡。
    /// </summary>
    public void RefreshSubSessionPending()
    {
        foreach (ToolCallItem call in _target.OfType<ToolCallItem>())
        {
            if (call.SubSessionId is { Length: > 0 } id)
            {
                call.IsAwaitingReport = BackgroundSubAgentDispatcher.IsAwaitingReport(id);
            }
        }
    }

    /// <summary>按 CallId 找回工具卡片：先看本次装配的产出，再看更早已渲染出去的那些</summary>
    private ToolCallItem? FindCall(string? callId)
    {
        return _target.OfType<ToolCallItem>().LastOrDefault(x => x.CallId == callId)
               ?? _renderedBefore?.OfType<ToolCallItem>().LastOrDefault(x => x.CallId == callId);
    }

    /// <summary>
    /// 把<b>本批之后</b>的历史里的工具结果回写到本批已渲染的卡片上。
    ///
    /// 为什么需要它：长会话按窗口分批回放（见 <c>HistoryWindow</c>），而一次调用与它的结果
    /// 是两条消息，开窗完全可能把它们切在两批里。批内看不到那条结果，收尾就会把这张卡
    /// 收口成「历史里没有这次调用的结果」——盘上明明有结果，界面却在谎报，而且滚一次
    /// 「加载更早」就复现一次。实机撞到过：子会话 Write 卡的结果落在下一批里。
    /// </summary>
    /// <param name="messages">完整历史</param>
    /// <param name="from">本批的结束下标（不含），从这里往后找结果</param>
    public void ApplyLaterResults(IReadOnlyList<ChatMessage> messages, int from)
    {
        for (int i = Math.Max(0, from); i < messages.Count; i++)
        {
            foreach (AIContent content in messages[i].Contents)
            {
                if (content is FunctionResultContent result) ApplyResultToExisting(result);
            }
        }
    }

    /// <summary>
    /// 把一条工具结果回写到已渲染的卡片（找不到则静默）。与 <see cref="Apply"/> 里的
    /// <c>FunctionResultContent</c> 分支共用同一份逻辑。
    /// </summary>
    private void ApplyResultToExisting(FunctionResultContent result)
    {
        if (FindCall(result.CallId) is not { } item) return;

        item.IsRunning = false;
        // 取消补写的结果要显示成失败:它没跑完,绿点会是假消息。
        // 判据只能取正文——Exception 带 [JsonIgnore],存盘再读回来就没了
        item.IsSuccess = result.Exception == null && !ToolCallCancellation.IsCancelled(result);
        item.ResultText = result.Result?.ToString() ?? result.Exception?.Message ?? string.Empty;
        // live 才给 provisional 耗时：回放卡建完即收尾，起点是重建时刻，算了也是假数，
        // 等渲染器按来源认领存档（ToolCallItem.FreezeReplayItems）。
        // 时间戳取在结果到达处：后面的缩略图/音频解码是到达之后的 UI 活，不该计进调用耗时
        if (_isUiBound) item.FinishProvisionalDuration();
        // 回放历史时 SubSessionStartedContent 早已随当时那一轮消失,
        // 入口只能从落了盘的结果文本里认回来
        if (!item.HasSubSession) item.SubSessionId = ToolCallItem.ParseSubSessionId(item.ResultText);
        // 生图、配音的产出同理:回放时文件在盘上、路径在结果里
        AgentPathResolver? paths = _pathsSource?.Invoke();
        item.LoadResultImages(paths);
        item.LoadResultAudio(paths);
    }

    /// <summary>
    /// 这一发整段重来（断线重试、参数被拒后的自愈重发）：把属于它的条目全摘掉。
    ///
    /// <para>
    /// 界线只有 <see cref="_segmentFirstItem"/>——段内的条目都属于这一次服务调用。重发的内容是
    /// <b>连着流进来</b>的，不摘的话第二发会续进第一发画出的思考卡/气泡：轻则同一段思考出现两遍，
    /// 重则先收段再重开，多出一张一样的卡。而那一发什么都不会执行（框架要等流走完才执行工具调用），
    /// 所以半截工具卡、点不到的审批卡留在界面上都是错的。
    /// </para>
    /// </summary>
    private void DiscardSegment()
    {
        _thinkParser.Reset();
        _streamingText = null;
        _streamingThinking?.StopTiming(); //不摘掉跑表，泵会一直攥着一张已经不在集合里的卡
        _streamingThinking = null;

        if (_segmentFirstItem >= 0)
        {
            for (int i = _target.Count - 1; i >= _segmentFirstItem; i--)
            {
                ConversationItemBase item = _target[i];
                // 用户那条消息画出来的气泡不属于这一发：中途插话不能被重发带走。
                // 判据是来源消息的角色而非 IsUser——群里的投递会拆成几个成员气泡，它们 IsUser 为 false
                if (item.IsUser || item.SourceMessage?.Role == ChatRole.User) continue;

                switch (item)
                {
                    case ThinkingItem thinking:
                        thinking.StopTiming();
                        break;
                    case ToolCallItem call:
                        call.FinishProvisionalDuration();
                        break;
                    // 卡摘掉了就没人能回应它：先按拒绝收口，免得谁还在等一个点不到的按钮
                    case ApprovalRequestItem approval:
                        approval.CancelAsDeny();
                        _pending.Remove(approval);
                        _round.Remove(approval);
                        break;
                }

                _target.RemoveAt(i);
            }
        }

        _segmentFirstItem = -1;
        _segmentHadThinking = false;
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
        StopRunningToolCalls(_target, note, _isUiBound);
    }

    /// <summary>
    /// 模型开始思考，但正文一个字都不会来（加密思考）。开一张<b>没有正文</b>的卡并让它转起来。
    ///
    /// 两条信号都走这里：HTTP 层在流里看见 <c>reasoning.encrypted</c> 时发的那条（开卡早，但没有 token 数），
    /// 与流尾「报了思考 token 却一个字没回」那条（带 token 数）。落到的是同一张卡。
    /// </summary>
    /// <param name="reasoningTokens">服务端报的思考 token；未知传 0</param>
    public void ThinkingStarted(long reasoningTokens = 0)
    {
        VerifyUiAccess();
        // 本段已经有过带正文的思考卡：再补一张「没有正文」的只是噪音——用户已经看见它想什么了。
        // 这条判断不能只看 _streamingThinking：正文一开始流，那张卡就收尾置空了
        if (_segmentHadThinking)
        {
            // 但 token 数可以补到先开的那张空卡上（它开在流里，那时还不知道这一发想了多少）
            if (reasoningTokens > 0 && _segmentFirstItem >= 0 && _segmentFirstItem < _target.Count &&
                _target[_segmentFirstItem] is ThinkingItem { IsContentUnavailable: true } existing)
            {
                existing.SetReasoningTokens(reasoningTokens);
            }

            return;
        }

        CloseText(); //思考发生在正文之前：正文万一先流了，先收段，卡才排得对

        ThinkingItem card = new()
        {
            IsContentUnavailable = true,
            IsExpanded = true,
            IsDone = false, //还在思考：跑表靠这一位判断该不该续拍
            ReasoningTokens = reasoningTokens,
        };
        // 插回本段开头，不是追加到当前末尾：票是网关在流的<b>任意一帧</b>塞过来的，
        // 按到达位置追加会变成「工具调用之后突然冒出一张思考卡」——思考其实在调用之前
        int at = _segmentFirstItem >= 0 ? _segmentFirstItem : _target.Count;
        _target.Insert(at, card);
        // 没有正文就没有增量来驱动上屏泵：踢一脚，标题上的耗时才开始走（与工具卡同一路）
        if (_isUiBound) card.BeginLiveTiming();
        _streamingThinking = card;
        _segmentHadThinking = true;
    }

    private static void StopRunningToolCalls(IEnumerable<ConversationItemBase> items, string note, bool finishDuration)
    {
        foreach (ToolCallItem call in items.OfType<ToolCallItem>())
        {
            if (!call.IsRunning) continue;

            call.IsRunning = false;
            call.IsSuccess = false;
            if (string.IsNullOrEmpty(call.ResultText)) call.ResultText = note;
            if (finishDuration) call.FinishProvisionalDuration();
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
    /// 取<b>指定的那一批</b>审批请求对应的卡片。它们仍留在待决清单里（回应期间用户可能点停止），
    /// 直到 <see cref="ResolveApprovals"/> 把它们移出。
    ///
    /// ⚠️ <b>必须按请求对象相认，不能把攒着的整批抽干。</b>同一个会话上可能同时有两轮在跑
    /// （用户那一轮与后台子代理回来时起的<b>唤醒轮</b>共用这一个转录器），无脑抽干会把
    /// 别人那一轮的卡片领走——于是那一轮的 <c>resolver</c> 拿到空回应、循环正常退出，
    /// 而它那次工具调用<b>永远没有结果</b>地留在历史里（实机踩到）。
    /// 相认按引用，与子会话窗口认领嵌套审批同一口径。
    /// </summary>
    /// <param name="requests">本轮要回应的请求</param>
    /// <returns>对应的卡片；认不出的请求不会凭空造卡</returns>
    public IReadOnlyList<ApprovalRequestItem> TakeRoundApprovals(
        IReadOnlyList<ToolApprovalRequestContent> requests)
    {
        List<ApprovalRequestItem> mine = _round.Where(x => requests.Contains(x.Request)).ToList();
        _round.RemoveAll(mine.Contains);
        return mine;
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
    /// 这些审批已在别处按这些回应决出（无人值守的自动同意）：卡片就地收掉，从本轮与待决清单里摘掉。
    ///
    /// 卡片上那一行按<b>真正发出去的那条回应</b>显示，不另写一套文案——两边各写各的，
    /// 将来口径一改就会出现「显示已同意、其实发了别的」。
    ///
    /// <b>一律 Post 到 UI 线程</b>，与 <see cref="LiveObserverSink"/> 同一理由：画卡的 Post 排在
    /// 更早的队列里，直接调会赶在卡片诞生之前（唤醒轮里调用方可能在后台线程上）。
    /// </summary>
    /// <param name="requests">已决出的审批请求</param>
    /// <param name="responses">与 <paramref name="requests"/> 一一对应的回应；缺了回应的那几条卡片原样留着</param>
    /// <param name="label">收起后那一行的文案；null 按回应本身判（同意/拒绝/本会话允许）</param>
    public void ResolveApprovalsElsewhere(IReadOnlyList<ToolApprovalRequestContent> requests,
        IReadOnlyList<ChatMessage> responses, LangKey? label = null)
    {
        // 只认有回应的那几条：没回应的卡片摘掉了就再也没人能回应它，留在本轮里还能交给用户点
        IReadOnlyList<ToolApprovalRequestContent> answered =
            responses.Count >= requests.Count ? requests : requests.Take(responses.Count).ToList();
        if (answered.Count == 0) return;

        Dispatcher.UIThread.Post(() =>
        {
            IReadOnlyList<ApprovalRequestItem> taken = TakeRoundApprovals(answered);
            foreach (ApprovalRequestItem approval in taken)
            {
                // 认卡按 _round 的顺序,回应按 requests 的顺序,两者未必一致,按请求对象对回去
                approval.MarkDecidedElsewhere(responses[IndexOf(answered, approval.Request)], label);
            }

            ResolveApprovals(taken);
        });
    }

    private static int IndexOf(IReadOnlyList<ToolApprovalRequestContent> requests, ToolApprovalRequestContent request)
    {
        for (int i = 0; i < requests.Count; i++)
        {
            if (ReferenceEquals(requests[i], request)) return i;
        }

        return -1;
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
        _streamingThinking?.StopTiming(); //切会话时卡片跟着走人，跑表得一起停
        _streamingThinking = null;
        _segmentFirstItem = -1;
        _segmentHadThinking = false;
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
        // 开卡前先把段首空白剥掉：纯空白不建卡（实测 deepseek 会发一个孤立空格，
        // 照建就是一张「1 字 · 1 字/秒」的假思考卡）。已有的卡照原样追加，句中的空格是真内容。
        // 这条口径与存档侧同一处（ThinkingSegments）：两边差那几个空格，
        // 回放认领时这张卡就会被当成「认错了消息」而丢掉耗时
        if (_streamingThinking == null || _streamingThinking.IsContentUnavailable)
        {
            delta = delta.TrimStart();
            if (delta.Length == 0) return;
        }

        _segmentHadThinking = true;
        // 先开的是「没有正文」那张（加密思考的信号），正文真来了就把它换成正常的卡
        if (_streamingThinking is { IsContentUnavailable: true } placeholder)
        {
            placeholder.StopTiming(); //它自己不会收尾，跑表得先摘掉，否则泵一直攥着它
            _target.Remove(placeholder);
            _streamingThinking = null;
        }

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
        // 没有正文的卡（加密思考）永远不会被 Append，Flush 也就不会替它收尾
        if (thinking.IsContentUnavailable) thinking.IsDone = true;
        if (AutoCollapseThinking) thinking.IsExpanded = false;
        _streamingThinking = null;
    }
}
