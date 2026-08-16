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
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.SessionList;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Features.Conversation.Pages;

/// <summary>
/// 对话类页面壳的公共部分：会话列表、左右面板的宽度、开合与窄宽响应式收起。
/// 界面侧的公共部分是 <c>ConversationPageShell</c>，它按本类做编译期绑定。
/// </summary>
public abstract partial class ConversationPageDataBase : PageDataBase
{
    /// <summary>
    /// 本页的会话列表。两页只差<b>作用域</b>与<b>新会话要不要自动选中</b>——
    /// 后者正是急建与懒建的分野，所以由子类经构造参数说，事件接线也留在各子类
    /// </summary>
    public SessionListModel SessionList { get; }

    /// <param name="scope">会话列表作用域</param>
    /// <param name="selectNewSessions">新会话是否自动选中（急建为 true，懒建为 false）</param>
    protected ConversationPageDataBase(ESessionListScope scope, bool selectNewSessions)
    {
        SessionList = new SessionListModel(scope, selectNewSessions);
    }

    /// <summary>低于此宽度收起右栏</summary>
    private const double RightPaneCollapseWidth = 888;

    /// <summary>低于此宽度同时收起左右栏</summary>
    private const double BothPanesCollapseWidth = 666;

    /// <summary>侧栏可拖到的最窄宽度</summary>
    private const float MinPaneWidth = 120;

    /// <summary>侧栏可拖到的最宽宽度</summary>
    private const float MaxPaneWidth = 400;

    [ObservableProperty] private float _leftPaneWidth = 200;
    [ObservableProperty] private float _rightPaneWidth = 200;
    [ObservableProperty] private bool _isLeftPaneOpen = true;
    [ObservableProperty] private bool _isRightPaneOpen = true;

    /// <summary>
    /// 拖动左栏与右栏的分隔条。上下限放在这里而不是各页的 code-behind：
    /// 原先两页各写一份 clamp，于是同一个界面的两条边拖起来手感不一样
    /// </summary>
    /// <param name="deltaX">本次拖动的水平位移</param>
    public void DragLeftPane(double deltaX)
    {
        LeftPaneWidth = Math.Clamp(LeftPaneWidth + (float)deltaX, MinPaneWidth, MaxPaneWidth);
    }

    /// <summary>
    /// 拖动右栏的分隔条（右栏在分隔条左侧，位移取反）
    /// </summary>
    /// <param name="deltaX">本次拖动的水平位移</param>
    public void DragRightPane(double deltaX)
    {
        RightPaneWidth = Math.Clamp(RightPaneWidth - (float)deltaX, MinPaneWidth, MaxPaneWidth);
    }

    /// <summary>
    /// 窗口宽度响应：过窄时自动收起面板
    /// </summary>
    /// <param name="width">当前宽度</param>
    public void UpdateResponsiveState(double width)
    {
        if (width <= 0) return;
        if (width < BothPanesCollapseWidth)
        {
            IsLeftPaneOpen = false;
            IsRightPaneOpen = false;
        }
        else if (width < RightPaneCollapseWidth)
        {
            IsRightPaneOpen = false;
        }
    }

    [RelayCommand]
    private void ToggleLeftPane()
    {
        IsLeftPaneOpen = !IsLeftPaneOpen;
    }

    [RelayCommand]
    private void ToggleRightPane()
    {
        IsRightPaneOpen = !IsRightPaneOpen;
    }

    //================= 每会话一个视图模型 =================

    private readonly List<ConversationViewModel> _conversations = new(); //缓存,见 PruneConversations

    /// <summary>
    /// 当前展示的会话视图模型（<c>ConversationView</c> 的 DataContext）。
    /// 切会话是换实例，不是把同一个实例清空重填
    /// </summary>
    [ObservableProperty] private ConversationViewModel _conversation = null!;

    /// <summary>
    /// 造一个本页配置好的视图模型（新建会话用的默认角色、输入框占位文案等）
    /// </summary>
    /// <returns>新实例</returns>
    protected abstract ConversationViewModel CreateConversation();

    /// <summary>
    /// 新实例刚进缓存。页面壳在此挂它的事件（挂在<b>每个</b>实例上而不是只挂当前那个——
    /// 后台跑完的那一轮同样要刷新列表）
    /// </summary>
    /// <param name="conversation">新实例</param>
    protected virtual void OnConversationCreated(ConversationViewModel conversation)
    {
    }

    /// <summary>
    /// 实例即将被弃用，页面壳在此卸掉自己挂的事件
    /// </summary>
    /// <param name="conversation">将被弃用的实例</param>
    protected virtual void OnConversationDiscarding(ConversationViewModel conversation)
    {
    }

    /// <summary>
    /// 切到某会话：缓存里有就复用（后台还在跑的那一轮因此原样接回界面），
    /// 没有就新建并装载。<paramref name="meta"/> 为 null 即新会话空态。
    /// <b>切到当前正显示的那个是空操作</b>——包括"已经在空态时再点新建"。
    /// </summary>
    /// <param name="meta">会话元数据；null 为新会话空态</param>
    protected void SwitchConversation(ChatSessionMeta? meta)
    {
        // 要切去的就是当前正显示的那个:什么都不做。
        // 空态尤其要拦——meta 为 null 时下面的 FindConversation 无从匹配,于是"再点一次新建"
        // 会丢掉当前那个空实例、另建一个,整个视图的 DataContext 跟着换掉,右栏整块重建一次
        if (Conversation != null && Conversation.CurrentMeta?.SessionId == meta?.SessionId) return;

        ConversationViewModel? target = meta == null ? null : FindConversation(meta.SessionId);
        if (target == null)
        {
            target = CreateConversation();
            target.PropertyChanged += OnConversationStateChanged;
            _conversations.Add(target);
            OnConversationCreated(target);
            _ = target.LoadSessionAsync(meta);
        }

        Conversation = target;
        PruneConversations();
    }

    /// <summary>
    /// 找出承载某会话的实例（含还在后台跑的）
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>实例；没有则为 null</returns>
    protected ConversationViewModel? FindConversation(string sessionId) =>
        _conversations.FirstOrDefault(x => x.CurrentMeta?.SessionId == sessionId);

    /// <summary>
    /// 弃用承载某会话的实例（会话被删除时用）
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    protected void DiscardConversation(string sessionId)
    {
        if (FindConversation(sessionId) is not { } target || target == Conversation) return;
        Discard(target);
    }

    private void OnConversationStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 后台那一轮跑完,留着它的理由就没了。等到下次切会话再回收也行,
        // 但那意味着「一个已经跑完的长会话的全部气泡」会一直占着内存。
        //
        // 排到下一个循环再回收:这个通知是从运行循环的收尾里抛出来的,它后面还有几句
        // (SessionsChanged 通知、交接文档),当场把实例弃用会打断它自己的收尾
        if (e.PropertyName == nameof(ConversationViewModel.IsGenerating))
        {
            Dispatcher.UIThread.Post(PruneConversations);
        }
    }

    /// <summary>
    /// 回收缓存。留下的理由只有两个：正在展示，或还在跑（含卡在审批上——
    /// 那种情形 IsGenerating 仍为真，运行循环还等在审批的回应上）
    /// </summary>
    private void PruneConversations()
    {
        for (int i = _conversations.Count - 1; i >= 0; i--)
        {
            ConversationViewModel conversation = _conversations[i];
            if (conversation == Conversation || conversation.IsGenerating) continue;
            Discard(conversation);
        }
    }

    private void Discard(ConversationViewModel conversation)
    {
        _conversations.Remove(conversation);
        conversation.PropertyChanged -= OnConversationStateChanged;
        OnConversationDiscarding(conversation);
        conversation.Dispose();
    }
}
