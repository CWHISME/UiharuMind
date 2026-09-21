/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
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
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Characters;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Diagnostics;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Conversation.Composer;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Features.Conversation.SidePanels;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 一次对话的视图模型，角色扮演与 agent 共用这一个实现。
/// 阶段 3 之后两者跑的是同一条路：session.Runner.RunAsync() → AIContent 流 → ApplyContent()，
/// 差异只剩"暴露哪些操作面板"(workspace / 权限档 / todo 侧栏 vs 角色卡 / 参数 / 翻译插件)，
/// 由角色的 ECharacterKind 控制显隐，因此不需要为此分出子类；
/// 原先的 ConversationViewModelBase 只有一个实现，已并入本类。
/// </summary>
public partial class ConversationViewModel : ViewModelBase, IConversationItemActionHost, IConversationReconcileHost, IDisposable
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
    [ObservableProperty] private bool _hasEarlierMessages;

    /// <summary>
    /// 本会话是否已经续过至少一窗更早的消息。只用来决定顶部那行「已到会话开头」显不显示——
    /// 短会话本来就没有更早的消息，一进来就挂那一行是噪音。
    /// </summary>
    [ObservableProperty] private bool _hasLoadedEarlier;

    [ObservableProperty] private bool _isSessionLoading; //会话切换构建中(空状态覆盖层此间不显示,避免闪烁)
    [ObservableProperty] private string _tokenUsageText = string.Empty; //token 统计(输入估算/本轮/会话累计)

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
        if (Palette.AcceptSkillCandidate()) return;

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
        if (Palette.AcceptSkillCandidate()) return;
        OnInputExtra();
    }

    /// <summary>
    /// 采纳补全候选。命令面板上那一个的转发——回车与 Tab 的改道点必须留在本类的
    /// 命令入口上（见 <see cref="SendMessage"/>），这个转发让调用方不必绕道面板
    /// </summary>
    /// <returns>是否采纳了候选</returns>
    public bool AcceptSkillCandidate() => Palette.AcceptSkillCandidate();

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
    public string NewSessionCharacterId { get; set; } = nameof(DefaultCharacter.WorkspaceAgent);

    /// <summary>
    /// 当前会话是否 agent 类型(决定工具行显示模式/权限还是发送身份)。
    /// 尚无会话时按页面的新建默认角色判定,agent 页的空会话也应显示 agent 工具
    /// </summary>
    public bool IsAgentSession => SessionCharacter.Kind.IsAgent();

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
    public string PermissionTooltip => ConversationModeLabels.PermissionTooltip(PermissionModeIndex);

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

    private CancellationTokenSource? _prepareCancellation; //会话装配阶段的取消源,此后由 TurnDriver 接手
    private bool _isPreparing; //正在装配会话(此时 TurnDriver 还没开始跑)
    private int _loadVersion; //会话加载版本号,用于放弃已被新切换取代的旧加载
    private bool _isDisplayed = true; //本实例是否正显示在界面上
    private ChatSessionMeta? _deferredLoad; //中途被切走而欠下的那次装载,切回来时接着做
    private bool _isLoadingSession; //加载会话期间抑制设置写回(加载是读,不是用户改动)
    private int _inputCountVersion; //输入估算版本号,后台计数只采纳最新一次
    private CancellationTokenSource? _tokenRefreshDebounce; //打字时合并刷新,避免每个字符都触发 ToolTip 重排
    private CancellationTokenSource? _usageRefreshDebounce; //流式期合并 UsageObserved 刷新,避免每个 chunk 都重排 ToolTip

    private readonly ConversationItemActions _itemActions; //气泡上的编辑/删除/分叉/重试
    private readonly ConversationSessionBinder _binder; //建/装会话并挂执行者
    private readonly ConversationTranscript _transcript; //实时流装配器,落点即 Items
    private readonly TurnDriver _driver; //一轮对话的编排,与定时任务共用同一份
    private ChatSession? _signalSession; //已挂上历史变更信号的会话
    private IDisposable? _sessionPin; //挂着期间钉住它的历史,不许被驻留策略卸掉
    private IDisposable? _liveObservation; //挂在会话实时内容流上的订阅(别人驱动那一轮时靠它逐 token)
    private readonly ITurnSink _liveObserverSink; //实时流的落点:同一个转录器,外面包一层 UI 线程 marshal
    private readonly HistoryWindow _historyWindow = new(); //历史渲染窗口
    private readonly ConversationItemWindowTrimmer _trimmer; //运行期把涨上来的条目裁回上限
    private readonly ConversationHistoryReconciler _reconciler; //落盘与界面对不上的兜底
    private readonly TurnUsageLedger _usage = new(); //token 账本

    /// <summary>上下文占用的悬停面板数据（进度条、压缩水位刻度与配色）</summary>
    public ContextUsageViewData ContextUsage { get; } = new();

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
    public bool IsGenerating => _isPreparing || _driver.IsRunning || IsExternallyDriven;

    /// <summary>此刻是否正在整理交接文档（压缩不是轮次，<c>IsGenerating</c> 不涵盖它）</summary>
    public bool IsCompacting => _driver.Busy == ETurnBusy.Compacting;

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

    /// <summary>
    /// 已投入注入队列、模型还没消费的插话。它们不在时间轴上——位置要等消费那一刻才定——
    /// 所以在输入区上方列出来，免得看着像没发出去。
    /// </summary>
    public ObservableCollection<PendingInterjectionViewData> PendingInterjections { get; } = new();

    /// <summary>
    /// 本会话这一轮是<b>别处</b>在驱动的（子代理跑着、定时任务无人值守跑着、
    /// 或者同一个会话在另一个界面壳里跑着）。
    ///
    /// 判据只能取运行态登记处：自己的 <see cref="_driver"/> 闲着并不代表会话空闲。
    /// 认错的后果不是显示不好看——用户打的字会走「发下一轮」而不是「插话」，
    /// 排进了队列却什么都不说（见 ADR 0021 的外驱条目）。
    /// </summary>
    /// <summary>
    /// 本会话是不是一个子会话（决定要不要显示「交回主代理」）。
    /// 会话是异步装载的，所以<b>装载完成时必须发一次变更通知</b>，
    /// 否则绑定停在初始的 false 上，那个按钮永远不出现
    /// </summary>
    public bool IsSubSession => CurrentSession?.IsSubSession == true;

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

    public bool IsExternallyDriven =>
        !_driver.IsRunning && SessionManager.Instance.Running.IsBusy(CurrentMeta?.SessionId);

    /// <summary>运行态指示点的配色键（status-dot 样式按 Tag 选色）</summary>
    /// <summary>
    /// 状态点配色键。三档而不是两档：本会话闲着、但名下还有后台子代理没交回报告，
    /// 既不是「在跑」也不是「空」——用户此刻要知道的正是这一档（见 CONTEXT.md「未了结的工作」）
    /// </summary>
    public string RunStatusKey => IsGenerating ? "Ready" : HasBackgroundWorkOnly ? "Progress" : "Idle";

    /// <summary>本会话这一轮没在跑，但名下还有后台子代理没交回报告</summary>
    public bool HasBackgroundWorkOnly => !IsGenerating && HasPendingWork;

    /// <summary>
    /// 名下有几个后台子代理卡在审批上等人点选。
    ///
    /// 这是<b>报警</b>，所以它的载体是输入区上方那条<b>位置固定</b>的横幅：滚多少轮都在、
    /// 点一下直达、没有在等的就整条消失。从前挂在工具卡上，而那张卡跑几十轮就滚没了
    /// ——通知又只停留一会儿。见 ADR 0025。
    /// </summary>
    public int ApprovalWaitingCount =>
        BackgroundSubAgentDispatcher.ApprovalWaiting(CurrentMeta?.SessionId).Count;

    /// <summary>有没有子代理在等审批（横幅据此显隐）</summary>
    public bool HasApprovalWaiting => ApprovalWaitingCount > 0;

    /// <summary>
    /// 输入区上方的<b>子代理状态</b>：等审批、在跑、跑完了压着等交回，各占一行。
    ///
    /// 三档同源同规则（见 <see cref="SubAgentStatusViewData"/>），所以合成一个列表由
    /// 界面照样画，而不是三段各写各的显隐。位置固定是它们共同的存在理由：
    /// 通知会飘走、工具卡跑几十轮就滚没了，而这条随时在。
    /// </summary>
    public ObservableCollection<SubAgentStatusViewData> SubAgentStatuses { get; } = new();

    /// <summary>
    /// 点开这一行对应的第一个子会话。
    ///
    /// 只开第一个而不是列出全部：用户要的是「马上看一眼/处理掉一个」，
    /// 处理完这一行自己会指向下一个。
    /// </summary>
    /// <param name="status">被点的那一行</param>
    [RelayCommand]
    private void OpenSubAgentStatus(SubAgentStatusViewData? status)
    {
        if (status is { Count: > 0 }) SubSessionWindowOpener.Open(status.SubSessionIds[0]);
    }

    /// <summary>
    /// 打开第一个在等审批的子会话。
    ///
    /// 只开第一个而不是列出全部：等审批是有时限的（到期按拒绝收口），
    /// 用户要的是「马上处理掉一个」，处理完横幅自己会指向下一个。
    /// </summary>
    [RelayCommand]
    private void OpenApprovalWaiting()
    {
        IReadOnlyList<string> waiting = BackgroundSubAgentDispatcher.ApprovalWaiting(CurrentMeta?.SessionId);
        if (waiting.Count > 0) SubSessionWindowOpener.Open(waiting[0]);
    }

    /// <summary>名下子代理的处境变了：横幅与那几个计数一起刷。只在 UI 线程上调</summary>
    private void NotifyApprovalWaitingChanged()
    {
        OnPropertyChanged(nameof(ApprovalWaitingCount));
        OnPropertyChanged(nameof(HasApprovalWaiting));
        RefreshSubAgentStatuses();
    }

    /// <summary>
    /// 重建状态行。整份重建而不是逐行增删：至多三行，而「哪一档有几个」是现取的快照，
    /// 比对着改反而要把同一份判据再写一遍
    /// </summary>
    /// <param name="force">内容没变也重建（换语言时文案要重算）</param>
    private void RefreshSubAgentStatuses(bool force = false)
    {
        List<SubAgentStatusViewData> fresh = SubAgentStatusViewData.Collect(CurrentMeta?.SessionId);
        if (fresh.Count == 0 && SubAgentStatuses.Count == 0) return;
        //一字未变就别动集合:每次运行态抖动都重建一遍会让那几行跟着闪
        if (!force && fresh.Count == SubAgentStatuses.Count &&
            !fresh.Where((x, i) => !x.Equals(SubAgentStatuses[i])).Any())
        {
            return;
        }

        SubAgentStatuses.Clear();
        foreach (SubAgentStatusViewData status in fresh) SubAgentStatuses.Add(status);
    }

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
    public string BusyLabel => Busy switch
    {
        ETurnBusy.ConnectingMcp => Loc.Text(LangKey.AgentMcpConnecting),
        ETurnBusy.Compacting => Loc.Text(LangKey.HandoffWriting),
        _ => string.Empty,
    };


    /// <summary>
    /// 发送按钮的文案。跑着的时候它是<b>插话</b>：消息进注入队列，agent 下一次机会消费。
    /// 那时按钮不能藏——藏了用户就只剩快捷键这一条路，而这正是"中途发不出消息"的由来。
    /// </summary>
    public string SendButtonText =>
        Loc.Text(IsGenerating ? LangKey.AgentInterject : LangKey.Send);

    public ConversationViewModel()
    {
        // 子模型只吃窄依赖、不反向持有本类:附件盘取会话要用委托(首轮发送时会话还不存在),
        // 命令面板要能改写输入框并读当前角色,挂接器只需报忙碌态
        Tray = new AttachmentTrayViewData(() => CurrentSession, () => SessionCharacter);
        Palette = new CommandPaletteViewData(text => InputText = text, () => SessionCharacter);
        _binder = new ConversationSessionBinder(NotifyBusyChanged);
        _itemActions = new ConversationItemActions(Items, this);
        _trimmer = new ConversationItemWindowTrimmer(Items, _historyWindow,
            () => CurrentRunner?.GetHistory() ?? [],
            // 不在界面上的实例没有会被抽走的视口,照裁——后台跑着的那个正是最该裁的
            () => !IsDisplayed || (IsStuckToBottomSource?.Invoke() ?? true));
        _reconciler = new ConversationHistoryReconciler(Items, _historyWindow, this);

        var agentSetting = AgentSettingConfig.Current;
        // 工作目录选择器要在最早构造:它持有那份状态,后面几处都从它读
        string? defaultWorkspace =
            !string.IsNullOrEmpty(agentSetting.DefaultWorkspacePath) &&
            Directory.Exists(agentSetting.DefaultWorkspacePath)
                ? agentSetting.DefaultWorkspacePath
                : null;
        Workspace = new WorkspacePickerViewData(defaultWorkspace, OnWorkspacePathChanged);
        SessionModel = new SessionModelViewData(() => CurrentMeta, () => _isLoadingSession, OnSessionModelChanged);
        SessionModel.Refresh();

        _transcript = new ConversationTranscript(Items, () => ConversationItemFactory.CreateAssistant(_currentCharacter),
            pattern => CurrentSession?.AddSessionApprovedShellPattern(pattern),
            () => CurrentSession?.WorkspacePath,
            createUserItem: CreateUserItem);
        // 用量不经转录器转发:运行侧看得见同一条内容流,由它记账并写回会话本体,
        // 这里只负责把数字刷到界面上(UsageObserved 通知)
        _transcript.HousekeepingToolCalled += () => _ = RefreshTodosAsync();
        _transcript.UserMessageRendered += OnUserMessageRendered;
        _transcript.ApprovalRequestCreated += OnApprovalRequestCreated;
        _transcript.SubSessionAttached += RefreshSubSessionApprovalWait;
        _transcript.MessageBoundaryReached += OnMessageBoundaryReached;
        // 登记与画卡在两个线程上各走各的,谁先都有可能——登记侧也喊一声,让已经画出来的卡回头认领
        SubSessionApprovalRegistry.Instance.PendingAdded += OnNestedApprovalsPending;
        _driver = new TurnDriver(_transcript, _usage, OnTurnNotice);
        // 观察别人驱动的那一轮时用它:内核仍是 _transcript,所以自己驱动时会被去重掉(见 LiveTurnStream)
        // 子会话才放行审批请求——嵌套审批的卡只该在子窗口弹,普通会话的观察窗弹出来也没人听
        _liveObserverSink = new LiveObserverSink(_transcript, AllowObservedApproval);
        _driver.StateChanged += OnDriverStateChanged;
        BackgroundSubAgentDispatcher.PendingWorkChanged += OnPendingWorkChanged;
        SessionManager.Instance.Running.StateChanged += OnSessionRunStateChanged;

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
        InputPlaceholder = Loc.Text(_inputPlaceholderKey);
    }

    /// <summary>
    /// 运行态或忙碌态变化。运行侧不认识绑定，属性变更由这里代它抛出。
    /// </summary>
    /// <summary>
    /// 运行态登记处变了。<b>可能来自后台线程</b>（无头执行与子代理都不在 UI 线程上），
    /// 所以 marshal 之后再动界面属性
    /// </summary>
    /// <param name="sessionId">状态变化的会话</param>
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

    private void OnSessionRunStateChanged(string sessionId)
    {
        // 别人的运行态也要看一眼:派出去的子会话卡在审批上时,派活那张卡要挂出提示
        if (sessionId != CurrentMeta?.SessionId)
        {
            RefreshSubSessionApprovalWait(sessionId);
            return;
        }

        // 本会话转空闲了：唤醒轮之类别处驱动的轮次到此应该已经把内容留在了历史里，
        // 还对不上就是实时通道漏了，对一次尾部（方法内部只在没人跑时动手）
        if (!SessionManager.Instance.Running.IsBusy(sessionId)) ReconcileHistoryTail("session idle");
        Dispatcher.UIThread.Post(() =>
        {
            NotifyRunStateChanged();
            NotifyBusyChanged(); //忙碌文案里有"别处正在跑"那一档,它跟着运行态变
        });
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

            if (ownTurn) AppendHandedBackReports(history, fromIndex);
            else if (streaming) AppendAlongsideStream(history, fromIndex);
            else AppendWholeSlice(history, fromIndex);

            if (!ownTurn) RefreshTokenUsageText(); //本轮的用量由 UsageObserved 逐块刷,这里重复一次只会抖
        });
    }

    /// <summary>
    /// 自己那一轮正跑着的时候落的盘：本轮的东西全由实时流渲染过了，这里<b>只补后续报告</b>。
    ///
    /// 它是唯一可能在本轮进行中从别处插进来的一类——子会话交回报告要等派活者空闲
    /// （见 <c>SubAgentReportHandoff</c>），而「登记处已空闲」与「本实例的 IsRunning 归零」
    /// 之间有一瞬的错位。整段丢掉的话那条报告就要等重开会话才看得见，用户看到的是「交回丢了」。
    /// 其余几类（检索卡、旁白、交接文档）本轮自有渲染路径，补在这里会画成两条。
    /// </summary>
    private void AppendHandedBackReports(IReadOnlyList<ChatMessage> history, int fromIndex)
    {
        for (int i = fromIndex; i < history.Count; i++)
        {
            if (ConversationMessageOrigin.KindOf(history[i]) != EHistoryItemKind.SubAgentReport) continue;

            foreach (ConversationItemBase item in BuildHistoryItems(history, i, i + 1, liveTail: true))
            {
                Items.Add(item);
            }
        }
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
        });
    }

    /// <summary>没有实时流时：整段照回放渲染</summary>
    private void AppendWholeSlice(IReadOnlyList<ChatMessage> history, int fromIndex)
    {
        foreach (ConversationItemBase item in BuildHistoryItems(history, fromIndex, history.Count, liveTail: true))
        {
            // 交接文档可能在落盘渲染入队之前已被别的路径画过(HandoffWritten 兜底、对账追加),
            // 按来源引用去重——否则同一条 note 会出两张卡
            if (item is HandoffItem && Items.Any(x => ReferenceEquals(x.SourceMessage, item.SourceMessage))) continue;
            Items.Add(item);
        }
    }

    /// <summary>
    /// 一轮正往界面流内容时落的盘：<b>内容流产出的那几类已经渲染过了</b>（助手正文、思考段、
    /// 工具卡、被消费的用户消息），这里只补它产不出的（检索卡、旁白、交接文档、后续报告），
    /// 再把流式条目与消息配对。归属判据只有一份（<see cref="ConversationMessageOrigin"/>），两条路都问它。
    /// </summary>
    private void AppendAlongsideStream(IReadOnlyList<ChatMessage> history, int fromIndex)
    {
        for (int i = fromIndex; i < history.Count; i++)
        {
            ChatMessage message = history[i];
            if (ConversationMessageOrigin.IsProducedByContentStream(ConversationMessageOrigin.KindOf(message)))
                continue;

            foreach (ConversationItemBase item in BuildHistoryItems(history, i, i + 1, liveTail: true))
            {
                // 同上:通知/兜底可能先画过交接文档,posted 的渲染不能再来一张
                if (item is HandoffItem && Items.Any(x => ReferenceEquals(x.SourceMessage, item.SourceMessage))) continue;
                Items.Add(item);
            }
        }

        // 流式条目此刻才能与落了盘的消息配对,配上了才有编辑/删除/分叉
        _itemActions.WireStreamed(history);
    }

    /// <summary>
    /// 用户消息 → 已接好来源的气泡。回放与实时（<see cref="UserMessageContent"/>）共用这一份，
    /// 两边因此对同一条消息画出同一个样子；框架注入的、空白无图的不画。
    /// </summary>
    /// <param name="message">用户消息</param>
    /// <returns>气泡；这条不该显示则为 null</returns>
    private TextConversationItem? CreateUserItem(ChatMessage message)
    {
        string text = ConversationItemFactory.DisplayTextOf(message);
        if (ConversationItemFactory.IsFrameworkInjected(message)) return null;
        if (string.IsNullOrWhiteSpace(text) && !ConversationItemFactory.HasImage(message)) return null;

        return _itemActions.Wire(ConversationItemFactory.CreateUser(text, message), message);
    }

    /// <summary>插的那句话被模型消费、画进时间轴了：待发提示撤掉</summary>
    private void OnUserMessageRendered(ChatMessage message)
    {
        for (int i = PendingInterjections.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(PendingInterjections[i].Message, message)) PendingInterjections.RemoveAt(i);
        }
    }

    /// <summary>
    /// 画出了一张审批卡：子会话窗口认领嵌套审批，把卡的回应接到登记项上。
    /// 父会话自己的卡认不到登记（没人登记过），原样走父轮次的回应口。
    /// </summary>
    private void OnApprovalRequestCreated(ApprovalRequestItem item)
    {
        if (CurrentSession is not { IsSubSession: true } session) return;
        SubSessionApprovalRegistry.Instance.TryAdopt(session.SessionId, item.Request, item.Response);
    }

    /// <summary>
    /// 有嵌套审批登记进来了：把本窗口已经画出来的待决卡片再认领一遍。
    ///
    /// 认领两头都要做——卡片可能先于登记诞生（内容流转发到界面是 Post 出去的），
    /// 也可能后于登记诞生（晚开的窗口从流回放里拿到同一批请求）。重复认领无害：
    /// 决定先到先得。<b>可能来自后台线程</b>，所以 marshal 之后再动界面。
    /// </summary>
    /// <param name="sessionId">登记进来的那个子会话</param>
    private void OnNestedApprovalsPending(string sessionId)
    {
        if (sessionId != CurrentMeta?.SessionId) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (CurrentSession is not { IsSubSession: true } session) return;
            foreach (ApprovalRequestItem item in _transcript.PendingApprovals.ToList())
            {
                SubSessionApprovalRegistry.Instance.TryAdopt(session.SessionId, item.Request, item.Response);
            }
        });
    }

    /// <summary>
    /// 刷新「派出去的子会话正在等审批」提示。状态取自运行态登记处——
    /// 子代理那一轮的审批等待本来就登记在册（<c>TurnDriver</c> 的 <c>BeginApprovalWait</c>），
    /// 不必另铺一条通知链路。
    /// </summary>
    /// <param name="subSessionId">子会话标识</param>
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
            NotifyApprovalWaitingChanged();
        });
    }

    /// <summary>
    /// 撤掉一条待发的插话：提示条先拿掉（不管队列里撤没撤成），再从注入队列里摘走——
    /// 撤不回来（已被模型消费）的那条此刻已经画进时间轴，提示条本也会由
    /// <see cref="OnUserMessageRendered"/> 撤，这里幂等
    /// </summary>
    [RelayCommand]
    private async Task RemoveInterjection(ChatMessage? message)
    {
        if (message == null) return;

        for (int i = PendingInterjections.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(PendingInterjections[i].Message, message))
            {
                PendingInterjections.RemoveAt(i);
                break;
            }
        }

        try
        {
            if (CurrentRunner is { } runner) await runner.CancelInjectionsAsync(new[] { message });
        }
        catch (Exception e)
        {
            // 队列访问反射失败等:界面提示已撤,模型之后仍可能收到这句——最低限度是不能再让 UI 崩
            Log.Warning($"撤销插话失败: {e.Message}");
        }
    }

    /// <summary>
    /// 一次性地把待发的插话全部撤掉（停止/一轮结束时的收尾）。
    /// 只清界面提示、不动队列的旧行为，就是「停止之后插话还遗留在那」的由来；
    /// 队列里那些不撤走，下次再跑会被模型突然消费，连提示都没有就冒出来。
    /// </summary>
    private async Task CancelPendingInterjectionsAsync()
    {
        if (PendingInterjections.Count == 0) return;
        ChatMessage[] messages = PendingInterjections.Select(x => x.Message).ToArray();
        PendingInterjections.Clear();
        try
        {
            if (CurrentRunner is { } runner) await runner.CancelInjectionsAsync(messages);
        }
        catch (Exception e)
        {
            Log.Warning($"撤销待发插话失败: {e.Message}");
        }
    }

    /// <summary>
    /// 历史里的某一条被别处原地换掉了（后续报告替换了上一份）。
    ///
    /// <b>只重建那一条产出的条目</b>，不整份回放：markdown 是按条目、进视口才逐帧启用渲染器的
    /// （见 <c>SimpleMarkdownViewer</c>），清空重建等于让满屏气泡一起退回纯文本再一条条转回来
    /// ——用户看到的就是整个窗口闪一下。
    /// </summary>
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

            // 旧那条产出的条目可能不止一个(工具卡、思考卡…),按来源整组认出来
            List<int> slots = new();
            for (int i = 0; i < Items.Count; i++)
            {
                if (ReferenceEquals(Items[i].SourceMessage, replaced)) slots.Add(i);
            }

            // 那一条落在历史开窗之外(没渲染过),此刻也不该凭空补出来
            if (slots.Count == 0) return;

            List<ConversationItemBase> rebuilt = BuildHistoryItems(history, index, index + 1);

            // 后续报告就是一条文本:能原地改就别动集合。摘掉再插回去会重建那一处的
            // markdown 渲染器(它按条目、进视口才启用),内容一字没变也要闪一下
            if (slots.Count == 1 && rebuilt is [TextConversationItem fresh] &&
                Items[slots[0]] is TextConversationItem existing)
            {
                fresh.Flush();
                existing.Message = fresh.Message;
                existing.Timestamp = fresh.Timestamp;
                _itemActions.Wire(existing, history[index]); //来源换人了,编辑/删除得指向新那条
                return;
            }

            for (int i = slots.Count - 1; i >= 0; i--)
            {
                Items.RemoveAt(slots[i]);
            }

            for (int i = 0; i < rebuilt.Count; i++)
            {
                Items.Insert(slots[0] + i, rebuilt[i]);
            }
        });
    }

    /// <summary>
    /// 观察别人驱动的那一轮时，审批卡放不放行。
    ///
    /// 两种情形要放：子会话窗口（嵌套审批，见 ADR 0021），以及本会话的<b>唤醒轮</b>
    /// ——那一轮由后台驱动，回应口就登记在本壳上（见 <see cref="WakeApprovalHosts"/>）。
    /// 其余普通会话的观察窗照旧丢弃：那张卡画出来也按不动，还会一直挂在待决清单上。
    /// </summary>
    private bool AllowObservedApproval()
    {
        ChatSession? session = CurrentSession;
        if (session == null) return false;
        return session.IsSubSession || WakeApprovalHosts.HasHost(session.SessionId);
    }

    /// <summary>挂上「别处改了这个会话的历史」的两个信号。重复挂接先摘再挂，不攒订阅</summary>
    private void AttachSessionSignals(ChatSession session)
    {
        DetachSessionSignals();
        _signalSession = session;
        // 钉住它的历史:每个气泡都指着历史里的某一条消息实例,历史被卸掉重载之后
        // 那些引用全部认不回来,编辑/删除/分叉/重试会静默失效(见 SessionResidencyPolicy)
        _sessionPin = SessionManager.Instance.Pin(session.SessionId);
        // 后台子代理跑完起的那一轮,审批只有本壳接得住(见 WakeApprovalHosts)
        WakeApprovalHosts.Register(session.SessionId, ResolveApprovalsAsync);
        session.HistoryAppended += OnSessionHistoryAppended;
        session.HistoryMessageReplaced += OnSessionHistoryReplaced;
        // 挂上这个会话的实时内容流。自己驱动时按身份去重,不会渲染两遍;
        // 这一轮跑到一半才挂上来也补得齐(尚未落盘的那一段会当场补发)
        _liveObservation = session.LiveTurn.Observe(_liveObserverSink, _transcript);
        session.LiveTurn.TurnEnded += OnObservedTurnEnded;
    }

    /// <summary>摘掉订阅。会话比本视图活得久，不摘就是一路泄漏到已销毁的视图上</summary>
    private void DetachSessionSignals()
    {
        if (_signalSession is not { } previous) return;
        WakeApprovalHosts.Unregister(previous.SessionId, ResolveApprovalsAsync);
        previous.HistoryAppended -= OnSessionHistoryAppended;
        previous.HistoryMessageReplaced -= OnSessionHistoryReplaced;
        previous.LiveTurn.TurnEnded -= OnObservedTurnEnded;
        _liveObservation?.Dispose();
        _liveObservation = null;
        _sessionPin?.Dispose();
        _sessionPin = null;
        _signalSession = null;
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
            NotifyApprovalWaitingChanged();
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
        RefreshTokenUsageText();
    }

    /// <summary>本会话的覆写变了，有效模型与用量分母都跟着变，走与全局切换同一套刷新</summary>
    private void OnSessionModelChanged()
    {
        OnPropertyChanged(nameof(SessionModelLabel));
        Tray.NotifyVisionStateChanged();
        RefreshTokenUsageText();
    }

    private void OnLanguageChanged()
    {
        InputPlaceholder = Loc.Text(_inputPlaceholderKey);
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(ModeTooltip));
        OnPropertyChanged(nameof(PermissionTooltip));
        OnPropertyChanged(nameof(SenderTooltip));
        RefreshTokenUsageText(); //压缩水位那句提示是在 C# 里拼的,不会自己跟着语言变
        RefreshSubAgentStatuses(force: true); //同上:那几行的文案也是取一次存一次
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
        _transcript.ApprovalRequestCreated -= OnApprovalRequestCreated;
        _transcript.SubSessionAttached -= RefreshSubSessionApprovalWait;
        _transcript.MessageBoundaryReached -= OnMessageBoundaryReached;
        SubSessionApprovalRegistry.Instance.PendingAdded -= OnNestedApprovalsPending;
        _driver.StateChanged -= OnDriverStateChanged;
        BackgroundSubAgentDispatcher.PendingWorkChanged -= OnPendingWorkChanged;
        SessionManager.Instance.Running.StateChanged -= OnSessionRunStateChanged;
        DetachSessionSignals();
        // 执行者归会话所有、比本视图活得久,回调不摘就是一路泄漏到已销毁的视图上
        if (CurrentRunner is { } runner) runner.BusyChanged = null;
        _prepareCancellation?.Cancel();
        _tokenRefreshDebounce?.Cancel();
        _usageRefreshDebounce?.Cancel();
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
    /// 重新生成最后一条回复:等价于对最后一条可重试的用户消息执行重试
    /// </summary>
    [RelayCommand]
    private void RegenerateLast()
    {
        if (IsGenerating) return;
        ConversationItemBase? target = Items.LastOrDefault(x => x.CanRetry);
        if (target != null) _itemActions.Retry(target);
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
        CharacterDraft draft = CharacterDraft.ForEdit(ActiveCharacter);
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
            SessionManager.Instance.Load(CurrentMeta.SessionId)?.ChangeCharacter(character);
            ConversationSessionBinder.PersistSettings(CurrentMeta);
        }

        _currentCharacter = character;
        OnPropertyChanged(nameof(ActiveCharacterName));
        OnPropertyChanged(nameof(ActiveCharacterDescription));
        OnPropertyChanged(nameof(ActiveCharacterIcon));
        NotifyCharacterKindChanged();
        SessionsChanged?.Invoke(); //会话列表里的角色头像/名字跟着变
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
    }

    /// 换了工作区:先把该项目的授权要到手,再无条件刷一次面板。
    /// 刷新不能只在"有待确认项"时做——项目级名单本身跟着工作区变,
    /// 换到一个没有 .mcp.json 的目录时,上一个项目那几条必须从预告区消失
    private async Task OnWorkspaceChangedAsync(string? workspacePath)
    {
        await PromptWorkspaceMcpApprovalAsync(workspacePath);
        await RefreshCapabilitiesAsync();
    }

    /// <summary>
    /// 选定工作区时，就地为该项目的 <c>.mcp.json</c> 要一次安全确认。
    ///
    /// <b>时机定在这一刻而不是首轮发送时</b>，理由是它同时解决三件事：
    /// 用户当场知道这个项目会连上什么、确认不会在发送后突然弹出来打断，
    /// 而最要紧的是——这一刻<b>早于任何子进程启动</b>。
    ///
    /// 弹窗由 App 层主动发起（Core 只提供"查待确认 / 记授权"两个被动 API）：
    /// 抛全局事件的做法这个仓库已经踩过，预连提示曾因此点亮到一个跟 MCP 毫无关系的会话上。
    ///
    /// 确认是「全部允许」这一档，但<b>记录仍逐条落</b>（每条各记自己的可执行面指纹）——
    /// 于是下次仓库新增第四个 server 时，弹窗只说新增的那一条，而不是把四条重新摆一遍。
    /// 后者会养出"看第三次就直接点确认"的习惯，而确认疲劳就是这类机制实际失效的方式。
    /// </summary>
    /// <param name="workspacePath">刚选定的工作区；空表示解绑，无事可做</param>
    private async Task PromptWorkspaceMcpApprovalAsync(string? workspacePath)
    {
        if (string.IsNullOrEmpty(workspacePath)) return;

        try
        {
            List<McpApprovalRequest> pending = McpManager.Instance.GetPendingApprovals(workspacePath);
            if (pending.Count == 0) return;

            IMessageService messageService = App.Services.GetRequiredService<IMessageService>();
            if (!await messageService.ConfirmAsync(BuildMcpApprovalMessage(workspacePath, pending),
                    Loc.Text(LangKey.AgentMcpApprovalTitle)))
            {
                // 拒绝不落任何记录:下次再进这个工作区会再问一次。
                // 记一条"拒绝过"看着更省事,但那会让"我当时点错了"没有回头路,
                // 而这一问的成本只是一个弹窗
                return;
            }

            McpManager.Instance.ApproveWorkspaceServers(workspacePath);
        }
        catch (Exception e)
        {
            Log.Warning($"Prompt workspace MCP approval failed: {e.Message}");
        }
    }

    /// 确认框正文:名字、将执行的命令原文,以及"这一条是被改过的"那个标记。
    /// 命令必须逐字摆出来——用户批的是这条命令,不是这个名字
    private static string BuildMcpApprovalMessage(string workspacePath, List<McpApprovalRequest> pending)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string changedMark = loc.GetString("AgentMcpApprovalChangedMark");
        StringBuilder list = new();
        foreach (McpApprovalRequest request in pending)
        {
            list.Append("• ").Append(request.Name).Append(":  ").Append(request.CommandLine);
            if (request.IsChanged) list.Append(changedMark);
            list.Append('\n');
        }

        return string.Format(loc.GetString("AgentMcpApprovalBody"),
            WorkspaceDisplay.NameOf(workspacePath), pending.Count, list.ToString());
    }

    //================= 发送与运行循环 =================

    private async Task SendCoreAsync(string text)
    {
        // 手动压缩:任务的自然边界由你比水位更清楚,在边界上压缩,交接文档质量高得多。
        // 命令后跟的文字作为额外指示随写文档的请求一起交给模型(见 TryParseCompact)
        if (CommandPaletteViewData.TryParseCompact(text, out string? compactExtra))
        {
            if (IsGenerating)
            {
                // 运行中命令没处安放:把字还给输入框并明说,静默吞掉就是"点了没反应"
                InputText = text;
                App.Services.GetRequiredService<IMessageService>().ShowNotification(
                    Loc.Text(LangKey.CompactWhileRunning), severity: MessageSeverity.Warning);
                return;
            }

            if (CurrentSession is not { } current)
            {
                // 空会话(新建未发首轮):没有可压缩的内容,明说而不是静默吞掉
                App.Services.GetRequiredService<IMessageService>().ShowNotification(
                    Loc.Text(LangKey.HandoffNothingToCompact), severity: MessageSeverity.Information);
                return;
            }

            // 用全局事实而非本地 Busy:同会话可能被另一窗口/外驱压缩,本地 driver 看不到
            if (TurnDriver.IsCompacting(current.SessionId))
            {
                // 正在整理时再敲一次:防重复写两份交接文档、出两张卡
                App.Services.GetRequiredService<IMessageService>().ShowNotification(
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
            ChatMessage? interjection = CurrentSession?.CreateMessage(ChatRole.User, text);
            if (interjection != null && CurrentRunner is { } runner && await runner.TryInjectAsync(new[] { interjection }))
            {
                PendingInterjections.Add(new PendingInterjectionViewData(interjection, text));
                return;
            }

            // 排不进去(执行者还在装配、或这个执行者不支持注入):把字还给输入框并明说,
            // 静默吞掉就是"点了没反应"
            InputText = text;
            App.Services.GetRequiredService<IMessageService>().ShowNotification(
                Loc.Text(LangKey.AgentInterjectUnavailable), severity: MessageSeverity.Warning);
            return;
        }

        // 以角色身份发送:直接写入一条回复,不触发生成
        if (SenderMode == SendMode.Assistant)
        {
            await AppendAssistantMessageAsync(text);
            return;
        }

        List<ConversationAttachment>? attachments = Tray.TakePending();
        Palette.CloseSkillPicker();

        // 点名调用:/技能名 [参数]。技能正文直接进本轮并常驻历史,气泡只显示用户敲的那一行。
        // 见 docs/adr/0001——框架的 load_skill 取不到退出模型自选的技能,所以不走它
        SkillInvocation? invocation = await Palette.TryBuildSkillInvocationAsync(text);

        ChatMessage userMessage = Tray.BuildUserMessage(invocation?.InjectedText ?? text, attachments);
        if (invocation != null) NamedSkillAnnotations.Mark(userMessage, invocation, text);

        // 乐观显示的气泡此刻就接上来源:轮首流里会再来一次同一个实例(UserMessageContent),
        // 转录器按引用认出它才不会画第二遍;副本落盘时由 WireStreamed 换成历史里那一条
        Items.Add(_itemActions.Wire(ConversationItemFactory.CreateUser(text, userMessage, attachments), userMessage));
        ScrollToEnd = true;
        await RunTurnAsync(userMessage, text);
    }

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
        _prepareCancellation?.Cancel(); //还卡在装配阶段时也要停得下来
        _driver.Cancel();
        // 外驱时要停的是别处那一轮——自己的 driver 根本没在跑。
        // 停止按钮既然显示出来了就必须真能停,否则是个骗人的按钮
        if (IsExternallyDriven) TurnDriver.CancelSession(CurrentMeta?.SessionId);
        _transcript.CancelPendingApprovals();
        _ = CancelPendingInterjectionsAsync(); //停止后待发的插话不该还挂在输入区,也从队列撤掉
    }

    /// <summary>
    /// 审批回应：等用户对本轮每个请求做出决定，回应即下一轮的输入。
    ///
    /// <b>按请求对象相认</b>，不是把转录器攒着的整批抽干：同一个会话上可能同时有两轮在跑
    /// （用户那一轮与后台委派回来时起的<b>唤醒轮</b>共用这一个转录器），抽干会领走别人那一轮的卡片，
    /// 让那一轮的工具调用永远没有结果地留在历史里。
    /// </summary>
    /// <param name="requests">本轮新增的审批请求</param>
    /// <returns>回应消息</returns>
    private async Task<IReadOnlyList<ChatMessage>> ResolveApprovalsAsync(
        IReadOnlyList<ToolApprovalRequestContent> requests)
    {
        IReadOnlyList<ApprovalRequestItem> turnApprovals = _transcript.TakeRoundApprovals(requests);
        List<ChatMessage> responses = new(turnApprovals.Count);
        foreach (ApprovalRequestItem approval in turnApprovals)
        {
            responses.Add(await approval.Response);
        }

        _transcript.ResolveApprovals(turnApprovals);
        return responses;
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
        bool trimmed = IsDisplayed ? _trimmer.TrimIfNeeded() : _trimmer.TrimToBackgroundBudget();
        if (trimmed) HasEarlierMessages = _historyWindow.HasEarlier;
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
                bool trimmed = IsDisplayed ? _trimmer.TrimIfNeeded() : _trimmer.TrimToBackgroundBudget();
                if (trimmed) HasEarlierMessages = _historyWindow.HasEarlier;
                break;

            case ETurnNotice.Ended:
                _transcript.ResolveApprovals(_transcript.PendingApprovals.ToList());
                SessionsChanged?.Invoke();
                // 流式期间的 UsageObserved 走防抖合并,停流后立刻补刷最终值,
                // 不能等下一个事件或防抖窗口——否则最后一个数要拖 250ms 才上屏
                DebouncedRefreshTokenUsage(force: true);
                // 这轮结束还没消费的插话不会再有机会被这轮消费,留着只会让下一轮莫名收到旧话
                _ = CancelPendingInterjectionsAsync();
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
                DebouncedRefreshTokenUsage();
                break;

            case ETurnNotice.KnowledgeRetrieved:
                // 落盘那份由 SessionChatHistoryProvider 在轮末插进历史,这里只管本轮即时可见;
                // 两者内容同源,重载会话后由回放分支再造出同一张卡
                Items.Add(ConversationItemFactory.CreateKnowledgeCard(notice.Payload ?? string.Empty));
                break;

            case ETurnNotice.HandoffWritten:
                RemoveHandoffWritingItem();
                // 卡片默认由落盘/回放路径渲染(BuildHistoryItems 的 HandoffNote 分支),这里只收掉占位;
                // 通知自己再 Add 一张会与落盘渲染各画一遍,同一条交接文档就出两张卡。
                // 只在自己那一轮正跑时补画:落盘路径走 AppendHandedBackReports 不画交接文档,
                // 只有通知这一条路。外部驱动者的轮(streaming)由 AppendAlongsideStream 画、
                // 无轮时由 AppendWholeSlice 画,都不该在这里再补——判据多取半条反而会双画
                // (Ensure 同步 Add 后,posted 的落盘渲染没有去重)
                if (_driver.IsRunning)
                {
                    EnsureHandoffCardRendered();
                }
                break;

            case ETurnNotice.HandoffFailed:
                RemoveHandoffWritingItem();
                Items.Add(new ErrorItem { Message = Loc.Text(LangKey.HandoffFailed) });
                break;

            case ETurnNotice.HandoffNothingToCompact:
                RemoveHandoffWritingItem();
                Items.Add(new ErrorItem
                    { Message = Loc.Text(LangKey.HandoffNothingToCompact) });
                break;

            case ETurnNotice.HandoffStarted:
                InsertHandoffWritingItem();
                break;
        }
    }

    /// <summary>正在整理交接文档的会话内占位卡(整理是多一次模型请求,会话流里不能毫无动静)</summary>
    private HandoffWritingItem? _handoffWritingItem;

    private void InsertHandoffWritingItem()
    {
        if (_handoffWritingItem != null) return; //事件是串行的,同一次整理不会重复挂
        var item = new HandoffWritingItem { Message = Loc.Text(LangKey.HandoffWriting) };
        _handoffWritingItem = item;
        Items.Add(item);
        ScrollToEnd = true;
    }

    private void RemoveHandoffWritingItem()
    {
        if (_handoffWritingItem is not { } item) return;
        _handoffWritingItem = null;
        if (Items.Remove(item)) ScrollToEnd = true;
    }

    /// <summary>
    /// 并发场景的交接卡兜底:压缩期间用户发了新消息,落盘路径不画交接文档,
    /// 只有这里补画。与 BuildHistoryItems 的 HandoffNote 分支同源,不另写一份渲染逻辑。
    /// </summary>
    private void EnsureHandoffCardRendered()
    {
        if (CurrentSession is not { } session) return;
        int index = HistoryHandoff.SupplyStartIndex(session.History);
        if (index < 0 || index >= session.History.Count) return;
        ChatMessage note = session.History[index];
        if (!HistoryHandoff.IsNote(note)) return; //没有交接文档时的兜底:SupplyStartIndex 无 note 返回 0
        if (Items.Any(x => ReferenceEquals(x.SourceMessage, note))) return; //已经画过就不再画
        foreach (ConversationItemBase item in BuildHistoryItems(session.History, index, index + 1, liveTail: true))
        {
            Items.Add(item);
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

    /// <summary>
    /// 跑一轮：先把会话装配好，再交给运行侧。
    ///
    /// 装配阶段单独持一个取消源——那时 <see cref="TurnDriver"/> 还没接手，
    /// 而它耗时（要建会话、装配 agent），用户在这期间按停止必须停得下来。
    /// </summary>
    /// <param name="userMessage">用户消息</param>
    /// <param name="titleSeed">新建会话时用来取标题的原文</param>
    private async Task RunTurnAsync(ChatMessage userMessage, string titleSeed)
    {
        _isPreparing = true;
        NotifyRunStateChanged();
        _prepareCancellation = new CancellationTokenSource();
        ChatSession? session = null; //提到 try 外:装配被停时还要靠它把 userMessage 补回历史
        try
        {
            // 装配阶段也登记成「在跑」:重建 agent 要拉 MCP 工具、可能好几秒,
            // 这期间不能让删除/清空去动它的文件,而那一轮随后照样会往里写。
            // 新会话此刻还没有标识,BeginRun(null) 按设计是空操作
            // 这一份**一直持有到本轮结束**,不在装配结束时放掉。从前是装配一段、运行一段两个作用域,
            // 注释写着「两段之间没有空窗」——单线程看确实没有,但登记处的锁在两段之间放开了,
            // 别的线程(后台委派回来时起的唤醒轮)正好能在这里挤进来抢到会话,两轮就重叠了。
            // 登记是引用计数的,与 TurnDriver 自己那一次叠加无害
            using IDisposable running = SessionManager.Instance.Running.BeginRun(CurrentMeta?.SessionId);
            session = await EnsureSessionAsync(titleSeed, _prepareCancellation.Token);
            Tray.FlushOwnedFiles();

            // 子会话与后台轮共用同一把串行闸（后台那轮整轮持有：跑+交回）：用户在子会话窗口
            // 直发必须等后台那一轮结束，否则两轮在 runner 释放/重建上重叠——后台轮 finally 释放
            // 旧实例，此刻正在 Attach/Run 的这一轮会拿到没挂接的新 runner（实机「尚未挂接会话」）。
            // 主会话不过闸（它的后台轮另走 TryBeginRun 抢占）。
            using IDisposable? turnGate = await BackgroundSubAgentDispatcher
                .EnterSubSessionTurnGateAsync(session.SessionId).ConfigureAwait(false);

            await _driver.RunAsync(session, session.Runner, userMessage, ResolveApprovalsAsync);
        }
        catch (OperationCanceledException)
        {
            // 装配阶段就被停掉:TurnDriver 还没接手,userMessage 从未交给框架,历史里自然也没有它。
            // 发送方(发送/重试)已经把「这条消息必须在历史里」当成 RunAsync 的责任,责任悬空就丢消息
            // (重试最典型:Retry 先删后跑,这里不补回,切走/重开会话那条输入就没了)
            RestoreUserMessageOnAbort(session ?? CurrentSession, userMessage);
        }
        catch (Exception e)
        {
            Log.Error($"Ensure session failed: {e}");
            Items.Add(new ErrorItem { Message = e.Message });
        }
        finally
        {
            _isPreparing = false;
            _prepareCancellation = null;
            NotifyRunStateChanged();
        }
    }

    /// <summary>
    /// 装配阶段被取消时把 userMessage 补回历史,避免「发送/重试后立刻停止」丢消息。
    /// 正常轮次的取消由 <see cref="TurnDriver"/> 的 <c>SettleInterruptedTurn</c> 收尾,
    /// 这里只兜它接手之前的那段空窗——那时 userMessage 还没交给框架,没有人会写它。
    /// </summary>
    /// <param name="session">会话;新建会话装配半路取消时为 null(此时无处可写)</param>
    /// <param name="userMessage">本轮输入</param>
    internal static void RestoreUserMessageOnAbort(ChatSession? session, ChatMessage userMessage)
    {
        if (session == null)
        {
            Log.Warning("Turn aborted during session assembly; user message was not persisted.");
            return;
        }

        // 与 TurnDriver 同口径:重试的原消息带着框架就地盖的 _attribution,
        // 不摘掉持久化会把它当注入消息滤掉(见 RunAsync 开头的 ClearAttribution)
        ChatMessageAnnotations.ClearAttribution(userMessage);
        // 取消前若恰好已写回(罕见)就别重复追加:框架落的是同一个实例,按引用判重即可
        if (session.History.Any(x => ReferenceEquals(x, userMessage))) return;

        int before = session.History.Count;
        session.History.Add(userMessage);
        session.SaveAppended(before);
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
            ChatSession created = await _binder.CreateAsync(_currentCharacter, titleSeed,
                Workspace.Path, PermissionModeIndex, cancellationToken, SessionModel.TakeDraft());

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
                ScheduleBackgroundTrim();
                return;
            }

            if (_deferredLoad is not { } deferred) return;

            _deferredLoad = null;
            _ = LoadSessionAsync(deferred);
        }
    }

    /// <summary>
    /// 切走之后把条目压到「后台上限」。
    ///
    /// 排到 Background 优先级而不是就地做：切走那一刻视图还绑在本实例上（页面壳先翻
    /// <see cref="IsDisplayed"/>，DataContext 的替换晚一步到），就地裁等于在「让切换变快」
    /// 这件事上先付一次布局；排到队尾时视图已经换给新会话，本集合不再有人绑，裁剪是纯内存操作。
    /// </summary>
    private void ScheduleBackgroundTrim()
    {
        // 两个数分开量:排队延迟说明这次裁剪有没有被饿着,耗时说明它值不值得占关键路径
        long queuedAt = StartupPhaseProbe.Begin();
        Dispatcher.UIThread.Post(() =>
        {
            if (IsDisplayed) return; //这一小会儿里又切回来了,当前上限自己会管

            StartupPhaseProbe.End("conversation/trim-delay", queuedAt);
            long trimBegin = StartupPhaseProbe.Begin();
            int before = Items.Count;
            if (_trimmer.TrimToBackgroundBudget()) HasEarlierMessages = _historyWindow.HasEarlier;
            StartupPhaseProbe.End($"conversation/trim:{before}->{Items.Count}", trimBegin);
            // Normal 而不是 Background:切走的会话往往正在流式输出,而流式期间高优先级任务
            // 不断进来,Background 会被饿着——那等于切回去时这次裁剪还没发生。
            // Normal 同样排在 DataContext 替换之后(替换是同步做完的),不会误裁到已经绑上的集合
        }, DispatcherPriority.Normal);
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
            RefreshTokenUsageText();
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
            Workspace.Path = meta.WorkspacePath;
            PermissionModeIndex = meta.PermissionModeIndex;
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
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
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
            OnPropertyChanged(nameof(SessionIdShort)); //编号同理:装载之前 CurrentMeta 还是空的
            OnPropertyChanged(nameof(SessionIdFull));
            // 运行态同理,而且更要紧:装载之前 CurrentMeta 还是空的,绑定算出来的是"没在跑"。
            // 而外驱那一轮多半在窗口打开<b>之前</b>就开跑了,登记处的变更信号早发完了——
            // 不在这里补一次,子会话窗口会一直是闲着的样子:没有转圈、没有停止按钮,
            // 用户打的字还会走"发下一轮"而不是"插话"
            NotifyRunStateChanged();
            NotifyBusyChanged();
            //名下子代理的处境同理:它们多半在装载之前就成立了,登记处的信号早发完了
            NotifyApprovalWaitingChanged();

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
            if (_driver.Busy == ETurnBusy.Compacting) InsertHandoffWritingItem();

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
        (int from, int to) = _historyWindow.Reset(messages.Count);
        HasEarlierMessages = _historyWindow.HasEarlier;
        // 有一轮正跑着的时候,历史末尾那次工具调用的结果多半正在路上(它是下一次服务调用的
        // 请求消息,随那次落盘,而实时流这就会把它送来)。按"历史里没有结果"收掉它就是谎报
        bool live = CurrentSession?.LiveTurn.IsTurnRunning == true;
        foreach (ConversationItemBase item in BuildHistoryItems(messages, from, to, liveTail: live))
        {
            Items.Add(item);
        }

        // 会话累计用量从本体恢复(响应 usage 不随消息持久化)
        if (CurrentSession is { } session)
        {
            _usage.RestoreSession(session.TotalInputTokens, session.TotalOutputTokens, session.LastInputTokens,
                session.TotalReasoningTokens);
        }

        RefreshTokenUsageText();
        StartupPhaseProbe.End($"conversation/replay:items={Items.Count},history={messages.Count}", replayBegin);
    }

    /// <summary>
    /// 向前扩展一窗历史。由视图层调用,滚动位置的保持由调用方负责
    /// </summary>
    /// <returns>真的前插了条目返回 true(调用方据此决定要不要补偿视口)</returns>
    public bool LoadEarlierMessages()
    {
        IReadOnlyList<ChatMessage> history = CurrentRunner?.GetHistory() ?? [];
        if (_historyWindow.Extend(history.Count) is not { } range)
        {
            HasEarlierMessages = false;
            return false;
        }

        PrependHistory(history, range);
        HasLoadedEarlier = true;
        return true;
    }

    /// <summary>
    /// 把首屏补齐到整窗。切会话时只回放首屏,省下的那几条布局是"点下去到看见"这段延迟的大头;
    /// 界面贴底可见之后由视图层在空闲时调用本方法补上。滚动位置的保持同样由调用方负责。
    ///
    /// 与 <see cref="LoadEarlierMessages"/> 不同,这不是用户往前翻,所以不置 <see cref="HasLoadedEarlier"/>——
    /// 那个标记只用来决定要不要显示"已到开头"
    /// </summary>
    /// <returns>真的补了条目返回 true</returns>
    public bool FillFirstWindow()
    {
        IReadOnlyList<ChatMessage> history = CurrentRunner?.GetHistory() ?? [];
        if (_historyWindow.FillFirstWindow(history.Count) is not { } range)
        {
            HasEarlierMessages = _historyWindow.HasEarlier;
            return false;
        }

        PrependHistory(history, range);
        return true;
    }

    /// <summary>把一段历史前插到条目集合头部</summary>
    private void PrependHistory(IReadOnlyList<ChatMessage> history, (int From, int To) range)
    {
        List<ConversationItemBase> buffer = BuildHistoryItems(history, range.From, range.To);
        for (int i = 0; i < buffer.Count; i++)
        {
            Items.Insert(i, buffer[i]);
        }

        HasEarlierMessages = _historyWindow.HasEarlier;
    }

    /// <summary>
    /// 回放一段历史到独立缓冲：用一个不订阅用量的转录器实例装配，
    /// 因此不会污染本轮/累计计数（累计口径由 <see cref="ReplayMessages"/> 从会话本体恢复）。
    /// </summary>
    /// <param name="messages">历史</param>
    /// <param name="from">起始下标</param>
    /// <param name="to">结束下标（不含）</param>
    /// <param name="liveTail">
    /// 这一段是<b>还在长的尾巴</b>（外驱会话每次服务调用补渲染一段）而不是定格的历史。
    /// 此时：结果要能配回更早那批里的工具卡（调用与结果落在不同批），
    /// 且尚无结果的调用得继续转圈——按"历史里没有结果"收掉它就是谎报，
    /// 而下一批真把结果送来时卡片早已定格。
    /// </param>
    private List<ConversationItemBase> BuildHistoryItems(IReadOnlyList<ChatMessage> messages, int from, int to,
        bool liveTail = false)
    {
        List<ConversationItemBase> buffer = new();
        ConversationTranscript replay = new(buffer, () => ConversationItemFactory.CreateAssistant(_currentCharacter),
            renderedBefore: liveTail ? (IReadOnlyList<ConversationItemBase>)Items : null)
        {
            AutoCollapseThinking = IsAutoCollapseThinking,
        };

        // 回放时最近见过的时间戳。助手气泡的工厂给不出时间(它只造壳,拿不到源消息),
        // 默认填的是"现在"——重开会话时整段历史因此显示当前时刻。
        // 旧存档里框架产出的消息本就没有时间戳,那种回落到同一轮的用户消息,
        // 误差在一轮之内,总好过一个每次打开都变的假时间
        DateTimeOffset? lastKnown = null;

        for (int index = from; index < to; index++)
        {
            ChatMessage message = messages[index];
            lastKnown = message.CreatedAt ?? lastKnown;
            // 渲染归属只有一份判据:哪些由内容流产出、哪些只能从历史来,
            // 实时流观察那条路问的是同一个函数(见 ConversationMessageOrigin)
            switch (ConversationMessageOrigin.KindOf(message))
            {
                // 交接文档要落盘也要渲染,但渲染成独立卡片而不是助手气泡
                case EHistoryItemKind.HandoffNote:
                    buffer.Add(new HandoffItem
                    {
                        Message = HistoryHandoff.NoteBody(ConversationItemFactory.DisplayTextOf(message)),
                        SourceMessage = message,
                    });
                    continue;

                // 开场白是 assistant 消息(要供给模型,否则首轮又自我介绍一遍),但画成居中旁白
                case EHistoryItemKind.Narration:
                {
                    TextConversationItem narration = _itemActions.Wire(
                        ConversationItemFactory.CreateNarration(message), message);
                    if (lastKnown is { } narrationStamp)
                        narration.Timestamp = ConversationItemFactory.TimestampText(narrationStamp);
                    buffer.Add(narration);
                    continue;
                }

                // 检索片段同样是「落盘但不是对话」:它的角色是 Tool,
                // 落进助手那一档会被当成工具结果去配对一个不存在的调用
                case EHistoryItemKind.Knowledge:
                {
                    ToolCallItem knowledgeCard = ConversationItemFactory.CreateKnowledgeCard(message.Text);
                    knowledgeCard.SourceMessage = message;
                    buffer.Add(knowledgeCard);
                    continue;
                }

                // 子会话的后续报告:角色是 User(它要供给模型),但<b>不是用户说的话</b>——
                // 画成用户气泡等于把子代理的结论安到用户头上。借旁白那套呈现:
                // 居中、无头像无名字,表示"这条不归对话双方任何一方"
                case EHistoryItemKind.SubAgentReport:
                {
                    TextConversationItem reportItem = _itemActions.Wire(
                        ConversationItemFactory.CreateNarration(message), message);
                    if (lastKnown is { } reportStamp)
                        reportItem.Timestamp = ConversationItemFactory.TimestampText(reportStamp);
                    buffer.Add(reportItem);
                    continue;
                }

                case EHistoryItemKind.UserInput:
                {
                    if (CreateUserItem(message) is { } userItem)
                    {
                        if (lastKnown is { } userStamp)
                            userItem.Timestamp = ConversationItemFactory.TimestampText(userStamp);
                        buffer.Add(userItem);
                    }

                    continue;
                }

                case EHistoryItemKind.StreamContents:
                    break; //落到下面交给转录器按内容装配

                default:
                    // 种类加了一项却没在这里表态。抛出来而不是默默画错:
                    // 静默的重复或缺失查起来要命,而这条路一跑就炸
                    throw new ArgumentOutOfRangeException(nameof(message),
                        $"Unhandled history item kind for message role '{message.Role}'.");
            }

            int before = buffer.Count;
            foreach (AIContent content in message.Contents)
            {
                replay.Apply(content);
            }

            replay.CloseSegment();

            // 本条消息产出的<b>每一个</b>条目都记下来源:删除是按「来源落在删除集合里」
            // 摘条目的,漏记的条目会在来源消失后成为删不掉的残留(思考卡、工具卡都没有
            // 自己的删除按钮)。消息级操作只挂在文本气泡上——只有它有那一行按钮
            for (int i = before; i < buffer.Count; i++)
            {
                buffer[i].SourceMessage = message;
                // 回放定格：命中存档读存档（冻结真耗时），未命中只留字数——
                // 重建的 _startedAt 是打开会话那一刻，不定格就是统一 0.1s 的假耗时
                if (buffer[i] is ThinkingItem thinking) ThinkingItem.FreezeReplayItem(thinking, message);
                if (buffer[i] is not TextConversationItem textItem) continue;

                _itemActions.Wire(textItem, message);
                if (lastKnown is { } stamp) textItem.Timestamp = ConversationItemFactory.TimestampText(stamp);
            }
        }

        // 调用与它的结果是两条消息,开窗分批完全可能把它们切在两批里:不越过批边界找一次,
        // 批尾那次调用就会被下面的收尾误判成「历史里没有这次调用的结果」(liveTail 那一批
        // to 就是历史末尾,这里是空操作)
        replay.ApplyLaterResults(messages, to);

        if (liveTail) replay.CloseSegment();
        else replay.FinalizeReplay(Loc.Text(LangKey.AgentToolCallUnfinished));

        return buffer;
    }

    //================= IConversationItemActionHost =================
    // 显式实现:这五件事是给消息级操作用的,不该混进本类给界面绑定的公开面

    /// <inheritdoc />
    ChatSession? IConversationItemActionHost.Session => CurrentSession;

    /// <inheritdoc />
    bool IConversationItemActionHost.IsGenerating => IsGenerating;

    /// <inheritdoc />
    void IConversationItemActionHost.Rerun(ChatMessage input)
    {
        ScrollToEnd = true;
        _ = RunTurnAsync(input, ConversationItemFactory.DisplayTextOf(input));
    }

    /// <inheritdoc />
    void IConversationItemActionHost.NotifySessionsChanged() => SessionsChanged?.Invoke();

    /// <inheritdoc />
    void IConversationItemActionHost.NotifyItemsWired() => OnPropertyChanged(nameof(CanRegenerate));

    //================= IConversationReconcileHost =================
    // 显式实现:对账要的只是这几个窄依赖,不该把整个视图模型暴露给对账器

    /// <inheritdoc />
    bool IConversationReconcileHost.IsOwnTurnRunning => _driver.IsRunning;

    /// <inheritdoc />
    bool IConversationReconcileHost.IsSessionLoading => IsSessionLoading;

    /// <inheritdoc />
    ChatSession? IConversationReconcileHost.CurrentSession => CurrentSession;

    /// <inheritdoc />
    bool IConversationReconcileHost.HasEarlierMessages
    {
        get => HasEarlierMessages;
        set => HasEarlierMessages = value;
    }

    /// <inheritdoc />
    bool IConversationReconcileHost.IsSessionBusy(string sessionId) =>
        SessionManager.Instance.Running.IsBusy(sessionId);

    /// <inheritdoc />
    List<ConversationItemBase> IConversationReconcileHost.BuildItems(IReadOnlyList<ChatMessage> history,
        int from, int to) => BuildHistoryItems(history, from, to);

    /// <inheritdoc />
    void IConversationReconcileHost.RefreshTokenUsage() => RefreshTokenUsageText();

    private ChatSession? CurrentSession =>
        CurrentMeta == null ? null : SessionManager.Instance.Load(CurrentMeta.SessionId);

    /// <summary>当前会话的执行者(会话本体持有);无会话为 null</summary>
    private ICharacterRunner? CurrentRunner => CurrentSession?.Runner;

    //================= token 统计 =================

    partial void OnInputTextChanged(string value)
    {
        // 同步到会话草稿(纯内存)。落盘时机交给宿主:切会话/切页/弃用时由页面壳调 SaveMeta
        if (CurrentSession is { } session) session.ComposerDraft = value;

        _ = Palette.RefreshSkillCandidatesAsync(value); //点名补全:仅在整行以 / 开头且技能名未写完时弹出

        int version = ++_inputCountVersion;
        if (string.IsNullOrEmpty(value))
        {
            _usage.InputEstimate = 0;
            RefreshTokenUsageText();
            return;
        }

        // 后台估算(首次会加载词表),只采纳最新一次的结果
        _ = Task.Run(() =>
        {
            int count = LlmTokenizer.CountTokens(value);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (version != _inputCountVersion) return;
                _usage.InputEstimate = count;
                // 防抖:打字时每个字符都会触发一次估算,合并到停手后一次性刷新,
                // 避免 ToolTip 里的 ContextUsagePanel 跟着每个字符重排而闪烁
                _tokenRefreshDebounce?.Cancel();
                _tokenRefreshDebounce = new CancellationTokenSource();
                CancellationToken token = _tokenRefreshDebounce.Token;
                DispatcherTimer.RunOnce(() =>
                {
                    if (token.IsCancellationRequested) return;
                    RefreshTokenUsageText();
                }, TimeSpan.FromMilliseconds(200));
            });
        });
    }

    private void RefreshTokenUsageText()
    {
        // 模型就绪的通知来自后台线程的异步续体,而绑定要求属性变更在 UI 线程上抛
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshTokenUsageText);
            return;
        }

        // 上限每次刷新时现读:顶栏换模型不重建 agent,这里同样不能缓存
        _usage.ContextLength = (CurrentSession?.ChatModelRunningData
                                ?? LlmManager.Instance.CurrentRunningModel)?.ContextLength ?? 0;
        TokenUsageText = _usage.Text;
        ContextUsage.Refresh(_usage, SessionModelLabel);
    }

    /// <summary>
    /// 合并刷新 token 统计。流式期间 provider 每个 chunk 都带 UsageContent 时,
    /// <see cref="ETurnNotice.UsageObserved"/> 会高频到达——每次都重排状态栏文本与
    /// ToolTip 面板就闪。这里把刷新收进 250ms 窗口,窗口内只刷一次;
    /// <paramref name="force"/> 跳过合并立即刷(流结束时补最终值)。
    /// </summary>
    /// <param name="force">是否立即刷新,不合并</param>
    private void DebouncedRefreshTokenUsage(bool force = false)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => DebouncedRefreshTokenUsage(force));
            return;
        }

        _usageRefreshDebounce?.Cancel();
        if (force)
        {
            RefreshTokenUsageText();
            return;
        }

        _usageRefreshDebounce = new CancellationTokenSource();
        CancellationToken token = _usageRefreshDebounce.Token;
        DispatcherTimer.RunOnce(() =>
        {
            if (token.IsCancellationRequested) return;
            RefreshTokenUsageText();
        }, TimeSpan.FromMilliseconds(250));
    }

    //================= 能力面板 =================

    /// <summary>
    /// 刷新右栏能力面板。挂接完成后调用——装配就在挂接里做，早于此调用拿到的是上一轮的工具集
    /// </summary>
    private async Task RefreshCapabilitiesAsync()
    {
        try
        {
            // 上限现读,与 RefreshTokenUsageText 同一解析次序:顶栏换模型不重建 agent,
            // 缓存下来就会留下过期的分母
            int contextLength = (CurrentSession?.ChatModelRunningData
                                 ?? LlmManager.Instance.CurrentRunningModel)?.ContextLength ?? 0;
            await Capabilities.RefreshAsync(CurrentRunner, IsAgentSession ? SessionCharacter : null, contextLength,
                Workspace.Path, PermissionModeIndex);
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

    /// <summary>条目与标题用的显示文本:点名调用取用户敲的那一行,其余取消息正文</summary>

    //================= 条目构造 =================

    /// <summary>助手条目:名字与头像取自当前会话的角色</summary>
    private void ClearStreamState()
    {
        // 气泡里的图是本会话现解出来的大位图,随条目走;条目被整体丢掉时没人会去释放它们,
        // 于是切一次会话就漏掉一整个会话的图。先 Clear 摘掉绑定,再释放(顺序反了会撞渲染)
        ConversationItemBase[] discarded = Items.ToArray();
        Items.Clear();
        foreach (ConversationItemBase item in discarded) item.ReleaseImages();
        // 整理中的占位卡随清空一起消失;若压缩还在跑,切回时由 LoadSessionAsync 重新挂上
        _handoffWritingItem = null;

        Todos.Clear();
        HasTodos = false;
        HasEarlierMessages = false;
        HasLoadedEarlier = false;
        _historyWindow.Clear();
        PendingInterjections.Clear(); //待发的插话归属于那个会话的注入队列,切走就不再显示
        _transcript.Reset();
        _usage.Reset();
        // 记忆库面板与 token 文本不在此清空:切会话时先空后填会让工具行闪烁,
        // 由 LoadSessionAsync 在新值就绪时一次性替换
    }
}

/// <summary>
/// todo 侧栏显示项
/// </summary>
public class TodoDisplayItem
{
    /// <summary>内容描述</summary>
    public string Content { get; }

    /// <summary>状态图形符号</summary>
    public string StatusGlyph { get; }

    /// <summary>是否已完成(删除线样式)</summary>
    public bool IsCompleted { get; }

    public TodoDisplayItem(TodoSnapshot todo)
    {
        Content = todo.Title;
        IsCompleted = todo.IsComplete;
        StatusGlyph = todo.IsComplete ? "✓" : "○";
    }
}

/// <summary>
/// 输入区上方那一条待发的插话
/// </summary>
/// <param name="Message">投入注入队列的那个实例（与消费时流出来的是同一个，据此撤掉提示）</param>
/// <param name="Text">显示文本</param>
public sealed record PendingInterjectionViewData(ChatMessage Message, string Text);