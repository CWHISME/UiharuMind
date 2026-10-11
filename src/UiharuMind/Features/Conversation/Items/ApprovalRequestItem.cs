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
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 审批卡片:三态回应,决定以 ChatMessage 形式回传给运行循环。
/// 扩展调用（ADR 0079）按拆开的内层调用画：浏览器调用按发起会话的页面说人话（<see cref="BrowserCallText"/>），
/// 接管（BrowserHandoff）换成「请你接手」的样子；「本会话允许」按扩展记，不按工具名
/// </summary>
public partial class ApprovalRequestItem : ConversationItemBase
{
    // 扩展「本会话允许」回应的理由前缀：回应本身只是一次 Once，别处的同一张卡靠它认出这是按扩展放行的
    private const string ExtensionSessionReason = "User allowed this extension for the conversation: ";

    private readonly ToolApprovalRequestContent _request;
    private readonly string? _sessionId; //发起调用的会话：浏览器调用按它的标签页说清点的是什么、接管时切哪个窗口
    private readonly IBrowserHost? _browser; //只有浏览器调用才有
    private readonly TaskCompletionSource<ChatMessage> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>参数摘要(审批展示)；浏览器调用是按页面说的人话</summary>
    [ObservableProperty] private string _argumentSummary;

    /// <summary>摘要是不是人话（浏览器调用按页面翻出来的）：是就不用等宽字体</summary>
    [ObservableProperty] private bool _isDescribed;

    /// <summary>是否已决出</summary>
    [ObservableProperty] private bool _isResolved;

    /// <summary>决出后那一行的文案</summary>
    [ObservableProperty] private string _resolvedText = string.Empty;

    /// <summary>接管卡上「切到浏览器」没切成时的说明；切成了为空</summary>
    [ObservableProperty] private string _showBrowserStatus = string.Empty;

    /// <param name="request">框架发来的审批请求</param>
    /// <param name="paths">会话工具的路径口径,用于把模型给的路径解析成真实文件以预演 diff;可空</param>
    /// <param name="sessionId">发起调用的会话:浏览器调用据此把 e21 翻成「按钮『提交订单』」;可空</param>
    /// <param name="browser">浏览器宿主;缺省时浏览器调用用全应用那一个(测试传假的)</param>
    public ApprovalRequestItem(ToolApprovalRequestContent request, AgentPathResolver? paths = null,
        string? sessionId = null, IBrowserHost? browser = null)
    {
        _request = request;
        _sessionId = sessionId;
        if (request.ToolCall is FunctionCallContent raw)
        {
            FunctionCallContent call = ExtensionCalls.Unwrap(raw);
            ExtensionName = ExtensionCalls.TryRead(raw, out ExtensionInvocation? invocation) ? invocation.Extension : null;
            AsksEveryTime = ExtensionRisk.AlwaysAsks(raw);
            ToolName = call.Name;
            // 只有浏览器调用才碰宿主：别的卡不该把浏览器那套（闲置回收的计时器）拉起来
            _browser = BrowserTools.HasPreview(call.Name) ? browser ?? PuppeteerBrowserHost.Shared : null;
            string? described = _browser != null ? BrowserCallText.Describe(call, _browser, sessionId) : null;
            _argumentSummary = described ?? AgentContentFormatter.SummarizeArguments(call, paths?.WorkspaceRoot);
            _isDescribed = described != null;
            IsBrowserHandoff = call.Name == BrowserTools.HandoffToolName;
            SuggestedCommandPattern = CharacterRunnerFactory.IsShellCommandTool(call.Name)
                ? ApprovalModeMapper.DeriveCommandPattern(
                    ApprovalModeMapper.ExtractCommand(call.Arguments) ?? string.Empty)
                : string.Empty;
            DiffLines = DiffLineView.BuildForToolCall(call, paths);
        }
        else
        {
            ToolName = request.ToolCall?.ToString() ?? "unknown";
            _argumentSummary = string.Empty;
            SuggestedCommandPattern = string.Empty;
        }
    }

    /// <summary>框架发来的审批请求（子窗口认领嵌套审批时按引用相认用）</summary>
    public ToolApprovalRequestContent Request => _request;

    /// <summary>待审批工具名</summary>
    public string ToolName { get; }

    /// <summary>shell 审批时按命令派生的"同类命令"放行模式;非 shell 或取不到命令为空串</summary>
    public string SuggestedCommandPattern { get; }

    /// <summary>编辑类工具(Write/Edit)的变更 diff;其余工具为空</summary>
    public IReadOnlyList<DiffLineView> DiffLines { get; } = [];

    /// <summary>是否有 diff 可展示(编码场景的审批体验就是 diff 体验)</summary>
    public bool HasDiff => DiffLines.Count > 0;

    /// <summary>是否提供"记住同类命令"选项(工具级的"本会话总是允许"对 shell 过粗)</summary>
    public bool CanRememberCommandPattern => SuggestedCommandPattern.Length > 0;

    /// <summary>
    /// 是不是「请你接手浏览器」：卡片换成接手的样子（切到浏览器 / 完成了 / 取消）。
    /// 不给「本会话允许」——框架按工具名记住它，以后的接管就不等人直接过了
    /// </summary>
    public bool IsBrowserHandoff { get; }

    /// <summary>扩展调用所属的扩展名；不是扩展调用为 null</summary>
    public string? ExtensionName { get; }

    /// <summary>每次都要问的那一类（接管、浏览器去本机地址）：不给「本会话允许」</summary>
    public bool AsksEveryTime { get; }

    /// <summary>
    /// 是否提供"本会话允许"：shell 有更细的"同类命令"，每次都要问的不能整会话放行。
    /// 扩展调用的这个按钮按扩展记（<see cref="ApproveExtensionCallback"/>），不交给框架按工具名记——
    /// 那样一点就把 <see cref="ExtensionCalls.CallName"/> 整个放行，连接管一起
    /// </summary>
    public bool CanApproveForSession => !CanRememberCommandPattern && !AsksEveryTime && !IsBrowserHandoff;

    /// <summary>「本会话允许」按钮的字：扩展调用写明放行的是哪个扩展</summary>
    public string SessionButtonText => ExtensionName != null
        ? Loc.Text(LangKey.AgentApproveSessionExtension, ExtensionLabels.Of(ExtensionName))
        : Loc.Text(LangKey.AgentApproveSession);

    /// <summary>用户点"记住同类命令"时回调(由会话侧写入并持久化)</summary>
    public Action<string>? RememberShellPatternCallback { get; set; }

    /// <summary>用户在扩展卡上点"本会话允许"时回调，参数是扩展名(由会话侧写入并持久化)</summary>
    public Action<string>? ApproveExtensionCallback { get; set; }

    /// <summary>用户决定完成后的回应消息</summary>
    public Task<ChatMessage> Response => _completion.Task;

    /// <summary>
    /// 这张卡是晚开的窗口补画的、而审批早已决出：页面已经往下走了，按此刻的快照翻编号会说错，退回原始参数
    /// </summary>
    public void UseRecordedArguments()
    {
        if (!IsDescribed || _request.ToolCall is not FunctionCallContent call) return;
        ArgumentSummary = AgentContentFormatter.SummarizeArguments(ExtensionCalls.Unwrap(call));
        IsDescribed = false;
    }

    /// <summary>
    /// 这次审批已在别处决出（同一次审批画在好几处，先点的那张算数）：收起按钮、显示那个决定
    /// </summary>
    /// <param name="response">最终的回应消息</param>
    public void MarkDecidedElsewhere(ChatMessage response)
    {
        if (IsResolved) return;
        // 「本会话总是允许」是框架的包装类型，认不出 ToolApprovalResponseContent 的就是它
        ToolApprovalResponseContent? plain = response.Contents.OfType<ToolApprovalResponseContent>().FirstOrDefault();
        bool extensionSession = plain?.Reason?.StartsWith(ExtensionSessionReason, StringComparison.Ordinal) == true;
        ResolvedText = ResolvedLabel(plain == null || extensionSession ? "session" : plain.Approved ? "once" : "deny");
        IsResolved = true;
        _completion.TrySetResult(response);
    }

    /// <summary>
    /// 外部取消(停止运行时):按拒绝处理
    /// </summary>
    public void CancelAsDeny()
    {
        if (IsResolved) return;
        IsResolved = true;
        ResolvedText = ResolvedLabel("deny");
        _completion.TrySetResult(new ChatMessage(ChatRole.User,
            new[] { ToolApprovalResponseFactory.Create(_request, EApprovalDecision.Deny, "Run stopped") }));
    }

    /// <summary>
    /// 审批动作:once / session / session-command / deny
    /// </summary>
    /// <param name="decision">决定代号</param>
    [RelayCommand]
    private void Resolve(string decision)
    {
        if (IsResolved) return;

        // 每次都要问的不许整会话放行：界面上没这个按钮，开发自动化之类的入口照样可能传进来
        if (AsksEveryTime && decision is "session" or "session-command") decision = "once";

        // 扩展的「本会话允许」:记进会话的扩展放行名单(后续这个扩展的一般命令由审批规则直接放行),本次按普通允许回应
        bool extensionSession = decision == "session" && ExtensionName != null;
        if (extensionSession) ApproveExtensionCallback?.Invoke(ExtensionName!);

        // "记住同类命令":先入会话放行清单(后续同类命令由审批规则直接放行),本次按普通允许回应
        if (decision == "session-command" && CanRememberCommandPattern)
        {
            RememberShellPatternCallback?.Invoke(SuggestedCommandPattern);
        }

        AIContent response = decision switch
        {
            "session" when extensionSession => ToolApprovalResponseFactory.Create(_request, EApprovalDecision.Once,
                ExtensionSessionReason + ExtensionName),
            "session" => ToolApprovalResponseFactory.Create(_request, EApprovalDecision.AlwaysInSession,
                "User chose to always approve this tool"),
            "deny" => ToolApprovalResponseFactory.Create(_request, EApprovalDecision.Deny, "User denied"),
            _ => ToolApprovalResponseFactory.Create(_request, EApprovalDecision.Once, "User approved"),
        };
        ResolvedText = ResolvedLabel(decision);
        IsResolved = true;
        _completion.TrySetResult(new ChatMessage(ChatRole.User, new[] { response }));
    }

    // 接管时把发起会话的浏览器窗口切到最前。没装 Chrome、档案被另一个实例占着（ADR 0074）时说在卡上，
    // 不让异常冲到界面线程
    [RelayCommand]
    private async Task ShowBrowser()
    {
        if (_sessionId == null || _browser == null) return;
        try
        {
            bool shown = await _browser.ShowAsync(_sessionId);
            ShowBrowserStatus = shown ? string.Empty : Loc.Text(LangKey.BrowserHandoffNoWindow);
        }
        catch (Exception e)
        {
            Log.Warning($"[Browser] could not show the window: {e.Message}");
            ShowBrowserStatus = Loc.Text(LangKey.BrowserHandoffShowFailed, e.Message);
        }
    }

    // 决定代号 → 卡片收起后那一行的文案（从前直接显示代号 once / deny）；扩展放行的那句带上扩展名
    private string ResolvedLabel(string decision) => Loc.Text(decision switch
    {
        "deny" when IsBrowserHandoff => LangKey.BrowserHandoffResolvedCancelled,
        _ when IsBrowserHandoff => LangKey.BrowserHandoffResolvedDone,
        "session" when ExtensionName != null => LangKey.AgentApprovalResolvedSessionExtension,
        "session" => LangKey.AgentApprovalResolvedSession,
        "session-command" => LangKey.AgentApprovalResolvedSessionCommand,
        "deny" => LangKey.AgentApprovalResolvedDenied,
        _ => LangKey.AgentApprovalResolvedOnce,
    }, ExtensionName != null ? ExtensionLabels.Of(ExtensionName) : string.Empty);
}
