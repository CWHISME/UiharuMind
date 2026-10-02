/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Characters;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.Search;
using UiharuMind.Features.Conversation.Search;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Diagnostics;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Conversation.Composer;
using UiharuMind.Features.Conversation.Group;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Features.Conversation.SidePanels;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 一次对话的视图模型，角色扮演与 agent 共用这一个实现。
/// 阶段 3 之后两者跑的是同一条路：session.Runner.RunAsync() → AIContent 流 → ApplyContent()，
/// 差异只剩"暴露哪些操作面板"(workspace / 权限档 / todo 侧栏 vs 角色卡 / 参数 / 翻译插件)，
/// 由角色的 IsAgent 控制显隐，因此不需要为此分出子类；
/// 原先的 ConversationViewModelBase 只有一个实现，已并入本类。
/// </summary>
public partial class ConversationViewModel : ViewModelBase, IConversationItemActionHost, IConversationReconcileHost,
    IAttachmentTrayHost, IConversationTurnHost, IDisposable
{
    /// <summary>发送身份:以用户身份发送并生成回复,或以角色身份直接写入一条回复</summary>
    public enum SendMode
    {
        User,
        Assistant
    }

    public ObservableCollection<ConversationItemBase> Items { get; } = new();

    /// <summary>输入框上方的附件盘(待发附件与「附件怎么变成一条用户消息」)</summary>
    public AttachmentTrayViewData Tray { get; }

    /// <summary>输入框的 / 命令面板(点名调用补全与内置命令)</summary>
    public CommandPaletteViewData Palette { get; }

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private string _inputPlaceholder = string.Empty;
    [ObservableProperty] private bool _scrollToEnd;
    [ObservableProperty] private SendMode _senderMode = SendMode.User;
    [ObservableProperty] private bool _isPlaintext;
    [ObservableProperty] private bool _isAutoCollapseThinking;

    [ObservableProperty] private bool _isSessionLoading; //会话切换构建中(空状态覆盖层此间不显示,避免闪烁)

    /// <summary>
    /// 发送/插话。
    ///
    /// <b>必须允许并发</b>：插话的入口就是再次点这个按钮——流式输出中 SendMessage 自己还在跑，
    /// AsyncRelayCommand 的默认禁并发会让 CanExecute 在执行期间返回 false，Avalonia 按钮据此把
    /// 整个按钮禁掉（走 <c>Button.IsEnabledCore && _commandCanExecute</c>，与 IsEnabled 绑定无关），
    /// 于是「跑着的时候想插话」永远点不动。这正是用户报告的置灰。
    /// 并发安全由 <see cref="SendCoreAsync"/> 内部自持：IsGenerating 分支只入注入队列，不碰时间轴。
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SendMessage()
    {
        // 补全开着时回车应当是"采纳候选"而不是发送。这里必须在命令入口改道:
        // Avalonia 的 KeyBindings 由 KeyboardDevice.ProcessRawEvent 沿视觉父链处理,
        // 时机在 KeyDown 路由事件被 raise 之前,连 Tunnel 都拦不住它
        if (Palette.AcceptCandidate()) return;

        string text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;
        InputText = string.Empty;
        // 发送即视为消费掉草稿,不再回填
        if (CurrentSession is { } sent) sent.ComposerDraft = "";
        await SendCoreAsync(text);
    }

    [RelayCommand]
    private void StopSending()
    {
        OnStopSending();
    }

    [RelayCommand]
    private void InputExtra()
    {
        // Tab 同理:补全开着时先采纳候选,否则会去切 plan/execute 模式
        if (Palette.AcceptCandidate()) return;
        OnInputExtra();
    }

    /// <summary>
    /// 采纳补全候选。命令面板上那一个的转发——回车与 Tab 的改道点必须留在本类的
    /// 命令入口上（见 <see cref="SendMessage"/>），这个转发让调用方不必绕道面板
    /// </summary>
    /// <returns>是否采纳了候选</returns>
    public bool AcceptCandidate() => Palette.AcceptCandidate();

    /// <summary>
    /// 输入框光标动了（视图报上来）：@ 补全看的是光标前那一段，光标挪走就该收起或换候选
    /// </summary>
    /// <param name="caret">光标位置</param>
    public void OnComposerCaretChanged(int caret)
    {
        _composerCaret = caret;
        _ = Palette.RefreshAsync(InputText, caret);
    }

    public ObservableCollection<TodoDisplayItem> Todos { get; } = new();

    /// <summary>右栏「能力」页签的数据（本会话实际挂上的工具、技能与 MCP）</summary>
    public ConversationCapabilityViewData Capabilities { get; } = new();

    /// <summary>工作目录选择器(当前目录与最近列表);工作目录这份状态由它持有</summary>
    public WorkspacePickerViewData Workspace { get; }

    /// <summary>本会话模型（默认跟随全局，选过即钉选）。右栏面板绑这一份</summary>
    public SessionModelViewData SessionModel { get; }

    [ObservableProperty] private int _permissionModeIndex = 1; //默认 AutoEdit
    [ObservableProperty] private EAgentMode _currentMode = EAgentMode.Execute;
    [ObservableProperty] private bool _hasTodos;

    /// <summary>当前会话元数据(未开始首轮前为空)</summary>
    public ChatSessionMeta? CurrentMeta { get; private set; }

    /// <summary>无会话时首轮发送创建新会话所用的角色;agent 页默认主代理,聊天页由页面壳指定</summary>
    public string NewSessionCharacterId { get; set; } = nameof(DefaultCharacter.ChenXiAgent);

    /// <summary>
    /// 空态下新建会话的形态（ADR 0050）：由页面壳按左侧类型指定——普通对话侧恒为 false，
    /// 即使预选了 agent 卡，首轮建出的也是普通对话形态；智能体侧恒为 true。
    /// </summary>
    public bool NewSessionIsAgentForm { get; set; } = true;

    /// <summary>
    /// 当前会话是否 agent 形态(决定工具行显示模式/权限还是发送身份)。
    /// 有会话看会话<b>存的形态</b>（agent 卡开成普通对话时这里为 false，工具行随之收起）；
    /// 空态看本侧的新建默认形态。
    /// </summary>
    public bool IsAgentSession => CurrentMeta is { } meta
        ? SessionManager.IsAgentSide(meta)
        : NewSessionIsAgentForm;

    /// <summary>
    /// 本会话的角色。尚无会话时取页面的新建默认角色——工具开关、技能清单、
    /// 计划模式与任务清单的可见性都按它判定(它们现在长在角色身上,见 ADR 0003)
    /// </summary>
    private CharacterData SessionCharacter =>
        _currentCharacter ?? CharacterManager.Instance.GetCharacterData(NewSessionCharacterId);

    /// <summary>输入框的模式切换是否可见(agent 会话且计划模式门控开启);随会话切换刷新</summary>
    public bool IsModeSwitchVisible => IsAgentSession && SessionCharacter.Tools.EnableAgentMode;

    /// <summary>侧栏任务清单是否可见(任务清单门控开启);随会话切换刷新</summary>
    public bool IsTodoListVisible => IsAgentSession && SessionCharacter.Tools.EnableTodoList;

    /// <summary>当前会话的记忆库面板(未挂接会话时为空)</summary>
    [ObservableProperty] private ConversationMemoryViewData? _memoryPanel;

    /// <summary>是否有可重新生成的目标。会话构建中沿用可见状态,避免切会话时按钮闪烁</summary>
    public bool CanRegenerate => !IsGenerating && (IsSessionLoading || Items.Any(x => x.CanRetry));

    private CharacterData? _currentCharacter; //当前会话所属角色,决定助手气泡的名字与头像

    /// <summary>会话集合变化(新会话创建/一轮结束),页面据此刷新左侧列表</summary>
    public event Action? SessionsChanged;

    /// <summary>当前模式显示标签</summary>
    public string ModeLabel => ConversationModeLabels.ModeLabel(CurrentMode);

    /// <summary>
    /// 当前会话对应的模型名:会话覆写名(找不到时回落全局但保留名字) → 全局当前模型 →
    /// 将被自动解析的偏好模型(未选模型时发送会走同一解析函数,显示与实际使用一致)。
    ///
    /// 没有会话覆写时缀一个「默认」:光报一个名字看不出它是<b>这个会话钉的</b>还是
    /// <b>跟着全局走的</b>,而这两件事的后续行为完全不同——后者会随用户换全局模型而变。
    /// </summary>
    public string SessionModelLabel
    {
        get
        {
            // 空态尚无会话：面板的预选记在草稿里，头部据此跟随，不回落全局。
            // 草稿只在空态有效（面板 SyncSelection 同口径），有会话时不读，避免旧草稿污染已落盘的会话
            string? pinned = CurrentSession?.SessionModelName;
            if (pinned == null && CurrentSession == null) pinned = SessionModel.PeekDraft();
            string name = pinned
                          ?? CurrentSession?.ChatModelRunningData?.ModelName
                          ?? LlmManager.Instance.CurrentRunningModel?.ModelName
                          ?? LlmManager.Instance.GetPreferredModelName(false)
                          ?? string.Empty;
            if (name.Length == 0 || !string.IsNullOrEmpty(pinned)) return name;
            return string.Format(Loc.Text(LangKey.SessionModelDefaultFormat), name);
        }
    }

    //================= 工具行图标态(Tag 驱动颜色 + 悬停提示当前值) =================
    // 文案与状态键全在 ConversationModeLabels,这里只是绑定用的转发

    /// <summary>模式状态键(Plan/Execute)</summary>
    public string ModeKey => CurrentMode.ToString();

    /// <summary>模式悬停提示</summary>
    public string ModeTooltip => ConversationModeLabels.ModeTooltip(CurrentMode);

    /// <summary>权限档状态键(ReadOnly/AutoEdit/FullAuto)</summary>
    public string PermissionModeKey => ConversationModeLabels.PermissionKey(PermissionModeIndex);

    /// <summary>权限档悬停提示</summary>
    public string PermissionTooltip => IsGroupMemberSession
        ? GroupMemberSessionViewData.PermissionTooltip(ConversationModeLabels.PermissionTooltip(PermissionModeIndex))
        : ConversationModeLabels.PermissionTooltip(PermissionModeIndex);

    /// <summary>权限档能不能在这里改：群成员跟群走，只在群视图设</summary>
    public bool IsPermissionEditable => !IsGroupMemberSession;

    /// <summary>发送身份对应的图标名(user/bot)</summary>
    public string SenderIconName => ConversationModeLabels.SenderIcon(SenderMode == SendMode.User);

    /// <summary>发送身份状态键(User/Assistant)</summary>
    public string SenderModeKey => SenderMode.ToString();

    /// <summary>发送身份悬停提示</summary>
    public string SenderTooltip => ConversationModeLabels.SenderTooltip(SenderMode == SendMode.User);

    partial void OnIsSessionLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRegenerate));
    }

    partial void OnSenderModeChanged(SendMode value)
    {
        OnPropertyChanged(nameof(SenderIconName));
        OnPropertyChanged(nameof(SenderModeKey));
        OnPropertyChanged(nameof(SenderTooltip));
    }

    private int _loadVersion; //会话加载版本号,用于放弃已被新切换取代的旧加载
    private bool _isDisplayed = true; //本实例是否正显示在界面上
    private ChatSessionMeta? _deferredLoad; //中途被切走而欠下的那次装载,切回来时接着做
    private bool _isLoadingSession; //加载会话期间抑制设置写回(加载是读,不是用户改动)
    private int _composerCaret = -1; //输入框光标（视图报上来）；-1 表示不知道，按末尾算

    private readonly IMessageService _messages; //弹提示与确认
    private readonly WorkspaceMcpApprovalFlow _mcpApproval; //选定工作区时为项目级 MCP 要一次确认
    private readonly ConversationItemActions _itemActions; //气泡上的编辑/删除/分叉/重试
    private readonly ConversationHistoryRenderer _history; //把历史画进 Items(回放、续窗、落盘补渲染)
    private readonly RegisteredApprovalAdopter _approvalAdopter; //子会话/群成员认领登记在册的审批卡
    private readonly ConversationSessionBinder _binder; //建/装会话并挂执行者
    private readonly ConversationTranscript _transcript; //实时流装配器,落点即 Items
    private readonly TurnDriver _driver; //一轮对话的编排,与定时任务共用同一份
    private readonly ConversationTurnRunner _turns; //跑一轮:装配、过闸、交给驱动
    private readonly HandoffWritingPlaceholder _handoffWriting; //整理交接文档时的占位卡
    private readonly SessionSignalHandlers _signalHandlers; //挂会话时接的几路回调,构造时建一份
    private SessionSignalSubscription? _signals; //挂在当前会话上的信号,换会话先摘再挂
    private readonly ConversationHistoryPager _pager; //历史开窗：首屏回放、续窗与裁剪
    private readonly ConversationHistoryReconciler _reconciler; //落盘与界面对不上的兜底

    /// <summary>token 用量（工具行文本与上下文占用的悬停面板）</summary>
    public ConversationUsageViewData Usage { get; }

    /// <summary>会话内搜索栏（关键词、范围、结果与上下条）</summary>
    public ConversationSearchViewData Search { get; }

    /// <summary>搜索跳转的决策：命中怎么进窗、落到哪张卡（滚动归视图）</summary>
    public ConversationSearchNavigator SearchNavigator { get; }

    /// <summary>
    /// 界面是否跟在底部，由宿主视图接上（它才持有那个滚动容器）。
    /// 视图切走时会把它<b>置回 null</b>，而那等于「没有视口」而不是「视口不在底部」——
    /// 运行期裁剪的闸门因此要连 <see cref="IsDisplayed"/> 一起看，不能把 null 折成 false
    /// （折成 false 的那版让后台会话一轮都没裁过，切回去就是几百条一次性重新布局）。
    /// </summary>
    public Func<bool>? IsStuckToBottomSource { get; set; }

    /// <summary>
    /// 本轮是否正在跑。装配会话的那一小段也算在内——那时执行者还没接手，
    /// 但界面必须已经显示停止按钮，否则用户能在装配期间再发一条。
    /// </summary>
    public bool IsGenerating => _turns.IsPreparing || _driver.IsRunning || IsExternallyDriven;

    /// <summary>此刻是否正在整理交接文档（压缩不是轮次，<c>IsGenerating</c> 不涵盖它）</summary>
    public bool IsCompacting => _driver.Busy == ETurnBusy.Compacting;

    /// <summary>
    /// 此刻有没有<b>本实例自己发起</b>的活在跑（装配、一轮、整理交接文档）。弃用本实例会取消它；
    /// 旁观别处跑的那一轮（<see cref="IsExternallyDriven"/>）不算，弃了不影响那边。
    /// 变化随 <see cref="IsGenerating"/> 一起通知
    /// </summary>
    public bool IsRunningOwnWork => _turns.IsPreparing || _driver.IsRunning || IsCompacting;

    /// <summary>
    /// 本会话名下还有<b>未了结的工作</b>：自己这一轮，或者名下还没交回报告的后台子代理。
    ///
    /// ⚠️ 它<b>不是</b> <see cref="IsGenerating"/>，两者不可合并（见 CONTEXT.md「未了结的工作」）。
    /// 这一个只驱动指示器；<see cref="IsGenerating"/> 还管着停止按钮与「打字走插话还是走发送」，
    /// 而后台子代理跑着时主代理那一轮<b>已经结束、没有轮次可插</b>——拿它去点亮忙碌，
    /// 用户打的字会进注入队列，一直等到几分钟后的唤醒轮才被消费。
    /// </summary>
    public bool HasPendingWork => IsGenerating
                                  || BackgroundSubAgentDispatcher.HasPendingWork(CurrentMeta?.SessionId);

    /// <summary>输入区上方待发的插话（已入注入队列、模型还没消费）</summary>
    public InterjectionQueueViewData Interjections { get; }

    /// <summary>
    /// 本会话是不是一个子会话（决定要不要显示「交回主代理」）。
    /// 会话是异步装载的，所以<b>装载完成时必须发一次变更通知</b>，
    /// 否则绑定停在初始的 false 上，那个按钮永远不出现
    /// </summary>
    public bool IsSubSession => CurrentSession?.IsSubSession == true;

    // 实时与回放共用一份路径口径:卡片里的草稿目录简写两边展开成同一个地方
    private AgentPathResolver? SessionPaths() =>
        CurrentSession is { } session ? AgentBuildProfile.PathResolverOf(session) : null;

    /// <summary>群壳那一份（右栏群卡、成员、产物、待审批条）；打开的不是群壳为 null</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGroupSession), nameof(IsSenderSwitchVisible))]
    private GroupShellViewData? _group;

    /// <summary>群成员会话那一份（待接收的群发言、群投递渲染、权限跟群）；打开的不是成员会话为 null</summary>
    [ObservableProperty] private GroupMemberSessionViewData? _groupMember;

    /// <summary>
    /// 本会话是不是群壳（ADR 0046）：输入框发的是群发言，气泡不给编辑/删除/重试（送达即不可改）
    /// </summary>
    public bool IsGroupSession => Group != null;

    /// <summary>能不能「从这里建群」：普通的单聊才行。与 <see cref="IsSubSession"/> 一样在会话装载完成时发变更通知</summary>
    public bool CanCreateGroupFromHere => GroupFromChat.CanStartFrom(CurrentMeta);

    /// <summary>
    /// 本会话是不是群成员会话。已解锁私聊：打字过轮次闸门排队，见 <see cref="GroupMemberTurnGate"/>（ADR 0046 未决已落地）。
    /// 看会话头而不是 <see cref="GroupMember"/>：后者挂接之后才建，而权限档的可改与否在装载一开始就要对
    /// </summary>
    public bool IsGroupMemberSession => CurrentMeta?.IsGroupMember == true;

    /// <summary>输入区是否可见</summary>
    public bool IsComposerVisible => true;

    /// <summary>「以角色身份发送」切换是否可见：普通对话才有，群里没有「替谁说话」这回事</summary>
    public bool IsSenderSwitchVisible => !IsAgentSession && !IsGroupSession;

    partial void OnGroupChanged(GroupShellViewData? oldValue, GroupShellViewData? newValue)
    {
        if (oldValue != null)
        {
            oldValue.SpeakersChanged -= NotifyBusyChanged;
            oldValue.Dispose();
        }

        if (newValue != null) newValue.SpeakersChanged += NotifyBusyChanged;
    }

    partial void OnGroupMemberChanged(GroupMemberSessionViewData? oldValue, GroupMemberSessionViewData? newValue)
    {
        oldValue?.Dispose();
    }

    /// <summary>请页面在列表里选中某个会话（建完群要切过去，而新建不经列表选中）</summary>
    public event Action<string>? OpenSessionRequested;

    /// <summary>
    /// 会话编号的短写（前 8 位）。<c>ContinueSubAgent</c>、日志与 <c>Agent/Workspaces</c> 的
    /// 房间目录后缀认的都是它，出了问题要贴出来的也是它——原先只能去右栏面板或日志里翻。
    /// 与 <see cref="IsSubSession"/> 一样在会话装载完成时发变更通知。
    /// </summary>
    public string SessionIdShort =>
        CurrentMeta?.SessionId is { Length: > ShortSessionIdLength } id ? id[..ShortSessionIdLength] : SessionIdFull;

    /// <summary>会话编号全串（悬停时显示，也是复制走的那一份）</summary>
    public string SessionIdFull => CurrentMeta?.SessionId ?? string.Empty;

    private const int ShortSessionIdLength = 8; //与 Agent/Workspaces 的房间目录后缀同宽

    /// <summary>
    /// 复制会话编号：显示的是短写，进剪贴板的是全串。
    /// </summary>
    [RelayCommand]
    private void CopySessionId()
    {
        if (SessionIdFull.Length == 0) return;
        App.Clipboard.CopyToClipboard(SessionIdFull, true, true);
    }

    /// <summary>
    /// 本会话这一轮是<b>别处</b>在驱动的（子代理跑着、定时任务无人值守跑着、
    /// 或者同一个会话在另一个界面壳里跑着）。
    ///
    /// 判据只能取运行态登记处：自己的 <see cref="_driver"/> 闲着并不代表会话空闲。
    /// 认错的后果不是显示不好看——用户打的字会走「发下一轮」而不是「插话」，
    /// 排进了队列却什么都不说（见 ADR 0021 的外驱条目）。
    /// </summary>
    public bool IsExternallyDriven =>
        !_driver.IsRunning && SessionManager.Instance.Running.IsBusy(CurrentMeta?.SessionId);

    /// <summary>
    /// 状态点配色键。三档而不是两档：本会话闲着、但名下还有后台子代理没交回报告，
    /// 既不是「在跑」也不是「空」——用户此刻要知道的正是这一档（见 CONTEXT.md「未了结的工作」）
    /// </summary>
    public string RunStatusKey => IsGenerating ? "Ready" : HasBackgroundWorkOnly ? "Progress" : "Idle";

    /// <summary>本会话这一轮没在跑，但名下还有后台子代理没交回报告</summary>
    public bool HasBackgroundWorkOnly => !IsGenerating && HasPendingWork;

    /// <summary>输入区上方的子代理状态行（等审批 / 在跑 / 压着等交回）</summary>
    public SubAgentStatusBarViewData SubAgentStatuses { get; } = new();

    /// <summary>名下子代理的处境变了：状态行跟着刷。只在 UI 线程上调</summary>
    private void NotifySubAgentStatusChanged() => SubAgentStatuses.Refresh(CurrentMeta?.SessionId);

    /// <summary>
    /// 本会话此刻卡在什么具名的事情上。两个来源合并成一处：整理交接文档在驱动那一层，
    /// 等 MCP server 连上（预连）在执行者那一层。
    ///
    /// 驱动优先：交接文档只会在一轮跑完之后开始，那时预连早已结束，两者实际不会同时为真；
    /// 万一同时为真，正在发请求的那件事更该说。
    /// </summary>
    public ETurnBusy Busy => _driver.Busy != ETurnBusy.None
        ? _driver.Busy
        : CurrentRunner?.Busy ?? ETurnBusy.None;

    /// <summary>
    /// 忙碌提示的文案；不忙时为空串，那一处整块不显示。
    /// 枚举 → 本地化键的映射只此一处——Core 侧不带文案，见 <see cref="ETurnBusy"/>
    /// </summary>
    public string BusyLabel
    {
        get
        {
            // 群壳自己那一轮不跑（ADR 0046），忙碌文案单独说：谁在发言
            if (Group?.BusyLabel() is { } groupBusy) return groupBusy;

            return Busy switch
            {
                ETurnBusy.ConnectingMcp => Loc.Text(LangKey.AgentMcpConnecting),
                ETurnBusy.Compacting => Loc.Text(LangKey.HandoffWriting),
                _ => string.Empty,
            };
        }
    }

    /// <summary>
    /// 发送按钮的文案。跑着的时候它是<b>插话</b>：消息进注入队列，agent 下一次机会消费。
    /// 那时按钮不能藏——藏了用户就只剩快捷键这一条路，而这正是"中途发不出消息"的由来。
    /// </summary>
    public string SendButtonText =>
        Loc.Text(IsGenerating ? LangKey.AgentInterject : LangKey.Send);

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="messages">弹提示与确认用的消息服务</param>
    public ConversationViewModel(IMessageService messages)
    {
        _messages = messages;
        _mcpApproval = new WorkspaceMcpApprovalFlow(messages);
        // 用量排最前:后面几个子模型的构造期回调就可能刷它
        Usage = new ConversationUsageViewData(ContextLength, () => SessionModelLabel,
            () => CurrentSession?.TotalGeneratedImages ?? 0);
        Search = new ConversationSearchViewData(() => CurrentSession?.History);
        // 子模型只吃窄依赖、不反向持有本类:附件盘取会话要用委托(首轮发送时会话还不存在),
        // 命令面板要能改写输入框并读当前角色,挂接器只需报忙碌态
        Tray = new AttachmentTrayViewData(this);
        Palette = new CommandPaletteViewData((text, caret) =>
            {
                _composerCaret = caret;
                InputText = text;
            }, () => SessionCharacter,
            () => IsAgentSession, //技能只在 agent 形态会话开放（ADR 0050）
            () => Group?.MentionTargets ?? []); //@ 补全只在群里有成员可点
        Interjections = new InterjectionQueueViewData(() => CurrentRunner, () => InputText, text => InputText = text,
            Tray.Attachments);
        _binder = new ConversationSessionBinder(NotifyBusyChanged);
        _itemActions = new ConversationItemActions(Items, this, messages);
        _history = new ConversationHistoryRenderer(Items, _itemActions, () => _currentCharacter,
            () => IsAutoCollapseThinking, () => GroupMember?.DeliveryRenderer, SessionPaths);
        _pager = new ConversationHistoryPager(Items, _history, () => CurrentRunner?.GetHistory() ?? [],
            () => IsDisplayed, () => IsStuckToBottomSource?.Invoke() ?? true,
            () => CurrentSession?.LiveTurn.IsTurnRunning == true);
        _pager.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName); //几个状态位原名转发给绑定
        _pager.ReturnedToLatest += () => ReturnedToLatest?.Invoke();
        SearchNavigator = new ConversationSearchNavigator(Items, _pager, () => CurrentSession?.History,
            () => IsSessionIdle && !IsCompacting);
        _reconciler = new ConversationHistoryReconciler(Items, _pager.Window, this);

        var agentSetting = AgentSettingConfig.Current;
        // 工作目录选择器要在最早构造:它持有那份状态,后面几处都从它读
        string? defaultWorkspace =
            !string.IsNullOrEmpty(agentSetting.DefaultWorkspacePath) &&
            Directory.Exists(agentSetting.DefaultWorkspacePath)
                ? agentSetting.DefaultWorkspacePath
                : null;
        // 群的工作区改之前要过群那两道关（空闲、跑过先确认）；单聊不拦
        Workspace = new WorkspacePickerViewData(defaultWorkspace, OnWorkspacePathChanged,
            path => Group?.ConfirmWorkspaceChangeAsync(path) ?? Task.FromResult(true));
        SessionModel = new SessionModelViewData(() => CurrentMeta, () => _isLoadingSession, OnSessionModelChanged);
        SessionModel.Refresh();

        _transcript = new ConversationTranscript(Items, () => ConversationItemFactory.CreateAssistant(_currentCharacter),
            pattern => CurrentSession?.AddSessionApprovedShellPattern(pattern),
            SessionPaths,
            createUserItems: _history.CreateUserItems);
        // 用量不经转录器转发:运行侧看得见同一条内容流,由它记账并写回会话本体,
        // 这里只负责把数字刷到界面上(UsageObserved 通知)
        _transcript.HousekeepingToolCalled += () => _ = RefreshTodosAsync();
        _transcript.UserMessageRendered += Interjections.OnRendered;
        _approvalAdopter = new RegisteredApprovalAdopter(_transcript, () => CurrentMeta?.SessionId, () => CurrentSession);
        _transcript.SubSessionAttached += RefreshSubSessionApprovalWait;
        _transcript.MessageBoundaryReached += OnMessageBoundaryReached;
        _driver = new TurnDriver(_transcript, Usage.Ledger, OnTurnNotice);
        _turns = new ConversationTurnRunner(this, _driver, _transcript);
        _handoffWriting = new HandoffWritingPlaceholder(Items, () => ScrollToEnd = true);
        // 观察别人驱动的那一轮时用它:内核仍是 _transcript,所以自己驱动时会被去重掉(见 LiveTurnStream)
        // 登记在册的才放行审批请求——嵌套审批的卡只该在子窗口弹,普通会话的观察窗弹出来也没人听
        _signalHandlers = new SessionSignalHandlers(_turns.ResolveApprovalsAsync, OnSessionHistoryAppended,
            OnSessionHistoryReplaced, OnObservedTurnEnded,
            new LiveObserverSink(_transcript, _approvalAdopter.AllowsObservedApproval), _transcript);
        _driver.StateChanged += OnDriverStateChanged;
        BackgroundSubAgentDispatcher.PendingWorkChanged += OnPendingWorkChanged;
        SessionManager.Instance.Running.StateChanged += OnSessionRunStateChanged;
        SessionManager.Instance.SessionUsageReported += OnSessionUsageReported;

        _permissionModeIndex = Math.Clamp(agentSetting.DefaultPermissionModeIndex, 0, 2);
        _currentMode = agentSetting.DefaultPlanMode ? EAgentMode.Plan : EAgentMode.Execute;

        _isPlaintext = ChatSettingConfig.Current.IsChatPlainText;
        _isAutoCollapseThinking = ChatSettingConfig.Current.IsChatAutoCollapseThinking;
        _transcript.AutoCollapseThinking = _isAutoCollapseThinking;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanRegenerate));

        // 这两个全局单例的事件必须能反注销,所以走具名方法而不是 lambda:
        // 本类现在是每会话一个实例、随会话切换来去,挂了不卸就是一路泄漏
        LlmManager.Instance.OnCurrentModelChanged += OnCurrentModelChanged;
        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        // 工作目录卡上的项目色要跟随主题（与左侧列表条目同一套刷新手势，见 SessionListModel）
        if (Application.Current is { } app) app.ActualThemeVariantChanged += OnWorkspaceThemeVariantChanged;
        InputPlaceholder = Loc.Text(_inputPlaceholderKey);
    }

    /// <summary>
    /// 把本子会话最新的结论交回派活者。
    ///
    /// 手动而不是自动：用户开这个窗口未必是为了帮派活者干活，手动那一下就是表态。
    /// 交回之后<b>不起新轮</b>——让派活者在用户没要求时自己动起来，
    /// 「这一轮的用户消息是什么」就没法回答了（见 ADR 0022 的轮次归属）。
    /// 唤醒轮只属于后台委派跑完那条路（见 <c>BackgroundSubAgentDispatcher</c> 与 ADR 0025）。
    /// </summary>
    [RelayCommand]
    private void HandBackToParent()
    {
        if (CurrentSession is not { } session) return;

        EHandoffOutcome outcome = SubAgentReportHandoff.Submit(session);
        LangKey key = outcome switch
        {
            EHandoffOutcome.Appended => LangKey.SubAgentHandoffDone,
            EHandoffOutcome.Replaced => LangKey.SubAgentHandoffReplaced,
            EHandoffOutcome.ParentBusy => LangKey.SubAgentHandoffParentBusy,
            EHandoffOutcome.ParentMissing => LangKey.SubAgentHandoffParentMissing,
            EHandoffOutcome.NothingToReport => LangKey.SubAgentHandoffNothing,
            _ => LangKey.SubAgentHandoffNothing,
        };
        HandoffNotice = Loc.Text(key);
    }

    /// <summary>「交回主代理」的结果提示（交回是一次性动作，没有别的反馈渠道）</summary>
    [ObservableProperty] private string _handoffNotice = string.Empty;

    /// <summary>
    /// 某个会话刚报了一次用量（来自执行线程）。两处要跟：群的成员列表那一行；
    /// 以及本窗口正旁观着别处跑的那一轮（群轮、子代理）——自己跑的那一轮由逐块通知刷，不走这里。
    /// 归属在执行线程上先认：报用量是高频事件，缓存着的每个实例都收得到，不相干的不该排进 UI 线程
    /// </summary>
    private void OnSessionUsageReported(string sessionId)
    {
        Group?.OnSessionUsageReported(sessionId);
        if (sessionId != CurrentMeta?.SessionId) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_driver.IsRunning || CurrentSession is not { } session) return;

            Usage.RestoreFrom(session);
            Usage.Refresh();
        });
    }

    /// <summary>
    /// 运行态登记处变了。<b>可能来自后台线程</b>（无头执行与子代理都不在 UI 线程上），
    /// 所以 marshal 之后再动界面属性
    /// </summary>
    /// <param name="sessionId">状态变化的会话</param>
    private void OnSessionRunStateChanged(string sessionId)
    {
        // 别人的运行态也要看一眼:派出去的子会话卡在审批上时,派活那张卡要挂出提示。
        // 只认自己名下的:并行群聊里成员运行态抖得很勤,缓存着的每个实例都收得到
        if (sessionId != CurrentMeta?.SessionId)
        {
            if (IsOwnSubSession(sessionId)) RefreshSubSessionApprovalWait(sessionId);
            Group?.OnSessionRunStateChanged(sessionId);
            return;
        }

        // 本会话转空闲了：唤醒轮之类别处驱动的轮次到此应该已经把内容留在了历史里，
        // 还对不上就是实时通道漏了，对一次尾部（方法内部只在没人跑时动手）
        bool busy = SessionManager.Instance.Running.IsBusy(sessionId);
        if (!busy) ReconcileHistoryTail("session idle");
        Dispatcher.UIThread.Post(() =>
        {
            // 别处开跑了(唤醒轮、子会话被续跑):实时流要往条目末尾追加,停在旧消息那段就接不上。
            // 运行登记早于第一段内容,这一步排在内容之前
            if (busy) ReturnToLatestIfDetached();
            NotifyRunStateChanged();
            NotifyBusyChanged(); //忙碌文案里有"别处正在跑"那一档,它跟着运行态变
        });
    }

    /// <summary>群改了权限档（成员跟群走）：只刷显示、不写回，写回由群那边做过了</summary>
    private void ApplyGroupPermission(int permissionModeIndex)
    {
        _isLoadingSession = true;
        try
        {
            PermissionModeIndex = permissionModeIndex;
        }
        finally
        {
            _isLoadingSession = false;
        }
    }

    /// <summary>
    /// 这个会话的历史被追加了。两条来路：一轮跑完/跑到某次服务调用时的落盘，
    /// 以及别处往我的历史里写了东西（子会话把后续报告交回派活者）。
    ///
    /// 粒度是每次服务调用，不是逐 token——逐 token 走的是实时内容流
    /// （<c>ChatSession.LiveTurn</c>），本方法据此分三档处理。
    /// </summary>
    /// <param name="fromIndex">新增段的起始下标</param>
    private void OnSessionHistoryAppended(int fromIndex)
    {
        // 自己正在跑的那一轮由实时流渲染,整段再补一遍就是每条显示两次
        bool ownTurn = _driver.IsRunning;
        // 别人驱动的那一轮也在往我这里流内容,同理:流已经渲染过的不能再渲染一遍
        bool streaming = CurrentSession?.LiveTurn.IsTurnRunning == true;

        Dispatcher.UIThread.Post(() =>
        {
            if (CurrentSession is not { } session) return;
            IReadOnlyList<ChatMessage> history = session.History;
            if (fromIndex < 0 || fromIndex >= history.Count) return;
            _pager.AppendPersisted(history, fromIndex, ownTurn, streaming);
            if (!ownTurn) Usage.Refresh(); //本轮的用量由 UsageObserved 逐块刷,这里重复一次只会抖
            Search.NotifyHistoryChanged();
        });
    }

    /// <summary>
    /// 观察的那一轮结束了：拿历史对一遍自己画出来的东西，顺序对不上就重放这一窗。
    ///
    /// 为什么要有这道网：一轮跑着的时候条目仍来自两股（实时内容流、历史落盘）。
    /// 边界与用户消息已由执行者在流里明说（推演那版错过两次："回答排在提问前面"
    /// "同一句话两条"），这里兜的是剩下那几类历史专属条目的顺序。<b>错了能自己纠正</b>
    /// 比每次靠截图发现强，顺带在日志里留痕——不然下一次还是只能靠用户告诉我。
    ///
    /// 代价是重放那一窗会让 markdown 重新渲染（闪一下），所以只在真的对不上时做。
    /// </summary>
    private void OnObservedTurnEnded()
    {
        if (_driver.IsRunning) return; //自己那一轮由 Persisted 通知负责配对,不走这里

        Dispatcher.UIThread.Post(() =>
        {
            if (CurrentSession is null) return;
            // 观察窗里没被认领的审批卡（复开的窗口、超时已拒的）到此不会再有人点，
            // 按拒绝收视觉。已认领已决出的不受影响（幂等）。
            _transcript.CancelPendingApprovals();
            _reconciler.Reconcile("observed turn ended");
            // 群成员那一轮的进群标记是落盘之后才盖的，接来源那一刻可能还没有：轮末补读一遍
            if (IsGroupMemberSession)
            {
                foreach (ConversationItemBase item in Items) item.RefreshFromSource();
            }
        });
    }

    /// <summary>
    /// 名下某个子会话的状态变了。
    ///
    /// 「等待审批」从前挂在父会话流里那张工具卡上，已删——审批是<b>报警</b>，
    /// 而一张跑了几十轮就滚没的卡找不到人（见 ADR 0025）。这里只刷卡片的
    /// 「已派出 / 结果待回」，那一档说的是这张卡此刻是不是在说实话，不是报警。
    /// </summary>
    /// <param name="subSessionId">子会话标识（本方法只用它判归属，刷的是整批卡）</param>
    private void RefreshSubSessionApprovalWait(string subSessionId)
    {
        if (subSessionId.Length == 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            _transcript.RefreshSubSessionPending();
            NotifySubAgentStatusChanged();
        });
    }

    /// <summary>这个会话是不是本会话派出去的子会话。只读索引，后台线程上也可调</summary>
    private bool IsOwnSubSession(string sessionId) =>
        CurrentMeta?.SessionId is { } self && SessionManager.Instance.GetMeta(sessionId)?.ParentSessionId == self;

    /// <summary>历史里的某一条被别处原地换掉了（后续报告替换了上一份），见 <see cref="ConversationHistoryRenderer.Replace"/></summary>
    /// <param name="index">被替换的下标</param>
    /// <param name="replaced">被换掉的那一条（界面靠它认回自己渲染出的条目）</param>
    private void OnSessionHistoryReplaced(int index, ChatMessage replaced)
    {
        if (_driver.IsRunning) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (CurrentSession is not { } session) return;
            IReadOnlyList<ChatMessage> history = session.History;
            if (index < 0 || index >= history.Count) return;

            _history.Replace(history, index, replaced);
            Search.NotifyHistoryChanged();
        });
    }

    /// <summary>挂上这个会话的信号。重复挂接先摘再挂，不攒订阅</summary>
    private void AttachSessionSignals(ChatSession session)
    {
        DetachSessionSignals();
        _signals = new SessionSignalSubscription(session, _signalHandlers);
    }

    private void DetachSessionSignals()
    {
        _signals?.Dispose();
        _signals = null;
    }

    private void OnDriverStateChanged()
    {
        NotifyRunStateChanged();
        NotifyBusyChanged();
    }

    /// <summary>
    /// 尾部对账兜底：交回报告/唤醒回复已落盘、但实时通道都没画出来时补齐。
    /// 只做 marshal，判定与执行归 <see cref="ConversationHistoryReconciler"/>。
    ///
    /// 守卫全部放到 UI 线程上判：两个调用点都可能来自后台线程，在后台读共享状态本身就是
    /// 数据竞争，还容易读到旧值误判「空闲」。Post 一次的成本远低于赌一个线程安全。
    /// </summary>
    /// <param name="reason">触发来源，进日志，方便区分是哪条路兜住的</param>
    private void ReconcileHistoryTail(string reason)
    {
        Dispatcher.UIThread.Post(() => _reconciler.Reconcile(reason));
    }

    /// <summary>名下的后台委派多了或少了一个。<b>可能来自后台线程</b>，marshal 之后再通知绑定</summary>
    private void OnPendingWorkChanged(string sessionId)
    {
        if (sessionId != CurrentMeta?.SessionId) return;
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(HasPendingWork));
            OnPropertyChanged(nameof(HasBackgroundWorkOnly));
            OnPropertyChanged(nameof(RunStatusKey));
            NotifySubAgentStatusChanged();
            // 流里那几张委派卡也要跟着改档:工具调用早返回了,光看结果它们全是「成功」
            _transcript.RefreshSubSessionPending();

            // 全部交回了：该到的报告都已落盘，此刻还对不上就是实时通道漏了，对一次尾部。
            // pending 判据必须在这里重读：事件来自后台线程，在外面读到的是旧值，
            // 会把「还没交完」看成「交完了」提前对一次空账。
            if (!BackgroundSubAgentDispatcher.HasPendingWork(sessionId)) _reconciler.Reconcile("background work settled");
        });
    }

    private void NotifyRunStateChanged()
    {
        OnPropertyChanged(nameof(IsGenerating));
        OnPropertyChanged(nameof(RunStatusKey));
        OnPropertyChanged(nameof(CanRegenerate));
        OnPropertyChanged(nameof(SendButtonText)); //跑着的时候它显示「插话」
        OnPropertyChanged(nameof(HasPendingWork)); //自己这一轮也算「未了结的工作」
        OnPropertyChanged(nameof(HasBackgroundWorkOnly));
    }

    /// 忙碌态可能从后台线程上抛(预连在装配线程上),而绑定要求属性变更在 UI 线程上发生
    private void NotifyBusyChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(Busy));
            OnPropertyChanged(nameof(BusyLabel));
        });
    }

    private void OnCurrentModelChanged(ModelRunningData? model)
    {
        OnPropertyChanged(nameof(SessionModelLabel));
        SessionModel.Refresh(); //默认项的跟随对象变了,下拉标签跟着变
        Tray.NotifyVisionStateChanged(); //换成非视觉模型时,待发的图就该立刻出警示
        // 上限是跟着模型走的:换个模型,占用的分母、三条水位与配色档位全都变了
        Usage.Refresh();
    }

    /// <summary>本会话的覆写变了，有效模型与用量分母都跟着变，走与全局切换同一套刷新</summary>
    private void OnSessionModelChanged()
    {
        OnPropertyChanged(nameof(SessionModelLabel));
        Tray.NotifyVisionStateChanged();
        Usage.Refresh();
    }

    private void OnLanguageChanged()
    {
        InputPlaceholder = Loc.Text(_inputPlaceholderKey);
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(ModeTooltip));
        OnPropertyChanged(nameof(PermissionTooltip));
        OnPropertyChanged(nameof(SenderTooltip));
        Usage.Refresh(); //压缩水位那句提示是在 C# 里拼的,不会自己跟着语言变
        SubAgentStatuses.Refresh(CurrentMeta?.SessionId, force: true); //同上:那几行的文案也是取一次存一次
    }

    /// <summary>
    /// 主题切换后重取工作目录卡上的项目色。<c>WorkspaceColor</c> 是绑定值，
    /// 不通知就不会重算，而 <see cref="WorkspaceTint.For"/> 是按当前主题取亮度的。
    /// </summary>
    private void OnWorkspaceThemeVariantChanged(object? sender, EventArgs e)
    {
        Workspace.RefreshWorkspaceColor();
    }

    /// <summary>
    /// 弃用本实例：反注销全局事件、弃用运行侧（它会取消正在跑的那一轮）。
    ///
    /// 只取消、不在这里补写取消结果——运行循环还活着，它自己会在取消分支里收尾。
    /// 要在进程即将消失时同步补写的场合用 <see cref="TurnDriver.SettleAllForShutdown"/>。
    /// </summary>
    public void Dispose()
    {
        // 实例被弃用前把草稿落盘(静默,不动列表排序)。此后恢复靠会话头上的 ComposerDraft
        if (CurrentSession is { } disposing) disposing.SaveMeta(false);

        LlmManager.Instance.OnCurrentModelChanged -= OnCurrentModelChanged;
        SessionModel.Dispose();
        LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
        if (Application.Current is { } app) app.ActualThemeVariantChanged -= OnWorkspaceThemeVariantChanged;
        _transcript.SubSessionAttached -= RefreshSubSessionApprovalWait;
        _transcript.MessageBoundaryReached -= OnMessageBoundaryReached;
        _approvalAdopter.Dispose();
        Group?.Dispose();
        GroupMember?.Dispose();
        _driver.StateChanged -= OnDriverStateChanged;
        BackgroundSubAgentDispatcher.PendingWorkChanged -= OnPendingWorkChanged;
        SessionManager.Instance.Running.StateChanged -= OnSessionRunStateChanged;
        SessionManager.Instance.SessionUsageReported -= OnSessionUsageReported;
        DetachSessionSignals();
        // 执行者归会话所有、比本视图活得久,回调不摘就是一路泄漏到已销毁的视图上
        if (CurrentRunner is { } runner) runner.BusyChanged = null;
        _turns.CancelPreparing();
        Usage.Dispose();
        Search.Dispose();
        _driver.Dispose();
        MemoryPanel?.Detach();
    }

    private LangKey _inputPlaceholderKey = LangKey.AgentInputWatermark;

    /// <summary>输入框占位文案的本地化键,页面壳按场景覆盖(agent 页描述任务,聊天页输入消息)</summary>
    public LangKey InputPlaceholderKey
    {
        get => _inputPlaceholderKey;
        set
        {
            _inputPlaceholderKey = value;
            InputPlaceholder = Loc.Text(value);
        }
    }

    [RelayCommand]
    private void ChangeSendMode()
    {
        SenderMode = SenderMode == SendMode.User ? SendMode.Assistant : SendMode.User;
    }

    partial void OnIsPlaintextChanged(bool value)
    {
        ChatSettingConfig.Current.IsChatPlainText = value;
        ChatSettingConfig.Current.Save();
    }

    partial void OnIsAutoCollapseThinkingChanged(bool value)
    {
        _transcript.AutoCollapseThinking = value;
        ChatSettingConfig.Current.IsChatAutoCollapseThinking = value;
        ChatSettingConfig.Current.Save();
    }

    /// <summary>
    /// 重新生成最后一条回复:等价于对最后一条可重试的消息执行重试
    /// </summary>
    [RelayCommand]
    private async Task RegenerateLast()
    {
        if (IsGenerating) return;
        // 停在旧消息那段时条目里的「最后一条」不是历史的最后一条:先回到最新再挑,否则重试会截掉后面整段历史
        ReturnToLatestIfDetached();
        ConversationItemBase? target = Items.LastOrDefault(x => x.CanRetry);
        if (target != null) await _itemActions.Retry(target);
    }

    //================= 模式 / 配置 =================

    [RelayCommand]
    private void CycleMode()
    {
        CurrentMode = CurrentMode.Next();
    }

    private void OnInputExtra()
    {
        CycleMode();
    }

    partial void OnCurrentModeChanged(EAgentMode value)
    {
        ApplyMode();
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(ModeKey));
        OnPropertyChanged(nameof(ModeTooltip));
    }

    partial void OnPermissionModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(PermissionModeKey));
        OnPropertyChanged(nameof(PermissionTooltip));
        if (CurrentMeta == null || _isLoadingSession) return;
        CurrentMeta.PermissionModeIndex = value;
        ConversationSessionBinder.PersistSettings(CurrentMeta);
    }

    /// <summary>当前会话的角色(尚无会话时是新建会话将使用的那个)</summary>
    private CharacterData ActiveCharacter =>
        _currentCharacter ?? CharacterManager.Instance.GetCharacterData(NewSessionCharacterId);

    /// <summary>当前角色标识(选择器据此把自己排除掉)</summary>
    public string ActiveCharacterId => ActiveCharacter.CharacterId;

    /// <summary>当前角色名(右侧栏角色行)</summary>
    public string ActiveCharacterName => ActiveCharacter.CharacterName;

    /// <summary>当前角色描述(右侧栏角色行副文本)</summary>
    public string ActiveCharacterDescription => ActiveCharacter.Description;

    /// <summary>当前角色头像</summary>
    public Bitmap? ActiveCharacterIcon => IconUtils.GetCharacterBitmapOrDefault(ActiveCharacter);

    /// <summary>
    /// 编辑当前角色。右栏那三个能力徽章要能就地点进去改——
    /// 「这个工具贵」与「关掉它」隔着一次跳页的话，那笔账摆出来也没人会动。
    ///
    /// 改完<b>不立即刷能力面板</b>：面板显示的是「此刻实际挂上的」，而装配要到
    /// 下一轮发送才按快照差异重建，提前刷会显示一份还没生效的名单。
    /// </summary>
    [RelayCommand]
    private void EditActiveCharacter()
    {
        CharacterDraft draft = CharacterDraft.ForEdit(ActiveCharacter, _messages);
        // 让编辑页能按开关标出「关掉这一档能省多少 token」。必须在能力面板首次建之前给
        draft.CapabilitySnapshot = CurrentRunner?.GetCapabilities();
        CharacterWindows.ShowEditCharacterWindow(draft);
    }

    /// <summary>
    /// 换角色。有会话就换会话的角色，还没有会话就只改新建默认值。
    ///
    /// 刻意不在此处重挂执行者：装配快照含角色标识与重算的系统提示，
    /// <b>下一轮发送时自然重建</b>——因此生成中换角色不会打断当前这一轮。
    /// </summary>
    /// <param name="character">新角色</param>
    public void ChangeCharacter(CharacterData character)
    {
        NewSessionCharacterId = character.CharacterId;
        if (CurrentMeta != null && character.CharacterId != CurrentMeta.CharacterId)
        {
            CurrentMeta.CharacterId = character.CharacterId;
            CurrentSession?.ChangeCharacter(character);
            ConversationSessionBinder.PersistSettings(CurrentMeta);
        }

        _currentCharacter = character;
        OnPropertyChanged(nameof(ActiveCharacterName));
        OnPropertyChanged(nameof(ActiveCharacterDescription));
        OnPropertyChanged(nameof(ActiveCharacterIcon));
        NotifyCharacterKindChanged();
        SessionsChanged?.Invoke(); //会话列表里的角色头像/名字跟着变
        // 换角色即换预演输入:空态没会话,能力面板的固定开销统计正是按角色算的,
        // 不刷就停在旧角色的数字上。非空态以当前 runner 为准、下一轮发送自然重建,这里不强行重启
        _ = RefreshCapabilitiesAsync();
    }

    /// <summary>
    /// 建群（ADR 0046）。群的类型跟着当前这一侧：空态下 <see cref="IsAgentSession"/> 与切换器的类型一一对应；
    /// 智能体群的工作区取右栏卡片上此刻选着的那个。落在 VM 而不是页面：中间空态寄在
    /// ConversationView 的槽里，DataContext 是 VM，页面命令在那里不可用
    /// </summary>
    [RelayCommand]
    private async Task CreateGroupChatAsync()
    {
        bool isAgentGroup = IsAgentSession;
        string? workspace = isAgentGroup ? Workspace.Path : null;
        GroupCreateRequest? request = await GroupCreateWindow.ShowAsync(isAgentGroup, workspace);
        if (request == null) return;

        ChatSession group = GroupChatSessions.Create(request.Name, isAgentGroup, request.Members, workspace,
            request.MemberModelNames, request.Schedule);
        SessionsChanged?.Invoke();
        OpenSessionRequested?.Invoke(group.SessionId);
    }

    /// <summary>不开口也让大家接着说，见 <see cref="GroupShellViewData.ContinueAsync"/></summary>
    [RelayCommand]
    private async Task ContinueGroupRound()
    {
        if (Group is not { } group) return;
        ScrollToEnd = true;
        await group.ContinueAsync();
    }

    /// <summary>
    /// 从这里建群（方案 v6 §6.6b 规则 5 的轻量落地）：另开一个群、带上这段单聊的背景，原单聊不动，见 <see cref="GroupFromChat"/>
    /// </summary>
    [RelayCommand]
    private async Task CreateGroupFromHereAsync()
    {
        if (CurrentSession is not { } source || !CanCreateGroupFromHere) return;
        ChatSession? group = await GroupFromChat.CreateAsync(source, Workspace.Path, _messages);
        if (group == null) return;
        SessionsChanged?.Invoke();
        OpenSessionRequested?.Invoke(group.SessionId);
    }

    /// <summary>
    /// 工作目录变化：写回会话头字段。装载会话期间不写——那是读取，不是用户改动
    /// </summary>
    /// <param name="value">新工作目录</param>
    private void OnWorkspacePathChanged(string? value)
    {
        // 会话头字段只在已有会话时写回；而 MCP 那一段与有没有会话无关——
        // 新会话（CurrentMeta 为空）恰恰是最需要知道"这个项目会连什么"的时候
        if (!_isLoadingSession) _ = OnWorkspaceChangedAsync(value);
        if (CurrentMeta == null || _isLoadingSession) return;
        CurrentMeta.WorkspacePath = value;
        ConversationSessionBinder.PersistSettings(CurrentMeta);
        // 群的草稿目录、成员预演都跟着工作区走
        Group?.OnWorkspaceChanged();
    }

    /// 换了工作区:先把该项目的授权要到手,再无条件刷一次面板。
    /// 刷新不能只在"有待确认项"时做——项目级名单本身跟着工作区变,
    /// 换到一个没有 .mcp.json 的目录时,上一个项目那几条必须从预告区消失
    private async Task OnWorkspaceChangedAsync(string? workspacePath)
    {
        await _mcpApproval.PromptAsync(workspacePath);
        await RefreshCapabilitiesAsync();
    }

    //================= 发送与运行循环 =================

    private async Task SendCoreAsync(string text)
    {
        ReturnToLatestIfDetached(); //停在搜索跳到的旧消息那里时发话:先回到末尾,新气泡才接得上
        // 群壳永不跑轮:打的字一律是群发言,交给调度器——闲着就开一圈,跑着就插进当前发言人那一轮。
        // 排在最前:压缩命令、以角色身份发送、插话这几条路对群壳都不成立
        if (Group is { } group)
        {
            (string postText, List<DataContent>? images) = Tray.BuildGroupPost(text, Tray.TakePending());
            Tray.FlushOwnedFiles(); //粘贴图落的盘归群壳：删群时一并删
            ScrollToEnd = true;
            await group.PostAsync(postText, images);
            return;
        }

        // 手动压缩:任务的自然边界由你比水位更清楚,在边界上压缩,交接文档质量高得多。
        // 命令后跟的文字作为额外指示随写文档的请求一起交给模型(见 TryParseCompact)
        if (CommandPaletteViewData.TryParseCompact(text, out string? compactExtra))
        {
            if (IsGenerating)
            {
                // 运行中命令没处安放:把字还给输入框并明说,静默吞掉就是"点了没反应"
                InputText = text;
                _messages.ShowNotification(
                    Loc.Text(LangKey.CompactWhileRunning), severity: MessageSeverity.Warning);
                return;
            }

            if (CurrentSession is not { } current)
            {
                // 空会话(新建未发首轮):没有可压缩的内容,明说而不是静默吞掉
                _messages.ShowNotification(
                    Loc.Text(LangKey.HandoffNothingToCompact), severity: MessageSeverity.Information);
                return;
            }

            // 用全局事实而非本地 Busy:同会话可能被另一窗口/外驱压缩,本地 driver 看不到
            if (TurnDriver.IsCompacting(current.SessionId))
            {
                // 正在整理时再敲一次:防重复写两份交接文档、出两张卡
                _messages.ShowNotification(
                    Loc.Text(LangKey.CompactAlreadyInProgress), severity: MessageSeverity.Information);
                return;
            }

            await _driver.CompactAsync(current, current.Runner, compactExtra);
            return;
        }

        // 运行中输入 = 插话:入注入队列,agent 下一次机会消费。
        // 此刻<b>不进时间轴</b>:它在时间轴上的位置是"模型消费它的那一刻",由执行者在那一刻
        // 发 UserMessageContent 画出来。发送时就画会把它排在正在流的回复前面,而历史里它在后面。
        // 等待期间在输入区挂一条待发提示,免得看着像没发出去
        if (IsGenerating)
        {
            // 群成员会话正在跑群里那一轮:打字不该插话——插话的回应会被群轮按「这一轮正文」
            // 收成群发言,私聊就泄进群里了。掉到正常发送路径,由 轮次运行器过闸排队
            if (GroupMember?.IsRunningGroupTurn(_driver.IsRunning) != true)
            {
                // 插话与正常发送共用同一套组装:附件盘上的图要进消息,不能只发 text
                List<ConversationAttachment>? interjectionAttachments = Tray.TakePending();
                ChatMessage? interjection = CurrentSession == null
                    ? null
                    : BuildOutgoingMessage(text, interjectionAttachments);
                if (await Interjections.TryInjectAsync(interjection, text, interjectionAttachments)) return;

                // 排不进去(执行者还在装配、或这个执行者不支持注入):字和附件已还回去,明说一声
                _messages.ShowNotification(
                    Loc.Text(LangKey.AgentInterjectUnavailable), severity: MessageSeverity.Warning);
                return;
            }

            // 他正在群里发言:明说排队,接着走下面的正常发送路径(轮次运行器会等到群轮结束)
            _messages.ShowNotification(
                Loc.Text(LangKey.GroupMemberBusyQueueTip), severity: MessageSeverity.Information);
        }

        // 以角色身份发送:直接写入一条回复,不触发生成
        if (SenderMode == SendMode.Assistant)
        {
            await AppendAssistantMessageAsync(text);
            return;
        }

        List<ConversationAttachment>? attachments = Tray.TakePending();
        Palette.ClosePicker();

        // 点名调用:/技能名 [参数]。技能正文直接进本轮并常驻历史,气泡只显示用户敲的那一行。
        // 见 docs/adr/0001——框架的 load_skill 取不到退出模型自选的技能,所以不走它
        SkillInvocation? invocation = await Palette.TryBuildSkillInvocationAsync(text);

        ChatMessage userMessage = BuildOutgoingMessage(invocation?.InjectedText ?? text, attachments);
        if (invocation != null) NamedSkillAnnotations.Mark(userMessage, invocation, text);

        // 乐观显示的气泡此刻就接上来源:轮首流里会再来一次同一个实例(UserMessageContent),
        // 转录器按引用认出它才不会画第二遍;副本落盘时由 WireStreamed 换成历史里那一条
        Items.Add(_itemActions.Wire(ConversationItemFactory.CreateUser(text, userMessage, attachments), userMessage));
        ScrollToEnd = true;
        await _turns.RunAsync(userMessage, text);
    }

    /// <summary>组装要发出去的用户消息。群成员会话里用户直接打的话是私聊，见 <see cref="GroupMemberSessionViewData.BuildPrivateMessage"/></summary>
    private ChatMessage BuildOutgoingMessage(string text, List<ConversationAttachment>? attachments) =>
        IsGroupMemberSession
            ? GroupMemberSessionViewData.BuildPrivateMessage(text, body => Tray.BuildUserMessage(body, attachments))
            : Tray.BuildUserMessage(text, attachments);

    /// <summary>
    /// 以角色身份写入一条回复(角色扮演的"替角色说话"),写入历史并立即持久化
    /// </summary>
    private async Task AppendAssistantMessageAsync(string text)
    {
        ChatSession session;
        try
        {
            session = await EnsureSessionAsync(text, CancellationToken.None);
        }
        catch (Exception e)
        {
            Log.Error($"Append assistant message failed: {e}");
            Items.Add(new ErrorItem { Message = e.Message });
            return;
        }

        ChatMessage message = session.CreateMessage(ChatRole.Assistant, text);
        int before = session.History.Count;
        session.History.Add(message);
        session.SaveAppended(before); //只追加这一条,不重写整份历史

        TextConversationItem item = ConversationItemFactory.CreateAssistant(_currentCharacter);
        item.Append(text);
        item.IsDone = true;
        Items.Add(_itemActions.Wire(item, message));
        ScrollToEnd = true;
    }

    private void OnStopSending()
    {
        _turns.Cancel(); //还卡在装配阶段时也要停得下来
        // 外驱时要停的是别处那一轮——自己的 driver 根本没在跑。
        // 停止按钮既然显示出来了就必须真能停,否则是个骗人的按钮
        if (IsExternallyDriven) TurnDriver.CancelSession(CurrentMeta?.SessionId);
        // 群壳的「在跑」是调度器那一圈,停它才停得下当前发言人与后面还没轮到的人
        Group?.Stop();
        _transcript.CancelPendingApprovals();
        _ = Interjections.CancelAllAsync(); //停止后待发的插话不该还挂在输入区,也从队列撤掉
    }

    /// <summary>
    /// 轮内的一次消息边界：<b>不在界面上的实例</b>在此把条目压回后台上限。
    ///
    /// 为什么非得在这里插一手：落盘通知一整轮才来一次，而一轮 agent 回复可以跑几十次
    /// 服务调用、堆出几百个条目。只在轮末裁，等于「切走的会话按一屏留」这条口径
    /// 在长轮次里整轮失效——切回去照样是几百条一次性重新实体化，
    /// 用户看到的就是「明明做过截断，切回运行中的会话还是很长」。
    ///
    /// 前台也裁，只是按宽得多的运行期上限（80），而且<b>只在跟底时</b>——
    /// 那道闸在裁剪器里（<c>canTrimSource</c>），用户一上滚就自动关掉，正在读的内容不会被抽走。
    /// 前台这一半本来出于「流式中途裁会打乱跟底与 Offset」的顾虑没做，实测把它按回去了：
    /// 一轮长下来光会话流的控件对象图就能涨 86MB（一张卡上百个控件，列表还没有虚拟化），
    /// 而那个峰值正是 GC 提交量下不来的源头。消息边界是轮内最安稳的时机：流段刚收尾，
    /// 来源消息也刚落盘，锚点与滚动位置都是确定的。
    ///
    /// 回填必须走在裁剪前面——锚点就是回填出来的那些来源消息。
    /// </summary>
    private void OnMessageBoundaryReached()
    {
        _itemActions.WireStreamed(CurrentRunner?.GetHistory() ?? []);
        _pager.TrimToBudget();
    }

    /// <summary>
    /// 运行侧通知 → 界面动作。措辞在这里落地：Core 只说发生了什么，本地化不下沉。
    /// </summary>
    /// <param name="notice">通知</param>
    private void OnTurnNotice(TurnNotice notice)
    {
        switch (notice.Kind)
        {
            case ETurnNotice.Started:
                OnPropertyChanged(nameof(SessionModelLabel)); //本轮实际使用的模型此刻可解析
                break;

            case ETurnNotice.RoundCompleted:
                // 与转录器的内务工具通知同一口径:不等它,todo 面板迟一步刷无妨,
                // 等的话会与下一轮的 RunAsync 抢执行者那把门闸
                _ = RefreshTodosAsync();
                // 装配可能在本轮开头因事实变化而重建(换模型、改角色卡、MCP 取回新工具),
                // 所以能力面板跟着每轮刷一次,而不是只在挂接时刷
                _ = RefreshCapabilitiesAsync();
                break;

            case ETurnNotice.Persisted:
                _itemActions.WireStreamed(CurrentRunner?.GetHistory() ?? []);
                StampThinkingStats();
                // 裁剪必须排在回填之后:锚点就是回填出来的那些来源消息。
                // 不在界面上的会话直接按首屏量级裁——它在后台可能还要跑很多轮,
                // 每轮都只裁回运行期上限的话,切回去照样是一屏之外的条目在重新实体化
                _pager.TrimToBudget();
                break;

            case ETurnNotice.Ended:
                _transcript.ResolveApprovals(_transcript.PendingApprovals.ToList());
                SessionsChanged?.Invoke();
                // 流式期间的 UsageObserved 走防抖合并,停流后立刻补刷最终值,
                // 不能等下一个事件或防抖窗口——否则最后一个数要拖 250ms 才上屏
                Usage.RefreshCoalesced(force: true);
                // 这轮结束还没消费的插话不会再有机会被这轮消费,留着只会让下一轮莫名收到旧话
                _ = Interjections.CancelAllAsync();
                break;

            case ETurnNotice.Failed:
                Items.Add(new ErrorItem { Message = notice.Payload ?? string.Empty });
                break;

            case ETurnNotice.ScrollToEnd:
                ScrollToEnd = true;
                break;

            case ETurnNotice.UsageObserved:
                // 流式期间 provider 通常每个 chunk 都带 UsageContent,逐块直达会让状态栏文本与
                // ToolTip 面板跟着每个 chunk 重排而闪烁;合并到 250ms 窗口内一次性刷新。
                // 账本值仍逐块记准,这里只是界面刷新频率的节流,与打字防抖同一套路
                Usage.RefreshCoalesced();
                break;

            case ETurnNotice.KnowledgeRetrieved:
                // 落盘那份由 SessionChatHistoryProvider 在轮末插进历史,这里只管本轮即时可见;
                // 两者内容同源,重载会话后由回放分支再造出同一张卡
                Items.Add(ConversationItemFactory.CreateKnowledgeCard(notice.Payload ?? string.Empty));
                break;

            case ETurnNotice.HandoffWritten:
                _handoffWriting.Hide();
                // 卡片默认由落盘/回放路径渲染(ConversationHistoryRenderer.Build 的 HandoffNote 分支),这里只收掉占位;
                // 通知自己再 Add 一张会与落盘渲染各画一遍,同一条交接文档就出两张卡。
                // 只在自己那一轮正跑时补画:落盘路径走 AppendHandedBackReports 不画交接文档,
                // 只有通知这一条路。外部驱动者的轮(streaming)由 AppendAlongsideStream 画、
                // 无轮时由 AppendWholeSlice 画,都不该在这里再补——判据多取半条反而会双画
                // (Ensure 同步 Add 后,posted 的落盘渲染没有去重)
                if (_driver.IsRunning)
                {
                    if (CurrentSession is { } session) _history.EnsureHandoffCard(session.History);
                }
                break;

            case ETurnNotice.HandoffFailed:
                _handoffWriting.Hide();
                Items.Add(new ErrorItem { Message = Loc.Text(LangKey.HandoffFailed) });
                break;

            case ETurnNotice.HandoffNothingToCompact:
                _handoffWriting.Hide();
                Items.Add(new ErrorItem
                    { Message = Loc.Text(LangKey.HandoffNothingToCompact) });
                break;

            case ETurnNotice.HandoffStarted:
                _handoffWriting.Show();
                break;
        }
    }

    /// <summary>
    /// 把本轮思考段的耗时写回历史并补存。落盘是追加式的，写回发生在行已上盘之后，
    /// 因此这里是一次全量重写——一轮一次，只在真有思考段时触发。
    /// 取消打断的那轮不走 Persisted，它的思考段本就没进历史，不盖。
    /// </summary>
    private void StampThinkingStats()
    {
        if (CurrentSession == null) return;
        if (ThinkingItem.StampLiveItems(Items) > 0) CurrentSession.Save();
    }

    //================= agent / 会话装配 =================

    /// <summary>
    /// 确保当前会话存在并已挂接其执行者，返回会话本体。
    /// 会话本体缺失（文件损坏）属于不可继续的状态，直接抛出由运行循环渲染为错误条目。
    /// </summary>
    private async Task<ChatSession> EnsureSessionAsync(string titleSeed, CancellationToken cancellationToken)
    {
        if (CurrentMeta == null)
        {
            _currentCharacter = CharacterManager.Instance.GetCharacterData(NewSessionCharacterId);
            // 空态建的会话形态随这一侧（ADR 0050）；普通对话形态不绑工作区
            ChatSession created = await _binder.CreateAsync(_currentCharacter, titleSeed,
                NewSessionIsAgentForm ? Workspace.Path : null, PermissionModeIndex, cancellationToken,
                SessionModel.TakeDraft(), NewSessionIsAgentForm);

            CurrentMeta = created.ToMeta();
            Title = CurrentMeta.Title;
            SessionModel.Refresh(); //首轮新建的会话（尤其懒建页）此前无元数据，面板据此现身
            OnPropertyChanged(nameof(SessionModelLabel));
            NotifyCharacterKindChanged();
            MemoryPanel = new ConversationMemoryViewData(created);
            ApplyMode();
            SessionsChanged?.Invoke();
            return created;
        }

        ChatSession session = await AttachAsync(CurrentMeta, cancellationToken)
                              ?? throw new InvalidOperationException(
                                  $"Session '{CurrentMeta.SessionId}' could not be loaded.");
        ApplyMode();
        return session;
    }

    /// <summary>装载会话并挂接执行者，工作目录与权限档取界面当前值</summary>
    /// <returns>会话本体；文件缺失或损坏为 null</returns>
    private Task<ChatSession?> AttachAsync(ChatSessionMeta meta, CancellationToken cancellationToken) =>
        _binder.AttachAsync(meta, Workspace.Path, PermissionModeIndex, cancellationToken);

    /// <summary>角色档位变了：四处可见性判据都挂在它身上（发图有没有退路也是按档位判的）</summary>
    private void NotifyCharacterKindChanged()
    {
        OnPropertyChanged(nameof(IsAgentSession));
        OnPropertyChanged(nameof(IsModeSwitchVisible));
        OnPropertyChanged(nameof(IsTodoListVisible));
        Tray.NotifyVisionStateChanged();
    }

    private void ApplyMode()
    {
        // 模式是装饰性状态:后台写入,失败由实现内部记日志
        if (CurrentRunner is { } runner) _ = runner.SetModeAsync(CurrentMode);
    }

    //================= 加载与回放 =================

    /// <summary>
    /// 本实例是否正显示在界面上（由页面壳在切换当前会话时维护）。
    ///
    /// 缓存里的实例不止一个：后台还在跑的那些也留着。装载一个长会话要占掉主线程几百毫秒，
    /// 而快速点会话列表时这些装载会叠在一起，表现为<b>整个列表都点不动</b>——
    /// 所以切走的那一份中途就停，欠账留到切回来再补（见 <see cref="LoadSessionAsync"/>）。
    /// </summary>
    public bool IsDisplayed
    {
        get => _isDisplayed;
        set
        {
            if (_isDisplayed == value) return;
            _isDisplayed = value;
            if (!value)
            {
                _pager.ScheduleBackgroundTrim();
                return;
            }

            if (_deferredLoad is not { } deferred) return;

            _deferredLoad = null;
            _ = LoadSessionAsync(deferred);
        }
    }

    /// <summary>
    /// 装载指定会话(null = 新会话空态)。
    ///
    /// <b>不再取消正在跑的轮次</b>：现在每个会话有自己的视图模型实例，切会话是换实例，
    /// 旧实例连着它那一轮留在页面壳的缓存里继续跑。因此走到这里的只有两种情形——
    /// 新实例的首次装载（没有轮次可打断），或就地改写后的重载（页面壳已确认它没在跑）。
    /// </summary>
    /// <param name="meta">会话元数据</param>
    public async Task LoadSessionAsync(ChatSessionMeta? meta)
    {
        int loadVersion = ++_loadVersion; //期间再次切换会话时,旧加载在每个悬挂点后自行放弃
        ClearStreamState();
        // 执行者归会话本体持有,切走不需要清理什么——旧会话的执行者随它的会话留在原处
        CurrentMeta = meta;
        Title = meta?.Title ?? string.Empty;
        _currentCharacter = meta == null ? null : CharacterManager.Instance.GetCharacterData(meta.CharacterId);
        // 本体在这里读进缓存,此后 CurrentSession 只查缓存(见 SessionManager.GetLoaded)
        ChatSession? loaded = meta == null ? null : SessionManager.Instance.Load(meta.SessionId);
        // 群壳这一份必须在第一个 await 之前就位:页面先调装载、再换绑实例,绑定在这之后立刻求值,
        // 晚一步右栏就先按单聊画出群壳的占位角色,再跳成群卡
        Group = loaded is { IsGroup: true } ? new GroupShellViewData(loaded, _messages) : null;
        if (Group != null) InputPlaceholderKey = LangKey.GroupInputTips; //群里是对全群说话,不是给谁派任务
        OnPropertyChanged(nameof(IsGroupMemberSession));
        OnPropertyChanged(nameof(IsPermissionEditable));
        OnPropertyChanged(nameof(PermissionTooltip));
        NotifyCharacterKindChanged();
        OnPropertyChanged(nameof(SessionModelLabel));
        SessionModel.Refresh();
        OnPropertyChanged(nameof(ActiveCharacterName));
        OnPropertyChanged(nameof(ActiveCharacterDescription));
        OnPropertyChanged(nameof(ActiveCharacterIcon));
        if (meta == null)
        {
            IsSessionLoading = false;
            MemoryPanel?.Detach();
            MemoryPanel = null;
            GroupMember = null;
            Usage.Refresh();
            // 空态也要刷一次能力面板。这里曾经直接返回,于是新会话在首轮发送之前
            // 整个「能力」页签一片空白——而恰恰是这个时候用户最需要知道
            // 「这个会话会自动连上什么、要占多少」。没有执行者时由面板自己预演一次装配,
            // 五档都报得出(见 ConversationCapabilityViewData.RefreshAsync)
            await RefreshCapabilitiesAsync();
            return;
        }

        IsSessionLoading = true;

        // 加载是读取会话状态,抑制变更处理器的写回——
        // 否则每次切入都会对刚加载的会话做一次同步全量 JSON 保存,还刷新 UpdatedAt 扰动列表排序
        _isLoadingSession = true;
        try
        {
            // 群成员显示群的那一份：他跟群走、不存副本
            Workspace.Path = GroupChatSessions.WorkspaceOf(meta);
            PermissionModeIndex = GroupChatSessions.PermissionOf(meta);
        }
        finally
        {
            _isLoadingSession = false;
        }

        // 每个悬挂点都要问一次:这次装载还算不算数(被更新的切换取代 / 已经不在界面上了)
        bool Abandoned()
        {
            if (loadVersion != _loadVersion) return true;
            if (IsDisplayed) return false;

            // 装载中途被切走:剩下的活没人看,而它照样跟新会话抢主线程——
            // 快速点会话列表时"整个列表都变迟钝"就有它一份。欠账记下,切回来再接着做
            _deferredLoad = meta;
            IsSessionLoading = false;
            return true;
        }

        // 分帧:先让"清空旧会话"渲染出去,再构建新会话,把一次长冻结拆成两段短的
        await UiDispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);
        if (Abandoned()) return;

        try
        {
            long attachBegin = StartupPhaseProbe.Begin();
            // 会话正被别处驱动时<b>不能挂接</b>:执行者的闸门被那一轮整轮占着
            // (HarnessCharacterRunner.RunAsync 持有 _gate),挂接会一直等到它跑完,
            // 表现是窗口一片空白。而外驱视图要的三样都不需要挂接——
            // 历史读盘、实时靠 HistoryAppended、插话走 TryInjectAsync(不碰闸门)。
            // 也不写回工作目录与权限档:那是正在跑的那一轮的配置,不该被观察者改掉
            bool externallyDriven = SessionManager.Instance.Running.IsBusy(meta.SessionId);
            ChatSession? body = externallyDriven
                ? SessionManager.Instance.Load(meta.SessionId)
                : await AttachAsync(meta, CancellationToken.None);
            // 墙上时间:里面有真正的 await(预连、装配),不等于 UI 线程被占这么久。
            // 与 ui-stall 的间隔对照才说明问题——两个数接近就说明它是在 UI 线程上同步跑的
            StartupPhaseProbe.End("conversation/attach-wall", attachBegin);
            if (Abandoned()) return;
            if (body == null)
            {
                Items.Add(new ErrorItem { Message = $"Session '{meta.SessionId}' could not be loaded." });
                return;
            }

            MemoryPanel?.Detach();
            MemoryPanel = new ConversationMemoryViewData(body);
            OnPropertyChanged(nameof(IsSubSession)); //会话换了,「交回主代理」的可见性跟着换
            OnPropertyChanged(nameof(CanCreateGroupFromHere));
            OnPropertyChanged(nameof(IsSenderSwitchVisible));
            // 成员这一份挂执行者的待发插话,留到挂接之后建:提前取 Runner 会把执行者的惰性创建挤进切会话的同步窗口
            GroupMember = body.IsGroupMember ? new GroupMemberSessionViewData(body, ApplyGroupPermission) : null;
            OnPropertyChanged(nameof(SessionIdShort)); //编号同理:装载之前 CurrentMeta 还是空的
            OnPropertyChanged(nameof(SessionIdFull));
            // 运行态同理,而且更要紧:装载之前 CurrentMeta 还是空的,绑定算出来的是"没在跑"。
            // 而外驱那一轮多半在窗口打开<b>之前</b>就开跑了,登记处的变更信号早发完了——
            // 不在这里补一次,子会话窗口会一直是闲着的样子:没有转圈、没有停止按钮,
            // 用户打的字还会走"发下一轮"而不是"插话"
            NotifyRunStateChanged();
            NotifyBusyChanged();
            //名下子代理的处境同理:它们多半在装载之前就成立了,登记处的信号早发完了
            NotifySubAgentStatusChanged();

            // 切回会话时恢复输入框草稿
            InputText = body.ComposerDraft;

            CurrentMode = await body.Runner.GetModeAsync();
            if (Abandoned()) return;

            // 信号一律挂上,不只外驱时:「别处改了这个会话的历史」还有另一条来路——
            // 子会话窗口点「交回主代理」会往<b>派活者</b>的历史里写,而派活者此刻很可能就闲着
            // 开在界面上。不挂的话那条报告要关掉会话再打开才看得见
            AttachSessionSignals(body);

            // 外驱时执行者未必绑好,历史一律从会话本体读(两者本就是同一份)
            ReplayMessages(externallyDriven ? body.History : body.Runner.GetHistory());

            // 切走时占位卡被清掉了;若压缩还在跑,切回来得重新挂上,否则会话流里毫无动静
            if (_driver.Busy == ETurnBusy.Compacting) _handoffWriting.Show();

            // 就在这里收尾,不能拖到下面两个 await 之后:视图靠这一步同步贴到底,
            // 而 await 会让出线程——中间那一帧会把列表按 offset 0(会话顶部)画出来,
            // 于是变成"先显示开头再跳到底部"。侧栏的 todo 与能力清单不属于消息列表,
            // 晚一点填上不影响列表已经就位这个事实
            if (loadVersion == _loadVersion) IsSessionLoading = false;

            // 侧栏(todo 与能力清单)与消息列表无关,不该挡在会话加载的关键路径上:
            // 实测两者串行占掉约 550ms,而此时列表已经显示出来了。
            // 两个方法各自带 try/catch,即发即忘不会漏掉异常
            _ = RefreshTodosAsync();
            _ = RefreshCapabilitiesAsync();
        }
        catch (Exception e)
        {
            Log.Warning($"Load session failed: {e.Message}");
            if (loadVersion == _loadVersion) Items.Add(new ErrorItem { Message = e.Message });
        }
        finally
        {
            // 被更新的切换取代时不动状态,由接手的那次加载收尾
            if (loadVersion == _loadVersion) IsSessionLoading = false;
        }
    }

    /// <summary>
    /// 历史消息回放:与实时流共用 ApplyContent 管道。
    /// 只渲染最近的<b>首屏</b>,凑够一窗的那几条由 <see cref="FillFirstWindow"/> 在界面可见之后补,
    /// 更早的由"加载更早"按批前插——非虚拟化列表靠数据开窗保住长会话性能
    /// </summary>
    private void ReplayMessages(IReadOnlyList<ChatMessage> messages)
    {
        long replayBegin = StartupPhaseProbe.Begin();
        // 有一轮正跑着的时候,历史末尾那次工具调用的结果多半正在路上(它是下一次服务调用的
        // 请求消息,随那次落盘,而实时流这就会把它送来)。按"历史里没有结果"收掉它就是谎报
        _pager.Replay(messages, CurrentSession?.LiveTurn.IsTurnRunning == true);

        // 会话累计用量从本体恢复(响应 usage 不随消息持久化)
        if (CurrentSession is { } session)
        {
            Usage.RestoreFrom(session);
        }

        Usage.Refresh();
        Search.NotifyHistoryChanged(); //换了会话:开着的搜索栏按原词重搜这一个
        StartupPhaseProbe.End($"conversation/replay:items={Items.Count},history={messages.Count}", replayBegin);
    }

    /// <summary>窗口之前还有没渲染的历史（顶部「加载更早」的可见性）</summary>
    public bool HasEarlierMessages => _pager.HasEarlierMessages;

    /// <summary>本会话是否已经往前续过一窗（决定「已到会话开头」那一行显不显示）</summary>
    public bool HasLoadedEarlier => _pager.HasLoadedEarlier;

    /// <summary>
    /// 向前扩展一窗历史。由视图层调用,滚动位置的保持由调用方负责
    /// </summary>
    /// <returns>真的前插了条目返回 true(调用方据此决定要不要补偿视口)</returns>
    public bool LoadEarlierMessages() => _pager.LoadEarlier();

    /// <summary>
    /// 把首屏补齐到整窗,界面贴底可见之后由视图层在空闲时调用,见 <see cref="ConversationHistoryPager.FillFirstWindow"/>
    /// </summary>
    /// <returns>真的补了条目返回 true</returns>
    public bool FillFirstWindow() => _pager.FillFirstWindow();

    /// <summary>窗口之后还有没渲染的历史：搜索截断重载到旧消息附近之后为真（「回到最新」的可见性）</summary>
    public bool HasLaterMessages => _pager.HasLaterMessages;

    /// <summary>从旧消息那段回到了最新（视图据此把视口贴回底部）</summary>
    public event Action? ReturnedToLatest;

    /// <summary>停在旧消息那段时往后续一批，由视图在滚到底时调用</summary>
    /// <returns>真的追加了条目返回 true</returns>
    public bool LoadLaterMessages() => _pager.LoadLater();

    [RelayCommand]
    private void ReturnToLatest() => ReturnToLatestIfDetached();

    private bool ReturnToLatestIfDetached() => _pager.ReturnToLatest();

    /// <summary>
    /// 会话此刻没人在跑：自己没跑也没在装配、登记处空闲、也没有别处的实时流。
    /// 对账与截断重载共用这一个判据——唤醒轮占位与直播置位之间有一瞬空窗，三者缺一不可
    /// </summary>
    private bool IsSessionIdle =>
        !IsGenerating && CurrentSession is { } session && !session.LiveTurn.IsTurnRunning &&
        !SessionManager.Instance.Running.IsBusy(session.SessionId);

    //================= IConversationItemActionHost =================
    // 显式实现:这五件事是给消息级操作用的,不该混进本类给界面绑定的公开面

    /// <inheritdoc />
    ChatSession? IConversationItemActionHost.Session => CurrentSession;

    /// <inheritdoc />
    bool IConversationItemActionHost.IsGenerating => IsGenerating;

    /// <inheritdoc />
    void IConversationItemActionHost.Rerun(ChatMessage? input)
    {
        ScrollToEnd = true;
        _ = _turns.RunAsync(input, input == null ? string.Empty : ConversationItemFactory.DisplayTextOf(input));
    }

    /// <inheritdoc />
    void IConversationItemActionHost.NotifySessionsChanged() => SessionsChanged?.Invoke();

    /// <inheritdoc />
    void IConversationItemActionHost.NotifyItemsWired() => OnPropertyChanged(nameof(CanRegenerate));

    /// <inheritdoc />
    void IConversationItemActionHost.NoteHistoryRemoved(IReadOnlyCollection<int> removedIndices)
    {
        _pager.NoteRemoved(removedIndices);
        Search.NotifyHistoryChanged();
    }

    //================= IAttachmentTrayHost =================

    ChatSession? IAttachmentTrayHost.Session => CurrentSession;

    // 发图退路按会话形态判（ADR 0050）：普通对话形态的会话不挂识图工具，图片照样会白发
    bool IAttachmentTrayHost.HasVisionFallback => VisionFallback.HasFallback(IsAgentSession, SessionCharacter.Tools);

    //================= IConversationTurnHost =================

    /// <inheritdoc />
    string? IConversationTurnHost.CurrentSessionId => CurrentMeta?.SessionId;

    /// <inheritdoc />
    ChatSession? IConversationTurnHost.CurrentSession => CurrentSession;

    /// <inheritdoc />
    Task<ChatSession> IConversationTurnHost.EnsureSessionAsync(string titleSeed, CancellationToken cancellationToken) =>
        EnsureSessionAsync(titleSeed, cancellationToken);

    /// <inheritdoc />
    void IConversationTurnHost.OnSessionEnsured() => Tray.FlushOwnedFiles();

    /// <inheritdoc />
    void IConversationTurnHost.NotifyPreparingChanged() => NotifyRunStateChanged();

    /// <inheritdoc />
    void IConversationTurnHost.ShowError(string message) => Items.Add(new ErrorItem { Message = message });

    //================= IConversationReconcileHost =================
    // 显式实现:对账要的只是这几个窄依赖,不该把整个视图模型暴露给对账器

    /// <inheritdoc />
    bool IConversationReconcileHost.IsSessionIdle => IsSessionIdle;

    /// <inheritdoc />
    bool IConversationReconcileHost.IsSessionLoading => IsSessionLoading;

    /// <inheritdoc />
    ChatSession? IConversationReconcileHost.CurrentSession => CurrentSession;

    /// <inheritdoc />
    void IConversationReconcileHost.NotifyWindowChanged() => _pager.NotifyWindowChanged();

    /// <inheritdoc />
    List<ConversationItemBase> IConversationReconcileHost.BuildItems(IReadOnlyList<ChatMessage> history,
        int from, int to) => _history.Build(history, from, to);

    /// <inheritdoc />
    void IConversationReconcileHost.WireStreamedSources(IReadOnlyList<ChatMessage> history)
    {
        _itemActions.WireStreamed(history);
        // 来源配好才认得出消息;旁观别人那一轮的思考卡此刻改读驱动方落盘的耗时
        ThinkingItem.AdoptPersistedStats(Items);
    }

    /// <inheritdoc />
    void IConversationReconcileHost.RefreshTokenUsage() => Usage.Refresh();

    /// <summary>
    /// 当前会话本体。只查已加载缓存而不走 <c>Load</c>：这里是高频路径（绑定 getter、事件回调），
    /// <c>Load</c> 顺带的冷历史卸载可能同步写盘。本体在装载 / 新建时已进缓存，会话被删后为 null
    /// </summary>
    private ChatSession? CurrentSession =>
        CurrentMeta == null ? null : SessionManager.Instance.GetLoaded(CurrentMeta.SessionId);

    /// <summary>当前会话的执行者(会话本体持有);无会话为 null</summary>
    private ICharacterRunner? CurrentRunner => CurrentSession?.Runner;

    /// <summary>
    /// 当前有效模型的上下文上限，用量分母与能力面板共用。每次现读：顶栏换模型不重建 agent，缓存就是过期的分母
    /// </summary>
    private int ContextLength() =>
        (CurrentSession?.ChatModelRunningData ?? LlmManager.Instance.CurrentRunningModel)?.ContextLength ?? 0;

    //================= 输入框 =================

    partial void OnInputTextChanged(string value)
    {
        // 同步到会话草稿(纯内存)。落盘时机交给宿主:切会话/切页/弃用时由页面壳调 SaveMeta
        if (CurrentSession is { } session) session.ComposerDraft = value;

        // 补全：/ 技能（整行）与群里的 @ 成员（光标前）。光标由视图另报，文本先到时按上次的光标算，
        // 越界（比如整段被替换）就当在末尾
        int caret = _composerCaret < 0 || _composerCaret > value.Length ? value.Length : _composerCaret;
        _ = Palette.RefreshAsync(value, caret);

        Usage.EstimateInput(value);
    }

    //================= 能力面板 =================

    /// <summary>
    /// 刷新右栏能力面板。挂接完成后调用——装配就在挂接里做，早于此调用拿到的是上一轮的工具集
    /// </summary>
    private async Task RefreshCapabilitiesAsync()
    {
        try
        {
            // 所有档都报固定开销:agent 报五档,普通对话只报角色提示词段(见
            // PreviewCapabilitiesAsync 对普通角色的处理)。以前这里对普通对话传 null,
            // 于是空态一片空白——而恰恰是发送前最该知道"这段对话固定占多少"
            await Capabilities.RefreshAsync(CurrentRunner, SessionCharacter, ContextLength(),
                Workspace.Path, PermissionModeIndex, IsAgentSession);
        }
        catch (Exception e)
        {
            Log.Warning($"Refresh capabilities failed: {e.Message}");
        }
    }

    //================= todo =================

    private async Task RefreshTodosAsync()
    {
        if (CurrentRunner is not { HasSession: true } runner) return;
        try
        {
            IReadOnlyList<TodoSnapshot> todos = await runner.GetTodosAsync();
            Todos.Clear();
            foreach (TodoSnapshot todo in todos)
            {
                Todos.Add(new TodoDisplayItem(todo));
            }

            HasTodos = Todos.Count > 0;
        }
        catch (Exception e)
        {
            Log.Warning($"Refresh todos failed: {e.Message}");
        }
    }

    //================= 切换清场 =================

    /// <summary>丢掉上一个会话留在界面上的一切(条目、侧栏、开窗与转录器状态)</summary>
    private void ClearStreamState()
    {
        // 气泡里的图是本会话现解出来的大位图,随条目走;条目被整体丢掉时没人会去释放它们,
        // 于是切一次会话就漏掉一整个会话的图。先 Clear 摘掉绑定,再释放(顺序反了会撞渲染)
        ConversationItemBase[] discarded = Items.ToArray();
        Items.Clear();
        foreach (ConversationItemBase item in discarded) item.ReleaseImages();
        // 整理中的占位卡随清空一起消失;若压缩还在跑,切回时由 LoadSessionAsync 重新挂上
        _handoffWriting.Forget();

        Todos.Clear();
        HasTodos = false;
        _pager.Reset();
        Interjections.Items.Clear(); //待发的插话归属于那个会话的注入队列,切走就不再显示
        _transcript.Reset();
        Usage.Ledger.Reset();
        // 记忆库面板与 token 文本不在此清空:切会话时先空后填会让工具行闪烁,
        // 由 LoadSessionAsync 在新值就绪时一次性替换
    }
}
