using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.SessionList;
using UiharuMind.Features.Conversation.SidePanels;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Pages;

/// <summary>
/// 合并后的对话页数据壳：一个页面容纳普通对话与智能体，差异由 <see cref="CurrentType"/>
/// （左栏切换器）<b>唯一</b>决定——列表、会话区、右栏面板全部跟随它，右栏自身不持有类型状态。
///
/// 取代原 <c>ChatPageData</c> 与 <c>AgentPageData</c>（见 ADR 0011 的改写）。
/// 统一<b>懒建</b>：新建按钮只切空态不建会话；唯一例外是「从角色出发开聊」
/// （空态右栏点角色、角色页 StartChat 直达）——那是明确的表态，急建并自动选中，
/// 开场白因此能当场写进历史（ADR 0016）。
/// </summary>
public partial class ConversationPageData : ConversationPageDataBase
{
    protected override Control CreateView => new ConversationPage();

    /// <summary>
    /// 当前会话类型。唯一入口是左栏切换器；角色页开聊直达也经 <see cref="RevealSession"/> 改它。
    /// 切换时：列表换类、会话区回到该类上次看的（没有则首条，再没有才空态）、右栏换面板。
    /// </summary>
    [ObservableProperty]
    private EConversationType _currentType = EConversationType.Agent;

    /// <summary>智能体右栏页签选中项（能力徽章跳转与它共用本壳）</summary>
    [ObservableProperty] private int _selectedSidePanelIndex;

    /// <summary>会话详情面板（普通对话右栏）</summary>
    public ChatInfoModel ChatInfo { get; }

    /// <summary>定时任务侧栏（智能体右栏）</summary>
    public ScheduledTaskListModel Scheduled { get; }

    /// <summary>子代理索引（智能体右栏。群聊的成员窗口将来也复用这块——子会话那套机制，不并入）</summary>
    public SubAgentListViewData SubAgents { get; }

    /// <summary>「能力」页签在右栏里的下标（徽章跳转与默认值都指向它）</summary>
    public const int CapabilityTabIndex = 0;

    /// <summary>
    /// 当前是否空态。以<b>会话区</b>状态判（<c>CurrentMeta == null</c>）而不是列表选中：
    /// 懒建下首轮发送会建会话、列表只加条目不选中，此时必须从新建卡切到"正在聊"——
    /// 用列表选中判会停在空态（见 review 0042 的 major 2）
    /// </summary>
    public bool IsEmptyState => Conversation?.CurrentMeta == null;

    /// <summary>当前是否为普通对话档（右栏/头像/新建默认角色据此切换）</summary>
    public bool IsChatType => CurrentType == EConversationType.Chat;

    /// <summary>当前是否为智能体类型</summary>
    public bool IsAgentType => CurrentType == EConversationType.Agent;

    /// <summary>普通对话且已有会话：右栏下块显示会话详情（ChatInfoView）</summary>
    public bool IsChatWithSession => !IsAgentType && !IsEmptyState;

    /// <summary>左栏标题：当前类型名</summary>
    public string TypeTitle => Loc.Text(CurrentType == EConversationType.Chat
        ? LangKey.ConversationTypeChat
        : LangKey.ConversationTypeAgent);

    /// <summary>
    /// 右栏当前应显示哪个面板。<c>ContentControl.Content</c> 直接绑页面本体、
    /// <c>ContentTemplate</c> 按这个值经 <see cref="RightPaneTemplateConverter"/> 切换——
    /// 同一时刻只渲染一个面板，从结构上杜绝空态/非空态重叠（IsVisible 组合在实机上不可靠），
    /// 且模板内 <c>DataContext</c> 仍是本页，各面板的绑定原样有效。
    /// 两类统一懒建：普通对话空态是新建卡；智能体空态也是完整面板（工作区/能力预演/
    /// 定时任务在无会话时本来就有数，合页前一直这样展示，懒建不丢信息）
    /// </summary>
    public ERightPaneKind RightPaneKind => IsAgentType
        ? ERightPaneKind.Agent
        : IsEmptyState ? ERightPaneKind.NewSession : ERightPaneKind.Chat;

    public ConversationPageData() : base(EConversationType.Agent)
    {
        // 与 ChatInfoView 构造自取的是同一个全局单例（App.ViewModel 按类型缓存）——
        // 页面 SetSession 要能反映到视图上，两者必须同一份
        ChatInfo = App.ViewModel.GetViewModel<ChatInfoModel>();
        Scheduled = new ScheduledTaskListModel(OpenSession);
        SubAgents = new SubAgentListViewData(() => Conversation?.CurrentMeta?.SessionId);

        SessionList.SelectionChanged += OnSelectionChanged;
        SessionList.Mutated += OnSessionMutated;
        SessionList.Removed += OnSessionRemoved;

        // 启动时恢复最近会话(历史加载不依赖模型状态)
        SessionListItem? first = SessionList.Sessions.FirstOrDefault();
        SwitchConversation(first?.Meta);
        SessionList.SelectWithoutNotifying(first);
        SubAgents.Refresh();
    }

    //================= 类型切换 =================

    /// <summary>左栏切换图标：在两个类型之间往返（工具提示说明目标）</summary>
    [RelayCommand]
    private void ToggleType()
    {
        CurrentType = CurrentType == EConversationType.Chat
            ? EConversationType.Agent
            : EConversationType.Chat;
    }

    partial void OnCurrentTypeChanged(EConversationType value)
    {
        // 换列表并按「上次看的→首条」静默选中。有选中就走用户点选同一路直接装载，
        // 切类型不再回空态；该类型一条都没有才回空态（配置随类型换，沿用旧口径）。
        SessionList.SwitchType(value);
        if (SessionList.SelectedSession is { } item)
        {
            OnSelectionChanged(item);
        }
        else
        {
            //   - 当前已是空会话 → forceRecreate 换新类型配置（默认角色/占位随类型变）
            //   - 当前还挂在旧类型会话上 → 普通退回空态
            SwitchConversation(null, forceRecreate: Conversation?.CurrentMeta == null);
            ChatInfo.SetSession(null);
            SubAgents.Refresh();
            RefreshRightPaneState();
        }

        OnPropertyChanged(nameof(IsChatType));
        OnPropertyChanged(nameof(IsAgentType));
        OnPropertyChanged(nameof(IsChatWithSession));
        OnPropertyChanged(nameof(TypeTitle));
    }

    /// <summary>
    /// 角色页「开始对话」直达：切到对应类型区并选中指定会话。
    /// 会话已由调用方急建入索引，这里只负责把它带到眼前
    /// </summary>
    /// <param name="sessionId">目标会话</param>
    /// <param name="type">目标类型</param>
    public void RevealSession(string sessionId, EConversationType type)
    {
        if (CurrentType != type) CurrentType = type;
        SessionList.SelectSession(sessionId);
    }

    /// <summary>
    /// 按类型各留一个最近在看的会话实例：切类型回来直接复用，不走冷重载。
    /// 上限两个（两类各一），与合页前的两页各持一个当前会话是同一量级
    /// </summary>
    protected override bool ShouldRetainConversation(ConversationViewModel conversation)
    {
        string? id = conversation.CurrentMeta?.SessionId;
        return id != null && SessionList.LastSelectedSessionIds.Contains(id);
    }

    //================= 新建 =================

    [RelayCommand]
    private void NewSession()
    {
        // 只切空态,不建会话(懒建):首轮发送时才入索引。误触零成本。
        // 角色由 CreateConversation 按"继承上个空会话"口径重建，不用在这里动手
        SessionList.SelectWithoutNotifying(null);
        SwitchConversation(null);
        ChatInfo.SetSession(null);
        SubAgents.Refresh();
        RefreshRightPaneState();
    }

    //================= 会话区 =================

    protected override ConversationViewModel CreateConversation()
    {
        ConversationViewModel conversation = new();
        if (CurrentType == EConversationType.Chat)
        {
            // 普通对话：继承上一个空会话的角色（同类才继承，跨类型不污染），否则回默认角色；
            // 输入框占位是聊天口吻
            conversation.NewSessionCharacterId =
                InheritCharacterId(EConversationType.Chat, nameof(DefaultCharacter.None));
            conversation.InputPlaceholderKey = LangKey.ChatInputTips;
        }
        else
        {
            // 智能体：新开空态继承当前展示会话的工作区与角色——切页/新开会话不该把已选路径
            // 和已选角色重置掉（此处 Conversation 仍是旧实例，基类赋值在其后，
            // 继承只对空态生效；角色跨档不继承，见 InheritCharacterId）
            conversation.NewSessionCharacterId =
                InheritCharacterId(EConversationType.Agent, nameof(DefaultCharacter.ChenXiAgent));
            if (Conversation?.Workspace.Path is { } lastPath) conversation.Workspace.Path = lastPath;
        }

        return conversation;
    }

    /// <summary>
    /// 新空会话继承上一个会话的角色。只认同档：切类型时旧会话的角色可能是另一档的
    /// （普通对话的扮演角色不能成为智能体的默认），对不上就回该类型的默认角色。
    /// 继承源优先取当前<b>展示会话实际用的角色</b>（切到已有会话时 NewSessionCharacterId
    /// 从未被装载逻辑更新，只读它会落回默认——见用户报告的「角色被重置」）；
    /// 还在空态（没建过会话）才看新建默认值
    /// </summary>
    /// <param name="type">新建会话的类型</param>
    /// <param name="fallback">该类型的默认角色</param>
    /// <returns>可继承的角色标识，或默认值</returns>
    private string InheritCharacterId(EConversationType type, string fallback)
    {
        string? prevId = Conversation?.CurrentMeta?.CharacterId ?? Conversation?.NewSessionCharacterId;
        if (prevId is not { } id) return fallback;
        CharacterData character = CharacterManager.Instance.GetCharacterData(id);
        bool fits = type == EConversationType.Chat ? character.IsChat() : character.IsAgent;
        return fits ? id : fallback;
    }

    protected override void OnConversationCreated(ConversationViewModel conversation)
    {
        conversation.SessionsChanged += SessionList.Sync;
        // SessionsChanged 是「会话获得/变更」的直接信号（EnsureSessionAsync 置完 CurrentMeta
        // 就触发）：空态判定在这里收口，比依赖 IsGenerating 等间接信号更稳——
        // 首轮发送在开跑前就失败这类路径也会走到
        conversation.SessionsChanged += OnAnyConversationSessionsChanged;
        conversation.PropertyChanged += OnConversationPropertyChanged;
        conversation.OpenSessionRequested += OnOpenSessionRequested;
    }

    protected override void OnConversationDiscarding(ConversationViewModel conversation)
    {
        conversation.SessionsChanged -= SessionList.Sync;
        conversation.SessionsChanged -= OnAnyConversationSessionsChanged;
        conversation.PropertyChanged -= OnConversationPropertyChanged;
        conversation.OpenSessionRequested -= OnOpenSessionRequested;
    }

    /// <summary>会话区请求切到某个会话（建完群）。走列表选中那一条路，与用户点一下完全一样</summary>
    private void OnOpenSessionRequested(string sessionId) => SessionList.SelectSession(sessionId);

    private void OnAnyConversationSessionsChanged()
    {
        // 任意实例（含后台）的集合变化都到这里。当前展示刚落定成真会话（首轮发送）时，
        // 详情栏要跟上：懒建不经列表选中，OnSelectionChanged 那条路收不到；
        // 搜到空时 Find 拿不到就保持原样——中间还在看它，详情栏不该清空
        if (Conversation?.CurrentMeta is { } meta
            && SessionList.Find(meta.SessionId) is { } item)
            ChatInfo.SetSession(item);
        RefreshRightPaneState();
    }

    /// <summary>右栏与空态相关属性集中刷新（单一通知点，避免散落）</summary>
    private void RefreshRightPaneState()
    {
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(IsChatWithSession));
        OnPropertyChanged(nameof(RightPaneKind));
    }

    private void OnConversationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ConversationViewModel.IsGenerating):
                // 详情栏讲的是「你正在看的这个会话」：后台会话的起止不该扰动它
                if (ReferenceEquals(sender, Conversation) && Conversation.IsGenerating) ChatInfo.NotifyChatBegin();
                // 首轮发送建会话后 CurrentMeta 才就位（CurrentMeta 自身不广播通知），
                // 空态判定要在此刻刷新，右栏才从新建卡切到「正在聊」
                if (ReferenceEquals(sender, Conversation)) RefreshRightPaneState();
                break;

            case nameof(ConversationViewModel.IsSessionLoading):
                // 装载完成时 CurrentMeta 才就位，空态判定此时才有最终答案
                if (ReferenceEquals(sender, Conversation) && !Conversation.IsSessionLoading)
                    RefreshRightPaneState();
                break;
        }
    }

    //================= 会话列表 =================

    private void OnSelectionChanged(SessionListItem? item)
    {
        SwitchConversation(item?.Meta);
        ChatInfo.SetSession(item);
        SubAgents.Refresh(); //换会话就换一套子代理索引
        if (Conversation.IsGenerating) ChatInfo.NotifyChatBegin(); // 切到后台跑着的会话时它就是“进行中”
        RefreshRightPaneState();
    }

    private void OnSessionMutated(SessionListItem item)
    {
        // 改名允许在跑的过程中进行,而重载会把界面条目清掉重新回放——正在流的那一轮会被拦腰截断
        if (FindConversation(item.Meta.SessionId) is not { IsGenerating: false } target) return;
        if (target == Conversation) _ = target.LoadSessionAsync(item.Meta);
    }

    private void OnSessionRemoved(SessionListItem item)
    {
        bool wasCurrent = Conversation.CurrentMeta?.SessionId == item.SessionId;
        DiscardConversation(item.SessionId); // 非当前会话缓存清理；删当前会话时是安全 no-op
        if (!wasCurrent) return;

        // 删掉当前会话后顺位选下一条（普通对话）或回空态（智能体）。两类口径保持原样
        if (CurrentType == EConversationType.Chat) SessionList.SelectFirstOrNone();
        else NewSession();
    }

    private void OpenSession(string sessionId)
    {
        if (SessionList.Find(sessionId) is { } item) SessionList.SelectedSession = item;
    }

    //================= 能力徽章跳转 =================

    [RelayCommand]
    private void ShowCapabilities()
    {
        SelectedSidePanelIndex = CapabilityTabIndex;
    }
}