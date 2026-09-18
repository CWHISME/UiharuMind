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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.Scheduler;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils.Tools;
using UiharuMind.Shared.Windows;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 思考过程条目(默认折叠,弱化展示)
/// </summary>
public partial class ThinkingItem : ConversationItemBase, IStreamFlushTarget
{
    private readonly StringBuilder _buffer = new();
    private readonly object _bufferGate = new(); //追加来自推理线程,冲刷在 UI 线程,两边都要过锁
    private readonly DateTime _startedAt = DateTime.Now; //本段思考开始时刻,收尾时用来算耗时

    /// <summary>
    /// 流式期间上屏的预览长度上限。展开的思考段每 150ms 把累积全文重设给 TextBlock,
    /// 全量文本重排成本随长度二次增长——思考一长(实测单段能到几十万字符)就把 UI 线程拖死,
    /// 表现为"思考过长直接断开且无报错"。预览截断把每次上屏的文本量钉在常数,段落收尾才渲染全文。
    /// </summary>
    private const int StreamingPreviewChars = 512;

    /// <summary>
    /// UI 侧上屏间隔。思考流是全场最高频、最低价值的一路,而每次把累积全文重设给
    /// TextBlock 都要一次全量文本重排,成本随长度二次增长——本地模型下子代理思考几千 token
    /// 就足以把界面拖住。~7Hz 仍然"看得见它在想",重排次数却降两个数量级。
    /// 节拍由 <see cref="StreamFlushPump"/> 统一给,尾巴由 <see cref="Flush"/> 保证不丢。
    /// </summary>
    public int FlushIntervalMs => 50;

    [ObservableProperty] private bool _isExpanded;

    /// <summary>标题栏统计,如「2.4s · 1,234 字 · 514 字/秒」;收尾(Flush)前为空</summary>
    [ObservableProperty] private string _statsText = string.Empty;

    /// <summary>流式预览是否已截断(截断时卡片上出现「查看全文」按钮)</summary>
    [ObservableProperty] private bool _isPreviewTruncated;

    /// <summary>
    /// 全文增量订阅者及其「已消费到的缓冲长度」（游标）。每人各记各的——
    /// 多个全文窗先后打开互不干扰：后开的窗口基线是打开那一刻，
    /// 先开的窗口照常收自己缺的那段，不会因为新订阅者出现而丢数据。
    /// 读写都只在 UI 线程（订阅发生在窗口打开，推送发生在节拍）。
    /// </summary>
    private readonly Dictionary<Action<string>, int> _contentSubscribers = new();

    /// <summary>
    /// 订阅全文增量并返回<b>订阅时刻的全量快照</b>。
    /// 读快照与设游标在锁内一次性完成——若增量落在快照之前，它已被包含在全量里；
    /// 落在订阅之后，会通过回调补上。窗口只需把返回值喂给全文控件，再等回调 append。
    /// </summary>
    /// <param name="handler">增量接收者（UI 线程调用）</param>
    /// <returns>当前全量文本</returns>
    public string SubscribeContent(Action<string> handler)
    {
        lock (_bufferGate)
        {
            _contentSubscribers[handler] = _buffer.Length;
            return _buffer.ToString();
        }
    }

    /// <summary>退订全文增量</summary>
    /// <param name="handler">与订阅时相同的委托</param>
    public void UnsubscribeContent(Action<string> handler)
    {
        _contentSubscribers.Remove(handler);
    }

    /// <summary>取缓冲全文快照(任意线程可调,取的是当下值)</summary>
    public string CurrentText
    {
        get
        {
            lock (_bufferGate) return _buffer.ToString();
        }
    }

    /// <summary>
    /// 追加一段流式增量
    /// </summary>
    /// <param name="delta">增量文本</param>
    public void Append(string delta)
    {
        lock (_bufferGate) _buffer.Append(delta);
        StreamFlushPump.Request(this);
    }

    /// <summary>
    /// 立即把缓冲同步到 <see cref="ConversationItemBase.Message"/>(段落收尾时调用)。
    /// 收尾语义:赋<b>全文</b>,此时只重排一次,可接受
    /// </summary>
    public void Flush()
    {
        if (_isStatsFrozen) return; //回放冻结:存档值已定,收尾重算只会把它抹成 0.1s
        string text;
        int fullLen;
        // 取快照再赋值:赋值会引发绑定与布局,不该攥着锁做
        lock (_bufferGate)
        {
            string full = _buffer.ToString();
            fullLen = full.Length;
            // 思考完毕后的正常展开也走同一套截断:超限时上截断预览而非全文,
            // 避免几十万字内联进卡片把 UI 拖死。全文阅读走「查看全文」窗(ShowFullText)
            text = IsTruncated(fullLen) ? BuildTruncatedPreview(fullLen) : full;
        }

        Message = text;
        NotifyContentChanged(); //收尾补上最后一段增量;没有订阅者时零成本
        UpdateStats(fullLen); //统计用全文长度,不随截断预览缩水
        // 收尾快照:写回历史用此刻的值——条目驻留内存期间 Now 只会越涨越假
        _closedElapsed = DateTime.Now - _startedAt;
        _closedChars = fullLen;
        _isClosed = true;
        IsPreviewTruncated = IsTruncated(fullLen); //超限的长思考收尾后仍保留「查看全文」入口
    }

    /// <inheritdoc />
    // 流式上屏:只赋<b>头部</b>截断预览,把重排成本钉在常数。全文等收尾(Flush)再上。
    // 为什么锚在头部而不是尾部:尾窗每次滑动,卡片高度随行数逐拍跳——就是"高低起伏"闪烁的根源。
    // 头部起点钉在 0,内容逐拍不变,高度稳定;想看最新与全量去全文窗(ShowFullText),按增量追着流走。
    void IStreamFlushTarget.FlushForDisplay()
    {
        if (_isStatsFrozen) return; //回放冻结:泵的延迟冲刷不能盖掉存档值
        string text;
        int len;
        lock (_bufferGate)
        {
            len = _buffer.Length;
            text = IsTruncated(len) ? BuildTruncatedPreview(len) : _buffer.ToString();
        }

        Message = text;
        NotifyContentChanged();
        IsPreviewTruncated = IsTruncated(len);
        // 统计节流:超过 1s 后标题数字 1s 一跳,50ms 跟着刷只会让它乱跳。
        // 正文预览不受影响——它是头部锚定的,本来就不跳。
        DateTime now = DateTime.Now;
        if (now - _startedAt >= TimeSpan.FromSeconds(1) && now - _lastStatsAt < TimeSpan.FromSeconds(1)) return;
        // 与 Flush 同样地锁外赋值:属性变更会引发绑定与布局,不该攥着锁做
        UpdateStats(len);
    }

    /// <summary>
    /// 是否需要对上屏文本做截断。流式(FlushForDisplay)与收尾(Flush)共用同一个判断——
    /// 只要超过预览上限就算截断，卡片据此显示「查看全文」入口。
    /// </summary>
    /// <param name="len">缓冲长度</param>
    /// <returns>超限返回 true</returns>
    private static bool IsTruncated(int len) => len > StreamingPreviewChars;

    /// <summary>
    /// 构建截断预览：头部锚定 + 本地化截断提示。
    /// 头部起点钉在 0,内容逐拍不变,高度稳定;想看最新与全量去全文窗(ShowFullText)。
    /// 必须在持有 <see cref="_bufferGate"/> 时调用(内部要读 buffer)。
    /// 文案统一为「已截断」,流式中与收尾后都成立,不引入时态。
    /// </summary>
    /// <param name="len">缓冲长度(已确认 &gt; <see cref="StreamingPreviewChars"/>)</param>
    /// <returns>上屏的截断文本</returns>
    private string BuildTruncatedPreview(int len)
    {
        return _buffer.ToString(0, StreamingPreviewChars) +
               "\n" + string.Format(Loc.Text(LangKey.AgentThinkingTruncatedFormat), len.ToString("N0"));
    }

    /// <summary>
    /// 把自各订阅者游标以来的增量推给订阅者并推进游标（只在 UI 线程调用）。
    /// 没有任何订阅者时直接返回，不掏 buffer。
    /// </summary>
    private void NotifyContentChanged()
    {
        List<(Action<string> Handler, string Delta, int NewCursor)>? deliveries = null;
        lock (_bufferGate)
        {
            if (_contentSubscribers.Count == 0) return;
            int len = _buffer.Length;
            foreach (KeyValuePair<Action<string>, int> subscriber in _contentSubscribers)
            {
                int start = subscriber.Value;
                if (start >= len) continue;
                deliveries ??= [];
                deliveries.Add((subscriber.Key, _buffer.ToString(start, len - start), len));
            }
        }

        if (deliveries == null) return;
        foreach ((Action<string> handler, string delta, int cursor) in deliveries)
        {
            try
            {
                handler(delta);
            }
            finally
            {
                // 回调执行期间可能已退订(如用户点关闭,OnPreClose 里退订)——
                // 此时不该把游标写回去,否则退订被抵消,关了的窗口继续收增量
                if (_contentSubscribers.ContainsKey(handler))
                {
                    _contentSubscribers[handler] = cursor;
                }
            }
        }
    }

    /// <summary>刷新标题栏的耗时、字符数与速度(流式期间受节拍节流,收尾为准)</summary>
    /// <param name="charCount">缓冲里的字符数</param>
    private void UpdateStats(int charCount)
    {
        StatsText = FormatStats(DateTime.Now - _startedAt, charCount);
        _lastStatsAt = DateTime.Now;
    }

    /// <summary>
    /// 耗时显示格式:1 分钟内给秒(带一位小数),1 小时内给分+秒,更久给时+分。
    /// 思考段短则几秒、长则几十分钟,统一给到秒级就够了。
    /// </summary>
    /// <param name="span">耗时</param>
    /// <returns>显示文本</returns>
    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m {span.Seconds}s";
        return $"{span.TotalSeconds:0.#}s";
    }

    /// <summary>打开全文窗(卡片上的「查看全文」按钮)</summary>
    [RelayCommand]
    private void ShowFullText()
    {
        ThinkingDetailWindow.Show(this);
    }
}

/// <summary>
/// 工具调用卡片:按工具类别渲染(shell / 文件 / 技能 / MCP / 通用)
/// </summary>
public partial class ToolCallItem : ConversationItemBase
{
    private ToolResultView _resultView = ToolResultTruncation.Empty; //结果纯文本的截断视图
    private ToolResultView _argumentsView = ToolResultTruncation.Empty; //参数原文的截断视图(同一套阈值)

    public string CallId { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;

    /// <summary>卡片图标(按工具类别)</summary>
    public string IconGlyph { get; init; } = "🔧";

    /// <summary>
    /// 本次委派建出来的<b>子会话</b>标识；空串即普通工具调用，卡片上不出现入口。
    ///
    /// 两条来路，都指向同一个 id：跑着的时候由 <c>SubSessionStartedContent</c> 现场交过来，
    /// 回放历史时从工具结果末尾的 <c>[sub-session: …]</c> 认出来。
    /// 过程本身不在这里——它是子会话自己的历史，点开即读（见 ADR 0021）。
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSubSession))]
    private string _subSessionId = string.Empty;

    /// <summary>这张卡片是不是一次子代理委派</summary>
    public bool HasSubSession => SubSessionId.Length > 0;

    /// <summary>工具结果里那行子会话标识（回放时据此恢复入口）</summary>
    private static readonly Regex _subSessionMarker = new(@"\[sub-session:\s*([A-Za-z0-9]+)\]",
        RegexOptions.Compiled);

    /// <summary>
    /// 从工具结果里认出子会话标识。回放历史时走这条——那时 <c>SubSessionStartedContent</c>
    /// 早已随当时那一轮消失，而结果文本是落了盘的
    /// </summary>
    /// <param name="resultText">工具结果正文</param>
    /// <returns>子会话标识；没有则为空串</returns>
    public static string ParseSubSessionId(string? resultText)
    {
        if (string.IsNullOrEmpty(resultText)) return string.Empty;
        Match match = _subSessionMarker.Match(resultText);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    [ObservableProperty] private string _argumentSummary = string.Empty;
    [ObservableProperty] private string _argumentsJson = string.Empty;

    /// <summary>本次调用涉及的文件路径（没有则为空）。只用来给全文窗按扩展名挑语法高亮</summary>
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private bool _isRunning = true;
    [ObservableProperty] private bool _isSuccess = true;

    /// <summary>
    /// <b>已派出 / 结果待回</b>：这次委派转入后台跑着，工具调用本身早就返回了。
    ///
    /// 光看调用有没有结果不够——委派默认后台执行之后，卡片在派出后一秒内就拿到结果，
    /// 于是显示成「成功」，而子代理还要跑好几分钟（实测中位数 3 分钟）。
    /// 这一档 ADR 0021 就预付过（「不要设计成三态」），ADR 0025 把它用起来。
    /// </summary>
    [ObservableProperty] private bool _isAwaitingReport;
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private bool _isExpanded;

    /// <summary>
    /// 结果面板真正渲染的正文。<see cref="ResultText"/> 是原文，绑到控件上的<b>只有</b>这一小段。
    ///
    /// 曾经有过一个「就地展开全文」的开关，它是错的：会话流没有虚拟化，
    /// 把几十万字换进 <c>SelectableTextBlock</c> 等于当场冻住界面。全文现在一律去
    /// <see cref="FullTextWindow"/>，那边的内核按行虚拟化。
    /// </summary>
    public string ResultDisplayText => _resultView.DisplayText;

    /// <summary>结果原文是否超过阈值（决定截断提示行的显隐；↗ 入口与它无关，常驻）</summary>
    public bool IsResultTruncated => _resultView.IsTruncated;

    /// <summary>结果的截断提示行文案</summary>
    public string ResultTruncationHint => ToolResultTruncation.FormatTruncationHint(_resultView);

    /// <summary>参数面板真正渲染的正文。参数原文此前<b>零截断</b>——一次 Write 带几百 KB 就直接进排版</summary>
    public string ArgumentsDisplayText => _argumentsView.DisplayText;

    /// <summary>参数原文是否超过阈值</summary>
    public bool IsArgumentsTruncated => _argumentsView.IsTruncated;

    /// <summary>参数的截断提示行文案（与结果共用一套文案，口径一致）</summary>
    public string ArgumentsTruncationHint => ToolResultTruncation.FormatTruncationHint(_argumentsView);

    /// <summary>
    /// 结果正文里的 diff 行（目前只有 <c>Edit</c> 会有）。空集合表示按纯文本渲染结果。
    /// 编辑成功后的 diff 在审批卡片上有增删配色，在这张卡片上曾经是一片灰字——
    /// 同一份 diff 两种观感，而这张卡片才是自动放行档位下唯一看得到改动的地方。
    /// </summary>
    public IReadOnlyList<DiffLineView> ResultDiffLines { get; private set; } = [];

    /// <summary>是否有 diff 可展示（有则替换掉纯文本结果面板）</summary>
    public bool HasResultDiff => ResultDiffLines.Count > 0;

    partial void OnResultTextChanged(string value)
    {
        ResultDiffLines = DiffLineView.ParseToolResult(value);
        // diff 那一支不用截断:Core 侧 PermissiveFileAccessTools.MaxEditDiffLines 已把它封死在 80 行,
        // 到不了阈值。截断只管纯文本那支
        _resultView = HasResultDiff ? ToolResultTruncation.Empty : ToolResultTruncation.Build(value);
        OnPropertyChanged(nameof(ResultDiffLines));
        OnPropertyChanged(nameof(HasResultDiff));
        OnPropertyChanged(nameof(ResultDisplayText));
        OnPropertyChanged(nameof(IsResultTruncated));
        OnPropertyChanged(nameof(ResultTruncationHint));
    }

    partial void OnArgumentsJsonChanged(string value)
    {
        _argumentsView = ToolResultTruncation.Build(value);
        OnPropertyChanged(nameof(ArgumentsDisplayText));
        OnPropertyChanged(nameof(IsArgumentsTruncated));
        OnPropertyChanged(nameof(ArgumentsTruncationHint));
    }

    /// <summary>把结果原文交给全文窗（非模态、可多开，与卡片各看各的）</summary>
    [RelayCommand]
    private void ShowFullResult()
    {
        // Read 之类工具的结果就是某个文件的正文,按它的扩展名高亮;其余工具拿不到路径,纯文本
        FullTextWindow.Show($"{ToolName} · {Loc.Text(LangKey.ToolFullTextResult)}", ResultText, FilePath);
    }

    /// <summary>把参数原文交给全文窗</summary>
    [RelayCommand]
    private void ShowFullArguments()
    {
        // 与结果同一个口径:语言只由"这次调用涉及哪个文件"决定,不按工具名也不按参数名分支——
        // Write 的正文在参数里(content),Read 的正文在结果里,新工具放哪都不用改这里。
        // 开头那几行"filePath: …"在任何语法下都只是普通标识符,不上色但也不会乱
        FullTextWindow.Show($"{ToolName} · {Loc.Text(LangKey.ToolFullTextArguments)}", ArgumentsJson, FilePath);
    }

    /// <summary>
    /// 打开这次委派的子会话。与右栏「子代理」面板点进去<b>是同一个动作</b>——
    /// 一个窗口、一个视图、一份 ViewModel，跑着还是跑完了由它自己判（外驱模式）
    /// </summary>
    [RelayCommand]
    private void ShowActivity()
    {
        if (SubSessionId.Length == 0) return;
        SubSessionWindowOpener.Open(SubSessionId);
    }
}

/// <summary>
/// 审批卡片:三态回应,决定以 ChatMessage 形式回传给运行循环
/// </summary>
public partial class ApprovalRequestItem : ConversationItemBase
{
    private readonly ToolApprovalRequestContent _request;
    private readonly TaskCompletionSource<ChatMessage> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>框架发来的审批请求（子窗口认领嵌套审批时按引用相认用）</summary>
    public ToolApprovalRequestContent Request => _request;

    /// <summary>待审批工具名</summary>
    public string ToolName { get; }

    /// <summary>参数摘要(审批展示)</summary>
    public string ArgumentSummary { get; }

    /// <summary>shell 审批时按命令派生的"同类命令"放行模式;非 shell 或取不到命令为空串</summary>
    public string SuggestedCommandPattern { get; }

    /// <summary>编辑类工具(Write/Edit)的变更 diff;其余工具为空</summary>
    public IReadOnlyList<DiffLineView> DiffLines { get; } = [];

    /// <summary>是否有 diff 可展示(编码场景的审批体验就是 diff 体验)</summary>
    public bool HasDiff => DiffLines.Count > 0;

    /// <summary>是否提供"记住同类命令"选项(工具级的"本会话总是允许"对 shell 过粗)</summary>
    public bool CanRememberCommandPattern => SuggestedCommandPattern.Length > 0;

    /// <summary>用户点"记住同类命令"时回调(由会话侧写入并持久化)</summary>
    public Action<string>? RememberShellPatternCallback { get; set; }

    [ObservableProperty] private bool _isResolved;
    [ObservableProperty] private string _resolvedText = string.Empty;

    /// <summary>用户决定完成后的回应消息</summary>
    public Task<ChatMessage> Response => _completion.Task;

    /// <param name="request">框架发来的审批请求</param>
    /// <param name="workspaceRoot">工作目录,用于把模型给的相对路径解析成真实文件以预演 diff;可空</param>
    public ApprovalRequestItem(ToolApprovalRequestContent request, string? workspaceRoot = null)
    {
        _request = request;
        if (request.ToolCall is FunctionCallContent call)
        {
            ToolName = call.Name;
            ArgumentSummary = AgentContentFormatter.SummarizeArguments(call, workspaceRoot);
            SuggestedCommandPattern = call.Name == CharacterRunnerFactory.ShellToolName
                ? ApprovalModeMapper.DeriveCommandPattern(
                    ApprovalModeMapper.ExtractCommand(call.Arguments) ?? string.Empty)
                : string.Empty;
            DiffLines = DiffLineView.BuildForToolCall(call, workspaceRoot);
        }
        else
        {
            ToolName = request.ToolCall?.ToString() ?? "unknown";
            ArgumentSummary = string.Empty;
            SuggestedCommandPattern = string.Empty;
        }
    }

    /// <summary>
    /// 审批动作:once / session / session-command / deny
    /// </summary>
    /// <param name="decision">决定代号</param>
    [RelayCommand]
    private void Resolve(string decision)
    {
        if (IsResolved) return;

        // "记住同类命令":先入会话放行清单(后续同类命令由审批规则直接放行),本次按普通允许回应
        if (decision == "session-command" && CanRememberCommandPattern)
        {
            RememberShellPatternCallback?.Invoke(SuggestedCommandPattern);
        }

        AIContent response = decision switch
        {
            "session" => ToolApprovalResponseFactory.Create(_request, EApprovalDecision.AlwaysInSession,
                "User chose to always approve this tool"),
            "deny" => ToolApprovalResponseFactory.Create(_request, EApprovalDecision.Deny, "User denied"),
            _ => ToolApprovalResponseFactory.Create(_request, EApprovalDecision.Once, "User approved"),
        };
        ResolvedText = decision;
        IsResolved = true;
        _completion.TrySetResult(new ChatMessage(ChatRole.User, new[] { response }));
    }

    /// <summary>
    /// 外部取消(停止运行时):按拒绝处理
    /// </summary>
    public void CancelAsDeny()
    {
        if (IsResolved) return;
        IsResolved = true;
        ResolvedText = "deny";
        _completion.TrySetResult(new ChatMessage(ChatRole.User,
            new[] { ToolApprovalResponseFactory.Create(_request, EApprovalDecision.Deny, "Run stopped") }));
    }
}

/// <summary>
/// 错误条目
/// </summary>
public partial class ErrorItem : ConversationItemBase
{
}

/// <summary>
/// 交接文档条目。压缩是会丢信息的操作，悄悄发生比丢信息本身更糟——
/// 显示出来，既能让人知道「模型从这里往前看不见了」，也能当场发现文档写砸了。
/// 默认折叠：它通常很长，展开是查证用的，不该挤占正常对话。
/// </summary>
public partial class HandoffItem : ConversationItemBase
{
    /// <summary>是否展开</summary>
    [ObservableProperty] private bool _isExpanded;
}

/// <summary>
/// 编辑审批卡片的一条 diff 行。编码场景的审批体验就是 diff 体验——
/// 裸拼的 old/new 参数文本看不出改了什么,等于逼人盲批。
/// </summary>
public sealed class DiffLineView
{
    private const int MaxDisplayLines = 300; //展示上限,超出折叠为提示行

    /// <summary>行前缀(+/-/空格)</summary>
    public string Prefix { get; private init; } = " ";

    /// <summary>行内容</summary>
    public string Text { get; private init; } = string.Empty;

    /// <summary>是否新增行</summary>
    public bool IsAdded { get; private init; }

    /// <summary>是否删除行</summary>
    public bool IsRemoved { get; private init; }

    /// <summary>预演读文件的大小上限:卡片在 UI 线程上构造,不能为一个巨大文件卡住界面</summary>
    private const long MaxPreviewFileBytes = 1024 * 1024;

    /// <summary>渲染过的 diff 行:前缀(+/-/空格) + 右对齐行号 + 空格 + 正文</summary>
    private static readonly Regex DiffLinePattern = new(@"^([ +-])(\s*\d+) (.*)$", RegexOptions.Compiled);

    /// <summary>
    /// 从编辑类工具调用构建 diff 行;非编辑类工具返回空
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <param name="workspaceRoot">工作目录,用于把模型给的相对路径解析成真实文件;可空</param>
    /// <returns>diff 行列表</returns>
    public static IReadOnlyList<DiffLineView> BuildForToolCall(FunctionCallContent call, string? workspaceRoot = null)
    {
        try
        {
            List<DiffLineView> lines = call.Name switch
            {
                FileToolNames.Write => BuildWriteDiff(call.Arguments),
                FileToolNames.Edit => BuildEditDiff(call.Arguments, workspaceRoot),
                _ => [],
            };
            return Cap(lines);
        }
        catch
        {
            // diff 只是展示增强,构建失败回退为原始参数摘要。
            // 预演判定"这次编辑必然失败"时也走这里:那种失败在自动编辑档下根本弹不出卡片,
            // 为它单独做一套红字告警,收益抵不上多养一条 UI 分支(见 ADR 0007 的决策记录)
            return [];
        }
    }

    /// <summary>
    /// 从工具结果正文里认出 diff 段并染色；不是编辑结果则返回空（调用方按纯文本渲染）。
    ///
    /// 格式（前缀 + 右对齐行号 + 空格 + 正文）是我们自己在
    /// <see cref="FileEditPlanner.RenderDiff"/> 里定的，认回来是<b>同一份代码两端</b>的事，
    /// 不是解析外部格式。往返测试钉住这层耦合，改了渲染格式不会让这里静默失效。
    ///
    /// 为什么不干跑重算：这张卡片出现在<b>执行之后</b>，文件已经改了，
    /// 拿 oldString 再去匹配必然落空。
    /// </summary>
    /// <param name="resultText">工具结果正文</param>
    /// <returns>diff 行列表；非编辑结果为空</returns>
    public static IReadOnlyList<DiffLineView> ParseToolResult(string? resultText)
    {
        if (string.IsNullOrEmpty(resultText) || !resultText.StartsWith("Applied ", StringComparison.Ordinal))
            return [];

        List<DiffLineView> lines = [];
        bool sawDiffLine = false;
        foreach (string raw in resultText.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            Match match = DiffLinePattern.Match(line);
            if (!match.Success)
            {
                lines.Add(Context(line)); //"Applied N edit(s) to ..." 与折叠提示原样留着
                continue;
            }

            sawDiffLine = true;
            string text = $"{match.Groups[2].Value} {match.Groups[3].Value}";
            lines.Add(match.Groups[1].Value switch
            {
                "+" => Added(text),
                "-" => Removed(text),
                _ => Context(text),
            });
        }

        return sawDiffLine ? Cap(lines) : [];
    }

    private static List<DiffLineView> BuildWriteDiff(IDictionary<string, object?>? args)
    {
        string? content = GetString(args, "content");
        if (content == null) return [];

        List<DiffLineView> lines = WithHeader(args);
        foreach (string line in content.Replace("\r\n", "\n").Split('\n'))
        {
            lines.Add(Added(line));
        }

        return lines;
    }

    /// <summary>
    /// Edit 的 diff 由 <see cref="FileEditPlanner"/> <b>干跑</b>出来——读真实文件、跑真实匹配,
    /// 因此卡片上看到的就是落盘后的样子,而不是把 oldString/newString 两块裸文本对着摆。
    /// 语义只有一处定义:工具执行与这张卡片调的是同一个纯函数。
    /// </summary>
    private static List<DiffLineView> BuildEditDiff(IDictionary<string, object?>? args, string? workspaceRoot)
    {
        string? filePath = GetString(args, "filePath");
        if (string.IsNullOrEmpty(filePath)) return [];
        if (args?.TryGetValue("edits", out object? value) != true) return [];

        // 与执行端同一份宽容解析(见 ToolJson.LenientFileEditListConverter):
        // 模型把 edits 发成 JSON 字符串 / 单对象 / underscore 时,执行能成功,预览也必须认得,
        // 否则出现"卡片空白、落盘成功"的盲盒——违背"预览=执行"不变量。
        List<FileEdit>? edits = value switch
        {
            JsonElement element => JsonSerializer.Deserialize<List<FileEdit>>(element.GetRawText(), ToolJson.Lenient),
            string raw => JsonSerializer.Deserialize<List<FileEdit>>(raw, ToolJson.Lenient),
            _ => null,
        };
        if (edits is not { Count: > 0 }) return [];

        string full = Path.IsPathRooted(filePath)
            ? filePath
            : string.IsNullOrEmpty(workspaceRoot)
                ? filePath //没有工作目录可拼,相对路径无从解析,回退成参数摘要
                : Path.Combine(workspaceRoot, filePath);

        if (!File.Exists(full) || new FileInfo(full).Length > MaxPreviewFileBytes) return [];

        FileEditPlan plan = FileEditPlanner.PlanFile(full, filePath, edits);
        if (!plan.Succeeded) return [];

        List<DiffLineView> lines = WithHeader(args);
        foreach (LineDiffEntry entry in plan.Diff)
        {
            lines.Add(entry.Kind switch
            {
                ELineDiffKind.Added => Added($"{entry.LineNumber,5} {entry.Text}"),
                ELineDiffKind.Removed => Removed($"{entry.LineNumber,5} {entry.Text}"),
                ELineDiffKind.Hunk => Context(entry.Text), //块头自带 @@ 标记，不套行号列
                _ => Context($"{entry.LineNumber,5} {entry.Text}"),
            });
        }

        return lines;
    }

    private static List<DiffLineView> WithHeader(IDictionary<string, object?>? args)
    {
        string? path = GetString(args, "filePath");
        return string.IsNullOrEmpty(path) ? [] : [Context($"@ {path}")];
    }

    private static IReadOnlyList<DiffLineView> Cap(List<DiffLineView> lines)
    {
        if (lines.Count <= MaxDisplayLines) return lines;
        int omitted = lines.Count - MaxDisplayLines;
        lines.RemoveRange(MaxDisplayLines, omitted);
        lines.Add(Context($"…(+{omitted} more lines)"));
        return lines;
    }

    private static DiffLineView Added(string text) => new() { Prefix = "+", Text = text, IsAdded = true };
    private static DiffLineView Removed(string text) => new() { Prefix = "-", Text = text, IsRemoved = true };
    private static DiffLineView Context(string text) => new() { Prefix = " ", Text = text };

    private static string? GetString(IDictionary<string, object?>? args, string name)
    {
        if (args == null || !args.TryGetValue(name, out object? value)) return null;
        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value?.ToString(),
        };
    }

    private static string? GetJsonString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
    }
}

/// <summary>
/// AIContent 展示辅助:工具卡片的图标与参数摘要
/// </summary>
public static class AgentContentFormatter
{
    /// <summary>框架内务工具(todo/mode),不进事件流卡片</summary>
    public static bool IsHousekeepingTool(string toolName)
    {
        return toolName.StartsWith("todos_", System.StringComparison.Ordinal) ||
               toolName is "mode_set" or "mode_get";
    }

    /// <summary>
    /// 工具图标
    /// </summary>
    /// <param name="toolName">工具名</param>
    /// <returns>图标字符</returns>
    public static string GetToolIcon(string toolName)
    {
        if (toolName == "run_shell") return "❯";
        // 曾经认的是 file_access_ 前缀(MFA 自带文件工具的命名),而我们那批工具早就自建改名了,
        // 症状是文件工具的卡片一律显示通用扳手。名字改由 FileToolNames 提供,不再各写字面量
        if (FileToolNames.All.Contains(toolName)) return "📄";
        if (toolName is "load_skill" or "read_skill_resource" or "run_skill_script") return "✨";
        if (toolName == VisionTool.ToolName) return "👁";
        if (toolName == SchedulerTools.ToolName) return "⏰";
        if (toolName == SubAgentTool.ToolExplorerName||toolName == SubAgentTool.ToolGeneralName) return "🤖";
        return "🔧";
    }

    
    private const int MaxSummaryValueChars = 60;                        //折叠标题栏是一行,摘要里任何一段都不该超过这个长度
    private const int HeadChars = 26;                                   // 头
    private const int TailChars = MaxSummaryValueChars - 1 - HeadChars; // 尾，减去 "…"
    
    /// <summary>
    /// 摘要优先认的参数键。<c>filePath</c> 曾经不在里面,而文件工具的路径参数正是它——
    /// 于是 Read/Write/Edit 全都落到兜底分支,把参数原样摊开当摘要。
    /// Edit 那种结构化参数摊出来就是一行转义过的 <c>[{"oldString": "…\n…"}]</c>,毫无可读性。
    /// </summary>
    private static readonly string[] PrimaryArgumentKeys =
        ["command", "filePath", "path", "pattern", "query", "skillName", "displayName", "imagePath", "task"];

    /// <summary>按路径口径收窄的那几个键(其余按普通文本收窄)</summary>
    private static readonly string[] PathArgumentKeys = ["filePath", "path", "imagePath"];

    /// <summary>
    /// 提取参数摘要(命令原文 / 文件路径 / 首个字符串参数)。
    /// 结构化参数只给条数不给内容——内容在展开后的 diff 里看得清楚得多。
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <param name="workspaceRoot">工作目录:路径参数在它之下时显示成相对路径,可空</param>
    /// <returns>摘要文本</returns>
    public static string SummarizeArguments(FunctionCallContent call, string? workspaceRoot = null)
    {
        if (call.Arguments == null || call.Arguments.Count == 0) return string.Empty;

        foreach (string key in PrimaryArgumentKeys)
        {
            if (!call.Arguments.TryGetValue(key, out object? value) || value == null) continue;

            string text = PathArgumentKeys.Contains(key)
                ? ShortenPath(value.ToString(), workspaceRoot)
                : Shorten(value.ToString());
            if (text.Length == 0) continue;

            int edits = CountArrayItems(call.Arguments, "edits");
            return edits > 0 ? $"{text}  ({edits} edit{(edits == 1 ? string.Empty : "s")})" : text;
        }

        return string.Join(", ", call.Arguments.Take(2).Select(x => $"{x.Key}: {Shorten(x.Value?.ToString())}"));
    }

    /// <summary>
    /// 取工具调用涉及的文件路径，供全文窗按扩展名选语法高亮。
    /// <b>不猜内容</b>——只认参数里明写的路径键，没有就返回空
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <returns>文件路径；该工具不涉及文件时为空串</returns>
    public static string GetFilePath(FunctionCallContent call)
    {
        if (call.Arguments == null) return string.Empty;

        foreach (string key in PathArgumentKeys)
        {
            if (call.Arguments.TryGetValue(key, out object? value) && value != null)
            {
                string path = value.ToString() ?? string.Empty;
                if (path.Length > 0) return path;
            }
        }

        return string.Empty;
    }

    /// <summary>数组参数的元素个数;不是数组则为 0</summary>
    private static int CountArrayItems(IDictionary<string, object?> args, string name)
    {
        return args.TryGetValue(name, out object? value) &&
               value is JsonElement { ValueKind: JsonValueKind.Array } array
            ? array.GetArrayLength()
            : 0;
    }

    /// <summary>
    /// 路径只做<b>语义</b>收窄:工作区内转相对路径,之外保持绝对(越界可见)。
    ///
    /// 长度不管:摘要列是 <c>TextTrimming=PathSegmentEllipsis</c>,窄列下排版引擎
    /// 按目录段折叠中间、文件名永远可见,比内容层按固定 60 字保尾更贴合实际宽度。
    /// 其余两个消费者(审批卡换行、转录喂卡片)对长串也都是优雅溢出。
    /// </summary>
    private static string ShortenPath(string? path, string? workspaceRoot)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;

        string display = path.Trim();
        if (!string.IsNullOrEmpty(workspaceRoot) && Path.IsPathRooted(display))
        {
            try
            {
                string relative = Path.GetRelativePath(workspaceRoot, display);
                // 回溯到工作区之外时保持绝对路径:"../../etc/hosts" 比绝对路径更难读,也更看不出越界
                if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                    display = relative.Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                // 路径含非法字符,按原样返回
            }
        }

        return display;
    }
    
    private static string Shorten(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        string flat = text.Replace("\\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flat.Length <= MaxSummaryValueChars
            ? flat
            : string.Concat(
                flat.AsSpan(0, HeadChars),
                "…",
                flat.AsSpan(flat.Length - TailChars).TrimStart());
    }
}
