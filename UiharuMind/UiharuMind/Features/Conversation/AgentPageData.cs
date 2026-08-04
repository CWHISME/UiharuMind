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

    /// <summary>会话内容视图模型(ConversationView 的 DataContext)</summary>
    public ConversationViewModel Conversation { get; } = new();

    public ObservableCollection<SessionListItem> Sessions { get; } = new();
    public ObservableCollection<ScheduledTaskDisplayItem> ScheduledTasks { get; } = new();

    [ObservableProperty] private SessionListItem? _selectedSession;

    private bool _suppressSelectionChange; //列表刷新期间抑制选择联动,避免 Clear() 误清界面

    public AgentPageData()
    {
        RefreshSessions();
        RefreshScheduledTasks();
        // 启动时恢复最近会话(历史加载不依赖模型状态)
        if (Sessions.Count > 0) SelectedSession = Sessions[0];
        Conversation.SessionsChanged += RefreshSessions;
        CharacterRunnerFactory.Instance.Scheduler.OnTaskUpdated += _ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshScheduledTasks);
    }

    //================= 会话列表 =================

    partial void OnSelectedSessionChanged(SessionListItem? value)
    {
        if (_suppressSelectionChange) return;
        _ = Conversation.LoadSessionAsync(value?.Meta);
    }

    [RelayCommand]
    private async Task NewSession()
    {
        SetSelectedWithoutLoad(null);
        await Conversation.LoadSessionAsync(null);
    }

    private void RefreshSessions()
    {
        // 刷新期间抑制选择联动:Clear() 会经 ListBox 双向绑定把 SelectedSession 置空,
        // 否则触发 LoadSessionAsync(null) 清空当前对话界面
        _suppressSelectionChange = true;
        try
        {
            string? selectedId = Conversation.CurrentMeta?.SessionId;
            Sessions.Clear();
            foreach (ChatSessionMeta meta in SessionManager.Instance.GetSessions(ECharacterKind.Agent))
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

    private void OnSessionDeleted(SessionListItem item)
    {
        bool wasCurrent = Conversation.CurrentMeta?.SessionId == item.Meta.SessionId;
        RefreshSessions();
        if (wasCurrent) _ = NewSession();
    }

    private void OnSessionMutated(SessionListItem item)
    {
        if (Conversation.CurrentMeta?.SessionId == item.Meta.SessionId)
        {
            _ = Conversation.LoadSessionAsync(item.Meta);
        }
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
