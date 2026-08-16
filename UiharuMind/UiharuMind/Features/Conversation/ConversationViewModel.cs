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
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Mcp;
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
public partial class ConversationViewModel : ViewModelBase, IConversationItemActionHost, IDisposable
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
    [ObservableProperty] private KeyGesture _sendGesture = new(Key.Enter);
    [ObservableProperty] private SendMode _senderMode = SendMode.User;
    [ObservableProperty] private bool _isPlaintext;
    [ObservableProperty] private bool _isAutoCollapseThinking;
    [ObservableProperty] private bool _hasEarlierMessages;
    [ObservableProperty] private bool _isSessionLoading; //会话切换构建中(空状态覆盖层此间不显示,避免闪烁)
    [ObservableProperty] private int _thinkingModeIndex; //本会话思考力度,序号即 EThinkingMode
    [ObservableProperty] private string _tokenUsageText = string.Empty; //token 统计(输入估算/本轮/会话累计)

    [RelayCommand]
    private async Task SendMessage()
    {
        // 补全开着时回车应当是"采纳候选"而不是发送。这里必须在命令入口改道:
        // Avalonia 的 KeyBindings 由 KeyboardDevice.ProcessRawEvent 沿视觉父链处理,
        // 时机在 KeyDown 路由事件被 raise 之前,连 Tunnel 都拦不住它
        if (Palette.AcceptSkillCandidate()) return;

        string text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;
        InputText = string.Empty;
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

    [ObservableProperty] private int _permissionModeIndex = 1; //默认 AutoEdit
    [ObservableProperty] private EAgentMode _currentMode = EAgentMode.Execute;
    [ObservableProperty] private bool _hasTodos;

    /// <summary>当前会话元数据(未开始首轮前为空)</summary>
    public ChatSessionMeta? CurrentMeta { get; private set; }

    /// <summary>无会话时首轮发送创建新会话所用的角色;agent 页默认主 agent,聊天页由页面壳指定</summary>
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
    /// 当前会话对应的模型名:会话绑定的专属模型 → 全局当前模型 →
    /// 将被自动解析的偏好模型(未选模型时发送会走同一解析函数,显示与实际使用一致)
    /// </summary>
    public string SessionModelLabel =>
        CurrentSession?.ChatModelRunningData?.ModelName
        ?? LlmManager.Instance.CurrentRunningModel?.ModelName
        ?? LlmManager.Instance.GetPreferredModelName(false)
        ?? string.Empty;

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

    /// <summary>思考力度状态键(EThinkingMode 名)</summary>
    public string ThinkingModeKey => ConversationModeLabels.ThinkingKey(ThinkingModeIndex);

    /// <summary>思考力度悬停提示</summary>
    public string ThinkingTooltip => ConversationModeLabels.ThinkingTooltip(ThinkingModeIndex);

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

    private const string ThinkingModeParamName = "ThinkingMode"; //CustomParams 中的思考力度键

    private CancellationTokenSource? _prepareCancellation; //会话装配阶段的取消源,此后由 TurnDriver 接手
    private bool _isPreparing; //正在装配会话(此时 TurnDriver 还没开始跑)
    private int _loadVersion; //会话加载版本号,用于放弃已被新切换取代的旧加载
    private bool _isLoadingSession; //加载会话期间抑制设置写回(加载是读,不是用户改动)
    private int _inputCountVersion; //输入估算版本号,后台计数只采纳最新一次

    private readonly ConversationItemActions _itemActions; //气泡上的编辑/删除/分叉/重试
    private readonly ConversationSessionBinder _binder; //建/装会话并挂执行者
    private readonly ConversationTranscript _transcript; //实时流装配器,落点即 Items
    private readonly TurnDriver _driver; //一轮对话的编排,与定时任务共用同一份
    private readonly HistoryWindow _historyWindow = new(); //历史渲染窗口
    private readonly TurnUsageLedger _usage = new(); //token 账本

    /// <summary>上下文占用的悬停面板数据（进度条、压缩水位刻度与配色）</summary>
    public ContextUsageViewData ContextUsage { get; } = new();

    /// <summary>
    /// 本轮是否正在跑。装配会话的那一小段也算在内——那时执行者还没接手，
    /// 但界面必须已经显示停止按钮，否则用户能在装配期间再发一条。
    /// </summary>
    public bool IsGenerating => _isPreparing || _driver.IsRunning;

    /// <summary>运行态指示点的配色键（status-dot 样式按 Tag 选色）</summary>
    public string RunStatusKey => IsGenerating ? "Ready" : "Idle";

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
        ETurnBusy.ConnectingMcp => LocalizationManager.Instance.GetString("AgentMcpConnecting"),
        ETurnBusy.Compacting => LocalizationManager.Instance.GetString("HandoffWriting"),
        _ => string.Empty,
    };

    public ConversationViewModel()
    {
        // 子模型只吃窄依赖、不反向持有本类:附件盘取会话要用委托(首轮发送时会话还不存在),
        // 命令面板要能改写输入框并读当前角色,挂接器只需报忙碌态
        Tray = new AttachmentTrayViewData(() => CurrentSession, () => SessionCharacter);
        Palette = new CommandPaletteViewData(text => InputText = text, () => SessionCharacter);
        _binder = new ConversationSessionBinder(NotifyBusyChanged);
        _itemActions = new ConversationItemActions(Items, this);

        var agentSetting = AgentSettingConfig.Current;
        // 工作目录选择器要在最早构造:它持有那份状态,后面几处都从它读
        string? defaultWorkspace =
            !string.IsNullOrEmpty(agentSetting.DefaultWorkspacePath) &&
            Directory.Exists(agentSetting.DefaultWorkspacePath)
                ? agentSetting.DefaultWorkspacePath
                : null;
        Workspace = new WorkspacePickerViewData(defaultWorkspace, OnWorkspacePathChanged);

        _transcript = new ConversationTranscript(Items, () => ConversationItemFactory.CreateAssistant(_currentCharacter),
            pattern => CurrentSession?.AddSessionApprovedShellPattern(pattern),
            () => CurrentSession?.WorkspacePath);
        // 用量不经转录器转发:运行侧看得见同一条内容流,由它记账并写回会话本体,
        // 这里只负责把数字刷到界面上(UsageObserved 通知)
        _transcript.HousekeepingToolCalled += () => _ = RefreshTodosAsync();
        _driver = new TurnDriver(_transcript, _usage, OnTurnNotice);
        _driver.StateChanged += OnDriverStateChanged;

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
        InputPlaceholder = LocalizationManager.Instance.GetString(_inputPlaceholderKey);
    }

    /// <summary>
    /// 运行态或忙碌态变化。运行侧不认识绑定，属性变更由这里代它抛出。
    /// </summary>
    private void OnDriverStateChanged()
    {
        NotifyRunStateChanged();
        NotifyBusyChanged();
    }

    private void NotifyRunStateChanged()
    {
        OnPropertyChanged(nameof(IsGenerating));
        OnPropertyChanged(nameof(RunStatusKey));
        OnPropertyChanged(nameof(CanRegenerate));
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
        Tray.NotifyVisionStateChanged(); //换成非视觉模型时,待发的图就该立刻出警示
        // 上限是跟着模型走的:换个模型,占用的分母、三条水位与配色档位全都变了
        RefreshTokenUsageText();
    }

    private void OnLanguageChanged()
    {
        InputPlaceholder = LocalizationManager.Instance.GetString(_inputPlaceholderKey);
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(ModeTooltip));
        OnPropertyChanged(nameof(PermissionTooltip));
        OnPropertyChanged(nameof(ThinkingTooltip));
        OnPropertyChanged(nameof(SenderTooltip));
        RefreshTokenUsageText(); //压缩水位那句提示是在 C# 里拼的,不会自己跟着语言变
    }

    /// <summary>
    /// 弃用本实例：反注销全局事件、弃用运行侧（它会取消正在跑的那一轮）。
    ///
    /// 只取消、不在这里补写取消结果——运行循环还活着，它自己会在取消分支里收尾。
    /// 要在进程即将消失时同步补写的场合用 <see cref="TurnDriver.SettleAllForShutdown"/>。
    /// </summary>
    public void Dispose()
    {
        LlmManager.Instance.OnCurrentModelChanged -= OnCurrentModelChanged;
        LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
        _driver.StateChanged -= OnDriverStateChanged;
        // 执行者归会话所有、比本视图活得久,回调不摘就是一路泄漏到已销毁的视图上
        if (CurrentRunner is { } runner) runner.BusyChanged = null;
        _prepareCancellation?.Cancel();
        _driver.Dispose();
        MemoryPanel?.Detach();
    }

    private string _inputPlaceholderKey = "AgentInputWatermark";

    /// <summary>输入框占位文案的本地化键,页面壳按场景覆盖(agent 页描述任务,聊天页输入消息)</summary>
    public string InputPlaceholderKey
    {
        get => _inputPlaceholderKey;
        set
        {
            _inputPlaceholderKey = value;
            InputPlaceholder = LocalizationManager.Instance.GetString(value);
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

    partial void OnThinkingModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ThinkingModeKey));
        OnPropertyChanged(nameof(ThinkingTooltip));
        if (CurrentMeta == null || _isLoadingSession) return;
        ChatSession? session = CurrentSession;
        if (session == null) return;
        session.CustomParams[ThinkingModeParamName] = ((EThinkingMode)value).ToString();
        session.SaveMeta(); //头字段小文件,直接原子写
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
        CharacterInfoViewData info = new(ActiveCharacter)
        {
            // 让编辑页能按开关标出「关掉这一档能省多少 token」。必须在能力面板首次建之前给
            CapabilitySnapshot = CurrentRunner?.GetCapabilities(),
        };
        CharacterWindows.ShowEditCharacterWindow(info, x => x.SaveCharacter());
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
                    LocalizationManager.Instance.GetString("AgentMcpApprovalTitle")))
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
        // 手动压缩:任务的自然边界由你比水位更清楚,在边界上压缩,交接文档质量高得多
        if (string.Equals(text.Trim(), CommandPaletteViewData.CompactCommand,
                StringComparison.OrdinalIgnoreCase))
        {
            if (!IsGenerating && CurrentSession is { } current)
            {
                await _driver.CompactAsync(current, current.Runner);
            }

            return;
        }

        // 运行中输入 = 插话:入注入队列,agent 下一次机会消费
        if (IsGenerating)
        {
            if (CurrentRunner is { } runner &&
                await runner.TryInjectAsync(new[] { new ChatMessage(ChatRole.User, text) }))
            {
                Items.Add(ConversationItemFactory.CreateUser(text));
            }

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

        Items.Add(ConversationItemFactory.CreateUser(text, userMessage, attachments));
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
        _transcript.CancelPendingApprovals();
    }

    /// <summary>
    /// 审批回应：等用户对本轮每个请求做出决定，回应即下一轮的输入。
    ///
    /// 不按 CallId 反查——请求与界面卡片由同一批 <see cref="ConversationTranscript.Apply"/>
    /// 产生，而运行侧一定在内容流耗尽之后才调这里，所以转录器本轮收集的那批就是顺序一致的同一批。
    /// </summary>
    /// <param name="requests">本轮新增的审批请求（顺序与转录器收集的一致，故不另用）</param>
    /// <returns>回应消息</returns>
    private async Task<IReadOnlyList<ChatMessage>> ResolveApprovalsAsync(
        IReadOnlyList<ToolApprovalRequestContent> requests)
    {
        IReadOnlyList<ApprovalRequestItem> turnApprovals = _transcript.TakeRoundApprovals();
        List<ChatMessage> responses = new(turnApprovals.Count);
        foreach (ApprovalRequestItem approval in turnApprovals)
        {
            responses.Add(await approval.Response);
        }

        _transcript.ResolveApprovals(turnApprovals);
        return responses;
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
                break;

            case ETurnNotice.Ended:
                _transcript.ResolveApprovals(_transcript.PendingApprovals.ToList());
                SessionsChanged?.Invoke();
                break;

            case ETurnNotice.Failed:
                Items.Add(new ErrorItem { Message = notice.Payload ?? string.Empty });
                break;

            case ETurnNotice.ScrollToEnd:
                ScrollToEnd = true;
                break;

            case ETurnNotice.UsageObserved:
                RefreshTokenUsageText();
                break;

            case ETurnNotice.KnowledgeRetrieved:
                // 落盘那份由 SessionChatHistoryProvider 在轮末插进历史,这里只管本轮即时可见;
                // 两者内容同源,重载会话后由回放分支再造出同一张卡
                Items.Add(ConversationItemFactory.CreateKnowledgeCard(notice.Payload ?? string.Empty));
                break;

            case ETurnNotice.HandoffWritten:
                Items.Add(new HandoffItem { Message = notice.Payload ?? string.Empty });
                break;

            case ETurnNotice.HandoffFailed:
                Items.Add(new ErrorItem { Message = LocalizationManager.Instance.GetString("HandoffFailed") });
                break;

            case ETurnNotice.HandoffNothingToCompact:
                Items.Add(new ErrorItem
                    { Message = LocalizationManager.Instance.GetString("HandoffNothingToCompact") });
                break;
        }
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
        try
        {
            ChatSession session;
            // 装配阶段也登记成「在跑」:重建 agent 要拉 MCP 工具、可能好几秒,
            // 这期间不能让删除/清空去动它的文件,而那一轮随后照样会往里写。
            // 新会话此刻还没有标识,BeginRun(null) 按设计是空操作
            using (SessionManager.Instance.Running.BeginRun(CurrentMeta?.SessionId))
            {
                session = await EnsureSessionAsync(titleSeed, _prepareCancellation.Token);
                Tray.FlushOwnedFiles();
            }

            // 交接给运行侧:它在返回之前就同步登记好了运行态,两段之间没有空窗
            await _driver.RunAsync(session, session.Runner, userMessage, ResolveApprovalsAsync,
                (EThinkingMode)ThinkingModeIndex);
        }
        catch (OperationCanceledException)
        {
            //装配阶段就被停掉:还没发出任何请求,没有要收尾的东西
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
                Workspace.Path, PermissionModeIndex, cancellationToken);

            CurrentMeta = created.ToMeta();
            Title = CurrentMeta.Title;
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
        OnPropertyChanged(nameof(ActiveCharacterName));
        OnPropertyChanged(nameof(ActiveCharacterDescription));
        OnPropertyChanged(nameof(ActiveCharacterIcon));
        if (meta == null)
        {
            IsSessionLoading = false;
            ThinkingModeIndex = (int)EThinkingMode.Default; //CurrentMeta 为空,处理器自然不落盘
            MemoryPanel?.Detach();
            MemoryPanel = null;
            RefreshTokenUsageText();
            // 空态也要刷一次能力面板。这里曾经直接返回,于是新会话在首轮发送之前
            // 整个「能力」页签一片空白——而恰恰是这个时候用户最需要知道
            // 「这个会话会自动连上什么」。没有执行者也仍有东西可报:技能清单与 MCP 预告
            // 都不依赖装配产物(工具与提示词那两档依赖,它们留到首轮之后)
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

        // 分帧:先让"清空旧会话"渲染出去,再构建新会话,把一次长冻结拆成两段短的
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        if (loadVersion != _loadVersion) return;

        try
        {
            ChatSession? body = await AttachAsync(meta, CancellationToken.None);
            if (loadVersion != _loadVersion) return;
            if (body == null)
            {
                Items.Add(new ErrorItem { Message = $"Session '{meta.SessionId}' could not be loaded." });
                return;
            }

            MemoryPanel?.Detach();
            MemoryPanel = new ConversationMemoryViewData(body);
            _isLoadingSession = true;
            try
            {
                ThinkingModeIndex = ReadThinkingModeIndex(body);
            }
            finally
            {
                _isLoadingSession = false;
            }

            CurrentMode = await body.Runner.GetModeAsync();
            ReplayMessages(body.Runner.GetHistory());
            await RefreshTodosAsync();
            await RefreshCapabilitiesAsync();
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
    /// 只渲染最近一窗,更早的由"加载更早"按批前插——非虚拟化列表靠数据开窗保住长会话性能
    /// </summary>
    private void ReplayMessages(IReadOnlyList<ChatMessage> messages)
    {
        (int from, int to) = _historyWindow.Reset(messages.Count);
        HasEarlierMessages = _historyWindow.HasEarlier;
        foreach (ConversationItemBase item in BuildHistoryItems(messages, from, to))
        {
            Items.Add(item);
        }

        // 会话累计用量从本体恢复(响应 usage 不随消息持久化)
        if (CurrentSession is { } session)
        {
            _usage.RestoreSession(session.TotalInputTokens, session.TotalOutputTokens, session.LastInputTokens);
        }

        RefreshTokenUsageText();
    }

    /// <summary>
    /// 向前扩展一窗历史。由视图层调用,滚动位置的保持由调用方负责
    /// </summary>
    public void LoadEarlierMessages()
    {
        IReadOnlyList<ChatMessage> history = CurrentRunner?.GetHistory() ?? [];
        if (_historyWindow.Extend(history.Count) is not { } range)
        {
            HasEarlierMessages = false;
            return;
        }

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
    private List<ConversationItemBase> BuildHistoryItems(IReadOnlyList<ChatMessage> messages, int from, int to)
    {
        List<ConversationItemBase> buffer = new();
        ConversationTranscript replay = new(buffer, () => ConversationItemFactory.CreateAssistant(_currentCharacter))
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
            // 交接文档要落盘也要渲染,但渲染成独立卡片而不是助手气泡,因此先于常规分派拦下
            if (HistoryHandoff.IsNote(message))
            {
                buffer.Add(new HandoffItem { Message = HistoryHandoff.NoteBody(ConversationItemFactory.DisplayTextOf(message)) });
                continue;
            }

            // 检索片段同样是「落盘但不是对话」,渲染成检索卡片,也先于常规分派拦下——
            // 它的角色是 Tool,落进下面的助手分支会被当成工具结果去配对一个不存在的调用
            if (ChatMessageAnnotations.IsKnowledge(message))
            {
                buffer.Add(ConversationItemFactory.CreateKnowledgeCard(message.Text));
                continue;
            }

            if (message.Role == ChatRole.User)
            {
                string text = ConversationItemFactory.DisplayTextOf(message);
                if (!ConversationItemFactory.IsFrameworkInjected(message) && (!string.IsNullOrWhiteSpace(text) || ConversationItemFactory.HasImage(message)))
                {
                    TextConversationItem userItem = _itemActions.Wire(ConversationItemFactory.CreateUser(text, message), message);
                    if (lastKnown is { } userStamp) userItem.Timestamp = ConversationItemFactory.TimestampText(userStamp);
                    buffer.Add(userItem);
                }

                continue;
            }

            int before = buffer.Count;
            foreach (AIContent content in message.Contents)
            {
                replay.Apply(content);
            }

            replay.CloseSegment();

            // 本条消息产出的文本气泡可定位回这条消息,据此提供消息级操作
            for (int i = before; i < buffer.Count; i++)
            {
                if (buffer[i] is not TextConversationItem textItem) continue;
                _itemActions.Wire(textItem, message);
                if (lastKnown is { } stamp) textItem.Timestamp = ConversationItemFactory.TimestampText(stamp);
            }
        }

        replay.FinalizeReplay(LocalizationManager.Instance.GetString("AgentToolCallUnfinished"));
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

    private ChatSession? CurrentSession =>
        CurrentMeta == null ? null : SessionManager.Instance.Load(CurrentMeta.SessionId);

    /// <summary>当前会话的执行者(会话本体持有);无会话为 null</summary>
    private ICharacterRunner? CurrentRunner => CurrentSession?.Runner;

    //================= token 统计 =================

    partial void OnInputTextChanged(string value)
    {
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
                RefreshTokenUsageText();
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

    private static int ReadThinkingModeIndex(ChatSession session)
    {
        if (session.CustomParams.TryGetValue(ThinkingModeParamName, out object? value) &&
            Enum.TryParse(value?.ToString(), out EThinkingMode mode))
        {
            return (int)mode;
        }

        return (int)EThinkingMode.Default;
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
                Workspace.Path);
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

        Todos.Clear();
        HasTodos = false;
        HasEarlierMessages = false;
        _historyWindow.Clear();
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
