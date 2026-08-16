/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Features.Conversation.SessionList;

namespace UiharuMind.Features.Conversation.Pages;

/// <summary>
/// Agent 工作区页面壳：面板开合/宽度（基类）、会话列表、定时任务侧栏。
/// 会话内容由 ConversationViewModel + 通用 ConversationView 承载。
/// </summary>
public partial class AgentPageData : ConversationPageDataBase
{
    protected override Control CreateView => new AgentPage();

    /// <summary>定时任务侧栏</summary>
    public ScheduledTaskListModel Scheduled { get; }

    /// <summary>
    /// 右栏页签的选中项。<b>显式绑定而不是由 TabControl 自己定</b>，两个原因：
    /// 工作区卡片上的能力徽章要能跳到「能力」页签（两个控件共用本 DataContext），
    /// 而 Todo 页签在角色关掉任务清单时整个隐藏——隐藏的页签会不会被自动跳过是框架行为，
    /// 不该赌。默认停在「能力」：它对智能体会话恒有内容，另两个是事件驱动的。
    /// </summary>
    [ObservableProperty] private int _selectedSidePanelIndex;

    /// <summary>「能力」页签在右栏里的下标（徽章跳转与默认值都指向它）</summary>
    public const int CapabilityTabIndex = 0;

    /// <summary>点能力徽章：跳到「能力」页签看明细</summary>
    [RelayCommand]
    private void ShowCapabilities()
    {
        SelectedSidePanelIndex = CapabilityTabIndex;
    }

    /// <summary>智能体页是<b>懒建</b>——新会话不自动选中，见 SessionListModel 的参数说明</summary>
    public AgentPageData() : base(ESessionListScope.Agent, selectNewSessions: false)
    {
        SessionList.SelectionChanged += OnSelectionChanged;
        SessionList.Mutated += OnSessionMutated;
        SessionList.Removed += OnSessionRemoved;

        Scheduled = new ScheduledTaskListModel(OpenSession);

        // 启动时恢复最近会话(历史加载不依赖模型状态)
        SessionListItem? first = SessionList.Sessions.Count > 0 ? SessionList.Sessions[0] : null;
        SwitchConversation(first?.Meta);
        SessionList.SelectWithoutNotifying(first);
    }

    protected override ConversationViewModel CreateConversation()
    {
        ConversationViewModel conversation = new();
        // 新开会话(空态)继承当前展示会话的工作目录:切页/新开会话不该把已选的路径重置掉。
        // 此处 Conversation 仍是旧实例(基类赋值在其后),只有空态没有 meta 可覆盖,故继承只对空态生效
        if (Conversation?.Workspace.Path is { } lastPath) conversation.Workspace.Path = lastPath;
        return conversation;
    }

    protected override void OnConversationCreated(ConversationViewModel conversation)
    {
        conversation.SessionsChanged += SessionList.Sync;
    }

    protected override void OnConversationDiscarding(ConversationViewModel conversation)
    {
        conversation.SessionsChanged -= SessionList.Sync;
    }

    //================= 会话列表 =================

    [RelayCommand]
    private void NewSession()
    {
        // 只切空态,不建会话:首轮发送时才入索引(懒建)
        SessionList.SelectWithoutNotifying(null);
        SwitchConversation(null);
    }

    private void OnSelectionChanged(SessionListItem? item) => SwitchConversation(item?.Meta);

    private void OnSessionMutated(SessionListItem item)
    {
        // 改名允许在跑的过程中进行,而重载会把界面条目清掉重新回放——
        // 正在流的那一轮会被拦腰截断。标题由列表项自己刷新,这里让它跑完
        if (FindConversation(item.Meta.SessionId) is not { IsGenerating: false } target) return;
        if (target == Conversation) _ = target.LoadSessionAsync(item.Meta);
    }

    private void OnSessionRemoved(SessionListItem item)
    {
        bool wasCurrent = Conversation.CurrentMeta?.SessionId == item.Meta.SessionId;
        DiscardConversation(item.Meta.SessionId);
        // 删掉当前会话后回空态,不顺位选下一条:智能体页的会话是懒建的,空态就是它的起点
        if (wasCurrent) NewSession();
    }

    private void OpenSession(string sessionId)
    {
        if (SessionList.Find(sessionId) is { } item) SessionList.SelectedSession = item;
    }
}
