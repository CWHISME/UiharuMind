/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Services;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Character;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// Agent 工作区页面壳(对齐 ChatPageData):面板开合/宽度、会话列表、右侧栏;
/// 会话内容由 ConversationViewModel + 通用 ConversationView 承载。
/// </summary>
public partial class AgentPageData : ConversationPageDataBase
{
    protected override Control CreateView => new AgentPage();

    public ObservableCollection<SessionListItem> Sessions { get; } = new();
    public ObservableCollection<ScheduledTaskDisplayItem> ScheduledTasks { get; } = new();

    [ObservableProperty] private SessionListItem? _selectedSession;

    private bool _suppressSelectionChange; //列表刷新期间抑制选择联动,避免 Clear() 误清界面

    public AgentPageData()
    {
        RefreshSessions();
        RefreshScheduledTasks();
        // 启动时恢复最近会话(历史加载不依赖模型状态)
        SwitchConversation(Sessions.Count > 0 ? Sessions[0].Meta : null);
        if (Sessions.Count > 0) SetSelectedWithoutLoad(Sessions[0]);
        // 会话可能由本页之外的动作产生(条目菜单里的"复制"、调度器新建),
        // 不订阅这个事件的话列表要等到下次发消息才刷新
        SessionManager.Instance.OnSessionAdded += OnSessionAdded;
        // 运行态变化来自后台线程(无头执行),列表项是界面绑定的,必须回到 UI 线程再改
        SessionManager.Instance.Running.StateChanged += id =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshRunState(id));
        CharacterRunnerFactory.Instance.Scheduler.OnTaskUpdated += _ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshScheduledTasks);
    }

    protected override ConversationViewModel CreateConversation() => new();

    protected override void OnConversationCreated(ConversationViewModel conversation)
    {
        conversation.SessionsChanged += RefreshSessions;
    }

    protected override void OnConversationDiscarding(ConversationViewModel conversation)
    {
        conversation.SessionsChanged -= RefreshSessions;
    }

    //================= 会话列表 =================

    partial void OnSelectedSessionChanged(SessionListItem? value)
    {
        if (_suppressSelectionChange) return;
        SwitchConversation(value?.Meta);
    }

    [RelayCommand]
    private void NewSession()
    {
        SetSelectedWithoutLoad(null);
        SwitchConversation(null);
    }

    private void RefreshRunState(string sessionId)
    {
        foreach (SessionListItem item in Sessions)
        {
            if (item.SessionId == sessionId) item.RefreshRunState();
        }
    }

    private void RefreshSessions()
    {
        // 刷新期间抑制选择联动:Clear() 会经 ListBox 双向绑定把 SelectedSession 置空,
        // 否则触发 LoadSessionAsync(null) 清空当前对话界面
        _suppressSelectionChange = true;
        try
        {
            //构造期的首次刷新早于视图模型就绪,那时没有"当前会话"可保持
            string? selectedId = Conversation?.CurrentMeta?.SessionId;
            Sessions.Clear();
            foreach (ChatSessionMeta meta in SessionManager.Instance.GetAgentSessions())
            {
                SessionListItem item = new(meta);
                item.Deleted += OnSessionDeleted;
                // 就地改写(改名/清空历史)命中当前会话时重载对话区
                item.Mutated += OnSessionMutated;
                Sessions.Add(item);
            }

            SetSelectedWithoutLoad(selectedId == null
                ? null
                : Sessions.FirstOrDefault(x => x.Meta.SessionId == selectedId));
        }
        finally
        {
            _suppressSelectionChange = false;
        }
    }

    /// <summary>
    /// 新会话入索引。只刷列表不改选中——<see cref="RefreshSessions"/> 走
    /// <see cref="SetSelectedWithoutLoad"/>，不会触发 LoadSessionAsync，
    /// 因此首轮发消息时新建会话的这条通知不会打断正在跑的那一轮。
    /// </summary>
    private void OnSessionAdded(ChatSession session)
    {
        // 智能体页只列智能体会话；聊天页那两档归 ChatListViewModel
        if (!session.CharacterData.Kind.IsAgent()) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshSessions);
    }

    private void OnSessionDeleted(SessionListItem item)
    {
        bool wasCurrent = Conversation.CurrentMeta?.SessionId == item.Meta.SessionId;
        DiscardConversation(item.Meta.SessionId);
        RefreshSessions();
        if (wasCurrent) NewSession();
    }

    private void OnSessionMutated(SessionListItem item)
    {
        // 改名允许在跑的过程中进行,而重载会把界面条目清掉重新回放——
        // 正在流的那一轮会被拦腰截断。标题由列表项自己刷新,这里让它跑完
        if (FindConversation(item.Meta.SessionId) is not { IsGenerating: false } target) return;
        if (target == Conversation) _ = target.LoadSessionAsync(item.Meta);
    }

    private void SetSelectedWithoutLoad(SessionListItem? item)
    {
        _suppressSelectionChange = true;
        try
        {
            SelectedSession = item;
        }
        finally
        {
            _suppressSelectionChange = false;
        }
    }

    //================= 定时任务 =================

    private void RefreshScheduledTasks()
    {
        ScheduledTasks.Clear();
        foreach (var task in CharacterRunnerFactory.Instance.Scheduler.GetTasks())
        {
            ScheduledTasks.Add(new ScheduledTaskDisplayItem(task));
        }
    }

    [RelayCommand]
    private async Task CancelTask(ScheduledTaskDisplayItem item)
    {
        await CharacterRunnerFactory.Instance.Scheduler.CancelAsync(item.TaskId);
    }

    [RelayCommand]
    private async Task RunTaskNow(ScheduledTaskDisplayItem item)
    {
        await CharacterRunnerFactory.Instance.Scheduler.RunNowAsync(item.TaskId);
    }

    [RelayCommand]
    private void OpenTaskSession(ScheduledTaskDisplayItem item)
    {
        if (string.IsNullOrEmpty(item.ResultSessionId)) return;
        RefreshSessions();
        SelectedSession = Sessions.FirstOrDefault(x => x.Meta.SessionId == item.ResultSessionId);
    }
}

/// <summary>
/// 定时任务侧栏显示项
/// </summary>
public class ScheduledTaskDisplayItem
{
    /// <summary>任务标识</summary>
    public string TaskId { get; }

    /// <summary>显示名</summary>
    public string DisplayName { get; }

    /// <summary>触发时间文本</summary>
    public string FireAtText { get; }

    /// <summary>状态文本</summary>
    public string StatusText { get; }

    /// <summary>结果会话标识</summary>
    public string? ResultSessionId { get; }

    /// <summary>是否可取消(待触发/已错过)</summary>
    public bool CanCancel { get; }

    /// <summary>是否可立即执行(待触发/已错过/失败)</summary>
    public bool CanRunNow { get; }

    /// <summary>是否有结果会话可打开</summary>
    public bool HasResultSession { get; }

    public ScheduledTaskDisplayItem(Core.AI.Execution.Scheduler.ScheduledAgentTask task)
    {
        TaskId = task.TaskId;
        DisplayName = task.DisplayName;
        FireAtText = task.FireAt.ToString("MM-dd HH:mm");
        StatusText = LocalizationManager.Instance.GetString($"AgentTaskStatus{task.Status}");
        ResultSessionId = task.ResultSessionId;
        CanCancel = task.Status is Core.AI.Execution.Scheduler.EScheduledTaskStatus.Pending
            or Core.AI.Execution.Scheduler.EScheduledTaskStatus.Missed;
        CanRunNow = task.Status is Core.AI.Execution.Scheduler.EScheduledTaskStatus.Pending
            or Core.AI.Execution.Scheduler.EScheduledTaskStatus.Missed
            or Core.AI.Execution.Scheduler.EScheduledTaskStatus.Failed;
        HasResultSession = !string.IsNullOrEmpty(task.ResultSessionId);
    }
}
